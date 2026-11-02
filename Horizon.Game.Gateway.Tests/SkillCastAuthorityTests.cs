using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Horizon.Core.Abstract;
using Horizon.Game.Core.Combat;
using Horizon.Entities;
using Horizon.Game.Message.Combat;
using Horizon.Game.Message.Network;
using Horizon.Model.GameModel;
using Horizon.Orleans.Grains;
using Horizon.Orleans.Interface;
using Horizon.Orleans.Interface.World;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Orleans;
using Orleans.Runtime;
using Xunit;
using DamageType = Horizon.Orleans.Interface.World.DamageType;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 技能施放的权威口径测试：五行、威力与能量消耗一律以服务端技能模板为准。
/// <para>
/// 客户端上报通道打通之后，"客户端报什么服务端算什么"就不再是理论问题：
/// 报 <c>EnergyCost = 0</c> 等于无限施法，报别的元素就等于自选克制。本套件钉死这两处都由服务端说了算。
/// </para>
/// </summary>
public class SkillCastAuthorityTests
{
    private const int Metal = 1, Wood = 2, Fire = 4;

    private sealed class Harness
    {
        public CombatGrain Grain = null!;
        public CombatState State = null!;
        public Mock<ICharacterGrain> Character = null!;
        public int LastHpDelta;
        public DamageType LastDamageType;
    }

    private static CombatInfo Participant(ulong id, float attack = 100f, float energy = 1000f, int element = 0)
        => new CombatInfo
        {
            CharacterId = id,
            Health = 1000f,
            MaxHealth = 1000f,
            AttackPower = attack,
            Defense = 0f,
            WuxingElement = element,
            Energy = energy,
            MaxEnergy = energy,
            DodgeRate = 0f,
            BlockRate = 0f,
            CritRate = 0f,
            CritDamageMultiplier = 1f,
        };

    private static Harness CreateHarness(SkillTemplateEntity? template)
    {
        var caster = Participant(1, element: 0);
        var target = Participant(2, element: Metal);

        var state = new CombatState
        {
            CombatParticipants = new Dictionary<ulong, CombatInfo> { [1] = caster, [2] = target },
            ActiveEffects = new Dictionary<ulong, EffectInfo>(),
            CombatLog = new List<CombatLogEntry>(),
        };
        var mockState = new Mock<IPersistentState<CombatState>>();
        mockState.SetupGet(s => s.State).Returns(state);

        var h = new Harness();
        h.State = state;
        h.Character = new Mock<ICharacterGrain>();
        h.Character
            .Setup(c => c.RequestElementalHpChangeAsync(It.IsAny<int>(), It.IsAny<ulong>(), It.IsAny<DamageType>(),
                It.IsAny<int>(), It.IsAny<ElementStatVector>(), It.IsAny<ElementStatVector>()))
            .ReturnsAsync((int delta, ulong _, DamageType type, int __, ElementStatVector ___, ElementStatVector ____) =>
            {
                h.LastHpDelta = delta;
                h.LastDamageType = type;
                return new HpChangeResult(delta, 900, 1000, false, false);
            });

        var templateContext = new Mock<IDataContext<GameEntityContext, SkillTemplateEntity, int>>();
        templateContext
            .Setup(c => c.QueryFirstOrDefaultAsync(It.IsAny<Expression<Func<SkillTemplateEntity, bool>>>(), It.IsAny<bool>()))
            .ReturnsAsync(template!);

        var grain = (CombatGrain)System.Runtime.Serialization.FormatterServices
            .GetUninitializedObject(typeof(CombatGrain));
        GrainTestHarness.InjectField(grain, "_logger", NullLogger<CombatGrain>.Instance);
        GrainTestHarness.InjectField(grain, "_combatState", mockState.Object);
        GrainTestHarness.InjectField(grain, "_characterContext",
            new Mock<IDataContext<GameEntityContext, CharacterEntity, long>>().Object);
        GrainTestHarness.InjectField(grain, "_mapper", new Mock<AutoMapper.IMapper>().Object);
        GrainTestHarness.InjectField(grain, "_characterGrainProvider",
            new Func<ulong, ICharacterGrain>(_ => h.Character.Object));
        GrainTestHarness.InjectField(grain, "_skillTemplateContext", templateContext.Object);
        h.Grain = grain;
        return h;
    }

    [Fact]
    public async Task Energy_cost_comes_from_the_server_template_not_the_client_report()
    {
        var h = CreateHarness(new SkillTemplateEntity
        {
            Id = 4001, WuxingElement = Fire, EnergyCostFormula = "250",
        });

        // 客户端报 0：模板说 250 就要扣 250
        await h.Grain.ProcessSkillCastAsync(new SkillCastMessage
        {
            CasterId = 1, SkillId = 4001, TargetEntityId = 2, EnergyCost = 0f,
        });

        var caster = h.State.CombatParticipants[1];
        Assert.Equal(1000f - 250f, caster.Energy);
    }

    [Fact]
    public async Task Cast_is_refused_when_the_server_cost_exceeds_the_pool_even_if_the_client_reports_zero()
    {
        var h = CreateHarness(new SkillTemplateEntity { Id = 4001, EnergyCostFormula = "5000" });
        var caster = h.State.CombatParticipants[1];
        caster.Energy = 100f;

        var response = await h.Grain.ProcessSkillCastAsync(new SkillCastMessage
        {
            CasterId = 1, SkillId = 4001, TargetEntityId = 2, EnergyCost = 0f,
        });

        Assert.False(response.Success);
        Assert.Equal(100f, caster.Energy);          // 没扣也没放行
        Assert.Equal(0, h.LastHpDelta);            // 也没有任何伤害回写
    }

    [Fact]
    public async Task Skill_template_element_decides_the_row_and_the_counter_multiplier()
    {
        // 守方命行金；火系技能（模板 wuxing_element=4）→ 火克金 ×1.25
        var h = CreateHarness(new SkillTemplateEntity
        {
            Id = 4001, WuxingElement = Fire, EnergyCostFormula = "30",
        });

        await h.Grain.ProcessSkillCastAsync(new SkillCastMessage
        {
            CasterId = 1, SkillId = 4001, TargetEntityId = 2, EnergyCost = 30f,
        });

        // 通用 (100×0.5 + 技能基础 100×0.6) = 110 → ×1.25 = 137.5 → 取整 137
        Assert.Equal(-137, h.LastHpDelta);
        Assert.Equal(DamageType.Internal, h.LastDamageType);
    }

    [Fact]
    public async Task Unknown_template_falls_back_to_the_casters_own_element_unchanged()
    {
        // 模板查不到（内容表未编辑该技能）：按施法者命行结算，此时命行为 0 → 无属性、倍率 1.0
        var h = CreateHarness(null);

        await h.Grain.ProcessSkillCastAsync(new SkillCastMessage
        {
            CasterId = 1, SkillId = 9999, TargetEntityId = 2, EnergyCost = 30f,
        });

        Assert.Equal(-110, h.LastHpDelta);
    }

    [Fact]
    public async Task A_real_formula_still_falls_back_to_the_reported_cost_instead_of_failing_open()
    {
        // energy_cost_formula 是公式列：填了非常量表达式时不猜，退回客户端报的值（与接入前一致），
        // 也绝不因为解析失败就把消耗当成 0。
        var h = CreateHarness(new SkillTemplateEntity
        {
            Id = 4001, WuxingElement = Fire, EnergyCostFormula = "base * level * 1.1",
        });
        var caster = h.State.CombatParticipants[1];

        await h.Grain.ProcessSkillCastAsync(new SkillCastMessage
        {
            CasterId = 1, SkillId = 4001, TargetEntityId = 2, EnergyCost = 40f,
        });

        Assert.Equal(960f, caster.Energy);
    }

    // ── 内力池：与客户端同基值，且没有 tick 也能回复 ───────────────

    [Fact]
    public void Server_energy_pool_shares_the_client_base_value()
    {
        // 客户端 CharacterAttributesComponent：_maxEnergyBase = 1000、EnergyRecoveryRate = 10/秒。
        // 服务端以前写死 100 + 派生，差一个数量级：由服务端裁决消耗后就会"放几次技能就永久卡住"。
        Assert.Equal(1000f, AttributeDerivation.EnergyBaseValue);
        Assert.Equal(10f, AttributeDerivation.EnergyRecoveryPerSecond);
    }

    [Fact]
    public async Task Energy_refills_with_elapsed_time_without_any_tick()
    {
        var h = CreateHarness(new SkillTemplateEntity { Id = 4001, EnergyCostFormula = "30" });
        var caster = h.State.CombatParticipants[1];
        caster.Energy = 0f;
        caster.MaxEnergy = 1000f;
        // 5 秒前刚结算过回复：10/秒 → 补到 50，扣掉 30 还剩 20
        caster.LastEnergyRegenUtc = DateTime.UtcNow.AddSeconds(-5);

        var response = await h.Grain.ProcessSkillCastAsync(new SkillCastMessage
        {
            CasterId = 1, SkillId = 4001, TargetEntityId = 2, EnergyCost = 0f,
        });

        Assert.True(response.Success);
        Assert.Equal(20f, caster.Energy, 0);
    }

    [Fact]
    public async Task An_unknown_regen_timestamp_does_not_backfill_the_pool_from_the_epoch()
    {
        // 旧状态反序列化出来 LastEnergyRegenUtc 是 default；按"从 0001-01-01 起经过的时间"补算
        // 等于重启瞬间把空池白送满，这里必须只记时刻、不补值。
        var h = CreateHarness(new SkillTemplateEntity { Id = 4001, EnergyCostFormula = "30" });
        var caster = h.State.CombatParticipants[1];
        caster.Energy = 0f;
        caster.MaxEnergy = 1000f;
        caster.LastEnergyRegenUtc = default;

        var response = await h.Grain.ProcessSkillCastAsync(new SkillCastMessage
        {
            CasterId = 1, SkillId = 4001, TargetEntityId = 2, EnergyCost = 0f,
        });

        Assert.False(response.Success);
        Assert.Equal(0f, caster.Energy);
        Assert.NotEqual(default, caster.LastEnergyRegenUtc);
    }
}

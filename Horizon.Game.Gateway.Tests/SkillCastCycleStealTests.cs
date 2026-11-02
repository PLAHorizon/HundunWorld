using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Horizon.Core.Abstract;
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

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 破势夺元的编排侧测试（"两人互相打断周天"这条玩法的接线本身）。
/// <para>
/// 判定与状态都各自有测试了（<c>BreakCycleOnlyLandsOnTheChosenGuardRow</c> 等钉 SkillGrain 那一侧），
/// 但"施放时到底有没有去破、破成才给攻方记一层"一直没有断言：这段代码走的是 <c>GrainFactory</c>，
/// 在脚手架里会被 catch 静默吞成 false，所以哪怕整条编排写反（比如不分成败都赠一层）也不会红。
/// 为此 CombatGrain 留了一个可注入的 SkillGrain provider，本套件就是钉它。
/// </para>
/// </summary>
public class SkillCastCycleStealTests
{
    private const int Metal = 1, Wood = 2, Fire = 4;

    private sealed class Harness
    {
        public CombatGrain Grain = null!;
        public CombatState State = null!;
        public Mock<ISkillGrain> Victim = null!;      // 守方（被破的那一侧）
        public Mock<ISkillGrain> Taker = null!;       // 攻方（夺元后记层的那一侧）
        public int LastHpDelta;
    }

    private static Harness Create(SkillTemplateEntity template, ElementStatVector defenderDefense)
    {
        var caster = new CombatInfo
        {
            CharacterId = 1, Health = 1000f, MaxHealth = 1000f, AttackPower = 100f,
            WuxingElement = Fire,                     // 故意与模板五行不同：以模板为准才算权威
            Energy = 1000f, MaxEnergy = 1000f,
        };
        var target = new CombatInfo
        {
            CharacterId = 2, Health = 1000f, MaxHealth = 1000f, AttackPower = 0f,
            WuxingElement = Wood, ElementDefense = defenderDefense,   // 只是缓存字段：真正的护体行由来源与 Buff 合成（见下方断言注释）
            Energy = 1000f, MaxEnergy = 1000f,
        };

        var state = new CombatState
        {
            CombatParticipants = new Dictionary<ulong, CombatInfo> { [1] = caster, [2] = target },
            ActiveEffects = new Dictionary<ulong, EffectInfo>(),
            CombatLog = new List<CombatLogEntry>(),
        };
        var persistent = new Mock<IPersistentState<CombatState>>();
        persistent.SetupGet(s => s.State).Returns(state);

        var character = new Mock<ICharacterGrain>();
        character
            .Setup(c => c.RequestElementalHpChangeAsync(It.IsAny<int>(), It.IsAny<ulong>(),
                It.IsAny<Horizon.Orleans.Interface.World.DamageType>(), It.IsAny<int>(),
                It.IsAny<ElementStatVector>(), It.IsAny<ElementStatVector>()))
            .ReturnsAsync((int delta, ulong _, Horizon.Orleans.Interface.World.DamageType __, int ___,
                ElementStatVector ____, ElementStatVector _____) =>
            {
                return new HpChangeResult(delta, 900, 1000, false, false);
            });

        var templateContext = new Mock<IDataContext<GameEntityContext, SkillTemplateEntity, int>>();
        templateContext
            .Setup(c => c.QueryFirstOrDefaultAsync(It.IsAny<Expression<Func<SkillTemplateEntity, bool>>>(), It.IsAny<bool>()))
            .ReturnsAsync(template);

        var h = new Harness
        {
            State = state,
            Victim = new Mock<ISkillGrain>(),
            Taker = new Mock<ISkillGrain>(),
        };

        var grain = (CombatGrain)System.Runtime.Serialization.FormatterServices
            .GetUninitializedObject(typeof(CombatGrain));
        GrainTestHarness.InjectField(grain, "_logger", NullLogger<CombatGrain>.Instance);
        GrainTestHarness.InjectField(grain, "_combatState", persistent.Object);
        GrainTestHarness.InjectField(grain, "_characterContext",
            new Mock<IDataContext<GameEntityContext, CharacterEntity, long>>().Object);
        GrainTestHarness.InjectField(grain, "_mapper", new Mock<AutoMapper.IMapper>().Object);
        GrainTestHarness.InjectField(grain, "_characterGrainProvider",
            new Func<ulong, ICharacterGrain>(_ => character.Object));
        GrainTestHarness.InjectField(grain, "_skillTemplateContext", templateContext.Object);
        GrainTestHarness.InjectField(grain, "_skillGrainProvider", new Func<ulong, ISkillGrain>(id =>
        {
            if (id == 2) return h.Victim.Object;
            if (id == 1) return h.Taker.Object;
            throw new InvalidOperationException($"不该去碰第三个角色 {id}");
        }));
        h.Grain = grain;
        return h;
    }

    private static Task<SkillCastMessage> Cast(Harness h) => h.Grain.ProcessSkillCastAsync(new SkillCastMessage
    {
        CasterId = 1, SkillId = 4001, TargetEntityId = 2, EnergyCost = 0f,
    });

    [Fact]
    public async Task Break_is_asked_with_the_template_element_and_the_computed_guard_row()
    {
        // 模板是金行，守方防御最高行是木 → 金克木成立，攻方应被记一层
        var h = Create(
            new SkillTemplateEntity { Id = 4001, WuxingElement = Metal, EnergyCostFormula = "0" },
            new ElementStatVector().WithRowAdded(Wood, 50f));
        int askedAttack = 0, askedDefenseRow = 0;
        h.Victim.Setup(s => s.BreakCycleAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync((int attack, int row) => { askedAttack = attack; askedDefenseRow = row; return true; });

        var response = await Cast(h);

        Assert.True(response.Success);
        Assert.Equal(Metal, askedAttack);            // 取服务端模板，不取施法者自身行（它是火）、也不取客户端上报
        // 交出去的守方护体行是 CombatGrain 那份兜底口径的<b>计算结果</b>：它由元素来源与 Buff 合成
        // （RefreshElementVectors），这个脚手架没给守方任何来源，所以结果就是 0 = "没有可自动判定的行"。
        // 断这里是为了钉住"传的是算出来的值、不是随手一个常量"；至于"镇岳指定行必须压过自动行"，
        // 归 SkillGrain 判，且已在真集群上验过（见方案 §14.7 的 G3）。
        Assert.Equal(0, askedDefenseRow);
        h.Taker.Verify(s => s.GrantCycleAsync(), Times.Once);
    }

    [Fact]
    public async Task Nothing_is_granted_to_the_attacker_when_the_guard_holds()
    {
        // 破不开就不能顺手给自己记一层：否则"破势"退化成双方各涨一层的互赠
        var h = Create(
            new SkillTemplateEntity { Id = 4001, WuxingElement = Metal, EnergyCostFormula = "0" },
            new ElementStatVector().WithRowAdded(Fire, 50f));
        h.Victim.Setup(s => s.BreakCycleAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(false);

        var response = await Cast(h);

        Assert.True(response.Success);               // 伤害照打，破不破是额外战果
        h.Taker.Verify(s => s.GrantCycleAsync(), Times.Never);
    }

    [Fact]
    public async Task A_failing_steal_never_rolls_back_the_damage_already_computed()
    {
        var h = Create(
            new SkillTemplateEntity { Id = 4001, WuxingElement = Metal, EnergyCostFormula = "0" },
            new ElementStatVector().WithRowAdded(Wood, 50f));
        h.Victim.Setup(s => s.BreakCycleAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ThrowsAsync(new InvalidOperationException("守方 Grain 不可达"));

        var response = await Cast(h);

        Assert.True(response.Success);
        h.Victim.Verify(s => s.BreakCycleAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Once);
        h.Taker.Verify(s => s.GrantCycleAsync(), Times.Never);
    }
}

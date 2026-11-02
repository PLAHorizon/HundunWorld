using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Horizon.Game.Message.Combat;
using Horizon.Game.Message.Network;
using Horizon.Orleans.Grains;
using Horizon.Orleans.Interface;
using Horizon.Orleans.Interface.World;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Orleans;
using Orleans.Runtime;
using Xunit;
using DamageType = Horizon.Orleans.Interface.World.DamageType;
using CharacterState = Horizon.Game.Message.Network.CharacterState;
using EffectType = Horizon.Game.Message.Enums.EffectType;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// Buff 的五行加成参与服务端权威结算的契约测试。
/// <para>
/// 三段链路各自钉死：<c>CombatGrain.ApplyEffectAsync</c> 写入五行载荷并按 (目标,效果) 收敛；
/// 结算前把未过期 Buff 并入攻方三行、到期即失效并清理；
/// <c>CharacterGrain.RequestElementalHpChangeAsync</c> 用"已高者计"把守方 Buff 防御折进减免。
/// </para>
/// </summary>
public class ElementalBuffAuthorityContractTests
{
    private const int Metal = 1, Wood = 2;

    private static ulong EffectKeyOf(ulong targetId, int effectId)
        => unchecked(targetId * 1000003ul + (uint)effectId);

    private sealed class Harness
    {
        public CombatGrain Grain = null!;
        public CombatState State = null!;
        public Mock<ICharacterGrain> Character = null!;
        public int LastHpDelta;
    }

    private static CombatInfo Participant(ulong id, float attack = 100f, int element = 0)
        => new CombatInfo
        {
            CharacterId = id,
            Health = 1000f,
            MaxHealth = 1000f,
            AttackPower = attack,
            Defense = 0f,
            WuxingElement = element,
            Energy = 100f,
            MaxEnergy = 100f,
            DodgeRate = 0f,
            BlockRate = 0f,
            CritRate = 0f,
            CritDamageMultiplier = 1f,
        };

    /// <summary>裸对象 + 反射注入：参与者预先塞好，绕开数据库与"表未迁移"的降级分支。</summary>
    private static Harness CreateCombatGrain(CombatInfo attacker, CombatInfo target, params EffectInfo[] effects)
    {
        var state = new CombatState
        {
            CombatParticipants = new Dictionary<ulong, CombatInfo>(),
            ActiveEffects = new Dictionary<ulong, EffectInfo>(),
            CombatLog = new List<CombatLogEntry>(),
        };
        state.CombatParticipants[attacker.CharacterId] = attacker;
        state.CombatParticipants[target.CharacterId] = target;
        foreach (var e in effects) state.ActiveEffects[EffectKeyOf(e.TargetId, e.EffectId)] = e;

        var mockState = new Mock<IPersistentState<CombatState>>();
        mockState.SetupGet(s => s.State).Returns(state);

        var harness = new Harness { State = state };
        harness.Character = new Mock<ICharacterGrain>();
        harness.Character
            .Setup(c => c.RequestElementalHpChangeAsync(It.IsAny<int>(), It.IsAny<ulong>(),
                It.IsAny<DamageType>(), It.IsAny<int>(), It.IsAny<ElementStatVector>(), It.IsAny<ElementStatVector>()))
            .ReturnsAsync((int delta, ulong _, DamageType _, int __, ElementStatVector ___, ElementStatVector ____) =>
            {
                harness.LastHpDelta = delta;
                return new HpChangeResult(delta, 900, 1000, false, false);
            });

        var grain = (CombatGrain)System.Runtime.Serialization.FormatterServices
            .GetUninitializedObject(typeof(CombatGrain));
        GrainTestHarness.InjectField(grain, "_logger", NullLogger<CombatGrain>.Instance);
        GrainTestHarness.InjectField(grain, "_combatState", mockState.Object);
        GrainTestHarness.InjectField(grain, "_characterContext",
            new Mock<Horizon.Core.Abstract.IDataContext<Horizon.Entities.GameEntityContext, Horizon.Model.GameModel.CharacterEntity, long>>().Object);
        GrainTestHarness.InjectField(grain, "_mapper", new Mock<AutoMapper.IMapper>().Object);
        GrainTestHarness.InjectField(grain, "_characterGrainProvider",
            new Func<ulong, ICharacterGrain>(_ => harness.Character.Object));
        harness.Grain = grain;
        return harness;
    }

    // ── 攻方：Buff 的五行攻击进入伤害基数 ───────────────────────────

    [Fact]
    public async Task Attack_buff_row_is_added_to_the_damage_base_before_the_element_multiplier()
    {
        var h = CreateCombatGrain(Participant(1, element: Metal), Participant(2, element: Wood));

        // 金系攻击 Buff +60；金克木 ×1.25。
        await h.Grain.ApplyEffectAsync(new EffectMessage
        {
            EffectId = 7001,
            TargetId = 1,
            SourceId = 1,
            Duration = 10f,
            EffectType = (int)EffectType.Strength,
            AttackBoost = new ElementStatVector(60f, 0f, 0f, 0f, 0f),
        });

        await h.Grain.ProcessAttackAsync(new AttackMessage
        {
            AttackerId = 1, TargetId = 2, Damage = 50, ElementType = Metal,
        });

        // (通用 100×0.5 + 上报 50 + 五行攻击 60) × 1.25 = 200 → 回写 -200
        Assert.Equal(-200, h.LastHpDelta);
    }

    [Fact]
    public async Task Expired_buff_drops_out_of_the_settlement_without_any_tick()
    {
        // AppliedAtUtc 在 30 秒前、Duration=1：读取时即判定过期（没有任何 tick 递减 RemainingDuration）。
        var stale = new EffectInfo
        {
            EffectId = 7001,
            EffectName = "过期金攻",
            TargetId = 1,
            SourceId = 1,
            Duration = 1f,
            RemainingDuration = 1f,
            AppliedAtUtc = DateTime.UtcNow.AddSeconds(-30),
            AttackBoost = new ElementStatVector(60f, 0f, 0f, 0f, 0f),
        };
        var h = CreateCombatGrain(Participant(1, element: Metal), Participant(2, element: Wood), stale);

        await h.Grain.ProcessAttackAsync(new AttackMessage
        {
            AttackerId = 1, TargetId = 2, Damage = 50, ElementType = Metal,
        });

        // 没有 Buff 加成：(50 + 50) × 1.25 = 125
        Assert.Equal(-125, h.LastHpDelta);
        Assert.Empty(h.State.ActiveEffects);   // 到期即清，不留永久堆积
    }

    [Fact]
    public async Task Effect_without_an_applied_timestamp_is_treated_as_permanent_not_expired()
    {
        // 旧状态反序列化出来 AppliedAtUtc 是默认值；按过期处理会让重启后 Buff 凭空蒸发。
        var legacy = new EffectInfo
        {
            EffectId = 7002, TargetId = 1, Duration = 5f,
            AttackBoost = new ElementStatVector(60f, 0f, 0f, 0f, 0f),
        };
        var h = CreateCombatGrain(Participant(1, element: Metal), Participant(2, element: Wood), legacy);

        await h.Grain.ProcessAttackAsync(new AttackMessage
        {
            AttackerId = 1, TargetId = 2, Damage = 50, ElementType = Metal,
        });

        Assert.Equal(-200, h.LastHpDelta);
        Assert.Single(h.State.ActiveEffects);
    }

    [Fact]
    public async Task Same_effect_reapplied_refreshes_instead_of_stacking_copies()
    {
        var h = CreateCombatGrain(Participant(1, element: Metal), Participant(2, element: Wood));
        var request = new EffectMessage
        {
            EffectId = 7001, TargetId = 1, SourceId = 1, Duration = 10f,
            AttackBoost = new ElementStatVector(60f, 0f, 0f, 0f, 0f),
        };

        await h.Grain.ApplyEffectAsync(request);
        await h.Grain.ApplyEffectAsync(request);

        await h.Grain.ProcessAttackAsync(new AttackMessage
        {
            AttackerId = 1, TargetId = 2, Damage = 50, ElementType = Metal,
        });

        // 仍是 +60 而不是 +120：同一 (目标,效果) 恒定落在同一条记录上
        Assert.Equal(-200, h.LastHpDelta);
        Assert.Single(h.State.ActiveEffects);
    }

    [Fact]
    public async Task Two_attack_buffs_stack_because_attack_aggregates_by_sum()
    {
        var now = DateTime.UtcNow;
        var h = CreateCombatGrain(Participant(1, element: Metal), Participant(2, element: 0),
            new EffectInfo
            {
                EffectId = 1, TargetId = 1, Duration = 10f, AppliedAtUtc = now,
                AttackBoost = new ElementStatVector(40f, 0f, 0f, 0f, 0f),
            },
            new EffectInfo
            {
                EffectId = 2, TargetId = 1, Duration = 10f, AppliedAtUtc = now,
                AttackBoost = new ElementStatVector(25f, 0f, 0f, 0f, 0f),
            });

        await h.Grain.ProcessAttackAsync(new AttackMessage
        {
            AttackerId = 1, TargetId = 2, Damage = 50, ElementType = Metal,
        });

        // (50 + 50 + 40 + 25) × 1.0 = 165
        Assert.Equal(-165, h.LastHpDelta);
    }

    // ── 守方：Buff 防御按"已高者计"折进减免 ─────────────────────────

    private static CharacterGrain CreateCharacterGrain(float defense)
    {
        var info = new CharacterInfo
        {
            CharacterId = 42,
            CurrentHealth = 1000f,
            MaxHealth = 1000f,
            Defense = defense,
            IsAlive = true,
        };
        var state = new CharacterState { CharacterInfo = info };
        var mockState = new Mock<IPersistentState<CharacterState>>();
        mockState.SetupGet(s => s.State).Returns(state);

        var grain = (CharacterGrain)System.Runtime.Serialization.FormatterServices
            .GetUninitializedObject(typeof(CharacterGrain));
        GrainTestHarness.InjectField(grain, "_logger", NullLogger<CharacterGrain>.Instance);
        GrainTestHarness.InjectField(grain, "_characterState", mockState.Object);
        GrainTestHarness.InjectField(grain, "_currentZoneShardId", 0L);
        return grain;
    }

    [Fact]
    public async Task Defender_buff_defense_takes_effect_through_the_same_highest_row_rule()
    {
        var grain = CreateCharacterGrain(defense: 100f);

        // 通用 100 + Buff 金防 500 = 600 → 减免 600/700 → -100 × (1/7) = -14.28… → -14
        var result = await grain.RequestElementalHpChangeAsync(
            -100, sourceId: 1, DamageType.Physical, attackElement: Metal,
            attackerPenetration: ElementStatVector.Zero,
            defenderBuffDefense: new ElementStatVector(500f, 200f, 0f, 0f, 0f));

        Assert.Equal(-14, result.ActualDelta);
    }

    [Fact]
    public async Task Attacker_penetration_offsets_the_defender_buff_defense_too()
    {
        var grain = CreateCharacterGrain(defense: 100f);

        // 金防 100+500 被火系破防 600 抵光 → 有效防御 0 → 全额伤害
        var result = await grain.RequestElementalHpChangeAsync(
            -100, sourceId: 1, DamageType.Physical, attackElement: Metal,
            attackerPenetration: new ElementStatVector(0f, 0f, 0f, 600f, 0f),
            defenderBuffDefense: new ElementStatVector(500f, 0f, 0f, 0f, 0f));

        Assert.Equal(-100, result.ActualDelta);
    }

    [Fact]
    public async Task Buff_defense_on_an_otherwise_row_does_not_protect_this_attack()
    {
        var grain = CreateCharacterGrain(defense: 100f);

        // 守方 Buff 只加木防，金攻走金行 → 与没有该 Buff 时逐值一致
        var withIrrelevant = await grain.RequestElementalHpChangeAsync(
            -100, sourceId: 1, DamageType.Physical, attackElement: Metal,
            attackerPenetration: ElementStatVector.Zero,
            defenderBuffDefense: new ElementStatVector(0f, 500f, 0f, 0f, 0f));
        var withoutBuff = await grain.RequestElementalHpChangeAsync(
            -100, sourceId: 1, DamageType.Physical, attackElement: Metal,
            attackerPenetration: ElementStatVector.Zero,
            defenderBuffDefense: ElementStatVector.Zero);

        Assert.Equal(withoutBuff.ActualDelta, withIrrelevant.ActualDelta);
        Assert.Equal(-50, withIrrelevant.ActualDelta);   // 通用 100 → 50% 减免
    }

    [Fact]
    public async Task Unattributed_attack_faces_the_highest_defense_row_including_buffs()
    {
        var grain = CreateCharacterGrain(defense: 100f);

        // attackElement=0：最高防御行是水行 900（Buff 带来），减免按它算
        var result = await grain.RequestElementalHpChangeAsync(
            -200, sourceId: 1, DamageType.Physical, attackElement: 0,
            attackerPenetration: ElementStatVector.Zero,
            defenderBuffDefense: new ElementStatVector(10f, 20f, 900f, 0f, 5f));

        // 有效防御 = 100 + 900 = 1000 → 减免 1000/1100 → -200 × (1/11) = -18.18… → -18
        Assert.Equal(-18, result.ActualDelta);
    }

    [Fact]
    public async Task True_damage_ignores_buff_defense_entirely()
    {
        var grain = CreateCharacterGrain(defense: 100f);

        var result = await grain.RequestElementalHpChangeAsync(
            -100, sourceId: 1, DamageType.True, attackElement: Metal,
            attackerPenetration: ElementStatVector.Zero,
            defenderBuffDefense: new ElementStatVector(500f, 0f, 0f, 0f, 0f));

        Assert.Equal(-100, result.ActualDelta);
    }
}

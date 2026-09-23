using System;
using System.Threading.Tasks;
using Horizon.Game.Message.Network;
using Horizon.Orleans.Grains;
using Horizon.Orleans.Interface.World;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Orleans;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 阶段 3.1 Phase 2 — HP 变更裁决契约测试。
/// <para>
/// 验证 <see cref="ICharacterGrain.RequestHpChangeAsync"/> 作为 HP 变更链路的唯一裁决点：
/// 按 <see cref="DamageType"/> 应用防御减免（True 无视防御）、叠加减伤 Buff
/// （<c>CharacterInfo.DamageReduction</c>）、治疗不减免、致死标记死亡、完全抵抗返回
/// <see cref="HpChangeResult.IsResisted"/>。
/// </para>
/// <para>
/// 对应 REFACTORING_TASK_LIST 3.1 遗留项（原 TODO Phase 2：防御/减伤/Buff 裁决直通）。
/// </para>
/// </summary>
public class HpAdjudicationContractTests
{
    /// <summary>构造 CharacterGrain（无 GrainContext 裸构造 + 反射注入裁决所需字段）。</summary>
    private static (CharacterGrain Grain, CharacterInfo Info) CreateGrain(
        float currentHp, float maxHp = 1000f, float defense = 0f, float damageReduction = 0f)
    {
        var info = new CharacterInfo
        {
            CharacterId = 42,
            CurrentHealth = currentHp,
            MaxHealth = maxHp,
            Defense = defense,
            DamageReduction = damageReduction,
            IsAlive = true,
        };
        var state = new CharacterState { CharacterInfo = info };

        var mockState = new Mock<IPersistentState<CharacterState>>();
        mockState.SetupGet(s => s.State).Returns(state);

        // CharacterGrain : Grain<CharacterState> 基类 ctor 在无 GrainContext 时抛 NRE（Orleans 10），
        // 用共享助手无构造创建 + 反射注入字段（GrainTestHarness 统一封装）。
        var grain = (CharacterGrain)System.Runtime.Serialization.FormatterServices
            .GetUninitializedObject(typeof(CharacterGrain));
        GrainTestHarness.InjectField(grain, "_logger", NullLogger<CharacterGrain>.Instance);
        GrainTestHarness.InjectField(grain, "_characterState", mockState.Object);
        // _currentZoneShardId = 0：跳过 ZoneShard 推送（GrainFactory 在无 GrainContext 时不可用）。
        GrainTestHarness.InjectField(grain, "_currentZoneShardId", 0L);

        return (grain, info);
    }

    [Fact]
    public async Task PhysicalDamage_AppliesDefenseReduction()
    {
        // Defense=100 → 减免 50%：-100 → -50。
        var (grain, info) = CreateGrain(currentHp: 500f, defense: 100f);

        var result = await grain.RequestHpChangeAsync(-100, sourceId: 1, DamageType.Physical);

        Assert.Equal(-50, result.ActualDelta);
        Assert.Equal(450, result.CurrentHp);
        Assert.False(result.IsDead);
        Assert.False(result.IsResisted);
        Assert.Equal(450f, info.CurrentHealth);
    }

    [Fact]
    public async Task TrueDamage_IgnoresDefense()
    {
        // True 伤害无视防御：-100 全额生效。
        var (grain, info) = CreateGrain(currentHp: 500f, defense: 100f);

        var result = await grain.RequestHpChangeAsync(-100, sourceId: 1, DamageType.True);

        Assert.Equal(-100, result.ActualDelta);
        Assert.Equal(400, result.CurrentHp);
        Assert.False(result.IsDead);
        Assert.False(result.IsResisted);
    }

    [Fact]
    public async Task Healing_IsNotReducedByDefense()
    {
        var (grain, info) = CreateGrain(currentHp: 500f, defense: 100f);

        var result = await grain.RequestHpChangeAsync(+30, sourceId: 1, DamageType.Healing);

        Assert.Equal(30, result.ActualDelta);
        Assert.Equal(530, result.CurrentHp);
        Assert.False(result.IsDead);
        Assert.False(result.IsResisted);
    }

    [Fact]
    public async Task DamageReductionBuff_StacksWithDefense()
    {
        // Defense=100（-50%）+ 减伤 Buff 0.5（再 -50%）：-100 → -50 → -25。
        var (grain, info) = CreateGrain(currentHp: 500f, defense: 100f, damageReduction: 0.5f);

        var result = await grain.RequestHpChangeAsync(-100, sourceId: 1, DamageType.Physical);

        Assert.Equal(-25, result.ActualDelta);
        Assert.Equal(475, result.CurrentHp);
        Assert.False(result.IsDead);
        Assert.False(result.IsResisted);
    }

    [Fact]
    public async Task LethalDamage_ClampsToZero_AndMarksDead()
    {
        var (grain, info) = CreateGrain(currentHp: 30f, defense: 0f);

        var result = await grain.RequestHpChangeAsync(-100, sourceId: 1, DamageType.Physical);

        Assert.Equal(-30, result.ActualDelta);
        Assert.Equal(0, result.CurrentHp);
        Assert.True(result.IsDead);
        Assert.Equal(0f, info.CurrentHealth);
        Assert.False(info.IsAlive);
        Assert.Equal(1, info.DeathCount);
    }

    [Fact]
    public async Task FullResistance_ReturnsIsResisted_WithoutHpChange()
    {
        // 高防御使 -1 减免后四舍五入为 0 → 完全抵抗。
        var (grain, info) = CreateGrain(currentHp: 500f, defense: 10000f);

        var result = await grain.RequestHpChangeAsync(-1, sourceId: 1, DamageType.Physical);

        Assert.Equal(0, result.ActualDelta);
        Assert.Equal(500, result.CurrentHp);
        Assert.True(result.IsResisted);
        Assert.False(result.IsDead);
        Assert.Equal(500f, info.CurrentHealth);
    }

    [Fact]
    public async Task Heal_DoesNotExceedMaxHp()
    {
        var (grain, info) = CreateGrain(currentHp: 990f, maxHp: 1000f);

        var result = await grain.RequestHpChangeAsync(+50, sourceId: 1, DamageType.Healing);

        Assert.Equal(10, result.ActualDelta); // 仅实际生效部分
        Assert.Equal(1000, result.CurrentHp);
        Assert.False(result.IsDead);
    }

    [Fact]
    public async Task MissingCharacterInfo_RejectsChange()
    {
        // CharacterInfo == null 时拒绝（IsResisted=true，无 HP 变化）。
        var mockState = new Mock<IPersistentState<CharacterState>>();
        mockState.SetupGet(s => s.State).Returns(new CharacterState { CharacterInfo = null });

        var grain = (CharacterGrain)System.Runtime.Serialization.FormatterServices
            .GetUninitializedObject(typeof(CharacterGrain));
        GrainTestHarness.InjectField(grain, "_logger", NullLogger<CharacterGrain>.Instance);
        GrainTestHarness.InjectField(grain, "_characterState", mockState.Object);
        GrainTestHarness.InjectField(grain, "_currentZoneShardId", 0L);

        var result = await grain.RequestHpChangeAsync(-100, sourceId: 1, DamageType.Physical);

        Assert.Equal(0, result.ActualDelta);
        Assert.True(result.IsResisted);
        Assert.False(result.IsDead);
    }
}

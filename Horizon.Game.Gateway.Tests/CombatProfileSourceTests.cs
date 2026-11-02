using System;
using System.Threading.Tasks;
using Horizon.Game.Core.Combat;
using Horizon.Game.Message.Combat;
using Horizon.Game.Message.Network;
using Horizon.Model.GameModel;
using Horizon.Orleans.Grains;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Orleans;
using Orleans.Runtime;
using Xunit;
using DamageType = Horizon.Orleans.Interface.World.DamageType;
using CharacterState = Horizon.Game.Message.Network.CharacterState;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 各属性来源如何进入权威五行画像的契约测试。
/// <para>
/// 覆盖"角色基础属性行 → 五行三行"的映射（<see cref="CombatAttributeProfiles"/>）、
/// 该画像在受击裁决里的实际减免，以及穿戴/加点之后的缓存失效入口
/// <see cref="Horizon.Orleans.Interface.ICharacterGrain.RefreshCombatProfileAsync"/>。
/// </para>
/// </summary>
public class CombatProfileSourceTests
{
    private const int Metal = 1, Wood = 2, Water = 3, Fire = 4, Earth = 5;

    // ── 属性表行 → 来源贡献 ────────────────────────────────────────

    [Fact]
    public void FromAttributeRow_maps_the_three_element_columns_in_the_agreed_row_order()
    {
        var row = new CharacterAttributeEntity
        {
            MetalAttack = 100, WoodAttack = 200, WaterAttack = 300, FireAttack = 400, EarthAttack = 500,
            MetalResistance = 11, WoodResistance = 22, WaterResistance = 33, FireResistance = 44, EarthResistance = 55,
            MetalPenetration = 1, WoodPenetration = 2, WaterPenetration = 3, FirePenetration = 4, EarthPenetration = 5,
        };

        var c = CombatAttributeProfiles.FromAttributeRow(row);

        Assert.Equal(ElementStatVector.Zero + new ElementStatVector(100, 200, 300, 400, 500), c.Attack);
        Assert.Equal(11f, c.Defense.Metal);
        Assert.Equal(55f, c.Defense.Earth);
        Assert.Equal(4f, c.Penetration.Fire);
        Assert.Equal((int)CombatSourceType.BaseAttribute, c.Source);
    }

    [Fact]
    public void FromAttributeRow_of_a_missing_row_contributes_nothing()
    {
        var c = CombatAttributeProfiles.FromAttributeRow(null);

        Assert.Equal(0f, c.Attack.Total);
        Assert.Equal(0f, c.Defense.Total);
        Assert.Equal(0f, c.Penetration.Total);
    }

    [Fact]
    public void Build_adds_base_row_attack_to_allocation_attack_but_keeps_the_highest_defense()
    {
        // 基础属性给金攻 100 / 金防 900；加点金 10 → 金攻 300、金防 50。
        var row = new CharacterAttributeEntity { MetalAttack = 100, MetalResistance = 900 };
        var allocation = new AttributeAllocationData { Metal = 10 };

        var profile = CombatAttributeProfiles.Build(allocation, row, baseDefense: 0f, baseAttack: 0f, element: Metal);

        Assert.Equal(400f, profile.Attack.Metal);   // 100 + 300：攻击求和
        Assert.Equal(900f, profile.Defense.Metal);  // max(900, 50)：防御已高者计
        Assert.Equal(300f, profile.Penetration.Metal); // max(0, 300)
        Assert.Equal(2, profile.Sources.Count);
    }

    // ── 受击裁决：来源真的进入减免 ─────────────────────────────────

    private static CharacterGrain CreateCharacterGrain(float defense, AttributeAllocationData? allocation)
    {
        var info = new CharacterInfo
        {
            CharacterId = 42,
            CurrentHealth = 1000f,
            MaxHealth = 1000f,
            Defense = defense,
            IsAlive = true,
        };
        var state = new CharacterState { CharacterInfo = info, Allocation = allocation };
        var mockState = new Mock<IPersistentState<CharacterState>>();
        mockState.SetupGet(s => s.State).Returns(state);

        var grain = (CharacterGrain)System.Runtime.Serialization.FormatterServices
            .GetUninitializedObject(typeof(CharacterGrain));
        GrainTestHarness.InjectField(grain, "_logger", NullLogger<CharacterGrain>.Instance);
        GrainTestHarness.InjectField(grain, "_characterState", mockState.Object);
        GrainTestHarness.InjectField(grain, "_currentZoneShardId", 0L);
        // 不注入 _gameAttributeContext：属性表行按"读不到"降级，加点仍来自 grain 状态——
        // 正是未迁移库上的真实运行形状。
        return grain;
    }

    [Fact]
    public async Task Allocation_element_defense_reduces_the_matching_attack_row()
    {
        // 通用防御 100 + 金 10 点（每点 5 金防）= 150 → 减免 150/250 = 60% → -100 变 -40。
        // 用金点而不是土点做这条断言：土点还会另给 50 通用防御/点，会把两层混成一笔账。
        var grain = CreateCharacterGrain(100f, new AttributeAllocationData { Metal = 10 });

        var result = await grain.RequestElementalHpChangeAsync(
            -100, sourceId: 1, DamageType.Physical, attackElement: Metal,
            attackerPenetration: ElementStatVector.Zero, defenderBuffDefense: ElementStatVector.Zero);

        Assert.Equal(-40, result.ActualDelta);
    }

    [Fact]
    public async Task Allocation_element_defense_does_not_leak_into_other_rows()
    {
        // 同一份金点分配，攻击走土行 → 土防 0，减免只来自通用 100
        var grain = CreateCharacterGrain(100f, new AttributeAllocationData { Metal = 10 });

        var result = await grain.RequestElementalHpChangeAsync(
            -100, sourceId: 1, DamageType.Physical, attackElement: Earth,
            attackerPenetration: ElementStatVector.Zero, defenderBuffDefense: ElementStatVector.Zero);

        Assert.Equal(-50, result.ActualDelta);
    }

    [Fact]
    public async Task Allocation_penetration_from_the_attacker_offsets_the_defenders_element_row()
    {
        // 守方通用防御 0，金 40 点 → 金防 200；攻方火破防 300 抵免金行（火克金）
        var grain = CreateCharacterGrain(0f, new AttributeAllocationData { Metal = 40 });

        var noPen = await grain.RequestElementalHpChangeAsync(
            -100, sourceId: 1, DamageType.Physical, attackElement: Metal,
            attackerPenetration: ElementStatVector.Zero, defenderBuffDefense: ElementStatVector.Zero);
        Assert.Equal(-33, noPen.ActualDelta);   // 200/(200+100) = 2/3 减免 → -33.33 取整

        var withPen = await grain.RequestElementalHpChangeAsync(
            -100, sourceId: 1, DamageType.Physical, attackElement: Metal,
            attackerPenetration: new ElementStatVector(0f, 0f, 0f, 300f, 0f),
            defenderBuffDefense: ElementStatVector.Zero);
        Assert.Equal(-100, withPen.ActualDelta); // 破防抵光 → 全额
    }

    [Fact]
    public async Task RefreshCombatProfile_picks_up_points_allocated_after_the_first_hit()
    {
        var allocation = new AttributeAllocationData { Metal = 10 };
        var grain = CreateCharacterGrain(100f, allocation);

        Assert.Equal(-40, (await grain.RequestElementalHpChangeAsync(
            -100, 1, DamageType.Physical, Metal, ElementStatVector.Zero, ElementStatVector.Zero)).ActualDelta);

        // 玩家在别的请求里把金点加到 40（画像此时已被缓存）
        allocation.Metal = 40;
        await grain.RefreshCombatProfileAsync();

        // 通用 100 + 金防 200 = 300 → 减免 3/4 → -25
        Assert.Equal(-25, (await grain.RequestElementalHpChangeAsync(
            -100, 1, DamageType.Physical, Metal, ElementStatVector.Zero, ElementStatVector.Zero)).ActualDelta);
    }

    [Fact]
    public async Task Legacy_three_argument_entry_point_behaves_exactly_like_the_unattributed_case()
    {
        var grain = CreateCharacterGrain(100f, new AttributeAllocationData());

        var legacy = await grain.RequestHpChangeAsync(-100, 1, DamageType.Physical);
        var elemental = await grain.RequestElementalHpChangeAsync(
            -100, 1, DamageType.Physical, 0, ElementStatVector.Zero, ElementStatVector.Zero);

        Assert.Equal(elemental.ActualDelta, legacy.ActualDelta);
        Assert.Equal(-50, legacy.ActualDelta);
    }
}

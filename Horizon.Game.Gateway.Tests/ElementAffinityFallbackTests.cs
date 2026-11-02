using System;
using Horizon.Game.Core.Combat;
using Horizon.Game.Message.Combat;
using Horizon.Game.Message.Network;
using Horizon.Model.GameModel;
using Horizon.Orleans.Grains;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 五行属性值（亲和）→ 三行兜底的口径钉死测试。
/// <para>
/// 规则（2026-09-29 定）：兜底行 = 亲和 ÷ <see cref="AttributeDerivation.AffinityPerEquivalentPoint"/>
/// 个等效点 × 该行每点换算 × <see cref="AttributeDerivation.AffinityFallbackRatio"/>，
/// 再与已聚合出来的那一行<b>逐行取较高</b>。
/// 之所以取较高而不是相加：加点已经全额进过三行，而它同时贡献了 10 点/分的亲和；
/// 若把由同一批点数折出的半额亲和再加一遍，等于给加点角色凭空多付五成五行伤害。
/// </para>
/// </summary>
public class ElementAffinityFallbackTests
{
    private const int Metal = 1, Wood = 2, Water = 3, Fire = 4, Earth = 5;

    // ── 换算本身 ────────────────────────────────────────────────────

    [Fact]
    public void Fallback_is_half_of_the_per_point_allocation_formula()
    {
        // 金 30 点亲和 = 3 个等效点：攻 3×30×0.5=45、防 3×5×0.5=7.5、破防 3×30×0.5=45。
        var vectors = AttributeDerivation.DeriveAffinityFallback(new ElementStatVector(30, 0, 0, 0, 0));

        Assert.Equal(45f, vectors.Attack.Metal);
        Assert.Equal(7.5f, vectors.Defense.Metal);
        Assert.Equal(45f, vectors.Penetration.Metal);
    }

    [Fact]
    public void Each_row_uses_its_own_element_conversion_and_fire_gets_no_defense()
    {
        // 木的每点攻击是 20（其余 30），火的每点防御按规格是 0。
        var vectors = AttributeDerivation.DeriveAffinityFallback(new ElementStatVector(0, 20, 0, 20, 0));

        Assert.Equal(20f, vectors.Attack.Wood);      // 2 点 × 20 × 0.5
        Assert.Equal(30f, vectors.Attack.Fire);      // 2 点 × 30 × 0.5
        Assert.Equal(5f, vectors.Defense.Wood);      // 2 × 5 × 0.5
        Assert.Equal(0f, vectors.Defense.Fire);      // 火系防御换算未给数值，兜底同样不给
        Assert.Equal(30f, vectors.Penetration.Wood);
    }

    [Fact]
    public void No_affinity_falls_back_to_nothing()
    {
        var vectors = AttributeDerivation.DeriveAffinityFallback(ElementStatVector.Zero);

        Assert.Equal(0f, vectors.Attack.Total);
        Assert.Equal(0f, vectors.Defense.Total);
        Assert.Equal(0f, vectors.Penetration.Total);
    }

    // ── 与聚合结果的合成 ────────────────────────────────────────────

    [Fact]
    public void Fallback_never_lowers_an_already_higher_row()
    {
        // 加点金 10 点：攻 300 / 防 50 / 破防 300，同时贡献 100 点亲和（兜底 150/25/150）。
        // 取较高之后必须与不兜底逐值相同——半额地板永远压不过全额加点。
        var allocation = new AttributeAllocationData { Metal = 10 };
        var sources = new[] { CombatSourceType.Allocation.AsContribution(
            AttributeDerivation.DeriveElementVectors(allocation)) };

        var plain = CombatProfile.Aggregate(sources);
        var withFallback = CombatProfile.Aggregate(sources,
            AttributeDerivation.AffinityFromAllocation(allocation));

        Assert.Equal(plain.Attack, withFallback.Attack);
        Assert.Equal(plain.Defense, withFallback.Defense);
        Assert.Equal(plain.Penetration, withFallback.Penetration);
        Assert.Equal(300f, withFallback.Attack.Metal);
    }

    [Fact]
    public void Affinity_that_has_no_row_behind_it_still_produces_a_row()
    {
        // 一件只给亲和、不给三行的装备（历史上包裹类就是这样）：三行原本恒 0，
        // 现在按半额地板拿到一份非零值——这正是兜底要解决的"亲和对战斗零贡献"。
        var sources = Array.Empty<CombatContribution>();

        var vectors = CombatProfile.Aggregate(sources, new ElementStatVector(40, 0, 0, 0, 0));

        Assert.Equal(60f, vectors.Attack.Metal);     // 4 点 × 30 × 0.5
        Assert.Equal(60f, vectors.Penetration.Metal);
    }

    [Fact]
    public void Fallback_is_applied_row_by_row_not_to_the_highest_row()
    {
        // 只有木有亲和，金行不该跟着涨。
        var vectors = CombatProfile.Aggregate(
            Array.Empty<CombatContribution>(), new ElementStatVector(0, 50, 0, 0, 0));

        Assert.Equal(0f, vectors.Attack.Metal);
        Assert.Equal(50f, vectors.Attack.Wood);      // 5 点 × 20 × 0.5（木的每点攻击是 20）
    }

    // ── 来源接线：装备亲和必须真的进到画像里 ────────────────────────

    [Fact]
    public void Equipment_affinity_flows_into_the_profile_fallback()
    {
        // 无加点、只穿一件"金 30 亲和"的装备：兜底攻 45。
        var parsed = ItemAttributeContract.Parse(@"{""Metal"": 30}");
        Assert.Equal(30f, parsed.Affinity.Metal);
        Assert.Equal(0f, parsed.Contribution.Attack.Metal);   // 亲和不进三行本身

        var profile = CombatAttributeProfiles.Build(
            new AttributeAllocationData(), new CharacterAttributeEntity(),
            extraAffinity: parsed.Affinity,
            extraSources: parsed.Contribution);

        Assert.Equal(45f, profile.Attack.Metal);
        Assert.Equal(30f, profile.Affinity.Metal);
    }

    [Fact]
    public void Allocation_and_equipment_affinity_add_before_the_fallback_is_applied()
    {
        // 加点金 1 点（10 亲和）+ 装备金 30 亲和 = 40 亲和 → 兜底 60，
        // 而全额加点行只有 30 → 取较高得 60。装备亲和确实抬高了地板，而不是被加点行盖住。
        var allocation = new AttributeAllocationData { Metal = 1 };
        var parsed = ItemAttributeContract.Parse(@"{""Metal"": 30}");

        var profile = CombatAttributeProfiles.Build(
            allocation, new CharacterAttributeEntity(),
            extraAffinity: parsed.Affinity,
            extraSources: parsed.Contribution);

        Assert.Equal(40f, profile.Affinity.Metal);
        Assert.Equal(60f, profile.Attack.Metal);
    }

    [Fact]
    public void Configured_attack_row_still_wins_over_the_fallback_row()
    {
        // 属性表配了金攻 100，亲和只折出 45 → 100。兜底是地板，不是天花板。
        var row = new CharacterAttributeEntity { MetalAttack = 100 };
        var parsed = ItemAttributeContract.Parse(@"{""Metal"": 30}");

        var profile = CombatAttributeProfiles.Build(
            new AttributeAllocationData(), row,
            extraAffinity: parsed.Affinity,
            extraSources: parsed.Contribution);

        Assert.Equal(100f, profile.Attack.Metal);
    }

    // ── 契约：亲和键的识别边界 ──────────────────────────────────────

    [Fact]
    public void Bare_element_names_parse_as_affinity_and_not_as_a_row()
    {
        var parsed = ItemAttributeContract.Parse(
            @"{""Attack"": 15, ""Metal"": 30, ""MetalAttack"": 30, ""Wood"": 10}");

        Assert.Equal(30f, parsed.Affinity.Metal);
        Assert.Equal(10f, parsed.Affinity.Wood);
        Assert.Equal(30f, parsed.Contribution.Attack.Metal);   // 只有 MetalAttack 进攻行
        Assert.Equal(0f, parsed.Contribution.Attack.Wood);
        Assert.Equal(4, parsed.RecognizedKeyCount);
    }

    [Fact]
    public void Combine_sums_affinity_across_items()
    {
        var combined = ItemAttributeContract.Combine(new[]
        {
            ItemAttributeContract.Parse(@"{""Metal"": 30}"),
            ItemAttributeContract.Parse(@"{""Metal"": 20, ""Wood"": 10}"),
        });

        Assert.Equal(50f, combined.Affinity.Metal);
        Assert.Equal(10f, combined.Affinity.Wood);
    }
}

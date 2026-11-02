using System;
using Horizon.Game.Core.Combat;
using Horizon.Game.Message.Combat;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 五行伤害权威口径的金值测试（锁定 <see cref="ElementalDamageResolver"/> 与
/// <see cref="ElementStatVector"/> 的三条聚合/结算规则）。
/// <para>
/// 这些数值是"服务端唯一事实源"的判据：任何一端改动公式都会在此处失败，
/// 从而强制客户端预览（<c>CharacterAttributesComponent</c>）与结算同步演进。
/// </para>
/// </summary>
public class ElementalDamageResolverTests
{
    private const int Metal = 1, Wood = 2, Water = 3, Fire = 4, Earth = 5;

    private static void AssertClose(float expected, float actual)
        => Assert.Equal((double)expected, (double)actual, 4);

    // ── 聚合：攻方求和 / 防与破防取最高 ──────────────────────────────

    [Fact]
    public void SumSources_adds_each_row_separately()
    {
        var sources = new[]
        {
            new ElementStatVector(100f, 10f, 0f, 0f, 0f),   // 基础属性
            new ElementStatVector( 20f,  0f, 5f, 0f, 0f),   // 装备
            new ElementStatVector(  0f, 40f, 0f, 8f, 0f),   // 技能
            new ElementStatVector( 15f,  0f, 0f, 0f, 2f),   // Buff
        };

        var sum = ElementalDamageResolver.SumSources(sources);

        AssertClose(135f, sum.Metal);
        AssertClose(50f, sum.Wood);
        AssertClose(5f, sum.Water);
        AssertClose(8f, sum.Fire);
        AssertClose(2f, sum.Earth);
    }

    [Fact]
    public void MaxSources_takes_highest_row_and_does_not_stack()
    {
        var sources = new[]
        {
            new ElementStatVector(100f, 0f, 0f, 0f, 30f),
            new ElementStatVector( 60f, 0f, 0f, 0f, 70f),   // 金属性更低、土属性更高
            new ElementStatVector(  0f, 0f, 0f, 0f,  5f),
        };

        var best = ElementalDamageResolver.MaxSources(sources);

        AssertClose(100f, best.Metal);   // 不等于 160：防御不叠加
        AssertClose(70f, best.Earth);
        AssertClose(0f, best.Wood);
    }

    [Fact]
    public void MaxSources_floors_negative_contributions_at_zero()
    {
        // 减益类来源（破防 debuff 写成负防御）不能把"已高者计"拉到负值。
        var best = ElementalDamageResolver.MaxSources(new[]
        {
            new ElementStatVector(-50f, 0f, 0f, 0f, 0f),
            ElementStatVector.Zero,
        });

        AssertClose(0f, best.Metal);
    }

    [Fact]
    public void Aggregation_helpers_tolerate_null_and_empty()
    {
        AssertClose(0f, ElementalDamageResolver.SumSources(null!).Total);
        AssertClose(0f, ElementalDamageResolver.MaxSources(null!).Total);
        AssertClose(0f, ElementalDamageResolver.SumSources(Array.Empty<ElementStatVector>()).Total);
        AssertClose(0f, ElementalDamageResolver.MaxSources(Array.Empty<ElementStatVector>()).Total);
    }

    // ── 相克环 ────────────────────────────────────────────────────

    [Theory]
    [InlineData(Metal, Wood)]   // 金克木
    [InlineData(Wood, Earth)]   // 木克土
    [InlineData(Earth, Water)]  // 土克水
    [InlineData(Water, Fire)]   // 水克火
    [InlineData(Fire, Metal)]   // 火克金
    public void Overcomes_matches_standard_cycle(int element, int overcome)
        => Assert.Equal(overcome, ElementalDamageResolver.Overcomes(element));

    [Theory]
    [InlineData(Metal, Fire)]   // 金被火克
    [InlineData(Wood, Metal)]   // 木被金克
    [InlineData(Water, Earth)]  // 水被土克
    [InlineData(Fire, Water)]   // 火被水克
    [InlineData(Earth, Wood)]   // 土被木克
    public void OvercomeBy_is_inverse_of_overcomes(int element, int overcomeBy)
    {
        Assert.Equal(overcomeBy, ElementalDamageResolver.OvercomeBy(element));
        Assert.Equal(element, ElementalDamageResolver.Overcomes(overcomeBy));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(6)]
    public void Cycle_returns_none_for_unattributed_or_out_of_range(int element)
    {
        Assert.Equal(0, ElementalDamageResolver.Overcomes(element));
        Assert.Equal(0, ElementalDamageResolver.OvercomeBy(element));
    }

    /// <summary>
    /// 完整 5×5 克制倍率矩阵 + 无属性行/列。逐格钉死，避免"改一个分支"造成的静默漂移。
    /// </summary>
    [Fact]
    public void DamageMultiplier_matches_full_golden_matrix()
    {
        // 行 = 攻方元素，列 = 守方元素；0 = 无属性。
        // 相克环无 2-环，故任一格至多命中一个分支：(攻克守)=1.25、(守克攻)=0.8、余 1.0。
        var expected = new Dictionary<(int atk, int def), float>
        {
            [(Metal, Metal)] = 1.0f, [(Metal, Wood)] = 1.25f, [(Metal, Water)] = 1.0f, [(Metal, Fire)] = 0.8f, [(Metal, Earth)] = 1.0f,
            [(Wood, Metal)] = 0.8f, [(Wood, Wood)] = 1.0f, [(Wood, Water)] = 1.0f, [(Wood, Fire)] = 1.0f, [(Wood, Earth)] = 1.25f,
            [(Water, Metal)] = 1.0f, [(Water, Wood)] = 1.0f, [(Water, Water)] = 1.0f, [(Water, Fire)] = 1.25f, [(Water, Earth)] = 0.8f,
            [(Fire, Metal)] = 1.25f, [(Fire, Wood)] = 1.0f, [(Fire, Water)] = 0.8f, [(Fire, Fire)] = 1.0f, [(Fire, Earth)] = 1.0f,
            [(Earth, Metal)] = 1.0f, [(Earth, Wood)] = 0.8f, [(Earth, Water)] = 1.25f, [(Earth, Fire)] = 1.0f, [(Earth, Earth)] = 1.0f,
        };

        foreach (var (key, value) in expected)
            AssertClose(value, ElementalDamageResolver.DamageMultiplier(key.atk, key.def));

        // 任一方无属性 → 不放大也不削弱。
        AssertClose(1.0f, ElementalDamageResolver.DamageMultiplier(0, Wood));
        AssertClose(1.0f, ElementalDamageResolver.DamageMultiplier(Metal, 0));
        AssertClose(1.0f, ElementalDamageResolver.DamageMultiplier(0, 0));
    }

    // ── 结算：向量按行取值 ────────────────────────────────────────

    [Fact]
    public void At_returns_zero_for_unattributed_and_out_of_range()
    {
        var v = new ElementStatVector(1f, 2f, 3f, 4f, 5f);
        AssertClose(1f, v.At(Metal));
        AssertClose(2f, v.At(Wood));
        AssertClose(3f, v.At(Water));
        AssertClose(4f, v.At(Fire));
        AssertClose(5f, v.At(Earth));
        AssertClose(0f, v.At(0));
        AssertClose(0f, v.At(6));
        AssertClose(0f, v.At(-3));
        AssertClose(15f, v.Total);
        AssertClose(5f, v.Max);
    }

    // ── 结算：命中哪一防御行 ──────────────────────────────────────

    /// <summary>
    /// 金攻 × 木守（×1.25），守方金防御 300 被攻方火系破防 200 抵免到 100，
    /// 曲线 100/(100+100)=50% → 200×1.25×0.5 = <b>125</b>。
    /// </summary>
    [Fact]
    public void Resolve_advantage_attack_with_offset_defense()
    {
        var result = ElementalDamageResolver.Resolve(
            attack: new ElementStatVector(200f, 0f, 0f, 0f, 0f),
            penetration: new ElementStatVector(0f, 0f, 0f, 200f, 0f),
            defense: new ElementStatVector(300f, 999f, 0f, 0f, 0f),
            attackElement: Metal,
            defenderElement: Wood);

        AssertClose(200f, result.Attack);
        AssertClose(1.25f, result.Multiplier);
        AssertClose(250f, result.RawDamage);
        AssertClose(300f, result.DefenseRow);        // 同行对抗：金攻由金防承担
        AssertClose(200f, result.Penetration);        // 火克金 → 火系破防抵免金防
        AssertClose(100f, result.EffectiveDefense);
        AssertClose(0.5f, result.DefenseReduction);
        AssertClose(125f, result.FinalDamage);
    }

    /// <summary>守方元素决定倍率，与防御行无关：金攻 × 火守（被克 ×0.8）→ 200×0.8×0.5 = <b>80</b>。</summary>
    [Fact]
    public void Resolve_disadvantage_attack_uses_same_defense_row()
    {
        var result = ElementalDamageResolver.Resolve(
            new ElementStatVector(200f, 0f, 0f, 0f, 0f),
            new ElementStatVector(0f, 0f, 0f, 200f, 0f),
            new ElementStatVector(300f, 0f, 0f, 0f, 0f),
            attackElement: Metal,
            defenderElement: Fire);

        AssertClose(0.8f, result.Multiplier);
        AssertClose(160f, result.RawDamage);
        AssertClose(100f, result.EffectiveDefense);
        AssertClose(80f, result.FinalDamage);
    }

    /// <summary>无关元素对垒不放大不削弱：金攻 × 水守 → 200×1.0×0.5 = <b>100</b>。</summary>
    [Fact]
    public void Resolve_neutral_pairing_has_unit_multiplier()
    {
        var result = ElementalDamageResolver.Resolve(
            new ElementStatVector(200f, 0f, 0f, 0f, 0f),
            new ElementStatVector(0f, 0f, 0f, 200f, 0f),
            new ElementStatVector(300f, 0f, 0f, 0f, 0f),
            attackElement: Metal,
            defenderElement: Water);

        AssertClose(1.0f, result.Multiplier);
        AssertClose(200f, result.RawDamage);
        AssertClose(100f, result.FinalDamage);
    }

    // ── 结算：破防只抵消防御，不额外加伤 ──────────────────────────

    /// <summary>
    /// 破防只打"克制的那一行"：攻方把破防堆在金行，对金防御毫无作用。
    /// 200×1.0 面对 300 防御 → 300/(300+100)=75% 减免 → <b>50</b>。
    /// </summary>
    [Fact]
    public void Resolve_penetration_on_wrong_row_offsets_nothing()
    {
        var result = ElementalDamageResolver.Resolve(
            new ElementStatVector(200f, 0f, 0f, 0f, 0f),
            new ElementStatVector(9999f, 0f, 0f, 0f, 0f),
            new ElementStatVector(300f, 0f, 0f, 0f, 0f),
            attackElement: Metal,
            defenderElement: Water);

        AssertClose(0f, result.Penetration);
        AssertClose(300f, result.EffectiveDefense);
        AssertClose(0.75f, result.DefenseReduction);
        AssertClose(50f, result.FinalDamage);
    }

    /// <summary>破防溢出把有效防御压到 0（不为负，也不产生增伤）：200×1.0×1.0 = <b>200</b>。</summary>
    [Fact]
    public void Resolve_penetration_overflow_clamps_defense_at_zero_without_bonus_damage()
    {
        var result = ElementalDamageResolver.Resolve(
            new ElementStatVector(200f, 0f, 0f, 0f, 0f),
            new ElementStatVector(0f, 0f, 0f, 500f, 0f),
            new ElementStatVector(50f, 0f, 0f, 0f, 0f),
            attackElement: Metal,
            defenderElement: 0);

        AssertClose(0f, result.EffectiveDefense);
        AssertClose(0f, result.DefenseReduction);
        AssertClose(200f, result.FinalDamage);
        AssertClose(result.RawDamage, result.FinalDamage);   // 全额伤害：破防没有加成
    }

    /// <summary>零攻击向量即使破防拉满也是零伤害——破防永远不是伤害来源。</summary>
    [Fact]
    public void Resolve_zero_attack_stays_zero_regardless_of_penetration()
    {
        var result = ElementalDamageResolver.Resolve(
            ElementStatVector.Zero,
            new ElementStatVector(9999f, 9999f, 9999f, 9999f, 9999f),
            new ElementStatVector(300f, 300f, 300f, 300f, 300f),
            attackElement: Metal,
            defenderElement: Wood);

        AssertClose(0f, result.FinalDamage);
    }

    // ── 结算：无属性攻击的回退口径 ────────────────────────────────

    /// <summary>
    /// 无属性攻击没有"该行"：伤害取攻方五行最高值，防御取守方最高行，
    /// 破防仍按克制该行的那一系抵免。500(木最高) × 1.0 → 土系破防 200 抵防水行 300 → 100 → <b>250</b>。
    /// </summary>
    [Fact]
    public void Resolve_unattributed_attack_falls_back_to_highest_rows()
    {
        var result = ElementalDamageResolver.Resolve(
            new ElementStatVector(300f, 500f, 100f, 200f, 400f),
            new ElementStatVector(0f, 0f, 0f, 0f, 200f),
            new ElementStatVector(50f, 80f, 300f, 90f, 40f),
            attackElement: 0,
            defenderElement: 0);

        Assert.Equal(0, result.AttackElement);
        AssertClose(500f, result.Attack);
        AssertClose(1.0f, result.Multiplier);
        AssertClose(300f, result.DefenseRow);   // 水行是守方最高防御
        AssertClose(200f, result.Penetration);  // 土克水
        AssertClose(100f, result.EffectiveDefense);
        AssertClose(250f, result.FinalDamage);
    }

    [Fact]
    public void HighestDefenseElement_picks_max_row_and_ties_break_to_lowest()
    {
        // 金/木同为 120 时取靠前的行，保证结算跨端可复现。
        Assert.Equal(Metal, ElementalDamageResolver.HighestDefenseElement(
            new ElementStatVector(120f, 120f, 0f, 0f, 0f)));

        Assert.Equal(Earth, ElementalDamageResolver.HighestDefenseElement(
            new ElementStatVector(10f, 20f, 30f, 40f, 50f)));

        // 全零没有对抗行 → 0，减免随之为 0。
        Assert.Equal(0, ElementalDamageResolver.HighestDefenseElement(ElementStatVector.Zero));
    }

    // ── Mitigate：上游已算好伤害的减免入口 ────────────────────────

    [Fact]
    public void Mitigate_applies_defense_curve_only()
    {
        // 100 有效防御 → 50% 减免。
        AssertClose(250f, ElementalDamageResolver.Mitigate(
            500f,
            new ElementStatVector(100f, 0f, 0f, 0f, 0f),
            ElementStatVector.Zero,
            attackElement: Metal));

        // 零防御 → 不减免。
        AssertClose(500f, ElementalDamageResolver.Mitigate(
            500f, ElementStatVector.Zero, ElementStatVector.Zero, attackElement: Metal));

        // 非攻击元素的防御行完全不参与。
        AssertClose(500f, ElementalDamageResolver.Mitigate(
            500f,
            new ElementStatVector(0f, 0f, 0f, 0f, 999f),
            ElementStatVector.Zero,
            attackElement: Metal));
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(-99999f)]
    public void Mitigate_never_produces_damage_from_non_positive_input(float incoming)
        => AssertClose(0f, ElementalDamageResolver.Mitigate(
            incoming, ElementStatVector.Zero, ElementStatVector.Zero, attackElement: Wood));

    [Fact]
    public void Mitigate_and_resolve_agree_on_the_same_inputs()
    {
        var attack = new ElementStatVector(200f, 0f, 0f, 0f, 0f);
        var penetration = new ElementStatVector(0f, 0f, 0f, 200f, 0f);
        var defense = new ElementStatVector(300f, 0f, 0f, 0f, 0f);

        var resolved = ElementalDamageResolver.Resolve(attack, penetration, defense, Metal, Wood);
        float viaMitigate = ElementalDamageResolver.Mitigate(resolved.RawDamage, defense, penetration, Metal);

        AssertClose(viaMitigate, resolved.FinalDamage);
    }

    // ── 端到端：多来源聚合 → 结算 ─────────────────────────────────

    /// <summary>
    /// 用真实的"基础属性 + 装备 + 技能 + Buff"四类来源走一遍完整链路：
    /// 攻击逐行相加、防御/破防取最高，最终 125.0。
    /// </summary>
    [Fact]
    public void End_to_end_multi_source_aggregation_then_resolve()
    {
        var sources = new[]
        {
            new ElementStatVector(100f, 0f, 0f, 20f, 0f),   // 基础属性（金/火）
            new ElementStatVector( 60f, 0f, 0f, 80f, 0f),   // 装备
            new ElementStatVector( 40f, 0f, 0f,  0f, 0f),   // 技能
            new ElementStatVector( 10f, 0f, 0f, 50f, 0f),   // Buff
        };

        var attack = ElementalDamageResolver.SumSources(sources);
        var penetration = ElementalDamageResolver.MaxSources(new[]
        {
            new ElementStatVector(0f, 0f, 0f, 90f, 0f),     // 装备提供的火系破防
            new ElementStatVector(0f, 0f, 0f, 200f, 0f),    // Buff 提供的火系破防（更高者胜）
        });

        AssertClose(210f, attack.Metal);
        AssertClose(150f, attack.Fire);
        AssertClose(200f, penetration.Fire);   // 90+200=290 若被误当求和会在此失败

        // 守方：木防 400（技能）与 120（装备）取 400；金防 300（基础）。
        var defense = ElementalDamageResolver.MaxSources(new[]
        {
            new ElementStatVector(300f, 120f, 0f, 0f, 0f),
            new ElementStatVector(  0f, 400f, 0f, 0f, 0f),
        });
        AssertClose(400f, defense.Wood);

        // 金攻 × 木守 = ×1.25 → 262.5；金防 300 - 火破防 200 = 100 → 50% 减免 → 131.25
        var result = ElementalDamageResolver.Resolve(attack, penetration, defense, Metal, Wood);
        AssertClose(262.5f, result.RawDamage);
        AssertClose(131.25f, result.FinalDamage);
    }

    // ── 与既有全项目减免曲线的兼容性 ─────────────────────────────

    [Fact]
    public void Defense_curve_constant_is_the_project_wide_100()
    {
        AssertClose(100f, ElementalDamageResolver.DefenseCurveConstant);
        AssertClose(1.25f, ElementalDamageResolver.AdvantageMultiplier);
        AssertClose(0.8f, ElementalDamageResolver.DisadvantageMultiplier);
    }
}

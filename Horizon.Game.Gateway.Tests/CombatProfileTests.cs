using System;
using Horizon.Game.Core.Combat;
using Horizon.Game.Message.Combat;
using Horizon.Game.Message.Network;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 战斗画像聚合（<see cref="CombatProfile"/>）与加点→五行向量（<see cref="AttributeDerivation.DeriveElementVectors"/>）
/// 的金值测试。
/// <para>
/// 每点换算常数在此钉死为客户端 <c>CharacterAttributesComponent</c>
/// （<c>GetElementDamage</c> / <c>GetElementDefense</c> / <c>GetPenetration</c>）的镜像值：
/// 两端任一改动都会让本套件失败，从而暴露"预览与结算不一致"这类最难查的缺陷。
/// </para>
/// </summary>
public class CombatProfileTests
{
    private const int Metal = 1, Wood = 2, Water = 3, Fire = 4, Earth = 5;

    private static void AssertClose(float expected, float actual)
        => Assert.Equal((double)expected, (double)actual, 4);

    private static AttributeAllocationData Alloc(int metal = 0, int wood = 0, int water = 0, int fire = 0, int earth = 0,
        int strength = 0, int agility = 0, int constitution = 0, int intelligence = 0)
        => new AttributeAllocationData
        {
            Strength = strength,
            Agility = agility,
            Health = constitution,
            Intelligence = intelligence,
            Metal = metal,
            Wood = wood,
            Water = water,
            Fire = fire,
            Earth = earth,
        };

    // ── 加点 → 五行三行（客户端镜像常数）────────────────────────────

    [Fact]
    public void DeriveElementVectors_matches_client_mirror_constants()
    {
        var v = AttributeDerivation.DeriveElementVectors(Alloc(metal: 10, wood: 5, water: 2, fire: 3, earth: 4));

        // 攻：金30 木20 水30 火30 土30 每点
        AssertClose(300f, v.Attack.Metal);
        AssertClose(100f, v.Attack.Wood);
        AssertClose(60f, v.Attack.Water);
        AssertClose(90f, v.Attack.Fire);
        AssertClose(120f, v.Attack.Earth);

        // 防：5 每点，火系无防御
        AssertClose(50f, v.Defense.Metal);
        AssertClose(25f, v.Defense.Wood);
        AssertClose(10f, v.Defense.Water);
        AssertClose(0f, v.Defense.Fire);
        AssertClose(20f, v.Defense.Earth);

        // 破防：统一 30 每点
        AssertClose(300f, v.Penetration.Metal);
        AssertClose(150f, v.Penetration.Wood);
        AssertClose(60f, v.Penetration.Water);
        AssertClose(90f, v.Penetration.Fire);
        AssertClose(120f, v.Penetration.Earth);
    }

    [Fact]
    public void DeriveElementVectors_of_null_or_empty_is_all_zero()
    {
        AssertClose(0f, AttributeDerivation.DeriveElementVectors(null).Attack.Total);
        AssertClose(0f, AttributeDerivation.DeriveElementVectors(null).Defense.Total);
        AssertClose(0f, AttributeDerivation.DeriveElementVectors(null).Penetration.Total);

        var empty = AttributeDerivation.DeriveElementVectors(new AttributeAllocationData());
        AssertClose(0f, empty.Attack.Total);
    }

    // ── 聚合口径 ──────────────────────────────────────────────────

    [Fact]
    public void Build_sums_attack_but_takes_max_of_defense_and_penetration()
    {
        var contributions = new[]
        {
            CombatContribution.FromVectors(CombatSourceType.BaseAttribute,
                new ElementStatVector(100f, 0f, 0f, 0f, 0f),
                new ElementStatVector(100f, 0f, 0f, 0f, 0f),
                new ElementStatVector(100f, 0f, 0f, 0f, 0f)),
            CombatContribution.FromVectors(CombatSourceType.Allocation,
                new ElementStatVector( 50f, 0f, 0f, 0f, 0f),
                new ElementStatVector( 30f, 0f, 0f, 0f, 0f),
                new ElementStatVector( 30f, 0f, 0f, 0f, 0f)),
            CombatContribution.FromVectors(CombatSourceType.Equipment,
                new ElementStatVector( 20f, 0f, 0f, 0f, 0f),
                new ElementStatVector(200f, 0f, 0f, 0f, 0f),   // 装备防御最高
                new ElementStatVector( 10f, 0f, 0f, 0f, 0f)),
            CombatContribution.FromVectors(CombatSourceType.Skill,
                new ElementStatVector( 10f, 0f, 0f, 0f, 0f),
                new ElementStatVector( 60f, 0f, 0f, 0f, 0f),
                new ElementStatVector(250f, 0f, 0f, 0f, 0f)),   // 技能破防最高
            CombatContribution.FromVectors(CombatSourceType.Buff,
                new ElementStatVector(  5f, 0f, 0f, 0f, 0f),
                new ElementStatVector( 40f, 0f, 0f, 0f, 0f),
                new ElementStatVector( 90f, 0f, 0f, 0f, 0f)),
        };

        var profile = CombatProfile.Build(contributions, baseDefense: 10f, baseAttack: 20f, element: Metal);

        AssertClose(185f, profile.Attack.Metal);      // 100+50+20+10+5（求和）
        AssertClose(200f, profile.Defense.Metal);     // max(100,30,200,60,40)（已高者计）
        AssertClose(250f, profile.Penetration.Metal); // max(100,30,10,250,90)
        AssertClose(10f, profile.BaseDefense);
        AssertClose(20f, profile.BaseAttack);
        Assert.Equal(Metal, profile.Element);
    }

    [Fact]
    public void Build_tolerates_null_and_empty_source_lists()
    {
        var empty = CombatProfile.Build(Array.Empty<CombatContribution>());
        AssertClose(0f, empty.Attack.Total);
        AssertClose(0f, empty.Defense.Total);
        AssertClose(0f, empty.Penetration.Total);

        var nullList = CombatProfile.Build(null!);
        AssertClose(0f, nullList.Attack.Total);
    }

    [Fact]
    public void AttackOnly_contribution_leaves_defense_and_penetration_untouched()
    {
        var profile = CombatProfile.Build(new[]
        {
            CombatContribution.AttackOnly(CombatSourceType.Skill, new ElementStatVector(0f, 0f, 500f, 0f, 0f)),
        });

        AssertClose(500f, profile.Attack.Water);
        AssertClose(0f, profile.Defense.Total);
        AssertClose(0f, profile.Penetration.Total);
    }

    // ── 加点画像（当前服务端唯一有真实数据的来源）──────────────────

    [Fact]
    public void FromAllocation_folds_scalar_bonuses_into_base_layer()
    {
        // 力量 10 → 攻击 +100；敏捷 5 → 防御 +100；金 2 → 金攻 60、金防 10、金破防 60
        var profile = CombatProfile.FromAllocation(Alloc(metal: 2, strength: 10, agility: 5),
            baseDefense: 20f, baseAttack: 100f, element: Metal);

        AssertClose(300f, profile.BaseAttack);   // 100 基值 +（力量10×10 + 金2×50）
        AssertClose(120f, profile.BaseDefense);  // 20 基值 + 敏捷5×20
        AssertClose(60f, profile.Attack.Metal);
        AssertClose(10f, profile.Defense.Metal);
        AssertClose(60f, profile.Penetration.Metal);
        AssertClose(200f, profile.Bonuses.AttackBonus);   // 力量×10 + 金×50
    }

    // ── 端到端：五类来源 → 权威结算 ────────────────────────────────

    /// <summary>
    /// 攻方：加点金 10（金攻 300）、火 4（火破防 120）；守方：通用防御 100、
    /// 装备金防 50、技能金防 80（已高者计 → 80）。
    /// <para>
    /// 300 ×1.25(金克木) = 375 → 有效防御 max(0, 100+80-120)=60 → 减免 60/160=37.5%
    /// → 234.375。这条是"全属性参与权威结算"的总账。
    /// </para>
    /// </summary>
    [Fact]
    public void End_to_end_five_source_profile_settles_authoritative_damage()
    {
        var attacker = CombatProfile.FromAllocation(Alloc(metal: 10, fire: 4), element: Metal);
        AssertClose(300f, attacker.Attack.Metal);
        AssertClose(120f, attacker.Penetration.Fire);

        var defender = CombatProfile.Build(new[]
        {
            CombatContribution.FromVectors(CombatSourceType.BaseAttribute,
                ElementStatVector.Zero, new ElementStatVector(20f, 0f, 0f, 0f, 0f), ElementStatVector.Zero),
            CombatContribution.FromVectors(CombatSourceType.Equipment,
                ElementStatVector.Zero, new ElementStatVector(50f, 0f, 0f, 0f, 0f), ElementStatVector.Zero),
            CombatContribution.FromVectors(CombatSourceType.Skill,
                ElementStatVector.Zero, new ElementStatVector(80f, 0f, 0f, 0f, 0f), ElementStatVector.Zero),
        }, baseDefense: 100f);
        AssertClose(80f, defender.Defense.Metal);   // max(20,50,80)

        var result = ElementalDamageResolver.Resolve(
            attacker.Attack, attacker.Penetration, defender.Defense,
            attackElement: Metal, defenderElement: Wood, baseDefense: defender.BaseDefense);

        AssertClose(375f, result.RawDamage);
        AssertClose(180f, result.DefenseRow);        // 通用 100 + 五行 80
        AssertClose(120f, result.Penetration);       // 火克金 → 火系破防打金防
        AssertClose(60f, result.EffectiveDefense);
        AssertClose(0.375f, result.DefenseReduction);
        AssertClose(234.375f, result.FinalDamage);
    }

    [Fact]
    public void Penetration_offsets_the_base_defense_layer_too()
    {
        // 破防抵免的是"通用+五行"合计：100+80-120=60。
        float actual = ElementalDamageResolver.Mitigate(
            400f, 100f, new ElementStatVector(80f, 0f, 0f, 0f, 0f),
            new ElementStatVector(0f, 0f, 0f, 120f, 0f), Metal);

        AssertClose(250f, actual);   // 400 × (1 - 60/160)
    }
}

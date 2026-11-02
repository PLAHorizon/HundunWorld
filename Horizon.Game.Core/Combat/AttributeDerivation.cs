using System;
using Horizon.Game.Message.Combat;
using Horizon.Game.Message.Network;

namespace Horizon.Game.Core.Combat;

/// <summary>
/// 属性加点 → 战斗属性派生的服务端单一来源。
/// <para>
/// 每点换算常数与客户端 <c>CharacterAttributesComponent</c>（预览侧镜像）逐值一致；
/// 服务端结算（CombatGrain 命中参数、CharacterGrain HP 裁决）一律以本类为准。
/// 修改任一常数时两端必须同步，并优先改这里。
/// </para>
/// </summary>
public static class AttributeDerivation
{
    // --- 每点换算常数（与客户端 CharacterAttributesComponent.cs:270-320 对齐）---
    public const float StrengthHealthPerPoint = 20f;
    public const float StrengthAttackPerPoint = 10f;
    public const float AgilityDefensePerPoint = 20f;
    public const float ConstitutionHealthPerPoint = 100f;
    /// <summary>智力每点提供的内力上限（2026-09-28 起，原"内力每点 +10"作废）。</summary>
    public const float IntelligenceEnergyPerPoint = 100f;
    public const float IntelligenceSpiritPerPoint = 10f;
    public const float IntelligenceWisdomPerPoint = 1f;
    public const float IntelligencePerceptionPerPoint = 3f;
    public const float IntelligenceLuckPerPoint = 0.2f;
    public const float MetalAttackPerPoint = 50f;
    public const float MetalCritRatePerPoint = 0.001f;   // +0.1%/点（比例制）
    public const float MetalCritDamagePerPoint = 0.03f;  // +3%/点
    public const float WoodHealthPerPoint = 300f;
    public const float WaterEnergyPerPoint = 200f;
    public const float EarthDefensePerPoint = 50f;

    // --- 五行点数 → 五行向量（与客户端 CharacterAttributesComponent.GetElementDamage /
    //     GetElementDefense / GetPenetration 逐值对齐；行号 1..5 = 金木水火土）---
    public const float MetalElementAttackPerPoint = 30f;
    public const float WoodElementAttackPerPoint = 20f;
    public const float WaterElementAttackPerPoint = 30f;
    public const float FireElementAttackPerPoint = 30f;
    public const float EarthElementAttackPerPoint = 30f;

    public const float MetalElementDefensePerPoint = 5f;
    public const float WoodElementDefensePerPoint = 5f;
    public const float WaterElementDefensePerPoint = 5f;
    /// <summary>火系防御：策划口径未给数值，保持 0（不是漏项）。</summary>
    public const float FireElementDefensePerPoint = 0f;
    public const float EarthElementDefensePerPoint = 5f;

    /// <summary>破防：五行各自 30/点，存于攻方本行，抵免被自己克制的那一行防御。</summary>
    public const float ElementPenetrationPerPoint = 30f;

    // --- 五行属性值（亲和）→ 三行兜底（2026-09-29 定口径，两端同规则）---
    //
    // 属性表的 metal_attack…earth_attack / *_resistance / *_penetration 三组列全仓没有任何写入方
    // （CharacterGrain 建行时只写加点列），所以"角色基础属性"这一层五行三行恒为 0；
    // 而装备/宝石的五行亲和此前只喂雷达图，同样一点没进战斗。
    //
    // 兜底规则（三句，缺一不可）：
    //   1. 等效点数 = 该五行属性值 ÷ <see cref="AffinityPerEquivalentPoint"/>（每 10 点亲和=1 个等效点，
    //      与客户端雷达图口径 WuxingPerPoint 同源）；
    //   2. 按上面的每点换算公式折成三行，再乘 <see cref="AffinityFallbackRatio"/>（半额）；
    //   3. 与已聚合出来的三行<b>逐行取较高</b>——所以加点全额层永远不会被半额兜底压低，
    //      兜底只把"原本不进战斗的亲和"按半份补进来。

    /// <summary>多少个五行属性值（亲和）折算成 1 个等效加点。</summary>
    public const float AffinityPerEquivalentPoint = 10f;

    /// <summary>兜底生效比例：半额。改这里就等于改整个五行伤害地板，两端必须同步。</summary>
    public const float AffinityFallbackRatio = 0.5f;

    /// <summary>
    /// 五行属性值（亲和）向量 → 兜底的三行。逐行按各自的每点换算折算后取半额。
    /// </summary>
    public static ElementVectors DeriveAffinityFallback(ElementStatVector affinity)
    {
        var points = affinity * (1f / AffinityPerEquivalentPoint) * AffinityFallbackRatio;
        return new ElementVectors
        {
            Attack = new ElementStatVector(
                points.Metal * MetalElementAttackPerPoint,
                points.Wood * WoodElementAttackPerPoint,
                points.Water * WaterElementAttackPerPoint,
                points.Fire * FireElementAttackPerPoint,
                points.Earth * EarthElementAttackPerPoint),
            // 火系防御每点换算未给数值（FireElementDefensePerPoint=0），兜底同样不给。
            Defense = new ElementStatVector(
                points.Metal * MetalElementDefensePerPoint,
                points.Wood * WoodElementDefensePerPoint,
                points.Water * WaterElementDefensePerPoint,
                points.Fire * FireElementDefensePerPoint,
                points.Earth * EarthElementDefensePerPoint),
            Penetration = points * ElementPenetrationPerPoint,
        };
    }

    /// <summary>逐行取较高地合并两份三行（兜底并入聚合结果的唯一入口）。</summary>
    public static ElementVectors MaxVectors(ElementVectors a, ElementVectors b) => new()
    {
        Attack = MaxRow(a.Attack, b.Attack),
        Defense = MaxRow(a.Defense, b.Defense),
        Penetration = MaxRow(a.Penetration, b.Penetration),
    };

    private static ElementStatVector MaxRow(ElementStatVector a, ElementStatVector b)
        => new(Math.Max(a.Metal, b.Metal), Math.Max(a.Wood, b.Wood), Math.Max(a.Water, b.Water),
               Math.Max(a.Fire, b.Fire), Math.Max(a.Earth, b.Earth));

    /// <summary>
    /// 由加点分配派生的五行属性值（亲和）向量：每点 +10 亲和，与客户端
    /// <c>CharacterAttributesComponent</c> 的 <c>WuxingPerPoint</c> 逐值一致。
    /// 装备/宝石亲和由调用方加进来（属性表里没有独立的亲和列供角色自身配置）。
    /// </summary>
    public static ElementStatVector AffinityFromAllocation(AttributeAllocationData? allocation)
    {
        var a = allocation ?? new AttributeAllocationData();
        return new ElementStatVector(a.Metal, a.Wood, a.Water, a.Fire, a.Earth) * AffinityPerEquivalentPoint;
    }

    // --- 内力（能量）口径：与客户端 CharacterAttributesComponent 的能量系统逐值对齐 ---
    // 服务端 CombatInfo.MaxEnergy 以前写死 100 + 派生，而客户端是 1000 基值 + 每秒 10 回复：
    // 两端差一个数量级，且服务端从不上限扩池也不回复，一旦由服务端裁决消耗就会出现
    // "放四五个技能后永久能量不足"。这两常数与 <see cref="EnergyRecoveryPerSecond"/> 一起构成唯一出处。

    /// <summary>内力上限基值（未计加点与装备）。</summary>
    public const float EnergyBaseValue = 1000f;

    /// <summary>
    /// 生命上限基值。与客户端 <c>CharacterAttributesComponent._maxHealthBase</c> 同值，
    /// 由 <c>CharacterHealthPoolTests.Client_And_Server_Share_One_HealthBase</c> 钉住。
    /// <para>
    /// 为什么要有这个常量：角色表里 <c>max_health</c> 一直是 0（建号时写的是 100，而客户端按 1000 起算），
    /// 于是服务端的"死亡判定/复活/克制结算"读到一个不存在的血池——角色既不是活的也不是死的，
    /// 表现是"施法者已死亡"挡下所有技能，而客户端血条满格。血与蓝一样必须有单一定义点。
    /// </para>
    /// </summary>
    public const float HealthBaseValue = 1000f;
    /// <summary>每秒回复的内力。</summary>
    public const float EnergyRecoveryPerSecond = 10f;

    // --- 通用攻防基值的缺省（角色表读不到该行时用什么）---
    // 此前攻方画像用 50/20、受击裁决用 0，同一个角色在两侧算出不同的基值，
    // 打出去和挨一下的减免自然对不上。两侧必须共读这两个常数。
    // 口径沿用历史值（50/20）而不是改成 0：把缺省防御降到 0 会直接削弱所有读不到角色表的实体。

    /// <summary>角色表缺行/缺列时的通用攻击基值。</summary>
    public const float DefaultBaseAttack = 50f;

    /// <summary>角色表缺行/缺列时的通用防御基值。</summary>
    public const float DefaultBaseDefense = 20f;

    /// <summary>
    /// 由加点分配派生战斗属性加成。入参使用服务端权威存储的九项点数
    /// （<see cref="AttributeAllocationData"/>：Health=体质、Intelligence=智力）。
    /// </summary>
    /// <summary>
    /// 服务端生命上限的唯一算法：<b>基值（或表里已写的值）+ 加点加成 + 装备加成</b>。
    /// <para>
    /// 抽成纯函数是为了能被逐值断言：血池一旦有三个数（建号默认 / 客户端镜像 / 战斗缺省），
    /// 死亡判定就会把"没写过"读成"已死亡"。 Grain 侧只留一个调用点，算法在这里。
    /// </para>
    /// <paramref name="rowMaxHealth"/> 大于 0 视为"这列已被权威写过"，与 CombatGrain 画像同口径。
    /// </summary>
    public static float DeriveMaxHealth(AttributeAllocationData? allocation,
        float rowMaxHealth = 0f, float equipmentMaxHealth = 0f)
    {
        var basePool = rowMaxHealth > 0f ? rowMaxHealth : HealthBaseValue;
        return basePool + Derive(allocation).MaxHealthBonus + equipmentMaxHealth;
    }

    public static DerivedBonuses Derive(AttributeAllocationData? allocation)
    {
        var a = allocation ?? new AttributeAllocationData();
        return new DerivedBonuses
        {
            MaxHealthBonus = a.Strength * StrengthHealthPerPoint
                           + a.Health * ConstitutionHealthPerPoint
                           + a.Wood * WoodHealthPerPoint,
            AttackBonus = a.Strength * StrengthAttackPerPoint
                        + a.Metal * MetalAttackPerPoint,
            DefenseBonus = a.Agility * AgilityDefensePerPoint
                         + a.Earth * EarthDefensePerPoint,
            MaxEnergyBonus = a.Intelligence * IntelligenceEnergyPerPoint
                           + a.Water * WaterEnergyPerPoint,
            CritRateBonus = a.Metal * MetalCritRatePerPoint,
            CritDamageBonus = a.Metal * MetalCritDamagePerPoint,
            Spirit = a.Intelligence * IntelligenceSpiritPerPoint,
            Wisdom = a.Intelligence * IntelligenceWisdomPerPoint,
            Perception = a.Intelligence * IntelligencePerceptionPerPoint,
            Luck = a.Intelligence * IntelligenceLuckPerPoint,
        };
    }

    /// <summary>加点派生的战斗属性加成（叠加在基础值/装备值之上）。</summary>
    public readonly struct DerivedBonuses
    {
        public float MaxHealthBonus { get; init; }
        public float AttackBonus { get; init; }
        public float DefenseBonus { get; init; }
        public float MaxEnergyBonus { get; init; }
        /// <summary>暴击率加成（比例制，0.001 = 0.1%）。</summary>
        public float CritRateBonus { get; init; }
        /// <summary>暴伤加成（叠加在 1.5 基础倍率上，比例制）。</summary>
        public float CritDamageBonus { get; init; }
        /// <summary>精神（智力×10）。目前只做展示口径，未参与任何结算。</summary>
        public float Spirit { get; init; }
        /// <summary>智慧（智力×1）。同上，纯派生展示量。</summary>
        public float Wisdom { get; init; }
        /// <summary>神识（智力×3）。同上，纯派生展示量。</summary>
        public float Perception { get; init; }
        /// <summary>幸运值（智力×0.2）。同上，纯派生展示量。</summary>
        public float Luck { get; init; }

        public static readonly DerivedBonuses Zero = new();
    }

    /// <summary>
    /// 由加点分配派生的五行向量（攻/防/破防三行）。
    /// <para>
    /// 行号约定与 <see cref="ElementStatVector"/> 一致（1..5 = 金木水火土）：
    /// 攻击与防御都落在"本行"（金点加金攻金防），破防也存于本行、但按
    /// <see cref="ElementalDamageResolver.OvercomeBy"/> 抵免被自己克制的那一行防御
    /// （金点破木防）。这三行是 <see cref="CombatProfile"/> 的"加点"来源贡献。
    /// </para>
    /// </summary>
    public static ElementVectors DeriveElementVectors(AttributeAllocationData? allocation)
    {
        var a = allocation ?? new AttributeAllocationData();
        return new ElementVectors
        {
            Attack = new ElementStatVector(
                a.Metal * MetalElementAttackPerPoint,
                a.Wood * WoodElementAttackPerPoint,
                a.Water * WaterElementAttackPerPoint,
                a.Fire * FireElementAttackPerPoint,
                a.Earth * EarthElementAttackPerPoint),
            Defense = new ElementStatVector(
                a.Metal * MetalElementDefensePerPoint,
                a.Wood * WoodElementDefensePerPoint,
                a.Water * WaterElementDefensePerPoint,
                a.Fire * FireElementDefensePerPoint,
                a.Earth * EarthElementDefensePerPoint),
            Penetration = new ElementStatVector(
                a.Metal, a.Wood, a.Water, a.Fire, a.Earth) * ElementPenetrationPerPoint,
        };
    }

    /// <summary>一个来源贡献的五行三行（攻/防/破防）。</summary>
    public readonly struct ElementVectors
    {
        public ElementStatVector Attack { get; init; }
        public ElementStatVector Defense { get; init; }
        public ElementStatVector Penetration { get; init; }

        public static readonly ElementVectors Zero = new();
    }
}

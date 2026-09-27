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
    public const float EnergyPointsPerEnergy = 10f;
    public const float MetalAttackPerPoint = 50f;
    public const float MetalCritRatePerPoint = 0.001f;   // +0.1%/点（比例制）
    public const float MetalCritDamagePerPoint = 0.03f;  // +3%/点
    public const float WoodHealthPerPoint = 300f;
    public const float WaterEnergyPerPoint = 200f;
    public const float EarthDefensePerPoint = 50f;

    /// <summary>
    /// 由加点分配派生战斗属性加成。入参使用服务端权威存储的九项点数
    /// （<see cref="AttributeAllocationData"/>：Health=体质、Energy=内力）。
    /// </summary>
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
            MaxEnergyBonus = a.Energy * EnergyPointsPerEnergy
                           + a.Water * WaterEnergyPerPoint,
            CritRateBonus = a.Metal * MetalCritRatePerPoint,
            CritDamageBonus = a.Metal * MetalCritDamagePerPoint,
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

        public static readonly DerivedBonuses Zero = new();
    }
}

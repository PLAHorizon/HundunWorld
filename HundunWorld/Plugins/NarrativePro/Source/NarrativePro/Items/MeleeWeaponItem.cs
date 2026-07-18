using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.Items
{
    /// <summary>
    /// 近战武器。适配 UE5 UMeleeWeaponItem。
    /// 在基础版中此类为空，因为攻击动画等数据更适合存储在攻击能力自身中。
    /// 如有需要可在子类中添加近战相关数据。
    /// </summary>
    public class MeleeWeaponItem : WeaponItem
    {
        /// <summary>近战攻击范围（单位：cm）</summary>
        public float MeleeRange { get; set; } = 150f;

        /// <summary>近战攻击角度（度）</summary>
        public float MeleeAttackAngle { get; set; } = 90f;

        public override float GetAttackRange() => MeleeRange;
    }
}

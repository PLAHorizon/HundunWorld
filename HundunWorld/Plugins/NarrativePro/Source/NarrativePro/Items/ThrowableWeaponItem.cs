using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.Items
{
    /// <summary>
    /// 投掷武器（手雷/飞刀）。适配 UE5 UThrowableWeaponItem。
    /// UE5 中继承自 UEquippableItem 而非 UWeaponItem，因为投掷武器更像可装备的消耗品。
    /// 攻击逻辑由授予的战斗能力处理。
    /// </summary>
    public class ThrowableWeaponItem : EquippableItem
    {
        /// <summary>投掷物资源路径</summary>
        public string ThrowableProjectilePath { get; set; } = "";

        /// <summary>投掷力度</summary>
        public float ThrowForce { get; set; } = 1000f;

        /// <summary>投掷角度偏移（度）</summary>
        public float ThrowAngleOffset { get; set; } = 0f;

        /// <summary>每次投掷消耗的数量</summary>
        public int ConsumePerThrow { get; set; } = 1;

        public override bool bConsumeOnUse { get => true; set => base.bConsumeOnUse = value; }

        /// <summary>投掷武器以堆叠数量作为"弹药"。</summary>
        public virtual bool HasAmmo() => GetQuantity() > 0;

        /// <summary>是否可以攻击（投掷）。</summary>
        public virtual bool CanAttack() => HasAmmo();

        /// <summary>消耗一次投掷所需的数量。</summary>
        public virtual bool ConsumeAmmo(int amount = 1)
        {
            if (OwningInventory == null) return false;
            if (GetQuantity() < amount) return false;
            int consumed = OwningInventory.ConsumeItem(this, amount);
            return consumed > 0;
        }
    }
}

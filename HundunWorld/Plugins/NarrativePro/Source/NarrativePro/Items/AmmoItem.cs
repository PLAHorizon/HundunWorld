using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.Items
{
    /// <summary>
    /// 战斗追踪数据。描述武器攻击时的射线/盒子追踪参数。
    /// 适配 UE5 FCombatTraceData，供 AmmoFragment、RangedWeaponItem 等共享。
    /// </summary>
    public class CombatTraceData
    {
        /// <summary>追踪距离（单位：cm）</summary>
        public float Range { get; set; } = 1000f;

        /// <summary>追踪半径，0 表示线追踪</summary>
        public float Radius { get; set; } = 0f;

        /// <summary>是否使用盒子追踪而非球形追踪</summary>
        public bool bUseBoxTrace { get; set; } = false;

        /// <summary>伤害类型 ID（对应 GAS GameplayEffect）</summary>
        public string DamageTypeId { get; set; } = "";
    }

    /// <summary>
    /// 弹药物品基类。任何物品都可作为弹药（通过 AmmoFragment），但此类添加自动装载为弹药源的额外逻辑。
    /// 适配 UE5 UAmmoItem。
    /// </summary>
    public class AmmoItem : NarrativeItem
    {
        public override void AddedToInventory(NarrativeInventoryComponent inventory, bool fromLoad)
        {
            base.AddedToInventory(inventory, fromLoad);
            if (fromLoad) return;
            // 自动装载为武器的弹药源
            InitAsAmmoSourceForWeapons(inventory);
        }

        /// <summary>查找背包中需要此弹药的武器，并将其设为弹药源。</summary>
        protected virtual void InitAsAmmoSourceForWeapons(NarrativeInventoryComponent inventory)
        {
            if (inventory == null) return;
            foreach (var item in inventory.GetItems())
            {
                if (item is WeaponItem weapon && weapon.WeaponClipState.AmmoItemSource == null)
                {
                    if (!string.IsNullOrEmpty(weapon.RequiredAmmo) &&
                        weapon.RequiredAmmo == ItemClassId)
                    {
                        weapon.WeaponClipState.AmmoItemSource = this;
                        weapon.WeaponClipState.AmmoItemGUID = ItemGUID;
                    }
                }
            }
        }
    }
}

using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.Items
{
    /// <summary>
    /// 装备组件，管理角色按槽位装备的物品。适配 UE5 UEquipmentComponent。
    /// 挂载到角色 Actor 上，与 InventoryComponent 配合。
    /// </summary>
    public class EquipmentComponent : Script
    {
        /// <summary>槽位 → 已装备物品</summary>
        protected Dictionary<string, EquippableItem> _equippedItems = new Dictionary<string, EquippableItem>(StringComparer.Ordinal);

        /// <summary>持握槽位 → 已持握武器。对应 UE5 <c>WieldedWeapons</c>（EquipmentComponent.h）。</summary>
        protected Dictionary<string, WeaponItem> _wieldedWeapons = new Dictionary<string, WeaponItem>(StringComparer.Ordinal);

        /// <summary>装备物品变更事件 (槽位, 旧物品, 新物品)</summary>
        public event Action<string, EquippableItem, EquippableItem> OnEquipmentChanged;

        /// <summary>当前所有已装备物品</summary>
        public IReadOnlyDictionary<string, EquippableItem> EquippedItems => _equippedItems;

        /// <summary>获取指定槽位的已装备物品。</summary>
        public EquippableItem GetEquippedItem(GameplayTag slot)
        {
            return slot.IsValid() && _equippedItems.TryGetValue(slot.TagName, out var item) ? item : null;
        }

        /// <summary>装备物品到槽位（内部调用，不做槽位校验，由物品自身调用）。</summary>
        public virtual void EquipItemToSlot(EquippableItem item, GameplayTag slot)
        {
            if (!slot.IsValid() || item == null) return;

            string slotName = slot.TagName;
            _equippedItems.TryGetValue(slotName, out var old);
            if (old != null && old != item)
            {
                old.UnequipItem();
            }

            _equippedItems[slotName] = item;
            OnEquipmentChanged?.Invoke(slotName, old, item);
        }

        /// <summary>从槽位卸下物品。</summary>
        public virtual void UnequipItemFromSlot(GameplayTag slot)
        {
            if (!slot.IsValid()) return;
            string slotName = slot.TagName;
            if (_equippedItems.TryGetValue(slotName, out var item))
            {
                _equippedItems.Remove(slotName);
                OnEquipmentChanged?.Invoke(slotName, item, null);
            }
        }

        /// <summary>返回是否在指定槽位装备了物品。</summary>
        public bool IsSlotEquipped(GameplayTag slot) => slot.IsValid() && _equippedItems.ContainsKey(slot.TagName);

        /// <summary>当前所有已持握武器（只读视图）。对应 UE5 <c>WieldedWeapons</c>。</summary>
        public IReadOnlyDictionary<string, WeaponItem> WieldedWeapons => _wieldedWeapons;

        /// <summary>
        /// 返回指定持握槽位已持握的武器。
        /// 对应 UE5 <c>UEquipmentComponent::GetWieldedWeaponAtSlot</c>（EquipmentComponent.cpp:85-93）。
        /// </summary>
        public WeaponItem GetWieldedWeaponAtSlot(GameplayTag slot)
        {
            if (slot.IsValid() && _wieldedWeapons.TryGetValue(slot.TagName, out var weapon))
            {
                return weapon;
            }
            return null;
        }

        /// <summary>
        /// 将武器标记为在指定槽位持握。对应 UE5 <c>WieldWeapon</c>（EquipmentComponent.cpp:95-102）。
        /// <para>
        /// 先处理"同槽位已有别的武器"与"同一件武器换手"两种冲突：
        /// 旧武器若不显式 Unwield，会一直留在 WieldedWeapons 表之外却自认持握，
        /// 攻击时会拿着已收起的武器继续打。
        /// </para>
        /// </summary>
        public virtual void WieldWeapon(WeaponItem weapon, GameplayTag wieldSlot)
        {
            if (weapon == null) return;

            if (!wieldSlot.IsValid())
            {
                UnwieldWeapon(weapon.WieldedSlot);
                return;
            }

            // 同槽位的另一件武器：先收起
            if (_wieldedWeapons.TryGetValue(wieldSlot.TagName, out var occupant) &&
                occupant != null && !ReferenceEquals(occupant, weapon))
            {
                occupant.WieldInSlot(GameplayTag.None);
                _wieldedWeapons.Remove(wieldSlot.TagName);
            }

            // 同一件武器挂在别的槽位上：从旧槽位摘掉
            if (weapon.WieldedSlot.IsValid() &&
                weapon.WieldedSlot.TagName != wieldSlot.TagName &&
                _wieldedWeapons.TryGetValue(weapon.WieldedSlot.TagName, out var stale) &&
                ReferenceEquals(stale, weapon))
            {
                _wieldedWeapons.Remove(weapon.WieldedSlot.TagName);
            }

            weapon.WieldInSlot(wieldSlot);
            if (wieldSlot.IsValid())
            {
                _wieldedWeapons[wieldSlot.TagName] = weapon;
            }

            // 持握武器时同步准星子类（对应 UE5 装备蓝图按武器切换 CrosshairWidget）。
            // WeaponItem.CrosshairWidgetPath 此前零消费者；这里经
            // NarrativeRuntimeBootstrap.SetWeaponCrosshair → NarrativeGameplayHUD.SetCrosshair(string)
            // 按类型名（全名/短名，如 NarrativeAimReticleCrosshair）实例化准星控件。
            NarrativePro.CommonUI.NarrativeRuntimeBootstrap.SetWeaponCrosshair(weapon.CrosshairWidgetPath);
        }

        /// <summary>
        /// 将指定持握槽位的武器收回。对应 UE5 <c>UnwieldWeapon</c>（EquipmentComponent.cpp:104-111）。
        /// </summary>
        public virtual void UnwieldWeapon(GameplayTag wieldSlot)
        {
            if (wieldSlot.IsValid() && _wieldedWeapons.TryGetValue(wieldSlot.TagName, out var weapon))
            {
                weapon.WieldInSlot(GameplayTag.None);
                _wieldedWeapons.Remove(wieldSlot.TagName);

                // 收起武器后：准星切回仍持握武器的准星样式，没有则回默认准星。
                string remainingCrosshairPath = "";
                foreach (var remaining in _wieldedWeapons.Values)
                {
                    if (remaining != null && !string.IsNullOrEmpty(remaining.CrosshairWidgetPath))
                    {
                        remainingCrosshairPath = remaining.CrosshairWidgetPath;
                        break;
                    }
                }
                NarrativePro.CommonUI.NarrativeRuntimeBootstrap.SetWeaponCrosshair(remainingCrosshairPath);
            }
        }

        /// <summary>获取所有已装备物品列表。</summary>
        public List<EquippableItem> GetAllEquippedItems()
        {
            return new List<EquippableItem>(_equippedItems.Values);
        }
    }
}

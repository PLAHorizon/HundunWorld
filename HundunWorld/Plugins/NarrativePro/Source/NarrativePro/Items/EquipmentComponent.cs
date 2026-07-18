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

        /// <summary>获取所有已装备物品列表。</summary>
        public List<EquippableItem> GetAllEquippedItems()
        {
            return new List<EquippableItem>(_equippedItems.Values);
        }
    }
}

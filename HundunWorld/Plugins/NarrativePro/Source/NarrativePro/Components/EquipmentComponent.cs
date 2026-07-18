using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.Items;

namespace NarrativePro.Components
{
    /// <summary>
    /// 装备组件，管理角色按槽位装备的物品与持握的武器。
    /// 完整移植自 UE5 NarrativeArsenal: Components/EquipmentComponent.h / .cpp。
    ///
    /// 简化点：
    /// - 移除 UE5 复制（Replication）/RPC 标记，改为本地逻辑 + 事件回调。
    /// - FGameplayTag → NarrativePro.Items.GameplayTag；FGameplayTagContainer → GameplayTagContainer。
    /// - USkeletalMeshComponent → FlaxEngine.AnimatedModel（LeaderPoseComponent / ClothingMeshes）。
    /// - UGroomComponent（毛发系统）Flax 无对应物，GroomComponents 用 object 占位（Flax-不兼容: UE5 的 GroomComponent 在 Flax 无对应物，保留占位）。
    /// - UE5 friend class（UEquippableItem/UWeaponItem 等）在 C# 无对应，原 protected 的
    ///   WieldWeapon/UnwieldWeapon/EquipItem/UnequipItem 改为 public virtual 供物品类调用。
    /// - 注意：项目中已存在 NarrativePro.Items.EquipmentComponent（简化占位版本，保留未删除）。
    ///   本文件为 Components/EquipmentComponent.h 的完整移植，位于 NarrativePro.Components 命名空间。
    /// </summary>
    public class EquipmentComponent : Script
    {
        /// <summary>槽位 → 已装备物品（对应 UE5 EquippedItems）</summary>
        public Dictionary<GameplayTag, EquippableItem> EquippedItems { get; set; } = new Dictionary<GameplayTag, EquippableItem>();

        /// <summary>持握槽位 → 已持握武器（对应 UE5 WieldedWeapons）</summary>
        public Dictionary<GameplayTag, WeaponItem> WieldedWeapons { get; set; } = new Dictionary<GameplayTag, WeaponItem>();

        /// <summary>所有可用的武器收起槽位（对应 UE5 HolsterSlots）</summary>
        public GameplayTagContainer HolsterSlots { get; set; } = CreateDefaultHolsterSlots();

        /// <summary>所有可用的武器持握槽位（对应 UE5 WieldSlots）</summary>
        public GameplayTagContainer WieldSlots { get; set; } = CreateDefaultWieldSlots();

        /// <summary>装备时告知物品跟随的主骨骼网格组件（对应 UE5 LeaderPoseComponent）</summary>
        public AnimatedModel LeaderPoseComponent { get; set; }

        /// <summary>
        /// 角色毛发组件映射（对应 UE5 GroomComponents）。Flax 无 Groom 系统，用 object 占位。
        /// Flax-不兼容: UE5 的 GroomComponent 在 Flax 无对应物，保留占位。原文 TODO: Flax 无毛发系统，需要时用自定义方案替换。
        /// </summary>
        public Dictionary<GameplayTag, object> GroomComponents { get; set; } = new Dictionary<GameplayTag, object>();

        /// <summary>物品装备事件 (槽位, 已装备物品)</summary>
        public event Action<GameplayTag, EquippableItem> OnItemEquipped;

        /// <summary>物品卸下事件 (槽位, 已卸下物品)</summary>
        public event Action<GameplayTag, EquippableItem> OnItemUnequipped;

        private static GameplayTagContainer CreateDefaultHolsterSlots()
        {
            var container = new GameplayTagContainer();
            container.AddTag("Narrative.Equipment.Slot.Weapon.HipLeft");
            container.AddTag("Narrative.Equipment.Slot.Weapon.HipRight");
            container.AddTag("Narrative.Equipment.Slot.Weapon.BackA");
            container.AddTag("Narrative.Equipment.Slot.Weapon.BackB");
            return container;
        }

        private static GameplayTagContainer CreateDefaultWieldSlots()
        {
            var container = new GameplayTagContainer();
            container.AddTag("Narrative.Equipment.WieldSlot.Mainhand");
            container.AddTag("Narrative.Equipment.WieldSlot.Offhand");
            return container;
        }

        /// <summary>
        /// 初始化装备组件，告知它哪些网格组件对应哪个槽位。
        /// 对应 UE5 Initialize(TMap&lt;FGameplayTag, USkeletalMeshComponent*&gt; ClothingMeshes, USkeletalMeshComponent* LeaderPoseComponent)。
        /// </summary>
        /// <param name="clothingMeshes">槽位 → 服装网格组件映射</param>
        /// <param name="leaderPoseComponent">所有装备物品将跟随的主骨骼网格组件</param>
        public virtual void Initialize(Dictionary<GameplayTag, AnimatedModel> clothingMeshes, AnimatedModel leaderPoseComponent)
        {
            LeaderPoseComponent = leaderPoseComponent;
            // 已确认 UE5 原实现仅保存 LeaderPoseComponent；如需按槽位保存服装网格，可在此扩展。
        }

        /// <summary>返回指定槽位已装备的物品。</summary>
        public EquippableItem GetEquippedItemAtSlot(GameplayTag slot)
        {
            if (slot.IsValid() && EquippedItems.TryGetValue(slot, out var item))
            {
                return item;
            }
            return null;
        }

        /// <summary>返回所有匹配给定槽位子标签的已装备物品（slot 为祖先标签）。</summary>
        public List<EquippableItem> GetItemsWithSlot(GameplayTag slot)
        {
            var items = new List<EquippableItem>();
            foreach (var kp in EquippedItems)
            {
                // 等价 UE5 Key.MatchesTag(Slot)：key 是 slot 的子标签或相等
                if (slot.Matches(kp.Key))
                {
                    items.Add(kp.Value);
                }
            }
            return items;
        }

        /// <summary>返回所有已持握的武器。</summary>
        public List<WeaponItem> GetWieldedWeapons()
        {
            return new List<WeaponItem>(WieldedWeapons.Values);
        }

        /// <summary>返回是否双持（持握武器数 &gt; 1）。</summary>
        public virtual bool IsDualWielding()
        {
            return WieldedWeapons.Count > 1;
        }

        /// <summary>返回所有匹配指定 equippableClass 的已装备物品。</summary>
        public void GetEquippedItemsOfClass(Type equippableClass, List<EquippableItem> outEquippables)
        {
            if (equippableClass == null || outEquippables == null) return;
            foreach (var kp in EquippedItems)
            {
                var equippable = kp.Value;
                if (equippable != null && equippableClass.IsAssignableFrom(equippable.GetType()))
                {
                    outEquippables.Add(equippable);
                }
            }
        }

        /// <summary>返回指定武器槽位已装备的武器。</summary>
        public WeaponItem GetEquippedWeaponAtSlot(GameplayTag slot)
        {
            return GetEquippedItemAtSlot(slot) as WeaponItem;
        }

        /// <summary>返回指定持握槽位已持握的武器。</summary>
        public WeaponItem GetWieldedWeaponAtSlot(GameplayTag slot)
        {
            if (slot.IsValid() && WieldedWeapons.TryGetValue(slot, out var weapon))
            {
                return weapon;
            }
            return null;
        }

        /// <summary>
        /// 返回指定槽位的毛发组件。Flax 无 Groom 系统，始终返回 null。
        /// Flax-不兼容: UE5 的 GroomComponent 在 Flax 无对应物，保留占位。原文 TODO: Flax 无毛发系统，需要时用自定义方案实现。
        /// </summary>
        public object GetGroomComponentAtSlot(GameplayTag slot)
        {
            if (slot.IsValid() && GroomComponents.TryGetValue(slot, out var groom))
            {
                return groom;
            }
            return null;
        }

        /// <summary>返回所有已装备物品的总重量。</summary>
        public virtual float GetEquippedItemsWeight()
        {
            float totalWeight = 0f;
            foreach (var kp in EquippedItems)
            {
                if (kp.Value != null)
                {
                    totalWeight += kp.Value.Weight;
                }
            }
            return totalWeight;
        }

        /// <summary>给定一组槽位，返回第一个空闲槽位；若无则返回 GameplayTag.None。</summary>
        public GameplayTag GetFirstFreeSlot(GameplayTagContainer slotsToCheck)
        {
            if (slotsToCheck == null) return GameplayTag.None;
            foreach (var tagName in slotsToCheck.GetTags())
            {
                var tag = new GameplayTag(tagName);
                if (GetEquippedItemAtSlot(tag) == null)
                {
                    return tag;
                }
            }
            return GameplayTag.None;
        }

        /// <summary>将武器标记为在指定槽位持握。</summary>
        public virtual void WieldWeapon(WeaponItem weapon, GameplayTag wieldSlot)
        {
            if (weapon == null || !wieldSlot.IsValid()) return;
            weapon.WieldInSlot(wieldSlot);
            WieldedWeapons[wieldSlot] = weapon;
        }

        /// <summary>将指定持握槽位的武器收回。</summary>
        public virtual void UnwieldWeapon(GameplayTag wieldSlot)
        {
            if (wieldSlot.IsValid() && WieldedWeapons.TryGetValue(wieldSlot, out var weapon))
            {
                weapon.WieldInSlot(GameplayTag.None);
                WieldedWeapons.Remove(wieldSlot);
            }
        }

        /// <summary>将物品装备到指定槽位（槽位由调用方指定，因为武器可装备到多个槽位）。</summary>
        public virtual void EquipItem(EquippableItem equippable, GameplayTag slot)
        {
            if (equippable == null || !slot.IsValid()) return;

            // 若该槽位已有物品，先卸下旧物品
            if (EquippedItems.TryGetValue(slot, out var alreadyEquipped))
            {
                if (alreadyEquipped != null)
                {
                    alreadyEquipped.UnequipItem();
                }
            }

            EquippedItems[slot] = equippable;
            equippable.HandleEquip();
            OnItemEquipped?.Invoke(slot, equippable);
        }

        /// <summary>从指定槽位卸下物品。</summary>
        public virtual void UnequipItem(GameplayTag slot)
        {
            var equippable = GetEquippedItemAtSlot(slot);
            if (equippable != null)
            {
                EquippedItems.Remove(slot);
                equippable.HandleUnequip(slot);
                OnItemUnequipped?.Invoke(slot, equippable);
            }
        }

        /// <summary>返回主骨骼网格组件。</summary>
        public AnimatedModel GetLeaderPoseComponent()
        {
            return LeaderPoseComponent;
        }
    }
}

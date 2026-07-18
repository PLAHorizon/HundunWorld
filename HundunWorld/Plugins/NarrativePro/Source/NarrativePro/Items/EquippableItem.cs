using System;
using System.Collections.Generic;
using System.Linq;
using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.Items
{
    /// <summary>
    /// 装备物品的使用动作（装备到槽位）。
    /// </summary>
    public class UseAction_Equip : NarrativeItemUseAction
    {
        public GameplayTag EquipToSlot { get; set; } = GameplayTag.None;

        public override bool OnUse(NarrativeItem item, NarrativeItem otherItem)
        {
            if (item is EquippableItem equippable)
            {
                return equippable.EquipItem(EquipToSlot);
            }
            return false;
        }

        public override string GetActionDisplayName(NarrativeItem item) => "Equip";
    }

    /// <summary>
    /// 可装备物品基类。玩家可穿戴的物品。适配 UE5 UEquippableItem。
    /// GAS 相关（装备效果/能力）以字符串 ID 引用，待 GAS 阶段填充。
    /// </summary>
    public class EquippableItem : NarrativeItem
    {
        /// <summary>当前装备的槽位</summary>
        public GameplayTag CurrentSlot { get; set; } = GameplayTag.None;

        /// <summary>可装备到的槽位列表</summary>
        public GameplayTagContainer EquippableSlots { get; set; } = new GameplayTagContainer();

        /// <summary>装备时应用的效果 ID（GAS GameplayEffect 等价物）</summary>
        public string EquipmentEffectId { get; set; } = "";

        /// <summary>装备效果按标签的数值（SetByCaller 等价）</summary>
        public Dictionary<string, float> EquipmentEffectValues { get; set; } = new Dictionary<string, float>();

        /// <summary>装备时授予的能力 ID 列表</summary>
        public List<string> EquipmentAbilities { get; set; } = new List<string>();

        // 评分（正在逐步弃用，改用 EquipmentEffectValues）
        public float AttackRating { get; set; } = 0f;
        public float ArmorRating { get; set; } = 0f;
        public float StealthRating { get; set; } = 0f;

        /// <summary>角色形态特定的网格数据 key=形态标签</summary>
        public Dictionary<string, string> FormSpecificMeshData { get; set; } = new Dictionary<string, string>();

        /// <summary>默认网格资源路径</summary>
        public string ClothingMeshPath { get; set; } = "";

        public bool IsEquipped() => CurrentSlot.IsValid();

        public override bool ShouldUseOnAdd() => false;

        public override List<NarrativeItemUseAction> GetItemUseActions()
        {
            var actions = base.GetItemUseActions();
            foreach (var slot in EquippableSlots.GetTags())
            {
                actions.Add(new UseAction_Equip
                {
                    EquipToSlot = new GameplayTag(slot),
                    ActionDisplayName = "Equip",
                    ActionType = EItemUseActionType.Equip
                });
            }
            return actions;
        }

        /// <summary>装备到指定槽位。返回是否成功。</summary>
        public virtual bool EquipItem(GameplayTag desiredSlot)
        {
            if (!EquippableSlots.HasTag(desiredSlot))
            {
                NarrativeLog.LogWarning($"Item '{DisplayName}' cannot equip to slot '{desiredSlot}'");
                return false;
            }

            var equipComp = GetEquipmentComponent();
            if (equipComp == null)
            {
                NarrativeLog.LogWarning($"Item '{DisplayName}' has no equipment component");
                return false;
            }

            GameplayTag oldSlot = CurrentSlot;
            CurrentSlot = desiredSlot;
            HandleEquip();
            ApplyEquipmentAttributes();
            NotifyModified();
            return true;
        }

        /// <summary>卸下物品。</summary>
        public virtual void UnequipItem()
        {
            if (!IsEquipped()) return;
            GameplayTag oldSlot = CurrentSlot;
            CurrentSlot = GameplayTag.None;
            HandleUnequip(oldSlot);
            RemoveEquipmentAttributes();
            NotifyModified();
        }

        public override void Use(NarrativeItem otherItem = null)
        {
            if (IsEquipped()) UnequipItem();
            else EquipItem(EquippableSlots.Count > 0
                ? new GameplayTag(new List<string>(EquippableSlots.GetTags())[0])
                : GameplayTag.None);
        }

        public override void AddedToInventory(NarrativeInventoryComponent inventory, bool fromLoad)
        {
            base.AddedToInventory(inventory, fromLoad);
        }

        public override void RemovedFromInventory(NarrativeInventoryComponent inventory)
        {
            if (IsEquipped()) UnequipItem();
            base.RemovedFromInventory(inventory);
        }

        public override bool ShowActiveInUI() => IsEquipped();

        /// <summary>装备时的处理（默认设置角色网格，可覆盖）。</summary>
        public virtual void HandleEquip() { }

        /// <summary>卸下时的处理。</summary>
        /// <param name="oldSlot">卸下前所在的槽位</param>
        public virtual void HandleUnequip(GameplayTag oldSlot) { }

        /// <summary>应用装备属性（GAS 效果）。</summary>
        public virtual void ApplyEquipmentAttributes() { }

        /// <summary>移除装备属性。</summary>
        public virtual void RemoveEquipmentAttributes() { }

        /// <summary>修改装备效果 Spec（武器可覆盖以添加伤害）。</summary>
        public virtual void ModifyEquipmentEffectSpec(object spec) { }

        public override string GetStringVariable(string variableName)
        {
            switch (variableName)
            {
                case "AttackRating": return AttackRating.ToString("F0");
                case "ArmorRating": return ArmorRating.ToString("F0");
                case "StealthRating": return StealthRating.ToString("F0");
                default: return base.GetStringVariable(variableName);
            }
        }

        /// <summary>获取拥有者的装备组件。</summary>
        protected EquipmentComponent GetEquipmentComponent()
        {
            var pawn = GetOwningPawn();
            // 通过 Actor 查找 EquipmentComponent
            return pawn != null ? pawn.GetScript<EquipmentComponent>() : null;
        }
    }

    /// <summary>
    /// 服装物品，设置角色网格。
    /// </summary>
    public class EquippableItem_Clothing : EquippableItem
    {
        public override void HandleEquip()
        {
            ApplyClothingMesh();
        }

        public override void HandleUnequip(GameplayTag oldSlot)
        {
            // 移除服装网格（恢复默认）
            var pawn = GetOwningPawn();
            if (pawn == null) return;
            // TODO [需接入角色外观系统]: 接入后根据 oldSlot 恢复对应部位默认网格
        }

        /// <summary>应用服装网格到角色。</summary>
        protected virtual void ApplyClothingMesh()
        {
            var pawn = GetOwningPawn();
            if (pawn == null) return;
            // TODO [需接入角色外观系统]: 接入后根据 CurrentSlot 设置对应部位网格
        }
    }
}

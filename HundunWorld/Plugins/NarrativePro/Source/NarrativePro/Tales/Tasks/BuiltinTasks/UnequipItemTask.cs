using System;
using NarrativePro.Core;
using NarrativePro.Items;

namespace NarrativePro.Tales.Tasks
{
    /// <summary>
    /// 「把指定装备脱下来」的任务条目。对应 UE5 蓝图任务 <c>BPT_UnequipItem</c>
    /// （Content/Pro/Core/Tales/Tasks/BPT_UnequipItem.uasset）。
    /// <para>
    /// 二进制证据：与 <c>BPT_EquipItem</c> 同族图（<c>K2_GetPawn</c> →
    /// <c>GetComponentByClass</c>(EquipmentComponent) → <c>GetEquippedItemAtSlot</c> +
    /// <c>ClassIsChildOf</c> + <c>IsValidClass</c>），回溯判定名换成
    /// <c>CheckAlreadyNotEquipped</c>，委托换成 <c>OnItemUnequipped</c>
    /// （<c>OnItemUnequipped__DelegateSignature</c>，自定义事件入参
    /// <c>CustomEvent_Equippable</c> / <c>CustomEvent_Slot</c>），变量
    /// <c>ItemToUnequip</c> / <c>CurrentSlot</c>，命中后 <c>Add_IntInt</c>。
    /// </para>
    /// <para>
    /// 端口差异：<see cref="EquipmentComponent"/> 只有单一的
    /// <c>OnEquipmentChanged(slot, old, new)</c> 广播（卸下时 new 为 null），
    /// 没有 UE5 的 OnItemUnequipped 独立委托，故在此做「new 为空且旧物品是本任务目标」的等价判定。
    /// </para>
    /// </summary>
    [Serializable]
    public class UnequipItemTask : NarrativeTask
    {
        /// <summary>要卸下的物品类 ID。对应 UE5 <c>ItemToUnequip</c>。</summary>
        public string ItemClassId { get; set; } = "";

        /// <summary>槽位标签；留空表示任意槽位上的该物品都算。对应 UE5 <c>Slot</c>。</summary>
        public string SlotTag { get; set; } = "";

        public string ItemDisplayName { get; set; } = "";

        /// <summary>是否在开始时代玩家卸下（默认只等待玩家动作，与 UE5 图一致）。</summary>
        public bool bUnequipOnBegin { get; set; } = false;

        private EquipmentComponent _equipment;

        protected override void OnBeginTask()
        {
            if (string.IsNullOrEmpty(ItemClassId))
            {
                NarrativeLog.LogWarning("UnequipItemTask: ItemClassId 为空，无卸下目标");
                return;
            }

            _equipment = BuiltinTaskSupport.GetEquipment(this);

            // CheckAlreadyNotEquipped：本来就没穿，任务即刻成立（UE5 同口径）。
            if (FindEquipped() == null)
            {
                CompleteTask();
                return;
            }

            if (_equipment != null)
                _equipment.OnEquipmentChanged += HandleEquipmentChanged;

            if (bUnequipOnBegin)
                TryUnequip();

            TickInterval = 0.5f;
        }

        protected override void TickTask()
        {
            if (FindEquipped() == null)
                CompleteTask();
        }

        private EquippableItem FindEquipped()
        {
            var targetSlot = new GameplayTag(SlotTag);

            if (_equipment != null)
            {
                if (targetSlot.IsValid())
                {
                    var atSlot = _equipment.GetEquippedItem(targetSlot);
                    if (atSlot != null && string.Equals(atSlot.ItemClassId, ItemClassId, StringComparison.Ordinal))
                        return atSlot;
                }
                else
                {
                    foreach (var kvp in _equipment.EquippedItems)
                    {
                        if (kvp.Value != null && string.Equals(kvp.Value.ItemClassId, ItemClassId, StringComparison.Ordinal))
                            return kvp.Value;
                    }
                }
            }

            var inInventory = BuiltinTaskSupport.GetInventory(this)?.FindItemOfClass(ItemClassId) as EquippableItem;
            if (inInventory != null && inInventory.IsEquipped() &&
                (!targetSlot.IsValid() || inInventory.CurrentSlot == targetSlot))
                return inInventory;

            return null;
        }

        private void TryUnequip()
        {
            var item = FindEquipped();
            if (item != null)
            {
                item.UnequipItem();
                return;
            }

            var inventory = BuiltinTaskSupport.GetInventory(this);
            if (inventory != null && inventory.FindItemOfClass(ItemClassId) is EquippableItem equipped && equipped.IsEquipped())
                equipped.UnequipItem();
            else
                NarrativeLog.LogWarning($"UnequipItemTask: 没有找到已装备的 '{ItemClassId}'，无法代为卸下");
        }

        private void HandleEquipmentChanged(string slotName, EquippableItem oldItem, EquippableItem newItem)
        {
            if (!bIsActive || newItem != null || oldItem == null) return;
            if (!string.Equals(oldItem.ItemClassId, ItemClassId, StringComparison.Ordinal)) return;

            var targetSlot = new GameplayTag(SlotTag);
            if (targetSlot.IsValid() && !string.Equals(slotName, targetSlot.TagName, StringComparison.Ordinal)) return;

            AddProgress(1);
        }

        protected override void OnEndTask()
        {
            if (_equipment != null)
            {
                _equipment.OnEquipmentChanged -= HandleEquipmentChanged;
                _equipment = null;
            }
        }

        public override string GetTaskDescription()
        {
            if (!string.IsNullOrEmpty(DescriptionOverride)) return DescriptionOverride;
            string name = !string.IsNullOrEmpty(ItemDisplayName) ? ItemDisplayName : ItemClassId;
            return $"脱下 {name}";
        }
    }
}

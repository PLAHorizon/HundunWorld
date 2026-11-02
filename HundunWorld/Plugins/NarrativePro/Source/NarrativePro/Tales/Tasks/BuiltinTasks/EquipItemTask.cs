using System;
using NarrativePro.Core;
using NarrativePro.Items;

namespace NarrativePro.Tales.Tasks
{
    /// <summary>
    /// 「把指定装备穿到身上」的任务条目。对应 UE5 蓝图任务 <c>BPT_EquipItem</c>
    /// （Content/Pro/Core/Tales/Tasks/BPT_EquipItem.uasset）。
    /// <para>
    /// 二进制证据：<c>CallFunc_K2_GetPawn_ReturnValue</c> →
    /// <c>CallFunc_GetComponentByClass_ReturnValue</c>（<c>EquipmentComponent</c>）→
    /// <c>K2Node_CreateDelegate</c> 绑 <c>OnItemEquipped</c>
    /// （<c>OnItemEquipped__DelegateSignature</c>，自定义事件入参
    /// <c>CustomEvent_Equippable</c> / <c>CustomEvent_Slot</c>），并用
    /// <c>CallFunc_GetEquippedItemAtSlot_ReturnValue</c> + <c>ClassIsChildOf</c> 做
    /// <c>CheckAlreadyEquipped</c> 回溯判定，命中后 <c>CallFunc_Add_IntInt_ReturnValue</c>。
    /// 变量 <c>ItemToEquip</c>（<c>TSubclassOf&lt;UNarrativeItem&gt;</c>）与 <c>Slot</c>
    /// （<c>FGameplayTag</c>）在端口换成物品类 ID + 槽位标签字符串。
    /// </para>
    /// <para>
    /// 端口差异：UE5 的 <c>UEquipmentComponent::EquipItem</c> 是蓝图可调的装备入口，端口
    /// <see cref="EquipmentComponent"/> 只暴露 <c>EquipItemToSlot</c>（注释明确「由物品自身调用」），
    /// 因此这里走 <see cref="EquippableItem.EquipItem(GameplayTag)"/>（指定槽位）或
    /// <see cref="NarrativeInventoryComponent.UseItemOfClass"/>（首个可用槽位）这两条真实通路。
    /// </para>
    /// </summary>
    [Serializable]
    public class EquipItemTask : NarrativeTask
    {
        /// <summary>要装备的物品类 ID。对应 UE5 <c>ItemToEquip</c>。</summary>
        public string ItemClassId { get; set; } = "";

        /// <summary>目标槽位标签；留空则由物品自选首个可装备槽位。对应 UE5 <c>Slot</c>。</summary>
        public string SlotTag { get; set; } = "";

        /// <summary>物品显示名（仅用于任务描述，避免为文案去加载物品资产）。</summary>
        public string ItemDisplayName { get; set; } = "";

        /// <summary>是否在开始时自动代玩家穿好（UE5 图里是「等玩家穿」，端口保留两种玩法）。</summary>
        public bool bEquipOnBegin { get; set; } = false;

        private EquipmentComponent _equipment;

        protected override void OnBeginTask()
        {
            if (string.IsNullOrEmpty(ItemClassId))
            {
                NarrativeLog.LogWarning("EquipItemTask: ItemClassId 为空，无装备目标");
                return;
            }

            _equipment = BuiltinTaskSupport.GetEquipment(this);

            if (FindEquipped() != null)
            {
                CompleteTask();
                return;
            }

            if (_equipment != null)
                _equipment.OnEquipmentChanged += HandleEquipmentChanged;

            if (bEquipOnBegin)
                TryEquip();

            // 玩家可能先把装备拾进背包再穿：TickTask 兜住「没有装备变更广播但已满足」的情况。
            TickInterval = 0.5f;
        }

        protected override void TickTask()
        {
            if (FindEquipped() != null)
                CompleteTask();
        }

        /// <summary>
        /// 装备状态查询：先看装备组件的槽位表（穿在身上即算，物品被移出背包时仍成立），
        /// 再看背包里该物品的自述槽位。UE5 用 <c>GetEquippedItemAtSlot</c> +
        /// <c>IsItemClassEquipped</c>，端口两处都读，避免只覆盖一半。
        /// </summary>
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

        private void TryEquip()
        {
            var inventory = BuiltinTaskSupport.GetInventory(this);
            if (inventory == null)
            {
                NarrativeLog.LogWarning("EquipItemTask: 角色没有背包组件，无法装备");
                return;
            }

            if (!(inventory.FindItemOfClass(ItemClassId) is EquippableItem item))
            {
                NarrativeLog.LogWarning($"EquipItemTask: 背包里没有可装备物品 '{ItemClassId}'");
                return;
            }

            var slot = new GameplayTag(SlotTag);
            bool bUsed = slot.IsValid() ? item.EquipItem(slot) : inventory.UseItemOfClass(ItemClassId);
            if (!bUsed)
                NarrativeLog.LogWarning($"EquipItemTask: 装备 '{ItemClassId}' 失败");
        }

        private void HandleEquipmentChanged(string slotName, EquippableItem oldItem, EquippableItem newItem)
        {
            if (!bIsActive || newItem == null) return;
            if (!string.Equals(newItem.ItemClassId, ItemClassId, StringComparison.Ordinal)) return;

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
            var slot = new GameplayTag(SlotTag);
            return slot.IsValid() ? $"装备 {name}（{slot.TagName}）" : $"装备 {name}";
        }
    }
}

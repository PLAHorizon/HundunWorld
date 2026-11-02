using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.Items;

namespace NarrativePro.Tales.Events.Builtin
{
    /// <summary>
    /// 消耗物品事件。
    /// <para>
    /// UE5 依据（Content/Pro/Core/Tales/Events/BPE_ConsumeItem.uasset 符号表）：
    /// CallFunc 含 <c>GetInventoryComponentFromTarget</c>、<c>FindItemByClassExact</c>、
    /// <c>ConsumeItem</c>、<c>Greater_IntInt</c>（数量>0 检查）、<c>IsLocallyControlled</c>，
    /// 并用 <c>Format</c> 拼「需要消耗 X 个 Y 但只有 Z」的失败提示；
    /// C++ 对应 InventoryComponent.h:348/360/400（ConsumeItem/ConsumeItemsOfClass/FindItemByClassExact）。
    /// </para>
    /// <para>
    /// 端口差异：①UE5 图 FindItemByClassExact 只取单一堆再 ConsumeItem，
    /// 端口直接走 <c>ConsumeItemsOfClass</c>（跨同类的多个堆累加消耗），
    /// 与「任务要求 N 个」的口径一致且少一个特例分支；②UE5 的 IsLocallyControlled 门
    /// 是给多人端过滤的，端口事件已在权威侧执行（见 Tales/Nodes/NarrativeNodeBase.cs 的调用链），不再重复判。
    /// </para>
    /// </summary>
    public class ConsumeItemEvent : NarrativeEvent
    {
        /// <summary>物品类标识。对应 UE5 <c>Item</c>（TSoftClassPtr 转字符串）。</summary>
        public string ItemId { get; set; } = "";

        /// <summary>要消耗的数量。对应 UE5 图内 Greater_IntInt 校验的 <c>Quantity</c>。</summary>
        public int Quantity { get; set; } = 1;

        public override void ExecuteEvent(object target, object controller, object narrativeComponent)
        {
            if (string.IsNullOrEmpty(ItemId))
            {
                NarrativeLog.LogWarning("ConsumeItemEvent: ItemId 为空，跳过");
                return;
            }

            // 对应 UE5 Greater_IntInt(Quantity, 0)：非正数量不成立消耗
            if (Quantity <= 0)
            {
                NarrativeLog.LogWarning($"ConsumeItemEvent: 数量 {Quantity} 非正，跳过消耗 '{ItemId}'");
                return;
            }

            var inventory = ResolveInventory(target, controller);
            if (inventory == null)
            {
                NarrativeLog.LogWarning($"ConsumeItemEvent: 目标没有背包组件，无法消耗 '{ItemId}'");
                return;
            }

            var owned = CountOwned(inventory);
            var consumed = inventory.ConsumeItemsOfClass(ItemId, Quantity);
            if (consumed < Quantity)
                NarrativeLog.LogWarning($"ConsumeItemEvent: 需要消耗 {Quantity} 个 '{ItemId}'，实际只有 {owned} 个，已消耗 {consumed}");
        }

        private int CountOwned(NarrativeInventoryComponent inventory)
        {
            var total = 0;
            foreach (var item in inventory.GetItems())
            {
                if (item != null && item.ItemClassId == ItemId)
                    total += item.GetQuantity();
            }
            return total;
        }

        private static NarrativeInventoryComponent ResolveInventory(object target, object controller)
        {
            var character = ArsenalStatics.ResolveCharacterFromContext(target, controller);
            var inventory = character?.GetInventoryComponent();
            if (inventory == null && target is Actor targetActor)
                inventory = InventoryFunctionLibrary.GetInventoryComponentFromTarget(targetActor);
            if (inventory == null && controller is Actor controllerActor)
                inventory = InventoryFunctionLibrary.GetInventoryComponentFromTarget(controllerActor);
            return inventory;
        }

        public override string GetGraphDisplayText() => $"Consume Item: {ItemId} x{Quantity}";
    }
}

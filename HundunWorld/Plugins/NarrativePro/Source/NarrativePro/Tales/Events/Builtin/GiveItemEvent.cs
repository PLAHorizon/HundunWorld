using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.Items;

namespace NarrativePro.Tales.Events.Builtin
{
    /// <summary>
    /// 发放物品事件。对应 UE5 <c>UNarrativeEvent_GiveItem</c>：把指定物品加入目标角色背包。
    /// </summary>
    public class GiveItemEvent : NarrativeEvent
    {
        /// <summary>物品类标识。对应 UE5 <c>Item</c>。</summary>
        public string ItemId { get; set; } = "";

        /// <summary>数量。对应 UE5 <c>Quantity</c>。</summary>
        public int Quantity { get; set; } = 1;

        public override void ExecuteEvent(object target, object controller, object narrativeComponent)
        {
            if (string.IsNullOrEmpty(ItemId))
            {
                NarrativeLog.LogWarning("GiveItemEvent: ItemId 为空，跳过发放");
                return;
            }

            var inventory = ResolveInventory(target, controller);
            if (inventory == null)
            {
                NarrativeLog.LogWarning($"GiveItemEvent: 目标没有背包组件，无法发放 '{ItemId}'");
                return;
            }

            var result = inventory.TryAddItemFromClass(ItemId, Mathf.Max(1, Quantity), true);
            if (result.AddedNoItems)
                NarrativeLog.LogWarning($"GiveItemEvent: 发放 '{ItemId}' x{Quantity} 失败（{result.ErrorText}）");
            else
            {
                // 发放成功后走物品提示框数据源，保证提示与 UI 口径一致
                // 对应 UE5 UNarrativeEvent_GiveItem 发放后经 UItemTooltip 展示获得项
                var tooltip = InventoryFunctionLibrary.CreateItemTooltip(inventory.FindItemOfClass(ItemId));
                if (tooltip != null)
                {
                    NarrativeLog.Log($"GiveItemEvent: {tooltip.GetSummaryText()}");
                }
            }
        }

        private static NarrativeInventoryComponent ResolveInventory(object target, object controller)
        {
            var character = ArsenalStatics.ResolveCharacterFromContext(target, controller);
            var inventory = character?.GetInventoryComponent();
            if (inventory == null && target is Actor targetActor)
                inventory = targetActor.GetScript<NarrativeInventoryComponent>();
            if (inventory == null && controller is Actor controllerActor)
                inventory = controllerActor.GetScript<NarrativeInventoryComponent>();
            return inventory;
        }

        public override string GetGraphDisplayText() => $"Give Item: {ItemId}";
        public override string GetHintText() => "获得物品";
    }
}

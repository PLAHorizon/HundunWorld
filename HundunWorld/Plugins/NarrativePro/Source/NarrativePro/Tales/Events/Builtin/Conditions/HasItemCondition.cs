using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.Items;

namespace NarrativePro.Tales.Events.Builtin.Conditions
{
    /// <summary>
    /// 持有物品条件。对应 UE5 <c>UNarrativeCondition_HasItem</c>：按背包里该类物品的总量判定。
    /// </summary>
    public class HasItemCondition : NarrativeCondition
    {
        /// <summary>物品类标识。对应 UE5 <c>Item</c>（TSubclassOf&lt;UNarrativeItem&gt;）。</summary>
        public string ItemId { get; set; } = "";

        /// <summary>需要的数量。对应 UE5 <c>Quantity</c>。</summary>
        public int RequiredCount { get; set; } = 1;

        /// <summary>为 true 时要求「不足」该数量。对应 UE5 <c>bHasItem</c> 语义位。</summary>
        public bool bItemShouldBePresent { get; set; } = true;

        public override bool CheckCondition(object target, object controller, TalesComponent narrativeComponent)
        {
            if (string.IsNullOrEmpty(ItemId))
            {
                NarrativeLog.LogWarning("HasItemCondition: ItemId 为空，条件按失败处理");
                return false;
            }

            var inventory = ResolveInventory(target, controller);
            if (inventory == null)
                return !bItemShouldBePresent;

            // UE5 用 GetTotalQuantityOfItemClass 而不是 HasItem，避免「至少 1 个」的语义偏差。
            bool enough = inventory.GetTotalQuantityOfItem(ItemId, false) >= RequiredCount;
            return enough == bItemShouldBePresent;
        }

        public override string GetGraphDisplayText() => $"Has Item: {ItemId} x{RequiredCount}";

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
    }
}

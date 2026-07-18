using System.Collections.Generic;
using FlaxEngine;

namespace NarrativePro.Items
{
    /// <summary>
    /// 商人背包组件，拾取/存储物品时消耗/获得货币。
    /// 适配 UE5 UVendorInventoryComponent。
    /// </summary>
    public class VendorInventoryComponent : NarrativeInventoryComponent
    {
        public VendorInventoryComponent()
        {
            bIsVendor = true;
            BuyItemPct = 1f;
            SellItemPct = 0.5f;
        }

        public override bool AllowLootItem(NarrativeInventoryComponent taker, string itemClassId, int quantity, out string errorText)
        {
            errorText = "";
            int price = GetBuyPrice(itemClassId, quantity);
            if (taker.GetCurrency() < price)
            {
                errorText = "货币不足";
                return false;
            }
            if (!HasItem(itemClassId, quantity))
            {
                errorText = "库存不足";
                return false;
            }
            taker.AddCurrency(-price);
            AddCurrency(price);
            return true;
        }

        public override bool AllowStoreItem(NarrativeInventoryComponent storer, string itemClassId, int quantity, out string errorText)
        {
            errorText = "";
            int price = GetSellPrice(itemClassId, quantity);
            if (GetCurrency() < price)
            {
                errorText = "商人货币不足";
                return false;
            }
            AddCurrency(-price);
            storer.AddCurrency(price);
            return true;
        }

        public override int GetBuyPrice(string itemClassId, int quantity = 1)
        {
            return base.GetBuyPrice(itemClassId, quantity);
        }

        public override int GetSellPrice(string itemClassId, int quantity = 1)
        {
            return base.GetSellPrice(itemClassId, quantity);
        }
    }

    /// <summary>
    /// 可交互物品拾取（InteractableItemPickup）适配，将背包挂到可拾取 Actor 上。
    /// </summary>
    public class InteractableItemPickup : NarrativeInventoryComponent
    {
        /// <summary>是否已被拾取完（物品为空）后自动销毁 Actor。</summary>
        public bool bDestroyWhenEmpty { get; set; } = true;

        public void CheckEmptyAndDestroy()
        {
            if (bDestroyWhenEmpty && _items.Count == 0)
            {
                var actor = Actor;
                if (actor != null) Actor.Destroy(actor);
            }
        }
    }
}

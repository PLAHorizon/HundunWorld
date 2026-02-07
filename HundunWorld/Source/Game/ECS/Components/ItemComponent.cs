using Arch.Core;

namespace HundunWorld.Game.ECS.Components
{
    /// <summary>
    /// 物品组件
    /// 标识一个实体是可拾取的物品
    /// </summary>
    public struct ItemComponent 
    {
        public ulong ItemId;
        public int ItemType;
        public string ItemName;
        public int Quantity;
        public bool IsPickupable;
        
        public ItemComponent(ulong itemId, int itemType, string itemName, int quantity = 1)
        {
            ItemId = itemId;
            ItemType = itemType;
            ItemName = itemName;
            Quantity = quantity;
            IsPickupable = true;
        }
    }
}

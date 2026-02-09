using Arch.Core;
using System.Collections.Generic;

namespace HundunWorld.Game.ECS.Components
{
    /// <summary>
    /// 背包物品数据
    /// </summary>
    public struct InventoryItem
    {
        /// <summary>物品ID</summary>
        public ulong ItemId;

        /// <summary>物品模板ID</summary>
        public int TemplateId;

        /// <summary>物品名称</summary>
        public string ItemName;

        /// <summary>物品类型</summary>
        public int ItemType;

        /// <summary>数量</summary>
        public int Count;

        /// <summary>品质</summary>
        public int Quality;

        /// <summary>是否绑定</summary>
        public bool IsBound;

        /// <summary>所在槽位索引</summary>
        public int SlotIndex;

        public InventoryItem(ulong itemId, int templateId, string itemName, int itemType, int count, int quality = 0, bool isBound = false, int slotIndex = -1)
        {
            ItemId = itemId;
            TemplateId = templateId;
            ItemName = itemName;
            ItemType = itemType;
            Count = count;
            Quality = quality;
            IsBound = isBound;
            SlotIndex = slotIndex;
        }
    }

    /// <summary>
    /// 背包组件
    /// 管理实体的物品存储
    /// </summary>
    public struct InventoryComponent
    {
        /// <summary>背包容量</summary>
        public int Capacity;

        /// <summary>背包物品列表</summary>
        public Dictionary<int, InventoryItem> Items;

        /// <summary>下一个可用槽位</summary>
        public int NextSlotIndex;

        public InventoryComponent(int capacity = 60)
        {
            Capacity = capacity;
            Items = new Dictionary<int, InventoryItem>();
            NextSlotIndex = 0;
        }

        /// <summary>
        /// 当前物品数量
        /// </summary>
        public int CurrentCount => Items?.Count ?? 0;

        /// <summary>
        /// 背包是否已满
        /// </summary>
        public bool IsFull => CurrentCount >= Capacity;

        /// <summary>
        /// 尝试添加物品
        /// </summary>
        /// <param name="item">要添加的物品</param>
        /// <returns>是否成功添加</returns>
        public bool TryAddItem(InventoryItem item)
        {
            if (Items == null)
                Items = new Dictionary<int, InventoryItem>();

            if (IsFull)
                return false;

            // 查找是否有可堆叠的同类物品
            foreach (var kvp in Items)
            {
                if (kvp.Value.TemplateId == item.TemplateId && kvp.Value.IsBound == item.IsBound)
                {
                    var existing = kvp.Value;
                    existing.Count += item.Count;
                    Items[kvp.Key] = existing;
                    return true;
                }
            }

            // 添加到新槽位
            item.SlotIndex = NextSlotIndex;
            Items[NextSlotIndex] = item;
            NextSlotIndex++;
            return true;
        }

        /// <summary>
        /// 尝试移除指定模板ID的物品
        /// </summary>
        /// <param name="templateId">物品模板ID</param>
        /// <param name="count">要移除的数量</param>
        /// <returns>是否成功移除</returns>
        public bool TryRemoveItem(int templateId, int count)
        {
            if (Items == null)
                return false;

            foreach (var kvp in Items)
            {
                if (kvp.Value.TemplateId == templateId && kvp.Value.Count >= count)
                {
                    var item = kvp.Value;
                    item.Count -= count;
                    if (item.Count <= 0)
                    {
                        Items.Remove(kvp.Key);
                    }
                    else
                    {
                        Items[kvp.Key] = item;
                    }
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 获取指定模板ID物品的数量
        /// </summary>
        /// <param name="templateId">物品模板ID</param>
        /// <returns>物品数量</returns>
        public int GetItemCount(int templateId)
        {
            if (Items == null)
                return 0;

            int total = 0;
            foreach (var kvp in Items)
            {
                if (kvp.Value.TemplateId == templateId)
                    total += kvp.Value.Count;
            }
            return total;
        }

        /// <summary>
        /// 检查是否有足够数量的指定物品
        /// </summary>
        /// <param name="templateId">物品模板ID</param>
        /// <param name="count">需要的数量</param>
        /// <returns>是否足够</returns>
        public bool HasItem(int templateId, int count)
        {
            return GetItemCount(templateId) >= count;
        }

        /// <summary>
        /// 通过槽位索引获取物品
        /// </summary>
        /// <param name="slotIndex">槽位索引</param>
        /// <param name="item">输出的物品数据</param>
        /// <returns>是否找到</returns>
        public bool TryGetItem(int slotIndex, out InventoryItem item)
        {
            if (Items != null && Items.TryGetValue(slotIndex, out item))
                return true;

            item = default;
            return false;
        }

        /// <summary>
        /// 清空背包
        /// </summary>
        public void Clear()
        {
            Items?.Clear();
            NextSlotIndex = 0;
        }
    }
}

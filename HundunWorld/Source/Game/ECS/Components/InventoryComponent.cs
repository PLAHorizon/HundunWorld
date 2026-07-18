using Arch.Core;
using System.Collections.Generic;
using System.Linq;

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
    /// 已装备的扩展背包装备数据
    /// </summary>
    public struct EquippedBag
    {
        /// <summary>背包槽索引（0-3）</summary>
        public int BagSlotIndex;

        /// <summary>背包装备物品模板ID</summary>
        public int TemplateId;

        /// <summary>该背包提供的扩展格子数</summary>
        public int ExtraSlots;

        public EquippedBag(int bagSlotIndex, int templateId, int extraSlots)
        {
            BagSlotIndex = bagSlotIndex;
            TemplateId = templateId;
            ExtraSlots = extraSlots;
        }
    }

    /// <summary>
    /// 背包组件
    /// 管理实体的物品存储
    /// </summary>
    public struct InventoryComponent
    {
        /// <summary>默认基础容量（6×6 = 36 格）</summary>
        public const int BaseCapacity = 36;

        /// <summary>最大背包槽数量（4 个扩展背包槽）</summary>
        public const int MaxBagSlots = 4;

        /// <summary>总容量上限（基础 36 + 最多 4 个背包）</summary>
        public const int MaxTotalCapacity = 108;

        /// <summary>背包容量（向后兼容字段，实际容量判断请使用 <see cref="TotalCapacity"/>）</summary>
        public int Capacity;

        /// <summary>背包物品列表</summary>
        public Dictionary<int, InventoryItem> Items;

        /// <summary>下一个可用槽位</summary>
        public int NextSlotIndex;

        /// <summary>已装备的扩展背包列表</summary>
        public List<EquippedBag> BagSlots;

        public InventoryComponent(int capacity = 60)
        {
            Capacity = capacity;
            Items = new Dictionary<int, InventoryItem>();
            NextSlotIndex = 0;
            BagSlots = new List<EquippedBag>();
        }

        /// <summary>
        /// 当前物品数量
        /// </summary>
        public int CurrentCount => Items?.Count ?? 0;

        /// <summary>
        /// 总容量（基础容量 + 所有已装备背包提供的扩展格子数）
        /// </summary>
        public int TotalCapacity => BaseCapacity + (BagSlots != null ? BagSlots.Sum(b => b.ExtraSlots) : 0);

        /// <summary>
        /// 背包是否已满（基于 <see cref="TotalCapacity"/> 判断）
        /// </summary>
        public bool IsFull => CurrentCount >= TotalCapacity;

        /// <summary>
        /// 尝试添加物品
        /// </summary>
        /// <param name="item">要添加的物品</param>
        /// <returns>是否成功添加</returns>
        public bool TryAddItem(InventoryItem item)
        {
            if (Items == null)
                Items = new Dictionary<int, InventoryItem>();

            if (BagSlots == null)
                BagSlots = new List<EquippedBag>();

            // 使用 TotalCapacity（基础容量 + 已装备背包扩展格子）判断容量上限
            if (CurrentCount >= TotalCapacity)
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

            // 先检查总数是否足够
            if (GetItemCount(templateId) < count)
                return false;

            int remaining = count;
            var slotsToRemove = new List<int>();

            foreach (var kvp in Items)
            {
                if (kvp.Value.TemplateId != templateId || remaining <= 0)
                    continue;

                var item = kvp.Value;
                if (item.Count <= remaining)
                {
                    remaining -= item.Count;
                    slotsToRemove.Add(kvp.Key);
                }
                else
                {
                    item.Count -= remaining;
                    Items[kvp.Key] = item;
                    remaining = 0;
                }
            }

            foreach (var slot in slotsToRemove)
            {
                Items.Remove(slot);
            }

            return remaining == 0;
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

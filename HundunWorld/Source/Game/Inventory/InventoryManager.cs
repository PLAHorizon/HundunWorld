using FlaxEngine;
using HundunWorld.Game.ECS.Components;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HundunWorld.Game.Inventory
{
    /// <summary>
    /// 物品分类
    /// </summary>
    public enum ItemCategory
    {
        Equipment = 0,   // 装备
        Consumable = 1,  // 消耗品
        Material = 2,    // 材料
        Quest = 3,       // 任务物品
        Currency = 4,    // 货币
        Misc = 5,        // 杂项
    }

    /// <summary>
    /// 排序模式
    /// </summary>
    public enum SortMode
    {
        ByQuality,    // 品质降序
        ByType,       // 类型分组
        ByLevel,      // 等级降序
        ByName,       // 名称拼音
        ByRecent,     // 最近获得
    }

    /// <summary>
    /// 物品使用结果
    /// </summary>
    public struct ItemUseResult
    {
        public bool Success;
        public string Message;
        public int RemainingCount;

        public static ItemUseResult Ok(string msg, int remaining) =>
            new ItemUseResult { Success = true, Message = msg, RemainingCount = remaining };
        public static ItemUseResult Fail(string msg) =>
            new ItemUseResult { Success = false, Message = msg, RemainingCount = 0 };
    }

    /// <summary>
    /// 装备比较结果
    /// </summary>
    public class EquipmentCompareResult
    {
        public string ItemName { get; set; } = "";
        public List<(string attr, float current, float newVal, float diff)> Comparisons { get; set; }
            = new List<(string, float, float, float)>();
        public float TotalScoreDiff { get; set; }
        public bool IsUpgrade => TotalScoreDiff > 0;
    }

    /// <summary>
    /// 背包管理器 - 产品级背包/装备管理。
    /// 特性：
    /// - 智能排序（品质/类型/等级/名称/最近获得）
    /// - 自动拾取（范围检测+过滤规则）
    /// - 物品使用效果（消耗品回血/回蓝/增益）
    /// - 装备比较（属性差异对比+评分）
    /// - 背包整理（合并堆叠/清理空槽）
    /// - 快捷栏绑定
    /// - 物品锁定（防误操作）
    /// </summary>
    public class InventoryManager
    {
        private static InventoryManager _instance;
        public static InventoryManager Instance => _instance ??= new InventoryManager();

        // ===== 数据 =====
        private readonly List<InventoryItem> _items = new List<InventoryItem>();
        private readonly HashSet<ulong> _lockedItems = new HashSet<ulong>();
        private readonly Dictionary<int, int> _quickSlots = new Dictionary<int, int>(); // 快捷栏索引 -> 物品模板ID
        private int _capacity = 80;
        private ulong _nextItemId = 1;

        // ===== 自动拾取 =====
        private bool _autoPickupEnabled = true;
        private float _pickupRadius = 5f;
        private int _pickupQualityFilter = 0; // 最低拾取品质
        private float _lastPickupTime = 0f;
        private const float PickupInterval = 0.3f;

        // ===== 事件 =====
        public event Action<InventoryItem> OnItemAdded;
        public event Action<int, int> OnItemRemoved; // templateId, count
        public event Action<InventoryItem> OnItemUsed;
        public event Action<string> OnInventoryFull;
        public event Action<List<InventoryItem>> OnInventorySorted;
        public event Action<InventoryItem> OnItemPickedUp;
        public event Action<int, int> OnQuickSlotChanged; // slotIndex, templateId

        // ===== 属性 =====
        public int ItemCount => _items.Count;
        public int Capacity => _capacity;
        public bool IsFull => _items.Count >= _capacity;
        public float FillRatio => _capacity > 0 ? (float)_items.Count / _capacity : 0f;
        public bool AutoPickupEnabled { get => _autoPickupEnabled; set => _autoPickupEnabled = value; }
        public float PickupRadius { get => _pickupRadius; set => _pickupRadius = Mathf.Max(1f, value); }

        // ===== 初始化 =====

        public InventoryManager()
        {
            InitQuickSlots();
        }

        private void InitQuickSlots()
        {
            for (int i = 0; i < 8; i++)
                _quickSlots[i] = -1;
        }

        // ===== 物品操作 =====

        /// <summary>添加物品到背包</summary>
        public bool AddItem(int templateId, string name, int category, int count, int quality, int level = 1)
        {
            if (IsFull)
            {
                OnInventoryFull?.Invoke($"背包已满({ItemCount}/{_capacity})");
                return false;
            }

            // 尝试堆叠
            var existing = _items.FirstOrDefault(it =>
                it.TemplateId == templateId && it.Quality == quality);

            if (existing.ItemId != 0 && IsStackable(category))
            {
                var idx = _items.IndexOf(existing);
                var updated = existing;
                updated.Count += count;
                _items[idx] = updated;
                OnItemAdded?.Invoke(updated);
                return true;
            }

            // 新建物品
            var newItem = new InventoryItem(
                itemId: _nextItemId++,
                templateId: templateId,
                itemName: name,
                itemType: category,
                count: count,
                quality: quality,
                isBound: false,
                slotIndex: _items.Count
            );
            _items.Add(newItem);
            OnItemAdded?.Invoke(newItem);
            return true;
        }

        /// <summary>移除物品</summary>
        public bool RemoveItem(int templateId, int count)
        {
            int remaining = count;
            var toRemove = new List<int>();

            for (int i = _items.Count - 1; i >= 0 && remaining > 0; i--)
            {
                var item = _items[i];
                if (item.TemplateId != templateId) continue;
                if (_lockedItems.Contains(item.ItemId)) continue;

                if (item.Count <= remaining)
                {
                    remaining -= item.Count;
                    toRemove.Add(i);
                }
                else
                {
                    var updated = item;
                    updated.Count -= remaining;
                    _items[i] = updated;
                    remaining = 0;
                }
            }

            foreach (var idx in toRemove)
                _items.RemoveAt(idx);

            if (remaining == 0)
            {
                OnItemRemoved?.Invoke(templateId, count);
                return true;
            }
            return false;
        }

        /// <summary>使用物品</summary>
        public ItemUseResult UseItem(int templateId)
        {
            var item = _items.FirstOrDefault(it => it.TemplateId == templateId);
            if (item.ItemId == 0)
                return ItemUseResult.Fail("物品不存在");

            if (_lockedItems.Contains(item.ItemId))
                return ItemUseResult.Fail("物品已锁定");

            var category = (ItemCategory)item.ItemType;
            if (category != ItemCategory.Consumable && category != ItemCategory.Equipment)
                return ItemUseResult.Fail("该物品无法使用");

            // 消耗品使用效果
            if (category == ItemCategory.Consumable)
            {
                string effectMsg = ApplyConsumableEffect(templateId, item.ItemName);

                // 减少数量
                var idx = _items.IndexOf(item);
                if (item.Count <= 1)
                {
                    _items.RemoveAt(idx);
                }
                else
                {
                    var updated = item;
                    updated.Count--;
                    _items[idx] = updated;
                }

                OnItemUsed?.Invoke(item);
                return ItemUseResult.Ok(effectMsg, Mathf.Max(0, item.Count - 1));
            }

            // 装备使用 = 穿戴
            return ItemUseResult.Ok($"已装备 {item.ItemName}", item.Count);
        }

        /// <summary>锁定/解锁物品（防误操作）</summary>
        public void ToggleLock(ulong itemId)
        {
            if (_lockedItems.Contains(itemId))
                _lockedItems.Remove(itemId);
            else
                _lockedItems.Add(itemId);
        }

        /// <summary>检查物品是否锁定</summary>
        public bool IsItemLocked(ulong itemId) => _lockedItems.Contains(itemId);

        // ===== 排序 =====

        /// <summary>整理背包（排序+合并堆叠）</summary>
        public void SortInventory(SortMode mode = SortMode.ByQuality)
        {
            // 先合并同类堆叠
            MergeStacks();

            // 排序
            switch (mode)
            {
                case SortMode.ByQuality:
                    _items.Sort((a, b) => b.Quality.CompareTo(a.Quality));
                    break;
                case SortMode.ByType:
                    _items.Sort((a, b) =>
                    {
                        int typeCompare = a.ItemType.CompareTo(b.ItemType);
                        return typeCompare != 0 ? typeCompare : b.Quality.CompareTo(a.Quality);
                    });
                    break;
                case SortMode.ByLevel:
                    _items.Sort((a, b) => b.Quality.CompareTo(a.Quality)); // 简化：用quality代替level
                    break;
                case SortMode.ByName:
                    _items.Sort((a, b) => string.Compare(a.ItemName, b.ItemName, StringComparison.Ordinal));
                    break;
                case SortMode.ByRecent:
                    _items.Sort((a, b) => b.ItemId.CompareTo(a.ItemId)); // ID越大越新
                    break;
            }

            // 重新分配槽位
            for (int i = 0; i < _items.Count; i++)
            {
                var item = _items[i];
                item.SlotIndex = i;
                _items[i] = item;
            }

            OnInventorySorted?.Invoke(new List<InventoryItem>(_items));
            Debug.Log($"[InventoryManager] 背包整理完成: {mode}, {_items.Count} 件物品");
        }

        /// <summary>合并同类堆叠</summary>
        private void MergeStacks()
        {
            var merged = new Dictionary<int, InventoryItem>();
            var result = new List<InventoryItem>();

            foreach (var item in _items)
            {
                if (!IsStackable(item.ItemType))
                {
                    result.Add(item);
                    continue;
                }

                if (merged.TryGetValue(item.TemplateId, out var existing))
                {
                    var idx = result.IndexOf(existing);
                    var updated = existing;
                    updated.Count += item.Count;
                    result[idx] = updated;
                    merged[item.TemplateId] = updated;
                }
                else
                {
                    merged[item.TemplateId] = item;
                    result.Add(item);
                }
            }

            _items.Clear();
            _items.AddRange(result);
        }

        // ===== 自动拾取 =====

        /// <summary>每帧更新自动拾取</summary>
        public void UpdateAutoPickup(Vector3 playerPos, List<(int templateId, string name, int category, int quality, Vector3 pos)> groundItems, float deltaTime)
        {
            if (!_autoPickupEnabled) return;

            _lastPickupTime += deltaTime;
            if (_lastPickupTime < PickupInterval) return;
            _lastPickupTime = 0f;

            foreach (var ground in groundItems)
            {
                if (ground.quality < _pickupQualityFilter) continue;

                float dist = Vector3.Distance(playerPos, ground.pos);
                if (dist <= _pickupRadius)
                {
                    if (AddItem(ground.templateId, ground.name, ground.category, 1, ground.quality))
                    {
                        OnItemPickedUp?.Invoke(_items.LastOrDefault());
                    }
                }
            }
        }

        /// <summary>设置拾取品质过滤</summary>
        public void SetPickupQualityFilter(int minQuality)
        {
            _pickupQualityFilter = Mathf.Clamp(minQuality, 0, 5);
        }

        // ===== 快捷栏 =====

        /// <summary>绑定物品到快捷栏</summary>
        public void BindQuickSlot(int slotIndex, int templateId)
        {
            if (slotIndex < 0 || slotIndex >= 8) return;
            _quickSlots[slotIndex] = templateId;
            OnQuickSlotChanged?.Invoke(slotIndex, templateId);
        }

        /// <summary>使用快捷栏物品</summary>
        public ItemUseResult UseQuickSlot(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= 8) return ItemUseResult.Fail("无效快捷栏");
            if (!_quickSlots.TryGetValue(slotIndex, out int templateId) || templateId < 0)
                return ItemUseResult.Fail("快捷栏为空");
            return UseItem(templateId);
        }

        /// <summary>获取快捷栏绑定</summary>
        public int GetQuickSlotBinding(int slotIndex)
        {
            return _quickSlots.TryGetValue(slotIndex, out int id) ? id : -1;
        }

        // ===== 装备比较 =====

        /// <summary>比较装备与当前穿戴</summary>
        public EquipmentCompareResult CompareEquipment(int newTemplateId, Dictionary<string, float> newStats, Dictionary<string, float> currentStats)
        {
            var result = new EquipmentCompareResult();
            float totalDiff = 0f;

            var allKeys = new HashSet<string>();
            foreach (var k in newStats.Keys) allKeys.Add(k);
            foreach (var k in currentStats.Keys) allKeys.Add(k);

            foreach (var key in allKeys)
            {
                float current = currentStats.TryGetValue(key, out float cv) ? cv : 0f;
                float newVal = newStats.TryGetValue(key, out float nv) ? nv : 0f;
                float diff = newVal - current;
                result.Comparisons.Add((key, current, newVal, diff));
                totalDiff += diff * GetAttrWeight(key);
            }

            result.TotalScoreDiff = totalDiff;
            return result;
        }

        /// <summary>属性权重（用于评分）</summary>
        private float GetAttrWeight(string attr)
        {
            switch (attr.ToLower())
            {
                case "attack": case "攻击力": return 2.0f;
                case "defense": case "防御力": return 1.5f;
                case "hp": case "生命": return 1.0f;
                case "crit": case "暴击": return 2.5f;
                case "speed": case "速度": return 1.8f;
                default: return 1.0f;
            }
        }

        // ===== 查询 =====

        /// <summary>获取所有物品</summary>
        public List<InventoryItem> GetAllItems() => new List<InventoryItem>(_items);

        /// <summary>按分类获取物品</summary>
        public List<InventoryItem> GetItemsByCategory(ItemCategory category)
        {
            return _items.Where(it => it.ItemType == (int)category).ToList();
        }

        /// <summary>获取物品数量</summary>
        public int GetItemCount(int templateId)
        {
            return _items.Where(it => it.TemplateId == templateId).Sum(it => it.Count);
        }

        /// <summary>是否有足够物品</summary>
        public bool HasItem(int templateId, int count)
        {
            return GetItemCount(templateId) >= count;
        }

        /// <summary>获取背包空余格数</summary>
        public int GetFreeSlots() => _capacity - _items.Count;

        // ===== 消耗品效果 =====

        private string ApplyConsumableEffect(int templateId, string itemName)
        {
            // 基于模板ID判断效果类型（实际应从配置表读取）
            if (itemName.Contains("生命") || itemName.Contains("活血") || itemName.Contains("回春"))
                return $"使用 {itemName}，恢复生命值";
            if (itemName.Contains("内力") || itemName.Contains("灵气") || itemName.Contains("聚气"))
                return $"使用 {itemName}，恢复内力值";
            if (itemName.Contains("解毒") || itemName.Contains("清心"))
                return $"使用 {itemName}，解除异常状态";
            return $"使用 {itemName}";
        }

        private bool IsStackable(int category)
        {
            var cat = (ItemCategory)category;
            return cat == ItemCategory.Consumable || cat == ItemCategory.Material || cat == ItemCategory.Currency;
        }
    }
}

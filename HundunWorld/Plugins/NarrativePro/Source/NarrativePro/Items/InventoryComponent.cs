using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.Items
{
    /// <summary>
    /// 背包组件，管理 NarrativeItem 集合。玩家应挂到 PlayerState，可拾取物品（如箱子）挂到对应 Actor。
    /// 适配 UE5 UNarrativeInventoryComponent，移除复制/RPC，改为本地逻辑 + 事件回调。
    /// </summary>
    public class NarrativeInventoryComponent : Script
    {
        protected bool bIsLoading = false;
        protected List<NarrativeItem> _items = new List<NarrativeItem>();
        protected Dictionary<Guid, NarrativeItem> _itemGuidMap = new Dictionary<Guid, NarrativeItem>();
        protected HashSet<NarrativeItem> _tickItems = new HashSet<NarrativeItem>();
        protected int _currency = 0;

        /// <summary>背包友好名（UI 使用）</summary>
        public string InventoryFriendlyName { get; set; } = "Inventory";

        /// <summary>最大承重（kg）</summary>
        public float WeightCapacity { get; set; } = 100f;

        /// <summary>最大物品数</summary>
        public int Capacity { get; set; } = 100;

        /// <summary>当前拾取源</summary>
        public NarrativeInventoryComponent LootSource { get; protected set; }

        /// <summary>BeginPlay 时自动添加的默认物品（类 ID + 数量）</summary>
        public List<ItemWithQuantity> DefaultItems { get; set; } = new List<ItemWithQuantity>();

        /// <summary>默认战利品表</summary>
        public List<LootTableRoll> DefaultItemTables { get; set; } = new List<LootTableRoll>();

        /// <summary>是否已发放默认物品</summary>
        public bool bGaveDefaultItems { get; set; } = false;

        /// <summary>是否为商人</summary>
        public bool bIsVendor { get; set; } = false;

        /// <summary>商人收购价百分比</summary>
        public float BuyItemPct { get; set; } = 1f;

        /// <summary>商人出售价百分比</summary>
        public float SellItemPct { get; set; } = 1f;

        // ===== 事件 =====
        public event Action<NarrativeInventoryComponent> OnInventoryUpdated;
        public event Action<NarrativeInventoryComponent, ItemAddResult> OnItemAdded;
        public event Action<NarrativeInventoryComponent, NarrativeItem, int> OnItemRemoved;
        public event Action<NarrativeInventoryComponent, NarrativeItem> OnItemUsed;
        public event Action<NarrativeInventoryComponent, NarrativeInventoryComponent> OnBeginLooting;
        public event Action<NarrativeInventoryComponent> OnEndLooting;
        public event Action<NarrativeInventoryComponent, int, int> OnCurrencyChanged;

        // ===== 公开属性 =====
        public bool IsLoading() => bIsLoading;
        public IReadOnlyList<NarrativeItem> Items => _items;
        public int GetCurrency() => _currency;
        public float GetWeightCapacity() => WeightCapacity;
        public int GetCapacity() => Capacity;

        /// <summary>当前背包重量。</summary>
        public float GetCurrentWeight()
        {
            float w = 0f;
            foreach (var i in _items) w += i.GetStackWeight();
            return w;
        }

        /// <summary>当前物品堆叠数。</summary>
        public int GetItemCount() => _items.Count;

        /// <summary>拥有者 Pawn（Actor）。</summary>
        public virtual Actor GetOwningPawn()
        {
            // InventoryComponent 通常挂在 PlayerState/Actor 上，向上查找
            var actor = Actor;
            return actor;
        }

        /// <summary>拥有者 Controller。</summary>
        public virtual object GetOwningController() => null;

        public override void OnEnable()
        {
            base.OnEnable();
            if (!bGaveDefaultItems && DefaultItems != null && DefaultItems.Count > 0)
            {
                // 默认物品在 BeginPlay 后由 GiveDefaultItems 显式调用，这里不自动调用
            }
        }

        public override void OnDisable()
        {
            StopLooting();
            base.OnDisable();
        }

        public override void OnUpdate()
        {
            base.OnUpdate();
            if (_tickItems.Count == 0) return;
            float dt = Time.DeltaTime;
            // 复制以避免遍历时修改
            var toTick = _tickItems.ToList();
            foreach (var item in toTick)
            {
                item.TickItem(dt);
            }
        }

        // ===== 添加物品 =====

        /// <summary>通过物品类 ID 添加物品到背包。</summary>
        /// <param name="itemClassId">物品类 ID</param>
        /// <param name="quantity">数量</param>
        /// <param name="bCheckAutoUse">是否检查自动使用</param>
        public virtual ItemAddResult TryAddItemFromClass(string itemClassId, int quantity = 1, bool bCheckAutoUse = true)
        {
            return TryAddItem_Internal(itemClassId, quantity, bCheckAutoUse);
        }

        /// <summary>内部添加物品实现。</summary>
        protected virtual ItemAddResult TryAddItem_Internal(string itemClassId, int quantity, bool bCheckAutoUse = true)
        {
            if (string.IsNullOrEmpty(itemClassId) || quantity <= 0)
                return ItemAddResult.AddedNone(quantity, "无效物品或数量");

            string noSpaceReason;
            int space = GetSpaceForItem(itemClassId, out noSpaceReason);
            if (space <= 0)
                return ItemAddResult.AddedNone(quantity, noSpaceReason);

            int amountToAdd = Math.Min(quantity, space);
            var createdStacks = new List<NarrativeItem>();
            int remaining = amountToAdd;

            // 先尝试堆叠到现有堆
            if (!string.IsNullOrEmpty(itemClassId))
            {
                foreach (var item in _items)
                {
                    if (remaining <= 0) break;
                    if (item.ItemClassId == itemClassId && !item.IsStackFull())
                    {
                        int canAdd = Math.Min(item.GetStackSpace(), remaining);
                        item.SetQuantity(item.GetQuantity() + canAdd);
                        remaining -= canAdd;
                        if (!createdStacks.Contains(item)) createdStacks.Add(item);
                    }
                }
            }

            // 创建新堆叠
            while (remaining > 0 && _items.Count < Capacity)
            {
                var item = ItemFactory.CreateItem(itemClassId);
                if (item == null) break;
                int maxStack = item.GetMaxStackSize();
                int stackAmount = Math.Min(maxStack, remaining);
                item.SetQuantity(stackAmount);
                item.ItemClassId = itemClassId;
                AddItemInternal(item);
                createdStacks.Add(item);
                remaining -= stackAmount;
            }

            int amountGiven = amountToAdd - remaining;
            foreach (var item in createdStacks)
            {
                item.AddedToInventory(this, false);
            }

            ItemAddResult result;
            if (amountGiven == quantity)
                result = ItemAddResult.AddedAll(createdStacks, quantity);
            else if (amountGiven > 0)
                result = ItemAddResult.AddedSome(createdStacks, quantity, amountGiven, "容量或重量不足");
            else
                result = ItemAddResult.AddedNone(quantity, "无法添加物品");

            result.ItemClassId = itemClassId;
            OnItemAdded?.Invoke(this, result);
            NotifyInventoryUpdated();
            return result;
        }

        /// <summary>内部添加物品实例（不做校验）。</summary>
        protected virtual NarrativeItem AddItemInternal(NarrativeItem item)
        {
            if (item == null) return null;
            _items.Add(item);
            _itemGuidMap[item.ItemGUID] = item;
            return item;
        }

        // ===== 消耗/移除 =====

        /// <summary>从指定物品堆消耗数量，归零则移除。返回实际消耗数量。</summary>
        public virtual int ConsumeItem(NarrativeItem item, int quantity = 1)
        {
            if (item == null || quantity <= 0) return 0;
            if (!_items.Contains(item)) return 0;

            int consumed = Math.Min(item.GetQuantity(), quantity);
            item.SetQuantity(item.GetQuantity() - consumed);

            if (item.GetQuantity() <= 0)
            {
                RemoveItem(item);
            }
            else
            {
                NotifyInventoryUpdated();
            }
            return consumed;
        }

        /// <summary>从所有匹配类的堆中消耗数量，归零则移除。返回实际消耗数量。</summary>
        public virtual int ConsumeItemsOfClass(string itemClassId, int quantity = 1)
        {
            if (string.IsNullOrEmpty(itemClassId) || quantity <= 0) return 0;
            int remaining = quantity;
            int consumed = 0;
            var toRemove = new List<NarrativeItem>();
            foreach (var item in _items.ToList())
            {
                if (remaining <= 0) break;
                if (item.ItemClassId != itemClassId) continue;
                int canTake = Math.Min(item.GetQuantity(), remaining);
                item.SetQuantity(item.GetQuantity() - canTake);
                remaining -= canTake;
                consumed += canTake;
                if (item.GetQuantity() <= 0) toRemove.Add(item);
            }
            foreach (var r in toRemove) RemoveItem(r);
            if (consumed > 0) NotifyInventoryUpdated();
            return consumed;
        }

        /// <summary>从背包移除物品。返回是否成功。</summary>
        public virtual bool RemoveItem(NarrativeItem item)
        {
            if (item == null || !_items.Remove(item)) return false;
            _itemGuidMap.Remove(item.ItemGUID);
            _tickItems.Remove(item);
            item.RemovedFromInventory(this);
            OnItemRemoved?.Invoke(this, item, 1);
            NotifyInventoryUpdated();
            return true;
        }

        // ===== 查询 =====

        /// <summary>是否拥有指定数量物品（包含子类）。</summary>
        public virtual bool HasItem(string itemClassId, int quantity = 1, bool bCheckVisibility = false)
        {
            return GetTotalQuantityOfItem(itemClassId, bCheckVisibility) >= quantity;
        }

        /// <summary>精确匹配类（不含子类）。</summary>
        public virtual bool HasItemExact(string itemClassId, int quantity = 1, bool bCheckVisibility = false)
        {
            return GetTotalQuantityOfItemExact(itemClassId, bCheckVisibility) >= quantity;
        }

        /// <summary>所有匹配类物品的总数量（含子类）。</summary>
        public virtual int GetTotalQuantityOfItem(string itemClassId, bool bCheckVisibility = false)
        {
            int total = 0;
            foreach (var item in _items)
            {
                if (bCheckVisibility && !item.ShouldShowInInventory()) continue;
                // 简单按 ItemClassId 匹配（无子类继承判断，因为此处为字符串 ID）
                if (item.ItemClassId == itemClassId)
                    total += item.GetQuantity();
            }
            return total;
        }

        /// <summary>精确类匹配的总数量。</summary>
        public virtual int GetTotalQuantityOfItemExact(string itemClassId, bool bCheckVisibility = false)
        {
            return GetTotalQuantityOfItem(itemClassId, bCheckVisibility);
        }

        /// <summary>按 GUID 查找物品。</summary>
        public NarrativeItem FindItemByGUID(Guid itemGuid)
        {
            return _itemGuidMap.TryGetValue(itemGuid, out var item) ? item : null;
        }

        /// <summary>返回第一个匹配类的物品。</summary>
        public virtual NarrativeItem FindItemOfClass(string itemClassId, bool bCheckVisibility = false)
        {
            foreach (var item in _items)
            {
                if (bCheckVisibility && !item.ShouldShowInInventory()) continue;
                if (item.ItemClassId == itemClassId) return item;
            }
            return null;
        }

        /// <summary>返回所有匹配类的物品。</summary>
        public virtual List<NarrativeItem> FindItemsOfClass(string itemClassId, bool bCheckVisibility = false)
        {
            var result = new List<NarrativeItem>();
            foreach (var item in _items)
            {
                if (bCheckVisibility && !item.ShouldShowInInventory()) continue;
                if (item.ItemClassId == itemClassId) result.Add(item);
            }
            return result;
        }

        /// <summary>返回指定物品可作用的所有物品。</summary>
        public bool GetItemsUsableWith(NarrativeItem item, List<NarrativeItem> outItems, bool bCheckVisibility = false)
        {
            if (item == null || outItems == null) return false;
            bool found = false;
            foreach (var other in _items)
            {
                if (other == item) continue;
                if (bCheckVisibility && !other.ShouldShowInInventory()) continue;
                if (item.CanUseItemWith(other))
                {
                    outItems.Add(other);
                    found = true;
                }
            }
            return found;
        }

        /// <summary>返回指定物品还能添加多少（基于重量/容量/堆叠）。</summary>
        public virtual int GetSpaceForItem(string itemClassId, out string noSpaceReason)
        {
            noSpaceReason = "";
            if (_items.Count >= Capacity)
            {
                // 还可通过堆叠添加
                int stackSpace = 0;
                foreach (var item in _items)
                {
                    if (item.ItemClassId == itemClassId)
                        stackSpace += item.GetStackSpace();
                }
                if (stackSpace <= 0)
                {
                    noSpaceReason = "容量已满";
                    return 0;
                }
                return stackSpace;
            }

            // 重量检查
            var sample = ItemFactory.CreateItem(itemClassId);
            float itemWeight = sample?.Weight ?? 0f;
            float remainingWeight = WeightCapacity - GetCurrentWeight();
            if (itemWeight > 0f && remainingWeight < itemWeight)
            {
                noSpaceReason = "重量不足";
                return 0;
            }

            int maxStack = sample?.GetMaxStackSize() ?? 1;
            int availableSlots = Capacity - _items.Count;
            int existingStackSpace = 0;
            foreach (var item in _items)
            {
                if (item.ItemClassId == itemClassId)
                    existingStackSpace += item.GetStackSpace();
            }

            int space = existingStackSpace + availableSlots * maxStack;
            if (itemWeight > 0f)
            {
                int weightLimited = (int)(remainingWeight / itemWeight);
                space = Math.Min(space, weightLimited);
            }
            return space;
        }

        /// <summary>获取所有物品列表副本。</summary>
        public List<NarrativeItem> GetItems() => new List<NarrativeItem>(_items);

        // ===== 货币 =====

        public virtual void AddCurrency(int amount)
        {
            if (amount == 0) return;
            int old = _currency;
            _currency = Math.Max(0, _currency + amount);
            OnCurrencyChanged?.Invoke(this, old, _currency);
            NotifyInventoryUpdated();
        }

        public virtual void SetCurrency(int amount)
        {
            int old = _currency;
            _currency = Math.Max(0, amount);
            OnCurrencyChanged?.Invoke(this, old, _currency);
            NotifyInventoryUpdated();
        }

        // ===== 容量设置 =====

        public void SetWeightCapacity(float newWeightCapacity) { WeightCapacity = Math.Max(0, newWeightCapacity); NotifyInventoryUpdated(); }
        public void SetCapacity(int newCapacity) { Capacity = Math.Max(0, newCapacity); NotifyInventoryUpdated(); }
        public void SetInventoryFriendlyName(string name) { InventoryFriendlyName = name; NotifyInventoryUpdated(); }
        public void SetIsVendor(bool newIsVendor) { bIsVendor = newIsVendor; NotifyInventoryUpdated(); }
        public void SetBuyPercentage(float pct) { BuyItemPct = pct; }
        public void SetSellPercentage(float pct) { SellItemPct = pct; }

        // ===== 默认物品 =====

        /// <summary>发放默认物品（仅一次）。</summary>
        public virtual void GiveDefaultItems()
        {
            if (bGaveDefaultItems) return;
            bGaveDefaultItems = true;

            foreach (var def in DefaultItems)
            {
                TryAddItemFromClass(def.ItemClassId, def.Quantity, true);
            }

            if (DefaultItemTables != null)
            {
                var results = new List<ItemAddResult>();
                foreach (var roll in DefaultItemTables)
                {
                    TryAddFromLootTable(roll, results);
                }
            }
        }

        // ===== 战利品表 =====

        /// <summary>按战利品表滚动添加物品。</summary>
        public virtual void TryAddFromLootTable(LootTableRoll lootTable, List<ItemAddResult> outResults)
        {
            if (lootTable == null || !lootTable.CanRoll()) return;

            var rng = new Random();
            for (int i = 0; i < lootTable.NumRolls; i++)
            {
                if (lootTable.Chance < 1f && rng.NextDouble() > lootTable.Chance) continue;

                // 必定授予的物品
                foreach (var grant in lootTable.ItemsToGrant)
                {
                    outResults.Add(TryAddItemFromClass(grant.ItemClassId, grant.Quantity, false));
                }

                // 物品集合
                foreach (var collectionId in lootTable.ItemCollectionsToGrant)
                {
                    var collection = LoadItemCollection(collectionId);
                    if (collection != null)
                    {
                        foreach (var item in collection.Items)
                        {
                            outResults.Add(TryAddItemFromClass(item.ItemClassId, item.Quantity, false));
                        }
                    }
                }

                // 子表
                if (lootTable.SubTablesToRoll != null)
                {
                    foreach (var sub in lootTable.SubTablesToRoll)
                    {
                        TryAddFromLootTable(sub, outResults);
                    }
                }
            }
        }

        /// <summary>加载物品集合（可由子类覆盖从资产系统加载）。</summary>
        protected virtual ItemCollection LoadItemCollection(string collectionId)
        {
            // 默认从 JSON 文件加载
            try
            {
                var settings = NarrativeProPlugin.Instance?.NarrativeSettings;
                string path = System.IO.Path.Combine(
                    settings?.DefaultDataTaskDirectory ?? "Content/NarrativePro/DataTasks",
                    "ItemCollections",
                    collectionId + ".json");
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    return JsonSerializer.Deserialize<ItemCollection>(json);
                }
            }
            catch (Exception ex)
            {
                NarrativeLog.LogWarning($"加载物品集合 '{collectionId}' 失败: {ex.Message}");
            }
            return null;
        }

        // ===== 拾取/存储 =====

        /// <summary>设置拾取源。</summary>
        public virtual void SetLootSource(NarrativeInventoryComponent newLootSource)
        {
            if (LootSource == newLootSource) return;
            if (LootSource != null)
            {
                LootSource.OnEndLooting?.Invoke(LootSource);
            }
            LootSource = newLootSource;
            if (newLootSource != null)
            {
                OnBeginLooting?.Invoke(this, newLootSource);
            }
        }

        /// <summary>停止拾取。</summary>
        public virtual void StopLooting()
        {
            if (LootSource != null)
            {
                var src = LootSource;
                LootSource = null;
                OnEndLooting?.Invoke(this);
            }
        }

        /// <summary>请求拾取物品。返回是否请求成功。</summary>
        public virtual bool RequestLootItem(NarrativeItem itemToLoot, out string errorText, int quantity = 1)
        {
            errorText = "";
            if (itemToLoot == null || LootSource == null)
            {
                errorText = "无效物品或拾取源";
                return false;
            }
            if (!LootSource.AllowLootItem(this, itemToLoot.ItemClassId, quantity, out errorText))
                return false;
            PerformLootItem(LootSource, itemToLoot.ItemClassId, quantity);
            return true;
        }

        /// <summary>请求存储物品。返回是否请求成功。</summary>
        public virtual bool RequestStoreItem(NarrativeItem itemToStore, out string errorText, int quantity = 1)
        {
            errorText = "";
            if (itemToStore == null || LootSource == null)
            {
                errorText = "无效物品或存储目标";
                return false;
            }
            if (!LootSource.AllowStoreItem(this, itemToStore.ItemClassId, quantity, out errorText))
                return false;
            PerformStoreItem(LootSource, itemToStore.ItemClassId, quantity);
            return true;
        }

        /// <summary>从 taker 拾取物品到本背包。</summary>
        public virtual ItemAddResult PerformLootItem(NarrativeInventoryComponent taker, string itemClassId, int quantity = 1)
        {
            // 从 taker 移除并添加到 self（this 是拾取者）
            var result = TryAddItemFromClass(itemClassId, quantity, false);
            if (result.AmountGiven > 0)
            {
                taker.ConsumeItemsOfClass(itemClassId, result.AmountGiven);
            }
            return result;
        }

        /// <summary>存储物品到 storer（this 是存储者）。</summary>
        public virtual ItemAddResult PerformStoreItem(NarrativeInventoryComponent storer, string itemClassId, int quantity = 1)
        {
            // 从 self 移除并添加到 storer
            var result = storer.TryAddItemFromClass(itemClassId, quantity, false);
            if (result.AmountGiven > 0)
            {
                ConsumeItemsOfClass(itemClassId, result.AmountGiven);
            }
            return result;
        }

        /// <summary>是否允许 taker 拾取物品。</summary>
        public virtual bool AllowLootItem(NarrativeInventoryComponent taker, string itemClassId, int quantity, out string errorText)
        {
            errorText = "";
            if (bIsVendor)
            {
                int price = GetBuyPrice(itemClassId, quantity);
                if (taker.GetCurrency() < price)
                {
                    errorText = "货币不足";
                    return false;
                }
                taker.AddCurrency(-price);
                AddCurrency(price);
            }
            return HasItem(itemClassId, quantity);
        }

        /// <summary>是否允许 storer 存储物品。</summary>
        public virtual bool AllowStoreItem(NarrativeInventoryComponent storer, string itemClassId, int quantity, out string errorText)
        {
            errorText = "";
            if (bIsVendor)
            {
                int price = GetSellPrice(itemClassId, quantity);
                if (GetCurrency() < price)
                {
                    errorText = "商人货币不足";
                    return false;
                }
                AddCurrency(-price);
                storer.AddCurrency(price);
            }
            return true;
        }

        /// <summary>收购价。</summary>
        public virtual int GetBuyPrice(string itemClassId, int quantity = 1)
        {
            var sample = ItemFactory.CreateItem(itemClassId);
            int baseValue = sample?.BaseValue ?? 0;
            return (int)(baseValue * BuyItemPct * quantity);
        }

        /// <summary>出售价。</summary>
        public virtual int GetSellPrice(string itemClassId, int quantity = 1)
        {
            var sample = ItemFactory.CreateItem(itemClassId);
            int baseValue = sample?.BaseValue ?? 0;
            return (int)(baseValue * SellItemPct * quantity);
        }

        // ===== Tick 物品 =====

        /// <summary>注册/注销需要 Tick 的物品。</summary>
        public void SetItemTickEnabled(NarrativeItem item, bool enabled)
        {
            if (item == null) return;
            if (enabled) _tickItems.Add(item);
            else _tickItems.Remove(item);
        }

        // ===== 存档 =====

        /// <summary>准备存档数据。</summary>
        public virtual List<SavedItem> PrepareForSave()
        {
            var saved = new List<SavedItem>();
            foreach (var item in _items)
            {
                saved.Add(new SavedItem
                {
                    ItemClassId = item.ItemClassId,
                    ItemGUID = item.ItemGUID,
                    Quantity = item.GetQuantity(),
                    bActive = item.IsActive(),
                    bFavourite = item.bFavourite,
                    CustomData = SerializeItemCustomData(item)
                });
            }
            return saved;
        }

        /// <summary>从存档加载。</summary>
        public virtual void Load(List<SavedItem> savedItems, int savedCurrency)
        {
            bIsLoading = true;
            // 清空现有
            foreach (var item in _items.ToList()) item.RemovedFromInventory(this);
            _items.Clear();
            _itemGuidMap.Clear();
            _tickItems.Clear();
            _currency = savedCurrency;

            foreach (var saved in savedItems)
            {
                var item = ItemFactory.CreateItem(saved.ItemClassId);
                if (item == null) continue;
                item.ItemClassId = saved.ItemClassId;
                item.ItemGUID = saved.ItemGUID;
                item.SetQuantity(saved.Quantity);
                if (saved.bActive) item.SetActive(true, true);
                item.bFavourite = saved.bFavourite;
                DeserializeItemCustomData(item, saved.CustomData);
                AddItemInternal(item);
                item.AddedToInventory(this, true);
            }

            bIsLoading = false;
            NotifyInventoryUpdated();
        }

        protected virtual string SerializeItemCustomData(NarrativeItem item)
        {
            return ""; // 子类可覆盖以序列化物品特定数据
        }

        protected virtual void DeserializeItemCustomData(NarrativeItem item, string data)
        {
            // 子类可覆盖
        }

        // ===== 通知 =====

        public void NotifyInventoryUpdated() => OnInventoryUpdated?.Invoke(this);
        public void NotifyItemUsed(NarrativeItem item) => OnItemUsed?.Invoke(this, item);
    }
}

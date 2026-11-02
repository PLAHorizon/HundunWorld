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

        /// <summary>
        /// 拥有者 Controller。对应 UE5 <c>GetOwningController</c>（InventoryComponent.cpp:845-872）：
        /// Owner 是 Pawn 时取其 <c>GetController()</c>，且只接受 APlayerController —— NPC 的控制器不是
        /// PlayerController，UE5 那里同样返回 null；容器/箱子类背包（Owner 既非 Pawn 也非 PC）也是 null。
        /// Flax 无 Controller 概念，按 Tales/TalesComponent.cs:875 的口径在场景里找玩家控制器，
        /// 并要求它当前操控的角色就是本背包所在的 Actor。UE5 在 BeginPlay 缓存（:884），这里同样缓存。
        /// </summary>
        public virtual Actor GetOwningController()
        {
            if (_ownerController != null) return _ownerController;

            var playerController = Level.FindScript<NarrativePro.UnrealFramework.NarrativePlayerController>();
            if (playerController == null) return null;

            var owned = playerController.OwnedCharacter;
            if (owned == null || owned.Actor != Actor) return null;

            _ownerController = playerController.Actor;
            return _ownerController;
        }

        [NonSerialized]
        private Actor _ownerController;

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
            _ownerController = null;
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

        // ===== 使用物品 =====

        /// <summary>
        /// 使用背包里的物品：走物品自己的 <see cref="NarrativeItemUseAction"/> 列表
        /// （对应 UE5 右键菜单点"装备/使用" → <c>FNarrativeItemUseAction::OnUse</c>）。
        /// 这是 <c>UseAction_Equip::OnUse</c> 在本端口的实际调用入口；没有可用动作时回退 <see cref="NarrativeItem.TryUse"/>。
        /// </summary>
        public virtual bool UseItem(NarrativeItem item)
        {
            return UseItem(item, null);
        }

        /// <summary>
        /// 带第二操作数的使用（对应 UE5 <c>UseItem(Item, OtherItem)</c>，InventoryComponent.cpp:132-190）：
        /// 毒物/配件安装这类「拖到目标物品上用」的物品靠它拿到目标，
        /// 库存 UI 的两段式选取（先选来源再选目标）必须走这条口，否则第二操作数永远是 null。
        /// </summary>
        public virtual bool UseItem(NarrativeItem item, NarrativeItem otherItem)
        {
            if (item == null || !item.CanUse()) return false;

            // UE5 :147-151 先确认背包里真的有这件物品，再允许使用
            if (FindItemOfClass(item.ItemClassId) == null)
            {
                return false;
            }

            var actions = item.GetItemUseActions();
            if (actions != null)
            {
                foreach (var action in actions)
                {
                    if (action == null) continue;
                    if (action.OnUse(item, otherItem))
                    {
                        NotifyItemUsed(item);
                        return true;
                    }
                }
            }

            return item.TryUse(otherItem);
        }

        /// <summary>按物品类 ID 使用背包里的第一件该物品（快捷键/脚本入口）。</summary>
        public virtual bool UseItemOfClass(string itemClassId)
        {
            if (string.IsNullOrEmpty(itemClassId)) return false;
            return UseItem(FindItemOfClass(itemClassId));
        }

        /// <summary>持握背包里的第一件指定武器（快捷键换武），与 <see cref="UseItemOfClass"/> 同源。</summary>
        public virtual bool WieldWeaponOfClass(string itemClassId) => UseItemOfClass(itemClassId);

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
                // bCheckAutoUse 原本没有落进 AddedToInventory：自动使用的开关在这里接上
                // （fromLoad = !bCheckAutoUse，与"读档/容器转移不自动使用"一致）
                item.AddedToInventory(this, !bCheckAutoUse);
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
            // 可拾取容器（对应 UE5 AInteractableItemPickup）取空后销毁自身 Actor
            if (this is InteractableItemPickup pickup) pickup.CheckEmptyAndDestroy();
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

        /// <summary>
        /// 按战利品表滚动添加物品。
        /// 对应 UE5 <c>TryAddFromLootTable</c>（InventoryComponent.cpp:222-266）：
        /// 必定授予的物品/集合 → 若可掷则每次 roll 取一行，行按自身 Chance 通过后才授予，
        /// 行内子表再递归（带递归守卫）。
        /// </summary>
        /// <param name="lootTable">要掷的表。</param>
        /// <param name="outResults">输出：每次授予的结果。</param>
        public virtual void TryAddFromLootTable(LootTableRoll lootTable, List<ItemAddResult> outResults)
        {
            TryAddFromLootTable(lootTable, outResults, null);
        }

        /// <summary>
        /// 带递归守卫的战利品掷取。
        /// <paramref name="rollingTables"/> 记录当前掷取链上已经打开的表，
        /// 等价 UE5 的 <c>Subtable.TableToRoll != LootTable.TableToRoll</c> 自检，
        /// 并把守卫扩展到更长的环（A→B→A）与过深嵌套，避免栈溢出。
        /// </summary>
        public virtual void TryAddFromLootTable(LootTableRoll lootTable, List<ItemAddResult> outResults, HashSet<LootTableData> rollingTables)
        {
            // 掷取决策（RNG 门 + 行选取 + 子表递归守卫）抽到纯静态 RollLootTable，
            // 授予动作通过 sink 回灌本组件（保持与 UE5 完全相同的调用序与守卫语义）。
            // 抽出的目的是让核心掷取规则可脱离引擎实例做离线校验（见 Tools/NarrativeProHarness）。
            RollLootTable(lootTable, GetLootRandom(), rollingTables, new InventoryLootRollSink(this, outResults));
        }

        /// <summary>战利品授予出口（行/表级"必定授予"与集合授予的落地方式由实现决定）。</summary>
        public interface ILootRollSink
        {
            void GrantAlwaysItems(List<ItemWithQuantity> itemsToGrant);
            void GrantCollections(List<string> collectionIds);
        }

        /// <summary>背包实现：把授予落到 outResults（经 TryAddItemFromClass）。</summary>
        private sealed class InventoryLootRollSink : ILootRollSink
        {
            private readonly NarrativeInventoryComponent _inv;
            private readonly List<ItemAddResult> _out;
            public InventoryLootRollSink(NarrativeInventoryComponent inv, List<ItemAddResult> outResults)
            {
                _inv = inv;
                _out = outResults;
            }
            public void GrantAlwaysItems(List<ItemWithQuantity> itemsToGrant) => _inv.GrantAlwaysItems(itemsToGrant, _out);
            public void GrantCollections(List<string> collectionIds) => _inv.GrantCollections(collectionIds, _out);
        }

        /// <summary>
        /// 纯掷取引擎：对给定 roll 掷一次（授予 + 子表 + 行随机），把授予委派给 <paramref name="sink"/>，
        /// 随机源与递归守卫链均由参数注入，因而可脱离 Script 实例离线运行。
        /// <para>语义与逐行照抄原 <c>TryAddFromLootTable</c>（InventoryComponent.cpp:222-266 端口）：
        /// 必定授予不判 CanRoll；链守卫集合 <paramref name="rollingTables"/> 为 null 时本次 RollSubTables 自建
        /// 一条新链（顶层行循环的子表各用新链），非 null 时沿子表递归向下传递同一条链。</para>
        /// </summary>
        public static void RollLootTable(LootTableRoll lootTable, Random rng, HashSet<LootTableData> rollingTables, ILootRollSink sink)
        {
            if (lootTable == null || sink == null) return;
            rng ??= new Random();

            // 必定授予的物品与集合不参与 CanRoll 判定（UE5 也是先无条件授予再判能否掷行）
            sink.GrantAlwaysItems(lootTable.ItemsToGrant);
            sink.GrantCollections(lootTable.ItemCollectionsToGrant);

            if (!lootTable.CanRoll()) return;

            // 子表递归：UE5 在 CanRoll/TableToRoll 之外，无条件遍历 SubTablesToRoll
            RollSubTablesStatic(lootTable.SubTablesToRoll, rng, rollingTables, sink, lootTable);

            var tableToRoll = lootTable.TableToRoll;
            if (tableToRoll == null || tableToRoll.RowCount <= 0) return;

            var allRows = tableToRoll.GetRowNames();
            if (allRows.Count == 0) return;

            for (int i = 0; i < lootTable.NumRolls; i++)
            {
                // 本次掷骰是否成立
                if (rng.NextDouble() > lootTable.Chance) continue;

                // 随机取一行（对应 AllRows[FMath::RandRange(0, Num-1)]）
                int rowIndex = rng.Next(allRows.Count);
                string randomRowName = allRows[rowIndex];

                var row = tableToRoll.FindRow(randomRowName);
                if (row == null) continue;

                // 行自身的命中率
                if (rng.NextDouble() > row.Chance) continue;

                sink.GrantAlwaysItems(row.ItemsToGrant);
                sink.GrantCollections(row.ItemCollectionsToGrant);

                // 行内子表：跳过正在掷的同一张表，否则会无限循环
                RollSubTablesStatic(row.SubTablesToRoll, rng, rollingTables, sink, lootTable);
            }
        }

        /// <summary>
        /// 递归掷子表（纯逻辑版），带守卫：
        /// 1. 子表指向的正是当前链上已在掷的表 → 跳过（对应 UE5 的直接自环检查）；
        /// 2. 递归深度超过 <see cref="LootTableRoll.MaxRecursionDepth"/> → 跳过并告警一次。
        /// </summary>
        private static void RollSubTablesStatic(List<LootTableRoll> subTables, Random rng, HashSet<LootTableData> rollingTables, ILootRollSink sink, LootTableRoll parentRoll)
        {
            if (subTables == null || subTables.Count == 0) return;

            int maxDepth = parentRoll != null && parentRoll.MaxRecursionDepth > 0 ? parentRoll.MaxRecursionDepth : 8;
            rollingTables ??= new HashSet<LootTableData>(ReferenceComparer<LootTableData>.Instance);

            if (rollingTables.Count >= maxDepth)
            {
                NarrativeLog.LogWarning($"[Loot] 子表递归深度达到上限 {maxDepth}，剩余子表不再掷取（可能存在环）");
                return;
            }

            var tableToRoll = parentRoll?.TableToRoll;

            foreach (var subTable in subTables)
            {
                if (subTable == null) continue;

                // 正在掷的表自己不重复进入，否则无限循环
                if (tableToRoll != null && ReferenceEquals(subTable.TableToRoll, tableToRoll)) continue;
                if (subTable.TableToRoll != null && rollingTables.Contains(subTable.TableToRoll)) continue;

                if (subTable.TableToRoll != null)
                {
                    rollingTables.Add(subTable.TableToRoll);
                }

                try
                {
                    RollLootTable(subTable, rng, rollingTables, sink);
                }
                finally
                {
                    if (subTable.TableToRoll != null)
                    {
                        rollingTables.Remove(subTable.TableToRoll);
                    }
                }
            }
        }


        /// <summary>授予一组"必定给予"的物品。</summary>
        protected virtual void GrantAlwaysItems(List<ItemWithQuantity> itemsToGrant, List<ItemAddResult> outResults)
        {
            if (itemsToGrant == null) return;
            foreach (var itemToGive in itemsToGrant)
            {
                if (itemToGive == null || string.IsNullOrEmpty(itemToGive.ItemClassId)) continue;
                outResults.Add(TryAddItemFromClass(itemToGive.ItemClassId, itemToGive.Quantity, false));
            }
        }

        /// <summary>授予一组物品集合。</summary>
        protected virtual void GrantCollections(List<string> collectionIds, List<ItemAddResult> outResults)
        {
            if (collectionIds == null) return;
            foreach (var collectionId in collectionIds)
            {
                if (string.IsNullOrEmpty(collectionId)) continue;
                var collection = LoadItemCollection(collectionId);
                if (collection == null || collection.Items == null) continue;
                foreach (var item in collection.Items)
                {
                    if (item == null || string.IsNullOrEmpty(item.ItemClassId) || item.Quantity <= 0) continue;
                    outResults.Add(TryAddItemFromClass(item.ItemClassId, item.Quantity, false));
                }
            }
        }

        /// <summary>引用相等比较器（掷表链守卫按对象身份判定）。</summary>
        private sealed class ReferenceComparer<T> : IEqualityComparer<T> where T : class
        {
            public static readonly ReferenceComparer<T> Instance = new ReferenceComparer<T>();
            public bool Equals(T a, T b) => ReferenceEquals(a, b);
            public int GetHashCode(T value) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);
        }

        /// <summary>
        /// 战利品掷取用的随机源。进程内共享一个实例：
        /// 每次掷取都 new Random() 在快循环里会拿到相近序列，UE5 的 FMath::FRand 是全局流，这里对齐。
        /// </summary>
        protected virtual Random GetLootRandom()
        {
            if (_lootRandom == null) _lootRandom = new Random();
            return _lootRandom;
        }

        private Random _lootRandom;

        /// <summary>注入随机源（测试/按角色种子复现用）。</summary>
        public void SetLootRandom(Random random)
        {
            _lootRandom = random;
        }


        /// <summary>
        /// 加载物品集合。对应 UE5 直接引用 <c>UItemCollection</c> DataAsset。
        /// 此前这条路径把文件拼在 <c>DefaultDataTaskDirectory/ItemCollections</c> 下
        /// （既不是集合该有的目录，也没有按工程根归一，PIE/打包后工作目录不同就永远找不到），
        /// 且用 <c>JsonSerializer</c> 默认的大小写敏感规则反序列化 camelCase JSON，
        /// 结果是集合能"加载成功"但 <c>Items</c> 为空——发放时静默不给任何东西。
        /// 现在统一走 <see cref="ItemCollection.LoadById"/>（同一套目录归一 + 大小写不敏感口径）。
        /// </summary>
        protected virtual ItemCollection LoadItemCollection(string collectionId)
        {
            return ItemCollection.LoadById(collectionId);
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

        /// <summary>
        /// 取本 Actor 上的商人背包（对应 UE5 挂在商人 Actor 上的 UVendorInventoryComponent）。
        /// 本组件自身即商人时返回自身；否则从同一 Actor 的脚本中查找显式挂载的商人组件。
        /// </summary>
        public VendorInventoryComponent AsVendor()
        {
            if (this is VendorInventoryComponent selfVendor) return selfVendor;
            return Actor != null ? Actor.GetScript<VendorInventoryComponent>() : null;
        }

        /// <summary>是否允许 taker 拾取物品。</summary>
        public virtual bool AllowLootItem(NarrativeInventoryComponent taker, string itemClassId, int quantity, out string errorText)
        {
            errorText = "";
            if (bIsVendor)
            {
                // Actor 上显式挂了商人组件时走其交易校验（含库存/货币校验），对应 UE5 UVendorInventoryComponent
                var vendor = AsVendor();
                if (vendor != null && vendor != this)
                {
                    return vendor.AllowLootItem(taker, itemClassId, quantity, out errorText);
                }
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
                // 同 Actor 上存在显式商人组件时，交由其收购校验（商人货币不足则拒绝）
                var vendor = AsVendor();
                if (vendor != null && vendor != this)
                {
                    return vendor.AllowStoreItem(storer, itemClassId, quantity, out errorText);
                }
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
                    CustomData = SerializeItemCustomData(item),
                    // 对应 UE5 UNarrativeItem::SavedFragments（PrepareForSave_Implementation 里抓片段字节流）
                    Fragments = item.CaptureSavedFragments()
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
                // 回填片段状态（涂毒等运行时数据随存档往返，对应 UE5 SavedFragments 的 Load 侧）
                item.ApplySavedFragments(saved.Fragments);
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

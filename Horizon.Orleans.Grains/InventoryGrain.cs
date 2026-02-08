using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using Horizon.Orleans.Interface;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;
using MemoryPack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Horizon.Orleans.Grains
{
    /// <summary>
    /// 背包状态
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class InventoryState
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public Dictionary<long, ItemInfo> Items { get; set; } = new();

        [MemoryPackOrder(1)]
        [Id(1)]
        public int Capacity { get; set; } = 50;

        [MemoryPackOrder(2)]
        [Id(2)]
        public long NextItemId { get; set; } = 1;

        [MemoryPackOrder(3)]
        [Id(3)]
        public Dictionary<int, long> EquippedItems { get; set; } = new();
    }

    /// <summary>
    /// 物品系统Grain实现 - 负责物品管理、背包操作
    /// </summary>
    public class InventoryGrain : Grain, IInventoryGrain
    {
        private readonly ILogger<InventoryGrain> _logger;
        private readonly IPersistentState<InventoryState> _inventoryState;

        public InventoryGrain(
            ILogger<InventoryGrain> logger,
            [PersistentState("inventory", "GameStore")] IPersistentState<InventoryState> inventoryState)
        {
            _logger = logger;
            _inventoryState = inventoryState;
        }

        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("InventoryGrain {GrainKey} activating.", this.GetPrimaryKey());

            if (_inventoryState.State.Items == null)
                _inventoryState.State.Items = new Dictionary<long, ItemInfo>();

            if (_inventoryState.State.EquippedItems == null)
                _inventoryState.State.EquippedItems = new Dictionary<int, long>();

            await base.OnActivateAsync(cancellationToken);
        }

        public Task<InventoryInfo> GetInventoryAsync()
        {
            try
            {
                var state = _inventoryState.State;
                var info = new InventoryInfo
                {
                    Items = state.Items.Values.ToList(),
                    Capacity = state.Capacity,
                    CurrentCount = state.Items.Count,
                    UpdateTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };
                return Task.FromResult(info);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取背包信息失败");
                throw;
            }
        }

        public async Task<ItemChangeInfo> ItemOperationAsync(ItemChangeInfo request)
        {
            try
            {
                _logger.LogInformation("物品操作: ItemId={ItemId}, ChangeType={ChangeType}", request.ItemId, request.ChangeType);

                switch (request.ChangeType)
                {
                    case 0: // 增加
                        await AddItemAsync(request.TemplateId, request.ChangeCount);
                        break;
                    case 1: // 减少
                        await RemoveItemAsync(request.ItemId, request.ChangeCount);
                        break;
                }

                return request;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "物品操作失败: ItemId={ItemId}", request.ItemId);
                throw;
            }
        }

        public async Task<bool> AddItemAsync(int templateId, int quantity, int quality = 0)
        {
            try
            {
                var state = _inventoryState.State;

                if (state.Items.Count >= state.Capacity)
                {
                    _logger.LogWarning("背包已满，无法添加物品: TemplateId={TemplateId}", templateId);
                    return false;
                }

                if (quantity <= 0)
                {
                    _logger.LogWarning("添加物品数量无效: {Quantity}", quantity);
                    return false;
                }

                // 尝试堆叠到现有物品
                var existingItem = state.Items.Values
                    .FirstOrDefault(i => i.TemplateId == templateId && i.Quality == quality);

                if (existingItem != null)
                {
                    existingItem.Count += quantity;
                    _logger.LogInformation("物品堆叠: ItemId={ItemId}, NewCount={Count}", existingItem.ItemId, existingItem.Count);
                }
                else
                {
                    var itemId = state.NextItemId++;
                    var newItem = new ItemInfo
                    {
                        ItemId = itemId,
                        TemplateId = templateId,
                        Count = quantity,
                        Quality = quality
                    };
                    state.Items[itemId] = newItem;
                    _logger.LogInformation("添加新物品: ItemId={ItemId}, TemplateId={TemplateId}", itemId, templateId);
                }

                await _inventoryState.WriteStateAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "添加物品失败: TemplateId={TemplateId}", templateId);
                throw;
            }
        }

        public async Task<bool> RemoveItemAsync(long itemId, int quantity)
        {
            try
            {
                var state = _inventoryState.State;

                if (!state.Items.TryGetValue(itemId, out var item))
                {
                    _logger.LogWarning("物品不存在: ItemId={ItemId}", itemId);
                    return false;
                }

                if (quantity <= 0 || item.Count < quantity)
                {
                    _logger.LogWarning("移除数量无效: ItemId={ItemId}, Count={Count}, RequestedRemove={Quantity}",
                        itemId, item.Count, quantity);
                    return false;
                }

                item.Count -= quantity;
                if (item.Count <= 0)
                {
                    state.Items.Remove(itemId);
                    _logger.LogInformation("物品移除完毕: ItemId={ItemId}", itemId);
                }
                else
                {
                    _logger.LogInformation("物品数量减少: ItemId={ItemId}, RemainingCount={Count}", itemId, item.Count);
                }

                await _inventoryState.WriteStateAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "移除物品失败: ItemId={ItemId}", itemId);
                throw;
            }
        }

        public async Task<bool> UseItemAsync(long itemId, int quantity = 1)
        {
            try
            {
                var state = _inventoryState.State;

                if (!state.Items.TryGetValue(itemId, out var item))
                {
                    _logger.LogWarning("物品不存在: ItemId={ItemId}", itemId);
                    return false;
                }

                if (item.Count < quantity)
                {
                    _logger.LogWarning("物品数量不足: ItemId={ItemId}, Count={Count}, Required={Quantity}",
                        itemId, item.Count, quantity);
                    return false;
                }

                // 消耗物品
                item.Count -= quantity;
                if (item.Count <= 0)
                {
                    state.Items.Remove(itemId);
                }

                await _inventoryState.WriteStateAsync();

                _logger.LogInformation("使用物品成功: ItemId={ItemId}, UsedCount={Quantity}", itemId, quantity);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "使用物品失败: ItemId={ItemId}", itemId);
                throw;
            }
        }

        public async Task<bool> SortInventoryAsync()
        {
            try
            {
                var state = _inventoryState.State;

                // 按模板ID和品质排序重建字典
                var sortedItems = state.Items.Values
                    .OrderBy(i => i.TemplateId)
                    .ThenByDescending(i => i.Quality)
                    .ToList();

                state.Items.Clear();
                foreach (var item in sortedItems)
                {
                    state.Items[item.ItemId] = item;
                }

                await _inventoryState.WriteStateAsync();

                _logger.LogInformation("背包整理完成");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "整理背包失败");
                throw;
            }
        }

        public async Task<bool> ExpandInventoryAsync(int slots)
        {
            try
            {
                if (slots <= 0)
                {
                    _logger.LogWarning("扩展背包槽位数无效: {Slots}", slots);
                    return false;
                }

                _inventoryState.State.Capacity += slots;
                await _inventoryState.WriteStateAsync();

                _logger.LogInformation("背包扩展成功: NewCapacity={Capacity}", _inventoryState.State.Capacity);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "扩展背包失败");
                throw;
            }
        }

        public async Task<bool> EquipItemAsync(long itemId, int slot)
        {
            try
            {
                var state = _inventoryState.State;

                if (slot < 0 || slot > 13)
                {
                    _logger.LogWarning("装备槽位无效: Slot={Slot}", slot);
                    return false;
                }

                if (!state.Items.ContainsKey(itemId))
                {
                    _logger.LogWarning("物品不存在: ItemId={ItemId}", itemId);
                    return false;
                }

                // If slot already has an item, swap it back to inventory
                if (state.EquippedItems.TryGetValue(slot, out var existingItemId))
                {
                    if (state.Items.Count >= state.Capacity)
                    {
                        _logger.LogWarning("背包已满，无法交换装备: Slot={Slot}", slot);
                        return false;
                    }
                }

                // Remove item from inventory
                var item = state.Items[itemId];
                state.Items.Remove(itemId);

                // If slot had an item, put it back in inventory
                if (existingItemId > 0)
                {
                    // Restore the previously equipped item placeholder
                    state.Items[existingItemId] = new ItemInfo { ItemId = existingItemId, TemplateId = 0, Count = 1 };
                }

                state.EquippedItems[slot] = itemId;

                await _inventoryState.WriteStateAsync();
                _logger.LogInformation("装备物品成功: ItemId={ItemId}, Slot={Slot}", itemId, slot);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "装备物品失败: ItemId={ItemId}, Slot={Slot}", itemId, slot);
                throw;
            }
        }

        public async Task<bool> UnequipItemAsync(int slot)
        {
            try
            {
                var state = _inventoryState.State;

                if (!state.EquippedItems.TryGetValue(slot, out var itemId))
                {
                    _logger.LogWarning("槽位没有装备: Slot={Slot}", slot);
                    return false;
                }

                if (state.Items.Count >= state.Capacity)
                {
                    _logger.LogWarning("背包已满，无法卸下装备: Slot={Slot}", slot);
                    return false;
                }

                state.EquippedItems.Remove(slot);
                state.Items[itemId] = new ItemInfo { ItemId = itemId, TemplateId = 0, Count = 1 };

                await _inventoryState.WriteStateAsync();
                _logger.LogInformation("卸下装备成功: Slot={Slot}, ItemId={ItemId}", slot, itemId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "卸下装备失败: Slot={Slot}", slot);
                throw;
            }
        }

        public Task<Dictionary<int, long>> GetEquippedItemsAsync()
        {
            try
            {
                return Task.FromResult(new Dictionary<int, long>(_inventoryState.State.EquippedItems));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取装备列表失败");
                throw;
            }
        }
    }
}

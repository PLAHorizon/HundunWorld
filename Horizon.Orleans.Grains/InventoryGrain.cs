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
    /// 物品系统Grain实现 - 负责物品管理、背包操作
    /// </summary>
    public class InventoryGrain : Grain, IInventoryGrain
    {
        /// <summary>
        /// 合法装备槽位上界。必须与客户端 <c>HundunWorld.Game.EquipmentSlot</c> 枚举的最大值一致
        /// （0=Head … 13=LeftWrist、<b>14=Face</b>）。此前硬编码 13，导致客户端把面饰穿到
        /// Face 槽时被服务端静默拒绝——面饰永远装不上。
        /// </summary>
        public const int MaxEquippedSlot = 14;

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

            // 旧存档里没有明细字典：补空即可，卸下时会回退成桩 ItemInfo（不炸，但保真度受限）。
            if (_inventoryState.State.EquippedItemDetails == null)
                _inventoryState.State.EquippedItemDetails = new Dictionary<int, ItemInfo>();

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

                // 模板 ID 是物品唯一的身份来源（服务端没有运行时物品表可校验其存在性），
                // 因此至少守住"必须是正数"：否则会凭空造出一件 TemplateId=0 的物品，
                // 客户端按定义表回查时解析不到，表现为一个点不开的空格子。
                if (templateId <= 0)
                {
                    _logger.LogWarning("添加物品模板 ID 无效: {TemplateId}", templateId);
                    return false;
                }

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

        /// <summary>
        /// 按**模板 ID**（物品定义 ID）装备到指定槽位。
        /// <para>
        /// 存在的理由：客户端装备体系全程使用定义表 ID（10001..30001），而
        /// <see cref="EquipItemAsync"/> 要的是背包 grain 的自增实例 ID（从 1 起）。
        /// 让客户端去维护"定义 ID ↔ 实例 ID"映射极易出错，故在服务端做一次解析。
        /// 同模板多件时取实例 ID 最小的一件，保证行为确定。
        /// </para>
        /// </summary>
        public async Task<bool> EquipByTemplateAsync(int templateId, int slot)
        {
            if (templateId <= 0) return false;

            var state = _inventoryState.State;
            long resolved = 0;
            foreach (var kv in state.Items)
            {
                if (kv.Value.TemplateId != templateId) continue;
                if (resolved == 0 || kv.Key < resolved) resolved = kv.Key;
            }

            if (resolved == 0)
            {
                _logger.LogWarning("背包内没有该模板的物品: TemplateId={TemplateId}", templateId);
                return false;
            }

            return await EquipItemAsync(resolved, slot);
        }

        public async Task<bool> EquipItemAsync(long itemId, int slot)
        {
            try
            {
                var state = _inventoryState.State;

                if (slot < 0 || slot > MaxEquippedSlot)
                {
                    _logger.LogWarning("装备槽位无效: Slot={Slot}", slot);
                    return false;
                }

                if (!state.Items.TryGetValue(itemId, out var itemToEquip))
                {
                    _logger.LogWarning("物品不存在: ItemId={ItemId}", itemId);
                    return false;
                }

                // 换装：被换下的那件要按**原样**回到背包（含 Count/TemplateId 等），
                // 只凭物品ID重建会得到 Count=1 的桩物品。
                if (state.EquippedItems.TryGetValue(slot, out var replacedItemId))
                {
                    if (state.EquippedItemDetails.TryGetValue(slot, out var replacedDetail))
                    {
                        state.Items[replacedItemId] = replacedDetail;
                    }
                    else
                    {
                        // 旧存档没有明细：至少保证物品不丢，退化为单件。
                        state.Items[replacedItemId] = new ItemInfo { ItemId = replacedItemId, Count = 1 };
                        _logger.LogWarning("槽位 {Slot} 的旧装备缺少完整明细，按 1 件还原: ItemId={ItemId}", slot, replacedItemId);
                    }
                    state.EquippedItemDetails.Remove(slot);
                }

                state.Items.Remove(itemId);
                state.EquippedItems[slot] = itemId;
                state.EquippedItemDetails[slot] = itemToEquip;

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
                // 按装备时存下的完整明细原样还原；旧存档缺明细则退化为单件并告警。
                if (state.EquippedItemDetails.TryGetValue(slot, out var detail))
                {
                    state.Items[itemId] = detail;
                    state.EquippedItemDetails.Remove(slot);
                }
                else
                {
                    state.Items[itemId] = new ItemInfo { ItemId = itemId, Count = 1 };
                    _logger.LogWarning("槽位 {Slot} 的装备缺少完整明细，按 1 件还原: ItemId={ItemId}", slot, itemId);
                }

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

        public Task<List<ItemInfo>> GetEquippedItemInfosAsync()
        {
            try
            {
                var state = _inventoryState.State;
                var result = new List<ItemInfo>(state.EquippedItems.Count);
                foreach (var kv in state.EquippedItems)
                {
                    // 明细缺失（旧存档）时至少回一个带 ID 的条目，别让整件装备在读取时消失。
                    result.Add(state.EquippedItemDetails.TryGetValue(kv.Key, out var detail)
                        ? detail
                        : new ItemInfo { ItemId = kv.Value, Count = 1 });
                }
                return Task.FromResult(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取已装备物品明细失败");
                throw;
            }
        }

        /// <summary>
        /// 槽位 → 完整物品信息。客户端渲染换装必须知道"哪件在哪个槽"，
        /// 只给 <see cref="GetEquippedItemInfosAsync"/> 的扁平列表会丢槽位。
        /// </summary>
        public Task<Dictionary<int, ItemInfo>> GetEquippedSlotsAsync()
        {
            try
            {
                var state = _inventoryState.State;
                var result = new Dictionary<int, ItemInfo>(state.EquippedItems.Count);
                foreach (var kv in state.EquippedItems)
                {
                    result[kv.Key] = state.EquippedItemDetails.TryGetValue(kv.Key, out var detail)
                        ? detail
                        : new ItemInfo { ItemId = kv.Value, Count = 1 };
                }
                return Task.FromResult(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取已装备槽位明细失败");
                throw;
            }
        }

        /// <summary>
        /// 宝石槽上界。服务端没有物品定义表，孔数规则由客户端目录决定；
        /// 这里只做越界兜底（与 <see cref="MaxEquippedSlot"/> 的"信任客户端编号空间 + 硬夹上界"同模式）。
        /// </summary>
        public const int MaxGemSlots = 5;

        public async Task<(bool Success, string Message, List<int> Gems)> SocketGemAsync(long equipmentItemId, int gemSlot, int gemTemplateId)
        {
            try
            {
                var state = _inventoryState.State;

                if (gemSlot < 0 || gemSlot >= MaxGemSlots)
                    return (false, "宝石槽位无效", new List<int>());

                // 定位装备条目：背包内，或已装备（EquippedItemDetails 持完整条目，穿卸搬运的都是它）。
                ItemInfo equipment = null;
                if (!state.Items.TryGetValue(equipmentItemId, out equipment))
                {
                    foreach (var kv in state.EquippedItemDetails)
                    {
                        if (kv.Value != null && kv.Value.ItemId == equipmentItemId)
                        {
                            equipment = kv.Value;
                            break;
                        }
                    }
                }
                if (equipment == null)
                {
                    _logger.LogWarning("镶嵌宝石失败：装备不存在 ItemId={ItemId}", equipmentItemId);
                    return (false, "装备不存在", new List<int>());
                }

                var gems = equipment.Gems ??= new List<int>();

                if (gemTemplateId == 0)
                {
                    // 卸下：宝石从槽位回到背包（可再利用）。
                    if (gemSlot >= gems.Count || gems[gemSlot] <= 0)
                        return (false, "该槽位没有宝石", new List<int>(gems));

                    var removed = gems[gemSlot];
                    gems[gemSlot] = -1;
                    if (!await AddItemAsync(removed, 1, 0))
                    {
                        // 背包满：回滚槽位，宝石不能凭空蒸发。
                        gems[gemSlot] = removed;
                        return (false, "背包已满，无法卸下宝石", new List<int>(gems));
                    }
                    await _inventoryState.WriteStateAsync();
                    _logger.LogInformation("卸下宝石: Equipment={Equip}, Slot={Slot}, Gem={Gem}", equipmentItemId, gemSlot, removed);
                    return (true, "宝石已卸下", new List<int>(gems));
                }

                // 镶嵌：从背包找一颗该模板宝石扣掉；槽内旧宝石换下回背包。
                ItemInfo gemSource = null;
                foreach (var kv in state.Items)
                {
                    if (kv.Value.TemplateId == gemTemplateId && kv.Value.Count > 0)
                    {
                        gemSource = kv.Value;
                        break;
                    }
                }
                // 无物品表可校验"gemTemplateId 确实是宝石"，至少拒绝拿装备实例自身当宝石。
                if (gemSource == null)
                    return (false, "背包中没有该宝石", new List<int>(gems));
                if (gemSource.ItemId == equipmentItemId)
                    return (false, "宝石无效", new List<int>(gems));

                while (gems.Count <= gemSlot)
                    gems.Add(-1);

                var replaced = gems[gemSlot];
                gems[gemSlot] = gemTemplateId;

                if (replaced > 0)
                {
                    // 先确认换下的旧宝石有地方放，再扣新宝石（避免"扣一还零"吞宝石）。
                    if (!await AddItemAsync(replaced, 1, 0))
                    {
                        gems[gemSlot] = replaced;
                        return (false, "背包已满，无法交换宝石", new List<int>(gems));
                    }
                }

                await RemoveItemAsync(gemSource.ItemId, 1);
                await _inventoryState.WriteStateAsync();

                _logger.LogInformation("镶嵌宝石: Equipment={Equip}, Slot={Slot}, Gem={Gem}, Replaced={Replaced}",
                    equipmentItemId, gemSlot, gemTemplateId, replaced);
                return (true, replaced > 0 ? "宝石已交换" : "宝石已镶嵌", new List<int>(gems));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "镶嵌宝石失败: Equipment={Equip}, Slot={Slot}", equipmentItemId, gemSlot);
                throw;
            }
        }
    }
}

using Horizon.Core.Abstract;
using Horizon.Entities;
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
        /// （0=Head … 14=Face、<b>15=Appearance 外观</b>）。此前硬编码 13，导致客户端把面饰穿到
        /// Face 槽时被服务端静默拒绝——面饰永远装不上；外观槽追加后同步抬到 15。
        /// </summary>
        public const int MaxEquippedSlot = 15;

        private readonly ILogger<InventoryGrain> _logger;
        private readonly IPersistentState<InventoryState> _inventoryState;

        /// <summary>物品模板表上下文。只为把材料缺项说成"锐金砂×6"而不是"物品 910001 需 6"。</summary>
        private readonly IDataContext<GameEntityContext, Horizon.Model.GameModel.ItemTemplateEntity, int>? _itemTemplateContext;

        public InventoryGrain(
            ILogger<InventoryGrain> logger,
            [PersistentState("inventory", "GameStore")] IPersistentState<InventoryState> inventoryState,
            IDataContext<GameEntityContext, Horizon.Model.GameModel.ItemTemplateEntity, int>? itemTemplateContext = null)
        {
            _logger = logger;
            _inventoryState = inventoryState;
            _itemTemplateContext = itemTemplateContext;
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

        /// <summary>
        /// 秘籍模板 id 段起点：秘籍 id = 本值 + 它所教的技能 id。
        /// 与服务端种子、客户端 <c>ItemCatalog.BookIdBase</c> 同一契约，三处必须同值
        /// （docs/SKILL_WUXING_TREE_DESIGN.md §7.2）。
        /// </summary>
        public const int BookItemTemplateBase = 800000;

        /// <summary>物品表里"秘籍"这一档的 <c>item_type</c> 值。</summary>
        public const int SkillBookItemType = 6;

        /// <summary>
        /// 一件物品该不该按"秘籍"处理（转投 <c>SkillGrain.LearnFromBookAsync</c>）。
        /// <para>
        /// 号段只是**必要条件**，不是判据本身：库里有 15 行五行素材/信物也落在 800000+ 号段内
        /// （<c>910001..910015</c>，见 <c>docs/TECH_DEBT.md</c> T9）。只看号段会让
        /// ① 带 <c>use_effect=favor</c> 的信物被当成书研读、好感永远不加；
        /// ② 无效果的素材被拿去研读一个不存在的技能。
        /// 所以判据回到模板自己：未声明好感效果 **且** 档位确实是秘籍。
        /// </para>
        /// <para>
        /// <b>查不到模板时退回旧的号段口径</b>（未接库的部署、查询异常）——
        /// 读不到东西绝不能变成"所有书都用不了"。
        /// </para>
        /// </summary>
        /// <param name="templateId">物品模板 id。</param>
        /// <param name="profileResolved">是否真的读到了这一行模板。</param>
        /// <param name="itemType">读到的 <c>item_type</c>（读不到时为 0）。</param>
        /// <param name="declaresFavorEffect">模板 <c>use_effect</c> 是否声明了 <c>{"kind":"favor"}</c>。</param>
        public static bool RoutesAsSkillBook(int templateId, bool profileResolved, int itemType, bool declaresFavorEffect)
        {
            if (templateId < BookItemTemplateBase) return false;
            if (declaresFavorEffect) return false;
            return profileResolved ? itemType == SkillBookItemType : true;
        }

        public async Task<UseItemResponse> UseItemWithResultAsync(ulong characterId, long itemId, int count)
        {
            var response = new UseItemResponse();
            var take = count > 0 ? count : 1;
            var state = _inventoryState.State;

            if (!state.Items.TryGetValue(itemId, out var item))
            {
                response.Message = "物品不存在";
                return response;
            }
            if (item.Count < take)
            {
                response.Message = "物品数量不足";
                response.RemainingCount = item.Count;
                return response;
            }

            // 先问模板"这东西是什么档、声明了什么效果"，再决定走哪条分支。
            // 判据为什么要改：号段启发式（id ≥ 800000 就当秘籍）**排在**模板事实之前，
            // 于是同号段里的五行信物（910011..910015，use_effect={"kind":"favor"}）被当成"研读"转投
            // SkillGrain，好感效果**永远不会生效**——任务奖励发出去就是一张点了没反应的纸；
            // 而 910001..910010 这 10 件素材会被拿去研读一个不存在的技能。详见 TECH_DEBT T9。
            // 代价：每次"使用"多一次主键查询；原先非秘籍分支本来就要查一次，所以增量只落在真秘籍上。
            // 回退：把这三行换回 `if (item.TemplateId >= BookItemTemplateBase)` 即可（其余逻辑未动）。
            var profile = await ReadItemUseProfileAsync(item.TemplateId);
            var favorEffect = profile.Favor;

            // 秘籍：转投 SkillGrain 研读。门槛未达不吃书，掷签失败吃一本（由 BookConsumed 表达）。
            if (RoutesAsSkillBook(item.TemplateId, profile.Resolved, profile.ItemType, favorEffect is not null))
            {
                int skillId = item.TemplateId - BookItemTemplateBase;
                var skillGrain = GrainFactory.GetGrain<ISkillGrain>(
                    Horizon.Game.Core.GrainKeys.ForEntity(characterId));
                // 判定要用的事实（背包材料 / 分域好感 / 基础属性）在这里装配。
                // 本 Grain 处理这条消息期间是独占的（非重入），所以"判定时看到的数量"
                // 与稍后"真正扣掉的数量"之间不会被别的操作插队。
                var gate = await BuildLearnGateContextAsync(characterId);
                var learned = await skillGrain.LearnFromBookAsync((long)characterId, skillId, gate);

                response.Success = learned.Success;
                response.Message = learned.Message;
                if (learned.BookConsumed) await UseItemAsync(itemId, take);

                // §14 的特殊材料：只有真习得成功才扣。扣减失败不回滚已成的习得——
                // 数量判定刚才已经过了，这里失败只可能是写库异常，记 warning 留人对着账补。
                if (learned.Success)
                {
                    for (int i = 0; i < learned.MaterialItemIds.Count && i < learned.MaterialCounts.Count; i++)
                    {
                        int templateId = learned.MaterialItemIds[i];
                        int need = learned.MaterialCounts[i];
                        if (need <= 0) continue;
                        if (!await ConsumeByTemplateAsync(characterId, templateId, need))
                            _logger.LogWarning(
                                "研读成功但材料扣减不完整: skill={SkillId} template={TemplateId} need={Need}",
                                skillId, templateId, need);
                    }
                }

                response.RemainingCount = state.Items.TryGetValue(itemId, out var left) ? left.Count : 0;
                return response;
            }

            // 五行信物一类的"用了就改变角色状态"的效果，只有这一方能同时看到"包里有几件"和
            // "该扣哪一格"，所以扣减与入账都在本 Grain 内完成：判定与写在同一条消息里，
            // 非重入 Grain 保证中间没有第二条操作插进来。
            // favorEffect 已在分支判定前取过（同一次查询，别在这再问一遍库）。
            var ok = await UseItemAsync(itemId, take);
            if (ok && favorEffect != null)
            {
                var favor = GrainFactory.GetGrain<IFavorabilityGrain>(
                    Horizon.Game.Core.GrainKeys.ForEntity(characterId));
                // 不记来源上限：信物本身就是一次性消耗品，成本即闸门
                int total = await favor.AddAsync(favorEffect.Value.Domain, favorEffect.Value.Delta * take);
                response.Success = true;
                response.Message = $"好感 +{favorEffect.Value.Delta * take}（该域现计 {total}）";
            }
            else
            {
                response.Success = ok;
                response.Message = ok ? "物品使用成功" : "使用失败";
            }
            response.RemainingCount = state.Items.TryGetValue(itemId, out var remaining) ? remaining.Count : 0;
            return response;
        }

        /// <summary>
        /// 读物品模板 <c>use_effect</c> 里的"换该域好感"效果
        /// （<c>{"kind":"favor","domain":n,"delta":m}</c>）。
        /// 模板表不可用时按"没有这种效果"处理：普通消耗品的使用绝不能因为查表失败而被拒。
        /// </summary>
        /// <summary>
        /// 一次查询读回"这行物品怎么用"的两条事实：档位 <c>item_type</c> 与 <c>use_effect</c> 里的好感效果。
        /// 分档判定与扣账都用它，所以**只查一次**（早先两个分支各查一次，改顺序后差点变成三次）。
        /// </summary>
        private async Task<(bool Resolved, int ItemType, (int Domain, int Delta)? Favor)> ReadItemUseProfileAsync(int templateId)
        {
            if (_itemTemplateContext == null || templateId <= 0) return (false, 0, null);
            try
            {
                var rows = await _itemTemplateContext.QueryAsync(t => t.Id == templateId);
                var row = rows?.FirstOrDefault();
                if (row == null) return (false, 0, null);
                return (true, row.ItemType, ParseFavorEffect(row.UseEffect));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "读物品模板失败，按号段旧口径处理。template={TemplateId}", templateId);
                return (false, 0, null);
            }
        }

        /// <summary>解析 <c>{"kind":"favor","domain":n,"delta":m}</c>；格式不对就当作"没有这种效果"。</summary>
        private static (int Domain, int Delta)? ParseFavorEffect(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(raw);
                var root = doc.RootElement;
                if (root.ValueKind != System.Text.Json.JsonValueKind.Object) return null;
                if (!root.TryGetProperty("kind", out var kind)
                    || kind.GetString() != "favor") return null;
                if (!root.TryGetProperty("domain", out var domainEl) || !domainEl.TryGetInt32(out var domain)
                    || domain < 1 || domain > 5) return null;
                int delta = root.TryGetProperty("delta", out var deltaEl) && deltaEl.TryGetInt32(out var d) ? d : 0;
                return delta > 0 ? (domain, delta) : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>模板表不可用时按"没有这种效果"处理：普通消耗品的使用绝不能因为查表失败而被拒。</summary>
        private async Task<(int Domain, int Delta)?> ReadFavorEffectAsync(int templateId)
            => (await ReadItemUseProfileAsync(templateId)).Favor;

        /// <summary>
        /// 装配 §14 动态门槛的判定输入。<b>只有 InventoryGrain 做这件事</b>：它本来就握着背包，
        /// 而 SkillGrain 反过来问它会与"秘籍研读"那条边凑成 Grain 环（非重入 Grain 互等死锁）。
        /// 读失败一律按"没有"处理：好感读不到就按零好感判，绝不因为查不到就放行。
        /// </summary>
        public async Task<LearnGateContext> BuildLearnGateContextAsync(ulong characterId)
        {
            var context = new LearnGateContext();

            var counts = new Dictionary<int, int>();
            if (_inventoryState.State.Items != null)
            {
                foreach (var entry in _inventoryState.State.Items.Values)
                {
                    if (entry == null || entry.TemplateId <= 0) continue;
                    counts.TryGetValue(entry.TemplateId, out var have);
                    counts[entry.TemplateId] = have + Math.Max(0, entry.Count);
                }
            }
            context.ItemCounts = counts;
            context.ItemNames = await LoadMaterialNamesAsync();

            try
            {
                var favor = GrainFactory.GetGrain<IFavorabilityGrain>(
                    Horizon.Game.Core.GrainKeys.ForEntity(characterId));
                context.FavorByDomain = await favor.GetByDomainAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "读取分域好感失败，本次按零好感判定。CharacterId={CharacterId}", characterId);
            }

            try
            {
                var info = await GrainFactory.GetGrain<ICharacterGrain>((long)characterId).GetCharacterInfoAsync(null);
                context.Allocation = info?.Allocation;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "读取角色基础属性失败。CharacterId={CharacterId}", characterId);
            }

            return context;
        }

        /// <summary>
        /// 只拉灵材那一段（按 id 区间），不为一句提示把整张物品模板表搬进 Grain。
        /// 模板表不可用时返回空表，判定文案退化成报 id。
        /// </summary>
        private async Task<Dictionary<int, string>> LoadMaterialNamesAsync()
        {
            var names = new Dictionary<int, string>();
            if (_itemTemplateContext == null) return names;

            try
            {
                var rows = await _itemTemplateContext.QueryAsync(t => t.Id >= 910000 && t.Id <= 919999);
                foreach (var t in rows)
                    if (t != null && !string.IsNullOrWhiteSpace(t.ItemName)) names[t.Id] = t.ItemName!;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "读取灵材名称失败，材料缺项将退化为报 id");
            }
            return names;
        }

        /// <summary>
        /// 按模板 id 跨堆叠扣减（材料可能散在好几个格子里）。先全量核对再动手：
        /// 不够就一票否决、一格都不扣，避免"扣了一半材料、习得又失败"的半态。
        /// </summary>
        public async Task<bool> ConsumeByTemplateAsync(ulong characterId, int templateId, int quantity)
        {
            var state = _inventoryState.State;
            if (quantity <= 0) return true;
            if (state.Items == null) return false;

            var stackIds = new List<long>();
            int available = 0;
            foreach (var entry in state.Items)
            {
                if (entry.Value == null || entry.Value.TemplateId != templateId) continue;
                available += Math.Max(0, entry.Value.Count);
                stackIds.Add(entry.Key);
                if (available >= quantity) break;
            }
            if (available < quantity) return false;

            int remaining = quantity;
            foreach (var stackId in stackIds)
            {
                if (remaining <= 0) break;
                if (!state.Items.TryGetValue(stackId, out var item)) continue;
                int take = Math.Min(remaining, Math.Max(0, item.Count));
                if (take <= 0) continue;
                await RemoveItemAsync(stackId, take);
                remaining -= take;
            }

            await _inventoryState.WriteStateAsync();
            return remaining <= 0;
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

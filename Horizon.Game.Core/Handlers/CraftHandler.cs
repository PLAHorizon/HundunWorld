using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Horizon.Game.Message;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using Horizon.Game.Sim.Economy;
using Horizon.Game.Sim.Npcs;
using Horizon.Orleans.Interface;
using Microsoft.Extensions.Logging;

namespace Horizon.Game.Core.Handlers
{
    /// <summary>
    /// NPC 制作（学配方 / 在设施旁制作）。
    /// <para>
    /// <b>三个权威各管一段</b>：配方定义与设施要求来自已装配内容（本处理器查），
    /// 是否已学与制作历史在 <see cref="ICraftingGrain"/>，材料与钱在 <see cref="IInventoryGrain"/> /
    /// <see cref="ICurrencyGrain"/>。<b>任何一段都不许"看起来成功"</b>——旧实现里
    /// "配方存在就算材料齐"已经删掉了（见设计文档 §11.10）。
    /// </para>
    /// <para>
    /// <b>失败补偿顺序</b>：扣料 → 收工费 → 发成品。任一步失败都按相反顺序退回，
    /// 退不掉的必须留 Error 日志（"钱没了货也没了"是唯一不可接受的状态）。
    /// </para>
    /// </summary>
    public class CraftHandler : MessageHandlerBase
    {
        private readonly Horizon.Game.Core.Sim.Server.DsEntityRegistry? _entityRegistry;
        private readonly Horizon.Game.Core.World.IShardRouter? _shardRouter;

        public CraftHandler(
            ILogger<MessageHandlerBase> logger,
            IClusterClient clusterClient,
            HorizonMessageAdapter adapter,
            Horizon.Game.Core.Sim.Server.DsEntityRegistry? entityRegistry = null,
            Horizon.Game.Core.World.IShardRouter? shardRouter = null) : base(logger, clusterClient, adapter)
        {
            _entityRegistry = entityRegistry;
            _shardRouter = shardRouter;
        }

        public override List<MessageType> MessageTypes { get; } = new List<MessageType> { MessageType.Crafting };

        public override ServiceType ServiceType => ServiceType.Game;

        public override async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> RouteHandlerAsync(HorizonMessagePacket message)
        {
            var request = message.Body as CraftingRequest;
            if (request == null || request.CharacterId == 0)
                return (true, CreateHorizonMessage(new CraftingResponse { Success = false, Message = "请求无效" }));

            return request.Action switch
            {
                1 => await HandleLearnAsync(request),
                2 => await HandleListAsync(request),
                _ => await HandleCraftAsync(request),
            };
        }

        // ────────────────────────────── 学配方 ──────────────────────────────

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleLearnAsync(CraftingRequest request)
        {
            var response = new CraftingResponse();

            try
            {
                var resolve = await ResolveNpcAsync(request.CharacterId, request.EntityId, (uint)NpcServiceBits.CraftTrainer);
                if (!resolve.Ok)
                {
                    response.Success = false;
                    response.Message = resolve.Reason;
                    return (true, CreateHorizonMessage(response));
                }

                var recipe = NpcContent.Recipe(request.RecipeId);
                if (recipe == null)
                {
                    response.Success = false;
                    response.Message = "这个配方不存在";
                    return (true, CreateHorizonMessage(response));
                }

                // 只能学**这位 NPC 教的**配方：清单按他的教学行出，动作不查这一步时，
                // 站到任意训练师面前就能学会全服配方——面板上的"师承"成了摆设。
                var taught = NpcCraftCore.FindTaughtRecipeRow(NpcContent.TrainerRows(resolve.NpcId), recipe.RecipeId);
                if (taught == null)
                {
                    response.Success = false;
                    response.Message = "这位前辈不教这张配方";
                    return (true, CreateHorizonMessage(response));
                }

                // 学费与材料在**教学行**上（面板也是照它显示）：不在这里收，就是"标价 800 金、实际免费"。
                // 与技能学习同一条纪律：先扣、学失败原路退回。
                var row = taught.Value;
                ICurrencyGrain? wallet = null;
                if (row.CostGold > 0)
                {
                    wallet = _clusterClient.GetGrain<ICurrencyGrain>((long)request.CharacterId);
                    var spend = await wallet.SpendAsync(WalletCurrency.Gold, row.CostGold,
                        $"npc_recipe:{resolve.NpcId}:{recipe.RecipeId}");
                    if (!spend.Ok)
                    {
                        response.Success = false;
                        response.Message = "金币不足，学不了这张图纸";
                        return (true, CreateHorizonMessage(response));
                    }
                }

                IInventoryGrain? inventory = null;
                if (row.CostItemId > 0 && row.CostItemCount > 0)
                {
                    inventory = _clusterClient.GetGrain<IInventoryGrain>(GrainKeys.ForEntity(request.CharacterId));
                    var consumed = await inventory.ConsumeByTemplateAsync(request.CharacterId, row.CostItemId, row.CostItemCount);
                    if (!consumed)
                    {
                        await RefundRecipeLearnAsync(wallet, row.CostGold, inventory, row.CostItemId, row.CostItemCount,
                            $"npc_recipe_refund:{recipe.RecipeId}");
                        response.Success = false;
                        response.Message = "缺少教学材料";
                        return (true, CreateHorizonMessage(response));
                    }
                }

                var grain = _clusterClient.GetGrain<ICraftingGrain>(GrainKeys.ForEntity(request.CharacterId));
                var learned = await grain.LearnRecipeAsync(BuildDefinition(recipe));
                if (!learned)
                {
                    // "已经学会了"也是没学成：收费与材料照退（与技能分支一致）。
                    await RefundRecipeLearnAsync(wallet, row.CostGold, inventory, row.CostItemId, row.CostItemCount,
                        $"npc_recipe_refund:{recipe.RecipeId}");
                }

                response.Success = learned;
                response.Message = learned ? $"已学会「{recipe.Name}」" : "这个配方已经学会了";
                return (true, CreateHorizonMessage(response));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "学配方失败 CharacterId={CharacterId} RecipeId={RecipeId}", request.CharacterId, request.RecipeId);
                response.Success = false;
                response.Message = "学配方失败";
                return (true, CreateHorizonMessage(response));
            }
        }

        // ────────────────────────────── 清单 ──────────────────────────────

        /// <summary>
        /// 配方清单：这位 NPC 教的（CraftTrainer）或这个设施能做的（CraftStation）。
        /// <para>客户端拿这份清单画卡片，<b>不在本地维护写死的配方表</b>。
        /// 两边都占的 NPC 会去重（同一配方只出现一次）。</para>
        /// </summary>
        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleListAsync(CraftingRequest request)
        {
            var response = new CraftingResponse();

            try
            {
                var resolve = await ResolveNpcAsync(request.CharacterId, request.EntityId, 0u);
                if (!resolve.Ok)
                {
                    response.Success = false;
                    response.Message = resolve.Reason;
                    return (true, CreateHorizonMessage(response));
                }

                if (!NpcContent.TryGetConfig(resolve.NpcId, out var config) || config is null)
                {
                    response.Success = false;
                    response.Message = "找不到这个 NPC";
                    return (true, CreateHorizonMessage(response));
                }

                var isTrainer = (config.ServiceFlags & (uint)NpcServiceBits.CraftTrainer) != 0;
                var isStation = (config.ServiceFlags & (uint)NpcServiceBits.CraftStation) != 0;
                if (!isTrainer && !isStation)
                {
                    response.Success = false;
                    response.Message = "这位 NPC 既不教配方，也不是制作设施";
                    return (true, CreateHorizonMessage(response));
                }

                var seen = new HashSet<int>();
                var recipes = new List<CraftingRecipe>();

                if (isTrainer)
                {
                    var rows = NpcContent.TrainerRows(resolve.NpcId);
                    for (var i = 0; i < rows.Count; i++)
                    {
                        if (rows[i].Service != NpcTrainerService.Recipe) continue;
                        var recipe = NpcContent.Recipe(rows[i].SkillId);
                        if (recipe == null || !seen.Add(recipe.RecipeId)) continue;
                        recipes.Add(BuildDefinition(recipe));
                    }
                }

                if (isStation)
                {
                    var all = NpcContent.AllRecipes();
                    for (var i = 0; i < all.Count; i++)
                    {
                        var recipe = all[i];
                        if (recipe.StationNpcId != resolve.NpcId) continue;
                        if (!seen.Add(recipe.RecipeId)) continue;
                        recipes.Add(BuildDefinition(recipe));
                    }
                }

                // 稳定顺序：客户端卡片顺序必须可复现（否则同样的清单两次打开顺序不同）。
                recipes.Sort(static (a, b) => a.RecipeId.CompareTo(b.RecipeId));

                response.Success = true;
                response.Message = $"可用配方 {recipes.Count} 个";
                response.Recipes = recipes;
                return (true, CreateHorizonMessage(response));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "拉取配方清单失败 CharacterId={CharacterId} NpcEntity={EntityId}", request.CharacterId, request.EntityId);
                response.Success = false;
                response.Message = "拉取配方清单失败";
                return (true, CreateHorizonMessage(response));
            }
        }

        // ────────────────────────────── 制作 ──────────────────────────────

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleCraftAsync(CraftingRequest request)
        {
            var response = new CraftingResponse();

            try
            {
                var recipe = NpcContent.Recipe(request.RecipeId);
                if (recipe == null)
                {
                    response.Success = false;
                    response.Message = "这个配方不存在";
                    return (true, CreateHorizonMessage(response));
                }

                // 设施要求：配方要求设施时，交互对象必须是设施型 NPC。
                var requireStation = recipe.StationNpcId > 0;
                var resolve = await ResolveNpcAsync(request.CharacterId, request.EntityId,
                    requireStation ? (uint)NpcServiceBits.CraftStation : 0u);
                if (!resolve.Ok)
                {
                    response.Success = false;
                    response.Message = resolve.Reason;
                    return (true, CreateHorizonMessage(response));
                }

                // 设施身份对号（与清单同一条判据）：只查"有没有设施位"时，站在别的设施前
                // 也能做不属于它的配方——清单与动作两条入口必须同数。
                if (!NpcCraftCore.StationIdentityOk(recipe.StationNpcId, resolve.NpcId))
                {
                    response.Success = false;
                    response.Message = "这里不是做这件东西的设施";
                    return (true, CreateHorizonMessage(response));
                }

                var inventory = _clusterClient.GetGrain<IInventoryGrain>(GrainKeys.ForEntity(request.CharacterId));
                var held = await SnapshotHeldAsync(inventory);

                var grain = _clusterClient.GetGrain<ICraftingGrain>(GrainKeys.ForEntity(request.CharacterId));
                var attempt = await grain.CraftItemAsync(request.RecipeId, held);
                if (!attempt.Success)
                {
                    response.Success = false;
                    response.Message = attempt.Message;
                    return (true, CreateHorizonMessage(response));
                }

                // ── 扣料（逐样，失败全部退回）──
                var consumed = new List<long>();
                var consumedCounts = new List<int>();
                var materialFailed = false;

                for (var i = 0; i < recipe.Materials.Length; i++)
                {
                    var need = recipe.Materials[i];
                    if (need.ItemId <= 0 || need.NeedCount <= 0) continue;

                    var ok = await inventory.ConsumeByTemplateAsync(request.CharacterId, need.ItemId, need.NeedCount);
                    if (!ok)
                    {
                        materialFailed = true;
                        break;
                    }

                    consumed.Add(need.ItemId);
                    consumedCounts.Add(need.NeedCount);
                }

                if (materialFailed)
                {
                    await RefundMaterialsAsync(inventory, request.CharacterId, consumed, consumedCounts);
                    response.Success = false;
                    response.Message = "材料不足";
                    return (true, CreateHorizonMessage(response));
                }

                // ── 收工费 ──
                ICurrencyGrain? wallet = null;
                var fee = recipe.CostGold;
                if (fee > 0)
                {
                    wallet = _clusterClient.GetGrain<ICurrencyGrain>((long)request.CharacterId);
                    var spend = await wallet.SpendAsync(WalletCurrency.Gold, fee, $"npc_craft:{request.RecipeId}");
                    if (!spend.Ok)
                    {
                        await RefundMaterialsAsync(inventory, request.CharacterId, consumed, consumedCounts);
                        response.Success = false;
                        response.Message = spend.Reject == (int)WalletReject.InsufficientFunds ? "金币不足，付不起工费" : "扣费失败";
                        return (true, CreateHorizonMessage(response));
                    }

                    response.ConsumedGold = fee;
                }

                // ── 发成品 ──
                var granted = await inventory.AddItemAsync(recipe.ProductItemId, 1);
                if (!granted)
                {
                    await RefundMaterialsAsync(inventory, request.CharacterId, consumed, consumedCounts);
                    if (wallet != null && fee > 0)
                    {
                        var refund = await wallet.AddAsync(WalletCurrency.Gold, fee, $"npc_craft_refund:{request.RecipeId}");
                        if (!refund.Ok) Logger.LogError("制作回滚：工费 {Fee} 未能退回 (CharacterId={CharacterId})", fee, request.CharacterId);
                    }

                    response.Success = false;
                    response.Message = "背包放不下成品";
                    return (true, CreateHorizonMessage(response));
                }

                response.Success = true;
                response.Message = $"做好了一件「{recipe.Name}」";
                response.CraftedItems.Add(new ItemInfo { TemplateId = recipe.ProductItemId, Count = 1, Name = recipe.Name });
                response.ConsumedMaterials.AddRange(consumed);
                return (true, CreateHorizonMessage(response));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "制作失败 CharacterId={CharacterId} RecipeId={RecipeId}", request.CharacterId, request.RecipeId);
                response.Success = false;
                response.Message = "制作失败";
                return (true, CreateHorizonMessage(response));
            }
        }

        // ────────────────────────────── 公共 ──────────────────────────────

        private readonly record struct NpcResolution(bool Ok, string Reason, int NpcId);

        /// <summary>解析交互对象，并按需校验服务位（0 = 不校验位）。</summary>
        private async Task<NpcResolution> ResolveNpcAsync(ulong characterId, ulong entityId, uint requiredServiceBit)
        {
            try
            {
                if (entityId == 0) return new NpcResolution(false, "请先与对应的 NPC 交互", 0);

                var shardId = _shardRouter?.Resolve((long)characterId) ?? 0L;

                var character = _clusterClient.GetGrain<ICharacterGrain>((long)characterId);
                var cached = await character.GetLastPositionAsync();
                if (cached is not { } p) return new NpcResolution(false, "读不到你的位置，无法结算", 0);

                var flaxPos = Sim.Server.QuestInteractionAuthority.ToFlax(p.X, p.Y, p.Z);
                var verdict = Sim.Server.QuestInteractionAuthority.Evaluate(_entityRegistry, shardId, entityId, flaxPos);
                if (!verdict.Ok) return new NpcResolution(false, "与目标的距离或身份校验未通过", 0);

                if (!NpcContent.TryGetConfig(verdict.TemplateId, out var config) || config is null)
                    return new NpcResolution(false, "找不到这个 NPC", 0);

                if (requiredServiceBit != 0 && (config.ServiceFlags & requiredServiceBit) == 0)
                    return new NpcResolution(false, requiredServiceBit == (uint)NpcServiceBits.CraftStation
                        ? "这里不是制作设施"
                        : "这位 NPC 不教配方", 0);

                return new NpcResolution(true, string.Empty, verdict.TemplateId);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "解析制作对象失败 CharacterId={CharacterId} EntityId={EntityId}", characterId, entityId);
                return new NpcResolution(false, "结算失败，请稍后再试", 0);
            }
        }

        /// <summary>背包快照 → (模板 id → 件数)。</summary>
        private async Task<Dictionary<long, int>> SnapshotHeldAsync(IInventoryGrain inventory)
        {
            var held = new Dictionary<long, int>();
            var info = await inventory.GetInventoryAsync();
            if (info?.Items == null) return held;

            for (var i = 0; i < info.Items.Count; i++)
            {
                var item = info.Items[i];
                if (item == null || item.TemplateId <= 0 || item.Count <= 0) continue;
                held.TryGetValue(item.TemplateId, out var sum);
                held[item.TemplateId] = sum + item.Count;
            }

            return held;
        }

        /// <summary>把内容快照里的配方翻成 Grain 认识的 DTO。</summary>
        private static CraftingRecipe BuildDefinition(NpcCraftRecipeSnapshot recipe)
        {
            var def = new CraftingRecipe
            {
                RecipeId = recipe.RecipeId,
                Name = recipe.Name,
                RequiredGold = recipe.CostGold,
                // 本工程没有"制作技能点"这一权威输入：skill_req 只作门槛（内容侧已校验），
                // 一期口径是"材料齐即可做"，所以成功率给 1.0（见设计文档 §11.10）。
                SuccessRate = 1.0f,
                IsRepeatable = true,
                RecipeType = "npc",
            };

            for (var i = 0; i < recipe.Materials.Length; i++)
            {
                var m = recipe.Materials[i];
                if (m.ItemId <= 0 || m.NeedCount <= 0) continue;
                def.RequiredMaterials[m.ItemId] = m.NeedCount;
            }

            if (recipe.ProductItemId > 0) def.OutputItems[recipe.ProductItemId] = 1;
            return def;
        }

        private async Task RefundMaterialsAsync(IInventoryGrain inventory, ulong characterId, List<long> itemIds, List<int> counts)
        {
            for (var i = 0; i < itemIds.Count && i < counts.Count; i++)
            {
                var restored = await inventory.AddItemAsync((int)itemIds[i], counts[i]);
                if (!restored)
                {
                    Logger.LogError("制作回滚失败：材料 {ItemId} x{Count} 未能退回 (CharacterId={CharacterId})",
                        itemIds[i], counts[i], characterId);
                }
            }
        }

        /// <summary>
        /// 学配方失败时把学费与教学材料原路退回（与技能分支同一条纪律：先扣、不成退回）。
        /// <para>退不掉的留下 Error —— "钱没了图纸也没学成"是唯一不可接受的状态。</para>
        /// </summary>
        private async Task RefundRecipeLearnAsync(ICurrencyGrain? wallet, long gold, IInventoryGrain? inventory,
            int itemId, int itemCount, string label)
        {
            if (wallet != null && gold > 0)
            {
                var back = await wallet.AddAsync(WalletCurrency.Gold, gold, label);
                if (!back.Ok) Logger.LogError("学配方回滚失败：学费 {Gold} 未能退回（{Label}）", gold, label);
            }

            if (inventory != null && itemId > 0 && itemCount > 0)
            {
                var restored = await inventory.AddItemAsync(itemId, itemCount);
                if (!restored) Logger.LogError("学配方回滚失败：材料 {ItemId} x{Count} 未能退回（{Label}）", itemId, itemCount, label);
            }
        }
    }
}

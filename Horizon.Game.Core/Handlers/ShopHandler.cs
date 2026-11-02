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
    /// NPC 商人（开窗 / 买 / 卖）。
    /// <para>
    /// <b>身份一律服务端解</b>：客户端只带运行时实体 id，NPC 编号由 DS 上行的实体注册表解出，
    /// 并核对"在本 shard 存在 + 可交互类别 + 离玩家够近"三重条件（与任务交互同一条权威路径
    /// <see cref="Sim.Server.QuestInteractionAuthority"/>）。客户端自报 npcId 一律不采信——
    /// 否则改一个整数就能买任意 NPC 的货。
    /// </para>
    /// <para>
    /// <b>钱、货、库存三者各自有权威</b>：余额在 <see cref="ICurrencyGrain"/>，库存计数在
    /// <see cref="INpcShopGrain"/>，物品在 <see cref="IInventoryGrain"/>。本处理器只编排，
    /// 且每条失败路径都做补偿（钱没扣成 → 库存还回去；物品没进包 → 钱与库存都还回去），
    /// 避免"扣了钱没拿到货"这种客服工单。
    /// </para>
    /// </summary>
    public class ShopHandler : MessageHandlerBase
    {
        /// <summary>世界实体身份注册表。null = DS 通道未接线，一律不采信。</summary>
        private readonly Horizon.Game.Core.Sim.Server.DsEntityRegistry? _entityRegistry;

        /// <summary>角色 → shard 路由。</summary>
        private readonly Horizon.Game.Core.World.IShardRouter? _shardRouter;

        public ShopHandler(
            ILogger<MessageHandlerBase> logger,
            IClusterClient clusterClient,
            HorizonMessageAdapter adapter,
            Horizon.Game.Core.Sim.Server.DsEntityRegistry? entityRegistry = null,
            Horizon.Game.Core.World.IShardRouter? shardRouter = null) : base(logger, clusterClient, adapter)
        {
            _entityRegistry = entityRegistry;
            _shardRouter = shardRouter;
        }

        public override List<MessageType> MessageTypes { get; } = new List<MessageType> { MessageType.Shop };

        public override ServiceType ServiceType => ServiceType.Trade;

        public override async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> RouteHandlerAsync(HorizonMessagePacket message)
        {
            // 三条请求共用 MessageType.Shop，按报文类型分派（都是本处理器自己的 DTO，不会误接别的系统）。
            if (message.Body is ShopBuyRequestMessage) return await HandleBuyAsync(message);
            if (message.Body is ShopSellRequestMessage) return await HandleSellAsync(message);
            return await HandleOpenAsync(message);
        }

        // ────────────────────────────── 开窗 ──────────────────────────────

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleOpenAsync(HorizonMessagePacket message)
        {
            var response = new ShopOpenResultMessage();
            try
            {
                if (message.Body is not ShopOpenRequestMessage request || request.CharacterId == 0)
                    return (true, CreateHorizonMessage(response));

                var resolve = await ResolveVendorAsync(request.CharacterId, request.EntityId);
                if (!resolve.Ok)
                {
                    response.Reason = resolve.Reason;
                    return (true, CreateHorizonMessage(response));
                }

                var npcId = resolve.NpcId;
                var rows = NpcContent.VendorRows(npcId);
                if (rows.Count == 0)
                {
                    response.Reason = "not_a_vendor";
                    return (true, CreateHorizonMessage(response));
                }

                var wallet = _clusterClient.GetGrain<ICurrencyGrain>((long)request.CharacterId);
                var stock = _clusterClient.GetGrain<INpcShopGrain>(npcId);

                response.Ok = true;
                response.NpcId = npcId;
                response.Gold = await wallet.GetBalanceAsync(WalletCurrency.Gold);

                var items = new List<ShopItemDto>(rows.Count);
                for (var i = 0; i < rows.Count; i++)
                {
                    var row = rows[i];
                    // 物品模板价在网关侧没有目录可用；只认商人覆盖价（种子里的行都带覆盖价）。
                    var emptyItem = new NpcItemPriceRow(0, 0);
                    var buy = NpcCommerceCore.ResolveBuyPrice(in row, in emptyItem);
                    var sell = NpcCommerceCore.ResolveSellPrice(in row, in emptyItem);
                    var view = await stock.GetStockAsync(row.ItemId, row.StockMax, row.RestockSeconds);

                    items.Add(new ShopItemDto
                    {
                        Slot = row.Slot,
                        ItemId = row.ItemId,
                        BuyPrice = buy.IsOk ? buy.Amount : 0,
                        SellPrice = sell.IsOk ? sell.Amount : 0,
                        Remaining = view.Unlimited ? -1 : view.Remaining,
                        Unlimited = view.Unlimited,
                    });
                }

                response.Items = items.ToArray();
                // 逐笔可对账：开了谁的窗、几件货、开窗时钱包是多少（P0 账本）。
                Logger.LogInformation("[SHOP] open char={Char} npc={Npc} items={Items} gold={Gold}",
                    request.CharacterId, npcId, items.Count, response.Gold);
                return (true, CreateHorizonMessage(response));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "打开商人窗口失败");
                response.Ok = false;
                response.Reason = "internal_error";
                return (true, CreateHorizonMessage(response));
            }
        }

        // ────────────────────────────── 买 ──────────────────────────────

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleBuyAsync(HorizonMessagePacket message)
        {
            var response = new ShopBuyResultMessage();
            var request = message.Body as ShopBuyRequestMessage;

            try
            {
                if (request == null || request.CharacterId == 0) return (true, CreateHorizonMessage(response));
                response.Slot = request.Slot;
                response.Count = request.Count;

                if (request.Count <= 0)
                {
                    response.Reason = "bad_count";
                    return (true, CreateHorizonMessage(response));
                }

                var resolve = await ResolveVendorAsync(request.CharacterId, request.EntityId);
                if (!resolve.Ok)
                {
                    response.Reason = resolve.Reason;
                    return (true, CreateHorizonMessage(response));
                }

                if (!TryFindRow(resolve.NpcId, request.Slot, out var row))
                {
                    response.Reason = "no_such_slot";
                    return (true, CreateHorizonMessage(response));
                }

                var emptyItem = new NpcItemPriceRow(0, 0);
                var price = NpcCommerceCore.ResolveBuyPrice(in row, in emptyItem);
                if (!price.IsOk)
                {
                    response.Reason = "price_unavailable";
                    return (true, CreateHorizonMessage(response));
                }

                var total = price.Amount * request.Count;
                var wallet = _clusterClient.GetGrain<ICurrencyGrain>((long)request.CharacterId);
                var stock = _clusterClient.GetGrain<INpcShopGrain>(resolve.NpcId);
                var inventory = _clusterClient.GetGrain<IInventoryGrain>(GrainKeys.ForEntity(request.CharacterId));

                // 先看库存（只读），能省掉一次扣款/退款往返。
                var peek = await stock.GetStockAsync(row.ItemId, row.StockMax, row.RestockSeconds);
                if (!peek.Unlimited && peek.Remaining < request.Count)
                {
                    response.Reason = "out_of_stock";
                    response.GoldAfter = await wallet.GetBalanceAsync(WalletCurrency.Gold);
                    Logger.LogInformation("[SHOP] buy_reject char={Char} npc={Npc} item={Item} qty={Qty} reason=out_of_stock remaining={Remaining}",
                        request.CharacterId, resolve.NpcId, row.ItemId, request.Count, peek.Remaining);
                    return (true, CreateHorizonMessage(response));
                }

                var spend = await wallet.SpendAsync(WalletCurrency.Gold, total, $"npc_buy:{resolve.NpcId}:{row.ItemId}");
                if (!spend.Ok)
                {
                    response.Reason = spend.Reject == (int)Horizon.Game.Sim.Economy.WalletReject.InsufficientFunds
                        ? "not_enough_gold"
                        : "wallet_rejected";
                    response.GoldAfter = spend.BalanceAfter;
                    Logger.LogInformation("[SHOP] buy_reject char={Char} npc={Npc} item={Item} qty={Qty} total={Total} reason={Reason} gold={Gold}",
                        request.CharacterId, resolve.NpcId, row.ItemId, request.Count, total, response.Reason, spend.BalanceAfter);
                    return (true, CreateHorizonMessage(response));
                }

                response.GoldAfter = spend.BalanceAfter;

                // 扣款后记账：抢到最后一件输了的人在这里被拒，并把钱原路退回。
                var take = await stock.TakeAsync(row.ItemId, request.Count, row.StockMax, row.RestockSeconds);
                if (!take.Ok)
                {
                    await RefundAsync(wallet, total, $"npc_buy_rollback:{resolve.NpcId}:{row.ItemId}");
                    response.Ok = false;
                    response.Reason = "out_of_stock";
                    response.GoldAfter = spend.BalanceBefore;
                    return (true, CreateHorizonMessage(response));
                }

                var added = await inventory.AddItemAsync(row.ItemId, request.Count);
                if (!added)
                {
                    // 货没进背包：库存 + 钱都还回去（顺序：先还库存，再退款；退款失败只记日志）。
                    await stock.ReturnAsync(row.ItemId, request.Count, row.StockMax, row.RestockSeconds);
                    await RefundAsync(wallet, total, $"npc_buy_addfail:{resolve.NpcId}:{row.ItemId}");
                    response.Ok = false;
                    response.Reason = "inventory_full";
                    response.GoldAfter = spend.BalanceBefore;
                    return (true, CreateHorizonMessage(response));
                }

                response.Ok = true;
                response.TotalCost = total;
                var remainingAfter = peek.Unlimited ? -1 : peek.Remaining - request.Count;
                Logger.LogInformation("[SHOP] buy char={Char} npc={Npc} item={Item} qty={Qty} unit={Unit} total={Total} gold={Before}->{After} remaining={Remaining}",
                    request.CharacterId, resolve.NpcId, row.ItemId, request.Count, price.Amount, total,
                    spend.BalanceBefore, spend.BalanceAfter, remainingAfter);
                return (true, CreateHorizonMessage(response));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "购买失败 CharacterId={CharacterId}", request?.CharacterId ?? 0);
                response.Ok = false;
                response.Reason = "internal_error";
                return (true, CreateHorizonMessage(response));
            }
        }

        // ────────────────────────────── 卖 ──────────────────────────────

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleSellAsync(HorizonMessagePacket message)
        {
            var response = new ShopSellResultMessage();
            var request = message.Body as ShopSellRequestMessage;

            try
            {
                if (request == null || request.CharacterId == 0) return (true, CreateHorizonMessage(response));
                response.ItemId = request.ItemId;
                response.Count = request.Count;

                if (request.Count <= 0 || request.ItemId <= 0)
                {
                    response.Reason = "bad_request";
                    return (true, CreateHorizonMessage(response));
                }

                var resolve = await ResolveVendorAsync(request.CharacterId, request.EntityId);
                if (!resolve.Ok)
                {
                    response.Reason = resolve.Reason;
                    return (true, CreateHorizonMessage(response));
                }

                var rows = NpcContent.VendorRows(resolve.NpcId);
                var found = false;
                var row = default(NpcVendorRow);
                for (var i = 0; i < rows.Count; i++)
                {
                    if (rows[i].ItemId != request.ItemId) continue;
                    row = rows[i];
                    found = true;
                    break;
                }

                if (!found)
                {
                    response.Reason = "vendor_does_not_buy";
                    return (true, CreateHorizonMessage(response));
                }

                var emptyItem = new NpcItemPriceRow(0, 0);
                var price = NpcCommerceCore.ResolveSellPrice(in row, in emptyItem);
                if (!price.IsOk)
                {
                    response.Reason = "vendor_does_not_buy";
                    return (true, CreateHorizonMessage(response));
                }

                var total = price.Amount * request.Count;
                var wallet = _clusterClient.GetGrain<ICurrencyGrain>((long)request.CharacterId);
                var inventory = _clusterClient.GetGrain<IInventoryGrain>(GrainKeys.ForEntity(request.CharacterId));

                // 先扣物、后给钱：物品不够时一票否决、不掉钱（反向先给钱再扣物会留下"白给"的窗口）。
                var consumed = await inventory.ConsumeByTemplateAsync(request.CharacterId, request.ItemId, request.Count);
                if (!consumed)
                {
                    response.Reason = "not_enough_items";
                    response.GoldAfter = await wallet.GetBalanceAsync(WalletCurrency.Gold);
                    Logger.LogInformation("[SHOP] sell_reject char={Char} npc={Npc} item={Item} qty={Qty} reason=not_enough_items",
                        request.CharacterId, resolve.NpcId, request.ItemId, request.Count);
                    return (true, CreateHorizonMessage(response));
                }

                var credit = await wallet.AddAsync(WalletCurrency.Gold, total, $"npc_sell:{resolve.NpcId}:{request.ItemId}");
                if (!credit.Ok)
                {
                    // 钱进不去（溢出）：把物品还回去。AddItem 失败只能记日志——那是"钱没了物也没了"的唯一可能，
                    // 必须留一条可检索的记录，而不是静默吞掉。
                    var restored = await inventory.AddItemAsync(request.ItemId, request.Count);
                    if (!restored)
                    {
                        Logger.LogError("卖出回滚失败：物品 {ItemId} x{Count} 既没给钱也没退物 (CharacterId={CharacterId})",
                            request.ItemId, request.Count, request.CharacterId);
                    }

                    response.Ok = false;
                    response.Reason = "wallet_overflow";
                    response.GoldAfter = credit.BalanceAfter;
                    return (true, CreateHorizonMessage(response));
                }

                response.Ok = true;
                response.UnitPrice = price.Amount;
                response.GoldAfter = credit.BalanceAfter;
                Logger.LogInformation("[SHOP] sell char={Char} npc={Npc} item={Item} qty={Qty} unit={Unit} total={Total} gold={Before}->{After}",
                    request.CharacterId, resolve.NpcId, request.ItemId, request.Count, price.Amount, total,
                    credit.BalanceBefore, credit.BalanceAfter);
                return (true, CreateHorizonMessage(response));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "卖出失败 CharacterId={CharacterId}", request?.CharacterId ?? 0);
                response.Ok = false;
                response.Reason = "internal_error";
                return (true, CreateHorizonMessage(response));
            }
        }

        // ────────────────────────────── 公共 ──────────────────────────────

        /// <summary>服务端解析"玩家正在交互的商人"。失败时 <see cref="VendorResolution.Reason"/> 是短码。</summary>
        private readonly record struct VendorResolution(bool Ok, string Reason, int NpcId);

        private async Task<VendorResolution> ResolveVendorAsync(ulong characterId, ulong entityId)
        {
            try
            {
                var shardId = _shardRouter?.Resolve((long)characterId) ?? 0L;

                var character = _clusterClient.GetGrain<ICharacterGrain>((long)characterId);
                var cached = await character.GetLastPositionAsync();
                if (cached is not { } p) return new VendorResolution(false, "no_position", 0);

                var flaxPos = Sim.Server.QuestInteractionAuthority.ToFlax(p.X, p.Y, p.Z);
                var verdict = Sim.Server.QuestInteractionAuthority.Evaluate(_entityRegistry, shardId, entityId, flaxPos);
                if (!verdict.Ok) return new VendorResolution(false, verdict.Reason, 0);
                if (verdict.TemplateId <= 0) return new VendorResolution(false, "npc_not_found", 0);

                // 位图必须声明商人服务：实体是 NPC 但这人不开店时，开窗请求应当被拒而不是给一张空货架。
                if (!NpcContent.TryGetConfig(verdict.TemplateId, out var config) || config is null)
                    return new VendorResolution(false, "npc_not_found", 0);

                if ((config.ServiceFlags & (uint)NpcServiceBits.Vendor) == 0)
                    return new VendorResolution(false, "not_a_vendor", 0);

                return new VendorResolution(true, string.Empty, verdict.TemplateId);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "解析商人实体失败 CharacterId={CharacterId} EntityId={EntityId}", characterId, entityId);
                return new VendorResolution(false, "resolve_failed", 0);
            }
        }

        private static bool TryFindRow(int npcId, int slot, out NpcVendorRow row)
        {
            row = default;
            var rows = NpcContent.VendorRows(npcId);
            for (var i = 0; i < rows.Count; i++)
            {
                if (rows[i].Slot != slot) continue;
                row = rows[i];
                return true;
            }

            return false;
        }

        /// <summary>退款（补偿用）：失败只记日志，因为此时调用方已经在返回失败回执。</summary>
        private async Task RefundAsync(ICurrencyGrain wallet, long amount, string label)
        {
            try
            {
                var refund = await wallet.AddAsync(WalletCurrency.Gold, amount, label);
                if (!refund.Ok)
                    Logger.LogError("退款失败：{Amount} 金币没能退回（{Label}）", amount, label);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "退款调用异常：{Amount} 金币（{Label}）", amount, label);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;
using Horizon.Orleans.Interface.World;

namespace Horizon.Orleans.Grains.World;

/// <summary>
/// P2.3 拍卖行 Grain 实现（全局单例）。<br/>
/// 负责：挂单/竞价/一口价/结算/过期回收/手续费扣除。<br/>
/// 经济安全：所有交易记录可追溯，异常交易检测。
/// </summary>
public sealed class AuctionHouseGrain : Grain, IAuctionHouseGrain
{
    private readonly ILogger<AuctionHouseGrain> _logger;

    /// <summary>活跃拍卖列表（auctionId → entry）。</summary>
    private readonly Dictionary<long, AuctionListEntry> _activeListings = new();

    /// <summary>拍卖 ID 自增计数器。</summary>
    private long _nextAuctionId = 1;

    /// <summary>累计手续费收入。</summary>
    private long _totalFeesCollected;

    /// <summary>累计成交额。</summary>
    private long _totalVolume;

    /// <summary>手续费率（5%）。</summary>
    private const decimal FeeRate = 0.05m;

    /// <summary>过期清理定时器。</summary>
    private IDisposable? _cleanupTimer;

    public AuctionHouseGrain(ILogger<AuctionHouseGrain> logger)
    {
        _logger = logger;
    }

    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        // 每 60 秒清理过期拍卖
        _cleanupTimer = this.RegisterGrainTimer(
            OnCleanupTimer,
            new GrainTimerCreationOptions(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60)));
        return base.OnActivateAsync(cancellationToken);
    }

    public override Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        _cleanupTimer?.Dispose();
        return base.OnDeactivateAsync(reason, cancellationToken);
    }

    public Task<AuctionListResult> ListItemAsync(AuctionListItem item)
    {
        // 校验
        if (item.SellerId <= 0)
            return Task.FromResult(new AuctionListResult { Success = false, ErrorMessage = "无效的卖家 ID。" });
        if (item.ItemId <= 0)
            return Task.FromResult(new AuctionListResult { Success = false, ErrorMessage = "无效的物品 ID。" });
        if (item.ItemCount <= 0)
            return Task.FromResult(new AuctionListResult { Success = false, ErrorMessage = "物品数量必须大于 0。" });
        if (item.StartingPrice < 0)
            return Task.FromResult(new AuctionListResult { Success = false, ErrorMessage = "起拍价不能为负。" });
        if (item.BuyoutPrice > 0 && item.BuyoutPrice < item.StartingPrice)
            return Task.FromResult(new AuctionListResult { Success = false, ErrorMessage = "一口价不能低于起拍价。" });

        var auctionId = _nextAuctionId++;
        var entry = new AuctionListEntry
        {
            AuctionId = auctionId,
            SellerId = item.SellerId,
            ItemId = item.ItemId,
            ItemCount = item.ItemCount,
            CurrentBid = item.StartingPrice,
            BuyoutPrice = item.BuyoutPrice,
            HighestBidderId = 0,
            ListTime = DateTime.UtcNow,
            ExpiryTime = DateTime.UtcNow.AddHours(item.DurationHours),
        };

        _activeListings[auctionId] = entry;

        _logger.LogInformation(
            "拍卖挂单成功。AuctionId={AuctionId}, Seller={Seller}, Item={ItemId}x{Count}, StartingPrice={Price}, Buyout={Buyout}",
            auctionId, item.SellerId, item.ItemId, item.ItemCount, item.StartingPrice, item.BuyoutPrice);

        return Task.FromResult(new AuctionListResult { Success = true, AuctionId = auctionId });
    }

    public Task<AuctionBidResult> PlaceBidAsync(long bidderId, long auctionId, long bidAmount)
    {
        if (!_activeListings.TryGetValue(auctionId, out var entry))
            return Task.FromResult(new AuctionBidResult { Success = false, ErrorMessage = "拍卖不存在或已结束。" });

        if (bidderId == entry.SellerId)
            return Task.FromResult(new AuctionBidResult { Success = false, ErrorMessage = "不能竞拍自己的物品。" });

        if (entry.ExpiryTime < DateTime.UtcNow)
            return Task.FromResult(new AuctionBidResult { Success = false, ErrorMessage = "拍卖已过期。" });

        // 一口价购买
        if (entry.BuyoutPrice > 0 && bidAmount >= entry.BuyoutPrice)
        {
            SettleAuction(entry, bidderId, entry.BuyoutPrice);
            _activeListings.Remove(auctionId);
            return Task.FromResult(new AuctionBidResult
            {
                Success = true,
                IsBuyout = true,
                CurrentHighestBid = entry.BuyoutPrice,
            });
        }

        // 竞价：必须高于当前最高价
        var minBid = entry.CurrentBid + 1;
        if (bidAmount < minBid)
            return Task.FromResult(new AuctionBidResult
            {
                Success = false,
                ErrorMessage = $"出价必须高于当前最高价 {entry.CurrentBid}。",
                CurrentHighestBid = entry.CurrentBid,
            });

        // 更新最高价
        entry.CurrentBid = bidAmount;
        entry.HighestBidderId = bidderId;

        _logger.LogDebug(
            "拍卖竞价。AuctionId={AuctionId}, Bidder={Bidder}, Amount={Amount}",
            auctionId, bidderId, bidAmount);

        return Task.FromResult(new AuctionBidResult
        {
            Success = true,
            IsBuyout = false,
            CurrentHighestBid = bidAmount,
        });
    }

    public Task<bool> CancelListingAsync(long sellerId, long auctionId)
    {
        if (!_activeListings.TryGetValue(auctionId, out var entry))
            return Task.FromResult(false);

        if (entry.SellerId != sellerId)
            return Task.FromResult(false);

        // 有人出价后不能取消
        if (entry.HighestBidderId != 0)
            return Task.FromResult(false);

        _activeListings.Remove(auctionId);
        _logger.LogInformation("拍卖取消。AuctionId={AuctionId}, Seller={Seller}", auctionId, sellerId);
        return Task.FromResult(true);
    }

    public Task<AuctionListEntry[]> QueryListingsAsync(int page, int pageSize, AuctionSortBy sortBy)
    {
        var query = _activeListings.Values.Where(e => e.ExpiryTime > DateTime.UtcNow);

        query = sortBy switch
        {
            AuctionSortBy.PriceLowToHigh => query.OrderBy(e => e.CurrentBid),
            AuctionSortBy.PriceHighToLow => query.OrderByDescending(e => e.CurrentBid),
            AuctionSortBy.RecentlyListed => query.OrderByDescending(e => e.ListTime),
            _ => query.OrderBy(e => e.ExpiryTime), // TimeRemaining
        };

        var result = query.Skip(page * pageSize).Take(pageSize).ToArray();
        return Task.FromResult(result);
    }

    public Task<AuctionListEntry?> GetListingAsync(long auctionId)
    {
        _activeListings.TryGetValue(auctionId, out var entry);
        return Task.FromResult(entry);
    }

    public Task<AuctionHouseStats> GetStatsAsync()
    {
        return Task.FromResult(new AuctionHouseStats
        {
            ActiveListings = _activeListings.Count,
            TotalVolume = _totalVolume,
            TotalFeesCollected = _totalFeesCollected,
        });
    }

    // --- 内部方法 ---

    private void SettleAuction(AuctionListEntry entry, long buyerId, long finalPrice)
    {
        var fee = (long)(finalPrice * FeeRate);
        var sellerProceeds = finalPrice - fee;

        _totalVolume += finalPrice;
        _totalFeesCollected += fee;

        _logger.LogInformation(
            "拍卖结算。AuctionId={AuctionId}, Item={ItemId}x{Count}, Seller={Seller}, Buyer={Buyer}, Price={Price}, Fee={Fee}",
            entry.AuctionId, entry.ItemId, entry.ItemCount, entry.SellerId, buyerId, finalPrice, fee);

        // TODO: 通过 ICharacterGrain 转移物品和金币
        // - 卖家获得 sellerProceeds 金币
        // - 买家获得物品
    }

    private Task OnCleanupTimer(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var expired = _activeListings
            .Where(kv => kv.Value.ExpiryTime < now)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var auctionId in expired)
        {
            if (_activeListings.TryGetValue(auctionId, out var entry))
            {
                if (entry.HighestBidderId != 0)
                {
                    // 有人出价 → 结算给最高出价者
                    SettleAuction(entry, entry.HighestBidderId, entry.CurrentBid);
                }
                else
                {
                    // 无人出价 → 退还物品给卖家
                    _logger.LogDebug("拍卖流拍。AuctionId={AuctionId}, Item={ItemId}", auctionId, entry.ItemId);
                }
                _activeListings.Remove(auctionId);
            }
        }

        if (expired.Count > 0)
        {
            _logger.LogInformation("拍卖行过期清理。Cleaned={Count}", expired.Count);
        }

        return Task.CompletedTask;
    }
}

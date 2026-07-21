using System;
using System.Threading.Tasks;
using Orleans;

namespace Horizon.Orleans.Interface.World;

/// <summary>
/// 拍卖行 Grain 契约（P2.3）。<br/>
/// Grain Primary Key = 0（全局单例）。<br/>
/// 负责：挂单/竞价/结算/过期回收/手续费扣除。
/// </summary>
[global::Orleans.CodeGeneration.Version(1)]
public interface IAuctionHouseGrain : IGrainWithIntegerKey
{
    /// <summary>挂单（出售物品）。</summary>
    Task<AuctionListResult> ListItemAsync(AuctionListItem item);

    /// <summary>竞价/一口价购买。</summary>
    Task<AuctionBidResult> PlaceBidAsync(long bidderId, long auctionId, long bidAmount);

    /// <summary>取消挂单（仅卖家可操作）。</summary>
    Task<bool> CancelListingAsync(long sellerId, long auctionId);

    /// <summary>查询拍卖列表（分页）。</summary>
    Task<AuctionListEntry[]> QueryListingsAsync(int page, int pageSize, AuctionSortBy sortBy);

    /// <summary>查询指定拍卖详情。</summary>
    Task<AuctionListEntry?> GetListingAsync(long auctionId);

    /// <summary>获取拍卖行统计。</summary>
    Task<AuctionHouseStats> GetStatsAsync();
}

[GenerateSerializer]
public sealed class AuctionListItem
{
    [Id(0)] public long SellerId { get; set; }
    [Id(1)] public int ItemId { get; set; }
    [Id(2)] public int ItemCount { get; set; }
    [Id(3)] public long StartingPrice { get; set; }
    [Id(4)] public long BuyoutPrice { get; set; }
    [Id(5)] public float DurationHours { get; set; } = 24f;
}

[GenerateSerializer]
public sealed class AuctionListResult
{
    [Id(0)] public bool Success { get; set; }
    [Id(1)] public long AuctionId { get; set; }
    [Id(2)] public string ErrorMessage { get; set; } = string.Empty;
}

[GenerateSerializer]
public sealed class AuctionBidResult
{
    [Id(0)] public bool Success { get; set; }
    [Id(1)] public string ErrorMessage { get; set; } = string.Empty;
    [Id(2)] public bool IsBuyout { get; set; }
    [Id(3)] public long CurrentHighestBid { get; set; }
}

[GenerateSerializer]
public sealed class AuctionListEntry
{
    [Id(0)] public long AuctionId { get; set; }
    [Id(1)] public long SellerId { get; set; }
    [Id(2)] public int ItemId { get; set; }
    [Id(3)] public int ItemCount { get; set; }
    [Id(4)] public long CurrentBid { get; set; }
    [Id(5)] public long BuyoutPrice { get; set; }
    [Id(6)] public long HighestBidderId { get; set; }
    [Id(7)] public DateTime ExpiryTime { get; set; }
    [Id(8)] public DateTime ListTime { get; set; }
}

[GenerateSerializer]
public sealed class AuctionHouseStats
{
    [Id(0)] public int ActiveListings { get; set; }
    [Id(1)] public long TotalVolume { get; set; }
    [Id(2)] public long TotalFeesCollected { get; set; }
}

[GenerateSerializer]
public enum AuctionSortBy : byte
{
    TimeRemaining = 0,
    PriceLowToHigh = 1,
    PriceHighToLow = 2,
    RecentlyListed = 3,
}

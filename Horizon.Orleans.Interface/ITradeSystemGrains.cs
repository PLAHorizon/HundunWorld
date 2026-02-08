using Orleans;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MemoryPack;

namespace Horizon.Orleans.Interface
{
    /// <summary>
    /// 交易系统Grain接口 - 负责面对面交易管理
    /// </summary>
    [global::Orleans.CodeGeneration.Version(1)]
    public interface ITradeGrain : IGrainWithGuidKey
    {
        /// <summary>
        /// 创建面对面交易
        /// </summary>
        Task<bool> CreateTradeAsync(Guid sellerId, Guid buyerId);

        /// <summary>
        /// 添加交易物品
        /// </summary>
        Task<bool> AddTradeItemAsync(Guid playerId, long itemId, int quantity);

        /// <summary>
        /// 移除交易物品
        /// </summary>
        Task<bool> RemoveTradeItemAsync(Guid playerId, long itemId);

        /// <summary>
        /// 设置交易货币
        /// </summary>
        Task<bool> SetTradeCurrencyAsync(Guid playerId, long amount);

        /// <summary>
        /// 确认交易
        /// </summary>
        Task<bool> ConfirmTradeAsync(Guid playerId);

        /// <summary>
        /// 取消交易
        /// </summary>
        Task<bool> CancelTradeAsync(Guid playerId);

        /// <summary>
        /// 获取交易信息
        /// </summary>
        Task<TradeInfo> GetTradeInfoAsync();

        /// <summary>
        /// 执行交易（双方确认后）
        /// </summary>
        Task<TradeResult> ExecuteTradeAsync();
    }

    /// <summary>
    /// 市场系统Grain接口 - 负责拍卖行/摆摊管理
    /// </summary>
    [global::Orleans.CodeGeneration.Version(1)]
    public interface IMarketGrain : IGrainWithIntegerKey
    {
        /// <summary>
        /// 上架物品
        /// </summary>
        Task<MarketListing> ListItemAsync(Guid sellerId, long itemId, int quantity, long price, int currencyType);

        /// <summary>
        /// 下架物品
        /// </summary>
        Task<bool> DelistItemAsync(Guid sellerId, long listingId);

        /// <summary>
        /// 购买物品
        /// </summary>
        Task<TradeResult> PurchaseItemAsync(Guid buyerId, long listingId);

        /// <summary>
        /// 搜索市场商品
        /// </summary>
        Task<List<MarketListing>> SearchListingsAsync(string keyword, int category, int sortBy);

        /// <summary>
        /// 获取玩家上架列表
        /// </summary>
        Task<List<MarketListing>> GetPlayerListingsAsync(Guid playerId);

        /// <summary>
        /// 获取市场统计信息
        /// </summary>
        Task<MarketStats> GetMarketStatsAsync();
    }

    #region 交易系统数据模型

    /// <summary>
    /// 交易状态枚举
    /// </summary>
    public enum TradeStatus
    {
        /// <summary>
        /// 已创建
        /// </summary>
        Created = 0,

        /// <summary>
        /// 双方确认
        /// </summary>
        BothConfirmed = 1,

        /// <summary>
        /// 已完成
        /// </summary>
        Completed = 2,

        /// <summary>
        /// 已取消
        /// </summary>
        Cancelled = 3,

        /// <summary>
        /// 失败
        /// </summary>
        Failed = 4
    }

    /// <summary>
    /// 市场商品状态枚举
    /// </summary>
    public enum MarketListingStatus
    {
        /// <summary>
        /// 上架中
        /// </summary>
        Active = 0,

        /// <summary>
        /// 已售出
        /// </summary>
        Sold = 1,

        /// <summary>
        /// 已下架
        /// </summary>
        Delisted = 2,

        /// <summary>
        /// 已过期
        /// </summary>
        Expired = 3
    }

    /// <summary>
    /// 交易信息
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class TradeInfo
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public Guid TradeId { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public Guid SellerId { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public Guid BuyerId { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public List<TradeItem> SellerItems { get; set; } = new();

        [MemoryPackOrder(4)]
        [Id(4)]
        public List<TradeItem> BuyerItems { get; set; } = new();

        [MemoryPackOrder(5)]
        [Id(5)]
        public long SellerCurrency { get; set; }

        [MemoryPackOrder(6)]
        [Id(6)]
        public long BuyerCurrency { get; set; }

        [MemoryPackOrder(7)]
        [Id(7)]
        public bool SellerConfirmed { get; set; }

        [MemoryPackOrder(8)]
        [Id(8)]
        public bool BuyerConfirmed { get; set; }

        [MemoryPackOrder(9)]
        [Id(9)]
        public int Status { get; set; }

        [MemoryPackOrder(10)]
        [Id(10)]
        public DateTime CreatedTime { get; set; }
    }

    /// <summary>
    /// 交易物品
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class TradeItem
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public long ItemId { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public int Quantity { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public string ItemName { get; set; } = "";
    }

    /// <summary>
    /// 交易结果
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class TradeResult
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public bool Success { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public string Message { get; set; } = "";

        [MemoryPackOrder(2)]
        [Id(2)]
        public Guid TradeId { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public long TotalAmount { get; set; }

        [MemoryPackOrder(4)]
        [Id(4)]
        public long Tax { get; set; }
    }

    /// <summary>
    /// 市场商品信息
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class MarketListing
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public long ListingId { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public Guid SellerId { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public string SellerName { get; set; } = "";

        [MemoryPackOrder(3)]
        [Id(3)]
        public long ItemId { get; set; }

        [MemoryPackOrder(4)]
        [Id(4)]
        public string ItemName { get; set; } = "";

        [MemoryPackOrder(5)]
        [Id(5)]
        public int Quantity { get; set; }

        [MemoryPackOrder(6)]
        [Id(6)]
        public long Price { get; set; }

        [MemoryPackOrder(7)]
        [Id(7)]
        public int CurrencyType { get; set; }

        [MemoryPackOrder(8)]
        [Id(8)]
        public DateTime ListTime { get; set; }

        [MemoryPackOrder(9)]
        [Id(9)]
        public int Status { get; set; }

        [MemoryPackOrder(10)]
        [Id(10)]
        public int Category { get; set; }
    }

    /// <summary>
    /// 市场统计信息
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class MarketStats
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public int TotalListings { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public long TotalTransactions { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public long TotalVolume { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public int ActiveListings { get; set; }
    }

    #endregion
}

using Orleans;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MemoryPack;
using Horizon.Game.Message.Network;

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
}

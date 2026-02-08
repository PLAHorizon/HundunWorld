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
    /// 市场系统Grain实现 - 负责拍卖行/摆摊管理
    /// </summary>
    public class MarketGrain : Grain, IMarketGrain
    {
        private readonly ILogger<MarketGrain> _logger;
        private readonly IPersistentState<MarketState> _marketState;

        /// <summary>
        /// 商品过期时间（72小时）
        /// </summary>
        private static readonly TimeSpan ListingExpiration = TimeSpan.FromHours(72);

        /// <summary>
        /// 市场交易税率（3%）
        /// </summary>
        private const decimal MarketTaxRate = 0.03m;

        public MarketGrain(
            ILogger<MarketGrain> logger,
            [PersistentState("market", "GameStore")] IPersistentState<MarketState> marketState)
        {
            _logger = logger;
            _marketState = marketState;
        }

        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("MarketGrain {GrainKey} activating.", this.GetPrimaryKeyLong());

            if (_marketState.State.Listings == null)
                _marketState.State.Listings = new Dictionary<long, MarketListing>();

            await base.OnActivateAsync(cancellationToken);
        }

        public async Task<MarketListing> ListItemAsync(Guid sellerId, long itemId, int quantity, long price, int currencyType)
        {
            try
            {
                if (sellerId == Guid.Empty)
                {
                    _logger.LogWarning("卖家ID无效");
                    return null;
                }

                if (itemId <= 0 || quantity <= 0)
                {
                    _logger.LogWarning("物品参数无效: ItemId={ItemId}, Quantity={Quantity}", itemId, quantity);
                    return null;
                }

                if (price <= 0)
                {
                    _logger.LogWarning("价格无效: Price={Price}", price);
                    return null;
                }

                var state = _marketState.State;
                var listingId = state.NextListingId++;

                var listing = new MarketListing
                {
                    ListingId = listingId,
                    SellerId = sellerId,
                    SellerName = "",
                    ItemId = itemId,
                    ItemName = "",
                    Quantity = quantity,
                    Price = price,
                    CurrencyType = currencyType,
                    ListTime = DateTime.UtcNow,
                    Status = (int)MarketListingStatus.Active,
                    Category = 0
                };

                state.Listings[listingId] = listing;
                await _marketState.WriteStateAsync();

                _logger.LogInformation("上架商品: ListingId={ListingId}, SellerId={SellerId}, ItemId={ItemId}, Price={Price}",
                    listingId, sellerId, itemId, price);
                return listing;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "上架商品失败: SellerId={SellerId}, ItemId={ItemId}", sellerId, itemId);
                throw;
            }
        }

        public async Task<bool> DelistItemAsync(Guid sellerId, long listingId)
        {
            try
            {
                var state = _marketState.State;

                if (!state.Listings.TryGetValue(listingId, out var listing))
                {
                    _logger.LogWarning("商品不存在: ListingId={ListingId}", listingId);
                    return false;
                }

                if (listing.SellerId != sellerId)
                {
                    _logger.LogWarning("无权下架他人商品: SellerId={SellerId}, ListingId={ListingId}", sellerId, listingId);
                    return false;
                }

                if (listing.Status != (int)MarketListingStatus.Active)
                {
                    _logger.LogWarning("商品状态无效，无法下架: Status={Status}", (MarketListingStatus)listing.Status);
                    return false;
                }

                listing.Status = (int)MarketListingStatus.Delisted;
                await _marketState.WriteStateAsync();

                _logger.LogInformation("下架商品: ListingId={ListingId}, SellerId={SellerId}", listingId, sellerId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "下架商品失败: ListingId={ListingId}", listingId);
                throw;
            }
        }

        public async Task<TradeResult> PurchaseItemAsync(Guid buyerId, long listingId)
        {
            try
            {
                var state = _marketState.State;

                if (buyerId == Guid.Empty)
                {
                    return new TradeResult
                    {
                        Success = false,
                        Message = "买家ID无效"
                    };
                }

                if (!state.Listings.TryGetValue(listingId, out var listing))
                {
                    return new TradeResult
                    {
                        Success = false,
                        Message = "商品不存在"
                    };
                }

                // Auto-expire check
                if (DateTime.UtcNow - listing.ListTime > ListingExpiration)
                {
                    listing.Status = (int)MarketListingStatus.Expired;
                    await _marketState.WriteStateAsync();

                    return new TradeResult
                    {
                        Success = false,
                        Message = "商品已过期"
                    };
                }

                if (listing.Status != (int)MarketListingStatus.Active)
                {
                    return new TradeResult
                    {
                        Success = false,
                        Message = "商品状态无效"
                    };
                }

                if (listing.SellerId == buyerId)
                {
                    return new TradeResult
                    {
                        Success = false,
                        Message = "不能购买自己的商品"
                    };
                }

                // Calculate 3% market tax
                long tax = (long)(listing.Price * MarketTaxRate);
                long totalAmount = listing.Price;

                listing.Status = (int)MarketListingStatus.Sold;
                state.TotalTransactions++;
                state.TotalVolume += totalAmount;

                await _marketState.WriteStateAsync();

                _logger.LogInformation("购买商品成功: ListingId={ListingId}, BuyerId={BuyerId}, Price={Price}, Tax={Tax}",
                    listingId, buyerId, totalAmount, tax);

                return new TradeResult
                {
                    Success = true,
                    Message = "购买成功",
                    TradeId = Guid.NewGuid(),
                    TotalAmount = totalAmount,
                    Tax = tax
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "购买商品失败: BuyerId={BuyerId}, ListingId={ListingId}", buyerId, listingId);
                throw;
            }
        }

        public Task<List<MarketListing>> SearchListingsAsync(string keyword, int category, int sortBy)
        {
            try
            {
                var state = _marketState.State;
                var now = DateTime.UtcNow;

                var query = state.Listings.Values
                    .Where(l => l.Status == (int)MarketListingStatus.Active)
                    .Where(l => now - l.ListTime <= ListingExpiration);

                // Filter by keyword
                if (!string.IsNullOrWhiteSpace(keyword))
                {
                    var kw = keyword.Trim();
                    query = query.Where(l =>
                        l.ItemName.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                        l.SellerName.Contains(kw, StringComparison.OrdinalIgnoreCase));
                }

                // Filter by category (0 = all)
                if (category > 0)
                {
                    query = query.Where(l => l.Category == category);
                }

                // Sort: 0=price asc, 1=price desc, 2=time desc, 3=name asc
                query = sortBy switch
                {
                    1 => query.OrderByDescending(l => l.Price),
                    2 => query.OrderByDescending(l => l.ListTime),
                    3 => query.OrderBy(l => l.ItemName),
                    _ => query.OrderBy(l => l.Price)
                };

                var results = query.ToList();
                return Task.FromResult(results);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "搜索商品失败: Keyword={Keyword}", keyword);
                throw;
            }
        }

        public Task<List<MarketListing>> GetPlayerListingsAsync(Guid playerId)
        {
            try
            {
                var listings = _marketState.State.Listings.Values
                    .Where(l => l.SellerId == playerId)
                    .ToList();
                return Task.FromResult(listings);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取玩家商品列表失败: PlayerId={PlayerId}", playerId);
                throw;
            }
        }

        public Task<MarketStats> GetMarketStatsAsync()
        {
            try
            {
                var state = _marketState.State;
                var now = DateTime.UtcNow;

                var activeListings = state.Listings.Values
                    .Count(l => l.Status == (int)MarketListingStatus.Active && now - l.ListTime <= ListingExpiration);

                var stats = new MarketStats
                {
                    TotalListings = state.Listings.Count,
                    TotalTransactions = state.TotalTransactions,
                    TotalVolume = state.TotalVolume,
                    ActiveListings = activeListings
                };

                return Task.FromResult(stats);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取市场统计信息失败");
                throw;
            }
        }
    }
}

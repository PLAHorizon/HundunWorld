using Horizon.Core.Abstract;
using Horizon.Entities;
using Horizon.Game.Message.Network;
using Horizon.Model.Flower;
using Horizon.Orleans.Interface;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Horizon.Orleans.Grains
{
    public class FlowerDashboardGrain : Grain, IDashboardGrain
    {
        private readonly ILogger<FlowerDashboardGrain> _logger;
        private readonly IDataContext<FlowerEntityContext, FlowerOrder, long> _orderContext;
        private readonly IDataContext<FlowerEntityContext, FlowerPaymentTransaction, long> _paymentContext;
        private readonly IDataContext<FlowerEntityContext, FlowerMarketSnapshot, long> _snapshotContext;
        private readonly IDataContext<FlowerEntityContext, FlowerDailyPriceStats, long> _statsContext;
        private readonly IDataContext<FlowerEntityContext, FlowerAlertLog, long> _alertContext;
        private readonly IDataContext<FlowerEntityContext, FlowerSpecies, long> _speciesContext;
        private readonly IDataContext<FlowerEntityContext, FlowerMerchant, long> _merchantContext;
        private readonly IDataContext<FlowerEntityContext, FlowerProduct, long> _productContext;
        private readonly IDataContext<FlowerEntityContext, FlowerOrderItem, long> _orderItemContext;
        private readonly IDataContext<FlowerEntityContext, FlowerMarket, long> _marketContext;
        private readonly IPersistentState<DashboardState> _state;

        public FlowerDashboardGrain(
            ILogger<FlowerDashboardGrain> logger,
            IDataContext<FlowerEntityContext, FlowerOrder, long> orderContext,
            IDataContext<FlowerEntityContext, FlowerPaymentTransaction, long> paymentContext,
            IDataContext<FlowerEntityContext, FlowerMarketSnapshot, long> snapshotContext,
            IDataContext<FlowerEntityContext, FlowerDailyPriceStats, long> statsContext,
            IDataContext<FlowerEntityContext, FlowerAlertLog, long> alertContext,
            IDataContext<FlowerEntityContext, FlowerSpecies, long> speciesContext,
            IDataContext<FlowerEntityContext, FlowerMerchant, long> merchantContext,
            IDataContext<FlowerEntityContext, FlowerProduct, long> productContext,
            IDataContext<FlowerEntityContext, FlowerOrderItem, long> orderItemContext,
            IDataContext<FlowerEntityContext, FlowerMarket, long> marketContext,
            [PersistentState("dashboard", "FlowerStore")] IPersistentState<DashboardState> state)
        {
            _logger = logger;
            _orderContext = orderContext;
            _paymentContext = paymentContext;
            _snapshotContext = snapshotContext;
            _statsContext = statsContext;
            _alertContext = alertContext;
            _speciesContext = speciesContext;
            _merchantContext = merchantContext;
            _productContext = productContext;
            _orderItemContext = orderItemContext;
            _marketContext = marketContext;
            _state = state;
        }

        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            if (_state.State.LastRefreshTime == default)
                _state.State.LastRefreshTime = DateTime.Now;

            await base.OnActivateAsync(cancellationToken);
        }

        public async Task<DashboardOverview> GetOverviewAsync()
        {
            try
            {
                var now = DateTime.Now;
                var today = now.Date;

                var todayOrders = await _orderContext.QueryAsync(
                    o => o.CreateTime >= today && o.CreateTime < now);
                var todayOrderList = todayOrders.ToList();

                var todayPayments = await _paymentContext.QueryAsync(
                    p => p.CreateTime >= today && p.PaidAt != null && p.PaidAt >= today);
                var todayPaymentList = todayPayments.ToList();

                var recentAlerts = await _alertContext.QueryAsync(
                    a => a.CreatedAt >= today);
                var alertList = recentAlerts.ToList();

                var overview = new DashboardOverview
                {
                    TotalTransactionAmount = todayPaymentList.Sum(p => p.Amount),
                    TotalOrderCount = todayOrderList.Count(o => o.Status >= (int)OrderStatus.Paid),
                    CompletedOrderCount = todayOrderList.Count(o => o.Status == (int)OrderStatus.Completed),
                    PendingOrderCount = todayOrderList.Count(o => o.Status == (int)OrderStatus.Pending),
                    TodayAlertCount = alertList.Count,
                    UnreadAlertCount = alertList.Count(a => !a.IsRead),
                    LastRefreshTime = now
                };

                _state.State.LastOverview = overview;
                _state.State.LastRefreshTime = now;
                await _state.WriteStateAsync();

                return overview;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取驾驶舱概览失败");
                return _state.State.LastOverview ?? new DashboardOverview();
            }
        }

        public async Task<List<RegionalHeatmapEntry>> GetRegionalHeatmapAsync()
        {
            try
            {
                var demandGrain = GrainFactory.GetGrain<IRegionDemandGrain>(0);
                var hotSpecies = await demandGrain.GetHotSpeciesAsync(10);

                var result = new List<RegionalHeatmapEntry>();

                for (int regionId = 1; regionId <= 5; regionId++)
                {
                    var regionGrain = GrainFactory.GetGrain<IRegionDemandGrain>(regionId);
                    var demand = await regionGrain.GetRegionalDemandAsync(0);

                    foreach (var kv in demand)
                    {
                        result.Add(new RegionalHeatmapEntry
                        {
                            RegionId = regionId,
                            SpeciesId = kv.Key,
                            DemandIndex = kv.Value
                        });
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取区域热力图数据失败");
                return new List<RegionalHeatmapEntry>();
            }
        }

        public async Task<List<SupplyDemandEntry>> GetSupplyDemandAsync()
        {
            try
            {
                var now = DateTime.Now;
                var weekAgo = now.AddDays(-7);

                var recentStats = await _statsContext.QueryAsync(
                    s => s.StatDate >= weekAgo.Date);
                var statsList = recentStats.ToList();

                var result = statsList
                    .GroupBy(s => s.SpeciesId)
                    .Select(g => new SupplyDemandEntry
                    {
                        SpeciesId = g.Key,
                        AvgPrice = g.Average(s => s.AvgPrice),
                        TotalVolume = g.Sum(s => s.TotalVolume),
                        PriceVolatility = (double)g.Average(s => s.PriceStdDev ?? 0),
                        TradeFrequency = g.Sum(s => s.TotalTradeCount)
                    })
                    .OrderByDescending(e => e.TradeFrequency)
                    .Take(20)
                    .ToList();

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取供需关系数据失败");
                return new List<SupplyDemandEntry>();
            }
        }

        public async Task<List<PriceTrendEntry>> GetPriceTrendAsync(int speciesId, int days)
        {
            try
            {
                var since = DateTime.Now.AddDays(-days).Date;
                var stats = await _statsContext.QueryAsync(
                    s => s.SpeciesId == speciesId && s.StatDate >= since);
                var statsList = stats.OrderBy(s => s.StatDate).ToList();

                return statsList.Select(s => new PriceTrendEntry
                {
                    Date = s.StatDate,
                    AvgPrice = s.AvgPrice,
                    MinPrice = s.MinPrice,
                    MaxPrice = s.MaxPrice,
                    Volume = s.TotalVolume
                }).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取价格趋势数据失败: SpeciesId={SpeciesId}", speciesId);
                return new List<PriceTrendEntry>();
            }
        }

        public async Task<string> GetAIMarketSummaryAsync()
        {
            try
            {
                var overview = await GetOverviewAsync();
                var supplyDemand = await GetSupplyDemandAsync();

                var topSpecies = supplyDemand.Take(5).ToList();
                var summary = $"今日花卉市场概况：成交额¥{overview.TotalTransactionAmount:F2}，" +
                              $"订单{overview.TotalOrderCount}笔，" +
                              $"预警{overview.TodayAlertCount}条。" +
                              $"热门品种：{string.Join("、", topSpecies.Select(s => $"品种{s.SpeciesId}(均价¥{s.AvgPrice:F2})"))}。" +
                              $"整体供需{(_state.State.LastOverview?.PendingOrderCount > 10 ? "偏紧" : "平稳")}。";

                return summary;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取AI市场摘要失败");
                return "市场摘要生成失败，请稍后重试。";
            }
        }

        public async Task<DashboardStats> GetDashboardStatsAsync()
        {
            try
            {
                var now = DateTime.Now;
                var today = now.Date;

                var todayPayments = await _paymentContext.QueryAsync(
                    p => p.CreateTime >= today && p.PaidAt != null && p.PaidAt >= today);
                var todayPaymentList = todayPayments.ToList();

                var todayOrders = await _orderContext.QueryAsync(
                    o => o.CreateTime >= today && o.CreateTime < now && o.Status >= (int)OrderStatus.Paid);
                var tradeCount = todayOrders.ToList().Count;

                var activeSpecies = await _speciesContext.QueryAsync(s => s.IsActive && !s.IsDeleted);
                var activeSpeciesCount = activeSpecies.ToList().Count;

                var onlineMerchants = await _merchantContext.QueryAsync(m => m.AuditStatus == 4 && !m.IsDeleted);
                var onlineMerchantCount = onlineMerchants.ToList().Count;

                return new DashboardStats
                {
                    TodayTradeAmount = todayPaymentList.Sum(p => p.Amount),
                    TradeCount = tradeCount,
                    ActiveSpeciesCount = activeSpeciesCount,
                    OnlineMerchantCount = onlineMerchantCount
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取仪表盘统计失败");
                return new DashboardStats();
            }
        }

        public async Task<List<RegionalTradeData>> GetRegionalTradeDataAsync()
        {
            try
            {
                var markets = await _marketContext.QueryAsync(m => m.IsActive && !m.IsDeleted);
                var marketList = markets.ToList();

                var result = new List<RegionalTradeData>();
                foreach (var market in marketList)
                {
                    var snapshots = await _snapshotContext.QueryAsync(
                        s => s.MarketId == market.Id);
                    var snapshotList = snapshots.ToList();
                    var demandIndex = snapshotList.Any()
                        ? snapshotList.Average(s => s.Volume > 0 ? Math.Min(s.Volume / 100.0 * 10 + 40, 100) : 0)
                        : 0;

                    result.Add(new RegionalTradeData
                    {
                        RegionName = market.Name ?? market.Region ?? "",
                        DemandIndex = demandIndex
                    });
                }

                return result.OrderByDescending(r => r.DemandIndex).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取区域交易数据失败");
                return new List<RegionalTradeData>();
            }
        }

        public async Task<List<SupplyDemandData>> GetSupplyDemandDataAsync()
        {
            try
            {
                var speciesList = (await _speciesContext.QueryAsync(s => s.IsActive && !s.IsDeleted)).ToList();
                var products = (await _productContext.QueryAsync(p => p.IsActive)).ToList();
                var now = DateTime.Now;
                var weekAgo = now.AddDays(-7);

                var paidOrders = await _orderContext.QueryAsync(
                    o => o.Status >= (int)OrderStatus.Paid);
                var paidOrderList = paidOrders.ToList();
                var paidOrderIds = paidOrderList.Select(o => o.Id).ToHashSet();

                var allPaidOrderItems = await _orderItemContext.QueryAsync(
                    oi => paidOrderIds.Contains(oi.OrderId));
                var paidItemList = allPaidOrderItems.ToList();

                var result = new List<SupplyDemandData>();
                foreach (var species in speciesList.Take(10))
                {
                    var supply = products.Where(p => p.SpeciesId == species.Id).Sum(p => p.Stock);

                    var demand = paidItemList.Where(oi => oi.SpeciesId == species.Id).Sum(oi => oi.Quantity);

                    var ratio = demand > 0 ? (decimal)supply / demand : supply > 0 ? 999m : 0m;

                    result.Add(new SupplyDemandData
                    {
                        SpeciesName = species.DisplayName ?? species.Name ?? "",
                        Supply = supply,
                        Demand = demand,
                        SupplyDemandRatio = ratio
                    });
                }

                return result.OrderByDescending(r => r.Demand).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取供需数据失败");
                return new List<SupplyDemandData>();
            }
        }

        public async Task<List<RecentTransaction>> GetRecentTransactionsAsync()
        {
            try
            {
                var recentOrders = await _orderContext.QueryAsync(
                    o => o.Status >= (int)OrderStatus.Paid);
                var recentOrderList = recentOrders.OrderByDescending(o => o.CreateTime).Take(10).ToList();

                if (!recentOrderList.Any()) return new List<RecentTransaction>();

                var orderIds = recentOrderList.Select(o => o.Id).ToHashSet();
                var allOrderItems = await _orderItemContext.QueryAsync(oi => orderIds.Contains(oi.OrderId));
                var orderItemList = allOrderItems.ToList();

                var markets = (await _marketContext.QueryAsync(m => m.IsActive && !m.IsDeleted)).ToList();

                var speciesIds = orderItemList.Select(oi => (long)oi.SpeciesId).Distinct().ToHashSet();
                var speciesList = (await _speciesContext.QueryAsync(s => speciesIds.Contains(s.Id))).ToList();

                var result = new List<RecentTransaction>();
                foreach (var order in recentOrderList)
                {
                    var items = orderItemList.Where(oi => oi.OrderId == order.Id).ToList();
                    foreach (var item in items)
                    {
                        var species = speciesList.FirstOrDefault(s => s.Id == item.SpeciesId);
                        var market = markets.FirstOrDefault(m => m.Id == (order.RegionId ?? 0));

                        result.Add(new RecentTransaction
                        {
                            TradeTime = order.CreateTime.ToString("HH:mm:ss"),
                            SpeciesName = species?.DisplayName ?? species?.Name ?? item.ProductName ?? "",
                            Price = item.Price,
                            Quantity = item.Quantity,
                            Market = market?.Name ?? market?.Region ?? ""
                        });
                    }
                }

                return result.Take(10).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取最近交易数据失败");
                return new List<RecentTransaction>();
            }
        }
    }

    [Serializable]
    [GenerateSerializer]
    public class DashboardState
    {
        [Id(0)]
        public DashboardOverview LastOverview { get; set; }
        [Id(1)]
        public DateTime LastRefreshTime { get; set; }
    }
}

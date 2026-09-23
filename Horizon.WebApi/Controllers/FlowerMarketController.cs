using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Horizon.Core.Options;
using Horizon.Share.VMs;
using Horizon.Orleans.Interface;
using Horizon.Game.Message.Network;
using Horizon.WebApi.Configs;
using Orleans;
using Orleans.Configuration;

namespace Horizon.WebApi.Controllers
{
    /// <summary>
    /// 平台自有花卉品种目录（真实数据源：昆明斗南花卉市场每日行情，KIFA昆明花拍中心等机构参考价）。
    /// SpeciesId 与 Gateway DounanMarketDataFetcher.SpeciesMappings 对齐（2001~2022）。
    /// Origin 为平台品种的花源产地；Grade 存放包装规格。
    /// </summary>
    public static class FlowerSpeciesCatalog
    {
        /// <summary>斗南花卉市场 Grain 主键（Gateway DounanMarketDataFetcher 推送目标）</summary>
        public const int DounanMarketId = 2;

        public sealed class SpeciesInfo
        {
            public long Id { get; init; }
            public string Name { get; init; } = "";
            public string Category { get; init; } = "";
            public string Origin { get; init; } = "";
            public string Grade { get; init; } = "";
        }

        public static readonly Dictionary<long, SpeciesInfo> Items = new()
        {
            [2001] = new SpeciesInfo { Id = 2001, Name = "白菊", Category = "菊花", Origin = "云南斗南", Grade = "10支/把" },
            [2002] = new SpeciesInfo { Id = 2002, Name = "白玫瑰", Category = "玫瑰", Origin = "云南斗南", Grade = "18支/把" },
            [2003] = new SpeciesInfo { Id = 2003, Name = "粉玫瑰", Category = "玫瑰", Origin = "云南斗南", Grade = "18支/把" },
            [2004] = new SpeciesInfo { Id = 2004, Name = "扶郎", Category = "非洲菊", Origin = "云南晋宁", Grade = "20支/把" },
            [2005] = new SpeciesInfo { Id = 2005, Name = "红豆", Category = "配花", Origin = "云南晋宁", Grade = "1把" },
            [2006] = new SpeciesInfo { Id = 2006, Name = "红玫瑰", Category = "玫瑰", Origin = "云南斗南", Grade = "18支/把" },
            [2007] = new SpeciesInfo { Id = 2007, Name = "桔梗", Category = "洋桔梗", Origin = "云南江川", Grade = "1把" },
            [2008] = new SpeciesInfo { Id = 2008, Name = "康乃馨(多头)", Category = "康乃馨", Origin = "云南姚安", Grade = "18支/把" },
            [2009] = new SpeciesInfo { Id = 2009, Name = "康乃馨(红色单头)", Category = "康乃馨", Origin = "云南姚安", Grade = "18支/把" },
            [2010] = new SpeciesInfo { Id = 2010, Name = "蓝星花", Category = "配花", Origin = "云南斗南", Grade = "10支/把" },
            [2011] = new SpeciesInfo { Id = 2011, Name = "龙竹(60公分)", Category = "绿植", Origin = "广东广州", Grade = "10支/把" },
            [2012] = new SpeciesInfo { Id = 2012, Name = "满天星(白色)", Category = "满天星", Origin = "云南斗南", Grade = "1把" },
            [2013] = new SpeciesInfo { Id = 2013, Name = "乒乓菊", Category = "菊花", Origin = "云南斗南", Grade = "10支/把" },
            [2014] = new SpeciesInfo { Id = 2014, Name = "双头白百合", Category = "百合", Origin = "云南昆明", Grade = "10支/把" },
            [2015] = new SpeciesInfo { Id = 2015, Name = "双头粉百合", Category = "百合", Origin = "云南昆明", Grade = "10支/把" },
            [2016] = new SpeciesInfo { Id = 2016, Name = "双头黄百合", Category = "百合", Origin = "云南昆明", Grade = "10支/把" },
            [2017] = new SpeciesInfo { Id = 2017, Name = "勿忘我", Category = "配花", Origin = "云南斗南", Grade = "1把" },
            [2018] = new SpeciesInfo { Id = 2018, Name = "向日葵", Category = "向日葵", Origin = "云南楚雄", Grade = "5支/把" },
            [2019] = new SpeciesInfo { Id = 2019, Name = "小雏菊", Category = "菊花", Origin = "云南斗南", Grade = "1把" },
            [2020] = new SpeciesInfo { Id = 2020, Name = "绣球花", Category = "绣球", Origin = "云南大理", Grade = "1把" },
            [2021] = new SpeciesInfo { Id = 2021, Name = "洋甘菊", Category = "配花", Origin = "云南斗南", Grade = "10支/把" },
            [2022] = new SpeciesInfo { Id = 2022, Name = "紫罗兰", Category = "紫罗兰", Origin = "云南斗南", Grade = "1把" }
        };

        public static string GetName(long speciesId)
        {
            return Items.TryGetValue(speciesId, out var info) ? info.Name : $"品种{speciesId}";
        }
    }

    [ApiGroup(ApiGroupName.Basic)]
    [ApiController]
    [Route("[controller]")]
    [Authorize]
    public class FlowerMarketController : OrleansControllerBase
    {
        private readonly ILogger<FlowerMarketController> _logger;

        public FlowerMarketController(
            IOptions<AdoNetOptions> options,
            IOptions<ClusterOptions> clusterOptions,
            ILogger<FlowerMarketController> logger,
            IClusterClient clusterClient)
            : base(options, clusterOptions, logger, clusterClient)
        {
            _logger = logger;
        }

        [AllowAnonymous]
        [HttpGet("overview")]
        public async Task<ResultVM<FlowerMarketOverviewDto>> GetOverviewAsync()
        {
            var result = new ResultVM<FlowerMarketOverviewDto>();
            try
            {
                // Grain key 必须与 Gateway DounanMarketDataFetcher 推送目标一致（DounanMarketId=2）。
                var client = await OrleansConnectClient();
                var marketGrain = client.GetGrain<IFlowerMarketGrain>(FlowerSpeciesCatalog.DounanMarketId);
                var snapshots = await marketGrain.GetMarketOverviewAsync();

                if (snapshots != null && snapshots.Count > 0)
                {
                    var now = DateTime.UtcNow;
                    var todayStart = now.Date;

                    // 并行聚合各品种：目录元数据 + 与最近历史点比较计算涨跌幅
                    var itemTasks = snapshots.Select(async s =>
                    {
                        var info = FlowerSpeciesCatalog.Items.TryGetValue(s.SpeciesId, out var meta) ? meta : null;
                        var item = new FlowerMarketSpeciesItemDto
                        {
                            SpeciesId = s.SpeciesId,
                            Name = info?.Name ?? $"品种{s.SpeciesId}",
                            Category = info?.Category ?? "其他",
                            Origin = info?.Origin ?? "未知",
                            Grade = info?.Grade ?? "A级",
                            AvgPrice = s.AvgPrice,
                            MinPrice = s.MinPrice,
                            MaxPrice = s.MaxPrice,
                            Volume = s.Volume,
                            TradeCount = s.TradeCount,
                            SnapshotTime = s.SnapshotTime,
                            ChangePercent = 0m
                        };

                        try
                        {
                            // 真实数据为周度发布：与快照时间之前最近的历史点比较涨跌幅
                            //（历史窗口 45 天覆盖回填的 30 天走势；同日历史点为快照自身写入，需排除）。
                            var speciesGrain = client.GetGrain<IFlowerSpeciesGrain>(s.SpeciesId);
                            var history = await speciesGrain.GetPriceHistoryAsync(todayStart.AddDays(-45), now);
                            var lastPoint = history
                                .Where(h => h.SnapshotTime < s.SnapshotTime)
                                .OrderByDescending(h => h.SnapshotTime)
                                .FirstOrDefault();
                            if (lastPoint != null && lastPoint.AvgPrice > 0)
                            {
                                item.ChangePercent = Math.Round((s.AvgPrice - lastPoint.AvgPrice) / lastPoint.AvgPrice * 100, 2);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "计算品种涨跌幅失败（忽略）: SpeciesId={SpeciesId}", s.SpeciesId);
                        }

                        return item;
                    });
                    var items = (await Task.WhenAll(itemTasks)).OrderBy(i => i.SpeciesId).ToList();

                    result.Data = new FlowerMarketOverviewDto
                    {
                        AvgPrice = items.Count > 0 ? items.Average(i => i.AvgPrice) : snapshots.Average(s => s.AvgPrice),
                        PriceChange = items.Count > 0 ? Math.Round(items.Average(i => i.ChangePercent), 2) : 0,
                        // 波动超过 ±5% 视为价格预警
                        AlertCount = items.Count(i => Math.Abs(i.ChangePercent) >= 5m),
                        ActiveSpeciesCount = items.Select(i => i.SpeciesId).Distinct().Count(),
                        LastUpdateTime = snapshots.Max(s => s.SnapshotTime),
                        Snapshots = snapshots,
                        Items = items
                    };
                }

                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取花卉市场概览失败");
                result.ErrorMessage = "获取花卉市场概览失败";
            }
            return result;
        }

        [AllowAnonymous]
        [HttpGet("species")]
        public async Task<ResultVM<List<FlowerPriceSnapshot>>> GetSpeciesAsync()
        {
            var result = new ResultVM<List<FlowerPriceSnapshot>>();
            try
            {
                var client = await OrleansConnectClient();
                var marketGrain = client.GetGrain<IFlowerMarketGrain>(FlowerSpeciesCatalog.DounanMarketId);
                var snapshots = await marketGrain.GetMarketOverviewAsync();

                result.Data = snapshots;
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取花卉品种列表失败");
                result.ErrorMessage = "获取花卉品种列表失败";
            }
            return result;
        }

        [AllowAnonymous]
        [HttpGet("species/{id}/price-history")]
        public async Task<ResultVM<List<FlowerPriceSnapshot>>> GetPriceHistoryAsync(int id, [FromQuery] DateTime? startTime, [FromQuery] DateTime? endTime)
        {
            var result = new ResultVM<List<FlowerPriceSnapshot>>();
            try
            {
                var client = await OrleansConnectClient();
                var speciesGrain = client.GetGrain<IFlowerSpeciesGrain>(id);
                var start = startTime ?? DateTime.UtcNow.AddDays(-30);
                var end = endTime ?? DateTime.UtcNow;
                var history = await speciesGrain.GetPriceHistoryAsync(start, end);

                result.Data = history;
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取品种价格历史失败: SpeciesId={SpeciesId}", id);
                result.ErrorMessage = "获取品种价格历史失败";
            }
            return result;
        }

        [AllowAnonymous]
        [HttpGet("species/{id}/forecast")]
        public async Task<ResultVM<FlowerPriceForecast>> GetForecastAsync(int id, [FromQuery] ForecastTimeScale timeScale = ForecastTimeScale.ShortTerm, [FromQuery] int horizonDays = 7)
        {
            var result = new ResultVM<FlowerPriceForecast>();
            try
            {
                var client = await OrleansConnectClient();
                var speciesGrain = client.GetGrain<IFlowerSpeciesGrain>(id);
                var forecast = await speciesGrain.PredictPriceAsync(timeScale, horizonDays);

                result.Data = forecast;
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取品种预测数据失败: SpeciesId={SpeciesId}", id);
                result.ErrorMessage = "获取品种预测数据失败";
            }
            return result;
        }
    }

    public class FlowerMarketOverviewDto
    {
        public decimal AvgPrice { get; set; }
        public decimal PriceChange { get; set; }
        public int AlertCount { get; set; }
        public int ActiveSpeciesCount { get; set; }
        public DateTime LastUpdateTime { get; set; }
        public List<FlowerPriceSnapshot> Snapshots { get; set; } = new();
        /// <summary>品种条目（含目录元数据与涨跌幅，供市场页商品网格渲染）</summary>
        public List<FlowerMarketSpeciesItemDto> Items { get; set; } = new();
    }

    public class FlowerMarketSpeciesItemDto
    {
        public long SpeciesId { get; set; }
        public string Name { get; set; } = "";
        public string Category { get; set; } = "";
        public string Origin { get; set; } = "";
        public string Grade { get; set; } = "";
        public decimal AvgPrice { get; set; }
        public decimal MinPrice { get; set; }
        public decimal MaxPrice { get; set; }
        public int Volume { get; set; }
        public int TradeCount { get; set; }
        public DateTime SnapshotTime { get; set; }
        /// <summary>相对上一历史点的涨跌幅（%，正=涨）</summary>
        public decimal ChangePercent { get; set; }
    }
}

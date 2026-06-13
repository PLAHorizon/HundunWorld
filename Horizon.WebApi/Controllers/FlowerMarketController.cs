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

        [HttpGet("overview")]
        public async Task<ResultVM<FlowerMarketOverviewDto>> GetOverviewAsync()
        {
            var result = new ResultVM<FlowerMarketOverviewDto>();
            try
            {
                var client = await OrleansConnectClient();
                var marketGrain = client.GetGrain<IFlowerMarketGrain>(0);
                var snapshots = await marketGrain.GetMarketOverviewAsync();

                if (snapshots != null && snapshots.Count > 0)
                {
                    var avgPrice = snapshots.Average(s => s.AvgPrice);
                    var totalVolume = snapshots.Sum(s => s.Volume);
                    var activeSpeciesCount = snapshots.Select(s => s.SpeciesId).Distinct().Count();

                    result.Data = new FlowerMarketOverviewDto
                    {
                        AvgPrice = avgPrice,
                        PriceChange = 0,
                        AlertCount = 0,
                        ActiveSpeciesCount = activeSpeciesCount,
                        LastUpdateTime = snapshots.Max(s => s.SnapshotTime),
                        Snapshots = snapshots
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

        [HttpGet("species")]
        public async Task<ResultVM<List<FlowerPriceSnapshot>>> GetSpeciesAsync()
        {
            var result = new ResultVM<List<FlowerPriceSnapshot>>();
            try
            {
                var client = await OrleansConnectClient();
                var marketGrain = client.GetGrain<IFlowerMarketGrain>(0);
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
    }
}

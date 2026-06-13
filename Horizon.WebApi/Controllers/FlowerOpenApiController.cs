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
    [ApiGroup(ApiGroupName.FlowerOpen)]
    [ApiController]
    [Route("api/open/[controller]")]
    [Authorize]
    public class FlowerOpenApiController : ControllerBase
    {
        private readonly ILogger<FlowerOpenApiController> _logger;
        private readonly IClusterClient _clusterClient;

        public FlowerOpenApiController(
            ILogger<FlowerOpenApiController> logger,
            IClusterClient clusterClient)
        {
            _logger = logger;
            _clusterClient = clusterClient;
        }

        [HttpGet("market/overview")]
        public async Task<ResultVM<FlowerMarketOverviewDto>> GetMarketOverviewAsync()
        {
            var result = new ResultVM<FlowerMarketOverviewDto>();
            try
            {
                var marketGrain = _clusterClient.GetGrain<IFlowerMarketGrain>(0);
                var snapshots = await marketGrain.GetMarketOverviewAsync();

                if (snapshots != null && snapshots.Count > 0)
                {
                    result.Data = new FlowerMarketOverviewDto
                    {
                        AvgPrice = snapshots.Average(s => s.AvgPrice),
                        PriceChange = 0,
                        ActiveSpeciesCount = snapshots.Select(s => s.SpeciesId).Distinct().Count(),
                        LastUpdateTime = snapshots.Max(s => s.SnapshotTime),
                        Snapshots = snapshots
                    };
                }
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "开放API: 获取市场概览失败");
                result.ErrorMessage = "获取市场概览失败";
            }
            return result;
        }

        [HttpGet("market/species/{speciesId}/price")]
        public async Task<ResultVM<FlowerPriceSnapshot>> GetSpeciesPriceAsync(int speciesId)
        {
            var result = new ResultVM<FlowerPriceSnapshot>();
            try
            {
                var marketGrain = _clusterClient.GetGrain<IFlowerMarketGrain>(0);
                var snapshot = await marketGrain.GetLatestSnapshotAsync(speciesId);

                result.Data = snapshot;
                result.IsSuccess = snapshot != null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "开放API: 获取品种价格失败: SpeciesId={SpeciesId}", speciesId);
                result.ErrorMessage = "获取品种价格失败";
            }
            return result;
        }

        [HttpGet("market/species/{speciesId}/forecast")]
        public async Task<ResultVM<FlowerPriceForecast>> GetSpeciesForecastAsync(
            int speciesId,
            [FromQuery] ForecastTimeScale timeScale = ForecastTimeScale.ShortTerm,
            [FromQuery] int horizonDays = 7)
        {
            var result = new ResultVM<FlowerPriceForecast>();
            try
            {
                var speciesGrain = _clusterClient.GetGrain<IFlowerSpeciesGrain>(speciesId);
                var forecast = await speciesGrain.PredictPriceAsync(timeScale, horizonDays);

                result.Data = forecast;
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "开放API: 获取品种预测失败: SpeciesId={SpeciesId}", speciesId);
                result.ErrorMessage = "获取品种预测失败";
            }
            return result;
        }

        [HttpGet("market/hot-species")]
        public async Task<ResultVM<List<int>>> GetHotSpeciesAsync([FromQuery] int topN = 10)
        {
            var result = new ResultVM<List<int>>();
            try
            {
                var demandGrain = _clusterClient.GetGrain<IRegionDemandGrain>(0);
                var hotSpecies = await demandGrain.GetHotSpeciesAsync(topN);

                result.Data = hotSpecies;
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "开放API: 获取热门品种失败");
                result.ErrorMessage = "获取热门品种失败";
            }
            return result;
        }

        [HttpGet("dashboard/summary")]
        public async Task<ResultVM<string>> GetMarketSummaryAsync()
        {
            var result = new ResultVM<string>();
            try
            {
                var dashboardGrain = _clusterClient.GetGrain<IDashboardGrain>(0);
                var summary = await dashboardGrain.GetAIMarketSummaryAsync();

                result.Data = summary;
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "开放API: 获取市场摘要失败");
                result.ErrorMessage = "获取市场摘要失败";
            }
            return result;
        }
    }
}

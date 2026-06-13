using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Horizon.Core.Options;
using Horizon.Share.VMs;
using Horizon.Orleans.Interface;
using Horizon.WebApi.Configs;
using Orleans;
using Orleans.Configuration;

namespace Horizon.WebApi.Controllers
{
    [ApiGroup(ApiGroupName.Flower)]
    [ApiController]
    [Route("[controller]")]
    [Authorize]
    public class FlowerDashboardController : OrleansControllerBase
    {
        private readonly ILogger<FlowerDashboardController> _logger;

        public FlowerDashboardController(
            IOptions<AdoNetOptions> options,
            IOptions<ClusterOptions> clusterOptions,
            ILogger<FlowerDashboardController> logger,
            IClusterClient clusterClient)
            : base(options, clusterOptions, logger, clusterClient)
        {
            _logger = logger;
        }

        [HttpGet("overview")]
        public async Task<ResultVM<DashboardOverview>> GetOverviewAsync()
        {
            var result = new ResultVM<DashboardOverview>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IDashboardGrain>(0);
                result.Data = await grain.GetOverviewAsync();
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取仪表盘概览失败");
                result.ErrorMessage = "获取仪表盘概览失败";
            }
            return result;
        }

        [HttpGet("regional-heatmap")]
        public async Task<ResultVM<List<RegionalHeatmapEntry>>> GetRegionalHeatmapAsync()
        {
            var result = new ResultVM<List<RegionalHeatmapEntry>>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IDashboardGrain>(0);
                result.Data = await grain.GetRegionalHeatmapAsync();
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取区域热力图失败");
                result.ErrorMessage = "获取区域热力图失败";
            }
            return result;
        }

        [HttpGet("supply-demand-stats")]
        public async Task<ResultVM<List<SupplyDemandEntry>>> GetSupplyDemandAsync()
        {
            var result = new ResultVM<List<SupplyDemandEntry>>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IDashboardGrain>(0);
                result.Data = await grain.GetSupplyDemandAsync();
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取供需关系失败");
                result.ErrorMessage = "获取供需关系失败";
            }
            return result;
        }

        [HttpGet("price-trend/{speciesId}")]
        public async Task<ResultVM<List<PriceTrendEntry>>> GetPriceTrendAsync(int speciesId, [FromQuery] int days = 30)
        {
            var result = new ResultVM<List<PriceTrendEntry>>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IDashboardGrain>(0);
                result.Data = await grain.GetPriceTrendAsync(speciesId, days);
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取价格趋势失败: SpeciesId={SpeciesId}", speciesId);
                result.ErrorMessage = "获取价格趋势失败";
            }
            return result;
        }

        [HttpGet("ai-summary")]
        public async Task<ResultVM<string>> GetAIMarketSummaryAsync()
        {
            var result = new ResultVM<string>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IDashboardGrain>(0);
                result.Data = await grain.GetAIMarketSummaryAsync();
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取AI市场摘要失败");
                result.ErrorMessage = "获取AI市场摘要失败";
            }
            return result;
        }

        [HttpGet("stats")]
        public async Task<ResultVM<DashboardStats>> GetStatsAsync()
        {
            var result = new ResultVM<DashboardStats>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IDashboardGrain>(0);
                result.Data = await grain.GetDashboardStatsAsync();
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取仪表盘统计失败");
                result.ErrorMessage = "获取仪表盘统计失败";
            }
            return result;
        }

        [HttpGet("regional")]
        public async Task<ResultVM<List<RegionalTradeData>>> GetRegionalAsync()
        {
            var result = new ResultVM<List<RegionalTradeData>>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IDashboardGrain>(0);
                result.Data = await grain.GetRegionalTradeDataAsync();
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取区域交易数据失败");
                result.ErrorMessage = "获取区域交易数据失败";
            }
            return result;
        }

        [HttpGet("supply-demand")]
        public async Task<ResultVM<List<SupplyDemandData>>> GetSupplyDemandDataAsync()
        {
            var result = new ResultVM<List<SupplyDemandData>>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IDashboardGrain>(0);
                result.Data = await grain.GetSupplyDemandDataAsync();
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取供需数据失败");
                result.ErrorMessage = "获取供需数据失败";
            }
            return result;
        }

        [HttpGet("transactions")]
        public async Task<ResultVM<List<RecentTransaction>>> GetTransactionsAsync()
        {
            var result = new ResultVM<List<RecentTransaction>>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IDashboardGrain>(0);
                result.Data = await grain.GetRecentTransactionsAsync();
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取最近交易数据失败");
                result.ErrorMessage = "获取最近交易数据失败";
            }
            return result;
        }
    }
}

using System;
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
    [ApiGroup(ApiGroupName.Flower)]
    [ApiController]
    [Route("[controller]")]
    [Authorize]
    public class FlowerReportController : OrleansControllerBase
    {
        private readonly ILogger<FlowerReportController> _logger;

        public FlowerReportController(
            IOptions<AdoNetOptions> options,
            IOptions<ClusterOptions> clusterOptions,
            ILogger<FlowerReportController> logger,
            IClusterClient clusterClient)
            : base(options, clusterOptions, logger, clusterClient)
        {
            _logger = logger;
        }

        [HttpGet("daily")]
        public async Task<ResultVM<DashboardOverview>> GetDailyReportAsync([FromQuery] DateTime? date)
        {
            var result = new ResultVM<DashboardOverview>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IDashboardGrain>(0);
                result.Data = await grain.GetOverviewAsync();
                result.IsSuccess = result.Data != null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取日报失败: Date={Date}", date);
                result.ErrorMessage = "获取日报失败";
            }
            return result;
        }

        [HttpPost("generate")]
        public async Task<ResultVM<bool>> GenerateDailyReportAsync([FromBody] GenerateReportRequest request)
        {
            var result = new ResultVM<bool>();
            try
            {
                var client = await OrleansConnectClient();
                var schedulerGrain = client.GetGrain<IForecastSchedulerGrain>(0);
                await schedulerGrain.TriggerDailyForecastAsync();
                await schedulerGrain.TriggerHourlyAggregationAsync();
                result.Data = true;
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "生成日报失败");
                result.ErrorMessage = "生成日报失败";
            }
            return result;
        }
    }

    public class GenerateReportRequest
    {
        public DateTime? ReportDate { get; set; }
    }
}

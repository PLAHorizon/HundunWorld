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
    public class FlowerForecastController : OrleansControllerBase
    {
        private readonly ILogger<FlowerForecastController> _logger;

        public FlowerForecastController(
            IOptions<AdoNetOptions> options,
            IOptions<ClusterOptions> clusterOptions,
            ILogger<FlowerForecastController> logger,
            IClusterClient clusterClient)
            : base(options, clusterOptions, logger, clusterClient)
        {
            _logger = logger;
        }

        [HttpGet("{speciesId}")]
        public async Task<ResultVM<FlowerPriceForecast>> PredictPriceAsync(int speciesId, [FromQuery] ForecastTimeScale timeScale = ForecastTimeScale.ShortTerm, [FromQuery] int horizonDays = 7)
        {
            var result = new ResultVM<FlowerPriceForecast>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IFlowerSpeciesGrain>(speciesId);
                result.Data = await grain.PredictPriceAsync(timeScale, horizonDays);
                result.IsSuccess = result.Data != null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "预测价格失败: SpeciesId={SpeciesId}", speciesId);
                result.ErrorMessage = "预测价格失败";
            }
            return result;
        }

        [HttpPost("trigger")]
        public async Task<ResultVM<bool>> TriggerDailyForecastAsync()
        {
            var result = new ResultVM<bool>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IForecastSchedulerGrain>(0);
                await grain.TriggerDailyForecastAsync();
                result.Data = true;
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "触发每日预测失败");
                result.ErrorMessage = "触发每日预测失败";
            }
            return result;
        }
    }
}

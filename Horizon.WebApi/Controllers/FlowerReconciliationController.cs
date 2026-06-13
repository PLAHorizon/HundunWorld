using System;
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
    public class FlowerReconciliationController : OrleansControllerBase
    {
        private readonly ILogger<FlowerReconciliationController> _logger;

        public FlowerReconciliationController(
            IOptions<AdoNetOptions> options,
            IOptions<ClusterOptions> clusterOptions,
            ILogger<FlowerReconciliationController> logger,
            IClusterClient clusterClient)
            : base(options, clusterOptions, logger, clusterClient)
        {
            _logger = logger;
        }

        [HttpPost("run")]
        public async Task<ResultVM<ReconciliationResult>> RunReconciliationAsync()
        {
            var result = new ResultVM<ReconciliationResult>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IReconciliationGrain>(0);
                result.Data = await grain.RunReconciliationAsync();
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "执行对账失败");
                result.ErrorMessage = "执行对账失败";
            }
            return result;
        }

        [HttpGet("last-run-time")]
        public async Task<ResultVM<DateTime>> GetLastRunTimeAsync()
        {
            var result = new ResultVM<DateTime>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IReconciliationGrain>(0);
                result.Data = await grain.GetLastRunTimeAsync();
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取对账时间失败");
                result.ErrorMessage = "获取对账时间失败";
            }
            return result;
        }

        [HttpGet("last-inconsistency-count")]
        public async Task<ResultVM<int>> GetLastInconsistencyCountAsync()
        {
            var result = new ResultVM<int>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IReconciliationGrain>(0);
                result.Data = await grain.GetLastInconsistencyCountAsync();
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取不一致数量失败");
                result.ErrorMessage = "获取不一致数量失败";
            }
            return result;
        }
    }
}

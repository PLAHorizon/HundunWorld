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
    public class FlowerSettledConfigController : OrleansControllerBase
    {
        private readonly ILogger<FlowerSettledConfigController> _logger;

        public FlowerSettledConfigController(
            IOptions<AdoNetOptions> options,
            IOptions<ClusterOptions> clusterOptions,
            ILogger<FlowerSettledConfigController> logger,
            IClusterClient clusterClient)
            : base(options, clusterOptions, logger, clusterClient)
        {
            _logger = logger;
        }

        [HttpGet]
        public async Task<ResultVM<SettledConfigState>> GetSettledConfigAsync()
        {
            var result = new ResultVM<SettledConfigState>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<ISettledConfigGrain>(1);
                result.Data = await grain.GetSettledConfigAsync();
                result.IsSuccess = result.Data != null;
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "获取入驻配置失败");
                result.ErrorMessage = "获取入驻配置失败";
            }
            return result;
        }

        [HttpPut]
        public async Task<ResultVM<SettledConfigState>> UpdateSettledConfigAsync([FromBody] SettledConfigState config)
        {
            var result = new ResultVM<SettledConfigState>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<ISettledConfigGrain>(1);
                result.Data = await grain.UpdateSettledConfigAsync(config);
                result.IsSuccess = result.Data != null;
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "更新入驻配置失败");
                result.ErrorMessage = "更新入驻配置失败";
            }
            return result;
        }
    }
}

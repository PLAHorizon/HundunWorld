using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Horizon.Share.VMs;
using Horizon.Orleans.Interface;
using Horizon.WebApi.Configs;
using Orleans;

namespace Horizon.WebApi.Controllers
{
    [ApiGroup(ApiGroupName.Flower)]
    [ApiController]
    [Route("[controller]")]
    [Authorize]
    public class FlowerApiKeyController : ControllerBase
    {
        private readonly ILogger<FlowerApiKeyController> _logger;
        private readonly IClusterClient _clusterClient;

        public FlowerApiKeyController(
            ILogger<FlowerApiKeyController> logger,
            IClusterClient clusterClient)
        {
            _logger = logger;
            _clusterClient = clusterClient;
        }

        [HttpGet("list")]
        public async Task<ResultVM<object>> ListApiKeysAsync([FromQuery] long passportId)
        {
            var result = new ResultVM<object>();
            try
            {
                var grain = _clusterClient.GetGrain<IApiKeyManagementGrain>(passportId);
                var keys = await grain.ListApiKeysAsync(passportId);
                result.Data = keys;
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取API Key列表失败");
                result.ErrorMessage = "获取API Key列表失败";
            }
            return result;
        }

        [HttpPost("create")]
        public async Task<ResultVM<ApiKeyInfo>> CreateApiKeyAsync([FromBody] CreateApiKeyRequest request)
        {
            var result = new ResultVM<ApiKeyInfo>();
            try
            {
                var grain = _clusterClient.GetGrain<IApiKeyManagementGrain>(request.PassportId);
                var keyInfo = await grain.CreateApiKeyAsync(request.PassportId, request.Name, request.Plan ?? "lite");
                result.Data = keyInfo;
                result.IsSuccess = keyInfo != null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建API Key失败");
                result.ErrorMessage = "创建API Key失败";
            }
            return result;
        }

        [HttpPost("{keyId}/revoke")]
        public async Task<ResultVM<bool>> RevokeApiKeyAsync(long keyId, [FromQuery] long passportId)
        {
            var result = new ResultVM<bool>();
            try
            {
                var grain = _clusterClient.GetGrain<IApiKeyManagementGrain>(passportId);
                result.Data = await grain.RevokeApiKeyAsync(keyId, passportId);
                result.IsSuccess = result.Data;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "撤销API Key失败: KeyId={KeyId}", keyId);
                result.ErrorMessage = "撤销API Key失败";
            }
            return result;
        }
    }

    public class CreateApiKeyRequest
    {
        public long PassportId { get; set; }
        public string Name { get; set; }
        public string Plan { get; set; }
    }
}

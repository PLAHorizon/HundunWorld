using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Horizon.Strategy.Storage.Redis;
using Horizon.WebApi.Configs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Horizon.WebApi.Controllers
{
    /// <summary>
    /// 供客户端查询当前在线网关（Game / IM）列表。
    /// 数据由各网关启动时写入 Redis，本接口按需读取并返回。
    /// </summary>
    [ApiGroup(ApiGroupName.Basic)]
    [ApiController]
    [Route("[controller]")]
    public class GatewayController : ControllerBase
    {
        private readonly ILogger<GatewayController> _logger;
        private readonly GatewayRegistry _gatewayRegistry;

        public GatewayController(
            ILogger<GatewayController> logger,
            GatewayRegistry gatewayRegistry)
        {
            _logger = logger;
            _gatewayRegistry = gatewayRegistry;
        }

        /// <summary>
        /// 返回所有在线网关（Game + IM）。
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            try
            {
                var all = await _gatewayRegistry.GetAllAsync().ConfigureAwait(false);
                return Ok(new GatewayListResponse
                {
                    Game = ProjectByType(all, GatewayType.Game),
                    Im = ProjectByType(all, GatewayType.IM)
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "读取网关列表失败");
                return StatusCode(500, new { message = "读取网关列表失败" });
            }
        }

        /// <summary>
        /// 返回 Game 网关列表。
        /// </summary>
        [HttpGet("game")]
        public async Task<IActionResult> GetGameGateways()
        {
            return await GetByTypeAsync(GatewayType.Game).ConfigureAwait(false);
        }

        /// <summary>
        /// 返回 IM 网关列表。
        /// </summary>
        [HttpGet("im")]
        public async Task<IActionResult> GetImGateways()
        {
            return await GetByTypeAsync(GatewayType.IM).ConfigureAwait(false);
        }

        private async Task<IActionResult> GetByTypeAsync(GatewayType type)
        {
            try
            {
                var list = await _gatewayRegistry.GetByTypeAsync(type).ConfigureAwait(false);
                return Ok(list.Select(ToDto).ToList());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "读取网关列表失败 (Type={Type})", type);
                return StatusCode(500, new { message = "读取网关列表失败" });
            }
        }

        private static List<GatewayEndpointDto> ProjectByType(IEnumerable<GatewayRegistration> all, GatewayType type)
        {
            return all.Where(r => r.GatewayType == type).Select(ToDto).ToList();
        }

        private static GatewayEndpointDto ToDto(GatewayRegistration registration) => new()
        {
            InstanceId = registration.InstanceId,
            Type = registration.GatewayType.ToString(),
            ClusterId = registration.ClusterId,
            Address = registration.Address,
            Port = registration.Port,
            Region = registration.Region,
            LastHeartbeatUtc = registration.LastHeartbeatUtc
        };

        /// <summary>网关端点 DTO。</summary>
        public class GatewayEndpointDto
        {
            public string InstanceId { get; set; } = string.Empty;
            public string Type { get; set; } = string.Empty;
            public string ClusterId { get; set; } = string.Empty;
            public string Address { get; set; } = string.Empty;
            public int Port { get; set; }
            public string Region { get; set; } = string.Empty;
            public DateTime LastHeartbeatUtc { get; set; }
        }

        /// <summary>汇总响应。</summary>
        public class GatewayListResponse
        {
            public List<GatewayEndpointDto> Game { get; set; } = new();
            public List<GatewayEndpointDto> Im { get; set; } = new();
        }
    }
}

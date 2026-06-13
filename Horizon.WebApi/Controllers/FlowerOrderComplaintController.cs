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
    public class FlowerOrderComplaintController : OrleansControllerBase
    {
        private readonly ILogger<FlowerOrderComplaintController> _logger;

        public FlowerOrderComplaintController(
            IOptions<AdoNetOptions> options,
            IOptions<ClusterOptions> clusterOptions,
            ILogger<FlowerOrderComplaintController> logger,
            IClusterClient clusterClient)
            : base(options, clusterOptions, logger, clusterClient)
        {
            _logger = logger;
        }

        [HttpPost]
        public async Task<ResultVM<OrderComplaintState>> SubmitComplaintAsync([FromBody] OrderComplaintState complaint)
        {
            var result = new ResultVM<OrderComplaintState>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IOrderComplaintGrain>(0);
                result.Data = await grain.SubmitComplaintAsync(complaint);
                result.IsSuccess = result.Data != null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "提交投诉失败");
                result.ErrorMessage = "提交投诉失败";
            }
            return result;
        }

        [HttpGet("{complaintId}")]
        public async Task<ResultVM<OrderComplaintState>> GetComplaintAsync(long complaintId)
        {
            var result = new ResultVM<OrderComplaintState>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IOrderComplaintGrain>(0);
                result.Data = await grain.GetComplaintAsync(complaintId);
                result.IsSuccess = result.Data != null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取投诉失败: ComplaintId={ComplaintId}", complaintId);
                result.ErrorMessage = "获取投诉失败";
            }
            return result;
        }

        [HttpGet("order/{orderId}")]
        public async Task<ResultVM<OrderComplaintState>> GetOrderComplaintAsync(long orderId)
        {
            var result = new ResultVM<OrderComplaintState>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IOrderComplaintGrain>(0);
                result.Data = await grain.GetOrderComplaintAsync(orderId);
                result.IsSuccess = result.Data != null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取订单投诉失败: OrderId={OrderId}", orderId);
                result.ErrorMessage = "获取订单投诉失败";
            }
            return result;
        }

        [HttpPost("{complaintId}/handle")]
        public async Task<ResultVM<OrderComplaintState>> HandleComplaintAsync(long complaintId, [FromBody] HandleComplaintRequest request)
        {
            var result = new ResultVM<OrderComplaintState>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IOrderComplaintGrain>(0);
                result.Data = await grain.HandleComplaintAsync(complaintId, request.ReplyContent ?? "");
                result.IsSuccess = result.Data != null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理投诉失败: ComplaintId={ComplaintId}", complaintId);
                result.ErrorMessage = "处理投诉失败";
            }
            return result;
        }

        [HttpGet("shop/{shopId}")]
        public async Task<ResultVM<List<OrderComplaintState>>> GetShopComplaintsAsync(long shopId)
        {
            var result = new ResultVM<List<OrderComplaintState>>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IOrderComplaintGrain>(0);
                result.Data = await grain.GetShopComplaintsAsync(shopId);
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取店铺投诉失败: ShopId={ShopId}", shopId);
                result.ErrorMessage = "获取投诉列表失败";
            }
            return result;
        }

        [HttpGet("user/{userId}")]
        public async Task<ResultVM<List<OrderComplaintState>>> GetUserComplaintsAsync(Guid userId)
        {
            var result = new ResultVM<List<OrderComplaintState>>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IOrderComplaintGrain>(0);
                result.Data = await grain.GetUserComplaintsAsync(userId);
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取用户投诉失败: UserId={UserId}", userId);
                result.ErrorMessage = "获取投诉列表失败";
            }
            return result;
        }
    }

    public class HandleComplaintRequest
    {
        public string ReplyContent { get; set; } = "";
    }
}

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
    public class FlowerTradeCommentController : OrleansControllerBase
    {
        private readonly ILogger<FlowerTradeCommentController> _logger;

        public FlowerTradeCommentController(
            IOptions<AdoNetOptions> options,
            IOptions<ClusterOptions> clusterOptions,
            ILogger<FlowerTradeCommentController> logger,
            IClusterClient clusterClient)
            : base(options, clusterOptions, logger, clusterClient)
        {
            _logger = logger;
        }

        [HttpPost]
        public async Task<ResultVM<TradeCommentState>> SubmitTradeCommentAsync([FromBody] TradeCommentState comment)
        {
            var result = new ResultVM<TradeCommentState>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<ITradeCommentGrain>(0);
                result.Data = await grain.SubmitTradeCommentAsync(comment);
                result.IsSuccess = result.Data != null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "提交交易评价失败");
                result.ErrorMessage = "提交交易评价失败";
            }
            return result;
        }

        [HttpGet("order/{orderId}")]
        public async Task<ResultVM<TradeCommentState>> GetOrderTradeCommentAsync(long orderId)
        {
            var result = new ResultVM<TradeCommentState>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<ITradeCommentGrain>(0);
                result.Data = await grain.GetOrderTradeCommentAsync(orderId);
                result.IsSuccess = result.Data != null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取订单评价失败: OrderId={OrderId}", orderId);
                result.ErrorMessage = "获取订单评价失败";
            }
            return result;
        }

        [HttpGet("shop/{shopId}")]
        public async Task<ResultVM<List<TradeCommentState>>> GetShopTradeCommentsAsync(long shopId)
        {
            var result = new ResultVM<List<TradeCommentState>>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<ITradeCommentGrain>(0);
                result.Data = await grain.GetShopTradeCommentsAsync(shopId);
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取店铺评价失败: ShopId={ShopId}", shopId);
                result.ErrorMessage = "获取店铺评价失败";
            }
            return result;
        }

        [HttpGet("shop/{shopId}/average")]
        public async Task<ResultVM<TradeCommentState>> GetShopAverageScoreAsync(long shopId)
        {
            var result = new ResultVM<TradeCommentState>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<ITradeCommentGrain>(0);
                result.Data = await grain.GetShopAverageScoreAsync(shopId);
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取店铺评分失败: ShopId={ShopId}", shopId);
                result.ErrorMessage = "获取店铺评分失败";
            }
            return result;
        }
    }
}

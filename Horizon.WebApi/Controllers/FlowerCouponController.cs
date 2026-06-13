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
    public class FlowerCouponController : OrleansControllerBase
    {
        private readonly ILogger<FlowerCouponController> _logger;

        public FlowerCouponController(
            IOptions<AdoNetOptions> options,
            IOptions<ClusterOptions> clusterOptions,
            ILogger<FlowerCouponController> logger,
            IClusterClient clusterClient)
            : base(options, clusterOptions, logger, clusterClient)
        {
            _logger = logger;
        }

        [HttpPost]
        public async Task<ResultVM<CouponState>> CreateCouponAsync([FromBody] CouponState coupon)
        {
            var result = new ResultVM<CouponState>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<ICouponGrain>(0);
                result.Data = await grain.CreateCouponAsync(coupon);
                result.IsSuccess = result.Data != null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建优惠券失败");
                result.ErrorMessage = "创建优惠券失败";
            }
            return result;
        }

        [HttpGet("{couponId}")]
        public async Task<ResultVM<CouponState>> GetCouponAsync(long couponId)
        {
            var result = new ResultVM<CouponState>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<ICouponGrain>(0);
                result.Data = await grain.GetCouponAsync(couponId);
                result.IsSuccess = result.Data != null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取优惠券失败: CouponId={CouponId}", couponId);
                result.ErrorMessage = "获取优惠券失败";
            }
            return result;
        }

        [HttpGet("shop/{shopId}")]
        public async Task<ResultVM<List<CouponState>>> GetShopCouponsAsync(long shopId)
        {
            var result = new ResultVM<List<CouponState>>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<ICouponGrain>(0);
                result.Data = await grain.GetShopCouponsAsync(shopId);
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取店铺优惠券失败: ShopId={ShopId}", shopId);
                result.ErrorMessage = "获取优惠券列表失败";
            }
            return result;
        }

        [HttpPost("{couponId}/receive")]
        public async Task<ResultVM<CouponRecordState>> ReceiveCouponAsync(long couponId, [FromQuery] Guid userId)
        {
            var result = new ResultVM<CouponRecordState>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<ICouponGrain>(0);
                result.Data = await grain.ReceiveCouponAsync(couponId, userId);
                result.IsSuccess = result.Data != null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "领取优惠券失败: CouponId={CouponId}", couponId);
                result.ErrorMessage = "领取优惠券失败";
            }
            return result;
        }

        [HttpPost("use/{recordId}")]
        public async Task<ResultVM<bool>> UseCouponAsync(long recordId, [FromQuery] long orderId)
        {
            var result = new ResultVM<bool>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<ICouponGrain>(0);
                result.Data = await grain.UseCouponAsync(recordId, orderId);
                result.IsSuccess = result.Data;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "使用优惠券失败: RecordId={RecordId}", recordId);
                result.ErrorMessage = "使用优惠券失败";
            }
            return result;
        }

        [HttpGet("user/{userId}")]
        public async Task<ResultVM<List<CouponRecordState>>> GetUserCouponsAsync(Guid userId)
        {
            var result = new ResultVM<List<CouponRecordState>>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<ICouponGrain>(0);
                result.Data = await grain.GetUserCouponsAsync(userId);
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取用户优惠券失败: UserId={UserId}", userId);
                result.ErrorMessage = "获取用户优惠券失败";
            }
            return result;
        }
    }
}

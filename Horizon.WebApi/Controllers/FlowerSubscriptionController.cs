using Horizon.Core.Abstract;
using Horizon.Core.Options;
using Horizon.Game.Message.Network;
using Horizon.Orleans.Interface;
using Horizon.Share.VMs;
using Horizon.WebApi.Configs;
using IdentityServer4.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans;
using Orleans.Configuration;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Horizon.WebApi.Controllers
{
    [ApiGroup(ApiGroupName.Basic)]
    [ApiController]
    [Route("[controller]")]
    [Authorize]
    public class FlowerSubscriptionController : OrleansControllerBase
    {
        private readonly ILogger<FlowerSubscriptionController> _logger;
        private readonly IPassportCurrentUser _passportCurrent;

        public FlowerSubscriptionController(IOptions<AdoNetOptions> options,
                                IOptions<ClusterOptions> clusterOptions,
                                ILogger<FlowerSubscriptionController> logger,
                                IClusterClient clusterClient,
                                IPassportCurrentUser passportCurrent)
                                : base(options, clusterOptions, logger, clusterClient)
        {
            _logger = logger;
            _passportCurrent = passportCurrent;
        }

        [HttpGet("subscriptions")]
        public async Task<ResultVM<List<FlowerSubscriptionInfo>>> GetSubscriptions([FromQuery] string? passportId)
        {
            var result = new ResultVM<List<FlowerSubscriptionInfo>>();
            try
            {
                var effectivePassportId = _passportCurrent?.PassportId ?? passportId;
                if (string.IsNullOrEmpty(effectivePassportId))
                {
                    result.ErrorMessage = "未获取到用户通行证，请先登录";
                    return result;
                }

                var userId = await ResolveUserGuid(effectivePassportId);
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IFlowerSubscriptionGrain>(userId);
                result.Data = await grain.GetSubscriptionsAsync();
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取用户订阅列表失败");
                result.ErrorMessage = "获取订阅列表失败";
            }
            return result;
        }

        [HttpPost("subscriptions")]
        public async Task<ResultVM<FlowerSubscriptionInfo>> CreateSubscription([FromBody] FlowerSubscriptionInfo subscription, [FromQuery] string? passportId)
        {
            var result = new ResultVM<FlowerSubscriptionInfo>();
            try
            {
                var effectivePassportId = _passportCurrent?.PassportId ?? passportId;
                if (string.IsNullOrEmpty(effectivePassportId))
                {
                    result.ErrorMessage = "未获取到用户通行证，请先登录";
                    return result;
                }

                var userId = await ResolveUserGuid(effectivePassportId);
                subscription.UserId = userId;
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IFlowerSubscriptionGrain>(userId);
                result.Data = await grain.CreateSubscriptionAsync(subscription);
                result.IsSuccess = result.Data != null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建订阅失败");
                result.ErrorMessage = "创建订阅失败";
            }
            return result;
        }

        [HttpDelete("subscriptions/{id}")]
        public async Task<ResultVM<bool>> CancelSubscription([FromQuery] long id, [FromQuery] string? passportId)
        {
            var result = new ResultVM<bool>();
            try
            {
                var effectivePassportId = _passportCurrent?.PassportId ?? passportId;
                if (string.IsNullOrEmpty(effectivePassportId))
                {
                    result.ErrorMessage = "未获取到用户通行证，请先登录";
                    return result;
                }

                var userId = await ResolveUserGuid(effectivePassportId);
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IFlowerSubscriptionGrain>(userId);
                result.Data = await grain.CancelSubscriptionAsync(id);
                result.IsSuccess = result.Data;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "取消订阅失败");
                result.ErrorMessage = "取消订阅失败";
            }
            return result;
        }

        [HttpGet("my")]
        public async Task<ResultVM<FlowerSubscriptionInfo>> GetMySubscription([FromQuery] string? passportId)
        {
            var result = new ResultVM<FlowerSubscriptionInfo>();
            try
            {
                var effectivePassportId = _passportCurrent?.PassportId ?? passportId;
                if (string.IsNullOrEmpty(effectivePassportId))
                {
                    result.ErrorMessage = "未获取到用户通行证，请先登录";
                    return result;
                }

                var userId =await ResolveUserGuid(effectivePassportId);
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IFlowerSubscriptionGrain>(userId);
                result.Data = await grain.GetActiveSubscriptionAsync();
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取当前用户订阅失败");
                result.ErrorMessage = "获取订阅信息失败";
            }
            return result;
        }

        [HttpPost("upgrade")]
        public async Task<ResultVM<FlowerSubscriptionInfo>> UpgradeSubscription([FromBody] UpgradeSubscriptionRequest request)
        {
            var result = new ResultVM<FlowerSubscriptionInfo>();
            try
            {
                var effectivePassportId = _passportCurrent?.PassportId ?? request.PassportId;
                if (string.IsNullOrEmpty(effectivePassportId))
                {
                    result.ErrorMessage = "未获取到用户通行证，请先登录";
                    return result;
                }

                var userId =await  ResolveUserGuid(effectivePassportId);
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IFlowerSubscriptionGrain>(userId);
                result.Data = await grain.UpgradeSubscriptionAsync(request.NewLevel, request.PaymentMethod ?? "");
                result.IsSuccess = result.Data != null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "升级订阅失败");
                result.ErrorMessage = "升级订阅失败";
            }
            return result;
        }

        [HttpGet("notification-settings")]
        public async Task<ResultVM<NotificationChannelSettings>> GetNotificationSettings([FromQuery] string? passportId)
        {
            var result = new ResultVM<NotificationChannelSettings>();
            try
            {
                var effectivePassportId = _passportCurrent?.PassportId ?? passportId;
                if (string.IsNullOrEmpty(effectivePassportId))
                {
                    result.ErrorMessage = "未获取到用户通行证，请先登录";
                    return result;
                }

                var userId =await  ResolveUserGuid(effectivePassportId);
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<INotificationGrain>(userId);
                result.Data = await grain.GetChannelSettingsAsync();
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取通知设置失败");
                result.ErrorMessage = "获取通知设置失败";
            }
            return result;
        }

        [HttpPut("notification-settings")]
        public async Task<ResultVM<bool>> UpdateNotificationSettings([FromBody] NotificationChannelSettings settings)
        {
            var result = new ResultVM<bool>();
            try
            {
                var effectivePassportId = _passportCurrent?.PassportId;
                if (string.IsNullOrEmpty(effectivePassportId))
                {
                    result.ErrorMessage = "未获取到用户通行证，请先登录";
                    return result;
                }

                var userId =await  ResolveUserGuid(effectivePassportId);
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<INotificationGrain>(userId);
                await grain.SetChannelSettingsAsync(settings);
                result.Data = true;
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新通知设置失败");
                result.ErrorMessage = "更新通知设置失败";
            }
            return result;
        }

        [HttpPut("auto-renew")]
        public async Task<ResultVM<bool>> UpdateAutoRenew([FromQuery] bool autoRenew, [FromQuery] string? passportId)
        {
            var result = new ResultVM<bool>();
            try
            {
                var effectivePassportId = _passportCurrent?.PassportId ?? passportId;
                if (string.IsNullOrEmpty(effectivePassportId))
                {
                    result.ErrorMessage = "未获取到用户通行证，请先登录";
                    return result;
                }

                var userId = await ResolveUserGuid(effectivePassportId);
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IFlowerSubscriptionGrain>(userId);
                result.Data = await grain.UpdateAutoRenewAsync(autoRenew);
                result.IsSuccess = result.Data;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新自动续费失败");
                result.ErrorMessage = "更新自动续费失败";
            }
            return result;
        }

        private async Task<Guid> ResolveUserGuid(string passportId)
        {
            var client = await OrleansConnectClient();
          var user =   client.GetGrain<IFlowerQueryGrain>(long.Parse(passportId));
            return await user.GetUserIdAsync(passportId);
            //if (Guid.TryParse(passportId, out var guid))
            //    return guid;

            //unchecked
            //{
            //    int hash = 17;
            //    foreach (var c in passportId)
            //        hash = hash * 31 + c;
            //    return new Guid(hash, (short)(hash >> 16), (short)(hash >> 8), (byte)hash, (byte)(hash >> 8), (byte)(hash >> 16), (byte)(hash >> 24), (byte)hash, (byte)(hash >> 8), (byte)(hash >> 16), (byte)(hash >> 24));
            //}
        }
    }

    public class UpgradeSubscriptionRequest
    {
        public string PassportId { get; set; } = "";
        public int NewLevel { get; set; }
        public string PaymentMethod { get; set; } = "";
    }
}

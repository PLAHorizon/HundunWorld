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
using Horizon.Share.Dtos;
using Orleans;
using Orleans.Configuration;
using Horizon.Core.Abstract;

namespace Horizon.WebApi.Controllers
{
    [ApiGroup(ApiGroupName.Basic)]
    [ApiController]
    [Route("[controller]")]
    [Authorize]
    public class FlowerMerchantController : OrleansControllerBase
    {
        private readonly ILogger<FlowerMerchantController> _logger;
        private readonly IPassportCurrentUser _passportCurrent;

        public FlowerMerchantController(
            IOptions<AdoNetOptions> options,
            IOptions<ClusterOptions> clusterOptions,
            ILogger<FlowerMerchantController> logger,
            IClusterClient clusterClient,
            IPassportCurrentUser passportCurrent)
            : base(options, clusterOptions, logger, clusterClient)
        {
            _logger = logger;
            _passportCurrent = passportCurrent;
        }

        [HttpGet("{merchantId}")]
        public async Task<ResultVM<MerchantState>> GetMerchantAsync(long merchantId)
        {
            var result = new ResultVM<MerchantState>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IMerchantGrain>(merchantId);
                result.Data = await grain.GetMerchantAsync();
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取商户详情失败: MerchantId={MerchantId}", merchantId);
                result.ErrorMessage = "获取商户详情失败";
            }
            return result;
        }

        private static long GetStableGrainKey(string passportId)
        {
            if (long.TryParse(passportId, out var key))
                return key;

            unchecked
            {
                long hash = 5381;
                foreach (var c in passportId)
                    hash = ((hash << 5) + hash) ^ c;
                return hash;
            }
        }

        [HttpGet("my")]
        public async Task<ResultVM<MerchantState>> GetMyMerchantAsync([FromQuery] string? passportId)
        {
            var result = new ResultVM<MerchantState>();
            try
            {
                var effectivePassportId = _passportCurrent?.PassportId ?? passportId;
                if (string.IsNullOrEmpty(effectivePassportId))
                {
                    result.ErrorMessage = "未获取到用户通行证，请先登录";
                    return result;
                }

                var client = await OrleansConnectClient();
                var lookupGrain = client.GetGrain<IMerchantGrain>(GetStableGrainKey(effectivePassportId));
                var merchantState = await lookupGrain.GetMerchantByPassportAsync(effectivePassportId);
                if (merchantState != null && merchantState.MerchantId > 0)
                {
                    result.Data = merchantState;
                    result.IsSuccess = true;
                }
                else
                {
                    result.Data = merchantState;
                    result.IsSuccess = merchantState != null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取当前用户商户失败");
                result.ErrorMessage = "获取商户信息失败";
            }
            return result;
        }

        [HttpPost("register")]
        public async Task<ResultVM<MerchantState>> RegisterMerchantAsync([FromBody] RegisterMerchantRequest request)
        {
            var result = new ResultVM<MerchantState>();
            try
            {
                var passportId = _passportCurrent?.PassportId ?? request.PassportId;

                if (string.IsNullOrEmpty(passportId))
                {
                    result.ErrorMessage = "未获取到用户通行证，请先登录";
                    return result;
                }

                var client = await OrleansConnectClient();

                var grainKey = GetStableGrainKey(passportId);
                var grain = client.GetGrain<IMerchantGrain>(grainKey);

                var merchantState = new MerchantState
                {
                    Passport = passportId,
                    ShopName = request.ShopName,
                    ShopDescription = request.ShopDescription ?? request.Description,
                    ContactPhone = request.ContactPhone,
                    MerchantType = ParseMerchantType(request.MerchantType),
                    BusinessLicense = request.BusinessLicense
                };

                var registered = await grain.RegisterMerchantAsync(merchantState);
                if (registered != null && registered.MerchantId > 0)
                {
                    result.Data = registered;
                    result.IsSuccess = true;
                }
                else
                {
                    result.Data = registered;
                    result.IsSuccess = registered != null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "注册商户失败");
                result.ErrorMessage = "注册商户失败: " + ex.Message;
            }
            return result;
        }

        private static MerchantType ParseMerchantType(string? type)
        {
            if (string.IsNullOrEmpty(type)) return MerchantType.Individual;
            return type.ToLowerInvariant() switch
            {
                "enterprise" or "1" => MerchantType.Enterprise,
                _ => MerchantType.Individual
            };
        }

        public class RegisterMerchantRequest
        {
            public string PassportId { get; set; } = "";
            public string ShopName { get; set; } = "";
            public string ShopDescription { get; set; } = "";
            public string Description { get; set; } = "";
            public string ContactPhone { get; set; } = "";
            public string MerchantType { get; set; } = "Individual";
            public string BusinessLicense { get; set; } = "";
        }

        [HttpPut("{merchantId}")]
        public async Task<ResultVM<MerchantState>> UpdateMerchantAsync(long merchantId, [FromBody] MerchantState merchant)
        {
            var result = new ResultVM<MerchantState>();
            try
            {
                merchant.MerchantId = merchantId;
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IMerchantGrain>(merchantId);
                result.Data = await grain.UpdateMerchantAsync(merchant);
                result.IsSuccess = result.Data != null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新商户失败: MerchantId={MerchantId}", merchantId);
                result.ErrorMessage = "更新商户失败";
            }
            return result;
        }

        [HttpPost("{merchantId}/verify")]
        public async Task<ResultVM<bool>> VerifyMerchantAsync(long merchantId)
        {
            var result = new ResultVM<bool>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IMerchantGrain>(merchantId);
                result.Data = await grain.VerifyMerchantAsync();
                result.IsSuccess = result.Data;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "认证商户失败: MerchantId={MerchantId}", merchantId);
                result.ErrorMessage = "认证商户失败";
            }
            return result;
        }

        [HttpGet("{merchantId}/shippers")]
        public async Task<ResultVM<List<ShopShipperState>>> GetShippersAsync(long merchantId)
        {
            var result = new ResultVM<List<ShopShipperState>>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IMerchantGrain>(merchantId);
                result.Data = await grain.GetShippersAsync(merchantId);
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取发货点失败: MerchantId={MerchantId}", merchantId);
                result.ErrorMessage = "获取发货点失败";
            }
            return result;
        }

        [HttpPost("{merchantId}/shippers")]
        public async Task<ResultVM<ShopShipperState>> AddShipperAsync(long merchantId, [FromBody] ShopShipperState shipper)
        {
            var result = new ResultVM<ShopShipperState>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IMerchantGrain>(merchantId);
                result.Data = await grain.AddShipperAsync(merchantId, shipper);
                result.IsSuccess = result.Data != null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "添加发货点失败: MerchantId={MerchantId}", merchantId);
                result.ErrorMessage = "添加发货点失败";
            }
            return result;
        }

        [HttpPut("{merchantId}/shippers/{shipperId}")]
        public async Task<ResultVM<ShopShipperState>> UpdateShipperAsync(long merchantId, long shipperId, [FromBody] ShopShipperState shipper)
        {
            var result = new ResultVM<ShopShipperState>();
            try
            {
                shipper.Id = shipperId;
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IMerchantGrain>(merchantId);
                result.Data = await grain.UpdateShipperAsync(shipper);
                result.IsSuccess = result.Data != null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新发货点失败: MerchantId={MerchantId}", merchantId);
                result.ErrorMessage = "更新发货点失败";
            }
            return result;
        }

        [HttpDelete("{merchantId}/shippers/{shipperId}")]
        public async Task<ResultVM<bool>> DeleteShipperAsync(long merchantId, long shipperId)
        {
            var result = new ResultVM<bool>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IMerchantGrain>(merchantId);
                result.Data = await grain.DeleteShipperAsync(merchantId, shipperId);
                result.IsSuccess = result.Data;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除发货点失败: MerchantId={MerchantId}", merchantId);
                result.ErrorMessage = "删除发货点失败";
            }
            return result;
        }

        [HttpPost("{merchantId}/audit")]
        public async Task<ResultVM<MerchantState>> AuditMerchantAsync(long merchantId, [FromBody] AuditMerchantRequest request)
        {
            var result = new ResultVM<MerchantState>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IMerchantGrain>(merchantId);
                result.Data = await grain.AuditMerchantAsync(merchantId, request.Approved, request.Reason ?? "");
                result.IsSuccess = result.Data != null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "审核商户失败: MerchantId={MerchantId}", merchantId);
                result.ErrorMessage = "审核商户失败";
            }
            return result;
        }

        [HttpPost("{merchantId}/freeze")]
        public async Task<ResultVM<bool>> FreezeMerchantAsync(long merchantId)
        {
            var result = new ResultVM<bool>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IMerchantGrain>(merchantId);
                result.Data = await grain.FreezeMerchantAsync(merchantId);
                result.IsSuccess = result.Data;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "冻结商户失败: MerchantId={MerchantId}", merchantId);
                result.ErrorMessage = "冻结商户失败";
            }
            return result;
        }

        [HttpPost("{merchantId}/unfreeze")]
        public async Task<ResultVM<bool>> UnfreezeMerchantAsync(long merchantId)
        {
            var result = new ResultVM<bool>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IMerchantGrain>(merchantId);
                result.Data = await grain.UnfreezeMerchantAsync(merchantId);
                result.IsSuccess = result.Data;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "解冻商户失败: MerchantId={MerchantId}", merchantId);
                result.ErrorMessage = "解冻商户失败";
            }
            return result;
        }
    }

    public class AuditMerchantRequest
    {
        public bool Approved { get; set; }
        public string Reason { get; set; } = "";
    }
}

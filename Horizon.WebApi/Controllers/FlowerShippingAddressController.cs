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
using Horizon.Core.Abstract;

namespace Horizon.WebApi.Controllers
{
    [ApiGroup(ApiGroupName.Basic)]
    [ApiController]
    [Route("[controller]")]
    [Authorize]
    public class FlowerShippingAddressController : OrleansControllerBase
    {
        private readonly ILogger<FlowerShippingAddressController> _logger;
        private readonly IPassportCurrentUser _passportCurrent;

        public FlowerShippingAddressController(
            IOptions<AdoNetOptions> options,
            IOptions<ClusterOptions> clusterOptions,
            ILogger<FlowerShippingAddressController> logger,
            IClusterClient clusterClient,
            IPassportCurrentUser passportCurrent)
            : base(options, clusterOptions, logger, clusterClient)
        {
            _logger = logger;
            _passportCurrent = passportCurrent;
        }

        [HttpGet("user/{userId}")]
        public async Task<ResultVM<List<ShippingAddressState>>> GetUserAddressesAsync(string userId)
        {
            var result = new ResultVM<List<ShippingAddressState>>();
            try
            {
                var currentUserId = GetAuthenticatedUserId();
                if (currentUserId == Guid.Empty)
                {
                    result.ErrorMessage = "用户未认证";
                    return result;
                }

                Guid.TryParse(userId, out Guid routeUserId);
                if (routeUserId != currentUserId)
                {
                    _logger.LogWarning("查询地址归属权校验失败: RouteUserId={RouteUserId}, CurrentUserId={CurrentUserId}", routeUserId, currentUserId);
                    result.ErrorMessage = "只能查询自己的收货地址";
                    return result;
                }

                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IShippingAddressGrain>(0);
                result.Data = await grain.GetUserAddressesAsync(currentUserId);
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取用户收货地址失败");
                result.ErrorMessage = "获取收货地址失败";
            }
            return result;
        }

        [HttpGet("{addressId}")]
        public async Task<ResultVM<ShippingAddressState>> GetAddressAsync(long addressId)
        {
            var result = new ResultVM<ShippingAddressState>();
            try
            {
                var userId = GetAuthenticatedUserId();
                if (userId == Guid.Empty)
                {
                    result.ErrorMessage = "用户未认证";
                    return result;
                }

                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IShippingAddressGrain>(0);
                var address = await grain.GetAddressAsync(addressId);
                if (address == null)
                {
                    result.ErrorMessage = "地址不存在";
                    return result;
                }
                if (address.UserId != userId)
                {
                    _logger.LogWarning("获取地址归属权校验失败: AddressId={AddressId}, UserId={UserId}", addressId, userId);
                    result.ErrorMessage = "无权查看此地址";
                    return result;
                }
                result.Data = address;
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取收货地址失败: AddressId={AddressId}", addressId);
                result.ErrorMessage = "获取收货地址失败";
            }
            return result;
        }

        [HttpPost]
        public async Task<ResultVM<ShippingAddressState>> AddAddressAsync([FromBody] ShippingAddressState state)
        {
            var result = new ResultVM<ShippingAddressState>();
            try
            {
                var userId = GetAuthenticatedUserId();
                if (userId == Guid.Empty)
                {
                    result.ErrorMessage = "用户未认证";
                    return result;
                }

                var passportId = _passportCurrent?.PassportId;
                if (!string.IsNullOrEmpty(passportId))
                    state.Passport = passportId;
                state.UserId = userId;
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IShippingAddressGrain>(0);
                var data = await grain.AddAddressAsync(state);
                if (data == null)
                {
                    result.ErrorMessage = "添加收货地址失败";
                    return result;
                }
                result.Data = data;
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "添加收货地址失败");
                result.ErrorMessage = "添加收货地址失败";
            }
            return result;
        }

        [HttpPut]
        public async Task<ResultVM<ShippingAddressState>> UpdateAddressAsync([FromBody] ShippingAddressState state)
        {
            var result = new ResultVM<ShippingAddressState>();
            try
            {
                var userId = GetAuthenticatedUserId();
                if (userId == Guid.Empty)
                {
                    result.ErrorMessage = "用户未认证";
                    return result;
                }

                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IShippingAddressGrain>(0);
                var existing = await grain.GetAddressAsync(state.Id);
                if (existing == null)
                {
                    result.ErrorMessage = "地址不存在";
                    return result;
                }
                if (existing.UserId != userId)
                {
                    _logger.LogWarning("更新地址归属权校验失败: AddressId={AddressId}, UserId={UserId}", state.Id, userId);
                    result.ErrorMessage = "无权修改此地址";
                    return result;
                }

                var passportId = _passportCurrent?.PassportId;
                if (!string.IsNullOrEmpty(passportId))
                    state.Passport = passportId;
                state.UserId = userId;

                var data = await grain.UpdateAddressAsync(state);
                if (data == null)
                {
                    result.ErrorMessage = "更新收货地址失败";
                    return result;
                }
                result.Data = data;
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新收货地址失败");
                result.ErrorMessage = "更新收货地址失败";
            }
            return result;
        }

        [HttpDelete("{userId}/{addressId}")]
        public async Task<ResultVM<bool>> DeleteAddressAsync(string userId, long addressId)
        {
            var result = new ResultVM<bool>();
            try
            {
                var currentUserId = GetAuthenticatedUserId();
                if (currentUserId == Guid.Empty)
                {
                    result.ErrorMessage = "用户未认证";
                    return result;
                }

                Guid.TryParse(userId, out Guid routeUserId);
                if (routeUserId != currentUserId)
                {
                    _logger.LogWarning("删除地址归属权校验失败: RouteUserId={RouteUserId}, CurrentUserId={CurrentUserId}", routeUserId, currentUserId);
                    result.ErrorMessage = "只能删除自己的收货地址";
                    return result;
                }

                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IShippingAddressGrain>(0);
                result.Data = await grain.DeleteAddressAsync(currentUserId, addressId);
                result.IsSuccess = result.Data;
                if (!result.IsSuccess) result.ErrorMessage = "删除收货地址失败";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除收货地址失败: AddressId={AddressId}", addressId);
                result.ErrorMessage = "删除收货地址失败";
            }
            return result;
        }

        [HttpPost("{userId}/{addressId}/set-default")]
        public async Task<ResultVM<bool>> SetDefaultAddressAsync(string userId, long addressId)
        {
            var result = new ResultVM<bool>();
            try
            {
                var currentUserId = GetAuthenticatedUserId();
                if (currentUserId == Guid.Empty)
                {
                    result.ErrorMessage = "用户未认证";
                    return result;
                }

                Guid.TryParse(userId, out Guid routeUserId);
                if (routeUserId != currentUserId)
                {
                    _logger.LogWarning("设置默认地址归属权校验失败: RouteUserId={RouteUserId}, CurrentUserId={CurrentUserId}", routeUserId, currentUserId);
                    result.ErrorMessage = "只能设置自己的默认地址";
                    return result;
                }

                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IShippingAddressGrain>(0);
                result.Data = await grain.SetDefaultAddressAsync(currentUserId, addressId);
                result.IsSuccess = result.Data;
                if (!result.IsSuccess) result.ErrorMessage = "设置默认地址失败";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "设置默认收货地址失败: AddressId={AddressId}", addressId);
                result.ErrorMessage = "设置默认地址失败";
            }
            return result;
        }

        [HttpGet("user/{userId}/default")]
        public async Task<ResultVM<ShippingAddressState>> GetDefaultAddressAsync(string userId)
        {
            var result = new ResultVM<ShippingAddressState>();
            try
            {
                var currentUserId = GetAuthenticatedUserId();
                if (currentUserId == Guid.Empty)
                {
                    result.ErrorMessage = "用户未认证";
                    return result;
                }

                Guid.TryParse(userId, out Guid routeUserId);
                if (routeUserId != currentUserId)
                {
                    _logger.LogWarning("获取默认地址归属权校验失败: RouteUserId={RouteUserId}, CurrentUserId={CurrentUserId}", routeUserId, currentUserId);
                    result.ErrorMessage = "只能查看自己的默认地址";
                    return result;
                }

                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IShippingAddressGrain>(0);
                result.Data = await grain.GetDefaultAddressAsync(currentUserId);
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取默认收货地址失败");
                result.ErrorMessage = "获取默认收货地址失败";
            }
            return result;
        }

        private Guid GetAuthenticatedUserId()
        {
            Guid.TryParse(_passportCurrent.UserId, out Guid id);
            return id;
        }
    }
}
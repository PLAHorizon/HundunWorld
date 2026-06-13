using Horizon.Core;
using Horizon.Core.Abstract;
using Horizon.Entities;
using Horizon.Game.Message.Network;
using Horizon.Model.Flower;
using Horizon.Orleans.Interface;
using Microsoft.Extensions.Logging;
using Orleans;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Horizon.Orleans.Grains
{
    public class FlowerShippingAddressGrain : Grain, IShippingAddressGrain
    {
        private readonly ILogger<FlowerShippingAddressGrain> _logger;
        private readonly IDataContext<FlowerEntityContext, FlowerShippingAddress, long> _dataContext;

        public FlowerShippingAddressGrain(
            ILogger<FlowerShippingAddressGrain> logger,
            IDataContext<FlowerEntityContext, FlowerShippingAddress, long> dataContext)
        {
            _logger = logger;
            _dataContext = dataContext;
        }

        public async Task<List<ShippingAddressState>> GetUserAddressesAsync(Guid userId)
        {
            try
            {
                var addresses = await _dataContext.QueryAsync(a => a.UserId == userId && a.IsValid);
                var list = addresses
                    .OrderByDescending(a => a.IsDefault)
                    .ThenByDescending(a => a.CreateTime)
                    .ToList();

                return list.Select(MapToState).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取用户收货地址失败: UserId={UserId}", userId);
                return new List<ShippingAddressState>();
            }
        }

        public async Task<ShippingAddressState> GetAddressAsync(long addressId)
        {
            try
            {
                var address = await _dataContext.QueryFirstOrDefaultAsync(a => a.Id == addressId);
                return address != null ? MapToState(address) : null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取收货地址失败: AddressId={AddressId}", addressId);
                return null;
            }
        }

        public async Task<ShippingAddressState> AddAddressAsync(ShippingAddressState state)
        {
            try
            {
                if (state.IsDefault)
                {
                    var existingDefaults = await _dataContext.QueryAsync(a => a.UserId == state.UserId && a.IsDefault, true);
                    foreach (var ed in existingDefaults)
                    {
                        ed.IsDefault = false;
                        await _dataContext.UpdateAsync(ed, ed.Id);
                    }
                }

                var address = new FlowerShippingAddress
                {
                    UserId = state.UserId,
                    ShipTo = state.ShipTo,
                    Phone = state.Phone,
                    ProvinceId = state.ProvinceId,
                    ProvinceName = state.ProvinceName,
                    CityId = state.CityId,
                    CityName = state.CityName,
                    DistrictId = state.DistrictId,
                    DistrictName = state.DistrictName,
                    Address = state.Address,
                    IsDefault = state.IsDefault,
                    Latitude = state.Latitude,
                    Longitude = state.Longitude,
                    Passport = !string.IsNullOrEmpty(state.Passport) ? state.Passport : this.GetPrimaryKey().ToString(),
                    CreateTime = DateTime.Now,
                    IsValid = true
                };

                var result = await _dataContext.AddAsync(address);
                _logger.LogInformation("添加收货地址: UserId={UserId}, AddressId={AddressId}", state.UserId, result.Id);
                return MapToState(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "添加收货地址失败: UserId={UserId}", state.UserId);
                return null;
            }
        }

        public async Task<ShippingAddressState> UpdateAddressAsync(ShippingAddressState state)
        {
            try
            {
                var address = await _dataContext.QueryFirstOrDefaultAsync(a => a.Id == state.Id);
                if (address == null) return null;

                if (state.IsDefault && !address.IsDefault)
                {
                    var existingDefaults = await _dataContext.QueryAsync(a => a.UserId == address.UserId && a.IsDefault, true);
                    foreach (var ed in existingDefaults.Where(e => e.Id != state.Id))
                    {
                        ed.IsDefault = false;
                        await _dataContext.UpdateAsync(ed, ed.Id);
                    }
                }

                address.ShipTo = state.ShipTo;
                address.Phone = state.Phone;
                address.ProvinceId = state.ProvinceId;
                address.ProvinceName = state.ProvinceName;
                address.CityId = state.CityId;
                address.CityName = state.CityName;
                address.DistrictId = state.DistrictId;
                address.DistrictName = state.DistrictName;
                address.Address = state.Address;
                address.IsDefault = state.IsDefault;
                address.Latitude = state.Latitude;
                address.Longitude = state.Longitude;

                await _dataContext.UpdateAsync(address, address.Id);
                _logger.LogInformation("更新收货地址: AddressId={AddressId}", state.Id);
                return MapToState(address);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新收货地址失败: AddressId={AddressId}", state.Id);
                return null;
            }
        }

        public async Task<bool> DeleteAddressAsync(Guid userId, long addressId)
        {
            try
            {
                var address = await _dataContext.QueryFirstOrDefaultAsync(a => a.Id == addressId);
                if (address == null || address.UserId != userId) return false;

                await _dataContext.DeletedAsync<FlowerShippingAddress, long>(addressId);
                _logger.LogInformation("删除收货地址: UserId={UserId}, AddressId={AddressId}", userId, addressId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除收货地址失败: AddressId={AddressId}", addressId);
                return false;
            }
        }

        public async Task<bool> SetDefaultAddressAsync(Guid userId, long addressId)
        {
            try
            {
                var existingDefaults = await _dataContext.QueryAsync(a => a.UserId == userId && a.IsDefault, true);
                foreach (var ed in existingDefaults)
                {
                    ed.IsDefault = false;
                    await _dataContext.UpdateAsync(ed, ed.Id);
                }

                var address = await _dataContext.QueryFirstOrDefaultAsync(a => a.Id == addressId, true);
                if (address == null || address.UserId != userId) return false;

                address.IsDefault = true;
                await _dataContext.UpdateAsync(address, address.Id);
                _logger.LogInformation("设置默认收货地址: UserId={UserId}, AddressId={AddressId}", userId, addressId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "设置默认收货地址失败: UserId={UserId}, AddressId={AddressId}", userId, addressId);
                return false;
            }
        }

        public async Task<ShippingAddressState> GetDefaultAddressAsync(Guid userId)
        {
            try
            {
                var address = await _dataContext.QueryFirstOrDefaultAsync(a => a.UserId == userId && a.IsDefault);
                return address != null ? MapToState(address) : null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取默认收货地址失败: UserId={UserId}", userId);
                return null;
            }
        }

        private static ShippingAddressState MapToState(FlowerShippingAddress a)
        {
            return new ShippingAddressState
            {
                Id = a.Id,
                UserId = a.UserId,
                ShipTo = a.ShipTo ?? "",
                Phone = a.Phone ?? "",
                ProvinceId = a.ProvinceId,
                ProvinceName = a.ProvinceName ?? "",
                CityId = a.CityId,
                CityName = a.CityName ?? "",
                DistrictId = a.DistrictId,
                DistrictName = a.DistrictName ?? "",
                Address = a.Address ?? "",
                IsDefault = a.IsDefault,
                Latitude = a.Latitude,
                Longitude = a.Longitude,
                Passport = a.Passport ?? ""
            };
        }
    }
}

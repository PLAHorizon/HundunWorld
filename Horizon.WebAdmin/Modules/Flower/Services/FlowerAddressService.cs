using System.Text.Json;
using Horizon.Share.VMs;
using Microsoft.Extensions.Configuration;

namespace Horizon.WebAdmin.Modules.Flower.Services;

public class FlowerAddressService : FlowerApiServiceBase
{
    public FlowerAddressService(HttpClient httpClient, IConfiguration configuration) : base(httpClient, configuration) { }

    public async Task<ResultVM<JsonElement?>?> GetUserAddressesAsync(long userId) => await GetAsync<JsonElement?>($"FlowerShippingAddress/user/{userId}");

    public async Task<ResultVM<JsonElement?>?> GetAddressAsync(long addressId) => await GetAsync<JsonElement?>($"FlowerShippingAddress/{addressId}");

    public async Task<ResultVM<JsonElement?>?> AddAddressAsync(object data) => await PostAsync<JsonElement?>("FlowerShippingAddress", data);

    public async Task<ResultVM<JsonElement?>?> UpdateAddressAsync(object data) => await PutAsync<JsonElement?>("FlowerShippingAddress", data);

    public async Task<ResultVM<JsonElement?>?> DeleteAddressAsync(long userId, long addressId) => await DeleteAsync<JsonElement?>($"FlowerShippingAddress/{userId}/{addressId}");

    public async Task<ResultVM<JsonElement?>?> SetDefaultAddressAsync(long userId, long addressId) => await PostAsync<JsonElement?>($"FlowerShippingAddress/{userId}/{addressId}/set-default");

    public async Task<ResultVM<JsonElement?>?> GetDefaultAddressAsync(long userId) => await GetAsync<JsonElement?>($"FlowerShippingAddress/user/{userId}/default");
}

using System.Text.Json;
using Horizon.Share.VMs;
using Microsoft.Extensions.Configuration;

namespace Horizon.WebAdmin.Modules.Flower.Services;

public class FlowerMerchantService : FlowerApiServiceBase
{
    public FlowerMerchantService(HttpClient httpClient, IConfiguration configuration) : base(httpClient, configuration) { }

    public async Task<ResultVM<JsonElement?>?> GetMerchantListAsync(string? shopName = null)
    {
        var url = "FlowerAdmin/merchants";
        if (!string.IsNullOrEmpty(shopName)) url += $"?shopName={Uri.EscapeDataString(shopName)}";
        return await GetAsync<JsonElement?>(url);
    }

    public async Task<ResultVM<JsonElement?>?> GetMerchantAsync(long merchantId) => await GetAsync<JsonElement?>($"FlowerMerchant/{merchantId}");

    public async Task<ResultVM<JsonElement?>?> GetMyMerchantAsync(string? passportId = null)
    {
        var url = "FlowerMerchant/my";
        if (!string.IsNullOrEmpty(passportId)) url += $"?passportId={passportId}";
        return await GetAsync<JsonElement?>(url);
    }

    public async Task<ResultVM<JsonElement?>?> RegisterMerchantAsync(object data) => await PostAsync<JsonElement?>("FlowerMerchant/register", data);

    public async Task<ResultVM<JsonElement?>?> UpdateMerchantAsync(long merchantId, object data) => await PutAsync<JsonElement?>($"FlowerMerchant/{merchantId}", data);

    public async Task<ResultVM<JsonElement?>?> VerifyMerchantAsync(long merchantId) => await PostAsync<JsonElement?>($"FlowerMerchant/{merchantId}/verify");

    public async Task<ResultVM<JsonElement?>?> GetShippersAsync(long merchantId) => await GetAsync<JsonElement?>($"FlowerMerchant/{merchantId}/shippers");

    public async Task<ResultVM<JsonElement?>?> AddShipperAsync(long merchantId, object data) => await PostAsync<JsonElement?>($"FlowerMerchant/{merchantId}/shippers", data);

    public async Task<ResultVM<JsonElement?>?> UpdateShipperAsync(long merchantId, long shipperId, object data) => await PutAsync<JsonElement?>($"FlowerMerchant/{merchantId}/shippers/{shipperId}", data);

    public async Task<ResultVM<JsonElement?>?> DeleteShipperAsync(long merchantId, long shipperId) => await DeleteAsync<JsonElement?>($"FlowerMerchant/{merchantId}/shippers/{shipperId}");

    public async Task<ResultVM<JsonElement?>?> AuditMerchantAsync(long merchantId, object data) => await PostAsync<JsonElement?>($"FlowerAdmin/merchant/{merchantId}/audit", data);

    public async Task<ResultVM<JsonElement?>?> FreezeMerchantAsync(long merchantId) => await PostAsync<JsonElement?>($"FlowerAdmin/merchant/{merchantId}/freeze");

    public async Task<ResultVM<JsonElement?>?> UnfreezeMerchantAsync(long merchantId) => await PostAsync<JsonElement?>($"FlowerAdmin/merchant/{merchantId}/unfreeze");
}

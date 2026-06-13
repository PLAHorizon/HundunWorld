using System.Text.Json;
using Horizon.Share.VMs;
using Microsoft.Extensions.Configuration;

namespace Horizon.WebAdmin.Modules.Flower.Services;

public class FlowerAdminService : FlowerApiServiceBase
{
    public FlowerAdminService(HttpClient httpClient, IConfiguration configuration) : base(httpClient, configuration) { }

    public async Task<ResultVM<JsonElement?>?> AuditMerchantAsync(long merchantId, object data) => await PostAsync<JsonElement?>($"FlowerAdmin/merchant/{merchantId}/audit", data);

    public async Task<ResultVM<JsonElement?>?> FreezeMerchantAsync(long merchantId) => await PostAsync<JsonElement?>($"FlowerAdmin/merchant/{merchantId}/freeze");

    public async Task<ResultVM<JsonElement?>?> UnfreezeMerchantAsync(long merchantId) => await PostAsync<JsonElement?>($"FlowerAdmin/merchant/{merchantId}/unfreeze");

    public async Task<ResultVM<JsonElement?>?> AuditProductAsync(long productId, object data) => await PostAsync<JsonElement?>($"FlowerAdmin/product/{productId}/audit", data);

    public async Task<ResultVM<JsonElement?>?> PlatformAuditRefundAsync(long refundId, object data) => await PostAsync<JsonElement?>($"FlowerAdmin/refund/{refundId}/platform-audit", data);

    public async Task<ResultVM<JsonElement?>?> GetRefundsAsync(int? status = null, int page = 1, int pageSize = 20)
    {
        var url = $"FlowerAdmin/refunds?page={page}&pageSize={pageSize}";
        if (status.HasValue) url += $"&status={status}";
        return await GetAsync<JsonElement?>(url);
    }

    public async Task<ResultVM<JsonElement?>?> GetStatisticsAsync() => await GetAsync<JsonElement?>("FlowerAdmin/statistics");
}

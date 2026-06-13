using System.Text.Json;
using Horizon.Share.VMs;
using Microsoft.Extensions.Configuration;

namespace Horizon.WebAdmin.Modules.Flower.Services;

public class FlowerLogisticsService : FlowerApiServiceBase
{
    public FlowerLogisticsService(HttpClient httpClient, IConfiguration configuration) : base(httpClient, configuration) { }

    public async Task<ResultVM<JsonElement?>?> GetTrackAsync(long orderId) => await GetAsync<JsonElement?>($"FlowerLogistics/{orderId}");

    public async Task<ResultVM<JsonElement?>?> GetMapDataAsync(long orderId) => await GetAsync<JsonElement?>($"FlowerLogistics/{orderId}/map");

    public async Task<ResultVM<JsonElement?>?> GetTrackHistoryAsync(long orderId) => await GetAsync<JsonElement?>($"FlowerLogistics/{orderId}/history");

    public async Task<ResultVM<JsonElement?>?> GetReturnTrackAsync(long refundId, string? expressCompanyName = null, string? shipOrderNumber = null)
    {
        var url = $"FlowerLogistics/return/{refundId}";
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(expressCompanyName)) parts.Add($"expressCompanyName={expressCompanyName}");
        if (!string.IsNullOrEmpty(shipOrderNumber)) parts.Add($"shipOrderNumber={shipOrderNumber}");
        if (parts.Count > 0) url += "?" + string.Join("&", parts);
        return await GetAsync<JsonElement?>(url);
    }

    public async Task<ResultVM<JsonElement?>?> GetAlertsAsync() => await GetAsync<JsonElement?>("FlowerLogistics/alerts");

    public async Task<ResultVM<JsonElement?>?> GetExpressCompaniesAsync() => await GetAsync<JsonElement?>("FlowerLogistics/companies");
}

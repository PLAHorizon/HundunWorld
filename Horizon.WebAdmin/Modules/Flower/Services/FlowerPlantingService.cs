using System.Text.Json;
using Horizon.Share.VMs;
using Microsoft.Extensions.Configuration;

namespace Horizon.WebAdmin.Modules.Flower.Services;

public class FlowerPlantingService : FlowerApiServiceBase
{
    public FlowerPlantingService(HttpClient httpClient, IConfiguration configuration) : base(httpClient, configuration) { }

    public async Task<ResultVM<JsonElement?>?> CreateBatchAsync(object data) => await PostAsync<JsonElement?>("FlowerPlanting/batches", data);

    public async Task<ResultVM<JsonElement?>?> ListBatchesAsync(string greenhouseId, string? status = null, int page = 1, int pageSize = 20)
    {
        var url = $"FlowerPlanting/batches/{greenhouseId}?page={page}&pageSize={pageSize}";
        if (!string.IsNullOrEmpty(status)) url += $"&status={status}";
        return await GetAsync<JsonElement?>(url);
    }

    public async Task<ResultVM<JsonElement?>?> UpdateBatchStatusAsync(long batchId, object data) => await PutAsync<JsonElement?>($"FlowerPlanting/batches/{batchId}/status", data);

    public async Task<ResultVM<JsonElement?>?> GetBatchLifecycleAsync(long batchId) => await GetAsync<JsonElement?>($"FlowerPlanting/batches/{batchId}/lifecycle");

    public async Task<ResultVM<JsonElement?>?> GetBatchProfitAsync(long batchId) => await GetAsync<JsonElement?>($"FlowerPlanting/batches/{batchId}/profit");

    public async Task<ResultVM<JsonElement?>?> GetPresaleStatusAsync(long batchId) => await GetAsync<JsonElement?>($"FlowerPlanting/batches/{batchId}/presale-status");
}

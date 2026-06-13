using System.Text.Json;
using Horizon.Share.VMs;
using Microsoft.Extensions.Configuration;

namespace Horizon.WebAdmin.Modules.Flower.Services;

public class FlowerDataPoolService : FlowerApiServiceBase
{
    public FlowerDataPoolService(HttpClient httpClient, IConfiguration configuration) : base(httpClient, configuration) { }

    public async Task<ResultVM<JsonElement?>?> WriteAsync(object data) => await PostAsync<JsonElement?>("FlowerDataPool", data);

    public async Task<ResultVM<JsonElement?>?> QueryAsync(int dataType, DateTime? startTime = null, DateTime? endTime = null, int pageNo = 1, int pageSize = 20)
    {
        var url = $"FlowerDataPool?dataType={dataType}&pageNo={pageNo}&pageSize={pageSize}";
        if (startTime.HasValue) url += $"&startTime={Uri.EscapeDataString(startTime.Value.ToString("O"))}";
        if (endTime.HasValue) url += $"&endTime={Uri.EscapeDataString(endTime.Value.ToString("O"))}";
        return await GetAsync<JsonElement?>(url);
    }
}

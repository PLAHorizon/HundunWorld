using System.Text.Json;
using Horizon.Share.VMs;
using Microsoft.Extensions.Configuration;

namespace Horizon.WebAdmin.Modules.Flower.Services;

public class FlowerReportService : FlowerApiServiceBase
{
    public FlowerReportService(HttpClient httpClient, IConfiguration configuration) : base(httpClient, configuration) { }

    public async Task<ResultVM<JsonElement?>?> GetDailyReportAsync(DateTime? date = null)
    {
        var url = "FlowerReport/daily";
        if (date.HasValue) url += $"?date={date.Value:O}";
        return await GetAsync<JsonElement?>(url);
    }

    public async Task<ResultVM<JsonElement?>?> GenerateDailyReportAsync(object data) => await PostAsync<JsonElement?>("FlowerReport/generate", data);
}

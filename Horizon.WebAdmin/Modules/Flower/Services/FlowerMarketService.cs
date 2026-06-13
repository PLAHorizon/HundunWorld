using System.Text.Json;
using Horizon.Share.VMs;
using Microsoft.Extensions.Configuration;

namespace Horizon.WebAdmin.Modules.Flower.Services;

public class FlowerMarketService : FlowerApiServiceBase
{
    public FlowerMarketService(HttpClient httpClient, IConfiguration configuration) : base(httpClient, configuration) { }

    public async Task<ResultVM<JsonElement?>?> GetOverviewAsync() => await GetAsync<JsonElement?>("FlowerMarket/overview");

    public async Task<ResultVM<JsonElement?>?> GetSpeciesAsync() => await GetAsync<JsonElement?>("FlowerMarket/species");

    public async Task<ResultVM<JsonElement?>?> GetPriceHistoryAsync(int id, DateTime? startTime = null, DateTime? endTime = null)
    {
        var query = $"FlowerMarket/species/{id}/price-history";
        if (startTime.HasValue || endTime.HasValue)
        {
            var parts = new List<string>();
            if (startTime.HasValue) parts.Add($"startTime={startTime.Value:O}");
            if (endTime.HasValue) parts.Add($"endTime={endTime.Value:O}");
            query += "?" + string.Join("&", parts);
        }
        return await GetAsync<JsonElement?>(query);
    }

    public async Task<ResultVM<JsonElement?>?> GetForecastAsync(int id, string timeScale = "ShortTerm", int horizonDays = 7) => await GetAsync<JsonElement?>($"FlowerMarket/species/{id}/forecast?timeScale={timeScale}&horizonDays={horizonDays}");
}

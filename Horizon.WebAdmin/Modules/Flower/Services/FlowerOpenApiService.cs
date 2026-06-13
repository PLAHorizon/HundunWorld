using System.Text.Json;
using Horizon.Share.VMs;
using Microsoft.Extensions.Configuration;

namespace Horizon.WebAdmin.Modules.Flower.Services;

public class FlowerOpenApiService : FlowerApiServiceBase
{
    public FlowerOpenApiService(HttpClient httpClient, IConfiguration configuration) : base(httpClient, configuration) { }

    public async Task<ResultVM<JsonElement?>?> GetMarketOverviewAsync() => await GetAsync<JsonElement?>("api/open/FlowerOpenApi/market/overview");

    public async Task<ResultVM<JsonElement?>?> GetSpeciesPriceAsync(int speciesId) => await GetAsync<JsonElement?>($"api/open/FlowerOpenApi/market/species/{speciesId}/price");

    public async Task<ResultVM<JsonElement?>?> GetSpeciesForecastAsync(int speciesId, string timeScale = "ShortTerm", int horizonDays = 7) => await GetAsync<JsonElement?>($"api/open/FlowerOpenApi/market/species/{speciesId}/forecast?timeScale={timeScale}&horizonDays={horizonDays}");

    public async Task<ResultVM<JsonElement?>?> GetHotSpeciesAsync(int topN = 10) => await GetAsync<JsonElement?>($"api/open/FlowerOpenApi/market/hot-species?topN={topN}");

    public async Task<ResultVM<JsonElement?>?> GetMarketSummaryAsync() => await GetAsync<JsonElement?>("api/open/FlowerOpenApi/dashboard/summary");
}

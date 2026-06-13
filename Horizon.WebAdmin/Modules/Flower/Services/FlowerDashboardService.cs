using System.Text.Json;
using Horizon.Share.VMs;
using Microsoft.Extensions.Configuration;

namespace Horizon.WebAdmin.Modules.Flower.Services;

public class FlowerDashboardService : FlowerApiServiceBase
{
    public FlowerDashboardService(HttpClient httpClient, IConfiguration configuration) : base(httpClient, configuration) { }

    public async Task<ResultVM<JsonElement?>?> GetOverviewAsync() => await GetAsync<JsonElement?>("FlowerDashboard/overview");

    public async Task<ResultVM<JsonElement?>?> GetRegionalHeatmapAsync() => await GetAsync<JsonElement?>("FlowerDashboard/regional-heatmap");

    public async Task<ResultVM<JsonElement?>?> GetSupplyDemandAsync() => await GetAsync<JsonElement?>("FlowerDashboard/supply-demand");

    public async Task<ResultVM<JsonElement?>?> GetSupplyDemandStatsAsync() => await GetAsync<JsonElement?>("FlowerDashboard/supply-demand-stats");

    public async Task<ResultVM<JsonElement?>?> GetPriceTrendAsync(int speciesId, int days = 30) => await GetAsync<JsonElement?>($"FlowerDashboard/price-trend/{speciesId}?days={days}");

    public async Task<ResultVM<JsonElement?>?> GetAIMarketSummaryAsync() => await GetAsync<JsonElement?>("FlowerDashboard/ai-summary");

    public async Task<ResultVM<JsonElement?>?> GetStatsAsync() => await GetAsync<JsonElement?>("FlowerDashboard/stats");

    public async Task<ResultVM<JsonElement?>?> GetRegionalAsync() => await GetAsync<JsonElement?>("FlowerDashboard/regional");

    public async Task<ResultVM<JsonElement?>?> GetSupplyDemandDataAsync() => await GetAsync<JsonElement?>("FlowerDashboard/supply-demand");

    public async Task<ResultVM<JsonElement?>?> GetTransactionsAsync() => await GetAsync<JsonElement?>("FlowerDashboard/transactions");
}

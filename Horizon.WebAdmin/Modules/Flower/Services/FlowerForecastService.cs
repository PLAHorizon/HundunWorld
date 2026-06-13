using System.Text.Json;
using Horizon.Share.VMs;
using Microsoft.Extensions.Configuration;

namespace Horizon.WebAdmin.Modules.Flower.Services;

public class FlowerForecastService : FlowerApiServiceBase
{
    public FlowerForecastService(HttpClient httpClient, IConfiguration configuration) : base(httpClient, configuration) { }

    public async Task<ResultVM<JsonElement?>?> GetModelListAsync() => await GetAsync<JsonElement?>("FlowerAdmin/forecast-models");

    public async Task<ResultVM<JsonElement?>?> ToggleModelAsync(int speciesId, bool isActive) => await PostAsync<JsonElement?>($"FlowerAdmin/forecast-models/{speciesId}/toggle", new { IsActive = isActive });

    public async Task<ResultVM<JsonElement?>?> PredictPriceAsync(int speciesId, string timeScale = "ShortTerm", int horizonDays = 7) => await GetAsync<JsonElement?>($"FlowerForecast/{speciesId}?timeScale={timeScale}&horizonDays={horizonDays}");

    public async Task<ResultVM<JsonElement?>?> TriggerDailyForecastAsync() => await PostAsync<JsonElement?>("FlowerForecast/trigger");
}

using System.Text.Json;
using Horizon.Share.VMs;
using Microsoft.Extensions.Configuration;

namespace Horizon.WebAdmin.Modules.Flower.Services;

public class FlowerAlertService : FlowerApiServiceBase
{
    public FlowerAlertService(HttpClient httpClient, IConfiguration configuration) : base(httpClient, configuration) { }

    public async Task<ResultVM<JsonElement?>?> GetAlertRulesAsync(Guid? userId = null) => await GetAsync<JsonElement?>($"FlowerAlert/alerts/rules{(userId.HasValue ? $"?userId={userId.Value}" : "")}");

    public async Task<ResultVM<JsonElement?>?> CreateAlertRuleAsync(object data) => await PostAsync<JsonElement?>("FlowerAlert/alerts/rules", data);

    public async Task<ResultVM<JsonElement?>?> UpdateAlertRuleAsync(long id, object data, Guid? userId = null) => await PutAsync<JsonElement?>($"FlowerAlert/alerts/rules/{id}{(userId.HasValue ? $"?userId={userId.Value}" : "")}", data);

    public async Task<ResultVM<JsonElement?>?> DeleteAlertRuleAsync(long id, Guid? userId = null) => await DeleteAsync<JsonElement?>($"FlowerAlert/alerts/rules/{id}{(userId.HasValue ? $"?userId={userId.Value}" : "")}");

    public async Task<ResultVM<JsonElement?>?> GetAlertLogsAsync(Guid? userId = null, int skip = 0, int take = 20) => await GetAsync<JsonElement?>($"FlowerAlert/alerts/logs{(userId.HasValue ? $"?userId={userId.Value}&skip={skip}&take={take}" : $"?skip={skip}&take={take}")}");
}

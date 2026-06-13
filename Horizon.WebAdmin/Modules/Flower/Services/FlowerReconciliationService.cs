using System.Text.Json;
using Horizon.Share.VMs;
using Microsoft.Extensions.Configuration;

namespace Horizon.WebAdmin.Modules.Flower.Services;

public class FlowerReconciliationService : FlowerApiServiceBase
{
    public FlowerReconciliationService(HttpClient httpClient, IConfiguration configuration) : base(httpClient, configuration) { }

    public async Task<ResultVM<JsonElement?>?> RunReconciliationAsync() => await PostAsync<JsonElement?>("FlowerReconciliation/run");

    public async Task<ResultVM<JsonElement?>?> GetLastRunTimeAsync() => await GetAsync<JsonElement?>("FlowerReconciliation/last-run-time");

    public async Task<ResultVM<JsonElement?>?> GetLastInconsistencyCountAsync() => await GetAsync<JsonElement?>("FlowerReconciliation/last-inconsistency-count");
}

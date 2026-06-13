using System.Text.Json;
using Horizon.Share.VMs;
using Microsoft.Extensions.Configuration;

namespace Horizon.WebAdmin.Modules.Flower.Services;

public class FlowerSettledConfigService : FlowerApiServiceBase
{
    public FlowerSettledConfigService(HttpClient httpClient, IConfiguration configuration) : base(httpClient, configuration) { }

    public async Task<ResultVM<JsonElement?>?> GetSettledConfigAsync() => await GetAsync<JsonElement?>("FlowerSettledConfig");

    public async Task<ResultVM<JsonElement?>?> UpdateSettledConfigAsync(object data) => await PutAsync<JsonElement?>("FlowerSettledConfig", data);
}

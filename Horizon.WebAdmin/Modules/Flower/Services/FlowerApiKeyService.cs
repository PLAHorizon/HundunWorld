using System.Text.Json;
using Horizon.Share.VMs;
using Microsoft.Extensions.Configuration;

namespace Horizon.WebAdmin.Modules.Flower.Services;

public class FlowerApiKeyService : FlowerApiServiceBase
{
    public FlowerApiKeyService(HttpClient httpClient, IConfiguration configuration) : base(httpClient, configuration) { }

    public async Task<ResultVM<JsonElement?>?> ListApiKeysAsync(long passportId) => await GetAsync<JsonElement?>($"FlowerApiKey/list?passportId={passportId}");

    public async Task<ResultVM<JsonElement?>?> CreateApiKeyAsync(object data) => await PostAsync<JsonElement?>("FlowerApiKey/create", data);

    public async Task<ResultVM<JsonElement?>?> RevokeApiKeyAsync(long keyId, long passportId) => await PostAsync<JsonElement?>($"FlowerApiKey/{keyId}/revoke?passportId={passportId}");
}

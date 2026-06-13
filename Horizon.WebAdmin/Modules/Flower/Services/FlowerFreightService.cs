using System.Text.Json;
using Horizon.Share.VMs;
using Microsoft.Extensions.Configuration;

namespace Horizon.WebAdmin.Modules.Flower.Services;

public class FlowerFreightService : FlowerApiServiceBase
{
    public FlowerFreightService(HttpClient httpClient, IConfiguration configuration) : base(httpClient, configuration) { }

    public async Task<ResultVM<JsonElement?>?> GetMerchantTemplatesAsync(long merchantId) => await GetAsync<JsonElement?>($"FlowerFreightTemplate/merchant/{merchantId}");

    public async Task<ResultVM<JsonElement?>?> GetTemplateAsync(long templateId) => await GetAsync<JsonElement?>($"FlowerFreightTemplate/{templateId}");

    public async Task<ResultVM<JsonElement?>?> AddTemplateAsync(object data) => await PostAsync<JsonElement?>("FlowerFreightTemplate", data);

    public async Task<ResultVM<JsonElement?>?> UpdateTemplateAsync(long templateId, object data) => await PutAsync<JsonElement?>($"FlowerFreightTemplate/{templateId}", data);

    public async Task<ResultVM<JsonElement?>?> DeleteTemplateAsync(long templateId) => await DeleteAsync<JsonElement?>($"FlowerFreightTemplate/{templateId}");

    public async Task<ResultVM<JsonElement?>?> CalculateFreightAsync(long templateId, object data) => await PostAsync<JsonElement?>($"FlowerFreightTemplate/{templateId}/calculate", data);
}

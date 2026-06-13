using System.Text.Json;
using Horizon.Share.VMs;
using Microsoft.Extensions.Configuration;

namespace Horizon.WebAdmin.Modules.Flower.Services;

public class FlowerBusinessCategoryService : FlowerApiServiceBase
{
    public FlowerBusinessCategoryService(HttpClient httpClient, IConfiguration configuration) : base(httpClient, configuration) { }

    public async Task<ResultVM<JsonElement?>?> GetBusinessCategoryAsync(long id) => await GetAsync<JsonElement?>($"FlowerBusinessCategory/{id}");

    public async Task<ResultVM<JsonElement?>?> GetShopBusinessCategoriesAsync(long shopId) => await GetAsync<JsonElement?>($"FlowerBusinessCategory/shop/{shopId}");

    public async Task<ResultVM<JsonElement?>?> ApplyBusinessCategoryAsync(object data) => await PostAsync<JsonElement?>("FlowerBusinessCategory/apply", data);

    public async Task<ResultVM<JsonElement?>?> AuditBusinessCategoryAsync(long id, object data) => await PostAsync<JsonElement?>($"FlowerBusinessCategory/{id}/audit", data);
}

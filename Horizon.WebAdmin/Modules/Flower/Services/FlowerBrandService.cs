using System.Text.Json;
using Horizon.Share.VMs;
using Microsoft.Extensions.Configuration;

namespace Horizon.WebAdmin.Modules.Flower.Services;

public class FlowerBrandService : FlowerApiServiceBase
{
    public FlowerBrandService(HttpClient httpClient, IConfiguration configuration) : base(httpClient, configuration) { }

    public async Task<ResultVM<JsonElement?>?> GetBrandAsync(long brandId) => await GetAsync<JsonElement?>($"FlowerBrand/{brandId}");

    public async Task<ResultVM<JsonElement?>?> GetAllBrandsAsync() => await GetAsync<JsonElement?>("FlowerBrand");

    public async Task<ResultVM<JsonElement?>?> AddBrandAsync(object data) => await PostAsync<JsonElement?>("FlowerBrand", data);

    public async Task<ResultVM<JsonElement?>?> UpdateBrandAsync(long brandId, object data) => await PutAsync<JsonElement?>($"FlowerBrand/{brandId}", data);

    public async Task<ResultVM<JsonElement?>?> DeleteBrandAsync(long brandId) => await DeleteAsync<JsonElement?>($"FlowerBrand/{brandId}");

    public async Task<ResultVM<JsonElement?>?> ApplyBrandAsync(object data) => await PostAsync<JsonElement?>("FlowerBrand/apply", data);

    public async Task<ResultVM<JsonElement?>?> AuditBrandApplyAsync(long applyId, object data) => await PostAsync<JsonElement?>($"FlowerBrand/apply/{applyId}/audit", data);

    public async Task<ResultVM<JsonElement?>?> GetShopBrandAppliesAsync(long shopId) => await GetAsync<JsonElement?>($"FlowerBrand/shop/{shopId}/applies");
}

using System.Text.Json;
using Horizon.Share.VMs;
using Microsoft.Extensions.Configuration;

namespace Horizon.WebAdmin.Modules.Flower.Services;

public class FlowerCategoryService : FlowerApiServiceBase
{
    public FlowerCategoryService(HttpClient httpClient, IConfiguration configuration) : base(httpClient, configuration) { }

    public async Task<ResultVM<JsonElement?>?> GetCategoryTreeAsync() => await GetAsync<JsonElement?>("FlowerProductCategory/tree");

    public async Task<ResultVM<JsonElement?>?> GetCategoryAsync(long categoryId) => await GetAsync<JsonElement?>($"FlowerProductCategory/{categoryId}");

    public async Task<ResultVM<JsonElement?>?> GetSubCategoriesAsync(long parentCategoryId) => await GetAsync<JsonElement?>($"FlowerProductCategory/{parentCategoryId}/children");

    public async Task<ResultVM<JsonElement?>?> AddCategoryAsync(object data) => await PostAsync<JsonElement?>("FlowerProductCategory", data);

    public async Task<ResultVM<JsonElement?>?> UpdateCategoryAsync(long categoryId, object data) => await PutAsync<JsonElement?>($"FlowerProductCategory/{categoryId}", data);

    public async Task<ResultVM<JsonElement?>?> DeleteCategoryAsync(long categoryId) => await DeleteAsync<JsonElement?>($"FlowerProductCategory/{categoryId}");
}

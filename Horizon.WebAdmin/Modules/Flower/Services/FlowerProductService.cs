using System.Text.Json;
using Horizon.Share.VMs;
using Microsoft.Extensions.Configuration;

namespace Horizon.WebAdmin.Modules.Flower.Services;

public class FlowerProductService : FlowerApiServiceBase
{
    public FlowerProductService(HttpClient httpClient, IConfiguration configuration) : base(httpClient, configuration) { }

    public async Task<ResultVM<JsonElement?>?> GetProductAsync(long productId) => await GetAsync<JsonElement?>($"FlowerProduct/{productId}");

    public async Task<ResultVM<JsonElement?>?> CreateProductAsync(object data) => await PostAsync<JsonElement?>("FlowerProduct", data);

    public async Task<ResultVM<JsonElement?>?> UpdateProductAsync(long productId, object data) => await PutAsync<JsonElement?>($"FlowerProduct/{productId}", data);

    public async Task<ResultVM<JsonElement?>?> GetMerchantProductsAsync(long merchantId, int page = 1, int pageSize = 20) => await GetAsync<JsonElement?>($"FlowerProduct/merchant/{merchantId}?page={page}&pageSize={pageSize}");

    public async Task<ResultVM<JsonElement?>?> GetActiveProductsAsync(int speciesId = 0, int page = 1, int pageSize = 20) => await GetAsync<JsonElement?>($"FlowerProduct/active?speciesId={speciesId}&page={page}&pageSize={pageSize}");

    public async Task<ResultVM<JsonElement?>?> ToggleProductActiveAsync(long productId, object data) => await PostAsync<JsonElement?>($"FlowerProduct/{productId}/toggle-active", data);

    public async Task<ResultVM<JsonElement?>?> DeleteProductAsync(long productId) => await DeleteAsync<JsonElement?>($"FlowerProduct/{productId}");

    public async Task<ResultVM<JsonElement?>?> AuditProductAsync(long productId, object data) => await PostAsync<JsonElement?>($"FlowerAdmin/product/{productId}/audit", data);

    public async Task<ResultVM<JsonElement?>?> GetProductSKUsAsync(long productId) => await GetAsync<JsonElement?>($"FlowerProduct/{productId}/skus");

    public async Task<ResultVM<JsonElement?>?> AddProductSKUAsync(long productId, object data) => await PostAsync<JsonElement?>($"FlowerProduct/{productId}/skus", data);

    public async Task<ResultVM<JsonElement?>?> DeleteProductSKUAsync(long productId, long skuId) => await DeleteAsync<JsonElement?>($"FlowerProduct/{productId}/skus/{skuId}");

    public async Task<ResultVM<JsonElement?>?> GetSuggestedPriceAsync(int speciesId) => await GetAsync<JsonElement?>($"FlowerProduct/suggested-price/{speciesId}");

    public async Task<ResultVM<JsonElement?>?> GetPriceAdjustmentSuggestionsAsync(long merchantId) => await GetAsync<JsonElement?>($"FlowerProduct/price-suggestions/{merchantId}");

    public async Task<ResultVM<JsonElement?>?> CreatePresaleProductAsync(object data) => await PostAsync<JsonElement?>("FlowerProduct/presale", data);
}

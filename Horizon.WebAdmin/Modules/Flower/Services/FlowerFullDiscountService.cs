using System.Text.Json;
using Horizon.Share.VMs;
using Microsoft.Extensions.Configuration;

namespace Horizon.WebAdmin.Modules.Flower.Services;

public class FlowerFullDiscountService : FlowerApiServiceBase
{
    public FlowerFullDiscountService(HttpClient httpClient, IConfiguration configuration) : base(httpClient, configuration) { }

    public async Task<ResultVM<JsonElement?>?> GetRuleAsync(long ruleId) => await GetAsync<JsonElement?>($"FlowerFullDiscount/{ruleId}");

    public async Task<ResultVM<JsonElement?>?> GetShopRulesAsync(long shopId) => await GetAsync<JsonElement?>($"FlowerFullDiscount/shop/{shopId}");

    public async Task<ResultVM<JsonElement?>?> AddRuleAsync(object data) => await PostAsync<JsonElement?>("FlowerFullDiscount", data);

    public async Task<ResultVM<JsonElement?>?> UpdateRuleAsync(long ruleId, object data) => await PutAsync<JsonElement?>($"FlowerFullDiscount/{ruleId}", data);

    public async Task<ResultVM<JsonElement?>?> DeleteRuleAsync(long ruleId) => await DeleteAsync<JsonElement?>($"FlowerFullDiscount/{ruleId}");

    public async Task<ResultVM<JsonElement?>?> CalculateDiscountAsync(object data) => await PostAsync<JsonElement?>("FlowerFullDiscount/calculate", data);
}

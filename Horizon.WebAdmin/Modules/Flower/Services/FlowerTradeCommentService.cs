using System.Text.Json;
using Horizon.Share.VMs;
using Microsoft.Extensions.Configuration;

namespace Horizon.WebAdmin.Modules.Flower.Services;

public class FlowerTradeCommentService : FlowerApiServiceBase
{
    public FlowerTradeCommentService(HttpClient httpClient, IConfiguration configuration) : base(httpClient, configuration) { }

    public async Task<ResultVM<JsonElement?>?> SubmitTradeCommentAsync(object data) => await PostAsync<JsonElement?>("FlowerTradeComment", data);

    public async Task<ResultVM<JsonElement?>?> GetOrderTradeCommentAsync(long orderId) => await GetAsync<JsonElement?>($"FlowerTradeComment/order/{orderId}");

    public async Task<ResultVM<JsonElement?>?> GetShopTradeCommentsAsync(long shopId) => await GetAsync<JsonElement?>($"FlowerTradeComment/shop/{shopId}");

    public async Task<ResultVM<JsonElement?>?> GetShopAverageScoreAsync(long shopId) => await GetAsync<JsonElement?>($"FlowerTradeComment/shop/{shopId}/average");
}

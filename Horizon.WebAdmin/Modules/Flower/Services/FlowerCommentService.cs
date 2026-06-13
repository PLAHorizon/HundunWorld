using System.Text.Json;
using Horizon.Share.VMs;
using Microsoft.Extensions.Configuration;

namespace Horizon.WebAdmin.Modules.Flower.Services;

public class FlowerCommentService : FlowerApiServiceBase
{
    public FlowerCommentService(HttpClient httpClient, IConfiguration configuration) : base(httpClient, configuration) { }

    public async Task<ResultVM<JsonElement?>?> GetProductCommentsAsync(long productId, int page = 1, int pageSize = 20) => await GetAsync<JsonElement?>($"FlowerProductComment/product/{productId}?page={page}&pageSize={pageSize}");

    public async Task<ResultVM<JsonElement?>?> SubmitCommentAsync(object data) => await PostAsync<JsonElement?>("FlowerProductComment", data);

    public async Task<ResultVM<JsonElement?>?> ReplyCommentAsync(long commentId, object data) => await PostAsync<JsonElement?>($"FlowerProductComment/{commentId}/reply", data);

    public async Task<ResultVM<JsonElement?>?> GetMerchantCommentsAsync(long merchantId, int page = 1, int pageSize = 20) => await GetAsync<JsonElement?>($"FlowerProductComment/merchant/{merchantId}?page={page}&pageSize={pageSize}");
}

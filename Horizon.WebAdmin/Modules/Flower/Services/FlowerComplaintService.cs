using System.Text.Json;
using Horizon.Share.VMs;
using Microsoft.Extensions.Configuration;

namespace Horizon.WebAdmin.Modules.Flower.Services;

public class FlowerComplaintService : FlowerApiServiceBase
{
    public FlowerComplaintService(HttpClient httpClient, IConfiguration configuration) : base(httpClient, configuration) { }

    public async Task<ResultVM<JsonElement?>?> SubmitComplaintAsync(object data) => await PostAsync<JsonElement?>("FlowerOrderComplaint", data);

    public async Task<ResultVM<JsonElement?>?> GetComplaintAsync(long complaintId) => await GetAsync<JsonElement?>($"FlowerOrderComplaint/{complaintId}");

    public async Task<ResultVM<JsonElement?>?> GetOrderComplaintAsync(long orderId) => await GetAsync<JsonElement?>($"FlowerOrderComplaint/order/{orderId}");

    public async Task<ResultVM<JsonElement?>?> HandleComplaintAsync(long complaintId, object data) => await PostAsync<JsonElement?>($"FlowerOrderComplaint/{complaintId}/handle", data);

    public async Task<ResultVM<JsonElement?>?> GetShopComplaintsAsync(long shopId) => await GetAsync<JsonElement?>($"FlowerOrderComplaint/shop/{shopId}");

    public async Task<ResultVM<JsonElement?>?> GetUserComplaintsAsync(Guid userId) => await GetAsync<JsonElement?>($"FlowerOrderComplaint/user/{userId}");
}

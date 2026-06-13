using System.Text.Json;
using Horizon.Share.VMs;
using Microsoft.Extensions.Configuration;

namespace Horizon.WebAdmin.Modules.Flower.Services;

public class FlowerOrderService : FlowerApiServiceBase
{
    public FlowerOrderService(HttpClient httpClient, IConfiguration configuration) : base(httpClient, configuration) { }

    public async Task<ResultVM<JsonElement?>?> GetOrderAsync(long orderId) => await GetAsync<JsonElement?>($"FlowerOrder/{orderId}");

    public async Task<ResultVM<JsonElement?>?> CreateOrderAsync(object data) => await PostAsync<JsonElement?>("FlowerOrder", data);

    public async Task<ResultVM<JsonElement?>?> CancelOrderAsync(long orderId, object data) => await PostAsync<JsonElement?>($"FlowerOrder/{orderId}/cancel", data);

    public async Task<ResultVM<JsonElement?>?> ConfirmDeliveryAsync(long orderId) => await PostAsync<JsonElement?>($"FlowerOrder/{orderId}/confirm-delivery");

    public async Task<ResultVM<JsonElement?>?> CompleteOrderAsync(long orderId) => await PostAsync<JsonElement?>($"FlowerOrder/{orderId}/complete");

    public async Task<ResultVM<JsonElement?>?> GetMyOrdersAsync(Guid buyerId, int page = 1, int pageSize = 20) => await GetAsync<JsonElement?>($"FlowerOrder/my-orders?buyerId={buyerId}&page={page}&pageSize={pageSize}");

    public async Task<ResultVM<JsonElement?>?> GetMerchantOrdersAsync(long merchantId, int? status = null, int page = 1, int pageSize = 20)
    {
        var url = $"FlowerAdmin/orders?page={page}&pageSize={pageSize}";
        if (status.HasValue) url += $"&status={status}";
        return await GetAsync<JsonElement?>(url);
    }

    public async Task<ResultVM<JsonElement?>?> ShipOrderAsync(long orderId, object data) => await PostAsync<JsonElement?>($"FlowerOrder/{orderId}/ship", data);

    public async Task<ResultVM<JsonElement?>?> RequestRefundAsync(long orderId, object data) => await PostAsync<JsonElement?>($"FlowerOrder/{orderId}/refund", data);

    public async Task<ResultVM<JsonElement?>?> RepurchaseAsync(long orderId) => await PostAsync<JsonElement?>($"FlowerOrder/{orderId}/repurchase");

    public async Task<ResultVM<JsonElement?>?> BatchShipOrdersAsync(object data) => await PostAsync<JsonElement?>("FlowerOrder/batch-ship", data);

    public async Task<ResultVM<JsonElement?>?> GetFrequentProductsAsync(Guid buyerId) => await GetAsync<JsonElement?>($"FlowerOrder/frequent-products/{buyerId}");
}

using System.Text.Json;
using Horizon.Share.VMs;
using Microsoft.Extensions.Configuration;

namespace Horizon.WebAdmin.Modules.Flower.Services;

public class FlowerPaymentService : FlowerApiServiceBase
{
    public FlowerPaymentService(HttpClient httpClient, IConfiguration configuration) : base(httpClient, configuration) { }

    public async Task<ResultVM<JsonElement?>?> GetPaymentListAsync() => await GetAsync<JsonElement?>("FlowerAdmin/payments");

    public async Task<ResultVM<JsonElement?>?> GetRefundListAsync() => await GetAsync<JsonElement?>("FlowerAdmin/refunds");

    public async Task<ResultVM<JsonElement?>?> InitiatePaymentAsync(object data) => await PostAsync<JsonElement?>("FlowerPayment/initiate", data);

    public async Task<ResultVM<JsonElement?>?> QueryPaymentStatusAsync(long transactionId) => await GetAsync<JsonElement?>($"FlowerPayment/{transactionId}/status");

    public async Task<ResultVM<JsonElement?>?> RefundAsync(long transactionId, object data) => await PostAsync<JsonElement?>($"FlowerPayment/{transactionId}/refund", data);
}

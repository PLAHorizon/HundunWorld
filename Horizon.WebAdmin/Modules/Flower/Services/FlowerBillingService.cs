using System.Text.Json;
using Horizon.Share.VMs;
using Microsoft.Extensions.Configuration;

namespace Horizon.WebAdmin.Modules.Flower.Services;

public class FlowerBillingService : FlowerApiServiceBase
{
    public FlowerBillingService(HttpClient httpClient, IConfiguration configuration) : base(httpClient, configuration) { }

    public async Task<ResultVM<JsonElement?>?> GetPendingSettlementsAsync(long shopId) => await GetAsync<JsonElement?>($"FlowerShopBilling/pending/{shopId}");

    public async Task<ResultVM<JsonElement?>?> SettleAsync(long shopId, object data) => await PostAsync<JsonElement?>($"FlowerShopBilling/settle/{shopId}", data);

    public async Task<ResultVM<JsonElement?>?> RequestWithdrawAsync(object data) => await PostAsync<JsonElement?>("FlowerShopBilling/withdraw", data);

    public async Task<ResultVM<JsonElement?>?> AuditWithdrawAsync(long withdrawId, object data) => await PostAsync<JsonElement?>($"FlowerShopBilling/withdraw/{withdrawId}/audit", data);

    public async Task<ResultVM<JsonElement?>?> GetShopAccountItemsAsync(long shopId) => await GetAsync<JsonElement?>($"FlowerShopBilling/account-items/{shopId}");
}

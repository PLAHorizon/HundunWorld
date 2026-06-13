using System.Text.Json;
using Horizon.Share.VMs;
using Microsoft.Extensions.Configuration;

namespace Horizon.WebAdmin.Modules.Flower.Services;

public class FlowerSettlementService : FlowerApiServiceBase
{
    public FlowerSettlementService(HttpClient httpClient, IConfiguration configuration) : base(httpClient, configuration) { }

    public async Task<ResultVM<JsonElement?>?> GetSettlementAsync(long settlementId) => await GetAsync<JsonElement?>($"FlowerSettlement/{settlementId}");

    public async Task<ResultVM<JsonElement?>?> CreateSettlementAsync(object data) => await PostAsync<JsonElement?>("FlowerSettlement/create", data);

    public async Task<ResultVM<JsonElement?>?> CompleteSettlementAsync(long settlementId) => await PostAsync<JsonElement?>($"FlowerSettlement/{settlementId}/complete");

    public async Task<ResultVM<JsonElement?>?> GetSettlementAccountAsync(long merchantId) => await GetAsync<JsonElement?>($"FlowerSettlement/{merchantId}/account");

    public async Task<ResultVM<JsonElement?>?> SaveSettlementAccountAsync(long merchantId, object data) => await PutAsync<JsonElement?>($"FlowerSettlement/{merchantId}/account", data);

    public async Task<ResultVM<JsonElement?>?> GetSettlementBillsAsync(long merchantId, int pageNo = 1, int pageSize = 20) => await GetAsync<JsonElement?>($"FlowerSettlement/{merchantId}/bills?pageNo={pageNo}&pageSize={pageSize}");

    public async Task<ResultVM<JsonElement?>?> RequestWithdrawAsync(object data) => await PostAsync<JsonElement?>("FlowerSettlement/withdraw", data);

    public async Task<ResultVM<JsonElement?>?> GetSettlementDetailsAsync(long settlementBillId) => await GetAsync<JsonElement?>($"FlowerSettlement/{settlementBillId}/details");

    public async Task<ResultVM<JsonElement?>?> GetAccountSummaryAsync(long merchantId) => await GetAsync<JsonElement?>($"FlowerSettlement/account/{merchantId}/summary");
}

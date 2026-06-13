using System.Text.Json;
using Horizon.Share.VMs;
using Microsoft.Extensions.Configuration;

namespace Horizon.WebAdmin.Modules.Flower.Services;

public class FlowerCashDepositService : FlowerApiServiceBase
{
    public FlowerCashDepositService(HttpClient httpClient, IConfiguration configuration) : base(httpClient, configuration) { }

    public async Task<ResultVM<JsonElement?>?> GetCashDepositAsync(long depositId) => await GetAsync<JsonElement?>($"FlowerCashDeposit/{depositId}");

    public async Task<ResultVM<JsonElement?>?> GetShopCashDepositsAsync(long shopId) => await GetAsync<JsonElement?>($"FlowerCashDeposit/shop/{shopId}");

    public async Task<ResultVM<JsonElement?>?> PayCashDepositAsync(object data) => await PostAsync<JsonElement?>("FlowerCashDeposit/pay", data);

    public async Task<ResultVM<JsonElement?>?> DeductCashDepositAsync(long depositId, object data) => await PostAsync<JsonElement?>($"FlowerCashDeposit/{depositId}/deduct", data);
}

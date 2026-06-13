using System.Text.Json;
using Horizon.Share.VMs;
using Microsoft.Extensions.Configuration;

namespace Horizon.WebAdmin.Modules.Flower.Services;

public class FlowerSubscriptionService : FlowerApiServiceBase
{
    public FlowerSubscriptionService(HttpClient httpClient, IConfiguration configuration) : base(httpClient, configuration) { }

    public async Task<ResultVM<JsonElement?>?> GetSubscriptionsAsync(string? passportId = null)
    {
        var url = "FlowerSubscription/subscriptions";
        if (!string.IsNullOrEmpty(passportId)) url += $"?passportId={passportId}";
        return await GetAsync<JsonElement?>(url);
    }

    public async Task<ResultVM<JsonElement?>?> CreateSubscriptionAsync(object data, string? passportId = null)
    {
        var url = "FlowerSubscription/subscriptions";
        if (!string.IsNullOrEmpty(passportId)) url += $"?passportId={passportId}";
        return await PostAsync<JsonElement?>(url, data);
    }

    public async Task<ResultVM<JsonElement?>?> CancelSubscriptionAsync(long id, string? passportId = null)
    {
        var url = $"FlowerSubscription/subscriptions/{id}";
        if (!string.IsNullOrEmpty(passportId)) url += $"?passportId={passportId}";
        return await DeleteAsync<JsonElement?>(url);
    }

    public async Task<ResultVM<JsonElement?>?> GetMySubscriptionAsync(string? passportId = null)
    {
        var url = "FlowerSubscription/my";
        if (!string.IsNullOrEmpty(passportId)) url += $"?passportId={passportId}";
        return await GetAsync<JsonElement?>(url);
    }

    public async Task<ResultVM<JsonElement?>?> UpgradeSubscriptionAsync(object data) => await PostAsync<JsonElement?>("FlowerSubscription/upgrade", data);

    public async Task<ResultVM<JsonElement?>?> GetNotificationSettingsAsync(string? passportId = null)
    {
        var url = "FlowerSubscription/notification-settings";
        if (!string.IsNullOrEmpty(passportId)) url += $"?passportId={passportId}";
        return await GetAsync<JsonElement?>(url);
    }

    public async Task<ResultVM<JsonElement?>?> UpdateNotificationSettingsAsync(object data) => await PutAsync<JsonElement?>("FlowerSubscription/notification-settings", data);

    public async Task<ResultVM<JsonElement?>?> UpdateAutoRenewAsync(bool autoRenew, string? passportId = null)
    {
        var url = $"FlowerSubscription/auto-renew?autoRenew={autoRenew}";
        if (!string.IsNullOrEmpty(passportId)) url += $"&passportId={passportId}";
        return await PutAsync<JsonElement?>(url);
    }
}

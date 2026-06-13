using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Horizon.Game.GengDi.Core.Services
{
    public class SubscriptionInfo
    {
        public long Id { get; set; }
        public Guid UserId { get; set; }
        public int Level { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public bool AutoRenew { get; set; }
        public string PaymentMethod { get; set; } = "";
    }

    public class NotificationSettingsInfo
    {
        public bool IsWebSocketEnabled { get; set; } = true;
        public bool IsSmsEnabled { get; set; }
        public bool IsWeChatEnabled { get; set; }
        public bool IsEmailEnabled { get; set; } = true;
        public System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<int>> SpeciesWatchlist { get; set; } = new();
    }

    public class NotificationSettingsRequest
    {
        public bool IsWebSocketEnabled { get; set; }
        public bool IsSmsEnabled { get; set; }
        public bool IsWeChatEnabled { get; set; }
        public bool IsEmailEnabled { get; set; }
        public System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<int>> SpeciesWatchlist { get; set; } = new();
    }

    public class FlowerSubscriptionService
    {
        public async Task<SubscriptionInfo?> GetSubscriptionInfoAsync()
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var passportId = AccountService.GetPassportId();
                var response = await FlowerHttpConfig.HttpClient.GetAsync($"{baseUri}FlowerSubscription/my?passportId={Uri.EscapeDataString(passportId)}").ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;

                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<SubscriptionInfo>>(json, FlowerHttpConfig.JsonOptions);
                return result?.IsSuccess == true ? result.Data : null;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerSubscriptionService] {nameof(GetSubscriptionInfoAsync)}: {ex.Message}"); return null; }
        }

        public async Task<SubscriptionInfo?> UpgradeSubscriptionAsync(string planType)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var passportId = AccountService.GetPassportId();
                var level = planType switch
                {
                    "Pro" => 1,
                    "Enterprise" => 2,
                    _ => 0
                };
                var body = JsonSerializer.Serialize(new { PassportId = passportId, NewLevel = level, PaymentMethod = "" }, FlowerHttpConfig.JsonOptions);
                var content = new StringContent(body, Encoding.UTF8, "application/json");
                var response = await FlowerHttpConfig.HttpClient.PostAsync($"{baseUri}FlowerSubscription/upgrade", content).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;

                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<SubscriptionInfo>>(json, FlowerHttpConfig.JsonOptions);
                return result?.IsSuccess == true ? result.Data : null;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerSubscriptionService] {nameof(UpgradeSubscriptionAsync)}: {ex.Message}"); return null; }
        }

        public async Task<NotificationSettingsInfo?> GetNotificationSettingsAsync()
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var passportId = AccountService.GetPassportId();
                var response = await FlowerHttpConfig.HttpClient.GetAsync($"{baseUri}FlowerSubscription/notification-settings?passportId={Uri.EscapeDataString(passportId)}").ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;

                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<NotificationSettingsInfo>>(json, FlowerHttpConfig.JsonOptions);
                return result?.IsSuccess == true ? result.Data : null;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerSubscriptionService] {nameof(GetNotificationSettingsAsync)}: {ex.Message}"); return null; }
        }

        public async Task<bool> UpdateNotificationSettingsAsync(NotificationSettingsRequest settings)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var body = JsonSerializer.Serialize(settings, FlowerHttpConfig.JsonOptions);
                var content = new StringContent(body, Encoding.UTF8, "application/json");
                var response = await FlowerHttpConfig.HttpClient.PutAsync($"{baseUri}FlowerSubscription/notification-settings", content).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return false;

                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<bool>>(json, FlowerHttpConfig.JsonOptions);
                return result?.IsSuccess == true;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerSubscriptionService] {nameof(UpdateNotificationSettingsAsync)}: {ex.Message}"); return false; }
        }

        public async Task<bool> UpdateAutoRenewAsync(bool autoRenew)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var passportId = AccountService.GetPassportId();
                var response = await FlowerHttpConfig.HttpClient.PutAsync($"{baseUri}FlowerSubscription/auto-renew?autoRenew={autoRenew}&passportId={Uri.EscapeDataString(passportId)}", null).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return false;

                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<bool>>(json, FlowerHttpConfig.JsonOptions);
                return result?.IsSuccess == true;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerSubscriptionService] {nameof(UpdateAutoRenewAsync)}: {ex.Message}"); return false; }
        }
    }
}

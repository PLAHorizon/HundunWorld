using Horizon.Game.Message.Network;
using Horizon.Orleans.Interface;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans;
using Orleans.Runtime;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Horizon.Orleans.Grains
{
    public class SmsConfig
    {
        public string AccessKeyId { get; set; } = "";
        public string AccessKeySecret { get; set; } = "";
        public string SignName { get; set; } = "";
        public string TemplateCode { get; set; } = "";
        public string PhoneNumber { get; set; } = "";
        public string RegionId { get; set; } = "cn-hangzhou";
    }

    public class WeChatConfig
    {
        public string AppId { get; set; } = "";
        public string AppSecret { get; set; } = "";
        public string TemplateId { get; set; } = "";
        public string OpenId { get; set; } = "";
    }

    public class FlowerNotificationGrain : Grain, INotificationGrain
    {
        private readonly ILogger<FlowerNotificationGrain> _logger;
        private readonly IPersistentState<NotificationState> _notificationState;
        private readonly SmsConfig _smsConfig;
        private readonly WeChatConfig _wechatConfig;
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        private static string? _cachedAccessToken;
        private static DateTime _accessTokenExpireTime = DateTime.MinValue;

        public FlowerNotificationGrain(
            ILogger<FlowerNotificationGrain> logger,
            [PersistentState("notification", "FlowerStore")] IPersistentState<NotificationState> notificationState,
            IOptions<SmsConfig> smsConfig,
            IOptions<WeChatConfig> wechatConfig)
        {
            _logger = logger;
            _notificationState = notificationState;
            _smsConfig = smsConfig?.Value ?? new SmsConfig();
            _wechatConfig = wechatConfig?.Value ?? new WeChatConfig();
        }

        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("FlowerNotificationGrain {GrainKey} activating.", this.GetPrimaryKey());

            var userId = this.GetPrimaryKey();
            if (_notificationState.State.Subscriptions == null)
                _notificationState.State.Subscriptions = new Dictionary<int, List<NotifyChannel>>();
            if (_notificationState.State.PendingAlerts == null)
                _notificationState.State.PendingAlerts = new List<AlertMessage>();
            if (_notificationState.State.LastRuleTriggerTime == null)
                _notificationState.State.LastRuleTriggerTime = new Dictionary<long, DateTime>();
            if (_notificationState.State.UserId == Guid.Empty)
                _notificationState.State.UserId = userId;

            await base.OnActivateAsync(cancellationToken);
        }

        public async Task SubscribeAsync(int speciesId, NotifyChannel channel)
        {
            try
            {
                var state = _notificationState.State;

                if (!state.Subscriptions.ContainsKey(speciesId))
                    state.Subscriptions[speciesId] = new List<NotifyChannel>();

                if (!state.Subscriptions[speciesId].Contains(channel))
                    state.Subscriptions[speciesId].Add(channel);

                await _notificationState.WriteStateAsync();

                _logger.LogInformation("订阅品种通知: UserId={UserId}, SpeciesId={SpeciesId}, Channel={Channel}",
                    state.UserId, speciesId, channel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "订阅品种通知失败: UserId={UserId}, SpeciesId={SpeciesId}", this.GetPrimaryKey(), speciesId);
                throw;
            }
        }

        public async Task UnsubscribeAsync(int speciesId, NotifyChannel channel)
        {
            try
            {
                var state = _notificationState.State;

                if (state.Subscriptions.TryGetValue(speciesId, out var channels))
                {
                    channels.Remove(channel);

                    if (channels.Count == 0)
                        state.Subscriptions.Remove(speciesId);
                }

                await _notificationState.WriteStateAsync();

                _logger.LogInformation("取消订阅: UserId={UserId}, SpeciesId={SpeciesId}, Channel={Channel}",
                    state.UserId, speciesId, channel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "取消订阅失败: UserId={UserId}, SpeciesId={SpeciesId}", this.GetPrimaryKey(), speciesId);
                throw;
            }
        }

        public async Task PushAlertAsync(AlertMessage alert)
        {
            try
            {
                if (alert == null)
                {
                    _logger.LogWarning("推送预警消息无效: alert is null");
                    return;
                }

                var state = _notificationState.State;

                if (IsInSilencePeriod(state, alert.RuleId))
                {
                    _logger.LogInformation("预警静默期内跳过推送: UserId={UserId}, RuleId={RuleId}",
                        state.UserId, alert.RuleId);
                    return;
                }

                state.PendingAlerts.Add(alert);
                state.LastPushTime = DateTime.Now;
                state.LastRuleTriggerTime[alert.RuleId] = DateTime.Now;

                await _notificationState.WriteStateAsync();

                if (state.Subscriptions.TryGetValue((int)alert.SpeciesId, out var channels))
                {
                    foreach (var channel in channels)
                    {
                        switch (channel)
                        {
                            case NotifyChannel.SMS:
                                await PushSmsAsync(alert);
                                break;
                            case NotifyChannel.WeChat:
                                await PushWeChatAsync(alert);
                                break;
                            case NotifyChannel.WebSocket:
                            case NotifyChannel.Email:
                            default:
                                break;
                        }
                    }
                }

                _logger.LogInformation("推送预警消息: UserId={UserId}, RuleId={RuleId}, SpeciesId={SpeciesId}",
                    state.UserId, alert.RuleId, alert.SpeciesId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "推送预警消息失败: UserId={UserId}", this.GetPrimaryKey());
                throw;
            }
        }

        public async Task MarkAlertsAsReadAsync(List<long> alertRuleIds)
        {
            try
            {
                var state = _notificationState.State;
                var ruleIdSet = new HashSet<long>(alertRuleIds);

                foreach (var alert in state.PendingAlerts.Where(a => ruleIdSet.Contains(a.RuleId) && !a.IsRead))
                {
                    alert.IsRead = true;
                }

                await _notificationState.WriteStateAsync();

                _logger.LogInformation("标记预警已读: UserId={UserId}, Count={Count}", state.UserId, alertRuleIds.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "标记预警已读失败: UserId={UserId}", this.GetPrimaryKey());
                throw;
            }
        }

        public async Task MarkAllAlertsAsReadAsync()
        {
            try
            {
                var state = _notificationState.State;

                foreach (var alert in state.PendingAlerts.Where(a => !a.IsRead))
                {
                    alert.IsRead = true;
                }

                await _notificationState.WriteStateAsync();

                _logger.LogInformation("标记全部预警已读: UserId={UserId}", state.UserId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "标记全部预警已读失败: UserId={UserId}", this.GetPrimaryKey());
                throw;
            }
        }

        public async Task SetSilencePeriodAsync(int minutes)
        {
            try
            {
                _notificationState.State.SilencePeriodMinutes = Math.Max(0, minutes);
                await _notificationState.WriteStateAsync();

                _logger.LogInformation("设置静默期: UserId={UserId}, Minutes={Minutes}", this.GetPrimaryKey(), minutes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "设置静默期失败: UserId={UserId}", this.GetPrimaryKey());
                throw;
            }
        }

        public Task<NotificationChannelSettings> GetChannelSettingsAsync()
        {
            try
            {
                var state = _notificationState.State;
                var settings = new NotificationChannelSettings
                {
                    IsWebSocketEnabled = !state.EnabledChannels.ContainsKey((int)NotifyChannel.WebSocket) || state.EnabledChannels[(int)NotifyChannel.WebSocket],
                    IsSmsEnabled = state.EnabledChannels.ContainsKey((int)NotifyChannel.SMS) && state.EnabledChannels[(int)NotifyChannel.SMS],
                    IsWeChatEnabled = state.EnabledChannels.ContainsKey((int)NotifyChannel.WeChat) && state.EnabledChannels[(int)NotifyChannel.WeChat],
                    IsEmailEnabled = !state.EnabledChannels.ContainsKey((int)NotifyChannel.Email) || state.EnabledChannels[(int)NotifyChannel.Email],
                    SpeciesWatchlist = state.Subscriptions.ToDictionary(
                        kvp => kvp.Key,
                        kvp => kvp.Value.Select(c => (int)c).ToList())
                };

                return Task.FromResult(settings);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取通知渠道设置失败: UserId={UserId}", this.GetPrimaryKey());
                throw;
            }
        }

        public async Task SetChannelSettingsAsync(NotificationChannelSettings settings)
        {
            try
            {
                var state = _notificationState.State;

                state.EnabledChannels[(int)NotifyChannel.WebSocket] = settings.IsWebSocketEnabled;
                state.EnabledChannels[(int)NotifyChannel.SMS] = settings.IsSmsEnabled;
                state.EnabledChannels[(int)NotifyChannel.WeChat] = settings.IsWeChatEnabled;
                state.EnabledChannels[(int)NotifyChannel.Email] = settings.IsEmailEnabled;

                if (settings.SpeciesWatchlist != null)
                {
                    state.Subscriptions.Clear();
                    foreach (var kvp in settings.SpeciesWatchlist)
                    {
                        state.Subscriptions[kvp.Key] = kvp.Value.Select(v => (NotifyChannel)v).ToList();
                    }
                }

                await _notificationState.WriteStateAsync();

                _logger.LogInformation("更新通知渠道设置: UserId={UserId}", this.GetPrimaryKey());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新通知渠道设置失败: UserId={UserId}", this.GetPrimaryKey());
                throw;
            }
        }

        public Task<List<AlertMessage>> GetPendingAlertsAsync()
        {
            try
            {
                var state = _notificationState.State;
                return Task.FromResult(state.PendingAlerts.ToList());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取待处理预警失败: UserId={UserId}", this.GetPrimaryKey());
                throw;
            }
        }

        private bool IsInSilencePeriod(NotificationState state, long ruleId)
        {
            if (state.SilencePeriodMinutes <= 0)
                return false;

            if (!state.LastRuleTriggerTime.TryGetValue(ruleId, out var lastTrigger))
                return false;

            return (DateTime.Now - lastTrigger).TotalMinutes < state.SilencePeriodMinutes;
        }

        private async Task PushSmsAsync(AlertMessage alert)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_smsConfig.AccessKeyId) ||
                    string.IsNullOrWhiteSpace(_smsConfig.AccessKeySecret) ||
                    string.IsNullOrWhiteSpace(_smsConfig.SignName) ||
                    string.IsNullOrWhiteSpace(_smsConfig.TemplateCode) ||
                    string.IsNullOrWhiteSpace(_smsConfig.PhoneNumber))
                {
                    _logger.LogWarning("SMS配置缺失，跳过短信推送: UserId={UserId}", alert.UserId);
                    return;
                }

                var templateParam = JsonSerializer.Serialize(new
                {
                    species = alert.SpeciesId.ToString(),
                    message = alert.Message,
                    value = alert.TriggeredValue.ToString(CultureInfo.InvariantCulture),
                    threshold = alert.ThresholdValue.ToString(CultureInfo.InvariantCulture)
                });

                var parameters = BuildAliyunSmsParameters(templateParam);
                var signature = ComputeHmacSha1(parameters, _smsConfig.AccessKeySecret);
                parameters["Signature"] = signature;

                var queryString = string.Join("&", parameters
                    .OrderBy(p => p.Key, StringComparer.Ordinal)
                    .Select(p => $"{PercentEncode(p.Key)}={PercentEncode(p.Value)}"));

                var requestUrl = $"https://dysmsapi.aliyuncs.com/?{queryString}";
                var response = await _httpClient.GetAsync(requestUrl);

                var responseBody = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var result = JsonSerializer.Deserialize<JsonElement>(responseBody);
                    if (result.TryGetProperty("Code", out var code) && code.GetString() == "OK")
                    {
                        _logger.LogInformation("SMS推送成功: UserId={UserId}, SpeciesId={SpeciesId}, Phone={Phone}",
                            alert.UserId, alert.SpeciesId, _smsConfig.PhoneNumber);
                    }
                    else
                    {
                        var errorMsg = result.TryGetProperty("Message", out var msg) ? msg.GetString() : "未知错误";
                        _logger.LogWarning("SMS推送返回错误: UserId={UserId}, Code={Code}, Message={Message}",
                            alert.UserId, code.GetString(), errorMsg);
                    }
                }
                else
                {
                    _logger.LogWarning("SMS推送HTTP请求失败: UserId={UserId}, StatusCode={StatusCode}, Body={Body}",
                        alert.UserId, (int)response.StatusCode, responseBody);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SMS推送异常，回退到日志输出: UserId={UserId}, SpeciesId={SpeciesId}, Message={Message}",
                    alert.UserId, alert.SpeciesId, alert.Message);
            }
        }

        private async Task PushWeChatAsync(AlertMessage alert)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_wechatConfig.AppId) ||
                    string.IsNullOrWhiteSpace(_wechatConfig.AppSecret) ||
                    string.IsNullOrWhiteSpace(_wechatConfig.TemplateId) ||
                    string.IsNullOrWhiteSpace(_wechatConfig.OpenId))
                {
                    _logger.LogWarning("微信配置缺失，跳过微信推送: UserId={UserId}", alert.UserId);
                    return;
                }

                var accessToken = await GetWeChatAccessTokenAsync();
                if (string.IsNullOrWhiteSpace(accessToken))
                {
                    _logger.LogWarning("获取微信access_token失败，跳过微信推送: UserId={UserId}", alert.UserId);
                    return;
                }

                var templateData = new
                {
                    touser = _wechatConfig.OpenId,
                    template_id = _wechatConfig.TemplateId,
                    data = new
                    {
                        first = new { value = "花卉价格预警通知", color = "#FF0000" },
                        keyword1 = new { value = alert.SpeciesId.ToString(), color = "#173177" },
                        keyword2 = new { value = alert.AlertType.ToString(), color = "#173177" },
                        keyword3 = new { value = alert.Message, color = "#173177" },
                        keyword4 = new { value = alert.TriggeredValue.ToString(CultureInfo.InvariantCulture), color = "#173177" },
                        keyword5 = new { value = alert.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"), color = "#173177" },
                        remark = new { value = $"阈值: {alert.ThresholdValue}", color = "#666666" }
                    }
                };

                var json = JsonSerializer.Serialize(templateData);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var sendUrl = $"https://api.weixin.qq.com/cgi-bin/message/template/send?access_token={accessToken}";
                var response = await _httpClient.PostAsync(sendUrl, content);

                var responseBody = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var result = JsonSerializer.Deserialize<JsonElement>(responseBody);
                    if (result.TryGetProperty("errcode", out var errcode) && errcode.GetInt32() == 0)
                    {
                        _logger.LogInformation("微信推送成功: UserId={UserId}, SpeciesId={SpeciesId}, OpenId={OpenId}",
                            alert.UserId, alert.SpeciesId, _wechatConfig.OpenId);
                    }
                    else
                    {
                        var errMsg = result.TryGetProperty("errmsg", out var msg) ? msg.GetString() : "未知错误";
                        _logger.LogWarning("微信推送返回错误: UserId={UserId}, ErrCode={ErrCode}, ErrMsg={ErrMsg}",
                            alert.UserId, errcode.GetInt32(), errMsg);
                    }
                }
                else
                {
                    _logger.LogWarning("微信推送HTTP请求失败: UserId={UserId}, StatusCode={StatusCode}, Body={Body}",
                        alert.UserId, (int)response.StatusCode, responseBody);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "微信推送异常，回退到日志输出: UserId={UserId}, SpeciesId={SpeciesId}, Message={Message}",
                    alert.UserId, alert.SpeciesId, alert.Message);
            }
        }

        private async Task<string?> GetWeChatAccessTokenAsync()
        {
            if (_cachedAccessToken != null && DateTime.Now < _accessTokenExpireTime)
                return _cachedAccessToken;

            try
            {
                var tokenUrl = $"https://api.weixin.qq.com/cgi-bin/token?grant_type=client_credential&appid={_wechatConfig.AppId}&secret={_wechatConfig.AppSecret}";
                var response = await _httpClient.GetAsync(tokenUrl);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var result = JsonSerializer.Deserialize<JsonElement>(responseBody);
                    if (result.TryGetProperty("access_token", out var token))
                    {
                        _cachedAccessToken = token.GetString();
                        var expiresIn = result.TryGetProperty("expires_in", out var exp) ? exp.GetInt32() : 7200;
                        _accessTokenExpireTime = DateTime.Now.AddSeconds(Math.Max(expiresIn - 300, 0));
                        return _cachedAccessToken;
                    }

                    var errMsg = result.TryGetProperty("errmsg", out var msg) ? msg.GetString() : "未知错误";
                    _logger.LogWarning("获取微信access_token失败: {ErrMsg}", errMsg);
                }
                else
                {
                    _logger.LogWarning("获取微信access_token HTTP请求失败: StatusCode={StatusCode}", (int)response.StatusCode);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取微信access_token异常");
            }

            return null;
        }

        private Dictionary<string, string> BuildAliyunSmsParameters(string templateParam)
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
            var nonce = Guid.NewGuid().ToString();

            return new Dictionary<string, string>
            {
                ["AccessKeyId"] = _smsConfig.AccessKeyId,
                ["Action"] = "SendSms",
                ["Format"] = "JSON",
                ["PhoneNumbers"] = _smsConfig.PhoneNumber,
                ["RegionId"] = _smsConfig.RegionId,
                ["SignName"] = _smsConfig.SignName,
                ["SignatureMethod"] = "HMAC-SHA1",
                ["SignatureNonce"] = nonce,
                ["SignatureVersion"] = "1.0",
                ["TemplateCode"] = _smsConfig.TemplateCode,
                ["TemplateParam"] = templateParam,
                ["Timestamp"] = timestamp,
                ["Version"] = "2017-05-25"
            };
        }

        private static string ComputeHmacSha1(Dictionary<string, string> parameters, string accessKeySecret)
        {
            var sortedParams = parameters
                .OrderBy(p => p.Key, StringComparer.Ordinal)
                .Select(p => $"{PercentEncode(p.Key)}={PercentEncode(p.Value)}");

            var queryString = string.Join("&", sortedParams);
            var stringToSign = $"GET&{PercentEncode("/")}&{PercentEncode(queryString)}";

            using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(accessKeySecret + "&"));
            var hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign));
            return Convert.ToBase64String(hashBytes);
        }

        private static string PercentEncode(string value)
        {
            return Uri.EscapeDataString(value)
                .Replace("+", "%20")
                .Replace("*", "%2A")
                .Replace("%7E", "~");
        }
    }
}

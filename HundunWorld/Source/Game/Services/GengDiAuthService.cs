using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using FlaxEngine;
using HundunWorld.Game.Network;

namespace HundunWorld.Game.Services
{
    public class GengDiAuthService
    {
        private static GengDiAuthService _instance;
        public static GengDiAuthService Instance => _instance ??= new GengDiAuthService();

        private static string _accessToken;
        private static string _refreshToken;
        private static string _imAuthToken;
        private static long _expiresIn;
        private static string _passportId;
        private static readonly object _lock = new object();

        private static readonly HttpClient _httpClient;
        private static readonly string _webApiBaseUrl;

        static GengDiAuthService()
        {
            _webApiBaseUrl = ResolveWebApiBaseUrl();
            _httpClient = CreateHttpClient();
        }

        public async Task<GengDiLoginResult> LoginAsync(string passportId, string password)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(passportId) || string.IsNullOrWhiteSpace(password))
                {
                    return new GengDiLoginResult
                    {
                        IsSuccess = false,
                        ErrorMessage = "通行证ID和密码不能为空"
                    };
                }

                var machineId = MachineIdentifier.GetMachineGuid();

                var requestBody = new
                {
                    PassportId = passportId.Trim(),
                    Password = password,
                    VerifyCode = "",
                    Phone = "",
                    Email = "",
                    AppId = 1,
                    AppType = 0,
                    PassportType = 0,
                    MachineId = machineId
                };

                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var requestUrl = $"{_webApiBaseUrl}Account/signin";
                Debug.Log($"[GengDiAuth] 正在登录: {requestUrl}");

                var response = await _httpClient.PostAsync(requestUrl, content);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    Debug.LogWarning($"[GengDiAuth] 登录请求失败: {(int)response.StatusCode} {response.ReasonPhrase}");
                    return new GengDiLoginResult
                    {
                        IsSuccess = false,
                        ErrorMessage = $"登录失败: {(int)response.StatusCode} {response.ReasonPhrase}"
                    };
                }

                var envelope = JsonSerializer.Deserialize<ApiEnvelope>(responseBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (envelope == null || !envelope.IsSuccess || envelope.Data == null)
                {
                    var errorMsg = envelope?.ErrorMessage ?? "登录失败，服务端未返回有效登录信息";
                    Debug.LogWarning($"[GengDiAuth] {errorMsg}");
                    return new GengDiLoginResult
                    {
                        IsSuccess = false,
                        ErrorMessage = errorMsg
                    };
                }

                var data = envelope.Data;
                if (string.IsNullOrWhiteSpace(data.AccessToken))
                {
                    Debug.LogWarning("[GengDiAuth] 登录失败: 服务端未返回有效的AccessToken");
                    return new GengDiLoginResult
                    {
                        IsSuccess = false,
                        ErrorMessage = "登录失败，服务端未返回有效的AccessToken"
                    };
                }

                lock (_lock)
                {
                    _accessToken = data.AccessToken;
                    _refreshToken = data.RefreshToken ?? string.Empty;
                    _imAuthToken = data.ImAuthToken ?? string.Empty;
                    _expiresIn = data.ExpiresIn;
                    _passportId = passportId.Trim();
                }

                Debug.Log("[GengDiAuth] 登录成功");

                return new GengDiLoginResult
                {
                    IsSuccess = true,
                    ErrorMessage = string.Empty,
                    AccessToken = data.AccessToken,
                    RefreshToken = data.RefreshToken ?? string.Empty,
                    ImAuthToken = data.ImAuthToken ?? string.Empty,
                    ExpiresIn = data.ExpiresIn
                };
            }
            catch (HttpRequestException ex)
            {
                Debug.LogError($"[GengDiAuth] 网络请求异常: {ex.Message}");
                return new GengDiLoginResult
                {
                    IsSuccess = false,
                    ErrorMessage = $"网络请求失败: {ex.Message}"
                };
            }
            catch (TaskCanceledException)
            {
                Debug.LogError("[GengDiAuth] 登录请求超时");
                return new GengDiLoginResult
                {
                    IsSuccess = false,
                    ErrorMessage = "登录请求超时，请检查网络连接"
                };
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GengDiAuth] 登录异常: {ex.Message}");
                return new GengDiLoginResult
                {
                    IsSuccess = false,
                    ErrorMessage = $"登录失败: {ex.Message}"
                };
            }
        }

        public async Task<bool> RefreshTokenAsync()
        {
            string refreshToken;
            string expiredImAuthToken;
            lock (_lock)
            {
                refreshToken = _refreshToken;
                expiredImAuthToken = _imAuthToken;
            }

            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                Debug.LogWarning("[GengDiAuth] 刷新Token失败: 没有可用的RefreshToken");
                return false;
            }

            try
            {
                // 将过期的ImAuthToken作为查询参数传递，供服务端提取machineId以签发新令牌
                var requestUrl = $"{_webApiBaseUrl}Account/GetRefreshToken";
                if (!string.IsNullOrWhiteSpace(expiredImAuthToken))
                {
                    requestUrl += $"?expiredImAuthToken={Uri.EscapeDataString(expiredImAuthToken)}";
                }
                var content = new StringContent(refreshToken, Encoding.UTF8, "application/json");

                Debug.Log($"[GengDiAuth] 正在刷新Token: {requestUrl}");

                var response = await _httpClient.PostAsync(requestUrl, content);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    Debug.LogWarning($"[GengDiAuth] 刷新Token请求失败: {(int)response.StatusCode}");
                    return false;
                }

                var envelope = JsonSerializer.Deserialize<ApiEnvelope>(responseBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (envelope == null || !envelope.IsSuccess || envelope.Data == null || string.IsNullOrWhiteSpace(envelope.Data.AccessToken))
                {
                    Debug.LogWarning($"[GengDiAuth] 刷新Token失败: {envelope?.ErrorMessage ?? "响应无效"}");
                    return false;
                }

                lock (_lock)
                {
                    _accessToken = envelope.Data.AccessToken;
                    _refreshToken = envelope.Data.RefreshToken ?? _refreshToken;
                    _imAuthToken = envelope.Data.ImAuthToken ?? _imAuthToken;
                    _expiresIn = envelope.Data.ExpiresIn;
                }

                Debug.Log("[GengDiAuth] Token刷新成功");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GengDiAuth] 刷新Token异常: {ex.Message}");
                return false;
            }
        }

        public static string GetAccessToken()
        {
            lock (_lock)
            {
                return _accessToken ?? string.Empty;
            }
        }

        public static string GetImAuthToken()
        {
            lock (_lock)
            {
                return _imAuthToken ?? string.Empty;
            }
        }

        public static string GetRefreshToken()
        {
            lock (_lock)
            {
                return _refreshToken ?? string.Empty;
            }
        }

        public static string GetPassportId()
        {
            lock (_lock)
            {
                return _passportId ?? string.Empty;
            }
        }

        private static string ResolveWebApiBaseUrl()
        {
            var envBaseUrl = Environment.GetEnvironmentVariable("HUNDUN_WEBAPI_BASE_URL");
            if (!string.IsNullOrWhiteSpace(envBaseUrl))
            {
                var url = EnsureTrailingSlash(envBaseUrl);
                Debug.Log($"[GengDiAuth] WebApi基础URL来自环境变量HUNDUN_WEBAPI_BASE_URL: {url}");
                return url;
            }

            var envAuthority = Environment.GetEnvironmentVariable("HUNDUN_WEBAPI_AUTHORITY");
            if (!string.IsNullOrWhiteSpace(envAuthority))
            {
                var url = EnsureTrailingSlash(envAuthority);
                Debug.Log($"[GengDiAuth] WebApi基础URL来自环境变量HUNDUN_WEBAPI_AUTHORITY: {url}");
                return url;
            }

            try
            {
                var config = NetworkConfigManager.LoadConfig();
                if (config?.GatewayList != null && config.GatewayList.Count > 0)
                {
                    var firstGateway = config.GatewayList[0];
                    var url = $"https://{firstGateway.IP}:5101/";
                    Debug.Log($"[GengDiAuth] WebApi基础URL来自network_config.json: {url}");
                    return url;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[GengDiAuth] 从NetworkConfigManager读取配置失败: {ex.Message}");
            }

            var defaultUrl = "https://192.168.1.78:5101/";
            Debug.Log($"[GengDiAuth] WebApi基础URL使用默认值: {defaultUrl}");
            return defaultUrl;
        }

        private static string EnsureTrailingSlash(string url)
        {
            if (!url.EndsWith("/"))
            {
                url += "/";
            }
            return url;
        }

        private static HttpClient CreateHttpClient()
        {
            var handler = new HttpClientHandler();

            bool bypassSsl = false;
#if DEBUG
            bypassSsl = true;
#endif
            var forceBypass = Environment.GetEnvironmentVariable("HUNDUN_FORCE_BYPASS_SSL");
            if (string.Equals(forceBypass, "true", StringComparison.OrdinalIgnoreCase))
            {
                bypassSsl = true;
            }

            if (bypassSsl)
            {
                handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
                Debug.Log("[GengDiAuth] 已启用SSL证书验证跳过（开发模式）");
            }

            var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(60)
            };

            return client;
        }

        private class ApiEnvelope
        {
            [JsonPropertyName("isSuccess")]
            public bool IsSuccess { get; set; }

            [JsonPropertyName("errorMessage")]
            public string ErrorMessage { get; set; }

            [JsonPropertyName("data")]
            public LoginData Data { get; set; }
        }

        private class LoginData
        {
            [JsonPropertyName("accessToken")]
            public string AccessToken { get; set; }

            [JsonPropertyName("refreshToken")]
            public string RefreshToken { get; set; }

            [JsonPropertyName("imAuthToken")]
            public string ImAuthToken { get; set; }

            [JsonPropertyName("expiresIn")]
            public long ExpiresIn { get; set; }
        }

    }

    public class GengDiLoginResult
    {
        public bool IsSuccess { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public string AccessToken { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public string ImAuthToken { get; set; } = string.Empty;
        public long ExpiresIn { get; set; }
    }
}

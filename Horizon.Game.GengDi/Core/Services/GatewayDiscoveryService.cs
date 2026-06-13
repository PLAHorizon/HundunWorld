using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;

namespace Horizon.Game.GengDi.Core.Services
{
    /// <summary>
    /// 网关发现服务。
    /// GengDi 客户端启动后通过 WebApi <c>/gateway</c> 接口获取当前可用的 IM 与 Game 网关，
    /// 结果在内存中缓存；后续 <see cref="GameGatewayClient"/> 与 <see cref="ImGatewayContactClient"/>
    /// 在解析连接地址时会优先使用缓存中的值，失败时回退到配置文件或环境变量或默认值。
    /// </summary>
    internal static class GatewayDiscoveryService
    {
        private static readonly IConfiguration Configuration = LoadConfiguration();
        private static readonly HttpClient HttpClient = CreateHttpClient();

        private static readonly object SyncRoot = new();

        private static volatile GatewayEndpoint? _gameEndpoint;
        private static volatile GatewayEndpoint? _imEndpoint;
        private static DateTime _lastRefreshUtc = DateTime.MinValue;
        private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(2);

        /// <summary>
        /// 当前缓存的 Game 网关端点；若未发现则返回 null。
        /// </summary>
        public static GatewayEndpoint? GameGateway => _gameEndpoint;

        /// <summary>
        /// 当前缓存的 IM 网关端点；若未发现则返回 null。
        /// </summary>
        public static GatewayEndpoint? ImGateway => _imEndpoint;

        /// <summary>
        /// 从 WebApi 拉取最新的网关列表并更新缓存。
        /// 若已在 <see cref="RefreshInterval"/> 内刷新过且不强制刷新，则直接返回缓存。
        /// </summary>
        public static async Task RefreshAsync(bool force = false, CancellationToken cancellationToken = default)
        {
            if (!force)
            {
                lock (SyncRoot)
                {
                    if (DateTime.UtcNow - _lastRefreshUtc < RefreshInterval
                        && (_gameEndpoint != null || _imEndpoint != null))
                    {
                        return;
                    }
                }
            }

            try
            {
                var uri = new Uri(GetBaseUri(), "gateway");
                using var response = await HttpClient.GetAsync(uri, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[GatewayDiscoveryService] WebApi 返回非成功状态: {(int)response.StatusCode}");
                    return;
                }

                var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var payload = JsonConvert.DeserializeObject<GatewayListResponse>(json);
                if (payload == null)
                {
                    return;
                }

                lock (SyncRoot)
                {
                    _gameEndpoint = SelectBest(payload.Game);
                    _imEndpoint = SelectBest(payload.Im);
                    _lastRefreshUtc = DateTime.UtcNow;
                }

                System.Diagnostics.Debug.WriteLine(
                    $"[GatewayDiscoveryService] 刷新完成: Game={_gameEndpoint}, IM={_imEndpoint}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GatewayDiscoveryService] 刷新失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取 Game 网关主机地址，优先级：环境变量 > 配置文件 > 默认值。
        /// </summary>
        public static string GetGameGatewayHost()
        {
            var configured = Environment.GetEnvironmentVariable("HUNDUNWORLD_GAME_GATEWAY_HOST")?.Trim();
            if (!string.IsNullOrWhiteSpace(configured)) return configured;
            return Configuration["Gateway:Game:Host"] ?? "192.168.1.78";
        }

        /// <summary>
        /// 获取 Game 网关端口，优先级：环境变量 > 配置文件 > 默认值。
        /// </summary>
        public static int GetGameGatewayPort()
        {
            var raw = Environment.GetEnvironmentVariable("HUNDUNWORLD_GAME_GATEWAY_PORT");
            if (int.TryParse(raw, out var port) && port is > 0 and <= 65535) return port;
            if (int.TryParse(Configuration["Gateway:Game:Port"], out var cfgPort) && cfgPort is > 0 and <= 65535) return cfgPort;
            return 7789;
        }

        /// <summary>
        /// 获取 IM 网关主机地址，优先级：环境变量 > 配置文件 > 默认值。
        /// </summary>
        public static string GetImGatewayHost()
        {
            var configured = Environment.GetEnvironmentVariable("HUNDUNWORLD_IM_GATEWAY_HOST")?.Trim();
            if (!string.IsNullOrWhiteSpace(configured)) return configured;
            return Configuration["Gateway:IM:Host"] ?? "192.168.1.78";
        }

        /// <summary>
        /// 获取 IM 网关端口，优先级：环境变量 > 配置文件 > 默认值。
        /// </summary>
        public static int GetImGatewayPort()
        {
            var raw = Environment.GetEnvironmentVariable("HUNDUNWORLD_IM_GATEWAY_PORT");
            if (int.TryParse(raw, out var port) && port is > 0 and <= 65535) return port;
            if (int.TryParse(Configuration["Gateway:IM:Port"], out var cfgPort) && cfgPort is > 0 and <= 65535) return cfgPort;
            return 31000;
        }

        /// <summary>
        /// 获取 WebApi 基础地址，优先级：环境变量 > 配置文件 > 默认值。
        /// </summary>
        public static string GetWebApiBaseUrl()
        {
            var configured = Environment.GetEnvironmentVariable("HUNDUN_WEBAPI_BASE_URL");
            if (string.IsNullOrWhiteSpace(configured))
            {
                configured = Environment.GetEnvironmentVariable("HUNDUN_WEBAPI_AUTHORITY");
            }
            if (string.IsNullOrWhiteSpace(configured))
            {
                configured = Configuration["WebApi:BaseUrl"] ?? Configuration["WebApi:Authority"];
            }
            configured = string.IsNullOrWhiteSpace(configured)
                ? "https://192.168.1.78:5101/"
                : configured.Trim();

            if (!configured.EndsWith("/", StringComparison.Ordinal))
            {
                configured += "/";
            }

            return configured;
        }

        private static IConfiguration LoadConfiguration()
        {
            var builder = new ConfigurationBuilder();
            var basePath = AppDomain.CurrentDomain.BaseDirectory;
            builder.SetBasePath(basePath);
            builder.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);

            var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                    ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
            if (!string.IsNullOrWhiteSpace(env))
            {
                builder.AddJsonFile($"appsettings.{env}.json", optional: true, reloadOnChange: false);
            }
            else if (System.Diagnostics.Debugger.IsAttached)
            {
                builder.AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: false);
            }

            return builder.Build();
        }

        private static GatewayEndpoint? SelectBest(IReadOnlyList<GatewayEndpointDto>? candidates)
        {
            if (candidates == null || candidates.Count == 0) return null;

            // 选择最近心跳的实例，保证集群视图下客户端能尽可能连上最新、最活跃的网关。
            var best = candidates
                .Where(c => !string.IsNullOrWhiteSpace(c.Address) && c.Port > 0)
                .OrderByDescending(c => c.LastHeartbeatUtc)
                .FirstOrDefault();

            return best == null
                ? null
                : new GatewayEndpoint(best.Address, best.Port, best.Type ?? string.Empty, best.InstanceId ?? string.Empty);
        }

        private static Uri GetBaseUri()
        {
            return new Uri(GetWebApiBaseUrl(), UriKind.Absolute);
        }

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient(SslConfiguration.CreateTestEnvironmentHandler())
            {
                Timeout = TimeSpan.FromSeconds(10)
            };
            return client;
        }

        /// <summary>网关端点（缓存值）。</summary>
        public sealed class GatewayEndpoint
        {
            public GatewayEndpoint(string host, int port, string type, string instanceId)
            {
                Host = host;
                Port = port;
                Type = type;
                InstanceId = instanceId;
            }

            public string Host { get; }
            public int Port { get; }
            public string Type { get; }
            public string InstanceId { get; }

            public override string ToString() => $"{Type}:{Host}:{Port}({InstanceId})";
        }

        private sealed class GatewayListResponse
        {
            [JsonProperty("game")]
            public List<GatewayEndpointDto>? Game { get; set; }

            [JsonProperty("im")]
            public List<GatewayEndpointDto>? Im { get; set; }
        }

        private sealed class GatewayEndpointDto
        {
            [JsonProperty("instanceId")]
            public string? InstanceId { get; set; }

            [JsonProperty("type")]
            public string? Type { get; set; }

            [JsonProperty("clusterId")]
            public string? ClusterId { get; set; }

            [JsonProperty("address")]
            public string Address { get; set; } = string.Empty;

            [JsonProperty("port")]
            public int Port { get; set; }

            [JsonProperty("region")]
            public string? Region { get; set; }

            [JsonProperty("lastHeartbeatUtc")]
            public DateTime LastHeartbeatUtc { get; set; }
        }
    }
}

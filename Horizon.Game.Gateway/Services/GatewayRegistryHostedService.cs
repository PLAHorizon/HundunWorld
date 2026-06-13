using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Horizon.Game.Gateway.Configuration;
using Horizon.Strategy.Storage.Redis;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans;

namespace Horizon.Game.Gateway.Services
{
    /// <summary>
    /// 网关注册后台服务。
    /// 在 Orleans 客户端连接就绪后，将当前网关实例（IP、端口、Type=Game）写入 Redis；
    /// 并按配置的心跳间隔刷新 TTL，保证集群中多个网关实例的数据一致性。
    /// 网关停止时主动注销。
    /// </summary>
    public class GatewayRegistryHostedService : BackgroundService
    {
        private readonly ILogger<GatewayRegistryHostedService> _logger;
        private readonly IOptionsMonitor<GatewayOptions> _gatewayOptions;
        private readonly IOptionsMonitor<NetworkOptions> _networkOptions;
        private readonly IServiceProvider _serviceProvider;

        private GatewayRegistry? _registry;
        private string _instanceId = string.Empty;

        public GatewayRegistryHostedService(
            ILogger<GatewayRegistryHostedService> logger,
            IOptionsMonitor<GatewayOptions> gatewayOptions,
            IOptionsMonitor<NetworkOptions> networkOptions,
            IServiceProvider serviceProvider)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _gatewayOptions = gatewayOptions ?? throw new ArgumentNullException(nameof(gatewayOptions));
            _networkOptions = networkOptions ?? throw new ArgumentNullException(nameof(networkOptions));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var gatewayConfig = _gatewayOptions.CurrentValue;
            var networkConfig = _networkOptions.CurrentValue;

            var connectionString = gatewayConfig.RedisConnectionString;
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                _logger.LogWarning("Gateway:RedisConnectionString 未配置，跳过网关注册");
                return;
            }

            _instanceId = string.IsNullOrWhiteSpace(gatewayConfig.GatewayId)
                ? $"Game-Gateway-{Guid.NewGuid():N}"
                : gatewayConfig.GatewayId;

            try
            {
                _registry = new GatewayRegistry(connectionString, logger: _logger);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "初始化 GatewayRegistry 失败，跳过网关注册");
                return;
            }

            var heartbeatInterval = TimeSpan.FromSeconds(
                Math.Max(5, gatewayConfig.RegistryHeartbeatIntervalSeconds));

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("网关注册服务在等待期间收到停止信号，退出注册");
                return;
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var registration = BuildRegistration();
                    await _registry.RegisterAsync(registration).ConfigureAwait(false);
                    _logger.LogInformation(
                        "Game 网关已注册/心跳: {InstanceId}, {Address}:{Port}, Cluster={ClusterId}",
                        registration.InstanceId, registration.Address, registration.Port, registration.ClusterId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "注册 Game 网关到 Redis 失败: {InstanceId}", _instanceId);
                }

                try
                {
                    await Task.Delay(heartbeatInterval, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("网关注册服务收到停止信号，退出心跳循环");
                    break;
                }
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            try
            {
                if (_registry != null && !string.IsNullOrWhiteSpace(_instanceId))
                {
                    await _registry.UnregisterAsync(_instanceId).ConfigureAwait(false);
                    _logger.LogInformation("Game 网关已从 Redis 注销: {InstanceId}", _instanceId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "注销 Game 网关时发生异常: {InstanceId}", _instanceId);
            }

            await base.StopAsync(cancellationToken).ConfigureAwait(false);
        }

        private GatewayRegistration BuildRegistration()
        {
            var gatewayConfig = _gatewayOptions.CurrentValue;
            var networkConfig = _networkOptions.CurrentValue;

            var address = !string.IsNullOrWhiteSpace(gatewayConfig.PublicIpAddress)
                ? gatewayConfig.PublicIpAddress
                : networkConfig.IpAddress;

            if (string.IsNullOrWhiteSpace(address) || address == "0.0.0.0" || address == "::" || address == "*")
            {
                address = ResolveHostAddress();
            }

            var port = gatewayConfig.PublicPort > 0 ? gatewayConfig.PublicPort : networkConfig.TcpPort;

            return new GatewayRegistration
            {
                InstanceId = _instanceId,
                GatewayType = GatewayType.Game,
                ClusterId = gatewayConfig.ClusterId ?? string.Empty,
                Address = address,
                Port = port,
                Region = gatewayConfig.Region ?? string.Empty,
                LastHeartbeatUtc = DateTime.UtcNow
            };
        }

        private static string ResolveHostAddress()
        {
            try
            {
                var hostName = Dns.GetHostName();
                var ipAddresses = Dns.GetHostAddresses(hostName);
                var ipv4 = ipAddresses.FirstOrDefault(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                return ipv4?.ToString() ?? hostName;
            }
            catch
            {
                return "192.168.1.78";
            }
        }
    }
}

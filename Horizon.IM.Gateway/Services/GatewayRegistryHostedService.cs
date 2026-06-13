using System.Net;
using Horizon.IM.Gateway.Configuration;
using Horizon.IM.Gateway.Network;
using Horizon.Strategy.Storage.Redis;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans;

namespace Horizon.IM.Gateway.Services;

/// <summary>
/// IM 网关注册后台服务。
/// 启动时等待 Orleans 客户端就绪，随后将网关 (IP, 端口, Type=IM) 写入共享 Redis，
/// 并按配置的心跳间隔刷新 TTL。停止时主动注销。
/// </summary>
public class GatewayRegistryHostedService : BackgroundService
{
    private readonly ILogger<GatewayRegistryHostedService> _logger;
    private readonly IOptionsMonitor<GatewayOptions> _gatewayOptions;
    private readonly IOptionsMonitor<NetworkOptions> _networkOptions;
    private readonly IOptionsMonitor<OrleansOptions> _orleansOptions;
    private readonly IClusterClient _clusterClient;
    private readonly IMNetworkServer _networkServer;

    private GatewayRegistry? _registry;
    private string _instanceId = string.Empty;

    public GatewayRegistryHostedService(
        ILogger<GatewayRegistryHostedService> logger,
        IOptionsMonitor<GatewayOptions> gatewayOptions,
        IOptionsMonitor<NetworkOptions> networkOptions,
        IOptionsMonitor<OrleansOptions> orleansOptions,
        IClusterClient clusterClient,
        IMNetworkServer networkServer)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _gatewayOptions = gatewayOptions ?? throw new ArgumentNullException(nameof(gatewayOptions));
        _networkOptions = networkOptions ?? throw new ArgumentNullException(nameof(networkOptions));
        _orleansOptions = orleansOptions ?? throw new ArgumentNullException(nameof(orleansOptions));
        _clusterClient = clusterClient ?? throw new ArgumentNullException(nameof(clusterClient));
        _networkServer = networkServer ?? throw new ArgumentNullException(nameof(networkServer));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var gatewayConfig = _gatewayOptions.CurrentValue;

        var connectionString = gatewayConfig.RedisConnectionString;
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            _logger.LogWarning("Gateway:RedisConnectionString 未配置，跳过 IM 网关注册");
            return;
        }

        _instanceId = string.IsNullOrWhiteSpace(gatewayConfig.GatewayId)
            ? $"IM-Gateway-{Guid.NewGuid():N}"
            : gatewayConfig.GatewayId;

        try
        {
            _registry = new GatewayRegistry(connectionString, logger: _logger);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "初始化 GatewayRegistry 失败，跳过 IM 网关注册");
            return;
        }

        var heartbeatInterval = TimeSpan.FromSeconds(
            Math.Max(5, gatewayConfig.RegistryHeartbeatIntervalSeconds));

        // BackgroundService.ExecuteAsync 在所有前置 IHostedService（包含 Orleans 客户端）成功启动后才会运行，
        // 因此到达此处时 Orleans 后端集群连接已就绪。

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var registration = BuildRegistration();
                await _registry.RegisterAsync(registration).ConfigureAwait(false);
                _logger.LogInformation(
                    "IM 网关已注册/心跳: {InstanceId}, {Address}:{Port}, Cluster={ClusterId}",
                    registration.InstanceId, registration.Address, registration.Port, registration.ClusterId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "注册 IM 网关到 Redis 失败: {InstanceId}", _instanceId);
            }

            try
            {
                await Task.Delay(heartbeatInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
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
                _logger.LogInformation("IM 网关已从 Redis 注销: {InstanceId}", _instanceId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "注销 IM 网关时发生异常: {InstanceId}", _instanceId);
        }

        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    private GatewayRegistration BuildRegistration()
    {
        var gatewayConfig = _gatewayOptions.CurrentValue;
        var networkConfig = _networkOptions.CurrentValue;
        var orleansConfig = _orleansOptions.CurrentValue;

        var address = !string.IsNullOrWhiteSpace(gatewayConfig.PublicIpAddress)
            ? gatewayConfig.PublicIpAddress
            : (!string.IsNullOrWhiteSpace(_networkServer.ListenIpAddress)
                ? _networkServer.ListenIpAddress
                : networkConfig.IpAddress);

        if (string.IsNullOrWhiteSpace(address) || address == "0.0.0.0" || address == "::" || address == "*")
        {
            address = ResolveHostAddress();
        }

        var port = gatewayConfig.PublicPort > 0
            ? gatewayConfig.PublicPort
            : (_networkServer.ListenPort > 0 ? _networkServer.ListenPort : networkConfig.TcpPort);

        var clusterId = !string.IsNullOrWhiteSpace(gatewayConfig.ClusterId)
            ? gatewayConfig.ClusterId
            : orleansConfig.ClusterId ?? string.Empty;

        return new GatewayRegistration
        {
            InstanceId = _instanceId,
            GatewayType = GatewayType.IM,
            ClusterId = clusterId,
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

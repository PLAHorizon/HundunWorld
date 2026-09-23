using System;
using System.Threading;
using System.Threading.Tasks;
using Horizon.Game.Core;
using Horizon.Game.Gateway.Configuration;
using Horizon.Game.Message;
using Horizon.Game.Message.Network;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TouchSocket.Core;
using TouchSocket.Sockets;

namespace Horizon.Game.Gateway.Network;

/// <summary>
/// 游戏网络服务器（阶段 3 拆分后的薄门面）：<br/>
/// 仅保留 TcpService 生命周期（Start/Stop）与 TouchSocket 事件接线；
/// 连接生命周期管理委托给 <see cref="ConnectionLifecycleManager"/>，
/// 数据接收流水线委托给 <see cref="ClientPacketPipeline"/>。
/// </summary>
public class GameNetworkServer
{
    private readonly ILogger<GameNetworkServer> _logger;
    private readonly IOptionsMonitor<NetworkOptions> _networkOptions;
    private readonly ConnectionLifecycleManager _lifecycle;
    private readonly ClientPacketPipeline _pipeline;
    private readonly ILog _touchSocketLogger;
    private TcpService? _tcpService;
    private volatile bool _isRunning;

    public GameNetworkServer(
        ILogger<GameNetworkServer> logger,
        ILog tlogger,
        IOptionsMonitor<NetworkOptions> networkOptions,
        ConnectionLifecycleManager lifecycle,
        ClientPacketPipeline pipeline)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _touchSocketLogger = tlogger ?? throw new ArgumentNullException(nameof(tlogger));
        _networkOptions = networkOptions ?? throw new ArgumentNullException(nameof(networkOptions));
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    }

    /// <summary>
    /// 消息路由分发（委托给 <see cref="ClientPacketPipeline"/>）。
    /// </summary>
    public Task<(bool IsSuccess, MessageUnion? Data)> ProcessMessageAsync(ITcpSessionClient client, HorizonMessagePacket message)
        => _pipeline.ProcessMessageAsync(client, message);

    /// <summary>
    /// 启动网络服务器
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (_isRunning)
            {
                _logger.LogWarning("网络服务器已在运行中");
                return;
            }

            _logger.LogInformation("正在启动游戏网络服务器...");

            // 创建并配置TCP服务
            _tcpService = new TcpService();
            _tcpService.Connecting = OnClientConnecting;
            _tcpService.Connected = (client, e) => _lifecycle.HandleConnectedAsync(client, e);
            _tcpService.Closed = (client, e) => _lifecycle.HandleDisconnectedAsync(client, e);
            _tcpService.Received = (client, e) => _pipeline.HandleReceivedAsync(client, e);
            var config = new TouchSocketConfig()
                // 注意：必须用 IPHost 对象重载。字符串重载会按本机网卡展开出额外监听
                // （含公网 IPv6），机器具备全局 IPv6 地址时第二个 bind 报 10048 假性端口占用。
                .SetListenIPHosts(new IPHost[] { new IPHost($"{_networkOptions.CurrentValue.IpAddress}:{_networkOptions.CurrentValue.TcpPort}") })
                .SetTcpDataHandlingAdapter(() => new HorizonMessageAdapter())
                .ConfigureContainer(container =>
                {
                    container.AddLogger(_touchSocketLogger);
                })
                .ConfigurePlugins(plugins =>
                {
                    // 可以添加插件配置
                });

            await _tcpService.SetupAsync(config);
            await _tcpService.StartAsync();

            _isRunning = true;
            _logger.LogInformation("游戏网络服务器启动成功，监听 {IpAddress}:{Port}",
                _networkOptions.CurrentValue.IpAddress, _networkOptions.CurrentValue.TcpPort);

            // 启动断线检测 + 租约续约定时器（由 ConnectionLifecycleManager 管理）。
            _lifecycle.StartTimers();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "启动网络服务器失败");
            throw;
        }
    }

    /// <summary>
    /// 停止网络服务器
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!_isRunning)
            {
                _logger.LogWarning("网络服务器未在运行");
                return;
            }

            _logger.LogInformation("正在停止游戏网络服务器...");

            _lifecycle.StopTimers();

            if (_tcpService != null)
            {
                await _tcpService.StopAsync();
                _tcpService.Dispose();
                _tcpService = null;
            }

            _isRunning = false;
            _logger.LogInformation("游戏网络服务器已停止");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "停止网络服务器时发生错误");
            throw;
        }
    }

    /// <summary>
    /// 客户端连接中事件。
    /// </summary>
    private Task OnClientConnecting(ITcpSessionClient client, ConnectingEventArgs e)
    {
        try
        {
            var ip = client.IP ?? "unknown";
            _logger.LogDebug($"客户端正在连接: {ip}:{client.Port}");

            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理客户端连接事件时发生错误");
            e.IsPermitOperation = false;
            return Task.CompletedTask;
        }
    }
}

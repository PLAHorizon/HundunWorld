using Horizon.Game.Core;
using Horizon.Game.Gateway.Configuration;
using Horizon.Game.Gateway.Services;
using Horizon.Game.Message;
using Horizon.Game.Message.Network;
using MemoryPack;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using TouchSocket.Core;
using TouchSocket.Sockets;

namespace Horizon.Game.Gateway.Network
{
    /// <summary>
    /// 游戏网络服务器
    /// </summary>
    public class GameNetworkServer
    {
        private readonly ILogger<GameNetworkServer> _logger;
        private readonly IOptionsMonitor<NetworkOptions> _networkOptions;
        private readonly IConnectionManager _connectionManager;
        private readonly IEnumerable<IMessageHandler> _messageHandlers;
        private readonly ILog _touchSocketLogger;
        private TcpService? _tcpService;
        private bool _isRunning;
        private HorizonMessageAdapter _adapter;
        public GameNetworkServer(
            ILogger<GameNetworkServer> logger,
            ILog tlogger,
            IOptionsMonitor<NetworkOptions> networkOptions,
            IConnectionManager connectionManager,
            IEnumerable<IMessageHandler> messageHandlers, HorizonMessageAdapter adapter)
        {
            _logger = logger;
            _touchSocketLogger = tlogger;
            _networkOptions = networkOptions;
            _connectionManager = connectionManager;
            _messageHandlers = messageHandlers;
            _adapter = adapter;
        }



        /// <summary>
        /// 
        /// </summary>
        /// <param name="message"></param>
        /// <returns></returns>
        public async Task<(bool IsSuccess, MessageUnion? Data)> ProcessMessageAsync(ITcpSessionClient client, HorizonMessagePacket message)
        {
            foreach (var handler in _messageHandlers)
            {
                if (handler.ServiceType != message.ServiceType) continue;
                if (handler.ValidateMessage(message))
                {
                    return await handler.HandleAsync(client, message);

                }
            }
            return (false, null);
        }
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
                _tcpService.Connected = OnClientConnected;
                _tcpService.Closed = OnClientDisconnected;
                _tcpService.Received = OnDataReceived;
                var config = new TouchSocketConfig()
                    .SetListenIPHosts(_networkOptions.CurrentValue.IpAddress, _networkOptions.CurrentValue.TcpPort)
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
        /// 客户端连接中事件
        /// </summary>
        private Task OnClientConnecting(ITcpSessionClient client, ConnectingEventArgs e)
        {
            try
            {
                _logger.LogDebug($"客户端正在连接: {client.IP}:{client.Port}");

                // 可以在这里进行连接前的验证，例如IP白名单检查
                // e.IsPermitOperation = false; // 拒绝连接

                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理客户端连接事件时发生错误");
                e.IsPermitOperation = false;
                return Task.CompletedTask;
            }
        }

        /// <summary>
        /// 客户端已连接事件
        /// </summary>
        private async Task OnClientConnected(ITcpSessionClient client, ConnectedEventArgs e)
        {
            try
            {
                _logger.LogInformation("新客户端已连接: {Id} from {RemoteEndPoint}",
                    client.Id, client.GetIPPort());

                // 创建游戏连接对象
                var gameConnection = new GameConnection(client, _logger);

                // 添加到连接管理器
                var success = await _connectionManager.AddConnectionAsync(gameConnection);
                if (!success)
                {
                    _logger.LogWarning("添加连接失败，关闭客户端: {Id}", client.Id);
                    await client.CloseAsync("连接管理器拒绝连接");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理客户端连接完成事件时发生错误: {Id}", client.Id);
                await client.CloseAsync("服务器内部错误");
            }
        }

        /// <summary>
        /// 客户端断开连接事件
        /// </summary>
        private async Task OnClientDisconnected(ITcpSessionClient client, ClosedEventArgs e)
        {
            try
            {
                _logger.LogInformation("客户端已断开连接: {Id}, 原因: {Message}",
                    client.Id, e.Message);

                // 从连接管理器移除
                await _connectionManager.RemoveConnectionAsync(client.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理客户端断开连接事件时发生错误: {Id}", client.Id);
            }
        }

        /// <summary>
        /// 接收数据事件
        /// </summary>
        private async Task OnDataReceived(ITcpSessionClient client, ReceivedDataEventArgs e)
        {
            try
            {
                // 获取连接对象
                var connection = _connectionManager.GetConnection(client.Id);
                if (connection == null)
                {
                    _logger.LogWarning("收到数据但连接不存在: {Id}", client.Id);
                    return;
                }

                // 更新最后活跃时间
                connection.LastActiveTime = DateTime.UtcNow;

                try
                {
                    HorizonMessagePacket messagePacket = null;

                    if (e.RequestInfo is HorizonMessageInfo horizonRequest)
                    {
                        messagePacket = horizonRequest.Packet;
                    }
                    else
                    {
                        // 使用 TouchSocket 4.0.2 的新 API 获取数据
                        messagePacket = _adapter.UnpackMessage(e.Memory.ToArray());
                    }

                    if (messagePacket != null)
                    {
                        // 验证消息头的必需字段
                        if (messagePacket.Header.GameId <= 0)
                        {
                            _logger.LogWarning("收到无效消息: GameId必须为正数. Client: {Id}", client.Id);
                            return;
                        }

                        if (messagePacket.Header.ServerId <= 0)
                        {
                            _logger.LogWarning("收到无效消息: ServerId必须为正数. Client: {Id}", client.Id);
                            return;
                        }

                        await ProcessMessageAsync(client, messagePacket);
                    }
                    else
                    {
                        _logger.LogWarning("反序列化消息失败: {Id}", client.Id);
                    }
                }
                catch (ArgumentException argEx)
                {
                    _logger.LogWarning(argEx, "消息验证失败: {Id}", client.Id);
                }
                catch (Exception deserializeEx)
                {
                    _logger.LogError(deserializeEx, "反序列化消息时发生错误: {Id}", client.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理接收数据事件时发生错误: {Id}", client.Id);
            }
        }


    }
}
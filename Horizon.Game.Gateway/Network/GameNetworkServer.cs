using Horizon.Game.Core;
using Horizon.Game.Core.Interfaces;
using Horizon.Game.Gateway.Configuration;
using Horizon.Game.Gateway.Services;
using Horizon.Game.Message;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using Horizon.Core.Security;
using MemoryPack;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Diagnostics;
using System.Collections.Generic;
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
        private readonly IOptionsMonitor<GatewayOptions> _gatewayOptions;
        private readonly IConnectionManager _connectionManager;
        private readonly IEnumerable<IMessageHandler> _messageHandlers;
        private readonly ILog _touchSocketLogger;
        private readonly UserAuthTokenProvider _authTokenProvider;
        private readonly ICharacterFingerprintService _fingerprintService;
        private TcpService? _tcpService;
        private volatile bool _isRunning;
        private HorizonMessageAdapter _adapter;
        private readonly SemaphoreSlim _connectionRegistrationGate = new(1, 1);
        public GameNetworkServer(
            ILogger<GameNetworkServer> logger,
            ILog tlogger,
            IOptionsMonitor<NetworkOptions> networkOptions,
            IOptionsMonitor<GatewayOptions> gatewayOptions,
            IConnectionManager connectionManager,
            IEnumerable<IMessageHandler> messageHandlers, HorizonMessageAdapter adapter,
            UserAuthTokenProvider? authTokenProvider = null,
            ICharacterFingerprintService? fingerprintService = null)
        {
            _logger = logger;
            _touchSocketLogger = tlogger;
            _networkOptions = networkOptions;
            _gatewayOptions = gatewayOptions;
            _connectionManager = connectionManager;
            _messageHandlers = messageHandlers;
            _adapter = adapter;
            _authTokenProvider = authTokenProvider;
            _fingerprintService = fingerprintService;
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
                if (handler.CanHandle(message) && handler.ValidateMessage(message))
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

                var gameConnection = await EnsureConnectionRegisteredAsync(client, logOnCreate: false);
                if (gameConnection == null)
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

                // 清除该连接关联的所有角色在线指纹
                if (_fingerprintService != null)
                {
                    try
                    {
                        await _fingerprintService.ReleaseByConnectionAsync(client.Id);
                    }
                    catch (Exception fpEx)
                    {
                        _logger.LogWarning(fpEx, "清理断线角色指纹时发生错误: {Id}", client.Id);
                    }
                }

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
                    connection = await EnsureConnectionRegisteredAsync(client, logOnCreate: true);
                    if (connection == null)
                    {
                        _logger.LogWarning("收到数据但连接不存在，且无法补注册: {Id}", client.Id);
                        return;
                    }
                }

                // 更新最后活跃时间
                connection.LastActiveTime = DateTime.UtcNow;

                try
                {
                    // HorizonMessageAdapter（CustomFixedHeaderDataHandlingAdapter）会在帧边界完整后
                    // 将解析结果作为 HorizonMessageInfo 投递到 e.RequestInfo。
                    if (e.RequestInfo is not HorizonMessageInfo horizonRequest || horizonRequest.Packet == null)
                    {
                        _logger.LogWarning("收到无法解析的消息帧: {Id}", client.Id);
                        return;
                    }

                    var messagePacket = horizonRequest.Packet;

                    // 验证消息头的必需字段
                    if (!messagePacket.Header.IsResponse)
                    {
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
                    }

                    // 用户鉴权令牌验证：非登录/注册请求必须携带有效的鉴权令牌
                    if (_authTokenProvider != null && !IsAuthExemptMessage(messagePacket))
                    {
                        var authToken = messagePacket.Header.AuthToken;
                        if (string.IsNullOrWhiteSpace(authToken))
                        {
                            _logger.LogWarning("收到未携带鉴权令牌的请求，拒绝服务. Client: {Id}, MessageType: {MessageType}", 
                                client.Id, messagePacket.Header.MessageType);
                            await SendAuthErrorAsync(client, messagePacket, "请求缺少鉴权令牌，请重新登录");
                            return;
                        }

                        var machineId = messagePacket.Header.MachineId;
                        var expectedMachineId = _gatewayOptions.CurrentValue.ValidateTokenMachineId ? machineId : null;
                        var validationResult = _authTokenProvider.ValidateToken(authToken, expectedMachineId: expectedMachineId);
                        if (!validationResult.IsValid)
                        {
                            _logger.LogWarning("鉴权令牌验证失败，拒绝服务. Client: {Id}, Reason: {Reason}, MessageType: {MessageType}", 
                                client.Id, validationResult.ErrorMessage, messagePacket.Header.MessageType);
                            await SendAuthErrorAsync(client, messagePacket, "鉴权失败，请重新登录");
                            return;
                        }
                    }

                    var (isSuccess, responseData) = await ProcessMessageAsync(client, messagePacket);

                    // 处理完成后，将响应中携带的鉴权令牌存储到对应连接，实现令牌替换
                    if (isSuccess && responseData != null)
                    {
                        if (responseData is LoginResponse loginResp && !string.IsNullOrEmpty(loginResp.AuthToken))
                        {
                            connection.AuthToken = loginResp.AuthToken;
                            _logger.LogDebug("已更新连接 {Id} 的鉴权令牌（登录）", client.Id);
                        }
                        else if (responseData is TokenLoginResponse tokenLoginResp && !string.IsNullOrEmpty(tokenLoginResp.AuthToken))
                        {
                            connection.AuthToken = tokenLoginResp.AuthToken;
                            _logger.LogDebug("已更新连接 {Id} 的鉴权令牌（Token登录）", client.Id);
                        }
                        else if (responseData is EnterGameResponse enterResp && !string.IsNullOrEmpty(enterResp.AuthToken))
                        {
                            connection.AuthToken = enterResp.AuthToken;
                            _logger.LogDebug("已更新连接 {Id} 的鉴权令牌（含角色Id）", client.Id);
                        }
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

        private async Task<IGameConnection?> EnsureConnectionRegisteredAsync(ITcpSessionClient client, bool logOnCreate)
        {
            var existing = _connectionManager.GetConnection(client.Id);
            if (existing != null)
            {
                return existing;
            }

            await _connectionRegistrationGate.WaitAsync().ConfigureAwait(false);
            try
            {
                existing = _connectionManager.GetConnection(client.Id);
                if (existing != null)
                {
                    return existing;
                }

                var gameConnection = new GameConnection(client, _logger);
                var success = await _connectionManager.AddConnectionAsync(gameConnection).ConfigureAwait(false);
                if (!success)
                {
                    return _connectionManager.GetConnection(client.Id);
                }

                if (logOnCreate)
                {
                    _logger.LogDebug("收到首包时补注册连接成功: {Id} from {RemoteEndPoint}", client.Id, client.GetIPPort());
                }

                return gameConnection;
            }
            finally
            {
                _connectionRegistrationGate.Release();
            }
        }

        /// <summary>
        /// 判断消息是否免除鉴权验证（登录、注册等无需鉴权的消息）
        /// </summary>
        private static bool IsAuthExemptMessage(HorizonMessagePacket message)
        {
            return message.Header.MessageType == MessageType.LoginRequest
                || message.Header.MessageType == MessageType.RegisterRequest
                || message.Header.MessageType == MessageType.TokenLoginRequest;
        }

        /// <summary>
        /// 向客户端发送鉴权失败错误消息
        /// </summary>
        private async Task SendAuthErrorAsync(ITcpSessionClient client, HorizonMessagePacket originalMessage, string errorMessage)
        {
            try
            {
                var errorResponse = new AuthenticationError
                {
                    ErrorCode = 1007,
                    ErrorMessage = errorMessage,
                    ErrorDetails = "用户鉴权验证失败，请重新登录获取有效的鉴权令牌",
                    RetryAfterSeconds = 0,
                    RequiresReconnect = true
                };

                var responsePacket = new HorizonMessagePacket
                {
                    Header = new MessageHeader
                    {
                        MessageType = MessageType.Error,
                        ServiceType = ServiceType.Account,
                        IsResponse = true,
                        ResponseToMessageId = originalMessage.Header.MessageId,
                        RequireResponse = false,
                        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        GameId = originalMessage.Header.GameId,
                        ZoneId = originalMessage.Header.ZoneId,
                        ServerId = originalMessage.Header.ServerId,
                    },
                    ServiceType = ServiceType.Account,
                    Body = errorResponse,
                    RawData = MemoryPackSerializer.Serialize(errorResponse),
                };

                var buff = _adapter.PackPacket(responsePacket);
                await client.SendAsync(buff);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "发送鉴权错误消息失败: {Id}", client.Id);
            }
        }

    }
}
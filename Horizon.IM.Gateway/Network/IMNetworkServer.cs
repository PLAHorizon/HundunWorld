using Horizon.IM.Core;
using Horizon.IM.Core.Adapters;
using Horizon.IM.Message;
using Horizon.IM.Message.Enums;
using Horizon.IM.Message.Network;
using Horizon.IM.Gateway.Configuration;
using Horizon.IM.Gateway.Services;
using Horizon.Core.Security;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using System.Net.Sockets;

using TouchSocket.Core;
using TouchSocket.Sockets;

namespace Horizon.IM.Gateway.Network;

public class IMNetworkServer
{
    private readonly ILogger<IMNetworkServer> _logger;
    private readonly IOptionsMonitor<NetworkOptions> _networkOptions;
    private readonly IIMConnectionManager _connectionManager;
    private readonly IMGatewayPushService _pushService;
    private readonly IEnumerable<IIMMessageHandler> _messageHandlers;
    private readonly ILog _touchSocketLogger;
    private readonly UserAuthTokenProvider? _authTokenProvider;
    private readonly IMMessageAdapter _adapter;

    private TcpService? _tcpService;
    private volatile bool _isRunning;
    private string _listenIpAddress = string.Empty;
    private int _listenPort;

    public IMNetworkServer(
        ILogger<IMNetworkServer> logger,
        ILog touchSocketLogger,
        IOptionsMonitor<NetworkOptions> networkOptions,
        IIMConnectionManager connectionManager,
        IMGatewayPushService pushService,
        IEnumerable<IIMMessageHandler> messageHandlers,
        IMMessageAdapter adapter,
        UserAuthTokenProvider? authTokenProvider = null)
    {
        _logger = logger;
        _touchSocketLogger = touchSocketLogger;
        _networkOptions = networkOptions;
        _connectionManager = connectionManager;
        _pushService = pushService;
        _messageHandlers = messageHandlers;
        _adapter = adapter;
        _authTokenProvider = authTokenProvider;
    }

    public string ListenIpAddress => _listenIpAddress;

    public int ListenPort => _listenPort;

    public async Task<(bool IsSuccess, IMMessageUnion? Data)> ProcessMessageAsync(ITcpSessionClient client, IMMessagePacket message)
    {
        foreach (var handler in _messageHandlers)
        {
            if (handler.ServiceType != message.ServiceType)
            {
                continue;
            }

            if (handler.ValidateMessage(message))
            {
                return await handler.HandleAsync(client, message).ConfigureAwait(false);
            }
        }

        _logger.LogWarning("未找到匹配的 IM 处理器: ServiceType={ServiceType}, MessageType={MessageType}", message.ServiceType, message.Header.MessageType);
        return (false, null);
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_isRunning)
        {
            return;
        }

        var networkOptions = _networkOptions.CurrentValue;
        var startPort = networkOptions.TcpPort;
        var maxPort = networkOptions.AllowPortFallback
            ? startPort + Math.Max(0, networkOptions.PortFallbackRange)
            : startPort;

        Exception? lastException = null;

        for (var port = startPort; port <= maxPort; port++)
        {
            var tcpService = CreateTcpService();

            try
            {
                var config = new TouchSocketConfig()
                    .SetListenIPHosts(networkOptions.IpAddress, port)
                    .SetTcpDataHandlingAdapter(() => new IMMessageAdapter())
                    .ConfigureContainer(container => container.AddLogger(_touchSocketLogger));

                await tcpService.SetupAsync(config).ConfigureAwait(false);
                await tcpService.StartAsync().ConfigureAwait(false);

                _tcpService = tcpService;
                _listenIpAddress = networkOptions.IpAddress;
                _listenPort = port;
                _isRunning = true;

                if (port != startPort)
                {
                    _logger.LogWarning(
                        "IM 网关默认端口 {ConfiguredPort} 已被占用，已切换到可用端口 {ActualPort}。",
                        startPort,
                        port);
                }

                _logger.LogInformation("IM 网关已启动，监听 {IpAddress}:{Port}", _listenIpAddress, _listenPort);
                return;
            }
            catch (Exception ex) when (IsAddressAlreadyInUse(ex) && port < maxPort)
            {
                lastException = ex;
                tcpService.Dispose();
                _logger.LogWarning(
                    ex,
                    "IM 网关端口 {Port} 已被占用，尝试下一个端口。",
                    port);
            }
            catch (Exception ex)
            {
                tcpService.Dispose();
                throw;
            }
        }

        throw new InvalidOperationException(
            $"IM 网关无法绑定监听地址 {networkOptions.IpAddress}:{startPort}-{maxPort}，请检查端口占用情况。",
            lastException);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!_isRunning)
        {
            return;
        }

        if (_tcpService != null)
        {
            await _tcpService.StopAsync().ConfigureAwait(false);
            _tcpService.Dispose();
            _tcpService = null;
        }

        _isRunning = false;
        _listenIpAddress = string.Empty;
        _listenPort = 0;
        _logger.LogInformation("IM 网关已停止");
    }

    private TcpService CreateTcpService()
    {
        var tcpService = new TcpService();
        tcpService.Connecting = OnClientConnecting;
        tcpService.Connected = OnClientConnected;
        tcpService.Closed = OnClientDisconnected;
        tcpService.Received = OnDataReceived;
        return tcpService;
    }

    private static bool IsAddressAlreadyInUse(Exception exception)
    {
        if (exception is SocketException socketException)
        {
            return socketException.SocketErrorCode == SocketError.AddressAlreadyInUse;
        }

        return exception.InnerException != null && IsAddressAlreadyInUse(exception.InnerException);
    }

    private Task OnClientConnecting(ITcpSessionClient client, ConnectingEventArgs e)
    {
        _logger.LogDebug("IM 客户端正在连接: {RemoteEndPoint}", client.GetIPPort());
        return Task.CompletedTask;
    }

    private async Task OnClientConnected(ITcpSessionClient client, ConnectedEventArgs e)
    {
        _logger.LogInformation("IM 客户端已连接: {ClientId} from {RemoteEndPoint}", client.Id, client.GetIPPort());
        await _connectionManager.AddConnectionAsync(new IMConnection(client)).ConfigureAwait(false);
    }

    private async Task OnClientDisconnected(ITcpSessionClient client, ClosedEventArgs e)
    {
        _logger.LogInformation("IM 客户端已断开: {ClientId}, Reason={Reason}", client.Id, e.Message);
        var connection = _connectionManager.GetConnection(client.Id);
        var shouldHandleDisconnect = connection?.UserId > 0
            && string.Equals(_connectionManager.GetConnectionByUser(connection.UserId)?.Id, client.Id, StringComparison.Ordinal);
        await _connectionManager.RemoveConnectionAsync(client.Id).ConfigureAwait(false);

        if (shouldHandleDisconnect)
        {
            await _pushService.HandleUserDisconnectedAsync(connection.UserId).ConfigureAwait(false);
        }
    }

    private async Task OnDataReceived(ITcpSessionClient client, ReceivedDataEventArgs e)
    {
        var connection = _connectionManager.GetConnection(client.Id);
        if (connection == null)
        {
            _logger.LogWarning("收到 IM 数据但连接不存在: {ClientId}", client.Id);
            return;
        }

        connection.LastActiveTime = DateTime.UtcNow;

        if (e.RequestInfo is not IMMessageInfo requestInfo || requestInfo.Packet == null)
        {
            _logger.LogWarning("收到无法解析的 IM 消息帧: {ClientId}", client.Id);
            return;
        }

        var packet = requestInfo.Packet;
        if (packet.Header.UserId == 0)
        {
            packet.Header.UserId = IMPacketUserResolver.ResolveUserId(packet);
        }

        // 用户鉴权令牌验证：所有IM请求必须携带有效的鉴权令牌
        if (_authTokenProvider != null)
        {
            var authToken = packet.Header.AuthToken;
            if (string.IsNullOrWhiteSpace(authToken))
            {
                _logger.LogWarning("收到未携带鉴权令牌的IM请求，拒绝服务. ClientId: {ClientId}, MessageType: {MessageType}",
                    client.Id, packet.Header.MessageType);
                await SendAuthErrorAsync(client, packet, "请求缺少鉴权令牌，请重新登录");
                return;
            }

            var machineId = packet.Header.MachineId;
            var validationResult = _authTokenProvider.ValidateToken(authToken, expectedMachineId: machineId);
            if (!validationResult.IsValid)
            {
                _logger.LogWarning("IM鉴权令牌验证失败，拒绝服务. ClientId: {ClientId}, Reason: {Reason}, MessageType: {MessageType}",
                    client.Id, validationResult.ErrorMessage, packet.Header.MessageType);
                await SendAuthErrorAsync(client, packet, "鉴权失败，请重新登录");
                return;
            }
        }

        if (packet.Header.UserId > 0 && ShouldBindSession(packet))
        {
            await _connectionManager.BindUserAsync(packet.Header.UserId, client.Id).ConfigureAwait(false);
            await _pushService
                .EnsureUserSessionAsync(
                    packet.Header.UserId,
                    ResolveSessionValue(packet, IMSessionHeaderKeys.Nickname),
                    ResolveSessionValue(packet, IMSessionHeaderKeys.Avatar),
                    ResolveOnlineStatus(packet))
                .ConfigureAwait(false);
        }

        await ProcessMessageAsync(client, packet).ConfigureAwait(false);
    }

    private static bool ShouldBindSession(IMMessagePacket packet)
    {
        return packet.Header.MessageType == IMMessageType.Heartbeat;
    }

    private static string ResolveSessionValue(IMMessagePacket packet, string key)
    {
        return packet.Header.ExtensionData.TryGetValue(key, out var value)
            ? value ?? string.Empty
            : string.Empty;
    }

    private static IMOnlineStatus ResolveOnlineStatus(IMMessagePacket packet)
    {
        if (!packet.Header.ExtensionData.TryGetValue(IMSessionHeaderKeys.OnlineStatus, out var rawValue)
            || string.IsNullOrWhiteSpace(rawValue))
        {
            return IMOnlineStatus.Online;
        }

        if (Enum.TryParse<IMOnlineStatus>(rawValue, ignoreCase: true, out var namedStatus))
        {
            return namedStatus;
        }

        return int.TryParse(rawValue, out var numericStatus)
            ? (IMOnlineStatus)numericStatus
            : IMOnlineStatus.Online;
    }

    /// <summary>
    /// 向客户端发送鉴权失败错误消息
    /// </summary>
    private async Task SendAuthErrorAsync(ITcpSessionClient client, IMMessagePacket originalPacket, string errorMessage)
    {
        try
        {
            var error = new IMErrorMessage
            {
                ErrorCode = IMErrorCode.Unknown,
                Message = errorMessage,
                RelatedMessageId = originalPacket.Header.MessageId,
                Details = "用户鉴权验证失败，请重新登录获取有效的鉴权令牌",
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            var responsePacket = _adapter.CreatePacket(
                error,
                originalPacket.Header.UserId,
                isResponse: true,
                responseToMessageId: originalPacket.Header.MessageId);

            await client.SendAsync(_adapter.PackPacket(responsePacket)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "发送IM鉴权错误消息失败: {ClientId}", client.Id);
        }
    }
}
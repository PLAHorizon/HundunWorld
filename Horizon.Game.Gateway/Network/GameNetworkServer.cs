using Horizon.Game.Core;
using Horizon.Game.Core.Interfaces;
using Horizon.Game.Gateway.Configuration;
using Horizon.Game.Gateway.Services;
using Horizon.Game.Message;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using Horizon.Game.Message.Sync;
using Horizon.Core.Security;
using MemoryPack;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Sockets;
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
        private readonly PlayerDespawnScheduler _despawnScheduler;
        private TcpService? _tcpService;
        private volatile bool _isRunning;
        private HorizonMessageAdapter _adapter;
        private readonly SemaphoreSlim _connectionRegistrationGate = new(1, 1);
        private Timer? _disconnectCheckTimer;
        private Timer? _leaseRenewalTimer;
        public GameNetworkServer(
            ILogger<GameNetworkServer> logger,
            ILog tlogger,
            IOptionsMonitor<NetworkOptions> networkOptions,
            IOptionsMonitor<GatewayOptions> gatewayOptions,
            IConnectionManager connectionManager,
            IEnumerable<IMessageHandler> messageHandlers, HorizonMessageAdapter adapter,
            PlayerDespawnScheduler despawnScheduler,
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
            _despawnScheduler = despawnScheduler;
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
            _logger.LogWarning(
                "无 handler 匹配消息。MessageType={MessageType}, ServiceType={ServiceType}, Client={ClientId}",
                message.Header.MessageType, message.ServiceType, client.Id);
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

                // 启动断线检测定时器：每 5 秒遍历所有连接，检测 IsConnected==false 的连接并主动清理。
                // TouchSocket 的 Closed 事件在客户端非正常断开（直接关进程/断网）时可能不被及时触发，
                // 此定时器作为后备机制，确保断线角色最终被 Despawn。
                _disconnectCheckTimer = new Timer(
                    CheckDisconnectedConnections, null,
                    TimeSpan.FromSeconds(5),
                    TimeSpan.FromSeconds(5));

                // 启动实体租约续约定时器：每 20 秒批量续约所有在线角色的实体租约。
                // ZoneShardGrain 会自动清理超过 90 秒未续约的孤儿实体（网关崩溃/断线未清理的残留实体）。
                // 这是兜底机制，确保任何情况下孤儿实体都不会永久残留。
                _leaseRenewalTimer = new Timer(
                    async _ => await RenewEntityLeasesCallbackAsync(),
                    null,
                    TimeSpan.FromSeconds(20),
                    TimeSpan.FromSeconds(20));
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

                _disconnectCheckTimer?.Dispose();
                _disconnectCheckTimer = null;

                _leaseRenewalTimer?.Dispose();
                _leaseRenewalTimer = null;

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

                // 设置 TCP KeepAlive：客户端非正常断开（直接关闭进程/断网）时，
                // 10 秒后开始探测，每 3 秒一次，快速判定断开。
                // 没有这个设置，Windows 默认 keepalive 可能长达 2 小时，OnClientDisconnected 无法及时触发。
                TrySetKeepAlive(client);

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
        /// 通过反射设置底层 Socket 的 KeepAlive 选项。<br/>
        /// TouchSocket 的 ITcpSessionClient 不直接暴露 Socket，需要递归查找属性和字段（含非公共成员）。<br/>
        /// 注意：此 KeepAlive 为辅助机制，核心断线检测依赖 <see cref="CheckDisconnectedConnections"/> 的应用层心跳超时。
        /// </summary>
        private void TrySetKeepAlive(ITcpSessionClient client)
        {
            try
            {
                var socket = FindSocketMember(client, depth: 0);
                if (socket == null)
                {
                    // 提升日志级别到 Warning：便于诊断反射查找失败问题。
                    // 反射失败时 TCP KeepAlive 不会设置，但应用层心跳超时检测仍能保证断线清理。
                    _logger.LogWarning(
                        "客户端 {Id} 未找到底层 Socket，跳过 TCP KeepAlive 设置（断线检测回退到应用层心跳超时）",
                        client.Id);
                    return;
                }

                socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, 10);
                socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, 3);
                socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, 3);
                _logger.LogDebug("客户端 {Id} 已设置 TCP KeepAlive（10s/3s/3次）", client.Id);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "设置 KeepAlive 失败: {Id}（断线检测回退到应用层心跳超时）", client.Id);
            }
        }

        /// <summary>
        /// 递归查找 Socket 类型的属性或字段（含非公共成员，最多 3 层深度）。<br/>
        /// TouchSocket 4.x 的 TcpSessionClient 内部 Socket 通常在私有字段中，
        /// 因此必须同时查找 Public/NonPublic 的属性和字段。
        /// </summary>
        private static Socket? FindSocketMember(object? obj, int depth)
        {
            if (obj == null || depth > 3) return null;
            var type = obj.GetType();
            const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            // 第一轮：直接查找 Socket 类型的属性（含非公共）
            foreach (var prop in type.GetProperties(Flags))
            {
                if (prop.PropertyType == typeof(Socket) && prop.GetIndexParameters().Length == 0)
                {
                    try { return prop.GetValue(obj) as Socket; }
                    catch { }
                }
            }

            // 第二轮：直接查找 Socket 类型的字段（含非公共）
            foreach (var field in type.GetFields(Flags))
            {
                if (field.FieldType == typeof(Socket))
                {
                    try { return field.GetValue(obj) as Socket; }
                    catch { }
                }
            }

            // 第三轮：递归查找嵌套对象中的 Socket（按常见名称优先）
            // 常见名称：InternalClient, MainSocket, Client, Socket, WorkSocket
            var nestedCandidates = new List<(object Value, string Name)>();
            foreach (var prop in type.GetProperties(Flags))
            {
                if (!prop.PropertyType.IsClass) continue;
                if (prop.PropertyType == typeof(string)) continue;
                if (prop.GetIndexParameters().Length > 0) continue;
                if (prop.PropertyType.Assembly.GetName().Name?.StartsWith("System") == true) continue;
                try
                {
                    var nested = prop.GetValue(obj);
                    if (nested == null || ReferenceEquals(nested, obj)) continue;
                    nestedCandidates.Add((nested, prop.Name));
                }
                catch { }
            }
            foreach (var field in type.GetFields(Flags))
            {
                if (!field.FieldType.IsClass) continue;
                if (field.FieldType == typeof(string)) continue;
                if (field.FieldType.Assembly.GetName().Name?.StartsWith("System") == true) continue;
                try
                {
                    var nested = field.GetValue(obj);
                    if (nested == null || ReferenceEquals(nested, obj)) continue;
                    nestedCandidates.Add((nested, field.Name));
                }
                catch { }
            }

            // 按名称优先级排序：Socket 相关名称优先
            foreach (var (nested, name) in nestedCandidates.OrderBy(c => GetSocketSearchPriority(c.Name)))
            {
                var socket = FindSocketMember(nested, depth + 1);
                if (socket != null) return socket;
            }
            return null;
        }

        /// <summary>
        /// Socket 查找名称优先级（值越小优先级越高）。
        /// </summary>
        private static int GetSocketSearchPriority(string name)
        {
            if (name == "Socket" || name == "MainSocket" || name == "WorkSocket") return 0;
            if (name == "InternalClient" || name == "Client") return 1;
            return 2;
        }

        /// <summary>
        /// 客户端断开连接事件（由 TouchSocket Closed 事件触发）
        /// </summary>
        private async Task OnClientDisconnected(ITcpSessionClient client, ClosedEventArgs e)
        {
            try
            {
                _logger.LogInformation("客户端断开: {Id}, 原因: {Message}", client.Id, e.Message);
                await CleanupConnectionAsync(client.Id, source: "Closed事件");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理客户端断开连接事件时发生错误: {Id}", client.Id);
            }
        }

        /// <summary>
        /// 定时检测断线连接（每 5 秒触发）。<br/>
        /// 检测两种断线判定：<br/>
        /// 1. <see cref="IGameConnection.IsConnected"/>==false：TouchSocket 底层判定（依赖 TCP 层 RST/FIN 或 KeepAlive 探测）；<br/>
        /// 2. <see cref="IGameConnection.LastActiveTime"/> 空闲超时：应用层心跳超时判定，超过 <see cref="NetworkOptions.IdleTimeoutSeconds"/> 未收到任何数据。<br/>
        /// <para>
        /// 关键说明：检测 2 是检测客户端非正常断开（关进程/断网）的最可靠机制。
        /// TCP KeepAlive 在 TouchSocket 中需要反射访问底层 Socket，可能失败；
        /// <c>Online</c> 属性在 TCP 层未探测到断开时会一直保持 true（Windows 默认 keepalive 长达 2 小时）。
        /// 而客户端有 20 秒心跳，超过 60 秒无数据必然是非正常断开。
        /// </para>
        /// </summary>
        private async void CheckDisconnectedConnections(object? state)
        {
            try
            {
                var idleTimeout = TimeSpan.FromSeconds(_networkOptions.CurrentValue.IdleTimeoutSeconds);
                var now = DateTime.UtcNow;

                foreach (var connection in _connectionManager.GetAllConnections())
                {
                    // 检测 1：TouchSocket Online 属性判定（依赖 TCP 层检测，可能不及时）
                    if (!connection.IsConnected)
                    {
                        await CleanupConnectionAsync(connection.ConnectionId, source: "Online=false");
                        continue;
                    }

                    // 检测 2：应用层心跳超时判定（最可靠，不依赖 TCP KeepAlive）
                    // 客户端心跳间隔约 20 秒，超过 IdleTimeoutSeconds（默认 60 秒）未收到任何数据，
                    // 说明客户端已非正常断开（关进程/断网），需要主动清理。
                    var idleDuration = now - connection.LastActiveTime;
                    if (idleDuration > idleTimeout)
                    {
                        _logger.LogWarning(
                            "连接 {Id} 空闲超时（{Seconds:F0}秒无数据），判定为离线并清理。LastActive={LastActive:O}, Remote={Remote}",
                            connection.ConnectionId, idleDuration.TotalSeconds, connection.LastActiveTime, connection.RemoteAddress);
                        await CleanupConnectionAsync(connection.ConnectionId, source: "空闲超时");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "定时检测断线连接时发生错误");
            }
        }

        /// <summary>
        /// 实体租约续约定时器回调：委托给 <see cref="PlayerDespawnScheduler.RenewAllLeasesAsync"/>。
        /// </summary>
        private async Task RenewEntityLeasesCallbackAsync()
        {
            try
            {
                await _despawnScheduler.RenewAllLeasesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "实体租约续约定时器异常");
            }
        }

        /// <summary>
        /// 清理断线连接的核心逻辑：主动关闭连接、移除连接映射、清理指纹、调度 Despawn。<br/>
        /// 由 OnClientDisconnected（Closed 事件）和 CheckDisconnectedConnections（定时检测）调用。
        /// </summary>
        /// <param name="connectionId">连接ID</param>
        /// <param name="source">调用来源（用于日志诊断）</param>
        private async Task CleanupConnectionAsync(string connectionId, string source)
        {
            try
            {
                // 关键时序：先反查 characterId，再 RemoveConnectionAsync（会清理映射）。
                // 修复 BUG：_despawnScheduler 现在是必需依赖，不再需要 null 检查。
                // characterIdsToDespawn 始终从 ConnectionManager 获取，确保反查不会因 null 检查而跳过。
                IReadOnlyList<long> characterIdsToDespawn = _connectionManager.GetCharacterIdsByConnection(connectionId);

                // 主动关闭底层连接（确保 Socket 被关闭，触发 TouchSocket 内部清理）
                // 对于"空闲超时"判定的离线连接，Online 可能仍为 true，必须主动 Close 才能释放底层资源
                var connectionToClose = _connectionManager.GetConnection(connectionId);
                if (connectionToClose != null && connectionToClose.IsConnected)
                {
                    try { await connectionToClose.CloseAsync("服务器清理离线连接"); }
                    catch (Exception closeEx) { _logger.LogDebug(closeEx, "关闭连接时异常: {Id}", connectionId); }
                }

                // 清除指纹
                if (_fingerprintService != null)
                {
                    try { await _fingerprintService.ReleaseByConnectionAsync(connectionId); }
                    catch (Exception fpEx) { _logger.LogWarning(fpEx, "清理指纹失败: {Id}", connectionId); }
                }

                // 从连接管理器移除。返回 false 说明连接已被并发清理（Closed 事件与定时检测竞态）。
                bool removed = await _connectionManager.RemoveConnectionAsync(connectionId);

                // 修复 BUG（两周未解决的核心根因）：原实现使用 fire-and-forget 的 ScheduleDespawn，
                // ExecuteDespawnAsync 异步执行时可能因二次确认误判、异常吞掉、进程重启等原因从未完成，
                // 导致 GoOfflineAsync 从未被调用，CharacterGrain 持久化状态 IsOnline 永久卡在 true。
                // 修复：改为 await DespawnImmediatelyAsync 同步执行，确保 UnregisterEntityAsync +
                // RemoveSessionAsync + GoOfflineAsync 全部完成后再返回。
                if (characterIdsToDespawn.Count > 0)
                {
                    foreach (var characterId in characterIdsToDespawn)
                    {
                        try
                        {
                            await _despawnScheduler.DespawnImmediatelyAsync(characterId);
                        }
                        catch (Exception despawnEx)
                        {
                            _logger.LogWarning(despawnEx, "同步 Despawn 失败: 角色={CharacterId}", characterId);
                        }
                    }
                    _logger.LogInformation(
                        "连接 {Id} 清理完成（来源: {Source}, removed={Removed}），已完成 {Count} 个角色 Despawn",
                        connectionId, source, removed, characterIdsToDespawn.Count);
                }
                else
                {
                    _logger.LogWarning(
                        "连接 {Id} 未绑定任何角色，跳过 Despawn（来源: {Source}, removed={Removed}）",
                        connectionId, source, removed);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "清理连接 {Id} 时发生错误（来源: {Source}）", connectionId, source);
            }
        }

        /// <summary>
        /// 接收数据事件
        /// </summary>
        private async Task OnDataReceived(ITcpSessionClient client, ReceivedDataEventArgs e)
        {
            try
            {
                // 快速检测：如果连接已不在线，直接关闭以触发 OnClientDisconnected
                if (!client.Online)
                {
                    try { await client.CloseAsync("连接已离线"); } catch { /* 忽略 */ }
                    return;
                }

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

                    // 后备角色映射注册：从请求体中提取 CharacterId 并注册映射。
                    // SyncPacket 消息的 Header.CharacterId 通常为 0（CharacterId 在 InputPacket 载荷中），
                    // 因此需要解码 SyncFrameMessage 来提取。这是 Despawn 能反查出 characterId 的前提。
                    // 优先级：HandshakePacket.LocalCharacterId > InputPacket.CharacterId > Header.CharacterId
                    long fallbackCharacterId = messagePacket.Header.CharacterId != 0
                        ? (long)messagePacket.Header.CharacterId
                        : 0;

                    if (fallbackCharacterId == 0 && messagePacket.Body is SyncFrameMessage reqSyncFrame)
                    {
                        try
                        {
                            var reqSyncPacket = SyncPacketCodec.Decode(reqSyncFrame.Frame);
                            fallbackCharacterId = reqSyncPacket switch
                            {
                                HandshakePacket h => (long)h.LocalCharacterId,
                                InputPacket i => (long)i.CharacterId,
                                _ => 0
                            };
                        }
                        catch { /* 解码失败，忽略 */ }
                    }

                    if (fallbackCharacterId != 0)
                    {
                        var existingConn = _connectionManager.GetConnectionByCharacterId(fallbackCharacterId);
                        if (existingConn == null || existingConn.ConnectionId != connection.ConnectionId)
                        {
                            _connectionManager.RegisterCharacter(fallbackCharacterId, connection);
                            _despawnScheduler.CancelDespawn(fallbackCharacterId);
                            _logger.LogInformation(
                                "请求体补注册角色映射: CharacterId={CharacterId}, ConnectionId={ConnectionId}, MessageType={MessageType}",
                                fallbackCharacterId, connection.ConnectionId, messagePacket.Header.MessageType);
                        }
                    }

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
                        _logger.LogDebug("响应拦截: Type={ResponseType}, ConnectionId={ConnectionId}",
                            responseData.GetType().Name, connection.ConnectionId);

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
                        else if (responseData is EnterGameResponse enterResp)
                        {
                            // 更新鉴权令牌（可能为空，例如 _authTokenProvider 未配置或失败时）
                            if (!string.IsNullOrEmpty(enterResp.AuthToken))
                            {
                                connection.AuthToken = enterResp.AuthToken;
                                _logger.LogDebug("已更新连接 {Id} 的鉴权令牌（含角色Id）", client.Id);
                            }

                            // 绑定 characterId → IGameConnection 映射，使 fanout 推送（按 characterId 寻址）能命中本连接
                            // 注意：此绑定不能依赖 AuthToken 非空，否则令牌缺失时 fanout 推送的快照包会被全部丢弃
                            // 优先使用顶层 CharacterId，回退到 CharacterInfo.CharacterId
                            if (enterResp.Success)
                            {
                                var characterId = (long)(enterResp.CharacterId != 0
                                    ? enterResp.CharacterId
                                    : enterResp.CharacterInfo?.CharacterId ?? 0);
                                if (characterId != 0)
                                {
                                    _connectionManager.RegisterCharacter(characterId, connection);
                                    // 取消任何挂起的延迟 Despawn（断线重连场景：避免角色被误注销）
                                    _despawnScheduler.CancelDespawn(characterId);
                                    _logger.LogInformation(
                                        "EnterGame响应注册角色映射: CharacterId={CharacterId}, ConnectionId={ConnectionId}",
                                        characterId, connection.ConnectionId);
                                }
                                else
                                {
                                    _logger.LogWarning(
                                        "EnterGame响应成功但无法提取CharacterId。TopLevel={TopLevel}, CharacterInfo={CharacterInfo}",
                                        enterResp.CharacterId, enterResp.CharacterInfo?.CharacterId ?? 0);
                                }
                            }
                            else
                            {
                                _logger.LogWarning("EnterGame响应失败，跳过角色映射注册。Message={Message}", enterResp.Message);
                            }
                        }
                        else if (responseData is SyncFrameMessage syncFrameResp)
                        {
                            // SyncPacket 握手响应：解码 HandshakePacket 并注册 characterId → connection 映射
                            // 握手路径不经过 EnterGameResponse，若不在此注册，fanout 推送（按 characterId 寻址）会全部丢弃
                            try
                            {
                                var syncPacket = SyncPacketCodec.Decode(syncFrameResp.Frame);
                                _logger.LogDebug("SyncFrame响应解码: Kind={Kind}, ConnectionId={ConnectionId}",
                                    syncPacket.Kind, connection.ConnectionId);
                                if (syncPacket is HandshakePacket handshakeResp)
                                {
                                    if (handshakeResp.LocalCharacterId != 0)
                                    {
                                        var characterId = (long)handshakeResp.LocalCharacterId;
                                        _connectionManager.RegisterCharacter(characterId, connection);
                                        // 取消任何挂起的延迟 Despawn（断线重连场景：避免角色被误注销）
                                        _despawnScheduler.CancelDespawn(characterId);
                                        _logger.LogInformation(
                                            "Sync握手响应已注册角色映射: CharacterId={CharacterId}, ConnectionId={ConnectionId}",
                                            characterId, connection.ConnectionId);
                                    }
                                    else
                                    {
                                        _logger.LogWarning("Sync握手响应中 LocalCharacterId=0，跳过角色映射注册");
                                    }
                                }
                            }
                            catch (Exception decodeEx)
                            {
                                _logger.LogWarning(decodeEx, "解码 SyncFrame 响应失败，跳过角色映射注册");
                            }
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

                // 订阅 GameConnection 的 Closed 事件：这是断线清理的主要入口。
                // 在此处订阅可以确保 CleanupConnectionAsync 在 ConnectionManager.RemoveConnectionAsync 之前执行，
                // 从而保证 GetCharacterIdsByConnection 能正确反查出角色 ID 并调度 Despawn。
                gameConnection.Closed += async (s, e) =>
                {
                    await CleanupConnectionAsync(e.ConnectionId, source: "GameConnection.Closed");
                };

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
        /// <remarks>
        /// SyncPacket 免除网关层鉴权的原因：
        /// 1. 握手包（HandshakePacket）在玩家已登录并进入游戏后才发送，此时连接已通过鉴权；
        /// 2. 输入包（InputPacket）携带的是移动/操作数据，鉴权已在登录阶段完成；
        /// 3. SyncPacketHandler 内部已有自己的安全校验逻辑（如检查 CharacterId、协议版本等）；
        /// 4. 在 HorizonMessagePacket 层面无法区分具体是哪种 SyncPacket，因此统一免除，由 Handler 内部做细粒度校验。
        /// </remarks>
        private static bool IsAuthExemptMessage(HorizonMessagePacket message)
        {
            return message.Header.MessageType == MessageType.LoginRequest
                || message.Header.MessageType == MessageType.RegisterRequest
                || message.Header.MessageType == MessageType.TokenLoginRequest
                || message.Header.MessageType == MessageType.SyncPacket;
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
using Horizon.Game.Core;
using Horizon.Game.Core.Interfaces;
using Horizon.Game.Core.Sim.Server;
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
using System.Collections.Concurrent;
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
        private readonly ICharacterPresenceStore _presenceStore;
        private TcpService? _tcpService;
        private volatile bool _isRunning;
        private HorizonMessageAdapter _adapter;
        private readonly SemaphoreSlim _connectionRegistrationGate = new(1, 1);
        private Timer? _disconnectCheckTimer;
        private Timer? _leaseRenewalTimer;

        // ── 诊断日志频率限制 ──
        // 数据接收和空闲检测的日志限频，避免每条消息都输出日志导致刷屏
        private long _lastReceiveDiagTicks = DateTime.MinValue.Ticks;
        private static readonly TimeSpan ReceiveDiagInterval = TimeSpan.FromSeconds(10);
        private long _lastIdleCheckDiagTicks = DateTime.MinValue.Ticks;
        private static readonly TimeSpan IdleCheckDiagInterval = TimeSpan.FromSeconds(30);

        /// <summary>
        /// 已清理连接的幂等保护集合。<br/>
        /// 修复 BUG：Closed 事件会被两个订阅路径同时触发：<br/>
        /// 1. <see cref="OnClientDisconnected"/>（订阅 <c>_tcpService.Closed</c>，source=ClosedEvent）<br/>
        /// 2. <see cref="EnsureConnectionRegisteredAsync"/> 中的 lambda（订阅 <c>GameConnection.Closed</c>，source=ClosedEvent）<br/>
        /// GameConnection 内部在 <c>_client.Closed</c> 触发时会 Invoke 自身的 Closed 事件，
        /// 因此一次断开会同时触发两条清理路径，导致指纹重复清理、重复日志、潜在 Despawn 竞态。<br/>
        /// 此集合确保同一连接的 CleanupConnectionAsync 只完整执行一次，第二次调用直接返回。
        /// </summary>
        private readonly ConcurrentDictionary<string, byte> _cleanedConnections = new();

        /// <summary>
        /// Presence TTL 兜底刷新服务（Phase 3.2 提取为独立服务）。
        /// </summary>
        private readonly PresenceRefreshService _presenceRefreshService;
        public GameNetworkServer(
            ILogger<GameNetworkServer> logger,
            ILog tlogger,
            IOptionsMonitor<NetworkOptions> networkOptions,
            IOptionsMonitor<GatewayOptions> gatewayOptions,
            IConnectionManager connectionManager,
            IEnumerable<IMessageHandler> messageHandlers, HorizonMessageAdapter adapter,
            PlayerDespawnScheduler despawnScheduler,
            ICharacterPresenceStore presenceStore,
            PresenceRefreshService presenceRefreshService,
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
            _despawnScheduler = despawnScheduler ?? throw new ArgumentNullException(nameof(despawnScheduler));
            _presenceStore = presenceStore ?? throw new ArgumentNullException(nameof(presenceStore));
            _presenceRefreshService = presenceRefreshService ?? throw new ArgumentNullException(nameof(presenceRefreshService));
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

                // 启动断线检测定时器：每 1 秒遍历所有连接，检测 IsConnected==false 的连接并主动清理。
                // TouchSocket 的 Closed 事件在客户端非正常断开（直接关进程/断网）时可能不被及时触发，
                // 此定时器作为后备机制，确保断线角色最终被 Despawn。
                // 修复 BUG（首包超时检测耗时 11-12 秒而非 5 秒）：
                // 原实现间隔 5 秒 + 严格 > 比较，当连接建立时间恰好使 sinceConnect 在第一次检测时
                // 等于 FirstPacketTimeoutSeconds（5s）时，5>5=false 不触发，需等到下一次检测（10s），
                // 加上连接建立与定时器触发的偏移，实测 11-12 秒。
                // 修复：间隔改为 1 秒（更及时），配合 >= 比较运算符，确保检测延迟不超过 1 秒。
                _disconnectCheckTimer = new Timer(
                    CheckDisconnectedConnections, null,
                    TimeSpan.FromSeconds(1),
                    TimeSpan.FromSeconds(1));

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
        /// 即使 Socket 反射查找失败也绝对不主动关闭连接，避免误杀健康连接（客户端 500ms 断线风暴的根源）。
        /// </summary>
        private void TrySetKeepAlive(ITcpSessionClient client, bool isRetry = false)
        {
            try
            {
                var socket = FindSocketMember(client, depth: 0);
                if (socket == null)
                {
                    if (!isRetry)
                    {
                        // 首次连接时 Socket 可能尚未初始化（TouchSocket 内部延迟创建），
                        // 延迟 500ms 后重试一次。若仍失败则降级到应用层心跳超时检测。
                        _ = Task.Run(async () =>
                        {
                            await Task.Delay(500);
                            if (client.Online)
                            {
                                TrySetKeepAlive(client, isRetry: true);
                            }
                        });
                        return;
                    }

                    // [修复] 重试后 Socket 仍为 null：不加 KeepAlive，依靠应用层心跳检测断线。
                    // 注意：绝对不能主动关闭连接！此前逻辑在重试失败后执行 client.CloseAsync()，
                    // 导致所有 FindSocketMember 反射查找失败的连接在 500ms 后被服务端主动关闭，
                    // 客户端因此陷入「重连成功→500ms 断线→重连」的死循环。
                    // 修复 BUG（日志噪音）：TouchSocket 4.x 的 TcpSessionClient 内部结构变化，
                    // 反射查找 Socket 路径不匹配，每条连接必现此警告。由于 TCP KeepAlive 是辅助机制
                    // （核心断线检测依赖 CheckDisconnectedConnections 的应用层心跳超时），
                    // 将日志级别降为 Debug，避免日志噪音。
                    _logger.LogDebug(
                        "客户端 {Id} 未找到底层 Socket，TCP KeepAlive 设置失败（反射路径不匹配），"
                        + "断线检测降级到应用层心跳超时",
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
                _logger.LogWarning(ex, "设置 KeepAlive 失败: {Id}（断线检测降级到应用层心跳超时）", client.Id);
            }
        }

        /// <summary>
        /// 兜底刷新角色 presence TTL（委托给 PresenceRefreshService）。
        /// </summary>
        /// <param name="characterId">角色 ID。</param>
        private void TryRefreshPresenceTtlInBackground(long characterId)
        {
            _presenceRefreshService.TryRefreshInBackground(characterId);
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
                await CleanupConnectionAsync(client.Id, source: ConnectionCleanupSource.ClosedEvent);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理客户端断开连接事件时发生错误: {Id}", client.Id);
            }
        }

        /// <summary>
        /// 定时检测断线连接（每 5 秒触发）。<br/>
        /// 检测三种断线判定：<br/>
        /// 1. <see cref="IGameConnection.IsConnected"/>==false：TouchSocket 底层判定（依赖 TCP 层 RST/FIN 或 KeepAlive 探测）；<br/>
        /// 2. 首包超时：连接建立后从未收到数据（<see cref="IGameConnection.LastActiveTime"/>==<see cref="IGameConnection.ConnectedTime"/>），
        ///    且超过 <see cref="NetworkOptions.FirstPacketTimeoutSeconds"/> 秒，判定为幽灵连接（探测/错误连接/客户端崩溃）；<br/>
        /// 3. <see cref="IGameConnection.LastActiveTime"/> 空闲超时：应用层心跳超时判定，超过 <see cref="NetworkOptions.IdleTimeoutSeconds"/> 未收到任何数据。<br/>
        /// <para>
        /// 关键说明：检测 3 是检测客户端非正常断开（关进程/断网）的最可靠机制。
        /// TCP KeepAlive 在 TouchSocket 中需要反射访问底层 Socket，可能失败；
        /// <c>Online</c> 属性在 TCP 层未探测到断开时会一直保持 true（Windows 默认 keepalive 长达 2 小时）。
        /// 而客户端有 20 秒心跳，超过 60 秒无数据必然是非正常断开。
        /// 检测 2 用于快速清理幽灵连接（连接后不发送任何数据），避免占用连接管理器资源 60 秒。
        /// </para>
        /// </summary>
        private async void CheckDisconnectedConnections(object? state)
        {
            try
            {
                var idleTimeout = TimeSpan.FromSeconds(_networkOptions.CurrentValue.IdleTimeoutSeconds);
                var firstPacketTimeout = TimeSpan.FromSeconds(_networkOptions.CurrentValue.FirstPacketTimeoutSeconds);
                var now = DateTime.UtcNow;

                // 修复 BUG：原实现串行 await CleanupConnectionAsync，单个连接的 DespawnImmediatelyAsync
                // 阻塞（grain 调用超时 30 秒）会导致后续连接检测延迟，最坏情况下 N 个断线连接
                // 需要 N × 30 秒才能全部清理，期间这些连接的角色在 silo 端仍被 RenewAllLeasesAsync 续约
                // （因为 RenewAllLeasesAsync 通过 ConnectionManager.GetAllCharacterIds 获取列表，只要连接
                // 还在管理器中就会续约），导致"网关运行时离线角色无法正常离线"。
                // 修复：先收集所有需要清理的连接，然后并行执行 CleanupConnectionAsync，互不阻塞。
                // 线程安全：CleanupConnectionAsync 内部所有操作（RemoveConnectionAsync、DespawnImmediatelyAsync）
                // 都按 connectionId/characterId 独立操作，不依赖全局状态，并行执行安全。
                var connectionsToCleanup = new List<(string ConnectionId, ConnectionCleanupSource Source)>();

                // ── 连接状态汇总日志（每30秒最多一次）──
                // 记录所有在线连接的空闲时长，方便排查空闲超时和心跳问题
                var idleDiagNowTicks = now.Ticks;
                if (idleDiagNowTicks - Interlocked.Read(ref _lastIdleCheckDiagTicks) >= IdleCheckDiagInterval.Ticks)
                {
                    Interlocked.Exchange(ref _lastIdleCheckDiagTicks, idleDiagNowTicks);
                    var allConns = _connectionManager.GetAllConnections().ToList();
                    if (allConns.Count > 0)
                    {
                        var connSummaries = allConns.Select(c =>
                            $"[{c.ConnectionId} idle={(now - c.LastActiveTime).TotalSeconds:F1}s remote={c.RemoteAddress}]");
                        _logger.LogInformation(
                            "[NetDiag] 连接状态汇总：TotalConnections={Count}, IdleTimeout={TimeoutS}s, Connections={Connections}",
                            allConns.Count, _networkOptions.CurrentValue.IdleTimeoutSeconds,
                            string.Join(", ", connSummaries));
                    }
                }

                foreach (var connection in _connectionManager.GetAllConnections())
                {
                    // 检测 1：TouchSocket Online 属性判定（依赖 TCP 层检测，可能不及时）
                    if (!connection.IsConnected)
                    {
                        connectionsToCleanup.Add((connection.ConnectionId, ConnectionCleanupSource.ClosedEvent));
                        continue;
                    }

                    // 检测 2：首包超时判定（幽灵连接检测）
                    // 连接建立后从未收到数据（LastActiveTime ≈ ConnectedTime），且达到首包超时时间，
                    // 判定为幽灵连接（探测/错误连接/客户端崩溃后未关闭 Socket），立即清理。
                    // 使用容差比较而非严格相等：即使 GameConnection 构造函数中只用了一次 DateTime.UtcNow
                    // 同时赋值给两者，DateTime 精度在跨平台/高负载下仍可能出现亚毫秒级偏差，严格相等可能误判。
                    // 修复 BUG（首包超时检测耗时 11-12 秒）：
                    // 原实现使用 > 严格比较，sinceConnect 恰好等于 FirstPacketTimeoutSeconds 时不触发，
                    // 需等待下一次定时器触发。改为 >= 后，达到阈值立即触发。
                    bool neverReceivedData = (connection.LastActiveTime - connection.ConnectedTime).TotalMilliseconds < 100;
                    if (neverReceivedData)
                    {
                        var sinceConnect = now - connection.ConnectedTime;
                        if (sinceConnect >= firstPacketTimeout)
                        {
                            _logger.LogWarning(
                                "连接 {Id} 首包超时（{Seconds:F0}秒未收到任何数据），判定为幽灵连接并清理。Connected={Connected:O}, Remote={Remote}",
                                connection.ConnectionId, sinceConnect.TotalSeconds, connection.ConnectedTime, connection.RemoteAddress);
                            connectionsToCleanup.Add((connection.ConnectionId, ConnectionCleanupSource.FirstPacketTimeout));
                            continue;
                        }
                    }

                    // 检测 3：应用层心跳超时判定（最可靠，不依赖 TCP KeepAlive）
                    // 客户端心跳间隔约 20 秒，超过 IdleTimeoutSeconds（默认 30 秒）未收到任何数据，
                    // 说明客户端已非正常断开（关进程/断网），需要主动清理。
                    // 修复：使用 >= 替代 >，达到阈值立即触发，避免额外一个定时器周期的延迟。
                    var idleDuration = now - connection.LastActiveTime;
                    if (idleDuration >= idleTimeout)
                    {
                        _logger.LogWarning(
                            "连接 {Id} 空闲超时（{Seconds:F0}秒无数据），判定为离线并清理。LastActive={LastActive:O}, Remote={Remote}",
                            connection.ConnectionId, idleDuration.TotalSeconds, connection.LastActiveTime, connection.RemoteAddress);
                        connectionsToCleanup.Add((connection.ConnectionId, ConnectionCleanupSource.IdleTimeout));
                    }
                }

                if (connectionsToCleanup.Count > 0)
                {
                    var cleanupTasks = new List<Task>(connectionsToCleanup.Count);
                    foreach (var (connectionId, source) in connectionsToCleanup)
                    {
                        cleanupTasks.Add(CleanupConnectionAsync(connectionId, source));
                    }
                    await Task.WhenAll(cleanupTasks);
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
        /// <param name="source">调用来源（连接清理来源枚举，用于日志诊断与治理统计）</param>
        private async Task CleanupConnectionAsync(string connectionId, ConnectionCleanupSource source)
        {
            // 幂等保护：Closed 事件会被 _tcpService.Closed 与 GameConnection.Closed 两条路径同时触发，
            // 同一连接只允许完整清理一次。第二次调用直接返回，避免重复清理指纹、重复日志、潜在 Despawn 竞态。
            // 标志在连接被移除后保留（不立即 TryRemove），防止并发重入；定时器下一轮不会再扫到该连接。
            if (!_cleanedConnections.TryAdd(connectionId, 0))
            {
                _logger.LogDebug(
                    "连接 {Id} 已在清理中/已清理，跳过重复清理（来源: {Source}）", connectionId, source);
                return;
            }

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

                // [连接精简治理 spec 6.3] 按来源枚举累加治理统计：
                // 首包超时（幽灵连接）清理次数、损坏连接次数、重复/超限拒绝次数。
                _connectionManager.RecordCleanup(source);

                // 修复 BUG（远程角色周期性地 Despawn+Spawn 循环 — 闪退）：
                // 原实现使用 DespawnImmediatelyAsync 立即执行 Despawn，客户端短暂断线后重连时
                // 实体已被从 ZoneShardGrain._simulatedEntities 移除，EnterWorldAsync 无法找到
                // 已存在的实体，走 RegisterEntityAsync 重新 Spawn，导致其他客户端看到远程角色闪退。
                // 修复：改用 ScheduleDespawn（带 15 秒宽限期），客户端在宽限期内重连时
                // StageCharacterMappingAndPresence 会调用 CancelDespawn 取消待执行的 Despawn，
                // EnterWorldAsync 发现实体仍存在，跳过 Despawn+Spawn 仅更新 AOI 订阅。
                //
                // GoOfflineAsync 的实现在 DoDespawnCoreAsync 的 finally 块中，即使 Despawn 被取消
                // 也不会执行（因为 DoDespawnCoreAsync 不会被调用）。但客户端重连时 StageResponseIntercept
                // 会重新进入游戏，CharacterGrain 的 IsOnline 状态保持为 true，这是正确的行为。
                if (characterIdsToDespawn.Count > 0)
                {
                    foreach (var characterId in characterIdsToDespawn)
                    {
                        // 清理 presence 兜底刷新时间记录，避免内存泄漏
                        _presenceRefreshService.RemoveCharacter(characterId);

                        // 使用 ScheduleDespawn（带宽限期）替代 DespawnImmediatelyAsync，避免闪退循环。
                        // ScheduleDespawn 是 fire-and-forget，不会 throw，因此不需要 try-catch。
                        // 宽限期（15秒）内客户端重连可取消 Despawn，宽限期后执行 DoDespawnCoreAsync
                        // 完成 UnregisterEntityAsync + RemoveSessionAsync + GoOfflineAsync。
                        _despawnScheduler.ScheduleDespawn(characterId);
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

                // [连接精简治理 spec 4.4.1a] 结构化清理日志。
                _logger.LogInformation(
                    "[ConnectionCleanup] Id={Id} Source={Source} Characters={Count} Result={Result}",
                    connectionId, source, characterIdsToDespawn.Count, removed ? "Cleaned" : "AlreadyRemoved");
            }
            catch (Exception ex)
            {
                // 清理失败时移除幂等标志，允许定时器下一轮重试。
                // 重试是安全的：GetCharacterIdsByConnection 在 RemoveConnectionAsync 之后会返回空列表，
                // DespawnImmediatelyAsync 内部对重复调用安全（grain 调用幂等）。
                _cleanedConnections.TryRemove(connectionId, out _);
                _logger.LogError(ex, "清理连接 {Id} 时发生错误（来源: {Source}）", connectionId, source);
            }

            // 延迟清理幂等标志，防止 _cleanedConnections 无限增长（内存泄漏修复）。
            // 60 秒窗口足以覆盖并发重入场景（Closed 事件 + 定时检测竞态），之后安全移除。
            _ = Task.Delay(TimeSpan.FromSeconds(60)).ContinueWith(t =>
            {
                _cleanedConnections.TryRemove(connectionId, out _);
            }, TaskScheduler.Default);
        }

        /// <summary>
        /// 接收数据事件（薄编排层，各阶段逻辑已提取为独立私有方法）。
        /// </summary>
        private async Task OnDataReceived(ITcpSessionClient client, ReceivedDataEventArgs e)
        {
            try
            {
                // Stage 1: 连接确保（补注册）
                var connection = await StageConnectionEnsureAsync(client);
                if (connection == null) return;

                // Stage 2: 活跃时间更新
                connection.LastActiveTime = DateTime.UtcNow;

                try
                {
                    // 解析消息帧
                    if (e.RequestInfo is not HorizonMessageInfo horizonRequest || horizonRequest.Packet == null)
                    {
                        _logger.LogWarning("收到无法解析的消息帧: {Id}", client.Id);
                        return;
                    }

                    var messagePacket = horizonRequest.Packet;

                    // ── 数据接收诊断日志（每10秒最多一次）──
                    // 记录最后活跃时间、消息类型和角色ID，方便排查空闲超时和延迟问题
                    var receiveNowTicks = DateTime.UtcNow.Ticks;
                    if (receiveNowTicks - Interlocked.Read(ref _lastReceiveDiagTicks) >= ReceiveDiagInterval.Ticks)
                    {
                        Interlocked.Exchange(ref _lastReceiveDiagTicks, receiveNowTicks);
                        _logger.LogInformation(
                            "[NetDiag] 数据接收：ConnectionId={ConnectionId}, MessageType={MessageType}, CharacterId={CharacterId}, LastActive={LastActive:O}, Remote={Remote}",
                            connection.ConnectionId, messagePacket.Header.MessageType, messagePacket.Header.CharacterId,
                            connection.LastActiveTime, connection.RemoteAddress);
                    }

                    // Stage 3: 角色映射注册 + Presence 兜底刷新
                    StageCharacterMappingAndPresence(connection, messagePacket);

                    // Stage 4: 鉴权令牌验证
                    if (!await StageAuthValidationAsync(client, messagePacket)) return;

                    // Stage 5: 消息路由分发
                    var (isSuccess, responseData) = await ProcessMessageAsync(client, messagePacket);

                    // Stage 6: 响应拦截（令牌更新/角色映射）
                    if (isSuccess && responseData != null)
                    {
                        StageResponseIntercept(connection, client, responseData);
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

        /// <summary>
        /// Stage 1: 连接确保——如果连接不存在则补注册。
        /// </summary>
        private async Task<IGameConnection?> StageConnectionEnsureAsync(ITcpSessionClient client)
        {
            // 快速检测：如果连接已不在线，直接关闭以触发 OnClientDisconnected
            if (!client.Online)
            {
                try { await client.CloseAsync("连接已离线"); } catch { /* 忽略 */ }
                return null;
            }

            var connection = _connectionManager.GetConnection(client.Id);
            if (connection == null)
            {
                connection = await EnsureConnectionRegisteredAsync(client, logOnCreate: true);
                if (connection == null)
                {
                    _logger.LogWarning("收到数据但连接不存在，且无法补注册: {Id}", client.Id);
                }
            }
            return connection;
        }

        /// <summary>
        /// Stage 3: 角色映射注册（从请求体提取 CharacterId 并注册映射）+ Presence TTL 兜底刷新。
        /// </summary>
        private async void StageCharacterMappingAndPresence(IGameConnection connection, HorizonMessagePacket messagePacket)
        {
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
                    // 修复（角色映射弹跳）：当已有不同连接持有该角色映射且仍在线时，
                    // 仅允许 HandshakePacket 覆盖映射（握手是新会话的权威信号）。
                    // InputPacket 不覆盖，防止旧连接的残留重传反复抢夺映射，
                    // 导致 fanout 快照包被投递到错误连接（根因：其他客户端看不到角色移动）。
                    bool isHandshake = messagePacket.Body is SyncFrameMessage hsFrame
                        && TryDecodeAsHandshake(hsFrame);
                    bool existingStillAlive = existingConn is { IsConnected: true };

                    // 修复 BUG（重连死循环：每 20 秒一次的"关闭连接→重连→再关闭"循环）：
                    // 原条件 `!existingStillAlive || isHandshake || existingConn == null` 在
                    // existingConn==null 时任何消息都会补注册映射。当实体已从 ZoneShardGrain
                    // 中消失（被 DespawnImmediatelyAsync 清理）后，客户端重连发送 InputPacket
                    // 仍会补注册映射，下次续约再次检测到"实体不在 ZoneShardGrain 中但
                    // ConnectionManager 仍有映射"→关闭连接→客户端重连→循环。
                    // 修复：existingConn==null 时，只有 HandshakePacket 才补注册映射
                    // （握手会触发 EnterWorldAsync 重新 Spawn 实体到 ZoneShardGrain）。
                    // InputPacket 等其他消息不补注册，客户端必须重新握手才能恢复。
                    bool shouldRegister = isHandshake
                        || (existingConn != null && !existingStillAlive);

                    if (shouldRegister)
                    {
                        _connectionManager.RegisterCharacter(fallbackCharacterId, connection);
                        _despawnScheduler.CancelDespawn(fallbackCharacterId);

                        // 修复（重连后"角色已在线" — 旧 fingerprint 未释放）：
                        // 真正断网后客户端 30s 心跳超时触发重连，但服务端 IdleTimeout=30s 可能尚未清理旧连接。
                        // 旧连接的 fingerprint（TTL 5min）仍存在于 Redis 中，新连接 EnterGame 时 TryAcquire 失败。
                        // HandshakePacket 是新会话的权威信号，此处主动释放旧 fingerprint，
                        // 确保后续 EnterGame 的 TryAcquire 能成功。
                        if (isHandshake && _fingerprintService != null)
                        {
                            try
                            {
                                await _fingerprintService.ReleaseAsync(fallbackCharacterId);
                                _logger.LogInformation(
                                    "HandshakePacket 触发释放旧 fingerprint: CharacterId={CharacterId}",
                                    fallbackCharacterId);
                            }
                            catch (Exception fpEx)
                            {
                                _logger.LogWarning(fpEx,
                                    "HandshakePacket 释放旧 fingerprint 失败（忽略）: CharacterId={CharacterId}",
                                    fallbackCharacterId);
                            }
                        }
                        _logger.LogInformation(
                            "请求体补注册角色映射: CharacterId={CharacterId}, ConnectionId={ConnectionId}, MessageType={MessageType}, IsHandshake={IsHandshake}",
                            fallbackCharacterId, connection.ConnectionId, messagePacket.Header.MessageType, isHandshake);
                    }
                    else
                    {
                        // 映射不存在且非握手：拒绝补注册。
                        // 实体可能已从 ZoneShardGrain 中消失（DespawnImmediatelyAsync 已清理映射），
                        // 客户端必须重新握手才能恢复（握手会触发 EnterWorldAsync 重新 Spawn 实体）。
                        _logger.LogWarning(
                            "角色映射不存在且非握手消息，拒绝补注册（需重新握手）。CharacterId={CharacterId}, ConnectionId={ConnectionId}, MessageType={MessageType}",
                            fallbackCharacterId, connection.ConnectionId, messagePacket.Header.MessageType);
                    }
                }

                // 修复 BUG（心跳 TTL 反复过低）：客户端只发送 InputPacket 不发送 Heartbeat 消息，
                // 导致 Redis presence TTL 在 90 秒后过期。此处作为兜底机制：
                // 收到已绑定角色的数据时，每 30 秒刷新一次 presence TTL，
                // 确保只要客户端在发送输入，角色在线状态就不会过期。
                // 不阻塞消息处理（fire-and-forget + 异常吞并）。
                TryRefreshPresenceTtlInBackground(fallbackCharacterId);
            }
        }

        /// <summary>
        /// Stage 4: 鉴权令牌验证。返回 true 表示通过，false 表示拒绝（已发送错误响应）。
        /// </summary>
        private async Task<bool> StageAuthValidationAsync(ITcpSessionClient client, HorizonMessagePacket messagePacket)
        {
            // 验证消息头的必需字段
            if (!messagePacket.Header.IsResponse)
            {
                if (messagePacket.Header.GameId <= 0)
                {
                    _logger.LogWarning("收到无效消息: GameId必须为正数. Client: {Id}", client.Id);
                    return false;
                }

                if (messagePacket.Header.ServerId <= 0)
                {
                    _logger.LogWarning("收到无效消息: ServerId必须为正数. Client: {Id}", client.Id);
                    return false;
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
                    return false;
                }

                var machineId = messagePacket.Header.MachineId;
                var expectedMachineId = _gatewayOptions.CurrentValue.ValidateTokenMachineId ? machineId : null;
                var validationResult = _authTokenProvider.ValidateToken(authToken, expectedMachineId: expectedMachineId);
                if (!validationResult.IsValid)
                {
                    _logger.LogWarning("鉴权令牌验证失败，拒绝服务. Client: {Id}, Reason: {Reason}, MessageType: {MessageType}", 
                        client.Id, validationResult.ErrorMessage, messagePacket.Header.MessageType);
                    await SendAuthErrorAsync(client, messagePacket, "鉴权失败，请重新登录");
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Stage 6: 响应拦截——从响应中提取鉴权令牌并存储到连接，以及注册角色映射。
        /// </summary>
        private void StageResponseIntercept(IGameConnection connection, ITcpSessionClient client, MessageUnion responseData)
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
                    else if (syncPacket is HandshakeRejectPacket rejectResp)
                    {
                        // P1.3：协议版本过低，服务器拒绝握手——不注册映射，客户端将触发强制更新。
                        _logger.LogWarning(
                            "Sync握手被拒绝（协议版本过低）: Reason={Reason}, MinimumVersion={MinimumVersion}, ConnectionId={ConnectionId}",
                            rejectResp.Reason, rejectResp.MinimumVersion, connection.ConnectionId);
                    }
                }
                catch (Exception decodeEx)
                {
                    _logger.LogWarning(decodeEx, "解码 SyncFrame 响应失败，跳过角色映射注册");
                }
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

                // [连接精简治理 spec 3.4] 接入连接数三级约束（全局→每 IP→每用户）。
                // 拒绝时向被拒连接发送明确提示并关闭，映射清理来源枚举后记录。
                var rejectReason = await _connectionManager.TryAcquireConnectionSlotAsync(
                    gameConnection, gameConnection.RemoteAddress, gameConnection.UserId);
                if (rejectReason != null)
                {
                    var rejectSource = MapRejectToCleanupSource(rejectReason);
                    _logger.LogWarning(
                        "[连接治理] 连接被拒绝: Id={Id}, Reason={Reason}, Remote={Remote}, Source={Source}",
                        client.Id, rejectReason, gameConnection.RemoteAddress, rejectSource);
                    // 向被拒连接发送明确拒绝提示（复用错误消息通道，尽力而为）。
                    try { await SendConnectionRejectedNoticeAsync(gameConnection, rejectReason); }
                    catch (Exception sendEx) { _logger.LogDebug(sendEx, "发送连接拒绝提示失败: {Id}", client.Id); }
                    // 关闭底层连接（触发 Closed 事件，但该连接未注册进 _connections，映射由拒绝路径清理来源统计）。
                    try { await gameConnection.CloseAsync(rejectReason); }
                    catch (Exception closeEx) { _logger.LogDebug(closeEx, "关闭被拒连接异常: {Id}", client.Id); }
                    _connectionManager.RecordCleanup(rejectSource);
                    return _connectionManager.GetConnection(client.Id);
                }

                // 订阅 GameConnection 的 Closed 事件：这是断线清理的主要入口。
                // 在此处订阅可以确保 CleanupConnectionAsync 在 ConnectionManager.RemoveConnectionAsync 之前执行，
                // 从而保证 GetCharacterIdsByConnection 能正确反查出角色 ID 并调度 Despawn。
                gameConnection.Closed += async (s, e) =>
                {
                    await CleanupConnectionAsync(e.ConnectionId, source: ConnectionCleanupSource.ClosedEvent);
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
        /// 将连接拒绝原因映射为清理来源枚举（连接精简治理 spec 4.3.1a）。
        /// 用于被拒连接关闭时按原因记录治理统计与清理日志。
        /// </summary>
        private static ConnectionCleanupSource MapRejectToCleanupSource(string rejectReason)
        {
            if (rejectReason.Contains("每IP", StringComparison.OrdinalIgnoreCase)) return ConnectionCleanupSource.PerIpLimit;
            if (rejectReason.Contains("每用户", StringComparison.OrdinalIgnoreCase)) return ConnectionCleanupSource.PerUserLimit;
            if (rejectReason.Contains("已满", StringComparison.OrdinalIgnoreCase)) return ConnectionCleanupSource.ConnectionLimit;
            return ConnectionCleanupSource.ConnectionLimit;
        }

        /// <summary>
        /// 向被拒连接发送明确拒绝提示（复用错误消息通道，尽力而为）。
        /// 在关闭底层连接前调用，使客户端可区分拒绝原因（连接数已满/每IP超限/每用户超限）。
        /// </summary>
        private async Task SendConnectionRejectedNoticeAsync(IGameConnection connection, string reason)
        {
            // 构造应用层错误消息：使用 AuthenticationError 通道（与既有认证失败提示一致）。
            var errorResponse = new AuthenticationError
            {
                ErrorCode = 4001,
                ErrorMessage = reason,
                ErrorDetails = "连接数约束：请等待现有连接清理后重试",
                RetryAfterSeconds = 5,
                RequiresReconnect = false,
            };
            var errorPacket = new HorizonMessagePacket
            {
                Header = new MessageHeader
                {
                    MessageId = Guid.NewGuid().ToString(),
                    MessageType = MessageType.Error,
                    ServiceType = ServiceType.Account,
                    IsResponse = false,
                    RequireResponse = false,
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                },
                ServiceType = ServiceType.Account,
                Body = errorResponse,
                RawData = MemoryPackSerializer.Serialize(errorResponse),
            };

            var frame = _adapter.PackPacket(errorPacket);
            await connection.SendAsync(frame);
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
        /// 尝试将 SyncFrameMessage 解码为 HandshakePacket（用于判断是否为握手请求）。
        /// 解码失败或非 HandshakePacket 时返回 false。
        /// </summary>
        private static bool TryDecodeAsHandshake(SyncFrameMessage syncFrame)
        {
            try
            {
                var packet = SyncPacketCodec.Decode(syncFrame.Frame);
                return packet is HandshakePacket;
            }
            catch
            {
                return false;
            }
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

                // 修复 BUG（Writing is not allowed after writer was completed）：
                // 原实现直接调用 client.SendAsync，绕过了 GameConnection._sendLock，
                // 与并发的 GameConnection.SendAsync（如快照分发/心跳推送）冲突，导致并发写入
                // TouchSocket pipeline，触发 InvalidOperationException。
                // 修复：通过 GameConnection.SendAsync 发送，复用其 _sendLock 串行化保护。
                // 连接在 StageConnectionEnsureAsync 中已确保注册，此处直接查询。
                var connection = _connectionManager.GetConnection(client.Id);
                if (connection != null)
                {
                    await connection.SendAsync(buff);
                }
                else
                {
                    // 极端场景：连接已被清理，直接发送（可能失败，但鉴权错误本身可忽略）
                    await client.SendAsync(buff);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "发送鉴权错误消息失败: {Id}", client.Id);
            }
        }

    }

    }
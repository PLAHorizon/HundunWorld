using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
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
using TouchSocket.Sockets;

namespace Horizon.Game.Gateway.Network;

/// <summary>
/// 连接生命周期管理（阶段 3 从 <see cref="GameNetworkServer"/> 拆分）：<br/>
/// 负责 TCP 连接建立/断开后的登记、KeepAlive 设置、断线检测（幽灵连接/空闲超时）、
/// 清理（指纹释放/角色 Despawn 调度）与连接补注册。数据接收流水线见
/// <see cref="ClientPacketPipeline"/>；<see cref="GameNetworkServer"/> 仅保留
/// TcpService 生命周期与事件接线。
/// </summary>
public sealed class ConnectionLifecycleManager
{
    private readonly ILogger<ConnectionLifecycleManager> _logger;
    private readonly IOptionsMonitor<NetworkOptions> _networkOptions;
    private readonly IConnectionManager _connectionManager;
    private readonly ICharacterFingerprintService? _fingerprintService;
    private readonly PlayerDespawnScheduler _despawnScheduler;
    private readonly PresenceRefreshService _presenceRefreshService;
    private readonly HorizonMessageAdapter _adapter;
    private readonly SemaphoreSlim _connectionRegistrationGate = new(1, 1);
    private Timer? _disconnectCheckTimer;
    private Timer? _leaseRenewalTimer;

    // ── 诊断日志频率限制 ──
    private long _lastIdleCheckDiagTicks = DateTime.MinValue.Ticks;
    private static readonly TimeSpan IdleCheckDiagInterval = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 已清理连接的幂等保护集合。<br/>
    /// 修复 BUG：Closed 事件会被两个订阅路径同时触发：<br/>
    /// 1. <see cref="HandleDisconnectedAsync"/>（订阅 <c>_tcpService.Closed</c>，source=ClosedEvent）<br/>
    /// 2. <see cref="EnsureConnectionRegisteredAsync"/> 中的 lambda（订阅 <c>GameConnection.Closed</c>，source=ClosedEvent）<br/>
    /// GameConnection 内部在 <c>_client.Closed</c> 触发时会 Invoke 自身的 Closed 事件，<br/>
    /// 因此一次断开会同时触发两条清理路径，导致指纹重复清理、重复日志、潜在 Despawn 竞态。<br/>
    /// 此集合确保同一连接的 CleanupConnectionAsync 只完整执行一次，第二次调用直接返回。
    /// </summary>
    private readonly ConcurrentDictionary<string, byte> _cleanedConnections = new();

    public ConnectionLifecycleManager(
        ILogger<ConnectionLifecycleManager> logger,
        IOptionsMonitor<NetworkOptions> networkOptions,
        IConnectionManager connectionManager,
        PlayerDespawnScheduler despawnScheduler,
        PresenceRefreshService presenceRefreshService,
        HorizonMessageAdapter adapter,
        ICharacterFingerprintService? fingerprintService = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _networkOptions = networkOptions ?? throw new ArgumentNullException(nameof(networkOptions));
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
        _despawnScheduler = despawnScheduler ?? throw new ArgumentNullException(nameof(despawnScheduler));
        _presenceRefreshService = presenceRefreshService ?? throw new ArgumentNullException(nameof(presenceRefreshService));
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _fingerprintService = fingerprintService;
    }

    /// <summary>启动断线检测与租约续约定时器（由 <see cref="GameNetworkServer.StartAsync"/> 调用）。</summary>
    public void StartTimers()
    {
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

    /// <summary>停止断线检测与租约续约定时器（由 <see cref="GameNetworkServer.StopAsync"/> 调用）。</summary>
    public void StopTimers()
    {
        _disconnectCheckTimer?.Dispose();
        _disconnectCheckTimer = null;

        _leaseRenewalTimer?.Dispose();
        _leaseRenewalTimer = null;
    }

    /// <summary>
    /// 客户端已连接事件（由 <see cref="GameNetworkServer"/> 的 Connected 事件委托）。
    /// </summary>
    public async Task HandleConnectedAsync(ITcpSessionClient client, ConnectedEventArgs e)
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
    /// 客户端断开连接事件（由 <see cref="GameNetworkServer"/> 的 Closed 事件委托）。
    /// </summary>
    public async Task HandleDisconnectedAsync(ITcpSessionClient client, ClosedEventArgs e)
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
    /// 通过反射设置底层 Socket 的 KeepAlive 选项。<br/>
    /// TouchSocket 的 ITcpSessionClient 不直接暴露 Socket，需要递归查找属性和字段（含非公共成员）。<br/>
    /// 注意：此 KeepAlive 为辅助机制，核心断线检测依赖 <see cref="CheckDisconnectedConnections"/> 的应用层心跳超时。
    /// 即使 Socket 反射查找失败也绝对不主动关闭连接，避免误杀健康连接（客户端 500ms 断线风暴的根源）。
    /// </summary>
    public void TrySetKeepAlive(ITcpSessionClient client, bool isRetry = false)
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
    /// 定时检测断线连接（每 1 秒触发）。<br/>
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
                // 连接建立后从未收到任何数据（HasReceivedData 仍为 false），且达到首包超时时间，
                // 判定为幽灵连接（探测/错误连接/客户端崩溃后未关闭 Socket），立即清理。
                //
                // 修复 BUG（误杀活连接）：原实现用
                //     neverReceivedData = (LastActiveTime - ConnectedTime).TotalMilliseconds < 100
                // 反推"从未收到数据"，但连接条目可能是在**首包到达时**才由
                // ClientPacketPipeline.StageConnectionEnsureAsync 补注册的 —— 此时 ConnectedTime
                // 就是首包时刻，紧随其后写入的 LastActiveTime 与之相差不足 1ms，判定恒为 true。
                // 结果：一条**刚服务完请求、之后 5 秒内没有新包**的连接会被当幽灵连接清理
                // （实机 2026-09-19：客户端 14:37:58 用该连接创建角色成功，14:38:03 被清理，
                //  客户端侧记录为「Game Gateway 连接已断开：远程终端主动断开」）。
                // 改用显式标记 HasReceivedData（由 HandleReceivedAsync 置位），语义精确、与时序无关。
                bool neverReceivedData = !connection.HasReceivedData;
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
    /// 由 HandleDisconnectedAsync（Closed 事件）和 CheckDisconnectedConnections（定时检测）调用。
    /// </summary>
    /// <param name="connectionId">连接ID</param>
    /// <param name="source">调用来源（连接清理来源枚举，用于日志诊断与治理统计）</param>
    public async Task CleanupConnectionAsync(string connectionId, ConnectionCleanupSource source)
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
    /// 确保连接已注册（连接建立时与收到首包补注册两条路径共用）。
    /// </summary>
    public async Task<IGameConnection?> EnsureConnectionRegisteredAsync(ITcpSessionClient client, bool logOnCreate)
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
}

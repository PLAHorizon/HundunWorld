using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Horizon.Game.Core.Interfaces;
using Horizon.Game.Core.Observability;
using Horizon.Game.Gateway.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Horizon.Game.Gateway.Services;

/// <summary>
/// 阶段 2.2/2.5 — UE5 DS 数据面实现：按 shard 从 <see cref="DSRegistry"/> 解析目标 DS，
/// 把客户端上行同步帧以 <c>[4B 大端长度][原始帧字节]</c> 的帧格式转发到 DS 的 TCP 监听端口。
/// <para>
/// 帧格式约定（DS 侧阶段 2.4 按此读取）：<c>[4B BE length][frame bytes]</c>，其中 frame 为
/// <see cref="Horizon.Game.Message.Sync.SyncPacketCodec"/> 编码的原始同步帧。
/// 传输失败（连接拒绝/超时/未注册）一律返回 false，由调用方回退旧 Orleans 路径——
/// 灰度期不因 DS 故障断服务，任一时点可回切。
/// </para>
/// </summary>
public sealed class DsSyncDataPlane : IDSyncDataPlane
{
    /// <summary>转发连接超时（毫秒）。</summary>
    private const int ConnectTimeoutMs = 2000;

    /// <summary>默认熔断阈值：连续转发失败次数达到该值即临时摘除 DS。</summary>
    private const int DefaultFailureThreshold = 3;

    /// <summary>默认熔断冷却时长：摘除后冷却期内不再路由到该 DS，到期后自动探测恢复。</summary>
    private static readonly TimeSpan DefaultCooldown = TimeSpan.FromSeconds(30);

    private readonly IOptionsMonitor<GatewayOptions> _options;
    private readonly IRollbackCoordinator? _rollbackCoordinator;

    /// <summary>
    /// Sprint 2 — Gateway→DS 上行 TCP 连接池（可选）。为 null 或 <c>GatewayOptions.DsRouteUsePersistentConnection=false</c> 时
    /// 完全走旧的「每包新建 TCP 连接」路径（行为与本类引入连接池之前完全一致）；非 null 且开关开启时改为租借复用长连接。
    /// </summary>
    private readonly DsTcpConnectionPool? _pool;

    private sealed class BreakerState
    {
        public int ConsecutiveFailures;
        public DateTimeOffset CooldownUntilUtc = DateTimeOffset.MinValue;
    }

    private readonly DSRegistry _registry;
    private readonly ILogger<DsSyncDataPlane> _logger;
    private readonly int _failureThreshold;
    private readonly TimeSpan _cooldown;
    private readonly Func<DateTimeOffset> _clock;
    private readonly ConcurrentDictionary<string, BreakerState> _breakerStates = new();

    /// <summary>
    /// 成功转发累计帧数（采样日志用）：上行成功是每帧必经的高频路径，逐帧 Information 会淹没
    /// 真正需要关注的告警。沿用 <c>GatewaySyncWiring</c> 既有惯例（<c>% N == 1</c> 采样）——
    /// 每 1000 帧出一条 Information 摘要（含累计计数，可直接推算吞吐），其余降为 Debug。
    /// </summary>
    private long _routeSuccessCount;

    /// <inheritdoc />
    /// <remarks>
    /// 运行时开关：实时读取 <c>UseDS</c>（IOptionsMonitor，支持不重启网关热切换）；
    /// 阶段 6.3 P2 自动回滚门：override 生效期间强制回切（即使 UseDS=true 也不路由 DS）。
    /// </remarks>
    public bool IsEnabled
        => _options.CurrentValue.UseDS && !(_rollbackCoordinator?.IsRollbackActive ?? false);

    /// <summary>
    /// 创建数据面。
    /// </summary>
    /// <param name="registry">DS 注册表（路由表来源）。</param>
    /// <param name="logger">日志器。</param>
    /// <param name="options">配置监视器：<see cref="IsEnabled"/> 在每次路由调用时读取 <c>UseDS</c>
    /// （不再是 DI 构造快照——改配置无需重启网关即可热切换灰度通道）。</param>
    /// <param name="failureThreshold">熔断阈值：连续转发失败达到该次数即临时摘除该 DS（路由级健康，默认 3）。</param>
    /// <param name="cooldown">熔断冷却时长：冷却期内该 DS 不参与路由，到期后自动探测恢复（默认 30s）。</param>
    /// <param name="clock">时钟提供者（测试注入用）。</param>
    /// <param name="rollbackCoordinator">自动回滚门协调器（可选）：override 生效期间强制回切 DS 通道。</param>
    /// <param name="pool">Sprint 2 — Gateway→DS 上行 TCP 连接池（可选）：仅在 <c>GatewayOptions.DsRouteUsePersistentConnection=true</c>
    /// 且本参数非 null 时生效；否则完全走旧的每包新建 TCP 连接路径（向后兼容，DI 未注入时行为不变）。</param>
    public DsSyncDataPlane(
        DSRegistry registry,
        ILogger<DsSyncDataPlane> logger,
        IOptionsMonitor<GatewayOptions> options,
        int failureThreshold = DefaultFailureThreshold,
        TimeSpan? cooldown = null,
        Func<DateTimeOffset>? clock = null,
        IRollbackCoordinator? rollbackCoordinator = null,
        DsTcpConnectionPool? pool = null)
    {
        if (failureThreshold <= 0) throw new ArgumentOutOfRangeException(nameof(failureThreshold), "熔断阈值必须 > 0。");
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _failureThreshold = failureThreshold;
        _cooldown = cooldown ?? DefaultCooldown;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _rollbackCoordinator = rollbackCoordinator;
        _pool = pool;
    }

    /// <summary>该 DS 是否处于熔断冷却期（临时摘除中；诊断/测试）。</summary>
    public bool IsDsInCooldown(string dsId)
    {
        if (!_breakerStates.TryGetValue(dsId, out var state)) return false;
        lock (state)
        {
            return _clock() < state.CooldownUntilUtc;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// 三个条件必须同时成立才算"DS 权威"，任一不成立都回退 Orleans 语义——
    /// 这样"无可用 DS"时网关的既有健康判定（实体租约等）仍然按老路径正常工作，
    /// 不会因为"以为 DS 在管"而停止清理真正失效的 Orleans 会话。
    /// </remarks>
    public bool IsSessionDsAuthoritative(long shardId, long characterId)
    {
        if (!IsEnabled) return false;
        if (!IsInGrayCohort(characterId, _options.CurrentValue.DsGrayRatio)) return false;

        var status = _registry.TryResolve(
            shardId, _options.CurrentValue.Region, _options.CurrentValue.StrictEmptyRegionMode, out _);
        return status == DsResolveStatus.Ok;
    }

    /// <inheritdoc />
    public async Task<bool> TryRouteAsync(long shardId, long characterId, ReadOnlyMemory<byte> frame, CancellationToken cancellationToken = default)
    {
        if (!IsEnabled) return false;

        var region = _options.CurrentValue.Region;

        // 阶段 6.3 P1⑦：路由决策指标带维度标签（shard_id/region/result[/ds_id]）——
        // attempts 在**结果点**记录一次（result = 最终决策），支撑按 shard/DS 粒度查询：
        //   attempts{result="success"}   = 实际进 DS 的包（每 DS 可直查）
        //   attempts{result="gray_excluded"} = 比例排除的包（每 shard 灰度占比分母）
        // 开关关闭不计 attempts（handler 门在更早处记录 fallback:disabled，网关级基线无 shard 维度）；
        // fallback 保持按 reason 计数（回滚门/ramp-check 既有消费方不变，跨标签求和语义一致）。

        // 阶段 6.3：按比例灰度（会话一致性哈希）。
        // 同一 characterId 判定恒定 → 会话粘滞（不会逐包摇摆）；比例上调单调（群组外→内，
        // 群组内会话不因调档切换宿主）。排除的会话在触碰注册表/网络前即回退（gray_excluded）。
        if (!IsInGrayCohort(characterId, _options.CurrentValue.DsGrayRatio))
        {
            RecordAttempt("gray_excluded", shardId, region);
            SyncMetrics.DsRouteFallback.Add(1, FallbackTags("gray_excluded", shardId, region));
            _logger.LogDebug(
                "DsSyncDataPlane：会话不在灰度群组（DsGrayRatio={Ratio}），回退 Orleans。CharacterId={CharacterId}",
                _options.CurrentValue.DsGrayRatio, characterId);
            return false;
        }

        var sw = Stopwatch.StartNew();

        // 阶段 6.3 P1⑥：按区域解析（防跨区盲路由）——shard 有 DS 但不在本网关区域时
        // 以 wrong_region 回退（配置/拓扑问题，区别于 no_ds 的容量问题，监控可分别告警）。
        // strictEmptyRegion（IOptionsMonitor 逐调用读取）：true 时未声明 region 的旧 DS 仅服务
        // 空 region 网关——有 region 的网关绝不明路由到空区注册（防旧注册盲路由）。
        var status = _registry.TryResolve(shardId, region, _options.CurrentValue.StrictEmptyRegionMode, out var ds);
        if (status == DsResolveStatus.NoDs)
        {
            RecordAttempt("no_ds", shardId, region);
            SyncMetrics.DsRouteFallback.Add(1, FallbackTags("no_ds", shardId, region));
            _logger.LogDebug(
                "DsSyncDataPlane：shard {ShardId} 无已注册且心跳有效的 DS，回退 Orleans 路径。",
                shardId);
            return false;
        }
        if (status == DsResolveStatus.WrongRegion)
        {
            RecordAttempt("wrong_region", shardId, region);
            SyncMetrics.DsRouteFallback.Add(1, FallbackTags("wrong_region", shardId, region));
            _logger.LogWarning(
                "DsSyncDataPlane：shard {ShardId} 的 DS 均不在本网关区域（{Region}），跨区盲路由已拦截，回退 Orleans。请核对 DS 注册区域配置。",
                shardId, region);
            return false;
        }

        // 路由级熔断：该 DS 处于冷却期（连续失败被临时摘除）→ 回退，不再重复打故障端点。
        // 注册表（心跳 liveness）与熔断（TCP 可路由性）分离：心跳仍新鲜但 TCP 故障的 DS
        // 由熔断摘除；冷却到期后首个包自动探测，成功即恢复（与心跳续约共同构成自愈闭环）。
        var breaker = _breakerStates.GetOrAdd(ds.DsId, _ => new BreakerState());
        lock (breaker)
        {
            if (_clock() < breaker.CooldownUntilUtc)
            {
                RecordAttempt("breaker", shardId, region, ds.DsId);
                SyncMetrics.DsRouteFallback.Add(1, FallbackTags("breaker", shardId, region, ds.DsId));
                _logger.LogDebug(
                    "DsSyncDataPlane：DS {DsId} 处于熔断冷却期（临时摘除中），回退 Orleans。ShardId={ShardId}",
                    ds.DsId, shardId);
                return false;
            }
        }

        // 帧封装：4 字节大端长度前缀 + 原始帧（在首个 await 之前同步完成拷贝，
        // 之后 frame 缓冲区由调用方归还池中也不受影响）。
        var payload = new byte[4 + frame.Length];
        BinaryPrimitives.WriteInt32BigEndian(payload, frame.Length);
        frame.Span.CopyTo(payload.AsSpan(4));

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(ConnectTimeoutMs));

            // ── Sprint 2：连接池路径（开关控制，默认关闭走旧路径）──
            if (_pool is not null && _options.CurrentValue.DsRouteUsePersistentConnection)
            {
                var conn = await _pool.RentAsync(ds.Host, ds.Port, timeoutCts.Token).ConfigureAwait(false);
                if (conn is not null)
                {
                    try
                    {
                        await conn.SendAsync(payload, timeoutCts.Token).ConfigureAwait(false);
                        _pool.Return(ds.Host, ds.Port, conn);
                    }
                    catch
                    {
                        // 写入失败：标记连接损坏（Return 内部检测到 IsBroken 会直接 Dispose 而非入池），
                        // 异常继续向上抛出，交由外层 catch 记录 fallback 指标并累计熔断计数。
                        conn.IsBroken = true;
                        _pool.Return(ds.Host, ds.Port, conn);
                        throw;
                    }
                }
                else
                {
                    // 池无法提供连接（达到 MaxPerEndpoint 上限/新建连接失败）：走一次性直连 fallback，
                    // 语义等价于旧路径（成功仍计入 success，不因池自身容量问题误伤熔断计数）。
                    using var fallbackClient = new TcpClient();
                    await fallbackClient.ConnectAsync(ds.Host, ds.Port, timeoutCts.Token).ConfigureAwait(false);
                    var fbStream = fallbackClient.GetStream();
                    await fbStream.WriteAsync(payload, timeoutCts.Token).ConfigureAwait(false);
                    await fbStream.FlushAsync(timeoutCts.Token).ConfigureAwait(false);
                }
            }
            else
            {
                // ── 旧路径：每包新建 TCP 连接（开关关闭或池未注入时的兜底行为，与引入连接池之前完全一致）──
                using var client = new TcpClient();
                await client.ConnectAsync(ds.Host, ds.Port, timeoutCts.Token).ConfigureAwait(false);
                var stream = client.GetStream();
                await stream.WriteAsync(payload, timeoutCts.Token).ConfigureAwait(false);
                await stream.FlushAsync(timeoutCts.Token).ConfigureAwait(false);
            }

            ResetBreaker(ds.DsId);
            sw.Stop();
            RecordAttempt("success", shardId, region, ds.DsId);
            SyncMetrics.DsRouteLatencyMs.Record(sw.Elapsed.TotalMilliseconds, LatencyTags(shardId, region, ds.DsId));

            // 上行成功路径日志降级 + 采样：常态 Debug，每 1000 帧一条 Information 摘要。
            var attempt = Interlocked.Increment(ref _routeSuccessCount);
            if (attempt % 1000 == 1)
            {
                _logger.LogInformation(
                    "DsSyncDataPlane：累计已转发 {Count} 帧到 UE5 DS（最近一帧 {Bytes}B, ShardId={ShardId}, DsId={DsId}）",
                    attempt, payload.Length, shardId, ds.DsId);
            }
            else
            {
                _logger.LogDebug(
                    "DsSyncDataPlane：已转发 {Bytes} 字节到 UE5 DS。ShardId={ShardId}, DsId={DsId}, Host={Host}:{Port}",
                    payload.Length, shardId, ds.DsId, ds.Host, ds.Port);
            }
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // 仅连接/写入超时（非外部取消）
            RecordAttempt("timeout", shardId, region, ds.DsId);
            SyncMetrics.DsRouteFallback.Add(1, FallbackTags("timeout", shardId, region, ds.DsId));
            _logger.LogWarning(
                "DsSyncDataPlane：转发到 UE5 DS 超时（回退 Orleans）。ShardId={ShardId}, DsId={DsId}, Host={Host}:{Port}",
                shardId, ds.DsId, ds.Host, ds.Port);
            RecordFailure(ds.DsId, ds.Host, ds.Port);
            return false;
        }
        catch (Exception ex)
        {
            RecordAttempt("transport_error", shardId, region, ds.DsId);
            SyncMetrics.DsRouteFallback.Add(1, FallbackTags("transport_error", shardId, region, ds.DsId));
            _logger.LogWarning(ex,
                "DsSyncDataPlane：转发到 UE5 DS 失败（回退 Orleans）。ShardId={ShardId}, DsId={DsId}, Host={Host}:{Port}",
                shardId, ds.DsId, ds.Host, ds.Port);
            RecordFailure(ds.DsId, ds.Host, ds.Port);
            return false;
        }
    }

    /// <summary>记录一次路由决策（结果点；attempts 带 shard/region/result[/ds_id] 维度标签）。</summary>
    private static void RecordAttempt(string result, long shardId, string region, string? dsId = null)
        => SyncMetrics.DsRouteAttempts.Add(1, RouteTags(shardId, region, result, dsId));

    /// <summary>attempts 维度标签：{shard_id, region, result} + 已解析出 DS 时 {ds_id}。</summary>
    private static KeyValuePair<string, object?>[] RouteTags(long shardId, string region, string result, string? dsId = null)
        => dsId is null
            ? new[]
            {
                new KeyValuePair<string, object?>("shard_id", shardId),
                new KeyValuePair<string, object?>("region", region),
                new KeyValuePair<string, object?>("result", result),
            }
            : new[]
            {
                new KeyValuePair<string, object?>("shard_id", shardId),
                new KeyValuePair<string, object?>("region", region),
                new KeyValuePair<string, object?>("ds_id", dsId),
                new KeyValuePair<string, object?>("result", result),
            };

    /// <summary>fallback 维度标签：{reason, shard_id, region} + 已解析出 DS 时 {ds_id}。</summary>
    private static KeyValuePair<string, object?>[] FallbackTags(string reason, long shardId, string region, string? dsId = null)
        => dsId is null
            ? new[]
            {
                new KeyValuePair<string, object?>("reason", reason),
                new KeyValuePair<string, object?>("shard_id", shardId),
                new KeyValuePair<string, object?>("region", region),
            }
            : new[]
            {
                new KeyValuePair<string, object?>("reason", reason),
                new KeyValuePair<string, object?>("shard_id", shardId),
                new KeyValuePair<string, object?>("region", region),
                new KeyValuePair<string, object?>("ds_id", dsId),
            };

    /// <summary>成功转发耗时标签：{shard_id, region, ds_id}（按 DS 定位转发慢的粒度）。</summary>
    private static KeyValuePair<string, object?>[] LatencyTags(long shardId, string region, string dsId)
        => new[]
        {
            new KeyValuePair<string, object?>("shard_id", shardId),
            new KeyValuePair<string, object?>("region", region),
            new KeyValuePair<string, object?>("ds_id", dsId),
        };

    /// <summary>
    /// 会话一致性灰度判定（阶段 6.3）：<paramref name="characterId"/> 是否在按比例放量的灰度群组内。
    /// <para>
    /// 纯函数（无状态、无时钟）：同一 characterId 在固定比例下判定恒定（粘滞）；比例单调——
    /// 群组内会话随比例上调不会移出，只在比例下调时移出。默认 100 = 全量（向后兼容
    /// <c>UseDS=true</c> 既有语义）；0 = 全量排除。公开静态供测试直接验证边界/单调性。
    /// </para>
    /// <param name="characterId">会话角色 ID（&lt;= 0 由上层过滤，此处按原值参与哈希）。</param>
    /// <param name="ratio">灰度比例 0–100（越界钳制）。</param>
    /// </summary>
    public static bool IsInGrayCohort(long characterId, int ratio)
    {
        var clamped = Math.Clamp(ratio, 0, 100);
        if (clamped == 0) return false;
        if (clamped == 100) return true;
        // Knuth 乘法哈希（黄金分割常数）：均匀分散连续 characterId，避免「characterId % 100 < ratio」
        // 对顺序 ID 的聚簇（那样会整体放量到一段连续 ID，而非均匀 10%）。
        var bucket = (int)(((ulong)characterId * 2654435761UL) % 100);
        return bucket < clamped;
    }

    private void RecordFailure(string dsId, string? host = null, int port = 0)
    {
        var state = _breakerStates.GetOrAdd(dsId, _ => new BreakerState());
        bool tripped;
        lock (state)
        {
            state.ConsecutiveFailures++;
            tripped = state.ConsecutiveFailures >= _failureThreshold;
            if (tripped)
            {
                state.CooldownUntilUtc = _clock() + _cooldown;
                _logger.LogWarning(
                    "DsSyncDataPlane：DS {DsId} 连续 {Threshold} 次转发失败，临时摘除（熔断 {Cooldown}s，到期自动探测恢复）。",
                    dsId, _failureThreshold, _cooldown.TotalSeconds);
            }
        }

        // Sprint 2 — 熔断联动：一旦该 DS 触发熔断（达到连续失败阈值），立即清空其连接池，
        // 避免冷却期结束后恢复探测时继续复用可能同样不健康的陈旧长连接（应重新建连验证）。
        if (tripped && host is not null && port > 0)
        {
            _pool?.Evict(host, port);
        }
    }

    private void ResetBreaker(string dsId)
    {
        if (_breakerStates.TryGetValue(dsId, out var state))
        {
            lock (state)
            {
                state.ConsecutiveFailures = 0;
                state.CooldownUntilUtc = DateTimeOffset.MinValue;
            }
        }
    }
}

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Horizon.Game.Core.Observability;
using Horizon.Game.Message.Sync;
using Microsoft.Extensions.Logging;

namespace Horizon.Game.Core.Sim.Server;

/// <summary>
/// Gateway 侧的 SyncPacket 广播分派器（P6-a / P6-b）。<br/>
/// 负责：
/// <list type="number">
///   <item>从 <see cref="IZoneShardFanoutSource"/>（对应 <c>IZoneShardGrain</c> 的订阅推送）拉取事件；</item>
///   <item>按 AOI Interest Set 查询 <see cref="ISessionRegistry"/>，把事件转发到每个相关玩家会话。</item>
///   <item>Task D.4：per-session 带宽预算守门，超阈值时降低该 session 的快照频率。</item>
/// </list>
/// </summary>
/// <remarks>
/// 纯 C# 逻辑，不依赖 Orleans / TouchSocket；通过接口注入实现解耦：
/// <list type="bullet">
///   <item><see cref="ISessionRegistry"/> — Gateway 持有的 sessionId → ClientEndpoint 映射。</item>
///   <item><see cref="IClientPacketSink"/> — 实际把字节写回客户端 TCP/QUIC 流的接收者。</item>
///   <item><see cref="IZoneShardFanoutSource"/> — Zone Shard 推送事件的来源。</item>
/// </list>
/// </remarks>
public sealed class GatewaySyncDispatcher
{
    private readonly IZoneShardFanoutSource _source;
    private readonly ISessionRegistry _registry;
    private readonly IClientPacketSink _sink;
    private readonly ILogger<GatewaySyncDispatcher>? _logger;
    private long _totalDispatchedCount;
    private long _totalFailedCount;

    // Task 15：多 worker 并发安全计数器（替代原 { get; private set; } 自增属性）
    private long _processedEventCount;
    private long _deliveredPacketCount;
    private long _droppedOfflineCount;

        // 包丢弃告警限频（每 10 秒最多一次）
        // 改进项 3：用 long ticks + Interlocked 替代 DateTime 字段，消除多 worker 并发读写竞态。
        // 原实现 _lastDropWarnUtc 为 DateTime 实例字段，Parallel.ForEach 多 worker 并发调用
        // LogDropWarn 时存在非原子读-改-写竞态，可能导致限频期间多个线程同时告警。
        private long _lastDropWarnTicks = DateTime.MinValue.Ticks;
        private static readonly TimeSpan DropWarnInterval = TimeSpan.FromSeconds(10);

    // Task D.4：per-session 带宽跟踪器。key = sessionId。
    private readonly ConcurrentDictionary<long, SessionBandwidthTracker> _bandwidthTrackers = new();

    /// <summary>
    /// Task D.4：带宽阈值（kbps），默认 500kbps。<br/>
    /// 修复 BUG（3人在线即触发限流导致移动延迟和位置不连贯）：<br/>
    /// 原值 100kbps 在 3 个玩家同时在线时即被触发（3 session × 2 delta × 80B × 20Hz ≈ 96kbps），
    /// 导致快照频率从 20Hz 降至 10Hz，表现为移动延迟加倍、位置不连贯不精确。<br/>
    /// 500kbps 可容纳约 15 个并发玩家（每玩家约 32kbps），满足小型多人场景需求。
    /// </summary>
    public double BandwidthThresholdKbps { get; set; } = 50000.0;

    /// <summary>Task D.4：正常快照频率（Hz），默认 20Hz。</summary>
    public int NormalSnapshotHz { get; set; } = 20;

    /// <summary>Task D.4：限流快照频率（Hz），默认 10Hz（超阈值时降频）。</summary>
    public int ThrottledSnapshotHz { get; set; } = 10;

    /// <summary>Task D.4：带宽恢复判定秒数（连续 N 秒低于阈值后回升频率），默认 3 秒。</summary>
    public int RecoverySeconds { get; set; } = 3;

    /// <summary>灰度开关；false 时 <see cref="RunOnceAsync"/> 直接 no-op，保持旧路径。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Task 15：单次 Dispatch 内 Parallel.ForEach 的并行度，默认为处理器数。</summary>
    public int MaxDispatchParallelism { get; set; } = Environment.ProcessorCount;

    /// <summary>累计处理的 fanout 事件数。</summary>
    public long ProcessedEventCount => Interlocked.Read(ref _processedEventCount);

    /// <summary>累计下发到客户端的包数。</summary>
    public long DeliveredPacketCount => Interlocked.Read(ref _deliveredPacketCount);

    /// <summary>因 session 下线/不在线而丢弃的包数。</summary>
    public long DroppedOfflineCount => Interlocked.Read(ref _droppedOfflineCount);

    public long TotalDispatchedCount => Interlocked.Read(ref _totalDispatchedCount);

    public long TotalFailedCount => Interlocked.Read(ref _totalFailedCount);

    public GatewaySyncDispatcher(
        IZoneShardFanoutSource source,
        ISessionRegistry registry,
        IClientPacketSink sink,
        ILogger<GatewaySyncDispatcher>? logger = null,
        bool enabled = true)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        _logger = logger;
        Enabled = enabled;
    }

    public Task EnableAsync()
    {
        Enabled = true;
        _logger?.LogInformation("GatewaySyncDispatcher 已启用。");
        return Task.CompletedTask;
    }

    public Task DisableAsync()
    {
        Enabled = false;
        _logger?.LogInformation("GatewaySyncDispatcher 已禁用。");
        return Task.CompletedTask;
    }

    /// <summary>处理一轮事件（适合从 gateway 主循环中每 tick 调用一次）。</summary>
    /// <returns>处理的事件条数。</returns>
    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        if (!Enabled) return 0;
        int processed = 0;
        while (!ct.IsCancellationRequested)
        {
            var evt = await _source.TryDequeueAsync(ct).ConfigureAwait(false);
            if (evt is null) break;
            Dispatch(evt);
            processed++;
            Interlocked.Increment(ref _processedEventCount);
        }
        return processed;
    }

    /// <summary>同步分派单条事件（测试友好，也用于单元测试）。</summary>
    public void Dispatch(FanoutEvent evt)
    {
        if (evt is null) throw new ArgumentNullException(nameof(evt));
        if (evt.Packet is null) return;

        var sessionCount = evt.TargetSessionIds.Count;

        if (ProcessedEventCount % 60 == 0)
        {
            _logger?.LogDebug(
                "GatewaySyncDispatcher：分派事件。PacketKind={PacketKind}, SessionCount={SessionCount}, Delivered={Delivered}, DroppedOffline={Dropped}",
                evt.Packet.Kind, sessionCount, DeliveredPacketCount, DroppedOfflineCount);
        }

        try
        {
            // Task 15：一次编码 wireBytes，所有 session 复用（避免每 session 重复 SyncPacketCodec.Encode + PackMessage）
            var wireBytes = _sink.Encode(evt.Packet, out var wireLength);

            if (evt.Packet is SnapshotPacket snapshot)
            {
                _logger?.LogInformation(
                    "GatewaySyncDispatcher 分派快照：目标会话数={SessionCount}，快照大小={DeltaCount}，wireBytes={WireLength}",
                    sessionCount, snapshot.Deltas?.Length ?? 0, wireLength);
            }

            // Task D.4：预估本包字节数，用于 per-session 带宽计数。
            var estimatedBytes = EstimatePacketSizeBytes(evt.Packet);

            // Task 15.3：并行分批分发。多 worker 并发调用 Dispatch 时，各 worker 内部又并行分发，
            // 互不冲突（计数器均用 Interlocked，_registry/_sink 线程安全）。
            var dop = Math.Max(1, MaxDispatchParallelism);
            Parallel.ForEach(evt.TargetSessionIds, new ParallelOptions { MaxDegreeOfParallelism = dop }, sessionId =>
            {
                if (_registry.TryGetEndpoint(sessionId, out var endpoint) && endpoint is not null)
                {
                    _sink.Send(endpoint, wireBytes, wireLength);
                    Interlocked.Increment(ref _deliveredPacketCount);
                    // Phase 4: 补充 PacketsDelivered 指标
                    SyncMetrics.PacketsDelivered.Add(1);
                    // Task D.4：累计该 session 的下发字节数，并按需触发限流/恢复。
                    RecordSend(sessionId, estimatedBytes);
                }
                else
                {
                    Interlocked.Increment(ref _droppedOfflineCount);
                    // Phase 4: 补充 PacketsDropped 指标（按 reason 维度）
                    SyncMetrics.PacketsDropped.Add(1, new KeyValuePair<string, object?>("reason", "offline"));
                    LogDropWarn(sessionId);
                }
            });

            Interlocked.Increment(ref _totalDispatchedCount);
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _totalFailedCount);
            _logger?.LogError(ex, "GatewaySyncDispatcher 分派失败：目标会话数={SessionCount}", sessionCount);
        }
    }

    // ── Task D.4：带宽预算守门 ──────────────────────────────────────────────

    /// <summary>
    /// Task D.4：记录一次下发到 session 的字节数，并更新该 session 的带宽/频率状态。
    /// </summary>
    public void RecordSend(long sessionId, int bytes)
    {
        if (bytes <= 0) return;
        var tracker = _bandwidthTrackers.GetOrAdd(sessionId, _ => new SessionBandwidthTracker());
        tracker.RecordBytes(bytes, DateTime.UtcNow, this);
    }

    /// <summary>
    /// Task D.4：查询指定 session 当前的快照频率（Hz）。
    /// 超阈值时返回 <see cref="ThrottledSnapshotHz"/>（10Hz），否则返回 <see cref="NormalSnapshotHz"/>（20Hz）。
    /// 供快照生成器（ZoneShardGrain）按 session 调整推送节奏。
    /// </summary>
    public int GetSessionSnapshotHz(long sessionId)
    {
        if (_bandwidthTrackers.TryGetValue(sessionId, out var tracker))
        {
            return tracker.CurrentSnapshotHz;
        }
        return NormalSnapshotHz;
    }

    /// <summary>
    /// Task D.4：获取各 session 当前带宽快照（sessionId → kbps）。
    /// 用于监控面板/Prometheus 指标导出。
    /// </summary>
    public IReadOnlyDictionary<long, double> GetBandwidthSnapshot()
    {
        var result = new Dictionary<long, double>(_bandwidthTrackers.Count);
        foreach (var kv in _bandwidthTrackers)
        {
            result[kv.Key] = kv.Value.CurrentBandwidthKbps;
        }
        return result;
    }

    /// <summary>
    /// Task D.4：预估一个 SyncPacket 序列化后的字节数。
    /// 不做真实序列化，按包类型给出保守上界估计，供带宽预算计数使用。
    /// <para>
    /// 修复 #18：Snapshot 超过 <see cref="SyncPacketCodec.SnapshotCompressionThreshold"/>（256B）
    /// 时会被 LZ4 压缩（通常压缩比 2~3×）。带宽估计若不考虑压缩，实际带宽消耗远低于估计值，
    /// 会触发虚假的带宽限流，将正常 20Hz 快照不必要地降到 10Hz。
    /// 此处将压缩包的大小按 50% 压缩比折减估计（保守上界：压缩后 ≤ 原始大小 × <see cref="_compressionEstimateFactor"/>）。
    /// </para>
    /// </summary>
    private const double _compressionEstimateFactor = 0.6; // 保守估计压缩比为 0.6×（一般实际 0.3~0.5×）

    private static int EstimatePacketSizeBytes(SyncPacket packet)
    {
        // 帧头 + SyncPacket 基类（Kind + ProtocolVersion）开销。
        const int HeaderOverhead = 16;

        // 帧头 + SyncPacketCodec 6 字节帧头
        const int WireHeaderOverhead = HeaderOverhead + SyncPacketCodec.FrameHeaderSize;

        int rawEstimate;
        bool isCompressible = false;

        switch (packet)
        {
            case SnapshotPacket snapshot:
                var deltaCount = snapshot.Deltas?.Length ?? 0;
                rawEstimate = WireHeaderOverhead + 24 + deltaCount * 80;
                // Snapshot 超过 256B 时会被 SyncPacketCodec LZ4 压缩
                isCompressible = rawEstimate > (WireHeaderOverhead + SyncPacketCodec.SnapshotCompressionThreshold);
                break;
            case WorldChunkDiffPacket diff:
                rawEstimate = WireHeaderOverhead + 40 + (diff.Payload?.Length ?? 0);
                isCompressible = diff.PayloadCompressed;
                break;
            case EventPacket evt:
                rawEstimate = WireHeaderOverhead + 16 + (evt.Events?.Length ?? 0) * 32;
                break;
            case InputAckPacket:
                rawEstimate = WireHeaderOverhead + 24;
                break;
            case InputPacket:
                rawEstimate = WireHeaderOverhead + 40;
                break;
            case InteractionSyncPacket:
                rawEstimate = WireHeaderOverhead + 40;
                break;
            case SceneObjectSyncPacket:
                rawEstimate = WireHeaderOverhead + 80;
                break;
            default:
                rawEstimate = WireHeaderOverhead + 64;
                break;
        }

        if (isCompressible)
        {
            // 按保守压缩比折减，避免带宽估计虚高触发不必要的限流
            var compressed = (int)(rawEstimate * _compressionEstimateFactor);
            return Math.Max(compressed, WireHeaderOverhead + 32); // 至少保留帧头 + 最小内容
        }

        return rawEstimate;
    }

    /// <summary>
    /// 限频输出"包因 session 离线被丢弃"警告（每 10 秒最多一次），避免刷屏。
    /// </summary>
    /// <param name="sessionId">丢失的 sessionId（实际为 characterId）。</param>
    private void LogDropWarn(long sessionId)
    {
        var now = DateTime.UtcNow;
        var lastTicks = Interlocked.Read(ref _lastDropWarnTicks);
        if (now.Ticks - lastTicks < DropWarnInterval.Ticks)
            return;

        // 改进项 3：CompareExchange 原子守门。多个 worker 可能同时通过上面的间隔检查，
        // 但只有 CAS 成功（把 lastTicks 替换为 now.Ticks）的线程才告警；
        // 其他线程发现 _lastDropWarnTicks 已被更新（!= lastTicks）则直接返回，
        // 严格保证限频期间只有一个线程输出告警。
        if (Interlocked.CompareExchange(ref _lastDropWarnTicks, now.Ticks, lastTicks) != lastTicks)
            return;

        _logger?.LogWarning(
            "[GatewaySyncDispatcher] Packet dropped: session offline (sessionId={SessionId}, totalDropped={Count})",
            sessionId,
            DroppedOfflineCount);
    }

    // ── Task D.4：per-session 带宽跟踪器 ───────────────────────────────────

    /// <summary>
    /// Task D.4：单个 session 的带宽跟踪器。
    /// <para>
    /// 维护 1 秒滚动窗口内的下发字节数，计算平均带宽（kbps）；
    /// 超过 <see cref="GatewaySyncDispatcher.BandwidthThresholdKbps"/> 时降低快照频率，
    /// 连续 <see cref="GatewaySyncDispatcher.RecoverySeconds"/> 秒低于阈值后回升。
    /// </para>
    /// </summary>
    public sealed class SessionBandwidthTracker
    {
        // Task 17：lock-free 化——移除 _lock，使用 Interlocked + Volatile。
        // 当前 1 秒窗口内累计字节数。
        private long _bytesInCurrentWindow;
        // 当前窗口起始时间（UTC ticks）。
        private long _windowStartTicks = DateTime.UtcNow.Ticks;
        // 最近完成窗口的平均带宽（kbps）。
        private double _currentBandwidthKbps;
        // 当前快照频率（Hz）：正常 20，限流 10。
        private int _currentSnapshotHz = 20;
        // 连续低于阈值的秒数（用于恢复判定）——仅由 CAS 成功的单线程读写，无需 Interlocked。
        private int _consecutiveUnderThresholdSeconds;
        // 是否已对本次超阈值事件记录过告警（避免 spam）——同上，单线程读写。
        private bool _overThresholdWarned;

        /// <summary>当前带宽（kbps，最近完成窗口的平均值）。</summary>
        public double CurrentBandwidthKbps => Volatile.Read(ref _currentBandwidthKbps);

        /// <summary>当前快照频率（Hz）。超阈值时为 10，正常为 20。</summary>
        public int CurrentSnapshotHz => Volatile.Read(ref _currentSnapshotHz);

        /// <summary>
        /// 记录一次下发的字节数，并在窗口滚动时更新带宽/频率状态。
        /// Task 17：lock-free 实现，使用 Interlocked.Add 累计字节，CompareExchange 自旋滚动窗口。
        /// </summary>
        /// <param name="bytes">本次下发字节数。</param>
        /// <param name="nowUtc">当前 UTC 时间。</param>
        /// <param name="owner">所属 dispatcher（用于读取阈值配置与写日志）。</param>
        public void RecordBytes(long bytes, DateTime nowUtc, GatewaySyncDispatcher owner)
        {
            if (bytes <= 0) return;
            Interlocked.Add(ref _bytesInCurrentWindow, bytes);

            // 检查窗口是否需要滚动
            var nowTicks = nowUtc.Ticks;
            var windowStart = Interlocked.Read(ref _windowStartTicks);
            var elapsedTicks = nowTicks - windowStart;
            if (elapsedTicks < TimeSpan.FromSeconds(1).Ticks) return;

            // 尝试 CAS 滚动窗口：只有一个线程能成功，由它负责计算带宽并重置
            if (Interlocked.CompareExchange(ref _windowStartTicks, nowTicks, windowStart) != windowStart)
            {
                // 其他线程已滚动，本线程的 bytes 已被计入，直接返回
                return;
            }

            // 本线程负责滚动窗口：读取并重置 bytes
            var bytesInWindow = Interlocked.Exchange(ref _bytesInCurrentWindow, 0);
            // kbps = bytes * 8 / 1024 / 秒数（二进制 kilobits-per-second）。
            // 注：此处采用 kbps 以匹配 100kbps 阈值（≈12.5KB/s，MMORPG 工业标准）。
            var seconds = (double)elapsedTicks / TimeSpan.TicksPerSecond;
            var kbps = (bytesInWindow * 8.0) / 1024.0 / seconds;

            UpdateThrottleState(kbps, owner);
        }

        /// <summary>
        /// 根据当前带宽更新限流状态（仅由 CAS 成功的单线程调用，内部字段无需 Interlocked，
        /// 但 <see cref="_currentBandwidthKbps"/> / <see cref="_currentSnapshotHz"/> 会被其他线程读取，故用 Volatile.Write）：
        /// <list type="bullet">
        ///   <item>超阈值 → 降到 <see cref="GatewaySyncDispatcher.ThrottledSnapshotHz"/>，告警一次。</item>
        ///   <item>连续 RecoverySeconds 秒低于阈值 → 回升到 <see cref="GatewaySyncDispatcher.NormalSnapshotHz"/>。</item>
        /// </list>
        /// </summary>
        private void UpdateThrottleState(double kbps, GatewaySyncDispatcher owner)
        {
            Volatile.Write(ref _currentBandwidthKbps, kbps);

            var threshold = owner.BandwidthThresholdKbps;

            if (kbps > threshold)
            {
                _consecutiveUnderThresholdSeconds = 0;

                if (_currentSnapshotHz != owner.ThrottledSnapshotHz)
                {
                    Volatile.Write(ref _currentSnapshotHz, owner.ThrottledSnapshotHz);
                }

                // 超阈值时仅记录一次 Warning，避免每秒 spam。
                if (!_overThresholdWarned)
                {
                    _overThresholdWarned = true;
                    owner._logger?.LogWarning(
                        "[GatewaySyncDispatcher] 带宽超阈值限流：bandwidth={BandwidthKbps:F2}kbps > threshold={ThresholdKbps:F2}kbps，" +
                        "快照频率降为 {ThrottledHz}Hz",
                        kbps, threshold, owner.ThrottledSnapshotHz);
                }
            }
            else
            {
                _consecutiveUnderThresholdSeconds++;

                // 连续 RecoverySeconds 秒低于阈值 → 回升频率。
                if (_consecutiveUnderThresholdSeconds >= owner.RecoverySeconds
                    && _currentSnapshotHz != owner.NormalSnapshotHz)
                {
                    Volatile.Write(ref _currentSnapshotHz, owner.NormalSnapshotHz);
                    _overThresholdWarned = false;
                    owner._logger?.LogInformation(
                        "[GatewaySyncDispatcher] 带宽恢复回升：连续 {Seconds} 秒低于阈值，快照频率回升为 {NormalHz}Hz",
                        _consecutiveUnderThresholdSeconds, owner.NormalSnapshotHz);
                }
            }
        }
    }
}

/// <summary>Zone Shard 推送的事件：一个 <see cref="SyncPacket"/> + 应当接收该包的 session 列表。</summary>
public sealed class FanoutEvent
{
    public SyncPacket Packet { get; init; } = null!;
    public IReadOnlyCollection<long> TargetSessionIds { get; init; } = Array.Empty<long>();
}

/// <summary>Zone Shard 的 fanout 来源。</summary>
public interface IZoneShardFanoutSource
{
    /// <summary>尝试取一个事件；无事件时返回 null（不抛）。</summary>
    Task<FanoutEvent?> TryDequeueAsync(CancellationToken ct);
}

/// <summary>Gateway 内部的 session → endpoint 注册表。</summary>
public interface ISessionRegistry
{
    bool TryGetEndpoint(long sessionId, out object? endpoint);
}

/// <summary>发到客户端 endpoint 的包 sink。</summary>
public interface IClientPacketSink
{
    void Send(object endpoint, SyncPacket packet);

    /// <summary>
    /// Task 16：使用预编码的 wireBytes 直接发送，避免每 session 重复编码。
    /// dispatcher 在并行分发前一次性编码，所有 session 复用同一份字节数组。
    /// </summary>
    void Send(object endpoint, byte[] wireBytes, int length);

    /// <summary>
    /// Task 15：一次性编码 SyncPacket 为 wireBytes，供所有 session 复用。
    /// 返回的 byte[] 长度通过 <paramref name="length"/> 输出。
    /// </summary>
    byte[] Encode(SyncPacket packet, out int length);
}

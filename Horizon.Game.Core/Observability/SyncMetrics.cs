using System;
using System.Diagnostics.Metrics;

namespace Horizon.Game.Core.Observability;

/// <summary>
/// 1M-CCU 同步栈的关键指标（P7-a）。<br/>
/// 所有度量都通过 <see cref="System.Diagnostics.Metrics.Meter"/> 暴露，可由 OpenTelemetry / Prometheus exporter 采集。
/// </summary>
/// <remarks>
/// 命名规范：<c>horizon.sync.*</c>，单位以 UCUM 规范标注（<c>{packets}</c> / <c>{sessions}</c> / <c>ms</c> / <c>{ops}</c>）。
/// 维度（Tags）：
/// <list type="bullet">
///   <item><c>shard_id</c> — Zone Shard 序号。</item>
///   <item><c>grain_kind</c> — <c>zone_shard</c> / <c>world_chunk_cell</c> / <c>world_diff_log</c> / <c>player_session</c>。</item>
///   <item><c>reason</c> — 用于 dropped counters（<c>offline</c> / <c>out_of_order</c> / <c>retention_exceeded</c> / <c>force_relogin</c>）。</item>
/// </list>
/// </remarks>
public static class SyncMetrics
{
    /// <summary>单例 Meter；所有 instrument 挂在这里。</summary>
    public static readonly Meter Meter = new("Horizon.Game.Sync", "1.0.0");

    /// <summary>从 <c>InputPacket.ClientTick</c> 到服务器实际 consume 的毫秒滞后。</summary>
    public static readonly Histogram<double> InputLagMs =
        Meter.CreateHistogram<double>("horizon.sync.input.lag", unit: "ms", description: "客户端 input 到服务器 consume 的滞后");

    /// <summary>Zone Shard 订阅的活动 session 数。</summary>
    public static readonly UpDownCounter<long> ActiveSessions =
        Meter.CreateUpDownCounter<long>("horizon.sync.sessions.active", unit: "{sessions}", description: "ZoneShard 当前订阅的 session 数");

    /// <summary>每个玩家 AOI interest set 的 chunk 数（histogram，支持 p99）。</summary>
    public static readonly Histogram<long> AoiChunkCount =
        Meter.CreateHistogram<long>("horizon.sync.aoi.chunks", unit: "{chunks}", description: "AOI interest-set 大小");

    /// <summary>WorldDiffLog 当前 head 与客户端 high-water 的 gap。</summary>
    public static readonly Histogram<long> DiffSeqGap =
        Meter.CreateHistogram<long>("horizon.sync.diff.gap", unit: "{ops}", description: "client high-water 与 server head 的差值");

    /// <summary>累计下发到客户端的包数。</summary>
    public static readonly Counter<long> PacketsDelivered =
        Meter.CreateCounter<long>("horizon.sync.packets.delivered", unit: "{packets}", description: "下发给客户端的 SyncPacket 数");

    /// <summary>累计丢弃的包数（按 reason 维度）。</summary>
    public static readonly Counter<long> PacketsDropped =
        Meter.CreateCounter<long>("horizon.sync.packets.dropped", unit: "{packets}", description: "被丢弃的包数");

    /// <summary>World diff log 单次 op 延迟。</summary>
    public static readonly Histogram<double> DiffLogOpMs =
        Meter.CreateHistogram<double>("horizon.sync.diff.op.ms", unit: "ms", description: "WorldDiffLog 单次 op 延迟");

    /// <summary>ChunkCell apply 延迟。</summary>
    public static readonly Histogram<double> ChunkApplyMs =
        Meter.CreateHistogram<double>("horizon.sync.chunk.apply.ms", unit: "ms", description: "ChunkCell.ApplyBatch 延迟");

    /// <summary>重连 decision 的分布。</summary>
    public static readonly Counter<long> ReconnectDecisions =
        Meter.CreateCounter<long>("horizon.sync.reconnect.decisions", unit: "{reconnects}", description: "重连决策分布（按 decision 维度）");

    /// <summary>
    /// 握手阶段被拒绝的连接数（按 <c>reason</c> 维度：
    /// <c>protocol_version</c> / <c>formula_version</c>）。
    /// 发布窗口（协议/公式版本升级）期间旧客户端拒绝量由此监控，驱动放量/回滚决策。
    /// </summary>
    public static readonly Counter<long> HandshakeRejected =
        Meter.CreateCounter<long>("horizon.sync.handshake.rejected", unit: "{handshakes}",
            description: "握手阶段被拒绝的连接数（按 reason 维度）");

    // ========== 阶段 6.3：UE5 DS 灰度路由指标 ==========

    /// <summary>
    /// 尝试路由到 UE5 DS 的包数（数据面启用态每次路由决策在**结果点**记录一次）。
    /// 维度：<c>shard_id</c>/<c>region</c>/<c>result</c>（<c>success</c>/<c>gray_excluded</c>/<c>no_ds</c>/
    /// <c>wrong_region</c>/<c>breaker</c>/<c>timeout</c>/<c>transport_error</c>），已解析出 DS 时带 <c>ds_id</c>。
    /// </summary>
    public static readonly Counter<long> DsRouteAttempts =
        Meter.CreateCounter<long>("horizon.sync.ds.route.attempts", unit: "{packets}",
            description: "尝试路由到 UE5 DS 的包数（按 shard/region/result 维度）");

    /// <summary>
    /// 路由到 UE5 DS 失败并回退 Orleans 的包数。维度：<c>reason</c>
    /// （<c>disabled</c>/<c>gray_excluded</c>/<c>no_ds</c>/<c>wrong_region</c>/<c>breaker</c>/<c>timeout</c>/<c>transport_error</c>）
    /// + <c>shard_id</c>/<c>region</c>；已解析出 DS 时带 <c>ds_id</c>（<c>disabled</c> 为网关级基线，仅 reason）。
    /// </summary>
    public static readonly Counter<long> DsRouteFallback =
        Meter.CreateCounter<long>("horizon.sync.ds.route.fallback", unit: "{packets}",
            description: "路由到 UE5 DS 失败并回退 Orleans 的包数（按 reason/shard/region 维度）");

    /// <summary>UE5 DS 转发耗时（成功路径，直方图支持 p95/p99；维度 <c>shard_id</c>/<c>region</c>/<c>ds_id</c>）。</summary>
    public static readonly Histogram<double> DsRouteLatencyMs =
        Meter.CreateHistogram<double>("horizon.sync.ds.route.latency.ms", unit: "ms",
            description: "UE5 DS 转发耗时（成功路径，按 shard/region/DS 维度）");

    // ========== 阶段 6.3：服务端 Correction 率 ==========

    /// <summary>服务端发出的位置修正包数（按 <c>reason</c> 维度，CorrectionReason 枚举名）。</summary>
    public static readonly Counter<long> Corrections =
        Meter.CreateCounter<long>("horizon.sync.corrections", unit: "{corrections}",
            description: "服务端发出的位置修正包数（按 reason 维度）");

    // ========== 阶段 6.3 P2：自动回滚门 ==========

    /// <summary>自动回滚门累计触发的回滚次数（进程级，供告警/审计；每次 Arm 自增一次）。</summary>
    public static readonly Counter<long> RollbacksTriggered =
        Meter.CreateCounter<long>("horizon.sync.ds.rollbacks.triggered", unit: "{rollbacks}",
            description: "自动回滚门累计触发回切 UseDS 的次数");

    // ========== 阶段 6.3 P2⑨：灰度对比面板（窗口聚合 gauge） ==========

    /// <summary>每窗口路由尝试增量（按 <c>result</c> 维度：<c>success</c>=DS 段实际进量，其余=回退分类）。</summary>
    public static readonly Gauge<long> GrayWindowAttempts =
        Meter.CreateGauge<long>("horizon.sync.gray.window.attempts", unit: "{packets}",
            description: "灰度对比面板：每窗口 DS 路由尝试增量（按 result 维度）");

    /// <summary>每窗口 DS 回退 Orleans 增量（按 <c>reason</c> 维度，Orleans 段兜底量）。</summary>
    public static readonly Gauge<long> GrayWindowFallback =
        Meter.CreateGauge<long>("horizon.sync.gray.window.fallback", unit: "{packets}",
            description: "灰度对比面板：每窗口 DS 回退 Orleans 增量（按 reason 维度）");

    /// <summary>每窗口服务端修正增量（按 <c>reason</c> 维度；跨进程自 Silo 经 Redis 汇聚）。</summary>
    public static readonly Gauge<long> GrayWindowCorrections =
        Meter.CreateGauge<long>("horizon.sync.gray.window.corrections", unit: "{corrections}",
            description: "灰度对比面板：每窗口服务端修正增量（按 reason 维度）");
}

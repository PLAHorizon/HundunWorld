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
}

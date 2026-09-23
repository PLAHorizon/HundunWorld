using System;
using System.Collections.Generic;
using Horizon.Game.Message.Sync;

namespace Horizon.Game.Core.Sim.Server;

/// <summary>
/// DS 下行帧的落地目标（MMORPG 开发计划 M2.3）。
/// <para>
/// 由 <c>GatewayZoneShardFanoutSource</c> 实现——它已经是 <see cref="IZoneShardFanoutSource"/>，
/// 也就是说 DS 下行注入与 Orleans grain 推送**共用同一条分发管线**
/// （带宽守门 / 兴趣分级 / 并行分发 / 背压丢弃），无需为 DS 另造一套。
/// </para>
/// </summary>
public interface IDsDownlinkSink
{
    /// <summary>尝试投递一个出站事件。返回 false 表示被背压丢弃（调用方计数但不阻断）。</summary>
    bool TryEnqueue(FanoutEvent evt);
}

/// <summary>
/// DS 下行入口（MMORPG 开发计划 M2.3 —— 消灭缺口 G4「DS 快照回不到客户端」）。
/// <para>
/// 职责：把 DS 上报的 <see cref="DsSnapshotBatch"/> 拆解为逐个
/// <see cref="FanoutEvent"/>（<c>SyncPacket</c> + 目标会话集合）并投递到既有分发管线。
/// </para>
/// <para>
/// <b>为什么必须批次化 + 定向</b>：DS 一次 tick 的广播天然是批量的；逐帧发送会把跨进程往返
/// 放大到 O(实体×会话)。同时每个条目都携带明确的目标会话集合——若退化为"广播给全服"，
/// 带宽会随在线人数线性爆炸，且直接绕过既有 AOI 的全部价值。
/// </para>
/// <para>
/// <b>失败语义</b>：单个条目解码失败（帧损坏/协议不匹配）**只丢该条目**，不影响同批次其他条目；
/// 这是必须的——一帧坏数据不该让整批玩家的状态更新全部丢失。丢弃计数经
/// <see cref="DecodeFailureCount"/> / <see cref="DroppedByBackpressureCount"/> 暴露给监控。
/// </para>
/// </summary>
public sealed class DsDownlinkIngress
{
    private readonly IDsDownlinkSink _sink;
    private readonly Action<string>? _onDrop;

    /// <summary>创建下行入口。</summary>
    /// <param name="sink">落地的分发管线（通常为网关的 fanout source）。</param>
    /// <param name="onDrop">丢弃回调（诊断/指标）；可为 null。</param>
    public DsDownlinkIngress(IDsDownlinkSink sink, Action<string>? onDrop = null)
    {
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        _onDrop = onDrop;
    }

    /// <summary>累计成功投递的事件数。</summary>
    public long AcceptedCount { get; private set; }

    /// <summary>累计解码失败被丢弃的条目数。</summary>
    public long DecodeFailureCount { get; private set; }

    /// <summary>累计因背压被丢弃的条目数。</summary>
    public long DroppedByBackpressureCount { get; private set; }

    /// <summary>累计空受众（无目标会话）被跳过的条目数。</summary>
    public long SkippedEmptyAudienceCount { get; private set; }

    /// <summary>
    /// 摄取一批 DS 下行帧。
    /// </summary>
    /// <param name="batch">DS 上报的批次；null 安全（直接返回）。</param>
    /// <returns>本批成功投递的事件数。</returns>
    public int Ingest(DsSnapshotBatch? batch)
    {
        if (batch?.Items is null || batch.Items.Length == 0)
        {
            return 0;
        }

        var accepted = 0;
        for (var i = 0; i < batch.Items.Length; i++)
        {
            var item = batch.Items[i];
            if (item is null) continue;

            // 无受众的条目直接跳过：既没有收件人，投递只会浪费队列槽位。
            if (item.SessionIds is null || item.SessionIds.Length == 0)
            {
                SkippedEmptyAudienceCount++;
                _onDrop?.Invoke("empty_audience");
                continue;
            }

            if (item.Frame is null || item.Frame.Length < SyncPacketCodec.FrameHeaderSize)
            {
                DecodeFailureCount++;
                _onDrop?.Invoke("short_frame");
                continue;
            }

            SyncPacket packet;
            try
            {
                packet = SyncPacketCodec.Decode(item.Frame);
            }
            catch (Exception)
            {
                // 不可信网络输入：绝不向上抛（否则一条坏帧会打断整个 DS 连接的处理循环）。
                DecodeFailureCount++;
                _onDrop?.Invoke("decode_failed");
                continue;
            }

            var evt = new FanoutEvent
            {
                Packet = packet,
                TargetSessionIds = item.SessionIds,
            };

            if (_sink.TryEnqueue(evt))
            {
                AcceptedCount++;
                accepted++;
            }
            else
            {
                DroppedByBackpressureCount++;
                _onDrop?.Invoke("backpressure");
            }
        }

        return accepted;
    }

    /// <summary>批量摄取多个批次（一个 DS 上行帧序列的便捷入口）。</summary>
    public int IngestAll(IEnumerable<DsSnapshotBatch> batches)
    {
        if (batches is null) return 0;
        var total = 0;
        foreach (var b in batches)
        {
            total += Ingest(b);
        }
        return total;
    }
}

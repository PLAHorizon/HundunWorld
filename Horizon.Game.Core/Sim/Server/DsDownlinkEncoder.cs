using System;
using System.Buffers;
using System.Collections.Generic;
using Horizon.Game.Message.Sync;
using MemoryPack;

namespace Horizon.Game.Core.Sim.Server;

/// <summary>DS 下行发送目标（由宿主注入；生产为 <see cref="DsUplinkClient"/>，测试可替换为假实现）。</summary>
public interface IDsDownlinkSender
{
    /// <summary>发送一批定向下行帧。返回 false 表示发送失败（宿主应计数，不应抛）。</summary>
    System.Threading.Tasks.Task<bool> SendSnapshotBatchAsync(DsSnapshotBatch batch, System.Threading.CancellationToken cancellationToken = default);
}

/// <summary>
/// DS tick 产出 → 下行批次 的编码器（MMORPG 开发计划 P3）。
/// <para>
/// <b>编码复用是刻意的</b>：一个 tick 的世界快照对所有在线会话是**同一份字节**，
/// 因此只编码一次、把同一个 <c>byte[]</c> 挂到多个受众上。若按会话逐个编码，
/// 1000 人同屏时每 tick 会做 1000 次 MemoryPack + 可能的 LZ4 压缩——
/// 这正是既有架构在网关侧「Task 15：一次性编码 wireBytes 供所有 session 复用」
/// 已经解决过的反模式，这里保持同一纪律。
/// </para>
/// <para>
/// <b>包型选择</b>：修正与 InputAck 走 <see cref="EventPacket"/> 承载——
/// 与既有设计一致（<c>CorrectionPacket</c> 的注释明确"以 EventPacket 的负载形式下发，
/// 避免 SyncPacket union 再次扩容"），因此本编码器**不需要改动协议版本或 MessageUnion**。
/// </para>
/// </summary>
public static class DsDownlinkEncoder
{
    /// <summary>把单个 tick 的产出编码为一个下行批次。</summary>
    /// <param name="result">tick 产出。</param>
    /// <returns>可直接交给 <see cref="IDsDownlinkSender"/> 的批次。</returns>
    public static DsSnapshotBatch Encode(DsTickResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var items = new List<DsSnapshotItem>(
            1 + (result.InitialSpawnSnapshots?.Count ?? 0) + result.Corrections.Count + result.InputAcks.Count);

        // 1) 世界快照：一份字节，多个受众。
        //    AOI 分桶模式：每个分组一份快照字节 + 该组受众——组内仍是「一份字节多受众」，
        //    相同可见集合的会话只编码一次，编码不变量得以保留。
        if (result.SnapshotGroups is { Count: > 0 })
        {
            for (var g = 0; g < result.SnapshotGroups.Count; g++)
            {
                var group = result.SnapshotGroups[g];
                if (group.AudienceSessionIds.Length == 0) continue;
                items.Add(new DsSnapshotItem
                {
                    SessionIds = group.AudienceSessionIds,
                    Frame = EncodeToExactArray(group.Snapshot),
                });
            }
        }
        //    旧全量路径（AOI 开关关闭时）：所有受众共享同一份全世界快照字节。
        else if (result.WorldSnapshot is not null && result.AudienceSessionIds.Length > 0)
        {
            items.Add(new DsSnapshotItem
            {
                SessionIds = result.AudienceSessionIds,
                Frame = EncodeToExactArray(result.WorldSnapshot),
            });
        }

        // 1.5) 会话级首包强制 Spawn 快照（晚加入/重连补发）：每会话一份字节、定向单播。
        //      与会话级 Spawn 状态一一对应，无法复用共享字节；加入/重连为低频事件，逐会话编码可接受。
        if (result.InitialSpawnSnapshots is { Count: > 0 })
        {
            for (var i = 0; i < result.InitialSpawnSnapshots.Count; i++)
            {
                var initial = result.InitialSpawnSnapshots[i];
                items.Add(new DsSnapshotItem
                {
                    SessionIds = new[] { initial.SessionId },
                    Frame = EncodeToExactArray(initial.Snapshot),
                });
            }
        }

        // 1.8) 离线 Despawn 全广播：一份字节、全在线受众（镜像 Orleans 经验：不做 AOI 过滤）。
        //      不等快照节拍，紧随会话结束后的第一个 tick 下发，缩短"幽灵角色"残留窗口。
        if (result.DespawnBroadcast is not null && result.DespawnBroadcastAudience.Length > 0)
        {
            items.Add(new DsSnapshotItem
            {
                SessionIds = result.DespawnBroadcastAudience,
                Frame = EncodeToExactArray(result.DespawnBroadcast),
            });
        }

        // 2) 修正包：定向单播（可靠 + 高优先级语义）。
        for (var i = 0; i < result.Corrections.Count; i++)
        {
            var (sessionId, correction) = result.Corrections[i];
            var payload = MemoryPackSerializer.Serialize(correction);
            var eventPacket = new EventPacket
            {
                ServerTick = result.ServerTick,
                Events = new[]
                {
                    new SyncEvent
                    {
                        Kind = SyncEventKind.Correction,
                        SourceEntityId = correction.EntityId,
                        TargetEntityId = correction.EntityId,
                        Payload = payload,
                    },
                },
            };

            items.Add(new DsSnapshotItem
            {
                SessionIds = new[] { sessionId },
                Frame = EncodeToExactArray(eventPacket),
            });
        }

        // 3) InputAck：定向单播，缩短客户端 reconciliation 窗口。
        for (var i = 0; i < result.InputAcks.Count; i++)
        {
            var (sessionId, lastProcessedClientTick) = result.InputAcks[i];
            var ack = new InputAckPacket
            {
                LastProcessedClientTick = lastProcessedClientTick,
                ServerTick = result.ServerTick,
            };

            items.Add(new DsSnapshotItem
            {
                SessionIds = new[] { sessionId },
                Frame = EncodeToExactArray(ack),
            });
        }

        // 4) 战斗表现事件（伤害/死亡）：每项自带受众，通常为单播。
        for (var i = 0; i < result.Events.Count; i++)
        {
            var (audience, packet) = result.Events[i];
            if (audience is null || audience.Length == 0 || packet is null) continue;

            items.Add(new DsSnapshotItem
            {
                SessionIds = audience,
                Frame = EncodeToExactArray(packet),
            });
        }

        return new DsSnapshotBatch { Items = items.ToArray() };
    }

    /// <summary>
    /// 编码为精确长度数组（Sprint 4 内存优化评估结论：当前已是最优形态）。
    /// <para>
    /// <see cref="SyncPacketCodec.Encode"/> 内部已从 <see cref="ArrayPool{T}"/> 租借缓冲，
    /// 本方法只做一次精确拷贝后即归还池缓冲——中间编码阶段已零分配。
    /// </para>
    /// <para>
    /// <b>最终 <c>new byte[length]</c> 无法消除</b>：<see cref="DsSnapshotItem.Frame"/> 经
    /// MemoryPack 序列化后写入线上帧，MemoryPack 对 <c>byte[]</c> 使用 <c>Array.Length</c>
    /// 作为长度前缀。若用 <c>ArrayPool.Rent</c>（返回数组长度 &ge; 请求值），序列化会多写
    /// 无效字节导致反序列化越界。要彻底消除需改 <c>DsSnapshotItem</c> 为
    /// <c>ReadOnlyMemory&lt;byte&gt;</c> + 自定义 MemoryPack Formatter（协议层变更，影响
    /// DS↔网关全链路兼容性），收益不足以承担风险。
    /// </para>
    /// <para>
    /// GC 压力评估：20Hz × N 组，每组一次 Gen0 短命分配（快照字节通常 &lt; 64KB），
    /// Gen0 回收成本极低（&lt; 0.1ms），不构成帧预算瓶颈。
    /// </para>
    /// </summary>
    private static byte[] EncodeToExactArray(SyncPacket packet)
    {
        SyncPacketCodec.Encode(packet, out var rented, out var length);
        try
        {
            var exact = new byte[length];
            Buffer.BlockCopy(rented, 0, exact, 0, length);
            return exact;
        }
        finally
        {
            SyncPacketCodec.ReturnFrame(rented);
        }
    }
}

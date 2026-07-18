using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Horizon.Game.Message.Sync;

namespace Horizon.Game.Core.Sim.Client;

/// <summary>
/// 客户端收到的 SyncPacket v2 四种新包的收件箱（P5-a）。<br/>
/// <see cref="SyncPacketDispatcher"/> 把 <see cref="SyncPacket"/> 分派到这里；
/// 应用层 ECS 系统（<see cref="WorldDiffApplier"/>）/ reconciliation（<see cref="ManagedHundunWorld.Network.Sync.MovementPrediction"/>）
/// 从队列里消费。
/// </summary>
/// <remarks>
/// 所有 ConcurrentQueue 成员都是线程安全的：网络线程写，ECS 线程读。
/// </remarks>
public sealed class SyncPacketInbox
{
    /// <summary>待应用的 chunk diff（按到达顺序；应用前还会再按 <c>DiffSeqStart</c> 排序）。</summary>
    public ConcurrentQueue<WorldChunkDiffPacket> ChunkDiffs { get; } = new();

    /// <summary>收到的 patch manifest（通常只有 1 条，由 UI 侧消费以跳启动器）。</summary>
    public ConcurrentQueue<WorldPatchManifestPacket> PatchManifests { get; } = new();

    /// <summary>最新的 input ACK（覆盖式，只保留最新一条；reconciliation 只关心 high-water）。</summary>
    public InputAckPacket? LatestAck { get; private set; }

    /// <summary>待消费的交互槽状态事件（阶段 1；按到达顺序，应用层按 SlotIdx/ServerTick 排序）。</summary>
    public ConcurrentQueue<InteractionSyncPacket> InteractionEvents { get; } = new();

    /// <summary>待消费的场景对象状态事件（阶段 C；按到达顺序，应用层按 ObjectId/ServerTick 排序）。</summary>
    public ConcurrentQueue<SceneObjectSyncPacket> SceneObjectEvents { get; } = new();

    /// <summary>截至本次应用结束的全局 diff seq，用于后续 <see cref="ReconnectResumePacket.LastAppliedDiffSeq"/>。</summary>
    public long AppliedDiffSeq { get; set; }

    /// <summary>累计丢弃的过期 diff 数（诊断用）。</summary>
    public long DroppedOutOfOrderCount { get; set; }

    internal void UpdateLatestAck(InputAckPacket packet)
    {
        // 用 tick 作为偏序 — 高 tick 覆盖低 tick
        var current = LatestAck;
        if (current is null || packet.LastProcessedClientTick > current.LastProcessedClientTick)
        {
            LatestAck = packet;
        }
    }

    /// <summary>诊断用快照。</summary>
    public SyncInboxSnapshot Snapshot() => new()
    {
        PendingChunkDiffCount = ChunkDiffs.Count,
        PendingManifestCount = PatchManifests.Count,
        PendingInteractionEventCount = InteractionEvents.Count,
        PendingSceneObjectEventCount = SceneObjectEvents.Count,
        LatestAckTick = LatestAck?.LastProcessedClientTick ?? 0,
        AppliedDiffSeq = AppliedDiffSeq,
        DroppedOutOfOrderCount = DroppedOutOfOrderCount,
    };
}

/// <summary>诊断快照。</summary>
public readonly struct SyncInboxSnapshot
{
    public int PendingChunkDiffCount { get; init; }
    public int PendingManifestCount { get; init; }

    /// <summary>待消费的交互槽状态事件数量（<see cref="SyncPacketInbox.InteractionEvents"/> 队列深度）。</summary>
    public int PendingInteractionEventCount { get; init; }

    /// <summary>待消费的场景对象状态事件数量（<see cref="SyncPacketInbox.SceneObjectEvents"/> 队列深度）。</summary>
    public int PendingSceneObjectEventCount { get; init; }

    public long LatestAckTick { get; init; }
    public long AppliedDiffSeq { get; init; }
    public long DroppedOutOfOrderCount { get; init; }
}

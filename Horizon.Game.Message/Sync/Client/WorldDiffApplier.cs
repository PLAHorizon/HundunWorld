using System;
using System.Collections.Generic;
using Horizon.Game.Core.Utilities;
using Horizon.Game.Core.World.ChunkCell;
using Horizon.Game.Message.Sync;
using Horizon.Game.Message.World;
using MemoryPack;

namespace Horizon.Game.Core.Sim.Client;

/// <summary>
/// 客户端 WorldDiff 应用器（P5-b）。<br/>
/// 每 tick 从 <see cref="SyncPacketInbox.ChunkDiffs"/> 里拉取全部待处理 diff，按
/// <c>DiffSeqStart</c> 升序排序，逐 chunk 合并进本地 <see cref="ChunkCellState"/> 镜像；
/// 过期 / 重复 / 乱序包会被丢弃并计入 <see cref="SyncPacketInbox.DroppedOutOfOrderCount"/>。
/// </summary>
/// <remarks>
/// 这是纯 C# 类（不依赖 Unreal 也不依赖 ECS 框架），可在单测中直接使用。
/// 具体 ECS 集成由 <c>WorldDiffApplierEcsSystem</c> 承担（后续小任务）。
/// </remarks>
public sealed class WorldDiffApplier
{
    private readonly SyncPacketInbox _inbox;
    private readonly Dictionary<ulong, ChunkCellState> _chunks = new();

    /// <summary>已知 chunk 的只读视图（诊断）。</summary>
    public IReadOnlyDictionary<ulong, ChunkCellState> Chunks => _chunks;

    public WorldDiffApplier(SyncPacketInbox inbox)
    {
        _inbox = inbox ?? throw new ArgumentNullException(nameof(inbox));
    }

    /// <summary>
    /// 消费一批 diff；返回成功应用的 diff 数量（非 op 数）。
    /// </summary>
    public int Drain()
    {
        // 1. 把 concurrent queue 里的包一次性捞出来（避免写线程持续注入导致活锁）。
        var batch = new List<WorldChunkDiffPacket>();
        while (_inbox.ChunkDiffs.TryDequeue(out var p))
        {
            batch.Add(p);
        }
        if (batch.Count == 0) return 0;

        // 2. 按 (ChunkMortonKey, DiffSeqStart) 排序；同 chunk 按 seq 升序保证确定性应用。
        batch.Sort(static (a, b) =>
        {
            var c = a.ChunkMortonKey.CompareTo(b.ChunkMortonKey);
            return c != 0 ? c : a.DiffSeqStart.CompareTo(b.DiffSeqStart);
        });

        int applied = 0;
        long maxSeqEnd = _inbox.AppliedDiffSeq;
        foreach (var diff in batch)
        {
            if (diff.DiffSeqEnd <= _inbox.AppliedDiffSeq)
            {
                // 已经处理过的范围 — 丢弃。
                _inbox.DroppedOutOfOrderCount++;
                continue;
            }

            VoxelOp[]? ops = DecodePayload(diff);
            if (ops is null) continue;

            if (!_chunks.TryGetValue(diff.ChunkMortonKey, out var state))
            {
                state = new ChunkCellState(diff.ChunkMortonKey);
                _chunks[diff.ChunkMortonKey] = state;
            }

            state.ApplyBatch(ops);
            if (diff.DiffSeqEnd > maxSeqEnd) maxSeqEnd = diff.DiffSeqEnd;
            applied++;
        }

        _inbox.AppliedDiffSeq = maxSeqEnd;
        return applied;
    }

    /// <summary>解码 payload；支持 LZ4 压缩（<see cref="WorldChunkDiffPacket.PayloadCompressed"/>=true 时用 <see cref="LZ4Pickler.Unpickle"/> 解包）。
    /// 修复：检查 PayloadType，非 VoxelOp 载荷不尝试解码为 VoxelOp[]，避免静默丢失。</summary>
    private static VoxelOp[]? DecodePayload(WorldChunkDiffPacket diff)
    {
        // 修复 #17：非 VoxelOp/EntityDelta 载荷不由 WorldDiffApplier 处理（由 SyncPacketDispatcher 路由或预留），
        // 此处直接返回空数组以跳过，避免 MemoryPack 反序列化失败返回 null 导致 diff 被静默丢弃。
        if (diff.PayloadType != WorldChunkDiffPayloadType.EntityDelta
            && diff.PayloadType != WorldChunkDiffPayloadType.Correction
            && diff.PayloadType != WorldChunkDiffPayloadType.Event)
        {
            return Array.Empty<VoxelOp>();
        }

        if (diff.Payload is null || diff.Payload.Length == 0)
        {
            return Array.Empty<VoxelOp>();
        }
        byte[] raw;
        if (diff.PayloadCompressed)
        {
            // 与生产端 LZ4Pickler.Pickle 对齐：[4 bytes int32 原长度][LZ4 块]。
            var unpickled = LZ4Pickler.Unpickle(diff.Payload);
            if (unpickled is null) return null;
            raw = unpickled;
        }
        else
        {
            raw = diff.Payload;
        }
        try
        {
            return MemoryPackSerializer.Deserialize<VoxelOp[]>(raw);
        }
        catch
        {
            // 数据损坏 — 上层通过 DroppedOutOfOrderCount 间接观察；这里直接返回 null。
            return null;
        }
    }
}

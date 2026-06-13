using Orleans;
using Horizon.Game.Message.World;

namespace Horizon.Orleans.Grains.World;

/// <summary>
/// <see cref="Horizon.Game.Core.World.ChunkCell.ChunkCellState"/> 的 Orleans 持久化投影（P4-a）。<br/>
/// 只序列化重建状态所需最少字段：Morton 键 + version + 压缩后的 op log。
/// 反序列化时按 op log 顺序回放即可恢复全部 (block, prefab) 状态。
/// </summary>
[GenerateSerializer]
public sealed class ChunkCellPersistedState
{
    /// <summary>本 chunk 的 Morton 键（= grain primary key，但冗余存储以便审计）。</summary>
    [Id(0)] public ulong MortonKey { get; set; }

    /// <summary>grain 写入时的版本号；随 op 递增，用于乐观并发与诊断。</summary>
    [Id(1)] public long Version { get; set; }

    /// <summary>
    /// 压缩后的 op log（调用 <c>ChunkCellState.CompactOpLog</c> 之后的最小集合）。
    /// 由 <c>WorldChunkCellGrain.WriteStateAsync</c> 保证每次保存前已 compact，以控制行大小。
    /// </summary>
    [Id(2)] public VoxelOp[] OpLog { get; set; } = System.Array.Empty<VoxelOp>();
}

/// <summary>
/// <see cref="Horizon.Game.Core.World.WorldDiffLog"/> 的 Orleans 持久化投影（P4-a）。<br/>
/// 注意：真正的"跨 silo 单调 seq"应来自 SQL Server <c>diff_log.seq</c> 的 IDENTITY 列；
/// 此投影仅持有 grain 本地的 <c>NextSeq</c> 与最近若干条目，以便 grain 重启时可继续提供增量拉取。
/// </summary>
[GenerateSerializer]
public sealed class WorldDiffLogPersistedState
{
    /// <summary>下一条 append 将分配的 seq。</summary>
    [Id(0)] public long NextSeq { get; set; } = 1;

    /// <summary>保留窗口中最早的 seq。</summary>
    [Id(1)] public long OldestRetainedSeq { get; set; } = 1;

    /// <summary>
    /// 保留窗口条目：(seq, chunkMortonKey, op)；
    /// 只持久化最近 <c>min(RetainedCount, PersistedRetention)</c> 条，避免行过大。
    /// </summary>
    [Id(2)] public PersistedDiffEntry[] Entries { get; set; } = System.Array.Empty<PersistedDiffEntry>();
}

/// <summary>日志条目的持久化形态。</summary>
[GenerateSerializer]
public readonly record struct PersistedDiffEntry(
    [property: Id(0)] long Seq,
    [property: Id(1)] ulong ChunkMortonKey,
    [property: Id(2)] VoxelOp Op);

using MemoryPack;
using Orleans;

namespace Horizon.Game.Message.World;

/// <summary>
/// 世界 chunk 中的单次操作（P3-a）。<br/>
/// 所有 voxel/prefab 修改都表达为 op，由服务器 <c>IWorldChunkCellGrain</c> 顺序追加到 op log；
/// diff 流 <see cref="Sync.WorldChunkDiffPacket"/> 的 payload 就是 op 列表的序列化字节。
/// </summary>
/// <remarks>
/// - 使用 <see cref="VoxelOpKind"/> 区分子类型，避免 MemoryPack union 过早定型；
/// <see cref="Data"/> 为 op 自带载荷（blittable 四元组）。<br/>
/// - 同一 (LocalX, LocalY, LocalZ) 的后续 op 覆盖前序（"最后写入获胜"），这由 <c>ChunkCellState.Apply</c> 保证。
/// </remarks>
[MemoryPackable]
[GenerateSerializer]
public partial struct VoxelOp
{
    /// <summary>op 类型。</summary>
    [MemoryPackOrder(0)] [Id(0)] public VoxelOpKind Kind;

    /// <summary>相对 chunk 原点的 X 偏移（0..ChunkSize-1）。</summary>
    [MemoryPackOrder(1)] [Id(1)] public byte LocalX;

    /// <summary>相对 chunk 原点的 Y 偏移（0..ChunkSize-1）。</summary>
    [MemoryPackOrder(2)] [Id(2)] public byte LocalY;

    /// <summary>相对 chunk 原点的 Z 偏移（0..ChunkSize-1）。</summary>
    [MemoryPackOrder(3)] [Id(3)] public byte LocalZ;

    /// <summary>保留字节，便于未来扩展 (layer / orientation / metadata)。</summary>
    [MemoryPackOrder(4)] [Id(4)] public byte Reserved;

    /// <summary>方块类型 ID（SetBlock）或 Prefab ID（PlacePrefab）；RemoveBlock/RemovePrefab 忽略。</summary>
    [MemoryPackOrder(5)] [Id(5)] public int PrimaryId;

    /// <summary>次级参数（如方块变体、Prefab 实例 GUID 低 32 位、prefab 旋转角度压缩字段等）。</summary>
    [MemoryPackOrder(6)] [Id(6)] public int SecondaryId;

    /// <summary>产生该 op 的作者（角色 ID / 系统来源，0 表示系统）。</summary>
    [MemoryPackOrder(7)] [Id(7)] public ulong AuthorId;

    /// <summary>服务器 tick（产生时），用于持久化排序与重放。</summary>
    [MemoryPackOrder(8)] [Id(8)] public long ServerTick;
}

/// <summary>voxel op 种类。</summary>
public enum VoxelOpKind : byte
{
    /// <summary>未知/无效。</summary>
    Unknown = 0,
    /// <summary>设置方块（<see cref="VoxelOp.PrimaryId"/> = 方块 ID，<see cref="VoxelOp.SecondaryId"/> = variant）。</summary>
    SetBlock = 1,
    /// <summary>删除方块（把格子置空气）。</summary>
    RemoveBlock = 2,
    /// <summary>放置 Prefab（<see cref="VoxelOp.PrimaryId"/> = prefab ID，<see cref="VoxelOp.SecondaryId"/> = 旋转压缩字段）。</summary>
    PlacePrefab = 3,
    /// <summary>移除 Prefab（<see cref="VoxelOp.PrimaryId"/> = prefab 实例 ID）。</summary>
    RemovePrefab = 4,
}

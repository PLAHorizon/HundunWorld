using System;
using System.Collections.Generic;
using Horizon.Game.Message.World;

namespace Horizon.Game.Core.World.ChunkCell;

/// <summary>
/// 单个 ChunkCell 的权威状态（P3-a）。<br/>
/// 维护两份数据：
/// <list type="number">
///   <item><b>block map</b>：<c>(LocalX, LocalY, LocalZ) → 方块 ID</c> 的稀疏存储（默认 = 空气 0）。</item>
///   <item><b>prefab map</b>：<c>InstanceId → (primaryId, secondaryId, localXYZ)</c> 的字典。</item>
/// </list>
/// 另有 <see cref="OpLog"/> 顺序保存已应用的 op，便于生成 <see cref="Horizon.Game.Message.Sync.WorldChunkDiffPacket"/>。
/// </summary>
/// <remarks>
/// - 纯数据 + 单线程；由 <c>IWorldChunkCellGrain</c> 薄封装并走 Orleans 串行化。<br/>
/// - 当 OpLog 超过 <see cref="Options.MaxOpLogSize"/> 时触发 compact：
/// 对同一 (LocalX, LocalY, LocalZ) 合并为最后一次 op，从而控制内存占用并支持 SQL Server 落盘效率。
/// </remarks>
public sealed class ChunkCellState
{
    /// <summary>ChunkCell 边长（方块数）；16 正好对齐 <c>WorldCoord.MetresPerChunkCell</c> = 16m、1 block = 1m。</summary>
    public const int ChunkSize = 16;

    /// <summary>最大单边坐标（含）。</summary>
    public const int ChunkMax = ChunkSize - 1;

    /// <summary>配置。</summary>
    public sealed class Options
    {
        public int MaxOpLogSize { get; set; } = 4096;
    }

    private readonly Options _options;
    private readonly Dictionary<int, BlockCell> _blocks = new();
    private readonly Dictionary<int, PrefabInstance> _prefabs = new();
    private readonly List<VoxelOp> _opLog = new();

    /// <summary>本 chunk 的 Morton 键（一经构造不可变）。</summary>
    public ulong MortonKey { get; }

    /// <summary>本 chunk 的版本号；每应用一个 op 递增，用于落盘乐观并发。</summary>
    public long Version { get; private set; }

    /// <summary>当前方块数（非空气）。</summary>
    public int BlockCount => _blocks.Count;

    /// <summary>当前 prefab 实例数。</summary>
    public int PrefabCount => _prefabs.Count;

    /// <summary>已记录的 op 数量。</summary>
    public int OpLogSize => _opLog.Count;

    /// <summary>op 日志的只读视图。</summary>
    public IReadOnlyList<VoxelOp> OpLog => _opLog;

    public ChunkCellState(ulong mortonKey, Options? options = null)
    {
        MortonKey = mortonKey;
        _options = options ?? new Options();
        if (_options.MaxOpLogSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "MaxOpLogSize 必须为正数。");
    }

    /// <summary>
    /// 应用一个 op；越界或非法 op 返回 <see cref="ApplyResult.Invalid"/>。
    /// </summary>
    public ApplyResult Apply(in VoxelOp op)
    {
        if (op.LocalX > ChunkMax || op.LocalY > ChunkMax || op.LocalZ > ChunkMax)
            return ApplyResult.OutOfRange;

        var packed = PackLocal(op.LocalX, op.LocalY, op.LocalZ);
        switch (op.Kind)
        {
            case VoxelOpKind.SetBlock:
                _blocks[packed] = new BlockCell(op.PrimaryId, op.SecondaryId);
                break;

            case VoxelOpKind.RemoveBlock:
                _blocks.Remove(packed);
                break;

            case VoxelOpKind.PlacePrefab:
                // PrimaryId 作为 prefab ID，SecondaryId 作为 orientation/variant；使用 packed local 作为实例键。
                _prefabs[packed] = new PrefabInstance(op.PrimaryId, op.SecondaryId,
                    op.LocalX, op.LocalY, op.LocalZ);
                break;

            case VoxelOpKind.RemovePrefab:
                _prefabs.Remove(packed);
                break;

            default:
                return ApplyResult.Invalid;
        }

        _opLog.Add(op);
        Version++;
        if (_opLog.Count >= _options.MaxOpLogSize)
        {
            CompactOpLog();
        }
        return ApplyResult.Applied;
    }

    /// <summary>
    /// 批量应用；遇到非法 op 即停止并返回已成功应用的数量。
    /// </summary>
    public int ApplyBatch(ReadOnlySpan<VoxelOp> ops)
    {
        int applied = 0;
        foreach (var op in ops)
        {
            if (Apply(op) == ApplyResult.Applied) applied++;
            else break;
        }
        return applied;
    }

    /// <summary>查询方块；返回 false 表示该格子为空气。</summary>
    public bool TryGetBlock(byte x, byte y, byte z, out BlockCell cell)
    {
        cell = default;
        if (x > ChunkMax || y > ChunkMax || z > ChunkMax) return false;
        return _blocks.TryGetValue(PackLocal(x, y, z), out cell);
    }

    /// <summary>枚举当前全部 prefab 实例（诊断 / 全量下发用）。</summary>
    public IReadOnlyCollection<PrefabInstance> EnumeratePrefabs() => _prefabs.Values;

    /// <summary>
    /// 返回从 <paramref name="sinceLogIndex"/>（含）开始的 op 子列表。
    /// 便于 <c>IWorldChunkCellGrain</c> 响应客户端的 "拉取最近 diff"。
    /// </summary>
    public IReadOnlyList<VoxelOp> ReadOpsSince(int sinceLogIndex)
    {
        if (sinceLogIndex <= 0) return _opLog;
        if (sinceLogIndex >= _opLog.Count) return Array.Empty<VoxelOp>();
        var slice = new VoxelOp[_opLog.Count - sinceLogIndex];
        _opLog.CopyTo(sinceLogIndex, slice, 0, slice.Length);
        return slice;
    }

    /// <summary>
    /// 清空 op log 并用当前状态合成一批"最小 op 集合"。
    /// 原则：对每个仍存在的方块 emit 一条 <see cref="VoxelOpKind.SetBlock"/>；
    /// 对每个 prefab emit 一条 <see cref="VoxelOpKind.PlacePrefab"/>。
    /// 已删除的格子从 log 中消失（因已删除）。
    /// </summary>
    /// <remarks>调用方通常仅在 <c>OpLogSize ≥ MaxOpLogSize</c> 时间接触发；亦可手动用于 checkpoint。</remarks>
    public int CompactOpLog()
    {
        var before = _opLog.Count;
        _opLog.Clear();

        foreach (var kv in _blocks)
        {
            var (x, y, z) = UnpackLocal(kv.Key);
            _opLog.Add(new VoxelOp
            {
                Kind = VoxelOpKind.SetBlock,
                LocalX = x, LocalY = y, LocalZ = z,
                PrimaryId = kv.Value.BlockId,
                SecondaryId = kv.Value.Variant,
            });
        }
        foreach (var kv in _prefabs)
        {
            var p = kv.Value;
            _opLog.Add(new VoxelOp
            {
                Kind = VoxelOpKind.PlacePrefab,
                LocalX = p.X, LocalY = p.Y, LocalZ = p.Z,
                PrimaryId = p.PrefabId,
                SecondaryId = p.OrientationBits,
            });
        }
        return before - _opLog.Count;
    }

    /// <summary>把 (x, y, z) ∈ [0, 15]³ 打包成 12 位 int 做 Dict 键。</summary>
    private static int PackLocal(byte x, byte y, byte z) => (x << 8) | (y << 4) | z;

    private static (byte X, byte Y, byte Z) UnpackLocal(int packed) =>
        ((byte)((packed >> 8) & 0xF), (byte)((packed >> 4) & 0xF), (byte)(packed & 0xF));
}

/// <summary><see cref="ChunkCellState.Apply"/> 的结果。</summary>
public enum ApplyResult : byte
{
    /// <summary>应用成功。</summary>
    Applied = 1,
    /// <summary>local 坐标越界。</summary>
    OutOfRange = 2,
    /// <summary>未知 op 类型。</summary>
    Invalid = 3,
}

/// <summary>一个方块的权威状态。</summary>
public readonly struct BlockCell
{
    public int BlockId { get; }
    public int Variant { get; }
    public BlockCell(int blockId, int variant) { BlockId = blockId; Variant = variant; }
}

/// <summary>一个 prefab 实例。</summary>
public readonly struct PrefabInstance
{
    public int PrefabId { get; }
    public int OrientationBits { get; }
    public byte X { get; }
    public byte Y { get; }
    public byte Z { get; }
    public PrefabInstance(int prefabId, int orientationBits, byte x, byte y, byte z)
    {
        PrefabId = prefabId;
        OrientationBits = orientationBits;
        X = x; Y = y; Z = z;
    }
}

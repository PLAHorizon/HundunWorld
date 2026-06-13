using System;
using System.Collections.Generic;
using Horizon.Game.Message.World;

namespace Horizon.Game.Core.World;

/// <summary>
/// 全局 world diff 日志的纯逻辑核心（P3-b）。<br/>
/// 聚合多 Chunk 的 <see cref="VoxelOp"/> 到单调递增的"全局 seq"序列，
/// 供 <see cref="Horizon.Game.Message.Sync.WorldChunkDiffPacket"/> 携带 <c>DiffSeqStart/End</c> 做流式下发。
/// </summary>
/// <remarks>
/// 设计要点：
/// <list type="bullet">
///   <item>单调递增的 seq；在 P4-a 切到 SQL Server 时，seq 会来自 <c>IDENTITY</c> 列，此实现只是前置契约。</item>
///   <item>内部维持一个环形缓冲（长度 <see cref="Options.RetentionSize"/>），
///   用于服务最近在线玩家的 "增量拉取" 请求；超期的 seq 需要重传 baseline。</item>
///   <item>线程不安全——由 Orleans grain 的 turn-based 执行保证串行。</item>
/// </list>
/// </remarks>
public sealed class WorldDiffLog
{
    /// <summary>配置。</summary>
    public sealed class Options
    {
        /// <summary>环形缓冲容量；写入后超过此值的最早条目被淘汰。</summary>
        public int RetentionSize { get; set; } = 65536;
    }

    private readonly Options _options;
    private readonly LinkedList<Entry> _entries = new();
    private long _nextSeq = 1; // 1-based，0 保留为 "未知"
    private long _oldestRetainedSeq = 1;

    public WorldDiffLog(Options? options = null)
    {
        _options = options ?? new Options();
        if (_options.RetentionSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "RetentionSize 必须为正数。");
    }

    /// <summary>下一条 append 将获得的 seq。</summary>
    public long NextSeq => _nextSeq;

    /// <summary>当前保留的最旧 seq；&lt; 该值的请求必须走 baseline 重传。</summary>
    public long OldestRetainedSeq => _oldestRetainedSeq;

    /// <summary>已保留的 entry 数量。</summary>
    public int RetainedCount => _entries.Count;

    /// <summary>
    /// 追加一条 diff 条目。
    /// </summary>
    /// <returns>分配到的全局 seq。</returns>
    public long Append(ulong chunkMortonKey, VoxelOp op)
    {
        var seq = _nextSeq++;
        _entries.AddLast(new Entry(seq, chunkMortonKey, op));
        while (_entries.Count > _options.RetentionSize)
        {
            var first = _entries.First!.Value;
            _entries.RemoveFirst();
            _oldestRetainedSeq = first.Seq + 1;
        }
        if (_entries.Count == 1) _oldestRetainedSeq = seq;
        return seq;
    }

    /// <summary>
    /// 批量 append；返回起始 seq（含）与结束 seq（含）。
    /// </summary>
    public (long Start, long End) AppendBatch(ulong chunkMortonKey, ReadOnlySpan<VoxelOp> ops)
    {
        if (ops.IsEmpty) return (_nextSeq, _nextSeq - 1);
        var start = _nextSeq;
        foreach (var op in ops) Append(chunkMortonKey, op);
        return (start, _nextSeq - 1);
    }

    /// <summary>
    /// 拉取 <paramref name="sinceExclusive"/> 之后（不含）的 entry；若 &lt; <see cref="OldestRetainedSeq"/>
    /// 则返回 <see cref="ReadResult.RetentionExceeded"/>，调用方须让客户端走 baseline 重传。
    /// </summary>
    public ReadResult Read(long sinceExclusive)
    {
        if (sinceExclusive < 0)
            return new ReadResult(ReadKind.Invalid, Array.Empty<Entry>(), _nextSeq - 1);

        if (_entries.Count == 0)
            return new ReadResult(ReadKind.Empty, Array.Empty<Entry>(), _nextSeq - 1);

        // 请求的起点比我们保留的还旧 → retention 失效
        if (sinceExclusive + 1 < _oldestRetainedSeq)
            return new ReadResult(ReadKind.RetentionExceeded, Array.Empty<Entry>(), _nextSeq - 1);

        var list = new List<Entry>();
        foreach (var e in _entries)
        {
            if (e.Seq > sinceExclusive) list.Add(e);
        }
        return new ReadResult(ReadKind.Ok, list, _nextSeq - 1);
    }

    /// <summary>单条目。</summary>
    public readonly struct Entry
    {
        public long Seq { get; }
        public ulong ChunkMortonKey { get; }
        public VoxelOp Op { get; }
        public Entry(long seq, ulong chunkMortonKey, VoxelOp op)
        {
            Seq = seq; ChunkMortonKey = chunkMortonKey; Op = op;
        }
    }

    /// <summary>读取结果。</summary>
    public readonly struct ReadResult
    {
        public ReadKind Kind { get; }
        public IReadOnlyList<Entry> Entries { get; }
        public long HeadSeq { get; }
        public ReadResult(ReadKind kind, IReadOnlyList<Entry> entries, long headSeq)
        {
            Kind = kind; Entries = entries; HeadSeq = headSeq;
        }
    }

    /// <summary>读取结果枚举。</summary>
    public enum ReadKind : byte
    {
        /// <summary>正常返回；<see cref="ReadResult.Entries"/> 可能为空，但对齐 head。</summary>
        Ok = 1,
        /// <summary>日志为空（服务器刚启动）。</summary>
        Empty = 2,
        /// <summary>请求的起点太旧，保留窗口已不覆盖；调用方须让客户端重拉 baseline。</summary>
        RetentionExceeded = 3,
        /// <summary>非法入参（如负数）。</summary>
        Invalid = 4,
    }
}

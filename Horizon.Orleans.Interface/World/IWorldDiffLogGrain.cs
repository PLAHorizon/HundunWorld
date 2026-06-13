using System.Threading.Tasks;
using Orleans;
using Horizon.Game.Message.World;

namespace Horizon.Orleans.Interface.World;

/// <summary>
/// 全局（或按 shard 分片的）world diff 日志 grain 契约（P3-b）。<br/>
/// 初版以单例 <c>IGrainWithStringKey = "global"</c> 承载；
/// P4-a 之后可按 Morton 高位 shard，减小锁竞争。
/// </summary>
[global::Orleans.CodeGeneration.Version(1)]
public interface IWorldDiffLogGrain : IGrainWithStringKey
{
    /// <summary>追加一批 op（同 chunk），返回 (startSeq, endSeq) 区间（含）。</summary>
    Task<(long Start, long End)> AppendAsync(ulong chunkMortonKey, VoxelOp[] ops);

    /// <summary>
    /// 从 <paramref name="sinceExclusive"/> 之后拉取日志；若 retention 超期则 <paramref name="retentionExceeded"/> = true。
    /// 返回的 entries 在 <see cref="WorldDiffLogEntry.Seq"/> 上单调递增。
    /// </summary>
    Task<WorldDiffLogReadResponse> ReadAsync(long sinceExclusive);

    /// <summary>返回当前 head / oldest seq / retained count。</summary>
    Task<WorldDiffLogStats> GetStatsAsync();
}

/// <summary>序列化友好的日志条目。</summary>
[GenerateSerializer]
public readonly record struct WorldDiffLogEntry(
    [property: Id(0)] long Seq,
    [property: Id(1)] ulong ChunkMortonKey,
    [property: Id(2)] VoxelOp Op);

/// <summary><see cref="IWorldDiffLogGrain.ReadAsync"/> 的响应。</summary>
[GenerateSerializer]
public sealed class WorldDiffLogReadResponse
{
    [Id(0)] public WorldDiffLogEntry[] Entries { get; set; } = System.Array.Empty<WorldDiffLogEntry>();
    [Id(1)] public long HeadSeq { get; set; }
    [Id(2)] public bool RetentionExceeded { get; set; }
}

/// <summary>日志统计。</summary>
[GenerateSerializer]
public readonly record struct WorldDiffLogStats(
    [property: Id(0)] long NextSeq,
    [property: Id(1)] long OldestRetainedSeq,
    [property: Id(2)] int RetainedCount);

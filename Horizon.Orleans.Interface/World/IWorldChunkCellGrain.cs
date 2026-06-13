using System.Threading.Tasks;
using Orleans;
using Horizon.Game.Message.World;

namespace Horizon.Orleans.Interface.World;

/// <summary>
/// 单个 ChunkCell 的权威 grain 契约（P3-a）。<br/>
/// Grain Primary Key = ChunkCell 的 Morton 键。
/// </summary>
/// <remarks>
/// 持久化分阶段：
/// <list type="bullet">
///   <item>P3-a（本次）：纯内存；grain 重新激活时状态丢失。用于联调。</item>
///   <item>P4-a：绑定 <c>[PersistentState("chunk", "WorldSqlStore")]</c>，落 SQL Server <c>chunk_state</c> 表。</item>
///   <item>P4-b：与 <see cref="IWorldDiffLogGrain"/> 联动产出带全局 seq 的 <c>WorldChunkDiffPacket</c>。</item>
/// </list>
/// </remarks>
[global::Orleans.CodeGeneration.Version(1)]
public interface IWorldChunkCellGrain : IGrainWithIntegerKey
{
    /// <summary>应用一批 op；返回成功应用的数量。</summary>
    Task<int> ApplyOpsAsync(VoxelOp[] ops);

    /// <summary>
    /// 拉取自 <paramref name="sinceVersion"/> 起的 op 列表；
    /// <paramref name="sinceVersion"/> &lt; 0 时视为 0。
    /// </summary>
    Task<VoxelOp[]> ReadOpsSinceAsync(int sinceVersion);

    /// <summary>返回当前 (version, blockCount, prefabCount, opLogSize)，用于诊断与版本仲裁。</summary>
    Task<ChunkCellStats> GetStatsAsync();

    /// <summary>手动触发 compact（把 op log 压缩为最小 op 集合）。</summary>
    Task<int> CompactAsync();
}

/// <summary>ChunkCell 统计快照。</summary>
[GenerateSerializer]
public readonly record struct ChunkCellStats(
    [property: Id(0)] long Version,
    [property: Id(1)] int BlockCount,
    [property: Id(2)] int PrefabCount,
    [property: Id(3)] int OpLogSize);

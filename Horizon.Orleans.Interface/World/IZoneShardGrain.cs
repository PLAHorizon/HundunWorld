using System;
using System.Threading.Tasks;
using Orleans;
using Horizon.Game.Message.Sync;

namespace Horizon.Orleans.Interface.World;

/// <summary>
/// Zone 分片 grain 契约（P2-b）。<br/>
/// 一个 ZoneShard 负责若干 ChunkCell 的权威状态 + AOI 订阅 + 批量扇出。
/// Grain Primary Key = shardId（由上层根据 chunk 哈希分配）。
/// </summary>
/// <remarks>
/// 本契约只暴露 P2-b 所需的订阅/扇出/tick 操作。P3+ 会扩展：
/// <list type="bullet">
///   <item>持久化 chunk op log（到 SQL Server 的 <c>chunk_state</c> 表）。</item>
///   <item>与 <see cref="IWorldDiffLogGrain"/> 对接产生 <see cref="WorldChunkDiffPacket"/>。</item>
/// </list>
/// </remarks>
[global::Orleans.CodeGeneration.Version(1)]
public interface IZoneShardGrain : IGrainWithIntegerKey
{
    /// <summary>给 sessionId 订阅一组 chunk（Morton 键数组）。返回新增订阅条数。</summary>
    Task<int> SubscribeSessionAsync(long sessionId, ulong[] mortonKeys);

    /// <summary>给 sessionId 退订一组 chunk。返回移除条数。</summary>
    Task<int> UnsubscribeSessionAsync(long sessionId, ulong[] mortonKeys);

    /// <summary>会话整体离线清理。返回被移除的条数。</summary>
    Task<int> RemoveSessionAsync(long sessionId);

    /// <summary>
    /// 对一批 <see cref="WorldChunkDiffPacket"/> 做扇出，返回"每个目标 sessionId 收到哪些 diff 下标"的映射，
    /// 上层据此走 Gateway 本地扇出或 stream pub-sub。
    /// </summary>
    /// <param name="diffs">本批待广播的 diff；每个元素的 <see cref="WorldChunkDiffPacket.ChunkMortonKey"/> 决定目标。</param>
    Task<FanOutResult[]> BroadcastChunkDiffsAsync(WorldChunkDiffPacket[] diffs);

    /// <summary>返回订阅了给定 chunk 的 sessionId 数组（诊断 / 监控用）。</summary>
    Task<long[]> GetSubscribersAsync(ulong mortonKey);

    /// <summary>返回 (会话数, chunk 数) 快照。</summary>
    Task<(int SessionCount, int ChunkCount)> GetStatsAsync();

    /// <summary>
    /// 注册一个 <see cref="IZoneShardFanoutObserver"/> 到本分片（P6-b 运行时连线）。<br/>
    /// 每次 <see cref="BroadcastChunkDiffsAsync"/> 产生扇出时，grain 会遍历所有已注册观察者并调用
    /// <see cref="IZoneShardFanoutObserver.OnChunkDiffAsync"/>。一个 gateway 实例用一个固定
    /// <paramref name="subscriptionId"/> 多次注册等价于覆盖（幂等）。
    /// </summary>
    Task SubscribeFanoutAsync(Guid subscriptionId, IZoneShardFanoutObserver observer);

    /// <summary>按 <paramref name="subscriptionId"/> 退订本分片 fanout（通常在 gateway 下线/关闭时调用）。</summary>
    Task UnsubscribeFanoutAsync(Guid subscriptionId);

    /// <summary>
    /// 执行一次 tick 周期：回放所有已注册实体的输入序列，校验位置偏差并生成 correction。
    /// </summary>
    /// <param name="tickTime">本次 tick 的模拟时间戳（秒）。</param>
    /// <returns>本次 tick 处理的实体数量。</returns>
    Task<int> TickAsync(double tickTime);

    /// <summary>
    /// 注册一个模拟实体到本分片。
    /// </summary>
    /// <param name="entityId">实体 ID。</param>
    /// <param name="initialX">初始位置 X。</param>
    /// <param name="initialY">初始位置 Y。</param>
    /// <param name="initialZ">初始位置 Z。</param>
    /// <param name="maxSpeed">最大水平速度（米/秒）。</param>
    Task RegisterEntityAsync(ulong entityId, float initialX, float initialY, float initialZ, float maxSpeed = 6f);

    /// <summary>
    /// 从本分片注销一个模拟实体。
    /// </summary>
    /// <param name="entityId">实体 ID。</param>
    Task UnregisterEntityAsync(ulong entityId);

    /// <summary>
    /// 向实体追加输入包（由上层调用，通常在收到客户端 input 时）。
    /// </summary>
    /// <param name="entityId">目标实体 ID。</param>
    /// <param name="input">输入包。</param>
    /// <param name="reportedEndX">客户端报告的终点 X。</param>
    /// <param name="reportedEndY">客户端报告的终点 Y。</param>
    /// <param name="reportedEndZ">客户端报告的终点 Z。</param>
    Task SubmitInputAsync(ulong entityId, InputPacket input, float reportedEndX, float reportedEndY, float reportedEndZ);

    Task SubmitSkillCastAsync(ulong entityId, int skillId, ulong targetId);

    Task CompleteSkillCastAsync(ulong entityId, float damage, ulong targetId, bool isCritical);
}

/// <summary>扇出结果：一个 session 收到哪些 diff（按原 <c>diffs</c> 数组下标表示）。</summary>
[GenerateSerializer]
public readonly record struct FanOutResult(
    [property: Id(0)] long SessionId,
    [property: Id(1)] int[] DiffIndices);

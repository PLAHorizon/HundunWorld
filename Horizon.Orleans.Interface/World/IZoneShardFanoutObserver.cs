using System.Collections.Generic;
using System.Threading.Tasks;
using Horizon.Game.Message.Sync;
using Orleans;

namespace Horizon.Orleans.Interface.World;

/// <summary>
/// 批量 fanout 推送条目：一个已解算好 AOI 受众的 diff + 其受众 session 列表。
/// 用于 <see cref="IZoneShardFanoutObserver.OnChunkDiffBatchAsync"/>，把一次广播内的
/// 全部 chunk diff / correction / InputAck 合并为单条 Orleans observer 消息，
/// 替代原先“每 chunk × 每 observer 一次 RPC”的串行推送（推送效率优化 P-F1）。
/// </summary>
[GenerateSerializer]
public sealed class FanoutBatchItem
{
    /// <summary>待广播的 diff（Payload 已序列化，gateway 按需转发）。</summary>
    [Id(0)]
    public WorldChunkDiffPacket Diff { get; set; } = null!;

    /// <summary>应接收 <see cref="Diff"/> 的 session 列表（== characterId 列表）。</summary>
    [Id(1)]
    public long[] SessionIds { get; set; } = System.Array.Empty<long>();
}

/// <summary>
/// Gateway 侧注册到 <see cref="IZoneShardGrain"/> 的 fanout 观察者（P6-b 运行时连线）。<br/>
/// 当 <see cref="IZoneShardGrain.BroadcastChunkDiffsAsync"/> 计算完 AOI 扇出表后，会对每个订阅者
/// 调用 <see cref="OnChunkDiffAsync"/>，把单个 <see cref="WorldChunkDiffPacket"/> 连同应接收该包
/// 的 sessionId 列表一并推给 gateway；gateway 再通过 <c>GatewaySyncDispatcher</c> 写回客户端。
/// </summary>
/// <remarks>
/// 设计要点：
/// <list type="bullet">
///   <item>契约放在 Orleans.Interface 下，grain 与 gateway 共享同一份 proxy 代码。</item>
///   <item>回调是 void-ish（返回 Task）并由 grain 在单条 await 尾部触发，调用方（grain）不阻塞主循环；
///         grain 侧已在 <see cref="IMUserGrain"/> 中验证该模式：观察者异常会被吞并记日志，不影响主流程。</item>
///   <item>Task 14：<paramref name="sessionIds"/> 改为 <see cref="IReadOnlyCollection{T}"/> 接口，
///         允许调用方直接传递 AOI 内部 HashSet 视图，避免热路径 <c>ToArray()</c> 分配。</item>
/// </list>
/// </remarks>
public interface IZoneShardFanoutObserver : IGrainObserver
{
    /// <summary>
    /// 推送一条"已解算好 AOI 受众"的 diff。
    /// </summary>
    /// <param name="diff">待广播的 <see cref="WorldChunkDiffPacket"/>（gateway 按需序列化）。</param>
    /// <param name="sessionIds">订阅了 <paramref name="diff"/>.<see cref="WorldChunkDiffPacket.ChunkMortonKey"/> 的 session 列表。</param>
    Task OnChunkDiffAsync(WorldChunkDiffPacket diff, IReadOnlyCollection<long> sessionIds);

    /// <summary>
    /// 批量推送一轮广播产生的全部 diff（推送效率优化 P-F1）。
    /// 一次广播（快照 delta + correction + InputAck）合并为单条 observer 消息，
    /// 将 grain→gateway 的跨进程 RPC 次数从 O(chunk数) 降为 O(1)，
    /// 同时消除 grain turn 内的多次序列化/发送开销。
    /// </summary>
    /// <remarks>
    /// 默认实现逐条回退到 <see cref="OnChunkDiffAsync"/>，保证仅实现旧契约的观察者
    /// （历史测试 mock 等）无需修改即可继续工作；gateway 生产实现直接覆写本方法走批量入队。
    /// </remarks>
    /// <param name="items">本批 diff 条目（顺序即广播顺序）。</param>
    Task OnChunkDiffBatchAsync(FanoutBatchItem[] items)
    {
        if (items is null || items.Length == 0)
            return Task.CompletedTask;
        return PushBatchFallbackAsync(this, items);
    }

    private static async Task PushBatchFallbackAsync(IZoneShardFanoutObserver self, FanoutBatchItem[] items)
    {
        foreach (var item in items)
        {
            if (item?.Diff is null || item.SessionIds is null || item.SessionIds.Length == 0)
                continue;
            await self.OnChunkDiffAsync(item.Diff, item.SessionIds).ConfigureAwait(false);
        }
    }
}

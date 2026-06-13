using System.Threading.Tasks;
using Horizon.Game.Message.Sync;
using Orleans;

namespace Horizon.Orleans.Interface.World;

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
/// </list>
/// </remarks>
public interface IZoneShardFanoutObserver : IGrainObserver
{
    /// <summary>
    /// 推送一条"已解算好 AOI 受众"的 diff。
    /// </summary>
    /// <param name="diff">待广播的 <see cref="WorldChunkDiffPacket"/>（gateway 按需序列化）。</param>
    /// <param name="sessionIds">订阅了 <paramref name="diff"/>.<see cref="WorldChunkDiffPacket.ChunkMortonKey"/> 的 session 列表。</param>
    Task OnChunkDiffAsync(WorldChunkDiffPacket diff, long[] sessionIds);
}

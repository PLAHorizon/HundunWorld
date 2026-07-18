using System.Collections.Generic;

namespace Horizon.Game.ECS.Arch.Components;

/// <summary>
/// 记录本地玩家当前的 chunk 订阅状态，用于检测跨 chunk 边界时触发订阅更新。
/// </summary>
/// <remarks>
/// 由 <see cref="Horizon.Game.ECS.Arch.Systems.LocalSimulationSystem"/> 在每次 tick 中读写：
/// 比较本地玩家当前所在 chunk 的 MortonKey 与 <see cref="CurrentChunkKey"/>，
/// 不一致时触发 <c>PlayerChunkChanged</c> 事件，由 <c>NetworkRuntime</c> 计算并上行
/// <see cref="Horizon.Game.Message.Sync.SubscriptionUpdatePacket"/>。
/// <see cref="SubscribedChunks"/> 由 NetworkRuntime 在收到服务端确认后回写，作为下一帧 diff 计算的基准。
/// </remarks>
public struct PlayerSubscriptionStateComponent
{
    /// <summary>当前玩家所在 chunk 的 MortonKey。</summary>
    public ulong CurrentChunkKey;

    /// <summary>当前已订阅的 chunk MortonKey 集合。</summary>
    public HashSet<ulong>? SubscribedChunks;

    /// <summary>是否已初始化（首次握手后设置）。</summary>
    public bool Initialized;
}

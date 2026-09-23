using System;
using System.Threading;
using System.Threading.Tasks;

namespace Horizon.Game.Core.Interfaces;

/// <summary>
/// 阶段 2.2 — UE5 DS 数据面抽象（RouteToDSByShard 灰度通道）。
/// <para>
/// 当 <see cref="IsEnabled"/> 为 true 时，<see cref="Horizon.Game.Core.Handlers.SyncPacketHandler"/>
/// 对 DS 模拟范围内的上行包（Handshake/Input/ReconnectResume）在触碰 Orleans grain 之前，
/// 先按 shard 尝试转发到承载该 shard 的 UE5 DS；转发成功则网关不再触碰 Orleans 移动权威，
/// 失败（无可用 DS / 传输异常）则回退旧 Orleans 路径——实现"灰度期双轨 + 任一时点可回切"。
/// 默认（UseDS=false）网关不注册实现，handler 以 null 兜底，零行为变化。
/// </para>
/// </summary>
public interface IDSyncDataPlane
{
    /// <summary>开关：false 时网关完全走旧 Orleans 路径。</summary>
    bool IsEnabled { get; }

    /// <summary>
    /// 按 shard 尝试把客户端上行同步帧（<see cref="Horizon.Game.Message.Sync.SyncPacketCodec"/>
    /// 编码的原始帧）转发给承载该 shard 的 UE5 DS。
    /// </summary>
    /// <param name="shardId">目标 shard（由 <see cref="Horizon.Game.Core.World.IShardRouter"/> 解析）。</param>
    /// <param name="frame">SyncPacketCodec 编码的原始同步帧（DS 侧直接解码回放）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>true = 已转发（调用方不得再走 Orleans 移动权威路径）；false = 无可用 DS 或转发失败（调用方回退旧路径）。</returns>
    Task<bool> TryRouteAsync(long shardId, long characterId, ReadOnlyMemory<byte> frame, CancellationToken cancellationToken = default);

    /// <summary>
    /// 该会话当前是否**由 UE5 DS 承载权威**（而不是 Orleans <c>ZoneShardGrain</c>）。
    /// <para>
    /// <b>为什么需要它</b>：网关里存在若干"实体是否还在 <c>ZoneShardGrain</c> 中"的健康判定
    /// （实体租约续约、Presence 监控、Despawn 编排）。这些判定写于 DS 通道存在之前，
    /// 隐含假设"实体一定在 ZoneShardGrain 里"。启用 DS 权威后该假设失效：
    /// 实体住在 DS 的 <c>DsWorldSession</c> 中，ZoneShardGrain 查不到它，
    /// 于是网关会**把自己的玩家当成幽灵踢下线**（实测：真客户端进世界约 13 秒后被关闭连接）。
    /// </para>
    /// <para>
    /// 判定为 DS 权威时，这些基于 ZoneShardGrain 的存活判定必须**跳过**——
    /// 该会话的租约与生命周期由 DS 负责。
    /// </para>
    /// </summary>
    /// <param name="shardId">会话所属 shard。</param>
    /// <param name="characterId">角色 ID（参与灰度分组判定）。</param>
    /// <returns>true = DS 承载且确实有可用 DS；false = 未启用 / 不在灰度 / 无可用 DS（均回退 Orleans 语义）。</returns>
    bool IsSessionDsAuthoritative(long shardId, long characterId);
}

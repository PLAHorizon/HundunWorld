using System;
using System.Threading;
using System.Threading.Tasks;
using Horizon.Game.Core.Interfaces;
using Horizon.Game.Core.Observability;
using Horizon.Game.Core.World;
using Horizon.Game.Message.Sync;
using Microsoft.Extensions.Logging;
using Orleans;

namespace Horizon.Game.Core.Handlers;

/// <summary>
/// 阶段 3.2 — SyncPacket 按 <see cref="SyncPacketKind"/> 拆分后的流处理器公共基类。
/// <para>
/// 持有各流处理器共享的依赖（cluster client / shard 路由 / UE5 DS 数据面 / 会话绑定校验器 /
/// 会话状态 / 连接 ID 提供者），并提供阶段 2.2 的 DS 灰度路由辅助（<see cref="TryRouteToDsAsync"/>）。
/// 由 <see cref="SyncPacketHandler"/> 构造并注入，自身不参与 IMessageHandler 注册。
/// </para>
/// </summary>
public abstract class SyncPacketFlowHandlerBase
{
    /// <summary>日志器（与 <see cref="SyncPacketHandler"/> 同一类别，兼容既有测试构造）。</summary>
    protected readonly ILogger<MessageHandlerBase> Logger;

    /// <summary>Orleans cluster client（grain 调用）。</summary>
    protected readonly IClusterClient ClusterClient;

    /// <summary>Shard 路由器（替换原 DefaultShardId 硬编码）。</summary>
    protected readonly IShardRouter ShardRouter;

    /// <summary>阶段 2.2：UE5 DS 数据面（灰度通道）。null/禁用 → 完全走旧 Orleans 路径。</summary>
    protected readonly IDSyncDataPlane? DsDataPlane;

    /// <summary>Phase 2：会话绑定校验器（可选，灰度开关控制）。</summary>
    protected readonly ISessionBindingValidator? SessionBindingValidator;

    /// <summary>各流处理器共享的按连接会话状态（去重/握手/速率限制）。</summary>
    protected readonly SyncSessionState SessionState;

    /// <summary>当前请求的连接 ID 提供者（AsyncLocal，由 <see cref="SyncPacketHandler.HandleAsync"/> 设置）。</summary>
    protected readonly Func<string> ConnectionIdProvider;

    protected SyncPacketFlowHandlerBase(
        ILogger<MessageHandlerBase> logger,
        IClusterClient clusterClient,
        IShardRouter shardRouter,
        IDSyncDataPlane? dsDataPlane,
        ISessionBindingValidator? sessionBindingValidator,
        SyncSessionState sessionState,
        Func<string> connectionIdProvider)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ClusterClient = clusterClient ?? throw new ArgumentNullException(nameof(clusterClient));
        ShardRouter = shardRouter ?? throw new ArgumentNullException(nameof(shardRouter));
        DsDataPlane = dsDataPlane;
        SessionBindingValidator = sessionBindingValidator;
        SessionState = sessionState ?? throw new ArgumentNullException(nameof(sessionState));
        ConnectionIdProvider = connectionIdProvider ?? throw new ArgumentNullException(nameof(connectionIdProvider));
    }

    /// <summary>当前请求的连接 ID（AsyncLocal 值；无连接上下文时为空串）。</summary>
    protected string CurrentConnectionId => ConnectionIdProvider();

    /// <summary>
    /// 阶段 2.2：RouteToDSByShard 灰度路由。开关开启且该 shard 有已注册的 UE5 DS 时，
    /// 把上行同步包重新编码为原始帧并转发 DS（阶段 2.4 以 SimWorld 回放并回权威 Snapshot/InputAck）；
    /// 返回 true 表示已转发（调用方须返回透传 ack，不再触碰 Orleans 移动权威）；false 回退旧路径。
    /// </summary>
    protected async Task<bool> TryRouteToDsAsync(SyncPacket packet, long characterId)
    {
        // 阶段 6.3：开关关闭 → 记录回退 reason=disabled（DS 通道未启用时的全量回退基线；
        // 与开启后的 no_ds/breaker/timeout/transport_error 合起来回答「进 DS 比例 vs 回退比例」）。
        if (DsDataPlane is not { IsEnabled: true })
        {
            SyncMetrics.DsRouteFallback.Add(1, new KeyValuePair<string, object?>("reason", "disabled"));
            return false;
        }
        if (characterId <= 0) return false;

        var shardId = ShardRouter.Resolve(characterId);
        SyncPacketCodec.Encode(packet, out var frame, out var frameLength);
        try
        {
            // frame 为池化缓冲区：DsSyncDataPlane 在首个 await 前同步完成拷贝，
            // 此处 await 结束后归还池中不影响已发送的 payload。
            var forwarded = await DsDataPlane.TryRouteAsync(shardId, characterId, frame.AsMemory(0, frameLength), CancellationToken.None);
            if (forwarded)
            {
                // 上行成功是逐帧高频路径，且 DsSyncDataPlane 已在成功点做采样摘要
                // （每 1000 帧一条 Information）；此处逐帧 Information 属于重复日志，降为 Debug。
                Logger.LogDebug(
                    "SyncPacket 已转发到 UE5 DS。Kind={Kind}, CharacterId={CharacterId}, ShardId={ShardId}",
                    packet.Kind, characterId, shardId);
            }
            return forwarded;
        }
        finally
        {
            SyncPacketCodec.ReturnFrame(frame);
        }
    }
}

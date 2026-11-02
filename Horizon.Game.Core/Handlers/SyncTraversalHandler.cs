using System;
using System.Threading.Tasks;
using Horizon.Game.Core.Interfaces;
using Horizon.Game.Core.World;
using Horizon.Game.Message.Sync;
using Horizon.Game.Sim;
using Horizon.Orleans.Interface.World;
using Microsoft.Extensions.Logging;
using Orleans;

namespace Horizon.Game.Core.Handlers;

/// <summary>
/// v17 翻越（Traversal）请求处理器：客户端计划 → DS 数据面 / ZoneShardGrain（SimWorld）裁决。
/// <para>
/// 与 <see cref="SyncCombatHandler"/> 同一条路由纪律：DS 权威会话必须先转 DS；
/// 转成功则本处返回 null（结果包由 DS 下行事件回推），避免两个权威各发一份。
/// </para>
/// <para>
/// 计划校验本身不在这里做——唯一实现是 <see cref="SimWorld.TryBeginTraversal"/>，
/// 它持有权威位置与 <see cref="TraversalLimits"/>；网关只负责鉴权（会话绑定）与路由。
/// </para>
/// </summary>
public sealed class SyncTraversalHandler : SyncPacketFlowHandlerBase
{
    public SyncTraversalHandler(
        ILogger<MessageHandlerBase> logger,
        IClusterClient clusterClient,
        IShardRouter shardRouter,
        IDSyncDataPlane? dsDataPlane,
        ISessionBindingValidator? sessionBindingValidator,
        SyncSessionState sessionState,
        Func<string> connectionIdProvider)
        : base(logger, clusterClient, shardRouter, dsDataPlane, sessionBindingValidator, sessionState, connectionIdProvider)
    {
    }

    public async Task<SyncPacket?> HandleTraversalRequestAsync(TraversalRequestPacket? request)
    {
        if (request is null || request.CharacterId == 0)
            return Reject(request, TraversalRejectReason.StartTooFar);

        var characterId = (long)request.CharacterId;

        // 翻越会直接改权威位置，与输入同一安全级别：先做会话绑定校验再触碰任何权威。
        var connId = CurrentConnectionId;
        if (SessionBindingValidator is not null && !string.IsNullOrEmpty(connId)
            && !SessionBindingValidator.IsCharacterBoundToConnection(connId, characterId))
        {
            Logger.LogWarning(
                "翻越请求会话绑定校验失败，已拦截。CharacterId={CharacterId}, ConnectionId={ConnectionId}, ClientTick={ClientTick}",
                characterId, connId, request.ClientTick);
            return Reject(request, TraversalRejectReason.AuthorityUnavailable);
        }

        if (DsDataPlane is { IsEnabled: true })
        {
            var shardId = ShardRouter.Resolve(characterId);
            if (DsDataPlane.IsSessionDsAuthoritative(shardId, characterId)
                && await TryRouteToDsAsync(request, characterId))
            {
                // 结果由 DS 直接按会话下行（与 CorpseLoot 同一纪律），网关不再合成。
                return null;
            }
        }

        try
        {
            var shard = ClusterClient.GetGrain<IZoneShardGrain>(ShardRouter.Resolve(characterId));
            return await shard.SubmitTraversalRequestAsync(request);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex,
                "翻越请求转发权威失败。CharacterId={CharacterId}, ClientTick={ClientTick}",
                characterId, request.ClientTick);
            return Reject(request, TraversalRejectReason.AuthorityUnavailable);
        }
    }

    private static TraversalResultPacket Reject(TraversalRequestPacket? request, TraversalRejectReason reason)
        => new TraversalResultPacket
        {
            CharacterId = request?.CharacterId ?? 0UL,
            ClientTick = request?.ClientTick ?? 0L,
            Accepted = false,
            Status = (byte)reason,
            // 拒绝时没有权威坐标可回显：回零，客户端据 Accepted=false 取消本地预测并等待修正包。
            EndX = 0f,
            EndY = 0f,
            EndZ = 0f,
            ServerTick = 0L,
        };
}

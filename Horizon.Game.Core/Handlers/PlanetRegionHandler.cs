using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Horizon.Game.Core.Interfaces;
using Horizon.Game.Message;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using Horizon.Orleans.Interface;
using Microsoft.Extensions.Logging;
using Orleans;

namespace Horizon.Game.Core.Handlers;

/// <summary>
/// 星球区域迁移上行处理器：把请求交给 <see cref="IPlanetRegionGrain"/>，并把权威回执原样下发。
/// <para>回执<em>无论接受还是拒绝都要发</em>：客户端把"被拒"和"丢包"分不开的话，它只能靠猜决定
/// 要不要回滚区域切换，而猜错的代价是地形原点错位（人站在错误的切平面上）。</para>
/// <para>判定一条都不在这里：<see cref="Horizon.Planet.Authority.PlanetRegionAuthority"/> 与 grain
/// 已经各自有判据。这个处理器只做三件事——认包、认连接、把结果发回去。</para>
/// </summary>
public class PlanetRegionHandler : MessageHandlerBase
{
    /// <summary>
    /// 目前只有一颗星球在跑，区域权威 grain 的主键就是它。
    /// <para>刻意写在这儿而不是 <c>PlanetConstants</c>：半径/层级是跨端要对齐的口径，
    /// 而"哪颗星球"是部署事实，客户端不需要知道也不该需要知道。</para>
    /// </summary>
    internal const long DefaultPlanetId = 1;

    private readonly IClusterClient _clusterClient;
    private readonly ISessionBindingValidator? _sessionBinding;

    public PlanetRegionHandler(
        ILogger<MessageHandlerBase> logger,
        IClusterClient clusterClient,
        HorizonMessageAdapter adapter,
        ISessionBindingValidator? sessionBinding = null) : base(logger, clusterClient, adapter)
    {
        _clusterClient = clusterClient;
        _sessionBinding = sessionBinding;
    }

    public override List<MessageType> MessageTypes { get; } = new List<MessageType>
    {
        MessageType.PlanetRegionTransfer,
        MessageType.PlanetRegionSnapshot,
    };

    public override ServiceType ServiceType => ServiceType.Game;

    /// <summary>会话绑定开着时校验角色号；校验器缺失（未注册/灰度关闭）时如实放行，与输入热路径一致。</summary>
    internal bool IsSessionBound(ulong characterId)
    {
        if (_sessionBinding is not { IsEnabled: true }) return true;
        var connectionId = GameClient?.Id;
        if (string.IsNullOrEmpty(connectionId)) return true;         // 拿不到连接号就判不了，不假装通过也不假装失败
        return _sessionBinding.IsCharacterBoundToConnection(connectionId, (long)characterId);
    }

    public override async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> RouteHandlerAsync(HorizonMessagePacket message)
    {
        // 两类上行共用一条链（认包、认连接、把结果发回去），差别只在 DTO 形状：
        // 分成两个处理器会把会话绑定这条安全判据写两遍，而写两遍的那份迟早漏一个分支。
        if (message.Body is PlanetRegionSnapshotRequest snapshotRequest)
            return await RouteSnapshotAsync(message, snapshotRequest);
        if (message.Body is not PlanetRegionTransferRequest request)
        {
            Logger.LogWarning("星球上行包体类型不匹配，已拒绝：{Type}", message.Body?.GetType().Name ?? "null");
            return (false, CreateHorizonMessage(new ErrorMessage { ErrorCode = 400, Message = "无效的星球区域请求" }));
        }

        if (!IsSessionBound(request.CharacterId))
            return (false, CreateHorizonMessage(RejectTransfer(request)));

        try
        {
            var grain = _clusterClient.GetGrain<IPlanetRegionGrain>(DefaultPlanetId);
            var result = await grain.TransferAsync(request);
            // 拒绝也要把结构化回执发回去（客户端据此决定是回滚还是重新落位），只是 IsSuccess 为 false
            if (!result.Accepted)
                Logger.LogInformation(
                    "区域迁移被服务端拒绝：角色号={CharacterId} 码={Code} 理由={Reason} 声明from={From}",
                    result.CharacterId, result.Code, result.Reason, request.FromRegionId);
            return (result.Accepted, CreateHorizonMessage(result));
        }
        catch (Exception ex)
        {
            // 走的是 ErrorMessage 而不是区域回执码：silo 不可达/超时是基础设施故障，
            // 和"这条请求不合法"不是一回事，混在一个码段里客户端就没法分别处理（重试 vs 改请求）
            Logger.LogError(ex, "区域迁移处理失败。角色号={CharacterId} from={From} to={To}",
                request.CharacterId, request.FromRegionId, request.ToRegionId);
            return (false, CreateHorizonMessage(new ErrorMessage
            {
                ErrorCode = 500,
                Message = "区域迁移处理失败",
                RelatedMessageId = message.Header.MessageId,
            }));
        }
    }

    /// <summary>
    /// 只读快照那一支（§6.4 / §13.55）。<b>伪造角色号来读快照不是"读到别人的数据"那么简单</b>：
    /// 订阅集里带的是"谁在附近"，越权读它等于把别人的邻区清单拿走，所以这里同样先过会话绑定。
    /// </summary>
    private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> RouteSnapshotAsync(
        HorizonMessagePacket message, PlanetRegionSnapshotRequest request)
    {
        if (!IsSessionBound(request.CharacterId))
        {
            Logger.LogWarning(
                "区域快照会话绑定失败：CharacterId 与当前连接不一致，越权读邻区清单被拦截。CharacterId={CharacterId}, ConnectionId={ConnectionId}",
                request.CharacterId, GameClient?.Id);
            var refused = new PlanetRegionSnapshotResponse
            {
                CharacterId = request.CharacterId,
                Accepted = false,
                Code = PlanetRegionRejectCode.SessionMismatch,
                Reason = "网关拦下：角色号与这条连接绑定的角色不一致",
            };
            return (false, CreateHorizonMessage(refused));
        }

        try
        {
            var grain = _clusterClient.GetGrain<IPlanetRegionGrain>(DefaultPlanetId);
            var snapshot = await grain.SnapshotAsync(request.CharacterId, request.PlanetCaliber);
            var response = PlanetRegionSnapshotResponse.FromSnapshot(snapshot);
            if (!response.Accepted)
                Logger.LogInformation("区域快照被服务端拒绝：角色号={CharacterId} 码={Code} 理由={Reason}",
                    response.CharacterId, response.Code, response.Reason);
            // 被拒也发结构化回执：空数组与"被拒"必须分得开，否则客户端只能靠超时猜
            return (response.Accepted, CreateHorizonMessage(response));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "区域快照处理失败。角色号={CharacterId}", request.CharacterId);
            return (false, CreateHorizonMessage(new ErrorMessage
            {
                ErrorCode = 500,
                Message = "区域快照处理失败",
                RelatedMessageId = message.Header.MessageId,
            }));
        }
    }

    private PlanetRegionTransferResult RejectTransfer(PlanetRegionTransferRequest request)
    {
        // 拦在触碰任何 grain 之前：伪造角色号的迁移改的是别人的归属
        Logger.LogWarning(
            "区域迁移会话绑定失败：CharacterId 与当前连接不一致，伪造迁移被拦截。CharacterId={CharacterId}, ConnectionId={ConnectionId}",
            request.CharacterId, GameClient?.Id);
        return RejectAtGateway(request, PlanetRegionRejectCode.SessionMismatch,
            "网关拦下：角色号与这条连接绑定的角色不一致");
    }

    /// <summary>
    /// 网关侧（没进 grain）造一条回执。<see cref="PlanetRegionTransferResult.NewRegionId"/> 填 0：
    /// 这里服务端还不知道（或不该再信）这个实体的归属，客户端必须走重新落位而不是照 0 切区。
    /// </summary>
    private static PlanetRegionTransferResult RejectAtGateway(PlanetRegionTransferRequest request,
                                                              PlanetRegionRejectCode code, string reason) =>
        new()
        {
            CharacterId = request.CharacterId,
            Accepted = false,
            Applied = false,
            Code = code,
            Reason = reason,
            NewRegionId = 0,
            HandoverTick = 0,
            ServerTick = 0,
        };
}

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Horizon.Game.Core.Interfaces;
using Horizon.Game.Core.Observability;
using Horizon.Game.Core.World;
using Horizon.Game.Message;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using Horizon.Game.Message.Sync;
using Microsoft.Extensions.Logging;
using Orleans;
using TouchSocket.Sockets;

namespace Horizon.Game.Core.Handlers;

/// <summary>
/// 实时同步包处理器：在现有 HorizonMessagePacket 通道内处理 SyncPacketCodec 编码帧。
/// <para>
/// 阶段 3.2 — 本类已从 ~1200 行上帝类收敛为**纯调度器**：解码帧 → 按
/// <see cref="SyncPacketKind"/> 委托各流处理器（<see cref="SyncHandshakeHandler"/> /
/// <see cref="SyncInputHandler"/> / <see cref="SyncInteractionHandler"/> /
/// <see cref="SyncWorldHandler"/> / <see cref="SyncCombatHandler"/>）→ 编码响应。
/// 各处理器共享同一份 <see cref="SyncSessionState"/>（去重/握手/速率限制状态），
/// 并通过 <see cref="SyncPacketFlowHandlerBase"/> 持有 cluster client / shard 路由 /
/// UE5 DS 数据面等公共依赖。
/// </para>
/// </summary>
public sealed class SyncPacketHandler : MessageHandlerBase
{
    /// <summary>P1.2：Shard 路由器（替换原 DefaultShardId 硬编码），注入各流处理器。</summary>
    private readonly IShardRouter _shardRouter;

    /// <summary>Phase 2：会话绑定校验器（可选，由 Gateway 层注入，灰度开关控制）。</summary>
    private readonly ISessionBindingValidator? _sessionBindingValidator;

    /// <summary>阶段 2.2：UE5 DS 数据面（灰度通道）。null/禁用 → 完全走旧 Orleans 路径，零行为变化。</summary>
    private readonly IDSyncDataPlane? _dsDataPlane;

    /// <summary>阶段 0.6 — 协议版本匹配策略（null = 宽松默认；生产按 RequireStrictVersionMatch 注入严格实现）。</summary>
    private readonly IProtocolVersionPolicy? _protocolVersionPolicy;

    /// <summary>各流处理器共享的按连接会话状态（去重/握手/速率限制/边界订阅跟踪）。</summary>
    private readonly SyncSessionState _sessionState;

    private readonly SyncHandshakeHandler _handshakeHandler;
    private readonly SyncInputHandler _inputHandler;
    private readonly SyncInteractionHandler _interactionHandler;
    private readonly SyncWorldHandler _worldHandler;
    private readonly SyncCombatHandler _combatHandler;
    private readonly SyncTraversalHandler _traversalHandler;

    public SyncPacketHandler(ILogger<MessageHandlerBase> logger, IClusterClient clusterClient, HorizonMessageAdapter adapter, IShardRouter? shardRouter = null, ISessionBindingValidator? sessionBindingValidator = null, IDSyncDataPlane? dsDataPlane = null, IShardRegionResolver? regionResolver = null, SyncSessionState? sessionState = null, IProtocolVersionPolicy? protocolVersionPolicy = null)
        : base(logger, clusterClient, adapter)
    {
        // 默认单 Shard 路由（兼容未注入路由器的场景）
        _shardRouter = shardRouter ?? new ZoneBasedShardRouter(1);
        _sessionBindingValidator = sessionBindingValidator;
        _dsDataPlane = dsDataPlane;
        _protocolVersionPolicy = protocolVersionPolicy;
        // 共享连接态：默认内部创建（兼容既有测试直接 new 的构造）；Gateway 侧注册为 DI 单例注入，
        // 使 PlayerDespawnScheduler 等网关组件能读取边界订阅跟踪做精确清理。
        _sessionState = sessionState ?? new SyncSessionState();

        // 连接 ID 提供者：读取 HandleAsync 设置的 AsyncLocal（按 ExecutionContext 隔离）。
        var connectionIdProvider = new Func<string>(() => _currentConnectionId.Value ?? string.Empty);

        // 阶段 5.2：横切服务（输入去重/交互限流/指标采样）由调度器创建并共享给各流处理器，
        // 使横切逻辑可独立单测；底层状态仍复用同一份 _sessionState（语义零变化）。
        var inputDeduper = new SessionInputDeduper(_sessionState);
        var rateLimiter = new SessionInteractionRateLimiter(_sessionState);
        var metricsSampler = new SyncMetricsSampler();

        _handshakeHandler = new SyncHandshakeHandler(logger, clusterClient, _shardRouter, _dsDataPlane, _sessionBindingValidator, _sessionState, connectionIdProvider, inputDeduper, metricsSampler, regionResolver, _protocolVersionPolicy);
        _inputHandler = new SyncInputHandler(logger, clusterClient, _shardRouter, _dsDataPlane, _sessionBindingValidator, _sessionState, connectionIdProvider, inputDeduper, metricsSampler);
        _interactionHandler = new SyncInteractionHandler(logger, clusterClient, _shardRouter, _dsDataPlane, _sessionBindingValidator, _sessionState, connectionIdProvider, rateLimiter);
        _worldHandler = new SyncWorldHandler(logger, clusterClient, _shardRouter, _dsDataPlane, _sessionBindingValidator, _sessionState, connectionIdProvider, regionResolver);
        _combatHandler = new SyncCombatHandler(logger, clusterClient, _shardRouter, _dsDataPlane, _sessionBindingValidator, _sessionState, connectionIdProvider);
        _traversalHandler = new SyncTraversalHandler(logger, clusterClient, _shardRouter, _dsDataPlane, _sessionBindingValidator, _sessionState, connectionIdProvider);
    }

    public override List<MessageType> MessageTypes => new() { MessageType.SyncPacket };

    public override ServiceType ServiceType => ServiceType.Game;

    /// <summary>
    /// Override：<see cref="MessageHandlerBase.HandleAsync"/> 入口，在调用基类逻辑前
    /// 将当前连接 ID 存入 <see cref="_currentConnectionId"/>（AsyncLocal），
    /// 供后续流处理器（经 <see cref="SyncPacketFlowHandlerBase.CurrentConnectionId"/>）读取，
    /// 实现按连接隔离的输入去重。
    /// </summary>
    public override async Task<(bool IsSuccess, MessageUnion? Response)> HandleAsync(ITcpSessionClient client, HorizonMessagePacket message)
    {
        _currentConnectionId.Value = client.Id;
        return await base.HandleAsync(client, message);
    }

    public override async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> RouteHandlerAsync(HorizonMessagePacket message)
    {
        if (message.Body is not SyncFrameMessage syncFrame || syncFrame.Frame.Length == 0)
        {
            Logger.LogWarning("收到空实时同步帧。MessageId={MessageId}", message.Header.MessageId);
            return (false, CreateSyncResponse(new WorldPatchManifestPacket()));
        }

        var packet = SyncPacketCodec.Decode(syncFrame.Frame);
        // 阶段 0.6：协议版本匹配策略（宽松告警 / 严格拒绝）。
        // 严格模式下非握手帧的版本不匹配同样拒绝——协议不兼容的连接在逐包路径上也不应继续。
        var protocolVersionPolicy = _protocolVersionPolicy ?? ProtocolVersionPolicy.Instance;
        if (!protocolVersionPolicy.IsVersionAccepted(packet.ProtocolVersion))
        {
            // 发布窗口监控：旧客户端拒绝量按 reason 计数。
            // 严格模式（RequireStrictVersionMatch）下协议拒绝在预分派阶段拦截（先于流处理器），
            // 因此此处是协议拒绝的主要计数点；宽松模式仅拒绝 < MinimumSupported 的版本。
            SyncMetrics.HandshakeRejected.Add(1,
                new KeyValuePair<string, object?>("reason", "protocol_version"));
            Logger.LogWarning(
                "实时同步协议版本不匹配，拒绝。ClientVersion={ClientVersion}, ServerVersion={ServerVersion}, Kind={Kind}",
                packet.ProtocolVersion,
                SyncProtocolVersion.Current,
                packet.Kind);
            // success=true：拒绝是正常路由结果（Reject 包仍下发，客户端据此提示更新）——
            // 与 HandleHandshakeAsync 内公式版本拒绝的返回语义一致；success 仅控制响应拦截。
            return (true, CreateSyncResponse(new HandshakeRejectPacket
            {
                ServerVersion = SyncProtocolVersion.Current,
                MinimumVersion = SyncProtocolVersion.MinimumSupported,
                Reason = protocolVersionPolicy.GetRejectionReason(packet.ProtocolVersion),
            }));
        }

        if (packet.ProtocolVersion != SyncProtocolVersion.Current)
        {
            Logger.LogWarning(
                "实时同步协议版本过渡期：允许连接但版本不完全匹配。ClientVersion={ClientVersion}, ServerVersion={ServerVersion}, Kind={Kind}",
                packet.ProtocolVersion,
                SyncProtocolVersion.Current,
                packet.Kind);
        }

        try
        {
            // 阶段 3.2：按 SyncPacketKind 委托各流处理器（每个处理器负责一类包的校验/转发/响应）。
            // SubscriptionUpdatePacket / BaselineResyncRequestPacket 不携带 CharacterId 字段
            // （Task 1 协议定义），故从 HorizonMessagePacket.Header.CharacterId 注入。
            // 该字段由客户端在进入游戏后填充（与登录/进入游戏流程绑定的角色 ID 一致）。
            SyncPacket? response = packet switch
            {
                HandshakePacket handshake => await _handshakeHandler.HandleHandshakeAsync(handshake),
                InputPacket input => await _inputHandler.HandleInputAsync(input),
                ReconnectResumePacket resume => await _handshakeHandler.HandleReconnectAsync(resume),
                InteractionSyncPacket interaction => await _interactionHandler.HandleInteractionUplinkAsync(interaction),
                SceneObjectSyncPacket sceneObject => await _interactionHandler.HandleSceneObjectUplinkAsync(sceneObject),
                SubscriptionUpdatePacket subscription => await _worldHandler.HandleSubscriptionUpdateAsync(subscription, (long)message.Header.CharacterId),
                CombatActionPacket combatAction => await _combatHandler.HandleCombatActionAsync(combatAction),
                TraversalRequestPacket traversal => await _traversalHandler.HandleTraversalRequestAsync(traversal),
                CorpseLootTakeRequestPacket lootTake => await _combatHandler.HandleCorpseLootTakeAsync(lootTake),
                BaselineResyncRequestPacket resync => await _worldHandler.HandleBaselineResyncAsync(resync, (long)message.Header.CharacterId),
                _ => await _worldHandler.HandleUnknownPacketAsync(packet),
            };

            // 订阅更新（SubscriptionUpdatePacket）等无需服务端回复包的情况：跳过响应下发，
            // 直接返回 null MessagePacket，HandleAsync 会据此跳过 SendAsync。
            if (response is null)
            {
                return (true, null!);
            }

            return (true, CreateSyncResponse(response));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex,
                "处理实时同步包失败。Kind={Kind}, MessageId={MessageId}",
                packet.Kind,
                message.Header.MessageId);

            return (false, CreateSyncResponse(new WorldPatchManifestPacket()));
        }
    }

    /// <summary>
    /// 处理客户端上行的交互意图（外部桥接层入口；委托 <see cref="SyncInteractionHandler.HandleInteractionIntent"/>）。
    /// </summary>
    public Task<bool> HandleInteractionIntent(long interactorId, long interactableId, int slotIdx, SyncEventKind intentType)
        => _interactionHandler.HandleInteractionIntent(interactorId, interactableId, slotIdx, intentType);

    /// <summary>
    /// 将同步包编码为 HorizonMessagePacket。
    /// </summary>
    private HorizonMessagePacket CreateSyncResponse(SyncPacket packet)
    {
        SyncPacketCodec.Encode(packet, out var frame, out var frameLength);
        try
        {
            var payload = new byte[frameLength];
            Buffer.BlockCopy(frame, 0, payload, 0, frameLength);

            var message = new SyncFrameMessage
            {
                Frame = payload,
                PacketKind = (byte)packet.Kind,
                ProtocolVersion = packet.ProtocolVersion,
            };

            return CreateHorizonMessage(message);
        }
        finally
        {
            SyncPacketCodec.ReturnFrame(frame);
        }
    }

    /// <summary>
    /// 当前请求的连接 ID（AsyncLocal，在 async 调用链中按 ExecutionContext 隔离传播，
    /// 不受单例多连接并发影响）。由 <see cref="HandleAsync"/> override 设置，
    /// 供各流处理器（经 <see cref="SyncPacketFlowHandlerBase.CurrentConnectionId"/>）读取，
    /// 用于按连接隔离输入去重状态。
    /// </summary>
    /// <remarks>
    /// 为什么不用 <see cref="MessageHandlerBase._gameClient"/>：SyncPacketHandler 是单例，
    /// 多连接并发时 <c>_gameClient</c> 实例字段会被互相覆盖，在 RouteHandlerAsync 的 async
    /// 执行段中读取到的可能是其他连接的 client。AsyncLocal 按 ExecutionContext 隔离，
    /// 每个 async 调用链读到的是各自 HandleAsync 设置的值，可靠且线程安全。
    /// </remarks>
    private static readonly AsyncLocal<string?> _currentConnectionId = new();
}

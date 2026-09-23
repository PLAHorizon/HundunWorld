using System;
using System.Linq;
using System.Threading.Tasks;
using Horizon.Game.Core.Interfaces;
using Horizon.Game.Core.World;
using Horizon.Game.Message.Sync;
using Horizon.Orleans.Interface.World;
using Microsoft.Extensions.Logging;
using Orleans;

namespace Horizon.Game.Core.Handlers;

/// <summary>
/// 阶段 3.2 — <see cref="SyncPacketKind.InteractionSync"/> 与 <see cref="SyncPacketKind.SceneObjectSync"/>
/// 流处理器：上行交互/场景对象意图解析、校验（基本合法性/存在性/会话绑定/速率限制）与权威状态下发。
/// 自 <see cref="SyncPacketHandler"/> 拆分而来。
/// </summary>
public sealed class SyncInteractionHandler : SyncPacketFlowHandlerBase
{
    /// <summary>阶段 5.2 — per-interactorId 速率限制（横切服务）。</summary>
    private readonly IRateLimiter _rateLimiter;

    public SyncInteractionHandler(
        ILogger<MessageHandlerBase> logger,
        IClusterClient clusterClient,
        IShardRouter shardRouter,
        IDSyncDataPlane? dsDataPlane,
        ISessionBindingValidator? sessionBindingValidator,
        SyncSessionState sessionState,
        Func<string> connectionIdProvider,
        IRateLimiter? rateLimiter = null)
        : base(logger, clusterClient, shardRouter, dsDataPlane, sessionBindingValidator, sessionState, connectionIdProvider)
    {
        _rateLimiter = rateLimiter ?? new SessionInteractionRateLimiter(sessionState);
    }

    /// <summary>
    /// 处理客户端上行的交互意图（阶段 5 预留入口）。
    /// <para>
    /// <see cref="InteractionSyncPacket"/> 为 S→C 方向（服务端下发），服务端不在此处理该包类型。
    /// 客户端上行的"交互意图"由阶段 2 的桥接层决定承载方案（独立消息或 InputPacket 附加项），
    /// 确定后由桥接层调用本方法。本方法负责校验合法性并调用
    /// <see cref="IZoneShardGrain.GenerateInteractionSync"/> 下发权威状态。
    /// </para>
    /// </summary>
    /// <param name="interactorId">交互者（玩家）角色 ID。</param>
    /// <param name="interactableId">可交互对象 NetworkId。</param>
    /// <param name="slotIdx">交互槽索引。</param>
    /// <param name="intentType">交互意图类型，应为 <see cref="SyncEventKind.InteractStart"/> / <see cref="SyncEventKind.InteractEnd"/> / <see cref="SyncEventKind.InteractStolen"/>。</param>
    /// <returns>true 表示已成功下发权威状态；false 表示校验失败或下发异常。</returns>
    public async Task<bool> HandleInteractionIntent(long interactorId, long interactableId, int slotIdx, SyncEventKind intentType)
    {
        // --- 校验骨架：基本合法性 ---
        if (interactorId == 0)
        {
            Logger.LogWarning(
                "交互意图校验失败：interactorId 为 0。InteractableId={InteractableId}, SlotIdx={SlotIdx}",
                interactableId, slotIdx);
            return false;
        }

        if (interactableId == 0)
        {
            Logger.LogWarning(
                "交互意图校验失败：interactableId 为 0。InteractorId={InteractorId}, SlotIdx={SlotIdx}",
                interactorId, slotIdx);
            return false;
        }

        if (intentType != SyncEventKind.InteractStart
            && intentType != SyncEventKind.InteractEnd
            && intentType != SyncEventKind.InteractStolen)
        {
            Logger.LogWarning(
                "交互意图校验失败：未知的 intentType={IntentType}。InteractorId={InteractorId}, InteractableId={InteractableId}",
                intentType, interactorId, interactableId);
            return false;
        }

        // --- 校验：slotIdx 范围 ---
        if (slotIdx < 0)
        {
            Logger.LogWarning(
                "交互意图校验失败：slotIdx 为负数。InteractorId={InteractorId}, InteractableId={InteractableId}, SlotIdx={SlotIdx}",
                interactorId, interactableId, slotIdx);
            return false;
        }

        // --- 校验：interactableId 存在性（防止身份伪造） ---
        var zoneShardForInteractable = ClusterClient.GetGrain<IZoneShardGrain>(ShardRouter.Resolve(interactorId));
        if (!await zoneShardForInteractable.HasEntityAsync((ulong)interactableId))
        {
            Logger.LogWarning(
                "交互意图校验失败：interactableId 不存在。InteractorId={InteractorId}, InteractableId={InteractableId}, SlotIdx={SlotIdx}",
                interactorId, interactableId, slotIdx);
            return false;
        }

        // --- 校验：会话绑定（防止身份伪造） ---
        // Phase 2 安全补全：验证 interactorId 是否与当前 TCP 连接绑定的 characterId 一致。
        // 灰度开关控制（GatewayOptions.EnableSessionBindingValidation），默认关闭。
        if (SessionBindingValidator is { IsEnabled: true })
        {
            var connId = CurrentConnectionId;
            if (!string.IsNullOrEmpty(connId) && !SessionBindingValidator.IsCharacterBoundToConnection(connId, interactorId))
            {
                Logger.LogWarning(
                    "会话绑定校验失败：interactorId 与当前连接绑定的 characterId 不一致。InteractorId={InteractorId}, ConnectionId={ConnectionId}, InteractableId={InteractableId}",
                    interactorId, connId, interactableId);
                return false;
            }
        }

        // --- 校验：速率限制（每个 interactorId 每秒最多 10 次请求，提取为 IRateLimiter 横切服务） ---
        if (!_rateLimiter.Allow(interactorId))
        {
            Logger.LogWarning(
                "交互意图校验失败：触发速率限制。InteractorId={InteractorId}, InteractableId={InteractableId}, SlotIdx={SlotIdx}",
                interactorId, interactableId, slotIdx);
            return false;
        }

        // 交互槽状态/AOI 等深度校验由 ZoneShardGrain.GenerateInteractionSync 在服务端完成。
        // 根据意图类型映射 StateBits（占位编码，客户端按阶段 1 协议解释）。
        byte stateBits = intentType switch
        {
            SyncEventKind.InteractStart => InteractionStateBits.Start,    // bit0 = 占用/进行中
            SyncEventKind.InteractEnd => InteractionStateBits.End,        // bit1 = 结束
            SyncEventKind.InteractStolen => InteractionStateBits.Stolen,  // bit2 = 被抢占
            _ => 0x00,
        };

        try
        {
            var zoneShard = ClusterClient.GetGrain<IZoneShardGrain>(ShardRouter.Resolve(interactorId));
            await zoneShard.GenerateInteractionSync(
                slotIdx,
                interactableId,
                interactorId,
                stateBits,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

            Logger.LogInformation(
                "交互意图已下发权威状态。InteractorId={InteractorId}, InteractableId={InteractableId}, SlotIdx={SlotIdx}, IntentType={IntentType}, StateBits={StateBits}",
                interactorId, interactableId, slotIdx, intentType, stateBits);
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex,
                "下发交互同步失败。InteractorId={InteractorId}, InteractableId={InteractableId}, SlotIdx={SlotIdx}",
                interactorId, interactableId, slotIdx);
            return false;
        }
    }

    /// <summary>
    /// 处理客户端上行的 <see cref="InteractionSyncPacket"/>（作为交互意图载体）。
    /// <para>
    /// 客户端在 <see cref="InteractionSyncPacket.StateBits"/> 高位携带意图标志：
    /// <see cref="InteractionStateBits.RequestStartFlag"/>（0x80）= 请求开始交互，
    /// <see cref="InteractionStateBits.RequestStopFlag"/>（0x40）= 请求停止交互。
    /// 本方法解析意图后委托 <see cref="HandleInteractionIntent"/> 执行校验与下发。
    /// </para>
    /// </summary>
    /// <param name="packet">客户端上行的交互同步包。</param>
    /// <returns>复用 <see cref="InputAckPacket"/> 作为简单确认。</returns>
    public async Task<SyncPacket> HandleInteractionUplinkAsync(InteractionSyncPacket packet)
    {
        var interactorId = packet.InteractorId;
        var interactableId = packet.InteractableId;
        var slotIdx = packet.SlotIdx;
        var stateBits = packet.StateBits;

        // 从 StateBits 高位解析上行意图
        SyncEventKind intentType;
        if (InteractionStateBits.IsRequestStart(stateBits))
        {
            intentType = SyncEventKind.InteractStart;
        }
        else if (InteractionStateBits.IsRequestStop(stateBits))
        {
            intentType = SyncEventKind.InteractEnd;
        }
        else
        {
            Logger.LogWarning(
                "上行交互包未携带有效意图标志。InteractorId={InteractorId}, InteractableId={InteractableId}, StateBits={StateBits}",
                interactorId, interactableId, stateBits);
            return new InputAckPacket
            {
                LastProcessedClientTick = 0,
                ServerTick = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                EchoClientTick = packet.ServerTick,
            };
        }

        var accepted = await HandleInteractionIntent(interactorId, interactableId, slotIdx, intentType);

        if (!accepted)
        {
            Logger.LogWarning(
                "上行交互意图被拒绝。InteractorId={InteractorId}, InteractableId={InteractableId}, SlotIdx={SlotIdx}, IntentType={IntentType}",
                interactorId, interactableId, slotIdx, intentType);
        }

        // 复用 InputAckPacket 作为简单确认；EchoClientTick 回显客户端 ServerTick 便于 RTT 估算
        return new InputAckPacket
        {
            LastProcessedClientTick = accepted ? packet.ServerTick : 0,
            ServerTick = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            EchoClientTick = packet.ServerTick,
        };
    }

    /// <summary>
    /// Task C.4.4：处理客户端上行的 <see cref="SceneObjectSyncPacket"/>（作为场景对象交互意图载体）。
    /// <para>
    /// 客户端在 <see cref="SceneObjectSyncPacket.OwnerCharacterId"/> 字段中携带交互者角色 ID，
    /// 在 <see cref="SceneObjectSyncPacket.StateBits"/> 字段中携带交互意图状态位（低 4 位有效）。
    /// 本方法解析后委托 <see cref="IZoneShardGrain.HandleSceneObjectInteract"/> 执行校验与下发。
    /// </para>
    /// </summary>
    /// <param name="packet">客户端上行的场景对象同步包。</param>
    /// <returns>复用 <see cref="InputAckPacket"/> 作为简单确认。</returns>
    public async Task<SyncPacket> HandleSceneObjectUplinkAsync(SceneObjectSyncPacket packet)
    {
        var interactorId = packet.OwnerCharacterId;
        var objectId = packet.ObjectId;
        var intentBits = packet.StateBits;

        if (interactorId == 0)
        {
            Logger.LogWarning(
                "上行场景对象交互包未携带有效 InteractorId（OwnerCharacterId=0）。ObjectId={ObjectId}",
                objectId);
            return new InputAckPacket
            {
                LastProcessedClientTick = 0,
                ServerTick = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                EchoClientTick = packet.ServerTick,
            };
        }

        // Phase 2 安全补全：会话绑定校验（灰度开关控制）
        if (SessionBindingValidator is { IsEnabled: true })
        {
            var connId = CurrentConnectionId;
            if (!string.IsNullOrEmpty(connId) && !SessionBindingValidator.IsCharacterBoundToConnection(connId, (long)interactorId))
            {
                Logger.LogWarning(
                    "场景对象交互会话绑定校验失败：OwnerCharacterId 与当前连接不一致。InteractorId={InteractorId}, ConnectionId={ConnectionId}, ObjectId={ObjectId}",
                    interactorId, connId, objectId);
                return new InputAckPacket
                {
                    LastProcessedClientTick = 0,
                    ServerTick = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    EchoClientTick = packet.ServerTick,
                };
            }
        }

        // 速率限制（复用交互意图的 per-interactorId 限制，提取为 IRateLimiter 横切服务）
        if (!_rateLimiter.Allow((long)interactorId))
        {
            Logger.LogWarning(
                "场景对象交互意图校验失败：触发速率限制。InteractorId={InteractorId}, ObjectId={ObjectId}",
                interactorId, objectId);
            return new InputAckPacket
            {
                LastProcessedClientTick = 0,
                ServerTick = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                EchoClientTick = packet.ServerTick,
            };
        }

        bool accepted = false;
        try
        {
            var zoneShard = ClusterClient.GetGrain<IZoneShardGrain>(ShardRouter.Resolve((long)interactorId));
            accepted = await zoneShard.HandleSceneObjectInteract(interactorId, objectId, intentBits);

            if (!accepted)
            {
                Logger.LogWarning(
                    "上行场景对象交互意图被拒绝。InteractorId={InteractorId}, ObjectId={ObjectId}, IntentBits=0x{IntentBits:X2}",
                    interactorId, objectId, intentBits);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex,
                "下发场景对象交互失败。InteractorId={InteractorId}, ObjectId={ObjectId}",
                interactorId, objectId);
        }

        return new InputAckPacket
        {
            LastProcessedClientTick = accepted ? packet.ServerTick : 0,
            ServerTick = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            EchoClientTick = packet.ServerTick,
        };
    }

}

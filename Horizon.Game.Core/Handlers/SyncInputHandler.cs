using System;
using System.Threading.Tasks;
using Horizon.Game.Core.Interfaces;
using Horizon.Game.Core.Observability;
using Horizon.Game.Core.World;
using Horizon.Game.Message.Sync;
using Horizon.Game.Message.Sync.Server;
using Horizon.Orleans.Interface.World;
using Microsoft.Extensions.Logging;
using Orleans;

namespace Horizon.Game.Core.Handlers;

/// <summary>
/// 阶段 3.2 — <see cref="SyncPacketKind.Input"/> 流处理器：服务端去重（per-connection ClientTick 基线 +
/// 序号去重）→ 阶段 2.2 UE5 DS 灰度路由 → Orleans 合并转发（ReceiveInputAndForwardAsync，
/// 失败回退三段式）→ ACK 构建。自 <see cref="SyncPacketHandler"/> 拆分而来。
/// </summary>
public sealed class SyncInputHandler : SyncPacketFlowHandlerBase
{
    /// <summary>阶段 5.2 — 服务端输入去重（横切服务）。</summary>
    private readonly IInputDeduper _inputDeduper;

    /// <summary>阶段 5.2 — 同步指标采样（横切服务）。</summary>
    private readonly IMetricsSampler _metricsSampler;

    public SyncInputHandler(
        ILogger<MessageHandlerBase> logger,
        IClusterClient clusterClient,
        IShardRouter shardRouter,
        IDSyncDataPlane? dsDataPlane,
        ISessionBindingValidator? sessionBindingValidator,
        SyncSessionState sessionState,
        Func<string> connectionIdProvider,
        IInputDeduper? inputDeduper = null,
        IMetricsSampler? metricsSampler = null)
        : base(logger, clusterClient, shardRouter, dsDataPlane, sessionBindingValidator, sessionState, connectionIdProvider)
    {
        _inputDeduper = inputDeduper ?? new SessionInputDeduper(sessionState);
        _metricsSampler = metricsSampler ?? new SyncMetricsSampler();
    }

    /// <summary>
    /// 处理输入包：接收客户端输入并生成确认包。
    /// </summary>
    public async Task<SyncPacket> HandleInputAsync(InputPacket? input)
    {
        if (input is null)
        {
            Logger.LogWarning("HandleInputAsync: 收到 null InputPacket");
            return new InputAckPacket
            {
                LastProcessedClientTick = 0,
                ServerTick = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                EchoClientTick = 0,
            };
        }

        // P-F3：上行热路径日志降为 Debug（原每输入包一条 LogInformation，30-60Hz × 全体玩家
        // 的同步日志 IO 是上行转发延迟的重要诱因）。
        Logger.LogDebug(
            "HandleInputAsync: 收到 InputPacket。CharacterId={CharacterId}, ClientTick={ClientTick}, MoveX={MoveX:F2}, MoveY={MoveY:F2}, InputBits=0x{InputBits:X8}",
            input.CharacterId, input.ClientTick, input.MoveX, input.MoveY, input.InputBits);

        // 网关为单例（由 Singleton GameNetworkServer 通过 DI 俘获），
        // 不能在实例字段中缓存 characterId——多连接会互相覆盖。
        // 改为从每个 InputPacket 显式携带的 CharacterId 字段读取，确保输入路由到正确的玩家 grain。
        var characterId = (long)input.CharacterId;

        if (characterId == 0)
        {
            Logger.LogWarning("输入处理失败：InputPacket 未携带 CharacterId。ClientTick={ClientTick}", input.ClientTick);
            return new InputAckPacket
            {
                LastProcessedClientTick = input.ClientTick,
                ServerTick = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                EchoClientTick = input.ClientTick,
            };
        }

        // 阶段 5.2：输入热路径会话绑定校验（防止伪造角色输入）。
        // 灰度开关控制（GatewayOptions.EnableSessionBindingValidation），与交互/场景对象路径一致；
        // 输入包是最高频上行，伪造 characterId 可冒充他人移动，必须在触碰任何 grain 前拦截。
        // 当前连接 ID（会话绑定校验与去重复合 key 共用）。
        var connId = CurrentConnectionId;

        // 阶段 5.2：输入热路径会话绑定校验（防止伪造角色输入）。
        // 灰度开关控制（GatewayOptions.EnableSessionBindingValidation），与交互/场景对象路径一致；
        // 输入包是最高频上行，伪造 characterId 可冒充他人移动，必须在触碰任何 grain 前拦截。
        if (SessionBindingValidator is { IsEnabled: true }
            && !string.IsNullOrEmpty(connId)
            && !SessionBindingValidator.IsCharacterBoundToConnection(connId, characterId))
        {
            Logger.LogWarning(
                "输入包会话绑定校验失败：CharacterId 与当前连接不一致，伪造角色输入被拦截。CharacterId={CharacterId}, ConnectionId={ConnectionId}, ClientTick={ClientTick}",
                characterId, connId, input.ClientTick);
            return new InputAckPacket
            {
                LastProcessedClientTick = 0,
                ServerTick = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                EchoClientTick = input.ClientTick,
            };
        }

        // ⚠️ 顺序至关重要：DS 灰度路由必须在**任何 Orleans 解析之前**尝试。
        //    原实现把 `GetGrain<IPlayerSessionGrain>` 放在此处（先于路由判定），
        //    后果有二：
        //      1) Orleans 不可用时 GetGrain 直接抛 ArgumentException（找不到 grain 实现），
        //         函数在抵达路由判定前就异常退出 —— DS 通道**永远走不通**（阶段 2 E2E 实测捕获）；
        //      2) Orleans 可用时，下方去重分支的 BuildInputAckAsync 会先打一次 grain RPC，
        //         而路由成功后 DS 也会模拟同一输入 —— 同一输入被**两个权威**各应用一次。
        //    因此这里只做无 grain 的本地去重，随后立即尝试 DS 路由；只有路由落空
        //    （无可用 DS / 转发失败）才解析 sessionGrain 走 Orleans 旧路径。

        // Task D.2.3：服务端去重（基于 ClientTick 序号），提取为 IInputDeduper 横切服务。
        // 客户端冗余重传会发送重复/过期 input，服务端据 (characterId, connectionId) 维护已接受的
        // 最大 ClientTick：重复/更早的包直接返回最新 ack，不转发到 ZoneShard（避免重复模拟）；
        // 低于握手基线的包视为旧会话残留直接丢弃（多连接 tick 污染防护）。
        // 该判定完全基于本地 SyncSessionState，不触碰 Orleans，故可安全地前置于路由之前。
        var dedupDecision = _inputDeduper.CheckDedup(
            input.CharacterId, connId, input.ClientTick, out var lastAcceptedTick);

        if (dedupDecision == InputDedupDecision.StaleSession)
        {
            Logger.LogDebug(
                "输入包被拒绝（旧会话残留，低于握手基线）。CharacterId={CharacterId}, ClientTick={ClientTick}",
                characterId, input.ClientTick);
            return new InputAckPacket
            {
                LastProcessedClientTick = 0,
                ServerTick = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                EchoClientTick = input.ClientTick,
            };
        }

        if (dedupDecision == InputDedupDecision.Duplicate)
        {
            Logger.LogDebug(
                "输入包被去重拒绝（重复或过期）。CharacterId={CharacterId}, ClientTick={ClientTick}, LastAccepted={LastAccepted}",
                characterId, input.ClientTick, lastAcceptedTick);

            // 重复包也需要 ack 让客户端推进已发送窗口。先试 DS 路由（吞掉重复输入、由 DS 回权威 ack），
            // 落空才解析 sessionGrain 走 Orleans —— 保持"无 DS 时的行为与改造前逐字节一致"。
            if (await TryRouteToDsAsync(input, characterId))
            {
                return BuildPassThroughAck(input);
            }

            return await ClusterClient.GetGrain<IPlayerSessionGrain>(characterId)
                .BuildInputAckAsync(echoClientTick: input.ClientTick);
        }

        // 阶段 2.2：UE5 DS 灰度路由（去重/基线校验通过后、**触碰任何 Orleans grain 之前**）。
        // UseDS 开启且该 shard 有已注册 DS 时，输入帧直接转发 DS（SimWorld 权威回放），
        // 网关返回透传 ack（LastProcessedClientTick=输入 tick：gateway→DS 为可靠 TCP，
        // 帧到达即入 DS 输入队列，客户端据此清理已发送历史，避免重传风暴；DS 权威 ack 由
        // 阶段 2.4 出站数据面回推）；无可用 DS / 转发失败 → 回退下方 Orleans 路径。
        if (await TryRouteToDsAsync(input, characterId))
        {
            return BuildPassThroughAck(input);
        }

        // 走到这里说明 DS 路由不可用（开关关闭 / 无可用 DS / 转发失败）——此时才允许触碰 Orleans。
        // 这是改造后的**唯一** grain 解析点，确保"DS 权威"期间不存在第二条模拟路径。
        var sessionGrain = ClusterClient.GetGrain<IPlayerSessionGrain>(characterId);

        var acceptResult = InputAcceptResult.Accepted;
        InputAckPacket ackPacket;

        // P-F3 上行转发合并：原路径为三次串行跨进程 RPC（ReceiveInputAsync → SubmitInputAsync →
        // BuildInputAckAsync），每次都要一次 gateway↔silo RTT，输入→ACK 延迟叠加。
        // 现改为单次 ReceiveInputAndForwardAsync：silo 内一个 grain turn 完成接收/转发/ACK 构建，
        // 跨进程 RTT 从 3 次降为 1 次。
        try
        {
            var forwardResult = await sessionGrain.ReceiveInputAndForwardAsync(
                input,
                ShardRouter.Resolve(characterId),
                input.PredictedEndX,
                input.PredictedEndY,
                input.PredictedEndZ);
            acceptResult = forwardResult.Result;
            ackPacket = forwardResult.Ack;
        }
        catch (Exception ex)
        {
            // 合并调用失败时回退到原三段式路径，保证输入链路可用性。
            Logger.LogDebug(ex,
                "ReceiveInputAndForwardAsync 失败，回退三段式路径。CharacterId={CharacterId}, ClientTick={ClientTick}",
                characterId, input.ClientTick);

            acceptResult = await sessionGrain.ReceiveInputAsync(input);
            if (acceptResult != InputAcceptResult.Invalid && acceptResult != InputAcceptResult.TooOld)
            {
                try
                {
                    var zoneShard = ClusterClient.GetGrain<IZoneShardGrain>(ShardRouter.Resolve(characterId));
                    await zoneShard.SubmitInputAsync((ulong)characterId, input, input.PredictedEndX, input.PredictedEndY, input.PredictedEndZ);
                }
                catch (Exception ex2)
                {
                    Logger.LogDebug(ex2,
                        "转发输入到 ZoneShard 失败。CharacterId={CharacterId}, ClientTick={ClientTick}",
                        characterId, input.ClientTick);
                }
            }
            ackPacket = await sessionGrain.BuildInputAckAsync(echoClientTick: input.ClientTick);
        }

        // Phase 4: 补充 InputLagMs 指标（客户端 input 到服务器 consume 的滞后，提取为 IMetricsSampler）
        _metricsSampler.RecordInputLag(input.ClientTick);

        if (acceptResult == InputAcceptResult.Invalid)
        {
            Logger.LogWarning("输入包被拒绝（无效）。CharacterId={CharacterId}, ClientTick={ClientTick}", characterId, input.ClientTick);
        }
        else if (acceptResult == InputAcceptResult.TooOld)
        {
            Logger.LogDebug("输入包被拒绝（过期）。CharacterId={CharacterId}, ClientTick={ClientTick}", characterId, input.ClientTick);
        }

        Logger.LogDebug(
            "输入处理完成。CharacterId={CharacterId}, ClientTick={ClientTick}, ServerTick={ServerTick}, LastProcessed={LastProcessed}, Result={Result}",
            characterId,
            input.ClientTick,
            ackPacket.ServerTick,
            ackPacket.LastProcessedClientTick,
            acceptResult);

        return ackPacket;
    }

    /// <summary>
    /// 构造 DS 路由成功时的透传 ack（不触碰 Orleans）。
    /// <para>
    /// <c>LastProcessedClientTick = 输入 tick</c>：gateway→DS 为可靠 TCP，帧送达即入 DS 输入队列，
    /// 客户端据此清理已发送历史，避免重传风暴。DS 的权威 ack 由出站数据面另行回推。
    /// </para>
    /// </summary>
    private static InputAckPacket BuildPassThroughAck(InputPacket input) => new()
    {
        LastProcessedClientTick = input.ClientTick,
        ServerTick = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        EchoClientTick = input.ClientTick,
    };
}

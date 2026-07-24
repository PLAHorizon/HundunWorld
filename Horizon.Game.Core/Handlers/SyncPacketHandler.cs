using Horizon.Game.Core.World;
using Horizon.Game.Message;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using Horizon.Game.Message.Sync;
using Horizon.Game.Message.Sync.Server;
using Horizon.Orleans.Interface.Combat;
using Horizon.Orleans.Interface.World;
using Microsoft.Extensions.Logging;
using Orleans;
using TouchSocket.Sockets;

namespace Horizon.Game.Core.Handlers;

/// <summary>
/// 实时同步包处理器：在现有 HorizonMessagePacket 通道内处理 SyncPacketCodec 编码帧。
/// </summary>
public sealed class SyncPacketHandler : MessageHandlerBase
{
    /// <summary>当前服务器基线版本（与游戏二进制版本一一对应）。</summary>
    private const int ServerBaselineVersion = 1;

    /// <summary>当前服务器世界补丁版本。</summary>
    private const int ServerWorldPatchVersion = 1;

    /// <summary>P1.2：Shard 路由器（替换原 DefaultShardId 硬编码）。</summary>
    private readonly IShardRouter _shardRouter;

    public SyncPacketHandler(ILogger<MessageHandlerBase> logger, IClusterClient clusterClient, HorizonMessageAdapter adapter, IShardRouter? shardRouter = null)
        : base(logger, clusterClient, adapter)
    {
        // 默认单 Shard 路由（兼容未注入路由器的场景）
        _shardRouter = shardRouter ?? new ZoneBasedShardRouter(1);
    }

    public override List<MessageType> MessageTypes => new() { MessageType.SyncPacket };

    public override ServiceType ServiceType => ServiceType.Game;

    /// <summary>
    /// Override：<see cref="MessageHandlerBase.HandleAsync"/> 入口，在调用基类逻辑前
    /// 将当前连接 ID 存入 <see cref="_currentConnectionId"/>（AsyncLocal），
    /// 供后续 <see cref="HandleInputAsync"/> / <see cref="HandleHandshakeAsync"/> 读取，
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
        if (packet.ProtocolVersion != SyncProtocolVersion.Current)
        {
            Logger.LogWarning(
                "实时同步协议版本不匹配。ClientVersion={ClientVersion}, ServerVersion={ServerVersion}, Kind={Kind}",
                packet.ProtocolVersion,
                SyncProtocolVersion.Current,
                packet.Kind);
        }

        try
        {
            // SubscriptionUpdatePacket 不携带 CharacterId 字段（Task 1 协议定义），
            // SyncPacket 基类也无该字段，故从 HorizonMessagePacket.Header.CharacterId 注入。
            // 该字段由客户端在进入游戏后填充（与登录/进入游戏流程绑定的角色 ID 一致）。
            SyncPacket? response = packet switch
            {
                HandshakePacket handshake => await HandleHandshakeAsync(handshake),
                InputPacket input => await HandleInputAsync(input),
                ReconnectResumePacket resume => await HandleReconnectAsync(resume),
                InteractionSyncPacket interaction => await HandleInteractionUplinkAsync(interaction),
                SceneObjectSyncPacket sceneObject => await HandleSceneObjectUplinkAsync(sceneObject),
                SubscriptionUpdatePacket subscription => await HandleSubscriptionUpdateAsync(subscription, (long)message.Header.CharacterId),
                CombatActionPacket combatAction => await HandleCombatActionAsync(combatAction),
                _ => await HandleUnknownPacketAsync(packet),
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

    /// <summary>P1.2：单 Shard 模式下的固定 shardId（已由 _shardRouter 替代，保留作为注释参考）。</summary>
    // private const long DefaultShardId = 0;  // 已由 IShardRouter 替代

    /// <summary>首次进入 World 时订阅出生 chunk 周围的 AOI 半径（与客户端 OnPlayerChunkChanged 的 ViewRadiusChunks 保持一致）。</summary>
    private const int InitialAoiRadiusChunks = 2;

    /// <summary>交互意图速率限制：每个 interactorId 每秒最多请求数。</summary>
    private const int InteractionRateLimitPerSecond = 10;

    /// <summary>交互意图速率限制：两次请求之间的最小间隔（毫秒）= 1000 / <see cref="InteractionRateLimitPerSecond"/>。</summary>
    private const double InteractionRateLimitIntervalMs = 1000.0 / InteractionRateLimitPerSecond;

    /// <summary>
    /// 每个 interactorId 最近一次交互意图请求时间，用于简单速率限制。
    /// SyncPacketHandler 为单例，需保证线程安全（使用 <see cref="_rateLimitLock"/> 保护）。
    /// </summary>
    private readonly Dictionary<long, DateTime> _interactionLastRequestTime = new();

    /// <summary>保护 <see cref="_interactionLastRequestTime"/> 的锁。</summary>
    private readonly object _rateLimitLock = new();

    /// <summary>上次执行速率限制条目清理的时间。</summary>
    private DateTime _lastRateLimitCleanup = DateTime.MinValue;

    /// <summary>
    /// 当前请求的连接 ID（AsyncLocal，在 async 调用链中按 ExecutionContext 隔离传播，
    /// 不受单例多连接并发影响）。由 <see cref="HandleAsync"/> override 设置，
    /// 供 <see cref="HandleInputAsync"/> 等方法读取，用于按连接隔离输入去重状态。
    /// </summary>
    /// <remarks>
    /// 为什么不用 <see cref="MessageHandlerBase._gameClient"/>：SyncPacketHandler 是单例，
    /// 多连接并发时 <c>_gameClient</c> 实例字段会被互相覆盖，在 RouteHandlerAsync 的 async
    /// 执行段中读取到的可能是其他连接的 client。AsyncLocal 按 ExecutionContext 隔离，
    /// 每个 async 调用链读到的是各自 HandleAsync 设置的值，可靠且线程安全。
    /// </remarks>
    private static readonly AsyncLocal<string?> _currentConnectionId = new();

    /// <summary>
    /// 每个连接最近一次接受的 ClientTick，用于服务端去重。
    /// key=(characterId, connectionId)，按连接隔离，防止旧连接高 tick 包污染新连接去重字典。
    /// SyncPacketHandler 为单例（多连接并发），需通过 <see cref="_inputDedupLock"/> 保护。
    /// </summary>
    /// <remarks>
    /// 修复（多连接 tick 污染）：原实现用 characterId 作 key，自动重连时旧连接的高 tick 包
    /// 在握手清理字典后重新写入，污染新连接的去重状态，导致新连接所有输入被持续拒绝
    /// （ClientTick &lt;= LastAccepted），角色卡住/离线。改用 (characterId, connectionId)
    /// 复合 key 后，不同连接的去重状态完全隔离，旧连接的包不影响新连接。
    /// </remarks>
    private readonly Dictionary<(ulong CharacterId, string ConnectionId), long> _lastInputTickPerConnection = new();

    /// <summary>
    /// 握手基线 tick。key=(characterId, connectionId)，value=握手时客户端声明的 InitialClientTick。
    /// 握手后，任何 ClientTick &lt; 基线的输入包视为来自旧会话/过期连接的残留重传，直接丢弃。
    /// </summary>
    private readonly Dictionary<(ulong CharacterId, string ConnectionId), long> _handshakeBaselinePerConnection = new();

    /// <summary>保护 <see cref="_lastInputTickPerConnection"/> 和 <see cref="_handshakeBaselinePerConnection"/> 的锁。</summary>
    private readonly object _inputDedupLock = new();

    /// <summary>
    /// 已完成握手的 (characterId, connectionId) 集合，用于重复握手幂等保护。<br/>
    /// 修复 BUG：客户端可能错误发送两次 Sync 握手（日志显示同一连接在 ~400ms 内发送两次握手），
    /// 导致 <see cref="HandleHandshakeAsync"/> 被调用两次，进而：<br/>
    /// 1. <c>EnterWorldAsync</c> 被调用两次，ZoneShard 实体重复创建<br/>
    /// 2. <c>ConnectionManager.RegisterCharacter</c> 被调用两次，重复日志/映射<br/>
    /// 3. AOI 订阅被重复建立<br/>
    /// 握手幂等保护：同一 (characterId, connectionId) 第二次握手直接返回成功响应，
    /// 不重复执行 EnterWorldAsync 等副作用。<br/>
    /// 连接断开时通过 <see cref="CleanupHandshakeRecord"/> 清理记录。
    /// </summary>
    private readonly HashSet<(ulong CharacterId, string ConnectionId)> _handshookConnections = new();

    /// <summary>保护 <see cref="_handshookConnections"/> 的锁。</summary>
    private readonly object _handshakeIdempotentLock = new();

    /// <summary>
    /// 处理握手包：初始化玩家会话并返回握手确认。
    /// 返回 <see cref="HandshakePacket"/>（回显 LocalCharacterId / InitialClientTick），
    /// 使客户端 <c>SyncPacketMessageHandler.HandshakeReceived</c> 事件能正确触发。
    /// </summary>
    private async Task<SyncPacket> HandleHandshakeAsync(HandshakePacket handshake)
    {
        // P1.3：协议版本强制校验。
        // 版本 < MinimumSupported → 返回 HandshakeRejectPacket（触发客户端强制更新）。
        // 版本 >= MinimumSupported 但 != Current → 允许连接（过渡期）并记录警告。
        if (handshake.ProtocolVersion < SyncProtocolVersion.MinimumSupported)
        {
            Logger.LogWarning(
                "Sync握手拒绝：协议版本过低，强制更新。ClientVersion={ClientVersion}, MinimumSupported={MinimumSupported}, ServerVersion={ServerVersion}",
                handshake.ProtocolVersion, SyncProtocolVersion.MinimumSupported, SyncProtocolVersion.Current);
            return new HandshakeRejectPacket
            {
                ServerVersion = SyncProtocolVersion.Current,
                MinimumVersion = SyncProtocolVersion.MinimumSupported,
                Reason = $"协议版本过低（客户端 v{handshake.ProtocolVersion}，服务器最低支持 v{SyncProtocolVersion.MinimumSupported}），请更新客户端。",
            };
        }

        if (handshake.ProtocolVersion != SyncProtocolVersion.Current)
        {
            Logger.LogWarning(
                "Sync握手版本过渡期：允许连接但版本不完全匹配。ClientVersion={ClientVersion}, ServerVersion={ServerVersion}",
                handshake.ProtocolVersion, SyncProtocolVersion.Current);
        }

        var characterId = (long)handshake.LocalCharacterId;

        // 重复握手幂等保护：同一 (characterId, connectionId) 已握手过时，直接返回成功响应，
        // 不重复执行 EnterWorldAsync / RegisterCharacter / AOI 订阅等副作用。
        // 修复 BUG：日志显示客户端在 ~400ms 内发送两次 Sync 握手，导致角色映射重复注册、
        // EnterWorldAsync 被调用两次、ZoneShard 实体重复创建。
        var idempotentConnId = _currentConnectionId.Value ?? string.Empty;
        var idempotentKey = (handshake.LocalCharacterId, idempotentConnId);
        lock (_handshakeIdempotentLock)
        {
            if (!_handshookConnections.Add(idempotentKey))
            {
                Logger.LogWarning(
                    "重复 Sync握手 被幂等拒绝，直接返回成功响应。CharacterId={CharacterId}, ConnectionId={ConnectionId}",
                    characterId, idempotentConnId);
                // 回显与首次握手一致的响应，触发客户端 HandshakeReceived 事件
                return new HandshakePacket
                {
                    LocalCharacterId = handshake.LocalCharacterId,
                    InitialClientTick = handshake.InitialClientTick,
                };
            }
        }

        Logger.LogInformation(
            "Sync握手开始。CharacterId={CharacterId}, ClientTick={ClientTick}",
            characterId,
            handshake.InitialClientTick);

        var sessionGrain = _clusterClient.GetGrain<IPlayerSessionGrain>(characterId);

        var handshakeSuccess = await sessionGrain.HandshakeAsync(
            handshake,
            ServerBaselineVersion,
            ServerWorldPatchVersion,
            lastAppliedDiffSeq: 0);

        if (!handshakeSuccess)
        {
            Logger.LogWarning("Sync握手失败，参数被拒绝。CharacterId={CharacterId}", characterId);
            return new HandshakePacket
            {
                LocalCharacterId = handshake.LocalCharacterId,
                InitialClientTick = handshake.InitialClientTick,
            };
        }

        // 握手表示新会话开始：清理去重字典中该角色的旧 tick 记录，
        // 避免客户端重新登录/断线重连后 ClientTick 从 0 重置时，所有新输入被误判为重复包而拒绝。
        // （PlayerSessionGrain.HandshakeAsync 内部会同步重置 PlayerSessionState.LastAcceptedClientTick）
        // 修复：客户端可能错误发送 Unix 时间戳作为 InitialClientTick，
        // 若值过大（> 1000000 判定为时间戳），重置为 0，避免后续所有顺序 tick 被误判为"旧会话残留"而拒绝。
        // 修复（多连接 tick 污染）：去重字典改用 (characterId, connectionId) 复合 key，
        // 不同连接的去重状态完全隔离，旧连接的高 tick 包无法污染新连接。
        // 此处设置当前连接的基线，并清理同一角色旧连接的残留记录（防内存泄漏）。
        var connId = _currentConnectionId.Value ?? string.Empty;
        var connKey = (handshake.LocalCharacterId, connId);
        lock (_inputDedupLock)
        {
            long baseline = handshake.InitialClientTick;
            if (baseline > 1_000_000) baseline = 0; // 时间戳保护
            _handshakeBaselinePerConnection[connKey] = baseline;

            // 清理同一 characterId 的旧连接记录（防内存泄漏）。
            // 复合 key 已隔离不同连接，此清理仅为释放断开连接的残留记录。
            var staleKeys = new List<(ulong, string)>();
            foreach (var k in _lastInputTickPerConnection.Keys)
            {
                if (k.CharacterId == handshake.LocalCharacterId && k.ConnectionId != connId)
                    staleKeys.Add(k);
            }
            foreach (var sk in staleKeys)
            {
                _lastInputTickPerConnection.Remove(sk);
                _handshakeBaselinePerConnection.Remove(sk);
            }
        }

        // 同步清理 _handshookConnections 中同一 characterId 的旧连接握手记录。
        // 避免锁嵌套（_inputDedupLock → _handshakeIdempotentLock），单独加锁。
        // 断线重连时旧连接的握手记录应被清除，允许新连接正常握手。
        lock (_handshakeIdempotentLock)
        {
            var staleHandshakeKeys = new List<(ulong, string)>();
            foreach (var k in _handshookConnections)
            {
                if (k.CharacterId == handshake.LocalCharacterId && k.ConnectionId != connId)
                    staleHandshakeKeys.Add(k);
            }
            foreach (var sk in staleHandshakeKeys)
            {
                _handshookConnections.Remove(sk);
            }
        }

        // 握手成功后，注册实体到 ZoneShard 权威模拟层并订阅 AOI
        try
        {
            var zoneShard = _clusterClient.GetGrain<IZoneShardGrain>(_shardRouter.Resolve(characterId));
            // Flax Y-up → ECS Z-up 转换：交换 Y/Z，确保 chunk 归属与服务端 entity 坐标一致
            var initialInterestChunks = WorldCoord
                .GetChunksInView(handshake.InitialX, handshake.InitialZ, handshake.InitialY, InitialAoiRadiusChunks)
                .ToArray();
            await zoneShard.EnterWorldAsync(
                characterId,
                (ulong)characterId,
                handshake.InitialX,
                handshake.InitialY,
                handshake.InitialZ,
                initialInterestChunks);
            Logger.LogInformation(
                "Sync握手后实体注册完成。CharacterId={CharacterId}, ShardId={ShardId}, InitialInterestChunkCount={InitialInterestChunkCount}",
                characterId, _shardRouter.Resolve(characterId), initialInterestChunks.Length);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex,
                "Sync握手后实体注册失败。CharacterId={CharacterId}，同步可能不完整。",
                characterId);
        }

        Logger.LogInformation("Sync握手成功。CharacterId={CharacterId}", characterId);

        // 回显客户端的 LocalCharacterId 与 InitialClientTick，作为服务端握手确认。
        // 客户端 SyncPacketMessageHandler 仅对 HandshakePacket 触发 HandshakeReceived 事件，
        // 因此必须返回 HandshakePacket（而非 WorldPatchManifestPacket）。
        return new HandshakePacket
        {
            LocalCharacterId = handshake.LocalCharacterId,
            InitialClientTick = handshake.InitialClientTick,
        };
    }

    /// <summary>
    /// 处理输入包：接收客户端输入并生成确认包。
    /// </summary>
    private async Task<SyncPacket> HandleInputAsync(InputPacket? input)
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

        Logger.LogInformation(
            "HandleInputAsync: 收到 InputPacket。CharacterId={CharacterId}, ClientTick={ClientTick}, MoveX={MoveX:F2}, MoveY={MoveY:F2}, InputBits=0x{InputBits:X8}",
            input.CharacterId, input.ClientTick, input.MoveX, input.MoveY, input.InputBits);

        // SyncPacketHandler 为单例（由 Singleton GameNetworkServer 通过 DI 俘获），
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

        var sessionGrain = _clusterClient.GetGrain<IPlayerSessionGrain>(characterId);

        // Task D.2.3：服务端去重（基于 ClientTick 序号）。
        // 客户端冗余重传会发送重复/过期 input，服务端据 characterId 维护已接受的最大 ClientTick，
        // 重复或更早的 ClientTick 直接返回最新 ack，不转发到 ZoneShard（避免重复模拟）。
        // SyncPacketHandler 为单例多连接并发，需加锁保护字典；锁内仅做字典操作，锁外再 await grain 调用。
        //
        // 修复（多连接 tick 冲突）：增加握手基线检查。
        // 场景：玩家断线重连后，旧连接仍在发送高 tick 重传包（如 tick=10570），
        // 新连接从低 tick 开始（如 tick=6480）。旧实现中旧连接的高 tick 会污染去重字典，
        // 导致新连接所有输入被永久拒绝（ClientTick <= LastAccepted）。
        // 修复：握手时记录 InitialClientTick 作为基线，低于基线的包视为旧会话残留，直接丢弃。
        // 修复（多连接 tick 污染）：去重字典使用 (characterId, connectionId) 复合 key，
        // 不同连接的去重状态完全隔离。旧连接的高 tick 包只会写入旧连接的 key，
        // 不会污染新连接，从根本上解决"自动重连后输入被持续拒绝"的问题。
        var connId = _currentConnectionId.Value ?? string.Empty;
        var connKey = (input.CharacterId, connId);
        bool isDuplicate = false;
        bool isStaleSession = false;
        long lastAcceptedTick = 0;
        lock (_inputDedupLock)
        {
            // 握手基线检查：低于基线的包来自旧会话/过期连接，直接丢弃
            if (_handshakeBaselinePerConnection.TryGetValue(connKey, out var baselineTick)
                && input.ClientTick < baselineTick)
            {
                isStaleSession = true;
            }
            else if (_lastInputTickPerConnection.TryGetValue(connKey, out lastAcceptedTick)
                && input.ClientTick <= lastAcceptedTick)
            {
                isDuplicate = true;
            }
            else
            {
                _lastInputTickPerConnection[connKey] = input.ClientTick;
            }
        }

        if (isStaleSession)
        {
            Logger.LogDebug(
                "输入包被拒绝（旧会话残留，低于握手基线）。CharacterId={CharacterId}, ClientTick={ClientTick}, BaselineTick={BaselineTick}",
                characterId, input.ClientTick, _handshakeBaselinePerConnection.GetValueOrDefault(connKey));
            return new InputAckPacket
            {
                LastProcessedClientTick = 0,
                ServerTick = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                EchoClientTick = input.ClientTick,
            };
        }

        if (isDuplicate)
        {
            Logger.LogDebug(
                "输入包被去重拒绝（重复或过期）。CharacterId={CharacterId}, ClientTick={ClientTick}, LastAccepted={LastAccepted}",
                characterId, input.ClientTick, lastAcceptedTick);
            return await sessionGrain.BuildInputAckAsync(echoClientTick: input.ClientTick);
        }

        var acceptResult = await sessionGrain.ReceiveInputAsync(input);

        if (acceptResult == InputAcceptResult.Invalid)
        {
            Logger.LogWarning("输入包被拒绝（无效）。CharacterId={CharacterId}, ClientTick={ClientTick}", characterId, input.ClientTick);
        }
        else if (acceptResult == InputAcceptResult.TooOld)
        {
            Logger.LogDebug("输入包被拒绝（过期）。CharacterId={CharacterId}, ClientTick={ClientTick}", characterId, input.ClientTick);
        }
        else
        {
            // 将输入转发到 ZoneShard 权威模拟层，使 TickAsync 能处理该输入并产生快照
            try
            {
                var zoneShard = _clusterClient.GetGrain<IZoneShardGrain>(_shardRouter.Resolve(characterId));
                await zoneShard.SubmitInputAsync((ulong)characterId, input, input.PredictedEndX, input.PredictedEndY, input.PredictedEndZ);
            }
            catch (Exception ex)
            {
                Logger.LogDebug(ex,
                    "转发输入到 ZoneShard 失败。CharacterId={CharacterId}, ClientTick={ClientTick}",
                    characterId, input.ClientTick);
            }
        }

        var ackPacket = await sessionGrain.BuildInputAckAsync(echoClientTick: input.ClientTick);

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

        // TODO（Task 8.6c / 阶段 2 对接后补全）：interactableId 存在性校验。
        // 当前 IZoneShardGrain 未暴露 EntityExistsAsync / HasEntityAsync 等查询接口，
        // 无法在不下发权威状态的前提下校验 interactableId 是否已注册。
        // 待 IZoneShardGrain 扩展实体存在性查询后，在此追加：
        //   var zoneShard = _clusterClient.GetGrain<IZoneShardGrain>(DefaultShardId);
        //   if (!await zoneShard.EntityExistsAsync((ulong)interactableId)) { ... return false; }
        // 当前存在身份伪造风险：客户端可对不存在的 interactableId 触发交互同步。

        // --- 校验：会话绑定（防止身份伪造） ---
        // TODO（Task 8.6a / 阶段 2 对接后补全）：IPlayerSessionGrain 当前未暴露
        // ValidateSessionAsync(long characterId) 或 GetBoundCharacterIdAsync() 等接口，
        // 无法校验 interactorId 是否与当前连接绑定的 characterId 一致。
        // 存在身份伪造风险：恶意客户端可在 InteractionSyncPacket.InteractorId 中填入他人 ID，
        // 触发以受害者身份下发的权威交互状态。
        // 待 IPlayerSessionGrain 扩展会话校验接口后，在此追加：
        //   var sessionGrain = _clusterClient.GetGrain<IPlayerSessionGrain>(interactorId);
        //   if (!await sessionGrain.ValidateSessionAsync(interactorId)) { ... return false; }
        Logger.LogWarning(
            "安全缺口：会话绑定校验未实装，interactorId 未与当前连接的 characterId 比对。InteractorId={InteractorId}, InteractableId={InteractableId}",
            interactorId, interactableId);

        // --- 校验：速率限制（每个 interactorId 每秒最多 10 次请求） ---
        if (!CheckInteractionRateLimit(interactorId))
        {
            Logger.LogWarning(
                "交互意图校验失败：触发速率限制。InteractorId={InteractorId}, InteractableId={InteractableId}, SlotIdx={SlotIdx}",
                interactorId, interactableId, slotIdx);
            return false;
        }

        // TODO（阶段 2 对接后补全）：检查交互槽是否空闲、玩家是否在 AOI 范围内等深度合法性。

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
            var zoneShard = _clusterClient.GetGrain<IZoneShardGrain>(_shardRouter.Resolve(interactorId));
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
    private async Task<SyncPacket> HandleInteractionUplinkAsync(InteractionSyncPacket packet)
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
    private async Task<SyncPacket> HandleSceneObjectUplinkAsync(SceneObjectSyncPacket packet)
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

        // 速率限制（复用交互意图的 per-interactorId 限制）
        if (!CheckInteractionRateLimit((long)interactorId))
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
            var zoneShard = _clusterClient.GetGrain<IZoneShardGrain>(_shardRouter.Resolve((long)interactorId));
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

    /// <summary>
    /// 处理客户端上行的 <see cref="SubscriptionUpdatePacket"/>（多玩家 AOI 动态 chunk 订阅变更）。
    /// <para>
    /// 客户端在玩家跨越 chunk 边界导致 AOI 窗口滚动时上行本包，告知服务端
    /// 本次新增订阅（<see cref="SubscriptionUpdatePacket.AddedChunks"/>）与
    /// 移除订阅（<see cref="SubscriptionUpdatePacket.RemovedChunks"/>）的 chunk key 集合。
    /// 服务端据此调用 <see cref="IZoneShardGrain.SubscribeSessionAsync"/> /
    /// <see cref="IZoneShardGrain.UnsubscribeSessionAsync"/> 更新权威订阅表，
    /// 后续 <see cref="IZoneShardGrain.BroadcastChunkDiffsAsync"/> 才能正确扇出到该玩家的兴趣集。
    /// </para>
    /// <para>
    /// <see cref="SubscriptionUpdatePacket"/> 不携带 CharacterId 字段（Task 1 协议定义），
    /// <see cref="SyncPacket"/> 基类亦无该字段，故 characterId 由路由层从
    /// <see cref="MessageHeader.CharacterId"/> 注入（客户端在进入游戏后填充）。
    /// </para>
    /// </summary>
    /// <param name="update">客户端上行的订阅变更包；可为 null（解码失败时）。</param>
    /// <param name="characterId">由 <see cref="MessageHeader.CharacterId"/> 注入的角色 ID。</param>
    /// <returns>始终返回 null：订阅更新无需服务端回复包，路由层据此跳过响应下发。</returns>
    private async Task<SyncPacket?> HandleSubscriptionUpdateAsync(SubscriptionUpdatePacket? update, long characterId)
    {
        if (update is null)
        {
            Logger.LogWarning("HandleSubscriptionUpdate: 收到 null 包");
            return null;
        }

        // 安全缺口（与 HandleInteractionIntent 同根问题）：
        // characterId 来自 message.Header.CharacterId（客户端填充），未与当前连接绑定的角色 ID 比对。
        // 恶意客户端可填充他人 ID 触发以受害者身份的订阅变更，导致其 AOI 推送被恶意增删。
        // 待 IPlayerSessionGrain 扩展会话校验接口后，在此追加 ValidateSessionAsync 校验。
        if (characterId == 0)
        {
            Logger.LogWarning(
                "HandleSubscriptionUpdate: CharacterId 为 0，无法路由订阅。AddedCount={AddedCount}, RemovedCount={RemovedCount}",
                update.AddedChunks?.Length ?? 0,
                update.RemovedChunks?.Length ?? 0);
            return null;
        }

        var zoneShard = _clusterClient.GetGrain<IZoneShardGrain>(_shardRouter.Resolve(characterId));

        if (update.AddedChunks is { Length: > 0 } added)
        {
            await zoneShard.SubscribeSessionAsync(characterId, added);
            Logger.LogInformation(
                "HandleSubscriptionUpdate: CharacterId={CharacterId} 新增订阅 {Count} 个 chunk",
                characterId, added.Length);
        }

        if (update.RemovedChunks is { Length: > 0 } removed)
        {
            await zoneShard.UnsubscribeSessionAsync(characterId, removed);
            Logger.LogInformation(
                "HandleSubscriptionUpdate: CharacterId={CharacterId} 移除订阅 {Count} 个 chunk",
                characterId, removed.Length);
        }

        // 订阅更新无需服务端回复包
        return null;
    }

    /// <summary>
    /// 处理未知类型的同步包：记录警告并返回空响应，避免静默路由到 <see cref="HandleInputAsync"/>。
    /// </summary>
    /// <param name="packet">未识别的同步包。</param>
    /// <returns>一个空的 <see cref="WorldPatchManifestPacket"/> 作为错误响应。</returns>
    private Task<SyncPacket> HandleUnknownPacketAsync(SyncPacket packet)
    {
        Logger.LogWarning(
            "收到未识别的同步包类型，已拒绝处理。Kind={Kind}, ProtocolVersion={ProtocolVersion}",
            packet.Kind,
            packet.ProtocolVersion);
        return Task.FromResult<SyncPacket>(new WorldPatchManifestPacket());
    }

    /// <summary>
    /// 简单的 per-interactorId 速率限制：限制每个 interactorId 每秒最多
    /// <see cref="InteractionRateLimitPerSecond"/> 次交互意图请求。
    /// 使用固定窗口近似（最小间隔 <see cref="InteractionRateLimitIntervalMs"/> 毫秒），
    /// 并周期性清理超过 1 秒未活动的条目以控制内存占用。
    /// </summary>
    /// <param name="interactorId">交互者角色 ID。</param>
    /// <returns>true 表示允许通过；false 表示被速率限制。</returns>
    private bool CheckInteractionRateLimit(long interactorId)
    {
        var now = DateTime.UtcNow;

        lock (_rateLimitLock)
        {
            // 周期性清理：移除超过 1 秒未活动的条目
            if ((now - _lastRateLimitCleanup).TotalSeconds > 1)
            {
                _lastRateLimitCleanup = now;
                var cutoff = now.AddSeconds(-1);
                var staleKeys = _interactionLastRequestTime
                    .Where(kvp => kvp.Value < cutoff)
                    .Select(kvp => kvp.Key)
                    .ToList();
                foreach (var key in staleKeys)
                {
                    _interactionLastRequestTime.Remove(key);
                }
            }

            // 速率限制：检查距上次请求的时间间隔
            if (_interactionLastRequestTime.TryGetValue(interactorId, out var lastTime))
            {
                var elapsedMs = (now - lastTime).TotalMilliseconds;
                if (elapsedMs < InteractionRateLimitIntervalMs)
                {
                    return false;
                }
            }

            _interactionLastRequestTime[interactorId] = now;
            return true;
        }
    }

    /// <summary>
    /// 处理重连恢复包：根据客户端状态决定恢复策略。
    /// </summary>
    private async Task<SyncPacket> HandleReconnectAsync(ReconnectResumePacket resume)
    {
        var characterId = (long)resume.LocalCharacterId;

        Logger.LogInformation(
            "断线重连恢复开始。CharacterId={CharacterId}, BaselineVersion={BaselineVersion}, WorldPatchVersion={WorldPatchVersion}, LastAppliedDiffSeq={LastAppliedDiffSeq}",
            characterId,
            resume.BaselineVersion,
            resume.WorldPatchVersion,
            resume.LastAppliedDiffSeq);

        var sessionGrain = _clusterClient.GetGrain<IPlayerSessionGrain>(characterId);

        var diffLog = _clusterClient.GetGrain<IWorldDiffLogGrain>("global");
        var diffLogStats = await diffLog.GetStatsAsync();
        var serverHeadDiffSeq = Math.Max(0, diffLogStats.NextSeq - 1);
        var decision = await sessionGrain.ResumeAsync(resume, serverHeadDiffSeq, ServerWorldPatchVersion);

        SyncPacket response = decision switch
        {
            ResumeDecision.ResumeIncremental => BuildIncrementalResume(resume, serverHeadDiffSeq),
            ResumeDecision.RequireLauncherPatch => BuildLauncherPatchRequiredResponse(),
            ResumeDecision.ResendFullChunks => BuildFullChunksResendResponse(resume),
            ResumeDecision.ForceReLogin => BuildForceReLoginResponse(),
            _ => new WorldPatchManifestPacket(),
        };

        Logger.LogInformation(
            "断线重连恢复完成。CharacterId={CharacterId}, Decision={Decision}",
            characterId,
            decision);

        return response;
    }

    /// <summary>
    /// 构建增量恢复响应。
    /// </summary>
    private WorldPatchManifestPacket BuildIncrementalResume(ReconnectResumePacket resume, long serverHeadDiffSeq)
    {
        return new WorldPatchManifestPacket
        {
            BaselineVersion = resume.BaselineVersion,
            WorldPatchVersion = resume.WorldPatchVersion,
            ManifestUrl = string.Empty,
            ManifestSha256 = string.Empty,
            PatchCutoverDiffSeq = resume.LastAppliedDiffSeq,
        };
    }

    /// <summary>
    /// 构建需要启动器补丁的响应。
    /// </summary>
    private WorldPatchManifestPacket BuildLauncherPatchRequiredResponse()
    {
        return new WorldPatchManifestPacket
        {
            BaselineVersion = ServerBaselineVersion,
            WorldPatchVersion = ServerWorldPatchVersion,
            ManifestUrl = string.Empty,
            ManifestSha256 = string.Empty,
            PatchCutoverDiffSeq = 0,
        };
    }

    /// <summary>
    /// 构建全量重发响应。
    /// </summary>
    private WorldPatchManifestPacket BuildFullChunksResendResponse(ReconnectResumePacket resume)
    {
        return new WorldPatchManifestPacket
        {
            BaselineVersion = resume.BaselineVersion,
            WorldPatchVersion = resume.WorldPatchVersion,
            ManifestUrl = string.Empty,
            ManifestSha256 = string.Empty,
            PatchCutoverDiffSeq = resume.LastAppliedDiffSeq,
        };
    }

    /// <summary>
    /// 构建强制重新登录响应。
    /// </summary>
    private WorldPatchManifestPacket BuildForceReLoginResponse()
    {
        return new WorldPatchManifestPacket
        {
            BaselineVersion = 0,
            WorldPatchVersion = 0,
            ManifestUrl = string.Empty,
            ManifestSha256 = string.Empty,
            PatchCutoverDiffSeq = 0,
        };
    }

    /// <summary>
    /// P1.4：处理客户端上行的战斗动作（攻击/技能/道具）。
    /// 路由到 CombatSystemGrain 裁决，返回 DamagePacket 或 null。
    /// </summary>
    private async Task<SyncPacket?> HandleCombatActionAsync(CombatActionPacket combatAction)
    {
        var attackerId = (long)combatAction.AttackerId;

        // 基本校验
        if (combatAction.AttackerId == 0)
        {
            Logger.LogWarning("战斗动作无效：AttackerId=0");
            return null;
        }

        try
        {
            // 路由到 CombatSystemGrain（以攻击者 ID 为 Grain Key，简化版 Phase 1）
            var combatGrain = _clusterClient.GetGrain<ICombatSystemGrain>(_shardRouter.Resolve(attackerId));

            var request = new CombatActionRequest
            {
                AttackerId = combatAction.AttackerId,
                TargetId = combatAction.TargetId,
                ActionKind = (byte)combatAction.ActionKind,
                SkillId = combatAction.SkillId,
                ClientTick = combatAction.ClientTick,
                AttackerYaw = combatAction.AttackerYaw,
            };

            var verdict = await combatGrain.ProcessActionAsync(request);

            if (!verdict.IsHit)
            {
                return null; // 闪避/无效，不下发包
            }

            // 构造 DamagePacket 返回给客户端
            var damagePacket = new DamagePacket
            {
                AttackerId = combatAction.AttackerId,
                TargetId = combatAction.TargetId,
                DamageAmount = verdict.DamageAmount,
                DamageType = verdict.DamageType,
                IsCritical = verdict.IsCritical,
                RemainingHp = verdict.TargetRemainingHp,
                MaxHp = verdict.TargetMaxHp,
                SkillId = combatAction.SkillId,
                ServerTick = verdict.ServerTick,
            };

            // 如果目标死亡，后续由 ZoneShard 广播 DeathPacket（TODO Phase 2）
            if (verdict.IsTargetDead)
            {
                Logger.LogInformation(
                    "战斗击杀。AttackerId={AttackerId}, TargetId={TargetId}, Damage={Damage}",
                    combatAction.AttackerId, combatAction.TargetId, verdict.DamageAmount);
            }

            return damagePacket;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "处理战斗动作失败。AttackerId={AttackerId}, TargetId={TargetId}",
                combatAction.AttackerId, combatAction.TargetId);
            return null;
        }
    }

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
}

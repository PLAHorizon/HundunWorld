using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Horizon.Game.Core;
using Horizon.Game.Core.Interfaces;
using Horizon.Game.Gateway.Configuration;
using Horizon.Game.Gateway.Services;
using Horizon.Game.Message;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using Horizon.Game.Message.Sync;
using Horizon.Core.Security;
using MemoryPack;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TouchSocket.Sockets;

namespace Horizon.Game.Gateway.Network;

/// <summary>
/// 客户端数据接收流水线（阶段 3 从 <see cref="GameNetworkServer"/> 拆分）：<br/>
/// 负责上行消息帧的逐级处理——连接确保（Stage 1）、活跃时间更新（Stage 2）、
/// 角色映射注册 + Presence 兜底刷新（Stage 3）、鉴权令牌验证（Stage 4）、
/// 消息路由分发（Stage 5）、响应拦截（Stage 6）。连接生命周期见
/// <see cref="ConnectionLifecycleManager"/>；<see cref="GameNetworkServer"/> 仅保留
/// TcpService 生命周期与事件接线。
/// </summary>
public sealed class ClientPacketPipeline
{
    private readonly ILogger<ClientPacketPipeline> _logger;
    private readonly IOptionsMonitor<GatewayOptions> _gatewayOptions;
    private readonly IConnectionManager _connectionManager;
    private readonly IEnumerable<IMessageHandler> _messageHandlers;
    private readonly ICharacterFingerprintService? _fingerprintService;
    private readonly PlayerDespawnScheduler _despawnScheduler;
    private readonly PresenceRefreshService _presenceRefreshService;
    private readonly UserAuthTokenProvider? _authTokenProvider;
    private readonly HorizonMessageAdapter _adapter;
    private readonly ConnectionLifecycleManager _lifecycle;

    // ── 诊断日志频率限制 ──
    private long _lastReceiveDiagTicks = DateTime.MinValue.Ticks;
    private static readonly TimeSpan ReceiveDiagInterval = TimeSpan.FromSeconds(10);

    public ClientPacketPipeline(
        ILogger<ClientPacketPipeline> logger,
        IOptionsMonitor<GatewayOptions> gatewayOptions,
        IConnectionManager connectionManager,
        IEnumerable<IMessageHandler> messageHandlers,
        PlayerDespawnScheduler despawnScheduler,
        PresenceRefreshService presenceRefreshService,
        HorizonMessageAdapter adapter,
        ConnectionLifecycleManager lifecycle,
        UserAuthTokenProvider? authTokenProvider = null,
        ICharacterFingerprintService? fingerprintService = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _gatewayOptions = gatewayOptions ?? throw new ArgumentNullException(nameof(gatewayOptions));
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
        _messageHandlers = messageHandlers ?? throw new ArgumentNullException(nameof(messageHandlers));
        _despawnScheduler = despawnScheduler ?? throw new ArgumentNullException(nameof(despawnScheduler));
        _presenceRefreshService = presenceRefreshService ?? throw new ArgumentNullException(nameof(presenceRefreshService));
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        _authTokenProvider = authTokenProvider;
        _fingerprintService = fingerprintService;
    }

    /// <summary>
    /// 消息路由分发：遍历 handler，首个匹配并验证通过的 handler 处理消息。
    /// </summary>
    public async Task<(bool IsSuccess, MessageUnion? Data)> ProcessMessageAsync(ITcpSessionClient client, HorizonMessagePacket message)
    {
        foreach (var handler in _messageHandlers)
        {
            if (handler.CanHandle(message) && handler.ValidateMessage(message))
            {
                return await handler.HandleAsync(client, message);
            }
        }
        _logger.LogWarning(
            "无 handler 匹配消息。MessageType={MessageType}, ServiceType={ServiceType}, Client={ClientId}",
            message.Header.MessageType, message.ServiceType, client.Id);
        return (false, null);
    }

    /// <summary>
    /// 接收数据事件（由 <see cref="GameNetworkServer"/> 的 Received 事件委托；薄编排层）。
    /// </summary>
    public async Task HandleReceivedAsync(ITcpSessionClient client, ReceivedDataEventArgs e)
    {
        try
        {
            // Stage 1: 连接确保（补注册）
            var connection = await StageConnectionEnsureAsync(client);
            if (connection == null) return;

            // Stage 2: 活跃时间更新 + 首包标记
            // HasReceivedData 是幽灵连接判定的权威依据：StageConnectionEnsureAsync 可能在**首包到达时**
            // 才补注册连接（此时 ConnectedTime == 首包时刻），仅凭 LastActiveTime 与 ConnectedTime 的
            // 时间差无法区分"刚建立就收到首包"与"从未收到数据"。
            connection.LastActiveTime = DateTime.UtcNow;
            connection.HasReceivedData = true;

            try
            {
                // 解析消息帧
                if (e.RequestInfo is not HorizonMessageInfo horizonRequest || horizonRequest.Packet == null)
                {
                    // 帧解析失败（长度非法 / 校验和不匹配 / 解压或反序列化失败）：
                    // 这里补上远端地址，用于定位是"哪个客户端"发来的非法帧；
                    // 帧字节级细节（原始 8 字节头 + 载荷头部十六进制）由 HorizonMessageInfo 侧输出。
                    _logger.LogWarning(
                        "收到无法解析的消息帧: Id={Id}, Remote={Remote}", client.Id, connection.RemoteAddress);
                    return;
                }

                var messagePacket = horizonRequest.Packet;

                // ── 数据接收诊断日志（每10秒最多一次）──
                // 记录最后活跃时间、消息类型和角色ID，方便排查空闲超时和延迟问题
                var receiveNowTicks = DateTime.UtcNow.Ticks;
                if (receiveNowTicks - Interlocked.Read(ref _lastReceiveDiagTicks) >= ReceiveDiagInterval.Ticks)
                {
                    Interlocked.Exchange(ref _lastReceiveDiagTicks, receiveNowTicks);
                    _logger.LogInformation(
                        "[NetDiag] 数据接收：ConnectionId={ConnectionId}, MessageType={MessageType}, CharacterId={CharacterId}, LastActive={LastActive:O}, Remote={Remote}",
                        connection.ConnectionId, messagePacket.Header.MessageType, messagePacket.Header.CharacterId,
                        connection.LastActiveTime, connection.RemoteAddress);
                }

                // Stage 4: 鉴权令牌验证
                if (!await StageAuthValidationAsync(client, messagePacket)) return;

                // Stage 3（后移至鉴权之后）：角色映射注册 + Presence 兜底刷新。
                // 安全审查 2026-08：原顺序在鉴权前注册映射，未认证连接可凭自报 CharacterId
                // 抢注他人角色映射（fanout 快照被重定向到攻击者连接）。未通过 Stage 4 的消息不再注册。
                _ = StageCharacterMappingAndPresenceAsync(connection, messagePacket);

                // Stage 5: 消息路由分发
                var (isSuccess, responseData) = await ProcessMessageAsync(client, messagePacket);

                // Stage 6: 响应拦截（令牌更新/角色映射）
                if (isSuccess && responseData != null)
                {
                    StageResponseIntercept(connection, client, responseData);
                }
            }
            catch (ArgumentException argEx)
            {
                _logger.LogWarning(argEx, "消息验证失败: {Id}", client.Id);
            }
            catch (Exception deserializeEx)
            {
                _logger.LogError(deserializeEx, "反序列化消息时发生错误: {Id}", client.Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理接收数据事件时发生错误: {Id}", client.Id);
        }
    }

    /// <summary>
    /// Stage 1: 连接确保——如果连接不存在则补注册。
    /// </summary>
    private async Task<IGameConnection?> StageConnectionEnsureAsync(ITcpSessionClient client)
    {
        // 快速检测：如果连接已不在线，直接关闭以触发 OnClientDisconnected
        if (!client.Online)
        {
            try { await client.CloseAsync("连接已离线"); } catch { /* 忽略 */ }
            return null;
        }

        var connection = _connectionManager.GetConnection(client.Id);
        if (connection == null)
        {
            connection = await _lifecycle.EnsureConnectionRegisteredAsync(client, logOnCreate: true);
            if (connection == null)
            {
                _logger.LogWarning("收到数据但连接不存在，且无法补注册: {Id}", client.Id);
            }
        }
        return connection;
    }

    /// <summary>
    /// Stage 3: 角色映射注册（从请求体提取 CharacterId 并注册映射）+ Presence TTL 兜底刷新。
    /// <para>安全审查 2026-08：由 <c>async void</c> 改为 <c>Task</c> + 全量异常兜底——
    /// 原 <c>async void</c> 中未捕获异常会直接打崩网关进程，而非进入 HandleReceivedAsync 的 catch。</para>
    /// </summary>
    private async Task StageCharacterMappingAndPresenceAsync(IGameConnection connection, HorizonMessagePacket messagePacket)
    {
        // 安全审查 2026-08：由 async void 改为 Task + 全量异常兜底——原 async void 中
        // 未捕获异常会直接打崩网关进程，而非进入 HandleReceivedAsync 的 catch。
        try
        {
            await StageCharacterMappingAndPresenceCoreAsync(connection, messagePacket);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Stage 3 角色映射注册/Presence 刷新失败。ConnectionId={ConnectionId}", connection.ConnectionId);
        }
    }

    private async Task StageCharacterMappingAndPresenceCoreAsync(IGameConnection connection, HorizonMessagePacket messagePacket)
    {
        // 后备角色映射注册：从请求体中提取 CharacterId 并注册映射。
        // SyncPacket 消息的 Header.CharacterId 通常为 0（CharacterId 在 InputPacket 载荷中），
        // 因此需要解码 SyncFrameMessage 来提取。这是 Despawn 能反查出 characterId 的前提。
        // 优先级：HandshakePacket.LocalCharacterId > InputPacket.CharacterId > Header.CharacterId
        long fallbackCharacterId = messagePacket.Header.CharacterId != 0
            ? (long)messagePacket.Header.CharacterId
            : 0;

        if (fallbackCharacterId == 0 && messagePacket.Body is SyncFrameMessage reqSyncFrame)
        {
            try
            {
                var reqSyncPacket = SyncPacketCodec.Decode(reqSyncFrame.Frame);
                fallbackCharacterId = reqSyncPacket switch
                {
                    HandshakePacket h => (long)h.LocalCharacterId,
                    InputPacket i => (long)i.CharacterId,
                    _ => 0
                };
            }
            catch { /* 解码失败，忽略 */ }
        }

        // 无角色绑定的账号消息（BuildGameUserRequest / CharacterListRequest 等）没有映射可注册，
        // 静默跳过（此前会落入未鉴权告警分支，登录突发期每条消息刷一条误导性 Warning）。
        if (fallbackCharacterId == 0)
        {
            return;
        }

        // 安全审查 2026-08（修订）：角色映射注册要求本连接已完成登录鉴权，防止未认证连接
        // 伪造 CharacterId 抢注他人角色映射（fanout 快照被重定向到攻击者连接）。
        // 鉴权信号二选一（任一通过即可注册）：
        //   1. 连接级令牌 connection.AuthToken（Stage 6 在网关 LoginResponse/TokenLoginResponse/
        //      EnterGameResponse 响应时写入）；
        //   2. 消息级令牌 Header.AuthToken（WebApi 令牌登录链路：客户端经 WebApi signin 取
        //      ImAuthToken 后直连网关，从不发送网关 LoginRequest，连接级令牌永远为空——
        //      UE5 GameGatewaySessionClient 与 GengDi 启动器均为此模式）。
        // 修订原因：原实现只认连接级令牌，令牌登录链路的握手/输入全部被拒，
        // HandshakePacket 触发的旧 fingerprint 释放与 Despawn 取消等关键副作用全部失效。
        // _authTokenProvider 未配置（开发环境无鉴权）时保持原行为。
        if (_authTokenProvider != null && string.IsNullOrEmpty(connection.AuthToken)
            && !IsMessageTokenValid(messagePacket))
        {
            _logger.LogWarning(
                "未鉴权连接请求注册角色映射，已拒绝。ConnectionId={ConnectionId}, MessageType={MessageType}, HeaderCharacterId={HeaderCharacterId}",
                connection.ConnectionId, messagePacket.Header.MessageType, messagePacket.Header.CharacterId);
            return;
        }

        if (fallbackCharacterId != 0)
        {
            var existingConn = _connectionManager.GetConnectionByCharacterId(fallbackCharacterId);
            if (existingConn == null || existingConn.ConnectionId != connection.ConnectionId)
            {
                // 修复（角色映射弹跳）：当已有不同连接持有该角色映射且仍在线时，
                // 仅允许 HandshakePacket 覆盖映射（握手是新会话的权威信号）。
                // InputPacket 不覆盖，防止旧连接的残留重传反复抢夺映射，
                // 导致 fanout 快照包被投递到错误连接（根因：其他客户端看不到角色移动）。
                bool isHandshake = messagePacket.Body is SyncFrameMessage hsFrame
                    && TryDecodeAsHandshake(hsFrame);
                bool existingStillAlive = existingConn is { IsConnected: true };

                // 修复 BUG（重连死循环：每 20 秒一次的"关闭连接→重连→再关闭"循环）：
                // 原条件 `!existingStillAlive || isHandshake || existingConn == null` 在
                // existingConn==null 时任何消息都会补注册映射。当实体已从 ZoneShardGrain
                // 中消失（被 DespawnImmediatelyAsync 清理）后，客户端重连发送 InputPacket
                // 仍会补注册映射，下次续约再次检测到"实体不在 ZoneShardGrain 中但
                // ConnectionManager 仍有映射"→关闭连接→客户端重连→循环。
                // 修复：existingConn==null 时，只有 HandshakePacket 才补注册映射
                // （握手会触发 EnterWorldAsync 重新 Spawn 实体到 ZoneShardGrain）。
                // InputPacket 等其他消息不补注册，客户端必须重新握手才能恢复。
                bool shouldRegister = isHandshake
                    || (existingConn != null && !existingStillAlive);

                if (shouldRegister)
                {
                    _connectionManager.RegisterCharacter(fallbackCharacterId, connection);
                    _despawnScheduler.CancelDespawn(fallbackCharacterId);

                    // 修复（重连后"角色已在线" — 旧 fingerprint 未释放）：
                    // 真正断网后客户端 30s 心跳超时触发重连，但服务端 IdleTimeout=30s 可能尚未清理旧连接。
                    // 旧连接的 fingerprint（TTL 5min）仍存在于 Redis 中，新连接 EnterGame 时 TryAcquire 失败。
                    // HandshakePacket 是新会话的权威信号，此处主动释放旧 fingerprint，
                    // 确保后续 EnterGame 的 TryAcquire 能成功。
                    if (isHandshake && _fingerprintService != null)
                    {
                        try
                        {
                            await _fingerprintService.ReleaseAsync(fallbackCharacterId);
                            _logger.LogInformation(
                                "HandshakePacket 触发释放旧 fingerprint: CharacterId={CharacterId}",
                                fallbackCharacterId);
                        }
                        catch (Exception fpEx)
                        {
                            _logger.LogWarning(fpEx,
                                "HandshakePacket 释放旧 fingerprint 失败（忽略）: CharacterId={CharacterId}",
                                fallbackCharacterId);
                        }
                    }
                    _logger.LogInformation(
                        "请求体补注册角色映射: CharacterId={CharacterId}, ConnectionId={ConnectionId}, MessageType={MessageType}, IsHandshake={IsHandshake}",
                        fallbackCharacterId, connection.ConnectionId, messagePacket.Header.MessageType, isHandshake);
                }
                else
                {
                    // 映射不存在且非握手：拒绝补注册。
                    // 实体可能已从 ZoneShardGrain 中消失（DespawnImmediatelyAsync 已清理映射），
                    // 客户端必须重新握手才能恢复（握手会触发 EnterWorldAsync 重新 Spawn 实体）。
                    _logger.LogWarning(
                        "角色映射不存在且非握手消息，拒绝补注册（需重新握手）。CharacterId={CharacterId}, ConnectionId={ConnectionId}, MessageType={MessageType}",
                        fallbackCharacterId, connection.ConnectionId, messagePacket.Header.MessageType);
                }
            }

            // 修复 BUG（心跳 TTL 反复过低）：客户端只发送 InputPacket 不发送 Heartbeat 消息，
            // 导致 Redis presence TTL 在 90 秒后过期。此处作为兜底机制：
            // 收到已绑定角色的数据时，每 30 秒刷新一次 presence TTL，
            // 确保只要客户端在发送输入，角色在线状态就不会过期。
            // 不阻塞消息处理（fire-and-forget + 异常吞并）。
            TryRefreshPresenceTtlInBackground(fallbackCharacterId);
        }
    }

    /// <summary>
    /// Stage 3 辅助：校验消息级令牌（<see cref="MessageHeader.AuthToken"/>）。
    /// 与 Stage 4 同一套 <see cref="UserAuthTokenProvider.ValidateToken"/> 逻辑
    /// （含 <c>ValidateTokenMachineId</c> 配置），供连接级令牌缺失时（WebApi 令牌登录链路）
    /// 的映射注册门禁复用。调用前提：<c>_authTokenProvider != null</c>。
    /// </summary>
    private bool IsMessageTokenValid(HorizonMessagePacket messagePacket)
    {
        var authToken = messagePacket.Header.AuthToken;
        if (string.IsNullOrWhiteSpace(authToken))
        {
            return false;
        }

        try
        {
            var machineId = messagePacket.Header.MachineId;
            var expectedMachineId = _gatewayOptions.CurrentValue.ValidateTokenMachineId ? machineId : null;
            return _authTokenProvider!.ValidateToken(authToken, expectedMachineId: expectedMachineId).IsValid;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Stage 4: 鉴权令牌验证。返回 true 表示通过，false 表示拒绝（已发送错误响应）。
    /// </summary>
    private async Task<bool> StageAuthValidationAsync(ITcpSessionClient client, HorizonMessagePacket messagePacket)
    {
        // 验证消息头的必需字段
        if (!messagePacket.Header.IsResponse)
        {
            if (messagePacket.Header.GameId <= 0)
            {
                _logger.LogWarning("收到无效消息: GameId必须为正数. Client: {Id}", client.Id);
                return false;
            }

            if (messagePacket.Header.ServerId <= 0)
            {
                _logger.LogWarning("收到无效消息: ServerId必须为正数. Client: {Id}", client.Id);
                return false;
            }
        }

        // 用户鉴权令牌验证：非登录/注册请求必须携带有效的鉴权令牌
        if (_authTokenProvider != null && !IsAuthExemptMessage(messagePacket))
        {
            var authToken = messagePacket.Header.AuthToken;
            if (string.IsNullOrWhiteSpace(authToken))
            {
                _logger.LogWarning("收到未携带鉴权令牌的请求，拒绝服务. Client: {Id}, MessageType: {MessageType}",
                    client.Id, messagePacket.Header.MessageType);
                await SendAuthErrorAsync(client, messagePacket, "请求缺少鉴权令牌，请重新登录");
                return false;
            }

            var machineId = messagePacket.Header.MachineId;
            var expectedMachineId = _gatewayOptions.CurrentValue.ValidateTokenMachineId ? machineId : null;
            var validationResult = _authTokenProvider.ValidateToken(authToken, expectedMachineId: expectedMachineId);
            if (!validationResult.IsValid)
            {
                _logger.LogWarning("鉴权令牌验证失败，拒绝服务. Client: {Id}, Reason: {Reason}, MessageType: {MessageType}",
                    client.Id, validationResult.ErrorMessage, messagePacket.Header.MessageType);
                await SendAuthErrorAsync(client, messagePacket, "鉴权失败，请重新登录");
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Stage 6: 响应拦截——从响应中提取鉴权令牌并存储到连接，以及注册角色映射。
    /// </summary>
    private void StageResponseIntercept(IGameConnection connection, ITcpSessionClient client, MessageUnion responseData)
    {
        _logger.LogDebug("响应拦截: Type={ResponseType}, ConnectionId={ConnectionId}",
            responseData.GetType().Name, connection.ConnectionId);

        if (responseData is LoginResponse loginResp && !string.IsNullOrEmpty(loginResp.AuthToken))
        {
            connection.AuthToken = loginResp.AuthToken;
            _logger.LogDebug("已更新连接 {Id} 的鉴权令牌（登录）", client.Id);
        }
        else if (responseData is TokenLoginResponse tokenLoginResp && !string.IsNullOrEmpty(tokenLoginResp.AuthToken))
        {
            connection.AuthToken = tokenLoginResp.AuthToken;
            _logger.LogDebug("已更新连接 {Id} 的鉴权令牌（Token登录）", client.Id);
        }
        else if (responseData is EnterGameResponse enterResp)
        {
            // 更新鉴权令牌（可能为空，例如 _authTokenProvider 未配置或失败时）
            if (!string.IsNullOrEmpty(enterResp.AuthToken))
            {
                connection.AuthToken = enterResp.AuthToken;
                _logger.LogDebug("已更新连接 {Id} 的鉴权令牌（含角色Id）", client.Id);
            }

            // 绑定 characterId → IGameConnection 映射，使 fanout 推送（按 characterId 寻址）能命中本连接
            // 注意：此绑定不能依赖 AuthToken 非空，否则令牌缺失时 fanout 推送的快照包会被全部丢弃
            // 优先使用顶层 CharacterId，回退到 CharacterInfo.CharacterId
            if (enterResp.Success)
            {
                var characterId = (long)(enterResp.CharacterId != 0
                    ? enterResp.CharacterId
                    : enterResp.CharacterInfo?.CharacterId ?? 0);
                if (characterId != 0)
                {
                    _connectionManager.RegisterCharacter(characterId, connection);
                    // 取消任何挂起的延迟 Despawn（断线重连场景：避免角色被误注销）
                    _despawnScheduler.CancelDespawn(characterId);
                    _logger.LogInformation(
                        "EnterGame响应注册角色映射: CharacterId={CharacterId}, ConnectionId={ConnectionId}",
                        characterId, connection.ConnectionId);
                }
                else
                {
                    _logger.LogWarning(
                        "EnterGame响应成功但无法提取CharacterId。TopLevel={TopLevel}, CharacterInfo={CharacterInfo}",
                        enterResp.CharacterId, enterResp.CharacterInfo?.CharacterId ?? 0);
                }
            }
            else
            {
                _logger.LogWarning("EnterGame响应失败，跳过角色映射注册。Message={Message}", enterResp.Message);
            }
        }
        else if (responseData is SyncFrameMessage syncFrameResp)
        {
            // SyncPacket 握手响应：解码 HandshakePacket 并注册 characterId → connection 映射
            // 握手路径不经过 EnterGameResponse，若不在此注册，fanout 推送（按 characterId 寻址）会全部丢弃
            try
            {
                var syncPacket = SyncPacketCodec.Decode(syncFrameResp.Frame);
                _logger.LogDebug("SyncFrame响应解码: Kind={Kind}, ConnectionId={ConnectionId}",
                    syncPacket.Kind, connection.ConnectionId);
                if (syncPacket is HandshakePacket handshakeResp)
                {
                    if (handshakeResp.LocalCharacterId != 0)
                    {
                        var characterId = (long)handshakeResp.LocalCharacterId;
                        _connectionManager.RegisterCharacter(characterId, connection);
                        // 取消任何挂起的延迟 Despawn（断线重连场景：避免角色被误注销）
                        _despawnScheduler.CancelDespawn(characterId);
                        _logger.LogInformation(
                            "Sync握手响应已注册角色映射: CharacterId={CharacterId}, ConnectionId={ConnectionId}",
                            characterId, connection.ConnectionId);
                    }
                    else
                    {
                        _logger.LogWarning("Sync握手响应中 LocalCharacterId=0，跳过角色映射注册");
                    }
                }
                else if (syncPacket is HandshakeRejectPacket rejectResp)
                {
                    // P1.3：协议版本过低，服务器拒绝握手——不注册映射，客户端将触发强制更新。
                    _logger.LogWarning(
                        "Sync握手被拒绝（协议版本过低）: Reason={Reason}, MinimumVersion={MinimumVersion}, ConnectionId={ConnectionId}",
                        rejectResp.Reason, rejectResp.MinimumVersion, connection.ConnectionId);
                }
            }
            catch (Exception decodeEx)
            {
                _logger.LogWarning(decodeEx, "解码 SyncFrame 响应失败，跳过角色映射注册");
            }
        }
    }

    /// <summary>
    /// 兜底刷新角色 presence TTL（委托给 PresenceRefreshService）。
    /// </summary>
    private void TryRefreshPresenceTtlInBackground(long characterId)
    {
        _presenceRefreshService.TryRefreshInBackground(characterId);
    }

    /// <summary>
    /// 判断消息是否免除鉴权验证（登录、注册等无需鉴权的消息）
    /// </summary>
    /// <remarks>
    /// SyncPacket 免除网关层鉴权的原因：
    /// 1. 握手包（HandshakePacket）在玩家已登录并进入游戏后才发送，此时连接已通过鉴权；
    /// 2. 输入包（InputPacket）携带的是移动/操作数据，鉴权已在登录阶段完成；
    /// 3. SyncPacketHandler 内部已有自己的安全校验逻辑（如检查 CharacterId、协议版本等）；
    /// 4. 在 HorizonMessagePacket 层面无法区分具体是哪种 SyncPacket，因此统一免除，由 Handler 内部做细粒度校验。
    /// </remarks>
    private static bool IsAuthExemptMessage(HorizonMessagePacket message)
    {
        // 收敛（与 MessageHandlerBase.CanHandle 一致）：以 Body 的权威元数据为准，防止客户端
        // 伪造 Header.MessageType=LoginRequest 绕过鉴权；Body 缺失时回退 Header（含 SyncPacket 帧）。
        var type = message.Body is INetworkMessage body ? body.Type : message.Header.MessageType;
        return type == MessageType.LoginRequest
            || type == MessageType.RegisterRequest
            || type == MessageType.TokenLoginRequest
            || type == MessageType.SyncPacket;
    }

    /// <summary>
    /// 尝试将 SyncFrameMessage 解码为 HandshakePacket（用于判断是否为握手请求）。
    /// 解码失败或非 HandshakePacket 时返回 false。
    /// </summary>
    private static bool TryDecodeAsHandshake(SyncFrameMessage syncFrame)
    {
        try
        {
            var packet = SyncPacketCodec.Decode(syncFrame.Frame);
            return packet is HandshakePacket;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 向客户端发送鉴权失败错误消息
    /// </summary>
    private async Task SendAuthErrorAsync(ITcpSessionClient client, HorizonMessagePacket originalMessage, string errorMessage)
    {
        try
        {
            var errorResponse = new AuthenticationError
            {
                ErrorCode = 1007,
                ErrorMessage = errorMessage,
                ErrorDetails = "用户鉴权验证失败，请重新登录获取有效的鉴权令牌",
                RetryAfterSeconds = 0,
                RequiresReconnect = true
            };

            var responsePacket = new HorizonMessagePacket
            {
                Header = new MessageHeader
                {
                    MessageType = MessageType.Error,
                    ServiceType = ServiceType.Account,
                    IsResponse = true,
                    ResponseToMessageId = originalMessage.Header.MessageId,
                    RequireResponse = false,
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    GameId = originalMessage.Header.GameId,
                    ZoneId = originalMessage.Header.ZoneId,
                    ServerId = originalMessage.Header.ServerId,
                },
                ServiceType = ServiceType.Account,
                Body = errorResponse,
                RawData = MemoryPackSerializer.Serialize(errorResponse),
            };

            var buff = _adapter.PackPacket(responsePacket);

            // 修复 BUG（Writing is not allowed after writer was completed）：
            // 原实现直接调用 client.SendAsync，绕过了 GameConnection._sendLock，
            // 与并发的 GameConnection.SendAsync（如快照分发/心跳推送）冲突，导致并发写入
            // TouchSocket pipeline，触发 InvalidOperationException。
            // 修复：通过 GameConnection.SendAsync 发送，复用其 _sendLock 串行化保护。
            // 连接在 StageConnectionEnsureAsync 中已确保注册，此处直接查询。
            var connection = _connectionManager.GetConnection(client.Id);
            if (connection != null)
            {
                await connection.SendAsync(buff);
            }
            else
            {
                // 极端场景：连接已被清理，直接发送（可能失败，但鉴权错误本身可忽略）
                await client.SendAsync(buff);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "发送鉴权错误消息失败: {Id}", client.Id);
        }
    }
}

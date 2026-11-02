using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Horizon.Game.Core.Interfaces;
using Horizon.Game.Core.Observability;
using Horizon.Game.Core.World;
using Horizon.Game.Message.Sync;
using Horizon.Game.Message.Sync.Server;
using Horizon.Game.Sim;
using Horizon.Orleans.Interface;
using Horizon.Orleans.Interface.World;
using Microsoft.Extensions.Logging;
using Orleans;

namespace Horizon.Game.Core.Handlers;

/// <summary>
/// 阶段 3.2 — <see cref="SyncPacketKind.Handshake"/> 与 <see cref="SyncPacketKind.ReconnectResume"/>
/// 流处理器：会话初始化（协议/公式版本校验、幂等、PlayerSessionGrain 握手、ZoneShard 实体注册与 AOI 订阅）
/// 与断线重连恢复决策。自 <see cref="SyncPacketHandler"/> 拆分而来。
/// </summary>
public sealed class SyncHandshakeHandler : SyncPacketFlowHandlerBase
{
    /// <summary>当前服务器基线版本（与游戏二进制版本一一对应）。</summary>
    private const int ServerBaselineVersion = 1;

    /// <summary>当前服务器世界补丁版本。</summary>
    private const int ServerWorldPatchVersion = 1;

    /// <summary>首次进入 World 时订阅出生 chunk 周围的 AOI 半径（与客户端 OnPlayerChunkChanged 的 ViewRadiusChunks 保持一致）。
    /// radius=28 → 57×57×57=185193 chunks（约为 radius=10 时的 20 倍体积），覆盖约 912m×912m×912m，视距约 448m。</summary>
    private const int InitialAoiRadiusChunks = 28;

    /// <summary>阶段 5.2 — 服务端输入去重（横切服务；握手写入基线并清理旧连接残留）。</summary>
    private readonly IInputDeduper _inputDeduper;

    /// <summary>阶段 5.2 — 同步指标采样（横切服务）。</summary>
    private readonly IMetricsSampler _metricsSampler;

    /// <summary>阶段 4.3 — 跨 Shard 边界双重订阅的空间分片解析器（null 兜底 = 禁用，仅订阅主 Shard）。</summary>
    private readonly IShardRegionResolver _regionResolver;

    /// <summary>阶段 0.6 — 协议版本匹配策略（null = 宽松默认：版本 != Current 仅告警放行；生产按 RequireStrictVersionMatch 注入严格实现拒绝）。</summary>
    private readonly IProtocolVersionPolicy? _protocolVersionPolicy;

    public SyncHandshakeHandler(
        ILogger<MessageHandlerBase> logger,
        IClusterClient clusterClient,
        IShardRouter shardRouter,
        IDSyncDataPlane? dsDataPlane,
        ISessionBindingValidator? sessionBindingValidator,
        SyncSessionState sessionState,
        Func<string> connectionIdProvider,
        IInputDeduper? inputDeduper = null,
        IMetricsSampler? metricsSampler = null,
        IShardRegionResolver? regionResolver = null,
        IProtocolVersionPolicy? protocolVersionPolicy = null)
        : base(logger, clusterClient, shardRouter, dsDataPlane, sessionBindingValidator, sessionState, connectionIdProvider)
    {
        _inputDeduper = inputDeduper ?? new SessionInputDeduper(sessionState);
        _metricsSampler = metricsSampler ?? new SyncMetricsSampler();
        _regionResolver = regionResolver ?? new ShardRegionResolver(shardRouter, 0);
        _protocolVersionPolicy = protocolVersionPolicy;
    }

    /// <summary>
    /// 处理握手包：初始化玩家会话并返回握手确认。
    /// 返回 <see cref="HandshakePacket"/>（回显 LocalCharacterId / InitialClientTick），
    /// 使客户端 <c>SyncPacketMessageHandler.HandshakeReceived</c> 事件能正确触发。
    /// </summary>
    public async Task<SyncPacket> HandleHandshakeAsync(HandshakePacket handshake)
    {
        // P1.3 + 阶段 0.6：协议版本校验——策略可配置。
        // 宽松（默认，ProtocolVersionPolicy）：版本 < MinimumSupported → 拒绝（强制更新）；
        //   版本 != Current → 允许连接（过渡期）并记录警告。
        // 严格（StrictProtocolVersionPolicy，生产 RequireStrictVersionMatch=true）：任何 != Current 都拒绝。
        var protocolVersionPolicy = _protocolVersionPolicy ?? ProtocolVersionPolicy.Instance;
        if (!protocolVersionPolicy.IsVersionAccepted(handshake.ProtocolVersion))
        {
            var reason = protocolVersionPolicy.GetRejectionReason(handshake.ProtocolVersion);
            // 发布窗口监控：旧客户端拒绝量按 reason 计数（协议版本不匹配）。
            // 注意：严格模式（RequireStrictVersionMatch）下协议拒绝发生在 SyncPacketHandler.RouteHandlerAsync
            // 预分派阶段（先于本处理器），本计数与预分派计数相加即完整拒绝量；两处不会重复计同一连接。
            SyncMetrics.HandshakeRejected.Add(1,
                new KeyValuePair<string, object?>("reason", "protocol_version"));
            Logger.LogWarning(
                "Sync握手拒绝：协议版本不匹配。ClientVersion={ClientVersion}, ServerVersion={ServerVersion}, Reason={Reason}",
                handshake.ProtocolVersion, SyncProtocolVersion.Current, reason);
            return new HandshakeRejectPacket
            {
                ServerVersion = SyncProtocolVersion.Current,
                MinimumVersion = SyncProtocolVersion.MinimumSupported,
                Reason = reason,
            };
        }

        if (handshake.ProtocolVersion != SyncProtocolVersion.Current)
        {
            Logger.LogWarning(
                "Sync握手版本过渡期：允许连接但版本不完全匹配。ClientVersion={ClientVersion}, ServerVersion={ServerVersion}",
                handshake.ProtocolVersion, SyncProtocolVersion.Current);
        }

        // 公式版本强制校验：不匹配即拒绝（公式版本是确定性的，必须双端精确对齐）。
        // 默认 0（旧客户端未填写）也会被拒绝，迫使客户端更新。
        if (!FormulaVersionPolicy.IsCompatible(handshake.FormulaVersion))
        {
            var reason = FormulaVersionPolicy.GetRejectionReason(handshake.FormulaVersion);
            // 发布窗口监控：旧客户端拒绝量按 reason 计数（公式版本不匹配）。
            // FormulaVersion 升级（3→4 等）期间的旧客户端在此被硬拒绝，此计数即灰度放量前的
            // "待升级客户端存量"，与公式版本升级/回滚演练（scripts/formula-rollout.sh）联动。
            SyncMetrics.HandshakeRejected.Add(1,
                new KeyValuePair<string, object?>("reason", "formula_version"));
            Logger.LogWarning(
                "Sync握手拒绝：公式版本不匹配。ClientFormulaVersion={ClientFormulaVersion}, ServerFormulaVersion={ServerFormulaVersion}",
                handshake.FormulaVersion, FormulaVersionPolicy.Current);
            return new HandshakeRejectPacket
            {
                ServerVersion = SyncProtocolVersion.Current,
                MinimumVersion = SyncProtocolVersion.MinimumSupported,
                Reason = reason,
            };
        }

        var characterId = (long)handshake.LocalCharacterId;

        // 重复握手幂等保护：同一 (characterId, connectionId) 已握手过时，直接返回成功响应，
        // 不重复执行 EnterWorldAsync / RegisterCharacter / AOI 订阅等副作用。
        // 修复 BUG：日志显示客户端在 ~400ms 内发送两次 Sync 握手，导致角色映射重复注册、
        // EnterWorldAsync 被调用两次、ZoneShard 实体重复创建。
        var idempotentConnId = CurrentConnectionId;
        var idempotentKey = (handshake.LocalCharacterId, idempotentConnId);
        lock (SessionState.HandshakeIdempotentLock)
        {
            if (!SessionState.HandshookConnections.Add(idempotentKey))
            {
                Logger.LogWarning(
                    "重复 Sync握手 被幂等拒绝，直接返回成功响应。CharacterId={CharacterId}, ConnectionId={ConnectionId}",
                    characterId, idempotentConnId);
                // 回显与首次握手一致的响应，触发客户端 HandshakeReceived 事件
                return new HandshakePacket
                {
                    LocalCharacterId = handshake.LocalCharacterId,
                    InitialClientTick = handshake.InitialClientTick,
                    ShardId = ShardRouter.Resolve(characterId),
                };
            }
        }

        Logger.LogInformation(
            "Sync握手开始。CharacterId={CharacterId}, ClientTick={ClientTick}",
            characterId,
            handshake.InitialClientTick);

        // 阶段 2.2：UE5 DS 灰度路由（协议/公式版本校验与握手幂等通过后、触碰 Orleans grain 前）。
        // UseDS 开启且该 shard 有已注册 DS 时，握手直接转发 DS（实体注册到 DS 侧 SimWorld），
        // 网关回显握手确认；无可用 DS / 转发失败 → 回退下方 Orleans 路径（灰度期双轨，任一时点可回切）。
        await StampAuthoritativeProfileAsync(handshake, characterId);

        if (await TryRouteToDsAsync(handshake, characterId))
        {
            return new HandshakePacket
            {
                LocalCharacterId = handshake.LocalCharacterId,
                InitialClientTick = handshake.InitialClientTick,
                ShardId = ShardRouter.Resolve(characterId),
            };
        }

        var sessionGrain = ClusterClient.GetGrain<IPlayerSessionGrain>(characterId);

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
                ShardId = ShardRouter.Resolve(characterId),
            };
        }

        // 握手表示新会话开始：设置当前连接输入基线，并清理去重字典中该角色的旧 tick 记录，
        // 避免客户端重新登录/断线重连后 ClientTick 从 0 重置时，所有新输入被误判为重复包而拒绝。
        // （PlayerSessionGrain.HandshakeAsync 内部会同步重置 PlayerSessionState.LastAcceptedClientTick）
        // 去重状态提取为 IInputDeduper 横切服务：基线写入（含时间戳保护）与旧连接残留清理
        // 与输入热路径共享同一份 SessionState 字典与锁，语义与拆分前逐字节一致。
        var connId = CurrentConnectionId;
        _inputDeduper.RecordHandshakeBaseline(handshake.LocalCharacterId, connId, handshake.InitialClientTick);
        _inputDeduper.CleanupCharacter(handshake.LocalCharacterId, connId);

        // 同步清理 HandshookConnections 中同一 characterId 的旧连接握手记录。
        // 避免锁嵌套（InputDedupLock → HandshakeIdempotentLock），单独加锁。
        // 断线重连时旧连接的握手记录应被清除，允许新连接正常握手。
        lock (SessionState.HandshakeIdempotentLock)
        {
            var staleHandshakeKeys = new List<(ulong, string)>();
            foreach (var k in SessionState.HandshookConnections)
            {
                if (k.CharacterId == handshake.LocalCharacterId && k.ConnectionId != connId)
                    staleHandshakeKeys.Add(k);
            }
            foreach (var sk in staleHandshakeKeys)
            {
                SessionState.HandshookConnections.Remove(sk);
            }
        }

        // 握手成功后，注册实体到 ZoneShard 权威模拟层并订阅 AOI
        try
        {
            var zoneShard = ClusterClient.GetGrain<IZoneShardGrain>(ShardRouter.Resolve(characterId));
            // 位置驱动初始订阅：不再在网关侧计算 185193 个 chunk key 传输给 grain，
            // 而是传空数组给 EnterWorldAsync（仅注册实体），然后通过 UpdateSessionPositionAsync
            // 让 grain 侧计算 AOI 并订阅。消除握手时的 ~1.5MB 跨进程 RPC 传输。
            // 指标仍记录实际 AOI chunk 数（57³=185193 @ R=28）供监控。
            var aoiChunkCount = Math.Pow(2 * InitialAoiRadiusChunks + 1, 3);
            _metricsSampler.RecordAoiChunkCount((long)aoiChunkCount);
            await zoneShard.EnterWorldAsync(
                characterId,
                (ulong)characterId,
                handshake.InitialX,
                handshake.InitialY,
                handshake.InitialZ,
                Array.Empty<ulong>());

            // 阶段 4.3：跨 Shard 边界双重订阅。
            // 权威实体只注册在主 Shard（characterId 路由），AOI 订阅则向视口覆盖的全部 Shard 建立——
            // 边界 Shard 上 session 无需实体（AOI 订阅与实体注册解耦），实体广播按实体所在 Shard 的
            // AOI 表扇出，跨 Shard 玩家双向可见无需实体副本迁移。
            var shards = _regionResolver.ResolveShards(
                characterId, handshake.InitialX, handshake.InitialY, handshake.InitialZ, InitialAoiRadiusChunks);
            foreach (var shardId in shards)
            {
                var targetShard = ClusterClient.GetGrain<IZoneShardGrain>(shardId);
                // grain 侧位置驱动订阅：自动计算新视野并订阅所有 chunk（首次订阅 = 全量新增）。
                await targetShard.UpdateSessionPositionAsync(characterId, handshake.InitialX, handshake.InitialY, handshake.InitialZ);
            }
            lock (SessionState.BoundaryShardLock)
            {
                SessionState.BoundaryShardSubscriptions[characterId] = new HashSet<long>(shards);
            }
            Logger.LogInformation(
                "Sync握手后实体注册完成（位置驱动订阅）。CharacterId={CharacterId}, ShardId={ShardId}, SubscribedShards=[{Shards}], AoiChunkCount={AoiChunkCount}",
                characterId, ShardRouter.Resolve(characterId), string.Join(",", shards), (long)aoiChunkCount);
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
            ShardId = ShardRouter.Resolve(characterId),
        };
    }

    /// <summary>
    /// 在把握手包转发给 DS 之前，用 CharacterGrain 的**权威档案**覆盖 Nickname/Level/Title 三字段。
    /// <para>
    /// 为什么必须在这里做：DS 灰度通道下握手不经过 <c>ZoneShardGrain</c>，DS 侧因此完全没有档案来源
    /// （<c>DsPlayer</c> 只有 HP 常量），远程玩家头顶只能显示 <c>Player_&lt;id&gt;</c> 与 <c>Lv.1</c>。
    /// <see cref="SyncPacketFlowHandlerBase.TryRouteToDsAsync"/> 是**重新编码解码后的对象**而非透传原始字节，
    /// 所以在此写入即可随握手原子到达 DS——不需要额外的网关→DS 通知通道，也不会出现
    /// "Spawn 已下发、档案后到" 的乱序（那会要求重发 Spawn，而重复 Spawn 会销毁重建远程角色）。
    /// </para>
    /// <para>
    /// 三字段无条件覆盖：客户端即使自行填入也会被权威值冲掉，故不构成昵称/等级伪造入口。
    /// </para>
    /// </summary>
    private async Task StampAuthoritativeProfileAsync(HandshakePacket handshake, long characterId)
    {
        // Orleans 通道的档案由 CharacterGrain.OnEnterZoneAsync 直接推给 ZoneShard，无需此次查询。
        if (DsDataPlane is not { IsEnabled: true }) return;

        try
        {
            var profile = await ClusterClient.GetGrain<ICharacterGrain>(characterId).GetSyncProfileAsync();
            handshake.Nickname = profile.Nickname ?? "";
            handshake.Level = profile.Level;
            handshake.Title = profile.Title ?? "";
            // 阵营也走同一通道：PvP 合法性必须由服务端档案判，客户端自报值一律冲掉。
            handshake.Faction = profile.Faction;
        }
        catch (Exception ex)
        {
            // 取档失败不阻断握手：DS 会回退占位名，宁可少显示昵称也不能让玩家进不去游戏。
            Logger.LogWarning(ex,
                "Sync握手：取权威档案失败，DS 侧将回退占位昵称。CharacterId={CharacterId}", characterId);
        }
    }

    /// <summary>
    /// 处理重连恢复包：根据客户端状态决定恢复策略。
    /// </summary>
    public async Task<SyncPacket> HandleReconnectAsync(ReconnectResumePacket resume)
    {
        var characterId = (long)resume.LocalCharacterId;

        Logger.LogInformation(
            "断线重连恢复开始。CharacterId={CharacterId}, BaselineVersion={BaselineVersion}, WorldPatchVersion={WorldPatchVersion}, LastAppliedDiffSeq={LastAppliedDiffSeq}",
            characterId,
            resume.BaselineVersion,
            resume.WorldPatchVersion,
            resume.LastAppliedDiffSeq);

        // 阶段 2.2：UE5 DS 灰度路由：重连恢复包直接转发 DS（DS 侧保留权威状态），
        // 网关返回握手确认（与 Orleans 可恢复决策的确认语义一致）；无 DS / 失败回退下方路径。
        if (await TryRouteToDsAsync(resume, characterId))
        {
            return BuildResumeHandshakeConfirmation(resume);
        }

        var sessionGrain = ClusterClient.GetGrain<IPlayerSessionGrain>(characterId);

        var diffLog = ClusterClient.GetGrain<IWorldDiffLogGrain>("global");
        var diffLogStats = await diffLog.GetStatsAsync();
        var serverHeadDiffSeq = Math.Max(0, diffLogStats.NextSeq - 1);
        var decision = await sessionGrain.ResumeAsync(resume, serverHeadDiffSeq, ServerWorldPatchVersion);

        // Phase 4: 补充 ReconnectDecisions 指标（按 decision 维度，提取为 IMetricsSampler）
        _metricsSampler.RecordReconnectDecision(decision);

        // 修复（静置断线后输入断流 — "无法移动 + 闪跳回原地"根因之一）：
        // 客户端 SyncPacketMessageHandler 仅对 HandshakePacket 触发 HandshakeReceived → MarkSyncHandshakeComplete。
        // 此前 ResumeIncremental/ResendFullChunks 均返回 WorldPatchManifestPacket（客户端落入 default 分支不处理），
        // 重连后 IsSyncHandshakeComplete 永不为 true → ECSUpdateDriver.FlushInputSendQueue 每帧丢弃 InputPacket
        // → 移动请求不上行 → 服务端权威位置冻结在静置坐标 → 恢复后客户端被全量快照拉回（"闪跳回原地"）。
        // 修复：可恢复决策（ResumeIncremental/ResendFullChunks）返回 HandshakePacket 作为握手确认，
        // 恢复客户端输入门控；RequireLauncherPatch/ForceReLogin 属不可恢复路径，保持返回 manifest（客户端不应继续游戏）。
        SyncPacket response = decision switch
        {
            ResumeDecision.ResumeIncremental => BuildResumeHandshakeConfirmation(resume),
            ResumeDecision.ResendFullChunks => BuildResumeHandshakeConfirmation(resume),
            ResumeDecision.RequireLauncherPatch => BuildLauncherPatchRequiredResponse(),
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
    /// 构建重连恢复的握手确认响应。
    /// <para>
    /// 客户端 SyncPacketMessageHandler 只对 <see cref="HandshakePacket"/> 触发 HandshakeReceived →
    /// MarkSyncHandshakeComplete。可恢复的续连决策（ResumeIncremental / ResendFullChunks）必须
    /// 以 HandshakePacket 确认，否则重连后客户端输入门控（IsSyncHandshakeComplete）永久关闭，
    /// 移动请求被 ECSUpdateDriver 丢弃，表现为"静置后无法移动 + 闪跳回静置位置"。
    /// </para>
    /// <para>
    /// InitialClientTick 回显 0：客户端 <c>OnHandshakeReceived</c> 不校验回显值，仅作为握手确认信号；
    /// 会话版本向量（diff seq 等）已由 <see cref="IPlayerSessionGrain.ResumeAsync"/> 在服务端状态机内落位，
    /// 无需回传。
    /// </para>
    /// </summary>
    private HandshakePacket BuildResumeHandshakeConfirmation(ReconnectResumePacket resume)
    {
        return new HandshakePacket
        {
            LocalCharacterId = resume.LocalCharacterId,
            InitialClientTick = 0,
            ShardId = ShardRouter.Resolve((long)resume.LocalCharacterId),
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
}

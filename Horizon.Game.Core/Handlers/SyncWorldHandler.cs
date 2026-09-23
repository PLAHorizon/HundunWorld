using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Horizon.Game.Core.Interfaces;
using Horizon.Game.Core.World;
using Horizon.Game.Message.Sync;
using Horizon.Orleans.Interface.World;
using Microsoft.Extensions.Logging;
using Orleans;

namespace Horizon.Game.Core.Handlers;

/// <summary>
/// 阶段 3.2 — <see cref="SyncPacketKind.SubscriptionUpdate"/> / <see cref="SyncPacketKind.BaselineResyncRequest"/>
/// / <see cref="SyncPacketKind.Unknown"/> 流处理器：AOI 动态订阅变更、baseline 全量重传请求与未知包拒绝。
/// 自 <see cref="SyncPacketHandler"/> 拆分而来。
/// </summary>
public sealed class SyncWorldHandler : SyncPacketFlowHandlerBase
{
    /// <summary>AOI 视野半径（chunk 数）。必须与 <c>ZoneShardGrain.SessionAoiRadiusChunks</c> / 握手路径一致。</summary>
    private const int AoiRadiusChunks = 28;

    /// <summary>阶段 4.3 — 跨 Shard 边界双重订阅的空间分片解析器（null 兜底 = 禁用，仅订阅主 Shard）。</summary>
    private readonly IShardRegionResolver _regionResolver;

    public SyncWorldHandler(
        ILogger<MessageHandlerBase> logger,
        IClusterClient clusterClient,
        IShardRouter shardRouter,
        IDSyncDataPlane? dsDataPlane,
        ISessionBindingValidator? sessionBindingValidator,
        SyncSessionState sessionState,
        Func<string> connectionIdProvider,
        IShardRegionResolver? regionResolver = null)
        : base(logger, clusterClient, shardRouter, dsDataPlane, sessionBindingValidator, sessionState, connectionIdProvider)
    {
        _regionResolver = regionResolver ?? new ShardRegionResolver(shardRouter, 0);
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
    /// <see cref="Horizon.Game.Message.Network.MessageHeader.CharacterId"/> 注入（客户端在进入游戏后填充）。
    /// </para>
    /// </summary>
    /// <param name="update">客户端上行的订阅变更包；可为 null（解码失败时）。</param>
    /// <param name="characterId">由 <see cref="Horizon.Game.Message.Network.MessageHeader.CharacterId"/> 注入的角色 ID。</param>
    /// <returns>始终返回 null：订阅更新无需服务端回复包，路由层据此跳过响应下发。</returns>
    public async Task<SyncPacket?> HandleSubscriptionUpdateAsync(SubscriptionUpdatePacket? update, long characterId)
    {
        if (update is null)
        {
            Logger.LogWarning("HandleSubscriptionUpdate: 收到 null 包");
            return null;
        }

        // Phase 2 安全补全：会话绑定校验（灰度开关控制）。
        // characterId 来自 message.Header.CharacterId（客户端填充），需验证与当前连接绑定一致。
        if (SessionBindingValidator is { IsEnabled: true })
        {
            var connId = CurrentConnectionId;
            if (!string.IsNullOrEmpty(connId) && !SessionBindingValidator.IsCharacterBoundToConnection(connId, characterId))
            {
                Logger.LogWarning(
                    "订阅变更会话绑定校验失败：characterId 与当前连接不一致。CharacterId={CharacterId}, ConnectionId={ConnectionId}",
                    characterId, connId);
                return null;
            }
        }

        if (characterId == 0)
        {
            Logger.LogWarning(
                "HandleSubscriptionUpdate: CharacterId 为 0，无法路由订阅。AddedCount={AddedCount}, RemovedCount={RemovedCount}",
                update.AddedChunks?.Length ?? 0,
                update.RemovedChunks?.Length ?? 0);
            return null;
        }

        var zoneShard = ClusterClient.GetGrain<IZoneShardGrain>(ShardRouter.Resolve(characterId));

        // 位置驱动订阅：客户端填充了位置信息时，只传位置给 grain（24 字节），
        // grain 侧自动计算 AOI 差异。替代传输 ~26000 个 chunk key（~208KB）的双 RPC 路径。
        // 向后兼容：旧客户端不填充位置（HasPosition=false），回退到 chunk 数组路径。
        if (update.HasPosition)
        {
            // 阶段 4.3：跨 Shard 边界双重订阅。
            // 玩家视口（位置 + AOI 半径）覆盖的全部 Shard（含主 Shard）逐个建立/更新 AOI 订阅——
            // 边界 Shard 上 session 无需注册实体（AOI 订阅与实体注册解耦），
            // 实体广播按实体所在 Shard 的 AOI 表扇出，双向跨 Shard 可见无需实体副本迁移。
            // 移出边界的 Shard 调用 RemoveSessionAsync 清理订阅，避免 AOI 表残留。
            var shards = _regionResolver.ResolveShards(
                characterId, update.PositionX, update.PositionY, update.PositionZ, AoiRadiusChunks);
            foreach (var shardId in shards)
            {
                var targetShard = ClusterClient.GetGrain<IZoneShardGrain>(shardId);
                await targetShard.UpdateSessionPositionAsync(characterId, update.PositionX, update.PositionY, update.PositionZ);
            }

            // 移出边界：对本次不再覆盖的 Shard 清理订阅（差量维护）。
            // lock 内收集、lock 外执行（C# 不允许在 lock 语句体内 await）。
            var staleShards = new List<long>();
            lock (SessionState.BoundaryShardLock)
            {
                if (SessionState.BoundaryShardSubscriptions.TryGetValue(characterId, out var previous))
                {
                    foreach (var staleShard in previous)
                    {
                        if (!shards.Contains(staleShard)) staleShards.Add(staleShard);
                    }
                }
                SessionState.BoundaryShardSubscriptions[characterId] = new HashSet<long>(shards);
            }
            foreach (var staleShard in staleShards)
            {
                var stale = ClusterClient.GetGrain<IZoneShardGrain>(staleShard);
                await stale.RemoveSessionAsync(characterId);
                Logger.LogInformation(
                    "HandleSubscriptionUpdate: CharacterId={CharacterId} 移出边界，清理 Shard {ShardId} 的 AOI 订阅",
                    characterId, staleShard);
            }

            Logger.LogInformation(
                "HandleSubscriptionUpdate: CharacterId={CharacterId} 位置驱动订阅更新 pos=({X:F1},{Y:F1},{Z:F1}), shards=[{Shards}]",
                characterId, update.PositionX, update.PositionY, update.PositionZ, string.Join(",", shards));
        }
        else
        {
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
        }

        // 订阅更新无需服务端回复包
        return null;
    }

    /// <summary>
    /// 处理客户端 baseline 重传请求（<see cref="BaselineResyncRequestPacket"/>）。
    /// 转发到 <see cref="IZoneShardGrain.RequestBaselineResyncAsync"/>，强制下一 tick 下发全量快照。无需响应包。
    /// </summary>
    public async Task<SyncPacket?> HandleBaselineResyncAsync(BaselineResyncRequestPacket resync, long characterId)
    {
        try
        {
            var zoneShard = ClusterClient.GetGrain<IZoneShardGrain>(ShardRouter.Resolve(characterId));
            await zoneShard.RequestBaselineResyncAsync((ulong)characterId, resync.ExpectedBaselineTick, resync.ClientLastAppliedTick);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex,
                "转发 baseline 重传请求到 ZoneShard 失败。CharacterId={CharacterId}, ExpectedBaselineTick={ExpectedTick}",
                characterId, resync.ExpectedBaselineTick);
        }
        return null;
    }

    /// <summary>
    /// 处理未知类型的同步包：记录警告并返回空响应，避免静默路由到 <see cref="SyncInputHandler.HandleInputAsync"/>。
    /// </summary>
    /// <param name="packet">未识别的同步包。</param>
    /// <returns>一个空的 <see cref="WorldPatchManifestPacket"/> 作为错误响应。</returns>
    public Task<SyncPacket> HandleUnknownPacketAsync(SyncPacket packet)
    {
        Logger.LogWarning(
            "收到未识别的同步包类型，已拒绝处理。Kind={Kind}, ProtocolVersion={ProtocolVersion}",
            packet.Kind,
            packet.ProtocolVersion);
        return Task.FromResult<SyncPacket>(new WorldPatchManifestPacket());
    }
}

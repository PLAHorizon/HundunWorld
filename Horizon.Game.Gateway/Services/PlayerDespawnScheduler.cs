using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Horizon.Game.Core.Handlers;
using Horizon.Game.Core.Interfaces;
using Horizon.Game.Core.Sim.Server;
using Horizon.Game.Core.World;
using Horizon.Orleans.Interface;
using Horizon.Orleans.Interface.World;
using Orleans.Runtime;
using Orleans.Runtime.Messaging;

namespace Horizon.Game.Gateway.Services
{
    /// <summary>
    /// 玩家断线立即 Despawn 调度器。<br/>
    /// 客户端离线后立即调用 <see cref="IZoneShardGrain.UnregisterEntityAsync"/> 广播 Despawn delta
    /// 给所有在线客户端，并调用 <see cref="IZoneShardGrain.RemoveSessionAsync"/> 清理服务端 AOI 订阅。<br/>
    /// 同时调用 <see cref="ICharacterGrain.GoOfflineAsync"/> 重置角色在线状态（修复 BUG：
    /// 原实现从未调用 GoOfflineAsync，导致 CharacterGrain 持久化状态 IsOnline 永久为 true，
    /// DeactivateOnIdle 也未触发，grain 长期保持激活态，IsOnlineAsync 永远返回 true）。<br/>
    /// 客户端收到 Despawn delta 后由 <c>SnapshotApplySystem.HandleDespawn</c> 销毁 Arch 实体，
    /// <c>FlaxActorSyncSystem.OnEntityDespawned</c> 销毁对应 Flax Actor 及关联资源，
    /// 从而彻底移除该角色的在线信息（角色模型、在线状态、关联动作/状态组件等）。<br/>
    /// 同时清理 Redis 中的所有角色在线持久化点（双轨制 + 指纹）：
    /// <list type="bullet">
    /// <item><c>character:presence:{characterId}</c>（TTL 90s，由 ICharacterPresenceStore 管理）</item>
    /// <item><c>character:fingerprint:{characterId}</c>（TTL 5min，由 ICharacterFingerprintService 管理）</item>
    /// </list>
    /// 修复 BUG：原实现只清理 presence key，未清理 fingerprint key，导致角色离线后 Redis 中
    /// 仍残留 fingerprint key 长达 5 分钟，外部观察"角色在线信息未及时更新"。
    /// </summary>
    public class PlayerDespawnScheduler
    {
        private readonly IClusterClient _clusterClient;
        private readonly IConnectionManager _connectionManager;
        private readonly ICharacterPresenceStore _presenceStore;
        private readonly ICharacterFingerprintService _fingerprintService;
        private readonly PresenceRefreshService? _presenceRefreshService;
        private readonly ILogger<PlayerDespawnScheduler> _logger;
        private readonly IShardRouter _shardRouter;
        private readonly SyncSessionState? _sessionState;

        /// <summary>
        /// DS 数据面（可选）。用于判断"该会话是否由 UE5 DS 承载权威"——
        /// DS 权威的会话，其存活判定必须跳过基于 <c>ZoneShardGrain</c> 的检查，
        /// 否则网关会把实体住在 DS 里的玩家当成幽灵踢下线。
        /// </summary>
        private readonly Horizon.Game.Core.Interfaces.IDSyncDataPlane? _dsDataPlane;

        /// <summary>DS 会话结束通知端（可选）：DS 权威会话断线时告知 DS 回收玩家实体。</summary>
        private readonly Horizon.Game.Core.Sim.Server.IDsSessionNotifier? _dsSessionNotifier;

        /// <summary>characterId → 待执行的 Despawn 取消令牌。</summary>
        private readonly ConcurrentDictionary<long, CancellationTokenSource> _pendingDespawns = new();

        public PlayerDespawnScheduler(
            IClusterClient clusterClient,
            IConnectionManager connectionManager,
            ICharacterPresenceStore presenceStore,
            ICharacterFingerprintService fingerprintService,
            ILogger<PlayerDespawnScheduler> logger,
            IShardRouter? shardRouter = null,
            PresenceRefreshService? presenceRefreshService = null,
            SyncSessionState? sessionState = null,
            Horizon.Game.Core.Interfaces.IDSyncDataPlane? dsDataPlane = null,
            Horizon.Game.Core.Sim.Server.IDsSessionNotifier? dsSessionNotifier = null)
        {
            _clusterClient = clusterClient ?? throw new ArgumentNullException(nameof(clusterClient));
            _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
            _presenceStore = presenceStore ?? throw new ArgumentNullException(nameof(presenceStore));
            _fingerprintService = fingerprintService ?? throw new ArgumentNullException(nameof(fingerprintService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _shardRouter = shardRouter ?? new ZoneBasedShardRouter(1);
            _presenceRefreshService = presenceRefreshService;
            _sessionState = sessionState;
            _dsDataPlane = dsDataPlane;
            _dsSessionNotifier = dsSessionNotifier;

            _logger.LogInformation("PlayerDespawnScheduler 初始化完成，断线立即 Despawn。ShardCount={ShardCount}", _shardRouter.ShardCount);
        }

        /// <summary>
        /// 延迟 Despawn：向 ZoneShardGrain 注销实体并移除 AOI session。<br/>
        /// 同一 characterId 重复调度会取消旧任务（避免重复注销）。<br/>
        /// <br/>
        /// 修复 BUG（远程角色周期性地 Despawn+Spawn 循环 — 闪退）：<br/>
        /// 原实现立即执行 Despawn（<see cref="DespawnImmediatelyAsync"/>），客户端短暂断线后重连时
        /// 实体已被从 <c>_simulatedEntities</c> 移除，<c>EnterWorldAsync</c> 无法找到已存在的实体，<br/>
        /// 走 <c>RegisterEntityAsync</c> 重新 Spawn，导致其他客户端看到远程角色闪退。<br/>
        /// 修复：添加 15 秒宽限期延迟，客户端在宽限期内重连时 <see cref="CancelDespawn"/> 取消待执行的 Despawn，
        /// <c>EnterWorldAsync</c> 发现实体仍存在，跳过 Despawn+Spawn 仅更新 AOI 订阅。<br/>
        /// 宽限期结束后实体才被真正 Despawn，此时若客户端重连则走正常 Spawn 注册路径。
        /// </summary>
        /// <param name="characterId">离线角色的 characterId。</param>
        public void ScheduleDespawn(long characterId)
        {
            // 修复 BUG（ObjectDisposedException at ScheduleDespawn:line 97）：
            // 原实现在 TryAdd 之后才读取 cts.Token，并发的 CancelDespawn(characterId) 可在
            // TryAdd 与 cts.Token 之间移除并 Dispose 该 CTS，导致 cts.Token 抛 ObjectDisposedException。
            // 修复：先创建 CTS 并捕获 Token（值类型拷贝，不再依赖 CTS 实例），
            // 然后 TryAdd。即使 CancelDespawn 随后 Dispose 了 CTS，
            // 已捕获的 token 仍可安全传递给 ExecuteDespawnAsync（CancellationToken 是 struct，
            // 持有的是 CTS 内部状态快照，CTS 被 Dispose 后 token.IsCancellationRequested 仍可读）。
            var cts = new CancellationTokenSource();
            var token = cts.Token;

            // 取消已存在的同 characterId 任务（避免重复注销）
            if (_pendingDespawns.TryRemove(characterId, out var existingCts))
            {
                try { existingCts.Cancel(); } catch { /* 忽略 */ }
                existingCts.Dispose();
            }

            if (!_pendingDespawns.TryAdd(characterId, cts))
            {
                // 极端竞态：并发添加失败，放弃本次调度
                try { cts.Dispose(); } catch { /* 忽略 */ }
                _logger.LogWarning("调度 Despawn 并发冲突: 角色 {CharacterId}", characterId);
                return;
            }

            _logger.LogInformation("角色 {CharacterId} 断线，调度 Despawn（15秒宽限期）", characterId);

            // 后台执行注销（不阻塞调用方）。使用已捕获的 token，避免并发 Dispose 引发的 ObjectDisposedException。
            _ = ExecuteDespawnAsync(characterId, token);
        }

        /// <summary>
        /// 取消挂起的 Despawn 任务。用于客户端重连场景：同一 characterId 在延迟期内重新进入游戏时调用，
        /// 避免角色被误注销导致其他玩家看到角色闪烁。
        /// </summary>
        /// <param name="characterId">重新上线的角色 ID。</param>
        public void CancelDespawn(long characterId)
        {
            if (_pendingDespawns.TryRemove(characterId, out var cts))
            {
                // 修复 BUG（ExecuteDespawnAsync ObjectDisposedException）：
                // 原实现 Cancel() + Dispose()，若 ScheduleDespawn 刚捕获 token 但还未进入
                // Task.Delay(delay, token)，Dispose 会让 Task.Delay 内部的 token.Register 抛
                // ObjectDisposedException（当 Cancel 未真正完成时）。
                // 修复：只 Cancel，不 Dispose。ExecuteDespawnAsync 在退出前会 Dispose。
                // 注意：Cancel() 后 token.IsCancellationRequested=true，Task.Delay 立即抛
                // TaskCanceledException（OperationCanceledException 子类），不会触发 Register。
                try { cts.Cancel(); } catch { /* 忽略 */ }
                _logger.LogInformation(
                    "角色 {CharacterId} 重连，取消 Despawn",
                    characterId);
            }
        }

        /// <summary>
        /// 获取当前挂起的 Despawn 任务数量（诊断/监控用）。
        /// </summary>
        public int PendingCount => _pendingDespawns.Count;

        /// <summary>
        /// 检查指定角色是否有待执行的宽限期 Despawn 任务。
        /// 用于 CharacterPresenceMonitor 判断是否需要绕过宽限期直接 Despawn。
        /// </summary>
        /// <param name="characterId">角色 ID。</param>
        /// <returns>true 表示该角色有正在等待宽限期的 Despawn 任务。</returns>
        public bool HasPendingDespawn(long characterId) => _pendingDespawns.ContainsKey(characterId);

        /// <summary>
        /// 立即同步执行 Despawn（不使用 fire-and-forget）。<br/>
        /// 由 <see cref="Network.GameNetworkServer.CleanupConnectionAsync"/> 在确认连接断开后直接 await 调用，
        /// 确保 <c>UnregisterEntityAsync</c> + <c>RemoveSessionAsync</c> + <c>GoOfflineAsync</c> 全部完成。<br/>
        /// 修复 BUG（两周未解决的核心根因）：<c>ScheduleDespawn</c> 是 fire-and-forget，
        /// <c>ExecuteDespawnAsync</c> 异步执行时可能因二次确认误判、异常吞掉、进程重启等原因从未完成，
        /// 导致 <c>GoOfflineAsync</c> 从未被调用，<c>CharacterGrain</c> 持久化状态 <c>IsOnline</c> 永久卡在 true。
        /// </summary>
        /// <param name="characterId">离线角色的 characterId。</param>
        public async Task DespawnImmediatelyAsync(long characterId)
        {
            // 取消可能存在的 fire-and-forget Despawn 任务，避免重复执行
            CancelDespawn(characterId);

            _logger.LogInformation("角色 {CharacterId} 同步执行 Despawn（CleanupConnection 已确认断线）", characterId);
            await DoDespawnCoreAsync(characterId);
        }

        /// <summary>
        /// 批量续约所有在线角色的实体租约。<br/>
        /// 由 <see cref="GameNetworkServer"/> 的定时器每 20 秒调用一次，
        /// 为所有已注册 characterId 对应的实体续约。<br/>
        /// 超过租约期（默认 90 秒）未续约的实体将被 ZoneShardGrain 自动清理为孤儿实体。
        /// </summary>
        /// <remarks>
        /// 修复 BUG：续约前检查连接是否在线，只续约在线角色的实体。<br/>
        /// 注意：本方法只跳过续约，不清理映射（UnregisterCharacter）。
        /// 映射清理由 CleanupConnectionAsync 负责。之前的实现在这里调用 UnregisterCharacter
        /// 会导致 B 的映射被错误清理（当 B 的连接 IsConnected 暂时为 false 时），
        /// 之后 fanout observer 转发 Despawn delta 时 TryGetEndpoint(B) 找不到 B 的连接，
        /// delta 被静默丢弃，B 看不到 A 的离线。
        /// </remarks>
        public async Task RenewAllLeasesAsync()
        {
            try
            {
                // 从 ConnectionManager 获取所有已注册的 characterId
                var allCharacterIds = _connectionManager.GetAllCharacterIds();
                if (allCharacterIds.Count == 0) return;

                // 检查连接是否在线，只续约在线角色的实体。
                // 不清理映射——映射清理由 CleanupConnectionAsync 负责。
                // 如果连接暂时 IsConnected=false（TCP 瞬时问题），只跳过本次续约，
                // 下次续约时连接恢复会继续续约。
                var validEntityIds = new List<ulong>(allCharacterIds.Count);
                var skippedCount = 0;

                foreach (var characterId in allCharacterIds)
                {
                    var conn = _connectionManager.GetConnectionByCharacterId(characterId);
                    if (conn != null && conn.IsConnected)
                    {
                        validEntityIds.Add((ulong)characterId);
                    }
                    else
                    {
                        skippedCount++;
                    }
                }

                if (validEntityIds.Count == 0)
                {
                    if (skippedCount > 0)
                    {
                        _logger.LogDebug("所有 {Count} 个角色连接均不在线，跳过续约", skippedCount);
                    }
                    return;
                }

                // 阶段 4（多 Shard）：按 shard 分组续约。每个角色解析到自己的 shard（Resolve(characterId)），
                // 同一 shard 的实体批量续约，不同 shard 分别处理、异常相互隔离。
                // 修复 BUG：原实现把全部实体一次性续约到 Resolve(0)（单 Shard 假设），
                // 多 Shard 下其他 shard 的实体租约 90s 后过期被孤儿清理，在线角色从其他客户端消失。
                var groups = validEntityIds
                    .GroupBy(id => _shardRouter.Resolve((long)id))
                    .OrderBy(g => g.Key);
                var renewedTotal = 0;
                foreach (var group in groups)
                {
                    renewedTotal += await RenewLeasesOnShardAsync(group.Key, group.ToArray()).ConfigureAwait(false);
                }

                if (skippedCount > 0)
                {
                    _logger.LogDebug(
                        "已续约 {Renewed}/{Total} 个实体租约（跳过 {Skipped} 个不在线连接，未清理映射）",
                        renewedTotal, validEntityIds.Count, skippedCount);
                }

                // 2026-08：同步续约边界分片的会话订阅租约。边界分片上是"纯订阅会话"（无实体），
                // 实体租约续约覆盖不到——不续约的话，ZoneShardGrain 的订阅租约过期驱逐
                // （SessionSubscriptionLease，5 分钟）会把合法在线玩家的边界订阅误清。
                // 断线/Despawn 后 BoundaryShardSubscriptions 条目被 DoDespawnCoreAsync 移除，
                // 不会为死会话续租。
                await RenewBoundarySessionLeasesAsync(validEntityIds).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "批量续约实体租约失败");
            }
        }

        /// <summary>
        /// 批量续约边界分片会话订阅租约（<see cref="IZoneShardGrain.RenewSessionLeaseAsync"/>）。<br/>
        /// 对每个在线角色，按 <see cref="SyncSessionState.BoundaryShardSubscriptions"/> 记录的
        /// 分片集合逐个续约；单会话/单分片失败仅记 Debug（下个 20s 周期重试），不影响其他续约。
        /// </summary>
        /// <param name="onlineCharacterIds">当前在线（连接 IsConnected）的角色 ID 列表。</param>
        private async Task RenewBoundarySessionLeasesAsync(List<ulong> onlineCharacterIds)
        {
            if (_sessionState == null || onlineCharacterIds.Count == 0) return;

            try
            {
                foreach (var entityId in onlineCharacterIds)
                {
                    var characterId = (long)entityId;

                    HashSet<long>? shards;
                    lock (_sessionState.BoundaryShardLock)
                    {
                        if (!_sessionState.BoundaryShardSubscriptions.TryGetValue(characterId, out shards))
                        {
                            continue;
                        }
                    }

                    if (shards == null || shards.Count == 0) continue;

                    foreach (var shardId in shards)
                    {
                        try
                        {
                            await _clusterClient.GetGrain<IZoneShardGrain>(shardId)
                                .RenewSessionLeaseAsync(characterId)
                                .ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogDebug(ex,
                                "续约边界分片会话租约失败（下个周期重试）。ShardId={ShardId}, CharacterId={CharacterId}",
                                shardId, characterId);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "批量续约边界分片会话租约失败");
            }
        }

        /// <summary>
        /// 在指定 shard 上批量续约实体租约；续约数量不足时查询该 shard 确认缺失实体，
        /// 对确认缺失的实体清理 ConnectionManager 映射并立即 Despawn（修复过期映射残留）。<br/>
        /// 阶段 4（多 Shard）：每个 shard 独立调用本方法，单 shard 的持续失败（超出重试次数）
        /// 只跳过该 shard 本次续约，不影响其他 shard（原单 Shard 实现中总失败会中断整个续约）。
        /// </summary>
        /// <param name="shardId">目标 shard。</param>
        /// <param name="entityIds">该 shard 上的实体 ID 列表。</param>
        /// <returns>续约成功的实体数量。</returns>
        private async Task<int> RenewLeasesOnShardAsync(long shardId, IReadOnlyList<ulong> entityIds)
        {
            var zoneShard = _clusterClient.GetGrain<IZoneShardGrain>(shardId);

            // 修复严重 BUG（在线角色一段时间后从其它客户端离线）：
            // 原实现只调用一次 RenewLeaseAsync，如果 Orleans grain 调用因瞬时网络问题失败，
            // 异常被 catch 并仅记录 Warning，导致租约未续约。连续 4 次失败（80 秒）后
            // 实体租约过期（90 秒），ZoneShardGrain 孤儿清理触发，广播 Despawn 给其他客户端，
            // 角色从其他客户端视野中消失。增加重试机制确保瞬时失败不会导致租约过期。
            int renewed = 0;
            const int maxRetries = 3;
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    renewed = await zoneShard.RenewLeaseAsync(entityIds.ToArray()).ConfigureAwait(false);
                    break; // 成功则退出重试循环
                }
                catch (Exception retryEx) when (attempt < maxRetries)
                {
                    _logger.LogWarning(retryEx,
                        "批量续约实体租约第 {Attempt}/{MaxRetries} 次尝试失败，{DelayMs}ms 后重试。ShardId={ShardId}",
                        attempt, maxRetries, 500 * attempt, shardId);
                    await Task.Delay(500 * attempt).ConfigureAwait(false);
                }
                catch (Exception finalEx)
                {
                    // 多 Shard 下单个 shard 的持续失败不应拖垮其他 shard 的续约，逐 shard 隔离。
                    _logger.LogWarning(finalEx,
                        "批量续约实体租约最终失败，跳过本次续约。ShardId={ShardId}",
                        shardId);
                    return 0;
                }
            }

            // 修复 BUG（ConnectionManager 过期角色映射残留）：
            // 当续约发现实体不在 ZoneShardGrain 中，且该角色有在线连接，说明 ConnectionManager
            // 中保留了过期映射（实体已从 ZoneShard 消失但映射未清理）。这会导致：
            // 1) 续约日志持续显示 0/1，PresenceRefreshService 每 30 秒尝试刷新已不存在的 Redis key
            // 2) 客户端重连循环，每 ~10 秒创建幽灵连接（因为客户端认为角色仍在线但实际上无法进入游戏）
            // 3) 日志噪音污染（Warning 日志每 20 秒刷一次，持续数分钟）
            //
            // 修复方案：当续约数量 < 预期时，查询 ZoneShardGrain 确认哪些实体缺失，
            // 对确认缺失的实体清理 ConnectionManager 映射并触发 Despawn。
            // 注意：不做重新注册（避免位置错乱），只清理过期映射。
            if (renewed < entityIds.Count)
            {
                try
                {
                    // 获取 ZoneShardGrain 中实际注册的所有实体 ID
                    var registeredEntityIds = await zoneShard.GetRegisteredEntityIdsAsync().ConfigureAwait(false);
                    var registeredSet = new HashSet<ulong>(registeredEntityIds);

                    foreach (var entityId in entityIds)
                    {
                        if (!registeredSet.Contains(entityId))
                        {
                            // 实体确认不在 ZoneShardGrain 中，但 ConnectionManager 中仍有映射。
                            // 二次确认：使用 HasEntityAsync 避免并发竞态（GetRegisteredEntityIdsAsync 返回的
                            // 是快照，实体可能在快照后刚被注册）。
                            var stillMissing = !await zoneShard.HasEntityAsync(entityId).ConfigureAwait(false);
                            if (stillMissing)
                            {
                                var characterId = (long)entityId;

                                // ★ DS 权威会话必须跳过本判定（否则玩家会被自己的网关踢掉）。
                                // 实体住在 DS 的 DsWorldSession 中，ZoneShardGrain 本就查不到它——
                                // 这在"DS 通道尚未存在"的年代是不可能的，因此旧代码把"查不到"一律
                                // 解读为幽灵映射。启用 DS 后该解读失效：实测真客户端进世界约 13 秒后
                                // 被此路径关闭连接（日志："实体 27 不在 ZoneShardGrain 中但 ConnectionManager 仍有映射"）。
                                // DS 权威会话的租约与生命周期由 DS 负责，此处不清理、不关连接。
                                if (_dsDataPlane?.IsSessionDsAuthoritative(shardId, characterId) == true)
                                {
                                    _logger.LogDebug(
                                        "实体 {EntityId} 不在 ZoneShardGrain 中，但该会话由 UE5 DS 承载权威（Shard={ShardId}），跳过幽灵映射判定。",
                                        entityId, shardId);
                                    continue;
                                }

                                _logger.LogWarning(
                                    "实体 {EntityId} 不在 ZoneShardGrain 中但 ConnectionManager 仍有映射，关闭客户端连接并立即 Despawn",
                                    entityId);

                                // 修复 BUG（重连死循环：每 20 秒一次的"关闭连接→重连→再关闭"循环）：
                                // 原实现使用 ScheduleDespawn（15 秒宽限期），客户端在宽限期内重连时
                                // StageCharacterMappingAndPresence 会 CancelDespawn + RegisterCharacter，
                                // 但实体已不在 ZoneShardGrain 中，无法恢复，下次续约再次检测到→循环。
                                // 修复：使用 DespawnImmediatelyAsync 立即完成 Despawn（UnregisterEntity +
                                // GoOffline + 清理 Redis + UnregisterCharacter），_pendingDespawns 中
                                // 不会有挂起任务，客户端重连时 CancelDespawn 无效果，必须走完整的
                                // EnterWorld 流程重新 Spawn 实体到 ZoneShardGrain。
                                var conn = _connectionManager.GetConnectionByCharacterId(characterId);
                                if (conn != null)
                                {
                                    _ = conn.CloseAsync("实体已从 ZoneShardGrain 消失，关闭连接");
                                }

                                // 清理 PresenceRefreshService 中的刷新记录
                                _presenceRefreshService?.RemoveCharacter(characterId);

                                // 立即执行 Despawn（不使用宽限期），DoDespawnCoreAsync 的 finally 块
                                // 会清理 ConnectionManager 映射 + Redis presence/fingerprint + GoOfflineAsync。
                                await DespawnImmediatelyAsync(characterId).ConfigureAwait(false);
                            }
                        }
                    }
                }
                catch (Exception detectEx)
                {
                    _logger.LogWarning(detectEx,
                        "查询实体丢失状态失败，跳过清理（下次续约重试）。Renewed={Renewed}/{Expected}, ShardId={ShardId}",
                        renewed, entityIds.Count, shardId);
                }
            }

            return renewed;
        }

        /// <summary>
        /// 宽限期时长：客户端断线后实体保留在 <c>_simulatedEntities</c> 中的时间。
        /// 客户端在此时间内重连可取消 Despawn，避免 Despawn+Spawn 循环（闪退）。
        /// 15 秒足够覆盖网络抖动/场景切换触发的短暂断线，且不会让远程角色长时间"假在线"。
        /// </summary>
        private static readonly TimeSpan DespawnGracePeriod = TimeSpan.FromSeconds(15);

        /// <summary>
        /// 延迟执行 Despawn：先等待宽限期，宽限期内客户端重连则取消 Despawn。<br/>
        /// 宽限期结束后执行 <see cref="DoDespawnCoreAsync"/> 完成实体注销。
        /// </summary>
        private async Task ExecuteDespawnAsync(long characterId, CancellationToken ct)
        {
            try
            {
                // 等待宽限期（客户端重连时 CancelDespawn 会取消此 CancellationTokenSource）
                // OperationCanceledException 由客户端重连触发，是正常取消，非异常行为。
                await Task.Delay(DespawnGracePeriod, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("角色 {CharacterId} 在宽限期内重连，Despawn 已取消", characterId);
                return;
            }

            // 宽限期已过，二次检查：若已被取消则跳过
            if (ct.IsCancellationRequested) return;

            // 从挂起表移除
            _pendingDespawns.TryRemove(characterId, out var finishedCts);
            try { finishedCts?.Dispose(); } catch { /* 忽略 */ }

            // 二次确认：若期间角色已重新注册新连接，则跳过 Despawn
            var currentConn = _connectionManager.GetConnectionByCharacterId(characterId);
            if (currentConn != null && currentConn.IsConnected)
            {
                _logger.LogInformation("角色 {CharacterId} 已重连，跳过 Despawn", characterId);
                return;
            }

            _logger.LogInformation("角色 {CharacterId} 宽限期已过（{Seconds}秒），执行 Despawn", characterId, DespawnGracePeriod.TotalSeconds);
            await DoDespawnCoreAsync(characterId);
        }

        /// <summary>
        /// Despawn 核心逻辑（统一实现）。<br/>
        /// 由 <see cref="DespawnImmediatelyAsync"/> 和 <see cref="ExecuteDespawnAsync"/> 共同调用，
        /// 消除原两条路径的代码重复。<br/>
        /// 执行顺序：
        /// <list type="number">
        ///   <item>UnregisterEntityAsync（广播 Despawn delta）+ RemoveSessionAsync（清理 AOI）</item>
        ///   <item>GoOfflineAsync（重置 CharacterGrain 持久化在线状态，finally 保证执行）</item>
        ///   <item>SetOfflineAsync（清理 Redis presence）</item>
        ///   <item>ReleaseAsync（清理 Redis fingerprint）</item>
        ///   <item>GameServerGrain.PlayerOfflineAsync（兜底清理持久化在线列表）</item>
        ///   <item>UnregisterCharacter（清理 ConnectionManager 角色映射）</item>
        /// </list>
        /// 修复 BUG（Orleans 瞬时不可达时 Despawn 完全失败）：<br/>
        /// 原实现无重试逻辑，当 Silo 短暂不可达（ConnectionFailedException）或重启（OrleansMessageRejectionException）时，
        /// UnregisterEntity/GoOffline/PlayerOffline 全部失败，导致：<br/>
        /// 1) 角色在线状态永久卡在 true（幽灵角色）<br/>
        /// 2) ZoneShardGrain AOI 订阅未清理，GatewaySyncDispatcher 持续向已离线 session 发包（totalDropped 持续增长）<br/>
        /// 修复：对 Orleans grain 调用增加重试（最多 3 次，指数退避 1s→2s→4s），覆盖瞬时网络故障。
        /// </summary>
        private async Task DoDespawnCoreAsync(long characterId)
        {
            // ★ DS 权威会话：必须**主动告知 DS**该玩家已断线。
            // 在此之前网关只清理自己的连接与映射，DS 却始终保留该玩家并持续 20Hz 广播给死会话
            // （实测：网关日志 "Packet dropped: session offline" 刷屏、计数不断增长）。
            // 放在 Despawn 核心（宽限期之后）而非断线瞬间：宽限期内客户端可能重连，
            // 提前通知会让 DS 误以为玩家走了，重连后还要重新握手建实体。
            if (_dsSessionNotifier is not null)
            {
                try
                {
                    var shardIdForNotify = _shardRouter.Resolve(characterId);
                    if (_dsDataPlane?.IsSessionDsAuthoritative(shardIdForNotify, characterId) == true)
                    {
                        _dsSessionNotifier.NotifySessionEnded(shardIdForNotify, new[] { characterId });
                    }
                }
                catch (Exception ex)
                {
                    // 通知失败不能阻断断线清理本身。
                    _logger.LogWarning(ex, "通知 DS 会话结束失败。CharacterId={CharacterId}", characterId);
                }
            }

            var goOfflineCompleted = false;
            try
            {
                var primaryShardId = _shardRouter.Resolve(characterId);
                var zoneShard = _clusterClient.GetGrain<IZoneShardGrain>(primaryShardId);

                // 1) 注销实体并广播 Despawn delta 给所有在线客户端。
                await ExecuteWithRetryAsync(
                    () => zoneShard.UnregisterEntityAsync((ulong)characterId),
                    $"UnregisterEntity({characterId})").ConfigureAwait(false);

                // 2) 移除 AOI session（清理该角色在服务端的订阅与可见性表）。
                //    阶段 4.3：跨 Shard 边界双重订阅下，角色可能同时在相邻 Shard 建有 AOI 订阅，
                //    清理主 Shard + 边界订阅跟踪记录的全部 Shard（精确，避免对无订阅 Shard 调用
                //    RemoveSessionAsync 产生诊断日志噪音）；RemoveSessionAsync 幂等，重复调用无副作用。
                var cleanupShards = new HashSet<long> { primaryShardId };
                if (_sessionState != null)
                {
                    lock (_sessionState.BoundaryShardLock)
                    {
                        if (_sessionState.BoundaryShardSubscriptions.TryGetValue(characterId, out var boundaryShards))
                        {
                            cleanupShards.UnionWith(boundaryShards);
                        }
                        _sessionState.BoundaryShardSubscriptions.Remove(characterId);
                    }
                }
                foreach (var cleanupShardId in cleanupShards)
                {
                    var cleanupShard = _clusterClient.GetGrain<IZoneShardGrain>(cleanupShardId);
                    await ExecuteWithRetryAsync(
                        () => cleanupShard.RemoveSessionAsync(characterId),
                        $"RemoveSession({characterId})@shard{cleanupShardId}").ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "角色 {CharacterId} UnregisterEntity/RemoveSession 失败（仍会执行 GoOfflineAsync）", characterId);
            }
            finally
            {
                // 3) 重置 CharacterGrain 在线状态（finally 保证执行）。
                try
                {
                    var characterGrain = _clusterClient.GetGrain<ICharacterGrain>(characterId);
                    var offlineResult = await ExecuteWithRetryAsync(
                        () => characterGrain.GoOfflineAsync(),
                        $"GoOffline({characterId})").ConfigureAwait(false);
                    goOfflineCompleted = true;
                    if (!offlineResult)
                    {
                        _logger.LogWarning(
                            "角色 {CharacterId} GoOfflineAsync 返回 false（可能角色数据未加载），在线状态可能未重置",
                            characterId);
                    }
                }
                catch (Exception charEx)
                {
                    _logger.LogWarning(charEx,
                        "角色 {CharacterId} GoOfflineAsync 异常（持久化在线状态可能未重置，依赖 OnActivateAsync 兜底重置）",
                        characterId);
                }

                // 4) 兜底：清理 Redis presence + fingerprint。
                try
                {
                    await _presenceStore.SetOfflineAsync(characterId).ConfigureAwait(false);
                }
                catch (Exception presenceEx)
                {
                    _logger.LogWarning(presenceEx,
                        "角色 {CharacterId} 直接清理 Redis presence 失败（依赖 Monitor 兜底）",
                        characterId);
                }

                try
                {
                    await _fingerprintService.ReleaseAsync(characterId).ConfigureAwait(false);
                }
                catch (Exception fpEx)
                {
                    _logger.LogWarning(fpEx,
                        "角色 {CharacterId} 直接清理 Redis fingerprint 失败（依赖 TTL 5min 兜底过期）",
                        characterId);
                }

                // 5) 兜底：更新 GameServerGrain 持久化在线角色列表。
                try
                {
                    var gameServerGrain = _clusterClient.GetGrain<IGameServerGrain>(1L);
                    await ExecuteWithRetryAsync(
                        () => gameServerGrain.PlayerOfflineAsync(characterId),
                        $"PlayerOffline({characterId})").ConfigureAwait(false);
                }
                catch (Exception gameServerEx)
                {
                    _logger.LogWarning(gameServerEx,
                        "角色 {CharacterId} 兜底调用 GameServerGrain.PlayerOfflineAsync 失败（依赖 Monitor 兜底）",
                        characterId);
                }

                // 6) 清理 ConnectionManager 中的角色映射。
                try
                {
                    _connectionManager.UnregisterCharacter(characterId);
                }
                catch (Exception connEx)
                {
                    _logger.LogWarning(connEx,
                        "角色 {CharacterId} 清理 ConnectionManager 角色映射失败（依赖 60s 空闲超时兜底）",
                        characterId);
                }
            }

            _logger.LogInformation(
                "角色 {CharacterId} Despawn 完成（goOfflineCompleted={GoOfflineCompleted}）",
                characterId, goOfflineCompleted);
        }

        /// <summary>
        /// 对 Orleans grain 调用执行带重试的包装。<br/>
        /// 修复 BUG（Orleans 瞬时不可达时 Despawn 完全失败）：<br/>
        /// 当 Silo 短暂不可达（ConnectionFailedException）或正在重启（OrleansMessageRejectionException）时，
        /// grain 调用会抛出瞬时异常。原实现无重试，导致 Despawn 链路全部失败，
        /// 角色在线状态永久卡在 true，AOI 订阅未清理，GatewaySyncDispatcher 持续丢包。<br/>
        /// 重试策略：最多 3 次，指数退避（1s → 2s → 4s）。
        /// </summary>
        private async Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> action, string context)
        {
            const int maxRetries = 3;
            var retryDelay = TimeSpan.FromSeconds(1);

            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    return await action().ConfigureAwait(false);
                }
                catch (Exception ex) when (attempt < maxRetries && IsTransientOrleansException(ex))
                {
                    _logger.LogWarning(
                        "Despawn grain 调用失败（Silo 可能正在重启/不可达），{Delay}s 后重试。Context={Context}, Attempt={Attempt}/{Max}, Error={Error}",
                        retryDelay.TotalSeconds, context, attempt + 1, maxRetries, ex.Message);
                    await Task.Delay(retryDelay).ConfigureAwait(false);
                    retryDelay = TimeSpan.FromTicks(retryDelay.Ticks * 2);
                }
            }
        }

        /// <summary>无返回值的重载。</summary>
        private async Task ExecuteWithRetryAsync(Func<Task> action, string context)
        {
            await ExecuteWithRetryAsync(async () => { await action().ConfigureAwait(false); return true; }, context).ConfigureAwait(false);
        }

        /// <summary>
        /// 判断异常是否为 Orleans 瞬时故障（可重试）。
        /// 覆盖：Silo 重启（OrleansMessageRejectionException）、Silo 不可达（ConnectionFailedException）、
        /// 以及它们的内部异常包装形式。
        /// </summary>
        private static bool IsTransientOrleansException(Exception ex)
        {
            if (ex is OrleansMessageRejectionException) return true;
            if (ex is ConnectionFailedException) return true;
            // 检查内部异常（Orleans 有时将瞬时故障包装在 OrleansException 中）
            if (ex is OrleansException && ex.InnerException is not null)
                return IsTransientOrleansException(ex.InnerException);
            return false;
        }
    }
}

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Horizon.Game.Core.Interfaces;
using Horizon.Game.Core.Sim.Server;
using Horizon.Orleans.Interface;
using Horizon.Orleans.Interface.World;

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
        private readonly ILogger<PlayerDespawnScheduler> _logger;

        /// <summary>characterId → 待执行的 Despawn 取消令牌。</summary>
        private readonly ConcurrentDictionary<long, CancellationTokenSource> _pendingDespawns = new();

        /// <summary>ZoneShard 默认分片 ID（与 SyncDispatcherHostedService.DefaultShardId 一致）。</summary>
        private const long DefaultShardId = 0;

        public PlayerDespawnScheduler(
            IClusterClient clusterClient,
            IConnectionManager connectionManager,
            ICharacterPresenceStore presenceStore,
            ICharacterFingerprintService fingerprintService,
            ILogger<PlayerDespawnScheduler> logger)
        {
            _clusterClient = clusterClient ?? throw new ArgumentNullException(nameof(clusterClient));
            _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
            _presenceStore = presenceStore ?? throw new ArgumentNullException(nameof(presenceStore));
            _fingerprintService = fingerprintService ?? throw new ArgumentNullException(nameof(fingerprintService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _logger.LogInformation("PlayerDespawnScheduler 初始化完成，断线立即 Despawn。");
        }

        /// <summary>
        /// 立即 Despawn：向 ZoneShardGrain 注销实体并移除 AOI session。<br/>
        /// 同一 characterId 重复调度会取消旧任务（避免重复注销）。
        /// </summary>
        /// <param name="characterId">离线角色的 characterId。</param>
        public void ScheduleDespawn(long characterId)
        {
            // 取消已存在的同 characterId 任务（避免重复注销）
            if (_pendingDespawns.TryRemove(characterId, out var existingCts))
            {
                try { existingCts.Cancel(); } catch { /* 忽略 */ }
                existingCts.Dispose();
            }

            var cts = new CancellationTokenSource();
            if (!_pendingDespawns.TryAdd(characterId, cts))
            {
                // 极端竞态：并发添加失败，放弃本次调度
                try { cts.Dispose(); } catch { /* 忽略 */ }
                _logger.LogWarning("调度 Despawn 并发冲突: 角色 {CharacterId}", characterId);
                return;
            }

            _logger.LogInformation("角色 {CharacterId} 断线，立即广播 Despawn", characterId);

            // 后台执行注销（不阻塞调用方）
            _ = ExecuteDespawnAsync(characterId, cts.Token);
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
                try { cts.Cancel(); } catch { /* 忽略 */ }
                cts.Dispose();
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

            // 修复 BUG（角色离线后持久化在线信息未更新）：
            // GoOfflineAsync 必须被立即调用并持久化 IsOnline=false，否则 CharacterGrain 的
            // [PersistentState] 状态会永久卡在 true，导致角色离线信息无法从服务端移除。
            // 原实现把 GoOfflineAsync 放在 try 块末尾，若 UnregisterEntityAsync/RemoveSessionAsync
            // 抛异常，GoOfflineAsync 会被跳过。现改为 finally 块确保一定执行。
            var goOfflineCompleted = false;
            try
            {
                var zoneShard = _clusterClient.GetGrain<IZoneShardGrain>(DefaultShardId);

                // 1) 注销实体并广播 Despawn delta 给所有在线客户端。
                //    UnregisterEntityAsync 内部有兜底逻辑：广播失败时保留实体并重试，10 次后强制移除。
                await zoneShard.UnregisterEntityAsync((ulong)characterId).ConfigureAwait(false);

                // 2) 移除 AOI session（清理该角色在服务端的订阅与可见性表）
                await zoneShard.RemoveSessionAsync(characterId).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // 实体注销/AOI 清理失败不阻断 GoOfflineAsync（持久化在线状态是更高优先级）
                _logger.LogError(ex, "角色 {CharacterId} UnregisterEntity/RemoveSession 失败（仍会执行 GoOfflineAsync）", characterId);
            }
            finally
            {
                // 3) 重置 CharacterGrain 在线状态（核心修复：GoOfflineAsync 必须被调用，
                //    否则 IsOnline 永久持久化为 true，grain 永不 DeactivateOnIdle）。
                //    放在 finally 块中：即使上面的步骤抛异常，GoOfflineAsync 也一定执行。
                try
                {
                    var characterGrain = _clusterClient.GetGrain<ICharacterGrain>(characterId);
                    var offlineResult = await characterGrain.GoOfflineAsync().ConfigureAwait(false);
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

                // 4) 兜底：直接清理 Redis 中所有角色在线持久化点（双轨制架构）。
                //    GoOfflineAsync 内部已调用 SetOfflineAsync，但若 grain 调用失败或超时，
                //    presence/fingerprint key 可能未被清理。这里直接调用确保一定被清除，
                //    避免角色在线状态残留导致 IsOnlineAsync 误返回 true。
                //    CharacterPresenceMonitorHostedService 也会在 90 秒后兜底清理过期 presence。
                //
                //    修复 BUG（角色离线后 Redis 中仍看到在线信息）：
                //    原实现只清理 character:presence:{id}（TTL 90s），未清理 character:fingerprint:{id}（TTL 5min）。
                //    fingerprint key 残留 5 分钟，外部观察"角色在线信息未及时更新"。
                //    现在按 characterId 直接调用 ReleaseAsync，不依赖 connectionId 反查 Set，
                //    确保 fingerprint key 被立即删除。
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

                // 5) 兜底：直接更新 GameServerGrain 持久化在线角色列表（修复严重 BUG 的核心兜底点）。
                //    GoOfflineAsync 内部已调用 PlayerOfflineAsync，但若 grain 调用失败、超时、
                //    或 CharacterGrain 因 State 异常提前 return false 而未真正调用 PlayerOfflineAsync，
                //    持久化在线列表 OnlinePlayers 仍会残留该 characterId，导致角色离线后持久化
                //    在线信息未被更新、角色永久残留服务端。这里直接调用 GameServerGrain 双保险。
                //    默认服务器 ID = 1；调用失败不影响其他清理流程。
                try
                {
                    var gameServerGrain = _clusterClient.GetGrain<IGameServerGrain>(1L);
                    await gameServerGrain.PlayerOfflineAsync(characterId).ConfigureAwait(false);
                }
                catch (Exception gameServerEx)
                {
                    _logger.LogWarning(gameServerEx,
                        "角色 {CharacterId} 兜底调用 GameServerGrain.PlayerOfflineAsync 失败（依赖 Monitor 兜底）",
                        characterId);
                }

                // 6) 清理 ConnectionManager 中的角色映射，消除僵尸映射残留窗口。
                //    修复 BUG：DespawnImmediatelyAsync 不清理 _characterConnections 映射，
                //    会导致 GetConnectionByCharacterId 仍返回旧 conn（即使角色已下线）。
                //    CharacterPresenceMonitor 的二次确认（conn==null || !conn.IsConnected）
                //    会误判为"连接仍在线"，导致计数器持续累计或重复 Despawn。
                //    虽然 GameNetworkServer 的 60 秒空闲超时会兜底清理连接，但此期间
                //    Monitor 会持续误判。这里主动调用 UnregisterCharacter 清理映射，
                //    确保 ConnectionManager 状态与 CharacterGrain/Redis 一致。
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

                var zoneShard = _clusterClient.GetGrain<IZoneShardGrain>(DefaultShardId);
                var renewed = await zoneShard.RenewLeaseAsync(validEntityIds.ToArray()).ConfigureAwait(false);

                if (skippedCount > 0)
                {
                    _logger.LogDebug(
                        "已续约 {Renewed}/{Total} 个实体租约（跳过 {Skipped} 个不在线连接，未清理映射）",
                        renewed, validEntityIds.Count, skippedCount);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "批量续约实体租约失败");
            }
        }

        /// <summary>
        /// 立即执行 Despawn：调用 ZoneShardGrain 注销实体并移除 AOI session。
        /// </summary>
        private async Task ExecuteDespawnAsync(long characterId, CancellationToken ct)
        {
            // 检查是否已被取消（重连场景）
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

            try
            {
                var zoneShard = _clusterClient.GetGrain<IZoneShardGrain>(DefaultShardId);

                // 1) 注销实体并广播 Despawn delta 给所有在线客户端。
                await zoneShard.UnregisterEntityAsync((ulong)characterId).ConfigureAwait(false);

                // 2) 移除 AOI session（清理该角色在服务端的订阅与可见性表）
                await zoneShard.RemoveSessionAsync(characterId).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // 实体注销/AOI 清理失败不阻断 GoOfflineAsync（持久化在线状态是更高优先级）
                _logger.LogError(ex, "角色 {CharacterId} UnregisterEntity/RemoveSession 失败（仍会执行 GoOfflineAsync）", characterId);
            }
            finally
            {
                // 3) 重置 CharacterGrain 在线状态（修复 BUG：GoOfflineAsync 必须被调用，
                //    否则 IsOnline 永久持久化为 true，grain 永不 DeactivateOnIdle，
                //    IsOnlineAsync 永远返回 true —— 即"在线信息不能被准确移除"的根因）。
                //    放在 finally 块中：即使上面的步骤抛异常，GoOfflineAsync 也一定执行。
                try
                {
                    var characterGrain = _clusterClient.GetGrain<ICharacterGrain>(characterId);
                    var offlineResult = await characterGrain.GoOfflineAsync().ConfigureAwait(false);
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

                // 4) 兜底：直接清理 Redis 中所有角色在线持久化点（与 DespawnImmediatelyAsync 一致）
                //    修复 BUG：原实现只清理 character:presence:{id}，未清理 character:fingerprint:{id}。
                //    fingerprint key TTL 5 分钟，远长于 presence 的 90 秒，离线后会残留导致
                //    外部观察"角色在线信息未及时更新"。现在按 characterId 直接 ReleaseAsync。
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

                // 5) 兜底：直接更新 GameServerGrain 持久化在线角色列表（与 DespawnImmediatelyAsync 一致）。
                //    即使 GoOfflineAsync 调用失败、超时或返回 false，也确保持久化 OnlinePlayers 列表
                //    中该 characterId 被移除，避免角色离线后持久化在线信息未更新、角色永久残留服务端。
                try
                {
                    var gameServerGrain = _clusterClient.GetGrain<IGameServerGrain>(1L);
                    await gameServerGrain.PlayerOfflineAsync(characterId).ConfigureAwait(false);
                }
                catch (Exception gameServerEx)
                {
                    _logger.LogWarning(gameServerEx,
                        "角色 {CharacterId} 兜底调用 GameServerGrain.PlayerOfflineAsync 失败（依赖 Monitor 兜底）",
                        characterId);
                }

                // 6) 清理 ConnectionManager 中的角色映射（与 DespawnImmediatelyAsync 一致）。
                //    消除僵尸映射残留窗口，避免 CharacterPresenceMonitor 二次确认误判。
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

            _logger.LogInformation("角色 {CharacterId} Despawn 完成，已广播离线并重置在线状态", characterId);
        }
    }
}

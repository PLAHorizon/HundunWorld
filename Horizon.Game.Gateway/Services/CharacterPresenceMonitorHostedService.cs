using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Horizon.Game.Core.Sim.Server;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Horizon.Game.Gateway.Services
{
    /// <summary>
    /// 角色在线状态监控后台服务（双轨制架构的修复兜底）。<br/>
    /// 每 10 秒扫描 Redis presence 中 TTL 过低的 key（TTL &lt; 30 秒），
    /// 对每个角色进行二次确认：<br/>
    /// - 若 <see cref="IConnectionManager"/> 中连接仍在线 → 刷新 presence（修复），跳过 Despawn<br/>
    /// - 若连接已断开 → 调用 <see cref="PlayerDespawnScheduler.DespawnImmediatelyAsync"/> 触发 Despawn<br/>
    /// <para>
    /// <b>设计目的</b>：作为 Redis presence 的修复机制，而非断线检测机制。
    /// 断线检测由 <see cref="GameNetworkServer.CheckDisconnectedConnections"/>（每 5 秒，
    /// TCP Online 判定 + 空闲超时 60 秒）和租约续约机制（每 20 秒，90 秒租约）负责。<br/>
    /// 本服务仅在 Redis 异常导致 presence TTL 下降时修复 presence，避免误判。
    /// </para>
    /// <para>
    /// <b>降级策略</b>：Redis 不可用时 <see cref="ICharacterPresenceStore.GetExpiredCharactersAsync"/>
    /// 返回空列表，服务空转，不影响主业务。
    /// </para>
    /// <para>
    /// <b>版本历史</b>：此版本恢复为简单修复机制（参考 2269294 版本的稳定实现）。
    /// 之前的复杂版本（阶段 1/阶段 2、_staleHeartbeatCounts 计数器）在修复"网关运行时
    /// 离线角色无法正常离线"BUG 时引入了新问题：对正常在线角色的误判 Despawn。
    /// 该 BUG 的根因已在 <see cref="RedisCharacterPresenceStore.GetExpiredCharactersAsync"/>
    /// 中通过 TTL 检测（而非 lastHeartbeat 比较）解决，Monitor 不需要复杂逻辑。
    /// </para>
    /// </summary>
    public class CharacterPresenceMonitorHostedService : BackgroundService
    {
        /// <summary>扫描间隔（秒）。默认 10 秒，平衡清理及时性与 Redis 负载。</summary>
        private const int ScanIntervalSeconds = 10;

        /// <summary>心跳超时阈值。与 Redis presence TTL（90 秒）保持一致。</summary>
        private static readonly TimeSpan HeartbeatTimeout = TimeSpan.FromSeconds(90);

        private readonly ICharacterPresenceStore _presenceStore;
        private readonly PlayerDespawnScheduler _despawnScheduler;
        private readonly IConnectionManager _connectionManager;
        private readonly ILogger<CharacterPresenceMonitorHostedService> _logger;

        public CharacterPresenceMonitorHostedService(
            ICharacterPresenceStore presenceStore,
            PlayerDespawnScheduler despawnScheduler,
            IConnectionManager connectionManager,
            ILogger<CharacterPresenceMonitorHostedService> logger)
        {
            _presenceStore = presenceStore ?? throw new ArgumentNullException(nameof(presenceStore));
            _despawnScheduler = despawnScheduler ?? throw new ArgumentNullException(nameof(despawnScheduler));
            _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation(
                "CharacterPresenceMonitorHostedService 启动（修复模式），扫描间隔 {Interval}s，心跳超时 {Timeout}s",
                ScanIntervalSeconds, HeartbeatTimeout.TotalSeconds);

            // 启动延迟，避免与 Silo 初始化竞争
            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken).ConfigureAwait(false);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ScanAndCleanupExpiredAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "扫描过期 presence 时发生未预期异常，将继续重试");
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(ScanIntervalSeconds), stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }

            _logger.LogInformation("CharacterPresenceMonitorHostedService 已停止");
        }

        /// <summary>
        /// 扫描过期 presence 并修复/清理。<br/>
        /// 对每个 TTL 过低的角色进行二次确认后决定修复还是 Despawn。
        /// </summary>
        /// <remarks>
        /// <b>核心设计原则（参考 2269294 稳定版本）</b>：<br/>
        /// 本服务是 <b>修复机制</b>，不是检测机制。断线检测由 CheckDisconnectedConnections
        /// （每 5 秒，TCP Online + 空闲超时 60 秒）负责。<br/>
        /// - 若连接仍在线（conn.IsConnected=true）→ 刷新 presence（修复），跳过 Despawn<br/>
        /// - 若连接已断开（conn==null 或 !conn.IsConnected）→ 触发 Despawn<br/>
        /// 僵尸连接（TCP 半关闭导致 conn.IsConnected=true 但客户端实际已断线）由
        /// CheckDisconnectedConnections 的空闲超时检测（LastActiveTime > 60 秒）兜底清理。
        /// </remarks>
        private async Task ScanAndCleanupExpiredAsync(CancellationToken stoppingToken)
        {
            var expiredCharacters = await _presenceStore.GetExpiredCharactersAsync(HeartbeatTimeout).ConfigureAwait(false);

            if (expiredCharacters.Count == 0) return;

            _logger.LogWarning("检测到 {Count} 个 TTL 过低的角色，开始修复/清理", expiredCharacters.Count);

            foreach (var (characterId, lastHeartbeat) in expiredCharacters)
            {
                if (stoppingToken.IsCancellationRequested) break;

                try
                {
                    var conn = _connectionManager.GetConnectionByCharacterId(characterId);
                    if (conn != null && conn.IsConnected)
                    {
                        // 连接仍在线 → 修复 presence（刷新 TTL + lastHeartbeat）
                        // 不触发 Despawn：真正的断线检测由 CheckDisconnectedConnections 负责
                        _logger.LogWarning(
                            "角色 {CharacterId} presence TTL 过低但连接仍在线，刷新 presence 并跳过 Despawn（lastHeartbeat={LastHeartbeat}）",
                            characterId, lastHeartbeat.ToString("yyyy-MM-dd HH:mm:ss"));
                        await _presenceStore.RefreshHeartbeatAsync(characterId).ConfigureAwait(false);
                        continue;
                    }

                    // 连接已断开 → 触发 Despawn
                    var offlineSince = DateTime.UtcNow - lastHeartbeat;
                    _logger.LogWarning(
                        "角色 {CharacterId} presence TTL 过低且连接已断开（{Seconds:F1}s），触发 DespawnImmediatelyAsync",
                        characterId, offlineSince.TotalSeconds);

                    await _despawnScheduler.DespawnImmediatelyAsync(characterId).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "修复/清理角色 {CharacterId} 时发生异常，将继续处理下一个", characterId);
                }
            }
        }
    }
}

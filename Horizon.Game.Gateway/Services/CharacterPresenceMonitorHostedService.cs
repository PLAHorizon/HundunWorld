using System;
using System.Threading;
using System.Threading.Tasks;
using Horizon.Game.Core.Sim.Server;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Horizon.Game.Gateway.Services
{
    /// <summary>
    /// 角色在线状态监控后台服务（双轨制架构的清理兜底）。<br/>
    /// 每 10 秒扫描 Redis presence 中过期的心跳 key（超过 90 秒未心跳），
    /// 对每个过期角色二次确认 <see cref="IConnectionManager"/> 中连接确实已断开后，
    /// 调用 <see cref="PlayerDespawnScheduler.DespawnImmediatelyAsync"/> 触发 Despawn + GoOfflineAsync。<br/>
    /// <para>
    /// <b>设计目的</b>：弥补 ConnectionManager 的 KeepAlive 在极端场景下未能及时触发清理的缺陷，
    /// 例如：<br/>
    /// - 服务器进程崩溃后重启，原连接的 KeepAlive 已失效，但 Redis presence key 因 TTL 未过期仍残留<br/>
    /// - 网络抖动导致 KeepAlive 误判，但客户端实际已断线<br/>
    /// - GameNetworkServer.CleanupConnectionAsync 异常未完成 Despawn<br/>
    /// 该服务作为最终兜底，确保过期的在线状态一定被清理。
    /// </para>
    /// <para>
    /// <b>降级策略</b>：Redis 不可用时 <see cref="ICharacterPresenceStore.GetExpiredCharactersAsync"/>
    /// 返回空列表，服务空转，不影响主业务。ConnectionManager 的 KeepAlive 仍作为主清理机制。
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
                "CharacterPresenceMonitorHostedService 启动，扫描间隔 {Interval}s，心跳超时 {Timeout}s",
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
                    // 正常关闭
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
        /// 扫描过期 presence 并清理。<br/>
        /// 对每个过期角色进行二次确认（ConnectionManager 中连接确实不在线）后触发 Despawn。
        /// </summary>
        private async Task ScanAndCleanupExpiredAsync(CancellationToken stoppingToken)
        {
            var expiredCharacters = await _presenceStore.GetExpiredCharactersAsync(HeartbeatTimeout).ConfigureAwait(false);
            if (expiredCharacters.Count == 0) return;

            _logger.LogWarning("检测到 {Count} 个心跳过期的角色，开始清理", expiredCharacters.Count);

            foreach (var (characterId, lastHeartbeat) in expiredCharacters)
            {
                if (stoppingToken.IsCancellationRequested) break;

                try
                {
                    // 二次确认：ConnectionManager 中该角色的连接是否真的不在线。
                    // 防止误清理"心跳过期但连接实际仍活跃"的角色（例如心跳延迟但 TCP 未断）。
                    var conn = _connectionManager.GetConnectionByCharacterId(characterId);
                    if (conn != null && conn.IsConnected)
                    {
                        // 连接仍在线，但 presence key 过期 —— 可能是心跳处理逻辑异常，刷新 presence 并跳过 Despawn
                        _logger.LogWarning(
                            "角色 {CharacterId} presence 过期但 ConnectionManager 显示仍在线，刷新 presence 并跳过 Despawn（lastHeartbeat={LastHeartbeat}）",
                            characterId, lastHeartbeat.ToString("yyyy-MM-dd HH:mm:ss"));
                        await _presenceStore.RefreshHeartbeatAsync(characterId).ConfigureAwait(false);
                        continue;
                    }

                    var offlineSince = DateTime.UtcNow - lastHeartbeat;
                    _logger.LogWarning(
                        "角色 {CharacterId} 心跳过期 {Seconds:F1}s，触发 DespawnImmediatelyAsync",
                        characterId, offlineSince.TotalSeconds);

                    // 同步执行 Despawn（内部会调用 GoOfflineAsync 清理 Redis presence + grain 状态）
                    await _despawnScheduler.DespawnImmediatelyAsync(characterId).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "清理过期角色 {CharacterId} 时发生异常，将继续处理下一个", characterId);
                }
            }
        }
    }
}

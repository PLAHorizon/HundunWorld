using Horizon.Game.Core.Interfaces;
using Horizon.Game.Core.Sim.Server;
using Horizon.Game.Core.World;
using Microsoft.Extensions.Logging;

namespace Horizon.Game.Gateway.Services
{
    /// <summary>
    /// 把击杀经验落库后的权威进度（等级 + 累计经验）推回该角色所在的 DS。
    /// <para>为什么要往返这一趟：客户端经验条读的是 DS 玩家快照里的 <c>Exp</c>，
    /// 而绝对值只有 CharacterGrain 知道（DS 只发了本场击杀的增量）。
    /// 推送失败不阻断经验入账——写入已经成功，这里只是让屏幕跟上。</para>
    /// </summary>
    public sealed class DsPlayerProgressNotifier : IPlayerProgressNotifier
    {
        private readonly IShardRouter _shardRouter;
        private readonly System.Func<DsUplinkServer> _uplinkFactory;
        private DsUplinkServer? _uplink;
        private readonly IDSyncDataPlane? _dataPlane;
        private readonly ILogger<DsPlayerProgressNotifier> _logger;

        /// <param name="uplinkFactory">
        /// 延迟解析上行通道：本类与 DsUplinkServer 之间隔着
        /// DsUplinkServer → DsControlPlane → IDsGameplayEventSink → 本类 的环。工厂内的
        /// GetService 不受容器环检测保护，构造期直接注入会让宿主启动时在 StackGuard 上互等死锁。
        /// </param>
        public DsPlayerProgressNotifier(
            IShardRouter shardRouter,
            System.Func<DsUplinkServer> uplinkFactory,
            ILogger<DsPlayerProgressNotifier> logger,
            IDSyncDataPlane? dataPlane = null)
        {
            _shardRouter = shardRouter ?? throw new System.ArgumentNullException(nameof(shardRouter));
            _uplinkFactory = uplinkFactory ?? throw new System.ArgumentNullException(nameof(uplinkFactory));
            _logger = logger ?? throw new System.ArgumentNullException(nameof(logger));
            _dataPlane = dataPlane;
        }

        private DsUplinkServer Uplink => _uplink ??= _uplinkFactory();

        public void Notify(long characterId, int level, long exp)
        {
            if (characterId <= 0) return;
            try
            {
                var shardId = _shardRouter.Resolve(characterId);
                // 只在"这个角色确实由 DS 权威"时才推：非 DS 会话没有玩家快照可下发。
                if (_dataPlane is not null && !_dataPlane.IsSessionDsAuthoritative(shardId, characterId))
                    return;

                if (!Uplink.NotifyPlayerProgress(shardId, characterId, level, exp))
                    _logger.LogDebug("玩家进度未送达 DS（无可用 DS/未连接）。CharacterId={CharacterId}", characterId);
            }
            catch (System.Exception ex)
            {
                _logger.LogWarning(ex, "推送玩家进度失败。CharacterId={CharacterId}", characterId);
            }
        }
    }
}

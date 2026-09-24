using FlaxEngine;

namespace HundunWorld.Game.Navigation
{
    /// <summary>
    /// POI 发现驱动：玩家进入某个 POI 的发现半径即把它标记为「已发现」。
    /// <para>
    /// <b>为什么不用 POI 自带的触发体积</b>：本项目玩家角色由 <c>CharacterRoot.prefab</c> 生成，
    /// 该 Prefab 与 <c>HundunWorldGame.CreateLocalPlayerActor</c> 都不给玩家挂碰撞体，
    /// 而 PhysX 的触发器重叠事件要求至少一方是动态刚体 —— 因此 <c>POIActor</c> 的
    /// <c>DiscoveryCollider.TriggerEnter</c> 在当前角色结构下不会触发。
    /// 距离判定是当前唯一可靠的发现途径；场景里的 <c>POIActor</c> 触发器体积保留，
    /// 待玩家角色获得碰撞体后可直接切回触发式发现。
    /// </para>
    /// </summary>
    public static class MapDiscoveryDriver
    {
        /// <summary>检测间隔（秒）。发现是低频事件，无需每帧遍历 POI。</summary>
        private const float TickInterval = 0.5f;

        /// <summary>POI 未配置发现半径时的回退值（世界单位）</summary>
        private const float DefaultDiscoveryRadius = 800f;

        private static float _timer;

        /// <summary>
        /// 推进发现检测。由 <c>HundunWorldGame.OnUpdate</c> 每帧调用（内部自行限频）。
        /// </summary>
        /// <param name="deltaTime">帧间隔</param>
        /// <param name="player">本地玩家 Actor</param>
        public static void Tick(float deltaTime, Actor player)
        {
            _timer += deltaTime;
            if (_timer < TickInterval) return;
            _timer = 0f;

            if (player == null) return;

            var navigation = NarrativePro.Navigation.NavigationSubsystem.Instance;
            var pois = navigation?.GetAllPOIs();
            if (pois == null || pois.Count == 0) return;

            var navigationComponent = FindNavigationComponent(player);
            if (navigationComponent == null) return;

            Vector3 playerPosition = player.Position;

            for (int i = 0; i < pois.Count; i++)
            {
                var poi = pois[i];
                if (poi == null || !poi.POITag.IsValid()) continue;
                if (!poi.bIsDiscoverable) continue;
                if (navigationComponent.HasDiscoveredPOI(poi.POITag)) continue;

                float radius = poi.DiscoveryRadius > 0f ? poi.DiscoveryRadius : DefaultDiscoveryRadius;
                if (NarrativePro.Navigation.NavigatorStatics.DistanceSquared2D(playerPosition, poi.POILocation) > radius * radius)
                    continue;

                navigationComponent.DiscoverPOI(poi.POITag);
                Debug.Log($"[MapDiscovery] 已发现 POI: {poi.POITag}（{poi.POIDisplayName}）");
            }
        }

        /// <summary>复位的运行时状态（场景切换/重新登录时调用，避免沿用上一次会话的计时）。</summary>
        public static void Reset()
        {
            _timer = 0f;
        }

        private static NarrativePro.Navigation.NarrativeNavigationComponent FindNavigationComponent(Actor player)
        {
            return player.GetScript<NarrativePro.Navigation.NarrativeNavigationComponent>()
                   ?? Level.FindScript<NarrativePro.Navigation.NarrativeNavigationComponent>();
        }
    }
}

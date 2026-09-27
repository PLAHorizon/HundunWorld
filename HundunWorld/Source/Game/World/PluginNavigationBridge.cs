using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Items;
using NarrativePro.Navigation;

namespace HundunWorld.Game.Navigation
{
    /// <summary>
    /// 地图标记在 UI 层的只读视图（插件数据 → 画面可用形态）。
    /// </summary>
    public struct MapMarkerView
    {
        /// <summary>POI 标签</summary>
        public string Tag;

        /// <summary>显示名</summary>
        public string Title;

        /// <summary>副标题</summary>
        public string Subtitle;

        /// <summary>世界位置</summary>
        public Vector3 WorldPosition;

        /// <summary>归一化地图坐标（0~1，左上为原点）</summary>
        public Float2 Normalized;

        /// <summary>是否支持快速旅行（对应地图上的传送点）</summary>
        public bool SupportsFastTravel;

        /// <summary>是否为「需探索才能发现」的 POI</summary>
        public bool IsDiscoverable;

        /// <summary>玩家是否已发现。非可发现 POI 恒为 true（始终可见）。</summary>
        public bool Discovered;
    }

    /// <summary>
    /// 把 NarrativePro 插件的导航数据（POI / 地图瓦片边界 / 快速旅行）适配为游戏 UI 可用的只读视图。
    /// <para>
    /// 所有方法在「插件导航不可用」（场景无 <see cref="MapTileBoundsActor"/>、无导航组件、坐标为默认值）时返回 false，
    /// 调用方必须回退到内置演示数据，以保证现有 UI 观感不被破坏。
    /// </para>
    /// </summary>
    public static class PluginNavigationBridge
    {
        /// <summary>查找场景中的地图瓦片边界 Actor。</summary>
        public static MapTileBoundsActor FindMapTileBounds()
        {
            return NavigationSubsystem.Instance?.MapTileBounds ?? Level.FindScript<MapTileBoundsActor>();
        }

        /// <summary>查找场景中的导航组件（承载 POI 发现状态与快速旅行）。</summary>
        public static NarrativeNavigationComponent FindNavigationComponent()
        {
            return Level.FindScript<NarrativeNavigationComponent>();
        }

        /// <summary>取地图投影参数（中心 + 世界宽度）。地图未配置时返回 false。</summary>
        public static bool TryGetMapProjection(out Float2 origin, out float mapWidth)
        {
            origin = Float2.Zero;
            mapWidth = 0f;

            var bounds = FindMapTileBounds();
            if (bounds == null || bounds.MapWidth <= 0f) return false;

            origin = bounds.MapOrigin2D;
            mapWidth = bounds.MapWidth;
            return true;
        }

        /// <summary>
        /// 收集当前应显示在地图上的 POI 标记。
        /// 数据来源为地图边界的 POI 列表与导航子系统（<see cref="POIActor"/> 注册）的并集，边界优先。
        /// </summary>
        public static bool TryGetMarkers(List<MapMarkerView> results)
        {
            if (results == null) return false;
            results.Clear();

            if (!TryGetMapProjection(out var origin, out var mapWidth)) return false;

            var navigation = FindNavigationComponent();
            var seen = new HashSet<string>();

            var bounds = FindMapTileBounds();
            Collect(bounds?.POIs, origin, mapWidth, navigation, seen, results);

            var registered = NavigationSubsystem.Instance?.GetAllPOIs();
            Collect(registered, origin, mapWidth, navigation, seen, results);

            // 移动目的地也作为一种标记进入同一列表：小地图 / 世界地图 / 罗盘都只认这个出口，
            // 若只挂在插件的 Markers 集合上，游戏侧 UI 并不会显示它。
            if (TryGetMoveDestination(out var destination))
            {
                results.Add(new MapMarkerView
                {
                    Tag = MoveDestinationTag,
                    Title = "移动目标",
                    Subtitle = "",
                    WorldPosition = destination,
                    Normalized = NavigatorStatics.WorldToNormalizedMapPosition(destination, origin, mapWidth),
                    SupportsFastTravel = false,
                    IsDiscoverable = false,
                    Discovered = true,
                });
            }

            return results.Count > 0;
        }

        /// <summary>移动目的地在地图视图里的固定标签（UI 据此选用目的地样式）。</summary>
        public const string MoveDestinationTag = "MoveDestination";

        private static void Collect(List<POIData> pois, Float2 origin, float mapWidth,
            NarrativeNavigationComponent navigation, HashSet<string> seen, List<MapMarkerView> results)
        {
            if (pois == null) return;

            foreach (var poi in pois)
            {
                if (poi == null || !poi.POITag.IsValid() || !poi.bNeedsMapMarker) continue;
                if (!seen.Add(poi.POITag.TagName)) continue;

                results.Add(new MapMarkerView
                {
                    Tag = poi.POITag.TagName,
                    Title = poi.POIDisplayName,
                    Subtitle = poi.POISubtitle,
                    WorldPosition = poi.POILocation,
                    Normalized = NavigatorStatics.WorldToNormalizedMapPosition(poi.POILocation, origin, mapWidth),
                    SupportsFastTravel = poi.bSupportsFastTravel,
                    IsDiscoverable = poi.bIsDiscoverable,
                    // 语义：非可发现 POI 恒为「已知」（始终可见）；
                    // 可发现 POI 必须由导航组件记录过才算已发现——无导航组件时不假定已发现，
                    // 否则所有可发现 POI 会一直显示为已探索，"可发现"就失去意义。
                    Discovered = !poi.bIsDiscoverable
                                 || (navigation != null && navigation.HasDiscoveredPOI(poi.POITag)),
                });
            }
        }

        /// <summary>取本地玩家在地图上的归一化位置。无玩家或地图未配置时返回 false。</summary>
        public static bool TryGetPlayerNormalizedPosition(out Float2 position)
        {
            position = Float2.Zero;

            if (!TryGetMapProjection(out var origin, out var mapWidth)) return false;
            if (!TryGetPlayerPose(out var worldPosition, out _)) return false;

            position = NavigatorStatics.WorldToNormalizedMapPosition(worldPosition, origin, mapWidth);
            return true;
        }

        /// <summary>
        /// 取本地玩家的世界位置与朝向（度，0=正北、顺时针）。
        /// <para>
        /// 优先用游戏侧 <c>HundunWorldGame.LocalPlayerActor</c>（与 MainUIManager 驱动小地图朝向的来源一致）——
        /// 这是运行时唯一可靠的玩家来源；NarrativePro 导航组件只是回退，
        /// 因为场景中不一定挂载它，若强依赖会导致小地图/方位条退化为演示数据。
        /// </para>
        /// </summary>
        public static bool TryGetPlayerPose(out Vector3 worldPosition, out float yawDegrees)
        {
            worldPosition = Vector3.Zero;
            yawDegrees = 0f;

            var local = HundunWorldGame.Instance?.LocalPlayerActor;
            if (local != null)
            {
                worldPosition = local.Position;
                yawDegrees = NormalizeYaw(local.EulerAngles.Y);
                return true;
            }

            var character = FindNavigationComponent()?.ResolveLocalCharacter();
            if (character?.Actor == null) return false;

            worldPosition = character.Actor.Position;
            yawDegrees = NormalizeYaw(character.Actor.EulerAngles.Y);
            return true;
        }

        /// <summary>把任意角度归一化到 [0,360)。</summary>
        public static float NormalizeYaw(float degrees)
        {
            return ((degrees % 360f) + 360f) % 360f;
        }

        /// <summary>
        /// 请求快速旅行到指定 POI 标签。POI 不存在或不支持快速旅行时返回 false。
        /// </summary>
        public static bool TryRequestFastTravel(string poiTag)
        {
            if (string.IsNullOrEmpty(poiTag)) return false;

            var tag = new GameplayTag(poiTag);
            var navigation = FindNavigationComponent();
            if (navigation == null) return false;

            if (!navigation.GetPointOfInterest(out var poi, tag) || poi == null) return false;

            return navigation.RequestFastTravel(poi);
        }

        /// <summary>取某个标签的 POI 数据（用于地图点击后展示详情等场景）。</summary>
        public static bool TryGetPOI(string poiTag, out POIData poi)
        {
            poi = null;
            if (string.IsNullOrEmpty(poiTag)) return false;

            var navigation = FindNavigationComponent();
            return navigation != null && navigation.GetPointOfInterest(out poi, new GameplayTag(poiTag));
        }

        // ===================================================================
        // 点击移动目的地（与 NarrativePro 标记/面包屑管线共用一份数据源）
        // ===================================================================

        private static ClickToMoveDestinationMarker _moveDestinationMarker;

        /// <summary>当前移动目的地标记（无目的地时为 null）。</summary>
        public static MapMarker MoveDestinationMarker => _moveDestinationMarker;

        /// <summary>
        /// 设置/更新移动目的地：在导航组件上挂一个带面包屑的目的地标记。
        /// <para>
        /// 标记由 <see cref="NarrativeNavigationComponent"/> 统一 Tick 重算面包屑（限频 4Hz），
        /// 世界地图 / 罗盘 / 小地图都从这同一条标记管线取数，所以点一次地图与角色走的路劲一致。
        /// 导航组件不可用时返回 false——目的地显示降级，但角色跟随仍可直线执行。
        /// </para>
        /// </summary>
        public static bool TrySetMoveDestination(Vector3 worldLocation)
        {
            var navigation = FindNavigationComponent();
            if (navigation == null) return false;

            if (_moveDestinationMarker == null)
            {
                _moveDestinationMarker = new ClickToMoveDestinationMarker();
                if (!navigation.AddMarker(_moveDestinationMarker))
                {
                    _moveDestinationMarker = null;
                    return false;
                }
            }

            _moveDestinationMarker.SetDestination(worldLocation);
            // 起点绑定到本地玩家：角色前进时面包屑随之缩短，而不是停在初始位置
            _moveDestinationMarker.BindBreadcrumbsTo(HundunWorldGame.Instance?.LocalPlayerActor);
            return true;
        }

        /// <summary>清除移动目的地标记（到达、玩家接管或超时）。</summary>
        public static void ClearMoveDestination()
        {
            if (_moveDestinationMarker == null) return;

            var navigation = FindNavigationComponent();
            navigation?.RemoveMarker(_moveDestinationMarker);
            _moveDestinationMarker = null;
        }

        /// <summary>取当前移动目的地世界坐标。</summary>
        public static bool TryGetMoveDestination(out Vector3 worldLocation)
        {
            worldLocation = Vector3.Zero;
            if (_moveDestinationMarker == null) return false;

            // 标记挂在本地玩家的导航组件上，而本字段是静态的：
            // 场景切换 / 玩家 Actor 销毁后导航组件已不在，此时必须自我清理，
            // 否则地图会一直显示一个指向已销毁对象的目的地。
            if (FindNavigationComponent() == null)
            {
                ClearMoveDestination();
                return false;
            }

            worldLocation = _moveDestinationMarker.GetMarkerTransform().Translation;
            return true;
        }

        /// <summary>
        /// 取面包屑点列（等弧长重采样后的路径），供需要画线的 UI 使用。
        /// <para>
        /// 导航网格尚未烘焙时插件算不出面包屑（<c>IsNavigationReady</c> 为 false），此时退化成
        /// 「角色→目的地」的单段直线——这与角色实际走的兜底行为一致，
        /// 否则点了地图却一条线都不画，看起来像是功能坏了。
        /// 无目的地时返回 false 且清空列表。
        /// </para>
        /// </summary>
        public static bool TryGetMoveDestinationPath(List<Vector3> points)
        {
            points?.Clear();
            if (points == null) return false;
            if (!TryGetMoveDestination(out var destination)) return false;

            var breadcrumb = _moveDestinationMarker.GetBreadcrumbPath();
            if (breadcrumb != null && breadcrumb.Count > 1)
            {
                for (int i = 0; i < breadcrumb.Count; i++) points.Add(breadcrumb[i]);
                return true;
            }

            if (!TryGetPlayerPose(out var playerPosition, out _)) return false;

            points.Add(playerPosition);
            points.Add(destination);
            return true;
        }

        /// <summary>
        /// 把世界坐标点列投影为大地图的归一化坐标（0~1，左上为原点）。
        /// 地图未配置时返回 false 且清空结果。UI 侧只走这个出口，不直接依赖插件投影函数。
        /// </summary>
        public static bool TryProjectToWorldMapNormalized(List<Vector3> worldPoints, List<Float2> results)
        {
            results?.Clear();
            if (worldPoints == null || results == null || worldPoints.Count == 0) return false;

            if (!TryGetMapProjection(out var origin, out var mapWidth)) return false;

            for (int i = 0; i < worldPoints.Count; i++)
            {
                results.Add(NavigatorStatics.WorldToNormalizedMapPosition(worldPoints[i], origin, mapWidth));
            }

            return results.Count > 0;
        }
    }
}

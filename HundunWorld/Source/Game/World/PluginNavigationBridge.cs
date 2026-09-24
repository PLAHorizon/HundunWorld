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

            return results.Count > 0;
        }

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
    }
}

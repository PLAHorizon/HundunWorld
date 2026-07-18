using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Items;

namespace NarrativePro.Navigation
{
    /// <summary>
    /// 导航主组件。挂到 PlayerController 上，管理标记注册、自定义航点、POI 发现和快速旅行。
    /// 适配 UE5 UNarrativeNavigationComponent，移除复制/RPC，改为本地逻辑 + 事件回调。
    /// 实现 INarrativeSavableComponent 通过 PrepareForSave/Load 方法。
    /// </summary>
    public class NarrativeNavigationComponent : FlaxEngine.Script
    {
        /// <summary>已发现的 POI 标签容器</summary>
        public GameplayTagContainer DiscoveredPOIs { get; set; } = new GameplayTagContainer();

        /// <summary>所有自定义航点</summary>
        public List<CustomWaypointMarker> CustomWaypoints { get; set; } = new List<CustomWaypointMarker>();

        /// <summary>地图宽度（cm），即地图图像覆盖的世界宽度</summary>
        public float MapWidth { get; set; } = 100000f;

        /// <summary>地图中心在世界空间的位置（X,Y 平面）</summary>
        public Float2 MapOrigin { get; set; } = Float2.Zero;

        /// <summary>最大自定义航点数</summary>
        public int MaxCustomWaypoints { get; set; } = 10;

        /// <summary>世界中找到的地图瓦片边界</summary>
        public MapTileBoundsActor MapTileBounds { get; set; }

        /// <summary>当前追踪的所有标记</summary>
        public List<MapMarker> Markers { get; set; } = new List<MapMarker>();

        /// <summary>缓存的地图瓦片标记（用于快速访问）</summary>
        public HashSet<MapTileMarker> MapTiles { get; set; } = new HashSet<MapTileMarker>();

        /// <summary>POI 标记查找表</summary>
        public Dictionary<GameplayTag, POIMapMarker> POIMarkers { get; set; } = new Dictionary<GameplayTag, POIMapMarker>();

        /// <summary>POI 快速查找表</summary>
        public Dictionary<GameplayTag, POIData> POILookupMap { get; set; } = new Dictionary<GameplayTag, POIData>();

        /// <summary>已保存的自定义航点变换（用于存档）</summary>
        public List<Transform> SavedCustomMarkerTransforms { get; set; } = new List<Transform>();

        // 事件
        public event System.Action<MapMarker> OnMarkerAdded;
        public event System.Action<MapMarker> OnMarkerRemoved;
        public event System.Action<GameplayTag> OnPOIDiscovered;
        public event System.Action<POIData> OnFastTravelRequested;

        public override void OnEnable()
        {
            base.OnEnable();
            NavigationSubsystem.Instance?.RegisterNavigationComponent(this);
            Load();
        }

        public override void OnDisable()
        {
            NavigationSubsystem.Instance?.UnregisterNavigationComponent(this);
            base.OnDisable();
        }

        /// <summary>添加标记。返回是否成功。</summary>
        public virtual bool AddMarker(MapMarker mapMarker)
        {
            if (mapMarker == null) return false;
            if (Markers.Contains(mapMarker)) return false;

            Markers.Add(mapMarker);

            if (mapMarker is MapTileMarker tileMarker)
            {
                MapTiles.Add(tileMarker);
            }
            else if (mapMarker is POIMapMarker poiMarker)
            {
                if (poiMarker.POI != null && poiMarker.POI.POITag.IsValid())
                {
                    POIMarkers[poiMarker.POI.POITag] = poiMarker;
                }
            }

            mapMarker.OnMarkerAdded(this);
            OnMarkerAdded?.Invoke(mapMarker);
            return true;
        }

        /// <summary>移除标记。返回是否成功。</summary>
        public virtual bool RemoveMarker(MapMarker mapMarker)
        {
            if (mapMarker == null) return false;
            bool removed = Markers.Remove(mapMarker);
            if (!removed) return false;

            if (mapMarker is MapTileMarker tileMarker)
            {
                MapTiles.Remove(tileMarker);
            }
            else if (mapMarker is POIMapMarker poiMarker)
            {
                if (poiMarker.POI != null && poiMarker.POI.POITag.IsValid())
                {
                    POIMarkers.Remove(poiMarker.POI.POITag);
                }
            }

            mapMarker.OnMarkerRemoved(this);
            OnMarkerRemoved?.Invoke(mapMarker);
            return true;
        }

        /// <summary>选中标记。</summary>
        public void SelectMarker(MapMarker marker)
        {
            if (marker == null) return;
            marker.OnSelect(this);
        }

        /// <summary>获取已缓存的 POI。返回是否找到。</summary>
        public bool GetPointOfInterest(out POIData outPointOfInterest, GameplayTag poiTag)
        {
            return POILookupMap.TryGetValue(poiTag, out outPointOfInterest);
        }

        /// <summary>查找最接近指定位置的 POI。返回是否找到。</summary>
        public bool GetNearestPOIToPoint(out POIData outPointOfInterest, Vector3 testLocation)
        {
            outPointOfInterest = null;
            float bestDist = float.MaxValue;
            foreach (var kvp in POILookupMap)
            {
                float d = Vector3.Distance(testLocation, kvp.Value.POILocation);
                if (d < bestDist)
                {
                    bestDist = d;
                    outPointOfInterest = kvp.Value;
                }
            }
            return outPointOfInterest != null;
        }

        /// <summary>设置指定域的地图层。返回是否成功。</summary>
        public bool SetMapLayer(GameplayTag newLayer, GameplayTagContainer domains)
        {
            if (MapTileBounds?.TileSet == null) return false;
            MapTileBounds.TileSet.TileLayer = newLayer;
            return true;
        }

        /// <summary>将 POI 标记为已发现。</summary>
        public virtual void DiscoverPOI(GameplayTag poiTag)
        {
            if (DiscoveredPOIs.HasTag(poiTag)) return;
            DiscoveredPOIs.AddTag(poiTag);
            OnPOIDiscovered?.Invoke(poiTag);
        }

        /// <summary>检查是否已发现给定 POI。</summary>
        public virtual bool HasDiscoveredPOI(GameplayTag poiTag)
        {
            return DiscoveredPOIs.HasTag(poiTag);
        }

        /// <summary>在指定位置放置自定义航点。</summary>
        public virtual CustomWaypointMarker PlaceCustomWaypoint(Transform transform)
        {
            if (CustomWaypoints.Count >= MaxCustomWaypoints)
            {
                // 移除最旧的航点
                var oldest = CustomWaypoints[0];
                RemoveCustomWaypoint(oldest);
            }

            var waypoint = new CustomWaypointMarker
            {
                MarkerTransform = transform,
                ActorOwner = Actor
            };
            // 添加到默认域
            waypoint.MarkerDomain.AddTag(NavigatorGameplayTags.NavigatorTypes_Worldmap);
            waypoint.MarkerDomain.AddTag(NavigatorGameplayTags.NavigatorTypes_Minimap);
            waypoint.MarkerDomain.AddTag(NavigatorGameplayTags.NavigatorTypes_Compass);

            CustomWaypoints.Add(waypoint);
            AddMarker(waypoint);
            return waypoint;
        }

        /// <summary>移除自定义航点。</summary>
        public virtual void RemoveCustomWaypoint(CustomWaypointMarker waypoint)
        {
            if (waypoint == null) return;
            RemoveMarker(waypoint);
            CustomWaypoints.Remove(waypoint);
        }

        /// <summary>请求快速旅行到指定 POI。</summary>
        public virtual void RequestFastTravel(POIData poi)
        {
            if (poi == null) return;
            if (!poi.bSupportsFastTravel) return;
            OnFastTravelRequested?.Invoke(poi);
        }

        /// <summary>存档：准备保存。</summary>
        public virtual void PrepareForSave()
        {
            SavedCustomMarkerTransforms.Clear();
            foreach (var wp in CustomWaypoints)
            {
                SavedCustomMarkerTransforms.Add(wp.MarkerTransform);
            }
        }

        /// <summary>读档：恢复状态。</summary>
        public virtual void Load()
        {
            // 恢复自定义航点
            if (SavedCustomMarkerTransforms != null)
            {
                foreach (var t in SavedCustomMarkerTransforms)
                {
                    PlaceCustomWaypoint(t);
                }
            }
        }
    }
}

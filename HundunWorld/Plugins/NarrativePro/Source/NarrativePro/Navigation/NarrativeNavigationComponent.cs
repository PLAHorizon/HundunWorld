using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.Items;
using NarrativePro.UnrealFramework;

namespace NarrativePro.Navigation
{
    /// <summary>
    /// 导航主组件。挂到 PlayerController（或玩家 Actor）上，管理标记注册、地图瓦片、自定义航点、POI 发现和快速旅行。
    /// 适配 UE5 UNarrativeNavigationComponent（移除复制/RPC，改为本地逻辑 + 事件回调）。
    /// <para>
    /// 启动流程（对应 UE5 <c>BeginPlay</c>）：
    /// 1) 解析场景中的 <see cref="MapTileBoundsActor"/>，同步 MapWidth / MapOrigin；
    /// 2) 按 <c>TileSet.Tiles</c> 逐块生成 <see cref="MapTileMarker"/>（UE5 在 BeginPlay 中完成）；
    /// 3) 按 POI 数据生成 <see cref="POIMapMarker"/>；
    /// 4) <see cref="Load"/> 恢复存档中的航点与地图层级。
    /// </para>
    /// </summary>
    public class NarrativeNavigationComponent : FlaxEngine.Script, NarrativePro.SaveSystem.INarrativeSavableComponent
    {
        /// <summary>构建导航的最大重试帧数（等待 MapTileBoundsActor / 子系统就绪）</summary>
        private const int MaxBuildAttempts = 60;

        /// <summary>已发现的 POI 标签容器</summary>
        public GameplayTagContainer DiscoveredPOIs { get; set; } = new GameplayTagContainer();

        /// <summary>所有自定义航点</summary>
        public List<CustomWaypointMarker> CustomWaypoints { get; set; } = new List<CustomWaypointMarker>();

        /// <summary>当前地图层级（对应 UE5 CurrentMapLayer，随存档恢复）</summary>
        public GameplayTag CurrentMapLayer { get; set; } = NavigatorGameplayTags.MapLayer_Default;

        /// <summary>地图宽度（世界单位），即地图图像覆盖的 XZ 正方形边长</summary>
        public float MapWidth { get; set; } = 100000f;

        /// <summary>地图中心在世界空间的位置（XZ 平面，Y 为高度轴不参与）</summary>
        public Float2 MapOrigin { get; set; } = Float2.Zero;

        /// <summary>最大自定义航点数</summary>
        public int MaxCustomWaypoints { get; set; } = 10;

        /// <summary>世界中找到的地图瓦片边界</summary>
        public MapTileBoundsActor MapTileBounds { get; set; }

        /// <summary>当前追踪的所有标记</summary>
        public List<MapMarker> Markers { get; set; } = new List<MapMarker>();

        /// <summary>缓存的地图瓦片标记（用于快速访问与分层切换）</summary>
        public HashSet<MapTileMarker> MapTiles { get; set; } = new HashSet<MapTileMarker>();

        /// <summary>POI 标记查找表</summary>
        public Dictionary<GameplayTag, POIMapMarker> POIMarkers { get; set; } = new Dictionary<GameplayTag, POIMapMarker>();

        /// <summary>POI 快速查找表</summary>
        public Dictionary<GameplayTag, POIData> POILookupMap { get; set; } = new Dictionary<GameplayTag, POIData>();

        /// <summary>已保存的自定义航点变换（用于存档）</summary>
        public List<Transform> SavedCustomMarkerTransforms { get; set; } = new List<Transform>();

        /// <summary>导航是否已从地图边界构建完成</summary>
        public bool IsNavigationBuilt { get; private set; }

        private int _buildAttempts;

        /// <summary>
        /// 宿主注入的本地玩家 Actor（运行时注入，私有字段故不参与序列化）。
        /// 游戏侧的玩家是普通 Actor（无 <c>NarrativeCharacter</c>），仅靠 <see cref="ResolveLocalCharacter"/>
        /// 解析不到，必须由宿主注入。
        /// </summary>
        private Actor _localPlayerActor;

        // 事件
        public event System.Action<MapMarker> OnMarkerAdded;
        public event System.Action<MapMarker> OnMarkerRemoved;
        public event System.Action<GameplayTag> OnPOIDiscovered;

        /// <summary>
        /// 快速旅行请求。参数：目标 POI、解析出的目标变换。
        /// 宿主（游戏侧）订阅后自行执行移动；未订阅时组件回退为直接移动 Actor。
        /// </summary>
        public event System.Action<POIData, Transform> OnFastTravelRequested;

        /// <summary>由宿主注册「本地玩家 Actor」（运行时注入，替代 UE5 中从 PlayerController 取 Pawn 的做法）。</summary>
        public void SetLocalPlayerActor(Actor player)
        {
            _localPlayerActor = player;
        }

        /// <summary>
        /// 解析本地玩家 Actor：优先用宿主注入的，其次回退到 <see cref="ResolveLocalCharacter"/>。
        /// </summary>
        public Actor ResolveLocalPlayerActor()
        {
            if (_localPlayerActor != null) return _localPlayerActor;
            return ResolveLocalCharacter()?.Actor;
        }

        public override void OnEnable()
        {
            base.OnEnable();
            NavigationSubsystem.Instance?.RegisterNavigationComponent(this);

            IsNavigationBuilt = false;
            _buildAttempts = 0;
            TryBuildNavigation();
        }

        public override void OnDisable()
        {
            NavigationSubsystem.Instance?.UnregisterNavigationComponent(this);
            ClearNavigation();
            base.OnDisable();
        }

        public override void OnUpdate()
        {
            // MapTileBoundsActor / 导航子系统的启用顺序不确定，未构建成功时按帧重试有限次数
            if (!IsNavigationBuilt && _buildAttempts < MaxBuildAttempts)
            {
                TryBuildNavigation();
                return;
            }

            // 驱动需要面包屑路径的标记（Flax 无 FTimerManager，由本组件统一按帧推进）
            float deltaTime = Time.DeltaTime;
            for (int i = 0; i < Markers.Count; i++)
            {
                Markers[i]?.Tick(deltaTime);
            }
        }

        /// <summary>
        /// 解析地图边界并构建瓦片 / POI 标记，随后恢复存档状态。构建成功后不再重复执行。
        /// </summary>
        public void TryBuildNavigation()
        {
            if (IsNavigationBuilt) return;
            _buildAttempts++;

            var bounds = MapTileBounds ?? NavigationSubsystem.Instance?.MapTileBounds ?? Level.FindScript<MapTileBoundsActor>();
            if (bounds == null) return;

            MapTileBounds = bounds;
            MapWidth = bounds.MapWidth;
            MapOrigin = bounds.MapOrigin2D;

            BuildTileMarkers();
            BuildPOIMarkers();

            IsNavigationBuilt = true;
            Load();
        }

        /// <summary>按 <see cref="MapTileBoundsActor.TileSet"/> 生成瓦片标记（对应 UE5 BeginPlay 中的瓦片构建循环）。</summary>
        public void BuildTileMarkers()
        {
            MapTiles.Clear();

            var tileSet = MapTileBounds?.TileSet;
            if (tileSet?.Tiles == null) return;

            for (int i = 0; i < tileSet.Tiles.Count; i++)
            {
                var tileData = tileSet.Tiles[i];
                if (tileData == null) continue;

                var tileMarker = new MapTileMarker
                {
                    TileData = tileData,
                    TileSet = tileSet,
                    TileIndex = i,
                    MarkerTransform = new Transform(tileData.TileLocation),
                };

                tileMarker.ApplyTileImageSize();
                tileMarker.ApplyLayerImage(CurrentMapLayer);

                MapTiles.Add(tileMarker);
                AddMarker(tileMarker);
            }
        }

        /// <summary>
        /// 按 POI 数据生成 POI 标记。数据来自地图边界的 POI 列表与导航子系统（<see cref="POIActor"/> 注册）的并集。
        /// </summary>
        public void BuildPOIMarkers()
        {
            CollectPOIs();

            foreach (var kvp in POILookupMap)
            {
                var poi = kvp.Value;
                if (poi == null || !poi.POITag.IsValid() || !poi.bNeedsMapMarker) continue;

                var poiMarker = new POIMapMarker
                {
                    POI = poi,
                    MarkerTransform = new Transform(poi.POILocation),
                };
                poiMarker.DefaultMarkerSettings.MarkerTitleText = poi.POIDisplayName;
                poiMarker.DefaultMarkerSettings.LocationIconPath = poi.MapMarkerIconPath;

                POIMarkers[poi.POITag] = poiMarker;
                AddMarker(poiMarker);
            }
        }

        /// <summary>合并地图边界与导航子系统中的 POI 数据（边界优先），写入 <see cref="POILookupMap"/>。</summary>
        private void CollectPOIs()
        {
            POILookupMap.Clear();

            var boundsPOIs = MapTileBounds?.POIs;
            if (boundsPOIs != null)
            {
                foreach (var poi in boundsPOIs)
                {
                    if (poi != null && poi.POITag.IsValid())
                    {
                        POILookupMap[poi.POITag] = poi;
                    }
                }
            }

            var registered = NavigationSubsystem.Instance?.GetAllPOIs();
            if (registered != null)
            {
                foreach (var poi in registered)
                {
                    if (poi != null && poi.POITag.IsValid() && !POILookupMap.ContainsKey(poi.POITag))
                    {
                        POILookupMap[poi.POITag] = poi;
                    }
                }
            }
        }

        /// <summary>移除全部由本组件构建的瓦片与 POI 标记。</summary>
        public void ClearNavigation()
        {
            foreach (var tile in MapTiles)
            {
                RemoveMarker(tile);
            }
            MapTiles.Clear();

            foreach (var kvp in POIMarkers)
            {
                RemoveMarker(kvp.Value);
            }
            POIMarkers.Clear();
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
            if (!marker.CanInteract(this)) return;
            marker.OnSelect(this);
        }

        /// <summary>获取已缓存的 POI。返回是否找到。</summary>
        public bool GetPointOfInterest(out POIData outPointOfInterest, GameplayTag poiTag)
        {
            return POILookupMap.TryGetValue(poiTag, out outPointOfInterest);
        }

        /// <summary>查找最接近指定位置的 POI（XZ 平面距离，忽略高度差）。返回是否找到。</summary>
        public bool GetNearestPOIToPoint(out POIData outPointOfInterest, Vector3 testLocation)
        {
            outPointOfInterest = null;

            float bestDistanceSqr = float.MaxValue;
            foreach (var kvp in POILookupMap)
            {
                var poi = kvp.Value;
                if (poi == null) continue;

                float distanceSqr = NavigatorStatics.DistanceSquared2D(poi.POILocation, testLocation);
                if (distanceSqr < bestDistanceSqr)
                {
                    bestDistanceSqr = distanceSqr;
                    outPointOfInterest = poi;
                }
            }

            return outPointOfInterest != null;
        }

        /// <summary>
        /// 切换地图层级：把每个瓦片的贴图替换为该层的图（该层无图时回退默认层）。
        /// 对应 UE5 <c>SetMapLayer</c>。参数 <paramref name="domains"/> 在 UE5 中亦为未实现的 TODO（多导航器域差异），此处保留签名。
        /// </summary>
        public bool SetMapLayer(GameplayTag newLayer, GameplayTagContainer domains)
        {
            if (MapTileBounds == null) return false;

            CurrentMapLayer = newLayer;

            foreach (var tileMarker in MapTiles)
            {
                if (tileMarker == null) continue;

                // 瓦片数据可能被重新生成过，按索引重新绑定，避免持有过期引用
                if (MapTileBounds.GetTileAtIndex(tileMarker.TileIndex) is MapTile tileData)
                {
                    tileMarker.TileData = tileData;
                }
                tileMarker.ApplyLayerImage(newLayer);
            }

            return true;
        }

        /// <summary>将 POI 标记为已发现，并刷新对应标记（未发现时图标置灰）。</summary>
        public virtual void DiscoverPOI(GameplayTag poiTag)
        {
            if (!poiTag.IsValid()) return;
            if (DiscoveredPOIs.HasTag(poiTag)) return;

            DiscoveredPOIs.AddTag(poiTag);
            OnPOIDiscovered?.Invoke(poiTag);

            if (POIMarkers.TryGetValue(poiTag, out var marker) && marker != null)
            {
                marker.RefreshMarker();
            }
        }

        /// <summary>检查是否已发现给定 POI。</summary>
        public virtual bool HasDiscoveredPOI(GameplayTag poiTag)
        {
            return DiscoveredPOIs.HasTag(poiTag);
        }

        /// <summary>在指定位置放置自定义航点。达到上限时返回 null（与 UE5 一致，不静默丢弃最旧航点）。</summary>
        public virtual CustomWaypointMarker PlaceCustomWaypoint(Transform transform)
        {
            if (CustomWaypoints.Count + 1 > MaxCustomWaypoints) return null;

            var waypoint = new CustomWaypointMarker
            {
                MarkerTransform = transform,
                ActorOwner = Actor
            };

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

        /// <summary>
        /// 请求快速旅行到指定 POI。
        /// <para>
        /// <b>谁真正执行移动</b>：对应 UE5 的语义，本组件只做校验与目标解析，然后广播
        /// <see cref="OnFastTravelRequested"/> 交由宿主（游戏侧）执行——因为在本项目里玩家位置由
        /// ECS <c>PredictedTransformComponent</c> 单点驱动，必须由游戏侧写该组件，直接改 <c>Actor.Position</c>
        /// 会被每帧同步覆盖。没有订阅者时（插件独立使用）才回退为直接移动 Actor。
        /// </para>
        /// </summary>
        /// <returns>是否已发出/完成传送请求。</returns>
        public virtual bool RequestFastTravel(POIData poi)
        {
            if (poi == null) return false;

            if (!poi.bSupportsFastTravel)
            {
                NarrativeLog.LogWarning($"RequestFastTravel: POI[{poi.POITag}] 不支持快速旅行");
                return false;
            }

            // 可发现的 POI 必须先被发现才能传送
            if (poi.bIsDiscoverable && !HasDiscoveredPOI(poi.POITag))
            {
                NarrativeLog.LogWarning($"RequestFastTravel: POI[{poi.POITag}] 尚未被发现，无法传送");
                return false;
            }

            var destination = ResolveFastTravelTransform(poi);

            // 宿主接管：由游戏侧按自己的权威位置机制落地
            if (OnFastTravelRequested != null)
            {
                OnFastTravelRequested(poi, destination);
                return true;
            }

            // 回退（无宿主）：插件自己移动 Actor
            var mover = ResolveLocalPlayerActor();
            if (mover == null)
            {
                NarrativeLog.LogWarning("RequestFastTravel: 找不到本地玩家 Actor（既无宿主订阅，也无法解析），无法传送");
                return false;
            }

            Vector3 location = destination.Translation;
            Quaternion rotation = destination.Orientation;

            // 安全落点求解失败时不阻断：POI 的落点是设计者配置的目标，直接采用并告警
            if (!ArsenalStatics.FindTeleportSpot(mover, ref location, ref rotation))
            {
                NarrativeLog.LogWarning($"RequestFastTravel: POI[{poi.POITag}] 未能求解安全落点，改用配置落点直接传送");
            }

            ArsenalStatics.TeleportActor(mover, location, rotation);
            return true;
        }

        /// <summary>解析传送目标变换：POI 数据 → 对应 POIActor 的快速旅行落点 → POI 位置。</summary>
        protected virtual Transform ResolveFastTravelTransform(POIData poi)
        {
            // ① POI 数据自带的有效落点（非 Identity 视为已配置）
            if (!IsIdentityTransform(poi.POIFastTravelSpot))
            {
                return poi.POIFastTravelSpot;
            }

            // ② 场景中同名 POIActor 的快速旅行落点
            var poiActor = FindPOIActor(poi.POITag);
            if (poiActor != null)
            {
                var fromActor = poiActor.GetFastTravelTransform();
                if (!IsIdentityTransform(fromActor))
                {
                    return fromActor;
                }
            }

            // ③ 回退到 POI 自身位置
            return new Transform(poi.POILocation);
        }

        /// <summary>查找本地玩家角色：本组件的 Actor 可能是控制器，也可能直接就是角色。</summary>
        public virtual NarrativeCharacter ResolveLocalCharacter()
        {
            if (Actor != null)
            {
                var controller = Actor.GetScript<NarrativePlayerController>();
                var controlled = controller?.GetControlledCharacter();
                if (controlled != null) return controlled;

                var direct = Actor.GetScript<NarrativeCharacter>();
                if (direct != null) return direct;
            }

            return Level.FindScript<NarrativePlayerCharacter>() ?? Level.FindScript<NarrativeCharacter>();
        }

        /// <summary>在场景中查找指定标签的 POIActor。</summary>
        protected virtual POIActor FindPOIActor(GameplayTag poiTag)
        {
            if (!poiTag.IsValid()) return null;

            var poiActors = Level.GetScripts<POIActor>(null);
            if (poiActors == null) return null;

            foreach (var poiActor in poiActors)
            {
                if (poiActor != null && poiActor.POITag == poiTag) return poiActor;
            }

            return null;
        }

        /// <summary>
        /// 执行传送：写入角色变换并同步 CharacterController（避免其位置缓存覆盖传送结果导致回弹），
        /// 随后触发 <see cref="NarrativeCharacter.OnTeleportSucceeded"/>。
        /// </summary>
        /// <summary>
        /// 判断传送点是否已配置。以「平移分量非零」为判据：
        /// <see cref="Transform.Identity"/> 是 POI 数据的默认值，视为未配置。
        /// </summary>
        private static bool IsIdentityTransform(Transform transform)
        {
            return transform.Translation.LengthSquared < 1e-6f;
        }

        /// <summary>存档：准备保存。</summary>
        public virtual void PrepareForSave()
        {
            SavedCustomMarkerTransforms.Clear();
            foreach (var wp in CustomWaypoints)
            {
                if (wp != null) SavedCustomMarkerTransforms.Add(wp.MarkerTransform);
            }
        }

        /// <summary>读档：恢复自定义航点与地图层级。</summary>
        public virtual void Load()
        {
            // 清除现有航点后按存档重建（对应 UE5 Load_Implementation）
            foreach (var wp in CustomWaypoints)
            {
                RemoveMarker(wp);
            }
            CustomWaypoints.Clear();

            if (SavedCustomMarkerTransforms != null)
            {
                foreach (var t in SavedCustomMarkerTransforms)
                {
                    PlaceCustomWaypoint(t);
                }
            }

            // 玩家数据可能在标记之后加载，此处主动刷新已发现 POI 的标记颜色
            foreach (var poiTag in DiscoveredPOIs.GetTags())
            {
                var tag = new GameplayTag(poiTag);
                if (POIMarkers.TryGetValue(tag, out var marker) && marker != null)
                {
                    marker.RefreshMarker();
                }
            }

            if (CurrentMapLayer.IsValid())
            {
                SetMapLayer(CurrentMapLayer, new GameplayTagContainer());
            }
        }
    }
}

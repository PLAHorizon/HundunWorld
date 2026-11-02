using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Items;

namespace NarrativePro.Navigation
{
    /// <summary>
    /// 导航标记设置。适配 UE5 FNavigationMarkerSettings。
    /// 支持按导航器类型（世界地图/小地图/罗盘/屏幕空间）的覆盖。
    /// </summary>
    [Serializable]
    public class NavigationMarkerSettings
    {
        // 覆盖开关
        public bool bOverride_LocationDisplayName { get; set; } = false;
        public bool bOverride_LocationIcon { get; set; } = false;
        public bool bOverride_IconTint { get; set; } = false;
        public bool bOverride_IconSize { get; set; } = false;
        public bool bOverride_IconOffset { get; set; } = false;
        public bool bOverride_bShowActorRotation { get; set; } = false;

        // 标记文本
        public string MarkerTitleText { get; set; } = "Location Marker";
        public string MarkerSubtitleText { get; set; } = "";

        // 图标资源路径（替代 UE UTexture2D*）
        public string LocationIconPath { get; set; } = "";

        // 图标着色
        public Color IconTint { get; set; } = Color.White;

        // 图标大小（罗盘/地图/屏幕标记均使用）
        public Vector2 IconSize { get; set; } = new Vector2(20f, 20f);

        // Actor 空间偏移（比 SceneComponent 更高效）
        public Vector3 IconOffset { get; set; } = Vector3.Zero;

        // 是否显示 Actor 旋转
        public bool bShowActorRotation { get; set; } = false;
    }

    /// <summary>
    /// 标记 OnPaint 数据。适配 UE5 FMarkerOnPaintData。
    /// UE5 用三块 FGeometry（父/地图/图标）表达绘制空间；Flax 的控件几何由
    /// <see cref="FlaxEngine.GUI.Control"/> 提供，因此这里同时保留矩形（几何尺寸）与控件引用
    /// （控件存在时 <see cref="NarrativePro.Widgets.NarrativeWidgetHelpers"/> 走精确的坐标变换）。
    /// </summary>
    public class MarkerOnPaintData
    {
        /// <summary>父控件绘制矩形</summary>
        public Rectangle ParentGeometry { get; set; }
        /// <summary>地图控件绘制矩形</summary>
        public Rectangle MapGeometry { get; set; }
        /// <summary>图标控件绘制矩形</summary>
        public Rectangle IconGeometry { get; set; }

        /// <summary>父控件（可选；提供时坐标变换按控件层级精确计算）</summary>
        public FlaxEngine.GUI.Control ParentControl { get; set; }
        /// <summary>地图控件（可选；提供时坐标变换按控件层级精确计算）</summary>
        public FlaxEngine.GUI.Control MapControl { get; set; }

        /// <summary>地图中心在世界空间的位置（X,Y 平面）</summary>
        public Float2 MapOrigin { get; set; } = new Float2(float.MaxValue, float.MaxValue);
        /// <summary>当前地图平移量（地图空间）</summary>
        public Float2 MapPan { get; set; } = new Float2(float.MaxValue, float.MaxValue);
        /// <summary>地图在世界空间的总宽度（世界单位），用于把世界距离换算成地图像素</summary>
        public float MapWorldWidth { get; set; } = 0f;
        /// <summary>地图贴图/控件在地图空间的像素宽度</summary>
        public float MapImagePixels { get; set; } = 0f;
        /// <summary>此标记显示的导航器域</summary>
        public GameplayTag Domain { get; set; } = GameplayTag.None;
    }

    /// <summary>
    /// 地图标记基类。适配 UE5 UMapMarker。
    /// UMapMarker 是 UObject（非 UActorComponent），但需要 GetWorld，因此 Flax 中作为普通 C# 类，
    /// 通过 Owner 字段引用 Actor 来获取场景。
    /// 移除复制/RPC，改为本地逻辑 + 事件回调。
    /// 面包屑路径走 Flax 引擎寻路（<see cref="NavigatorStatics.TryFindPath"/>）并带导航区域代价优化，
    /// 绘制由 <see cref="DrawBreadcrumb"/> 用 Render2D 虚线完成。
    /// </summary>
    public class MapMarker
    {
        /// <summary>标记默认设置</summary>
        public NavigationMarkerSettings DefaultMarkerSettings { get; set; } = new NavigationMarkerSettings();

        /// <summary>世界地图覆盖设置</summary>
        public NavigationMarkerSettings WorldMapOverrideSettings { get; set; } = new NavigationMarkerSettings();

        /// <summary>小地图覆盖设置</summary>
        public NavigationMarkerSettings MinimapOverrideSettings { get; set; } = new NavigationMarkerSettings();

        /// <summary>罗盘覆盖设置</summary>
        public NavigationMarkerSettings CompassOverrideSettings { get; set; } = new NavigationMarkerSettings();

        /// <summary>屏幕空间覆盖设置</summary>
        public NavigationMarkerSettings ScreenspaceOverrideSettings { get; set; } = new NavigationMarkerSettings();

        /// <summary>标记在世界中的位置</summary>
        public Transform MarkerTransform { get; set; } = Transform.Identity;

        /// <summary>此标记代表的 Actor</summary>
        public Actor ActorOwner { get; set; }

        /// <summary>是否需要 OnPaint 调用</summary>
        public bool bWantsOnPaint { get; set; } = false;

        /// <summary>悬停时显示的动作文本</summary>
        public string DefaultMarkerActionText { get; set; } = "";

        /// <summary>UI 绘制 ZOrder（数值越大优先级越高）</summary>
        public int ZOrder { get; set; } = 0;

        /// <summary>是否固定到地图边缘（不超出边界）</summary>
        public bool bPinToMapEdge { get; set; } = false;

        /// <summary>罗盘上开始淡出标记的距离</summary>
        public float MarkerStartFadeOutDistance { get; set; } = 5000f;

        /// <summary>罗盘上开始淡入标记的距离</summary>
        public float MarkerStartFadeInDistance { get; set; } = 10000f;

        /// <summary>标记显示在哪些导航器上</summary>
        public GameplayTagContainer MarkerDomain { get; set; } = new GameplayTagContainer();

        /// <summary>所属的导航组件</summary>
        public NarrativeNavigationComponent OwnerNavComp { get; set; }

        // 面包屑路径相关
        public bool bDrawBreadcrumbs { get; set; } = true;
        public double UpdateNavDistanceThreshhold { get; set; } = 200.0;
        public float DistanceBetweenPoints { get; set; } = 300f;
        public float UpdateNavPathRate { get; set; } = 4f;
        public Color BreadcrumbColor { get; set; } = Color.Gray;
        public float BreadcrumbThickness { get; set; } = 5f;
        public float BreadcrumbDashLength { get; set; } = 10f;

        // 面包屑运行时状态（不序列化）
        private Actor _ownerNavActor;
        private float _pathRefreshTimer;
        private bool _bPathQueryInFlight;
        private Vector3 _cachedLocation = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        private readonly List<Vector3> _navPath = new List<Vector3>();

        // 事件
        public event Action OnRefreshRequired;
        public event Action<NarrativeNavigationComponent> OnSelected;

        /// <summary>注册到所有导航器。</summary>
        public virtual void RegisterMarker()
        {
            // 添加到所有 NarrativeNavigationComponent 实例
            var allNavComps = NavigationSubsystem.Instance?.GetAllNavigationComponents();
            if (allNavComps != null)
            {
                foreach (var navComp in allNavComps)
                {
                    navComp.AddMarker(this);
                }
            }
        }

        /// <summary>从所有导航器移除。</summary>
        public virtual void RemoveMarker()
        {
            var allNavComps = NavigationSubsystem.Instance?.GetAllNavigationComponents();
            if (allNavComps != null)
            {
                foreach (var navComp in allNavComps)
                {
                    navComp.RemoveMarker(this);
                }
            }
            OwnerNavComp = null;
        }

        /// <summary>启用/禁用绘制到标记的路径（面包屑）。对应 UE5 UMapMarker::SetDrawMarkerPathEnabled（作用于 bWantsOnPaint）。</summary>
        public void SetDrawMarkerPathEnabled(bool enabled)
        {
            bWantsOnPaint = enabled;
            if (!enabled) CleanupBreadcrumb();
        }

        /// <summary>Owner 销毁时调用。</summary>
        public virtual void OnOwnerDestroyed(Actor destroyedActor)
        {
            RemoveMarker();
        }

        /// <summary>标记被添加到导航器时调用。对应 UE5 OnMarkerAdded_Implementation（此处初始化面包屑）。</summary>
        public virtual void OnMarkerAdded(NarrativeNavigationComponent ownerNavComp)
        {
            OwnerNavComp = ownerNavComp;

            if (ActorOwner != null)
            {
                InitializeBreadcrumb(ownerNavComp?.Actor);
            }
        }

        /// <summary>标记从导航器移除时调用。对应 UE5 OnMarkerRemoved_Implementation（此处清理面包屑）。</summary>
        public virtual void OnMarkerRemoved(NarrativeNavigationComponent ownerNavComp)
        {
            if (OwnerNavComp == ownerNavComp) OwnerNavComp = null;

            bWantsOnPaint = false;
            CleanupBreadcrumb();
        }

        /// <summary>获取指定导航器类型的设置（含覆盖）。</summary>
        public NavigationMarkerSettings GetMarkerSettings(GameplayTag navigatorType)
        {
            // 默认设置作为基础
            var result = CloneSettings(DefaultMarkerSettings);

            // 应用对应类型的覆盖
            NavigationMarkerSettings overrides = null;
            if (navigatorType == NavigatorGameplayTags.NavigatorTypes_Worldmap)
                overrides = WorldMapOverrideSettings;
            else if (navigatorType == NavigatorGameplayTags.NavigatorTypes_Minimap)
                overrides = MinimapOverrideSettings;
            else if (navigatorType == NavigatorGameplayTags.NavigatorTypes_Compass)
                overrides = CompassOverrideSettings;
            else if (navigatorType == NavigatorGameplayTags.NavigatorTypes_Screenspace)
                overrides = ScreenspaceOverrideSettings;

            if (overrides != null)
            {
                ApplyOverrides(result, overrides);
            }
            return result;
        }

        private static NavigationMarkerSettings CloneSettings(NavigationMarkerSettings src)
        {
            return new NavigationMarkerSettings
            {
                bOverride_LocationDisplayName = src.bOverride_LocationDisplayName,
                bOverride_LocationIcon = src.bOverride_LocationIcon,
                bOverride_IconTint = src.bOverride_IconTint,
                bOverride_IconSize = src.bOverride_IconSize,
                bOverride_IconOffset = src.bOverride_IconOffset,
                bOverride_bShowActorRotation = src.bOverride_bShowActorRotation,
                MarkerTitleText = src.MarkerTitleText,
                MarkerSubtitleText = src.MarkerSubtitleText,
                LocationIconPath = src.LocationIconPath,
                IconTint = src.IconTint,
                IconSize = src.IconSize,
                IconOffset = src.IconOffset,
                bShowActorRotation = src.bShowActorRotation
            };
        }

        private static void ApplyOverrides(NavigationMarkerSettings target, NavigationMarkerSettings overrides)
        {
            if (overrides.bOverride_LocationDisplayName) target.MarkerTitleText = overrides.MarkerTitleText;
            if (overrides.bOverride_LocationDisplayName) target.MarkerSubtitleText = overrides.MarkerSubtitleText;
            if (overrides.bOverride_LocationIcon) target.LocationIconPath = overrides.LocationIconPath;
            if (overrides.bOverride_IconTint) target.IconTint = overrides.IconTint;
            if (overrides.bOverride_IconSize) target.IconSize = overrides.IconSize;
            if (overrides.bOverride_IconOffset) target.IconOffset = overrides.IconOffset;
            if (overrides.bOverride_bShowActorRotation) target.bShowActorRotation = overrides.bShowActorRotation;
        }

        /// <summary>通知 UI 需要刷新。</summary>
        public void RefreshMarker()
        {
            OnRefreshRequired?.Invoke();
        }

        /// <summary>获取标记动作文本。可覆盖。</summary>
        public virtual string GetMarkerActionText(NarrativeNavigationComponent selector)
        {
            return DefaultMarkerActionText;
        }

        /// <summary>获取标记显示文本。可覆盖。</summary>
        public virtual string GetMarkerDisplayText(NarrativeNavigationComponent selector, GameplayTag navigatorType, out string outSubtitleText)
        {
            var settings = GetMarkerSettings(navigatorType);
            outSubtitleText = settings.MarkerSubtitleText;
            return settings.MarkerTitleText;
        }

        /// <summary>获取标记颜色。可覆盖。</summary>
        public virtual Color GetMarkerColor(NarrativeNavigationComponent selector, GameplayTag navigatorType)
        {
            var settings = GetMarkerSettings(navigatorType);
            return settings.IconTint;
        }

        /// <summary>是否可交互。可覆盖。</summary>
        public virtual bool CanInteract(NarrativeNavigationComponent selector)
        {
            return true;
        }

        /// <summary>在地图中被选中时调用。可覆盖。</summary>
        public virtual void OnSelect(NarrativeNavigationComponent selector)
        {
            OnSelected?.Invoke(selector);
        }

        /// <summary>设置默认域。</summary>
        public void SetDefaultDomains(GameplayTagContainer newMarkerDomain)
        {
            MarkerDomain = new GameplayTagContainer(newMarkerDomain.GetTags());
            RefreshMarker();
        }

        /// <summary>设置 ZOrder。</summary>
        public void SetZOrder(int newZOrder)
        {
            ZOrder = newZOrder;
            RefreshMarker();
        }

        /// <summary>设置显示域。</summary>
        public virtual void SetDomains(GameplayTagContainer newMarkerDomain)
        {
            MarkerDomain = new GameplayTagContainer(newMarkerDomain.GetTags());
            RefreshMarker();
        }

        /// <summary>添加域。</summary>
        public virtual void AddDomains(GameplayTagContainer newMarkerDomains)
        {
            if (newMarkerDomains == null) return;
            foreach (var tag in newMarkerDomains.GetTags())
            {
                MarkerDomain.AddTag(new GameplayTag(tag));
            }
            RefreshMarker();
        }

        /// <summary>移除域。</summary>
        public virtual void RemoveDomains(GameplayTagContainer removeDomains)
        {
            if (removeDomains == null) return;
            foreach (var tag in removeDomains.GetTags())
            {
                MarkerDomain.RemoveTag(new GameplayTag(tag));
            }
            RefreshMarker();
        }

        /// <summary>获取标记变换。</summary>
        public virtual Transform GetMarkerTransform()
        {
            if (ActorOwner != null)
            {
                return ActorOwner.Transform;
            }
            return MarkerTransform;
        }

        /// <summary>获取 ZOrder。</summary>
        public virtual int GetMarkerZOrder() => ZOrder;

        /// <summary>获取标记在地图本地空间的位置。
        /// MapOrigin 为地图中心的世界 XZ（Flax 为 Y-up，地图平面是 XZ），MapPan 为当前地图平移量。</summary>
        public Float2 GetMarkerMapLocalPosition(Float2 mapOrigin, Float2 mapPan)
        {
            var transform = GetMarkerTransform();
            // 将世界 XZ 投影到地图空间（Y 是高度轴，不参与投影）
            float worldX = transform.Translation.X;
            float worldZ = transform.Translation.Z;
            return new Float2(worldX - mapOrigin.X + mapPan.X, worldZ - mapOrigin.Y + mapPan.Y);
        }

        /// <summary>获取标记在绘制空间左上角的位置。
        /// 对应 UE5 UMapMarker::GetMarkerTopLeftLocalPosition —— 用控件几何换算，再减去图标半宽。</summary>
        public Float2 GetMarkerTopLeftLocalPosition(MarkerOnPaintData onPaintData)
        {
            if (onPaintData == null) return Float2.Zero;

            var transform = GetMarkerTransform();
            Float2 markerLocal = NarrativePro.Widgets.NarrativeWidgetHelpers.WorldLocationToPaint(onPaintData, transform.Translation);

            Float2 iconSize = GetMarkerSettings(onPaintData.Domain).IconSize;
            return new Float2(markerLocal.X - iconSize.X * 0.5f, markerLocal.Y - iconSize.Y * 0.5f);
        }

        /// <summary>当 bWantsOnPaint 为 true 时，由地图控件在其 Draw 中调用。对应 UE5 MarkerOnPaint_Implementation。</summary>
        public virtual void MarkerOnPaint(MarkerOnPaintData onPaintData)
        {
            DrawBreadcrumb(onPaintData);
        }

        /// <summary>设置是否绘制面包屑路径。</summary>
        public void SetDrawBreadcrumbs(bool bCanDrawBreadcrumbs)
        {
            bDrawBreadcrumbs = bCanDrawBreadcrumbs;
            if (!bCanDrawBreadcrumbs) CleanupBreadcrumb();
            RefreshMarker();
        }

        /// <summary>
        /// 每帧驱动（由 <see cref="NarrativeNavigationComponent.OnUpdate"/> 调用）。
        /// Flax 无 FTimerManager，改用时间累加实现 UE5 中按 <c>UpdateNavPathRate</c> 的定时刷新。
        /// </summary>
        public virtual void Tick(float deltaTime)
        {
            if (!bDrawBreadcrumbs || !bWantsOnPaint || _ownerNavActor == null) return;

            _pathRefreshTimer += deltaTime;
            float interval = UpdateNavPathRate > 0f ? 1f / UpdateNavPathRate : 0.25f;
            if (_pathRefreshTimer < interval) return;

            _pathRefreshTimer = 0f;
            UpdateBreadcrumbNavPath();
        }

        /// <summary>初始化面包屑路径。对应 UE5 UMapMarker::InitializeBreadcrumb。</summary>
        protected virtual void InitializeBreadcrumb(Actor navActor)
        {
            _ownerNavActor = navActor;
            _cachedLocation = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            _pathRefreshTimer = 0f;
            UpdateBreadcrumbNavPath();
        }

        /// <summary>清理面包屑路径。对应 UE5 UMapMarker::CleanupBreadcrumb。</summary>
        protected virtual void CleanupBreadcrumb()
        {
            _ownerNavActor = null;
            _navPath.Clear();
            _cachedLocation = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            _pathRefreshTimer = 0f;
        }

        /// <summary>
        /// 更新面包屑导航路径。对应 UE5 UMapMarker::UpdateBreadcrumbNavPath：
        /// 使用 <see cref="MapMarkerQueryFilter"/>（把搜索节点上限抬到 8192）求路径，
        /// 再按 <see cref="DistanceBetweenPoints"/> 等弧长重采样为点列。
        /// <para>
        /// Flax 的导航查询是**同步**的（无 FindPathAsync），因此必须依赖 <c>UpdateNavPathRate</c> 限频
        /// 与 <c>UpdateNavDistanceThreshhold</c> 距离门控，避免每帧查询拖慢主线程。
        /// </para>
        /// </summary>
        protected virtual void UpdateBreadcrumbNavPath()
        {
            if (!bDrawBreadcrumbs) return;
            if (_ownerNavActor == null) return;
            if (!bWantsOnPaint) return; // 只有会被绘制时才更新路径

            if (_bPathQueryInFlight) return; // 对应 UE5 的 NavRequestID != INVALID_NAVQUERYID 去重

            Vector3 actorLocation = _ownerNavActor.Position;

            // 只有移动足够远才重新计算
            if (Vector3.Distance(actorLocation, _cachedLocation) < UpdateNavDistanceThreshhold) return;

            if (!NavigatorStatics.IsNavigationReady()) return;

            _cachedLocation = actorLocation;
            Vector3 targetLocation = GetMarkerTransform().Translation;

            // 远距离标记专用过滤器（对应 UE5 GetQueryFilter(..., UMapMarkerQueryFilter::StaticClass())）
            var filter = MapMarkerQueryFilter.GetQueryFilter(_ownerNavActor);
            if (filter != null && !filter.IsValidFilter()) filter = null;

            _bPathQueryInFlight = true;
            bool bFound = NavigatorStatics.TryFindPath(actorLocation, targetLocation, filter, out var corners);
            _bPathQueryInFlight = false;

            if (!bFound || corners == null || corners.Length == 0)
            {
                // 求不到路径时清空，避免继续绘制过期的面包屑
                _navPath.Clear();
                RefreshMarker();
                return;
            }

            if (NavigatorStatics.ResamplePath(corners, DistanceBetweenPoints, out var resampled))
            {
                GeneratePath(resampled);
                RefreshMarker();
            }
        }

        /// <summary>生成路径（写入本标记的面包屑点列）。对应 UE5 UMapMarker::GeneratePath。</summary>
        protected virtual void GeneratePath(List<Vector3> navPath)
        {
            _navPath.Clear();
            if (navPath != null)
            {
                _navPath.AddRange(navPath);
            }
        }

        /// <summary>取面包屑点列（只读），供 UI 绘制使用。</summary>
        public IReadOnlyList<Vector3> GetBreadcrumbPath() => _navPath;

        /// <summary>
        /// 取应当绘制的面包屑世界坐标点列。
        /// 对应 UE5 DrawBreadcrumb 里的 FindNearest 逻辑：
        /// 起点用观察者当前位置，然后从"观察者前方"的那个采样点开始连线，
        /// 避免玩家身后的路径段被画出来。
        /// </summary>
        public List<Vector3> GetBreadcrumbWorldPathForViewer(Vector3 viewerLocation)
        {
            var result = new List<Vector3>();
            if (_navPath.Count == 0) return result;

            result.Add(viewerLocation);

            float nearestAt = NarrativePro.Widgets.NarrativeWidgetHelpers.FindNearestDistanceAlongPath(_navPath, viewerLocation, out _);

            // 累计弧长参数化：找出最近点，再判断"下一个采样点"是否已在观察者前方
            float accumulated = 0f;
            float relevantAt = nearestAt;
            if (_navPath.Count >= 2)
            {
                Vector3 nearestPoint = EvaluatePathAt(nearestAt);
                Vector3 nextPoint = EvaluatePathAt(nearestAt + DistanceBetweenPoints);
                Vector3 viewerToNearest = nearestPoint - viewerLocation;
                if (viewerToNearest.LengthSquared > 0.0001f) viewerToNearest = Vector3.Normalize(viewerToNearest);
                Vector3 nearestToNext = nearestPoint - nextPoint;
                if (nearestToNext.LengthSquared > 0.0001f) nearestToNext = Vector3.Normalize(nearestToNext);

                if (Vector3.Dot(viewerToNearest, nearestToNext) < 0f)
                {
                    relevantAt = nearestAt + DistanceBetweenPoints;
                }
            }

            // 采样点等距分布，因此起点索引 = ceil(relevantAt / DistanceBetweenPoints)
            float spacing = DistanceBetweenPoints > 0f ? DistanceBetweenPoints : 1f;
            int startIndex = (int)Math.Ceiling(relevantAt / spacing);
            if (startIndex < 0) startIndex = 0;

            for (int i = startIndex; i < _navPath.Count; i++)
            {
                result.Add(_navPath[i]);
            }

            // 末尾补上目标标记本身
            result.Add(GetMarkerTransform().Translation);
            return result;
        }

        /// <summary>按弧长参数求路径上的点（对应 UE5 FPathCurve::Eval）。</summary>
        private Vector3 EvaluatePathAt(float distanceAlong)
        {
            if (_navPath.Count == 0) return Vector3.Zero;
            if (_navPath.Count == 1 || distanceAlong <= 0f) return _navPath[0];

            float accumulated = 0f;
            for (int i = 1; i < _navPath.Count; i++)
            {
                float segmentLength = Vector3.Distance(_navPath[i - 1], _navPath[i]);
                if (accumulated + segmentLength >= distanceAlong)
                {
                    float t = segmentLength <= 0f ? 0f : (distanceAlong - accumulated) / segmentLength;
                    return Vector3.Lerp(_navPath[i - 1], _navPath[i], t);
                }
                accumulated += segmentLength;
            }

            return _navPath[_navPath.Count - 1];
        }

        /// <summary>
        /// 绘制面包屑虚线。对应 UE5 UMapMarker::DrawBreadcrumb + UArsenalStatics::DrawDashedLine。
        /// 必须在地图控件的 Draw 回调里被调用（Flax 的 Render2D 是即时模式，且只能在主线程）。
        /// </summary>
        protected virtual void DrawBreadcrumb(MarkerOnPaintData onPaintData)
        {
            if (!bDrawBreadcrumbs) return;
            if (onPaintData == null) return;
            if (_ownerNavActor == null) return;
            if (_navPath.Count == 0) return;

            var worldPath = GetBreadcrumbWorldPathForViewer(_ownerNavActor.Position);

            var paintPoints = new List<Float2>(worldPath.Count);
            for (int i = 0; i < worldPath.Count; i++)
            {
                paintPoints.Add(NarrativePro.Widgets.NarrativeWidgetHelpers.WorldLocationToPaint(onPaintData, worldPath[i]));
            }

            var segments = NarrativePro.Widgets.NarrativeWidgetHelpers.BuildDashedLineSegments(paintPoints, BreadcrumbDashLength, BreadcrumbDashLength);

            // 裁剪到地图几何内，防止长路径画到相邻面板上
            var clip = onPaintData.MapGeometry;
            Render2D.PushClip(ref clip);
            for (int i = 0; i < segments.Count; i++)
            {
                Render2D.DrawLine(segments[i][0], segments[i][1], BreadcrumbColor, BreadcrumbThickness);
            }
            Render2D.PopClip();
        }
    }
}

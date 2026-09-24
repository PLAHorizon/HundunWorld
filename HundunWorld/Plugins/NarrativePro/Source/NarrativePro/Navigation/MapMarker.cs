using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Items;

namespace NarrativePro.Navigation
{
    /// <summary>
    /// 区域绘制数据（多边形顶点）。适配 UE5 FZoneDrawData。
    /// </summary>
    [Serializable]
    public class ZoneDrawData
    {
        public List<Vector3> Points { get; set; } = new List<Vector3>();
    }

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
    /// Flax 中使用 Float2 替代 FVector2D，使用 Rectangle 替代 FGeometry。
    /// </summary>
    public class MarkerOnPaintData
    {
        /// <summary>父控件绘制矩形</summary>
        public Rectangle ParentGeometry { get; set; }
        /// <summary>地图控件绘制矩形</summary>
        public Rectangle MapGeometry { get; set; }
        /// <summary>图标控件绘制矩形</summary>
        public Rectangle IconGeometry { get; set; }
        /// <summary>地图中心在世界空间的位置（X,Y 平面）</summary>
        public Float2 MapOrigin { get; set; } = new Float2(float.MaxValue, float.MaxValue);
        /// <summary>当前地图平移量（地图空间）</summary>
        public Float2 MapPan { get; set; } = new Float2(float.MaxValue, float.MaxValue);
        /// <summary>此标记显示的导航器域</summary>
        public GameplayTag Domain { get; set; } = GameplayTag.None;
    }

    /// <summary>
    /// 地图标记基类。适配 UE5 UMapMarker。
    /// UMapMarker 是 UObject（非 UActorComponent），但需要 GetWorld，因此 Flax 中作为普通 C# 类，
    /// 通过 Owner 字段引用 Actor 来获取场景。
    /// 移除复制/RPC，改为本地逻辑 + 事件回调。
    /// 面包屑路径（Breadcrumb）依赖 NavMesh，Flax 中简化为直线路径或留待后续接入。
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

        /// <summary>获取标记在绘制空间左上角的位置。</summary>
        public Float2 GetMarkerTopLeftLocalPosition(MarkerOnPaintData onPaintData)
        {
            Float2 markerLocal = GetMarkerMapLocalPosition(onPaintData.MapOrigin, onPaintData.MapPan);
            // 转换到绘制空间（相对于地图控件的位置）
            Float2 mapTopLeft = onPaintData.MapGeometry.Location;
            Float2 iconSize = GetMarkerSettings(onPaintData.Domain).IconSize;
            return new Float2(mapTopLeft.X + markerLocal.X - iconSize.X * 0.5f, mapTopLeft.Y + markerLocal.Y - iconSize.Y * 0.5f);
        }

        /// <summary>当 bWantsOnPaint 为 true 时，由 UI 控件调用。对应 UE5 MarkerOnPaint_Implementation。</summary>
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
        /// 使用 <see cref="FlaxEngine.Navigation.FindPath"/> 求路径后按 <see cref="DistanceBetweenPoints"/> 重采样。
        /// <para>
        /// Flax 的导航查询是**同步**的（无 FindPathAsync），因此必须依赖 <c>UpdateNavPathRate</c> 限频
        /// 与 <c>UpdateNavDistanceThreshhold</c> 距离门控，避免每帧查询拖慢主线程。
        /// </para>
        /// </summary>
        protected virtual void UpdateBreadcrumbNavPath()
        {
            if (!bDrawBreadcrumbs) return;
            if (_ownerNavActor == null) return;
            if (!bWantsOnPaint) return;

            Vector3 actorLocation = _ownerNavActor.Position;

            // 只有移动足够远才重新计算
            if (Vector3.Distance(actorLocation, _cachedLocation) < UpdateNavDistanceThreshhold) return;

            if (!NavigatorStatics.IsNavigationReady()) return;

            _cachedLocation = actorLocation;
            Vector3 targetLocation = GetMarkerTransform().Translation;

            if (!NavigatorStatics.TryFindPath(actorLocation, targetLocation, out var corners)) return;

            if (NavigatorStatics.ResamplePath(corners, DistanceBetweenPoints, out var resampled))
            {
                GeneratePath(resampled);
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

        /// <summary>绘制面包屑路径。由 UI 控件在 <see cref="MarkerOnPaint"/> 中调用。</summary>
        protected virtual void DrawBreadcrumb(MarkerOnPaintData onPaintData)
        {
            // 默认不绘制：Flax 无 FPaintContext，虚线绘制由地图 UI 控件按 GetBreadcrumbPath() 完成
        }
    }
}

using FlaxEngine;
using NarrativePro.Navigation;

namespace HundunWorld.Game.Navigation
{
    /// <summary>
    /// 点击移动的目的地标记：向 NarrativePro 的标记管线注册，主要用途是**取它的面包屑路径数据**。
    /// <para>
    /// 只派生不改插件：<see cref="MapMarker.MarkerTransform"/>、<see cref="MapMarker.bWantsOnPaint"/>、
    /// <see cref="MapMarker.DistanceBetweenPoints"/> 等都是 public，而受保护的
    /// <see cref="MapMarker.InitializeBreadcrumb"/> 在派生类里可正常调用，
    /// 因此整条"目的地 + 面包屑"能力无需修改 NarrativePro（改插件要 Flax.Build + 编辑器重载，成本高）。
    /// </para>
    /// <para>
    /// 注意显示通路：本项目的 Ink 小地图 / 世界地图 / 罗盘**并不枚举插件的 Markers 集合**，
    /// 它们只认 <c>PluginNavigationBridge.TryGetMarkers</c> 这一个出口。所以目的地的地图显示由该桥
    /// 追加一条 <c>MapMarkerView</c>（Tag = MoveDestinationTag）完成，本标记负责的是
    /// 面包屑路径的计算与持有（供 <c>TryGetMoveDestinationPath</c> 取用，以及插件自带地图控件将来直接消费）。
    /// </para>
    /// <para>
    /// 面包屑由 <see cref="NarrativeNavigationComponent.OnUpdate"/> 统一 <c>Tick</c> 驱动：
    /// 按 <see cref="MapMarker.UpdateNavPathRate"/> 限频、以本地玩家位置为起点重算到本标记位置，
    /// 因此路径随角色前进自然缩短。角色跟随与它共用 <see cref="NavigatorStatics"/>，两边结果一致。
    /// </para>
    /// </summary>
    public sealed class ClickToMoveDestinationMarker : MapMarker
    {
        /// <summary>面包屑点间距（世界单位，Flax 为厘米；300 = 3 米）。</summary>
        private const float BreadcrumbSpacing = 300f;

        /// <summary>角色移动超过该距离才重算面包屑（厘米，200 = 2 米）。</summary>
        private const double BreadcrumbRefreshDistance = 200.0;

        /// <summary>面包屑重算频率（次/秒）。Flax 的导航查询是同步的，不能按帧查。</summary>
        private const float BreadcrumbRefreshRate = 4f;

        public ClickToMoveDestinationMarker()
        {
            DefaultMarkerSettings.MarkerTitleText = "移动目标";
            DefaultMarkerSettings.IconSize = new Vector2(24f, 24f);

            // 与 CustomWaypointMarker 同域：小地图、世界地图、罗盘、屏幕空间都能看到
            MarkerDomain.AddTag(NavigatorGameplayTags.NavigatorTypes_Worldmap);
            MarkerDomain.AddTag(NavigatorGameplayTags.NavigatorTypes_Minimap);
            MarkerDomain.AddTag(NavigatorGameplayTags.NavigatorTypes_Compass);
            MarkerDomain.AddTag(NavigatorGameplayTags.NavigatorTypes_Screenspace);

            bDrawBreadcrumbs = true;
            // Tick 与 UpdateBreadcrumbNavPath 都要求 bWantsOnPaint，否则面包屑不会刷新
            bWantsOnPaint = true;
            DistanceBetweenPoints = BreadcrumbSpacing;
            UpdateNavDistanceThreshhold = BreadcrumbRefreshDistance;
            UpdateNavPathRate = BreadcrumbRefreshRate;
        }

        /// <summary>把面包屑的起点绑定到本地玩家 Actor（角色移动时路径随之重算、逐段缩短）。</summary>
        public void BindBreadcrumbsTo(Actor navActor)
        {
            if (navActor == null) return;
            InitializeBreadcrumb(navActor);
        }

        /// <summary>更新目的地位置并立即重算一次面包屑。</summary>
        public void SetDestination(Vector3 worldLocation)
        {
            MarkerTransform = new Transform(worldLocation);
            RefreshMarker();
        }
    }
}

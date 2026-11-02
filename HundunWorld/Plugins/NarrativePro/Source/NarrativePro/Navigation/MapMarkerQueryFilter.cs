using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.Navigation
{
    /// <summary>
    /// 导航查询过滤器基类。适配 UE5 UNavigationQueryFilter / FNavigationQueryFilter 的可配置面。
    /// <para>
    /// Flax 1.12 的托管导航 API 不接受查询过滤器（<c>Navigation.FindPath</c> 只有起止点重载），
    /// 因此这里把过滤器做成端口自己的参数对象，由
    /// <see cref="NavigatorStatics.TryFindPath(FlaxEngine.Vector3,FlaxEngine.Vector3,NarrativeNavQueryFilter,out FlaxEngine.Vector3[])"/>
    /// 与 <see cref="NavAreaSystem"/> 的代价搜索实际消费其字段。
    /// </para>
    /// </summary>
    [Serializable]
    public class NarrativeNavQueryFilter
    {
        /// <summary>UE5 FNavigationQueryFilter 的默认搜索节点上限。</summary>
        public const int DefaultMaxSearchNodes = 210;

        /// <summary>路径搜索最多扩展的节点数（对应 UE5 SetMaxSearchNodes）。</summary>
        public int MaxSearchNodes = DefaultMaxSearchNodes;

        /// <summary>是否允许使用区域代价（道路优先）二次搜索。</summary>
        public bool bAllowAreaCostPathfinding = true;

        /// <summary>是否允许把路径裁到目标（对应 UE5 bFindNearestNode）。</summary>
        public bool bFindNearestNode = false;

        /// <summary>过滤器是否可用。对应 UE5 IsValidFilter。</summary>
        public virtual bool IsValidFilter() => MaxSearchNodes > 0;

        /// <summary>
        /// 初始化过滤器。对应 UE5 InitializeFilter(NavData, Querier, Filter)。
        /// </summary>
        /// <param name="navData">场景导航数据（Flax 中为 NavMesh Actor，可能为 null）</param>
        /// <param name="querier">查询发起者 Actor</param>
        /// <param name="filter">被配置的运行时过滤器状态</param>
        public virtual void InitializeFilter(NavMesh navData, Actor querier, NarrativeNavQueryFilter filter)
        {
            if (filter == null) return;
            filter.MaxSearchNodes = MaxSearchNodes;
            filter.bAllowAreaCostPathfinding = bAllowAreaCostPathfinding;
            filter.bFindNearestNode = bFindNearestNode;
        }

        /// <summary>生成一个由本配置初始化的运行时过滤器。对应 UE5 UNavigationQueryFilter::GetQueryFilter。</summary>
        public NarrativeNavQueryFilter CreateFilter(NavMesh navData, Actor querier)
        {
            var filter = new NarrativeNavQueryFilter();
            InitializeFilter(navData, querier, filter);
            return filter;
        }
    }

    /// <summary>
    /// 地图标记导航查询过滤器。适配 UE5 UMapMarkerQueryFilter（继承 UNavigationQueryFilter）。
    /// UE5 唯一的实际行为就是把搜索节点上限提到 8192，以便为远距离标记求出完整面包屑路径。
    /// </summary>
    public class MapMarkerQueryFilter : NarrativeNavQueryFilter
    {
        /// <summary>UE5 UMapMarkerQueryFilter::InitializeFilter 中设定的搜索节点上限。</summary>
        public const int LongRangeMaxSearchNodes = 8192;

        /// <summary>是否支持更长范围的导航（对应 UE5 自定义过滤器的核心能力）</summary>
        public bool bSupportsLongerRangeNavigation = true;

        /// <summary>导航搜索半径扩展（与世界单位相同）。用于远距离标记的接驳搜索半径。</summary>
        public float ExtendedSearchRadius = 50000f;

        public MapMarkerQueryFilter()
        {
            MaxSearchNodes = LongRangeMaxSearchNodes;
        }

        /// <summary>
        /// 初始化过滤器：把搜索节点上限抬到 8192，让 <see cref="NavAreaSystem"/> 的 A*
        /// 有足够的节点预算去求长距离面包屑。对应 UE5 InitializeFilter 的 SetMaxSearchNodes(8192)。
        /// </summary>
        public override void InitializeFilter(NavMesh navData, Actor querier, NarrativeNavQueryFilter filter)
        {
            if (filter == null) return;

            base.InitializeFilter(navData, querier, filter);

            if (bSupportsLongerRangeNavigation)
            {
                filter.MaxSearchNodes = LongRangeMaxSearchNodes;
            }
        }

        /// <summary>过滤器是否有效。对应 UE5 的默认行为：始终有效。</summary>
        public override bool IsValidFilter() => true;

        private static readonly MapMarkerQueryFilter _default = new MapMarkerQueryFilter();

        /// <summary>共享默认实例（对应 UE5 传给 GetQueryFilter 的 UMapMarkerQueryFilter::StaticClass()）。</summary>
        public static MapMarkerQueryFilter Default => _default;

        /// <summary>
        /// 取当前场景 NavMesh 上的地图标记查询过滤器。
        /// 对应 UE5 <c>UNavigationQueryFilter::GetQueryFilter(*NavData, OwnerNavActor.Get(), UMapMarkerQueryFilter::StaticClass())</c>。
        /// </summary>
        public static NarrativeNavQueryFilter GetQueryFilter(Actor querier)
        {
            NavMesh navData = FindDefaultNavData();
            return _default.CreateFilter(navData, querier);
        }

        /// <summary>取场景默认导航数据（对应 UE5 NavigationSystem::GetDefaultNavDataInstance）。</summary>
        public static NavMesh FindDefaultNavData()
        {
            try
            {
                var navMeshes = Level.GetActors<NavMesh>(false);
                if (navMeshes != null && navMeshes.Length > 0) return navMeshes[0];
            }
            catch
            {
                // 场景无导航数据
            }
            return null;
        }
    }
}

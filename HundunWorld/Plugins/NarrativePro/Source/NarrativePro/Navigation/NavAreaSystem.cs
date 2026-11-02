using System;
using System.Collections.Generic;
using FlaxEngine;

namespace NarrativePro.Navigation
{
    /// <summary>
    /// 导航区域系统：维护场景中所有 <see cref="NavAreaModifier"/> 的代价场，
    /// 并提供"代价感知"的路径优化，供 <see cref="NavigatorStatics.TryFindPath"/> 消费。
    /// <para>
    /// <b>为什么需要它</b>：UE5 的 UNavArea_Road 通过 Recast 烘焙把 AreaCost 写进多边形，
    /// 寻路天然偏向道路。Flax 1.12 托管层只暴露
    /// <c>Navigation.FindPath(Vector3, Vector3, out Vector3[])</c>，既没有 query filter
    /// 也没有 area cost 入口（已核对 <c>FlaxEngine.Navigation</c> 的全部公开方法）。
    /// 因此这里用一层自建的道路代价图把这个能力补回来：
    /// </para>
    /// <para>
    /// 1) 引擎寻路给出几何上可通行的走廊（碰撞有效性由引擎保证）；
    /// 2) 本系统在道路采样点图上跑 A*，边代价 = 距离 × 沿途平均区域代价，
    ///    每条边都用 <c>Navigation.TestPath</c> 复核可通行性；
    /// 3) 只有当道路路径的总代价低于引擎路径时才采用它。
    /// </para>
    /// <para>
    /// 道路图是静态几何，只在区域体积增删/移动时重建；单次查询只付出
    /// 起点/终点到少数邻近节点的接驳开销。
    /// </para>
    /// </summary>
    public static class NavAreaSystem
    {
        /// <summary>道路图上两个节点之间允许的最大直连距离（世界单位）。</summary>
        public static float MaxLinkLength = 4000f;

        /// <summary>起点/终点到道路节点的最大接驳距离。</summary>
        public static float EntryDistance = 4000f;

        /// <summary>沿一条边采样多少个点来估算平均区域代价。</summary>
        public static int SegmentCostSamples = 5;

        /// <summary>是否启用代价优化二次搜索（关掉则完全沿用引擎路径）。</summary>
        public static bool bUseCostAwarePathfinding = true;

        private static readonly List<NavAreaModifier> _modifiers = new List<NavAreaModifier>();
        private static readonly List<INavAreaVolume> _volumes = new List<INavAreaVolume>();
        private static readonly NavCostGraph _graph = new NavCostGraph();
        private static readonly EngineAreaField _field = new EngineAreaField();
        private static bool _graphDirty = true;

        /// <summary>
        /// 引擎侧的代价场注入实现：区域代价取本系统的区域体积，可通行性用 Flax 导航网格复核。
        /// 纯算法（边代价 / 启发式 / A*）住在 <see cref="NavCostGraph"/>，harness 用同一份实现跑自己的数据。
        /// </summary>
        private sealed class EngineAreaField : INavAreaField
        {
            public float GetAreaCostAt(NavCostPoint location)
            {
                return NavAreaSystem.GetAreaCostAt(new Vector3(location.X, location.Y, location.Z));
            }

            public bool IsReachable(NavCostPoint a, NavCostPoint b)
            {
                return NavigatorStatics.TestPath(new Vector3(a.X, a.Y, a.Z), new Vector3(b.X, b.Y, b.Z));
            }
        }

        /// <summary>把一个 <see cref="NavAreaModifier"/>（Script）适配成代价场纯函数需要的体积形态。</summary>
        private sealed class ModifierVolume : INavAreaVolume
        {
            private readonly NavAreaModifier _modifier;

            public ModifierVolume(NavAreaModifier modifier)
            {
                _modifier = modifier;
            }

            public bool IsEnabled => _modifier != null && _modifier.IsEnabledInHierarchy;

            public float AreaCost => _modifier != null ? _modifier.GetAreaCost() : 1f;

            public bool Contains(NavCostPoint location)
            {
                return _modifier != null
                       && _modifier.ContainsPoint(new Vector3(location.X, location.Y, location.Z));
            }
        }

        /// <summary>注册表变化后重建体积适配器（Register/Unregister/Clear 的唯一同步点）。</summary>
        private static void RebuildVolumes()
        {
            _volumes.Clear();
            for (int i = 0; i < _modifiers.Count; i++)
            {
                if (_modifiers[i] == null) continue;
                _volumes.Add(new ModifierVolume(_modifiers[i]));
            }
        }

        private static NavCostPoint ToPoint(Vector3 v) => new NavCostPoint(v.X, v.Y, v.Z);

        private static Vector3 ToVector(NavCostPoint p) => new Vector3(p.X, p.Y, p.Z);

        /// <summary>当前注册的区域体积数量。</summary>
        public static int ModifierCount => _modifiers.Count;

        /// <summary>当前代价图节点数量。</summary>
        public static int NodeCount => _graph.NodeCount;

        /// <summary>把当前公开可调参数打包给纯算法层。</summary>
        private static NavCostSearchOptions MakeOptions()
        {
            return new NavCostSearchOptions
            {
                MaxLinkLength = MaxLinkLength,
                EntryDistance = EntryDistance,
                SegmentCostSamples = SegmentCostSamples,
                MaxEntryNodes = 6,
                MaxEntryPathTests = 12,
            };
        }

        /// <summary>注册一个区域体积。</summary>
        public static void Register(NavAreaModifier modifier)
        {
            if (modifier == null || _modifiers.Contains(modifier)) return;
            _modifiers.Add(modifier);
            RebuildVolumes();
            _graphDirty = true;
        }

        /// <summary>注销一个区域体积。</summary>
        public static void Unregister(NavAreaModifier modifier)
        {
            if (modifier == null) return;
            if (_modifiers.Remove(modifier))
            {
                RebuildVolumes();
                _graphDirty = true;
            }
        }

        /// <summary>令道路代价图失效（体积移动或配置变化时调用）。</summary>
        public static void InvalidateGraph()
        {
            _graphDirty = true;
        }

        /// <summary>清空全部注册（切场景时调用）。</summary>
        public static void Clear()
        {
            _modifiers.Clear();
            RebuildVolumes();
            _graph.Clear();
            _graphDirty = true;
        }

        /// <summary>
        /// 某点的有效区域代价：取包含该点的区域里代价最小的那个（道路 0.5 &lt; 地面 1.0）；
        /// 若该点被不可通行区域（NavArea.Null，代价 float.MaxValue 或非正代价）覆盖且没有任何
        /// 更低价的可通行区域显式覆盖，则返回 <see cref="float.MaxValue"/> ——
        /// 阻断因此会沿段代价 / 连边 / 路径代价一路传播，而不只在代价图节点层面生效。
        /// 规则本体在 <see cref="NavAreaCostField.ResolveCostAt"/>（纯实现，harness 驱动同一份），
        /// 这里只把区域体积适配成它要的 <see cref="INavAreaVolume"/> 形态。
        /// </summary>
        public static float GetAreaCostAt(Vector3 location)
        {
            return NavAreaCostField.ResolveCostAt(ToPoint(location), NavArea.Default.DefaultCost, _volumes);
        }

        /// <summary>
        /// 一条线段上的平均区域代价（略偏向最小值，避免长边只靠端点占便宜）。
        /// 采样/折中规则本身在 <see cref="NavCostGraph.SegmentAverageCost"/>（纯实现，harness 驱动同一份）。
        /// </summary>
        public static float GetSegmentAverageCost(Vector3 a, Vector3 b)
        {
            return NavCostGraph.SegmentAverageCost(ToPoint(a), ToPoint(b), _field, SegmentCostSamples);
        }

        /// <summary>
        /// 同一规则的纯点形态：供 <see cref="NavCostGraph.PathCost"/> 直接以委托形式调用，
        /// 避免 Vector3 版本来回转换。
        /// </summary>
        public static float GetSegmentAverageCost(NavCostPoint a, NavCostPoint b)
        {
            return NavCostGraph.SegmentAverageCost(a, b, _field, SegmentCostSamples);
        }

        /// <summary>重建道路代价图（惰性）。</summary>
        private static void EnsureGraph()
        {
            if (!_graphDirty) return;

            _graph.Clear();

            // 1) 收集所有区域体积的中心线采样点作为节点
            for (int i = 0; i < _modifiers.Count; i++)
            {
                var modifier = _modifiers[i];
                if (modifier == null || !modifier.bIncludeInCostGraph) continue;
                if (!modifier.IsEnabledInHierarchy) continue;

                var samples = modifier.GetCostSamplePoints();
                float cost = modifier.GetAreaCost();

                for (int s = 0; s < samples.Count; s++)
                {
                    _graph.AddNode(ToPoint(samples[s]), cost);
                }
            }

            // 2) 连边：距离阈值内、引擎确认两点之间存在导航路径、且沿途不穿越不可通行区域
            _graph.BuildLinks(_field, MaxLinkLength, SegmentCostSamples);

            _graphDirty = false;
        }

        /// <summary>
        /// 在道路代价图上求一条从 start 到 end 的代价最优路径（不含 start / end 本身）。
        /// 搜索本体在 <see cref="NavCostGraph.TryFindPath"/>（纯实现，harness 驱动同一份）；
        /// 这里只做引擎侧的取材：区域体积 → 节点、Navigation.TestPath → 连边与接驳复核。
        /// </summary>
        /// <param name="start">起点</param>
        /// <param name="end">终点</param>
        /// <param name="maxSearchNodes">A* 最多扩展的节点数（来自查询过滤器，对应 UE5 FNavigationQueryFilter::SetMaxSearchNodes）</param>
        /// <param name="path">输出道路拐点序列</param>
        /// <param name="outCost">输出该路径的总代价（用于和引擎路径比较）</param>
        /// <returns>是否求出道路路径</returns>
        public static bool TryFindCostAwarePath(Vector3 start, Vector3 end, int maxSearchNodes, out List<Vector3> path, out float outCost)
        {
            path = null;
            outCost = float.MaxValue;

            if (!bUseCostAwarePathfinding) return false;
            if (_modifiers.Count == 0) return false;
            if (!NavigatorStatics.IsNavigationReady()) return false;

            EnsureGraph();
            if (_graph.NodeCount == 0) return false;

            var options = MakeOptions();
            var startLinks = _graph.CollectEntryLinks(ToPoint(start), _field, options);
            var endLinks = _graph.CollectEntryLinks(ToPoint(end), _field, options);
            if (startLinks.Count == 0 || endLinks.Count == 0) return false;

            var result = _graph.TryFindPath(startLinks, endLinks, maxSearchNodes);
            if (!result.bFound || result.Nodes == null) return false;

            var reversed = new List<Vector3>(result.Nodes.Count);
            for (int i = 0; i < result.Nodes.Count; i++)
            {
                reversed.Add(ToVector(_graph.GetLocation(result.Nodes[i])));
            }

            path = reversed;
            outCost = result.Cost;
            return path.Count > 0;
        }
    }
}

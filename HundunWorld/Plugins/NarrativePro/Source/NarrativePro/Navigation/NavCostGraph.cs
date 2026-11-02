// 模块关系说明：
// 本文件属于 NarrativePro.Navigation（UE5 Navigator/NavArea 移植）。
//
// 为什么需要它：道路优先寻路的「代价图 + A*」原本和引擎调用长在一起
// （NavAreaModifier 是 Script、连边要 Navigation.TestPath、区域代价要读体积），
// 因此无法在没有引擎的进程外被验证 —— 而恰恰是这一层承载了 UNavArea_Road DefaultCost=0.5
// 这条会被写错的规则，以及「不可通行区域不能被当成 0 代价穿过」「启发式不得高估」
// 「目标不可达时必须在有限次展开内返回无解」这些终止性/正确性约束。
//
// 本文件把这一层抽成纯托管实现（不引用任何 FlaxEngine 类型）：
//   - 节点/连边/边代价/启发式/A* 主体都在 NavCostGraph 里；
//   - 引擎相关的两件事通过 INavAreaField 注入：某点的区域代价（NavAreaSystem.GetAreaCostAt）
//     与两点之间是否真的可通行（FlaxEngine.Navigation.TestPath）。
// 实时通路（NavAreaSystem.TryFindCostAwarePath / NavigatorStatics.GetPathCost）喂进这两个实现，
// harness 喂进纯托管替身 —— 两侧跑的是同一份代价与搜索代码，测试不会退化成复刻。

using System;
using System.Collections.Generic;

namespace NarrativePro.Navigation
{
    /// <summary>
    /// 一片导航区域体积的最小可注入形态：实时侧把 <see cref="NavAreaModifier"/>（Script，
    /// 无引擎环境造不出来）包一层适配器喂进来，harness 直接喂纯数据盒子。
    /// 「覆盖该点的所有区域里取最小代价」这条规则本身住在 <see cref="NavAreaCostField"/>，
    /// 两侧共用同一份实现。
    /// </summary>
    public interface INavAreaVolume
    {
        /// <summary>体积当前是否生效（对应 Actor 层级启用状态）。</summary>
        bool IsEnabled { get; }

        /// <summary>该体积赋予的区域代价。</summary>
        float AreaCost { get; }

        /// <summary>世界点是否落在体积内。</summary>
        bool Contains(NavCostPoint location);
    }

    /// <summary>
    /// 区域代价场的纯求值：某点的有效代价 = 覆盖它的所有生效区域里的最小代价，
    /// 没有任何区域覆盖时用默认地面代价（对应 UE5 NavArea 的多边形加权）。
    /// </summary>
    public static class NavAreaCostField
    {
        /// <summary>空区域集合（场景里还没有任何 NavAreaModifier 时的常态）。</summary>
        public static readonly IReadOnlyList<INavAreaVolume> EmptyVolumes = new INavAreaVolume[0];

        /// <summary>
        /// UE5 语义下的「不可通行代价」：区域代价为非正数或达到不可达哨兵时，
        /// 覆盖点一律按不可通行处理。
        /// 对应引擎侧：UNavArea_Null 的 DefaultCost = FLT_MAX 且 AreaFlags = 0
        /// （UE 5.8 Engine/Source/Runtime/NavigationSystem/Private/NavAreas/NavArea_Null.cpp:11-12），
        /// Recast 把不可通行区域的寻路代价置为 RECAST_UNWALKABLE_POLY_COST = FLT_MAX
        /// （Private/NavMesh/RecastNavMesh.cpp:457-459 + Public/NavMesh/RecastNavMesh.h:26），
        /// 并在默认过滤器注释里明确「AreaFlags == 0（如 NavArea_Null）应视为 unwalkable」
        /// （Private/NavMesh/RecastNavMesh.cpp:1035-1037）。
        /// </summary>
        public static bool IsImpassableCost(float cost)
        {
            return cost <= 0f || cost >= NavCostGraph.Unreachable;
        }

        /// <summary>
        /// 求某点的有效区域代价。取最小值意味着道路（0.5）永远优先于地面（1.0）；
        /// 而不可通行区域（<see cref="IsImpassableCost"/>，如 NavArea.Null）会**向上阻断**：
        /// 只要该点被任一生效的不可通行体积覆盖、且没有任何显式可通行区域在同一以更低代价覆盖它，
        /// 返回 <see cref="NavCostGraph.Unreachable"/>，使段代价 / 边连边 / 路径代价 / 接驳全部绕开该点
        /// （对应 UE5「blocked 区域代价 = FLT_MAX，寻路穿不过去」，而不是被 min 归零后白嫖穿过）。
        /// 显式铺在阻断体积里的可通行区域（如 Null 区域中一段路）按更低价可通行 ——
        /// 等价于 UE5 烘焙时后写入的 modifier 覆写多边形区域标记。
        /// 节点级的硬阻断（AddNode 的 blocked 判定）依然独立生效，两者叠加。
        /// </summary>
        public static float ResolveCostAt(NavCostPoint location, float defaultCost, IReadOnlyList<INavAreaVolume> volumes)
        {
            float best = defaultCost;
            if (volumes == null) return best;

            bool blockedCoverage = false;
            bool passableOverride = false;

            for (int i = 0; i < volumes.Count; i++)
            {
                var volume = volumes[i];
                if (volume == null || !volume.IsEnabled) continue;
                if (!volume.Contains(location)) continue;

                float cost = volume.AreaCost;
                if (IsImpassableCost(cost))
                {
                    blockedCoverage = true;
                    continue;
                }

                if (cost < best)
                {
                    best = cost;
                    passableOverride = true;
                }
            }

            if (blockedCoverage && !passableOverride) return NavCostGraph.Unreachable;
            return best;
        }
    }

    /// <summary>代价图用的纯三维点（不依赖 FlaxEngine.Vector3，可在无引擎环境构造/运算）。</summary>
    public struct NavCostPoint
    {
        public float X;
        public float Y;
        public float Z;

        public NavCostPoint(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }
    }

    /// <summary>
    /// 代价场的注入点：把「引擎侧才知道的两件事」交给调用方，代价算法本身保持纯托管。
    /// 实时实现是 <see cref="NavAreaSystem"/>（区域体积 + Navigation.TestPath），
    /// harness 用纯数据实现同一接口。
    /// </summary>
    public interface INavAreaField
    {
        /// <summary>某点的有效区域代价（道路 0.5 &lt; 地面 1.0，不可通行 <see cref="float.MaxValue"/>）。</summary>
        float GetAreaCostAt(NavCostPoint location);

        /// <summary>两点之间是否真的存在可通行的导航路径（引擎侧用 Navigation.TestPath 复核）。</summary>
        bool IsReachable(NavCostPoint a, NavCostPoint b);
    }

    /// <summary>起点/终点接到代价图节点上的一条接驳边。</summary>
    public struct NavCostEntryLink
    {
        /// <summary>接到的节点下标。</summary>
        public int Node;

        /// <summary>查询点到该节点的直线距离。</summary>
        public float Distance;

        /// <summary>接驳段上的平均区域代价。</summary>
        public float AverageCost;
    }

    /// <summary>代价图搜索的可调参数（实时侧取自 <see cref="NavAreaSystem"/> 的同名公开静态字段）。</summary>
    public struct NavCostSearchOptions
    {
        /// <summary>两个节点之间允许的最大直连距离。</summary>
        public float MaxLinkLength;

        /// <summary>查询点到道路节点的最大接驳距离。</summary>
        public float EntryDistance;

        /// <summary>估算一段平均区域代价时采样多少个点。</summary>
        public int SegmentCostSamples;

        /// <summary>最多接驳多少个道路节点。</summary>
        public int MaxEntryNodes;

        /// <summary>接驳阶段最多做多少次可通行性复核。</summary>
        public int MaxEntryPathTests;
    }

    /// <summary>A* 一次搜索的结果（代价与展开次数，展开次数用于证明终止性与预算被尊重）。</summary>
    public struct NavCostPathResult
    {
        /// <summary>是否求出了路径。</summary>
        public bool bFound;

        /// <summary>路径总代价（无解时为 <see cref="NavCostGraph.Unreachable"/>）。</summary>

        public float Cost;

        /// <summary>本次搜索实际扩展的节点数。</summary>
        public int Expansions;

        /// <summary>搜索是否因预算耗尽而停止（展开次数达到上限）。</summary>
        public bool bBudgetExhausted;

        /// <summary>路径节点下标序列（从起点接驳节点到终点接驳节点）。</summary>
        public List<int> Nodes;
    }

    /// <summary>
    /// 道路优先代价图：节点 = 区域中心线采样点，边代价 = 直线距离 × 两端平均区域代价。
    /// 纯托管、可无引擎运行；<see cref="NavAreaSystem"/> 负责把引擎数据喂进来。
    /// </summary>
    public sealed class NavCostGraph
    {
        /// <summary>不可通行 / 无解的代价哨兵（与历史实现一致，沿用 float.MaxValue）。</summary>
        public const float Unreachable = float.MaxValue;

        private readonly List<NavCostPoint> _locations = new List<NavCostPoint>();
        private readonly List<float> _costs = new List<float>();
        private readonly List<bool> _blocked = new List<bool>();
        private readonly List<List<int>> _links = new List<List<int>>();

        /// <summary>节点数量。</summary>
        public int NodeCount => _locations.Count;

        /// <summary>清空整张图。</summary>
        public void Clear()
        {
            _locations.Clear();
            _costs.Clear();
            _blocked.Clear();
            _links.Clear();
        }

        /// <summary>
        /// 追加一个节点。代价达到 <see cref="Unreachable"/> 的节点被标记为不可通行
        /// （对应 NavArea.Null：它绝不参与连边，也绝不产生 0 代价的边）。
        /// </summary>
        public int AddNode(NavCostPoint location, float cost)
        {
            int index = _locations.Count;
            _locations.Add(location);
            _costs.Add(cost);
            _blocked.Add(cost >= Unreachable);
            _links.Add(new List<int>());
            return index;
        }

        /// <summary>取节点位置。</summary>
        public NavCostPoint GetLocation(int node)
        {
            return (node >= 0 && node < _locations.Count) ? _locations[node] : new NavCostPoint();
        }

        /// <summary>取节点区域代价。</summary>
        public float GetNodeCost(int node)
        {
            return (node >= 0 && node < _costs.Count) ? _costs[node] : Unreachable;
        }

        /// <summary>节点是否不可通行。</summary>
        public bool IsNodeBlocked(int node)
        {
            return node >= 0 && node < _blocked.Count && _blocked[node];
        }

        /// <summary>节点的邻居表。</summary>
        public IReadOnlyList<int> GetNeighbours(int node)
        {
            if (node < 0 || node >= _links.Count) return EmptyNeighbours;
            return _links[node];
        }

        /// <summary>两个节点之间是否存在真实连边（<see cref="BuildLinks"/> / <see cref="AddLink"/> 建立的连通性）。</summary>
        public bool IsLinked(int a, int b)
        {
            if (a < 0 || a >= _links.Count) return false;
            return _links[a].Contains(b);
        }

        private static readonly int[] EmptyNeighbours = new int[0];

        /// <summary>手工连一条双向边（端点越界或不可通行则不连）。</summary>
        public void AddLink(int a, int b)
        {
            if (a < 0 || b < 0 || a >= _locations.Count || b >= _locations.Count) return;
            if (_blocked[a] || _blocked[b]) return;
            if (!_links[a].Contains(b)) _links[a].Add(b);
            if (!_links[b].Contains(a)) _links[b].Add(a);
        }

        /// <summary>
        /// 重建连边：距离阈值内、注入的代价场确认两点之间存在可通行路径、
        /// 且沿线段采样的区域代价不撞上不可通行区域，才连这条边。
        /// 与旧实现逐条对应（跳过不可通行端点、跳过零距离），并补上「不可通行<b>区域</b>
        /// 横在两点之间时不得连边」——对应 UE5 中 blocked 区域代价 = FLT_MAX、
        /// 寻路无法穿过该区域多边形（RecastNavMesh.cpp:457-459）。
        /// </summary>
        public void BuildLinks(INavAreaField field, float maxLinkLength, int segmentCostSamples)
        {
            for (int i = 0; i < _links.Count; i++) _links[i].Clear();

            for (int a = 0; a < _locations.Count; a++)
            {
                if (_blocked[a]) continue;

                for (int b = a + 1; b < _locations.Count; b++)
                {
                    if (_blocked[b]) continue;

                    float dist = Distance(_locations[a], _locations[b]);
                    if (dist > maxLinkLength || dist <= 0f) continue;

                    if (field != null && !field.IsReachable(_locations[a], _locations[b])) continue;

                    // 阻断区域横在两点之间：这条边在代价上不存在（段平均代价为 Unreachable）。
                    if (SegmentAverageCost(_locations[a], _locations[b], field, segmentCostSamples) >= Unreachable) continue;

                    _links[a].Add(b);
                    _links[b].Add(a);
                }
            }
        }

        // ===== 纯代价函数 =====

        /// <summary>三维直线距离。</summary>
        public static float Distance(NavCostPoint a, NavCostPoint b)
        {
            float dx = a.X - b.X;
            float dy = a.Y - b.Y;
            float dz = a.Z - b.Z;
            return (float)System.Math.Sqrt((double)(dx * dx + dy * dy + dz * dz));
        }

        /// <summary>线性插值（与 Vector3.Lerp 同式：a + (b - a) * t）。</summary>
        public static NavCostPoint Lerp(NavCostPoint a, NavCostPoint b, float t)
        {
            return new NavCostPoint(
                a.X + (b.X - a.X) * t,
                a.Y + (b.Y - a.Y) * t,
                a.Z + (b.Z - a.Z) * t);
        }

        /// <summary>
        /// 一条边的代价：直线距离 × 两端区域代价的均值。
        /// 任一端不可通行（或下标越界）一律返回 <see cref="Unreachable"/>，绝不返回 0 ——
        /// 这是「不可通行区域不能被白嫖穿过」这条规则的实现点。
        /// 重合点距离为 0 时代价为 0（同一个位置移动不消耗代价）。
        /// </summary>
        public static float EdgeCost(NavCostPoint a, float costA, bool blockedA, NavCostPoint b, float costB, bool blockedB)
        {
            if (blockedA || blockedB) return Unreachable;

            float dist = Distance(a, b);
            if (dist <= 0f) return 0f;

            return dist * (costA + costB) * 0.5f;
        }

        /// <summary>本图的一条边代价（含越界保护）。</summary>
        public float EdgeCost(int a, int b)
        {
            if (a < 0 || b < 0 || a >= _locations.Count || b >= _locations.Count) return Unreachable;
            return EdgeCost(_locations[a], _costs[a], _blocked[a], _locations[b], _costs[b], _blocked[b]);
        }

        /// <summary>
        /// 一段折线上的平均区域代价：等距采样取均值，再与最小值折中（略偏向最小值，
        /// 避免长边只靠一个端点落在道路上就占便宜）。任一点不可通行则整段不可通行。
        /// </summary>
        public static float SegmentAverageCost(NavCostPoint a, NavCostPoint b, INavAreaField field, int sampleCount)
        {
            int samples = sampleCount > 2 ? sampleCount : 2;
            float total = 0f;
            float min = Unreachable;

            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / (samples - 1);
                float cost = field != null ? field.GetAreaCostAt(Lerp(a, b, t)) : 1f;
                if (cost >= Unreachable) return Unreachable;
                total += cost;
                if (cost < min) min = cost;
            }

            float average = total / samples;
            return (average + min) * 0.5f;
        }

        /// <summary>
        /// 启发式：到最近终点接驳节点的直线距离 × 全图最小区域代价。
        /// 因为任意一条边的代价都是「距离 × 不小于最小区域代价的均值」，h(n) 恒不超过真实剩余代价
        /// （只要最小区域代价取自 &lt;= 1.0 的那一侧，即道路 0.5 / 地面 1.0 的实际取值域），
        /// 所以不会高估，A* 在此权重域内给出最优代价路径。
        /// </summary>
        public static float HeuristicToGoal(NavCostPoint from, IList<NavCostEntryLink> endLinks, Func<int, NavCostPoint> locationOf, float minAreaCost)
        {
            float best = Unreachable;
            for (int i = 0; i < endLinks.Count; i++)
            {
                float d = Distance(from, locationOf(endLinks[i].Node));
                if (d < best) best = d;
            }

            return best * minAreaCost;
        }

        /// <summary>本图某个节点到终点接驳集的启发式代价（纯函数的实例形式，避免每次扩展分配委托）。</summary>
        public float HeuristicToGoal(int node, IList<NavCostEntryLink> endLinks, float minAreaCost)
        {
            if (node < 0 || node >= _locations.Count) return Unreachable;
            return HeuristicToGoal(_locations[node], endLinks, GetLocation, minAreaCost);
        }

        /// <summary>
        /// 全图可通行节点里最小的区域代价（用于启发式下界）；没有可用节点或非法值时取 1。
        /// <para>
        /// <b>可采纳性前提</b>：启发式只在「节点代价 &lt;= 默认地面代价 1.0」的取值域内不会高估
        /// —— 端口实际用的就是 UNavArea_Default 1.0 与 UNavArea_Road 0.5（见 harness Check 8 的
        /// h(n) &lt;= 手工推导剩余代价断言）。若将来接入代价大于 1.0 的区域（泥沼、浅水），
        /// 而路径上仍存在未被任何体积覆盖的地面（按 1.0 计），该下界就会偏高，A* 可能给出次优路径；
        /// 届时应把本函数与 <see cref="NavArea.Default"/> 的默认代价取 min。
        /// </para>
        /// </summary>
        public float GetMinNodeAreaCost()

        {
            float minAreaCost = Unreachable;
            for (int i = 0; i < _locations.Count; i++)
            {
                if (_blocked[i]) continue;
                if (_costs[i] < minAreaCost) minAreaCost = _costs[i];
            }
            if (minAreaCost >= Unreachable || minAreaCost <= 0f) minAreaCost = 1f;
            return minAreaCost;
        }

        /// <summary>取某个位置最近的若干可接驳节点（并确认两点之间确实可通行）。</summary>
        public List<NavCostEntryLink> CollectEntryLinks(
            NavCostPoint location,
            INavAreaField field,
            NavCostSearchOptions options)
        {
            var result = new List<NavCostEntryLink>();
            float entryDistance = options.EntryDistance;
            int maxEntries = options.MaxEntryNodes > 0 ? options.MaxEntryNodes : 6;
            int maxPathTests = options.MaxEntryPathTests > 0 ? options.MaxEntryPathTests : 12;

            var candidates = new List<int>();
            float limit = entryDistance * entryDistance;
            for (int i = 0; i < _locations.Count; i++)
            {
                if (_blocked[i]) continue;
                if (DistanceSquaredTo(location, i) > limit) continue;
                candidates.Add(i);
            }

            candidates.Sort((x, y) => DistanceSquaredTo(location, x).CompareTo(DistanceSquaredTo(location, y)));

            int tested = 0;
            for (int i = 0; i < candidates.Count && result.Count < maxEntries && tested < maxPathTests; i++)
            {
                int node = candidates[i];
                tested++;

                float distance = Distance(location, _locations[node]);
                if (field != null && !field.IsReachable(location, _locations[node])) continue;

                float averageCost = SegmentAverageCost(location, _locations[node], field, options.SegmentCostSamples);
                // 接驳段本身撞上不可通行区域时不允许接入（等价 UE5：端点 snap 到的多边形必须能被查询走到）。
                if (averageCost >= Unreachable) continue;

                result.Add(new NavCostEntryLink
                {
                    Node = node,
                    Distance = distance,
                    AverageCost = averageCost
                });
            }

            return result;
        }

        private float DistanceSquaredTo(NavCostPoint from, int node)
        {
            float dx = from.X - _locations[node].X;
            float dy = from.Y - _locations[node].Y;
            float dz = from.Z - _locations[node].Z;
            return dx * dx + dy * dy + dz * dz;
        }

        /// <summary>
        /// 在代价图上求一条从起点接驳集到终点接驳集的代价最优路径（不含查询点本身）。
        /// 展开次数受 <paramref name="maxSearchNodes"/> 硬约束：目标不可达或存在环时也必然在预算内退出，
        /// 不会无限循环。
        /// </summary>
        public NavCostPathResult TryFindPath(
            IList<NavCostEntryLink> startLinks,
            IList<NavCostEntryLink> endLinks,
            int maxSearchNodes)
        {
            var failure = new NavCostPathResult { Cost = Unreachable, Nodes = null };
            if (startLinks == null || endLinks == null) return failure;
            if (_locations.Count == 0) return failure;
            if (startLinks.Count == 0 || endLinks.Count == 0) return failure;

            int budget = maxSearchNodes > 0 ? maxSearchNodes : 2048;
            float minAreaCost = GetMinNodeAreaCost();

            var gScore = new Dictionary<int, float>();
            var cameFrom = new Dictionary<int, int>();
            var open = new List<int>();
            var closed = new HashSet<int>();

            for (int i = 0; i < startLinks.Count; i++)
            {
                var link = startLinks[i];
                int state = link.Node;
                float cost = link.Distance * link.AverageCost;
                if (!gScore.TryGetValue(state, out var existing) || cost < existing)
                {
                    gScore[state] = cost;
                    cameFrom[state] = -1; // -1 表示来自起点
                }
                if (!open.Contains(state)) open.Add(state);
            }

            int expansions = 0;
            float bestCost = Unreachable;
            int bestEndState = -1;

            while (open.Count > 0 && expansions < budget)
            {
                int bestIndex = 0;
                float bestF = Unreachable;
                for (int i = 0; i < open.Count; i++)
                {
                    int state = open[i];
                    float f = gScore[state] + HeuristicToGoal(state, endLinks, minAreaCost);
                    if (f < bestF)
                    {
                        bestF = f;
                        bestIndex = i;
                    }
                }

                int current = open[bestIndex];
                open.RemoveAt(bestIndex);
                if (closed.Contains(current)) continue;
                closed.Add(current);
                expansions++;

                // 能否从当前节点接到终点。
                // 修复记录（历史实现是缺陷）：过去这一步只按<b>几何</b>边代价 current→endNode 计分，
                // 不要求两点之间存在图上连边 —— 于是被墙隔断 / BuildLinks 明确拒绝过的邻居
                // 会被 A* 凭空「跨过去」，找出一条图上根本不连通的路径。
                // 现在的规则与 UE5 一致：端点允许 snap 到最近多边形（CollectEntryLinks 已用
                // IsReachable + 段代价复核这段接驳），但多边形之间的行进必须走真实连通关系 ——
                // dtNavMeshQuery::findPath 只沿共享边的相邻多边形推进
                // （UE 5.8 Private/NavMesh/PImplRecastNavMesh.cpp:1333，端点 snap 见 :1471/:1487 findNearestPoly）。
                // 因此 current→endNode 只有在图上有真实连边（或 current 就是 endNode，
                // 此时唯一需要的连接是已复核的终点接驳段，图上距离 0）时才允许接入。
                for (int e = 0; e < endLinks.Count; e++)

                {
                    int endNode = endLinks[e].Node;
                    float edge;
                    if (endNode == current)
                    {
                        edge = 0f;
                    }
                    else
                    {
                        if (!IsLinked(current, endNode)) continue;
                        edge = EdgeCost(current, endNode);
                        if (edge >= Unreachable) continue;
                    }

                    float total = gScore[current] + edge + endLinks[e].Distance * endLinks[e].AverageCost;
                    if (total < bestCost)
                    {
                        bestCost = total;
                        bestEndState = current;
                    }
                }

                var neighbours = _links[current];
                for (int n = 0; n < neighbours.Count; n++)
                {
                    int next = neighbours[n];
                    if (closed.Contains(next)) continue;

                    float edge = EdgeCost(current, next);
                    if (edge >= Unreachable) continue;

                    float tentative = gScore[current] + edge;
                    if (gScore.TryGetValue(next, out var known) && tentative >= known) continue;

                    gScore[next] = tentative;
                    cameFrom[next] = current;
                    if (!open.Contains(next)) open.Add(next);
                }
            }

            if (bestEndState < 0)
            {
                return new NavCostPathResult
                {
                    bFound = false,
                    Cost = Unreachable,
                    Expansions = expansions,
                    bBudgetExhausted = expansions >= budget,
                    Nodes = null,
                };
            }

            var reversed = new List<int>();
            int cursor = bestEndState;
            int guard = 0;
            while (cursor >= 0 && guard++ <= _locations.Count)
            {
                reversed.Add(cursor);
                if (!cameFrom.TryGetValue(cursor, out var prev)) break;
                cursor = prev;
            }

            reversed.Reverse();

            return new NavCostPathResult
            {
                bFound = reversed.Count > 0,
                Cost = bestCost,
                Expansions = expansions,
                bBudgetExhausted = expansions >= budget,
                Nodes = reversed,
            };
        }

        /// <summary>
        /// 一条点列的通行代价：逐段「长度 × 该段平均区域代价」累加，任一段不可通行则整条不可通行。
        /// 供 <see cref="NavigatorStatics.GetPathCost"/> 与 harness 共用。
        /// </summary>
        public static float PathCost(IList<NavCostPoint> points, Func<NavCostPoint, NavCostPoint, float> segmentAverageCost)
        {
            if (points == null || points.Count == 0) return Unreachable;
            if (points.Count == 1) return 0f;

            float total = 0f;
            for (int i = 1; i < points.Count; i++)
            {
                float length = Distance(points[i - 1], points[i]);
                float cost = segmentAverageCost != null ? segmentAverageCost(points[i - 1], points[i]) : 1f;
                if (cost >= Unreachable) return Unreachable;
                total += length * cost;
            }
            return total;
        }
    }
}

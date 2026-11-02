using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.AI.Navigation;
using NarrativePro.Core;

namespace NarrativePro.AI.Cover
{
    /// <summary>
    /// 掩护瓦片生成任务。对应 UE5 <c>FCoverTileGeneratorWrapper</c> +
    /// <c>FNarrativeTileCoverGenerator</c>（<c>Private/AI/Cover/NarrativeTileCoverGenerator.cpp</c>）。
    /// <para>
    /// UE5 里它是一个 <c>FAsyncTask</c>（Setup 取瓦片墙边 → DoWork 组链 → 写回 <c>FTileCover</c>）。
    /// Flax 的物理查询只能在主线程调用，因此同一个状态机改成按帧预算推进：<see cref="Step"/>
    /// 每消耗一次预算就推进一段探测，探测完毕后一次性完成组链运算。
    /// </para>
    /// </summary>
    public class CoverTileGeneratorWrapper
    {
        /// <summary>本任务负责的瓦片索引。对应 UE5 <c>TileIndexes</c>。</summary>
        public List<long> TileIndexes = new List<long>();

        /// <summary>瓦片引用（本实现中与索引同值）。对应 UE5 <c>TileRefs</c>。</summary>
        public List<long> TileRefs = new List<long>();

        /// <summary>所属导航瓦片网格。</summary>
        public NarrativeRecastNavMesh NavMesh;

        /// <summary>链单元角度容差（度）。对应 UE5 <c>ChainLinkAngleTolerance</c>。</summary>
        public float ChainLinkAngleTolerance = 45f;

        /// <summary>最小链单元长度。对应 UE5 <c>SmallestChainLinkLength</c>。</summary>
        public float SmallestChainLinkLength = 100f;

        /// <summary>链修正角度容差（度）。对应 UE5 <c>ChainLinkCorrectionAngleTolerance</c>。</summary>
        public float ChainLinkCorrectionAngleTolerance = 15f;

        /// <summary>链修正迭代次数。对应 UE5 <c>CorrectionIterationCount</c>。</summary>
        public int CorrectionIterationCount = 2;

        /// <summary>掩护探测配置。</summary>
        public CoverTraceConfig TraceConfig = new CoverTraceConfig();

        /// <summary>任务创建时刻（用于诊断陈旧任务）。对应 UE5 <c>PendingTileCreationTime</c>。</summary>
        public float PendingTileCreationTime = 0f;

        /// <summary>本任务处理的瓦片索引。对应 UE5 <c>TileIndex</c>（单瓦片用法）。</summary>
        public long TileIndex = -1;

        /// <summary>本任务生成的掩护数据（单瓦片用法）。对应 UE5 <c>GeneratedCover</c>。</summary>
        public TileCover GeneratedCover = new TileCover();

        /// <summary>任务是否已完成。</summary>
        public bool IsDone { get; private set; }

        /// <summary>任务是否已被取消。对应 UE5 <c>FAsyncTask::Cancel</c>。</summary>
        public bool bCancelled { get; private set; }

        private readonly List<List<NavigationWallEdge>> _wallEdges = new List<List<NavigationWallEdge>>();
        private readonly List<long[]> _neighbourIndexes = new List<long[]>();
        private readonly List<BoundingBox> _tileBounds = new List<BoundingBox>();
        private int _probeCursor;

        /// <summary>
        /// 采集瓦片数据（墙边、邻接瓦片、边界盒）并清空其旧掩护。
        /// 对应 UE5 <c>FNarrativeTileCoverGenerator::Setup</c>（:37-91）。
        /// </summary>
        public void Setup()
        {
            if (NavMesh == null)
            {
                IsDone = true;
                return;
            }

            _wallEdges.Clear();
            _neighbourIndexes.Clear();
            _tileBounds.Clear();
            _probeCursor = 0;

            for (int i = 0; i < TileIndexes.Count; i++)
            {
                long tileIndex = TileIndexes[i];

                // 旧掩护即将失效，先清空，避免查询命中半成品
                lock (NavMesh.TileCoverMutex)
                {
                    if (NavMesh.TilesCovers.TryGetValue(tileIndex, out var existing) && existing != null
                        && !ReferenceEquals(existing, TileCover.Invalid))
                    {
                        existing.OwnedCovers.Clear();
                        existing.BoundaryHash.Clear();
                    }
                    else
                    {
                        NavMesh.TilesCovers[tileIndex] = new TileCover();
                    }
                }

                _tileBounds.Add(NavMesh.GetTileBounds(tileIndex));
                _neighbourIndexes.Add(NavMesh.GetNeighbourTileIndexes(tileIndex));
                _wallEdges.Add(null);
            }
        }

        /// <summary>
        /// 推进任务，最多消耗 <paramref name="probeBudget"/> 次物理探测。
        /// 对应 UE5 的后台线程 <c>DoWork</c>。
        /// </summary>
        public void Step(ref int probeBudget)
        {
            if (IsDone || bCancelled) return;
            if (NavMesh == null)
            {
                IsDone = true;
                return;
            }

            // 阶段 1：逐瓦片采集墙边（含物理查询，必须在主线程）
            while (_probeCursor < TileIndexes.Count)
            {
                if (probeBudget <= 0) return;

                var edges = new List<NavigationWallEdge>();
                NavMesh.GetEdgesInTile(TileIndexes[_probeCursor], edges);

                int consumed = CostForTile(TileIndexes[_probeCursor]);
                probeBudget -= consumed > 0 ? consumed : 1;

                _wallEdges[_probeCursor] = edges;
                _probeCursor++;

                // 对应 UE5「if no wall edges exist, then no work needs to be done」
                if (edges.Count == 0)
                {
                    continue;
                }
            }

            DoWork();
            IsDone = true;
        }

        private int CostForTile(long tileIndex)
        {
            if (!NavMesh.TryGetTileCoords(tileIndex, out _)) return 256;
            float spacing = NavMesh.ProbeSpacing;
            int cells = spacing <= 0f ? 16 : Mathf.Max(1, (int)Math.Round(NavMesh.TileSize / spacing));
            int dim = cells + 2;
            return dim * dim * 2;
        }

        /// <summary>取消任务。对应 UE5 <c>FAsyncTask::Cancel</c>。</summary>
        public bool Cancel()
        {
            if (IsDone) return false;
            bCancelled = true;
            return true;
        }

        /// <summary>
        /// 为单个边界盒生成掩护数据。对应 UE5 单瓦片生成入口，保留原方法签名。
        /// </summary>
        /// <param name="tileBounds">瓦片边界</param>
        /// <param name="traceConfig">射线检测配置</param>
        public virtual void GenerateCover(BoundingBox tileBounds, CoverTraceConfig traceConfig)
        {
            if (traceConfig != null)
            {
                TraceConfig = traceConfig;
            }

            // 重建前清空旧输出（原写法 `TileCover = null` 误用了类型名，成员实为 GeneratedCover）
            GeneratedCover = null;
            GeneratedCover = BuildCoverForTile(0, tileBounds, new long[8]);
        }

        /// <summary>
        /// 组链 → 排序 → 合并 → 修正 → 写边界哈希。
        /// 对应 UE5 <c>FNarrativeTileCoverGenerator::DoWork</c>（:93-219）。
        /// </summary>
        protected void DoWork()
        {
            for (int index = 0; index < TileIndexes.Count; index++)
            {
                long workingTileIndex = TileIndexes[index];
                var edges = _wallEdges[index] ?? new List<NavigationWallEdge>();
                if (edges.Count == 0) continue;

                var newCoverChains = BuildChains(workingTileIndex, edges);
                var boundaryHash = BuildBoundaryHash(newCoverChains, workingTileIndex, _tileBounds[index], _neighbourIndexes[index]);

                lock (NavMesh.TileCoverMutex)
                {
                    if (!NavMesh.TilesCovers.TryGetValue(workingTileIndex, out var tileCover) || tileCover == null)
                    {
                        tileCover = new TileCover();
                        NavMesh.TilesCovers[workingTileIndex] = tileCover;
                    }
                    tileCover.OwnedCovers = newCoverChains;
                    tileCover.BoundaryHash = boundaryHash;
                }
            }
        }

        /// <summary>单瓦片便捷路径：直接产出该瓦片的 <see cref="TileCover"/>。</summary>
        private TileCover BuildCoverForTile(long workingTileIndex, BoundingBox bounds, long[] neighbours)
        {
            var result = new TileCover();
            if (NavMesh == null) return result;

            var edges = new List<NavigationWallEdge>();
            NavMesh.GetEdgesInTile(workingTileIndex, edges);
            if (edges.Count == 0) return result;

            result.OwnedCovers = BuildChains(workingTileIndex, edges);
            result.BoundaryHash = BuildBoundaryHash(result.OwnedCovers, workingTileIndex, bounds, neighbours);
            return result;
        }

        /// <summary>
        /// 把墙边归入掩护链：能接进已有链就接，否则新开一条链；
        /// 随后排序、合并相邻链、并做链单元修正。
        /// 对应 UE5 DoWork 中 :111-161 的三步。
        /// </summary>
        private List<CoverChainContainer> BuildChains(long workingTileIndex, List<NavigationWallEdge> edges)
        {
            var newCoverChains = new List<CoverChainContainer>(18);

            foreach (var edge in edges)
            {
                var tileWallChainLink = edge.ToChainLink((ulong)workingTileIndex);

                if (newCoverChains.Count == 0)
                {
                    newCoverChains.Add(new CoverChainContainer { Chain = new List<ChainLink> { tileWallChainLink } });
                    continue;
                }

                if (!AddLinkToExistingChain(tileWallChainLink, newCoverChains))
                {
                    newCoverChains.Add(new CoverChainContainer { Chain = new List<ChainLink> { tileWallChainLink } });
                }
            }

            // 链在此刻排序后顺序不再变化
            foreach (var chain in newCoverChains)
            {
                chain.Chain.Sort(CompareChainLinks);
            }

            MergeCovers(newCoverChains, ChainLinkAngleTolerance);
            FilterLinks(newCoverChains, SmallestChainLinkLength, ChainLinkCorrectionAngleTolerance, CorrectionIterationCount);

            // 移除被合并空的链
            newCoverChains.RemoveAll(c => c.Chain.Count == 0);
            return newCoverChains;
        }

        /// <summary>
        /// 链单元按「沿链首尾相接」的顺序排序。
        /// UE5 依赖 <c>Algo::Sort(Chain)</c> 的 <c>FChainLink::operator&lt;</c>（按连接性），
        /// 这里等价地按与链起点的连接距离排序，保证 Start()/End() 语义成立。
        /// </summary>
        private static int CompareChainLinks(ChainLink a, ChainLink b)
        {
            float da = Vector3.DistanceSquared(a.Start, b.Start);
            if (Math.Abs(da) > 0.01f)
            {
                // 不同起点：按 X 再按 Z 的稳定顺序排（同一网格线上的短边起点必然共线）
                int c = a.Start.X.CompareTo(b.Start.X);
                if (c != 0) return c;
                return a.Start.Z.CompareTo(b.Start.Z);
            }
            return 0;
        }

        /// <summary>尝试把链单元接进已有掩护链。对应 UE5 <c>AddLinkToExistingChain</c>（:235-253）。</summary>
        private bool AddLinkToExistingChain(ChainLink inChainLink, List<CoverChainContainer> inCover)
        {
            foreach (var cover in inCover)
            {
                for (int i = 0; i < cover.Chain.Count; i++)
                {
                    if (NarrativeRecastNavMesh.DoesLinkFitInChain(inChainLink, cover.Chain[i], ChainLinkAngleTolerance))
                    {
                        cover.Chain.Add(inChainLink);
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// 合并应当相连的掩护链。对应 UE5 <c>MergeCovers</c>（:255-312）。
        /// 判定本体住在 <see cref="CoverGeometryRules.MergeCovers"/>（进程外可断言），
        /// 这里只做纯几何容器 &lt;-&gt; 引擎类型的搬运与结果写回。
        /// </summary>
        public static void MergeCovers(List<CoverChainContainer> covers, float chainLinkAngleTolerance)
        {
            // UE5 :260-263：不足两条链时直接 return，连末尾的 RemoveAll 都不执行
            if (covers == null || covers.Count < 2) return;

            var geomChains = covers.ToGeomChains();
            var survivors = CoverGeometryRules.MergeCovers(geomChains, chainLinkAngleTolerance, Mathf.Epsilon);
            if (survivors == null) return;

            var kept = new List<CoverChainContainer>(survivors.Count);
            for (int i = 0; i < survivors.Count; i++)
            {
                int index = survivors[i];
                if (index < 0 || index >= covers.Count || covers[index] == null) continue;
                covers[index].Chain = geomChains[index].Chain.ToChainLinks();
                kept.Add(covers[index]);
            }

            covers.Clear();
            covers.AddRange(kept);
        }

        /// <summary>
        /// 折叠过短或夹角过小的相邻链单元。对应 UE5 <c>FilterLinks</c>
        /// （<c>NarrativeTileCoverGenerator.cpp:314-359</c>，同时被导航系统按同名语义复用）。
        /// minLinkLength / maxAngleForMerge / iterationCount 三条判据的本体住在
        /// <see cref="CoverGeometryRules.FilterLinks"/>。
        /// </summary>
        public static void FilterLinks(List<CoverChainContainer> covers, float minLinkLength, float maxAngleForMerge, int iterationCount)
        {
            if (covers == null) return;

            var geomChains = covers.ToGeomChains();
            CoverGeometryRules.FilterLinks(geomChains, minLinkLength, maxAngleForMerge, iterationCount, Mathf.Epsilon);

            for (int i = 0; i < covers.Count && i < geomChains.Count; i++)
            {
                if (covers[i] == null) continue;
                covers[i].Chain = geomChains[i].Chain.ToChainLinks();
            }
        }

        /// <summary>
        /// 生成边界哈希：跨出本瓦片边界的链端点 → 邻接瓦片 + 链索引。
        /// 对应 UE5 DoWork 中的 boundary hash 段（:163-206）。
        /// </summary>
        private Dictionary<Vector3, BoundaryHashInfo> BuildBoundaryHash(
            List<CoverChainContainer> chains, long workingTileIndex, BoundingBox bounds, long[] neighbourIndexes)
        {
            var hash = new Dictionary<Vector3, BoundaryHashInfo>();
            if (chains == null || chains.Count == 0) return hash;

            for (int chainIndex = 0; chainIndex < chains.Count; chainIndex++)
            {
                var coverChain = chains[chainIndex];
                if (coverChain.Chain.Count == 0) continue;

                AddBoundaryEntry(hash, coverChain.Start().Start, chainIndex, bounds, neighbourIndexes);
                AddBoundaryEntry(hash, coverChain.End().End, chainIndex, bounds, neighbourIndexes);
            }

            return hash;
        }

        private static void AddBoundaryEntry(Dictionary<Vector3, BoundaryHashInfo> hash, Vector3 point,
            int chainIndex, BoundingBox bounds, long[] neighbourIndexes)
        {
            var edge = GetTargetNeighbour(point, bounds);
            if (edge == ETileEdge.None) return;

            int slot = (int)edge;
            long neighbourIndex = (neighbourIndexes != null && slot < neighbourIndexes.Length) ? neighbourIndexes[slot] : -1;
            hash[Quantize(point)] = new BoundaryHashInfo((sbyte)chainIndex, neighbourIndex);
        }

        /// <summary>
        /// 判断点落在瓦片边界盒的哪条边上，并给出该边指向的邻接方向。
        /// 对应 UE5 <c>GetTargetNeighbour</c>（:8-19）。Flax 是 Y-up，故 UE 的 X/Y 平面在此对应 X/Z。
        /// 判定本体住在 <see cref="CoverGeometryRules.GetTargetNeighbour"/>。
        /// </summary>
        public static ETileEdge GetTargetNeighbour(Vector3 point, BoundingBox box)
        {
            int edge = CoverGeometryRules.GetTargetNeighbour(point.ToGeom(), box.Minimum.ToGeom(), box.Maximum.ToGeom(),
                NarrativeRecastNavMesh.TileBoundaryTolerance);
            return (ETileEdge)edge;
        }

        /// <summary>
        /// 把边界点量化到固定精度，作为跨瓦片共享键。
        /// UE5 依赖 Recast 瓦片边界坐标天然精确相等；Flax 侧由网格线公式保证，
        /// 这里再量化一次以吸收探测中的浮点噪声。尺度常量住在纯接缝
        /// <see cref="CoverGeometryRules.HashQuantizeScale"/>。
        /// </summary>
        internal static Vector3 Quantize(Vector3 value)
        {
            return value.ToGeom().Quantized(CoverGeometryRules.HashQuantizeScale).ToVector3();
        }
    }

    /// <summary>
    /// 掩护瓦片生成器。对应 UE5 <c>UNarrativeTileCoverGenerator</c>。
    /// <para>
    /// 是场景里挂载用的门面：确保存在 <see cref="NarrativeRecastNavMesh"/>，
    /// 把「按区域生成掩护」转成瓦片脏标记 + 异步重建，并对外提供掩护查询。
    /// </para>
    /// </summary>
    public class NarrativeTileCoverGenerator : Script
    {
        /// <summary>单例实例</summary>
        public static NarrativeTileCoverGenerator Instance { get; private set; }

        /// <summary>掩护检测配置</summary>
        [Serialize]
        public CoverTraceConfig TraceConfig = new CoverTraceConfig();

        /// <summary>是否在启用时自动全域生成一次掩护。</summary>
        [Serialize]
        public bool bGenerateOnEnable = true;

        /// <summary>自动生成的覆盖半径（世界单位）。</summary>
        [Serialize]
        public float AutoGenerateRadius = 10000f;

        /// <summary>已生成的瓦片掩护数据（按瓦片索引）</summary>
        private readonly Dictionary<long, TileCover> _tileCoverMap = new Dictionary<long, TileCover>();

        /// <summary>所有掩护容器（用于查询）</summary>
        private readonly List<CoverContainer> _allCovers = new List<CoverContainer>();

        /// <summary>本生成器管理的导航瓦片网格。对应 UE5 的 NavData 实例。</summary>
        public NarrativeRecastNavMesh NavMesh { get; private set; }

        public override void OnEnable()
        {
            base.OnEnable();
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;

            NavMesh = ResolveNavMesh();
            if (NavMesh != null)
            {
                NavMesh.CoverGenerationFinished += RebuildFlatCoverList;
            }

            if (bGenerateOnEnable)
            {
                GenerateCoversAround(Actor != null ? Actor.Position : Vector3.Zero, AutoGenerateRadius);
            }
        }

        public override void OnDisable()
        {
            if (NavMesh != null)
            {
                NavMesh.CoverGenerationFinished -= RebuildFlatCoverList;
            }
            if (Instance == this) Instance = null;
            base.OnDisable();
        }

        /// <summary>
        /// 找到（或就地创建）承载掩护数据的导航瓦片网格。
        /// UE5 中 NavMesh 与 NavData 由引擎实例化；Flax 中需要一个宿主 Actor。
        /// </summary>
        private NarrativeRecastNavMesh ResolveNavMesh()
        {
            if (NarrativeRecastNavMesh.Instance != null)
            {
                return NarrativeRecastNavMesh.Instance;
            }

            if (Actor == null) return null;

            var navMesh = Actor.GetScript<NarrativeRecastNavMesh>();
            if (navMesh != null) return navMesh;

            navMesh = Actor.AddScript<NarrativeRecastNavMesh>();
            return navMesh;
        }

        /// <summary>为指定区域生成掩护数据。对应 UE5 的瓦片脏标记 + 重建。</summary>
        /// <param name="bounds">区域包围盒</param>
        public void GenerateCoversForArea(BoundingBox bounds)
        {
            if (NavMesh == null)
            {
                NavMesh = ResolveNavMesh();
            }
            if (NavMesh == null)
            {
                NarrativeLog.LogWarning("GenerateCoversForArea: 无法解析 NarrativeRecastNavMesh，掩护未生成");
                return;
            }

            var affected = NavMesh.QueueAreaForUpdate(bounds);
            if (affected.Count == 0)
            {
                NarrativeLog.LogWarning($"GenerateCoversForArea: 区域 {bounds} 未匹配到任何导航瓦片");
                return;
            }

            foreach (var tileIndex in affected)
            {
                if (!_tileCoverMap.ContainsKey(tileIndex))
                {
                    _tileCoverMap[tileIndex] = NavMesh.GetTileCover(tileIndex);
                }
            }

            NavMesh.RebuildCover();
        }

        /// <summary>以某点为圆心生成掩护，便于运行时无现成区域盒时使用。</summary>
        public void GenerateCoversAround(Vector3 center, float radius)
        {
            Vector3 min = new Vector3(center.X - radius, center.Y, center.Z - radius);
            Vector3 max = new Vector3(center.X + radius, center.Y, center.Z + radius);
            GenerateCoversForArea(new BoundingBox(min, max));
        }

        /// <summary>全域重建掩护。对应 UE5 <c>UNarrativeNavigationSystem::RebuildAllCover</c>。</summary>
        public void RebuildAllCover()
        {
            if (NavMesh == null)
            {
                NavMesh = ResolveNavMesh();
            }
            NavMesh?.RebuildAll();
        }

        /// <summary>把导航网格中的瓦片掩护摊平成可查询列表，并同步本地缓存。</summary>
        private void RebuildFlatCoverList()
        {
            if (NavMesh == null) return;

            _tileCoverMap.Clear();
            _allCovers.Clear();

            lock (NavMesh.TileCoverMutex)
            {
                foreach (var kvp in NavMesh.TilesCovers)
                {
                    var tileCover = kvp.Value;
                    if (tileCover == null || !tileCover.AnyCovers()) continue;

                    _tileCoverMap[kvp.Key] = tileCover;

                    for (int i = 0; i < tileCover.OwnedCovers.Count; i++)
                    {
                        var chain = tileCover.OwnedCovers[i];
                        if (chain.Chain.Count == 0) continue;
                        _allCovers.Add(new CoverContainer(chain.Chain, kvp.Key, (sbyte)i));
                    }
                }
            }
        }

        /// <summary>获取指定瓦片的掩护数据</summary>
        public TileCover GetTileCover(long tileIndex)
        {
            if (_tileCoverMap.TryGetValue(tileIndex, out var cover))
            {
                return cover;
            }
            return NavMesh != null ? NavMesh.GetTileCover(tileIndex) : TileCover.Invalid;
        }

        /// <summary>获取所有掩护容器</summary>
        public List<CoverContainer> GetAllCovers()
        {
            if (_allCovers.Count == 0 && NavMesh != null)
            {
                RebuildFlatCoverList();
            }
            return _allCovers;
        }

        /// <summary>
        /// 查找距离指定位置最近的掩护。转发到 <see cref="NarrativeNavigationSystem"/>，
        /// 以便与 UE5 一样支持方向偏置、跨瓦片串联与链索引输出。
        /// </summary>
        /// <param name="position">查询位置</param>
        /// <param name="maxDistance">最大搜索距离</param>
        /// <returns>最近的掩护容器，找不到返回 null</returns>
        public CoverContainer FindNearestCover(Vector3 position, float maxDistance = 1000f)
        {
            var chain = new List<ChainLink>();
            if (!NarrativeNavigationSystem.FindNearestCoverToPoint(this, position, chain, out _, out _, maxDistance))
            {
                return null;
            }

            // 串联结果可能跨瓦片，用首链单元定位归属
            var first = chain[0];
            long tileIndex = (long)first.ParentTileIndex;
            var tileCover = GetTileCover(tileIndex);
            int chainIndex = FindChainIndex(tileCover, first);

            return new CoverContainer(chain, tileIndex, (sbyte)Mathf.Max(0, chainIndex));
        }

        private static int FindChainIndex(TileCover tileCover, ChainLink link)
        {
            if (tileCover == null) return -1;
            for (int i = 0; i < tileCover.OwnedCovers.Count; i++)
            {
                foreach (var candidate in tileCover.OwnedCovers[i].Chain)
                {
                    if (candidate.Start == link.Start && candidate.End == link.End)
                    {
                        return i;
                    }
                }
            }
            return -1;
        }

        /// <summary>查找指定位置范围内的所有掩护</summary>
        public List<CoverContainer> FindCoversInRadius(Vector3 position, float radius)
        {
            var result = new List<CoverContainer>();
            if (NarrativeNavigationSystem.FindAllCoverInRadiusToPoint(this, position, result, radius))
            {
                return result;
            }

            // 导航网格尚未就绪时退回本地摊平列表，保证查询语义可用
            float r2 = radius * radius;
            foreach (var cover in GetAllCovers())
            {
                if (cover == null || !cover.IsValid()) continue;
                foreach (var link in cover.CoverChain)
                {
                    Vector3 closest = ClosestPointOnSegment(position, link.Start, link.End);
                    if (Vector3.DistanceSquared(position, closest) <= r2)
                    {
                        result.Add(cover);
                        break;
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// 点到线段最近点。对应 UE5 <c>FMath::ClosestPointOnSegment</c>。
        /// 投影与钳制本体住在 <see cref="CoverGeometryRules.ClosestPointOnSegment"/>。
        /// </summary>
        internal static Vector3 ClosestPointOnSegment(Vector3 point, Vector3 segStart, Vector3 segEnd)
        {
            return CoverGeometryRules.ClosestPointOnSegment(point.ToGeom(), segStart.ToGeom(), segEnd.ToGeom(), Mathf.Epsilon)
                .ToVector3();
        }

        /// <summary>清空所有掩护数据</summary>
        public void ClearAllCovers()
        {
            _tileCoverMap.Clear();
            _allCovers.Clear();

            if (NavMesh != null)
            {
                lock (NavMesh.TileCoverMutex)
                {
                    foreach (var kvp in NavMesh.TilesCovers)
                    {
                        kvp.Value?.OwnedCovers.Clear();
                        kvp.Value?.BoundaryHash.Clear();
                    }
                    NavMesh.TilesCovers.Clear();
                }
            }
        }
    }
}

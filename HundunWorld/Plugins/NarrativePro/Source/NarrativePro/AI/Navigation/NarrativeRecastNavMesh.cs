using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.AI.Cover;
using NarrativePro.Core;

namespace NarrativePro.AI.Navigation
{
    /// <summary>
    /// 导航瓦片内的一面墙边缘。对应 UE5 <c>FNavigationWallEdge</c>（RecastHelpers.h）。
    /// <para>
    /// UE5 从 Recast 瓦片多边形中提取「侧边被阻挡、邻边可通行」的墙边；
    /// Flax 1.12 的托管导航 API（<see cref="FlaxEngine.Navigation"/>）不暴露 dtNavMesh 瓦片/多边形，
    /// 因此这里由 <see cref="NarrativeRecastNavMesh"/> 对场景做等间距占用探测后生成同语义的墙边。
    /// </para>
    /// </summary>
    public struct NavigationWallEdge
    {
        /// <summary>边起点（约定：<see cref="Normal"/> = Cross(Vector3.Up, End - Start)）。</summary>
        public Vector3 Start;

        /// <summary>边终点。</summary>
        public Vector3 End;

        /// <summary>朝向开阔侧（可躲藏的一侧）的法线。</summary>
        public Vector3 Normal;

        /// <summary>该墙面的探测高度（世界 Y）。</summary>
        public float Height;

        /// <summary>是否为低掩（低处被挡、高处可越过）。</summary>
        public bool bIsLowCover;

        /// <summary>边长度。</summary>
        public float Length => Vector3.Distance(Start, End);

        public NavigationWallEdge(Vector3 start, Vector3 end, Vector3 normal, float height, bool bLowCover)
        {
            Start = start;
            End = end;
            Normal = normal;
            Height = height;
            bIsLowCover = bLowCover;
        }

        /// <summary>转换为掩护链单元。对应 UE5 <c>FChainLink(const FNavigationWallEdge&amp;)</c>。</summary>
        public ChainLink ToChainLink(ulong parentTileIndex)
        {
            var link = new ChainLink(Start, End, Normal)
            {
                ParentTileIndex = parentTileIndex
            };
            return link;
        }
    }

    /// <summary>
    /// 带掩护数据的虚拟导航瓦片网格。对应 UE5 <c>ANarrativeRecastNavMesh</c>
    /// （<c>Private/AI/Navigation/NarrativeRecastNavMesh.cpp</c>）。
    /// <para>
    /// UE5 中该类扩展 <c>ARecastNavMesh</c>，从 Detour 瓦片提取墙边并异步生成掩护链。
    /// Flax 1.12 的托管层不暴露 Detour 瓦片，因此本类自带一层「瓦片网格」：
    /// 世界 XZ 平面被切成 <see cref="TileSize"/> 见方的格子，格子内的墙体由物理占用探测得到，
    /// 其余流程（链分组 → 合并 → FilterLinks 修正 → 边界哈希跨瓦片串联）与 UE5 完全一致。
    /// </para>
    /// <para>
    /// UE5 用 <c>FAsyncTask</c> 后台线程做链运算；Flax 的物理查询必须在主线程，
    /// 因此这里把每个 <see cref="CoverTileGeneratorWrapper"/> 做成可分帧推进的状态机
    /// （<see cref="CoverTileGeneratorWrapper.Step"/>），语义上等价于
    /// <c>RebuildCoverV2</c> + <c>TickAsyncCoverBuild</c> 的运行任务表。
    /// </para>
    /// </summary>
    public class NarrativeRecastNavMesh : Script
    {
        /// <summary>当前场景中的活动实例（对应 UE5 的默认 NavData 实例）。</summary>
        public static NarrativeRecastNavMesh Instance { get; private set; }

        /// <summary>
        /// 单个导航瓦片的边长（世界单位，厘米）。
        /// 会自动对齐到探测间距的整数倍，以保证相邻瓦片在公共边界上得到完全相同的坐标
        /// （UE5 由 Recast 瓦片天然保证这一点，边界哈希依赖精确相等）。
        /// </summary>
        [Serialize]
        public float TileSize = 2000f;

        /// <summary>网格在原点侧的起点（世界 XZ）。Y 由探测高度决定，见 <see cref="CoverTraceConfig"/>。</summary>
        [Serialize]
        public Vector3 GridOrigin = Vector3.Zero;

        /// <summary>可容纳的最大瓦片数。对应 UE5 <c>dtNavMesh::m_maxTiles</c>。</summary>
        [Serialize]
        public int MaxTiles = 4096;

        /// <summary>每个运行任务分组的瓦片数。对应 UE5 <c>RebuildCoverV2</c> 中写死的 8。</summary>
        [Serialize]
        public int TilesPerCoverTask = 8;

        /// <summary>每帧允许推进的探测预算（物理查询次数）。0 表示不限制。</summary>
        [Serialize]
        public int ProbeBudgetPerFrame = 1500;

        /// <summary>是否在启用时自动做一次全域重建。</summary>
        [Serialize]
        public bool bRebuildOnEnable = true;

        /// <summary>重建覆盖范围（生成区域）。默认以 <see cref="GridOrigin"/> 为中心。</summary>
        [Serialize]
        public float DefaultBuildRadius = 10000f;

        /// <summary>调试：在编辑器中绘制已生成的掩护链。</summary>
        [Serialize]
        public bool bDebugDrawCover = false;

        // ===== 与 UE5 成员一一对应的运行时状态 =====

        /// <summary>待生成/正在生成掩护的瓦片索引表。对应 UE5 <c>DirtyTiles</c>。</summary>
        public readonly List<long> DirtyTiles = new List<long>();

        /// <summary>正在运行的掩护生成任务。对应 UE5 <c>RunningCoverTasks</c>。</summary>
        public readonly List<RunningCoverTask> RunningCoverTasks = new List<RunningCoverTask>();

        /// <summary>瓦片索引 → 该瓦片的掩护数据。对应 UE5 <c>TilesCovers</c>。</summary>
        public readonly Dictionary<long, TileCover> TilesCovers = new Dictionary<long, TileCover>();

        /// <summary>掩护数据互斥锁。对应 UE5 <c>TileCoverMutex</c>（Flax 用 <c>lock</c> 对象表达）。</summary>
        public readonly object TileCoverMutex = new object();

        /// <summary>当前运行中的任务数。对应 UE5 <c>NumRunningTasks</c>。</summary>
        public int NumRunningTasks { get; private set; }

        /// <summary>掩护生成全部完成时触发。对应 UE5 <c>OnCoverGenerationFinished</c>。</summary>
        public event Action CoverGenerationFinished;

        private bool _bPendingRebuildAll;
        private bool _bNavMeshReadyReported;

        public override void OnEnable()
        {
            base.OnEnable();
            if (Instance != null && Instance != this)
            {
                NarrativeLog.LogWarning("场景中已存在 NarrativeRecastNavMesh，当前实例不再生效");
                return;
            }
            Instance = this;
            AlignTileSizeToProbeSpacing();

            if (bRebuildOnEnable)
            {
                RebuildAll();
            }
        }

        public override void OnDisable()
        {
            if (Instance == this)
            {
                Instance = null;
            }
            CancelAllRunningTasks();
            base.OnDisable();
        }

        public override void OnUpdate()
        {
            // 对应 UE5 UNarrativeNavigationSystem::Tick → NavData->TickAsyncCoverBuild(DeltaSeconds)
            TickAsyncCoverBuild(Time.DeltaTime);

            if (_bPendingRebuildAll)
            {
                _bPendingRebuildAll = false;
                MarkAllTilesDirty();
            }

            if (DirtyTiles.Count > 0 && RunningCoverTasks.Count < MaxConcurrentCoverTasks)
            {
                RebuildCover();
            }
        }

#if FLAX_EDITOR
        public override void OnDebugDraw()
        {
            base.OnDebugDraw();
            if (bDebugDrawCover)
            {
                DebugDrawCover();
            }
        }
#endif

        // ===================================================================
        // 瓦片坐标映射
        // ===================================================================

        /// <summary>探测间距（取设置中的掩护采样间距，最小 20 以防退化）。</summary>
        public float ProbeSpacing
        {
            get
            {
                float spacing = ArsenalSettings.Instance?.CoverTraceConfig?.CoverSpacing ?? 150f;
                return spacing <= 20f ? 150f : spacing;
            }
        }

        private void AlignTileSizeToProbeSpacing()
        {
            float spacing = ProbeSpacing;
            if (spacing <= 0f) return;
            int cells = Mathf.Max(4, (int)(TileSize / spacing));
            TileSize = cells * spacing;
        }

        /// <summary>世界坐标 → 瓦片坐标。对应 UE5 <c>dtNavMesh::getTileByCoord</c>。</summary>
        public Int2 GetTileCoords(Vector3 world)
        {
            float size = TileSize <= 0f ? 1f : TileSize;
            return new Int2(
                (int)Math.Floor((world.X - GridOrigin.X) / size),
                (int)Math.Floor((world.Z - GridOrigin.Z) / size));
        }

        /// <summary>瓦片坐标 → 稳定的瓦片索引。对应 UE5 <c>dtNavMesh::getTileIndex</c>。</summary>
        public long GetTileIndex(Int2 coords)
        {
            unchecked
            {
                // 与 UE5 一样，瓦片索引由坐标哈希得到；层恒为 0
                long h = (long)coords.X * 73856093L ^ (long)coords.Y * 19349663L;
                if (h < 0) h = -h;
                return h % (long)Mathf.Max(1, MaxTiles);
            }
        }

        /// <summary>世界坐标 → 瓦片索引。</summary>
        public long GetTileIndexForPoint(Vector3 world) => GetTileIndex(GetTileCoords(world));

        /// <summary>瓦片索引对应的坐标（由 <see cref="TileLookup"/> 维护的映射还原）。</summary>
        public bool TryGetTileCoords(long tileIndex, out Int2 coords)
        {
            return TileLookup.TryGetValue(tileIndex, out coords);
        }

        /// <summary>瓦片索引 → 边界盒。对应 UE5 <c>Recast2UnrealBox(dtMeshHeader::bmin/bmax)</c>。</summary>
        public BoundingBox GetTileBounds(long tileIndex)
        {
            if (!TileLookup.TryGetValue(tileIndex, out var coords))
            {
                return BoundingBox.Empty;
            }
            return GetTileBounds(coords);
        }

        /// <summary>瓦片坐标 → 边界盒（Y 方向取掩护探测高度范围）。</summary>
        public BoundingBox GetTileBounds(Int2 coords)
        {
            float size = TileSize;
            var trace = ArsenalSettings.Instance?.CoverTraceConfig ?? new CoverTraceConfig();
            float minY = GridOrigin.Y - size * 0.5f;
            float maxY = GridOrigin.Y + Mathf.Max(trace.PeekOverHeight, trace.HalfHeight) + size;
            Vector3 min = new Vector3(GridOrigin.X + coords.X * size, minY, GridOrigin.Z + coords.Y * size);
            Vector3 max = new Vector3(min.X + size, maxY, min.Z + size);
            return new BoundingBox(min, max);
        }

        /// <summary>瓦片边界判定容差。对应 UE5 <c>FVector::Equals(..., 0.1)</c>。</summary>
        public const float TileBoundaryTolerance = 0.1f;

        /// <summary>该瓦片索引是否已被网格登记。对应 UE5 <c>dtMeshTile</c> 是否有效。</summary>
        public bool TileCoversContain(long tileIndex)
        {
            return tileIndex >= 0 && TileLookup.ContainsKey(tileIndex);
        }

        /// <summary>已登记的瓦片总数（诊断用）。</summary>
        public int RegisteredTileCount => TileLookup.Count;

        /// <summary>掩护数据是否已生成完成（无脏瓦片、无运行中任务）。</summary>
        public bool IsCoverGenerationIdle => DirtyTiles.Count == 0 && RunningCoverTasks.Count == 0;

        /// <summary>瓦片索引 → 8 邻接瓦片索引（按 <see cref="ETileEdge"/> 顺序，-1 表示不存在）。对应 UE5 <c>getNeighbourCoords</c>。</summary>
        public long[] GetNeighbourTileIndexes(long tileIndex)
        {
            var result = new long[8];
            for (int i = 0; i < 8; i++) result[i] = -1;
            if (!TileLookup.TryGetValue(tileIndex, out var coords)) return result;

            for (int edge = 0; edge < 8; edge++)
            {
                Int2 offset = TileEdgeOffset((ETileEdge)edge);
                // 用坐标表解析而不是再哈希一次：哈希冲突时登记索引与裸哈希不同
                if (!TryGetTileIndex(new Int2(coords.X + offset.X, coords.Y + offset.Y), out long neighbour))
                {
                    continue;
                }
                if (neighbour != tileIndex)
                {
                    result[edge] = neighbour;
                }
            }
            return result;
        }

        /// <summary>瓦片坐标 → 已登记的瓦片索引。</summary>
        public bool TryGetTileIndex(Int2 coords, out long tileIndex)
        {
            return TileByCoord.TryGetValue(coords, out tileIndex);
        }

        /// <summary>按边界方向取邻接坐标偏移。与 <see cref="NarrativeTileCoverGenerator.GetTargetNeighbour"/> 一致。</summary>
        public static Int2 TileEdgeOffset(ETileEdge edge)
        {
            switch (edge)
            {
                case ETileEdge.Bottom: return new Int2(-1, 0);
                case ETileEdge.BottomLeft: return new Int2(-1, -1);
                case ETileEdge.Left: return new Int2(0, -1);
                case ETileEdge.TopLeft: return new Int2(1, -1);
                case ETileEdge.Top: return new Int2(1, 0);
                case ETileEdge.TopRight: return new Int2(1, 1);
                case ETileEdge.Right: return new Int2(0, 1);
                case ETileEdge.BottomRight: return new Int2(-1, 1);
                default: return new Int2(0, 0);
            }
        }

        /// <summary>瓦片索引与坐标的双向映射（本实现自建，UE5 由 dtNavMesh 提供）。</summary>
        private readonly Dictionary<long, Int2> TileLookup = new Dictionary<long, Int2>();
        private readonly Dictionary<Int2, long> TileByCoord = new Dictionary<Int2, long>();

        /// <summary>登记一个瓦片坐标并返回其索引（冲突时线性探测，保证不同坐标得到不同索引）。</summary>
        public long RegisterTile(Int2 coords)
        {
            if (TileByCoord.TryGetValue(coords, out var existing)) return existing;

            long index = GetTileIndex(coords);
            // 哈希冲突：坐标不同的瓦片不能共用索引，否则边界哈希会把两条无关掩护串起来
            for (int probe = 1; probe <= 64; probe++)
            {
                if (!TileLookup.TryGetValue(index, out var occupant) || occupant == coords)
                {
                    break;
                }
                index = GetTileIndex(new Int2(coords.X + probe, coords.Y - probe));
            }

            if (TileLookup.TryGetValue(index, out var clash) && clash != coords)
            {
                // 放弃登记：UE5 中瓦片数是硬上限，满了就不再生成
                NarrativeLog.LogWarning($"导航瓦片已满（MaxTiles={MaxTiles}），无法登记新瓦片 {coords}");
                return -1;
            }

            TileLookup[index] = coords;
            TileByCoord[coords] = index;
            return index;
        }

        // ===================================================================
        // UE5 静态链匹配逻辑
        // ===================================================================

        /// <summary>取链单元的方向法线（起点 → 终点）。对应 UE5 <c>GetLinkDirectionNormal</c>。</summary>
        public static Vector3 GetLinkDirectionNormal(ChainLink chainLink)
        {
            return chainLink.GetStartNormal();
        }

        /// <summary>
        /// 判断候选链单元是否与已有链单元首尾相接且在角度容差内。
        /// 对应 UE5 <c>ANarrativeRecastNavMesh::DoesLinkFitInChain</c>（NarrativeRecastNavMesh.cpp:20-44）。
        /// 「共享恰好一个顶点 + 方向夹角在容差内」这条判据的本体住在
        /// <see cref="CoverGeometryRules.DoesLinkFitInChain"/>（进程外可断言）。
        /// </summary>
        public static bool DoesLinkFitInChain(ChainLink linkCandidate, ChainLink chainLink, float angleTolerance)
        {
            return CoverGeometryRules.DoesLinkFitInChain(linkCandidate.ToGeom(), chainLink.ToGeom(), angleTolerance, Mathf.Epsilon);
        }

        /// <summary>UE5 <c>FVector::Equals(..., 0.1)</c> 的等价比较（转发到纯接缝）。</summary>
        internal static bool ApproxEqual(Vector3 a, Vector3 b)
        {
            return a.ToGeom().ApproxEqual(b.ToGeom(), CoverGeometryRules.VertexTolerance);
        }

        // ===================================================================
        // 掩护重建
        // ===================================================================

        /// <summary>把瓦片加入待更新列表。对应 UE5 <c>QueueTilesForUpdate</c>。</summary>
        public void QueueTilesForUpdate(List<long> changedTiles)
        {
            if (changedTiles == null) return;
            for (int i = 0; i < changedTiles.Count; i++)
            {
                long tile = changedTiles[i];
                if (tile < 0 || DirtyTiles.Contains(tile)) continue;
                DirtyTiles.Add(tile);
            }
        }

        /// <summary>为某个世界区域登记并加脏所有相交瓦片。返回受影响的瓦片索引。</summary>
        public List<long> QueueAreaForUpdate(BoundingBox area)
        {
            AlignTileSizeToProbeSpacing();
            var affected = new List<long>();
            float size = TileSize;
            int minX = (int)Math.Floor((area.Minimum.X - GridOrigin.X) / size);
            int maxX = (int)Math.Floor((area.Maximum.X - GridOrigin.X) / size);
            int minZ = (int)Math.Floor((area.Minimum.Z - GridOrigin.Z) / size);
            int maxZ = (int)Math.Floor((area.Maximum.Z - GridOrigin.Z) / size);

            for (int x = minX; x <= maxX; x++)
            {
                for (int z = minZ; z <= maxZ; z++)
                {
                    long index = RegisterTile(new Int2(x, z));
                    if (index < 0) continue;
                    affected.Add(index);
                }
            }

            QueueTilesForUpdate(affected);
            return affected;
        }

        /// <summary>全域重建。对应 UE5 <c>RebuildAll</c> + <c>UNarrativeNavigationSystem::RebuildAllCover</c>。</summary>
        public void RebuildAll()
        {
            // UE5 中把重建推迟到下一 tick，因为部分参数可能尚未传播完成
            _bPendingRebuildAll = true;
        }

        /// <summary>把当前网格范围内的所有瓦片标记为脏。对应 UE5 <c>dtNavMesh</c> 全瓦片遍历。</summary>
        private void MarkAllTilesDirty()
        {
            AlignTileSizeToProbeSpacing();
            float size = TileSize;
            float radius = Mathf.Max(DefaultBuildRadius, size);
            int span = (int)Math.Ceiling(radius / size);
            var centre = GetTileCoords(GridOrigin);

            CancelAllRunningTasks();
            DirtyTiles.Clear();

            for (int x = centre.X - span; x <= centre.X + span; x++)
            {
                for (int z = centre.Y - span; z <= centre.Y + span; z++)
                {
                    long index = RegisterTile(new Int2(x, z));
                    if (index < 0) continue;
                    if (!DirtyTiles.Contains(index))
                    {
                        DirtyTiles.Add(index);
                    }
                }
            }

            _bNavMeshReadyReported = false;
            RebuildCover();
        }

        private int MaxConcurrentCoverTasks => 2;

        /// <summary>
        /// 把脏瓦片分组并启动掩护生成任务。对应 UE5 <c>RebuildCoverV2</c>（NarrativeRecastNavMesh.cpp:59-183）。
        /// </summary>
        public void RebuildCover()
        {
            if (DirtyTiles.Count == 0) return;

            // 导航未就绪时不生成掩护。对应 UE5「don't generate cover until nav mesh is done」
            if (!IsNavigationReadyForCover())
            {
                return;
            }

            var settings = ArsenalSettings.Instance;
            if (settings == null) return;
            var traceConfig = settings.CoverTraceConfig ?? new CoverTraceConfig();

            int groupLimit = Mathf.Max(1, TilesPerCoverTask);

            while (DirtyTiles.Count > 0 && RunningCoverTasks.Count < MaxConcurrentCoverTasks)
            {
                var tileRefs = new List<long>();
                var tileIndexes = new List<long>();
                for (int i = 0; i < groupLimit && DirtyTiles.Count > 0; i++)
                {
                    long tileRef = DirtyTiles[0];
                    DirtyTiles.RemoveAt(0);
                    if (tileRef < 0) continue;
                    if (!TileLookup.ContainsKey(tileRef)) continue;

                    tileRefs.Add(tileRef);
                    tileIndexes.Add(tileRef);
                }

                if (tileIndexes.Count == 0) continue;

                var pending = new RunningCoverTask { TileRefs = new List<long>(tileRefs) };

                // 不启动与已有工作分组共享瓦片的重复任务
                if (RunningCoverTasks.Contains(pending))
                {
                    continue;
                }

                var generator = new CoverTileGeneratorWrapper
                {
                    NavMesh = this,
                    TileRefs = tileRefs,
                    TileIndexes = tileIndexes,
                    ChainLinkAngleTolerance = settings.ChainLinkAngleTolerance,
                    SmallestChainLinkLength = settings.SmallestChainLinkLength,
                    ChainLinkCorrectionAngleTolerance = settings.ChainLinkCorrectionAngleTolerance,
                    CorrectionIterationCount = Mathf.Max(0, settings.CorrectionIterationCount),
                    TraceConfig = traceConfig,
                    PendingTileCreationTime = (float)Time.GameTime
                };

                generator.Setup();
                pending.AsyncTask = generator;
                RunningCoverTasks.Add(pending);
                NumRunningTasks = RunningCoverTasks.Count;
            }
        }

        /// <summary>推进运行中的掩护任务并回收已完成的。对应 UE5 <c>TickAsyncCoverBuild</c>（:203-227）。</summary>
        public void TickAsyncCoverBuild(float deltaSeconds)
        {
            if (RunningCoverTasks.Count == 0) return;

            int budget = ProbeBudgetPerFrame > 0 ? ProbeBudgetPerFrame : int.MaxValue;
            int running = 0;

            for (int i = RunningCoverTasks.Count - 1; i >= 0; i--)
            {
                var task = RunningCoverTasks[i];
                if (task.AsyncTask == null)
                {
                    RemoveRunningTaskAt(i);
                    continue;
                }

                task.AsyncTask.Step(ref budget);

                if (task.AsyncTask.IsDone)
                {
                    for (int t = 0; t < task.TileRefs.Count; t++)
                    {
                        DirtyTiles.Remove(task.TileRefs[t]);
                    }
                    RemoveRunningTaskAt(i);
                }
                else
                {
                    running++;
                }
            }

            if (NumRunningTasks > running)
            {
                NumRunningTasks = running;
                if (running == 0)
                {
                    OnCoverGenerationFinished();
                }
            }
        }

        private void RemoveRunningTaskAt(int index)
        {
            var task = RunningCoverTasks[index];
            task.AsyncTask = null;
            // UE5 用 RemoveAtSwap；这里同样用末元素交换删除以避免整体搬移
            int last = RunningCoverTasks.Count - 1;
            RunningCoverTasks[index] = RunningCoverTasks[last];
            RunningCoverTasks.RemoveAt(last);
        }

        private void CancelAllRunningTasks()
        {
            for (int i = RunningCoverTasks.Count - 1; i >= 0; i--)
            {
                var task = RunningCoverTasks[i];
                if (task.AsyncTask != null && !task.AsyncTask.IsDone)
                {
                    task.AsyncTask.Cancel();
                    // 取消中的任务把未完成瓦片退回脏列表，等待重建
                    for (int t = 0; t < task.TileRefs.Count; t++)
                    {
                        if (!DirtyTiles.Contains(task.TileRefs[t]))
                        {
                            DirtyTiles.Add(task.TileRefs[t]);
                        }
                    }
                }
                RunningCoverTasks.RemoveAt(i);
            }
            NumRunningTasks = RunningCoverTasks.Count;
        }

        /// <summary>掩护生成完成。对应 UE5 <c>OnCoverGenerationFinished</c>。</summary>
        protected virtual void OnCoverGenerationFinished()
        {
            CoverGenerationFinished?.Invoke();
        }

        /// <summary>
        /// 场景导航是否可用于掩护生成。对应 UE5 的
        /// <c>FRecastNavMeshGenerator::GetNumRemaningBuildTasks() == 0</c> 门禁。
        /// </summary>
        public bool IsNavigationReadyForCover()
        {
            if (FlaxEngine.Navigation.IsBuildingNavMesh) return false;
            return true;
        }

        /// <summary>取瓦片掩护数据。对应 UE5 <c>GetTileCover</c>。</summary>
        public TileCover GetTileCover(long tileIndex)
        {
            lock (TileCoverMutex)
            {
                if (TilesCovers.TryGetValue(tileIndex, out var cover) && cover != null)
                {
                    return cover;
                }
            }
            return TileCover.Invalid;
        }

        /// <summary>
        /// 提取瓦片内的墙边。对应 UE5 <c>GetEdgesInTile(FNavTileRef, out TArray&lt;FNavigationWallEdge&gt;)</c>。
        /// <para>
        /// Flax 托管层拿不到 Detour 多边形，这里用等间距占用探测替代：
        /// 在低掩高度与可越过高度各做一次球体占用检测，
        /// 若某格被挡而其邻格未被挡，则两格之间的公共面就是一面「墙」，据此生成边。
        /// </para>
        /// </summary>
        public void GetEdgesInTile(long tileIndex, List<NavigationWallEdge> outEdges)
        {
            if (outEdges == null) return;
            outEdges.Clear();

            if (!TileLookup.TryGetValue(tileIndex, out var coords)) return;

            float spacing = ProbeSpacing;
            float size = TileSize;
            var trace = ArsenalSettings.Instance?.CoverTraceConfig ?? new CoverTraceConfig();

            int cells = Mathf.Max(1, (int)Math.Round(size / spacing));
            float tileMinX = GridOrigin.X + coords.X * size;
            float tileMinZ = GridOrigin.Z + coords.Y * size;

            // 采样窗口向瓦片外各扩一格，保证跨过边界的墙在相邻两瓦片里都能被完整发现，
            // 且公共边界坐标由同一条公式给出（边界哈希要求精确相等）。
            int dim = cells + 2;
            var lowBlocked = new bool[dim * dim];
            var highBlocked = new bool[dim * dim];

            // 低掩探测半径不能大到让球体碰到地面本身，否则所有格子都会被判定为墙
            float lowRadius = Mathf.Min(spacing * 0.45f, trace.HalfHeight * 0.6f);
            float highRadius = trace.HalfHeight * 0.3f;

            for (int i = 0; i < dim; i++)
            {
                float wx = tileMinX + (i - 0.5f) * spacing;
                for (int j = 0; j < dim; j++)
                {
                    float wz = tileMinZ + (j - 0.5f) * spacing;
                    int idx = i * dim + j;

                    // 以该格的地面高度为基准，保证斜坡/高台上的掩护同样能被检出
                    if (!TryGetGroundHeight(wx, wz, out float groundY))
                    {
                        continue;
                    }

                    lowBlocked[idx] = Physics.CheckSphere(new Vector3(wx, groundY + trace.HalfHeight, wz), lowRadius, uint.MaxValue, false);
                    highBlocked[idx] = lowBlocked[idx]
                        && Physics.CheckSphere(new Vector3(wx, groundY + trace.PeekOverHeight, wz), highRadius, uint.MaxValue, false);
                }
            }

            // 只有落在本格内部的墙才会被写入本瓦片，跨界墙由两侧瓦片各自生成到公共边界线为止
            for (int i = 1; i <= cells; i++)
            {
                for (int j = 1; j <= cells; j++)
                {
                    int idx = i * dim + j;
                    if (!lowBlocked[idx]) continue;

                    // 低掩 = 低处被挡且高处没被挡（与 NarrativeNavigationSystem.TestPointForCoverType
                    // 同一条裁决，本体在 CoverGeometryRules.ClassifyCoverType）
                    bool bIsLowCover = CoverGeometryRules.ClassifyCoverType(lowBlocked[idx], highBlocked[idx], false, false).bIsLowCover;
                    float probeHeight = CoverGeometryRules.ResolveProbeHeight(bIsLowCover, trace.HalfHeight, trace.PeekOverHeight);

                    // 实体格子的四个方向中，凡是邻格自由的，其分界网格线上就有一面朝向该自由侧的墙
                    if (!IsBlockedAt(i + 1, j, dim, lowBlocked))
                        EmitEdge(outEdges, tileMinX, tileMinZ, i, j, spacing, EdgeDirection.PositiveX, probeHeight, bIsLowCover);
                    if (!IsBlockedAt(i - 1, j, dim, lowBlocked))
                        EmitEdge(outEdges, tileMinX, tileMinZ, i - 1, j, spacing, EdgeDirection.NegativeX, probeHeight, bIsLowCover);
                    if (!IsBlockedAt(i, j + 1, dim, lowBlocked))
                        EmitEdge(outEdges, tileMinX, tileMinZ, i, j, spacing, EdgeDirection.PositiveZ, probeHeight, bIsLowCover);
                    if (!IsBlockedAt(i, j - 1, dim, lowBlocked))
                        EmitEdge(outEdges, tileMinX, tileMinZ, i, j - 1, spacing, EdgeDirection.NegativeZ, probeHeight, bIsLowCover);
                }
            }

            MergeCollinearEdges(outEdges);
        }

        /// <summary>墙边朝向（仅水平四个方向）。</summary>
        private enum EdgeDirection
        {
            PositiveX,
            NegativeX,
            PositiveZ,
            NegativeZ
        }

        private static bool IsBlockedAt(int i, int j, int dim, bool[] blocked)
        {
            // 越出采样窗口的地方按自由处理，保证贴边墙仍能生成
            if (i < 0 || j < 0 || i >= dim || j >= dim) return false;
            return blocked[i * dim + j];
        }

        /// <summary>
        /// 向下探测格子地面高度。对应 UE5 直接从 Recast 多边形取地面高度的做法。
        /// 没有地面（悬空）的格子视为空，不参与掩护生成。
        /// </summary>
        private bool TryGetGroundHeight(float wx, float wz, out float groundY)
        {
            groundY = GridOrigin.Y;
            float probeTop = GridOrigin.Y + GroundProbeHeight;
            Vector3 origin = new Vector3(wx, probeTop, wz);
            try
            {
                if (Physics.RayCast(origin, Vector3.Down, out var hit, GroundProbeHeight, uint.MaxValue, false))
                {
                    groundY = hit.Point.Y;
                    return true;
                }
            }
            catch
            {
                // 物理场景未就绪：按无地面处理
            }
            return false;
        }

        /// <summary>地面探测的射线长度。</summary>
        public const float GroundProbeHeight = 20000f;

        /// <summary>
        /// 在分界网格线上发射一面墙边。
        /// <paramref name="lineIndex"/> 是墙面所在网格线的索引（沿法线轴），
        /// <paramref name="cellIndex"/> 是该墙所覆盖的格子在另一轴上的索引。
        /// 端点顺序保证 <c>Cross(Vector3.Up, End - Start) == normal</c>，与 <see cref="ChainLink"/> 构造约定一致。
        /// </summary>
        private static void EmitEdge(List<NavigationWallEdge> outEdges,
            float tileMinX, float tileMinZ,
            int lineIndex, int cellIndex, float spacing,
            EdgeDirection direction, float probeHeight, bool bIsLowCover)
        {
            Vector3 normal;
            Vector3 start;
            Vector3 end;

            switch (direction)
            {
                case EdgeDirection.PositiveX:
                    normal = new Vector3(1f, 0f, 0f);
                    start = new Vector3(tileMinX + lineIndex * spacing, probeHeight, tileMinZ + (cellIndex - 1) * spacing);
                    end = new Vector3(start.X, probeHeight, tileMinZ + cellIndex * spacing);
                    break;
                case EdgeDirection.NegativeX:
                    normal = new Vector3(-1f, 0f, 0f);
                    end = new Vector3(tileMinX + lineIndex * spacing, probeHeight, tileMinZ + (cellIndex - 1) * spacing);
                    start = new Vector3(end.X, probeHeight, tileMinZ + cellIndex * spacing);
                    break;
                case EdgeDirection.PositiveZ:
                    normal = new Vector3(0f, 0f, 1f);
                    end = new Vector3(tileMinX + (cellIndex - 1) * spacing, probeHeight, tileMinZ + lineIndex * spacing);
                    start = new Vector3(tileMinX + cellIndex * spacing, probeHeight, end.Z);
                    break;
                default:
                    normal = new Vector3(0f, 0f, -1f);
                    start = new Vector3(tileMinX + (cellIndex - 1) * spacing, probeHeight, tileMinZ + lineIndex * spacing);
                    end = new Vector3(tileMinX + cellIndex * spacing, probeHeight, start.Z);
                    break;
            }

            outEdges.Add(new NavigationWallEdge(start, end, normal, probeHeight, bIsLowCover));
        }

        /// <summary>把同一条网格线上首尾相接且属性相同的边合并为长边，减少链单元数量。</summary>
        private static void MergeCollinearEdges(List<NavigationWallEdge> edges)
        {
            if (edges.Count < 2) return;

            var merged = new List<NavigationWallEdge>(edges.Count);
            var used = new bool[edges.Count];

            edges.Sort((a, b) =>
            {
                int c = a.Normal.X.CompareTo(b.Normal.X);
                if (c != 0) return c;
                c = a.Normal.Z.CompareTo(b.Normal.Z);
                if (c != 0) return c;
                c = a.Start.Z.CompareTo(b.Start.Z);
                if (c != 0) return c;
                return a.Start.X.CompareTo(b.Start.X);
            });

            for (int i = 0; i < edges.Count; i++)
            {
                if (used[i]) continue;
                var current = edges[i];
                used[i] = true;

                bool grew = true;
                while (grew)
                {
                    grew = false;
                    for (int j = i + 1; j < edges.Count; j++)
                    {
                        if (used[j]) continue;
                        var candidate = edges[j];
                        if (!ApproxEqual(candidate.Normal, current.Normal)) continue;
                        if (candidate.bIsLowCover != current.bIsLowCover) continue;
                        if (Math.Abs(candidate.Height - current.Height) > 0.01f) continue;
                        if (!ApproxEqual(candidate.Start, current.End)) continue;

                        current.End = candidate.End;
                        used[j] = true;
                        grew = true;
                        break;
                    }
                }

                merged.Add(current);
            }

            edges.Clear();
            edges.AddRange(merged);
        }

        /// <summary>调试绘制所有已生成的掩护链。对应 UE5 <c>DebugDrawTemp</c>（:234-288）。</summary>
        public void DebugDrawCover()
        {
#if FLAX_EDITOR
            lock (TileCoverMutex)
            {
                foreach (var kvp in TilesCovers)
                {
                    var tile = kvp.Value;
                    if (tile == null || !tile.AnyCovers()) continue;
                    int coverNum = 0;
                    foreach (var chain in tile.OwnedCovers)
                    {
                        Color color = DebugColorFor(coverNum);
                        foreach (var link in chain.Chain)
                        {
                            DebugDraw.DrawLine(link.Start, link.End, color, 0f, false);
                            DebugDraw.DrawLine(link.Start, link.Start + link.CoverNormal * 50f, color, 0f, false);
                        }
                        coverNum++;
                    }
                    coverNum = 0;
                }
            }
#endif
        }

        private static Color DebugColorFor(int index)
        {
            var palette = new[]
            {
                new Color(1f, 0.3f, 0.3f, 1f),
                new Color(0.3f, 1f, 0.4f, 1f),
                new Color(0.3f, 0.6f, 1f, 1f),
                new Color(1f, 0.9f, 0.2f, 1f),
                new Color(0.9f, 0.4f, 1f, 1f),
            };
            return palette[Math.Abs(index) % palette.Length];
        }
    }
}

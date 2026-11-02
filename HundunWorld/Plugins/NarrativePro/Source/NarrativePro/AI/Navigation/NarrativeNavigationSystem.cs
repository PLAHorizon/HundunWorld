using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.AI.Cover;
using NarrativePro.Core;

namespace NarrativePro.AI.Navigation
{
    /// <summary>
    /// 导航系统门面：掩护查询与覆盖重建。对应 UE5 <c>UNarrativeNavigationSystem</c>
    /// （<c>Private/AI/Navigation/NarrativeNavigationSystem.cpp</c>，368 行）。
    /// <para>
    /// UE5 里它是 <c>UNavigationSystemV1</c> 的子类并由引擎自动实例化；Flax 的托管导航 API
    /// 只给出路径/投影查询而没有导航系统子类挂点，因此这里以静态门面实现同一组可观测行为，
    /// 数据源是场景中的 <see cref="NarrativeRecastNavMesh"/>。
    /// </para>
    /// </summary>
    public static class NarrativeNavigationSystem
    {
        /// <summary>取当前掩护数据源。对应 UE5 <c>GetNavDataForProps</c>。</summary>
        public static NarrativeRecastNavMesh GetNavData(object worldContextObject)
        {
            return NarrativeRecastNavMesh.Instance;
        }

        /// <summary>取掩护射线配置。对应 UE5 <c>GetCoverTraceConfig</c>（:349-353）。</summary>
        public static CoverTraceConfig GetCoverTraceConfig()
        {
            var settings = ArsenalSettings.Instance;
            return settings?.CoverTraceConfig ?? new CoverTraceConfig();
        }

        /// <summary>
        /// 收集查询点所在瓦片及其邻接瓦片内的全部掩护。
        /// 对应 UE5 <c>FindAllCoverInRadiusToPoint</c>（:77-159）。
        /// </summary>
        /// <param name="worldContextObject">世界上下文对象</param>
        /// <param name="point">查询点</param>
        /// <param name="foundCover">输出：找到的掩护链</param>
        /// <param name="searchRadius">搜索半径</param>
        /// <returns>是否成功取到掩护数据</returns>
        public static bool FindAllCoverInRadiusToPoint(object worldContextObject, Vector3 point,
            List<CoverContainer> foundCover, float searchRadius)
        {
            if (foundCover == null) return false;

            var navMesh = GetNavData(worldContextObject);
            if (navMesh == null) return false;

            // 收集阶段只用瓦片邻接，不用 searchRadius（UE5 :174 的 TODO 原样保留）
            var collected = CollectCoverGeoms(navMesh, point);
            if (collected == null) return false;

            for (int i = 0; i < collected.Count; i++)
            {
                var cover = collected[i];
                foundCover.Add(new CoverContainer(cover.CoverChain.ToChainLinks(), cover.ParentTileIndex, (sbyte)cover.CoverChainIndex));
            }

            return true;
        }

        /// <summary>
        /// 收集「查询点所在瓦片 + 8 邻接瓦片」里的全部掩护链，转成纯几何容器供规则层使用。
        /// 对应 UE5 <c>FindAllCoverInRadiusToPoint</c> 的瓦片遍历（:136-157）；
        /// 「空链不收集」这条判据本体住在 <see cref="CoverGeometryRules.CollectTileCovers"/>。
        /// 返回 null 表示取不到掩护数据源（对应 UE5 的 false 分支）。
        /// </summary>
        private static List<CoverContainerGeom> CollectCoverGeoms(NarrativeRecastNavMesh navMesh, Vector3 point)
        {
            // 查询点必须先能落到某个导航瓦片。对应 UE5 的 FindNearestPoly + GetPolyTileIndex
            long initialTileIndex = ResolveTileIndex(navMesh, point);
            if (initialTileIndex < 0) return null;

            // 取该瓦片与 8 个邻接瓦片
            var tileIndexes = new List<long> { initialTileIndex };
            long[] neighbours = navMesh.GetNeighbourTileIndexes(initialTileIndex);
            for (int i = 0; i < neighbours.Length; i++)
            {
                if (neighbours[i] >= 0 && !tileIndexes.Contains(neighbours[i]))
                {
                    tileIndexes.Add(neighbours[i]);
                }
            }

            var collected = new List<CoverContainerGeom>();
            lock (navMesh.TileCoverMutex)
            {
                for (int t = 0; t < tileIndexes.Count; t++)
                {
                    long tileIndex = tileIndexes[t];
                    if (!navMesh.TilesCovers.TryGetValue(tileIndex, out var tileCover) || tileCover == null)
                    {
                        continue;
                    }
                    if (!tileCover.AnyCovers()) continue;

                    collected.AddRange(CoverGeometryRules.CollectTileCovers(tileCover.OwnedCovers.ToGeomChains(), tileIndex));
                }
            }

            return collected;
        }

        /// <summary>
        /// 找查询点的最近掩护，并沿边界哈希把跨瓦片的同一条掩护串联成完整链。
        /// 对应 UE5 <c>FindNearestCoverToPoint</c>（:161-225）。
        /// </summary>
        /// <param name="worldContextObject">世界上下文对象</param>
        /// <param name="point">查询点</param>
        /// <param name="foundCover">输出：完整掩护链（可能跨多个瓦片）</param>
        /// <param name="chainLinkIndex">输出：链中最近单元的索引</param>
        /// <param name="nearestPoint">输出：链上最近点</param>
        /// <param name="searchRadius">搜索半径</param>
        /// <param name="directionBias">方向偏置（传入 Vector3.Up 表示不做方向限制）</param>
        /// <param name="directionBiasToleranceAngle">方向偏置容差（度）</param>
        public static bool FindNearestCoverToPoint(object worldContextObject, Vector3 point,
            List<ChainLink> foundCover, out int chainLinkIndex, out Vector3 nearestPoint,
            float searchRadius, Vector3 directionBias = default, float directionBiasToleranceAngle = 45f)
        {
            chainLinkIndex = -1;
            nearestPoint = Vector3.Zero;
            if (foundCover == null) return false;
            foundCover.Clear();

            var navMesh = GetNavData(worldContextObject);
            if (navMesh == null) return false;

            var foundCoverInRadius = CollectCoverGeoms(navMesh, point);
            if (foundCoverInRadius == null)
            {
                return false;
            }

            // 半径内候选按距离排序 + 方向偏置过滤（本体在纯接缝，UE5 :186-205）
            var ranked = CoverGeometryRules.RankCoversInRadius(point.ToGeom(), foundCoverInRadius, searchRadius,
                directionBias.ToGeom(), directionBiasToleranceAngle, Mathf.Epsilon);

            if (ranked.Count == 0)
            {
                return false;
            }

            // 排序首个即最近候选：同距离平局取靠前候选，与 UE5 的严格 < 一致
            var firstCover = foundCoverInRadius[ranked[0].CandidateIndex];
            nearestPoint = ranked[0].NearestPoint.ToVector3();

            var reader = new NavMeshCoverReader(navMesh);
            var stitched = CoverGeometryRules.StitchCoverAcrossTiles(reader, firstCover,
                ArsenalSettings.Instance != null ? ArsenalSettings.Instance.ChainLinkAngleTolerance : 45f, Mathf.Epsilon, out _);

            if (stitched.Count == 0) return false;

            foundCover.AddRange(stitched.ToChainLinks());

            // 串联后链已变化，重新求最近单元索引
            return FindNearestCoverLink(point, foundCover, out chainLinkIndex, out nearestPoint);
        }

        /// <summary>
        /// 在链上找离点最近的单元。对应 UE5 <c>FindNearestCoverLink</c>（:227-250），
        /// 投影与「同距离取靠后单元」的判据本体住在
        /// <see cref="CoverGeometryRules.FindNearestCoverLink"/>。
        /// </summary>
        public static bool FindNearestCoverLink(Vector3 point, List<ChainLink> chainLinks,
            out int chainLinkIndex, out Vector3 nearestPoint)
        {
            var hit = CoverGeometryRules.FindNearestCoverLink(point.ToGeom(), chainLinks.ToGeomLinks(), Mathf.Epsilon);
            chainLinkIndex = hit.ChainLinkIndex;
            nearestPoint = hit.NearestPoint.ToVector3();
            return hit.bFound;
        }

        /// <summary>
        /// 顺着边界哈希取掩护在给定方向上的下一段。
        /// 对应 UE5 <c>FindNextSectionOfCover</c>（:252-314）；哈希邻接与端点相容判据的本体住在
        /// <see cref="CoverGeometryRules.FindNextSectionOfCover"/>，这里只把瓦片数据适配成纯视图，
        /// 并把结果按 (瓦片, 链下标) 还原回引擎侧的真实链引用（保持 UE5 的共享语义）。
        /// </summary>
        /// <param name="navMesh">数据源</param>
        /// <param name="cover">当前掩护</param>
        /// <param name="bSearchAtStartOfCover">true 表示向链头方向找</param>
        /// <param name="outCover">输出：下一段掩护</param>
        public static bool FindNextSectionOfCover(NarrativeRecastNavMesh navMesh, CoverContainer cover,
            bool bSearchAtStartOfCover, out CoverContainer outCover)
        {
            outCover = null;
            if (navMesh == null || cover == null || !cover.IsValid()) return false;
            if (cover.CoverChain.Count == 0) return false;

            var settings = ArsenalSettings.Instance;
            float angleTolerance = settings != null ? settings.ChainLinkAngleTolerance : 45f;

            CoverContainerGeom pure;
            lock (navMesh.TileCoverMutex)
            {
                var reader = new NavMeshCoverReader(navMesh);
                if (!CoverGeometryRules.FindNextSectionOfCover(reader, cover.ToGeom(), bSearchAtStartOfCover,
                        angleTolerance, Mathf.Epsilon, out pure))
                {
                    return false;
                }
            }

            lock (navMesh.TileCoverMutex)
            {
                if (!navMesh.TilesCovers.TryGetValue(pure.ParentTileIndex, out var tile) || tile == null)
                {
                    return false;
                }
                if (pure.CoverChainIndex < 0 || pure.CoverChainIndex >= tile.OwnedCovers.Count)
                {
                    return false;
                }
                var chain = tile.OwnedCovers[pure.CoverChainIndex];
                if (chain == null || chain.Chain.Count == 0) return false;

                outCover = new CoverContainer(chain.Chain, pure.ParentTileIndex, (sbyte)pure.CoverChainIndex);
            }
            return true;
        }

        /// <summary>
        /// 检测某个位置作为掩护的类型：低掩 / 可左探 / 可右探。
        /// 对应 UE5 <c>TestPointForCoverType</c>（:316-347）。四条射线的命中由物理给出，
        /// 「低掩 = 低处被挡且高处没被挡，低掩会把左右探一起压掉」这条裁决住在
        /// <see cref="CoverGeometryRules.ClassifyCoverType"/>。
        /// </summary>
        /// <param name="worldContextObject">世界上下文对象</param>
        /// <param name="point">掩护面上的点</param>
        /// <param name="direction">掩护朝向（指向角色所在的一侧）</param>
        /// <param name="bIsLowCover">输出：是否为低掩（低处被挡、高处可越过）</param>
        /// <param name="bCanPeekLeft">输出：向左探身是否可见</param>
        /// <param name="bCanPeekRight">输出：向右探身是否可见</param>
        /// <returns>该点是否是任何形式的可用掩护</returns>
        public static bool TestPointForCoverType(object worldContextObject, Vector3 point, Vector3 direction,
            out bool bIsLowCover, out bool bCanPeekLeft, out bool bCanPeekRight)
        {
            var coverTraceConfig = GetCoverTraceConfig();
            Vector3 directionNormalised = Flatten(direction);
            if (directionNormalised.LengthSquared <= Mathf.Epsilon)
            {
                bIsLowCover = false;
                bCanPeekLeft = false;
                bCanPeekRight = false;
                return false;
            }
            directionNormalised = Vector3.Normalize(directionNormalised);

            bool bLowHit = IsCoverTraceBlocked(point, directionNormalised, coverTraceConfig.Low(), coverTraceConfig.TraceDepth);
            bool bHighHit = IsCoverTraceBlocked(point, directionNormalised, coverTraceConfig.High(), coverTraceConfig.TraceDepth);

            // 与 UE5 的 && 短路一致：已经是低掩时不再发射左右探射线
            bool lowCover = bLowHit && !bHighHit;
            bool bLeanLeftHit = false;
            bool bLeanRightHit = false;
            if (!lowCover)
            {
                bLeanLeftHit = IsCoverTraceBlocked(point, directionNormalised, coverTraceConfig.LeanLeft(), coverTraceConfig.TraceDepth);
                bLeanRightHit = IsCoverTraceBlocked(point, directionNormalised, coverTraceConfig.LeanRight(), coverTraceConfig.TraceDepth);
            }

            var classified = CoverGeometryRules.ClassifyCoverType(bLowHit, bHighHit, bLeanLeftHit, bLeanRightHit);
            bIsLowCover = classified.bIsLowCover;
            bCanPeekLeft = classified.bCanPeekLeft;
            bCanPeekRight = classified.bCanPeekRight;

            return classified.IsAnyCover;
        }

        /// <summary>
        /// 单条掩护探测射线。对应 UE5 内部 <c>IsCoverTraceBlocked</c> lambda（:319-338）。
        /// UE5 把局部偏移按方向旋转：<c>Direction.Rotation().RotateVector(InTracePosition)</c>；
        /// Flax 为 Y-up，故局部轴映射为 X=侧向、Y=前向(沿 direction)、Z=高度。
        /// 基向量与偏移的合成住在 <see cref="CoverGeometryRules.BuildTraceRay"/>，本函数只负责发物理射线。
        /// </summary>
        private static bool IsCoverTraceBlocked(Vector3 inPoint, Vector3 directionNormalised, Vector3 inTracePosition, float traceDepth)
        {
            CoverGeometryRules.BuildTraceRay(inPoint.ToGeom(), directionNormalised.ToGeom(), inTracePosition.ToGeom(),
                traceDepth, out var pureStart, out _);
            Vector3 traceStart = pureStart.ToVector3();

            try
            {
                return Physics.RayCast(traceStart, directionNormalised, out _, traceDepth, uint.MaxValue, false);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 全域重建掩护。对应 UE5 <c>RebuildAllCover</c>（:355-368）。
        /// UE5 只在编辑器下把 <c>RebuildAll</c> 推迟到下一 tick；这里运行时同样适用。
        /// </summary>
        public static void RebuildAllCover()
        {
            var navMesh = GetNavData(null);
            navMesh?.RebuildAll();
        }

        /// <summary>链单元修正过滤。对应 UE5 <c>FNarrativeTileCoverGenerator::FilterLinks</c>，
        /// 由 <see cref="CoverTileGeneratorWrapper.FilterLinks"/> 承载。</summary>
        public static void FilterLinks(List<CoverChainContainer> covers, float minLinkLength, float maxAngleForMerge, int iterationCount)
        {
            CoverTileGeneratorWrapper.FilterLinks(covers, minLinkLength, maxAngleForMerge, iterationCount);
        }

        // ===== 内部辅助 =====

        /// <summary>把点归到导航瓦片；必要时投影到导航网格。对应 UE5 <c>FindNearestPoly</c>。</summary>
        private static long ResolveTileIndex(NarrativeRecastNavMesh navMesh, Vector3 point)
        {
            long index = navMesh.GetTileIndexForPoint(point);
            if (navMesh.TileCoversContain(index))
            {
                return index;
            }

            // 点落在尚未登记的瓦片：按掩检索容差就近找一个已登记瓦片
            Int2 coords = navMesh.GetTileCoords(point);
            for (int ring = 1; ring <= 2; ring++)
            {
                for (int dx = -ring; dx <= ring; dx++)
                {
                    for (int dz = -ring; dz <= ring; dz++)
                    {
                        if (Math.Abs(dx) != ring && Math.Abs(dz) != ring) continue;
                        long candidate = navMesh.GetTileIndex(new Int2(coords.X + dx, coords.Y + dz));
                        if (navMesh.TileCoversContain(candidate))
                        {
                            return candidate;
                        }
                    }
                }
            }

            return -1;
        }

        /// <summary>把向量压平到 XZ 平面（Flax 的「2D 地面」）。对应 UE5 <c>GetSafeNormal2D</c>。</summary>
        private static Vector3 Flatten(Vector3 value)
        {
            return new Vector3(value.X, 0f, value.Z);
        }

        /// <summary>
        /// 把 <see cref="NarrativeRecastNavMesh.TilesCovers"/> 适配成纯接缝要的瓦片视图
        /// （链 + 已量化过的边界哈希）。一次查询内做瓦片级缓存，避免串联时反复搬运同一条链。
        /// </summary>
        private sealed class NavMeshCoverReader : ICoverTileReader
        {
            private readonly NarrativeRecastNavMesh _navMesh;
            private readonly Dictionary<long, CoverTileGeom> _cache = new Dictionary<long, CoverTileGeom>();

            public NavMeshCoverReader(NarrativeRecastNavMesh navMesh)
            {
                _navMesh = navMesh;
            }

            public bool TryGetTile(long tileIndex, out CoverTileGeom tile)
            {
                if (_cache.TryGetValue(tileIndex, out tile))
                {
                    return tile != null;
                }

                tile = null;
                if (_navMesh == null || !_navMesh.TilesCovers.TryGetValue(tileIndex, out var cover) || cover == null)
                {
                    return false;
                }

                var geom = new CoverTileGeom { Chains = cover.OwnedCovers.ToGeomChains() };
                foreach (var kvp in cover.BoundaryHash)
                {
                    // 写入时已量化（AddBoundaryEntry -> Quantize），这里再量化一次是幂等的
                    geom.BoundaryHash[kvp.Key.ToGeom().Quantized(CoverGeometryRules.HashQuantizeScale)] =
                        new CoverBoundaryGeom(kvp.Value.NextTileIndex, kvp.Value.CoverChainIndex);
                }

                tile = geom;
                _cache[tileIndex] = tile;
                return true;
            }
        }
    }
}

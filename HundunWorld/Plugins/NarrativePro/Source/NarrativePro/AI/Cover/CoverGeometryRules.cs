// 模块关系说明：
// 本文件是 NarrativePro.AI.Cover 的「掩护几何纯接缝」：掩护链的投影/最近单元、链相接判定、
// 合并与修正（FilterLinks）、跨瓦片串联（边界哈希邻接）、候选排序与半径剔除、掩护类型裁决
// 全部住在这里，**不引用任何 FlaxEngine 类型**（Vector3/Mathf/Physics/Script 都不出现）。
//
// 为什么要有它：这些规则原本和引擎调用长在一起（Physics.RayCast 探测、NarrativeRecastNavMesh 是 Script、
// 瓦片数据在字典里），进程外一条都跑不到 —— 而恰恰是这一层承载了 UE5 里最容易写错的几条约束：
//   - FMath::ClosestPointOnSegment 的投影与端点钳制；
//   - FindNearestCoverLink 用 <=（同距离取靠后单元），FindNearestCoverToPoint 用 <（同距离取靠前候选）；
//   - DoesLinkFitInChain 要求「共享恰好一个顶点 + 方向夹角在容差内」；
//   - FilterLinks 的 minLinkLength / maxAngleForMerge / iterationCount 三条合并判据；
//   - 跨瓦片串联必须有 visited 集合保证在环状边界哈希下终止。
// 线上通路（NarrativeNavigationSystem / NarrativeTileCoverGenerator / NarrativeRecastNavMesh /
// CoverTraceConfig）都改成调用本文件的同一份实现，harness 也调用它，因此测试不会退化成复刻。
//
// UE5 依据（只读参考工程 C:\Works\GitHubProjects\20260801\HundunWorld\Plugins\NarrativePro\Source\NarrativeArsenal）：
//   - Private/AI/Navigation/NarrativeNavigationSystem.cpp:25-43  AddToChainLinkArrOrdered
//   - Private/AI/Navigation/NarrativeNavigationSystem.cpp:45-75  WalkCoverInDirection
//   - Private/AI/Navigation/NarrativeNavigationSystem.cpp:77-159 FindAllCoverInRadiusToPoint
//   - Private/AI/Navigation/NarrativeNavigationSystem.cpp:161-225 FindNearestCoverToPoint
//   - Private/AI/Navigation/NarrativeNavigationSystem.cpp:227-250 FindNearestCoverLink
//   - Private/AI/Navigation/NarrativeNavigationSystem.cpp:252-314 FindNextSectionOfCover
//   - Private/AI/Navigation/NarrativeNavigationSystem.cpp:316-347 TestPointForCoverType
//   - Private/AI/Navigation/NarrativeRecastNavMesh.cpp:20-44     DoesLinkFitInChain
//   - Private/AI/Cover/NarrativeTileCoverGenerator.cpp:8-19      GetTargetNeighbour
//   - Private/AI/Cover/NarrativeTileCoverGenerator.cpp:235-253   AddLinkToExistingChain
//   - Private/AI/Cover/NarrativeTileCoverGenerator.cpp:255-312   MergeCovers
//   - Private/AI/Cover/NarrativeTileCoverGenerator.cpp:314-359   FilterLinks

using System;
using System.Collections.Generic;

namespace NarrativePro.AI.Cover
{
    /// <summary>纯三维坐标（与引擎 Vector3 逐分量对应，转换在 CoverTypes.cs 的适配层做）。</summary>
    public struct CoverVec3
    {
        public float X;
        public float Y;
        public float Z;

        public CoverVec3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static CoverVec3 Zero => new CoverVec3(0f, 0f, 0f);

        /// <summary>引擎 Vector3.Up 在本工程（Flax Y-up）里的分量，掩护射线基向量用它。</summary>
        public static CoverVec3 Up => new CoverVec3(0f, 1f, 0f);

        public float LengthSquared => X * X + Y * Y + Z * Z;
        public float Length => (float)System.Math.Sqrt(LengthSquared);

        public static CoverVec3 operator +(CoverVec3 a, CoverVec3 b) => new CoverVec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static CoverVec3 operator -(CoverVec3 a, CoverVec3 b) => new CoverVec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static CoverVec3 operator *(CoverVec3 a, float s) => new CoverVec3(a.X * s, a.Y * s, a.Z * s);

        public static float Dot(CoverVec3 a, CoverVec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

        public static CoverVec3 Cross(CoverVec3 a, CoverVec3 b) => new CoverVec3(
            a.Y * b.Z - a.Z * b.Y,
            a.Z * b.X - a.X * b.Z,
            a.X * b.Y - a.Y * b.X);

        public static float Distance(CoverVec3 a, CoverVec3 b) => (a - b).Length;
        public static float DistanceSquared(CoverVec3 a, CoverVec3 b) => (a - b).LengthSquared;

        /// <summary>UE5 <c>GetSafeNormal()</c>：长度不足容差时返回零向量。</summary>
        public CoverVec3 SafeNormal(float epsilon)
        {
            float lenSq = LengthSquared;
            if (lenSq <= epsilon) return Zero;
            float len = (float)System.Math.Sqrt(lenSq);
            return len > 0f ? this * (1f / len) : Zero;
        }

        /// <summary>UE5 <c>GetSafeNormal2D()</c> 在本工程的等价：压平到 XZ 地面后归一。</summary>
        public CoverVec3 FlattenToGround() => new CoverVec3(X, 0f, Z);

        /// <summary>
        /// 顶点近似相等。沿用本工程既有的 <c>NarrativeRecastNavMesh.ApproxEqual</c> 口径（严格 <c>&lt;</c>），
        /// 对应 UE5 <c>FVector::Equals(..., 0.1)</c>（NarrativeRecastNavMesh.cpp:28-29）。
        /// 两侧只在「两点恰好相距 0.1」时才有差别，而掩护坐标都由网格间距公式生成，取不到这个值，
        /// 因此这里保持端口既有比较不变，不借抽取之机改动线上判定。
        /// </summary>
        public bool ApproxEqual(CoverVec3 other, float tolerance)
        {
            return System.Math.Abs(X - other.X) < tolerance
                && System.Math.Abs(Y - other.Y) < tolerance
                && System.Math.Abs(Z - other.Z) < tolerance;
        }

        /// <summary>边界哈希键量化（跨瓦片同一点必须得到同一个键）。</summary>
        public CoverVec3 Quantized(float scale)
        {
            return new CoverVec3(
                (float)System.Math.Round(X * scale) / scale,
                (float)System.Math.Round(Y * scale) / scale,
                (float)System.Math.Round(Z * scale) / scale);
        }

        public override string ToString() => $"({X}, {Y}, {Z})";
    }

    /// <summary>纯链单元（对应 UE5 FChainLink：起点/终点/朝向掩护面的法线/来源瓦片）。</summary>
    public struct CoverLinkGeom
    {
        public ulong ParentTileIndex;
        public CoverVec3 Start;
        public CoverVec3 End;
        public CoverVec3 CoverNormal;

        public CoverLinkGeom(ulong parentTileIndex, CoverVec3 start, CoverVec3 end, CoverVec3 coverNormal)
        {
            ParentTileIndex = parentTileIndex;
            Start = start;
            End = end;
            CoverNormal = coverNormal;
        }

        /// <summary>UE5 <c>GetStartNormal()</c>：终点 - 起点 的安全法线（沿链前进方向）。</summary>
        public CoverVec3 GetStartNormal(float epsilon) => (End - Start).SafeNormal(epsilon);

        /// <summary>UE5 <c>GetEndNormal()</c>：起点 - 终点 的安全法线。</summary>
        public CoverVec3 GetEndNormal(float epsilon) => (Start - End).SafeNormal(epsilon);

        /// <summary>UE5 <c>Length()</c>。</summary>
        public float Length() => (End - Start).Length;
    }

    /// <summary>纯掩护链（对应 UE5 FCoverChainContainer）。</summary>
    public sealed class CoverChainGeom
    {
        public List<CoverLinkGeom> Chain = new List<CoverLinkGeom>(2);

        public CoverChainGeom() { }

        public CoverChainGeom(List<CoverLinkGeom> chain)
        {
            Chain = chain ?? new List<CoverLinkGeom>();
        }

        /// <summary>UE5 <c>Start()</c>：空链返回默认单元（与 UE5 Chain[0] 越界语义不同，本工程沿用既有的安全返回）。</summary>
        public CoverLinkGeom Start() => Chain.Count > 0 ? Chain[0] : default;
        public CoverLinkGeom End() => Chain.Count > 0 ? Chain[Chain.Count - 1] : default;
    }

    /// <summary>纯边界哈希项（对应 UE5 FBoundaryHashInfo）。</summary>
    public struct CoverBoundaryGeom
    {
        public long NextTileIndex;
        public int CoverChainIndex;

        public CoverBoundaryGeom(long nextTileIndex, int coverChainIndex)
        {
            NextTileIndex = nextTileIndex;
            CoverChainIndex = coverChainIndex;
        }

        public bool IsValid() => NextTileIndex != -1 && CoverChainIndex != -1;
    }

    /// <summary>纯瓦片掩护视图（对应 UE5 FTileCover 的可查询部分）。</summary>
    public sealed class CoverTileGeom
    {
        public List<CoverChainGeom> Chains = new List<CoverChainGeom>(4);
        public Dictionary<CoverVec3, CoverBoundaryGeom> BoundaryHash = new Dictionary<CoverVec3, CoverBoundaryGeom>();

        public bool AnyCovers()
        {
            for (int i = 0; i < Chains.Count; i++)
            {
                if (Chains[i] != null && Chains[i].Chain.Count > 0) return true;
            }
            return false;
        }

        public bool HashContains(CoverVec3 point) => BoundaryHash.ContainsKey(point);

        public bool TryGetBoundary(CoverVec3 point, out CoverBoundaryGeom info)
        {
            return BoundaryHash.TryGetValue(point, out info);
        }
    }

    /// <summary>纯掩护容器（对应 UE5 FCoverContainer）。</summary>
    public struct CoverContainerGeom
    {
        public long ParentTileIndex;
        public int CoverChainIndex;
        public List<CoverLinkGeom> CoverChain;

        public CoverContainerGeom(long parentTileIndex, int coverChainIndex, List<CoverLinkGeom> chain)
        {
            ParentTileIndex = parentTileIndex;
            CoverChainIndex = coverChainIndex;
            CoverChain = chain;
        }

        public bool IsValid() => ParentTileIndex != -1 && CoverChainIndex != -1
                                 && CoverChain != null && CoverChain.Count > 0;

        public CoverLinkGeom Start() => CoverChain != null && CoverChain.Count > 0 ? CoverChain[0] : default;
        public CoverLinkGeom End() => CoverChain != null && CoverChain.Count > 0 ? CoverChain[CoverChain.Count - 1] : default;
    }

    /// <summary>
    /// 瓦片掩护数据的最小可注入形态：线上侧由 <c>NarrativeRecastNavMesh.TilesCovers</c> 适配，
    /// harness 直接喂纯数据瓦片 —— 串联与终止性规则两侧跑的是同一份实现。
    /// </summary>
    public interface ICoverTileReader
    {
        /// <summary>取瓦片的掩护视图；不存在返回 false。对应 UE5 <c>NavMesh->TilesCovers[Index]</c>。</summary>
        bool TryGetTile(long tileIndex, out CoverTileGeom tile);
    }

    /// <summary>一条掩护链上离查询点最近的单元（对应 UE5 FindNearestCoverLink 的输出）。</summary>
    public struct CoverNearestHit
    {
        public int ChainLinkIndex;
        public CoverVec3 NearestPoint;
        public bool bFound;
    }

    /// <summary>半径内一条候选掩护的距离命中（用于排序与最近选择）。</summary>
    public struct CoverDistanceHit
    {
        public int CandidateIndex;
        public int ChainLinkIndex;
        public CoverVec3 NearestPoint;
        public float Distance;
    }

    /// <summary>掩护类型裁决结果（对应 UE5 TestPointForCoverType 的三个出参）。</summary>
    public struct CoverTypeResult
    {
        public bool bIsLowCover;
        public bool bCanPeekLeft;
        public bool bCanPeekRight;

        /// <summary>UE5 返回值：<c>bIsLowCover || bCanPeekLeft || bCanPeekRight</c>。</summary>
        public bool IsAnyCover => bIsLowCover || bCanPeekLeft || bCanPeekRight;
    }

    /// <summary>
    /// 掩护几何与决策纯规则集。
    /// </summary>
    public static class CoverGeometryRules
    {
        /// <summary>UE5 <c>FVector::Equals(..., 0.1)</c> 的顶点相等容差（NarrativeRecastNavMesh.cpp:28-29）。</summary>
        public const float VertexTolerance = 0.1f;

        /// <summary>UE5 <c>UE_KINDA_SMALL_NUMBER</c>（FilterLinks / DoesLinkFitInChain 的浮点补偿）。</summary>
        public const float KindaSmallNumber = 1e-4f;

        /// <summary>边界哈希键量化尺度（CoverTileGeneratorWrapper.Quantize）。</summary>
        public const float HashQuantizeScale = 10f;

        // ================= 投影 / 最近单元 =================

        /// <summary>
        /// 点到线段最近点。对应 UE5 <c>FMath::ClosestPointOnSegment(Point, SegmentStart, SegmentEnd)</c>
        /// （调用处见 NarrativeNavigationSystem.cpp:238，注意 UE5 传的是 (Point, ChainLink.End, ChainLink.Start)）。
        /// </summary>
        public static CoverVec3 ClosestPointOnSegment(CoverVec3 point, CoverVec3 segStart, CoverVec3 segEnd, float epsilon)
        {
            CoverVec3 segment = segEnd - segStart;
            float lengthSq = segment.LengthSquared;
            if (lengthSq <= epsilon) return segStart;

            float t = CoverVec3.Dot(point - segStart, segment) / lengthSq;
            if (t < 0f) t = 0f;
            else if (t > 1f) t = 1f;
            return segStart + segment * t;
        }

        /// <summary>
        /// 链上离点最近的单元。对应 UE5 <c>FindNearestCoverLink</c>（NarrativeNavigationSystem.cpp:227-250）。
        /// 判据是 <c>DistToCover &lt;= LastDistanceToLink</c>（:240），因此**同距离时靠后的单元胜出**，
        /// 这是 UE5 的原样行为（不是「任选一个」），必须钉住。
        /// </summary>
        public static CoverNearestHit FindNearestCoverLink(CoverVec3 point, IList<CoverLinkGeom> chainLinks, float epsilon)
        {
            var hit = new CoverNearestHit { ChainLinkIndex = -1, NearestPoint = CoverVec3.Zero, bFound = false };
            if (chainLinks == null || chainLinks.Count == 0) return hit;

            float lastDistanceToLink = float.MaxValue;
            for (int linkIndex = 0; linkIndex < chainLinks.Count; linkIndex++)
            {
                CoverLinkGeom chainLink = chainLinks[linkIndex];
                CoverVec3 closePointOnLink = ClosestPointOnSegment(point, chainLink.End, chainLink.Start, epsilon);
                float distToCover = CoverVec3.Distance(point, closePointOnLink);

                if (distToCover <= lastDistanceToLink)
                {
                    lastDistanceToLink = distToCover;
                    hit.ChainLinkIndex = linkIndex;
                    hit.NearestPoint = closePointOnLink;
                }
            }

            hit.bFound = hit.ChainLinkIndex != -1;
            return hit;
        }

        // ================= 链相接判定 =================

        /// <summary>
        /// 候选单元能否接进给定单元。对应 UE5 <c>ANarrativeRecastNavMesh::DoesLinkFitInChain</c>
        /// （NarrativeRecastNavMesh.cpp:20-44）：共享**恰好一个**顶点（两个都相同视为同一条边 = 冲突链），
        /// 且两条边的方向夹角在容差内（余弦比较减去 <see cref="KindaSmallNumber"/> 补偿浮点误差）。
        /// </summary>
        public static bool DoesLinkFitInChain(CoverLinkGeom linkCandidate, CoverLinkGeom chainLink, float angleToleranceDegrees, float epsilon)
        {
            CoverVec3 candidateNormal = linkCandidate.GetStartNormal(epsilon);
            if (candidateNormal.LengthSquared <= epsilon)
            {
                return false;
            }

            CoverVec3 linkNormal = chainLink.GetStartNormal(epsilon);
            bool bStartPass = linkCandidate.Start.ApproxEqual(chainLink.End, VertexTolerance);
            bool bEndPass = chainLink.Start.ApproxEqual(linkCandidate.End, VertexTolerance);

            if (bStartPass == bEndPass)
            {
                return false;
            }

            float dirDot = CoverVec3.Dot(candidateNormal, linkNormal);
            return dirDot >= (Cos(angleToleranceDegrees) - KindaSmallNumber);
        }

        // ================= 合并 / 修正 =================

        /// <summary>
        /// 合并本应相连的掩护链。对应 UE5 <c>MergeCovers</c>（NarrativeTileCoverGenerator.cpp:255-312）。
        /// 返回循环结束后**仍持有单元的链下标**（升序），等价于 UE5 末尾的
        /// <c>Covers.RemoveAll(IsEmpty)</c>；被清空的链在返回下标里缺席，但原列表位置不变，
        /// 以便调用方把结果写回自己的容器。
        /// 返回 <c>null</c> 表示走了 UE5 :260-263 的早退分支（不足两条链时**连 RemoveAll 都不执行**，
        /// 因此一条空链会原样留着）—— 这条差异也要能被断言到，不能悄悄归一化。
        /// </summary>
        public static List<int> MergeCovers(IList<CoverChainGeom> covers, float chainLinkAngleTolerance, float epsilon)
        {
            if (covers == null) return new List<int>();
            if (covers.Count < 2)
            {
                return null;
            }

            for (int toBeMergedIndex = 0; toBeMergedIndex < covers.Count; toBeMergedIndex++)
            {
                CoverChainGeom chainToBeMerged = covers[toBeMergedIndex];
                if (chainToBeMerged == null || chainToBeMerged.Chain.Count == 0) continue;

                for (int mergeWithIndex = 0; mergeWithIndex < covers.Count; mergeWithIndex++)
                {
                    if (mergeWithIndex == toBeMergedIndex) continue;
                    CoverChainGeom chainToMergeWith = covers[mergeWithIndex];
                    if (chainToMergeWith == null || chainToMergeWith.Chain.Count == 0) continue;

                    // 待合并链起点接在目标链终点之后：直接追加（:282-291）
                    if (DoesLinkFitInChain(chainToBeMerged.Start(), chainToMergeWith.End(), chainLinkAngleTolerance, epsilon))
                    {
                        var newChain = new List<CoverLinkGeom>(chainToMergeWith.Chain);
                        newChain.AddRange(chainToBeMerged.Chain);
                        chainToMergeWith.Chain = newChain;
                        chainToBeMerged.Chain = new List<CoverLinkGeom>();
                        break;
                    }

                    // 待合并链终点接在目标链起点之前：整体插到前面（:294-303）
                    if (DoesLinkFitInChain(chainToBeMerged.End(), chainToMergeWith.Start(), chainLinkAngleTolerance, epsilon))
                    {
                        var newChain = new List<CoverLinkGeom>(chainToBeMerged.Chain);
                        newChain.AddRange(chainToMergeWith.Chain);
                        chainToMergeWith.Chain = newChain;
                        chainToBeMerged.Chain = new List<CoverLinkGeom>();
                        break;
                    }
                }
            }

            var survivors = new List<int>();
            for (int i = 0; i < covers.Count; i++)
            {
                if (covers[i] != null && covers[i].Chain.Count > 0) survivors.Add(i);
            }
            return survivors;
        }

        /// <summary>
        /// 折叠过短或夹角过小的相邻单元。对应 UE5 <c>FilterLinks</c>（NarrativeTileCoverGenerator.cpp:314-359）。
        /// 判据三条：任一段过短（<c>&lt;= MinLinkLength</c>，:333-334）/ 两段方向夹角小于
        /// <see cref="KindaSmallNumber"/> 补偿后的 <c>MaxAngleForMerge</c>（:337-339）；
        /// 命中就把 first 段的终点直接改写到 second 段的终点（:344-346）。
        /// 每轮都从**原始** Chain 读取相邻二元组（UE5 :329-330 读 Cover.Chain），
        /// 因此连续多段会被折叠成多条重复覆盖的合并段 —— UE5 自己在 :341 注释
        /// 「BUG: seems to leave a lingering link...」，这条怪癖按原样保留，不做修饰。
        /// </summary>
        public static void FilterLinks(IList<CoverChainGeom> covers, float minLinkLength, float maxAngleForMerge,
            int iterationCount, float epsilon)
        {
            if (covers == null) return;

            for (int iteration = 0; iteration < iterationCount; iteration++)
            {
                for (int coverIndex = 0; coverIndex < covers.Count; coverIndex++)
                {
                    CoverChainGeom cover = covers[coverIndex];
                    if (cover == null || cover.Chain.Count < 2) continue;

                    var sourceChain = cover.Chain;
                    var coverChain = new List<CoverLinkGeom>(sourceChain.Count);

                    for (int chainLinkIndex = 1; chainLinkIndex < sourceChain.Count; chainLinkIndex++)
                    {
                        int firstLinkIndex = chainLinkIndex - 1;
                        CoverLinkGeom firstChainLink = sourceChain[firstLinkIndex];
                        CoverLinkGeom secondChainLink = sourceChain[chainLinkIndex];

                        bool bFirstLinkTooShort = firstChainLink.Length() <= minLinkLength;
                        bool bSecondLinkTooShort = secondChainLink.Length() <= minLinkLength;

                        float dirDot = CoverVec3.Dot(firstChainLink.GetStartNormal(epsilon), secondChainLink.GetStartNormal(epsilon));
                        bool bAngleMerge = dirDot >= (Cos(maxAngleForMerge) - KindaSmallNumber);

                        if (bFirstLinkTooShort || bSecondLinkTooShort || bAngleMerge)
                        {
                            CoverLinkGeom newChain = firstChainLink;
                            newChain.End = secondChainLink.End;
                            coverChain.Add(newChain);
                            continue;
                        }

                        coverChain.Add(firstChainLink);
                        if (chainLinkIndex == sourceChain.Count - 1)
                        {
                            coverChain.Add(secondChainLink);
                        }
                    }

                    cover.Chain = coverChain;
                }
            }
        }

        // ================= 候选排序 / 半径 =================

        /// <summary>
        /// 半径内候选掩护按距离升序排列。对应 UE5 <c>FindNearestCoverToPoint</c> 的选优循环
        /// （NarrativeNavigationSystem.cpp:186-205）：
        /// <c>Dist &lt;= SearchRadius</c> 才入选（:198），跨候选的同距离平局用严格 <c>&lt;</c> 比较
        /// 保留**靠前**的候选（:198 的 <c>Dist &lt; LastCoverDist</c>），与链内的 <c>&lt;=</c>（保留靠后）相反 ——
        /// 两者都是 UE5 原样语义。方向偏置沿用 UE5 :197 的判据：偏置压平后为零视为「无偏置」，
        /// 否则要求 <c>Dot(bias, toCover) &gt; Cos(tolerance)</c>。
        /// </summary>
        public static List<CoverDistanceHit> RankCoversInRadius(CoverVec3 point, IReadOnlyList<CoverContainerGeom> candidates,
            float searchRadius, CoverVec3 directionBias, float directionBiasToleranceAngle, float epsilon)
        {
            var hits = new List<CoverDistanceHit>();
            if (candidates == null) return hits;

            CoverVec3 bias = directionBias.FlattenToGround();
            bool bHasBias = bias.LengthSquared > epsilon;
            float biasCos = Cos(directionBiasToleranceAngle);

            for (int candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
            {
                CoverContainerGeom cover = candidates[candidateIndex];
                if (!cover.IsValid()) continue;

                CoverNearestHit nearest = FindNearestCoverLink(point, cover.CoverChain, epsilon);
                if (!nearest.bFound) continue;

                float dist = CoverVec3.Distance(nearest.NearestPoint, point);
                if (dist > searchRadius) continue;

                if (bHasBias)
                {
                    CoverVec3 toCover = (nearest.NearestPoint - point).FlattenToGround();
                    float dot = CoverVec3.Dot(bias.SafeNormal(epsilon), toCover.SafeNormal(epsilon));
                    if (dot <= biasCos) continue;
                }

                hits.Add(new CoverDistanceHit
                {
                    CandidateIndex = candidateIndex,
                    ChainLinkIndex = nearest.ChainLinkIndex,
                    NearestPoint = nearest.NearestPoint,
                    Distance = dist
                });
            }

            // 距离升序；同距离按候选下标升序 —— 与 UE5 严格 < 的先到先得完全一致，且结果确定。
            hits.Sort((a, b) =>
            {
                int c = a.Distance.CompareTo(b.Distance);
                if (c != 0) return c;
                return a.CandidateIndex.CompareTo(b.CandidateIndex);
            });
            return hits;
        }

        /// <summary>
        /// UE5 的收集阶段（FindAllCoverInRadiusToPoint，NarrativeNavigationSystem.cpp:77-159）
        /// **不使用 SearchRadius**：它按「查询点所在瓦片 + 8 邻接瓦片」整瓦片收集（引擎在 :174 留下
        /// TODO: have search radius actually do something）。本函数把这条怪癖显式化：
        /// 收集只看瓦片，半径只在 <see cref="RankCoversInRadius"/> 生效。
        /// </summary>
        public static List<CoverContainerGeom> CollectTileCovers(IReadOnlyList<CoverChainGeom> tileChains, long tileIndex)
        {
            var found = new List<CoverContainerGeom>();
            if (tileChains == null) return found;
            for (int coverIndex = 0; coverIndex < tileChains.Count; coverIndex++)
            {
                CoverChainGeom chain = tileChains[coverIndex];
                if (chain == null || chain.Chain.Count == 0) continue;
                found.Add(new CoverContainerGeom(tileIndex, coverIndex, chain.Chain));
            }
            return found;
        }

        // ================= 跨瓦片串联 =================

        /// <summary>
        /// 把整段掩护按顺序接到已有链的头或尾。对应 UE5 文件内静态 <c>AddToChainLinkArrOrdered</c>
        /// （NarrativeNavigationSystem.cpp:25-43）：链是排好序的整链，只需比较两端距离决定插前还是追加。
        /// </summary>
        public static void AddToChainLinkArrOrdered(List<CoverLinkGeom> targetCover, CoverContainerGeom coverToAdd, float epsilon)
        {
            if (targetCover.Count == 0)
            {
                if (coverToAdd.CoverChain != null) targetCover.AddRange(coverToAdd.CoverChain);
                return;
            }

            CoverLinkGeom last = targetCover[targetCover.Count - 1];
            float appendDistance = CoverVec3.DistanceSquared(last.End, coverToAdd.Start().Start);
            float insertDistance = CoverVec3.DistanceSquared(last.Start, coverToAdd.End().End);

            if (appendDistance <= insertDistance)
            {
                targetCover.AddRange(coverToAdd.CoverChain);
            }
            else
            {
                targetCover.InsertRange(0, coverToAdd.CoverChain);
            }
        }

        /// <summary>
        /// 顺着边界哈希取掩护在给定方向上的下一段。对应 UE5 <c>FindNextSectionOfCover</c>
        /// （NarrativeNavigationSystem.cpp:252-314）：当前端点必须落在本瓦片边界哈希里（:280），
        /// 哈希指向的下一瓦片必须也含该键且有掩护（:295），并且候选端点要能通过
        /// <see cref="DoesLinkFitInChain"/>（:306）才允许接上。
        /// </summary>
        public static bool FindNextSectionOfCover(ICoverTileReader reader, CoverContainerGeom cover,
            bool bSearchAtStartOfCover, float angleToleranceDegrees, float epsilon, out CoverContainerGeom outCover)
        {
            outCover = default;
            if (reader == null || !cover.IsValid()) return false;
            if (cover.CoverChain == null || cover.CoverChain.Count == 0) return false;

            CoverLinkGeom link = bSearchAtStartOfCover ? cover.CoverChain[0] : cover.CoverChain[cover.CoverChain.Count - 1];
            CoverVec3 linkHash = (bSearchAtStartOfCover ? link.Start : link.End).Quantized(HashQuantizeScale);

            if (!reader.TryGetTile(cover.ParentTileIndex, out var parentTile) || parentTile == null)
            {
                return false;
            }

            // 当前掩护在该方向上必须确实跨越了本瓦片边界
            if (!parentTile.HashContains(linkHash))
            {
                return false;
            }

            if (!parentTile.TryGetBoundary(linkHash, out var parentBoundary) || parentBoundary.NextTileIndex < 0)
            {
                return false;
            }
            long nextTileIndex = parentBoundary.NextTileIndex;

            if (!reader.TryGetTile(nextTileIndex, out var neighbourTile) || neighbourTile == null)
            {
                return false;
            }
            if (!neighbourTile.HashContains(linkHash) || !neighbourTile.AnyCovers())
            {
                return false;
            }

            if (!neighbourTile.TryGetBoundary(linkHash, out var coverHashInfo))
            {
                return false;
            }
            if (coverHashInfo.CoverChainIndex < 0 || coverHashInfo.CoverChainIndex >= neighbourTile.Chains.Count)
            {
                return false;
            }

            CoverChainGeom neighbourChain = neighbourTile.Chains[coverHashInfo.CoverChainIndex];
            if (neighbourChain == null || neighbourChain.Chain.Count == 0) return false;

            CoverLinkGeom linkCandidate = bSearchAtStartOfCover ? neighbourChain.End() : neighbourChain.Start();

            if (!DoesLinkFitInChain(linkCandidate, link, angleToleranceDegrees, epsilon))
            {
                return false;
            }

            outCover = new CoverContainerGeom(nextTileIndex, coverHashInfo.CoverChainIndex, neighbourChain.Chain);
            return true;
        }

        /// <summary>
        /// 沿一个方向串联掩护，visited 集合保证在环状边界哈希下必然终止。
        /// 对应 UE5 文件内静态 <c>WalkCoverInDirection</c>（NarrativeNavigationSystem.cpp:45-75，
        /// 引擎在 :47 留了「TODO: add limit to this」，实际的终止性由 VisitedTilesAndCovers 提供）。
        /// 每成功推进一步就会新增一个从未访问过的 (瓦片, 链) 对，而可登记的对数有限，故循环有限步退出。
        /// <paramref name="maxSteps"/> 只是 harness/线上共用的防御性上界（等于可登记的 (瓦片,链) 对总数时
        /// visited 逻辑本应先停下），不影响正常数据的结果。
        /// </summary>
        public static int WalkCoverInDirection(ICoverTileReader reader, CoverContainerGeom startCover, bool bWalkForward,
            float angleToleranceDegrees, float epsilon, Dictionary<long, List<int>> visitedTilesAndCovers,
            List<CoverLinkGeom> outFoundCover, int maxSteps = int.MaxValue)
        {
            if (visitedTilesAndCovers == null || outFoundCover == null) return 0;

            var current = startCover;
            int steps = 0;
            while (steps < maxSteps)
            {
                if (!FindNextSectionOfCover(reader, current, !bWalkForward, angleToleranceDegrees, epsilon, out var outCover))
                {
                    break;
                }
                if (!outCover.IsValid())
                {
                    break;
                }

                if (!visitedTilesAndCovers.TryGetValue(outCover.ParentTileIndex, out var visitedChains))
                {
                    visitedChains = new List<int>();
                    visitedTilesAndCovers[outCover.ParentTileIndex] = visitedChains;
                }

                if (visitedChains.Contains(outCover.CoverChainIndex))
                {
                    break;
                }
                visitedChains.Add(outCover.CoverChainIndex);

                AddToChainLinkArrOrdered(outFoundCover, outCover, epsilon);
                current = outCover;
                steps++;
            }
            return steps;
        }

        /// <summary>
        /// 先把命中链铺进去，再向前/向后各走一遍，得到跨瓦片的完整掩护。
        /// 对应 UE5 <c>FindNearestCoverToPoint</c> :212-220（同一份 visited 表被两次 Walk 共享）。
        /// </summary>
        public static List<CoverLinkGeom> StitchCoverAcrossTiles(ICoverTileReader reader, CoverContainerGeom firstCover,
            float angleToleranceDegrees, float epsilon, out int stepsWalked)
        {
            var links = new List<CoverLinkGeom>();
            stepsWalked = 0;
            if (!firstCover.IsValid()) return links;

            var visited = new Dictionary<long, List<int>>
            {
                { firstCover.ParentTileIndex, new List<int> { firstCover.CoverChainIndex } }
            };

            if (firstCover.CoverChain != null) links.AddRange(firstCover.CoverChain);

            stepsWalked += WalkCoverInDirection(reader, firstCover, true, angleToleranceDegrees, epsilon, visited, links);
            stepsWalked += WalkCoverInDirection(reader, firstCover, false, angleToleranceDegrees, epsilon, visited, links);
            return links;
        }

        // ================= 掩护类型 / 探测射线 =================

        /// <summary>
        /// 由 CoverTraceConfig 的四个高度/宽度量构造 4 条探测射线的本地起点。
        /// 与 <see cref="CoverTraceConfig.UpdateTraceStarts"/> 同源：
        /// 0=低掩(Z=HalfHeight)、1=可越过(Z=PeekOverHeight)、2/3=左右探(Z=PeekSideHeight, Y=∓PeekSideWidth)。
        /// </summary>
        public static CoverVec3[] BuildTraceStarts(float halfHeight, float peekOverHeight, float peekSideHeight, float peekSideWidth)
        {
            return new[]
            {
                new CoverVec3(0.0f, 0.0f, halfHeight),
                new CoverVec3(0.0f, 0.0f, peekOverHeight),
                new CoverVec3(0.0f, -peekSideWidth, peekSideHeight),
                new CoverVec3(0.0f, peekSideWidth, peekSideHeight),
            };
        }

        /// <summary>
        /// 单条掩护探测射线（对应 UE5 <c>IsCoverTraceBlocked</c> lambda，
        /// NarrativeNavigationSystem.cpp:319-338：把 TraceStart 按方向旋到世界再沿方向推进 TraceDepth）。
        /// Flax 为 Y-up，局部轴映射：X=侧向、Y=前向(沿 direction)、Z=高度。
        /// </summary>
        public static void BuildTraceRay(CoverVec3 inPoint, CoverVec3 directionNormalised, CoverVec3 inTracePosition,
            float traceDepth, out CoverVec3 traceStart, out CoverVec3 traceEnd)
        {
            CoverVec3 forward = directionNormalised;
            CoverVec3 up = CoverVec3.Up;
            CoverVec3 side = CoverVec3.Cross(up, forward);

            CoverVec3 worldOffset = side * inTracePosition.X + forward * inTracePosition.Y + up * inTracePosition.Z;
            traceStart = inPoint + worldOffset;
            traceEnd = traceStart + forward * traceDepth;
        }

        /// <summary>
        /// 由四条探测射线的命中结果裁决掩护类型。对应 UE5 <c>TestPointForCoverType</c>
        /// （NarrativeNavigationSystem.cpp:340-346）：
        /// 低掩 = 低处被挡 **且** 高处没被挡；只有不是低掩时才谈左右探，且左右探要求各自的射线没被挡。
        /// 因此「低掩」会把左右探一起压成 false —— 即使左右实际可见。
        /// </summary>
        public static CoverTypeResult ClassifyCoverType(bool bLowHit, bool bHighHit, bool bLeanLeftHit, bool bLeanRightHit)
        {
            var result = new CoverTypeResult();
            result.bIsLowCover = bLowHit && !bHighHit;
            result.bCanPeekLeft = !result.bIsLowCover && !bLeanLeftHit;
            result.bCanPeekRight = !result.bIsLowCover && !bLeanRightHit;
            return result;
        }

        /// <summary>
        /// 低掩/高掩的探测高度：低掩（高处可越过）在半高处采样，高掩在可越过高度采样。
        /// 对应 NarrativeRecastNavMesh 的占用探测取值（UE5 侧由 Recast 多边形高度给出）。
        /// </summary>
        public static float ResolveProbeHeight(bool bIsLowCover, float halfHeight, float peekOverHeight)
        {
            return bIsLowCover ? halfHeight : peekOverHeight;
        }

        // ================= 瓦片边界 =================

        /// <summary>
        /// 点落在瓦片边界盒的哪条边、指向哪个邻接方向。
        /// 对应 UE5 <c>GetTargetNeighbour</c>（NarrativeTileCoverGenerator.cpp:8-19）。
        /// UE5 是 Z-up（判定用 X/Y 平面），Flax 为 Y-up，故这里对应 X/Z，且严格保持 UE5 的判定顺序
        /// （角点先于边判定的是 UE5 :11/:13/:15/:17 的四角分支顺序）。
        /// </summary>
        public static int GetTargetNeighbour(CoverVec3 point, CoverVec3 boxMin, CoverVec3 boxMax, float tolerance)
        {
            bool minX = System.Math.Abs(point.X - boxMin.X) <= tolerance;
            bool maxX = System.Math.Abs(point.X - boxMax.X) <= tolerance;
            bool minZ = System.Math.Abs(point.Z - boxMin.Z) <= tolerance;
            bool maxZ = System.Math.Abs(point.Z - boxMax.Z) <= tolerance;
            bool inZ = point.Z > boxMin.Z && point.Z < boxMax.Z;
            bool inX = point.X > boxMin.X && point.X < boxMax.X;

            if (minX && inZ) return (int)ETileEdge.Bottom;
            if (minZ && minX) return (int)ETileEdge.BottomLeft;
            if (minZ && inX) return (int)ETileEdge.Left;
            if (minZ && maxX) return (int)ETileEdge.TopLeft;
            if (maxX && inZ) return (int)ETileEdge.Top;
            if (maxZ && maxX) return (int)ETileEdge.TopRight;
            if (maxZ && inX) return (int)ETileEdge.Right;
            if (maxZ && minX) return (int)ETileEdge.BottomRight;
            return (int)ETileEdge.None;
        }

        /// <summary>弧度制余弦（引擎无关；UE5 用 FMath::Cos(FMath::DegreesToRadians(x))）。</summary>
        public static float Cos(float angleDegrees)
        {
            return (float)System.Math.Cos(angleDegrees * (System.Math.PI / 180.0));
        }
    }
}

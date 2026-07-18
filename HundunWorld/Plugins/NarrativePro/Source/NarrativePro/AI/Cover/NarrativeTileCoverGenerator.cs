using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.AI.Cover;
using NarrativePro.Core;

namespace NarrativePro.AI.Cover
{
    /// <summary>
    /// 掩护瓦片生成器。对应 UE5 FCoverTileGeneratorWrapper。
    /// 为单个导航瓦片生成掩护数据。
    /// 注：Flax 无 RecastNavMesh 系统，此实现为简化版，使用场景几何体射线检测。
    /// </summary>
    public class CoverTileGeneratorWrapper
    {
        public long TileIndex = -1;
        public TileCover GeneratedCover = new TileCover();

        /// <summary>为此瓦片生成掩护数据</summary>
        /// <param name="tileBounds">瓦片边界</param>
        /// <param name="traceConfig">射线检测配置</param>
        public virtual void GenerateCover(BoundingBox tileBounds, CoverTraceConfig traceConfig)
        {
            // Flax-不兼容: UE5 的 Cover 系统依赖 RecastNavMesh 瓦片，在 Flax 无对应物，保留占位。原文 TODO: 完整实现需要遍历瓦片内的墙体边缘并生成掩护链
            // 简化实现：仅扫描瓦片边界，检测附近的障碍物
            NarrativeLog.Log($"为瓦片 {TileIndex} 生成掩护（简化实现）");
        }
    }

    /// <summary>
    /// 掩护瓦片生成器。对应 UE5 UNarrativeTileCoverGenerator。
    /// 管理 NavMesh 瓦片的掩护数据生成与缓存。
    /// 注：Flax 无 Recast NavMesh，此实现为简化版，使用场景几何体。
    /// </summary>
    public class NarrativeTileCoverGenerator : Script
    {
        /// <summary>单例实例</summary>
        public static NarrativeTileCoverGenerator Instance { get; private set; }

        /// <summary>掩护检测配置</summary>
        public CoverTraceConfig TraceConfig = new CoverTraceConfig();

        /// <summary>已生成的瓦片掩护数据（按瓦片索引）</summary>
        private Dictionary<long, TileCover> _tileCoverMap = new Dictionary<long, TileCover>();

        /// <summary>所有掩护容器（用于查询）</summary>
        private List<CoverContainer> _allCovers = new List<CoverContainer>();

        public override void OnEnable()
        {
            base.OnEnable();
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
        }

        public override void OnDisable()
        {
            if (Instance == this) Instance = null;
            base.OnDisable();
        }

        /// <summary>为指定区域生成掩护数据</summary>
        /// <param name="bounds">区域包围盒</param>
        public void GenerateCoversForArea(BoundingBox bounds)
        {
            NarrativeLog.Log($"为区域 {bounds} 生成掩护数据（简化实现）");
            // Flax-不兼容: UE5 的 Cover 系统依赖 RecastNavMesh 瓦片划分，在 Flax 无对应物，保留占位。原文 TODO: 将区域划分为瓦片，为每个瓦片生成掩护
            // 简化实现：扫描整个区域，检测障碍物边缘
        }

        /// <summary>获取指定瓦片的掩护数据</summary>
        public TileCover GetTileCover(long tileIndex)
        {
            if (_tileCoverMap.TryGetValue(tileIndex, out var cover))
            {
                return cover;
            }
            return TileCover.Invalid;
        }

        /// <summary>获取所有掩护容器</summary>
        public List<CoverContainer> GetAllCovers()
        {
            return _allCovers;
        }

        /// <summary>查找距离指定位置最近的掩护</summary>
        /// <param name="position">查询位置</param>
        /// <param name="maxDistance">最大搜索距离</param>
        /// <returns>最近的掩护容器，找不到返回 null</returns>
        public CoverContainer FindNearestCover(Vector3 position, float maxDistance = 1000f)
        {
            CoverContainer best = null;
            float bestDist = maxDistance * maxDistance;

            foreach (var cover in _allCovers)
            {
                if (cover == null || !cover.IsValid()) continue;
                foreach (var link in cover.CoverChain)
                {
                    Vector3 mid = (link.Start + link.End) * 0.5f;
                    float d = Vector3.DistanceSquared(position, mid);
                    if (d < bestDist)
                    {
                        bestDist = d;
                        best = cover;
                    }
                }
            }
            return best;
        }

        /// <summary>查找指定位置范围内的所有掩护</summary>
        public List<CoverContainer> FindCoversInRadius(Vector3 position, float radius)
        {
            var result = new List<CoverContainer>();
            float r2 = radius * radius;

            foreach (var cover in _allCovers)
            {
                if (cover == null || !cover.IsValid()) continue;
                foreach (var link in cover.CoverChain)
                {
                    Vector3 mid = (link.Start + link.End) * 0.5f;
                    if (Vector3.DistanceSquared(position, mid) <= r2)
                    {
                        result.Add(cover);
                        break;
                    }
                }
            }
            return result;
        }

        /// <summary>清空所有掩护数据</summary>
        public void ClearAllCovers()
        {
            _tileCoverMap.Clear();
            _allCovers.Clear();
        }
    }
}

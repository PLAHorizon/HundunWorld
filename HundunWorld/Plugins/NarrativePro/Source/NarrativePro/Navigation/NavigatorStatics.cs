using System.Collections.Generic;
using FlaxEngine;

namespace NarrativePro.Navigation
{
    /// <summary>
    /// 导航系统静态工具函数库。适配 UE5 UNavigatorStatics。
    /// <para>
    /// <b>坐标约定（重要）</b>：Flax 为 Y-up 左手系，地面平面是 <b>XZ</b>。
    /// 因此「世界 → 地图」投影必须使用 (X, Z)，而非 UE 的 (X, Y)。
    /// 高度（Y）不参与地图投影，仅在需要回填世界位置时由导航网格探测。
    /// </para>
    /// </summary>
    public static class NavigatorStatics
    {
        /// <summary>XZ 平面上的距离（忽略 Y 高度差）。等价 UE5 <c>FVector::Dist2D</c>。</summary>
        public static float Distance2D(Vector3 a, Vector3 b)
        {
            float dx = a.X - b.X;
            float dz = a.Z - b.Z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>XZ 平面上距离的平方（避免开方，用于比较）。</summary>
        public static float DistanceSquared2D(Vector3 a, Vector3 b)
        {
            float dx = a.X - b.X;
            float dz = a.Z - b.Z;
            return dx * dx + dz * dz;
        }

        /// <summary>
        /// 将世界位置投影到地图本地像素空间。
        /// <paramref name="mapOrigin"/> 是地图中心的世界 XZ（见 <see cref="MapTileBoundsActor.MapOrigin2D"/>）。
        /// </summary>
        public static Float2 WorldToMapLocalPosition(Vector3 worldLocation, Float2 mapOrigin, float mapWidth, float mapImagePixels)
        {
            float scale = mapWidth <= 0f ? 0f : mapImagePixels / mapWidth;
            return new Float2(
                (worldLocation.X - mapOrigin.X) * scale,
                (worldLocation.Z - mapOrigin.Y) * scale
            );
        }

        /// <summary>
        /// 将地图本地像素位置反投影到世界位置。世界高度取 <paramref name="worldY"/>。
        /// </summary>
        public static Vector3 MapLocalPositionToWorld(Float2 mapLocal, Float2 mapOrigin, float mapWidth, float mapImagePixels, float worldY = 0f)
        {
            float scale = mapImagePixels <= 0f ? 0f : mapWidth / mapImagePixels;
            return new Vector3(
                mapLocal.X * scale + mapOrigin.X,
                worldY,
                mapLocal.Y * scale + mapOrigin.Y
            );
        }

        /// <summary>
        /// 世界位置 → 归一化地图坐标（0~1，左上为原点，X 向右、Y 向下/朝 Z 增大方向）。
        /// 供世界地图 UI 按贴图比例定位标记使用。
        /// </summary>
        public static Float2 WorldToNormalizedMapPosition(Vector3 worldLocation, Float2 mapOrigin, float mapWidth)
        {
            if (mapWidth <= 0f) return Float2.Zero;
            return new Float2(
                (worldLocation.X - mapOrigin.X) / mapWidth + 0.5f,
                (worldLocation.Z - mapOrigin.Y) / mapWidth + 0.5f
            );
        }

        /// <summary>归一化地图坐标 → 世界位置（世界高度取 <paramref name="worldY"/>）。</summary>
        public static Vector3 NormalizedMapPositionToWorld(Float2 normalized, Float2 mapOrigin, float mapWidth, float worldY = 0f)
        {
            return new Vector3(
                (normalized.X - 0.5f) * mapWidth + mapOrigin.X,
                worldY,
                (normalized.Y - 0.5f) * mapWidth + mapOrigin.Y
            );
        }

        /// <summary>计算两标记之间的世界距离（XZ 平面）。</summary>
        public static float DistanceBetweenMarkers(MapMarker a, MapMarker b)
        {
            if (a == null || b == null) return float.MaxValue;
            return Distance2D(a.GetMarkerTransform().Translation, b.GetMarkerTransform().Translation);
        }

        /// <summary>根据敌意类型获取标记颜色。</summary>
        public static Color GetColorForAttitude(ENavigationAttitude attitude)
        {
            switch (attitude)
            {
                case ENavigationAttitude.Player: return NavigationDeveloperSettings.PlayerColor;
                case ENavigationAttitude.Friendly: return NavigationDeveloperSettings.FriendlyColor;
                case ENavigationAttitude.Neutral: return NavigationDeveloperSettings.NeutralColor;
                case ENavigationAttitude.Hostile: return NavigationDeveloperSettings.HostileColor;
                default: return Color.White;
            }
        }

        // ===================================================================
        // Flax 导航网格查询封装
        // 说明：Flax 1.12 自带 Recast/Detour 导航模块（FlaxEngine.Navigation 静态类）。
        // 注意：本文件位于 NarrativePro.Navigation 命名空间下，"Navigation" 会被解析为命名空间而非类型，
        // 必须使用全限定名 FlaxEngine.Navigation，否则报 CS0118。
        // ===================================================================

        /// <summary>场景中是否存在导航网格 Actor（NavMesh）。</summary>
        public static bool HasNavMeshActor()
        {
            try
            {
                var navMeshes = Level.GetActors<NavMesh>(false);
                return navMeshes != null && navMeshes.Length > 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 导航网格是否可用于查询。
        /// <para>
        /// <b>降级语义</b>：Flax 在导航网格缺失或构建中时，所有查询都会返回 false。
        /// 若直接把 false 当作「不可达」，会导致所有远程寻路判定失败，因此调用方必须先判断本属性，
        /// 未就绪时按「未知」处理（见 <see cref="TestPath"/>）。
        /// </para>
        /// </summary>
        public static bool IsNavigationReady()
        {
            if (FlaxEngine.Navigation.IsBuildingNavMesh) return false;
            return HasNavMeshActor();
        }

        /// <summary>
        /// 测试两点间是否存在完整（非分段）导航路径。
        /// 返回 false 有两种可能：确实不可达，或导航未就绪；调用方可用 <see cref="IsNavigationReady"/> 区分。
        /// </summary>
        public static bool TestPath(Vector3 start, Vector3 end)
        {
            try
            {
                return FlaxEngine.Navigation.TestPath(start, end);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 求两点间的导航路径拐点。失败（含导航未就绪）返回 false，<paramref name="path"/> 为空数组。
        /// 等价于 <see cref="TryFindPath(Vector3, Vector3, NarrativeNavQueryFilter, out Vector3[])"/> 使用默认过滤器。
        /// </summary>
        public static bool TryFindPath(Vector3 start, Vector3 end, out Vector3[] path)
        {
            return TryFindPath(start, end, null, out path);
        }

        /// <summary>
        /// 带查询过滤器的路径求解。对应 UE5 的 FindPath(NavData, QueryFilter)：
        /// 引擎几何寻路之后，再按导航区域代价（<see cref="NavArea_Road"/> 等）做一次道路优先的优化。
        /// <para>
        /// Flax 的 <c>Navigation.FindPath</c> 没有过滤器入口，因此 <paramref name="filter"/> 的
        /// <see cref="NarrativeNavQueryFilter.MaxSearchNodes"/> 用作 <see cref="NavAreaSystem"/> A* 的
        /// 节点预算，<see cref="NarrativeNavQueryFilter.bAllowAreaCostPathfinding"/> 决定是否做代价优化。
        /// </para>
        /// </summary>
        public static bool TryFindPath(Vector3 start, Vector3 end, NarrativeNavQueryFilter filter, out Vector3[] path)
        {
            path = System.Array.Empty<Vector3>();
            try
            {
                if (!FlaxEngine.Navigation.FindPath(start, end, out var corners) || corners == null || corners.Length == 0)
                {
                    return false;
                }

                // 引擎路径的代价（几何长度 × 沿途区域代价），作为是否采用道路路径的比较基准
                float engineCost = GetPathCost(corners);

                if (filter != null && !filter.bAllowAreaCostPathfinding)
                {
                    path = corners;
                    return true;
                }
                if (filter == null && !NavAreaSystem.bUseCostAwarePathfinding)
                {
                    path = corners;
                    return true;
                }

                int budget = filter != null ? filter.MaxSearchNodes : NarrativeNavQueryFilter.DefaultMaxSearchNodes;

                if (NavAreaSystem.TryFindCostAwarePath(start, end, budget, out var roadWaypoints, out float roadCost)
                    && roadCost < engineCost)
                {
                    var refined = new List<Vector3>(roadWaypoints.Count + 2) { start };
                    foreach (var waypoint in roadWaypoints)
                    {
                        // 去掉与起点/终点重合的接驳点，保持拐点序列干净
                        if (Vector3.DistanceSquared(waypoint, start) < 1f) continue;
                        if (Vector3.DistanceSquared(waypoint, end) < 1f) continue;
                        if (Vector3.DistanceSquared(waypoint, refined[refined.Count - 1]) < 1f) continue;
                        refined.Add(waypoint);
                    }
                    refined.Add(end);

                    path = refined.ToArray();
                    return path.Length > 0;
                }

                path = corners;
                return true;
            }
            catch
            {
                // 导航网格缺失/构建中，按失败处理
            }
            return false;
        }

        /// <summary>
        /// 一条路径的通行代价：总长度乘以沿途区域代价（道路 0.5 会显著降低代价）。
        /// 对应 UE5 Recast 的 AreaCost 加权距离。
        /// 累加规则本体在 <see cref="NavCostGraph.PathCost"/>（纯实现，harness 驱动同一份）。
        /// </summary>
        public static float GetPathCost(Vector3[] path)
        {
            if (path == null || path.Length == 0) return float.MaxValue;
            if (path.Length == 1) return 0f;

            var points = new NavCostPoint[path.Length];
            for (int i = 0; i < path.Length; i++)
            {
                points[i] = new NavCostPoint(path[i].X, path[i].Y, path[i].Z);
            }

            return NavCostGraph.PathCost(points, NavAreaSystem.GetSegmentAverageCost);
        }

        /// <summary>把位置投影到导航网格表面。失败返回 false。</summary>
        public static bool TryFindClosestPoint(Vector3 point, out Vector3 result)
        {
            result = point;
            try
            {
                return FlaxEngine.Navigation.FindClosestPoint(point, out result);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>在导航网格上、圆心 <paramref name="center"/> 半径 <paramref name="radius"/> 内随机取点。失败返回 false。</summary>
        public static bool TryFindRandomPointAroundCircle(Vector3 center, float radius, out Vector3 result)
        {
            result = center;
            try
            {
                return FlaxEngine.Navigation.FindRandomPointAroundCircle(center, radius, out result);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>取位置到导航网格边界的距离（越近越小）。失败返回 false。</summary>
        public static bool TryFindDistanceToWall(Vector3 point, out float distance, float maxDistance = 500f)
        {
            distance = maxDistance;
            try
            {
                if (FlaxEngine.Navigation.FindDistanceToWall(point, out var hit, maxDistance))
                {
                    distance = hit.Distance;
                    return true;
                }
            }
            catch
            {
                // 导航未就绪
            }
            return false;
        }

        /// <summary>把导航路径按等弧长重采样为固定间距的点列（用于面包屑绘制）。</summary>
        /// <param name="corners">导航路径拐点（至少 2 个）</param>
        /// <param name="distanceBetweenPoints">重采样间距（世界单位，&gt;0）</param>
        /// <param name="result">输出点列</param>
        /// <returns>是否产生至少一个点</returns>
        public static bool ResamplePath(Vector3[] corners, float distanceBetweenPoints, out List<Vector3> result)
        {
            result = new List<Vector3>();
            if (corners == null || corners.Length == 0 || distanceBetweenPoints <= 0f) return false;

            if (corners.Length == 1)
            {
                result.Add(corners[0]);
                return true;
            }

            // 以「沿路径累计距离」为参数，每 distanceBetweenPoints 取一个点
            float interval = distanceBetweenPoints;
            float nextSampleAt = interval;
            float accumulated = 0f;

            result.Add(corners[0]);

            for (int i = 1; i < corners.Length; i++)
            {
                Vector3 segmentStart = corners[i - 1];
                Vector3 segmentEnd = corners[i];
                float segmentLength = Vector3.Distance(segmentStart, segmentEnd);
                if (segmentLength <= 0f) continue;

                while (nextSampleAt <= accumulated + segmentLength)
                {
                    float t = (nextSampleAt - accumulated) / segmentLength;
                    result.Add(Vector3.Lerp(segmentStart, segmentEnd, t));
                    nextSampleAt += interval;

                    // 防御：异常小的间距会导致超长列表
                    if (result.Count > MaxResampledPathPoints) return true;
                }

                accumulated += segmentLength;
            }

            // 终点始终纳入，保证面包屑指向目标
            if (Vector3.Distance(result[result.Count - 1], corners[corners.Length - 1]) > 0.01f)
            {
                result.Add(corners[corners.Length - 1]);
            }

            return result.Count > 0;
        }

        /// <summary>面包屑重采样点数的硬上限，防止误配间距导致内存暴涨。</summary>
        public const int MaxResampledPathPoints = 512;
    }

    /// <summary>
    /// 导航标记的敌意类型，用于决定标记颜色。
    /// </summary>
    public enum ENavigationAttitude
    {
        /// <summary>本地玩家</summary>
        Player = 0,
        /// <summary>友方</summary>
        Friendly = 1,
        /// <summary>中立</summary>
        Neutral = 2,
        /// <summary>敌方</summary>
        Hostile = 3,
        /// <summary>无特定敌意</summary>
        None = 4
    }
}

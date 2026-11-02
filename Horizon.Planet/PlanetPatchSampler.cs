using System;
using System.Threading.Tasks;

namespace Horizon.Planet;

/// <summary>
/// 把一个 Flax Terrain patch（4×4 chunk、509×509 顶点）的高度填成星球地表。
/// <para>高度图布局按引擎实现：<c>index = z * HeightmapEdge + x</c>，x 是世界 X（东）、z 是世界 Z（南）；
/// 见 Flax Source/Engine/Terrain/TerrainPatch.cpp 里 UpdateHeightMap / CalculateHeightmapRange 的下标写法。</para>
/// </summary>
public static class PlanetPatchSampler
{
    /// <summary>patch 每边顶点数 = 127 × 4 + 1 = 509。</summary>
    public const int HeightmapEdge =
        PlanetConstants.TerrainChunkQuads * PlanetConstants.TerrainPatchEdgeChunks + 1;

    /// <summary>一个 patch 高度图的元素数。</summary>
    public const int HeightmapLength = HeightmapEdge * HeightmapEdge;

    /// <summary>把 patch 内第 (x, z) 个顶点的高度数组下标算出来（布局判据集中在这里）。</summary>
    public static int HeightIndex(int x, int z) => z * HeightmapEdge + x;

    /// <summary>
    /// 填一个 patch 的高度（局部 Y，厘米）。
    /// <para>顶点先落在切平面上（Y=0），投影回球面取方向，再按该方向的高程回到切平面高度：
    /// localY = (R + h) · cos(夹角) − R，其中 −R·(1−cos) 就是曲率下垂，所以远景球面和近景平面天然接得上。</para>
    /// </summary>
    /// <summary>
    /// 填一个 patch 的高度（局部 Y，厘米）。
    /// <para>逐顶点算到世界坐标再取高程，因此结果与顶点遍历顺序无关，可以按行并行；
    /// 并行只是把同一串算术摊到多个线程上，任何一个数都不会因为线程数而改变。</para>
    /// </summary>
    public static void FillPatchHeights(RegionFrame frame, PlanetTerrain terrain,
                                        double patchOriginLocalXCm, double patchOriginLocalZCm,
                                        float[] heights, HeightSampler? sampler = null, bool parallel = true)
    {
        if (heights is null) throw new ArgumentNullException(nameof(heights));
        if (heights.Length != HeightmapLength)
            throw new ArgumentException($"高度数组长度必须是 {HeightmapLength}（{HeightmapEdge}²），实际 {heights.Length}",
                                        nameof(heights));

        double radius = terrain.RadiusCm;
        void FillRow(int z, HeightSampler? rowSampler)
        {
            double localZ = patchOriginLocalZCm + (double)z * PlanetConstants.TerrainUnitsPerVertex;
            for (int x = 0; x < HeightmapEdge; x++)
            {
                double localX = patchOriginLocalXCm + (double)x * PlanetConstants.TerrainUnitsPerVertex;
                PlanetVector3 onPlane = frame.GlobalFromLocalCm(new PlanetVector3(localX, 0, localZ));
                double len = onPlane.Length;
                if (len <= 1e-300) throw new InvalidOperationException("切平面点退化成零向量");
                PlanetVector3 direction = onPlane / len;
                double elevationCm = rowSampler is not null
                    ? rowSampler.SampleElevationCm(direction)
                    : terrain.ElevationCm(direction);
                heights[HeightIndex(x, z)] =
                    (float)((radius + elevationCm) * PlanetVector3.Dot(direction, frame.Up) - radius);
            }
        }

        if (!parallel)
        {
            for (int z = 0; z < HeightmapEdge; z++) FillRow(z, sampler);
            return;
        }

        int workers = Math.Max(1, Environment.ProcessorCount);
        if (sampler is not null) workers = Math.Min(workers, HeightmapEdge);
        var jobs = new Task[workers];
        for (int t = 0; t < workers; t++)
        {
            int z0 = t * HeightmapEdge / workers;
            int z1 = (t + 1) * HeightmapEdge / workers;
            // 每线程一份采样器（缓存可变）；采样本身是位置的纯函数，切分方式不影响任何数值
            var rowSampler = sampler is null ? null : new HeightSampler(sampler.Grid, sampler.Hybrid);
            jobs[t] = Task.Run(() =>
            {
                for (int z = z0; z < z1; z++) FillRow(z, rowSampler);
            });
        }
        Task.WaitAll(jobs);
    }

    /// <summary>单点版：给定局部 (东, 南) 坐标，返回地面的局部 Y（厘米）。服务端/AI 落点也走这条。</summary>
    public static double SampleGroundLocalYCm(RegionFrame frame, PlanetTerrain terrain,
                                              double localXCm, double localZCm, double radiusCm)
    {
        PlanetVector3 onPlane = frame.GlobalFromLocalCm(new PlanetVector3(localXCm, 0, localZCm));
        double len = onPlane.Length;
        if (len <= 1e-300) throw new InvalidOperationException("切平面点退化成零向量");
        PlanetVector3 direction = onPlane / len;
        double elevationCm = terrain.ElevationCm(direction);
        return (radiusCm + elevationCm) * PlanetVector3.Dot(direction, frame.Up) - radiusCm;
    }

    /// <summary>局部地面高度（默认用星球半径）。</summary>
    public static double SampleGroundLocalYCm(RegionFrame frame, PlanetTerrain terrain,
                                              double localXCm, double localZCm) =>
        SampleGroundLocalYCm(frame, terrain, localXCm, localZCm, terrain.RadiusCm);
}

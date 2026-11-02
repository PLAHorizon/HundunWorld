using System;
using Xunit;

namespace Horizon.Planet.Tests;

/// <summary>点积坡：沿某轴的高度 = 振幅 × 方向在该轴上的投影。用来定向检验 patch 填高，不会与轴向同时巧合。</summary>
internal sealed class AxisedHeightSource : IHeightSource
{
    private readonly PlanetVector3 _axis;
    private readonly double _amplitudeCm;

    public AxisedHeightSource(PlanetVector3 axis, double amplitudeCm)
    {
        _axis = axis;
        _amplitudeCm = amplitudeCm;
    }

    public int SourceVersion => 1;

    public double SampleElevationCm(in PlanetVector3 unitDirection) =>
        _amplitudeCm * PlanetVector3.Dot(unitDirection, _axis);
}

public class PlanetPatchSamplerTests
{
    private const double RampAmplitudeCm = 1e8;   // 坡率 = 振幅 / 半径 ≈ 0.0157 cm/cm → 508 m 抬升约 8 m

    private static RegionFrame Frame(RegionKey key) => RegionFrame.Build(key);

    private static PlanetTerrain Constant(double cm) => new(PlanetConstants.PlanetRadiusCm, new ConstantHeightSource(cm));

    [Fact]
    public void Heightmap_Layout_IsZMajorRow()
    {
        Assert.Equal(509, PlanetPatchSampler.HeightmapEdge);
        Assert.Equal(509 * 509, PlanetPatchSampler.HeightmapLength);
        Assert.Equal(1, PlanetPatchSampler.HeightIndex(1, 0));
        Assert.Equal(509, PlanetPatchSampler.HeightIndex(0, 1));
        Assert.Equal(509 * 509 - 1, PlanetPatchSampler.HeightIndex(508, 508));
    }

    [Fact]
    public void FillPatchHeights_RejectsWrongLength()
    {
        var frame = Frame(new RegionKey(4, 14, 8192, 8192));
        var terrain = Constant(1_000);
        Assert.Throws<ArgumentException>(() =>
            PlanetPatchSampler.FillPatchHeights(frame, terrain, 0, 0, new float[16]));
        Assert.Throws<ArgumentNullException>(() =>
            PlanetPatchSampler.FillPatchHeights(frame, terrain, 0, 0, null!));
    }

    [Fact]
    public void ConstantElevation_AtFrameOrigin_IsExact_AndDipsByCurvature()
    {
        var frame = Frame(new RegionKey(0, 14, 8192, 8192));
        const double elevation = 12_345;
        var terrain = Constant(elevation);

        Assert.Equal(elevation, PlanetPatchSampler.SampleGroundLocalYCm(frame, terrain, 0, 0), 4);

        // 切平面边缘的球面比平面低一个"曲率下垂"，常数高程也必须跟着掉下去
        double cornerX = PlanetConstants.TerrainPatchSizeCm;
        double cornerZ = PlanetConstants.TerrainPatchSizeCm;
        double horizontal = Math.Sqrt(cornerX * cornerX + cornerZ * cornerZ);
        double expected = elevation - RegionMetrics.CurvatureSagCm(horizontal);
        double actual = PlanetPatchSampler.SampleGroundLocalYCm(frame, terrain, cornerX, cornerZ);
        Assert.True(Math.Abs(actual - expected) < 0.5,
                    $"角点 {actual:F4} cm 与 高程−下垂 {expected:F4} cm 不符");
    }

    [Fact]
    public void PatchArray_MatchesSinglePointApi()
    {
        var frame = Frame(new RegionKey(2, 14, 4000, 9000));
        var terrain = Constant(-4_321);
        var heights = new float[PlanetPatchSampler.HeightmapLength];
        PlanetPatchSampler.FillPatchHeights(frame, terrain, 0, 0, heights);

        foreach (var (x, z) in new[] { (0, 0), (254, 13), (508, 0), (0, 508), (508, 508) })
        {
            double localX = (double)x * PlanetConstants.TerrainUnitsPerVertex;
            double localZ = (double)z * PlanetConstants.TerrainUnitsPerVertex;
            double single = PlanetPatchSampler.SampleGroundLocalYCm(frame, terrain, localX, localZ);
            Assert.Equal(single, heights[PlanetPatchSampler.HeightIndex(x, z)], 3);
        }
    }

    [Fact]
    public void EastFacingRamp_RisesAlongLocalX_NotAlongZ()
    {
        var frame = Frame(new RegionKey(4, 14, 8192, 8192));
        var terrain = new PlanetTerrain(PlanetConstants.PlanetRadiusCm,
                                        new AxisedHeightSource(frame.East, RampAmplitudeCm));
        var heights = new float[PlanetPatchSampler.HeightmapLength];
        PlanetPatchSampler.FillPatchHeights(frame, terrain, 0, 0, heights);

        double alongX = heights[PlanetPatchSampler.HeightIndex(508, 0)] - heights[PlanetPatchSampler.HeightIndex(0, 0)];
        double alongZ = heights[PlanetPatchSampler.HeightIndex(0, 508)] - heights[PlanetPatchSampler.HeightIndex(0, 0)];

        // 坡率 = 振幅 / 半径；508 m 应抬升约 8 m，而横向（南北）几乎不动
        double expectedRise = RampAmplitudeCm * PlanetConstants.TerrainPatchSizeCm / PlanetConstants.PlanetRadiusCm;
        Assert.True(Math.Abs(alongX - expectedRise) < expectedRise * 0.02,
                    $"东向坡抬升 {alongX:F2} cm，期望 {expectedRise:F2} cm");
        Assert.True(Math.Abs(alongZ) < 5.0, $"沿 Z（南）出现了 {alongZ:F2} cm 的一阶变化，轴向接错了");

        // 单调性：沿 x 每一步都在涨，排除"整块镜像"
        for (int x = 1; x < PlanetPatchSampler.HeightmapEdge; x++)
            Assert.True(heights[PlanetPatchSampler.HeightIndex(x, 0)] > heights[PlanetPatchSampler.HeightIndex(x - 1, 0)]);
    }

    [Fact]
    public void SouthFacingRamp_RisesAlongLocalZ_NotAlongX()
    {
        var frame = Frame(new RegionKey(1, 14, 1234, 5678));
        var terrain = new PlanetTerrain(PlanetConstants.PlanetRadiusCm,
                                        new AxisedHeightSource(frame.South, RampAmplitudeCm));
        var heights = new float[PlanetPatchSampler.HeightmapLength];
        PlanetPatchSampler.FillPatchHeights(frame, terrain, 0, 0, heights);

        double alongZ = heights[PlanetPatchSampler.HeightIndex(0, 508)] - heights[PlanetPatchSampler.HeightIndex(0, 0)];
        double alongX = heights[PlanetPatchSampler.HeightIndex(508, 0)] - heights[PlanetPatchSampler.HeightIndex(0, 0)];

        double expectedRise = RampAmplitudeCm * PlanetConstants.TerrainPatchSizeCm / PlanetConstants.PlanetRadiusCm;
        Assert.True(Math.Abs(alongZ - expectedRise) < expectedRise * 0.02,
                    $"南向坡抬升 {alongZ:F2} cm，期望 {expectedRise:F2} cm");
        Assert.True(Math.Abs(alongX) < 5.0, $"沿 X（东）出现了 {alongX:F2} cm 的一阶变化，轴向接错了");

        for (int z = 1; z < PlanetPatchSampler.HeightmapEdge; z++)
            Assert.True(heights[PlanetPatchSampler.HeightIndex(0, z)] > heights[PlanetPatchSampler.HeightIndex(0, z - 1)]);
    }

    [Fact]
    public void PatchAtNeighbourOrigin_ShiftsMonotonically()
    {
        // 相邻 patch 的接缝值必须能续上：同一个东向坡，第 1 块 patch 的右边高度 = 第 2 块 patch 的左边高度
        var frame = Frame(new RegionKey(0, 14, 8192, 8192));
        var terrain = new PlanetTerrain(PlanetConstants.PlanetRadiusCm,
                                        new AxisedHeightSource(frame.East, RampAmplitudeCm));
        int edge = PlanetPatchSampler.HeightmapEdge;

        var left = new float[PlanetPatchSampler.HeightmapLength];
        var right = new float[PlanetPatchSampler.HeightmapLength];
        PlanetPatchSampler.FillPatchHeights(frame, terrain, 0, 0, left);
        PlanetPatchSampler.FillPatchHeights(frame, terrain, PlanetConstants.TerrainPatchSizeCm, 0, right);

        // 两块 patch 共用一列顶点（patch 高度图边界重叠是引擎的约定）
        for (int z = 0; z < 5; z++)
            Assert.Equal(left[PlanetPatchSampler.HeightIndex(edge - 1, z)], right[PlanetPatchSampler.HeightIndex(0, z)], 3);
    }
}

using System;
using Horizon.Planet;
using Horizon.Planet.Dem;
using Xunit;

namespace Horizon.Planet.Tests;

/// <summary>
/// 窗口缓存采样器必须与逐点路径逐字同值——它是性能改写，不该改变任何一个数。
/// 三条分支各自被"计数器 + 等值"双重钉住：双三次缓存、双线性缓存、以及委托回 DemGrid。
/// </summary>
public class HeightSamplerTests
{
    private const int Rows = 128;
    private const int Cols = 256;
    private const double StepDeg = 1.0;

    private static short?[,] QuadraticField(bool withHole = false)
    {
        var g = new short?[Rows, Cols];
        for (int r = 0; r < Rows; r++)
        {
            for (int c = 0; c < Cols; c++)
            {
                int v = (c * c) / 4 + r;                    // 二次：错一个缓存偏移就会显出差别
                if (withHole && r == 60 && c is >= 120 and <= 124) v = -32767;
                g[r, c] = (short)(v > 32000 ? 32000 : v);
            }
        }
        return g;
    }

    private static void ComparePaths(DemGrid grid, HybridHeightSource hybrid, bool withHole, int samples)
    {
        var sampler = new HeightSampler(grid, hybrid);
        var rng = new Random(withHole ? 99 : 1234);
        int compared = 0;

        for (int i = 0; i < samples; i++)
        {
            // 洞在 (row=60, col=120..124)：步长 1° 时对应 lat≈29.5、lon≈-59.5，采样窗口要罩住它
            PlanetVector3 dir = withHole
                ? Geodetic.DirectionFromLatLon((27.5 + rng.NextDouble() * 5.0) * Math.PI / 180.0,
                                              (-62.0 + rng.NextDouble() * 6.0) * Math.PI / 180.0)
                : CubeSphereTests.RandomUnitDirection(rng);

            Geodetic.LatLonFromDirection(dir, out double latRad, out double lonRad);
            double direct = hybrid.Profile.UseCatmullRom
                ? grid.SampleBicubicCmAt(grid.Manifest.Finest.Level, latRad, lonRad)
                : grid.SampleElevationCmAt(grid.Manifest.Finest.Level, latRad, lonRad);

            // 两条路径现在做的是同一串算术，要求逐位相等；只要缓存偏移错一格，量级就会差出米
            Assert.Equal(direct, sampler.BaseElevationCm(dir));
            Assert.Equal(hybrid.SampleElevationCm(dir), sampler.SampleElevationCm(dir));
            compared++;
        }

        Assert.Equal(samples, compared);
        Assert.True(sampler.CachedSamples > 0, $"快速路径一次没跑到（委托 {sampler.DelegatedSamples} 次），这条用例是空转");
        if (withHole)
            Assert.True(sampler.DelegatedSamples > 0, "含 nodata 的模板没有回落到 DemGrid，回退分支没被测到");
    }

    [Fact]
    public void CachedCatmullRomPath_MatchesPerPointPath()
    {
        using var dem = TempDem.Create(StepDeg, (0, QuadraticField()));
        using var grid = new DemGrid(dem.Load());
        var profile = new TerrainProfile(PlanetConstants.PlanetRadiusCm, StepDeg, useCatmullRom: true);
        var hybrid = new HybridHeightSource(grid, profile, 0x1L, 1);
        ComparePaths(grid, hybrid, withHole: false, samples: 2000);
    }

    [Fact]
    public void CachedBilinearPath_MatchesPerPointPath()
    {
        using var dem = TempDem.Create(StepDeg, (0, QuadraticField()));
        using var grid = new DemGrid(dem.Load());
        var profile = new TerrainProfile(PlanetConstants.PlanetRadiusCm, StepDeg, useCatmullRom: false);
        var hybrid = new HybridHeightSource(grid, profile, 0x1L, 1);
        ComparePaths(grid, hybrid, withHole: false, samples: 2000);
    }

    [Fact]
    public void HoleInStencil_DelegatesAndStillMatches()
    {
        using var dem = TempDem.Create(StepDeg, (0, QuadraticField(withHole: true)));
        using var grid = new DemGrid(dem.Load());
        var profile = new TerrainProfile(PlanetConstants.PlanetRadiusCm, StepDeg, useCatmullRom: true);
        var hybrid = new HybridHeightSource(grid, profile, 0x1L, 1);
        ComparePaths(grid, hybrid, withHole: true, samples: 400);
    }

    [Fact]
    public void PatchFill_WithSampler_MatchesWithoutSampler()
    {
        using var dem = TempDem.Create(StepDeg, (0, QuadraticField()));
        using var grid = new DemGrid(dem.Load());
        var profile = new TerrainProfile(PlanetConstants.PlanetRadiusCm, StepDeg);
        var hybrid = new HybridHeightSource(grid, profile, 0x1L, 1);
        var terrain = new PlanetTerrain(PlanetConstants.PlanetRadiusCm, hybrid);
        var frame = RegionFrame.Build(new RegionKey(4, 14, 6400, 6400));   // 北极附近：极边与缓存并存

        var plain = new float[PlanetPatchSampler.HeightmapLength];
        var cached = new float[PlanetPatchSampler.HeightmapLength];
        PlanetPatchSampler.FillPatchHeights(frame, terrain, 0, 0, plain, null, parallel: false);
        var sampler = new HeightSampler(grid, hybrid);
        // 并行时每个线程各有一份采样器，计数挂在传入的那个实例上不算数，这里走串行验证快速路径
        PlanetPatchSampler.FillPatchHeights(frame, terrain, 0, 0, cached, sampler, parallel: false);

        int different = 0;
        for (int i = 0; i < plain.Length; i++)
            if (Math.Abs(plain[i] - cached[i]) > 1e-3) different++;
        Assert.Equal(0, different);
        Assert.True(sampler.CachedSamples > 0, "patch 路径没走窗口缓存，这条用例只测了慢路径");
    }

    [Fact]
    public void ParallelFill_IsBitIdenticalToSerial()
    {
        // 并行只该改变耗时，不该改变任何一个数；线程数与遍历顺序都不进入结果
        using var dem = TempDem.Create(StepDeg, (0, QuadraticField()));
        using var grid = new DemGrid(dem.Load());
        var profile = new TerrainProfile(PlanetConstants.PlanetRadiusCm, StepDeg);
        var hybrid = new HybridHeightSource(grid, profile, 0x1L, 1);
        var terrain = new PlanetTerrain(PlanetConstants.PlanetRadiusCm, hybrid);
        var frame = RegionFrame.Build(new RegionKey(0, 14, 9000, 9000));
        var sampler = new HeightSampler(grid, hybrid);

        var serial = new float[PlanetPatchSampler.HeightmapLength];
        var parallel = new float[PlanetPatchSampler.HeightmapLength];
        PlanetPatchSampler.FillPatchHeights(frame, terrain, 0, 0, serial, sampler, parallel: false);
        PlanetPatchSampler.FillPatchHeights(frame, terrain, 0, 0, parallel, sampler, parallel: true);

        for (int i = 0; i < serial.Length; i++)
            Assert.Equal(serial[i], parallel[i]);
    }

    [Fact]
    public void Sampler_RejectsMismatchedGrid()
    {
        using var demA = TempDem.Create(StepDeg, (0, QuadraticField()));
        using var demB = TempDem.Create(StepDeg, (0, QuadraticField()));
        using var gridA = new DemGrid(demA.Load());
        using var gridB = new DemGrid(demB.Load());
        var hybrid = new HybridHeightSource(gridA, new TerrainProfile(PlanetConstants.PlanetRadiusCm, StepDeg), 0x1L, 1);

        Assert.Throws<ArgumentException>(() => new HeightSampler(gridB, hybrid));
        Assert.Throws<ArgumentNullException>(() => new HeightSampler(null!));
    }
}

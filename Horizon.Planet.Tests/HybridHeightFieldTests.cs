using System;
using System.Collections.Generic;
using Horizon.Planet;
using Horizon.Planet.Dem;
using Xunit;

namespace Horizon.Planet.Tests;

public class HybridHeightFieldTests
{
    private const double RealStepDeg = 15.0 / 3600.0;

    private static short?[,] Constant(short? value, int rows = 4, int cols = 8)
    {
        var g = new short?[rows, cols];
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                g[r, c] = value;
        return g;
    }

    private static short?[,] LinearField()
    {
        var g = new short?[4, 8];
        for (int r = 0; r < 4; r++)
            for (int c = 0; c < 8; c++)
                g[r, c] = (short)(10 * c + r);
        return g;
    }

    private static TerrainProfile Profile(double gain = 1.0, double finestAmp = 15.0, double hurst = 0.75,
                                          bool underWater = false, bool catmull = true, double intertidalCm = 1500.0) =>
        new(PlanetConstants.PlanetRadiusCm, RealStepDeg, gain, PlanetConstants.TerrainUnitsPerVertex,
            finestAmp, hurst, underWater, catmull, intertidalCm);

    // ---------- Catmull-Rom ----------

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.25)]
    [InlineData(0.5)]
    [InlineData(0.75)]
    [InlineData(1.0)]
    public void CatmullRomWeights_SumToOne_AndReproduceLinearAndQuadratic(double t)
    {
        double[] w = DemGrid.CatmullRomWeights(t);
        double sum = 0;
        double linear = 0;
        double quadratic = 0;
        for (int i = 0; i < 4; i++)
        {
            double x = i - 1;                      // 抽头坐标 -1,0,1,2
            sum += w[i];
            linear += w[i] * x;
            quadratic += w[i] * x * x;
        }
        Assert.Equal(1.0, sum, 12);
        Assert.Equal(t, linear, 12);
        // 中心差分斜率对二次以上是精确的（误差项含 f‴），所以判"复现二次"而不是三次
        Assert.Equal(t * t, quadratic, 12);
    }

    [Fact]
    public void Bicubic_MatchesBilinear_OnLinearField()
    {
        using var dem = TempDem.Create(45.0, (0, LinearField()));
        using var grid = new DemGrid(dem.Load());

        // 只在 4×4 模板不跨经度回绕、不触极边行的内部窗口里比：跨界处两条路径本来就该不同
        for (int i = 0; i < 400; i++)
        {
            double latDeg = -20.0 + i * 0.1;                  // row0 = 1，模板覆盖 row 0..3
            double lonDeg = -110.0 + i * 0.5;                 // col0 ∈ [1,5]，模板覆盖 col 0..7
            double lat = latDeg * Math.PI / 180.0;
            double lon = lonDeg * Math.PI / 180.0;
            Assert.Equal(grid.SampleElevationCmAt(0, lat, lon), grid.SampleBicubicCmAt(0, lat, lon), 6);
        }
    }

    [Fact]
    public void Bicubic_ReproducesQuadraticColumn()
    {
        // 1D 二次场：v = c²（c=0..7 → 最大 49）。Catmull-Rom 对二次精确复现，双线性不行——
        // 这条同时钉住"基场用双三次"真的比双线性多做了一件事，不是换个名字读同一份数。
        var g = new short?[4, 8];
        for (int r = 0; r < 4; r++)
            for (int c = 0; c < 8; c++)
                g[r, c] = (short)(c * c);
        using var dem = TempDem.Create(45.0, (0, g));
        using var grid = new DemGrid(dem.Load());

        double lat = 22.5 * Math.PI / 180.0;                 // row0=1，模板覆盖 row 0..3，不触极边
        int differsFromBilinear = 0;
        for (int i = 0; i < 60; i++)
        {
            // 采样列坐标 x 限制在 [1,5]，4×4 模板正好落在 0..7 列内（跨过 ±180° 回绕时复现不成立）
            double x = 1.0 + i * (4.0 / 59.0);
            double lonDeg = -180.0 + (x + 0.5) * 45.0;
            double expected = x * x;
            double got = grid.SampleBicubicCmAt(0, lat, lonDeg * Math.PI / 180.0) / 100.0;
            Assert.Equal(expected, got, 6);
            double linearGuess = grid.SampleElevationCmAt(0, lat, lonDeg * Math.PI / 180.0) / 100.0;
            if (Math.Abs(linearGuess - expected) > 1e-3) differsFromBilinear++;
        }
        Assert.True(differsFromBilinear > 20, "双线性在这段居然也和二次吻合，说明夹具没有区分度");
    }

    [Fact]
    public void Bicubic_FallsBackToBilinear_WhenStencilHitsNodata()
    {
        // 三次场 + 一个洞：模板不含洞时两条路径必须给出不同值（说明夹具真有鉴别力），
        // 含洞时必须逐字退回双线性（说明回退分支真被执行）。
        var g = new short?[4, 8];
        for (int r = 0; r < 4; r++)
            for (int c = 0; c < 8; c++)
                g[r, c] = (short)(c * c * c);
        g[1, 3] = -32767;

        using var dem = TempDem.Create(45.0, (0, g));
        using var grid = new DemGrid(dem.Load());

        double lat = 22.5 * Math.PI / 180.0;
        int fellBack = 0;
        int discriminative = 0;
        for (int i = 0; i < 40; i++)
        {
            double x = 0.2 + i * 0.17;                       // 采样列坐标
            double lonDeg = -180.0 + (x + 0.5) * 45.0;
            double bi = grid.SampleElevationCmAt(0, lat, lonDeg * Math.PI / 180.0);
            double bic = grid.SampleBicubicCmAt(0, lat, lonDeg * Math.PI / 180.0);

            int col0 = (int)Math.Floor(x);
            bool stencilCoversHole = 3 >= col0 - 1 && 3 <= col0 + 2;
            if (stencilCoversHole)
            {
                Assert.Equal(bi, bic, 9);
                fellBack++;
            }
            else
            {
                Assert.NotEqual(bi, bic);
                discriminative++;
            }
        }
        Assert.True(fellBack > 0, "没有样本落进含 nodata 的模板，回退分支其实没被测到");
        Assert.True(discriminative > 0, "对照组为空：双线性与双三次在洞外也没区别，夹具没有鉴别力");
    }

    // ---------- Profile 口径 ----------

    [Fact]
    public void Profile_CrossWavelength_IsTwiceTheBasePixel()
    {
        var p = Profile();
        Assert.Equal(2 * p.CellWavelengthCm, p.CrossWavelengthCm, 9);
        // 10× 半径 + 15″ ⇒ 单像元 4.63 km、λ_c 9.27 km。这是实测数字，改半径/步长就得跟着改断言
        Assert.InRange(p.CellWavelengthCm / 100000.0, 4.62, 4.64);
        Assert.InRange(p.CrossWavelengthCm / 100000.0, 9.26, 9.28);
        Assert.Equal(14, p.BandCount);
        Assert.Equal(p.CrossWavelengthCm, p.WavelengthsCm[0], 9);
        for (int k = 1; k < p.BandCount; k++)
        {
            Assert.Equal(p.WavelengthsCm[k - 1] / 2.0, p.WavelengthsCm[k], 6);
            Assert.True(p.AmplitudesCm[k] < p.AmplitudesCm[k - 1], "带幅度必须随波长单调递减");
            Assert.True(p.WavelengthsCm[k] <= p.CrossWavelengthCm, "细节带不许越过 λ_c，否则与基场重复计数");
        }
        // λ_min=1 m 只是标定锚点，最后一条带的波长是"≤λ_min 的第一次减半"，会比 1 m 略粗，
        // 所以末带幅度应当落在 FinestBandAmplitudeCm 的同一量级内，而不是恰好相等。
        double lastAmplitude = p.AmplitudesCm[p.BandCount - 1];
        Assert.True(lastAmplitude > p.FinestBandAmplitudeCm * 0.5 && lastAmplitude < p.FinestBandAmplitudeCm * 4.0,
                    $"末带幅度 {lastAmplitude:F2} cm 与标定值 {p.FinestBandAmplitudeCm:F2} cm 不在同一量级");
        Assert.True(p.WavelengthsCm[p.BandCount - 1] >= p.MinWavelengthCm,
                    "最后一条带比 λ_min 还细，带数算错了");
    }

    [Fact]
    public void TerrainVersion_ChangesWithEveryKnob()
    {
        const long hash = 0x39bb6eb45a785101L;
        uint baseline = Profile().ComputeTerrainVersion(hash, 2026);

        var mutants = new Dictionary<string, uint>
        {
            ["baseGain"] = Profile(gain: 1.5).ComputeTerrainVersion(hash, 2026),
            ["finestAmp"] = Profile(finestAmp: 25.0).ComputeTerrainVersion(hash, 2026),
            ["hurst"] = Profile(hurst: 0.9).ComputeTerrainVersion(hash, 2026),
            ["detailUnderWater"] = Profile(underWater: true).ComputeTerrainVersion(hash, 2026),
            ["catmullRom"] = Profile(catmull: false).ComputeTerrainVersion(hash, 2026),
            ["intertidal"] = Profile(intertidalCm: 2500.0).ComputeTerrainVersion(hash, 2026),
            ["basefieldVersion"] = Profile().ComputeTerrainVersion(hash, 2027),
            ["basefieldHash"] = Profile().ComputeTerrainVersion(hash + 1, 2026),
        };
        foreach (var pair in mutants)
            Assert.NotEqual(baseline, pair.Value);

        Assert.Equal(baseline, Profile().ComputeTerrainVersion(hash, 2026));   // 稳定
        Assert.NotEqual(0u, baseline);
    }

    // ---------- 混合场行为 ----------

    [Fact]
    public void Hybrid_EqualsBaseGainTimesBasePlusDetail()
    {
        using var dem = TempDem.Create(45.0, (0, Constant(100)));     // 全场 100 m 陆地
        using var grid = new DemGrid(dem.Load());
        var profile = Profile(gain: 2.0, finestAmp: 40.0);
        var hybrid = new HybridHeightSource(grid, profile, unchecked((long)0x1122334455667788UL), 2026);

        var rng = new Random(20261102);
        for (int i = 0; i < 50; i++)
        {
            PlanetVector3 dir = CubeSphereTests.RandomUnitDirection(rng);
            double baseCm = hybrid.BaseFieldElevationCm(dir);
            Assert.Equal(10000.0, baseCm, 6);                          // 常数场两种插值都精确
            double expected = 2.0 * baseCm + hybrid.SampleDetailCm(dir * PlanetConstants.PlanetRadiusCm);
            Assert.Equal(expected, hybrid.SampleElevationCm(dir), 9);
            Assert.NotEqual(expected, 2.0 * baseCm);                   // 细节确实加进去了，不是空转
        }
    }

    [Theory]
    [InlineData(-2000, false, true)]   // 深海：默认不叠细节
    [InlineData(-2000, true, false)]   // 深海 + 允许水下细节
    [InlineData(-10, false, false)]    // 潮间带（−10 m > −15 m 阈值）：要叠细节
    public void Hybrid_DetailGate_RespectsWaterRule(short baseMetres, bool underWater, bool expectNoDetail)
    {
        using var dem = TempDem.Create(45.0, (0, Constant(baseMetres)));
        using var grid = new DemGrid(dem.Load());
        var hybrid = new HybridHeightSource(grid, Profile(underWater: underWater), 0x01L, 2026);

        PlanetVector3 dir = Geodetic.DirectionFromLatLon(0.3, -1.1);
        double total = hybrid.SampleElevationCm(dir);
        double baseCm = hybrid.BaseFieldElevationCm(dir);
        double detail = hybrid.SampleDetailCm(dir * PlanetConstants.PlanetRadiusCm);

        if (expectNoDetail)
        {
            Assert.Equal(baseCm, total, 9);
            Assert.True(Math.Abs(detail) > 1.0, "夹具本身没被噪声激励，这条用例等于没跑");
        }
        else
        {
            Assert.Equal(baseCm + detail, total, 9);
        }
    }

    [Fact]
    public void Noise_IsDeterministic_AndDependsOnlyOnWorldPosition()
    {
        using var dem = TempDem.Create(45.0, (0, Constant(50)));
        using var grid = new DemGrid(dem.Load());
        var a = new HybridHeightSource(grid, Profile(), unchecked((long)0xAABBCCDDEEFF0011UL), 2026);
        var b = new HybridHeightSource(grid, Profile(), unchecked((long)0xAABBCCDDEEFF0011UL), 2026);
        var c = new HybridHeightSource(grid, Profile(), unchecked((long)0xAABBCCDDEEFF0012UL), 2026);

        var rng = new Random(5);
        for (int i = 0; i < 200; i++)
        {
            PlanetVector3 dir = CubeSphereTests.RandomUnitDirection(rng);
            PlanetVector3 world = dir * PlanetConstants.PlanetRadiusCm;

            Assert.Equal(a.SampleDetailCm(world), a.SampleDetailCm(world));   // 同实例自反
            Assert.Equal(a.SampleDetailCm(world), b.SampleDetailCm(world));   // 同种子双端一致
            Assert.True(Math.Abs(a.SampleDetailCm(world) - c.SampleDetailCm(world)) > 1e-6,
                        "换了种子却没换出结果，噪声根本没吃到 seed");

            // 位置纯度：换一个区域帧绕一圈回到同一个世界点，细节必须还是那个值
            var frame = RegionFrame.Build(RegionKey.FromDirection(dir, PlanetConstants.DefaultRegionLevel));
            PlanetVector3 local = frame.LocalFromGlobalCm(world);
            PlanetVector3 roundTripped = frame.GlobalFromLocalCm(local);
            Assert.True(Math.Abs(a.SampleDetailCm(roundTripped) - a.SampleDetailCm(world)) < 1e-4,
                        "换帧后细节变了，跨 patch 会出接缝");
        }
    }

    [Fact]
    public void Hybrid_RejectsCoarseBaseField_ButAcceptsFinest()
    {
        var manifest = TempDem.Create(45.0, (0, Constant(100)), (1, Constant(100, 2, 4))).Load();

        using var coarse = new DemGrid(manifest, preferredLevel: 1);
        Assert.False(coarse.IsFinestLevel);
        var rejected = Assert.Throws<ArgumentException>(() =>
            new HybridHeightSource(coarse, Profile(), 0x1L, 2026));
        Assert.Contains("最细一级", rejected.Message);

        using var finest = new DemGrid(manifest);
        Assert.True(finest.IsFinestLevel);
        var accepted = new HybridHeightSource(finest, Profile(), 0x1L, 2026);
        Assert.Equal(2026, accepted.BasefieldVersion);
        Assert.NotEqual(0u, accepted.TerrainVersion);
    }

    [Fact]
    public void Hybrid_RejectsMismatchedRadius()
    {
        using var dem = TempDem.Create(45.0, (0, Constant(100)));
        using var grid = new DemGrid(dem.Load());
        var wrongRadius = new TerrainProfile(PlanetConstants.EarthMeanRadiusCm, RealStepDeg);
        Assert.Throws<ArgumentException>(() => new HybridHeightSource(grid, wrongRadius, 0x1L, 2026));
    }
}

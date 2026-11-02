using System;
using Horizon.Planet;
using Horizon.Planet.Dem;
using Xunit;

namespace Horizon.Planet.Tests;

/// <summary>
/// §13.10 路线 B 的判据：面积平均采样器。
/// <para>核心性质不是"数值变平滑了"这种观感，而是：同一个 footprint 的均值几乎不随采样相位变化
/// ——单点采样会命中或错过山峰，那才是远景"山随视角忽有忽无"的根源。</para>
/// </summary>
public class DemBoxAverageSourceTests
{
    private const double Step = 45.0;          // 夹具每格 45°，4 行 × 8 列正好铺满全球
    private const int Rows = 4, Cols = 8;

    private static double LatCentre(int row) => 90.0 - (row + 0.5) * Step;
    private static double LonCentre(int col) => -180.0 + (col + 0.5) * Step;

    private static PlanetVector3 Direction(double latDeg, double lonDeg) =>
        Geodetic.DirectionFromLatLon(latDeg * Math.PI / 180.0, lonDeg * Math.PI / 180.0);

    private static short?[,] Fill(short value)
    {
        var g = new short?[Rows, Cols];
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Cols; c++)
                g[r, c] = value;
        return g;
    }

    private static double Spread(double[] values)
    {
        double min = double.MaxValue, max = double.MinValue;
        foreach (double v in values) { if (v < min) min = v; if (v > max) max = v; }
        return max - min;
    }

    [Fact]
    public void TapsOfOne_MatchesPointSampleExactly()
    {
        var g = Fill(1234);
        g[1, 3] = 5000;
        using var dem = TempDem.Create(Step, (0, g));
        using var grid = new DemGrid(dem.Load());
        var box = new DemBoxAverageSource(grid, 1);

        Assert.Equal(0, box.Level);
        Assert.Equal(45.0, box.FootprintDeg, 9);
        for (int r = 0; r < Rows; r++)
        {
            for (int c = 0; c < Cols; c++)
            {
                var dir = Direction(LatCentre(r), LonCentre(c));
                Assert.Equal(grid.SampleElevationCm(dir), box.SampleElevationCm(dir), 9);   // 退化档必须同值
            }
        }
    }

    [Fact]
    public void ConstantField_StayExact_AtPolesAndDateLine()
    {
        using var dem = TempDem.Create(Step, (0, Fill(777)));
        using var grid = new DemGrid(dem.Load());
        var box = new DemBoxAverageSource(grid, 4);

        double expected = 777 * 100.0;
        foreach (var (lat, lon) in new[]
        {
            (90.0, 0.0), (-90.0, 123.0), (0.0, 180.0), (0.0, -180.0), (45.0, 179.9), (-67.0, -0.1),
        })
        {
            double v = box.SampleElevationCm(Direction(lat, lon));
            Assert.True(Math.Abs(v - expected) < 1e-6,
                        $"常数场在 ({lat},{lon}) 读成 {v:F4}，期望 {expected:F1}");
        }
    }

    [Fact]
    public void Spike_StopsComingAndGoingWithSamplingPhase()
    {
        var g = Fill(100);
        g[1, 2] = 5000;                                  // 一根孤立山峰
        using var dem = TempDem.Create(Step, (0, g));
        using var grid = new DemGrid(dem.Load());
        var point = new DemBoxAverageSource(grid, 1);
        var averaged = new DemBoxAverageSource(grid, 2);

        // 沿山峰所在纬度带横扫两个格宽，逐点采样会从"错过"跳到"正中"
        double lat = LatCentre(1);
        var pointVals = new double[9];
        var averagedVals = new double[9];
        for (int i = 0; i < 9; i++)
        {
            double lon = LonCentre(2) + (i - 4) * (Step / 4.0);
            var dir = Direction(lat, lon);
            pointVals[i] = point.SampleElevationCm(dir);
            averagedVals[i] = averaged.SampleElevationCm(dir);
        }

        double pointSpread = Spread(pointVals), averagedSpread = Spread(averagedVals);
        Assert.True(pointSpread > 2000 * 100.0,
                    $"夹具没造出可见的摩尔纹：逐点采样摆幅只有 {pointSpread / 100.0:F0} m");
        Assert.True(averagedSpread < pointSpread / 4.0,
                    $"面积平均后摆幅 {averagedSpread / 100.0:F0} m 没压到逐点 {pointSpread / 100.0:F0} m 的 1/4 以下");

        // 另一半：峰没有凭空消失（均值里必须还留着它的份额），否则"平滑"可能只是把场抹成常数
        Assert.True(Spread(averagedVals) > 10.0, "平均结果成了常数，说明 footprint 大过头或抽头没落到场上");
        Assert.True(pointVals[4] > averagedVals[4], "正中峰顶时均值反而更高：抽头分布不对");

        Assert.Equal(45.0 * 2, averaged.FootprintDeg, 9);
        Assert.Equal(2, averaged.TapsPerEdge);
    }

    [Fact]
    public void Mean_UsesCosLatitudeWeights_MatchesClosedForm()
    {
        // 场只随纬度变：v(row) = 1000·r 米。此时经度方向的 k 个抽头同值，闭式只剩纬度加权和
        var g = new short?[Rows, Cols];
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Cols; c++)
                g[r, c] = (short)(1000 * r);
        using var dem = TempDem.Create(Step, (0, g));
        using var grid = new DemGrid(dem.Load());
        var box = new DemBoxAverageSource(grid, 4);

        // 查询 lat=45°，k=4 → 抽头纬度 = 45 ± 1.5·45 = −22.5, 22.5, 67.5, 112.5(钳到 90)
        double[] tapLat = { -22.5, 22.5, 67.5, 90.0 };
        double[] tapValueCm = { 2000 * 100.0, 1000 * 100.0, 0.0, 0.0 };   // 90° 钳位后落进 row0（值 0）
        double wsum = 0, vsum = 0, unwsum = 0;
        for (int i = 0; i < tapLat.Length; i++)
        {
            double w = Math.Cos(tapLat[i] * Math.PI / 180.0);
            if (w <= 0.0) continue;
            wsum += w; vsum += w * tapValueCm[i];
            unwsum += 1.0;
        }
        double weighted = vsum / wsum;
        double unweighted = (2000 * 100.0 + 1000 * 100.0 + 0.0 + 0.0) / 4.0;

        double got = box.SampleElevationCm(Direction(45.0, LonCentre(3)));
        Assert.True(Math.Abs(got - weighted) < 1e-6,
                    $"面积加权均值 {got:F4} 与闭式 {weighted:F4} 不符（wsum={wsum:F4}）");
        Assert.True(Math.Abs(got - unweighted) > 10 * 100.0,
                    "加了 cos 权重却和不加权一样：权重根本没起作用");
        Assert.Equal(4.0, unwsum);              // 反向确认：不加权就是 4 票
    }

    [Fact]
    public void ExtremeQueries_StayFiniteAndWithinFieldRange()
    {
        var g = Fill(0);
        g[0, 0] = 8000;
        g[3, 4] = -9000;                                  // 深海沟
        using var dem = TempDem.Create(Step, (0, g));
        using var grid = new DemGrid(dem.Load());
        var box = new DemBoxAverageSource(grid, 8);
        Assert.Equal(8, DemBoxAverageSource.MaxTapsPerEdge);

        for (int i = 0; i < 200; i++)
        {
            double lat = -90.0 + i * 0.9;               // 从南极扫到接近北极，含钳位区
            double lon = -180.0 + (i * 17.3) % 360.0;
            double v = box.SampleElevationCm(Direction(lat, lon));
            Assert.True(double.IsFinite(v), $"({lat:F2},{lon:F2}) 读出 {v}");
            Assert.InRange(v, -9000 * 100.0 - 1e-3, 8000 * 100.0 + 1e-3);   // 均值不许造出场外极值
        }
    }

    [Fact]
    public void Rejects_TapCountsOutsideTheAllowedBand()
    {
        using var dem = TempDem.Create(Step, (0, Fill(10)));
        using var grid = new DemGrid(dem.Load());
        Assert.Throws<ArgumentOutOfRangeException>(() => new DemBoxAverageSource(grid, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DemBoxAverageSource(grid, -2));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DemBoxAverageSource(grid, 9));
        Assert.Throws<ArgumentNullException>(() => new DemBoxAverageSource(null!, 4));

        // 正向对照：1..8 都建得出来，且 8 档的 footprint = 8 个像元
        var wide = new DemBoxAverageSource(grid, 8);
        Assert.Equal(360.0, wide.FootprintDeg, 9);
        Assert.Contains("taps=8x8", wide.Describe());
        Assert.Contains("level=0", wide.Describe());
    }

    [Fact]
    public void NodataHole_DoesNotMakeTheAverageGoWild()
    {
        var g = Fill(2000);
        g[1, 3] = null;                                  // 一个洞
        g[2, 3] = null;
        using var dem = TempDem.Create(Step, (0, g));
        using var grid = new DemGrid(dem.Load()) { NodataFallbackCm = 0.0 };
        var box = new DemBoxAverageSource(grid, 2);
        var point = new DemBoxAverageSource(grid, 1);

        // 每个抽头自己按 DemGrid 的口径处理 nodata（重归一/最近有效），这里只要求均值不越界、不发散
        for (int i = 0; i < 8; i++)
        {
            double lon = LonCentre(3) + (i - 4) * (Step / 2.0);
            double v = box.SampleElevationCm(Direction(LatCentre(1), lon));
            Assert.True(double.IsFinite(v));
            Assert.InRange(v, 0.0 - 1e-3, 2000 * 100.0 + 1e-3);
        }
        Assert.True(double.IsFinite(point.SampleElevationCm(Direction(LatCentre(1), LonCentre(3)))));
    }

    [Fact]
    public void PlanSampling_FootprintMustActuallyCoverTheCell()
    {
        var manifest = SyntheticManifest(1.0 / 240.0, 6);       // 15″，0..5 级（真实产物的形状）
        double c3 = PlanetLod.CellWidthCm(manifest, 3);         // 2′   ≈ 37.1 km
        double c5 = PlanetLod.CellWidthCm(manifest, 5);         // 8′   ≈ 148.3 km
        Assert.True(Math.Abs(c3 - 37.06e5) < 0.05e5 && Math.Abs(c5 - 148.26e5) < 0.05e5,
                    $"像元宽度不对：c3={c3 / 1e5:F2} km c5={c5 / 1e5:F2} km");

        // 政策是"不采得比网格更细"：间距 ≤ 某级像元 → 选那一级，1 个抽头就够
        var matched = PlanetLod.PlanSampling(manifest, c3);
        Assert.Equal(3, matched.DemLevel);
        Assert.Equal(1, matched.TapsPerEdge);
        Assert.True(matched.FullyCovered);

        var justBelowC5 = PlanetLod.PlanSampling(manifest, c5 * 0.85);
        Assert.Equal(5, justBelowC5.DemLevel);
        Assert.Equal(1, justBelowC5.TapsPerEdge);

        // 间距超过最粗一级像元：只能用多抽头把 footprint 摊到罩住为止（区域级 6 的 tile 就是这一档）
        double regionLevel6Spacing = 497.51e5;                  // §13.8 表里量出来的值
        var mid = PlanetLod.PlanSampling(manifest, regionLevel6Spacing);
        Assert.Equal(5, mid.DemLevel);
        Assert.Equal(4, mid.TapsPerEdge);                        // ceil(497.51/148.26) = 4
        Assert.True(mid.FootprintCm >= mid.RequestedSpacingCm);
        Assert.True(mid.FullyCovered);
        Assert.Equal(4 * c5, mid.FootprintCm, 3);

        // 远景球面：间距 25,019 km 要 169 抽，被上限夹住 → 如实报"没罩住"
        var globe = PlanetLod.PlanSampling(manifest, 25018.86e5);
        Assert.Equal(5, globe.DemLevel);
        Assert.Equal(DemBoxAverageSource.MaxTapsPerEdge, globe.TapsPerEdge);
        Assert.False(globe.FullyCovered);
        Assert.Equal(8 * c5, globe.FootprintCm, 3);
        Assert.Contains("covered=False", globe.ToString());

        // 关系式扫描：covered 当且仅当 footprint ≥ 间距；抽头永远在 1..8
        for (double spacingKm = 20; spacingKm <= 30_000; spacingKm *= 1.6)
        {
            var plan = PlanetLod.PlanSampling(manifest, spacingKm * 1e5);
            Assert.InRange(plan.TapsPerEdge, 1, DemBoxAverageSource.MaxTapsPerEdge);
            Assert.Equal(plan.FootprintCm >= plan.RequestedSpacingCm - 1e-6, plan.FullyCovered);
            Assert.True(plan.CellWidthCm > 0 && plan.FootprintCm >= plan.CellWidthCm);
        }

        Assert.Throws<ArgumentNullException>(() => PlanetLod.PlanSampling(null!, 1000));
        Assert.Throws<ArgumentOutOfRangeException>(() => PlanetLod.PlanSampling(manifest, 0));
    }

    private static DemManifest SyntheticManifest(double stepDeg, int levels)
    {
        var manifest = new DemManifest
        {
            Format = 1,
            SourceName = "synthetic",
            SourceSha256 = new string('b', 64),
            ArcSecond = stepDeg * 3600.0,
            StepDeg = stepDeg,
            GridCols = 86400,
            GridRows = 43200,
            Levels = new System.Collections.Generic.List<DemLevel>(),
        };
        for (int i = 0; i < levels; i++)
        {
            manifest.Levels.Add(new DemLevel
            {
                Level = i,
                Cols = 86400 >> i,
                Rows = 43200 >> i,
                File = $"level_{i:00}.bin",
            });
        }
        return manifest;
    }
}

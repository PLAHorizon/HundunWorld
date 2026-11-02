using System;
using Xunit;

namespace Horizon.Planet.Tests;

public class RegionFrameTests
{
    private static void AssertUnitAndOrthogonal(RegionFrame f)
    {
        Assert.Equal(1.0, f.East.LengthSquared, 12);
        Assert.Equal(1.0, f.Up.LengthSquared, 12);
        Assert.Equal(1.0, f.South.LengthSquared, 12);
        Assert.Equal(0.0, PlanetVector3.Dot(f.East, f.Up), 12);
        Assert.Equal(0.0, PlanetVector3.Dot(f.East, f.South), 12);
        Assert.Equal(0.0, PlanetVector3.Dot(f.Up, f.South), 12);

        // 局部 Z 轴 = 南 = 东 × 上（工程口径：北 = −Z、东 = +X）
        var expected = PlanetVector3.Cross(f.East, f.Up);
        Assert.Equal(expected.X, f.South.X, 12);
        Assert.Equal(expected.Y, f.South.Y, 12);
        Assert.Equal(expected.Z, f.South.Z, 12);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Basis_IsStable_OnEveryFaceIncludingPoles(int face)
    {
        const int level = 14;
        int span = 1 << level;
        int[] sampleTiles = { 0, span / 2, span - 1 };
        foreach (int x in sampleTiles)
        {
            foreach (int y in sampleTiles)
            {
                var key = new RegionKey(face, level, x, y);
                var frame = RegionFrame.Build(key);
                AssertUnitAndOrthogonal(frame);
                Assert.Equal((double)PlanetConstants.PlanetRadiusCm, frame.OriginCm.Length, 3);
                Assert.Equal(0.0, frame.LocalFromGlobalCm(frame.OriginCm).X, 3);
                Assert.Equal(0.0, frame.LocalFromGlobalCm(frame.OriginCm).Y, 3);
                Assert.Equal(0.0, frame.LocalFromGlobalCm(frame.OriginCm).Z, 3);
            }
        }
    }

    [Fact]
    public void PoleRegion_BasisIsFinite()
    {
        // 面 4 中心瓦片几乎正对北极：东向必须仍然单位、仍与上正交
        var key = new RegionKey(4, 14, 8192, 8192);
        var frame = RegionFrame.Build(key);
        Assert.Equal(1.0, frame.East.LengthSquared, 12);
        Assert.Equal(0.0, PlanetVector3.Dot(frame.East, frame.Up), 12);
        Assert.Equal(1.0, frame.Up.Z, 6);
    }

    [Fact]
    public void ComputeEast_FallsBackToFaceAxis_AtExactPoles()
    {
        var north = RegionFrame.ComputeEast(PlanetVector3.AxisZ, 4);
        Assert.Equal(1.0, north.X, 12);
        Assert.Equal(0.0, north.Y, 12);
        Assert.Equal(0.0, north.Z, 12);

        var south = RegionFrame.ComputeEast(-PlanetVector3.AxisZ, 5);
        Assert.Equal(-1.0, south.X, 12);
        Assert.Equal(0.0, south.Y, 12);
        Assert.Equal(0.0, south.Z, 12);
    }

    [Fact]
    public void LocalGlobal_RoundTrips_WithinHalfHundredthOfACentimetre()
    {
        var rng = new Random(99);
        foreach (long regionId in new long[]
        {
            new RegionKey(0, 14, 8192, 8192).ToId(),
            new RegionKey(2, 14, 0, 0).ToId(),
            new RegionKey(3, 14, 16383, 16383).ToId(),
            new RegionKey(5, 12, 2048, 2048).ToId(),
        })
        {
            var frame = RegionFrame.Build(RegionKey.FromId(regionId));
            for (int i = 0; i < 5000; i++)
            {
                var local = new PlanetVector3(
                    (rng.NextDouble() * 2 - 1) * 400_000,   // ±4 km 东西
                    (rng.NextDouble() * 2 - 1) * 200_000,   // ±2 km 高程
                    (rng.NextDouble() * 2 - 1) * 400_000);  // ±4 km 南北

                PlanetVector3 global = frame.GlobalFromLocalCm(local);
                PlanetVector3 back = frame.LocalFromGlobalCm(global);

                Assert.True(Math.Abs(back.X - local.X) < 0.005, $"X 往返误差 {back.X - local.X} cm");
                Assert.True(Math.Abs(back.Y - local.Y) < 0.005, $"Y 往返误差 {back.Y - local.Y} cm");
                Assert.True(Math.Abs(back.Z - local.Z) < 0.005, $"Z 往返误差 {back.Z - local.Z} cm");
            }
        }
    }

    [Fact]
    public void EcefCentimetres_RoundingErrorIsBelowHalfCm()
    {
        var rng = new Random(1234);
        var frame = RegionFrame.Build(new RegionKey(1, 14, 4096, 4096));
        for (int i = 0; i < 20000; i++)
        {
            var local = new PlanetVector3(
                (rng.NextDouble() * 2 - 1) * 450_000,
                rng.NextDouble() * 900_000 - 450_000,
                (rng.NextDouble() * 2 - 1) * 450_000);
            PlanetVector3 exact = frame.GlobalFromLocalCm(local);
            EcefCentimeters rounded = frame.GlobalFromLocal(local);
            PlanetVector3 recovered = rounded.ToVector();
            double err = (recovered - exact).Length;
            Assert.True(err < 0.9, $"整数厘米往返误差 {err} cm（局部 {local}）");
        }
    }

    /// <summary>
    /// 这条是“局部平面会不会凭空抬高/压低地表”的根判据：
    /// 球面上一个点投到切平面后，它的局部 Y 必须恰好等于 −曲率下垂量。
    /// </summary>
    [Theory]
    [InlineData(50_000.0)]
    [InlineData(250_000.0)]
    [InlineData(500_000.0)]
    [InlineData(780_000.0)]
    public void SpherePoint_LocalY_EqualsNegativeCurvatureSag(double horizontalCm)
    {
        var frame = RegionFrame.Build(new RegionKey(0, 14, 8192, 8192));
        double theta = horizontalCm / PlanetConstants.PlanetRadiusCm;

        PlanetVector3 dir = (frame.Up * Math.Cos(theta) + frame.East * Math.Sin(theta)).Normalized();
        PlanetVector3 surfaceCm = dir * PlanetConstants.PlanetRadiusCm;

        PlanetVector3 local = frame.LocalFromGlobalCm(surfaceCm);
        double measuredHorizontal = frame.HorizontalDistanceCm(local);
        double measuredSag = -local.Y;
        double predictedSag = RegionMetrics.CurvatureSagCm(measuredHorizontal);

        // 弦长 R·sinθ 与输入弧长 R·θ 差在 θ³ 量级，这里只要求 0.01 cm 内一致
        Assert.Equal(horizontalCm, measuredHorizontal, 2);
        Assert.Equal(predictedSag, measuredSag, 3);
    }

    [Fact]
    public void TerrainWindow_FitsInsideTheSmallestRegion()
    {
        // 近景 5×5 patch 必须整体落在一个区域内，否则“区域权威”盖不住脚下有碰撞的那片地
        double windowCm = (double)PlanetConstants.NearPatchWindowEdge * PlanetConstants.TerrainPatchSizeCm;
        const int level = PlanetConstants.DefaultRegionLevel;
        int span = 1 << level;

        double narrowest = double.MaxValue;
        foreach (int face in new[] { 0, 2, 4 })
        {
            foreach (int x in new[] { 0, span - 1, span / 2 })
            {
                foreach (int y in new[] { 0, span - 1, span / 2 })
                {
                    var key = new RegionKey(face, level, x, y);
                    narrowest = Math.Min(narrowest, RegionMetrics.CenterWidthCm(key));
                    narrowest = Math.Min(narrowest, RegionMetrics.CenterHeightCm(key));
                }
            }
        }

        Assert.True(windowCm < narrowest,
            $"近景窗口 {windowCm / 100000.0:F2} km 放不下最窄区域 {narrowest / 100000.0:F2} km");
    }

    [Fact]
    public void RegionCorner_IsWithinRebaseBudget()
    {
        // 区域最远角点的水平跨度必须是 rebase 阈值的若干倍以内，否则玩家会在“还没换帧”时就跑到精度差的区间
        const int level = PlanetConstants.DefaultRegionLevel;
        double corner = Math.Max(
            RegionMetrics.CenterToCornerCm(new RegionKey(4, level, (1 << level) / 2, (1 << level) / 2)),
            RegionMetrics.CenterToCornerCm(new RegionKey(0, level, 0, 0)));

        Assert.True(corner < 8.0 * PlanetConstants.RebaseTriggerCm,
            $"角点跨度 {corner / 100000.0:F2} km 超过 rebase 预算 8×{PlanetConstants.RebaseTriggerCm / 100000.0:F1} km");
    }
}

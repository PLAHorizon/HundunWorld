using System;
using System.Collections.Generic;
using System.Linq;
using Horizon.Planet.Dem;
using Xunit;

namespace Horizon.Planet.Tests;

/// <summary>在指定方向上返回 NaN 的高度源：证明"坏高程会被大声拒掉"，而不是产出带洞的网格。</summary>
internal sealed class NanAtDirectionHeightSource : IHeightSource
{
    private readonly PlanetVector3 _bad;

    public NanAtDirectionHeightSource(PlanetVector3 bad) => _bad = bad;

    public int SourceVersion => 1;

    public double SampleElevationCm(in PlanetVector3 unitDirection) =>
        PlanetVector3.Dot(unitDirection, _bad) > 0.999999 ? double.NaN : 100.0;
}

public class PlanetRegionMeshTests
{
    private const long Radius = PlanetConstants.PlanetRadiusCm;
    private static readonly RegionKey Face0 = new(0, 0, 0, 0);

    private static PlanetTerrain Constant(double cm) => new(Radius, new ConstantHeightSource(cm));

    private static double RadiusOf(in PlanetVector3 p) => Math.Sqrt(p.X * p.X + p.Y * p.Y + p.Z * p.Z);

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    public void Tile_Topology_MatchesGridFormulas(int n)
    {
        var mesh = PlanetRegionMesh.BuildTile(Face0, n, Constant(50_000));
        Assert.Equal((n + 1) * (n + 1), mesh.VertexCount);
        Assert.Equal(2 * n * n, mesh.TriangleCount);
        Assert.Equal(3 * mesh.TriangleCount, mesh.Triangles.Length);

        var report = PlanetRegionMesh.Analyze(mesh);
        // 棱独立算一遍：横 n(n+1) + 竖 n(n+1) + 对角 n²，不是从同一份计数代码推出来的
        Assert.Equal(2 * n * (n + 1) + n * n, report.Edges);
        Assert.Equal(0, report.InwardWinding);
        Assert.Equal(0, report.DegenerateTriangles);
        Assert.Equal(0, report.NonFiniteVertices);
        Assert.Equal(0, report.NonManifoldEdges);
        Assert.Equal(4 * n, report.BoundaryEdges);              // 开片的边界就是外圈那 4n 条棱
        Assert.Equal(1, report.EulerCharacteristic);           // 圆盘 χ=1
        Assert.Equal(report.ManifoldEdges + report.BoundaryEdges, report.Edges);
        Assert.Equal(mesh.VertexCount, mesh.Ecef.Distinct().Count());   // tile 内不许两点落到同一厘米
    }

    [Fact]
    public void AllSixFaces_HaveOutwardWinding()
    {
        for (int face = 0; face < CubeSphere.FaceCount; face++)
        {
            var key = new RegionKey(face, 1, face % 2, (face + 1) % 2);
            var report = PlanetRegionMesh.Analyze(PlanetRegionMesh.BuildTile(key, 3, Constant(-12_000)));
            Assert.Equal(0, report.InwardWinding);
            Assert.Equal(0, report.DegenerateTriangles);
            Assert.Equal(1, report.EulerCharacteristic);
        }
    }

    [Fact]
    public void TileVertices_LandOnTheirOwnGridPointOfTheFace()
    {
        // 有坡度的源：常数高程时"用未归一化的立方体投影点"这类错会被半径判据抓住，但"uv 接错轴"不会
        var terrain = new PlanetTerrain(Radius, new AxisedHeightSource(PlanetVector3.AxisY, 5e7));
        var key = new RegionKey(4, 3, 5, 2);
        var mesh = PlanetRegionMesh.BuildTile(key, 4, terrain);
        int stride = 5;
        var (u0, u1, v0, v1) = key.UvBounds();

        foreach (var index in new[] { 0, 6, 12, 17, 24 })
        {
            int i = index % stride, j = index / stride;
            PlanetVector3 p = mesh.Positions[index];
            double len = RadiusOf(p);

            // (1) 半径就是 R + 该方向的高程：证明点真的建在表面上
            double expected = Radius + terrain.ElevationCm(p / len);
            Assert.True(Math.Abs(len - expected) < 1e-3,
                        $"顶点 {index} 半径 {len:F4} 与 R+h {expected:F4} 差 {len - expected:F4} cm");

            // (2) 反算回本面的 uv，必须正好是这个格点的 (u,v)
            Assert.True(CubeSphere.TryToUv(p / len, key.Face, out double u, out double v),
                        $"顶点 {index} 落不回面 {key.Face} 的 uv 范围");
            Assert.Equal(u0 + (u1 - u0) * i / 4, u, 12);
            Assert.Equal(v0 + (v1 - v0) * j / 4, v, 12);
        }
    }

    [Fact]
    public void BuildTile_IsDeterministic()
    {
        var key = new RegionKey(2, 3, 5, 7);
        var terrain = new PlanetTerrain(Radius, new AxisedHeightSource(PlanetVector3.AxisZ, 3e6));
        var a = PlanetRegionMesh.BuildTile(key, 5, terrain);
        var b = PlanetRegionMesh.BuildTile(key, 5, terrain);

        Assert.Equal(a.Triangles, b.Triangles);
        Assert.Equal(a.Ecef, b.Ecef);
        Assert.Equal(a.Positions.Length, b.Positions.Length);
        for (int i = 0; i < a.Positions.Length; i++)
        {
            Assert.Equal(a.Positions[i].X, b.Positions[i].X, 9);
            Assert.Equal(a.Positions[i].Y, b.Positions[i].Y, 9);
            Assert.Equal(a.Positions[i].Z, b.Positions[i].Z, 9);
        }
    }

    [Fact]
    public void RaisingElevation_RaisesVerticesAlongTheNormalByExactlyThatAmount()
    {
        var key = new RegionKey(5, 2, 1, 3);
        var low = PlanetRegionMesh.BuildTile(key, 3, Constant(0));
        const double mountain = 804_000;
        var high = PlanetRegionMesh.BuildTile(key, 3, Constant(mountain));

        Assert.Equal(low.VertexCount, high.VertexCount);
        for (int i = 0; i < low.VertexCount; i++)
        {
            double lowLen = RadiusOf(low.Positions[i]), highLen = RadiusOf(high.Positions[i]);
            Assert.True(Math.Abs((highLen - lowLen) - mountain) < 1e-6,
                        $"顶点 {i} 抬高 {highLen - lowLen:F6} cm，期望恰好 {mountain} cm");
            PlanetVector3 d1 = low.Positions[i] / lowLen, d2 = high.Positions[i] / highLen;
            Assert.True(Math.Abs(PlanetVector3.Dot(d1, d2) - 1.0) < 1e-15, "顶点被横向挪了");
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void LevelZero_IsAClosedSphere_MatchingCubeSubdivisionCounts(int n)
    {
        var mesh = PlanetRegionMesh.BuildLevel(0, n, Constant(-30_000));
        Assert.Null(mesh.Key);                                      // 合并体不属于任何单块区域

        int expectedV = 8 + 12 * (n - 1) + 6 * (n - 1) * (n - 1);   // 角 + 棱内 + 面内
        int expectedF = 12 * n * n;
        Assert.Equal(expectedV, mesh.VertexCount);                  // 焊接后剩下的必须正好是这个数
        Assert.Equal(expectedF, mesh.TriangleCount);

        var report = PlanetRegionMesh.Analyze(mesh);
        Assert.Equal(expectedF * 3 / 2, report.Edges);              // 封闭 2-流形：每条棱被两个三角形共用
        Assert.Equal(0, report.BoundaryEdges);
        Assert.Equal(0, report.NonManifoldEdges);
        Assert.Equal(0, report.InwardWinding);
        Assert.Equal(0, report.DegenerateTriangles);
        Assert.Equal(2, report.EulerCharacteristic);
        Assert.True(report.IsClosed);
    }

    [Fact]
    public void LevelOne_WithIntraFaceTileSeams_StillClosesUp()
    {
        // level 1 = 24 块 tile：除了面间接缝，还多了同一面内 tile 之间的接缝
        var mesh = PlanetRegionMesh.BuildLevel(1, 2, Constant(0));
        var report = PlanetRegionMesh.Analyze(mesh);
        Assert.Equal(24 * 8, mesh.TriangleCount);            // 24 块 tile，每块 2×2 quad × 2 三角形
        Assert.Equal(0, report.InwardWinding);               // 封闭不等于朝外：绕序要单独判
        Assert.Equal(0, report.DegenerateTriangles);
        Assert.Equal(0, report.BoundaryEdges);
        Assert.Equal(0, report.NonManifoldEdges);
        Assert.Equal(2, report.EulerCharacteristic);
        Assert.True(report.IsClosed);
        Assert.True(mesh.VertexCount < 24 * 9, $"焊接没起作用？顶点 {mesh.VertexCount} 仍接近 24×9");
    }

    [Fact]
    public void AdjacentTiles_OnAFace_ShareBoundaryVerticesExactly()
    {
        var a = new RegionKey(3, 3, 4, 6);
        Assert.True(a.TryNeighbour(1, 0, out var b));
        var meshA = PlanetRegionMesh.BuildTile(a, 4, Constant(7_777));
        var meshB = PlanetRegionMesh.BuildTile(b, 4, Constant(7_777));

        const int stride = 5;
        var aRight = new HashSet<EcefCentimeters>();
        var bLeft = new HashSet<EcefCentimeters>();
        var aLeft = new HashSet<EcefCentimeters>();
        for (int j = 0; j < stride; j++)
        {
            aRight.Add(meshA.Ecef[j * stride + (stride - 1)]);
            bLeft.Add(meshB.Ecef[j * stride]);
            aLeft.Add(meshA.Ecef[j * stride]);
        }

        // 共边必须逐厘米相同，否则拼起来就是漏光的缝
        Assert.True(aRight.SetEquals(bLeft), "相邻 tile 共边的厘米坐标不完全相同");
        Assert.Equal(stride, aRight.Count);
        Assert.Empty(aRight.Intersect(aLeft));               // 另一半：同一条边不该和自己重合
    }

    [Fact]
    public void NonFiniteElevation_ThrowsInsteadOfPoisoningTheMesh()
    {
        var terrain = new PlanetTerrain(Radius, new NanAtDirectionHeightSource(Face0.CenterDirection()));
        var error = Assert.Throws<InvalidOperationException>(
            () => PlanetRegionMesh.BuildTile(Face0, 4, terrain));
        Assert.Contains("不是有限值", error.Message);
        Assert.Contains("uv", error.Message);

        // 正向对照：同一个 tile、同一个网格，换成正常源就建得出来
        Assert.Equal(25, PlanetRegionMesh.BuildTile(Face0, 4, Constant(0)).VertexCount);
    }

    [Fact]
    public void Builders_RejectBadConfiguration()
    {
        var terrain = Constant(0);
        Assert.Throws<ArgumentOutOfRangeException>(() => PlanetRegionMesh.BuildTile(Face0, 0, terrain));
        Assert.Throws<ArgumentOutOfRangeException>(() => PlanetRegionMesh.BuildTile(Face0, -3, terrain));
        Assert.Throws<ArgumentNullException>(() => PlanetRegionMesh.BuildTile(Face0, 2, null!));
        Assert.Throws<ArgumentNullException>(() => PlanetRegionMesh.BuildLevel(0, 2, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => PlanetRegionMesh.BuildLevel(0, 0, terrain));
        Assert.Throws<ArgumentNullException>(() => PlanetRegionMesh.Analyze(null!));

        // level 8 一面就有 65536 块 tile，配 8 quad 是千万级顶点：必须在分配之前挡住
        Assert.Throws<ArgumentOutOfRangeException>(() => PlanetRegionMesh.BuildLevel(8, 8, terrain));

        // 正向对照：区域最深层级用 4 quad 建单块 tile 是合法配置
        Assert.Equal(25, PlanetRegionMesh.BuildTile(new RegionKey(5, 15, 32767, 0), 4, terrain).VertexCount);
        Assert.Equal(96 * 8, PlanetRegionMesh.BuildLevel(2, 2, terrain).TriangleCount);   // 6 面 × 16 块 × 8 三角形
    }

    [Fact]
    public void GridSpacing_MatchesMeasuredNeighbourDistanceAndScalesWithQuads()
    {
        var key = new RegionKey(0, 10, 512, 512);
        double spacing2 = PlanetRegionMesh.GridSpacingCm(key, 2);
        double spacing4 = PlanetRegionMesh.GridSpacingCm(key, 4);
        Assert.True(Math.Abs(spacing4 * 2 - spacing2) < spacing2 * 1e-9);
        Assert.Throws<ArgumentOutOfRangeException>(() => PlanetRegionMesh.GridSpacingCm(key, 0));

        // 用中心那一行的相邻顶点实测大圆距离，和 helper 对得上（helper 取较窄的轴，允许两轴差异）
        var mesh = PlanetRegionMesh.BuildTile(key, 4, Constant(0));
        const int stride = 5;
        int mid = 2 * stride + 2;
        double measured = CubeSphere.GreatCircleCm(
            mesh.Positions[mid] / RadiusOf(mesh.Positions[mid]),
            mesh.Positions[mid + 1] / RadiusOf(mesh.Positions[mid + 1]),
            Radius);
        Assert.True(measured > 0);
        Assert.True(Math.Abs(measured - spacing4) / spacing4 < 0.12,
                    $"helper 给 {spacing4 / 100000.0:F3} km，实测相邻顶点 {measured / 100000.0:F3} km");
    }

    [Fact]
    public void LodLevelPicker_ChoosesTheCoarsestEnoughSampleLevel()
    {
        var manifest = SyntheticManifest(1.0 / 240.0, 6);              // 15″ 起，0..5 级
        double cell0 = PlanetLod.CellWidthCm(manifest, 0);
        double cell5 = PlanetLod.CellWidthCm(manifest, 5);
        Assert.True(cell0 > 460_000 && cell0 < 470_000,
                    $"15″ 在 10 倍半径上应约 4.63 km，实际 {cell0 / 100000.0:F3} km");
        Assert.True(cell5 > 14_800_000 && cell5 < 14_900_000,
                    $"8′ 应约 148 km，实际 {cell5 / 100000.0:F3} km");

        // 网格间距落在两级之间 → 选"像元刚好不小于间距"的那一级（更细一级会欠采样出摩尔纹）
        for (int k = 0; k + 1 < manifest.Levels.Count; k++)
        {
            double fine = PlanetLod.CellWidthCm(manifest, k);
            double coarse = PlanetLod.CellWidthCm(manifest, k + 1);
            double spacing = (fine + coarse) * 0.5;
            Assert.Equal(k + 1, PlanetLod.DemLevelForSpacing(manifest, spacing));

            // 定义性质：选中那级的像元 ≥ 间距，而细一级的像元 < 间距
            Assert.True(coarse >= spacing);
            Assert.True(fine < spacing);
        }

        // 网格比最粗一级还粗（远景 globe）：回最粗一级，并让调用方能从 CellWidth < spacing 看出欠采样
        Assert.Equal(5, PlanetLod.DemLevelForSpacing(manifest, cell5 * 4));
        Assert.True(PlanetLod.CellWidthCm(manifest, 5) < cell5 * 4);

        // 网格比最细一级还密（近景）：选最细一级，正是"细节层由程序化补"的那一档
        Assert.Equal(0, PlanetLod.DemLevelForSpacing(manifest, cell0 / 4));

        Assert.Throws<ArgumentNullException>(() => PlanetLod.DemLevelForSpacing(null!, 1000));
        Assert.Throws<ArgumentOutOfRangeException>(() => PlanetLod.DemLevelForSpacing(manifest, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => PlanetLod.DemLevelForSpacing(manifest, double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => PlanetLod.CellWidthCm(manifest, 9));
        Assert.Throws<ArgumentException>(() => PlanetLod.DemLevelForSpacing(
            new DemManifest { StepDeg = 1.0 / 240.0, Levels = new List<DemLevel>() }, cell0));
    }

    private static DemManifest SyntheticManifest(double stepDeg, int levels)
    {
        var manifest = new DemManifest
        {
            Format = 1,
            SourceName = "synthetic",
            SourceSha256 = new string('a', 64),
            ArcSecond = stepDeg * 3600.0,
            StepDeg = stepDeg,
            GridCols = 43200,
            GridRows = 43200,
            Levels = new List<DemLevel>(),
        };
        for (int i = 0; i < levels; i++)
        {
            manifest.Levels.Add(new DemLevel
            {
                Level = i,
                Cols = 43200 >> i,
                Rows = 43200 >> i,
                File = $"level_{i:00}.bin",
            });
        }
        return manifest;
    }

    [Fact]
    public void NearPatchAndFarMesh_SitOnTheSameSurface()
    {
        // 近景（切平面 + 局部 Y）与远景（球面半径）必须落在同一张面上，否则接缝处会 z-fighting
        const double elevationCm = 804_000;                       // 8 km 山，够苛刻
        var key = new RegionKey(2, 14, 8192, 8192);
        var frame = RegionFrame.Build(key);
        var terrain = Constant(elevationCm);

        var heights = new float[PlanetPatchSampler.HeightmapLength];
        double originX = 2 * (double)PlanetConstants.TerrainPatchSizeCm;
        double originZ = -(double)PlanetConstants.TerrainPatchSizeCm;
        PlanetPatchSampler.FillPatchHeights(frame, terrain, originX, originZ, heights, null, parallel: false);

        int x = 300, z = 120;
        PlanetVector3 near = frame.GlobalFromLocalCm(new PlanetVector3(
            originX + x * PlanetConstants.TerrainUnitsPerVertex,
            heights[PlanetPatchSampler.HeightIndex(x, z)],
            originZ + z * PlanetConstants.TerrainUnitsPerVertex));

        double nearRadius = RadiusOf(near);
        double farRadius = Radius + terrain.ElevationCm(near / nearRadius);
        double mismatch = Math.Abs(nearRadius - farRadius);
        // 余量给的是 float32 局部 Y（8 km 处 ULP = 0.0625 cm）；1 cm 也正好是引擎能分辨的高差
        Assert.True(mismatch < 1.0,
                    $"近景点半径 {nearRadius:F4} 与远景球面半径 {farRadius:F4} 差 {mismatch:F4} cm");

        // 反证判据本身有效：拿"远景忘了加高程"的口径来比，差值就该是海拔本身
        Assert.True(Math.Abs(nearRadius - Radius) > elevationCm * 0.9);
    }
}

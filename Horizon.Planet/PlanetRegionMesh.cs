using System;
using System.Collections.Generic;
using System.Globalization;

namespace Horizon.Planet;

/// <summary>
/// 中/远景网格：顶点直接落在球面上（方向 × (R + 高程)），不走切平面。
/// <para>切平面是近景的约定（5 km 内曲率下垂不到 0.24 m）；区域尺度一大就必须换回球面，
/// 否则一块 150 km 的"平面"会整体浮在球面外几十米。</para>
/// </summary>
public sealed class RegionMesh
{
    internal RegionMesh(RegionKey? key, int quadsPerEdge, PlanetVector3[] positions,
                        EcefCentimeters[] ecef, int[] triangles)
    {
        Key = key;
        QuadsPerEdge = quadsPerEdge;
        Positions = positions;
        Ecef = ecef;
        Triangles = triangles;
    }

    /// <summary>单块 tile 才有值；多层/多面合并体为 null。</summary>
    public RegionKey? Key { get; }

    /// <summary>每边 quad 数（tile 用；合并体各面一致时才有效）。</summary>
    public int QuadsPerEdge { get; }

    /// <summary>ECEF 厘米（double），渲染与几何计算用。</summary>
    public PlanetVector3[] Positions { get; }

    /// <summary>同一批点的整数厘米形式：焊接与协议用的是它，double 只用于算。</summary>
    public EcefCentimeters[] Ecef { get; }

    /// <summary>三角形下标，每三个一组，绕序使外法线朝外。</summary>
    public int[] Triangles { get; }

    public int VertexCount => Positions.Length;
    public int TriangleCount => Triangles.Length / 3;
}

/// <summary>网格自检结果。数量按公式对账，不是"看起来对"。</summary>
public readonly struct MeshReport
{
    public int Vertices { get; init; }
    public int Triangles { get; init; }
    public int Edges { get; init; }
    public int BoundaryEdges { get; init; }
    public int ManifoldEdges { get; init; }
    public int NonManifoldEdges { get; init; }

    /// <summary>法线朝里（绕序反）的三角形数。</summary>
    public int InwardWinding { get; init; }

    /// <summary>三点里有重合或叉积为零的三角形数。</summary>
    public int DegenerateTriangles { get; init; }

    public int NonFiniteVertices { get; init; }

    /// <summary>V − E + F：开片=1，封闭球面=2。</summary>
    public int EulerCharacteristic => Vertices - Edges + Triangles;

    public bool IsClosed => BoundaryEdges == 0 && NonManifoldEdges == 0 && EulerCharacteristic == 2;

    public override string ToString() => string.Format(CultureInfo.InvariantCulture,
        "V={0} F={1} E={2} chi={3} boundary={4} manifold={5} nonmanifold={6} inward={7} degenerate={8} nonfinite={9}",
        Vertices, Triangles, Edges, EulerCharacteristic, BoundaryEdges, ManifoldEdges,
        NonManifoldEdges, InwardWinding, DegenerateTriangles, NonFiniteVertices);
}

/// <summary>
/// 区域网格生成与拓扑自检（§4 的中景四叉树/远景 globe 那一层）。
/// <para>三件事按可判定的方式做掉：绕序由外法线向导而不是"看着对"；跨 tile/跨面的公共点按整数厘米
/// 焊接，所以拼起来是无洞的 2-流形；V/E/F 用欧拉公式独立算一遍。</para>
/// </summary>
public static class PlanetRegionMesh
{
    /// <summary>单次构建的顶点上限（约 100 MB 位置数组），挡住把 LOD 调成"全球每面一百万格"这种配置。</summary>
    public const int MaxVertices = 4_000_000;

    /// <summary>一块 tile：uv 网格 → 球面点 → 三角形。</summary>
    public static RegionMesh BuildTile(RegionKey key, int quadsPerEdge, PlanetTerrain terrain)
    {
        if (quadsPerEdge < 1)
            throw new ArgumentOutOfRangeException(nameof(quadsPerEdge), quadsPerEdge, "每边至少 1 个 quad");
        if (terrain is null) throw new ArgumentNullException(nameof(terrain));

        int vertsPerEdge = quadsPerEdge + 1;
        CheckVertexBudget(vertsPerEdge * vertsPerEdge);
        return BuildGrid(key, quadsPerEdge, terrain);
    }

    /// <summary>
    /// 整个层级（6 面 × 2^level × 2^level 块 tile）拼成一个封闭球面，公共顶点按整数厘米焊接。
    /// <para>level 0 = 6 面，level 1 = 24 块。远景只需要 0..3，再细就该走 tile 流送而不是这里。</para>
    /// </summary>
    public static RegionMesh BuildLevel(int level, int quadsPerEdge, PlanetTerrain terrain)
    {
        if (terrain is null) throw new ArgumentNullException(nameof(terrain));
        if (quadsPerEdge < 1)
            throw new ArgumentOutOfRangeException(nameof(quadsPerEdge), quadsPerEdge, "每边至少 1 个 quad");
        int span = 1 << level;
        long tiles = (long)CubeSphere.FaceCount * span * span;
        long vertsPerTile = (long)(quadsPerEdge + 1) * (quadsPerEdge + 1);
        CheckVertexBudget(tiles * vertsPerTile);

        var positions = new List<PlanetVector3>((int)Math.Min(tiles * vertsPerTile, MaxVertices));
        var ecef = new List<EcefCentimeters>(positions.Capacity);
        var indexByKey = new Dictionary<EcefCentimeters, int>(positions.Capacity);
        var triangles = new List<int>((int)Math.Min(tiles * quadsPerEdge * quadsPerEdge * 2L, int.MaxValue));

        for (int face = 0; face < CubeSphere.FaceCount; face++)
        {
            for (int y = 0; y < span; y++)
            {
                for (int x = 0; x < span; x++)
                {
                    var key = new RegionKey(face, level, x, y);
                    var tile = BuildTile(key, quadsPerEdge, terrain);
                    var baseIndex = new int[tile.VertexCount];
                    for (int i = 0; i < tile.VertexCount; i++)
                    {
                        // 焊接按整数厘米而不是 double：相邻 tile 的公共点由不同的表达式算出，
                        // double 层可能差最后一个 ULP，落到厘米上就是同一个点
                        if (!indexByKey.TryGetValue(tile.Ecef[i], out int found))
                        {
                            found = positions.Count;
                            indexByKey.Add(tile.Ecef[i], found);
                            positions.Add(tile.Positions[i]);
                            ecef.Add(tile.Ecef[i]);
                        }
                        baseIndex[i] = found;
                    }
                    int n = quadsPerEdge;
                    for (int t = 0; t < tile.Triangles.Length; t += 3)
                    {
                        triangles.Add(baseIndex[tile.Triangles[t]]);
                        triangles.Add(baseIndex[tile.Triangles[t + 1]]);
                        triangles.Add(baseIndex[tile.Triangles[t + 2]]);
                    }
                }
            }
        }

        return new RegionMesh(null, quadsPerEdge, positions.ToArray(), ecef.ToArray(), triangles.ToArray());
    }

    private static RegionMesh BuildGrid(RegionKey key, int quadsPerEdge, PlanetTerrain terrain)
    {
        var (u0, u1, v0, v1) = key.UvBounds();
        int n = quadsPerEdge;
        int stride = n + 1;
        var positions = new PlanetVector3[stride * stride];
        var ecef = new EcefCentimeters[stride * stride];
        double radius = terrain.RadiusCm;

        for (int j = 0; j < stride; j++)
        {
            double v = v0 + (v1 - v0) * j / n;
            for (int i = 0; i < stride; i++)
            {
                double u = u0 + (u1 - u0) * i / n;
                PlanetVector3 direction = CubeSphere.ToDirection(key.Face, u, v);
                double elevationCm = terrain.ElevationCm(direction);
                if (!double.IsFinite(elevationCm))
                    throw new InvalidOperationException(
                        $"{key} uv=({u:F6},{v:F6}) 处高程不是有限值：{elevationCm}，高度源坏了");
                PlanetVector3 point = direction * (radius + elevationCm);
                int index = j * stride + i;
                positions[index] = point;
                ecef[index] = EcefCentimeters.FromVector(point);
            }
        }

        // 绕序：a=(i,j) b=(i+1,j) c=(i+1,j+1) d=(i,j+1)。uv 网格的 Cross(du,dv) 与面法线同向
        // （CubeSphere 的轴表保证 n = Cross(u,v) 外指），所以 (a,b,c)/(a,c,d) 的法线朝外。
        var triangles = new int[n * n * 6];
        int w = 0;
        for (int j = 0; j < n; j++)
        {
            for (int i = 0; i < n; i++)
            {
                int a = j * stride + i;
                int b = a + 1;
                int c = a + stride + 1;
                int d = a + stride;
                triangles[w++] = a; triangles[w++] = b; triangles[w++] = c;
                triangles[w++] = a; triangles[w++] = c; triangles[w++] = d;
            }
        }

        return new RegionMesh(key, n, positions, ecef, triangles);
    }

    private static void CheckVertexBudget(long vertices)
    {
        if (vertices > MaxVertices)
            throw new ArgumentOutOfRangeException("quadsPerEdge/level", vertices,
                $"单次网格顶点数超过上限 {MaxVertices}，LOD 配置不合理（该走流送而不是一个整层）");
    }

    /// <summary>该 tile 在这个 quad 密度下的地面网格间距（厘米），取两轴里较窄的那个（保守）。</summary>
    public static double GridSpacingCm(RegionKey key, int quadsPerEdge,
                                       long radiusCm = PlanetConstants.PlanetRadiusCm)
    {
        if (quadsPerEdge < 1) throw new ArgumentOutOfRangeException(nameof(quadsPerEdge));
        double width = Math.Min(RegionMetrics.CenterWidthCm(key, radiusCm),
                                RegionMetrics.CenterHeightCm(key, radiusCm));
        return width / quadsPerEdge;
    }

    /// <summary>
    /// 网格拓扑与几何自检。渲染层拿它做装载断言，判据也拿它——两边问的是同一个函数，
    /// 不会出现"测试里算的是一套、客户端跑的是另一套"。
    /// </summary>
    public static MeshReport Analyze(RegionMesh mesh)
    {
        if (mesh is null) throw new ArgumentNullException(nameof(mesh));

        int nonFinite = 0;
        for (int i = 0; i < mesh.Positions.Length; i++)
        {
            var p = mesh.Positions[i];
            if (!double.IsFinite(p.X) || !double.IsFinite(p.Y) || !double.IsFinite(p.Z)) nonFinite++;
        }

        int inward = 0, degenerate = 0;
        for (int t = 0; t < mesh.Triangles.Length; t += 3)
        {
            int i0 = mesh.Triangles[t], i1 = mesh.Triangles[t + 1], i2 = mesh.Triangles[t + 2];
            CheckIndex(mesh, i0); CheckIndex(mesh, i1); CheckIndex(mesh, i2);
            if (i0 == i1 || i1 == i2 || i0 == i2) { degenerate++; continue; }

            PlanetVector3 a = mesh.Positions[i0], b = mesh.Positions[i1], c = mesh.Positions[i2];
            PlanetVector3 normal = PlanetVector3.Cross(b - a, c - a);
            if (normal.LengthSquared <= 0) { degenerate++; continue; }
            // 外法线判据：法线与"从地心指向三角形中心"同向
            PlanetVector3 outward = new((a.X + b.X + c.X) / 3.0, (a.Y + b.Y + c.Y) / 3.0, (a.Z + b.Z + c.Z) / 3.0);
            if (PlanetVector3.Dot(normal, outward) <= 0) inward++;
        }

        var counts = new Dictionary<(int, int), int>();
        for (int t = 0; t < mesh.Triangles.Length; t += 3)
        {
            AddEdge(counts, mesh.Triangles[t], mesh.Triangles[t + 1]);
            AddEdge(counts, mesh.Triangles[t + 1], mesh.Triangles[t + 2]);
            AddEdge(counts, mesh.Triangles[t + 2], mesh.Triangles[t]);
        }
        int boundary = 0, manifold = 0, nonManifold = 0;
        foreach (var count in counts.Values)
        {
            if (count == 1) boundary++;
            else if (count == 2) manifold++;
            else nonManifold++;
        }

        return new MeshReport
        {
            Vertices = mesh.VertexCount,
            Triangles = mesh.TriangleCount,
            Edges = counts.Count,
            BoundaryEdges = boundary,
            ManifoldEdges = manifold,
            NonManifoldEdges = nonManifold,
            InwardWinding = inward,
            DegenerateTriangles = degenerate,
            NonFiniteVertices = nonFinite,
        };
    }

    private static void CheckIndex(RegionMesh mesh, int index)
    {
        if (index < 0 || index >= mesh.VertexCount)
            throw new InvalidOperationException($"三角形下标 {index} 越出顶点范围 0..{mesh.VertexCount - 1}");
    }

    private static void AddEdge(Dictionary<(int, int), int> counts, int a, int b)
    {
        var key = a < b ? (a, b) : (b, a);
        counts.TryGetValue(key, out int c);
        counts[key] = c + 1;
    }
}

/// <summary>
/// LOD 选型：网格密度决定该读哪一级基场。反过来（粗网格读最细一级）是纯浪费 IO，
/// 而且点采样会出摩尔纹——山会随视角忽有忽无。
/// </summary>
public static class PlanetLod
{
    /// <summary>
    /// 返回"像元宽度 ≥ 网格间距"里最细的一级（采样密度刚好配得上网格，不越采越细）。
    /// 全都比间距细时返回最粗一级，并让调用方从结果里看见它——不静默兜到某个默认档。
    /// </summary>
    public static int DemLevelForSpacing(Dem.DemManifest manifest, double gridSpacingCm,
                                         long radiusCm = PlanetConstants.PlanetRadiusCm)
    {
        if (manifest is null) throw new ArgumentNullException(nameof(manifest));
        if (!(gridSpacingCm > 0) || !double.IsFinite(gridSpacingCm))
            throw new ArgumentOutOfRangeException(nameof(gridSpacingCm), gridSpacingCm, "网格间距必须是正有限值");

        int best = int.MinValue;
        double bestCell = 0;
        int coarsest = int.MinValue;
        foreach (var entry in manifest.Levels)
        {
            int level = entry.Level;
            double cell = TerrainProfile.CellWavelengthForStepCm(radiusCm, manifest.StepDegAt(level));
            if (level > coarsest) coarsest = level;
            if (cell >= gridSpacingCm && (best == int.MinValue || cell < bestCell))
            {
                best = level;
                bestCell = cell;
            }
        }
        if (coarsest == int.MinValue)
            throw new ArgumentException("manifest 里一级数据都没有，无法选 LOD", nameof(manifest));
        return best == int.MinValue ? coarsest : best;
    }

    /// <summary>这一级的像元宽度（厘米）——用来核对上面选出来的档是否真的配得上网格。</summary>
    public static double CellWidthCm(Dem.DemManifest manifest, int level,
                                     long radiusCm = PlanetConstants.PlanetRadiusCm)
    {
        if (manifest is null) throw new ArgumentNullException(nameof(manifest));
        if (!manifest.TryGetLevel(level, out _))
            throw new ArgumentOutOfRangeException(nameof(level), level, "manifest 里没有这一级数据");
        return TerrainProfile.CellWavelengthForStepCm(radiusCm, manifest.StepDegAt(level));
    }

    /// <summary>
    /// 给一个网格间距排出"读哪一级 + 每边铺几个抽头 + 有没有罩住一个单元"。
    /// <para>抽头数向上取整：footprint 必须 ≥ 间距才算真把这一格的面积平均掉，向下取整会留下没平均的缝隙，
    /// 于是"看着像 B，实际还在摩尔纹里"。到不了就在计划里写明 <c>FullyCovered=false</c>，不装作解决。</para>
    /// </summary>
    public static Dem.LodSamplePlan PlanSampling(Dem.DemManifest manifest, double gridSpacingCm,
                                                 long radiusCm = PlanetConstants.PlanetRadiusCm)
    {
        int level = DemLevelForSpacing(manifest, gridSpacingCm, radiusCm);
        double cell = CellWidthCm(manifest, level, radiusCm);
        int taps = (int)Math.Ceiling(gridSpacingCm / cell - 1e-12);
        if (taps < 1) taps = 1;
        bool covered = true;
        if (taps > Dem.DemBoxAverageSource.MaxTapsPerEdge)
        {
            taps = Dem.DemBoxAverageSource.MaxTapsPerEdge;
            covered = false;
        }
        double footprint = taps * cell;
        return new Dem.LodSamplePlan
        {
            DemLevel = level,
            TapsPerEdge = taps,
            CellWidthCm = cell,
            FootprintCm = footprint,
            RequestedSpacingCm = gridSpacingCm,
            FullyCovered = covered && footprint >= gridSpacingCm,
        };
    }
}

using System;
using System.Collections.Generic;
using Xunit;

namespace Horizon.Planet.Tests;

/// <summary>
/// §9 给 P2 的那条"无 LOD 裂缝"。它以前只有同一层级内的拓扑判据（闭合/流形/绕序/欧拉数），
/// 而**跨级**才是裂缝真正出现的地方：粗侧走的是直线弦，细侧走的是同一根曲线的更多样本。
/// <para>
/// 这里把两件事分开量，因为它们各有各的判据：
/// ① **公共顶点必须重合**——粗边的采样参数在细边上也必须落在同一个点上。
///    注意 uv 是由两套表达式算出来的（父 `v0 + (v1-v0)*j/n` 与子 `v0' + (v1'-v0')*j'/n`），
///    double 层可能差最后几个 ULP，所以焊接用的是整数厘米——判据两头都看：
///    double 差必须极小，整数厘米必须完全相同。
/// ② **弦高就是裂缝宽度的上限**——它不为零，是几何事实，只能靠裙边/吸附去盖。
///    判据不钉一个绝对值（那会随高程函数漂），钉的是标度律：把每边 quad 数翻倍，弦高要 ÷≈4。
/// </para>
/// </summary>
public class PlanetRegionMeshLodCrackTests
{
    private const long Radius = PlanetConstants.PlanetRadiusCm;   // PlanetTerrain 的半径参数是 long 厘米

    /// <summary>带坡度的确定高程源：常数项会让整层缩放，斜项才让"父与子用不同表达式算同一个 uv"真的有风险。</summary>
    private sealed class Ramp : IHeightSource
    {
        public int SourceVersion => 1;
        public double SampleElevationCm(in PlanetVector3 dir)
            => 7_300 + 0.011 * PlanetVector3.Dot(dir, new PlanetVector3(1, 0, 0)) * Radius
                     + 0.007 * PlanetVector3.Dot(dir, new PlanetVector3(0, 1, 0)) * Radius;
    }

    /// <summary>反证用：同一套 uv 参数化，但高程整体抬 1 cm——公共顶点就该不再重合。</summary>
    private sealed class RampShifted : IHeightSource
    {
        private readonly Ramp _inner = new();
        public int SourceVersion => 1;
        public double SampleElevationCm(in PlanetVector3 dir) => _inner.SampleElevationCm(dir) + 1.0;
    }

    private static readonly PlanetTerrain Terrain = new(Radius, new Ramp());

    private const int Quads = 8;          // 必须是偶数：子 tile 的边只覆盖父边的一半
    private const int Stride = Quads + 1;

    private static PlanetVector3 RightEdgeAt(RegionMesh m, int rowIndex) =>
        m.Positions[rowIndex * Stride + Quads];

    private static PlanetVector3 TopEdgeAt(RegionMesh m, int colIndex) =>
        m.Positions[Quads * Stride + colIndex];

    [Fact]
    public void 父tile右边缘的采样点在右子tile上必须落在同一个位置()
    {
        var parent = new RegionKey(2, 3, 3, 5);
        var (u0, u1, v0, v1) = parent.UvBounds();
        Assert.Equal(2.0 / 8.0, u1 - u0, 9);           // level 3 → 一面 8×8，每格 1/4

        var p = PlanetRegionMesh.BuildTile(parent, Quads, Terrain);
        var lower = PlanetRegionMesh.BuildTile(new RegionKey(2, 4, 2 * parent.X + 1, 2 * parent.Y), Quads, Terrain);
        var upper = PlanetRegionMesh.BuildTile(new RegionKey(2, 4, 2 * parent.X + 1, 2 * parent.Y + 1), Quads, Terrain);

        double worstDouble = 0;
        int mismatchedCm = 0;
        for (int j = 0; j <= Quads; j++)
        {
            // 父边第 j 个点在 v = v0 + (v1-v0)*j/Quads；
            // 下半子边覆盖 v0..中点，其第 k 个点在 v0 + (v1'-v0')*k/Quads，步长是父的一半 ⇒ k = 2j
            var child = j <= Quads / 2 ? lower : upper;
            int k = j <= Quads / 2 ? 2 * j : 2 * (j - Quads / 2);
            var a = RightEdgeAt(p, j);
            var b = RightEdgeAt(child, k);
            double d = Dist(a, b);
            worstDouble = Math.Max(worstDouble, d);
            if (!Ecef(p, j, Quads).Equals(EcefOf(child, k, Quads))) mismatchedCm++;
        }

        // 焊接判据：整数厘米必须完全相同，一个都不能差
        Assert.Equal(0, mismatchedCm);
        // double 层允许最后一两个 ULP（两套表达式），但必须远小于 1 mm——否则"落到厘米就是同一个点"是运气
        Assert.True(worstDouble < 1e-3,
                    $"跨级公共点在 double 层差了 {worstDouble:0.####} cm，整数厘米焊接的前提不成立");
    }

    [Fact]
    public void 父tile上边缘的采样点在上方子tile上同样重合()
    {
        var parent = new RegionKey(0, 2, 1, 1);
        var p = PlanetRegionMesh.BuildTile(parent, Quads, Terrain);
        var left = PlanetRegionMesh.BuildTile(new RegionKey(0, 3, 2 * parent.X, 2 * parent.Y + 1), Quads, Terrain);
        var right = PlanetRegionMesh.BuildTile(new RegionKey(0, 3, 2 * parent.X + 1, 2 * parent.Y + 1), Quads, Terrain);

        int mismatched = 0;
        double worst = 0;
        for (int i = 0; i <= Quads; i++)
        {
            var child = i <= Quads / 2 ? left : right;
            int k = i <= Quads / 2 ? 2 * i : 2 * (i - Quads / 2);
            var a = TopEdgeAt(p, i);
            var b = TopEdgeAt(child, k);
            worst = Math.Max(worst, Dist(a, b));
            if (!Ecef(p, Quads, i).Equals(EcefOf(child, Quads, k))) mismatched++;
        }
        Assert.Equal(0, mismatched);
        Assert.True(worst < 1e-3, $"上边缘跨级点 double 层最大差 {worst:0.####} cm");
    }

    [Fact]
    public void 反证_子tile高程差一厘米就必须被这条判据抓到()
    {
        // 这条不是"多测一个分支"，是证明上面两条不是空转：同一个 uv、同一套参数化，
        // 只把高度源抬 1 cm，整数厘米就必须对不上。
        var parent = new RegionKey(2, 3, 3, 5);
        var p = PlanetRegionMesh.BuildTile(parent, Quads, Terrain);
        var shifted = new PlanetTerrain(Radius, new RampShifted());
        var lower = PlanetRegionMesh.BuildTile(new RegionKey(2, 4, 2 * parent.X + 1, 2 * parent.Y), Quads, shifted);

        int mismatched = 0;
        for (int j = 0; j <= Quads / 2; j++)
        {
            if (!Ecef(p, j, Quads).Equals(EcefOf(lower, 2 * j, Quads))) mismatched++;
        }
        Assert.True(mismatched > 0,
                    "换了高度源却仍然逐厘米相同 ⇒ 那两条重合判据其实在空转");
    }

    [Fact]
    public void 弦高是裂缝宽度上限_且随每边quad数翻倍而四分()
    {
        // 同一条边、两种分辨率：细侧顶点到粗侧直线弦的最大距离就是"跨级缝隙"的量级。
        // 不钉绝对值（它随高程函数与半径漂），钉标度律：quad 翻倍 ⇒ 弦高 ÷≈4。
        var key = new RegionKey(1, 3, 3, 4);
        double coarse = Sag(key, Quads, Quads * 2);
        double fine = Sag(key, Quads * 2, Quads * 4);
        Assert.True(coarse > 0 && fine > 0, "弦高必须为正，否则这条边是直线，判据没意义");
        double ratio = coarse / fine;
        Assert.True(ratio > 3.4 && ratio < 4.6,
                    $"quad 数翻倍后弦高只从 {coarse:0.####} 降到 {fine:0.####} cm（比值 {ratio:0.###}），" +
                    "不是 4 倍标度 ⇒ 裂缝宽度的预测公式不能这么用");
    }

    /// <summary>取 tile 的右边曲线：粗侧每段直线弦，细侧取其中点，量最大偏离（cm）。</summary>
    private static double Sag(RegionKey key, int coarseQuads, int fineQuads)
    {
        var coarseMesh = PlanetRegionMesh.BuildTile(key, coarseQuads, Terrain);
        var fineMesh = PlanetRegionMesh.BuildTile(key, fineQuads, Terrain);
        double worst = 0;
        for (int seg = 0; seg < coarseQuads; seg++)
        {
            var a = coarseMesh.Positions[seg * (coarseQuads + 1) + coarseQuads];
            var b = coarseMesh.Positions[(seg + 1) * (coarseQuads + 1) + coarseQuads];
            // 细侧在这段弦的中点处的样本
            int mid = (int)(((double)seg + 0.5) * fineQuads / coarseQuads);
            var m = fineMesh.Positions[mid * (fineQuads + 1) + fineQuads];
            worst = Math.Max(worst, PointSegmentCm(m, a, b));
        }
        return worst;
    }

    private static double PointSegmentCm(PlanetVector3 p, PlanetVector3 a, PlanetVector3 b)
    {
        var ab = b - a;
        double len2 = PlanetVector3.Dot(ab, ab);
        if (len2 <= 0) return Dist(p, a);
        double t = PlanetVector3.Dot(p - a, ab) / len2;
        if (t < 0) t = 0;
        if (t > 1) t = 1;
        return Dist(p, a + ab * t);
    }

    private static EcefCentimeters Ecef(RegionMesh m, int row, int col) =>
        m.Ecef[row * Stride + col];

    private static EcefCentimeters EcefOf(RegionMesh m, int row, int col) =>
        m.Ecef[row * (m.QuadsPerEdge + 1) + col];

    private static double Dist(in PlanetVector3 a, in PlanetVector3 b)
    {
        double x = a.X - b.X, y = a.Y - b.Y, z = a.Z - b.Z;
        return Math.Sqrt(x * x + y * y + z * z);
    }
}

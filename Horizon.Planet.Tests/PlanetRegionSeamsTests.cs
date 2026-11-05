using System;
using Xunit;

namespace Horizon.Planet.Tests;

/// <summary>
/// 跨级缝隙的缝合件判据（§13.29 / §13.30）。核心是三件事：
/// 裙边不许自己制造新缝（顶边逐位相同）、深度必须盖过<b>实测</b>弦高、壁必须朝外。
/// </summary>
public class PlanetRegionSeamsTests
{
    private const long Radius = PlanetConstants.PlanetRadiusCm;

    private sealed class Ramp : IHeightSource
    {
        public int SourceVersion => 1;
        public double SampleElevationCm(in PlanetVector3 d)
            => 7_300 + 0.011 * PlanetVector3.Dot(d, PlanetVector3.AxisX) * Radius
                     + 0.007 * PlanetVector3.Dot(d, PlanetVector3.AxisY) * Radius;
    }

    private static readonly PlanetTerrain Terrain = new(Radius, new Ramp());

    private static double Dist(in PlanetVector3 a, in PlanetVector3 b)
    {
        double x = a.X - b.X, y = a.Y - b.Y, z = a.Z - b.Z;
        return Math.Sqrt(x * x + y * y + z * z);
    }

    /// <summary>同一条边、两种分辨率：细侧样本到粗侧直线弦的最大偏离（cm）——就是缝隙宽度。</summary>
    private static double MeasuredSagCm(RegionKey key, int coarse, int fine)
    {
        var cm = PlanetRegionMesh.BuildTile(key, coarse, Terrain);
        var fm = PlanetRegionMesh.BuildTile(key, fine, Terrain);
        double worst = 0;
        for (int seg = 0; seg < coarse; seg++)
        {
            var a = cm.Positions[seg * (coarse + 1) + coarse];
            var b = cm.Positions[(seg + 1) * (coarse + 1) + coarse];
            int mid = (int)(((double)seg + 0.5) * fine / coarse);
            var m = fm.Positions[mid * (fine + 1) + fine];
            var ab = b - a;
            double l2 = PlanetVector3.Dot(ab, ab);
            double t = l2 <= 0 ? 0 : PlanetVector3.Dot(m - a, ab) / l2;
            if (t < 0) t = 0;
            if (t > 1) t = 1;
            worst = Math.Max(worst, Dist(m, a + ab * t));
        }
        return worst;
    }

    /// <summary>
    /// 山脊尺度的起伏：波长 20 km、振幅 2 km——这才是真实 DEM 里把步距顶上去的那一项。
    /// <para>标定过：60 km 波长时实测 2.046 km（还没超过 2.916 km 的深度），20 km 波长时 5.537 km
    /// （1.90 倍）。真实 GEBCO 的网格是 15″≈463 m，比这里锐得多，所以这条反例是**保守**的。</para>
    /// <para>试过用整体二次面（哪怕振幅给到上千 km）做反例，实测步距只从 1.885 km 涨到 2.253 km：
    /// 弦只有 49 km 长，缓变地形的二阶导在里面几乎不动。真正要紧的是**短波长**的弯曲
    /// （海岸线、断崖、DEM 网格角点），它才是纯球面弦高公式看不见的项。</para>
    /// </summary>
    private sealed class Ridge : IHeightSource
    {
        private const double AmpCm = 200_000;          // 2 km
        private const double WaveCm = 2_000_000;       // 20 km
        public int SourceVersion => 1;
        public double SampleElevationCm(in PlanetVector3 d)
        {
            double dot = PlanetVector3.Dot(d, PlanetVector3.AxisX);
            if (dot > 1) dot = 1; if (dot < -1) dot = -1;
            double arc = Radius * Math.Acos(dot);
            return AmpCm * Math.Cos(2 * Math.PI * arc / WaveCm);
        }
    }

    /// <summary>线性坡度：一阶项让弦整体倾斜，但不产生"弦与面之间"的偏离。</summary>
    private sealed class Tilt : IHeightSource
    {
        public int SourceVersion => 1;
        public double SampleElevationCm(in PlanetVector3 d) => 0.01 * Radius * d.X;
    }

    [Fact]
    public void 实测步距只收二阶弯曲_线性坡度不该冒充地形起伏()
    {
        var key = new RegionKey(1, 4, 7, 9);
        double curvature = PlanetRegionSeams.ChordSagCm(key, 8, Radius);

        var flat = new PlanetTerrain(Radius, new Flat());
        var tilt = new PlanetTerrain(Radius, new Tilt());
        var dome = new PlanetTerrain(Radius, new Ridge());

        double stepFlat = PlanetRegionSeams.MeasuredStepCm(key, 8, flat, RegionEdge.UPlus);
        double stepTilt = PlanetRegionSeams.MeasuredStepCm(key, 8, tilt, RegionEdge.UPlus);
        double stepDome = PlanetRegionSeams.MeasuredStepCm(key, 8, dome, RegionEdge.UPlus);

        // 纯球面：实测与解析必须贴在一起（差在 uv 弧长放大那几 %）
        Assert.True(Math.Abs(stepFlat - curvature) / curvature < 0.10,
                    $"纯球面实测 {stepFlat:0} 与解析 {curvature:0} 差太多，量法有问题");
        // 线性坡度不该让步距变大（一阶项整条弦一起斜）
        Assert.True(stepTilt < stepFlat * 1.35,
                    $"线性坡度把步距抬到 {stepTilt:0}（纯球面 {stepFlat:0}）⇒ 量到的是坡度而不是弯曲");
        // 地形自己在弯，必须比纯球面大
        Assert.True(stepDome > stepFlat,
                    $"二次起伏下实测 {stepDome:0} 不比纯球面 {stepFlat:0} 大 ⇒ 这条判据抓不到地形的弯曲");
    }

    [Fact]
    public void 按真实表面定深度才盖得住_纯弯曲公式盖不住()
    {
        var key = new RegionKey(1, 4, 7, 9);
        var dome = new PlanetTerrain(Radius, new Ridge());

        double measured = PlanetRegionSeams.MeasuredStepCm(key, 8, dome, RegionEdge.UPlus);
        double curvatureOnly = PlanetRegionSeams.SkirtDepthCm(key, 8, Radius);
        double forEdge = PlanetRegionSeams.SkirtDepthForEdgeCm(key, 8, dome, RegionEdge.UPlus);

        Assert.True(measured > curvatureOnly,
                    $"这块地形上实测步距 {measured:0} 没超过纯弯曲深度 {curvatureOnly:0} ⇒ 反例不成立，这条判据白写");
        Assert.False(PlanetRegionSeams.CoversSag(curvatureOnly, measured),
                     "纯弯曲深度居然盖住了实测步距 ⇒ 老公式没问题，那就不该引入按边定深度");
        Assert.True(PlanetRegionSeams.CoversSag(forEdge, measured),
                    $"按边定的深度 {forEdge:0} 还盖不住实测 {measured:0} ⇒ 取大之后再乘系数这一步写错了");
    }

    [Fact]
    public void 实测步距的参数不合法时当场拒绝()
    {
        var key = new RegionKey(1, 4, 7, 9);
        Assert.Throws<ArgumentNullException>(() =>
            PlanetRegionSeams.MeasuredStepCm(key, 8, null!, RegionEdge.UPlus));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PlanetRegionSeams.MeasuredStepCm(key, 8, Terrain, RegionEdge.UPlus, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PlanetRegionSeams.SkirtDepthForEdgeCm(key, 8, Terrain, RegionEdge.UPlus, 0.5));
    }

    /// <summary>常数 0 高程：用来把"纯球面"这一档单独拎出来。</summary>
    private sealed class Flat : IHeightSource
    {
        public int SourceVersion => 1;
        public double SampleElevationCm(in PlanetVector3 d) => 0.0;
    }

    [Fact]
    public void 裙边顶边必须与粗tile的边界顶点逐位相同()
    {
        // 顶边一旦与粗 tile 的边界差一点点，裙边自己就成了一条新缝——那比原来的缝更糟。
        var key = new RegionKey(1, 3, 3, 4);
        const int n = 8;
        var tile = PlanetRegionMesh.BuildTile(key, n, Terrain);
        var skirt = PlanetRegionSeams.BuildSkirt(key, n, Terrain, RegionEdge.UPlus, 50_000);

        for (int j = 0; j <= n; j++)
        {
            var a = tile.Positions[j * (n + 1) + n];
            var b = skirt.Positions[j];
            Assert.True(Dist(a, b) == 0.0,
                        $"第 {j} 个顶边点与粗 tile 差了 {Dist(a, b):0.######} cm，裙边自己制造了新缝");
            Assert.Equal(tile.Ecef[j * (n + 1) + n], skirt.Ecef[j]);
        }

        // 上边缘同理（换一条边，走另一套 uv 表达式）
        var top = PlanetRegionSeams.BuildSkirt(key, n, Terrain, RegionEdge.VPlus, 50_000);
        for (int i = 0; i <= n; i++)
        {
            var a = tile.Positions[n * (n + 1) + i];
            Assert.True(Dist(a, top.Positions[i]) == 0.0, $"VPlus 第 {i} 个顶边点不逐位相同");
        }
    }

    [Fact]
    public void 裙边深度必须盖过实测弦高()
    {
        var key = new RegionKey(1, 4, 7, 9);
        double measured = MeasuredSagCm(key, 8, 16);
        double depth = PlanetRegionSeams.SkirtDepthCm(key, 8, Radius);
        Assert.True(measured > 0, "弦高为 0 ⇒ 这条边是直的，判据没意义");
        Assert.True(PlanetRegionSeams.CoversSag(depth, measured),
                    $"解析深度 {depth:0.#} cm 盖不住实测弦高 {measured:0.#} cm ⇒ 深度公式不能这么用");

        // 反证：深度只给一半时谓词必须说"盖不住"，否则 CoversSag 是空转
        Assert.False(PlanetRegionSeams.CoversSag(measured / 2.0, measured));
        // 而且默认安全系数确实留了余量，不是刚好卡平
        Assert.True(depth > measured * 1.1,
                    $"深度 {depth:0.#} 与实测 {measured:0.#} 只差一点，面边缘的弧长放大会把它顶穿");
    }

    /// <summary>
    /// 裙边垒在哪一侧不是风格问题：洞朝哪边张开，墙就得往哪边垒，垒反了整面墙都在粗面以下，
    /// 掠射视线从它上方直接穿过去——像素台（§13.36）实测过垒反的臂，缝隙与不挂裙边逐像素相同。
    /// <para>两头都要钉：① 洞确实在外侧（细 tile 边界半径 &gt; 粗 tile 边界弦的半径）；
    /// ② 墙往同一侧长（底边半径 = 顶边半径 + 深度，不是减）。只钉 ② 的话，把 ① 也反过来写就还是绿的。</para>
    /// </summary>
    [Fact]
    public void 裙边必须朝洞张开的那一侧_也就是远离球心()
    {
        var coarse = new RegionKey(2, 3, 3, 5);
        var fine = new RegionKey(2, 4, 2 * (coarse.X + 1), 2 * coarse.Y);
        const int n = 2, nf = 8;
        double depth = PlanetRegionSeams.SkirtDepthCm(coarse, n, Radius);

        var cm = PlanetRegionMesh.BuildTile(coarse, n, Terrain);
        var fm = PlanetRegionMesh.BuildTile(fine, nf, Terrain);
        var skirt = PlanetRegionSeams.BuildSkirt(coarse, n, Terrain, RegionEdge.UPlus, depth);

        // ① 洞在外侧：细边界顶点到粗边界弦的垂足，半径必须更大
        int cStride = n + 1, fStride = nf + 1;
        int outwardHoles = 0;
        for (int k = 0; k <= nf; k++)
        {
            var p = fm.Positions[k * fStride];
            for (int j = 0; j < n; j++)
            {
                var a = cm.Positions[j * cStride + n];
                var b = cm.Positions[(j + 1) * cStride + n];
                var ab = b - a;
                double l2 = PlanetVector3.Dot(ab, ab);
                if (l2 <= 0) continue;
                double t = PlanetVector3.Dot(p - a, ab) / l2;
                if (t < 0 || t > 1) continue;
                if (p.Length > (a + ab * t).Length) outwardHoles++;
            }
        }
        // 两端的细边界顶点与粗边界弦的端点是同一个点（半径相等），所以"在外侧"的只可能是内部样本
        Assert.Equal(nf - 1, outwardHoles);
        Assert.True(Math.Abs(fm.Positions[0].Length - cm.Positions[n].Length) < 1.0,
                    $"共边的起点半径都对不上（{fm.Positions[0].Length:0} vs {cm.Positions[n].Length:0}），这条边根本不是共边");
        // 细 tile 只覆盖粗边的一半：它的另一端落在粗边的**中间**顶点上，不是粗边的角上
        double endRadius = fm.Positions[nf * fStride].Length;
        bool endOnCoarseVertex = false;
        for (int j = 0; j <= n; j++)
            if (Math.Abs(endRadius - cm.Positions[j * cStride + n].Length) < 1.0) endOnCoarseVertex = true;
        Assert.True(endOnCoarseVertex, $"细边的另一端半径 {endRadius:0} 不在粗边任何一个顶点上，这条边不是共边");

        // ② 墙往同一侧长
        for (int i = 0; i <= n; i++)
        {
            double top = skirt.Positions[i].Length;
            double bottom = skirt.Positions[n + 1 + i].Length;
            Assert.True(bottom > top, $"第 {i} 段裙边往球心垒了：顶边半径 {top:0}，底边 {bottom:0}");
            Assert.Equal(top + depth, bottom, 1);              // 沿径向平移，半径差就是深度
        }
    }

    [Fact]
    public void 四条边的可见面都朝向看得见洞的那一侧()
    {
        var key = new RegionKey(2, 3, 3, 5);
        const int n = 8;
        foreach (RegionEdge edge in new[] { RegionEdge.UPlus, RegionEdge.UMinus,
                                            RegionEdge.VPlus, RegionEdge.VMinus })
        {
            var skirt = PlanetRegionSeams.BuildSkirt(key, n, Terrain, edge, 100_000);
            var (u0, u1, v0, v1) = key.UvBounds();
            var centerDir = CubeSphere.ToDirection(key.Face, (u0 + u1) / 2.0, (v0 + v1) / 2.0);
            var center = centerDir * (double)Radius;

            // 法线必须按**实际三角形绕序**算，不能拿固定三点算：
            // 顶点位置与绕序无关，第一版就是在这里把夹具写错，于是"翻绕序"这件事根本测不到。
            var a = skirt.Positions[skirt.Triangles[0]];
            var b = skirt.Positions[skirt.Triangles[1]];
            var c = skirt.Positions[skirt.Triangles[2]];
            var normal = PlanetVector3.Cross(b - a, c - a);
            var mid = (skirt.Positions[0] + skirt.Positions[n + 2]) * 0.5;
            double dot = PlanetVector3.Dot(normal, mid - center);
            // 朝向不是美学问题：引擎剔背面。像素台（§13.36）上深度/位置/顶点全同，
            // 只把绕序反过来，缝隙暗像素就从 5933 掉到 0——所以可见面必须朝"低的那一侧"，
            // 也就是本 tile 内侧（洞是从这边看见的）。
            Assert.True(dot < 0, $"{edge} 的壁法线指向邻居那侧（dot={dot:0.###}）⇒ 从粗面这边看它是背面，等于没有墙");

            // 深度就是顶边到底边的距离（沿远离球心方向，见上一条判据）
            double depth = Dist(skirt.Positions[0], skirt.Positions[n + 1]);
            Assert.True(Math.Abs(depth - 100_000) < 1.0,
                        $"{edge} 的底边偏移 {depth:0.###} cm，不等于要求的 100000 cm");
        }
    }

    [Fact]
    public void 只有更细的邻居才需要裙边()
    {
        var key = new RegionKey(2, 4, 5, 6);
        Assert.True(PlanetRegionSeams.NeedsSkirt(key, new RegionKey(2, 5, 10, 12)));      // 子级
        Assert.False(PlanetRegionSeams.NeedsSkirt(key, new RegionKey(2, 4, 5, 6)));       // 自己
        Assert.False(PlanetRegionSeams.NeedsSkirt(key, new RegionKey(2, 4, 6, 6)));       // 同级邻居
        Assert.False(PlanetRegionSeams.NeedsSkirt(key, new RegionKey(2, 3, 2, 3)));       // 更粗的邻居
        Assert.False(PlanetRegionSeams.NeedsSkirt(key, new RegionKey(3, 5, 10, 12)));     // 跨面：这里不掺和
    }

    [Fact]
    public void 像素预算公式要能复现文档那张表()
    {
        // §13.30：L6 q16 的弦高约 30 m，亚像素所需视距约 31.3 km
        var key = new RegionKey(0, 6, 1, 1);
        double sagCm = PlanetRegionSeams.ChordSagCm(key, 16, Radius);
        Assert.True(Math.Abs(sagCm - 3_040) / 3_040 < 0.02,
                    $"L6 q16 弦高 {sagCm:0.#} cm 与文档记的约 30 m 对不上");

        double dist = PlanetRegionSeams.SubPixelDistanceCm(key, 16, Radius, pixels: 1.0);
        Assert.True(Math.Abs(dist - 3.13e6) / 3.13e6 < 0.02,
                    $"亚像素视距 {dist / 1e5:0.##} km 与文档记的 31.3 km 对不上");

        // 反过来：在那个视距上，允许的弦高就等于该档弦高（两条函数互逆）
        double allowed = PlanetRegionSeams.AllowedSagCm(dist, 1.0);
        Assert.True(Math.Abs(allowed - sagCm) / sagCm < 0.01,
                    $"互逆性不成立：allowed={allowed:0.###} vs sag={sagCm:0.###}");

        // 1080p / 60° 下 1 px 的张角约 9.696e-4 rad ⇒ 视距 100 km 允许约 97 m
        double at100km = PlanetRegionSeams.AllowedSagCm(1e7, 1.0);
        Assert.True(Math.Abs(at100km - 9_700) / 9_700 < 0.02, $"100 km 处允许 {at100km:0.#} cm");
    }

    [Fact]
    public void 参数不合法时当场拒绝()
    {
        var key = new RegionKey(0, 3, 1, 1);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PlanetRegionSeams.ChordSagCm(key, 0, Radius));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PlanetRegionSeams.ChordSagCm(key, 8, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PlanetRegionSeams.SkirtDepthCm(key, 8, Radius, safetyFactor: 0.5));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PlanetRegionSeams.AllowedSagCm(0, 1.0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PlanetRegionSeams.AllowedSagCm(1e6, 1.0, verticalFovDeg: 200));
        Assert.Throws<ArgumentNullException>(() =>
            PlanetRegionSeams.BuildSkirt(key, 8, null!, RegionEdge.UPlus, 1000));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PlanetRegionSeams.BuildSkirt(key, 8, Terrain, RegionEdge.UPlus, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PlanetRegionSeams.BuildSkirt(key, 0, Terrain, RegionEdge.UPlus, 1000));

        // 深度顶到星球半径 ⇒ 底边会穿过球心，几何没有意义。这种 LOD 配错必须响，不能悄悄画出一坨内翻的壁。
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PlanetRegionSeams.BuildSkirt(key, 8, Terrain, RegionEdge.UPlus, Radius));
        // 线画在"深度 >= 半径"这一处：0.9 倍半径仍然合法（底边还在球内），等于半径就穿心。
        Assert.NotNull(PlanetRegionSeams.BuildSkirt(key, 8, Terrain, RegionEdge.UPlus, Radius * 0.9));
    }
}

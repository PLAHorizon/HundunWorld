using System;
using System.Collections.Generic;

namespace Horizon.Planet;

/// <summary>
/// 跨级缝隙的缝合件（§13.29 / §13.30）。
/// <para>
/// 已经量清的两件事决定了这里只有一种做法可用：
/// ① 同级相邻 tile 的公共顶点是<b>逐位相同</b>的（实测 double 偏离 0 cm）⇒ 同级之间不需要任何处理；
/// ② 跨级时粗侧走直线弦、细侧走同一根曲线的更多样本，缝隙宽度就是粗侧的<b>弦高</b>，
///    在 10× 半径的星球上是公里级（L3/q8 是 7.78 km），而且"加密"买不动它
///    （要把弦高压到 1 m 需要全球约 1.9 亿 quad）。
/// </para>
/// <para>
/// 所以这里给的是<b>裙边</b>：沿粗 tile 的这条共享边，把边界顶点朝球心方向再拉出一圈竖直壁，
/// 深度 ≥ 该档弦高。细侧鼓出来的那部分不会与粗侧共面，但从外面看过去视线会被这圈壁挡住，
/// 缝隙不再"看穿"。裙边只加在<b>真的有更细邻居</b>的那条边上，同级边不加（白涨顶点数）。
/// </para>
/// <para>
/// 允许多大的缝不该用厘米回答，该用<b>像素</b>回答：
/// <see cref="AllowedSagCm"/> 把"视距 + 允许几像素"换成允许弦高，反过来就能定每级的最低视距。
/// </para>
/// </summary>
public static class PlanetRegionSeams
{
    /// <summary>
    /// 一条 quad 边的弦高（cm）：粗侧直线弦与它所属圆弧之间的最大偏离，取 <c>R·Δθ²/8</c>。
    /// <para>这是解析式而不是实测拟合，但 §13.29 里两者对纯球面差 0.07%、对带起伏的面差 2.2%，
    /// 而且实测标度律比值是 3.998（二次律）——所以用它做裙边深度的依据是站得住的。
    /// 面边缘（uv 角大处）实际弧长比面心大，故这里给的是<b>下界</b>，
    /// 需要保险时用 <see cref="SkirtDepthCm"/> 的安全系数。</para>
    /// </summary>
    public static double ChordSagCm(RegionKey key, int quadsPerEdge, double radiusCm)
    {
        if (quadsPerEdge < 1)
            throw new ArgumentOutOfRangeException(nameof(quadsPerEdge), quadsPerEdge, "每边至少 1 个 quad");
        if (radiusCm <= 0) throw new ArgumentOutOfRangeException(nameof(radiusCm), radiusCm, "半径必须为正");
        var (u0, u1, v0, _) = key.UvBounds();
        // uv 步长在面心处≈弧度；面边缘更大，所以按 uv 算出来的是保守下界之外的"偏小值"，
        // 因此 SkirtDepthCm 再乘一个系数，而不是直接用这里的数当深度。
        double step = (u1 - u0) / quadsPerEdge;
        return radiusCm * step * step / 8.0;
    }

    /// <summary>
    /// 裙边深度（cm）：只算<b>球面弯曲</b>那一项的弦高 × <paramref name="safetyFactor"/>。
    /// <para><b>这一条不够用，别拿它当地形的深度</b>：真实 GEBCO 上 L4/每边 8 quad 抽 60 块共边量，
    /// 实测步距有 2/60 块超过这个深度（最大 1.42 倍，4.13 km vs 2.92 km）——因为地形自己也在弯，
    /// 而 <see cref="ChordSagCm"/> 里只有半径。要按真实表面定深度用
    /// <see cref="SkirtDepthForEdgeCm"/>，它把实测步距取进来。</para>
    /// </summary>
    public static double SkirtDepthCm(RegionKey key, int quadsPerEdge, double radiusCm, double safetyFactor = 1.5)
    {
        if (safetyFactor < 1.0)
            throw new ArgumentOutOfRangeException(nameof(safetyFactor), safetyFactor, "安全系数不能小于 1");
        return ChordSagCm(key, quadsPerEdge, radiusCm) * safetyFactor;
    }

    /// <summary>
    /// 沿粗 tile 某条边<b>实测</b>的步距（cm）：把这条边按 <paramref name="subdivisions"/> 倍细分取点
    /// （就是更细那一侧的顶点列），求每个点到粗边直线弦的最大垂直偏离。
    /// <para>与 <see cref="ChordSagCm"/> 的区别不是精度而是**物理**：解析式只算球面弯曲，
    /// 这里算的是"粗弦与真实表面之间差多少"，起伏自己的弯曲一并进来。
    /// 线性坡度不贡献（弦也跟着斜），贡献的是地形的二阶弯曲——所以这条必须在真实高程源上量。</para>
    /// </summary>
    public static double MeasuredStepCm(RegionKey coarseKey, int quadsPerEdge, PlanetTerrain terrain,
                                        RegionEdge edge, int subdivisions = 2)
    {
        if (terrain is null) throw new ArgumentNullException(nameof(terrain));
        if (quadsPerEdge < 1)
            throw new ArgumentOutOfRangeException(nameof(quadsPerEdge), quadsPerEdge, "每边至少 1 个 quad");
        if (subdivisions < 2)
            throw new ArgumentOutOfRangeException(nameof(subdivisions), subdivisions,
                "细分倍数至少 2，否则『更细的一侧』不存在，量到的步距恒为 0");

        int n = quadsPerEdge;
        var coarse = BoundaryPoints(coarseKey, n, terrain, edge);
        double radius = terrain.RadiusCm;
        int m = n * subdivisions;
        double worst = 0;
        for (int k = 0; k <= m; k++)
        {
            double t = k / (double)m;
            var p = EdgePoint(coarseKey, terrain, edge, t);
            int seg = (int)(t * n);
            if (seg > n - 1) seg = n - 1;
            var a = coarse[seg];
            var b = coarse[seg + 1];
            var ab = b - a;
            double l2 = PlanetVector3.Dot(ab, ab);
            if (l2 <= 0) continue;
            double s = PlanetVector3.Dot(p - a, ab) / l2;
            if (s < 0) s = 0;
            if (s > 1) s = 1;
            var foot = a + ab * s;
            double dx = p.X - foot.X, dy = p.Y - foot.Y, dz = p.Z - foot.Z;
            double d = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (d > worst) worst = d;
        }
        return worst;
    }

    /// <summary>
    /// 这条边真正需要的裙边深度：取"球面弯曲的弦高"与"实测步距"里更大的那个，再乘安全系数。
    /// <para>为什么不能只用实测：粗块这边只有 n+1 个边界点，实测列取的是 level+1 的顶点格；
    /// 更深的邻居（level+2、+3）的步距会比它大，弯曲项在那里仍是下界的一部分，两者取大才稳。</para>
    /// </summary>
    public static double SkirtDepthForEdgeCm(RegionKey coarseKey, int quadsPerEdge, PlanetTerrain terrain,
                                             RegionEdge edge, double safetyFactor = 1.5, int subdivisions = 2)
    {
        if (safetyFactor < 1.0)
            throw new ArgumentOutOfRangeException(nameof(safetyFactor), safetyFactor, "安全系数不能小于 1");
        double measured = MeasuredStepCm(coarseKey, quadsPerEdge, terrain, edge, subdivisions);
        double curvature = ChordSagCm(coarseKey, quadsPerEdge, terrain.RadiusCm);
        return Math.Max(measured, curvature) * safetyFactor;
    }

    /// <summary>边上参数 t∈[0,1] 处的真实表面点（含高程）——<see cref="BoundaryPoints"/> 与实测步距共用一套参数化。</summary>
    private static PlanetVector3 EdgePoint(RegionKey key, PlanetTerrain terrain, RegionEdge edge, double t)
    {
        var (u0, u1, v0, v1) = key.UvBounds();
        double u, v;
        switch (edge)
        {
            case RegionEdge.UPlus: u = u1; v = v0 + (v1 - v0) * t; break;
            case RegionEdge.UMinus: u = u0; v = v0 + (v1 - v0) * t; break;
            case RegionEdge.VPlus: v = v1; u = u0 + (u1 - u0) * t; break;
            case RegionEdge.VMinus: v = v0; u = u0 + (u1 - u0) * t; break;
            default: throw new ArgumentOutOfRangeException(nameof(edge), edge, "未知的边");
        }
        var dir = CubeSphere.ToDirection(key.Face, u, v);
        return dir * (terrain.RadiusCm + terrain.ElevationCm(dir));
    }

    /// <summary>这条边是否需要裙边：邻居比它细（级更高）才需要；同级或更粗都不需要。</summary>
    public static bool NeedsSkirt(RegionKey coarse, RegionKey neighbour)
    {
        if (coarse.Face != neighbour.Face) return false;         // 跨面边由共面的另一套规则管，这里不掺和
        return neighbour.Level > coarse.Level;
    }

    /// <summary>
    /// 允许弦高（cm）：<c>视距 × tan(每像素张角 × 允许像素数)</c>。
    /// 默认按 1080p / 60° 垂直视野，即 1 px ≈ 9.696e-4 rad。
    /// </summary>
    public static double AllowedSagCm(double viewDistanceCm, double pixels,
                                      double verticalFovDeg = 60.0, double screenHeightPx = 1080.0)
    {
        if (viewDistanceCm <= 0) throw new ArgumentOutOfRangeException(nameof(viewDistanceCm), viewDistanceCm, "视距必须为正");
        if (pixels <= 0) throw new ArgumentOutOfRangeException(nameof(pixels), pixels, "允许像素必须为正");
        if (verticalFovDeg <= 0 || verticalFovDeg >= 180)
            throw new ArgumentOutOfRangeException(nameof(verticalFovDeg), verticalFovDeg, "视野要在 0..180 度之间");
        if (screenHeightPx < 1) throw new ArgumentOutOfRangeException(nameof(screenHeightPx), screenHeightPx, "屏幕高度不合理");
        double perPixel = verticalFovDeg / screenHeightPx * Math.PI / 180.0;
        double angle = perPixel * pixels;
        // tan 是周期的：允许像素给到几千时角度会绕过 90° 再落回一个小值，"允许弦高"于是凭空变小，
        // 规划器就会一路细分下去（实测过：允许 1e6 像素反而比 4 像素细）。这个模型只在张角远小于 1 弧度时成立，
        // 所以越界就直接拒，而不是给一个看起来合理的数。
        if (angle >= Math.PI / 4.0)
            throw new ArgumentOutOfRangeException(nameof(pixels), pixels,
                $"允许张角 {angle:0.###} rad（≈{angle * 180 / Math.PI:0.#}°）超出小角模型范围");
        return viewDistanceCm * Math.Tan(angle);
    }

    /// <summary>这条档位的壳在多远离视点时才"亚像素"（不需要裙边）。反过来用就是 §13.30 那张表。</summary>
    public static double SubPixelDistanceCm(RegionKey key, int quadsPerEdge, double radiusCm,
                                            double pixels = 1.0,
                                            double verticalFovDeg = 60.0, double screenHeightPx = 1080.0)
    {
        double sag = ChordSagCm(key, quadsPerEdge, radiusCm);
        double perPixel = verticalFovDeg / screenHeightPx * Math.PI / 180.0;
        return sag / Math.Tan(perPixel * pixels);
    }

    /// <summary>这条裙边够不够盖住给定的弦高——缝合方案唯一的验收谓词（§13.30）。</summary>
    public static bool CoversSag(double depthCm, double sagCm) => depthCm >= sagCm;

    /// <summary>
    /// 沿粗 tile 的某条边生成一圈裙边：<c>2×(quadsPerEdge+1)</c> 个顶点、
    /// <c>quadsPerEdge</c> 段各两个三角形。顶边与粗 tile 的边界顶点<b>逐位相同</b>
    /// （否则裙边自己就制造了一条新缝），底边是顶边<b>远离球心</b>平移 <paramref name="depthCm"/>：
    /// 洞朝外张开，墙必须垒在洞的那一侧（方向由 §13.36 的像素台定，朝球心垒等于没缝）。
    /// </summary>
    public static RegionMesh BuildSkirt(RegionKey key, int quadsPerEdge, PlanetTerrain terrain,
                                        RegionEdge edge, double depthCm)
    {
        if (terrain is null) throw new ArgumentNullException(nameof(terrain));
        if (quadsPerEdge < 1)
            throw new ArgumentOutOfRangeException(nameof(quadsPerEdge), quadsPerEdge, "每边至少 1 个 quad");
        if (depthCm <= 0) throw new ArgumentOutOfRangeException(nameof(depthCm), depthCm, "裙边深度必须为正");
        if (depthCm >= terrain.RadiusCm)
            throw new ArgumentOutOfRangeException(nameof(depthCm), depthCm,
                $"裙边深度不能达到星球半径 {terrain.RadiusCm:0} cm：墙会盖住半个星球，LOD 配置错了");

        int n = quadsPerEdge;
        var top = BoundaryPoints(key, n, terrain, edge);        // 顺序：沿边参数递增
        var positions = new PlanetVector3[2 * (n + 1)];
        var ecef = new EcefCentimeters[2 * (n + 1)];
        double radius = terrain.RadiusCm;

        for (int i = 0; i <= n; i++)
        {
            var p = top[i];
            double len = p.Length;
            if (len <= 0)
                throw new InvalidOperationException($"{key} 的边界点 {i} 长度为 0，无法定朝球心的方向");
            // 方向由像素台定死（§13.36）：粗侧走弦、细侧走弧，弧在弦的**外侧**，
            // 所以那个楔形洞是朝外张开的——裙边必须朝外（远离球心）垒起来才挡得住视线；
            // 朝球心垒的墙整面都在粗面以下，掠射视线从它上方直接穿过去，等于没缝。
            var outward = p * (1.0 / len);                       // 单位向量远离球心
            var q = p + outward * depthCm;
            positions[i] = p;
            positions[n + 1 + i] = q;
            ecef[i] = EcefCentimeters.FromVector(p);
            ecef[n + 1 + i] = EcefCentimeters.FromVector(q);
        }

        // 绕序：壁的外侧（背离本 tile 内部的那一侧）要朝外。
        // 顶边沿参数递增、底边同序，所以 (top_i, top_i+1, bottom_i+1) / (top_i, bottom_i+1, bottom_i)
        // 的法线指向"参数增大方向 × 向内"的一侧；不同边的朝向不同，故按 tile 中心显式判一次再翻。
        var tris = new List<int>(n * 6);
        for (int i = 0; i < n; i++)
        {
            int t0 = i, t1 = i + 1;
            int b0 = n + 1 + i, b1 = n + 1 + i + 1;
            tris.Add(t0); tris.Add(t1); tris.Add(b1);
            tris.Add(t0); tris.Add(b1); tris.Add(b0);
        }
        FlipToFaceCoarseSide(key, n, positions, tris, terrain);

        var triangles = new int[tris.Count];
        tris.CopyTo(triangles);
        return new RegionMesh(key, n, positions, ecef, triangles);
    }

    private static PlanetVector3[] BoundaryPoints(RegionKey key, int n, PlanetTerrain terrain, RegionEdge edge)
    {
        var (u0, u1, v0, v1) = key.UvBounds();
        double radius = terrain.RadiusCm;
        var pts = new PlanetVector3[n + 1];
        for (int i = 0; i <= n; i++)
        {
            double t = i / (double)n;
            double u, v;
            switch (edge)
            {
                case RegionEdge.UPlus: u = u1; v = v0 + (v1 - v0) * t; break;
                case RegionEdge.UMinus: u = u0; v = v0 + (v1 - v0) * t; break;
                case RegionEdge.VPlus: v = v1; u = u0 + (u1 - u0) * t; break;
                case RegionEdge.VMinus: v = v0; u = u0 + (u1 - u0) * t; break;
                default: throw new ArgumentOutOfRangeException(nameof(edge), edge, "未知的边");
            }
            var dir = CubeSphere.ToDirection(key.Face, u, v);
            pts[i] = dir * (radius + terrain.ElevationCm(dir));
        }
        return pts;
    }

    /// <summary>
    /// 壁的可见面必须朝<b>本 tile 这一侧</b>（也就是"低的那一侧"）：洞是从粗面这边看过去的，
    /// 引擎剔掉背面，所以朝邻居那一面等于一面看不见的墙。
    /// <para>这条不是推出来的：像素台（§13.36）上，法线朝外时缝隙 5933 个暗像素，
    /// 只把绕序反过来就是 0——深度、位置、顶点全都没动。</para>
    /// </summary>
    private static void FlipToFaceCoarseSide(RegionKey key, int n, PlanetVector3[] positions,
                                             List<int> tris, PlanetTerrain terrain)
    {
        var (u0, u1, v0, v1) = key.UvBounds();
        double radius = terrain.RadiusCm;
        var centerDir = CubeSphere.ToDirection(key.Face, (u0 + u1) / 2.0, (v0 + v1) / 2.0);
        var center = centerDir * radius;

        var mid = (positions[0] + positions[n + 1]) * 0.5;
        var a = positions[0];
        var b = positions[1];
        var c = positions[n + 2];
        var normal = PlanetVector3.Cross(b - a, c - a);
        var towardNeighbour = mid - center;
        // 法线与"tile 中心 → 壁"同向 = 朝着邻居 = 从这边看不见，反掉
        if (PlanetVector3.Dot(normal, towardNeighbour) >= 0)
        {
            for (int t = 0; t < tris.Count; t += 3)
            {
                int tmp = tris[t + 1];
                tris[t + 1] = tris[t + 2];
                tris[t + 2] = tmp;
            }
        }
    }
}

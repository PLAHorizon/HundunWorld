using System;
using System.Globalization;
using Horizon.Planet;
using Xunit;

namespace Horizon.Planet.Tests;

/// <summary>
/// 球面行走与角色朝向（P3 / C2 的几何那一半）。
/// <para>这里能离线验的是"朝向场是否处处良定义、走一圈会不会翻转、高度会不会漂"。
/// 控制器手感、动画、载具要引擎，不在这条链上。</para>
/// <para>与 §13.48 的分工：那一条修的是"区域帧在极地面内相邻差 90°"；
/// 这一层的立场是<em>角色朝向根本不该建在区域帧上</em>——只由"该点的径向 + 想朝的方向"决定，
/// 于是区域怎么切、面怎么编号都影响不到它。</para>
/// </summary>
public class PlanetSphericalMotionTests
{
    private const long Radius = PlanetConstants.PlanetRadiusCm;      // 6 371 000 km

    /// <summary>显式容差比较。xunit v3 没有 (expected, actual, 小数位数) 那个重载，
    /// 而"容差是多少"本来就该写在判据里而不是藏在位数里。</summary>
    private static void Close(double expected, double actual, double tolerance, string what) =>
        Assert.True(Math.Abs(actual - expected) <= tolerance,
                    $"{what}：期望 {expected:0.############} ± {tolerance:0.#####}，实际 {actual:0.############}");

    private static PlanetVector3 SurfacePoint(double latitudeDeg, double longitudeDeg)
    {
        double lat = latitudeDeg * Math.PI / 180.0, lon = longitudeDeg * Math.PI / 180.0;
        return Geodetic.DirectionFromLatLon(lat, lon) * Radius;
    }

    [Fact]
    public void 朝向场处处正交右手_上就是径向()
    {
        // 覆盖六个面的中心、边角与极点附近：区域怎么编号完全不参与
        var samples = new System.Collections.Generic.List<PlanetVector3>();
        foreach (int face in new[] { 0, 1, 2, 3, 4, 5 })
            foreach (var key in new[]
            {
                new RegionKey(face, 4, 8, 8), new RegionKey(face, 4, 15, 8),
                new RegionKey(face, 4, 0, 0), new RegionKey(face, 4, 15, 15),
            })
                samples.Add(key.CenterDirection() * Radius);
        samples.Add(SurfacePoint(89.999, 0));            // 正好在地理北极附近
        samples.Add(SurfacePoint(-89.999, 137));         // 南极附近
        samples.Add(SurfacePoint(0, 0));

        foreach (var point in samples)
        {
            Assert.True(PlanetSphericalMotion.TryUp(point, out var up));
            // 想朝"北"：拿极轴方向当期望朝向，投到切平面
            var desired = PlanetVector3.AxisZ;
            if (!PlanetSphericalMotion.TryCharacterBasis(up, desired, out var forward, out var left, out var upOut))
                continue;                                 // 在极点本子上"北"就是"上"，退化是正确答案（另一条判据管）

            Close(1.0, forward.LengthSquared, 1e-9, "forward 单位长");
            Close(1.0, left.LengthSquared, 1e-9, "left 单位长");
            Close(0.0, PlanetVector3.Dot(forward, upOut), 1e-9, "前·上 正交");
            Close(0.0, PlanetVector3.Dot(left, upOut), 1e-9, "左·上 正交");
            Close(0.0, PlanetVector3.Dot(left, forward), 1e-9, "左·前 正交");
            // 右手：left = up × forward
            Close(1.0, PlanetVector3.Dot(PlanetVector3.Cross(upOut, forward), left), 1e-9, "右手系");
            Close(1.0, PlanetVector3.Dot(up, upOut), 1e-12, "up 就是径向（同一次归一化，应当逐位相同）");
        }
    }

    [Fact]
    public void 退化输入返回false而不是NaN()
    {
        Assert.False(PlanetSphericalMotion.TryUp(PlanetVector3.Zero, out _));
        Assert.False(PlanetSphericalMotion.TryUp(new PlanetVector3(double.NaN, 1, 1), out _));
        Assert.False(PlanetSphericalMotion.TryUp(new PlanetVector3(double.PositiveInfinity, 0, 0), out _));

        var up = PlanetVector3.AxisY;                     // 北极点上的"上"
        // "想朝的方向"正好是上：切向没有意义，必须拒而不是静默挑一个
        Assert.False(PlanetSphericalMotion.TryCharacterBasis(up, up * 3.0, out _, out _, out _));
        Assert.False(PlanetSphericalMotion.TryCharacterBasis(up, PlanetVector3.Zero, out _, out _, out _));
        Assert.False(PlanetSphericalMotion.TryCharacterBasis(up, new PlanetVector3(double.NaN, 0, 0), out _, out _, out _));
        Assert.False(PlanetSphericalMotion.TryCharacterBasis(PlanetVector3.Zero, PlanetVector3.AxisX, out _, out _, out _));

        // 正向孪生：只要有一点点切向分量就能建帧
        Assert.True(PlanetSphericalMotion.TryCharacterBasis(up, up + PlanetVector3.AxisX * 1e-6, out var f, out _, out _));
        Close(1.0, f.LengthSquared, 1e-9, "退化边界上仍能建出单位前向");

        Assert.Throws<ArgumentException>(() => PlanetSphericalMotion.StepAlongGeodesic(
            PlanetVector3.Zero, PlanetVector3.AxisX, 1000));
        Assert.Throws<ArgumentOutOfRangeException>(() => PlanetSphericalMotion.StepAlongGeodesic(
            SurfacePoint(0, 0), PlanetVector3.AxisX, double.NaN));
    }

    [Fact]
    public void 走一步的方向变化对得上文档那条0_005度()
    {
        // §5 的 C1 论证写的是"区域尺度 5.5 km 对 63,710 km 半径，重力方向偏差约 0.005°"。
        // 这句现在被实测复核：不是信它，是量它。
        var start = SurfacePoint(0, 0);
        var after5500m = PlanetSphericalMotion.StepAlongGeodesic(start, PlanetVector3.AxisZ, 550_000);   // 5.5 km
        PlanetSphericalMotion.TryUp(start, out var upA);
        PlanetSphericalMotion.TryUp(after5500m, out var upB);
        double deg = PlanetVector3.AngleBetween(upA, upB) * 180.0 / Math.PI;
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "MOTION_TILT per_5_5km_deg={0:0.#####} per_1km_deg={1:0.######}", deg, deg / 5.5));
        Assert.True(Math.Abs(deg - 0.005) < 0.0005, $"5.5 km 的倾角变化应当≈0.005°，实测 {deg:0.#####}°");

        // 反证：如果 StepAlongGeodesic 其实是"点 + 前×距离再归一"，半径会被拉小；这里必须不变
        Close(start.Length, after5500m.Length, 1e-3, "走一步半径不变（厘米）");            // 厘米精度的 3 位小数
    }

    [Fact]
    public void 沿大圆走一整圈回到起点_高度不漂朝向不翻转()
    {
        const double stepCm = 500_000;                     // 5 km 一步
        double circumference = 2 * Math.PI * Radius;
        int steps = (int)Math.Round(circumference / stepCm);

        var start = SurfacePoint(12, 47);
        PlanetSphericalMotion.TryUp(start, out var up0);
        var forward0 = PlanetSphericalMotion.ProjectToTangent(PlanetVector3.AxisZ, up0);   // 朝北出发
        Assert.True(forward0.LengthSquared > 0.5);

        var point = start;
        var forward = forward0;
        double worstRadiusDrift = 0, worstUpTilt = 0, worstOrtho = 0;

        for (int i = 0; i < steps; i++)
        {
            var previous = point;                                  // 先留住上一步的点：搬运要的是"这一段的两个端点"
            point = PlanetSphericalMotion.StepAlongGeodesic(point, forward, stepCm);
            forward = PlanetSphericalMotion.TransportForward(previous, point, forward);

            worstRadiusDrift = Math.Max(worstRadiusDrift, Math.Abs(point.Length - Radius));
            PlanetSphericalMotion.TryUp(point, out var up);
            worstUpTilt = Math.Max(worstUpTilt, PlanetVector3.AngleBetween(up, point.Normalized()) * 180.0 / Math.PI);
            worstOrtho = Math.Max(worstOrtho, Math.Abs(PlanetVector3.Dot(up, forward)));
        }

        double closedAngle = PlanetVector3.AngleBetween(up0, point.Normalized()) * 180.0 / Math.PI;
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "MOTION_LOOP steps={0} closed_deg={1:0.#####} radius_drift_cm={2:0.#####} up_tilt_deg={3:0.######} ortho_worst={4:0.######}",
            steps, closedAngle, worstRadiusDrift, worstUpTilt, worstOrtho));

        // 回到出发点的正上方（大圆闭合）
        Assert.True(closedAngle < 0.01, $"走 {steps} 步没回到起点：离出发点 {closedAngle:0.####}°");
        // 平行搬运绕大圆一圈的和乐角是 2π（一整圈），等价于朝向复原
        double forwardAngle = PlanetVector3.AngleBetween(forward0, forward) * 180.0 / Math.PI;
        Assert.True(forwardAngle < 0.05, $"绕大圆一圈后朝向没复原：差 {forwardAngle:0.####}°");
        Assert.True(worstRadiusDrift < 1.0, $"半径漂了 {worstRadiusDrift:0.###} cm，海拔会一路走偏");
        Assert.True(worstUpTilt < 1e-6, $"up 与径向偏离 {worstUpTilt:0.######}°，角色在'斜着站'");
        Assert.True(worstOrtho < 1e-9, $"forward 里混进了径向分量 {worstOrtho:0.######}");
    }

    [Fact]
    public void 绕极点走一圈朝向不跳变_区域帧那个90度在这里不存在()
    {
        // §13.48 那个缺陷的表现形式是"相邻区域的东轴差 90°"。
        // 这一层的朝向只由径向 + 期望朝向决定，所以绕极区走一整圈也不该有任何跳变。
        var pole = PlanetVector3.AxisZ * Radius;                      // 面 4 的法线方向 = 地理北极
        const double ringCm = 300_000;                                // 离极点 3 km 的一圈
        int steps = 3600;                                             // 每步 0.1°
        var upPole = PlanetVector3.AxisZ;

        PlanetVector3 PointAt(double angleRad)
        {
            // 纬度 90° − 3 km 的那条纬线圈
            double colat = ringCm / Radius;
            double lat = Math.PI / 2 - colat;
            return new PlanetVector3(Math.Cos(lat) * Math.Cos(angleRad),
                                     Math.Cos(lat) * Math.Sin(angleRad),
                                     Math.Sin(lat)) * Radius;
        }

        double worstJump = 0, worstTilt = 0;
        PlanetSphericalMotion.TryUp(PointAt(0), out var prevUp);
        Assert.True(PlanetSphericalMotion.TryCharacterBasis(prevUp, upPole, out var prevForward, out _, out _));

        for (int i = 1; i <= steps; i++)
        {
            var point = PointAt(i * 2 * Math.PI / steps);
            Assert.True(PlanetSphericalMotion.TryUp(point, out var up));
            // 期望朝向：沿这条纬线的切向（"向东绕圈"）
            var desired = PlanetVector3.Cross(upPole, point).Normalized();
            Assert.True(PlanetSphericalMotion.TryCharacterBasis(up, desired, out var forward, out _, out var upOut));

            worstTilt = Math.Max(worstTilt, PlanetVector3.AngleBetween(upOut, point.Normalized()) * 180.0 / Math.PI);
            if (i > 1)
            {
                double jump = PlanetVector3.AngleBetween(prevForward, forward) * 180.0 / Math.PI;
                worstJump = Math.Max(worstJump, jump);
            }
            prevForward = forward;
            prevUp = up;
        }

        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "MOTION_POLAR steps={0} worst_step_jump_deg={1:0.######} worst_up_tilt_deg={2:0.######}",
            steps, worstJump, worstTilt));
        // 每步跨 0.1° 经度，朝东的方向本来就该跟着转 0.1°——那是要**对上**的期望，不是要防的跳变。
        // 要防的是 §13.48 那种 90° 的整块翻转，它会让实测值比几何期望大三个数量级。
        double expectedJump = 360.0 / steps;
        Assert.True(Math.Abs(worstJump - expectedJump) < 0.001,
                    $"每步朝向变化 {worstJump:0.####}°，几何期望 {expectedJump:0.####}°——对不上就是有异常跳变");
        Assert.True(worstTilt < 1e-9, "up 不再严格径向");
    }

    [Fact]
    public void 表面重力两条锚按物理式算对_但不由几何替产品选()
    {
        double g10x = PlanetSphericalMotion.SurfaceGravitySameDensityCmPerS2(Radius);
        double gSameMass = PlanetSphericalMotion.SurfaceGravitySameMassCmPerS2(Radius);
        double earth = PlanetSphericalMotion.EarthGravityCmPerS2;

        // 等密度：M ∝ R³ 且 g ∝ M/R² ⇒ g ∝ R，10 倍半径就是 10 倍重力（≈ 98 m/s²）
        Close(earth * 10.0, g10x, 0.05, "等密度 10 倍重力");
        // 等质量：g ∝ 1/R² ⇒ 1/100
        Close(earth / 100.0, gSameMass, 0.0005, "等质量 1/100 重力");

        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "MOTION_GRAVITY same_density_m_per_s2={0:0.###} same_mass_m_per_s2={1:0.####} ratio={2:0.#}",
            g10x / 100.0, gSameMass / 100.0, g10x / gSameMass));

        // 这两个数差 1000 倍，而它一次决定跳跃高度、下落时间、载具抓地——是产品决定，不是几何推论。
        // 这里只钉"两个都算对了、差距是 1000 倍"，不钉"用哪个"。
        Close(1000.0, g10x / gSameMass, 0.05, "两条锚的差距是 1000 倍");
    }
}

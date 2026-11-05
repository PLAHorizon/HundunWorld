using System;
using Horizon.Planet;
using Xunit;

namespace Horizon.Planet.Tests;

/// <summary>
/// 表面重力的选定值与由它推出的手感数。
/// <para>这一组判据的存在理由不是"980.665 这个数写对了"，而是<b>把'这是产品决定'这件事钉成一个可判的数</b>：
/// 同一个半径能物理自洽地推出 10 g 和 0.01 g 两档，与选定值差 10 倍与 0.01 倍。
/// 如果哪天有人把常量换成其中一档，下游不会报错，只会集体手感突变——
/// 而那正是"跳跃参数是谁定的"这种问题的来源。</para>
/// </summary>
public class PlanetGravityTests
{
    /// <summary>xunit v3 删掉了 Assert.Equal(double, double, int) 那个精度重载，这里显式给差。</summary>
    private static void AssertNear(double expected, double actual, double tolerance, string what) =>
        Assert.True(Math.Abs(expected - actual) <= tolerance,
            $"{what}：期望 {expected:R}，实际 {actual:R}（容差 {tolerance:E1}）");

    [Fact]
    public void 选定值是1g_与两个物理候选差多少写在描述里()
    {
        AssertNear(980.665, PlanetGravity.GameCmPerS2, 1e-9, "选定值就是 1g");
        AssertNear(PlanetGravity.EarthNormalCmPerS2, PlanetGravity.GameCmPerS2, 0, "GameCmPerS2 就是 EarthNormal 那个别名");

        string line = PlanetGravity.DescribeAgainstPhysics(PlanetConstants.PlanetRadiusCm);
        // 同密度：g ∝ R ⇒ 10 倍半径就是 10 g
        Assert.Contains("同密度=9806.65", line, StringComparison.Ordinal);
        Assert.Contains("(10×)", line, StringComparison.Ordinal);
        // 同质量：g ∝ 1/R² ⇒ 百分之一 g
        Assert.Contains("(0.01×)", line, StringComparison.Ordinal);
        // 选定值本身要出现在同一行里，否则"跟哪个比"这句话没有落点
        Assert.Contains("g_game=980.665", line, StringComparison.Ordinal);
    }

    [Fact]
    public void 三个候选之间的关系是1000倍_而不是随手挑的两个数()
    {
        double g10 = PlanetSphericalMotion.SurfaceGravitySameDensityCmPerS2(PlanetConstants.PlanetRadiusCm);
        double g001 = PlanetSphericalMotion.SurfaceGravitySameMassCmPerS2(PlanetConstants.PlanetRadiusCm);
        // 10× 与 1/100 相差 1000 倍：这就是"为什么必须由产品决定，几何无权替它选"
        Assert.True(Math.Abs(g10 / g001 - 1000.0) < 1e-9, $"同密度/同质量 = {g10 / g001}，不是 1000");
        Assert.True(Math.Abs(g10 / PlanetGravity.GameCmPerS2 - 10.0) < 1e-9);
        Assert.True(Math.Abs(g001 / PlanetGravity.GameCmPerS2 - 0.01) < 1e-12);
    }

    [Fact]
    public void 抛物线三个方向互相闭合_系数约定只有一份()
    {
        double takeoff = PlanetGravity.TakeoffSpeedForApex(150.0);          // 跳 1.5 m
        var (apex, airTime) = PlanetGravity.Jump(takeoff);
        Assert.True(Math.Abs(apex - 150.0) < 1e-9, $"起跳初速反推再跳回来应当是 150 cm，实际 {apex}");

        // 整段滞空时间如果<em>从静止</em>落下来，走的不是 apex 的两倍而是四倍：
        // ½g(2v/g)² = 4·(v²/2g)。物理上人不会从静止落满全程（上升段是减速），
        // 所以这一条只在"系数约定"层面成立——把它写成 2× 会是错的，而它看起来很像对的。
        PlanetGravity.FreeFall(airTime, out double speedOverFullAir, out double dropFull);
        Assert.True(Math.Abs(dropFull - 4 * apex) < 1e-9,
                    $"从静止落满全程走了 {dropFull:0.####} cm，应当是 4×apex={4 * apex:0.####}");

        // 下落的那一半才是起跳的镜像：从顶点静止落 airTime/2，距离=apex、末速=起跳速度。
        // （第一版这里写成"整个滞空时间的末速等于起跳速度"，那是错的——从静止落满全程
        //  得到的是 2v，而它恰好等于上升段与下落段的速度之和，很像一个自洽的数。）
        PlanetGravity.FreeFall(airTime / 2, out double speedHalf, out double dropHalf);
        Assert.True(Math.Abs(speedHalf - takeoff) < 1e-9,
                    $"落回地面那一半时间的末速 {speedHalf:0.####} vs 起跳 {takeoff:0.####}");
        Assert.True(Math.Abs(dropHalf - apex) < 1e-9,
                    $"从顶点落 {airTime / 2:0.###} s 走了 {dropHalf:0.####} cm，应当是 apex {apex:0.####}");
        Assert.True(Math.Abs(speedOverFullAir - 2 * takeoff) < 1e-9, "落满全程的末速应当是起跳的两倍");

        // 1 g 下的常识锚：跳 1.5 m 的滞空约 1.1 s（上升 0.55 s）——手感如果漂了，这条会先知道
        Assert.True(airTime > 1.0 && airTime < 1.2, $"跳 1.5 m 的滞空时间 {airTime:0.###} s 不在 1g 的合理区间");
    }

    [Fact]
    public void 非法时长与速度当场抛_而不是给一个看起来合理的负距离()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PlanetGravity.FreeFall(-1, out _, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => PlanetGravity.FreeFall(double.NaN, out _, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => PlanetGravity.FreeFall(double.PositiveInfinity, out _, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => PlanetGravity.Jump(-5));
        Assert.Throws<ArgumentOutOfRangeException>(() => PlanetGravity.Jump(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => PlanetGravity.TakeoffSpeedForApex(-0.1));

        // 正向孪生：0 是合法边界（不跳、不落），三条都得给 0 而不是抛
        PlanetGravity.FreeFall(0, out double v0, out double d0);
        AssertNear(0, v0, 1e-12, "0 秒下落没有速度");
        AssertNear(0, d0, 1e-12, "0 秒下落没有距离");
        var (apex0, air0) = PlanetGravity.Jump(0);
        AssertNear(0, apex0, 1e-12, "不跳就没有顶点");
        AssertNear(0, air0, 1e-12, "不跳就没有滞空");
        AssertNear(0, PlanetGravity.TakeoffSpeedForApex(0), 1e-12, "跳 0 高不需要初速");
    }

    [Fact]
    public void 忽略曲率的直线近似在跳跃这一档成立在长途行走上不成立()
    {
        // "球面上的下落能不能当直线算"是 P3 要不要每一帧重算 up 矢量的依据。
        // 边界不是拍的：走 d 之后地面在切平面下弯下去 d²/(2R)。
        double R = PlanetConstants.PlanetRadiusCm;
        double Sag(double distanceCm) => distanceCm * distanceCm / (2 * R);

        // 跳 1.5 m：水平位移最多几十米，曲率亏在 1e-3 cm 量级——完全可以从切平面里算
        var (_, airTime) = PlanetGravity.Jump(PlanetGravity.TakeoffSpeedForApex(150.0));
        double jumpSag = Sag(1000.0 * airTime);                    // 按 10 m/s 的水平速度估
        Assert.True(jumpSag < 0.01, $"跳跃档的曲率亏 {jumpSag:E3} cm，已经不能忽略");

        // 跑 1 km：不到 1 cm；跑 10 km：78 cm，已经和一个人的身高同量级——那一档必须按球面算，
        // 不能靠一条直线积分撑过去。两档的<em>比值</em>才是这条判据要钉的东西（位移 10 倍，亏 100 倍）。
        Assert.True(Sag(100_000) < 1.0, $"1 km 的曲率亏 {Sag(100_000):0.###} cm，已经不到厘米档了");
        double sag10km = Sag(1_000_000);
        Assert.True(sag10km > 50.0 && sag10km < 120.0,
                    $"10 km 的曲率亏 {sag10km:0.##} cm 不在预期的 78 cm 附近：档位或半径口径变了");
        Assert.True(sag10km / jumpSag > 1e4,
                    $"跳跃档与长途档只差 {sag10km / jumpSag:0.###} 倍，那条'可以忽略'的界线站不住");
    }
}

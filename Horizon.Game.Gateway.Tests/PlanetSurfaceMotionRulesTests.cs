using System;
using Horizon.Planet;
using HundunWorld.Game.Planet;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 球面步行决策层的判据（P3 的离线那一半）。
/// <para>这一层只回答三个数：<b>走一步半径不变</b>、<b>跳起来的高度与滞空对得上 1g</b>、
/// <b>落地落在地面上</b>。它们各自都有一个独立参照——重力用 ICPO 的 980.665 cm/s² 现算，
/// 不拿 <see cref="PlanetGravity"/> 反过来给自己作证；滞空时间用 <c>2v₀/g</c>；
/// 落地检查直接量"落地那一帧的离地高度"。</para>
/// <para>每个出口都配反向孪生：能落地的分支要有"继续飞"的样本，能贴地的要有"走出边沿"的样本，
/// 否则"永远吸附地面"或"永远在空中"这类实现也能全绿。</para>
/// </summary>
public class PlanetSurfaceMotionRulesTests
{
    private const double Radius = PlanetConstants.PlanetRadiusCm;
    private const double G = 980.665;                 // 独立参照：1g = 9.80665 m/s²，不读 PlanetGravity
    private static readonly PlanetVector3 Home =
        Geodetic.DirectionFromLatLon(30.0 * Math.PI / 180.0, 120.0 * Math.PI / 180.0);
    private static readonly PlanetVector3 EastApprox =
        PlanetVector3.Cross(PlanetVector3.AxisZ, Home).Normalized();     // Home 的切向"东"

    private static double Altitude(in PlanetSurfaceMotionRules.State s) => s.PositionCm.Length - Radius;

    private static void CloseTo(double expected, double actual, double tol, string what) =>
        Assert.True(Math.Abs(expected - actual) <= tol, $"{what}：期望 {expected:0.######} 实得 {actual:0.######}（容差 {tol}）");

    private static PlanetSurfaceMotionRules.State Standing(double ground = 0.0) =>
        PlanetSurfaceMotionRules.StandingAt(Home, ground, EastApprox);

    private static bool Step(in PlanetSurfaceMotionRules.State s, double dt, double fwd, double left, bool jump,
                             Func<PlanetVector3, double?> ground, out PlanetSurfaceMotionRules.State next,
                             out PlanetSurfaceMotionRules.StepOutcome outcome) =>
        PlanetSurfaceMotionRules.TryStep(s, dt, fwd, left, jump, ground, out next, out outcome, out _);

    // ==================== 走：只走测地线 ====================

    [Fact]
    public void 平地走一步_半径不变且水平位移等于走速乘dt()
    {
        var s0 = Standing();
        Assert.True(Step(s0, 0.1, 1.0, 0.0, false, _ => 0.0, out var s1, out var outcome));
        Assert.Equal(PlanetSurfaceMotionRules.StepOutcome.Walked, outcome);
        Assert.True(s1.Grounded);

        double chord = (s1.PositionCm - s0.PositionCm).Length;
        CloseTo(PlanetSurfaceMotionRules.WalkSpeedCmPerS * 0.1, chord, 1e-6, "一步的弦长");
        CloseTo(0.0, Altitude(s1), 1e-6, "平地走一步的离地高度");
        // 方向：位移必须沿着"前"（而不是随便某个切向）
        var moved = (s1.PositionCm - s0.PositionCm).Normalized();
        Assert.True(PlanetVector3.Dot(moved, EastApprox) > 0.999999,
            $"位移方向偏离朝向：dot={PlanetVector3.Dot(moved, EastApprox):0.######}");
    }

    [Fact]
    public void 输入长度超过一被归一化_斜着走不比直着走快()
    {
        Assert.True(Step(Standing(), 0.1, 1.0, 1.0, false, _ => 0.0, out var diag, out _));
        double diagDistance = (diag.PositionCm - Standing().PositionCm).Length;
        CloseTo(PlanetSurfaceMotionRules.WalkSpeedCmPerS * 0.1, diagDistance, 1e-6, "对角输入的步长");
    }

    [Fact]
    public void 上坡跟着地面抬_下五厘米台阶踩下去_走出五米边沿改成下落()
    {
        var s0 = Standing(0.0);
        // 上坡：地面比脚下高 50 cm —— 抬上去，仍贴地
        Assert.True(Step(s0, 0.05, 1.0, 0.0, false, _ => 50.0, out var up, out var upOutcome));
        Assert.Equal(PlanetSurfaceMotionRules.StepOutcome.Walked, upOutcome);
        Assert.True(up.Grounded);
        CloseTo(50.0, Altitude(up), 1e-6, "上坡后的离地高度");

        // 下 5 cm 台阶 —— 在台阶上限之内，踩下去，仍贴地
        Assert.True(Step(s0, 0.05, 1.0, 0.0, false, _ => -5.0, out var down, out var downOutcome));
        Assert.True(down.Grounded, "5 cm 台阶应当踩下去而不是掉下去");
        Assert.Equal(PlanetSurfaceMotionRules.StepOutcome.Walked, downOutcome);
        CloseTo(-5.0, Altitude(down), 1e-6, "台阶下的离地高度");

        // 走出 5 m 边沿 —— 超过一个台阶，转成下落（反向孪生：不许"永远吸附地面"）
        Assert.True(Step(s0, 0.05, 1.0, 0.0, false, _ => -500.0, out var off, out var offOutcome));
        Assert.False(off.Grounded);
        Assert.Equal(PlanetSurfaceMotionRules.StepOutcome.Airborne, offOutcome);
        CloseTo(0.0, Altitude(off), 1e-6, "走出边沿那一帧还没开始掉");
    }

    [Fact]
    public void 贴地走一千步半径不漂()
    {
        var s = Standing();
        for (int i = 0; i < 1000; i++)
        {
            Assert.True(Step(s, 1.0 / 60.0, 1.0, 0.0, false, _ => 0.0, out s, out _));
        }
        Assert.True(s.Grounded);
        CloseTo(0.0, Altitude(s), 1e-3, "一千步之后的离地高度");
    }

    [Fact]
    public void 跨极点走一步半径不变()
    {
        // 极区是 §13.48 那类缺陷的老巢：帧的"东"在那里退化。这一层用径向当"上"，
        // 所以只要 StepAlongGeodesic 保半径，这里就该保半径。（朝向由调用方给，不依赖区域帧。）
        var pole = PlanetVector3.AxisZ;
        var s0 = PlanetSurfaceMotionRules.StandingAt(pole, 0.0, PlanetVector3.AxisX);
        Assert.True(Step(s0, 0.1, 1.0, 0.0, false, _ => 0.0, out var s1, out _));
        CloseTo(0.0, Altitude(s1), 1e-6, "极点附近走一步的离地高度");
        Assert.True(Math.Abs(PlanetVector3.Dot(s1.PositionCm.Normalized(), PlanetVector3.AxisZ)) > 0.999999,
            "走一步之后还应当贴着极点附近");
    }

    [Fact]
    public void 左转一步之后朝向转到左边且仍与半径垂直()
    {
        var s0 = Standing();
        var left0 = PlanetVector3.Cross(s0.PositionCm.Normalized(), s0.ForwardTangent);
        Assert.True(Step(s0, 0.1, 0.0, 1.0, false, _ => 0.0, out var s1, out _));
        CloseTo(1.0, PlanetVector3.Dot(s1.ForwardTangent, left0), 1e-6, "转完之后朝向与原来那个左方向");
        // 6.371e9 cm 量级上的单位化会留下 ~1e-9 的相对误差，正交性按 1e-6 rad 判就够了
        CloseTo(0.0, PlanetVector3.Dot(s1.ForwardTangent, s1.PositionCm.Normalized()), 1e-6, "朝向与径向的点积");
    }

    // ==================== 跳与落：对账 1g 的解析解 ====================

    [Fact]
    public void 起跳初速与滞空时间对得上1g的解析解()
    {
        double v0Expected = Math.Sqrt(2 * G * PlanetSurfaceMotionRules.JumpApexCm);     // ≈ 542.4 cm/s
        var s = Standing();
        Assert.True(Step(s, 1.0 / 60.0, 0.0, 0.0, true, _ => 0.0, out s, out var jumpOutcome));
        Assert.Equal(PlanetSurfaceMotionRules.StepOutcome.Jumped, jumpOutcome);
        Assert.False(s.Grounded);
        CloseTo(v0Expected, s.VerticalSpeedCmPerS, 0.05, "起跳初速");
        CloseTo(0.0, Altitude(s), 1e-9, "起跳那一帧位置不动");

        // 空中的解析解：滞空 2v₀/g、顶点 v₀²/2g
        double airTimeExpected = 2 * v0Expected / G;                                    // ≈ 1.106 s
        double apexExpected = PlanetSurfaceMotionRules.JumpApexCm;
        double apexSeen = 0, elapsed = 0;
        const double dt = 0.002;               // 2 ms：离散步进的顶点误差 O(g·dt²) ≈ 0.002 cm
        for (int i = 0; i < 5000; i++)
        {
            Assert.True(Step(s, dt, 0.0, 0.0, false, _ => 0.0, out s, out var outcome));
            elapsed += dt;
            apexSeen = Math.Max(apexSeen, Altitude(s));
            if (outcome == PlanetSurfaceMotionRules.StepOutcome.Landed) break;
            Assert.NotEqual(PlanetSurfaceMotionRules.StepOutcome.Walked, outcome);
        }
        Assert.True(s.Grounded, "跳起来之后必须在有限帧内落地");
        CloseTo(airTimeExpected, elapsed, 0.01, "滞空时间");
        CloseTo(apexExpected, apexSeen, 0.5, "跳跃顶点高度");
    }

    [Fact]
    public void 从一米高自由落体_落地时间与速度对得上解析解()
    {
        // 起点：离地 1000 cm 的空中，速度为 0
        var up = Home;
        var start = new PlanetSurfaceMotionRules.State(
            PlanetSurfaceMotionRules.ScaleToAltitude(up * Radius, 1000.0),
            EastApprox, 0.0, grounded: false);

        double tExpected = Math.Sqrt(2 * 1000.0 / G);          // ≈ 1.4278 s
        double vExpected = Math.Sqrt(2 * G * 1000.0);          // ≈ 1400.5 cm/s
        const double dt = 0.002;
        double elapsed = 0, lastSpeed = 0;
        var s = start;
        for (int i = 0; i < 5000; i++)
        {
            lastSpeed = s.VerticalSpeedCmPerS;
            Assert.True(Step(s, dt, 0.0, 0.0, false, _ => 0.0, out s, out var outcome));
            elapsed += dt;
            if (outcome == PlanetSurfaceMotionRules.StepOutcome.Landed) break;
        }
        Assert.True(s.Grounded, "一米高的自由落体必须落地");
        CloseTo(tExpected, elapsed, 0.01, "落地时间");
        CloseTo(-vExpected, lastSpeed, 0.02 * vExpected, "落地前一帧的垂直速度");
        CloseTo(0.0, s.VerticalSpeedCmPerS, 1e-9, "落地后垂直速度清零");
        CloseTo(0.0, Altitude(s), 1e-6, "落地后的离地高度");
    }

    [Fact]
    public void 落地后的下一帧_按地面走而不是继续往下穿()
    {
        var s = Standing(0.0);
        Assert.True(Step(s, 0.5 / PlanetSurfaceMotionRules.WalkSpeedCmPerS, 0.0, 0.0, true, _ => 0.0, out s, out _));
        for (int i = 0; i < 5000 && !s.Grounded; i++)
            Assert.True(Step(s, 0.002, 0.0, 0.0, false, _ => 0.0, out s, out _));
        Assert.True(s.Grounded);
        // 地面在这之后抬到 -20 cm（比容差小、比台阶大？20 > 10 → 会转成下落；这里用 -5 保持贴地）
        Assert.True(Step(s, 1.0 / 60.0, 1.0, 0.0, false, _ => -5.0, out var s1, out var outcome));
        Assert.True(s1.Grounded);
        Assert.Equal(PlanetSurfaceMotionRules.StepOutcome.Walked, outcome);
        CloseTo(-5.0, Altitude(s1), 1e-6, "落地后贴到新地面");
    }

    // ==================== 拒绝的出口 ====================

    [Fact]
    public void dt不合法时拒绝_理由里带着dt的读数()
    {
        var s = Standing();
        foreach (double dt in new[] { 0.0, -0.01, double.NaN, PlanetSurfaceMotionRules.MaxStepSeconds * 2 })
        {
            Assert.False(PlanetSurfaceMotionRules.TryStep(s, dt, 1, 0, false, _ => 0.0,
                out _, out _, out string why));
            Assert.Contains("dt=", why, StringComparison.Ordinal);
        }
        // 正向孪生：上限本身是允许的
        Assert.True(PlanetSurfaceMotionRules.TryStep(s, PlanetSurfaceMotionRules.MaxStepSeconds, 0, 0, false,
            _ => 0.0, out _, out _, out _));
    }

    [Fact]
    public void 站在地上却取不出地面时拒绝_而不是把海平面当零()
    {
        Assert.False(PlanetSurfaceMotionRules.TryStep(Standing(), 1.0 / 60.0, 1, 0, false, _ => null,
            out _, out _, out string why));
        Assert.Contains("基场没接上", why, StringComparison.Ordinal);

        // 反向孪生：空中取不出地面只影响何时落地，不该拒绝（否则掉进无数据区就卡住）
        var air = new PlanetSurfaceMotionRules.State(
            PlanetSurfaceMotionRules.ScaleToAltitude(Home * Radius, 500.0), EastApprox, 0.0, grounded: false);
        Assert.True(PlanetSurfaceMotionRules.TryStep(air, 1.0 / 60.0, 0, 0, false, _ => null,
            out var next, out var outcome, out _));
        Assert.Equal(PlanetSurfaceMotionRules.StepOutcome.Airborne, outcome);
        Assert.True(Altitude(next) < 500.0, "没有地面数据时仍应按重力下落");
    }

    [Fact]
    public void 位置或输入退化时当场拒绝()
    {
        var bad = new PlanetSurfaceMotionRules.State(PlanetVector3.Zero, EastApprox, 0.0, true);
        Assert.False(PlanetSurfaceMotionRules.TryStep(bad, 1.0 / 60.0, 1, 0, false, _ => 0.0,
            out _, out _, out string why));
        Assert.Contains("状态不可用", why, StringComparison.Ordinal);

        Assert.False(PlanetSurfaceMotionRules.TryStep(Standing(), 1.0 / 60.0, double.NaN, 0, false,
            _ => 0.0, out _, out _, out string why2));
        Assert.Contains("非有限值", why2, StringComparison.Ordinal);

        // 构造期就拒绝的两种：落点退化、朝向退化
        Assert.Throws<ArgumentException>(() =>
            PlanetSurfaceMotionRules.StandingAt(PlanetVector3.Zero, 0.0, EastApprox));
        Assert.Throws<ArgumentException>(() =>
            PlanetSurfaceMotionRules.StandingAt(Home, 0.0, Home));        // 朝向与径向平行
    }

    // ==================== 与库的其它口径同源 ====================

    [Fact]
    public void 走速与跳跃高度是这一层的公开口径_不是散落在各处的常数()
    {
        // 走速与客户端 MoveSpeed 同口径；跳跃高度对应 §13.55 的手感锚 1.5 m。
        // 这条钉的其实是"改手感只改一处"：两个常数一旦被下游抄走，这里改了下游不跟，测试仍绿。
        Assert.Equal(500.0, PlanetSurfaceMotionRules.WalkSpeedCmPerS);
        Assert.Equal(150.0, PlanetSurfaceMotionRules.JumpApexCm);
        CloseTo(542.4, PlanetGravity.TakeoffSpeedForApex(PlanetSurfaceMotionRules.JumpApexCm), 0.1,
            "1.5 m 跳跃对应的初速（文档锚）");
        var (apex, airTime) = PlanetGravity.Jump(542.4);
        CloseTo(1.106, airTime, 0.002, "文档锚的滞空时间");
        CloseTo(150.0, apex, 0.5, "文档锚的顶点高度");
    }
}

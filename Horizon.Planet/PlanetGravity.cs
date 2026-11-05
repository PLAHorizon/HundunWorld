using System;
using System.Globalization;

namespace Horizon.Planet;

/// <summary>
/// 这颗星球<em>玩法上</em>用的表面重力。
/// <para><b>这是一个产品决定，不是几何推论</b>（2026-11-04 由 Long 确认：保持 1g 手感）。
/// 同一个半径可以物理自洽地推出两个相差 1000 倍的值——同密度得 10 g、同质量得 0.01 g——
/// 而这两个数都不给"地球化"的手感：前者跳不起来、后者近乎失重。
/// 所以这里明写"选定值"，并把那两个候选留在 <see cref="PlanetSphericalMotion"/> 里随时可对账。</para>
/// <para>要改手感只改这一处：所有下游（跳跃初速、下落积分、载具抓地、AI 落地判定）都应当从
/// <see cref="GameCmPerS2"/> 取数，而不是自己再写一个 9.8。<b>不要再抄第二份</b>——
/// 一个常量散成两处，就会有两套下落速度互相判对方作弊，那是本仓库反复踩过的那类形状。</para>
/// </summary>
public static class PlanetGravity
{
    /// <summary>标准重力（ICPO 约定 9.80665 m/s²）换算成本项目的厘米/秒²。</summary>
    public const double EarthNormalCmPerS2 = 980.665;

    /// <summary>
    /// 玩法用的表面重力。当前决定：与星球半径解耦，保持 1g 手感。
    /// <para>代价说清楚：这意味着这颗星球的平均密度不是地球的，
    /// <see cref="PlanetSphericalMotion.SurfaceGravitySameDensityCmPerS2"/> 那条物理式<em>不</em>成立。
    /// 我们做的是"地球化手感"，不是"地球化天体物理"。</para>
    /// </summary>
    public const double GameCmPerS2 = EarthNormalCmPerS2;

    /// <summary>
    /// 选定值相对于两个物理候选的比值。<b>用来把决定本身写成一个可判的数字</b>：
    /// 如果有人把 <see cref="GameCmPerS2"/> 改成同密度或同质量那一档，这里会跟着变，
    /// 判据就会把"手感换了"这件事指出来，而不是让下游默默用上一个差 1000 倍的数。
    /// </summary>
    public static string DescribeAgainstPhysics(long radiusCm)
    {
        double sameDensity = PlanetSphericalMotion.SurfaceGravitySameDensityCmPerS2(radiusCm);
        double sameMass = PlanetSphericalMotion.SurfaceGravitySameMassCmPerS2(radiusCm);
        return string.Format(CultureInfo.InvariantCulture,
            "g_game={0:0.###} cm/s² (1g 手感约定) | 同密度={1:0.###} ({2:0.###}×) | 同质量={3:0.####} ({4:0.####}×)",
            GameCmPerS2, sameDensity, sameDensity / GameCmPerS2, sameMass, sameMass / GameCmPerS2);
    }

    /// <summary>
    /// 从静止自由下落 <paramref name="seconds"/> 后的速度与下落距离（厘米/秒、厘米）。
    /// <para>单独成一个函数而不是让各处自己写 <c>0.5 * g * t * t</c>：跳跃高度、掉落伤害、
    /// AI 落地时序都要用同一条抛物线，写三遍就会有三份不同的<em>系数约定</em>
    /// （有的地方漏了 0.5，有的地方用了帧数而不是秒）。球面上的"下落"是朝地心，
    /// 距离与半径比得极小才谈得上直线近似——所以这里也给出它适用的范围提示。</para>
    /// </summary>
    public static void FreeFall(double seconds, out double speedCmPerS, out double dropCm)
    {
        if (seconds < 0 || !double.IsFinite(seconds))
            throw new ArgumentOutOfRangeException(nameof(seconds), seconds, "下落时长必须是有限非负秒数");
        speedCmPerS = GameCmPerS2 * seconds;
        dropCm = 0.5 * GameCmPerS2 * seconds * seconds;
    }

    /// <summary>
    /// 以初速 <paramref name="takeoffCmPerS"/> 垂直起跳能到的高度（厘米）与滞空时间（秒）。
    /// <para>这是给"手感参数反推"用的：设计说"跳 1.5 m"，这里给出对应的初速应该是多少，
    /// 而不是让人在引擎里试。</para>
    /// </summary>
    public static (double ApexCm, double AirTimeS) Jump(double takeoffCmPerS)
    {
        if (takeoffCmPerS < 0 || !double.IsFinite(takeoffCmPerS))
            throw new ArgumentOutOfRangeException(nameof(takeoffCmPerS), takeoffCmPerS, "起跳初速必须是有限非负值");
        double apex = takeoffCmPerS * takeoffCmPerS / (2 * GameCmPerS2);
        return (apex, 2 * takeoffCmPerS / GameCmPerS2);
    }

    /// <summary>反过来：要跳到 <paramref name="apexCm"/> 高，需要的起跳初速（厘米/秒）。</summary>
    public static double TakeoffSpeedForApex(double apexCm)
    {
        if (apexCm < 0 || !double.IsFinite(apexCm))
            throw new ArgumentOutOfRangeException(nameof(apexCm), apexCm, "跳跃高度必须是有限非负厘米");
        return Math.Sqrt(2 * GameCmPerS2 * apexCm);
    }
}

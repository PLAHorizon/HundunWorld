using System;

namespace Horizon.Planet;

/// <summary>
/// 球面行走与角色朝向（P3 / C2 的那套数学）。
/// <para>这一层刻意只做<em>几何</em>：给定"我在球面的哪一点、想朝哪个方向、走多远"，
/// 给出新的位置与一套正交右手朝向。控制器、动画、载具都不在这儿——
/// 它们要引擎，而这里每一条都能离线判据（P3 的"角色不翻转""环球片段可走通"两句话，
/// 说到底就是这里的不变量）。</para>
/// <para>与 §13.48 的关系是直接的：那里的缺陷（极地面内相邻帧差 90°）之所以危险，
/// 就是因为角色朝向如果建在"区域帧的东轴"上，走到极区会整体转四分之一圈。
/// 这一层给出的答案是：<b>朝向只由"该点的径向 + 想要的朝前方向"决定</b>，
/// 与区域怎么切、面怎么编号完全无关，所以不存在那种翻转。</para>
/// </summary>
public static class PlanetSphericalMotion
{
    /// <summary>地球表面重力加速度（厘米/秒²）：9.80665 m/s²。</summary>
    public const double EarthGravityCmPerS2 = 980.665;

    /// <summary>地球平均半径（厘米），与 <see cref="PlanetConstants.EarthMeanRadiusCm"/> 同口径。</summary>
    public const double EarthRadiusCm = PlanetConstants.EarthMeanRadiusCm;

    /// <summary>
    /// 单位径向（= 该点的"上"）。零向量与非有限值返回 false，不给 NaN。
    /// </summary>
    public static bool TryUp(in PlanetVector3 ecefCm, out PlanetVector3 up)
    {
        up = PlanetVector3.Zero;
        double length = ecefCm.Length;
        if (!double.IsFinite(length) || length <= 1e-300) return false;
        up = ecefCm / length;
        return true;
    }

    /// <summary>
    /// 同密度放大到 <paramref name="radiusCm"/> 后的表面重力：质量 ∝ R³ 而 g ∝ M/R² ⇒ <b>g ∝ R</b>。
    /// 10 倍半径就是 10 倍重力（≈ 98 m/s²）。
    /// </summary>
    public static double SurfaceGravitySameDensityCmPerS2(double radiusCm) =>
        EarthGravityCmPerS2 * (radiusCm / EarthRadiusCm);

    /// <summary>
    /// 同质量摊到 <paramref name="radiusCm"/> 后的表面重力：g ∝ 1/R² ⇒ 10 倍半径只有 1/100 的重力。
    /// <para>两个数都摆在这儿而不是选一个：<b>这颗星球的表面重力是产品决定，不是几何推论</b>，
    /// 而它一次决定跳跃高度、下落速度、载具抓地力全部手感。判据只钉"这两个数按物理式算对了"，
    /// 不钉"用哪个"。</para>
    /// </summary>
    public static double SurfaceGravitySameMassCmPerS2(double radiusCm)
    {
        double ratio = EarthRadiusCm / radiusCm;
        return EarthGravityCmPerS2 * ratio * ratio;
    }

    /// <summary>
    /// 由"该点的上"与"想朝前的方向"造角色朝向：朝前向量投影到切平面再归一，
    /// 左 = 上 × 前，保证正交右手。
    /// <para>退化情况（想要的朝向正好与上平行，即"仰头看天"当朝向）返回 false：
    /// 这时任何切向选择都是对的，但<em>静默挑一个</em>会让角色在极区附近莫名转向。</para>
    /// </summary>
    public static bool TryCharacterBasis(in PlanetVector3 up, in PlanetVector3 desiredForward,
                                         out PlanetVector3 forward, out PlanetVector3 left, out PlanetVector3 upOut)
    {
        forward = left = upOut = PlanetVector3.Zero;
        if (!TryUp(in up, out var u)) return false;                       // 顺带把 up 归一
        if (!double.IsFinite(desiredForward.LengthSquared)) return false;

        // 把想要的朝向投到切平面上：减去径向分量
        var tangent = desiredForward - u * PlanetVector3.Dot(desiredForward, u);
        double squared = tangent.LengthSquared;
        if (!double.IsFinite(squared) || squared <= 1e-24) return false;   // 与 up 平行：切向没有意义

        forward = tangent / Math.Sqrt(squared);
        left = PlanetVector3.Cross(u, forward);
        upOut = u;
        return true;
    }

    /// <summary>
    /// 沿测地线走 <paramref name="distanceCm"/>：绕"上 × 前"这根轴旋转，
    /// 半径（= 到地心的距离，即海拔）严格保持。
    /// <para>用旋转而不是"点 + 前 × 距离"再归一化：后者每步都会把半径拉偏一点点，
    /// 走四万公里的环球时那点偏差会累积成可见的高度漂移。</para>
    /// </summary>
    public static PlanetVector3 StepAlongGeodesic(in PlanetVector3 ecefCm, in PlanetVector3 tangentForward, double distanceCm)
    {
        if (!TryUp(ecefCm, out var up))
            throw new ArgumentException($"起点不可用：{ecefCm}", nameof(ecefCm));
        if (!double.IsFinite(distanceCm))
            throw new ArgumentOutOfRangeException(nameof(distanceCm), distanceCm, "距离必须有限");

        double radius = ecefCm.Length;
        var forward = ProjectToTangent(tangentForward, up);
        // 转轴 = 上 × 前（右手：绕它旋转把"上"往"前"那边倒）
        var axisRaw = PlanetVector3.Cross(up, forward);
        double axisLength = axisRaw.Length;
        if (axisLength <= 1e-300) return ecefCm;             // 距离为 0 或 forward 与 up 同向：原地不动
        double angle = distanceCm / radius;
        return RotateVector(axisRaw / axisLength, angle, up) * radius;
    }

    /// <summary>
    /// 把切向量平行搬运到新点：沿同一段测地线旋转同样的角度。
    /// <para>测地线的切向量在平行搬运下不变，所以"前"跟着点一起转就行——
    /// 这就是走弧线时角色不会自己慢慢转向的原因（不搬的话，每步都会引入一个小的转向，
    /// 累积起来是环球一圈几十度的漂移）。</para>
    /// </summary>
    public static PlanetVector3 TransportForward(in PlanetVector3 fromEcefCm, in PlanetVector3 toEcefCm,
                                                 in PlanetVector3 tangentForward)
    {
        if (!TryUp(fromEcefCm, out var fromUp) || !TryUp(toEcefCm, out var toUp))
            throw new ArgumentException($"端点不可用：{fromEcefCm} → {toEcefCm}", nameof(toEcefCm));

        var axis = PlanetVector3.Cross(fromUp, toUp);
        double axisLength = axis.Length;
        double angle = PlanetVector3.AngleBetween(fromUp, toUp);
        if (axisLength <= 1e-300)
            return angle <= 0 ? tangentForward : -tangentForward;      // 同一点或正反对点：对点本来就不唯一
        return RotateVector(axis / axisLength, angle, tangentForward);
    }

    /// <summary>向量投影到与 up 垂直的切平面并归一；退化时返回 Zero（调用方自己判）。</summary>
    public static PlanetVector3 ProjectToTangent(in PlanetVector3 vector, in PlanetVector3 upUnit)
    {
        var tangent = vector - upUnit * PlanetVector3.Dot(vector, upUnit);
        double squared = tangent.LengthSquared;
        if (!double.IsFinite(squared) || squared <= 1e-24) return PlanetVector3.Zero;
        return tangent / Math.Sqrt(squared);
    }

    /// <summary>
    /// Rodrigues 公式：把 <paramref name="v"/> 绕单位轴 <paramref name="axisUnit"/> 转 <paramref name="angleRadians"/>。
    /// <para>轴不做归一——调用方给什么轴就按什么转，因为"轴是不是单位长"这种错
    /// 表现为一转就缩，比抛异常难查得多，所以这里当场判。</para>
    /// </summary>
    private static PlanetVector3 RotateVector(in PlanetVector3 axisUnit, double angleRadians, in PlanetVector3 v)
    {
        double deviation = Math.Abs(axisUnit.LengthSquared - 1.0);
        if (deviation > 1e-9)
            throw new ArgumentException($"旋转轴不是单位向量（偏差 {deviation:E2}）：结果会连长度一起转掉", nameof(axisUnit));
        double cos = Math.Cos(angleRadians), sin = Math.Sin(angleRadians);
        return v * cos + PlanetVector3.Cross(axisUnit, v) * sin + axisUnit * PlanetVector3.Dot(axisUnit, v) * (1 - cos);
    }
}

using System.Globalization;

namespace Horizon.Planet;

/// <summary>
/// "脚底下是不是地面"这一条判定的两端共用的容差。
/// <para>为什么要放进库而不是各端各写一个：<em>客户端抬位的门槛</em>和<em>服务端拒收的门槛</em>
/// 必须是同一个数，否则会出现一段"客户端觉得没问题、服务端判穿模"的区间——而那段区间的表现是
/// 换区被拒、客户端重新落位、再被拒。两端各写 100 与 120 这种数不会报错，只会把这条判定变成一台
/// 每秒钟敲一次的循环机。</para>
/// <para>取值 1 m 的理由：两端跑的是同一份采样代码（<see cref="PlanetHeightField"/>），
/// 正常差异只来自整数厘米的舍入，那是厘米级；1 m 已经比它宽两个数量级，又远小于一个近景 patch 的边长。</para>
/// </summary>
public static class PlanetGround
{
    public const double BelowToleranceCm = 100.0;

    /// <summary>点海拔（厘米，相对海平面）与地面海拔之间的"埋在地下多少"；负数表示在地面之上。</summary>
    public static double BelowGroundCm(double groundElevationCm, double pointElevationCm) =>
        groundElevationCm - pointElevationCm;

    /// <summary>给日志与回执用的一行：三个数都念出来，只念"埋了"读不出是数据错还是判定错。</summary>
    public static string Describe(double groundElevationCm, double pointElevationCm) =>
        string.Format(CultureInfo.InvariantCulture,
            "地面海拔 {0:0.###} m，点海拔 {1:0.###} m，埋深 {2:0.###} m，容差 {3:0.#} m",
            groundElevationCm / 100.0, pointElevationCm / 100.0,
            BelowGroundCm(groundElevationCm, pointElevationCm) / 100.0, BelowToleranceCm / 100.0);
}

using System;
using Horizon.Planet;
using Horizon.Planet.Wire;
using Xunit;

namespace Horizon.Planet.Tests;

/// <summary>
/// 引擎侧 float32 的精度预算——用来回答那条一直悬着的决定：
/// <b>到底要不要重编 Flax、开 <c>USE_LARGE_WORLDS</c>。</b>
/// <para>路线 C 的整个赌注是"引擎永远只见区域局部坐标，绝对坐标留在 double/int64 那一层"。
/// 这个赌注要么成立、要么不成立，而它此前只是被<em>假定</em>成立：文档里写着"P1 不依赖引擎重建，
/// <c>USE_LARGE_WORLDS</c> 作为 P3 的可选增强保留"，却没人量过引擎实际被要求表示的最大局部量级
/// 与那个量级上 float32 的分辨率。判据在这里把这句话变成可核对的数字。</para>
/// <para>三件事分别钉：① 常量决定的最大局部量级（含换基触发点与滞后）；② 一条<em>用 float32 累加</em>
/// 走出来的路径，端到端与 double 参照比——这才是控制器真正会做的事，理论 ulp 只是它的下界；
/// ③ 远处的壳：顶点精度取决于它相对<em>自己 tile 原点</em>的距离，与相机看到它有多远无关。</para>
/// </summary>
public class EngineFloatBudgetTests
{
    private const long Radius = PlanetConstants.PlanetRadiusCm;
    private const int Level = PlanetConstants.DefaultRegionLevel;

    /// <summary>引擎里一个 float 与它的下一个可表示值之间差多少（厘米）。</summary>
    private static double UlpCm(double magnitudeCm)
    {
        float f = (float)Math.Abs(magnitudeCm);
        if (f == 0f || float.IsInfinity(f)) return 0.0;
        return Math.Abs((double)MathF.BitIncrement(f) - f);
    }

    private static readonly RegionKey Home = new(2, Level, 8192, 8192);

    [Fact]
    public void 引擎要表示的最大局部量级_其float分辨率远小于1厘米()
    {
        // 局部坐标最大能长到多大，由这三个常量决定：近景窗口的一半、换基触发点、窗口滞后。
        // 换基排在越过 RebaseTriggerCm 之前，所以"引擎存过的最大局部值"是这三项之和。
        double halfWindow = PlanetConstants.NearPatchWindowEdge * PlanetConstants.TerrainPatchSizeCm / 2.0;
        double worst = halfWindow + PlanetConstants.RebaseTriggerCm + PlanetConstants.PatchWindowHysteresisCm;

        Assert.True(halfWindow > 0 && PlanetConstants.RebaseTriggerCm > 0);
        Assert.True(worst < 5.0e5, $"局部量级 {worst:N0} cm 已超出预期，预算表要重算（别把结论钉在自己的猜测上）");

        double ulp = UlpCm(worst);
        Assert.True(ulp <= 0.05, $"最坏量级 {worst:N0} cm 上 float32 的分辨率是 {ulp:0.####} cm，超出 1 cm 判据的预算");

        // 同一张表在 P0 那条"≤1 cm"判据的参照量级上也成立：一个 patch 顶点的局部坐标
        double patchVertex = PlanetConstants.TerrainPatchSizeCm;          // 单块 patch 内的最大局部值
        Assert.True(UlpCm(patchVertex) < ulp);
        Assert.True(UlpCm(PlanetConstants.RebaseTriggerCm / 2.0) <= 0.01);

        // 换基这一步本身不许制造误差：换基前后同一个全局点反查回的局部值差在厘米以内
        var frame = RegionFrame.Build(Home, Radius);
        var off = new PlanetVector3(halfWindow, 0, PlanetConstants.RebaseTriggerCm);
        var global = frame.GlobalFromLocalCm(off);
        var back = frame.LocalFromGlobalCm(global);
        Assert.True((back - off).Length <= 1.0, $"换基往返误差 {(back - off).Length:0.####} cm");
    }

    [Fact]
    public void 用float32累加走一圈_端到端误差仍在1厘米内()
    {
        // 控制器就是这么干每帧把 float 位移加到 float 位置上的，所以这里照它的形状走：
        // 半径取换基触发点之内的最大值（引擎真会存到这么大），步数取到一局里走得动量级。
        var frame = RegionFrame.Build(Home, Radius);
        double radiusCm = PlanetConstants.RebaseTriggerCm * 0.95;
        int steps = 3600;                                  // 半径 1.9 km 的圆走一整圈：每步约 331 cm
        double stepAngle = 2 * Math.PI / steps;

        float x = (float)radiusCm, z = 0f;                 // float32 状态：引擎侧的形状
        double gx = radiusCm, gz = 0.0;                    // double 参照
        double worstCm = 0;
        for (int i = 0; i < steps; i++)
        {
            float dx = (float)(radiusCm * (Math.Cos(stepAngle * (i + 1)) - Math.Cos(stepAngle * i)));
            float dz = (float)(radiusCm * (Math.Sin(stepAngle * (i + 1)) - Math.Sin(stepAngle * i)));
            x += dx; z += dz;                              // 引擎：float 累加
            gx += dx; gz += dz;                            // 参照：同一个增量，double 累加
            worstCm = Math.Max(worstCm, Math.Abs((double)x - gx) + Math.Abs((double)z - gz));
        }

        // 参照本身也不是真圆：这条判据比的是"float 累加比 double 累加多漂了多少"，不是"漂了多少"
        Assert.True(worstCm <= 1.0,
                    $"float32 累加 {steps} 步后与 double 相差 {worstCm:0.####} cm，超出 1 cm 判据");

        // 同一条路放回全局坐标上再看一眼：局部误差经切平面换回 ECEF 不该被放大
        var asDouble = frame.GlobalFromLocalCm(new PlanetVector3(gx, 0, gz));
        var asFloat = frame.GlobalFromLocalCm(new PlanetVector3(x, 0, z));
        double globalGap = (asFloat - asDouble).Length;
        Assert.True(globalGap <= 2.0, $"换回全局后差 {globalGap:0.####} cm（局部差 {worstCm:0.####} cm）");
    }

    [Fact]
    public void 远处的壳_顶点精度跟着tile原点走_不跟相机距离走()
    {
        // 这条是"要不要为大坐标重编引擎"最容易想错的地方：50 km 外的壳确实需要一个大局部坐标，
        // 但网格顶点存的是<em>相对本 tile 原点</em>的小值，大值只出现在 tile 的变换上。
        // 所以精度问题分成两笔，各自量：
        const double farCm = 50.0e5;                        // 50 km：§5 给地面相机远平面定的上限
        double tileHalf = PlanetConstants.TerrainPatchSizeCm / 2.0;   // 顶点相对本 tile 的量级

        // 顶点相对本 tile 原点的量级只有 ±半块 patch：它的分辨率由那个小数决定，与相机距离无关
        Assert.True(UlpCm(tileHalf) <= 0.002,
                    $"tile 局部顶点的分辨率 {UlpCm(tileHalf):0.#####} cm 已经不该由距离决定");
        double originUlp = UlpCm(farCm);
        Assert.True(originUlp <= 0.5,
                    $"50 km 处 tile 原点的分辨率 {originUlp:0.####} cm——超过半厘米就要重新考虑远平面上限");

        // 而且这一笔误差是"整块平移"，不是"顶点各自抖"：同一 tile 内任意两顶点的相对距离不受影响
        var a = new PlanetVector3(tileHalf, 0, tileHalf);
        var b = new PlanetVector3(-tileHalf, 0, tileHalf);
        double exact = (a - b).Length;
        double floored = ((new PlanetVector3((float)a.X, 0, (float)a.Z)) -
                          (new PlanetVector3((float)b.X, 0, (float)b.Z))).Length;
        Assert.True(Math.Abs(exact - floored) <= 0.01,
                    $"同一 tile 内两点相对距离被 float 化改变了 {Math.Abs(exact - floored):0.####} cm");
    }
}

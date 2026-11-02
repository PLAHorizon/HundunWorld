using System;

namespace Horizon.Planet.Dem;

/// <summary>
/// 基场的"面积平均采样器"（§13.10 路线 B）：一个查询点不取单点双线性，而是在 k×k 等角抽头上取面积加权均值。
/// <para>粗网格（中景 tile、远景球面）读细基场时，单点采样会随采样相位命中或错过山峰——山随视角忽有忽无，
/// 这就是摩尔纹。把 footprint 摊成 k×k 抽头之后，同一个 footprint 的均值与相位无关，观感稳定。</para>
/// <para>它不改变数据，也不新增产物：金字塔造不出比 level 5 更粗的级（象限 21600 = 2^5×675 的对齐闸门），
/// 所以要的"比 148 km 更粗的基场"只能在采样侧近似出来。真的需要全球尺度的山形剪影要走路线 A（按度分桶的
/// 均值/最低/最高产物），那是数据格式变更，不在这里假装解决。</para>
/// </summary>
public sealed class DemBoxAverageSource : IHeightSource
{
    /// <summary>每边抽头上限。超过它就不该继续加大这个循环，而要换成路线 A 的独立粗级产物。</summary>
    public const int MaxTapsPerEdge = 8;

    private readonly DemGrid _grid;
    private readonly int _level;
    private readonly double _stepRad;

    public DemBoxAverageSource(DemGrid grid, int tapsPerEdge)
    {
        if (grid is null) throw new ArgumentNullException(nameof(grid));
        if (tapsPerEdge < 1)
            throw new ArgumentOutOfRangeException(nameof(tapsPerEdge), tapsPerEdge, "至少 1 个抽头");
        if (tapsPerEdge > MaxTapsPerEdge)
            throw new ArgumentOutOfRangeException(nameof(tapsPerEdge), tapsPerEdge,
                $"超过 {MaxTapsPerEdge} 就该走路线 A 的粗级产物，而不是把这个循环开到几百倍");

        _grid = grid;
        _level = grid.PreferredLevel;
        _stepRad = grid.Manifest.StepDegAt(_level) * Math.PI / 180.0;
        TapsPerEdge = tapsPerEdge;
    }

    public int TapsPerEdge { get; }

    /// <summary>抽头铺的那一级的层级号（= 传入 grid 的 PreferredLevel）。</summary>
    public int Level => _level;

    /// <summary>footprint 的角宽（度）：k 个像元。</summary>
    public double FootprintDeg => TapsPerEdge * _grid.Manifest.StepDegAt(_level);

    public int SourceVersion => _grid.SourceVersion;

    /// <summary>给日志与判据用的一行自述：级、抽头、footprint，让"到底平均了多大一片"可见。</summary>
    public string Describe() => string.Format(System.Globalization.CultureInfo.InvariantCulture,
        "box_avg level={0} taps={1}x{1} footprint_deg={2:F4}", Level, TapsPerEdge, FootprintDeg);

    /// <summary>
    /// 面积加权均值。累加顺序固定（先纬后经），任何平台上同一个方向给同一个数；
    /// <c>k = 1</c> 时直接交给 <see cref="DemGrid"/>，保证退化档与逐点采样同值。
    /// </summary>
    public double SampleElevationCm(in PlanetVector3 unitDirection)
    {
        Geodetic.LatLonFromDirection(unitDirection, out double latRad, out double lonRad);

        int k = TapsPerEdge;
        if (k == 1) return _grid.SampleElevationCmAt(_level, latRad, lonRad);

        const double halfPi = Math.PI * 0.5;
        double half = (k - 1) * 0.5 * _stepRad;
        double weightSum = 0, valueSum = 0;

        for (int i = 0; i < k; i++)
        {
            double lat = latRad - half + i * _stepRad;
            if (lat > halfPi) lat = halfPi;              // 越极的抽头钳到极点：不许把 footprint 甩出球面
            else if (lat < -halfPi) lat = -halfPi;

            // 等角格的实心角正比于 cos(纬度)；不加权的话高纬抽头会替极地"多报票数"
            double w = Math.Cos(lat);
            if (w <= 0.0) continue;

            for (int j = 0; j < k; j++)
            {
                double lon = NormalizeLongitude(lonRad - half + j * _stepRad);
                double v = _grid.SampleElevationCmAt(_level, lat, lon);
                weightSum += w;
                valueSum += w * v;
            }
        }

        if (weightSum <= 0.0) return _grid.SampleElevationCmAt(_level, latRad, lonRad);
        return valueSum / weightSum;
    }

    private static double NormalizeLongitude(double lonRadians)
    {
        double deg = lonRadians * 180.0 / Math.PI;
        deg -= Math.Floor((deg + 180.0) / 360.0) * 360.0;
        return deg * Math.PI / 180.0;
    }
}

/// <summary>一次 LOD 采样计划的自述：选了哪一级、每边铺几个抽头、覆盖到没有。</summary>
public readonly struct LodSamplePlan
{
    public int DemLevel { get; init; }
    public int TapsPerEdge { get; init; }
    public double CellWidthCm { get; init; }
    public double FootprintCm { get; init; }
    public double RequestedSpacingCm { get; init; }

    /// <summary>抽头数被上限夹住 = 这个 footprint 还没罩住一个网格单元，欠采样仍在，只是被摊平了。</summary>
    public bool FullyCovered { get; init; }

    public override string ToString() => string.Format(System.Globalization.CultureInfo.InvariantCulture,
        "plan level={0} taps={1} cell={2:F1} km footprint={3:F1} km spacing={4:F1} km covered={5}",
        DemLevel, TapsPerEdge, CellWidthCm / 100000.0, FootprintCm / 100000.0,
        RequestedSpacingCm / 100000.0, FullyCovered);
}

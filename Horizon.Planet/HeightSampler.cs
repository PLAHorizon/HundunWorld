using System;
using Horizon.Planet.Dem;

namespace Horizon.Planet;

/// <summary>
/// 逐顶点采样时的模板缓存器。
/// <para>一个 508 m 的 patch 只跨约 1/9 个基场像元（4.63 km），逐顶点走 <see cref="DemGrid.ReadCell"/>
/// 会把加锁随机 IO 放大到 25 万次/patch（实测陆地 1.8 s/patch，不可用）。这里缓存 5×5 模板，
/// 落在缓存外或模板含 nodata 的点整点委托回 <see cref="DemGrid"/>，保证两条路径逐字同值。</para>
/// </summary>
public sealed class HeightSampler
{
    private const int Window = 5;                           // 双三次 4×4 + 一行余量

    private readonly DemGrid _grid;
    private readonly HybridHeightSource? _hybrid;
    private readonly DemLevel _level;
    private readonly short[,] _cache = new short[Window, Window];
    private int _originRow = int.MinValue;
    private int _originCol = int.MinValue;

    /// <summary>走了窗口缓存的次数（判据要能看见快速路径真的被跑到）。</summary>
    public long CachedSamples { get; private set; }

    /// <summary>整点委托回 DemGrid 的次数（极边、跨窗口、模板含 nodata）。</summary>
    public long DelegatedSamples { get; private set; }

    public HeightSampler(DemGrid grid, HybridHeightSource? hybrid = null)
    {
        _grid = grid ?? throw new ArgumentNullException(nameof(grid));
        if (hybrid is not null && !ReferenceEquals(hybrid.BaseField, grid))
            throw new ArgumentException("hybrid 的基场必须是同一个 grid", nameof(hybrid));
        _hybrid = hybrid;
        _level = grid.Manifest.Finest;
    }

    public DemGrid Grid => _grid;
    public HybridHeightSource? Hybrid => _hybrid;

    /// <summary>基场高程（厘米），不含细节层。</summary>
    public double BaseElevationCm(in PlanetVector3 unitDirection)
    {
        Geodetic.LatLonFromDirection(unitDirection, out double latRad, out double lonRad);
        double latDeg = latRad * 180.0 / Math.PI;
        double lonDeg = NormalizeLongitude(lonRad * 180.0 / Math.PI);

        double step = _grid.Manifest.StepDegAt(_level.Level);
        double x = (lonDeg + 180.0) / step - 0.5;           // 角点配准 → 格心坐标
        double y = (90.0 - latDeg) / step - 0.5;
        long col0 = (long)Math.Floor(x);
        long row0 = (long)Math.Floor(y);
        double fx = x - col0;
        double fy = y - row0;

        bool catmull = _hybrid is null || _hybrid.Profile.UseCatmullRom;
        int rows = _level.Rows;

        // 窗口左上角 = (row0-1, col0-1)，5×5 同时罩住双三次的 4×4 与双线性的 2×2
        if (row0 >= 1 && row0 + 3 < rows && TryFillWindow(row0 - 1, col0 - 1))
        {
            double weight = 0;
            double total = 0;
            bool hasNodata = false;

            if (catmull)
            {
                double[] wy = DemGrid.CatmullRomWeights(fy);
                double[] wx = DemGrid.CatmullRomWeights(fx);
                for (int iy = 0; iy < 4 && !hasNodata; iy++)
                {
                    for (int ix = 0; ix < 4; ix++)
                    {
                        short v = _cache[iy, ix];           // 抽头 (row0-1+iy, col0-1+ix)
                        if (v == _grid.Manifest.Nodata) { hasNodata = true; break; }
                        // 与 DemGrid.SampleBicubicCmAt 同结合序、同样不归一：那里是 Σwy·wx·v 再 ×100
                        total += wy[iy] * wx[ix] * v;
                    }
                }
            }
            else
            {
                for (int dr = 0; dr < 2 && !hasNodata; dr++)
                {
                    for (int dc = 0; dc < 2; dc++)
                    {
                        short v = _cache[1 + dr, 1 + dc];   // 抽头 (row0+dr, col0+dc)
                        if (v == _grid.Manifest.Nodata) { hasNodata = true; break; }
                        double w = (dr == 0 ? 1 - fy : fy) * (dc == 0 ? 1 - fx : fx);
                        weight += w;
                        total += w * v;
                    }
                }
            }

            if (!hasNodata && (catmull || Math.Abs(weight - 1.0) < 1e-12))
            {
                CachedSamples++;
                return catmull ? total * 100.0 : total / weight * 100.0;
            }
        }

        // 缓存不可用（极边、跨窗口、模板含 nodata）：整点交回 DemGrid，语义与权重都由它统一
        DelegatedSamples++;
        return catmull
            ? _grid.SampleBicubicCmAt(_level.Level, latRad, lonRad)
            : _grid.SampleElevationCmAt(_level.Level, latRad, lonRad);
    }

    /// <summary>完整高度：基场 × baseGain + 细节层；口径与 <see cref="HybridHeightSource.SampleElevationCm"/> 一致。</summary>
    public double SampleElevationCm(in PlanetVector3 unitDirection)
    {
        if (_hybrid is null) return BaseElevationCm(unitDirection);

        double baseCm = BaseElevationCm(unitDirection);
        double detailCm = _hybrid.Profile.DetailUnderWater || baseCm >= -_hybrid.Profile.IntertidalBandCm
            ? _hybrid.SampleDetailCm(unitDirection * _hybrid.Profile.RadiusCm)
            : 0.0;
        return _hybrid.Profile.BaseGain * baseCm + detailCm;
    }

    private static double NormalizeLongitude(double lonDeg)
    {
        lonDeg -= Math.Floor((lonDeg + 180.0) / 360.0) * 360.0;
        return lonDeg;
    }

    /// <summary>把以 (topRow, leftCol) 为左上角的 5×5 模板读进缓存；列向环绕由 DemGrid 负责。</summary>
    private bool TryFillWindow(long topRow, long leftCol)
    {
        if (topRow + Window > _level.Rows) return false;

        int rows = _level.Rows;
        if (topRow != _originRow || leftCol != _originCol)
        {
            if (topRow < 0 || topRow + Window > rows) return false;
            // 一片 5×5 一次读进来。过去这里是 25 次 ReadCell，每次都可能拉一整块 256×256=131 KB：
            // 真实 15″ 产物上实测一次点取样 6.94 ms，几乎全是取块的开销（见 DemGrid.ReadStencil）。
            // 环绕与逐格语义没变，所以这条快路与整点委托回 DemGrid 的那条仍然逐位同值。
            var stencil = _grid.ReadStencil(_level.Level, (int)topRow, leftCol, Window, Window);
            for (int r = 0; r < Window; r++)
                for (int c = 0; c < Window; c++)
                    _cache[r, c] = stencil[r * Window + c];
            _originRow = (int)topRow;
            _originCol = (int)leftCol;
        }
        return true;
    }
}

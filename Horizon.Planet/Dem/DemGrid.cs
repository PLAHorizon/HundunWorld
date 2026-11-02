using System;
using System.Collections.Generic;
using System.IO;

namespace Horizon.Planet.Dem;

/// <summary>
/// 分级高程网格的只读采样器：按格心索引、列向环绕、行向夹紧、nodata 加权。
/// <para>约定与 Tools/gebco_build_dem_pyramid.py 一致，任何一侧改动都要同步另一侧并升 manifest.format。</para>
/// </summary>
public sealed class DemGrid : IHeightSource, IDisposable
{
    /// <summary>缓存块每边采样数。</summary>
    public const int BlockSamples = 256;

    /// <summary>潮下判定的兜底半径（格）。</summary>
    private const int NearestScanRadius = 4;

    private readonly DemManifest _manifest;
    private readonly Dictionary<int, FileStream> _streams = new();
    private readonly Dictionary<(int Level, int BlockY, int BlockX), CachedBlock> _blocks = new();
    private readonly object _gate = new();
    private long _tick;

    public DemGrid(DemManifest manifest, int preferredLevel = -1, int maxCachedBlocks = 32)
    {
        _manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        if (maxCachedBlocks < 1) throw new ArgumentOutOfRangeException(nameof(maxCachedBlocks));
        MaxCachedBlocks = maxCachedBlocks;
        PreferredLevel = preferredLevel < 0 ? manifest.Finest.Level : preferredLevel;
        if (!_manifest.TryGetLevel(PreferredLevel, out _))
            throw new ArgumentException($"manifest 里没有 level {PreferredLevel}");
    }

    public DemManifest Manifest => _manifest;
    public int PreferredLevel { get; }
    public int MaxCachedBlocks { get; }

    /// <summary>首选层级是否就是最细一级；近景烘焙必须走它。</summary>
    public bool IsFinestLevel => PreferredLevel == _manifest.Finest.Level;

    /// <summary>四邻全 nodata 时的返回值（厘米）。默认按海平面。</summary>
    public double NodataFallbackCm { get; set; }

    /// <summary>进协议/缓存键的数据版本号。</summary>
    public int SourceVersion => _manifest.Format;

    public long ContentSeed => _manifest.ContentSeed;

    public double SampleElevationCm(in PlanetVector3 unitDirection)
    {
        Geodetic.LatLonFromDirection(unitDirection, out double latRad, out double lonRad);
        return SampleElevationCmAt(PreferredLevel, latRad, lonRad);
    }

    public double SampleElevationCmByLatLon(double latRadians, double lonRadians) =>
        SampleElevationCmAt(PreferredLevel, latRadians, lonRadians);

    /// <summary>指定层级采样（远景 LOD 用粗级，近景用细级），返回相对海平面的厘米高程。</summary>
    public double SampleElevationCmAt(int level, double latRadians, double lonRadians)
    {
        if (!TryMap(level, latRadians, lonRadians, out var info, out long row0, out long col0, out double fx, out double fy))
            return NodataFallbackCm;

        double weightSum = 0;
        double valueSum = 0;
        for (int dr = 0; dr < 2; dr++)
        {
            long row = row0 + dr;
            if (row < 0 || row >= info.Rows) continue;         // 极边：丢弃越界抽头并按剩余权重归一
            double wr = dr == 0 ? 1.0 - fy : fy;
            for (int dc = 0; dc < 2; dc++)
            {
                double wc = dc == 0 ? 1.0 - fx : fx;
                double w = wr * wc;
                if (w <= 0.0) continue;                        // 正落在别的格心上：抽头可用但权重为 0
                short v = ReadCell(level, (int)row, WrapCol(col0 + dc, info.Cols));
                if (v == _manifest.Nodata) continue;
                weightSum += w;
                valueSum += w * v;
            }
        }

        if (weightSum > 0.0) return valueSum / weightSum * 100.0;   // 米 → 厘米

        // 四个抽头全 nodata（或正落在 nodata 格心上）：向外找最近有效格，避免整块塌陷成海平面
        int baseRow = (int)Math.Clamp(row0, 0, info.Rows - 1);
        int baseCol = WrapCol(col0, info.Cols);
        if (TryFindNearestValid(level, info, baseRow, baseCol, out short nearest))
            return nearest * 100.0;

        return NodataFallbackCm;
    }

    /// <summary>
    /// 双三次 Catmull-Rom（4×4 抽头）。近景 patch 烘焙用它：
    /// 双线性在 4.63 km 的海岸格上会切出菱形折痕，Catmull-Rom 过样条且对线性场与双线性同值。
    /// <para>任一模板格是 nodata 就整点退回双线性——混合权重没有"忽略一角"的正当定义，宁可降级也不要瞎补。</para>
    /// </summary>
    public double SampleBicubicCmAt(int level, double latRadians, double lonRadians)
    {
        if (!TryMap(level, latRadians, lonRadians, out var info, out long row0, out long col0, out double fx, out double fy))
            return NodataFallbackCm;

        double[] wy = CatmullRomWeights(fy);
        double[] wx = CatmullRomWeights(fx);
        double sum = 0;
        for (int iy = 0; iy < 4; iy++)
        {
            long row = row0 - 1 + iy;
            if (row < 0) row = 0;
            if (row >= info.Rows) row = info.Rows - 1;          // 极边：复制边界行
            for (int ix = 0; ix < 4; ix++)
            {
                short v = ReadCell(level, (int)row, WrapCol(col0 - 1 + ix, info.Cols));
                if (v == _manifest.Nodata)
                    return SampleElevationCmAt(level, latRadians, lonRadians);
                sum += wy[iy] * wx[ix] * v;
            }
        }
        return sum * 100.0;
    }

    /// <summary>首选层级上的双三次采样（接口默认路径）。</summary>
    public double SampleBicubicCm(in PlanetVector3 unitDirection)
    {
        Geodetic.LatLonFromDirection(unitDirection, out double latRad, out double lonRad);
        return SampleBicubicCmAt(PreferredLevel, latRad, lonRad);
    }

    /// <summary>经纬度 → 该级的整数锚点与小数权重；方向退化（零向量）时返回 false。</summary>
    private bool TryMap(int level, double latRadians, double lonRadians, out DemLevel info,
                        out long row0, out long col0, out double fx, out double fy)
    {
        info = null!;
        row0 = col0 = 0;
        fx = fy = 0;
        if (!_manifest.TryGetLevel(level, out info))
            throw new ArgumentException($"没有 level {level}", nameof(level));

        double latDeg = latRadians * 180.0 / Math.PI;
        double lonDeg = lonRadians * 180.0 / Math.PI;
        // 经度折回 [-180, 180)；纬度夹紧到 [-90, 90]
        lonDeg -= Math.Floor((lonDeg + 180.0) / 360.0) * 360.0;
        if (latDeg > 90.0) latDeg = 90.0;
        else if (latDeg < -90.0) latDeg = -90.0;

        double step = _manifest.StepDegAt(level);
        double x = (lonDeg + 180.0) / step - 0.5;   // 角点配准 → 格心坐标
        double y = (90.0 - latDeg) / step - 0.5;

        row0 = (long)Math.Floor(y);
        col0 = (long)Math.Floor(x);
        fx = x - col0;
        fy = y - row0;
        return true;
    }

    private static int WrapCol(long col, int cols)
    {
        long wrapped = col % cols;
        if (wrapped < 0) wrapped += cols;
        return (int)wrapped;
    }

    /// <summary>Catmull-Rom 基函数：和恒为 1，且对线性与三次场都能精确复现。</summary>
    public static double[] CatmullRomWeights(double t)
    {
        double t2 = t * t;
        double t3 = t2 * t;
        return new[]
        {
            -0.5 * t3 + t2 - 0.5 * t,
            1.5 * t3 - 2.5 * t2 + 1.0,
            -1.5 * t3 + 2.0 * t2 + 0.5 * t,
            0.5 * t3 - 0.5 * t2,
        };
    }

    /// <summary>直接读一个格子（带 LRU 块缓存）。测试与对账用。</summary>
    public short ReadCell(int level, int row, int col)
    {
        if (!_manifest.TryGetLevel(level, out var info))
            throw new ArgumentException($"没有 level {level}", nameof(level));
        if (row < 0 || row >= info.Rows) throw new ArgumentOutOfRangeException(nameof(row));
        if (col < 0 || col >= info.Cols) throw new ArgumentOutOfRangeException(nameof(col));

        int blockY = row / BlockSamples;
        int blockX = col / BlockSamples;
        lock (_gate)
        {
            var key = (level, blockY, blockX);
            if (!_blocks.TryGetValue(key, out var block))
            {
                block = new CachedBlock(LoadBlock(info, blockY, blockX), _tick++);
                _blocks[key] = block;
                EvictIfNeeded();
            }
            else
            {
                block.Stamp = _tick++;
            }

            int width = Math.Min(BlockSamples, info.Cols - blockX * BlockSamples);
            return block.Data[(row - blockY * BlockSamples) * width + (col - blockX * BlockSamples)];
        }
    }

    private bool TryFindNearestValid(int level, DemLevel info, int row, int col, out short value)
    {
        value = 0;
        double bestDistance = double.MaxValue;
        bool found = false;
        for (int dr = -NearestScanRadius; dr <= NearestScanRadius; dr++)
        {
            int r = row + dr;
            if (r < 0 || r >= info.Rows) continue;
            for (int dc = -NearestScanRadius; dc <= NearestScanRadius; dc++)
            {
                if (dr == 0 && dc == 0) continue;
                short v = ReadCell(level, r, WrapCol((long)col + dc, info.Cols));
                if (v == _manifest.Nodata) continue;
                double distance = dr * (double)dr + dc * (double)dc;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    value = v;
                    found = true;
                }
            }
        }
        return found;
    }

    private short[] LoadBlock(DemLevel info, int blockY, int blockX)
    {
        int firstRow = blockY * BlockSamples;
        int rows = Math.Min(BlockSamples, info.Rows - firstRow);
        int firstCol = blockX * BlockSamples;
        int cols = Math.Min(BlockSamples, info.Cols - firstCol);

        var stream = OpenStream(info);
        var data = new short[rows * cols];
        var buffer = new byte[cols * 2];
        for (int r = 0; r < rows; r++)
        {
            long offset = ((long)(firstRow + r) * info.Cols + firstCol) * 2;
            stream.Seek(offset, SeekOrigin.Begin);
            int read = 0;
            while (read < buffer.Length)
            {
                int got = stream.Read(buffer, read, buffer.Length - read);
                if (got <= 0) break;
                read += got;
            }
            if (read != buffer.Length)
                throw new EndOfStreamException($"level {info.Level} 读 (row={firstRow + r}, col={firstCol}) 只有 {read}/{buffer.Length} 字节");
            for (int c = 0; c < cols; c++)
                data[r * cols + c] = (short)(buffer[c * 2] | (buffer[c * 2 + 1] << 8));
        }
        return data;
    }

    private FileStream OpenStream(DemLevel info)
    {
        if (_streams.TryGetValue(info.Level, out var existing)) return existing;
        string path = Path.Combine(_manifest.RootDirectory, info.File);
        var created = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.RandomAccess);
        _streams[info.Level] = created;
        return created;
    }

    private void EvictIfNeeded()
    {
        if (_blocks.Count <= MaxCachedBlocks) return;
        (int, int, int) victim = default;
        long oldest = long.MaxValue;
        foreach (var pair in _blocks)
        {
            if (pair.Value.Stamp >= oldest) continue;
            oldest = pair.Value.Stamp;
            victim = pair.Key;
        }
        _blocks.Remove(victim);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var stream in _streams.Values) stream.Dispose();
            _streams.Clear();
            _blocks.Clear();
        }
    }

    /// <summary>当前驻留的块数（缓存上限判据用）。</summary>
    public int CachedBlockCount
    {
        get { lock (_gate) return _blocks.Count; }
    }

    /// <summary>列出 manifest 里可用的层级（升序）。</summary>
    public IEnumerable<int> AvailableLevels()
    {
        foreach (var level in _manifest.Levels) yield return level.Level;
    }

    private sealed class CachedBlock
    {
        public CachedBlock(short[] data, long stamp) { Data = data; Stamp = stamp; }
        public short[] Data { get; }
        public long Stamp { get; set; }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace Horizon.Planet.Dem;

/// <summary>某一级高程网格的元数据。</summary>
public sealed class DemLevel
{
    public int Level { get; set; }
    public int Cols { get; set; }
    public int Rows { get; set; }
    public string File { get; set; } = "";
    public long Bytes { get; set; }
    public long Crc32 { get; set; }
}

/// <summary>
/// Tools/gebco_build_dem_pyramid.py 写出的 manifest.json 的服务端/客户端可读视图。
/// <para>采样约定必须由这里统一：格心、row 自北向南、col 自西向东、nodata 加权。</para>
/// </summary>
public sealed class DemManifest
{
    public int Format { get; set; }
    public string SourceName { get; set; } = "";
    public string SourceSha256 { get; set; } = "";
    public double ArcSecond { get; set; }
    public int GridCols { get; set; }
    public int GridRows { get; set; }
    public double StepDeg { get; set; }
    public int Nodata { get; set; } = -32767;
    public List<DemLevel> Levels { get; set; } = new();

    /// <summary>数据根目录（manifest.json 所在处）。</summary>
    public string RootDirectory { get; private set; } = "";

    /// <summary>最细一级（level 最小）。</summary>
    public DemLevel Finest
    {
        get
        {
            DemLevel best = Levels[0];
            foreach (var l in Levels)
                if (l.Level < best.Level) best = l;
            return best;
        }
    }

    public bool TryGetLevel(int level, out DemLevel found)
    {
        found = null!;
        foreach (var l in Levels)
        {
            if (l.Level != level) continue;
            found = l;
            return true;
        }
        return false;
    }

    /// <summary>该级每格的角宽（度）。</summary>
    public double StepDegAt(int level) => StepDeg * (1 << level);

    /// <summary>握手/缓存键用的内容种子：源包 sha256 前 8 字节（大端）。</summary>
    public long ContentSeed
    {
        get
        {
            if (SourceSha256.Length < 16) return 0;
            long seed = 0;
            for (int i = 0; i < 8; i++)
                seed = (seed << 8) | byte.Parse(SourceSha256.AsSpan(i * 2, 2), NumberStyles.HexNumber);
            return seed;
        }
    }

    public static bool TryLoad(string root, out DemManifest manifest, out string error)
    {
        manifest = new DemManifest();
        string path = Path.Combine(root, "manifest.json");
        if (!File.Exists(path))
        {
            error = "manifest.json 不存在：" + path + "（先跑 Tools/gebco_build_dem_pyramid.py --levels 0-5）";
            return false;
        }

        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            using var stream = File.OpenRead(path);
            var raw = JsonSerializer.Deserialize<Wire>(stream, options);
            if (raw is null)
            {
                error = "manifest.json 解析为空";
                return false;
            }

            manifest.Format = raw.Format;
            manifest.SourceName = raw.Source?.Name ?? "";
            manifest.SourceSha256 = raw.Source?.Sha256 ?? "";
            manifest.ArcSecond = raw.Source?.ArcSecond ?? 0;
            manifest.GridCols = raw.Grid?.Cols ?? 0;
            manifest.GridRows = raw.Grid?.Rows ?? 0;
            manifest.StepDeg = raw.Grid?.StepDeg ?? 0;
            manifest.Nodata = raw.Grid?.Nodata ?? -32767;
            manifest.RootDirectory = root;
            manifest.Levels = raw.Levels ?? new List<DemLevel>();

            if (manifest.Levels.Count == 0)
            {
                error = "manifest.json 没有任何 level";
                return false;
            }
            if (manifest.GridCols <= 0 || manifest.GridRows <= 0 || manifest.StepDeg <= 0)
            {
                error = "manifest.json 的 grid 参数非法";
                return false;
            }

            foreach (var level in manifest.Levels)
            {
                string file = Path.Combine(root, level.File);
                if (!File.Exists(file))
                {
                    error = $"level {level.Level} 的数据文件不存在：{file}";
                    return false;
                }
                long size = new FileInfo(file).Length;
                if (size != level.Bytes)
                {
                    error = $"level {level.Level} 字节数 {size} != manifest {level.Bytes}";
                    return false;
                }
            }

            error = "";
            return true;
        }
        catch (Exception ex)
        {
            error = "manifest.json 读取失败：" + ex.Message;
            return false;
        }
    }

    private sealed class Wire
    {
        public int Format { get; set; }
        public SourceInfo? Source { get; set; }
        public GridInfo? Grid { get; set; }
        public List<DemLevel>? Levels { get; set; }
    }

    private sealed class SourceInfo
    {
        public string? Name { get; set; }
        public string? Sha256 { get; set; }
        public double ArcSecond { get; set; }
    }

    private sealed class GridInfo
    {
        public int Cols { get; set; }
        public int Rows { get; set; }
        public double StepDeg { get; set; }
        public int Nodata { get; set; }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Horizon.Planet;
using Horizon.Planet.Dem;
using Xunit;

namespace Horizon.Planet.Tests;

/// <summary>临时 DEM 目录：写出 manifest.json + 各级 int16 文件，供读取器测试。</summary>
internal sealed class TempDem : IDisposable
{
    private const int Nodata = -32767;

    public string Root { get; }
    public string Sha256 { get; }

    private TempDem(string root, string sha256)
    {
        Root = root;
        Sha256 = sha256;
    }

    public static TempDem Create(double stepDeg0, params (int Level, short?[,] Grid)[] levels)
    {
        string root = Path.Combine(Path.GetTempPath(), "hundun_dem_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        const string sha = "39bb6eb45a785101da606749a00cf0ec3217f32160d25859b48fd5db9c6d9368";
        var dem = new TempDem(root, sha);

        var entries = new List<object>();
        foreach (var (level, grid) in levels)
        {
            int rows = grid.GetLength(0);
            int cols = grid.GetLength(1);
            string file = $"level_{level:00}.bin";
            var bytes = new byte[rows * cols * 2];
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    short v = grid[r, c] ?? 0;
                    int i = (r * cols + c) * 2;
                    bytes[i] = (byte)(v & 0xFF);
                    bytes[i + 1] = (byte)((v >> 8) & 0xFF);
                }
            }
            File.WriteAllBytes(Path.Combine(root, file), bytes);
            entries.Add(new
            {
                level,
                cols,
                rows,
                file,
                bytes = (long)bytes.Length,
                crc32 = 0L,
            });
        }

        var manifest = new
        {
            format = 1,
            source = new { name = "temp fixture", sha256 = sha, arcSecond = stepDeg0 * 3600.0 },
            grid = new
            {
                cols = levels.Length > 0 ? levels[0].Grid.GetLength(1) : 0,
                rows = levels.Length > 0 ? levels[0].Grid.GetLength(0) : 0,
                stepDeg = stepDeg0,
                sampleConvention = "cell-centre",
                rowOrder = "north->south",
                colOrder = "west->east",
                nodata = Nodata,
                dtype = "int16le",
                unit = "metre",
            },
            levels = entries,
        };
        File.WriteAllText(Path.Combine(root, "manifest.json"),
                          JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        return dem;
    }

    public DemManifest Load()
    {
        Assert.True(DemManifest.TryLoad(Root, out var manifest, out string error), "manifest 应当能加载：" + error);
        return manifest;
    }

    public void Dispose()
    {
        try { Directory.Delete(Root, true); } catch (IOException) { /* 临时目录留给系统回收 */ }
    }
}

public class DemGridTests
{
    private const int Nodata = -32767;

    /// <summary>8×4 的线性场：v(r,c) = 10c + r（米），每格 45°。</summary>
    private static short?[,] LinearGrid()
    {
        var g = new short?[4, 8];
        for (int r = 0; r < 4; r++)
            for (int c = 0; c < 8; c++)
                g[r, c] = (short)(10 * c + r);
        return g;
    }

    /// <summary>4×2 的粗级场，刻意与细级不同数值，用来证明"读到的是哪一级"。</summary>
    private static short?[,] CoarseGrid()
    {
        var g = new short?[2, 4];
        for (int r = 0; r < 2; r++)
            for (int c = 0; c < 4; c++)
                g[r, c] = (short)(1000 + 100 * c + r);
        return g;
    }

    private static double LatCentre(int row, int level, double stepDeg) =>
        90.0 - (row + 0.5) * stepDeg * (1 << level);

    private static double LonCentre(int col, int level, double stepDeg) =>
        -180.0 + (col + 0.5) * stepDeg * (1 << level);

    [Fact]
    public void CellCentre_ReturnsThatCellExactly()
    {
        using var dem = TempDem.Create(45.0, (0, LinearGrid()), (1, CoarseGrid()));
        using var grid = new DemGrid(dem.Load());

        for (int r = 0; r < 4; r++)
        {
            for (int c = 0; c < 8; c++)
            {
                double lat = LatCentre(r, 0, 45.0) * Math.PI / 180.0;
                double lon = LonCentre(c, 0, 45.0) * Math.PI / 180.0;
                double cm = grid.SampleElevationCmAt(0, lat, lon);
                Assert.Equal((10 * c + r) * 100.0, cm, 6);
            }
        }
    }

    [Fact]
    public void MidpointBetweenCells_IsTheirAverage()
    {
        using var dem = TempDem.Create(45.0, (0, LinearGrid()));
        using var grid = new DemGrid(dem.Load());

        // 两格中心的中点 = 格边界：左右权重各半
        for (int c = 0; c < 7; c++)
        {
            double lon = LonCentre(c, 0, 45.0) + 22.5;
            double lat = LatCentre(1, 0, 45.0) * Math.PI / 180.0;
            double cm = grid.SampleElevationCmAt(0, lat, lon * Math.PI / 180.0);
            Assert.Equal(((10 * c + 1) + (10 * (c + 1) + 1)) / 2.0 * 100.0, cm, 6);
        }
    }

    [Fact]
    public void LongitudeSeam_WrapsLastAndFirstColumn()
    {
        using var dem = TempDem.Create(45.0, (0, LinearGrid()));
        using var grid = new DemGrid(dem.Load());

        // lon = −180 正好是接缝：应当混合"最后一列"和"第一列"
        for (int r = 0; r < 4; r++)
        {
            double lat = LatCentre(r, 0, 45.0) * Math.PI / 180.0;
            double lon = -Math.PI;
            double cm = grid.SampleElevationCmAt(0, lat, lon);
            Assert.Equal(((70 + r) + r) / 2.0 * 100.0, cm, 6);
        }

        // +180 与 −180 是同一条线
        double same = grid.SampleElevationCmAt(0, LatCentre(2, 0, 45.0) * Math.PI / 180.0, Math.PI);
        double wrap = grid.SampleElevationCmAt(0, LatCentre(2, 0, 45.0) * Math.PI / 180.0, -Math.PI);
        Assert.Equal(wrap, same, 6);
    }

    [Theory]
    [InlineData(90.0, 0)]
    [InlineData(-90.0, 3)]
    public void PoleEdge_ClampsToNearestRow(double latDeg, int expectedRow)
    {
        using var dem = TempDem.Create(45.0, (0, LinearGrid()));
        using var grid = new DemGrid(dem.Load());

        // 极点：只有内侧那一行参与，权重归一后等价于该行的经向插值
        for (int c = 0; c < 8; c++)
        {
            double lon = LonCentre(c, 0, 45.0) * Math.PI / 180.0;
            double cm = grid.SampleElevationCmAt(0, latDeg * Math.PI / 180.0, lon);
            Assert.Equal((10 * c + expectedRow) * 100.0, cm, 6);
        }
    }

    [Fact]
    public void CoarseLevel_IsReadFromItsOwnFile()
    {
        using var dem = TempDem.Create(45.0, (0, LinearGrid()), (1, CoarseGrid()));
        using var grid = new DemGrid(dem.Load());

        for (int r = 0; r < 2; r++)
        {
            for (int c = 0; c < 4; c++)
            {
                double lat = LatCentre(r, 1, 45.0) * Math.PI / 180.0;
                double lon = LonCentre(c, 1, 45.0) * Math.PI / 180.0;
                Assert.Equal((1000 + 100 * c + r) * 100.0, grid.SampleElevationCmAt(1, lat, lon), 6);
            }
        }

        Assert.Throws<ArgumentException>(() => grid.SampleElevationCmAt(7, 0, 0));
    }

    [Fact]
    public void PreferredLevel_DrivesTheInterfaceDefault()
    {
        using var dem = TempDem.Create(45.0, (0, LinearGrid()), (1, CoarseGrid()));
        var manifest = dem.Load();

        using (var fine = new DemGrid(manifest))
        {
            Assert.Equal(0, fine.PreferredLevel);
            double lat = LatCentre(0, 1, 45.0) * Math.PI / 180.0;
            double lon = LonCentre(0, 1, 45.0) * Math.PI / 180.0;
            // 细级在粗级格心处的值 ≠ 粗级值，说明默认走了首选层级
            Assert.NotEqual(fine.SampleElevationCmAt(1, lat, lon), fine.SampleElevationCmByLatLon(lat, lon));
        }

        using var coarse = new DemGrid(manifest, preferredLevel: 1);
        Assert.Equal(1, coarse.PreferredLevel);
        double clat = LatCentre(1, 1, 45.0) * Math.PI / 180.0;
        double clon = LonCentre(3, 1, 45.0) * Math.PI / 180.0;
        Assert.Equal((1000 + 300 + 1) * 100.0, coarse.SampleElevationCmByLatLon(clat, clon), 6);
    }

    [Fact]
    public void DirectionOverload_MatchesLatLonOverload()
    {
        using var dem = TempDem.Create(45.0, (0, LinearGrid()));
        using var grid = new DemGrid(dem.Load());
        var rng = new Random(31337);

        for (int i = 0; i < 500; i++)
        {
            double lat = (rng.NextDouble() * 2 - 1) * (Math.PI / 2);
            double lon = (rng.NextDouble() * 2 - 1) * Math.PI;
            PlanetVector3 dir = Geodetic.DirectionFromLatLon(lat, lon);
            Assert.Equal(grid.SampleElevationCmByLatLon(lat, lon), grid.SampleElevationCm(dir), 9);
        }
    }

    [Fact]
    public void NodataCell_FallsBackToNearestValidCell()
    {
        var g = new short?[4, 8];
        for (int r = 0; r < 4; r++)
            for (int c = 0; c < 8; c++)
                g[r, c] = Nodata;
        g[0, 0] = 77;

        using var dem = TempDem.Create(45.0, (0, g));
        using var grid = new DemGrid(dem.Load());

        // 采样点 (r=1,c=1) 的四邻全 nodata，唯一有效格是 (0,0)
        double lat = LatCentre(1, 0, 45.0) * Math.PI / 180.0;
        double lon = LonCentre(1, 0, 45.0) * Math.PI / 180.0;
        Assert.Equal(77 * 100.0, grid.SampleElevationCmAt(0, lat, lon), 6);
    }

    [Fact]
    public void AllNodata_UsesConfiguredFallback()
    {
        var g = new short?[4, 8];
        for (int r = 0; r < 4; r++)
            for (int c = 0; c < 8; c++)
                g[r, c] = Nodata;

        using var dem = TempDem.Create(45.0, (0, g));
        using var grid = new DemGrid(dem.Load()) { NodataFallbackCm = 1234.0 };
        Assert.Equal(1234.0, grid.SampleElevationCmAt(0, 0, 0), 9);
        Assert.Equal(0.0, new DemGrid(dem.Load()).NodataFallbackCm, 9);
    }

    [Fact]
    public void PartialNodata_RenormalizesRemainingWeights()
    {
        var g = LinearGrid();
        g[1, 3] = Nodata;   // 抹掉一格，验证权重归一

        using var dem = TempDem.Create(45.0, (0, g));
        using var grid = new DemGrid(dem.Load());

        // 该格中心：唯一正权重的抽头是 nodata → 走最近有效格。
        // 扫描顺序固定为 dr 升序、dc 升序且只在"严格更近"时替换，所以四个等距邻居里 (0,3) 先胜出。
        double lat = LatCentre(1, 0, 45.0) * Math.PI / 180.0;
        double lon = LonCentre(3, 0, 45.0) * Math.PI / 180.0;
        double atCentre = grid.SampleElevationCmAt(0, lat, lon);
        Assert.Equal((10 * 3 + 0) * 100.0, atCentre, 6);

        // 中点（该格与 (1,4) 之间）：只剩一个有效抽头 → 归一后就是它的值
        double midLon = (LonCentre(3, 0, 45.0) + LonCentre(4, 0, 45.0)) * 0.5 * Math.PI / 180.0;
        Assert.Equal((10 * 4 + 1) * 100.0, grid.SampleElevationCmAt(0, lat, midLon), 6);
    }

    [Fact]
    public void CacheEviction_KeepsValuesCorrect_AndBoundsBlockCount()
    {
        const int size = 600;      // 600 > 2×BlockSamples(256) → 会跨块，触发淘汰
        var g = new short?[size, size];
        for (int r = 0; r < size; r++)
            for (int c = 0; c < size; c++)
                g[r, c] = (short)(r + c);

        using var dem = TempDem.Create(0.05, (0, g));
        using var grid = new DemGrid(dem.Load(), maxCachedBlocks: 2);

        var sampled = new List<double>();
        for (int pass = 0; pass < 2; pass++)
        {
            for (int i = 0; i < 300; i++)
            {
                int r = i * 137 % size;      // 跨块散布，第二轮必然踩到被逐出的块
                int c = i * 211 % size;
                double lat = (90.0 - (r + 0.5) * 0.05) * Math.PI / 180.0;
                double lon = (-180.0 + (c + 0.5) * 0.05) * Math.PI / 180.0;
                double cm = grid.SampleElevationCmAt(0, lat, lon);
                Assert.Equal((r + c) * 100.0, cm, 6);
                sampled.Add(cm);
            }
        }

        Assert.True(grid.CachedBlockCount <= 2, $"块缓存没被限住：{grid.CachedBlockCount}");
        for (int i = 0; i < 300; i++)
            Assert.Equal(sampled[i], sampled[i + 300], 9);
    }

    [Fact]
    public void Manifest_ReportsMissingDataWithTheToolHint()
    {
        string missing = Path.Combine(Path.GetTempPath(), "hundun_dem_missing_" + Guid.NewGuid().ToString("N"));
        Assert.False(DemManifest.TryLoad(missing, out _, out string error));
        Assert.Contains("gebco_build_dem_pyramid.py", error);

        using var dem = TempDem.Create(45.0, (0, LinearGrid()));
        var manifest = dem.Load();
        string file = Path.Combine(dem.Root, manifest.Levels[0].File);
        long shrunk = new FileInfo(file).Length - 4;
        using (var fs = new FileStream(file, FileMode.Open)) fs.SetLength(shrunk);
        Assert.False(DemManifest.TryLoad(dem.Root, out _, out string sizeError));
        Assert.Contains("字节数", sizeError);
    }

    [Fact]
    public void ContentSeed_IsFirstEightBytesOfSourceSha256()
    {
        using var dem = TempDem.Create(45.0, (0, LinearGrid()));
        var manifest = dem.Load();
        // 期望值直接按十六进制串手工拼出来，不复用实现里的循环
        long expected = ((long)0x39 << 56) | ((long)0xbb << 48) | ((long)0x6e << 40) | ((long)0xb4 << 32) |
                        ((long)0x5a << 24) | (0x78 << 16) | (0x51 << 8) | 0x01;
        Assert.Equal(expected, manifest.ContentSeed);
    }
}

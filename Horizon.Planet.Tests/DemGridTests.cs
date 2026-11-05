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
        // 最后一个 FileStream 关闭后，Windows 还会短暂占着目录：立刻删会失败，静默吞掉的结果
        // 是攒出一堆看不见的临时目录（这一轮先查出 25 个，改完重试仍每次漏 1 个）。
        // 进程一退出就能删 ⇒ 是释放时机问题，所以给足重试窗口，而不是把失败咽下去。
        for (int attempt = 0; attempt < 8; attempt++)
        {
            try { Directory.Delete(Root, true); return; }
            catch (IOException) { System.Threading.Thread.Sleep(50); }
            catch (UnauthorizedAccessException) { System.Threading.Thread.Sleep(50); }
        }
        try { Directory.Delete(Root, true); }
        catch (Exception e) { Console.Error.WriteLine($"临时夹具目录没清掉：{Root}（{e.GetType().Name}）"); }
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
        // 第二个 grid 也必须用完就关：句柄还开着，TempDem 删临时目录就会失败（本轮那个残留就是这么来的）
        using var defaulted = new DemGrid(dem.Load());
        Assert.Equal(0.0, defaulted.NodataFallbackCm, 9);
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

        // 块缓存现在只有 ReadCell 这一条路会填（插值改走片读之后），所以这半条用例直接驱动 ReadCell：
        // 改成用 SampleElevationCmAt 去驱动的话，CachedBlockCount 恒为 0，"≤2" 就成了永远成立的空断言。
        for (int i = 0; i < 300; i++)
        {
            grid.ReadCell(0, i * 137 % size, i * 211 % size);
        }
        Assert.True(grid.CachedBlockCount > 0, "ReadCell 一个块都没装进来：这条用例的缓存那半是空转");
        Assert.True(grid.CachedBlockCount <= 2, $"块缓存没被限住：{grid.CachedBlockCount}");
        for (int i = 0; i < 300; i++)
            Assert.Equal(sampled[i], sampled[i + 300], 9);
    }

    [Fact]
    public void 点取样走片读不再填块缓存()
    {
        // 这条钉的是 §13.60 那次提速<em>本身</em>：真实 15″ 产物上一次点取样 6.94 ms，
        // 几乎全是 256×256=131 KB 的取块开销。插值改走片读之后，块缓存只剩 ReadCell 一条路会填。
        // 有人把抽头改回 ReadCell 时这条会红——那正是把 6.9 ms 请回来的动作。
        const int size = 600;
        var g = new short?[size, size];
        for (int r = 0; r < size; r++)
            for (int c = 0; c < size; c++)
                g[r, c] = (short)(r + c);

        using var dem = TempDem.Create(0.05, (0, g));
        using var grid = new DemGrid(dem.Load());

        for (int i = 0; i < 200; i++)
        {
            int r = i * 137 % size;
            int c = i * 211 % size;
            grid.SampleElevationCmAt(0, (90.0 - (r + 0.5) * 0.05) * Math.PI / 180.0,
                                        (-180.0 + (c + 0.5) * 0.05) * Math.PI / 180.0);
            grid.SampleBicubicCmAt(0, (90.0 - (r + 0.7) * 0.05) * Math.PI / 180.0,
                                      (-180.0 + (c + 0.3) * 0.05) * Math.PI / 180.0);
        }
        Assert.Equal(0, grid.CachedBlockCount);

        // 反向对照（同夹具）：ReadCell 确实会往缓存里装块，所以上面那个 0 不是计数器坏了
        grid.ReadCell(0, 10, 10);
        Assert.Equal(1, grid.CachedBlockCount);
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

using System;
using System.Buffers.Binary;
using System.IO;
using Horizon.Planet;
using Horizon.Planet.Dem;
using Xunit;

namespace Horizon.Planet.Tests;

/// <summary>
/// <see cref="DemGrid.ReadStencil"/> 与两条插值入口的<em>独立参照</em>判据。
/// <para>为什么另写一份：<c>HeightSamplerTests</c> 那几条比的是"窗口缓存 vs 逐点委托"，
/// 而这两条现在<em>都</em>走片读了——片读里错一格，两边一起错，那条等值判据就成了摆设。
/// 这里的参照不经过库里任何一个读函数：按 manifest 声明的格式
/// （int16le、行主序、north→south、west→east、格心配准）直接从字节偏移取数，
/// 权重用 Catmull–Rom 的标准展开独立写一遍，再把库给的数对上。</para>
/// <para>选点刻意压在三种边界上：跨经度接缝的抽头（左邻要绕到 Cols−1）、
/// 贴北端的行（row0−1 越界）、贴南端的行（row0+2 越界）——片读的行/列索引最可能在这三处差一格。</para>
/// </summary>
public class DemStencilTests
{
    private const int Rows = 128;
    private const int Cols = 256;

    /// <summary>
    /// 像元角度：<b>360/Cols = 180/Rows = 1.40625°</b>。写成 1.0° 是个陷阱（第一版就踩了）：
    /// 那样 256 列只盖住 256° 经度，于是"接缝那侧的经度"会被折回网格中部，
    /// 这条用例以为自己测了环绕，其实测的是另一个格。
    /// </summary>
    private const double StepDeg = 360.0 / Cols;

    /// <summary>场值 v(r,c) = 3r + 7c：行与列的系数不同，任何一处行/列错位都会显出来。</summary>
    private static int ValueAt(int r, int c) => 3 * r + 7 * c;

    private static short?[,] Field()
    {
        var g = new short?[Rows, Cols];
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Cols; c++)
                g[r, c] = (short)ValueAt(r, c);
        return g;
    }

    private static int Wrap(long col)
    {
        long wrapped = col % Cols;
        if (wrapped < 0) wrapped += Cols;
        return (int)wrapped;
    }

    private static string LevelPath(DemManifest manifest) =>
        Path.Combine(manifest.RootDirectory, manifest.Levels[0].File);

    /// <summary>不经过任何库内读函数：按 (row, col) 的字节偏移直接取一个格。</summary>
    private static short ReadFromBytes(Stream file, int row, int col)
    {
        file.Seek(((long)row * Cols + col) * 2, SeekOrigin.Begin);
        Span<byte> two = stackalloc byte[2];
        file.ReadExactly(two);
        return BinaryPrimitives.ReadInt16LittleEndian(two);
    }

    /// <summary>锚点 (row0,col0) 带小数偏移 (fx,fy) 处的经纬度（格心配准的那条约定）。</summary>
    private static (double LatRad, double LonRad) At(int row0, int col0, double fx, double fy)
    {
        double lonDeg = (col0 + fx + 0.5) * StepDeg - 180.0;
        double latDeg = 90.0 - (row0 + fy + 0.5) * StepDeg;
        return (latDeg * Math.PI / 180.0, lonDeg * Math.PI / 180.0);
    }

    /// <summary>
    /// 手算的双三次：抽头按字节取，行夹紧、列环绕按声明的约定做，
    /// 权重用标准基函数展开（与 <see cref="DemGrid.CatmullRomWeights"/> 无共享代码）。
    /// </summary>
    private static double ExpectedBicubic(Stream file, int row0, int col0, double fx, double fy)
    {
        double[] wy = Basis(fy), wx = Basis(fx);
        double total = 0;
        for (int iy = 0; iy < 4; iy++)
        {
            int row = Math.Clamp(row0 - 1 + iy, 0, Rows - 1);
            for (int ix = 0; ix < 4; ix++)
            {
                int col = Wrap(col0 - 1 + ix);
                total += wy[iy] * wx[ix] * ReadFromBytes(file, row, col);
            }
        }
        return total * 100.0;
    }

    /// <summary>手算的双线性：同样按字节取抽头，越界行丢弃并按剩余权重归一（原语义）。</summary>
    private static double ExpectedBilinear(Stream file, int row0, int col0, double fx, double fy)
    {
        double weightSum = 0, valueSum = 0;
        for (int dr = 0; dr < 2; dr++)
        {
            int row = row0 + dr;
            if (row < 0 || row >= Rows) continue;
            double wr = dr == 0 ? 1.0 - fy : fy;
            for (int dc = 0; dc < 2; dc++)
            {
                double wc = dc == 0 ? 1.0 - fx : fx;
                double w = wr * wc;
                if (w <= 0.0) continue;
                valueSum += w * ReadFromBytes(file, row, Wrap(col0 + dc));
                weightSum += w;
            }
        }
        return weightSum > 0 ? valueSum / weightSum * 100.0 : 0.0;
    }

    /// <summary>作用在 (p0,p1,p2,p3) 上的 Catmull–Rom 标准基，参数 t 从 p1 起算。</summary>
    private static double[] Basis(double t) => new[]
    {
        -0.5 * t * t * t + t * t - 0.5 * t,
        1.5 * t * t * t - 2.5 * t * t + 1.0,
        -1.5 * t * t * t + 2.0 * t * t + 0.5 * t,
        0.5 * t * t * t - 0.5 * t * t,
    };

    /// <summary>厘米级比较：手算与库内的累加顺序一致，但基函数的写法不同，留 1e-6 cm 的余地。</summary>
    private static void CloseTo(double expected, double actual, string what) =>
        Assert.True(Math.Abs(expected - actual) <= 1e-6,
                    $"{what}：手算 {expected:0.######}，库给 {actual:0.######}，差 {actual - expected:0.######} cm");

    [Fact]
    public void 片读逐格等于按字节偏移自己算()
    {
        using var dem = TempDem.Create(StepDeg, (0, Field()));
        var manifest = dem.Load();
        using var grid = new DemGrid(manifest);
        using var file = File.OpenRead(LevelPath(manifest));

        // leftCol 刻意取负数、取 Cols、取 Cols−2（跨接缝）：绕回后最多断成两段，那两段就是新代码
        var tops = new[] { 0, 1, Rows / 2, Rows - 2, Rows - 5 };
        var lefts = new long[] { 0, -3, Cols - 2, Cols, Cols + 5, -Cols };
        int checkedCells = 0;
        foreach (int top in tops)
            foreach (long left in lefts)
            {
                int rows = Math.Min(5, Rows - top);
                short[] stencil = grid.ReadStencil(0, top, left, rows, 5);
                Assert.Equal(rows * 5, stencil.Length);
                for (int r = 0; r < rows; r++)
                    for (int c = 0; c < 5; c++)
                    {
                        int wrapped = Wrap(left + c);
                        short byBytes = ReadFromBytes(file, top + r, wrapped);
                        Assert.True(stencil[r * 5 + c] == byBytes,
                            $"({top + r}, {left}+{c}→{wrapped})：片读 {stencil[r * 5 + c]}，字节读 {byBytes}");
                        Assert.Equal((short)ValueAt(top + r, wrapped), byBytes);
                        checkedCells++;
                    }
            }
        Assert.True(checkedCells > 300, $"只核了 {checkedCells} 格，覆盖面不够，这条用例在空转");
    }

    [Fact]
    public void 片读与老的ReadCell块缓存路逐格同值()
    {
        // 两条读法各自独立（一个按行片读，一个装 256×256 的块），同值才叫"换了读法没换语义"
        using var dem = TempDem.Create(StepDeg, (0, Field()));
        using var grid = new DemGrid(dem.Load());
        var rng = new Random(7);
        int compared = 0;
        for (int trial = 0; trial < 40; trial++)
        {
            int top = rng.Next(0, Rows - 4);
            long left = rng.Next(-4, Cols + 4);
            short[] stencil = grid.ReadStencil(0, top, left, 4, 4);
            for (int r = 0; r < 4; r++)
                for (int c = 0; c < 4; c++)
                {
                    Assert.Equal(grid.ReadCell(0, top + r, Wrap(left + c)), stencil[r * 4 + c]);
                    compared++;
                }
        }
        Assert.Equal(640, compared);
    }

    [Fact]
    public void 格心取样拿到的就是那个格本身()
    {
        // 格心上 fx=fy=0：双线性与双三次都应当<em>正好</em>回该格高程。
        // 这条对"抽头行/列映射"最狠：错一格，数就偏出去 3 或 7 的量级。
        using var dem = TempDem.Create(StepDeg, (0, Field()));
        var manifest = dem.Load();
        using var grid = new DemGrid(manifest);
        using var file = File.OpenRead(LevelPath(manifest));

        int probed = 0;
        foreach (var (r, c) in new[]
        {
            (64, 128), (0, 0), (0, 1), (1, 0), (Rows - 1, Cols - 1), (Rows - 1, 0),
            (0, Cols - 1), (Rows - 2, Cols - 2), (3, Cols - 1), (Rows - 1, 5),
        })
        {
            var (lat, lon) = At(r, c, 0.0, 0.0);
            double expected = ValueAt(r, c) * 100.0;
            // 经纬度往返回了一圈浮点（fx/fy 落在 1e-14 而不是精确 0），所以这里按容差比：
            // 抽头错一格的量级是 300~700 cm，1e-6 cm 的容差不会把真错放过
            CloseTo(expected, grid.SampleElevationCmAt(0, lat, lon), $"双线性格心 ({r},{c})");
            CloseTo(expected, grid.SampleBicubicCmAt(0, lat, lon), $"双三次格心 ({r},{c})");
            Assert.Equal(expected, ReadFromBytes(file, r, c) * 100.0);
            probed++;
        }
        Assert.Equal(10, probed);
    }

    [Fact]
    public void 跨经度接缝的抽头与手算一致()
    {
        // col0=−1、fx=0.8 ⇒ 四个抽头是 Cols−2, Cols−1, 0, 1：片读里"最多断两段"那条分支只有这里会走到。
        // fy=0 把行的影响摘掉，所以这条量的就是列环绕。
        using var dem = TempDem.Create(StepDeg, (0, Field()));
        var manifest = dem.Load();
        using var grid = new DemGrid(manifest);
        using var file = File.OpenRead(LevelPath(manifest));

        int row = 70;
        var (lat, lon) = At(row, -1, 0.8, 0.0);
        double expected = ExpectedBicubic(file, row, -1, 0.8, 0.0);
        CloseTo(expected, grid.SampleBicubicCmAt(0, lat, lon), "跨接缝双三次");

        // 同一点从接缝另一侧喂进来（经度差一整圈），必须拿到同一个数：
        // 环绕写错时两侧会读到不同的抽头，差的是整格高程（数百厘米），不是最后一位浮点
        CloseTo(grid.SampleBicubicCmAt(0, lat, lon),
                grid.SampleBicubicCmAt(0, lat, lon - 2 * Math.PI), "接缝两侧同一点");

        // 双线性同样跨接缝（抽头 Cols−1 与 0），用的是同一个点：fx 必须与上面 At() 里那个 0.8 一致，
        // 各写一个数就会变成"实现没错、参照错"的假红（第一版就是这么错的）
        CloseTo(ExpectedBilinear(file, row, -1, 0.8, 0.0), grid.SampleElevationCmAt(0, lat, lon), "跨接缝双线性");
    }

    [Fact]
    public void 极边行夹紧后与手算一致()
    {
        // row0−1 越界（北端）与 row0+2 越界（南端）：语义是"复制边界行"，
        // 而片读只覆盖夹紧后的那段行区间——映射写错时这两条会各自偏出去。
        using var dem = TempDem.Create(StepDeg, (0, Field()));
        var manifest = dem.Load();
        using var grid = new DemGrid(manifest);
        using var file = File.OpenRead(LevelPath(manifest));

        foreach (int row0 in new[] { 0, 1, Rows - 2, Rows - 3 })
            foreach (double fy in new[] { 0.5, 0.25 })
                foreach (int col0 in new[] { 0, 5, Cols - 1, Cols - 6 })
                {
                    var (lat, lon) = At(row0, col0, 0.3, fy);
                    CloseTo(ExpectedBicubic(file, row0, col0, 0.3, fy), grid.SampleBicubicCmAt(0, lat, lon),
                                  $"夹紧双三次 row0={row0} fy={fy} col0={col0}");
                    CloseTo(ExpectedBilinear(file, row0, col0, 0.3, fy), grid.SampleElevationCmAt(0, lat, lon),
                                  $"夹紧双线性 row0={row0} fy={fy} col0={col0}");
                }
    }

    [Fact]
    public void 片读越界与非法尺寸大声失败而不是静默补零()
    {
        // 这条是"快路不能自己发明语义"的下界：调用方越界就该炸，
        // 悄悄返回 0 会被当成"海平面"，那正是地面约束最坏的错法。
        using var dem = TempDem.Create(StepDeg, (0, Field()));
        using var grid = new DemGrid(dem.Load());
        Assert.Throws<ArgumentOutOfRangeException>(() => grid.ReadStencil(0, Rows - 2, 0, 5, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => grid.ReadStencil(0, -1, 0, 5, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => grid.ReadStencil(0, 0, 0, 0, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => grid.ReadStencil(0, 0, 0, 5, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => grid.ReadStencil(0, 0, 0, 5, Cols + 1));
        Assert.Throws<ArgumentException>(() => grid.ReadStencil(9, 0, 0, 5, 5));
    }
}

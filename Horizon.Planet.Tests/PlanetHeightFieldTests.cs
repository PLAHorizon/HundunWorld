using System;
using System.Collections.Generic;
using System.IO;
using Horizon.Planet;
using Horizon.Planet.Dem;
using Xunit;

namespace Horizon.Planet.Tests;

/// <summary>
/// 高度场装配（<see cref="PlanetHeightField"/>）与它给服务端的<em>地面来源</em>。
/// <para>这一批判据要钉的是 §6.1 那句"服务端高度调用共享采样"的可执行含义。它不是"两边引用同一个
/// 程序集"——引用同一个程序集而装配顺序不同（忘了 hybrid、或者 basefieldVersion 用了 Levels.Count）
/// 照样会各算一份海拔，而那没有报错，症状是人站在地面下面。所以这里逐条把"同一份数据 ⇒ 同一个数"
/// 钉住，包括失败时必须<em>不判</em>而不是拿 0 当地面。</para>
/// </summary>
public class PlanetHeightFieldTests : IDisposable
{
    private const double StepDeg = 45.0 / 3600.0;      // 与口径判据同一种大像元夹具
    private const long Radius = PlanetConstants.PlanetRadiusCm;

    private readonly List<TempDem> _owned = new();
    private readonly List<IDisposable> _disposed = new();
    private readonly List<string> _emptyDirs = new();

    private DemManifest Fixture()
    {
        var dem = TempDem.Create(StepDeg, (0, Linear()));
        _owned.Add(dem);
        return dem.Load();
    }

    private PlanetHeightField Field(DemManifest manifest)
    {
        Assert.True(PlanetHeightField.TryCreate(manifest, out var field, out string why), why);
        _disposed.Add(field);
        return field;
    }

    /// <summary>一个空目录（有目录没 manifest）：DemGrid 的失败形状与"目录压根不在"不同，要分开测。</summary>
    private string EmptyDirectory()
    {
        string root = Path.Combine(Path.GetTempPath(), "hundun_empty_dem_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        _emptyDirs.Add(root);
        return root;
    }

    public void Dispose()
    {
        foreach (var d in _disposed) { try { d.Dispose(); } catch (Exception) { } }
        foreach (var dem in _owned) dem.Dispose();
        foreach (var dir in _emptyDirs)
        {
            try { Directory.Delete(dir, true); } catch (Exception) { }
        }
        _disposed.Clear(); _owned.Clear(); _emptyDirs.Clear();
        GC.SuppressFinalize(this);
    }

    private static short?[,] Linear()
    {
        var g = new short?[4, 8];
        for (int r = 0; r < 4; r++)
            for (int c = 0; c < 8; c++)
                g[r, c] = (short)(10 * c + r);
        return g;
    }

    private static PlanetVector3 Unit(double x, double y, double z)
    {
        var v = new PlanetVector3(x, y, z);
        return v / v.Length;
    }

    [Fact]
    public void 口径里的地形版本与混合高度源算出的逐位相同()
    {
        // 客户端近景把 patch 缓存的键从 hybrid.TerrainVersion 换成了 Caliber.TerrainVersion。
        // 两个数必须同位：不同就是"缓存按 A 套地形命名、采样按 B 套地形生成"，读到的块不会报错，只会是旧地形。
        var field = Field(Fixture());
        Assert.NotNull(field.Sampler.Hybrid);                       // 没有 hybrid 就没有细节层，采样只剩基场
        Assert.Equal(field.Sampler.Hybrid.TerrainVersion, field.Caliber.TerrainVersion);
    }

    [Fact]
    public void 海拔只由方向决定_同方向不同半径给同一个数()
    {
        // 地面判定的语义就是"这一点上方的地面在哪"，与点在半径外多少无关。
        // 若这里改成按半径插值，空中与地面的点会拿到不同的"地面海拔"，穿模判定随高度漂。
        var field = Field(Fixture());
        var dir = Unit(0.3, -0.55, 0.78);
        Assert.True(field.TryGetGroundElevationCm(dir * Radius, out double onSurface));
        Assert.True(field.TryGetGroundElevationCm(dir * (Radius + 500_000), out double above));
        Assert.Equal(onSurface, above);
    }

    [Fact]
    public void 退化点取不出海拔_而不是给出0()
    {
        // 拿零向量归一会得到 NaN，然后"埋深 = 地面 - NaN" 的比较一律不成立 ⇒ 地下被判成地上。
        // 所以这里必须是 false（调用方跳过判定），而不是 0（调用方把海平面当地面）。
        var field = Field(Fixture());
        Assert.False(field.TryGetGroundElevationCm(new PlanetVector3(0, 0, 0), out double zero));
        Assert.Equal(0, zero);                                       // false 时的 0 是初值，不许被当读数用
        Assert.False(field.TryGetGroundElevationCm(new PlanetVector3(double.NaN, 1, 1), out _));
        Assert.False(field.TryGetGroundElevationCm(new PlanetVector3(double.PositiveInfinity, 0, 0), out _));
        Assert.False(field.TryGetGroundElevationCm(new PlanetVector3(1e-320, 0, 0), out _));   // 下溢到零长度
    }

    [Fact]
    public void 装配失败时不返回一份看着合法的高度场()
    {
        Assert.False(PlanetHeightField.TryCreate(null!, out var bad, out string nullWhy));
        Assert.Null(bad);
        Assert.False(string.IsNullOrWhiteSpace(nullWhy));

        // 目录压根不在
        Assert.False(PlanetHeightField.TryCreateFromDirectory(
            Path.Combine(Path.GetTempPath(), "hundun_missing_" + Guid.NewGuid().ToString("N")),
            out var missing, out string missingWhy));
        Assert.Null(missing);
        Assert.Contains("manifest", missingWhy, StringComparison.Ordinal);

        // 有目录没 manifest：与上面是同一条主张，但失败发生在读文件的深处（DemGrid 会抛），
        // 启动路径要的是"回 false + 一句原因"，不是一个冒泡的 IOException
        Assert.False(PlanetHeightField.TryCreateFromDirectory(EmptyDirectory(), out var empty, out string emptyWhy));
        Assert.Null(empty);
        Assert.False(string.IsNullOrWhiteSpace(emptyWhy));
    }

    [Fact]
    public void 数据身份缺失的manifest不装配_因为口径会是自洽但无中生有的()
    {
        var manifest = Fixture();
        manifest.SourceSha256 = "";                       // ContentSeed 因此算成 0
        Assert.False(PlanetHeightField.TryCreate(manifest, out var field, out string why));
        Assert.Null(field);
        Assert.Contains("ContentSeed", why, StringComparison.Ordinal);
    }

    [Fact]
    public void 同一份manifest两次装配得到同一个海拔()
    {
        // 服务端与客户端是各自装配的（不同进程），确定性是"两边同数"的全部依据
        var manifest = Fixture();
        var a = Field(manifest);
        var b = Field(manifest);
        foreach (var dir in new[] { Unit(1, 2, 3), Unit(-0.2, 0.9, -0.4), Unit(0, -1, 0) })
        {
            Assert.True(a.TryGetGroundElevationCm(dir, out double ea));
            Assert.True(b.TryGetGroundElevationCm(dir, out double eb));
            Assert.Equal(ea, eb);
        }
    }

    [Fact]
    public void 释放两次不抛_因为收尾通路会连着调()
    {
        var field = Field(Fixture());
        field.Dispose();
        field.Dispose();
    }

    [Fact]
    public void 没挂目录的地面来源取不出海拔且报未配置()
    {
        foreach (string root in new[] { null!, "", "   " })
        {
            using var ground = new DemPlanetGroundSource(root);
            var dir = Unit(0.3, -0.55, 0.78);
            Assert.False(ground.TryGetGroundElevationCm(dir * Radius, out double elev));
            Assert.Equal(0, elev);                                   // 同一条主张：false 时不许留下可用的数
            Assert.Null(ground.TerrainVersion);                      // 不是 0：0 是一个合法算出来的版本
            Assert.Contains("<none>", ground.Describe(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void 挂在目录上的地面来源与直接装配同数()
    {
        // 这就是 §6.1 那句话的可执行形式：服务端读的那个数，与客户端装配出来的是同一个函数、同一份数据
        var dem = TempDem.Create(StepDeg, (0, Linear()));
        _owned.Add(dem);
        using var ground = new DemPlanetGroundSource(dem.Root);
        var field = Field(dem.Load());

        Assert.Equal(field.Caliber.TerrainVersion, ground.TerrainVersion);
        foreach (var dir in new[] { Unit(1, 0, 0), Unit(0, 1, 0), Unit(-0.4, 0.3, 0.86) })
        {
            Assert.True(field.TryGetGroundElevationCm(dir, out double fromField));
            Assert.True(ground.TryGetGroundElevationCm(dir * (Radius + 12_345), out double fromGround),
                        "地面来源取不出海拔：" + ground.Describe());
            Assert.Equal(fromField, fromGround);
        }
    }

    [Fact]
    public void 目录在但数据读不出来时地面来源只报失败不抛()
    {
        using var ground = new DemPlanetGroundSource(EmptyDirectory());
        Assert.False(ground.TryGetGroundElevationCm(Unit(1, 1, 1) * Radius, out _));
        Assert.Null(ground.TerrainVersion);
        Assert.False(string.IsNullOrWhiteSpace(ground.Failure));     // 失败原因要能读出来，不能只剩一个"没挂"
        Assert.Contains(ground.Failure, ground.Describe(), StringComparison.Ordinal);
    }
}

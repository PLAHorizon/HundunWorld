using System;
using System.Collections.Generic;
using System.IO;
using Horizon.Planet;
using Horizon.Planet.Dem;
using Horizon.Planet.Wire;
using Xunit;

namespace Horizon.Planet.Tests;

/// <summary>
/// 星球口径的推出与对账。
/// <para><see cref="PlanetHandshakePolicy"/> 判的是"两边是不是同一颗星球"，而前提是"两边各有一份口径"。
/// 此前这两份口径散在三处（<see cref="HybridHeightSource"/> 算版本、<see cref="PlanetPatchCache"/> 的键装
/// seed 与 version、manifest 装数据身份），且握手要的 <c>terrainSeed</c> 根本没有出口——
/// 高度场只在构造函数里把它喂给噪声器。这一类把"口径 = f(manifest)"钉成一条可执行的主张。</para>
/// </summary>
public class PlanetCaliberTests : IDisposable
{
    private const double StepDeg = 45.0 / 3600.0;      // 45″：夹具用的大像元，λ_c 很大，细节层数自然少

    private readonly List<TempDem> _owned = new();

    /// <summary>
    /// 造一个"活着"的基场目录并返回读好的 manifest。
    /// <para>不能在这里 <c>using</c> 掉 <see cref="TempDem"/>：<see cref="DemGrid"/> 是按
    /// <c>manifest.RootDirectory</c> 现开文件的，目录先被删掉就变成"manifest 有效但数据不在"，
    /// 那会让下面几条判据报出一个跟口径无关的错。所以夹具由本类持有，测试结束统一删。</para>
    /// </summary>
    private DemManifest Fixture()
    {
        var dem = TempDem.Create(StepDeg, (0, Linear()));
        _owned.Add(dem);
        return dem.Load();
    }

    public void Dispose()
    {
        foreach (var dem in _owned) dem.Dispose();
        _owned.Clear();
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

    /// <summary>把 manifest 改成"数据身份缺失"（ContentSeed 会算成 0）。</summary>
    private static DemManifest WithoutIdentity(DemManifest m)
    {
        m.SourceSha256 = "";
        return m;
    }

    [Fact]
    public void 完整manifest能推出口径_七项都齐()
    {
        Assert.True(PlanetCaliber.TryFromDem(Fixture(), out var caliber, out string why), why);
        Assert.Equal("", why);
        Assert.Equal(PlanetHandshake.ProtocolVersion, caliber.WireProtocol);
        Assert.Equal(PlanetConstants.PlanetRadiusCm, caliber.PlanetRadiusCm);
        Assert.Equal((byte)PlanetConstants.DefaultRegionLevel, caliber.RegionLevel);
        Assert.NotEqual(0L, caliber.TerrainSeed);              // 此前握手包里的这一项无处可取
        Assert.NotEqual(0u, caliber.TerrainVersion);
        Assert.NotEqual(0L, caliber.BasefieldHashHi);
    }

    [Fact]
    public void 口径与真实高度场逐字段对账_同一份数据只能有一个说法()
    {
        var manifest = Fixture();
        Assert.True(PlanetCaliber.TryFromDem(manifest, out var caliber, out string why), why);

        // 用同一份 manifest 走客户端真实那条构造链：profile → grid → hybrid
        var profile = new TerrainProfile(PlanetConstants.PlanetRadiusCm, manifest.ArcSecond / 3600.0);
        using var grid = new DemGrid(manifest);
        var hybrid = new HybridHeightSource(grid, profile, manifest.ContentSeed, manifest.Format);

        Assert.True(PlanetCaliber.MatchesHeightSource(caliber, hybrid, out why), why);

        // 反证：口径里任何一个字段被改，对账必须失败，而且要说出是哪一项
        var tampered = caliber;
        tampered.TerrainVersion ^= 0x1u;
        Assert.False(PlanetCaliber.MatchesHeightSource(tampered, hybrid, out why));
        Assert.Contains("地形版本", why);

        tampered = caliber;
        tampered.BasefieldHashHi ^= 7L;
        Assert.False(PlanetCaliber.MatchesHeightSource(tampered, hybrid, out why));
        Assert.Contains("基场哈希", why);

        // 正向孪生：原样再来一次仍然通过（上面那两条失败不是"永远失败"）
        Assert.True(PlanetCaliber.MatchesHeightSource(caliber, hybrid, out why), why);
        Assert.Equal("", why);
    }

    [Fact]
    public void 基场版本按位比_负一号与全1是同一个值()
    {
        var manifest = Fixture();
        manifest.Format = -1;                       // 线上是 u32，库里存的是 int：边界上必须比位型
        Assert.True(PlanetCaliber.TryFromDem(manifest, out var caliber, out string why), why);
        Assert.Equal(unchecked((uint)-1), caliber.BasefieldVersion);

        var profile = new TerrainProfile(PlanetConstants.PlanetRadiusCm, manifest.ArcSecond / 3600.0);
        using var grid = new DemGrid(manifest);
        var hybrid = new HybridHeightSource(grid, profile, manifest.ContentSeed, manifest.Format);

        // 若这里写成 caliber.BasefieldVersion != hybrid.BasefieldVersion，C# 会把两边提到 long，
        // 4294967295 != -1 ⇒ 同一份数据被判成两份口径。这条判据钉的就是那个坑。
        Assert.True(PlanetCaliber.MatchesHeightSource(caliber, hybrid, out string agree), agree);
    }

    [Fact]
    public void 数据身份缺失时拒不出口径_而不是给一份自洽的假口径()
    {
        // ContentSeed 在 sha256 缺失时返回 0，而 0 照样能算出一个非零 terrainVersion：
        // 那是一份"自洽但无中生有"的口径，双端会一起信任它，比报错难查得多
        Assert.False(PlanetCaliber.TryFromDem(WithoutIdentity(Fixture()), out var caliber, out string why));
        Assert.Equal(0u, caliber.TerrainVersion);
        Assert.Equal(0L, caliber.TerrainSeed);
        Assert.Contains("无中生有", why);

        var cases = new List<(string Label, Action<DemManifest> Break, string Expect)>
        {
            ("没有层级", m => m.Levels.Clear(), "没有任何一级网格"),
            ("角宽为 0", m => m.ArcSecond = 0, "ArcSecond"),
            ("角宽为负", m => m.ArcSecond = -1, "ArcSecond"),
            ("角宽 NaN", m => m.ArcSecond = double.NaN, "不是正有限值"),
        };
        foreach (var (label, break_, expect) in cases)
        {
            var m = Fixture();
            break_(m);
            Assert.False(PlanetCaliber.TryFromDem(m, out caliber, out why), label);
            Assert.Contains(expect, why, StringComparison.Ordinal);
            Assert.Equal(0u, caliber.TerrainVersion);          // 失败时 out 必须是干净的默认值
        }

        // null 单独一条：它不该抛
        Assert.False(PlanetCaliber.TryFromDem(null, out caliber, out why));
        Assert.Contains("manifest 为空", why);

        // 正向对照：只把身份补回来，同样的 manifest 就能算出口径（上面那些失败各自是原因，不是"全都不可用"）
        var good = Fixture();
        good.ArcSecond = StepDeg * 3600.0;
        good.Levels.Clear();
        good.Levels.Add(new DemLevel { Level = 0, Cols = 8, Rows = 4, File = "level_00.bin", Bytes = 512 });
        Assert.True(PlanetCaliber.TryFromDem(good, out _, out string repaired), repaired);
    }

    [Fact]
    public void FromDem_算不出来就抛_不给启动路径留静默()
    {
        Assert.True(PlanetCaliber.TryFromDem(Fixture(), out var expected, out _));
        Assert.Equal(expected.TerrainVersion, PlanetCaliber.FromDem(Fixture()).TerrainVersion);

        var ex = Assert.Throws<InvalidOperationException>(() => PlanetCaliber.FromDem(WithoutIdentity(Fixture())));
        Assert.Contains("星球口径推不出来", ex.Message);
    }

    [Fact]
    public void 换一份基场数据_整组口径一起变()
    {
        var a = Fixture();
        var b = Fixture();
        b.SourceSha256 = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        Assert.True(PlanetCaliber.TryFromDem(a, out var ca, out _));
        Assert.True(PlanetCaliber.TryFromDem(b, out var cb, out _));

        Assert.NotEqual(ca.BasefieldHashHi, cb.BasefieldHashHi);
        Assert.NotEqual(ca.TerrainVersion, cb.TerrainVersion);
        Assert.NotEqual(ca.TerrainSeed, cb.TerrainSeed);
        Assert.Equal(ca.PlanetRadiusCm, cb.PlanetRadiusCm);        // 星球身份没变：只有数据身份变

        var c = Fixture();
        c.Format = a.Format + 1;
        Assert.True(PlanetCaliber.TryFromDem(c, out var cc, out _));
        Assert.NotEqual(ca.TerrainVersion, cc.TerrainVersion);     // 基场版本号也进版本哈希
        Assert.Equal(ca.BasefieldHashHi, cc.BasefieldHashHi);      // 数据身份没变
    }

    [Fact]
    public void 目录版入口_读不到时把目录名带进理由()
    {
        string missing = Path.Combine(Path.GetTempPath(), "hundun_no_such_dem_" + Guid.NewGuid().ToString("N"));
        Assert.False(PlanetCaliber.TryFromDirectory(missing, out var caliber, out string why));
        Assert.Contains(missing, why, StringComparison.Ordinal);   // 排查要先知道是哪个目录
        Assert.Contains("读不到 manifest", why);
        Assert.Equal(0L, caliber.BasefieldHashHi);

        // 正向孪生：同一个入口对着一个真目录就能算出来
        using var dem = TempDem.Create(StepDeg, (0, Linear()));
        Assert.True(PlanetCaliber.TryFromDirectory(dem.Root, out var real, out string ok), ok);
        Assert.NotEqual(0u, real.TerrainVersion);
    }

    [Fact]
    public void 口径来源_没挂基场的部署不判_而不是把所有人拒光()
    {
        // 这条是"服务端可以没有口径"的正面表述：本地跑服务端、灰度还没接星球的部署也走这段判定，
        // 把它判成"全员口径不符"会让整颗星球起不来
        var none = new DemPlanetCaliberSource(null);
        Assert.False(none.TryGetCaliber(out var caliber));
        Assert.Equal(0u, caliber.TerrainVersion);

        // "没配"与"配了但读不出来"必须是两回事：运维看到后者要去查目录，看到前者该知道这条路还没接。
        // 两边都只回 false 的话，这条区分就丢了。
        Assert.Null(none.RootDirectory);
        Assert.Null(none.Failure);

        var blank = new DemPlanetCaliberSource("   ");
        Assert.False(blank.TryGetCaliber(out _));                  // 空白目录 = 没配，不是"配错了"
        Assert.Null(blank.RootDirectory);
        Assert.Null(blank.Failure);

        var broken = new DemPlanetCaliberSource(Path.Combine(Path.GetTempPath(),
            "hundun_no_such_dem_" + Guid.NewGuid().ToString("N")));
        Assert.False(broken.TryGetCaliber(out _));
        Assert.NotNull(broken.RootDirectory);                      // 配了：目录名要留着
        Assert.NotNull(broken.Failure);                            // 而且要说清为什么读不出来
    }

    [Fact]
    public void 口径来源_坏目录回false且把原因留住_好目录读一次就缓存()
    {
        string missing = Path.Combine(Path.GetTempPath(), "hundun_no_such_dem_" + Guid.NewGuid().ToString("N"));
        var broken = new DemPlanetCaliberSource(missing);
        Assert.False(broken.TryGetCaliber(out _));
        Assert.Contains(missing, broken.Failure, StringComparison.Ordinal);
        Assert.False(broken.TryGetCaliber(out _));                 // 反复问都如实回 false，不缓存成"能算出来"

        using var dem = TempDem.Create(StepDeg, (0, Linear()));
        var source = new DemPlanetCaliberSource(dem.Root);
        Assert.True(source.TryGetCaliber(out var first), "好目录应当能取出口径");
        Assert.NotEqual(0u, first.TerrainVersion);

        // 读成功之后把目录删掉：口径必须照旧（进程生命周期内一份，口径中途变了会让已进世界的客户端
        // 下一次换区被拒，而它没法恢复）
        Directory.Delete(dem.Root, true);
        Assert.True(source.TryGetCaliber(out var second));
        Assert.Equal(first.TerrainVersion, second.TerrainVersion);
        Assert.Equal(first.TerrainSeed, second.TerrainSeed);
        Assert.Equal(first.BasefieldHashHi, second.BasefieldHashHi);
        Assert.True(PlanetHandshakePolicy.Evaluate(first, second).Accepted);

        // 相对/带空白路径也照实处理，不许悄悄当成"没配"
        Assert.NotNull(new DemPlanetCaliberSource("  " + dem.Root + " ").RootDirectory);
    }
}

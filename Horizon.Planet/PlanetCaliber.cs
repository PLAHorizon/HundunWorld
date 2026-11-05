using System;
using Horizon.Planet.Dem;
using Horizon.Planet.Wire;

namespace Horizon.Planet;

/// <summary>
/// 从基场 manifest 推出<em>这一份数据</em>对应的握手口径。
/// <para>为什么要单独有一个入口：<see cref="PlanetHandshakePolicy"/> 要判的是"两边是不是同一颗星球"，
/// 而两边各自的口径此前散在三处——<see cref="HybridHeightSource"/> 里算 terrainVersion，
/// <see cref="PlanetPatchCache"/> 的键里装 seed 与 version，manifest 里装 identity。
/// 散着写意味着任何一处多乘了个 1000、或者把 <c>Format</c> 当成 <c>Version</c> 传进去，
/// 两端就各自得到一份"看起来自洽"的口径，然后互相判对方作弊。这里收拢成一条路：
/// <b>口径 = f(manifest)</b>，客户端与服务端跑的是同一个函数、同一份数据。</para>
/// <para><see cref="HybridHeightSource"/> 没有把噪声种子暴露出来（它只在构造函数里喂给
/// <see cref="DeterministicValueNoise"/>），所以握手包里的 <c>terrainSeed</c> 此前无处可取——
/// 这个入口按<em>同一条派生式</em>把它算出来，判据里逐字段核对过两边算的一样。</para>
/// </summary>
public static class PlanetCaliber
{
    /// <summary>
    /// 从 manifest 算口径；算不出来时返回 false 并说明为什么，<b>不返回一份看起来合法的口径</b>。
    /// <para>最要防的是"数据身份缺失但版本号照算"：<see cref="DemManifest.ContentSeed"/> 在
    /// sha256 缺失时返回 0，而 0 仍然能算出一个非零的 terrainVersion——那是一份
    /// <em>自洽但无中生有</em>的口径，双端会一起信任它。宁可不判。</para>
    /// </summary>
    public static bool TryFromDem(DemManifest? manifest, out PlanetHandshake caliber, out string why)
    {
        caliber = default;
        why = "";
        if (manifest is null)
        {
            why = "manifest 为空：没有基场就没有口径";
            return false;
        }
        if (manifest.Levels is null || manifest.Levels.Count == 0)
        {
            why = "manifest 里没有任何一级网格：数据不完整";
            return false;
        }
        if (!(manifest.ArcSecond > 0) || !double.IsFinite(manifest.ArcSecond))
        {
            why = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "manifest.ArcSecond={0} 不是正有限值：算不出 λ_c，也就算不出 terrainVersion",
                manifest.ArcSecond);
            return false;
        }
        long contentSeed = manifest.ContentSeed;
        if (contentSeed == 0)
        {
            why = $"manifest 没有可信的数据身份（SourceSha256=\"{manifest.SourceSha256}\"，ContentSeed=0）："
                  + "拿它算出来的 terrainVersion 自洽但是无中生有，双端会一起信错";
            return false;
        }

        TerrainProfile profile;
        try
        {
            profile = new TerrainProfile(PlanetConstants.PlanetRadiusCm, manifest.ArcSecond / 3600.0);
        }
        catch (ArgumentException ex)
        {
            why = $"由 manifest 构造 TerrainProfile 失败：{ex.Message}";
            return false;
        }

        caliber = new PlanetHandshake
        {
            WireProtocol = PlanetHandshake.ProtocolVersion,
            PlanetRadiusCm = PlanetConstants.PlanetRadiusCm,
            RegionLevel = (byte)PlanetConstants.DefaultRegionLevel,
            TerrainSeed = profile.ComputeNoiseSeed(contentSeed, manifest.Format),
            TerrainVersion = profile.ComputeTerrainVersion(contentSeed, manifest.Format),
            BasefieldVersion = unchecked((uint)manifest.Format),
            BasefieldHashHi = contentSeed,
        };
        return true;
    }

    /// <summary><see cref="TryFromDem"/> 的抛异常版：用于"算不出来就是配置错误"的启动路径。</summary>
    public static PlanetHandshake FromDem(DemManifest manifest)
    {
        if (!TryFromDem(manifest, out var caliber, out string why))
            throw new InvalidOperationException("星球口径推不出来：" + why);
        return caliber;
    }

    /// <summary>读目录再算。<see cref="DemManifest.TryLoad"/> 的失败理由原样带出去。</summary>
    public static bool TryFromDirectory(string root, out PlanetHandshake caliber, out string why)
    {
        if (!DemManifest.TryLoad(root, out var manifest, out string loadError))
        {
            caliber = default;
            why = $"读不到 manifest（{root}）：{loadError}";
            return false;
        }
        return TryFromDem(manifest, out caliber, out why);
    }

    /// <summary>
    /// 口径与 <see cref="HybridHeightSource"/> 实际用出来的那三个字段是否逐位一致。
    /// <para>这是"同一个函数、同一份数据"这条主张的可执行版本：算完口径再拿同一份 manifest
    /// 去构造真实高度场，两边必须对得上。对不上说明有一侧偷偷改了派生式。</para>
    /// </summary>
    public static bool MatchesHeightSource(in PlanetHandshake caliber, HybridHeightSource source, out string why)
    {
        why = "";
        if (source is null) { why = "height source 为空"; return false; }
        if (caliber.BasefieldHashHi != source.BasefieldHashHi)
        {
            why = $"基场哈希：口径 {caliber.BasefieldHashHi:x} vs 高度场 {source.BasefieldHashHi:x}";
            return false;
        }
        // 按<em>位</em>比而不是按数值比：握手里的 basefieldVersion 是 u32，高度场里存的是 int。
        // C# 会把两边一起提到 long，于是 -1 与 4294967295 被判成"不同"——同一份数据两条结论，
        // 正是这条判据要消灭的东西。取 unchecked 之后比的是位型。
        if (unchecked((int)caliber.BasefieldVersion) != source.BasefieldVersion)
        {
            why = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "基场版本：口径 {0}({1:x8}) vs 高度场 {2}",
                caliber.BasefieldVersion, (int)caliber.BasefieldVersion, source.BasefieldVersion);
            return false;
        }
        if (caliber.TerrainVersion != source.TerrainVersion)
        {
            why = $"地形版本：口径 {caliber.TerrainVersion:x8} vs 高度场 {source.TerrainVersion:x8}";
            return false;
        }
        return true;
    }
}

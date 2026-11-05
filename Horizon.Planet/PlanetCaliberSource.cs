using System;
using System.Globalization;
using Horizon.Planet.Wire;

namespace Horizon.Planet;

/// <summary>
/// 服务端这一侧"我这颗星球是什么口径"的来源。
/// <para>之所以是个接口而不是一个常量：半径与层级写死在 <see cref="PlanetConstants"/> 里没问题
/// （它们是协议的一部分），但 <c>terrainSeed/terrainVersion/basefieldHash</c> 描述的是
/// <em>部署在磁盘上的那一份基场数据</em>。同一份 build 换一份 DEM 就该拒掉旧客户端——
/// 把数据身份当成编译期常量，等于放弃这条判据能防的那类事故。</para>
/// </summary>
public interface IPlanetCaliberSource
{
    /// <summary>
    /// 取服务端口径。<b>返回 false 表示"服务端没有声明口径"</b>，此时调用方必须跳过判定，
    /// 而不是把请求全拒——没挂基场数据的部署（灰度、本地跑服务端但不跑星球）也会走到这条判定，
    /// 判成"全员口径不符"会让整颗星球起不来。
    /// </summary>
    bool TryGetCaliber(out PlanetHandshake caliber);
}

/// <summary>
/// 从基场目录（<c>manifest.json</c> + 各级网格）取口径，读一次就缓存。
/// <para>缓存的理由不是省时间——是<em>一致性</em>：口径如果在两次请求之间变了，
/// 已经进了世界的客户端会在下一次换区时被拒，而它拒不了（人在地球上）。
/// 一份进程生命周期内固定的口径，配合部署重启，才是可解释的行为。</para>
/// </summary>
public sealed class DemPlanetCaliberSource : IPlanetCaliberSource
{
    private readonly object _gate = new();
    private PlanetHandshake? _caliber;
    private string? _failure;

    public DemPlanetCaliberSource(string? rootDirectory)
    {
        RootDirectory = string.IsNullOrWhiteSpace(rootDirectory) ? null : rootDirectory!.Trim();
    }

    /// <summary>配置进来的基场根目录；null/空白 = 这个部署没挂基场，判定不生效。</summary>
    public string? RootDirectory { get; }

    /// <summary>读取失败的原因，一次都没有成功过时非空。<b>日志要用它，别只说"没口径"</b>。</summary>
    public string? Failure => _failure;

    public bool TryGetCaliber(out PlanetHandshake caliber)
    {
        if (_caliber is { } cached)
        {
            caliber = cached;
            return true;
        }
        if (RootDirectory is null)
        {
            caliber = default;
            return false;
        }
        lock (_gate)
        {
            if (_caliber is { } again)
            {
                caliber = again;
                return true;
            }
            if (!PlanetCaliber.TryFromDirectory(RootDirectory, out var loaded, out string why))
            {
                // 失败也记住原因，但每次照实返回 false：目录坏了不缓存成一个"永远算不出来"的口径
                _failure = why;
                caliber = default;
                return false;
            }
            _caliber = loaded;
            _failure = null;
            caliber = loaded;
            return true;
        }
    }

    public override string ToString() => string.Format(CultureInfo.InvariantCulture,
        "planet caliber source root={0} loaded={1} failure={2}",
        RootDirectory ?? "<none>", _caliber is not null, _failure ?? "<none>");
}

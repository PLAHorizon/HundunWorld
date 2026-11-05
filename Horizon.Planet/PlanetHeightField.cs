using System;
using System.Globalization;
using Horizon.Planet.Dem;
using Horizon.Planet.Wire;

namespace Horizon.Planet;

/// <summary>
/// 一份基场数据 + 一套可调口径 → 一个可用的高度场。<b>客户端与服务端共用这一处装配</b>。
/// <para>为什么要收成一处：§6.1 要求"服务端高度调用共享采样，与客户端同一 terrainVersion"。
/// 这话的可执行含义不是"两边引用同一个程序集"，而是"两边按同一顺序把
/// manifest → grid → profile → hybrid → sampler 串起来"。此前这条链只在客户端
/// （<c>PlanetNearFieldDriver</c>）里拼过一次，服务端压根没有等价物——地面约束、刷怪、
/// AOI 那几项因此都落不了地；而"服务端再拼一遍"的下一步一定是某一步参数不一样
/// （忘了传 hybrid、或者 basefieldVersion 用了 Levels.Count），两边海拔就会安静地差几十米，
/// 而那不会报错，只会让人站在地面下面。</para>
/// </summary>
public sealed class PlanetHeightField : IDisposable
{
    private readonly DemGrid _grid;
    private bool _disposed;

    private PlanetHeightField(DemManifest manifest, DemGrid grid, HeightSampler sampler, PlanetHandshake caliber)
    {
        Manifest = manifest;
        _grid = grid;
        Sampler = sampler;
        Caliber = caliber;
    }

    public DemManifest Manifest { get; }
    public HeightSampler Sampler { get; }

    /// <summary>这份数据对应的握手口径（与 <see cref="PlanetCaliber"/> 同源，不另算一遍）。</summary>
    public PlanetHandshake Caliber { get; }

    /// <summary>
    /// 装配链中间的 <c>profile</c> 与 <c>hybrid</c> 不往外递：<see cref="Sampler"/> 已经把它们包在里面了，
    /// 而多一个出口就多一个"绕过顺序自己拼一遍"的入口——那正是这个类要消掉的东西。
    /// <see cref="DemGrid"/> 只留着 <see cref="Dispose"/> 用。
    /// </summary>
    /// <summary>
    /// 从 manifest 装配。<b>失败回 false 并给出原因</b>：启动路径拿不到高度场是配置问题，
    /// 要能被日志说清，而不是抛一个 "file not found" 让人去猜是哪一步。
    /// </summary>
    public static bool TryCreate(DemManifest manifest, out PlanetHeightField field, out string why)
    {
        field = null!;
        why = "";
        if (manifest is null)
        {
            why = "manifest 为空";
            return false;
        }
        if (!PlanetCaliber.TryFromDem(manifest, out var caliber, out string caliberWhy))
        {
            why = caliberWhy;
            return false;
        }
        try
        {
            var grid = new DemGrid(manifest);
            var profile = new TerrainProfile(PlanetConstants.PlanetRadiusCm, manifest.ArcSecond / 3600.0);
            var hybrid = new HybridHeightSource(grid, profile, manifest.ContentSeed, manifest.Format);
            var sampler = new HeightSampler(grid, hybrid);
            field = new PlanetHeightField(manifest, grid, sampler, caliber);
            return true;
        }
        catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException
                                     or FormatException or InvalidOperationException or NotSupportedException)
        {
            // 不让它冒泡：调用方是启动路径与 grain 的激活路径，一颗没挂好的基场应当
            // 表现为"这条判定不生效 + 一句话说清为什么"，不是进程起不来
            why = e.GetType().Name + ": " + e.Message;
            field = null!;
            return false;
        }
    }

    public static bool TryCreateFromDirectory(string root, out PlanetHeightField field, out string why)
    {
        if (!DemManifest.TryLoad(root, out var manifest, out string loadError))
        {
            field = null!;
            why = $"读不到 manifest（{root}）：{loadError}";
            return false;
        }
        return TryCreate(manifest, out field, out why);
    }

    /// <summary>单位方向处的海拔（厘米，相对海平面）。走的是 sampler，即基场 + 细节那一套完整口径。</summary>
    public double SampleElevationCm(in PlanetVector3 unitDirection) => Sampler.SampleElevationCm(unitDirection);

    /// <summary>
    /// 全局点（ECEF 厘米）处的地面海拔。方向 = 点归一化，半径外的偏移不参与采样。
    /// <para>退化点返回 false：拿零向量归一会得到 NaN，然后所有下游比较都"不成立"，
    /// 于是地下会被判成地上。</para>
    /// </summary>
    public bool TryGetGroundElevationCm(in PlanetVector3 globalCm, out double elevationCm)
    {
        elevationCm = 0;
        double length = globalCm.Length;
        if (!double.IsFinite(length) || length <= 1e-300) return false;
        elevationCm = SampleElevationCm(globalCm / length);
        return double.IsFinite(elevationCm);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _grid.Dispose();
    }

    public override string ToString() => string.Format(CultureInfo.InvariantCulture,
        "height field terrainVersion={0:x8} basefield={1} arcSecond={2:0.####}",
        Caliber.TerrainVersion, Caliber.BasefieldVersion, Manifest.ArcSecond);
}

/// <summary>
/// 服务端这一侧的"脚下地面在哪"。返回 false 表示<b>这个部署没挂基场数据</b>——
/// 此时调用方必须跳过地面判定，而不是把海拔当 0。
/// <para>把 0 当默认值的后果很具体：这颗星球的洋底是负的、山地是正的，
/// 用 0 当地面会让所有站在山上的人被判成"在地面以下"，而所有在海里的人被判成正常。</para>
/// </summary>
public interface IPlanetGroundSource
{
    bool TryGetGroundElevationCm(in PlanetVector3 globalCm, out double elevationCm);

    /// <summary>
    /// 这份地面数据对应的地形版本；<b>没挂数据时为 null</b>（不是 0：0 是一个合法算出来的版本，
    /// 把它当成"没有"会让配置错误看起来像一次正常的地形换代）。
    /// <para>存在的理由只有一个：它必须与口径来源算出的 <c>terrainVersion</c> 相等。
    /// 两个来源各挂一个目录是配得出来的，那表现为"握手信 A 套数据、地面判 B 套数据"，
    /// 而两条判定各自都自洽——只有把这一个是非摆在线上才会被看见。</para>
    /// </summary>
    uint? TerrainVersion { get; }

    /// <summary>给日志与判据用的一行说明：有没有挂、挂在哪、地形版本是多少。</summary>
    string Describe();
}

/// <summary>按目录懒加载的高度场来源。<b>一个实例一份数据</b>：与 <see cref="DemPlanetCaliberSource"/>
/// 同理，口径中途变了会让已进世界的客户端在下一次换区被拒，而它没法恢复。</summary>
public sealed class DemPlanetGroundSource : IPlanetGroundSource, IDisposable
{
    private readonly object _gate = new();
    private PlanetHeightField? _field;
    private string? _failure;
    private bool _tried;

    public DemPlanetGroundSource(string? rootDirectory)
    {
        RootDirectory = string.IsNullOrWhiteSpace(rootDirectory) ? null : rootDirectory!.Trim();
    }

    public string? RootDirectory { get; }
    public string? Failure => _failure;
    public uint? TerrainVersion => EnsureLoaded()?.Caliber.TerrainVersion;

    public bool TryGetGroundElevationCm(in PlanetVector3 globalCm, out double elevationCm)
    {
        var field = EnsureLoaded();
        elevationCm = 0;
        return field is not null && field.TryGetGroundElevationCm(in globalCm, out elevationCm);
    }

    private PlanetHeightField? EnsureLoaded()
    {
        if (RootDirectory is null) return null;         // 没配目录：这条判定不生效，一次磁盘都不碰
        lock (_gate)
        {
            if (_field is not null) return _field;
            if (_tried) return null;                        // 上次就没读出来，不反复敲磁盘
            _tried = true;
            if (RootDirectory is null) return null;
            if (!PlanetHeightField.TryCreateFromDirectory(RootDirectory, out var loaded, out string why))
            {
                _failure = why;
                return null;
            }
            _field = loaded;
            return loaded;
        }
    }

    public string Describe() => string.Format(CultureInfo.InvariantCulture,
        "ground root={0} loaded={1} terrainVersion={2} failure={3}",
        RootDirectory ?? "<none>", _field is not null,
        _field is null ? "<none>" : _field.Caliber.TerrainVersion.ToString("x8"), _failure ?? "<none>");

    public void Dispose()
    {
        lock (_gate)
        {
            _field?.Dispose();
            _field = null;
        }
    }
}

using System;
using System.Globalization;
using Horizon.Planet.Wire;

namespace Horizon.Planet;

/// <summary>握手被拒时<em>具体是哪一项</em>不一致。合并成一条"口径不符"是没用的：
/// 半径错与基场版本错要做的处置完全不同（前者是 build 选错了星球，后者是数据没同步）。</summary>
public enum PlanetHandshakeReject
{
    None = 0,

    /// <summary>线协议版本不对：字节的意思都不一样了，后面任何字段都不能信。</summary>
    WireProtocol,

    /// <summary>线层读失败（截断/多余字节/非法区域号），字段还没开始比。</summary>
    WireRead,

    /// <summary>半径不对。</summary>
    PlanetRadius,

    /// <summary>区域层级不对：同一块地面在两边会被编成不同的 RegionId。</summary>
    RegionLevel,

    /// <summary>噪声种子不对。</summary>
    TerrainSeed,

    /// <summary>地形版本不对：双端会拿不同地形互相判对方作弊。</summary>
    TerrainVersion,

    /// <summary>基场数据版本号不对。</summary>
    BasefieldVersion,

    /// <summary>基场内容哈希不对：号一样但数据不一样，是最坏的一种（双方都以为自己是对的）。</summary>
    BasefieldHash,
}

/// <summary>一次握手的结论：是否放行、卡在哪一项、以及给人看的那句话。</summary>
public readonly struct PlanetHandshakeVerdict
{
    internal PlanetHandshakeVerdict(bool accepted, PlanetHandshakeReject field, string detail)
    {
        Accepted = accepted;
        Field = field;
        Detail = detail ?? "";
    }

    public bool Accepted { get; }

    /// <summary>卡在哪一项；接受时是 <see cref="PlanetHandshakeReject.None"/>。</summary>
    public PlanetHandshakeReject Field { get; }

    /// <summary>被拒时非空且带两边的值；接受时是"口径一致"的摘要。</summary>
    public string Detail { get; }

    public override string ToString() => string.Format(CultureInfo.InvariantCulture,
        "handshake accepted={0} field={1} detail={2}", Accepted, Field, Detail);
}

/// <summary>
/// 星球口径握手策略（§6.2 那句"terrainSeed/terrainVersion 不匹配直接拒绝进入世界"的可执行版）。
/// <para>库里早就有 <see cref="PlanetHandshake"/> 的编解码与 <c>DescribeMismatch</c>，
/// 但没有任何服务端代码调用它们——所以"不匹配拒绝进入"这件事在客户端缓存那一侧有判据（§13.12 的
/// <c>reject_badterrainversion</c> 等计数），在服务端那一侧一直是空的。这一类是纯判定，
/// 双端都能用，也不依赖 Orleans。</para>
/// <para><b>比较顺序是有意义的</b>：线协议 → 星球身份（半径、层级）→ 地形身份（种子、版本、基场）。
/// 前面错了后面就不可信，报后面那项会把人引向错误的排查方向。</para>
/// </summary>
public static class PlanetHandshakePolicy
{
    /// <summary>逐项比较。调用方负责先把字节解成 <see cref="PlanetHandshake"/>（读失败走 <see cref="EvaluateRead"/>）。</summary>
    public static PlanetHandshakeVerdict Evaluate(in PlanetHandshake expected, in PlanetHandshake actual)
    {
        if (actual.WireProtocol != expected.WireProtocol)
            return Reject(PlanetHandshakeReject.WireProtocol,
                string.Format(CultureInfo.InvariantCulture, "wire {0} != {1}",
                    actual.WireProtocol, expected.WireProtocol));
        if (actual.PlanetRadiusCm != expected.PlanetRadiusCm)
            return Reject(PlanetHandshakeReject.PlanetRadius,
                string.Format(CultureInfo.InvariantCulture, "radius {0} != {1}",
                    actual.PlanetRadiusCm, expected.PlanetRadiusCm));
        if (actual.RegionLevel != expected.RegionLevel)
            return Reject(PlanetHandshakeReject.RegionLevel,
                string.Format(CultureInfo.InvariantCulture, "regionLevel {0} != {1}",
                    actual.RegionLevel, expected.RegionLevel));
        if (actual.TerrainSeed != expected.TerrainSeed)
            return Reject(PlanetHandshakeReject.TerrainSeed,
                string.Format(CultureInfo.InvariantCulture, "terrainSeed {0:x} != {1:x}",
                    actual.TerrainSeed, expected.TerrainSeed));
        if (actual.TerrainVersion != expected.TerrainVersion)
            return Reject(PlanetHandshakeReject.TerrainVersion,
                string.Format(CultureInfo.InvariantCulture, "terrainVersion {0} != {1}",
                    actual.TerrainVersion, expected.TerrainVersion));
        if (actual.BasefieldVersion != expected.BasefieldVersion)
            return Reject(PlanetHandshakeReject.BasefieldVersion,
                string.Format(CultureInfo.InvariantCulture, "basefieldVersion {0} != {1}",
                    actual.BasefieldVersion, expected.BasefieldVersion));
        if (actual.BasefieldHashHi != expected.BasefieldHashHi)
            return Reject(PlanetHandshakeReject.BasefieldHash,
                string.Format(CultureInfo.InvariantCulture, "basefieldHash {0:x} != {1:x}",
                    actual.BasefieldHashHi, expected.BasefieldHashHi));

        return new PlanetHandshakeVerdict(true, PlanetHandshakeReject.None,
            string.Format(CultureInfo.InvariantCulture,
                "口径一致 radius={0} level={1} terrainVersion={2} basefield={3}",
                expected.PlanetRadiusCm, expected.RegionLevel, expected.TerrainVersion, expected.BasefieldVersion));
    }

    /// <summary>线层读取结果先过一遍：读都不成，字段比较毫无意义。</summary>
    public static PlanetHandshakeVerdict EvaluateRead(PlanetWireStatus status, bool readOk,
                                                      in PlanetHandshake expected, in PlanetHandshake actual)
    {
        if (!readOk)
        {
            // UnsupportedProtocol 由 PlanetHandshake.TryRead 自己标出来，这里归成 wire 协议那一项，
            // 好让上层只处理"哪一项不对"这一种形状
            if (status == PlanetWireStatus.UnsupportedProtocol)
                return Reject(PlanetHandshakeReject.WireProtocol,
                    string.Format(CultureInfo.InvariantCulture, "wire 读不出来：{0}", status));
            return Reject(PlanetHandshakeReject.WireRead,
                string.Format(CultureInfo.InvariantCulture, "握手包解不开：{0}", status));
        }
        return Evaluate(in expected, in actual);
    }

    /// <summary>
    /// 一行日志。<b>必须与 <see cref="PlanetHandshake.DescribeMismatch"/> 说同一件事</b>：
    /// 那一份是既有的字段级描述，这一份是带优先级的判定，两者对不上就是同一个握手有两套真相
    /// （判据里逐条比过）。
    /// </summary>
    public static string Describe(in PlanetHandshake expected, in PlanetHandshake actual)
    {
        var verdict = Evaluate(in expected, in actual);
        return verdict.Accepted
            ? verdict.Detail
            : string.Format(CultureInfo.InvariantCulture, "{0} / {1}", verdict.Field, verdict.Detail);
    }

    private static PlanetHandshakeVerdict Reject(PlanetHandshakeReject field, string detail) =>
        new(false, field, detail);
}

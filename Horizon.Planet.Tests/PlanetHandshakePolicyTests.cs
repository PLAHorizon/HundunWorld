using System;
using System.Collections.Generic;
using System.Linq;
using Horizon.Planet;
using Horizon.Planet.Wire;
using Xunit;

namespace Horizon.Planet.Tests;

/// <summary>
/// 星球口径握手的判定策略。§6.2 写着"terrainSeed/terrainVersion 不匹配直接拒绝进入世界"，
/// 但这句话此前只在客户端缓存那一侧有判据（§13.12 的 <c>reject_badterrainversion</c> 等计数），
/// 服务端一侧的比较逻辑一直不存在——虽然 <see cref="PlanetHandshake.DescribeMismatch"/> 早就写好了却没人调用。
/// 这里补的就是那半边，并且把"两套真相"的风险直接做成判据：判定结果必须与既有描述逐字相同。
/// </summary>
public class PlanetHandshakePolicyTests
{
    private static PlanetHandshake Expected() => new()
    {
        WireProtocol = PlanetHandshake.ProtocolVersion,
        PlanetRadiusCm = PlanetConstants.PlanetRadiusCm,
        RegionLevel = (byte)PlanetConstants.DefaultRegionLevel,
        TerrainSeed = 0x123456789ABCDEF0L,
        TerrainVersion = 0xA5A51234,
        BasefieldVersion = 7,
        BasefieldHashHi = unchecked((long)0xFEDCBA9876543210UL),
    };

    /// <summary>
    /// 结构体改副本。<b>必须是"改完返回"的 Func，不能是 Action</b>：
    /// 前几版写成 <c>Action&lt;PlanetHandshake&gt;</c>，lambda 收到的是一份形参副本，
    /// 改完丢掉，于是"我篡改了半径"这件事其实什么都没发生——判据当场绿给你看。
    /// 这条判据能红，正是因为发现了这个写法。
    /// </summary>
    private static PlanetHandshake Changed(PlanetHandshake baseValue, Func<PlanetHandshake, PlanetHandshake> mutate) =>
        mutate(baseValue);

    private static readonly List<(string Label, Func<PlanetHandshake, PlanetHandshake> Mutate, PlanetHandshakeReject Field)> Mismatches =
        new()
        {
            ("线协议", p => { p.WireProtocol = (ushort)(p.WireProtocol + 1); return p; }, PlanetHandshakeReject.WireProtocol),
            ("半径", p => { p.PlanetRadiusCm = PlanetConstants.EarthMeanRadiusCm; return p; }, PlanetHandshakeReject.PlanetRadius),
            ("层级", p => { p.RegionLevel = (byte)(p.RegionLevel - 1); return p; }, PlanetHandshakeReject.RegionLevel),
            ("种子", p => { p.TerrainSeed = 1; return p; }, PlanetHandshakeReject.TerrainSeed),
            ("地形版本", p => { p.TerrainVersion = 2; return p; }, PlanetHandshakeReject.TerrainVersion),
            ("基场版本", p => { p.BasefieldVersion = 8; return p; }, PlanetHandshakeReject.BasefieldVersion),
            ("基场哈希", p => { p.BasefieldHashHi = 0; return p; }, PlanetHandshakeReject.BasefieldHash),
        };

    [Fact]
    public void 篡改副本这件事本身要有效()
    {
        // 上面那条注释不是修辞：这一层如果静默失效，下面所有判据都会变成"两个相同值在比较"。
        var expected = Expected();
        var tampered = Changed(expected, p => { p.TerrainVersion = 2; return p; });
        Assert.NotEqual(expected.TerrainVersion, tampered.TerrainVersion);
        Assert.Equal(expected.PlanetRadiusCm, tampered.PlanetRadiusCm);      // 只该动那一项
        Assert.True(PlanetHandshakePolicy.Evaluate(expected, tampered).Field == PlanetHandshakeReject.TerrainVersion);
    }

    [Fact]
    public void 口径全同放行_理由一行说清了是哪颗星球()
    {
        var verdict = PlanetHandshakePolicy.Evaluate(Expected(), Expected());
        Assert.True(verdict.Accepted);
        Assert.Equal(PlanetHandshakeReject.None, verdict.Field);
        Assert.Contains("口径一致", verdict.Detail);
        Assert.Contains("terrainVersion", verdict.Detail);
    }

    [Fact]
    public void 七个字段各自不一致都给出自己的码_理由带两边的值()
    {
        var expected = Expected();
        var seen = new HashSet<PlanetHandshakeReject>();
        foreach (var (label, mutate, field) in Mismatches)
        {
            var actual = mutate(expected);
            var verdict = PlanetHandshakePolicy.Evaluate(expected, actual);
            Assert.False(verdict.Accepted, label);
            Assert.True(verdict.Field == field, $"{label}：期望码 {field}，实际 {verdict.Field}");
            Assert.True(seen.Add(verdict.Field), $"{label} 与其它分支撞码");
            Assert.False(string.IsNullOrWhiteSpace(verdict.Detail), label);
            // 理由里必须带上两边的值，否则排查时只能靠猜
            Assert.Contains("!=", verdict.Detail, StringComparison.Ordinal);

            // 同一件事的两种写法不许分叉：既有 DescribeMismatch 说什么，判定就说什么
            Assert.Equal(PlanetHandshake.DescribeMismatch(expected, actual), verdict.Detail);
        }
        Assert.Equal(Mismatches.Count, seen.Count);
        // 码段除了 None 与"线层读失败"之外，必须逐条对应一个字段——不许有发不出来的孤儿码
        Assert.Equal(Mismatches.Count, Enum.GetValues<PlanetHandshakeReject>().Length - 2);
    }

    [Fact]
    public void 比较顺序是线协议_星球身份_地形身份()
    {
        var expected = Expected();
        // 同时改两项：报的那一项必须按优先级来，否则排查方向会被引到错的地方
        Assert.Equal(PlanetHandshakeReject.WireProtocol, PlanetHandshakePolicy.Evaluate(expected,
            Changed(expected, p => { p.WireProtocol = 9; p.PlanetRadiusCm = 1; return p; })).Field);
        Assert.Equal(PlanetHandshakeReject.PlanetRadius, PlanetHandshakePolicy.Evaluate(expected,
            Changed(expected, p => { p.PlanetRadiusCm = 1; p.RegionLevel = 3; return p; })).Field);
        Assert.Equal(PlanetHandshakeReject.RegionLevel, PlanetHandshakePolicy.Evaluate(expected,
            Changed(expected, p => { p.RegionLevel = 3; p.TerrainSeed = 99; return p; })).Field);
        Assert.Equal(PlanetHandshakeReject.TerrainSeed, PlanetHandshakePolicy.Evaluate(expected,
            Changed(expected, p => { p.TerrainSeed = 99; p.TerrainVersion = 5; return p; })).Field);
        Assert.Equal(PlanetHandshakeReject.TerrainVersion, PlanetHandshakePolicy.Evaluate(expected,
            Changed(expected, p => { p.TerrainVersion = 5; p.BasefieldVersion = 6; return p; })).Field);
        Assert.Equal(PlanetHandshakeReject.BasefieldVersion, PlanetHandshakePolicy.Evaluate(expected,
            Changed(expected, p => { p.BasefieldVersion = 6; p.BasefieldHashHi = 0; return p; })).Field);

        // 正向对照：只改最末那一项时，报的就是它自己
        Assert.Equal(PlanetHandshakeReject.BasefieldHash, PlanetHandshakePolicy.Evaluate(expected,
            Changed(expected, p => { p.BasefieldHashHi = 0; return p; })).Field);
    }

    [Fact]
    public void 线层读失败先过_不许拿半包的字段去比()
    {
        var expected = Expected();
        var actual = Expected();

        var truncated = PlanetHandshakePolicy.EvaluateRead(PlanetWireStatus.Truncated, false, expected, actual);
        Assert.False(truncated.Accepted);
        Assert.Equal(PlanetHandshakeReject.WireRead, truncated.Field);
        Assert.Contains("Truncated", truncated.Detail);

        var trailing = PlanetHandshakePolicy.EvaluateRead(PlanetWireStatus.TrailingBytes, false, expected, actual);
        Assert.Equal(PlanetHandshakeReject.WireRead, trailing.Field);

        // 协议号不对时 TryRead 会标 UnsupportedProtocol：那归到 wire 协议这一项，而不是"包坏了"
        var wrongProtocol = PlanetHandshakePolicy.EvaluateRead(PlanetWireStatus.UnsupportedProtocol, false, expected, actual);
        Assert.Equal(PlanetHandshakeReject.WireProtocol, wrongProtocol.Field);

        // 正向孪生：读成功就走逐项比较
        Assert.True(PlanetHandshakePolicy.EvaluateRead(PlanetWireStatus.Ok, true, expected, actual).Accepted);
    }

    [Fact]
    public void 握手包编解码往返后判定不变()
    {
        var expected = Expected();
        var buffer = new byte[PlanetHandshake.Size];
        Assert.True(PlanetHandshake.TryWrite(buffer, expected, out int written));
        Assert.Equal(PlanetHandshake.Size, written);

        Assert.True(PlanetHandshake.TryRead(buffer, out var readBack, out var status, out int consumed));
        Assert.Equal(PlanetWireStatus.Ok, status);
        Assert.Equal(PlanetHandshake.Size, consumed);
        Assert.True(PlanetHandshakePolicy.Evaluate(expected, readBack).Accepted);

        // 每一项都要能活着穿过这 35 个字节：改一项、写、读、判，报的必须是那一项
        foreach (var (label, mutate, field) in Mismatches)
        {
            if (field == PlanetHandshakeReject.WireProtocol) continue;   // 协议号不对时 TryRead 直接标失败，那条走另一条判据
            var tampered = mutate(expected);
            var bytes = new byte[PlanetHandshake.Size];
            Assert.True(PlanetHandshake.TryWrite(bytes, tampered, out _), label);
            Assert.True(PlanetHandshake.TryRead(bytes, out var decoded, out _, out _), label);
            var verdict = PlanetHandshakePolicy.Evaluate(expected, decoded);
            Assert.True(verdict.Field == field, $"{label} 过一趟线之后报成了 {verdict.Field}");
        }
    }

    [Fact]
    public void 由TerrainProfile推出的口径自己一致_换基场就整组一起变()
    {
        var profile = new TerrainProfile(PlanetConstants.PlanetRadiusCm, 15.0 / 3600.0);   // 基场最细一级 15 角秒
        var hashA = unchecked((long)0xAAAABBBBCCCCDDDDUL);
        var hashB = unchecked((long)0x1111222233334444UL);

        var versionA = profile.ComputeTerrainVersion(hashA, 7);
        var seedA = profile.ComputeNoiseSeed(hashA, 7);
        Assert.Equal(versionA, profile.ComputeTerrainVersion(hashA, 7));    // 同输入同结果：双端才谈得上逐位一致
        Assert.Equal(seedA, profile.ComputeNoiseSeed(hashA, 7));

        var versionB = profile.ComputeTerrainVersion(hashB, 7);
        var seedB = profile.ComputeNoiseSeed(hashB, 7);
        Assert.NotEqual(versionA, versionB);
        Assert.NotEqual(seedA, seedB);
        Assert.NotEqual(0u, versionA);

        // 基场版本号也进版本哈希：只改它同样必须让 terrainVersion 变（§12.4 那条"改任何一个都得变"）
        Assert.NotEqual(versionA, profile.ComputeTerrainVersion(hashA, 8));

        // 用推出来的口径做一次握手：自洽；把基场换成另一份就必须被拒
        var caliber = new PlanetHandshake
        {
            WireProtocol = PlanetHandshake.ProtocolVersion,
            PlanetRadiusCm = profile.RadiusCm,
            RegionLevel = (byte)PlanetConstants.DefaultRegionLevel,
            TerrainSeed = seedA,
            TerrainVersion = versionA,
            BasefieldVersion = 7,
            BasefieldHashHi = hashA,
        };
        Assert.True(PlanetHandshakePolicy.Evaluate(caliber, caliber).Accepted);
        var other = Changed(caliber, p => { p.TerrainVersion = versionB; p.BasefieldHashHi = hashB; return p; });
        Assert.Equal(PlanetHandshakeReject.TerrainVersion, PlanetHandshakePolicy.Evaluate(caliber, other).Field);
    }

    [Fact]
    public void 一行日志与判定说的是同一件事()
    {
        var expected = Expected();
        Assert.Equal(PlanetHandshakePolicy.Evaluate(expected, expected).Detail,
                     PlanetHandshakePolicy.Describe(expected, expected));

        var actual = Changed(expected, p => { p.TerrainVersion = 42; return p; });
        var line = PlanetHandshakePolicy.Describe(expected, actual);
        Assert.Contains("TerrainVersion", line);                                  // 码名要在，日志才知道是哪一类
        Assert.Contains(PlanetHandshake.DescribeMismatch(expected, actual), line); // 值也要在，日志才能对着排查
    }
}

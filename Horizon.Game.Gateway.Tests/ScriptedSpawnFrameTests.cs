using Horizon.Game.Core.Sim.Server;
using Horizon.Game.Message.Network;
using Horizon.Game.Message.Security;
using System;
using System.Linq;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 脚本指令帧（<see cref="DsUplinkMessageKind.ScriptedSpawn"/> = 8）的**线上格式**测试。
/// <para>
/// 链路的其余三段都在同一进程里传对象，只有这一段跨进程：Grain 签好名的令牌要先被
/// MemoryPack 序列化、经 TCP 送到 DS、再反序列化回来验签。<b>签名保护的是字段值</b>，
/// 所以这一段真正要证明的不是"能编能解"，而是
/// <b>序列化之后 canonical 字段的每一位仍然一致</b>——任何一个字段在 wire 上被丢掉、
/// 改序或精度受损，表现都是 DS 侧 <c>signature-mismatch</c> 全量拒绝，
/// 而分段单测与端到端单测都看不见它（它们不经过序列化）。
/// </para>
/// </summary>
public class ScriptedSpawnFrameTests
{
    private static readonly byte[] Key = Enumerable.Repeat((byte)0x5A, 32).ToArray();

    /// <summary>每个字段都取"不会被默认值巧合掩盖"的非零值。</summary>
    private static ScriptGrantToken MakeSignedToken(long nowMs)
    {
        var token = new ScriptGrantToken
        {
            Nonce = 60006000600L,
            OperatorCharacterId = 7001,
            Op = ScriptOp.Spawn,
            Kind = ScriptedKind.TriggerZone,
            TemplateId = 3,
            TargetShardId = 9,
            X = 12.25f,
            Y = -3.5f,
            Z = 104.75f,
            Count = 4,
            TtlSeconds = 60,
            Radius = 7.125f,
            TriggerMode = ScriptTriggerMode.Repeat,
            CooldownSeconds = 15,
            TargetEntityId = 0xFFFF_FFFF_0000_0002UL,
            RequestId = 4711,
        };
        ScriptGrantTokenSigner.Sign(token, Key, nowMs, 5);
        return token;
    }

    [Fact]
    public void 脚本帧往返保真且验签仍通过()
    {
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var original = MakeSignedToken(nowMs);

        var frame = DsUplinkCodec.EncodeFrame(DsUplinkMessageKind.ScriptedSpawn,
            new DsScriptedSpawnEnvelope { Token = original });

        var reader = new DsFrameReader();
        reader.Append(frame);
        Assert.True(reader.TryReadFrame(out var payload));
        Assert.True(DsUplinkCodec.TryDecode<DsScriptedSpawnEnvelope>(payload, out var kind, out var envelope));
        Assert.Equal(DsUplinkMessageKind.ScriptedSpawn, kind);
        var decoded = envelope!.Token!;

        // canonical 字节里的每一项都必须逐位一致——少一项都可能被"看起来正常"掩盖。
        Assert.Equal(original.Nonce, decoded.Nonce);
        Assert.Equal(original.OperatorCharacterId, decoded.OperatorCharacterId);
        Assert.Equal(original.Op, decoded.Op);
        Assert.Equal(original.Kind, decoded.Kind);
        Assert.Equal(original.TemplateId, decoded.TemplateId);
        Assert.Equal(original.TargetShardId, decoded.TargetShardId);
        Assert.Equal(original.X, decoded.X);
        Assert.Equal(original.Y, decoded.Y);
        Assert.Equal(original.Z, decoded.Z);
        Assert.Equal(original.Count, decoded.Count);
        Assert.Equal(original.TtlSeconds, decoded.TtlSeconds);
        Assert.Equal(original.Radius, decoded.Radius);
        Assert.Equal(original.TriggerMode, decoded.TriggerMode);
        Assert.Equal(original.CooldownSeconds, decoded.CooldownSeconds);
        Assert.Equal(original.TargetEntityId, decoded.TargetEntityId);
        Assert.Equal(original.RequestId, decoded.RequestId);
        Assert.Equal(original.IssuedAtUnixMs, decoded.IssuedAtUnixMs);
        Assert.Equal(original.ExpiresAtUnixMs, decoded.ExpiresAtUnixMs);
        Assert.Equal(original.Signature, decoded.Signature);

        // 决定性断言：反序列化出来的令牌用同一把密钥仍然验得过。
        Assert.True(ScriptGrantTokenSigner.Validate(decoded, Key, nowMs + 100, out var reason), reason);
    }

    [Fact]
    public void 线上传输后被篡改一个坐标即验签失败()
    {
        // 签名覆盖坐标这件事必须在"过了序列化边界"之后依然成立：否则中途改一个数
        // 就能把生成点挪到任意位置。
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var frame = DsUplinkCodec.EncodeFrame(DsUplinkMessageKind.ScriptedSpawn,
            new DsScriptedSpawnEnvelope { Token = MakeSignedToken(nowMs) });

        var reader = new DsFrameReader();
        reader.Append(frame);
        Assert.True(reader.TryReadFrame(out var payload));
        Assert.True(DsUplinkCodec.TryDecode<DsScriptedSpawnEnvelope>(payload, out _, out var envelope));

        envelope!.Token!.X += 500f;

        Assert.False(ScriptGrantTokenSigner.Validate(envelope.Token, Key, nowMs + 100, out var reason));
        Assert.Equal("signature-mismatch", reason);
    }

    [Fact]
    public void 脚本帧与其它控制帧粘包时按序解出()
    {
        // 真实 TCP 会把注册帧与脚本帧并进同一次接收；顺序与 kind 都不能错，
        // 否则脚本指令会被当成别的消息丢弃。
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var register = DsUplinkCodec.EncodeFrame(DsUplinkMessageKind.Register, new DsRegisterRequest
        {
            DsId = "ds-1",
            ShardIds = new long[] { 3, 4 },
            Port = 19880,
        });
        var scripted = DsUplinkCodec.EncodeFrame(DsUplinkMessageKind.ScriptedSpawn,
            new DsScriptedSpawnEnvelope { Token = MakeSignedToken(nowMs) });

        var stream = new byte[register.Length + scripted.Length];
        register.CopyTo(stream, 0);
        scripted.CopyTo(stream, register.Length);

        var reader = new DsFrameReader();
        reader.Append(stream);

        Assert.True(reader.TryReadFrame(out var first));
        Assert.True(DsUplinkCodec.TryDecode<DsRegisterRequest>(first, out var firstKind, out var registerBody));
        Assert.Equal(DsUplinkMessageKind.Register, firstKind);
        Assert.Equal(new long[] { 3, 4 }, registerBody!.ShardIds);
        reader.ConsumeFrame();

        Assert.True(reader.TryReadFrame(out var second));
        Assert.True(DsUplinkCodec.TryDecode<DsScriptedSpawnEnvelope>(second, out var secondKind, out var scriptedBody));
        Assert.Equal(DsUplinkMessageKind.ScriptedSpawn, secondKind);
        Assert.True(ScriptGrantTokenSigner.Validate(scriptedBody!.Token!, Key, nowMs + 100, out _));
        reader.ConsumeFrame();

        Assert.False(reader.TryReadFrame(out _));
    }

    [Fact]
    public void 空信封帧不抛异常且被丢弃()
    {
        // 网关不会发 Token=null，但线上输入不可信：解码必须安静失败（DS 侧计数丢弃），
        // 而不是在 tick 线程上抛出异常拖垮模拟。
        var frame = DsUplinkCodec.EncodeFrame(DsUplinkMessageKind.ScriptedSpawn,
            new DsScriptedSpawnEnvelope { Token = null! });

        var reader = new DsFrameReader();
        reader.Append(frame);
        Assert.True(reader.TryReadFrame(out var payload));

        // TryDecode 只认 body is not null：Token=null 的信封仍可解出，
        // 因此真正的兜底在 DS 入队处——这里把它接到底，证明 null 令牌确实被拒而不是被忽略。
        Assert.True(DsUplinkCodec.TryDecode<DsScriptedSpawnEnvelope>(payload, out _, out var envelope));
        Assert.Null(envelope!.Token);

        var session = new DsWorldSession(new DsWorldSessionOptions
        {
            ShardId = 9,
            EnableScriptedSpawn = true,
            ScriptedSigningKey = Key,
        });
        Assert.Equal("null-token", session.EnqueueScriptedCommand(envelope.Token));
    }
}

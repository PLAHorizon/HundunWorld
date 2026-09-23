using System;
using System.Buffers.Binary;
using Horizon.Game.Core.Sim.Server;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// MMORPG 开发计划 M2 —— DS 上行协议（控制面 + 定向下行帧）单元测试。
/// <para>
/// 覆盖目标：长度前缀分帧在 TCP 字节流下的**粘包/拆包**、超长帧拒绝（DoS 防护）、
/// 编解码往返一致性、以及「未知 kind 前向兼容不抛异常」。
/// </para>
/// <para>
/// 这些是 DS↔网关闭环（验收门 H2）里最容易出线上事故的一层：DS 侧（UnrealSharp）
/// 与网关侧共用同一份实现，任何分帧语义错误会表现为"偶发丢包/半包解析"，极难排查。
/// 因此把分帧逻辑做成纯函数并在此穷举边界。
/// </para>
/// </summary>
public class DsUplinkProtocolTests
{
    // ---------- 分帧：完整帧 ----------

    [Fact]
    public void TryReadFrame_SingleCompleteFrame_ReturnsPayload()
    {
        var reader = new DsFrameReader();
        var frame = DsUplinkCodec.FramePayload(new byte[] { 1, 2, 3, 4 });

        reader.Append(frame);

        Assert.True(reader.TryReadFrame(out var payload));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, payload.ToArray());

        reader.ConsumeFrame();
        Assert.Equal(0, reader.BufferedBytes);
        Assert.False(reader.TryReadFrame(out _));
    }

    [Fact]
    public void TryReadFrame_EmptyPayload_IsValidFrame()
    {
        var reader = new DsFrameReader();
        reader.Append(DsUplinkCodec.FramePayload(ReadOnlySpan<byte>.Empty));

        Assert.True(reader.TryReadFrame(out var payload));
        Assert.Equal(0, payload.Length);

        reader.ConsumeFrame();
        Assert.Equal(0, reader.BufferedBytes);
    }

    // ---------- 分帧：拆包（半帧到达）----------

    [Fact]
    public void TryReadFrame_SplitAcrossAppends_OnlyCompletesWhenWhole()
    {
        var reader = new DsFrameReader();
        var frame = DsUplinkCodec.FramePayload(new byte[] { 10, 20, 30 });

        // 先到一半（含长度前缀的一部分）
        reader.Append(frame.AsSpan(0, 3));
        Assert.False(reader.TryReadFrame(out _));

        // 再到长度前缀剩余部分——仍不完整（负载未到齐）
        reader.Append(frame.AsSpan(3, 3));
        Assert.False(reader.TryReadFrame(out _));

        // 最后到达尾部
        reader.Append(frame.AsSpan(6));
        Assert.True(reader.TryReadFrame(out var payload));
        Assert.Equal(new byte[] { 10, 20, 30 }, payload.ToArray());

        reader.ConsumeFrame();
        Assert.Equal(0, reader.BufferedBytes);
    }

    [Fact]
    public void TryReadFrame_ByteByByteArrival_EventuallySucceeds()
    {
        var reader = new DsFrameReader(initialCapacity: 4);
        var frame = DsUplinkCodec.FramePayload(new byte[] { 7, 7, 7, 7, 7, 7, 7, 7 });

        for (var i = 0; i < frame.Length - 1; i++)
        {
            reader.Append(frame.AsSpan(i, 1));
            Assert.False(reader.TryReadFrame(out _));
        }

        reader.Append(frame.AsSpan(frame.Length - 1, 1));
        Assert.True(reader.TryReadFrame(out var payload));
        Assert.Equal(8, payload.Length);

        reader.ConsumeFrame();
        Assert.Equal(0, reader.BufferedBytes);
    }

    // ---------- 分帧：粘包（一帧半 / 多帧）----------

    [Fact]
    public void TryReadFrame_StickyPackets_YieldsFramesInOrder()
    {
        var reader = new DsFrameReader();
        var a = DsUplinkCodec.FramePayload(new byte[] { 1 });
        var b = DsUplinkCodec.FramePayload(new byte[] { 2, 2 });
        var c = DsUplinkCodec.FramePayload(new byte[] { 3, 3, 3 });

        var combined = new byte[a.Length + b.Length + c.Length];
        a.CopyTo(combined, 0);
        b.CopyTo(combined, a.Length);
        c.CopyTo(combined, a.Length + b.Length);
        reader.Append(combined);

        Assert.True(reader.TryReadFrame(out var p1));
        Assert.Equal(new byte[] { 1 }, p1.ToArray());
        reader.ConsumeFrame();

        Assert.True(reader.TryReadFrame(out var p2));
        Assert.Equal(new byte[] { 2, 2 }, p2.ToArray());
        reader.ConsumeFrame();

        Assert.True(reader.TryReadFrame(out var p3));
        Assert.Equal(new byte[] { 3, 3, 3 }, p3.ToArray());
        reader.ConsumeFrame();

        Assert.False(reader.TryReadFrame(out _));
        Assert.Equal(0, reader.BufferedBytes);
    }

    [Fact]
    public void TryReadFrame_FramePlusPartialNext_LeavesRemainderBuffered()
    {
        var reader = new DsFrameReader();
        var a = DsUplinkCodec.FramePayload(new byte[] { 9, 9 });
        var b = DsUplinkCodec.FramePayload(new byte[] { 8, 8, 8, 8 });

        var combined = new byte[a.Length + 5];
        a.CopyTo(combined, 0);
        b.AsSpan(0, 5).CopyTo(combined.AsSpan(a.Length));
        reader.Append(combined);

        Assert.True(reader.TryReadFrame(out var p1));
        Assert.Equal(new byte[] { 9, 9 }, p1.ToArray());
        reader.ConsumeFrame();

        Assert.False(reader.TryReadFrame(out _));
        Assert.Equal(5, reader.BufferedBytes);

        // 剩余部分补齐后应能成帧。
        reader.Append(b.AsSpan(5));
        Assert.True(reader.TryReadFrame(out var p2));
        Assert.Equal(new byte[] { 8, 8, 8, 8 }, p2.ToArray());
    }

    // ---------- 分帧：超长帧拒绝（DoS 防护）----------

    [Fact]
    public void TryReadFrame_OversizedDeclaredLength_IsRejectedAndCounted()
    {
        var reader = new DsFrameReader(maxPayloadBytes: 1024);
        var header = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(header, 1024 * 1024); // 声明 1MiB，超过上限
        reader.Append(header);

        Assert.False(reader.TryReadFrame(out _));
        Assert.Equal(1, reader.OversizedFrameCount);
        Assert.Equal(0, reader.BufferedBytes); // 非法帧后缓冲被清空
    }

    [Fact]
    public void TryReadFrame_NegativeDeclaredLength_IsRejectedAndCounted()
    {
        var reader = new DsFrameReader();
        var header = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(header, -1);
        reader.Append(header);

        Assert.False(reader.TryReadFrame(out _));
        Assert.Equal(1, reader.OversizedFrameCount);
    }

    [Fact]
    public void FramePayload_ExceedingMax_Throws()
    {
        var oversized = new byte[DsUplinkCodec.MaxPayloadBytes + 1];
        Assert.Throws<ArgumentOutOfRangeException>(() => DsUplinkCodec.FramePayload(oversized));
    }

    // ---------- 编解码：往返 ----------

    [Fact]
    public void Register_RoundTrip_PreservesAllFields()
    {
        var request = new DsRegisterRequest
        {
            DsId = "ds-shard-0",
            ShardIds = new long[] { 0, 3, 7 },
            Host = "10.0.0.12",
            Port = 19870,
            Region = "华东",
            Token = "s3cr3t",
        };

        var frame = DsUplinkCodec.EncodeFrame(DsUplinkMessageKind.Register, request);

        var reader = new DsFrameReader();
        reader.Append(frame);
        Assert.True(reader.TryReadFrame(out var payload));
        reader.ConsumeFrame();

        Assert.True(DsUplinkCodec.TryDecode<DsRegisterRequest>(payload, out var kind, out var decoded));
        Assert.Equal(DsUplinkMessageKind.Register, kind);
        Assert.NotNull(decoded);
        Assert.Equal("ds-shard-0", decoded!.DsId);
        Assert.Equal(new long[] { 0, 3, 7 }, decoded.ShardIds);
        Assert.Equal("10.0.0.12", decoded.Host);
        Assert.Equal(19870, decoded.Port);
        Assert.Equal("华东", decoded.Region);
        Assert.Equal("s3cr3t", decoded.Token);
    }

    [Fact]
    public void SnapshotBatch_RoundTrip_PreservesSessionTargetingAndFrames()
    {
        var batch = new DsSnapshotBatch
        {
            Items = new[]
            {
                new DsSnapshotItem { SessionIds = new long[] { 1001 }, Frame = new byte[] { 0xAA, 0xBB } },
                new DsSnapshotItem { SessionIds = new long[] { 1002, 1003 }, Frame = new byte[] { 0xCC } },
            },
        };

        var frame = DsUplinkCodec.EncodeFrame(DsUplinkMessageKind.SnapshotBatch, batch);

        var reader = new DsFrameReader();
        reader.Append(frame);
        Assert.True(reader.TryReadFrame(out var payload));
        reader.ConsumeFrame();

        Assert.True(DsUplinkCodec.TryDecode<DsSnapshotBatch>(payload, out var kind, out var decoded));
        Assert.Equal(DsUplinkMessageKind.SnapshotBatch, kind);
        Assert.NotNull(decoded);
        Assert.Equal(2, decoded!.Items.Length);
        Assert.Equal(new long[] { 1002, 1003 }, decoded.Items[1].SessionIds);
        Assert.Equal(new byte[] { 0xAA, 0xBB }, decoded.Items[0].Frame);
    }

    [Fact]
    public void ControlAck_RoundTrip_PreservesFailureReason()
    {
        var ack = new DsControlAck { Ok = false, Reason = "no_ds_route" };
        var frame = DsUplinkCodec.EncodeFrame(DsUplinkMessageKind.ControlAck, ack);

        var reader = new DsFrameReader();
        reader.Append(frame);
        Assert.True(reader.TryReadFrame(out var payload));

        Assert.True(DsUplinkCodec.TryDecode<DsControlAck>(payload, out _, out var decoded));
        Assert.False(decoded!.Ok);
        Assert.Equal("no_ds_route", decoded.Reason);
    }

    // ---------- 前向兼容与健壮性 ----------

    [Fact]
    public void TryDecode_UnknownKind_DoesNotThrow()
    {
        var payload = new byte[] { 250, 1, 2, 3 }; // kind=250 未定义
        var ok = DsUplinkCodec.TryDecode<DsRegisterRequest>(payload, out var kind, out var body);

        Assert.False(ok);
        Assert.Null(body);
        Assert.Equal((DsUplinkMessageKind)250, kind);
    }

    [Fact]
    public void TryDecode_TruncatedBody_DoesNotThrow()
    {
        var payload = new byte[] { (byte)DsUplinkMessageKind.Register, 0xFF, 0x01 };
        var ok = DsUplinkCodec.TryDecode<DsRegisterRequest>(payload, out _, out var body);

        Assert.False(ok);
        Assert.Null(body);
    }

    [Fact]
    public void TryDecode_EmptyPayload_DoesNotThrow()
    {
        Assert.False(DsUplinkCodec.TryDecode<DsRegisterRequest>(ReadOnlySpan<byte>.Empty, out _, out _));
    }

    [Fact]
    public void TryDecode_KindOnlyPayload_IsRejected()
    {
        var payload = new byte[] { (byte)DsUplinkMessageKind.Heartbeat };
        Assert.False(DsUplinkCodec.TryDecode<DsIdentityRequest>(payload, out var kind, out _));
        Assert.Equal(DsUplinkMessageKind.Heartbeat, kind);
    }

    [Fact]
    public void Reader_GrowsPastInitialCapacity_WithoutLosingData()
    {
        var reader = new DsFrameReader(initialCapacity: 8, maxPayloadBytes: 4096);
        var big = new byte[2048];
        for (var i = 0; i < big.Length; i++) big[i] = (byte)(i % 251);

        reader.Append(DsUplinkCodec.FramePayload(big));

        Assert.True(reader.TryReadFrame(out var payload));
        Assert.Equal(2048, payload.Length);
        Assert.Equal(big, payload.ToArray());
    }
}

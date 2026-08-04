using System;
using Horizon.Game.Message.Sync;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// Phase C5 验证：ReconnectResumePacket 编解码 + 字段完整性。
/// </summary>
public class ReconnectResumeTests
{
    [Fact]
    public void ReconnectResumePacket_HasCorrectKind()
    {
        var packet = new ReconnectResumePacket();
        Assert.Equal(SyncPacketKind.ReconnectResume, packet.Kind);
    }

    [Fact]
    public void ReconnectResumePacket_EncodeDecode_RoundTrip()
    {
        var original = new ReconnectResumePacket
        {
            LocalCharacterId = 12345UL,
            LastAppliedSnapshotTick = 98765L,
            LastAppliedDiffSeq = 42L,
            BaselineVersion = 3,
            WorldPatchVersion = 7,
        };

        // 编码
        SyncPacketCodec.Encode(original, out var frame, out var frameLength);
        Assert.True(frameLength > 0);

        // 解码
        var payload = new byte[frameLength];
        Buffer.BlockCopy(frame, 0, payload, 0, frameLength);
        SyncPacketCodec.ReturnFrame(frame);

        var decoded = SyncPacketCodec.Decode(payload) as ReconnectResumePacket;
        Assert.NotNull(decoded);
        Assert.Equal(12345UL, decoded!.LocalCharacterId);
        Assert.Equal(98765L, decoded.LastAppliedSnapshotTick);
        Assert.Equal(42L, decoded.LastAppliedDiffSeq);
        Assert.Equal(3, decoded.BaselineVersion);
        Assert.Equal(7, decoded.WorldPatchVersion);
    }

    [Fact]
    public void ReconnectResumePacket_ZeroTick_EncodesCorrectly()
    {
        var packet = new ReconnectResumePacket
        {
            LocalCharacterId = 1UL,
            LastAppliedSnapshotTick = 0L, // 首次连接后断线，无已应用快照
            LastAppliedDiffSeq = 0L,
        };

        SyncPacketCodec.Encode(packet, out var frame, out var frameLength);
        var payload = new byte[frameLength];
        Buffer.BlockCopy(frame, 0, payload, 0, frameLength);
        SyncPacketCodec.ReturnFrame(frame);

        var decoded = SyncPacketCodec.Decode(payload) as ReconnectResumePacket;
        Assert.NotNull(decoded);
        Assert.Equal(0L, decoded!.LastAppliedSnapshotTick);
        Assert.Equal(SyncPacketKind.ReconnectResume, decoded.Kind);
    }

    [Fact]
    public void ReconnectResumePacket_LargeTick_EncodesCorrectly()
    {
        var packet = new ReconnectResumePacket
        {
            LocalCharacterId = ulong.MaxValue,
            LastAppliedSnapshotTick = long.MaxValue,
            LastAppliedDiffSeq = long.MaxValue,
        };

        SyncPacketCodec.Encode(packet, out var frame, out var frameLength);
        var payload = new byte[frameLength];
        Buffer.BlockCopy(frame, 0, payload, 0, frameLength);
        SyncPacketCodec.ReturnFrame(frame);

        var decoded = SyncPacketCodec.Decode(payload) as ReconnectResumePacket;
        Assert.NotNull(decoded);
        Assert.Equal(ulong.MaxValue, decoded!.LocalCharacterId);
        Assert.Equal(long.MaxValue, decoded.LastAppliedSnapshotTick);
    }

    [Fact]
    public void SyncPacketKind_ReconnectResume_HasExpectedValue()
    {
        // 协议定义 Kind=8 为 ReconnectResume
        Assert.Equal(8, (int)SyncPacketKind.ReconnectResume);
    }
}


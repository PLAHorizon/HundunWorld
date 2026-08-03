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

/// <summary>
/// Phase C1 验证：NetworkSyncManager 遗留预测逻辑跳过（结构验证）。
/// 注：NetworkSyncManager 依赖 FlaxEngine 运行时（Script/Time/Vector3），
/// 无法在纯 xunit 环境中实例化。此处验证协议层和标志位的正确性。
/// </summary>
public class NetworkSyncManagerLegacyTests
{
    [Fact]
    public void SyncProtocolVersion_Current_IsV7()
    {
        // 确认协议版本已递增到 7（新增 BaselineResyncRequestPacket）
        Assert.Equal(7, Horizon.Game.Message.Sync.SyncProtocolVersion.Current);
    }

    [Fact]
    public void InputPacket_EncodeDecode_PreservesClientTick()
    {
        // 验证 InputPacket 的 ClientTick 在编解码后保持一致（ECS 管线依赖此字段做 reconciliation）
        var input = new InputPacket
        {
            ClientTick = 12345L,
            InputBits = 0b1010u,
            LookYaw = 1.57f,
            LookPitch = -0.3f,
        };

        SyncPacketCodec.Encode(input, out var frame, out var frameLength);
        var payload = new byte[frameLength];
        Buffer.BlockCopy(frame, 0, payload, 0, frameLength);
        SyncPacketCodec.ReturnFrame(frame);

        var decoded = SyncPacketCodec.Decode(payload) as InputPacket;
        Assert.NotNull(decoded);
        Assert.Equal(12345L, decoded!.ClientTick);
        Assert.Equal(0b1010u, decoded.InputBits);
        Assert.Equal(1.57f, decoded.LookYaw, 0.001f);
        Assert.Equal(-0.3f, decoded.LookPitch, 0.001f);
    }

    [Fact]
    public void InputAckPacket_EncodeDecode_PreservesEchoClientTick()
    {
        // 验证 InputAckPacket 的 EchoClientTick（RTT 计算依赖此字段）
        var ack = new InputAckPacket
        {
            EchoClientTick = 99999L,
            LastProcessedClientTick = 100L,
            ServerTick = 5000L,
        };

        SyncPacketCodec.Encode(ack, out var frame, out var frameLength);
        var payload = new byte[frameLength];
        Buffer.BlockCopy(frame, 0, payload, 0, frameLength);
        SyncPacketCodec.ReturnFrame(frame);

        var decoded = SyncPacketCodec.Decode(payload) as InputAckPacket;
        Assert.NotNull(decoded);
        Assert.Equal(99999L, decoded!.EchoClientTick);
        Assert.Equal(100L, decoded.LastProcessedClientTick);
        Assert.Equal(5000L, decoded.ServerTick);
    }

    [Fact]
    public void SnapshotPacket_EncodeDecode_PreservesServerTick()
    {
        // 验证 SnapshotPacket 的 ServerTick（lastAppliedServerTick 依赖此字段）
        var snapshot = new SnapshotPacket
        {
            ServerTick = 55555L,
            BaselineTick = 0L, // 全量快照
            Deltas = new EntityDelta[]
            {
                new EntityDelta
                {
                    EntityId = 1UL,
                    Kind = EntityDeltaKind.Spawn,
                    Transform = new Horizon.Game.Message.Sync.Components.AuthTransformComponent
                    {
                        X = 10f,
                        Y = 0f,
                        Z = 20f,
                    },
                },
            },
        };

        SyncPacketCodec.Encode(snapshot, out var frame, out var frameLength);
        var payload = new byte[frameLength];
        Buffer.BlockCopy(frame, 0, payload, 0, frameLength);
        SyncPacketCodec.ReturnFrame(frame);

        var decoded = SyncPacketCodec.Decode(payload) as SnapshotPacket;
        Assert.NotNull(decoded);
        Assert.Equal(55555L, decoded!.ServerTick);
        Assert.Equal(0L, decoded.BaselineTick);
        Assert.Single(decoded.Deltas);
        Assert.Equal(1UL, decoded.Deltas[0].EntityId);
    }
}

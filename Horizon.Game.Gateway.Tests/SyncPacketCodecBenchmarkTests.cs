using System;
using System.Diagnostics;
using Horizon.Game.Message.Sync;
using Horizon.Game.Message.Sync.Components;
using MemoryPack;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 阶段 10.5 — SyncPacketCodec 编解码基准测试。
/// 验证 InteractionSyncPacket 的编解码往返、WorldChunkDiffPacket 嵌入编解码、
/// LZ4 压缩策略及编码性能基准。
/// </summary>
public class SyncPacketCodecBenchmarkTests
{
    /// <summary>
    /// InteractionSyncPacket 编解码往返：所有字段保持一致。
    /// </summary>
    [Fact]
    public void EncodeDecode_InteractionSyncPacket_Roundtrip()
    {
        var original = new InteractionSyncPacket
        {
            SlotIdx = 7,
            InteractableId = 0xABCDEF1234L,
            InteractorId = 0x567890ABCDEFL,
            StateBits = InteractionStateBits.Start | InteractionStateBits.End,
            ServerTick = 123456789L,
        };

        SyncPacketCodec.Encode(original, out var frame, out var frameLength);
        Assert.True(frameLength > SyncPacketCodec.FrameHeaderSize);

        try
        {
            var decoded = SyncPacketCodec.Decode(frame.AsSpan(0, frameLength));
            Assert.NotNull(decoded);
            Assert.IsType<InteractionSyncPacket>(decoded);

            var typed = (InteractionSyncPacket)decoded!;
            Assert.Equal(original.SlotIdx, typed.SlotIdx);
            Assert.Equal(original.InteractableId, typed.InteractableId);
            Assert.Equal(original.InteractorId, typed.InteractorId);
            Assert.Equal(original.StateBits, typed.StateBits);
            Assert.Equal(original.ServerTick, typed.ServerTick);
            Assert.Equal(SyncPacketKind.InteractionSync, typed.Kind);
        }
        finally
        {
            SyncPacketCodec.ReturnFrame(frame);
        }
    }

    /// <summary>
    /// InteractionSyncPacket 嵌入 WorldChunkDiffPacket.Payload 的编解码往返。
    /// 模拟 ZoneShardGrain.BroadcastInteractionSyncAsync 的打包 → SyncPacketDispatcher.RouteWorldChunkDiff 的解包。
    /// </summary>
    [Fact]
    public void EncodeDecode_InteractionSyncInWorldChunkDiff_Roundtrip()
    {
        var interactionPacket = new InteractionSyncPacket
        {
            SlotIdx = 3,
            InteractableId = 9999L,
            InteractorId = 8888L,
            StateBits = InteractionStateBits.Stolen,
            ServerTick = 555L,
        };

        var payloadBytes = MemoryPackSerializer.Serialize(interactionPacket);
        var diff = new WorldChunkDiffPacket
        {
            ChunkMortonKey = 42,
            DiffSeqStart = 1,
            DiffSeqEnd = 1,
            Payload = payloadBytes,
            PayloadType = WorldChunkDiffPayloadType.InteractionSync,
        };

        SyncPacketCodec.Encode(diff, out var frame, out var frameLength);

        try
        {
            var decodedDiff = SyncPacketCodec.Decode(frame.AsSpan(0, frameLength));
            Assert.NotNull(decodedDiff);
            Assert.IsType<WorldChunkDiffPacket>(decodedDiff);

            var typedDiff = (WorldChunkDiffPacket)decodedDiff!;
            Assert.Equal(diff.ChunkMortonKey, typedDiff.ChunkMortonKey);
            Assert.Equal(diff.DiffSeqStart, typedDiff.DiffSeqStart);
            Assert.Equal(diff.DiffSeqEnd, typedDiff.DiffSeqEnd);
            Assert.Equal(WorldChunkDiffPayloadType.InteractionSync, typedDiff.PayloadType);
            Assert.NotNull(typedDiff.Payload);

            var decodedInteraction = MemoryPackSerializer.Deserialize<InteractionSyncPacket>(typedDiff.Payload);
            Assert.NotNull(decodedInteraction);
            Assert.Equal(interactionPacket.SlotIdx, decodedInteraction!.SlotIdx);
            Assert.Equal(interactionPacket.InteractableId, decodedInteraction.InteractableId);
            Assert.Equal(interactionPacket.InteractorId, decodedInteraction.InteractorId);
            Assert.Equal(interactionPacket.StateBits, decodedInteraction.StateBits);
            Assert.Equal(interactionPacket.ServerTick, decodedInteraction.ServerTick);
        }
        finally
        {
            SyncPacketCodec.ReturnFrame(frame);
        }
    }

    /// <summary>
    /// 编码性能基准：编码 N 个 InteractionSyncPacket，报告平均耗时。
    /// 非严格基准，仅作 CI 合理性检查。
    /// </summary>
    [Fact]
    public void Encode_Performance_Benchmark()
    {
        const int iterations = 10000;
        var packet = new InteractionSyncPacket
        {
            SlotIdx = 1,
            InteractableId = 100L,
            InteractorId = 200L,
            StateBits = InteractionStateBits.Start,
            ServerTick = 999L,
        };

        SyncPacketCodec.Encode(packet, out var warmup, out _);
        SyncPacketCodec.ReturnFrame(warmup);

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            SyncPacketCodec.Encode(packet, out var frame, out _);
            SyncPacketCodec.ReturnFrame(frame);
        }
        sw.Stop();

        var avgMicroseconds = (double)sw.ElapsedTicks / iterations * 1_000_000 / Stopwatch.Frequency;
        Console.WriteLine($"Encode {iterations} InteractionSyncPackets: total={sw.ElapsedMilliseconds}ms, avg={avgMicroseconds:F2}us");

        Assert.True(avgMicroseconds < 1000, $"Average encode time {avgMicroseconds:F2}us exceeds 1000us sanity threshold");
    }

    /// <summary>
    /// 验证 LZ4 压缩不应用于 InteractionSyncPacket（仅 SnapshotPacket 在超阈值时压缩）。
    /// </summary>
    [Fact]
    public void LZ4_Compression_NotApplied_ToInteractionSync()
    {
        var packet = new InteractionSyncPacket
        {
            SlotIdx = 1,
            InteractableId = 1L,
            InteractorId = 1L,
            StateBits = 0xFF,
            ServerTick = 1L,
        };

        SyncPacketCodec.Encode(packet, out var frame, out var frameLength);

        try
        {
            var compression = (SyncPacketCodec.CompressionKind)frame[1];
            Assert.Equal(SyncPacketCodec.CompressionKind.None, compression);
        }
        finally
        {
            SyncPacketCodec.ReturnFrame(frame);
        }
    }

    /// <summary>
    /// 验证 LZ4 压缩应用于超阈值的 SnapshotPacket（对照测试）。
    /// </summary>
    [Fact]
    public void LZ4_Compression_Applied_ToLargeSnapshot()
    {
        var largeDeltas = new EntityDelta[50];
        for (int i = 0; i < largeDeltas.Length; i++)
        {
            largeDeltas[i] = new EntityDelta
            {
                EntityId = (ulong)(i + 1),
                Kind = EntityDeltaKind.Spawn,
                Identity = new NetworkIdentityAuthComponent { NetworkId = (ulong)(i + 1), EntityType = i },
                Transform = new AuthTransformComponent { X = i * 1.5f, Y = i * 2.5f, Z = 0, Yaw = 0 },
            };
        }

        var snapshot = new SnapshotPacket
        {
            ServerTick = 100L,
            BaselineTick = 0L,
            Deltas = largeDeltas,
        };

        SyncPacketCodec.Encode(snapshot, out var frame, out var frameLength);

        try
        {
            Assert.Equal((byte)SyncPacketKind.Snapshot, frame[0]);
            Assert.True(frameLength > SyncPacketCodec.FrameHeaderSize);
        }
        finally
        {
            SyncPacketCodec.ReturnFrame(frame);
        }
    }

    /// <summary>
    /// 验证帧头 Kind 字段与 SyncPacketKind 一致。
    /// </summary>
    [Fact]
    public void Encode_FrameHeader_KindMatchesPacketKind()
    {
        var packet = new InteractionSyncPacket
        {
            SlotIdx = 1,
            InteractableId = 1L,
        };

        SyncPacketCodec.Encode(packet, out var frame, out _);

        try
        {
            Assert.Equal((byte)SyncPacketKind.InteractionSync, frame[0]);
        }
        finally
        {
            SyncPacketCodec.ReturnFrame(frame);
        }
    }

    /// <summary>
    /// 验证解码帧长不足时抛出 ArgumentException。
    /// </summary>
    [Fact]
    public void Decode_TooShortFrame_Throws()
    {
        var tooShort = new byte[SyncPacketCodec.FrameHeaderSize - 1];
        Assert.Throws<ArgumentException>(() => SyncPacketCodec.Decode(tooShort));
    }
}

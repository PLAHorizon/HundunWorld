using System;
using System.Buffers;
using Horizon.Game.Message.Sync;
using Horizon.Game.Message.Sync.Components;
using MemoryPack;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// SyncPacketCodec LZ4 重试路径回归测试（对应 BUG 1：双重归还 ArrayPool）。
/// <para>
/// BUG 1 现象：Decode 在 LZ4 首次 hint 不足进入重试分支时，显式 Return(rented) 后
/// 外层 finally 再次 Return(rented)，导致 ArrayPool freelist 损坏、同一缓冲区被多次借出。
/// </para>
/// <para>
/// 本测试通过 <see cref="SyncPacketCodec.Decode(ReadOnlySpan{byte}, int)"/> 的
/// <c>originalPayloadLengthHint</c> 参数传入一个故意小的值（如 8），强制首次 hint 不足
/// 触发重试路径，而重试 biggerSize（= payloadLength * 16）对中等压缩比数据足够解压成功。
/// 这样无需构造极端高压缩比数据即可覆盖重试路径。
/// </para>
/// </summary>
public class SyncPacketCodecLz4RetryTests
{
    /// <summary>
    /// 构造 SnapshotPacket。deltaCount 个 EntityDelta，每个 delta 的 EntityId/Transform/Identity
    /// 均按 i 取不同值，确保序列化后 LZ4 压缩比低（&lt; 4x），使首次 hint=payloadLength*4 即可解压成功。
    /// 重试路径仅通过 originalPayloadLengthHint=8 强制触发。
    /// </summary>
    private static SnapshotPacket BuildSnapshot(int deltaCount)
    {
        var deltas = new EntityDelta[deltaCount];
        for (int i = 0; i < deltaCount; i++)
        {
            // 每个 delta 的所有字段均不同，最大化熵、最小化压缩比
            deltas[i] = new EntityDelta
            {
                EntityId = (ulong)(i * 31 + 7),
                Kind = EntityDeltaKind.Update,
                Identity = new NetworkIdentityAuthComponent
                {
                    NetworkId = (ulong)(i * 13 + 1),
                    EntityType = (byte)(i % 200),
                    OwnerId = (ulong)(i * 17),
                },
                Transform = new AuthTransformComponent
                {
                    X = i * 1.1f + 0.1f,
                    Y = i * 2.2f + 0.2f,
                    Z = i * 3.3f + 0.3f,
                    Pitch = i * 0.01f,
                    Yaw = i * 0.02f,
                    Roll = i * 0.03f,
                    ServerTick = (long)(i * 100 + 1),
                },
            };
        }

        return new SnapshotPacket
        {
            ServerTick = 12345,
            BaselineTick = 0,
            Deltas = deltas,
        };
    }

    /// <summary>
    /// 大 SnapshotPacket 经 Encode/Decode 往返后数据保持一致。
    /// 覆盖 LZ4 压缩路径（Snapshot 且 ≥ 256B 触发压缩）。
    /// </summary>
    [Fact]
    public void Lz4Roundtrip_LargeSnapshot_PreservesData()
    {
        var original = BuildSnapshot(200);

        SyncPacketCodec.Encode(original, out var frame, out var frameLength);
        try
        {
            // 确认走了 LZ4 压缩路径
            Assert.Equal((byte)SyncPacketCodec.CompressionKind.Lz4, frame[1]);

            var decoded = SyncPacketCodec.Decode(new ReadOnlySpan<byte>(frame, 0, frameLength));
            var snapshot = Assert.IsType<SnapshotPacket>(decoded);

            Assert.Equal(original.ServerTick, snapshot.ServerTick);
            Assert.Equal(original.BaselineTick, snapshot.BaselineTick);
            Assert.Equal(original.Deltas.Length, snapshot.Deltas.Length);
            Assert.Equal(original.Deltas[0].Transform!.Value.X, snapshot.Deltas[0].Transform!.Value.X);
            Assert.Equal(original.Deltas[0].Identity!.Value.NetworkId, snapshot.Deltas[0].Identity!.Value.NetworkId);
        }
        finally
        {
            SyncPacketCodec.ReturnFrame(frame);
        }
    }

    /// <summary>
    /// 通过传入极小的 <c>originalPayloadLengthHint</c> 强制触发 Decode LZ4 重试路径。
    /// <para>
    /// 首次 hint = 8 远小于实际解压大小，LZ4Codec.Decode 返回负数，进入重试分支；
    /// 重试 biggerSize = max(8*4, payloadLength*16) = payloadLength*16 对中等压缩比数据足够。
    /// BUG 1 未修复时重试路径双重归还 ArrayPool，可能抛异常或返回损坏数据；
    /// 修复后应正确返回原始 SnapshotPacket。
    /// </para>
    /// </summary>
    [Fact]
    public void Lz4RetryPath_ForcedBySmallHint_DecodesCorrectly()
    {
        var original = BuildSnapshot(60);

        SyncPacketCodec.Encode(original, out var frame, out var frameLength);
        try
        {
            Assert.Equal((byte)SyncPacketCodec.CompressionKind.Lz4, frame[1]);

            // 传 originalPayloadLengthHint=8 强制首次 hint 不足，触发重试路径
            var decoded = SyncPacketCodec.Decode(new ReadOnlySpan<byte>(frame, 0, frameLength), originalPayloadLengthHint: 8);
            var snapshot = Assert.IsType<SnapshotPacket>(decoded);

            Assert.Equal(original.ServerTick, snapshot.ServerTick);
            Assert.Equal(original.BaselineTick, snapshot.BaselineTick);
            Assert.Equal(original.Deltas.Length, snapshot.Deltas.Length);
            Assert.Equal(original.Deltas[0].Transform!.Value.X, snapshot.Deltas[0].Transform!.Value.X);
            Assert.Equal(original.Deltas[10].EntityId, snapshot.Deltas[10].EntityId);
        }
        finally
        {
            SyncPacketCodec.ReturnFrame(frame);
        }
    }

    /// <summary>
    /// LZ4 重试路径执行后 ArrayPool 仍可正常借还。
    /// 间接验证 BUG 1 已修复：双重归还会破坏 freelist，导致后续 Rent 返回同一缓冲区。
    /// </summary>
    [Fact]
    public void Lz4RetryPath_ArrayPoolRemainsUsableAfterDecode()
    {
        var original = BuildSnapshot(60);
        SyncPacketCodec.Encode(original, out var frame, out var frameLength);
        try
        {
            // 强制触发重试路径
            SyncPacketCodec.Decode(new ReadOnlySpan<byte>(frame, 0, frameLength), originalPayloadLengthHint: 8);
        }
        finally
        {
            SyncPacketCodec.ReturnFrame(frame);
        }

        // 验证 ArrayPool 借还正常：借两个缓冲区写入不同模式，确认互不干扰
        var a = ArrayPool<byte>.Shared.Rent(1024);
        var b = ArrayPool<byte>.Shared.Rent(1024);
        try
        {
            for (int i = 0; i < 1024; i++)
            {
                a[i] = 0xAA;
                b[i] = 0xBB;
            }
            // 若双重归还导致 a、b 指向同一缓冲区，a[0] 会被 b 覆盖为 0xBB
            Assert.Equal(0xAA, a[0]);
            Assert.Equal(0xBB, b[0]);
            Assert.Equal(0xAA, a[512]);
            Assert.Equal(0xBB, b[512]);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(a);
            ArrayPool<byte>.Shared.Return(b);
        }
    }

    /// <summary>
    /// 多次连续触发重试路径的稳定性测试。
    /// BUG 1 双重归还会在多次调用后累积损坏 freelist，本测试连续 50 次强制重试验证稳定性。
    /// </summary>
    [Fact]
    public void Lz4RetryPath_MultipleInvocations_StableNoCorruption()
    {
        var original = BuildSnapshot(40);

        for (int iter = 0; iter < 50; iter++)
        {
            SyncPacketCodec.Encode(original, out var frame, out var frameLength);
            try
            {
                // 每次都强制触发重试路径
                var decoded = SyncPacketCodec.Decode(new ReadOnlySpan<byte>(frame, 0, frameLength), originalPayloadLengthHint: 8);
                var snapshot = Assert.IsType<SnapshotPacket>(decoded);
                Assert.Equal(original.ServerTick, snapshot.ServerTick);
                Assert.Equal(original.Deltas.Length, snapshot.Deltas.Length);
            }
            finally
            {
                SyncPacketCodec.ReturnFrame(frame);
            }
        }

    }
}

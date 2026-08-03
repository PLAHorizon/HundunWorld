using System;
using System.Buffers;
using K4os.Compression.LZ4;
using MemoryPack;

namespace Horizon.Game.Message.Sync;

/// <summary>
/// <see cref="SyncPacket"/> 的统一编解码器：
///   * 普通包：直接 MemoryPack 序列化。
///   * <see cref="SnapshotPacket"/>：当原始字节数 ≥ <see cref="SnapshotCompressionThreshold"/> 时启用 LZ4 压缩；
///     输入包始终不压缩以保延迟。
/// 帧格式（与 TouchSocket FixedHeaderPackageAdapter 对齐）：
///   <code>
///     [0..1]  byte  Kind          // 与 SyncPacketKind 一致，便于 fast-path 路由
///     [1..2]  byte  Compression   // 0 = none, 1 = lz4
///     [2..6]  i32   PayloadLength // 后续 payload 字节数
///     [6..]   bytes Payload       // MemoryPack 序列化（或压缩后）
///   </code>
/// </summary>
public static class SyncPacketCodec
{
    /// <summary>超过该字节数的 snapshot 启用 LZ4 压缩。</summary>
    public const int SnapshotCompressionThreshold = 256;

    public const int FrameHeaderSize = 6;

    public const int MaxDecompressedSize = 4 * 1024 * 1024;

    /// <summary>压缩标记。</summary>
    public enum CompressionKind : byte
    {
        None = 0,
        Lz4 = 1,
    }

    /// <summary>
    /// 序列化 <paramref name="packet"/> 到 <see cref="ArrayPool{Byte}.Shared"/> 借出的缓冲区。
    /// 调用方负责把 <paramref name="frame"/> 归还回池。
    /// </summary>
    public static void Encode(SyncPacket packet, out byte[] frame, out int frameLength)
    {
        ArgumentNullException.ThrowIfNull(packet);

        // 1. 序列化 payload。
        var rawPayload = MemoryPackSerializer.Serialize<SyncPacket>(packet);

        // 2. 决定是否压缩。
        var shouldCompress = packet.Kind == SyncPacketKind.Snapshot
                             && rawPayload.Length >= SnapshotCompressionThreshold;

        byte[] payload = rawPayload;
        var compression = CompressionKind.None;
        if (shouldCompress)
        {
            var maxOut = LZ4Codec.MaximumOutputSize(rawPayload.Length);
            var compressed = ArrayPool<byte>.Shared.Rent(maxOut);
            try
            {
                var written = LZ4Codec.Encode(rawPayload, 0, rawPayload.Length, compressed, 0, compressed.Length);
                if (written > 0 && written < rawPayload.Length)
                {
                    // 只有真正变小时才采用压缩结果。
                    payload = new byte[written];
                    Array.Copy(compressed, payload, written);
                    compression = CompressionKind.Lz4;
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(compressed);
            }
        }

        frameLength = FrameHeaderSize + payload.Length;
        frame = ArrayPool<byte>.Shared.Rent(frameLength);
        frame[0] = (byte)packet.Kind;
        frame[1] = (byte)compression;
        frame[2] = (byte)(payload.Length & 0xFF);
        frame[3] = (byte)((payload.Length >> 8) & 0xFF);
        frame[4] = (byte)((payload.Length >> 16) & 0xFF);
        frame[5] = (byte)((payload.Length >> 24) & 0xFF);
        Buffer.BlockCopy(payload, 0, frame, FrameHeaderSize, payload.Length);
    }

    /// <summary>
    /// 反序列化一帧字节为 <see cref="SyncPacket"/>。
    /// </summary>
    /// <param name="frame">完整帧（含 6 字节帧头 + payload）。</param>
    /// <param name="originalPayloadLengthHint">可选：解压后预期长度。&lt;= 0 时自动按 4×payload 估算。</param>
    public static SyncPacket Decode(ReadOnlySpan<byte> frame, int originalPayloadLengthHint = 0)
    {
        if (frame.Length < FrameHeaderSize)
        {
            throw new ArgumentException("Frame is too short to contain a sync header.", nameof(frame));
        }

        var compression = (CompressionKind)frame[1];
        int payloadLength = frame[2]
                            | (frame[3] << 8)
                            | (frame[4] << 16)
                            | (frame[5] << 24);

        if (payloadLength < 0 || FrameHeaderSize + payloadLength > frame.Length)
        {
            throw new ArgumentException("Invalid sync frame: payload length out of bounds.", nameof(frame));
        }

        var payload = frame.Slice(FrameHeaderSize, payloadLength);

        switch (compression)
        {
            case CompressionKind.None:
                return MemoryPackSerializer.Deserialize<SyncPacket>(payload)
                       ?? throw new InvalidOperationException("MemoryPack returned null SyncPacket.");

            case CompressionKind.Lz4:
                {
                    var hint = originalPayloadLengthHint > 0
                        ? originalPayloadLengthHint
                        : Math.Max(payloadLength * 4, payloadLength + 64);
                    if (hint > MaxDecompressedSize)
                        hint = MaxDecompressedSize;
                    var rented = ArrayPool<byte>.Shared.Rent(hint);
                    try
                    {
                        int decoded = LZ4Codec.Decode(payload, rented.AsSpan());
                        if (decoded >= 0)
                        {
                            if (decoded > MaxDecompressedSize)
                                throw new InvalidOperationException($"LZ4 decompressed size ({decoded}) exceeds limit ({MaxDecompressedSize} bytes).");
                            return MemoryPackSerializer.Deserialize<SyncPacket>(rented.AsSpan(0, decoded))
                                   ?? throw new InvalidOperationException("MemoryPack returned null SyncPacket.");
                        }

                        // 首次 hint 过小，用更大尺寸重试。
                        // 修复 #6：重试时复用 ArrayPool 避免 new byte[] GC 压力。
                        // 修复 BUG（双重归还）：此处不再显式 Return(rented)，
                        // 外层 finally 会统一归还 rented，避免同一缓冲区被归还两次
                        // 导致 ArrayPool 内部 freelist 损坏、同一数组被多次借出引发数据竞争。
                        var biggerSize = Math.Max(hint * 4, payloadLength * 16);
                        if (biggerSize > MaxDecompressedSize)
                            throw new InvalidOperationException($"LZ4 decompressed size exceeds limit ({MaxDecompressedSize} bytes). Possible decompression bomb.");
                        var bigger = ArrayPool<byte>.Shared.Rent(biggerSize);
                        try
                        {
                            decoded = LZ4Codec.Decode(payload, bigger.AsSpan());
                            if (decoded < 0)
                                throw new InvalidOperationException("LZ4 decode failed for sync frame.");
                            if (decoded > MaxDecompressedSize)
                                throw new InvalidOperationException($"LZ4 decompressed size ({decoded}) exceeds limit ({MaxDecompressedSize} bytes).");
                            return MemoryPackSerializer.Deserialize<SyncPacket>(bigger.AsSpan(0, decoded))
                                   ?? throw new InvalidOperationException("MemoryPack returned null SyncPacket.");
                        }
                        finally
                        {
                            ArrayPool<byte>.Shared.Return(bigger);
                        }
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(rented);
                    }
                }

            default:
                throw new NotSupportedException($"Unsupported sync compression: {compression}");
        }
    }

    /// <summary>把通过 <see cref="Encode"/> 借出的帧归还到 <see cref="ArrayPool{Byte}.Shared"/>。</summary>
    public static void ReturnFrame(byte[] frame)
    {
        if (frame is { Length: > 0 })
        {
            ArrayPool<byte>.Shared.Return(frame);
        }
    }
}

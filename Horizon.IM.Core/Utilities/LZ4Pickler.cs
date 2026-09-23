using K4os.Compression.LZ4;

namespace Horizon.IM.Core.Utilities;

public static class LZ4Pickler
{
    /// <summary>
    /// 解压输出长度上限（默认 8MB）。长度前缀来自远端数据，必须在分配输出缓冲区
    /// 之前拒绝超限值，否则单个 ≤1MB 压缩帧即可触发最高 ~2GB 分配（解压炸弹）。
    /// 与 SyncPacketCodec.Decode 的上限防护对齐。
    /// </summary>
    public const int MaxDecompressedLength = 8 * 1024 * 1024;

    public static byte[] Pickle(byte[] input)
    {
        if (input == null || input.Length == 0)
        {
            return Array.Empty<byte>();
        }

        try
        {
            var maxLength = LZ4Codec.MaximumOutputSize(input.Length);
            var output = new byte[maxLength + 4];
            BitConverter.GetBytes(input.Length).CopyTo(output, 0);

            var compressedLength = LZ4Codec.Encode(
                input,
                0,
                input.Length,
                output,
                4,
                output.Length - 4);

            if (compressedLength >= input.Length)
            {
                var rawResult = new byte[input.Length + 4];
                BitConverter.GetBytes(input.Length).CopyTo(rawResult, 0);
                input.CopyTo(rawResult, 4);
                return rawResult;
            }

            var final = new byte[compressedLength + 4];
            Array.Copy(output, 0, final, 0, final.Length);
            return final;
        }
        catch
        {
            // 修复（BUG，与 Horizon.Game.Message.Utilities.LZ4Pickler 对齐）：
            // 原实现直接返回无长度前缀的 input，而 Unpickle 恒按
            // 「[4 字节 int32 原长][数据]」解析，会把原始数据前 4 字节误读成长度，
            // 导致 Pickle→Unpickle 无法往返（解压失败 → 整包被丢弃）。
            // 与「压缩无益」分支保持一致，补上长度前缀。
            var fallback = new byte[input.Length + 4];
            BitConverter.GetBytes(input.Length).CopyTo(fallback, 0);
            input.CopyTo(fallback, 4);
            return fallback;
        }
    }

    public static byte[]? Unpickle(ReadOnlySpan<byte> input, int maxDecompressedLength = MaxDecompressedLength)
    {
        if (input.Length < 4)
        {
            return null;
        }

        try
        {
            var raw = input.ToArray();
            var originalLength = BitConverter.ToInt32(raw, 0);
            // 解压炸弹防护：长度前缀完全由对端控制，超限直接拒绝（先于任何分配）。
            if (originalLength <= 0 || originalLength > maxDecompressedLength)
            {
                return null;
            }

            var output = new byte[originalLength];
            var decompressedLength = LZ4Codec.Decode(raw, 4, raw.Length - 4, output, 0, output.Length);

            if (decompressedLength != originalLength)
            {
                if (raw.Length - 4 == originalLength)
                {
                    Array.Copy(raw, 4, output, 0, originalLength);
                    return output;
                }

                return null;
            }

            return output;
        }
        catch
        {
            return null;
        }
    }
}
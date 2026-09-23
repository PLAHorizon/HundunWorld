using K4os.Compression.LZ4;
using System;

namespace Horizon.Game.Core.Utilities
{
    /// <summary>
    /// LZ4压缩和解压缩工具类
    /// </summary>
    public static class LZ4Pickler
    {
        /// <summary>
        /// 解压输出长度上限（默认 8MB）。长度前缀来自远端数据，必须在分配输出缓冲区
        /// 之前拒绝超限值，否则单个 ≤1MB 压缩帧即可触发最高 ~2GB 分配（解压炸弹）。
        /// 与 SyncPacketCodec.Decode 的上限防护对齐。
        /// </summary>
        public const int MaxDecompressedLength = 8 * 1024 * 1024;

        /// <summary>
        /// 使用LZ4压缩数据
        /// </summary>
        public static byte[] Pickle(byte[] input)
        {
            if (input == null || input.Length == 0)
                return Array.Empty<byte>();

            try
            {
                int maxLength = LZ4Codec.MaximumOutputSize(input.Length);
                byte[] output = new byte[maxLength + 4]; // 前4字节存储原始长度

                // 存储原始长度到前4个字节
                BitConverter.GetBytes(input.Length).CopyTo(output, 0);

                // 压缩数据
                int compressedLength = LZ4Codec.Encode(
                    input, 0, input.Length,
                    output, 4, output.Length - 4);

                // 如果压缩后更大，则返回原始数据
                if (compressedLength >= input.Length)
                {
                    byte[] result = new byte[input.Length + 4];
                    BitConverter.GetBytes(input.Length).CopyTo(result, 0);
                    input.CopyTo(result, 4);
                    return result;
                }

                // 调整数组大小为实际压缩后的长度
                byte[] final = new byte[compressedLength + 4];
                Array.Copy(output, 0, final, 0, compressedLength + 4);
                return final;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceError($"LZ4压缩失败: {ex.Message}");

                // 修复（BUG，与 Horizon.Game.Message.Utilities.LZ4Pickler 对齐）：
                // 原实现直接 `return input`，返回的是**无 4 字节长度前缀**的原始数据；
                // 而 Unpickle 恒按「[4 字节 int32 原长][数据]」解析，会把原始数据的前 4 个字节
                // 误读成长度，导致 Pickle→Unpickle 无法往返（解压失败 → 整包被丢弃）。
                // 此处与「压缩无益」分支保持一致，补上长度前缀，保证往返恒成立。
                // 影响面：网关**发送**方向走的就是本实现（HorizonMessageAdapter 显式别名到
                // Horizon.Game.Core.Utilities.LZ4Pickler），因此不通修会导致下行包在客户端解压失败。
                var fallback = new byte[input.Length + 4];
                BitConverter.GetBytes(input.Length).CopyTo(fallback, 0);
                input.CopyTo(fallback, 4);
                return fallback;
            }
        }

        /// <summary>
        /// 使用LZ4解压缩数据
        /// </summary>
        public static byte[]? Unpickle(ReadOnlySpan<byte> input, int maxDecompressedLength = MaxDecompressedLength)
        {
            if (input.Length < 4)
                return null;

            try
            {
                var raw = input.ToArray();
                // 从前4个字节读取原始长度
                int originalLength = BitConverter.ToInt32(raw, 0);
                // 解压炸弹防护：长度前缀完全由对端控制，超限直接拒绝（先于输出缓冲区分配）。
                if (originalLength <= 0 || originalLength > maxDecompressedLength)
                    return null;

                // 创建输出缓冲区
                byte[] output = new byte[originalLength];

                // 解压缩数据
                int decompressedLength = LZ4Codec.Decode(
                    raw, 4, raw.Length - 4,
                    output, 0, output.Length);

                // 验证解压缩后的长度是否与原始长度一致
                if (decompressedLength != originalLength)
                {
                    // 如果不一致，可能是未压缩的数据，直接复制
                    if (raw.Length - 4 == originalLength)
                    {
                        Array.Copy(raw, 4, output, 0, originalLength);
                        return output;
                    }
                    return null;
                }

                return output;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceError($"LZ4解压缩失败: {ex.Message}");
                return null;
            }
        }
    }
}

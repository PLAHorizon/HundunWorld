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
                Console.WriteLine($"[ERROR] LZ4压缩失败: {ex.Message}");
                return input;
            }
        }

        /// <summary>
        /// 使用LZ4解压缩数据
        /// </summary>
        public static byte[]? Unpickle(byte[] input)
        {
            if (input == null || input.Length < 4)
                return null;

            try
            {
                // 从前4个字节读取原始长度
                int originalLength = BitConverter.ToInt32(input, 0);
                if (originalLength <= 0)
                    return null;

                // 创建输出缓冲区
                byte[] output = new byte[originalLength];

                // 解压缩数据
                int decompressedLength = LZ4Codec.Decode(
                    input, 4, input.Length - 4,
                    output, 0, output.Length);

                // 验证解压缩后的长度是否与原始长度一致
                if (decompressedLength != originalLength)
                {
                    // 如果不一致，可能是未压缩的数据，直接复制
                    if (input.Length - 4 == originalLength)
                    {
                        Array.Copy(input, 4, output, 0, originalLength);
                        return output;
                    }
                    return null;
                }

                return output;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] LZ4解压缩失败: {ex.Message}");
                return null;
            }
        }
    }
}

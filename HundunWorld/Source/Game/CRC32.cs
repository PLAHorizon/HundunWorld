using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics.Arm;
using System.Text;
using System.Threading.Tasks;

namespace HundunWorld.Game
{
    public static class CRC32
    {
        // IEEE 802.3标准多项式：0xEDB88320
        private static readonly uint[] Table = GenerateTable(0xEDB88320u);

        // 预生成CRC表（256项）
        private static uint[] GenerateTable(uint polynomial)
        {
            var table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                var entry = i;
                for (var j = 0; j < 8; j++)
                {
                    if ((entry & 1) == 1)
                        entry = (entry >> 1) ^ polynomial;
                    else
                        entry >>= 1;
                }
                table[i] = entry;
            }
            return table;
        }

        // 高性能计算CRC32
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint Compute(ReadOnlySpan<byte> data)
        {
            uint crc = 0xFFFFFFFFu;
            foreach (byte b in data)
            {
                crc = (crc >> 8) ^ Table[(crc ^ b) & 0xFF];
            }
            return crc ^ 0xFFFFFFFFu;
        }

        // 支持分段计算的版本
        public static uint Compute(ref uint crc, ReadOnlySpan<byte> data)
        {
            uint localCrc = crc;
            foreach (byte b in data)
            {
                localCrc = (localCrc >> 8) ^ Table[(localCrc ^ b) & 0xFF];
            }
            crc = localCrc;
            return crc ^ 0xFFFFFFFFu;
        }

        // 内存安全的高性能版本（使用指针）
        private static unsafe uint ComputeUnsafe(byte* data, int length)
        {
            uint crc = 0xFFFFFFFFu;
            for (int i = 0; i < length; i++)
            {
                crc = (crc >> 8) ^ Table[(crc ^ data[i]) & 0xFF];
            }
            return crc ^ 0xFFFFFFFFu;
        }

        /// <summary>
        /// 分段计算
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public static unsafe uint RollingCrcCompute(byte[] data)
        {
            // 分段计算
            uint rollingCrc = 0xFFFFFFFFu;
            Compute(ref rollingCrc, data.AsSpan(0, 5));
            Compute(ref rollingCrc, data.AsSpan(5));
            uint crc2 = rollingCrc ^ 0xFFFFFFFFu;
            Console.WriteLine($"分段CRC32: 0x{crc2:X8}");
            return crc2;
        }
        /// <summary>
        /// 计算CRC32
        /// </summary>
        /// <param name="data"></param>
        /// <param name="length"></param>
        /// <returns></returns>
        public static unsafe uint ComputeUnsafe(ReadOnlySpan<byte> data, int length)
        {
            // 不安全版本
            unsafe
            {
                fixed (byte* ptr = data)
                {
                    uint crc3 = ComputeUnsafe(ptr, length);
                    return crc3;
                }
            }
        }
    }
}

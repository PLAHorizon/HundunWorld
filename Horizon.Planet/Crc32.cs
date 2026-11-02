using System;
using System.IO;

namespace Horizon.Planet;

/// <summary>IEEE CRC32（反射多项式 0xEDB88320），与 zlib.crc32 同口径，用于 DEM 产物双端对账。</summary>
public static class Crc32
{
    private static readonly uint[] Table = BuildTable();

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[i] = c;
        }
        return table;
    }

    public static uint Compute(ReadOnlySpan<byte> data, uint crc = 0)
    {
        uint c = ~crc;
        for (int i = 0; i < data.Length; i++)
            c = Table[(c ^ data[i]) & 0xFF] ^ (c >> 8);
        return ~c;
    }

    public static uint HashFile(string path, int bufferSize = 1 << 22)
    {
        uint crc = 0;
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize);
        var buffer = new byte[bufferSize];
        int read;
        while ((read = fs.Read(buffer, 0, buffer.Length)) > 0)
            crc = Compute(buffer.AsSpan(0, read), crc);
        return crc;
    }
}

using System;
using System.IO;
using System.Text;
using Horizon.Planet;
using Xunit;

namespace Horizon.Planet.Tests;

public class Crc32Tests
{
    /// <summary>行业标准的 check value：ASCII "123456789" 的 CRC-32/ISO-HDLC = 0xCBF43926。</summary>
    [Fact]
    public void Matches_StandardCheckValue()
    {
        byte[] data = Encoding.ASCII.GetBytes("123456789");
        Assert.Equal(0xCBF43926u, Crc32.Compute(data));
    }

    [Fact]
    public void EmptyInput_IsZero() => Assert.Equal(0u, Crc32.Compute(ReadOnlySpan<byte>.Empty));

    [Fact]
    public void ChunkedFeeding_EqualsWholeBuffer()
    {
        var rng = new Random(4242);
        var data = new byte[100_000];
        rng.NextBytes(data);

        uint whole = Crc32.Compute(data);
        uint streamed = 0;
        for (int offset = 0; offset < data.Length; offset += 4096)
            streamed = Crc32.Compute(data.AsSpan(offset, Math.Min(4096, data.Length - offset)), streamed);
        Assert.Equal(whole, streamed);
    }

    [Fact]
    public void HashFile_MatchesInMemoryCompute()
    {
        var data = new byte[200_000];
        new Random(7).NextBytes(data);
        string path = Path.Combine(Path.GetTempPath(), "crc32_test_" + Guid.NewGuid().ToString("N") + ".bin");
        try
        {
            File.WriteAllBytes(path, data);
            Assert.Equal(Crc32.Compute(data), Crc32.HashFile(path, bufferSize: 8192));
        }
        finally
        {
            File.Delete(path);
        }
    }
}

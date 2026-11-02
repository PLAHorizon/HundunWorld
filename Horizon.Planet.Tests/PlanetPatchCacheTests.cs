using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Horizon.Planet.Tests;

/// <summary>每个用例一个独立临时目录，用例结束整体删掉。</summary>
internal sealed class TempCacheDir : IDisposable
{
    public string Root { get; } =
        Path.Combine(Path.GetTempPath(), "hundun_patchcache_" + Guid.NewGuid().ToString("N"));

    public TempCacheDir() => Directory.CreateDirectory(Root);

    public void Dispose()
    {
        // 同 TempDem：最后一片高度文件的句柄关掉之后目录还会被占一会儿，静默吞掉就是攒一堆看不见的残留
        for (int attempt = 0; attempt < 8; attempt++)
        {
            try { Directory.Delete(Root, true); return; }
            catch (IOException) { Thread.Sleep(50); }
            catch (UnauthorizedAccessException) { Thread.Sleep(50); }
        }
        try { Directory.Delete(Root, true); }
        catch (Exception e) { Console.Error.WriteLine($"临时缓存目录没清掉：{Root}（{e.GetType().Name}）"); }
    }
}

public class PlanetPatchCacheTests
{
    private const long Seed = 0x0123456789ABCDEFL;
    private const uint TerrainVersion = 0xABCD1234;
    private const long RegionA = 1234567890L;
    private const long RegionB = 9876543210L;

    /// <summary>
    /// 缓存不解释高度，只保证"字节进=字节出"，所以这里用确定的伪随机内容即可；
    /// 与真实生成器逐位一致的那条判据在 PlanetTerrainStreamerTests 里。
    /// </summary>
    private static float[] MakeHeights(long regionId, int patchX, int patchZ)
    {
        var h = new float[PlanetPatchSampler.HeightmapLength];
        uint s = unchecked((uint)(regionId ^ (patchX * 2654435761L) ^ (patchZ * 40503L)) | 1u);
        for (int i = 0; i < h.Length; i++)
        {
            s = s * 1664525u + 1013904223u;
            h[i] = ((s >> 8) & 0xFFFFFF) / 4096f - 2000f;
        }
        return h;
    }

    private static void AssertBitsEqual(float[] expected, float[] actual, string label)
    {
        Assert.Equal(expected.Length, actual.Length);
        var a = MemoryMarshal.AsBytes(expected.AsSpan());
        var b = MemoryMarshal.AsBytes(actual.AsSpan());
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i])
                Assert.Fail($"{label}：第 {i} 字节 期望 {a[i]:X2} 实际 {b[i]:X2}");
        }
    }

    private static uint CrcOf(float[] heights) => Crc32.Compute(MemoryMarshal.AsBytes(heights.AsSpan()));

    private static PlanetPatchCache NewCache(TempCacheDir dir, long seed = Seed, uint version = TerrainVersion) =>
        new(dir.Root, seed, version);

    /// <summary>把已写好的缓存文件的 header 换成另一份（载荷不动），用来逐字段制造坏 header。</summary>
    private static void RewriteHeader(string path, in PatchCacheRecord record)
    {
        var all = File.ReadAllBytes(path);
        PlanetPatchCache.BuildHeader(all.AsSpan(0, PlanetPatchCache.HeaderSize), record);
        File.WriteAllBytes(path, all);
    }

    [Fact]
    public void Write_ThenRead_RoundTripsBitExact()
    {
        using var dir = new TempCacheDir();
        var cache = NewCache(dir);
        var heights = MakeHeights(RegionA, 3, -2);

        cache.Write(RegionA, 3, -2, heights);
        Assert.Equal(1, cache.Writes);
        string path = cache.PathFor(RegionA, 3, -2);
        Assert.True(File.Exists(path), "写盘后文件必须在");
        Assert.Equal(PlanetPatchCache.EntryBytes, new FileInfo(path).Length);

        var read = new float[PlanetPatchSampler.HeightmapLength];
        Assert.True(cache.TryRead(RegionA, 3, -2, read, out var reason));
        Assert.Equal(PatchCacheReject.None, reason);
        AssertBitsEqual(heights, read, "round-trip");
        Assert.Equal(1, cache.Hits);
        Assert.Equal(0, cache.Misses);
        Assert.Equal(PlanetPatchCache.EntryBytes, cache.BytesUsed);
    }

    [Fact]
    public void RoundTrip_PreservesSpecialFloatBitPatterns()
    {
        using var dir = new TempCacheDir();
        var cache = NewCache(dir);
        var heights = MakeHeights(RegionA, 0, 0);
        heights[0] = float.NaN;
        heights[1] = float.PositiveInfinity;
        heights[2] = float.NegativeInfinity;
        heights[3] = -0.0f;                       // 与 +0.0 数值相等但位型不同
        heights[4] = float.MinValue;
        heights[5] = float.MaxValue;
        heights[6] = 1.40129846E-45f;             // 最小正规数
        heights[60000] = float.NaN;

        cache.Write(RegionA, 0, 0, heights);

        // 读回缓冲区先灌满 NaN：任何没被覆盖的位置都会在这条判据下暴露
        var read = new float[PlanetPatchSampler.HeightmapLength];
        Array.Fill(read, float.NaN);
        Assert.True(cache.TryRead(RegionA, 0, 0, read, out _));
        AssertBitsEqual(heights, read, "特殊位型");
        Assert.False(float.IsPositiveInfinity(read[0]), "NaN 被读成了别的值");
    }

    [Fact]
    public void WrongLength_StillMissingFile_AndFreshReadCountsMiss()
    {
        using var dir = new TempCacheDir();
        var cache = NewCache(dir);
        var read = new float[PlanetPatchSampler.HeightmapLength];

        Assert.False(cache.TryRead(RegionA, 7, 7, read, out var reason));
        Assert.Equal(PatchCacheReject.MissingFile, reason);
        Assert.Equal(1, cache.Misses);
        Assert.Equal(1, cache.RejectCount(PatchCacheReject.MissingFile));
        // 未命中不是"坏"：损坏计数必须留在 0，否则日志里两种情况会混成一个数
        Assert.Equal(0, cache.CorruptRejects);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(1L)]
    [InlineData(63L)]                 // header 都不完整
    [InlineData(64L)]                 // 只有 header
    [InlineData(65L)]                 // 多一个字节
    public void EveryTruncatedLength_IsRejectedAsBadLength_AndDeleted(long keepBytes)
    {
        using var dir = new TempCacheDir();
        var cache = NewCache(dir);
        var heights = MakeHeights(RegionA, 1, 2);
        cache.Write(RegionA, 1, 2, heights);
        string path = cache.PathFor(RegionA, 1, 2);
        var good = File.ReadAllBytes(path);
        Assert.Equal(PlanetPatchCache.EntryBytes, good.Length);

        int patchZ = 100 + (int)keepBytes;
        cache.Write(RegionA, 1, patchZ, heights);
        string trunc = cache.PathFor(RegionA, 1, patchZ);
        var slice = new byte[keepBytes];
        Array.Copy(good, slice, keepBytes);
        File.WriteAllBytes(trunc, slice);

        var read = new float[PlanetPatchSampler.HeightmapLength];
        Assert.False(cache.TryRead(RegionA, 1, patchZ, read, out var reason));
        Assert.Equal(PatchCacheReject.BadLength, reason);
        Assert.False(File.Exists(trunc), "坏文件读完还留在盘上，下次照样被拒");

        // 正向对照：同一个 patch 重新写一遍就能读回原值
        cache.Write(RegionA, 1, patchZ, heights);
        Assert.True(cache.TryRead(RegionA, 1, patchZ, read, out reason));
        AssertBitsEqual(heights, read, "截断后重建");
    }

    [Fact]
    public void OversizedFile_IsRejectedAsBadLength()
    {
        using var dir = new TempCacheDir();
        var cache = NewCache(dir);
        cache.Write(RegionA, 4, 4, MakeHeights(RegionA, 4, 4));
        string path = cache.PathFor(RegionA, 4, 4);
        var all = File.ReadAllBytes(path);
        var withExtra = new byte[all.Length + 4];
        Array.Copy(all, withExtra, all.Length);
        File.WriteAllBytes(path, withExtra);

        Assert.False(cache.TryRead(RegionA, 4, 4,
                                   new float[PlanetPatchSampler.HeightmapLength], out var reason));
        Assert.Equal(PatchCacheReject.BadLength, reason);
    }

    [Fact]
    public void EachHeaderFieldMismatch_HasItsOwnReason_WithPositiveTwin()
    {
        using var dir = new TempCacheDir();
        var cache = NewCache(dir);
        const int px = 5, pz = -6;
        var heights = MakeHeights(RegionA, px, pz);
        uint crc = CrcOf(heights);
        var good = cache.RecordFor(RegionA, px, pz, crc);
        string path = cache.PathFor(RegionA, px, pz);
        var read = new float[PlanetPatchSampler.HeightmapLength];

        var expected = new List<(string Name, PatchCacheRecord Record, PatchCacheReject Reason)>
        {
            ("format", good with { FormatVersion = 9999 }, PatchCacheReject.BadFormatVersion),
            ("edge", good with { Edge = 510 }, PatchCacheReject.BadGeometry),
            ("floats", good with { FloatCount = 1000 }, PatchCacheReject.BadGeometry),
            ("seed", good with { Seed = 424242 }, PatchCacheReject.BadSeed),
            ("terrainVersion", good with { TerrainVersion = TerrainVersion ^ 0xFF }, PatchCacheReject.BadTerrainVersion),
            ("region", good with { RegionId = RegionB }, PatchCacheReject.BadRegion),
            ("patchX", good with { PatchX = px + 1 }, PatchCacheReject.BadPatchCoords),
            ("patchZ", good with { PatchZ = pz - 1 }, PatchCacheReject.BadPatchCoords),
        };

        foreach (var (name, record, wantReason) in expected)
        {
            cache.Write(RegionA, px, pz, heights);                       // 先写正的
            Assert.True(PlanetPatchCache.TryMatchHeader(
                File.ReadAllBytes(path).AsSpan(0, PlanetPatchCache.HeaderSize), good, out _),
                $"{name} 用例的正向对照：完好 header 必须能匹配自己");

            RewriteHeader(path, record);                                  // 只改这一格
            Assert.False(cache.TryRead(RegionA, px, pz, read, out var reason),
                         $"{name} 被改坏了却还读得出来");
            Assert.Equal(wantReason, reason);
            Assert.True(cache.RejectCount(wantReason) >= 1, $"{name} 的拒绝没进 {wantReason} 这一档");
            Assert.False(File.Exists(path), $"{name} 坏文件没被清掉");

            cache.Write(RegionA, px, pz, heights);                        // 同夹具正向对照
            Assert.True(cache.TryRead(RegionA, px, pz, read, out reason));
            Assert.Equal(PatchCacheReject.None, reason);
            AssertBitsEqual(heights, read, name + " 重建后");
        }

        // 八条分支各自落档，其中 edge/floats 共用 BadGeometry 所以是 7 个不同的原因
        long accounted = 0;
        foreach (PatchCacheReject reason in Enum.GetValues<PatchCacheReject>())
            accounted += cache.RejectCount(reason);
        Assert.Equal(expected.Count, accounted);
    }

    [Fact]
    public void BadMagic_And_NonZeroReserved_AreRejected()
    {
        using var dir = new TempCacheDir();
        var cache = NewCache(dir);
        var heights = MakeHeights(RegionA, 0, 9);
        cache.Write(RegionA, 0, 9, heights);
        string path = cache.PathFor(RegionA, 0, 9);
        var read = new float[PlanetPatchSampler.HeightmapLength];

        var all = File.ReadAllBytes(path);
        all[0] = (byte)'X';                                    // magic 首字节
        File.WriteAllBytes(path, all);
        Assert.False(cache.TryRead(RegionA, 0, 9, read, out var reason));
        Assert.Equal(PatchCacheReject.BadMagic, reason);

        cache.Write(RegionA, 0, 9, heights);
        all = File.ReadAllBytes(path);
        all[PlanetPatchCache.HeaderSize - 1] = 0x7F;           // 保留区最后一个字节
        File.WriteAllBytes(path, all);
        Assert.False(cache.TryRead(RegionA, 0, 9, read, out reason));
        Assert.Equal(PatchCacheReject.BadReserved, reason);

        // 正向对照：完好文件两种检查都放行
        cache.Write(RegionA, 0, 9, heights);
        Assert.True(cache.TryRead(RegionA, 0, 9, read, out reason));
        AssertBitsEqual(heights, read, "保留区完好");
    }

    [Fact]
    public void SinglePayloadBitFlip_IsCaughtByCrc()
    {
        using var dir = new TempCacheDir();
        var cache = NewCache(dir);
        var heights = MakeHeights(RegionA, 11, 12);
        cache.Write(RegionA, 11, 12, heights);
        string path = cache.PathFor(RegionA, 11, 12);

        var all = File.ReadAllBytes(path);
        all[PlanetPatchCache.HeaderSize + 12345] ^= 0x01;      // 载荷里翻一个比特
        File.WriteAllBytes(path, all);

        var read = new float[PlanetPatchSampler.HeightmapLength];
        Assert.False(cache.TryRead(RegionA, 11, 12, read, out var reason));
        Assert.Equal(PatchCacheReject.BadCrc, reason);
        Assert.Equal(1, cache.RejectCount(PatchCacheReject.BadCrc));
        Assert.Equal(1, cache.CorruptRejects);
        Assert.False(File.Exists(path));

        // 正向对照：翻回去就认——说明拦下它的是校验和，不是别的东西
        all[PlanetPatchCache.HeaderSize + 12345] ^= 0x01;
        File.WriteAllBytes(path, all);
        Assert.True(cache.TryRead(RegionA, 11, 12, read, out reason));
        AssertBitsEqual(heights, read, "比特翻回去");
    }

    [Fact]
    public void CrossRegion_SamePatchCoords_DoNotCollide()
    {
        using var dir = new TempCacheDir();
        var cache = NewCache(dir);
        var heightsA = MakeHeights(RegionA, 2, 2);
        var heightsB = MakeHeights(RegionB, 2, 2);

        cache.Write(RegionA, 2, 2, heightsA);
        cache.Write(RegionB, 2, 2, heightsB);

        Assert.NotEqual(cache.PathFor(RegionA, 2, 2), cache.PathFor(RegionB, 2, 2));
        Assert.True(File.Exists(cache.PathFor(RegionA, 2, 2)));
        Assert.True(File.Exists(cache.PathFor(RegionB, 2, 2)));
        Assert.Equal(2, cache.EntryCount);
        Assert.Equal(2L * PlanetPatchCache.EntryBytes, cache.BytesUsed);

        var read = new float[PlanetPatchSampler.HeightmapLength];
        Assert.True(cache.TryRead(RegionA, 2, 2, read, out _));
        AssertBitsEqual(heightsA, read, "region A");
        Assert.True(cache.TryRead(RegionB, 2, 2, read, out _));
        AssertBitsEqual(heightsB, read, "region B");

        // 反证：拿 A 的 region 去读 B 那份文件的位置读不到东西（不是读到错的高度）
        Assert.False(cache.TryRead(RegionA, 2, 3, read, out var reason));
        Assert.Equal(PatchCacheReject.MissingFile, reason);
    }

    [Fact]
    public void SeedAndVersion_ChangeTheDirectorySoOldFilesAreNotRead()
    {
        using var dir = new TempCacheDir();
        var original = NewCache(dir);
        var heights = MakeHeights(RegionA, 1, 1);
        original.Write(RegionA, 1, 1, heights);

        var read = new float[PlanetPatchSampler.HeightmapLength];

        var otherSeed = NewCache(dir, seed: Seed + 1);
        Assert.NotEqual(original.RootDirectory, otherSeed.RootDirectory);
        Assert.False(otherSeed.TryRead(RegionA, 1, 1, read, out var reason));
        Assert.Equal(PatchCacheReject.MissingFile, reason);
        Assert.Equal(0, otherSeed.CorruptRejects);   // 旧版本的缓存还在盘上，但不能被当成"当前版本的有效数据"

        var otherVersion = NewCache(dir, version: TerrainVersion + 1);
        Assert.False(otherVersion.TryRead(RegionA, 1, 1, read, out reason));
        Assert.Equal(PatchCacheReject.MissingFile, reason);

        // 正向对照：同 seed 同 version 的新实例照样命中，说明上面两个是目录分层拦的，不是读取器坏了
        Assert.True(NewCache(dir).TryRead(RegionA, 1, 1, read, out _));
        AssertBitsEqual(heights, read, "同版本重开");
    }

    [Fact]
    public void NewInstance_SeesPreviousRunsFilesWithoutReadingContents()
    {
        using var dir = new TempCacheDir();
        var first = NewCache(dir);
        var h0 = MakeHeights(RegionA, 0, 0);
        var h1 = MakeHeights(RegionA, 0, 1);
        first.Write(RegionA, 0, 0, h0);
        first.Write(RegionA, 0, 1, h1);

        var second = NewCache(dir);
        Assert.Equal(2, second.EntryCount);
        Assert.Equal(2L * PlanetPatchCache.EntryBytes, second.BytesUsed);
        Assert.Equal(0, second.Hits);                 // 扫描不该被记成命中

        // 正向对照：扫出来的条目是真的能读，不是只数了个数
        var read = new float[PlanetPatchSampler.HeightmapLength];
        Assert.True(second.TryRead(RegionA, 0, 0, read, out _));
        AssertBitsEqual(h0, read, "重启后读取");
        Assert.Equal(1, second.Hits);
    }

    [Fact]
    public void Lru_EvictsLeastRecentlyUsed_AndKeepsTouchedOnDiskAndReadable()
    {
        using var dir = new TempCacheDir();
        long now = 0;
        var cache = new PlanetPatchCache(dir.Root, Seed, TerrainVersion, () => ++now);
        for (int px = 0; px < 4; px++)
            cache.Write(RegionA, px, 0, MakeHeights(RegionA, px, 0));
        Assert.Equal(4L * PlanetPatchCache.EntryBytes, cache.BytesUsed);

        var read = new float[PlanetPatchSampler.HeightmapLength];
        Assert.True(cache.TryRead(RegionA, 2, 0, read, out _));   // 2 号变成最新（时钟 5）

        int deleted = cache.TrimToBudget(2L * PlanetPatchCache.EntryBytes);
        Assert.Equal(2, deleted);
        Assert.Equal(2, cache.EntryCount);
        Assert.Equal(2L * PlanetPatchCache.EntryBytes, cache.BytesUsed);
        Assert.Equal(2, cache.Evictions);

        // 访问序 1、2 的（0 号、1 号）先走；3 号还在，2 号刚被 touch 更不可能走
        Assert.False(File.Exists(cache.PathFor(RegionA, 0, 0)));
        Assert.False(File.Exists(cache.PathFor(RegionA, 1, 0)));
        Assert.True(File.Exists(cache.PathFor(RegionA, 2, 0)));
        Assert.True(File.Exists(cache.PathFor(RegionA, 3, 0)));

        Assert.True(cache.TryRead(RegionA, 2, 0, read, out _));
        AssertBitsEqual(MakeHeights(RegionA, 2, 0), read, "幸存者仍可读出原值");
    }

    [Fact]
    public void Trim_IsNoOpWhenAlreadyInsideBudget_AndEmptiesAtZero()
    {
        using var dir = new TempCacheDir();
        var cache = NewCache(dir);
        cache.Write(RegionA, 0, 0, MakeHeights(RegionA, 0, 0));
        cache.Write(RegionA, 0, 1, MakeHeights(RegionA, 0, 1));

        Assert.Equal(0, cache.TrimToBudget(PlanetPatchCache.DefaultBudgetBytes));
        Assert.Equal(0, cache.TrimToBudget(2L * PlanetPatchCache.EntryBytes));   // 恰好等于占用也不能删
        Assert.Equal(2, cache.EntryCount);

        Assert.Equal(2, cache.TrimToBudget(0));
        Assert.Equal(0, cache.EntryCount);
        Assert.Equal(0, cache.BytesUsed);
        Assert.False(File.Exists(cache.PathFor(RegionA, 0, 0)));
        Assert.False(File.Exists(cache.PathFor(RegionA, 0, 1)));
    }

    [Fact]
    public void BudgetOfZeroOnEmptyCache_Terminates()
    {
        using var dir = new TempCacheDir();
        var cache = NewCache(dir);
        Assert.Equal(0, cache.TrimToBudget(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => cache.TrimToBudget(-1));
    }

    [Fact]
    public void DescribeStats_NamesEveryRejectionReason()
    {
        using var dir = new TempCacheDir();
        var cache = NewCache(dir);
        cache.Write(RegionA, 6, 6, MakeHeights(RegionA, 6, 6));
        var read = new float[PlanetPatchSampler.HeightmapLength];
        Assert.True(cache.TryRead(RegionA, 6, 6, read, out _));         // 一次命中
        Assert.False(cache.TryRead(RegionA, 6, 7, read, out _));        // 一次正常未命中

        string stats = cache.DescribeStats();
        Assert.Contains("hits=1", stats);
        Assert.Contains("misses=1", stats);
        Assert.Contains("writes=1", stats);
        foreach (PatchCacheReject reason in Enum.GetValues<PatchCacheReject>())
        {
            if (reason is PatchCacheReject.None or PatchCacheReject.MissingFile) continue;
            Assert.Contains("reject_" + reason.ToString().ToLowerInvariant() + "=", stats);
        }
        Assert.DoesNotContain("reject_missingfile=", stats);   // 未命中已经单列，不该再混进来一次
    }

    [Fact]
    public void BuffersOfWrongLength_ThrowInsteadOfSilentlyPassing()
    {
        using var dir = new TempCacheDir();
        var cache = NewCache(dir);
        Assert.Throws<ArgumentException>(() => cache.Write(RegionA, 0, 0, new float[16]));
        Assert.Throws<ArgumentException>(() =>
            cache.TryRead(RegionA, 0, 0, new float[16], out _));
        Assert.Throws<ArgumentNullException>(() =>
            cache.TryRead(RegionA, 0, 0, null!, out _));
        Assert.Throws<ArgumentException>(() =>
            PlanetPatchCache.BuildHeader(new byte[8], default));
        Assert.False(PlanetPatchCache.TryMatchHeader(new byte[8], default, out var shortReason));
        Assert.Equal(PatchCacheReject.BadLength, shortReason);
    }

    [Fact]
    public void LockedFile_IsNotJudgedCorrupt_AndSurvivesTheFailedRead()
    {
        using var dir = new TempCacheDir();
        var cache = NewCache(dir);
        var heights = MakeHeights(RegionA, 8, 9);
        cache.Write(RegionA, 8, 9, heights);
        string path = cache.PathFor(RegionA, 8, 9);
        var read = new float[PlanetPatchSampler.HeightmapLength];

        // 独占打开=并发读一定拿共享冲突；这条把"读不动"和"内容坏"的界线钉死：
        // 前者只能算未命中且不许删文件，否则一次竞态就会销毁好数据（本轮那个偶发红就是这个）
        using (var hog = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.False(cache.TryRead(RegionA, 8, 9, read, out var blocked));
            Assert.Equal(PatchCacheReject.MissingFile, blocked);
            Assert.Equal(0, cache.CorruptRejects);
        }

        Assert.True(File.Exists(path), "读不动就把好数据删了");
        Assert.True(cache.TryRead(RegionA, 8, 9, read, out var after));
        Assert.Equal(PatchCacheReject.None, after);
        AssertBitsEqual(heights, read, "占用解除后");

        // 正向对照的另一半：真的坏了才进损坏账，并且文件会被清掉
        var all = File.ReadAllBytes(path);
        all[PlanetPatchCache.HeaderSize + 7] ^= 0x01;
        File.WriteAllBytes(path, all);
        Assert.False(cache.TryRead(RegionA, 8, 9, read, out var corrupt));
        Assert.Equal(PatchCacheReject.BadCrc, corrupt);
        Assert.Equal(1, cache.CorruptRejects);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void DirectoryRootIsNamedBySeedAndVersion()
    {
        using var dir = new TempCacheDir();
        var cache = NewCache(dir);
        Assert.Equal(Path.Combine(dir.Root, "v1", "0123456789abcdef", "abcd1234"), cache.RootDirectory);
        Assert.Equal(Path.Combine(cache.RootDirectory, "region_" + RegionA.ToString("x16"), "near_3_4.hgt"),
                     cache.PathFor(RegionA, 3, 4));
        Assert.Equal(64 + 509 * 509 * 4, PlanetPatchCache.EntryBytes);
        Assert.Throws<ArgumentException>(() => new PlanetPatchCache("", Seed, TerrainVersion));
    }

    [Fact]
    public async Task ConcurrentWritesAndTrim_KeepTheLedgerExact()
    {
        using var dir = new TempCacheDir();
        var cache = NewCache(dir);
        const int threads = 8, perThread = 6;
        var heights = new float[perThread][];
        for (int i = 0; i < perThread; i++) heights[i] = MakeHeights(RegionA, i, 3);

        var unexpected = new ConcurrentQueue<string>();
        int writeFailures = 0, tornReads = 0;
        var start = new ManualResetEventSlim(false);
        var workers = new Task[threads];
        for (int t = 0; t < threads; t++)
        {
            int id = t;
            workers[t] = Task.Run(() =>
            {
                start.Wait();
                for (int i = 0; i < perThread; i++)
                {
                    try
                    {
                        cache.Write(RegionA, i, 3, heights[i]);
                    }
                    catch (IOException)
                    {
                        // "目标正被隔壁读，这次没存上"是允许的降级；但它不能变成别的异常，也不能写坏
                        Interlocked.Increment(ref writeFailures);
                    }
                    catch (Exception e)
                    {
                        unexpected.Enqueue("Write 抛了非 IOException：" + e.GetType().Name + " " + e.Message);
                    }

                    var read = new float[PlanetPatchSampler.HeightmapLength];
                    if (cache.TryRead(RegionA, i, 3, read, out _))
                    {
                        if (!MemoryMarshal.AsBytes(heights[i].AsSpan())
                                 .SequenceEqual(MemoryMarshal.AsBytes(read.AsSpan())))
                            Interlocked.Increment(ref tornReads);
                    }
                    if (id % 3 == 0) cache.TrimToBudget(2L * PlanetPatchCache.EntryBytes);
                }
            });
        }
        start.Set();
        await Task.WhenAll(workers);

        Assert.True(unexpected.IsEmpty, string.Join(" | ", unexpected));
        Assert.Equal(0, tornReads);                         // 并发下绝不允许读到半成品高度
        // 账本自己必须严格自洽；盘上文件只可以多（删除失败留孤儿，下次启动再清），不可以少
        Assert.Equal(cache.EntryCount * (long)PlanetPatchCache.EntryBytes, cache.BytesUsed);
        Assert.True(cache.EntryCount <= Directory.EnumerateFiles(cache.RootDirectory, "*.hgt",
                      SearchOption.AllDirectories).Count(), "有条目却没文件：落位与记账不在同一原子段");
        Assert.Equal(0, cache.CorruptRejects);              // 淘汰与读取交错不能被判成"坏文件"
        Assert.Empty(Directory.EnumerateFiles(cache.RootDirectory, "*.tmp", SearchOption.AllDirectories));

        string stats = cache.DescribeStats();
        Assert.Contains("entries=" + cache.EntryCount, stats);
        Assert.Contains("bytes=" + cache.BytesUsed, stats);
        Assert.Contains("reject_badlength=0", stats);
    }

    [Fact]
    public async Task ConcurrentWritesOfTheSamePatch_DoNotDoubleCount()
    {
        using var dir = new TempCacheDir();
        var cache = NewCache(dir);
        var heights = MakeHeights(RegionA, 4, 4);

        // 同一个 key 同时被多个来源写：临时名必须各自唯一，而占用只能记一份
        var barrier = new Barrier(4);
        var unexpected = new ConcurrentQueue<string>();
        var workers = new Task[4];
        for (int t = 0; t < 4; t++)
        {
            workers[t] = Task.Run(() =>
            {
                try
                {
                    barrier.SignalAndWait();
                    cache.Write(RegionA, 4, 4, heights);
                }
                catch (IOException) { /* 允许"这次没存上" */ }
                catch (Exception e) { unexpected.Enqueue(e.GetType().Name + ": " + e.Message); }
            });
        }
        await Task.WhenAll(workers);

        Assert.True(unexpected.IsEmpty, string.Join(" | ", unexpected));
        Assert.Equal(1, cache.EntryCount);
        Assert.Equal((long)PlanetPatchCache.EntryBytes, cache.BytesUsed);   // 四份写只记一份占用
        var read = new float[PlanetPatchSampler.HeightmapLength];
        Assert.True(cache.TryRead(RegionA, 4, 4, read, out var reason), "同键并发写完必须还能读回来");
        Assert.Equal(PatchCacheReject.None, reason);
        AssertBitsEqual(heights, read, "同键并发写");
        Assert.Empty(Directory.EnumerateFiles(cache.RootDirectory, "*.tmp", SearchOption.AllDirectories));
    }
}

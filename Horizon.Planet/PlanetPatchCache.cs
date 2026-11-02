using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Horizon.Planet;

/// <summary>
/// 缓存文件读不回来的原因。MissingFile 是正常未命中；其余每一条都意味着"盘上有东西但不能信"，
/// 消费方要能区分这两种，否则"没有缓存"和"缓存坏了"在日志里同形。
/// </summary>
public enum PatchCacheReject
{
    None = 0,
    MissingFile,
    BadLength,
    BadMagic,
    BadFormatVersion,
    BadReserved,
    BadGeometry,
    BadSeed,
    BadTerrainVersion,
    BadRegion,
    BadPatchCoords,
    BadCrc,
}

/// <summary>近景 patch 缓存文件的自述信息：六个身份字段 + 几何 + 载荷校验和。</summary>
public readonly struct PatchCacheRecord
{
    public int FormatVersion { get; init; }
    public long Seed { get; init; }
    public uint TerrainVersion { get; init; }
    public long RegionId { get; init; }
    public int PatchX { get; init; }
    public int PatchZ { get; init; }
    public int Edge { get; init; }
    public int FloatCount { get; init; }
    public uint Crc32 { get; init; }
}

/// <summary>
/// 近景 patch 高度图的磁盘缓存（§7）。
/// <para>载荷是 <see cref="PlanetPatchSampler.FillPatchHeights"/> 直接产出的 float32 数组，不做 int16
/// 二次量化——缓存命中的结果必须和重算逐位相同，否则"命中"本身就成了一种精度分支。</para>
/// <para>目录已按 formatVersion/seed/terrainVersion 分层，header 里再写一遍同样的身份：文件名说
/// "这是哪一版的哪个 patch"，文件内容必须自证，两边不一致就当坏文件删掉重算。</para>
/// </summary>
public sealed class PlanetPatchCache
{
    /// <summary>文件格式版本；header 布局变一次就要 +1（老文件全部自动失效）。</summary>
    public const int FileFormatVersion = 1;

    /// <summary>header 固定 64 字节：46 字节字段 + 16 字节保留（必须为 0）。</summary>
    public const int HeaderSize = 64;

    private const int MagicOffset = 0;
    private const int FormatVersionOffset = 4;
    private const int SeedOffset = 8;
    private const int TerrainVersionOffset = 16;
    private const int RegionIdOffset = 20;
    private const int PatchXOffset = 28;
    private const int PatchZOffset = 32;
    private const int EdgeOffset = 36;
    private const int FloatCountOffset = 40;
    private const int CrcOffset = 44;
    private const int ReservedOffset = 48;
    private const int ReservedBytes = HeaderSize - ReservedOffset;
    private static readonly byte[] Magic = { (byte)'H', (byte)'P', (byte)'H', (byte)'1' };

    /// <summary>一个近景 patch 文件的字节数（header + 509² float32 = 1,036,388）。</summary>
    public const int PayloadBytes = PlanetPatchSampler.HeightmapLength * 4;
    public const int EntryBytes = HeaderSize + PayloadBytes;

    /// <summary>默认 2 GB（§7.1）。</summary>
    public const long DefaultBudgetBytes = 2L * 1024 * 1024 * 1024;

    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<long> _clock;
    private readonly long[] _rejectCounts = new long[(int)PatchCacheReject.BadCrc + 1];

    /// <summary>
    /// 只保护"账本"（条目表与字节数），不保护文件读写本身。
    /// <para>后台生成线程会并发写不同 patch（§13.7 的结论：生成必须离开主线程），
    /// 而 `+=` 与 Dictionary 都不是原子的——不加这层锁，预算数会悄悄漂，LRU 也会看见不存在的条目。</para>
    /// </summary>
    private readonly object _gate = new();
    private long _bytesUsed;
    private long _hits, _misses, _writes, _evictions, _deleteFailures;
    private int _tmpCounter;

    public long Seed { get; }
    public uint TerrainVersion { get; }

    /// <summary>版本化根目录：{root}/v{format}/{seed:x16}/{terrainVersion:x8}。</summary>
    public string RootDirectory { get; }

    public long Hits => Interlocked.Read(ref _hits);
    public long Misses => Interlocked.Read(ref _misses);
    public long Writes => Interlocked.Read(ref _writes);
    public long Evictions => Interlocked.Read(ref _evictions);
    public long DeleteFailures => Interlocked.Read(ref _deleteFailures);
    public long BytesUsed { get { lock (_gate) return _bytesUsed; } }
    public int EntryCount { get { lock (_gate) return _entries.Count; } }

    public PlanetPatchCache(string rootDirectory, long seed, uint terrainVersion, Func<long>? accessClock = null)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
            throw new ArgumentException("缓存根目录不能为空", nameof(rootDirectory));
        if (!BitConverter.IsLittleEndian)
            throw new PlatformNotSupportedException("缓存文件按小端写入，大端平台需要显式换序后才能启用");

        RootDirectory = Path.Combine(rootDirectory,
                                     "v" + FileFormatVersion.ToString(CultureInfo.InvariantCulture),
                                     seed.ToString("x16", CultureInfo.InvariantCulture),
                                     terrainVersion.ToString("x8", CultureInfo.InvariantCulture));
        Seed = seed;
        TerrainVersion = terrainVersion;
        _clock = accessClock ?? (static () => DateTime.UtcNow.Ticks);
        ScanExisting();
    }

    public long RejectCount(PatchCacheReject reason) => Interlocked.Read(ref _rejectCounts[(int)reason]);

    /// <summary>除了"文件本来就不在"之外的拒绝总数：这才是缓存真正坏掉的数量。</summary>
    public long CorruptRejects
    {
        get
        {
            long sum = 0;
            for (int i = 0; i < _rejectCounts.Length; i++)
            {
                if (i == (int)PatchCacheReject.None || i == (int)PatchCacheReject.MissingFile) continue;
                sum += Interlocked.Read(ref _rejectCounts[i]);
            }
            return sum;
        }
    }

    /// <summary>
    /// 只列目录、不读内容（§7.2 的"轻量校验"口径）。上一次运行留下的文件必须计入占用和 LRU，
    /// 否则重启后预算闸门以为缓存是空的，会重写整个目录还不觉得超预算。
    /// </summary>
    private void ScanExisting()
    {
        if (!Directory.Exists(RootDirectory)) return;
        foreach (string path in Directory.EnumerateFiles(RootDirectory, "*.hgt", SearchOption.AllDirectories))
        {
            try
            {
                var info = new FileInfo(path);
                Track(path, info.Length, info.LastWriteTimeUtc.Ticks);
            }
            catch (IOException) { /* 列到一半文件消失：下次启动再说 */ }
        }
    }

    private void Track(string path, long bytes, long lastAccess)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(path, out var existing)) _bytesUsed -= existing.Bytes;
            _entries[path] = new Entry(path, bytes, lastAccess);
            _bytesUsed += bytes;
        }
    }

    private void Forget(string path)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(path, out var entry))
            {
                _bytesUsed -= entry.Bytes;
                _entries.Remove(path);
            }
        }
    }

    /// <summary>取当前最旧的一条（同刻并列用路径序打破）。锁内只做快照，删文件在锁外。</summary>
    private Entry? OldestEntry()
    {
        lock (_gate)
        {
            Entry? oldest = null;
            foreach (var entry in _entries.Values)
            {
                if (oldest is null || entry.LastAccess < oldest.LastAccess ||
                    (entry.LastAccess == oldest.LastAccess &&
                     string.CompareOrdinal(entry.Path, oldest.Path) < 0))
                    oldest = entry;
            }
            return oldest;
        }
    }

    /// <summary>某个 patch 的缓存路径。region 单独一层目录，跨 region 的同格坐标不可能撞名。</summary>
    public string PathFor(long regionId, int patchX, int patchZ)
    {
        string region = "region_" + regionId.ToString("x16", CultureInfo.InvariantCulture);
        string name = string.Format(CultureInfo.InvariantCulture, "near_{0}_{1}.hgt", patchX, patchZ);
        return Path.Combine(RootDirectory, region, name);
    }

    /// <summary>当前实例读写某个 patch 时期望的 header 内容（crc 由调用方补齐）。</summary>
    public PatchCacheRecord RecordFor(long regionId, int patchX, int patchZ, uint payloadCrc) => new()
    {
        FormatVersion = FileFormatVersion,
        Seed = Seed,
        TerrainVersion = TerrainVersion,
        RegionId = regionId,
        PatchX = patchX,
        PatchZ = patchZ,
        Edge = PlanetPatchSampler.HeightmapEdge,
        FloatCount = PlanetPatchSampler.HeightmapLength,
        Crc32 = payloadCrc,
    };

    /// <summary>
    /// header 的唯一写法：写盘用它，测试改坏单个字段也用它——所以格式布局在这一个文件里只存在一份。
    /// </summary>
    public static void BuildHeader(Span<byte> header, in PatchCacheRecord record)
    {
        if (header.Length != HeaderSize)
            throw new ArgumentException($"header 必须恰好 {HeaderSize} 字节", nameof(header));

        header.Clear();
        Magic.CopyTo(header.Slice(MagicOffset, Magic.Length));
        BinaryPrimitives.WriteInt32LittleEndian(header.Slice(FormatVersionOffset, 4), record.FormatVersion);
        BinaryPrimitives.WriteInt64LittleEndian(header.Slice(SeedOffset, 8), record.Seed);
        BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(TerrainVersionOffset, 4), record.TerrainVersion);
        BinaryPrimitives.WriteInt64LittleEndian(header.Slice(RegionIdOffset, 8), record.RegionId);
        BinaryPrimitives.WriteInt32LittleEndian(header.Slice(PatchXOffset, 4), record.PatchX);
        BinaryPrimitives.WriteInt32LittleEndian(header.Slice(PatchZOffset, 4), record.PatchZ);
        BinaryPrimitives.WriteInt32LittleEndian(header.Slice(EdgeOffset, 4), record.Edge);
        BinaryPrimitives.WriteInt32LittleEndian(header.Slice(FloatCountOffset, 4), record.FloatCount);
        BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(CrcOffset, 4), record.Crc32);
        // ReservedOffset.. 已由 header.Clear() 归零
    }

    /// <summary>
    /// header 的唯一读法：身份字段逐条比对期望值，任何一条不符都不能用——错 patch 的高度比没高度更糟。
    /// 载荷 crc 不在此处（需要整段载荷），由 <see cref="TryRead"/> 补上。
    /// </summary>
    public static bool TryMatchHeader(ReadOnlySpan<byte> header, in PatchCacheRecord expected,
                                      out PatchCacheReject reason)
    {
        if (header.Length != HeaderSize)
        {
            reason = PatchCacheReject.BadLength;
            return false;
        }
        for (int i = 0; i < Magic.Length; i++)
        {
            if (header[MagicOffset + i] != Magic[i])
            {
                reason = PatchCacheReject.BadMagic;
                return false;
            }
        }
        if (BinaryPrimitives.ReadInt32LittleEndian(header.Slice(FormatVersionOffset, 4)) != expected.FormatVersion)
        {
            reason = PatchCacheReject.BadFormatVersion;
            return false;
        }
        for (int i = 0; i < ReservedBytes; i++)
        {
            if (header[ReservedOffset + i] != 0)
            {
                reason = PatchCacheReject.BadReserved;
                return false;
            }
        }
        int edge = BinaryPrimitives.ReadInt32LittleEndian(header.Slice(EdgeOffset, 4));
        int floats = BinaryPrimitives.ReadInt32LittleEndian(header.Slice(FloatCountOffset, 4));
        if (edge != expected.Edge || floats != expected.FloatCount)
        {
            reason = PatchCacheReject.BadGeometry;
            return false;
        }
        reason = PatchCacheReject.None;
        return true;
    }

    /// <summary>把 patch 高度写进缓存。原子：先写 .tmp、flush 到盘，再 Move 覆盖目标。</summary>
    public void Write(long regionId, int patchX, int patchZ, ReadOnlySpan<float> heights)
    {
        if (heights.Length != PlanetPatchSampler.HeightmapLength)
            throw new ArgumentException($"必须是 {PlanetPatchSampler.HeightmapLength} 个高度值", nameof(heights));

        string path = PathFor(regionId, patchX, patchZ);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var payload = MemoryMarshal.AsBytes(heights);
        var record = RecordFor(regionId, patchX, patchZ, Crc32.Compute(payload));

        var file = new byte[EntryBytes];
        BuildHeader(file.AsSpan(0, HeaderSize), record);
        payload.CopyTo(file.AsSpan(HeaderSize));

        // 临时名带本实例的递增序号：并发写不同 patch 时不能共用一个 .tmp 路径
        string tmp = path + "." + Interlocked.Increment(ref _tmpCounter).ToString(CultureInfo.InvariantCulture) + ".tmp";
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
        {
            fs.Write(file);
            fs.Flush(true);
        }
        // 只在锁内做"落位 + 记账"这两步：1 MB 的写和 fsync 留在锁外，
        // 否则并发写会串行到磁盘上；而 Move 与 Track 之间被别的线程插进来，账本和盘上文件就会不一致
        lock (_gate)
        {
            File.Move(tmp, path, true);
            Interlocked.Increment(ref _writes);
            Track(path, file.Length, _clock());
        }
    }

    /// <summary>
    /// 读缓存。每次拒绝都会把这个文件删掉并记账，下一次调用就是干净的未命中——
    /// 坏文件留在盘上只会每次都被读、每次都被拒。
    /// </summary>
    public bool TryRead(long regionId, int patchX, int patchZ, float[] heights, out PatchCacheReject reason)
    {
        if (heights is null) throw new ArgumentNullException(nameof(heights));
        if (heights.Length != PlanetPatchSampler.HeightmapLength)
            throw new ArgumentException($"必须是 {PlanetPatchSampler.HeightmapLength} 个高度值", nameof(heights));

        string path = PathFor(regionId, patchX, patchZ);
        if (!File.Exists(path))
        {
            Forget(path);
            reason = PatchCacheReject.MissingFile;
            Interlocked.Increment(ref _misses);
            Interlocked.Increment(ref _rejectCounts[(int)reason]);
            return false;
        }

        byte[] all;
        try
        {
            all = File.ReadAllBytes(path);
        }
        catch (IOException)
        {
            reason = PatchCacheReject.BadLength;
            Interlocked.Increment(ref _rejectCounts[(int)reason]);
            Discard(path);
            return false;
        }

        if (all.Length != EntryBytes)
        {
            reason = PatchCacheReject.BadLength;
            Interlocked.Increment(ref _rejectCounts[(int)reason]);
            Discard(path);
            return false;
        }

        var expected = RecordFor(regionId, patchX, patchZ, 0);
        if (!TryMatchHeader(all.AsSpan(0, HeaderSize), expected, out reason))
        {
            Interlocked.Increment(ref _rejectCounts[(int)reason]);
            Discard(path);
            return false;
        }

        long seed = BinaryPrimitives.ReadInt64LittleEndian(all.AsSpan(SeedOffset, 8));
        uint terrainVersion = BinaryPrimitives.ReadUInt32LittleEndian(all.AsSpan(TerrainVersionOffset, 4));
        long storedRegion = BinaryPrimitives.ReadInt64LittleEndian(all.AsSpan(RegionIdOffset, 8));
        int storedX = BinaryPrimitives.ReadInt32LittleEndian(all.AsSpan(PatchXOffset, 4));
        int storedZ = BinaryPrimitives.ReadInt32LittleEndian(all.AsSpan(PatchZOffset, 4));
        uint crc = BinaryPrimitives.ReadUInt32LittleEndian(all.AsSpan(CrcOffset, 4));

        if (seed != expected.Seed) reason = PatchCacheReject.BadSeed;
        else if (terrainVersion != expected.TerrainVersion) reason = PatchCacheReject.BadTerrainVersion;
        else if (storedRegion != expected.RegionId) reason = PatchCacheReject.BadRegion;
        else if (storedX != expected.PatchX || storedZ != expected.PatchZ) reason = PatchCacheReject.BadPatchCoords;
        else if (Crc32.Compute(all.AsSpan(HeaderSize)) != crc) reason = PatchCacheReject.BadCrc;
        else
        {
            MemoryMarshal.Cast<byte, float>(all.AsSpan(HeaderSize)).CopyTo(heights);
            Interlocked.Increment(ref _hits);
            Track(path, all.Length, _clock());
            reason = PatchCacheReject.None;
            return true;
        }

        Interlocked.Increment(ref _rejectCounts[(int)reason]);
        Discard(path);
        return false;
    }

    private void Discard(string path)
    {
        // 与 Write/Trim 同一个原子段：删文件和出账不能中间被别人插进来
        lock (_gate)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (IOException) { /* 被别人锁住：已要出表，不再计入占用，下次启动再清 */ }
            catch (UnauthorizedAccessException) { }
            Forget(path);
        }
    }

    /// <summary>
    /// 按"最后访问"淘汰到预算内，返回真正删掉的个数。
    /// <para>近景 patch 是每帧被读/写的那一批，所以按新近排序就等价于 §7.1 的"近景优先保留"。</para>
    /// <para>同刻并列用路径序打破，不依赖时钟分辨率——判据只守"总量到预算内 + 被逐的确实从盘上消失"。</para>
    /// </summary>
    public int TrimToBudget(long maxBytes)
    {
        if (maxBytes < 0) throw new ArgumentOutOfRangeException(nameof(maxBytes));

        int deleted = 0;
        while (true)
        {
            // 一次锁内完成"挑最旧 → 删文件 → 出账"：这样并发的 Write 不会把刚落位的文件被这边删掉、
            // 也不会出现"账本里有条目但盘上没文件"（那是 §7.1 预算闸门数不准的根源）
            lock (_gate)
            {
                if (_bytesUsed <= maxBytes) return deleted;
                Entry? oldest = OldestEntry();
                if (oldest is null) return deleted;    // 账本已空：停，不能死循环

                try
                {
                    File.Delete(oldest.Path);
                    deleted++;
                    Interlocked.Increment(ref _evictions);
                }
                catch (IOException) { Interlocked.Increment(ref _deleteFailures); }
                catch (UnauthorizedAccessException) { Interlocked.Increment(ref _deleteFailures); }
                Forget(oldest.Path);
            }
        }
    }

    /// <summary>
    /// 给日志用的一行统计：命中/未命中/写入/淘汰/每种拒绝各几次都要看得见。
    /// <para>只报一个总数就查不出"为什么没命中"，所以按原因逐个念（键名用 ASCII，避免中文标签吞掉后面的数字）。</para>
    /// </summary>
    public string DescribeStats()
    {
        var sb = new StringBuilder();
        sb.Append("patch_cache hits=").Append(Hits)
          .Append(" misses=").Append(Misses)
          .Append(" writes=").Append(Writes)
          .Append(" evictions=").Append(Evictions)
          .Append(" delete_failures=").Append(DeleteFailures)
          .Append(" entries=").Append(_entries.Count)
          .Append(" bytes=").Append(_bytesUsed);
        for (int i = 0; i < _rejectCounts.Length; i++)
        {
            if (i == (int)PatchCacheReject.None || i == (int)PatchCacheReject.MissingFile) continue;
            sb.Append(" reject_")
              .Append(((PatchCacheReject)i).ToString().ToLowerInvariant())
              .Append('=').Append(Interlocked.Read(ref _rejectCounts[i]));
        }
        return sb.ToString();
    }

    private sealed class Entry
    {
        public Entry(string path, long bytes, long lastAccess)
        {
            Path = path;
            Bytes = bytes;
            LastAccess = lastAccess;
        }

        public string Path { get; }
        public long Bytes { get; }
        public long LastAccess { get; set; }
    }
}

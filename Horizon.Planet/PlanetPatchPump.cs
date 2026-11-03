using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Horizon.Planet;

/// <summary>一块已经生成好的 patch：主线程拿到它才能交给引擎。</summary>
public readonly struct ResolvedPatch
{
    internal ResolvedPatch(PatchKey key, float[] heights, PatchFillSource source, RegionKey region)
    {
        Key = key;
        Heights = heights;
        Source = source;
        Region = region;
    }

    public PatchKey Key { get; }
    public float[] Heights { get; }
    public PatchFillSource Source { get; }

    /// <summary>结果属于哪个区域：换区之后旧区域的结果一律不许落地。</summary>
    public RegionKey Region { get; }
}

/// <summary>
/// 近景 patch 的后台生成队列（§13.7 的结论：178.9 ms/块绝不能占主线程）。
/// <para>主线程只做两件事：<see cref="Request"/> 投递、<see cref="TryDequeueCompleted"/> 取成品；
/// 采样与落盘都在线程池上。<see cref="PlanetPatchSampler.FillPatchHeights"/> 按行分片且结果与线程数无关，
/// 所以后台算出来的与主线程算的是同一批数——这点由判据逐位对比钉住，不靠"应该一样"。</para>
/// <para>换区域时旧帧的坐标语义作废：还没启动的不跑，已经跑完的丢掉并计数，
/// 绝不把旧区域的高度贴进新区域。</para>
/// </summary>
public sealed class PlanetPatchPump : IDisposable
{
    /// <summary>池里最多留几块 1 MB 缓冲区，超了就让它自然回收。</summary>
    public const int MaxPooledArrays = 4;

    private readonly PlanetTerrain _terrain;
    private readonly PlanetPatchCache? _cache;
    private readonly HeightSampler? _samplerTemplate;
    private readonly SemaphoreSlim _slots;
    private readonly ConcurrentQueue<PatchKey> _queue = new();
    private readonly ConcurrentDictionary<PatchKey, byte> _scheduled = new();
    private readonly ConcurrentQueue<ResolvedPatch> _completed = new();
    private readonly ConcurrentBag<float[]> _pool = new();
    private readonly int _maxWorkers;

    private RegionFrame _frame;
    private int _epoch;
    private int _pending;
    private int _inFlight;
    private int _maxInFlight;
    private int _stopRequested;

    /// <summary>累计量一律 long：长时间跑图后 int 会翻负，回执与对账等式会一次性失真。</summary>
    private long _requested, _completedCount, _staleDropped, _duplicatesRejected;
    private long _writeFailures, _generationFailures, _rents, _poolHits;

    /// <summary>
    /// 已生成好、但没被主线程取走就因换区/停止被丢弃的件数。
    /// <para>它属于 <see cref="Completed"/> 的一部分，不能再算进 <see cref="StaleDropped"/>——
    /// 否则 <c>Requested = Completed + Stale + Duplicate</c> 这条等式会把同一件计两次，
    /// 看着"对得上"其实账是错的。</para>
    /// </summary>
    private long _readyDiscarded;

    public PlanetPatchPump(RegionFrame frame, PlanetTerrain terrain, PlanetPatchCache? cache = null,
                           HeightSampler? samplerTemplate = null, int maxWorkers = 2)
    {
        if (maxWorkers < 1) throw new ArgumentOutOfRangeException(nameof(maxWorkers), maxWorkers, "至少 1 个工作线程");
        _terrain = terrain ?? throw new ArgumentNullException(nameof(terrain));
        if (samplerTemplate is not null &&
            !ReferenceEquals(samplerTemplate.Grid, (terrain.Heights as HybridHeightSource)?.BaseField))
            throw new ArgumentException("samplerTemplate 的基场必须与 terrain 的高度源同属一个混合源",
                                        nameof(samplerTemplate));
        _frame = frame;
        _cache = cache;
        _samplerTemplate = samplerTemplate;
        _maxWorkers = maxWorkers;
        _slots = new SemaphoreSlim(maxWorkers, maxWorkers);
    }

    public RegionFrame Frame => _frame;
    public int MaxWorkers => _maxWorkers;

    public long Requested => Interlocked.Read(ref _requested);
    public long Completed => Interlocked.Read(ref _completedCount);

    /// <summary>
    /// 没成为成品的丢件：换区时被清的排队、代次过期作废的在飞结果、生成抛异常的块、停止后仍投来的投递。
    /// <para>对账等式是 <c>Requested = Completed + StaleDropped + DuplicatesRejected</c>；
    /// <see cref="GenerationFailures"/> 是本数的一个子口径，不要再往上加。</para>
    /// </summary>
    public long StaleDropped => Interlocked.Read(ref _staleDropped);

    /// <summary>成品未取走就被丢（换区/停止）：这是 <see cref="Completed"/> 的子口径，不参与上面的等式。</summary>
    public long ReadyDiscarded => Interlocked.Read(ref _readyDiscarded);

    public long DuplicatesRejected => Interlocked.Read(ref _duplicatesRejected);
    public long CacheWriteFailures => Interlocked.Read(ref _writeFailures);

    /// <summary>生成过程本身抛异常的件数（<see cref="StaleDropped"/> 的子口径）。丢掉的那块由调度器下帧重投。</summary>
    public long GenerationFailures => Interlocked.Read(ref _generationFailures);

    public int PendingCount => Volatile.Read(ref _pending);
    public int InFlightCount => Volatile.Read(ref _inFlight);
    public int ReadyCount => _completed.Count;

    /// <summary>实测到的并发峰值。上限之内的断言要两头看：没超，而且真的并发过。</summary>
    public int MaxInFlight => Volatile.Read(ref _maxInFlight);

    public int PooledArrays => _pool.Count;
    public long Rents => Interlocked.Read(ref _rents);
    public long PoolHits => Interlocked.Read(ref _poolHits);

    /// <summary>投递一块 patch。同一块在飞/在排时不会重复生成，返回 false 表示已经在办或已停止。</summary>
    public bool Request(PatchKey key)
    {
        Interlocked.Increment(ref _requested);
        if (Volatile.Read(ref _stopRequested) != 0)
        {
            // 停止之后不再收活，但仍然记账：不然等式 Requested = Completed + Stale + Duplicate 会少一件
            Interlocked.Increment(ref _staleDropped);
            return false;
        }
        if (!_scheduled.TryAdd(key, 0))
        {
            Interlocked.Increment(ref _duplicatesRejected);
            return false;
        }
        _queue.Enqueue(key);
        Interlocked.Increment(ref _pending);
        Drain();
        return true;
    }

    /// <summary>有空位就取一件开跑；满了就等某个 worker 收尾时再被推动。</summary>
    private void Drain()
    {
        if (Volatile.Read(ref _stopRequested) != 0) return;
        if (!_slots.Wait(0)) return;
        if (!_queue.TryDequeue(out var key))
        {
            _slots.Release();
            return;
        }

        Interlocked.Decrement(ref _pending);
        int epoch = Volatile.Read(ref _epoch);
        RegionFrame frame = _frame;
        Int32Max(ref _inFlight, ref _maxInFlight);

        _ = Task.Run(() =>
        {
            try { Work(key, epoch, frame); }
            finally
            {
                Interlocked.Decrement(ref _inFlight);
                _slots.Release();
                Drain();
            }
        });
    }

    private void Work(PatchKey key, int epoch, RegionFrame frame)
    {
        float[] buffer = Rent();
        var source = PatchFillSource.Generated;
        bool ok = true;
        try
        {
            // 每次生成都用自己的一份窗口缓存：HeightSampler 内部有 5×5 可变模板，多个 worker 共用会互相踩
            HeightSampler? sampler = _samplerTemplate is null
                ? null
                : new HeightSampler(_samplerTemplate.Grid, _samplerTemplate.Hybrid);
            // 泵本身就是并发源：多 worker 时每块只用自己的那一个线程算，
            // 否则每块再按行分片就变成 workers × ProcessorCount 的嵌套超额订阅（实测 179 ms/块 涨到 570 ms/块）
            source = PlanetPatchFill.Fill(frame, _terrain, _cache, sampler, key, buffer,
                                          parallel: _maxWorkers <= 1, out bool writeFailed);
            if (writeFailed) Interlocked.Increment(ref _writeFailures);
        }
        catch (Exception)
        {
            ok = false;      // 一块失败不能弄死 worker：丢掉，让调度器下帧重投，但要计数
            Interlocked.Increment(ref _generationFailures);
        }

        if (!ok || Volatile.Read(ref _epoch) != epoch)
        {
            Interlocked.Increment(ref _staleDropped);
            _scheduled.TryRemove(key, out _);
            Release(buffer);
            return;
        }

        Interlocked.Increment(ref _completedCount);
        _completed.Enqueue(new ResolvedPatch(key, buffer, source, frame.Key));
    }

    /// <summary>
    /// 主线程取一件成品（每帧限量就在这里控）。取出后缓冲区归调用方，交给引擎后必须
    /// <see cref="Release"/> 还回来。
    /// </summary>
    public bool TryDequeueCompleted(out ResolvedPatch resolved)
    {
        if (_completed.TryDequeue(out resolved))
        {
            _scheduled.TryRemove(resolved.Key, out _);
            return true;
        }
        resolved = default;
        return false;
    }

    /// <summary>换区域：没启动的排队直接丢，抬高代次让在飞的成品作废，已备好的成品也回收。</summary>
    public void BeginRegion(RegionFrame frame)
    {
        _frame = frame;
        Interlocked.Increment(ref _epoch);

        DiscardReady();
        DiscardQueue();
        Drain();
    }

    /// <summary>取件前就被丢弃的成品：记在 Completed 的子口径上，不重复记进丢件。</summary>
    private void DiscardReady()
    {
        while (_completed.TryDequeue(out var ready))
        {
            _scheduled.TryRemove(ready.Key, out _);
            Release(ready.Heights);
            Interlocked.Increment(ref _readyDiscarded);
        }
    }

    private void DiscardQueue()
    {
        int dropped = 0;
        while (_queue.TryDequeue(out var stale))
        {
            _scheduled.TryRemove(stale, out _);
            dropped++;
        }
        Interlocked.Exchange(ref _pending, 0);
        if (dropped > 0) Interlocked.Add(ref _staleDropped, dropped);
    }

    /// <summary>向池里要一块缓冲区（一块 patch 的 float32 高度 = 1 MB，逐帧新建会把 LOH 打满）。</summary>
    public float[] Rent()
    {
        Interlocked.Increment(ref _rents);
        if (_pool.TryTake(out var array))
        {
            Interlocked.Increment(ref _poolHits);
            return array;
        }
        return new float[PlanetPatchSampler.HeightmapLength];
    }

    /// <summary>用完归还。重复归还本身是 bug：两块 patch 会共用同一份高度数组。</summary>
    public void Release(float[] heights)
    {
        if (heights is null) throw new ArgumentNullException(nameof(heights));
        if (heights.Length != PlanetPatchSampler.HeightmapLength)
            throw new ArgumentException($"必须是 {PlanetPatchSampler.HeightmapLength} 个高度值", nameof(heights));
        if (_pool.Count < MaxPooledArrays) _pool.Add(heights);
    }

    /// <summary>把 inFlight +1 并顺带记录峰值。</summary>
    private static void Int32Max(ref int counter, ref int peak)
    {
        int now = Interlocked.Increment(ref counter);
        int seen = Volatile.Read(ref peak);
        while (now > seen && Interlocked.CompareExchange(ref peak, now, seen) != seen)
            seen = Volatile.Read(ref peak);
    }

    /// <summary>给日志用的一行回执：等式两边都念出来，少一件都会在这行露出来。</summary>
    public string Describe() => string.Format(CultureInfo.InvariantCulture,
        "pump requested={0} completed={1} stale={2} duplicate={3} ready_discarded={4} inflight={5} pending={6} " +
        "ready={7} max_inflight={8} workers={9} write_failures={10} generation_failures={11} rents={12} " +
        "pool_hits={13} pooled={14}",
        Requested, Completed, StaleDropped, DuplicatesRejected, ReadyDiscarded, InFlightCount, PendingCount,
        ReadyCount, MaxInFlight, _maxWorkers, CacheWriteFailures, GenerationFailures, Rents, PoolHits, PooledArrays);

    /// <summary>
    /// 停止：不再接新活，等在飞的收尾，然后把排队与未取走的成品清干净。
    /// 会阻塞到最后一块生成结束（每块 ~179 ms），换区/退出时调用，不要在每帧路径上调。
    /// </summary>
    public void Dispose()
    {
        Volatile.Write(ref _stopRequested, 1);
        DiscardQueue();
        while (InFlightCount > 0) Thread.Sleep(1);
        DiscardReady();
        _slots.Dispose();
    }
}

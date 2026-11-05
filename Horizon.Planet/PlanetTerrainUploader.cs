using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Threading;

namespace Horizon.Planet;

/// <summary>一次已经交给引擎（或本该交给引擎但被作废）的上传/卸载：主线程取走它做账，并把缓冲区还回池子。</summary>
public readonly struct UploadedPatch
{
    internal UploadedPatch(PatchKey key, float[]? heights, RegionKey region, bool remove,
                          bool failed, double ms, bool stale)
    {
        Key = key;
        Heights = heights;
        Region = region;
        IsRemove = remove;
        Failed = failed;
        Ms = ms;
        Stale = stale;
    }

    public PatchKey Key { get; }

    /// <summary>
    /// 那块 1 MB 高度缓冲；卸载（<see cref="IsRemove"/>）没有缓冲区，这里是 null。<b>所有权一路跟着这件成品回到取件方</b>：不管是写进了引擎、
    /// 引擎报了失败、还是排队期间换了区域根本没碰引擎，取件方都必须把它还回池子——
    /// 一个字节也不可以在这里悄悄丢掉。
    /// </summary>
    public float[]? Heights { get; }

    public RegionKey Region { get; }

    /// <summary>这是一次卸载而不是上传（卸载没有缓冲区）。</summary>
    public bool IsRemove { get; }

    /// <summary>引擎报失败（<c>SetupPatchHeightMap</c> 返回 true）或调用抛了异常。</summary>
    public bool Failed { get; }

    /// <summary>这次调用在 worker 上花了多少毫秒（stale 件恒为 0，因为它根本没进引擎）。</summary>
    public double Ms { get; }

    /// <summary>排队或开跑之前区域已经换了，所以这件没碰引擎。</summary>
    public bool Stale { get; }
}

/// <summary>
/// 引擎侧那两次调用的唯一入口。<b>实现里的方法只会被 uploader 的那一条 worker 线程调用</b>，
/// 所以地形 patch 表始终是单线程访问——这不是约定，是本类的结构保证（判据实测并发峰值恰好是 1）。
/// </summary>
public interface IPatchUploadTarget
{
    /// <summary>
    /// 把一块高度贴进引擎。<b>返回 true 表示失败</b>——保持与 Flax
    /// <c>Terrain.SetupPatchHeightMap</c> 同一口径，免得在边界上再做一次"负负得正"。
    /// </summary>
    bool Upload(PatchKey key, float[] heights);

    /// <summary>从引擎里摘掉一块；不在位就什么也不做。</summary>
    void Remove(PatchKey key);
}

/// <summary>
/// 近景 patch 的<b>上传</b>后台队列——把 §13.15 量到的 21–22 ms/块从主线程挪走。
/// <para>
/// 与 <see cref="PlanetPatchPump"/> 的分工是一条硬线：泵算高度（可以多 worker，结果与线程数无关，
/// 有逐位对比的判据钉着），uploader 只把结果交给引擎，而且<b>只能有一条线程</b>——
/// §13.16 实测并发上传会让引擎把高度写成别的块的数值（最大 69 m 级偏差），
/// 而 <c>setup_failures</c>、异常、缺失全程为 0，任何只看"调用成没成"的接线都会读成正常。
/// </para>
/// <para>
/// 换区域时旧坐标语义作废：排队中的件与刚要开跑的件都在<b>调用之前</b>被代次挡掉，
/// 绝不把旧区域的高度贴进新区域。被挡掉的件仍然要送回取件口——缓冲区所有权跟着件走，
/// 否则换一次区就漏一批 1 MB 数组。
/// </para>
/// <para>
/// 对账等式：<c>Submitted = Rejected + QueueDepth + InFlight + Completed + Taken + ReadyDiscarded</c>。
/// 每一件被接受的活最终必然落在其中一格，少一件就会在这行露出来。
/// </para>
/// </summary>
public sealed class PlanetTerrainUploader : IDisposable
{
    private readonly IPatchUploadTarget _target;
    private readonly ConcurrentQueue<Pending> _queue = new();
    private readonly ConcurrentQueue<UploadedPatch> _done = new();
    private readonly AutoResetEvent _wake = new(false);
    private readonly Thread? _worker;
    private readonly int _capacity;

    private int _epoch;
    private int _queued;
    private int _inFlight;
    private int _maxInFlight;
    private int _maxQueueDepth;
    private int _stopRequested;
    private readonly bool _runInline;

    /// <summary>累计量一律 long：长时间跑图后 int 会翻负，对账等式会一次性失真。</summary>
    private long _submitted, _rejected, _uploads, _removes, _failures, _staleDropped, _taken, _readyDiscarded;
    private double _msSum, _msMax;

    /// <param name="runInline">
    /// true = 不建线程，<see cref="TrySubmit"/> 的调用线程自己把那件跑完（串行、单线程、账完全一样的形状）。
    /// 这是 A/B 用的开关：要问"改 patch 的那条线程是不是主线程"，只改这一处，别改队列语义。
    /// </param>
    public PlanetTerrainUploader(IPatchUploadTarget target, int capacity = 4, bool runInline = false)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "至少要能排一件");
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _capacity = capacity;
        _runInline = runInline;
        if (runInline) return;
        // 一条专属线程串行跑：§13.16 封死的是"并发上传"，而实测安全的那条路
        // 正是"同一根线程一件接一件"。线程池会换线程，所以这里不用 Task.Run。
        _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "PlanetTerrainUploader" };
        _worker.Start();
    }

    private readonly struct Pending
    {
        public Pending(PatchKey key, float[]? heights, RegionKey region, bool remove, int epoch)
        {
            Key = key; Heights = heights; Region = region; IsRemove = remove; Epoch = epoch;
        }
        public PatchKey Key { get; }
        public float[]? Heights { get; }
        public RegionKey Region { get; }
        public bool IsRemove { get; }
        public int Epoch { get; }
    }

    public int Capacity => _capacity;
    public long Submitted => Interlocked.Read(ref _submitted);
    public long Rejected => Interlocked.Read(ref _rejected);
    public long Uploads => Interlocked.Read(ref _uploads);
    public long Removes => Interlocked.Read(ref _removes);

    /// <summary>引擎报失败的件数。它是 <see cref="Uploads"/> 的子口径，不参与对账等式的加法。</summary>
    public long Failures => Interlocked.Read(ref _failures);

    /// <summary>没碰引擎就作废的件数（换区排队件 + 开跑前代次已过）。同样是子口径。</summary>
    public long StaleDropped => Interlocked.Read(ref _staleDropped);

    /// <summary>取件方已经取走的件数（等式右边那一格）。</summary>
    public long Taken => Interlocked.Read(ref _taken);

    /// <summary>停在取件口却没被取走就被丢弃（停止时）的件数——这些缓冲区就此还给 GC，不再回池。</summary>
    public long ReadyDiscarded => Interlocked.Read(ref _readyDiscarded);

    public int QueueDepth => Volatile.Read(ref _queued);
    public int InFlightCount => Volatile.Read(ref _inFlight);
    public int CompletedCount => _done.Count;
    public int MaxInFlight => Volatile.Read(ref _maxInFlight);
    public int MaxQueueDepth => Volatile.Read(ref _maxQueueDepth);
    public double TotalMs => Volatile.Read(ref _msSum);
    public double MaxMs => Volatile.Read(ref _msMax);

    /// <summary>
    /// 没有任何件在排、在飞或等取。只有这个状态下主线程才可以直接读引擎的地形
    /// （验收、射线），否则读到的可能是 worker 写到一半的 patch。
    /// </summary>
    public bool IsIdle => QueueDepth == 0 && InFlightCount == 0 && CompletedCount == 0;

    /// <summary>
    /// 投一件。<b>返回 false 时缓冲区仍然归调用方</b>（队满、已停止都算数）——
    /// "投出去才转移所有权"这条线一破，同一份 float[] 就可能被两块 patch 同时用。
    /// </summary>
    public bool TrySubmit(PatchKey key, float[] heights, RegionKey region)
    {
        if (heights is null) throw new ArgumentNullException(nameof(heights));
        if (heights.Length != PlanetPatchSampler.HeightmapLength)
            throw new ArgumentException($"必须是 {PlanetPatchSampler.HeightmapLength} 个高度值", nameof(heights));
        return Submit(new Pending(key, heights, region, remove: false, Volatile.Read(ref _epoch)));
    }

    /// <summary>投一次卸载。卸载没有缓冲区要转移，完成的 <see cref="UploadedPatch.Heights"/> 是 null。</summary>
    public bool TrySubmitRemove(PatchKey key, RegionKey region) =>
        Submit(new Pending(key, null, region, remove: true, Volatile.Read(ref _epoch)));

    private bool Submit(Pending item)
    {
        Interlocked.Increment(ref _submitted);
        if (Volatile.Read(ref _stopRequested) != 0 || QueueDepth >= _capacity)
        {
            Interlocked.Increment(ref _rejected);
            return false;
        }
        _queue.Enqueue(item);
        UpdateMax(ref _maxQueueDepth, Interlocked.Increment(ref _queued));
        Drain();
        return true;
    }

    /// <summary>叫 worker 去看一眼队列。唤醒放在入队之后，所以不会丢件。</summary>
    private void Drain()
    {
        if (Volatile.Read(ref _stopRequested) != 0) return;
        if (_runInline)
        {
            RunQueue();                       // 调用线程就是那条唯一的上传线程
            return;
        }
        _wake.Set();
    }

    private void WorkerLoop()
    {
        while (true)
        {
            _wake.WaitOne();                                  // Dispose 也会 Set 一次，所以不会永远等
            if (Volatile.Read(ref _stopRequested) != 0) break;
            RunQueue();
        }
        // 停止时不再开新的调用；剩下排队的件由 Dispose 按 stale 送回取件口
    }

    private void RunQueue()
    {
        while (Volatile.Read(ref _stopRequested) == 0 && _queue.TryDequeue(out var item))
        {
            Interlocked.Decrement(ref _queued);
            UpdateMax(ref _maxInFlight, Interlocked.Increment(ref _inFlight));
            try { Work(item); }
            finally { Interlocked.Decrement(ref _inFlight); }
        }
    }

    private void Work(Pending item)
    {
        if (Volatile.Read(ref _epoch) != item.Epoch)
        {
            // 代次变了就根本不碰引擎：这是"旧区域的高度不许贴进新区域"唯一的执行点。
            // 但件仍然要送回取件口，不然那块缓冲区就没人还了。
            Interlocked.Increment(ref _staleDropped);
            Deliver(item, failed: false, ms: 0, stale: true);
            return;
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        bool failed = false;
        try
        {
            if (item.IsRemove) _target.Remove(item.Key);
            else failed = _target.Upload(item.Key, item.Heights!);   // TrySubmit 已经拒绝过 null
        }
        catch (Exception)
        {
            failed = true;      // 抛异常也算失败：一件不能弄死 worker，也不能悄悄算成功
        }
        sw.Stop();
        double ms = sw.Elapsed.TotalMilliseconds;

        if (item.IsRemove) Interlocked.Increment(ref _removes);
        else Interlocked.Increment(ref _uploads);
        if (failed) Interlocked.Increment(ref _failures);
        // 这两个只有那一条 worker 会写，主线程只读来看（Interlocked.Add 没有 double 重载）
        Volatile.Write(ref _msSum, Volatile.Read(ref _msSum) + ms);
        if (ms > Volatile.Read(ref _msMax)) Volatile.Write(ref _msMax, ms);

        Deliver(item, failed, ms, stale: false);
    }

    private void Deliver(Pending item, bool failed, double ms, bool stale)
    {
        var result = new UploadedPatch(item.Key, item.Heights, item.Region, item.IsRemove,
                                       failed, ms, stale);
        if (Volatile.Read(ref _stopRequested) != 0)
        {
            Interlocked.Increment(ref _readyDiscarded);
            return;
        }
        _done.Enqueue(result);
    }

    /// <summary>主线程取一件已完成。取出后 <see cref="UploadedPatch.Heights"/> 归调用方，用完要还回池子。</summary>
    public bool TryDequeueCompleted(out UploadedPatch completed)
    {
        if (_done.TryDequeue(out completed))
        {
            Interlocked.Increment(ref _taken);
            return true;
        }
        return false;
    }

    /// <summary>换区域：抬代次（在飞与排队的都不许再碰引擎），把排队的件送回取件口好还缓冲区。</summary>
    public void BeginRegion()
    {
        Interlocked.Increment(ref _epoch);
        DiscardQueue();
        Drain();
    }

    private void DiscardQueue()
    {
        while (_queue.TryDequeue(out var item))
        {
            Interlocked.Decrement(ref _queued);
            Interlocked.Increment(ref _staleDropped);
            Deliver(item, failed: false, ms: 0, stale: true);
        }
    }

    private static void UpdateMax(ref int peak, int value)
    {
        int seen = Volatile.Read(ref peak);
        while (value > seen && Interlocked.CompareExchange(ref peak, value, seen) != seen)
            seen = Volatile.Read(ref peak);
    }

    /// <summary>给日志用的一行回执：等式两边都念出来，少一件都会在这行露出来。</summary>
    public string Describe() => string.Format(CultureInfo.InvariantCulture,
        "uploader mode={0} submitted={1} uploads={2} removes={3} stale={4} failures={5} rejected={6} taken={7} " +
        "ready_discarded={8} queue={9} inflight={10} done={11} max_queue={12} max_inflight={13} capacity={14} " +
        "ms_total={15:0.#} ms_max={16:0.###} equation={17}",
        _runInline ? "inline" : "thread",
        Submitted, Uploads, Removes, StaleDropped, Failures, Rejected, Taken, ReadyDiscarded,
        QueueDepth, InFlightCount, CompletedCount, MaxQueueDepth, MaxInFlight, _capacity, TotalMs, MaxMs,
        Bit(EquationBalanced));

    /// <summary>
    /// <c>Submitted = Rejected + QueueDepth + InFlight + Completed + Taken + ReadyDiscarded</c>。
    /// <see cref="Failures"/> 与 <see cref="StaleDropped"/> 是子口径，不加进来。
    /// </summary>
    public bool EquationBalanced =>
        Submitted == Rejected + QueueDepth + InFlightCount + CompletedCount + Taken + ReadyDiscarded;

    /// <summary>
    /// 停止：不再接新活，等在飞的那件收尾，然后把取件口清掉。
    /// 会阻塞到最后一块上传结束（每块 ~22 ms），只在换区/退出时调用，不要在每帧路径上调。
    /// </summary>
    public void Dispose()
    {
        Volatile.Write(ref _stopRequested, 1);
        _wake.Set();
        _worker?.Join(TimeSpan.FromSeconds(10));      // inline 模式没建线程，这里没有线程可等
        DiscardQueue();
        while (_done.TryDequeue(out _)) Interlocked.Increment(ref _readyDiscarded);
        _wake.Dispose();
    }

    private static string Bit(bool b) => b ? "1" : "0";
}

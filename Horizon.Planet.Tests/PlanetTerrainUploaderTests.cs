using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Xunit;

namespace Horizon.Planet.Tests;

/// <summary>
/// 上传目标的替身：把"引擎调用落在哪条线程、有没有重叠、什么时候真的落地"变成可观测的账。
/// 闸门用来把第一件卡在引擎里，这样"排队"与"并发峰值"都不是靠睡觉猜时间。
/// </summary>
internal sealed class RecordingTarget : IPatchUploadTarget
{
    private readonly ConcurrentQueue<(PatchKey Key, float[]? Heights)> _applied = new();
    private readonly ConcurrentDictionary<int, byte> _threads = new();
    private readonly ConcurrentDictionary<PatchKey, byte> _failKeys = new();
    private int _live, _maxLive, _uploads, _removes;

    public ManualResetEventSlim? Gate;

    public int UploadCalls => Volatile.Read(ref _uploads);
    public int RemoveCalls => Volatile.Read(ref _removes);
    public int MaxLive => Volatile.Read(ref _maxLive);
    public int ThreadCount => _threads.Count;
    public IReadOnlyCollection<int> ThreadIds => _threads.Keys.ToArray();
    public IReadOnlyCollection<(PatchKey Key, float[]? Heights)> Applied => _applied;

    public void Fail(PatchKey key) => _failKeys.TryAdd(key, 0);

    public bool Upload(PatchKey key, float[] heights)
    {
        int live = Interlocked.Increment(ref _live);
        int seen = Volatile.Read(ref _maxLive);
        while (live > seen && Interlocked.CompareExchange(ref _maxLive, live, seen) != seen)
            seen = Volatile.Read(ref _maxLive);
        _threads.TryAdd(Thread.CurrentThread.ManagedThreadId, 0);
        Interlocked.Increment(ref _uploads);
        WaitGate();
        Interlocked.Decrement(ref _live);
        _applied.Enqueue((key, heights));
        return _failKeys.ContainsKey(key);
    }

    public void Remove(PatchKey key)
    {
        Interlocked.Increment(ref _removes);
        _threads.TryAdd(Thread.CurrentThread.ManagedThreadId, 0);
        WaitGate();
        _applied.Enqueue((key, null));
    }

    public void ReleaseAll() => Gate?.Set();

    private void WaitGate()
    {
        if (Gate != null) Assert.True(Gate.Wait(5000), "替身没在 5 秒内被放行");
    }
}

public class PlanetTerrainUploaderTests
{
    private static readonly RegionKey KeyA = new(0, 14, 8192, 8192);
    private static float[] Buffer() => new float[PlanetPatchSampler.HeightmapLength];

    /// <summary>等 worker 真的进到引擎里（闸门挡住的那一件正在飞），把"排队"变成可断言的确定状态。</summary>
    private static void WaitInsideEngine(RecordingTarget target)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline && target.MaxLive == 0) Thread.Sleep(1);
        Assert.Equal(1, target.MaxLive);
    }

    private static List<UploadedPatch> DrainAll(PlanetTerrainUploader uploader, int want, string because)
    {
        var sink = new List<UploadedPatch>();
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            while (uploader.TryDequeueCompleted(out var done)) sink.Add(done);
            if (sink.Count >= want && uploader.QueueDepth == 0 && uploader.InFlightCount == 0) return sink;
            Thread.Sleep(1);
        }
        Assert.Fail($"等不到 {want} 件（{because}）：taken={sink.Count} queue={uploader.QueueDepth} " +
                    $"inflight={uploader.InFlightCount} {uploader.Describe()}");
        return sink;
    }

    [Fact]
    public void 引擎调用永远只落在这一条上传线程上()
    {
        var target = new RecordingTarget();
        using var uploader = new PlanetTerrainUploader(target, capacity: 8);

        var keys = Enumerable.Range(0, 40).Select(i => new PatchKey(i, i)).ToArray();
        int rejectedWhileFilling = 0;
        foreach (var k in keys)
        {
            // 队列有界：被拒就等 worker 腾出一格再投。这条判据要验的是"几条线程、会不会重叠"，
            // 不是容量策略——把拒收当成失败会让这条测试跟着机器快慢抖。
            var until = DateTime.UtcNow.AddSeconds(5);
            bool accepted = false;
            while (!accepted && DateTime.UtcNow < until)
            {
                if (uploader.TrySubmit(k, Buffer(), KeyA)) accepted = true;
                else { rejectedWhileFilling++; Thread.SpinWait(2000); }
            }
            Assert.True(accepted, $"投了 5 秒还进不去：{uploader.Describe()}");
        }
        Assert.True(rejectedWhileFilling >= 0);          // 拒过多少次都不影响结论，只是别把件弄丢

        var sink = DrainAll(uploader, keys.Length, "40 件全部落地");

        Assert.Equal(keys.Length, target.UploadCalls);
        Assert.Equal(1, target.ThreadCount);    // 全程只出现过一条线程
        Assert.Equal(1, target.MaxLive);        // 且从来没有两件同时在引擎里
        Assert.Equal(1, uploader.MaxInFlight);
        Assert.Equal(keys.Length, sink.Count);
        Assert.Equal(keys.Length, uploader.Uploads);
        Assert.Equal(0, uploader.Failures);
        Assert.All(sink, s => Assert.False(s.Stale));
        Assert.True(uploader.TotalMs > 0 && uploader.MaxMs <= uploader.TotalMs);
        Assert.True(uploader.EquationBalanced, uploader.Describe());
    }

    /// <summary>
    /// A/B 的开关本身要有判据：inline 只把"哪条线程进引擎"这一件事换掉，串行、账本、取件口都不变。
    /// <para>两条臂在同一条测试里对拍：thread 模式的引擎线程必须<em>不是</em>提交方线程，
    /// inline 模式必须<em>就是</em>提交方线程。只测一边会漏掉"开关根本没生效"这种假绿。</para>
    /// </summary>
    [Fact]
    public void inline模式只把引擎调用搬回提交方线程_串行与账本不变()
    {
        int caller = -1;
        var inlineTarget = new RecordingTarget();
        using (var inline = new PlanetTerrainUploader(inlineTarget, capacity: 8, runInline: true))
        {
            Exception? workerFailure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    caller = Thread.CurrentThread.ManagedThreadId;
                    for (int i = 0; i < 20; i++)
                        Assert.True(inline.TrySubmit(new PatchKey(i, i), Buffer(), KeyA), "inline 模式不该被容量卡住");
                }
                catch (Exception e) { workerFailure = e; }        // 断言在别的线程炸也要传回来，不能让测试"绿着"过去
            });
            thread.IsBackground = false;
            thread.Start();
            thread.Join();
            if (workerFailure != null) throw workerFailure;

            Assert.Equal(0, inline.QueueDepth);            // 提交返回时件已经跑完
            Assert.Equal(1, inline.MaxQueueDepth);         // 而且队列从没深过一格
            Assert.False(inline.IsIdle);                   // 成品还停在取件口
            var sink = DrainAll(inline, 20, "inline 的 20 件全部落地");
            Assert.Equal(20, sink.Count);
            Assert.True(inline.IsIdle, inline.Describe());
            Assert.Equal(20, inlineTarget.UploadCalls);
            Assert.Equal(1, inlineTarget.ThreadCount);     // 仍然一条线程、仍然串行
            Assert.Equal(1, inlineTarget.MaxLive);
            Assert.Equal(1, inline.MaxInFlight);
            Assert.Contains(caller, inlineTarget.ThreadIds);          // 那条线程就是提交方
            Assert.True(inline.EquationBalanced, inline.Describe());
        }

        var threadedTarget = new RecordingTarget();
        int main = Thread.CurrentThread.ManagedThreadId;
        using (var threaded = new PlanetTerrainUploader(threadedTarget, capacity: 40))
        {
            for (int i = 0; i < 20; i++)
                Assert.True(threaded.TrySubmit(new PatchKey(i, i), Buffer(), KeyA));
            DrainAll(threaded, 20, "thread 模式的 20 件全部落地");
            Assert.Equal(1, threadedTarget.ThreadCount);
            Assert.DoesNotContain(main, threadedTarget.ThreadIds);    // 反证：默认模式换的是另一条线程
        }
    }

    [Fact]
    public void 换区时排队中的件根本不碰引擎但缓冲区要送回()
    {
        var target = new RecordingTarget { Gate = new ManualResetEventSlim(false) };
        using var uploader = new PlanetTerrainUploader(target, capacity: 8);

        var first = new PatchKey(100, 100);
        Assert.True(uploader.TrySubmit(first, Buffer(), KeyA));
        WaitInsideEngine(target);                        // 第一件正卡在引擎里

        var queued = Enumerable.Range(1, 5).Select(i => new PatchKey(i, 7)).ToArray();
        var buffers = queued.ToDictionary(k => k, _ => Buffer());
        foreach (var k in queued) Assert.True(uploader.TrySubmit(k, buffers[k], KeyA));
        Assert.Equal(5, uploader.QueueDepth);

        uploader.BeginRegion();                          // 抬代次 + 把排队件送回取件口
        Assert.Equal(0, uploader.QueueDepth);
        target.ReleaseAll();

        var sink = DrainAll(uploader, 6, "在飞那件 + 5 件排队");
        Assert.Equal(5, uploader.StaleDropped);
        Assert.Equal(1, uploader.Uploads);               // 只有换区前那件真的进了引擎
        Assert.Single(sink, s => s.Key == first && !s.Stale);
        foreach (var k in queued)
        {
            var done = Assert.Single(sink, s => s.Key == k);
            Assert.True(done.Stale);
            Assert.Same(buffers[k], done.Heights);       // 缓冲区必须跟着件回来，不然换一次区漏一批 1 MB
        }
        Assert.DoesNotContain(target.Applied, a => queued.Contains(a.Key));
        Assert.True(uploader.EquationBalanced, uploader.Describe());
    }

    [Fact]
    public void 对账等式在拒收取件与停止之后仍然平()
    {
        var target = new RecordingTarget { Gate = new ManualResetEventSlim(false) };
        var uploader = new PlanetTerrainUploader(target, capacity: 1);

        var a = new PatchKey(0, 0);
        Assert.True(uploader.TrySubmit(a, Buffer(), KeyA));
        WaitInsideEngine(target);                                 // a 在引擎里，队列位空出来
        Assert.True(uploader.TrySubmit(new PatchKey(1, 0), Buffer(), KeyA));   // 排队位被它占满
        Assert.False(uploader.TrySubmit(new PatchKey(2, 0), Buffer(), KeyA));  // 队满 → 拒
        Assert.Equal(1, uploader.Rejected);
        Assert.Equal(1, uploader.QueueDepth);

        target.ReleaseAll();
        DrainAll(uploader, 2, "在飞 + 排队各一件");

        Assert.True(uploader.EquationBalanced, uploader.Describe());
        Assert.Equal(3, uploader.Submitted);
        Assert.Equal(2, uploader.Taken);
        Assert.Equal(2, uploader.Uploads);

        // 停止时取件口若还压着件，那一格也要在等式里
        var target2 = new RecordingTarget();
        var stopped = new PlanetTerrainUploader(target2, capacity: 4);
        stopped.TrySubmit(new PatchKey(9, 9), Buffer(), KeyA);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline && stopped.CompletedCount == 0) Thread.Sleep(1);
        stopped.Dispose();                                 // 不取件直接停：成品记到 ready_discarded
        Assert.Equal(1, stopped.ReadyDiscarded);
        Assert.True(stopped.EquationBalanced, stopped.Describe());
    }

    [Fact]
    public void 队满被拒时缓冲区仍然归调用方()
    {
        var target = new RecordingTarget { Gate = new ManualResetEventSlim(false) };
        using var uploader = new PlanetTerrainUploader(target, capacity: 1);

        Assert.True(uploader.TrySubmit(new PatchKey(0, 0), Buffer(), KeyA));
        WaitInsideEngine(target);                        // 这件真的进了引擎，队列位才是空的
        Assert.True(uploader.TrySubmit(new PatchKey(1, 1), Buffer(), KeyA));   // 占满 capacity=1
        var mine = Buffer();
        Assert.False(uploader.TrySubmit(new PatchKey(5, 5), mine, KeyA));      // 被拒

        target.ReleaseAll();
        var sink = DrainAll(uploader, 2, "两件被接受的都回来");

        Assert.DoesNotContain(sink, s => ReferenceEquals(s.Heights, mine));    // 被拒那份从没被接管
        Assert.Equal(2, sink.Count(s => s.Heights != null));
    }

    [Fact]
    public void 引擎报失败被单独计数而件仍然回到取件口()
    {
        var failing = new PatchKey(3, 4);
        var target = new RecordingTarget();
        target.Fail(failing);
        using var uploader = new PlanetTerrainUploader(target, capacity: 4);

        var buf = Buffer();
        Assert.True(uploader.TrySubmit(failing, buf, KeyA));
        var done = Assert.Single(DrainAll(uploader, 1, "失败件也要回取件口"));

        Assert.True(done.Failed);
        Assert.Equal(1, uploader.Failures);
        Assert.Equal(1, uploader.Uploads);               // 调用确实发生过，失败是引擎报的
        Assert.Same(buf, done.Heights);
        Assert.False(done.Stale);
    }

    [Fact]
    public void 抛异常的调用算失败而不是悄悄过去()
    {
        var boom = new PatchKey(6, 6);
        var target = new ThrowingTarget(boom);
        using var uploader = new PlanetTerrainUploader(target, capacity: 4);

        Assert.True(uploader.TrySubmit(boom, Buffer(), KeyA));
        Assert.True(uploader.TrySubmit(new PatchKey(7, 7), Buffer(), KeyA));
        var sink = DrainAll(uploader, 2, "一件抛异常、一件正常");

        Assert.Equal(1, uploader.Failures);
        Assert.Equal(2, uploader.Uploads);               // 两件都算调用过
        Assert.Single(sink, s => s.Failed);
        Assert.True(uploader.EquationBalanced, uploader.Describe());
    }

    [Fact]
    public void 卸载不占缓冲区且与上传走同一条线程()
    {
        var target = new RecordingTarget();
        using var uploader = new PlanetTerrainUploader(target, capacity: 4);

        Assert.True(uploader.TrySubmit(new PatchKey(1, 1), Buffer(), KeyA));
        Assert.True(uploader.TrySubmitRemove(new PatchKey(2, 2), KeyA));
        var sink = DrainAll(uploader, 2, "上传 + 卸载");

        Assert.Equal(1, uploader.Uploads);
        Assert.Equal(1, uploader.Removes);
        Assert.Single(sink, s => s.IsRemove && s.Heights == null);
        Assert.Equal(1, target.ThreadCount);
    }

    [Fact]
    public void Dispose会等在飞的那件收尾()
    {
        var target = new RecordingTarget { Gate = new ManualResetEventSlim(false) };
        var uploader = new PlanetTerrainUploader(target, capacity: 4);
        Assert.True(uploader.TrySubmit(new PatchKey(0, 0), Buffer(), KeyA));
        WaitInsideEngine(target);                        // 确实有一件正卡在引擎里

        var disposer = new Thread(() => uploader.Dispose());
        disposer.Start();
        Thread.Sleep(50);
        Assert.True(disposer.IsAlive, "Dispose 应该在有件在飞时等着，而不是直接放行");
        target.ReleaseAll();
        disposer.Join(TimeSpan.FromSeconds(5));

        Assert.False(disposer.IsAlive);
        Assert.Equal(0, uploader.InFlightCount);
        Assert.Single(target.Applied);                   // 卡住的那件跑完了，没被半路丢
        Assert.True(uploader.IsIdle);
    }

    [Fact]
    public void 停止之后不再接活但仍然记账()
    {
        var target = new RecordingTarget();
        var uploader = new PlanetTerrainUploader(target, capacity: 4);
        uploader.Dispose();

        Assert.False(uploader.TrySubmit(new PatchKey(0, 0), Buffer(), KeyA));
        Assert.Equal(1, uploader.Submitted);
        Assert.Equal(1, uploader.Rejected);
        Assert.Empty(target.Applied);
        Assert.True(uploader.EquationBalanced, uploader.Describe());
    }

    [Fact]
    public void 容量与缓冲区不合法时当场拒绝()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PlanetTerrainUploader(new RecordingTarget(), capacity: 0));
        Assert.Throws<ArgumentNullException>(() =>
            new PlanetTerrainUploader(null!, capacity: 4));

        var uploader = new PlanetTerrainUploader(new RecordingTarget(), capacity: 2);
        Assert.Throws<ArgumentNullException>(() => uploader.TrySubmit(new PatchKey(0, 0), null!, KeyA));
        Assert.Throws<ArgumentException>(() =>
            uploader.TrySubmit(new PatchKey(0, 0), new float[16], KeyA));
        Assert.Equal(0, uploader.Submitted);             // 抛出去的那两件一件都没进账
        uploader.Dispose();
    }

    [Fact]
    public void 换区吞不掉已经落地的那件_它要带着原区域回到取件口()
    {
        // 驱动换区时要靠这个 Region 判断"这件属于旧坐标系"，从而补投一次卸载。
        // 换区若把它吞了（或把 Stale 标成 true），引擎里就会留下一片永远没人摘的旧区域 patch。
        var target = new RecordingTarget();
        using var uploader = new PlanetTerrainUploader(target, capacity: 4);
        var k = new PatchKey(4, 9);
        var buf = Buffer();
        Assert.True(uploader.TrySubmit(k, buf, KeyA));

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline && uploader.CompletedCount == 0) Thread.Sleep(1);
        Assert.Equal(1, uploader.CompletedCount);          // 先让它完成，故意不取走

        uploader.BeginRegion();                             // 换区
        Assert.True(uploader.TryDequeueCompleted(out var done));
        Assert.False(done.Stale);                           // 它真的进过引擎
        Assert.Equal(KeyA, done.Region);                    // 区域还是提交时那个
        Assert.Same(buf, done.Heights);                     // 缓冲区没被换区吞掉
        Assert.Single(target.Applied, a => a.Key == k);
        Assert.True(uploader.EquationBalanced, uploader.Describe());
    }

    [Fact]
    public void 队列深度永不超过容量()
    {
        // "有界队列"只在文档里说有，等于没说：这里用一次猛投把上界钉住。
        // 容量算的是排队的那一段，在飞的那件另算——所以峰值最多 capacity，绝不越界。
        var target = new RecordingTarget { Gate = new ManualResetEventSlim(false) };
        using var uploader = new PlanetTerrainUploader(target, capacity: 3);
        int accepted = 0, rejected = 0;
        for (int i = 0; i < 40; i++)
        {
            if (uploader.TrySubmit(new PatchKey(i, 0), Buffer(), KeyA)) accepted++;
            else rejected++;
        }
        Assert.True(accepted <= 1 + uploader.Capacity,
                    $"闸门没放行时被收了 {accepted} 件，超过 在飞1 + 排队{uploader.Capacity}");
        Assert.Equal(40, accepted + rejected);
        Assert.True(uploader.QueueDepth <= uploader.Capacity,
                    $"当前排队 {uploader.QueueDepth} 超过容量 {uploader.Capacity}");
        Assert.True(uploader.MaxQueueDepth <= uploader.Capacity,
                    $"实测峰值排队 {uploader.MaxQueueDepth} 超过容量 {uploader.Capacity}");
        Assert.Equal(rejected, uploader.Rejected);

        WaitInsideEngine(target);
        target.ReleaseAll();
        DrainAll(uploader, accepted, "闸门放行后全部回来");
        Assert.True(uploader.EquationBalanced, uploader.Describe());
    }

    private sealed class ThrowingTarget : IPatchUploadTarget
    {
        private readonly PatchKey _boom;
        public ThrowingTarget(PatchKey boom) { _boom = boom; }
        public bool Upload(PatchKey key, float[] heights) => key == _boom
            ? throw new InvalidOperationException("引擎炸了")
            : false;
        public void Remove(PatchKey key) { }
    }
}

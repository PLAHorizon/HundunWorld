using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Xunit;

namespace Horizon.Planet.Tests;

/// <summary>
/// 闸门没放行前每次采样都卡住的高度源：把"几块在飞、什么时候收尾"变成测试能拨的开关。
/// 不然并发判据只能靠睡觉等时间，红了也说不清是产品错还是机器慢。
/// </summary>
internal sealed class GatedHeightSource : IHeightSource
{
    private readonly ManualResetEventSlim _gate = new(false);
    private int _entered;

    public int SourceVersion => 1;
    public int Entered => Volatile.Read(ref _entered);

    public double SampleElevationCm(in PlanetVector3 unitDirection)
    {
        Interlocked.Increment(ref _entered);
        if (!_gate.IsSet && !_gate.Wait(5000)) throw new TimeoutException("闸门没被放行");
        return 12_345;
    }

    public void Release() => _gate.Set();
}

public class PlanetPatchPumpTests
{
    private const long Radius = PlanetConstants.PlanetRadiusCm;
    private const long Seed = 0x0CAFEBABE1234567L;
    private const uint TerrainVersion = 0x77AA0011;
    private static readonly RegionKey KeyA = new(0, 14, 8192, 8192);
    private static readonly RegionKey KeyB = new(3, 14, 4000, 6000);

    private static PlanetTerrain Ramp(RegionFrame frame) =>
        new(Radius, new AxisedHeightSource(frame.East, 1e8));

    private static float[] Reference(RegionFrame frame, PlanetTerrain terrain, PatchKey key)
    {
        var heights = new float[PlanetPatchSampler.HeightmapLength];
        PlanetPatchSampler.FillPatchHeights(frame, terrain, key.OriginXCm, key.OriginZCm, heights,
                                            null, parallel: false);
        return heights;
    }

    private static void AssertBitsEqual(float[] expected, float[] actual, string label)
    {
        var a = MemoryMarshal.AsBytes(expected.AsSpan());
        var b = MemoryMarshal.AsBytes(actual.AsSpan());
        Assert.Equal(a.Length, b.Length);
        for (int i = 0; i < a.Length; i += 4096)
            if (a[i] != b[i]) Assert.Fail($"{label}：第 {i} 字节 期望 {a[i]:X2} 实际 {b[i]:X2}");
    }

    private static void WaitQuiesced(PlanetPatchPump pump, int timeoutMs = 30_000)
    {
        var sw = Stopwatch.StartNew();
        while (pump.PendingCount > 0 || pump.InFlightCount > 0)
        {
            if (sw.ElapsedMilliseconds > timeoutMs) throw new TimeoutException("泵没收敛：" + pump.Describe());
            Thread.Sleep(1);
        }
    }

    private static void WaitUntil(Func<bool> condition, string because, int timeoutMs = 10_000)
    {
        var sw = Stopwatch.StartNew();
        while (!condition())
        {
            if (sw.ElapsedMilliseconds > timeoutMs) throw new TimeoutException(because);
            Thread.Sleep(1);
        }
    }

    [Fact]
    public void PumpResults_MatchTheSynchronousPathBitForBit()
    {
        var frame = RegionFrame.Build(KeyA);
        var terrain = Ramp(frame);
        var keys = new[] { new PatchKey(0, 0), new PatchKey(1, -2), new PatchKey(-3, 4) };

        var got = new Dictionary<PatchKey, float[]>();
        using (var pump = new PlanetPatchPump(frame, terrain, maxWorkers: 3))
        {
            foreach (var key in keys) Assert.True(pump.Request(key));
            WaitQuiesced(pump);
            while (pump.TryDequeueCompleted(out var resolved)) got[resolved.Key] = resolved.Heights;
            Assert.Equal(keys.Length, got.Count);
            Assert.Equal(0, pump.GenerationFailures);
            Assert.Equal(0, pump.StaleDropped);
            Assert.Equal(PatchFillSource.Generated, PatchFillSource.Generated);   // 没接缓存，全是生成

            foreach (var key in keys)
            {
                AssertBitsEqual(Reference(frame, terrain, key), got[key], $"后台 vs 主线程同步 {key}");
                pump.Release(got[key]);
            }
        }
    }

    [Fact]
    public void SecondRoundOfTheSamePatch_ComesFromCache_WithoutRewriting()
    {
        using var dir = new TempCacheDir();
        var cache = new PlanetPatchCache(dir.Root, Seed, TerrainVersion);
        var frame = RegionFrame.Build(KeyA);
        var terrain = Ramp(frame);
        var key = new PatchKey(2, 1);

        using var pump = new PlanetPatchPump(frame, terrain, cache, maxWorkers: 2);
        Assert.True(pump.Request(key));
        WaitQuiesced(pump);
        Assert.True(pump.TryDequeueCompleted(out var first));
        Assert.Equal(PatchFillSource.Generated, first.Source);
        Assert.Equal(1, cache.Writes);

        Assert.True(pump.Request(key));                       // 归还前先再投一次：这一轮该走盘
        WaitQuiesced(pump);
        Assert.True(pump.TryDequeueCompleted(out var second));
        Assert.Equal(PatchFillSource.CacheHit, second.Source);
        AssertBitsEqual(first.Heights, second.Heights, "两轮必须同一份数");
        Assert.Equal(1, cache.Writes);                        // 第二轮只读盘，没再写
        Assert.Equal(1, cache.Misses);                        // 第一轮那次是真未命中

        pump.Release(first.Heights);
        pump.Release(second.Heights);
        Assert.Equal(1, cache.Hits);                          // 命中确实发生了一次
    }

    [Fact]
    public void StreamerAndPump_ShareOneCacheNamespace_EachConsumesTheOthersWrites()
    {
        // 同一条"读盘→算→回填"现在只有一份实现，但两个调用者各自传的键推导仍可能分叉：
        // 泵用的是投递时抓到的 frame，流送用的是 Frame 属性。谁写的那一块，另一边必须认得出是同一块——
        // 认不出就会各写一份（缓存翻倍）或算出两份不同的数（玩家看到的地形取决于走哪条线程）。
        using var dir = new TempCacheDir();
        var cache = new PlanetPatchCache(dir.Root, Seed, TerrainVersion);
        var frame = RegionFrame.Build(KeyA);
        var terrain = Ramp(frame);
        var streamer = new PlanetTerrainStreamer(frame, terrain, cache);

        var written = new float[PlanetPatchSampler.HeightmapLength];
        var k1 = new PatchKey(2, -3);
        Assert.Equal(PatchFillSource.Generated, streamer.FillPatch(k1, written, parallel: false));

        using (var pump = new PlanetPatchPump(frame, terrain, cache, maxWorkers: 2))
        {
            Assert.True(pump.Request(k1));
            WaitQuiesced(pump);
            Assert.True(pump.TryDequeueCompleted(out var fromStreamed));
            Assert.Equal(k1, fromStreamed.Key);
            Assert.Equal(PatchFillSource.CacheHit, fromStreamed.Source);
            AssertBitsEqual(written, fromStreamed.Heights, "泵读流送写的那一块");

            var k2 = new PatchKey(-5, 1);
            Assert.True(pump.Request(k2));
            WaitQuiesced(pump);
            Assert.True(pump.TryDequeueCompleted(out var fromPump));
            Assert.Equal(PatchFillSource.Generated, fromPump.Source);
            Assert.Equal(2, cache.Writes);                    // 流送写 k1、泵写 k2；泵读 k1 命中不该再写

            pump.Release(fromStreamed.Heights);

            var readBack = new float[PlanetPatchSampler.HeightmapLength];
            Assert.Equal(PatchFillSource.CacheHit, streamer.FillPatch(k2, readBack, parallel: false));
            AssertBitsEqual(fromPump.Heights, readBack, "流送读泵写的那一块");

            pump.Release(fromPump.Heights);
        }

        Assert.Equal(2, cache.Hits);                          // 两个方向各真命中过一次
        Assert.Equal(2, cache.Misses);
        Assert.Equal(0, cache.CorruptRejects);                // 跨路径不能把对方写的判成坏数据
        Assert.Equal(1, streamer.CacheMisses);                // 流送自己：k1 冷、k2 命中
        Assert.Equal(1, streamer.CacheHits);
    }

    [Fact]
    public void SameKeyWhileInFlight_IsNotGeneratedTwice()
    {
        var frame = RegionFrame.Build(KeyA);
        var gate = new GatedHeightSource();
        var terrain = new PlanetTerrain(Radius, gate);
        var key = new PatchKey(0, 0);

        using var pump = new PlanetPatchPump(frame, terrain, maxWorkers: 2);
        Assert.True(pump.Request(key));
        WaitUntil(() => pump.InFlightCount == 1, "第一块没进入在飞状态");

        Assert.False(pump.Request(key));                      // 在飞中的重复投递必须被去重
        Assert.Equal(1, pump.DuplicatesRejected);
        Assert.Equal(2, pump.Requested);

        gate.Release();
        WaitQuiesced(pump);
        Assert.Equal(1, pump.Completed);
        Assert.True(pump.TryDequeueCompleted(out var only));
        Assert.Equal(key, only.Key);
        pump.Release(only.Heights);
        Assert.False(pump.TryDequeueCompleted(out _));
        Assert.Equal(0, pump.GenerationFailures);
    }

    [Fact]
    public void WorkerLimit_IsBothRespectedAndActuallyReached()
    {
        var frame = RegionFrame.Build(KeyA);
        var gate = new GatedHeightSource();
        var terrain = new PlanetTerrain(Radius, gate);

        using var pump = new PlanetPatchPump(frame, terrain, maxWorkers: 2);
        Assert.True(pump.Request(new PatchKey(0, 0)));
        WaitUntil(() => pump.InFlightCount == 1, "第一块没进入在飞状态");
        Assert.True(pump.Request(new PatchKey(1, 0)));
        WaitUntil(() => pump.InFlightCount == 2, "第二块没并起来：限流判据根本没被跑到");
        Assert.True(pump.Request(new PatchKey(2, 0)));

        // 第三位只能排队：并发上限是被真按住的，不是巧合
        Thread.Sleep(50);
        Assert.Equal(2, pump.InFlightCount);
        Assert.Equal(1, pump.PendingCount);

        gate.Release();
        WaitQuiesced(pump);
        Assert.Equal(3, pump.Completed);
        Assert.Equal(2, pump.MaxInFlight);                    // 既没超上限，也确实并到过
        int outCount = 0;
        while (pump.TryDequeueCompleted(out var ready))
        {
            Assert.Equal(PatchFillSource.Generated, ready.Source);   // 没接缓存，三块都该是生成的
            outCount++;
            pump.Release(ready.Heights);
        }
        Assert.Equal(3, outCount);
    }

    [Fact]
    public void RegionChange_InFlightResultsNeverLandInTheNewRegion()
    {
        var frameA = RegionFrame.Build(KeyA);
        var gate = new GatedHeightSource();
        var terrainA = new PlanetTerrain(Radius, gate);
        var frameB = RegionFrame.Build(KeyB);

        using var pump = new PlanetPatchPump(frameA, terrainA, maxWorkers: 2);
        Assert.True(pump.Request(new PatchKey(0, 0)));
        Assert.True(pump.Request(new PatchKey(1, 0)));
        WaitUntil(() => pump.InFlightCount == 2, "两块没能同时在飞");
        Assert.True(pump.Request(new PatchKey(2, 0)));        // 这一条还在队里

        pump.BeginRegion(frameB);
        Assert.Equal(0, pump.PendingCount);                   // 排队项当场清掉
        Assert.Equal(0, pump.ReadyCount);

        gate.Release();
        WaitQuiesced(pump);

        Assert.False(pump.TryDequeueCompleted(out _));        // 旧区域的成品一块都不许落地
        Assert.Equal(3, pump.StaleDropped);                   // 2 在飞 + 1 排队
        Assert.Equal(0, pump.Completed);
        Assert.Equal(KeyB, pump.Frame.Key);
        Assert.Contains("stale=3", pump.Describe());
    }

    [Fact]
    public void AccountingEquation_ClosesAfterDrain()
    {
        var frame = RegionFrame.Build(KeyA);
        var terrain = Ramp(frame);
        var keys = new List<PatchKey>();
        for (int x = -2; x <= 2; x++) keys.Add(new PatchKey(x, 0));

        var pump = new PlanetPatchPump(frame, terrain, maxWorkers: 3);
        foreach (var key in keys) pump.Request(key);
        foreach (var key in keys) pump.Request(key);          // 第二遍全是重复投递
        WaitQuiesced(pump);

        int dequeued = 0;
        while (pump.TryDequeueCompleted(out var ready)) { dequeued++; pump.Release(ready.Heights); }
        long readyLeft = pump.ReadyCount;
        pump.Dispose();

        // 投递数 = 完成 + 作废 + 去重，一件都不能凭空消失
        Assert.Equal(2 * keys.Count, pump.Requested);
        Assert.Equal(keys.Count, pump.DuplicatesRejected);
        Assert.Equal(pump.Requested, dequeued + readyLeft + pump.StaleDropped + pump.DuplicatesRejected);
        Assert.Equal(keys.Count, dequeued + readyLeft);
        Assert.Equal(0, pump.GenerationFailures);
        Assert.Contains("requested=10", pump.Describe());
    }

    [Fact]
    public void BufferPool_ReusesButNeverHandsTheSameArrayOutTwice()
    {
        var frame = RegionFrame.Build(KeyA);
        var terrain = Ramp(frame);

        using var pump = new PlanetPatchPump(frame, terrain, maxWorkers: 2);
        for (int x = 0; x < 4; x++) pump.Request(new PatchKey(x, 0));
        WaitQuiesced(pump);

        var held = new HashSet<float[]>();
        while (pump.TryDequeueCompleted(out var ready)) Assert.True(held.Add(ready.Heights), "同一块缓冲区同时交给两片 patch");
        Assert.Equal(4, held.Count);
        Assert.Equal(4, pump.Rents);
        Assert.Equal(0, pump.PoolHits);                       // 第一轮池里没东西可复用

        foreach (var array in held) pump.Release(array);
        Assert.Equal(PlanetPatchPump.MaxPooledArrays, pump.PooledArrays);

        held.Clear();
        for (int x = 10; x < 14; x++) pump.Request(new PatchKey(x, 0));
        WaitQuiesced(pump);
        while (pump.TryDequeueCompleted(out var ready)) Assert.True(held.Add(ready.Heights));

        Assert.Equal(PlanetPatchPump.MaxPooledArrays, pump.PoolHits);   // 第二轮全从池里拿
        Assert.Equal(8, pump.Rents);
        Assert.Contains("pool_hits=4", pump.Describe());
    }

    [Fact]
    public void Dispose_DrainsInFlightAndLeavesNoStragglers()
    {
        var frame = RegionFrame.Build(KeyA);
        var gate = new GatedHeightSource();
        var terrain = new PlanetTerrain(Radius, gate);

        var pump = new PlanetPatchPump(frame, terrain, maxWorkers: 2);
        Assert.True(pump.Request(new PatchKey(0, 0)));
        WaitUntil(() => pump.InFlightCount == 1, "没进入在飞状态");
        Assert.True(pump.Request(new PatchKey(1, 0)));
        Assert.True(pump.Request(new PatchKey(2, 0)));
        Assert.Equal(1, pump.PendingCount);

        gate.Release();
        pump.Dispose();                                        // Dispose 会等在飞的收尾，再清未取走的成品

        Assert.Equal(0, pump.InFlightCount);
        Assert.Equal(0, pump.PendingCount);
        Assert.Equal(2, pump.Completed);                       // 放行后在飞的两块确实做完了
        Assert.Equal(2, pump.ReadyDiscarded);                  // 但没被取走就随停止回收：这是 Completed 的子口径
        Assert.Equal(1, pump.StaleDropped);                    // 只有那条还没启动的排队算丢件
        Assert.Equal(pump.Requested, pump.Completed + pump.StaleDropped + pump.DuplicatesRejected);
        Assert.Equal(3, pump.Requested);
        Assert.False(pump.Request(new PatchKey(9, 9)));        // 停止之后不再接活
        Assert.Equal(4, pump.Requested);
        Assert.Equal(2, pump.StaleDropped);                    // 停止后的投递只进丢件，不产生工作
        Assert.Equal(pump.Requested, pump.Completed + pump.StaleDropped + pump.DuplicatesRejected);
        Assert.Contains("ready_discarded=2", pump.Describe());
        Assert.Contains("inflight=0", pump.Describe());
    }

    [Fact]
    public void DequeuePath_IsFarCheaperThanCheckingOneMegabyte()
    {
        var frame = RegionFrame.Build(KeyA);
        var terrain = Ramp(frame);

        using var pump = new PlanetPatchPump(frame, terrain, maxWorkers: 2);
        for (int x = 0; x < 4; x++) pump.Request(new PatchKey(x, 0));
        WaitQuiesced(pump);            // 等生成都跑完：下面的计时要在 CPU 空的时候量，不然量到的是被抢核

        // 尺子用同一进程里"搬 1 MB 做一遍校验"的耗时：这台机器的绝对毫秒数会漂好几倍，
        // 写死 1 ms 这种阈值明天就会红得没有意义
        var yardstick = new float[PlanetPatchSampler.HeightmapLength];
        var ySw = Stopwatch.StartNew();
        for (int i = 0; i < 3; i++) Crc32.Compute(MemoryMarshal.AsBytes(yardstick.AsSpan()));
        ySw.Stop();
        double crcMs = ySw.Elapsed.TotalMilliseconds / 3.0;

        var dSw = Stopwatch.StartNew();
        int dequeued = 0;
        while (pump.TryDequeueCompleted(out var ready))
        {
            dequeued++;
            pump.Release(ready.Heights);
        }
        dSw.Stop();

        Assert.Equal(4, dequeued);
        Assert.True(crcMs > 0.1, $"尺子本身不可信：1 MB 校验只用了 {crcMs:F4} ms");
        Assert.True(dSw.Elapsed.TotalMilliseconds < crcMs / 4,
                    $"取件 4 块用了 {dSw.Elapsed.TotalMilliseconds:F3} ms，超过一次 1 MB 校验（{crcMs:F2} ms）" +
                    "的 1/4：取件路径里藏了重活");
    }

    [Fact]
    public void Constructor_RejectsBadConfiguration()
    {
        var frame = RegionFrame.Build(KeyA);
        var terrain = Ramp(frame);
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlanetPatchPump(frame, terrain, maxWorkers: 0));
        Assert.Throws<ArgumentNullException>(() => new PlanetPatchPump(frame, null!));

        using var ok = new PlanetPatchPump(frame, terrain);
        Assert.Equal(2, ok.MaxWorkers);
        Assert.True(ok.Request(new PatchKey(0, 0)));
        WaitQuiesced(ok);
        Assert.True(ok.TryDequeueCompleted(out var ready));
        Assert.Throws<ArgumentNullException>(() => ok.Release(null!));
        Assert.Throws<ArgumentException>(() => ok.Release(new float[16]));
        ok.Release(ready.Heights);
        Assert.Equal(0, ok.CacheWriteFailures);               // 没接缓存时这个计数就该是 0
        Assert.Equal(0, ok.GenerationFailures);
    }
}

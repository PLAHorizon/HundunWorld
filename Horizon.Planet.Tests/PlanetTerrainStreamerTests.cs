using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Xunit;

namespace Horizon.Planet.Tests;

public class PlanetTerrainStreamerTests
{
    private const long Seed = 0x1122334455667788L;
    private const uint TerrainVersion = 0x5A5A1234;
    private const int Patch = PlanetConstants.TerrainPatchSizeCm;     // 50,800 cm = 508 m

    private static readonly RegionKey KeyA = new(0, 14, 8192, 8192);
    private static readonly RegionKey KeyB = new(3, 14, 4000, 6000);

    /// <summary>东向缓坡：patch 内每个顶点都不一样，patch 之间也不同——"缓存读回的是这一块"才不是恒等式。</summary>
    private static PlanetTerrain RampTerrain(RegionFrame frame) =>
        new(PlanetConstants.PlanetRadiusCm, new AxisedHeightSource(frame.East, 1e8));

    private static float[] ReferenceHeights(RegionFrame frame, PlanetTerrain terrain, int patchX, int patchZ)
    {
        var heights = new float[PlanetPatchSampler.HeightmapLength];
        PlanetPatchSampler.FillPatchHeights(frame, terrain, (double)patchX * Patch, (double)patchZ * Patch,
                                            heights, null, parallel: false);
        return heights;
    }

    private static void AssertBitsEqual(float[] expected, float[] actual, string label)
    {
        var a = MemoryMarshal.AsBytes(expected.AsSpan());
        var b = MemoryMarshal.AsBytes(actual.AsSpan());
        Assert.Equal(a.Length, b.Length);
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i])
                Assert.Fail($"{label}：第 {i} 字节 期望 {a[i]:X2} 实际 {b[i]:X2}");
        }
    }

    private static PatchKey[] MarkWindowReady(PlanetTerrainStreamer streamer, double x, double z, float[] scratch)
    {
        var loaded = new List<PatchKey>();
        for (int guard = 0; guard < 100; guard++)
        {
            var plan = streamer.Tick(x, z, scratch);
            loaded.AddRange(plan.ToLoad);
            if (plan.IsSatisfied) return loaded.ToArray();
        }
        throw new InvalidOperationException("窗口在 100 轮内没收敛");
    }

    [Fact]
    public void Window_IsFiveByFiveCentredOnThePlayer()
    {
        var frame = RegionFrame.Build(KeyA);
        var streamer = new PlanetTerrainStreamer(frame, RampTerrain(frame));
        Assert.Equal(PlanetConstants.NearPatchWindowEdge, streamer.WindowEdge);
        Assert.Equal(5, streamer.WindowEdge);

        var plan = streamer.BuildPlan(0, 0);
        Assert.Equal(25, plan.DesiredPatches.Count);
        Assert.Equal(25, plan.DesiredPatches.Distinct().Count());
        Assert.Equal(0, plan.CenterPatchX);
        Assert.Equal(0, plan.CenterPatchZ);
        Assert.False(plan.CenterChanged);
        Assert.Equal(0, plan.ReadyInWindow);

        foreach (var key in plan.DesiredPatches)
        {
            Assert.InRange(key.X, -2, 2);
            Assert.InRange(key.Z, -2, 2);
        }
        Assert.Contains(plan.DesiredPatches, k => k == new PatchKey(-2, 2));
        Assert.Contains(plan.DesiredPatches, k => k == new PatchKey(2, -2));
        Assert.DoesNotContain(plan.DesiredPatches, k => k.X == 3);
    }

    [Fact]
    public void PatchOf_UsesFloor_AtZeroAtNegativesAndAcrossBoundaries()
    {
        var frame = RegionFrame.Build(KeyA);
        var streamer = new PlanetTerrainStreamer(frame, RampTerrain(frame));

        Assert.Equal(new PatchKey(0, 0), streamer.PatchOf(0, 0));
        Assert.Equal(new PatchKey(0, 0), streamer.PatchOf(Patch - 1, Patch - 0.5));
        Assert.Equal(new PatchKey(1, 1), streamer.PatchOf(Patch, Patch));                 // 边界归右边那块
        Assert.Equal(new PatchKey(-1, -1), streamer.PatchOf(-1, -0.5));
        Assert.Equal(new PatchKey(-1, -1), streamer.PatchOf(-Patch, -Patch));
        Assert.Equal(new PatchKey(-2, -2), streamer.PatchOf(-Patch - 1, -Patch - 1));
        Assert.Equal(new PatchKey(3, -5), streamer.PatchOf(3.5 * Patch, -4.5 * Patch));

        Assert.Throws<ArgumentOutOfRangeException>(() => streamer.PatchOf(double.NaN, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => streamer.PatchOf(0, double.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => streamer.PatchOf(PlanetTerrainStreamer.MaxLocalCoordinateCm + 1, 0));
        // 正向对照：恰好等于上限不拒（"该换区了"是区域迁移流程的判断，不是本类的）
        Assert.Equal(new PatchKey(196, 0), streamer.PatchOf(PlanetTerrainStreamer.MaxLocalCoordinateCm, 0));
    }

    [Fact]
    public void BoundaryCrossing_SwapsExactlyOneColumnOfFive()
    {
        var frame = RegionFrame.Build(KeyA);
        var streamer = new PlanetTerrainStreamer(frame, RampTerrain(frame), maxLoadsPerPlan: 25);
        var scratch = new float[PlanetPatchSampler.HeightmapLength];

        // 先让窗口在中心 patch = 2 上就绪（离边界远，不沾滞后的边）
        double inside2 = 2.5 * Patch;
        Assert.Equal(2, streamer.BuildPlan(inside2, 0).CenterPatchX);
        MarkWindowReady(streamer, inside2, 0, scratch);
        Assert.Equal(25, streamer.ResidentCount);
        var before = streamer.BuildPlan(inside2, 0).DesiredPatches.ToArray();
        Assert.Empty(streamer.BuildPlan(inside2, 0).ToLoad);

        // 走到下一格的正中：越过 3 号格边界再走 127 m 滞后 → 中心 2→3
        var plan = streamer.BuildPlan(3.5 * Patch, 0);
        Assert.True(plan.CenterChanged);
        Assert.Equal(3, plan.CenterPatchX);
        Assert.Equal(5, plan.ToUnload.Count);
        Assert.Equal(5, plan.ToLoad.Count);
        Assert.Equal(0, plan.PendingBeyondBudget);
        Assert.All(plan.ToUnload, k => Assert.Equal(0, k.X));       // 出窗的是最左边那一列
        Assert.All(plan.ToLoad, k => Assert.Equal(5, k.X));         // 新进的是最右边那一列
        Assert.Equal(25, plan.DesiredPatches.Count);
        Assert.Equal(20, plan.ReadyInWindow);

        var after = plan.DesiredPatches.ToHashSet();
        Assert.Equal(20, before.Intersect(after).Count());
        Assert.Equal(5, before.Except(after).Count());
        Assert.Equal(5, after.Except(before).Count());
    }

    [Fact]
    public void Hysteresis_DeadBandHoldsTheWindowThenMovesAtTheMargin()
    {
        var frame = RegionFrame.Build(KeyA);
        int h = PlanetConstants.PatchWindowHysteresisCm;
        var streamer = new PlanetTerrainStreamer(frame, RampTerrain(frame));
        Assert.Equal(Patch / 4, h);

        var initial = streamer.BuildPlan(0.5 * Patch, 0);
        Assert.Equal(0, initial.CenterPatchX);

        // 越过 1 号格边界但没走完滞后余量：窗口不动（玩家已在相邻格里，靠 5×5 的余量罩住）
        var justBefore = streamer.BuildPlan(Patch + h - 1, 0);
        Assert.False(justBefore.CenterChanged);
        Assert.Equal(0, justBefore.CenterPatchX);
        Assert.Equal(initial.DesiredPatches.ToArray(), justBefore.DesiredPatches.ToArray());

        // 走到余量末端：动一格
        var atMargin = streamer.BuildPlan(Patch + h, 0);
        Assert.True(atMargin.CenterChanged);
        Assert.Equal(1, atMargin.CenterPatchX);
        Assert.DoesNotContain(new PatchKey(-2, 0), atMargin.DesiredPatches);   // 确实整体右移了一格
        Assert.Contains(new PatchKey(3, 0), atMargin.DesiredPatches);

        // 往回走同样要越过余量：停在 1 号格里不够远就还是不回中心
        var backBefore = streamer.BuildPlan(Patch - h + 1, 0);
        Assert.False(backBefore.CenterChanged);
        Assert.Equal(1, backBefore.CenterPatchX);
        var backAtMargin = streamer.BuildPlan(Patch - h, 0);
        Assert.Equal(0, backAtMargin.CenterPatchX);
    }

    [Fact]
    public void JitterAcrossABoundary_DoesNotChurnTheWindow()
    {
        var frame = RegionFrame.Build(KeyA);
        var streamer = new PlanetTerrainStreamer(frame, RampTerrain(frame), maxLoadsPerPlan: 25);
        var scratch = new float[PlanetPatchSampler.HeightmapLength];
        MarkWindowReady(streamer, 0, 0, scratch);

        int moves = 0;
        for (int i = 0; i < 20; i++)
        {
            // 在 1 号格边界两侧各抖 10 m 反复走
            var plan = streamer.BuildPlan(Patch + (i % 2 == 0 ? -10_000 : 10_000), 0);
            if (plan.CenterChanged) moves++;
        }
        Assert.Equal(0, moves);                 // 滞后带 ±127 m 完全罩住了抖动
        Assert.Equal(25, streamer.ResidentCount);

        // 真的走满余量才该动：证明上面的"不动"是滞后拦的，不是调度器根本没在看位置
        var moved = streamer.BuildPlan(Patch + PlanetConstants.PatchWindowHysteresisCm, 0);
        Assert.True(moved.CenterChanged);
        Assert.Equal(5, moved.ToLoad.Count);
    }

    [Fact]
    public void Teleport_ReCentresInOnePlan()
    {
        var frame = RegionFrame.Build(KeyA);
        var streamer = new PlanetTerrainStreamer(frame, RampTerrain(frame));
        Assert.Equal(0, streamer.BuildPlan(0, 0).CenterPatchX);

        var plan = streamer.BuildPlan(10.5 * Patch, -6.5 * Patch);
        Assert.Equal(10, plan.CenterPatchX);
        Assert.Equal(-7, plan.CenterPatchZ);
        Assert.True(plan.CenterChanged);
        Assert.Equal(4, plan.ToLoad.Count);         // 新窗口一块都没有，按预算分批
        Assert.Equal(21, plan.PendingBeyondBudget);
    }

    [Fact]
    public void Budget_ConvergesTo25Patches()
    {
        var frame = RegionFrame.Build(KeyA);
        var streamer = new PlanetTerrainStreamer(frame, RampTerrain(frame), maxLoadsPerPlan: 4);
        var scratch = new float[PlanetPatchSampler.HeightmapLength];

        var all = new List<PatchKey>();
        var readySeen = new List<int>();
        var loadCounts = new List<int>();
        int ticks = 0;
        PatchWindowPlan last = null!;
        for (; ticks < 100; ticks++)
        {
            last = streamer.Tick(0, 0, scratch);
            all.AddRange(last.ToLoad);
            readySeen.Add(last.ReadyInWindow);
            loadCounts.Add(last.ToLoad.Count);
            if (last.IsSatisfied) break;
        }

        Assert.True(last.IsSatisfied);
        Assert.Equal(8, ticks + 1);       // 7 帧装满 25 块，第 8 帧的计划才报"满足"
        Assert.Equal(new[] { 4, 4, 4, 4, 4, 4, 1, 0 }, loadCounts.ToArray());
        Assert.Equal(new[] { 0, 4, 8, 12, 16, 20, 24, 25 }, readySeen.ToArray());
        Assert.Equal(25, all.Count);
        Assert.Equal(25, all.Distinct().Count());
        Assert.Equal(25, streamer.ResidentCount);
        Assert.Equal(25, last.DesiredPatches.Count);
        Assert.Contains("desired=25", last.Describe());
        Assert.Contains("budget=4", last.Describe());

        // 已经就绪的窗口下一帧不该再生成任何东西
        var idle = streamer.Tick(0, 0, scratch);
        Assert.Empty(idle.ToLoad);
        Assert.Empty(idle.ToUnload);
        Assert.Equal(0, idle.PendingBeyondBudget);
    }

    [Fact]
    public void LoadOrder_IsNearestFirstAndIdenticalAcrossRuns()
    {
        var frame = RegionFrame.Build(KeyA);
        var scratch = new float[PlanetPatchSampler.HeightmapLength];

        var first = new PlanetTerrainStreamer(frame, RampTerrain(frame), maxLoadsPerPlan: 25);
        var planA = first.BuildPlan(0, 0);
        var planB = new PlanetTerrainStreamer(frame, RampTerrain(frame), maxLoadsPerPlan: 25).BuildPlan(0, 0);
        Assert.Equal(planA.ToLoad.ToArray(), planB.ToLoad.ToArray());    // 同一位置两次计划必须同序

        Assert.Equal(new PatchKey(0, 0), planA.ToLoad[0]);
        var rings = planA.ToLoad.Select(k => Math.Max(Math.Abs(k.X), Math.Abs(k.Z))).ToArray();
        for (int i = 1; i < rings.Length; i++)
            Assert.True(rings[i] >= rings[i - 1], $"第 {i} 项 {planA.ToLoad[i]} 的环号 {rings[i]} 比前一项小");

        // 前 9 块必须正好是中心 + 第一环的 8 块
        var nearest9 = planA.ToLoad.Take(9).ToHashSet();
        Assert.Equal(9, nearest9.Count);
        for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
                Assert.Contains(new PatchKey(dx, dz), nearest9);

        var loaded = MarkWindowReady(new PlanetTerrainStreamer(frame, RampTerrain(frame)), 0, 0, scratch);
        Assert.Equal(25, loaded.Length);
    }

    [Fact]
    public void EngineDroppedPatch_IsLoadedAgainOnlyAfterBeingTold()
    {
        var frame = RegionFrame.Build(KeyA);
        var streamer = new PlanetTerrainStreamer(frame, RampTerrain(frame), maxLoadsPerPlan: 25);
        var scratch = new float[PlanetPatchSampler.HeightmapLength];
        MarkWindowReady(streamer, 0, 0, scratch);

        var untouched = streamer.BuildPlan(0, 0);
        Assert.Empty(untouched.ToLoad);                       // 正向对照：调度器认为 25 块都在
        Assert.Equal(25, untouched.ReadyInWindow);

        streamer.MarkUnloaded(new PatchKey(1, -1));
        Assert.Equal(24, streamer.ResidentCount);
        var after = streamer.BuildPlan(0, 0);
        Assert.Equal(new[] { new PatchKey(1, -1) }, after.ToLoad.ToArray());
        Assert.Equal(24, after.ReadyInWindow);
        Assert.True(streamer.IsReady(new PatchKey(0, 0)));
        Assert.False(streamer.IsReady(new PatchKey(1, -1)));
    }

    [Fact]
    public void CacheHit_IsBitIdenticalToRecompute()
    {
        using var dir = new TempCacheDir();
        var cache = new PlanetPatchCache(dir.Root, Seed, TerrainVersion);
        var frame = RegionFrame.Build(KeyA);
        var terrain = RampTerrain(frame);
        var streamer = new PlanetTerrainStreamer(frame, terrain, cache);
        var key = new PatchKey(1, 2);

        var generated = new float[PlanetPatchSampler.HeightmapLength];
        Assert.Equal(PatchFillSource.Generated, streamer.FillPatch(key, generated));
        Assert.Equal(1, streamer.CacheMisses);
        Assert.Equal(0, streamer.CacheHits);

        // 参照：串行直接生成（FillPatch 走的是并行路径，两者必须与缓存同为一份数）
        var reference = ReferenceHeights(frame, terrain, key.X, key.Z);
        AssertBitsEqual(reference, generated, "streamer 生成 vs 直接生成");

        var cached = new float[PlanetPatchSampler.HeightmapLength];
        Assert.True(cache.TryRead(KeyA.ToId(), key.X, key.Z, cached, out var readReason));
        Assert.Equal(PatchCacheReject.None, readReason);
        AssertBitsEqual(reference, cached, "缓存 vs 重算");
        Assert.Equal(1, cache.Writes);
        Assert.Equal(1, cache.Hits);

        Array.Fill(cached, float.NaN);
        Assert.Equal(PatchFillSource.CacheHit, streamer.FillPatch(key, cached));
        AssertBitsEqual(reference, cached, "第二次走命中");
        Assert.Equal(1, streamer.CacheHits);
        Assert.Equal(1, streamer.CacheMisses);
        Assert.Equal(2, cache.Hits);       // 直读一次 + 经 streamer 一次

        // 反证恒等：相邻 patch 的内容必须不同，否则上面那几条比较可以只是同一份缓冲区自比
        var other = new float[PlanetPatchSampler.HeightmapLength];
        streamer.FillPatch(new PatchKey(1, 3), other);
        var crcCached = Crc32.Compute(MemoryMarshal.AsBytes(cached.AsSpan()));
        var crcOther = Crc32.Compute(MemoryMarshal.AsBytes(other.AsSpan()));
        Assert.NotEqual(crcCached, crcOther);
        Assert.NotEqual(0u, crcCached);
    }

    [Fact]
    public void StreamerWorksWithoutCache_AndTouchesNoDisk()
    {
        using var dir = new TempCacheDir();
        var frame = RegionFrame.Build(KeyA);
        var terrain = RampTerrain(frame);
        var streamer = new PlanetTerrainStreamer(frame, terrain);
        Assert.Null(streamer.Cache);

        var key = new PatchKey(-3, 4);
        var heights = new float[PlanetPatchSampler.HeightmapLength];
        Assert.Equal(PatchFillSource.Generated, streamer.FillPatch(key, heights, parallel: false));
        AssertBitsEqual(ReferenceHeights(frame, terrain, key.X, key.Z), heights, "无缓存路径");
        Assert.Equal(0, streamer.CacheHits);
        Assert.Equal(0, streamer.CacheMisses);
        Assert.Empty(Directory.EnumerateFileSystemEntries(dir.Root));   // 没接缓存就不该往任何地方写
    }

    [Fact]
    public void CacheWriteFailure_KeepsHeightsAndStaysVisible()
    {
        using var dir = new TempCacheDir();
        var cache = new PlanetPatchCache(dir.Root, Seed, TerrainVersion);
        long regionId = KeyA.ToId();

        // 在缓存要建 region 目录的位置先放一个同名普通文件：建目录必然失败
        string regionDir = Path.GetDirectoryName(cache.PathFor(regionId, 0, 0))!;
        Directory.CreateDirectory(Path.GetDirectoryName(regionDir)!);
        File.WriteAllText(regionDir, "占位：这里本该是个目录");

        var frame = RegionFrame.Build(KeyA);
        var terrain = RampTerrain(frame);
        var streamer = new PlanetTerrainStreamer(frame, terrain, cache);
        var key = new PatchKey(0, 0);
        var heights = new float[PlanetPatchSampler.HeightmapLength];

        Assert.Equal(PatchFillSource.Generated, streamer.FillPatch(key, heights));
        Assert.Equal(1, streamer.CacheWriteFailures);
        Assert.Equal(0, cache.Writes);
        Assert.Equal(1, cache.Misses);
        AssertBitsEqual(ReferenceHeights(frame, terrain, key.X, key.Z), heights, "写盘失败后的高度仍是正确值");

        // 占位文件没被动过：失败的是新建，不是覆盖别人的东西
        Assert.Equal("占位：这里本该是个目录", File.ReadAllText(regionDir));

        // 正向对照：换成正常目录后同一条通路就能写成功
        File.Delete(regionDir);
        var cache2 = new PlanetPatchCache(dir.Root, Seed, TerrainVersion);
        var streamer2 = new PlanetTerrainStreamer(frame, terrain, cache2);
        Assert.Equal(PatchFillSource.Generated, streamer2.FillPatch(key, heights));
        Assert.Equal(0, streamer2.CacheWriteFailures);
        Assert.Equal(1, cache2.Writes);
        Assert.True(cache2.TryRead(regionId, 0, 0, heights, out _));
    }

    [Fact]
    public void AttachRegion_DropsEverythingAndStartsANewWindow()
    {
        using var dir = new TempCacheDir();
        var cache = new PlanetPatchCache(dir.Root, Seed, TerrainVersion);
        var frameA = RegionFrame.Build(KeyA);
        var streamer = new PlanetTerrainStreamer(frameA, RampTerrain(frameA), cache);
        var scratch = new float[PlanetPatchSampler.HeightmapLength];

        Assert.Equal(25, MarkWindowReady(streamer, 0, 0, scratch).Length);
        Assert.Equal(25, cache.EntryCount);

        var frameB = RegionFrame.Build(KeyB);
        streamer.AttachRegion(frameB);
        Assert.Equal(0, streamer.ResidentCount);
        Assert.Equal(0, streamer.CenterPatchX);
        Assert.Equal(0, streamer.CenterPatchZ);

        var plan = streamer.BuildPlan(0, 0);
        Assert.Equal(25, plan.DesiredPatches.Count);
        Assert.Equal(0, plan.ReadyInWindow);
        Assert.Equal(4, plan.ToLoad.Count);
        Assert.Equal(21, plan.PendingBeyondBudget);

        // 旧区域的 25 块还在盘上，新区域得重新生成——同格坐标不能读到对方的数据
        Assert.Equal(PatchFillSource.Generated, streamer.FillPatch(new PatchKey(0, 0), scratch));
        Assert.Equal(26, cache.EntryCount);
        Assert.Equal(0, streamer.CacheHits);
        Assert.True(File.Exists(cache.PathFor(KeyA.ToId(), 0, 0)));
        Assert.True(File.Exists(cache.PathFor(KeyB.ToId(), 0, 0)));

        // 正向对照：换回 A 区域时虽然驻留集空了，但缓存全命中，不再重算
        streamer.AttachRegion(frameA);
        Assert.Equal(PatchFillSource.CacheHit, streamer.FillPatch(new PatchKey(0, 0), scratch));
        Assert.Equal(1, streamer.CacheHits);
    }

    [Fact]
    public void Constructor_RejectsEvenWindowBadHysteresisAndZeroBudget()
    {
        var frame = RegionFrame.Build(KeyA);
        var terrain = RampTerrain(frame);

        Assert.Throws<ArgumentException>(() =>
            new PlanetTerrainStreamer(frame, terrain, windowEdge: 4));
        Assert.Throws<ArgumentException>(() =>
            new PlanetTerrainStreamer(frame, terrain, windowEdge: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PlanetTerrainStreamer(frame, terrain, hysteresisCm: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PlanetTerrainStreamer(frame, terrain, hysteresisCm: Patch));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PlanetTerrainStreamer(frame, terrain, maxLoadsPerPlan: 0));
        Assert.Throws<ArgumentNullException>(() =>
            new PlanetTerrainStreamer(frame, null!));

        // 正向对照：奇数窗口 + 0 滞后 + 预算 1 都是合法配置
        var ok = new PlanetTerrainStreamer(frame, terrain, windowEdge: 3, hysteresisCm: 0, maxLoadsPerPlan: 1);
        Assert.Equal(9, ok.BuildPlan(0, 0).DesiredPatches.Count);
        Assert.Equal(new PatchKey(0, 0), Assert.Single(ok.BuildPlan(0, 0).ToLoad));

        // 滞后为 0 时越界即动，不该有死区
        Assert.True(ok.BuildPlan(Patch, 0).CenterChanged);
    }

    [Fact]
    public void Tick_EnforcesCacheBudget_InsideBudgetNothingIsEvicted()
    {
        using var roomyDir = new TempCacheDir();
        using var tightDir = new TempCacheDir();
        var frame = RegionFrame.Build(KeyA);
        var terrain = RampTerrain(frame);
        var scratch = new float[PlanetPatchSampler.HeightmapLength];

        // 正向对照：默认 2 GB，冷加载 25 块之后一块都不该被逐
        var roomyCache = new PlanetPatchCache(roomyDir.Root, Seed, TerrainVersion);
        var roomy = new PlanetTerrainStreamer(frame, terrain, roomyCache);
        MarkWindowReady(roomy, 0, 0, scratch);
        Assert.Equal(25, roomyCache.EntryCount);
        Assert.Equal(0, roomyCache.Evictions);
        Assert.Equal(25, roomy.ResidentCount);

        // 预算只够 10 块：25 块冷加载后必须收敛到 10，且被逐的确实从盘上消失
        var tightCache = new PlanetPatchCache(tightDir.Root, Seed, TerrainVersion);
        var tight = new PlanetTerrainStreamer(frame, terrain, tightCache,
                                             cacheBudgetBytes: 10L * PlanetPatchCache.EntryBytes);
        MarkWindowReady(tight, 0, 0, scratch);
        Assert.Equal(10, tightCache.EntryCount);
        Assert.Equal(10L * PlanetPatchCache.EntryBytes, tightCache.BytesUsed);
        Assert.Equal(15, tightCache.Evictions);
        Assert.Equal(10, Directory.EnumerateFiles(tightCache.RootDirectory, "*.hgt",
                                                  SearchOption.AllDirectories).Count());

        // 磁盘淘汰不动引擎侧：驻留集仍然是满的 25 块，超预算的那部分只是"下次要重算"
        Assert.Equal(25, tight.ResidentCount);
        Assert.Equal(25, tight.CacheMisses);        // 25 块全是冷跑，没有一块是被误当成命中
        Assert.Equal(25, tightCache.Writes);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PlanetTerrainStreamer(frame, terrain, tightCache, cacheBudgetBytes: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PlanetTerrainStreamer(frame, terrain, tightCache,
                                      cacheBudgetBytes: PlanetPatchCache.EntryBytes - 1));
    }

    [Fact]
    public void FillPatch_RejectsWrongBuffer()
    {
        var frame = RegionFrame.Build(KeyA);
        var streamer = new PlanetTerrainStreamer(frame, RampTerrain(frame));
        Assert.Throws<ArgumentException>(() => streamer.FillPatch(new PatchKey(0, 0), new float[4]));
        Assert.Throws<ArgumentNullException>(() => streamer.FillPatch(new PatchKey(0, 0), null!));
    }
}

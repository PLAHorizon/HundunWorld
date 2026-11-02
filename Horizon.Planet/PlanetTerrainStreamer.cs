using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Horizon.Planet.Wire;

namespace Horizon.Planet;

/// <summary>近景 patch 在区域切平面里的格坐标（一格 = 一个 Flax Terrain patch，可为负）。</summary>
public readonly struct PatchKey : IEquatable<PatchKey>
{
    public int X { get; }
    public int Z { get; }

    public PatchKey(int x, int z)
    {
        X = x;
        Z = z;
    }

    /// <summary>patch 左下角（西北角按局部轴说）在区域切平面里的坐标（厘米）。</summary>
    public double OriginXCm => (double)X * PlanetConstants.TerrainPatchSizeCm;
    public double OriginZCm => (double)Z * PlanetConstants.TerrainPatchSizeCm;

    public bool Equals(PatchKey other) => X == other.X && Z == other.Z;
    public override bool Equals(object? obj) => obj is PatchKey k && Equals(k);
    public override int GetHashCode() => HashCode.Combine(X, Z);
    public override string ToString() => string.Format(CultureInfo.InvariantCulture, "P({0},{1})", X, Z);

    public static bool operator ==(PatchKey a, PatchKey b) => a.Equals(b);
    public static bool operator !=(PatchKey a, PatchKey b) => !a.Equals(b);
}

/// <summary>一块 patch 的高度是从哪儿来的：重算，还是磁盘缓存命中。</summary>
public enum PatchFillSource
{
    Generated = 0,
    CacheHit = 1,
}

/// <summary>
/// 一次流送计划：窗口该有哪 25 块、这次该加载哪几块（不超预算）、该卸载哪几块。
/// <para>计划是纯信息，不含引擎调用；客户端按计划去 AddPatch/RemovePatch，再回标 <c>MarkReady</c>。</para>
/// </summary>
public sealed class PatchWindowPlan
{
    internal PatchWindowPlan(int centerX, int centerZ, bool centerChanged,
                             PatchKey[] desired, PatchKey[] toLoad, PatchKey[] toUnload,
                             int pendingBeyondBudget, int readyInWindow, int budget)
    {
        CenterPatchX = centerX;
        CenterPatchZ = centerZ;
        CenterChanged = centerChanged;
        DesiredPatches = desired;
        ToLoad = toLoad;
        ToUnload = toUnload;
        PendingBeyondBudget = pendingBeyondBudget;
        ReadyInWindow = readyInWindow;
        Budget = budget;
    }

    public int CenterPatchX { get; }
    public int CenterPatchZ { get; }
    public bool CenterChanged { get; }
    public IReadOnlyList<PatchKey> DesiredPatches { get; }
    public IReadOnlyList<PatchKey> ToLoad { get; }
    public IReadOnlyList<PatchKey> ToUnload { get; }
    public int PendingBeyondBudget { get; }
    public int ReadyInWindow { get; }
    public int Budget { get; }

    /// <summary>窗口里每一块都已就位。</summary>
    public bool IsSatisfied => ToLoad.Count == 0 && PendingBeyondBudget == 0;

    public string Describe() => string.Format(CultureInfo.InvariantCulture,
        "plan center=({0},{1}) changed={2} desired={3} ready={4} toLoad={5} toUnload={6} pending={7} budget={8}",
        CenterPatchX, CenterPatchZ, CenterChanged, DesiredPatches.Count, ReadyInWindow,
        ToLoad.Count, ToUnload.Count, PendingBeyondBudget, Budget);
}

/// <summary>
/// 近景 5×5 patch 窗口的流送调度（§4/§7）。
/// <para>三件事：窗口跟着玩家走但带滞后（边界上抖几厘米不整行换块）；每帧限量生成；
/// 出窗的 patch 立刻从驻留集里摘掉（磁盘缓存不删，下次回来直接命中）。</para>
/// <para>高度的取法只有一条：<see cref="PlanetPatchSampler.FillPatchHeights"/>，缓存只是它的加速层，
/// 所以 <see cref="FillPatch"/> 的命中结果与重算逐位相同。</para>
/// </summary>
public sealed class PlanetTerrainStreamer
{
    /// <summary>
    /// 局部坐标的合法范围（厘米）：直接取线协议里上报位置的同一个上限，不另立一个数——
    /// 两处各写一遍迟早会漂。真正的"该换区了"由区域迁移流程判，这里只挡坏输入和整数溢出。
    /// </summary>
    public const int MaxLocalCoordinateCm = PlayerStatePacket.MaxAbsLocalCm;   // 100 km

    private readonly PlanetTerrain _terrain;
    private readonly HeightSampler? _sampler;
    private readonly PlanetPatchCache? _cache;
    private readonly HashSet<PatchKey> _resident = new();
    private readonly int _hysteresisCm;
    private readonly int _maxLoadsPerPlan;
    private readonly long _cacheBudgetBytes;

    public RegionFrame Frame { get; private set; }

    /// <summary>窗口每边 patch 数（奇数，玩家在正中）。</summary>
    public int WindowEdge { get; }

    /// <summary>缓存命中/未命中次数。命中走的是同一份高度数组，不再产生生成成本。</summary>
    public long CacheHits { get; private set; }
    public long CacheMisses { get; private set; }

    /// <summary>缓存写失败次数（盘满/被锁）：不影响玩，但必须看得见，否则命中率会莫名一直为零。</summary>
    public long CacheWriteFailures { get; private set; }

    public int CenterPatchX { get; private set; }
    public int CenterPatchZ { get; private set; }
    public int ResidentCount => _resident.Count;
    public IReadOnlyCollection<PatchKey> ResidentPatches => _resident;
    public PlanetPatchCache? Cache => _cache;

    public PlanetTerrainStreamer(RegionFrame frame, PlanetTerrain terrain,
                                 PlanetPatchCache? cache = null, HeightSampler? sampler = null,
                                 int windowEdge = PlanetConstants.NearPatchWindowEdge,
                                 int hysteresisCm = PlanetConstants.PatchWindowHysteresisCm,
                                 int maxLoadsPerPlan = PlanetConstants.PatchLoadsPerPlan,
                                 long cacheBudgetBytes = PlanetPatchCache.DefaultBudgetBytes)
    {
        Frame = frame;
        _terrain = terrain ?? throw new ArgumentNullException(nameof(terrain));
        if (windowEdge < 1 || windowEdge % 2 != 1)
            throw new ArgumentException($"窗口边长必须是正奇数（中心 patch 才在正中），实际 {windowEdge}",
                                        nameof(windowEdge));
        if (hysteresisCm < 0)
            throw new ArgumentOutOfRangeException(nameof(hysteresisCm), hysteresisCm, "滞后余量不能为负");
        if (hysteresisCm >= PlanetConstants.TerrainPatchSizeCm)
            throw new ArgumentOutOfRangeException(nameof(hysteresisCm), hysteresisCm,
                                                  "滞后余量必须小于一格 patch，否则窗口永远追不上玩家");
        if (maxLoadsPerPlan < 1)
            throw new ArgumentOutOfRangeException(nameof(maxLoadsPerPlan), maxLoadsPerPlan, "每帧预算至少 1 块");
        if (cacheBudgetBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(cacheBudgetBytes), cacheBudgetBytes,
                                                  "缓存预算必须至少容得下一块 patch");
        if (cache is not null && cacheBudgetBytes < PlanetPatchCache.EntryBytes)
            throw new ArgumentOutOfRangeException(nameof(cacheBudgetBytes), cacheBudgetBytes,
                                                  $"预算小于一块 patch（{PlanetPatchCache.EntryBytes} 字节）就会把刚写的删掉，等于没有缓存");
        if (sampler is not null && !ReferenceEquals(sampler.Grid, (_terrain.Heights as HybridHeightSource)?.BaseField))
            throw new ArgumentException("sampler 的基场必须与 terrain 的高度源同属一个混合源", nameof(sampler));

        WindowEdge = windowEdge;
        _hysteresisCm = hysteresisCm;
        _maxLoadsPerPlan = maxLoadsPerPlan;
        _cacheBudgetBytes = cacheBudgetBytes;
        _cache = cache;
        _sampler = sampler;
    }

    /// <summary>玩家所在 patch（不受滞后影响：这是"脚下"，不是"窗口中心"）。</summary>
    public PatchKey PatchOf(double localXCm, double localZCm)
    {
        CheckLocal(localXCm, nameof(localXCm));
        CheckLocal(localZCm, nameof(localZCm));
        return new PatchKey(PatchIndexOf(localXCm), PatchIndexOf(localZCm));
    }

    private static void CheckLocal(double value, string name)
    {
        if (!double.IsFinite(value))
            throw new ArgumentOutOfRangeException(name, value, "玩家局部坐标必须是有限值");
        if (Math.Abs(value) > MaxLocalCoordinateCm)
            throw new ArgumentOutOfRangeException(name, value,
                $"局部坐标超出 {MaxLocalCoordinateCm} cm，玩家已不在本区域的切平面内");
    }

    private static int PatchIndexOf(double localCm) =>
        (int)Math.Floor(localCm / PlanetConstants.TerrainPatchSizeCm);

    /// <summary>
    /// 换区域：驻留集整体作废（旧区域的 patch 在新帧里坐标含义不同），窗口回到中心格。
    /// </summary>
    public void AttachRegion(RegionFrame frame)
    {
        Frame = frame;
        CenterPatchX = 0;
        CenterPatchZ = 0;
        _resident.Clear();
    }

    /// <summary>引擎确认一块 patch 已就位。</summary>
    public void MarkReady(PatchKey key) => _resident.Add(key);

    public void MarkReady(int patchX, int patchZ) => _resident.Add(new PatchKey(patchX, patchZ));

    /// <summary>引擎把 patch 丢了（内存压力/被踢）：调度器必须知道，否则再也不会重新加载它。</summary>
    public void MarkUnloaded(PatchKey key) => _resident.Remove(key);

    public bool IsReady(PatchKey key) => _resident.Contains(key);

    /// <summary>
    /// 算一次计划。会移动窗口中心（带滞后）并把出窗的 patch 从驻留集里摘掉——
    /// 所以同一个位置连调两次，第二次不会再报同一批卸载。
    /// </summary>
    public PatchWindowPlan BuildPlan(double localXCm, double localZCm)
    {
        PatchKey foot = PatchOf(localXCm, localZCm);
        int previousX = CenterPatchX, previousZ = CenterPatchZ;
        CenterPatchX = Advance(CenterPatchX, foot.X, localXCm);
        CenterPatchZ = Advance(CenterPatchZ, foot.Z, localZCm);
        bool centerChanged = CenterPatchX != previousX || CenterPatchZ != previousZ;

        int half = WindowEdge / 2;
        int count = WindowEdge * WindowEdge;
        var desired = new PatchKey[count];
        var wanted = new HashSet<PatchKey>();
        int i = 0;
        for (int dz = -half; dz <= half; dz++)
        {
            for (int dx = -half; dx <= half; dx++)
            {
                var key = new PatchKey(CenterPatchX + dx, CenterPatchZ + dz);
                desired[i++] = key;
                wanted.Add(key);
            }
        }

        var unloadList = new List<PatchKey>();
        foreach (var key in _resident)
        {
            if (!wanted.Contains(key)) unloadList.Add(key);
        }
        foreach (var key in unloadList) _resident.Remove(key);

        // 由近及远排队：优先玩家脚下，其次同一环里的按 (dx, dz) 定序，保证两次计划给同样的顺序
        var missing = new List<(PatchKey Key, int Ring, long Dist2, int Dx, int Dz)>();
        foreach (var key in desired)
        {
            if (_resident.Contains(key)) continue;
            int dx = key.X - CenterPatchX, dz = key.Z - CenterPatchZ;
            missing.Add((key, Math.Max(Math.Abs(dx), Math.Abs(dz)), (long)dx * dx + (long)dz * dz, dx, dz));
        }
        missing.Sort(static (a, b) =>
        {
            int c = a.Ring.CompareTo(b.Ring);
            if (c != 0) return c;
            c = a.Dist2.CompareTo(b.Dist2);
            if (c != 0) return c;
            c = a.Dx.CompareTo(b.Dx);
            return c != 0 ? c : a.Dz.CompareTo(b.Dz);
        });

        int readyInWindow = count - missing.Count;
        int take = Math.Min(_maxLoadsPerPlan, missing.Count);
        var loadList = new PatchKey[take];
        for (int k = 0; k < take; k++) loadList[k] = missing[k].Key;

        return new PatchWindowPlan(CenterPatchX, CenterPatchZ, centerChanged,
                                   desired, loadList, unloadList.ToArray(),
                                   missing.Count - take, readyInWindow, _maxLoadsPerPlan);
    }

    /// <summary>
    /// 单向滞后（施密特触发）：中心只在一格一格地挪，且必须越过边界再走 <c>hysteresisCm</c> 才挪。
    /// 跳两格以上说明玩家传送了，滞后没有意义，直接对齐。
    /// </summary>
    private int Advance(int current, int candidate, double playerCm)
    {
        if (candidate == current) return current;
        if (Math.Abs(candidate - current) > 1) return candidate;

        double edge = PlanetConstants.TerrainPatchSizeCm;
        if (candidate > current)
            return playerCm >= (current + 1) * edge + _hysteresisCm ? current + 1 : current;
        return playerCm <= current * edge - _hysteresisCm ? current - 1 : current;
    }

    /// <summary>
    /// 取一块 patch 的高度：先查磁盘缓存，未命中就重算并回填。
    /// <para>两条出口给的是同一个 <see cref="float"/> 数组，所以命中与否不会改变任何一个数；
    /// 缓存写失败只影响下一次的速度，不影响这次的结果。</para>
    /// </summary>
    public PatchFillSource FillPatch(PatchKey key, float[] heights, bool parallel = true)
    {
        if (heights is null) throw new ArgumentNullException(nameof(heights));
        if (heights.Length != PlanetPatchSampler.HeightmapLength)
            throw new ArgumentException($"必须是 {PlanetPatchSampler.HeightmapLength} 个高度值", nameof(heights));

        long regionId = Frame.Key.ToId();
        if (_cache is not null && _cache.TryRead(regionId, key.X, key.Z, heights, out _))
        {
            CacheHits++;
            return PatchFillSource.CacheHit;
        }
        if (_cache is not null) CacheMisses++;

        PlanetPatchSampler.FillPatchHeights(Frame, _terrain, key.OriginXCm, key.OriginZCm,
                                            heights, _sampler, parallel);

        if (_cache is not null)
        {
            try
            {
                _cache.Write(regionId, key.X, key.Z, heights);
            }
            catch (IOException) { CacheWriteFailures++; }
            catch (UnauthorizedAccessException) { CacheWriteFailures++; }
        }
        return PatchFillSource.Generated;
    }

    /// <summary>
    /// 计划 + 落地一条完整通路：加载本帧该加载的（走缓存），回标驻留，返回计划。
    /// 客户端每帧调一次；离线判据也用它，所以"预算收敛"是跑出来的而不是推出来的。
    /// </summary>
    public PatchWindowPlan Tick(double localXCm, double localZCm, float[] scratch)
    {
        var plan = BuildPlan(localXCm, localZCm);
        foreach (var key in plan.ToLoad)
        {
            FillPatch(key, scratch);
            MarkReady(key);
        }
        // 预算闸门：没超预算时 TrimToBudget 只比一个数就返回，所以每帧调得起；
        // 真超了才逐块淘汰，不然 §7.1 那个 2 GB 上限就是个没人执行的声明
        _cache?.TrimToBudget(_cacheBudgetBytes);
        return plan;
    }

    /// <summary>缓存目录的字节占用（供上层打日志/做预算决策）。</summary>
    public long CacheBytesUsed => _cache?.BytesUsed ?? 0;

    public string Describe() => string.Format(CultureInfo.InvariantCulture,
        "streamer region={0} center=({1},{2}) window={3} resident={4}",
        Frame.Key, CenterPatchX, CenterPatchZ, WindowEdge, _resident.Count);
}

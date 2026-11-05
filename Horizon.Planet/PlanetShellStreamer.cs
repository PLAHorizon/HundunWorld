using System;
using System.Collections.Generic;

namespace Horizon.Planet;

/// <summary>
/// 壳的每帧投放器：拿 <see cref="PlanetShellPlanner"/> 的计划，按顶点预算决定"这一帧建哪几块"，
/// 并把已经离开计划的块交还给调用方去回收。
/// <para>为什么单独有这个东西：§13.26 的"每帧 ≤2 ms"是一条预算，不是一条"全都建"的指令；
/// 而玩家移动时计划会整片换血，没有驱逐就会一边堆新块一边漏旧块。</para>
/// <para>它是纯状态机：不碰引擎、不持时间、不并发。同一串 Sync 必须给同一串返回值——
/// 判据靠这一点逐帧对拍。</para>
/// </summary>
public sealed class PlanetShellStreamer
{
    private readonly long _vertexBudgetPerFrame;
    private readonly HashSet<RegionKey> _desired = new();
    private readonly HashSet<RegionKey> _built = new();
    private readonly List<ShellTile> _frameScratch = new();
    private readonly List<RegionKey> _evictScratch = new();

    private ShellPlan? _plan;
    private long _builtCount, _evictedCount, _deferredFrames, _peakFrameVertices, _framesToSettle;
    private bool _settledReported;

    public PlanetShellStreamer(long vertexBudgetPerFrame)
    {
        if (vertexBudgetPerFrame < 0)
            throw new ArgumentOutOfRangeException(nameof(vertexBudgetPerFrame), vertexBudgetPerFrame, "预算不能为负");
        _vertexBudgetPerFrame = vertexBudgetPerFrame;
    }

    public long VertexBudgetPerFrame => _vertexBudgetPerFrame;
    public ShellPlan? Plan => _plan;

    /// <summary>当前计划里已经建好的块数（不含等待回收的旧块）。</summary>
    public int BuiltInPlan
    {
        get
        {
            int n = 0;
            foreach (var key in _built)
                if (_desired.Contains(key)) n++;
            return n;
        }
    }

    public int PendingCount => _desired.Count - BuiltInPlan;
    public bool IsSettled => PendingCount == 0;
    public long Built => Interlocked.Read(ref _builtCount);
    public long Evicted => Interlocked.Read(ref _evictedCount);

    /// <summary>预算不够、被迫留到下一帧的帧数。0 就说明预算从来没卡过。</summary>
    public long DeferredFrames => Interlocked.Read(ref _deferredFrames);

    /// <summary>实测到的单帧最大顶点数——预算是否真被守住，看这个而不是看配置。</summary>
    public long PeakFrameVertices => Interlocked.Read(ref _peakFrameVertices);

    /// <summary>从最后一次 Sync 到全部建好用了多少帧；没 settle 过就是 0。</summary>
    public long FramesToSettle => Interlocked.Read(ref _framesToSettle);

    /// <summary>
    /// 换计划。旧计划里不在新计划中的块会进回收名单，由 <see cref="Step"/> 一并交出来。
    /// </summary>
    public void Sync(ShellPlan plan)
    {
        if (plan is null) throw new ArgumentNullException(nameof(plan));
        _plan = plan;
        _desired.Clear();
        foreach (var tile in plan.Tiles)
        {
            _desired.Add(tile.Key);
        }
        // 建好但已经不在计划里的块：交给调用方去删，删完才算账平
        _evictScratch.Clear();
        foreach (var key in _built)
            if (!_desired.Contains(key)) _evictScratch.Add(key);
        if (_evictScratch.Count > 0)
        {
            foreach (var key in _evictScratch) _built.Remove(key);
            Interlocked.Add(ref _evictedCount, _evictScratch.Count);
        }
    }

    /// <summary>本帧要建的块（按计划的近到远顺序，累计顶点不超预算）。同时给出要回收的旧块。</summary>
    public IReadOnlyList<ShellTile> Step(out IReadOnlyList<RegionKey> evict)
    {
        _stepCount++;                       // 先算帧号，settle 的那一帧自己也要计入
        _frameScratch.Clear();
        if (_evictScratch.Count == 0) evict = Array.Empty<RegionKey>();
        else
        {
            // 只交出来一次：第二次 Step 不能再报同一批，否则调用方会删两遍
            evict = _evictScratch.ToArray();
            _evictScratch.Clear();
        }
        if (_plan is null) return _frameScratch;

        long used = 0;
        bool deferred = false;
        foreach (var tile in _plan.Tiles)
        {
            if (_built.Contains(tile.Key)) continue;
            if (used + tile.VertexCount > _vertexBudgetPerFrame)
            {
                deferred = true;
                break;
            }
            used += tile.VertexCount;
            _frameScratch.Add(tile);
            _built.Add(tile.Key);
            Interlocked.Increment(ref _builtCount);
        }
        if (used > _peakFrameVertices) Interlocked.Exchange(ref _peakFrameVertices, used);
        if (deferred) Interlocked.Increment(ref _deferredFrames);
        if (!deferred && IsSettled && !_settledReported)
        {
            _settledReported = true;
            Interlocked.Exchange(ref _framesToSettle, _stepCount);
        }
        return _frameScratch;
    }

    private long _stepCount;

    /// <summary>给日志用的一行回执。</summary>
    public string Describe() =>
        $"shell built={_builtCount} evicted={_evictedCount} pending={PendingCount} settled={(_settledReported ? 1 : 0)} " +
        $"frames_to_settle={_framesToSettle} deferred_frames={_deferredFrames} peak_frame_verts={_peakFrameVertices} " +
        $"budget={_vertexBudgetPerFrame}";
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Arch.Core;

namespace Horizon.Game.ECS.Arch.Core;

/// <summary>
/// Arch ECS 的宿主：封装一个 <see cref="World"/>，按 <see cref="SystemGroup"/> 分阶段执行 <see cref="IArchSystem"/>。
/// 设计目标：
///   * 接管旧 <see cref="Horizon.Game.ECS.Core.EcsWorld"/> 的角色，提供更高性能的 chunk-based 存储。
///   * 调度顺序：NetworkReceive → FixedUpdate → Update → Render → NetworkSend。
///   * 主线程系统串行执行；标记非 <c>MainThreadOnly</c> 的系统在同组内仍按声明顺序执行（后续可接入
///     Arch.System.JobScheduler 实现并行 chunk）。
/// </summary>
public sealed class ArchWorldHost : IDisposable
{
    private readonly object _systemLock = new();
    private readonly Dictionary<SystemGroup, List<IArchSystem>> _systemsByGroup = new();
    private readonly List<IArchSystem> _allSystems = new();
    private bool _disposed;

    // ── 系统数组缓存（消除每帧 ToArray 分配）──
    // Tick 和 GetSystems 每帧被调用，原实现每次 ToArray 创建新数组 → GC 压力 → 卡顿。
    // 缓存数组，仅在 AddSystem/RemoveSystem 时标记为脏并重建。
    private Dictionary<SystemGroup, IArchSystem[]>? _systemArraysCache;
    private volatile bool _cacheDirty = true;

    /// <summary>底层 Arch 世界。</summary>
    public World World { get; }

    /// <summary>当前 tick 序号（每次 <see cref="Tick"/> 自增）。</summary>
    public long CurrentTick { get; private set; }

    /// <summary>累计已运行时间（驱动用）。</summary>
    public TimeSpan TotalTime { get; private set; }

    /// <summary>当前是否处于 <see cref="Tick"/> 调用栈中。</summary>
    public bool IsTicking => Interlocked.CompareExchange(ref _ticking, 0, 0) != 0;

    private int _ticking;

    public ArchWorldHost()
        : this(World.Create())
    {
    }

    public ArchWorldHost(World world)
    {
        World = world ?? throw new ArgumentNullException(nameof(world));
        foreach (SystemGroup group in Enum.GetValues<SystemGroup>())
        {
            _systemsByGroup[group] = new List<IArchSystem>();
        }
    }

    /// <summary>
    /// 注册一个系统。同组内按 <see cref="IArchSystem.Order"/> 升序执行。
    /// 不允许在 <see cref="Tick"/> 期间注册（会抛 <see cref="InvalidOperationException"/>）。
    /// </summary>
    public void AddSystem(IArchSystem system)
    {
        ArgumentNullException.ThrowIfNull(system);
        if (IsTicking)
        {
            throw new InvalidOperationException("Cannot AddSystem while ArchWorldHost is ticking.");
        }

        lock (_systemLock)
        {
            ThrowIfDisposed();
            var bucket = _systemsByGroup[system.Group];
            bucket.Add(system);
            bucket.Sort(static (a, b) => a.Order.CompareTo(b.Order));
            _allSystems.Add(system);
            _cacheDirty = true;
        }

        system.Initialize(World);
    }

    /// <summary>移除并 Dispose 一个系统。</summary>
    public bool RemoveSystem(IArchSystem system)
    {
        ArgumentNullException.ThrowIfNull(system);
        if (IsTicking)
        {
            throw new InvalidOperationException("Cannot RemoveSystem while ArchWorldHost is ticking.");
        }

        bool removed;
        lock (_systemLock)
        {
            ThrowIfDisposed();
            removed = _systemsByGroup[system.Group].Remove(system);
            if (removed)
            {
                _allSystems.Remove(system);
                _cacheDirty = true;
            }
        }

        if (removed)
        {
            try { system.Dispose(World); } catch { /* swallow to avoid disrupting host */ }
        }

        return removed;
    }

    /// <summary>列出某组系统（返回缓存的数组引用，不分配新数组）。</summary>
    public IReadOnlyList<IArchSystem> GetSystems(SystemGroup group)
    {
        EnsureCacheValid();
        return _systemArraysCache![group];
    }

    /// <summary>
    /// 确保 _systemArraysCache 是最新的。仅在 _cacheDirty 时重建（AddSystem/RemoveSystem 后）。
    /// 正常运行时每帧调用此方法是无操作（零分配），消除原 ToArray 的每帧 GC 压力。
    /// </summary>
    private void EnsureCacheValid()
    {
        if (!_cacheDirty) return;
        lock (_systemLock)
        {
            if (!_cacheDirty) return; // double-check
            _systemArraysCache ??= new Dictionary<SystemGroup, IArchSystem[]>();
            foreach (SystemGroup group in Enum.GetValues<SystemGroup>())
            {
                _systemArraysCache[group] = _systemsByGroup[group].ToArray();
            }
            _cacheDirty = false;
        }
    }

    /// <summary>列出所有已注册系统。</summary>
    public IReadOnlyList<IArchSystem> GetAllSystems()
    {
        lock (_systemLock)
        {
            return _allSystems.ToArray();
        }
    }

    /// <summary>
    /// 推进一帧。按 <see cref="SystemGroup"/> 顺序串行执行各组系统。
    /// 注意：本方法 **必须** 在主线程调用，因 <see cref="IArchSystem.MainThreadOnly"/> 系统
    /// 会被假定运行在调用线程上。
    /// </summary>
    public void Tick(TimeSpan deltaTime)
    {
        ThrowIfDisposed();
        if (Interlocked.Exchange(ref _ticking, 1) != 0)
        {
            throw new InvalidOperationException("ArchWorldHost.Tick is not reentrant.");
        }

        try
        {
            CurrentTick++;
            TotalTime += deltaTime;

            // 使用缓存的系统数组，消除每帧 ToArray + lock 的分配和锁开销。
            // 仅在 AddSystem/RemoveSystem 后重建缓存（EnsureCacheValid 内部判断）。
            EnsureCacheValid();
            foreach (SystemGroup group in Enum.GetValues<SystemGroup>())
            {
                var snapshot = _systemArraysCache![group];
                if (snapshot.Length == 0) continue;

                foreach (var sys in snapshot)
                {
                    sys.Update(World, deltaTime);
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _ticking, 0);
        }
    }

    /// <summary>释放所有系统并销毁底层 World。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        IArchSystem[] snapshot;
        lock (_systemLock)
        {
            snapshot = _allSystems.ToArray();
            _allSystems.Clear();
            foreach (var bucket in _systemsByGroup.Values)
            {
                bucket.Clear();
            }
        }

        foreach (var sys in snapshot)
        {
            try { sys.Dispose(World); } catch { /* ignore */ }
        }

        try { World.Destroy(World); } catch { /* ignore */ }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ArchWorldHost));
    }
}

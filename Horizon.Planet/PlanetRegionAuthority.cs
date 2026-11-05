using System;
using System.Collections.Generic;
using System.Globalization;
using Horizon.Planet.Wire;

namespace Horizon.Planet.Authority;

/// <summary>权限层自己的拒绝理由。几何/协议判定由 <see cref="TransferRejectReason"/> 负责，
/// 这里只补"实体表与 tick"这三条权限层才有的检查——两套理由分开，日志才知道该找谁。</summary>
public enum AuthorityReject
{
    None = 0,
    UnknownEntity = 1,
    OwnerMismatch = 2,
    TickRegression = 3,
}

/// <summary>一次迁移请求的完整答复：几何判定 + 权限判定 + 是否真的改了归属 + 交接 tick。</summary>
public readonly struct TransferResult
{
    internal TransferResult(TransferDecision decision, AuthorityReject reject, bool applied,
                            RegionKey region, long handoverTick, RegionFrame frame)
    {
        Decision = decision;
        Reject = reject;
        Applied = applied;
        NewRegion = region;
        HandoverTick = handoverTick;
        Frame = frame;
    }

    public TransferDecision Decision { get; }
    public AuthorityReject Reject { get; }

    /// <summary>只有几何判定与权限判定都过，才真的换归属。</summary>
    public bool Applied { get; }

    public RegionKey NewRegion { get; }
    public long HandoverTick { get; }

    /// <summary>新区的切平面基（§6.3 第 3 步客户端要切的就是这一套）。</summary>
    public RegionFrame Frame { get; }

    public override string ToString() => string.Format(CultureInfo.InvariantCulture,
        "transfer applied={0} new={1} tick={2} decision={3} reject={4}",
        Applied, NewRegion, HandoverTick, Decision.Reason, Reject);
}

/// <summary>100 m 格（§6.4 的 AOI 中间层）。区域 + 格才够描述"边界 cell 订阅"。</summary>
public readonly struct CellKey : IEquatable<CellKey>
{
    public CellKey(RegionKey region, int cx, int cz) { Region = region; Cx = cx; Cz = cz; }
    public RegionKey Region { get; }
    public int Cx { get; }
    public int Cz { get; }

    public bool Equals(CellKey other) =>
        Region.Equals(other.Region) && Cx == other.Cx && Cz == other.Cz;

    public override bool Equals(object? obj) => obj is CellKey c && Equals(c);
    public override int GetHashCode() => HashCode.Combine(Region, Cx, Cz);
    public override string ToString() => string.Format(CultureInfo.InvariantCulture,
        "Cell({0} {1},{2})", Region, Cx, Cz);
}

/// <summary>
/// 一条实体记录的对外视图（持久化投影与诊断用）。
/// <para>刻意不做成可变类型，也不暴露内部 <c>Entry</c>：调用方拿到的是一份拷贝，
/// 改了它不会污染权威表——否则"存档写出去的值和内存里的值一致"这条判据就没了着力点。</para>
/// <para><see cref="Tick"/> 是该实体最后一次被受理的 tick（不是时钟）：恢复时要按它升序回放，
/// 因为 <see cref="PlanetRegionAuthority.TryBind"/> 拒绝比时钟旧的落位。</para>
/// </summary>
public readonly struct EntitySnapshot
{
    internal EntitySnapshot(long entityId, RegionKey region, in PlanetVector3 globalCm, long tick)
    {
        EntityId = entityId;
        Region = region;
        GlobalCm = globalCm;
        Tick = tick;
    }

    public long EntityId { get; }
    public RegionKey Region { get; }
    public PlanetVector3 GlobalCm { get; }
    public long Tick { get; }

    public override string ToString() => string.Format(CultureInfo.InvariantCulture,
        "entity({0} region={1} tick={2})", EntityId, Region, Tick);
}

/// <summary>
/// 区域权威（§6.1 / §6.3 / §6.4 的服务端那一半）。
/// <para>
/// 这块刻意<b>不依赖 Orleans</b>：它只管"谁在哪个区域、什么时候交接、谁能看见谁"，
/// 全部用调用方给的 tick 推进，所以能在离线判据里被完整跑一遍——包括 §9 那条
/// "双客户端跨区不丢实体"。Orleans 那一层（grain、消息注册、持久化）是它的适配器，
/// 不是它的正确性来源。
/// </para>
/// <para>三条硬规矩：</para>
/// <list type="number">
/// <item><description><b>不信客户端报的目标区域</b>：归属一律取"全局点反查出来的真正区域"，
/// 并且要求它与当前归属共边（<see cref="PlanetRegions.ValidateTransfer"/>，§13.13）。</description></item>
/// <item><description><b>被拒的迁移必须一点状态都不改</b>：归属、重叠窗口、tick、订阅集都不许动。</description></item>
/// <item><description><b>旧区在重叠窗口内继续可见</b>（§6.3 第 5 步），窗口结束才摘除——
/// 摘除的依据是 tick，不是墙上时间，所以判据是确定的。</description></item>
/// </list>
/// </summary>
public sealed class PlanetRegionAuthority
{
    /// <summary>AOI 中间层的格边长：100 m = 10 000 cm（§6.4）。</summary>
    public const int CellSizeCm = 10_000;

    private sealed class Entry
    {
        public RegionKey Region;
        public PlanetVector3 GlobalCm;
        public long Tick;
        public RegionKey OverlapFrom;
        public long OverlapUntil;
        public bool HasOverlap;
    }

    private readonly Dictionary<long, Entry> _entries = new();
    private readonly long _radiusCm;
    private readonly int _level;
    private readonly int _overlapTicks;
    private long _clock;
    private long _applied, _rejectedGeometry, _rejectedAuthority;

    public PlanetRegionAuthority(long radiusCm = PlanetConstants.PlanetRadiusCm,
                                 int level = PlanetConstants.DefaultRegionLevel,
                                 int overlapTicks = 120)
    {
        if (radiusCm <= 0) throw new ArgumentOutOfRangeException(nameof(radiusCm), radiusCm, "半径必须为正");
        if (level < 0) throw new ArgumentOutOfRangeException(nameof(level), level, "层级不能为负");
        if (overlapTicks < 0) throw new ArgumentOutOfRangeException(nameof(overlapTicks), overlapTicks, "重叠窗口不能为负");
        _radiusCm = radiusCm;
        _level = level;
        _overlapTicks = overlapTicks;
        _clock = 0;
    }

    public long RadiusCm => _radiusCm;
    public int Level => _level;
    public int OverlapTicks => _overlapTicks;
    public long Clock => VolatileRead();
    public int Count => _entries.Count;
    public long Applied => System.Threading.Interlocked.Read(ref _applied);
    public long RejectedGeometry => System.Threading.Interlocked.Read(ref _rejectedGeometry);
    public long RejectedAuthority => System.Threading.Interlocked.Read(ref _rejectedAuthority);

    private long VolatileRead() => System.Threading.Volatile.Read(ref _clock);

    /// <summary>初始落位。区域由全局点反查，不接受调用方报的区域。</summary>
    public bool TryBind(long entityId, in PlanetVector3 globalCm, long tick, out RegionKey region)
    {
        region = default;
        if (tick < _clock) return false;                       // 落位不许把时钟拨回去
        // 先验坐标再反查区域：FromDirection 对零向量/NaN 会抛，而"坐标不可用"是数据问题不是程序问题。
        // 与 PlanetRegions.ValidateTransfer 同一口径（那边也是先判 len 再反查）。
        double len = globalCm.Length;
        if (!UsablePoint(len)) return false;

        region = RegionKey.FromDirection(globalCm, _level);
        _entries[entityId] = new Entry { Region = region, GlobalCm = globalCm, Tick = tick };
        _clock = Math.Max(_clock, tick);
        return true;
    }

    /// <summary>
    /// 点能不能用：有限、非退化、且离海面不过分远。
    /// <para>除了"允许在大气里飞"之外不掺别的判断——地心、天外、NaN 都是数据坏掉，
    /// 落位与传送两条路都要在同一处挡掉，否则 <see cref="RegionKey.FromDirection"/>
    /// 对退化方向会直接抛（那是数据问题，不该冒泡成服务端异常）。</para>
    /// </summary>
    private bool UsablePoint(double length) =>
        double.IsFinite(length) && length > 1e-300 && Math.Abs(length - _radiusCm) <= MaxAltitudeCm;

    /// <summary>离地多高还算"在这个区域里"。与 §13.13 的迁移口径一致。</summary>
    public const double MaxAltitudeCm = PlanetRegions.MaxTransferAltitudeCm;

    /// <summary>
    /// 传送 / 快速旅行（§6.4）：直接改全局点，<b>不要求共边</b>，也不走重叠窗口——
    /// 跨半个星球的插值与回滚才是要避免的东西。
    /// </summary>
    public bool TryTeleport(long entityId, in PlanetVector3 globalCm, long tick, out RegionKey region)
    {
        region = default;
        if (!_entries.TryGetValue(entityId, out var entry)) return false;
        if (tick < entry.Tick) return false;
        double len = globalCm.Length;
        if (!UsablePoint(len)) return false;

        region = RegionKey.FromDirection(globalCm, _level);
        entry.Region = region;
        entry.GlobalCm = globalCm;
        entry.Tick = tick;
        entry.HasOverlap = false;
        _clock = Math.Max(_clock, tick);
        System.Threading.Interlocked.Increment(ref _applied);
        return true;
    }

    /// <summary>
    /// 处理一条 <see cref="RegionTransferRequest"/>。
    /// 几何判定先过 <see cref="PlanetRegions.ValidateTransfer"/>，再过权限层自己的三条：
    /// 实体存在、报的 <c>from</c> 确实是当前归属、tick 不倒退。
    /// <b>实体号由调用方传</b>：线协议里只有 <c>fromRegionId</c>，"会话→实体"是服务端自己的事，
    /// 不该混进几何判定里（否则协议一改，几何判定的判据就跟着漂）。
    /// </summary>
    public TransferResult RequestTransfer(long entityId, in RegionTransferRequest request, long tick)
    {
        var decision = PlanetRegions.ValidateTransfer(request, _radiusCm, _level);
        if (!decision.Accepted)
        {
            System.Threading.Interlocked.Increment(ref _rejectedGeometry);
            return new TransferResult(decision, AuthorityReject.None, false, decision.From,
                                      tick, default);
        }

        if (!_entries.TryGetValue(entityId, out var entry))
        {
            System.Threading.Interlocked.Increment(ref _rejectedAuthority);
            return new TransferResult(decision, AuthorityReject.UnknownEntity, false, decision.From,
                                      tick, default);
        }
        if (entry.Region != decision.From)
        {
            System.Threading.Interlocked.Increment(ref _rejectedAuthority);
            return new TransferResult(decision, AuthorityReject.OwnerMismatch, false, entry.Region,
                                      tick, default);
        }
        if (tick < entry.Tick)
        {
            System.Threading.Interlocked.Increment(ref _rejectedAuthority);
            return new TransferResult(decision, AuthorityReject.TickRegression, false, entry.Region,
                                      tick, default);
        }

        var old = entry.Region;
        entry.Region = decision.To;
        entry.GlobalCm = request.GlobalCm.ToVector();
        entry.Tick = tick;
        entry.OverlapFrom = old;
        entry.OverlapUntil = _overlapTicks == 0 ? tick : tick + _overlapTicks;
        entry.HasOverlap = _overlapTicks > 0;
        _clock = Math.Max(_clock, tick);
        System.Threading.Interlocked.Increment(ref _applied);

        return new TransferResult(decision, AuthorityReject.None, true, decision.To, tick,
                                  RegionFrame.Build(decision.To, _radiusCm));
    }

    /// <summary>按 tick 推进：过期的重叠窗口在这里摘除。</summary>
    public void AdvanceTo(long tick)
    {
        if (tick < _clock)
            throw new ArgumentOutOfRangeException(nameof(tick), tick, $"时钟已经在 {_clock}，不许倒退");
        _clock = tick;
        foreach (var e in _entries.Values)
        {
            if (e.HasOverlap && e.OverlapUntil <= tick) e.HasOverlap = false;
        }
    }

    /// <summary>
    /// 还没过期的重叠旧区，没有则 null。
    /// <para>注意它<b>不改变区域层的可见集</b>：合法迁移的目标必然与旧区共边（§13.13），
    /// 所以旧区本来就靠 <see cref="EdgeNeighbours"/> 在订阅集里——重叠窗口在区域层是冗余的。
    /// 它真正的用处是"什么时候允许摘掉旧区的地形/预加载"这类时序，所以这里把它做成可观察的账，
    /// 而不是拿可见集去证明它有用（判据不能靠循环论证）。</para>
    /// </summary>
    public RegionKey? OverlapOf(long entityId)
    {
        if (!_entries.TryGetValue(entityId, out var e)) return null;
        if (!e.HasOverlap || e.OverlapUntil <= _clock) return null;
        return e.OverlapFrom;
    }

    public RegionKey? OwnerOf(long entityId) =>
        _entries.TryGetValue(entityId, out var e) ? e.Region : (RegionKey?)null;

    public PlanetVector3? GlobalOf(long entityId) =>
        _entries.TryGetValue(entityId, out var e) ? e.GlobalCm : (PlanetVector3?)null;

    public CellKey? CellOf(long entityId)
    {
        if (!_entries.TryGetValue(entityId, out var e)) return null;
        var frame = RegionFrame.Build(e.Region, _radiusCm);
        var local = frame.LocalFromGlobalCm(e.GlobalCm);
        return new CellKey(e.Region, FloorDiv(local.X, CellSizeCm), FloorDiv(local.Z, CellSizeCm));
    }

    /// <summary>订阅集 = 当前区域 + 它的共边邻居 + 还没过期的重叠旧区（§6.3 第 5 步、§6.4）。</summary>
    public IReadOnlyList<RegionKey> Subscriptions(long entityId)
    {
        if (!_entries.TryGetValue(entityId, out var e)) return Array.Empty<RegionKey>();
        var set = new List<RegionKey> { e.Region };
        foreach (var n in PlanetRegions.EdgeNeighbours(e.Region))
            if (!set.Contains(n)) set.Add(n);
        if (e.HasOverlap && e.OverlapUntil > _clock && !set.Contains(e.OverlapFrom))
            set.Add(e.OverlapFrom);
        return set;
    }

    /// <summary>能看见谁：对方的归属落在我任一订阅区域里就算看见（区域层 AOI）。</summary>
    public IReadOnlyList<long> VisibleEntities(long entityId)
    {
        if (!_entries.ContainsKey(entityId)) return Array.Empty<long>();
        var subs = Subscriptions(entityId);
        var list = new List<long>();
        foreach (var (id, e) in _entries)
        {
            if (id == entityId) continue;
            for (int i = 0; i < subs.Count; i++)
                if (subs[i].Equals(e.Region)) { list.Add(id); break; }
        }
        list.Sort();
        return list;
    }

    /// <summary>
    /// 全部实体记录的快照（持久化投影用）。返回的是新建列表，改它不影响权威表。
    /// <para>顺序不保证——所以恢复侧必须自己按 <see cref="EntitySnapshot.Tick"/> 排序，
    /// 不能指望这里给的就是可回放的次序（<see cref="TryBind"/> 会拒绝比时钟旧的落位）。</para>
    /// </summary>
    public List<EntitySnapshot> SnapshotEntities()
    {
        var list = new List<EntitySnapshot>(_entries.Count);
        foreach (var (id, e) in _entries)
            list.Add(new EntitySnapshot(id, e.Region, e.GlobalCm, e.Tick));
        return list;
    }

    public string Describe() => string.Format(CultureInfo.InvariantCulture,
        "authority entities={0} clock={1} level={2} overlap_ticks={3} applied={4} " +
        "rejected_geometry={5} rejected_authority={6}",
        _entries.Count, _clock, _level, _overlapTicks, Applied, RejectedGeometry, RejectedAuthority);

    private static int FloorDiv(double value, int size)
    {
        double q = Math.Floor(value / size);
        if (q > int.MaxValue || q < int.MinValue)
            throw new OverflowException($"局部坐标 {value:0} 换算格号时溢出");
        return (int)q;
    }
}

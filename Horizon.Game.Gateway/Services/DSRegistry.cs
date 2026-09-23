using System;
using System.Collections.Generic;
using System.Linq;

namespace Horizon.Game.Gateway.Services;

/// <summary>DS 注册表条目快照（供路由/监控消费）。</summary>
/// <param name="Region">DS 所在区域（阶段 6.3 P1⑥）；空串表示未声明区域——宽松模式下视为不限区（可服务任意区域网关），严格模式（<c>GatewayOptions.StrictEmptyRegionMode</c>）下仅服务空 region 网关。</param>
public sealed record DsEndpointInfo(
    string DsId,
    IReadOnlyList<long> ShardIds,
    string Host,
    int Port,
    DateTimeOffset LastHeartbeatUtc,
    string Region = "");

/// <summary>阶段 6.3 P1⑥ — <see cref="DSRegistry.TryResolve"/> 三态解析结果（供数据面精确分类回退原因）。</summary>
public enum DsResolveStatus
{
    /// <summary>该 shard 无任何已注册/心跳有效的 DS（含无 DS 注册与全部过期）。</summary>
    NoDs = 0,
    /// <summary>该 shard 有 DS，但全部不在请求的区域（跨区盲路由防护）。</summary>
    WrongRegion = 1,
    /// <summary>解析成功。</summary>
    Ok = 2,
}

/// <summary>
/// 阶段 2.5 — UE5 DS 注册表（DSRegistry）。
/// <para>
/// 职责：DS 进程注册其承载的 shard 集合 → 周期性心跳续约 → 网关按 shard 解析目标 DS →
/// 过期条目摘除。DS 侧（阶段 2.4，UE 进程）通过传输层调用 <see cref="RegisterOrUpdate"/> /
/// <see cref="Heartbeat"/> 维护存活状态；网关路由面（<see cref="DsSyncDataPlane"/>）通过
/// <see cref="TryResolve"/> 消费路由表。<see cref="PruneStale"/> 由宿主定期调用（测试可显式驱动），
/// 摘除超过 <c>staleAfter</c> 未心跳的 DS，防止故障 DS 残留造成路由黑洞。
/// </para>
/// <para>
/// 线程安全：所有公开方法内部加锁，可在网关多连接并发下安全使用。时钟可注入（测试用）。
/// </para>
/// </summary>
public sealed class DSRegistry
{
    private sealed class Entry
    {
        public string DsId;
        public List<long> ShardIds;
        public string Host;
        public int Port;
        public DateTimeOffset LastHeartbeatUtc;
        public string Region;

        public Entry(string dsId, List<long> shardIds, string host, int port, DateTimeOffset lastHeartbeatUtc, string region = "")
        {
            DsId = dsId;
            ShardIds = shardIds;
            Host = host;
            Port = port;
            LastHeartbeatUtc = lastHeartbeatUtc;
            Region = region ?? string.Empty;
        }
    }

    private readonly object _lock = new();
    private readonly Dictionary<string, Entry> _entries = new();
    private readonly TimeSpan _staleAfter;
    private readonly Func<DateTimeOffset> _clock;

    /// <summary>
    /// 创建注册表。
    /// </summary>
    /// <param name="staleAfter">心跳过期阈值；超过该时长未心跳的 DS 视为失效。默认 90s（3×30s 心跳间隔）。</param>
    /// <param name="clock">时钟提供者（测试注入用）。</param>
    public DSRegistry(TimeSpan? staleAfter = null, Func<DateTimeOffset>? clock = null)
    {
        _staleAfter = staleAfter ?? TimeSpan.FromSeconds(90);
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// 注册或更新 DS（幂等 upsert；已存在时刷新 shard 集合与心跳）。
    /// </summary>
    /// <param name="dsId">DS 唯一 ID。</param>
    /// <param name="shardIds">该 DS 承载的 shard 集合。</param>
    /// <param name="host">DS 主机。</param>
    /// <param name="port">DS 端口。</param>
    /// <param name="region">DS 所在区域（阶段 6.3 P1⑥）；空串 = 未声明区域（宽松模式不限区；严格模式仅服务空 region 网关）。</param>
    public void RegisterOrUpdate(string dsId, IReadOnlyCollection<long> shardIds, string host, int port, string? region = null)
    {
        if (string.IsNullOrWhiteSpace(dsId)) throw new ArgumentException("DsId 不能为空。", nameof(dsId));
        if (shardIds is null) throw new ArgumentNullException(nameof(shardIds));
        if (string.IsNullOrWhiteSpace(host)) throw new ArgumentException("Host 不能为空。", nameof(host));
        if (port <= 0 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port), "端口必须在 1–65535。");

        var now = _clock();
        lock (_lock)
        {
            if (_entries.TryGetValue(dsId, out var entry))
            {
                entry.ShardIds.Clear();
                entry.ShardIds.AddRange(shardIds.Distinct());
                entry.Host = host;
                entry.Port = port;
                entry.Region = region ?? string.Empty;
                entry.LastHeartbeatUtc = now;
            }
            else
            {
                _entries[dsId] = new Entry(dsId, shardIds.Distinct().ToList(), host, port, now, region ?? string.Empty);
            }
        }
    }

    /// <summary>续约心跳（DS 周期调用）。</summary>
    public void Heartbeat(string dsId)
    {
        lock (_lock)
        {
            if (_entries.TryGetValue(dsId, out var entry))
                entry.LastHeartbeatUtc = _clock();
        }
    }

    /// <summary>
    /// 从持久化快照恢复条目（网关重启/新实例冷启动，阶段 2.5+）。
    /// <para>
    /// 以条目**存储的** <see cref="DsEndpointInfo.LastHeartbeatUtc"/> 原样插入（不刷新为当前时间）：
    /// 过期条目由 <see cref="TryResolve"/> 的 staleness 检查天然排除（inert），并在下一轮
    /// <see cref="PruneStale"/> 被摘除；新鲜条目立即参与路由——消除「等 DS 下一轮心跳」的恢复空窗。
    /// 快照恢复的假阳性（DS 实际已死）由数据面熔断兜底：连接失败计入熔断计数，连续失败即临时摘除。
    /// </para>
    /// </summary>
    /// <param name="entries">快照条目列表（来自 <see cref="IDSnapshotStore.RestoreSnapshotAsync"/>）。</param>
    public void RestoreSnapshot(IReadOnlyList<DsEndpointInfo> entries)
    {
        if (entries is null) throw new ArgumentNullException(nameof(entries));
        lock (_lock)
        {
            foreach (var e in entries)
            {
                if (string.IsNullOrWhiteSpace(e.DsId)) continue;
                if (_entries.TryGetValue(e.DsId, out var entry))
                {
                    entry.ShardIds.Clear();
                    entry.ShardIds.AddRange(e.ShardIds.Distinct());
                    entry.Host = e.Host;
                    entry.Port = e.Port;
                    entry.Region = e.Region ?? string.Empty;
                    entry.LastHeartbeatUtc = e.LastHeartbeatUtc;
                }
                else
                {
                    _entries[e.DsId] = new Entry(e.DsId, e.ShardIds.Distinct().ToList(), e.Host, e.Port, e.LastHeartbeatUtc, e.Region ?? string.Empty);
                }
            }
        }
    }

    /// <summary>摘除 DS（DS 下线/故障转移时调用）。</summary>
    public void Unregister(string dsId)
    {
        lock (_lock)
        {
            _entries.Remove(dsId);
        }
    }

    /// <summary>
    /// 按 shard 解析目标 DS（不过滤区域）：返回最近心跳未过期、且承载该 shard 的 DS。
    /// 多个 DS 承载同一 shard（主备/双活）时取心跳最新者。<br/>
    /// 等价于 <c>TryResolve(shardId, region: null, out _)</c>（null = 不限区，向后兼容）。
    /// </summary>
    /// <param name="shardId">目标 shard ID。</param>
    /// <param name="endpoint">解析到的 DS 端点；失败时为 null。</param>
    /// <returns>true 表示存在可用 DS。</returns>
    public bool TryResolve(long shardId, out DsEndpointInfo endpoint)
    {
        var status = TryResolve(shardId, region: null, out endpoint);
        return status == DsResolveStatus.Ok;
    }

    /// <summary>
    /// 按 shard + 区域解析目标 DS（阶段 6.3 P1⑥ 跨区盲路由防护；宽松模式，向后兼容）。
    /// <para>
    /// 等价于 <c>TryResolve(shardId, region, strictEmptyRegion: false, out endpoint)</c>——
    /// 空 region DS 视为不限区（可服务任意区域网关）。需要严格模式（空区 DS 仅服务空 region 网关）
    /// 时请调用 4 参重载并传入 <c>GatewayOptions.StrictEmptyRegionMode</c>。
    /// </para>
    /// </summary>
    public DsResolveStatus TryResolve(long shardId, string? region, out DsEndpointInfo endpoint)
        => TryResolve(shardId, region, strictEmptyRegion: false, out endpoint);

    /// <summary>
    /// 按 shard + 区域解析目标 DS（阶段 6.3 P1⑥ 跨区盲路由防护 + 空 region 严格模式收紧）。
    /// <para>
    /// 语义：<paramref name="region"/> 为 null/空串时不限区（网关未设区，过滤不生效）；
    /// 否则仅返回区域匹配的 DS：
    /// <list type="bullet">
    /// <item><description><paramref name="strictEmptyRegion"/> = false（宽松）：DS 的 Region 为空串视为不限区
    /// （可服务任意区域）；非空则要求与 <paramref name="region"/> 完全一致。</description></item>
    /// <item><description><paramref name="strictEmptyRegion"/> = true（严格）：DS 的 Region 必须与
    /// <paramref name="region"/> 完全一致——未声明 region 的旧注册（空串）被排除，防盲路由到
    /// 实际部署于其他区域的 DS。</description></item>
    /// </list>
    /// 返回 <see cref="DsResolveStatus"/> 供数据面精确分类回退原因（<c>no_ds</c> vs <c>wrong_region</c>）：
    /// shard 有注册但全部被区域/严格规则排除时返回 <c>WrongRegion</c>（拓扑问题），无注册返回 <c>NoDs</c>。
    /// </para>
    /// </summary>
    /// <param name="shardId">目标 shard ID。</param>
    /// <param name="region">要求 DS 所属区域（网关的 <c>GatewayOptions.Region</c>）；null/空 = 不限区。</param>
    /// <param name="strictEmptyRegion">严格模式：true 时未声明 region 的 DS 仅服务空 region 网关（不匹配非空请求区）。</param>
    /// <param name="endpoint">解析到的 DS 端点；失败时为 null。</param>
    /// <returns>三态解析结果。</returns>
    public DsResolveStatus TryResolve(long shardId, string? region, bool strictEmptyRegion, out DsEndpointInfo endpoint)
    {
        var now = _clock();
        var wantRegion = string.IsNullOrWhiteSpace(region) ? null : region!.Trim();
        lock (_lock)
        {
            Entry? best = null;
            var anyInShard = false;
            foreach (var e in _entries.Values)
            {
                if (!e.ShardIds.Contains(shardId)) continue;
                if (now - e.LastHeartbeatUtc > _staleAfter) continue;
                anyInShard = true;
                // 区域过滤：请求区为空（网关未设区）不过滤；否则按模式判定匹配——
                // 宽松：空区 DS 不限区 + 同区 DS；严格：仅 Region 与请求区完全一致的 DS。
                if (wantRegion is not null)
                {
                    var dsRegion = (e.Region ?? string.Empty).Trim();
                    var matches = !strictEmptyRegion
                        ? string.IsNullOrEmpty(dsRegion) || string.Equals(dsRegion, wantRegion, StringComparison.Ordinal)
                        : string.Equals(dsRegion, wantRegion, StringComparison.Ordinal);
                    if (!matches) continue;
                }
                if (best is null || e.LastHeartbeatUtc > best.LastHeartbeatUtc) best = e;
            }
            if (best is null)
            {
                endpoint = null!;
                return anyInShard ? DsResolveStatus.WrongRegion : DsResolveStatus.NoDs;
            }
            endpoint = new DsEndpointInfo(best.DsId, best.ShardIds.ToList(), best.Host, best.Port, best.LastHeartbeatUtc, best.Region);
            return DsResolveStatus.Ok;
        }
    }

    /// <summary>
    /// 当前心跳有效（未过期）的 DS 数。<br/>
    /// 与 <see cref="TryResolve"/> 的存活判定同源（同一 <c>staleAfter</c> 阈值），
    /// 供维护服务（<see cref="DSRegistryMaintenanceHostedService"/>）上报存活数指标。
    /// </summary>
    /// <param name="staleAfter">本次使用的过期阈值；null 使用构造时的默认值。</param>
    /// <returns>存活 DS 数。</returns>
    public int CountAlive(TimeSpan? staleAfter = null)
    {
        var now = _clock();
        var threshold = staleAfter ?? _staleAfter;
        lock (_lock)
        {
            return _entries.Values.Count(e => now - e.LastHeartbeatUtc <= threshold);
        }
    }

    /// <summary>
    /// 摘除所有超过过期阈值未心跳的 DS。
    /// </summary>
    /// <param name="staleAfter">本次使用的过期阈值；null 使用构造时的默认值。</param>
    /// <returns>摘除的 DS 数量。</returns>
    public int PruneStale(TimeSpan? staleAfter = null)
    {
        var cutoff = _clock() - (staleAfter ?? _staleAfter);
        lock (_lock)
        {
            var stale = _entries.Values.Where(e => e.LastHeartbeatUtc < cutoff).Select(e => e.DsId).ToList();
            foreach (var id in stale) _entries.Remove(id);
            return stale.Count;
        }
    }

    /// <summary>当前注册表快照（监控/诊断/测试）。</summary>
    public IReadOnlyList<DsEndpointInfo> Snapshot()
    {
        lock (_lock)
        {
            return _entries.Values
                .Select(e => new DsEndpointInfo(e.DsId, e.ShardIds.ToList(), e.Host, e.Port, e.LastHeartbeatUtc, e.Region))
                .ToList();
        }
    }
}

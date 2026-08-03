using System;
using System.Collections.Generic;
using System.Linq;

namespace Horizon.Game.Core.World;

/// <summary>
/// Zone 分片 AOI（Area Of Interest）状态管理器（P2-b）。<br/>
/// 纯数据/逻辑层：维护 <c>ChunkMortonKey → HashSet&lt;sessionId&gt;</c> 的双向映射，
/// 支持按 chunk 扇出、按会话退订、按会话查询订阅列表。
/// </summary>
/// <remarks>
/// - 不依赖 Orleans，可以被 <c>ZoneShardGrain</c> 薄封装，也可在单测中独立使用。<br/>
/// - 单线程访问；<c>ZoneShardGrain</c> 的 turn-based 执行保证串行。<br/>
/// - 扇出操作返回**订阅该 chunk 的 sessionId 列表**，Gateway/grain 侧按此列表分派。
/// </remarks>
public sealed class ZoneShardAoi
{
    private readonly Dictionary<ulong, HashSet<long>> _chunkToSessions = new();
    private readonly Dictionary<long, HashSet<ulong>> _sessionToChunks = new();

    /// <summary>当前订阅者总数（会话数）。</summary>
    public int SessionCount => _sessionToChunks.Count;

    /// <summary>当前被订阅的 chunk 数量。</summary>
    public int ChunkCount => _chunkToSessions.Count;

    /// <summary>
    /// 给会话 <paramref name="sessionId"/> 订阅一组 chunk。重复订阅自动去重。
    /// </summary>
    /// <returns>实际新增的 chunk 订阅条数（已存在的不计）。</returns>
    public int Subscribe(long sessionId, ReadOnlySpan<ulong> mortonKeys)
    {
        if (mortonKeys.IsEmpty) return 0;
        if (!_sessionToChunks.TryGetValue(sessionId, out var sessionChunks))
        {
            sessionChunks = new HashSet<ulong>();
            _sessionToChunks[sessionId] = sessionChunks;
        }
        int added = 0;
        foreach (var key in mortonKeys)
        {
            if (!_chunkToSessions.TryGetValue(key, out var sessions))
            {
                sessions = new HashSet<long>();
                _chunkToSessions[key] = sessions;
            }
            if (sessions.Add(sessionId)) added++;
            sessionChunks.Add(key);
        }
        return added;
    }

    /// <summary>
    /// 给会话 <paramref name="sessionId"/> 退订一组 chunk；未订阅的自动忽略。
    /// </summary>
    /// <returns>实际被移除的订阅条数。</returns>
    public int Unsubscribe(long sessionId, ReadOnlySpan<ulong> mortonKeys)
    {
        if (mortonKeys.IsEmpty) return 0;
        if (!_sessionToChunks.TryGetValue(sessionId, out var sessionChunks)) return 0;

        int removed = 0;
        foreach (var key in mortonKeys)
        {
            if (_chunkToSessions.TryGetValue(key, out var sessions) && sessions.Remove(sessionId))
            {
                removed++;
                if (sessions.Count == 0) _chunkToSessions.Remove(key);
            }
            sessionChunks.Remove(key);
        }
        if (sessionChunks.Count == 0) _sessionToChunks.Remove(sessionId);
        return removed;
    }

    /// <summary>
    /// 会话整体离线：移除其所有订阅。
    /// </summary>
    /// <returns>被移除的订阅条数。</returns>
    public int RemoveSession(long sessionId)
    {
        // 修复（NullReferenceException — RemoveSession 第 87 行 NRE）：
        // 在极端场景下（grain 激活/反激活时序、内存压力）可能出现字典状态不一致。
        // 增加防御性 null 检查，避免 NRE 中断清理流程导致会话残留。
        if (_sessionToChunks == null || _chunkToSessions == null) return 0;
        if (!_sessionToChunks.TryGetValue(sessionId, out var chunks) || chunks == null) return 0;
        int removed = 0;
        foreach (var key in chunks)
        {
            if (_chunkToSessions.TryGetValue(key, out var sessions) && sessions != null && sessions.Remove(sessionId))
            {
                removed++;
                if (sessions.Count == 0) _chunkToSessions.Remove(key);
            }
        }
        _sessionToChunks.Remove(sessionId);
        return removed;
    }

    /// <summary>
    /// 返回订阅了 <paramref name="mortonKey"/> 的 sessionId 只读视图。不包含任何分配；调用方不得修改。
    /// 未订阅则返回空集合。
    /// </summary>
    public IReadOnlyCollection<long> GetSubscribers(ulong mortonKey)
    {
        if (_chunkToSessions.TryGetValue(mortonKey, out var s)) return s;
        return Array.Empty<long>();
    }

    /// <summary>
    /// 返回当前所有已订阅的 sessionId 只读视图（跨所有 chunk）。调用方不得修改。
    /// 用于实体位置未知时的回退广播（P8-8.2）。
    /// </summary>
    public IReadOnlyCollection<long> GetAllSubscribers() => _sessionToChunks.Keys;

    /// <summary>
    /// 返回会话当前订阅的所有 chunk 的只读视图；未注册则返回空。
    /// </summary>
    public IReadOnlyCollection<ulong> GetSubscriptions(long sessionId)
    {
        if (_sessionToChunks.TryGetValue(sessionId, out var c)) return c;
        return Array.Empty<ulong>();
    }

    /// <summary>
    /// 批量扇出：给定一组 (mortonKey, payloadIndex) 目标，
    /// 返回 "sessionId → payloadIndex 列表" 的映射，供上层一次性聚合推送。
    /// 空结果（无订阅者）会被跳过。
    /// </summary>
    /// <param name="targets">要广播的 (chunk, 载荷下标) 对。</param>
    public Dictionary<long, List<int>> FanOut(ReadOnlySpan<(ulong MortonKey, int PayloadIndex)> targets)
    {
        var result = new Dictionary<long, List<int>>();
        foreach (var (key, payloadIndex) in targets)
        {
            if (!_chunkToSessions.TryGetValue(key, out var sessions) || sessions.Count == 0)
                continue;
            foreach (var sid in sessions)
            {
                if (!result.TryGetValue(sid, out var list))
                {
                    list = new List<int>();
                    result[sid] = list;
                }
                list.Add(payloadIndex);
            }
        }
        return result;
    }

    /// <summary>
    /// 诊断/调试用快照：返回整个订阅表的副本。
    /// </summary>
    public IReadOnlyDictionary<ulong, IReadOnlyCollection<long>> Snapshot() =>
        _chunkToSessions.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyCollection<long>)kv.Value.ToArray());
}

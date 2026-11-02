using System;
using System.Collections.Generic;
using System.Globalization;
using Horizon.QuestCli;
using StackExchange.Redis;

namespace Horizon.MonsterCli;

/// <summary>草稿索引条目。</summary>
public sealed class MonsterDraftEntry
{
    public int MonsterId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset LastWrite { get; set; }
    public long TtlMilliseconds { get; set; }
}

/// <summary>
/// 怪物草稿的 Redis 落点（键 <c>monster:draft:{id}</c>，索引 ZSET <c>monster:drafts</c>，TTL 72h）。
/// <para>
/// <b>为什么草稿不进 SQL</b>：一份怪物草稿是六张表的组合体，未校验通过的半份进库会立刻被
/// 运行时装配器读走（它只按 publish_state 过滤，读不懂"这是半成品"）。Redis 让"编辑期"与
/// "运行期"物理分开，publish 是那唯一的一次跨线写入。
/// </para>
/// <para>连接候选口径复用 <see cref="ConnectionConfig.RedisCandidates"/>（与任务编辑器同一份，避免两个编辑器连两个库）。</para>
/// </summary>
public sealed class MonsterDraftStore : IDisposable
{
    public const string KeyPrefix = "monster:draft:";
    public const string IndexKey = "monster:drafts";
    public static readonly TimeSpan DraftTtl = TimeSpan.FromHours(72);

    private readonly ConnectionMultiplexer _redis;

    public MonsterDraftStore(ConnectionMultiplexer redis) => _redis = redis;

    /// <summary>尝试连上任一候选 Redis；失败时 <paramref name="error"/> 带上每个候选的错误（不返回 null 却不报错）。</summary>
    public static bool TryConnect(out MonsterDraftStore? store, out string source, out string error)
    {
        store = null;
        source = string.Empty;
        var failures = new List<string>();
        foreach (var (candidate, origin) in ConnectionConfig.RedisCandidates())
        {
            try
            {
                var mux = ConnectionMultiplexer.Connect(candidate);
                store = new MonsterDraftStore(mux);
                source = origin + " → " + candidate;
                error = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                failures.Add(origin + ": " + ex.GetBaseException().Message);
            }
        }
        error = "所有 Redis 候选都连不上：" + string.Join(" | ", failures);
        return false;
    }

    public static string BodyKey(int monsterId) => KeyPrefix + monsterId.ToString(CultureInfo.InvariantCulture);

    public void Save(MonsterDraft draft)
    {
        var db = _redis.GetDatabase();
        var json = draft.ToJson();
        db.StringSet(BodyKey(draft.MonsterId), json, DraftTtl);
        // score 用最后写入毫秒：列表按"最近编辑"排序，且 TTL 死掉的条目要能顺手从索引里清掉。
        db.SortedSetAdd(IndexKey, draft.MonsterId.ToString(CultureInfo.InvariantCulture),
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    public string? GetRaw(int monsterId) => _redis.GetDatabase().StringGet(BodyKey(monsterId));

    public MonsterDraft? Get(int monsterId) => MonsterDraft.FromJson(GetRaw(monsterId) ?? string.Empty);

    public List<MonsterDraftEntry> List()
    {
        var db = _redis.GetDatabase();
        var members = db.SortedSetRangeByRankWithScores(IndexKey, 0, -1, Order.Descending);
        var outList = new List<MonsterDraftEntry>(members.Length);
        foreach (var m in members)
        {
            if (!int.TryParse(m.Element.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)) continue;
            var ttl = db.KeyTimeToLive(BodyKey(id));
            if (ttl.HasValue && ttl.Value <= TimeSpan.Zero)
            {
                // 主体已过期，索引里的这条是僵尸；留着会让列表显示"有草稿"而 get 却取不到。
                db.SortedSetRemove(IndexKey, m.Element);
                continue;
            }
            var body = db.StringGet(BodyKey(id));
            var draft = body.HasValue ? MonsterDraft.FromJson(body.ToString()) : null;
            outList.Add(new MonsterDraftEntry
            {
                MonsterId = id,
                Name = draft?.Name ?? "(正文解析失败)",
                LastWrite = DateTimeOffset.FromUnixTimeMilliseconds((long)m.Score),
                TtlMilliseconds = ttl.HasValue ? (long)ttl.Value.TotalMilliseconds : -1,
            });
        }
        return outList;
    }

    public bool Delete(int monsterId)
    {
        var db = _redis.GetDatabase();
        var removed = db.KeyDelete(BodyKey(monsterId));
        db.SortedSetRemove(IndexKey, monsterId.ToString(CultureInfo.InvariantCulture));
        return removed;
    }

    public void Dispose() => _redis.Dispose();
}

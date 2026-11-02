using System.Globalization;
using Horizon.QuestCli;
using StackExchange.Redis;

namespace Horizon.ItemCli;

/// <summary>草稿索引条目。</summary>
public sealed class ItemDraftEntry
{
    public int TemplateId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset LastWrite { get; set; }
    public long TtlMilliseconds { get; set; }
}

/// <summary>
/// 物品草稿的 Redis 落点（键 <c>item:draft:{id}</c>，索引 ZSET <c>item:drafts</c>，TTL 72h）。
/// <para>
/// <b>为什么草稿不进 SQL</b>：一份物品草稿是十张表的组合体，未校验通过的半份进库会立刻被
/// 导出器读走（它只按 publish_state 过滤，读不懂"这是半成品"）。
/// </para>
/// <para>连接候选口径复用 <see cref="ConnectionConfig.RedisCandidates"/>，避免两个编辑器连两个库。</para>
/// </summary>
public sealed class ItemDraftStore : IDisposable
{
    public const string KeyPrefix = "item:draft:";
    public const string IndexKey = "item:drafts";
    public static readonly TimeSpan DraftTtl = TimeSpan.FromHours(72);

    private readonly ConnectionMultiplexer _redis;

    public ItemDraftStore(ConnectionMultiplexer redis) => _redis = redis;

    public static bool TryConnect(out ItemDraftStore? store, out string source, out string error)
    {
        store = null;
        source = string.Empty;
        var failures = new List<string>();
        foreach (var (candidate, origin) in ConnectionConfig.RedisCandidates())
        {
            try
            {
                var mux = ConnectionMultiplexer.Connect(candidate);
                store = new ItemDraftStore(mux);
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

    public static string BodyKey(int templateId) => KeyPrefix + templateId.ToString(CultureInfo.InvariantCulture);

    public void Save(ItemDraft draft)
    {
        var db = _redis.GetDatabase();
        db.StringSet(BodyKey(draft.TemplateId), draft.ToJson(), DraftTtl);
        db.SortedSetAdd(IndexKey, draft.TemplateId.ToString(CultureInfo.InvariantCulture),
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    public string? GetRaw(int templateId) => _redis.GetDatabase().StringGet(BodyKey(templateId));

    public List<ItemDraftEntry> List()
    {
        var db = _redis.GetDatabase();
        var members = db.SortedSetRangeByRankWithScores(IndexKey, 0, -1, Order.Descending);
        var outList = new List<ItemDraftEntry>(members.Length);
        foreach (var m in members)
        {
            if (!int.TryParse(m.Element.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)) continue;
            var ttl = db.KeyTimeToLive(BodyKey(id));
            if (ttl.HasValue && ttl.Value <= TimeSpan.Zero)
            {
                // 主体已过期，索引里这条是僵尸；留着会让列表显示"有草稿"而 get 却取不到。
                db.SortedSetRemove(IndexKey, m.Element);
                continue;
            }
            var body = db.StringGet(BodyKey(id));
            var draft = body.HasValue ? ItemDraft.FromJson(body.ToString()) : null;
            outList.Add(new ItemDraftEntry
            {
                TemplateId = id,
                Name = draft?.Name ?? "(正文解析失败)",
                LastWrite = DateTimeOffset.FromUnixTimeMilliseconds((long)m.Score),
                TtlMilliseconds = ttl.HasValue ? (long)ttl.Value.TotalMilliseconds : -1,
            });
        }
        return outList;
    }

    public bool Delete(int templateId)
    {
        var db = _redis.GetDatabase();
        var removed = db.KeyDelete(BodyKey(templateId));
        db.SortedSetRemove(IndexKey, templateId.ToString(CultureInfo.InvariantCulture));
        return removed;
    }

    public void Dispose() => _redis.Dispose();
}

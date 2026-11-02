using System.Globalization;
using Horizon.QuestCli;
using StackExchange.Redis;

namespace Horizon.PetCli;

/// <summary>草稿索引条目。</summary>
public sealed class PetDraftEntry
{
    public int SpeciesId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset LastWrite { get; set; }
    public long TtlMilliseconds { get; set; }
}

/// <summary>
/// 宠物草稿的 Redis 落点（键 <c>pet:draft:{speciesId}</c>，索引 ZSET <c>pet:drafts</c>，TTL 72h）。
/// <para>
/// <b>为什么草稿不进 SQL</b>：一份宠物草稿是九张表的组合体，未校验通过的半份进库会立刻被
/// 运行时装配器读走（它只按 publish_state 过滤，读不懂"这是半成品"）。
/// </para>
/// <para>连接候选口径复用 <see cref="ConnectionConfig.RedisCandidates"/>，避免两个编辑器连两个库。</para>
/// </summary>
public sealed class PetDraftStore : IDisposable
{
    public const string KeyPrefix = "pet:draft:";
    public const string IndexKey = "pet:drafts";
    public static readonly TimeSpan DraftTtl = TimeSpan.FromHours(72);

    private readonly ConnectionMultiplexer _redis;

    public PetDraftStore(ConnectionMultiplexer redis) => _redis = redis;

    public static bool TryConnect(out PetDraftStore? store, out string source, out string error)
    {
        store = null;
        source = string.Empty;
        var failures = new List<string>();
        foreach (var (candidate, origin) in ConnectionConfig.RedisCandidates())
        {
            try
            {
                var mux = ConnectionMultiplexer.Connect(candidate);
                store = new PetDraftStore(mux);
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

    public static string BodyKey(int speciesId) => KeyPrefix + speciesId.ToString(CultureInfo.InvariantCulture);

    public void Save(PetDraft draft)
    {
        var db = _redis.GetDatabase();
        db.StringSet(BodyKey(draft.SpeciesId), draft.ToJson(), DraftTtl);
        // score 用最后写入毫秒：列表按"最近编辑"排序，且 TTL 死掉的条目要能顺手从索引里清掉。
        db.SortedSetAdd(IndexKey, draft.SpeciesId.ToString(CultureInfo.InvariantCulture),
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    public string? GetRaw(int speciesId) => _redis.GetDatabase().StringGet(BodyKey(speciesId));

    public PetDraft? Get(int speciesId) => PetDraft.FromJson(GetRaw(speciesId) ?? string.Empty);

    public List<PetDraftEntry> List()
    {
        var db = _redis.GetDatabase();
        var members = db.SortedSetRangeByRankWithScores(IndexKey, 0, -1, Order.Descending);
        var outList = new List<PetDraftEntry>(members.Length);
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
            var draft = body.HasValue ? PetDraft.FromJson(body.ToString()) : null;
            outList.Add(new PetDraftEntry
            {
                SpeciesId = id,
                Name = draft?.Name ?? "(正文解析失败)",
                LastWrite = DateTimeOffset.FromUnixTimeMilliseconds((long)m.Score),
                TtlMilliseconds = ttl.HasValue ? (long)ttl.Value.TotalMilliseconds : -1,
            });
        }
        return outList;
    }

    public bool Delete(int speciesId)
    {
        var db = _redis.GetDatabase();
        var removed = db.KeyDelete(BodyKey(speciesId));
        db.SortedSetRemove(IndexKey, speciesId.ToString(CultureInfo.InvariantCulture));
        return removed;
    }

    public void Dispose() => _redis.Dispose();
}

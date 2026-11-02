using System;
using System.Collections.Generic;
using System.Linq;
using StackExchange.Redis;
using Horizon.Game.Sim.Quests;

namespace Horizon.QuestCli;

/// <summary>Redis 里一份草稿的摘要（给"草稿清单"用；正文按需再取）。</summary>
public sealed class QuestDraftEntry
{
    public int QuestId { get; set; }
    public string QuestName { get; set; } = string.Empty;
    public DateTimeOffset LastWrite { get; set; }

    /// <summary>剩余有效期（毫秒）。Redis 侧过期后这条就会消失，不存在"过期但还在"的状态。</summary>
    public long TtlMilliseconds { get; set; }
}

/// <summary>
/// 草稿层：**编辑中的任务只住 Redis**，SQL Server 只在"发布"那一刻被写。
/// <para>
/// 为什么这么分：任务模板的编辑是高频、多轮、要反复试的状态，而这个表同时是**权威源**——
/// 每敲一次保存就往里写一行，等于让"没定稿的东西"和"线上正在跑的东西"用同一条通路，
/// 于是既要靠状态位区分、又会在版本号上互相推挤。分层的代价也很清楚：报错必须说清
/// "这条还在草稿里（未落库）"，不能让作者以为已经上线。
/// </para>
/// <para>
/// 键布局：正文 <c>quest:draft:{id}</c>（string，值为 <see cref="QuestDraft.ToJson"/> 的原文，
/// 与 validate/upsert 用的是**同一份序列化**，所以草稿能原样喂给校验器）；索引 <c>quest:drafts</c>
/// （ZSET，score = 最后写入的 ticks）。索引会因 TTL 先行删除而残留，所以读取时**惰性剔除**——
/// 索引只用来排序，不作为存在性判据。
/// </para>
/// </summary>
public sealed class QuestDraftStore : IDisposable
{
    /// <summary>正文键前缀。</summary>
    public const string KeyPrefix = "quest:draft:";

    /// <summary>索引键（ZSET）。</summary>
    public const string IndexKey = "quest:drafts";

    /// <summary>草稿存活 72 小时；**每次保存续期**（编辑中的草稿不会因为"停手"而消失）。</summary>
    public static readonly TimeSpan DraftTtl = TimeSpan.FromHours(72);

    private readonly ConnectionMultiplexer _redis;

    public QuestDraftStore(ConnectionMultiplexer redis) => _redis = redis;

    /// <summary>
    /// 依次尝试候选连接串，**每个都必须打一次 PING 才算可用**。
    /// <para>
    /// 为什么非要真打：<c>abortConnect=false</c> 时 <c>ConnectionMultiplexer.Connect()</c> 无论目标通不通
    /// 都会返回一个对象，所以"Connect 没抛异常"这件事本身不构成"连上了"。只有 PING 有应答，
    /// 才能把"能用的 Redis"和"配置里写了一个不存在的地址"分开——否则草稿层会以一个假可用的状态挂在那里，
    /// 作者点保存时才知道不对。
    /// </para>
    /// </summary>
    public static bool TryConnect(out QuestDraftStore? store, out string source, out string error)
    {
        var tried = new List<string>();

        foreach (var (connectionString, origin) in ConnectionConfig.RedisCandidates())
        {
            try
            {
                var options = ConfigurationOptions.Parse(connectionString);
                options.ConnectTimeout = 3000;
                options.AbortOnConnectFail = false;
                var redis = ConnectionMultiplexer.Connect(options);
                // 真应答才算数：这里是唯一的可用性判据（Ping 抛异常就落到 catch，试下一个候选）。
                redis.GetDatabase().Ping();

                store = new QuestDraftStore(redis);
                source = origin;
                error = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                tried.Add(origin + "（" + ex.GetBaseException().Message + "）");
            }
        }

        store = null;
        source = string.Empty;
        error = "连不上任何 Redis。试过：" + string.Join("；", tried) +
                "。在 .env 里补 GATEWAY_REDIS_CONNECTION_STRING 或设 HORIZON_QUEST_REDIS 环境变量。";
        return false;
    }

    public static string BodyKey(int questId) => KeyPrefix + questId.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>写草稿并续期到 72 小时。</summary>
    public void Save(QuestDraft draft)
    {
        var db = _redis.GetDatabase();
        var now = DateTimeOffset.UtcNow;
        db.StringSet(BodyKey(draft.QuestId), draft.ToJson(), DraftTtl);
        db.SortedSetAdd(IndexKey, draft.QuestId, now.ToUnixTimeMilliseconds());
    }

    /// <summary>取草稿正文；不存在（或已过期）返回 null。</summary>
    public QuestDraft? Get(int questId)
    {
        var json = _redis.GetDatabase().StringGet(BodyKey(questId));
        if (json.IsNullOrEmpty) return null;
        return QuestDraft.TryParse(json.ToString(), out _);
    }

    /// <summary>取原始 JSON（核验与排错时要看原文，而不是被反序列化抹平后的对象）。</summary>
    public string? GetRaw(int questId)
    {
        var json = _redis.GetDatabase().StringGet(BodyKey(questId));
        return json.IsNullOrEmpty ? null : json.ToString();
    }

    /// <summary>列出未过期的草稿（按最后修改倒序）。顺手把索引里已经被 TTL 删掉的 id 剔掉。</summary>
    public List<QuestDraftEntry> List()
    {
        var db = _redis.GetDatabase();
        var entries = new List<QuestDraftEntry>();
        var stale = new List<RedisValue>();

        foreach (var member in db.SortedSetRangeByScore(IndexKey, order: Order.Descending))
        {
            if (!int.TryParse(member.ToString(), out var questId)) continue;

            var json = db.StringGet(BodyKey(questId));
            if (json.IsNullOrEmpty)
            {
                // 正文已被 Redis 按 TTL 清掉，索引项就是垃圾：这里剔除，不让它出现在清单里。
                stale.Add(member);
                continue;
            }

            var draft = QuestDraft.TryParse(json.ToString(), out _);
            var ttl = db.KeyTimeToLive(BodyKey(questId));
            entries.Add(new QuestDraftEntry
            {
                QuestId = questId,
                QuestName = draft?.QuestName ?? string.Empty,
                LastWrite = DateTimeOffset.FromUnixTimeMilliseconds((long)db.SortedSetScore(IndexKey, member).GetValueOrDefault()),
                TtlMilliseconds = ttl?.TotalMilliseconds is double ms ? (long)ms : 0L,
            });
        }

        if (stale.Count > 0) db.SortedSetRemove(IndexKey, stale.ToArray());
        return entries;
    }

    /// <summary>删除草稿（发布成功后调用：内容权威权交给 SQL，草稿即刻退场）。</summary>
    public bool Delete(int questId)
    {
        var db = _redis.GetDatabase();
        var removed = db.KeyDelete(BodyKey(questId));
        db.SortedSetRemove(IndexKey, questId);
        return removed;
    }

    public void Dispose() => _redis.Dispose();
}

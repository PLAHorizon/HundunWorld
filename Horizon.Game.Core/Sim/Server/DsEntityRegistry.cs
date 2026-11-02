using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Horizon.Game.Sim.Quests;

namespace Horizon.Game.Core.Sim.Server;

/// <summary>
/// 网关侧的**世界实体身份注册表**：DS 上行的 <see cref="DsEntityDescriptor"/> 落在这里，
/// 使网关与业务层能回答"entityId 是什么 / 玩家附近有哪些可交互对象"。
/// <para>
/// <b>为什么必须由 DS 上报</b>：这份映射只存在于 <c>DsWorldSession._monstersById</c>，
/// 下行同步包（<c>EntityDelta</c>）不带模板 id，网关与客户端都不可能自己算出来。
/// 有了它，任务侧的"与某 NPC 对话"才不必采信客户端自报的模板 id——客户端只报
/// <b>实体 id</b>，身份由本表解出，报不出表里存在的 id 就是不成立的事件。
/// </para>
/// <para>
/// <b>分片与生命周期</b>：按 shard 分片（一个 DS 承载若干 shard），条目随 DS 上行的
/// <c>Removed</c> 淘汰；DS 摘除/重连时整片 <see cref="ClearShard"/>，重连后由 DS 全量补发，
/// 因此不会残留"上个 DS 的鬼对象"。
/// </para>
/// <para>
/// <b>位置新鲜度</b>：条目记录的是**生成时刻**的位置。NPC（脚本 <c>Npc</c>）被强制
/// <c>MoveSpeed=0</c> 站定不动，这个近似是准的；会游荡的怪物只适合做粗粒度邻近判断，
/// 需要精确距离的判定应回到 DS 内做。
/// </para>
/// </summary>
public sealed class DsEntityRegistry
{
    /// <summary>一个实体条目的不可变快照（读侧拿到的都是快照，避免边遍历边改）。</summary>
    public sealed class Entry
    {
        /// <summary>运行时实体 id。</summary>
        public ulong EntityId { get; init; }

        /// <summary>DS 生成类别原值（0 = 静态刷怪点怪物）。</summary>
        public byte ScriptedKind { get; init; }

        /// <summary>模板 id。</summary>
        public int TemplateId { get; init; }

        /// <summary>生成点 id（脚本点在负数域）。</summary>
        public int SpawnPointId { get; init; }

        /// <summary>X（Flax Y-up）。</summary>
        public float X { get; init; }

        /// <summary>Y（Flax 上下）。</summary>
        public float Y { get; init; }

        /// <summary>Z（Flax 前后）。</summary>
        public float Z { get; init; }

        /// <summary>任务语义下的类别。</summary>
        public QuestEntityKind QuestKind => ToQuestKind(ScriptedKind);
    }

    private sealed class ShardBook
    {
        /// <summary>读写共用一把锁：上行的批量 apply 与请求侧的邻近查询都低频。</summary>
        public readonly object Gate = new();

        public readonly Dictionary<ulong, Entry> ByEntityId = new();
    }

    private readonly ConcurrentDictionary<long, ShardBook> _shards = new();

    /// <summary>累计收到的条目数（含 Removed），供对账与测试断言。</summary>
    public long AppliedCount { get; private set; }

    /// <summary>DS 的 <c>ScriptedKind</c> 原值 → 任务语义类别。</summary>
    /// <remarks>
    /// <see cref="Horizon.Game.Message.Network.ScriptedKind.Event"/> 归入 <see cref="QuestEntityKind.Prop"/>：
    /// 脚本事件实体在任务语义里就是"世界里的一个物件"，而不是可击杀的怪。
    /// </remarks>
    public static QuestEntityKind ToQuestKind(byte scriptedKind) => scriptedKind switch
    {
        2 => QuestEntityKind.Npc,     // ScriptedKind.Npc
        3 => QuestEntityKind.Prop,    // ScriptedKind.Event
        4 => QuestEntityKind.Zone,    // ScriptedKind.TriggerZone
        _ => QuestEntityKind.Monster, // 0 = 静态点怪物、1 = ScriptedKind.Monster
    };

    /// <summary>应用一批描述符（同一 shard）。null/空批次直接返回。</summary>
    public void Apply(DsEntityDescriptorBatch? batch)
    {
        if (batch?.Descriptors is null || batch.Descriptors.Length == 0) return;

        var book = _shards.GetOrAdd(batch.ShardId, _ => new ShardBook());
        lock (book.Gate)
        {
            for (var i = 0; i < batch.Descriptors.Length; i++)
            {
                var d = batch.Descriptors[i];
                if (d.Removed)
                {
                    book.ByEntityId.Remove(d.EntityId);
                    continue;
                }

                book.ByEntityId[d.EntityId] = new Entry
                {
                    EntityId = d.EntityId,
                    ScriptedKind = d.ScriptedKind,
                    TemplateId = d.TemplateId,
                    SpawnPointId = d.SpawnPointId,
                    X = d.X,
                    Y = d.Y,
                    Z = d.Z,
                };
            }
        }

        AppliedCount += batch.Descriptors.Length;
    }

    /// <summary>整片丢弃（DS 摘除时调用；重连后由 DS 全量补发）。</summary>
    public void ClearShard(long shardId)
    {
        if (!_shards.TryGetValue(shardId, out var book)) return;
        lock (book.Gate) book.ByEntityId.Clear();
    }

    /// <summary>某 shard 当前登记的实体数。</summary>
    public int Count(long shardId)
    {
        if (!_shards.TryGetValue(shardId, out var book)) return 0;
        lock (book.Gate) return book.ByEntityId.Count;
    }

    /// <summary>按实体 id 解出权威身份。false = 该 id 在本 shard 世界里不存在。</summary>
    public bool TryResolve(long shardId, ulong entityId, out Entry entry)
    {
        entry = null!;
        if (!_shards.TryGetValue(shardId, out var book)) return false;
        lock (book.Gate) return book.ByEntityId.TryGetValue(entityId, out entry!);
    }

    /// <summary>
    /// 邻近查询：以 (<paramref name="flaxX"/>;<paramref name="flaxY"/>;<paramref name="flaxZ"/>) 为圆心，
    /// 在**同一 Flax Y-up 空间**里取半径 <paramref name="radius"/> 内、且 <paramref name="accept"/> 接受的条目。
    /// <para>
    /// 调用方注意换轴：角色位置缓存（<c>ICharacterGrain.GetLastPositionAsync</c>）是 ECS Z-up，
    /// 本表是 Flax Y-up，比较前必须统一（<c>flaxX=ecsX, flaxY=ecsZ, flaxZ=ecsY</c>）。
    /// </para>
    /// </summary>
    public List<Entry> FindNear(long shardId, float flaxX, float flaxY, float flaxZ, float radius,
        Func<Entry, bool>? accept = null)
    {
        var hits = new List<Entry>();
        if (!_shards.TryGetValue(shardId, out var book)) return hits;
        if (radius < 0f) return hits;

        var radiusSq = radius * radius;
        lock (book.Gate)
        {
            foreach (var entry in book.ByEntityId.Values)
            {
                var dx = entry.X - flaxX;
                var dy = entry.Y - flaxY;
                var dz = entry.Z - flaxZ;
                if (dx * dx + dy * dy + dz * dz > radiusSq) continue;
                if (accept is not null && !accept(entry)) continue;
                hits.Add(entry);
            }
        }

        // 固定顺序：结果进 UI/协议时，同一世界状态必须产出同一份列表。
        hits.Sort((a, b) => a.EntityId.CompareTo(b.EntityId));
        return hits;
    }
}

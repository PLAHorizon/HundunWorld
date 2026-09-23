using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Horizon.Game.Message.Network;
using Orleans;

namespace Horizon.Game.Core.Sim.Server;

/// <summary>
/// Orleans 适配器集合（MMORPG 开发计划 M4.3/M4.4 收尾）——
/// 把 <see cref="ILootTableConfigSource"/> / <see cref="IInventoryWriter"/> /
/// <see cref="IQuestProgressWriter"/> 落到真实 Grain 调用上。
/// <para>
/// 这些类**只做协议转换**（内核结构 ↔ Orleans 契约），不含业务判断；
/// 业务判断（缓存、确定性掷骰、队列、路由）都在
/// <see cref="OrleansLootProvider"/> / <see cref="OrleansGameplayEventSink"/> 里，
/// 因此可以在没有 Orleans 集群的情况下被完整单测。
/// </para>
/// <para>
/// 在此之前 <c>ILootTableGrain</c> / <c>IInventoryGrain</c> / 玩家维度 <c>IQuestGrain</c>
/// **全仓没有任何生产调用方**——它们只是被设计出来却从未接线（与 <c>DSRegistry</c> 同类问题）。
/// 本文件是它们的首个真实调用方。
/// </para>
/// </summary>
public sealed class OrleansLootTableConfigSource : ILootTableConfigSource
{
    private readonly IClusterClient _clusterClient;

    /// <summary>创建适配器。</summary>
    /// <param name="clusterClient">Orleans 集群客户端。</param>
    public OrleansLootTableConfigSource(IClusterClient clusterClient)
    {
        _clusterClient = clusterClient ?? throw new ArgumentNullException(nameof(clusterClient));
    }

    /// <inheritdoc />
    public async Task<LootTableSnapshot?> GetLootTableAsync(int lootTableId, CancellationToken cancellationToken = default)
    {
        if (lootTableId <= 0) return null;

        var grain = _clusterClient.GetGrain<Horizon.Orleans.Interface.World.ILootTableGrain>(lootTableId);
        var config = await grain.GetConfigAsync().ConfigureAwait(false);
        if (config is null) return null;

        var entries = config.Entries ?? Array.Empty<Horizon.Orleans.Interface.World.LootEntry>();
        var snapshot = new LootTableSnapshot
        {
            TableId = config.TableId,
            Entries = new LootEntrySnapshot[entries.Length],
        };

        for (var i = 0; i < entries.Length; i++)
        {
            var e = entries[i];
            snapshot.Entries[i] = new LootEntrySnapshot
            {
                ItemId = e.ItemId,
                DropRate = e.DropRate,
                MinCount = e.MinCount,
                MaxCount = e.MaxCount,
                Rarity = e.Rarity,
            };
        }

        return snapshot;
    }
}

/// <summary>把 <see cref="IInventoryWriter"/> 落到 <c>IInventoryGrain.AddItemAsync</c>。</summary>
public sealed class OrleansInventoryWriter : IInventoryWriter
{
    private readonly IClusterClient _clusterClient;

    /// <summary>创建适配器。</summary>
    /// <param name="clusterClient">Orleans 集群客户端。</param>
    public OrleansInventoryWriter(IClusterClient clusterClient)
    {
        _clusterClient = clusterClient ?? throw new ArgumentNullException(nameof(clusterClient));
    }

    /// <inheritdoc />
    public async Task<bool> AddItemAsync(Guid playerId, int templateId, int quantity, int quality, CancellationToken cancellationToken = default)
    {
        if (templateId <= 0 || quantity <= 0) return false;

        var grain = _clusterClient.GetGrain<Horizon.Orleans.Interface.IInventoryGrain>(playerId);
        return await grain.AddItemAsync(templateId, quantity, quality).ConfigureAwait(false);
    }
}

/// <summary>
/// 把 <see cref="IQuestProgressWriter"/> 落到玩家维度 <c>IQuestGrain</c>。
/// <para>
/// <b>调用链</b>：DS 击杀事件 → 本适配器 → <c>GetActiveQuestsAsync()</c> 取该玩家进行中的任务
/// → 由 <see cref="IQuestKillObjectiveMatcher"/> 判定哪些目标应被推进
/// → <c>UpdateQuestProgressAsync(questId, objectiveIndex, count)</c>。
/// </para>
/// <para>
/// <b>为什么需要"先查再改"</b>：既有玩家维度 <c>IQuestGrain</c> 的
/// <c>UpdateQuestProgressAsync</c> 需要 <c>objectiveIndex</c>，而击杀事件只带怪物模板 ID。
/// 因此必须先从任务列表里定位目标下标。这是一次额外的读取，但击杀频率远低于移动，
/// 且走的是玩家自己的 grain（无跨分片广播），成本可接受。
/// </para>
/// <para>
/// 任务的"完成"判定与奖励发放仍由任务系统负责（<c>CompleteQuestAsync</c>），
/// 本适配器只推进计数——保持单一职责。
/// </para>
/// </summary>
public sealed class OrleansQuestProgressWriter : IQuestProgressWriter
{
    private readonly IClusterClient _clusterClient;
    private readonly IQuestKillObjectiveMatcher _matcher;

    /// <summary>创建适配器。</summary>
    /// <param name="clusterClient">Orleans 集群客户端。</param>
    /// <param name="matcher">击杀目标匹配器；null 使用默认的类型字符串匹配。</param>
    public OrleansQuestProgressWriter(IClusterClient clusterClient, IQuestKillObjectiveMatcher? matcher = null)
    {
        _clusterClient = clusterClient ?? throw new ArgumentNullException(nameof(clusterClient));
        _matcher = matcher ?? new KillObjectiveTypeMatcher();
    }

    /// <inheritdoc />
    public async Task<int> AdvanceKillObjectiveAsync(ulong characterId, int monsterTemplateId, int count, CancellationToken cancellationToken = default)
    {
        if (characterId == 0UL || monsterTemplateId <= 0 || count <= 0) return 0;

        var playerKey = GrainKeyMapper.ToInventoryKey(characterId);
        var questGrain = _clusterClient.GetGrain<Horizon.Orleans.Interface.IQuestGrain>(playerKey);

        List<QuestData> active;
        try
        {
            active = await questGrain.GetActiveQuestsAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // 任务系统不可用不应影响实时链路；返回 0 表示"本次没推进任何目标"。
            return 0;
        }

        if (active is null || active.Count == 0) return 0;

        var advanced = 0;
        for (var q = 0; q < active.Count; q++)
        {
            var quest = active[q];
            if (quest?.Objectives is null || quest.Objectives.Count == 0) continue;

            // Status: 0=进行中, 1=可提交, 2=已完成, 3=已放弃
            // 只推进"进行中"；"可提交"表示已达标，继续累加没有意义。
            if (quest.Status != 0) continue;

            for (var i = 0; i < quest.Objectives.Count; i++)
            {
                var objective = quest.Objectives[i];
                if (objective is null || objective.IsCompleted) continue;
                if (!_matcher.ShouldAdvance(objective.ObjectiveType, objective.Description, monsterTemplateId)) continue;

                try
                {
                    var ok = await questGrain.UpdateQuestProgressAsync(quest.QuestId, i, count).ConfigureAwait(false);
                    if (ok) advanced++;
                }
                catch (Exception)
                {
                    // 单个任务推进失败不影响其他任务。
                }
            }
        }

        return advanced;
    }
}

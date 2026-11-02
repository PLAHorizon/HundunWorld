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

/// <summary>
/// 把 <see cref="IFavorabilityWriter"/> 落到 <c>IFavorabilityGrain.AddAsync</c>。
/// <para>
/// 玩家键与背包用同一个 <see cref="GrainKeyMapper.ToInventoryKey"/>：好感是按角色记的，
/// 而研读门槛读的也是同一角色 grain 上的 <c>GetByDomainAsync()</c>——两处键算得不同，
/// 表现就是"杀了一百只怪，好感门槛纹丝不动"，且没有任何一方报错。
/// </para>
/// </summary>
/// <summary>
/// Orleans 版击杀经验写入端：把 <c>KillXp</c> 事件落到 <see cref="ICharacterGrain.AddExpAsync"/>。
/// <para>Grain 键用 <c>(long)characterId</c>——与 <c>QuestHandler</c>、<c>SyncHandshakeHandler</c>
/// 取同一角色 Grain 的写法一致。换成别的键会得到"经验加到了一个没人读的 grain 里"：
/// 加经验返回成功、角色等级不动、日志干净。</para>
/// </summary>
public sealed class OrleansExperienceWriter : IExperienceWriterWithResult
{
    private readonly IClusterClient _clusterClient;

    /// <summary>创建适配器。</summary>
    public OrleansExperienceWriter(IClusterClient clusterClient)
    {
        _clusterClient = clusterClient ?? throw new ArgumentNullException(nameof(clusterClient));
    }

    /// <inheritdoc />
    public async Task<bool> AddExpAsync(ulong characterId, long amount, string reason, CancellationToken cancellationToken = default)
        => (await AddExpWithResultAsync(characterId, amount, reason, cancellationToken).ConfigureAwait(false)).Success;

    /// <inheritdoc />
    public async Task<ExpGrant> AddExpWithResultAsync(ulong characterId, long amount, string reason, CancellationToken cancellationToken = default)
    {
        if (characterId == 0UL || amount <= 0L) return ExpGrant.Failed;
        try
        {
            var grain = _clusterClient.GetGrain<Horizon.Orleans.Interface.ICharacterGrain>((long)characterId);
            var result = await grain.AddExpAsync(amount).ConfigureAwait(false);
            // AddExpAsync 返回结构化结果（含是否升级）；用它而不是"没抛异常就算成功"，
            // 否则"上限满级还继续加经验"这种情形会一直报成功。
            if (result == null || !result.Success) return ExpGrant.Failed;
            return new ExpGrant { Success = true, Level = result.Level, Experience = result.Experience };
        }
        catch
        {
            return ExpGrant.Failed;
        }
    }
}

/// <summary>
/// Orleans 版金币入账写入端：把 <c>KillMoney</c> 事件落到 <see cref="Horizon.Orleans.Interface.ICurrencyGrain.AddAsync"/>。
/// <para>Grain 键用 <c>(long)characterId</c>——与 <c>ShopHandler</c>/<c>SkillHandler</c>/<c>CraftHandler</c>
/// 取同一个钱包的写法逐字相同。用别的键会得到"钱加到了一个没人读的 grain 里"：
/// 商店买不起东西、角色口袋却没变化，而这条链的日志全是绿的。</para>
/// <para>成功判据取回执（<c>Ok</c> 且余额确实增加了这么多），不是"没抛异常"：
/// 钱包在越界/未登记货币时是**一分不入地拒绝**，那必须算失败并被调用方计数。</para>
/// </summary>
public sealed class OrleansCurrencyWriter : ICurrencyWriter
{
    private readonly IClusterClient _clusterClient;

    /// <summary>创建适配器。</summary>
    public OrleansCurrencyWriter(IClusterClient clusterClient)
    {
        _clusterClient = clusterClient ?? throw new ArgumentNullException(nameof(clusterClient));
    }

    /// <inheritdoc />
    public async Task<bool> AddGoldAsync(ulong characterId, long amount, string source, CancellationToken cancellationToken = default)
    {
        if (characterId == 0UL || amount <= 0L) return false;
        try
        {
            var wallet = _clusterClient.GetGrain<Horizon.Orleans.Interface.ICurrencyGrain>((long)characterId);
            var mutation = await wallet.AddAsync(Horizon.Game.Sim.Economy.WalletCurrency.Gold, amount, source).ConfigureAwait(false);
            return mutation is { Ok: true } && mutation.BalanceAfter - mutation.BalanceBefore == amount;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// 任务持有状态读取端（条件掉落的判据）：查该角色**进行中**的任务列表里有没有这个任务 id。
/// <para>用的 id 口径与怪物编辑器校验器一致（<c>QuestId</c> = 任务模板 id，见
/// <c>MonsterValidator</c> 对 <c>questIds</c> 的对账），两边各用一套键会让条件掉落永远判不过。</para>
/// </summary>
public sealed class OrleansQuestStateReader : IQuestStateReader
{
    private readonly IClusterClient _clusterClient;

    /// <summary>创建适配器。</summary>
    public OrleansQuestStateReader(IClusterClient clusterClient)
    {
        _clusterClient = clusterClient ?? throw new ArgumentNullException(nameof(clusterClient));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<int>?> ActiveQuestIdsAsync(ulong characterId, CancellationToken cancellationToken = default)
    {
        if (characterId == 0UL) return Array.Empty<int>();
        try
        {
            var playerKey = GrainKeyMapper.ToInventoryKey(characterId);
            var questGrain = _clusterClient.GetGrain<Horizon.Orleans.Interface.IQuestGrain>(playerKey);
            var active = await questGrain.GetActiveQuestsAsync().ConfigureAwait(false);
            if (active is null) return null;
            var ids = new List<int>(active.Count);
            for (var i = 0; i < active.Count; i++)
            {
                var q = active[i];
                // 与 QueryHoldAsync 同一口径：只有进行中（Status==0）才算持有。
                if (q is not null && q.Status == 0 && q.QuestId > 0) ids.Add(q.QuestId);
            }
            return ids;
        }
        catch
        {
            // 问不出来 ≠ 不配拿——但条件件这一侧只能"判不了→不满足"；返回 null 让派生出显形。
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<QuestHoldState> QueryHoldAsync(ulong characterId, int questId, CancellationToken cancellationToken = default)
    {
        if (characterId == 0UL || questId <= 0) return QuestHoldState.NotHeld;

        try
        {
            var playerKey = GrainKeyMapper.ToInventoryKey(characterId);
            var questGrain = _clusterClient.GetGrain<Horizon.Orleans.Interface.IQuestGrain>(playerKey);
            var active = await questGrain.GetActiveQuestsAsync().ConfigureAwait(false);
            if (active is null) return QuestHoldState.Unknown;

            for (var i = 0; i < active.Count; i++)
            {
                var quest = active[i];
                if (quest is null || quest.QuestId != questId) continue;
                // Status == 0 = 进行中：已完成的任务不再算"持有"，
                // 否则"做完任务后还能继续从怪身上掉那件任务奖励"这种刷取会成立。
                if (quest.Status == 0) return QuestHoldState.Held;
            }
            return QuestHoldState.NotHeld;
        }
        catch
        {
            // 问不出来 ≠ 玩家不配拿。调用方必须能分辨这两种情况（Unknown vs NotHeld）。
            return QuestHoldState.Unknown;
        }
    }
}

public sealed class OrleansFavorabilityWriter : IFavorabilityWriter
{
    private readonly IClusterClient _clusterClient;

    /// <summary>创建适配器。</summary>
    public OrleansFavorabilityWriter(IClusterClient clusterClient)
    {
        _clusterClient = clusterClient ?? throw new ArgumentNullException(nameof(clusterClient));
    }

    /// <inheritdoc />
    public async Task<bool> AddAsync(Guid playerId, int domain, int delta, string targetKey, CancellationToken cancellationToken = default)
    {
        if (playerId == Guid.Empty || delta == 0 || domain < 1 || domain > 5) return false;

        var grain = _clusterClient.GetGrain<Horizon.Orleans.Interface.IFavorabilityGrain>(playerId);
        try
        {
            await grain.AddAsync(domain, delta, targetKey).ConfigureAwait(false);
            return true;
        }
        catch (Exception)
        {
            // 实时链路（击杀事件）不能因为好感系统不可用而失败；调用方按计数对账。
            return false;
        }
    }
}

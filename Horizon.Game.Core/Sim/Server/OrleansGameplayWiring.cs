using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Horizon.Game.Sim;

namespace Horizon.Game.Core.Sim.Server;

/// <summary>
/// Orleans 掉落提供者（MMORPG 开发计划 M4.3 收尾）—— 把 <c>LootTableGrain</c> 真正接到掉落链路上。
/// <para>
/// <b>工作方式</b>：异步预热掉落表**配置**到本地缓存 → <see cref="Roll"/> 在 DS 的 tick 线程上
/// 以纯确定性方式本地掷骰。这样既让掉落内容由业务层的 <c>LootTableGrain</c> 说了算，
/// 又不在热路径上做跨进程 RPC。
/// </para>
/// <para>
/// <b>配置未命中时的语义（fail-closed）</b>：表尚未预热成功时返回**空掉落**并计入
/// <see cref="CacheMissCount"/>，而不是回退到内置表。理由：静默编造掉落会让运营数据与配置脱节，
/// 且难以在线上发现；"暂时不掉东西"是可观测、可告警、可恢复的失败。
/// </para>
/// <para>
/// <b>保底（Pity）不在此实现</b>：<c>LootTableConfig.PityThreshold</c> 依赖**跨击杀的每玩家计数**，
/// 那是 grain 状态。DS 是无状态实时层，本地做保底会导致"换 DS 就重置保底"。
/// 保底应由业务层在收到掉落事件时校准，或在 DS 侧引入显式持久化的计数——已登记为待办。
/// </para>
/// </summary>
public sealed class OrleansLootProvider : IDsLootProvider
{
    private readonly ILootTableConfigSource _configSource;
    private readonly IMonsterLootMapping _mapping;
    private readonly ConcurrentDictionary<int, LootTableSnapshot> _cache = new();
    private readonly ConcurrentDictionary<int, byte> _failedTables = new();

    /// <summary>创建提供者。</summary>
    /// <param name="configSource">配置来源（生产：<c>ILootTableGrain.GetConfigAsync</c> 适配器）。</param>
    /// <param name="mapping">怪物模板 → 掉落表映射；null 使用恒等映射。</param>
    public OrleansLootProvider(ILootTableConfigSource configSource, IMonsterLootMapping? mapping = null)
    {
        _configSource = configSource ?? throw new ArgumentNullException(nameof(configSource));
        _mapping = mapping ?? new IdentityMonsterLootMapping();
    }

    /// <summary>累计本地掷骰次数。</summary>
    public long TotalRolls { get; private set; }

    /// <summary>累计因配置未命中而返回空掉落的次数（告警源）。</summary>
    public long CacheMissCount { get; private set; }

    /// <summary>当前已缓存的掉落表数量。</summary>
    public int CachedTableCount => _cache.Count;

    /// <summary>
    /// 预热指定怪物模板的掉落表配置（应在 tick 循环之外调用，例如刷怪点配置完成后）。
    /// </summary>
    /// <returns>是否成功缓存。</returns>
    public async Task<bool> WarmupAsync(IEnumerable<int> monsterTemplateIds, CancellationToken cancellationToken = default)
    {
        if (monsterTemplateIds is null) return false;

        var allOk = true;
        foreach (var templateId in monsterTemplateIds)
        {
            var tableId = _mapping.ResolveLootTableId(templateId);
            if (tableId <= 0) continue;

            if (_cache.ContainsKey(tableId)) continue;

            try
            {
                var snapshot = await _configSource.GetLootTableAsync(tableId, cancellationToken).ConfigureAwait(false);
                if (snapshot is null || snapshot.Entries is null || snapshot.Entries.Length == 0)
                {
                    _failedTables[tableId] = 1;
                    allOk = false;
                    continue;
                }

                _cache[tableId] = snapshot;
                _failedTables.TryRemove(tableId, out _);
            }
            catch (Exception)
            {
                // 预热失败不能杀死宿主：DS 应在"无掉落"状态下继续提供实时服务，
                // 由运维通过 CacheMissCount 发现并重试。
                _failedTables[tableId] = 1;
                allOk = false;
            }
        }

        return allOk;
    }

    /// <summary>直接注入一张掉落表（测试/热更新用）。</summary>
    public void PrimeTable(LootTableSnapshot snapshot)
    {
        if (snapshot is null) return;
        _cache[snapshot.TableId] = snapshot;
        _failedTables.TryRemove(snapshot.TableId, out _);
    }

    /// <inheritdoc />
    /// <remarks>
    /// 纯本地确定性：掷骰输入为 (killerCharacterId, itemId, serverTick, salt)，
    /// 不含任何墙钟/随机源——同一 tick 重放必然得到同一掉落。
    /// </remarks>
    public IReadOnlyList<DsLootDrop> Roll(int monsterTemplateId, ulong killerCharacterId, long serverTick)
    {
        if (monsterTemplateId <= 0 || killerCharacterId == 0UL) return Array.Empty<DsLootDrop>();

        var tableId = _mapping.ResolveLootTableId(monsterTemplateId);
        if (tableId <= 0) return Array.Empty<DsLootDrop>();

        if (!_cache.TryGetValue(tableId, out var table) || table.Entries.Length == 0)
        {
            CacheMissCount++;
            return Array.Empty<DsLootDrop>();
        }

        TotalRolls++;

        var drops = new List<DsLootDrop>(2);
        var entries = table.Entries;

        for (var i = 0; i < entries.Length; i++)
        {
            var entry = entries[i];
            if (entry.ItemId <= 0) continue;

            var rate = entry.DropRate;
            if (rate <= 0f) continue;

            if (rate < 1f)
            {
                // salt 用条目下标而非物品 ID：同一张表里两个条目引用同一物品 ID 时也能独立掷骰。
                var roll = CombatCore.Roll01(killerCharacterId, (ulong)entry.ItemId, serverTick, salt: 0x7000u + (uint)i);
                if (roll >= rate) continue;
            }

            var count = RollQuantity(entry.MinCount, entry.MaxCount, killerCharacterId, entry.ItemId, serverTick, i);
            if (count <= 0) count = 1; // 必掉条目数量至少 1

            drops.Add(new DsLootDrop { ItemId = entry.ItemId, Count = count });
        }

        return drops;
    }

    private static int RollQuantity(int min, int max, ulong killerCharacterId, int itemId, long serverTick, int index)
    {
        if (max <= min) return min > 0 ? min : 0;

        var span = max - min + 1;
        var roll = CombatCore.Roll01(killerCharacterId, (ulong)itemId, serverTick + index, salt: 0x7100u);
        var offset = (int)(roll * span);
        if (offset >= span) offset = span - 1; // 浮点上界保护
        return min + offset;
    }
}

/// <summary>
/// Orleans 跨层玩法事件接收端（MMORPG 开发计划 M4.3/M4.4 收尾）——
/// 把 DS 上报的击杀/掉落事实落到真实业务 Grain。
/// <para>
/// <b>为什么用有界队列 + 后台泵</b>：<see cref="IDsGameplayEventSink.Publish"/> 由网关的网络
/// 接收线程调用，直接在里面 await grain RPC 会把 TouchSocket 的接收线程拖住，
/// 导致上行帧积压（玩家输入延迟飙升）。队列把"接收"与"业务处理"解耦，
/// 与既有 <c>GatewayZoneShardFanoutSource</c> 的做法一致。
/// </para>
/// <para>
/// <b>丢弃语义</b>：队列满时丢**最旧**（与 fanout 一致，"丢旧保新"），
/// 计数经 <see cref="DroppedCount"/> 暴露。业务侧应据此对账——掉落是可补偿的（幂等键），
/// 而把网关拖死是不可恢复的。
/// </para>
/// </summary>
public sealed class OrleansGameplayEventSink : IDsGameplayEventSink, IAsyncDisposable
{
    private readonly IInventoryWriter? _inventory;
    private readonly IQuestProgressWriter? _quests;
    private readonly IFavorabilityWriter? _favorability;
    private readonly IMonsterDomainResolver _domains;
    private readonly IExperienceWriter? _experience;
    private readonly IPlayerProgressNotifier? _progress;
    private readonly ICurrencyWriter? _currency;
    private readonly IQuestStateReader? _questStates;
    private readonly int _capacity;
    private readonly System.Threading.Channels.Channel<DsGameplayEvent> _channel;
    private readonly System.Threading.Tasks.Task _pump;
    private readonly CancellationTokenSource _cts = new();
    private readonly Action<string>? _log;

    /// <summary>单次击杀累积的好感点数。</summary>
    /// <remarks>
    /// 与"来源上限 20"（<c>FavorabilityGrain.DefaultPerTargetCap</c>）配套：杀满 20 只同类才吃满一来源，
    /// 正好够 §14 玄·初的 favorNeed 20——所以"在同一片地域多待一会儿"是可玩路径，而不是无谓刷数。
    /// </remarks>
    public const int FavorPerKill = 1;

    /// <summary>创建接收端。</summary>
    /// <param name="inventory">背包写入端；null = 掉落事件只计数不落库。</param>
    /// <param name="quests">任务进度写入端；null = 击杀事件只计数不落库。</param>
    /// <param name="log">日志回调。</param>
    /// <param name="capacity">队列容量；默认 8192（约 1 秒内数十次群怪死亡的峰值余量）。</param>
    /// <param name="favorability">分域好感写入端；null = 击杀不涨好感。</param>
    /// <param name="domains">怪物模板 → 五行域 解析；null 用 <see cref="MonsterTemplateDomainResolver"/>（读怪物模板的 WuxingDomain 列）。</param>
    public OrleansGameplayEventSink(
        IInventoryWriter? inventory,
        IQuestProgressWriter? quests,
        Action<string>? log = null,
        int capacity = 8192,
        IFavorabilityWriter? favorability = null,
        IMonsterDomainResolver? domains = null,
        IExperienceWriter? experience = null,
        IQuestStateReader? questStates = null,
        ICurrencyWriter? currency = null,
        IPlayerProgressNotifier? progress = null)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _inventory = inventory;
        _quests = quests;
        _favorability = favorability;
        _domains = domains ?? new MonsterTemplateDomainResolver();
        _experience = experience;
        _questStates = questStates;
        _currency = currency;
        _progress = progress;
        _log = log;
        _capacity = capacity;

        _channel = System.Threading.Channels.Channel.CreateBounded<DsGameplayEvent>(
            new System.Threading.Channels.BoundedChannelOptions(capacity)
            {
                FullMode = System.Threading.Channels.BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false,
            });

        _pump = System.Threading.Tasks.Task.Run(() => PumpAsync(_cts.Token), CancellationToken.None);
    }

    /// <summary>累计入队事件数。</summary>
    public long PublishedCount { get; private set; }

    /// <summary>累计因背压丢弃的事件数。</summary>
    public long DroppedCount { get; private set; }

    /// <summary>累计发放的击杀经验总量（<c>KillXp</c> 事件的产出；0 且 ExpSkippedNoWriter&gt;0 = 没接线）。</summary>
    public long ExpGranted { get; private set; }

    /// <summary>因"没有经验写入端"而被跳过的事件数（装配缺失的显形计数）。</summary>
    public long ExpSkippedNoWriter { get; private set; }

    /// <summary>累计入账的击杀金币（<c>KillMoney</c> 的产出；0 而 <see cref="GoldSkippedNoWriter"/> 不为 0 = 没接线）。</summary>
    public long GoldCredited { get; private set; }

    /// <summary>
    /// 因没有 <see cref="ICurrencyWriter"/> 而被跳过的金币<b>总额</b>。
    /// <para>单位刻意与 <see cref="ExpSkippedNoWriter"/>（事件数）不同：排查"我的钱去哪了"时
    /// 要回答的是"少了多少金币"，不是"少了几条事件"。</para>
    /// </summary>
    public long GoldSkippedNoWriter { get; private set; }

    /// <summary>条件掉落：判过、确实发出去的数量（件数，按 ItemCount 累加）。</summary>
    public long ConditionalLootGranted { get; private set; }

    /// <summary>条件掉落：玩家没持有该任务而拒绝的件数（正常路径，不是故障）。</summary>
    public long ConditionalLootDeniedNotHeld { get; private set; }

    /// <summary>条件掉落：任务系统问不出结果的件数（grain 不可用/超时——与"没持有"必须分开）。</summary>
    public long ConditionalLootUnknown { get; private set; }

    /// <summary>条件掉落：因没接 <see cref="IQuestStateReader"/> 而整档跳过的件数（装配缺失的显形）。</summary>
    public long ConditionalLootSkippedNoReader { get; private set; }

    /// <summary>条件掉落：判过"该给"但没有背包写入端的件数（另一类装配缺失）。</summary>
    public long ConditionalLootSkippedNoWriter { get; private set; }

    /// <summary>累计成功处理（落库）的事件数。</summary>
    public long ProcessedCount { get; private set; }

    /// <summary>累计处理失败的事件数（业务层异常/返回 false）。</summary>
    public long FailedCount { get; private set; }

    /// <summary>累计被推进的任务目标数。</summary>
    public long QuestObjectiveAdvances { get; private set; }

    /// <summary>累计成功入包的物品件数。</summary>
    public long ItemsGranted { get; private set; }

    /// <summary>累计好感入账次数（一次 = 一次击杀，点数见 <see cref="FavorPerKill"/>）。</summary>
    public long FavorabilityGrants { get; private set; }

    /// <summary>累计好感写入失败次数。</summary>
    public long FavorabilityFailed { get; private set; }

    /// <summary>当前待处理队列长度。</summary>
    public int PendingCount => _channel.Reader.Count;

    /// <inheritdoc />
    public void Publish(in DsGameplayEvent evt)
    {
        PublishedCount++;
        if (!_channel.Writer.TryWrite(evt))
        {
            DroppedCount++;
        }
    }

    /// <summary>等待队列排空（测试/优雅下线用）。</summary>
    public async Task DrainAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (_channel.Reader.Count == 0)
            {
                // 再等一拍，确保最后一条已被泵取走并落库。
                await System.Threading.Tasks.Task.Delay(30).ConfigureAwait(false);
                if (_channel.Reader.Count == 0) return;
            }
            await System.Threading.Tasks.Task.Delay(15).ConfigureAwait(false);
        }
    }

    private async System.Threading.Tasks.Task PumpAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                DsGameplayEvent evt;
                try
                {
                    evt = await _channel.Reader.ReadAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (System.Threading.Channels.ChannelClosedException)
                {
                    return;
                }

                await HandleAsync(evt).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _log?.Invoke($"OrleansGameplayEventSink：泵异常退出：{ex.Message}");
        }
    }

    private async System.Threading.Tasks.Task HandleAsync(DsGameplayEvent evt)
    {
        try
        {
            switch (evt.Kind)
            {
                case DsGameplayEventKind.Kill:
                {
                    // 击杀 → 任务进度（"击杀 N 只 X"类目标的唯一推进入口）。
                    if (_quests is not null)
                    {
                        var advanced = await _quests.AdvanceKillObjectiveAsync(
                            evt.ActorCharacterId, evt.MonsterTemplateId, 1, _cts.Token).ConfigureAwait(false);
                        QuestObjectiveAdvances += advanced;
                    }

                    // 击杀 → 该怪所属五行域的好感（§14"中级靠区域生物好感"的动态来源）。
                    // 未定域（0）直接跳过：这不是"漏了"，而是这只怪本来就不该给任何域记账——
                    // 猜一个域的表现是"金域好感从火域怪身上涨"，任何报错里都看不出来。
                    var domain = _domains.ResolveDomain(evt.MonsterTemplateId);
                    if (_favorability is not null && domain >= 1 && domain <= 5 && evt.ActorCharacterId != 0UL)
                    {
                        // 好感用 GrainKeys.ForEntity：研读门槛与任务奖励都按这把键读写好感，
                        // 用背包那把（大端）键会把击杀攒的好感记到没人看的 grain 里 ——
                        // "刷同级怪涨好感开中级门"于是永远不开，且两端都没有报错。
                        var favorKey = GrainKeys.ForEntity(evt.ActorCharacterId);
                        var ok = await _favorability.AddAsync(
                            favorKey, domain, FavorPerKill, $"monster:{evt.MonsterTemplateId}", _cts.Token)
                            .ConfigureAwait(false);
                        if (ok) FavorabilityGrants++;
                        else FavorabilityFailed++;
                    }

                    ProcessedCount++;
                    return;
                }

                case DsGameplayEventKind.Loot:
                {
                    if (_inventory is not null && evt.ItemId > 0 && evt.ItemCount > 0)
                    {
                        var playerId = GrainKeyMapper.ToInventoryKey(evt.ActorCharacterId);
                        var ok = await _inventory.AddItemAsync(
                            playerId, evt.ItemId, evt.ItemCount, quality: 0, _cts.Token).ConfigureAwait(false);
                        if (ok)
                        {
                            ItemsGranted += evt.ItemCount;
                            ProcessedCount++;
                        }
                        else
                        {
                            FailedCount++;
                        }
                    }
                    else
                    {
                        ProcessedCount++;
                    }
                    return;
                }

                // 条件掉落：DS 已经掷过骰（确定性归模拟层），这里只判"这个玩家配不配拿"。
                case DsGameplayEventKind.LootConditional:
                {
                    if (evt.ItemId <= 0 || evt.ItemCount <= 0)
                    {
                        ProcessedCount++;
                        return;
                    }

                    if (_questStates is null)
                    {
                        // 没接读取端就不给——绝不"判不了就当满足"，否则条件掉落会变成普通掉落。
                        ConditionalLootSkippedNoReader++;
                        ProcessedCount++;
                        _log?.Invoke($"OrleansGameplayEventSink：条件掉落未接线（无 IQuestStateReader），" +
                                     $"已跳过物品 {evt.ItemId}×{evt.ItemCount}（任务 {evt.QuestId}）。");
                        return;
                    }

                    var hold = await _questStates.QueryHoldAsync(evt.ActorCharacterId, evt.QuestId, _cts.Token)
                        .ConfigureAwait(false);
                    switch (hold)
                    {
                        case QuestHoldState.Held:
                        {
                            if (_inventory is null)
                            {
                                // 判过了但没有写入端：与"没有读取端"同类的装配缺失，单独可见。
                                ConditionalLootSkippedNoWriter++;
                                ProcessedCount++;
                                return;
                            }
                            var playerId = GrainKeyMapper.ToInventoryKey(evt.ActorCharacterId);
                            var ok = await _inventory.AddItemAsync(
                                playerId, evt.ItemId, evt.ItemCount, quality: 0, _cts.Token).ConfigureAwait(false);
                            if (ok)
                            {
                                ConditionalLootGranted += evt.ItemCount;
                                ItemsGranted += evt.ItemCount;
                                ProcessedCount++;
                            }
                            else
                            {
                                FailedCount++;
                            }
                            return;
                        }
                        case QuestHoldState.NotHeld:
                            // 正常路径：玩家没接这个任务。计数而不是静默丢弃。
                            ConditionalLootDeniedNotHeld++;
                            ProcessedCount++;
                            return;
                        default:
                            // 问不出来（grain 挂了/超时）：与"没持有"分开计，否则会伪装成玩家不满足条件。
                            ConditionalLootUnknown++;
                            ProcessedCount++;
                            return;
                    }
                }

                case DsGameplayEventKind.KillXp:
                {
                    // 击杀经验（值由 MonsterXpCore 在权威侧算好，含灰名断供与随玩家等级的反比例衰减）。
                    // **不推进任务、不涨好感**：那两个只认 Kill 事件，否则围观的人都会白得击杀进度。
                    if (evt.ItemCount <= 0)
                    {
                        ProcessedCount++;
                        return;
                    }
                    if (_experience is null)
                    {
                        // 没接线时必须看得见：否则"经验系统上线了"这个说法只是事件在发、没人在听。
                        ExpSkippedNoWriter++;
                        _log?.Invoke($"OrleansGameplayEventSink：KillXp 未接线（无 IExperienceWriter），已跳过 {evt.ItemCount} 点经验。");
                        return;
                    }
                    ExpGrant? grant = null;
                    bool granted;
                    if (_experience is IExperienceWriterWithResult withResult)
                    {
                        grant = await withResult.AddExpWithResultAsync(
                            evt.ActorCharacterId, evt.ItemCount,
                            $"monster:{evt.MonsterTemplateId}", _cts.Token).ConfigureAwait(false);
                        granted = grant.Value.Success;
                    }
                    else
                    {
                        granted = await _experience.AddExpAsync(
                            evt.ActorCharacterId, evt.ItemCount,
                            $"monster:{evt.MonsterTemplateId}", _cts.Token).ConfigureAwait(false);
                    }

                    if (granted)
                    {
                        ExpGranted += evt.ItemCount;
                        // 把权威累计值推回 DS：客户端经验条读的是 DS 玩家快照里的 Exp，
                        // 不推这一手，经验涨在 Grain 里、玩家屏幕上纹丝不动。
                        if (grant is { Success: true } g && _progress is not null)
                        {
                            try { _progress.Notify((long)evt.ActorCharacterId, g.Level, g.Experience); }
                            catch (Exception ex) { _log?.Invoke($"OrleansGameplayEventSink：玩家进度推送异常：{ex.Message}"); }
                        }
                        ProcessedCount++;
                    }
                    else
                    {
                        FailedCount++;
                    }
                    return;
                }

                case DsGameplayEventKind.KillMoney:
                {
                    // 击杀掉落金币（金额由 MonsterLootCore 在权威侧掷好，与物品同一套盐值与顺序）。
                    // 单独一种事件而不是挂在 Loot 上：Loot 分支第一句就要求 ItemId/ItemCount 为正，
                    // 金额挂在上面会被那条判据安静吞掉。
                    if (evt.Money <= 0)
                    {
                        ProcessedCount++;
                        return;
                    }
                    if (_currency is null)
                    {
                        // 同 KillXp 的纪律：没接线必须看得见，否则"金币已到账"只是事件在发、没人在听。
                        GoldSkippedNoWriter += evt.Money;
                        _log?.Invoke($"OrleansGameplayEventSink：KillMoney 未接线（无 ICurrencyWriter），已跳过 {evt.Money} 金币。");
                        return;
                    }
                    var credited = await _currency.AddGoldAsync(
                        evt.ActorCharacterId, evt.Money,
                        $"monster_loot:{evt.MonsterTemplateId}", _cts.Token).ConfigureAwait(false);
                    if (credited)
                    {
                        GoldCredited += evt.Money;
                        ProcessedCount++;
                    }
                    else
                    {
                        // 钱包拒绝（越界一分不入）或异常都算失败：**不重试、不静默**，交给计数与告警。
                        FailedCount++;
                    }
                    return;
                }

                case DsGameplayEventKind.PlayerDeath:
                    // 死亡惩罚/统计属业务层策略；当前无对应 Grain 接口，显式计数而非静默丢弃。
                    ProcessedCount++;
                    return;

                default:
                    _log?.Invoke($"OrleansGameplayEventSink：未知事件类型 {evt.Kind}（已忽略）。");
                    FailedCount++;
                    return;
            }
        }
        catch (OperationCanceledException)
        {
            // 关闭期
        }
        catch (Exception ex)
        {
            // 单条事件失败不能中断泵：一条掉落入包失败不该让后续所有击杀都停止推进任务。
            FailedCount++;
            _log?.Invoke($"OrleansGameplayEventSink：处理事件失败（已跳过）。Kind={evt.Kind} Actor={evt.ActorCharacterId} Item={evt.ItemId} Err={ex.Message}");
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        try { _cts.Cancel(); } catch { /* 忽略 */ }
        _channel.Writer.TryComplete();
        try { await _pump.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); } catch { /* 忽略 */ }
        _cts.Dispose();
    }
}

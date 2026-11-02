using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Horizon.Core.Abstract;
using Horizon.Entities;
using Horizon.Game.Sim.Quests;
using Horizon.Model.GameModel;
using Horizon.Orleans.Interface.World;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;

namespace Horizon.Orleans.Grains.World;

/// <summary>
/// 任务玩法管理 Grain：模板目录缓存 + 实时发布/关闭 + 持有者登记与撤销队列。
/// <para>
/// <b>本 Grain 不回头调用 QuestGrain</b>（除 <see cref="CancelCharacterQuestAsync"/> 这种由外部发起的定向操作）。
/// 原因：接取链上 <c>QuestGrain → 本 Grain</c> 已经在等回复，若本 Grain 同时反过来调那个 QuestGrain，
/// 两个非重入 Grain 就会互等到超时。强制取消因此写成"撤销队列"：本 Grain 只记账，
/// 真正执行放弃由网关（不在 Grain 调用环里）取走队列后代为调用。
/// </para>
/// </summary>
[GrainType("quest.manager")]
public sealed class QuestManagerGrain : Grain, IQuestManagerGrain
{
    /// <summary>目录缓存默认有效期（秒）。任务编辑器直接改库后，最迟这么久就会生效。</summary>
    public const int DefaultCacheSeconds = 10;

    private readonly ILogger<QuestManagerGrain> _logger;
    private readonly IPersistentState<QuestManagerState> _state;
    private readonly IDataContext<GameEntityContext, QuestTemplateEntity, int>? _templates;
    private readonly IDataContext<GameEntityContext, ItemTemplateEntity, int>? _items;

    private QuestCatalogEntry[] _cache = Array.Empty<QuestCatalogEntry>();
    private bool _hasCache;
    private DateTime _cacheUntilUtc = DateTime.MinValue;
    private int _catalogVersion;
    private readonly SemaphoreSlim _writeGate = new SemaphoreSlim(1, 1);

    /// <summary>构造函数（Orleans 注入）。</summary>
    public QuestManagerGrain(
        ILogger<QuestManagerGrain> logger,
        [PersistentState("questManager", "GameStore")] IPersistentState<QuestManagerState> state,
        IDataContext<GameEntityContext, QuestTemplateEntity, int>? templates = null,
        IDataContext<GameEntityContext, ItemTemplateEntity, int>? items = null)
    {
        _logger = logger;
        _state = state;
        _templates = templates;
        _items = items;
    }

    /// <inheritdoc />
    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        _state.State.Holders ??= new Dictionary<int, List<Guid>>();
        _state.State.Audit ??= new List<string>();
        _state.State.RevokedQueues ??= new Dictionary<Guid, List<int>>();
        _catalogVersion = _state.State.CatalogVersion;
        await base.OnActivateAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<QuestCatalogSnapshot> GetCatalogAsync(bool forceRefresh = false)
    {
        await EnsureCatalogAsync(forceRefresh);
        return new QuestCatalogSnapshot
        {
            CatalogVersion = _catalogVersion,
            BuiltAtUtc = DateTime.UtcNow,
            Entries = _cache,
        };
    }

    /// <inheritdoc />
    public async Task<int> GetCatalogVersionAsync()
    {
        await EnsureCatalogAsync(forceRefresh: false);
        return _catalogVersion;
    }

    /// <inheritdoc />
    public async Task<QuestOpResult> PublishAsync(int questId)
    {
        if (questId <= 0) return QuestOpResult.Fail("任务 id 必须是正整数。");
        var row = await ReadRowAsync(questId);
        if (row == null) return QuestOpResult.Fail("任务模板不存在：" + questId);
        if (row.PublishState == (int)QuestPublishState.Online)
            return QuestOpResult.Ok("任务已是发布状态。", _catalogVersion);

        // 发布前按"可发布"的标准再校验一次：编辑器那边校验过不等于这里可以省，
        // 直接改库、跑种子、手滑都能把一条不合格的任务推到玩家面前。
        // 校验的是"发布之后"的样子——拿当前 publish_state=0 的行跑 requirePublishable，
        // Q050 会把每一次发布都自相矛盾地拒掉。
        var draft = QuestTemplateMapper.ToDraft(row);
        draft.PublishState = (int)QuestPublishState.Online;
        var validation = QuestTemplateValidator.Validate(draft, await BuildIndexAsync(), requirePublishable: true);
        if (validation.HasErrors)
            return QuestOpResult.Fail("发布被校验拦下：" + Environment.NewLine + SummarizeErrors(validation));

        await _writeGate.WaitAsync();
        try
        {
            row.PublishState = (int)QuestPublishState.Online;
            if (_templates == null || !await _templates.UpdateAsync(row, row.Id))
                return QuestOpResult.Fail("发布状态写库失败（模板上下文未注册或被拒）。");

            await TouchCatalogAsync("发布任务 " + questId + "（" + row.QuestName + "）");
            return QuestOpResult.Ok("已发布。", _catalogVersion);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<QuestOpResult> UnpublishAsync(int questId, bool forceAbandonActive = false)
    {
        if (questId <= 0) return QuestOpResult.Fail("任务 id 必须是正整数。");
        var row = await ReadRowAsync(questId);
        if (row == null) return QuestOpResult.Fail("任务模板不存在：" + questId);

        int versionAfterClose;
        await _writeGate.WaitAsync();
        try
        {
            row.PublishState = (int)QuestPublishState.Offline;
            if (_templates == null || !await _templates.UpdateAsync(row, row.Id))
                return QuestOpResult.Fail("关闭状态写库失败。");

            await TouchCatalogAsync("关闭任务 " + questId + "（" + row.QuestName + "）");
            versionAfterClose = _catalogVersion;
        }
        finally
        {
            _writeGate.Release();
        }

        // 撤销账必须在这把锁**外面**记。_writeGate 是不可重入的 SemaphoreSlim(1,1)，
        // 在持锁路径里再等它就是自己等自己：本 grain 是单例（主键恒为 1），
        // 一次"关闭并强制取消"会把它永久卡住，之后所有任务管理调用都排队到超时——
        // 现场表现是"运营点了下线，整个任务系统没反应了"，而且没有任何日志说明为什么。
        // 这条纪律由 QuestGrainLockDisciplineTests 以源码结构守住（突变验证过：把调用挪回锁内即判红）。
        var abandoned = 0;
        if (forceAbandonActive)
            abandoned = await RecordRevocationsAsync(questId, "运营关闭并强制取消");

        return QuestOpResult.Ok(
            forceAbandonActive
                ? "已关闭，" + abandoned + " 个进行中实例已进撤销队列（网关下次刷新时执行放弃）。"
                : "已关闭（进行中实例不受影响）。",
            versionAfterClose,
            abandoned);
    }

    /// <inheritdoc />
    public async Task<QuestOpResult> UpsertTemplateAsync(string draftJson)
    {
        var draft = QuestDraft.TryParse(draftJson, out var parseError);
        if (draft == null) return QuestOpResult.Fail(parseError ?? "草稿解析失败。");

        var validation = QuestTemplateValidator.Validate(draft, await BuildIndexAsync());
        if (validation.HasErrors)
            return QuestOpResult.Fail("草稿未通过校验：" + Environment.NewLine + SummarizeErrors(validation));

        if (_templates == null) return QuestOpResult.Fail("模板上下文未注册，无法写库。");

        await _writeGate.WaitAsync();
        try
        {
            var existing = await ReadRowAsync(draft.QuestId, tracking: true);
            if (existing == null)
            {
                var row = QuestTemplateMapper.NewRow(draft);
                row.ContentVersion = 1;
                // Id 是 IDENTITY 列：新任务一律由数据库发号，草稿里的 id 只在"已存在"时用于定位。
                row.Id = 0;
                await _templates.AddAsync(row);
                await TouchCatalogAsync("新建任务 " + row.QuestName);
                return QuestOpResult.Ok("已新建（id=" + row.Id + "）。", _catalogVersion);
            }

            var before = existing.ContentVersion;
            QuestTemplateMapper.Apply(draft, existing);
            existing.ContentVersion = before + 1;
            if (!await _templates.UpdateAsync(existing, existing.Id))
                return QuestOpResult.Fail("写库被拒。");

            await TouchCatalogAsync("更新任务 " + existing.Id + "（" + existing.QuestName + "）v" + existing.ContentVersion);
            return QuestOpResult.Ok("已更新到 v" + existing.ContentVersion + "。", _catalogVersion);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<QuestOpResult> CancelCharacterQuestAsync(Guid characterId, int questId, string reason)
    {
        if (characterId == Guid.Empty || questId <= 0) return QuestOpResult.Fail("角色或任务 id 无效。");
        try
        {
            var questGrain = GrainFactory.GetGrain<global::Horizon.Orleans.Interface.IQuestGrain>(characterId);
            var abandoned = await questGrain.AbandonQuestAsync(questId);
            await RemoveHolderAsync(questId, characterId);
            await WriteAuditAsync("取消角色 " + characterId + " 的任务 " + questId + "：" + (reason ?? "") + (abandoned ? "（已放弃）" : "（该角色身上没有这个任务）"));
            return abandoned
                ? QuestOpResult.Ok("已取消。", _catalogVersion)
                : QuestOpResult.Fail("该角色没有进行中的这个任务。");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "定向取消任务失败：character={Character} quest={Quest}", characterId, questId);
            return QuestOpResult.Fail("取消失败：" + ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<List<QuestOfferSummary>> GetOfferedQuestsAsync(int giverKind, int giverTemplateId,
        int characterLevel, int[] completedQuestIds, int[] activeQuestIds)
    {
        var result = new List<QuestOfferSummary>();

        // giverTemplateId 为 0 = "不按发布者过滤"：任务日志要的是"我现在能接的所有任务"这份总表。
        // 之所以能这么做：可接与否完全由服务端按发布状态/等级/前置算，客户端不声明任何身份，
        // 也就没有"谎报我在哪个 NPC 面前"这种事（按区域/靠近才开放需要 DS 提供对象身份，见方案 §11）。
        var filterByGiver = giverTemplateId > 0;

        await EnsureCatalogAsync(forceRefresh: false);

        var completed = new HashSet<int>(completedQuestIds ?? Array.Empty<int>());
        var active = new HashSet<int>(activeQuestIds ?? Array.Empty<int>());

        foreach (var entry in _cache)
        {
            if (entry.PublishState != (int)QuestPublishState.Online) continue;
            if (filterByGiver && (entry.GiverKind != giverKind || entry.GiverTemplateId != giverTemplateId)) continue;
            if (entry.LevelRequire > characterLevel) continue;

            var prereqsMet = true;
            foreach (var prerequisite in entry.PrerequisiteQuestIds)
            {
                if (!completed.Contains(prerequisite)) { prereqsMet = false; break; }
            }
            if (!prereqsMet) continue;

            var finished = completed.Contains(entry.QuestId);
            // 一次性任务交过就不再提供；可重复任务（repeat_limit≠0）继续提供，并带上"已交过"标记。
            if (finished && entry.RepeatLimit == 0) continue;

            result.Add(new QuestOfferSummary
            {
                QuestId = entry.QuestId,
                QuestName = entry.QuestName,
                Description = entry.Description,
                QuestType = entry.QuestType,
                ContentVersion = entry.ContentVersion,
                AlreadyAccepted = active.Contains(entry.QuestId),
                Finished = finished,
                LevelRequire = entry.LevelRequire,
                ObjectivesJson = entry.ObjectivesJson,
                RewardsJson = entry.RewardsJson,
                GiverKind = entry.GiverKind,
                GiverTemplateId = entry.GiverTemplateId,
                GiverName = entry.GiverName,
            });
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<int[]> TakeRevokedQuestsAsync(Guid characterId)
    {
        if (characterId == Guid.Empty) return Array.Empty<int>();
        await _writeGate.WaitAsync();
        try
        {
            if (!_state.State.RevokedQueues.TryGetValue(characterId, out var queue) || queue.Count == 0)
                return Array.Empty<int>();

            var snapshot = queue.ToArray();
            _state.State.RevokedQueues.Remove(characterId);
            await _state.WriteStateAsync();
            return snapshot;
        }
        finally
        {
            _writeGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task NotifyAcceptedAsync(int questId, Guid characterId)
    {
        if (questId <= 0 || characterId == Guid.Empty) return;
        await _writeGate.WaitAsync();
        try
        {
            if (!_state.State.Holders.TryGetValue(questId, out var holders))
            {
                holders = new List<Guid>();
                _state.State.Holders[questId] = holders;
            }
            if (!holders.Contains(characterId))
            {
                holders.Add(characterId);
                await _state.WriteStateAsync();
            }
        }
        finally
        {
            _writeGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task NotifyFinishedAsync(int questId, Guid characterId) => await RemoveHolderAsync(questId, characterId);

    /// <inheritdoc />
    public Task<int> CountHoldersAsync(int questId)
        => Task.FromResult(_state.State.Holders.TryGetValue(questId, out var holders) ? holders.Count : 0);

    private async Task RemoveHolderAsync(int questId, Guid characterId)
    {
        await _writeGate.WaitAsync();
        try
        {
            if (_state.State.Holders.TryGetValue(questId, out var holders) && holders.Remove(characterId))
                await _state.WriteStateAsync();
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async Task<int> RecordRevocationsAsync(int questId, string reason)
    {
        if (!_state.State.Holders.TryGetValue(questId, out var snapshot) || snapshot.Count == 0) return 0;

        var queued = 0;
        await _writeGate.WaitAsync();
        try
        {
            foreach (var characterId in snapshot.ToArray())
            {
                if (!_state.State.RevokedQueues.TryGetValue(characterId, out var queue))
                {
                    queue = new List<int>();
                    _state.State.RevokedQueues[characterId] = queue;
                }
                if (!queue.Contains(questId))
                {
                    queue.Add(questId);
                    queued++;
                }
            }
            await _state.WriteStateAsync();
        }
        finally
        {
            _writeGate.Release();
        }

        await WriteAuditAsync("关闭任务 " + questId + "，" + queued + " 个进行中实例进入撤销队列（" + reason + "）");
        return queued;
    }

    private async Task EnsureCatalogAsync(bool forceRefresh)
    {
        if (_templates == null)
        {
            // 上下文没注册时（测试脚手架/未接库的部署）：把缓存视为"确实一条也没有"，
            // 但绝不静默——这条 Warning 就是"任务系统看着上线了其实没数据"的唯一线索。
            if (!_hasCache)
                _logger.LogWarning("任务模板上下文未注册，任务目录为空（运行时不会提供任何任务）。");
            return;
        }

        if (!forceRefresh && _hasCache && DateTime.UtcNow < _cacheUntilUtc) return;

        try
        {
            var rows = await _templates.QueryAsync(row => row.IsValid);
            _cache = (rows ?? Enumerable.Empty<QuestTemplateEntity>())
                .OrderBy(row => row.Id)
                .Select(row => new QuestCatalogEntry
                {
                    QuestId = row.Id,
                    QuestName = row.QuestName ?? string.Empty,
                    QuestType = row.QuestType,
                    LevelRequire = row.LevelRequire,
                    PublishState = row.PublishState,
                    GiverKind = row.GiverKind,
                    GiverTemplateId = row.GiverTemplateId,
                    // 交付者随目录一起下发：Completion 侧据此判"是不是该在这一位面前交"（D11）。
                    TurnInKind = row.TurnInKind,
                    TurnInTemplateId = row.TurnInTemplateId,
                    ContentVersion = row.ContentVersion,
                    PrerequisiteQuestIds = QuestDraft.ParsePrerequisites(row.PrerequisiteQuests).ToArray(),
                    Description = row.Description ?? string.Empty,
                    RepeatLimit = row.RepeatLimit,
                    ObjectivesJson = row.Objectives ?? string.Empty,
                    RewardsJson = row.Rewards ?? string.Empty,
                })
                .ToArray();

            await FillGiverNamesAsync(_cache);

            _hasCache = true;
            _cacheUntilUtc = DateTime.UtcNow.AddSeconds(DefaultCacheSeconds);

            // 版本号 = 内容指纹，不是求和。求和会互抵（一条下线 −1 被另一条内容版本 +1 抵掉），
            // 也看不见"运营直接改库但没 bump content_version"的那一类改动，
            // 而这两类恰恰是"不停服实时更新玩法"最常走的路。详见 QuestCatalogFingerprint。
            var observed = QuestCatalogFingerprint.Compute(_cache);
            if (observed != _catalogVersion)
            {
                _catalogVersion = observed;
                if (_state.State.CatalogVersion != observed)
                {
                    _state.State.CatalogVersion = observed;
                    await _state.WriteStateAsync();
                }
            }
        }
        catch (Exception ex)
        {
            // 回库失败时保留上一份缓存：坏连接不该把线上任务清空。
            _logger.LogWarning(ex, "任务目录刷新失败，沿用上一份缓存（{Count} 条）", _cache.Length);
            _cacheUntilUtc = DateTime.UtcNow.AddSeconds(DefaultCacheSeconds);
        }
    }

    /// <summary>
    /// 给目录条目补发布者显示名。
    /// <para>为什么在服务端解析而不是让客户端自己查：客户端拿不到 NPC/物品模板目录（它连模板 id 都不该信），
    /// 名字只有一份真相才不会和目录分叉。解析不到就留空串，由界面回退成"发布者 #id"，不编一个假名。</para>
    /// </summary>
    private async Task FillGiverNamesAsync(QuestCatalogEntry[] entries)
    {
        if (entries.Length == 0) return;

        Dictionary<int, string>? itemNames = null;
        var needsItems = entries.Any(entry => entry.GiverKind == (int)QuestEntityKind.Item && entry.GiverTemplateId > 0);
        if (needsItems && _items != null)
        {
            try
            {
                itemNames = new Dictionary<int, string>();
                var rows = await _items.QueryAsync(item => item.Id > 0);
                foreach (var item in rows ?? Enumerable.Empty<ItemTemplateEntity>())
                    itemNames[item.Id] = item.ItemName ?? string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "解析物品发布者名失败，按空名处理");
            }
        }

        foreach (var entry in entries)
        {
            entry.GiverName = QuestGiverNaming.Resolve(entry.GiverKind, entry.GiverTemplateId, itemNames);
        }
    }

    private async Task TouchCatalogAsync(string auditNote)
    {
        await EnsureCatalogAsync(forceRefresh: true);
        await WriteAuditAsync(auditNote);
    }

    private async Task WriteAuditAsync(string note)
    {
        _state.State.Audit.Add(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") + " " + note);
        while (_state.State.Audit.Count > 200) _state.State.Audit.RemoveAt(0);
        await _state.WriteStateAsync();
    }

    private async Task<QuestTemplateEntity?> ReadRowAsync(int questId, bool tracking = false)
    {
        if (_templates == null) return null;
        try
        {
            var rows = await _templates.QueryAsync(row => row.Id == questId, tracking);
            return rows?.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "读任务模板失败：quest={Quest}", questId);
            return null;
        }
    }

    private async Task<IQuestReferenceIndex> BuildIndexAsync()
    {
        if (_templates == null) return StaticQuestReferenceIndex.Default;
        var itemIds = new HashSet<int>();
        if (_items != null)
        {
            try
            {
                var rows = await _items.QueryAsync(item => item.Id > 0);
                foreach (var item in rows ?? Enumerable.Empty<ItemTemplateEntity>())
                    itemIds.Add(item.Id);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "读物品目录失败，物品引用按\"未知\"处理（不误报成不存在）");
                return StaticQuestReferenceIndex.Default;
            }
        }

        var questIds = new HashSet<int>();
        try
        {
            var rows = await _templates.QueryAsync(row => row.Id > 0);
            foreach (var row in rows ?? Enumerable.Empty<QuestTemplateEntity>())
                questIds.Add(row.Id);
        }
        catch
        {
            // 前置任务对账降级为 Unknown：宁可放行由发布校验兜住，也不把库连接问题报成"任务不存在"。
        }

        return new TemplateQuestReferenceIndex(itemIds, questIds);
    }

    private static string SummarizeErrors(QuestValidationResult result)
    {
        var lines = new List<string>();
        foreach (var issue in result.Issues)
        {
            if (issue.Level == QuestIssueLevel.Error)
                lines.Add("  [" + issue.Code + "] " + issue.Path + "：" + issue.Message);
        }
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>用库里的真实 id 集合做的对账实现（物品/任务都查得到，不再返回 Unknown）。</summary>
    private sealed class TemplateQuestReferenceIndex : IQuestReferenceIndex
    {
        private readonly HashSet<int> _itemIds;
        private readonly HashSet<int> _questIds;

        public TemplateQuestReferenceIndex(HashSet<int> itemIds, HashSet<int> questIds)
        {
            _itemIds = itemIds;
            _questIds = questIds;
        }

        /// <inheritdoc />
        public QuestReferenceState Npc(int templateId)
            => Horizon.Game.Sim.NpcTemplateCatalog.TryGetRegistered(templateId, out _) ? QuestReferenceState.Exists : QuestReferenceState.Missing;

        /// <inheritdoc />
        public QuestReferenceState Monster(int templateId)
            => Horizon.Game.Sim.MonsterTemplateCatalog.TryGetRegistered(templateId, out _) ? QuestReferenceState.Exists : QuestReferenceState.Missing;

        /// <inheritdoc />
        public QuestReferenceState Item(int templateId)
            => _itemIds.Contains(templateId) ? QuestReferenceState.Exists : QuestReferenceState.Missing;

        /// <inheritdoc />
        public QuestReferenceState Quest(int questId)
            => _questIds.Count == 0 ? QuestReferenceState.Unknown
               : (_questIds.Contains(questId) ? QuestReferenceState.Exists : QuestReferenceState.Missing);
    }
}

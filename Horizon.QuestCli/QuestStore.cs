using System;
using System.Collections.Generic;
using System.Linq;
using Horizon.Entities;
using Horizon.Game.Sim;
using Horizon.Game.Sim.Quests;
using Horizon.Model.GameModel;
using Microsoft.EntityFrameworkCore;

namespace Horizon.QuestCli;

/// <summary>一次写入的结果。</summary>
public sealed class StoreResult
{
    /// <summary>是否成功。</summary>
    public bool Ok { get; set; }
    /// <summary>任务 id。</summary>
    public int QuestId { get; set; }
    /// <summary>写后版本号。</summary>
    public int ContentVersion { get; set; }
    /// <summary>说明或错误。</summary>
    public string Message { get; set; } = string.Empty;
    /// <summary>警告条目（不阻断写入，但要人看一眼）。</summary>
    public List<string> Warnings { get; set; } = new List<string>();
}

/// <summary>
/// 任务模板表的数据访问：既是 CRUD 口子，也是校验器的引用对账实现
/// （物品/任务这两个 id 空间只有连了库才判得准，所以校验必须在能查库的这一侧跑）。
/// </summary>
public sealed class QuestStore : IQuestReferenceIndex, IDisposable
{
    private readonly GameEntityContext _db;

    /// <summary>用真实连接串建立上下文（不读 repository.json 的占位符）。</summary>
    public QuestStore(string connectionString)
    {
        var options = new DbContextOptionsBuilder<GameEntityContext>()
            .UseSqlServer(connectionString)
            .Options;
        _db = new GameEntityContext(options);
    }

    /// <summary>全部任务行（含下线）。</summary>
    public List<QuestTemplateEntity> AllQuests()
        => _db.QuestTemplates.AsNoTracking().OrderBy(row => row.Id).ToList();

    /// <summary>取一行。</summary>
    public QuestTemplateEntity? FindQuest(int questId)
        => _db.QuestTemplates.AsNoTracking().FirstOrDefault(row => row.Id == questId);

    /// <summary>表里是否有数据；用于区分"没任务"与"库还没迁移"。</summary>
    public bool QuestTableReachable(out string error)
    {
        try
        {
            _ = _db.QuestTemplates.Count();
            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.GetBaseException().Message;
            return false;
        }
    }

    /// <summary>全部物品模板（奖励选择器的数据源）。</summary>
    public List<ItemTemplateEntity> AllItems()
        => _db.ItemTemplates.AsNoTracking().OrderBy(item => item.Id).ToList();

    /// <summary>
    /// 全部 NPC 模板（任务编辑器 NPC 下拉与"发布者是否存在"的数据源）。
    /// <para>
    /// **必须读库**：静态 `NpcTemplateCatalog` 只有硬编码的 1/2/3，QuestCli 是新进程、从不装配 NPC 内容，
    /// 用它会把新建/发布的 NPC 判成"不存在"——任务编辑器里选不到人、校验直接报 Missing。
    /// </para>
    /// </summary>
    public List<NpcTemplateEntity> AllNpcTemplates()
        => _db.NpcTemplates.AsNoTracking().OrderBy(npc => npc.Id).ToList();

    /// <inheritdoc />
    public QuestReferenceState Npc(int templateId)
        => NpcIds().Contains(templateId) ? QuestReferenceState.Exists : QuestReferenceState.Missing;

    /// <inheritdoc />
    public QuestReferenceState Monster(int templateId)
        => MonsterTemplateCatalog.TryGetRegistered(templateId, out _) ? QuestReferenceState.Exists : QuestReferenceState.Missing;

    private HashSet<int>? _itemIds;
    private HashSet<int>? _questIds;
    private HashSet<int>? _npcIds;

    /// <inheritdoc />
    public QuestReferenceState Item(int templateId)
        => (ItemIds().Contains(templateId)) ? QuestReferenceState.Exists : QuestReferenceState.Missing;

    /// <inheritdoc />
    public QuestReferenceState Quest(int questId)
        => QuestIds().Contains(questId) ? QuestReferenceState.Exists : QuestReferenceState.Missing;

    /// <summary>物品 id 集合（一次取全表，够小；每次进程生命周期内只取一次）。</summary>
    public HashSet<int> ItemIds() => _itemIds ??= new HashSet<int>(AllItems().Select(item => item.Id));

    /// <summary>NPC id 集合（同上，来自库而不是静态登记表）。</summary>
    public HashSet<int> NpcIds() => _npcIds ??= new HashSet<int>(AllNpcTemplates().Select(npc => npc.Id));

    /// <summary>任务 id 集合。</summary>
    public HashSet<int> QuestIds() => _questIds ??= new HashSet<int>(AllQuests().Select(row => row.Id));

    /// <summary>
    /// 写入一份草稿（新建或更新）。<b>写前一定过校验器</b>：库里出现一条运行时接不了的任务，
    /// 比 CLI 报一次错贵得多。
    /// </summary>
    public StoreResult Upsert(QuestDraft draft)
    {
        var validation = QuestTemplateValidator.Validate(draft, this);
        var warnings = validation.Issues
            .Where(issue => issue.Level != QuestIssueLevel.Error)
            .Select(issue => "[" + issue.Code + "] " + issue.Path + "：" + issue.Message)
            .ToList();

        if (validation.HasErrors)
        {
            return new StoreResult
            {
                Ok = false,
                QuestId = draft.QuestId,
                Message = "校验未通过：" + string.Join(" / ", validation.Issues
                    .Where(issue => issue.Level == QuestIssueLevel.Error)
                    .Select(issue => "[" + issue.Code + "] " + issue.Path + "：" + issue.Message)),
                Warnings = warnings,
            };
        }

        var existing = _db.QuestTemplates.Local.FirstOrDefault(row => row.Id == draft.QuestId)
                       ?? _db.QuestTemplates.FirstOrDefault(row => row.Id == draft.QuestId);

        if (existing != null)
        {
            QuestTemplateMapper.Apply(draft, existing);
            existing.ContentVersion = existing.ContentVersion + 1;
            _db.SaveChanges();
            _questIds = null;
            return new StoreResult
            {
                Ok = true,
                QuestId = existing.Id,
                ContentVersion = existing.ContentVersion,
                Message = "已更新。",
                Warnings = warnings,
            };
        }

        var row = QuestTemplateMapper.NewRow(draft);
        row.ContentVersion = 1;

        // quest_id 是 IDENTITY 列：显式 id 只在"同一条连接"的这个窗口里被允许。
        // 裸调 ExecuteSqlRaw 会另开一条连接、执行完就归还给池，SET 当场失效——
        // 于是插入报"IDENTITY_INSERT 为 OFF 时不能插入显式值"（本仓库实测踩过）。
        // 先把上下文连接显式打开并占住，SaveChanges 才会复用同一条连接。
        _db.Database.OpenConnection();
        try
        {
            _db.Database.ExecuteSqlRaw("SET IDENTITY_INSERT dbo.Game_HunduShijie_QuestTemplate ON");
            _db.QuestTemplates.Add(row);
            _db.SaveChanges();
            _db.Database.ExecuteSqlRaw("SET IDENTITY_INSERT dbo.Game_HunduShijie_QuestTemplate OFF");
        }
        finally
        {
            try { _db.Database.ExecuteSqlRaw("SET IDENTITY_INSERT dbo.Game_HunduShijie_QuestTemplate OFF"); } catch { /* 连接可能已断，关掉即可 */ }
            _db.Database.CloseConnection();
        }

        _questIds = null;
        return new StoreResult
        {
            Ok = true,
            QuestId = row.Id,
            ContentVersion = row.ContentVersion,
            Message = "已新建。",
            Warnings = warnings,
        };
    }

    /// <summary>设置发布状态（实时发布/关闭的库侧动作；运行时的缓存最迟一个 TTL 周期后生效）。</summary>
    public StoreResult SetPublishState(int questId, int publishState)
    {
        var row = _db.QuestTemplates.FirstOrDefault(item => item.Id == questId);
        if (row == null) return new StoreResult { Ok = false, QuestId = questId, Message = "任务模板不存在。" };

        // 作废是**终态**：库里实测出现过 IsValid=0 且 publish_state=1 的行（作废之后又被 online 拉起来），
        // 那种行不会被接取（接取只认 IsValid），但它在清单里显示成"已发布"，会让人以为线上还跑着这条。
        // 要复活就新建一条，别让一个软删的行在两个状态之间来回摆。
        if (publishState == (int)QuestPublishState.Online && !row.IsValid)
        {
            return new StoreResult
            {
                Ok = false,
                QuestId = questId,
                Message = "这条任务已作废，不能再上线（作废是终态）。要复活请新建一条，要清掉请用删除。",
            };
        }

        if (publishState == (int)QuestPublishState.Online)
        {
            // 按"发布之后的样子"校验：拿当前（还是下线）的行去跑 requirePublishable，
            // Q050 会以"该任务当前是下线状态"把每一次发布都拒掉——那是自相矛盾的门禁。
            var toBePublished = QuestTemplateMapper.ToDraft(row);
            toBePublished.PublishState = (int)QuestPublishState.Online;
            var validation = QuestTemplateValidator.Validate(toBePublished, this, requirePublishable: true);
            if (validation.HasErrors)
            {
                return new StoreResult
                {
                    Ok = false,
                    QuestId = questId,
                    Message = "发布被校验拦下：" + string.Join(" / ", validation.Issues
                        .Where(issue => issue.Level == QuestIssueLevel.Error)
                        .Select(issue => "[" + issue.Code + "] " + issue.Path + "：" + issue.Message)),
                };
            }
        }

        row.PublishState = publishState;
        _db.SaveChanges();
        return new StoreResult
        {
            Ok = true,
            QuestId = questId,
            ContentVersion = row.ContentVersion,
            Message = publishState == (int)QuestPublishState.Online ? "已发布。" : "已关闭。",
        };
    }

    /// <summary>作废一行（软删）：标 IsValid=0 并强制下线，避免"删掉正在跑的任务"。</summary>
    public StoreResult Retire(int questId)
    {
        var row = _db.QuestTemplates.FirstOrDefault(item => item.Id == questId);
        if (row == null) return new StoreResult { Ok = false, QuestId = questId, Message = "任务模板不存在。" };

        row.IsValid = false;
        row.PublishState = (int)QuestPublishState.Offline;
        row.ContentVersion = row.ContentVersion + 1;
        _db.SaveChanges();
        _questIds = null;
        return new StoreResult { Ok = true, QuestId = questId, ContentVersion = row.ContentVersion, Message = "已作废并下线。" };
    }

    /// <summary>
    /// 物理删除一行（**只允许删已作废的**）。
    /// <para>
    /// 为什么必须先作废：作废的语义是"下线 + 停掉新接取"，先走一步能让"从这一刻起没人再接得到"
    /// 这件事在界面上被看见；直接删一条还在线的模板等于跳过那道确认。
    /// </para>
    /// <para>
    /// 删掉之后**已接这条任务的角色不受影响**，这不是运气而是结构决定的：接取时角色状态就把
    /// 名字/描述/目标/奖励抄进了自己的 <c>QuestData</c>，而完成路径（<c>QuestGrain.CompleteQuestAsync</c>）
    /// 从不回头查模板表——模板上下文 <c>_templates</c> 在 <c>QuestGrain</c> 里只有
    /// <c>AcceptFromTemplateAsync</c> 一个使用者（由 `QuestGrainLockDiscipline` 同类的源码扫描闸门钉住）。
    /// 所以持有者照样能推进、提交、查看详情；受影响的只是"还没接的人"——他们从此接不到，这正是删除的意图。
    /// </para>
    /// </summary>
    public StoreResult Delete(int questId)
    {
        var row = _db.QuestTemplates.FirstOrDefault(item => item.Id == questId);
        if (row == null) return new StoreResult { Ok = false, QuestId = questId, Message = "任务模板不存在（可能已经删过）。" };

        if (row.IsValid)
        {
            return new StoreResult
            {
                Ok = false,
                QuestId = questId,
                Message = "这条任务还在有效状态，不能直接删除：请先「作废」（会一并下线），确认不再有人接得到之后再删。",
            };
        }

        _db.QuestTemplates.Remove(row);
        _db.SaveChanges();
        _questIds = null;
        return new StoreResult
        {
            Ok = true,
            QuestId = questId,
            Message = "已物理删除。已接这条任务的角色不受影响：仍能继续推进、提交并查看详情。",
        };
    }

    /// <inheritdoc />
    public void Dispose() => _db.Dispose();
}

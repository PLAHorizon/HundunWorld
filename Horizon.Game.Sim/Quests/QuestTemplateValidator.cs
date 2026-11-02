using System.Collections.Generic;

namespace Horizon.Game.Sim.Quests;

/// <summary>校验问题的严重度。</summary>
public enum QuestIssueLevel : byte
{
    /// <summary>提示：可以入库，但要人看一眼。</summary>
    Info = 0,
    /// <summary>警告：可以入库，行为可能与预期不同。</summary>
    Warning = 1,
    /// <summary>错误：不允许入库/不允许发布。</summary>
    Error = 2,
}

/// <summary>一条校验结果。</summary>
/// <param name="Code">稳定编号，编辑器与日志都按它定位（改名会断掉测试与文档的引用）。</param>
/// <param name="Level">严重度。</param>
/// <param name="Message">中文说明，直接进编辑器界面。</param>
/// <param name="Path">指到草稿里的哪一行，如 <c>objectives[0]</c>。</param>
public readonly record struct QuestIssue(string Code, QuestIssueLevel Level, string Message, string Path);

/// <summary>一次校验的汇总结果。</summary>
public sealed class QuestValidationResult
{
    /// <summary>全部条目。</summary>
    public List<QuestIssue> Issues { get; } = new List<QuestIssue>();

    /// <summary>是否存在错误级条目。</summary>
    public bool HasErrors => HasError();

    /// <summary>是否存在错误级条目（方法形式，供表达式内使用）。</summary>
    public bool HasError()
    {
        foreach (var issue in Issues)
        {
            if (issue.Level == QuestIssueLevel.Error) return true;
        }
        return false;
    }

    /// <summary>把结果压成多行文本（CLI 输出用）。</summary>
    public string ToText()
    {
        if (Issues.Count == 0) return "校验通过，无问题。";
        var lines = new List<string>(Issues.Count);
        foreach (var issue in Issues)
        {
            lines.Add("[" + issue.Code + "/" + issue.Level + "] " + issue.Path + "：" + issue.Message);
        }
        return string.Join("\n", lines);
    }
}

/// <summary>引用是否查得到。</summary>
public enum QuestReferenceState : byte
{
    /// <summary>确认存在。</summary>
    Exists = 0,
    /// <summary>确认不存在。</summary>
    Missing = 1,
    /// <summary>这一侧没有判据（例如离线校验时不知道物品表里有没有这个 id）。</summary>
    Unknown = 2,
}

/// <summary>
/// 引用对账口：NPC/怪物走静态目录，物品与既有任务走数据库。
/// <para>做成接口是为了让"校验器只有一份实现"——CLI 入库前、Grain 发布前、
/// 单元测试里用的都是同一套规则，只是喂进去的对账能力不同。</para>
/// </summary>
public interface IQuestReferenceIndex
{
    /// <summary>查 NPC 模板。</summary>
    QuestReferenceState Npc(int templateId);
    /// <summary>查怪物模板。</summary>
    QuestReferenceState Monster(int templateId);
    /// <summary>查物品模板（装备/材料/丹药/宝石/任务物品/秘籍都在这个 id 空间里）。</summary>
    QuestReferenceState Item(int templateId);
    /// <summary>查任务模板（前置任务用）。</summary>
    QuestReferenceState Quest(int questId);
}

/// <summary>
/// 只依赖 <see cref="Horizon.Game.Sim"/> 静态目录的最小对账实现：物品与任务一律返回
/// <see cref="QuestReferenceState.Unknown"/>，因此离线校验不会把"查不到"误报成"不存在"。
/// </summary>
public sealed class StaticQuestReferenceIndex : IQuestReferenceIndex
{
    /// <inheritdoc />
    public QuestReferenceState Npc(int templateId)
        => NpcTemplateCatalog.TryGetRegistered(templateId, out _) ? QuestReferenceState.Exists : QuestReferenceState.Missing;

    /// <inheritdoc />
    public QuestReferenceState Monster(int templateId)
        => MonsterTemplateCatalog.TryGetRegistered(templateId, out _) ? QuestReferenceState.Exists : QuestReferenceState.Missing;

    /// <inheritdoc />
    public QuestReferenceState Item(int templateId) => QuestReferenceState.Unknown;

    /// <inheritdoc />
    public QuestReferenceState Quest(int questId) => QuestReferenceState.Unknown;

    /// <summary>共享实例（无状态，可直接复用）。</summary>
    public static readonly IQuestReferenceIndex Default = new StaticQuestReferenceIndex();
}

/// <summary>
/// 任务草稿校验器。
/// <para>
/// <b>为什么错误级条目要拦住发布</b>：任务这条链最坏的失败不是报错，而是"看起来上线了但玩家什么也做不了"——
/// 没有发布者可接、没有目标可推、奖励发空、或者条件根本没有事件源而永远卡在"进行中"。
/// 这几种都在入库前判死，比让玩家在现场发现便宜得多。
/// </para>
/// </summary>
public static class QuestTemplateValidator
{
    /// <summary>五行域上限。</summary>
    public const int MaxWuxingDomain = 5;

    /// <summary>等级门槛上限（与角色等级上限同量级，写错成几万时能立刻看见）。</summary>
    public const int MaxLevelRequire = 999;

    /// <summary>
    /// 校验一份草稿。
    /// </summary>
    /// <param name="draft">待校验草稿。</param>
    /// <param name="index">引用对账口；null 用静态目录实现（物品/任务只给 Unknown，不报错）。</param>
    /// <param name="requirePublishable">true 时按"允许发布上线"的标准判（要求目标与奖励非空、状态合法）。</param>
    public static QuestValidationResult Validate(QuestDraft? draft, IQuestReferenceIndex? index = null, bool requirePublishable = false)
    {
        var result = new QuestValidationResult();
        var refs = index ?? StaticQuestReferenceIndex.Default;
        if (draft == null)
        {
            result.Issues.Add(new QuestIssue("Q000", QuestIssueLevel.Error, "草稿为空。", "-"));
            return result;
        }

        if (draft.QuestId <= 0)
            result.Issues.Add(new QuestIssue("Q001", QuestIssueLevel.Error, "任务 id 必须是正整数。", "questId"));
        if (string.IsNullOrWhiteSpace(draft.QuestName))
            result.Issues.Add(new QuestIssue("Q002", QuestIssueLevel.Error, "任务名不能为空。", "questName"));
        if (draft.QuestName?.Length > 50)
            result.Issues.Add(new QuestIssue("Q003", QuestIssueLevel.Error, "任务名超过 50 字（列宽 nvarchar(50)）。", "questName"));
        if (draft.Description?.Length > 500)
            result.Issues.Add(new QuestIssue("Q004", QuestIssueLevel.Error, "任务描述超过 500 字（列宽 nvarchar(500)）。", "description"));
        if (draft.QuestType < 0 || draft.QuestType > 3)
            result.Issues.Add(new QuestIssue("Q005", QuestIssueLevel.Error, "任务类型只能是 0主线/1支线/2日常/3周常。", "questType"));
        if (draft.LevelRequire < 0 || draft.LevelRequire > MaxLevelRequire)
            result.Issues.Add(new QuestIssue("Q006", QuestIssueLevel.Error, $"接取等级门槛需在 0..{MaxLevelRequire}。", "levelRequire"));
        if (draft.WuxingDomain < 0 || draft.WuxingDomain > MaxWuxingDomain)
            result.Issues.Add(new QuestIssue("Q007", QuestIssueLevel.Error, $"五行域只能是 0（通用）或 1..{MaxWuxingDomain}。", "wuxingDomain"));
        if (draft.RepeatLimit < 0)
            result.Issues.Add(new QuestIssue("Q008", QuestIssueLevel.Error, "可重复次数不能为负。", "repeatLimit"));
        if (draft.PublishState != (int)QuestPublishState.Offline && draft.PublishState != (int)QuestPublishState.Online)
            result.Issues.Add(new QuestIssue("Q009", QuestIssueLevel.Error, "发布状态只能是 0下线/1已发布。", "publishState"));

        ValidateEntityRef(result, draft.Giver, "giver", refs, isGiver: true);
        if (draft.TurnIn != null && !draft.TurnIn.IsEmpty)
            ValidateEntityRef(result, draft.TurnIn, "turnIn", refs, isGiver: true);

        if (draft.Objectives.Count == 0)
        {
            result.Issues.Add(new QuestIssue("Q020",
                requirePublishable || draft.PublishState == (int)QuestPublishState.Online ? QuestIssueLevel.Error : QuestIssueLevel.Warning,
                "没有任何完成条件：接了就能交，这条产出通路是空转的。", "objectives"));
        }

        for (var i = 0; i < draft.Objectives.Count; i++)
        {
            var path = "objectives[" + i + "]";
            var objective = draft.Objectives[i];
            if (objective == null)
            {
                result.Issues.Add(new QuestIssue("Q021", QuestIssueLevel.Error, "目标条目为空。", path));
                continue;
            }

            var kind = objective.KindOrNone();
            if (kind == null)
            {
                result.Issues.Add(new QuestIssue("Q022", QuestIssueLevel.Error,
                    "无法识别的目标类别：" + objective.Kind + "（可用：" + VerbList() + "）。", path));
                continue;
            }

            if (objective.Count <= 0)
                result.Issues.Add(new QuestIssue("Q023", QuestIssueLevel.Error, "目标数量必须大于 0。", path + ".count"));

            if (objective.ToTypeString() == null)
            {
                if (kind == QuestObjectiveKind.KillDomain)
                    result.Issues.Add(new QuestIssue("Q024", QuestIssueLevel.Error,
                        $"域击杀目标的 templateId 是五行域号，需在 1..{MaxWuxingDomain}。", path + ".templateId"));
                else
                    result.Issues.Add(new QuestIssue("Q025", QuestIssueLevel.Error, "目标模板 id 必须是正整数。", path + ".templateId"));
            }

            if (!QuestObjectiveCodec.HasProducer(kind.Value))
            {
                result.Issues.Add(new QuestIssue("Q026", QuestIssueLevel.Error,
                    "该目标类型目前没有事件源（服务端不会推进它），接了会永久卡在\"进行中\"。", path + ".kind"));
            }

            if (kind == QuestObjectiveKind.TalkNpc
                && refs.Npc(objective.TemplateId) == QuestReferenceState.Missing)
            {
                result.Issues.Add(new QuestIssue("Q027", QuestIssueLevel.Error,
                    "引用的 NPC 模板未在目录登记（未登记即拒绝，运行时不会有这个对象）。", path + ".templateId"));
            }

            // 触发点已接（客户端选中对象后按 G 发交互请求），剩下的前提都是**部署态**而不是代码缺口：
            // DS 与网关必须已重启到"实体身份上行"这一版，否则服务端解不出身份，目标恒 0/N 且没有任何报错。
            // 这条信息写成 Info 常驻在校验输出里，是为了让"任务发不出去"在现场能被第一时间归因。
            if (kind is QuestObjectiveKind.TalkNpc or QuestObjectiveKind.InteractProp)
            {
                result.Issues.Add(new QuestIssue("Q039", QuestIssueLevel.Info,
                    "这类目标要玩家选中该对象后按 G 触发；且需 Silo/Gateway/DsHost 都已重启到带实体身份上行的版本，否则服务端会判 unknown_entity 而推不动。",
                    path + ".kind"));
            }

            if (kind == QuestObjectiveKind.KillMonster && objective.TemplateId > 0)
            {
                var monster = refs.Monster(objective.TemplateId);
                if (monster == QuestReferenceState.Missing)
                    result.Issues.Add(new QuestIssue("Q028", QuestIssueLevel.Error,
                        "引用的怪物模板未在目录登记。", path + ".templateId"));
                else if (monster == QuestReferenceState.Unknown)
                    result.Issues.Add(new QuestIssue("Q029", QuestIssueLevel.Info,
                        "离线校验无法确认该怪物模板是否存在，入库时会再对账一次。", path + ".templateId"));
            }

            if (string.IsNullOrWhiteSpace(objective.Description))
                result.Issues.Add(new QuestIssue("Q030", QuestIssueLevel.Warning, "目标没有描述，玩家只会看到一条空文本。", path + ".desc"));
        }

        ValidateRewards(result, draft, refs, requirePublishable);

        for (var i = 0; i < draft.PrerequisiteQuests.Count; i++)
        {
            var path = "prerequisiteQuests[" + i + "]";
            var prerequisite = draft.PrerequisiteQuests[i];
            if (prerequisite <= 0)
                result.Issues.Add(new QuestIssue("Q040", QuestIssueLevel.Error, "前置任务 id 必须是正整数。", path));
            else if (prerequisite == draft.QuestId)
                result.Issues.Add(new QuestIssue("Q041", QuestIssueLevel.Error, "前置任务不能是自己。", path));
            else if (refs.Quest(prerequisite) == QuestReferenceState.Missing)
                result.Issues.Add(new QuestIssue("Q042", QuestIssueLevel.Error, "前置任务在模板表里不存在。", path));
        }

        if (requirePublishable && draft.PublishState != (int)QuestPublishState.Online)
        {
            result.Issues.Add(new QuestIssue("Q050", QuestIssueLevel.Error, "该任务当前是下线状态，不能发布。", "publishState"));
        }

        return result;
    }

    private static void ValidateEntityRef(QuestValidationResult result, QuestEntityRefDraft reference, string path, IQuestReferenceIndex index, bool isGiver)
    {
        if (!QuestEntityRef.TryParseKind(reference.Kind, out var kind))
        {
            result.Issues.Add(new QuestIssue("Q010", QuestIssueLevel.Error,
                "无法识别的实体类别：" + reference.Kind + "（可用：npc/monster/item/prop/zone）。", path + ".kind"));
            return;
        }

        if (reference.TemplateId <= 0)
        {
            result.Issues.Add(new QuestIssue("Q011", QuestIssueLevel.Error, "模板 id 必须是正整数。", path + ".templateId"));
            return;
        }

        if (isGiver && !QuestEntityRef.CanBeGiver(kind))
        {
            result.Issues.Add(new QuestIssue("Q012", QuestIssueLevel.Error,
                "发布者只能是 npc/item/prop，怪物与地点不能发布任务。", path + ".kind"));
        }

        switch (kind)
        {
            case QuestEntityKind.Npc:
                if (index.Npc(reference.TemplateId) == QuestReferenceState.Missing)
                    result.Issues.Add(new QuestIssue("Q013", QuestIssueLevel.Error,
                        "该 NPC 模板未登记；任务编辑器只允许关联运行时真的存在的对象。", path + ".templateId"));
                break;
            case QuestEntityKind.Item:
                if (index.Item(reference.TemplateId) == QuestReferenceState.Missing)
                    result.Issues.Add(new QuestIssue("Q014", QuestIssueLevel.Error, "该物品模板不存在。", path + ".templateId"));
                if (isGiver)
                {
                    // 物品**可以**被写成发布者（`CanBeGiver` 允许，显示名也能从库里解析出来），但世界里的
                    // 实体身份只由 DS 上行，而 DS 只报四类：怪物 / NPC / 事件物件(prop) / 触发区
                    // （`DsEntityRegistry.ToQuestKind` 永远不会产出 item）。所以这条任务不可能被
                    // "走近某个对象 → 只看这个对象给的任务"那条按实体 id 收窄的通路命中，只在总表里出现。
                    // 刻意不判 Error：目标要求"发布者可以是某个物品"，这条先要写得下来；真修要么给 DS 加一类
                    // 物品型脚本实体，要么在 prop 与物品模板 id 之间建立对应，见 TECH_DEBT T11。
                    // 这条 Info 常驻校验输出，是为了让"任务明明挂在物品上，走近怎么看不见"能当场归因。
                    result.Issues.Add(new QuestIssue("Q043", QuestIssueLevel.Info,
                        "发布者是物品：世界里的实体身份只由 DS 上报（怪物/NPC/物件/触发区四类），解不出 item，" +
                        "所以这条不会命中“走近发布者”的收窄可接列表，只会在总表里出现（详见 TECH_DEBT T11）。",
                        path + ".kind"));
                }
                break;
            case QuestEntityKind.Monster:
                if (index.Monster(reference.TemplateId) == QuestReferenceState.Missing)
                    result.Issues.Add(new QuestIssue("Q015", QuestIssueLevel.Error, "该怪物模板未登记。", path + ".templateId"));
                break;
        }

        if (reference.ZoneId < 0 || reference.SpawnPointId < 0)
            result.Issues.Add(new QuestIssue("Q016", QuestIssueLevel.Error, "区域/生成点限定不能为负。", path));
    }

    private static void ValidateRewards(QuestValidationResult result, QuestDraft draft, IQuestReferenceIndex index, bool requirePublishable)
    {
        var rewards = draft.Rewards ?? new QuestRewardSpec();

        if (rewards.IsEmpty)
        {
            result.Issues.Add(new QuestIssue("Q031",
                requirePublishable || draft.PublishState == (int)QuestPublishState.Online ? QuestIssueLevel.Error : QuestIssueLevel.Warning,
                "奖励全空：任务完成不会给出任何东西。", "rewards"));
        }

        for (var i = 0; i < rewards.Items.Count; i++)
        {
            var path = "rewards.items[" + i + "]";
            var item = rewards.Items[i];
            if (item.TemplateId <= 0)
                result.Issues.Add(new QuestIssue("Q032", QuestIssueLevel.Error, "奖励物品模板 id 必须是正整数。", path + ".templateId"));
            if (item.Count <= 0)
                result.Issues.Add(new QuestIssue("Q033", QuestIssueLevel.Error, "奖励物品数量必须大于 0。", path + ".count"));

            switch (index.Item(item.TemplateId))
            {
                case QuestReferenceState.Missing:
                    result.Issues.Add(new QuestIssue("Q034", QuestIssueLevel.Error,
                        "奖励引用的物品模板不存在；现场表现是\"交了任务但东西没到手\"。", path + ".templateId"));
                    break;
                case QuestReferenceState.Unknown when item.TemplateId > 0:
                    result.Issues.Add(new QuestIssue("Q035", QuestIssueLevel.Info,
                        "离线校验无法确认该物品是否存在（id>0 才会入库），连库时会再对账。", path + ".templateId"));
                    break;
            }
        }

        for (var i = 0; i < rewards.Favor.Count; i++)
        {
            var path = "rewards.favor[" + i + "]";
            var favor = rewards.Favor[i];
            if (favor.Domain < 1 || favor.Domain > MaxWuxingDomain)
                result.Issues.Add(new QuestIssue("Q036", QuestIssueLevel.Error,
                    $"好感域号需在 1..{MaxWuxingDomain}（越界会静默加到一个没人读的域上）。", path + ".domain"));
            if (favor.Points <= 0)
                result.Issues.Add(new QuestIssue("Q037", QuestIssueLevel.Error, "好感点数必须大于 0。", path + ".points"));
        }

        var rejected = QuestRewardSpec.ParseRejectedKeyCount(rewards.ToJson());
        if (rejected > 0)
            result.Issues.Add(new QuestIssue("Q038", QuestIssueLevel.Warning,
                "有 " + rejected + " 条奖励被编码丢弃（数量≤0 或域号越界），它们不会写进库。", "rewards"));
    }

    private static string VerbList()
    {
        var verbs = new List<string>();
        foreach (QuestObjectiveKind kind in System.Enum.GetValues(typeof(QuestObjectiveKind)))
        {
            var verb = QuestObjectiveCodec.VerbOf(kind);
            if (verb != null) verbs.Add(verb);
        }
        return string.Join(", ", verbs);
    }
}

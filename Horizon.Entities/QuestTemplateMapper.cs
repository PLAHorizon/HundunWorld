using System;
using System.Collections.Generic;
using Horizon.Game.Sim.Quests;
using Horizon.Model.GameModel;

namespace Horizon.Entities;

/// <summary>
/// <see cref="QuestTemplateEntity"/>（数据库列）↔ <see cref="QuestDraft"/>（编辑器/CLI 草稿）的双向映射。
/// <para>
/// 放在数据访问层（而不是 Sim）：Sim 是零存储的确定性层，不能引用 EF 实体；
/// 放在 Grains 里又只有服务端能用。映射规则必须只有一份实现——任务编辑器写进去的
/// 与运行时 Grain 读出来的如果各写一遍，早晚会分叉。
/// </para>
/// </summary>
public static class QuestTemplateMapper
{
    /// <summary>库行 → 草稿（编辑器读路径 / 发布前校验用）。</summary>
    public static QuestDraft ToDraft(QuestTemplateEntity row)
    {
        if (row == null) throw new ArgumentNullException(nameof(row));

        var giverKind = (QuestEntityKind)row.GiverKind;
        if (giverKind == 0) giverKind = QuestEntityKind.Npc;

        var draft = new QuestDraft
        {
            QuestId = row.Id,
            QuestName = row.QuestName ?? string.Empty,
            Description = row.Description ?? string.Empty,
            QuestType = row.QuestType,
            LevelRequire = row.LevelRequire,
            WuxingDomain = row.WuxingDomain,
            RepeatLimit = row.RepeatLimit,
            PublishState = row.PublishState,
            IsValid = row.IsValid,
            ContentVersion = row.ContentVersion,
            Giver = QuestEntityRefDraft.FromRef(new QuestEntityRef(giverKind, row.GiverTemplateId, row.GiverZoneId, row.GiverSpawnPointId)),
            PrerequisiteQuests = QuestDraft.ParsePrerequisites(row.PrerequisiteQuests),
            Objectives = QuestDraft.ParseObjectives(row.Objectives),
            Rewards = QuestRewardSpec.FromJson(row.Rewards),
        };

        if (row.TurnInKind != 0 && row.TurnInTemplateId > 0)
        {
            draft.TurnIn = QuestEntityRefDraft.FromRef(new QuestEntityRef((QuestEntityKind)row.TurnInKind, row.TurnInTemplateId));
        }

        return draft;
    }

    /// <summary>
    /// 草稿 → 库行字段。
    /// <para>这里<b>不做校验</b>：能不能入库由 <see cref="QuestTemplateValidator"/> 决定，
    /// 映射只负责"把已经通过校验的意图如实写成列"。</para>
    /// </summary>
    public static void Apply(QuestDraft draft, QuestTemplateEntity row)
    {
        if (draft == null) throw new ArgumentNullException(nameof(draft));
        if (row == null) throw new ArgumentNullException(nameof(row));

        row.QuestName = Trim(draft.QuestName, 50);
        row.Description = Trim(draft.Description, 500);
        row.QuestType = draft.QuestType;
        row.LevelRequire = draft.LevelRequire;
        row.WuxingDomain = draft.WuxingDomain;
        row.RepeatLimit = draft.RepeatLimit;
        row.PublishState = draft.PublishState;
        row.IsValid = draft.IsValid;

        var giver = draft.Giver.ToRef();
        row.GiverKind = (int)giver.Kind;
        row.GiverTemplateId = giver.TemplateId;
        row.GiverZoneId = giver.ZoneId;
        row.GiverSpawnPointId = giver.SpawnPointId;

        var turnIn = draft.TurnIn;
        if (turnIn != null && !turnIn.IsEmpty)
        {
            var reference = turnIn.ToRef();
            row.TurnInKind = (int)reference.Kind;
            row.TurnInTemplateId = reference.TemplateId;
        }
        else
        {
            row.TurnInKind = 0;
            row.TurnInTemplateId = 0;
        }

        row.PrerequisiteQuests = Trim(draft.BuildPrerequisitesCsv(), 200);
        row.Objectives = draft.BuildObjectivesJson();
        row.Rewards = draft.Rewards.ToJson();
    }

    /// <summary>由草稿造一行新记录（Id 由调用方决定：IDENTITY_INSERT 打开时才允许显式 id）。</summary>
    public static QuestTemplateEntity NewRow(QuestDraft draft)
    {
        var row = new QuestTemplateEntity { IsValid = true };
        Apply(draft, row);
        row.Id = draft.QuestId;
        return row;
    }

    private static string Trim(string? value, int max)
    {
        value ??= string.Empty;
        return value.Length <= max ? value : value.Substring(0, max);
    }
}

using System;
using System.Collections.Generic;
using Horizon.Orleans.Interface.World;

namespace Horizon.Game.Core.Sim.Server;

/// <summary>
/// 头顶任务符号在**网关侧**要补的两个事实：这位发布者名下"条件不足"的任务有几条、
/// 以及它是不是某条任务的交付者。
/// <para>
/// <b>为什么要在这里算</b>：可接清单由 Grain 按等级/前置过滤，被挡下的任务客户端根本看不到；
/// 而交付者身份只存在于任务目录。两者都不能让客户端猜（猜等级等于把内容暴露给客户端，
/// 猜交付者等于把"能不能交"交给客户端）。网关手里既有人物等级与完成/进行中集合，
/// 又有目录快照，所以事实在这里一次算清、只把**结论**下发。
/// </para>
/// <para><b>纯函数</b>：不查库、不调 Grain，单测可以直接喂合成目录。</para>
/// </summary>
public static class QuestIndicatorOfferCore
{
    /// <summary>"接不了"的两种原因。<b>总数 = 两者之和</b>（同一条任务只进一档，先判等级）。</summary>
    public readonly record struct LockedOffers(int ByLevel, int ByPrereq)
    {
        public int Total => ByLevel + ByPrereq;
    }

    /// <summary>
    /// 统计"这位发布者当前接不了"的任务条数。
    /// <para>口径与 <c>GetOfferedQuestsAsync</c> 的过滤**逐条对齐**：只算已发布、发布者匹配、
    /// 未被这个角色持有/已完成（可重复任务交过仍算可接，不进"接不了"）。</para>
    /// </summary>
    public static LockedOffers CountLocked(
        QuestCatalogEntry[]? entries,
        int giverKind,
        int giverTemplateId,
        int playerLevel,
        IReadOnlyCollection<int>? completedQuestIds,
        IReadOnlyCollection<int>? activeQuestIds)
    {
        if (entries == null || entries.Length == 0) return default;
        if (giverKind <= 0 || giverTemplateId <= 0) return default;

        var completed = completedQuestIds == null ? new HashSet<int>() : new HashSet<int>(completedQuestIds);
        var active = activeQuestIds == null ? new HashSet<int>() : new HashSet<int>(activeQuestIds);

        int byLevel = 0, byPrereq = 0;
        for (var i = 0; i < entries.Length; i++)
        {
            var entry = entries[i];
            if (entry == null) continue;
            if (entry.PublishState != 1) continue;                       // 0 = 下线
            if (entry.GiverKind != giverKind || entry.GiverTemplateId != giverTemplateId) continue;
            if (active.Contains(entry.QuestId)) continue;                // 已接：那是"进行中"，不是"接不了"
            if (completed.Contains(entry.QuestId) && entry.RepeatLimit == 0) continue; // 交过且不可重复

            if (entry.LevelRequire > playerLevel)
            {
                byLevel++;
                continue;
            }

            var prereqMet = true;
            var prerequisites = entry.PrerequisiteQuestIds;
            if (prerequisites != null)
            {
                for (var p = 0; p < prerequisites.Length; p++)
                {
                    if (completed.Contains(prerequisites[p])) continue;
                    prereqMet = false;
                    break;
                }
            }

            if (!prereqMet) byPrereq++;
        }

        return new LockedOffers(byLevel, byPrereq);
    }

    /// <summary>
    /// 这个发布者是不是这条任务的交付者。目录里交付者留空（kind 与模板 id 都 0）= 沿用发布者。
    /// </summary>
    public static bool IsTurnInHere(QuestCatalogEntry? entry, int giverKind, int giverTemplateId)
    {
        if (entry == null || giverKind <= 0 || giverTemplateId <= 0) return false;

        var turnInKind = entry.TurnInKind;
        var turnInTemplateId = entry.TurnInTemplateId;
        if (turnInKind <= 0 || turnInTemplateId <= 0)
        {
            turnInKind = entry.GiverKind;
            turnInTemplateId = entry.GiverTemplateId;
        }

        return turnInKind == giverKind && turnInTemplateId == giverTemplateId;
    }
}

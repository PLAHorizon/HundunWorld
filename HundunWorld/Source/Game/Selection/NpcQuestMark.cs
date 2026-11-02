using System;
using System.Collections.Generic;
using Horizon.Game.Message.Network;

namespace HundunWorld.Game.Selection
{
    /// <summary>头顶任务符号（与服务端 <c>NpcQuestIndicatorCore</c> 同形，输入是**服务端裁决过的**清单与交付者结论）。</summary>
    public enum NpcQuestMarkKind : byte
    {
        /// <summary>不显示。</summary>
        None = 0,

        /// <summary>黄色 <c>?</c>：这个 NPC 是交付者，且目标已达成。</summary>
        TurnIn = 1,

        /// <summary>黄色 <c>!</c>：有可接的任务。</summary>
        Available = 2,

        /// <summary>灰色 <c>?</c>：我是交付者，但目标还没做完。</summary>
        InProgress = 3,

        /// <summary>灰色 <c>!</c>：这位有任务，但我的等级/前置还没到（服务端只回条数）。</summary>
        Locked = 4,
    }

    /// <summary>
    /// 由"这位 NPC 能给的清单 + 我身上的任务 + 服务端给的交付者集合 + 条件不足条数"推出头顶符号。
    /// <para>
    /// <b>这里不做接取/交付判定</b>：能不能接、能不能交、谁是交付者、有几条被等级/前置挡下，
    /// 全部由服务端算好（<c>QuestListResponse.LockedOfferCount</c> 与 <c>QuestSummary.TurnInHere</c>）。
    /// 本类只按 WoW 的优先级合成一个图形：可交付 &gt; 可接 &gt; 进行中 &gt; 条件不足。
    /// </para>
    /// <para>
    /// <b>交付者集合必须按 NPC 传</b>：身上的任务清单是全局的，而"这条任务在哪交"是**相对某个 NPC** 的结论。
    /// 直接读全局清单上的标记，会把 A 的交付结论画到 B 头上。
    /// </para>
    /// </summary>
    public static class NpcQuestMark
    {
        /// <summary>合成符号与数量（数字前缀 = 同类任务条数，单条时调用方不画数字）。</summary>
        /// <param name="offers">这位 NPC 的服务端清单（已过滤等级/前置）。</param>
        /// <param name="quests">我身上的任务（全局）。</param>
        /// <param name="lockedOfferCount">服务端回的"这位名下条件不足条数"。</param>
        /// <param name="turnInHereQuestIds">服务端认定"交付者就是这位"的任务号集合。</param>
        public static (NpcQuestMarkKind Kind, int Count) Resolve(
            QuestOfferInfo[] offers,
            QuestSummary[] quests,
            int lockedOfferCount,
            IReadOnlyCollection<int> turnInHereQuestIds)
        {
            int available = 0, turnIn = 0, inProgress = 0;
            var seen = new HashSet<int>();

            if (offers != null)
            {
                for (var i = 0; i < offers.Length; i++)
                {
                    var offer = offers[i];
                    if (offer == null || offer.QuestId <= 0) continue;

                    if (!offer.AlreadyAccepted && !offer.AlreadyFinished)
                    {
                        available++;
                        seen.Add(offer.QuestId);
                        continue;
                    }

                    if (!offer.AlreadyAccepted) continue;   // 交过且不可重复：不提示

                    seen.Add(offer.QuestId);
                    if (!Contains(turnInHereQuestIds, offer.QuestId)) continue; // 交付者在别处：这里不画

                    var quest = Find(quests, offer.QuestId);
                    if (quest == null) continue;
                    if (AllObjectivesDone(quest)) turnIn++;
                    else inProgress++;
                }
            }

            // 在**交付者**面前时，它的清单按 giver 过滤，本来就拿不到这条已接任务——
            // 所以身上的任务也要参与（以服务端给的交付者集合为准）。
            if (quests != null)
            {
                for (var i = 0; i < quests.Length; i++)
                {
                    var quest = quests[i];
                    if (quest == null || quest.QuestId <= 0) continue;
                    if (!Contains(turnInHereQuestIds, quest.QuestId)) continue;
                    if (seen.Contains(quest.QuestId)) continue;
                    if (quest.Status == 2) continue;        // 已交付的旧记录
                    if (AllObjectivesDone(quest)) turnIn++;
                    else inProgress++;
                }
            }

            if (turnIn > 0) return (NpcQuestMarkKind.TurnIn, turnIn);
            if (available > 0) return (NpcQuestMarkKind.Available, available);
            if (inProgress > 0) return (NpcQuestMarkKind.InProgress, inProgress);
            if (lockedOfferCount > 0) return (NpcQuestMarkKind.Locked, lockedOfferCount);
            return (NpcQuestMarkKind.None, 0);
        }

        private static bool Contains(IReadOnlyCollection<int> ids, int questId)
        {
            if (ids == null || ids.Count == 0) return false;
            if (ids is HashSet<int> set) return set.Contains(questId);
            foreach (var id in ids)
            {
                if (id == questId) return true;
            }

            return false;
        }

        /// <summary>符号本体（客户端据 Kind 画图；数量 &gt; 1 时在角落画数字）。</summary>
        public static string GlyphOf(NpcQuestMarkKind kind) => kind switch
        {
            NpcQuestMarkKind.TurnIn => "?",
            NpcQuestMarkKind.Available => "!",
            NpcQuestMarkKind.InProgress => "?",
            NpcQuestMarkKind.Locked => "!",
            _ => string.Empty,
        };

        /// <summary>目标是否全部完成。<b>没有逐条目标的旧数据按"未完成"</b>——宁可显示灰色问号，不要谎报可交付。</summary>
        private static bool AllObjectivesDone(QuestSummary quest)
        {
            if (quest.Objectives == null || quest.Objectives.Count == 0) return false;

            for (var i = 0; i < quest.Objectives.Count; i++)
            {
                if (quest.Objectives[i] == null || !quest.Objectives[i].Completed) return false;
            }

            return true;
        }

        private static QuestSummary Find(QuestSummary[] quests, int questId)
        {
            if (quests == null || questId <= 0) return null;
            for (var i = 0; i < quests.Length; i++)
            {
                if (quests[i] != null && quests[i].QuestId == questId) return quests[i];
            }

            return null;
        }
    }
}

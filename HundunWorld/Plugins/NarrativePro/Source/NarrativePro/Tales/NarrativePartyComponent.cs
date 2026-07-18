using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.Tales.Data;
using NarrativePro.Tales.Nodes;
using DialogueClass = NarrativePro.Tales.Dialogue.Dialogue;

namespace NarrativePro.Tales
{
    /// <summary>
    /// 队伍对话控制策略。对应 UE5 EPartyDialogueControlPolicy。
    /// </summary>
    public enum EPartyDialogueControlPolicy
    {
        /// <summary>仅队长可选择对话选项，其他成员观战。</summary>
        PartyLeaderControlled,

        /// <summary>任何队员都可选对话选项，选者摄像机会切到对应玩家。</summary>
        AllPlayers
    }

    /// <summary>
    /// 队伍叙事组件。对应 UE5 UNarrativePartyComponent。
    /// 多个客户端共享的 TalesComponent，队友可共同参与对话/任务。
    /// Flax 中无网络复制，使用本地列表 + 事件回调。
    /// </summary>
    public class NarrativePartyComponent : TalesComponent
    {
        /// <summary>队伍对话控制策略。</summary>
        public EPartyDialogueControlPolicy PartyDialogueControlPolicy = EPartyDialogueControlPolicy.PartyLeaderControlled;

        /// <summary>队伍成员的 TalesComponent 列表（Flax 中无复制，运行期维护）。</summary>
        public List<TalesComponent> PartyMembers = new List<TalesComponent>();

        /// <summary>队伍成员对应的 Pawn/Actor 列表（替代 UE5 PartyMemberStates）。</summary>
        public List<Actor> PartyMemberPawns = new List<Actor>();

        /// <summary>是否为队伍组件（UE5 中用于区分普通 TalesComponent）。</summary>
        public virtual bool IsPartyComponent()
        {
            return true;
        }

        /// <summary>添加队伍成员。返回是否成功。</summary>
        public virtual bool AddPartyMember(TalesComponent member)
        {
            if (member == null) return false;
            if (PartyMembers.Contains(member)) return false;
            PartyMembers.Add(member);
            PartyMemberPawns.Add(member?.Actor);
            return true;
        }

        /// <summary>移除队伍成员。返回是否成功。</summary>
        public virtual bool RemovePartyMember(TalesComponent member)
        {
            if (member == null) return false;
            int idx = PartyMembers.IndexOf(member);
            if (idx < 0) return false;
            PartyMembers.RemoveAt(idx);
            if (idx < PartyMemberPawns.Count)
            {
                PartyMemberPawns.RemoveAt(idx);
            }
            return true;
        }

        /// <summary>返回所有队伍成员。</summary>
        public List<TalesComponent> GetPartyMembers()
        {
            return PartyMembers;
        }

        /// <summary>返回所有队伍成员对应的 Pawn。</summary>
        public List<Actor> GetPartyMemberPawns()
        {
            return PartyMemberPawns;
        }

        /// <summary>返回队伍队长（队伍为空时返回 null）。</summary>
        public TalesComponent GetPartyLeader()
        {
            return PartyMembers.Count > 0 ? PartyMembers[0] : null;
        }

        /// <summary>
        /// 判断给定成员是否为队长。非队伍成员视为队长（独立时即队长）。
        /// </summary>
        public bool IsPartyLeader(Actor memberPawn)
        {
            if (PartyMembers.Count == 0) return true;
            var leader = PartyMembers[0];
            return leader != null && leader.Actor == memberPawn;
        }

        /// <summary>
        /// 队伍开始对话：所有队伍成员同步开始对话。
        /// </summary>
        public override bool BeginDialogue(string dialogueClassId, DialoguePlayParams playParams = null)
        {
            bool result = base.BeginDialogue(dialogueClassId, playParams);
            if (!result) return false;

            // 队伍模式下，让其他成员也进入对话观战
            foreach (var member in PartyMembers)
            {
                if (member == null || member == this) continue;
                if (member.IsInDialogue) continue;
                member.BeginDialogue(dialogueClassId, playParams);
            }
            return true;
        }

        /// <summary>
        /// 选择对话选项。根据策略，非队长不能选择。
        /// </summary>
        public override void SelectDialogueOption(DialogueNode_Player option, Actor selector = null)
        {
            if (PartyDialogueControlPolicy == EPartyDialogueControlPolicy.PartyLeaderControlled)
            {
                var leader = GetPartyLeader();
                if (leader != null && selector != null && leader.Actor != selector)
                {
                    NarrativeLog.LogWarning("Non-leader party member attempted to select dialogue option");
                    return;
                }
            }
            TrySelectDialogueOption(option);

            // 同步选项给其他队伍成员
            foreach (var member in PartyMembers)
            {
                if (member == null || member == this) continue;
                member.TrySelectDialogueOption(option);
            }
        }

        /// <summary>
        /// 队伍退出对话：所有成员一起退出。
        /// 先调用 base 退出自身，再同步给其他成员。
        /// </summary>
        protected override void ExitDialogue(EExitDialogueReason reason)
        {
            base.ExitDialogue(reason);
            foreach (var member in PartyMembers)
            {
                if (member == null || member == this) continue;
                if (member.IsInDialogue)
                {
                    member.TryExitDialogue(reason);
                }
            }
        }

        /// <summary>队伍组件没有单一拥有者，返回队长对应的 Pawn。</summary>
        public override Actor GetOwningPawn()
        {
            var leader = GetPartyLeader();
            return leader?.Actor;
        }

        /// <summary>队伍组件没有单一拥有者 Controller，返回 null（Flax 中无 PlayerController 概念）。</summary>
        public override Actor GetOwningController()
        {
            return null;
        }
    }
}

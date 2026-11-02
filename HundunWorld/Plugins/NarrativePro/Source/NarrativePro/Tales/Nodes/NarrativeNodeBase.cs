using System;
using System.Collections.Generic;
using NarrativePro.AI;
using NarrativePro.Core;
using NarrativePro.Tales.Events;

namespace NarrativePro.Tales.Nodes
{
    /// <summary>
    /// 任务状态机 / 对话树节点的公共基类。对应 UE5 <c>UNarrativeNodeBase</c>
    /// （NarrativeNodeBase.h/cpp）。
    /// </summary>
    public abstract class NarrativeNodeBase
    {
        /// <summary>节点 ID。对应 UE5 <c>ID</c>（用于网络侧按 ID 还原节点）。</summary>
        public string ID { get; set; } = "";

        /// <summary>图节点位置 X。对应 UE5 <c>NodePos.X</c>：竖直走线模式下按它排序回复。</summary>
        public float NodePosX { get; set; } = 0f;

        /// <summary>图节点位置 Y。对应 UE5 <c>NodePos.Y</c>：默认按它排序回复。</summary>
        public float NodePosY { get; set; } = 0f;

        /// <summary>节点上的事件。对应 UE5 <c>Events</c>。</summary>
        public List<NarrativeEvent> Events { get; set; } = new List<NarrativeEvent>();

        /// <summary>节点上的条件。对应 UE5 <c>Conditions</c>。</summary>
        public List<NarrativeCondition> Conditions { get; set; } = new List<NarrativeCondition>();

        /// <summary>对应 UE5 <c>GetID</c>。</summary>
        public string GetID() => ID ?? "";

        /// <summary>对应 UE5 <c>SetID</c>：赋值后保证 ID 在同任务/对话内唯一。</summary>
        public virtual void SetID(string newID)
        {
            ID = newID ?? "";
            EnsureUniqueID();
        }

        /// <summary>保证 ID 唯一。对应 UE5 <c>EnsureUniqueID</c>，由具体节点族重写。</summary>
        public virtual void EnsureUniqueID()
        {
        }

        /// <summary>
        /// 图位置排序键。对应 UE5 里
        /// <c>DialogueSettings->bEnableVerticalWiring ? NodePos.X : NodePos.Y</c>。
        /// </summary>
        public float GetGraphSortKey()
        {
            var settings = NarrativeDialogueSettings.Instance;
            bool vertical = settings != null && settings.bEnableVerticalWiring;
            return vertical ? NodePosX : NodePosY;
        }

        /// <summary>
        /// 执行本节点上属于 <paramref name="runtime"/> 的全部事件。
        /// 对应 UE5 <c>UNarrativeNodeBase::ProcessEvents</c>（NarrativeNodeBase.cpp:35-105）。
        /// </summary>
        public void ProcessEvents(object target, object controller, object narrativeComponent, Core.EEventRuntime runtime)
        {
            var comp = narrativeComponent as TalesComponent;
            if (comp == null)
            {
                NarrativeLog.LogWarning($"节点 {ID} 尝试执行事件，但 Narrative 组件为空。");
                return;
            }

            bool bIsLoading = comp.bIsLoading;

            foreach (var evt in Events)
            {
                if (evt == null) continue;

                // UE5:49 —— 读档期间只有 bRefireOnLoad 的事件才重放。
                bool bShouldFire = (!bIsLoading || evt.bRefireOnLoad)
                                   && (evt.EventRuntime == runtime || evt.EventRuntime == Core.EEventRuntime.Both);

                if (!bShouldFire) continue;

                // C# 端口额外保留：事件自身的条件必须全部满足（UE5 在别处校验，此处等价且不改变单机行为）。
                if (!evt.ShouldExecute(target, controller, comp)) continue;

                var charTargets = evt.GetCharacterTargets();

                // ① 配置了 NPC/角色目标：在这些角色的 Actor 上跑事件，而不是拥有者玩家。
                if (charTargets.Count > 0)
                {
                    var subsystem = NarrativeCharacterSubsystem.Instance;
                    if (subsystem == null) continue;

                    foreach (var targetDef in charTargets)
                    {
                        if (targetDef == null) continue;
                        var character = subsystem.FindCharacter(targetDef);
                        if (character != null)
                        {
                            evt.ExecuteEvent(character, controller, comp);
                        }
                    }

                    continue;
                }

                // ② 队伍策略：UE5 用 `PartyEventPolicy > EPartyEventPolicy::Party` 判定需要遍历队员。
                var party = comp as NarrativePartyComponent;
                if (evt.PartyEventPolicy > Core.EPartyEventPolicy.Party && party != null)
                {
                    foreach (var member in party.GetPartyMembers())
                    {
                        if (member == null) continue;

                        evt.ExecuteEvent(member.GetOwningPawn(), member.GetOwningController(), member);

                        // 队长永远是第一个成员，跑完即可退出。
                        if (evt.PartyEventPolicy == Core.EPartyEventPolicy.PartyLeader)
                        {
                            break;
                        }
                    }
                }
                else
                {
                    // ③ 默认：跑在拥有者（队伍组件本身，或无队伍时的 Solo 玩家）上。
                    evt.ExecuteEvent(comp.GetOwningPawn(), comp.GetOwningController(), comp);
                }
            }
        }

        /// <summary>
        /// 本节点的条件是否全部满足。
        /// 对应 UE5 <c>UNarrativeNodeBase::AreConditionsMet</c>（NarrativeNodeBase.cpp:107-209）；
        /// 条件本身如何判定（队伍策略 / 角色目标 / bNot）全部收敛在纯层
        /// <see cref="NarrativeConditionEvaluation"/>，本方法只负责组装输入与「与」聚合。
        /// </summary>
        public bool AreConditionsMet(object target, object controller, object narrativeComponent)
        {
            var comp = narrativeComponent as TalesComponent;
            if (comp == null)
            {
                NarrativeLog.LogWarning($"节点 {ID} 尝试判定条件，但 Narrative 组件为空。");
                return false;
            }

            return NarrativeConditionEvaluation.EvaluateAll(BuildConditionRequests(target, controller, comp), true);
        }

        /// <summary>
        /// 把本节点的条件集折成纯求值输入列表（null 条目按 UE5:118 跳过）。
        /// </summary>
        internal List<NarrativeConditionEvalRequest> BuildConditionRequests(object target, object controller, TalesComponent comp)
        {
            var requests = new List<NarrativeConditionEvalRequest>();
            if (Conditions == null) return requests;

            for (int i = 0; i < Conditions.Count; i++)
            {
                var cond = Conditions[i];
                if (cond == null) continue;
                requests.Add(cond.BuildEvalRequest(target, controller, comp));
            }

            return requests;
        }
    }
}

using System.Collections.Generic;
using NarrativePro.Core;

namespace NarrativePro.Tales.Nodes
{
    /// <summary>
    /// 任务状态节点。对应 UE5 <c>UQuestState</c>（QuestSM.h:67-89 / QuestSM.cpp:18-44）。
    /// </summary>
    public class QuestState : QuestNode
    {
        /// <summary>本状态出向的分支。对应 UE5 <c>Branches</c>。</summary>
        public List<QuestBranch> Branches { get; set; } = new List<QuestBranch>();

        /// <summary>状态类型（普通/成功/失败）。对应 UE5 <c>StateNodeType</c>。</summary>
        public EStateNodeType StateNodeType { get; set; } = EStateNodeType.Regular;

        /// <summary>
        /// 激活状态。对应 UE5 <c>UQuestState::Activate</c>（QuestSM.cpp:18-30）：
        /// 状态一激活就要激活它的全部出向分支，任务才会真正开始跑，
        /// 这是整条「进度 → 分支完成 → 状态推进」链的起点。
        /// </summary>
        public override void Activate()
        {
            if (Branches != null)
            {
                foreach (var branch in Branches)
                {
                    branch?.Activate();
                }
            }

            base.Activate();
        }

        /// <summary>
        /// 失活状态。对应 UE5 <c>UQuestState::Deactivate</c>（QuestSM.cpp:32-44）。
        /// </summary>
        public override void Deactivate()
        {
            if (Branches != null)
            {
                foreach (var branch in Branches)
                {
                    branch?.Deactivate();
                }
            }

            base.Deactivate();
        }

        /// <summary>
        /// 对应 UE5 <c>UQuestState::GetNodeTitle</c>：
        /// UE5 用 <c>FName::NameToDisplayString</c> 转可读串，Flax 用 <see cref="NarrativeFunctionLibrary.MakeDisplayString"/>。
        /// </summary>
        public override string GetNodeTitle()
        {
            if (string.IsNullOrEmpty(ID)) return "";
            return NarrativeFunctionLibrary.MakeDisplayString(ID);
        }
    }
}

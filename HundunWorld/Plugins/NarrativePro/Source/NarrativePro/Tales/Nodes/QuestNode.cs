using NarrativePro.Core;
using QuestClass = NarrativePro.Tales.Quest.Quest;

namespace NarrativePro.Tales.Nodes
{
    /// <summary>
    /// 任务状态机节点基类（状态与分支的公共父类）。
    /// 对应 UE5 <c>UQuestNode</c>（QuestSM.h:31-63 / QuestSM.cpp:181-271）。
    /// </summary>
    public class QuestNode : NarrativeNodeBase
    {
        /// <summary>
        /// 节点被进入/离开时要调用的函数名。对应 UE5 <c>OnEnteredFuncName</c>。
        /// UE5 通过 <c>OwningQuest->FindFunction(...)+ProcessEvent</c> 派发（QuestSM.cpp:195-198），
        /// Flax 无 UFunction，改由 <see cref="QuestClass.DispatchNodeEntered"/> 回调派发。
        /// </summary>
        public string OnEnteredFuncName { get; set; } = "";

        /// <summary>节点描述（任务日志文本）。对应 UE5 <c>Description</c>。</summary>
        public string Description { get; set; } = "";

        /// <summary>拥有本节点的运行时任务对象。对应 UE5 <c>OwningQuest</c>。</summary>
        public QuestClass OwningQuest { get; set; }

        /// <summary>
        /// 节点激活。对应 UE5 <c>UQuestNode::Activate</c>（QuestSM.cpp:181-202）：
        /// 先派发 OnEnteredFuncName（bActivated=true），再跑 Start 事件。
        /// </summary>
        public virtual void Activate()
        {
            var quest = GetOwningQuest();
            if (quest == null) return;

            if (!string.IsNullOrEmpty(OnEnteredFuncName))
            {
                quest.DispatchNodeEntered(this, true);
            }

            ProcessEvents(quest.GetOwningPawn(), quest.GetOwningController(), quest.GetOwningComp(), EEventRuntime.Start);
        }

        /// <summary>
        /// 节点失活。对应 UE5 <c>UQuestNode::Deactivate</c>（QuestSM.cpp:204-225）：
        /// 仅在任务仍处于 Started 时派发，并跑 End 事件。
        /// </summary>
        public virtual void Deactivate()
        {
            var quest = GetOwningQuest();
            if (quest == null || quest.QuestCompletion != EQuestCompletion.Started) return;

            if (!string.IsNullOrEmpty(OnEnteredFuncName))
            {
                quest.DispatchNodeEntered(this, false);
            }

            ProcessEvents(quest.GetOwningPawn(), quest.GetOwningController(), quest.GetOwningComp(), EEventRuntime.End);
        }

        /// <summary>对应 UE5 <c>UQuestNode::EnsureUniqueID</c>。</summary>
        public override void EnsureUniqueID()
        {
            QuestSM.EnsureUniqueID(this);
        }

        /// <summary>
        /// 对应 UE5 <c>UQuestNode::GetOwningQuest</c>。UE5 在编辑器态回退到 Outer，
        /// Flax 无 Outer 概念，运行时由 <c>Quest.Initialize</c> 统一写入。
        /// </summary>
        public QuestClass GetOwningQuest()
        {
            return OwningQuest;
        }

        /// <summary>对应 UE5 <c>UQuestNode::GetOwningNarrativeComp</c>。</summary>
        public TalesComponent GetOwningNarrativeComp()
        {
            return GetOwningQuest()?.GetOwningComp();
        }

        /// <summary>对应 UE5 <c>UQuestNode::GetNodeTitle</c> 默认实现。</summary>
        public virtual string GetNodeTitle()
        {
            return "Node";
        }
    }
}

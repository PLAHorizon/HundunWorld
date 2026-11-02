using NarrativePro.Core;

namespace NarrativePro.Tales.Events.Builtin.Conditions
{
    /// <summary>
    /// 任务状态条件。对应 UE5 <c>UNarrativeCondition_QuestState</c>：判定某任务是否处于指定的完成态。
    /// </summary>
    public class QuestStateCondition : NarrativeCondition
    {
        /// <summary>任务类标识。对应 UE5 <c>Quest</c>。</summary>
        public string QuestClassId { get; set; } = "";

        /// <summary>要求的完成态。对应 UE5 <c>RequiredState</c>。</summary>
        public EQuestCompletion RequiredState { get; set; } = EQuestCompletion.Succeeded;

        public override bool CheckCondition(object target, object controller, TalesComponent narrativeComponent)
        {
            if (narrativeComponent == null || string.IsNullOrEmpty(QuestClassId))
            {
                NarrativeLog.LogWarning("QuestStateCondition: QuestClassId 或叙事组件为空，条件按失败处理");
                return false;
            }

            var quest = narrativeComponent.GetQuestInstance(QuestClassId);
            if (quest == null)
            {
                // UE5：NotStarted 状态下「查不到任务实例」正是成立条件。
                return RequiredState == EQuestCompletion.NotStarted;
            }

            return quest.GetQuestCompletion() == RequiredState;
        }

        public override string GetGraphDisplayText() => $"Quest {QuestClassId} State == {RequiredState}";
    }
}

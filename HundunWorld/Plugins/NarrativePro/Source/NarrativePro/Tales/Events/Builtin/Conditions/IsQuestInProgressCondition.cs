using NarrativePro.Core;

namespace NarrativePro.Tales.Events.Builtin.Conditions
{
    /// <summary>
    /// 任务进行中条件。对应 UE5 蓝图资产 <c>NC_IsQuestInProgress</c>。
    /// <para>
    /// 二进制证据：CheckCondition 图为 <c>CallFunc_GetClassDisplayName</c> +
    /// <c>CallFunc_IsQuestInProgress</c>，即调用叙事组件上的 <c>IsQuestInProgress(Quest)</c>。
    /// 端口用 <see cref="TalesComponent.IsQuestInProgress"/>。
    /// </para>
    /// </summary>
    public class IsQuestInProgressCondition : NarrativeCondition
    {
        /// <summary>任务类标识。对应 UE5 蓝图的任务参数（TSubclassOf&lt;UQuest&gt;）。</summary>
        public string QuestClassId { get; set; } = "";

        public override bool CheckCondition(object target, object controller, TalesComponent narrativeComponent)
        {
            if (narrativeComponent == null || string.IsNullOrEmpty(QuestClassId))
            {
                NarrativeLog.LogWarning("IsQuestInProgressCondition: QuestClassId 或叙事组件为空，条件按失败处理");
                return false;
            }
            return narrativeComponent.IsQuestInProgress(QuestClassId);
        }

        public override string GetGraphDisplayText() => $"Quest {QuestClassId} In Progress";
    }
}

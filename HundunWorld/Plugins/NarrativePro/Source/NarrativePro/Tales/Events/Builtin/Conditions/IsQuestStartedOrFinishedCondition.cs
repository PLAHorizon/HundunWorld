using NarrativePro.Core;

namespace NarrativePro.Tales.Events.Builtin.Conditions
{
    /// <summary>
    /// 任务已开始或已结束条件。对应 UE5 蓝图资产 <c>NC_IsQuestStartedOrFinished</c>。
    /// <para>
    /// 二进制证据：CheckCondition 图为 <c>CallFunc_GetClassDisplayName</c> +
    /// <c>CallFunc_IsQuestStartedOrFinished</c>，即调用叙事组件上的同名方法。
    /// 端口用 <see cref="TalesComponent.IsQuestStartedOrFinished"/>（只要任务列表里存在该任务实例即成立）。
    /// </para>
    /// </summary>
    public class IsQuestStartedOrFinishedCondition : NarrativeCondition
    {
        /// <summary>任务类标识。对应 UE5 蓝图的任务参数（TSubclassOf&lt;UQuest&gt;）。</summary>
        public string QuestClassId { get; set; } = "";

        public override bool CheckCondition(object target, object controller, TalesComponent narrativeComponent)
        {
            if (narrativeComponent == null || string.IsNullOrEmpty(QuestClassId))
            {
                NarrativeLog.LogWarning("IsQuestStartedOrFinishedCondition: QuestClassId 或叙事组件为空，条件按失败处理");
                return false;
            }
            return narrativeComponent.IsQuestStartedOrFinished(QuestClassId);
        }

        public override string GetGraphDisplayText() => $"Quest {QuestClassId} Started Or Finished";
    }
}

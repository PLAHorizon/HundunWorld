using NarrativePro.Core;

namespace NarrativePro.Tales.Events.Builtin
{
    /// <summary>
    /// 结算任务事件。对应 UE5 <c>UNarrativeEvent_CompleteQuest</c>。
    /// </summary>
    public class CompleteQuestEvent : NarrativeEvent
    {
        /// <summary>任务类标识。</summary>
        public string QuestClassId { get; set; } = "";

        /// <summary>true 为成功，false 为失败。</summary>
        public bool bSucceed { get; set; } = true;

        /// <summary>结算提示文本。对应 UE5 <c>Message</c>。</summary>
        public string Message { get; set; } = "";

        public override void ExecuteEvent(object target, object controller, object narrativeComponent)
        {
            var component = narrativeComponent as TalesComponent;
            if (component == null || string.IsNullOrEmpty(QuestClassId))
            {
                NarrativeLog.LogWarning("CompleteQuestEvent: 缺少组件或 QuestClassId，跳过");
                return;
            }

            var quest = component.GetQuestInstance(QuestClassId);
            if (quest == null)
            {
                NarrativeLog.LogWarning($"CompleteQuestEvent: 未找到任务实例 '{QuestClassId}'");
                return;
            }

            if (bSucceed)
                quest.SucceedQuest(Message ?? "");
            else
                quest.FailQuest(Message ?? "");
        }

        public override string GetGraphDisplayText() => $"{(bSucceed ? "Succeed" : "Fail")} Quest: {QuestClassId}";
    }
}

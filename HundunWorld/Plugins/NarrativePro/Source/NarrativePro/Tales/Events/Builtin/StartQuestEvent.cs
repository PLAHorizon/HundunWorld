using NarrativePro.Core;

namespace NarrativePro.Tales.Events.Builtin
{
    /// <summary>
    /// 开始任务事件。对应 UE5 <c>UNarrativeEvent_StartQuest</c>：真正调用组件的 BeginQuest。
    /// </summary>
    public class StartQuestEvent : NarrativeEvent
    {
        /// <summary>任务类标识。对应 UE5 <c>Quest</c>。</summary>
        public string QuestClassId { get; set; } = "";

        /// <summary>可选的起始状态 id。对应 UE5 <c>StartingFromStateId</c>。</summary>
        public string StartFromId { get; set; } = "";

        public override void ExecuteEvent(object target, object controller, object narrativeComponent)
        {
            if (string.IsNullOrEmpty(QuestClassId))
            {
                NarrativeLog.LogWarning("StartQuestEvent: QuestClassId 为空，跳过");
                return;
            }

            var component = narrativeComponent as TalesComponent;
            if (component == null)
            {
                NarrativeLog.LogWarning("StartQuestEvent: 上下文没有 TalesComponent，无法开始任务");
                return;
            }

            // UE5：已存在同 id 任务实例时 BeginQuest 会直接返回既有实例，不会重复创建。
            var quest = component.BeginQuest(QuestClassId, StartFromId ?? "");
            if (quest == null)
                NarrativeLog.LogWarning($"StartQuestEvent: 任务 '{QuestClassId}' 启动失败");
        }

        public override string GetGraphDisplayText() => $"Start Quest: {QuestClassId}";
        public override string GetHintText() => "开始任务";
    }
}

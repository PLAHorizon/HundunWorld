using NarrativePro.Core;

namespace NarrativePro.Tales.Events.Builtin
{
    public class StartQuestEvent : NarrativeEvent
    {
        public string QuestClassId { get; set; } = "";
        public string StartFromId { get; set; } = "";

        public override void ExecuteEvent(object target, object controller, object narrativeComponent)
        {
            NarrativeLog.Log($"StartQuestEvent: Starting quest '{QuestClassId}' from '{StartFromId}'");
        }

        public override string GetGraphDisplayText() => $"Start Quest: {QuestClassId}";
        public override string GetHintText() => "开始任务";
    }
}

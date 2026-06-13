using NarrativePro.Core;

namespace NarrativePro.Tales.Events.Builtin
{
    public class CompleteQuestEvent : NarrativeEvent
    {
        public string QuestClassId { get; set; } = "";
        public bool bSucceed { get; set; } = true;
        public string Message { get; set; } = "";

        public override void ExecuteEvent(object target, object controller, object narrativeComponent)
        {
            NarrativeLog.Log($"CompleteQuestEvent: {(bSucceed ? "Succeeding" : "Failing")} quest '{QuestClassId}'");
        }

        public override string GetGraphDisplayText() => $"{(bSucceed ? "Succeed" : "Fail")} Quest: {QuestClassId}";
    }
}

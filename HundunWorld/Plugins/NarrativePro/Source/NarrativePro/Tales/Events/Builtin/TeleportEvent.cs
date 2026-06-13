using NarrativePro.Core;

namespace NarrativePro.Tales.Events.Builtin
{
    public class TeleportEvent : NarrativeEvent
    {
        public string TargetLocationName { get; set; } = "";
        public float X { get; set; } = 0f;
        public float Y { get; set; } = 0f;
        public float Z { get; set; } = 0f;

        public override void ExecuteEvent(object target, object controller, object narrativeComponent)
        {
            NarrativeLog.Log($"TeleportEvent: Teleporting to {TargetLocationName} ({X},{Y},{Z})");
        }

        public override string GetGraphDisplayText() => $"Teleport: {TargetLocationName}";
    }
}

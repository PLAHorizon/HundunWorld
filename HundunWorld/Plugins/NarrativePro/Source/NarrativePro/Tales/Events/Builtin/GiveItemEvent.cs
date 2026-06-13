using NarrativePro.Core;

namespace NarrativePro.Tales.Events.Builtin
{
    public class GiveItemEvent : NarrativeEvent
    {
        public string ItemId { get; set; } = "";
        public int Quantity { get; set; } = 1;

        public override void ExecuteEvent(object target, object controller, object narrativeComponent)
        {
            NarrativeLog.Log($"GiveItemEvent: Giving item '{ItemId}' x{Quantity}");
        }

        public override string GetGraphDisplayText() => $"Give Item: {ItemId}";
        public override string GetHintText() => "获得物品";
    }
}

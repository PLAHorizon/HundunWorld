namespace NarrativePro.Tales.Events.Builtin.Conditions
{
    public class HasItemCondition : NarrativeCondition
    {
        public string ItemId { get; set; } = "";
        public int RequiredCount { get; set; } = 1;

        public override bool IsConditionMet(object target, object controller, object narrativeComponent)
        {
            return true;
        }
    }
}

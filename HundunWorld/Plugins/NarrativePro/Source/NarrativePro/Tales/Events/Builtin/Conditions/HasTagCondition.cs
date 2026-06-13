namespace NarrativePro.Tales.Events.Builtin.Conditions
{
    public class HasTagCondition : NarrativeCondition
    {
        public string Tag { get; set; } = "";

        public override bool IsConditionMet(object target, object controller, object narrativeComponent)
        {
            return true;
        }
    }
}

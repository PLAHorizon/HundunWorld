namespace NarrativePro.Tales.Events.Builtin.Conditions
{
    public class HasCompletedTaskCondition : NarrativeCondition
    {
        public string TaskName { get; set; } = "";
        public string Argument { get; set; } = "";
        public int RequiredCount { get; set; } = 1;

        public override bool IsConditionMet(object target, object controller, object narrativeComponent)
        {
            return true;
        }
    }
}

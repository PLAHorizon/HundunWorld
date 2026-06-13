namespace NarrativePro.Tales.Events
{
    public abstract class NarrativeCondition
    {
        public abstract bool IsConditionMet(object target, object controller, object narrativeComponent);
    }
}

using NarrativePro.Core;

namespace NarrativePro.Tales.Events.Builtin.Conditions
{
    public class QuestStateCondition : NarrativeCondition
    {
        public string QuestClassId { get; set; } = "";
        public EQuestCompletion RequiredState { get; set; } = EQuestCompletion.Succeeded;

        public override bool IsConditionMet(object target, object controller, object narrativeComponent)
        {
            return true;
        }
    }
}

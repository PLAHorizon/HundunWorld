using System;
using System.Collections.Generic;
using NarrativePro.Core;

namespace NarrativePro.Tales.Events
{
    public abstract class NarrativeEvent
    {
        public EEventRuntime EventRuntime { get; set; } = EEventRuntime.Start;
        public EEventFilter EventFilter { get; set; } = EEventFilter.Anyone;
        public bool bRefireOnLoad { get; set; } = false;
        public EPartyEventPolicy PartyEventPolicy { get; set; } = EPartyEventPolicy.OnlyTriggerForOwningPlayer;
        public List<NarrativeCondition> Conditions { get; set; } = new List<NarrativeCondition>();

        public abstract void ExecuteEvent(object target, object controller, object narrativeComponent);
        public virtual void OnActivate(object target, object controller, object narrativeComponent) { }
        public virtual void OnDeactivate(object target, object controller, object narrativeComponent) { }

        public virtual string GetGraphDisplayText()
        {
            return GetType().Name;
        }

        public virtual string GetHintText()
        {
            return "";
        }

        public bool ShouldExecute(object target, object controller, object narrativeComponent)
        {
            if (Conditions == null || Conditions.Count == 0) return true;
            return Conditions.TrueForAll(c => c.IsConditionMet(target, controller, narrativeComponent));
        }
    }
}

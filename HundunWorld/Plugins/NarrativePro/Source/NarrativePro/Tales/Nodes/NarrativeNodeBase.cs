using System;
using System.Collections.Generic;
using NarrativePro.Tales.Events;

namespace NarrativePro.Tales.Nodes
{
    public abstract class NarrativeNodeBase
    {
        public string ID { get; set; } = "";
        public float NodePosX { get; set; } = 0f;
        public float NodePosY { get; set; } = 0f;
        public List<NarrativeEvent> Events { get; set; } = new List<NarrativeEvent>();
        public List<NarrativeCondition> Conditions { get; set; } = new List<NarrativeCondition>();

        public void ProcessEvents(object target, object controller, object narrativeComponent, Core.EEventRuntime runtime)
        {
            foreach (var evt in Events)
            {
                if (evt.EventRuntime == runtime || evt.EventRuntime == Core.EEventRuntime.Both)
                {
                    if (evt.Conditions == null || evt.Conditions.Count == 0 || evt.Conditions.TrueForAll(c => c.IsConditionMet(target, controller, narrativeComponent)))
                    {
                        evt.ExecuteEvent(target, controller, narrativeComponent);
                    }
                }
            }
        }

        public bool AreConditionsMet(object target, object controller, object narrativeComponent)
        {
            if (Conditions == null || Conditions.Count == 0) return true;
            return Conditions.TrueForAll(c => c.IsConditionMet(target, controller, narrativeComponent));
        }
    }
}

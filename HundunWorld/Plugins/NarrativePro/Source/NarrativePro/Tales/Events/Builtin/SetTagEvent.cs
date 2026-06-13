using System.Collections.Generic;
using NarrativePro.Core;

namespace NarrativePro.Tales.Events.Builtin
{
    public class SetTagEvent : NarrativeEvent
    {
        public string Tag { get; set; } = "";
        public bool bAdd { get; set; } = true;

        public override void ExecuteEvent(object target, object controller, object narrativeComponent)
        {
            NarrativeLog.Log($"SetTagEvent: {(bAdd ? "Adding" : "Removing")} tag '{Tag}'");
        }

        public override string GetGraphDisplayText() => $"{(bAdd ? "Add" : "Remove")} Tag: {Tag}";
    }
}

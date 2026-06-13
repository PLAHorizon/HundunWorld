using System;
using System.Collections.Generic;

namespace NarrativePro.Tales.Data
{
    [Serializable]
    public class DialoguePlayParams
    {
        public string StartFromID { get; set; } = "";
        public int Priority { get; set; } = -1;
        public bool bOverrideFreeMovement { get; set; } = false;
        public bool bFreeMovement { get; set; } = true;
        public bool bOverrideStopMovement { get; set; } = false;
        public bool bStopMovement { get; set; } = false;
        public bool bOverrideUnskippable { get; set; } = false;
        public bool bUnskippable { get; set; } = false;
    }
}

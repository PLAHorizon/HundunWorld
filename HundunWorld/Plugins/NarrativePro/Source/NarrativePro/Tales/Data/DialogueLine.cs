using System;
using System.Collections.Generic;
using NarrativePro.Core;
using NarrativePro.Tales.Events;

namespace NarrativePro.Tales.Data
{
    [Serializable]
    public class DialogueLine
    {
        public string Text { get; set; } = "";
        public ELineDuration Duration { get; set; } = ELineDuration.Default;
        public float DurationSecondsOverride { get; set; } = 0f;
        public string SoundPath { get; set; } = "";
        public string BodyAnimationName { get; set; } = "";
        public string FacialAnimationName { get; set; } = "";
        public string ShotName { get; set; } = "";
        public List<NarrativeCondition> Conditions { get; set; } = new List<NarrativeCondition>();
    }
}

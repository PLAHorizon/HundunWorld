using NarrativePro.Core;

namespace NarrativePro.Tales.Events.Builtin
{
    public class PlaySoundEvent : NarrativeEvent
    {
        public string SoundPath { get; set; } = "";
        public float Volume { get; set; } = 1.0f;

        public override void ExecuteEvent(object target, object controller, object narrativeComponent)
        {
            NarrativeLog.Log($"PlaySoundEvent: Playing sound '{SoundPath}'");
        }

        public override string GetGraphDisplayText() => $"Play Sound: {SoundPath}";
    }
}

namespace NarrativePro.Core
{
    public class NarrativeSettings
    {
        public float LettersPerSecond { get; set; } = 30.0f;
        public string DefaultQuestDirectory { get; set; } = "Content/NarrativePro/Quests";
        public string DefaultDialogueDirectory { get; set; } = "Content/NarrativePro/Dialogues";
        public string DefaultDataTaskDirectory { get; set; } = "Content/NarrativePro/DataTasks";
        public string SaveSlotName { get; set; } = "NarrativeSaveData";
    }
}

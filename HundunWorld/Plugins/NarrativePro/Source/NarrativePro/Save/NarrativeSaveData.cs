using System;
using System.Collections.Generic;
using NarrativePro.Core;

namespace NarrativePro.Save
{
    [Serializable]
    public class NarrativeSaveData
    {
        public List<SavedQuest> SavedQuests { get; set; } = new List<SavedQuest>();
        public Dictionary<string, int> MasterTaskList { get; set; } = new Dictionary<string, int>();
        public string CurrentDialogueId { get; set; } = "";
        public string CurrentDialogueNodeId { get; set; } = "";
    }

    [Serializable]
    public class SavedQuest
    {
        public string QuestClassId { get; set; } = "";
        public EQuestCompletion QuestCompletion { get; set; } = EQuestCompletion.NotStarted;
        public string CurrentStateId { get; set; } = "";
        public List<string> ReachedStateIds { get; set; } = new List<string>();
        public List<SavedQuestBranch> Branches { get; set; } = new List<SavedQuestBranch>();
        public bool bTracked { get; set; } = false;
    }

    [Serializable]
    public class SavedQuestBranch
    {
        public string BranchId { get; set; } = "";
        public List<int> TasksProgress { get; set; } = new List<int>();
    }
}

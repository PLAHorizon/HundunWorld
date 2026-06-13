using System.Collections.Generic;
using NarrativePro.Tales.Tasks;

namespace NarrativePro.Tales.Nodes
{
    public class QuestBranch : QuestNode
    {
        public List<NarrativeTask> QuestTasks { get; set; } = new List<NarrativeTask>();
        public QuestState DestinationState { get; set; }
        public bool bHidden { get; set; } = false;

        public virtual void Activate()
        {
            foreach (var task in QuestTasks)
            {
                task.BeginTaskInit();
            }
        }

        public virtual void Deactivate()
        {
            foreach (var task in QuestTasks)
            {
                task.EndTask();
            }
        }

        public bool AreTasksComplete()
        {
            if (QuestTasks == null || QuestTasks.Count == 0) return true;
            return QuestTasks.TrueForAll(t => t.IsComplete());
        }
    }
}

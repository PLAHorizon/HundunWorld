using QuestClass = NarrativePro.Tales.Quest.Quest;

namespace NarrativePro.Tales.Nodes
{
    public class QuestNode : NarrativeNodeBase
    {
        public string OnEnteredFuncName { get; set; } = "";
        public string Description { get; set; } = "";
        public QuestClass OwningQuest { get; set; }
    }
}

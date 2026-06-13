namespace NarrativePro.Tales.Quest
{
    public abstract class QuestRequirement
    {
        public Quest OwningQuest { get; protected set; }

        public virtual void OnAdded(Quest quest) { OwningQuest = quest; }
        public virtual void OnRemoved(Quest quest) { }
    }
}

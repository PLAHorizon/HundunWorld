namespace NarrativePro.Tales.Tasks
{
    public class CollectItemTask : NarrativeTask
    {
        public string ItemId { get; set; } = "";
        public string ItemName { get; set; } = "";

        public override string GetTaskDescription()
        {
            if (!string.IsNullOrEmpty(DescriptionOverride)) return DescriptionOverride;
            string name = !string.IsNullOrEmpty(ItemName) ? ItemName : ItemId;
            return $"收集 {name}";
        }
    }
}

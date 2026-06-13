namespace NarrativePro.Tales.Tasks
{
    public class GenericTask : NarrativeTask
    {
        public string TaskTypeId { get; set; } = "";
        public string TargetId { get; set; } = "";

        public override string GetTaskDescription()
        {
            if (!string.IsNullOrEmpty(DescriptionOverride)) return DescriptionOverride;
            if (!string.IsNullOrEmpty(TargetId)) return $"{TaskTypeId}: {TargetId}";
            return TaskTypeId;
        }
    }
}

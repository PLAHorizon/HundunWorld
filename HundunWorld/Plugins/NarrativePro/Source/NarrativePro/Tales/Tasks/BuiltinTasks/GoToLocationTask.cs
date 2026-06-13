using FlaxEngine;

namespace NarrativePro.Tales.Tasks
{
    public class GoToLocationTask : NarrativeTask
    {
        public string LocationName { get; set; } = "";
        public Vector3 TargetLocation { get; set; } = Vector3.Zero;
        public float AcceptanceRadius { get; set; } = 200f;

        public override string GetTaskDescription()
        {
            if (!string.IsNullOrEmpty(DescriptionOverride)) return DescriptionOverride;
            return !string.IsNullOrEmpty(LocationName) ? $"前往 {LocationName}" : "前往目标地点";
        }
    }
}

using NarrativePro.Core;

namespace NarrativePro.Tales.Events.Builtin.Conditions
{
    /// <summary>
    /// 已完成任务条目条件。对应 UE5 <c>UNarrativeCondition_HasCompletedTask</c>
    /// （按任务类 + 进度标签统计历史完成次数）。
    /// </summary>
    public class HasCompletedTaskCondition : NarrativeCondition
    {
        /// <summary>任务类标识。对应 UE5 <c>Task</c>（TSubclassOf&lt;UNarrativeTask&gt;）。</summary>
        public string TaskName { get; set; } = "";

        /// <summary>可选的进度名。对应 UE5 <c>ProgressName</c>（FName）。</summary>
        public string Argument { get; set; } = "";

        /// <summary>需要的完成次数。对应 UE5 <c>RequiredCount</c>。</summary>
        public int RequiredCount { get; set; } = 1;

        public override bool CheckCondition(object target, object controller, TalesComponent narrativeComponent)
        {
            if (string.IsNullOrEmpty(TaskName))
            {
                NarrativeLog.LogWarning("HasCompletedTaskCondition: TaskName 为空，条件按失败处理");
                return false;
            }

            // 条件既可以跑在队伍成员上（narrativeComponent 已指向该成员），也可以跑在 pawn 自己的组件上。
            var component = narrativeComponent;
            if (component == null)
                return false;

            return component.HasCompletedTask(TaskName, Argument ?? "", RequiredCount);
        }

        public override string GetGraphDisplayText() => $"Has Completed Task: {TaskName} x{RequiredCount}";
    }
}

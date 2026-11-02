using System.Collections.Generic;
using System.Text;
using NarrativePro.Tales.Tasks;

namespace NarrativePro.Tales.Nodes
{
    /// <summary>
    /// 任务分支节点。对应 UE5 <c>UQuestBranch</c>（QuestSM.h:91-139 / QuestSM.cpp:46-166）。
    /// </summary>
    public class QuestBranch : QuestNode
    {
        /// <summary>完成此分支所需的任务。对应 UE5 <c>QuestTasks</c>。</summary>
        public List<NarrativeTask> QuestTasks { get; set; } = new List<NarrativeTask>();

        /// <summary>分支被取走后要进入的状态；为空则忽略本分支。对应 UE5 <c>DestinationState</c>。</summary>
        public QuestState DestinationState { get; set; }

        /// <summary>是否在 UI 上隐藏本分支。对应 UE5 <c>bHidden</c>。</summary>
        public bool bHidden { get; set; } = false;

        /// <summary>
        /// 激活分支。对应 UE5 <c>UQuestBranch::Activate</c>（QuestSM.cpp:51-62）：
        /// 注意 UE5 调的是 <c>BeginTask()</c>（完整启动：初始化 + 逻辑开始 + 定时器 + 首帧 tick + 导航标记），
        /// 而不是只写 bIsActive 的 <c>BeginTaskInit()</c>。
        /// </summary>
        public override void Activate()
        {
            if (QuestTasks != null)
            {
                foreach (var task in QuestTasks)
                {
                    if (task == null) continue;
                    task.OwningBranch = this;
                    task.BeginTask();
                }
            }

            base.Activate();
        }

        /// <summary>失活分支。对应 UE5 <c>UQuestBranch::Deactivate</c>（QuestSM.cpp:64-75）。</summary>
        public override void Deactivate()
        {
            if (QuestTasks != null)
            {
                foreach (var task in QuestTasks)
                {
                    task?.EndTask();
                }
            }

            base.Deactivate();
        }

        /// <summary>
        /// 分支内某个任务完成时回调。对应 UE5 <c>UQuestBranch::OnQuestTaskComplete</c>
        /// （QuestSM.cpp:77-88）—— 这是驱动状态机前进的枢纽：
        /// 全部分支任务完成 → <c>OwningQuest.TakeBranch(this)</c>（激活分支、进入目标状态），
        /// 然后广播任务完成事件。
        /// </summary>
        public void OnQuestTaskComplete(NarrativeTask updatedTask)
        {
            var quest = GetOwningQuest();
            if (quest == null) return;

            if (AreTasksComplete())
            {
                quest.TakeBranch(this);
            }

            quest.OnQuestTaskCompleted(updatedTask, this);
        }

        /// <summary>
        /// 分支内所有非可选任务是否都已完成。对应 UE5 <c>UQuestBranch::AreTasksComplete</c>（QuestSM.cpp:152-166）：
        /// UE5 会跳过空指针（<c>if (MyTask && !MyTask->IsComplete())</c>），这里保持一致。
        /// </summary>
        public virtual bool AreTasksComplete()
        {
            if (QuestTasks == null) return true;

            foreach (var myTask in QuestTasks)
            {
                if (myTask != null && !myTask.IsComplete())
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 分支标题：列出所含任务及其所需数量。对应 UE5 <c>UQuestBranch::GetNodeTitle</c>（QuestSM.cpp:90-126）。
        /// </summary>
        public override string GetNodeTitle()
        {
            var sb = new StringBuilder();
            sb.Append("Tasks: ");

            if (QuestTasks == null || QuestTasks.Count == 0) return sb.ToString();

            int idx = 0;
            foreach (var task in QuestTasks)
            {
                if (task == null) continue;
                if (idx > 0) sb.AppendLine();

                string taskName = task.GetTaskNodeDescription();

                if (task.RequiredQuantity <= 1)
                {
                    sb.Append(taskName);
                }
                else
                {
                    sb.Append($"{taskName} (0/{task.RequiredQuantity})");
                }

                idx++;
            }

            return sb.ToString();
        }
    }
}

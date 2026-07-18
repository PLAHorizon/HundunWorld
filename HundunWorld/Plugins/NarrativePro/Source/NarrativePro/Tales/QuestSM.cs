using System;
using System.Collections.Generic;
using System.Text;
using NarrativePro.Tales.Nodes;
using QuestClass = NarrativePro.Tales.Quest.Quest;

namespace NarrativePro.Tales
{
    /// <summary>
    /// 状态到达事件委托。对应 UE5 FOnStateReachedEvent。
    /// </summary>
    public delegate void OnStateReachedEvent();

    /// <summary>
    /// 任务状态机辅助工具。对应 UE5 QuestSM.h 中除节点类以外的运行时辅助逻辑。
    /// 任务节点类（QuestNode/QuestState/QuestBranch）已移植到 Nodes/ 子目录，
    /// EStateNodeType 枚举已移植到 Core.Enums，此处仅保留 UE5 中尚未移植的部分。
    /// </summary>
    public static class QuestSM
    {
        /// <summary>
        /// 确保节点 ID 唯一。对应 UE5 UQuestNode::EnsureUniqueID。
        /// 若 ID 与同任务中其他节点冲突，则追加数字后缀直到唯一。
        /// </summary>
        public static void EnsureUniqueID(QuestNode node)
        {
            if (node?.OwningQuest == null) return;

            var allNodes = node.OwningQuest.GetNodes();
            var existingIDs = new HashSet<string>(StringComparer.Ordinal);
            foreach (var n in allNodes)
            {
                if (n != null && n != node && !string.IsNullOrEmpty(n.ID))
                {
                    existingIDs.Add(n.ID);
                }
            }

            if (!existingIDs.Contains(node.ID)) return;

            int suffix = 1;
            string baseID = node.ID ?? "";
            string newID = baseID + suffix;
            while (existingIDs.Contains(newID))
            {
                suffix++;
                newID = baseID + suffix;
            }
            node.ID = newID;
        }

        /// <summary>
        /// 获取任务节点的默认标题。对应 UE5 UQuestNode::GetNodeTitle 默认实现。
        /// </summary>
        public static string GetNodeTitle(QuestNode node)
        {
            return node != null ? "Node" : "";
        }

        /// <summary>
        /// 获取任务状态的标题。对应 UE5 UQuestState::GetNodeTitle。
        /// UE5 中使用 FName::NameToDisplayString 转换为可读字符串，Flax 中直接返回 ID。
        /// </summary>
        public static string GetStateTitle(QuestState state)
        {
            if (state == null || string.IsNullOrEmpty(state.ID)) return "";
            return state.ID;
        }

        /// <summary>
        /// 获取任务分支的标题。对应 UE5 UQuestBranch::GetNodeTitle。
        /// 列出分支包含的所有任务及其所需数量。
        /// </summary>
        public static string GetBranchTitle(QuestBranch branch)
        {
            var sb = new StringBuilder();
            sb.Append("Tasks: ");
            if (branch?.QuestTasks == null || branch.QuestTasks.Count == 0) return sb.ToString();

            sb.AppendLine();
            int idx = 0;
            foreach (var task in branch.QuestTasks)
            {
                if (task == null) continue;
                if (idx > 0) sb.AppendLine();
                // UE5 中使用 GetTaskNodeDescription，Flax 中使用已有的 GetTaskDescription
                string taskName = task.GetTaskDescription();
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

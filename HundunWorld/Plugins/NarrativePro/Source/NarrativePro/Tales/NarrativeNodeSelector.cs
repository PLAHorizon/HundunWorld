using System;

namespace NarrativePro.Tales
{
    /// <summary>
    /// 节点 ID 选择器基类。对应 UE5 FNodeIDSelector。
    /// </summary>
    [Serializable]
    public class NodeIDSelector
    {
        /// <summary>实际的节点 ID。</summary>
        public string NodeID = "";
    }

    /// <summary>
    /// 对话节点选择器。对应 UE5 FDialogueNodeSelector。
    /// </summary>
    [Serializable]
    public class DialogueNodeSelector : NodeIDSelector
    {
        /// <summary>对话资产路径（替代 UE5 TSoftClassPtr&lt;UDialogue&gt;）。</summary>
        public string AssetPath = "";
    }

    /// <summary>
    /// 任务节点选择器基类。对应 UE5 FQuestNodeSelector。
    /// </summary>
    [Serializable]
    public class QuestNodeSelector : NodeIDSelector
    {
        /// <summary>任务资产路径（替代 UE5 TSoftClassPtr&lt;UQuest&gt;）。</summary>
        public string AssetPath = "";
    }

    /// <summary>
    /// 任务状态选择器。对应 UE5 FQuestStateSelector。
    /// </summary>
    [Serializable]
    public class QuestStateSelector : QuestNodeSelector
    {
    }

    /// <summary>
    /// 任务分支选择器。对应 UE5 FQuestBranchSelector。
    /// </summary>
    [Serializable]
    public class QuestBranchSelector : QuestNodeSelector
    {
    }
}

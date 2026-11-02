using NarrativePro.Core;

namespace NarrativePro.Tales.Events.Builtin.Conditions
{
    /// <summary>
    /// 对话节点是否播放过条件。对应 UE5 蓝图资产 <c>NC_HasDialogueNodePlayed</c>。
    /// <para>
    /// 二进制证据：CheckCondition 图为 <c>CallFunc_BreakDialogueNodeSelector_NodeID</c> →
    /// <c>CallFunc_Conv_NameToString</c> → <c>CallFunc_HasCompletedTask</c> →
    /// <c>CallFunc_Conv_IntToInt64</c> + <c>CallFunc_Greater_IntInt</c>（次数 &gt; 0）。
    /// 即：把对话节点当一条以 NodeID 为键的 <c>UNarrativeTask</c> 查历史完成记录。
    /// 端口用 <see cref="DialogueNodeSelector"/> + <see cref="TalesComponent.HasCompletedTask"/>。
    /// </para>
    /// </summary>
    public class HasDialogueNodePlayedCondition : NarrativeCondition
    {
        /// <summary>要检查的对话节点。对应 UE5 蓝图变量 <c>NodeToCheck</c>（FDialogueNodeSelector）。</summary>
        public DialogueNodeSelector NodeToCheck { get; set; }

        public override bool CheckCondition(object target, object controller, TalesComponent narrativeComponent)
        {
            if (narrativeComponent == null)
                return false;

            NarrativeFunctionLibrary.BreakDialogueNodeSelector(NodeToCheck, out string nodeId);
            if (string.IsNullOrEmpty(nodeId))
            {
                NarrativeLog.LogWarning("HasDialogueNodePlayedCondition: NodeToCheck 未填 NodeID，条件按失败处理");
                return false;
            }

            return narrativeComponent.HasCompletedTask(nodeId, "", 1);
        }

        public override string GetGraphDisplayText() => $"Dialogue Node Played: {NodeToCheck?.NodeID}";
    }
}

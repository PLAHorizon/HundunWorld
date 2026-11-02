using System.Collections.Generic;
using NarrativePro.Core;

namespace NarrativePro.Tales.Nodes
{
    /// <summary>
    /// NPC 对话节点。对应 UE5 <c>UDialogueNode_NPC</c>（DialogueSM.h:162-190 / DialogueSM.cpp:377-440）。
    /// </summary>
    public class DialogueNode_NPC : DialogueNode
    {
        /// <summary>本节点的说话者 ID。对应 UE5 <c>SpeakerID</c>（FSpeakerSelector）。</summary>
        public string SpeakerID { get; set; } = "";

        /// <summary>玩家选择回复时播放的镜头名（JSON 兼容字段）。</summary>
        public string SelectingReplyShotName { get; set; } = "";

        /// <summary>
        /// 玩家选择回复时播放的镜头。对应 UE5 <c>SelectingReplyShot</c>（Instanced UNarrativeDialogueSequence）。
        /// </summary>
        public NarrativeDialogueSequence SelectingReplyShot;

        /// <summary>对应 UE5 <c>GetSpeakerID</c>。</summary>
        public string GetSpeakerID() => SpeakerID ?? "";

        /// <summary>
        /// 生成 NPC 连续回复链。对应 UE5 <c>UDialogueNode_NPC::GetReplyChain</c>（DialogueSM.cpp:377-440）。
        /// <para>
        /// UE5 的做法：把自己先放进链尾，然后反复「把下一跳的 NPCReplies 按图位置排序 → 取第一个
        /// 不是起点且条件通过的回复」直到没有可用回复。旧 C# 端口直接取 <c>NPCReplies[0]</c>，
        /// 既不看条件也防不住环，这里补齐。
        /// </para>
        /// </summary>
        /// <param name="owningController">拥有者控制器。</param>
        /// <param name="owningPawn">拥有者 Pawn。</param>
        /// <param name="narrativeComponent">叙事组件（跑条件用）。</param>
        public List<DialogueNode_NPC> GetReplyChain(object owningController, object owningPawn, TalesComponent narrativeComponent)
        {
            // 走链、条件跳过与环截断的规则本体在纯层 NarrativeDialogueSelection.BuildReplyChain。
            var result = NarrativeDialogueSelection.BuildReplyChain(
                this,
                node => node.NPCReplies != null
                    ? SortByGraphPosition(new List<DialogueNode_NPC>(node.NPCReplies))
                    : new List<DialogueNode_NPC>(),
                narrativeComponent != null
                    ? (System.Func<DialogueNode_NPC, bool>)(reply => reply.AreConditionsMet(owningPawn, owningController, narrativeComponent))
                    : null);

            if (result.bCycleDetected)
            {
                NarrativeLog.LogWarning(
                    $"对话回复链出现环（节点 {result.CycleNode.GetID()} 已在链中），在 {GetID()} 处截断。");
            }

            return result.Chain;
        }

        /// <summary>
        /// 无上下文的旧入口（保持 <c>GetReplyChain()</c> 无参重载可用：编辑器/测试路径不做条件过滤）。
        /// </summary>
        public List<DialogueNode_NPC> GetReplyChain()
        {
            return GetReplyChain(null, null, null);
        }
    }

    /// <summary>引用语义的比较器，供 HashSet 使用（DialogueNode 未重写 Equals）。</summary>
    internal sealed class ReferenceEqualityComparer : IEqualityComparer<DialogueNode_NPC>
    {
        public static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();

        public bool Equals(DialogueNode_NPC a, DialogueNode_NPC b) => ReferenceEquals(a, b);

        public int GetHashCode(DialogueNode_NPC node) =>
            node == null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(node);
    }
}

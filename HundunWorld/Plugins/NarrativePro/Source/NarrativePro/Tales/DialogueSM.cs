using System;
using System.Collections.Generic;
using NarrativePro.Tales.Data;
using NarrativePro.Tales.Nodes;

namespace NarrativePro.Tales
{
    /// <summary>
    /// 说话者选择器。对应 UE5 FSpeakerSelector。
    /// 在 UE5 中带 Details 定制，允许从下拉框选择 SpeakerID 而非手动输入 FName。
    /// </summary>
    [Serializable]
    public struct SpeakerSelector
    {
        /// <summary>说话者 ID。UE5 中为 FName，Flax 中用 string。</summary>
        public string SpeakerID;

        public SpeakerSelector(string speakerID)
        {
            SpeakerID = speakerID ?? "";
        }
    }

    /// <summary>
    /// 对话节点播放结束委托。对应 UE5 FOnDialogueNodeFinishedPlaying。
    /// </summary>
    public delegate void OnDialogueNodeFinishedPlaying();

    /// <summary>
    /// 对话状态机辅助工具。对应 UE5 DialogueSM.h 中除节点类以外的运行时辅助逻辑。
    /// 对话节点类（DialogueNode/DialogueNode_NPC/DialogueNode_Player）已移植到 Nodes/ 子目录，
    /// 对话行（DialogueLine）已移植到 Data/ 子目录，
    /// ELineDuration 枚举已移植到 Core.Enums，此处仅保留 UE5 中尚未移植的部分。
    /// </summary>
    public static class DialogueSM
    {
        /// <summary>
        /// 获取第一个满足条件的 NPC 回复。对应 UE5 UDialogueNode::GetFirstValidNPCReply。
        /// UE5 中会按 NarrativeDialogueSettings.bEnableVerticalWiring 决定按 X 或 Y 排序，
        /// Flax 中暂不排序，按列表顺序返回第一个满足条件的回复。
        /// </summary>
        public static DialogueNode_NPC GetFirstValidNPCReply(DialogueNode node, object owningController, object owningPawn, object narrativeComponent)
        {
            if (node == null || node.NPCReplies == null) return null;
            foreach (var reply in node.NPCReplies)
            {
                if (reply != null && reply.AreConditionsMet(owningPawn, owningController, narrativeComponent))
                {
                    return reply;
                }
            }
            return null;
        }

        /// <summary>
        /// 获取所有满足条件的玩家回复。对应 UE5 UDialogueNode::GetPlayerReplies。
        /// UE5 中会按 NarrativeDialogueSettings.bEnableVerticalWiring 决定按 X 或 Y 排序，
        /// Flax 中暂不排序，按列表顺序收集满足条件的回复。
        /// </summary>
        public static List<DialogueNode_Player> GetPlayerReplies(DialogueNode node, object owningController, object owningPawn, object narrativeComponent)
        {
            var valid = new List<DialogueNode_Player>();
            if (node == null || node.PlayerReplies == null) return valid;
            foreach (var reply in node.PlayerReplies)
            {
                if (reply != null && reply.AreConditionsMet(owningPawn, owningController, narrativeComponent))
                {
                    valid.Add(reply);
                }
            }
            return valid;
        }

        /// <summary>
        /// 节点是否缺少提示（有文本但无语音，或备选行为空且无语音）。对应 UE5 UDialogueNode::IsMissingCues。
        /// </summary>
        public static bool IsMissingCues(DialogueNode node)
        {
            if (node?.Line == null) return false;
            if (!string.IsNullOrEmpty(node.Line.Text) && string.IsNullOrEmpty(node.Line.SoundPath))
            {
                return true;
            }
            if (node.AlternativeLines == null || node.AlternativeLines.Count == 0) return false;
            foreach (var alt in node.AlternativeLines)
            {
                if (alt != null && string.IsNullOrWhiteSpace(alt.Text) && string.IsNullOrEmpty(alt.SoundPath))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 获取对话文本。对应 UE5 UDialogueNode::GetDialogueText。
        /// UE5 中会尝试从 SoundWave 字幕提取文本，Flax 中无此机制，直接返回 Line.Text。
        /// </summary>
        public static string GetDialogueText(DialogueNode node)
        {
            if (node?.Line == null) return "";
            return node.Line.Text ?? "";
        }
    }
}

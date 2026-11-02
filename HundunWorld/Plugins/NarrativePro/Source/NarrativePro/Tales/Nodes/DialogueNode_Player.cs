using System.Collections.Generic;
using NarrativePro.Character;
using DialogueClass = NarrativePro.Tales.Dialogue.Dialogue;

namespace NarrativePro.Tales.Nodes
{
    /// <summary>
    /// 玩家回复（对话选项）节点。对应 UE5 <c>UDialogueNode_Player</c>（DialogueSM.h:192-225 / DialogueSM.cpp:443-486）。
    /// </summary>
    public class DialogueNode_Player : DialogueNode
    {
        /// <summary>选项按钮上显示的文本。对应 UE5 <c>OptionText</c>。</summary>
        public string OptionText { get; set; } = "";

        /// <summary>手动指定的提示文本。对应 UE5 <c>HintText</c>。</summary>
        public string HintText { get; set; } = "";

        /// <summary>是否自动选中。对应 UE5 <c>bAutoSelect</c>。</summary>
        public bool bAutoSelect { get; set; } = false;

        /// <summary>仅当它是唯一回复时自动选中。对应 UE5 <c>bAutoSelectIfOnlyReply</c>。</summary>
        public bool bAutoSelectIfOnlyReply { get; set; } = true;

        /// <summary>
        /// 该回复附带的一段电影演出（可选项文本 + 目的地序列 + 跳转设置）。
        /// 对应 UE5 <c>FNarrativeCineDialogueLine</c>（NarrativeLevelSequenceActor.h:117）：
        /// UE5 里它是「电影播放中可选择的对话行」，由蓝图/演出层消费；
        /// 端口由 <c>Dialogue.BeginCinematicDialogueOption</c> 消费——玩家说出本节点台词时，
        /// 经 <c>AsyncAction_PlayNarrativeSequence</c> 播放目的地序列，台词结束时经
        /// <c>AsyncAction_BlendOutNarrativeSequence</c> 混合输出收尾。
        /// </summary>
        public NarrativePro.Cinematics.NarrativeCineDialogueLine CinematicOption { get; set; }

        /// <summary>
        /// 用该玩家定义替代默认玩家形象出演本段对话。对应 UE5 <c>PlayerOverride</c>。
        /// </summary>
        public PlayerDefinition PlayerOverride;

        /// <summary>对应 UE5 <c>IsAutoSelect</c>：路由节点天然自动选中。</summary>
        public bool IsAutoSelect()
        {
            return bAutoSelect || IsRoutingNode();
        }

        /// <summary>对应 UE5 <c>IsAutoSelectIfOnlyReply</c>。</summary>
        public bool IsAutoSelectIfOnlyReply()
        {
            return bAutoSelectIfOnlyReply || IsRoutingNode();
        }

        /// <summary>
        /// 取选项文本。对应 UE5 <c>GetOptionText(UDialogue*)</c>（DialogueSM.cpp:443-454）：
        /// OptionText 为空时回退到行文本，并且同样要过一遍字符串变量替换。
        /// </summary>
        public string GetOptionText(DialogueClass dialogue = null)
        {
            string textToUse = string.IsNullOrWhiteSpace(OptionText) ? (Line?.Text ?? "") : OptionText;

            if (dialogue != null)
            {
                // UE5 用 dummy line 走 ReplaceStringVariables，这里等价：对话尚未取到 CurrentLine
                textToUse = dialogue.ReplaceStringVariables(this, Line, textToUse);
            }

            return textToUse;
        }

        /// <summary>
        /// 取提示文本。对应 UE5 <c>GetHintText(UDialogue*)</c>（DialogueSM.cpp:457-486）：
        /// 手动 HintText 优先，否则把节点各事件自身的 HintText 用「, 」拼接起来。
        /// </summary>
        public string GetHintText(DialogueClass dialogue = null)
        {
            if (!string.IsNullOrWhiteSpace(HintText))
            {
                return HintText;
            }

            var hints = new List<string>();

            if (Events != null)
            {
                foreach (var evt in Events)
                {
                    if (evt == null) continue;
                    string evtHint = evt.GetHintText();
                    if (!string.IsNullOrWhiteSpace(evtHint))
                    {
                        hints.Add(evtHint);
                    }
                }
            }

            if (hints.Count == 0) return HintText ?? "";

            return string.Join(", ", hints.ToArray());
        }

        /// <summary>无对话上下文时的旧调用形态。</summary>
        public string GetHintText()
        {
            return GetHintText(OwningDialogue);
        }
    }
}

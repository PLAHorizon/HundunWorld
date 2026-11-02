using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.Tales.Data;
using NarrativePro.Tales.Events;
using DialogueClass = NarrativePro.Tales.Dialogue.Dialogue;

namespace NarrativePro.Tales.Nodes
{
    /// <summary>
    /// 对话状态机节点基类。对应 UE5 <c>UDialogueNode</c>（DialogueSM.h:121-160 / DialogueSM.cpp:22-375）。
    /// </summary>
    public class DialogueNode : NarrativeNodeBase
    {
        /// <summary>主对话行。对应 UE5 <c>Line</c>。</summary>
        public DialogueLine Line { get; set; } = new DialogueLine();

        /// <summary>备选行（随机挑选，受条件过滤）。对应 UE5 <c>AlternativeLines</c>。</summary>
        public List<DialogueLine> AlternativeLines { get; set; } = new List<DialogueLine>();

        /// <summary>
        /// 本行播放时要调用的函数名。对应 UE5 <c>OnPlayNodeFuncName</c>；
        /// UE5 在 <c>PlayDialogueNode_Implementation</c> 里 FindFunction+ProcessEvent，
        /// Flax 由 <see cref="DialogueClass.DispatchNodePlayed"/> 派发。
        /// </summary>
        public string OnPlayNodeFuncName { get; set; } = "";

        /// <summary>本行朝向的说话者 ID。对应 UE5 <c>DirectedAtSpeakerID</c>。</summary>
        public string DirectedAtSpeakerID { get; set; } = "";

        /// <summary>本行是否可被玩家跳过。对应 UE5 <c>bIsSkippable</c>（构造默认 true）。</summary>
        public bool bIsSkippable { get; set; } = true;

        /// <summary>本次实际播放出来的行（GetRandomLine 的返回值副本）。对应 UE5 <c>PlayedLine</c>。</summary>
        public DialogueLine PlayedLine { get; set; }

        /// <summary>接在本节点后的 NPC 回复。对应 UE5 <c>NPCReplies</c>。</summary>
        public List<DialogueNode_NPC> NPCReplies { get; set; } = new List<DialogueNode_NPC>();

        /// <summary>玩家可选回复。对应 UE5 <c>PlayerReplies</c>。</summary>
        public List<DialogueNode_Player> PlayerReplies { get; set; } = new List<DialogueNode_Player>();

        /// <summary>所属对话。对应 UE5 <c>OwningDialogue</c>。</summary>
        public DialogueClass OwningDialogue { get; set; }

        /// <summary>拥有本节点的叙事组件。对应 UE5 <c>OwningComponent</c>。</summary>
        public TalesComponent OwningComponent { get; set; }

        /// <summary>共享的随机源（避免每行 new Random() 在同一帧拿到同一种子）。</summary>
        private static readonly Random _random = new Random();

        /// <summary>
        /// 随机挑一条满足条件的对话行，并把 <see cref="ELineDuration.Default"/> 解析成具体时长。
        /// 对应 UE5 <c>UDialogueNode::GetRandomLine</c>（DialogueSM.cpp:29-125）。
        /// </summary>
        /// <param name="bStandalone">
        /// 是否单机。UE5:108-122 —— 服务端收不到音频/序列结束事件，必须退化成固定时长。
        /// </param>
        public virtual DialogueLine GetRandomLine(bool bStandalone = true)
        {
            var newLine = new DialogueLine();

            // 候选顺序（备选在前、主行在后）、条件过滤与随机下标都在纯层，
            // 因此「DialogueLine.Conditions 是否真被遵守」可被进程外断言。
            var allLines = NarrativeDialogueSelection.CollectCandidateLines(AlternativeLines, Line);

            // 无组件（编辑器预览/单元测试）时不做条件过滤，保持旧行为。
            var allPassingLines = OwningComponent != null
                ? NarrativeDialogueSelection.FilterPassing(allLines, IsLineConditionsMet)
                : NarrativeDialogueSelection.FilterPassing<DialogueLine>(allLines, null);

            int picked = NarrativeDialogueSelection.PickRandomIndex(allPassingLines.Count, _random);
            if (picked >= 0)
            {
                // 返回副本：Duration 解析不能写回对话资产里的原始行
                newLine = allPassingLines[picked].Clone();
            }

            // picked < 0（没有任何行通过条件）时保持 UE5 的回退语义：
            // 返回上面默认构造的空行，绝不返回 null。

            // LD_Default 的解析（UE5:92-106）
            if (newLine.Duration == ELineDuration.Default)
            {
                var shot = newLine.ResolveShot();

                if (!string.IsNullOrEmpty(newLine.SoundPath))
                {
                    newLine.Duration = ELineDuration.WhenAudioEnds;
                }
                else if (shot != null && string.IsNullOrWhiteSpace(newLine.Text))
                {
                    newLine.Duration = ELineDuration.WhenSequenceEnds;
                }
                else
                {
                    newLine.Duration = ELineDuration.AfterReadingTime;
                }
            }

            // 非单机（服务端）拿不到音频/序列结束事件，退化为固定时长（UE5:108-122）
            if (!bStandalone)
            {
                if (newLine.Duration == ELineDuration.WhenAudioEnds)
                {
                    newLine.Duration = ELineDuration.AfterDuration;
                    newLine.DurationSecondsOverride = ResolveAudioDuration(newLine.SoundPath, 0.2f);
                }
                else if (newLine.Duration == ELineDuration.WhenSequenceEnds)
                {
                    NarrativeLog.LogWarning("WhenSequenceEnds 时长在网络化游戏中不受支持，回退到音频长度。");
                    newLine.Duration = ELineDuration.AfterDuration;
                    newLine.DurationSecondsOverride = ResolveAudioDuration(newLine.SoundPath, 0.2f);
                }
            }

            return newLine;
        }

        /// <summary>
        /// 单条对话行的条件是否全部满足。对应 UE5 DialogueSM.cpp:45-77（该段与节点级逻辑重复，
        /// 这里统一复用节点侧的同一套纯求值聚合 <see cref="NarrativeConditionEvaluation"/>）。
        /// </summary>
        private bool IsLineConditionsMet(DialogueLine lineToCheck)
        {
            if (lineToCheck == null) return false;
            if (lineToCheck.Conditions == null || lineToCheck.Conditions.Count == 0) return true;

            var pawn = OwningComponent.GetOwningPawn();
            var controller = OwningComponent.GetOwningController();

            var requests = new List<NarrativeConditionEvalRequest>();
            foreach (var cond in lineToCheck.Conditions)
            {
                if (cond == null) continue;
                requests.Add(cond.BuildEvalRequest(pawn, controller, OwningComponent));
            }

            return NarrativeConditionEvaluation.EvaluateAll(requests, true);
        }

        /// <summary>
        /// 取语音资产时长（秒），失败时返回 <paramref name="fallback"/>。
        /// 对应 UE5 的 <c>DialogueSound->GetDuration()</c>。
        /// </summary>
        internal static float ResolveAudioDuration(string soundPath, float fallback)
        {
            if (string.IsNullOrEmpty(soundPath)) return fallback;

            var clip = Content.Load<AudioClip>(soundPath);
            if (clip != null && clip.Length > 0f)
            {
                return clip.Length;
            }

            return fallback;
        }

        /// <summary>
        /// 取第一个满足条件的 NPC 回复。对应 UE5 <c>UDialogueNode::GetFirstValidNPCReply</c>（DialogueSM.cpp:127-148）：
        /// 先按图位置（竖直走线时按 X，否则按 Y）排序，再取第一个条件通过的。
        /// </summary>
        public DialogueNode_NPC GetFirstValidNPCReply(object owningController, object owningPawn, TalesComponent narrativeComponent)
        {
            if (NPCReplies == null) return null;

            var repliesToCheck = SortByGraphPosition(new List<DialogueNode_NPC>(NPCReplies));

            // 「取第一个条件通过的」这条规则本体在纯层（与回复链、玩家回复共用同一份过滤实现）。
            return NarrativeDialogueSelection.FirstPassing(
                repliesToCheck,
                npcReply => npcReply.AreConditionsMet(owningPawn, owningController, narrativeComponent));
        }

        /// <summary>
        /// 取所有条件通过的玩家回复并按图位置排序。
        /// 对应 UE5 <c>UDialogueNode::GetPlayerReplies</c>（DialogueSM.cpp:150-171）。
        /// </summary>
        public List<DialogueNode_Player> GetPlayerReplies(object owningController, object owningPawn, TalesComponent narrativeComponent)
        {
            // 过滤段在纯层（保留输入顺序、跳过 null 条目），排序仍按 UE5 在过滤之后进行。
            var validReplies = NarrativeDialogueSelection.FilterPassing(
                PlayerReplies,
                playerReply => playerReply.AreConditionsMet(owningPawn, owningController, narrativeComponent));

            return SortByGraphPosition(validReplies);
        }

        /// <summary>按图位置排序（UE5 用 bEnableVerticalWiring 决定 X/Y 键）。</summary>
        protected static List<T> SortByGraphPosition<T>(List<T> nodes) where T : DialogueNode
        {
            if (nodes == null) return new List<T>();
            nodes.Sort((a, b) =>
            {
                float keyA = a != null ? a.GetGraphSortKey() : 0f;
                float keyB = b != null ? b.GetGraphSortKey() : 0f;
                return keyA.CompareTo(keyB);
            });
            return nodes;
        }

        /// <summary>
        /// 是否为「路由节点」（没有可播内容，只用来连线与跑事件）。
        /// 对应 UE5 <c>UDialogueNode::IsRoutingNode</c>（DialogueSM.cpp:201-209）：
        /// 有 Shot、有语音、有事件、或有非空文本，都算有内容。
        /// </summary>
        public virtual bool IsRoutingNode()
        {
            if (Line == null) return Events == null || Events.Count == 0;

            if (Line.ResolveShot() != null) return false;
            if (!string.IsNullOrEmpty(Line.SoundPath)) return false;
            if (Events != null && Events.Count > 0) return false;
            if (!string.IsNullOrWhiteSpace(Line.Text)) return false;

            return true;
        }

        /// <summary>对应 UE5 <c>UDialogueNode::GetDialogueText</c>。</summary>
        public string GetDialogueText()
        {
            return Line != null ? (Line.Text ?? "") : "";
        }

        /// <summary>对应 UE5 <c>UDialogueNode::EnsureUniqueID</c>：在同对话的所有节点里保证 ID 唯一。</summary>
        public override void EnsureUniqueID()
        {
            if (OwningDialogue == null) return;

            // List<string> 没有比较器构造重载：用 HashSet 承载 Ordinal 比较语义（同时加速 Contains）
            var existing = new HashSet<string>(StringComparer.Ordinal);
            foreach (var node in OwningDialogue.GetNodes())
            {
                if (node != null && !ReferenceEquals(node, this) && !string.IsNullOrEmpty(node.ID))
                {
                    existing.Add(node.ID);
                }
            }

            if (!existing.Contains(ID)) return;

            int suffix = 1;
            string newID = ID + suffix;
            while (existing.Contains(newID))
            {
                suffix++;
                newID = ID + suffix;
            }

            ID = newID;
        }
    }
}

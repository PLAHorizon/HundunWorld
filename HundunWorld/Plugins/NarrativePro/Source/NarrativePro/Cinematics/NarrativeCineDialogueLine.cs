using System;
using System.Collections.Generic;
using NarrativePro.Tales;
using NarrativePro.Tales.Events;

namespace NarrativePro.Cinematics
{
    /// <summary>
    /// 电影对话中可选择的对话行。对应 UE5 <c>FNarrativeCineDialogueLine</c>
    /// （NarrativeLevelSequenceActor.h:117-166）：玩家在过场序列播放期间选中的选项，
    /// 选中后把序列跳到 DestinationSequence（或在无目的地序列时跳到命名 Marker），
    /// 并携带跳转后的播放设置覆盖项。
    /// <para>
    /// 端口消费方：<c>Tales/Dialogue/DialogueNode_Player.CinematicOption</c> +
    /// <c>Dialogue.BeginCinematicDialogueOption/EndCinematicDialogueOption</c>
    /// （经 <see cref="AsyncAction_PlayNarrativeSequence"/> / <see cref="AsyncAction_BlendOutNarrativeSequence"/> 播放与收尾）。
    /// Flax 无命名序列 Marker 轨道（已在 FlaxEngine.CSharp.xml 核实无 Sequencer 类型），
    /// <see cref="DestinationMarker"/> 仅在无目的地序列时作为演出备注记录于日志。
    /// </para>
    /// <para>字段全部为带 setter 的属性，保证对话资产 JSON（大小写不敏感绑定）可写入。</para>
    /// </summary>
    [Serializable]
    public class NarrativeCineDialogueLine
    {
        /// <summary>选中本行需全部满足的条件。对应 UE5 <c>Conditions</c>（Instanced UNarrativeCondition 数组）。</summary>
        public List<NarrativeCondition> Conditions { get; set; } = new List<NarrativeCondition>();

        /// <summary>展示给玩家的选项文本。对应 UE5 <c>OptionText</c>。</summary>
        public string OptionText { get; set; } = "";

        /// <summary>选项后的提示文本（说服/威吓/求助等）。对应 UE5 <c>HintText</c>。</summary>
        public string HintText { get; set; } = "";

        /// <summary>选中后跳转播放的目的地序列路径；为空则尝试跳到 <see cref="DestinationMarker"/>。对应 UE5 <c>DestinationSequence</c>。</summary>
        public string DestinationSequencePath { get; set; } = "";

        /// <summary>目的地序列内的命名 Marker（Flax 无 Marker 轨道，仅日志备注）。对应 UE5 <c>DestinationMarker</c>。</summary>
        public string DestinationMarker { get; set; } = "";

        /// <summary>
        /// 跳转时是否让角色动画混合过渡（UE5 默认直接硬切，开启后由绑定走位平滑代替混合）。
        /// 对应 UE5 <c>bBlendAnimsBetweenSequences</c>：为 false 时端口会清掉播放设置的
        /// bWalkBindingsToStartTransform，让角色瞬时落位（硬切）；为 true 时保留混合起演。
        /// </summary>
        public bool bBlendAnimsBetweenSequences { get; set; } = true;

        /// <summary>沿用序列现有播放设置；false 时改用 <see cref="DestinationPlaybackSettings"/>。对应 UE5 <c>bKeepExistingSequenceSettings</c>。</summary>
        public bool bKeepExistingSequenceSettings { get; set; } = true;

        /// <summary>目的地播放设置（仅 <see cref="bKeepExistingSequenceSettings"/> 为 false 时生效）。对应 UE5 <c>DestinationPlaybackSettings</c>。</summary>
        public NarrativeSequencePlaybackSettings DestinationPlaybackSettings { get; set; } = new NarrativeSequencePlaybackSettings();

        /// <summary>
        /// 条件求值：全部条件通过才可选中。对应 UE5 蓝图侧对 <c>Conditions</c> 的 AreConditionsMet 口径
        /// （空条件视为通过；null 条件跳过——与 <c>DialogueNode.IsLineConditionsMet</c> 同一规则）。
        /// </summary>
        public bool CanSelect(object pawn, object controller, TalesComponent narrativeComponent)
        {
            if (Conditions == null || Conditions.Count == 0) return true;

            foreach (var cond in Conditions)
            {
                if (cond == null) continue;
                if (!cond.Evaluate(pawn, controller, narrativeComponent))
                {
                    return false;
                }
            }

            return true;
        }
    }
}

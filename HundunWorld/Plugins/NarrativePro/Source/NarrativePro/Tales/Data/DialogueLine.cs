using System;
using System.Collections.Generic;
using NarrativePro.Core;
using NarrativePro.Tales.Events;

namespace NarrativePro.Tales.Data
{
    /// <summary>
    /// 一行对话。对应 UE5 <c>FDialogueLine</c>（DialogueSM.h:51-124）。
    /// </summary>
    [Serializable]
    public class DialogueLine
    {
        /// <summary>对话文本。UI 默认可直接显示。</summary>
        public string Text { get; set; } = "";

        /// <summary>本行何时结束。对应 UE5 <c>Duration</c>，默认 Default。</summary>
        public ELineDuration Duration { get; set; } = ELineDuration.Default;

        /// <summary>
        /// <see cref="ELineDuration.AfterDuration"/> 模式下本行的秒数。
        /// 对应 UE5 <c>DurationSecondsOverride</c>。
        /// </summary>
        public float DurationSecondsOverride { get; set; } = 0f;

        /// <summary>语音资产路径。对应 UE5 <c>DialogueSound</c>（TObjectPtr&lt;USoundBase&gt;）。</summary>
        public string SoundPath { get; set; } = "";

        /// <summary>身体动画名。对应 UE5 <c>DialogueMontage</c>（Body 标签骨骼组件上播放）。</summary>
        public string BodyAnimationName { get; set; } = "";

        /// <summary>面部动画名。对应 UE5 <c>FacialAnimation</c>（Face 标签骨骼组件上播放）。</summary>
        public string FacialAnimationName { get; set; } = "";

        /// <summary>镜头序列名（JSON 兼容字段）。对应 UE5 <c>Shot</c> 的标识。</summary>
        public string ShotName { get; set; } = "";

        /// <summary>本行的 CG/过场视频路径（工程内）。对应 UE5 端口扩展：行上直接挂过场。</summary>
        public string CutscenePath { get; set; } = "";

        /// <summary>true＝等 CG 播完再结束本行（与 <see cref="ELineDuration.WhenSequenceEnds"/> 同类的等待语义）。</summary>
        public bool WaitForCutscene { get; set; }

        /// <summary>
        /// 本行的镜头序列。对应 UE5 <c>Shot</c>（Instanced UNarrativeDialogueSequence），
        /// 会覆盖说话者自身的默认镜头。设置后 <see cref="ShotName"/> 取其 FriendlyShotName。
        /// </summary>
        public NarrativeDialogueSequence Shot;

        /// <summary>本行的可选条件，全部满足才会被选中。对应 UE5 <c>Conditions</c>。</summary>
        public List<NarrativeCondition> Conditions { get; set; } = new List<NarrativeCondition>();

        /// <summary>
        /// 取实际生效的镜头序列：优先行上直接设置的 Shot 实例，其次按 <see cref="ShotName"/>
        /// 经 <see cref="NarrativePro.Cinematics.NarrativeDialogueShotRegistry"/> 解析
        /// （Flax 无 Instanced UObject，UE5 的对象引用在这里降为按名解析，见注册表文件头「端口差异」）。
        /// 名字为空或未命中返回 null，调用方按原语义回退说话者/对话默认镜头。
        /// </summary>
        public NarrativeDialogueSequence ResolveShot()
        {
            if (Shot != null) return Shot;
            if (string.IsNullOrEmpty(ShotName)) return null;

            // 解析结果按名字缓存：行对象会被逐行反复问（GetRandomLine/IsRoutingNode/ResolveShotForLine），
            // 且 Dialogue.cs:1322 的「同镜头不重启」依赖引用相等，必须稳定返回同一份实例。
            if (_resolvedShot != null && _resolvedShotName == ShotName) return _resolvedShot;
            _resolvedShot = NarrativePro.Cinematics.NarrativeDialogueShotRegistry.Resolve(ShotName);
            _resolvedShotName = ShotName;
            return _resolvedShot;
        }

        [NonSerialized] private NarrativeDialogueSequence _resolvedShot;
        [NonSerialized] private string _resolvedShotName;

        /// <summary>
        /// 复制一份行数据。UE5 中 <c>FDialogueLine</c> 是结构体，<c>GetRandomLine</c> 会在副本上
        /// 把 <c>LD_Default</c> 解析成具体时长；Flax 里它是引用类型，必须显式克隆，
        /// 否则会污染对话资产里的原始数据。
        /// </summary>
        public DialogueLine Clone()
        {
            var copy = (DialogueLine)MemberwiseClone();
            copy.Conditions = Conditions != null ? new List<NarrativeCondition>(Conditions) : new List<NarrativeCondition>();
            return copy;
        }
    }
}

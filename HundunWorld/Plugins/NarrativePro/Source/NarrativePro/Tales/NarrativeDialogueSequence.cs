using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;
using DialogueClass = NarrativePro.Tales.Dialogue.Dialogue;

namespace NarrativePro.Tales
{
    /// <summary>
    /// 锚点原点规则。对应 UE5 EAnchorOriginRule。
    /// 定义锚定镜头的原点位置。
    /// </summary>
    public enum EAnchorOriginRule
    {
        /// <summary>禁用锚定/跟踪。</summary>
        AOR_Disabled,

        /// <summary>锚定到所有说话者之间的中点。</summary>
        AOR_ConversationCenter,

        /// <summary>锚点到当前说话者。</summary>
        AOR_Speaker,

        /// <summary>锚点到当前听者。</summary>
        AOR_Listener,

        /// <summary>使用自定义 Avatar ID 作为锚点。</summary>
        AOR_Custom
    }

    /// <summary>
    /// 锚点旋转规则。对应 UE5 EAnchorRotationRule。
    /// </summary>
    public enum EAnchorRotationRule
    {
        /// <summary>使用锚点 Avatar 的前向向量。</summary>
        ARR_AnchorActorForwardVector,

        /// <summary>使用对话方向（说话者→听者的偏移向量）。</summary>
        ARR_Conversation
    }

    /// <summary>
    /// 镜头跟踪规则。对应 UE5 EShotTrackingRule。
    /// </summary>
    public enum EShotTrackingRule
    {
        /// <summary>禁用跟踪。</summary>
        STR_Disabled,

        /// <summary>跟踪当前说话者。</summary>
        STR_Speaker,

        /// <summary>跟踪当前听者。</summary>
        STR_Listener,

        /// <summary>跟踪自定义 Avatar ID。</summary>
        STR_Custom
    }

    /// <summary>
    /// 镜头跟踪设置。对应 UE5 FShotTrackingSettings。
    /// </summary>
    [Serializable]
    public class ShotTrackingSettings
    {
        public ShotTrackingSettings()
        {
            AvatarToTrack = EShotTrackingRule.STR_Speaker;
            bUpdateTrackingEveryFrame = false;
            UpdateTrackingInterpSpeed = 0.5f;
            // UE5 中将摄像机跟踪到面部（约头部骨骼前 10 单位）。
            TrackBoneNudge = new Vector3(10f, 0f, 0f);
        }

        /// <summary>要跟踪的 Avatar。</summary>
        public EShotTrackingRule AvatarToTrack;

        /// <summary>自定义跟踪的 Avatar ID（当 AvatarToTrack == STR_Custom 时使用）。</summary>
        public string TrackedAvatarCustomID = "";

        /// <summary>Actor 本地空间下的跟踪偏移。</summary>
        public Vector3 TrackBoneNudge;

        /// <summary>是否每帧更新跟踪位置（角色头部移动较多时重要）。</summary>
        public bool bUpdateTrackingEveryFrame;

        /// <summary>每帧跟踪插值速度。</summary>
        public float UpdateTrackingInterpSpeed;
    }

    /// <summary>
    /// 对话序列。对应 UE5 UNarrativeDialogueSequence。
    /// 封装一段 Level Sequence 及其在对话上下文中播放所需的额外数据。
    /// Flax 中无 LevelSequence 系统，序列播放保留占位实现。
    /// </summary>
    [Serializable]
    public class NarrativeDialogueSequence
    {
        /// <summary>镜头友好名称。</summary>
        public string FriendlyShotName = "";

        /// <summary>序列资产路径列表（随机选一个播放）。</summary>
        public List<string> SequenceAssetPaths = new List<string>();

        /// <summary>播放设置（简化为占位字段）。</summary>
        public bool bAutoPlay = true;
        public bool bLoop = false;
        public float PlaybackSpeed = 1f;

        /// <summary>如果此序列已从更早的节点开始播放，是否重启镜头。</summary>
        public bool bShouldRestart = false;

        /// <summary>序列原点相对规则。</summary>
        public EAnchorOriginRule AnchorOriginRule = EAnchorOriginRule.AOR_Disabled;

        /// <summary>锚点原点偏移（在说话者 transform 空间下应用）。</summary>
        public Vector3 AnchorOriginNudge = Vector3.Zero;

        /// <summary>序列旋转相对规则。</summary>
        public EAnchorRotationRule AnchorRotationRule = EAnchorRotationRule.ARR_AnchorActorForwardVector;

        /// <summary>自定义锚点 Avatar ID（当 AnchorOriginRule == AOR_Custom 时使用）。</summary>
        public string AnchorAvatarCustomID = "";

        /// <summary>是否强制玩家和其他说话者位于屏幕两侧（180度规则）。</summary>
        public bool bUse180DegreeRule = false;

        /// <summary>180度规则下，沿 Y 轴推动的距离。</summary>
        public float UnitsY180DegreeRule = 50f;

        /// <summary>180度规则下，沿 Yaw 推动的角度。</summary>
        public float DegreesYaw180DegreeRule = 15f;

        /// <summary>LookAt 跟踪设置。</summary>
        public ShotTrackingSettings LookAtTrackingSettings = new ShotTrackingSettings();

        /// <summary>Focus 跟踪设置。</summary>
        public ShotTrackingSettings FocusTrackingSettings = new ShotTrackingSettings();

        /// <summary>是否绘制焦点调试框。</summary>
        public bool bDrawDebugFocusPoint = false;

        // ===== 运行期引用（UE5 中为 TWeakObjectPtr） =====

        [NonSerialized] public Actor Speaker;
        [NonSerialized] public Actor Listener;
        [NonSerialized] public Actor AnchorActor;
        [NonSerialized] public Actor LookAtActor;
        [NonSerialized] public Actor FocusActor;
        [NonSerialized] public Actor SequenceActor;
        [NonSerialized] public FlaxEngine.Camera Cinecam;
        [NonSerialized] public DialogueClass Dialogue;

        /// <summary>每帧更新（Flax 中由 TalesComponent 调用）。</summary>
        public virtual void Tick(float deltaTime)
        {
            // Flax-不兼容: UE5 的 LevelSequence 在 Flax 无对应物，保留占位。原文 TODO: Flax 无 LevelSequence，跟踪/锚点更新逻辑以占位形式保留。
            if (bUse180DegreeRule && Speaker != null && Listener != null)
            {
                // 180 度规则：保持说话者与听者位于屏幕两侧
            }
        }

        /// <summary>
        /// 开始播放序列。anchor 是头部 transform 作为镜头偏移基准的 Actor；
        /// speaker 接收摄像机的跟踪/焦点。
        /// </summary>
        public virtual void BeginPlaySequence(Actor inSequenceActor, DialogueClass inDialogue, Actor inSpeaker, Actor inListener)
        {
            SequenceActor = inSequenceActor;
            Dialogue = inDialogue;
            Speaker = inSpeaker;
            Listener = inListener;
            AnchorActor = ResolveAnchorActor();
            LookAtActor = ResolveLookAtActor();
            FocusActor = ResolveFocusActor();

            NarrativeLog.Log($"NarrativeDialogueSequence.BeginPlaySequence: speaker={Speaker?.Name}, listener={Listener?.Name}, anchor={AnchorOriginRule}");
            PlaySequence();
        }

        /// <summary>停止前回调（UE5 BlueprintImplementableEvent 占位）。</summary>
        public virtual void OnStop()
        {
        }

        /// <summary>结束序列。</summary>
        public virtual void EndSequence()
        {
            NarrativeLog.Log("NarrativeDialogueSequence.EndSequence");
            OnStop();
            Cinecam = null;
            SequenceActor = null;
        }

        /// <summary>播放序列（Flax 无 LevelSequence，仅占位）。</summary>
        protected virtual void PlaySequence()
        {
            // Flax-不兼容: UE5 的 LevelSequence 在 Flax 无对应物，保留占位。原文 TODO: Flax 无 LevelSequence 系统，需要自定义序列播放或对接 Visject 动画。
            NarrativeLog.Log("NarrativeDialogueSequence.PlaySequence (Flax 无 LevelSequence，占位实现)");
        }

        /// <summary>
        /// 返回序列拍摄空间。默认使用锚点 Actor 的头部位置，
        /// 不同身高的角色也能正确对齐。若启用 180 度规则，应用额外偏移。
        /// </summary>
        public virtual Transform GetShotAnchorTransform()
        {
            if (AnchorActor == null) return Transform.Identity;
            var t = AnchorActor.Transform;
            // 头部前推（UE5 中约为 10 单位）
            t.Translation += AnchorActor.Transform.Forward * 10f;
            if (bUse180DegreeRule)
            {
                t.Translation += AnchorActor.Transform.Right * UnitsY180DegreeRule;
                t.Orientation *= Quaternion.Euler(0, DegreesYaw180DegreeRule, 0);
            }
            return t;
        }

        /// <summary>节点上显示的文本。</summary>
        public virtual string GetGraphDisplayText()
        {
            return string.IsNullOrEmpty(FriendlyShotName) ? "Dialogue Sequence" : FriendlyShotName;
        }

        // ===== 解析运行期 Actor =====

        protected Actor ResolveAnchorActor()
        {
            switch (AnchorOriginRule)
            {
                case EAnchorOriginRule.AOR_Speaker: return Speaker;
                case EAnchorOriginRule.AOR_Listener: return Listener;
                case EAnchorOriginRule.AOR_ConversationCenter:
                    // 说话者与听者的中点（无对应 Actor，返回 null，由 GetShotAnchorTransform 计算）
                    return null;
                case EAnchorOriginRule.AOR_Custom:
                    // TODO [需接入 Avatar 系统]: 通过 AnchorAvatarCustomID 在对话中查找对应 Avatar
                    return Speaker;
                case EAnchorOriginRule.AOR_Disabled:
                default:
                    return null;
            }
        }

        protected Actor ResolveLookAtActor()
        {
            return ResolveTrackingActor(LookAtTrackingSettings);
        }

        protected Actor ResolveFocusActor()
        {
            return ResolveTrackingActor(FocusTrackingSettings);
        }

        protected Actor ResolveTrackingActor(ShotTrackingSettings settings)
        {
            if (settings == null) return null;
            switch (settings.AvatarToTrack)
            {
                case EShotTrackingRule.STR_Speaker: return Speaker;
                case EShotTrackingRule.STR_Listener: return Listener;
                case EShotTrackingRule.STR_Custom:
                    // TODO [需接入 Avatar 系统]: 通过 TrackedAvatarCustomID 查找对应 Avatar
                    return Speaker;
                case EShotTrackingRule.STR_Disabled:
                default:
                    return null;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.Items;
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

        /// <summary>
        /// 序列时长（秒）。Flax 未向脚本暴露 LevelSequence 资产时长，
        /// 由设计侧显式配置，供 <c>LD_WhenSequenceEnds</c> 求真实时长使用；0 表示未知。
        /// </summary>
        public float SequenceDurationSeconds = 0f;

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

        /// <summary>本镜头当前的播放器（Flax 侧自驱动，替代 UE5 的 ULevelSequencePlayer）。</summary>
        [NonSerialized] protected NarrativePro.Cinematics.NarrativeLevelSequencePlayer _player;

        /// <summary>主动 EndSequence 时抑制一次「播放完成」广播。</summary>
        [NonSerialized] private bool _bSupressFinishedBroadcast;

        // ===== 电影呈现与震动（缺陷⑤/⑥，可移植部分） =====

        /// <summary>是否显示电影黑边（镜头级开关；也可由对话 WantsCinematicBars 标签驱动）。</summary>
        public bool bShowCinematicBars = false;

        /// <summary>是否在镜头期间隐藏 HUD（镜头级开关；也可由对话 WantsHideHUD 标签驱动）。</summary>
        public bool bHideHUD = false;

        /// <summary>隐藏 HUD 时是否连必要控件（任务更新等）一起隐藏。对应 UE5 bHideEvenEssentialHUDElements。</summary>
        public bool bHideEvenEssentialHUD = false;

        /// <summary>本镜头是否允许玩家跳过（对应 UE5 bCanSkip / 对话 bUnskippable 的镜头级细化）。</summary>
        public bool bCanSkip = true;

        /// <summary>
        /// 电影遮幅（crop）设置。对应 UE5 <c>UNarrativeDialogueSequence::CropSettings</c>
        /// （NarrativeDialogueSequence.h:131，类型是引擎 <c>FPlateCropSettings</c>；
        /// 构造函数默认宽高比 2.35，NarrativeDialogueSequence.cpp:27）。
        /// UE5 在 BeginPlaySequence 把它赋给序列 spawn 出的 CineCameraComponent
        /// （NarrativeDialogueSequence.cpp:209），Flax 相机无遮幅 API，
        /// 由 <see cref="NarrativePro.Cinematics.NarrativeCinematicBars"/> 换算为黑条高度占比消费
        /// （见 ApplyPresentation）。默认 2.35 银幕在 16:9 屏上 ≈ 0.12，与此前固定黑边一致，不改变现有表现。
        /// </summary>
        public NarrativePro.Cinematics.PlateCropSettings CropSettings { get; set; } =
            new NarrativePro.Cinematics.PlateCropSettings();

        /// <summary>跳过键（默认 Esc）。</summary>
        public KeyboardKeys SkipKey = KeyboardKeys.Escape;

        /// <summary>镜头相机震动振幅（场景单位）。<=0 表示不震动。</summary>
        public float ShakeAmplitude = 0f;

        /// <summary>镜头相机震动时长（秒）。<=0 表示不震动。</summary>
        public float ShakeDurationSeconds = 0f;

        /// <summary>镜头相机震动频率（Hz）。</summary>
        public float ShakeFrequencyHz = 8f;

        /// <summary>镜头是否已把说话者/听者走到拍摄起点（对应 UE5 bWalkBindingsToStartTransform，180 度规则前置）。</summary>
        public bool bWalkToStartTransform = false;

        [NonSerialized] private NarrativePro.UnrealFramework.NarrativePlayerController _controller;
        [NonSerialized] private NarrativePro.Camera.NarrativeCameraComponent _cameraComponent;
        [NonSerialized] private bool _bPresentationApplied;
        [NonSerialized] private bool _bBarsShown;
        [NonSerialized] private bool _bHudHidden;
        [NonSerialized] private bool _bCinematicModeSet;
        private readonly System.Collections.Generic.List<(NarrativePro.GAS.NarrativeAbilitySystemComponent Asc, GameplayTag Tag)> _appliedTags =
            new System.Collections.Generic.List<(NarrativePro.GAS.NarrativeAbilitySystemComponent, GameplayTag)>();

        /// <summary>每帧更新（Flax 中由 TalesComponent 调用）。</summary>
        public virtual void Tick(float deltaTime)
        {
            // 跳过输入：对话电影镜头期间玩家可按 SkipKey 立即结束本镜头（推进/结束当前行）。
            if (bCanSkip && Input.GetKeyDown(SkipKey))
            {
                SkipSequence();
                return;
            }

            // 180 度规则（缺陷⑥）：把说话者与听者分置到镜头轴两侧，避免"越轴"跳切。
            if (bUse180DegreeRule && Speaker != null && Listener != null)
            {
                Apply180DegreeRule();
            }

            // 走到拍摄起点（对应 UE5 bWalkBindingsToStartTransform 的对话镜头版本）：
            // 把说话者平滑推向镜头锚点前方，替代 UE5 由序列轨驱动的走位。
            if (bWalkToStartTransform && Speaker != null)
            {
                WalkSpeakerToStart(deltaTime);
            }

            // 序列播放推进：Flax 无 LevelSequence 脚本 API，用自驱动的播放器计算时长。
            if (_player != null && _player.bIsPlaying)
            {
                _player.Tick(deltaTime);
            }
        }

        /// <summary>跳过当前镜头：结束序列并广播"播放完成"，让对话按 WhenSequenceEnds 收行。</summary>
        public virtual void SkipSequence()
        {
            if (!bCanSkip) return;
            NarrativeLog.Log($"NarrativeDialogueSequence.SkipSequence: {FriendlyShotName}");
            RevertPresentation();
            EndSequence();
            OnSequenceFinished?.Invoke();
        }

        /// <summary>180 度规则：以镜头 forward 为银幕横轴，把说话者/听者沿 right 分置两侧并朝内偏转。</summary>
        private void Apply180DegreeRule()
        {
            var anchor = GetShotAnchorTransform();
            Vector3 right = anchor.Right;   // 银幕横轴
            Vector3 separation = right * (UnitsY180DegreeRule * 0.5f);

            float half = UnitsY180DegreeRule * 0.5f;
            var center = (Speaker.Position + Listener.Position) * 0.5f;

            var speakerTransform = Speaker.Transform;
            speakerTransform.Translation = center - separation;
            speakerTransform.Orientation = anchor.Orientation * Quaternion.Euler(0f, 180f - DegreesYaw180DegreeRule, 0f);
            Speaker.Transform = speakerTransform;

            var listenerTransform = Listener.Transform;
            listenerTransform.Translation = center + separation;
            listenerTransform.Orientation = anchor.Orientation * Quaternion.Euler(0f, DegreesYaw180DegreeRule, 0f);
            Listener.Transform = listenerTransform;
        }

        /// <summary>把说话者沿目标方向平滑走到镜头锚点前方（简化的"走到起始变换"）。</summary>
        private void WalkSpeakerToStart(float deltaTime)
        {
            if (AnchorActor == null) return;
            Vector3 target = GetShotAnchorTransform().Translation;
            Vector3 current = Speaker.Position;
            float step = Mathf.Clamp(deltaTime * 4f, 0f, 1f); // 收敛速度
            Vector3 next = Vector3.Lerp(current, target, step);
            var t = Speaker.Transform;
            t.Translation = next;
            Speaker.Transform = t;
        }

        /// <summary>
        /// 序列时长（秒）。对应 UE5 中 <c>ULevelSequencePlayer::OnFinished</c> 所代表的等待时长。
        /// <para>
        /// Flax 1.12 未向脚本暴露 LevelSequence 资产时长，因此这里按优先级求解真实值：
        /// ① 运行期播放器已知的 <see cref="NarrativeLevelSequencePlayer.TotalDuration"/>；
        /// ② 数据里显式配置的 <see cref="SequenceDurationSeconds"/>；
        /// ③ 循环播放时视为「永不自动结束」；
        /// ④ 都拿不到时返回 0，由调用方回退到别的时长策略。
        /// </para>
        /// </summary>
        public virtual float GetSequenceDurationSeconds()
        {
            if (_player != null && _player.TotalDuration > 0f)
            {
                return _player.TotalDuration;
            }

            if (bLoop)
            {
                return float.MaxValue;
            }

            return SequenceDurationSeconds > 0f ? SequenceDurationSeconds : 0f;
        }

        /// <summary>序列播放结束事件。对应 UE5 绑定到 <c>ULevelSequencePlayer::OnFinished</c>。</summary>
        public event Action OnSequenceFinished;

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

            // Flax 无 LevelSequencePlayer，用自驱动播放器承载时长与结束事件，
            // 对应 UE5 中 SP->OnFinished.AddDynamic(this, &UDialogue::EndCurrentLine) 的数据来源。
            if (_player == null)
            {
                _player = new NarrativePro.Cinematics.NarrativeLevelSequencePlayer();
                _player.OnFinished += _ =>
                {
                    if (!_bSupressFinishedBroadcast)
                    {
                        OnSequenceFinished?.Invoke();
                    }
                };
            }

            _player.TotalDuration = SequenceDurationSeconds;
            PlaySequence();

            if (bAutoPlay && _player.TotalDuration > 0f)
            {
                _player.Play();
            }
        }

        /// <summary>停止前回调（UE5 BlueprintImplementableEvent 占位）。</summary>
        public virtual void OnStop()
        {
        }

        /// <summary>结束序列。</summary>
        public virtual void EndSequence()
        {
            NarrativeLog.Log("NarrativeDialogueSequence.EndSequence");

            // 撤销电影呈现（黑边/HUD/门禁/受控标签/震动），幂等。
            RevertPresentation();

            if (_player != null)
            {
                // 主动结束镜头时不要把「播放完成」再广播一次，否则会误触发对话行结束。
                _bSupressFinishedBroadcast = true;
                if (_player.bIsPlaying)
                {
                    _player.Stop();
                }
                _bSupressFinishedBroadcast = false;
                _player = null;
            }

            OnStop();
            Cinecam = null;
            SequenceActor = null;
        }

        /// <summary>
        /// 播放序列的呈现部分（缺陷⑥：可移植的部分——黑边/隐藏 HUD/受控标签/震动）。
        /// UE5 里这些由 Level Sequence 相机节点与绑定轨驱动；Flax 无 Level Sequence，改为直接施加到
        /// 玩家控制器/相机组件/HUD 与角色 ASC 上，行为真实、可结束回收。
        /// </summary>
        protected virtual void PlaySequence()
        {
            // Flax-不兼容: 序列轨（相机动画/绑定）无法还原；此处实现可移植的电影呈现（黑边/HUD/标签/震动）。
            NarrativeLog.Log($"NarrativeDialogueSequence.PlaySequence: {FriendlyShotName}（应用电影呈现）");
            ApplyPresentation();
        }

        /// <summary>解析插件相机组件（对话镜头的震动落点）。</summary>
        private NarrativePro.Camera.NarrativeCameraComponent ResolveCameraComponent()
        {
            if (_cameraComponent != null && _cameraComponent.Actor != null) return _cameraComponent;
            _cameraComponent = Level.FindScript<NarrativePro.Camera.NarrativeCameraComponent>();
            return _cameraComponent;
        }

        /// <summary>解析本地玩家控制器。</summary>
        private NarrativePro.UnrealFramework.NarrativePlayerController ResolveController()
        {
            if (_controller != null && _controller.Actor != null) return _controller;
            _controller = Level.FindScript<NarrativePro.UnrealFramework.NarrativePlayerController>();
            return _controller;
        }

        /// <summary>施加电影呈现：黑边（消费 WantsCinematicBars）、隐藏 HUD（消费 WantsHideHUD）、受控/无敌标签、镜头震动。</summary>
        private void ApplyPresentation()
        {
            if (_bPresentationApplied) return;
            _bPresentationApplied = true;

            var controller = ResolveController();

            // 需求来源：镜头字段 OR 玩家身上的对话标签（WantsCinematicBars/WantsHideHUD 此前无任何消费方）
            bool wantsBars = bShowCinematicBars
                             || (controller != null && controller.HasMatchingGameplayTag(NarrativePro.Core.NarrativeGameplayTags.State_Player_WantsCinematicBars));
            bool wantsHide = bHideHUD
                             || (controller != null && controller.HasMatchingGameplayTag(NarrativePro.Core.NarrativeGameplayTags.State_Player_WantsHideHUD));

            // 黑边：自建屏幕空间覆盖层（NarrativeCinematicBars），Flax 无相机 letterbox API。
            if (wantsBars)
            {
                // 数据驱动：按镜头遮幅设置换算黑边高度（对应 UE5 CinecamComp->CropSettings = CropSettings）。
                NarrativePro.Cinematics.NarrativeCinematicBars.Show(CropSettings);
                _bBarsShown = true;
            }

            // 隐藏 HUD：走既有 NarrativeGameplayHUD.SetHUDHidden 真实接口（不改该文件，只调用）。
            if (wantsHide && !_bHudHidden)
            {
                var hud = Level.FindScript<NarrativePro.Widgets.NarrativeGameplayHUD>();
                if (hud != null)
                {
                    hud.SetHUDHidden(true, bHideEvenEssentialHUD);
                    _bHudHidden = true;
                }
                else if (controller != null)
                {
                    // 兜底：控制器路径已有 SetCinematicMode 的 HUD 分支
                    controller.SetCinematicMode(true, false, true, false, false);
                }
            }

            // 输入/朝向门禁：镜头期间禁止转动（移动在宿主装配模式下由游戏负责，这里只关转向注视输入）。
            if (controller != null && !_bCinematicModeSet)
            {
                controller.SetCinematicMode(true, false, false, false, true);
                _bCinematicModeSet = true;
            }

            // 受控/无敌标签：镜头期间把说话者/听者置为 SequencerControlled + Invulnerable（对应 NarrativeSequencerBindingConfig）。
            ApplyControlledTags(Speaker);
            ApplyControlledTags(Listener);
            ApplyControlledTags(AnchorActor);

            // 镜头相机震动：交给插件相机组件（其 OnUpdate 叠加确定性衰减振荡）。
            var cam = ResolveCameraComponent();
            if (cam != null && ShakeDurationSeconds > 0f && ShakeAmplitude > 0f)
            {
                cam.PlayCameraShake(ShakeAmplitude, ShakeDurationSeconds, ShakeFrequencyHz);
            }
        }

        /// <summary>回收电影呈现：撤销黑边/HUD/门禁/标签/震动。幂等。</summary>
        private void RevertPresentation()
        {
            if (!_bPresentationApplied) return;
            _bPresentationApplied = false;

            if (_bBarsShown)
            {
                NarrativePro.Cinematics.NarrativeCinematicBars.Hide();
                _bBarsShown = false;
            }

            if (_bHudHidden)
            {
                var hud = Level.FindScript<NarrativePro.Widgets.NarrativeGameplayHUD>();
                hud?.SetHUDHidden(false, false);
                _bHudHidden = false;
            }

            if (_bCinematicModeSet)
            {
                ResolveController()?.SetCinematicMode(false, false, _bHudHidden, false, false);
                _bCinematicModeSet = false;
            }

            RemoveControlledTags();

            var cam = _cameraComponent;
            cam?.StopCameraShake();
        }

        /// <summary>给角色 ASC 施加 SequencerControlled + Invulnerable（记录以便结束回收）。</summary>
        private void ApplyControlledTags(Actor actor)
        {
            if (actor == null) return;
            // 缺陷1（宿主盲区）：宿主装配模式的游戏宿主没有 NarrativeCharacter 脚本，
            // 旧 GetScript<NarrativeCharacter>() 路径会让镜头期间标签静默不施加。
            // 改走 ArsenalStatics 多级解析链（与 ASC/交互查找同源，含引导登记的宿主兜底）。
            var asc = NarrativePro.Core.ArsenalStatics.GetNarrativeAbilitySystemComponent(actor);
            if (asc?.GameplayTags == null) return;

            AddOneControlledTag(asc, NarrativePro.Core.NarrativeGameplayTags.State_SequencerControlled);
            AddOneControlledTag(asc, NarrativePro.Core.NarrativeGameplayTags.State_Invulnerable);
        }

        private void AddOneControlledTag(NarrativePro.GAS.NarrativeAbilitySystemComponent asc, GameplayTag tag)
        {
            if (asc.GameplayTags.HasTag(tag)) return; // 避免重复计数
            asc.GameplayTags.AddTag(tag);
            _appliedTags.Add((asc, tag));
        }

        private void RemoveControlledTags()
        {
            for (int i = 0; i < _appliedTags.Count; i++)
            {
                var pair = _appliedTags[i];
                if (pair.Asc != null && pair.Asc.GameplayTags != null)
                {
                    pair.Asc.GameplayTags.RemoveTag(pair.Tag);
                }
            }
            _appliedTags.Clear();
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
                    // 按 Avatar ID 在对话参与者子树内查找（对应 UE5 用 AvatarID 查 Avatar 的做法）
                    var anchorAvatar = ResolveAvatarById(AnchorAvatarCustomID);
                    return anchorAvatar != null ? anchorAvatar : Speaker;
                case EAnchorOriginRule.AOR_Disabled:
                default:
                    return null;
            }
        }

        /// <summary>
        /// 按 Avatar ID 解析对话中的 Avatar Actor。
        /// UE5 由对话子系统持有 Avatar 表（<c>UNarrativeDialogueSequence</c> 的 Avatar 映射），
        /// Flax 无该子系统（已核对 FlaxEngine.CSharp.xml 无 Avatar/Dialogue 子系统类型），
        /// 此处以对话参与者（说话者/听者）及其子节点的名字索引代替。
        /// </summary>
        /// <param name="avatarId">自定义 Avatar ID（<c>AnchorAvatarCustomID</c> / <c>TrackedAvatarCustomID</c>）</param>
        /// <returns>命中的 Actor；未命中返回 null</returns>
        protected Actor ResolveAvatarById(string avatarId)
        {
            if (string.IsNullOrEmpty(avatarId)) return null;

            var candidates = new[] { Speaker, Listener };
            foreach (var root in candidates)
            {
                if (root == null) continue;
                if (string.Equals(root.Name, avatarId, System.StringComparison.Ordinal)) return root;
                var found = FindDescendantByName(root, avatarId);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>在父 Actor 的子树中按名查找 Actor（Flax 的 Actor.Children 递归）。</summary>
        private static Actor FindDescendantByName(Actor parent, string actorName)
        {
            if (parent == null) return null;
            foreach (var child in parent.Children)
            {
                if (child == null) continue;
                if (string.Equals(child.Name, actorName, System.StringComparison.Ordinal)) return child;
                var deeper = FindDescendantByName(child, actorName);
                if (deeper != null) return deeper;
            }
            return null;
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
                    // 按 Avatar ID 在对话参与者子树内查找（对应 UE5 用 AvatarID 查 Avatar 的做法）
                    var trackedAvatar = ResolveAvatarById(settings.TrackedAvatarCustomID);
                    return trackedAvatar != null ? trackedAvatar : Speaker;
                case EShotTrackingRule.STR_Disabled:
                default:
                    return null;
            }
        }
    }
}

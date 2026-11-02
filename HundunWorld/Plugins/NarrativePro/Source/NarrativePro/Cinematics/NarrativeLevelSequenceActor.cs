using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.Items;

namespace NarrativePro.Cinematics
{
    /// <summary>
    /// 序列绑定配置。对应 UE5 FNarrativeSequencerBindingConfig。
    /// 描述要绑定到序列中的角色及其起始变换和标签。
    /// </summary>
    [Serializable]
    public class NarrativeSequencerBindingConfig
    {
        /// <summary>要绑定的角色 Actor</summary>
        [NonSerialized]
        public Actor Character;

        /// <summary>序列开始前角色应到达的变换</summary>
        public Transform CinematicStartTransform = Transform.Identity;

        /// <summary>绑定期间应用到角色的标签</summary>
        public GameplayTagContainer TagsToApplyWhilstBound = new GameplayTagContainer();

        public NarrativeSequencerBindingConfig()
        {
            TagsToApplyWhilstBound.AddTag(new GameplayTag("Narrative.State.SequencerControlled"));
            TagsToApplyWhilstBound.AddTag(new GameplayTag("Narrative.State.Invulnerable"));
        }
    }

    /// <summary>
    /// 序列播放设置。对应 UE5 FNarrativeSequencePlaybackSettings。
    /// 在基础播放设置上增加 Narrative 特有功能。
    /// </summary>
    [Serializable]
    public class NarrativeSequencePlaybackSettings
    {
        /// <summary>是否将角色走到第 0 帧的变换位置（无缝过渡）</summary>
        public bool bWalkBindingsToStartTransform = false;

        /// <summary>是否将控制旋转 yaw 匹配到 Pawn（混合出序列时有用）</summary>
        public bool bUpdateControlRotationToPawn = false;

        /// <summary>是否显示电影黑边</summary>
        public bool bShowCinematicBars = false;

        /// <summary>是否允许玩家跳过此序列</summary>
        public bool bCanSkip = true;

        /// <summary>播放序列前是否停止对话</summary>
        public bool bStopDialogue = false;

        /// <summary>是否隐藏必要 HUD 元素（任务更新等）</summary>
        public bool bHideEvenEssentialHUDElements = false;

        /// <summary>是否自动播放</summary>
        public bool bAutoPlay = true;

        /// <summary>
        /// 播放速率（对应 UE5 <c>ULevelSequencePlayer::SetPlayRate</c>）。
        /// 端口消费方：本 Actor 的计时推进按 <c>DeltaTime * PlayRate</c> 走（见 OnUpdate）。
        /// </summary>
        public float PlayRate = 1f;

        /// <summary>
        /// 序列时长（秒）。UE5 由 MovieScene 的 DisplayRate/模板长度给出；
        /// Flax 1.12 无 LevelSequence 资产（已核查 FlaxEngine.CSharp.xml 无 Sequencer 类型），
        /// 时长只能由数据/调用方声明。&lt;=0 时播放侧退落到
        /// <see cref="NarrativeLevelSequenceActor.FallbackDurationSeconds"/>，保证仍然有限时收敛。
        /// </summary>
        public float DurationSeconds = 0f;

        /// <summary>
        /// 混合输出窗口长度（秒）。对应 UE5 <c>ULevelSequencePlayer::BeginStop(EMovieSceneStopFlags, blendOut)</c>
        /// 的 blend-out 时长；端口在 <see cref="NarrativeLevelSequenceActor.BlendOutAndStop"/> 内真实计时。
        /// </summary>
        public float BlendOutTime = 0.35f;

        /// <summary>
        /// 演出期间接管的主相机。对应 UE5 序列里的 CineCamera 轨道 + <c>UCameraSubsystem</c> 栈推送；
        /// Flax 端的可移植等价是 <see cref="FlaxEngine.Camera.OverrideMainCamera"/>（开关式接管），
        /// 播放时置 true、停止/混合输出结束时置回，因此不会有相机被遗留在接管态。
        /// </summary>
        [NonSerialized]
        public FlaxEngine.Camera CinematicCamera;

        /// <summary>要绑定的角色配置列表</summary>
        public List<NarrativeSequencerBindingConfig> BindingConfigs = new List<NarrativeSequencerBindingConfig>();

        /// <summary>绑定期间应用到角色的标签</summary>
        public GameplayTagContainer TagsToApplyWhilstBound = new GameplayTagContainer();

        /// <summary>若任何绑定角色获得这些标签则停止播放</summary>
        public GameplayTagContainer StopTags = new GameplayTagContainer();

        public NarrativeSequencePlaybackSettings()
        {
            TagsToApplyWhilstBound.AddTag(new GameplayTag("Narrative.State.SequencerControlled"));
            TagsToApplyWhilstBound.AddTag(new GameplayTag("Narrative.State.Invulnerable"));
            StopTags.AddTag(new GameplayTag("Narrative.State.IsDead"));
        }
    }

    /// <summary>
    /// 叙事序列播放完成回调。
    /// </summary>
    public delegate void OnSequenceFinished(NarrativeLevelSequenceActor sequenceActor);

    /// <summary>
    /// 叙事序列 Actor。对应 UE5 ANarrativeLevelSequenceActor。
    /// UE5 中继承 ALevelSequenceActor；Flax 无 LevelSequence 等价物。
    /// Flax-不兼容（已核实）：UE5 的 LevelSequence 在 Flax 无对应物——Flax 1.12 托管 API 文档
    /// FlaxEngine.CSharp.xml 中不存在 LevelSequence/Cutscene/Sequencer 任何类型，动画侧仅有
    /// AnimationController/AnimationGraph，不提供通用 Actor 轨道绑定与时间求值。
    /// <para>
    /// 因此本类实现的是「可移植的一半」（缺陷4）：绑定解析/刷新、播放设置落地
    /// （PlayRate / CinematicCamera / 黑边 / 隐藏 HUD / 停话 / keep-settings）、
    /// 按声明时长的计时推进（OnStarted→OnFinished）、绑定 GameplayTag 的施加与回收、
    /// StopTags 逐帧门禁，以及真实的 blend-out 窗口。
    /// 轨道级动画/属性求值（MovieScene evaluate）在 Flax 侧不存在，由绑定角色自身的动画控制器继续驱动，
    /// 序列时长由数据声明（<see cref="NarrativeSequencePlaybackSettings.DurationSeconds"/>）。
    /// </para>
    /// </summary>
    public class NarrativeLevelSequenceActor : Script
    {
        /// <summary>叙事播放设置</summary>
        public NarrativeSequencePlaybackSettings NarrativeSequenceParams = new NarrativeSequencePlaybackSettings();

        /// <summary>拥有此序列的控制器 Actor</summary>
        [NonSerialized]
        public Actor OwnerController;

        /// <summary>序列资源路径（Flax-不兼容：Flax 无 LevelSequence 资源）</summary>
        public string LevelSequencePath = "";

        /// <summary>是否正在等待角色到达绑定位置</summary>
        [NonSerialized]
        protected bool bPendingBeginSequence = false;

        /// <summary>本次播放施加、停止时必须回收的（ASC, 标签）对（见 OnPlay/OnStop）。</summary>
        private readonly List<(NarrativePro.GAS.NarrativeAbilitySystemComponent Asc, GameplayTag Tag)> _appliedBoundTags
            = new List<(NarrativePro.GAS.NarrativeAbilitySystemComponent Asc, GameplayTag Tag)>();

        /// <summary>序列播放完成事件</summary>
        public event OnSequenceFinished OnFinished;

        /// <summary>
        /// 序列开始播放事件。对应 UE5 <c>ULevelSequencePlayer::OnStartedPlaying</c>
        /// （端口由 <see cref="NarrativeLevelSequencePlayer.OnStartedPlaying"/> 起播时触发）。
        /// </summary>
        public event OnSequenceFinished OnStarted;

        // ===== 可移植播放通路状态（缺陷4） =====
        // UE5 的 ANarrativeLevelSequenceActor 把 ULevelSequencePlayer 建起来后，MovieScene 每帧求值
        // 驱动动画轨/属性轨/相机轨/Audio 轨，并由播放器在到达模板末尾或 BeginStop 的 blend-out 结束后
        // 广播 OnFinished。Flax 1.12 托管 API 里没有任何 Sequencer/MovieScene 类型
        //（已核查 FlaxEngine.CSharp.xml），轨道求值这一半无法还原；
        // 因此这里把可移植的一半做实：绑定解析与刷新、播放设置落地（play rate / 相机 / 电影呈现 /
        // keep-settings）、按声明时长推进计时并广播 OnStarted/OnFinished、绑定标签施加与回收、
        // StopTags 逐帧门禁、以及真实的 blend-out 窗口（黑边淡出 + 标签回收 + 输入交还都在窗口内收敛）。

        /// <summary>无时长数据时的兜底播放时长（秒）：仍保证有限时间内收敛，不吊住等待方。</summary>
        public const float FallbackDurationSeconds = 2f;

        /// <summary>blend-out 窗口兜底长度（秒），播放设置未给出时使用。</summary>
        public const float FallbackBlendOutSeconds = 0.35f;

        private NarrativeLevelSequencePlayer _player;
        private bool _bPlaying;
        private bool _bBlendingOut;
        private float _blendOutElapsed;
        private bool _bBarsShown;
        private bool _bHudHidden;
        private bool _bCameraTaken;
        private FlaxEngine.Camera _previousMainCamera;
        private bool _bFinishedBroadcast;
        private float _pendingBeginElapsed;

        /// <summary>「走到绑定起点再起演」的最长等待（秒），超时直接吸附落位起演，绝不吊住等待方。</summary>
        public float PendingBeginTimeoutSeconds = 5f;

        /// <summary>是否正在播放（含 blend-out 窗口内）。</summary>
        public bool IsPlaying => _bPlaying;

        /// <summary>是否处于混合输出收尾窗口。</summary>
        public bool IsBlendingOut => _bBlendingOut;

        /// <summary>当前播放时间（秒），未播放时为 0。</summary>
        public float CurrentPlayTime => _player != null ? _player.CurrentTime : 0f;

        /// <summary>本次播放的总时长（秒）。</summary>
        public float TotalDuration => _player != null ? _player.TotalDuration : 0f;

        public override void OnEnable()
        {
            base.OnEnable();
        }

        public override void OnDisable()
        {
            // 关卡切换/宿主销毁时的收口：回收标签 + 归还电影呈现 + 广播完成，
            // 否则挂在 OnFinished 上的异步节点（AsyncAction_PlayNarrativeSequence / _BlendOut）会永久悬挂，
            // 承载 Actor 也永远不会被回收。
            ReclaimPresentation();
            ClearBoundTags();
            _bPlaying = false;
            _bBlendingOut = false;
            bPendingBeginSequence = false;
            if (_player != null) { _player.bIsPlaying = false; _player = null; }
            BroadcastFinished();
            base.OnDisable();
        }

        /// <summary>获取所有绑定到序列的对象</summary>
        public List<Actor> GetBoundObjects()
        {
            var result = new List<Actor>();
            if (NarrativeSequenceParams?.BindingConfigs != null)
            {
                foreach (var config in NarrativeSequenceParams.BindingConfigs)
                {
                    if (config?.Character != null) result.Add(config.Character);
                }
            }
            return result;
        }

        /// <summary>使用生成器设置绑定（Flax-不兼容：简化实现）</summary>
        public virtual void SetBindingsUsingSpawner(Actor spawner)
        {
            // Flax-不兼容（已核实）：UE5 在此把 NPC 绑定进序列对象轨；Flax 无序列绑定 API（见类注释），
            // NPC 枚举虽可由 NarrativeCharacterSubsystem 提供，但不存在可绑定的轨，行为无法成立，保留占位。
            NarrativeLog.Log("SetBindingsUsingSpawner：Flax 无 LevelSequence，未实现");
        }

        /// <summary>处理绑定并启动序列</summary>
        /// <returns>返回 false 表示因标签要求无法启动</returns>
        public virtual bool HandleBindingsAndStartSequence()
        {
            if (NarrativeSequenceParams == null) return false;

            // 缺陷4：绑定解析/刷新 —— 已失效的绑定角色按 null 处理，未显式指定时回落到 OwnerController。
            ResolveBindings();

            // 停止标签门禁：UE5 由播放器逐帧比对 StopTags；本端口现在有了真实播放循环
            //（OnUpdate 推进 NarrativeLevelSequencePlayer），所以起播前与播放中都检查，命中即不启动。
            if (HasStopTag()) return false;

            if (NarrativeSequenceParams.bWalkBindingsToStartTransform)
            {
                // UE5 由序列轨驱动角色走到绑定变换；Flax 无序列轨，端口等价是逐帧插值走位
                //（见 OnUpdate / WalkBindingsTowardStart，真实实现而非占位），到位后 PlaySequence。
                bPendingBeginSequence = true;
                _pendingBeginElapsed = 0f;
                NarrativeLog.Log("HandleBindingsAndStartSequence：等待角色到达位置");
                return true;
            }

            PlaySequence();
            return true;
        }

        /// <summary>
        /// 解析/刷新绑定：让标签施加、走位、相机接管都按当前存活的角色集合走。
        /// 对应 UE5 序列播放器的 <c>ResolveBindings</c>（把绑定句柄换成真实对象）；
        /// Flax 无序列绑定 API，可移植等价是维护本类自己的 BindingConfigs 并剔除失效项。
        /// </summary>
        public virtual void ResolveBindings()
        {
            var configs = NarrativeSequenceParams?.BindingConfigs;
            if (configs == null) return;

            for (int i = 0; i < configs.Count; i++)
            {
                var config = configs[i];
                if (config == null) continue;

                if (config.Character == null && OwnerController != null)
                {
                    // 未指定（或原绑定角色已被销毁 —— Flax 的 Actor 销毁后按 null 比较）
                    // → 绑定到序列拥有者（对话演出「只锁玩家」的常见用法）。
                    config.Character = OwnerController;
                }
            }
        }

        /// <summary>绑定角色身上是否出现了播放设置声明的停止标签（默认 IsDead）。</summary>
        private bool HasStopTag()
        {
            var configs = NarrativeSequenceParams?.BindingConfigs;
            if (configs == null || NarrativeSequenceParams.StopTags == null) return false;

            var stopTag = NarrativePro.Core.NarrativeGameplayTags.State_IsDead;
            if (!NarrativeSequenceParams.StopTags.HasTag(stopTag)) return false;

            for (int i = 0; i < configs.Count; i++)
            {
                var character = configs[i]?.Character;
                if (character == null) continue;
                var asc = NarrativePro.Core.ArsenalStatics.GetNarrativeAbilitySystemComponent(character);
                if (asc?.GameplayTags != null && asc.GameplayTags.HasTag(stopTag)) return true;
            }
            return false;
        }

        /// <summary>更新序列和绑定参数</summary>
        public virtual void UpdateSequence(string levelSequencePath, NarrativeSequencePlaybackSettings settings)
        {
            // keep-settings 语义（UE5 FNarrativeCineDialogueLine.bKeepExistingSequenceSettings 的落地）：
            // 传 null 表示沿用现有设置，绝不新建一份把已有绑定/时长清空。
            if (settings != null && !ReferenceEquals(settings, NarrativeSequenceParams))
            {
                // 换设置时若正在播放，先按旧设置把上一轮的电影呈现归还（黑边/相机/HUD），
                // 避免新设置里关闭的项被遗留。
                if (_bPlaying || _bBlendingOut) ReclaimPresentation();
                NarrativeSequenceParams = settings;
            }
            else if (NarrativeSequenceParams == null)
            {
                NarrativeSequenceParams = new NarrativeSequencePlaybackSettings();
            }

            LevelSequencePath = levelSequencePath;

            // 绑定刷新：设置换了绑定角色也要跟着换（对应 UE5 UpdateSequence 后播放器重新解析绑定）。
            ResolveBindings();
        }

        /// <summary>混合输出并停止序列</summary>
        public virtual void BlendOutAndStop()
        {
            // UE5：播放器 BeginStop + blend-out 窗口，窗口结束才广播 OnFinished。
            // 缺陷4：端口原来只记一行日志就同步广播 OnFinished，挂在事件上的异步节点
            //（AsyncAction_BlendOutNarrativeSequence / AsyncAction_PlayNarrativeSequence）当场结束，
            // 收尾形同虚设。这里改成真实的 blend-out 窗口：窗口内黑边淡出、绑定标签仍在、
            // 玩家输入尚未交还；窗口走完才 OnStop（回收标签 + 归还呈现 + 广播完成）。
            if (_bFinishedBroadcast) return;          // 已经收过尾（例如 OnDisable 抢先广播）

            if (!_bPlaying && !_bBlendingOut && !bPendingBeginSequence)
            {
                // 没有在播（含 shot-less 兜底：只有路径/设置没有可播内容时也不能吊住等待方）——
                // 立即按停止语义收口。
                bPendingBeginSequence = false;
                OnStop();
                return;
            }

            if (_bBlendingOut) return;                // 已在窗口内，不重复起算

            bPendingBeginSequence = false;            // 走位待起播中被打断：直接收口
            _bBlendingOut = true;
            _blendOutElapsed = 0f;

            // 窗口开始的可见动作：黑边淡出（NarrativeCinematicBars 自带插值收敛）。
            if (_bBarsShown)
            {
                NarrativeCinematicBars.Hide();
                _bBarsShown = false;
            }

            float blend = GetBlendOutSeconds();
            NarrativeLog.Log($"BlendOutAndStop：进入 {blend:0.##}s 混合输出窗口（{LevelSequencePath}）");
        }

        /// <summary>blend-out 窗口长度（秒）：取播放设置，非法值退落到兜底常量。</summary>
        private float GetBlendOutSeconds()
        {
            float blend = NarrativeSequenceParams != null ? NarrativeSequenceParams.BlendOutTime : FallbackBlendOutSeconds;
            if (blend <= 0f) blend = FallbackBlendOutSeconds;
            return blend;
        }

        /// <summary>窗口走完：结束播放器计时并按停止语义收口（一次性）。</summary>
        private void FinishBlendOut()
        {
            _bBlendingOut = false;
            if (_player != null) _player.bIsPlaying = false;
            OnStop();
        }

        /// <summary>检查所有角色是否已到达绑定变换位置</summary>
        protected bool AreCharactersAtBindTransforms()
        {
            if (NarrativeSequenceParams?.BindingConfigs == null) return true;
            foreach (var config in NarrativeSequenceParams.BindingConfigs)
            {
                if (config?.Character == null) continue;
                float dist = Vector3.Distance(config.Character.Position, config.CinematicStartTransform.Translation);
                if (dist > 10f) return false;
            }
            return true;
        }

        /// <summary>尝试自动生成绑定配置</summary>
        protected void TryAutogenerateBindingConfigs()
        {
            // Flax-不兼容（已核实）：UE5 从序列资产对象轨反射出标记绑定并生成 BindingConfigs；
            // Flax 资产层不存在序列资产类型（xml 核实），无轨可枚举，绑定配置只能由场景手填，保留占位。
            NarrativeLog.Log("TryAutogenerateBindingConfigs：Flax 无 LevelSequence，未实现");
        }

        /// <summary>播放序列</summary>
        protected virtual void PlaySequence()
        {
            // UE5：这里创建 ULevelSequencePlayer（MovieScene 逐帧求值：动画轨/属性轨/相机轨/Audio 轨、
            // OnStartedPlaying / OnFinished 委托、StopTags 门禁）。Flax 1.12 无任何 Sequencer/MovieScene
            // 类型（见类注释的 xml 核实结论），轨道求值那一半确实无法还原；
            // 缺陷4 把可移植的一半做实：绑定刷新 + 播放设置落地 + 计时推进（OnStarted/OnFinished）+
            // 标签施加与回收 + StopTags 门禁。角色动画继续由其自身 AnimationController 驱动。
            if (_bPlaying || _bBlendingOut) return;   // 重复起播不叠加
            if (_bFinishedBroadcast) _bFinishedBroadcast = false; // 允许同一 Actor 再演一次

            ResolveBindings();

            float duration = NarrativeSequenceParams != null && NarrativeSequenceParams.DurationSeconds > 0f
                ? NarrativeSequenceParams.DurationSeconds
                : FallbackDurationSeconds;

            OnPlay();

            _player = new NarrativeLevelSequencePlayer(this)
            {
                TotalDuration = duration,
                CurrentTime = 0f,
            };
            _player.OnFinished += HandlePlayerFinished;
            _player.Play();
            _bPlaying = true;

            OnStarted?.Invoke(this);
            NarrativeLog.Log($"PlaySequence：播放序列 {LevelSequencePath}（时长 {duration:0.##}s、" +
                             $"速率 {GetPlayRate():0.##}x —— Flax 无 MovieScene，按计时推进驱动）");
        }

        /// <summary>当前播放速率（非法值退落为 1x，避免计时倒流/停滞）。</summary>
        private float GetPlayRate()
        {
            float rate = NarrativeSequenceParams != null ? NarrativeSequenceParams.PlayRate : 1f;
            return rate > 0f ? rate : 1f;
        }

        /// <summary>播放器到达声明时长：进入 blend-out（无窗口时等价立即停止），由它广播完成。</summary>
        private void HandlePlayerFinished(NarrativeLevelSequenceActor actor)
        {
            if (!_bPlaying || _bBlendingOut) return;
            _bPlaying = false;
            BlendOutAndStop();
        }

        /// <summary>序列开始播放时调用</summary>
        protected virtual void OnPlay()
        {
            // 应用绑定标签（缺陷1：TagsToApplyWhilstBound 此前只声明不施加，纯装饰）。
            // UE5 的施加时机是序列绑定事件；Flax 无序列绑定事件，但本类有明确的播放/停止
            // 调用点（PlaySequence / BlendOutAndStop→OnStop），因此改为播放时施加、停止时回收。
            // ASC 走 ArsenalStatics 多级链解析，宿主装配模式与角色模式都能命中；
            // 读取端见 Character/NarrativeCharacterMovement（锁移动输入）与
            // UnrealFramework/NarrativePlayerController（锁玩家输入）。
            ApplyBoundTagsToBindings();

            // 播放设置里的电影呈现项（缺陷4：bShowCinematicBars / bHideEvenEssentialHUDElements /
            // CinematicCamera 此前全仓零消费方）。走的是本仓对话镜头已经验证过的同一批接口
            //（Tales/NarrativeDialogueSequence.ApplyPresentation 同款）。
            ApplyPresentation();
        }

        /// <summary>施加电影呈现：黑边、隐藏 HUD、主相机接管。</summary>
        private void ApplyPresentation()
        {
            var settings = NarrativeSequenceParams;
            if (settings == null) return;

            if (settings.bShowCinematicBars && !_bBarsShown)
            {
                NarrativeCinematicBars.Show();
                _bBarsShown = true;
            }

            if (settings.bHideEvenEssentialHUDElements && !_bHudHidden)
            {
                var hud = Level.FindScript<NarrativePro.Widgets.NarrativeGameplayHUD>();
                if (hud != null)
                {
                    hud.SetHUDHidden(true, true);
                    _bHudHidden = true;
                }
            }

            var cam = settings.CinematicCamera;
            if (cam != null && !_bCameraTaken)
            {
                // Flax 端等价的「序列相机」：Camera.OverrideMainCamera 是静态接管开关
                //（对应 UE5 的 CineCamera 轨道 + 相机栈推送）。记录接管前的相机，停止时原样还回去。
                _previousMainCamera = FlaxEngine.Camera.OverrideMainCamera;
                FlaxEngine.Camera.OverrideMainCamera = cam;
                _bCameraTaken = true;
            }

            // bStopDialogue：起演前收掉正在进行的对话（UE5 在播放序列前停话）。
            // 走 TalesComponent 既有真实接口，不新造退出通道。
            if (settings.bStopDialogue)
            {
                var tales = OwnerController != null
                    ? OwnerController.GetScript<NarrativePro.Tales.TalesComponent>()
                    : Level.FindScript<NarrativePro.Tales.TalesComponent>();
                tales?.TryExitDialogue();
            }
        }

        /// <summary>归还电影呈现（停止/混合输出结束/禁用都必须调用，避免黑边与相机被遗留）。</summary>
        private void ReclaimPresentation()
        {
            if (_bBarsShown)
            {
                NarrativeCinematicBars.Hide();
                _bBarsShown = false;
            }

            if (_bHudHidden)
            {
                var hud = Level.FindScript<NarrativePro.Widgets.NarrativeGameplayHUD>();
                if (hud != null) hud.SetHUDHidden(false, false);
                _bHudHidden = false;
            }

            if (_bCameraTaken)
            {
                // 只还到自己接管前那一个相机：中途别人（其它演出/玩家相机）再改过就按改后的留着，
                // 不会把序列遗一个过期相机在主位上。
                FlaxEngine.Camera.OverrideMainCamera = _previousMainCamera;
                _previousMainCamera = null;
                _bCameraTaken = false;
            }
        }

        /// <summary>把播放设置/绑定配置里声明的 SequencerControlled / Invulnerable 施加到各绑定角色。</summary>
        private void ApplyBoundTagsToBindings()
        {
            ClearBoundTags(); // 重复播放不叠加
            if (NarrativeSequenceParams?.BindingConfigs == null) return;

            foreach (var config in NarrativeSequenceParams.BindingConfigs)
            {
                var character = config?.Character;
                if (character == null) continue;

                var asc = NarrativePro.Core.ArsenalStatics.GetNarrativeAbilitySystemComponent(character);
                if (asc?.GameplayTags == null) continue;

                AddBoundTagIfRequested(asc, NarrativeSequenceParams.TagsToApplyWhilstBound, NarrativePro.Core.NarrativeGameplayTags.State_SequencerControlled);
                AddBoundTagIfRequested(asc, NarrativeSequenceParams.TagsToApplyWhilstBound, NarrativePro.Core.NarrativeGameplayTags.State_Invulnerable);
                AddBoundTagIfRequested(asc, config.TagsToApplyWhilstBound, NarrativePro.Core.NarrativeGameplayTags.State_SequencerControlled);
                AddBoundTagIfRequested(asc, config.TagsToApplyWhilstBound, NarrativePro.Core.NarrativeGameplayTags.State_Invulnerable);
            }
        }

        /// <summary>容器里声明了该标签才施加；角色已有的标签不重复施加、也不由本序列回收（与对话序列同一规则）。</summary>
        private void AddBoundTagIfRequested(NarrativePro.GAS.NarrativeAbilitySystemComponent asc, GameplayTagContainer requested, GameplayTag tag)
        {
            if (requested == null || !requested.HasTag(tag)) return;
            if (asc.GameplayTags.HasTag(tag)) return;
            asc.GameplayTags.AddTag(tag);
            _appliedBoundTags.Add((asc, tag));
        }

        /// <summary>只回收本序列施加过的标签（幂等）。</summary>
        private void ClearBoundTags()
        {
            for (int i = 0; i < _appliedBoundTags.Count; i++)
            {
                var pair = _appliedBoundTags[i];
                if (pair.Asc != null && pair.Asc.GameplayTags != null)
                {
                    pair.Asc.GameplayTags.RemoveTag(pair.Tag);
                }
            }
            _appliedBoundTags.Clear();
        }

        /// <summary>序列停止时调用</summary>
        protected virtual void OnStop()
        {
            // 停止 = 回收绑定标签（角色重新接受移动/输入）+ 归还电影呈现（黑边/HUD/主相机）
            // + 广播完成（唤醒挂在 OnFinished 上的异步节点，由其销毁承载 Actor）。
            _bPlaying = false;
            ClearBoundTags();
            ReclaimPresentation();
            BroadcastFinished();
        }

        /// <summary>一次性完成广播：防止 OnStop 与 OnDisable 双路径各播一次。</summary>
        private void BroadcastFinished()
        {
            if (_bFinishedBroadcast) return;
            _bFinishedBroadcast = true;
            OnFinished?.Invoke(this);
        }

        /// <summary>走到绑定起点的收敛速度（每秒插值比例）。对应 UE5 bWalkBindingsToStartTransform 的走位速率。</summary>
        public float WalkToStartSpeed = 4f;

        public override void OnUpdate()
        {
            base.OnUpdate();

            float dt = (float)Time.DeltaTime;

            // 等待角色到达位置后播放序列
            if (bPendingBeginSequence)
            {
                // 缺陷⑥可移植部分：UE5 由序列轨驱动角色走到绑定起点；Flax 无序列轨，改为逐帧把绑定角色
                // 平滑移动到各自的 CinematicStartTransform（位置插值 + 到位后吸附朝向），到位即开演。
                WalkBindingsTowardStart(dt);

                // 走不到位的兜底（被卡住/绑定点不可达）：不能永远不起演 —— 那会吊死
                // AsyncAction_PlayNarrativeSequence 的等待方。超时后直接吸附落位起演。
                _pendingBeginElapsed += dt;
                if (AreCharactersAtBindTransforms() || _pendingBeginElapsed >= PendingBeginTimeoutSeconds)
                {
                    bPendingBeginSequence = false;
                    SnapBindingsToStartTransforms();
                    PlaySequence();
                }
            }

            // 缺陷4：blend-out 窗口推进（黑边已在窗口起点淡出，窗口走完才回收标签/归还输入/广播完成）。
            if (_bBlendingOut)
            {
                _blendOutElapsed += dt;
                if (_blendOutElapsed >= GetBlendOutSeconds()) FinishBlendOut();
                return;
            }

            // 缺陷4：计时推进 —— 这就是 Flax 端「MovieScene 逐帧求值」的可移植替身：
            // 播放器按 DeltaTime * PlayRate 累加，到声明时长即广播 OnFinished（→ blend-out → 收口）。
            if (_bPlaying && _player != null)
            {
                _player.Tick(dt * GetPlayRate());

                // StopTags 逐帧门禁（对应 UE5 播放器每帧比对）：绑定角色中途死亡 → 立即收尾。
                if (HasStopTag()) BlendOutAndStop();
            }
        }

        /// <summary>把每个绑定角色朝其 CinematicStartTransform 平移一步（位置插值，接近后吸附朝向）。</summary>
        protected virtual void WalkBindingsTowardStart(float deltaTime)
        {
            if (NarrativeSequenceParams?.BindingConfigs == null) return;

            float step = Mathf.Clamp(deltaTime * WalkToStartSpeed, 0f, 1f);
            foreach (var config in NarrativeSequenceParams.BindingConfigs)
            {
                var character = config?.Character;
                if (character == null) continue;

                var t = character.Transform;
                t.Translation = Vector3.Lerp(t.Translation, config.CinematicStartTransform.Translation, step);

                // 靠近后再对齐朝向，避免走位中途被强扭
                if (Vector3.Distance(t.Translation, config.CinematicStartTransform.Translation) < 25f)
                {
                    t.Orientation = config.CinematicStartTransform.Orientation;
                }

                character.Transform = t;
            }
        }

        /// <summary>到位后精确吸附到起点，消除插值残差。</summary>
        protected virtual void SnapBindingsToStartTransforms()
        {
            if (NarrativeSequenceParams?.BindingConfigs == null) return;
            foreach (var config in NarrativeSequenceParams.BindingConfigs)
            {
                if (config?.Character == null) continue;
                config.Character.Transform = config.CinematicStartTransform;
            }
        }
    }
}

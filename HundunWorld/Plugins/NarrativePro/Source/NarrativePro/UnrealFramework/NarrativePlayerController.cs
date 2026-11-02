using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.AI;
using NarrativePro.Camera;
using NarrativePro.Cinematics;
using NarrativePro.CommonUI;
using NarrativePro.Core;
using NarrativePro.GAS;
using NarrativePro.Interaction;
using NarrativePro.Items;
using NarrativePro.Navigation;
using NarrativePro.Tales;

namespace NarrativePro.UnrealFramework
{
    /// <summary>
    /// NPC 系留数据。对应 UE5 FNPCTether。
    /// 当 NPC 生成点被流式卸载时，可将 NPC 系留到玩家控制器，使其保留在场景中。
    /// </summary>
    [Serializable]
    public class NPCTether
    {
        /// <summary>当前已生成的 NPC 角色。</summary>
        [NonSerialized]
        public NarrativeNPCCharacter NPCCharacter;

        /// <summary>NPC 定义路径（替代 UE5 UNPCDefinition*）。</summary>
        public string NPCDefPath = "";

        /// <summary>NPC 定义对象（运行时用于系留重生，对应 UE5 FNPCTether::NPCDefinition）。</summary>
        [NonSerialized]
        public NPCDefinition Definition;

        /// <summary>NPC 的保存 GUID（用于恢复存档记录）。</summary>
        public string NPCSaveGUID = "";
    }

    /// <summary>
    /// 电影序列事件委托。对应 UE5 FOnCinematicEvent。
    /// </summary>
    /// <param name="sequenceActor">序列 Actor。</param>
    /// <param name="settings">播放设置。</param>
    public delegate void OnCinematicEventDelegate(NarrativeLevelSequenceActor sequenceActor, NarrativeSequencePlaybackSettings settings);

    /// <summary>
    /// Narrative 玩家控制器。对应 UE5 ANarrativePlayerController。
    /// UE5 中继承 APlayerController；Flax 无 PlayerController 基类，改为 Script。
    /// 通常挂载到玩家角色 Actor 或独立的控制器 Actor 上。
    /// 简化点：
    /// - 移除 UE5 复制/RPC（OnRep_PlayerState、NotifyDealtDamage 等），改为本地逻辑 + 事件回调
    /// - 移除 INarrativeSavableActor 接口（Flax-不兼容: UE5 INarrativeSavableActor 在 Flax 无对应物，保留占位）
    /// - 移除 UInputAction（Flax-已实现: 接入 Flax Input 系统）
    /// - TSubclassOf 转为字符串路径
    /// </summary>
    public class NarrativePlayerController : Script, INarrativeCharacterOwner
    {
        // ===== 配置字段 =====

        /// <summary>能力输入映射路径（替代 UE5 UNarrativeAbilityInputMapping*）。</summary>
        public string AbilityInputMappingPath = "";

        /// <summary>游戏 HUD 类路径（替代 UE5 TSubclassOf&lt;UNarrativeGameplayHUD&gt;）。</summary>
        public string GameplayHUDClassPath = "";

        /// <summary>注视输入 Action 路径。</summary>
        public string LookActionPath = "";

        // ===== 运行时引用 =====

        /// <summary>拥有的角色（即使当前未拥有也保留，避免车辆/坐骑场景丢失引用）。对应 UE5 OwnedCharacter。</summary>
        [NonSerialized]
        public NarrativePlayerCharacter OwnedCharacter;

        /// <summary>能力输入映射实例。</summary>
        [NonSerialized]
        protected NarrativeAbilityInputMapping AbilityInputMappings;

        /// <summary>游戏 HUD 实例（Flax-不兼容: UE5 AHUD 在 Flax 无对应物，使用 object 占位）。</summary>
        [NonSerialized]
        protected object GameplayHUD;

        /// <summary>玩家交互组件。</summary>
        [NonSerialized]
        protected PlayerInteractionComponent InteractionComponent;

        /// <summary>Tales 组件（对话/任务系统）。</summary>
        [NonSerialized]
        protected TalesComponent TalesComponent;

        /// <summary>导航组件。</summary>
        [NonSerialized]
        protected NarrativeNavigationComponent NavigationComponent;

        /// <summary>
        /// 宿主装配模式下的宿主 Actor（本游戏不实例化插件角色，运行期引导把
        /// ASC / 背包 / 交互 / Tales 组件挂在游戏自己的玩家宿主 Actor 上，控制器同样挂在它上面）。
        /// </summary>
        [NonSerialized]
        protected Actor _boundHostActor;

        /// <summary>宿主装配模式下缓存的 ASC（没有 OwnedCharacter 时的权威来源）。</summary>
        [NonSerialized]
        protected NarrativeAbilitySystemComponent _hostAbilitySystemComponent;

        /// <summary>宿主装配模式下宿主 Actor 上的玩家状态（本地玩家的阵营容器；角色模式下为 null）。</summary>
        protected NarrativePlayerState _hostPlayerState;

        // ===== NPC 系留 =====

        /// <summary>系留的 NPC 列表（SaveGame）。</summary>
        public List<NPCTether> NPCTethers = new List<NPCTether>();

        // ===== 电影序列 =====

        /// <summary>当前播放中的序列 Actor 列表。</summary>
        [NonSerialized]
        public List<NarrativeLevelSequenceActor> CurrentSequences = new List<NarrativeLevelSequenceActor>();

        /// <summary>序列播放开始事件。</summary>
        public event OnCinematicEventDelegate OnLevelSequencePlay;

        /// <summary>序列播放停止事件。</summary>
        public event OnCinematicEventDelegate OnLevelSequenceStop;

        // ===== 生命周期 =====

        public override void OnEnable()
        {
            base.OnEnable();

            // 查找子组件（这些组件现在由 NarrativeCharacter 的 OnEnable 显式构建，
            // 控制器与角色同 Actor 时可直接命中；独立控制器 Actor 上则为 null 并走按需创建）
            InteractionComponent = Actor.GetScript<PlayerInteractionComponent>();
            TalesComponent = Actor.GetScript<TalesComponent>();
            NavigationComponent = Actor.GetScript<NarrativeNavigationComponent>();

            // 如果挂载到玩家角色 Actor 上，自动获取 OwnedCharacter
            if (OwnedCharacter == null)
            {
                OwnedCharacter = Actor.GetScript<NarrativePlayerCharacter>();
                if (OwnedCharacter != null)
                {
                    OwnedCharacter.SetCachedPlayerController(this);

                    // 角色侧组件在独立控制器布局下缺失时，转发给角色自身的构建入口
                    InteractionComponent ??= OwnedCharacter.Actor?.GetScript<PlayerInteractionComponent>();
                    TalesComponent ??= OwnedCharacter.GetTalesComponent();
                    NavigationComponent ??= OwnedCharacter.Actor?.GetScript<NarrativeNavigationComponent>();
                }
            }

            // 宿主装配模式：本游戏不实例化插件角色，运行期引导把 ASC / 背包 / 交互 / Tales
            // 组件与控制器一起挂在游戏自己的玩家宿主 Actor 上（场景里名为 "Player" 的 Actor）。
            // 此时没有 OwnedCharacter，控制器必须自己采集同 Actor 的组件，并把该 Actor 记为宿主，
            // 否则 GetAbilitySystemComponent() 恒为 null → 能力输入/标签门禁/交互全部空转。
            if (OwnedCharacter == null)
            {
                _boundHostActor = Actor;
                InteractionComponent ??= Actor.GetScript<PlayerInteractionComponent>();
                TalesComponent ??= Actor.GetScript<TalesComponent>();
                NavigationComponent ??= Actor.GetScript<NarrativeNavigationComponent>();
                _hostAbilitySystemComponent ??= Actor.GetScript<NarrativeAbilitySystemComponent>();
            }

            // 订阅 CommonUI 激活栈：栈顶控件屏蔽游戏输入时，本控制器停止下发游戏输入
            NarrativeInputRouter.OnStackChanged += HandleUIStackChanged;
            RefreshGameInputBlockState();
        }

        public override void OnDisable()
        {
            NarrativeInputRouter.OnStackChanged -= HandleUIStackChanged;
            bGameInputBlockedByUI = false;
            base.OnDisable();
        }

        // ===== 游戏输入门禁（对应 UE5 CommonUI 的 GameInputBlocked → 输入组件停用）=====

        /// <summary>UI 激活栈当前是否屏蔽游戏输入（由 <see cref="NarrativeInputRouter"/> 的栈变化驱动）。</summary>
        public bool bGameInputBlockedByUI { get; private set; }

        /// <summary>激活栈变化回调：把路由器的阻塞位缓存到控制器，并通知角色清理残留输入。</summary>
        private void HandleUIStackChanged(NarrativeActivatableWidget topWidget)
        {
            RefreshGameInputBlockState();
        }

        /// <summary>
        /// 重新读取路由器的阻塞位。栈变化事件与逐帧刷新都走这里，
        /// 保证任何未被事件覆盖的路径（例如控件直接改 InputConfig）最终一致。
        /// </summary>
        public virtual void RefreshGameInputBlockState()
        {
            bool blocked = NarrativeInputRouter.bGameInputBlocked;
            if (blocked == bGameInputBlockedByUI) return;

            bGameInputBlockedByUI = blocked;

            var interactComp = GetInteractionComponent();
            if (blocked && interactComp != null && interactComp.bInteractHeld)
            {
                // 菜单态下 AbilityInputReleased 会被门禁吞掉，正在按住交互必须就地取消，
                // 否则 bInteractHeld 会永久停在 true（返回游戏后一次松手就能误触发交互）
                interactComp.EndInteract();
            }

            if (OwnedCharacter != null)
            {
                OwnedCharacter.ApplyGameInputBlock(blocked);
            }

            NarrativeLog.Log($"[NarrativePlayerController] 游戏输入门禁 = {(blocked ? "屏蔽（UI 菜单态）" : "放行")}");
        }

        /// <summary>游戏输入是否被屏蔽（UI 栈）。对应 UE5 <c>UCommonInputSubsystem::IsGameInputBlocked</c>。</summary>
        public bool IsGameInputBlocked() => bGameInputBlockedByUI;

        public override void OnUpdate()
        {
            // 每帧对账一次阻塞位（栈可能在事件之外被改写）
            RefreshGameInputBlockState();

            // 缺陷②：UE5 里 AutoManageActiveCameraTarget 由引擎 PlayerController::UpdateCameraManager 每帧调用，
            // Flax 无引擎回调，这里补上——把插件相机组件的跟随目标同步到当前受控角色/宿主，
            // 否则 SetCameraMode 后的 blend 根位置（GetCameraRootLocation）永远停在旧 TargetActor 上。
            AutoManageActiveCameraTarget(OwnedCharacter != null ? OwnedCharacter.Actor : _boundHostActor);

            // 缺陷③：RootScene 里摆放的 NarrativePlayerCameraManager 只在 InitializeFor 后 self-enable，
            // 而此前 InitializeFor 全仓零调用方。由控制器路径接线（幂等，场景无该对象时安全跳过）。
            EnsureSceneCameraManager();

            // Flax-已实现: 处理注视输入、相机管理（通过 Flax Input 系统）
            // 宿主装配模式下没有 OwnedCharacter，Look() 本就是空操作；此处不再轮询输入轴
            // （本游戏的转向由游戏自己的 PlayerController/ThirdPersonCamera 负责，
            //  且 "Look X"/"Look Y" 轴在玩家宿主场景里未必配置，逐帧读取只会产生缺失轴噪声）。
            if (OwnedCharacter != null && !IsLookInputIgnored() && !IsSequencerControlled())
            {
                Float2 lookValue = new Float2(Input.GetAxis("Look X"), Input.GetAxis("Look Y"));
                if (lookValue.LengthSquared > Mathf.Epsilon)
                {
                    Look(lookValue);
                }
            }

            // 能力输入：把键鼠边沿投递成 ASC 的 InputTag 按下/释放（UE5 里由 InputComponent 绑定完成）
            ProcessAbilityInput();

            // 快捷换武（背包里的武器 → 持握 → 授予其攻击能力）
            ProcessQuickWield();
        }

        // ===== INarrativeCharacterOwner 实现 =====

        /// <summary>返回与此控制器关联的 NarrativeCharacter（即使当前未拥有）。</summary>
        public virtual NarrativeCharacter GetNarrativeCharacter() => OwnedCharacter;

        // ===== IAbilitySystemInterface 等价 =====

        /// <summary>
        /// 获取能力系统组件。对应 UE5 GetAbilitySystemComponent。
        /// <para>
        /// 两条布局都支持：① 角色模式 —— ASC 由 <c>NarrativeCharacter.EnsureSubobjects</c> 建在受控角色上；
        /// ② 宿主装配模式 —— 引导把 ASC 直接 AddScript 到本地玩家宿主 Actor（本游戏没有插件角色），
        /// 此时 <see cref="OwnedCharacter"/> 为 null，必须回落到同 Actor 的 ASC，
        /// 否则能力输入（<see cref="AbilityInputPressed"/>/<see cref="AbilityInputReleased"/>）与标签门禁永远拿不到 ASC。
        /// </para>
        /// </summary>
        public virtual NarrativeAbilitySystemComponent GetAbilitySystemComponent()
        {
            var asc = OwnedCharacter != null ? OwnedCharacter.GetAbilitySystemComponent() : null;
            if (asc != null) return asc;

            if (_hostAbilitySystemComponent != null && _hostAbilitySystemComponent.Actor != null) return _hostAbilitySystemComponent;

            var source = Actor != null ? Actor : _boundHostActor;
            if (source == null) return null;

            _hostAbilitySystemComponent = source.GetScript<NarrativeAbilitySystemComponent>();
            return _hostAbilitySystemComponent;
        }

        /// <summary>
        /// 绑定本地玩家宿主 Actor（宿主装配模式）。由 <c>NarrativeRuntimeBootstrap</c> 在
        /// 把 Tales/ASC/背包/交互组件 AddScript 到宿主之后调用；没有插件角色时用它替代 <see cref="OnPossess"/>。
        /// </summary>
        /// <param name="host">承载插件组件的场景 Actor（本游戏为名为 "Player" 的 Actor）。</param>
        public virtual void BindHostActor(Actor host)
        {
            if (host == null) return;

            _boundHostActor = host;
            _hostAbilitySystemComponent = host.GetScript<NarrativeAbilitySystemComponent>();
            _hostPlayerState = host.GetScript<NarrativePlayerState>();
            InteractionComponent ??= host.GetScript<PlayerInteractionComponent>();
            TalesComponent ??= host.GetScript<TalesComponent>();
            NavigationComponent ??= host.GetScript<NarrativeNavigationComponent>();
            RefreshGameInputBlockState();
        }

        /// <summary>宿主装配模式绑定的宿主 Actor（角色模式下为 null）。</summary>
        public Actor GetBoundHostActor() => _boundHostActor;

        // ===== IGameplayTagAssetInterface 等价 =====

        /// <summary>获取拥有的所有 GameplayTag（角色优先，宿主装配模式回落到宿主 ASC）。</summary>
        public virtual void GetOwnedGameplayTags(GameplayTagContainer tagContainer)
        {
            if (OwnedCharacter != null)
            {
                OwnedCharacter.GetOwnedGameplayTags(tagContainer);
                return;
            }
            GetAbilitySystemComponent()?.GetOwnedGameplayTags(tagContainer);
        }

        /// <summary>是否拥有指定标签。</summary>
        public virtual bool HasMatchingGameplayTag(GameplayTag tagToCheck)
        {
            if (OwnedCharacter != null) return OwnedCharacter.HasMatchingGameplayTag(tagToCheck);
            var asc = GetAbilitySystemComponent();
            return asc != null && asc.HasMatchingGameplayTag(tagToCheck);
        }

        /// <summary>是否拥有所有指定标签。</summary>
        public virtual bool HasAllMatchingGameplayTags(GameplayTagContainer tagsToCheck)
        {
            if (OwnedCharacter != null) return OwnedCharacter.HasAllMatchingGameplayTags(tagsToCheck);
            var asc = GetAbilitySystemComponent();
            return asc != null && asc.HasAllMatchingGameplayTags(tagsToCheck);
        }

        /// <summary>是否拥有任意指定标签。</summary>
        public virtual bool HasAnyMatchingGameplayTags(GameplayTagContainer tagsToCheck)
        {
            if (OwnedCharacter != null) return OwnedCharacter.HasAnyMatchingGameplayTags(tagsToCheck);
            var asc = GetAbilitySystemComponent();
            return asc != null && asc.HasAnyMatchingGameplayTags(tagsToCheck);
        }

        // ===== INarrativeTeamAgentInterface 等价 =====

        /// <summary>获取阵营（委托给 PlayerState，对应 UE5 GetFactions 委托 PlayerState）。</summary>
        public virtual GameplayTagContainer GetFactions()
        {
            var state = GetResolvedPlayerState();
            return state != null ? state.GetFactions() : new GameplayTagContainer();
        }

        /// <summary>添加阵营（委托给 PlayerState）。</summary>
        public virtual void AddFaction(GameplayTag faction)
        {
            // Flax-已实现: 委托 PlayerState.AddFaction（对应 UE5 PlayerState->AddFaction）
            GetResolvedPlayerState()?.AddFaction(faction);
        }

        /// <summary>移除阵营（委托给 PlayerState）。</summary>
        public virtual void RemoveFaction(GameplayTag faction)
        {
            // Flax-已实现: 委托 PlayerState.RemoveFaction（对应 UE5 PlayerState->RemoveFaction）
            GetResolvedPlayerState()?.RemoveFaction(faction);
        }

        /// <summary>
        /// 解析控制器对应的玩家状态：角色模式取角色的 <c>NarrativePlayerState</c>；
        /// 宿主装配模式（<see cref="OwnedCharacter"/> 为 null）回落到宿主 Actor 上的玩家状态
        /// ——那是引导在 <c>AttachHostComponents</c> 里补挂的阵营容器（UE5 由 PlayerState 持有阵营）。
        /// 没有它时宿主玩家的 <see cref="GetFactions"/> 恒空、<see cref="AddFaction"/> 无处可写。
        /// </summary>
        public NarrativePlayerState GetResolvedPlayerState()
        {
            var state = OwnedCharacter != null ? OwnedCharacter.GetNarrativePlayerState() : null;
            if (state != null) return state;

            if (_hostPlayerState != null && _hostPlayerState.Actor != null) return _hostPlayerState;

            var source = Actor != null ? Actor : _boundHostActor;
            _hostPlayerState = source != null ? source.GetScript<NarrativePlayerState>() : null;
            return _hostPlayerState;
        }

        /// <summary>
        /// 解析任意 Actor 的阵营容器：角色优先取其定义 <c>DefaultFactions</c>（既有角色行为保持不变）；
        /// 其次宿主玩家 Actor 上的 <see cref="NarrativePlayerState"/>（宿主装配模式下没有角色定义）；
        /// 最后该 Actor 上的 <see cref="NarrativePlayerController"/>。
        /// 供 <see cref="GetTeamAttitudeTowards"/> 与 <c>NarrativeNPCCharacter</c> 的态度查询共用，
        /// 让本地玩家与 NPC 角色读同一份阵营容器。
        /// </summary>
        public static GameplayTagContainer ResolveActorFactions(Actor actor)
        {
            if (actor == null) return new GameplayTagContainer();

            var defFactions = actor.GetScript<NarrativeCharacter>()?.GetCharacterDefinition()?.DefaultFactions;
            if (defFactions != null && defFactions.Count > 0) return defFactions;

            var state = actor.GetScript<NarrativePlayerState>();
            if (state != null)
            {
                var f = state.GetFactions();
                if (f != null && f.Count > 0) return f;
            }

            var controller = actor.GetScript<NarrativePlayerController>();
            if (controller != null)
            {
                var f = controller.GetFactions();
                if (f != null && f.Count > 0) return f;
            }

            return defFactions ?? new GameplayTagContainer();
        }

        /// <summary>获取朝向其他 Actor 的态度。对应 UE5 GetTeamAttitudeTowards。</summary>
        /// <returns>0=友好, 1=中立, 2=敌对。</returns>
        public virtual byte GetTeamAttitudeTowards(Actor other)
        {
            if (other == null) return 1; // 中立
            // Flax-已实现: 通过 NarrativeGameState 阵营联盟图查询（对应 UE5 GetWorldState()->GetTeamAttitudeTowards）
            var gameState = NarrativeGameState.GetCurrent();
            if (gameState == null) return 1; // 中立
            // 目标阵营走共用解析器：角色用定义 DefaultFactions，宿主玩家用其 NarrativePlayerState
            // （此前只读 NarrativeCharacter 定义，宿主玩家没有角色 → 阵营恒空 → 被视为一方有阵营的敌对目标）。
            var targetFactions = ResolveActorFactions(other);
            var attitude = gameState.GetFactionsAttitudeTowardsFactions(GetFactions(), targetFactions);
            return (byte)attitude; // ETeamAttitude: Friendly=0/Neutral=1/Hostile=2，与返回值口径一致
        }

        // ===== 输入设备 =====

        /// <summary>获取输入设备名（键盘/手柄等）。对应 UE5 GetNarrativeInputDeviceName。</summary>
        public virtual string GetNarrativeInputDeviceName()
        {
            // Flax-已实现: 通过 Flax Input 系统查询当前输入设备
            if (Input.GamepadsCount > 0) return "Gamepad";
            return "Keyboard";
        }

        /// <summary>是否正在使用手柄。对应 UE5 IsUsingGamepad。</summary>
        public virtual bool IsUsingGamepad()
        {
            // Flax-已实现: 通过 Flax Input 检测手柄
            return Input.GamepadsCount > 0;
        }

        /// <summary>是否处于电影模式。对应 UE5 bCinematicMode。</summary>
        public bool bCinematicMode { get; protected set; }

        /// <summary>电影模式期间是否忽略注视输入。对应 UE5 PlayerInput->IgnoreLookInput。</summary>
        protected bool bLookInputIgnoredByCinematic;

        /// <summary>重写：是否忽略注视输入。对应 UE5 IsLookInputIgnored。</summary>
        public virtual bool IsLookInputIgnored() => bLookInputIgnoredByCinematic || bGameInputBlockedByUI;

        /// <summary>设置电影模式。对应 UE5 SetCinematicMode。</summary>
        public virtual void SetCinematicMode(bool bInCinematicMode, bool bHidePlayer, bool bAffectsHUD, bool bAffectsMovement, bool bAffectsTurning)
        {
            // Flax-已实现: 电影模式下禁用转向（注视输入忽略）与移动（角色脚本 Enabled 开关）
            bCinematicMode = bInCinematicMode;
            bLookInputIgnoredByCinematic = bInCinematicMode && bAffectsTurning;
            if (bAffectsMovement && OwnedCharacter != null)
            {
                OwnedCharacter.Enabled = !bInCinematicMode;
            }
            if (bHidePlayer && OwnedCharacter?.Actor != null)
            {
                // Flax-已实现: 走 NarrativeCharacterVisual（角色外观装配）+ 宿主直挂 AnimatedModel 两条路径，
                // 关/开角色可见 Actor 即隐藏/恢复玩家外观（UE5 SetActorHiddenInGame 的等价物）。
                SetPlayerVisualHidden(bInCinematicMode);
            }
            if (bAffectsHUD)
            {
                // Flax-已实现: HUD 端口已有真实显隐接口（Widgets/NarrativeGameplayHUD.SetHUDHidden），
                // 过场期间整盘隐藏但保留 EssentialWidgets（对应 UE5 SetHUDHidden 语义）。
                var hud = GameplayHUD as NarrativePro.Widgets.NarrativeGameplayHUD;
                hud?.SetHUDHidden(bInCinematicMode, false);
            }
            NarrativeLog.Log($"[NarrativePlayerController] SetCinematicMode = {bInCinematicMode}");
        }

        /// <summary>
        /// 隐藏/恢复玩家外观（对应 UE5 PlayerController 的 bHidePlayer 分支 → SetActorHiddenInGame）。
        /// 优先操作 <see cref="NarrativePro.Character.NarrativeCharacterVisual"/> 装配的视觉根与网格组件；
        /// 宿主直挂模式（无外观脚本）兜底直接开关宿主子树里的 AnimatedModel。
        /// Flax 侧 Actor 显隐 API 是 <c>Actor.IsActive</c> 可写属性（同 Weapons/WeaponVisual.cs:522 既有用法）。
        /// </summary>
        private void SetPlayerVisualHidden(bool bHidden)
        {
            var host = OwnedCharacter != null ? OwnedCharacter.Actor : null;
            if (host == null) return;

            var visible = !bHidden;
            var visual = host.GetScript<NarrativePro.Character.NarrativeCharacterVisual>();
            if (visual != null)
            {
                if (visual.CharacterVisualRoot != null)
                {
                    visual.CharacterVisualRoot.IsActive = visible;
                }
                if (visual.MeshComponents != null)
                {
                    for (int i = 0; i < visual.MeshComponents.Count; i++)
                    {
                        var comp = visual.MeshComponents[i] != null ? visual.MeshComponents[i].Component : null;
                        if (comp != null) comp.IsActive = visible;
                    }
                }
                return;
            }

            var model = host.FindActor<AnimatedModel>();
            if (model != null) model.IsActive = visible;
        }

        /// <summary>自动管理相机目标。对应 UE5 AutoManageActiveCameraTarget。</summary>
        protected virtual void AutoManageActiveCameraTarget(Actor suggestedTarget)
        {
            // Flax-已实现: 把相机组件的跟随目标切换到建议目标（默认回到受控角色）
            var target = suggestedTarget != null ? suggestedTarget : OwnedCharacter?.Actor;
            if (target == null) return;
            var camera = Actor != null ? Actor.GetScript<NarrativeCameraComponent>() : null;
            camera ??= target.GetScript<NarrativeCameraComponent>();
            if (camera != null)
            {
                camera.TargetActor = target;
            }
        }

        // ===== 场景相机管理器接线（缺陷③） =====

        [NonSerialized] private NarrativePlayerCameraManager _sceneCameraManager;
        private bool _sceneCameraManagerResolved;
        private float _sceneCameraManagerResolveTimer;
        private int _sceneCameraManagerResolveAttempts;
        private bool _sceneCameraManagerResolveGaveUp;

        /// <summary>相机管理器解析的最大重试次数（缺陷③：此前场景无管理器时每秒一次全场景 FindScript 永久重试）。</summary>
        private const int MaxSceneCameraManagerResolveAttempts = 10;

        /// <summary>解析插件相机组件：控制器同 Actor → 角色 → 宿主 → 全场景。</summary>
        private NarrativeCameraComponent ResolvePluginCameraComponent()
        {
            var cam = Actor != null ? Actor.GetScript<NarrativeCameraComponent>() : null;
            cam ??= OwnedCharacter?.Actor?.GetScript<NarrativeCameraComponent>();
            cam ??= _boundHostActor != null ? _boundHostActor.GetScript<NarrativeCameraComponent>() : null;
            cam ??= Level.FindScript<NarrativeCameraComponent>();
            return cam;
        }

        /// <summary>
        /// 幂等地找到 RootScene 里的 <see cref="NarrativePlayerCameraManager"/> 并绑定控制器。
        /// 场景里没有该对象时按 ~1 秒节流做<b>有界</b>重试（最多 <see cref="MaxSceneCameraManagerResolveAttempts"/> 次），
        /// 用尽后静默放弃（缺陷③：此前未命中会永久逐秒全场景扫描）；
        /// 已绑定的管理器随 Actor 销毁失效时，计数复位并重新解析。
        /// </summary>
        private void EnsureSceneCameraManager()
        {
            if (_sceneCameraManagerResolved)
            {
                // Actor 被销毁后引用会失效，重新解析（并把放弃标记复位，允许再试一轮）
                if (_sceneCameraManager == null || _sceneCameraManager.Actor == null)
                {
                    _sceneCameraManagerResolved = false;
                    _sceneCameraManager = null;
                    _sceneCameraManagerResolveAttempts = 0;
                    _sceneCameraManagerResolveGaveUp = false;
                    return;
                }

                UpdateCameraManagerOwnership();
                return;
            }

            if (_sceneCameraManagerResolveGaveUp) return; // 已确认场景中没有该对象：不再扫描

            _sceneCameraManagerResolveTimer -= Time.DeltaTime;
            if (_sceneCameraManagerResolveTimer > 0f) return;
            _sceneCameraManagerResolveTimer = 1f;

            var mgr = Level.FindScript<NarrativePlayerCameraManager>();
            if (mgr == null)
            {
                if (++_sceneCameraManagerResolveAttempts >= MaxSceneCameraManagerResolveAttempts)
                {
                    _sceneCameraManagerResolveGaveUp = true; // 静默停止：本场景没有插件相机管理器，属正常配置
                }
                return;
            }

            _sceneCameraManager = mgr;
            _sceneCameraManagerResolved = true;
            mgr.InitializeFor(Actor);  // 缺陷③的接线点：此前 InitializeFor 全仓零调用方
        }

        /// <summary>
        /// 依据当前相机归属决定管理器是否接管相机朝向。
        /// 单一谓词（缺陷②）：接管条件 = 「插件角色模式 + 插件相机有激活模式」（既有行为，不回归）
        /// OR 「相机组件被宿主显式声明了输出所有权」（<see cref="NarrativeCameraComponent.ClaimCameraOwnership"/>）。
        /// 宿主装配模式（本游戏运行态）两个条件都不成立：引导补挂的 NarrativeCameraComponent 默认
        /// bDrivesCameraOutput=false，游戏 ThirdPersonCamera 继续独占相机，管理器不接管朝向。
        /// </summary>
        private void UpdateCameraManagerOwnership()
        {
            if (_sceneCameraManager == null) return;

            var cam = ResolvePluginCameraComponent();
            bool characterMode = OwnedCharacter != null && cam != null && cam.CurrentCameraMode != null;
            bool pluginOwnsCamera = characterMode || (cam != null && cam.bDrivesCameraOutput);

            _sceneCameraManager.OwningCharacter = OwnedCharacter != null ? OwnedCharacter.Actor : null;
            _sceneCameraManager.SetDrivesCameraOrientation(pluginOwnsCamera, cam != null ? cam.CameraActor : null);
        }

        // ===== 拥有/控制角色 =====

        /// <summary>返回拥有的 Narrative 玩家角色（即使当前未拥有）。对应 UE5 GetOwnedCharacter。</summary>
        public virtual NarrativePlayerCharacter GetOwnedCharacter() => OwnedCharacter;

        /// <summary>返回当前控制的 Narrative 玩家角色。对应 UE5 GetControlledCharacter。</summary>
        public virtual NarrativePlayerCharacter GetControlledCharacter()
        {
            // 简化版：与 OwnedCharacter 相同
            return OwnedCharacter;
        }

        // ===== 快速旅行 =====

        /// <summary>快速旅行到 POI。对应 UE5 FastTravelToPOI（BlueprintImplementableEvent）。</summary>
        /// <param name="poi">目标 POI 数据。</param>
        /// <returns>是否成功完成传送。</returns>
        public virtual bool FastTravelToPOI(POIData poi)
        {
            if (poi == null) return false;

            // 引导（NarrativeRuntimeBootstrap.AttachHostComponents）已把导航组件补挂到宿主 Actor，
            // 与控制器同 Actor；再兜底用 OnEnable/BindHost 阶段缓存的引用，避免装配时序竞态。
            var navigation = Actor != null ? Actor.GetScript<NarrativeNavigationComponent>() : null;
            navigation ??= NavigationComponent;
            if (navigation == null)
            {
                NarrativeLog.LogWarning($"[NarrativePlayerController] FastTravelToPOI: 本控制器上未挂载 NarrativeNavigationComponent，无法传送（{poi.POITag}）");
                return false;
            }

            return navigation.RequestFastTravel(poi);
        }

        // ===== 伤害通知 =====

        /// <summary>通知造成了伤害的本地等价。对应 UE5 NotifyDealtDamage（Client RPC）。</summary>
        public virtual void NotifyDealtDamage(Actor damagedActor, float damageAmount)
        {
            HandleDamageActor(damagedActor, damageAmount);
        }

        /// <summary>处理伤害目标（如显示伤害数字）。对应 UE5 HandleDamageActor（BlueprintImplementableEvent）。</summary>
        public virtual void HandleDamageActor(Actor damagedActor, float damageAmount)
        {
            NarrativeLog.Log($"[NarrativePlayerController] 造成伤害: {damagedActor?.Name} -{damageAmount}");

            // Flax-已实现: UE5 由 BP HUD 控件实现伤害数字；端口把这条路径接到真实 HUD
            // （Widgets/NarrativeGameplayHUD.ShowNotification），使伤害反馈在运行时有落点。
            var hud = GameplayHUD as NarrativePro.Widgets.NarrativeGameplayHUD;
            hud?.ShowNotification($"{damagedActor?.Name ?? "目标"} -{damageAmount:0.##}", 2f);
        }

        // ===== 能力输入 =====

        /// <summary>
        /// 一条"真实按键 → 能力输入标签"的绑定。
        /// 对应 UE5 在 <c>SetupPlayerInputComponent</c> 里把 InputAction 绑到
        /// <c>AbilityInputPressed/Released</c>；Flax 侧没有 InputAction 资产，
        /// 因此这里直接用键鼠原语（必要时可改成命名输入动作 <see cref="ActionName"/>）。
        /// </summary>
        [Serializable]
        public class AbilityInputBinding
        {
            /// <summary>能力输入标签全名（如 "Narrative.Input.Attack"）。</summary>
            public string InputTagName = "";

            /// <summary>键盘键（<see cref="KeyboardKeys.None"/> 表示不占键盘）。</summary>
            public KeyboardKeys Key = KeyboardKeys.None;

            /// <summary>鼠标键（<see cref="MouseButton.None"/> 表示不占鼠标）。</summary>
            public MouseButton MouseButton = MouseButton.None;

            /// <summary>
            /// 命名输入动作名（Flax 工程输入设置里的 Action）。非空时优先用它，
            /// 走 <c>Input.GetActionState</c>，这样改键配置能直接生效。
            /// </summary>
            public string ActionName = "";
        }

        /// <summary>能力输入绑定表（默认覆盖攻击/重击/装填/技能位）。</summary>
        public List<AbilityInputBinding> AbilityInputBindings = new List<AbilityInputBinding>();

        /// <summary>快捷换武的物品类 ID 列表（按 <see cref="CycleWeaponKey"/> 循环持握）。</summary>
        public List<string> QuickWieldItemClassIds = new List<string> { "item_sword", "item_bow" };

        /// <summary>快捷换武按键。</summary>
        public KeyboardKeys CycleWeaponKey = KeyboardKeys.Q;

        /// <summary>
        /// 交互键（默认 <see cref="KeyboardKeys.T"/>）。
        /// 对应 UE5 在 <c>SetupPlayerInputComponent</c> 里把 <c>IA_Interact</c> 绑到
        /// <c>PlayerInteractionComponent::BeginInteract</c> / <c>EndInteract</c> ——
        /// 这条绑定是 <c>bInteractHeld</c> 的唯一写入方，缺它整套交互层（NPC 对话/交易/拾取）都不可达。
        /// <para>
        /// 为什么不是 UE5 惯例的 E：<c>Source/Game/ThirdPersonCamera.cs:1685</c> 把 E 用作
        /// 锁敌目标循环（键码全量普查：<c>grep -rhoE "Keys\.[A-Za-z0-9_]+" --include=*.cs Source/Game
        /// Plugins/NarrativePro/Source | sort | uniq -c | sort -rn</c>，E 在宿主工程出现 4 次、
        /// 本插件绑定表占用 Q/R/F/G/H，Tab 另有世界选层占用史），E 与宿主冲突。
        /// T 在宿主与插件的普查直方图中均为 0 次命中（唯一含 T 的 Token 是
        /// <c>GameConfigurationService.cs:468</c> 的显示映射，非按键绑定），且贴近 WASD 手位，
        /// 故默认取 T。改键无需改码：<c>InteractKey</c> 为 public 字段，关卡蓝图/宿主可在
        /// <c>OnBeginPlay</c> 前改写它或 <see cref="AbilityInputBindings"/>；
        /// 若宿主将来把 T 也占了，同样只改这个字段即可（插件侧不抢键）。
        /// </para>
        /// </summary>
        public KeyboardKeys InteractKey = KeyboardKeys.T;

        /// <summary>
        /// 上车按键。UE5 由载具蓝图上的 Interactable 槽位承担，Flax 侧槽位由交通模拟自己生成，
        /// 因此这里只要一个入口调用（<c>VehicleTrafficSimulation.TryMountLocalPlayerNearestVehicle</c>）。
        /// 键位普查同 InteractKey：V 在宿主工程与本插件均为 0 次命中（仅 G/H 被技能位占用）。
        /// </summary>
        /// <summary>
        /// 上车按键。键位普查同 <see cref="InteractKey"/>：<c>V</c> 已被宿主工程占用
        /// （<c>Source/Game/InputConfigurationManager.cs:165</c> 把 SwitchCameraMode 绑到 KeyboardKeys.V，
        /// 普查命令：<c>grep -rhoE "(KeyboardKeys|Keys)\.[A-Z]\b" --include=*.cs Source/Game Plugins/NarrativePro/Source | sort | uniq -c</c>），
        /// 故取零命中的 <c>M</c>（Mount 助记）。改键只需改这个字段，插件侧不抢键。
        /// </summary>
        public KeyboardKeys MountKey = KeyboardKeys.M;

        /// <summary>上车键的上一帧状态（边沿检测，避免按住连发）。</summary>
        private bool _mountKeyWasDown;

        /// <summary>按住 V 尝试上最近的空位载具；无载具时静默（这不是错误状态）。</summary>
        protected virtual void TickMountInput()
        {
            if (Actor == null) return;
            // 只有本地玩家才采集按键：宿主装配模式下没有 OwnedCharacter，此时本控制器即本地玩家控制器。
            if (OwnedCharacter != null && !OwnedCharacter.IsLocallyControlled()) return;
            // 缺陷1读取端：序列受控期间不接受上车输入（UE5 序列绑定 Pawn 的输入锁）
            if (IsSequencerControlled()) return;

            bool down = false;
            try
            {
                down = Input.GetKey(MountKey);
            }
            catch
            {
                return;
            }

            if (down && !_mountKeyWasDown)
            {
                NarrativePro.Vehicles.VehicleTrafficSimulation.TryMountLocalPlayerNearestVehicle();
            }

            _mountKeyWasDown = down;
        }

        /// <summary>
        /// 进/出掩体键。对应 UE5 的 <c>IA_Cover</c>：上游这条动作连在蓝图输入资产上，
        /// C++ 侧 <c>TryEnterCover</c>（NarrativeCharacterMovement.cpp:2441）没有调用方，
        /// 所以端口缺的正是这条"作者/玩家入口"——补在这里，移动侧的进/出与贴墙积分不动。
        /// 键位普查（<c>grep -rhoE "KeyboardKeys\.[A-Za-z0-9_]+" --include=*.cs Source/Game Plugins/NarrativePro/Source | sort -u</c>）
        /// 里 J/K/L/P/Y 全零命中，取 L；改键只改本字段，插件不抢宿主已用的键。
        /// </summary>
        public KeyboardKeys CoverKey = KeyboardKeys.L;

        /// <summary>掩体键的上一帧状态（边沿检测，按住不连发）。</summary>
        private bool _coverKeyWasDown;

        /// <summary>按一次进掩体、再按一次离开（对称出口是 <c>InvalidateCover()</c>，它会广播 OnExitCover）。</summary>
        protected virtual void TickCoverInput()
        {
            if (Actor == null) return;
            if (OwnedCharacter != null && !OwnedCharacter.IsLocallyControlled()) return;
            if (IsSequencerControlled()) return;

            bool down = false;
            try
            {
                down = Input.GetKey(CoverKey);
            }
            catch
            {
                return;
            }

            if (down && !_coverKeyWasDown)
            {
                var pawn = OwnedCharacter != null ? OwnedCharacter.Actor : null;
                var movement = pawn != null ? pawn.GetScript<NarrativePro.Character.NarrativeCharacterMovement>() : null;
                if (movement != null)
                {
                    if (movement.HasCover()) movement.InvalidateCover();
                    else movement.TryEnterCover();
                }
            }

            _coverKeyWasDown = down;
        }

        /// <summary>
        /// 下车键（默认 <see cref="KeyboardKeys.X"/>；缺陷2：此前只有上车键，载具能上不能下）。
        /// 键位普查同 InteractKey（grep -rhoE "Keys\.[A-Za-z0-9_]+" --include=*.cs Source/Game
        /// Plugins/NarrativePro/Source | sort | uniq -c）：X 在宿主工程与本插件均 0 命中；
        /// Z/C 是宿主 UI 子页签键、C 还是宿主蹲伏键、B/I 是宿主背包入口、V/Q/W/A/S/D/F/R/T/G/H
        /// 已被宿主或本插件占用，故取 X。
        /// </summary>
        public KeyboardKeys DismountKey = KeyboardKeys.X;

        /// <summary>下车键的上一帧状态（边沿检测）。</summary>
        private bool _dismountKeyWasDown;

        /// <summary>
        /// 交易键（默认 <see cref="KeyboardKeys.U"/>，键位普查 0 命中）。注视「可交易 NPC」时：
        /// 首次按下 = <c>NPCInteractable.BeginTrade</c>（HUD 经 OnTradeBegin 列出库存），
        /// 会话内后续按下 = 沿库存列表购买下一件；按住 Shift 再按 = 卖出身上第一件（经 TradeItem 结算）。
        /// <para>
        /// 游戏侧要走自己的商店页时唯一需要调用的一行是：
        /// <c>npcInteractable.OnTradeBegin += (interactor, vendorInventory) => 打开商店UI(vendorInventory);</c>
        /// （插件内消费者见 Widgets/NarrativeGameplayHUD.HandleTradeBegin；买卖结算走
        ///  NPCInteractable.BeginTrade(buyerActor) / TradeItem(counterparty, classId, qty, bBuyFromVendor, out err)）。
        /// </para>
        /// </summary>
        public KeyboardKeys TradeKey = KeyboardKeys.U;

        private bool _tradeKeyWasDown;
        private NarrativePro.AI.NPCInteractable _tradeSession;
        private readonly List<NarrativePro.Items.NarrativeItem> _tradeStock = new List<NarrativePro.Items.NarrativeItem>();
        private int _tradeCursor;
        private float _tradeSessionExpire;

        /// <summary>交易会话保活时长（秒）：每次按键都会续期，超时/目标销毁自动关单。</summary>
        private const float TradeSessionSeconds = 30f;

        /// <summary>
        /// 本地玩家是否处于序列受控态（缺陷1读取端：<c>Narrative.State.SequencerControlled</c>）。
        /// 角色模式查受控角色，宿主装配模式查引导登记/同 Actor 的宿主；
        /// 施加方为 NarrativeDialogueSequence.ApplyControlledTags 与 NarrativeLevelSequenceActor.OnPlay。
        /// </summary>
        public bool IsSequencerControlled()
        {
            var pawn = OwnedCharacter != null ? OwnedCharacter.Actor
                     : (_boundHostActor != null ? _boundHostActor : Actor);
            return NarrativePro.Character.NarrativeCharacterMovement.ActorIsSequencerControlled(pawn);
        }

        /// <summary>挂载状态下按下 <see cref="DismountKey"/> 从当前载具下车（对称入口在 VehicleTrafficSimulation）。</summary>
        protected virtual void TickDismountInput()
        {
            if (Actor == null) return;
            // 只有本地玩家才采集按键（与 TickMountInput 同一门禁）
            if (OwnedCharacter != null && !OwnedCharacter.IsLocallyControlled()) return;

            bool down = false;
            try
            {
                down = Input.GetKey(DismountKey);
            }
            catch
            {
                return;
            }

            if (down && !_dismountKeyWasDown && !IsSequencerControlled())
            {
                NarrativePro.Vehicles.VehicleTrafficSimulation.TryDismountLocalPlayerVehicle();
            }

            _dismountKeyWasDown = down;
        }

        /// <summary>交易键：注视商人开单/继续购买，按住 Shift 卖出（经 BeginTrade/TradeItem 真实结算）。</summary>
        protected virtual void TickTradeInput()
        {
            if (Actor == null) return;
            if (OwnedCharacter != null && !OwnedCharacter.IsLocallyControlled()) return;

            bool down = false;
            try
            {
                down = Input.GetKey(TradeKey);
            }
            catch
            {
                return;
            }

            bool bPressed = down && !_tradeKeyWasDown;
            _tradeKeyWasDown = down;
            if (!bPressed || IsSequencerControlled() || IsGameInputBlocked()) return;

            var pawn = OwnedCharacter != null ? OwnedCharacter.Actor
                     : (_boundHostActor != null ? _boundHostActor : Actor);
            if (pawn == null) return;

            bool bSell = false;
            try
            {
                bSell = Input.GetKey(KeyboardKeys.Shift); // Flax 1.12：修饰键是单个 KeyboardKeys.Shift（xml 核实，无 Left/RightShift）
            }
            catch
            {
                bSell = false;
            }

            // 目标销毁 / 会话超时 → 关单
            if (_tradeSession != null && (_tradeSession.Actor == null || Time.GameTime > _tradeSessionExpire))
            {
                CloseTradeSession();
            }

            if (_tradeSession == null)
            {
                var comp = GetInteractionComponent();
                var vendor = comp != null ? comp.ViewedInteractable as NarrativePro.AI.NPCInteractable : null;
                if (vendor == null || !vendor.bIsAlive || !vendor.bOffersTrade) return;
                if (!vendor.BeginTrade(pawn)) return; // 失败原因由 HandleTrade 侧告警，这里静默

                _tradeSession = vendor;
                _tradeCursor = 0;
                _tradeSessionExpire = Time.GameTime + TradeSessionSeconds;
                RefreshTradeStock();
                return;
            }

            string errorText;
            if (bSell)
            {
                var playerInventory = NarrativePro.Core.ArsenalStatics.GetNarrativeInventoryComponent(pawn);
                var mine = playerInventory != null ? playerInventory.Items : null;
                if (mine == null || mine.Count == 0 || mine[0] == null || string.IsNullOrEmpty(mine[0].ItemClassId))
                {
                    NotifyTrade("卖出失败：背包里没有可卖的物品");
                    return;
                }
                bool ok = _tradeSession.TradeItem(pawn, mine[0].ItemClassId, 1, false, out errorText);
                NotifyTrade(ok ? $"已卖出 {TradeItemName(mine[0])} x1" : $"卖出失败：{errorText}");
            }
            else
            {
                if (_tradeStock.Count == 0)
                {
                    NotifyTrade("商店暂无库存");
                    return;
                }
                var item = _tradeStock[_tradeCursor % _tradeStock.Count];
                _tradeCursor++;
                if (item == null || string.IsNullOrEmpty(item.ItemClassId))
                {
                    NotifyTrade("购买失败：库存条目无效");
                    return;
                }
                bool ok = _tradeSession.TradeItem(pawn, item.ItemClassId, 1, true, out errorText);
                NotifyTrade(ok ? $"已购买 {TradeItemName(item)} x1" : $"购买失败：{errorText}");
            }

            _tradeSessionExpire = Time.GameTime + TradeSessionSeconds;
        }

        /// <summary>关单并清缓存（幂等）。</summary>
        private void CloseTradeSession()
        {
            _tradeSession = null;
            _tradeStock.Clear();
            _tradeCursor = 0;
            _tradeSessionExpire = 0f;
        }

        /// <summary>抓一份商人库存快照，供会话内逐件购买循环。</summary>
        private void RefreshTradeStock()
        {
            _tradeStock.Clear();
            var vendorActor = _tradeSession != null ? _tradeSession.Actor : null;
            var vendorInventory = vendorActor != null ? vendorActor.GetScript<NarrativePro.Items.NarrativeInventoryComponent>() : null;
            if (vendorInventory != null && vendorInventory.Items != null) _tradeStock.AddRange(vendorInventory.Items);
        }

        private static string TradeItemName(NarrativePro.Items.NarrativeItem item)
        {
            if (item == null) return "?";
            return !string.IsNullOrEmpty(item.DisplayName) ? item.DisplayName : item.ItemClassId;
        }

        private static void NotifyTrade(string text)
        {
            // HUD 提示带统一出口（CommonUI 引导回挂，见 NarrativeProPlugin.ShowNotification）
            NarrativePro.NarrativeProPlugin.ShowNotification(text, 6f);
        }

        /// <summary>是否已配置默认绑定。</summary>
        private bool _bAbilityBindingsConfigured;

        /// <summary>上一帧各绑定的按下态（边沿检测用）。</summary>
        private readonly List<bool> _bindingWasDown = new List<bool>();

        /// <summary>
        /// 填默认的能力输入绑定（左键攻击 / 右键重击 / R 装填 / F·G·H 技能位 / T 交互）。
        /// 关卡蓝图或角色可以在 OnBeginPlay 前替换 <see cref="AbilityInputBindings"/> 覆盖。
        /// </summary>
        public virtual void SetupDefaultAbilityInputBindings()
        {
            if (_bAbilityBindingsConfigured) return;
            _bAbilityBindingsConfigured = true;
            if (AbilityInputBindings == null) AbilityInputBindings = new List<AbilityInputBinding>();
            if (AbilityInputBindings.Count > 0) return;

            AbilityInputBindings.Add(new AbilityInputBinding
            {
                InputTagName = NarrativePro.Core.NarrativeGameplayTags.Narrative_Input_Attack.TagName,
                MouseButton = MouseButton.Left
            });
            AbilityInputBindings.Add(new AbilityInputBinding
            {
                InputTagName = NarrativePro.Core.NarrativeGameplayTags.Narrative_Input_AltAttack.TagName,
                MouseButton = MouseButton.Right
            });
            AbilityInputBindings.Add(new AbilityInputBinding
            {
                InputTagName = NarrativePro.Core.NarrativeGameplayTags.Narrative_Input_Reload.TagName,
                Key = KeyboardKeys.R
            });
            AbilityInputBindings.Add(new AbilityInputBinding
            {
                InputTagName = NarrativePro.Core.NarrativeGameplayTags.Narrative_Input_Ability1.TagName,
                Key = KeyboardKeys.F
            });
            AbilityInputBindings.Add(new AbilityInputBinding
            {
                InputTagName = NarrativePro.Core.NarrativeGameplayTags.Narrative_Input_Ability2.TagName,
                Key = KeyboardKeys.G
            });
            AbilityInputBindings.Add(new AbilityInputBinding
            {
                InputTagName = NarrativePro.Core.NarrativeGameplayTags.Narrative_Input_Ability3.TagName,
                Key = KeyboardKeys.H
            });

            // 交互：与 Attack/Reload 完全同一条边沿管线（IsBindingDown → AbilityInputPressed/Released），
            // 由这两个回调转发给 PlayerInteractionComponent（见下面的 IsInteractInputTag 分支）。
            // 默认键为 T（InteractKey）：E 已被宿主工程锁敌循环占用（ThirdPersonCamera.cs:1685），
            // Q/R/F/G/H 已被本插件的换武/装填/技能位占用，Tab 有世界选层占用史——见 InteractKey 注释中的普查命令。
            var interactBinding = new AbilityInputBinding
            {
                InputTagName = NarrativePro.Core.NarrativeGameplayTags.Narrative_Input_Interact.TagName,
                Key = InteractKey,
            };
            // 交互组件上的命名输入动作优先（对应 UE5 数据驱动的 InteractionInputs 配置；
            // 工程输入设置里没有这条动作时，IsBindingDown 会自动回落到 InteractKey）
            var interactComp = GetInteractionComponent();
            if (interactComp != null && interactComp.InteractionInputs != null && interactComp.InteractionInputs.Count > 0)
            {
                interactBinding.ActionName = interactComp.InteractionInputs[0] ?? "";
            }
            AbilityInputBindings.Add(interactBinding);
        }

        /// <summary>
        /// 每帧轮询能力输入绑定，把边沿变成 ASC 的 InputTag 按下/释放。
        /// 对应 UE5 <c>SetupPlayerInputComponent</c> 里 BindAction 的 Started/Completed 两个触发点。
        /// </summary>
        protected virtual void ProcessAbilityInput()
        {
            SetupDefaultAbilityInputBindings();
            TickMountInput();
            TickDismountInput();
            TickCoverInput();
            TickTradeInput();
            if (AbilityInputBindings == null) return;

            int count = AbilityInputBindings.Count;
            if (_bindingWasDown.Count != count) _bindingWasDown.Clear();
            while (_bindingWasDown.Count < count) _bindingWasDown.Add(false);

            for (int i = 0; i < count; i++)
            {
                var binding = AbilityInputBindings[i];
                bool bDown = binding != null && !string.IsNullOrEmpty(binding.InputTagName) && IsBindingDown(binding);
                bool bWasDown = _bindingWasDown[i];
                _bindingWasDown[i] = bDown;

                if (bDown == bWasDown) continue;
                if (binding == null || string.IsNullOrEmpty(binding.InputTagName)) continue;

                var tag = new GameplayTag(binding.InputTagName);
                if (bDown) AbilityInputPressed(tag);
                else AbilityInputReleased(tag);
            }
        }

        /// <summary>快捷换武：循环持握 <see cref="QuickWieldItemClassIds"/> 里的武器。</summary>
        protected virtual void ProcessQuickWield()
        {
            if (bGameInputBlockedByUI) return;
            if (IsSequencerControlled()) return; // 缺陷1：序列受控期间锁玩家输入（与 AbilityInputPressed 同一门禁）
            if (QuickWieldItemClassIds == null || QuickWieldItemClassIds.Count == 0) return;
            if (CycleWeaponKey == KeyboardKeys.None || !Input.GetKeyDown(CycleWeaponKey)) return;

            var inventory = OwnedCharacter != null
                ? OwnedCharacter.Actor?.GetScript<NarrativePro.Items.NarrativeInventoryComponent>()
                : null;
            if (inventory == null) return;

            for (int step = 1; step <= QuickWieldItemClassIds.Count; step++)
            {
                int index = (_quickWieldIndex + step) % QuickWieldItemClassIds.Count;
                var classId = QuickWieldItemClassIds[index];
                if (string.IsNullOrEmpty(classId)) continue;
                if (inventory.UseItemOfClass(classId))
                {
                    _quickWieldIndex = index;
                    return;
                }
            }
        }

        private int _quickWieldIndex = -1;

        /// <summary>
        /// 工程输入设置里不存在的命名动作（一次性探测记账）。
        /// Flax 的 <c>Input.GetAction</c> 对未配置动作每帧都会抛异常，靠 catch 吞掉等于
        /// 每帧一次异常风暴；这里首次失败即拉黑该动作名并只告警一次，之后直接走键鼠原语。
        /// </summary>
        private static readonly HashSet<string> _unavailableActionNames = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>该绑定当前是否处于按下态（命名动作优先，其次键鼠原语）。</summary>
        private static bool IsBindingDown(AbilityInputBinding binding)
        {
            if (!string.IsNullOrEmpty(binding.ActionName) && !_unavailableActionNames.Contains(binding.ActionName))
            {
                try
                {
                    if (Input.GetAction(binding.ActionName)) return true;
                }
                catch
                {
                    // 工程输入设置里没有这条动作：永久拉黑（只Warn一次），本帧起直接回落到键鼠原语
                    if (_unavailableActionNames.Add(binding.ActionName))
                    {
                        NarrativeLog.LogWarning($"[NarrativePlayerController] 命名输入动作不存在：{binding.ActionName}；该绑定永久回落到键鼠原语（仅首次告警）。");
                    }
                }
            }
            if (binding.Key != KeyboardKeys.None && Input.GetKey(binding.Key)) return true;
            if (binding.MouseButton != MouseButton.None && Input.GetMouseButton(binding.MouseButton)) return true;
            return false;
        }

        /// <summary>能力输入按下。对应 UE5 AbilityInputPressed。</summary>
        public virtual void AbilityInputPressed(GameplayTag inputTag)
        {
            if (bGameInputBlockedByUI) return; // UI 菜单态：游戏侧能力输入不下发（UE5 由输入组件阻断）
            // 缺陷1读取端：序列受控（Narrative.State.SequencerControlled）期间，
            // 角色不再接受任何玩家输入 —— UE5 PlayerInput 的 IgnoreMoveInput/IgnoreLookInput 等价物。
            if (IsSequencerControlled()) return;
            var asc = GetAbilitySystemComponent();
            asc?.AbilityInputTagPressed(inputTag);

            // 交互标签：UE5 里 IA_Interact 不经过 GAS，而是直接绑到
            // UPlayerInteractionComponent::BeginInteract —— 端口沿用同一条边沿管线转发给它，
            // 这里就是 bInteractHeld（按住交互状态机）在全仓库的唯一写入点。
            // 单一入口声明：交互的唯一入口是这条组件直调；NarrativeInteractAbility 只作为
            // 槽位行为载荷经 InteractionComponent.RunInteractBehavior 按句柄激活
            // （见 GAS/NarrativeInteractAbility.cs 头注释），插件配置路径从不把它授予到
            // Narrative.Input.Interact 输入标签上。即便日后有人这么配，上面的 ASC 广播与这里的
            // 直调也只会生效一次：PlayerInteractionComponent.BeginInteract 已按 bInteractHeld 幂等。
            if (IsInteractInputTag(inputTag))
            {
                GetInteractionComponent()?.BeginInteract();
            }
        }

        /// <summary>能力输入释放。对应 UE5 AbilityInputReleased。</summary>
        public virtual void AbilityInputReleased(GameplayTag inputTag)
        {
            if (bGameInputBlockedByUI) return;
            var asc = GetAbilitySystemComponent();
            asc?.AbilityInputTagReleased(inputTag);

            // 松手：短按立即完成交互、长按超时取消（PlayerInteractionComponent.ReleaseInteract）
            if (IsInteractInputTag(inputTag))
            {
                GetInteractionComponent()?.ReleaseInteract();
            }
        }

        /// <summary>该输入标签是否为交互标签（<c>Narrative.Input.Interact</c>）。</summary>
        private static bool IsInteractInputTag(GameplayTag inputTag)
        {
            string name = inputTag.TagName;
            return !string.IsNullOrEmpty(name)
                   && name == NarrativePro.Core.NarrativeGameplayTags.Narrative_Input_Interact.TagName;
        }

        // ===== 死亡处理 =====

        /// <summary>处理死亡。对应 UE5 HandleDeath_Implementation。</summary>
        public virtual void HandleDeath(Actor killedActor, NarrativeAbilitySystemComponent killedActorASC)
        {
            NarrativeLog.Log($"[NarrativePlayerController] 处理死亡: {killedActor?.Name}");
            // 对应 UE5：仅本地玩家死亡触发重生流程；重生具体逻辑（存档点/复活界面）在 UE5 中同样由 BP 事件图实现，
            // Flax-不兼容: 端口暂无存档点/复活 UI（已核查 SaveSystem 无重生接口、UI/ 无复活面板），保留事件广播点供 UI 监听。
            OnPlayerDied?.Invoke(killedActor);
        }

        /// <summary>本地玩家死亡事件（替代 UE5 BP 的 ReceiveHandleDeath）。</summary>
        public event Action<Actor> OnPlayerDied;

        // ===== Possess/UnPossess 等价 =====

        /// <summary>拥有 Pawn 时调用。对应 UE5 OnPossess。</summary>
        public virtual void OnPossess(NarrativePlayerCharacter inCharacter)
        {
            OwnedCharacter = inCharacter;
            if (inCharacter != null)
            {
                inCharacter.SetCachedPlayerController(this);

                // 对应 UE5 Possess 后 PlayerController 重新采集 Pawn 上的组件：
                // 角色侧子对象由 NarrativeCharacter.EnsureSubobjects 构建，此处把引用同步到控制器
                InteractionComponent = inCharacter.Actor?.GetScript<PlayerInteractionComponent>();
                TalesComponent = inCharacter.GetTalesComponent();
                NavigationComponent = inCharacter.Actor?.GetScript<NarrativeNavigationComponent>();
                RefreshGameInputBlockState();

                // 缺陷②：UE5 在 BecomeViewTarget/Possess 里也会调 AutoManageActiveCameraTarget，这里对齐
                AutoManageActiveCameraTarget(inCharacter.Actor);
                // 缺陷③：拥有角色的瞬间尝试绑定场景相机管理器（幂等）
                EnsureSceneCameraManager();
            }
        }

        /// <summary>取消拥有时调用。对应 UE5 OnUnPossess。</summary>
        public virtual void OnUnPossess()
        {
            // 保留 OwnedCharacter 引用，避免车辆/坐骑场景丢失
        }

        /// <summary>PlayerState 改变时的本地处理。对应 UE5 OnRep_PlayerState。</summary>
        protected virtual void OnPlayerStateChanged()
        {
            // 子类可重写
        }

        /// <summary>设置输入组件。对应 UE5 SetupInputComponent。</summary>
        protected virtual void SetupInputComponent()
        {
            // Flax-已实现: 通过 Flax Input 系统接入（具体输入绑定在子类或蓝图实现）
        }

        // ===== 注视输入 =====

        /// <summary>注视输入偏航灵敏度（每输入单位转过的度数）。对应 UE5 BaseTurnRate 的简化。</summary>
        public float LookYawScale = 0.25f;

        /// <summary>注视输入俯仰灵敏度（每输入单位转过的度数）。对应 UE5 BaseLookUpRate 的简化。</summary>
        public float LookPitchScale = 0.25f;

        /// <summary>累计俯仰角（度），供相机系统读取。对应 UE5 控制器 Pitch。</summary>
        public float ControllerPitch;

        /// <summary>处理注视输入。对应 UE5 Look。</summary>
        /// <param name="value">输入值（X yaw，Y pitch）。</param>
        public virtual void Look(Float2 value)
        {
            if (IsLookInputIgnored()) return;
            // Flax-已实现: 偏航直接旋转受控角色（对应 UE5 AddYawInput→控制器旋转→Pawn），俯仰累计给相机系统
            if (OwnedCharacter?.Actor == null) return;
            var t = OwnedCharacter.Actor.Transform;
            float yawRad = value.X * LookYawScale * FlaxEngine.Mathf.DegreesToRadians;
            t.Orientation = FlaxEngine.Quaternion.RotationY(yawRad) * t.Orientation;
            OwnedCharacter.Actor.Transform = t;
            ControllerPitch = FlaxEngine.Mathf.Clamp(ControllerPitch + value.Y * LookPitchScale, -89f, 89f);
        }

        // ===== 获取组件 =====

        /// <summary>获取游戏 HUD。对应 UE5 GetNarrativeGameplayHUD。</summary>
        public virtual object GetNarrativeGameplayHUD() => GameplayHUD;

        /// <summary>
        /// 绑定运行时创建的玩法 HUD（对应 UE5 PlayerController 由 GameMode/PostInitializeComponents
        /// 拿到 DrawHUD 类并实例化）。由 <c>NarrativeRuntimeBootstrap</c> 在场景启动时回挂。
        /// </summary>
        /// <returns>绑定后的 HUD 实例，便于链式使用。</returns>
        public virtual NarrativePro.Widgets.NarrativeGameplayHUD AttachGameplayHUD(NarrativePro.Widgets.NarrativeGameplayHUD gameplayHUD)
        {
            GameplayHUD = gameplayHUD;
            if (gameplayHUD != null)
            {
                gameplayHUD.OwnerPC = this;
            }
            return gameplayHUD;
        }

        /// <summary>获取 Tales 组件。对应 UE5 GetTalesComponent。</summary>
        public virtual TalesComponent GetTalesComponent() => TalesComponent;

        /// <summary>
        /// 获取交互组件。对应 UE5 GetInteractionComponent。
        /// <para>
        /// 缓存缺失时按三种布局依次解析：① 角色模式（<see cref="OnPossess"/> 采集）；
        /// ② 宿主装配模式（控制器与交互组件同 Actor，或晚于控制器装配到宿主上）；
        /// ③ 兜底取活跃注册表里的本地玩家交互组件
        /// （引导在控制器之后才 AddScript 交互组件时，仅靠 OnEnable 的采集会拿到 null，
        ///  交互输入就会投进空引用 —— 这是整条交互链路可达性的最后一环）。
        /// </para>
        /// </summary>
        public virtual PlayerInteractionComponent GetInteractionComponent()
        {
            if (InteractionComponent != null && InteractionComponent.Actor != null) return InteractionComponent;

            InteractionComponent = Actor != null ? Actor.GetScript<PlayerInteractionComponent>() : null;
            if (InteractionComponent == null && _boundHostActor != null)
            {
                InteractionComponent = _boundHostActor.GetScript<PlayerInteractionComponent>();
            }
            if (InteractionComponent == null)
            {
                InteractionComponent = PlayerInteractionComponent.LocalPlayerInteraction;
            }
            return InteractionComponent;
        }

        /// <summary>获取导航组件。</summary>
        public virtual NarrativeNavigationComponent GetNavigationComponent() => NavigationComponent;

        // ===== NPC 系留 =====

        /// <summary>系留 NPC。对应 UE5 TetherNPC。</summary>
        /// <param name="npcToTether">要系留的 NPC。</param>
        /// <returns>是否成功系留。</returns>
        public virtual bool TetherNPC(NarrativeNPCCharacter npcToTether)
        {
            if (npcToTether == null) return false;
            if (npcToTether.GetNPCDefinition() == null) return false;

            var tether = new NPCTether
            {
                NPCCharacter = npcToTether,
                Definition = npcToTether.GetNPCDefinition(),
                NPCDefPath = npcToTether.GetNPCDefinition()?.NPCClassPath ?? "",
                NPCSaveGUID = npcToTether.SpawnInfo?.SpawnAssignedSaveGUID ?? ""
            };
            NPCTethers.Add(tether);
            return true;
        }

        /// <summary>解系 NPC。对应 UE5 UntetherNPC。</summary>
        public virtual bool UntetherNPC(NarrativeNPCCharacter npcToUntether)
        {
            if (npcToUntether == null) return false;
            for (int i = 0; i < NPCTethers.Count; i++)
            {
                if (NPCTethers[i].NPCCharacter == npcToUntether)
                {
                    NPCTethers.RemoveAt(i);
                    return true;
                }
            }
            return false;
        }

        /// <summary>查询指定 GUID 的系留。对应 UE5 GetTether。</summary>
        public virtual bool GetTether(string npcGuid, out NPCTether outTether)
        {
            outTether = null;
            if (string.IsNullOrEmpty(npcGuid)) return false;
            foreach (var t in NPCTethers)
            {
                if (t != null && t.NPCSaveGUID == npcGuid)
                {
                    outTether = t;
                    return true;
                }
            }
            return false;
        }

        /// <summary>重生所有系留的 NPC。对应 UE5 RespawnTethers。</summary>
        public virtual void RespawnTethers()
        {
            // Flax-已实现: 通过 NarrativeCharacterSubsystem 重新生成失去 Actor 的系留 NPC
            var subsys = NarrativeCharacterSubsystem.Instance;
            if (subsys == null)
            {
                NarrativeLog.LogWarning("[NarrativePlayerController] RespawnTethers: NarrativeCharacterSubsystem 未就绪");
                return;
            }
            foreach (var tether in NPCTethers)
            {
                if (tether == null) continue;
                if (tether.NPCCharacter != null && tether.NPCCharacter.Actor != null) continue; // 仍存活，跳过重生
                if (tether.Definition == null)
                {
                    NarrativeLog.LogWarning($"[NarrativePlayerController] RespawnTethers: 系留缺少定义（{tether.NPCDefPath}）");
                    continue;
                }
                var spawned = subsys.SpawnNPC(tether.Definition);
                tether.NPCCharacter = spawned?.GetScript<NarrativeNPCCharacter>();
            }
        }

        /// <summary>系留 NPC 被销毁时调用。对应 UE5 OnTetheredNPCDestroyed。</summary>
        public virtual void OnTetheredNPCDestroyed(Actor destroyedActor)
        {
            var npc = destroyedActor?.GetScript<NarrativeNPCCharacter>();
            if (npc != null)
            {
                UntetherNPC(npc);
            }
        }

        // ===== 电影序列事件 =====

        /// <summary>序列开始播放时调用。对应 UE5 LevelSequencePlayed。</summary>
        public virtual void LevelSequencePlayed(NarrativeLevelSequenceActor sequenceActor, NarrativeSequencePlaybackSettings settings)
        {
            if (sequenceActor == null) return;
            if (!CurrentSequences.Contains(sequenceActor))
            {
                CurrentSequences.Add(sequenceActor);
            }
            OnLevelSequencePlay?.Invoke(sequenceActor, settings);
        }

        /// <summary>序列停止播放时调用。对应 UE5 LevelSequenceStopped。</summary>
        public virtual void LevelSequenceStopped(NarrativeLevelSequenceActor sequenceActor, NarrativeSequencePlaybackSettings settings)
        {
            if (sequenceActor == null) return;
            CurrentSequences.Remove(sequenceActor);
            OnLevelSequenceStop?.Invoke(sequenceActor, settings);
        }

        // ===== 存档（INarrativeSavableActor 等价）=====

        /// <summary>加载存档。对应 UE5 Load_Implementation。</summary>
        public virtual void Load()
        {
            // Flax-已实现: 系留记录随 SaveGame 反序列化恢复；重生化交由此前注册的系留定义完成
            // （对应 UE5 Load_Implementation → RespawnTethers 路径；跨会话定义为 NonSerialized，需生成器重挂后才有定义）
            RespawnTethers();
        }
    }
}

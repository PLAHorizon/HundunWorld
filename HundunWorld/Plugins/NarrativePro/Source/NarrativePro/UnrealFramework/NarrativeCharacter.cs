using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.AI;
using NarrativePro.Character;
using NarrativePro.Core;
using NarrativePro.GAS;
using NarrativePro.Interaction;
using NarrativePro.Items;
using NarrativePro.Tales;

namespace NarrativePro.UnrealFramework
{
    /// <summary>
    /// 遍历动作类型。对应 UE5 ETraversalActionType。
    /// </summary>
    public enum ETraversalActionType : byte
    {
        None = 0,
        Hurdle = 1,
        Mantle = 2,
        Vault = 3,
        Climb = 4,
        ExitClimb = 5
    }

    /// <summary>
    /// 武器持有状态。对应 UE5 FWeaponWieldState。
    /// 记录当前从哪些装备槽位出鞘，以及目标手槽位。
    /// </summary>
    [Serializable]
    public class WeaponWieldState
    {
        /// <summary>出鞘来源装备槽位标签容器。</summary>
        public GameplayTagContainer EquipSlots = new GameplayTagContainer();

        /// <summary>目标手槽位标签容器。</summary>
        public GameplayTagContainer WieldSlots = new GameplayTagContainer();
    }

    /// <summary>
    /// 遍历（攀爬/跨越）附加 warp 属性。对应 UE5 FAttachWarpProps。
    /// MotionWarping 系统使用，用于动画驱动的跨越动作。
    /// </summary>
    [Serializable]
    public class AttachWarpProps
    {
        /// <summary>目标 ledge 的变换。</summary>
        public Transform LedgeTransform = Transform.Identity;

        /// <summary>当前 ledge 的变换。</summary>
        public Transform CurrentLedgeTransform = Transform.Identity;

        /// <summary>背侧 ledge 位置。</summary>
        public Vector3 BackLedgeLocation = Vector3.Zero;

        /// <summary>背侧地面位置。</summary>
        public Vector3 BackFloorLocation = Vector3.Zero;

        /// <summary>选定的动画蒙太奇路径（替代 UE5 UAnimMontage*）。</summary>
        public string SelectedMontagePath = "";

        /// <summary>当前移动模式。</summary>
        public NarrativeMovementMode CurrentMovementMode = NarrativeMovementMode.Walking;

        /// <summary>新移动模式。</summary>
        public NarrativeMovementMode NewMovementMode = NarrativeMovementMode.Walking;

        /// <summary>朝向 ledge 的 yaw 旋转。</summary>
        public float YawRotationToLedge = 0f;

        /// <summary>播放速率。</summary>
        public float PlayRate = 0f;

        /// <summary>动画起始时间。</summary>
        public float StartTime = 0f;

        /// <summary>速度。</summary>
        public float Speed = 0f;

        /// <summary>障碍物高度。</summary>
        public float ObstacleHeight = 0f;

        /// <summary>障碍物深度。</summary>
        public float ObstacleDepth = 0f;

        /// <summary>背侧 ledge 高度。</summary>
        public float BackLedgeHeight = 0f;

        /// <summary>覆盖层 blend in 时间。</summary>
        public float OverrideLayerBlendIn = 0f;

        /// <summary>覆盖层 blend out 时间。</summary>
        public float OverrideLayerBlendOut = 0f;

        /// <summary>可选 blend in 时间（-1 表示使用默认）。</summary>
        public float OptionalBlendInTime = -1f;

        /// <summary>遍历时是否按下了跳跃。</summary>
        public bool bPressedJump = false;

        /// <summary>
        /// 命中的攀爬物（对应 UE5 FAttachWarpProps::ClimbableActor，NarrativeCharacter.h:57）。
        /// 取代此前端口的死布尔 IsClimbableObject：UE5 里该 bool 只是扫描通道的副产物，
        /// 攀爬行为全部经 ClimbableActor 的 IClimbableActor 接口派发（Movement.cpp:1525-1527），
        /// 保留接口引用即保留全部真相，布尔为冗余真相源，已删除。
        /// </summary>
        [NonSerialized]
        public NarrativePro.World.IClimbable ClimbableActor;

        /// <summary>是否有正前方 ledge。</summary>
        public bool HasFrontLedge = false;

        /// <summary>是否有背侧 ledge。</summary>
        public bool HasBackLedge = false;

        /// <summary>是否有背侧地面。</summary>
        public bool HasBackFloor = false;

        /// <summary>相机是否在头部内（第一人称）。</summary>
        public bool bIsCameraInsideHead = false;

        /// <summary>遍历动作类型。</summary>
        public ETraversalActionType ActionType = ETraversalActionType.None;

        /// <summary>清空所有属性。对应 UE5 FAttachWarpProps::ClearProps。</summary>
        public static void ClearProps(AttachWarpProps props)
        {
            if (props == null) return;
            props.LedgeTransform = Transform.Identity;
            props.BackLedgeLocation = Vector3.Zero;
            props.BackFloorLocation = Vector3.Zero;
            props.SelectedMontagePath = "";
            props.CurrentMovementMode = NarrativeMovementMode.Walking;
            props.NewMovementMode = NarrativeMovementMode.Walking;
            props.YawRotationToLedge = 0f;
            props.PlayRate = 0f;
            props.StartTime = 0f;
            props.Speed = 0f;
            props.ObstacleHeight = 0f;
            props.ObstacleDepth = 0f;
            props.BackLedgeHeight = 0f;
            props.OverrideLayerBlendIn = 0f;
            props.OverrideLayerBlendOut = 0f;
            props.OptionalBlendInTime = -1f;
            props.bPressedJump = false;
            props.ClimbableActor = null; // UE5 ClearProps 不清 AActor*（先取后用，Movement.cpp:1514），端口调用序相同，显式清零更稳
            props.HasFrontLedge = false;
            props.HasBackLedge = false;
            props.HasBackFloor = false;
        }
    }

    /// <summary>角色事件委托。对应 UE5 FNarrativeCharacterEvent。</summary>
    /// <param name="character">触发事件的角色 Script。</param>
    public delegate void NarrativeCharacterEventDelegate(NarrativeCharacter character);

    /// <summary>阵营更新事件委托。对应 UE5 FOnFactionUpdated。</summary>
    public delegate void OnFactionUpdatedDelegate();

    /// <summary>传送事件委托。对应 UE5 FOnTeleported。</summary>
    public delegate void OnTeleportedDelegate();

    /// <summary>遍历事件委托。对应 UE5 FOnTraverse。</summary>
    /// <param name="traversalProps">遍历属性。</param>
    public delegate void OnTraverseDelegate(AttachWarpProps traversalProps);

    /// <summary>角色跳跃事件委托。对应 UE5 FCharacterJumped。</summary>
    public delegate void CharacterJumpedDelegate();

    /// <summary>
    /// Narrative 角色拥有者接口。对应 UE5 INarrativeCharacterOwner。
    /// 控制器、角色视觉、武器视觉等需要通过此接口获取关联的 NarrativeCharacter。
    /// </summary>
    public interface INarrativeCharacterOwner
    {
        /// <summary>返回与此对象关联的 NarrativeCharacter（即使当前未拥有）。</summary>
        NarrativeCharacter GetNarrativeCharacter();
    }

    /// <summary>
    /// Narrative 角色基类。对应 UE5 ANarrativeCharacter。
    /// UE5 中继承 ACharacter，Flax 中 Actor 为 sealed，改为 Script 挂载到 Actor 上，
    /// 通过 Actor 属性访问挂载的 Actor。
    /// 简化点：
    /// - 移除 UE5 复制/RPC（OnRep_Xxx、Server_、NetMulticast_），改为本地逻辑 + 事件回调
    /// - 移除 AISightTargetInterface、INarrativeImpactInterface 等 UE5 接口（Flax-不兼容: UE5 接口在 Flax 需按需重新设计，保留占位）
    /// - 移除 UMotionWarpingComponent（Flax-不兼容: UE5 MotionWarping 在 Flax 无对应物，保留占位）
    /// - TSubclassOf 转为字符串路径占位
    /// - FText 转为 string
    /// </summary>
    public class NarrativeCharacter : Script, INarrativeCharacterOwner
    {
        // ===== 配置字段 =====

        /// <summary>是否需要地图标记。对应 UE5 bWantsMapMarker（NarrativeCharacter.cpp:113 类默认值为 true）。</summary>
        public bool bWantsMapMarker = true;

        /// <summary>默认属性初始化效果路径（Instant 类型，设置 BaseValue）。</summary>
        public string DefaultAttributesPath = "";

        /// <summary>启动时一次性应用的效果路径列表。</summary>
        public List<string> StartupEffectPaths = new List<string>();

        /// <summary>默认授予的能力路径列表。</summary>
        public List<string> DefaultAbilityPaths = new List<string>();

        /// <summary>等级曲线 X。对应 UE5 <c>LevelExponentX</c>（NarrativeCharacter.cpp:106 = 0.07）。</summary>
        public float LevelExponentX = 0.07f;

        /// <summary>等级曲线 Y。对应 UE5 <c>LevelExponentY</c>（NarrativeCharacter.cpp:107 = 2.0）。</summary>
        public float LevelExponentY = 2.0f;

        /// <summary>
        /// 升级时是否重灌初始属性（让 MaxHealth 这类按等级缩放的派生值跟着升）。
        /// 对应 UE5 <c>bReapplyAttributesOnLevelUp</c>（NarrativeCharacter.cpp:108，默认 false）。
        /// </summary>
        public bool bReapplyAttributesOnLevelUp = false;

        /// <summary>遍历动作选择表路径（替代 UE5 UChooserTable*）。</summary>
        public string TraversalTablePath = "";

        /// <summary>触发器路径列表（对应 UE5 TArray&lt;UNarrativeTrigger*&gt; Triggers）。</summary>
        public List<string> TriggerPaths = new List<string>();

        // ===== 运行时状态 =====

        /// <summary>角色随机种子（生成一次并同步）。-1 = 尚未赋值，启用时 roll；对应 UE5 NarrativeCharacter.cpp:110。</summary>
        public int CharacterRandomSeed = -1;

        /// <summary>是否处于布娃娃状态。</summary>
        public bool bIsRagdoll = false;

        /// <summary>是否已初始化新角色。</summary>
        public bool bInitializedNewCharacter = false;

        /// <summary>当前持有的武器（物品）。</summary>
        [NonSerialized]
        public WeaponItem EquippedWeapon;

        /// <summary>当前出鞘状态。</summary>
        public WeaponWieldState WieldState = new WeaponWieldState();

        /// <summary>移动忽略的 Actor 列表（本地等价 UE5 ReplicatedMoveIgnoreActors）。</summary>
        [NonSerialized]
        public List<Actor> MoveIgnoreActors = new List<Actor>();

        /// <summary>遍历蒙太奇缓存。</summary>
        [NonSerialized]
        public List<string> TraversalMontages = new List<string>();

        /// <summary>当前遍历属性。</summary>
        public AttachWarpProps AttachWarpProps = new AttachWarpProps();

        /// <summary>是否正在播放 AttachWarp 蒙太奇。</summary>
        public bool IsPlayingAttachWarpMontage = false;

        // ===== 子组件引用（运行时通过 Actor.GetScript 查找）=====

        /// <summary>能力系统组件。</summary>
        [NonSerialized]
        protected NarrativeAbilitySystemComponent AbilitySystemComponent;

        /// <summary>属性集基类。</summary>
        [NonSerialized]
        protected NarrativeAttributeSetBase AttributeSetBase;

        /// <summary>背包组件。</summary>
        [NonSerialized]
        protected NarrativeInventoryComponent InventoryComponent;

        /// <summary>装备组件。</summary>
        [NonSerialized]
        protected EquipmentComponent EquipmentComp;

        /// <summary>角色视觉。</summary>
        [NonSerialized]
        protected NarrativeCharacterVisual CharVisual;

        /// <summary>地图标记组件。</summary>
        [NonSerialized]
        protected CharacterMapMarker MapMarker;

        /// <summary>交互组件（UE5 角色类默认对象，本端口在 OnEnable 里构建）。</summary>
        [NonSerialized]
        protected NarrativeInteractionComponent InteractionComp;

        /// <summary>Tales 组件（对话/任务状态宿主，仅本地玩家角色默认构建）。</summary>
        [NonSerialized]
        protected TalesComponent TalesComp;

        /// <summary>脚步 foley 组件（对应 UE5 由动画脚步 Notify 承担的职责）。</summary>
        [NonSerialized]
        protected NarrativeFootstepFoley FootstepFoleyComp;

        /// <summary>外观资产。</summary>
        [NonSerialized]
        protected CharacterAppearanceBase Appearance;

        /// <summary>角色定义（运行时设置）。</summary>
        [NonSerialized]
        protected CharacterDefinition CharacterDefinition;

        /// <summary>本角色的动画实例（对应 UE5 骨骼网格上的 AnimInstance，见 <see cref="GetCharacterAnimInstance"/>）。</summary>
        [NonSerialized]
        protected NarrativeAnimInstance CharacterAnimInstance;

        /// <summary>子对象是否已构建（幂等标记，OnDisable 时复位）。</summary>
        private bool _bSubobjectsEnsured;

        // ===== 事件 =====

        /// <summary>阵营更新事件。</summary>
        public event OnFactionUpdatedDelegate OnFactionUpdated;

        /// <summary>传送事件。</summary>
        public event OnTeleportedDelegate OnTeleported;

        /// <summary>开始遍历事件。</summary>
        public event OnTraverseDelegate OnStartTraversal;

        /// <summary>角色跳跃事件。</summary>
        public event CharacterJumpedDelegate OnJumpedDelegate;

        /// <summary>角色视觉初始化完成事件。</summary>
        public event NarrativeCharacterEventDelegate CharacterVisualInitialized;

        // ===== Ragdoll 内部 =====

        private float _ragdollTimer = -1f;

        /// <summary>触发阵营更新事件（供派生类调用，因 C# event 只能在声明类中触发）。</summary>
        protected virtual void RaiseOnFactionUpdated()
        {
            OnFactionUpdated?.Invoke();
        }

        /// <summary>
        /// 实现 INarrativeCharacterOwner 接口，返回自身。
        /// </summary>
        public virtual NarrativeCharacter GetNarrativeCharacter() => this;

        // ===== 生命周期 =====

        public override void OnEnable()
        {
            base.OnEnable();

            // 生成随机种子：判据用「小于 0」而不是「等于 0」——UE5 NarrativeCharacter.cpp:860
            // 的哨兵就是负数（CDO 默认 -1），所以作者真填 0 当种子时端口也该留着。
            if (CharacterRandomSeed < 0)
            {
                CharacterRandomSeed = new System.Random().Next();
            }

            // UE5 里 ASC / 背包 / 交互 / 移动 / 视觉这些是角色的类默认子对象，
            // Actor 构造时就存在；Flax 端口没有类构造器，必须在启用时显式构建，
            // 否则下面所有 GetScript 都拿到 null，整套系统静默空转。
            EnsureSubobjects();

            // UE5 的砸趴分支用 GetWorld()->TimeSince(CreationTime) > 3 挡住"出生瞬间就结算坠落"
            // （NarrativeCharacter.cpp:252），端口的 CreationTime 就是启用时刻。
            _creationUtcTime = System.DateTime.UtcNow;

            // 查找子组件（EnsureSubobjects 已保证存在，这里再解析一次以复用场景手工挂载的实例）
            AbilitySystemComponent = Actor.GetScript<NarrativeAbilitySystemComponent>();
            AttributeSetBase = Actor.GetScript<NarrativeAttributeSetBase>();
            InventoryComponent = Actor.GetScript<NarrativeInventoryComponent>();
            EquipmentComp = Actor.GetScript<EquipmentComponent>();
            CharVisual = Actor.GetScript<NarrativeCharacterVisual>();
            MapMarker = Actor.GetScript<CharacterMapMarker>();
            InteractionComp = ResolveInteractionComponent();
            TalesComp = Actor.GetScript<TalesComponent>();
            FootstepFoleyComp = Actor.GetScript<NarrativeFootstepFoley>();

            // 属性集可能由 ASC 在启用时创建（ASC.InitializeAttributes 会 AddScript），
            // 因此要在 ASC 之后再解析一次，保证角色侧引用不为空。
            AttributeSetBase ??= AbilitySystemComponent?.GetAttributeSet();

            // UE5 把 OnXPChanged 绑在属性集的委托上；端口属性集是 C# 事件，且它可能晚于角色启用
            // （由 ASC 在初始化里创建），所以订阅必须排在这里而不是更早。
            SubscribeAttributeHooks();

            // 场景预置的 ASC 不走子对象构建分支，这里再补一次绑定（幂等）
            BindDeathCallbacks();

            // 注册进角色子系统（对应 UE5 WorldSubsystem 的自动登记）
            RegisterWithCharacterSubsystem();
        }

        public override void OnDisable()
        {
            UnbindDeathCallbacks();
            UnsubscribeAttributeHooks();

            var subsystem = NarrativeCharacterSubsystem.Instance;
            if (subsystem != null && Actor != null)
            {
                subsystem.UnregisterCharacter(Actor);
            }

            _bRegisteredWithSubsystem = false;
            _bSubobjectsEnsured = false;
            base.OnDisable();
        }

        // ===== 子对象构建（对应 UE5 类默认对象 / PostInitializeComponents）=====

        /// <summary>
        /// 构建并持有 UE5 由角色类默认对象提供的全部子组件：
        /// ASC + 属性集、背包、交互组件、（玩家）Tales 组件、移动、视觉、脚步 foley。
        /// 幂等：场景里已手工挂载的组件只复用，不会重复创建；派生类可重写以增删组件。
        /// </summary>
        public virtual void EnsureSubobjects()
        {
            if (_bSubobjectsEnsured || Actor == null) return;
            _bSubobjectsEnsured = true;

            // 编辑态不装配运行期子对象（对应 UE5 的类默认对象只在游戏 World 里实例化），
            // 避免在编辑器打开场景时给 Actor 追加脚本、污染场景资产。
            if (!Engine.IsPlayMode) return;

            EnsureAbilitySystemComponent();
            EnsureInventoryComponent();
            // 装备/持握组件：UE5 由角色类默认对象提供，Flax 侧必须显式装配。
            // 没有它，EquipItem/WieldWeapon/GiveWeaponAbilities、取武器攻击与毒药读取都会静默失败
            //（玩家已在派生类补齐，这里是 NPC 与直接挂载角色的通路）。
            if (Actor.GetScript<NarrativePro.Items.EquipmentComponent>() == null)
            {
                Actor.AddScript<NarrativePro.Items.EquipmentComponent>();
            }
            EnsureInteractionComponent();
            EnsureTalesComponent();
            EnsureMovementComponent();
            EnsureCharacterVisual();
            EnsureFootstepFoley();

            // UE5 在 OnCharacterVisualInitialized（NarrativeCharacter.cpp:664）里，即派系就绪之后
            // 调 RegisterCharacterMapMarker；端口同样排在视觉/交互装配之后。
            RegisterCharacterMapMarker();
        }

        /// <summary>
        /// 创建并登记本角色的地图标记。对应 UE5
        /// <c>ANarrativeCharacter::RegisterCharacterMapMarker</c>（NarrativeCharacter.cpp:952-977）。
        /// UE5 用 <c>NewObject&lt;UCharacterMapMarker&gt;(this)</c> 挂在角色 Actor 上；
        /// Flax 端口里 <see cref="CharacterMapMarker"/> 不是 Script（<c>MapMarker</c> 是纯对象，
        /// 由 <c>MapMarker.RegisterMarker()</c> 登记进 <c>NavigationSubsystem</c> 的导航器列表），
        /// 所以这里对应的是"直接 new 一份并登记"。
        /// 此前端口只在 OnEnable 里 <c>GetScript</c> 查找、没有任何地方构造它，
        /// 于是代码生成的角色（NPC、子系统装配的玩家角色）地图标记整条不存在，
        /// 死亡/复活时的标记增删（SetDead 分支）也因引用为空而空转。
        /// </summary>
        /// <returns>是否成功登记。</returns>
        public virtual bool RegisterCharacterMapMarker()
        {
            if (!bWantsMapMarker || Actor == null) return false;
            if (MapMarker != null) return true;

            MapMarker = new CharacterMapMarker
            {
                ActorOwner = Actor
            };

            // UE5 :964-968：本地玩家在自己看的世界地图上可见；其它角色默认只进小地图/罗盘。
            // UE5 的判据是 IsLocallyControlled() && IsPlayerControlled()，端口这一层没有
            // IsPlayerControlled 的等价物（角色是否由玩家驱动由派生类与控制器决定），
            // 因此只按 IsLocallyControlled 收敛，语义保守：不会把别人升成世界地图标记。
            if (IsLocallyControlled())
            {
                MapMarker.MarkerDomain.AddTag(NarrativePro.Navigation.NavigatorGameplayTags.NavigatorTypes_Worldmap);
            }

            MapMarker.DefaultMarkerSettings.MarkerTitleText = GetCharacterName();
            MapMarker.RegisterMarker();
            return true;
        }

        /// <summary>
        /// 是否允许创建运行期子对象：仅在 Play 模式、且 Actor 有效时为真。
        /// 编辑态一律只查不建，避免给场景资产追加脚本。
        /// </summary>
        protected bool bCanCreateSubobjects => Actor != null && Engine.IsPlayMode;

        /// <summary>
        /// 构建并返回本角色的 ASC（对应 UE5 <c>ANarrativeCharacter</c> 构造函数里的
        /// <c>AbilitySystemComponent = CreateDefaultSubobject&lt;UNarrativeAbilitySystemComponent&gt;</c>）。
        /// 创建后把角色上的 GAS 配置灌入并刷新启动效果（<c>AddScript</c> 出的脚本
        /// OnEnable 已用空配置跑过一次初始化，不重刷会静默丢配置）。
        /// </summary>
        public virtual NarrativeAbilitySystemComponent EnsureAbilitySystemComponent()
        {
            if (Actor == null) return AbilitySystemComponent;

            AbilitySystemComponent = Actor.GetScript<NarrativeAbilitySystemComponent>();
            if (AbilitySystemComponent == null && bCanCreateSubobjects)
            {
                AbilitySystemComponent = Actor.AddScript<NarrativeAbilitySystemComponent>();
                if (AbilitySystemComponent != null)
                {
                    AbilitySystemComponent.DefaultAttributesEffectPath = DefaultAttributesPath;
                    AbilitySystemComponent.StartupEffectPaths = StartupEffectPaths;
                    AbilitySystemComponent.DefaultAbilityPaths = DefaultAbilityPaths;

                    if (AbilitySystemComponent.AreStartupEffectsApplied)
                    {
                        AbilitySystemComponent.ReapplyStartupEffects();
                    }
                    else
                    {
                        AbilitySystemComponent.InitializeAttributes();
                    }
                }
            }

            // 属性集由 ASC 的初始化创建；角色侧缓存同一实例
            AttributeSetBase = Actor.GetScript<NarrativeAttributeSetBase>() ?? AbilitySystemComponent?.GetAttributeSet();

            // UE5 由角色类默认对象读取 UCharacterDefinition::AbilityConfiguration 灌入 ASC。
            // 这里两条路都要覆盖：场景预置的 ASC（GetScript 命中，不走下面的 AddScript 分支）
            // 与刚新建的 ASC（AddScript 的 OnEnable 已用空配置初始化过，
            // ApplyAbilityConfiguration 会合并路径并立即补应用效果与能力）。
            ApplyDefinitionAbilityConfiguration();

            // 死亡回调在 ASC 落定的这一刻绑定：场景预置的 ASC 不会走 OnEnable 的 AddScript 分支，
            // 惰性构建（GetAbilitySystemComponent）又可能晚于 OnEnable，两处都得绑到。
            BindDeathCallbacks();
            return AbilitySystemComponent;
        }

        /// <summary>
        /// 把 <see cref="CharacterDefinition.AbilityConfigurationPath"/> 指向的能力配置灌入本角色的 ASC
        /// （对应 UE5 <c>UCharacterDefinition::AbilityConfiguration</c> 软引用在角色初始化时被读进 ASC）。
        /// 走 <c>NarrativeAbilitySystemComponent.ApplyAbilityConfiguration(path)</c> →
        /// <c>NarrativeGASAssetRegistry.LoadAbilityConfiguration</c>，因此
        /// <c>DefaultAttributes / StartupEffects / DefaultAbilityPaths</c> 三组字段由同一条既有用量通路填充，
        /// 玩家与 NPC 共用（定义分别是 PlayerDefinition / NPCDefinition）。
        /// 幂等：ASC 侧只做合并，重复调用不会二次扣属性。
        /// </summary>
        public virtual bool ApplyDefinitionAbilityConfiguration()
        {
            // 不调用 EnsureAbilitySystemComponent()，避免与本方法的调用点形成递归
            var asc = AbilitySystemComponent ?? Actor?.GetScript<NarrativeAbilitySystemComponent>();
            if (asc == null) return false;

            var definition = GetCharacterDefinition();
            if (definition == null || string.IsNullOrEmpty(definition.AbilityConfigurationPath)) return false;

            // 本方法有两个调用点（ASC 构建、定义设置），而 ASC 的 ApplyAbilityConfiguration 在
            // 启动窗口已过的场合会立刻再应用一次效果——同一条配置路径只灌一次，
            // 否则定义上的默认属性效果会按次数线性叠加（如 AttackDamage 翻倍）。
            if (string.Equals(_appliedDefinitionAbilityConfigurationPath, definition.AbilityConfigurationPath, StringComparison.Ordinal))
            {
                return false;
            }
            _appliedDefinitionAbilityConfigurationPath = definition.AbilityConfigurationPath;

            return asc.ApplyAbilityConfiguration(definition.AbilityConfigurationPath);
        }

        /// <summary>已灌入 ASC 的定义能力配置路径（防止双调用点重复应用效果）。</summary>
        [NonSerialized]
        private string _appliedDefinitionAbilityConfigurationPath = "";

        /// <summary>构建并返回背包组件（对应 UE5 角色类默认对象 <c>InventoryComponent</c>）。</summary>
        public virtual NarrativeInventoryComponent EnsureInventoryComponent()
        {
            if (Actor == null) return InventoryComponent;

            InventoryComponent = Actor.GetScript<NarrativeInventoryComponent>();
            if (InventoryComponent == null && bCanCreateSubobjects)
            {
                InventoryComponent = Actor.AddScript<NarrativeInventoryComponent>();
            }
            return InventoryComponent;
        }

        /// <summary>
        /// 解析 Actor 上已有的交互组件。
        /// Flax 的 <c>GetScript&lt;TBase&gt;</c> 不保证命中派生脚本，故按具体类型逐一探测。
        /// </summary>
        protected virtual NarrativeInteractionComponent ResolveInteractionComponent()
        {
            if (Actor == null) return null;

            var component = Actor.GetScript<PlayerInteractionComponent>() as NarrativeInteractionComponent;
            component ??= Actor.GetScript<NPCInteractionComponent>() as NarrativeInteractionComponent;
            component ??= Actor.GetScript<NarrativeInteractionComponent>();
            return component;
        }

        /// <summary>构建并返回交互组件（对应 UE5 角色类默认对象）。</summary>
        public virtual NarrativeInteractionComponent EnsureInteractionComponent()
        {
            if (Actor == null) return InteractionComp;

            InteractionComp = ResolveInteractionComponent();
            if (InteractionComp == null && bCanCreateSubobjects)
            {
                InteractionComp = CreateInteractionComponent();
            }
            return InteractionComp;
        }

        /// <summary>创建交互组件的默认类型（玩家/NPC 子类各自重写为对应派生类）。</summary>
        protected virtual NarrativeInteractionComponent CreateInteractionComponent()
        {
            return Actor.AddScript<NarrativeInteractionComponent>();
        }

        /// <summary>是否应为本角色构建 Tales 组件（对应 UE5 仅玩家侧持有任务/对话状态机）。</summary>
        protected virtual bool ShouldCreateTalesComponent() => false;

        /// <summary>构建并返回 Tales 组件（仅本地玩家角色默认构建，避免 NPC 抢注激活组件）。</summary>
        public virtual TalesComponent EnsureTalesComponent()
        {
            if (Actor == null) return TalesComp;

            TalesComp = Actor.GetScript<TalesComponent>();
            if (TalesComp == null && ShouldCreateTalesComponent() && bCanCreateSubobjects)
            {
                TalesComp = Actor.AddScript<TalesComponent>();
            }
            return TalesComp;
        }

        /// <summary>构建并返回移动组件（对应 UE5 <c>NarrativeCharacterMovement</c> 类默认对象）。</summary>
        public virtual NarrativeCharacterMovement EnsureMovementComponent()
        {
            if (Actor == null) return null;

            var movement = Actor.GetScript<NarrativeCharacterMovement>();
            if (movement == null && bCanCreateSubobjects)
            {
                movement = Actor.AddScript<NarrativeCharacterMovement>();
            }
            return movement;
        }

        /// <summary>构建并返回角色视觉（对应 UE5 <c>CharacterVisual</c> 类默认对象）。</summary>
        public virtual NarrativeCharacterVisual EnsureCharacterVisual()
        {
            if (Actor == null) return CharVisual;

            CharVisual = Actor.GetScript<NarrativeCharacterVisual>();
            if (CharVisual == null && bCanCreateSubobjects)
            {
                CharVisual = Actor.AddScript<NarrativeCharacterVisual>();
            }
            return CharVisual;
        }

        /// <summary>
        /// 构建并返回脚步 foley 组件（UE5 由动画脚步 Notify 发声，Flax 侧需要一个真实挂载的驱动脚本，
        /// 否则 <see cref="NarrativePro.Character.NarrativeCharacterFoleyExtensions.ResolveBank"/> 永远解析不到库）。
        /// </summary>
        public virtual NarrativeFootstepFoley EnsureFootstepFoley()
        {
            if (Actor == null) return FootstepFoleyComp;

            FootstepFoleyComp = Actor.GetScript<NarrativeFootstepFoley>();
            if (FootstepFoleyComp == null && bCanCreateSubobjects)
            {
                FootstepFoleyComp = Actor.AddScript<NarrativeFootstepFoley>();
            }
            return FootstepFoleyComp;
        }

        /// <summary>是否已登记进角色子系统（幂等标记，避免引导补票时重复登记）。</summary>
        private bool _bRegisteredWithSubsystem;

        /// <summary>把角色登记进 <see cref="NarrativeCharacterSubsystem"/>（子系统未就绪时静默跳过，幂等）。</summary>
        public virtual void RegisterWithCharacterSubsystem()
        {
            if (_bRegisteredWithSubsystem) return;

            var subsystem = NarrativeCharacterSubsystem.Instance;
            if (subsystem == null || Actor == null) return;

            subsystem.RegisterCharacter(Actor, GetCharacterDefinition() as NPCDefinition);
            _bRegisteredWithSubsystem = true;
        }

        public override void OnUpdate()
        {
            // 处理 Ragdoll 计时
            if (_ragdollTimer > 0f)
            {
                _ragdollTimer -= Time.DeltaTime;
                if (_ragdollTimer <= 0f)
                {
                    _ragdollTimer = -1f;
                    GetUpFromTimedRagdoll();
                }
            }
        }

        // ===== 属性/能力访问 =====

        /// <summary>返回能力系统组件。对应 UE5 GetAbilitySystemComponent。</summary>
        public virtual NarrativeAbilitySystemComponent GetAbilitySystemComponent()
        {
            // 角色可能在 OnEnable 之前被外部（生成器/存档恢复）访问，这里兜底构建
            return AbilitySystemComponent != null ? AbilitySystemComponent : EnsureAbilitySystemComponent();
        }

        /// <summary>返回 NarrativeAbilitySystemComponent。对应 UE5 GetNarrativeAbilitySystemComponent。</summary>
        public virtual NarrativeAbilitySystemComponent GetNarrativeAbilitySystemComponent() => GetAbilitySystemComponent();

        /// <summary>获取属性集基类。</summary>
        public virtual NarrativeAttributeSetBase GetAttributeSetBase()
        {
            AttributeSetBase ??= Actor?.GetScript<NarrativeAttributeSetBase>() ?? GetAbilitySystemComponent()?.GetAttributeSet();
            return AttributeSetBase;
        }

        /// <summary>获取背包组件。</summary>
        public virtual NarrativeInventoryComponent GetInventoryComponent()
        {
            return InventoryComponent != null ? InventoryComponent : EnsureInventoryComponent();
        }

        /// <summary>获取交互组件。子类可重写。</summary>
        public virtual NarrativeInteractionComponent GetInteractionComponent()
        {
            return InteractionComp != null ? InteractionComp : EnsureInteractionComponent();
        }

        /// <summary>获取装备组件。</summary>
        public virtual EquipmentComponent GetEquipmentComponent()
        {
            EquipmentComp ??= Actor?.GetScript<EquipmentComponent>();
            return EquipmentComp;
        }

        /// <summary>获取角色视觉。</summary>
        public virtual NarrativeCharacterVisual GetCharacterVisual()
        {
            return CharVisual != null ? CharVisual : EnsureCharacterVisual();
        }

        /// <summary>获取 Tales 组件（对话/任务状态机宿主，未构建时返回 null）。</summary>
        public virtual TalesComponent GetTalesComponent()
        {
            TalesComp ??= Actor?.GetScript<TalesComponent>();
            return TalesComp;
        }

        /// <summary>获取脚步 foley 组件。</summary>
        public virtual NarrativeFootstepFoley GetFootstepFoleyComponent()
        {
            FootstepFoleyComp ??= Actor?.GetScript<NarrativeFootstepFoley>();
            return FootstepFoleyComp;
        }

        /// <summary>获取地图标记组件。</summary>
        public virtual CharacterMapMarker GetMarkerComponent() => MapMarker;

        /// <summary>获取角色定义。</summary>
        public virtual CharacterDefinition GetCharacterDefinition() => CharacterDefinition;

        /// <summary>获取角色移动组件。对应 UE5 GetNarrativeCharacterMovement。</summary>
        public virtual NarrativeCharacterMovement GetNarrativeCharacterMovement()
        {
            return Actor?.GetScript<NarrativeCharacterMovement>();
        }

        // ===== 生命/属性查询 =====

        /// <summary>是否存活。对应 UE5 IsAlive。</summary>
        public virtual bool IsAlive()
        {
            if (AbilitySystemComponent == null) return true;
            return !AbilitySystemComponent.IsDead;
        }

        /// <summary>获取当前生命值。</summary>
        public virtual float GetHealth()
        {
            return AttributeSetBase?.Health.CurrentValue ?? 0f;
        }

        /// <summary>获取最大生命值。</summary>
        public virtual float GetMaxHealth()
        {
            return AttributeSetBase?.MaxHealth.CurrentValue ?? 0f;
        }

        /// <summary>获取当前耐力。</summary>
        public virtual float GetStamina()
        {
            return AttributeSetBase?.Stamina.CurrentValue ?? 0f;
        }

        /// <summary>获取最大耐力。</summary>
        public virtual float GetMaxStamina()
        {
            return AttributeSetBase?.MaxStamina.CurrentValue ?? 0f;
        }

        /// <summary>获取经验值。</summary>
        public virtual float GetXP()
        {
            return AttributeSetBase?.XP.CurrentValue ?? 0f;
        }

        /// <summary>获取潜行评级。</summary>
        public virtual float GetStealthRating()
        {
            return AttributeSetBase?.StealthRating.CurrentValue ?? 0f;
        }

        /// <summary>获取角色等级。</summary>
        public virtual int GetCharacterLevel()
        {
            return XPToLevel(GetXP());
        }

        /// <summary>获取攻击范围。</summary>
        public virtual float GetAttackRange()
        {
            // Flax-已实现: 优先取当前手持武器的攻击范围（对应 UE5 装备武器碰撞胶囊范围）
            var equipment = GetEquipmentComponent();
            if (equipment != null && equipment.WieldedWeapons != null)
            {
                foreach (var kv in equipment.WieldedWeapons)
                {
                    if (kv.Value != null) return kv.Value.GetAttackRange();
                }
            }
            return 200f;
        }

        /// <summary>设置生命值（仅用于复活等特殊场景）。</summary>
        public virtual void SetHealth(float health)
        {
            if (AttributeSetBase == null) return;
            float clamped = Mathf.Clamp(health, 0f, AttributeSetBase.MaxHealth.CurrentValue);
            AttributeSetBase.Health.SetCurrentValue(clamped);
        }

        /// <summary>设置耐力值。</summary>
        public virtual void SetStamina(float stamina)
        {
            if (AttributeSetBase == null) return;
            float clamped = Mathf.Clamp(stamina, 0f, AttributeSetBase.MaxStamina.CurrentValue);
            AttributeSetBase.Stamina.SetCurrentValue(clamped);
        }

        /// <summary>
        /// 根据 XP 计算等级。对应 UE5 <c>XPToLevel</c>（NarrativeCharacter.cpp:1715-1718）＝
        /// <c>TruncToInt(LevelExponentX * Sqrt(XP)) + 1</c>。
        /// 规则本体在 <see cref="NarrativePro.Character.ExperienceRules"/>（线上同一条，harness 直接断）。
        /// </summary>
        public virtual int XPToLevel(float xp)
        {
            return NarrativePro.Character.ExperienceRules.XpToLevel(xp, LevelExponentX);
        }

        /// <summary>
        /// 根据等级计算该级起始 XP。对应 UE5 <c>LevelToXP</c>（<c>:1720-1723</c>）＝
        /// <c>Pow((Level - 1) / LevelExponentX, LevelExponentY)</c>，是 <see cref="XPToLevel"/> 的解析反函数。
        /// </summary>
        public virtual float LevelToXP(int level)
        {
            return NarrativePro.Character.ExperienceRules.LevelToXp(level, LevelExponentX, LevelExponentY);
        }

        /// <summary>获取距离下一级的百分比。</summary>
        public virtual float GetPercentToNextLevel()
        {
            return NarrativePro.Character.ExperienceRules.PercentToNextLevel(
                GetXP(), GetCharacterLevel(), LevelExponentX, LevelExponentY);
        }

        /// <summary>
        /// 绑属性集事件（现在是 XP，日后同类回调并入这里）。
        /// 先解再订，保证 OnEnable 反复触发（重生/换宿主）时不会重复计数。
        /// </summary>
        private void SubscribeAttributeHooks()
        {
            if (AttributeSetBase == null) return;
            AttributeSetBase.OnXPChanged -= HandleXPChanged;
            AttributeSetBase.OnXPChanged += HandleXPChanged;
        }

        private void UnsubscribeAttributeHooks()
        {
            if (AttributeSetBase != null)
            {
                AttributeSetBase.OnXPChanged -= HandleXPChanged;
            }
        }

        /// <summary>
        /// XP 变化 → 是否跨级 → 升级。对应 UE5 <c>ANarrativeCharacter::OnXPChanged</c>
        /// （NarrativeCharacter.cpp:1696-1703）：比较的是换算出的等级而不是 XP 差，
        /// 所以一次加满多级也只报一次 <see cref="OnLevelUp"/>。
        /// </summary>
        protected virtual void HandleXPChanged(float newValue, float oldValue)
        {
            if (!NarrativePro.Character.ExperienceRules.DidLevelUp(oldValue, newValue, LevelExponentX))
            {
                return;
            }

            OnLevelUp(XPToLevel(newValue));
        }

        /// <summary>
        /// 升级回调。对应 UE5 <c>OnLevelUp_Implementation</c>（<c>:1705-1713</c>）：
        /// 开关打开时重灌基础属性，让按等级缩放的派生值（MaxHealth 等）跟着涨。
        /// 端口重灌走 ASC 的等价入口：启动效果已灌过时必须用
        /// <c>ReapplyStartupEffects</c>（清标记重跑），单独调 <c>InitializeAttributes</c>
        /// 会被幂等门控吞掉（见 SetDead 复活分支同一口径）。
        /// </summary>
        public virtual void OnLevelUp(int newLevel)
        {
            if (!bReapplyAttributesOnLevelUp || AbilitySystemComponent == null) return;

            if (AbilitySystemComponent.AreStartupEffectsApplied)
            {
                AbilitySystemComponent.ReapplyStartupEffects();
            }
            else
            {
                AbilitySystemComponent.InitializeAttributes();
            }
        }

        // ===== 角色定义 =====

        /// <summary>设置角色定义时调用。对应 UE5 OnDefinitionSet。</summary>
        public virtual void OnDefinitionSet(CharacterDefinition newDefinition)
        {
            CharacterDefinition = newDefinition;
            if (newDefinition != null)
            {
                // 应用默认标签/阵营
                ApplyDefaultFactions(newDefinition.DefaultFactions);
                ApplyDefaultOwnedTags(newDefinition.DefaultOwnedTags);

                // 定义晚于 ASC 到达是本工程的常态（NPC 生成先 EnsureSubobjects 建 ASC，
                // 之后才 SetNPCDefinition），所以这里补一次能力配置灌入：
                // ASC 已过启动窗口时 ApplyAbilityConfiguration 会立即应用效果并授予能力。
                ApplyDefinitionAbilityConfiguration();

                if (!bInitializedNewCharacter)
                {
                    InitNewCharacter(newDefinition);
                    bInitializedNewCharacter = true;
                }
            }
        }

        /// <summary>
        /// 默认物品装载（首次初始化时发放）。对应 UE5 <c>GetDefaultItemLoadout</c> BP 虚函数：
        /// 基类取角色定义的 <c>DefaultItemLoadout</c>；NPC 重写以支持生成参数覆盖
        /// （<see cref="NarrativeNPCCharacter.GetDefaultItemLoadout"/>）。
        /// </summary>
        public virtual List<LootTableRoll> GetDefaultItemLoadout()
        {
            return CharacterDefinition?.DefaultItemLoadout ?? new List<LootTableRoll>();
        }

        /// <summary>首次初始化新角色时调用。对应 UE5 InitNewCharacter_Implementation。</summary>
        public virtual void InitNewCharacter(CharacterDefinition newDefinition)
        {
            // UE5 NarrativeCharacter.cpp:740：首次创建角色即发放默认物品装载——
            // 遍历 GetDefaultItemLoadout()，逐表 TryAddFromLootTable 进背包，
            // 并把同一批表注入 ASC.DeathLootTables 实现「击杀掉落」。
            // 玩家与 NPC 共用本调用点（NPC 侧 NarrativeNPCCharacter 重写只影响装载来源）。
            var inventory = GetInventoryComponent();

            // 定义上的起始货币（对应 UE5 UCharacterDefinition::DefaultCurrency 在 InitCharacter 里
            // 走 Inventory->SetCurrency/AddCurrency）。此前没有任何消费点，NPC 钱包恒为 0，
            // 商人买卖校验（AllowLootItem/AllowStoreItem 的货币判定）因此永远只能靠游戏侧手工灌值。
            if (newDefinition != null && newDefinition.DefaultCurrency != 0)
            {
                inventory?.AddCurrency(newDefinition.DefaultCurrency);
            }

            var loadout = GetDefaultItemLoadout();
            if (loadout == null || loadout.Count == 0) return;

            var results = new List<ItemAddResult>();
            foreach (var roll in loadout)
            {
                if (roll == null) continue;

                if (inventory != null)
                {
                    inventory.TryAddFromLootTable(roll, results);
                }

                if (AbilitySystemComponent != null && !AbilitySystemComponent.DeathLootTables.Contains(roll))
                {
                    AbilitySystemComponent.DeathLootTables.Add(roll);
                }
            }
        }

        /// <summary>应用默认阵营。</summary>
        protected virtual void ApplyDefaultFactions(GameplayTagContainer factions)
        {
            // Flax-已实现: 阵营态度查询直接以定义 DefaultFactions 为数据源
            // （UnrealFramework/NarrativePlayerController.cs、NarrativeNPCCharacter.cs 的 GetTeamAttitudeTowards
            // 经 NarrativeGameState.GetFactionsAttitudeTowardsFactions 消费），无需 UE5 的 PlayerState 注册步骤。
        }

        /// <summary>应用默认拥有标签。</summary>
        protected virtual void ApplyDefaultOwnedTags(GameplayTagContainer tags)
        {
            if (AbilitySystemComponent != null && tags != null)
            {
                AbilitySystemComponent.AddDynamicTagsGameplayEffect(tags);
            }
        }

        // ===== 标签查询（IGameplayTagAssetInterface 等价）=====

        /// <summary>获取拥有的所有 GameplayTag。</summary>
        public virtual void GetOwnedGameplayTags(GameplayTagContainer tagContainer)
        {
            AbilitySystemComponent?.GetOwnedGameplayTags(tagContainer);
        }

        /// <summary>是否拥有指定标签。</summary>
        public virtual bool HasMatchingGameplayTag(GameplayTag tagToCheck)
        {
            return AbilitySystemComponent != null && AbilitySystemComponent.HasMatchingGameplayTag(tagToCheck);
        }

        /// <summary>是否拥有所有指定标签。</summary>
        public virtual bool HasAllMatchingGameplayTags(GameplayTagContainer tagsToCheck)
        {
            return AbilitySystemComponent != null && AbilitySystemComponent.HasAllMatchingGameplayTags(tagsToCheck);
        }

        /// <summary>是否拥有任意指定标签。</summary>
        public virtual bool HasAnyMatchingGameplayTags(GameplayTagContainer tagsToCheck)
        {
            return AbilitySystemComponent != null && AbilitySystemComponent.HasAnyMatchingGameplayTags(tagsToCheck);
        }

        // ===== 移动锁定 =====

        /// <summary>移动是否被锁定（拥有 Narrative.State.Movement.Lock 标签时为 true）。</summary>
        public virtual bool IsMovementLocked()
        {
            return HasMatchingGameplayTag(new GameplayTag("Narrative.State.Movement.Lock"));
        }

        /// <summary>相机是否在头部内（第一人称）。</summary>
        public virtual bool IsCameraInsideHead()
        {
            return HasMatchingGameplayTag(new GameplayTag("Narrative.Camera.FirstPerson.CameraInsideHead"));
        }

        // ===== 名称 =====

        /// <summary>获取角色名。对应 UE5 GetCharacterName。</summary>
        public virtual string GetCharacterName()
        {
            return Actor?.Name ?? "";
        }

        // ===== 拥有控制器 =====

        /// <summary>获取拥有此角色的控制器（即使当前未拥有）。子类可重写。</summary>
        public virtual Actor GetOwningController()
        {
            // Flax 无 Controller 概念，返回 null；子类（玩家/NPC）应重写
            return null;
        }

        /// <summary>
        /// 本角色是否由本地玩家控制器操控。对应 UE5 <c>APawn::IsLocallyControlled</c>
        /// （<c>Controller && Controller->IsLocalPlayerController()</c>）。
        /// Flax 中 NarrativePlayerController 仅存在于本地（远端角色不挂该控制器），
        /// 故以「所属控制器 Actor 上是否存在 NarrativePlayerController 脚本」判定。
        /// 子类（NarrativePlayerCharacter）缓存了控制器，可直接命中。
        /// </summary>
        public virtual bool IsLocallyControlled()
        {
            // 玩家角色走缓存控制器快速路径（SetCachedPlayerController 仅本地接线时调用）
            if (this is NarrativePlayerCharacter playerCharacter)
            {
                return playerCharacter.GetPlayerController() != null;
            }

            var controllerActor = GetOwningController();
            if (controllerActor == null) return false;
            return controllerActor.GetScript<NarrativePlayerController>() != null;
        }

        // ===== 死亡处理 =====

        /// <summary>
        /// 绑定死亡状态回调。对应 UE5 <c>AbilitySystemComponent->OnDeathStateChanged.AddUniqueDynamic(
        /// this, &amp;ANarrativeCharacter::HandleDeath)</c>（NarrativeCharacter.cpp:1645）。
        /// 先减后加：OnEnable 与 ASC 惰性构建两处都会调用，必须幂等。
        /// </summary>
        protected virtual void BindDeathCallbacks()
        {
            if (AbilitySystemComponent == null) return;
            AbilitySystemComponent.OnDeathStateChanged -= HandleDeath;
            AbilitySystemComponent.OnDeathStateChanged += HandleDeath;
        }

        /// <summary>解绑死亡状态回调（OnDisable；Flax 脚本会随 Actor 复用，残留委托会把已回收角色再拖回来）。</summary>
        protected virtual void UnbindDeathCallbacks()
        {
            if (AbilitySystemComponent == null) return;
            AbilitySystemComponent.OnDeathStateChanged -= HandleDeath;
        }

        /// <summary>处理死亡/复活。对应 UE5 <c>HandleDeath_Implementation</c>（NarrativeCharacter.cpp:1806-1878）。</summary>
        /// <param name="killedActor">状态变化的 Actor（本角色）。</param>
        /// <param name="killedActorASC">其能力系统组件。</param>
        /// <param name="bIsDead">true=死亡，false=复活（UE5 同一委托带此状态）。</param>
        protected virtual void HandleDeath(Actor killedActor, NarrativeAbilitySystemComponent killedActorASC, bool bIsDead)
        {
            NarrativeLog.Log($"[NarrativeCharacter] {GetCharacterName()}: Message, death state is now: {(bIsDead ? "dead" : "alive")}");

            // 复活按初始值重灌属性与启动效果。对应 UE5 :1812-1816（InitializeAttributes + AddStartupEffects）；
            // 端口的这两步合在 InitializeAttributes 里（由 bStartupEffectsApplied 幂等门控），
            // ReapplyStartupEffects 就是"清标记 + 重跑"的等价入口，单独调 InitializeAttributes 会被门控吞掉。
            if (!bIsDead)
            {
                AbilitySystemComponent?.ReapplyStartupEffects();
            }

            // 死亡即倒地、复活即起身（对应 UE5 :1833 SetRagdoll(bIsDead)），
            // 碰撞/输入/AI 的停与复都由移动组件的 OnEnterRagdoll/OnExitRagdoll 沿同一条路做。
            SetRagdoll(bIsDead);

            // 蒙太奇清场。UE5 只在复活时清（:1836-1844，因为它把尸体直接挪回玩家起点、不接起身动画）；
            // 端口死亡侧同样清一次：此刻被打断的是仍在播的攻击连段，尸体不该继续挥刀。
            var anim = GetCharacterAnimInstance();
            if (anim != null)
            {
                if (!bIsDead) anim.OverrideLayerBlendOutTime = 0f;
                anim.BPStopAllMontages(0f);
            }

            // UE5 :1846-1877 的胶囊/网格响应通道与地图标记增删：
            // 端口的角色没有胶囊（碰撞体是子 Actor，已由移动组件挂起），地图标记由
            // CharacterMapMarker 自身随 Owner 状态呈现，尸体可搜刮走 NPCInteractable.bIsAlive。
            var marker = GetMarkerComponent();
            if (marker != null)
            {
                if (bIsDead) marker.RemoveMarker();
                else marker.RegisterMarker();
            }
        }

        /// <summary>
        /// 本角色的动画实例。对应 UE5 <c>GetCharacterAnimInstance</c>。
        /// UE5 的 AnimInstance 由骨骼网格组件按 AnimBP 类自动实例化；Flax 的动画由 AnimationGraph
        /// 资产驱动、托管层没有 AnimInstance 对象，这里按角色惰性持有一份端口数据实例，
        /// 让蒙太奇/覆盖层这类 UE5 动画入口在本端口有真实的调用落点。
        /// </summary>
        public virtual NarrativeAnimInstance GetCharacterAnimInstance()
        {
            if (CharacterAnimInstance == null)
            {
                CharacterAnimInstance = new NarrativeAnimInstance
                {
                    NarrativeCharacterRef = this
                };
                CharacterAnimInstance.NativeInitializeAnimation();
            }
            return CharacterAnimInstance;
        }

        // ===== 位置查询 =====

        /// <summary>获取根骨骼位置（地面位置减 2 单位 Z）。</summary>
        public virtual Vector3 GetRootBoneLocation()
        {
            return GetFloorLocation(-2f);
        }

        /// <summary>获取地面位置（可选 Z 偏移）。</summary>
        public virtual Vector3 GetFloorLocation(float zOffset = 0f)
        {
            if (Actor == null) return Vector3.Zero;
            Vector3 pos = Actor.Position;
            return new Vector3(pos.X, pos.Y, pos.Z + zOffset);
        }

        /// <summary>AnimBP 查询头部骨骼注视位置。子类可重写。</summary>
        public virtual Vector3 GetHeadLookAtLocation(out bool bOutWantsLookAt)
        {
            bOutWantsLookAt = false;
            return Vector3.Zero;
        }

        // ===== 对话事件 =====

        /// <summary>成为对话 avatar 时调用。</summary>
        public virtual void OnEnterDialogue(object dialogue)
        {
            // 子类可重写
        }

        /// <summary>结束对话 avatar 时调用。</summary>
        public virtual void OnEndDialogue(object dialogue)
        {
            // 子类可重写
        }

        // ===== NarrativeEvent =====

        /// <summary>激活/取消激活一个 NarrativeEvent。返回是否成功。</summary>
        public virtual bool SetEventActive(object narrativeEvent, bool bActivate)
        {
            // Flax-不兼容: 插件端口暂无 NarrativeEvent 子系统（已全库检索无对应类），UE5 侧此为事件队列激活入口；
            // 保留签名作为接入点，当前恒返回 false。
            return false;
        }

        // ===== 跳跃/着陆（UE5 重写）=====

        /// <summary>
        /// 角色跳跃时调用。对应 UE5 OnJumped_Implementation。
        /// UE5 玩家以跳跃输入驱动遍历发现（NarrativeCharacterMovement.cpp:1583 PressedJump 分支），
        /// 本端口在跳跃事件里补同款请求：游戏侧调 OnJumped 即自动获得「对着攀爬物起跳=抓面攀爬」。
        /// </summary>
        public virtual void OnJumped()
        {
            OnJumpedDelegate?.Invoke();

            var movement = GetNarrativeCharacterMovement();
            Float2 input = movement != null ? movement.LocalInputVector : Float2.Zero;
            TryAttachWarp(true, input, -1f);
        }

        /// <summary>角色着陆时调用。对应 UE5 Landed。</summary>
        public virtual void Landed(RayCastHit hit)
        {
            // 子类可重写
        }

        private System.DateTime _creationUtcTime = System.DateTime.MinValue;

        /// <summary>
        /// 落地结算：坠落伤害 + 陡落砸趴。对应 UE5 <c>ANarrativeCharacter::Landed</c>
        /// （NarrativeCharacter.cpp:230-259），由 <c>NarrativeCharacterMovement</c> 的
        /// 重力/落地积分器在 FALLING→WALKING 收口处调用（端口里那是唯一的落地点）。
        /// <para>
        /// 与 UE5 的两处有意差别，都记在这儿备查：
        /// ① UE5 用曲线资产求比例，端口把曲线离散成移动组件上的三个作者可调参数
        ///    （起算/封顶/封顶比例），规则本体在 <see cref="NarrativePro.Character.FallDamageRules"/>；
        /// ② UE5 的砸趴分支带 <c>GetNetMode() == NM_Standalone</c> 门槛，
        ///    端口没有等价的"单机"概念（移动积分在谁的进程里跑就谁结算），因此不加这层门。
        ///    联网侧若由此产生分歧表现，需要按复制方案重新评估。
        /// </para>
        /// </summary>
        public virtual void LandedFromFallImpact(
            float impactZVelocity, NarrativePro.Character.NarrativeCharacterMovement movement)
        {
            if (movement == null) return;

            float damagePercent = NarrativePro.Character.FallDamageRules.EvaluateDamagePercent(
                impactZVelocity,
                movement.FallDamageStartZVelocity,
                movement.FallDamageFullZVelocity,
                movement.FallDamageMaxPercent);

            if (NarrativePro.Character.FallDamageRules.ShouldApplyDamage(damagePercent)
                && AbilitySystemComponent != null)
            {
                AbilitySystemComponent.DealDamage(GetMaxHealth() * damagePercent);
            }

            float ageSeconds = _creationUtcTime == System.DateTime.MinValue
                ? float.PositiveInfinity
                : (float)(System.DateTime.UtcNow - _creationUtcTime).TotalSeconds;

            if (NarrativePro.Character.FallDamageRules.ShouldRagdollOnLanding(
                    impactZVelocity, ageSeconds, movement.EnterRagdollFallZImpactGroundThreshold))
            {
                RagdollForDuration(3f);
            }
        }

        /// <summary>角色掉出世界时调用。对应 UE5 FellOutOfWorld。</summary>
        public virtual void FellOutOfWorld()
        {
            if (AbilitySystemComponent != null)
            {
                AbilitySystemComponent.DealDamage(GetHealth());
            }
        }

        // ===== 武器相关 =====

        /// <summary>设置出鞘状态。对应 UE5 SetWieldState。</summary>
        public virtual void SetWieldState(WeaponWieldState newWieldState)
        {
            WieldState = newWieldState ?? new WeaponWieldState();
            // 本地等价 OnRep_WieldState：刷新装备/视觉
            OnWieldStateChanged();
        }

        /// <summary>获取出鞘状态。</summary>
        public virtual WeaponWieldState GetWeaponWieldState() => WieldState;

        /// <summary>出鞘状态改变时的本地处理。对应 UE5 OnRep_WieldState。</summary>
        protected virtual void OnWieldStateChanged()
        {
            // Flax-已实现: 武器视觉切换由 Weapons/WeaponVisual.cs 依据 WeaponItem.WeaponVisualPath 驱动
            // （Character/NarrativeCharacterVisual.cs 的 AddWeaponVisual/RemoveWeaponVisual 是其唯一生成/移除入口），
            // 本方法保留为派生类扩展点，避免与武器视觉系统双重生成。
            NarrativeLog.Log($"[NarrativeCharacter] {Actor?.Name} 持有状态改变");
        }

        /// <summary>获取主手或副手的武器。对应 UE5 GetWeapon。</summary>
        public virtual WeaponItem GetWeapon(bool bMainhand = true) => EquippedWeapon;

        /// <summary>获取所有出鞘的武器。</summary>
        public virtual List<WeaponItem> GetWieldedWeapons()
        {
            var result = new List<WeaponItem>();
            if (EquippedWeapon != null) result.Add(EquippedWeapon);
            return result;
        }

        // ===== Ragdoll =====

        /// <summary>进入/退出布娃娃状态。对应 UE5 SetRagdoll。</summary>
        public virtual void SetRagdoll(bool bWantsRagdoll)
        {
            if (!CanRagdoll() && bWantsRagdoll) return;
            bIsRagdoll = bWantsRagdoll;
            // 本地等价 OnRep_bIsRagdoll
            OnRagdollChanged();
        }

        /// <summary>指定时长内处于布娃娃状态。多次调用会重置时长。对应 UE5 RagdollForDuration。</summary>
        public virtual void RagdollForDuration(float duration)
        {
            if (!CanRagdoll()) return;
            SetRagdoll(true);
            _ragdollTimer = Mathf.Max(_ragdollTimer, duration);
        }

        /// <summary>带伤害和冲量的布娃娃。对应 UE5 RagdollWithDamageAndImpulse。</summary>
        public virtual void RagdollWithDamageAndImpulse(float duration, Vector3 impulse, float damage)
        {
            RagdollForDuration(duration);
            if (damage > 0f && AbilitySystemComponent != null)
            {
                AbilitySystemComponent.DealDamage(damage);
            }
            // Flax-已实现: 角色 Actor 若挂有 RigidBody，按投射物同口径写入线速度近似冲量效果（Flax 骨骼角色无常驻刚体时降级跳过）
            var rigidBody = Actor != null ? Actor.GetScript<RigidBody>() : null;
            if (rigidBody != null)
            {
                rigidBody.LinearVelocity = impulse;
            }
        }

        /// <summary>是否可以进入布娃娃状态。</summary>
        public virtual bool CanRagdoll()
        {
            return !bIsRagdoll;
        }

        /// <summary>是否可以退出布娃娃状态。</summary>
        public virtual bool CanExitRagdoll()
        {
            return bIsRagdoll;
        }

        /// <summary>是否处于布娃娃状态。bCheckGettingUp 为 true 时也包含正在起身的瞬间。</summary>
        public virtual bool IsRagdoll(bool bCheckGettingUp = true)
        {
            return bIsRagdoll || (bCheckGettingUp && _ragdollTimer >= 0f);
        }

        /// <summary>定时布娃娃结束起身。对应 UE5 GetUpFromTimedRagdoll。</summary>
        protected virtual void GetUpFromTimedRagdoll()
        {
            if (CanExitRagdoll())
            {
                SetRagdoll(false);
            }
        }

        /// <summary>
        /// 布娃娃状态改变时的本地处理。对应 UE5 <c>OnRep_bIsRagdoll</c>（NarrativeCharacter.cpp:1369-1384）：
        /// 进入切 Custom/CMOVE_Ragdoll、退出回 MOVE_Walking——UE5 在这个入口里只做切档，
        /// 碰撞/物理/覆盖层都在移动组件的 OnEnterRagdoll/OnExitRagdoll 内完成，端口保持同一分层。
        /// </summary>
        protected virtual void OnRagdollChanged()
        {
            var movement = GetNarrativeCharacterMovement();
            if (movement != null)
            {
                if (bIsRagdoll)
                {
                    movement.SetMovementMode(NarrativeMovementMode.Custom, (byte)NarrativeCustomMovementMode.Ragdoll);
                }
                else
                {
                    movement.SetMovementMode(NarrativeMovementMode.Walking);
                }
            }
            else if (bIsRagdoll)
            {
                // 没有移动组件时布娃娃无处落地（UE5 的角色必带 CMC），显式说明而不是静默假成功
                NarrativeLog.LogWarning($"[NarrativeCharacter] {Actor?.Name} 缺少 NarrativeCharacterMovement，Ragdoll 无法切换移动模式");
            }

            NarrativeLog.Log($"[NarrativeCharacter] {Actor?.Name} Ragdoll = {bIsRagdoll}");
        }

        // ===== 移动忽略 Actor =====

        /// <summary>添加/移除移动忽略的 Actor。对应 UE5 SetIgnoreActorWhenMoving。</summary>
        public virtual void SetIgnoreActorWhenMoving(Actor ignoreActor, bool bShouldIgnore)
        {
            if (ignoreActor == null) return;
            if (bShouldIgnore)
            {
                if (!MoveIgnoreActors.Contains(ignoreActor))
                {
                    MoveIgnoreActors.Add(ignoreActor);
                }
            }
            else
            {
                MoveIgnoreActors.Remove(ignoreActor);
            }
        }

        /// <summary>移动忽略 Actor 列表改变时的本地处理。对应 UE5 OnRep_ReplicatedMoveIgnoreActors。</summary>
        protected virtual void OnMoveIgnoreActorsChanged()
        {
            // Flax-已实现: 忽略列表已在 MoveIgnoreActors 字段维护并即时增删（见 AddMoveIgnore/RemoveMoveIgnore），
            // 端口移动为自研运动学逻辑、无 UE5 CharacterMovement 的批量更新通知入口，无需额外同步。
        }

        // ===== 随机种子 =====

        /// <summary>设置随机种子。</summary>
        public virtual void SetRandomSeed(int newSeed)
        {
            CharacterRandomSeed = newSeed;
        }

        /// <summary>获取随机种子。</summary>
        public virtual int GetCharacterRandomSeed() => CharacterRandomSeed;

        // ===== 遍历（Traversal）=====

        /// <summary>
        /// 尝试执行 AttachWarp（跨越/攀爬）。对应 UE5 ANarrativeCharacter::TryAttachWarp
        /// （NarrativeCharacter.cpp:2027：先经移动组件 <c>TryFindAttachTransform</c> 求出 warp 属性，
        /// 成功才 Server/MultiCast PlayAttachWarp）。
        /// 端口：发现与攀爬积分在 <see cref="NarrativePro.Character.NarrativeCharacterMovement"/>
        /// （对应 UE5 把扫描逻辑放在 CMC 的分层），Flax 无 MotionWarping/蒙太奇，
        /// 成功路径 = PlayAttachWarp（事件）+ movement.BeginClimb（确定性位移）。
        /// </summary>
        /// <param name="pressedJump">是否按下跳跃。</param>
        /// <param name="inputVector">输入向量。</param>
        /// <param name="optionalInBlendTime">可选 blend in 时间（-1 使用默认）。</param>
        /// <returns>是否成功开始遍历。</returns>
        public virtual bool TryAttachWarp(bool pressedJump, Float2 inputVector, float optionalInBlendTime)
        {
            var movement = GetNarrativeCharacterMovement();
            if (movement == null) return false;
            if (movement.IsSequencerControlled || IsMovementLocked() || !IsAlive()) return false;

            // 攀爬中的跳跃 = 顶部起身的请求（UE5 把 PressedJump 透传给 TryFindClimbTransform，:1527）。
            if (movement.IsClimbing())
            {
                movement.RequestClimbJump();
                return true;
            }

            AttachWarpProps.bPressedJump = pressedJump;
            if (!movement.TryFindAttachTransform(AttachWarpProps, pressedJump, inputVector, optionalInBlendTime))
            {
                return false;
            }

            // 本地等价 ServerPlayAttachWarp → MultiCastPlayAttachWarp（Movement.cpp:2340 处 SetMovementMode(Custom, Climb)）。
            PlayAttachWarp(AttachWarpProps);
            movement.BeginClimb(AttachWarpProps);
            return true;
        }

        /// <summary>播放 AttachWarp 蒙太奇的本地处理。对应 UE5 MultiCastPlayAttachWarp。</summary>
        public virtual void PlayAttachWarp(AttachWarpProps inTraversalProps)
        {
            AttachWarpProps = inTraversalProps;
            IsPlayingAttachWarpMontage = true;
            OnStartTraversal?.Invoke(inTraversalProps);
        }

        // ===== 外观/视觉 =====

        /// <summary>变更外观。对应 UE5 ChangeAppearance_Implementation。</summary>
        public virtual void ChangeAppearance(CharacterAppearance defaultAppearance)
        {
            if (CharVisual != null)
            {
                CharVisual.InitializeFromCharacterAndAppearance(Actor, defaultAppearance);
            }
        }

        /// <summary>应用外观到角色视觉。对应 UE5 ApplyAppearance_Implementation。</summary>
        public virtual void ApplyAppearance(CharacterAppearance defaultAppearance)
        {
            ChangeAppearance(defaultAppearance);
        }

        /// <summary>角色视觉初始化完成时调用。对应 UE5 OnCharacterVisualInitialized。</summary>
        protected virtual void OnCharacterVisualInitialized()
        {
            CharacterVisualInitialized?.Invoke(this);
        }

        /// <summary>刷新胶囊体旋转设置。对应 UE5 RefreshCapsuleRotationSettings_Implementation。</summary>
        public virtual void RefreshCapsuleRotationSettings()
        {
            // 子类可重写以根据武器/视角调整 UseControllerRotationYaw、OrientRotationToMovement
        }

        /// <summary>销毁时调用。对应 UE5 Destroyed。</summary>
        protected virtual void OnDestroyed()
        {
            // 子类可重写以清理资源
        }

        /// <summary>传送成功时调用。对应 UE5 TeleportSucceeded（UE5 中为 public，供传送系统在移动完成后回调）。</summary>
        public virtual void OnTeleportSucceeded(bool bIsATest)
        {
            if (!bIsATest)
            {
                OnTeleported?.Invoke();
            }
        }
    }
}

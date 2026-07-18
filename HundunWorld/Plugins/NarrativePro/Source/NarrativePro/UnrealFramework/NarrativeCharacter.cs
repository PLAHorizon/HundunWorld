using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Character;
using NarrativePro.Core;
using NarrativePro.GAS;
using NarrativePro.Interaction;
using NarrativePro.Items;

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
    /// 近战战斗数据。对应 UE5 FMeleeCombatData。
    /// </summary>
    [Serializable]
    public class MeleeCombatData
    {
        /// <summary>无武器攻击时使用的检测数据。</summary>
        public float TraceDistance = 300f;

        /// <summary>无武器攻击时使用的检测半径。</summary>
        public float TraceRadius = 100f;

        /// <summary>普通近战连击动画集路径列表。</summary>
        public List<string> AttackComboPaths = new List<string>();

        /// <summary>重击近战连击动画集路径列表。</summary>
        public List<string> HeavyAttackComboPaths = new List<string>();
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

        /// <summary>是否为可攀爬对象。</summary>
        public bool IsClimbableObject = false;

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
            props.IsClimbableObject = false;
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

        /// <summary>是否需要地图标记。对应 UE5 bWantsMapMarker。</summary>
        public bool bWantsMapMarker = false;

        /// <summary>默认属性初始化效果路径（Instant 类型，设置 BaseValue）。</summary>
        public string DefaultAttributesPath = "";

        /// <summary>启动时一次性应用的效果路径列表。</summary>
        public List<string> StartupEffectPaths = new List<string>();

        /// <summary>默认授予的能力路径列表。</summary>
        public List<string> DefaultAbilityPaths = new List<string>();

        /// <summary>等级指数 X（越小则基础 XP 越高）。</summary>
        public float LevelExponentX = 0.1f;

        /// <summary>等级指数 Y（越大则等级间跳跃越大，指数增长）。</summary>
        public float LevelExponentY = 1.0f;

        /// <summary>遍历动作选择表路径（替代 UE5 UChooserTable*）。</summary>
        public string TraversalTablePath = "";

        /// <summary>触发器路径列表（对应 UE5 TArray&lt;UNarrativeTrigger*&gt; Triggers）。</summary>
        public List<string> TriggerPaths = new List<string>();

        // ===== 运行时状态 =====

        /// <summary>角色随机种子（生成一次并同步）。</summary>
        public int CharacterRandomSeed = 0;

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

        /// <summary>外观资产。</summary>
        [NonSerialized]
        protected CharacterAppearanceBase Appearance;

        /// <summary>角色定义（运行时设置）。</summary>
        [NonSerialized]
        protected CharacterDefinition CharacterDefinition;

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

            // 生成随机种子
            if (CharacterRandomSeed == 0)
            {
                CharacterRandomSeed = new System.Random().Next();
            }

            // 查找子组件
            AbilitySystemComponent = Actor.GetScript<NarrativeAbilitySystemComponent>();
            AttributeSetBase = Actor.GetScript<NarrativeAttributeSetBase>();
            InventoryComponent = Actor.GetScript<NarrativeInventoryComponent>();
            EquipmentComp = Actor.GetScript<EquipmentComponent>();
            CharVisual = Actor.GetScript<NarrativeCharacterVisual>();
            MapMarker = Actor.GetScript<CharacterMapMarker>();
        }

        public override void OnDisable()
        {
            base.OnDisable();
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
        public virtual NarrativeAbilitySystemComponent GetAbilitySystemComponent() => AbilitySystemComponent;

        /// <summary>返回 NarrativeAbilitySystemComponent。对应 UE5 GetNarrativeAbilitySystemComponent。</summary>
        public virtual NarrativeAbilitySystemComponent GetNarrativeAbilitySystemComponent() => AbilitySystemComponent;

        /// <summary>获取属性集基类。</summary>
        public virtual NarrativeAttributeSetBase GetAttributeSetBase() => AttributeSetBase;

        /// <summary>获取背包组件。</summary>
        public virtual NarrativeInventoryComponent GetInventoryComponent() => InventoryComponent;

        /// <summary>获取交互组件。子类可重写。</summary>
        public virtual NarrativeInteractionComponent GetInteractionComponent() => null;

        /// <summary>获取装备组件。</summary>
        public virtual EquipmentComponent GetEquipmentComponent() => EquipmentComp;

        /// <summary>获取角色视觉。</summary>
        public virtual NarrativeCharacterVisual GetCharacterVisual() => CharVisual;

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
            // TODO [需接入装备系统]: 根据装备武器返回攻击范围
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

        /// <summary>根据 XP 计算等级。对应 UE5 XPToLevel。</summary>
        public virtual int XPToLevel(float xp)
        {
            if (xp <= 0f) return 1;
            // 简化：level = floor(xp^(X / Y))
            float levelF = Mathf.Pow(xp, LevelExponentX / Mathf.Max(LevelExponentY, 0.0001f));
            return Math.Max(1, (int)Math.Floor(levelF));
        }

        /// <summary>根据等级计算 XP。对应 UE5 LevelToXP。</summary>
        public virtual float LevelToXP(int level)
        {
            if (level <= 1) return 0f;
            // 反函数：xp = level^(Y / X)
            float exponent = LevelExponentY / Mathf.Max(LevelExponentX, 0.0001f);
            return Mathf.Pow(level, exponent);
        }

        /// <summary>获取距离下一级的百分比。</summary>
        public virtual float GetPercentToNextLevel()
        {
            int currentLevel = GetCharacterLevel();
            float currentLevelXP = LevelToXP(currentLevel);
            float nextLevelXP = LevelToXP(currentLevel + 1);
            if (nextLevelXP <= currentLevelXP) return 0f;
            return Mathf.Clamp((GetXP() - currentLevelXP) / (nextLevelXP - currentLevelXP), 0f, 1f);
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

                if (!bInitializedNewCharacter)
                {
                    InitNewCharacter(newDefinition);
                    bInitializedNewCharacter = true;
                }
            }
        }

        /// <summary>首次初始化新角色时调用。对应 UE5 InitNewCharacter_Implementation。</summary>
        public virtual void InitNewCharacter(CharacterDefinition newDefinition)
        {
            // 子类可重写以发放默认物品、应用外观等
        }

        /// <summary>应用默认阵营。</summary>
        protected virtual void ApplyDefaultFactions(GameplayTagContainer factions)
        {
            // TODO [需接入 NarrativeGameState 系统]: 接入 NarrativeGameState 的阵营系统
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

        // ===== 死亡处理 =====

        /// <summary>处理死亡。对应 UE5 HandleDeath_Implementation。</summary>
        protected virtual void HandleDeath(Actor killedActor, NarrativeAbilitySystemComponent killedActorASC)
        {
            NarrativeLog.Log($"[NarrativeCharacter] {Actor?.Name} 处理死亡事件");
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
            // TODO [需接入 NarrativeEvent 系统]: 接入 NarrativeEvent 系统
            return false;
        }

        // ===== 跳跃/着陆（UE5 重写）=====

        /// <summary>角色跳跃时调用。对应 UE5 OnJumped_Implementation。</summary>
        public virtual void OnJumped()
        {
            OnJumpedDelegate?.Invoke();
        }

        /// <summary>角色着陆时调用。对应 UE5 Landed。</summary>
        public virtual void Landed(RayCastHit hit)
        {
            // 子类可重写
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
            // TODO [需接入 CharVisual 系统]: 通知 CharVisual 切换武器视觉
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
            // TODO [需接入物理系统]: 应用冲量到物理组件
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

        /// <summary>布娃娃状态改变时的本地处理。对应 UE5 OnRep_bIsRagdoll。</summary>
        protected virtual void OnRagdollChanged()
        {
            // TODO [需接入物理系统]: 切换骨骼物理模拟
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
            // TODO [需接入移动组件系统]: 通知移动组件更新忽略列表
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

        /// <summary>尝试执行 AttachWarp（跨越/攀爬）。对应 UE5 TryAttachWarp。</summary>
        /// <param name="pressedJump">是否按下跳跃。</param>
        /// <param name="inputVector">输入向量。</param>
        /// <param name="optionalInBlendTime">可选 blend in 时间（-1 使用默认）。</param>
        /// <returns>是否成功开始遍历。</returns>
        public virtual bool TryAttachWarp(bool pressedJump, Float2 inputVector, float optionalInBlendTime)
        {
            // 简化版：仅记录请求，TODO [需接入遍历检测系统]: 实现 ledge 检测和动画选择
            AttachWarpProps.bPressedJump = pressedJump;
            AttachWarpProps.OptionalBlendInTime = optionalInBlendTime;
            return false;
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

        /// <summary>传送成功时调用。对应 UE5 TeleportSucceeded。</summary>
        protected virtual void OnTeleportSucceeded(bool bIsATest)
        {
            if (!bIsATest)
            {
                OnTeleported?.Invoke();
            }
        }
    }
}

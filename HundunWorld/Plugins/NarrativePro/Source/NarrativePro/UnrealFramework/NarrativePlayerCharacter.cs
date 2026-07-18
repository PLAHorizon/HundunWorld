using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Character;
using NarrativePro.CharacterCreator;
using NarrativePro.Core;
using NarrativePro.GAS;
using NarrativePro.Items;

namespace NarrativePro.UnrealFramework
{
    /// <summary>
    /// 玩家控制角色基类。对应 UE5 ANarrativePlayerCharacter。
    /// 继承自 NarrativeCharacter（Flax Script 可继承）。
    /// 简化点：
    /// - 移除 UE5 复制/RPC（OnRep_PlayerDefinition、ServerClientVisualReady 等），改为本地逻辑 + 事件回调
    /// - 移除 UInputAction/UInputMappingContext（Flax-已实现: 接入 Flax Input 系统）
    /// - TSubclassOf 转为字符串路径
    /// </summary>
    public class NarrativePlayerCharacter : NarrativeCharacter
    {
        // ===== 输入配置 =====

        /// <summary>移动输入 Action 路径（替代 UE5 UInputAction* MoveAction）。</summary>
        public string MoveActionPath = "";

        /// <summary>注视输入 Action 路径（替代 UE5 UInputAction* LookAction）。</summary>
        public string LookActionPath = "";

        /// <summary>默认输入映射上下文路径（替代 UE5 UInputMappingContext*）。</summary>
        public string DefaultMappingContextPath = "";

        // ===== 玩家定义 =====

        /// <summary>玩家定义（运行时设置）。</summary>
        [NonSerialized]
        protected PlayerDefinition PlayerDefinition;

        // ===== 输入状态 =====

        /// <summary>是否已绑定 ASC 输入。对应 UE5 ASCInputBound。</summary>
        protected bool ASCInputBound = false;

        /// <summary>本地输入向量。对应 UE5 MovementVector。</summary>
        public Float2 MovementVector = Float2.Zero;

        /// <summary>客户端是否已通知视觉就绪（本地等价 bClientNotifiedVisualReady）。</summary>
        public bool bClientNotifiedVisualReady = false;

        // ===== 缓存控制器 =====

        /// <summary>缓存的玩家控制器（替代 UE5 CachedController，即使当前未拥有也保留）。</summary>
        [NonSerialized]
        protected NarrativePlayerController CachedController;

        /// <summary>缓存的玩家状态。</summary>
        [NonSerialized]
        protected NarrativePlayerState CachedPlayerState;

        // ===== 生命周期 =====

        public override void OnEnable()
        {
            base.OnEnable();
            // 绑定 ASC 输入（在 ASC 与视觉就绪后）
            NotifyServerIfReadyForInit();
        }

        public override void OnDisable()
        {
            base.OnDisable();
        }

        public override void OnUpdate()
        {
            base.OnUpdate();
        }

        // ===== 重写：玩家/NPC 区分 =====

        /// <summary>是否由玩家控制。</summary>
        public virtual bool IsPlayerControlled() => true;

        /// <summary>是否由 Bot 控制。</summary>
        public virtual bool IsBotControlled() => false;

        // ===== 玩家定义 =====

        /// <summary>获取玩家定义。对应 UE5 GetPlayerDefinition。</summary>
        public virtual PlayerDefinition GetPlayerDefinition() => PlayerDefinition;

        /// <summary>设置玩家定义。对应 UE5 SetPlayerDefinition。</summary>
        public virtual void SetPlayerDefinition(PlayerDefinition pDef)
        {
            PlayerDefinition = pDef;
            // 本地等价 OnRep_PlayerDefinition：触发定义应用
            OnPlayerDefinitionChanged();
        }

        /// <summary>玩家定义改变时的本地处理。对应 UE5 OnRep_PlayerDefinition。</summary>
        protected virtual void OnPlayerDefinitionChanged()
        {
            if (PlayerDefinition != null)
            {
                OnDefinitionSet(PlayerDefinition);
            }
        }

        /// <summary>获取角色定义（重写：返回 PlayerDefinition）。</summary>
        public override CharacterDefinition GetCharacterDefinition() => PlayerDefinition;

        /// <summary>首次初始化新角色时调用。对应 UE5 InitNewCharacter_Implementation。</summary>
        public override void InitNewCharacter(CharacterDefinition newDefinition)
        {
            base.InitNewCharacter(newDefinition);
            // 玩家特有初始化（默认物品等）
        }

        // ===== 名称 =====

        /// <summary>获取角色名（重写：优先使用 PlayerDisplayName）。</summary>
        public override string GetCharacterName()
        {
            if (PlayerDefinition != null && !string.IsNullOrEmpty(PlayerDefinition.PlayerDisplayName))
            {
                return PlayerDefinition.PlayerDisplayName;
            }
            return base.GetCharacterName();
        }

        // ===== 控制器/PlayerState =====

        /// <summary>获取玩家控制器（检查 previousController，避免车辆/坐骑情形）。对应 UE5 GetPlayerController。</summary>
        public virtual NarrativePlayerController GetPlayerController() => CachedController;

        /// <summary>获取玩家状态。对应 UE5 GetNarrativePlayerState。</summary>
        public virtual NarrativePlayerState GetNarrativePlayerState() => CachedPlayerState;

        /// <summary>设置缓存的玩家控制器（供 GameMode/PlayerController 调用）。</summary>
        public virtual void SetCachedPlayerController(NarrativePlayerController controller)
        {
            CachedController = controller;
        }

        /// <summary>设置缓存的玩家状态。</summary>
        public virtual void SetCachedPlayerState(NarrativePlayerState playerState)
        {
            CachedPlayerState = playerState;
        }

        /// <summary>重写：返回拥有此角色的控制器（返回控制器 Script 挂载的 Actor）。</summary>
        public override Actor GetOwningController() => CachedController?.Actor;

        // ===== 视角 =====

        /// <summary>重写：相机是否在头部内。</summary>
        public override bool IsCameraInsideHead()
        {
            // 玩家特有：通过相机模式或标签判断
            return base.IsCameraInsideHead();
        }

        /// <summary>相机是否应跟随第三人称头部骨骼位置。对应 UE5 ShouldCameraFollow3PHeadBoneLocation。</summary>
        public virtual bool ShouldCameraFollow3PHeadBoneLocation()
        {
            return HasMatchingGameplayTag(new GameplayTag("Narrative.Camera.FirstPerson.Follow3PHeadLocation"));
        }

        // ===== 输入 =====

        /// <summary>处理移动输入。对应 UE5 Move。</summary>
        /// <param name="value">输入值（X 侧向，Y 前后）。</param>
        public virtual void Move(Float2 value)
        {
            MovementVector = value;
            var movement = GetNarrativeCharacterMovement();
            if (movement != null)
            {
                movement.LocalInputVector = value;
            }
        }

        /// <summary>移动输入完成时调用。对应 UE5 CompletedMove。</summary>
        public virtual void CompletedMove()
        {
            // 子类可重写
        }

        /// <summary>处理注视输入。对应 UE5 Look。</summary>
        /// <param name="value">输入值（X yaw，Y pitch）。</param>
        public virtual void Look(Float2 value)
        {
            // TODO [需接入相机系统]: 应用到相机控制器
        }

        /// <summary>绑定/解绑能力输入。对应 UE5 SetupPlayerInputComponent 中的 ASC 输入绑定部分。</summary>
        protected virtual void BindASCInput()
        {
            if (ASCInputBound || AbilitySystemComponent == null) return;
            ASCInputBound = true;
            // TODO [需接入 NarrativeAbilityInputMapping 系统]: 接入 NarrativeAbilityInputMapping 并绑定输入标签
        }

        /// <summary>客户端通知服务器初始化就绪的本地等价。对应 UE5 ServerClientVisualReady。</summary>
        public virtual void NotifyServerIfReadyForInit()
        {
            if (bClientNotifiedVisualReady) return;
            if (AbilitySystemComponent == null) return;
            if (GetCharacterVisual() == null) return;

            bClientNotifiedVisualReady = true;
            // 本地等价：服务器接收到通知后初始化物品等
            HandleClientVisualReady();
        }

        /// <summary>客户端视觉就绪的处理。对应 UE5 服务器端接收到 ServerClientVisualReady 后的逻辑。</summary>
        protected virtual void HandleClientVisualReady()
        {
            BindASCInput();
        }

        // ===== 重写：外观 =====

        /// <summary>重写：应用外观。对应 UE5 ApplyAppearance_Implementation。</summary>
        public override void ApplyAppearance(CharacterAppearance defaultAppearance)
        {
            base.ApplyAppearance(defaultAppearance);
        }

        /// <summary>重写：角色视觉初始化完成时调用。对应 UE5 OnCharacterVisualInitialized。</summary>
        protected override void OnCharacterVisualInitialized()
        {
            base.OnCharacterVisualInitialized();
            NotifyServerIfReadyForInit();
        }

        /// <summary>重写：刷新胶囊体旋转设置。对应 UE5 RefreshCapsuleRotationSettings_Implementation。</summary>
        public override void RefreshCapsuleRotationSettings()
        {
            base.RefreshCapsuleRotationSettings();
            // 玩家特有：根据武器/第一人称调整朝向
        }

        /// <summary>获取角色创建器数据。对应 UE5 GetCharacterCreatorData。</summary>
        public virtual NarrativeSaveWithCreatorData GetCharacterCreatorData()
        {
            // TODO [需接入存档系统]: 从 PlayerDefinition 或存档获取角色创建器数据
            return null;
        }
    }
}

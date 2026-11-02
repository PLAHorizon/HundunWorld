using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Character;
using NarrativePro.CharacterCreator;
using NarrativePro.Core;
using NarrativePro.GAS;
using NarrativePro.Interaction;
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
            // 玩家一定要装备/持握武器，装备组件必须先于 ASC 输入与背包初始化存在
            EnsureEquipmentComponent();
            // 读档优先：在任何消费者使用视觉之前，把存档里的创建器外观铺上去（否则这里会看到随机重掷的外观）
            TryApplySavedCreatorAppearance();
            // 绑定 ASC 输入（在 ASC 与视觉就绪后）
            NotifyServerIfReadyForInit();
        }

        /// <summary>
        /// 装配玩家侧子对象。角色基类 <c>EnsureSubobjects</c> 只建 ASC/背包/交互/视觉/脚步，
        /// 从不创建 <see cref="NarrativePro.Items.EquipmentComponent"/>——
        /// 而 <c>EquipItem</c> / <c>WieldWeapon</c> / 毒药读取 / 攻击取武器全部依赖它，
        /// 缺件时整条持握链静默失败。玩家在基类之后补上。
        /// </summary>
        public override void EnsureSubobjects()
        {
            base.EnsureSubobjects();
            EnsureEquipmentComponent();
        }

        /// <summary>取或建玩家的装备组件（编辑态只查不建，避免污染场景资产）。</summary>
        public virtual NarrativePro.Items.EquipmentComponent EnsureEquipmentComponent()
        {
            if (Actor == null) return null;

            var existing = Actor.GetScript<NarrativePro.Items.EquipmentComponent>();
            if (existing != null)
            {
                EquipmentComp = existing;
                return existing;
            }
            if (!bCanCreateSubobjects) return null;

            EquipmentComp = Actor.AddScript<NarrativePro.Items.EquipmentComponent>();
            return EquipmentComp;
        }

        public override void OnDisable()
        {
            base.OnDisable();
        }

        public override void OnUpdate()
        {
            base.OnUpdate();

            // 输入绑定重试：控制器缓存可能晚于视觉就绪（出生/换控制权），这里补一次
            if (!ASCInputBound && AbilitySystemComponent != null && GetPlayerController() != null)
            {
                BindASCInput();
            }
        }

        // ===== 重写：子对象类型 =====

        /// <summary>玩家角色使用 <see cref="PlayerInteractionComponent"/>（对应 UE5 玩家侧交互组件类默认对象）。</summary>
        protected override NarrativeInteractionComponent CreateInteractionComponent()
        {
            return Actor.AddScript<PlayerInteractionComponent>();
        }

        /// <summary>本地玩家角色持有 Tales 状态机（对应 UE5 玩家侧 NarrativeTalesComponent）。</summary>
        protected override bool ShouldCreateTalesComponent() => true;

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
            // UE5 CommonUI 在菜单态通过输入组件阻断游戏输入；Flax 侧等价物是
            // NarrativeInputRouter.bGameInputBlocked，由本地控制器缓存后在此消费。
            var controller = GetPlayerController();
            if (controller != null && controller.bGameInputBlockedByUI)
            {
                ApplyGameInputBlock(true);
                return;
            }

            MovementVector = value;
            var movement = GetNarrativeCharacterMovement();
            if (movement != null)
            {
                movement.LocalInputVector = value;
            }
        }

        /// <summary>
        /// UI 屏蔽游戏输入时清空当前输入向量（对应 UE5 输入组件被停用时残留输入的清理）。
        /// 由 <see cref="NarrativePlayerController"/> 在激活栈变化时调用。
        /// </summary>
        public virtual void ApplyGameInputBlock(bool bBlocked)
        {
            if (!bBlocked) return;

            MovementVector = Float2.Zero;
            var movement = GetNarrativeCharacterMovement();
            if (movement != null)
            {
                movement.LocalInputVector = Float2.Zero;
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
            // Flax-已实现: 俯仰经控制器 ControllerPitch 统管（偏航由 NarrativePlayerController.Look 旋转角色），此处把俯仰增量作用于主相机
            var controller = GetPlayerController();
            if (controller == null || controller.IsLookInputIgnored()) return;
            float pitchDelta = value.Y * controller.LookPitchScale;
            float next = Mathf.Clamp(controller.ControllerPitch + pitchDelta, -89f, 89f);
            pitchDelta = next - controller.ControllerPitch;
            controller.ControllerPitch = next;
            if (Mathf.Abs(pitchDelta) < Mathf.Epsilon) return;
            var cam = FlaxEngine.Camera.MainCamera;
            if (cam == null) return;
            var t = cam.Transform;
            t.Orientation = t.Orientation * Quaternion.RotationX(pitchDelta * Mathf.DegreesToRadians);
            cam.Transform = t;
        }

        /// <summary>
        /// 玩家的能力配置 ID（<c>Content/NarrativePro/GAS/Configs/{id}.json</c>）。
        /// 承载默认属性效果与启动效果；攻击能力仍由武器授予。
        /// </summary>
        public string PlayerAbilityConfigurationId = "ability_config_player_default";

        /// <summary>
        /// 绑定/解绑能力输入。对应 UE5 SetupPlayerInputComponent 中的 ASC 输入绑定部分。
        /// <para>
        /// UE5 在这里把 InputAction 逐个绑到 <c>AbilityInputPressed/Released</c>；
        /// 端口的等价实现是控制器轮询 <c>NarrativePlayerController.AbilityInputBindings</c>
        /// （键鼠边沿 → <c>ASC.AbilityInputTagPressed/Released</c>，按 <c>spec.InputTag</c> 命中能力）。
        /// 本方法负责三件事：让控制器把默认绑定铺好、应用玩家能力配置（默认属性/启动效果），
        /// 并记录绑定状态防止重复应用。
        /// </para>
        /// </summary>
        protected virtual void BindASCInput()
        {
            if (ASCInputBound || AbilitySystemComponent == null) return;

            var controller = GetPlayerController();
            if (controller == null)
            {
                // 控制器还没缓存（出生顺序/换控制权）：等 NotifyServerIfReadyForInit 再试一次
                NarrativeLog.LogWarning("[PlayerCharacter] BindASCInput：尚未缓存玩家控制器，输入绑定推迟");
                return;
            }

            ASCInputBound = true;

            // 0) 装备组件必须在位，否则武器永远持握不上
            EnsureEquipmentComponent();

            // 1) 控制器侧铺好"按键 → 输入标签"的绑定表（幂等）
            controller.SetupDefaultAbilityInputBindings();

            // 2) 应用能力配置：默认属性效果 + 启动效果 + 默认能力（按数据定义的 InputTag 授予）
            if (!string.IsNullOrEmpty(PlayerAbilityConfigurationId))
            {
                AbilitySystemComponent.ApplyAbilityConfiguration(PlayerAbilityConfigurationId);
            }

            // 3) 已经持握的武器（例如读档恢复的装备）补一次能力授予：
            //    武器在 ASC 就绪前 HandleWield 会因拿不到 ASC 而静默失败
            RegrantWieldedWeaponAbilities(controller);
        }

        /// <summary>
        /// 为当前已持握的武器重新授予能力（对应 UE5 在输入绑定完成后再挂武器能力）。
        /// </summary>
        protected virtual void RegrantWieldedWeaponAbilities(NarrativePlayerController controller)
        {
            var actor = controller?.GetNarrativeCharacter()?.Actor ?? Actor;
            if (actor == null) return;

            var equipment = actor.GetScript<NarrativePro.Items.EquipmentComponent>();
            if (equipment == null) return;

            foreach (var weapon in equipment.WieldedWeapons.Values)
            {
                if (weapon != null) weapon.GiveWeaponAbilities();
            }
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
            // 存档里的创建器外观优先：随机外观不得覆盖已恢复的创建器数据
            if (bSavedCreatorAppearanceApplied) return;
            base.ApplyAppearance(defaultAppearance);
        }

        /// <summary>重写：角色视觉初始化完成时调用。对应 UE5 OnCharacterVisualInitialized。</summary>
        protected override void OnCharacterVisualInitialized()
        {
            base.OnCharacterVisualInitialized();

            // 视觉初始化按默认外观铺了一遍（GetAppearanceAttributes 会按随机种子重掷），
            // 这里在通知“就绪”之前再用存档里的创建器数据覆盖一次，保证下游看到的是最终外观。
            TryApplySavedCreatorAppearance();

            NotifyServerIfReadyForInit();
        }

        /// <summary>重写：刷新胶囊体旋转设置。对应 UE5 RefreshCapsuleRotationSettings_Implementation。</summary>
        public override void RefreshCapsuleRotationSettings()
        {
            base.RefreshCapsuleRotationSettings();
            // 玩家特有：根据武器/第一人称调整朝向
        }

        // ===== 创建器外观持久化 =====

        /// <summary>是否已把存档里的创建器外观应用到本角色（用于阻止随机外观覆盖）。</summary>
        protected bool bSavedCreatorAppearanceApplied;

        /// <summary>
        /// 取当前角色的创建器存档数据。对应 UE5 ANarrativePlayerCharacter::GetCharacterCreatorData
        /// （UE5 返回 UNarrativeSaveWithCreatorData，供存档与创建器组件共享外观）。
        /// <para>
        /// 本端口此前声明被当作“无调用方的死声明”删掉了注释里承认的缺口 —— 创建器产出的外观因此从不进存档，
        /// 每次会话按随机种子重掷。现在由 <see cref="NarrativePro.CharacterCreator.NarrativeCreatorPersistence"/>
        /// 在 NarrativeSaveSubsystem 写盘前调用，产出物写入 NarrativeSave.CreatorData。
        /// </para>
        /// </summary>
        public virtual NarrativePro.CharacterCreator.NarrativeSaveWithCreatorData GetCharacterCreatorData()
        {
            var visual = GetCharacterVisual();
            var attributes = visual != null ? visual.AppearanceAttributeSet : null;
            if (attributes == null) return null;

            // 用户名沿用存档里已有的（UE5 里由创建器 UI 写入 UNarrativeSaveWithCreatorData）
            var snapshot = NarrativePro.CharacterCreator.NarrativeCreatorPersistence.LoadFromCurrentSave();

            return new NarrativePro.CharacterCreator.NarrativeSaveWithCreatorData
            {
                CharacterCreatorAttributes = attributes,
                CharacterCreatorUsername = snapshot != null ? snapshot.Username : string.Empty,
            };
        }

        /// <summary>
        /// 读档优先：把 NarrativeSave.CreatorData 里的创建器外观应用到角色视觉
        /// （对应 UE5 读档时先用 UNarrativeSaveWithCreatorData 初始化 CharacterVisual 的顺序）。
        /// </summary>
        protected virtual bool TryApplySavedCreatorAppearance()
        {
            if (bSavedCreatorAppearanceApplied) return true;

            var snapshot = NarrativePro.CharacterCreator.NarrativeCreatorPersistence.LoadFromCurrentSave();
            if (snapshot == null || !snapshot.IsValid()) return false;

            var attributes = NarrativePro.CharacterCreator.NarrativeCreatorPersistence.ToAttributeSet(snapshot);
            if (attributes == null) return false;

            var visual = GetCharacterVisual();
            if (visual == null) return false;

            bSavedCreatorAppearanceApplied = true;
            visual.InitializeFromCharacterAndAttributes(Actor, attributes);

            NarrativeLog.Log(
                $"[NarrativePro] 读档应用创建器外观：表单={attributes.FormTag}，" +
                $"网格槽={attributes.MeshEntries.Count}，毛发槽={attributes.GroomEntries.Count}，" +
                $"标量={attributes.ScalarEntries.Count}，颜色={attributes.VectorEntries.Count}");
            return true;
        }
    }
}

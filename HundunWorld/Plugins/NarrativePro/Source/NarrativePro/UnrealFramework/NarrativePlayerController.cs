using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Cinematics;
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

            // 查找子组件
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
                }
            }
        }

        public override void OnDisable()
        {
            base.OnDisable();
        }

        public override void OnUpdate()
        {
            // Flax-已实现: 处理注视输入、相机管理（通过 Flax Input 系统）
            if (!IsLookInputIgnored())
            {
                Float2 lookValue = new Float2(Input.GetAxis("Look X"), Input.GetAxis("Look Y"));
                if (lookValue.LengthSquared > Mathf.Epsilon)
                {
                    Look(lookValue);
                }
            }
        }

        // ===== INarrativeCharacterOwner 实现 =====

        /// <summary>返回与此控制器关联的 NarrativeCharacter（即使当前未拥有）。</summary>
        public virtual NarrativeCharacter GetNarrativeCharacter() => OwnedCharacter;

        // ===== IAbilitySystemInterface 等价 =====

        /// <summary>获取能力系统组件。对应 UE5 GetAbilitySystemComponent。</summary>
        public virtual NarrativeAbilitySystemComponent GetAbilitySystemComponent()
        {
            return OwnedCharacter?.GetAbilitySystemComponent();
        }

        // ===== IGameplayTagAssetInterface 等价 =====

        /// <summary>获取拥有的所有 GameplayTag（委托给 OwnedCharacter 的 ASC）。</summary>
        public virtual void GetOwnedGameplayTags(GameplayTagContainer tagContainer)
        {
            OwnedCharacter?.GetOwnedGameplayTags(tagContainer);
        }

        /// <summary>是否拥有指定标签。</summary>
        public virtual bool HasMatchingGameplayTag(GameplayTag tagToCheck)
        {
            return OwnedCharacter != null && OwnedCharacter.HasMatchingGameplayTag(tagToCheck);
        }

        /// <summary>是否拥有所有指定标签。</summary>
        public virtual bool HasAllMatchingGameplayTags(GameplayTagContainer tagsToCheck)
        {
            return OwnedCharacter != null && OwnedCharacter.HasAllMatchingGameplayTags(tagsToCheck);
        }

        /// <summary>是否拥有任意指定标签。</summary>
        public virtual bool HasAnyMatchingGameplayTags(GameplayTagContainer tagsToCheck)
        {
            return OwnedCharacter != null && OwnedCharacter.HasAnyMatchingGameplayTags(tagsToCheck);
        }

        // ===== INarrativeTeamAgentInterface 等价 =====

        /// <summary>获取阵营（委托给 OwnedCharacter）。</summary>
        public virtual GameplayTagContainer GetFactions()
        {
            // TODO [需接入 NarrativePlayerState 系统]: 接入 NarrativePlayerState 的阵营
            return new GameplayTagContainer();
        }

        /// <summary>添加阵营。</summary>
        public virtual void AddFaction(GameplayTag faction)
        {
            // TODO [需接入 NarrativePlayerState 系统]: 委托给 PlayerState
        }

        /// <summary>移除阵营。</summary>
        public virtual void RemoveFaction(GameplayTag faction)
        {
            // TODO [需接入 NarrativePlayerState 系统]: 委托给 PlayerState
        }

        /// <summary>获取朝向其他 Actor 的态度。对应 UE5 GetTeamAttitudeTowards。</summary>
        /// <returns>0=友好, 1=中立, 2=敌对。</returns>
        public virtual byte GetTeamAttitudeTowards(Actor other)
        {
            // TODO [需接入 NarrativeGameState 系统]: 通过 NarrativeGameState 查询阵营态度
            return 1; // 中立
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

        /// <summary>重写：是否忽略注视输入。对应 UE5 IsLookInputIgnored。</summary>
        public virtual bool IsLookInputIgnored() => false;

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
        public virtual void FastTravelToPOI(POIData poi)
        {
            if (poi == null) return;
            NarrativeLog.Log($"[NarrativePlayerController] FastTravelToPOI: {poi}");
            // TODO [需接入快速旅行系统]: 实现快速旅行（传送玩家到 POI 位置）
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
            // TODO [需接入 HUD 系统]: 显示伤害数字
            NarrativeLog.Log($"[NarrativePlayerController] 造成伤害: {damagedActor?.Name} -{damageAmount}");
        }

        // ===== 能力输入 =====

        /// <summary>能力输入按下。对应 UE5 AbilityInputPressed。</summary>
        public virtual void AbilityInputPressed(GameplayTag inputTag)
        {
            var asc = GetAbilitySystemComponent();
            asc?.AbilityInputTagPressed(inputTag);
        }

        /// <summary>能力输入释放。对应 UE5 AbilityInputReleased。</summary>
        public virtual void AbilityInputReleased(GameplayTag inputTag)
        {
            var asc = GetAbilitySystemComponent();
            asc?.AbilityInputTagReleased(inputTag);
        }

        // ===== 死亡处理 =====

        /// <summary>处理死亡。对应 UE5 HandleDeath_Implementation。</summary>
        public virtual void HandleDeath(Actor killedActor, NarrativeAbilitySystemComponent killedActorASC)
        {
            NarrativeLog.Log($"[NarrativePlayerController] 处理死亡: {killedActor?.Name}");
            // TODO [需接入重生系统]: 触发重生逻辑
        }

        // ===== Possess/UnPossess 等价 =====

        /// <summary>拥有 Pawn 时调用。对应 UE5 OnPossess。</summary>
        public virtual void OnPossess(NarrativePlayerCharacter inCharacter)
        {
            OwnedCharacter = inCharacter;
            if (inCharacter != null)
            {
                inCharacter.SetCachedPlayerController(this);
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

        /// <summary>设置电影模式。对应 UE5 SetCinematicMode。</summary>
        public virtual void SetCinematicMode(bool bInCinematicMode, bool bHidePlayer, bool bAffectsHUD, bool bAffectsMovement, bool bAffectsTurning)
        {
            // TODO [需接入电影模式系统]: 切换电影模式（隐藏玩家、禁用 HUD/移动/转向）
            NarrativeLog.Log($"[NarrativePlayerController] SetCinematicMode = {bInCinematicMode}");
        }

        /// <summary>自动管理相机目标。对应 UE5 AutoManageActiveCameraTarget。</summary>
        protected virtual void AutoManageActiveCameraTarget(Actor suggestedTarget)
        {
            // TODO [需接入相机系统]: 设置相机目标
        }

        // ===== 注视输入 =====

        /// <summary>处理注视输入。对应 UE5 Look。</summary>
        /// <param name="value">输入值（X yaw，Y pitch）。</param>
        public virtual void Look(Float2 value)
        {
            // TODO [需接入相机系统]: 应用到相机控制器
        }

        // ===== 获取组件 =====

        /// <summary>获取游戏 HUD。对应 UE5 GetNarrativeGameplayHUD。</summary>
        public virtual object GetNarrativeGameplayHUD() => GameplayHUD;

        /// <summary>获取 Tales 组件。对应 UE5 GetTalesComponent。</summary>
        public virtual TalesComponent GetTalesComponent() => TalesComponent;

        /// <summary>获取交互组件。对应 UE5 GetInteractionComponent。</summary>
        public virtual PlayerInteractionComponent GetInteractionComponent() => InteractionComponent;

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
                NPCDefPath = "", // TODO [需接入 NPCDefinition 系统]: 从 NPCDefinition 获取路径
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
            // TODO [需接入 NarrativeCharacterSubsystem 系统]: 重新生成所有系留的 NPC
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
            // TODO [需接入存档系统]: 从存档恢复系留等状态
        }
    }
}

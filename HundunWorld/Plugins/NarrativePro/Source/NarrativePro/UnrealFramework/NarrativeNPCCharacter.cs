using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.AI;
using NarrativePro.AI.Activities;
using NarrativePro.Character;
using NarrativePro.Core;
using NarrativePro.GAS;
using NarrativePro.Interaction;
using NarrativePro.Items;

namespace NarrativePro.UnrealFramework
{
    /// <summary>
    /// 标签对话。对应 UE5 FTaggedDialogue。
    /// 通过标签触发的对话（如 TaggedDialogue.Taunt、TaggedDialogue.Greet）。
    /// </summary>
    [Serializable]
    public class TaggedDialogue
    {
        /// <summary>触发此对话的标签（Narrative.TaggedDialogue 分类）。</summary>
        public GameplayTag Tag = GameplayTag.None;

        /// <summary>对话路径（替代 UE5 TSoftClassPtr&lt;UDialogue&gt;）。</summary>
        public string DialoguePath = "";

        /// <summary>再次播放此对话前的冷却时间（秒）。</summary>
        public float Cooldown = 30f;

        /// <summary>NPC 与目标距离超过此值时不播放。</summary>
        public float MaxDistance = 5000f;

        /// <summary>NPC 拥有所有这些标签时才能开始此对话。</summary>
        public GameplayTagContainer RequiredTags = new GameplayTagContainer();

        /// <summary>NPC 拥有任意这些标签时禁止此对话（例如战斗中不问候）。</summary>
        public GameplayTagContainer BlockedTags = new GameplayTagContainer();

        /// <summary>上次播放时间（运行时）。</summary>
        public float LastPlayTime = -10000f;
    }

    /// <summary>
    /// NPC 生成参数。对应 UE5 FNPCSpawnParams。
    /// 用于覆盖 NPC 定义中的默认选项。
    /// </summary>
    [Serializable]
    public class NPCSpawnParams
    {
        /// <summary>是否覆盖 NPC 名称。</summary>
        public bool bOverride_NPCName = false;

        /// <summary>覆盖的 NPC 名称。</summary>
        public string NPCName = "";

        /// <summary>是否覆盖等级范围。</summary>
        public bool bOverride_LevelRange = false;

        /// <summary>等级下限。</summary>
        public int MinLevel = 1;

        /// <summary>等级上限。</summary>
        public int MaxLevel = 1;

        /// <summary>是否覆盖默认阵营。</summary>
        public bool bOverride_DefaultFactions = false;

        /// <summary>覆盖的默认阵营。</summary>
        public GameplayTagContainer DefaultFactions = new GameplayTagContainer();

        /// <summary>是否覆盖默认拥有标签。</summary>
        public bool bOverride_DefaultOwnedTags = false;

        /// <summary>覆盖的默认拥有标签。</summary>
        public GameplayTagContainer DefaultOwnedTags = new GameplayTagContainer();

        /// <summary>是否覆盖活动配置。</summary>
        public bool bOverride_ActivityConfiguration = false;

        /// <summary>覆盖的活动配置路径。</summary>
        public string ActivityConfigurationPath = "";

        /// <summary>是否覆盖默认物品装载。</summary>
        public bool bOverride_DefaultItemLoadout = false;

        /// <summary>覆盖的默认物品装载。</summary>
        public List<LootTableRoll> DefaultItemLoadout = new List<LootTableRoll>();

        /// <summary>是否覆盖默认外观。</summary>
        public bool bOverride_DefaultAppearance = false;

        /// <summary>覆盖的默认外观路径。</summary>
        public string DefaultAppearancePath = "";

        /// <summary>是否覆盖触发器集。</summary>
        public bool bOverride_TriggerSets = false;

        /// <summary>覆盖的触发器集路径列表。</summary>
        public List<string> TriggerSetPaths = new List<string>();

        /// <summary>是否覆盖角色随机种子。</summary>
        public bool bOverride_CharacterRandomSeed = false;

        /// <summary>覆盖的角色随机种子（-1 表示不覆盖）。</summary>
        public int CharacterRandomSeed = -1;

        /// <summary>可选的待机序列路径（替代 UE5 ULevelSequence*）。</summary>
        public string OptionalIdleSequencePath = "";
    }

    /// <summary>
    /// NPC 生成信息。对应 UE5 FNPCSpawnInfo。
    /// 记录 NPC 的生成来源、保存 GUID、生成变换等。
    /// </summary>
    [Serializable]
    public class NPCSpawnInfo
    {
        /// <summary>所属生成器的 GUID（字符串形式，替代 UE5 FGuid）。</summary>
        public string OwningSpawnerGUID = "";

        /// <summary>在生成器中的生成点名。</summary>
        public string SpawnName = "";

        /// <summary>生成器分配的保存 GUID。</summary>
        public string SpawnAssignedSaveGUID = "";

        /// <summary>生成变换。</summary>
        public Transform SpawnTransform = Transform.Identity;

        /// <summary>传入的生成参数。</summary>
        public NPCSpawnParams SpawnParams = new NPCSpawnParams();
    }

    /// <summary>
    /// Narrative NPC 角色基类。对应 UE5 ANarrativeNPCCharacter。
    /// 继承自 NarrativeCharacter（Flax Script 可继承）。
    /// 简化点：
    /// - 移除 UE5 复制/RPC（OnRep_NPCDefinition、OnRep_NPCFactions 等），改为本地逻辑 + 事件回调
    /// - 移除 INarrativeSavableActor 接口（Flax-不兼容: UE5 INarrativeSavableActor 在 Flax 无对应物，保留占位）
    /// - 移除 Mass Entity 相关
    /// - TSoftObjectPtr 转为字符串路径
    /// </summary>
    public class NarrativeNPCCharacter : NarrativeCharacter
    {
        // ===== 子组件引用 =====

        /// <summary>交易背包组件（NPC 专用）。</summary>
        [NonSerialized]
        protected NarrativeInventoryComponent TradingInventoryComponent;

        /// <summary>NPC 交互组件。</summary>
        [NonSerialized]
        protected NPCInteractable NPCInteractableComponent;

        // ===== NPC 数据 =====

        /// <summary>NPC 定义资产（运行时设置）。</summary>
        [NonSerialized]
        protected NPCDefinition NPCDefinition;

        /// <summary>生成信息。对应 UE5 SpawnInfo（SaveGame）。</summary>
        public NPCSpawnInfo SpawnInfo = new NPCSpawnInfo();

        /// <summary>NPC 等级（SaveGame）。</summary>
        public int NPCLevel = 1;

        /// <summary>NPC 阵营（SaveGame）。</summary>
        public GameplayTagContainer NPCFactions = new GameplayTagContainer();

        /// <summary>是否受击后变敌对。</summary>
        public bool bAggressiveOnTakeDamage = false;

        /// <summary>标签对话列表。</summary>
        public List<TaggedDialogue> TaggedDialogues = new List<TaggedDialogue>();

        /// <summary>敌对覆盖列表（无论阵营如何都视为敌对）。</summary>
        [NonSerialized]
        public List<Actor> Hostiles = new List<Actor>();

        /// <summary>AIController 存档记录（SaveGame）。</summary>
        public string AICRecord = "";

        // ===== 缓存控制器 =====

        /// <summary>缓存的 NPC 控制器（即使当前未拥有也保留）。</summary>
        [NonSerialized]
        protected NarrativeNPCController CachedController;

        // ===== 生命周期 =====

        public override void OnEnable()
        {
            base.OnEnable();

            // 查找 NPC 专用组件
            TradingInventoryComponent = Actor.GetScript<NarrativeInventoryComponent>();
            NPCInteractableComponent = Actor.GetScript<NPCInteractable>();
        }

        public override void OnDisable()
        {
            base.OnDisable();
        }

        // ===== 重写：玩家/NPC 区分 =====

        /// <summary>是否由玩家控制。</summary>
        public virtual bool IsPlayerControlled() => false;

        /// <summary>是否由 Bot 控制。</summary>
        public virtual bool IsBotControlled() => true;

        // ===== NPC 定义 =====

        /// <summary>获取 NPC 定义。对应 UE5 GetNPCDefinition。</summary>
        public virtual NPCDefinition GetNPCDefinition() => NPCDefinition;

        /// <summary>设置 NPC 定义。对应 UE5 SetNPCDefinition。</summary>
        public virtual void SetNPCDefinition(NPCDefinition npcData)
        {
            NPCDefinition = npcData;
            // 本地等价 OnRep_NPCDefinition：触发定义应用
            OnNPCDefinitionChanged();
        }

        /// <summary>NPC 定义改变时的本地处理。对应 UE5 OnRep_NPCDefinition。</summary>
        protected virtual void OnNPCDefinitionChanged()
        {
            if (NPCDefinition != null)
            {
                OnDefinitionSet(NPCDefinition);
                NPCDataReady();
            }
        }

        /// <summary>NPC 数据就绪时调用。对应 UE5 NPCDataReady（BlueprintImplementableEvent）。</summary>
        public virtual void NPCDataReady()
        {
            // 子类可重写
        }

        /// <summary>重写：获取角色定义（返回 NPCDefinition）。</summary>
        public override CharacterDefinition GetCharacterDefinition() => NPCDefinition;

        /// <summary>重写：设置角色定义时调用。</summary>
        public override void OnDefinitionSet(CharacterDefinition newDefinition)
        {
            base.OnDefinitionSet(newDefinition);
        }

        /// <summary>重写：首次初始化新角色。对应 UE5 InitNewCharacter_Implementation。</summary>
        public override void InitNewCharacter(CharacterDefinition newDefinition)
        {
            base.InitNewCharacter(newDefinition);
            // NPC 特有：随机化等级
            if (newDefinition is NPCDefinition npcDef)
            {
                NPCLevel = npcDef.GetRandomLevel();
            }
        }

        // ===== 名称 =====

        /// <summary>获取 NPC 名称。对应 UE5 GetNPCName。</summary>
        public virtual string GetNPCName()
        {
            if (NPCDefinition != null && !string.IsNullOrEmpty(NPCDefinition.NPCName))
            {
                return NPCDefinition.NPCName;
            }
            return Actor?.Name ?? "NPC";
        }

        /// <summary>重写：获取角色名（NPC 返回 NPC 名称）。</summary>
        public override string GetCharacterName()
        {
            return GetNPCName();
        }

        /// <summary>获取人类可读名。对应 UE5 GetHumanReadableName。</summary>
        public virtual string GetHumanReadableName()
        {
            return GetNPCName();
        }

        // ===== 等级 =====

        /// <summary>重写：获取角色等级（NPC 返回 NPCLevel）。</summary>
        public override int GetCharacterLevel() => NPCLevel;

        // ===== 控制器 =====

        /// <summary>获取 NPC 控制器。对应 UE5 GetNPCController。</summary>
        public virtual NarrativeNPCController GetNPCController() => CachedController;

        /// <summary>设置缓存的 NPC 控制器。</summary>
        public virtual void SetCachedNPCController(NarrativeNPCController controller)
        {
            CachedController = controller;
        }

        /// <summary>重写：返回拥有此角色的控制器。</summary>
        public override Actor GetOwningController() => CachedController?.Actor;

        /// <summary>获取活动组件。对应 UE5 GetActivityComponent。</summary>
        public virtual NPCActivityComponent GetActivityComponent()
        {
            return Actor?.GetScript<NPCActivityComponent>();
        }

        /// <summary>获取交易背包组件。</summary>
        public virtual NarrativeInventoryComponent GetTradingInventoryComponent() => TradingInventoryComponent;

        /// <summary>重写：获取交互组件（NPC 没有 InteractionComponent，返回 null）。</summary>
        public override NarrativeInteractionComponent GetInteractionComponent() => null;

        /// <summary>重写：获取背包组件（NPC 优先返回主背包）。</summary>
        public override NarrativeInventoryComponent GetInventoryComponent() => InventoryComponent;

        // ===== 阵营 =====

        /// <summary>重写：获取阵营（NPC 返回 NPCFactions）。</summary>
        public virtual GameplayTagContainer GetFactions() => NPCFactions;

        /// <summary>添加阵营。对应 UE5 AddFaction。</summary>
        public virtual void AddFaction(GameplayTag faction)
        {
            if (faction.IsValid())
            {
                NPCFactions.AddTag(faction);
                RaiseOnFactionUpdated();
            }
        }

        /// <summary>移除阵营。对应 UE5 RemoveFaction。</summary>
        public virtual void RemoveFaction(GameplayTag faction)
        {
            if (faction.IsValid())
            {
                NPCFactions.RemoveTag(faction);
                RaiseOnFactionUpdated();
            }
        }

        /// <summary>NPC 阵营改变时的本地处理。对应 UE5 OnRep_NPCFactions。</summary>
        protected virtual void OnNPCFactionsChanged()
        {
            RaiseOnFactionUpdated();
        }

        // ===== 敌对判定 =====

        /// <summary>是否应对目标采取敌对态度。对应 UE5 ShouldBeAggressiveTowardsTarget。</summary>
        public virtual bool ShouldBeAggressiveTowardsTarget(Actor target)
        {
            if (target == null) return false;
            return Hostiles.Contains(target);
        }

        /// <summary>获取朝向目标的态度。对应 UE5 GetTeamAttitudeTowards。</summary>
        /// <returns>0=友好, 1=中立, 2=敌对。</returns>
        public virtual byte GetTeamAttitudeTowards(Actor other)
        {
            if (other == null) return 1; // 中立
            if (Hostiles.Contains(other)) return 2; // 敌对

            // TODO [需接入 NarrativeGameState 系统]: 通过 NarrativeGameState 查询阵营态度
            return 1; // 中立
        }

        // ===== 标签对话 =====

        /// <summary>尝试播放标签对话。对应 UE5 PlayTaggedDialogue。</summary>
        /// <param name="tag">对话触发标签（Narrative.TaggedDialogue 分类）。</param>
        /// <param name="dialogueInstigator">对话发起者。</param>
        public virtual void PlayTaggedDialogue(GameplayTag tag, Actor dialogueInstigator)
        {
            if (!tag.IsValid()) return;
            TaggedDialogue found = null;
            foreach (var td in TaggedDialogues)
            {
                if (td == null) continue;
                if (td.Tag == tag)
                {
                    // 检查冷却
                    if (Time.GameTime - td.LastPlayTime < td.Cooldown) return;
                    // 检查距离
                    if (dialogueInstigator != null && Actor != null)
                    {
                        float dist = Vector3.Distance(Actor.Position, dialogueInstigator.Position);
                        if (dist > td.MaxDistance) return;
                    }
                    // 检查 RequiredTags
                    if (td.RequiredTags != null && !HasAllMatchingGameplayTags(td.RequiredTags)) return;
                    // 检查 BlockedTags
                    if (td.BlockedTags != null && HasAnyMatchingGameplayTags(td.BlockedTags)) return;
                    found = td;
                    break;
                }
            }
            if (found == null) return;

            found.LastPlayTime = Time.GameTime;
            ExecutePlayTaggedDialogue(found, dialogueInstigator);
        }

        /// <summary>实际执行标签对话播放。对应 UE5 ExecutePlayTaggedDialogue（BlueprintImplementableEvent）。</summary>
        public virtual void ExecutePlayTaggedDialogue(TaggedDialogue dialogue, Actor dialogueInstigator)
        {
            // TODO [需接入 Tales 对话系统]: 加载并播放对话
            NarrativeLog.Log($"[NarrativeNPCCharacter] {GetNPCName()} 播放标签对话: {dialogue?.Tag}");
        }

        // ===== 死亡处理 =====

        /// <summary>重写：处理死亡。对应 UE5 HandleDeath_Implementation。</summary>
        protected override void HandleDeath(Actor killedActor, NarrativeAbilitySystemComponent killedActorASC)
        {
            base.HandleDeath(killedActor, killedActorASC);
            CachedController?.HandleDeath(killedActor, killedActorASC);
        }

        // ===== 活动配置 =====

        /// <summary>应用活动配置。对应 UE5 ApplyActivityConfig_Implementation。</summary>
        public virtual void ApplyActivityConfig(NPCActivityConfiguration npcActivityConfig)
        {
            if (npcActivityConfig == null) return;
            var activityComp = GetActivityComponent();
            if (activityComp != null)
            {
                // TODO [需接入 NPCActivityComponent 系统]: 接入 NPCActivityComponent 的配置应用
            }
        }

        /// <summary>应用活动调度。对应 UE5 ApplyActivitySchedules_Implementation。</summary>
        /// <param name="activitySchedulePaths">活动调度路径列表。</param>
        public virtual void ApplyActivitySchedules(List<string> activitySchedulePaths)
        {
            if (activitySchedulePaths == null) return;
            var activityComp = GetActivityComponent();
            if (activityComp == null) return;
            // TODO [需接入 NPCActivityComponent 系统]: 加载调度并应用到活动组件
        }

        /// <summary>应用对话。对应 UE5 ApplyDialogue_Implementation。</summary>
        /// <param name="npcDialoguePath">NPC 对话路径。</param>
        public virtual void ApplyDialogue(string npcDialoguePath)
        {
            if (NPCInteractableComponent != null)
            {
                // TODO [需接入 Tales 对话系统]: 设置 NPC 交互组件的对话路径
            }
        }

        // ===== 默认数据获取（供基类 OnDefinitionSet 调用）=====

        /// <summary>获取默认物品装载（重写：NPC 从定义/生成参数获取）。</summary>
        public virtual List<LootTableRoll> GetDefaultItemLoadout()
        {
            if (SpawnInfo?.SpawnParams?.bOverride_DefaultItemLoadout == true)
            {
                return SpawnInfo.SpawnParams.DefaultItemLoadout;
            }
            return NPCDefinition?.DefaultItemLoadout ?? new List<LootTableRoll>();
        }

        /// <summary>获取默认外观路径（重写：NPC 从定义/生成参数获取）。</summary>
        public virtual string GetDefaultAppearancePath()
        {
            if (SpawnInfo?.SpawnParams?.bOverride_DefaultAppearance == true)
            {
                return SpawnInfo.SpawnParams.DefaultAppearancePath;
            }
            // NPCDefinition.DefaultAppearance 是 CharacterAppearance 类型，此处返回其路径占位
            return "";
        }

        /// <summary>获取默认触发器集路径（重写：NPC 从定义/生成参数获取）。</summary>
        public virtual List<string> GetDefaultTriggerSetPaths()
        {
            if (SpawnInfo?.SpawnParams?.bOverride_TriggerSets == true)
            {
                return SpawnInfo.SpawnParams.TriggerSetPaths;
            }
            return NPCDefinition?.TriggerSetPaths ?? new List<string>();
        }

        // ===== 掉落世界 =====

        /// <summary>重写：掉出世界时调用。对应 UE5 FellOutOfWorld。</summary>
        public override void FellOutOfWorld()
        {
            base.FellOutOfWorld();
            // NPC 特有：清理
            if (CachedController != null)
            {
                CachedController.CleanUp(0f);
            }
        }

        // ===== 存档（INarrativeSavableActor 等价）=====

        /// <summary>准备保存。对应 UE5 PrepareForSave_Implementation。</summary>
        public virtual void PrepareForSave()
        {
            // TODO [需接入存档系统]: 收集需要保存的状态
        }

        /// <summary>加载存档。对应 UE5 Load_Implementation。</summary>
        public virtual void Load()
        {
            // TODO [需接入存档系统]: 从存档恢复状态
        }

        /// <summary>处理生成参数覆盖加载完成。对应 UE5 HandleSpawnParamOverridesLoaded。</summary>
        protected virtual void HandleSpawnParamOverridesLoaded()
        {
            if (SpawnInfo?.SpawnParams?.bOverride_CharacterRandomSeed == true)
            {
                SetRandomSeed(SpawnInfo.SpawnParams.CharacterRandomSeed);
            }
        }
    }
}

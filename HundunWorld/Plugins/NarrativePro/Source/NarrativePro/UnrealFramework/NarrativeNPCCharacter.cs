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
using NarrativePro.Tales;

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

        /// <summary>本 NPC 是否配置了标签对话（供交互入口判定）。</summary>
        public bool HasTaggedDialogues => TaggedDialogues != null && TaggedDialogues.Count > 0;

        /// <summary>
        /// 运行时触发器实例。对应 UE5 ANarrativeCharacter.Triggers（NarrativeCharacter.h:686，
        /// AddTrigger 复制模板后加入）。Flax 端口宿主放在 NPC 侧。
        /// </summary>
        [NonSerialized]
        public List<NarrativeTrigger> Triggers = new List<NarrativeTrigger>();

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
            // UE5 触发器对象随 Owner 销毁、时辰委托自动失效；Flax 需显式反注册，防悬挂回调。
            ClearTriggers();
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
                // UE5 生成路径：定义就绪即加载 NPCActivityConfiguration 数据资产并 SetActivityConfiguration，
                // 同时逐条加载调度资产 AddActivitySchedule（NPCActivityComponent.cpp:568-623）。
                ApplyActivityConfigurationFromDefinition();
                // UE5 生成路径（NarrativeCharacter.cpp:535-556）：定义数据就绪即加载 TriggerSet 模板，
                // 经 AddTrigger 复制实例、绑定 OwnerCharacter 并 Initialize（NarrativeCharacter.cpp:1921-1935）。
                ApplyTriggerSetsFromDefinition();
                // UE5 NPC.cpp:392-397：TaggedDialogueSet 资产加载后把条目复制进 NPC 的 TaggedDialogues。
                LoadTaggedDialoguesFromDefinition();
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

        /// <summary>默认物品装载是否已由角色路径应用（防止控制器重复发放）。</summary>
        public bool bDefaultItemLoadoutApplied;

        /// <summary>重写：首次初始化新角色。对应 UE5 InitNewCharacter_Implementation。</summary>
        public override void InitNewCharacter(CharacterDefinition newDefinition)
        {
            // 基类经 GetDefaultItemLoadout（本类重写）完成 UE5 NarrativeCharacter.cpp:740 的默认装载发放
            base.InitNewCharacter(newDefinition);
            bDefaultItemLoadoutApplied = true;
            // NPC 特有：随机化等级
            if (newDefinition is NPCDefinition npcDef)
            {
                NPCLevel = npcDef.GetRandomLevel();
            }

            // UE5 的 NPC 蓝图在 BeginPlay 里会装备自己的武器，武器 HandleWield → GiveWeaponAbilities
            // 才把战斗能力挂上 ASC。本端口此前没有任何 NPC 装备调用点：ASC 上永远没有
            // NarrativeCombatAbility，NarrativeNPCController.UpdateCombatAI 的
            // FindFirstCombatAbilityHandle 恒返回无效句柄并早退，NPC 只会追人不会出手。
            WieldFirstLoadoutWeapon();
        }

        /// <summary>
        /// 持握装载表里的第一件武器（对应 UE5 NPC 蓝图 BeginPlay 的装备步骤）。
        /// 只走 <c>NarrativeInventoryComponent.UseItemOfClass</c> —— 与玩家快捷换武同一条通路
        /// （<c>UnrealFramework/NarrativePlayerController.cs</c> ProcessQuickWield），
        /// 非武器类 ID 不会被误用（不会吃掉补给品）。
        /// </summary>
        public virtual bool WieldFirstLoadoutWeapon()
        {
            var inventory = GetInventoryComponent();
            if (inventory == null) return false;

            foreach (var roll in GetDefaultItemLoadout())
            {
                if (roll == null || roll.ItemsToGrant == null) continue;

                foreach (var entry in roll.ItemsToGrant)
                {
                    if (entry == null || string.IsNullOrEmpty(entry.ItemClassId)) continue;

                    // 只在背包里确实存在且是武器时才持握：UseItemOfClass 会触发物品的 Use()
                    if (inventory.FindItemOfClass(entry.ItemClassId) is NarrativePro.Items.WeaponItem)
                    {
                        return inventory.UseItemOfClass(entry.ItemClassId);
                    }
                }
            }

            return false;
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
        public override NarrativeInteractionComponent GetInteractionComponent()
        {
            // 原来恒返回 null：NPC 侧交互/对话入口因此全部空转。
            // 基类现在会在 OnEnable 构建 NPCInteractionComponent，这里直接复用其解析结果。
            return base.GetInteractionComponent();
        }

        /// <summary>NPC 角色使用 <see cref="NPCInteractionComponent"/>（对应 UE5 NPC 侧类默认交互对象）。</summary>
        protected override NarrativeInteractionComponent CreateInteractionComponent()
        {
            return Actor.AddScript<NPCInteractionComponent>();
        }

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

            // Flax-已实现: 通过 NarrativeGameState 阵营联盟图查询（对应 UE5 GetWorldState()->GetTeamAttitudeTowards）
            var gameState = NarrativeGameState.GetCurrent();
            if (gameState == null) return 1; // 中立
            // 目标阵营走共用解析器：角色用定义 DefaultFactions，宿主玩家用其 NarrativePlayerState。
            // 否则宿主玩家（无 NarrativeCharacter 定义）阵营恒空 → 被阵营化 NPC 视为「一方有阵营」的敌对目标。
            var targetFactions = NarrativePlayerController.ResolveActorFactions(other);
            var attitude = gameState.GetFactionsAttitudeTowardsFactions(NPCDefinition?.DefaultFactions ?? new GameplayTagContainer(), targetFactions);
            return (byte)attitude; // ETeamAttitude: Friendly=0/Neutral=1/Hostile=2，与返回值口径一致
        }

        // ===== 标签对话 =====

        /// <summary>
        /// 尝试播放标签对话。对应 UE5 PlayTaggedDialogue（NarrativeNPCCharacter.cpp:536-557）：
        /// 逐条比较标签，条目冷却（FTaggedDialogue.Cooldown/LastPlayTime，:548）、
        /// TalesComponent 的按标签冷却表（TaggedDialogueCooldowns/FallbackCooldown，
        /// TalesComponent.h:276-287，端口 TalesComponent.cs:827-866）、距离与 Required/Blocked 标签
        /// 全部通过才播出并记录播放时间。返回是否实际播出。</summary>
        /// <param name="tag">对话触发标签（Narrative.TaggedDialogue 分类）。</param>
        /// <param name="dialogueInstigator">对话发起者。</param>
        public virtual bool PlayTaggedDialogue(GameplayTag tag, Actor dialogueInstigator)
        {
            if (!tag.IsValid()) return false;

            var talesComp = Actor != null ? Actor.GetScript<TalesComponent>() : null;
            TaggedDialogue found = null;
            foreach (var td in TaggedDialogues)
            {
                if (td == null) continue;
                if (td.Tag == tag)
                {
                    // 检查条目冷却（UE5 FTaggedDialogue.Cooldown + LastPlayTime）
                    if (Time.GameTime - td.LastPlayTime < td.Cooldown) return false;
                    // 检查组件级冷却表（SetTaggedDialogueCooldown 可按标签覆写，缺省 FallbackCooldown）
                    if (talesComp != null && !talesComp.CanPlayTaggedDialogue(tag, Time.GameTime)) return false;
                    // 检查距离
                    if (dialogueInstigator != null && Actor != null)
                    {
                        float dist = Vector3.Distance(Actor.Position, dialogueInstigator.Position);
                        if (dist > td.MaxDistance) return false;
                    }
                    // 检查 RequiredTags
                    if (td.RequiredTags != null && !HasAllMatchingGameplayTags(td.RequiredTags)) return false;
                    // 检查 BlockedTags
                    if (td.BlockedTags != null && HasAnyMatchingGameplayTags(td.BlockedTags)) return false;
                    found = td;
                    break;
                }
            }
            if (found == null) return false;

            found.LastPlayTime = Time.GameTime;
            talesComp?.MarkTaggedDialoguePlayed(tag, Time.GameTime);
            ExecutePlayTaggedDialogue(found, dialogueInstigator);
            return true;
        }

        /// <summary>实际执行标签对话播放。对应 UE5 ExecutePlayTaggedDialogue（BlueprintImplementableEvent）。</summary>
        public virtual void ExecutePlayTaggedDialogue(TaggedDialogue dialogue, Actor dialogueInstigator)
        {
            // Flax-已实现: 通过交互者（回退 NPC 自身）的 TalesComponent.BeginDialogue 播放（对应 UE5 BP 播放 Dialogue 类）
            if (dialogue == null || string.IsNullOrEmpty(dialogue.DialoguePath)) return;
            TalesComponent tales = dialogueInstigator != null ? dialogueInstigator.GetScript<TalesComponent>() : null;
            tales ??= Actor != null ? Actor.GetScript<TalesComponent>() : null;
            if (tales != null)
            {
                tales.BeginDialogue(dialogue.DialoguePath);
            }
            else
            {
                NarrativeLog.LogWarning($"[NarrativeNPCCharacter] {GetNPCName()} 播放标签对话失败：未找到 TalesComponent（{dialogue.Tag}）");
            }
        }

        // ===== 死亡处理 =====

        /// <summary>重写：处理死亡/复活。对应 UE5 <c>HandleDeath_Implementation</c>（NarrativeNPCCharacter.cpp:443-460）。</summary>
        protected override void HandleDeath(Actor killedActor, NarrativeAbilitySystemComponent killedActorASC, bool bIsDead)
        {
            base.HandleDeath(killedActor, killedActorASC, bIsDead);

            // 控制器侧的清理不在这里转发：UE5 由 NarrativeNPCController 自己绑 OnDeathStateChanged
            // （NarrativeNPCController.cpp:160-166，端口 AI/NarrativeNPCController.cs:128-134 同构），
            // 再调一次会把 CleanUp 跑两遍。

            // 尸体可搜刮：UE5 的 NPCInteractable 靠存活状态在"对话"与"搜刮"两个分支间切换，
            // 本端口的 bIsAlive 此前没有任何写入点（NPCInteractable.cs:22 声明后无人赋值），
            // 打死 NPC 后交互仍走对话分支，搜刮/掉落入口不可达。复活则反向还回。
            var interactable = NPCInteractableComponent ?? Actor?.GetScript<NPCInteractable>();
            if (interactable != null)
            {
                interactable.bIsAlive = !bIsDead;
            }

            // 死亡 NPC 不再进存档（UE5 :454-458 RemoveSingleActor）：否则读档会按动态重生把尸体重新生成一遍
            if (bIsDead && Actor != null)
            {
                NarrativePro.SaveSystem.SaveSystemStatics.RemoveSingleActor(Actor);
            }
        }

        // ===== 活动配置 =====

        /// <summary>活动配置是否已加载应用（定义变化只应用一次）。</summary>
        [NonSerialized]
        protected bool bActivityConfigurationApplied;

        /// <summary>
        /// 从生成参数覆盖 / NPC 定义 / 活动组件预置路径解析活动配置路径，加载 JSON 并应用；
        /// 随后应用定义中的活动调度列表。对应 UE5 生成时加载 NPCActivityConfiguration 数据资产
        /// 并调用 SetActivityConfiguration（NPCActivityComponent.cpp:601-623）+ 调度派生活动
        /// （AddActivitySchedule，:568-591）。
        /// </summary>
        /// <summary>
        /// NPC 定义/生成参数都没给活动配置路径时使用的默认配置（Content/NarrativePro/Activities/ 下随插件发布）。
        /// </summary>
        public const string DefaultActivityConfigurationPath =
            "Content/NarrativePro/Activities/DefaultNPCActivityConfiguration.json";

        public virtual void ApplyActivityConfigurationFromDefinition()
        {
            if (bActivityConfigurationApplied) return;

            var activityComp = GetActivityComponent();
            string configPath = null;
            var spawnParams = SpawnInfo?.SpawnParams;

            // 路径优先级与 UE5 一致：生成参数显式覆盖 > NPC 定义引用 > 组件上预置的路径
            if (spawnParams != null && spawnParams.bOverride_ActivityConfiguration)
            {
                configPath = spawnParams.ActivityConfigurationPath;
            }
            else if (NPCDefinition != null && !string.IsNullOrEmpty(NPCDefinition.ActivityConfigurationPath))
            {
                configPath = NPCDefinition.ActivityConfigurationPath;
            }
            else if (activityComp != null && !string.IsNullOrEmpty(activityComp.ActivityConfigurationPath))
            {
                configPath = activityComp.ActivityConfigurationPath;
            }
            else
            {
                // UE5 的 NPC 定义必然引用一份数据资产；本项目的定义是 JSON 路径字符串，可能留空。
                // 留空时退回默认配置，否则新 NPC 会带着空活动表跑，整套日程/评分逻辑形同不存在。
                configPath = DefaultActivityConfigurationPath;
            }

            if (!string.IsNullOrEmpty(configPath))
            {
                var config = NPCActivityConfigurationLoader.Load(configPath);
                if (config != null)
                {
                    ApplyActivityConfig(config);
                    bActivityConfigurationApplied = true;

                    // 记录生效路径到组件，供存档诊断与后续 RemoveActivitySchedule 对照
                    if (activityComp != null && string.IsNullOrEmpty(activityComp.ActivityConfigurationPath))
                    {
                        activityComp.ActivityConfigurationPath = configPath;
                    }
                }
            }

            ApplyActivitySchedules(NPCDefinition?.ActivitySchedulePaths);
        }

        /// <summary>应用活动配置。对应 UE5 ApplyActivityConfig_Implementation。</summary>
        public virtual void ApplyActivityConfig(NPCActivityConfiguration npcActivityConfig)
        {
            if (npcActivityConfig == null) return;
            var activityComp = GetActivityComponent();
            if (activityComp != null)
            {
                // Flax-已实现: 委托 NPCActivityComponent.SetActivityConfiguration（对应 UE5 ActivityComp->SetActivityConfiguration）
                activityComp.SetActivityConfiguration(npcActivityConfig);
            }
        }

        /// <summary>
        /// 应用活动调度。对应 UE5 ApplyActivitySchedules_Implementation：
        /// 逐条加载 UNPCActivitySchedule 资产并 AddActivitySchedule（组件内复制行为实例、
        /// 绑定昼夜事件，到点自动加/移除调度目标）。Flax 走本插件 JSON 数据约定
        /// （<see cref="NPCActivityConfigurationLoader.LoadSchedule"/>）。
        /// </summary>
        /// <param name="activitySchedulePaths">活动调度路径列表。</param>
        public virtual void ApplyActivitySchedules(List<string> activitySchedulePaths)
        {
            if (activitySchedulePaths == null || activitySchedulePaths.Count == 0) return;
            var activityComp = GetActivityComponent();
            if (activityComp == null)
            {
                NarrativeLog.LogWarning($"[NarrativeNPCCharacter] {GetNPCName()} 无 NPCActivityComponent，{activitySchedulePaths.Count} 个活动调度未应用");
                return;
            }

            foreach (var schedulePath in activitySchedulePaths)
            {
                if (string.IsNullOrEmpty(schedulePath)) continue;
                var schedule = NPCActivityConfigurationLoader.LoadSchedule(schedulePath);
                if (schedule == null) continue;
                if (string.IsNullOrEmpty(schedule.ScheduleAssetPath)) schedule.ScheduleAssetPath = schedulePath;
                activityComp.AddActivitySchedule(schedule);
            }
        }

        /// <summary>应用对话。对应 UE5 ApplyDialogue_Implementation。</summary>
        /// <param name="npcDialoguePath">NPC 对话路径。</param>
        public virtual void ApplyDialogue(string npcDialoguePath)
        {
            if (NPCInteractableComponent != null)
            {
                // Flax-已实现: 写入 NPCInteractable.DialoguePath（对应 UE5 Interactable->DialoguePath）
                NPCInteractableComponent.DialoguePath = npcDialoguePath ?? "";
            }
        }

        // ===== 默认数据获取（供基类 OnDefinitionSet 调用）=====

        /// <summary>获取默认物品装载（重写基类虚函数：NPC 从定义/生成参数获取，UE5 GetDefaultItemLoadout BP 虚函数等价）。</summary>
        public override List<LootTableRoll> GetDefaultItemLoadout()
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

        // ===== 触发器注册表（UE5 ANarrativeCharacter 触发器生命周期的 NPC 侧宿主） =====

        /// <summary>
        /// 应用角色定义/生成参数里的触发器集。对应 UE5 OnCharacterDefinitionDataLoaded 的
        /// 「2. Apply any trigger sets」+ ApplyTriggerSets_Implementation
        /// （NarrativeCharacter.cpp:535-556、1877-1891）：逐集逐模板 AddTrigger。
        /// 定义未显式引用 TriggerSetPaths 时，回退到触发器 JSON 数据侧的 appliesToNpcIds 绑定
        /// （Flax 无 TSoftObjectPtr 资产引用的等价通路，见 TriggerSet.AppliesToNpcIds 注释）。
        /// </summary>
        public virtual void ApplyTriggerSetsFromDefinition()
        {
            if (Triggers == null) Triggers = new List<NarrativeTrigger>();

            List<Tales.TriggerSet> sets = null;
            var paths = GetDefaultTriggerSetPaths();
            if (paths != null && paths.Count > 0)
            {
                sets = new List<Tales.TriggerSet>();
                foreach (var path in paths)
                {
                    var set = NarrativePro.Data.NarrativeDataLoader.LoadTriggerSet(path);
                    if (set == null)
                    {
                        NarrativeLog.LogWarning($"[NarrativeNPCCharacter] {GetNPCName()} 触发器集加载失败: {path}");
                        continue;
                    }
                    sets.Add(set);
                }
            }
            else
            {
                sets = NarrativePro.Data.NarrativeDataLoader.GetTriggerSetsForNpc(NPCDefinition?.NPCID);
            }

            if (sets == null) return;
            foreach (var set in sets)
            {
                if (set?.Triggers == null) continue;
                foreach (var template in set.Triggers)
                {
                    AddTrigger(template);
                }
            }
        }

        /// <summary>
        /// 复制触发器模板、绑定宿主角色并初始化。对应 UE5 AddTrigger
        /// （NarrativeCharacter.cpp:1921-1935：DuplicateObject → OwnerCharacter → Initialize → Triggers.Add）。
        /// </summary>
        public virtual NarrativeTrigger AddTrigger(NarrativeTrigger template)
        {
            if (template == null) return null;

            var newTrigger = template.CopyForOwner(Actor);
            if (newTrigger == null) return null;

            Triggers.Add(newTrigger);
            newTrigger.Initialize();
            return newTrigger;
        }

        /// <summary>移除触发器并解绑其运行时注册。对应 UE5 RemoveTrigger（NarrativeCharacter.cpp:1938-1946）。</summary>
        public virtual bool RemoveTrigger(NarrativeTrigger trigger)
        {
            if (trigger == null) return false;
            trigger.Unbind();
            return Triggers.Remove(trigger);
        }

        /// <summary>解绑并清空全部触发器（OnDisable 调用；UE5 里由对象销毁自动完成）。</summary>
        public void ClearTriggers()
        {
            if (Triggers == null) return;
            for (int i = 0; i < Triggers.Count; i++)
            {
                Triggers[i]?.Unbind();
            }
            Triggers.Clear();
        }

        /// <summary>
        /// 从 TaggedDialogueSetPath（或数据侧 appliesToNpcIds 回退绑定）加载标签对话。
        /// 对应 UE5 NPC.cpp:392-397（NPCDef-&gt;TaggedDialogueSet.LoadSynchronous → TaggedDialogues = DSet-&gt;TaggedDialogues）。
        /// </summary>
        public virtual void LoadTaggedDialoguesFromDefinition()
        {
            var def = NPCDefinition;
            if (def == null) return;

            Tales.TaggedDialogueSet assetSet = null;
            if (!string.IsNullOrEmpty(def.TaggedDialogueSetPath))
            {
                assetSet = NarrativePro.Data.NarrativeDataLoader.LoadTaggedDialogueSet(def.TaggedDialogueSetPath);
            }
            else
            {
                assetSet = NarrativePro.Data.NarrativeDataLoader.GetTaggedDialogueSetForNpc(def.NPCID);
            }
            if (assetSet?.TaggedDialogues == null || assetSet.TaggedDialogues.Count == 0) return;

            int added = 0;
            foreach (var entry in assetSet.TaggedDialogues)
            {
                if (entry == null || !entry.Tag.IsValid() || string.IsNullOrEmpty(entry.DialoguePath)) continue;

                TaggedDialogues.Add(new TaggedDialogue
                {
                    Tag = entry.Tag,
                    DialoguePath = entry.DialoguePath,
                    Cooldown = entry.Cooldown,
                    MaxDistance = entry.MaxDistance,
                    RequiredTags = entry.RequiredTags ?? new GameplayTagContainer(),
                    BlockedTags = entry.BlockedTags ?? new GameplayTagContainer(),
                    LastPlayTime = -10000f,
                });
                added++;
            }

            if (added > 0)
            {
                NarrativeLog.Log($"[NarrativeNPCCharacter] {GetNPCName()} 载入标签对话 {added} 条（TaggedDialogueSet）。");
            }
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
            // 端口中 NPC 的状态收集由 SaveSystem/NarrativeSavableActor 与 NarrativeSaveSubsystem 的统一记录路径完成，
            // 本方法保留为派生类的额外收集扩展点（对应 UE5 BP 覆写位），无独立状态需要在此收集。
        }

        /// <summary>加载存档。对应 UE5 Load_Implementation。</summary>
        public virtual void Load()
        {
            // 端口中 NPC 的存档恢复由 SaveSystem（StableActor GUID → ActorRecords）在生成后回灌，
            // 本方法保留为派生类的恢复扩展点（对应 UE5 BP 覆写位），无独立状态需要在此恢复。
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

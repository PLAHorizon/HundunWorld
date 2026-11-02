using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Character;
using NarrativePro.Core;
using NarrativePro.UnrealFramework;

namespace NarrativePro.AI
{
    /// <summary>
    /// 角色数组包装。对应 UE5 FCharacterArray。
    /// 因 TMap 不能直接存储 TArray，需用结构体包装。
    /// </summary>
    [Serializable]
    public class CharacterArray
    {
        public List<Actor> Characters = new List<Actor>();

        public bool HasValidCharacters()
        {
            foreach (var c in Characters)
            {
                if (c != null) return true;
            }
            return false;
        }
    }

    /// <summary>
    /// NPC 数组包装。对应 UE5 FNPCArray。
    /// </summary>
    [Serializable]
    public class NPCArray
    {
        public List<Actor> NPCs = new List<Actor>();

        public bool HasValidNPCs()
        {
            foreach (var n in NPCs)
            {
                if (n != null) return true;
            }
            return false;
        }
    }

    /// <summary>
    /// 请求 NPC 生成完成回调签名。对应 UE5 FOnRequestedNPCSpawned。
    /// </summary>
    /// <param name="npcData">NPC 数据资产</param>
    /// <param name="character">生成的 NPC 角色</param>
    public delegate void OnRequestedNPCSpawned(NPCDefinition npcData, Actor character);

    /// <summary>
    /// 角色子系统。对应 UE5 UNarrativeCharacterSubsystem。
    /// UE5 中继承 UWorldSubsystem；Flax 中改为单例模式（静态 Instance）。
    /// 负责：NPC 生成、查找、缓存、唯一性检查。
    /// 通过 TMap 缓存定义→角色映射，避免昂贵的 GetAllActorsOfClass 调用。
    /// </summary>
    public class NarrativeCharacterSubsystem : Script
    {
        /// <summary>单例实例</summary>
        public static NarrativeCharacterSubsystem Instance { get; private set; }

        /// <summary>NPC 生成完成事件</summary>
        public event OnRequestedNPCSpawned OnNPCSpawned;

        /// <summary>角色定义 → 角色数组映射</summary>
        private Dictionary<CharacterDefinition, CharacterArray> _characterMap = new Dictionary<CharacterDefinition, CharacterArray>();

        /// <summary>NPCID → NPC 数组映射</summary>
        private Dictionary<string, NPCArray> _npcMap = new Dictionary<string, NPCArray>();

        public override void OnEnable()
        {
            base.OnEnable();
            if (Instance != null && Instance != this)
            {
                NarrativeLog.LogWarning("已存在 NarrativeCharacterSubsystem 实例，当前实例将被销毁");
                Destroy(this);
                return;
            }
            Instance = this;
        }

        /// <summary>
        /// 缺陷5：内容驱动攀爬物的落地位。角色子系统由运行期引导
        /// （<c>CommonUI/NarrativeRuntimeBootstrap</c>）挂在宿主 Actor 上，是插件里最早稳定可用的时机；
        /// 首帧把 <c>Content/NarrativePro/Climbables/*.json</c> 生成出来，NPC 在第一次攀爬尝试之前
        /// 就有可抓的攀爬物（与 <c>Spawners/NPCCrowdSpawner</c> 在 OnUpdate 里代码生成 NPC 同一套路）。
        /// <see cref="NarrativePro.World.NarrativeClimbableSpawner.EnsureSpawnedFromContent"/> 是一次性幂等，
        /// 后续每帧只剩一次布尔判断，不做任何重复扫盘。
        /// </summary>
        public override void OnUpdate()
        {
            base.OnUpdate();
            NarrativePro.World.NarrativeClimbableSpawner.EnsureSpawnedFromContent();
        }

        public override void OnDisable()
        {
            if (Instance == this)
            {
                Instance = null;
            }
            base.OnDisable();
        }

        /// <summary>清理所有失效的角色引用</summary>
        public void CleanupInvalidReferences()
        {
            foreach (var kvp in _characterMap)
            {
                kvp.Value.Characters.RemoveAll(c => c == null);
            }
            foreach (var kvp in _npcMap)
            {
                kvp.Value.NPCs.RemoveAll(n => n == null);
            }
        }

        /// <summary>销毁指定 NPC</summary>
        public bool DestroyNPC(Actor npc)
        {
            if (npc == null) return false;
            // 从缓存中移除
            UnregisterCharacter(npc);
            Destroy(npc);
            return true;
        }

        /// <summary>
        /// 生成 NPC。
        /// </summary>
        /// <param name="npcData">NPC 定义数据资产</param>
        /// <param name="spawnTransform">生成变换（可选）</param>
        /// <param name="spawnParams">生成参数</param>
        /// <returns>生成的 NPC 角色 Actor</returns>
        public Actor SpawnNPC(NPCDefinition npcData, Transform spawnTransform = default, NPCSpawnParams spawnParams = null)
        {
            if (npcData == null)
            {
                NarrativeLog.LogWarning("SpawnNPC 失败：npcData 为空");
                return null;
            }

            // 检查唯一性
            if (!npcData.bAllowMultipleInstances)
            {
                var existing = FindNPC(npcData);
                if (existing != null)
                {
                    NarrativeLog.Log($"NPC {npcData.NPCName} 已存在唯一实例，返回现有实例");
                    return existing;
                }
            }

            return SpawnNPC_Internal(npcData, spawnTransform, spawnParams);
        }

        /// <summary>查找或生成 NPC</summary>
        public Actor FindOrSpawnNPC(NPCDefinition npcData, Transform spawnTransform = default)
        {
            if (npcData == null) return null;
            var existing = FindNPC(npcData);
            if (existing != null) return existing;
            return SpawnNPC(npcData, spawnTransform);
        }

        /// <summary>通过 NPC 定义查找 NPC</summary>
        public Actor FindNPC(NPCDefinition npcData)
        {
            if (npcData == null) return null;
            if (_npcMap.TryGetValue(npcData.NPCID, out var npcArray))
            {
                foreach (var npc in npcArray.NPCs)
                {
                    if (npc != null) return npc;
                }
            }
            return null;
        }

        /// <summary>通过 NPC 定义查找 NPC（带成功标志）</summary>
        public Actor FindNPCWithStatus(NPCDefinition npcData, out bool bOutSucceeded)
        {
            bOutSucceeded = false;
            if (npcData == null) return null;
            if (_npcMap.TryGetValue(npcData.NPCID, out var npcArray))
            {
                foreach (var npc in npcArray.NPCs)
                {
                    if (npc != null)
                    {
                        bOutSucceeded = true;
                        return npc;
                    }
                }
            }
            return null;
        }

        /// <summary>通过 NPCID 查找 NPC（高效 TMap 查询）</summary>
        public Actor FindNPCByID(string npcId)
        {
            if (string.IsNullOrEmpty(npcId)) return null;
            if (_npcMap.TryGetValue(npcId, out var npcArray))
            {
                foreach (var npc in npcArray.NPCs)
                {
                    if (npc != null) return npc;
                }
            }
            return null;
        }

        /// <summary>通过角色定义查找角色</summary>
        public Actor FindCharacter(CharacterDefinition characterDefinition)
        {
            if (characterDefinition == null) return null;
            if (_characterMap.TryGetValue(characterDefinition, out var charArray))
            {
                foreach (var c in charArray.Characters)
                {
                    if (c != null) return c;
                }
            }
            return null;
        }

        /// <summary>查找所有指定类型的 NPC</summary>
        public void FindNPCs(NPCDefinition npcData, List<Actor> outActors)
        {
            if (npcData == null || outActors == null) return;
            if (_npcMap.TryGetValue(npcData.NPCID, out var npcArray))
            {
                foreach (var npc in npcArray.NPCs)
                {
                    if (npc != null) outActors.Add(npc);
                }
            }
        }

        /// <summary>查找所有指定定义的角色</summary>
        public void FindCharacters(CharacterDefinition characterDefinition, List<Actor> outActors)
        {
            if (characterDefinition == null || outActors == null) return;
            if (_characterMap.TryGetValue(characterDefinition, out var charArray))
            {
                foreach (var c in charArray.Characters)
                {
                    if (c != null) outActors.Add(c);
                }
            }
        }

        /// <summary>查询指定角色是否已生成</summary>
        public bool IsCharacterSpawned(CharacterDefinition characterDefinition)
        {
            if (characterDefinition == null) return false;
            if (_characterMap.TryGetValue(characterDefinition, out var charArray))
            {
                return charArray.HasValidCharacters();
            }
            return false;
        }

        /// <summary>内部生成 NPC 实现</summary>
        protected virtual Actor SpawnNPC_Internal(NPCDefinition npcData, Transform spawnTransform, NPCSpawnParams spawnParams)
        {
            Actor npcActor = null;

            // 路径①：定义给出的 Prefab（UE5 的 TSubclassOf&lt;ANarrativeNPCCharacter&gt; 等价物）
            if (!string.IsNullOrEmpty(npcData.NPCClassPath))
            {
                Prefab prefab = Content.LoadAsync<Prefab>(npcData.NPCClassPath);
                if (prefab != null)
                {
                    npcActor = PrefabManager.SpawnPrefab(prefab, spawnTransform.Translation, spawnTransform.Orientation);
                    // SpawnPrefab 默认落 Scenes[0]（RootScene），NPC 属玩法世界对象，搬入玩法场景
                    NarrativePro.CommonUI.NarrativeRuntimeBootstrap.MoveToGameplayScene(npcActor, $"NPC_{npcData.NPCName}");
                }
                else
                {
                    NarrativeLog.LogWarning($"NPC {npcData.NPCName} 的 Prefab 不可用（{npcData.NPCClassPath}），改用代码装配的 NPC Actor。");
                }
            }

            // 路径②（兜底）：本工程 Content 下没有任何 .flaxpref 插件预制体，
            // UE5「NPC 蓝图类」在 Flax 侧没有对应资产；若因此返回 null，
            // 定义 → SetNPCDefinition → 活动配置/调度 → 目标评分 → 装备/战利品整条链路都无从触达。
            // 与本地玩家的既有做法一致（本游戏同样由代码生成玩家），这里就地装配一个可用的 NPC Actor。
            if (npcActor == null)
            {
                npcActor = BuildNpcActor(npcData, spawnTransform);
            }

            if (npcActor == null)
            {
                NarrativeLog.LogError($"生成 NPC Actor 失败：{npcData.NPCName}");
                return null;
            }

            // 预制体路径的 Actor 未必带齐 NPC 脚本，统一在此补齐（幂等：已存在只复用）
            EnsureNpcRuntimeScripts(npcActor, npcData, spawnParams);

            RegisterCharacter(npcActor, npcData);
            OnNPCSpawned?.Invoke(npcData, npcActor);
            return npcActor;
        }

        /// <summary>角色宿主碰撞体高度，与游戏工程兜底碰撞体口径一致（HundunWorldGame.cs:2772-2775）。</summary>
        public const float HostColliderHeight = 1.8f;

        /// <summary>
        /// 幂等地保证角色宿主带一个物理碰撞体。
        /// <para>
        /// 为什么必须存在：代码装配的 NPC 宿主是 <see cref="FlaxEngine.EmptyActor"/>，
        /// 预制体路径的宿主也未必带碰撞体；而战斗命中全部走物理查询
        /// （<c>NarrativeNPCController.FindBestAttackTarget</c> 的 <c>Physics.OverlapSphere</c>、
        /// <c>NarrativeCombatAbility</c> 的 RayCast/SphereCastAll、
        /// <c>TargetingTransformProvider</c> 的 LineCast）——没有碰撞体的角色对战斗完全隐形，
        /// NPC-vs-NPC 与 NPC 攻击玩家（含预制体路径的玩家宿主）都不可能成立。
        /// </para>
        /// <para>
        /// 创建方式复刻宿主工程已验证可用的写法（<c>Source/Game/HundunWorldGame.cs:2772</c>：
        /// <c>root.AddChild&lt;BoxCollider&gt;()</c> + <c>Size</c>/<c>Center</c>，不动层）。
        /// 保持宿主默认层与工程兜底碰撞体一致：本插件战斗查询默认掩码为 <c>uint.MaxValue</c>
        /// （<c>FindBestAttackTarget</c>、<c>Physics.RayCast</c> 无掩码重载），
        /// 且 <c>NarrativeGASStatics.GetWeaponTraceMask()</c> 在未配置 WeaponTraceChannel 时同为全掩码，
        /// 默认层碰撞体对这三类查询都可见。
        /// </para>
        /// </summary>
        public static void EnsureHostCollider(Actor host, float height = HostColliderHeight)
        {
            if (host == null || height <= 0f) return;
            if (HasColliderRecursive(host, 0)) return;

            var collider = host.AddChild<FlaxEngine.BoxCollider>();
            collider.Name = "CombatCollider";
            collider.Size = new Vector3(height / 3f, height, height / 3f);
            collider.Center = new Vector3(0f, height * 0.5f, 0f);
        }

        /// <summary>沿子树查找任意 Collider（碰撞体是 Actor 而非 Script，见 Navigation/MapTileBounds.cs:239）。</summary>
        private static bool HasColliderRecursive(Actor actor, int depth)
        {
            if (actor == null || depth > 8) return false;
            var children = actor.Children;
            if (children == null) return false;
            for (int i = 0; i < children.Length; i++)
            {
                var child = children[i];
                if (child == null) continue;
                if (child is FlaxEngine.Collider) return true;
                if (HasColliderRecursive(child, depth + 1)) return true;
            }
            return false;
        }

        /// <summary>
        /// 创建一个空 Actor 作为 NPC 宿主。对应 UE5 <c>World-&gt;SpawnActor(NPCClass, Transform)</c>。
        /// Flax 约定：<c>Level.SpawnActor</c> 返回 <c>true</c> 才是失败。
        /// </summary>
        private static Actor BuildNpcActor(NPCDefinition npcData, Transform spawnTransform)
        {
            var actor = new FlaxEngine.EmptyActor
            {
                Name = $"NPC_{npcData.NPCName}",
            };

            // 世界对象必须显式生成进玩法场景：无父级版 SpawnActor 固定落 Scenes[0]（RootScene）
            if (NarrativePro.CommonUI.NarrativeRuntimeBootstrap.TrySpawnToGameplayScene(actor, actor.Name))
            {
                NarrativeLog.LogError($"NPC Actor 生成失败：{npcData.NPCName}");
                FlaxEngine.Object.Destroy(actor);
                return null;
            }

            actor.Position = spawnTransform.Translation;
            actor.Orientation = spawnTransform.Orientation;

            // 裸 EmptyActor 对物理查询隐形 —— 生成即补碰撞体，NPC 才能被攻击/互相索敌
            EnsureHostCollider(actor);
            return actor;
        }

        /// <summary>
        /// 把一个 Actor 装配成可运行的 NPC（等价 UE5 的 NPC 蓝图类默认对象 + PostInitializeComponents）。
        /// <para>顺序敏感：</para>
        /// <list type="number">
        /// <item><c>NarrativeNPCCharacter</c> 先建 —— 它继承 <c>NarrativeCharacter</c>，
        /// <c>EnsureSubobjects</c> 会补 ASC / 属性集 / 背包 / 装备 / 交互 / Tales / 移动 / 视觉 / foley；</item>
        /// <item><c>NPCActivityComponent</c> 次建 —— <c>NarrativeNPCController.OnEnable</c>
        /// 按 <c>Actor.GetScript&lt;NPCActivityComponent&gt;()</c> 缓存活动组件并写 OwnerControllerId，
        /// 反序则活动组件上的控制器引用为空，日程目标找不到归属、活动永不启动；</item>
        /// <item>最后 <c>SetNPCDefinition</c>：触发 OnDefinitionSet（灌阵营/拥有标签、等级、默认装载）
        /// 与 ApplyActivityConfigurationFromDefinition（活动配置 + 活动调度）。</item>
        /// </list>
        /// </summary>
        protected virtual void EnsureNpcRuntimeScripts(Actor npcActor, NPCDefinition npcData, NPCSpawnParams spawnParams)
        {
            if (npcActor == null || npcData == null) return;

            // 预制体路径同样要保碰撞体：SpawnNPC_Internal 对两条路径都走这里（幂等）
            EnsureHostCollider(npcActor);

            // UE5 的 NPC 一定带一个 UNPCInteractable（对话/搜刮/交易的唯一入口组件）。
            // 必须先于 NarrativeNPCCharacter 挂载：它的 OnEnable 用
            // Actor.GetScript<NPCInteractable>() 缓存交互组件（NarrativeNPCCharacter.cs:198），
            // 反序则 ApplyDialogue 拿不到实例，对话路径永远写不进交互组件。
            EnsureNpcInteractable(npcActor, npcData);

            var character = npcActor.GetScript<NarrativePro.UnrealFramework.NarrativeNPCCharacter>();
            if (character == null)
            {
                character = npcActor.AddScript<NarrativePro.UnrealFramework.NarrativeNPCCharacter>();
            }
            character?.EnsureSubobjects();

            if (spawnParams != null && character != null)
            {
                // 生成参数覆盖项交给 SpawnInfo，ApplyActivityConfigurationFromDefinition 会按
                // 「生成参数 > 定义 > 组件预置」的 UE5 优先级读取它。
                character.SpawnInfo.SpawnParams = spawnParams;
            }

            var activityComponent = npcActor.GetScript<Activities.NPCActivityComponent>();
            if (activityComponent == null)
            {
                activityComponent = npcActor.AddScript<Activities.NPCActivityComponent>();
            }

            var controller = npcActor.GetScript<NarrativeNPCController>();
            if (controller == null)
            {
                controller = npcActor.AddScript<NarrativeNPCController>();
            }

            if (controller != null)
            {
                controller.NPCData = npcData;

                // OnEnable 已经双向链接时这里只是兜底（预制体里两者顺序未知）
                if (controller.NPCActivityComponent == null) controller.NPCActivityComponent = activityComponent;
                if (activityComponent != null)
                {
                    activityComponent.OwnerController = controller;
                    if (string.IsNullOrEmpty(activityComponent.OwnerControllerId))
                        activityComponent.OwnerControllerId = controller.ID.ToString();
                }
            }

            if (character != null)
            {
                character.SetCachedNPCController(controller);
                character.ApplyDialogue(npcData.DialoguePath);

                if (!ReferenceEquals(character.GetNPCDefinition(), npcData))
                {
                    character.SetNPCDefinition(npcData);
                }

                // 定义在装载发放（InitNewCharacter）之后才配商人角色：
                // 商人钱包/买卖比例覆盖默认值，且此时库存已经成型。
                if (npcData.bIsVendor)
                {
                    ApplyVendorRole(character, npcData);
                }
            }
        }

        /// <summary>
        /// 给 NPC Actor 补上 <see cref="NPCInteractable"/> 并按定义配置它（幂等，预制体路径自带时只覆写文案）。
        /// 这是 NPC 对话/搜刮/交易进入交互系统（<see cref="NarrativePro.Interaction.PlayerInteractionComponent"/>）
        /// 的唯一挂载点。
        /// </summary>
        protected virtual NPCInteractable EnsureNpcInteractable(Actor npcActor, NPCDefinition npcData)
        {
            if (npcActor == null || npcData == null) return null;

            var interactable = npcActor.GetScript<NPCInteractable>();
            if (interactable == null)
            {
                interactable = npcActor.AddScript<NPCInteractable>();
            }
            if (interactable == null)
            {
                NarrativeLog.LogWarning($"NPC {npcData.NPCName} 无法挂载 NPCInteractable，交互入口不可达。");
                return null;
            }

            interactable.InteractableNameText = npcData.NPCName;
            interactable.bOffersTrade = npcData.bIsVendor;
            interactable.TradeShopName = npcData.ShopFriendlyName ?? "";
            interactable.InteractableActionText = !string.IsNullOrEmpty(npcData.DialoguePath)
                ? "交谈"
                : npcData.bIsVendor ? "交易" : "交互";

            if (string.IsNullOrEmpty(interactable.DialoguePath))
            {
                interactable.DialoguePath = npcData.DialoguePath ?? "";
            }

            return interactable;
        }

        /// <summary>
        /// 按 NPC 定义把背包升级为商人背包（对应 UE5 商人 NPC 上挂的 UVendorInventoryComponent）：
        /// <c>bIsVendor</c> 打开后，<c>NarrativeInventoryComponent.AllowLootItem/AllowStoreItem</c>
        /// 会走商人校验与货币结算；库存即该背包本身（由 DefaultItemLoadout / TradingItemLoadout 灌入）。
        /// </summary>
        protected virtual NarrativePro.Items.NarrativeInventoryComponent ApplyVendorRole(
            NarrativePro.UnrealFramework.NarrativeNPCCharacter character, NPCDefinition npcData)
        {
            var inventory = character?.GetInventoryComponent();
            if (inventory == null)
            {
                NarrativeLog.LogWarning($"商人 NPC {npcData.NPCName} 没有背包组件，商人角色无法生效。");
                return null;
            }

            inventory.SetIsVendor(true);
            inventory.SetBuyPercentage(npcData.BuyItemPercentage);
            inventory.SetSellPercentage(npcData.SellItemPercentage);
            if (npcData.TradingCurrency != 0)
            {
                // 商人钱包以 TradingCurrency 为准（UE5 商店组件的初始货币）
                inventory.SetCurrency(npcData.TradingCurrency);
            }
            if (!string.IsNullOrEmpty(npcData.ShopFriendlyName))
            {
                inventory.SetInventoryFriendlyName(npcData.ShopFriendlyName);
            }

            // 专供出售的货架（数据未填时为空调用，不影响 DefaultItemLoadout 已发的装载）
            if (npcData.TradingItemLoadout != null && npcData.TradingItemLoadout.Count > 0)
            {
                var results = new List<NarrativePro.Items.ItemAddResult>();
                foreach (var roll in npcData.TradingItemLoadout)
                {
                    if (roll == null) continue;
                    inventory.TryAddFromLootTable(roll, results);
                }
            }

            return inventory;
        }

        /// <summary>注册角色到缓存</summary>
        public void RegisterCharacter(Actor character, NPCDefinition npcData = null)
        {
            if (character == null) return;

            if (npcData != null)
            {
                if (!_npcMap.TryGetValue(npcData.NPCID, out var npcArray))
                {
                    npcArray = new NPCArray();
                    _npcMap[npcData.NPCID] = npcArray;
                }
                npcArray.NPCs.Add(character);
            }
        }

        /// <summary>从缓存注销角色</summary>
        public void UnregisterCharacter(Actor character)
        {
            if (character == null) return;
            foreach (var kvp in _npcMap)
            {
                kvp.Value.NPCs.Remove(character);
            }
            foreach (var kvp in _characterMap)
            {
                kvp.Value.Characters.Remove(character);
            }
        }
    }
}


using System;
using FlaxEngine;
using NarrativePro.AI;
using NarrativePro.AI.Activities;
using NarrativePro.Core;
using NarrativePro.GAS;
using NarrativePro.SaveSystem.Subsystems;
using NarrativePro.UnrealFramework;

namespace NarrativePro.Spawners
{
    /// <summary>
    /// NPC 生成组件。对应 UE5 UNPCSpawnComponent。
    /// 继承自 SpawnComponent，当被生成器请求时生成一个 NPC。
    /// 支持栓系（tether）：反生成时若玩家在栓系范围内，NPC 会被系留到本地玩家控制器而保留在场景里；
    /// 再次生成时优先通过栓系找回该 NPC（对应 UE5 NPCSpawnComponent.cpp:60-157）。
    /// </summary>
    public class NPCSpawnComponent : SpawnComponent
    {
        /// <summary>
        /// NPC 在此范围内不会被反生成，而是栓系到玩家。
        /// 默认值对齐 UE5 <c>UNPCSpawnComponent</c> 构造函数（NPCSpawnComponent.cpp:18 的 10000.f）。
        /// </summary>
        public float UntetherDistance = SpawnDecisionRules.DefaultUntetherDistance;

        /// <summary>
        /// 若为 true，NPC 此前被击杀则不再生成。
        /// 默认值对齐 UE5 构造函数（NPCSpawnComponent.cpp:19 写死 <c>bDontSpawnIfPreviouslyKilled = true</c>），
        /// 即「生成出来的 NPC 被打死之后默认不再刷新」是 UE5 的默认玩法，不是可选项。
        /// </summary>
        public bool bDontSpawnIfPreviouslyKilled = SpawnDecisionRules.DefaultDontSpawnIfPreviouslyKilled;

        /// <summary>要生成的 NPC 定义。对应 UE5 TObjectPtr&lt;UNPCDefinition&gt; NPCToSpawn。</summary>
        public NPCDefinition NPCToSpawn;

        public override void OnStart()
        {
            base.OnStart();

#if FLAX_EDITOR
            // UE5 在 ANPCSpawner 构造函数里 CreateEditorOnlyDefaultSubobject<UNPCSpawnVisualizerComponent>
            //（NPCSpawner.cpp:30，字段见 NPCSpawner.h:41）。Flax 没有编辑器组件可视化器，
            // 等价通路：仅编辑器目标下给宿主 Actor 挂一个只在 OnDebugDraw 画生成范围的
            // NPCSpawnVisualizerComponent（它会回读本组件的 UntetherDistance）。
            if (Actor != null && Actor.GetScript<NPCSpawnVisualizerComponent>() == null)
            {
                Actor.AddScript<NPCSpawnVisualizerComponent>();
            }
#endif
        }

        /// <summary>生成时使用的可选参数。对应 UE5 FNPCSpawnParams SpawnParams。</summary>
        public NPCSpawnParams SpawnParams = new NPCSpawnParams();

        /// <summary>NPC 生成时分配的可选目标。对应 UE5 TObjectPtr&lt;UNPCGoalItem&gt; OptionalGoal。</summary>
        public NPCGoalItem OptionalGoal;

        /// <summary>
        /// 分配给 NPC 的存档 GUID，使其属性、物品等可被保存。
        /// 仅当 NPC 非唯一且确实需要存档时设置。对应 UE5 NPCSaveGUID。
        /// </summary>
        public Guid NPCSaveGUID = Guid.NewGuid();

        /// <summary>此生成组件创建的 NPC 过去是否被击杀。若被击杀则不再生成。</summary>
        public bool bWasKilled = false;

        /// <summary>NPC 是否已栓系到玩家。</summary>
        public bool bTetheredToPlayer = false;

        /// <summary>
        /// 端口口径开关：没有存档子系统时（未启用存档的会话）仍按常规流程生成。
        /// UE5 在 NPCSpawnComponent.cpp:95 那种情况下不生成任何东西（本次返回 nullptr）。
        /// 这条差异显式命名并传给纯决策层，harness 两种结果都钉住，避免被读成「UE5 等价」。
        /// </summary>
        protected const bool SpawnWhenSaveSubsystemMissing = true;

        /// <summary>
        /// 是否应当生成 Actor。覆盖基类以加入击杀记录判断。对应 UE5 ShouldSpawnActor（:27-47），
        /// 判据本体在 <see cref="SpawnDecisionRules.ShouldSpawnActor"/>。
        /// </summary>
        public override bool ShouldSpawnActor()
        {
            return SpawnDecisionRules.ShouldSpawnActor(SpawnedActor != null, bDontSpawnIfPreviouslyKilled, bWasKilled, NPCToSpawn != null);
        }

        /// <summary>
        /// 是否应当反生成 Actor。覆盖基类（栓系中的 NPC 不应被反生成）。对应 UE5 ShouldDespawnActor（:50-58）
        /// 加上端口必需的「已栓系就不再反生成」（端口的 TetherNPC 没有 UE5 的同 GUID 去重门），
        /// 判据本体在 <see cref="SpawnDecisionRules.ShouldDespawnNpcActor"/>。
        /// </summary>
        public override bool ShouldDespawnActor()
        {
            return SpawnDecisionRules.ShouldDespawnNpcActor(SpawnedActor != null, bTetheredToPlayer);
        }

        /// <summary>
        /// 生成 NPC。对应 UE5 SpawnActor_Implementation（:60-157）：
        /// 处理栓系找回（GetTether/UntetherNPC）、击杀记录判定（DoesRecordExist → bWasKilled）、
        /// SpawnInfo 归属信息（OwningSpawnerGUID/SpawnName/SpawnTransform/SpawnAssignedSaveGUID）、
        /// 死亡回调绑定与可选目标下发。
        /// </summary>
        public override Actor SpawnActor()
        {
            var spawnerOwner = Actor != null ? Actor.GetScript<SpawnerBase>() : null;
            if (spawnerOwner == null)
            {
                // UE5 中该组件必须位于 ASpawnerBase 上（:62）
                NarrativeLog.LogWarning("NPCSpawnComponent: 所在 Actor 上没有 SpawnerBase，无法生成 NPC");
                return null;
            }

            if (NPCToSpawn == null)
            {
                NarrativeLog.LogWarning("NPCSpawnComponent: NPCToSpawn 未配置，无法生成 NPC");
                return null;
            }

            var subsystem = NarrativeCharacterSubsystem.Instance;
            if (subsystem == null)
            {
                NarrativeLog.LogError("NPCSpawnComponent: NarrativeCharacterSubsystem 未就绪，无法生成 NPC");
                return null;
            }

            Transform spawnTransform = GetSpawnTransform();
            Actor npc = null;

            // 唯一 NPC 用定义自带 GUID，非唯一 NPC 用生成组件分配的 GUID（UE5:76）
            string npcGuidString = NPCToSpawn.bAllowMultipleInstances
                ? NPCSaveGUID.ToString()
                : NPCToSpawn.UniqueNPCGUID;

            // 此前被栓系到玩家：先尝试通过栓系找回 NPC 并解系（UE5:74-104）
            // 引擎侧只负责取回这三份事实，走哪条出口由 SpawnDecisionRules.DecideSpawnResume 裁决
            NarrativePlayerController pc = null;
            NPCTether tether = null;
            NarrativeSaveSubsystem saveSubsystem = null;
            bool recordExists = false;

            if (bTetheredToPlayer)
            {
                pc = GetLocalPlayerController();
                if (pc != null)
                {
                    NPCTether found;
                    // 与 UE5 :84 一致：拿不到栓系、或栓系里的 NPC 已经不存在，都算「记录已丢失」
                    if (!pc.GetTether(npcGuidString, out found) || found == null || found.NPCCharacter == null)
                    {
                        tether = null;
                    }
                    else
                    {
                        tether = found;
                    }

                    if (tether == null)
                    {
                        saveSubsystem = NarrativeSaveSubsystem.Instance;
                        if (saveSubsystem != null && TryParseGuid(npcGuidString, out var parsedGuid))
                        {
                            recordExists = saveSubsystem.DoesRecordExist(parsedGuid);
                        }
                    }
                }
            }

            var decision = SpawnDecisionRules.DecideSpawnResume(
                hasSpawnerOwner: true,
                hasDefinition: true,
                hasCharacterSubsystem: true,
                bTetheredToPlayer: bTetheredToPlayer,
                hasPlayerController: pc != null,
                tetherFoundAndValid: tether != null && tether.NPCCharacter != null,
                saveSubsystemPresent: saveSubsystem != null,
                recordExists: recordExists,
                // 端口口径：没有存档子系统的会话里 NPC 仍要能生成（UE5 在此返回 nullptr）
                spawnWhenSaveSubsystemMissing: SpawnWhenSaveSubsystemMissing);

            if (decision.bClearTetheredFlag)
            {
                bTetheredToPlayer = false;
            }

            if (decision.bShouldMarkKilled)
            {
                // 找不到栓系记录、存档里也没有记录 ⇒ NPC 是被击杀且记录已移除
                bWasKilled = true;
            }

            switch (decision.Action)
            {
                case ESpawnResumeAction.ReattachFromTether:
                    npc = tether.NPCCharacter.Actor;
                    pc.UntetherNPC(tether.NPCCharacter);
                    break;

                case ESpawnResumeAction.SpawnNormally:
                case ESpawnResumeAction.SpawnFromSavedRecord:
                case ESpawnResumeAction.SpawnWithoutSaveSubsystem:
                    npc = subsystem.SpawnNPC(NPCToSpawn, spawnTransform, SpawnParams);
                    break;

                default:
                    return null;
            }

            if (npc == null)
            {
                return null;
            }

            var npcCharacter = npc.GetScript<NarrativeNPCCharacter>();
            if (npcCharacter != null)
            {
                // SpawnInfo 归属信息（UE5:116-119）。Flax 的 Script 无 FName，用「宿主 Actor 名 + 组件类型名」表达生成点名。
                npcCharacter.SpawnInfo.OwningSpawnerGUID = spawnerOwner.GetActorGUID().ToString();
                npcCharacter.SpawnInfo.SpawnName = (Actor != null ? Actor.Name : "") + "." + GetType().Name;
                npcCharacter.SpawnInfo.SpawnTransform = spawnTransform;

                // 非唯一 NPC：由生成组件分配存档 GUID，把记录绑定到生成点上（UE5:122-124）
                if (NPCToSpawn.bAllowMultipleInstances)
                {
                    npcCharacter.SpawnInfo.SpawnAssignedSaveGUID = NPCSaveGUID.ToString();
                }
            }

            // 死亡状态绑定：仅在需要「击杀后不再生成」时（UE5:127-133，AddUniqueDynamic OnDeathStateChanged）
            if (SpawnDecisionRules.ShouldBindDeathCallback(bDontSpawnIfPreviouslyKilled))
            {
                var asc = npc.GetScript<NarrativeAbilitySystemComponent>();
                if (asc != null)
                {
                    asc.OnDeathStateChanged -= SpawnedNPCDied;
                    asc.OnDeathStateChanged += SpawnedNPCDied;
                }
            }

            // 可选目标下发（UE5:138-142）
            if (OptionalGoal != null)
            {
                var activityComponent = npc.GetScript<NPCActivityComponent>();
                if (activityComponent != null)
                {
                    activityComponent.AddGoal(OptionalGoal);
                }
            }

            return npc;
        }

        /// <summary>
        /// 尝试反生成 Actor。对应 UE5 TryDespawnActor（:159-181）：
        /// 若玩家处于栓系范围内，不反生成而是把 NPC 栓系到本地玩家控制器，交由玩家管理。
        /// 「栓系范围内就不反生成」这条裁决住在 <see cref="SpawnDecisionRules.DecideDespawnAttempt"/>。
        /// </summary>
        public override bool TryDespawnActor()
        {
            var npcCharacter = SpawnedActor != null ? SpawnedActor.GetScript<NarrativeNPCCharacter>() : null;
            var pc = npcCharacter != null ? GetLocalPlayerController() : null;
            var controlledPawn = pc != null ? pc.OwnedCharacter : null;
            bool hasControlledPawnActor = controlledPawn != null && controlledPawn.Actor != null;

            // 与 UE5 :166-169 的逐层判空一致：只有前面三层都成立才取距离
            float distance = hasControlledPawnActor
                ? Vector3.Distance(controlledPawn.Actor.Position, SpawnedActor.Position)
                : float.MaxValue;

            // 先按「栓系会被接受」问一次纯规则（UE5:171 才真的调 TetherNPC）
            var action = SpawnDecisionRules.DecideDespawnAttempt(npcCharacter != null, pc != null, hasControlledPawnActor,
                distance, UntetherDistance, true);

            if (action == ETetherDespawnAction.TetherToPlayer)
            {
                if (pc.TetherNPC(npcCharacter))
                {
                    bTetheredToPlayer = true;
                    return true;
                }
                // 栓系被拒（UE5 的 TetherNPC 在 GUID 无效或已有同 GUID 栓系时返回 false）→ 退回移除（:181）
            }

            return RemoveActor();
        }

        /// <summary>
        /// 移除已生成的 NPC。对应 UE5 RemoveActor（:184-197）：
        /// 有 NPC 控制器则走 CleanUp（处理交互/行为收尾），否则直接销毁；解绑死亡回调。
        /// 走哪条通路的判据在 <see cref="SpawnDecisionRules.ShouldCleanUpViaController"/>。
        /// </summary>
        public override bool RemoveActor()
        {
            if (SpawnedActor == null) return false;

            Actor npcActor = SpawnedActor;
            SpawnedActor = null;
            bTetheredToPlayer = false;

            var asc = npcActor.GetScript<NarrativeAbilitySystemComponent>();
            if (asc != null)
            {
                asc.OnDeathStateChanged -= SpawnedNPCDied;
            }

            var npcCharacter = npcActor.GetScript<NarrativeNPCCharacter>();
            var npcController = npcActor.GetScript<NarrativeNPCController>();
            if (SpawnDecisionRules.ShouldCleanUpViaController(npcController != null))
            {
                npcController.CleanUp(0f);
            }
            else if (npcCharacter != null || npcActor != null)
            {
                FlaxEngine.Object.Destroy(npcActor, 0f);
            }

            return true;
        }

        /// <summary>编辑器显示标签。</summary>
        public override string GetEditorLabel()
        {
            if (NPCToSpawn != null)
            {
                return $"NPC Spawn ({NPCToSpawn.NPCName})";
            }
            return "NPC Spawn (未配置)";
        }

        /// <summary>登记存档系统用的 NPC Prefab 来源路径（NPC 定义的 NPCClassPath，对应 UE5 登记的生成类路径）。</summary>
        public override string GetSpawnedAssetPath()
        {
            return NPCToSpawn != null ? NPCToSpawn.NPCClassPath : null;
        }

        /// <summary>尝试获取当前已生成的 NPC（若存在）。对应 UE5 GetSpawnedNPC。</summary>
        /// <returns>已生成的 NPC Actor，不存在则返回 null</returns>
        public Actor GetSpawnedNPC()
        {
            return SpawnedActor;
        }

        /// <summary>
        /// 已生成 NPC 死亡回调。对应 UE5 SpawnedNPCDied（:214-221）：
        /// 仅当确实死亡（bIsDead）且死者就是本组件生成的 Actor 时记录击杀状态，
        /// 以便后续不再重生成（当 bDontSpawnIfPreviouslyKilled 为 true 时生效）。
        /// 两条判据的本体在 <see cref="SpawnDecisionRules.ShouldMarkSpawnKilled"/>。
        /// </summary>
        /// <param name="killedActor">被击杀的 Actor</param>
        /// <param name="killedActorASC">被击杀 Actor 的能力系统组件</param>
        /// <param name="bIsDead">当前死亡状态（复活时再次触发且为 false）</param>
        public virtual void SpawnedNPCDied(Actor killedActor, NarrativeAbilitySystemComponent killedActorASC, bool bIsDead)
        {
            if (SpawnDecisionRules.ShouldMarkSpawnKilled(bIsDead, killedActor == SpawnedActor))
            {
                bWasKilled = true;
                NarrativeLog.Log($"NPCSpawnComponent: NPC {killedActor?.Name} 已被击杀，记录击杀状态");
            }
        }

        /// <summary>
        /// 取本地玩家控制器。UE5 用 <c>UGameplayStatics::GetPlayerController(World, 0)</c>，
        /// Flax 无 PlayerController 概念——取场景中第一个已控制角色的 NarrativePlayerController。
        /// </summary>
        protected static NarrativePlayerController GetLocalPlayerController()
        {
            var controllers = Level.GetScripts<NarrativePlayerController>();
            if (controllers == null) return null;
            foreach (var pc in controllers)
            {
                if (pc == null || !pc.Enabled) continue;
                if (pc.OwnedCharacter != null) return pc;
            }
            // 没有已控制角色的控制器时，退回首個启用的控制器（栓系数据仍可能在其上）
            foreach (var pc in controllers)
            {
                if (pc != null && pc.Enabled) return pc;
            }
            return null;
        }

        /// <summary>安全解析存档 GUID 字符串。</summary>
        private static bool TryParseGuid(string guidString, out Guid parsed)
        {
            parsed = Guid.Empty;
            if (string.IsNullOrEmpty(guidString)) return false;
            return Guid.TryParse(guidString, out parsed);
        }
    }
}

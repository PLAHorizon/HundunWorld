using System;
using FlaxEngine;
using NarrativePro.AI;
using NarrativePro.AI.Activities;
using NarrativePro.Core;
using NarrativePro.GAS;

namespace NarrativePro.Spawners
{
    /// <summary>
    /// NPC 生成组件。对应 UE5 UNPCSpawnComponent。
    /// 继承自 SpawnComponent，当被生成器请求时生成一个 NPC。
    /// </summary>
    public class NPCSpawnComponent : SpawnComponent
    {
        /// <summary>NPC 在此范围内不会被反生成，而是栓系到玩家。对应 UE5 UntetherDistance。</summary>
        public float UntetherDistance = 3000f;

        /// <summary>若为 true，NPC 此前被击杀则不再生成。对应 UE5 bDontSpawnIfPreviouslyKilled。</summary>
        public bool bDontSpawnIfPreviouslyKilled = false;

        /// <summary>要生成的 NPC 定义。对应 UE5 TObjectPtr&lt;UNPCDefinition&gt; NPCToSpawn。</summary>
        public NPCDefinition NPCToSpawn;

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

        /// <summary>是否应当生成 Actor。覆盖基类以加入击杀记录判断。</summary>
        public override bool ShouldSpawnActor()
        {
            if (SpawnedActor != null) return false;

            // 若配置了"曾被击杀则不再生成"且 NPC 已被击杀
            if (bDontSpawnIfPreviouslyKilled && bWasKilled)
            {
                return false;
            }

            return true;
        }

        /// <summary>是否应当反生成 Actor。覆盖基类。</summary>
        public override bool ShouldDespawnActor()
        {
            // 已栓系到玩家的 NPC 不反生成
            if (bTetheredToPlayer) return false;
            return SpawnedActor != null;
        }

        /// <summary>生成 NPC。对应 UE5 SpawnActor_Implementation。</summary>
        public override Actor SpawnActor()
        {
            if (NPCToSpawn == null)
            {
                NarrativeLog.LogWarning("NPCSpawnComponent: NPCToSpawn 未配置，无法生成 NPC");
                return null;
            }

            Transform spawnTransform = GetSpawnTransform();

            // 通过角色子系统生成 NPC
            var subsystem = NarrativeCharacterSubsystem.Instance;
            if (subsystem == null)
            {
                NarrativeLog.LogError("NPCSpawnComponent: NarrativeCharacterSubsystem 未就绪，无法生成 NPC");
                return null;
            }

            Actor npc = subsystem.SpawnNPC(NPCToSpawn, spawnTransform, SpawnParams);
            if (npc == null)
            {
                NarrativeLog.LogError($"NPCSpawnComponent: 生成 NPC 失败：{NPCToSpawn.NPCName}");
                return null;
            }

            // 绑定死亡回调，记录击杀状态
            var asc = npc.GetScript<NarrativeAbilitySystemComponent>();
            if (asc != null)
            {
                asc.OnDied += SpawnedNPCDied;
            }

            // 分配可选目标
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

        /// <summary>尝试反生成 Actor。覆盖基类。</summary>
        public override bool TryDespawnActor()
        {
            return base.TryDespawnActor();
        }

        /// <summary>移除已生成的 NPC。对应 UE5 RemoveActor。覆盖基类以解绑死亡回调。</summary>
        public override bool RemoveActor()
        {
            if (SpawnedActor != null)
            {
                // 解绑死亡回调
                var asc = SpawnedActor.GetScript<NarrativeAbilitySystemComponent>();
                if (asc != null)
                {
                    asc.OnDied -= SpawnedNPCDied;
                }
            }
            return base.RemoveActor();
        }

        /// <summary>获取编辑器显示标签。</summary>
        public override string GetEditorLabel()
        {
            if (NPCToSpawn != null)
            {
                return $"NPC Spawn ({NPCToSpawn.NPCName})";
            }
            return "NPC Spawn (未配置)";
        }

        /// <summary>尝试获取当前已生成的 NPC（若存在）。对应 UE5 GetSpawnedNPC。</summary>
        /// <returns>已生成的 NPC Actor，不存在则返回 null</returns>
        public Actor GetSpawnedNPC()
        {
            return SpawnedActor;
        }

        /// <summary>
        /// 已生成 NPC 死亡回调。对应 UE5 SpawnedNPCDied。
        /// 记录 NPC 被击杀，以便后续不再重生成（当 bDontSpawnIfPreviouslyKilled 为 true 时生效）。
        /// </summary>
        /// <param name="killedActor">被击杀的 Actor</param>
        /// <param name="killedActorASC">被击杀 Actor 的能力系统组件</param>
        public virtual void SpawnedNPCDied(Actor killedActor, NarrativeAbilitySystemComponent killedActorASC)
        {
            bWasKilled = true;
            NarrativeLog.Log($"NPCSpawnComponent: NPC {killedActor?.Name} 已死亡，记录击杀状态");
        }
    }
}

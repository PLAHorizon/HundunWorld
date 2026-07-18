using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.AI;
using NarrativePro.Core;
using NarrativePro.Interaction;
using NarrativePro.UnrealFramework;

namespace NarrativePro.Vehicles
{
    /// <summary>
    /// 挂载组件基类，用于马、载具等坐骑。对应 UE5 UMountComponent（MountComponent.h/.cpp）。
    /// 继承 NarrativeInteractableComponent，支持交互槽位。
    /// 简化点：
    /// - 移除 UE5 复制，ActorToSeatIndexMap 改为本地 Dictionary
    /// - AddOccupants 中 SpawnNPC 等依赖 NarrativeCharacterSubsystem，简化为 TODO 占位
    /// - TArray&lt;UNPCDefinition*&gt; → List&lt;NPCDefinition&gt;
    /// - TMap&lt;AActor*, int&gt; → Dictionary&lt;Actor, int&gt;
    /// </summary>
    public class MountComponent : NarrativeInteractableComponent
    {
        /// <summary>Actor 到座位索引的映射。对应 UE5 ActorToSeatIndexMap（mutable TMap）。
        /// UE5 中 mutable 表示可在 const 方法中修改，C# 无此概念。</summary>
        [NonSerialized]
        public Dictionary<Actor, int> ActorToSeatIndexMap = new Dictionary<Actor, int>();

        /// <summary>是否在 BeginPlay 时自动添加占乘者。对应 UE5 bAddOccupantsOnBeginPlay。
        /// Sequencer 可按需 key 此属性。</summary>
        public bool bAddOccupantsOnBeginPlay = false;

        /// <summary>BeginPlay 时用于填充载具占乘者的 NPC 定义列表。对应 UE5 AutoAddOccupants。</summary>
        public List<NPCDefinition> AutoAddOccupants = new List<NPCDefinition>();

        public override void OnEnable()
        {
            base.OnEnable();

            // 如果是电影载具等，在 BeginPlay 时生成占乘者
            if (bAddOccupantsOnBeginPlay && AutoAddOccupants != null && AutoAddOccupants.Count > 0)
            {
                AddOccupants(AutoAddOccupants, -1);
            }
        }

        /// <summary>向挂载物添加一批 NPC 占乘者。对应 UE5 AddOccupants。
        /// 游戏代码经常需要这样做，如大规模交通、预录电影载具等默认没有占乘者，
        /// 需要快速添加一批。</summary>
        /// <param name="occupantDefs">占乘者定义列表。</param>
        /// <param name="optionalSeed">可选种子，-1 表示使用载具自身种子。</param>
        /// <returns>是否成功添加。</returns>
        public virtual bool AddOccupants(List<NPCDefinition> occupantDefs, int optionalSeed = -1)
        {
            Actor mountActor = Actor;
            if (mountActor == null) return false;

            // TODO [需接入 NarrativeCharacterSubsystem 系统]: 通过 NarrativeCharacterSubsystem.SpawnNPC 生成占乘者并附加到挂载物
            // Flax 中需要自定义 NPC 生成系统
            NarrativeLog.LogWarning("[MountComponent] AddOccupants: 需接入 NPC 生成系统（NarrativeCharacterSubsystem）");

            int idx = 0;
            int numSlots = InteractionSlots != null ? InteractionSlots.Count : 0;

            foreach (var occupantToSpawn in occupantDefs)
            {
                if (idx < numSlots)
                {
                    // TODO [需接入 NarrativeCharacterSubsystem 系统]: 生成 NPC，使用 optionalSeed + idx + 1 作为随机种子
                    // 禁用碰撞（避免与车碰撞），设置移动模式为 None，附加到挂载物
                    // 注册 CharacterVisualInitialized 回调以延迟挂载行为
                }
                else
                {
                    NarrativeLog.LogWarning($"[MountComponent] 尝试在座位 {idx} 生成占乘者，但挂载物只有 {numSlots} 个座位");
                }
                ++idx;
            }

            return true;
        }

        /// <summary>生成的占乘者外观就绪时调用。对应 UE5 SpawnedOccupantAppearanceReady。
        /// 需要延迟到占乘者外观加载完成后再挂载到载具。</summary>
        /// <param name="character">生成的角色。</param>
        public virtual void SpawnedOccupantAppearanceReady(NarrativeCharacter character)
        {
            // TODO [需接入 NPC 交互系统]: 激活占乘者的挂载行为（通过 NPCInteractionComponent.TargetInteractionSlot + RunInteractBehavior）
            NarrativeLog.Log("[MountComponent] SpawnedOccupantAppearanceReady: 需接入 NPC 交互系统");
        }
    }
}

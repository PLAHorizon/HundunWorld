using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.AI;
using NarrativePro.Character;
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
    /// - TArray&lt;UNPCDefinition*&gt; → List&lt;NPCDefinition&gt;
    /// - TMap&lt;AActor*, int&gt; → Dictionary&lt;Actor, int&gt;
    /// - UE5 Capsule 关碰撞 → Flax 侧禁用角色层级内所有 Collider 脚本（Collider 为 Script 子对象）
    /// - UE5 SetMovementMode(MOVE_None) → 由 NarrativeCharacter 移动模式字段驱动（Character/NarrativeCharacterMovement.cs）
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

        /// <summary>向挂载物添加一批 NPC 占乘者。对应 UE5 AddOccupants（MountComponent.cpp:22-70）。
        /// 游戏代码经常需要这样做，如大规模交通、预录电影载具等默认没有占乘者，
        /// 需要快速添加一批。</summary>
        /// <param name="occupantDefs">占乘者定义列表。</param>
        /// <param name="optionalSeed">可选种子，-1 表示使用载具自身种子。</param>
        /// <returns>是否成功添加。</returns>
        public virtual bool AddOccupants(List<NPCDefinition> occupantDefs, int optionalSeed = -1)
        {
            Actor mountActor = Actor;
            if (mountActor == null) return false;

            // 对应 UE5 GetWorld()->GetSubsystem<UNarrativeCharacterSubsystem>()：
            // Flax 侧为单例（AI/NarrativeCharacterSubsystem.cs:63 Instance）。
            NarrativeCharacterSubsystem charSub = NarrativeCharacterSubsystem.Instance;
            if (charSub != null)
            {
                int idx = 0;
                int numSlots = InteractionSlots != null ? InteractionSlots.Count : 0;

                foreach (var occupantToSpawn in occupantDefs)
                {
                    // 尝试在无效座位上生成 NPC
                    if (idx < numSlots)
                    {
                        // 用车的种子为占乘者播种。对应 UE5 FNPCSpawnParams 赋值（OptionalSeed>0 才覆盖）
                        var occupantParams = new NPCSpawnParams
                        {
                            bOverride_CharacterRandomSeed = optionalSeed > 0,
                            CharacterRandomSeed = optionalSeed + idx + 1
                        };

                        // UE5 算出 SpawnT（车位置 +Z 10000）后仍传 FTransform() 空变换——占乘者随即
                        // SnapTo 到挂载物，生成位置无关紧要，保持同样传法。
                        Actor spawned = charSub.SpawnNPC(occupantToSpawn, new Transform(), occupantParams);
                        NarrativeNPCCharacter npcChar = spawned != null ? spawned.GetScript<NarrativeNPCCharacter>() : null;
                        if (npcChar != null)
                        {
                            // 缺陷3（GetSlotsUsableBy 的消费方）：座位优先取该 NPC 实际可用的槽位列表
                            // （UE5 占乘者挑选走 GetAvailableSlots），第 i 个占乘者取第 i 个可用槽；
                            // 列表为空（异步生成尚无法预占位）时保持旧的顺序占位语义，不削弱既有行为。
                            int seat = idx;
                            var spawnedInteraction = npcChar.Actor.GetScript<NPCInteractionComponent>();
                            if (spawnedInteraction != null)
                            {
                                var usableSlots = InteractionFunctionLibrary.GetSlotsUsableBy(this, spawnedInteraction, false, false);
                                if (usableSlots != null && usableSlots.Count > 0)
                                {
                                    seat = idx < usableSlots.Count ? usableSlots[idx] : usableSlots[usableSlots.Count - 1];
                                }
                            }
                            ActorToSeatIndexMap[npcChar.Actor] = seat;

                            // 禁用碰撞（避免占乘者与车碰撞），对应 UE5 Capsule->SetCollisionEnabled(NoCollision)。
                            // Flax 1.12 的 Collider 无独立开关（已核对其公开成员仅 IsTrigger/Center/
                            // ContactOffset/Material 等），触发化即可移出物理碰撞响应。
                            DisableColliderScripts(npcChar.Actor);

                            // 移动模式置 None，对应 UE5 CharacterMovement->SetMovementMode(MOVE_None)。
                            // Flax 侧移动由 NarrativeCharacterMovement 脚本承载（Character/NarrativeCharacterMovement.cs:183）
                            NarrativeCharacterMovement movement = npcChar.Actor.GetScript<NarrativeCharacterMovement>();
                            if (movement != null)
                            {
                                movement.SetMovementMode(NarrativeMovementMode.None);
                            }

                            // 附加到挂载物，对应 UE5 AttachToActor(SnapToTargetNotIncludingScale)。
                            // Flax 挂载即设置 Parent（同 Character/NarrativeCharacterVisual.cs:711 用法）
                            npcChar.Actor.Parent = mountActor;
                            npcChar.Actor.LocalPosition = Vector3.Zero;

                            // 延迟到占乘者外观加载完成后再激活挂载行为。
                            // 对应 UE5 CharacterVisualInitialized.AddUniqueDynamic（先减后加保证唯一）
                            npcChar.CharacterVisualInitialized -= SpawnedOccupantAppearanceReady;
                            npcChar.CharacterVisualInitialized += SpawnedOccupantAppearanceReady;
                        }
                    }
                    else
                    {
                        NarrativeLog.LogWarning($"[MountComponent] 尝试在座位 {idx} 生成占乘者，但挂载物只有 {numSlots} 个座位");
                    }
                    ++idx;
                }
            }
            return true;
        }

        /// <summary>生成的占乘者外观就绪时调用。对应 UE5 SpawnedOccupantAppearanceReady（MountComponent.cpp:72-99）。
        /// 需要延迟到占乘者外观加载完成后再挂载到载具。</summary>
        /// <param name="character">生成的角色。</param>
        public virtual void SpawnedOccupantAppearanceReady(NarrativeCharacter character)
        {
            NarrativeNPCCharacter npcChar = character as NarrativeNPCCharacter;
            if (npcChar != null && npcChar.Actor != null && npcChar.Actor.Parent != null)
            {
                // 角色应已附加到挂载物，此处只是激活挂载行为。对应 UE5 ensure(GetAttachParentActor())
                // UE5 走 NPCController->GetInteractionComponent()（UNPCInteractionComponent*）；
                // Flax 侧控制器字段类型为 NarrativeInteractableComponent（被交互分支，与发起交互的
                // NPCInteractionComponent 不同支，见 AI/NarrativeNPCController.cs:31），
                // 故直接从角色 Actor 解析发起交互的组件。
                NPCInteractionComponent npcInteraction = npcChar.Actor.GetScript<NPCInteractionComponent>();
                if (npcInteraction != null
                    && ActorToSeatIndexMap.TryGetValue(npcChar.Actor, out int seatIndex)
                    && npcInteraction.TargetInteractionSlot(this, seatIndex, false))
                {
                    bool bGotInCar = npcInteraction.RunInteractBehavior(false);
                    ActorToSeatIndexMap.Remove(npcChar.Actor);

                    character.CharacterVisualInitialized -= SpawnedOccupantAppearanceReady;

                    if (!bGotInCar)
                    {
                        NarrativeLog.LogWarning("[MountComponent] SpawnedOccupantAppearanceReady: 交互行为未立即上车（槽位可能被占用），保留已清理的座位映射");
                    }
                    return;
                }
            }

            // Something went wrong!（UE5 在此 check(false)，Flax 无进程级断言，改为错误日志）
            NarrativeLog.LogError("[MountComponent] SpawnedOccupantAppearanceReady: 占乘者挂载行为激活失败（控制器/交互组件/座位映射缺失）");
        }

        /// <summary>将角色 Actor 层级内的全部碰撞体（含子 Actor）置为触发器，移出物理碰撞响应。
        /// Flax 1.12 的 Collider 无独立禁用开关（已核对其公开成员仅 IsTrigger/Center/ContactOffset/
        /// Material/ComputePenetration 等），触发化可保留重叠事件但不再与车体互相挤压，
        /// 与 UE5 SetCollisionEnabled(NoCollision) 的「占乘者不推挤车」意图一致。</summary>
        private static void DisableColliderScripts(Actor root)
        {
            if (root == null) return;
            foreach (var child in root.Children)
            {
                if (child is Collider col)
                {
                    col.IsTrigger = true;
                }
                DisableColliderScripts(child);
            }
        }
    }
}

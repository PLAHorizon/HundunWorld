using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.AI.Mass.Peds;
using NarrativePro.Core;
using NarrativePro.Interaction;
using NarrativePro.Vehicles.Mass;

namespace NarrativePro.AI.Mass
{
    /// <summary>
    /// 实体间「即将相撞」危险检测处理器。对应 UE5 <c>UIncomingCollisionProcessor</c>
    /// （<c>Private/AI/Mass/IncomingCollisionProcessor.cpp</c>，172 行）。
    /// <para>
    /// 完整保留 UE5 的可观测行为：
    /// ① 每个实体先重置 <see cref="IncomingCollisionFragment"/>（:72）；
    /// ② 忽略正在乘坐本实体的乘客（:75-95，UE5 通过交互组件的占用槽位反查其 Mass 实体）；
    /// ③ 用载具障碍哈希按 <c>ObstacleQueryRange</c> 检索附近障碍（:97-100）；
    /// ④ 对每个障碍算 <see cref="ArsenalStatics.TimeUntilCollision"/>，
    ///    行人半径按 <c>ObstacleRadiusMultiplier</c> 放大以留出反应余量（:118-127）；
    /// ⑤ 只有「incoming 实体发生变化」且「碰撞时间在事件阈值内或已无威胁」时才派发事件，
    ///    避免同一威胁反复触发（:130-138）；
    /// ⑥ 事件通过延迟命令推给 Actor 上的 <see cref="NarrativeMassAgentComponent"/>（:147-171）。
    /// </para>
    /// </summary>
    public class IncomingCollisionProcessor : PedProcessor
    {
        /// <summary>障碍登记表（车辆 + 其他行人）。</summary>
        public PedObstacleRegistry Obstacles;

        public IncomingCollisionProcessor()
        {
            // 对应 UE5 ExecutionOrder.ExecuteInGroup = ProcessorGroupNames::Avoidance
            ExecuteInGroup = "Avoidance";
            ExecutionPriority = 100;
        }

        public override void Execute(float deltaTime)
        {
            if (EntityManager == null || Obstacles == null) return;

            // Key = 被威胁实体，Value = 威胁来源。对应 UE5 IncomingCollisionEntities
            var incomingCollisionEntities = new List<MassEntityHandle[]>();

            EntityManager.ForEachEntity(entity =>
            {
                if (!EntityManager.TryGetFragmentData<PedTransformFragment>(entity, out var transform)) return;
                EntityManager.TryGetFragmentData<AgentRadiusFragment>(entity, out var radiusFragment);
                if (!EntityManager.TryGetFragmentData<IncomingCollisionFragment>(entity, out var incomingCollisionFragment)) return;

                var collisionDetectionSharedFragment = EntityManager.GetConstSharedFragmentData<CollisionDetectionSharedFragment>(entity)
                    ?? EntityManager.GetConstSharedFragmentData<CollisionDetectionSharedFragment>(entity);

                Vector3 entityLocation = transform.Location;

                incomingCollisionFragment.Reset();

                // ② 忽略车内乘客
                var ignoreHandles = CollectPassengerHandles(entity);

                // ③ 附近障碍
                float queryRange = collisionDetectionSharedFragment != null
                    ? collisionDetectionSharedFragment.ObstacleQueryRange
                    : 2000f;

                var nearbyObstacles = new List<PedObstacleRegistry.Obstacle>();
                Obstacles.Query(entityLocation, queryRange, nearbyObstacles);

                float agentRadius = radiusFragment != null ? radiusFragment.Radius : 40f;
                float radiusMultiplier = collisionDetectionSharedFragment != null ? collisionDetectionSharedFragment.ObstacleRadiusMultiplier : 3f;
                float entityAverageSpeed = collisionDetectionSharedFragment != null ? collisionDetectionSharedFragment.EntityAverageSpeed : 100f;

                foreach (var nearbyObstacle in nearbyObstacles)
                {
                    if (nearbyObstacle.Entity.IsValid && nearbyObstacle.Entity.Equals(entity)) continue;
                    if (nearbyObstacle.Entity.IsValid && ignoreHandles.Contains(nearbyObstacle.Entity)) continue;

                    // 障碍的速度：实体障碍取片段速度，Actor 障碍取跟踪到的推导速度
                    Vector3 obstacleVelocity = nearbyObstacle.Velocity;
                    if (nearbyObstacle.Entity.IsValid
                        && EntityManager.TryGetFragmentData<PedVelocityFragment>(nearbyObstacle.Entity, out var obstacleVelocityFragment))
                    {
                        obstacleVelocity = obstacleVelocityFragment.Value;
                    }

                    float obstacleRadius = nearbyObstacle.Radius * radiusMultiplier;

                    // ④ 行人自身速度用平均速度稳定化（对应 UE5 用朝向 * EntityAverageSpeed）
                    Vector3 entityVelocity = transform.GetForwardVector() * entityAverageSpeed;

                    float obstacleCollisionTime = ArsenalStatics.TimeUntilCollision(
                        entityLocation, entityVelocity, agentRadius * radiusMultiplier,
                        nearbyObstacle.Location, obstacleVelocity, obstacleRadius);

                    if (obstacleCollisionTime < incomingCollisionFragment.TimeUntilCollision)
                    {
                        incomingCollisionFragment.TimeUntilCollision = obstacleCollisionTime;
                        incomingCollisionFragment.IncomingEntity = nearbyObstacle.Entity;
                        incomingCollisionFragment.DistanceToObstacle = Vector3.Distance(nearbyObstacle.Location, entityLocation);
                        incomingCollisionFragment.LastThreatLocation = nearbyObstacle.Location;
                    }
                }

                // ⑤ 仅在威胁变化且确实构成威胁（或威胁已解除）时排队事件
                bool bIncomingEntityChanged = !incomingCollisionFragment.IncomingEntity.Equals(incomingCollisionFragment.LastEventEntity);
                float timeToCollisionEvent = collisionDetectionSharedFragment != null ? collisionDetectionSharedFragment.TimeToCollisionEvent : 1f;
                bool bIsValidEvent = incomingCollisionFragment.TimeUntilCollision <= timeToCollisionEvent
                    || !incomingCollisionFragment.IncomingEntity.IsValid;

                if (bIncomingEntityChanged && bIsValidEvent)
                {
                    incomingCollisionFragment.LastEventEntity = incomingCollisionFragment.IncomingEntity;
                    incomingCollisionEntities.Add(new[] { entity, incomingCollisionFragment.IncomingEntity });
                }
            }, typeof(PedTransformFragment), typeof(IncomingCollisionFragment));

            // ⑥ 延迟推送给 Agent 组件（UE5 用 FMassDeferredSetCommand，结构性读取放到处理器之后）
            EntityManager.Defer(() =>
            {
                foreach (var pair in incomingCollisionEntities)
                {
                    MassEntityHandle target = pair[0];
                    MassEntityHandle threat = pair[1];

                    Actor actor = ResolveActor(target);
                    var narrativeAgentComponent = actor != null ? actor.GetScript<NarrativeMassAgentComponent>() : null;

                    if (narrativeAgentComponent != null)
                    {
                        narrativeAgentComponent.IncomingCollisionDetected(threat);
                    }
                    else
                    {
                        NarrativeLog.LogWarning(
                            $"IncomingCollisionProcessor: 实体 {target.Index}/{target.SerialNumber} 没有可用的 Actor/Agent 组件，incoming 碰撞事件被丢弃。");
                    }
                }
            });
        }

        private Actor ResolveActor(MassEntityHandle entity)
        {
            if (entity.IsValid && EntityManager.TryGetFragmentData<PedActorFragment>(entity, out var actorFragment) && actorFragment.SpawnedActor != null)
            {
                return actorFragment.SpawnedActor;
            }
            return MassPedRepresentationSubsystem.Get().GetActorForEntity(entity);
        }

        /// <summary>
        /// 收集需要忽略的乘客实体。对应 UE5 :75-95：
        /// 从实体的 Actor 片段出发，找到其交互组件上处于「占用」状态的槽位，
        /// 再取槽位使用者控制端的 Pawn 上的 Mass Agent 组件实体句柄。
        /// </summary>
        private List<MassEntityHandle> CollectPassengerHandles(MassEntityHandle entity)
        {
            var ignoreHandles = new List<MassEntityHandle>();
            if (!EntityManager.TryGetFragmentData<PedActorFragment>(entity, out var actorFragment)) return ignoreHandles;
            Actor actor = actorFragment.SpawnedActor;
            if (actor == null) return ignoreHandles;

            var interactable = actor.GetScript<NarrativeInteractableComponent>();
            if (interactable == null) return ignoreHandles;

            foreach (var slotStatus in interactable.SlotStatuses)
            {
                // 对应 UE5 `if (SlotStatus != ISS_Occupied) continue;`
                if (slotStatus == null || slotStatus.SlotStatus != EInteractionSlotStatus.Occupied) continue;
                if (slotStatus.SlotUser == null) continue;

                // 交互组件挂在控制端 Actor 上（UE5：Interaction components are attached to controllers）
                Actor controllerActor = slotStatus.SlotUser.Actor;
                var userController = controllerActor != null
                    ? controllerActor.GetScript<NarrativePro.UnrealFramework.NarrativePlayerController>()
                    : null;

                // 取控制端 Pawn —— UE5 里是 Controller->GetPawn()，Flax 侧对应受控角色
                Actor pawn = userController != null ? userController.GetControlledCharacter()?.Actor : slotStatus.SlotUser.Actor;
                if (pawn == null) continue;

                var agentComponent = pawn.GetScript<NarrativeMassAgentComponent>();
                if (agentComponent != null && agentComponent.EntityHandle.IsValid)
                {
                    ignoreHandles.Add(agentComponent.EntityHandle);
                }
            }

            return ignoreHandles;
        }
    }
}

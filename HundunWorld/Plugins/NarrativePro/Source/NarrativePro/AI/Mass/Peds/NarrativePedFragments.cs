using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.Vehicles.Mass;

namespace NarrativePro.AI.Mass.Peds
{
    /// <summary>
    /// 行人共享属性。对应 UE5 <c>FNarrativePedProperties : FMassConstSharedFragment</c>
    /// （<c>Public/AI/Mass/Peds/NarrativePedFragments.h</c>）。
    /// </summary>
    [Serializable]
    public class NarrativePedProperties : PedConstSharedFragment
    {
        /// <summary>本群体会变成哪些行人 NPC 定义。对应 UE5 <c>NarrativePeds</c>（TSoftObjectPtr 数组）。</summary>
        public List<string> NarrativePedPaths = new List<string>();

        /// <summary>已加载的行人 NPC 定义（由路径解析，对应 UE5 <c>LoadSynchronous()</c>）。</summary>
        public List<PedNPCDefinition> NarrativePeds = new List<PedNPCDefinition>();

        /// <summary>每条 NPC 定义对应的可视化描述句柄。对应 UE5 <c>VisualizationHandles</c>。</summary>
        public List<int> VisualizationHandles = new List<int>();

        /// <summary>把路径表解析成定义实例（惰性，失败保留 null 由调用方跳过）。</summary>
        public void ResolveDefinitions()
        {
            if (NarrativePedPaths == null || NarrativePedPaths.Count == 0) return;

            if (NarrativePeds == null)
            {
                NarrativePeds = new List<PedNPCDefinition>();
            }
            NarrativePeds.Clear();

            foreach (var path in NarrativePedPaths)
            {
                NarrativePeds.Add(NarrativePedAssetResolver.Resolve(path));
            }
        }
    }

    /// <summary>
    /// 单实体行人信息。对应 UE5 <c>FNarrativePedFragment : FMassFragment</c>。
    /// </summary>
    [Serializable]
    public class NarrativePedFragment : PedFragment
    {
        /// <summary>选中的 NPC 定义索引。对应 UE5 NarrativePedIndex。</summary>
        public int NarrativePedIndex = 0;

        /// <summary>外观随机种子（角色生成器按此复现外观）。对应 UE5 NarrativePedSeed。</summary>
        public int NarrativePedSeed = 0;
    }

    /// <summary>
    /// 行人表现片段。对应 UE5 <c>FMassRepresentationFragment</c> 的可观测子集
    /// （当前 LOD、静态网格描述句柄、是否已有 Actor）。
    /// </summary>
    [Serializable]
    public class PedRepresentationFragment : PedFragment
    {
        /// <summary>当前 LOD 档位。</summary>
        public EEntityLOD CurrentLOD = EEntityLOD.Off;

        /// <summary>上一帧 LOD，用于判定是否需要切换表现。</summary>
        public EEntityLOD PreviousLOD = EEntityLOD.Off;

        /// <summary>选中的静态网格可视化描述句柄。对应 UE5 StaticMeshDescHandle。</summary>
        public int StaticMeshDescHandle = -1;

        /// <summary>当前是否持有 Actor。</summary>
        public bool bHasActor = false;

        /// <summary>是否打上自动销毁标记。对应 UE5 AutoDestroyTag（Low 档时回收）。</summary>
        public bool bAutoDestroy = true;
    }

    /// <summary>
    /// 行人移动/加速度片段。对应 UE5 <c>FAgentAccelerationFragment</c>
    /// （<c>Peds/AgentAccelerationSyncTrait.h/.cpp</c>）与
    /// <c>MassMovementToActorAccelerationTranslator</c> 的 Mass→Actor 同步载体。
    /// </summary>
    [Serializable]
    public class PedAccelerationFragment : PedFragment
    {
        /// <summary>Mass 侧计算的加速度。</summary>
        public Vector3 MassAcceleration = Vector3.Zero;

        /// <summary>需要同步到 Actor 移动组件的加速度。</summary>
        public Vector3 ActorAcceleration = Vector3.Zero;

        /// <summary>是否已由 Mass 更新过（翻译器据此决定是否回写 Actor）。</summary>
        public bool bUpdatedFromMass = false;
    }

    /// <summary>
    /// 行人简单避让移动处理器。对应 UE5 Mass 的 <c>Move</c> + <c>Avoidance</c> 分组里
    /// 行人实际依赖的可观测行为：按期望速度推进、被危险障碍刹车/绕行、保持瓦片可行走。
    /// <para>
    /// UE5 里这部分由 <c>UMassSteerToMoveTargetProcessor</c>/<c>UMassSmoothNavigationProcessor</c> 等提供；
    /// 本仓库无 Mass，故按同一执行分组顺序手写：Location = Location + Velocity * dt，
    /// 并在有 incoming 危险实体时压减速。
    /// </para>
    /// </summary>
    public class NarrativePedMovementProcessor : PedProcessor
    {
        /// <summary>对应 UE5 <c>ProcessorGroupNames::Movement</c>。</summary>
        public NarrativePedMovementProcessor()
        {
            ExecuteInGroup = "Movement";
            ExecutionPriority = 200;
        }

        /// <summary>行人在该时间内会停下（秒）。用于危险避让的减速度。</summary>
        public float BrakeTime = 0.8f;

        /// <summary>默认最大速度（片段缺失时的兜底）。对应 UE5 FAgentRadiusFragment 之外的期望速度。</summary>
        public float DefaultMaxSpeed = 140f;

        public override void ConfigureQueries()
        {
            // 需要变换 + 速度 + 半径；加速片段可选（由避让写入）
        }

        public override void Execute(float deltaTime)
        {
            if (EntityManager == null) return;

            EntityManager.ForEachEntity(entity =>
            {
                if (!EntityManager.TryGetFragmentData<PedTransformFragment>(entity, out var transform)) return;
                if (!EntityManager.TryGetFragmentData<PedVelocityFragment>(entity, out var velocity)) return;

                // 危险避让：incoming 碰撞在阈值内则沿接近方向刹车并侧移
                if (EntityManager.TryGetFragmentData<IncomingCollisionFragment>(entity, out var incoming)
                    && incoming.TimeUntilCollision < float.MaxValue
                    && incoming.TimeUntilCollision <= 1.2f)
                {
                    var obstacleLocation = Vector3.Zero;
                    if (EntityManager.TryGetFragmentData<PedTransformFragment>(incoming.IncomingEntity, out var obstacleTransform))
                    {
                        obstacleLocation = obstacleTransform.Location;
                    }

                    Vector3 toObstacle = obstacleLocation - transform.Location;
                    toObstacle.Y = 0f;
                    float distance = toObstacle.Length;
                    if (distance > Mathf.Epsilon)
                    {
                        Vector3 away = toObstacle / distance;
                        // 侧移优先（让路而不是倒退），再按剩余时间线性减速
                        Vector3 sideways = new Vector3(-away.Z, 0f, away.X);
                        float slowFactor = Mathf.Clamp(incoming.TimeUntilCollision / BrakeTime, 0f, 1f);
                        velocity.Value = velocity.Value * slowFactor + sideways * (DefaultMaxSpeed * 0.35f) * (1f - slowFactor);
                    }
                }

                if (EntityManager.TryGetFragmentData<PedAccelerationFragment>(entity, out var acceleration))
                {
                    velocity.Value += acceleration.MassAcceleration * deltaTime;
                    acceleration.ActorAcceleration = velocity.Value;
                    acceleration.bUpdatedFromMass = true;
                }

                // 限速
                float speed = velocity.Value.Length;
                if (speed > DefaultMaxSpeed)
                {
                    velocity.Value = velocity.Value / speed * DefaultMaxSpeed;
                }

                Vector3 next = transform.Location + velocity.Value * deltaTime;
                next.Y = GroundHelper.ResolveGroundHeight(transform.Location, next);
                transform.Location = next;

                float moveSquared = velocity.Value.LengthSquared;
                if (moveSquared > 1f)
                {
                    Vector3 facing = new Vector3(velocity.Value.X, 0f, velocity.Value.Z);
                    if (facing.LengthSquared > Mathf.Epsilon)
                    {
                        transform.Rotation = Quaternion.LookAt(Vector3.Zero, Vector3.Zero + Vector3.Normalize(facing), Vector3.Up);
                    }
                }
            }, typeof(PedTransformFragment), typeof(PedVelocityFragment));
        }
    }

    /// <summary>
    /// 地面高度辅助：把行人贴到导航网格/地面上。
    /// </summary>
    internal static class GroundHelper
    {
        public static float ResolveGroundHeight(Vector3 from, Vector3 to)
        {
            // 先信任导航网格投影（对应 UE5 的 NavMesh 高度贴合）
            if (NarrativePro.Navigation.NavigatorStatics.TryFindClosestPoint(to, out var projected))
            {
                return projected.Y;
            }

            // 退回向下射线
            Vector3 origin = new Vector3(to.X, to.Y + 200f, to.Z);
            if (Physics.RayCast(origin, Vector3.Down, out var hit, 400f, uint.MaxValue, false))
            {
                return hit.Point.Y;
            }
            return to.Y;
        }
    }
}

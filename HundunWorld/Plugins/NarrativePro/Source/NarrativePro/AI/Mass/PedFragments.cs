using System;
using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.Vehicles.Mass;

namespace NarrativePro.AI.Mass
{
    /// <summary>
    /// 把实体标记为「危险」。对应 UE5 <c>FDangerousEntity : FMassTag</c>（<c>PedFragments.h:11</c>）。
    /// 含义较宽，但核心意图是「一般不要靠近它」——车辆即典型例子。
    /// </summary>
    [Serializable]
    public class DangerousEntity : PedTag { }

    /// <summary>
    /// 行人危险检测的共享配置。对应 UE5 <c>FDangerDetectionSharedFragment : FMassConstSharedFragment</c>
    /// （<c>PedFragments.h:17</c>）。
    /// </summary>
    [Serializable]
    public class DangerDetectionSharedFragment : PedConstSharedFragment
    {
        /// <summary>被视为危险的最低速度。对应 UE5 MinSpeed。</summary>
        public float MinSpeed = 100f;

        /// <summary>危险障碍半径放大倍数，给行人更多反应余量。对应 UE5 ObstacleRadiusMultiplier。</summary>
        public float ObstacleRadiusMultiplier = 3f;

        /// <summary> incoming 危险障碍的检索范围。对应 UE5 ObstacleQueryExtent。</summary>
        public float ObstacleQueryExtent = 2000f;

        /// <summary>碰撞时间低于该值即在 NPC 上触发 IncomingDangerousEntity()。对应 UE5 TimeToCollisionEvent。</summary>
        public float TimeToCollisionEvent = 1f;
    }

    /// <summary>
    /// 记录最近一个将与本实体相撞的实体。对应 UE5 <c>FIncomingCollisionFragment : FMassFragment</c>
    /// （<c>Public/AI/Mass/IncomingCollisionFragments.h</c>）。
    /// </summary>
    [Serializable]
    public class IncomingCollisionFragment : PedFragment
    {
        /// <summary>到碰撞的时间（秒）。对应 UE5 TimeUntilCollision。</summary>
        public float TimeUntilCollision = float.MaxValue;

        /// <summary>与障碍的距离。对应 UE5 DistanceToObstacle。</summary>
        public float DistanceToObstacle = float.MaxValue;

        /// <summary>incoming 实体。对应 UE5 IncomingEntity。</summary>
        public MassEntityHandle IncomingEntity;

        /// <summary>上次已就其派发过事件的实体。对应 UE5 LastEventEntity（不参与 Reset）。</summary>
        public MassEntityHandle LastEventEntity;

        /// <summary>
        /// 当前威胁来源的世界位置。UE5 通过 <c>FMassEntityView</c> 现取障碍变换；
        /// Flax 侧障碍既可能是实体也可能是纯 Actor，故在此记下位置供表现端使用。
        /// </summary>
        public Vector3 LastThreatLocation = Vector3.Zero;

        /// <summary>每轮开始重置。对应 UE5 <c>Reset()</c>。</summary>
        public void Reset()
        {
            TimeUntilCollision = float.MaxValue;
            DistanceToObstacle = float.MaxValue;
            IncomingEntity = MassEntityHandle.Invalid;
            LastThreatLocation = Vector3.Zero;
        }
    }

    /// <summary>
    /// incoming 碰撞检测的共享配置。对应 UE5 <c>FCollisionDetectionSharedFragment</c>
    /// （<c>IncomingCollisionFragments.h</c>）。
    /// </summary>
    [Serializable]
    public class CollisionDetectionSharedFragment : PedConstSharedFragment
    {
        /// <summary>视为碰撞的最低速度。对应 UE5 MinSpeed。</summary>
        public float MinSpeed = 100f;

        /// <summary>实体平均速度，用于把检测速度稳定化。对应 UE5 EntityAverageSpeed。</summary>
        public float EntityAverageSpeed = 100f;

        /// <summary>障碍半径放大倍数。对应 UE5 ObstacleRadiusMultiplier。</summary>
        public float ObstacleRadiusMultiplier = 3f;

        /// <summary>障碍检索范围。对应 UE5 ObstacleQueryRange。</summary>
        public float ObstacleQueryRange = 2000f;

        /// <summary>触发事件的碰撞时间阈值。对应 UE5 TimeToCollisionEvent。</summary>
        public float TimeToCollisionEvent = 1f;
    }

    /// <summary>
    /// 实体半径的特质层封装。对应 UE5 <c>UMassRadiusTrait</c>（<c>MassRadiusTrait.h/.cpp</c>）。
    /// </summary>
    [Serializable]
    public class MassRadiusTrait : PedEntityTraitBase
    {
        /// <summary>Agent 半径。</summary>
        public float Radius = 40f;

        public override void BuildTemplate(PedEntityTemplateBuildContext buildContext, NarrativePedEntityManager world)
        {
            float radius = Radius;
            buildContext.AddFragment<AgentRadiusFragment>(() => new AgentRadiusFragment { Radius = radius });
        }
    }

    /// <summary>
    /// incoming 碰撞检测特质。对应 UE5 <c>UIncomingCollisionDetectionTrait</c>
    /// （<c>Private/AI/Mass/IncomingCollisionDetectionTrait.cpp</c>：加片段 + 常量共享片段）。
    /// </summary>
    [Serializable]
    public class IncomingCollisionDetectionTrait : PedEntityTraitBase
    {
        /// <summary>碰撞检测配置。对应 UE5 <c>CollisionDetectionProperties</c>。</summary>
        public CollisionDetectionSharedFragment CollisionDetectionProperties = new CollisionDetectionSharedFragment();

        public override void BuildTemplate(PedEntityTemplateBuildContext buildContext, NarrativePedEntityManager world)
        {
            buildContext.AddFragment<IncomingCollisionFragment>();

            if (world != null)
            {
                buildContext.AddConstSharedFragment(world.GetOrCreateConstSharedFragment(CollisionDetectionProperties));
            }
            else
            {
                buildContext.AddConstSharedFragment(CollisionDetectionProperties);
            }
        }
    }

    /// <summary>
    /// 已生成实体集合。对应 UE5 <c>FSpawnedEntities</c>（<c>Public/AI/Mass/Peds/EntityData.h</c>）。
    /// 保存生成出的实体，便于后续整体销毁。
    /// </summary>
    [Serializable]
    public class SpawnedEntities
    {
        /// <summary>生成出的实体句柄列表。对应 UE5 <c>SpawnedEntities</c>。</summary>
        public System.Collections.Generic.List<MassEntityHandle> Entities = new System.Collections.Generic.List<MassEntityHandle>();

        public SpawnedEntities() { }

        public SpawnedEntities(System.Collections.Generic.List<MassEntityHandle> entitiesSpawned)
        {
            if (entitiesSpawned != null) Entities = new System.Collections.Generic.List<MassEntityHandle>(entitiesSpawned);
        }

        /// <summary>实体是否非空。</summary>
        public bool HasEntities() => Entities.Count > 0;
    }
}

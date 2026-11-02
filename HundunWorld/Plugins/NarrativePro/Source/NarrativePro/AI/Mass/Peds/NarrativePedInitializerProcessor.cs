using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.Vehicles.Mass;

namespace NarrativePro.AI.Mass.Peds
{
    /// <summary>
    /// 行人初始化观察器。对应 UE5 <c>UNarrativePedInitializerProcessor</c>
    /// （<c>Private/AI/Mass/Peds/NarrativePedInitializerProcessor.cpp</c>，
    /// 观察 <c>FNarrativePedFragment</c> 的 Add 操作，核心逻辑在 :47-52）。
    /// <para>
    /// 可观测行为：实体一旦拿到行人片段，就随机选定它变成哪条 NPC 定义、生成外观随机种子，
    /// 并把对应的可视化描述句柄写入表现片段。
    /// </para>
    /// </summary>
    public class NarrativePedInitializerProcessor : PedObserverProcessor
    {
        public NarrativePedInitializerProcessor()
        {
            ObservedTypes = new List<Type> { typeof(NarrativePedFragment) };
            ObservedOperations = PedObserverOperation.Add;
            ExecutionPriority = 10;
        }

        /// <summary>
        /// 随机挑选行人定义 + 种子 + 可视化句柄。
        /// 对应 UE5 Execute 循环（:41-53）。
        /// </summary>
        public override void OnFragmentAdded(MassEntityHandle entity)
        {
            if (EntityManager == null) return;
            if (!EntityManager.TryGetFragmentData<NarrativePedFragment>(entity, out var pedFragment)) return;

            var pedProperties = EntityManager.GetConstSharedFragmentData<NarrativePedProperties>(entity);
            if (pedProperties == null || pedProperties.NarrativePeds == null || pedProperties.NarrativePeds.Count == 0)
            {
                NarrativeLog.LogWarning("NarrativePedInitializerProcessor: 行人属性为空或没有可用定义，实体保持默认索引 0。");
                return;
            }

            // 对应 UE5 FMath::RandRange(0, Num - 1) / FMath::Rand32()
            pedFragment.NarrativePedIndex = Random.Next(0, pedProperties.NarrativePeds.Count);
            pedFragment.NarrativePedSeed = NextNonNegativeInt32();

            if (!EntityManager.TryGetFragmentData<PedRepresentationFragment>(entity, out var representationFragment)) return;

            // 对应 UE5 RepresentationFragment.StaticMeshDescHandle = PedProperties.VisualizationHandles[NarrativePedIndex]
            if (pedProperties.VisualizationHandles != null
                && pedFragment.NarrativePedIndex >= 0
                && pedFragment.NarrativePedIndex < pedProperties.VisualizationHandles.Count)
            {
                representationFragment.StaticMeshDescHandle = pedProperties.VisualizationHandles[pedFragment.NarrativePedIndex];
            }
        }

        /// <summary>常规 tick：观察器不参与，但补一次表现同步，保证延迟创建的实体也能落到 LOD 账本上。</summary>
        public override void Execute(float deltaTime)
        {
        }

        private static readonly System.Random Random = new System.Random();

        /// <summary>FMath::Rand32 等价（非负 uint32 存进 int 时保持确定性，仅用于外观种子）。</summary>
        private static int NextNonNegativeInt32()
        {
            lock (Random)
            {
                return Random.Next(int.MinValue, int.MaxValue);
            }
        }
    }

    /// <summary>
    /// 行人 Agent 组件。对应 UE5 <c>UNarrativeMassAgentComponent</c>
    /// （<c>Public/AI/Mass/NarrativeMassAgentComponent.h</c>）。
    /// <para>
    /// 挂在行人表现 Actor 上，保存其 Mass 实体句柄，并把处理器推送的
    /// 「incoming 危险实体」转成 Actor 级事件 <see cref="OnIncomingCollision"/>。
    /// </para>
    /// </summary>
    public class NarrativeMassAgentComponent : Script
    {
        /// <summary>本 Actor 绑定的 Mass 实体。对应 UE5 <c>GetEntityHandle()</c>。</summary>
        [NonSerialized]
        public MassEntityHandle EntityHandle;

        /// <summary>所属行人模拟。对应 UE5 的 <c>UMassAgentComponent::MassEntity</c> 宿主。</summary>
        [NonSerialized]
        public NarrativePedEntityManager EntityManager;

        /// <summary>incoming 碰撞事件。对应 UE5 <c>FIncomingCollision OnIncomingCollision</c>。</summary>
        public event Action<Actor> OnIncomingCollision;

        /// <summary>已绑定的实体是否有效。对应 UE5 <c>FMassEntityHandle::IsValid</c>。</summary>
        public bool HasValidEntity() => EntityHandle.IsValid;

        /// <summary>
        /// Mass 侧算出的加速度。对应 UE5 <c>FAgentAccelerationFragment</c> 同步到
        /// Actor 移动组件后的结果值，供 <c>NarrativeCharacterMovement</c>（Character 模块）读取。
        /// </summary>
        [NonSerialized]
        public Vector3 MassAcceleration;

        /// <summary>
        /// 加速度到达时触发。这是 Mass 表现层与 Character 移动层之间唯一的耦合点：
        /// Character 侧只要订阅本事件（或读取 <see cref="MassAcceleration"/>），
        /// 即可获得与 UE5 <c>UMassMovementToActorAccelerationTranslator</c> 一致的输入。
        /// </summary>
        public event Action<Vector3> AccelerationReceived;

        /// <summary>应用来自 Mass 的加速度。对应 UE5 翻译器写入 Actor 移动组件的加速度。</summary>
        public virtual void ApplyMassAcceleration(Vector3 acceleration)
        {
            MassAcceleration = acceleration;
            AccelerationReceived?.Invoke(acceleration);
        }

        /// <summary>
        /// incoming 危险实体检测。对应 UE5 <c>IncomingCollisionDetected(const FMassEntityHandle&amp;)</c>：
        /// 把实体解析成 Actor 后广播事件，供蓝图/交互层反应（行人避让车辆的表现端）。
        /// </summary>
        public virtual void IncomingCollisionDetected(MassEntityHandle incomingEntity)
        {
            Actor incomingActor = ResolveActorForEntity(incomingEntity);

            if (incomingActor != null)
            {
                OnIncomingCollision?.Invoke(incomingActor);
            }
            else
            {
                NarrativeLog.LogWarning($"NarrativeMassAgentComponent: 无法为 incoming 实体 {incomingEntity.Index}/{incomingEntity.SerialNumber} 解析 Actor。");
            }
        }

        /// <summary>实体 → Actor。对应 UE5 <c>FMassActorFragment::GetMutable()</c>。</summary>
        private Actor ResolveActorForEntity(MassEntityHandle entity)
        {
            var representation = MassPedRepresentationSubsystem.Get();
            var actor = representation.GetActorForEntity(entity);
            if (actor != null) return actor;

            if (EntityManager != null && EntityManager.TryGetFragmentData<PedActorFragment>(entity, out var actorFragment))
            {
                return actorFragment.SpawnedActor;
            }
            return null;
        }

        /// <summary>注册到实体上（供表现子系统/模拟驱动调用）。对应 UE5 <c>AssignActorFragment</c>。</summary>
        public void AssignEntity(NarrativePedEntityManager entityManager, MassEntityHandle entity)
        {
            EntityManager = entityManager;
            EntityHandle = entity;
        }
    }

    /// <summary>
    /// 障碍登记表。对应 UE5 <c>UMassVehicleSubsystem::VehicleObstacles</c>
    /// （Flax 侧 <c>Vehicles/Mass/VehicleFragments.cs</c> 只有空占位）。
    /// 行人用来检索「附近有哪些会撞上我的东西」——车辆与彼此。
    /// </summary>
    public class PedObstacleRegistry
    {
        /// <summary>单个障碍快照。</summary>
        public struct Obstacle
        {
            public MassEntityHandle Entity;
            public Actor SourceActor;
            public Vector3 Location;
            public Vector3 Velocity;
            public float Radius;
        }

        /// <summary>空间哈希。对应 UE5 THierarchicalHashGrid2D。</summary>
        public readonly PedHashGrid Grid = new PedHashGrid { CellSize = 1200f };

        private readonly Dictionary<MassEntityHandle, Obstacle> _byEntity = new Dictionary<MassEntityHandle, Obstacle>();
        private readonly Dictionary<Actor, Obstacle> _byActor = new Dictionary<Actor, Obstacle>();

        /// <summary>登记/更新一个实体障碍。</summary>
        public void Update(MassEntityHandle entity, Vector3 location, Vector3 velocity, float radius)
        {
            var obstacle = new Obstacle { Entity = entity, Location = location, Velocity = velocity, Radius = radius };
            _byEntity[entity] = obstacle;
            Grid.Insert(entity, location, radius);
        }

        /// <summary>登记/更新一个 Actor 障碍（车辆等由 Actor 驱动的对象）。对应 UE5 载具同时是 Mass 实体也是 Actor。</summary>
        public void Update(Actor actor, Vector3 location, Vector3 velocity, float radius)
        {
            if (actor == null) return;
            _byActor[actor] = new Obstacle { SourceActor = actor, Location = location, Velocity = velocity, Radius = radius };
        }

        /// <summary>移除实体障碍。</summary>
        public void Remove(MassEntityHandle entity)
        {
            _byEntity.Remove(entity);
            Grid.Remove(entity);
        }

        public void Remove(Actor actor)
        {
            if (actor != null) _byActor.Remove(actor);
        }

        /// <summary>就近取障碍快照。对应 UE5 <c>FMassEntityView</c> 读取片段。</summary>
        public bool TryGetObstacle(MassEntityHandle entity, out Obstacle obstacle)
        {
            return _byEntity.TryGetValue(entity, out obstacle);
        }

        /// <summary>
        /// 检索范围内的障碍（实体 + Actor）。
        /// 对应 UE5 <c>VehicleSubsystem.VehicleObstacles.Query(ObstacleQueryExtent, NearbyObstacles)</c>。
        /// </summary>
        public void Query(Vector3 center, float extent, List<Obstacle> outObstacles)
        {
            if (outObstacles == null) return;

            var handles = new List<MassEntityHandle>();
            Grid.Query(center, extent, handles);
            for (int i = 0; i < handles.Count; i++)
            {
                if (_byEntity.TryGetValue(handles[i], out var obstacle))
                {
                    outObstacles.Add(obstacle);
                }
            }

            foreach (var kvp in _byActor)
            {
                if (kvp.Value.SourceActor == null) continue;
                if (Vector3.DistanceSquared(kvp.Value.Location, center) <= extent * extent)
                {
                    outObstacles.Add(kvp.Value);
                }
            }
        }

        /// <summary>清空。</summary>
        public void Clear()
        {
            _byEntity.Clear();
            _byActor.Clear();
            Grid.Clear();
        }

        /// <summary>当前登记的 Actor 障碍数（诊断用）。</summary>
        public int ActorObstacleCount => _byActor.Count;
    }
}

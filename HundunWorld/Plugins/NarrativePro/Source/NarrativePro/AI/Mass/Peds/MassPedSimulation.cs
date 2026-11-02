using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Character;
using NarrativePro.Core;
using NarrativePro.Vehicles.Mass;

namespace NarrativePro.AI.Mass.Peds
{
    /// <summary>
    /// 行人 Mass 模拟驱动。对应 UE5 的 <c>UMassSimulationSubsystem</c> +
    /// <c>UMassEntitySubsystem</c> + 场景中的 <c>AMassSpawner</c> 组合。
    /// <para>
    /// Flax 没有 Mass，所以本类是手写的宿主：持有实体管理器，按分组顺序执行处理器，
    /// 每帧同步 LOD/障碍/加速度，并对外提供「按模板批量生成实体」的入口。
    /// </para>
    /// </summary>
    public class MassPedSimulation : Script
    {
        /// <summary>单例。</summary>
        public static MassPedSimulation Instance { get; private set; }

        /// <summary>行人模板名。对应 UE5 Mass 实体模板。</summary>
        [Serialize]
        public string TemplateName = "NarrativePed";

        /// <summary>群集可视化特质（决定模板片段集合）。对应 UE5 trait 列表。</summary>
        public NarrativeVisualizationTrait VisualizationTrait = new NarrativeVisualizationTrait();

        /// <summary>incoming 碰撞检测特质。对应 UE5 <c>UIncomingCollisionDetectionTrait</c>。</summary>
        public IncomingCollisionDetectionTrait CollisionDetectionTrait = new IncomingCollisionDetectionTrait();

        /// <summary>半径特质。对应 UE5 <c>UMassRadiusTrait</c>。</summary>
        public MassRadiusTrait RadiusTrait = new MassRadiusTrait { Radius = 40f };

        /// <summary>加速度同步特质。对应 UE5 <c>UAgentAccelerationSyncTrait</c>。</summary>
        public AgentAccelerationSyncTrait AccelerationTrait = new AgentAccelerationSyncTrait();

        /// <summary>危险检测共享配置。对应 UE5 <c>FDangerDetectionSharedFragment</c>。</summary>
        public DangerDetectionSharedFragment DangerDetection = new DangerDetectionSharedFragment();

        /// <summary>LOD 成本控制。</summary>
        public EntityLODCostControl LodCostControl = new EntityLODCostControl();

        /// <summary>是否自动把场景中的载具登记为危险障碍。</summary>
        [Serialize]
        public bool bRegisterVehiclesAsDangerous = true;

        /// <summary>
        /// 载具障碍重扫间隔（秒）。与 <c>VehicleObstacleProcessor.ExternalRefreshInterval</c> 同一节奏：
        /// 载具是 Actor、没有实体观察者可增量维护，只能周期扫描——但没必要逐帧扫。
        /// </summary>
        [Serialize]
        public float VehicleObstacleSyncInterval = 0.25f;

        /// <summary>是否自动按本地玩家位置更新 LOD。</summary>
        [Serialize]
        public bool bAutoUpdateObserver = true;

        /// <summary>实体管理器。</summary>
        public NarrativePedEntityManager EntityManager { get; private set; }

        /// <summary>障碍登记表。</summary>
        public PedObstacleRegistry Obstacles { get; private set; }

        /// <summary>本模拟生成的实体（用于整体销毁）。对应 UE5 <c>FSpawnedEntities</c>。</summary>
        public SpawnedEntities SpawnedEntities { get; } = new SpawnedEntities();

        private PedRepresentationLODProcessor _lodProcessor;
        private NarrativePedMovementProcessor _movementProcessor;
        private IncomingCollisionProcessor _collisionProcessor;
        private PedObstacleSyncProcessor _obstacleSyncProcessor;
        private NarrativePedAutoDestroyProcessor _autoDestroyProcessor;
        private MassMovementToActorAccelerationTranslator _accelerationTranslator;
        private Vector3 _observerLocation;

        public override void OnEnable()
        {
            base.OnEnable();
            if (Instance != null && Instance != this)
            {
                NarrativeLog.LogWarning("已存在 MassPedSimulation，当前实例不再生效");
                return;
            }
            Instance = this;

            EntityManager = new NarrativePedEntityManager();
            Obstacles = new PedObstacleRegistry();

            // 注册模板（对应 UE5 由 trait 列表构建实体模板）
            var template = BuildTemplate();
            EntityManager.RegisterTemplate(TemplateName, template);

            // 处理器注册顺序 = ExecutionPriority 升序：观察器 → 障碍同步 → 避让 → 移动 → LOD → 加速度翻译 → 自动销毁
            EntityManager.AddObserver(new NarrativePedInitializerProcessor());
            _obstacleSyncProcessor = new PedObstacleSyncProcessor { Simulation = this };
            EntityManager.AddProcessor(_obstacleSyncProcessor);
            _collisionProcessor = new IncomingCollisionProcessor { Obstacles = Obstacles };
            EntityManager.AddProcessor(_collisionProcessor);
            _movementProcessor = new NarrativePedMovementProcessor();
            EntityManager.AddProcessor(_movementProcessor);
            _lodProcessor = new PedRepresentationLODProcessor { Simulation = this, LodCostControl = LodCostControl };
            EntityManager.AddProcessor(_lodProcessor);
            _accelerationTranslator = new MassMovementToActorAccelerationTranslator();
            EntityManager.AddProcessor(_accelerationTranslator);
            _autoDestroyProcessor = new NarrativePedAutoDestroyProcessor();
            EntityManager.AddProcessor(_autoDestroyProcessor);

            EntityManager.GetOrCreateConstSharedFragment(DangerDetection);

            _observerLocation = Actor != null ? Actor.Position : Vector3.Zero;
        }

        public override void OnDisable()
        {
            DespawnAll();
            Obstacles?.Clear();
            EntityManager?.Reset();
            if (Instance == this) Instance = null;
            base.OnDisable();
        }

        public override void OnUpdate()
        {
            if (EntityManager == null) return;

            if (bAutoUpdateObserver)
            {
                _observerLocation = ResolveObserverLocation();
            }

            if (bRegisterVehiclesAsDangerous)
            {
                // 缺陷③：SyncVehicleObstacles 每次都要 Level.GetActors + 逐 Actor GetScript，
                // 逐帧执行在人群场景里是纯 CPU 浪费；改按 VehicleObstacleSyncInterval（默认 0.25s）
                // 的节律重扫，速度用两次重扫之间的真实经过时间做差分。
                _vehicleSyncElapsed += Time.DeltaTime;
                _vehicleSyncTimer -= Time.DeltaTime;
                if (_vehicleSyncTimer <= 0f)
                {
                    SyncVehicleObstacles(_vehicleSyncElapsed);
                    _vehicleSyncElapsed = 0f;
                    _vehicleSyncTimer = Mathf.Max(VehicleObstacleSyncInterval, 1e-3f);
                }
            }

            EntityManager.Tick(Time.DeltaTime);
        }

        /// <summary>构建行人实体模板。对应 UE5 由 trait 组合出模板。</summary>
        public PedEntityTemplateBuildContext BuildTemplate()
        {
            var context = new PedEntityTemplateBuildContext { TemplateName = TemplateName };
            VisualizationTrait.BuildTemplate(context, EntityManager);
            CollisionDetectionTrait.BuildTemplate(context, EntityManager);
            RadiusTrait.BuildTemplate(context, EntityManager);
            AccelerationTrait.BuildTemplate(context, EntityManager);
            return context;
        }

        /// <summary>
        /// 运行期配置行人定义集合并重建模板。
        /// <para>
        /// 为什么需要：Flax 的 <c>AddScript</c> 在挂上 Actor 当刻就执行 <c>OnEnable</c>，
        /// 外部没有机会在模板注册「之前」往 <see cref="VisualizationTrait"/> 写数据
        /// （UE5 的 trait 列表是编辑器里在类默认对象上配好的）。这里按 UE5「改 trait 参数后重建模板」
        /// 的语义，在实体生成之前把新路径表灌进去并覆盖同名模板，已存在的实体不受影响。
        /// </para>
        /// </summary>
        /// <param name="pedDefinitionPaths">行人 NPC 定义路径（需在 <see cref="NarrativePedAssetResolver"/> 可解析）。</param>
        /// <returns>是否成功重建模板。</returns>
        public bool ConfigurePedDefinitions(System.Collections.Generic.IEnumerable<string> pedDefinitionPaths)
        {
            if (EntityManager == null || VisualizationTrait?.PedProperties == null) return false;

            var paths = new System.Collections.Generic.List<string>();
            if (pedDefinitionPaths != null)
            {
                foreach (var path in pedDefinitionPaths)
                {
                    if (!string.IsNullOrEmpty(path) && !paths.Contains(path)) paths.Add(path);
                }
            }

            if (paths.Count == 0)
            {
                NarrativeLog.LogWarning("MassPedSimulation: 未提供任何行人定义路径，模板保持原状。");
                return false;
            }

            if (_bPedTemplateConfigured
                && EntityManager.Templates.TryGetValue(TemplateName, out _)
                && System.Linq.Enumerable.SequenceEqual(VisualizationTrait.PedProperties.NarrativePedPaths, paths))
            {
                return true;
            }

            VisualizationTrait.PedProperties.NarrativePedPaths = paths;

            var template = BuildTemplate();
            EntityManager.RegisterTemplate(TemplateName, template);
            _bPedTemplateConfigured = true;

            NarrativeLog.Log($"MassPedSimulation: 已按 {paths.Count} 条行人定义重建模板 '{TemplateName}'。");
            return true;
        }

        private bool _bPedTemplateConfigured;

        /// <summary>
        /// 批量生成行人实体。对应 UE5 <c>AMassSpawner</c> 的生成流程。
        /// </summary>
        /// <param name="count">生成数量。</param>
        /// <param name="spawnCenter">生成中心。</param>
        /// <param name="spreadRadius">散布半径。</param>
        public List<MassEntityHandle> SpawnPedGroup(int count, Vector3 spawnCenter, float spreadRadius = 400f)
        {
            var created = new List<MassEntityHandle>();
            if (EntityManager == null || count <= 0) return created;

            var template = EntityManager.Templates.TryGetValue(TemplateName, out var existing) ? existing : BuildTemplate();

            for (int i = 0; i < count; i++)
            {
                Vector3 offset;
                lock (_random)
                {
                    float angle = (float)(_random.NextDouble() * 2.0 * Math.PI);
                    float spawnRadius = spreadRadius * (float)_random.NextDouble();
                    offset = new Vector3(Mathf.Cos(angle) * spawnRadius, 0f, Mathf.Sin(angle) * spawnRadius);
                }

                Vector3 location = spawnCenter + offset;
                if (NarrativePro.Navigation.NavigatorStatics.TryFindClosestPoint(location, out var projected))
                {
                    location = projected;
                }

                var entity = EntityManager.CreateEntity(template);
                if (!entity.IsValid) continue;

                if (EntityManager.TryGetFragmentData<PedTransformFragment>(entity, out var transform))
                {
                    transform.Location = location;
                }

                // 给群体一个缓慢的初始期望速度，形成 UE5 群集那种「在周围游走」的观感
                if (EntityManager.TryGetFragmentData<PedVelocityFragment>(entity, out var velocity))
                {
                    lock (_random)
                    {
                        float angle = (float)(_random.NextDouble() * 2.0 * Math.PI);
                        velocity.Value = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 60f;
                    }
                }

                // 实体之间互为障碍（对应 UE5 载具/行人共享障碍哈希）
                if (EntityManager.TryGetFragmentData<AgentRadiusFragment>(entity, out var radius))
                {
                    Obstacles.Update(entity, location, Vector3.Zero, radius.Radius);
                }

                created.Add(entity);
                if (!SpawnedEntities.Entities.Contains(entity))
                {
                    SpawnedEntities.Entities.Add(entity);
                }
            }

            // 结构性变更（加片段/观察器派发）后立刻冲刷，让初始化器在同一帧生效
            EntityManager.FlushDeferredCommands();
            return created;
        }

        /// <summary>销毁全部行人实体与其表现 Actor。对应 UE5 用 <c>FSpawnedEntities</c> 反查销毁。</summary>
        public void DespawnAll()
        {
            if (EntityManager == null) return;

            foreach (var entity in SpawnedEntities.Entities)
            {
                Obstacles?.Remove(entity);
                EntityManager.DestroyEntity(entity);
            }
            SpawnedEntities.Entities.Clear();
        }

        /// <summary>销毁单个实体。</summary>
        public bool DespawnEntity(MassEntityHandle entity)
        {
            if (EntityManager == null || !entity.IsValid) return false;
            Obstacles?.Remove(entity);
            SpawnedEntities.Entities.Remove(entity);
            return EntityManager.DestroyEntity(entity);
        }

        /// <summary>当前观察者位置（本地玩家Pawn，退回场景相机/生成器自身）。对应 UE5 LOD 以视口为基准。</summary>
        public Vector3 ResolveObserverLocation()
        {
            var player = Level.FindScript<NarrativePro.UnrealFramework.NarrativePlayerCharacter>();
            if (player != null && player.Actor != null)
            {
                return player.Actor.Position;
            }

            var narrativeCharacter = Level.FindScript<NarrativePro.UnrealFramework.NarrativeCharacter>();
            if (narrativeCharacter?.Actor != null)
            {
                return narrativeCharacter.Actor.Position;
            }

            return Actor != null ? Actor.Position : Vector3.Zero;
        }

        /// <summary>
        /// 把场景载具登记成带 <see cref="DangerousEntity"/> 语义的障碍，
        /// 速度由位置差分推导（Flax 载具未暴露速度接口）。
        /// </summary>
        /// <param name="syncElapsed">距上次重扫的真实经过时间（秒），作速度差分的时间基。</param>
        private void SyncVehicleObstacles(float syncElapsed)
        {
            if (Obstacles == null) return;

            _vehiclePreviousPositions ??= new Dictionary<Actor, Vector3>();

            var seen = _vehicleSeen ??= new HashSet<Actor>();
            seen.Clear();

            // 重扫节奏下（默认 0.25s 一次）用两次重扫之间的经过时间做差分，速度量纲与逐帧版一致。
            float deltaTime = Mathf.Max(syncElapsed, 1e-4f);

            try
            {
                foreach (var vehicle in Level.GetActors<Actor>())
                {
                    if (vehicle == null) continue;
                    var baseVehicle = vehicle.GetScript<NarrativePro.Vehicles.NarrativeVehicleBase>();
                    if (baseVehicle == null) continue;

                    seen.Add(vehicle);

                    Vector3 location = vehicle.Position;
                    Vector3 velocity = Vector3.Zero;
                    if (_vehiclePreviousPositions.TryGetValue(vehicle, out var previous))
                    {
                        velocity = (location - previous) / deltaTime;
                    }
                    _vehiclePreviousPositions[vehicle] = location;

                    float radius = ArsenalStatics.DefaultAgentRadius;
                    Obstacles.Update(vehicle, location, velocity, radius);

                    // 车速超过阈值才算「危险实体」（对应 UE5 MinSpeed 语义）
                    if (velocity.Length >= DangerDetection.MinSpeed)
                    {
                        _dangerousVehicles.Add(vehicle);
                    }
                    else
                    {
                        _dangerousVehicles.Remove(vehicle);
                    }
                }
            }
            catch
            {
                // 场景未就绪
            }

            // 清掉已销毁载具的跟踪状态
            if (_vehiclePreviousPositions != null && _vehiclePreviousPositions.Count > seen.Count)
            {
                var stale = _staleBuffer ??= new List<Actor>();
                stale.Clear();
                foreach (var key in _vehiclePreviousPositions.Keys)
                {
                    if (!seen.Contains(key)) stale.Add(key);
                }
                foreach (var key in stale)
                {
                    _vehiclePreviousPositions.Remove(key);
                    Obstacles.Remove(key);
                    _dangerousVehicles.Remove(key);
                }
            }
        }

        private static readonly System.Random _random = new System.Random();
        private float _vehicleSyncTimer;
        private float _vehicleSyncElapsed;
        private Dictionary<Actor, Vector3> _vehiclePreviousPositions;
        private HashSet<Actor> _vehicleSeen;
        private List<Actor> _staleBuffer;
        private readonly HashSet<Actor> _dangerousVehicles = new HashSet<Actor>();

        /// <summary>当前被判定为危险的载具集合（车速 ≥ <see cref="DangerDetectionSharedFragment.MinSpeed"/>）。</summary>
        public IReadOnlyCollection<Actor> DangerousVehicles => _dangerousVehicles;

        // ===== 供处理器访问的内部视图 =====

        internal Vector3 ObserverLocation => _observerLocation;
        internal NarrativePedMovementProcessor MovementProcessor => _movementProcessor;
    }

    /// <summary>
    /// 行人障碍同步处理器。对应 UE5 由 <c>UMassVehicleSubsystem</c> 维护
    /// <c>VehicleObstacles</c> 哈希（此处把行人自身位置/速度刷新进去）。
    /// </summary>
    public class PedObstacleSyncProcessor : PedProcessor
    {
        public MassPedSimulation Simulation;

        public PedObstacleSyncProcessor()
        {
            ExecuteInGroup = "Avoidance";
            ExecutionPriority = 50;
        }

        public override void Execute(float deltaTime)
        {
            if (EntityManager == null || Simulation?.Obstacles == null) return;

            EntityManager.ForEachEntity(entity =>
            {
                if (!EntityManager.TryGetFragmentData<PedTransformFragment>(entity, out var transform)) return;
                EntityManager.TryGetFragmentData<AgentRadiusFragment>(entity, out var radius);
                EntityManager.TryGetFragmentData<PedVelocityFragment>(entity, out var velocity);

                Simulation.Obstacles.Update(entity,
                    transform.Location,
                    velocity != null ? velocity.Value : Vector3.Zero,
                    radius != null ? radius.Radius : 40f);
            }, typeof(PedTransformFragment));
        }
    }

    /// <summary>
    /// 行人 LOD 表现处理器。对应 UE5 Mass Representation 的 LOD 分档 + Actor 复用，
    /// 档位与配额来自 <see cref="EntityLODCostControl"/>（<c>EntityLOD.h</c> 的成本侧）。
    /// </summary>
    public class PedRepresentationLODProcessor : PedProcessor
    {
        public MassPedSimulation Simulation;
        public EntityLODCostControl LodCostControl = new EntityLODCostControl();

        public PedRepresentationLODProcessor()
        {
            ExecuteInGroup = "Representation";
            ExecutionPriority = 400;
        }

        public override void Execute(float deltaTime)
        {
            if (EntityManager == null || Simulation == null) return;

            var representationSubsystem = MassPedRepresentationSubsystem.Get();
            if (LodCostControl != null)
            {
                representationSubsystem.LodCostControl = LodCostControl;
            }

            Vector3 observer = Simulation.ObserverLocation;

            EntityManager.ForEachEntity(entity =>
            {
                representationSubsystem.UpdateEntityRepresentation(EntityManager, entity, observer);

                // Actor 位置回写：High/Medium 档才有 Actor
                var actor = representationSubsystem.GetActorForEntity(entity);
                if (actor == null) return;
                if (!EntityManager.TryGetFragmentData<PedTransformFragment>(entity, out var transform)) return;

                if (actor.Position != transform.Location)
                {
                    actor.Position = transform.Location;
                }
                if (actor.Orientation != transform.Rotation)
                {
                    actor.Orientation = transform.Rotation;
                }
            }, typeof(PedTransformFragment), typeof(PedRepresentationFragment));
        }
    }

    /// <summary>
    /// 低档实体自动销毁处理器。对应 UE5 <c>UAutoDestroyProcessor</c>
    /// （<c>Vehicles/Mass/AutoDestroyProcessor.h</c>：「实体处于 Low LOD 且带 AutoDestroyTag 时自动销毁」）。
    /// </summary>
    public class NarrativePedAutoDestroyProcessor : PedProcessor
    {
        public NarrativePedAutoDestroyProcessor()
        {
            ExecuteInGroup = "Representation";
            ExecutionPriority = 500;
        }

        public override void Execute(float deltaTime)
        {
            if (EntityManager == null) return;

            var pending = new List<MassEntityHandle>();

            EntityManager.ForEachEntity(entity =>
            {
                if (!EntityManager.TryGetFragmentData<PedRepresentationFragment>(entity, out var representation)) return;
                if (!representation.bAutoDestroy) return;
                if (representation.CurrentLOD == EEntityLOD.Low || representation.CurrentLOD == EEntityLOD.Off)
                {
                    pending.Add(entity);
                }
            }, typeof(PedRepresentationFragment));

            if (pending.Count == 0) return;

            EntityManager.Defer(() =>
            {
                foreach (var entity in pending)
                {
                    EntityManager.DestroyEntity(entity);
                }
            });
        }
    }

    /// <summary>
    /// 加速度同步特质。对应 UE5 <c>UAgentAccelerationSyncTrait</c>
    /// （<c>Public/AI/Mass/Peds/AgentAccelerationSyncTrait.h</c>）：给实体挂 <see cref="PedAccelerationFragment"/>。
    /// </summary>
    [Serializable]
    public class AgentAccelerationSyncTrait : PedEntityTraitBase
    {
        public override void BuildTemplate(PedEntityTemplateBuildContext buildContext, NarrativePedEntityManager world)
        {
            buildContext.AddFragment<PedAccelerationFragment>();
        }
    }

    /// <summary>
    /// Mass → Actor 的加速度翻译器。对应 UE5 <c>UMassMovementToActorAccelerationTranslator</c>
    /// （<c>Peds/MassMovementToActorAccelerationTranslator.h/.cpp</c>，继承 <c>UMassTranslator</c>）。
    /// <para>
    /// 可观测行为：把实体上的 Mass 加速度搬到 Actor 的移动组件上，
    /// 让本地真实的 <see cref="NarrativeCharacterMovement"/> 表现与 Mass 侧一致。
    /// </para>
    /// </summary>
    public class MassMovementToActorAccelerationTranslator : PedProcessor
    {
        public MassMovementToActorAccelerationTranslator()
        {
            ExecuteInGroup = "Representation";
            ExecutionPriority = 450;
        }

        public override void Execute(float deltaTime)
        {
            if (EntityManager == null) return;

            var representation = MassPedRepresentationSubsystem.Get();

            EntityManager.ForEachEntity(entity =>
            {
                if (!EntityManager.TryGetFragmentData<PedAccelerationFragment>(entity, out var acceleration)) return;
                if (!acceleration.bUpdatedFromMass) return;

                Actor actor = representation.GetActorForEntity(entity);
                if (actor == null)
                {
                    if (EntityManager.TryGetFragmentData<PedActorFragment>(entity, out var actorFragment))
                    {
                        actor = actorFragment.SpawnedActor;
                    }
                }
                if (actor == null) return;

                // 对应 UE5 把 Mass 加速度写进 Actor 的移动组件。
                // Character 模块的 NarrativeCharacterMovement 尚未提供公开的加速度入口，
                // 因此这里落到行人 Agent 组件上并以事件暴露（Character 侧订阅即可），
                // 同时若将来出现 CharacterController，就直接把速度交给它。
                var agentComponent = actor.GetScript<NarrativeMassAgentComponent>();
                agentComponent?.ApplyMassAcceleration(acceleration.ActorAcceleration);

                var controller = actor.GetChild<CharacterController>();
                if (controller != null && acceleration.ActorAcceleration.LengthSquared > Mathf.Epsilon)
                {
                    controller.Direction = acceleration.ActorAcceleration;
                }

                acceleration.bUpdatedFromMass = false;
            }, typeof(PedAccelerationFragment));
        }
    }

    /// <summary>
    /// 行人生成器（可放置在场景中）。对应 UE5 <c>AMassSpawner</c> 的行人版
    /// （UE5 侧由 <c>UMassPedSpawnerSubsystem</c> 承担实际 Actor 生成，本类只是场景入口）。
    /// </summary>
    public class MassPedSpawner : Script
    {
        /// <summary>生成数量。对应 UE5 <c>SetSpawnCount</c>。</summary>
        [Serialize]
        public int SpawnCount = 8;

        /// <summary>散布半径。</summary>
        [Serialize]
        public float SpreadRadius = 400f;

        /// <summary>是否在启用时自动生成。</summary>
        [Serialize]
        public bool bSpawnOnEnable = true;

        /// <summary>最近一次生成的实体。</summary>
        public SpawnedEntities SpawnedEntities { get; } = new SpawnedEntities();

        public override void OnEnable()
        {
            base.OnEnable();
            if (bSpawnOnEnable)
            {
                TrySpawn();
            }
        }

        public override void OnDisable()
        {
            RemoveSpawned();
            base.OnDisable();
        }

        /// <summary>设置生成数量（下次生成生效）。对应 UE5 <c>SetSpawnCount</c>。</summary>
        public virtual void SetSpawnCount(int newCount)
        {
            SpawnCount = Mathf.Max(0, newCount);
        }

        /// <summary>执行生成。</summary>
        public bool TrySpawn()
        {
            var simulation = MassPedSimulation.Instance;
            if (simulation == null)
            {
                NarrativeLog.LogWarning("MassPedSpawner: 场景中没有 MassPedSimulation，行人未生成。");
                return false;
            }

            var created = simulation.SpawnPedGroup(SpawnCount, Actor != null ? Actor.Position : Vector3.Zero, SpreadRadius);
            foreach (var entity in created)
            {
                if (!SpawnedEntities.Entities.Contains(entity))
                {
                    SpawnedEntities.Entities.Add(entity);
                }
            }
            return created.Count > 0;
        }

        /// <summary>移除本生成器生成的实体。</summary>
        public void RemoveSpawned()
        {
            var simulation = MassPedSimulation.Instance;
            if (simulation == null) return;
            foreach (var entity in SpawnedEntities.Entities)
            {
                simulation.DespawnEntity(entity);
            }
            SpawnedEntities.Entities.Clear();
        }
    }
}

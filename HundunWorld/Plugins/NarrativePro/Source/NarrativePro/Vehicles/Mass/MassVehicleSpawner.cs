using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.Vehicles.Mass
{
    /// <summary>
    /// Mass 载具生成器（Flax 侧手写实现）。对应 UE5 AMassVehicleSpawner
    /// （MassVehicleSpawner.h/.cpp:11-29 —— 构造函数建注解组件 + 注册
    /// <c>URoadControlsSpawnPointsGenerator</c> 作为生成数据源，<c>SetSpawnCount</c> 改数量）。
    /// <para>
    /// UE5 的生成能力来自 MassSpawner 基类（按 SpawnDataGenerators 产出的变换创建实体）。
    /// Flax 无 Mass，本 Script 承担同一角色：按 <see cref="SpawnCount"/> 保持存活车流，
    /// 出生点由 <see cref="RoadControlsSpawnPointsGenerator"/> 在真实车道上给出，
    /// 实体由 <see cref="VehicleTrait.BuildVehicle"/> + <see cref="InitVehicleProcessor"/> 装配。
    /// 它是可挂到任意 Actor 上的 Script（场景摆放或由
    /// <see cref="VehicleTrafficSimulation"/> 按 JSON 生成），因此不依赖任何关卡资产。
    /// </para>
    /// </summary>
    public class MassVehicleSpawner : Script
    {
        /// <summary>场景内全部生成器。对应 UE5 <c>GetActorOfClass</c> 的作用域。</summary>
        public static readonly List<MassVehicleSpawner> AllSpawners = new List<MassVehicleSpawner>();

        /// <summary>模板名（决定用哪份 <see cref="VehicleTrait"/>）。对应 UE5 EntityAsset。</summary>
        [Serialize]
        public string TemplateName = "TrafficVehicle";

        /// <summary>应维持的存活载具数。对应 UE5 AMassSpawner 的 Count。</summary>
        [Serialize]
        public int SpawnCount = 2;

        /// <summary>补员节奏（秒）。UE5 只在 BeginPlay 生成一次，Flax 侧为了演示持续车流按节奏补齐。</summary>
        [Serialize]
        public float RefillIntervalSeconds = 3f;

        /// <summary>是否启用。</summary>
        [Serialize]
        public bool bAutoSpawn = true;

        /// <summary>随机选择种子。对应 UE5 <c>GetRandomSelectionSeed()</c>。</summary>
        [Serialize]
        public int RandomSelectionSeed = 0;

        /// <summary>交叉口注解组件。对应 UE5 IntersectionAnnotations（UTrafficIntersectionAnnotations*）。</summary>
        [NonSerialized]
        public TrafficIntersectionAnnotations IntersectionAnnotations;

        /// <summary>关联的道路控制器。对应 UE5 AQuestRoadControls 反向引用的 VehicleSpawner。</summary>
        [NonSerialized]
        public QuestRoadControls RoadControls;

        /// <summary>本生成器生成的实体。对应 UE5 <c>FSpawnedEntities</c>。</summary>
        private readonly List<MassEntityHandle> _spawned = new List<MassEntityHandle>();

        private float _refillTimer;

        /// <summary>本生成器当前存活实体数。</summary>
        public int AliveCount
        {
            get
            {
                var subsystem = MassVehicleSubsystem.Instance;
                if (subsystem == null) return 0;
                int alive = 0;
                foreach (var handle in _spawned)
                {
                    if (subsystem.Contains(handle)) alive++;
                }
                return alive;
            }
        }

        /// <summary>累计生成数（诊断自证）。</summary>
        public int TotalSpawned { get; private set; }

        public override void OnEnable()
        {
            base.OnEnable();
            if (!AllSpawners.Contains(this)) AllSpawners.Add(this);

            // 对应 UE5 构造函数里的 CreateDefaultSubobject<UTrafficIntersectionAnnotations>
            if (IntersectionAnnotations == null && Actor != null)
            {
                IntersectionAnnotations = Actor.GetScript<TrafficIntersectionAnnotations>();
            }
            _refillTimer = RefillIntervalSeconds;
        }

        public override void OnDisable()
        {
            DespawnAll();
            AllSpawners.Remove(this);
            base.OnDisable();
        }

        public override void OnUpdate()
        {
            if (!bAutoSpawn) return;

            _refillTimer += (float)Time.DeltaTime;
            if (_refillTimer < Mathf.Max(0.1f, RefillIntervalSeconds)) return;
            _refillTimer = 0f;

            if (MassVehicleSubsystem.Instance == null) return;
            if (AliveCount >= SpawnCount) return;
            TrySpawn();
        }

        /// <summary>设置生成数量（下次使用此生成器生成实体时生效）。对应 UE5 SetSpawnCount（cpp:24-28）。</summary>
        /// <param name="newCount">新的生成数量。</param>
        public virtual void SetSpawnCount(int newCount)
        {
            SpawnCount = Mathf.Max(0, newCount);
            MassVehicleLog.Log($"MassVehicleSpawner[{Actor?.Name}] 生成数量 → {SpawnCount}");
        }

        /// <summary>道路控制器绑定时调用。</summary>
        public void BindRoadControls(QuestRoadControls roadControls)
        {
            RoadControls = roadControls;
        }

        /// <summary>
        /// 补一轮生成。对应 UE5 MassSpawner 的生成流程（生成点 → 实体 → 初始化观察者）。
        /// </summary>
        /// <returns>本次实际生成的实体数。</returns>
        public int TrySpawn()
        {
            var simulation = VehicleTrafficSimulation.Instance;
            if (simulation == null)
            {
                MassVehicleLog.LogWarning("MassVehicleSpawner：场景中没有 VehicleTrafficSimulation，载具未生成。");
                return 0;
            }

            int deficit = Mathf.Max(0, SpawnCount - AliveCount);
            if (deficit <= 0) return 0;

            var trait = simulation.GetTrait(TemplateName);
            if (trait == null) return 0;

            var points = simulation.GenerateSpawnPoints(deficit, RandomSelectionSeed);
            int created = 0;
            foreach (var point in points)
            {
                var vehicle = trait.BuildVehicle(simulation.VehicleSubsystem, point.Location, point.Lane,
                    point.DistanceAlongLane, RandomSelectionSeed);
                if (vehicle == null) continue;

                if (!simulation.InitializeVehicle(vehicle, point.Lane, point.DistanceAlongLane, RandomSelectionSeed))
                {
                    simulation.VehicleSubsystem.RemoveVehicle(vehicle.Handle);
                    continue;
                }

                _spawned.Add(vehicle.Handle);
                created++;
            }

            TotalSpawned += created;
            if (created > 0)
            {
                MassVehicleLog.Log($"MassVehicleSpawner[{Actor?.Name}] 生成 {created} 辆（模板 {TemplateName}，" +
                                   $"存活 {AliveCount}/{SpawnCount}）。");
            }
            return created;
        }

        /// <summary>销毁本生成器生成的全部载具。</summary>
        public void DespawnAll()
        {
            var simulation = VehicleTrafficSimulation.Instance;
            if (simulation != null)
            {
                foreach (var handle in _spawned)
                {
                    simulation.DespawnVehicle(handle);
                }
            }
            _spawned.Clear();
        }
    }
}

using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.AI;
using NarrativePro.Core;

namespace NarrativePro.Vehicles.Mass
{
    /// <summary>Actor 生成请求状态。对应 UE5 <c>ESpawnRequestStatus</c>（MassActorSpawnerSubsystem.h）。</summary>
    public enum EVehicleSpawnRequestStatus
    {
        Failed = 0,
        Succeeded = 1,
        TryAgain = 2,
    }

    /// <summary>
    /// 一条载具 Actor 生成请求。对应 UE5 <c>FSpawnArgs</c> 的 <c>Request</c>（FMassActorSpawnRequest）。
    /// </summary>
    public class VehicleActorSpawnRequest
    {
        /// <summary>请求发起的实体。对应 UE5 Request.MassAgent。</summary>
        public TrafficVehicle Vehicle;

        /// <summary>期望变换。对应 UE5 InOutSpawnParameters 的变换部分。</summary>
        public Transform Transform;

        /// <summary>稳定标识（用于同名复用）。对应 UE5 的 Mass Agent Guid。</summary>
        public Guid Guid;

        /// <summary>模板名。对应 UE5 Template->GetName()。</summary>
        public string TemplateName = "TrafficVehicle";

        /// <summary>结果 Actor。对应 UE5 Request.SpawnedActor。</summary>
        public Actor SpawnedActor;

        /// <summary>是否为池内复用（对应 UE5 命中已有 Actor 的分支）。</summary>
        public bool bFoundExisting;
    }

    /// <summary>
    /// 载具生成子系统（Flax 侧手写宿主）。对应 UE5 UVehicleSpawnerSubsystem
    /// （VehicleSpawnerSubsystem.h/.cpp:22-77）。
    /// <para>
    /// UE5 的 SpawnActor 在父类生成 Actor 之后做四件事：
    /// ① 找 Chaos 轮式移动组件并用 Mass 的速度做 SetBaseSnapshot（cpp:35-52）；
    /// ② 把 Mass 的种子写给载具 <c>SetRandomSeed</c>（cpp:56-57）；
    /// ③ <c>SetManagedByMass(true)</c>（cpp:58）；
    /// ④ 找到 <c>UMountComponent</c> 并把乘客定义交给 <c>AddOccupants</c>（cpp:60-71）。
    /// Flax 无 Chaos（已核对 FlaxEngine.CSharp.xml：Vehicle/WheeledVehicle 0 命中），
    /// ①无法移植 → 改为把速度/朝向写进 Actor 的运动学接口；
    /// ②③④ 完整移植。Actor 本身用「按名池化 + 复用」实现，
    /// 对应 UE5 的 Actor 复用（同名命中即恢复可用态，cpp 的 FindActorByName 分支）。
    /// </para>
    /// </summary>
    public class VehicleSpawnerSubsystem
    {
        /// <summary>当前实例。由 <see cref="VehicleTrafficSimulation"/> 创建/销毁。</summary>
        public static VehicleSpawnerSubsystem Instance { get; private set; }

        /// <summary>获取实例。对应 UE5 <c>GetSubsystem&lt;UVehicleSpawnerSubsystem&gt;</c>。</summary>
        public static VehicleSpawnerSubsystem Get() => Instance;

        /// <summary>载具子系统（存储与实体表）。</summary>
        public MassVehicleSubsystem VehicleSubsystem;

        /// <summary>Actor 到座位索引的映射。对应 UE5 ActorToSeatIndexMap（mutable TMap）。</summary>
        [NonSerialized]
        public Dictionary<Actor, int> ActorToSeatIndexMap = new Dictionary<Actor, int>();

        /// <summary>池化的空闲 Actor（隐藏、待复用）。对应 UE5 的 Actor 回收池。</summary>
        private readonly Queue<Actor> _pool = new Queue<Actor>();

        /// <summary>池上限（防止长时间运行堆积）。</summary>
        public int MaxPoolSize = 8;

        /// <summary>按名索引已生成 Actor。对应 UE5 FindActorByName 的作用。</summary>
        private readonly Dictionary<string, Actor> _spawnedByName = new Dictionary<string, Actor>(StringComparer.Ordinal);

        /// <summary>车身碰撞盒尺寸。对应 UE5 载具蓝图根碰撞体。</summary>
        public Vector3 BodyColliderSize = new Vector3(120f, 90f, 220f);

        /// <summary>本子系统成功生成/复用的 Actor 数（诊断自证）。</summary>
        public int SpawnedCount { get; private set; }

        /// <summary>复用了池内 Actor 的次数。</summary>
        public int ReusedCount { get; private set; }

        public VehicleSpawnerSubsystem(MassVehicleSubsystem vehicleSubsystem)
        {
            VehicleSubsystem = vehicleSubsystem;
        }

        /// <summary>绑定实例。</summary>
        public void Initialize() => Instance = this;

        /// <summary>解绑并销毁托管 Actor。</summary>
        public void Shutdown()
        {
            if (Instance == this) Instance = null;
            foreach (var pair in _spawnedByName)
            {
                if (pair.Value != null) FlaxEngine.Object.Destroy(pair.Value);
            }
            _spawnedByName.Clear();
            _pool.Clear();
            ActorToSeatIndexMap.Clear();
        }

        /// <summary>
        /// 处理一条 Actor 生成请求。对应 UE5 <c>SpawnActor(const FSpawnArgs&amp;, FActorSpawnParameters&amp;)</c>。
        /// </summary>
        public virtual EVehicleSpawnRequestStatus SpawnActor(VehicleActorSpawnRequest request)
        {
            if (request?.Vehicle == null) return EVehicleSpawnRequestStatus.Failed;

            var vehicle = request.Vehicle;
            string actorName = BuildActorName(request);
            request.Transform = new Transform(vehicle.Location, vehicle.Rotation);

            // ① 池化/同名复用（对应 UE5 命中已有 Actor → 恢复可见与碰撞，返回 Succeeded）
            Actor actor = null;
            if (_spawnedByName.TryGetValue(actorName, out var existing) && existing != null)
            {
                actor = existing;
                request.bFoundExisting = true;
            }
            else if (_pool.Count > 0)
            {
                actor = _pool.Dequeue();
                request.bFoundExisting = true;
                ReusedCount++;
            }

            if (actor != null && actor.Parent == null)
            {
                actor.Position = request.Transform.Translation;
                actor.Orientation = request.Transform.Orientation;
                actor.IsActive = true;
            }
            else
            {
                actor = null;
            }

            // ② 无可用 Actor → 新建（对应 UE5 World->SpawnActor(VehicleClass)）
            if (actor == null)
            {
                actor = CreateVehicleActor(request, actorName);
                if (actor == null) return EVehicleSpawnRequestStatus.Failed;
            }

            var vehicleActor = actor.GetScript<TrafficVehicleActor>();
            if (vehicleActor == null)
            {
                // 池内 Actor 缺脚本（被外部销毁/重建）→ 放弃本次，下一帧重试
                return EVehicleSpawnRequestStatus.TryAgain;
            }

            // ③ 运动学初始化（替代 UE5 的 Chaos SetBaseSnapshot，cpp:46-53）
            vehicleActor.ConfigureFromEntity(vehicle, request.bFoundExisting, request.Transform,
                vehicle.Velocity, vehicle.Speed);

            // ④ 种子 + Mass 管理标记（cpp:56-58）
            vehicleActor.SetRandomSeed(vehicle.Seed != null ? vehicle.Seed.Seed : 0);
            vehicleActor.SetManagedByMass(true);

            // ⑤ 挂载乘员（cpp:60-71：找到 UMountComponent → AddOccupants(PassengerDefs, Seed)）
            var mount = vehicleActor.GetMountComponent();
            if (mount != null)
            {
                var passengerDefs = new List<NPCDefinition>();
                if (vehicle.Passengers?.Passengers != null)
                {
                    foreach (var pair in vehicle.Passengers.Passengers)
                    {
                        passengerDefs.Add(pair.Value);
                        // 座位映射同时登记（对应 UE5 ActorToSeatIndexMap 在 MountComp 内的等价用途）
                        ActorToSeatIndexMap[actor] = pair.Key;
                    }
                }
                if (passengerDefs.Count > 0)
                {
                    mount.AddOccupants(passengerDefs, vehicle.Seed != null ? vehicle.Seed.Seed : -1);
                }
            }

            vehicle.Actor = actor;
            vehicle.ComponentWrapper.Vehicle = vehicleActor;
            vehicle.LOD = EVehicleRepresentationLOD.High;
            _spawnedByName[actorName] = actor;
            SpawnedCount++;
            request.SpawnedActor = actor;
            return EVehicleSpawnRequestStatus.Succeeded;
        }

        /// <summary>归还 Actor 进池（低 LOD 或实体销毁时）。对应 UE5 Mass representation 的 Actor 回收。</summary>
        public void ReleaseActor(TrafficVehicle vehicle)
        {
            if (vehicle == null) return;
            var actor = vehicle.Actor;
            vehicle.Actor = null;
            vehicle.ComponentWrapper.Vehicle = null;

            if (actor == null) return;

            _spawnedByName.Remove(BuildActorName(vehicle));
            ActorToSeatIndexMap.Remove(actor);

            var vehicleActor = actor.GetScript<TrafficVehicleActor>();
            vehicleActor?.NotifyReleased();

            actor.IsActive = false;
            if (_pool.Count < MaxPoolSize)
            {
                _pool.Enqueue(actor);
            }
            else
            {
                FlaxEngine.Object.Destroy(actor);
            }
        }

        /// <summary>彻底销毁池内 Actor（关闭模拟时）。</summary>
        public void ClearPool()
        {
            while (_pool.Count > 0)
            {
                var actor = _pool.Dequeue();
                if (actor != null) FlaxEngine.Object.Destroy(actor);
            }
        }

        private Actor CreateVehicleActor(VehicleActorSpawnRequest request, string actorName)
        {
            var actor = new EmptyActor
            {
                Name = actorName,
            };

            // 世界对象必须显式生成进玩法场景：无父级版 SpawnActor 固定落 Scenes[0]（RootScene）。
            // Flax 约定：返回 true 才是失败
            if (NarrativePro.CommonUI.NarrativeRuntimeBootstrap.TrySpawnToGameplayScene(actor, actorName))
            {
                MassVehicleLog.LogError($"载具 Actor 生成失败：{actorName}");
                FlaxEngine.Object.Destroy(actor);
                return null;
            }

            actor.Position = request.Transform.Translation;
            actor.Orientation = request.Transform.Orientation;

            // 车身碰撞盒：无碰撞的裸 Actor 对物理/攻击查询隐形（同 NPC 宿主的做法）
            var collider = actor.AddChild<BoxCollider>();
            collider.Name = "VehicleBodyCollider";
            collider.Size = BodyColliderSize;
            collider.Center = new Vector3(0f, BodyColliderSize.Y * 0.5f, 0f);

            var vehicleActor = actor.AddScript<TrafficVehicleActor>();
            if (vehicleActor == null)
            {
                MassVehicleLog.LogError($"载具脚本挂载失败：{actorName}");
                FlaxEngine.Object.Destroy(actor);
                return null;
            }
            vehicleActor.ApplySeatConfiguration(request.Vehicle);
            return actor;
        }

        /// <summary>
        /// 确定性 Actor 名。对应 UE5 <c>%s_%ud_%ud_%ud</c>（Template 名 + Guid 分量）。
        /// </summary>
        public static string BuildActorName(VehicleActorSpawnRequest request)
        {
            if (request?.Vehicle == null) return "TrafficVehicle";
            return BuildActorName(request.Vehicle);
        }

        /// <summary>实体 → Actor 名。</summary>
        public static string BuildActorName(TrafficVehicle vehicle)
        {
            string template = string.IsNullOrEmpty(vehicle.TemplateName) ? "TrafficVehicle" : vehicle.TemplateName;
            return $"{template}_{(uint)vehicle.Handle.Index}_{(uint)vehicle.Handle.SerialNumber}";
        }
    }
}

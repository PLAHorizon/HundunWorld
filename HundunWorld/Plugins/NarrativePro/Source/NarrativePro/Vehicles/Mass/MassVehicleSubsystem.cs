using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.Vehicles.Mass
{
    /// <summary>
    /// 车道内的载具集合。对应 UE5 FVehicleLane（MassVehicleSubsystem.h）。
    /// </summary>
    [Serializable]
    public class VehicleLane
    {
        public VehicleLane() { }

        public List<MassEntityHandle> VehiclesInLane = new List<MassEntityHandle>();

        public int IncomingVehicles = 0;
    }

    /// <summary>
    /// Mass 载具子系统（Flax 侧手写宿主）。对应 UE5 UMassVehicleSubsystem（MassVehicleSubsystem.h/.cpp）。
    /// <para>
    /// UE5 里它是 Mass 世界子系统，负责「zonegraph 内载具的通用管理」：
    /// 车道占用表（<c>VehiclesInLanes</c>）、障碍物哈希（<c>VehicleObstacles</c>）、
    /// 车位声明（<c>ClaimLaneSpot</c> / <c>UnclaimLaneSpot</c>）。
    /// Flax 无 Mass，因此本类由 <see cref="NarrativePro.Vehicles.VehicleTrafficSimulation"/> 这个 Script
    /// 持有并逐帧驱动（<see cref="Initialize"/> / <see cref="Shutdown"/> 有真实调用者，不再是无人认领的单例）。
    /// </para>
    /// </summary>
    public class MassVehicleSubsystem
    {
        /// <summary>当前实例。由 <see cref="VehicleTrafficSimulation"/> 创建/销毁。</summary>
        public static MassVehicleSubsystem Instance { get; private set; }

        /// <summary>获取实例。对应 UE5 <c>GetSubsystem&lt;UMassVehicleSubsystem&gt;</c>。</summary>
        public static MassVehicleSubsystem Get() => Instance;

        /// <summary>载具障碍物哈希网格。对应 UE5 VehicleObstacles（FVehicleObstacleHashGrid）。</summary>
        public VehicleObstacleHashGrid VehicleObstacles = new VehicleObstacleHashGrid();

        /// <summary>车道存储。对应 UE5 由 UZoneGraphSubsystem 提供的 FZoneGraphStorage。</summary>
        public ZoneGraphStorage Storage = new ZoneGraphStorage();

        /// <summary>交通灯标签/提前关闭配置。对应 UE5 <c>GetDefault&lt;UTrafficLightSettings&gt;()</c>。</summary>
        public TrafficLightSettings TrafficLightSettings = new TrafficLightSettings();

        /// <summary>车道到车道载具列表的映射。对应 UE5 VehiclesInLanes。</summary>
        protected Dictionary<ZoneGraphLaneHandle, VehicleLane> VehiclesInLanes = new Dictionary<ZoneGraphLaneHandle, VehicleLane>();

        private readonly Dictionary<MassEntityHandle, TrafficVehicle> _vehicles = new Dictionary<MassEntityHandle, TrafficVehicle>();
        private readonly List<TrafficVehicle> _vehicleList = new List<TrafficVehicle>();
        private readonly Dictionary<Actor, MassEntityHandle> _externalObstacles = new Dictionary<Actor, MassEntityHandle>();
        private readonly List<MassEntityHandle> _externalObstacleScratch = new List<MassEntityHandle>();
        private int _nextEntityIndex = 1;
        private int _nextSerial = 1;

        /// <summary>观察者位置（本地玩家/相机），LOD 与自动销毁判定的基准。对应 UE5 Mass LOD 的观察者。</summary>
        public Vector3 ObserverLocation;

        /// <summary>当前全部载具实体（只读视图）。</summary>
        public IReadOnlyList<TrafficVehicle> Vehicles => _vehicleList;

        /// <summary>当前登记的外部障碍物数（行人等非载具对象）。</summary>
        public int ExternalObstacleCount => _externalObstacles.Count;

        /// <summary>分配实体句柄。对应 UE5 <c>FMassEntityManager::CreateEntity</c> 的句柄语义。</summary>
        public MassEntityHandle AllocateHandle()
        {
            var handle = new MassEntityHandle { Index = _nextEntityIndex++, SerialNumber = _nextSerial++ };
            return handle;
        }

        /// <summary>登记实体（由生成器调用）。对应 UE5 实体创建。</summary>
        public TrafficVehicle AddVehicle(TrafficVehicle vehicle)
        {
            if (vehicle == null) return null;
            if (_vehicles.ContainsKey(vehicle.Handle)) return vehicle;
            _vehicles[vehicle.Handle] = vehicle;
            _vehicleList.Add(vehicle);
            return vehicle;
        }

        /// <summary>按句柄取实体。对应 UE5 <c>FMassEntityView::TryMakeView</c>。</summary>
        public bool TryGetVehicle(MassEntityHandle handle, out TrafficVehicle vehicle)
        {
            return _vehicles.TryGetValue(handle, out vehicle);
        }

        /// <summary>是否包含该实体。</summary>
        public bool Contains(MassEntityHandle handle) => _vehicles.ContainsKey(handle);

        /// <summary>
        /// 移除实体并清理它在车道占用表与障碍网格中的残留。
        /// 对应 UE5 实体销毁 + <c>UVehicleObstacleDestructor</c> + 车道离开。
        /// </summary>
        public bool RemoveVehicle(MassEntityHandle handle)
        {
            if (!_vehicles.TryGetValue(handle, out var vehicle)) return false;
            _vehicles.Remove(handle);
            _vehicleList.Remove(vehicle);

            if (vehicle.LaneHandle.IsValid)
            {
                VehicleLeaveLane(vehicle.LaneHandle, handle);
            }
            VehicleObstacles.Remove(handle, vehicle.Obstacle.CellLocation);
            return true;
        }

        /// <summary>
        /// 清空全部状态（切场景/关闭模拟）。
        /// </summary>
        public void Reset()
        {
            _vehicles.Clear();
            _vehicleList.Clear();
            VehiclesInLanes.Clear();
            VehicleObstacles.Clear();
            _externalObstacles.Clear();
        }

        // =====================================================================
        // 车道占用（逐行对应 MassVehicleSubsystem.cpp:13-96）
        // =====================================================================

        /// <summary>载具进入车道。对应 UE5 VehicleEnterLane（cpp:13-18）。</summary>
        public virtual void VehicleEnterLane(ZoneGraphLaneHandle lane, MassEntityHandle vehicle)
        {
            if (!lane.IsValid) return;
            var vehiclesInLane = GetOrAddLane(lane);
            if (!vehiclesInLane.VehiclesInLane.Contains(vehicle))
            {
                vehiclesInLane.VehiclesInLane.Add(vehicle);
            }
        }

        /// <summary>载具离开车道。对应 UE5 VehicleLeaveLane（cpp:20-26）。</summary>
        public virtual void VehicleLeaveLane(ZoneGraphLaneHandle lane, MassEntityHandle vehicle)
        {
            if (!lane.IsValid) return;
            var vehiclesInLane = GetOrAddLane(lane);
            vehiclesInLane.VehiclesInLane.Remove(vehicle);
        }

        /// <summary>获取车道末尾载具（最后一辆进入的）。对应 UE5 GetTailVehicle（cpp:28-43）。</summary>
        public virtual MassEntityHandle GetTailVehicle(ZoneGraphLaneHandle lane)
        {
            if (!VehiclesInLanes.TryGetValue(lane, out var vehicleLane)) return MassEntityHandle.Invalid;
            if (vehicleLane.VehiclesInLane.Count == 0) return MassEntityHandle.Invalid;
            return vehicleLane.VehiclesInLane[vehicleLane.VehiclesInLane.Count - 1];
        }

        /// <summary>
        /// 获取车道可用空间。对应 UE5 GetAvailableSpace（cpp:45-82）：
        /// 尾车里程即「还能放进多少」；无尾车且无声明时放行至少一辆（返回 float.MaxValue），
        /// 否则整条车道会被车道加权过滤掉 —— UE5 原文注释同样指出这对短车道才有风险。
        /// </summary>
        public virtual float GetAvailableSpace(ZoneGraphLaneHandle lane, bool bIncludeIncomingVehicles)
        {
            if (!Storage.GetLaneLength(lane, out float laneLength))
            {
                return 0f;
            }

            VehiclesInLanes.TryGetValue(lane, out var vehicleLane);
            if (vehicleLane == null)
            {
                return laneLength;
            }

            float distanceAlongLane = laneLength;
            var tailVehicle = GetTailVehicle(lane);

            if (tailVehicle.IsValid && TryGetVehicle(tailVehicle, out var tail))
            {
                distanceAlongLane = tail.DistanceAlongLane;
            }
            else if (vehicleLane.IncomingVehicles == 0)
            {
                // UE5 cpp:66-73 的特殊分支：空车道必须至少放进一辆，否则会被加权过滤
                return float.MaxValue;
            }

            float vehicleSpace = bIncludeIncomingVehicles ? vehicleLane.IncomingVehicles * 1000f : 0f;
            return Mathf.Max(0f, distanceAlongLane - vehicleSpace);
        }

        /// <summary>声明车位（准备进入车道，防过满）。对应 UE5 ClaimLaneSpot（cpp:84-89）。</summary>
        public virtual void ClaimLaneSpot(ZoneGraphLaneHandle lane)
        {
            if (!lane.IsValid) return;
            GetOrAddLane(lane).IncomingVehicles++;
        }

        /// <summary>取消声明。对应 UE5 UnclaimLaneSpot（cpp:91-96）。</summary>
        public virtual void UnclaimLaneSpot(ZoneGraphLaneHandle lane)
        {
            if (!lane.IsValid) return;
            var vehicleLane = GetOrAddLane(lane);
            vehicleLane.IncomingVehicles = Mathf.Max(0, vehicleLane.IncomingVehicles - 1);
        }

        /// <summary>车道内载具数（调试/日志）。对应 UE5 VehiclesInLane 视图。</summary>
        public int GetVehicleCountInLane(ZoneGraphLaneHandle lane)
        {
            return VehiclesInLanes.TryGetValue(lane, out var vehicleLane) ? vehicleLane.VehiclesInLane.Count : 0;
        }

        /// <summary>调试输出车道占用。对应 UE5 DebugLane（cpp:98-124，UE5 用 VisualLogger，Flax 无对应→日志）。</summary>
        public virtual void DebugLane(ZoneGraphLaneHandle lane)
        {
            if (!VehiclesInLanes.TryGetValue(lane, out var vehicleLane))
            {
                MassVehicleLog.Log($"DebugLane {lane}: 无占用记录");
                return;
            }
            MassVehicleLog.Log($"DebugLane {lane}: 空间={GetAvailableSpace(lane, true):0.#} " +
                               $"载具={vehicleLane.VehiclesInLane.Count} 声明={vehicleLane.IncomingVehicles}");
        }

        private VehicleLane GetOrAddLane(ZoneGraphLaneHandle lane)
        {
            if (!VehiclesInLanes.TryGetValue(lane, out var vehicleLane))
            {
                vehicleLane = new VehicleLane();
                VehiclesInLanes[lane] = vehicleLane;
            }
            return vehicleLane;
        }

        // =====================================================================
        // 外部障碍物（行人等）登记：让载具对「非载具」对象也减速
        // =====================================================================

        /// <summary>
        /// 登记/更新一个外部障碍物（UE5 里带 <c>UTrafficVehicleObstacleTrait</c> 的实体；
        /// Flax 侧行人是 Actor 而非 Mass 实体，故这里为 Actor 分配合成句柄）。
        /// </summary>
        public MassEntityHandle RegisterExternalObstacle(Actor owner, Vector3 location, float radius)
        {
            if (owner == null) return MassEntityHandle.Invalid;

            if (!_externalObstacles.TryGetValue(owner, out var handle))
            {
                handle = AllocateHandle();
                _externalObstacles[owner] = handle;
                VehicleObstacles.Add(handle, location, radius);
            }
            else
            {
                if (VehicleObstacles.TryGet(handle, out var record))
                {
                    VehicleObstacles.Move(handle, record.CellLocation, location, radius);
                    record.OwnerActor = owner;
                    record.Radius = radius;
                }
                else
                {
                    VehicleObstacles.Add(handle, location, radius);
                }
            }

            if (VehicleObstacles.TryGet(handle, out var added))
            {
                added.OwnerActor = owner;
                added.bIsVehicle = false;
            }
            return handle;
        }

        /// <summary>移除已消失的外部障碍物。返回被清理的数量。</summary>
        public int PruneExternalObstacles(ICollection<Actor> stillPresent)
        {
            _externalObstacleScratch.Clear();
            foreach (var pair in _externalObstacles)
            {
                bool alive = pair.Key != null && (stillPresent == null || stillPresent.Contains(pair.Key));
                if (alive) continue;
                _externalObstacleScratch.Add(pair.Value);
            }

            foreach (var handle in _externalObstacleScratch)
            {
                if (VehicleObstacles.TryGet(handle, out var record))
                {
                    VehicleObstacles.Remove(handle, record.CellLocation);
                }
            }
            if (_externalObstacleScratch.Count > 0)
            {
                var keys = new List<Actor>(_externalObstacles.Count);
                foreach (var pair in _externalObstacles)
                {
                    if (_externalObstacleScratch.Contains(pair.Value)) keys.Add(pair.Key);
                }
                foreach (var key in keys) _externalObstacles.Remove(key);
            }
            return _externalObstacleScratch.Count;
        }

        /// <summary>
        /// 初始化：绑定实例。由 <see cref="NarrativePro.Vehicles.VehicleTrafficSimulation.OnEnable"/> 调用。
        /// </summary>
        public void Initialize()
        {
            Instance = this;
        }

        /// <summary>关闭：解除实例绑定。</summary>
        public void Shutdown()
        {
            if (Instance == this) Instance = null;
            Reset();
        }
    }
}

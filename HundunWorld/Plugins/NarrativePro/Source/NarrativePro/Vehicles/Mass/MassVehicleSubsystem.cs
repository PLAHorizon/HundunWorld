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
    /// Mass 载具子系统。对应 UE5 UMassVehicleSubsystem（MassVehicleSubsystem.h）。
    /// 继承 UMassSubsystemBase。处理 zonegraph 内载具的通用管理。
    /// 简化点：
    /// - Flax 无 Mass Entity System，改为 [Serializable] 单例类占位（Flax 不兼容）
    /// - TMap → Dictionary
    /// - TMassExternalSubsystemTraits 移除（Flax 无对应）
    /// </summary>
    [Serializable]
    public class MassVehicleSubsystem
    {
        /// <summary>单例实例。对应 UE5 子系统的世界级生命周期。</summary>
        public static MassVehicleSubsystem Instance { get; } = new MassVehicleSubsystem();

        /// <summary>获取单例。对应 UE5 GetSubsystem。</summary>
        public static MassVehicleSubsystem Get() => Instance;

        /// <summary>载具障碍物哈希网格。对应 UE5 VehicleObstacles（FVehicleObstacleHashGrid）。</summary>
        public VehicleObstacleHashGrid VehicleObstacles = new VehicleObstacleHashGrid();

        /// <summary>车道到车道载具列表的映射。对应 UE5 VehiclesInLanes。</summary>
        protected Dictionary<ZoneGraphLaneHandle, VehicleLane> VehiclesInLanes = new Dictionary<ZoneGraphLaneHandle, VehicleLane>();

        /// <summary>载具进入车道。对应 UE5 VehicleEnterLane。</summary>
        public virtual void VehicleEnterLane(ZoneGraphLaneHandle lane, MassEntityHandle vehicle)
        {
            // Flax-不兼容: UE5 的 Mass Entity System 在 Flax 无对应物，保留占位。原文 TODO: Flax 无 Mass Entity System，需自定义实现
        }

        /// <summary>载具离开车道。对应 UE5 VehicleLeaveLane。</summary>
        public virtual void VehicleLeaveLane(ZoneGraphLaneHandle lane, MassEntityHandle vehicle)
        {
            // Flax-不兼容: UE5 的 Mass Entity System 在 Flax 无对应物，保留占位。原文 TODO: Flax 无 Mass Entity System，需自定义实现
        }

        /// <summary>获取车道末尾载具。对应 UE5 GetTailVehicle。</summary>
        public virtual MassEntityHandle GetTailVehicle(ZoneGraphLaneHandle lane)
        {
            // Flax-不兼容: UE5 的 Mass Entity System 在 Flax 无对应物，保留占位。原文 TODO: Flax 无 Mass Entity System，需自定义实现
            return MassEntityHandle.Invalid;
        }

        /// <summary>获取车道可用空间。对应 UE5 GetAvailableSpace。</summary>
        public virtual float GetAvailableSpace(ZoneGraphLaneHandle lane, bool bIncludeIncomingVehicles)
        {
            // Flax-不兼容: UE5 的 Mass Entity System 在 Flax 无对应物，保留占位。原文 TODO: Flax 无 Mass Entity System，需自定义实现
            return 0f;
        }

        /// <summary>声明车位。对应 UE5 ClaimLaneSpot。</summary>
        public virtual void ClaimLaneSpot(ZoneGraphLaneHandle lane)
        {
            // Flax-不兼容: UE5 的 Mass Entity System 在 Flax 无对应物，保留占位。原文 TODO: Flax 无 Mass Entity System，需自定义实现
        }

        /// <summary>取消声明车位。对应 UE5 UnclaimLaneSpot。</summary>
        public virtual void UnclaimLaneSpot(ZoneGraphLaneHandle lane)
        {
            // Flax-不兼容: UE5 的 Mass Entity System 在 Flax 无对应物，保留占位。原文 TODO: Flax 无 Mass Entity System，需自定义实现
        }

        /// <summary>调试绘制车道。对应 UE5 DebugLane。</summary>
        public virtual void DebugLane(ZoneGraphLaneHandle lane)
        {
            // Flax-不兼容: UE5 的 Mass Entity System 在 Flax 无对应物，保留占位。原文 TODO: Flax 无 Mass Entity System，需自定义实现
        }
    }
}

using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.Vehicles.TrafficLights;

namespace NarrativePro.Vehicles.Mass
{
    /// <summary>
    /// 交通灯子系统。对应 UE5 UTrafficLightSubsystem（TrafficLightSubsystem.h）。
    /// 继承 UMassTickableSubsystemBase。管理交通灯周期与交叉口侧。
    /// 简化点：
    /// - Flax 无 Mass Entity System / ZoneGraph，改为 [Serializable] 单例类占位（Flax 不兼容）
    /// - FDelegateHandle → object 占位
    /// - TWeakObjectPtr&lt;ATrafficLight&gt; → TrafficLight
    /// </summary>
    [Serializable]
    public class TrafficLightSubsystem
    {
        /// <summary>单例实例。</summary>
        public static TrafficLightSubsystem Instance { get; } = new TrafficLightSubsystem();

        /// <summary>获取单例。</summary>
        public static TrafficLightSubsystem Get() => Instance;

        /// <summary>ZoneGraph 子系统。对应 UE5 ZoneGraphSubsystem。Flax 无对应，占位。</summary>
        [NonSerialized]
        public object ZoneGraphSubsystem;

        /// <summary>ZoneGraph 注解子系统。对应 UE5 ZoneGraphAnnotationSubsystem。Flax 无对应，占位。</summary>
        [NonSerialized]
        public object ZoneGraphAnnotationSubsystem;

        /// <summary>载具子系统。对应 UE5 VehicleSubsystem。</summary>
        [NonSerialized]
        public MassVehicleSubsystem VehicleSubsystem;

        /// <summary>ZoneGraph 数据添加后委托句柄。对应 UE5 OnPostZoneGraphDataAddedHandle。</summary>
        [NonSerialized]
        public object OnPostZoneGraphDataAddedHandle;

        /// <summary>ZoneGraph 数据移除前委托句柄。对应 UE5 OnPreZoneGraphDataRemovedHandle。</summary>
        [NonSerialized]
        public object OnPreZoneGraphDataRemovedHandle;

        /// <summary>已注册的车道数据列表。对应 UE5 RegisteredLaneData。</summary>
        public List<TrafficLightData> RegisteredLaneData = new List<TrafficLightData>();

        /// <summary>交叉口侧哈希网格。对应 UE5 IntersectionSidesGrid（FIntersectionSideHashGrid）。</summary>
        public IntersectionSideHashGrid IntersectionSidesGrid = new IntersectionSideHashGrid();

        /// <summary>已注册的交通灯列表。对应 UE5 RegisteredTrafficLights（TArray&lt;TWeakObjectPtr&lt;ATrafficLight&gt;&gt;）。</summary>
        [NonSerialized]
        public List<TrafficLight> RegisteredTrafficLights = new List<TrafficLight>();

        /// <summary>初始化。对应 UE5 Initialize。</summary>
        public virtual void Initialize()
        {
            // Flax-不兼容: UE5 的 Mass/ZoneGraph 在 Flax 无对应物，保留占位。原文 TODO: Flax 无 Mass/ZoneGraph，需自定义实现
        }

        /// <summary>每帧更新。对应 UE5 Tick。</summary>
        public virtual void Tick(float deltaTime)
        {
            // Flax-不兼容: UE5 的 Mass/ZoneGraph 在 Flax 无对应物，保留占位。原文 TODO: Flax 无 Mass/ZoneGraph，需自定义实现
        }

        /// <summary>ZoneGraph 数据添加后处理。对应 UE5 PostZoneGraphDataAdded。</summary>
        public virtual void PostZoneGraphDataAdded(object zoneGraphData)
        {
            // Flax-不兼容: UE5 的 Mass/ZoneGraph 在 Flax 无对应物，保留占位。原文 TODO: Flax 无 Mass/ZoneGraph，需自定义实现
        }

        /// <summary>ZoneGraph 数据移除前处理。对应 UE5 PreZoneGraphDataRemoved。</summary>
        public virtual void PreZoneGraphDataRemoved(object zoneGraphData)
        {
            // Flax-不兼容: UE5 的 Mass/ZoneGraph 在 Flax 无对应物，保留占位。原文 TODO: Flax 无 Mass/ZoneGraph，需自定义实现
        }

        /// <summary>构建车道数据。对应 UE5 BuildLaneData。</summary>
        public virtual void BuildLaneData(TrafficLightData laneData, ZoneGraphStorage storage)
        {
            // Flax-不兼容: UE5 的 Mass/ZoneGraph 在 Flax 无对应物，保留占位。原文 TODO: Flax 无 Mass/ZoneGraph，需自定义实现
        }

        /// <summary>更新已注册的交通灯。对应 UE5 UpdateRegisteredTrafficLights。</summary>
        public virtual void UpdateRegisteredTrafficLights(TrafficLightIntersection updatedIntersection)
        {
            // Flax-不兼容: UE5 的 Mass/ZoneGraph 在 Flax 无对应物，保留占位。原文 TODO: Flax 无 Mass/ZoneGraph，需自定义实现
        }

        /// <summary>注册交通灯。对应 UE5 RegisterTrafficLight。</summary>
        public virtual void RegisterTrafficLight(TrafficLight trafficLight)
        {
            if (trafficLight == null) return;
            if (!RegisteredTrafficLights.Contains(trafficLight))
            {
                RegisteredTrafficLights.Add(trafficLight);
            }
        }
    }
}

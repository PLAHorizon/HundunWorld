using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.AI;
using NarrativePro.Core;

namespace NarrativePro.Vehicles.Mass
{
    // =========================================================================
    // 共享占位类型：Flax 无 Mass Entity System / ZoneGraph 对应，以下为占位类型。
    // 仅用于保留 UE5 类型层级与字段签名，便于后续按需替换实现。
    // =========================================================================

    /// <summary>Mass 实体句柄占位。对应 UE5 FMassEntityHandle。</summary>
    [Serializable]
    public struct MassEntityHandle
    {
        public int Index;
        public int SerialNumber;
        public bool IsValid => Index != 0 || SerialNumber != 0;
        public static readonly MassEntityHandle Invalid = default;
    }

    /// <summary>ZoneGraph 车道句柄占位。对应 UE5 FZoneGraphLaneHandle。</summary>
    [Serializable]
    public struct ZoneGraphLaneHandle
    {
        public int Index;
        public bool IsValid => Index >= 0;
        public static readonly ZoneGraphLaneHandle Invalid = default;
    }

    /// <summary>ZoneGraph 数据句柄占位。对应 UE5 FZoneGraphDataHandle。</summary>
    [Serializable]
    public struct ZoneGraphDataHandle
    {
        public int Index;
        public static readonly ZoneGraphDataHandle Invalid = default;
    }

    /// <summary>ZoneGraph 标签占位。对应 UE5 FZoneGraphTag。</summary>
    [Serializable]
    public struct ZoneGraphTag
    {
        public string Name;
    }

    /// <summary>ZoneGraph 标签掩码占位。对应 UE5 FZoneGraphTagMask。</summary>
    [Serializable]
    public struct ZoneGraphTagMask
    {
        public uint Mask;
        public static readonly ZoneGraphTagMask None = default;
    }

    /// <summary>ZoneGraph 标签过滤器占位。对应 UE5 FZoneGraphTagFilter。</summary>
    [Serializable]
    public class ZoneGraphTagFilter
    {
        public ZoneGraphTagMask AnyTags = ZoneGraphTagMask.None;
        public ZoneGraphTagMask AllTags = ZoneGraphTagMask.None;
        public ZoneGraphTagMask NotTags = ZoneGraphTagMask.None;
    }

    /// <summary>载具障碍物哈希网格占位。对应 UE5 FVehicleObstacleHashGrid（THierarchicalHashGrid2D）。</summary>
    [Serializable]
    public class VehicleObstacleHashGrid
    {
        /// <summary>网格单元位置占位。对应 UE5 FVehicleObstacleHashGrid::FCellLocation。</summary>
        [Serializable]
        public struct FCellLocation
        {
            public int X;
            public int Y;
            public int Layer;
        }
    }

    /// <summary>交叉口侧哈希网格占位。对应 UE5 FIntersectionSideHashGrid。</summary>
    [Serializable]
    public class IntersectionSideHashGrid
    {
        // Flax-不兼容: UE5 的 Mass/ZoneGraph 对应 在 Flax 无对应物，保留占位。原文 TODO: Flax 无 Mass/ZoneGraph 对应，需自定义实现
    }

    /// <summary>ZoneGraph 存储占位。对应 UE5 FZoneGraphStorage。</summary>
    [Serializable]
    public class ZoneGraphStorage
    {
        public List<object> Lanes = new List<object>();
        public List<Vector3> LanePoints = new List<Vector3>();
    }

    /// <summary>Mass 生成实体类型占位。对应 UE5 FMassSpawnedEntityType。</summary>
    [Serializable]
    public class MassSpawnedEntityType
    {
        public int Amount;
    }

    // =========================================================================
    // 片段基类占位：对应 UE5 FMassFragment/FMassConstSharedFragment/FMassTag/FObjectWrapperFragment
    // =========================================================================

    /// <summary>Mass 片段基类占位。对应 UE5 FMassFragment。</summary>
    [Serializable]
    public abstract class MassFragment { }

    /// <summary>Mass 共享片段基类占位。对应 UE5 FMassConstSharedFragment。</summary>
    [Serializable]
    public abstract class MassConstSharedFragment { }

    /// <summary>Mass 标签基类占位。对应 UE5 FMassTag。</summary>
    [Serializable]
    public abstract class MassTag : MassFragment { }

    /// <summary>对象包装片段基类占位。对应 UE5 FObjectWrapperFragment。</summary>
    [Serializable]
    public abstract class ObjectWrapperFragment : MassFragment { }

    /// <summary>Agent 半径片段占位。对应 UE5 FAgentRadiusFragment。</summary>
    [Serializable]
    public class AgentRadiusFragment : MassFragment
    {
        public float Radius = 0f;
    }

    // =========================================================================
    // VehicleFragments.h 中的结构
    // =========================================================================

    /// <summary>
    /// 载具移动片段。对应 UE5 FVehicleLocomotionFragment（VehicleFragments.h）。
    /// 继承 FMassFragment。Flax 无 Mass，改为 [Serializable] class 占位。
    /// </summary>
    [Serializable]
    public class VehicleLocomotionFragment : MassFragment
    {
        public float DesiredSpeed = 0f;

        /// <summary>高 LOD PID 控制器。</summary>
        public VehiclePIDController ThrottlePIDController = new VehiclePIDController();
        public VehiclePIDController SteeringPIDController = new VehiclePIDController();

        /// <summary>油门，范围 -1 到 1，&lt; 0 为刹车。</summary>
        public float Throttle = 0f;

        public float Steering = 0f;

        public float DistanceToNextVehicle = float.MaxValue;

        public MassEntityHandle NextVehicleHandle = MassEntityHandle.Invalid;

        public ZoneGraphLaneHandle NextLane = ZoneGraphLaneHandle.Invalid;

        public float Color = 0f;
    }

    /// <summary>
    /// PID 设置。对应 UE5 FPIDSettings（VehicleFragments.h）。
    /// </summary>
    [Serializable]
    public class PIDSettings
    {
        public float ProportionalGain = 0f;
        public float IntegralGain = 0f;
        public float DerivativeGain = 0f;
    }

    /// <summary>
    /// 载具 PID 控制器片段。对应 UE5 FVehiclePIDControllerFragment（VehicleFragments.h）。
    /// 继承 FMassConstSharedFragment。
    /// </summary>
    [Serializable]
    public class VehiclePIDControllerFragment : MassConstSharedFragment
    {
        public PIDSettings SteeringSettings = new PIDSettings();
        public PIDSettings ThrottleSettings = new PIDSettings();

        /// <summary>当前车道前方计算转向的距离。</summary>
        public float LookAheadDistance = 500f;
    }

    /// <summary>
    /// 载具设置片段。对应 UE5 FVehicleSettingsFragment（VehicleFragments.h）。
    /// 继承 FMassConstSharedFragment。
    /// </summary>
    [Serializable]
    public class VehicleSettingsFragment : MassConstSharedFragment
    {
        /// <summary>希望与障碍物保持的最小距离。</summary>
        public float MinimumDistanceToObstacle = 300f;

        /// <summary>有障碍物接近时开始刹车的距离。</summary>
        public float BrakingDistanceFromObstacle = 500f;

        /// <summary>障碍物接近时刹车力度。</summary>
        public float ObstacleAvoidanceBrakingPower = 2f;

        /// <summary>搜索周围障碍物的半径。</summary>
        public float ObstacleSearchRadius = 1500f;

        /// <summary>与关闭车道的最小距离。</summary>
        public float ClosedLaneMinDistance = 100f;

        /// <summary>关闭车道接近时开始刹车的距离。</summary>
        public float ClosedLaneBrakingDistance = 300f;

        /// <summary>关闭车道接近时刹车力度。</summary>
        public float ClosedLaneBrakingPower = 2f;

        /// <summary>与下一辆车保持的最小距离。</summary>
        public float MinimumDistanceToNext = 300f;

        /// <summary>下一辆车接近时开始刹车的距离。</summary>
        public float BrakingDistanceFromNext = 500f;

        /// <summary>下一辆车接近时刹车力度。</summary>
        public float NextVehicleAvoidanceBrakingPower = 2f;

        /// <summary>载具期望的最大速度。</summary>
        public float VehicleMaxSpeed = 400f;

        /// <summary>搜索附近 zonegraph 车道的范围。</summary>
        public float LaneSearchRadius = 500f;

        /// <summary>在 zonegraph 中选择车道时使用的过滤器。</summary>
        public ZoneGraphTagFilter VehicleLaneFilter = new ZoneGraphTagFilter();
    }

    /// <summary>
    /// 载具障碍物片段。对应 UE5 FVehicleObstacleFragment（VehicleFragments.h）。
    /// 继承 FMassFragment。
    /// </summary>
    [Serializable]
    public class VehicleObstacleFragment : MassFragment
    {
        /// <summary>此障碍物存储的位置。</summary>
        public VehicleObstacleHashGrid.FCellLocation CellLocation;
    }

    /// <summary>
    /// 载具组件包装片段。对应 UE5 FVehicleComponentWrapperFragment（VehicleFragments.h）。
    /// 继承 FObjectWrapperFragment。包装 UChaosWheeledVehicleMovementComponent。
    /// </summary>
    [Serializable]
    public class VehicleComponentWrapperFragment : ObjectWrapperFragment
    {
        /// <summary>包装的移动组件。对应 UE5 TWeakObjectPtr&lt;UChaosWheeledVehicleMovementComponent&gt;。
        /// Flax 无 Chaos Vehicle System，用 object 占位。</summary>
        [NonSerialized]
        public object Component;
    }

    /// <summary>
    /// Mass 载具移动到 Actor 标签。对应 UE5 FMassVehicleMovementToActorTag（VehicleFragments.h）。
    /// 继承 FMassTag。
    /// </summary>
    [Serializable]
    public class MassVehicleMovementToActorTag : MassTag
    {
    }

    /// <summary>
    /// 种子片段。对应 UE5 FSeedFragment（VehicleFragments.h）。
    /// 继承 FMassFragment。存储确定性随机值的种子。
    /// </summary>
    [Serializable]
    public class SeedFragment : MassFragment
    {
        public SeedFragment() { }

        public SeedFragment(int inSeed)
        {
            Seed = inSeed;
        }

        public int Seed = 0;

        /// <summary>随机流。对应 UE5 FRandomStream。Flax 简化为 System.Random。</summary>
        [NonSerialized]
        public System.Random Stream;
    }

    /// <summary>
    /// 乘客加权概率。对应 UE5 FPassengerWeightedProbability（VehicleFragments.h）。
    /// 存储加权概率与乘客数量。
    /// </summary>
    [Serializable]
    public class PassengerWeightedProbability
    {
        public PassengerWeightedProbability() { }

        public PassengerWeightedProbability(float inWeightedProb, int inNumPassengers)
        {
            WeightedProbability = inWeightedProb;
            NumPassengers = inNumPassengers;
        }

        public float WeightedProbability = 1f;
        public int NumPassengers = 1;
    }

    /// <summary>
    /// 乘客设置片段。对应 UE5 FPassengerSettingsFragment（VehicleFragments.h）。
    /// 继承 FMassConstSharedFragment。定义可进入载具的乘客。
    /// </summary>
    [Serializable]
    public class PassengerSettingsFragment : MassConstSharedFragment
    {
        public PassengerSettingsFragment() { }

        /// <summary>可进入载具的乘客定义。对应 UE5 TArray&lt;UNPCDefinition*&gt;。</summary>
        public List<NPCDefinition> PassengerDefinitions = new List<NPCDefinition>();

        /// <summary>获得 x 名乘客的加权概率。</summary>
        public List<PassengerWeightedProbability> PassengerCountProbability =
            new List<PassengerWeightedProbability> { new PassengerWeightedProbability(1f, 1) };

        public bool IsValid()
        {
            return PassengerDefinitions != null && PassengerDefinitions.Count > 0
                && PassengerCountProbability != null && PassengerCountProbability.Count > 0;
        }
    }

    /// <summary>
    /// 载具乘客片段。对应 UE5 FVehiclePassengersFragment（VehicleFragments.h）。
    /// 继承 FMassFragment。存储载具内乘客的实体特定数据。
    /// </summary>
    [Serializable]
    public class VehiclePassengersFragment : MassFragment
    {
        public VehiclePassengersFragment() { }

        /// <summary>键为座位索引，值为乘客定义。对应 UE5 TArray&lt;TPair&lt;int, UNPCDefinition*&gt;&gt;。</summary>
        public List<KeyValuePair<int, NPCDefinition>> Passengers = new List<KeyValuePair<int, NPCDefinition>>();
    }

    /// <summary>
    /// 载具乘客加权采样器。对应 UE5 FVehiclePassengerWeightedSampler（VehicleFragments.h）。
    /// 继承 FWeightedRandomSampler。用于获得载具内乘客数量的加权概率。
    /// </summary>
    [Serializable]
    public class VehiclePassengerWeightedSampler
    {
        public List<PassengerWeightedProbability> PassengerCountProbability;

        public VehiclePassengerWeightedSampler(List<PassengerWeightedProbability> probability)
        {
            PassengerCountProbability = probability ?? new List<PassengerWeightedProbability>();
        }

        /// <summary>获取权重列表。对应 UE5 GetWeights。</summary>
        public virtual float GetWeights(List<float> outWeights)
        {
            // Flax-不兼容: UE5 的 Mass 在 Flax 无对应物，保留占位。原文 TODO: Flax 无 Mass，需自定义实现
            float total = 0f;
            if (outWeights == null) return 0f;
            outWeights.Clear();
            foreach (var p in PassengerCountProbability)
            {
                outWeights.Add(p.WeightedProbability);
                total += p.WeightedProbability;
            }
            return total;
        }
    }

    /// <summary>
    /// 自动销毁标签。对应 UE5 FAutoDestroyTag（VehicleFragments.h）。
    /// 继承 FMassTag。当实体处于 Low LOD 且带此标签时自动销毁。
    /// </summary>
    [Serializable]
    public class AutoDestroyTag : MassTag
    {
    }
}

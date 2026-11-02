using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.Vehicles.Mass
{
    /// <summary>
    /// 载具表现档位。对应 UE5 <c>EMassRepresentationType</c> + <c>FMassRepresentationFragment</c> 的组合语义：
    /// <see cref="High"/> 用真实 Actor（<see cref="TrafficVehicleActor"/>）承载，
    /// <see cref="Low"/> 隐藏 Actor 只跑车道运动学，<see cref="Off"/> 超出模拟半径。
    /// </summary>
    public enum EVehicleRepresentationLOD
    {
        Off = 0,
        Low = 1,
        High = 2,
    }

    /// <summary>
    /// 一辆交通载具的运行期实体。
    /// <para>
    /// UE5 侧它是 Mass 实体，片段由 <c>UVehicleTrait::BuildTemplate</c> 决定
    /// （FTransformFragment / FMassVelocityFragment / FMassZoneGraphLaneLocationFragment /
    /// FVehicleLocomotionFragment / FAgentRadiusFragment / FSeedFragment /
    /// FVehicleObstacleFragment / FVehiclePassengersFragment / FVehicleComponentWrapperFragment）。
    /// Flax 无 Mass 实体管理器，这里把同一组片段显式装进一个对象，
    /// 由 <see cref="VehicleMovementProcessor"/> / <see cref="VehicleObstacleProcessor"/> 等逐帧处理，
    /// 处理器读写的字段与 UE5 逐一对应。
    /// </para>
    /// </summary>
    public class TrafficVehicle
    {
        /// <summary>实体句柄。对应 UE5 FMassEntityHandle。</summary>
        public MassEntityHandle Handle;

        /// <summary>模板名（日志/Actor 命名用）。对应 UE5 实体模板。</summary>
        public string TemplateName = "TrafficVehicle";

        /// <summary>
        /// 本实体的特质（共享片段载体）。对应 UE5 的 FMassConstSharedFragment 组
        /// （FVehicleSettingsFragment / FVehiclePIDControllerFragment / FPassengerSettingsFragment）——
        /// 处理器按实体读取它，因此不同模板的车流可以有不同速度/刹车参数。
        /// </summary>
        public VehicleTrait Trait;

        // ===== 片段（与 UE5 同名，便于对照） =====

        /// <summary>移动片段（期望速度/PID/前车/下一车道）。对应 UE5 FVehicleLocomotionFragment。</summary>
        public VehicleLocomotionFragment Locomotion = new VehicleLocomotionFragment();

        /// <summary>半径片段。对应 UE5 FAgentRadiusFragment。</summary>
        public AgentRadiusFragment Radius = new AgentRadiusFragment { Radius = 60f };

        /// <summary>种子片段。对应 UE5 FSeedFragment。</summary>
        public SeedFragment Seed = new SeedFragment();

        /// <summary>障碍片段（哈希网格单元）。对应 UE5 FVehicleObstacleFragment。</summary>
        public VehicleObstacleFragment Obstacle = new VehicleObstacleFragment();

        /// <summary>乘客片段。对应 UE5 FVehiclePassengersFragment。</summary>
        public VehiclePassengersFragment Passengers = new VehiclePassengersFragment();

        /// <summary>组件包装片段。对应 UE5 FVehicleComponentWrapperFragment（Chaos → Flax Actor）。</summary>
        public VehicleComponentWrapperFragment ComponentWrapper = new VehicleComponentWrapperFragment();

        /// <summary>是否带自动销毁标签。对应 UE5 FAutoDestroyTag。</summary>
        public bool bAutoDestroyTag;

        // ===== 变换 / 车道位置（UE5 的三个 Fragment 在此显式化） =====

        /// <summary>当前世界位置。对应 UE5 FTransformFragment。</summary>
        public Vector3 Location;

        /// <summary>当前朝向。对应 UE5 FTransformFragment 的旋转部分。</summary>
        public Quaternion Rotation = Quaternion.Identity;

        /// <summary>当前速度向量。对应 UE5 FMassVelocityFragment。</summary>
        public Vector3 Velocity;

        /// <summary>当前车道。对应 UE5 FMassZoneGraphLaneLocationFragment::LaneHandle。</summary>
        public ZoneGraphLaneHandle LaneHandle = ZoneGraphLaneHandle.Invalid;

        /// <summary>车道内里程。对应 UE5 FMassZoneGraphLaneLocationFragment::DistanceAlongLane。</summary>
        public float DistanceAlongLane;

        /// <summary>当前车道长度。对应 UE5 FMassZoneGraphLaneLocationFragment::LaneLength。</summary>
        public float LaneLength;

        /// <summary>沿车道的标量速度（世界单位/秒）。UE5 由 DesiredSpeed 直接推进，此处保留实际速度供 PID 收敛。</summary>
        public float Speed;

        /// <summary>当前表现档位。对应 UE5 FMassRepresentationFragment::CurrentRepresentation。</summary>
        public EVehicleRepresentationLOD LOD = EVehicleRepresentationLOD.Low;

        /// <summary>
        /// 运动学移动模型（UE5 侧即 Chaos 轮式移动组件的位置）。
        /// 对应 UE5 <c>UChaosWheeledVehicleMovementComponent</c> + <c>FMassVelocityFragment</c>：
        /// 速度的唯一积分点，高/低 LOD 共用同一实例，Actor 侧通过
        /// <see cref="NarrativeWheeledVehicle.GetVehicleMovementComponent"/> 读同一个对象。
        /// </summary>
        public KinematicVehicleMovement Movement = new KinematicVehicleMovement();

        /// <summary>表现 Actor（高 LOD 时非空）。对应 UE5 Mass 生成的 Actor。</summary>
        public Actor Actor;

        /// <summary>本实体存活时间（秒），用于最长存活兜底销毁。</summary>
        public float AliveTime;

        /// <summary>「找不到附近车道」告警是否已报过（缺陷③：每辆车只报一次，不再每 5 秒刷屏）。</summary>
        public bool bLaneLossWarned;

        /// <summary>是否被销毁流程请求（下一帧由 <see cref="AutoDestroyProcessor"/> 结算）。</summary>
        public bool bDestroyRequested;

        /// <summary>
        /// 把车道上的投影位置同步回实体（高 LOD 时由 Actor 反推，对应 UE5
        /// <c>MoveVehicleQuery</c> 里 HighResSpawnedActor 分支的 FindNearestLocationOnLane）。
        /// </summary>
        public void SyncFromActor()
        {
            if (Actor == null) return;
            Location = Actor.Position;
            Rotation = Actor.Orientation;
        }

        /// <summary>把实体变换写回 Actor（低/中档 Actor 隐藏时也不写，避免无谓跨界调用）。</summary>
        public void ApplyToActor()
        {
            if (Actor == null || LOD != EVehicleRepresentationLOD.High) return;
            Actor.Position = Location;
            Actor.Orientation = Rotation;
        }

        /// <summary>到某点的平面距离（LOD/销毁判定用）。</summary>
        public float DistanceTo(Vector3 point)
        {
            return Vector3.Distance(Location, point);
        }
    }
}

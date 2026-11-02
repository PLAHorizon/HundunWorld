using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.Vehicles.Mass
{
    /// <summary>
    /// 载具初始化处理器。对应 UE5 UInitVehicleProcessor（InitVehicleProcessor.h/.cpp:1-165）。
    /// <para>
    /// UE5 在实体进入 SetLaneQuery 时把它落到最近的 zonegraph 车道上：
    /// 解析车道、写 <c>FMassZoneGraphLaneLocationFragment</c>、把实体登记进
    /// <c>UMassVehicleSubsystem</c> 的车道占用表，并按 <c>FPassengerSettingsFragment</c> 抽样乘客。
    /// Flax 侧同一流程由本类实现，<see cref="VehicleSpawnerSubsystem"/> 在每次生成后调用
    /// <see cref="InitializeVehicle"/>；种子部分委派给 <see cref="InitSeedProcessor"/>，避免两处真相。
    /// </para>
    /// </summary>
    [Serializable]
    public class InitVehicleProcessor
    {
        /// <summary>载具子系统（车道占用表 + 存储）。</summary>
        public MassVehicleSubsystem VehicleSubsystem;

        /// <summary>种子初始化器（UE5 里是两个独立观察者，这里保持拆分为同一流程的两步）。</summary>
        public InitSeedProcessor SeedProcessor = new InitSeedProcessor();

        /// <summary>障碍特质（进网格用）。</summary>
        public TrafficVehicleObstacleTrait ObstacleTrait = new TrafficVehicleObstacleTrait();

        /// <summary>已成功落到车道的实体数（诊断自证：为 0 说明生成点没落在车道附近）。</summary>
        public int InitializedOnLaneCount { get; private set; }

        /// <summary>初始化失败（找不到附近车道）的次数。</summary>
        public int FailedLaneCount { get; private set; }

        public InitVehicleProcessor(MassVehicleSubsystem vehicleSubsystem)
        {
            VehicleSubsystem = vehicleSubsystem;
        }

        /// <summary>
        /// 初始化一个刚生成的载具。对应 UE5 UInitVehicleProcessor::Execute。
        /// </summary>
        /// <param name="vehicle">实体。</param>
        /// <param name="preferredLane">指定车道；无效时在 <see cref="VehicleSettingsFragment.LaneSearchRadius"/> 内找最近车道。</param>
        /// <param name="preferredDistance">指定车道内里程（仅当 preferredLane 有效时生效）。</param>
        /// <param name="requestedSeed">请求种子，&lt;=0 随机。</param>
        /// <returns>是否成功落到车道上。</returns>
        public bool InitializeVehicle(TrafficVehicle vehicle, ZoneGraphLaneHandle preferredLane,
                                       float preferredDistance, int requestedSeed)
        {
            if (vehicle == null || VehicleSubsystem == null) return false;

            var storage = VehicleSubsystem.Storage;

            // ① 种子（UE5 的 FSeedFragment 初始化观察者）
            SeedProcessor.InitializeSeed(vehicle, requestedSeed);

            // ② 障碍特质生效：半径与进网格声明
            ObstacleTrait.ApplyTo(vehicle);

            // ③ 落到车道。对应 UE5 cpp 的 lane 解析 + FMassZoneGraphLaneLocationFragment 写入
            if (!preferredLane.IsValid || storage.GetLane(preferredLane) == null)
            {
                if (!storage.FindClosestLane(vehicle.Location,
                        Trait().VehicleSettingsFragment.LaneSearchRadius, out var closest))
                {
                    FailedLaneCount++;
                    return false;
                }
                preferredLane = closest.LaneHandle;
                preferredDistance = closest.DistanceAlongLane;
                vehicle.Location = closest.Position;
                vehicle.Rotation = VehicleTrait.RotationFromTangent(closest.Tangent);
            }

            if (!storage.GetLaneLength(preferredLane, out float laneLength))
            {
                FailedLaneCount++;
                return false;
            }

            vehicle.LaneHandle = preferredLane;
            vehicle.LaneLength = laneLength;
            vehicle.DistanceAlongLane = Mathf.Clamp(preferredDistance, 0f, laneLength);

            if (storage.CalculateLocationAlongLane(preferredLane, vehicle.DistanceAlongLane, out var laneLocation))
            {
                vehicle.Location = laneLocation.Position;
                vehicle.Rotation = VehicleTrait.RotationFromTangent(laneLocation.Tangent);
            }

            // ④ 进入车道占用表。对应 UE5 VehicleEnterLane
            VehicleSubsystem.VehicleEnterLane(vehicle.LaneHandle, vehicle.Handle);

            // ⑤ 起步速度：UE5 由 MaxSpeed 起步（避免首帧被前车判定刹停），这里同样给一个初值
            vehicle.Speed = Trait().VehicleSettingsFragment.VehicleMaxSpeed * 0.5f;
            vehicle.Locomotion.DesiredSpeed = Trait().VehicleSettingsFragment.VehicleMaxSpeed;

            InitializedOnLaneCount++;
            return true;
        }

        private VehicleTrait _fallbackTrait;

        private VehicleTrait Trait()
        {
            var trait = VehicleTrafficSimulation.Instance != null ? VehicleTrafficSimulation.Instance.Trait : null;
            if (trait != null) return trait;
            _fallbackTrait ??= new VehicleTrait();
            return _fallbackTrait;
        }
    }
}

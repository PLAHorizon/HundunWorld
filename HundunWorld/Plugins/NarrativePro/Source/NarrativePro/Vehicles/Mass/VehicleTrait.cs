using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.AI;
using NarrativePro.Core;

namespace NarrativePro.Vehicles.Mass
{
    /// <summary>
    /// 载具特质。对应 UE5 UVehicleTrait（VehicleTrait.h/.cpp:BuildTemplate）。
    /// <para>
    /// UE5 的 BuildTemplate 往实体模板里加片段：FAgentRadiusFragment、FVehiclePIDControllerFragment、
    /// FVehicleSettingsFragment、FSeedFragment（0 则随机）、FPassengerSettingsFragment、
    /// FVehiclePassengersFragment，再加移动/车道位置/速度片段与 FVehicleMovementToActorTag。
    /// Flax 无 Mass 模板构建上下文，这里等价实现为「按同一份片段集合装配一个
    /// <see cref="TrafficVehicle"/> 实体」，由 <see cref="VehicleSpawnerSubsystem"/> /
    /// <see cref="RoadControlsSpawnPointsGenerator"/> 在生成时调用。
    /// </para>
    /// </summary>
    [Serializable]
    public class VehicleTrait
    {
        /// <summary>模板名。对应 UE5 实体模板 Asset 名。</summary>
        public string TemplateName = "TrafficVehicle";

        /// <summary>Agent 半径片段。对应 UE5 Radius（FAgentRadiusFragment）。</summary>
        public AgentRadiusFragment Radius = new AgentRadiusFragment { Radius = 60f };

        /// <summary>PID 控制器设置。对应 UE5 PIDControllerSettings（FVehiclePIDControllerFragment）。</summary>
        public VehiclePIDControllerFragment PIDControllerSettings = new VehiclePIDControllerFragment();

        /// <summary>载具设置片段。对应 UE5 VehicleSettingsFragment。</summary>
        public VehicleSettingsFragment VehicleSettingsFragment = new VehicleSettingsFragment();

        /// <summary>可选种子片段，为 0 时为每个实体随机选择。对应 UE5 SeedFragment。</summary>
        public SeedFragment SeedFragment = new SeedFragment();

        /// <summary>载具内的乘客定义。对应 UE5 Passengers（FPassengerSettingsFragment）。</summary>
        public PassengerSettingsFragment Passengers = new PassengerSettingsFragment();

        /// <summary>座位数（供 <see cref="TrafficVehicleActor"/> 建交互槽位）。对应 UE5 载具蓝图上的槽位数。</summary>
        public int SeatCount = 1;

        /// <summary>油门 PID 输出的加速度上限（世界单位/秒²）。Flax 无 Chaos，油门直接作用于标量速度。</summary>
        public float ThrottleAuthority = 600f;

        /// <summary>转向角速度上限（度/秒）。对应 UE5 Steering PID 驱动车轮转角的等效限制。</summary>
        public float MaxSteerRateDegrees = 140f;

        /// <summary>生成时的初始档位。UE5 由 Mass LOD 决定，这里先给低档，第一帧由表示子系统升档。</summary>
        public EVehicleRepresentationLOD InitialLOD = EVehicleRepresentationLOD.Low;

        public VehicleTrait()
        {
            ApplyPIDDefaults();
        }

        /// <summary>
        /// 未配置时给一组可用的油门/转向 PID 默认值。
        /// UE5 侧这些增益配在类默认对象上（此处资产不可得），全 0 会让载具永远不动，
        /// 因此按「临界阻尼跟随」量级取默认，配置可覆盖。
        /// </summary>
        public void ApplyPIDDefaults()
        {
            PIDControllerSettings ??= new VehiclePIDControllerFragment();
            PIDControllerSettings.ThrottleSettings ??= new PIDSettings();
            PIDControllerSettings.SteeringSettings ??= new PIDSettings();

            if (PIDControllerSettings.ThrottleSettings.ProportionalGain <= 0f
                && PIDControllerSettings.ThrottleSettings.IntegralGain <= 0f
                && PIDControllerSettings.ThrottleSettings.DerivativeGain <= 0f)
            {
                PIDControllerSettings.ThrottleSettings.ProportionalGain = 1.2f;
                PIDControllerSettings.ThrottleSettings.IntegralGain = 0.02f;
                PIDControllerSettings.ThrottleSettings.DerivativeGain = 0.25f;
            }

            if (PIDControllerSettings.SteeringSettings.ProportionalGain <= 0f
                && PIDControllerSettings.SteeringSettings.IntegralGain <= 0f
                && PIDControllerSettings.SteeringSettings.DerivativeGain <= 0f)
            {
                PIDControllerSettings.SteeringSettings.ProportionalGain = 4f;
                PIDControllerSettings.SteeringSettings.IntegralGain = 0f;
                PIDControllerSettings.SteeringSettings.DerivativeGain = 0.5f;
            }

            if (PIDControllerSettings.LookAheadDistance <= 0f)
            {
                PIDControllerSettings.LookAheadDistance = 500f;
            }
        }

        /// <summary>
        /// 装配一个载具实体。对应 UE5 <c>UVehicleTrait::BuildTemplate</c> 的片段集合。
        /// </summary>
        /// <param name="subsystem">载具子系统（提供句柄、存储与车道占用表）。</param>
        /// <param name="spawnLocation">生成位置。</param>
        /// <param name="lane">入口车道。无效时由调用方先用 <see cref="ZoneGraphStorage.FindClosestLane"/> 解析。</param>
        /// <param name="distanceAlongLane">入口里程。</param>
        /// <param name="optionalSeed">种子，&lt;=0 表示随机（对应 UE5 Seed==0 时随机）。</param>
        public TrafficVehicle BuildVehicle(MassVehicleSubsystem subsystem, Vector3 spawnLocation,
                                           ZoneGraphLaneHandle lane, float distanceAlongLane, int optionalSeed = -1)
        {
            if (subsystem == null) return null;
            ApplyPIDDefaults();

            var vehicle = new TrafficVehicle
            {
                Handle = subsystem.AllocateHandle(),
                TemplateName = TemplateName,
                Trait = this,
                Radius = new AgentRadiusFragment { Radius = Mathf.Max(1f, Radius.Radius) },
                Locomotion = new VehicleLocomotionFragment
                {
                    ThrottlePIDController = new VehiclePIDController(),
                    SteeringPIDController = new VehiclePIDController(),
                    DistanceToNextVehicle = float.MaxValue,
                    NextVehicleHandle = MassEntityHandle.Invalid,
                    NextLane = ZoneGraphLaneHandle.Invalid,
                },
                Passengers = new VehiclePassengersFragment(),
                Obstacle = new VehicleObstacleFragment(),
                ComponentWrapper = new VehicleComponentWrapperFragment(),
                LOD = InitialLOD,
                Location = spawnLocation,
                Rotation = Quaternion.Identity,
            };

            // 种子：对应 UE5 FSeedFragment（0 → 随机）
            int seed = optionalSeed > 0 ? optionalSeed : (SeedFragment.Seed > 0 ? SeedFragment.Seed : new Random().Next(1, int.MaxValue));
            vehicle.Seed = new SeedFragment(seed);
            vehicle.Seed.Stream = new Random(seed);

            // 乘客数量按加权概率抽样。对应 UE5 FVehiclePassengerWeightedSampler + Passengers 定义
            if (Passengers != null && Passengers.IsValid())
            {
                int count = RollPassengerCount(vehicle.Seed.Stream);
                for (int i = 0; i < count; i++)
                {
                    var definition = Passengers.PassengerDefinitions[i % Passengers.PassengerDefinitions.Count];
                    if (definition == null) continue;
                    vehicle.Passengers.Passengers.Add(new KeyValuePair<int, NPCDefinition>(i, definition));
                }
            }

            // 车道位置片段（UE5 FMassZoneGraphLaneLocationFragment）
            if (lane.IsValid && subsystem.Storage.GetLaneLength(lane, out float laneLength))
            {
                vehicle.LaneHandle = lane;
                vehicle.LaneLength = laneLength;
                vehicle.DistanceAlongLane = Mathf.Clamp(distanceAlongLane, 0f, laneLength);

                if (subsystem.Storage.CalculateLocationAlongLane(lane, vehicle.DistanceAlongLane, out var laneLocation))
                {
                    vehicle.Location = laneLocation.Position;
                    vehicle.Rotation = RotationFromTangent(laneLocation.Tangent);
                    vehicle.Velocity = laneLocation.Tangent * vehicle.Speed;
                }

                // 进入车道，对应 UE5 VehicleEnterLane（初始化观察者 UInitVehicleProcessor 的等价动作）
                subsystem.VehicleEnterLane(lane, vehicle.Handle);
            }

            subsystem.AddVehicle(vehicle);
            return vehicle;
        }

        /// <summary>按加权概率抽乘客数量。对应 UE5 FVehiclePassengerWeightedSampler 的采样。</summary>
        public int RollPassengerCount(Random random)
        {
            if (Passengers?.PassengerCountProbability == null || Passengers.PassengerCountProbability.Count == 0) return 1;

            var weights = new List<float>();
            var sampler = new VehiclePassengerWeightedSampler(Passengers.PassengerCountProbability);
            float total = sampler.GetWeights(weights);
            if (total <= 0f) return Passengers.PassengerCountProbability[0].NumPassengers;

            float roll = (float)(random != null ? random.NextDouble() : new Random().NextDouble()) * total;
            float accumulated = 0f;
            for (int i = 0; i < Passengers.PassengerCountProbability.Count; i++)
            {
                float weight = i < weights.Count ? weights[i] : 0f;
                accumulated += weight;
                if (roll <= accumulated)
                {
                    return Mathf.Max(0, Passengers.PassengerCountProbability[i].NumPassengers);
                }
            }
            return Passengers.PassengerCountProbability[Passengers.PassengerCountProbability.Count - 1].NumPassengers;
        }

        /// <summary>切线 → 朝向。对应 UE5 <c>FRotationMatrix::MakeFromX(Tangent)</c>。</summary>
        public static Quaternion RotationFromTangent(Vector3 tangent)
        {
            tangent.Y = 0f;
            if (tangent.LengthSquared < 1e-6f) return Quaternion.Identity;
            return Quaternion.LookRotation(Vector3.Normalize(tangent));
        }
    }
}

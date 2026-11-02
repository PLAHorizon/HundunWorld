using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.UnrealFramework;

namespace NarrativePro.Vehicles.Mass
{
    /// <summary>
    /// 载具障碍物处理器（Flax 侧手写实现）。对应 UE5 UVehicleObstacleProcessor
    /// + UVehicleObstacleInitializer + UVehicleObstacleDestructor（VehicleObstacleProcessor.h/.cpp:15-101）。
    /// <para>
    /// UE5 用三个 Mass 观察者/处理器维护 <c>UMassVehicleSubsystem::VehicleObstacles</c> 哈希网格：
    /// Add（初始化，cpp:69-83）、Move（每帧，cpp:22-49）、Remove（销毁，cpp:95-101），
    /// 移动/避让与交通灯的交叉口占用判定都读这张网格。
    /// Flax 侧同样由它喂网格，并额外把「行人 Actor」登记成外部障碍物
    /// （UE5 里带 <c>UTrafficVehicleObstacleTrait</c> 的行人实体在 Flax 是 NPC Actor，见下）。
    /// </para>
    /// </summary>
    public class VehicleObstacleProcessor
    {
        /// <summary>载具子系统（持有网格）。</summary>
        public MassVehicleSubsystem VehicleSubsystem;

        /// <summary>障碍特质（半径/搜索参数）。</summary>
        public TrafficVehicleObstacleTrait ObstacleTrait = new TrafficVehicleObstacleTrait();

        /// <summary>外部（行人）障碍物重扫间隔（秒）。UE5 由实体观察者增量维护，Flax 侧行人是 Actor，需周期扫描。</summary>
        public float ExternalRefreshInterval = 0.25f;

        private float _refreshTimer;
        private readonly List<Actor> _presentExternalOwners = new List<Actor>();

        /// <summary>本帧登记的载具障碍物数。</summary>
        public int VehicleObstacleCount { get; private set; }

        /// <summary>本帧登记的外部（行人）障碍物数。</summary>
        public int ExternalObstacleCount { get; private set; }

        public VehicleObstacleProcessor(MassVehicleSubsystem vehicleSubsystem)
        {
            VehicleSubsystem = vehicleSubsystem;
        }

        /// <summary>
        /// 每帧同步。对应 UE5 UVehicleObstacleProcessor::Execute（cpp:22-49）：
        /// 用实体当前位置/半径把记录在网格里 Move，并刷新记录上的车道与速度，供前车/交叉口占用判定读取。
        /// </summary>
        public void UpdateTrafficObstacles(IReadOnlyList<TrafficVehicle> vehicles, float deltaTime)
        {
            if (VehicleSubsystem == null) return;
            _refreshTimer += deltaTime;

            int count = 0;
            if (vehicles != null)
            {
                foreach (var vehicle in vehicles)
                {
                    if (vehicle == null) continue;
                    float radius = Mathf.Max(1f, vehicle.Radius.Radius, ObstacleTrait.ObstacleRadius);

                    // 未登记过 → Add（UE5 观察者）；已登记 → Move（UE5 每帧处理器）
                    if (!VehicleSubsystem.VehicleObstacles.TryGet(vehicle.Handle, out var record))
                    {
                        vehicle.Obstacle.CellLocation = InitializeObstacle(vehicle);
                        VehicleSubsystem.VehicleObstacles.TryGet(vehicle.Handle, out record);
                    }
                    else
                    {
                        vehicle.Obstacle.CellLocation = VehicleSubsystem.VehicleObstacles.Move(
                            vehicle.Handle, record.CellLocation, vehicle.Location, radius);
                    }

                    if (record != null)
                    {
                        record.bIsVehicle = true;
                        record.Lane = vehicle.LaneHandle;
                        record.DistanceAlongLane = vehicle.DistanceAlongLane;
                        record.Velocity = vehicle.Velocity;
                        record.Radius = radius;
                    }
                    count++;
                }
            }
            VehicleObstacleCount = count;

            if (_refreshTimer >= ExternalRefreshInterval)
            {
                _refreshTimer = 0f;
                RefreshExternalObstacles();
            }
        }

        /// <summary>障碍物首次登记。对应 UE5 UVehicleObstacleInitializer::Execute（cpp:69-83）。</summary>
        public VehicleObstacleHashGrid.FCellLocation InitializeObstacle(TrafficVehicle vehicle)
        {
            if (VehicleSubsystem == null || vehicle == null) return default;
            float radius = Mathf.Max(1f, vehicle.Radius.Radius, ObstacleTrait.ObstacleRadius);
            return VehicleSubsystem.VehicleObstacles.Add(vehicle.Handle, vehicle.Location, radius);
        }

        /// <summary>障碍物注销。对应 UE5 UVehicleObstacleDestructor::Execute（cpp:95-101）。</summary>
        public void DestroyObstacle(TrafficVehicle vehicle)
        {
            if (VehicleSubsystem == null || vehicle == null) return;
            VehicleSubsystem.VehicleObstacles.Remove(vehicle.Handle, vehicle.Obstacle.CellLocation);
            vehicle.Obstacle.CellLocation = default;
        }

        /// <summary>
        /// 扫描场景内活的 NPC/行人 Actor，登记成载具要避让的外部障碍物；顺带清理已销毁的。
        /// UE5 侧这些是带 UTrafficVehicleObstacleTrait 的 Mass 实体，由三个观察者维护；
        /// Flax 侧行人由 <c>MassPedSpawnerSubsystem</c> 生成成 Actor，故这里按 Actor 扫描（同
        /// <c>MassPedSimulation.SyncVehicleObstacles</c> 的做法，反向对称）。
        /// </summary>
        public int RefreshExternalObstacles()
        {
            var subsystem = VehicleSubsystem;
            if (subsystem == null) return 0;

            _presentExternalOwners.Clear();
            int registered = 0;
            float radius = Mathf.Max(1f, ObstacleTrait.ObstacleRadius);
            float searchRadius = ObstacleTrait.ExternalSearchRadius;

            try
            {
                foreach (var actor in Level.GetActors<Actor>())
                {
                    if (actor == null) continue;

                    // 已上车的乘员挂在载具 Actor 下，不再算路面障碍
                    if (actor.Parent != null) continue;

                    var character = actor.GetScript<NarrativeCharacter>();
                    if (character == null) continue;

                    Vector3 location = actor.Position;
                    subsystem.RegisterExternalObstacle(actor, location, radius);
                    _presentExternalOwners.Add(actor);
                    registered++;
                }
            }
            catch (Exception ex)
            {
                MassVehicleLog.LogWarning($"外部障碍物扫描未完成（场景未就绪？）：{ex.Message}");
                return registered;
            }

            // 场景里已消失的行人要退出网格（否则尸体永远挡路）
            subsystem.PruneExternalObstacles(_presentExternalOwners);
            ExternalObstacleCount = registered;
            return registered;
        }
    }
}

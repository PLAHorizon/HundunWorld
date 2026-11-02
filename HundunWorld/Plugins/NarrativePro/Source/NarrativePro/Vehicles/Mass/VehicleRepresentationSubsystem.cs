using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.Vehicles.Mass
{
    /// <summary>
    /// 载具表现子系统（Flax 侧手写宿主）。对应 UE5 UVehicleRepresentationSubsystem
    /// （VehicleRepresentationSubsystem.h/.cpp:11-21）+ MassRepresentation 的 LOD→Actor 那一段。
    /// <para>
    /// UE5 侧本子系统只做「托管实体计数」（真正的 Actor 生成在 MassRepresentation +
    /// UVehicleSpawnerSubsystem），本类把两侧合一：按观察者距离给实体定档
    /// （<see cref="VehicleMovementSyncTrait"/> 的距离阈值），升到高档时向
    /// <see cref="VehicleSpawnerSubsystem"/> 请求 Actor，降档时归还 Actor，
    /// 并在超出关闭距离时给实体打上自动销毁标签（UE5 的 FAutoDestroyTag 语义）。
    /// </para>
    /// </summary>
    public class VehicleRepresentationSubsystem
    {
        /// <summary>当前实例。由 <see cref="VehicleTrafficSimulation"/> 创建/销毁。</summary>
        public static VehicleRepresentationSubsystem Instance { get; private set; }

        /// <summary>获取实例。</summary>
        public static VehicleRepresentationSubsystem Get() => Instance;

        /// <summary>载具子系统。</summary>
        public MassVehicleSubsystem VehicleSubsystem;

        /// <summary>Actor 生成子系统。对应 UE5 Initialize 里的 InitializeDependency（cpp:13）。</summary>
        public VehicleSpawnerSubsystem ActorSpawnerSubsystem;

        /// <summary>同步特质（LOD 距离阈值）。对应 UE5 trait 上的表示配置。</summary>
        public VehicleMovementSyncTrait SyncTrait = new VehicleMovementSyncTrait();

        /// <summary>托管实体计数。对应 UE5 HandledMassAgents（AddManagedEntity 的 TMap）。</summary>
        private readonly Dictionary<MassEntityHandle, int> _handledMassAgents = new Dictionary<MassEntityHandle, int>();

        /// <summary>当前高 LOD（有 Actor）的实体数。</summary>
        public int HighLodCount { get; private set; }

        /// <summary>本帧请求 Actor 的次数。</summary>
        public int ActorRequestsThisFrame { get; private set; }

        public VehicleRepresentationSubsystem(MassVehicleSubsystem vehicleSubsystem, VehicleSpawnerSubsystem actorSpawner)
        {
            VehicleSubsystem = vehicleSubsystem;
            ActorSpawnerSubsystem = actorSpawner;
        }

        /// <summary>绑定实例。</summary>
        public void Initialize() => Instance = this;

        /// <summary>解绑。</summary>
        public void Shutdown()
        {
            if (Instance == this) Instance = null;
            _handledMassAgents.Clear();
        }

        /// <summary>登记被管理的实体。对应 UE5 AddManagedEntity（cpp:18-21，<c>++HandledMassAgents[Entity]</c>）。</summary>
        public virtual void AddManagedEntity(MassEntityHandle entity)
        {
            if (!entity.IsValid) return;
            _handledMassAgents.TryGetValue(entity, out int count);
            _handledMassAgents[entity] = count + 1;
        }

        /// <summary>注销被管理的实体。</summary>
        public void RemoveManagedEntity(MassEntityHandle entity)
        {
            _handledMassAgents.Remove(entity);
        }

        /// <summary>当前托管实体数。</summary>
        public int ManagedEntityCount => _handledMassAgents.Count;

        /// <summary>
        /// 每帧按观察者距离重算档位并同步 Actor。
        /// 对应 UE5 MassRepresentation 的 LOD 更新 + <c>UVehicleMovementSyncTrait</c> 的 Actor 绑定。
        /// </summary>
        public void UpdateRepresentation(IReadOnlyList<TrafficVehicle> vehicles)
        {
            HighLodCount = 0;
            ActorRequestsThisFrame = 0;
            if (VehicleSubsystem == null || vehicles == null) return;

            Vector3 observer = VehicleSubsystem.ObserverLocation;

            foreach (var vehicle in vehicles)
            {
                if (vehicle == null) continue;

                float distance = vehicle.DistanceTo(observer);
                EVehicleRepresentationLOD previous = vehicle.LOD;

                if (distance > SyncTrait.DespawnDistance)
                {
                    vehicle.LOD = EVehicleRepresentationLOD.Off;
                    // 对应 UE5：远处实体打上 FAutoDestroyTag，由 AutoDestroyProcessor 结算
                    vehicle.bAutoDestroyTag = true;
                }
                else if (vehicle.LOD == EVehicleRepresentationLOD.High)
                {
                    // 滞后：只有超过 LowLodDistance 才降档，避免边界抖动
                    vehicle.LOD = distance > SyncTrait.LowLodDistance ? EVehicleRepresentationLOD.Low : EVehicleRepresentationLOD.High;
                }
                else if (distance <= SyncTrait.HighLodDistance)
                {
                    vehicle.LOD = EVehicleRepresentationLOD.High;
                }
                else
                {
                    vehicle.LOD = EVehicleRepresentationLOD.Low;
                }

                if (vehicle.LOD == EVehicleRepresentationLOD.High && vehicle.Actor == null)
                {
                    RequestActor(vehicle);
                }
                else if (vehicle.LOD != EVehicleRepresentationLOD.High && vehicle.Actor != null)
                {
                    ReleaseActor(vehicle);
                }

                if (previous != vehicle.LOD)
                {
                    AddManagedEntity(vehicle.Handle);
                }

                if (vehicle.LOD == EVehicleRepresentationLOD.High)
                {
                    HighLodCount++;
                    if (SyncTrait.bSyncLocationToActor && vehicle.Actor != null)
                    {
                        vehicle.ApplyToActor();
                    }
                }
            }
        }

        /// <summary>向生成子系统请求 Actor。对应 UE5 表示层提交 ActorSpawnRequest。</summary>
        public bool RequestActor(TrafficVehicle vehicle)
        {
            if (ActorSpawnerSubsystem == null || vehicle == null) return false;

            var request = new VehicleActorSpawnRequest
            {
                Vehicle = vehicle,
                TemplateName = vehicle.TemplateName,
                Guid = GuidFromHandle(vehicle.Handle),
            };

            var status = ActorSpawnerSubsystem.SpawnActor(request);
            ActorRequestsThisFrame++;
            if (status != EVehicleSpawnRequestStatus.Succeeded)
            {
                if (status == EVehicleSpawnRequestStatus.Failed)
                {
                    MassVehicleLog.LogWarning($"载具 {vehicle.Handle} 的 Actor 生成失败（暂以低档继续模拟）。");
                }
                vehicle.LOD = EVehicleRepresentationLOD.Low;
                return false;
            }
            return true;
        }

        /// <summary>归还 Actor。</summary>
        public void ReleaseActor(TrafficVehicle vehicle)
        {
            if (ActorSpawnerSubsystem != null)
            {
                ActorSpawnerSubsystem.ReleaseActor(vehicle);
            }
            else if (vehicle?.Actor != null)
            {
                FlaxEngine.Object.Destroy(vehicle.Actor);
                vehicle.Actor = null;
                vehicle.ComponentWrapper.Vehicle = null;
            }
        }

        /// <summary>句柄 → 稳定 Guid。对应 UE5 用 Mass Agent Guid 做 Actor 复用键。</summary>
        public static Guid GuidFromHandle(MassEntityHandle handle)
        {
            byte[] bytes = new byte[16];
            BitConverter.GetBytes(handle.Index).CopyTo(bytes, 0);
            BitConverter.GetBytes(handle.SerialNumber).CopyTo(bytes, 4);
            BitConverter.GetBytes(handle.Index ^ handle.SerialNumber).CopyTo(bytes, 8);
            BitConverter.GetBytes(unchecked(handle.SerialNumber * 397 + 17)).CopyTo(bytes, 12);
            return new Guid(bytes);
        }
    }
}

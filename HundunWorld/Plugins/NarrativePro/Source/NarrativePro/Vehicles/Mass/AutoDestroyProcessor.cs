using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.Vehicles.Mass
{
    /// <summary>
    /// 自动销毁处理器。对应 UE5 UAutoDestroyProcessor（AutoDestroyProcessor.h/.cpp:9-28）。
    /// <para>
    /// UE5 是观察者处理器：实体拿到 <c>FMassLowLODTag</c> 且带 <c>FAutoDestroyTag</c> 时
    /// <c>Context.Defer().DestroyEntities(...)</c>。
    /// Flax 侧「进入 Low/Off LOD」由 <see cref="VehicleRepresentationSubsystem"/> 的档位计算给出，
    /// 本类在同一判定上结算销毁（远处车流不再占内存/网格）。
    /// </para>
    /// </summary>
    [Serializable]
    public class AutoDestroyProcessor
    {
        /// <summary>载具子系统。</summary>
        public MassVehicleSubsystem VehicleSubsystem;

        /// <summary>表示子系统（销毁时要归还 Actor）。</summary>
        public VehicleRepresentationSubsystem Representation;

        /// <summary>存活时间上限（秒）。对应 UE5「低 LOD 即销毁」之外的兜底：车流循环后不无限累积。</summary>
        public float MaxAliveSeconds = 240f;

        /// <summary>本帧销毁数。</summary>
        public int DestroyedThisFrame { get; private set; }

        /// <summary>累计销毁数（诊断自证）。</summary>
        public int TotalDestroyed { get; private set; }

        public AutoDestroyProcessor(MassVehicleSubsystem vehicleSubsystem, VehicleRepresentationSubsystem representation)
        {
            VehicleSubsystem = vehicleSubsystem;
            Representation = representation;
        }

        /// <summary>
        /// 结算销毁请求。对应 UE5 UAutoDestroyProcessor::Execute。
        /// </summary>
        public int Execute()
        {
            DestroyedThisFrame = 0;
            if (VehicleSubsystem == null) return 0;

            var vehicles = VehicleSubsystem.Vehicles;
            for (int i = vehicles.Count - 1; i >= 0; i--)
            {
                var vehicle = vehicles[i];
                if (vehicle == null) continue;

                bool bShouldDestroy = vehicle.bDestroyRequested
                                      || (vehicle.bAutoDestroyTag && vehicle.LOD <= EVehicleRepresentationLOD.Low
                                          && vehicle.DistanceTo(VehicleSubsystem.ObserverLocation) > 1f)
                                      || (MaxAliveSeconds > 0f && vehicle.AliveTime > MaxAliveSeconds);

                if (!bShouldDestroy) continue;

                if (Representation != null)
                {
                    Representation.ReleaseActor(vehicle);
                }
                else if (vehicle.Actor != null)
                {
                    FlaxEngine.Object.Destroy(vehicle.Actor);
                    vehicle.Actor = null;
                }

                VehicleSubsystem.RemoveVehicle(vehicle.Handle);
                DestroyedThisFrame++;
                TotalDestroyed++;
            }

            return DestroyedThisFrame;
        }
    }
}

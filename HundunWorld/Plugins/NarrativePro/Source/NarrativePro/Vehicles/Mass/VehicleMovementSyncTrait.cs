using System;
using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.Vehicles.Mass
{
    /// <summary>
    /// 载具移动同步特质。对应 UE5 UVehicleMovementSyncTrait（VehicleMovementSyncTrait.h/.cpp:11-34）。
    /// <para>
    /// UE5 的 BuildTemplate 给高 LOD 载具加 <c>FVehicleComponentWrapperFragment</c>
    /// （包装 Chaos 轮式移动组件）与 <c>FMassVehicleMovementToActorTag</c>，
    /// 于是 Mass 的车道位置与 Actor 的物理变换双向同步。
    /// Flax 无 Chaos，同步是单向的（车道 → Actor），本特质即这一定义的载体，
    /// 由 <see cref="VehicleRepresentationSubsystem"/> 在取得/释放 Actor 时消费。
    /// </para>
    /// </summary>
    [Serializable]
    public class VehicleMovementSyncTrait
    {
        /// <summary>是否把车道位置写回 Actor。对应 UE5 的 MovementToActorTag。</summary>
        public bool bSyncLocationToActor = true;

        /// <summary>是否把车道切线朝向写回 Actor。</summary>
        public bool bSyncRotationToActor = true;

        /// <summary>升为高 LOD 的距离（世界单位）。</summary>
        public float HighLodDistance = 6000f;

        /// <summary>降回低 LOD 的距离（带滞后，避免抖动）。</summary>
        public float LowLodDistance = 7500f;

        /// <summary>超出该距离即完全关闭 Actor（并按 UE5 的 Low-LOD 自动销毁语义打标签）。</summary>
        public float DespawnDistance = 16000f;

        /// <summary>
        /// 写入实体片段。对应 UE5 BuildTemplate —— Flax 侧等价动作是给实体打上
        /// 「需要 Actor 同步」的标记（<see cref="TrafficVehicle.ComponentWrapper"/>）。
        /// </summary>
        public void ApplyTo(TrafficVehicle vehicle)
        {
            if (vehicle == null) return;
            vehicle.ComponentWrapper ??= new VehicleComponentWrapperFragment();
        }
    }
}

using System;
using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.Vehicles
{
    /// <summary>
    /// Narrative 轮式载具基类。对应 UE5 ANarrativeWheeledVehicle（NarrativeWheeledVehicle.h/.cpp）。
    /// UE5 中继承 ANarrativeVehicleBase，使用 Chaos 载具移动组件（UChaosVehicleMovementComponent）。
    /// 简化点：
    /// - Flax 无 Chaos Vehicle System，移动组件用 object 占位（Flax-不兼容: UE5 Chaos Vehicle System 在 Flax 无对应物，保留占位）
    /// - PossessedBy 简化为占位（Flax-不兼容: UE5 Chaos Vehicle System 在 Flax 无对应物，保留占位。原文玩家控制器强制 bReverseAsBrake 的逻辑 TODO）
    /// - 移除 UE5 复制（VehicleMovementComponent->SetIsReplicated）
    /// </summary>
    public abstract class NarrativeWheeledVehicle : NarrativeVehicleBase
    {
        /// <summary>载具移动组件名称。对应 UE5 VehicleMovementComponentName。</summary>
        public const string VehicleMovementComponentName = "VehicleMovementComp";

        /// <summary>载具模拟组件。对应 UE5 VehicleMovementComponent（UChaosVehicleMovementComponent）。
        /// Flax 无 Chaos Vehicle System，用 object 占位。</summary>
        [NonSerialized]
        protected object VehicleMovementComponent;

        /// <summary>获取轮式载具移动组件。对应 UE5 GetVehicleMovementComponent。
        /// Flax-不兼容: UE5 的 Chaos Vehicle System 在 Flax 无对应物，保留占位。原文 TODO: Flax 无 Chaos Vehicle System，需自定义实现。</summary>
        public virtual object GetVehicleMovementComponent()
        {
            return VehicleMovementComponent;
        }

        /// <summary>被控制器拥有时调用。对应 UE5 PossessedBy。
        /// 若为玩家控制器，强制开启 bReverseAsBrake。</summary>
        public override void PossessedBy(Actor newController)
        {
            base.PossessedBy(newController);

            // Flax-不兼容: UE5 的 Chaos Vehicle System 在 Flax 无对应物，保留占位。原文 TODO: 若为玩家控制器，强制 VehicleMovementComponent.bReverseAsBrake = true
        }
    }
}

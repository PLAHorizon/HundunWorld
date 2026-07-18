using System;
using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.Vehicles.Mass
{
    /// <summary>
    /// 载具特质。对应 UE5 UVehicleTrait（VehicleTrait.h）。
    /// 继承 UMassEntityTraitBase。定义载具实体的半径、PID 设置、载具设置、种子、乘客等片段。
    /// 简化点：
    /// - Flax 无 Mass Entity System，改为 [Serializable] class 占位（Flax 不兼容）
    /// - FMassEntityTemplateBuildContext/UWorld → object 占位
    /// </summary>
    [Serializable]
    public class VehicleTrait
    {
        /// <summary>Agent 半径片段。对应 UE5 Radius（FAgentRadiusFragment）。</summary>
        public AgentRadiusFragment Radius = new AgentRadiusFragment();

        /// <summary>PID 控制器设置。对应 UE5 PIDControllerSettings（FVehiclePIDControllerFragment）。</summary>
        public VehiclePIDControllerFragment PIDControllerSettings = new VehiclePIDControllerFragment();

        /// <summary>载具设置片段。对应 UE5 VehicleSettingsFragment。</summary>
        public VehicleSettingsFragment VehicleSettingsFragment = new VehicleSettingsFragment();

        /// <summary>可选种子片段，为 0 时为每个实体随机选择。对应 UE5 SeedFragment。</summary>
        public SeedFragment SeedFragment = new SeedFragment();

        /// <summary>载具内的乘客定义。对应 UE5 Passengers（FPassengerSettingsFragment）。</summary>
        public PassengerSettingsFragment Passengers = new PassengerSettingsFragment();

        /// <summary>构建实体模板。对应 UE5 BuildTemplate。
        /// Flax-不兼容: UE5 的 Mass Entity System 在 Flax 无对应物，保留占位。原文 TODO: Flax 无 Mass Entity System，需自定义实现。</summary>
        /// <param name="buildContext">模板构建上下文（占位）。</param>
        /// <param name="world">世界（占位）。</param>
        public virtual void BuildTemplate(object buildContext, object world)
        {
            // Flax-不兼容: UE5 的 Mass Entity System 在 Flax 无对应物，保留占位。原文 TODO: Flax 无 Mass Entity System，需自定义实现
        }
    }
}

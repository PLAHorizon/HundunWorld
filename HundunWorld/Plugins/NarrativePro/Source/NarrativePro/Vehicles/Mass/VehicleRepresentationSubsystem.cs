using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.Vehicles.Mass
{
    /// <summary>
    /// 载具表现子系统。对应 UE5 UVehicleRepresentationSubsystem（VehicleRepresentationSubsystem.h）。
    /// 继承 UMassRepresentationSubsystem。管理 Mass 载具的表现。
    /// 简化点：
    /// - Flax 无 Mass Entity System，改为 [Serializable] 单例类占位（Flax 不兼容）
    /// </summary>
    [Serializable]
    public class VehicleRepresentationSubsystem
    {
        /// <summary>单例实例。</summary>
        public static VehicleRepresentationSubsystem Instance { get; } = new VehicleRepresentationSubsystem();

        /// <summary>获取单例。</summary>
        public static VehicleRepresentationSubsystem Get() => Instance;

        /// <summary>初始化。对应 UE5 Initialize。</summary>
        public virtual void Initialize()
        {
            // Flax-不兼容: UE5 的 Mass Entity System 在 Flax 无对应物，保留占位。原文 TODO: Flax 无 Mass Entity System，需自定义实现
        }

        /// <summary>添加被管理的实体。对应 UE5 AddManagedEntity。</summary>
        /// <param name="entity">实体句柄。</param>
        public virtual void AddManagedEntity(MassEntityHandle entity)
        {
            // Flax-不兼容: UE5 的 Mass Entity System 在 Flax 无对应物，保留占位。原文 TODO: Flax 无 Mass Entity System，需自定义实现
        }
    }
}

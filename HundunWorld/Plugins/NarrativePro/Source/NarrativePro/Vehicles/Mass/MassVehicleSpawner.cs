using System;
using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.Vehicles.Mass
{
    /// <summary>
    /// Mass 载具生成器。对应 UE5 AMassVehicleSpawner（MassVehicleSpawner.h）。
    /// 继承 AMassSpawner。Flax 无 Mass，Actor 为 sealed，改为 Script 挂载到 Actor 上。
    /// 简化点：
    /// - AMassSpawner → Script 占位
    /// - 生成逻辑用占位保留（Flax 不兼容）
    /// </summary>
    public class MassVehicleSpawner : Script
    {
        /// <summary>交叉口注解组件。对应 UE5 IntersectionAnnotations（UTrafficIntersectionAnnotations*）。</summary>
        [NonSerialized]
        public TrafficIntersectionAnnotations IntersectionAnnotations;

        /// <summary>设置生成数量。对应 UE5 SetSpawnCount。
        /// 下次使用此生成器生成实体时生效。</summary>
        /// <param name="newCount">新的生成数量。</param>
        public virtual void SetSpawnCount(int newCount)
        {
            // Flax-不兼容: UE5 的 Mass Entity System 在 Flax 无对应物，保留占位。原文 TODO: Flax 无 Mass Entity System，需自定义实现
        }
    }
}

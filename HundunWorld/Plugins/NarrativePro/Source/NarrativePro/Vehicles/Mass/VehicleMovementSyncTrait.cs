using System;
using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.Vehicles.Mass
{
    /// <summary>
    /// 载具移动同步特质。对应 UE5 UVehicleMovementSyncTrait（VehicleMovementSyncTrait.h）。
    /// 继承 UMassAgentSyncTrait。同步 Mass 与载具 Actor 之间的移动。
    /// 简化点：
    /// - Flax 无 Mass Entity System，改为 [Serializable] class 占位（Flax 不兼容）
    /// - FMassEntityTemplateBuildContext/UWorld → object 占位
    /// </summary>
    [Serializable]
    public class VehicleMovementSyncTrait
    {
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

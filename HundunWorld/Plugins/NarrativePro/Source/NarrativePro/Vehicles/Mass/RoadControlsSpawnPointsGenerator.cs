using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.Vehicles.Mass
{
    /// <summary>
    /// 道路控制生成点生成器。对应 UE5 URoadControlsSpawnPointsGenerator（RoadControlsSpawnPointsGenerator.h）。
    /// 继承 UMassEntityZoneGraphSpawnPointsGenerator。Flax 无 Mass，改为 [Serializable] class 占位。
    /// 简化点：
    /// - Flax 无 Mass Entity System，方法实现用占位保留（Flax 不兼容）
    /// - FFinishedGeneratingSpawnDataSignature → object 占位
    /// </summary>
    [Serializable]
    public class RoadControlsSpawnPointsGenerator
    {
        /// <summary>生成生成点。对应 UE5 Generate。
        /// Flax-不兼容: UE5 的 Mass Entity System 在 Flax 无对应物，保留占位。原文 TODO: Flax 无 Mass Entity System，需自定义实现。</summary>
        /// <param name="queryOwner">查询拥有者（占位）。</param>
        /// <param name="entityTypes">实体类型列表。</param>
        /// <param name="count">生成数量。</param>
        /// <param name="finishedGeneratingSpawnPointsDelegate">完成生成回调（占位）。</param>
        public virtual void Generate(object queryOwner, List<MassSpawnedEntityType> entityTypes, int count, object finishedGeneratingSpawnPointsDelegate)
        {
            // Flax-不兼容: UE5 的 Mass Entity System 在 Flax 无对应物，保留占位。原文 TODO: Flax 无 Mass Entity System，需自定义实现
        }
    }
}

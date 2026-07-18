using System;
using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.Vehicles.Mass
{
    /// <summary>
    /// 载具初始化处理器。对应 UE5 UInitVehicleProcessor（InitVehicleProcessor.h）。
    /// 继承 UMassObserverProcessor。初始化载具实体。
    /// 简化点：
    /// - Flax 无 Mass Entity System，改为 [Serializable] class 占位（Flax 不兼容）
    /// - FMassEntityQuery/FMassEntityManager/FMassExecutionContext → object 占位
    /// </summary>
    [Serializable]
    public class InitVehicleProcessor
    {
        /// <summary>实体查询。对应 UE5 EntityQuery（FMassEntityQuery）。</summary>
        [NonSerialized]
        public object EntityQuery;

        /// <summary>设置车道查询。对应 UE5 SetLaneQuery（FMassEntityQuery）。</summary>
        [NonSerialized]
        public object SetLaneQuery;

        public InitVehicleProcessor()
        {
            // Flax-不兼容: UE5 的 Mass Entity System 在 Flax 无对应物，保留占位。原文 TODO: Flax 无 Mass Entity System，需自定义实现
        }

        /// <summary>配置查询。对应 UE5 ConfigureQueries。</summary>
        public virtual void ConfigureQueries(object entityManager)
        {
            // Flax-不兼容: UE5 的 Mass Entity System 在 Flax 无对应物，保留占位。原文 TODO: Flax 无 Mass Entity System，需自定义实现
        }

        /// <summary>执行。对应 UE5 Execute。</summary>
        public virtual void Execute(object entityManager, object context)
        {
            // Flax-不兼容: UE5 的 Mass Entity System 在 Flax 无对应物，保留占位。原文 TODO: Flax 无 Mass Entity System，需自定义实现
        }
    }
}

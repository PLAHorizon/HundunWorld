using System;
using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.Vehicles.Mass
{
    /// <summary>
    /// 载具障碍物处理器。对应 UE5 UVehicleObstacleProcessor（VehicleObstacleProcessor.h）。
    /// 继承 UMassProcessor。处理载具避让的障碍物哈希网格更新。
    /// 简化点：
    /// - Flax 无 Mass Entity System，改为 [Serializable] class 占位（Flax 不兼容）
    /// - FMassEntityQuery/FMassEntityManager/FMassExecutionContext → object 占位
    /// </summary>
    [Serializable]
    public class VehicleObstacleProcessor
    {
        /// <summary>实体查询。对应 UE5 EntityQuery（FMassEntityQuery）。</summary>
        [NonSerialized]
        public object EntityQuery;

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

    /// <summary>
    /// 载具障碍物初始化器。对应 UE5 UVehicleObstacleInitializer（VehicleObstacleProcessor.h）。
    /// 继承 UMassObserverProcessor。初始化障碍物实体以便在哈希网格中查询和更新其位置。
    /// 简化点：Flax 无 Mass Entity System，改为 [Serializable] class 占位（Flax 不兼容）。
    /// </summary>
    [Serializable]
    public class VehicleObstacleInitializer
    {
        /// <summary>实体查询。对应 UE5 EntityQuery（FMassEntityQuery）。</summary>
        [NonSerialized]
        public object EntityQuery;

        public VehicleObstacleInitializer()
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

    /// <summary>
    /// 载具障碍物销毁器。对应 UE5 UVehicleObstacleDestructor（VehicleObstacleProcessor.h）。
    /// 继承 UMassObserverProcessor。清理障碍物实体，使其不再在哈希网格中更新和可用。
    /// 简化点：Flax 无 Mass Entity System，改为 [Serializable] class 占位（Flax 不兼容）。
    /// </summary>
    [Serializable]
    public class VehicleObstacleDestructor
    {
        /// <summary>实体查询。对应 UE5 EntityQuery（FMassEntityQuery）。</summary>
        [NonSerialized]
        public object EntityQuery;

        public VehicleObstacleDestructor()
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

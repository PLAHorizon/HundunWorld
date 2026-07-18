using System;
using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.Vehicles.Mass
{
    /// <summary>
    /// 自动销毁处理器。对应 UE5 UAutoDestroyProcessor（AutoDestroyProcessor.h）。
    /// 继承 UMassObserverProcessor。当实体处于 Low LOD 且带 AutoDestroyTag 时自动销毁。
    /// 简化点：
    /// - Flax 无 Mass Entity System，改为 [Serializable] class 占位（Flax 不兼容）
    /// - FMassEntityQuery/FMassEntityManager/FMassExecutionContext → object 占位
    /// </summary>
    [Serializable]
    public class AutoDestroyProcessor
    {
        /// <summary>实体查询。对应 UE5 EntityQuery（FMassEntityQuery）。</summary>
        [NonSerialized]
        public object EntityQuery;

        public AutoDestroyProcessor()
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

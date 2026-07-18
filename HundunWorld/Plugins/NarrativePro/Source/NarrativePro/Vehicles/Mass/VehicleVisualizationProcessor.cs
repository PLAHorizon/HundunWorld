using System;
using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.Vehicles.Mass
{
    /// <summary>
    /// 载具可视化打包数据。对应 UE5 FVehicleVisualPackedData（VehicleVisualizationProcessor.h）。
    /// 发送到材质用于计算载具颜色和其他效果的打包数据。
    /// </summary>
    [Serializable]
    public class VehicleVisualPackedData
    {
        public VehicleVisualPackedData() { }

        /// <summary>载具材质已用索引 0 实现泥污效果，此值设为 0 即关闭。</summary>
        public float Dirt = 0f;

        /// <summary>颜色值。</summary>
        public float Color = 0f;
    }

    /// <summary>
    /// 载具可视化处理器。对应 UE5 UVehicleVisualizationProcessor（VehicleVisualizationProcessor.h）。
    /// 继承 UMassTranslator。管理世界中载具的可视化，主要用于高精度载具 BP 及 Mass 与 BP 之间的同步。
    /// 简化点：
    /// - Flax 无 Mass Entity System，改为 [Serializable] class 占位（Flax 不兼容）
    /// - FMassEntityQuery/FMassEntityManager/FMassExecutionContext → object 占位
    /// </summary>
    [Serializable]
    public class VehicleVisualizationProcessor
    {
        /// <summary>实体查询。对应 UE5 EntityQuery（FMassEntityQuery）。</summary>
        [NonSerialized]
        public object EntityQuery;

        /// <summary>速度同步查询。对应 UE5 SyncVelocityQuery（FMassEntityQuery）。</summary>
        [NonSerialized]
        public object SyncVelocityQuery;

        public VehicleVisualizationProcessor()
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
    /// 载具 ISM 可视化处理器。对应 UE5 UVehicleISMVisualizationProcessor（VehicleVisualizationProcessor.h）。
    /// 继承 UMassProcessor。使用实例化静态网格（ISM）管理载具可视化。
    /// 简化点：Flax 无 Mass Entity System，改为 [Serializable] class 占位（Flax 不兼容）。
    /// </summary>
    [Serializable]
    public class VehicleISMVisualizationProcessor
    {
        /// <summary>实体查询。对应 UE5 EntityQuery（FMassEntityQuery）。</summary>
        [NonSerialized]
        public object EntityQuery;

        public VehicleISMVisualizationProcessor()
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

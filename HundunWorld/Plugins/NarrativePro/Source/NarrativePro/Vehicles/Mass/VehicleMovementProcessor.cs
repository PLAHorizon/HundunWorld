using System;
using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.Vehicles.Mass
{
    /// <summary>
    /// 载具移动处理器。对应 UE5 UVehicleMovementProcessor（VehicleMovementProcessor.h）。
    /// 继承 UMassProcessor。管理载具沿 zonegraph 的移动，根据局部障碍物、速度等移动载具并在车道末端选择新车道。
    /// 简化点：
    /// - Flax 无 Mass Entity System，改为 [Serializable] class 占位（Flax 不兼容）
    /// - FMassEntityQuery/FMassEntityManager/FMassExecutionContext → object 占位
    /// - float&amp; → ref float
    /// </summary>
    [Serializable]
    public class VehicleMovementProcessor
    {
        /// <summary>移动载具查询。对应 UE5 MoveVehicleQuery。</summary>
        [NonSerialized]
        public object MoveVehicleQuery;

        /// <summary>计算速度查询。对应 UE5 CalculateSpeedQuery。</summary>
        [NonSerialized]
        public object CalculateSpeedQuery;

        /// <summary>计算车道查询。对应 UE5 CalculateLaneQuery。</summary>
        [NonSerialized]
        public object CalculateLaneQuery;

        /// <summary>下一辆车查询。对应 UE5 NextVehicleQuery。</summary>
        [NonSerialized]
        public object NextVehicleQuery;

        public VehicleMovementProcessor()
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

        /// <summary>根据障碍物距离计算速度。对应 UE5 CalculateSpeedFromObstacle（static）。</summary>
        /// <param name="distanceToObstacle">与障碍物距离。</param>
        /// <param name="minDistanceToObstacle">最小距离。</param>
        /// <param name="breakingDistanceToObstacle">刹车距离。</param>
        /// <param name="brakingPower">刹车力度。</param>
        /// <param name="speed">输出速度。</param>
        public static void CalculateSpeedFromObstacle(float distanceToObstacle, float minDistanceToObstacle, float breakingDistanceToObstacle, float brakingPower, ref float speed)
        {
            // Flax-不兼容: UE5 的 Mass Entity System 在 Flax 无对应物，保留占位。原文 TODO: Flax 无 Mass Entity System，需自定义实现
        }
    }
}

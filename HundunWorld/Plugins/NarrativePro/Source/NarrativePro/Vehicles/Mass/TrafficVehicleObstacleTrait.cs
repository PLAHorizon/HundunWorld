using System;
using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.Vehicles.Mass
{
    /// <summary>
    /// 交通载具障碍物特质。对应 UE5 UTrafficVehicleObstacleTrait（TrafficVehicleObstacleTrait.h/.cpp:9-17）。
    /// <para>
    /// UE5 的 BuildTemplate 只做一件事：给实体加 <c>FVehicleObstacleFragment</c> +
    /// <c>UVehicleObstacleInitializer</c> 所需的标签，让该实体进入载具避让用的障碍哈希网格。
    /// Flax 无 Mass 模板构建上下文，因此这里等价为一组「进入网格时用的参数」，
    /// 由 <see cref="VehicleObstacleProcessor"/> 读取（半径/搜索范围）并有真实消费者。
    /// </para>
    /// </summary>
    [Serializable]
    public class TrafficVehicleObstacleTrait
    {
        /// <summary>障碍物半径（世界单位）。对应 UE5 加进网格的 extent 半边长。</summary>
        public float ObstacleRadius = 45f;

        /// <summary>外部障碍物（行人等）的登记上限半径；&lt;=0 表示不裁剪。</summary>
        public float ExternalSearchRadius = 4000f;

        /// <summary>是否把非载具 Actor（行人/NPC）也登记为载具要避让的障碍。</summary>
        public bool bRegisterPedsAsObstacles = true;

        /// <summary>
        /// 写入实体片段。对应 UE5 BuildTemplate —— Flax 侧等价动作是确保实体带上了
        /// <see cref="TrafficVehicle.Obstacle"/> 并允许进网格（实体构造时已建，此处做显式声明与校验）。
        /// </summary>
        public void ApplyTo(TrafficVehicle vehicle)
        {
            if (vehicle == null) return;
            vehicle.Obstacle ??= new VehicleObstacleFragment();
            vehicle.Radius.Radius = Mathf.Max(vehicle.Radius.Radius, ObstacleRadius);
        }
    }
}

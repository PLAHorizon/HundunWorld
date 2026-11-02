using System;
using FlaxEngine;

namespace NarrativePro.Vehicles.Mass
{
    /// <summary>
    /// 交通灯设置。对应 UE5 UTrafficLightSettings（TrafficLightSettings.h/.cpp）。
    /// UE5 继承 UMassModuleSettings 并以类默认对象被 <c>GetDefault&lt;UTrafficLightSettings&gt;()</c> 读取；
    /// Flax 无 Mass 模块设置，改由 <see cref="MassVehicleSubsystem.TrafficLightSettings"/> 持有同一份配置，
    /// 移动/交通灯/注解三个消费者都从那里读标签名与提前关闭时间。
    /// </summary>
    [Serializable]
    public class TrafficLightSettings
    {
        /// <summary>默认交叉口标签名（UE5 资产里配的是 ZoneGraph tag "Intersection"）。</summary>
        public const string DefaultIntersectionTagName = "Intersection";

        /// <summary>默认关闭标签名（UE5 资产里配的是 ZoneGraph tag "Closed"）。</summary>
        public const string DefaultClosedTagName = "Closed";

        /// <summary>标记区域为交叉口的标签。对应 UE5 IntersectionTag（FZoneGraphTag）。</summary>
        public ZoneGraphTag IntersectionTag = new ZoneGraphTag(DefaultIntersectionTagName);

        /// <summary>标记车道为关闭/不可通过的标签。对应 UE5 ClosedTag（FZoneGraphTag）。</summary>
        public ZoneGraphTag ClosedTag = new ZoneGraphTag(DefaultClosedTagName);

        /// <summary>提前关闭车道的时间（秒），确保在换周期前没有车辆尝试通过。对应 UE5 LaneCloseAdvanceTime。</summary>
        public float LaneCloseAdvanceTime = 3f;

        /// <summary>交叉口查询范围（世界单位）。对应 UE5 TrafficLightSubsystem.cpp 的 BuildAABB(SideAverage, 2000)。</summary>
        public float IntersectionOccupancyQueryExtent = 2000f;

        /// <summary>交叉口「不够空间就不进」的阈值。对应 UE5 cpp 的 Space &lt; 800 判定。</summary>
        public float MinimumLaneSpaceToEnter = 800f;

        /// <summary>非方形路口每侧放行时长。对应 UE5 cpp:255-261 硬编码的 10 秒。</summary>
        public float SimplePeriodDuration = 10f;

        /// <summary>方形路口的「直行+右转」周期时长。对应 UE5 cpp:230-236 硬编码的 15 秒。</summary>
        public float StraightPeriodDuration = 15f;

        /// <summary>方形路口的单侧全放行周期时长。对应 UE5 cpp:238-251 硬编码的 10 秒。</summary>
        public float AllDirectionsPeriodDuration = 10f;

        /// <summary>所有周期时长的统一缩放（演示时加快切换）。UE5 无此项，属可配置扩展。</summary>
        public float PeriodDurationScale = 1f;

        /// <summary>把 UE5 硬编码时长按配置缩放。</summary>
        public float ScaleDuration(float duration)
        {
            if (duration < 0f) return duration; // -1（常驻周期）不缩放
            return duration * (PeriodDurationScale > 0f ? PeriodDurationScale : 1f);
        }
    }
}

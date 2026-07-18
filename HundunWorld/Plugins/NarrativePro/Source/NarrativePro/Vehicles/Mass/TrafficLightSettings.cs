using System;
using FlaxEngine;

namespace NarrativePro.Vehicles.Mass
{
    /// <summary>
    /// 交通灯设置。对应 UE5 UTrafficLightSettings（TrafficLightSettings.h）。
    /// 继承 UMassModuleSettings。Flax 无 Mass，改为 [Serializable] class 占位。
    /// 配置交通灯行为相关参数。
    /// </summary>
    [Serializable]
    public class TrafficLightSettings
    {
        /// <summary>标记区域为交叉口的标签。对应 UE5 IntersectionTag（FZoneGraphTag）。</summary>
        public ZoneGraphTag IntersectionTag = new ZoneGraphTag();

        /// <summary>标记车道为关闭/不可通过的标签。对应 UE5 ClosedTag（FZoneGraphTag）。</summary>
        public ZoneGraphTag ClosedTag = new ZoneGraphTag();

        /// <summary>提前关闭车道的时间（秒），确保在换周期前没有车辆尝试通过。</summary>
        public float LaneCloseAdvanceTime = 3f;
    }
}

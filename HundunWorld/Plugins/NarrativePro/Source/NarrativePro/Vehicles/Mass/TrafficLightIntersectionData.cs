using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.Vehicles.Mass
{
    /// <summary>
    /// ZoneGraph 注解事件基类占位。对应 UE5 FZoneGraphAnnotationEventBase。
    /// </summary>
    [Serializable]
    public abstract class ZoneGraphAnnotationEventBase { }

    /// <summary>
    /// 车道状态。对应 UE5 ELaneState（TrafficLightIntersectionData.h）。
    /// </summary>
    public enum ELaneState : byte
    {
        Open,
        Closed
    }

    /// <summary>
    /// 交叉口侧规则。对应 UE5 EIntersectionSideRule（TrafficLightIntersectionData.h）。
    /// Bitmask 枚举，定义交叉口中可用的车道方向。
    /// </summary>
    [Flags]
    public enum EIntersectionSideRule : byte
    {
        AllClosed = 0,
        RightOpen = 1 << 1,
        StraightOpen = 1 << 2,
        LeftOpen = 1 << 3,
        AllDirectionsOpen = RightOpen | StraightOpen | LeftOpen
    }

    /// <summary>
    /// 交叉口侧。对应 UE5 FTrafficIntersectionSide（TrafficLightIntersectionData.h）。
    /// 定义来自一个方向（进入交叉口）的车道。
    /// </summary>
    [Serializable]
    public class TrafficIntersectionSide
    {
        public TrafficIntersectionSide() { }

        /// <summary>此侧的交叉口内车道（来自一个方向的进入车道）。</summary>
        public List<ZoneGraphLaneHandle> Lanes = new List<ZoneGraphLaneHandle>();

        /// <summary>对此交叉口侧的自定义值位掩码（EIntersectionSideRule），例如过场动画期间使用。</summary>
        public EIntersectionSideRule SideOverride = EIntersectionSideRule.AllClosed;

        public Vector3 DirectionIntoIntersection = Vector3.Zero;
        public Vector3 SideLocation = Vector3.Zero;
    }

    /// <summary>
    /// 交叉口侧句柄。对应 UE5 FTrafficIntersectionSideHandle（TrafficLightIntersectionData.h）。
    /// </summary>
    [Serializable]
    public class TrafficIntersectionSideHandle
    {
        public TrafficIntersectionSideHandle() { }

        public TrafficIntersectionSideHandle(ZoneGraphDataHandle inZoneGraphDataHandle, int inIntersectionIndex, int inSideIndex)
        {
            ZoneGraphDataHandle = inZoneGraphDataHandle;
            IntersectionIndex = inIntersectionIndex;
            SideIndex = inSideIndex;
        }

        public ZoneGraphDataHandle ZoneGraphDataHandle = ZoneGraphDataHandle.Invalid;
        public int IntersectionIndex = -1;
        public int SideIndex = -1;

        /// <summary>获取此句柄的交叉口侧列表。对应 UE5 GetIntersectionSides。</summary>
        public List<TrafficIntersectionSide> GetIntersectionSides(TrafficLightSubsystem trafficLightSubsystem)
        {
            // Flax-不兼容: UE5 的 Mass/ZoneGraph 在 Flax 无对应物，保留占位。原文 TODO: Flax 无 Mass/ZoneGraph，需自定义实现
            return null;
        }

        /// <summary>获取可变的交叉口侧列表。对应 UE5 GetMutableIntersectionSides。</summary>
        public List<TrafficIntersectionSide> GetMutableIntersectionSides(TrafficLightSubsystem trafficLightSubsystem)
        {
            // Flax-不兼容: UE5 的 Mass/ZoneGraph 在 Flax 无对应物，保留占位。原文 TODO: Flax 无 Mass/ZoneGraph，需自定义实现
            return null;
        }

        /// <summary>获取交叉口侧。对应 UE5 GetIntersectionSide。</summary>
        public TrafficIntersectionSide GetIntersectionSide(TrafficLightSubsystem trafficLightSubsystem)
        {
            // Flax-不兼容: UE5 的 Mass/ZoneGraph 在 Flax 无对应物，保留占位。原文 TODO: Flax 无 Mass/ZoneGraph，需自定义实现
            return null;
        }

        /// <summary>获取可变的交叉口侧。对应 UE5 GetMutableIntersectionSide。</summary>
        public TrafficIntersectionSide GetMutableIntersectionSide(TrafficLightSubsystem trafficLightSubsystem)
        {
            // Flax-不兼容: UE5 的 Mass/ZoneGraph 在 Flax 无对应物，保留占位。原文 TODO: Flax 无 Mass/ZoneGraph，需自定义实现
            return null;
        }

        /// <summary>获取此句柄的交叉口。对应 UE5 GetIntersection。</summary>
        public TrafficLightIntersection GetIntersection(TrafficLightSubsystem trafficLightSubsystem)
        {
            // Flax-不兼容: UE5 的 Mass/ZoneGraph 在 Flax 无对应物，保留占位。原文 TODO: Flax 无 Mass/ZoneGraph，需自定义实现
            return null;
        }

        /// <summary>此句柄是否有效。对应 UE5 IsValid。</summary>
        public bool IsValid()
        {
            // Flax-不兼容: UE5 的 Mass/ZoneGraph 在 Flax 无对应物，保留占位。原文 TODO: Flax 无 Mass/ZoneGraph，需自定义实现
            return IntersectionIndex >= 0 && SideIndex >= 0;
        }
    }

    /// <summary>
    /// 交通周期。对应 UE5 FTrafficPeriod（TrafficLightIntersectionData.h）。
    /// 定义此周期内管理的车道与持续时间。
    /// </summary>
    [Serializable]
    public class TrafficPeriod
    {
        public TrafficPeriod() { }

        public TrafficPeriod(List<ZoneGraphLaneHandle> inLanes, float inDuration, EIntersectionSideRule inLanesCovered)
        {
            Lanes = inLanes;
            LanesCoveredMask = inLanesCovered;
            Duration = inDuration;
        }

        /// <summary>此周期内管理的车道。</summary>
        public List<ZoneGraphLaneHandle> Lanes = new List<ZoneGraphLaneHandle>();

        /// <summary>此周期影响的车道类型。</summary>
        public EIntersectionSideRule LanesCoveredMask = EIntersectionSideRule.AllClosed;

        public float Duration = -1f;
    }

    /// <summary>
    /// 交通周期事件。对应 UE5 FTrafficPeriodEvent（TrafficLightIntersectionData.h）。
    /// 继承 FZoneGraphAnnotationEventBase。
    /// </summary>
    [Serializable]
    public class TrafficPeriodEvent : ZoneGraphAnnotationEventBase
    {
        public TrafficPeriodEvent() { }

        public TrafficPeriod Period = new TrafficPeriod();
        public ELaneState State = ELaneState.Open;
    }

    /// <summary>
    /// 交通灯交叉口。对应 UE5 FTrafficLightIntersection（TrafficLightIntersectionData.h）。
    /// 管理交叉口的多侧车道与周期切换。
    /// </summary>
    [Serializable]
    public class TrafficLightIntersection
    {
        public TrafficLightIntersection() { }

        public TrafficLightIntersection(int intersectionZoneIndex)
        {
            ZoneIndex = intersectionZoneIndex;
        }

        /// <summary>标识符，避免重复添加交叉口。</summary>
        public int ZoneIndex = -1;

        /// <summary>最多 4 侧。</summary>
        public List<TrafficIntersectionSide> IntersectionSides = new List<TrafficIntersectionSide>();

        public List<TrafficPeriod> TrafficPeriods = new List<TrafficPeriod>();
        public int CurrentPeriodIndex = -1;
        public float RemainingPeriodDuration = -1f;
        public bool bOverrideIntersection = false;

        /// <summary>获取此交叉口的所有车道。对应 UE5 GetLanes。</summary>
        public List<ZoneGraphLaneHandle> GetLanes()
        {
            var lanes = new List<ZoneGraphLaneHandle>();
            foreach (var side in IntersectionSides)
            {
                if (side?.Lanes != null) lanes.AddRange(side.Lanes);
            }
            return lanes;
        }

        /// <summary>调试绘制。对应 UE5 DrawDebug。</summary>
        public void DrawDebug(ZoneGraphStorage storage)
        {
            // Flax-不兼容: UE5 的 Mass/ZoneGraph 调试绘制 在 Flax 无对应物，保留占位。原文 TODO: Flax 无 Mass/ZoneGraph 调试绘制，需自定义实现
        }

        /// <summary>对侧按方向角排序。对应 UE5 SortSides。</summary>
        public void SortSides()
        {
            // Flax-不兼容: UE5 的 Mass/ZoneGraph 在 Flax 无对应物，保留占位。原文 TODO: Flax 无 Mass/ZoneGraph，按需实现侧向排序
            // UE5 原实现基于方向向量与参考方向(1,0,0)的带符号夹角排序
        }

        /// <summary>获取当前周期。对应 UE5 GetCurrentPeriod。</summary>
        public TrafficPeriod GetCurrentPeriod()
        {
            if (CurrentPeriodIndex >= 0 && CurrentPeriodIndex < TrafficPeriods.Count)
                return TrafficPeriods[CurrentPeriodIndex];
            return null;
        }

        /// <summary>推进到下一个周期并返回。对应 UE5 IncrementCurrentPeriod。</summary>
        public TrafficPeriod IncrementCurrentPeriod()
        {
            // Flax-不兼容: UE5 的 Mass/ZoneGraph 在 Flax 无对应物，保留占位。原文 TODO: Flax 无 Mass/ZoneGraph，需自定义实现
            if (TrafficPeriods == null || TrafficPeriods.Count == 0) return null;
            CurrentPeriodIndex = (CurrentPeriodIndex + 1) % TrafficPeriods.Count;
            return TrafficPeriods[CurrentPeriodIndex];
        }

        /// <summary>交叉口是否为方形。对应 UE5 IsSquareShaped。</summary>
        public bool IsSquareShaped()
        {
            // Flax-不兼容: UE5 的 Mass/ZoneGraph 在 Flax 无对应物，保留占位。原文 TODO: Flax 无 Mass/ZoneGraph，需自定义实现
            return false;
        }

        /// <summary>获取两个侧之间连接的车道。对应 UE5 GetSidesConnectingLanes。</summary>
        public int GetSidesConnectingLanes(int startSideIndex, int endSideIndex, ZoneGraphStorage zoneGraphStorage, List<ZoneGraphLaneHandle> outTrafficLanes)
        {
            // Flax-不兼容: UE5 的 Mass/ZoneGraph 在 Flax 无对应物，保留占位。原文 TODO: Flax 无 Mass/ZoneGraph，需自定义实现
            return 0;
        }

        /// <summary>设置是否覆盖交叉口。对应 UE5 SetOverrideIntersection。</summary>
        public void SetOverrideIntersection(bool bShouldOverrideIntersection)
        {
            bOverrideIntersection = bShouldOverrideIntersection;
        }
    }

    /// <summary>
    /// 交通灯数据。对应 UE5 FTrafficLightData（TrafficLightIntersectionData.h）。
    /// 存储某 ZoneGraph 数据下的所有交叉口。
    /// </summary>
    [Serializable]
    public class TrafficLightData
    {
        public TrafficLightData() { }

        public ZoneGraphDataHandle DataHandle = ZoneGraphDataHandle.Invalid;

        public List<TrafficLightIntersection> Intersections = new List<TrafficLightIntersection>();

        /// <summary>查找或添加指定 ZoneIndex 的交叉口。对应 UE5 FindOrAddIntersection。</summary>
        public TrafficLightIntersection FindOrAddIntersection(int intersectionZoneIndex)
        {
            foreach (var i in Intersections)
            {
                if (i.ZoneIndex == intersectionZoneIndex) return i;
            }
            var newIntersection = new TrafficLightIntersection(intersectionZoneIndex);
            Intersections.Add(newIntersection);
            return newIntersection;
        }
    }
}

using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.Vehicles.Mass
{
    /// <summary>
    /// ZoneGraph 注解事件基类。对应 UE5 FZoneGraphAnnotationEventBase。
    /// UE5 由 UZoneGraphAnnotationSubsystem 把事件分发给注解组件，Flax 无该子系统：
    /// 事件由 <see cref="TrafficLightSubsystem"/> 发出，交给
    /// <see cref="TrafficIntersectionAnnotations"/> / <see cref="RoadControlAnnotationsComponent"/> 消费。
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

        /// <summary>进入交叉口的方向（取本侧第一条车道的首点切线）。对应 UE5 DirectionIntoIntersection。</summary>
        public Vector3 DirectionIntoIntersection = Vector3.Zero;

        /// <summary>本侧位置（交叉口入口）。对应 UE5 SideLocation。</summary>
        public Vector3 SideLocation = Vector3.Zero;
    }

    /// <summary>
    /// 交叉口侧句柄。对应 UE5 FTrafficIntersectionSideHandle（TrafficLightIntersectionData.h）。
    /// UE5 用 (ZoneGraphDataHandle, IntersectionIndex, SideIndex) 三元组索引到
    /// <c>UTrafficLightSubsystem::RegisteredLaneData</c>；Flax 侧同一张表在
    /// <see cref="TrafficLightSubsystem.RegisteredLaneData"/>，因此查表逻辑可以直接等价实现。
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

        /// <summary>获取此句柄所在交叉口的侧列表。对应 UE5 GetIntersectionSides。</summary>
        public List<TrafficIntersectionSide> GetIntersectionSides(TrafficLightSubsystem trafficLightSubsystem)
        {
            var intersection = GetIntersection(trafficLightSubsystem);
            return intersection?.IntersectionSides;
        }

        /// <summary>获取可变的交叉口侧列表。对应 UE5 GetMutableIntersectionSides（C# 引用语义下与只读同源）。</summary>
        public List<TrafficIntersectionSide> GetMutableIntersectionSides(TrafficLightSubsystem trafficLightSubsystem)
        {
            return GetIntersectionSides(trafficLightSubsystem);
        }

        /// <summary>获取本侧。对应 UE5 GetIntersectionSide。</summary>
        public TrafficIntersectionSide GetIntersectionSide(TrafficLightSubsystem trafficLightSubsystem)
        {
            var sides = GetIntersectionSides(trafficLightSubsystem);
            if (sides == null || SideIndex < 0 || SideIndex >= sides.Count) return null;
            return sides[SideIndex];
        }

        /// <summary>获取可变的本侧。对应 UE5 GetMutableIntersectionSide。</summary>
        public TrafficIntersectionSide GetMutableIntersectionSide(TrafficLightSubsystem trafficLightSubsystem)
        {
            return GetIntersectionSide(trafficLightSubsystem);
        }

        /// <summary>获取此句柄的交叉口。对应 UE5 GetIntersection。</summary>
        public TrafficLightIntersection GetIntersection(TrafficLightSubsystem trafficLightSubsystem)
        {
            var data = trafficLightSubsystem != null ? trafficLightSubsystem.FindLaneData(ZoneGraphDataHandle) : null;
            if (data == null) return null;
            if (IntersectionIndex < 0 || IntersectionIndex >= data.Intersections.Count) return null;
            return data.Intersections[IntersectionIndex];
        }

        /// <summary>此句柄是否有效（索引能解析到真实侧）。对应 UE5 IsValid。</summary>
        public bool IsValid()
        {
            return IsValid(out _);
        }

        /// <summary>此句柄是否有效并输出对应交叉口。供子系统内部使用。</summary>
        public bool IsValid(out TrafficLightIntersection intersection)
        {
            intersection = null;
            if (ZoneGraphDataHandle.Index < 0 || IntersectionIndex < 0 || SideIndex < 0) return false;
            var data = TrafficLightSubsystem.Instance != null ? TrafficLightSubsystem.Instance.FindLaneData(ZoneGraphDataHandle) : null;
            if (data == null || IntersectionIndex >= data.Intersections.Count) return false;
            intersection = data.Intersections[IntersectionIndex];
            return intersection != null && SideIndex < intersection.IntersectionSides.Count;
        }

        public override string ToString() => $"SideHandle(data={ZoneGraphDataHandle.Index}, intersection={IntersectionIndex}, side={SideIndex})";
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
            Lanes = inLanes ?? new List<ZoneGraphLaneHandle>();
            LanesCoveredMask = inLanesCovered;
            Duration = inDuration;
        }

        /// <summary>此周期内管理的车道。</summary>
        public List<ZoneGraphLaneHandle> Lanes = new List<ZoneGraphLaneHandle>();

        /// <summary>此周期影响的车道类型。</summary>
        public EIntersectionSideRule LanesCoveredMask = EIntersectionSideRule.AllClosed;

        /// <summary>持续秒数，-1 表示不自动推进（UE5 初始化时的全关闭周期即用它）。</summary>
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

        public TrafficPeriodEvent(TrafficPeriod inPeriod, ELaneState inState)
        {
            Period = inPeriod;
            State = inState;
        }

        public TrafficPeriod Period = new TrafficPeriod();
        public ELaneState State = ELaneState.Open;
    }

    /// <summary>
    /// 交通灯交叉口。对应 UE5 FTrafficLightIntersection（TrafficLightIntersectionData.h/.cpp）。
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

        /// <summary>最多 4 侧（UE5 注释：SortSides 后按顺时针排列）。</summary>
        public List<TrafficIntersectionSide> IntersectionSides = new List<TrafficIntersectionSide>();

        public List<TrafficPeriod> TrafficPeriods = new List<TrafficPeriod>();
        public int CurrentPeriodIndex = -1;
        public float RemainingPeriodDuration = -1f;
        public bool bOverrideIntersection = false;

        /// <summary>
        /// 覆盖交叉口状态（过场动画期间锁死/放行）。对应 UE5 <c>FTrafficLightIntersection::SetOverrideIntersection</c>
        /// （TrafficLightIntersectionData.cpp:113-130）。UE5 的 World 参数只用来取 ZoneGraph 注解子系统，
        /// 本端口的周期重算由 <see cref="TrafficLightSubsystem.Tick"/> 自己驱动，因此不需要该参数。
        /// </summary>
        public void SetOverrideIntersection(bool bShouldOverrideIntersection)
        {
            if (bOverrideIntersection == bShouldOverrideIntersection) return;

            bOverrideIntersection = bShouldOverrideIntersection;

            if (!bOverrideIntersection)
            {
                // 解除覆盖时把所有侧的规则复位为全封，交给周期表重新决定通行
                foreach (var side in IntersectionSides)
                {
                    if (side != null) side.SideOverride = EIntersectionSideRule.AllClosed;
                }

                // 让子系统立刻接管周期推进（RemainingPeriodDuration<0 是其重新计时的入口条件）
                RemainingPeriodDuration = -1f;
                CurrentPeriodIndex = -1;
            }
        }

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

        /// <summary>
        /// 调试输出。对应 UE5 DrawDebug（UE5 用 DrawDebugLine/VisualLogger，Flax 无世界调试线绘制 API
        /// 在脚本侧的等价物，故这里输出等价信息到日志）。
        /// </summary>
        public void DrawDebug(ZoneGraphStorage storage)
        {
            MassVehicleLog.Log($"Intersection zone={ZoneIndex}: 侧={IntersectionSides.Count} 周期={TrafficPeriods.Count} " +
                               $"当前周期={CurrentPeriodIndex} 剩余={RemainingPeriodDuration:0.#}s 覆盖={bOverrideIntersection}");
        }

        /// <summary>
        /// 对侧按方向角排序（顺时针）。对应 UE5 SortSides：
        /// UE5 以各侧 DirectionIntoIntersection 与参考方向 (1,0,0) 的带符号夹角升序排列。
        /// Flax 地面在 XZ 平面、上方向为 +Y，故参考方向取 (1,0,0)、法向取 +Y。
        /// </summary>
        public void SortSides()
        {
            if (IntersectionSides == null || IntersectionSides.Count < 2) return;
            IntersectionSides.Sort((a, b) => SignedAngle(a).CompareTo(SignedAngle(b)));
        }

        private static float SignedAngle(TrafficIntersectionSide side)
        {
            Vector3 direction = side?.DirectionIntoIntersection ?? Vector3.Zero;
            direction.Y = 0f;
            if (direction.LengthSquared < 1e-6f) return 0f;
            direction = Vector3.Normalize(direction);

            Vector3 reference = Vector3.Right;
            float dot = Mathf.Clamp(Vector3.Dot(reference, direction), -1f, 1f);
            float crossY = Vector3.Cross(reference, direction).Y;
            float angle = Mathf.Atan2(-crossY, dot);
            if (angle < 0f) angle += (float)(System.Math.PI * 2.0);
            return angle;
        }

        /// <summary>获取当前周期。对应 UE5 GetCurrentPeriod。</summary>
        public TrafficPeriod GetCurrentPeriod()
        {
            if (CurrentPeriodIndex >= 0 && CurrentPeriodIndex < TrafficPeriods.Count)
                return TrafficPeriods[CurrentPeriodIndex];
            return null;
        }

        /// <summary>
        /// 推进到下一个周期并返回。对应 UE5 IncrementCurrentPeriod ——
        /// UE5 在推进时把 RemainingPeriodDuration 重置为该周期的 Duration，此处一致。
        /// </summary>
        public TrafficPeriod IncrementCurrentPeriod()
        {
            if (TrafficPeriods == null || TrafficPeriods.Count == 0) return null;
            CurrentPeriodIndex = (CurrentPeriodIndex + 1) % TrafficPeriods.Count;
            var period = TrafficPeriods[CurrentPeriodIndex];
            RemainingPeriodDuration = period != null ? period.Duration : -1f;
            return period;
        }

        /// <summary>
        /// 交叉口是否为方形。对应 UE5 IsSquareShaped：
        /// 四侧且相对两侧（S 与 S+2）的进入方向大致相反（点积 &lt; -0.7）即视为方形，
        /// 只有方形四向口才启用「对向同时放行」的复杂周期（UE5 TrafficLightSubsystem.cpp:203-206 的同款前提）。
        /// </summary>
        public bool IsSquareShaped()
        {
            if (IntersectionSides == null || IntersectionSides.Count != 4) return false;

            for (int s = 0; s < 4; s++)
            {
                Vector3 a = Flat(IntersectionSides[s]);
                Vector3 b = Flat(IntersectionSides[(s + 2) % 4]);
                if (a.LengthSquared < 1e-6f || b.LengthSquared < 1e-6f) return false;
                if (Vector3.Dot(a, b) > -0.7f) return false;
            }
            return true;
        }

        private static Vector3 Flat(TrafficIntersectionSide side)
        {
            Vector3 direction = side?.DirectionIntoIntersection ?? Vector3.Zero;
            direction.Y = 0f;
            if (direction.LengthSquared < 1e-6f) return Vector3.Zero;
            return Vector3.Normalize(direction);
        }

        /// <summary>
        /// 收集从 startSide 侧出发、最终进入 endSide 侧的交叉口内车道（含右转）。
        /// 对应 UE5 GetSidesConnectingLanes（TrafficLightIntersectionData.cpp）：
        /// UE5 沿 start 侧车道的出向链接进入交叉口，再检查该交叉口车道能否接到 end 侧的车道。
        /// 这里用同一拓扑（storage 的 NextLanes 链接 + 两侧的 Lanes 集合）实现，返回新增数量。
        /// </summary>
        public int GetSidesConnectingLanes(int startSideIndex, int endSideIndex, ZoneGraphStorage zoneGraphStorage, List<ZoneGraphLaneHandle> outTrafficLanes)
        {
            if (zoneGraphStorage == null || outTrafficLanes == null) return 0;
            if (startSideIndex < 0 || startSideIndex >= IntersectionSides.Count) return 0;
            if (endSideIndex < 0 || endSideIndex >= IntersectionSides.Count) return 0;

            var endLanes = new HashSet<ZoneGraphLaneHandle>();
            foreach (var lane in IntersectionSides[endSideIndex].Lanes) endLanes.Add(lane);

            // 交叉口内的进入车道 = 本侧登记的车道；用 end 侧的入向链接反查哪些本侧车道能接到 end 侧
            int added = 0;
            var linked = new List<ZoneGraphLaneHandle>();
            foreach (var startLane in IntersectionSides[startSideIndex].Lanes)
            {
                zoneGraphStorage.GetLinkedLanes(startLane, true, linked);
                foreach (var candidate in linked)
                {
                    if (ReachableWithin(candidate, endLanes, zoneGraphStorage, 2) && !outTrafficLanes.Contains(candidate))
                    {
                        outTrafficLanes.Add(candidate);
                        added++;
                    }
                }
            }
            return added;
        }

        /// <summary>从某车道出发，在 maxHops 步内是否能落到目标集合。</summary>
        private static bool ReachableWithin(ZoneGraphLaneHandle start, HashSet<ZoneGraphLaneHandle> targets, ZoneGraphStorage storage, int maxHops)
        {
            if (targets.Contains(start)) return true;
            if (maxHops <= 0) return false;

            var next = new List<ZoneGraphLaneHandle>();
            storage.GetLinkedLanes(start, true, next);
            foreach (var lane in next)
            {
                if (ReachableWithin(lane, targets, storage, maxHops - 1)) return true;
            }
            return false;
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

        /// <summary>交叉口在列表中的序号。对应 UE5 IndexOfByKey。</summary>
        public int IndexOfIntersection(int intersectionZoneIndex)
        {
            for (int i = 0; i < Intersections.Count; i++)
            {
                if (Intersections[i].ZoneIndex == intersectionZoneIndex) return i;
            }
            return -1;
        }

        /// <summary>按侧序号取侧句柄。对应 UE5 FTrafficIntersectionSideHandle 的构造。</summary>
        public TrafficIntersectionSideHandle MakeSideHandle(int intersectionIndex, int sideIndex)
        {
            return new TrafficIntersectionSideHandle(DataHandle, intersectionIndex, sideIndex);
        }
    }
}

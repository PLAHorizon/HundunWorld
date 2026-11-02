using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.Vehicles.Mass
{
    /// <summary>
    /// 道路控制注解事件。对应 UE5 FRoadControlAnnotationEvent（RoadControlAnnotationsComponent.h）。
    /// 继承 FZoneGraphAnnotationEventBase。
    /// </summary>
    [Serializable]
    public class RoadControlAnnotationEvent : ZoneGraphAnnotationEventBase
    {
        public RoadControlAnnotationEvent() { }

        public RoadControlAnnotationEvent(bool bIsEnabled, List<ZoneGraphLaneHandle> inLanes, ZoneGraphTagMask inTagsToAdd)
        {
            bEnabled = bIsEnabled;
            Lanes = inLanes ?? new List<ZoneGraphLaneHandle>();
            TagsToAdd = inTagsToAdd ?? ZoneGraphTagMask.None;
        }

        /// <summary>是否启用（true 加标签，false 去标签）。</summary>
        public bool bEnabled = true;

        /// <summary>受影响的车道列表。</summary>
        public List<ZoneGraphLaneHandle> Lanes = new List<ZoneGraphLaneHandle>();

        /// <summary>要添加/移除的标签。对应 UE5 TagsToAdd（FZoneGraphTagMask）。</summary>
        public ZoneGraphTagMask TagsToAdd = ZoneGraphTagMask.None;
    }

    /// <summary>
    /// 道路控制注解组件（Flax 侧手写实现）。对应 UE5 URoadControlAnnotationsComponent
    /// （RoadControlAnnotationsComponent.h/.cpp:1-94）。
    /// <para>
    /// UE5 里它是 ZoneGraph 注解组件：<c>TickAnnotation</c> 把 <c>StateChangeEvents</c> 里的
    /// 标签写进受影响车道的注解标签容器（cpp:22-60），并供调试绘制读取（cpp:62-93）。
    /// Flax 无注解子系统，车道标签直接落在 <see cref="TrafficLaneData.Tags"/> 上，
    /// 由本组件在 <see cref="TickAnnotation"/> 中结算；移动/生成两处消费者读的就是同一份标签。
    /// </para>
    /// </summary>
    public class RoadControlAnnotationsComponent : Script
    {
        /// <summary>状态变更事件列表。对应 UE5 StateChangeEvents。</summary>
        [NonSerialized]
        public List<RoadControlAnnotationEvent> StateChangeEvents = new List<RoadControlAnnotationEvent>();

        /// <summary>受影响的车道列表（最近一次结算的并集）。对应 UE5 AffectedLanes。</summary>
        [NonSerialized]
        public List<ZoneGraphLaneHandle> AffectedLanes = new List<ZoneGraphLaneHandle>();

        /// <summary>累计结算的事件数（诊断自证）。</summary>
        public int HandledEventCount { get; private set; }

        /// <summary>入队一条状态变更事件。对应 UE5 蓝图/道路控制器写 StateChangeEvents。</summary>
        public void QueueEvent(RoadControlAnnotationEvent annotationEvent)
        {
            if (annotationEvent == null) return;
            StateChangeEvents.Add(annotationEvent);
        }

        public override void OnUpdate()
        {
            // 对应 UE5 注解组件每帧被 TickAnnotation 调用
            TickAnnotation((float)Time.DeltaTime);
        }

        /// <summary>
        /// 每帧注解更新。对应 UE5 TickAnnotation（cpp:22-60）。
        /// </summary>
        public virtual void TickAnnotation(float deltaTime)
        {
            if (StateChangeEvents.Count == 0) return;
            HandleEvents(StateChangeEvents);
            StateChangeEvents.Clear();
        }

        /// <summary>
        /// 处理事件。对应 UE5 HandleEvents（cpp:30-60）：把 TagsToAdd 按启用态加/撤到受影响车道。
        /// </summary>
        public virtual void HandleEvents(List<RoadControlAnnotationEvent> events)
        {
            if (events == null || events.Count == 0) return;

            var storage = MassVehicleSubsystem.Instance != null ? MassVehicleSubsystem.Instance.Storage : null;
            if (storage == null)
            {
                MassVehicleLog.LogWarning("RoadControlAnnotationsComponent：车道存储不可用，事件未应用。");
                return;
            }

            foreach (var annotationEvent in events)
            {
                if (annotationEvent?.Lanes == null || annotationEvent.TagsToAdd == null) continue;

                foreach (var laneHandle in annotationEvent.Lanes)
                {
                    var lane = storage.GetLane(laneHandle);
                    if (lane == null) continue;

                    foreach (var tag in annotationEvent.TagsToAdd.TagNames)
                    {
                        if (annotationEvent.bEnabled)
                        {
                            if (!lane.HasTag(tag)) lane.AddTag(tag);
                        }
                        else
                        {
                            lane.RemoveTag(tag);
                        }
                    }

                    if (!AffectedLanes.Contains(laneHandle)) AffectedLanes.Add(laneHandle);
                    HandledEventCount++;
                }
            }
        }

        /// <summary>
        /// 调试绘制。对应 UE5 DebugDraw（cpp:62-93，UE5 用 DrawDebug 线框）。
        /// Flax 脚本侧无等价的世界调试绘制接口（已核对 FlaxEngine.CSharp.xml 无 DrawDebug 家族），
        /// 因此这里等价输出受影响车道的标签状态。
        /// </summary>
        public virtual void DebugDraw()
        {
            var storage = MassVehicleSubsystem.Instance != null ? MassVehicleSubsystem.Instance.Storage : null;
            if (storage == null) return;

            foreach (var laneHandle in AffectedLanes)
            {
                var lane = storage.GetLane(laneHandle);
                MassVehicleLog.Log($"RoadControlAnnotation {laneHandle}: 标签=[{string.Join(",", lane?.Tags ?? new List<string>())}]");
            }
        }
    }
}

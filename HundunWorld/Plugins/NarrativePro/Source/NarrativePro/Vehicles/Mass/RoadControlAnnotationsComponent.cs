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
            Lanes = inLanes;
            TagsToAdd = inTagsToAdd;
        }

        /// <summary>是否启用。</summary>
        public bool bEnabled = true;

        /// <summary>受影响的车道列表。</summary>
        public List<ZoneGraphLaneHandle> Lanes = new List<ZoneGraphLaneHandle>();

        /// <summary>要添加的标签。对应 UE5 TagsToAdd（FZoneGraphTagMask）。</summary>
        public ZoneGraphTagMask TagsToAdd = ZoneGraphTagMask.None;
    }

    /// <summary>
    /// 道路控制注解组件。对应 UE5 URoadControlAnnotationsComponent（RoadControlAnnotationsComponent.h）。
    /// 继承 UZoneGraphAnnotationComponent。Flax 无 ZoneGraph，改为 Script 挂载到 Actor 上。
    /// 简化点：
    /// - UZoneGraphAnnotationComponent → Script 占位
    /// - Flax 无 Mass/ZoneGraph，方法实现用占位保留（Flax 不兼容）
    /// </summary>
    public class RoadControlAnnotationsComponent : Script
    {
        /// <summary>状态变更事件列表。对应 UE5 StateChangeEvents。</summary>
        [NonSerialized]
        public List<RoadControlAnnotationEvent> StateChangeEvents = new List<RoadControlAnnotationEvent>();

        /// <summary>受影响的车道列表。对应 UE5 AffectedLanes。</summary>
        [NonSerialized]
        public List<ZoneGraphLaneHandle> AffectedLanes = new List<ZoneGraphLaneHandle>();

        /// <summary>处理事件。对应 UE5 HandleEvents。
        /// Flax-不兼容: UE5 的 Mass/ZoneGraph 在 Flax 无对应物，保留占位。原文 TODO: Flax 无 Mass/ZoneGraph，需自定义实现。</summary>
        /// <param name="events">事件容器（占位）。</param>
        public virtual void HandleEvents(object events)
        {
            // Flax-不兼容: UE5 的 Mass/ZoneGraph 在 Flax 无对应物，保留占位。原文 TODO: Flax 无 Mass/ZoneGraph，需自定义实现
        }

        /// <summary>每帧注解更新。对应 UE5 TickAnnotation。
        /// Flax-不兼容: UE5 的 Mass/ZoneGraph 在 Flax 无对应物，保留占位。原文 TODO: Flax 无 Mass/ZoneGraph，需自定义实现。</summary>
        /// <param name="deltaTime">增量时间。</param>
        /// <param name="annotationTagContainer">注解标签容器（占位）。</param>
        public virtual void TickAnnotation(float deltaTime, object annotationTagContainer)
        {
            // Flax-不兼容: UE5 的 Mass/ZoneGraph 在 Flax 无对应物，保留占位。原文 TODO: Flax 无 Mass/ZoneGraph，需自定义实现
        }

        /// <summary>调试绘制。对应 UE5 DebugDraw（#if UE_ENABLE_DEBUG_DRAWING）。
        /// Flax-不兼容: UE5 的 Mass/ZoneGraph 调试绘制 在 Flax 无对应物，保留占位。原文 TODO: Flax 无 Mass/ZoneGraph 调试绘制，需自定义实现。</summary>
        /// <param name="debugProxy">调试代理（占位）。</param>
        public virtual void DebugDraw(object debugProxy)
        {
            // Flax-不兼容: UE5 的 Mass/ZoneGraph 在 Flax 无对应物，保留占位。原文 TODO: Flax 无 Mass/ZoneGraph，需自定义实现
        }
    }
}

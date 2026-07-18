using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.Vehicles.Mass
{
    /// <summary>
    /// 交叉口注解组件。对应 UE5 UTrafficIntersectionAnnotations（TrafficIntersectionAnnotations.h）。
    /// 继承 UZoneGraphAnnotationComponent。Flax 无 ZoneGraph，改为 Script 挂载到 Actor 上。
    /// 简化点：
    /// - UZoneGraphAnnotationComponent → Script 占位
    /// - Flax 无 Mass/ZoneGraph，方法实现用占位保留（Flax 不兼容）
    /// </summary>
    public class TrafficIntersectionAnnotations : Script
    {
        /// <summary>标记车道关闭的标签。对应 UE5 CloseLaneTag（FZoneGraphTag）。</summary>
        public ZoneGraphTag CloseLaneTag = new ZoneGraphTag();

        /// <summary>周期事件列表。对应 UE5 PeriodEvents（TArray&lt;FTrafficPeriodEvent&gt;）。</summary>
        [NonSerialized]
        public List<TrafficPeriodEvent> PeriodEvents = new List<TrafficPeriodEvent>();

        /// <summary>交通灯子系统。对应 UE5 TrafficLightSubsystem（UTrafficLightSubsystem*）。</summary>
        [NonSerialized]
        public TrafficLightSubsystem TrafficLightSubsystem;

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

        /// <summary>子系统初始化后调用。对应 UE5 PostSubsystemsInitialized。
        /// Flax-不兼容: UE5 的 Mass/ZoneGraph 在 Flax 无对应物，保留占位。原文 TODO: Flax 无 Mass/ZoneGraph，需自定义实现。</summary>
        public virtual void PostSubsystemsInitialized()
        {
            // 缓存 TrafficLightSubsystem
            TrafficLightSubsystem = TrafficLightSubsystem.Instance;
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

using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.Vehicles.Mass
{
    /// <summary>
    /// 交叉口注解组件（Flax 侧手写实现）。对应 UE5 UTrafficIntersectionAnnotations
    /// （TrafficIntersectionAnnotations.h/.cpp:1-117）。
    /// <para>
    /// UE5 里它是 ZoneGraph 注解组件：PostSubsystemsInitialized 缓存
    /// <c>UTrafficLightSubsystem</c>（cpp:16-20），HandleEvents 收到 <c>FTrafficPeriodEvent</c> 后
    /// 把周期内车道的 Closed 标签写入/撤掉（cpp:22-70），TickAnnotation 负责按周期剩余时间推进
    /// 「提前关闭」的车道（cpp:72-95），DebugDraw 画周期状态（cpp:97-117）。
    /// Flax 侧周期推进与标签落地的权威实现放在 <see cref="TrafficLightSubsystem"/>
    /// （没有 Mass 注解子系统可挂钩），本组件作为可选的场景侧观察者：
    /// 订阅同一批事件、记录每口当前周期状态，供任务/UI/调试读取；
    /// 未摆放本组件时交通灯照常工作（不依赖场景资产，符合本作「插件对象全部由代码生成」的前提）。
    /// </para>
    /// </summary>
    public class TrafficIntersectionAnnotations : Script
    {
        /// <summary>标记车道关闭的标签。对应 UE5 CloseLaneTag（FZoneGraphTag）。</summary>
        public ZoneGraphTag CloseLaneTag = new ZoneGraphTag(TrafficLightSettings.DefaultClosedTagName);

        /// <summary>是否只观察本 ZoneIndex 的交叉口；-1 表示全部。对应 UE5 组件的注解区域限定。</summary>
        public int ObservedZoneIndex = -1;

        /// <summary>周期事件列表（本组件收到过的）。对应 UE5 PeriodEvents。</summary>
        [NonSerialized]
        public List<TrafficPeriodEvent> PeriodEvents = new List<TrafficPeriodEvent>();

        /// <summary>交通灯子系统。对应 UE5 TrafficLightSubsystem（PostSubsystemsInitialized 缓存）。</summary>
        [NonSerialized]
        public TrafficLightSubsystem TrafficLightSubsystem;

        /// <summary>收到过的关闭事件数。</summary>
        public int ClosedEventCount { get; private set; }

        /// <summary>收到过的放行事件数。</summary>
        public int OpenedEventCount { get; private set; }

        /// <summary>最近一次收到的周期（供任务/UI 读取）。</summary>
        public TrafficPeriod LastPeriod { get; private set; }

        public override void OnEnable()
        {
            base.OnEnable();
            PostSubsystemsInitialized();
            TrafficLightSubsystem?.RegisterAnnotations(this);
        }

        public override void OnDisable()
        {
            TrafficLightSubsystem?.UnregisterAnnotations(this);
            base.OnDisable();
        }

        /// <summary>子系统初始化后调用。对应 UE5 PostSubsystemsInitialized（cpp:16-20）。</summary>
        public virtual void PostSubsystemsInitialized()
        {
            TrafficLightSubsystem = TrafficLightSubsystem.Instance;
        }

        /// <summary>
        /// 收到交通灯周期事件。对应 UE5 HandleEvents（cpp:22-70）的事件入口。
        /// </summary>
        public void HandlePeriodEvent(TrafficPeriodEvent periodEvent)
        {
            if (periodEvent?.Period == null) return;
            if (ObservedZoneIndex >= 0 && !MatchesObservedZone(periodEvent.Period)) return;

            if (PeriodEvents.Count > 32) PeriodEvents.RemoveAt(0);
            PeriodEvents.Add(periodEvent);
            LastPeriod = periodEvent.Period;

            if (periodEvent.State == ELaneState.Closed) ClosedEventCount++;
            else OpenedEventCount++;
        }

        private bool MatchesObservedZone(TrafficPeriod period)
        {
            var subsystem = TrafficLightSubsystem ?? TrafficLightSubsystem.Instance;
            var storage = MassVehicleSubsystem.Instance != null ? MassVehicleSubsystem.Instance.Storage : null;
            if (subsystem == null || storage == null || period.Lanes == null) return false;

            foreach (var laneHandle in period.Lanes)
            {
                var lane = storage.GetLane(laneHandle);
                if (lane != null && lane.ZoneIndex == ObservedZoneIndex) return true;
            }
            return false;
        }

        /// <summary>
        /// 每帧注解更新。对应 UE5 TickAnnotation（cpp:72-95）。
        /// UE5 在这里推进「提前关闭」的时间；Flax 侧该逻辑的唯一真相在
        /// <see cref="TrafficLightSubsystem.Tick"/>，本方法只做观察者侧的状态汇总（不重复改标签）。
        /// </summary>
        public virtual void TickAnnotation(float deltaTime)
        {
            // 保持句柄有效（子系统可能在切场景后重建）
            TrafficLightSubsystem ??= TrafficLightSubsystem.Instance;
        }

        public override void OnUpdate()
        {
            TickAnnotation((float)Time.DeltaTime);
        }

        /// <summary>调试输出。对应 UE5 DebugDraw（cpp:97-117）。</summary>
        public virtual void DebugDraw()
        {
            MassVehicleLog.Log($"TrafficIntersectionAnnotations: 事件 {PeriodEvents.Count} 条" +
                               $"（关闭 {ClosedEventCount} / 放行 {OpenedEventCount}），" +
                               $"最近周期车道 {(LastPeriod?.Lanes?.Count ?? 0)} 条、时长 {LastPeriod?.Duration ?? 0f:0.#}s");
        }
    }
}

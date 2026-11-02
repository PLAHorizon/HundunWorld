using System;
using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.Vehicles.Mass;

namespace NarrativePro.Vehicles.TrafficLights
{
    /// <summary>
    /// 交通灯。对应 UE5 ATrafficLight（TrafficLight.h/.cpp）。
    /// UE5 中是 Actor + BeginPlay 缓存交叉口侧并注册到子系统；Flax 的 Actor 为 sealed，
    /// 因此改为可挂到任意 Actor 上的 Script（由场景摆放，或由
    /// <see cref="VehicleTrafficSimulation"/> 按 JSON 生成）。
    /// <para>
    /// <see cref="GetPeriod"/> / <see cref="CacheIntersectionSide"/> 已按 TrafficLight.cpp:24-101 与
    /// cpp:103-155 实现：前者读交叉口当前周期与本侧车道/方向掩码，后者按位置查询交叉口侧网格并取最近侧。
    /// </para>
    /// </summary>
    public class TrafficLight : Script
    {
        /// <summary>此交通灯使用的交叉口侧位置（相对本 Actor）。对应 UE5 SideLocation（MakeEditWidget）。</summary>
        public Vector3 SideLocation = Vector3.Zero;

        /// <summary>查询 SideLocation 周围交叉口侧的范围。对应 UE5 QueryExtent。
        /// 设置过小可能产生假阳性。</summary>
        public Vector3 QueryExtent = new Vector3(3000f);

        /// <summary>方向规则：本灯默认放行「直行」。对应 UE5 调用方传入的 Rule。</summary>
        public EIntersectionSideRule DefaultRule = EIntersectionSideRule.StraightOpen;

        /// <summary>缓存的交叉口侧句柄。对应 UE5 CachedIntersectionSide。</summary>
        [NonSerialized]
        public TrafficIntersectionSideHandle CachedIntersectionSide = new TrafficIntersectionSideHandle();

        /// <summary>最近一次查询到的剩余周期（秒）。0 表示本灯方向不放行（红）。</summary>
        public float LastRemainingPeriod { get; private set; }

        /// <summary>上一次记录的通行态（用于 OnPeriodUpdated 的去重日志）。</summary>
        private bool bLastGreen;

        public override void OnEnable()
        {
            base.OnEnable();
            // 对应 UE5 BeginPlay：缓存交叉口侧并注册到子系统
            CacheIntersectionSide();
            TrafficLightSubsystem.Instance?.RegisterTrafficLight(this);
        }

        public override void OnDisable()
        {
            TrafficLightSubsystem.Instance?.UnregisterTrafficLight(this);
            base.OnDisable();
        }

        /// <summary>获取此交通灯的当前周期。对应 UE5 GetPeriod（TrafficLight.cpp:103-155）。
        /// 值为 0 表示当前周期不涉及此交通灯（可视为红灯），或缓存的侧无效。</summary>
        /// <param name="rule">方向规则位掩码。</param>
        /// <returns>剩余周期时间（秒）。</returns>
        public virtual float GetPeriod(EIntersectionSideRule rule)
        {
            var subsystem = TrafficLightSubsystem.Instance;
            if (subsystem == null || !CachedIntersectionSide.IsValid())
            {
                return 0f;
            }

            var intersection = CachedIntersectionSide.GetIntersection(subsystem);
            var side = CachedIntersectionSide.GetIntersectionSide(subsystem);
            if (intersection == null || side == null)
            {
                return 0f;
            }

            // cpp:118-130 —— 交叉口被覆盖（任务/过场）时只看本侧的覆盖值
            if (intersection.bOverrideIntersection)
            {
                float overridden = HasAnyFlag(side.SideOverride, rule) ? float.MaxValue : 0f;
                LastRemainingPeriod = overridden;
                return overridden;
            }

            var currentPeriod = intersection.GetCurrentPeriod();
            if (currentPeriod == null)
            {
                // cpp:132-137 —— 开局尚无周期时不报错，直接按红处理
                return 0f;
            }

            // cpp:139-152 —— 当前周期含本侧车道且方向掩码放行本灯 → 返回剩余时长
            bool bValidPeriod = false;
            if (currentPeriod.Lanes != null)
            {
                foreach (var lane in currentPeriod.Lanes)
                {
                    if (!side.Lanes.Contains(lane)) continue;
                    if (!HasAnyFlag(currentPeriod.LanesCoveredMask, rule)) continue;
                    bValidPeriod = true;
                    break;
                }
            }

            float remaining = bValidPeriod ? Mathf.Max(0f, intersection.RemainingPeriodDuration) : 0f;
            LastRemainingPeriod = remaining;
            return remaining;
        }

        private static bool HasAnyFlag(EIntersectionSideRule value, EIntersectionSideRule flags)
        {
            return ((byte)value & (byte)flags) != 0;
        }

        /// <summary>本灯当前是否放行 <see cref="DefaultRule"/>。供载具与调试读取。</summary>
        public bool IsGreen()
        {
            return GetPeriod(DefaultRule) > 0f;
        }

        /// <summary>缓存 SideLocation 处的交叉口侧。对应 UE5 CacheIntersectionSide（TrafficLight.cpp:24-101）。</summary>
        public virtual void CacheIntersectionSide()
        {
            var subsystem = TrafficLightSubsystem.Instance;
            if (subsystem == null || Actor == null) return;

            Vector3 worldSideLocation = Actor.Transform.Translation + SideLocation;
            var sides = subsystem.IntersectionSidesGrid.Query(worldSideLocation, QueryExtent);
            if (sides == null || sides.Count == 0)
            {
                NarrativeLog.LogWarning($"[TrafficLight] {Actor.Name}：在 {worldSideLocation} 周围未找到交叉口侧，" +
                                        "请确认车道带 Intersection 标签且交通灯位置靠近交叉口入口。");
                return;
            }

            // UE5 用 DistSquared2D 排序取最近；Query 已按同一度量升序返回，故取第一个
            CachedIntersectionSide = sides[0];
            var side = CachedIntersectionSide.GetIntersectionSide(subsystem);
            MassVehicleLog.Log($"[TrafficLight] {Actor.Name} 绑定 {CachedIntersectionSide}" +
                               $"（侧位置 {side?.SideLocation}，候选 {sides.Count} 个）");
        }

        /// <summary>缓存的交叉口获得新周期时调用。对应 UE5 OnPeriodUpdated（BlueprintNativeEvent）。</summary>
        public virtual void OnPeriodUpdated()
        {
            bool bGreen = IsGreen();
            if (bGreen != bLastGreen)
            {
                bLastGreen = bGreen;
                MassVehicleLog.Log($"[TrafficLight] {Actor?.Name} → {(bGreen ? "通行" : "停止")}，剩余 {LastRemainingPeriod:0.#}s");
            }
        }

        /// <summary>外部按指定规则查询时的便捷入口（对应 UE5 蓝图节点 GetPeriod(Rule)）。</summary>
        public float GetPeriod() => GetPeriod(DefaultRule);
    }
}

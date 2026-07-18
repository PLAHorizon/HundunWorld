using System;
using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.Vehicles.Mass;

namespace NarrativePro.Vehicles.TrafficLights
{
    /// <summary>
    /// 交通灯。对应 UE5 ATrafficLight（TrafficLight.h）。
    /// 继承 AActor。Flax 中 Actor 为 sealed，改为 Script 挂载到 Actor 上。
    /// 简化点：
    /// - AActor → Script
    /// - 依赖 TrafficLightSubsystem（Mass 占位），周期查询实现简化为占位保留（Flax 不兼容）
    /// - FGameplayTag/FVector → ZoneGraphTag/Vector3
    /// - BlueprintNativeEvent（OnPeriodUpdated）合并为 virtual 方法
    /// </summary>
    public class TrafficLight : Script
    {
        /// <summary>此交通灯使用的交叉口侧位置。对应 UE5 SideLocation（MakeEditWidget）。</summary>
        public Vector3 SideLocation = Vector3.Zero;

        /// <summary>查询 SideLocation 周围交叉口侧的范围。对应 UE5 QueryExtent。
        /// 设置过小可能产生假阳性。</summary>
        public Vector3 QueryExtent = new Vector3(3000f);

        /// <summary>缓存的交叉口侧句柄。对应 UE5 CachedIntersectionSide。</summary>
        [NonSerialized]
        public TrafficIntersectionSideHandle CachedIntersectionSide = new TrafficIntersectionSideHandle();

        public override void OnEnable()
        {
            base.OnEnable();
            // 对应 UE5 BeginPlay：缓存交叉口侧并注册到子系统
            CacheIntersectionSide();
            TrafficLightSubsystem.Instance?.RegisterTrafficLight(this);
        }

        /// <summary>获取此交通灯的当前周期。对应 UE5 GetPeriod。
        /// 值为 0 表示当前周期不涉及此交通灯（可视为红灯），或缓存的侧无效。</summary>
        /// <param name="rule">方向规则位掩码。</param>
        /// <returns>剩余周期时间（秒）。</returns>
        public virtual float GetPeriod(EIntersectionSideRule rule)
        {
            // Flax-不兼容: UE5 的 Mass/ZoneGraph 在 Flax 无对应物，保留占位。原文 TODO: Flax 无 Mass/ZoneGraph，需自定义实现
            return 0f;
        }

        /// <summary>缓存 SideLocation 处的交叉口侧。对应 UE5 CacheIntersectionSide。
        /// 应在 GetPeriod 之前调用。</summary>
        public virtual void CacheIntersectionSide()
        {
            // Flax-不兼容: UE5 的 Mass/ZoneGraph 在 Flax 无对应物，保留占位。原文 TODO: Flax 无 Mass/ZoneGraph，需自定义实现
        }

        /// <summary>缓存的交叉口获得新周期时调用。对应 UE5 OnPeriodUpdated（BlueprintNativeEvent）。</summary>
        public virtual void OnPeriodUpdated()
        {
            // 子类可重写
        }
    }
}

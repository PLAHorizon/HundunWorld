using FlaxEngine;
using NarrativePro.GAS;

namespace NarrativePro.Vehicles
{
    /// <summary>
    /// 撞击接口。对应 UE5 INarrativeImpactInterface（NarrativeImpactInterface.h）。
    /// Actor 可实现此接口以响应载具撞击或爆炸冲击。
    /// 简化点：
    /// - 移除 UE5 UInterface（C# 无此概念），仅保留 C# interface
    /// - UPrimitiveComponent → FlaxEngine.Collider
    /// - FHitResult → FlaxEngine.RayCastHit
    /// - AActor → FlaxEngine.Actor
    /// - BlueprintNativeEvent 的 _Implementation 模式合并为单一接口方法
    /// </summary>
    public interface INarrativeImpactInterface
    {
        /// <summary>
        /// 处理载具撞击此 Actor 时的事件。对应 UE5 HandleVehicleImpact_Implementation。
        /// </summary>
        /// <param name="vehicle">撞击的载具。</param>
        /// <param name="overlappedComponent">被重叠的组件。</param>
        /// <param name="otherComp">另一个碰撞组件。</param>
        /// <param name="otherBodyIndex">另一个物体的骨骼索引。</param>
        /// <param name="bFromSweep">是否来自扫描。</param>
        /// <param name="sweepResult">扫描结果。</param>
        void HandleVehicleImpact(NarrativeVehicleBase vehicle, Collider overlappedComponent, Collider otherComp, int otherBodyIndex, bool bFromSweep, RayCastHit sweepResult);

        /// <summary>
        /// 处理爆炸冲击此 Actor 时的事件。对应 UE5 HandleExplosionImpact_Implementation。
        /// </summary>
        /// <param name="explosionCauser">爆炸造成者的 ASC。</param>
        /// <param name="explosionLocation">爆炸位置。</param>
        /// <param name="intendedDamage">预期伤害。</param>
        void HandleExplosionImpact(NarrativeAbilitySystemComponent explosionCauser, Vector3 explosionLocation, float intendedDamage);
    }
}

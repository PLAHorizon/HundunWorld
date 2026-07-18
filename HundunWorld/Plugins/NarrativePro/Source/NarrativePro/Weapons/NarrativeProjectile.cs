using System;
using FlaxEngine;

namespace NarrativePro.Weapons
{
    /// <summary>
    /// Narrative 投射物。移植自 UE5 NarrativeArsenal: Weapons/NarrativeProjectile.h（ANarrativeProjectile : AActor）。
    ///
    /// 简化点：
    /// - AActor → Flax Script 挂载到投射物 Actor。
    /// - 移除 UE5 复制/RPC，改为本地逻辑 + 事件回调。
    /// - FProjectileTargetDataDelegate（参数 FGameplayAbilityTargetDataHandle）→ event Action&lt;object&gt;，
    ///   其中 object 为 GameplayAbilityTargetDataHandle 占位（GAS 数据句柄）。
    /// - SetProjectileTargetData 的目标数据句柄同样用 object 占位。
    /// TODO [需接入 GAS 系统]: 接入 GAS 后用实际的 GameplayAbilityTargetDataHandle 类型替换 object。
    /// </summary>
    public class NarrativeProjectile : Script
    {
        /// <summary>
        /// 投射物目标数据委托（对应 UE5 OnProjectileTargetData）。
        /// 参数为 GameplayAbilityTargetDataHandle 占位（object）。
        /// </summary>
        public event Action<object> OnProjectileTargetData;

        /// <summary>
        /// 设置投射物目标数据并广播委托。
        /// 对应 UE5 SetProjectileTargetData(FGameplayAbilityTargetDataHandle TargetHandle)。
        /// </summary>
        /// <param name="targetHandle">目标数据句柄（GAS 占位）</param>
        public void SetProjectileTargetData(object targetHandle)
        {
            // TODO [需接入 GAS 系统]: 接入 GAS 后用实际 GameplayAbilityTargetDataHandle 类型替换 object
            OnProjectileTargetData?.Invoke(targetHandle);
        }
    }
}

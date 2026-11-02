using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.GAS;

namespace NarrativePro.Weapons
{
    /// <summary>
    /// Narrative 投射物。移植自 UE5 NarrativeArsenal: Weapons/NarrativeProjectile.h:11,26,32
    /// （ANarrativeProjectile : AActor），实现见 Private/Weapons/NarrativeProjectile.cpp:28-32。
    ///
    /// 简化点：
    /// - AActor → Flax Script 挂载到投射物 Actor。
    /// - 移除 UE5 复制/RPC，改为本地逻辑 + 事件回调。
    /// - FProjectileTargetDataDelegate（参数 const FGameplayAbilityTargetDataHandle&，
    ///   对应 UE5 NarrativeProjectile.h:11）→ event Action&lt;List&lt;CombatHitResult&gt;&gt;。
    ///   与本 GAS 移植的既有口径一致（见 GAS/NarrativeCombatAbility.cs 头注：
    ///   "移除 UE5 FGameplayAbilityTargetDataHandle，改为 CombatHitResult 列表"）。
    /// - SetProjectileTargetData 直接广播目标数据（对应 UE5 cpp:28-32 的 Broadcast）。
    /// </summary>
    public class NarrativeProjectile : Script
    {
        /// <summary>
        /// 投射物目标数据委托（对应 UE5 OnProjectileTargetData，NarrativeProjectile.h:26）。
        /// 参数为命中结果列表，替代 UE5 的 FGameplayAbilityTargetDataHandle。
        /// </summary>
        public event Action<List<CombatHitResult>> OnProjectileTargetData;

        /// <summary>
        /// 设置投射物目标数据并广播委托。
        /// 对应 UE5 SetProjectileTargetData(const FGameplayAbilityTargetDataHandle& TargetHandle)
        /// （NarrativeProjectile.h:32，实现见 cpp:28-32：仅转发广播，UE5 中为 BlueprintCallable）。
        /// </summary>
        /// <param name="targetData">目标数据（命中结果列表，GAS 移植口径）</param>
        public void SetProjectileTargetData(List<CombatHitResult> targetData)
        {
            OnProjectileTargetData?.Invoke(targetData);
        }
    }
}

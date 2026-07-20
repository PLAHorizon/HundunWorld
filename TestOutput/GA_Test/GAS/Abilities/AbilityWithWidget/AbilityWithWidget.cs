// 此文件由 UE5ToFlaxConverter 自动生成。
// 源资源: Class'/Script/Engine.Blueprint'
// 生成时间: 2026-07-20 06:29:28 UTC

using System;
using System.Collections.Generic;
using NarrativePro.GAS;
using NarrativePro.Items; // GameplayTag/GameplayTagContainer

namespace NarrativePro.GAS.Abilities
{
    /// <summary>
    /// UE5 转换的 GameplayAbility：AbilityWithWidget
    /// </summary>
    public class AbilityWithWidget : NarrativeGameplayAbility
    {
        public const string AbilityInputId = "0";

        public AbilityWithWidget()
        {
            InstancingPolicy = AbilityInstancingPolicy.InstancedPerActor;
            NetExecutionPolicy = AbilityNetExecutionPolicy.LocalPredicted;
        }

        public override void ActivateAbility(AbilitySystemComponent asc)
        {
            base.ActivateAbility(asc);
            // TODO: 从 UE5 蓝图逻辑迁移技能执行代码
        }

        public override void EndAbility(AbilitySystemComponent asc)
        {
            base.EndAbility(asc);
        }
    }
}

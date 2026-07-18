using System;
using System.Collections.Generic;

namespace NarrativePro.GAS
{
    /// <summary>
    /// 能力配置资产。对应 UE5 UAbilityConfiguration。
    /// 包含默认属性、启动效果、默认能力列表。
    /// 简化点：
    /// - TSubclassOf&lt;UGameplayEffect&gt; 替换为字符串路径
    /// - TSubclassOf&lt;UNarrativeGameplayAbility&gt; 替换为字符串路径
    /// </summary>
    [Serializable]
    public class AbilityConfiguration
    {
        /// <summary>默认属性初始化效果路径（Instant 类型，设置 BaseValue）。</summary>
        public string DefaultAttributesEffectPath = "";

        /// <summary>启动时一次性应用的效果路径列表。</summary>
        public List<string> StartupEffectPaths = new List<string>();

        /// <summary>默认授予的能力路径列表。</summary>
        public List<string> DefaultAbilityPaths = new List<string>();

        public AbilityConfiguration() { }
    }
}

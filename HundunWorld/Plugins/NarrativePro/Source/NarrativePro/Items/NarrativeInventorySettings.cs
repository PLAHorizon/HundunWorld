using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.Items
{
    /// <summary>
    /// Narrative 背包插件的配置设置。适配 UE5 UNarrativeInventorySettings。
    /// UE5 中为 config=Engine, defaultconfig 的 UObject，Flax 中以 [Serializable] 类 + 静态 Instance 单例实现。
    /// </summary>
    [System.Serializable]
    public class NarrativeInventorySettings
    {
        /// <summary>是否允许同一物品存在多个堆叠？</summary>
        public bool bAllowMultipleStacks { get; set; } = false;

        /// <summary>单例实例。</summary>
        public static NarrativeInventorySettings Instance { get; set; } = LoadDefault();

        public NarrativeInventorySettings()
        {
            // 默认构造
        }

        private static NarrativeInventorySettings LoadDefault()
        {
            // TODO [需接入统一配置加载机制]: 从 Flax 引擎配置或 JSON 文件加载。暂时返回默认实例。
            var settings = new NarrativeInventorySettings();
            NarrativeLog.Log("NarrativeInventorySettings 已使用默认值初始化。");
            return settings;
        }
    }
}

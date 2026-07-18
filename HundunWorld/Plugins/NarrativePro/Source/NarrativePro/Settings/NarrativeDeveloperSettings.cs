using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.Items;
using NarrativePro.AI;
using NarrativePro.Character;

namespace NarrativePro.Settings
{
    /// <summary>
    /// 本地开发者设置（按用户隔离）。对应 UE5 UNarrativeDeveloperSettings。
    /// UE5 中使用 config=EditorPerProjectUserSettings，包含开发者本地调试用的小调整，
    /// 不会传播给其他用户。Flax 中以 [Serializable] 类 + 静态 Instance 单例实现。
    /// </summary>
    [Serializable]
    public class NarrativeDeveloperSettings
    {
        /// <summary>
        /// NPC 允许生成名单。仅此列表中的 NPC 会被生成，便于调试时筛选。
        /// 对应 UE5 NPCAllowList（TArray&lt;UNPCDefinition*&gt;）。
        /// </summary>
        public List<NPCDefinition> NPCAllowList = new List<NPCDefinition>();

        /// <summary>
        /// 玩家定义覆盖列表。使用此列表中的定义替代 GameMode 指定的玩家定义。
        /// 对应 UE5 PlayerDefinitionOverrides（TArray&lt;UPlayerDefinition*&gt;）。
        /// </summary>
        public List<PlayerDefinition> PlayerDefinitionOverrides = new List<PlayerDefinition>();

        /// <summary>单例实例。</summary>
        public static NarrativeDeveloperSettings Instance { get; set; } = LoadDefault();

        private static NarrativeDeveloperSettings LoadDefault()
        {
            // TODO [需接入设置加载系统]: 从 Flax 编辑器用户配置或 JSON 文件加载持久化设置。暂时返回默认实例。
            var settings = new NarrativeDeveloperSettings();
            NarrativeLog.Log("NarrativeDeveloperSettings 已使用默认值初始化。");
            return settings;
        }
    }
}

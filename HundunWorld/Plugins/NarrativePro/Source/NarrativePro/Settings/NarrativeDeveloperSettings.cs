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
            // Flax-不兼容（已核实，保留构造默认值）：UE5 中此类是 EditorPerProjectUserSettings——
            // 编辑器本地调试过滤器，两个列表持有的是编辑器内 NPCDefinition/PlayerDefinition 资产对象引用。
            // 核实过 Flax 脚本层无按用户隔离的设置存储 API；且 System.Text.Json 反序列化只会得到
            // 脱离资产体系的新实例，Spawner 侧按引用查找名单成员会静默失效，因此不能像
            // NarrativeCombatDeveloperSettings 那样走 JSON 持久化。运行时调试请直接覆盖 Instance。
            var settings = new NarrativeDeveloperSettings();
            NarrativeLog.Log("NarrativeDeveloperSettings 已使用默认值初始化（资产引用列表仅可由运行时代码覆盖）。");
            return settings;
        }
    }
}

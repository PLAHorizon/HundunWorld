using System;
using System.Collections.Generic;
using NarrativePro.Character;
using NarrativePro.Items;

namespace NarrativePro.Character
{
    /// <summary>
    /// 角色定义。对应 UE5 UCharacterDefinition。
    /// 数据资产，包含角色默认外观、初始货币/物品、归属标签、阵营、触发器集、攻击优先级、能力配置。
    /// </summary>
    [Serializable]
    public class CharacterDefinition
    {
        /// <summary>角色默认外观。</summary>
        public CharacterAppearance DefaultAppearance;

        /// <summary>初始货币。</summary>
        public int DefaultCurrency = 0;

        /// <summary>初始物品掉落表（按 LootTableRoll 列表）。</summary>
        public List<LootTableRoll> DefaultItemLoadout = new List<LootTableRoll>();

        /// <summary>默认拥有标签（Narrative.State）。</summary>
        public GameplayTagContainer DefaultOwnedTags = new GameplayTagContainer();

        /// <summary>默认阵营标签（Narrative.Factions）。</summary>
        public GameplayTagContainer DefaultFactions = new GameplayTagContainer();

        /// <summary>触发器集路径列表（运行时按路径加载 TriggerSet 资源）。</summary>
        public List<string> TriggerSetPaths = new List<string>();

        /// <summary>攻击优先级（AI EQS 用，越大越受关注）。</summary>
        public float AttackPriority = 1f;

        /// <summary>能力配置路径（GAS Phase 7 实现，字符串占位）。</summary>
        // TODO [需接入 GAS 系统]: GAS Phase 7 实现后填充能力配置路径加载逻辑
        public string AbilityConfigurationPath = "";
    }
}

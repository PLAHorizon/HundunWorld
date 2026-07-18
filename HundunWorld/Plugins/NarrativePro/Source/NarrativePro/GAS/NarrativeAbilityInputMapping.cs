using System;
using System.Collections.Generic;
using NarrativePro.Items;

namespace NarrativePro.GAS
{
    /// <summary>
    /// 能力输入映射数据。对应 UE5 FAbilityInputMappingData。
    /// 描述一个输入标签到能力路径的映射。
    /// </summary>
    [Serializable]
    public class AbilityInputMappingData
    {
        /// <summary>输入标签（如 "Input.Attack"、"Input.AltAttack"）。</summary>
        public GameplayTag InputTag = GameplayTag.None;

        /// <summary>绑定的能力路径。</summary>
        public string AbilityPath = "";

        public AbilityInputMappingData() { }

        public AbilityInputMappingData(GameplayTag inputTag, string abilityPath)
        {
            InputTag = inputTag;
            AbilityPath = abilityPath;
        }
    }

    /// <summary>
    /// 能力输入映射资产。对应 UE5 UNarrativeAbilityInputMapping。
    /// Narrative 武器通过此资产定义哪些输入动作映射到哪些能力。
    /// 简化点：UE5 UInputAction 用字符串路径占位。
    /// </summary>
    [Serializable]
    public class NarrativeAbilityInputMapping
    {
        /// <summary>输入到能力的映射列表。</summary>
        public List<AbilityInputMappingData> InputAbilities = new List<AbilityInputMappingData>();

        public NarrativeAbilityInputMapping() { }

        /// <summary>根据输入标签查找能力路径。</summary>
        public string FindAbilityPath(GameplayTag inputTag)
        {
            if (InputAbilities == null) return "";
            foreach (var mapping in InputAbilities)
            {
                if (mapping?.InputTag == inputTag) return mapping.AbilityPath;
            }
            return "";
        }

        /// <summary>根据能力路径查找输入标签。</summary>
        public GameplayTag FindInputTag(string abilityPath)
        {
            if (InputAbilities == null || string.IsNullOrEmpty(abilityPath)) return GameplayTag.None;
            foreach (var mapping in InputAbilities)
            {
                if (mapping?.AbilityPath == abilityPath) return mapping.InputTag;
            }
            return GameplayTag.None;
        }

        /// <summary>添加或更新映射。</summary>
        public void AddOrUpdateMapping(GameplayTag inputTag, string abilityPath)
        {
            if (InputAbilities == null) InputAbilities = new List<AbilityInputMappingData>();
            for (int i = 0; i < InputAbilities.Count; i++)
            {
                if (InputAbilities[i].InputTag == inputTag)
                {
                    InputAbilities[i].AbilityPath = abilityPath;
                    return;
                }
            }
            InputAbilities.Add(new AbilityInputMappingData(inputTag, abilityPath));
        }
    }
}

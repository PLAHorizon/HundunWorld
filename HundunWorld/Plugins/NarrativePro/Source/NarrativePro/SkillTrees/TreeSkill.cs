using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.Items;

namespace NarrativePro.SkillTrees
{
    /// <summary>
    /// 技能树中单个 Perk 的配置。对应 UE5 FPerkConfig（USTRUCT）。
    /// 描述一个 Perk 在技能树 UI 中的位置及其子节点链接。
    /// </summary>
    [Serializable]
    public class FPerkConfig
    {
        /// <summary>
        /// 该 Perk 的类路径标识。对应 UE5 TSubclassOf&lt;UTreePerk&gt;。
        /// </summary>
        public string Perk = "";

        /// <summary>Perk 在技能树 UI 中显示的位置。对应 UE5 FVector2D。</summary>
        public Vector2 PerkCords = Vector2.Zero;

        /// <summary>
        /// 该 Perk 应链接到的子 Perk 类路径列表。对应 UE5 TArray&lt;TSubclassOf&lt;UTreePerk&gt;&gt;。
        /// </summary>
        public List<string> LinkedTo = new List<string>();

        public FPerkConfig()
        {
            PerkCords = Vector2.Zero;
        }
    }

    /// <summary>
    /// 技能树中的一个技能（如战斗、潜行等）。对应 UE5 UTreeSkill（UObject, Blueprintable, EditInlineNew）。
    /// 每个游戏通过子类化本类实现自己的技能。Flax 中以 [Serializable] 普通类承载。
    /// </summary>
    [Serializable]
    public class TreeSkill
    {
        /// <summary>
        /// 该技能的类路径标识。对应 UE5 TSubclassOf&lt;UTreeSkill&gt;。
        /// 用于存档恢复时在 <see cref="SkillTreeComponent.SkillTreeSkills"/> 中检索对应技能实例。
        /// </summary>
        public string SkillClassPath = "";

        /// <summary>该技能包含的所有 Perk 配置。</summary>
        public List<FPerkConfig> Perks = new List<FPerkConfig>();

        /// <summary>技能的显示名称。对应 UE5 FText。</summary>
        public string SkillDisplayName = "";

        /// <summary>技能的描述文本。对应 UE5 FText。</summary>
        public string SkillDescription = "";

        /// <summary>该技能当前的等级。</summary>
        public int SkillLevel = 1;
    }
}

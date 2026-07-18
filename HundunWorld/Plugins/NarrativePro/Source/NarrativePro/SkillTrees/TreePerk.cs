using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.Items;

namespace NarrativePro.SkillTrees
{
    /// <summary>
    /// 技能树中的 Perk（天赋节点）。对应 UE5 UTreePerk（UObject, Blueprintable, EditInlineNew）。
    /// 由 <see cref="TreeSkill"/> 持有，并指向树中后续的 Perk。
    /// UE5 中为可被蓝图子类化的 UObject；Flax 中以 [Serializable] 普通类承载，子类可重写虚方法。
    /// 资产引用（贴图、视频）以字符串路径占位，对应 UE5 TSoftObjectPtr/UMediaSource*。
    /// </summary>
    [Serializable]
    public class TreePerk
    {
        /// <summary>
        /// 该 Perk 的类路径标识。对应 UE5 TSubclassOf&lt;UTreePerk&gt;。
        /// 用于在 <see cref="SkillTreeComponent"/> 中按路径检索 Perk 实例。
        /// </summary>
        public string PerkClassPath = "";

        /// <summary>当前 Perk 等级。-1 表示尚未购买。</summary>
        public int PerkLevel = -1;

        /// <summary>Perk 允许的最高等级。</summary>
        public int MaxLevels = 1;

        /// <summary>紧随其后的 Perk 实例列表（Instanced，编辑器内联编辑）。</summary>
        public List<TreePerk> LinkedPerks = new List<TreePerk>();

        /// <summary>
        /// 该 Perk 需要链接到的 Perk 类路径列表。对应 UE5 TArray&lt;TSubclassOf&lt;UTreePerk&gt;&gt;。
        /// 即必须先购买本 Perk 才能购买这些链接的 Perk。
        /// </summary>
        public List<string> LinkedPerkClasses = new List<string>();

        /// <summary>Perk 的显示名称。对应 UE5 FText。</summary>
        public string PerkDisplayName = "";

        /// <summary>Perk 的显示图标（资产路径占位）。对应 UE5 TSoftObjectPtr&lt;UTexture2D&gt;。</summary>
        public string PerkDisplayIcon = "";

        /// <summary>Perk 的描述文本。对应 UE5 FText。</summary>
        public string PerkDescription = "";

        /// <summary>Perk 的预览视频（资产路径占位）。对应 UE5 UMediaSource*。</summary>
        public string PerkVideo = "";

        /// <summary>拥有该 Perk 的技能树组件（由 SkillTreeComponent 在注册时设置）。</summary>
        public SkillTreeComponent OwningComponent { get; set; }

        /// <summary>
        /// 设置 Perk 等级并应用对应功能。对应 UE5 BlueprintNativeEvent SetPerkLevel。
        /// 子类应重写以实现对玩家的实际效果（如属性修改）。
        /// </summary>
        public virtual void SetPerkLevel(int newPerkLevel)
        {
            PerkLevel = newPerkLevel;
        }

        /// <summary>
        /// 获取 Perk 的描述。对应 UE5 BlueprintNativeEvent GetPerkDescription。
        /// 默认返回 <see cref="PerkDescription"/>；子类可重写以动态生成描述。
        /// </summary>
        public virtual string GetPerkDescription()
        {
            return PerkDescription;
        }

        /// <summary>获取拥有该 Perk 的技能树组件。对应 UE5 GetOwningComponent。</summary>
        public SkillTreeComponent GetOwningComponent() => OwningComponent;
    }
}

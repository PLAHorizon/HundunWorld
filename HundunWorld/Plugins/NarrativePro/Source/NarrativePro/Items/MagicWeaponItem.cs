using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.Items
{
    /// <summary>
    /// 魔法武器（法杖/魔杖）。适配 UE5 UMagicWeaponItem。
    /// 提供魔法攻击连击动画集，由授予的攻击能力使用。
    /// </summary>
    public class MagicWeaponItem : WeaponItem
    {
        /// <summary>法术投射物资源路径</summary>
        public string SpellProjectilePath { get; set; } = "";

        /// <summary>法术效果 ID（对应 GAS GameplayEffect）</summary>
        public string SpellEffectId { get; set; } = "";

        /// <summary>魔法消耗（MP）</summary>
        public float ManaCost { get; set; } = 0f;

        /// <summary>
        /// 魔法攻击连击动画集注册键列表（对应 UE5 TArray&lt;TObjectPtr&lt;UNarrativeAnimSet&gt;&gt;；
        /// 每项经 <see cref="NarrativePro.GAS.NarrativeCombatAnimSets.ResolveAnimSet"/> 解析成动画集，
        /// 由攻击能力的近战/动画通知窗口消费）。
        /// </summary>
        public List<string> AttackCombos { get; set; } = new List<string>();

        /// <summary>魔法重击连击动画集注册键列表</summary>
        public List<string> HeavyAttackCombos { get; set; } = new List<string>();

        /// <summary>
        /// 返回对应类型的连击动画集（UE5 <c>GetComboAnims</c> 返回 AnimSet 数组口径）。
        /// 键解析结果为空时回退武器基类（<see cref="WeaponItem.ComboAnimSetId"/> 指向的连击集）。
        /// </summary>
        /// <param name="bHeavyAttack">是否为重击</param>
        public override List<NarrativePro.GAS.NarrativeAnimSet> GetComboAnims(bool bHeavyAttack)
        {
            var keys = bHeavyAttack ? HeavyAttackCombos : AttackCombos;
            var result = new List<NarrativePro.GAS.NarrativeAnimSet>();
            if (keys != null)
            {
                foreach (var key in keys)
                {
                    var set = NarrativePro.GAS.NarrativeCombatAnimSets.ResolveAnimSet(key);
                    if (set != null) result.Add(set);
                }
            }
            if (result.Count > 0) return result;
            return base.GetComboAnims(bHeavyAttack);
        }
    }
}

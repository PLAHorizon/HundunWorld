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

        /// <summary>魔法攻击连击动画集资源路径列表（对应 UE5 TArray&lt;TObjectPtr&lt;UNarrativeAnimSet&gt;&gt;）</summary>
        public List<string> AttackCombos { get; set; } = new List<string>();

        /// <summary>魔法重击连击动画集资源路径列表</summary>
        public List<string> HeavyAttackCombos { get; set; } = new List<string>();

        /// <summary>返回对应类型的连击动画集路径列表。</summary>
        /// <param name="bHeavyAttack">是否为重击</param>
        public override List<string> GetComboAnims(bool bHeavyAttack)
        {
            return bHeavyAttack ? HeavyAttackCombos : AttackCombos;
        }
    }
}

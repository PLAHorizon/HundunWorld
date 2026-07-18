using FlaxEngine;

namespace NarrativePro.GAS
{
    /// <summary>
    /// 执行计算基类。对应 UE5 UGameplayEffectExecutionCalculation。
    /// 用于在效果应用时执行自定义计算逻辑（如伤害公式、治疗公式）。
    /// 简化点：移除 UE5 FGameplayEffectCustomExecutionParameters/Output 复杂结构，
    /// 直接传入 ASC 与 Spec，由 Execute 修改属性。
    /// </summary>
    public abstract class GameplayEffectExecutionCalculation
    {
        /// <summary>执行计算。由 ASC 在应用效果时调用。</summary>
        /// <param name="targetASC">目标 ASC。</param>
        /// <param name="spec">效果规格。</param>
        public abstract void Execute(NarrativeAbilitySystemComponent targetASC, GameplayEffectSpec spec);
    }

    /// <summary>
    /// 伤害执行计算。对应 UE5 UNarrativeDamageExecCalc。
    /// 计算公式：伤害 = 攻击伤害 * (AttackRating / 100) - Armor
    /// 然后将伤害值写入 Damage 元属性。
    /// </summary>
    public class NarrativeDamageExecCalc : GameplayEffectExecutionCalculation
    {
        public override void Execute(NarrativeAbilitySystemComponent targetASC, GameplayEffectSpec spec)
        {
            if (targetASC?.AttributeSet == null) return;

            var attrs = targetASC.AttributeSet;
            float attackDamage = attrs.AttackDamage.CurrentValue;
            float attackRating = attrs.AttackRating.CurrentValue;
            float armor = attrs.Armor.CurrentValue;

            // 攻击评级倍率（AttackRating / 100）
            float attackMultiplier = attackRating / 100f;

            // 计算最终伤害
            float rawDamage = attackDamage * attackMultiplier;
            float finalDamage = rawDamage - armor;
            if (finalDamage < 0f) finalDamage = 0f;

            // 写入 Damage 元属性
            float oldDamage = attrs.Damage.CurrentValue;
            attrs.Damage.SetCurrentValue(oldDamage + finalDamage);

            NarrativePro.Core.NarrativeLog.Log($"[DamageExecCalc] {targetASC.Actor?.Name}: raw={rawDamage}, armor={armor}, final={finalDamage}");
        }
    }

    /// <summary>
    /// 治疗执行计算。对应 UE5 UNarrativeHealExecCalc。
    /// 将效果中指定的治疗量写入 Heal 元属性。
    /// </summary>
    public class NarrativeHealExecCalc : GameplayEffectExecutionCalculation
    {
        public override void Execute(NarrativeAbilitySystemComponent targetASC, GameplayEffectSpec spec)
        {
            if (targetASC?.AttributeSet == null || spec?.Effect == null) return;

            // 从效果修饰器中提取 Heal 量
            float healAmount = 0f;
            foreach (var mod in spec.Effect.Modifiers)
            {
                if (mod.AttributeName == "Heal")
                {
                    healAmount += mod.Magnitude;
                }
            }

            if (healAmount > 0f)
            {
                var attrs = targetASC.AttributeSet;
                float oldHeal = attrs.Heal.CurrentValue;
                attrs.Heal.SetCurrentValue(oldHeal + healAmount);

                NarrativePro.Core.NarrativeLog.Log($"[HealExecCalc] {targetASC.Actor?.Name}: heal={healAmount}");
            }
        }
    }

    /// <summary>
    /// 执行计算注册表。由 TypeId 字符串查找对应的 ExecCalc 实例。
    /// </summary>
    public static class ExecutionCalculationRegistry
    {
        private static readonly System.Collections.Generic.Dictionary<string, GameplayEffectExecutionCalculation> _calculations =
            new System.Collections.Generic.Dictionary<string, GameplayEffectExecutionCalculation>
            {
                { "Damage", new NarrativeDamageExecCalc() },
                { "Heal", new NarrativeHealExecCalc() }
            };

        /// <summary>注册执行计算。</summary>
        public static void Register(string typeId, GameplayEffectExecutionCalculation calc)
        {
            if (string.IsNullOrEmpty(typeId) || calc == null) return;
            _calculations[typeId] = calc;
        }

        /// <summary>查找执行计算。</summary>
        public static GameplayEffectExecutionCalculation Find(string typeId)
        {
            if (string.IsNullOrEmpty(typeId)) return null;
            return _calculations.TryGetValue(typeId, out var calc) ? calc : null;
        }
    }
}

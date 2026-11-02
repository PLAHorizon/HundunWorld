using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.GAS;

namespace NarrativePro.SkillTrees
{
    /// <summary>
    /// 属性修正 Perk。对应 UE5 蓝图资产 TreePerk_AttributeModifier（Perk_AttributeMod）。
    /// 使购买 Perk 可以直接修改属性，例如“攻击力提高 5%”、“潜行提高 10%”
    /// （参见 UE5 SkillTreeComponent.h 注释：“Narrative ships with a Perk_AttributeMod which
    /// allows for easy attribute modifier perks”）。
    /// 通过 owner ASC 的 GameplayEffect 通道施加/移除效果：
    /// 等级变化时以 Instant 效果施加“增量”修饰器（等价 UE5 SetByCaller 幅度按等级重放），
    /// 并记录已施加幅度，防止存档重载时重复叠加
    /// （对应蓝图注释 “so we don't add the attribute twice!”）。
    /// </summary>
    [Serializable]
    public class Perk_AttributeMod : TreePerk
    {
        /// <summary>要修改的属性名（对应 UE5 SetByCaller 数据标签，如 "AttackDamage"、"StealthRating"）。</summary>
        public string AttributeName = "";

        /// <summary>修饰操作类型（仅支持 Add / Multiply）。</summary>
        public EGameplayModOp ModifierOp = EGameplayModOp.Add;

        /// <summary>
        /// 每级效果幅度表（0 基等级索引，即首次购买取 [0]）。对应蓝图变量 LevelPercentages。
        /// Add 语义为该等级的合计绝对加值；Multiply 语义为该等级的合计百分比（0.05 = +5%）。
        /// </summary>
        public List<float> LevelPercentages = new List<float>();

        /// <summary>当 LevelPercentages 为空时的每级回退幅度（按购买次数线性累加）。</summary>
        public float MagnitudePerLevel = 0f;

        /// <summary>效果名称（调试用）。</summary>
        public string EffectName = "";

        /// <summary>当前已应用到 ASC 的总幅度（Add：已加值；Multiply：已乘因子）。用于计算增量与移除。</summary>
        private float _appliedMagnitude = 0f;

        /// <summary>是否已有生效中的属性效果。</summary>
        private bool _bEffectApplied = false;

        public override void SetPerkLevel(int newPerkLevel)
        {
            base.SetPerkLevel(newPerkLevel);
            SyncEffectToASC();
        }

        /// <summary>计算指定 Perk 等级（0 基，-1 未购买）对应的效果总幅度。</summary>
        public virtual float ComputeMagnitudeForLevel(int level)
        {
            bool bMultiply = ModifierOp == EGameplayModOp.Multiply;
            if (level < 0) return bMultiply ? 1f : 0f;

            if (LevelPercentages != null && LevelPercentages.Count > 0)
            {
                int idx = level < LevelPercentages.Count ? level : LevelPercentages.Count - 1;
                float raw = LevelPercentages[idx];
                return bMultiply ? 1f + raw : raw;
            }

            int purchases = level + 1;
            return bMultiply ? 1f + MagnitudePerLevel * purchases : MagnitudePerLevel * purchases;
        }

        /// <summary>把属性效果同步到 owner ASC：以 Instant GameplayEffect 施加等级增量。</summary>
        protected virtual void SyncEffectToASC()
        {
            if (OwningComponent == null || string.IsNullOrEmpty(AttributeName)) return;

            NarrativeAbilitySystemComponent asc = ArsenalStatics.GetNarrativeAbilitySystemComponent(OwningComponent.Actor);
            if (asc == null)
            {
                NarrativeLog.LogWarning($"[SkillTree] Perk {PerkClassPath} 无法应用属性效果：Actor 上未找到 NarrativeAbilitySystemComponent");
                return;
            }

            bool bMultiply = ModifierOp == EGameplayModOp.Multiply;
            float target = ComputeMagnitudeForLevel(PerkLevel);
            float delta;
            EGameplayModOp applyOp = ModifierOp;

            if (bMultiply)
            {
                if (!_bEffectApplied || Mathf.Abs(_appliedMagnitude) < 0.0001f)
                {
                    delta = target; // 首次施加（或此前因子为 0 需重置）
                    if (Mathf.Abs(delta - 1f) < 0.00001f) return; // 无实际效果
                }
                else
                {
                    delta = target / _appliedMagnitude; // 比率链
                    if (Mathf.Abs(delta - 1f) < 0.00001f) return; // 等级未变化或重复加载
                }
            }
            else
            {
                delta = target - _appliedMagnitude;
                if (Mathf.Abs(delta) < 0.00001f) return; // 等级未变化或重复加载，避免重复叠加
            }

            var effect = new GameplayEffect(string.IsNullOrEmpty(EffectName) ? $"PerkMod:{AttributeName}" : EffectName)
            {
                DurationType = EGameplayEffectDurationType.Instant
            };
            effect.Modifiers.Add(new GameplayModifierInfo
            {
                AttributeName = AttributeName,
                ModifierOp = applyOp,
                Magnitude = delta
            });

            var spec = new GameplayEffectSpec(effect, asc);
            asc.ApplyGameplayEffectSpecToSelf(new GameplayEffectSpecHandle(spec));

            _appliedMagnitude = bMultiply ? delta : _appliedMagnitude + delta;
            _bEffectApplied = true;
        }

        /// <summary>移除本 Perk 已施加的属性效果（对称逆操作）。对应 UE5 中移除 GameplayEffect。</summary>
        public virtual void RemovePerkEffect()
        {
            if (!_bEffectApplied) return;
            if (OwningComponent == null || string.IsNullOrEmpty(AttributeName)) return;

            NarrativeAbilitySystemComponent asc = ArsenalStatics.GetNarrativeAbilitySystemComponent(OwningComponent.Actor);
            if (asc == null) return;

            bool bMultiply = ModifierOp == EGameplayModOp.Multiply;
            var effect = new GameplayEffect(string.IsNullOrEmpty(EffectName) ? $"PerkModRemove:{AttributeName}" : EffectName)
            {
                DurationType = EGameplayEffectDurationType.Instant
            };
            effect.Modifiers.Add(new GameplayModifierInfo
            {
                AttributeName = AttributeName,
                ModifierOp = bMultiply ? EGameplayModOp.Divide : EGameplayModOp.Add,
                Magnitude = bMultiply ? _appliedMagnitude : -_appliedMagnitude
            });

            var spec = new GameplayEffectSpec(effect, asc);
            asc.ApplyGameplayEffectSpecToSelf(new GameplayEffectSpecHandle(spec));

            _appliedMagnitude = 0f;
            _bEffectApplied = false;
        }

        public override string GetPerkDescription()
        {
            if (!string.IsNullOrEmpty(PerkDescription)) return PerkDescription;
            string value = ModifierOp == EGameplayModOp.Multiply
                ? $"{(ComputeMagnitudeForLevel(PerkLevel) - 1f) * 100f:0.#}%"
                : ComputeMagnitudeForLevel(PerkLevel).ToString("0.###");
            return $"{AttributeName} {value}（Perk 等级 {PerkLevel + 1}）";
        }
    }
}

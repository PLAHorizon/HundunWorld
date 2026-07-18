using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Items;

namespace NarrativePro.GAS
{
    /// <summary>
    /// 属性事件委托。对应 UE5 FNarrativeAttributeEvent。
    /// </summary>
    /// <param name="newValue">新值。</param>
    /// <param name="oldValue">旧值。</param>
    public delegate void AttributeEvent(float newValue, float oldValue);

    /// <summary>
    /// 属性集合基类。对应 UE5 UNarrativeAttributeSetBase。
    /// 包含 Narrative Pro 框架的默认属性：Health、MaxHealth、Stamina、MaxStamina、StaminaRegenRate、
    /// AttackRating、Armor、AttackDamage、StealthRating、XP，以及 Heal/Damage 元属性。
    /// 简化点：
    /// - 移除 UE5 复制（OnRep_Xxx），改为事件 OnXxxChanged
    /// - 移除网络预测/本地/远端区分
    /// - FGameplayAttributeData 替换为 AttributeData
    /// </summary>
    public class NarrativeAttributeSetBase : Script
    {
        // ===== 属性（每个属性一个 AttributeData + 对应事件）=====

        /// <summary>经验值（XP）。</summary>
        public AttributeData XP = new AttributeData(0f);

        /// <summary>当前生命值，0 时角色死亡。被 MaxHealth 限制。</summary>
        public AttributeData Health = new AttributeData(100f);

        /// <summary>最大生命值。GameplayEffects 可修改。</summary>
        public AttributeData MaxHealth = new AttributeData(100f);

        /// <summary>当前耐力，用于特殊能力。被 MaxStamina 限制。</summary>
        public AttributeData Stamina = new AttributeData(100f);

        /// <summary>最大耐力值。GameplayEffects 可修改。</summary>
        public AttributeData MaxStamina = new AttributeData(100f);

        /// <summary>耐力恢复速率（每秒）。</summary>
        public AttributeData StaminaRegenRate = new AttributeData(5f);

        /// <summary>攻击评级（百分比形式，作为伤害倍率使用，倍率 = AttackRating / 100）。</summary>
        public AttributeData AttackRating = new AttributeData(100f);

        /// <summary>护甲（减少受到的伤害）。</summary>
        public AttributeData Armor = new AttributeData(0f);

        /// <summary>攻击伤害基础值（攻击造成的原始伤害）。</summary>
        public AttributeData AttackDamage = new AttributeData(10f);

        /// <summary>潜行评级（0-100，越高越不被 NPC 发现）。</summary>
        public AttributeData StealthRating = new AttributeData(0f);

        /// <summary>治疗元属性（瞬时使用，每秒应用后归零）。</summary>
        public AttributeData Heal = new AttributeData(0f);

        /// <summary>伤害元属性（瞬时使用，每秒应用后归零）。</summary>
        public AttributeData Damage = new AttributeData(0f);

        // ===== 事件 =====

        /// <summary>生命值变化事件。</summary>
        public event AttributeEvent OnHealthChanged;

        /// <summary>最大生命值变化事件。</summary>
        public event AttributeEvent OnMaxHealthChanged;

        /// <summary>耐力值变化事件。</summary>
        public event AttributeEvent OnStaminaChanged;

        /// <summary>最大耐力值变化事件。</summary>
        public event AttributeEvent OnMaxStaminaChanged;

        /// <summary>耐力恢复速率变化事件。</summary>
        public event AttributeEvent OnStaminaRegenRateChanged;

        /// <summary>攻击评级变化事件。</summary>
        public event AttributeEvent OnAttackRatingChanged;

        /// <summary>护甲变化事件。</summary>
        public event AttributeEvent OnArmorChanged;

        /// <summary>攻击伤害变化事件。</summary>
        public event AttributeEvent OnAttackDamageChanged;

        /// <summary>潜行评级变化事件。</summary>
        public event AttributeEvent OnStealthRatingChanged;

        /// <summary>经验值变化事件。</summary>
        public event AttributeEvent OnXPChanged;

        /// <summary>生命值耗尽事件。</summary>
        public event AttributeEvent OnOutOfHealth;

        // ===== 属性访问 =====

        /// <summary>根据属性名获取 AttributeData。</summary>
        public virtual AttributeData GetAttribute(string attributeName)
        {
            switch (attributeName)
            {
                case "XP": return XP;
                case "Health": return Health;
                case "MaxHealth": return MaxHealth;
                case "Stamina": return Stamina;
                case "MaxStamina": return MaxStamina;
                case "StaminaRegenRate": return StaminaRegenRate;
                case "AttackRating": return AttackRating;
                case "Armor": return Armor;
                case "AttackDamage": return AttackDamage;
                case "StealthRating": return StealthRating;
                case "Heal": return Heal;
                case "Damage": return Damage;
                default: return null;
            }
        }

        /// <summary>根据属性名触发对应的变化事件。</summary>
        public virtual void NotifyAttributeChanged(string attributeName, float newValue, float oldValue)
        {
            switch (attributeName)
            {
                case "XP": OnXPChanged?.Invoke(newValue, oldValue); break;
                case "Health":
                    OnHealthChanged?.Invoke(newValue, oldValue);
                    if (newValue <= 0f && oldValue > 0f) OnOutOfHealth?.Invoke(newValue, oldValue);
                    break;
                case "MaxHealth": OnMaxHealthChanged?.Invoke(newValue, oldValue); break;
                case "Stamina": OnStaminaChanged?.Invoke(newValue, oldValue); break;
                case "MaxStamina": OnMaxStaminaChanged?.Invoke(newValue, oldValue); break;
                case "StaminaRegenRate": OnStaminaRegenRateChanged?.Invoke(newValue, oldValue); break;
                case "AttackRating": OnAttackRatingChanged?.Invoke(newValue, oldValue); break;
                case "Armor": OnArmorChanged?.Invoke(newValue, oldValue); break;
                case "AttackDamage": OnAttackDamageChanged?.Invoke(newValue, oldValue); break;
                case "StealthRating": OnStealthRatingChanged?.Invoke(newValue, oldValue); break;
            }
        }

        // ===== 元属性处理 =====

        /// <summary>每帧处理元属性（Heal/Damage），由 ASC 调用。</summary>
        public virtual void PostGameplayEffectExecute()
        {
            // 应用 Heal
            if (Heal.CurrentValue != 0f)
            {
                float healAmount = Heal.CurrentValue;
                Heal.SetCurrentValue(0f);
                float oldHealth = Health.CurrentValue;
                float newHealth = Mathf.Clamp(oldHealth + healAmount, 0f, MaxHealth.CurrentValue);
                Health.SetCurrentValue(newHealth);
                NotifyAttributeChanged("Health", newHealth, oldHealth);
            }

            // 应用 Damage
            if (Damage.CurrentValue != 0f)
            {
                float dmgAmount = Damage.CurrentValue;
                Damage.SetCurrentValue(0f);
                float oldHealth = Health.CurrentValue;
                float newHealth = Mathf.Clamp(oldHealth - dmgAmount, 0f, MaxHealth.CurrentValue);
                Health.SetCurrentValue(newHealth);
                NotifyAttributeChanged("Health", newHealth, oldHealth);
            }
        }

        /// <summary>每帧处理耐力恢复（由 ASC 调用）。</summary>
        public virtual void TickStaminaRegen(float deltaTime)
        {
            if (StaminaRegenRate.CurrentValue > 0f && Stamina.CurrentValue < MaxStamina.CurrentValue)
            {
                float old = Stamina.CurrentValue;
                float next = Mathf.Clamp(old + StaminaRegenRate.CurrentValue * deltaTime, 0f, MaxStamina.CurrentValue);
                Stamina.SetCurrentValue(next);
                if (Mathf.Abs(next - old) > 0.001f)
                {
                    NotifyAttributeChanged("Stamina", next, old);
                }
            }
        }

        public override void OnEnable()
        {
            base.OnEnable();
        }

        public override void OnDisable()
        {
            base.OnDisable();
        }
    }
}

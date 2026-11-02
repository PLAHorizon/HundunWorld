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
    /// 血量耗尽的详细事件。对应 UE5 <c>FNarrativeOnOutOfHealth</c>
    /// （参数为伤害发起者 Actor、伤害制造者 Actor、造成伤害的效果规格、伤害量）。
    /// <para>
    /// UE5 用同一个委托把 ASC 的 <c>HandleOutOfHealth</c> 绑上来；这里保留旧的
    /// <see cref="AttributeEvent"/> 版 <c>OnOutOfHealth</c>（只带新旧值）以兼容既有订阅方，
    /// 由本详细事件承担需要伤害上下文的场景。
    /// </para>
    /// </summary>
    public delegate void OnOutOfHealthDetailedDelegate(
        FlaxEngine.Actor damageInstigator,
        FlaxEngine.Actor damageCauser,
        GameplayEffectSpec damageEffectSpec,
        float damageMagnitude);

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

        /// <summary>
        /// 生命值耗尽事件（带伤害上下文）。对应 UE5 OnOutOfHealth → ASC::HandleOutOfHealth。
        /// 由 <see cref="PostGameplayEffectExecute(GameplayEffectSpec)"/> 在血量归零时触发。
        /// </summary>
        public event OnOutOfHealthDetailedDelegate OnOutOfHealthDetailed;

        // ===== 归属 ASC（对应 UE5 GetOwningAbilitySystemComponent） =====

        /// <summary>拥有本属性集的 ASC（运行时由 ASC 或 <see cref="AttachToComponent"/> 设置）。</summary>
        [NonSerialized]
        public NarrativeAbilitySystemComponent OwningASC;

        /// <summary>记录 OutOfHealth 是否已绑定，避免重复订阅。</summary>
        private bool _bOutOfHealthBound;

        /// <summary>绑定归属 ASC（幂等）。</summary>
        public virtual void AttachToComponent(NarrativeAbilitySystemComponent asc)
        {
            if (asc == null) return;
            OwningASC = asc;
        }

        /// <summary>
        /// 由 ASC 订阅血量耗尽事件。
        /// 对应 UE5 <c>InitAbilityActorInfo</c> 中 <c>MutableAttributeSet-&gt;OnOutOfHealth.AddUObject(this, &amp;HandleOutOfHealth)</c>。
        /// </summary>
        public virtual void BindOutOfHealth(NarrativeAbilitySystemComponent asc)
        {
            if (asc == null || _bOutOfHealthBound) return;
            OwningASC = asc;
            OnOutOfHealthDetailed += asc.HandleOutOfHealth;
            _bOutOfHealthBound = true;
        }

        /// <summary>解除血量耗尽订阅。</summary>
        public virtual void UnbindOutOfHealth(NarrativeAbilitySystemComponent asc)
        {
            if (!_bOutOfHealthBound) return;
            if (asc != null) OnOutOfHealthDetailed -= asc.HandleOutOfHealth;
            _bOutOfHealthBound = false;
        }

        /// <summary>取归属 ASC。对应 UE5 <c>GetOwningAbilitySystemComponent</c>。</summary>
        public NarrativeAbilitySystemComponent GetOwningAbilitySystemComponent() => OwningASC;

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

        /// <summary>
        /// 效果执行前判定。对应 UE5 <c>PreGameplayEffectExecute</c>
        /// （NarrativeAttributeSetBase.cpp:44-62）。
        /// <para>返回 false 表示把这次执行作废——用于免疫状态下丢弃伤害。</para>
        /// </summary>
        /// <param name="attributeName">本次要写入的属性名（UE5 中是 FGameplayAttribute）。</param>
        /// <param name="magnitude">已解析（含 SetByCaller）的修饰量级。</param>
        /// <param name="spec">效果规格，可能为 null（直接 DealDamage 这类无规格路径）。</param>
        public virtual bool PreGameplayEffectExecute(string attributeName, float magnitude, GameplayEffectSpec spec)
        {
            if (attributeName == "Damage" && magnitude > 0f)
            {
                var invulnerable = NarrativePro.Core.NarrativeGameplayTags.State_Invulnerable;
                if (OwningASC != null && OwningASC.HasMatchingGameplayTag(invulnerable))
                {
                    // 免疫：把伤害清零并丢弃这次执行
                    if (spec != null)
                    {
                        spec.SetSetByCallerMagnitude(NarrativePro.Core.NarrativeGameplayTags.SetByCaller_Damage, 0f);
                    }
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 无规格版的元属性处理（保持既有公共签名可用）。
        /// </summary>
        public virtual void PostGameplayEffectExecute()
        {
            PostGameplayEffectExecute(null);
        }

        /// <summary>
        /// 每帧处理元属性（Heal/Damage），由 ASC 调用。
        /// 对应 UE5 <c>PostGameplayEffectExecute</c>。
        /// </summary>
        /// <param name="spec">触发本次处理的效果规格（用于回填伤害发起者等上下文，可为 null）。</param>
        public virtual void PostGameplayEffectExecute(GameplayEffectSpec spec)
        {
            // 应用 Heal
            if (Heal.CurrentValue != 0f)
            {
                float healAmount = Heal.CurrentValue;
                // 元属性是一次性投递量：Base/Current 都要归零，否则下次重算会把旧值再算进去
                Heal.BaseValue = 0f;
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
                Damage.BaseValue = 0f;
                Damage.SetCurrentValue(0f);
                float oldHealth = Health.CurrentValue;
                float newHealth = Mathf.Clamp(oldHealth - dmgAmount, 0f, MaxHealth.CurrentValue);
                Health.SetCurrentValue(newHealth);
                NotifyAttributeChanged("Health", newHealth, oldHealth);

                // 血量归零：向 ASC 抛出带上下文的事件（UE5 里这条链触发 HandleOutOfHealth）
                if (newHealth <= 0f && oldHealth > 0f)
                {
                    FlaxEngine.Actor instigator = spec?.SourceActor;
                    OnOutOfHealthDetailed?.Invoke(instigator, instigator, spec, dmgAmount);
                }
            }
        }

        // ===== Max 属性联动 =====

        /// <summary>
        /// 当 Max 类属性变化时，按比例重标定其派生属性。
        /// 对应 UE5 <c>AdjustAttributeForMaxChange</c>（NarrativeAttributeSetBase.cpp:224-236）：
        /// 保持 当前值 / 最大值 的百分比不变。
        /// <para>
        /// 调用时机与 UE5 一致：必须在把新 Max 写入 <paramref name="maxAttribute"/> 之前调用，
        /// 因为旧 Max 是从 <c>maxAttribute.CurrentValue</c> 读出的。
        /// </para>
        /// </summary>
        /// <param name="affectedAttribute">需要重标定的派生属性（如 Health）。</param>
        /// <param name="maxAttribute">其 Max 属性（如 MaxHealth），此时仍持有旧 Max 值。</param>
        /// <param name="newMaxValue">Max 的新值。</param>
        /// <param name="affectedAttributeName">派生属性名（用于触发变化通知）。</param>
        public virtual void AdjustAttributeForMaxChange(
            AttributeData affectedAttribute,
            AttributeData maxAttribute,
            float newMaxValue,
            string affectedAttributeName)
        {
            if (affectedAttribute == null || maxAttribute == null) return;

            float currentMaxValue = maxAttribute.CurrentValue;
            if (Mathf.Abs(currentMaxValue - newMaxValue) < 0.0001f) return;

            // 维持百分比：新值 = 当前值 * 新Max / 旧Max
            float currentValue = affectedAttribute.CurrentValue;
            float newDelta = currentMaxValue > 0f
                ? (currentValue * newMaxValue / currentMaxValue) - currentValue
                : newMaxValue;

            float targetValue = Mathf.Clamp(currentValue + newDelta, 0f, newMaxValue);

            float oldValue = affectedAttribute.CurrentValue;
            affectedAttribute.BaseValue = targetValue;
            affectedAttribute.SetCurrentValue(targetValue);

            if (Mathf.Abs(targetValue - oldValue) > 0.0001f && !string.IsNullOrEmpty(affectedAttributeName))
            {
                NotifyAttributeChanged(affectedAttributeName, targetValue, oldValue);
            }
        }

        /// <summary>
        /// 某个属性的当前值即将变为 newMaxValue 时的 Max 联动：
        /// 若该属性是 MaxHealth/MaxStamina，则按比例重标定其派生属性。
        /// UE5 由子类在 PreGameplayEffectExecute 内显式调用；
        /// Flax 侧属性写入集中在 ASC，故在这里做统一派发（须在写入新值之前调用）。
        /// </summary>
        public virtual void OnMaxAttributeChanged(string attributeName, float newMaxValue)
        {
            if (attributeName == "MaxHealth")
            {
                AdjustAttributeForMaxChange(Health, MaxHealth, newMaxValue, "Health");
            }
            else if (attributeName == "MaxStamina")
            {
                AdjustAttributeForMaxChange(Stamina, MaxStamina, newMaxValue, "Stamina");
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

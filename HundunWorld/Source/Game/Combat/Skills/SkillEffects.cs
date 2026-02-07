using FlaxEngine;
using System;

namespace HundunWorld.Game.Combat.Skills
{
    /// <summary>
    /// 技能效果类型
    /// </summary>
    public enum SkillEffectType
    {
        DamageOverTime,     // 持续伤害
        HealOverTime,       // 持续治疗
        Buff,               // 增益效果
        Debuff,             // 减益效果
        Stun,               // 眩晕
        Slow,               // 减速
        Root,               // 定身
        Silence,            // 沉默
        Invulnerable,       // 无敌
        Stealth,            // 隐身
        Taunt,              // 嘲讽
        Fear                // 恐惧
    }

    /// <summary>
    /// 技能效果基类
    /// </summary>
    public abstract class SkillEffect
    {
        #region 效果属性
        public string EffectName { get; set; }
        public SkillEffectType EffectType { get; set; }
        public float Duration { get; set; }
        public float ElapsedTime { get; private set; }
        public int Stacks { get; set; } = 1;
        public int MaxStacks { get; set; } = 1;
        public bool IsPermanent { get; set; } = false;
        public Actor Caster { get; set; }
        public object[] Parameters { get; set; }
        #endregion

        #region 状态标志
        public bool IsActive { get; private set; } = false;
        public bool IsExpired => !IsPermanent && ElapsedTime >= Duration;
        #endregion

        protected Actor _target;

        #region 生命周期方法
        /// <summary>
        /// 应用效果到目标
        /// </summary>
        public virtual void Apply(Actor target)
        {
            _target = target;
            IsActive = true;
            OnApply();
        }

        /// <summary>
        /// 移除效果
        /// </summary>
        public virtual void Remove()
        {
            if (IsActive)
            {
                IsActive = false;
                OnRemove();
            }
        }

        /// <summary>
        /// 更新效果
        /// </summary>
        public virtual void Update(float deltaTime)
        {
            if (!IsActive) return;

            ElapsedTime += deltaTime;
            OnUpdate(deltaTime);

            // 检查是否到期
            if (IsExpired)
            {
                Remove();
            }
        }

        /// <summary>
        /// 堆叠效果
        /// </summary>
        public virtual bool StackWith(SkillEffect other)
        {
            if (other.EffectType == EffectType && other.EffectName == EffectName)
            {
                if (Stacks < MaxStacks)
                {
                    Stacks++;
                    OnStack();
                    return true;
                }
                else
                {
                    // 刷新持续时间
                    ElapsedTime = 0f;
                    OnRefresh();
                    return true;
                }
            }
            return false;
        }
        #endregion

        #region 虚方法（子类实现）
        protected virtual void OnApply() { }
        protected virtual void OnRemove() { }
        protected virtual void OnUpdate(float deltaTime) { }
        protected virtual void OnStack() { }
        protected virtual void OnRefresh() { }
        #endregion

        #region 辅助方法
        protected T GetParameter<T>(int index, T defaultValue = default)
        {
            if (Parameters != null && index < Parameters.Length)
            {
                try
                {
                    return (T)Convert.ChangeType(Parameters[index], typeof(T));
                }
                catch
                {
                    return defaultValue;
                }
            }
            return defaultValue;
        }

        protected void SetParameter(int index, object value)
        {
            if (Parameters == null)
            {
                Parameters = new object[Math.Max(4, index + 1)];
            }
            else if (index >= Parameters.Length)
            {
                var newArray = new object[index + 1];
                Array.Copy(Parameters, newArray, Parameters.Length);
                Parameters = newArray;
            }
            Parameters[index] = value;
        }
        #endregion
    }

    /// <summary>
    /// 持续伤害效果
    /// </summary>
    public class DamageOverTimeEffect : SkillEffect
    {
        private float _damagePerTick;
        private float _tickInterval;
        private float _timeSinceLastTick;

        public DamageOverTimeEffect(float damagePerSecond, float duration, float tickInterval = 1.0f)
        {
            EffectName = "持续伤害";
            EffectType = SkillEffectType.DamageOverTime;
            Duration = duration;
            _tickInterval = tickInterval;
            _damagePerTick = damagePerSecond * tickInterval;
        }

        protected override void OnApply()
        {
            _timeSinceLastTick = 0f;
            Debug.Log($"[DoT] 应用持续伤害效果: {_damagePerTick:F1}/tick");
        }

        protected override void OnUpdate(float deltaTime)
        {
            _timeSinceLastTick += deltaTime;
            
            if (_timeSinceLastTick >= _tickInterval)
            {
                DealDamage();
                _timeSinceLastTick = 0f;
            }
        }

        private void DealDamage()
        {
            if (_target == null) return;

            // 这里应该调用实际的伤害系统
            // DamageSystem.Instance.DealDamage(Caster, _target, _damagePerTick, DamageType.Magic);
            
            Debug.Log($"[DoT] 造成 {_damagePerTick:F1} 点伤害");
        }
    }

    /// <summary>
    /// 持续治疗效果
    /// </summary>
    public class HealOverTimeEffect : SkillEffect
    {
        private float _healPerTick;
        private float _tickInterval;
        private float _timeSinceLastTick;

        public HealOverTimeEffect(float healPerSecond, float duration, float tickInterval = 1.0f)
        {
            EffectName = "持续治疗";
            EffectType = SkillEffectType.HealOverTime;
            Duration = duration;
            _tickInterval = tickInterval;
            _healPerTick = healPerSecond * tickInterval;
        }

        protected override void OnApply()
        {
            _timeSinceLastTick = 0f;
            Debug.Log($"[HoT] 应用持续治疗效果: {_healPerTick:F1}/tick");
        }

        protected override void OnUpdate(float deltaTime)
        {
            _timeSinceLastTick += deltaTime;
            
            if (_timeSinceLastTick >= _tickInterval)
            {
                ApplyHeal();
                _timeSinceLastTick = 0f;
            }
        }

        private void ApplyHeal()
        {
            if (_target == null) return;

            // 这里应该调用实际的治疗系统
            // HealingSystem.Instance.Heal(_target, _healPerTick);
            
            Debug.Log($"[HoT] 恢复 {_healPerTick:F1} 点生命值");
        }
    }

    /// <summary>
    /// 属性增益/减益效果
    /// </summary>
    public class AttributeBuffEffect : SkillEffect
    {
        public enum AttributeType
        {
            Attack, Defense, Speed, Critical, Resistance
        }

        private AttributeType _attributeType;
        private float _modifierValue;
        private bool _isPercentage;

        public AttributeBuffEffect(AttributeType attribute, float value, bool isPercentage, float duration)
        {
            _attributeType = attribute;
            _modifierValue = value;
            _isPercentage = isPercentage;
            Duration = duration;
            
            EffectName = $"{attribute} {(_modifierValue >= 0 ? "增益" : "减益")}";
            EffectType = _modifierValue >= 0 ? SkillEffectType.Buff : SkillEffectType.Debuff;
        }

        protected override void OnApply()
        {
            ApplyAttributeModifier();
            Debug.Log($"[Buff] 应用属性效果: {_attributeType} {(_isPercentage ? $"{_modifierValue:P}" : $"{_modifierValue:F1}")}");
        }

        protected override void OnRemove()
        {
            RemoveAttributeModifier();
            Debug.Log($"[Buff] 移除属性效果: {_attributeType}");
        }

        private void ApplyAttributeModifier()
        {
            if (_target == null) return;
            
            // 这里应该调用实际的属性系统
            // var attrComp = _target.GetComponent<CharacterAttributesComponent>();
            // if (attrComp != null)
            // {
            //     attrComp.ApplyTemporaryModifier(_attributeType, _modifierValue, _isPercentage, Duration);
            // }
        }

        private void RemoveAttributeModifier()
        {
            // 移除属性修饰符的逻辑
        }
    }

    /// <summary>
    /// 控制效果基类
    /// </summary>
    public abstract class ControlEffect : SkillEffect
    {
        protected bool _wasControlled = false;

        protected override void OnApply()
        {
            ApplyControl();
            _wasControlled = true;
        }

        protected override void OnRemove()
        {
            if (_wasControlled)
            {
                RemoveControl();
                _wasControlled = false;
            }
        }

        protected abstract void ApplyControl();
        protected abstract void RemoveControl();
    }

    /// <summary>
    /// 眩晕效果
    /// </summary>
    public class StunEffect : ControlEffect
    {
        public StunEffect(float duration) : base()
        {
            EffectName = "眩晕";
            EffectType = SkillEffectType.Stun;
            Duration = duration;
        }

        protected override void ApplyControl()
        {
            // 禁用目标的移动和技能
            // var controller = _target.GetComponent<CharacterController>();
            // if (controller != null) controller.enabled = false;
            
            // var skillSystem = _target.GetComponent<SkillSystem>();
            // if (skillSystem != null) skillSystem.Disable();
            
            Debug.Log("[Stun] 目标已被眩晕");
        }

        protected override void RemoveControl()
        {
            // 恢复目标的控制
            // var controller = _target.GetComponent<CharacterController>();
            // if (controller != null) controller.enabled = true;
            
            // var skillSystem = _target.GetComponent<SkillSystem>();
            // if (skillSystem != null) skillSystem.Enable();
            
            Debug.Log("[Stun] 眩晕效果结束");
        }
    }

    /// <summary>
    /// 减速效果
    /// </summary>
    public class SlowEffect : ControlEffect
    {
        private float _slowPercent;

        public SlowEffect(float slowPercent, float duration) : base()
        {
            EffectName = "减速";
            EffectType = SkillEffectType.Slow;
            Duration = duration;
            _slowPercent = slowPercent;
        }

        protected override void ApplyControl()
        {
            // 降低目标移动速度
            // var movement = _target.GetComponent<MovementComponent>();
            // if (movement != null) movement.SpeedMultiplier *= (1.0f - _slowPercent);
            
            Debug.Log($"[Slow] 目标速度降低 {_slowPercent:P}");
        }

        protected override void RemoveControl()
        {
            // 恢复目标移动速度
            // var movement = _target.GetComponent<MovementComponent>();
            // if (movement != null) movement.SpeedMultiplier /= (1.0f - _slowPercent);
            
            Debug.Log("[Slow] 减速效果结束");
        }
    }

    /// <summary>
    /// 无敌效果
    /// </summary>
    public class InvulnerabilityEffect : SkillEffect
    {
        public InvulnerabilityEffect(float duration) : base()
        {
            EffectName = "无敌";
            EffectType = SkillEffectType.Invulnerable;
            Duration = duration;
        }

        protected override void OnApply()
        {
            // 启用无敌状态
            // var health = _target.GetComponent<HealthComponent>();
            // if (health != null) health.IsInvulnerable = true;
            
            Debug.Log("[Invuln] 目标获得无敌");
        }

        protected override void OnRemove()
        {
            // 移除无敌状态
            // var health = _target.GetComponent<HealthComponent>();
            // if (health != null) health.IsInvulnerable = false;
            
            Debug.Log("[Invuln] 无敌效果结束");
        }
    }

    /// <summary>
    /// 技能效果工厂
    /// </summary>
    public static class SkillEffectFactory
    {
        public static SkillEffect CreateDamageOverTime(float dps, float duration, float tickInterval = 1.0f)
        {
            return new DamageOverTimeEffect(dps, duration, tickInterval);
        }

        public static SkillEffect CreateHealOverTime(float hps, float duration, float tickInterval = 1.0f)
        {
            return new HealOverTimeEffect(hps, duration, tickInterval);
        }

        public static SkillEffect CreateAttributeBuff(AttributeBuffEffect.AttributeType attribute, 
            float value, bool isPercentage, float duration)
        {
            return new AttributeBuffEffect(attribute, value, isPercentage, duration);
        }

        public static SkillEffect CreateStun(float duration)
        {
            return new StunEffect(duration);
        }

        public static SkillEffect CreateSlow(float percent, float duration)
        {
            return new SlowEffect(percent, duration);
        }

        public static SkillEffect CreateInvulnerability(float duration)
        {
            return new InvulnerabilityEffect(duration);
        }
    }
}
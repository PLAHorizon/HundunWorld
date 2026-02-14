using FlaxEngine;
using System;
using System.Collections.Generic;
using HundunWorld.Game.ECS.Components;
using HundunWorld.Game.Character;
using Horizon.Game.Message.Enums;

namespace HundunWorld.Game.Combat.Effects
{
    /// <summary>
    /// 技能效果系统
    /// 管理各种战斗效果（Buff/Debuff）的施加、更新和移除
    /// </summary>
    public class SkillEffectSystem
    {
        private static SkillEffectSystem _instance;
        public static SkillEffectSystem Instance => _instance ??= new SkillEffectSystem();

        private readonly Dictionary<ulong, List<ActiveEffect>> _activeEffects;
        private readonly List<EffectTemplate> _effectTemplates;
        private readonly ICharacterAttributeManager _attributeManager;
        private readonly IControlStateManager _controlManager;

        private SkillEffectSystem()
        {
            _activeEffects = new Dictionary<ulong, List<ActiveEffect>>();
            _effectTemplates = new List<EffectTemplate>();
            _attributeManager = CharacterAttributeManager.Instance;
            _controlManager = ControlStateManager.Instance;
            InitializeEffectTemplates();
        }

        /// <summary>
        /// 初始化预定义的效果模板
        /// </summary>
        private void InitializeEffectTemplates()
        {
            // 增益效果模板
            _effectTemplates.Add(new EffectTemplate
            {
                Id = 1,
                Name = "力量提升",
                Type = EffectType.Buff,
                Attribute = EffectAttribute.Attack,
                Value = 50,
                Duration = 30,
                MaxStacks = 3,
                IsPercent = false
            });

            _effectTemplates.Add(new EffectTemplate
            {
                Id = 2,
                Name = "防御强化",
                Type = EffectType.Buff,
                Attribute = EffectAttribute.Defense,
                Value = 30,
                Duration = 30,
                MaxStacks = 1,
                IsPercent = true
            });

            _effectTemplates.Add(new EffectTemplate
            {
                Id = 3,
                Name = "急速",
                Type = EffectType.Buff,
                Attribute = EffectAttribute.AttackSpeed,
                Value = 25,
                Duration = 15,
                MaxStacks = 1,
                IsPercent = true
            });

            // 减益效果模板
            _effectTemplates.Add(new EffectTemplate
            {
                Id = 101,
                Name = "虚弱",
                Type = EffectType.Debuff,
                Attribute = EffectAttribute.Attack,
                Value = 20,
                Duration = 10,
                MaxStacks = 1,
                IsPercent = true
            });

            _effectTemplates.Add(new EffectTemplate
            {
                Id = 102,
                Name = "迟缓",
                Type = EffectType.Debuff,
                Attribute = EffectAttribute.MoveSpeed,
                Value = 30,
                Duration = 8,
                MaxStacks = 1,
                IsPercent = true
            });

            _effectTemplates.Add(new EffectTemplate
            {
                Id = 103,
                Name = "中毒",
                Type = EffectType.DoT,
                Attribute = EffectAttribute.Health,
                Value = 15,
                Duration = 12,
                MaxStacks = 1,
                IsPercent = false,
                TickInterval = 2
            });

            // 控制效果模板
            _effectTemplates.Add(new EffectTemplate
            {
                Id = 201,
                Name = "眩晕",
                Type = EffectType.Control,
                Attribute = EffectAttribute.Stun,
                Value = 1,
                Duration = 3,
                MaxStacks = 1,
                IsPercent = false
            });

            _effectTemplates.Add(new EffectTemplate
            {
                Id = 202,
                Name = "沉默",
                Type = EffectType.Control,
                Attribute = EffectAttribute.Silence,
                Value = 1,
                Duration = 5,
                MaxStacks = 1,
                IsPercent = false
            });
        }

        /// <summary>
        /// 对目标施加效果
        /// </summary>
        public bool ApplyEffect(ulong targetId, int effectTemplateId, ulong sourceId = 0)
        {
            var template = _effectTemplates.Find(t => t.Id == effectTemplateId);
            if (template == null)
            {
                Debug.LogWarning($"[SkillEffectSystem] 未找到效果模板: {effectTemplateId}");
                return false;
            }

            var effect = new ActiveEffect
            {
                Template = template,
                SourceEntityId = sourceId,
                RemainingDuration = template.Duration,
                CurrentTicks = 0,
                Stacks = 1
            };

            // 检查目标是否已有相同效果
            if (_activeEffects.ContainsKey(targetId))
            {
                var existingEffect = _activeEffects[targetId].Find(e => e.Template.Id == effectTemplateId);
                if (existingEffect != null)
                {
                    // 处理叠加
                    if (template.MaxStacks > 1)
                    {
                        if (existingEffect.Stacks < template.MaxStacks)
                        {
                            existingEffect.Stacks++;
                            existingEffect.RemainingDuration = template.Duration; // 刷新持续时间
                            Debug.Log($"[SkillEffectSystem] 效果叠加: {template.Name} (层数: {existingEffect.Stacks})");
                            return true;
                        }
                        else
                        {
                            // 已达最大层数，刷新持续时间
                            existingEffect.RemainingDuration = template.Duration;
                            Debug.Log($"[SkillEffectSystem] 效果刷新: {template.Name}");
                            return true;
                        }
                    }
                    else
                    {
                        // 不可叠加，刷新持续时间
                        existingEffect.RemainingDuration = template.Duration;
                        Debug.Log($"[SkillEffectSystem] 效果刷新: {template.Name}");
                        return true;
                    }
                }
            }
            else
            {
                _activeEffects[targetId] = new List<ActiveEffect>();
            }

            // 添加新效果
            _activeEffects[targetId].Add(effect);
            Debug.Log($"[SkillEffectSystem] 施加效果: {template.Name} 到目标 {targetId}");

            // 立即应用效果属性变化
            ApplyEffectAttributes(targetId, effect, true);

            return true;
        }

        /// <summary>
        /// 移除效果
        /// </summary>
        public void RemoveEffect(ulong targetId, int effectId)
        {
            if (!_activeEffects.ContainsKey(targetId)) return;

            var effects = _activeEffects[targetId];
            var effectToRemove = effects.Find(e => e.Template.Id == effectId);
            
            if (effectToRemove != null)
            {
                // 移除属性变化
                ApplyEffectAttributes(targetId, effectToRemove, false);
                
                effects.Remove(effectToRemove);
                Debug.Log($"[SkillEffectSystem] 移除效果: {effectToRemove.Template.Name} 从目标 {targetId}");
                
                // 如果该目标没有其他效果，清理字典项
                if (effects.Count == 0)
                {
                    _activeEffects.Remove(targetId);
                }
            }
        }

        /// <summary>
        /// 更新所有效果
        /// </summary>
        public void UpdateEffects(float deltaTime)
        {
            var targetsToRemove = new List<ulong>();

            foreach (var kvp in _activeEffects)
            {
                var targetId = kvp.Key;
                var effects = kvp.Value;
                var effectsToRemove = new List<ActiveEffect>();

                foreach (var effect in effects)
                {
                    // 更新持续时间
                    effect.RemainingDuration -= deltaTime;
                    
                    // 处理周期性效果
                    if (effect.Template.TickInterval > 0)
                    {
                        effect.CurrentTicks += deltaTime;
                        if (effect.CurrentTicks >= effect.Template.TickInterval)
                        {
                            ProcessPeriodicEffect(targetId, effect);
                            effect.CurrentTicks = 0;
                        }
                    }

                    // 检查效果是否到期
                    if (effect.RemainingDuration <= 0)
                    {
                        effectsToRemove.Add(effect);
                    }
                }

                // 移除到期的效果
                foreach (var expiredEffect in effectsToRemove)
                {
                    ApplyEffectAttributes(targetId, expiredEffect, false);
                    effects.Remove(expiredEffect);
                    Debug.Log($"[SkillEffectSystem] 效果到期: {expiredEffect.Template.Name}");
                }

                // 如果该目标没有效果了，标记为待移除
                if (effects.Count == 0)
                {
                    targetsToRemove.Add(targetId);
                }
            }

            // 清理没有效果的目标
            foreach (var targetId in targetsToRemove)
            {
                _activeEffects.Remove(targetId);
            }
        }

        /// <summary>
        /// 处理周期性效果（如DoT、HoT）
        /// </summary>
        private void ProcessPeriodicEffect(ulong targetId, ActiveEffect effect)
        {
            switch (effect.Template.Type)
            {
                case EffectType.DoT:
                    // 造成持续伤害
                    var dotDamage = effect.Template.Value * effect.Stacks;
                    DealPeriodicDamage(targetId, dotDamage, effect.SourceEntityId, effect.Template.Name);
                    break;
                    
                case EffectType.HoT:
                    // 持续治疗
                    var hotHeal = effect.Template.Value * effect.Stacks;
                    ApplyPeriodicHealing(targetId, hotHeal, effect.Template.Name);
                    break;
            }
        }

        /// <summary>
        /// 应用或移除效果的属性变化
        /// </summary>
        private void ApplyEffectAttributes(ulong targetId, ActiveEffect effect, bool apply)
        {
            var multiplier = apply ? 1 : -1;
            var value = effect.Template.Value * effect.Stacks * multiplier;

            // 这里应该与角色属性系统集成
            // 示例：修改目标的属性值
            switch (effect.Template.Attribute)
            {
                case EffectAttribute.Attack:
                    ModifyAttribute(targetId, "Attack", value, effect.Template.IsPercent);
                    break;
                case EffectAttribute.Defense:
                    ModifyAttribute(targetId, "Defense", value, effect.Template.IsPercent);
                    break;
                case EffectAttribute.MoveSpeed:
                    ModifyAttribute(targetId, "MoveSpeed", value, effect.Template.IsPercent);
                    break;
                case EffectAttribute.AttackSpeed:
                    ModifyAttribute(targetId, "AttackSpeed", value, effect.Template.IsPercent);
                    break;
                case EffectAttribute.Stun:
                    SetControlState(targetId, ControlState.Stunned, apply);
                    break;
                case EffectAttribute.Silence:
                    SetControlState(targetId, ControlState.Silenced, apply);
                    break;
            }
        }

        /// <summary>
        /// 修改属性值
        /// </summary>
        private void ModifyAttribute(ulong targetId, string attributeName, float value, bool isPercent)
        {
            _attributeManager.ModifyAttribute(targetId, attributeName, value, isPercent);
        }

        /// <summary>
        /// 设置控制状态
        /// </summary>
        private void SetControlState(ulong targetId, ControlState state, bool enable)
        {
            if (enable)
            {
                _controlManager.ApplyControlState(targetId, state, 3.0f); // 默认3秒持续时间
            }
            else
            {
                _controlManager.RemoveControlState(targetId, state);
            }
        }

        /// <summary>
        /// 造成周期性伤害
        /// </summary>
        private void DealPeriodicDamage(ulong targetId, float damage, ulong sourceId, string effectName)
        {
            var actualDamage = _attributeManager.DealDamage(targetId, damage, sourceId);
            Debug.Log($"[SkillEffectSystem] {effectName} 造成周期性伤害: {actualDamage:F1} (目标: {targetId}, 来源: {sourceId})");
        }

        /// <summary>
        /// 应用周期性治疗
        /// </summary>
        private void ApplyPeriodicHealing(ulong targetId, float healAmount, string effectName)
        {
            var actualHeal = _attributeManager.Heal(targetId, healAmount);
            Debug.Log($"[SkillEffectSystem] {effectName} 提供周期性治疗: {actualHeal:F1} (目标: {targetId})");
        }

        /// <summary>
        /// 获取目标的所有活跃效果
        /// </summary>
        public List<ActiveEffect> GetActiveEffects(ulong targetId)
        {
            return _activeEffects.ContainsKey(targetId) ? 
                new List<ActiveEffect>(_activeEffects[targetId]) : 
                new List<ActiveEffect>();
        }

        /// <summary>
        /// 检查目标是否具有某种控制状态
        /// </summary>
        public bool HasControlState(ulong targetId, ControlState state)
        {
            var effects = GetActiveEffects(targetId);
            return effects.Exists(e => 
                (e.Template.Attribute == EffectAttribute.Stun && state == ControlState.Stunned) ||
                (e.Template.Attribute == EffectAttribute.Silence && state == ControlState.Silenced));
        }

        /// <summary>
        /// 清除目标的所有效果
        /// </summary>
        public void ClearAllEffects(ulong targetId)
        {
            if (_activeEffects.ContainsKey(targetId))
            {
                var effects = _activeEffects[targetId];
                foreach (var effect in effects)
                {
                    ApplyEffectAttributes(targetId, effect, false);
                }
                _activeEffects.Remove(targetId);
                Debug.Log($"[SkillEffectSystem] 清除目标 {targetId} 的所有效果");
            }
        }
    }

    /// <summary>
    /// 活跃效果
    /// </summary>
    public class ActiveEffect
    {
        public EffectTemplate Template { get; set; }
        public ulong SourceEntityId { get; set; }
        public float RemainingDuration { get; set; }
        public float CurrentTicks { get; set; }
        public int Stacks { get; set; }
    }

    /// <summary>
    /// 效果模板
    /// </summary>
    public class EffectTemplate
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public EffectType Type { get; set; }
        public EffectAttribute Attribute { get; set; }
        public float Value { get; set; }
        public float Duration { get; set; }
        public int MaxStacks { get; set; }
        public bool IsPercent { get; set; }
        public float TickInterval { get; set; } // 周期性效果的时间间隔（秒）
    }

    /// <summary>
    /// 效果影响的属性
    /// </summary>
    public enum EffectAttribute
    {
        Attack,         // 攻击力
        Defense,        // 防御力
        MoveSpeed,      // 移动速度
        AttackSpeed,    // 攻击速度
        Health,         // 生命值（用于DoT/HoT）
        Stun,           // 眩晕
        Silence         // 沉默
    }

    /// <summary>
    /// 技能效果控制状态
    /// </summary>
    public enum SkillEffectControlState
    {
        Stunned,    // 眩晕
        Silenced,   // 沉默
        Rooted,     // 定身
        Feared,     // 恐惧
        Confused    // 混乱
    }
}
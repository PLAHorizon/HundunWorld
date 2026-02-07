using System;
using System.Collections.Generic;
using FlaxEngine;
using HundunWorld.Game.ECS.Components;
using Game.Character.Attributes;

namespace HundunWorld.Game.Character
{
    /// <summary>
    /// 角色属性管理器实现
    /// </summary>
    public class CharacterAttributeManager : ICharacterAttributeManager
    {
        private static CharacterAttributeManager _instance;
        public static CharacterAttributeManager Instance => _instance ??= new CharacterAttributeManager();

        // 角色属性数据存储
        private readonly Dictionary<ulong, CharacterAttributeData> _characterAttributes;
        private readonly Dictionary<ulong, List<Action<string, float, float>>> _attributeChangeCallbacks;

        private CharacterAttributeManager()
        {
            _characterAttributes = new Dictionary<ulong, CharacterAttributeData>();
            _attributeChangeCallbacks = new Dictionary<ulong, List<Action<string, float, float>>>();
        }

        public CharacterStats GetBaseStats(ulong characterId)
        {
            if (!_characterAttributes.ContainsKey(characterId))
            {
                InitializeCharacter(characterId);
            }

            return _characterAttributes[characterId].BaseStats;
        }

        public CharacterStats GetCurrentStats(ulong characterId)
        {
            if (!_characterAttributes.ContainsKey(characterId))
            {
                InitializeCharacter(characterId);
            }

            var data = _characterAttributes[characterId];
            var currentStats = CloneStats(data.BaseStats);
            
            // 应用所有属性修饰符
            foreach (var modifier in data.AttributeModifiers)
            {
                ApplyAttributeModifier(currentStats, modifier);
            }

            return currentStats;
        }

        public void ModifyAttribute(ulong characterId, string attributeName, float value, bool isPercent = false)
        {
            if (!_characterAttributes.ContainsKey(characterId))
            {
                InitializeCharacter(characterId);
            }

            var data = _characterAttributes[characterId];
            var oldValue = GetAttributeValue(data.BaseStats, attributeName);
            
            // 添加属性修饰符
            data.AttributeModifiers.Add(new AttributeModifier
            {
                AttributeName = attributeName,
                Value = value,
                IsPercent = isPercent,
                Duration = -1 // 永久效果
            });

            var newValue = GetAttributeValue(GetCurrentStats(characterId), attributeName);
            
            // 触发属性变化事件
            TriggerAttributeChangeEvent(characterId, attributeName, oldValue, newValue);
        }

        public float GetCurrentHealth(ulong characterId)
        {
            if (!_characterAttributes.ContainsKey(characterId))
                return 0;

            return _characterAttributes[characterId].CurrentHealth;
        }

        public void SetCurrentHealth(ulong characterId, float health)
        {
            if (!_characterAttributes.ContainsKey(characterId))
                return;

            var data = _characterAttributes[characterId];
            var maxHealth = GetMaxHealth(characterId);
            var oldHealth = data.CurrentHealth;
            
            data.CurrentHealth = Mathf.Clamp(health, 0, maxHealth);
            
            // 触发生命值变化事件
            TriggerAttributeChangeEvent(characterId, "Health", oldHealth, data.CurrentHealth);
        }

        public float GetMaxHealth(ulong characterId)
        {
            var stats = GetCurrentStats(characterId);
            return stats.MaxHealth;
        }

        public bool IsAlive(ulong characterId)
        {
            return GetCurrentHealth(characterId) > 0;
        }

        public float DealDamage(ulong characterId, float damage, ulong attackerId = 0)
        {
            if (damage <= 0) return 0;

            var oldHealth = GetCurrentHealth(characterId);
            var actualDamage = Math.Min(damage, oldHealth);
            
            SetCurrentHealth(characterId, oldHealth - actualDamage);
            
            Debug.Log($"[CharacterAttributeManager] 角色 {characterId} 受到伤害: {actualDamage:F1} (剩余生命: {GetCurrentHealth(characterId):F1})");
            
            return actualDamage;
        }

        public float Heal(ulong characterId, float amount, ulong healerId = 0)
        {
            if (amount <= 0) return 0;

            var oldHealth = GetCurrentHealth(characterId);
            var maxHealth = GetMaxHealth(characterId);
            var actualHeal = Math.Min(amount, maxHealth - oldHealth);
            
            SetCurrentHealth(characterId, oldHealth + actualHeal);
            
            Debug.Log($"[CharacterAttributeManager] 角色 {characterId} 获得治疗: {actualHeal:F1} (当前生命: {GetCurrentHealth(characterId):F1})");
            
            return actualHeal;
        }

        public Vector3 GetPosition(ulong characterId)
        {
            // TODO: 与实际的位置系统集成
            return Vector3.Zero;
        }

        public void SetPosition(ulong characterId, Vector3 position)
        {
            // TODO: 与实际的位置系统集成
            Debug.Log($"[CharacterAttributeManager] 设置角色 {characterId} 位置: {position}");
        }

        public bool IsInRange(ulong characterId1, ulong characterId2, float range)
        {
            var pos1 = GetPosition(characterId1);
            var pos2 = GetPosition(characterId2);
            var distance = Vector3.Distance(pos1, pos2);
            return distance <= range;
        }

        public void SubscribeAttributeChanged(ulong characterId, Action<string, float, float> callback)
        {
            if (!_attributeChangeCallbacks.ContainsKey(characterId))
            {
                _attributeChangeCallbacks[characterId] = new List<Action<string, float, float>>();
            }
            
            _attributeChangeCallbacks[characterId].Add(callback);
        }

        public void UnsubscribeAttributeChanged(ulong characterId, Action<string, float, float> callback)
        {
            if (_attributeChangeCallbacks.ContainsKey(characterId))
            {
                _attributeChangeCallbacks[characterId].Remove(callback);
            }
        }

        /// <summary>
        /// 初始化角色属性
        /// </summary>
        private void InitializeCharacter(ulong characterId)
        {
            var baseStats = new CharacterStats
            {
                Name = $"Character_{characterId}",
                Level = 1,
                Attack = 100,
                MagicAttack = 80,
                Defense = 50,
                MagicDefense = 40,
                MaxHealth = 1000,
                CurrentHealth = 1000,
                MoveSpeed = 100,
                AttackSpeed = 100,
                CriticalRate = 0.1f,
                CriticalDamage = 0.5f,
                Element = WuxingElement.None
            };

            _characterAttributes[characterId] = new CharacterAttributeData
            {
                BaseStats = baseStats,
                CurrentHealth = baseStats.MaxHealth,
                AttributeModifiers = new List<AttributeModifier>()
            };

            Debug.Log($"[CharacterAttributeManager] 初始化角色 {characterId} 属性");
        }

        /// <summary>
        /// 应用属性修饰符
        /// </summary>
        private void ApplyAttributeModifier(CharacterStats stats, AttributeModifier modifier)
        {
            var currentValue = GetAttributeValue(stats, modifier.AttributeName);
            var newValue = modifier.IsPercent ? 
                currentValue * (1 + modifier.Value / 100) : 
                currentValue + modifier.Value;
            
            SetAttributeValue(stats, modifier.AttributeName, newValue);
        }

        /// <summary>
        /// 获取属性值
        /// </summary>
        private float GetAttributeValue(CharacterStats stats, string attributeName)
        {
            return attributeName.ToLower() switch
            {
                "attack" => stats.Attack,
                "magicattack" => stats.MagicAttack,
                "defense" => stats.Defense,
                "magicdefense" => stats.MagicDefense,
                "maxhealth" => stats.MaxHealth,
                "movespeed" => stats.MoveSpeed,
                "attackspeed" => stats.AttackSpeed,
                "criticalrate" => stats.CriticalRate,
                "criticaldamage" => stats.CriticalDamage,
                _ => 0
            };
        }

        /// <summary>
        /// 设置属性值
        /// </summary>
        private void SetAttributeValue(CharacterStats stats, string attributeName, float value)
        {
            switch (attributeName.ToLower())
            {
                case "attack":
                    stats.Attack = value;
                    break;
                case "magicattack":
                    stats.MagicAttack = value;
                    break;
                case "defense":
                    stats.Defense = value;
                    break;
                case "magicdefense":
                    stats.MagicDefense = value;
                    break;
                case "maxhealth":
                    stats.MaxHealth = value;
                    break;
                case "movespeed":
                    stats.MoveSpeed = value;
                    break;
                case "attackspeed":
                    stats.AttackSpeed = value;
                    break;
                case "criticalrate":
                    stats.CriticalRate = value;
                    break;
                case "criticaldamage":
                    stats.CriticalDamage = value;
                    break;
            }
        }

        /// <summary>
        /// 克隆角色属性
        /// </summary>
        private CharacterStats CloneStats(CharacterStats original)
        {
            return new CharacterStats
            {
                Name = original.Name,
                Level = original.Level,
                Attack = original.Attack,
                MagicAttack = original.MagicAttack,
                Defense = original.Defense,
                MagicDefense = original.MagicDefense,
                MaxHealth = original.MaxHealth,
                CurrentHealth = original.CurrentHealth,
                MoveSpeed = original.MoveSpeed,
                AttackSpeed = original.AttackSpeed,
                CriticalRate = original.CriticalRate,
                CriticalDamage = original.CriticalDamage,
                Element = original.Element
            };
        }

        /// <summary>
        /// 触发属性变化事件
        /// </summary>
        private void TriggerAttributeChangeEvent(ulong characterId, string attributeName, float oldValue, float newValue)
        {
            if (_attributeChangeCallbacks.ContainsKey(characterId))
            {
                foreach (var callback in _attributeChangeCallbacks[characterId])
                {
                    try
                    {
                        callback?.Invoke(attributeName, oldValue, newValue);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[CharacterAttributeManager] 属性变化回调执行异常: {ex.Message}");
                    }
                }
            }
        }
    }

    /// <summary>
    /// 角色属性数据
    /// </summary>
    public class CharacterAttributeData
    {
        public CharacterStats BaseStats { get; set; }
        public float CurrentHealth { get; set; }
        public List<AttributeModifier> AttributeModifiers { get; set; }
    }

    /// <summary>
    /// 属性修饰符
    /// </summary>
    public class AttributeModifier
    {
        public string AttributeName { get; set; }
        public float Value { get; set; }
        public bool IsPercent { get; set; }
        public float Duration { get; set; } // -1表示永久
        public float RemainingTime { get; set; }
    }
}
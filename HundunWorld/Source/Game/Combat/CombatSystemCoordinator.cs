using FlaxEngine;
using System;
using System.Collections.Generic;
using HundunWorld.Game.Character;
using HundunWorld.Game.Combat.Effects;

namespace HundunWorld.Game.Combat
{
    /// <summary>
    /// 战斗系统协调器
    /// 负责整合所有战斗相关系统，提供统一的战斗管理接口
    /// </summary>
    public class CombatSystemCoordinator
    {
        private static CombatSystemCoordinator _instance;
        public static CombatSystemCoordinator Instance => _instance ??= new CombatSystemCoordinator();

        private readonly CombatSystemManager _combatManager;
        private readonly SkillEffectSystem _effectSystem;
        private readonly CharacterAttributeManager _attributeManager;
        private readonly ControlStateManager _controlManager;
        private readonly List<ulong> _activeEntities;

        private CombatSystemCoordinator()
        {
            _combatManager = CombatSystemManager.Instance;
            _effectSystem = SkillEffectSystem.Instance;
            _attributeManager = CharacterAttributeManager.Instance;
            _controlManager = ControlStateManager.Instance;
            _activeEntities = new List<ulong>();
        }

        /// <summary>
        /// 初始化战斗系统
        /// </summary>
        public void Initialize()
        {
            Debug.Log("[CombatSystemCoordinator] 战斗系统初始化完成");
        }

        /// <summary>
        /// 更新战斗系统（每帧调用）
        /// </summary>
        public void Update(float deltaTime)
        {
            // 更新战斗管理器
            _combatManager.Update(deltaTime);
            
            // 更新效果系统
            _effectSystem.UpdateEffects(deltaTime);
            
            // 更新控制状态管理器
            _controlManager.Update(deltaTime);
            
            // 清理过期实体
            CleanupExpiredEntities();
        }

        /// <summary>
        /// 注册战斗实体
        /// </summary>
        public void RegisterEntity(ulong entityId)
        {
            if (!_activeEntities.Contains(entityId))
            {
                _activeEntities.Add(entityId);
                Debug.Log($"[CombatSystemCoordinator] 注册战斗实体: {entityId}");
            }
        }

        /// <summary>
        /// 注销战斗实体
        /// </summary>
        public void UnregisterEntity(ulong entityId)
        {
            if (_activeEntities.Remove(entityId))
            {
                // 清理该实体的所有战斗数据
                _effectSystem.ClearAllEffects(entityId);
                _controlManager.ClearAllControlStates(entityId);
                Debug.Log($"[CombatSystemCoordinator] 注销战斗实体: {entityId}");
            }
        }

        /// <summary>
        /// 处理攻击动作
        /// </summary>
        public CombatActionResult ProcessAttack(AttackAction attack)
        {
            return _combatManager.ProcessAttack(attack);
        }

        /// <summary>
        /// 应用技能效果
        /// </summary>
        public bool ApplyEffect(ulong targetId, int effectTemplateId, ulong sourceId = 0)
        {
            return _effectSystem.ApplyEffect(targetId, effectTemplateId, sourceId);
        }

        /// <summary>
        /// 获取角色当前属性
        /// </summary>
        public HundunWorld.Game.Character.CharacterStats GetCharacterStats(ulong characterId)
        {
            return _attributeManager.GetCurrentStats(characterId);
        }

        /// <summary>
        /// 获取角色战斗状态
        /// </summary>
        public CombatStateInfo GetCombatStateInfo(ulong characterId)
        {
            return _combatManager.GetCombatStateInfo(characterId);
        }

        /// <summary>
        /// 获取角色控制状态
        /// </summary>
        public List<ActiveControlState> GetActiveControlStates(ulong characterId)
        {
            return _controlManager.GetActiveControlStates(characterId);
        }

        /// <summary>
        /// 获取角色活跃效果
        /// </summary>
        public List<ActiveEffect> GetActiveEffects(ulong characterId)
        {
            return _effectSystem.GetActiveEffects(characterId);
        }

        /// <summary>
        /// 检查角色是否具有特定控制状态
        /// </summary>
        public bool HasControlState(ulong characterId, ControlState state)
        {
            return _controlManager.HasControlState(characterId, state);
        }

        /// <summary>
        /// 对角色造成伤害
        /// </summary>
        public float DealDamage(ulong characterId, float damage, ulong attackerId = 0)
        {
            return _attributeManager.DealDamage(characterId, damage, attackerId);
        }

        /// <summary>
        /// 治疗角色
        /// </summary>
        public float Heal(ulong characterId, float amount, ulong healerId = 0)
        {
            return _attributeManager.Heal(characterId, amount, healerId);
        }

        /// <summary>
        /// 检查角色是否存活
        /// </summary>
        public bool IsAlive(ulong characterId)
        {
            return _attributeManager.IsAlive(characterId);
        }

        /// <summary>
        /// 获取战斗统计信息
        /// </summary>
        public CombatStatistics GetStatistics()
        {
            return new CombatStatistics
            {
                ActiveEntities = _activeEntities.Count,
                TotalEffects = GetTotalActiveEffects(),
                TotalControlStates = GetTotalActiveControlStates()
            };
        }

        /// <summary>
        /// 清理所有战斗数据
        /// </summary>
        public void Cleanup()
        {
            foreach (var entityId in _activeEntities)
            {
                _effectSystem.ClearAllEffects(entityId);
                _controlManager.ClearAllControlStates(entityId);
            }
            
            _activeEntities.Clear();
            Debug.Log("[CombatSystemCoordinator] 战斗系统清理完成");
        }

        /// <summary>
        /// 清理过期实体
        /// </summary>
        private void CleanupExpiredEntities()
        {
            var expiredEntities = new List<ulong>();
            
            foreach (var entityId in _activeEntities)
            {
                // 检查实体是否仍然有效（这里可以根据实际需求实现）
                if (!IsEntityValid(entityId))
                {
                    expiredEntities.Add(entityId);
                }
            }
            
            foreach (var entityId in expiredEntities)
            {
                UnregisterEntity(entityId);
            }
        }

        /// <summary>
        /// 检查实体是否有效
        /// </summary>
        private bool IsEntityValid(ulong entityId)
        {
            // 检查实体是否已在属性管理器中注册
            return _attributeManager.HasCharacter(entityId);
        }

        /// <summary>
        /// 获取总的活跃效果数量
        /// </summary>
        private int GetTotalActiveEffects()
        {
            int total = 0;
            foreach (var entityId in _activeEntities)
            {
                total += _effectSystem.GetActiveEffects(entityId).Count;
            }
            return total;
        }

        /// <summary>
        /// 获取总的活跃控制状态数量
        /// </summary>
        private int GetTotalActiveControlStates()
        {
            int total = 0;
            foreach (var entityId in _activeEntities)
            {
                total += _controlManager.GetActiveControlStates(entityId).Count;
            }
            return total;
        }
    }

    /// <summary>
    /// 战斗统计信息
    /// </summary>
    public class CombatStatistics
    {
        public int ActiveEntities { get; set; }
        public int TotalEffects { get; set; }
        public int TotalControlStates { get; set; }
        
        public override string ToString()
        {
            return $"活跃实体: {ActiveEntities}, 总效果数: {TotalEffects}, 总控制状态: {TotalControlStates}";
        }
    }
}
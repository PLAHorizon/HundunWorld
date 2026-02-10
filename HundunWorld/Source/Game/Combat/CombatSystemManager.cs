using FlaxEngine;
using System;
using System.Collections.Generic;
using HundunWorld.Game.ECS.Components;
using HundunWorld.Game.Combat.Effects;
using HundunWorld.Game.Character;
using Game.Character.Attributes;
using Horizon.Game.Message.Enums;

namespace HundunWorld.Game.Combat
{
    /// <summary>
    /// 战斗系统管理器
    /// 协调伤害计算、效果管理和战斗状态
    /// </summary>
    public class CombatSystemManager
    {
        private static CombatSystemManager _instance;
        public static CombatSystemManager Instance => _instance ??= new CombatSystemManager();

        private readonly DamageCalculator _damageCalculator;
        private readonly SkillEffectSystem _effectSystem;
        private readonly ICharacterAttributeManager _attributeManager;
        private readonly IControlStateManager _controlManager;
        private readonly Dictionary<ulong, CombatState> _combatStates;
        private readonly List<PendingAction> _pendingActions;
        private readonly Dictionary<int, SkillInfo> _skillCache;

        private CombatSystemManager()
        {
            _damageCalculator = DamageCalculator.Instance;
            _effectSystem = SkillEffectSystem.Instance;
            _attributeManager = CharacterAttributeManager.Instance;
            _controlManager = ControlStateManager.Instance;
            _combatStates = new Dictionary<ulong, CombatState>();
            _pendingActions = new List<PendingAction>();
            _skillCache = new Dictionary<int, SkillInfo>();
        }

        /// <summary>
        /// 注册技能信息到缓存
        /// </summary>
        public void RegisterSkill(SkillInfo skill)
        {
            _skillCache[skill.Id] = skill;
        }

        /// <summary>
        /// 处理攻击动作
        /// </summary>
        public CombatActionResult ProcessAttack(AttackAction attack)
        {
            var result = new CombatActionResult();

            try
            {
                // 1. 验证攻击合法性
                if (!ValidateAttack(attack))
                {
                    result.IsSuccess = false;
                    result.ErrorMessage = "攻击条件不满足";
                    return result;
                }

                // 2. 检查冷却时间和资源消耗
                if (!CheckResourceCost(attack.AttackerId, attack.Skill))
                {
                    result.IsSuccess = false;
                    result.ErrorMessage = "资源不足或技能冷却中";
                    return result;
                }

                // 3. 计算伤害
                var attackerStats = ConvertToCombatStats(GetCharacterStats(attack.AttackerId));
                var defenderStats = ConvertToCombatStats(GetCharacterStats(attack.DefenderId));
                var previousSkill = GetPreviousSkill(attack.AttackerId);
                
                var damageResult = _damageCalculator.CalculateDamage(
                    attackerStats, 
                    defenderStats, 
                    attack.Skill,
                    previousSkill);

                result.DamageResult = damageResult;
                result.ActualDamage = damageResult.FinalDamage;

                // 4. 应用伤害到目标
                ApplyDamage(attack.DefenderId, result.ActualDamage, attack.AttackerId);

                // 5. 应用技能效果
                ApplySkillEffects(attack);

                // 6. 更新战斗状态
                UpdateCombatState(attack.AttackerId, attack.DefenderId, attack.Skill);

                // 7. 消耗资源和设置冷却
                ConsumeResources(attack.AttackerId, attack.Skill);

                result.IsSuccess = true;
                Debug.Log($"[CombatSystemManager] 攻击成功: {attackerStats.Name} -> {defenderStats.Name}, 伤害: {result.ActualDamage:F1}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CombatSystemManager] 处理攻击时出错: {ex.Message}");
                result.IsSuccess = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        /// <summary>
        /// 验证攻击合法性
        /// </summary>
        private bool ValidateAttack(AttackAction attack)
        {
            // 检查目标是否存在且存活
            if (!IsEntityAlive(attack.DefenderId))
            {
                Debug.LogWarning($"[CombatSystemManager] 目标 {attack.DefenderId} 已死亡或不存在");
                return false;
            }

            // 检查距离
            if (!IsInRange(attack.AttackerId, attack.DefenderId, attack.Skill.Range))
            {
                Debug.LogWarning($"[CombatSystemManager] 目标超出技能范围");
                return false;
            }

            // 检查控制状态
            if (_effectSystem.HasControlState(attack.AttackerId, ControlState.Stunned))
            {
                Debug.LogWarning($"[CombatSystemManager] 攻击者处于眩晕状态");
                return false;
            }

            if (_effectSystem.HasControlState(attack.AttackerId, ControlState.Silenced) && 
                attack.Skill.Type == SkillType.ActiveAttack)
            {
                Debug.LogWarning($"[CombatSystemManager] 攻击者处于沉默状态，无法使用攻击技能");
                return false;
            }

            return true;
        }

        /// <summary>
        /// 检查资源消耗
        /// </summary>
        private bool CheckResourceCost(ulong entityId, SkillInfo skill)
        {
            var stats = GetCharacterStats(entityId);
            
            // 检查能量/法力值
            float currentEnergy = _attributeManager.GetCurrentEnergy(entityId);
            if (currentEnergy < skill.EnergyCost)
            {
                return false;
            }

            // 检查技能冷却
            var combatState = GetCombatState(entityId);
            if (combatState.SkillCooldowns.ContainsKey(skill.Id) && 
                combatState.SkillCooldowns[skill.Id] > 0)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// 应用伤害到目标
        /// </summary>
        private void ApplyDamage(ulong targetId, float damage, ulong attackerId)
        {
            var actualDamage = _attributeManager.DealDamage(targetId, damage, attackerId);
            Debug.Log($"[CombatSystemManager] 对目标 {targetId} 造成伤害: {actualDamage:F1} (来源: {attackerId})");
            
            // 检查是否死亡
            if (!_attributeManager.IsAlive(targetId))
            {
                HandleEntityDeath(targetId, attackerId);
            }
        }

        /// <summary>
        /// 应用技能效果
        /// </summary>
        private void ApplySkillEffects(AttackAction attack)
        {
            // 应用技能附带的效果
            foreach (var effectId in attack.Skill.Effects)
            {
                _effectSystem.ApplyEffect(attack.DefenderId, effectId, attack.AttackerId);
            }

            // 应用攻击者可能获得的效果（如吸血、增益等）
            foreach (var selfEffectId in attack.Skill.SelfEffects)
            {
                _effectSystem.ApplyEffect(attack.AttackerId, selfEffectId, attack.AttackerId);
            }
        }

        /// <summary>
        /// 更新战斗状态
        /// </summary>
        private void UpdateCombatState(ulong attackerId, ulong defenderId, SkillInfo skill)
        {
            // 更新攻击者状态
            var attackerState = GetCombatState(attackerId);
            attackerState.LastCombatTime = Time.GameTime;
            attackerState.IsInCombat = true;
            attackerState.SkillCooldowns[skill.Id] = skill.Cooldown;
            attackerState.PreviousSkillId = skill.Id;

            // 更新防御者状态
            var defenderState = GetCombatState(defenderId);
            defenderState.LastCombatTime = Time.GameTime;
            defenderState.IsInCombat = true;
            defenderState.LastAttackerId = attackerId;

            // 更新连击计数
            if (skill.Type == SkillType.ActiveAttack)
            {
                if (Time.GameTime - attackerState.LastAttackTime < 3.0f) // 3秒内的连击
                {
                    attackerState.ComboCount++;
                }
                else
                {
                    attackerState.ComboCount = 1;
                }
                attackerState.LastAttackTime = Time.GameTime;
            }
        }

        /// <summary>
        /// 消耗资源
        /// </summary>
        private void ConsumeResources(ulong entityId, SkillInfo skill)
        {
            _attributeManager.ConsumeEnergy(entityId, skill.EnergyCost);
            Debug.Log($"[CombatSystemManager] 消耗资源: {skill.EnergyCost} (实体: {entityId})");
        }

        /// <summary>
        /// 更新战斗系统
        /// </summary>
        public void Update(float deltaTime)
        {
            // 更新效果系统
            _effectSystem.UpdateEffects(deltaTime);

            // 更新技能冷却
            UpdateSkillCooldowns(deltaTime);

            // 清理过期的战斗状态
            CleanupExpiredCombatStates();
        }

        /// <summary>
        /// 更新技能冷却时间
        /// </summary>
        private void UpdateSkillCooldowns(float deltaTime)
        {
            foreach (var kvp in _combatStates)
            {
                var state = kvp.Value;
                var cooldownsToRemove = new List<int>();

                foreach (var cooldown in state.SkillCooldowns)
                {
                    var remaining = cooldown.Value - deltaTime;
                    if (remaining <= 0)
                    {
                        cooldownsToRemove.Add(cooldown.Key);
                    }
                    else
                    {
                        state.SkillCooldowns[cooldown.Key] = remaining;
                    }
                }

                // 移除已完成冷却的技能
                foreach (var skillId in cooldownsToRemove)
                {
                    state.SkillCooldowns.Remove(skillId);
                }
            }
        }

        /// <summary>
        /// 清理过期的战斗状态
        /// </summary>
        private void CleanupExpiredCombatStates()
        {
            var currentTime = Time.GameTime;
            var statesToRemove = new List<ulong>();

            foreach (var kvp in _combatStates)
            {
                var state = kvp.Value;
                // 如果10秒内没有战斗行为，则退出战斗状态
                if (currentTime - state.LastCombatTime > 10.0f)
                {
                    state.IsInCombat = false;
                    state.ComboCount = 0;
                    
                    // 如果长时间没有战斗，清理状态
                    if (currentTime - state.LastCombatTime > 30.0f)
                    {
                        statesToRemove.Add(kvp.Key);
                    }
                }
            }

            foreach (var entityId in statesToRemove)
            {
                _combatStates.Remove(entityId);
                Debug.Log($"[CombatSystemManager] 清理实体 {entityId} 的战斗状态");
            }
        }

        /// <summary>
        /// 获取战斗状态
        /// </summary>
        private CombatState GetCombatState(ulong entityId)
        {
            if (!_combatStates.ContainsKey(entityId))
            {
                _combatStates[entityId] = new CombatState();
            }
            return _combatStates[entityId];
        }

        /// <summary>
        /// 获取角色属性（需要与实际系统集成）
        /// </summary>
        private HundunWorld.Game.Character.CharacterStats GetCharacterStats(ulong entityId)
        {
            return _attributeManager.GetCurrentStats(entityId);
        }

        /// <summary>
        /// 将Character命名空间的CharacterStats转换为Combat命名空间的CharacterStats
        /// </summary>
        private HundunWorld.Game.Combat.CharacterStats ConvertToCombatStats(HundunWorld.Game.Character.CharacterStats charStats)
        {
            return new HundunWorld.Game.Combat.CharacterStats
            {
                Name = charStats.Name,
                Level = charStats.Level,
                Attack = charStats.Attack,
                MagicAttack = charStats.MagicAttack,
                Defense = charStats.Defense,
                MagicDefense = charStats.MagicDefense,
                // 注意：Character命名空间的CharacterStats没有五行属性，所以使用默认值
                MetalAttack = 0,
                WoodAttack = 0,
                WaterAttack = 0,
                FireAttack = 0,
                EarthAttack = 0,
                MetalDefense = 0,
                WoodDefense = 0,
                WaterDefense = 0,
                FireDefense = 0,
                EarthDefense = 0,
                PhysicalDamageBonus = 0,
                MagicDamageBonus = 0,
                ElementalDamageBonus = 0,
                PhysicalResistance = 0,
                MagicResistance = 0,
                ElementalResistance = 0,
                ElementalDefense = 0,
                CriticalRate = charStats.CriticalRate,
                CriticalDamage = charStats.CriticalDamage,
                CriticalResistance = 0,
                Element = WuxingElement.None
            };
        }

        /// <summary>
        /// 获取上一个使用的技能
        /// </summary>
        private SkillInfo GetPreviousSkill(ulong entityId)
        {
            var state = GetCombatState(entityId);
            if (state.PreviousSkillId > 0 && _skillCache.TryGetValue(state.PreviousSkillId, out var skill))
            {
                return skill;
            }
            return null;
        }

        /// <summary>
        /// 检查实体是否存活
        /// </summary>
        private bool IsEntityAlive(ulong entityId)
        {
            return _attributeManager.IsAlive(entityId);
        }

        /// <summary>
        /// 检查是否在范围内
        /// </summary>
        private bool IsInRange(ulong attackerId, ulong defenderId, float range)
        {
            return _attributeManager.IsInRange(attackerId, defenderId, range);
        }

        /// <summary>
        /// 检查实体是否死亡
        /// </summary>
        private bool IsEntityDead(ulong entityId, float damage)
        {
            var currentHealth = _attributeManager.GetCurrentHealth(entityId);
            return (currentHealth - damage) <= 0;
        }

        /// <summary>
        /// 实体死亡事件
        /// </summary>
        public event Action<ulong, ulong> EntityDied;

        /// <summary>
        /// 处理实体死亡
        /// </summary>
        private void HandleEntityDeath(ulong entityId, ulong killerId)
        {
            Debug.Log($"[CombatSystemManager] 实体 {entityId} 被 {killerId} 击杀");
            
            // 移除所有效果
            _effectSystem.ClearAllEffects(entityId);
            
            // 清理战斗状态
            if (_combatStates.ContainsKey(entityId))
            {
                _combatStates.Remove(entityId);
            }

            // 触发死亡事件通知外部系统（掉落、经验、UI等）
            EntityDied?.Invoke(entityId, killerId);
        }

        /// <summary>
        /// 获取实体的战斗状态信息
        /// </summary>
        public CombatStateInfo GetCombatStateInfo(ulong entityId)
        {
            var state = GetCombatState(entityId);
            return new CombatStateInfo
            {
                IsInCombat = state.IsInCombat,
                ComboCount = state.ComboCount,
                ActiveCooldowns = new Dictionary<int, float>(state.SkillCooldowns)
            };
        }
    }

    /// <summary>
    /// 攻击动作
    /// </summary>
    public class AttackAction
    {
        public ulong AttackerId { get; set; }
        public ulong DefenderId { get; set; }
        public SkillInfo Skill { get; set; }
        public Vector3 AttackPosition { get; set; }
    }

    /// <summary>
    /// 战斗行动结果
    /// </summary>
    public class CombatActionResult
    {
        public bool IsSuccess { get; set; }
        public string ErrorMessage { get; set; }
        public float ActualDamage { get; set; }
        public DamageCalculationResult DamageResult { get; set; }
        public List<int> AppliedEffects { get; set; } = new List<int>();
    }

    /// <summary>
    /// 战斗状态
    /// </summary>
    public class CombatState
    {
        public bool IsInCombat { get; set; }
        public float LastCombatTime { get; set; }
        public float LastAttackTime { get; set; }
        public int ComboCount { get; set; }
        public ulong LastAttackerId { get; set; }
        public int PreviousSkillId { get; set; }
        public Dictionary<int, float> SkillCooldowns { get; set; } = new Dictionary<int, float>();
    }

    /// <summary>
    /// 战斗状态信息（对外暴露）
    /// </summary>
    public class CombatStateInfo
    {
        public bool IsInCombat { get; set; }
        public int ComboCount { get; set; }
        public Dictionary<int, float> ActiveCooldowns { get; set; }
    }
}
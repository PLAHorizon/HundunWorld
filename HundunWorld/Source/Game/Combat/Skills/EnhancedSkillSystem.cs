using FlaxEngine;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Game.Combat.Skills; // 添加技能相关的命名空间引用
using Game.Character.Attributes; // 添加角色属性组件命名空间

namespace HundunWorld.Game.Combat.Skills
{
    /// <summary>
    /// 增强技能系统管理器
    /// 负责技能的注册、管理和执行
    /// </summary>
    public class EnhancedSkillSystem : Script
    {
        #region 技能系统配置
        [Header("技能系统设置")]
        [Tooltip("最大同时施法技能数量")]
        public int MaxConcurrentSkills = 3;
        
        [Tooltip("技能打断优先级阈值")]
        public int InterruptPriorityThreshold = 50;
        
        [Tooltip("全局冷却时间（秒）")]
        public float GlobalCooldown = 0.1f;
        
        [Tooltip("技能效果持续时间倍率")]
        public float EffectDurationMultiplier = 1.0f;
        #endregion

        #region 技能管理
        private Dictionary<int, SkillBase> _registeredSkills;
        private Dictionary<int, SkillInstance> _activeSkills;
        private Queue<SkillExecutionRequest> _skillQueue;
        private float _globalCooldownTimer = 0f;
        private int _concurrentSkillCount = 0;
        #endregion

        #region 技能效果管理
        private List<SkillEffect> _activeEffects;
        private Dictionary<Actor, List<SkillEffect>> _actorEffects;
        #endregion

        #region 引用
        private PlayerController _playerController;
        private CharacterAttributesComponent _characterAttributes;
        #endregion

        public override void OnStart()
        {
            InitializeSkillSystem();
            Debug.Log("[EnhancedSkill] 技能系统已初始化");
        }

        public override void OnUpdate()
        {
            UpdateSkillTimers();
            UpdateActiveSkills();
            UpdateSkillEffects();
            ProcessSkillQueue();
        }

        #region 初始化
        private void InitializeSkillSystem()
        {
            _registeredSkills = new Dictionary<int, SkillBase>();
            _activeSkills = new Dictionary<int, SkillInstance>();
            _skillQueue = new Queue<SkillExecutionRequest>();
            _activeEffects = new List<SkillEffect>();
            _actorEffects = new Dictionary<Actor, List<SkillEffect>>();
            
            _playerController = Actor.Parent?.GetScript<PlayerController>();
            _characterAttributes = Actor.Parent?.GetScript<CharacterAttributesComponent>();
            
            RegisterDefaultSkills();
        }

        private void RegisterDefaultSkills()
        {
            // 注册基础技能模板
            RegisterSkillTemplate(1001, "基础攻击", SkillType.ActiveAttack, 1.0f, 0f, 0.5f);
            RegisterSkillTemplate(1002, "重击", SkillType.ActiveAttack, 1.5f, 20f, 1.2f);
            RegisterSkillTemplate(1003, "旋风斩", SkillType.ActiveAttack, 0.8f, 30f, 1.5f);
            RegisterSkillTemplate(2001, "治疗术", SkillType.Support, 0f, 25f, 2.0f);
            RegisterSkillTemplate(3001, "冲锋", SkillType.Dash, 0f, 15f, 0.3f);
        }

        private void RegisterSkillTemplate(int skillId, string name, SkillType type, 
            float damageMult, float energyCost, float cooldown)
        {
            var skillData = new SkillData
            {
                SkillId = skillId,
                SkillName = name,
                Type = type,
                DamageMultiplier = damageMult,
                EnergyCost = energyCost,
                Cooldown = cooldown,
                CastTime = type == SkillType.Dash ? 0.1f : 0.5f,
                Range = type == SkillType.Support ? 15f : 5f
            };

            var skill = new BasicSkill(skillData);
            _registeredSkills[skillId] = skill;
        }
        #endregion

        #region 技能执行管理
        /// <summary>
        /// 请求执行技能
        /// </summary>
        public async Task<bool> RequestSkillExecution(int skillId, Actor target = null, Vector3? position = null)
        {
            if (!_registeredSkills.TryGetValue(skillId, out var skill))
            {
                Debug.LogWarning($"[EnhancedSkill] 未找到技能: {skillId}");
                return false;
            }

            // 检查全局冷却
            if (_globalCooldownTimer > 0)
            {
                // 加入队列等待
                _skillQueue.Enqueue(new SkillExecutionRequest(skillId, target, position));
                return true;
            }

            // 检查并发限制
            if (_concurrentSkillCount >= MaxConcurrentSkills)
            {
                _skillQueue.Enqueue(new SkillExecutionRequest(skillId, target, position));
                return true;
            }

            // 执行技能
            return await ExecuteSkill(skill, target, position);
        }

        private async Task<bool> ExecuteSkill(SkillBase skill, Actor target, Vector3? position)
        {
            try
            {
                // 创建技能实例
                var skillInstance = new SkillInstance
                {
                    Skill = skill,
                    Target = target,
                    TargetPosition = position ?? Vector3.Zero,
                    StartTime = Time.GameTime,
                    State = SkillInstanceState.Casting
                };

                // 添加到活跃技能列表
                _activeSkills[skill.Data.SkillId] = skillInstance;
                _concurrentSkillCount++;

                // 应用全局冷却
                _globalCooldownTimer = GlobalCooldown;

                // 执行施法过程
                await PerformSkillCast(skillInstance);
                
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EnhancedSkill] 技能执行失败: {ex.Message}");
                _concurrentSkillCount = Math.Max(0, _concurrentSkillCount - 1);
                return false;
            }
        }

        private async Task PerformSkillCast(SkillInstance instance)
        {
            var skill = instance.Skill;
            
            // 播放施法开始效果
            PlayCastStartEffects(instance);
            
            // 等待施法时间
            if (skill.Data.CastTime > 0)
            {
                await Task.Delay((int)(skill.Data.CastTime * 1000));
            }
            
            // 完成施法
            instance.State = SkillInstanceState.Executing;
            await CompleteSkillExecution(instance);
        }

        private async Task CompleteSkillExecution(SkillInstance instance)
        {
            var skill = instance.Skill;
            
            // 执行技能效果
            skill.ExecuteSkillPublic(instance.Target);
            
            // 播放完成效果
            PlayCastCompleteEffects(instance);
            
            // 设置冷却
            instance.State = SkillInstanceState.OnCooldown;
            instance.CooldownEndTime = Time.GameTime + skill.Data.Cooldown;
            
            // 减少并发计数
            _concurrentSkillCount = Math.Max(0, _concurrentSkillCount - 1);
            
            Debug.Log($"[EnhancedSkill] 技能执行完成: {skill.Data.SkillName}");
        }

        private void ProcessSkillQueue()
        {
            if (_skillQueue.Count == 0 || _globalCooldownTimer > 0 || _concurrentSkillCount >= MaxConcurrentSkills)
                return;

            var request = _skillQueue.Dequeue();
            _ = RequestSkillExecution(request.SkillId, request.Target, request.Position);
        }
        #endregion

        #region 技能效果管理
        /// <summary>
        /// 应用技能效果到目标
        /// </summary>
        public void ApplySkillEffect(Actor target, SkillEffect effect)
        {
            if (target == null) return;

            // 添加到全局效果列表
            _activeEffects.Add(effect);
            
            // 添加到目标效果列表
            if (!_actorEffects.ContainsKey(target))
            {
                _actorEffects[target] = new List<SkillEffect>();
            }
            _actorEffects[target].Add(effect);
            
            // 应用效果
            effect.Apply(target);
            Debug.Log($"[EnhancedSkill] 应用效果: {effect.EffectName} 到 {target.Name}");
        }

        private void UpdateSkillEffects()
        {
            // 更新所有效果的持续时间
            for (int i = _activeEffects.Count - 1; i >= 0; i--)
            {
                var effect = _activeEffects[i];
                effect.Update(Time.DeltaTime);
                
                if (effect.IsExpired)
                {
                    effect.Remove();
                    _activeEffects.RemoveAt(i);
                }
            }
            
            // 清理无效的目标效果引用
            var keysToRemove = new List<Actor>();
            foreach (var kvp in _actorEffects)
            {
                kvp.Value.RemoveAll(e => e.IsExpired);
                if (kvp.Value.Count == 0)
                {
                    keysToRemove.Add(kvp.Key);
                }
            }
            
            foreach (var key in keysToRemove)
            {
                _actorEffects.Remove(key);
            }
        }

        /// <summary>
        /// 获取目标身上的所有效果
        /// </summary>
        public List<SkillEffect> GetActorEffects(Actor actor)
        {
            return _actorEffects.TryGetValue(actor, out var effects) ? new List<SkillEffect>(effects) : new List<SkillEffect>();
        }

        /// <summary>
        /// 移除目标的特定类型效果
        /// </summary>
        public void RemoveEffectsByType(Actor actor, SkillEffectType effectType)
        {
            if (_actorEffects.TryGetValue(actor, out var effects))
            {
                effects.RemoveAll(effect =>
                {
                    if (effect.EffectType == effectType)
                    {
                        effect.Remove();
                        _activeEffects.Remove(effect);
                        return true;
                    }
                    return false;
                });
            }
        }
        #endregion

        #region 状态更新
        private void UpdateSkillTimers()
        {
            // 更新全局冷却
            if (_globalCooldownTimer > 0)
            {
                _globalCooldownTimer -= Time.DeltaTime;
            }
            
            // 更新技能冷却
            var expiredSkills = new List<int>();
            foreach (var kvp in _activeSkills)
            {
                var instance = kvp.Value;
                if (instance.State == SkillInstanceState.OnCooldown)
                {
                    if (Time.GameTime >= instance.CooldownEndTime)
                    {
                        expiredSkills.Add(kvp.Key);
                    }
                }
            }
            
            foreach (var skillId in expiredSkills)
            {
                _activeSkills.Remove(skillId);
            }
        }

        private void UpdateActiveSkills()
        {
            // 这里可以添加对正在进行的技能的实时更新
            // 比如追踪弹道、持续伤害等
        }
        #endregion

        #region 效果和视觉反馈
        private void PlayCastStartEffects(SkillInstance instance)
        {
            // 播放施法开始特效
            // 这里应该调用实际的特效系统
            Debug.Log($"[EnhancedSkill] 开始施法: {instance.Skill.Data.SkillName}");
        }

        private void PlayCastCompleteEffects(SkillInstance instance)
        {
            // 播放施法完成特效
            Debug.Log($"[EnhancedSkill] 完成施法: {instance.Skill.Data.SkillName}");
        }
        #endregion

        #region 公共接口
        public bool IsSkillAvailable(int skillId)
        {
            return _registeredSkills.ContainsKey(skillId) && 
                   !_activeSkills.ContainsKey(skillId) &&
                   _globalCooldownTimer <= 0;
        }

        public float GetSkillCooldownRemaining(int skillId)
        {
            if (_activeSkills.TryGetValue(skillId, out var instance))
            {
                if (instance.State == SkillInstanceState.OnCooldown)
                {
                    return Math.Max(0, instance.CooldownEndTime - Time.GameTime);
                }
            }
            return 0f;
        }

        public Dictionary<int, float> GetAllSkillCooldowns()
        {
            var cooldowns = new Dictionary<int, float>();
            foreach (var kvp in _activeSkills)
            {
                cooldowns[kvp.Key] = GetSkillCooldownRemaining(kvp.Key);
            }
            return cooldowns;
        }

        public int GetConcurrentSkillCount()
        {
            return _concurrentSkillCount;
        }

        public void CancelAllSkills()
        {
            _activeSkills.Clear();
            _concurrentSkillCount = 0;
            _skillQueue.Clear();
            Debug.Log("[EnhancedSkill] 所有技能已取消");
        }
        #endregion
    }

    #region 辅助类定义
    /// <summary>
    /// 技能执行请求
    /// </summary>
    public class SkillExecutionRequest
    {
        public int SkillId { get; }
        public Actor Target { get; }
        public Vector3? Position { get; }
        
        public SkillExecutionRequest(int skillId, Actor target = null, Vector3? position = null)
        {
            SkillId = skillId;
            Target = target;
            Position = position;
        }
    }

    /// <summary>
    /// 技能实例状态
    /// </summary>
    public enum SkillInstanceState
    {
        Casting,      // 施法中
        Executing,    // 执行中
        OnCooldown    // 冷却中
    }

    /// <summary>
    /// 技能实例
    /// </summary>
    public class SkillInstance
    {
        public SkillBase Skill { get; set; }
        public Actor Target { get; set; }
        public Vector3 TargetPosition { get; set; }
        public float StartTime { get; set; }
        public float CooldownEndTime { get; set; }
        public SkillInstanceState State { get; set; }
    }

    /// <summary>
    /// 基础技能实现
    /// </summary>
    public class BasicSkill : SkillBase
    {
        public BasicSkill(SkillData data)
        {
            Data = data;
        }

        protected override void ExecuteSkill(Actor target)
        {
            // 基础技能执行逻辑
            Debug.Log($"[BasicSkill] 执行技能: {Data.SkillName}");
            
            // 这里应该实现具体的技能效果
            // 比如造成伤害、施加状态效果等
        }
    }
    #endregion
}
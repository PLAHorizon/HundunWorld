using Arch.Core;
using FlaxEngine;
using HundunWorld.Game.ECS.Components;
using HundunWorld.Game.Combat.Skills;
using System;
using System.Collections.Generic;

namespace HundunWorld.Game.Combat
{
    /// <summary>
    /// 连招状态组件（ECS）
    /// </summary>
    public struct ComboStateComponent
    {
        /// <summary>当前连招链的起始技能ID</summary>
        public int ChainStartSkillId;

        /// <summary>当前连招序号（0=起手招）</summary>
        public int CurrentComboIndex;

        /// <summary>连招窗口剩余时间（秒）</summary>
        public float ComboWindowRemaining;

        /// <summary>连招窗口总时间</summary>
        public float ComboWindowTotal;

        /// <summary>连招命中计数</summary>
        public int HitCount;

        /// <summary>连招是否激活</summary>
        public bool IsActive => ComboWindowRemaining > 0f;

        public ComboStateComponent(int startSkillId)
        {
            ChainStartSkillId = startSkillId;
            CurrentComboIndex = 0;
            ComboWindowRemaining = 0f;
            ComboWindowTotal = 0f;
            HitCount = 0;
        }
    }

    /// <summary>
    /// 连招系统 - 管理技能连招链的输入窗口、状态追踪和伤害递增。
    /// 产品级特性：
    /// - 连招窗口计时（超时则连招中断）
    /// - 连招伤害递增（每命中一次增加伤害）
    /// - 连招计数器（用于UI显示）
    /// - 连招终结技触发（连招满后解锁终结技）
    /// </summary>
    public class ComboSystem
    {
        /// <summary>连招伤害递增系数（每击 +8%）</summary>
        public float ComboDamageStep = 0.08f;

        /// <summary>最大连招伤害加成上限（+80%）</summary>
        public float MaxComboDamageBonus = 0.8f;

        /// <summary>连招中断后的短暂硬直（秒）</summary>
        public float ComboBreakStagger = 0.3f;

        /// <summary>连招事件：连招开始</summary>
        public event Action<int, string> OnComboStarted;

        /// <summary>连招事件：连招命中（skillId, hitCount, damageBonus）</summary>
        public event Action<int, int, float> OnComboHit;

        /// <summary>连招事件：连招中断</summary>
        public event Action<int, int> OnComboBroken;

        /// <summary>连招事件：终结技就绪</summary>
        public event Action<int> OnUltimateReady;

        private World _world;
        private QueryDescription _comboQuery;

        public void Initialize(World world)
        {
            _world = world;
            _comboQuery = new QueryDescription().WithAll<ComboStateComponent>();
        }

        /// <summary>
        /// 每帧更新连招窗口计时
        /// </summary>
        public void Update(float deltaTime)
        {
            if (_world == null) return;

            _world.Query(in _comboQuery, (Entity entity, ref ComboStateComponent combo) =>
            {
                if (combo.ComboWindowRemaining > 0f)
                {
                    combo.ComboWindowRemaining -= deltaTime;
                    if (combo.ComboWindowRemaining <= 0f)
                    {
                        // 连招窗口过期，中断连招
                        combo.ComboWindowRemaining = 0f;
                        OnComboBroken?.Invoke(combo.ChainStartSkillId, combo.HitCount);
                    }
                }
            });
        }

        /// <summary>
        /// 尝试执行连招中的下一招。
        /// 返回下一招的技能ID（0=无后续连招或窗口已关闭）。
        /// </summary>
        public int TryAdvanceCombo(Entity entity, int currentSkillId)
        {
            if (_world == null || !_world.IsAlive(entity)) return 0;

            var skillConfig = SkillDatabase.GetSkill(currentSkillId);
            if (skillConfig == null || skillConfig.ComboNextId <= 0) return 0;

            // 检查是否有活跃的连招状态
            if (!_world.Has<ComboStateComponent>(entity))
            {
                // 如果当前技能是连招起手（ComboIndex == 0），创建新连招
                if (skillConfig.ComboIndex == 0)
                {
                    StartNewCombo(entity, skillConfig);
                    return skillConfig.ComboNextId;
                }
                return 0;
            }

            var combo = _world.Get<ComboStateComponent>(entity);

            // 检查连招窗口是否仍然有效
            if (combo.ComboWindowRemaining <= 0f)
            {
                // 窗口已关闭，如果当前是起手招则重新开始
                if (skillConfig.ComboIndex == 0)
                {
                    StartNewCombo(entity, skillConfig);
                    return skillConfig.ComboNextId;
                }
                return 0;
            }

            // 验证是否是当前连招链中的正确下一招
            var expectedNextId = GetExpectedNextSkill(entity, currentSkillId);
            if (expectedNextId <= 0) return 0;

            // 推进连招
            AdvanceCombo(entity, skillConfig);
            return skillConfig.ComboNextId;
        }

        /// <summary>
        /// 开始新的连招链
        /// </summary>
        public void StartNewCombo(Entity entity, SkillConfig startSkill)
        {
            var combo = new ComboStateComponent(startSkill.SkillId)
            {
                CurrentComboIndex = 0,
                ComboWindowRemaining = startSkill.ComboWindow,
                ComboWindowTotal = startSkill.ComboWindow,
                HitCount = 1
            };
            _world.Set(entity, combo);

            OnComboStarted?.Invoke(startSkill.SkillId, startSkill.SkillName);
            OnComboHit?.Invoke(startSkill.SkillId, 1, 0f);
        }

        /// <summary>
        /// 推进连招到下一招
        /// </summary>
        private void AdvanceCombo(Entity entity, SkillConfig currentSkill)
        {
            var combo = _world.Get<ComboStateComponent>(entity);
            combo.CurrentComboIndex = currentSkill.ComboIndex;
            combo.ComboWindowRemaining = currentSkill.ComboWindow;
            combo.ComboWindowTotal = currentSkill.ComboWindow;
            combo.HitCount++;
            _world.Set(entity, combo);

            float damageBonus = GetComboDamageBonus(combo.HitCount);
            OnComboHit?.Invoke(currentSkill.SkillId, combo.HitCount, damageBonus);

            // 检查是否到达连招链末端（终结技就绪）
            if (currentSkill.ComboNextId <= 0 && combo.HitCount >= 3)
            {
                OnUltimateReady?.Invoke(combo.ChainStartSkillId);
            }
        }

        /// <summary>
        /// 获取当前连招状态下期望的下一招ID
        /// </summary>
        private int GetExpectedNextSkill(Entity entity, int currentSkillId)
        {
            var skillConfig = SkillDatabase.GetSkill(currentSkillId);
            if (skillConfig == null) return 0;
            return skillConfig.ComboNextId;
        }

        /// <summary>
        /// 获取连招伤害加成
        /// </summary>
        public float GetComboDamageBonus(int hitCount)
        {
            if (hitCount <= 1) return 0f;
            float bonus = (hitCount - 1) * ComboDamageStep;
            return Mathf.Min(bonus, MaxComboDamageBonus);
        }

        /// <summary>
        /// 获取实体的当前连招伤害倍率（1.0 + bonus）
        /// </summary>
        public float GetComboDamageMultiplier(Entity entity)
        {
            if (_world == null || !_world.IsAlive(entity)) return 1.0f;
            if (!_world.Has<ComboStateComponent>(entity)) return 1.0f;

            var combo = _world.Get<ComboStateComponent>(entity);
            if (combo.ComboWindowRemaining <= 0f) return 1.0f;

            return 1.0f + GetComboDamageBonus(combo.HitCount);
        }

        /// <summary>
        /// 获取实体的连招命中数
        /// </summary>
        public int GetComboHitCount(Entity entity)
        {
            if (_world == null || !_world.IsAlive(entity)) return 0;
            if (!_world.Has<ComboStateComponent>(entity)) return 0;

            var combo = _world.Get<ComboStateComponent>(entity);
            return combo.ComboWindowRemaining > 0f ? combo.HitCount : 0;
        }

        /// <summary>
        /// 获取连招窗口进度（0-1，1=刚命中，0=即将超时）
        /// </summary>
        public float GetComboWindowProgress(Entity entity)
        {
            if (_world == null || !_world.IsAlive(entity)) return 0f;
            if (!_world.Has<ComboStateComponent>(entity)) return 0f;

            var combo = _world.Get<ComboStateComponent>(entity);
            if (combo.ComboWindowTotal <= 0f) return 0f;
            return Mathf.Clamp(combo.ComboWindowRemaining / combo.ComboWindowTotal, 0f, 1f);
        }

        /// <summary>
        /// 强制中断连招（被控制/击飞等）
        /// </summary>
        public void BreakCombo(Entity entity)
        {
            if (_world == null || !_world.IsAlive(entity)) return;
            if (!_world.Has<ComboStateComponent>(entity)) return;

            var combo = _world.Get<ComboStateComponent>(entity);
            if (combo.HitCount > 0)
            {
                OnComboBroken?.Invoke(combo.ChainStartSkillId, combo.HitCount);
            }
            combo.ComboWindowRemaining = 0f;
            combo.HitCount = 0;
            _world.Set(entity, combo);
        }

        /// <summary>
        /// 重置连招状态
        /// </summary>
        public void ResetCombo(Entity entity)
        {
            if (_world == null || !_world.IsAlive(entity)) return;
            if (_world.Has<ComboStateComponent>(entity))
            {
                _world.Remove<ComboStateComponent>(entity);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using FlaxEngine;

namespace Game.Combat.Skills
{
    /// <summary>
    /// 技能动画状态机
    /// 管理攻击/施法/受击/死亡等动画状态的切换和过渡
    /// </summary>
    public class SkillAnimationStateMachine : Script
    {
        /// <summary>
        /// 动画状态
        /// </summary>
        public enum AnimState
        {
            Idle,           // 待机
            Moving,         // 移动
            AttackStartup,  // 攻击前摇
            AttackActive,   // 攻击判定
            AttackRecovery, // 攻击后摇
            CastStartup,   // 施法前摇
            CastActive,     // 施法激活
            CastRecovery,   // 施法后摇
            Hit,            // 受击
            Death,          // 死亡
            Charging,       // 蓄力
            Channeling      // 引导
        }

        /// <summary>
        /// 状态转换条件
        /// </summary>
        public class StateTransition
        {
            public AnimState FromState { get; set; }
            public AnimState ToState { get; set; }
            public float TransitionDuration { get; set; }
            public Func<bool> Condition { get; set; }
        }

        [Header("状态机配置")]
        [Tooltip("当前状态")]
        public AnimState CurrentState { get; private set; } = AnimState.Idle;

        [Tooltip("上一个状态")]
        public AnimState PreviousState { get; private set; } = AnimState.Idle;

        [Tooltip("当前状态持续时间")]
        public float StateTime { get; private set; }

        [Tooltip("是否锁定状态（受击/死亡期间不可切换）")]
        public bool IsStateLocked { get; private set; }

        [Header("时间配置")]
        [Tooltip("受击硬直时间")]
        public float HitStunDuration = 0.3f;

        [Tooltip("死亡动画时间")]
        public float DeathAnimationDuration = 2.0f;

        [Tooltip("默认过渡时间")]
        public float DefaultTransitionDuration = 0.1f;

        [Header("调试")]
        [Tooltip("显示调试信息")]
        public bool ShowDebug = false;

        // 状态转换表
        private readonly List<StateTransition> _transitions = new List<StateTransition>();

        // 状态时长配置
        private readonly Dictionary<AnimState, float> _stateDurations = new Dictionary<AnimState, float>();

        // 事件
        public event Action<AnimState, AnimState> OnStateChanged;
        public event Action<AnimState> OnStateEnter;
        public event Action<AnimState> OnStateExit;

        // 动画控制器引用
        private SkillAnimationController _animationController;

        /// <summary>
        /// 初始化
        /// </summary>
        public override void OnEnable()
        {
            _animationController = Actor.GetScript<SkillAnimationController>();
            InitializeDefaultTransitions();
            InitializeDefaultDurations();
        }

        /// <summary>
        /// 每帧更新
        /// </summary>
        public override void OnUpdate()
        {
            StateTime += Time.DeltaTime;

            // 检查自动状态过渡
            CheckAutoTransitions();

            // 检查状态锁定超时
            CheckStateLockTimeout();

            if (ShowDebug)
            {
                DebugDraw.DrawText($"AnimState: {CurrentState}, Time: {StateTime:F2}s, Locked: {IsStateLocked}",
                    new Vector3(100, 180, 0), Color.Cyan);
            }
        }

        /// <summary>
        /// 初始化默认状态转换
        /// </summary>
        private void InitializeDefaultTransitions()
        {
            // 攻击流程：前摇 -> 激活 -> 后摇 -> 待机
            AddTransition(AnimState.AttackStartup, AnimState.AttackActive, 0.05f);
            AddTransition(AnimState.AttackActive, AnimState.AttackRecovery, 0.05f);
            AddTransition(AnimState.AttackRecovery, AnimState.Idle, DefaultTransitionDuration);

            // 施法流程：前摇 -> 激活 -> 后摇 -> 待机
            AddTransition(AnimState.CastStartup, AnimState.CastActive, 0.05f);
            AddTransition(AnimState.CastActive, AnimState.CastRecovery, 0.05f);
            AddTransition(AnimState.CastRecovery, AnimState.Idle, DefaultTransitionDuration);

            // 受击 -> 待机
            AddTransition(AnimState.Hit, AnimState.Idle, DefaultTransitionDuration);

            // 蓄力 -> 攻击激活
            AddTransition(AnimState.Charging, AnimState.AttackActive, 0.05f);

            // 引导 -> 待机
            AddTransition(AnimState.Channeling, AnimState.Idle, DefaultTransitionDuration);
        }

        /// <summary>
        /// 初始化默认状态时长
        /// </summary>
        private void InitializeDefaultDurations()
        {
            _stateDurations[AnimState.AttackStartup] = 0.3f;
            _stateDurations[AnimState.AttackActive] = 0.1f;
            _stateDurations[AnimState.AttackRecovery] = 0.2f;
            _stateDurations[AnimState.CastStartup] = 0.5f;
            _stateDurations[AnimState.CastActive] = 0.2f;
            _stateDurations[AnimState.CastRecovery] = 0.3f;
            _stateDurations[AnimState.Hit] = HitStunDuration;
            _stateDurations[AnimState.Death] = DeathAnimationDuration;
            _stateDurations[AnimState.Charging] = 1.0f;
            _stateDurations[AnimState.Channeling] = 2.0f;
        }

        /// <summary>
        /// 添加状态转换
        /// </summary>
        public void AddTransition(AnimState from, AnimState to, float duration, Func<bool> condition = null)
        {
            _transitions.Add(new StateTransition
            {
                FromState = from,
                ToState = to,
                TransitionDuration = duration,
                Condition = condition
            });
        }

        /// <summary>
        /// 设置状态时长
        /// </summary>
        public void SetStateDuration(AnimState state, float duration)
        {
            _stateDurations[state] = duration;
        }

        /// <summary>
        /// 获取状态时长
        /// </summary>
        public float GetStateDuration(AnimState state)
        {
            return _stateDurations.TryGetValue(state, out var duration) ? duration : 0f;
        }

        /// <summary>
        /// 请求切换状态
        /// </summary>
        public bool RequestStateChange(AnimState newState)
        {
            // 死亡状态不可被覆盖（除了从死亡恢复）
            if (CurrentState == AnimState.Death && newState != AnimState.Idle)
                return false;

            // 状态锁定期间，只有死亡可以打断
            if (IsStateLocked && newState != AnimState.Death)
                return false;

            // 不能切换到相同状态
            if (CurrentState == newState)
                return false;

            // 验证状态转换是否合法
            if (!IsTransitionValid(CurrentState, newState))
                return false;

            TransitionToState(newState);
            return true;
        }

        /// <summary>
        /// 强制切换状态（跳过验证）
        /// </summary>
        public void ForceStateChange(AnimState newState)
        {
            TransitionToState(newState);
        }

        /// <summary>
        /// 触发攻击
        /// </summary>
        public bool TriggerAttack(string animationName = "Attack", float startupTime = -1)
        {
            if (!CanStartAction())
                return false;

            if (startupTime >= 0)
                _stateDurations[AnimState.AttackStartup] = startupTime;

            TransitionToState(AnimState.AttackStartup);

            // 同步到动画控制器
            _animationController?.PlaySkillAnimation("Attack", animationName,
                _stateDurations.GetValueOrDefault(AnimState.AttackStartup, 0.3f),
                _stateDurations.GetValueOrDefault(AnimState.AttackActive, 0.1f),
                _stateDurations.GetValueOrDefault(AnimState.AttackRecovery, 0.2f));

            return true;
        }

        /// <summary>
        /// 触发施法
        /// </summary>
        public bool TriggerCast(string skillName, string animationName = "Cast", float castTime = -1)
        {
            if (!CanStartAction())
                return false;

            if (castTime >= 0)
                _stateDurations[AnimState.CastStartup] = castTime;

            TransitionToState(AnimState.CastStartup);

            _animationController?.PlaySkillAnimation(skillName, animationName,
                _stateDurations.GetValueOrDefault(AnimState.CastStartup, 0.5f),
                _stateDurations.GetValueOrDefault(AnimState.CastActive, 0.2f),
                _stateDurations.GetValueOrDefault(AnimState.CastRecovery, 0.3f));

            return true;
        }

        /// <summary>
        /// 触发受击
        /// </summary>
        public void TriggerHit()
        {
            // 受击可以打断大部分状态
            if (CurrentState == AnimState.Death)
                return;

            _stateDurations[AnimState.Hit] = HitStunDuration;
            TransitionToState(AnimState.Hit);
            IsStateLocked = true;
        }

        /// <summary>
        /// 触发死亡
        /// </summary>
        public void TriggerDeath()
        {
            if (CurrentState == AnimState.Death)
                return;

            _stateDurations[AnimState.Death] = DeathAnimationDuration;
            TransitionToState(AnimState.Death);
            IsStateLocked = true;
        }

        /// <summary>
        /// 触发蓄力
        /// </summary>
        public bool TriggerCharge(string skillName, float maxChargeTime = 2.0f)
        {
            if (!CanStartAction())
                return false;

            _stateDurations[AnimState.Charging] = maxChargeTime;
            TransitionToState(AnimState.Charging);

            _animationController?.PlayChargeSkill(skillName, "Charge", maxChargeTime);

            return true;
        }

        /// <summary>
        /// 释放蓄力
        /// </summary>
        public void ReleaseCharge()
        {
            if (CurrentState == AnimState.Charging)
            {
                TransitionToState(AnimState.AttackActive);
            }
        }

        /// <summary>
        /// 触发引导
        /// </summary>
        public bool TriggerChannel(string skillName, float channelDuration = 3.0f)
        {
            if (!CanStartAction())
                return false;

            _stateDurations[AnimState.Channeling] = channelDuration;
            TransitionToState(AnimState.Channeling);

            _animationController?.PlayChannelSkill(skillName, "Channel", channelDuration);

            return true;
        }

        /// <summary>
        /// 是否可以开始新动作
        /// </summary>
        public bool CanStartAction()
        {
            if (IsStateLocked)
                return false;

            return CurrentState == AnimState.Idle ||
                   CurrentState == AnimState.Moving ||
                   CurrentState == AnimState.AttackRecovery ||
                   CurrentState == AnimState.CastRecovery;
        }

        /// <summary>
        /// 是否处于可移动状态
        /// </summary>
        public bool CanMove()
        {
            return CurrentState == AnimState.Idle ||
                   CurrentState == AnimState.Moving;
        }

        /// <summary>
        /// 是否处于攻击中
        /// </summary>
        public bool IsAttacking()
        {
            return CurrentState == AnimState.AttackStartup ||
                   CurrentState == AnimState.AttackActive ||
                   CurrentState == AnimState.AttackRecovery;
        }

        /// <summary>
        /// 是否处于施法中
        /// </summary>
        public bool IsCasting()
        {
            return CurrentState == AnimState.CastStartup ||
                   CurrentState == AnimState.CastActive ||
                   CurrentState == AnimState.CastRecovery;
        }

        /// <summary>
        /// 获取当前状态的进度（0-1）
        /// </summary>
        public float GetStateProgress()
        {
            if (!_stateDurations.TryGetValue(CurrentState, out var duration) || duration <= 0)
                return 1.0f;

            return Mathf.Clamp(StateTime / duration, 0, 1);
        }

        /// <summary>
        /// 验证状态转换是否合法
        /// </summary>
        private bool IsTransitionValid(AnimState from, AnimState to)
        {
            // 死亡可以从任何状态转入
            if (to == AnimState.Death)
                return true;

            // 受击可以打断非死亡状态
            if (to == AnimState.Hit && from != AnimState.Death)
                return true;

            // 从待机/移动可以开始任何动作
            if (from == AnimState.Idle || from == AnimState.Moving)
                return true;

            // 后摇阶段可以被新动作打断
            if (from == AnimState.AttackRecovery || from == AnimState.CastRecovery)
                return true;

            // 检查已注册的转换
            foreach (var transition in _transitions)
            {
                if (transition.FromState == from && transition.ToState == to)
                {
                    if (transition.Condition == null || transition.Condition())
                        return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 执行状态切换
        /// </summary>
        private void TransitionToState(AnimState newState)
        {
            var oldState = CurrentState;
            PreviousState = oldState;
            CurrentState = newState;
            StateTime = 0;

            OnStateExit?.Invoke(oldState);
            OnStateEnter?.Invoke(newState);
            OnStateChanged?.Invoke(oldState, newState);

            if (ShowDebug)
            {
                Debug.Log($"State transition: {oldState} -> {newState}");
            }
        }

        /// <summary>
        /// 检查自动状态过渡
        /// </summary>
        private void CheckAutoTransitions()
        {
            if (!_stateDurations.TryGetValue(CurrentState, out var duration))
                return;

            if (duration <= 0 || StateTime < duration)
                return;

            // 查找自动过渡目标
            foreach (var transition in _transitions)
            {
                if (transition.FromState == CurrentState)
                {
                    if (transition.Condition == null || transition.Condition())
                    {
                        TransitionToState(transition.ToState);
                        return;
                    }
                }
            }

            // 如果没有匹配的过渡，默认回到待机
            if (CurrentState != AnimState.Idle && CurrentState != AnimState.Death)
            {
                TransitionToState(AnimState.Idle);
            }
        }

        /// <summary>
        /// 检查状态锁定超时
        /// </summary>
        private void CheckStateLockTimeout()
        {
            if (!IsStateLocked)
                return;

            if (_stateDurations.TryGetValue(CurrentState, out var duration) && StateTime >= duration)
            {
                IsStateLocked = false;

                // 死亡状态不自动恢复
                if (CurrentState != AnimState.Death)
                {
                    TransitionToState(AnimState.Idle);
                }
            }
        }

        /// <summary>
        /// 重置状态机
        /// </summary>
        public void Reset()
        {
            CurrentState = AnimState.Idle;
            PreviousState = AnimState.Idle;
            StateTime = 0;
            IsStateLocked = false;
        }
    }
}

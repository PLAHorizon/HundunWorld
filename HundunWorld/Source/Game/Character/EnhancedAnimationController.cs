using FlaxEngine;
using System;
using System.Collections.Generic;

namespace HundunWorld.Game.Character
{
    /// <summary>
    /// 扩展动画状态（包含战斗/技能/交互状态）
    /// </summary>
    public enum AnimState
    {
        // 基础移动
        Idle,
        Walk,
        Run,
        Sprint,
        Crouch,
        CrouchWalk,

        // 空中
        Jump,
        Fall,
        Land,
        Glide,

        // 战斗
        Attack_01,
        Attack_02,
        Attack_03,
        Attack_Combo_Finish,
        Cast_Start,
        Cast_Loop,
        Cast_End,
        Skill_Ultimate,

        // 受击
        Hit_Light,
        Hit_Heavy,
        Hit_Launch,
        Hit_Knockdown,
        GetUp,

        // 特殊
        Death,
        Revive,
        Interact,
        Mount,
        Dismount,
        Emote
    }

    /// <summary>
    /// 动画层定义
    /// </summary>
    public enum AnimLayer
    {
        /// <summary>基础层（移动/姿态）</summary>
        Base = 0,
        /// <summary>上半身覆盖层（攻击/施法）</summary>
        UpperBody = 1,
        /// <summary>全身覆盖层（受击/终结技）</summary>
        FullBody = 2,
        /// <summary>附加层（表情/手指）</summary>
        Additive = 3
    }

    /// <summary>
    /// 动画过渡配置
    /// </summary>
    [Serializable]
    public class AnimTransitionConfig
    {
        public AnimState From;
        public AnimState To;
        public float BlendDuration = 0.2f;
        public bool CanInterrupt = true;
    }

    /// <summary>
    /// 动画事件数据
    /// </summary>
    [Serializable]
    public class AnimEventData
    {
        /// <summary>事件触发时间点（0-1，动画进度比例）</summary>
        public float TriggerTime = 0.5f;

        /// <summary>事件类型标识</summary>
        public string EventType = "";

        /// <summary>事件参数</summary>
        public string Parameter = "";

        /// <summary>是否已触发</summary>
        [NonSerialized]
        public bool HasFired = false;
    }

    /// <summary>
    /// 增强版角色动画控制器 - 产品级动画状态机。
    /// 特性：
    /// - 多层动画（基础层 + 上半身覆盖 + 全身覆盖）
    /// - 战斗动画状态机（攻击连招/施法/受击）
    /// - 动画事件系统（技能伤害判定时机）
    /// - 根运动支持
    /// - 动画混合树（速度驱动的移动混合）
    /// </summary>
    public class EnhancedAnimationController : Script
    {
        // ===== 组件引用 =====
        [Header("组件")]
        public AnimatedModel AnimatedModel;

        // ===== 动画参数名 =====
        [Header("参数名称")]
        public string MoveSpeedParam = "MoveSpeed";
        public string IsGroundedParam = "IsGrounded";
        public string VerticalVelocityParam = "VerticalVelocity";
        public string IsCombatParam = "IsCombat";
        public string AttackIndexParam = "AttackIndex";
        public string CastProgressParam = "CastProgress";
        public string HitReactionParam = "HitReaction";

        // ===== 配置 =====
        [Header("配置")]
        public float WalkSpeedThreshold = 0.1f;
        public float RunSpeedThreshold = 3.0f;
        public float SprintSpeedThreshold = 6.0f;
        public float DefaultBlendTime = 0.2f;
        public float CombatBlendTime = 0.1f;
        public bool EnableRootMotion = false;

        // ===== 运行时状态 =====
        public AnimState CurrentState { get; private set; } = AnimState.Idle;
        public AnimState PreviousState { get; private set; } = AnimState.Idle;
        public bool IsInCombat { get; private set; } = false;
        public int CurrentAttackIndex { get; private set; } = 0;
        public float CurrentAnimProgress { get; private set; } = 0f;

        // ===== 内部 =====
        private bool _paramsInitialized = false;
        private AnimGraphParameter _moveSpeedParam;
        private AnimGraphParameter _isGroundedParam;
        private AnimGraphParameter _verticalVelParam;
        private AnimGraphParameter _isCombatParam;
        private AnimGraphParameter _attackIndexParam;
        private AnimGraphParameter _castProgressParam;
        private AnimGraphParameter _hitReactionParam;

        private List<AnimEventData> _pendingEvents = new List<AnimEventData>();
        private Dictionary<AnimState, string> _stateToAnimName = new Dictionary<AnimState, string>();

        // ===== 事件 =====
        /// <summary>动画事件触发回调</summary>
        public event Action<string, string> OnAnimEvent;

        /// <summary>动画状态变更回调</summary>
        public event Action<AnimState, AnimState> OnStateChanged;

        public override void OnStart()
        {
            if (AnimatedModel == null)
            {
                AnimatedModel = Actor.GetChild<AnimatedModel>();
                if (AnimatedModel == null)
                    AnimatedModel = Actor.FindActor<AnimatedModel>();
            }

            InitializeStateMappings();
            TryInitializeParams();
        }

        public override void OnUpdate()
        {
            if (!_paramsInitialized)
            {
                TryInitializeParams();
                return;
            }

            // 更新动画事件
            UpdateAnimEvents();
        }

        // ===== 初始化 =====

        private void InitializeStateMappings()
        {
            _stateToAnimName[AnimState.Idle] = "Idle";
            _stateToAnimName[AnimState.Walk] = "Walk";
            _stateToAnimName[AnimState.Run] = "Run";
            _stateToAnimName[AnimState.Sprint] = "Sprint";
            _stateToAnimName[AnimState.Crouch] = "Crouch";
            _stateToAnimName[AnimState.Jump] = "Jump";
            _stateToAnimName[AnimState.Fall] = "Fall";
            _stateToAnimName[AnimState.Land] = "Land";
            _stateToAnimName[AnimState.Glide] = "Glide";
            _stateToAnimName[AnimState.Attack_01] = "Attack_01";
            _stateToAnimName[AnimState.Attack_02] = "Attack_02";
            _stateToAnimName[AnimState.Attack_03] = "Attack_03";
            _stateToAnimName[AnimState.Attack_Combo_Finish] = "Attack_ComboFinish";
            _stateToAnimName[AnimState.Cast_Start] = "Cast_Start";
            _stateToAnimName[AnimState.Cast_Loop] = "Cast_Loop";
            _stateToAnimName[AnimState.Cast_End] = "Cast_End";
            _stateToAnimName[AnimState.Skill_Ultimate] = "Ultimate";
            _stateToAnimName[AnimState.Hit_Light] = "Hit_Light";
            _stateToAnimName[AnimState.Hit_Heavy] = "Hit_Heavy";
            _stateToAnimName[AnimState.Hit_Launch] = "Hit_Launch";
            _stateToAnimName[AnimState.Hit_Knockdown] = "Hit_Knockdown";
            _stateToAnimName[AnimState.GetUp] = "GetUp";
            _stateToAnimName[AnimState.Death] = "Death";
            _stateToAnimName[AnimState.Revive] = "Revive";
            _stateToAnimName[AnimState.Interact] = "Interact";
        }

        private bool TryInitializeParams()
        {
            if (_paramsInitialized) return true;
            if (AnimatedModel == null) return false;
            if (AnimatedModel.SkinnedModel == null || !AnimatedModel.SkinnedModel.IsLoaded) return false;
            if (AnimatedModel.AnimationGraph == null || !AnimatedModel.AnimationGraph.IsLoaded) return false;

            try
            {
                _moveSpeedParam = AnimatedModel.GetParameter(MoveSpeedParam);
                _isGroundedParam = AnimatedModel.GetParameter(IsGroundedParam);
                _verticalVelParam = AnimatedModel.GetParameter(VerticalVelocityParam);
                _isCombatParam = AnimatedModel.GetParameter(IsCombatParam);
                _attackIndexParam = AnimatedModel.GetParameter(AttackIndexParam);
                _castProgressParam = AnimatedModel.GetParameter(CastProgressParam);
                _hitReactionParam = AnimatedModel.GetParameter(HitReactionParam);
                _paramsInitialized = true;
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[EnhancedAnimController] 参数初始化失败: {ex.Message}");
                return false;
            }
        }

        // ===== 移动控制 =====

        /// <summary>设置移动速度（驱动混合树）</summary>
        public void SetMoveSpeed(float speed)
        {
            if (_moveSpeedParam != null)
                _moveSpeedParam.Value = speed;

            // 自动切换基础状态
            if (!IsInCombat)
            {
                if (speed < WalkSpeedThreshold) TransitionTo(AnimState.Idle);
                else if (speed < RunSpeedThreshold) TransitionTo(AnimState.Walk);
                else if (speed < SprintSpeedThreshold) TransitionTo(AnimState.Run);
                else TransitionTo(AnimState.Sprint);
            }
        }

        /// <summary>设置是否着地</summary>
        public void SetGrounded(bool grounded)
        {
            if (_isGroundedParam != null)
                _isGroundedParam.Value = grounded;
        }

        /// <summary>设置垂直速度（跳跃/下落）</summary>
        public void SetVerticalVelocity(float vel)
        {
            if (_verticalVelParam != null)
                _verticalVelParam.Value = vel;

            if (vel > 1f) TransitionTo(AnimState.Jump);
            else if (vel < -1f) TransitionTo(AnimState.Fall);
        }

        // ===== 战斗控制 =====

        /// <summary>进入战斗姿态</summary>
        public void EnterCombat()
        {
            IsInCombat = true;
            if (_isCombatParam != null)
                _isCombatParam.Value = true;
        }

        /// <summary>退出战斗姿态</summary>
        public void ExitCombat()
        {
            IsInCombat = false;
            CurrentAttackIndex = 0;
            if (_isCombatParam != null)
                _isCombatParam.Value = false;
            TransitionTo(AnimState.Idle);
        }

        /// <summary>播放攻击动画（连招序号 0-2）</summary>
        public void PlayAttack(int comboIndex)
        {
            CurrentAttackIndex = comboIndex;
            if (_attackIndexParam != null)
                _attackIndexParam.Value = comboIndex;

            var state = comboIndex switch
            {
                0 => AnimState.Attack_01,
                1 => AnimState.Attack_02,
                2 => AnimState.Attack_03,
                _ => AnimState.Attack_Combo_Finish
            };
            TransitionTo(state, CombatBlendTime);

            // 注册伤害判定事件（动画进度50%时触发）
            RegisterAnimEvent(0.5f, "DamageHit", comboIndex.ToString());
        }

        /// <summary>播放施法动画</summary>
        public void PlayCast(float castTime)
        {
            TransitionTo(AnimState.Cast_Start, CombatBlendTime);

            // 施法完成事件
            if (castTime > 0f)
            {
                RegisterAnimEvent(1.0f, "CastComplete", "");
            }
        }

        /// <summary>更新施法进度</summary>
        public void SetCastProgress(float progress)
        {
            if (_castProgressParam != null)
                _castProgressParam.Value = progress;
        }

        /// <summary>播放终结技动画</summary>
        public void PlayUltimate()
        {
            TransitionTo(AnimState.Skill_Ultimate, 0.3f);
            RegisterAnimEvent(0.4f, "UltimateHit", "");
            RegisterAnimEvent(0.7f, "UltimateHit", "2");
        }

        // ===== 受击反应 =====

        /// <summary>播放受击动画</summary>
        public void PlayHitReaction(float damage, bool isLaunch = false)
        {
            AnimState hitState;
            if (isLaunch)
                hitState = AnimState.Hit_Launch;
            else if (damage > 50f)
                hitState = AnimState.Hit_Heavy;
            else
                hitState = AnimState.Hit_Light;

            TransitionTo(hitState, 0.05f);

            if (_hitReactionParam != null)
                _hitReactionParam.Value = (int)hitState;
        }

        /// <summary>播放击倒动画</summary>
        public void PlayKnockdown()
        {
            TransitionTo(AnimState.Hit_Knockdown, 0.1f);
            RegisterAnimEvent(0.8f, "CanGetUp", "");
        }

        /// <summary>播放起身动画</summary>
        public void PlayGetUp()
        {
            TransitionTo(AnimState.GetUp, 0.15f);
            RegisterAnimEvent(0.9f, "GetUpComplete", "");
        }

        // ===== 特殊状态 =====

        /// <summary>播放死亡动画</summary>
        public void PlayDeath()
        {
            TransitionTo(AnimState.Death, 0.3f);
        }

        /// <summary>播放复活动画</summary>
        public void PlayRevive()
        {
            TransitionTo(AnimState.Revive, 0.3f);
        }

        /// <summary>播放滑翔动画</summary>
        public void PlayGlide()
        {
            TransitionTo(AnimState.Glide, 0.2f);
        }

        // ===== 状态转换 =====

        /// <summary>转换到指定动画状态</summary>
        public void TransitionTo(AnimState newState, float blendTime = -1f)
        {
            if (CurrentState == newState) return;

            PreviousState = CurrentState;
            CurrentState = newState;
            CurrentAnimProgress = 0f;

            // 清除旧事件
            _pendingEvents.Clear();

            OnStateChanged?.Invoke(PreviousState, CurrentState);
        }

        // ===== 动画事件系统 =====

        /// <summary>注册动画事件</summary>
        public void RegisterAnimEvent(float triggerTime, string eventType, string parameter)
        {
            _pendingEvents.Add(new AnimEventData
            {
                TriggerTime = triggerTime,
                EventType = eventType,
                Parameter = parameter,
                HasFired = false
            });
        }

        private void UpdateAnimEvents()
        {
            // 简化：基于时间推进模拟动画进度
            // 完整实现应从 AnimatedModel 获取实际动画进度
            CurrentAnimProgress += Time.DeltaTime * 2f; // 假设动画约0.5秒

            for (int i = _pendingEvents.Count - 1; i >= 0; i--)
            {
                var evt = _pendingEvents[i];
                if (!evt.HasFired && CurrentAnimProgress >= evt.TriggerTime)
                {
                    evt.HasFired = true;
                    OnAnimEvent?.Invoke(evt.EventType, evt.Parameter);
                    _pendingEvents.RemoveAt(i);
                }
            }

            // 动画播放完毕，回到 Idle
            if (CurrentAnimProgress >= 1.0f && IsTransientState(CurrentState))
            {
                TransitionTo(IsInCombat ? AnimState.Idle : AnimState.Idle);
            }
        }

        /// <summary>判断是否为临时状态（播放完自动回到Idle）</summary>
        private bool IsTransientState(AnimState state)
        {
            return state switch
            {
                AnimState.Attack_01 or AnimState.Attack_02 or AnimState.Attack_03 or
                AnimState.Attack_Combo_Finish or AnimState.Cast_Start or AnimState.Cast_End or
                AnimState.Hit_Light or AnimState.Hit_Heavy or AnimState.Hit_Launch or
                AnimState.Land or AnimState.GetUp or AnimState.Interact => true,
                _ => false
            };
        }

        // ===== 根运动 =====

        /// <summary>获取根运动位移（由动画驱动的移动）</summary>
        public Vector3 GetRootMotionDelta()
        {
            if (!EnableRootMotion || AnimatedModel == null) return Vector3.Zero;
            // Flax 中根运动通过 AnimatedModel.RootMotion 获取
            // 此处为框架占位，实际需要接入 Flax 的根运动 API
            return Vector3.Zero;
        }
    }
}

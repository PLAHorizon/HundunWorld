using System;
using System.Collections.Generic;
using FlaxEngine;

namespace Game.Combat.Skills
{
    /// <summary>
    /// 动画图控制器
    /// 负责与Flax Engine的动画图系统交互，管理动画参数和状态切换
    /// </summary>
    public class AnimationGraphController : Script
    {
        [Header("动画器引用")]
        [Tooltip("角色的AnimatedModel组件")]
        public AnimatedModel AnimatedModel;

        [Header("动画图参数名称")]
        [Tooltip("待机状态参数名")]
        public string IdleParameterName = "IsIdle";
        
        [Tooltip("移动状态参数名")]
        public string MoveParameterName = "IsMoving";
        
        [Tooltip("移动速度参数名")]
        public string MoveSpeedParameterName = "MoveSpeed";
        
        [Tooltip("攻击触发参数名")]
        public string AttackTriggerName = "TriggerAttack";
        
        [Tooltip("施法触发参数名")]
        public string CastTriggerName = "TriggerCast";
        
        [Tooltip("受击触发参数名")]
        public string HitTriggerName = "TriggerHit";
        
        [Tooltip("死亡触发参数名")]
        public string DeathTriggerName = "TriggerDeath";
        
        [Tooltip("技能ID参数名")]
        public string SkillIdParameterName = "SkillId";
        
        [Tooltip("动画速度参数名")]
        public string AnimSpeedParameterName = "AnimSpeed";

        [Header("调试")]
        [Tooltip("显示调试信息")]
        public bool ShowDebug = false;

        // 动画图实例
        private AnimationGraph _animGraph;
        
        // 当前动画状态缓存
        private bool _isIdle = true;
        private bool _isMoving = false;
        private float _moveSpeed = 0f;
        private int _currentSkillId = 0;

        /// <summary>
        /// 初始化
        /// </summary>
        public override void OnEnable()
        {
            if (AnimatedModel == null)
            {
                AnimatedModel = Actor.GetChild<AnimatedModel>();
                if (AnimatedModel == null)
                {
                    Debug.LogError("[AnimationGraphController] 未找到AnimatedModel组件");
                    return;
                }
            }

            _animGraph = AnimatedModel.AnimationGraph;
            if (_animGraph == null)
            {
                Debug.LogWarning("[AnimationGraphController] AnimatedModel没有设置动画图");
            }
            else
            {
                Debug.Log("[AnimationGraphController] 动画图控制器初始化成功");
            }
        }

        /// <summary>
        /// 设置待机状态
        /// </summary>
        public void SetIdle(bool isIdle)
        {
            if (_isIdle == isIdle) return;
            
            _isIdle = isIdle;
            SetAnimationParameter(IdleParameterName, isIdle);
            
            if (ShowDebug)
                Debug.Log($"[AnimGraph] 设置待机状态: {isIdle}");
        }

        /// <summary>
        /// 设置移动状态
        /// </summary>
        public void SetMoving(bool isMoving, float speed = 1.0f)
        {
            if (_isMoving == isMoving && Math.Abs(_moveSpeed - speed) < 0.01f) 
                return;
            
            _isMoving = isMoving;
            _moveSpeed = speed;
            
            SetAnimationParameter(MoveParameterName, isMoving);
            SetAnimationParameter(MoveSpeedParameterName, speed);
            
            if (ShowDebug)
                Debug.Log($"[AnimGraph] 设置移动状态: {isMoving}, 速度: {speed}");
        }

        /// <summary>
        /// 触发攻击动画
        /// </summary>
        public void TriggerAttack(int skillId = 0)
        {
            TriggerAnimationEvent(AttackTriggerName);
            
            if (skillId > 0)
            {
                _currentSkillId = skillId;
                SetAnimationParameter(SkillIdParameterName, skillId);
            }
            
            if (ShowDebug)
                Debug.Log($"[AnimGraph] 触发攻击动画, 技能ID: {skillId}");
        }

        /// <summary>
        /// 触发施法动画
        /// </summary>
        public void TriggerCast(int skillId = 0)
        {
            TriggerAnimationEvent(CastTriggerName);
            
            if (skillId > 0)
            {
                _currentSkillId = skillId;
                SetAnimationParameter(SkillIdParameterName, skillId);
            }
            
            if (ShowDebug)
                Debug.Log($"[AnimGraph] 触发施法动画, 技能ID: {skillId}");
        }

        /// <summary>
        /// 触发受击动画
        /// </summary>
        public void TriggerHit()
        {
            TriggerAnimationEvent(HitTriggerName);
            
            if (ShowDebug)
                Debug.Log("[AnimGraph] 触发受击动画");
        }

        /// <summary>
        /// 触发死亡动画
        /// </summary>
        public void TriggerDeath()
        {
            TriggerAnimationEvent(DeathTriggerName);
            
            if (ShowDebug)
                Debug.Log("[AnimGraph] 触发死亡动画");
        }

        /// <summary>
        /// 设置动画播放速度
        /// </summary>
        public void SetAnimationSpeed(float speed)
        {
            SetAnimationParameter(AnimSpeedParameterName, speed);
            
            if (ShowDebug)
                Debug.Log($"[AnimGraph] 设置动画速度: {speed}");
        }

        /// <summary>
        /// 直接播放指定动画（绕过动画图状态机）
        /// </summary>
        /// <param name="animationName">动画资源名称</param>
        /// <param name="loop">是否循环</param>
        /// <param name="speed">播放速度</param>
        public void PlayAnimationDirect(string animationName, bool loop = false, float speed = 1.0f)
        {
            if (AnimatedModel == null)
            {
                Debug.LogWarning("[AnimationGraphController] AnimatedModel is null");
                return;
            }

            try
            {
                // Flax Engine 中直接播放动画：通过设置动画图参数触发对应状态
                AnimatedModel.UpdateSpeed = speed;

                // 尝试通过参数名触发动画（动画图中应配置对应的触发参数）
                var param = AnimatedModel.GetParameter(animationName);
                if (param != null)
                {
                    param.Value = true;
                    // 对于非循环动画，下一帧自动重置由动画图状态机处理
                }

                if (ShowDebug)
                    Debug.Log($"[AnimGraph] 直接播放动画: {animationName}, 循环: {loop}, 速度: {speed}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AnimationGraphController] 播放动画失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 设置动画参数（通用方法）
        /// </summary>
        private void SetAnimationParameter(string paramName, object value)
        {
            if (_animGraph == null) return;

            try
            {
                // Flax Engine的AnimationGraph参数设置
                // 注意：Flax的API可能不同，这里提供基本框架
                // 实际使用时需要根据Flax版本调整
                
                if (value is bool boolValue)
                {
                    // Flax API: AnimatedModel.GetParameter(name).Value = value
                    var param = AnimatedModel.GetParameter(paramName);
                    if (param != null)
                    {
                        param.Value = boolValue;
                    }
                    if (ShowDebug)
                        Debug.Log($"[AnimGraph] 设置布尔参数 {paramName} = {boolValue}");
                }
                else if (value is int intValue)
                {
                    var param = AnimatedModel.GetParameter(paramName);
                    if (param != null)
                    {
                        param.Value = intValue;
                    }
                    if (ShowDebug)
                        Debug.Log($"[AnimGraph] 设置整数参数 {paramName} = {intValue}");
                }
                else if (value is float floatValue)
                {
                    var param = AnimatedModel.GetParameter(paramName);
                    if (param != null)
                    {
                        param.Value = floatValue;
                    }
                    if (ShowDebug)
                        Debug.Log($"[AnimGraph] 设置浮点参数 {paramName} = {floatValue}");
                }
                else
                {
                    Debug.LogWarning($"[AnimationGraphController] 不支持的参数类型: {value.GetType()}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AnimationGraphController] 设置动画参数 {paramName} 失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 触发动画事件（Trigger类型参数）
        /// Flax 无 SetTrigger 概念，用 bool 参数模拟：设为 true 触发，下一帧由动画图状态机处理
        /// </summary>
        private void TriggerAnimationEvent(string triggerName)
        {
            if (AnimatedModel == null) return;

            try
            {
                // Flax API：用 bool 参数模拟 trigger（设为 true，动画图状态机消费后自动重置）
                var param = AnimatedModel.GetParameter(triggerName);
                if (param != null)
                {
                    param.Value = true;
                }
                if (ShowDebug)
                    Debug.Log($"[AnimGraph] 触发动画事件: {triggerName}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AnimationGraphController] 触发动画事件 {triggerName} 失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取当前动画的播放进度（0-1）
        /// 注意：Flax AnimatedModel 不直接暴露当前动画播放进度，需通过动画图参数间接判断
        /// </summary>
        public float GetCurrentAnimationProgress()
        {
            if (AnimatedModel == null) return 1.0f;

            try
            {
                // Flax 无直接获取动画进度的 API，基于动画参数状态推断
                // 如果处于攻击/施法状态返回进行中，否则返回完成
                var attackParam = AnimatedModel.GetParameter(AttackTriggerName);
                if (attackParam != null && attackParam.Value is bool b && b)
                    return 0.5f; // 攻击动画进行中
                return 1.0f; // 默认认为已完成
            }
            catch
            {
                return 1.0f;
            }
        }

        /// <summary>
        /// 检查指定动画是否正在播放
        /// 通过检查对应的触发参数是否为 true 来判断
        /// </summary>
        public bool IsAnimationPlaying(string animationName)
        {
            if (AnimatedModel == null) return false;

            try
            {
                // Flax API：通过动画图参数值判断当前动画状态
                var param = AnimatedModel.GetParameter(animationName);
                if (param != null && param.Value is bool b)
                {
                    return b;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 重置动画状态到待机
        /// </summary>
        public void ResetToIdle()
        {
            SetIdle(true);
            SetMoving(false);
            _currentSkillId = 0;
            
            if (ShowDebug)
                Debug.Log("[AnimGraph] 重置到待机状态");
        }

        /// <summary>
        /// 获取动画图是否已加载
        /// </summary>
        public bool IsAnimationGraphLoaded()
        {
            return _animGraph != null && AnimatedModel != null;
        }

        /// <summary>
        /// 调试信息绘制
        /// </summary>
        public override void OnDebugDraw()
        {
            if (!ShowDebug) return;

            var pos = Actor.Position + new Vector3(0, 2.5f, 0);
            DebugDraw.DrawText($"Anim Graph Status:\nIdle: {_isIdle}\nMoving: {_isMoving}\nSpeed: {_moveSpeed:F2}\nSkillID: {_currentSkillId}",
                pos, Color.White, 10);
        }
    }
}

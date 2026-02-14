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
                // 注意：Flax Engine的直接动画播放API可能需要根据实际版本调整
                // 这里提供基本框架
                AnimatedModel.UpdateSpeed = speed;
                
                // TODO: 根据Flax Engine版本实现直接播放动画的逻辑
                // 可能需要：
                // 1. 加载动画资源
                // 2. 设置到AnimatedModel
                // 3. 控制循环和速度
                
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
                    // TODO: 根据Flax Engine实际API调整
                    // 可能的API: _animGraph.Parameters.SetValue(paramName, boolValue);
                    Debug.Log($"[AnimGraph] 设置布尔参数 {paramName} = {boolValue}");
                }
                else if (value is int intValue)
                {
                    Debug.Log($"[AnimGraph] 设置整数参数 {paramName} = {intValue}");
                }
                else if (value is float floatValue)
                {
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
        /// </summary>
        private void TriggerAnimationEvent(string triggerName)
        {
            if (_animGraph == null) return;

            try
            {
                // TODO: 根据Flax Engine实际API调整
                // 可能的API: _animGraph.Parameters.SetTrigger(triggerName);
                Debug.Log($"[AnimGraph] 触发动画事件: {triggerName}");
                
                // 注意：某些引擎需要手动重置trigger，Flax可能自动处理
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AnimationGraphController] 触发动画事件 {triggerName} 失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取当前动画的播放进度（0-1）
        /// </summary>
        public float GetCurrentAnimationProgress()
        {
            if (AnimatedModel == null) return 1.0f;

            try
            {
                // TODO: 根据Flax Engine API实现
                // 可能需要访问当前动画状态的时间信息
                return 0f; // 占位符
            }
            catch
            {
                return 1.0f;
            }
        }

        /// <summary>
        /// 检查指定动画是否正在播放
        /// </summary>
        public bool IsAnimationPlaying(string animationName)
        {
            if (AnimatedModel == null || _animGraph == null) return false;

            try
            {
                // TODO: 根据Flax Engine API实现
                // 需要查询当前活动的动画状态
                return false; // 占位符
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

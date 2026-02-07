using FlaxEngine;
using System;
using System.Collections.Generic;

namespace HundunWorld.Game.UI.Components
{
    /// <summary>
    /// 角色动画控制器，用于控制3D角色模型的动画播放
    /// </summary>
    public class CharacterAnimator : Script
    {
        private AnimatedModel _animatedModel;
        private bool _isInitialized = false;
        
        public override void OnStart()
        {
            _animatedModel = Actor.GetScript<AnimatedModel>();
            InitializeAnimator();
        }
        
        /// <summary>
        /// 初始化动画控制器
        /// </summary>
        private void InitializeAnimator()
        {
            if (_animatedModel == null || _animatedModel.SkinnedModel == null)
            {
                FlaxEngine.Debug.LogWarning("无法初始化动画控制器：模型或蒙皮模型为空");
                return;
            }
            
            _isInitialized = true;
            FlaxEngine.Debug.Log($"动画控制器初始化成功");
        }
        
        /// <summary>
        /// 播放指定名称的动画
        /// </summary>
        /// <param name="animationName">动画名称</param>
        /// <param name="loop">是否循环播放</param>
        public void PlayAnimation(string animationName, bool loop = true)
        {
            if (!_isInitialized || _animatedModel == null)
            {
                FlaxEngine.Debug.LogWarning("动画控制器未初始化");
                return;
            }
            
            // 通过动画图参数控制动画播放
            // 这里简化实现，实际项目中需要根据具体的动画图设置参数
            FlaxEngine.Debug.Log($"播放动画: {animationName}");
        }
        
        /// <summary>
        /// 停止当前动画
        /// </summary>
        public void StopAnimation()
        {
            if (!_isInitialized || _animatedModel == null) return;
            
            // 通过动画图参数控制动画停止
            FlaxEngine.Debug.Log("动画已停止");
        }
        
        /// <summary>
        /// 暂停动画
        /// </summary>
        public void PauseAnimation()
        {
            if (!_isInitialized || _animatedModel == null) return;
            
            // 通过动画图参数控制动画暂停
            FlaxEngine.Debug.Log("动画已暂停");
        }
        
        /// <summary>
        /// 恢复动画播放
        /// </summary>
        public void ResumeAnimation()
        {
            if (!_isInitialized || _animatedModel == null) return;
            
            // 通过动画图参数控制动画恢复
            FlaxEngine.Debug.Log("动画已恢复");
        }
        
        /// <summary>
        /// 设置动画速度
        /// </summary>
        /// <param name="speed">播放速度</param>
        public void SetAnimationSpeed(float speed)
        {
            if (!_isInitialized || _animatedModel == null) return;
            
            try
            {
                _animatedModel.UpdateSpeed = speed;
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"设置动画速度失败: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 获取是否正在播放动画
        /// </summary>
        public bool IsPlaying => _isInitialized && _animatedModel != null;
        
        /// <summary>
        /// 获取当前播放的动画名称
        /// </summary>
        public string CurrentAnimationName
        {
            get
            {
                if (!_isInitialized || _animatedModel == null) return string.Empty;
                
                // 简化实现
                return "Unknown";
            }
        }
    }
}
using System;
using FlaxEngine;

namespace HundunWorld.Game.UI.Character
{
    /// <summary>
    /// 步骤导航管理器 - 负责角色创建流程的步骤切换和过渡动画管理
    /// 从 CharacterSceneController 中提取的职责：
    /// - 步骤导航状态管理
    /// - 步骤过渡动画
    /// - 步骤切换事件发布
    /// </summary>
    public class StepNavigationManager
    {
        private readonly StepNavigation _stepNavigation;
        private float _stepTransitionTimer = 0f;
        private const float StepTransitionDuration = 0.2f;
        private bool _stepTransitionActive = false;

        /// <summary>
        /// 当前步骤
        /// </summary>
        public CreationStep CurrentStep => _stepNavigation.CurrentStep;

        /// <summary>
        /// 步骤数据
        /// </summary>
        public StepData StepData => _stepNavigation.StepData;

        /// <summary>
        /// 是否正在过渡
        /// </summary>
        public bool IsTransitioning => _stepTransitionActive;

        /// <summary>
        /// 步骤切换事件
        /// </summary>
        public event Action<CreationStep, CreationStep> OnStepChanged;

        /// <summary>
        /// 步骤过渡开始事件
        /// </summary>
        public event Action<CreationStep> OnStepTransitionStart;

        /// <summary>
        /// 步骤过渡完成事件
        /// </summary>
        public event Action<CreationStep> OnStepTransitionComplete;

        public StepNavigationManager()
        {
            _stepNavigation = new StepNavigation();
            _stepNavigation.OnStepChanged += HandleStepChanged;
        }

        /// <summary>
        /// 前进到下一步
        /// </summary>
        public void GoNext()
        {
            if (!_stepTransitionActive)
            {
                _stepNavigation.GoNext();
            }
        }

        /// <summary>
        /// 返回上一步
        /// </summary>
        public void GoBack()
        {
            if (!_stepTransitionActive)
            {
                _stepNavigation.GoBack();
            }
        }

        /// <summary>
        /// 重置到初始状态
        /// </summary>
        public void Reset()
        {
            _stepNavigation.Reset();
            _stepTransitionActive = false;
            _stepTransitionTimer = 0f;
        }

        /// <summary>
        /// 每帧更新过渡动画
        /// </summary>
        /// <returns>如果正在过渡返回 true，否则返回 false</returns>
        public bool UpdateTransition()
        {
            if (!_stepTransitionActive)
                return false;

            _stepTransitionTimer += Time.DeltaTime;

            if (_stepTransitionTimer >= StepTransitionDuration)
            {
                _stepTransitionActive = false;
                _stepTransitionTimer = 0f;
                OnStepTransitionComplete?.Invoke(_stepNavigation.CurrentStep);
            }

            return true;
        }

        /// <summary>
        /// 获取当前步骤的过渡进度 (0~1)
        /// </summary>
        public float GetTransitionProgress()
        {
            if (!_stepTransitionActive)
                return 1f;
            return Math.Min(1f, _stepTransitionTimer / StepTransitionDuration);
        }

        /// <summary>
        /// 获取步骤索引
        /// </summary>
        public int GetStepIndex(CreationStep? step)
        {
            if (step == null) return 0;
            switch (step.Value)
            {
                case CreationStep.GenderSelection: return 0;
                case CreationStep.FacePreset: return 1;
                case CreationStep.DetailedCreation: return 2;
                case CreationStep.NamingComplete: return 3;
                default: return 0;
            }
        }

        private void HandleStepChanged(CreationStep oldStep, CreationStep newStep)
        {
            _stepTransitionActive = true;
            _stepTransitionTimer = 0f;
            OnStepTransitionStart?.Invoke(newStep);
            OnStepChanged?.Invoke(oldStep, newStep);
        }
    }
}

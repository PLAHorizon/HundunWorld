using FlaxEngine;
using System;
using System.Collections.Generic;
using Horizon.Game.Message.Enums;

namespace HundunWorld.Game.UI.Character
{
    /// <summary>
    /// 角色创建步骤枚举
    /// </summary>
    public enum CreationStep
    {
        /// <summary>
        /// 性别选择
        /// </summary>
        GenderSelection,

        /// <summary>
        /// 脸型预设选择
        /// </summary>
        FacePreset,

        /// <summary>
        /// 精细捏脸创建
        /// </summary>
        DetailedCreation,

        /// <summary>
        /// 命名完成
        /// </summary>
        NamingComplete
    }

    /// <summary>
    /// 角色创建步骤数据，存储所有步骤中的用户输入数据
    /// </summary>
    public class StepData
    {
        /// <summary>
        /// 性别：0=男，1=女
        /// </summary>
        public int Gender { get; set; }

        /// <summary>
        /// 选中的预设索引
        /// </summary>
        public int SelectedPresetIndex { get; set; }

        /// <summary>
        /// 脸型预设名称
        /// </summary>
        public string FacePresetName { get; set; }

        /// <summary>
        /// 面部参数字典，存储各个滑块的数值
        /// </summary>
        public Dictionary<string, float> FaceParameters { get; set; } = new Dictionary<string, float>();

        /// <summary>
        /// 角色名称
        /// </summary>
        public string CharacterName { get; set; }

        /// <summary>
        /// 职业
        /// </summary>
        public Profession Profession { get; set; }

        /// <summary>
        /// 身高: 0.0~1.0 (默认 0.5)
        /// </summary>
        public float BodyHeight { get; set; } = 0.5f;

        /// <summary>
        /// 体型: 0.0~1.0 (默认 0.5, 0=纤细, 1=健壮)
        /// </summary>
        public float BodyType { get; set; } = 0.5f;

        /// <summary>
        /// 头部比例: 0.0~1.0 (默认 0.5)
        /// </summary>
        public float HeadSize { get; set; } = 0.5f;
    }

    /// <summary>
    /// 分步角色创建导航系统，管理创建流程中的步骤切换和数据存储
    /// </summary>
    public class StepNavigation
    {
        private CreationStep _currentStep;
        private StepData _stepData;

        /// <summary>
        /// 当前步骤
        /// </summary>
        public CreationStep CurrentStep
        {
            get => _currentStep;
            private set => _currentStep = value;
        }

        /// <summary>
        /// 存储所有步骤数据的对象
        /// </summary>
        public StepData StepData
        {
            get => _stepData;
            private set => _stepData = value;
        }

        /// <summary>
        /// 步骤改变事件，参数为 (旧步骤, 新步骤)
        /// </summary>
        public event Action<CreationStep, CreationStep> OnStepChanged;

        /// <summary>
        /// 创建步骤导航实例，默认从 GenderSelection 开始
        /// </summary>
        public StepNavigation()
        {
            _currentStep = CreationStep.GenderSelection;
            _stepData = new StepData();
            Debug.Log("[StepNavigation] 初始化，起始步骤: " + _currentStep);
        }

        /// <summary>
        /// 前进到下一步，如果已经是最后一步则不处理
        /// </summary>
        public void GoNext()
        {
            CreationStep oldStep = _currentStep;
            CreationStep nextStep = GetNextStep();

            if (nextStep > oldStep)
            {
                ChangeStep(oldStep, nextStep);
            }
            else
            {
                Debug.Log("[StepNavigation] 已经是最后一步，无法前进");
            }
        }

        /// <summary>
        /// 返回上一步，如果已经是第一步则不处理
        /// </summary>
        public void GoBack()
        {
            CreationStep oldStep = _currentStep;
            CreationStep previousStep = GetPreviousStep();

            if (previousStep < oldStep)
            {
                ChangeStep(oldStep, previousStep);
            }
            else
            {
                Debug.Log("[StepNavigation] 已经是第一步，无法后退");
            }
        }

        /// <summary>
        /// 跳转到指定步骤
        /// </summary>
        /// <param name="step">目标步骤</param>
        public void GoToStep(CreationStep step)
        {
            CreationStep oldStep = _currentStep;
            if (oldStep != step)
            {
                ChangeStep(oldStep, step);
            }
        }

        /// <summary>
        /// 重置到初始状态，回到 GenderSelection 并清空数据
        /// </summary>
        public void Reset()
        {
            CreationStep oldStep = _currentStep;
            _currentStep = CreationStep.GenderSelection;
            _stepData = new StepData();
            Debug.Log("[StepNavigation] 已重置到初始步骤: " + _currentStep);
            OnStepChanged?.Invoke(oldStep, _currentStep);
        }

        /// <summary>
        /// 获取当前步骤是否可以前进
        /// </summary>
        /// <returns>是否可以前进</returns>
        public bool CanGoNext()
        {
            return GetNextStep() > _currentStep;
        }

        /// <summary>
        /// 获取当前步骤是否可以后退
        /// </summary>
        /// <returns>是否可以后退</returns>
        public bool CanGoBack()
        {
            return GetPreviousStep() < _currentStep;
        }

        /// <summary>
        /// 获取下一步
        /// </summary>
        private CreationStep GetNextStep()
        {
            return (CreationStep)((int)_currentStep + 1);
        }

        /// <summary>
        /// 获取上一步
        /// </summary>
        private CreationStep GetPreviousStep()
        {
            return (CreationStep)((int)_currentStep - 1);
        }

        /// <summary>
        /// 改变步骤并触发事件
        /// </summary>
        private void ChangeStep(CreationStep oldStep, CreationStep newStep)
        {
            _currentStep = newStep;
            Debug.Log($"[StepNavigation] 步骤切换: {oldStep} -> {newStep}");
            OnStepChanged?.Invoke(oldStep, newStep);
        }
    }
}

using System;
using System.Collections.Generic;
using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI;
using HundunWorld.Game.UI.Components;
using HundunWorld.Game.UI.StyleSystem;
using Horizon.Game.Message.Network;

namespace HundunWorld.Game.UI.Character
{
    /// <summary>
    /// UI 组件管理器 - 负责 UI 组件的显示/隐藏、状态管理和更新
    /// 从 CharacterSceneController 中提取的职责：
    /// - 选择模式/创建模式 UI 可见性管理
    /// - 步骤 UI 切换
    /// - 步骤指示器更新
    /// - 按钮 hover 效果
    /// - 角色列表刷新
    /// </summary>
    public class UIComponentManager
    {
        private static readonly Color GoldColor = ChineseClassicalTheme.SecondaryColor;
        private static readonly Color GoldHighlightBg = ChineseClassicalTheme.SecondaryColorWithAlpha(0.25f);
        private static readonly string[] StepNames = { "选择性别", "选择面容", "精细捏脸", "命名完成" };
        private const int TotalSteps = 4;
        private const float CharItemHeight = 80f;
        private const float CharItemSpacing = 8f;

        private ContainerControl _guiContainer;
        private SelectionModeUIComponents _selectionUI;
        private CreationModeUIComponents _creationUI;
        private StepIndicatorComponents _stepIndicator;
        private NextStepButton _ctrlNextStepButton;
        private CharacterIdLabel _globalIdLabel;
        private UIFactory _uiFactory;

        private bool _isCreationMode = false;

        /// <summary>
        /// 是否处于创建模式
        /// </summary>
        public bool IsCreationMode => _isCreationMode;

        /// <summary>
        /// 全局 ID 标签控件
        /// </summary>
        public CharacterIdLabel GlobalIdLabel => _globalIdLabel;

        /// <summary>
        /// 控制器级下一步按钮
        /// </summary>
        public NextStepButton CtrlNextStepButton => _ctrlNextStepButton;

        public UIComponentManager(UIFactory factory)
        {
            _uiFactory = factory;
        }

        /// <summary>
        /// 初始化组件管理器
        /// </summary>
        public void Initialize(ContainerControl guiContainer, SelectionModeUIComponents selectionUI,
            CreationModeUIComponents creationUI, StepIndicatorComponents stepIndicator,
            NextStepButton ctrlNextStepButton, CharacterIdLabel globalIdLabel)
        {
            _guiContainer = guiContainer;
            _selectionUI = selectionUI;
            _creationUI = creationUI;
            _stepIndicator = stepIndicator;
            _ctrlNextStepButton = ctrlNextStepButton;
            _globalIdLabel = globalIdLabel;
        }

        /// <summary>
        /// 设置选择模式 UI 可见性
        /// </summary>
        public void SetSelectionModeVisible(bool visible)
        {
            var gui = _guiContainer;

            if (visible)
            {
                if (_selectionUI.TopBar != null && _selectionUI.TopBar.Parent == null) _selectionUI.TopBar.Parent = gui;
                if (_selectionUI.TitleLabel != null && _selectionUI.TitleLabel.Parent == null) _selectionUI.TitleLabel.Parent = gui;
                if (_selectionUI.LeftPanel != null && _selectionUI.LeftPanel.Parent == null) _selectionUI.LeftPanel.Parent = gui;
                if (_selectionUI.BottomBar != null && _selectionUI.BottomBar.Parent == null) _selectionUI.BottomBar.Parent = gui;
            }
            else
            {
                if (_selectionUI.TopBar != null) _selectionUI.TopBar.Parent = null;
                if (_selectionUI.TitleLabel != null) _selectionUI.TitleLabel.Parent = null;
                if (_selectionUI.LeftPanel != null) _selectionUI.LeftPanel.Parent = null;
                if (_selectionUI.BottomBar != null) _selectionUI.BottomBar.Parent = null;
            }
        }

        /// <summary>
        /// 设置创建模式 UI 可见性
        /// </summary>
        public void SetCreationModeVisible(bool visible)
        {
            if (_creationUI.GenderSelectionUI != null) _creationUI.GenderSelectionUI.Visible = false;
            if (_creationUI.FacePresetSelectionUI != null) _creationUI.FacePresetSelectionUI.Visible = false;
            if (_creationUI.IntegratedCreationUI != null) _creationUI.IntegratedCreationUI.Visible = false;
            if (_creationUI.NamingCompleteUI != null) _creationUI.NamingCompleteUI.Visible = false;

            if (visible)
            {
                ShowCurrentStepUI(CreationStep.GenderSelection);
            }
        }

        /// <summary>
        /// 根据当前步骤显示对应的 UI
        /// </summary>
        public void ShowCurrentStepUI(CreationStep currentStep)
        {
            if (_creationUI.GenderSelectionUI != null) _creationUI.GenderSelectionUI.Hide();
            if (_creationUI.FacePresetSelectionUI != null) _creationUI.FacePresetSelectionUI.Hide();
            if (_creationUI.IntegratedCreationUI != null) _creationUI.IntegratedCreationUI.Hide();
            if (_creationUI.NamingCompleteUI != null) _creationUI.NamingCompleteUI.Hide();

            if (_ctrlNextStepButton != null) _ctrlNextStepButton.Visible = false;

            switch (currentStep)
            {
                case CreationStep.GenderSelection:
                    _creationUI.GenderSelectionUI?.Show();
                    if (_ctrlNextStepButton != null) _ctrlNextStepButton.Visible = true;
                    break;
                case CreationStep.FacePreset:
                    if (_creationUI.FacePresetSelectionUI != null)
                    {
                        _creationUI.FacePresetSelectionUI.Show();
                    }
                    if (_ctrlNextStepButton != null) _ctrlNextStepButton.Visible = true;
                    break;
                case CreationStep.DetailedCreation:
                    if (_creationUI.IntegratedCreationUI != null)
                    {
                        _creationUI.IntegratedCreationUI.Show();
                    }
                    if (_ctrlNextStepButton != null) _ctrlNextStepButton.Visible = true;
                    break;
                case CreationStep.NamingComplete:
                    _creationUI.NamingCompleteUI?.Show();
                    break;
            }
        }

        /// <summary>
        /// 更新步骤指示器
        /// </summary>
        public void UpdateStepIndicator(CreationStep currentStep)
        {
            int stepIdx = 0;
            switch (currentStep)
            {
                case CreationStep.GenderSelection: stepIdx = 0; break;
                case CreationStep.FacePreset: stepIdx = 1; break;
                case CreationStep.DetailedCreation: stepIdx = 2; break;
                case CreationStep.NamingComplete: stepIdx = 3; break;
            }

            Color gold = GoldColor;
            Color dimDot = UIStyleTokens.TextDisabled;
            Color doneDot = new Color(gold.R * 0.7f, gold.G * 0.7f, gold.B * 0.7f, 0.8f);
            Color doneLine = new Color(gold.R * 0.5f, gold.G * 0.5f, gold.B * 0.5f, 0.4f);
            Color pendingLine = UIStyleTokens.Divider;

            for (int i = 0; i < TotalSteps; i++)
            {
                if (_stepIndicator.Dots[i] == null) continue;
                if (i < stepIdx)
                    _stepIndicator.Dots[i].BackgroundColor = doneDot;
                else if (i == stepIdx)
                    _stepIndicator.Dots[i].BackgroundColor = gold;
                else
                    _stepIndicator.Dots[i].BackgroundColor = dimDot;
            }

            for (int i = 0; i < _stepIndicator.Lines.Length; i++)
            {
                if (_stepIndicator.Lines[i] == null) continue;
                _stepIndicator.Lines[i].BackgroundColor = (i < stepIdx) ? doneLine : pendingLine;
            }

            if (_stepIndicator.NameLabel != null)
                _stepIndicator.NameLabel.Text = StepNames[stepIdx];
        }

        /// <summary>
        /// 更新步骤过渡动画
        /// </summary>
        public void UpdateStepTransition(StepNavigationManager stepNavManager)
        {
            if (!stepNavManager.IsTransitioning)
            {
                // 恢复圆点标准尺寸
                if (_stepIndicator.Dots != null)
                {
                    for (int i = 0; i < _stepIndicator.Dots.Length; i++)
                        if (_stepIndicator.Dots[i] != null) _stepIndicator.Dots[i].Size = new Float2(10, 10);
                }
                return;
            }

            // 过渡期间禁用按钮防止连点
            if (_ctrlNextStepButton != null)
                _ctrlNextStepButton.Enabled = false;

            // 当前步骤圆点脉动效果
            if (_stepIndicator.Dots != null)
            {
                int currentIdx = stepNavManager.GetStepIndex(stepNavManager.CurrentStep);
                if (currentIdx >= 0 && currentIdx < _stepIndicator.Dots.Length && _stepIndicator.Dots[currentIdx] != null)
                {
                    float progress = stepNavManager.GetTransitionProgress();
                    float pulse = (float)(Math.Sin(progress * Math.PI * 4) * 0.5 + 0.5);
                    float sz = 10f + pulse * 3f;
                    _stepIndicator.Dots[currentIdx].Size = new Float2(sz, sz);
                }
            }
        }

        /// <summary>
        /// 恢复按钮状态
        /// </summary>
        public void RestoreButtonState()
        {
            if (_ctrlNextStepButton != null)
                _ctrlNextStepButton.Enabled = true;
        }

        /// <summary>
        /// 更新按钮 hover 效果
        /// </summary>
        public void UpdateButtonHover()
        {
            if (_isCreationMode) return;

            UpdateButtonHover(_selectionUI.BackBtn, UIStyleTokens.BgElevated, UIStyleTokens.BgPaper);
            UpdateButtonHover(_selectionUI.CreateBtn, UIStyleTokens.BgPaper, UIStyleTokens.BgHover);
            if (_selectionUI.EnterBtn != null)
                UpdateButtonHover(_selectionUI.EnterBtn, GoldColor, new Color(GoldColor.R * 1.15f, GoldColor.G * 1.15f, GoldColor.B * 1.15f, 1f));
        }

        private void UpdateButtonHover(Button btn, Color normalColor, Color hoverColor)
        {
            if (btn == null || btn.Parent == null) return;
            btn.BackgroundColor = btn.IsMouseOver ? hoverColor : normalColor;
        }

        /// <summary>
        /// 创建模式下的强制清理: 每帧确保选择模式UI不在渲染树中
        /// </summary>
        public void EnforceCreationModeCleanup()
        {
            if (!_isCreationMode) return;

            if (_selectionUI.TopBar != null && _selectionUI.TopBar.Parent != null) _selectionUI.TopBar.Parent = null;
            if (_selectionUI.TitleLabel != null && _selectionUI.TitleLabel.Parent != null) _selectionUI.TitleLabel.Parent = null;
            if (_selectionUI.LeftPanel != null && _selectionUI.LeftPanel.Parent != null) _selectionUI.LeftPanel.Parent = null;
            if (_selectionUI.BottomBar != null && _selectionUI.BottomBar.Parent != null) _selectionUI.BottomBar.Parent = null;
        }

        /// <summary>
        /// 进入创建模式
        /// </summary>
        public void EnterCreationMode()
        {
            _isCreationMode = true;
            SetSelectionModeVisible(false);
            SetCreationModeVisible(true);
        }

        /// <summary>
        /// 退出创建模式
        /// </summary>
        public void ExitCreationMode()
        {
            _isCreationMode = false;
            SetCreationModeVisible(false);
            SetSelectionModeVisible(true);
        }

        /// <summary>
        /// 刷新角色列表
        /// </summary>
        public void RefreshCharacterList(List<CharacterInfo> characters, CharacterInfo selectedCharacter)
        {
            if (_selectionUI.CharacterScrollView == null || _selectionUI.HintLabel == null) return;

            _selectionUI.CharacterScrollView.RemoveChildren();

            if (characters == null || characters.Count == 0)
            {
                _selectionUI.HintLabel.Visible = true;
                _selectionUI.CharacterScrollView.Visible = false;
                if (_selectionUI.EnterBtn != null) _selectionUI.EnterBtn.Enabled = false;
                return;
            }

            _selectionUI.HintLabel.Visible = false;
            _selectionUI.CharacterScrollView.Visible = true;

            float listWidth = _selectionUI.CharacterScrollView.Width;
            for (int i = 0; i < characters.Count; i++)
            {
                var character = characters[i];
                bool isSelected = selectedCharacter != null && selectedCharacter.CharacterId == character.CharacterId;
                var itemPanel = _uiFactory.CreateCharacterListItem(character, listWidth, isSelected, (c) =>
                {
                    // 点击回调由外部处理
                    OnCharacterItemSelected?.Invoke(c);
                });
                itemPanel.Parent = _selectionUI.CharacterScrollView;
                float yPos = i * (CharItemHeight + CharItemSpacing);
                itemPanel.Location = new Float2(0, yPos);
                itemPanel.Size = new Float2(listWidth, CharItemHeight);
            }

            if (_selectionUI.EnterBtn != null) _selectionUI.EnterBtn.Enabled = selectedCharacter != null;
        }

        /// <summary>
        /// 角色列表项被选中事件
        /// </summary>
        public event Action<CharacterInfo> OnCharacterItemSelected;

        /// <summary>
        /// 设置面容预设步骤的性别
        /// </summary>
        public void SetFacePresetGender(int gender)
        {
            _creationUI.FacePresetSelectionUI?.SetGender(gender == 0 ? "male" : "female");
        }

        /// <summary>
        /// 设置精细捏脸步骤的数据
        /// </summary>
        public void SetIntegratedCreationStepData(StepData data)
        {
            _creationUI.IntegratedCreationUI?.SetStepData(data);
        }

        /// <summary>
        /// 更新全局 ID 标签
        /// </summary>
        public void UpdateGlobalIdLabel(string id)
        {
            if (_globalIdLabel != null)
            {
                _globalIdLabel.CharacterId = id;
            }
        }
    }
}

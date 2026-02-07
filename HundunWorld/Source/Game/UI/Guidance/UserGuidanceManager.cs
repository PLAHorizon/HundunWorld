using System;
using System.Collections.Generic;
using FlaxEngine;
using FlaxEngine.GUI;

namespace HundunWorld.Game.UI.Guidance
{
    /// <summary>
    /// 用户引导管理器
    /// 提供新手引导和功能介绍
    /// </summary>
    public class UserGuidanceManager : Script
    {
        private static UserGuidanceManager _instance;
        private List<GuidanceStep> _currentGuidance = new List<GuidanceStep>();
        private int _currentStepIndex = 0;
        private Panel _guidanceOverlay;
        private Panel _guidancePanel;
        private Label _titleLabel;
        private Label _descriptionLabel;
        private Button _nextButton;
        private Button _skipButton;
        private Panel _highlightPanel;

        public static UserGuidanceManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    var gameObject = Level.FindActor("UserGuidanceManager") ?? new EmptyActor();
                    gameObject.Name = "UserGuidanceManager";
                    _instance = gameObject.GetScript<UserGuidanceManager>() ?? gameObject.AddScript<UserGuidanceManager>();
                }
                return _instance;
            }
        }

        public override void OnAwake()
        {
            if (_instance == null)
            {
                _instance = this;
                // 确保跨场景持久化
                Actor.SetStaticFlag(StaticFlags.FullyStatic, true);
            }
            else if (_instance != this)
            {
                // 销毁多余的实例
                Destroy(Actor);
                return;
            }
        }

        public override void OnStart()
        {
            CreateGuidanceUI();
            FlaxEngine.Debug.Log("用户引导管理器初始化完成");
        }

        private void CreateGuidanceUI()
        {
            // 创建引导遮罩层
            _guidanceOverlay = new Panel
            {
                AnchorPreset = AnchorPresets.StretchAll,
                BackgroundColor = new Color(0, 0, 0, 0.7f),
                Visible = false
            };

            // 创建引导面板
            _guidancePanel = new Panel
            {
                AnchorPreset = AnchorPresets.BottomCenter,
                Size = new Float2(400, 150),
                Y = -200,
                BackgroundColor = new Color(0.2f, 0.2f, 0.25f, 0.95f)
            };

            // 标题
            _titleLabel = new Label
            {
                Text = "引导标题",
                Location = new Float2(20, 20),
                Size = new Float2(360, 30),
                TextColor = Color.White,
                HorizontalAlignment = TextAlignment.Center
            };
            _guidancePanel.AddChild(_titleLabel);

            // 描述
            _descriptionLabel = new Label
            {
                Text = "引导描述内容",
                Location = new Float2(20, 55),
                Size = new Float2(360, 60),
                TextColor = Color.LightGray,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center
            };
            _guidancePanel.AddChild(_descriptionLabel);

            // 下一步按钮
            _nextButton = new Button
            {
                Text = "下一步",
                Location = new Float2(220, 110),
                Size = new Float2(80, 30),
                BackgroundColor = new Color(0.3f, 0.6f, 0.3f),
                TextColor = Color.White
            };
            _nextButton.ButtonClicked += OnNextButtonClicked;
            _guidancePanel.AddChild(_nextButton);

            // 跳过按钮
            _skipButton = new Button
            {
                Text = "跳过",
                Location = new Float2(310, 110),
                Size = new Float2(60, 30),
                BackgroundColor = new Color(0.6f, 0.3f, 0.3f),
                TextColor = Color.White
            };
            _skipButton.ButtonClicked += OnSkipButtonClicked;
            _guidancePanel.AddChild(_skipButton);

            // 高亮面板
            _highlightPanel = new Panel
            {
                BackgroundColor = Color.Transparent,
                Visible = false
            };

            _guidanceOverlay.AddChild(_guidancePanel);
            _guidanceOverlay.AddChild(_highlightPanel);
        }

        /// <summary>
        /// 开始引导
        /// </summary>
        public void StartGuidance(List<GuidanceStep> steps)
        {
            if (steps == null || steps.Count == 0)
            {
                FlaxEngine.Debug.LogWarning("引导步骤为空");
                return;
            }

            _currentGuidance = steps;
            _currentStepIndex = 0;

            ShowGuidanceOverlay();
            ShowCurrentStep();
        }

        /// <summary>
        /// 显示当前步骤
        /// </summary>
        private void ShowCurrentStep()
        {
            if (_currentStepIndex >= _currentGuidance.Count)
            {
                CompleteGuidance();
                return;
            }

            var step = _currentGuidance[_currentStepIndex];

            _titleLabel.Text = step.Title;
            _descriptionLabel.Text = step.Description;

            // 更新按钮文本
            _nextButton.Text = _currentStepIndex == _currentGuidance.Count - 1 ? "完成" : "下一步";

            // 显示高亮
            if (step.ShowHighlight && !string.IsNullOrEmpty(step.TargetElementId))
            {
                ShowHighlight(step.Position, new Float2(100, 100)); // 简化的高亮显示
            }
            else
            {
                HideHighlight();
            }
        }

        private void ShowGuidanceOverlay()
        {
            if (_guidanceOverlay == null) CreateGuidanceUI();
            _guidanceOverlay.Visible = true;
        }

        private void HideGuidanceOverlay()
        {
            _guidanceOverlay.Visible = false;
        }

        private void ShowHighlight(Float2 position, Float2 size)
        {
            _highlightPanel.Location = position;
            _highlightPanel.Size = size;
            _highlightPanel.Visible = true;
        }

        private void HideHighlight()
        {
            _highlightPanel.Visible = false;
        }

        private void OnNextButtonClicked(Button sender)
        {
            var currentStep = _currentGuidance[_currentStepIndex];
            currentStep.OnComplete?.Invoke();

            _currentStepIndex++;
            ShowCurrentStep();
        }

        private void OnSkipButtonClicked(Button sender)
        {
            CompleteGuidance();
        }

        private void CompleteGuidance()
        {
            HideGuidanceOverlay();
            _currentGuidance.Clear();
            _currentStepIndex = 0;

            FlaxEngine.Debug.Log("引导完成");
        }

        #region 预设引导

        /// <summary>
        /// 创建登录引导
        /// </summary>
        public static List<GuidanceStep> CreateLoginGuidance()
        {
            return new List<GuidanceStep>
            {
                new GuidanceStep("login_welcome", "欢迎来到混沌世界", "这是一个充满冒险的世界，让我们开始您的旅程吧！"),
                new GuidanceStep("login_input", "输入账户信息", "请输入您的用户名和密码，如果没有账户请点击注册"),
                new GuidanceStep("login_submit", "登录游戏", "点击登录按钮进入游戏")
            };
        }

        /// <summary>
        /// 创建角色创建引导
        /// </summary>
        public static List<GuidanceStep> CreateCharacterCreationGuidance()
        {
            return new List<GuidanceStep>
            {
                new GuidanceStep("char_name", "起个好名字", "为您的角色起一个独特的名字"),
                new GuidanceStep("char_profession", "选择职业", "选择适合您游戏风格的职业"),
                new GuidanceStep("char_appearance", "定制外观", "调整角色的外观特征"),
                new GuidanceStep("char_create", "创建角色", "确认设置并创建您的角色")
            };
        }

        #endregion
    }
}
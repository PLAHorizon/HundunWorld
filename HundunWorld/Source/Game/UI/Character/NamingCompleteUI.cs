using System;
using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Components;
using HundunWorld.Game.UI.StyleSystem;

namespace HundunWorld.Game.UI.Character
{
    /// <summary>
    /// 命名完成界面 - 角色创建流程第4步
    /// 输入角色名称,选择职业,确认创建
    /// 风格: 燕云十六声古典水墨
    /// </summary>
    public class NamingCompleteUI : ContainerControl
    {
        #region Events
        /// <summary>
        /// 点击返回时触发
        /// </summary>
        public event Action OnGoBack;

        /// <summary>
        /// 确认创建时触发,参数为角色名称
        /// </summary>
        public event Action<string> OnComplete;
        #endregion

        #region State
        private bool _uiCreated = false;
        private string _characterName = "";
        private int _selectedProfession = 0;
        private CharacterPreviewPanel _previewPanel;
        private int _hoveredProfession = -1;

        // 入场动画状态
        private bool _isAnimating = false;
        private float _animProgress = 0f;
        private const float AnimDuration = 0.25f;

        // 验证反馈
        private Label _validationLabel;
        private float _validationTimer = 0f;

        // 职业列表
        private static readonly string[] Professions = { "剑客", "刀客", "医师", "琴师", "游侠", "隐士" };

        /// <summary>
        /// 当前选中的职业索引 (0-5)
        /// </summary>
        public int SelectedProfessionIndex => _selectedProfession;
        #endregion

        #region UI Components
        private ContainerControl _centerPanel;
        private Label _titleLabel;
        private Label _subtitleLabel;

        // 名称输入
        private ContainerControl _nameInputContainer;
        private Label _nameInputLabel;
        private TextBox _nameTextBox;
        private Panel _nameUnderline;

        // 职业选择
        private ContainerControl _professionContainer;
        private Label _professionLabel;
        private Label[] _professionButtons;

        // 操作按钮
        private Button _backButton;
        private Button _confirmButton;

        // 装饰元素
        private Panel _topDecorLine;
        private Panel _bottomDecorLine;
        #endregion

        #region Constructor
        public NamingCompleteUI()
        {
            AnchorPreset = AnchorPresets.StretchAll;
            Offsets = Margin.Zero;
            BackgroundColor = Color.Transparent;
        }

        public void SetPreviewPanel(CharacterPreviewPanel previewPanel)
        {
            _previewPanel = previewPanel;
        }

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);

            if (!_uiCreated && Parent != null && Parent.Width > 0 && Parent.Height > 0)
            {
                _uiCreated = true;
                CreateUI();
            }

            // 入场动画: 从下方滑入
            if (_isAnimating && _centerPanel != null)
            {
                _animProgress += deltaTime / AnimDuration;
                if (_animProgress >= 1f)
                {
                    _animProgress = 1f;
                    _isAnimating = false;
                }
                float ease = 1f - (1f - _animProgress) * (1f - _animProgress); // easeOut
                float targetY = (Parent.Height - _centerPanel.Height) / 2f;
                _centerPanel.Location = new Float2(_centerPanel.Location.X, targetY + (1f - ease) * 30f);
            }

            // 职业按钮 hover 检测 (Label 不支持 hover 事件)
            if (_professionButtons != null && Visible)
            {
                int newHovered = -1;
                if (_professionContainer != null)
                {
                    for (int i = 0; i < _professionButtons.Length; i++)
                    {
                        if (_professionButtons[i] != null && _professionButtons[i].IsMouseOver)
                        {
                            newHovered = i;
                            break;
                        }
                    }
                }
                if (newHovered != _hoveredProfession)
                {
                    _hoveredProfession = newHovered;
                    UpdateProfessionVisuals();
                }
            }

            // 验证提示自动消失
            if (_validationTimer > 0)
            {
                _validationTimer -= deltaTime;
                if (_validationTimer <= 0 && _validationLabel != null)
                {
                    _validationLabel.Visible = false;
                    _nameUnderline.BackgroundColor = UIStyleTokens.GoldPrimary;
                }
            }
        }
        #endregion

        #region UI Creation
        private void CreateUI()
        {
            float W = Parent.Width;
            float H = Parent.Height;

            // 中央面板 - 半透明磨砂背景
            float panelW = 420f;
            float panelH = 480f;
            float panelX = (W - panelW) / 2f;
            float panelY = (H - panelH) / 2f;

            _centerPanel = new ContainerControl
            {
                Parent = this,
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = UIStyleTokens.BgPanel
            };
            _centerPanel.Location = new Float2(panelX, panelY);
            _centerPanel.Size = new Float2(panelW, panelH);

            // 顶部金色装饰线
            _topDecorLine = new Panel
            {
                Parent = _centerPanel,
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = UIStyleTokens.GoldPrimary
            };
            _topDecorLine.Location = new Float2(panelW * 0.15f, 0);
            _topDecorLine.Size = new Float2(panelW * 0.7f, 2);

            // 标题
            _titleLabel = new Label
            {
                Parent = _centerPanel,
                AnchorPreset = AnchorPresets.TopLeft,
                Text = "赐名立命",
                TextColor = UIStyleTokens.TextGold,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                Font = UIHelper.SetFont(size: 28)
            };
            _titleLabel.Location = new Float2(0, 25);
            _titleLabel.Size = new Float2(panelW, 45);

            // 副标题
            _subtitleLabel = new Label
            {
                Parent = _centerPanel,
                AnchorPreset = AnchorPresets.TopLeft,
                Text = "行走江湖,当有名号",
                TextColor = UIStyleTokens.WithAlpha(UIStyleTokens.TextSecondary, 0.8f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                Font = UIHelper.SetFont(size: 14)
            };
            _subtitleLabel.Location = new Float2(0, 70);
            _subtitleLabel.Size = new Float2(panelW, 30);

            // 名称输入区域
            CreateNameInput(panelW);

            // 职业选择区域
            CreateProfessionSelector(panelW);

            // 底部装饰线
            _bottomDecorLine = new Panel
            {
                Parent = _centerPanel,
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = UIStyleTokens.Gold(0.3f)
            };
            _bottomDecorLine.Location = new Float2(panelW * 0.15f, panelH - 80);
            _bottomDecorLine.Size = new Float2(panelW * 0.7f, 1);

            // 操作按钮
            CreateActionButtons(panelW, panelH);
        }

        private void CreateNameInput(float panelW)
        {
            _nameInputContainer = new ContainerControl
            {
                Parent = _centerPanel,
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = Color.Transparent
            };
            _nameInputContainer.Location = new Float2(40, 115);
            _nameInputContainer.Size = new Float2(panelW - 80, 70);

            _nameInputLabel = new Label
            {
                Parent = _nameInputContainer,
                AnchorPreset = AnchorPresets.TopLeft,
                Text = "角色名称",
                TextColor = UIStyleTokens.TextSecondary,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                Font = UIHelper.SetFont(size: 14)
            };
            _nameInputLabel.Location = new Float2(0, 0);
            _nameInputLabel.Size = new Float2(panelW - 80, 24);

            _nameTextBox = new TextBox
            {
                Parent = _nameInputContainer,
                AnchorPreset = AnchorPresets.TopLeft,
                Text = "",
                WatermarkText = "请输入角色名称（2-6字）",
                BackgroundColor = Color.Transparent,
                TextColor = UIStyleTokens.TextPrimary,
                Font = UIHelper.SetFont(size: 22),
                BorderColor = Color.Transparent,
                BorderThickness = 0f
            };
            _nameTextBox.Location = new Float2(0, 28);
            _nameTextBox.Size = new Float2(panelW - 80, 36);

            // 金色下划线
            _nameUnderline = new Panel
            {
                Parent = _nameInputContainer,
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = UIStyleTokens.GoldPrimary
            };
            _nameUnderline.Location = new Float2(0, 64);
            _nameUnderline.Size = new Float2(panelW - 80, 1.5f);

            // 验证错误提示（初始隐藏）
            _validationLabel = new Label
            {
                Parent = _nameInputContainer,
                AnchorPreset = AnchorPresets.TopLeft,
                Text = "",
                TextColor = UIStyleTokens.TextBlood, // 血色强调（--ink-text-blood）
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                Font = UIHelper.SetFont(size: 12),
                Visible = false
            };
            _validationLabel.Location = new Float2(0, 67);
            _validationLabel.Size = new Float2(panelW - 80, 18);
        }

        private void CreateProfessionSelector(float panelW)
        {
            _professionContainer = new ContainerControl
            {
                Parent = _centerPanel,
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = Color.Transparent
            };
            _professionContainer.Location = new Float2(40, 200);
            _professionContainer.Size = new Float2(panelW - 80, 170);

            _professionLabel = new Label
            {
                Parent = _professionContainer,
                AnchorPreset = AnchorPresets.TopLeft,
                Text = "选择职业",
                TextColor = UIStyleTokens.TextSecondary,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                Font = UIHelper.SetFont(size: 14)
            };
            _professionLabel.Location = new Float2(0, 0);
            _professionLabel.Size = new Float2(panelW - 80, 24);

            // 职业按钮网格: 3列2行
            _professionButtons = new Label[Professions.Length];
            int cols = 3;
            float btnW = (panelW - 80 - 20) / cols; // 20 = 2 * 10 spacing
            float btnH = 44f;
            float spacingX = 10f;
            float spacingY = 10f;

            for (int i = 0; i < Professions.Length; i++)
            {
                int col = i % cols;
                int row = i / cols;
                float x = col * (btnW + spacingX);
                float y = 34 + row * (btnH + spacingY);

                var btn = new Label
                {
                    Parent = _professionContainer,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Text = Professions[i],
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                    Font = UIHelper.SetFont(size: 14),
                    BackgroundColor = UIStyleTokens.InkPanel(0.8f),
                    TextColor = UIStyleTokens.TextPrimary
                };
                btn.Location = new Float2(x, y);
                btn.Size = new Float2(btnW, btnH);

                _professionButtons[i] = btn;
            }

            // 默认选中第一个
            UpdateProfessionVisuals();
        }

        private void CreateActionButtons(float panelW, float panelH)
        {
            float btnW = 140f;
            float btnH = 44f;
            float btnY = panelH - 65;
            float spacing = 20f;
            float totalW = 2 * btnW + spacing;
            float startX = (panelW - totalW) / 2f;

            // 返回按钮
            _backButton = new Button
            {
                Parent = _centerPanel,
                AnchorPreset = AnchorPresets.TopLeft,
                Text = "返回上一步",
                BackgroundColor = UIStyleTokens.WithAlpha(UIStyleTokens.BgElevated, 0.8f),
                TextColor = new Color(0.7f, 0.7f, 0.75f),
                Font = UIHelper.SetFont(size: 14)
            };
            _backButton.Location = new Float2(startX, btnY);
            _backButton.Size = new Float2(btnW, btnH);
            _backButton.Clicked += () => OnGoBack?.Invoke();

            // 确认创建按钮（金色主色调）
            _confirmButton = new Button
            {
                Parent = _centerPanel,
                AnchorPreset = AnchorPresets.TopLeft,
                Text = "确认创建",
                BackgroundColor = UIStyleTokens.GoldPrimary,
                TextColor = UIStyleTokens.TextInverse,
                Font = UIHelper.SetFont(size: 14)
            };
            _confirmButton.Location = new Float2(startX + btnW + spacing, btnY);
            _confirmButton.Size = new Float2(btnW, btnH);
            _confirmButton.Clicked += OnConfirmClicked;
        }
        #endregion

        #region Input Handling
        private void ShowValidationError(string message)
        {
            if (_nameUnderline != null)
                _nameUnderline.BackgroundColor = UIStyleTokens.BloodPrimary;
            if (_validationLabel != null)
            {
                _validationLabel.Text = message;
                _validationLabel.Visible = true;
            }
            _validationTimer = 2.5f;
        }

        private void OnConfirmClicked()
        {
            string name = _nameTextBox?.Text?.Trim();

            if (string.IsNullOrEmpty(name))
            {
                ShowValidationError("角色名称不能为空");
                return;
            }

            if (name.Length < 2)
            {
                ShowValidationError("角色名称至少需要2个字");
                return;
            }

            if (name.Length > 6)
            {
                ShowValidationError("角色名称不能超过6个字");
                return;
            }

            _characterName = name;
            OnComplete?.Invoke(name);
        }

        public override bool OnMouseDown(Float2 location, MouseButton button)
        {
            if (button == MouseButton.Left && _professionButtons != null)
            {
                for (int i = 0; i < _professionButtons.Length; i++)
                {
                    if (_professionButtons[i] != null && IsPointInControl(_professionButtons[i], location))
                    {
                        _selectedProfession = i;
                        UpdateProfessionVisuals();
                        return true;
                    }
                }
            }
            return base.OnMouseDown(location, button);
        }

        private bool IsPointInControl(Control control, Float2 point)
        {
            if (control == null) return false;
            var bounds = new Rectangle(control.Location, control.Size);
            return bounds.Contains(point);
        }
        #endregion

        #region Visual Update
        private void UpdateProfessionVisuals()
        {
            if (_professionButtons == null) return;

            Color gold = UIStyleTokens.GoldPrimary;
            Color goldBg = new Color(gold.R, gold.G, gold.B, 0.2f);
            Color hoverBg = UIStyleTokens.BgHover;
            Color normalText = UIStyleTokens.TextPrimary;
            Color normalBg = UIStyleTokens.InkPanel(0.8f);

            for (int i = 0; i < _professionButtons.Length; i++)
            {
                if (_professionButtons[i] == null) continue;

                if (i == _selectedProfession)
                {
                    _professionButtons[i].TextColor = gold;
                    _professionButtons[i].BackgroundColor = goldBg;
                }
                else if (i == _hoveredProfession)
                {
                    _professionButtons[i].TextColor = UIStyleTokens.TextPrimary;
                    _professionButtons[i].BackgroundColor = hoverBg;
                }
                else
                {
                    _professionButtons[i].TextColor = normalText;
                    _professionButtons[i].BackgroundColor = normalBg;
                }
            }
        }
        #endregion

        #region Public Methods
        public void Show()
        {
            Visible = true;
            _isAnimating = true;
            _animProgress = 0f;
            // 重置验证状态
            if (_validationLabel != null) _validationLabel.Visible = false;
            if (_nameUnderline != null) _nameUnderline.BackgroundColor = UIStyleTokens.GoldPrimary;
            _validationTimer = 0f;
        }

        public void Hide()
        {
            Visible = false;
        }
        #endregion
    }
}

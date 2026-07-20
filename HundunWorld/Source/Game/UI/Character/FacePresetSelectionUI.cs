using System;
using System.Collections.Generic;
using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Components;
using HundunWorld.Game.UI.StyleSystem;

namespace HundunWorld.Game.UI.Character
{
    /// <summary>
    /// 捏脸预设选择界面（角色创建流程第2步）
    /// 风格参考燕云十六声
    /// </summary>
    public class FacePresetSelectionUI : ContainerControl
    {
        #region Events
        public event Action OnNextStep;
        public event Action OnGoBack;
        #endregion

        #region Properties
        public FacePresetData SelectedPreset { get; private set; }
        #endregion

        #region State
        private int _selectedMainTab = 0; // 0=捏脸, 1=智能捏脸
        private int _selectedSubTab = 0;  // 0=风雅, 1=写实
        private string _gender = "male";
        private bool _uiCreated = false;
        #endregion

        #region UI Components - Top Tab Bar
        private ContainerControl _topTabBar;
        private Label _mainTab1;
        private Label _mainTab2;
        private Panel _mainTab1Underline;
        private Panel _mainTab2Underline;
        #endregion

        #region UI Components - Sub Tab Bar
        private ContainerControl _subTabBar;
        private Label _subTab1;
        private Label _subTab2;
        private Panel _subTab1Underline;
        private Panel _subTab2Underline;
        #endregion

        #region UI Components - Preset Cards
        private ScrollableControl _presetScrollView;
        private List<FacePresetCard> _presetCards = new List<FacePresetCard>();
        private Button _morePresetsButton;
        #endregion

        #region UI Components - Right Icons
        private ContainerControl _rightIcons;
        private ContainerControl _wanxiangjiButton;
        private ContainerControl _shareButton;
        private ContainerControl _clothesIcon;
        private ContainerControl _hatIcon;
        private ContainerControl _bodyIcon;
        #endregion

        #region UI Components - Bottom Actions
        private ContainerControl _bottomLeftActions;
        private Label _backButton;
        private Label _importButton;
        private NextStepButton _nextStepButton;
        #endregion

        #region Constructor
        public FacePresetSelectionUI()
        {
            AnchorPreset = AnchorPresets.StretchAll;
            Offsets = Margin.Zero;
            BackgroundColor = Color.Transparent;
            // 延迟创建UI，确保父控件有正确的尺寸
        }

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);

            if (!_uiCreated && Parent != null && Parent.Width > 0 && Parent.Height > 0)
            {
                _uiCreated = true;
                CreateUI();
            }

            if (_uiCreated && Visible)
            {
                if (Input.GetKeyDown(KeyboardKeys.Q)) SwitchMainTab(0);
                if (Input.GetKeyDown(KeyboardKeys.E)) SwitchMainTab(1);
                if (Input.GetKeyDown(KeyboardKeys.Z)) SwitchSubTab(0);
                if (Input.GetKeyDown(KeyboardKeys.C)) SwitchSubTab(1);
                if (Input.GetKeyDown(KeyboardKeys.V)) ImportFaceData();
                if (Input.GetKeyDown(KeyboardKeys.Spacebar)) OnNextStep?.Invoke();
            }
        }
        #endregion

        #region CreateUI
        private void CreateUI()
        {
            CreateTopTabBar();
            CreateSubTabBar();
            CreatePresetCards();
            CreateRightIcons();
            CreateBottomActions();
            CreateNextStepButton();
        }

        private void CreateTopTabBar()
        {
            _topTabBar = new ContainerControl
            {
                Parent = this,
                AnchorPreset = AnchorPresets.HorizontalStretchTop,
                Offsets = new Margin(0, 0, 20, 60),
                Height = 40,
                BackgroundColor = Color.Transparent
            };

            _mainTab1 = new Label
            {
                Parent = _topTabBar,
                Text = "Q 捏脸",
                Location = new Float2(160, 5),
                Size = new Float2(120, 35),
                Font = UIHelper.SetFont(size: 22),
                TextColor = ChineseClassicalTheme.SecondaryColor,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center
            };

            _mainTab1Underline = new Panel
            {
                Parent = _topTabBar,
                Size = new Float2(80, 3),
                Location = new Float2(180, 42),
                BackgroundColor = ChineseClassicalTheme.SecondaryColor
            };

            _mainTab2 = new Label
            {
                Parent = _topTabBar,
                Text = "E 智能捏脸",
                Location = new Float2(300, 5),
                Size = new Float2(160, 35),
                Font = UIHelper.SetFont(size: 22),
                TextColor = UIStyleTokens.WithAlpha(UIStyleTokens.TextPrimary, 0.4f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center
            };

            _mainTab2Underline = new Panel
            {
                Parent = _topTabBar,
                Size = new Float2(80, 3),
                Location = new Float2(340, 42),
                BackgroundColor = ChineseClassicalTheme.SecondaryColorWithAlpha(0f)
            };
        }

        private void CreateSubTabBar()
        {
            _subTabBar = new ContainerControl
            {
                Parent = this,
                AnchorPreset = AnchorPresets.HorizontalStretchTop,
                Offsets = new Margin(0, 0, 65, 95),
                Height = 30,
                BackgroundColor = Color.Transparent
            };

            _subTab1 = new Label
            {
                Parent = _subTabBar,
                Text = "Z 风雅",
                Location = new Float2(180, 2),
                Size = new Float2(100, 28),
                Font = UIHelper.SetFont(size: 18),
                TextColor = ChineseClassicalTheme.SecondaryColor,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center
            };

            _subTab1Underline = new Panel
            {
                Parent = _subTabBar,
                Size = new Float2(60, 2),
                Location = new Float2(200, 30),
                BackgroundColor = ChineseClassicalTheme.SecondaryColor
            };

            _subTab2 = new Label
            {
                Parent = _subTabBar,
                Text = "C 写实",
                Location = new Float2(300, 2),
                Size = new Float2(100, 28),
                Font = UIHelper.SetFont(size: 18),
                TextColor = UIStyleTokens.WithAlpha(UIStyleTokens.TextPrimary, 0.4f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center
            };

            _subTab2Underline = new Panel
            {
                Parent = _subTabBar,
                Size = new Float2(60, 2),
                Location = new Float2(320, 30),
                BackgroundColor = ChineseClassicalTheme.SecondaryColorWithAlpha(0f)
            };
        }

        private void CreatePresetCards()
        {
            // 预设列表固定宽度 320px，保持左对齐 (Offsets.Left = 20)
            // 通过动态 Offsets.Right 确保宽度 = 320：Right = Parent.Width - 340
            float rightOffset = (Parent != null ? Parent.Width : 1920f) - 340f;
            _presetScrollView = new ScrollableControl
            {
                Parent = this,
                AnchorPreset = AnchorPresets.VerticalStretchLeft,
                Offsets = new Margin(20, 130, rightOffset, 100),
                Width = 320,
                BackgroundColor = Color.Transparent
            };

            PopulatePresetCards();
        }

        private void PopulatePresetCards()
        {
            _presetScrollView.RemoveChildren();
            _presetCards.Clear();

            var presets = FacePresetData.GetDefaultPresets(_gender);

            for (int i = 0; i < presets.Count; i++)
            {
                var preset = presets[i];
                var card = new FacePresetCard(preset)
                {
                    Parent = _presetScrollView,
                    Location = new Float2(10, i * 125)
                };

                card.OnSelected += OnPresetCardSelected;
                _presetCards.Add(card);
            }

            // 更多面容按钮（与 BottomActionBar 风格一致：140x44, Font 18，金色边框）
            var morePresetsY = presets.Count * 125 + 5;
            _morePresetsButton = new Button
            {
                Parent = _presetScrollView,
                Text = "更多面容 \u25BC",
                Location = new Float2(10, morePresetsY),
                Size = new Float2(140, 44),
                BackgroundColor = UIStyleTokens.InkPanel(0.8f),
                TextColor = UIStyleTokens.TextPrimary,
                BorderColor = ChineseClassicalTheme.BorderColor,
                BorderThickness = 1.5f,
                Font = UIHelper.SetFont(size: 18)
            };
        }

        private void OnPresetCardSelected(FacePresetCard selectedCard)
        {
            foreach (var card in _presetCards)
            {
                if (card != selectedCard)
                {
                    card.IsSelected = false;
                }
            }

            SelectedPreset = selectedCard.PresetData;
        }
        #endregion

        #region CreateUI - Right Icons
        private static readonly Color ThumbSelectedBorder = ChineseClassicalTheme.SecondaryColor;
        private static readonly Color ThumbNormalBg = UIStyleTokens.BgPanel;
        private static readonly Color ThumbLabelColor = UIStyleTokens.WithAlpha(UIStyleTokens.TextPrimary, 0.9f);
        private const float ThumbSize = 56f;
        private const float ThumbSpacing = 6f;
        private const float SmallThumbSize = 48f;

        private void CreateRightIcons()
        {
            _rightIcons = new ContainerControl
            {
                Parent = this,
                AnchorPreset = AnchorPresets.VerticalStretchRight,
                Offsets = new Margin(0, 20, 100, 100),
                Width = 80,
                BackgroundColor = Color.Transparent
            };

            float y = 0;

            // === 万相集缩略图（选中态，金色边框） ===
            _wanxiangjiButton = CreatePortraitThumbnail("万相集", y, ThumbSize, true);
            _wanxiangjiButton.Parent = _rightIcons;
            y += ThumbSize + 4f;

            // === 分享缩略图 ===
            _shareButton = CreatePortraitThumbnail("分享", y, ThumbSize, false);
            _shareButton.Parent = _rightIcons;
            y += ThumbSize + 12f;

            // === 预设面容小缩略图（5个） ===
            for (int i = 0; i < 5; i++)
            {
                var thumb = CreatePortraitThumbnail("", y, SmallThumbSize, false);
                thumb.Parent = _rightIcons;
                y += SmallThumbSize + ThumbSpacing;
            }

            // === 底部圆形分类图标 ===
            y += 8f;
            _clothesIcon = CreateCircularIconButton("衣", y, 44f, false);
            _clothesIcon.Parent = _rightIcons;
            y += 44f + 6f;
            _hatIcon = CreateCircularIconButton("帽", y, 44f, false);
            _hatIcon.Parent = _rightIcons;
            y += 44f + 6f;
            _bodyIcon = CreateCircularIconButton("体", y, 44f, false);
            _bodyIcon.Parent = _rightIcons;
        }

        /// <summary>
        /// 创建面容缩略图卡片（方形，带可选金色边框）
        /// </summary>
        private ContainerControl CreatePortraitThumbnail(string label, float y, float size, bool isSelected)
        {
            var container = new ContainerControl
            {
                Location = new Float2(0, y),
                Size = new Float2(size + 4f, size + (string.IsNullOrEmpty(label) ? 0 : 22f)),
                BackgroundColor = Color.Transparent
            };

            // 背景面板
            var panel = new Panel
            {
                Parent = container,
                Location = new Float2(2, 0),
                Size = new Float2(size, size),
                BackgroundColor = ThumbNormalBg
            };

            // 选中时绘制金色边框
            if (isSelected)
            {
                var border = new Panel
                {
                    Parent = container,
                    Location = new Float2(0, 0),
                    Size = new Float2(size + 4f, size),
                    BackgroundColor = Color.Transparent
                };
                // 用四边 Panel 模拟边框
                float bw = 2f;
                new Panel { Parent = border, Location = Float2.Zero, Size = new Float2(size + 4f, bw), BackgroundColor = ThumbSelectedBorder };
                new Panel { Parent = border, Location = new Float2(0, size - bw), Size = new Float2(size + 4f, bw), BackgroundColor = ThumbSelectedBorder };
                new Panel { Parent = border, Location = Float2.Zero, Size = new Float2(bw, size), BackgroundColor = ThumbSelectedBorder };
                new Panel { Parent = border, Location = new Float2(size + 4f - bw, 0), Size = new Float2(bw, size), BackgroundColor = ThumbSelectedBorder };
            }

            // 标签文字（万相集/分享）
            if (!string.IsNullOrEmpty(label))
            {
                var labelCtrl = new Label
                {
                    Parent = container,
                    Text = label,
                    Location = new Float2(0, size + 2f),
                    Size = new Float2(size + 4f, 18),
                    TextColor = ThumbLabelColor,
                    Font = UIHelper.SetFont(size: 11),
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center
                };
            }

            return container;
        }

        private ContainerControl CreateCircularIconButton(string text, float y, float size = 50, bool isSelected = false)
        {
            var container = new ContainerControl
            {
                Location = new Float2(2, y),
                Size = new Float2(size, size),
                BackgroundColor = Color.Transparent
            };

            // 使用标准 Panel 替代 RoundedPanel
            var panel = new Panel
            {
                Parent = container,
                Location = new Float2(0, 0),
                Size = new Float2(size, size),
                BackgroundColor = isSelected ? UIStyleTokens.Gold(0.25f) : UIStyleTokens.WithAlpha(UIStyleTokens.BgAbyss, 0.4f)
            };

            var label = new Label
            {
                Parent = container,
                Text = text,
                Location = new Float2(0, 0),
                Size = new Float2(size, size),
                TextColor = UIStyleTokens.TextPrimary,
                Font = UIHelper.SetFont(size: 12),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center
            };

            return container;
        }
        #endregion

        #region CreateUI - Bottom Actions
        private void CreateBottomActions()
        {
            _bottomLeftActions = new ContainerControl
            {
                Parent = this,
                AnchorPreset = AnchorPresets.BottomLeft,
                Offsets = new Margin(20, 120, 0, 25),
                Height = 60,
                BackgroundColor = Color.Transparent
            };

            // 返回按钮
            _backButton = new Label
            {
                Parent = _bottomLeftActions,
                Text = "鼠标 返回",
                Location = new Float2(0, 8),
                Size = new Float2(140, 44),
                BackgroundColor = UIStyleTokens.WithAlpha(UIStyleTokens.BgInk, 0.4f),
                TextColor = UIStyleTokens.TextSecondary,
                Font = UIHelper.SetFont(size: 18),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center
            };

            // 导入捏脸按钮
            _importButton = new Label
            {
                Parent = _bottomLeftActions,
                Text = "V 导入捏脸",
                Location = new Float2(160, 8),
                Size = new Float2(140, 44),
                BackgroundColor = UIStyleTokens.WithAlpha(UIStyleTokens.BgElevated, 0.4f),
                TextColor = UIStyleTokens.TextPrimary,
                Font = UIHelper.SetFont(size: 18),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center
            };
        }

        private void CreateNextStepButton()
        {
            _nextStepButton = new NextStepButton
            {
                Parent = this
            };
            _nextStepButton.OnClicked += () => OnNextStep?.Invoke();
        }
        #endregion

        #region Mouse Input
        public override bool OnMouseDown(Float2 location, MouseButton button)
        {
            if (button == MouseButton.Left)
            {
                if (_mainTab1 != null && IsPointInControl(_mainTab1, location))
                {
                    SwitchMainTab(0);
                    return true;
                }
                if (_mainTab2 != null && IsPointInControl(_mainTab2, location))
                {
                    SwitchMainTab(1);
                    return true;
                }

                if (_subTab1 != null && IsPointInControl(_subTab1, location))
                {
                    SwitchSubTab(0);
                    return true;
                }
                if (_subTab2 != null && IsPointInControl(_subTab2, location))
                {
                    SwitchSubTab(1);
                    return true;
                }

                if (_backButton != null && IsPointInControl(_backButton, location))
                {
                    OnGoBack?.Invoke();
                    return true;
                }
                if (_importButton != null && IsPointInControl(_importButton, location))
                {
                    ImportFaceData();
                    return true;
                }

                if (_wanxiangjiButton != null && IsPointInControl(_wanxiangjiButton, location)) { return true; }
                if (_shareButton != null && IsPointInControl(_shareButton, location)) { return true; }
                if (_clothesIcon != null && IsPointInControl(_clothesIcon, location)) { return true; }
                if (_hatIcon != null && IsPointInControl(_hatIcon, location)) { return true; }
                if (_bodyIcon != null && IsPointInControl(_bodyIcon, location)) { return true; }
            }

            return base.OnMouseDown(location, button);
        }

        private bool IsPointInControl(Control control, Float2 point)
        {
            if (control == null) return false;
            var localPoint = point - control.Location;
            return new Rectangle(Float2.Zero, control.Size).Contains(localPoint);
        }
        #endregion

        #region Keyboard Input
        // Keyboard input is handled in the Update method in the Constructor region
        #endregion

        #region Tab Switching
        private void SwitchMainTab(int tabIndex)
        {
            _selectedMainTab = tabIndex;
            UpdateMainTabVisual();
        }

        private void UpdateMainTabVisual()
        {
            Color goldUnderline = ChineseClassicalTheme.SecondaryColor;
            Color dimmedUnderline = ChineseClassicalTheme.SecondaryColorWithAlpha(0f);

            if (_mainTab1 != null)
            {
                _mainTab1.TextColor = _selectedMainTab == 0
                    ? ChineseClassicalTheme.SecondaryColor
                    : UIStyleTokens.WithAlpha(UIStyleTokens.TextPrimary, 0.4f);
            }
            if (_mainTab2 != null)
            {
                _mainTab2.TextColor = _selectedMainTab == 1
                    ? ChineseClassicalTheme.SecondaryColor
                    : UIStyleTokens.WithAlpha(UIStyleTokens.TextPrimary, 0.4f);
            }
            if (_mainTab1Underline != null)
                _mainTab1Underline.BackgroundColor = _selectedMainTab == 0 ? goldUnderline : dimmedUnderline;
            if (_mainTab2Underline != null)
                _mainTab2Underline.BackgroundColor = _selectedMainTab == 1 ? goldUnderline : dimmedUnderline;
        }

        private void SwitchSubTab(int tabIndex)
        {
            _selectedSubTab = tabIndex;
            UpdateSubTabVisual();
        }

        private void UpdateSubTabVisual()
        {
            Color goldUnderline = ChineseClassicalTheme.SecondaryColor;
            Color dimmedUnderline = ChineseClassicalTheme.SecondaryColorWithAlpha(0f);

            if (_subTab1 != null)
            {
                _subTab1.TextColor = _selectedSubTab == 0
                    ? ChineseClassicalTheme.SecondaryColor
                    : UIStyleTokens.WithAlpha(UIStyleTokens.TextPrimary, 0.4f);
            }
            if (_subTab2 != null)
            {
                _subTab2.TextColor = _selectedSubTab == 1
                    ? ChineseClassicalTheme.SecondaryColor
                    : UIStyleTokens.WithAlpha(UIStyleTokens.TextPrimary, 0.4f);
            }
            if (_subTab1Underline != null)
                _subTab1Underline.BackgroundColor = _selectedSubTab == 0 ? goldUnderline : dimmedUnderline;
            if (_subTab2Underline != null)
                _subTab2Underline.BackgroundColor = _selectedSubTab == 1 ? goldUnderline : dimmedUnderline;
        }
        #endregion

        #region Public Methods
        public void SetGender(string gender)
        {
            _gender = gender;
            RefreshPresets();
        }

        public void Show()
        {
            Visible = true;
        }

        public void Hide()
        {
            Visible = false;
        }

        /// <summary>
        /// 隐藏内部 NextStepButton（由控制器级按钮替代）
        /// </summary>
        public void HideExternalButton()
        {
            if (_nextStepButton != null)
                _nextStepButton.Parent = null;
        }

        public void RefreshPresets()
        {
            SelectedPreset = null;
            PopulatePresetCards();
        }
        #endregion

        #region Import
        private void ImportFaceData()
        {
            // 导入捏脸数据（后续实现）
        }
        #endregion
    }
}
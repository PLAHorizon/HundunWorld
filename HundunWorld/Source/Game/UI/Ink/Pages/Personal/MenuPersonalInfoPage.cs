using System;
using System.Linq;
using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.StyleSystem;

namespace HundunWorld.Game.UI.Ink.Pages.Personal
{
    public class MenuPersonalInfoPage : ContainerControl, IInkPage
    {
        private Float2 _screenSize;

        private InkPanel _leftNav;
        private InkPanel _topBar;
        private InkButton _backButton;
        private ContainerControl _playerAvatar;
        private Label _playerNameLabel;
        private Label _playerLevelLabel;
        private Label _sectLabel;
        private Label _currencyCoinLabel;
        private Label _currencyIngotLabel;

        private Label _pageTitle;

        private InkPanel _leftInfoPanel;
        private ContainerControl _characterAvatar;
        private InkVerticalTitle _verticalName;
        private Label[] _infoLabels;
        private InkPanel _titlePanel;
        private Label[] _titleLabels;

        private InkPanel _rightSection;
        private InkButton[] _tabButtons;
        private InkPanel _statsGrid;
        private Label[] _statLabels;

        private InkPanel _achievementPanel;
        private Label _achievementTitle;
        private Label _achievementValue;
        private InkBar _achievementBar;

        private string[] _basicInfoLabels = { "等级", "门派", "性别", "创建日期" };
        private string[] _basicInfoValues = { "42", "逍遥派", "男", "2025-01-15" };
        private string[] _titles = { "剑客初成", "逍遥弟子", "江湖新秀" };

        private string[] _statCategories = { "游戏时长", "击败敌人", "完成任务", "探索区域", "收集物品", "PVP胜场" };
        private string[] _statValues = { "128", "3,420", "186", "23", "567", "89" };
        private string[] _statUnits = { "小时", "", "", "/ 50", "", "" };

        public MenuPersonalInfoPage()
        {
            _screenSize = FlaxEngine.Screen.Size;
            if (_screenSize.X <= 0f || _screenSize.Y <= 0f)
            {
                _screenSize = new Float2(1920f, 1080f);
            }

            AnchorPreset = AnchorPresets.StretchAll;
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
            AutoFocus = false;
            Location = Float2.Zero;
            Size = _screenSize;

            try
            {
                BuildLeftNav();
                BuildTopBar();
                BuildLeftInfoPanel();
                BuildRightSection();

                ApplyLayout();
                RefreshAllData();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[MenuPersonalInfoPage] 初始化失败: {ex.Message}");
            }
        }

        private void BuildLeftNav()
        {
            _leftNav = new InkPanel
            {
                Width = 240f,
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = InkWashTheme.BaseDefault,
                Parent = this
            };

            var logoText = new Label
            {
                Text = "混沌世界",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 22f),
                TextColor = InkWashTheme.GoldBright,
                HorizontalAlignment = TextAlignment.Center,
                Parent = _leftNav
            };

            var divider = new InkDivider
            {
                Width = 240f,
                Height = 1f,
                Parent = _leftNav
            };

            string[] navItems = { "角色", "装备", "外观", "备战", "门派", "个人信息", "时辰", "任务", "博物志", "武林录", "营生", "组队", "邮箱", "商店", "设置" };
            int[] disabledIndices = { 7, 8, 9, 10, 11, 12, 13, 14 };

            float y = 80f;
            for (int i = 0; i < navItems.Length; i++)
            {
                var navItem = new InkButton
                {
                    Width = 240f,
                    Height = 40f,
                    Text = navItems[i],
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                    Parent = _leftNav
                };

                if (i == 5)
                {
                    navItem.Variant = InkButtonVariant.Ghost;
                    navItem.BackgroundColor = InkWashTheme.GoldPrimary * 0.1f;
                    navItem.TextColor = InkWashTheme.GoldBright;
                }
                else if (disabledIndices.Contains(i))
                {
                    navItem.Variant = InkButtonVariant.Ghost;
                    navItem.TextColor = InkWashTheme.TextDisabled;
                    navItem.Enabled = false;
                }
                else
                {
                    navItem.Variant = InkButtonVariant.Ghost;
                    navItem.TextColor = InkWashTheme.PaperBright;
                }

                y += 40f;

                if (i == 6 || i == 13)
                {
                    var div = new InkDivider
                    {
                        Width = 240f,
                        Height = 1f,
                        Parent = _leftNav
                    };
                    y += 8f;
                }
            }
        }

        private void BuildTopBar()
        {
            _topBar = new InkPanel
            {
                Height = 60f,
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = InkWashTheme.BaseTertiary,
                Parent = this
            };

            _backButton = new InkButton
            {
                Width = 36f,
                Height = 36f,
                Text = "<",
                Variant = InkButtonVariant.Ghost,
                Parent = _topBar
            };

            _playerAvatar = new ContainerControl
            {
                Size = new Float2(36f, 36f),
                BackgroundColor = Color.Lerp(InkWashTheme.BaseElevated, InkWashTheme.BaseTertiary, 0.5f),
                Parent = _topBar
            };
            var avatarLabel = new Label
            {
                Text = "无",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 16f),
                TextColor = InkWashTheme.GoldBright,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.StretchAll,
                Parent = _playerAvatar
            };

            _playerNameLabel = new Label
            {
                Text = "无名侠",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 14f),
                TextColor = InkWashTheme.PaperBright,
                Parent = _topBar
            };

            _playerLevelLabel = new Label
            {
                Text = "Lv.42",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.VermilionBright,
                Parent = _topBar
            };

            _sectLabel = new Label
            {
                Text = "逍遥派",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.PaperAged,
                Parent = _topBar
            };

            _currencyCoinLabel = new Label
            {
                Text = "12,450",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 13f),
                TextColor = InkWashTheme.GoldBright,
                Parent = _topBar
            };

            _currencyIngotLabel = new Label
            {
                Text = "328",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 13f),
                TextColor = InkWashTheme.JadeBright,
                Parent = _topBar
            };
        }

        private void BuildLeftInfoPanel()
        {
            _leftInfoPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Parent = this
            };

            var cornerDecoTL = new InkCornerDeco
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Parent = _leftInfoPanel
            };

            var cornerDecoTR = new InkCornerDeco
            {
                AnchorPreset = AnchorPresets.TopRight,
                Parent = _leftInfoPanel
            };

            var cornerDecoBL = new InkCornerDeco
            {
                AnchorPreset = AnchorPresets.BottomLeft,
                Parent = _leftInfoPanel
            };

            var cornerDecoBR = new InkCornerDeco
            {
                AnchorPreset = AnchorPresets.BottomRight,
                Parent = _leftInfoPanel
            };

            _characterAvatar = new ContainerControl
            {
                Size = new Float2(80f, 80f),
                BackgroundColor = Color.Lerp(InkWashTheme.BaseElevated, InkWashTheme.BaseTertiary, 0.5f),
                Parent = _leftInfoPanel
            };
            _characterAvatar.AnchorPreset = AnchorPresets.TopLeft;
            var charAvatarLabel = new Label
            {
                Text = "无",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 32f),
                TextColor = InkWashTheme.GoldBright,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.StretchAll,
                Parent = _characterAvatar
            };

            _verticalName = new InkVerticalTitle
            {
                Text = "无名侠",
                FontSize = 28f,
                Width = 30f,
                Height = 120f,
                Parent = _leftInfoPanel
            };

            var divider1 = new InkDivider
            {
                Width = 200f,
                Height = 1f,
                Parent = _leftInfoPanel
            };

            var basicTitle = new Label
            {
                Text = "基本信息",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 13f),
                TextColor = InkWashTheme.GoldBright,
                Parent = _leftInfoPanel
            };

            _infoLabels = new Label[_basicInfoLabels.Length * 2];
            for (int i = 0; i < _basicInfoLabels.Length; i++)
            {
                _infoLabels[i * 2] = new Label
                {
                    Text = _basicInfoLabels[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                    TextColor = InkWashTheme.PaperAged,
                    Parent = _leftInfoPanel
                };

                _infoLabels[i * 2 + 1] = new Label
                {
                    Text = _basicInfoValues[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                    TextColor = InkWashTheme.PaperBright,
                    Parent = _leftInfoPanel
                };
            }

            var divider2 = new InkDivider
            {
                Width = 200f,
                Height = 1f,
                Parent = _leftInfoPanel
            };

            var titleTitle = new Label
            {
                Text = "已获称号",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 13f),
                TextColor = InkWashTheme.GoldBright,
                Parent = _leftInfoPanel
            };

            _titlePanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = Color.Transparent,
                Parent = _leftInfoPanel
            };

            _titleLabels = new Label[_titles.Length];
            for (int i = 0; i < _titles.Length; i++)
            {
                _titleLabels[i] = new Label
                {
                    Text = _titles[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                    TextColor = i == 1 ? InkWashTheme.GoldBright : InkWashTheme.TextSecondary,
                    Parent = _titlePanel
                };
            }
        }

        private void BuildRightSection()
        {
            _rightSection = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopRight,
                BackgroundColor = Color.Transparent,
                Parent = this
            };

            _pageTitle = new Label
            {
                Text = "个人信息",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 22f),
                TextColor = InkWashTheme.PaperBright,
                Parent = _rightSection
            };

            _tabButtons = new InkButton[4];
            string[] tabNames = { "总览", "战斗", "探索", "社交" };
            for (int i = 0; i < tabNames.Length; i++)
            {
                _tabButtons[i] = new InkButton
                {
                    Width = 80f,
                    Height = 32f,
                    Text = tabNames[i],
                    Variant = InkButtonVariant.Ghost,
                    TextColor = i == 0 ? InkWashTheme.GoldBright : InkWashTheme.TextDisabled,
                    Parent = _rightSection
                };
                if (i == 0)
                {
                    _tabButtons[i].BackgroundColor = InkWashTheme.GoldPrimary * 0.1f;
                }
            }

            _statsGrid = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = Color.Transparent,
                Parent = _rightSection
            };

            _statLabels = new Label[_statCategories.Length * 3];
            for (int i = 0; i < _statCategories.Length; i++)
            {
                var statCard = new InkPanel
                {
                    BackgroundColor = InkWashTheme.BaseTertiary,
                    Parent = _statsGrid
                };

                var categoryLabel = new Label
                {
                    Text = _statCategories[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                    TextColor = InkWashTheme.TextDisabled,
                    Parent = statCard
                };

                var valueLabel = new Label
                {
                    Text = _statValues[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 24f),
                    TextColor = InkWashTheme.PaperBright,
                    Parent = statCard
                };

                var unitLabel = new Label
                {
                    Text = _statUnits[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                    TextColor = InkWashTheme.PaperAged,
                    Parent = statCard
                };

                _statLabels[i * 3] = categoryLabel;
                _statLabels[i * 3 + 1] = valueLabel;
                _statLabels[i * 3 + 2] = unitLabel;
            }

            _achievementPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = Color.Transparent,
                Parent = _rightSection
            };

            _achievementTitle = new Label
            {
                Text = "成就进度",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 13f),
                TextColor = InkWashTheme.GoldBright,
                Parent = _achievementPanel
            };

            _achievementValue = new Label
            {
                Text = "145 / 320",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 13f),
                TextColor = InkWashTheme.PaperAged,
                Parent = _achievementPanel
            };

            _achievementBar = new InkBar
            {
                Height = 8f,
                Parent = _achievementPanel
            };
            _achievementBar.Value = 145f / 320f;
        }

        private void ApplyLayout()
        {
            float sw = _screenSize.X;
            float sh = _screenSize.Y;

            if (_leftNav != null)
            {
                _leftNav.Size = new Float2(240f, sh);
                _leftNav.Location = new Float2(0f, 0f);
            }

            if (_topBar != null)
            {
                _topBar.Size = new Float2(sw - 240f, 60f);
                _topBar.Location = new Float2(240f, 0f);
            }

            if (_backButton != null)
                _backButton.Location = new Float2(24f, 12f);
            if (_playerAvatar != null)
                _playerAvatar.Location = new Float2(72f, 12f);
            if (_playerNameLabel != null)
                _playerNameLabel.Location = new Float2(116f, 14f);
            if (_playerLevelLabel != null)
                _playerLevelLabel.Location = new Float2(220f, 16f);
            if (_sectLabel != null)
                _sectLabel.Location = new Float2(280f, 16f);
            if (_currencyCoinLabel != null)
                _currencyCoinLabel.Location = new Float2(sw - 160f, 16f);
            if (_currencyIngotLabel != null)
                _currencyIngotLabel.Location = new Float2(sw - 80f, 16f);

            float leftWidth = (sw - 240f) * 0.35f;
            float rightWidth = (sw - 240f) * 0.65f;
            float padding = 24f;

            if (_leftInfoPanel != null)
            {
                _leftInfoPanel.Size = new Float2(leftWidth - padding, 500f);
                _leftInfoPanel.Location = new Float2(240f + padding, 80f);
            }

            if (_characterAvatar != null)
                _characterAvatar.Location = new Float2(32f, 32f);
            if (_verticalName != null)
                _verticalName.Location = new Float2(130f, 32f);

            float infoY = 180f;
            for (int i = 0; i < _basicInfoLabels.Length; i++)
            {
                if (_infoLabels[i * 2] != null)
                    _infoLabels[i * 2].Location = new Float2(32f, infoY);
                if (_infoLabels[i * 2 + 1] != null)
                    _infoLabels[i * 2 + 1].Location = new Float2(leftWidth - padding - 80f, infoY);
                infoY += 28f;
            }

            float titleX = 32f;
            for (int i = 0; i < _titles.Length; i++)
            {
                if (_titleLabels[i] != null)
                {
                    _titleLabels[i].Location = new Float2(titleX, 340f);
                    titleX += 90f;
                }
            }

            if (_rightSection != null)
            {
                _rightSection.Size = new Float2(rightWidth, sh - 80f);
                _rightSection.Location = new Float2(240f + leftWidth, 80f);
            }

            if (_pageTitle != null)
                _pageTitle.Location = new Float2(0f, 0f);

            float tabX = 0f;
            for (int i = 0; i < _tabButtons.Length; i++)
            {
                if (_tabButtons[i] != null)
                {
                    _tabButtons[i].Location = new Float2(tabX, 40f);
                    tabX += 100f;
                }
            }

            float statGridX = 0f;
            float statGridY = 80f;
            for (int i = 0; i < _statCategories.Length; i++)
            {
                float cardWidth = (rightWidth - padding * 3f) / 3f;
                float cardHeight = 100f;

                if (_statsGrid != null)
                {
                    _statsGrid.Size = new Float2(rightWidth, 260f);
                    _statsGrid.Location = new Float2(0f, statGridY);
                }

                var categoryLabel = _statLabels[i * 3];
                var valueLabel = _statLabels[i * 3 + 1];
                var unitLabel = _statLabels[i * 3 + 2];

                if (categoryLabel != null)
                    categoryLabel.Location = new Float2(statGridX + 16f, 16f);
                if (valueLabel != null)
                    valueLabel.Location = new Float2(statGridX + 16f, 40f);
                if (unitLabel != null)
                    unitLabel.Location = new Float2(statGridX + 80f, 50f);

                statGridX += cardWidth;
                if ((i + 1) % 3 == 0)
                {
                    statGridX = 0f;
                    statGridY += cardHeight;
                }
            }

            if (_achievementPanel != null)
            {
                _achievementPanel.Size = new Float2(rightWidth, 60f);
                _achievementPanel.Location = new Float2(0f, 360f);
            }

            if (_achievementTitle != null)
                _achievementTitle.Location = new Float2(0f, 0f);
            if (_achievementValue != null)
                _achievementValue.Location = new Float2(rightWidth - 80f, 0f);
            if (_achievementBar != null)
            {
                _achievementBar.Size = new Float2(rightWidth, 8f);
                _achievementBar.Location = new Float2(0f, 24f);
            }
        }

        public void RefreshLayout()
        {
            _screenSize = new Float2(Width, Height);
            ApplyLayout();
        }

        public void RefreshAllData()
        {
        }

        public void OnPageEnter()
        {
            RefreshAllData();
        }

        public void OnPageLeave()
        {
        }

        public void OnPageUpdate()
        {
        }

        public void OnResolutionChanged()
        {
            _screenSize = FlaxEngine.Screen.Size;
            ApplyLayout();
        }
    }
}

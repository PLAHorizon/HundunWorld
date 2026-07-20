using System;
using System.Linq;
using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.StyleSystem;

namespace HundunWorld.Game.UI.Ink.Pages.Timing
{
    public class MenuTimePage : ContainerControl, IInkPage
    {
        private Float2 _screenSize;

        private InkPanel _leftNav;
        private InkPanel _topBar;
        private InkButton _backButton;
        private ContainerControl _playerAvatar;
        private Label _playerNameLabel;
        private Label _playerLevelLabel;
        private Label _currencyCoinLabel;
        private Label _currencyIngotLabel;
        private InkButton _questButton;

        private InkPanel _dialSection;
        private InkPanel _panelSection;
        private Label _dialTitle;
        private Label _dialSubtitle;
        private InkPanel _dialPanel;
        private Label _centerShichenLabel;
        private Label _centerDateLabel;
        private Label _legendLabel1;
        private Label _legendLabel2;
        private Label _legendLabel3;

        private InkPaperPanel _currentTimePanel;
        private Label _currentTimeTitle;
        private Label _dateLabel;
        private Label _shichenLabel;
        private Label _weatherLabel;

        private InkPanel _fastForwardPanel;
        private Label _fastForwardTitle;
        private InkButton _btnWait1Shichen;
        private InkButton _btnWaitDusk;
        private InkButton _btnWaitDawn;

        private InkPanel _effectPanel;
        private Label _effectTitle;
        private Label[] _effectLabels;

        private InkPanel _hintBar;
        private Label _hintText;

        private InkPanel _bottomBar;
        private Label _versionLabel;
        private Label _pingLabel;

        private string[] _shichenNames = { "子", "丑", "寅", "卯", "辰", "巳", "午", "未", "申", "酉", "戌", "亥" };
        private string[] _shichenActivities = { "夜市", "巡夜", "晨钟", "早市", "早课", "武馆", "集市", "茶馆", "比武", "晚歇", "花灯", "夜行" };
        private string _currentShichen = "午时";
        private string _currentDate = "三月初七";
        private string _currentWeather = "晴";

        public MenuTimePage()
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
                BuildAtmosphere();
                BuildLeftNav();
                BuildTopBar();
                BuildDialSection();
                BuildPanelSection();
                BuildBottomBar();

                ApplyLayout();
                RefreshAllData();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[MenuTimePage] 初始化失败: {ex.Message}");
            }
        }

        private void BuildAtmosphere()
        {
            var vignette = new InkVignette();
            vignette.AnchorPreset = AnchorPresets.StretchAll;
            AddChild(vignette);

            var bgLayer = new InkBackgroundLayer();
            bgLayer.AnchorPreset = AnchorPresets.StretchAll;
            AddChild(bgLayer);
        }

        private void BuildLeftNav()
        {
            _leftNav = new InkPanel
            {
                Width = 240f,
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = InkWashTheme.BaseTertiary,
                Parent = this
            };

            var navTitle = new InkVerticalTitle
            {
                Text = "时辰",
                FontSize = 22f,
                Width = 30f,
                Height = 100f,
                Parent = _leftNav
            };

            var divider = new InkDivider
            {
                Width = 240f,
                Height = 1f,
                Parent = _leftNav
            };

            string[] navItems = { "角色", "装备", "外观", "备战", "门派", "个人信息", "时间", "任务", "博物志", "武林录", "营生", "组队", "邮箱", "商店", "设置" };
            int[] disabledIndices = { 7, 8, 9, 10, 11, 12, 13, 14 };

            float y = 140f;
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

                if (i == 6)
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
                Size = new Float2(40f, 40f),
                BackgroundColor = Color.Lerp(InkWashTheme.GoldBright, InkWashTheme.GoldDeep, 0.5f),
                Parent = _topBar
            };
            var avatarLabel = new Label
            {
                Text = "无",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 18f),
                TextColor = InkWashTheme.TextOnBrand,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.StretchAll,
                Parent = _playerAvatar
            };

            _playerNameLabel = new Label
            {
                Text = "无名侠",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 18f),
                TextColor = InkWashTheme.PaperBright,
                Parent = _topBar
            };

            _playerLevelLabel = new Label
            {
                Text = "Lv.42",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                TextColor = InkWashTheme.GoldBright,
                Parent = _topBar
            };

            var sectLabel = new Label
            {
                Text = "逍遥派",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                TextColor = InkWashTheme.GoldBright,
                Parent = _topBar
            };

            _questButton = new InkButton
            {
                Width = 60f,
                Height = 28f,
                Text = "任务",
                Variant = InkButtonVariant.Ghost,
                Parent = _topBar
            };

            _currencyCoinLabel = new Label
            {
                Text = "铜钱 12,450",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                TextColor = InkWashTheme.PaperAged,
                Parent = _topBar
            };

            _currencyIngotLabel = new Label
            {
                Text = "银两 328",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                TextColor = InkWashTheme.GoldBright,
                Parent = _topBar
            };
        }

        private void BuildDialSection()
        {
            _dialSection = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = InkWashTheme.BaseDefault,
                Parent = this
            };

            _dialTitle = new Label
            {
                Text = "十二时辰",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 28f),
                TextColor = InkWashTheme.PaperBright,
                Parent = _dialSection
            };

            _dialSubtitle = new Label
            {
                Text = "子丑寅卯辰巳 · 午未申酉戌亥",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                TextColor = InkWashTheme.PaperAged,
                Parent = _dialSection
            };

            _dialPanel = new InkPanel
            {
                Size = new Float2(500f, 500f),
                BackgroundColor = Color.Transparent,
                Parent = _dialSection
            };

            _centerShichenLabel = new Label
            {
                Text = "午时",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 30f),
                TextColor = InkWashTheme.GoldBright,
                Width = 100f,
                Height = 36f,
                HorizontalAlignment = TextAlignment.Center,
                Parent = _dialPanel
            };

            _centerDateLabel = new Label
            {
                Text = "三月初七",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 14f),
                TextColor = InkWashTheme.GoldDeep,
                Width = 100f,
                Height = 20f,
                HorizontalAlignment = TextAlignment.Center,
                Parent = _dialPanel
            };

            float radius = 220f;
            float centerX = 250f;
            float centerY = 250f;
            for (int i = 0; i < 12; i++)
            {
                float angle = -90f + i * 30f;
                float radian = angle * ((float)Math.PI / 180f);
                float x = centerX + (float)Math.Cos(radian) * radius;
                float y = centerY + (float)Math.Sin(radian) * radius;

                var shichenLabel = new Label
                {
                    Text = _shichenNames[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, i == 6 ? 24f : 18f),
                    TextColor = i == 6 ? InkWashTheme.GoldPrimary : InkWashTheme.PaperAged,
                    Width = 30f,
                    Height = 30f,
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                    Parent = _dialPanel
                };
                shichenLabel.Location = new Float2(x - 15f, y - 15f);

                float activityRadius = radius + 35f;
                float activityX = centerX + (float)Math.Cos(radian) * activityRadius;
                float activityY = centerY + (float)Math.Sin(radian) * activityRadius;

                var activityLabel = new Label
                {
                    Text = _shichenActivities[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, i == 6 ? 11f : 10f),
                    TextColor = i == 6 ? InkWashTheme.GoldBright : InkWashTheme.PaperFaded,
                    Width = 40f,
                    Height = 16f,
                    HorizontalAlignment = TextAlignment.Center,
                    Parent = _dialPanel
                };
                activityLabel.Location = new Float2(activityX - 20f, activityY - 8f);
            }

            _legendLabel1 = new Label
            {
                Text = "● 当前时辰",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.GoldPrimary,
                Parent = _dialSection
            };

            _legendLabel2 = new Label
            {
                Text = "● 其他时辰",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextTertiary,
                Parent = _dialSection
            };

            _legendLabel3 = new Label
            {
                Text = "● 外圈活动",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.GoldDeep,
                Parent = _dialSection
            };
        }

        private void BuildPanelSection()
        {
            _panelSection = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopRight,
                BackgroundColor = InkWashTheme.BaseSecondary,
                Parent = this
            };

            _currentTimePanel = new InkPaperPanel
            {
                Parent = _panelSection
            };

            _currentTimeTitle = new Label
            {
                Text = "当前时辰",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 16f),
                TextColor = InkWashTheme.TextOnPaper,
                Parent = _currentTimePanel
            };

            _dateLabel = new Label
            {
                Text = "三月初七",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 18f),
                TextColor = InkWashTheme.TextOnPaper,
                HorizontalAlignment = TextAlignment.Center,
                Parent = _currentTimePanel
            };

            var dateLabelTag = new Label
            {
                Text = "日期",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.PaperDark,
                HorizontalAlignment = TextAlignment.Center,
                Parent = _currentTimePanel
            };

            _shichenLabel = new Label
            {
                Text = "午时",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 18f),
                TextColor = InkWashTheme.TextOnPaper,
                HorizontalAlignment = TextAlignment.Center,
                Parent = _currentTimePanel
            };

            var shichenLabelTag = new Label
            {
                Text = "时辰",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.PaperDark,
                HorizontalAlignment = TextAlignment.Center,
                Parent = _currentTimePanel
            };

            _weatherLabel = new Label
            {
                Text = "晴",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 18f),
                TextColor = InkWashTheme.TextOnPaper,
                HorizontalAlignment = TextAlignment.Center,
                Parent = _currentTimePanel
            };

            var weatherLabelTag = new Label
            {
                Text = "天气",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.PaperDark,
                HorizontalAlignment = TextAlignment.Center,
                Parent = _currentTimePanel
            };

            _fastForwardPanel = new InkPanel
            {
                Parent = _panelSection
            };

            var cornerDeco1 = new InkCornerDeco
            {
                AnchorPreset = AnchorPresets.StretchAll,
                Parent = _fastForwardPanel
            };

            _fastForwardTitle = new Label
            {
                Text = "时间快进",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 16f),
                TextColor = InkWashTheme.GoldBright,
                Parent = _fastForwardPanel
            };

            _btnWait1Shichen = new InkButton
            {
                Text = "等待1时辰",
                Variant = InkButtonVariant.Primary,
                ButtonSize = InkButtonSize.Lg,
                Parent = _fastForwardPanel
            };

            _btnWaitDusk = new InkButton
            {
                Text = "等待至黄昏",
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Lg,
                Parent = _fastForwardPanel
            };

            _btnWaitDawn = new InkButton
            {
                Text = "等待至黎明",
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Lg,
                Parent = _fastForwardPanel
            };

            _effectPanel = new InkPanel
            {
                Parent = _panelSection
            };

            var cornerDeco2 = new InkCornerDeco
            {
                AnchorPreset = AnchorPresets.StretchAll,
                Parent = _effectPanel
            };

            _effectTitle = new Label
            {
                Text = "时辰影响",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 16f),
                TextColor = InkWashTheme.GoldBright,
                Parent = _effectPanel
            };

            string[] effects = {
                "不同时辰影响NPC出现与消失，部分商贩仅在昼间营业，夜行侠客多出没于亥子之时",
                "夜间某些区域出现特殊事件，荒郊古寺、深巷暗街或有奇遇",
                "特定时辰可修炼特定功法，子时练阴属功法事半功倍，午时修阳属心法收效甚佳"
            };

            _effectLabels = new Label[effects.Length];
            for (int i = 0; i < effects.Length; i++)
            {
                _effectLabels[i] = new Label
                {
                    Text = effects[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                    TextColor = InkWashTheme.PaperAged,
                    Parent = _effectPanel
                };
            }

            _hintBar = new InkPanel
            {
                Height = 44f,
                BackgroundColor = InkWashTheme.GoldPrimary * 0.08f,
                Parent = _panelSection
            };

            _hintText = new Label
            {
                Text = "等待时间可能触发随机事件",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                TextColor = InkWashTheme.GoldBright,
                Parent = _hintBar
            };
        }

        private void BuildBottomBar()
        {
            _bottomBar = new InkPanel
            {
                Height = 32f,
                AnchorPreset = AnchorPresets.BottomLeft,
                BackgroundColor = InkWashTheme.BaseDefault,
                Parent = this
            };

            _versionLabel = new Label
            {
                Text = "混沌世界 v1.0.0",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                TextColor = InkWashTheme.TextTertiary,
                Parent = _bottomBar
            };

            _pingLabel = new Label
            {
                Text = "混沌盛世 · 32ms",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                TextColor = InkWashTheme.TextTertiary,
                Parent = _bottomBar
            };
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
                _playerAvatar.Location = new Float2(72f, 10f);
            if (_playerNameLabel != null)
                _playerNameLabel.Location = new Float2(124f, 10f);
            if (_playerLevelLabel != null)
                _playerLevelLabel.Location = new Float2(280f, 12f);

            float dialWidth = (sw - 240f) * 0.55f;
            if (_dialSection != null)
            {
                _dialSection.Size = new Float2(dialWidth, sh - 92f);
                _dialSection.Location = new Float2(240f, 60f);
            }

            if (_dialTitle != null)
                _dialTitle.Location = new Float2(80f, 32f);
            if (_dialSubtitle != null)
                _dialSubtitle.Location = new Float2(80f, 76f);
            if (_dialPanel != null)
                _dialPanel.Location = new Float2((dialWidth - 500f) * 0.5f, 120f);

            if (_centerShichenLabel != null)
                _centerShichenLabel.Location = new Float2(200f, 190f);
            if (_centerDateLabel != null)
                _centerDateLabel.Location = new Float2(200f, 230f);

            if (_legendLabel1 != null)
                _legendLabel1.Location = new Float2((dialWidth - 300f) * 0.5f, sh - 92f - 40f);
            if (_legendLabel2 != null)
                _legendLabel2.Location = new Float2((dialWidth - 300f) * 0.5f + 100f, sh - 92f - 40f);
            if (_legendLabel3 != null)
                _legendLabel3.Location = new Float2((dialWidth - 300f) * 0.5f + 200f, sh - 92f - 40f);

            float panelWidth = (sw - 240f) * 0.45f;
            if (_panelSection != null)
            {
                _panelSection.Size = new Float2(panelWidth, sh - 92f);
                _panelSection.Location = new Float2(240f + dialWidth, 60f);
            }

            float padding = 24f;
            if (_currentTimePanel != null)
            {
                _currentTimePanel.Size = new Float2(panelWidth - padding * 2f, 140f);
                _currentTimePanel.Location = new Float2(padding, padding);
            }

            if (_currentTimeTitle != null)
                _currentTimeTitle.Location = new Float2(padding, 16f);

            float gridWidth = (panelWidth - padding * 4f - 16f) / 3f;
            if (_dateLabel != null)
                _dateLabel.Location = new Float2(padding, 56f);
            if (_shichenLabel != null)
                _shichenLabel.Location = new Float2(padding + gridWidth + 8f, 56f);
            if (_weatherLabel != null)
                _weatherLabel.Location = new Float2(padding + (gridWidth + 8f) * 2f, 56f);

            if (_fastForwardPanel != null)
            {
                _fastForwardPanel.Size = new Float2(panelWidth - padding * 2f, 160f);
                _fastForwardPanel.Location = new Float2(padding, padding + 164f);
            }

            if (_fastForwardTitle != null)
                _fastForwardTitle.Location = new Float2(padding, 16f);
            if (_btnWait1Shichen != null)
            {
                _btnWait1Shichen.Size = new Float2(panelWidth - padding * 4f, 44f);
                _btnWait1Shichen.Location = new Float2(padding, 52f);
            }
            if (_btnWaitDusk != null)
            {
                _btnWaitDusk.Size = new Float2(panelWidth - padding * 4f, 44f);
                _btnWaitDusk.Location = new Float2(padding, 100f);
            }
            if (_btnWaitDawn != null)
            {
                _btnWaitDawn.Size = new Float2(panelWidth - padding * 4f, 44f);
                _btnWaitDawn.Location = new Float2(padding, 100f);
            }

            if (_effectPanel != null)
            {
                _effectPanel.Size = new Float2(panelWidth - padding * 2f, 200f);
                _effectPanel.Location = new Float2(padding, padding + 356f);
            }

            if (_effectTitle != null)
                _effectTitle.Location = new Float2(padding, 16f);

            float effectY = 56f;
            for (int i = 0; i < _effectLabels.Length; i++)
            {
                if (_effectLabels[i] != null)
                {
                    _effectLabels[i].Size = new Float2(panelWidth - padding * 4f - 32f, 40f);
                    _effectLabels[i].Location = new Float2(padding * 2f, effectY);
                    effectY += 56f;
                }
            }

            if (_hintBar != null)
            {
                _hintBar.Size = new Float2(panelWidth - padding * 2f, 44f);
                _hintBar.Location = new Float2(padding, padding + 572f);
            }

            if (_hintText != null)
                _hintText.Location = new Float2(padding + 12f, 10f);

            if (_bottomBar != null)
            {
                _bottomBar.Size = new Float2(sw, 32f);
                _bottomBar.Location = new Float2(0f, sh - 32f);
            }

            if (_versionLabel != null)
                _versionLabel.Location = new Float2(24f, 6f);
            if (_pingLabel != null)
                _pingLabel.Location = new Float2(sw - 224f, 6f);
        }

        public void RefreshLayout()
        {
            _screenSize = new Float2(Width, Height);
            ApplyLayout();
        }

        public void RefreshAllData()
        {
            if (_centerShichenLabel != null)
                _centerShichenLabel.Text = _currentShichen;
            if (_centerDateLabel != null)
                _centerDateLabel.Text = _currentDate;
            if (_dateLabel != null)
                _dateLabel.Text = _currentDate;
            if (_shichenLabel != null)
                _shichenLabel.Text = _currentShichen;
            if (_weatherLabel != null)
                _weatherLabel.Text = _currentWeather;
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

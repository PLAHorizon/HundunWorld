using System;
using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.StyleSystem;

namespace HundunWorld.Game.UI.Ink.Pages.Settings
{
    public class SettingsAudioPage : ContainerControl, IInkPage
    {
        private Float2 _screenSize;

        private InkButton _backButton;
        private InkPanel _leftNav;
        private InkPanel _topBar;
        private InkPanel _contentArea;

        private InkPanel[] _categoryTabs;
        private InkPanel[] _settingGroups;

        private InkPanel _actionBar;

        private string[] _categoryNames = { "画面", "音效", "操作", "账号", "其他" };
        private int[] _categoryCounts = { 10, 7, 12, 6, 5 };

        private string[] _volumeLabels = { "主音量", "背景音乐", "音效", "语音", "环境音" };
        private string[] _volumeDescs = {
            "调整游戏整体音量，影响所有音频通道",
            "调整场景与战斗背景音乐音量",
            "调整技能、招式与环境交互音效音量",
            "调整角色对话与剧情语音音量",
            "调整风声、水声、虫鸣等自然环境音量"
        };
        private float[] _volumeValues = { 0.8f, 0.6f, 0.75f, 0.9f, 0.5f };

        private string[] _toggleLabels = { "UI音效", "语音聊天" };
        private string[] _toggleDescs = {
            "界面按钮、标签切换等操作音效反馈",
            "开启队伍语音聊天功能，需配合麦克风使用"
        };
        private bool[] _toggleValues = { true, false };

        public SettingsAudioPage()
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
                BuildBackButton();
                BuildLeftNav();
                BuildTopBar();
                BuildContentArea();
                BuildCategoryTabs();
                BuildSettingsList();
                BuildActionBar();

                ApplyLayout();
                RefreshAllData();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[SettingsAudioPage] 初始化失败: {ex.Message}");
            }
        }

        private void BuildBackButton()
        {
            _backButton = new InkButton
            {
                Width = 40f,
                Height = 40f,
                Text = "<",
                Variant = InkButtonVariant.Ghost,
                BackgroundColor = InkWashTheme.BaseDefault,
                Parent = this
            };
        }

        private void BuildLeftNav()
        {
            _leftNav = new InkPanel
            {
                Width = 240f,
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = InkWashTheme.BaseSecondary,
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
                Width = 208f,
                Height = 1f,
                Parent = _leftNav
            };

            string[] navItems = { "任务", "抽卡", "通行证", "角色", "博物志", "奇珍阁", "设置" };

            for (int i = 0; i < navItems.Length; i++)
            {
                var navItem = new InkButton
                {
                    Width = 240f,
                    Height = 52f,
                    Text = navItems[i],
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                    Parent = _leftNav
                };

                if (i == 6)
                {
                    navItem.Variant = InkButtonVariant.Ghost;
                    navItem.BackgroundColor = InkWashTheme.GoldPrimary * 0.08f;
                    navItem.TextColor = InkWashTheme.GoldBright;
                }
                else
                {
                    navItem.Variant = InkButtonVariant.Ghost;
                    navItem.TextColor = InkWashTheme.PaperAged;
                }
            }

            var footerDivider = new InkDivider
            {
                Width = 208f,
                Height = 1f,
                Parent = _leftNav
            };

            var versionLabel = new Label
            {
                Text = "v1.2.0 · Build 20260715",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextDisabled,
                HorizontalAlignment = TextAlignment.Center,
                Parent = _leftNav
            };
        }

        private void BuildTopBar()
        {
            _topBar = new InkPanel
            {
                Height = 60f,
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = InkWashTheme.BaseSecondary,
                Parent = this
            };

            var backHudBtn = new InkButton
            {
                Width = 80f,
                Height = 32f,
                Text = "返回战斗",
                Variant = InkButtonVariant.Ghost,
                TextColor = InkWashTheme.TextSecondary,
                Parent = _topBar
            };

            var avatarCircle = new ContainerControl
            {
                Size = new Float2(36f, 36f),
                BackgroundColor = InkWashTheme.BaseElevated,
                Parent = _topBar
            };

            var avatarLabel = new Label
            {
                Text = "客",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 15f),
                TextColor = InkWashTheme.GoldBright,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.StretchAll,
                Parent = avatarCircle
            };

            var charName = new Label
            {
                Text = "江湖过客",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 15f),
                TextColor = InkWashTheme.PaperBright,
                Parent = _topBar
            };

            var charLevel = new Label
            {
                Text = "Lv. 42",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 12f),
                TextColor = InkWashTheme.GoldBright,
                Parent = _topBar
            };

            var currencyCoin = new Label
            {
                Text = "12,450",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 13f),
                TextColor = InkWashTheme.PaperAged,
                Parent = _topBar
            };

            var currencyIngot = new Label
            {
                Text = "328",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 13f),
                TextColor = InkWashTheme.GoldBright,
                Parent = _topBar
            };

            var timeLabel = new Label
            {
                Text = "戌时 三刻",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.PaperAged,
                Parent = _topBar
            };
        }

        private void BuildContentArea()
        {
            _contentArea = new InkPanel
            {
                AnchorPreset = AnchorPresets.StretchAll,
                BackgroundColor = Color.Transparent,
                Parent = this
            };
        }

        private void BuildCategoryTabs()
        {
            _categoryTabs = new InkPanel[_categoryNames.Length];

            for (int i = 0; i < _categoryNames.Length; i++)
            {
                _categoryTabs[i] = new InkPanel
                {
                    Width = 200f,
                    Height = 44f,
                    BackgroundColor = i == 1 ? InkWashTheme.GoldPrimary * 0.1f : InkWashTheme.BaseTertiary,
                    Parent = _contentArea
                };

                var tabLabel = new Label
                {
                    Text = _categoryNames[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 14f),
                    TextColor = i == 1 ? InkWashTheme.GoldBright : InkWashTheme.PaperAged,
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                    Parent = _categoryTabs[i]
                };

                var countLabel = new Label
                {
                    Text = _categoryCounts[i].ToString(),
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 12f),
                    TextColor = InkWashTheme.TextDisabled,
                    HorizontalAlignment = TextAlignment.Far,
                    VerticalAlignment = TextAlignment.Center,
                    Parent = _categoryTabs[i]
                };
            }
        }

        private void BuildSettingsList()
        {
            var headerTitle = new Label
            {
                Text = "音效设置",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 28f),
                TextColor = InkWashTheme.GoldBright,
                Parent = _contentArea
            };

            var headerSubtitle = new Label
            {
                Text = "调整游戏音频体验，沉浸江湖之声",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                TextColor = InkWashTheme.TextTertiary,
                Parent = _contentArea
            };

            var volumeGroupHeader = new Label
            {
                Text = "音量调节",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 14f),
                TextColor = InkWashTheme.TextSecondary,
                Parent = _contentArea
            };

            for (int i = 0; i < _volumeLabels.Length; i++)
            {
                var settingItem = new InkPanel
                {
                    Width = 500f,
                    Height = 60f,
                    BackgroundColor = InkWashTheme.BaseTertiary,
                    Parent = _contentArea
                };

                var label = new Label
                {
                    Text = _volumeLabels[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                    TextColor = InkWashTheme.PaperBright,
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Near,
                    Parent = settingItem
                };

                var desc = new Label
                {
                    Text = _volumeDescs[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                    TextColor = InkWashTheme.TextDisabled,
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                    Parent = settingItem
                };

                var sliderBar = new InkBar
                {
                    Width = 200f,
                    Height = 8f,
                    Parent = settingItem
                };
                sliderBar.Value = _volumeValues[i];

                var valueLabel = new Label
                {
                    Text = $"{(int)(_volumeValues[i] * 100)}",
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 12f),
                    TextColor = InkWashTheme.GoldBright,
                    Parent = settingItem
                };
            }

            var audioFeatureHeader = new Label
            {
                Text = "音频功能",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 14f),
                TextColor = InkWashTheme.TextSecondary,
                Parent = _contentArea
            };

            for (int i = 0; i < _toggleLabels.Length; i++)
            {
                var settingItem = new InkPanel
                {
                    Width = 500f,
                    Height = 60f,
                    BackgroundColor = InkWashTheme.BaseTertiary,
                    Parent = _contentArea
                };

                var label = new Label
                {
                    Text = _toggleLabels[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                    TextColor = InkWashTheme.PaperBright,
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Near,
                    Parent = settingItem
                };

                var desc = new Label
                {
                    Text = _toggleDescs[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                    TextColor = InkWashTheme.TextDisabled,
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                    Parent = settingItem
                };

                var togglePanel = new ContainerControl
                {
                    Size = new Float2(40f, 20f),
                    BackgroundColor = _toggleValues[i] ? InkWashTheme.GoldPrimary * 0.3f : InkWashTheme.BaseElevated,
                    Parent = settingItem
                };

                var toggleThumb = new ContainerControl
                {
                    Size = new Float2(16f, 16f),
                    BackgroundColor = _toggleValues[i] ? InkWashTheme.GoldBright : InkWashTheme.PaperAged,
                    Parent = togglePanel
                };
            }

            var deviceHeader = new Label
            {
                Text = "音频设备",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 14f),
                TextColor = InkWashTheme.TextSecondary,
                Parent = _contentArea
            };

            var devicePanel = new InkPanel
            {
                Width = 500f,
                Height = 100f,
                BackgroundColor = InkWashTheme.BaseTertiary,
                Parent = _contentArea
            };

            var outputLabel = new Label
            {
                Text = "输出设备",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextDisabled,
                Parent = devicePanel
            };

            var outputValue = new Label
            {
                Text = "系统默认 · 扬声器（Realtek Audio）",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                TextColor = InkWashTheme.PaperBright,
                Parent = devicePanel
            };

            var inputLabel = new Label
            {
                Text = "输入设备",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextDisabled,
                Parent = devicePanel
            };

            var inputValue = new Label
            {
                Text = "系统默认 · 麦克风（USB Audio Device）",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                TextColor = InkWashTheme.PaperBright,
                Parent = devicePanel
            };

            var sampleLabel = new Label
            {
                Text = "采样率",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextDisabled,
                Parent = devicePanel
            };

            var sampleValue = new Label
            {
                Text = "48000 Hz",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 12f),
                TextColor = InkWashTheme.GoldBright,
                Parent = devicePanel
            };
        }

        private void BuildActionBar()
        {
            _actionBar = new InkPanel
            {
                Height = 60f,
                AnchorPreset = AnchorPresets.BottomLeft,
                BackgroundColor = InkWashTheme.BaseSecondary,
                Parent = this
            };

            var infoLabel = new Label
            {
                Text = "修改将在保存后生效",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextTertiary,
                Parent = _actionBar
            };

            var resetButton = new InkButton
            {
                Width = 100f,
                Height = 36f,
                Text = "恢复默认",
                Variant = InkButtonVariant.Ghost,
                Parent = _actionBar
            };

            var saveButton = new InkButton
            {
                Width = 100f,
                Height = 36f,
                Text = "保存设置",
                Variant = InkButtonVariant.Primary,
                Parent = _actionBar
            };
        }

        private void ApplyLayout()
        {
            float sw = _screenSize.X;
            float sh = _screenSize.Y;

            if (_backButton != null)
                _backButton.Location = new Float2(252f, 14f);

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

            if (_contentArea != null)
            {
                _contentArea.Size = new Float2(sw - 240f, sh - 120f);
                _contentArea.Location = new Float2(240f, 60f);
            }

            float catY = 24f;
            for (int i = 0; i < _categoryTabs.Length; i++)
            {
                if (_categoryTabs[i] != null)
                {
                    _categoryTabs[i].Location = new Float2(24f, catY);
                    catY += 44f;
                }
            }

            if (_actionBar != null)
            {
                _actionBar.Size = new Float2(sw - 240f, 60f);
                _actionBar.Location = new Float2(240f, sh - 60f);
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
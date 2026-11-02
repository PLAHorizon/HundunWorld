using System;
using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.Ink.Components;
using HundunWorld.Game.UI.StyleSystem;

namespace HundunWorld.Game.UI.Ink.Pages.Settings
{
    public class SettingsAudioPage : ContainerControl, IInkPage, IInkNavigationSource
    {
        private Float2 _screenSize;

        private InkPanel _leftNav;
        private InkPanel _topBar;
        private InkPanel _contentArea;

        // 顶栏控件：同样需要在 ApplyLayout 里排位置（原实现全部默认 (0,0)）
        private InkButton _backHudButton;
        private ContainerControl _topAvatar;
        private Label _topCharName;
        private Label _topCharLevel;
        private Label _topCoin;
        private Label _topIngot;
        private Label _topTime;
        private InkButton _quickJumpButton;

        private InkPanel[] _categoryTabs;

        // 内容区控件引用：位置在 ApplyLayout 统一排，值在 SyncFromSettings 统一刷新。
        // 这一页原先所有行/滑块都没有 Location，全部堆在内容区左上角——两件事一起修。
        private Label _titleLabel;
        private Label _subtitleLabel;
        private Label _volumeHeaderLabel;
        private Label _featureHeaderLabel;
        private Label _deviceHeaderLabel;
        private InkPanel[] _volumeRows;
        private Slider[] _volumeSliders;
        private Label[] _volumeValueLabels;
        private InkPanel[] _toggleRows;
        private AudioToggle[] _toggles;
        private InkPanel _devicePanel;
        private InkButton _resetButton;
        private InkButton _saveButton;

        /// <summary>正在从设置回填控件：此时滑块的 ValueChanged 不应再回写设置。</summary>
        private bool _syncingUi;

        /// <summary>内容列左边距：左侧分类标签占 24..224，内容从 268 起，避免压住标签列。</summary>
        private const float ContentPadX = 268f;
        private const float RowWidth = 500f;
        private const float RowHeight = 60f;
        private const float RowGap = 10f;
        private const float GroupHeaderH = 24f;

        private InkPanel _actionBar;

        private string[] _categoryNames = { "画面", "音效", "操作", "账号", "其他" };

        // 画面=设置主页 10 个设置行；音效=本页 5 个滑条 + 2 个开关；其余分类尚未接入，不编数字。
        private string[] _categoryCounts = { "10", "7", "—", "—", "—" };

        private string[] _volumeLabels = { "主音量", "背景音乐", "音效", "语音", "环境音" };
        private string[] _volumeDescs = {
            "调整游戏整体音量，影响所有音频通道",
            "调整场景与战斗背景音乐音量",
            "调整技能、招式与环境交互音效音量",
            "调整角色对话与剧情语音音量",
            "调整风声、水声、虫鸣等自然环境音量"
        };
        private string[] _toggleLabels = { "UI音效", "语音聊天" };
        private string[] _toggleDescs = {
            "界面按钮、标签切换等操作音效反馈",
            "开启队伍语音聊天功能，需配合麦克风使用"
        };

        /// <summary>
        /// 标准导航事件（对齐 Ink 页面契约，由工厂挂钩后经路由器跳转）。
        /// </summary>
        public event Action<string> NavigationRequested;

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
                BuildLeftNav();
                BuildTopBar();
                BuildContentArea();
                BuildCategoryTabs();
                BuildSettingsList();
                BuildActionBar();

                ApplyLayout();
                RefreshAllData();
                InkPlayerHeaderBinder.Bind(_topCharName, _topCharLevel, _topCoin);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[SettingsAudioPage] 初始化失败: {ex.Message}");
            }
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
                Size = new Float2(240f, 40f),
                Location = new Float2(0f, 20f),
                Parent = _leftNav
            };

            var divider = new InkDivider
            {
                Width = 208f,
                Height = 1f,
                Location = new Float2(16f, 68f),
                Parent = _leftNav
            };

            string[] navItems = { "任务", "抽卡", "通行证", "角色", "博物志", "奇珍阁", "设置" };

            // 左侧导航即设计链路：任务→抽卡→通行证→角色→博物志→奇珍阁→设置
            string[] navTargets =
            {
                InkPageDomIds.NavQuests,
                InkPageDomIds.NavGacha,
                InkPageDomIds.NavBattlePass,
                InkPageDomIds.NavCharacterV2,
                InkPageDomIds.NavBestiary,
                InkPageDomIds.NavShopRare,
                InkPageDomIds.NavSettings,
            };

            float navY = 80f;
            for (int i = 0; i < navItems.Length; i++)
            {
                var navItem = new InkButton
                {
                    Width = 240f,
                    Height = 52f,
                    Text = navItems[i],
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                    Location = new Float2(0f, navY),
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

                // 左栏项直达对应菜单（设计链路）
                int navIndex = i;
                navItem.Clicked += () => NavigationRequested?.Invoke(navTargets[navIndex]);
                navY += 52f;
            }

            float navH = _screenSize.Y;
            var footerDivider = new InkDivider
            {
                Width = 208f,
                Height = 1f,
                Location = new Float2(16f, navH - 58f),
                Parent = _leftNav
            };

            var versionLabel = new Label
            {
                Text = "v1.2.0 · Build 20260715",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextDisabled,
                HorizontalAlignment = TextAlignment.Center,
                Size = new Float2(240f, 16f),
                Location = new Float2(0f, navH - 44f),
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

            _backHudButton = new InkButton
            {
                Width = 80f,
                Height = 32f,
                Text = "返回战斗",
                Variant = InkButtonVariant.Ghost,
                TextColor = InkWashTheme.TextSecondary,
                Parent = _topBar
            };
            // 返回战斗 HUD（设计隐藏出口 back-hud）
            _backHudButton.Clicked += () => NavigationRequested?.Invoke(InkPageDomIds.BackOneLevel);

            _topAvatar = new ContainerControl
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
                // 头像圈 36×36：StretchAll 会被撑成 136×56，字被裁
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Vector2(36f, 36f),
                Parent = _topAvatar
            };

            _topCharName = new Label
            {
                Text = "—",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 15f),
                TextColor = InkWashTheme.PaperBright,
                Parent = _topBar
            };

            _topCharLevel = new Label
            {
                Text = "—",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 12f),
                TextColor = InkWashTheme.GoldBright,
                Parent = _topBar
            };

            _topCoin = new Label
            {
                Text = "—",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 13f),
                TextColor = InkWashTheme.PaperAged,
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
                Parent = _topBar
            };

            _topIngot = new Label
            {
                // 元宝没有服务端快照（ShopClientService 只有 Gold），不摆设计稿的 328
                Text = "—",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 13f),
                TextColor = InkWashTheme.GoldBright,
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
                Parent = _topBar
            };

            _topTime = new Label
            {
                // 世界时间同步没有接入客户端（同 MenuTimePage），顶栏不摆设计稿的时辰
                Text = "—",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.PaperAged,
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
                Parent = _topBar
            };

            // 设计稿顶栏的「小游戏」快捷入口（data-dom-id=link-mini-game-complete）
            _quickJumpButton = new InkButton
            {
                Width = 72f,
                Height = 26f,
                Text = "小游戏",
                Variant = InkButtonVariant.Ghost,
                TextColor = InkWashTheme.TextSecondary,
                Parent = _topBar
            };
            _quickJumpButton.Clicked += () => NavigationRequested?.Invoke(InkPageDomIds.MiniGameComplete);
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
                    Location = new Float2(16f, 0f),
                    Size = new Float2(140f, 44f),
                    Parent = _categoryTabs[i]
                };

                var countLabel = new Label
                {
                    Text = _categoryCounts[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 12f),
                    TextColor = InkWashTheme.TextDisabled,
                    HorizontalAlignment = TextAlignment.Far,
                    VerticalAlignment = TextAlignment.Center,
                    Location = new Float2(156f, 0f),
                    Size = new Float2(28f, 44f),
                    Parent = _categoryTabs[i]
                };

                // 整条标签可点：面板本身没有事件，铺一层命中区在最上面
                int tabIndex = i;
                var hit = new TabHitArea
                {
                    AnchorPreset = AnchorPresets.StretchAll,
                    Parent = _categoryTabs[i]
                };
                hit.Clicked += () => OnCategoryTabClicked(tabIndex);
            }
        }

        /// <summary>
        /// 分类标签点击：音效页就是当前页；画面回到设置主页；操作/账号/其他尚未接入，给明确提示而不是静默无反应。
        /// </summary>
        private void OnCategoryTabClicked(int index)
        {
            if (index == 1) return;

            if (index == 0)
            {
                NavigationRequested?.Invoke(InkPageDomIds.NavSettings);
                return;
            }

            HundunWorld.Game.UI.UIHelper.ShowToast("该分类的设置项尚未接入", HundunWorld.Game.UI.ToastType.Info);
        }

        /// <summary>顶栏是否已拿到真实玩家信息（进世界瞬间构造的页面拿不到，需自愈一次）。</summary>
        private bool _headerBound;
        private float _headerBindTimer;
        private int _headerBindAttempts;

        /// <summary>玩家数据晚于页面构造时补绑一次顶栏（与设置页 §87 同一套做法）。</summary>
        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);

            if (_headerBound || !VisibleInHierarchy)
                return;

            _headerBindTimer += deltaTime;
            if (_headerBindTimer < 0.5f || _headerBindAttempts >= 20)
                return;

            _headerBindTimer = 0f;
            _headerBindAttempts++;
            InkPlayerHeaderBinder.Bind(_topCharName, _topCharLevel, _topCoin);
            _headerBound = _topCharName != null && !string.IsNullOrEmpty(_topCharName.Text) && _topCharName.Text != "—";
        }

        /// <summary>分类标签的透明命中区（面板容器本身没有 Clicked）。</summary>
        private sealed class TabHitArea : Control
        {
            public event Action Clicked;

            public TabHitArea()
            {
                BackgroundColor = Color.Transparent;
                AutoFocus = false;
            }

            public override bool OnMouseUp(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left)
                {
                    Clicked?.Invoke();
                    return true;
                }
                return base.OnMouseUp(location, button);
            }
        }

        /// <summary>顶栏「音频系统」标签（设计稿 .ink-tag-brand）</summary>
        private InkTag _systemTag;

        private void BuildSettingsList()
        {
            _titleLabel = new Label
            {
                Text = "音效设置",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 28f),
                TextColor = InkWashTheme.GoldBright,
                Parent = _contentArea
            };

            // 设计稿顶栏标签「音频系统」（.ink-tag-brand）
            var systemTag = new InkTag
            {
                Text = "音频系统",
                TagVariant = InkTagVariant.Brand,
                Width = 76f,
                Height = 22f,
                Parent = _contentArea
            };
            _systemTag = systemTag;

            _subtitleLabel = new Label
            {
                Text = "调整游戏音频体验，沉浸江湖之声",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                TextColor = InkWashTheme.TextTertiary,
                Parent = _contentArea
            };

            _volumeHeaderLabel = new Label
            {
                Text = "音量调节",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 14f),
                TextColor = InkWashTheme.TextSecondary,
                Parent = _contentArea
            };

            var settings = HundunWorld.Game.Core.GameStateManager.Instance.Settings;
            var initialVolumes = new[]
            {
                settings.MasterVolume,
                settings.BGMVolume,
                settings.SFXVolume,
                settings.VoiceVolume,
                settings.AmbientVolume,
            };

            _volumeRows = new InkPanel[_volumeLabels.Length];
            _volumeSliders = new Slider[_volumeLabels.Length];
            _volumeValueLabels = new Label[_volumeLabels.Length];

            for (int i = 0; i < _volumeLabels.Length; i++)
            {
                int index = i;
                float initial = Mathf.Clamp(initialVolumes[i], 0f, 1f);

                var settingItem = new InkPanel
                {
                    Width = RowWidth,
                    Height = RowHeight,
                    BackgroundColor = InkWashTheme.BaseTertiary,
                    Parent = _contentArea
                };
                _volumeRows[i] = settingItem;

                var label = new Label
                {
                    Text = _volumeLabels[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                    TextColor = InkWashTheme.PaperBright,
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Near,
                    Location = new Float2(16f, 4f),
                    Size = new Float2(300f, 18f),
                    Parent = settingItem
                };

                var desc = new Label
                {
                    Text = _volumeDescs[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                    TextColor = InkWashTheme.TextDisabled,
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                    Location = new Float2(16f, 22f),
                    Size = new Float2(460f, 16f),
                    Parent = settingItem
                };

                var slider = new Slider(320f, 18f)
                {
                    Location = new Float2(16f, 38f),
                    Minimum = 0f,
                    Maximum = 1f,
                    Value = initial,
                    TrackThickness = 6,
                    TrackLineColor = InkWashTheme.Abyss,
                    TrackFillLineColor = InkWashTheme.GoldPrimary,
                    ThumbColor = InkWashTheme.GoldBright,
                    ThumbColorHighlighted = InkWashTheme.GoldBright,
                    ThumbColorSelected = InkWashTheme.GoldBright,
                    Parent = settingItem
                };
                // Value 的赋值本身会触发 ValueChanged：先赋值、后订阅，初始化才不会回写设置
                slider.ValueChanged += () => OnVolumeChanged(index, slider.Value);
                _volumeSliders[i] = slider;

                var valueLabel = new Label
                {
                    Text = Mathf.RoundToInt(initial * 100f).ToString(),
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 12f),
                    TextColor = InkWashTheme.GoldBright,
                    HorizontalAlignment = TextAlignment.Far,
                    VerticalAlignment = TextAlignment.Center,
                    Location = new Float2(344f, 38f),
                    Size = new Float2(60f, 18f),
                    Parent = settingItem
                };
                _volumeValueLabels[i] = valueLabel;
            }

            _featureHeaderLabel = new Label
            {
                Text = "音频功能",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 14f),
                TextColor = InkWashTheme.TextSecondary,
                Parent = _contentArea
            };

            bool[] initialToggles = { settings.UISfxEnabled, settings.VoiceChatEnabled };
            _toggleRows = new InkPanel[_toggleLabels.Length];
            _toggles = new AudioToggle[_toggleLabels.Length];

            for (int i = 0; i < _toggleLabels.Length; i++)
            {
                int index = i;

                var settingItem = new InkPanel
                {
                    Width = RowWidth,
                    Height = RowHeight,
                    BackgroundColor = InkWashTheme.BaseTertiary,
                    Parent = _contentArea
                };
                _toggleRows[i] = settingItem;

                var label = new Label
                {
                    Text = _toggleLabels[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                    TextColor = InkWashTheme.PaperBright,
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Near,
                    Location = new Float2(16f, 8f),
                    Size = new Float2(300f, 18f),
                    Parent = settingItem
                };

                var desc = new Label
                {
                    Text = _toggleDescs[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                    TextColor = InkWashTheme.TextDisabled,
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                    Location = new Float2(16f, 28f),
                    Size = new Float2(380f, 16f),
                    Parent = settingItem
                };

                var toggle = new AudioToggle
                {
                    Location = new Float2(RowWidth - 16f - 40f, 20f),
                    Size = new Float2(40f, 20f),
                    Checked = initialToggles[i],
                    Parent = settingItem
                };
                toggle.CheckedChanged += on => OnToggleChanged(index, on);
                _toggles[i] = toggle;
            }

            _deviceHeaderLabel = new Label
            {
                Text = "音频设备",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 14f),
                TextColor = InkWashTheme.TextSecondary,
                Parent = _contentArea
            };

            _devicePanel = new InkPanel
            {
                Width = RowWidth,
                Height = 100f,
                BackgroundColor = InkWashTheme.BaseTertiary,
                Parent = _contentArea
            };

            // MakeDeviceLabel 的对象初始化器里已经把 Parent 设为 _devicePanel，
            // 这里不能再 AddChild（否则 Flax 抛 "child is already its parent"）。
            MakeDeviceLabel("输出设备", 16f, 10f, 90f, false, false);
            MakeDeviceLabel("系统默认（设备枚举尚未接入）", 110f, 10f, 370f, true, false);
            MakeDeviceLabel("输入设备", 16f, 40f, 90f, false, false);
            MakeDeviceLabel("系统默认（设备枚举尚未接入）", 110f, 40f, 370f, true, false);
            MakeDeviceLabel("采样率", 16f, 70f, 90f, false, false);
            MakeDeviceLabel("—", 110f, 70f, 370f, true, true);
        }

        /// <summary>音频设备面板内的一行：左标签（暗色小字）或右值（纸白数字/正文）。</summary>
        private Label MakeDeviceLabel(string text, float x, float y, float width, bool isValue, bool isNumber)
        {
            return new Label
            {
                Text = text,
                Font = InkRenderHelper.GetFontRef(
                    isValue && isNumber ? InkWashTheme.FontRole.Number : InkWashTheme.FontRole.Body,
                    isValue ? 12f : 11f),
                TextColor = isValue
                    ? (isNumber ? InkWashTheme.GoldBright : InkWashTheme.PaperBright)
                    : InkWashTheme.TextDisabled,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                Location = new Float2(x, y),
                Size = new Float2(width, 16f),
                Parent = _devicePanel
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
                Text = "改动即时生效 · 5 秒内自动保存",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextTertiary,
                Location = new Float2(16f, 22f),
                Size = new Float2(300f, 16f),
                Parent = _actionBar
            };

            _resetButton = new InkButton
            {
                Width = 100f,
                Height = 36f,
                Text = "恢复默认",
                Variant = InkButtonVariant.Ghost,
                Parent = _actionBar
            };
            _resetButton.Clicked += OnResetClicked;

            _saveButton = new InkButton
            {
                Width = 100f,
                Height = 36f,
                Text = "保存设置",
                Variant = InkButtonVariant.Primary,
                Parent = _actionBar
            };
            _saveButton.Clicked += OnSaveClicked;
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

                float topW = sw - 240f;
                if (_backHudButton != null)
                    _backHudButton.Location = new Float2(16f, 14f);
                if (_topAvatar != null)
                    _topAvatar.Location = new Float2(112f, 12f);
                if (_topCharName != null)
                {
                    _topCharName.Location = new Float2(158f, 8f);
                    _topCharName.Size = new Float2(160f, 20f);
                }
                if (_topCharLevel != null)
                {
                    _topCharLevel.Location = new Float2(158f, 30f);
                    _topCharLevel.Size = new Float2(100f, 16f);
                }

                // 右侧从右往左：时辰 / 元宝 / 铜钱
                if (_topTime != null)
                {
                    _topTime.Location = new Float2(topW - 16f - 100f, 20f);
                    _topTime.Size = new Float2(100f, 20f);
                }
                if (_topIngot != null)
                {
                    _topIngot.Location = new Float2(topW - 16f - 100f - 8f - 64f, 18f);
                    _topIngot.Size = new Float2(64f, 24f);
                }
                if (_topCoin != null)
                {
                    _topCoin.Location = new Float2(topW - 16f - 100f - 8f - 64f - 8f - 80f, 18f);
                    _topCoin.Size = new Float2(80f, 24f);
                }
                if (_quickJumpButton != null)
                {
                    _quickJumpButton.Location = new Float2(
                        topW - 16f - 100f - 8f - 64f - 8f - 80f - 8f - 72f, 17f);
                }
            }

            if (_contentArea != null)
            {
                _contentArea.Size = new Float2(sw - 240f, sh - 120f);
                _contentArea.Location = new Float2(240f, 60f);
            }

            // 内容区纵向排布：标题 → 音量组 → 功能组 → 设备组。
            // 这些控件以前一个 Location 都没设，全部叠在内容区左上角。
            if (_titleLabel != null)
            {
                _titleLabel.Location = new Float2(ContentPadX, 20f);
                _titleLabel.Size = new Float2(500f, 36f);
            }
            if (_subtitleLabel != null)
            {
                _subtitleLabel.Location = new Float2(ContentPadX, 58f);
                _subtitleLabel.Size = new Float2(700f, 20f);
            }

            if (_volumeHeaderLabel != null)
            {
                _volumeHeaderLabel.Location = new Float2(ContentPadX, 92f);
                _volumeHeaderLabel.Size = new Float2(300f, GroupHeaderH);
            }

            float contentY = 124f;
            if (_volumeRows != null)
            {
                for (int i = 0; i < _volumeRows.Length; i++)
                {
                    if (_volumeRows[i] == null) continue;
                    _volumeRows[i].Location = new Float2(ContentPadX, contentY);
                    contentY += RowHeight + RowGap;
                }
            }

            if (_featureHeaderLabel != null)
            {
                _featureHeaderLabel.Location = new Float2(ContentPadX, contentY + 8f);
                _featureHeaderLabel.Size = new Float2(300f, GroupHeaderH);
            }
            contentY += 8f + GroupHeaderH + 8f;

            if (_toggleRows != null)
            {
                for (int i = 0; i < _toggleRows.Length; i++)
                {
                    if (_toggleRows[i] == null) continue;
                    _toggleRows[i].Location = new Float2(ContentPadX, contentY);
                    contentY += RowHeight + RowGap;
                }
            }

            if (_deviceHeaderLabel != null)
            {
                _deviceHeaderLabel.Location = new Float2(ContentPadX, contentY + 8f);
                _deviceHeaderLabel.Size = new Float2(300f, GroupHeaderH);
            }
            contentY += 8f + GroupHeaderH + 8f;

            if (_devicePanel != null)
            {
                _devicePanel.Location = new Float2(ContentPadX, contentY);
                _devicePanel.Size = new Float2(RowWidth, 100f);
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

                // 按钮在操作条内右对齐（操作条自身从 x=240 起算）
                float barW = sw - 240f;
                if (_saveButton != null)
                    _saveButton.Location = new Float2(barW - 16f - 100f, 12f);
                if (_resetButton != null)
                    _resetButton.Location = new Float2(barW - 16f - 100f - 12f - 100f, 12f);
            }

            // 顶栏「音频系统」标签：内容区右上角（坐标相对 _contentArea，不是页面）
            if (_systemTag != null)
            {
                _systemTag.Location = new Float2(Mathf.Max(ContentPadX, (sw - 240f) - 76f - 24f), 14f);
            }
        }

        public void RefreshLayout()
        {
            _screenSize = new Float2(Width, Height);
            ApplyLayout();
        }

        public void RefreshAllData()
        {
            SyncFromSettings();
        }

        /// <summary>滑块拖动 → 写回设置。UpdateSettings 会立即应用并标记待保存，不必等"保存"按钮。</summary>
        private void OnVolumeChanged(int index, float value)
        {
            if (_syncingUi) return;

            if (_volumeValueLabels != null && index >= 0 && index < _volumeValueLabels.Length
                && _volumeValueLabels[index] != null)
            {
                _volumeValueLabels[index].Text = Mathf.RoundToInt(value * 100f).ToString();
            }

            var state = HundunWorld.Game.Core.GameStateManager.Instance;
            switch (index)
            {
                case 0: state.SetMasterVolume(value); break;
                case 1: state.SetBGMVolume(value); break;
                case 2: state.SetSFXVolume(value); break;
                case 3: state.SetVoiceVolume(value); break;
                case 4: state.SetAmbientVolume(value); break;
            }
        }

        private void OnToggleChanged(int index, bool on)
        {
            if (_syncingUi) return;

            var state = HundunWorld.Game.Core.GameStateManager.Instance;
            if (index == 0) state.SetUISfxEnabled(on);
            else if (index == 1) state.SetVoiceChatEnabled(on);
        }

        private void OnSaveClicked()
        {
            HundunWorld.Game.Core.GameStateManager.Instance.SaveSettings();
            HundunWorld.Game.UI.UIHelper.ShowToast("设置已保存", HundunWorld.Game.UI.ToastType.Success);
        }

        private void OnResetClicked()
        {
            HundunWorld.Game.Core.GameStateManager.Instance.ResetSettings();
            SyncFromSettings();
            HundunWorld.Game.UI.UIHelper.ShowToast("已恢复默认设置", HundunWorld.Game.UI.ToastType.Info);
        }

        /// <summary>
        /// 把当前设置回填到控件：打开页面、恢复默认后都走这一条。
        /// 回填期间置 <see cref="_syncingUi"/>，避免滑块的 ValueChanged 又把同一个值写回设置。
        /// </summary>
        private void SyncFromSettings()
        {
            if (_volumeSliders == null || _volumeValueLabels == null) return;

            var settings = HundunWorld.Game.Core.GameStateManager.Instance.Settings;
            var values = new[]
            {
                settings.MasterVolume,
                settings.BGMVolume,
                settings.SFXVolume,
                settings.VoiceVolume,
                settings.AmbientVolume,
            };

            _syncingUi = true;
            try
            {
                for (int i = 0; i < _volumeSliders.Length && i < values.Length; i++)
                {
                    if (_volumeSliders[i] == null) continue;

                    float value = Mathf.Clamp(values[i], 0f, 1f);
                    _volumeSliders[i].Value = value;
                    if (_volumeValueLabels[i] != null)
                        _volumeValueLabels[i].Text = Mathf.RoundToInt(value * 100f).ToString();
                }

                if (_toggles != null)
                {
                    if (_toggles.Length > 0 && _toggles[0] != null) _toggles[0].Checked = settings.UISfxEnabled;
                    if (_toggles.Length > 1 && _toggles[1] != null) _toggles[1].Checked = settings.VoiceChatEnabled;
                }
            }
            finally
            {
                _syncingUi = false;
            }
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

        /// <summary>音频设置页的开关：40×20 圆角轨道 + 圆形滑块，左键切换。</summary>
        private sealed class AudioToggle : ContainerControl
        {
            private bool _checked;

            public event Action<bool> CheckedChanged;

            public bool Checked
            {
                get => _checked;
                set => _checked = value;
            }

            public AudioToggle()
            {
                BackgroundColor = Color.Transparent;
                ClipChildren = false;
                AutoFocus = false;
            }

            public override bool OnMouseDown(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left)
                {
                    _checked = !_checked;
                    CheckedChanged?.Invoke(_checked);
                    return true;
                }
                return base.OnMouseDown(location, button);
            }

            public override void Draw()
            {
                if (!Visible || Width <= 0f || Height <= 0f) return;

                var rect = new Rectangle(0f, 0f, Width, Height);
                float radius = Height * 0.5f;

                InkRenderHelper.FillRoundedRectangle(rect, radius,
                    _checked ? InkWashTheme.GoldPrimary * 0.3f : InkWashTheme.BaseElevated);
                InkRenderHelper.DrawRoundedRectangle(rect, radius,
                    _checked ? InkWashTheme.GoldDeep : InkWashTheme.BorderNeutralL2, 1f);

                float thumbRadius = (Height - 4f) * 0.5f;
                var center = new Float2(
                    _checked ? Width - 2f - thumbRadius : 2f + thumbRadius,
                    Height * 0.5f);
                InkRenderHelper.FillCircle(center, thumbRadius,
                    _checked ? InkWashTheme.GoldBright : InkWashTheme.PaperDark);
            }
        }
    }
}
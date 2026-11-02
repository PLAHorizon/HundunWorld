using System;
using System.Collections.Generic;
using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.Ink.Components;
using HundunWorld.Game.UI.StyleSystem;

namespace HundunWorld.Game.UI.Ink.Pages.Gacha
{
    /// <summary>
    /// 祈愿页（设计稿 hundun-yy-ui/pages/menu-gacha.html）。
    /// 结构：顶部栏（返回 / 祈愿标题 / 卡池名 / 元宝·命星余额）
    ///      + 左侧卡池选择器（四个卡池标签 + 卡池剩余倒计时）
    ///      + 中央展示区（保底计数面板 / 角色展示框 / 概率概览面板）
    ///      + 底部抽卡区（祈愿一次·祈愿十次 + 抽卡进度）
    ///      + 右下角链接（活动 / 祈愿记录 / 概率公示 / 详情规则）+ 左下角 UP 名侠预览。
    /// 颜色与字体全部取自 <see cref="InkWashTheme"/> token，禁止硬编码色值。
    /// </summary>
    public class MenuGachaPage : ContainerControl, IInkPage, IInkNavigationSource
    {
        // ================= 布局常量（对齐设计稿 CSS 像素） =================

        /// <summary>顶部栏高度（.gacha-topbar height: 60px）</summary>
        private const float TopbarHeight = 60f;

        /// <summary>左侧卡池选择器宽度（.pool-selector width: 200px）</summary>
        private const float PoolSelectorWidth = 200f;

        /// <summary>内容区外边距（--ink-space-6 = 32px）</summary>
        private const float ContentPadding = 32f;

        /// <summary>角色展示框尺寸（.character-showcase 400x500）</summary>
        private const float ShowcaseWidth = 400f;
        private const float ShowcaseHeight = 500f;

        /// <summary>展示框底部信息条高度</summary>
        private const float ShowcaseFooterHeight = 84f;

        /// <summary>底部抽卡区高度（按钮 52 + 下方价格行 16，再加底边留白）</summary>
        private const float DrawAreaHeight = 136f;

        /// <summary>保底计数面板宽度（.pity-counter-panel width: 200px）</summary>
        private const float PityPanelWidth = 200f;

        /// <summary>概率概览面板宽度（.pool-overview-panel width: 220px）</summary>
        private const float OverviewPanelWidth = 220f;

        /// <summary>UP 名侠预览面板宽度（.up-preview-panel width: 220px）</summary>
        private const float UpPreviewWidth = 220f;

        /// <summary>抽卡按钮宽度（.draw-btn min-width + padding）</summary>
        private const float DrawButtonWidth = 220f;

        /// <summary>抽卡按钮高度</summary>
        private const float DrawButtonHeight = 52f;

        // ================= 运行时状态 =================

        private Float2 _screenSize;

        /// <summary>当前选中卡池索引</summary>
        private int _activePoolIndex;

        // 以下字段初值即 hundun-yy-ui/pages/menu-gacha.html 的示例数据，
        // 构造时直接写入各子控件；接入真实数据源后由本类「数据绑定 API」区的 Bind* 方法覆盖。

        /// <summary>已祈愿次数（设计稿 47）</summary>
        // 祈愿没有服务端数据：次数/保底/余额全部按 0 呈现（2026-10-29）
        private int _drawCount = 0;
        /// <summary>是否已拿到服务端祈愿数据（BindPity 置位）；false 时保底/记录一律显示占位。</summary>
        private bool _hasServerData;

        /// <summary>小保底累计次数（设计稿 47/90）</summary>
        private int _smallPity = 0;

        /// <summary>大保底累计次数（设计稿 0/90）</summary>
        private int _greatPity;

        /// <summary>保底总次数（设计稿 90）</summary>
        private int _pityTotal = 90;

        /// <summary>
        /// 距离保底剩余次数（设计稿「距离保底还有 3 次」）。
        /// 注：设计稿此值与其「小保底 47/90」并不自洽（90-47=43）；此处按设计稿示例数据落地，
        /// 接入真实数据源后由 <see cref="BindPity"/> 覆盖。
        /// </summary>
        private int _pityRemaining = 90;

        /// <summary>UP 单抽价格（元宝，设计稿 160）</summary>
        private int _singleDrawPrice = 160;

        /// <summary>UP 十连价格（元宝，设计稿 1600）</summary>
        private int _tenDrawPrice = 1600;

        /// <summary>元宝余额（设计稿 328）</summary>
        private int _ingot = -1;

        /// <summary>命星余额：没有服务端快照，-1 表示未知（显示「—」）</summary>
        private int _fateStar = -1;

        // 卡池结束时间是服务端排期，客户端没有数据源；-1 表示未知，
        // 不摆设计稿的「12 天 07 时 23 分」——那会被当成真实的限时倒计时。
        private int _countdownDay = -1;
        private int _countdownHour = -1;
        private int _countdownMinute = -1;

        // ================= 背景装饰层 =================

        private InkSplash _splashGold;
        private InkSplash _splashVermilion;
        private InkSplash _splashCenter;

        // ================= 顶部栏 =================

        private Panel _topbar;
        private InkDivider _topbarDivider;
        private InkBackButton _backButton;
        private Label _titleLabel;
        private Label _titleEnLabel;
        private InkDividerVertical _titleDivider;
        private Label _poolNameLabel;

        private InkPanel _yuanbaoDisplay;
        private InkAvatar _yuanbaoIcon;
        private Label _yuanbaoIconChar;
        private Label _yuanbaoLabel;
        private Label _yuanbaoValue;
        private InkButton _rechargeButton;
        private InkDividerVertical _currencyDivider;

        private InkPanel _mingxingDisplay;
        private InkAvatar _mingxingIcon;
        private Label _mingxingIconChar;
        private Label _mingxingLabel;
        private Label _mingxingValue;

        // ================= 左侧卡池选择器 =================

        private Panel _poolSelector;
        private Label _poolSelectorTitle;
        private InkDividerVertical _poolSelectorDivider;
        private readonly List<PoolTabView> _poolTabs = new List<PoolTabView>();

        private InkPanel _countdownPanel;
        private Label _countdownLabel;
        private readonly List<Label> _countdownNumbers = new List<Label>();
        private readonly List<Label> _countdownUnits = new List<Label>();

        // ================= 中央展示区 =================

        private ContainerControl _centerStage;

        private InkPanel _pityPanel;
        private Label _pityTitle;
        private Label _greatPityLabel;
        private Label _greatPityCurrent;
        private Label _greatPitySeparator;
        private Label _greatPityTotal;
        private InkBar _greatPityBar;
        private Label _smallPityLabel;
        private Label _smallPityCurrent;
        private Label _smallPitySeparator;
        private Label _smallPityTotal;
        private InkBar _smallPityBar;

        private ContainerControl _showcase;
        private Panel _showcaseInner;
        private InkSplash _showcaseSilhouette;
        private Label _characterNamePrefix;
        private Label _characterNameDot;
        private Label _characterNameMain;
        private Label _characterTitleText;
        private InkVerticalTitle _showcaseVerticalText;
        private Panel _showcaseFooter;
        private Label _showcaseFooterName;
        private Label _showcaseFooterDesc;
        private InkTag _showcaseUpBadge;
        private InkTag _showcaseQualityBadge;

        private InkPanel _overviewPanel;
        private Label _overviewTitle;
        private readonly List<OverviewRowView> _overviewRows = new List<OverviewRowView>();
        private InkDivider _overviewDivider;
        private readonly List<Label> _guaranteeLabels = new List<Label>();

        // ================= 底部抽卡区 =================

        private Panel _drawArea;
        private InkButton _singleDrawButton;
        private Label _singleDrawCost;
        private InkButton _multiDrawButton;
        private Label _multiDrawCost;
        private InkTag _multiDrawSaveBadge;
        private Label _drawCountLabel;
        private Label _drawPityLabel;
        private InkBar _drawProgressBar;

        // ================= 右下角链接 / 左下角 UP 预览 =================

        private readonly List<InkButton> _linkButtons = new List<InkButton>();
        private InkPanel _upPreviewPanel;
        private Label _upPreviewTitle;
        private readonly List<UpPreviewItemView> _upPreviewItems = new List<UpPreviewItemView>();

        /// <summary>导航请求事件（由 MainUIManager 反射挂钩至 InkPageRouter）。</summary>
        public event Action<string> NavigationRequested;

        // ================= 设计稿数据 =================

        /// <summary>卡池标签数据（.pool-tab 名称 / 说明 / 角标 + 展示区联动文案）。</summary>
        private sealed class GachaPoolData
        {
            public string Name;
            public string Desc;
            public string Badge;
            public string TopbarName;
            public string ShowcaseName;
            public string ShowcaseDesc;

            public GachaPoolData(string name, string desc, string badge, string topbarName, string showcaseName, string showcaseDesc)
            {
                Name = name;
                Desc = desc;
                Badge = badge;
                TopbarName = topbarName;
                ShowcaseName = showcaseName;
                ShowcaseDesc = showcaseDesc;
            }
        }

        /// <summary>四个卡池（对齐设计稿 .pool-tabs 的四个标签与 topbar 卡池名）。</summary>
        private static readonly GachaPoolData[] Pools =
        {
            new GachaPoolData("名侠祈愿", "限定名侠 · 概率UP", null, "名侠祈愿 · 青莲剑仙", "青莲剑仙", "概率UP · 限定名侠"),
            new GachaPoolData("武器祈愿", "限定神兵 · 概率UP", null, "武器祈愿", "武器祈愿", "限定神兵 · 概率UP"),
            new GachaPoolData("常驻祈愿", "名侠神兵 · 常驻开放", null, "常驻祈愿", "常驻祈愿", "名侠神兵 · 常驻开放"),
            new GachaPoolData("新手祈愿", "新手限定 · 首抽折扣", "新手", "新手祈愿", "新手祈愿", "新手限定 · 首抽折扣")
        };

        /// <summary>概率概览行数据（品质点色 + 名称 + 概率）。</summary>
        private sealed class GachaRateRow
        {
            public string Name;
            public string Rate;
            public Color Color;

            public GachaRateRow(string name, string rate, Color color)
            {
                Name = name;
                Rate = rate;
                Color = color;
            }
        }

        /// <summary>
        /// 概率概览四行（对齐设计稿 .pool-overview-list）。
        /// 概率是服务端公示数据，客户端没有来源：一律显示「—」，不摆设计稿的 1.20%/8.80%/30%/60%。
        /// </summary>
        private static readonly GachaRateRow[] RateRows =
        {
            new GachaRateRow("传说名侠", "—", InkWashTheme.VermilionPrimary),
            new GachaRateRow("史诗名侠", "—", InkWashTheme.QualityEpic),
            new GachaRateRow("稀有名侠", "—", InkWashTheme.QualityRare),
            new GachaRateRow("精良名侠", "—", InkWashTheme.QualityUncommon)
        };

        /// <summary>概率概览面板底部说明：保底规则同样没有服务端来源，只留一句如实提示。</summary>
        private static readonly string[] GuaranteeTexts =
        {
            "概率与保底规则尚未接入服务端"
        };

        /// <summary>卡池剩余倒计时单位（天 / 时 / 分）。</summary>
        private static readonly string[] CountdownUnits = { "天", "时", "分" };

        /// <summary>
        /// 卡池剩余倒计时数值文案（设计稿 12 天 07 时 23 分）。
        /// 由实例字段 <c>_countdownDay/Hour/Minute</c> 生成，便于 <see cref="BindPoolCountdown"/> 覆盖。
        /// </summary>
        private string[] CountdownNumberTexts()
        {
            // 未绑定（_countdownDay < 0）时只有一个「—」，不给出任何假的剩余时间
            if (_countdownDay < 0)
                return new[] { "—", string.Empty, string.Empty };

            return new[]
            {
                _countdownDay.ToString("D2"),
                _countdownHour.ToString("D2"),
                _countdownMinute.ToString("D2")
            };
        }

        /// <summary>未绑定卡池排期时时辰单位不显示（否则会出现「— 天 — 时 — 分」）。</summary>
        private string[] CountdownUnitTexts()
        {
            return _countdownDay < 0
                ? new[] { string.Empty, string.Empty, string.Empty }
                : CountdownUnits;
        }

        /// <summary>UP 名侠预览数据（对齐设计稿 .up-preview-list）。</summary>
        private sealed class GachaUpEntry
        {
            public string Char;
            public string Name;
            public string Title;
            public Color Color;

            public GachaUpEntry(string ch, string name, string title, Color color)
            {
                Char = ch;
                Name = name;
                Title = title;
                Color = color;
            }
        }

        /// <summary>当前 UP 名侠（传说/史诗品质用不同点色）。</summary>
        private static readonly GachaUpEntry[] UpEntries =
        {
            new GachaUpEntry("李", "李白", "青莲剑仙", InkWashTheme.VermilionPrimary),
            new GachaUpEntry("杜", "杜甫", "诗圣", InkWashTheme.QualityEpic),
            new GachaUpEntry("王", "王维", "诗佛", InkWashTheme.QualityEpic)
        };

        // ================= 构造 =================

        public MenuGachaPage()
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
                BuildTopbar();
                BuildPoolSelector();
                BuildCenterStage();
                BuildDrawArea();
                BuildGachaLinks();
                BuildUpPreview();

                ApplyLayout();
                RefreshAllData();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[MenuGachaPage] 初始化失败: {ex.Message}");
            }
        }

        // ================= 1. 背景层 =================

        /// <summary>
        /// 背景层：金色水墨光效底 + 暗角 + 三处水墨晕染（对齐设计稿 .gacha-bg-layer / .ink-splash-gacha）。
        /// </summary>
        private void BuildAtmosphere()
        {
            // 水墨背景层与暗角由外壳统一提供（本页为沉浸式全屏页），页面只保留专属晕染。
            _splashGold = new InkSplash
            {
                Variant = InkSplashVariant.Normal,
                Opacity = 0.22f,
                AnchorPreset = AnchorPresets.TopLeft,
                Parent = this
            };

            _splashCenter = new InkSplash
            {
                Variant = InkSplashVariant.Elevated,
                Opacity = 0.12f,
                AnchorPreset = AnchorPresets.TopLeft,
                Parent = this
            };

            _splashVermilion = new InkSplash
            {
                Variant = InkSplashVariant.Vermilion,
                Opacity = 0.20f,
                AnchorPreset = AnchorPresets.TopLeft,
                Parent = this
            };
        }

        // ================= 2. 顶部栏 =================

        /// <summary>
        /// 顶部栏：返回按钮 + 祈愿标题（含英文副标） + 竖分隔 + 卡池名；
        /// 右侧元宝余额（含充值按钮）、竖分隔、命星余额（对齐设计稿 .gacha-topbar）。
        /// </summary>
        private void BuildTopbar()
        {
            _topbar = new Panel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = WithAlpha(InkWashTheme.BaseDefault, 0.92f),
                Parent = this
            };

            _topbarDivider = new InkDivider
            {
                Height = 1f,
                AnchorPreset = AnchorPresets.TopLeft,
                Parent = _topbar
            };

            _backButton = new InkBackButton
            {
                Size = new Float2(36f, 36f),
                TooltipText = "返回战斗",
                Parent = _topbar
            };
            _backButton.Clicked += OnBackClicked;

            _titleLabel = MakeLabel(_topbar, "祈愿", InkWashTheme.FontRole.Display, 28f,
                InkWashTheme.GoldBright, 0f, 0f, 220f, 34f);
            _titleLabel.TooltipText = "祈愿 - 混沌世界";

            _titleEnLabel = MakeLabel(_topbar, "Wishing Ceremony", InkWashTheme.FontRole.Body, 11f,
                InkWashTheme.TextTertiary, 0f, 0f, 260f, 14f);

            _titleDivider = new InkDividerVertical
            {
                Size = new Float2(1f, 28f),
                AnchorPreset = AnchorPresets.TopLeft,
                Parent = _topbar
            };

            _poolNameLabel = MakeLabel(_topbar, Pools[0].TopbarName, InkWashTheme.FontRole.Heading, 15f,
                InkWashTheme.PaperAged, 0f, 0f, 320f, 20f);

            // ---- 元宝余额 ----
            _yuanbaoDisplay = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Parent = _topbar
            };

            _yuanbaoIcon = new InkAvatar
            {
                Size = new Float2(28f, 28f),
                Parent = _yuanbaoDisplay
            };

            _yuanbaoIconChar = MakeLabel(_yuanbaoIcon, "元", InkWashTheme.FontRole.Display, 15f,
                InkWashTheme.GoldBright, 0f, 0f, 28f, 28f);
            _yuanbaoIconChar.HorizontalAlignment = TextAlignment.Center;
            _yuanbaoIconChar.Enabled = false;

            _yuanbaoLabel = MakeLabel(_yuanbaoDisplay, "元宝", InkWashTheme.FontRole.Body, 11f,
                InkWashTheme.PaperFaded, 0f, 0f, 70f, 14f);

            _yuanbaoValue = MakeLabel(_yuanbaoDisplay, _ingot >= 0 ? _ingot.ToString() : "—", InkWashTheme.FontRole.Number, 18f,
                InkWashTheme.TextDefault, 0f, 0f, 70f, 20f);

            _rechargeButton = new InkButton
            {
                ButtonSize = InkButtonSize.Sm,
                Variant = InkButtonVariant.Ghost,
                Text = "+",
                TooltipText = "充值",
                Width = 24f,
                Height = 24f,
                Parent = _yuanbaoDisplay
            };
            _rechargeButton.Clicked += () => NavigationRequested?.Invoke(InkPageDomIds.NavShop);

            _currencyDivider = new InkDividerVertical
            {
                Size = new Float2(1f, 32f),
                AnchorPreset = AnchorPresets.TopLeft,
                Parent = _topbar
            };

            // ---- 命星余额 ----
            _mingxingDisplay = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Parent = _topbar
            };

            _mingxingIcon = new InkAvatar
            {
                Size = new Float2(28f, 28f),
                Parent = _mingxingDisplay
            };

            _mingxingIconChar = MakeLabel(_mingxingIcon, "星", InkWashTheme.FontRole.Display, 15f,
                InkWashTheme.VermilionBright, 0f, 0f, 28f, 28f);
            _mingxingIconChar.HorizontalAlignment = TextAlignment.Center;
            _mingxingIconChar.Enabled = false;

            _mingxingLabel = MakeLabel(_mingxingDisplay, "命星", InkWashTheme.FontRole.Body, 11f,
                InkWashTheme.PaperFaded, 0f, 0f, 70f, 14f);

            _mingxingValue = MakeLabel(_mingxingDisplay, _fateStar >= 0 ? _fateStar.ToString() : "—", InkWashTheme.FontRole.Number, 18f,
                InkWashTheme.TextDefault, 0f, 0f, 70f, 20f);
        }

        // ================= 3. 左侧卡池选择器 =================

        /// <summary>
        /// 左侧卡池选择器：卡池列表标题 + 四个卡池标签（名称/说明/新手角标/选中指示） + 卡池剩余倒计时。
        /// </summary>
        private void BuildPoolSelector()
        {
            _poolSelector = new Panel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = WithAlpha(InkWashTheme.BaseDefault, 0.88f),
                Parent = this
            };

            _poolSelectorDivider = new InkDividerVertical
            {
                Size = new Float2(1f, 10f),
                AnchorPreset = AnchorPresets.TopLeft,
                Parent = _poolSelector
            };

            _poolSelectorTitle = MakeLabel(_poolSelector, "卡池列表", InkWashTheme.FontRole.Heading, 13f,
                InkWashTheme.PaperFaded, 0f, 0f, 168f, 18f);

            _poolTabs.Clear();
            for (int i = 0; i < Pools.Length; i++)
            {
                GachaPoolData pool = Pools[i];

                GachaTabButton tab = new GachaTabButton
                {
                    Size = new Float2(PoolSelectorWidth, 68f),
                    Parent = _poolSelector
                };

                Panel indicator = new Panel
                {
                    Size = new Float2(3f, 68f),
                    BackgroundColor = Color.Transparent,
                    Parent = tab
                };

                Label nameLabel = MakeLabel(tab, pool.Name, InkWashTheme.FontRole.Heading, 16f,
                    InkWashTheme.PaperDark, 0f, 0f, 150f, 22f);

                Label descLabel = MakeLabel(tab, pool.Desc, InkWashTheme.FontRole.Body, 11f,
                    InkWashTheme.TextTertiary, 0f, 0f, 160f, 16f);

                InkTag badge = null;
                if (!string.IsNullOrEmpty(pool.Badge))
                {
                    badge = new InkTag
                    {
                        TagVariant = InkTagVariant.Brand,
                        Text = pool.Badge,
                        Size = new Float2(38f, 18f),
                        Enabled = false,
                        Parent = tab
                    };
                }

                GachaDot dot = new GachaDot
                {
                    DotColor = Color.Transparent,
                    Parent = tab
                };

                int poolIndex = i;
                tab.Clicked += () => SelectPool(poolIndex);

                _poolTabs.Add(new PoolTabView
                {
                    Root = tab,
                    Indicator = indicator,
                    NameLabel = nameLabel,
                    DescLabel = descLabel,
                    Badge = badge,
                    Dot = dot
                });
            }

            // ---- 卡池剩余倒计时 ----
            _countdownPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Parent = _poolSelector
            };

            _countdownLabel = MakeLabel(_countdownPanel, "卡池剩余", InkWashTheme.FontRole.Body, 11f,
                InkWashTheme.PaperFaded, 0f, 0f, 150f, 16f);

            _countdownNumbers.Clear();
            _countdownUnits.Clear();
            string[] countdownTexts = CountdownNumberTexts();
            string[] countdownUnitTexts = CountdownUnitTexts();
            for (int i = 0; i < CountdownUnits.Length; i++)
            {
                Label num = MakeLabel(_countdownPanel, countdownTexts[i], InkWashTheme.FontRole.Number, 18f,
                    InkWashTheme.GoldBright, 0f, 0f, 30f, 24f);
                num.HorizontalAlignment = TextAlignment.Center;
                _countdownNumbers.Add(num);

                Label unit = MakeLabel(_countdownPanel, countdownUnitTexts[i], InkWashTheme.FontRole.Body, 11f,
                    InkWashTheme.PaperFaded, 0f, 0f, 16f, 18f);
                unit.HorizontalAlignment = TextAlignment.Center;
                _countdownUnits.Add(unit);
            }
        }

        // ================= 4. 中央展示区 =================

        /// <summary>
        /// 中央展示区：保底计数面板（大/小保底） + 角色展示框（名侠·李白 / 青莲剑仙 / 竖排天生我材 / 底部 UP·传说）
        /// + 概率概览面板（四档概率 + 保底说明）。
        /// </summary>
        private void BuildCenterStage()
        {
            _centerStage = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = Color.Transparent,
                Parent = this
            };

            // ---- 保底计数面板 ----
            _pityPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Parent = _centerStage
            };

            _pityTitle = MakeLabel(_pityPanel, "保底计数", InkWashTheme.FontRole.Heading, 13f,
                InkWashTheme.PaperFaded, 0f, 0f, PityPanelWidth - 32f, 18f);

            _greatPityLabel = MakeLabel(_pityPanel, "大保底", InkWashTheme.FontRole.Body, 13f,
                InkWashTheme.VermilionBright, 0f, 0f, 80f, 18f);
            _greatPityCurrent = MakeLabel(_pityPanel, _greatPity.ToString(), InkWashTheme.FontRole.Number, 15f,
                InkWashTheme.VermilionBright, 0f, 0f, 28f, 18f);
            _greatPityCurrent.HorizontalAlignment = TextAlignment.Far;
            _greatPitySeparator = MakeLabel(_pityPanel, "/", InkWashTheme.FontRole.Number, 13f,
                InkWashTheme.TextTertiary, 0f, 0f, 10f, 18f);
            _greatPitySeparator.HorizontalAlignment = TextAlignment.Center;
            _greatPityTotal = MakeLabel(_pityPanel, _pityTotal.ToString(), InkWashTheme.FontRole.Number, 13f,
                InkWashTheme.TextTertiary, 0f, 0f, 28f, 18f);
            _greatPityTotal.HorizontalAlignment = TextAlignment.Far;

            _greatPityBar = new InkBar
            {
                FillVariant = InkBarFillVariant.Vermilion,
                Height = 4f,
                Parent = _pityPanel
            };

            _smallPityLabel = MakeLabel(_pityPanel, "小保底", InkWashTheme.FontRole.Body, 13f,
                InkWashTheme.GoldBright, 0f, 0f, 80f, 18f);
            _smallPityCurrent = MakeLabel(_pityPanel, _smallPity.ToString(), InkWashTheme.FontRole.Number, 15f,
                InkWashTheme.GoldBright, 0f, 0f, 28f, 18f);
            _smallPityCurrent.HorizontalAlignment = TextAlignment.Far;
            _smallPitySeparator = MakeLabel(_pityPanel, "/", InkWashTheme.FontRole.Number, 13f,
                InkWashTheme.TextTertiary, 0f, 0f, 10f, 18f);
            _smallPitySeparator.HorizontalAlignment = TextAlignment.Center;
            _smallPityTotal = MakeLabel(_pityPanel, _pityTotal.ToString(), InkWashTheme.FontRole.Number, 13f,
                InkWashTheme.TextTertiary, 0f, 0f, 28f, 18f);
            _smallPityTotal.HorizontalAlignment = TextAlignment.Far;

            _smallPityBar = new InkBar
            {
                FillVariant = InkBarFillVariant.Gold,
                Height = 4f,
                Parent = _pityPanel
            };

            // ---- 角色展示框 ----
            _showcase = new ContainerControl
            {
                Size = new Float2(ShowcaseWidth, ShowcaseHeight),
                BackgroundColor = Color.Transparent,
                Parent = _centerStage
            };

            _showcaseInner = new Panel
            {
                Size = new Float2(ShowcaseWidth, ShowcaseHeight),
                BackgroundColor = WithAlpha(InkWashTheme.BaseDefault, 0.92f),
                Parent = _showcase
            };

            _showcaseSilhouette = new InkSplash
            {
                Variant = InkSplashVariant.Normal,
                Opacity = 0.35f,
                Size = new Float2(180f, 240f),
                Parent = _showcaseInner
            };

            _characterNamePrefix = MakeLabel(_showcaseInner, "名侠", InkWashTheme.FontRole.Heading, 18f,
                InkWashTheme.PaperFaded, 0f, 0f, 40f, 30f);
            _characterNamePrefix.HorizontalAlignment = TextAlignment.Far;

            _characterNameDot = MakeLabel(_showcaseInner, "·", InkWashTheme.FontRole.Heading, 18f,
                InkWashTheme.GoldDeep, 0f, 0f, 16f, 30f);
            _characterNameDot.HorizontalAlignment = TextAlignment.Center;

            _characterNameMain = MakeLabel(_showcaseInner, "李白", InkWashTheme.FontRole.Display, 48f,
                InkWashTheme.GoldBright, 0f, 0f, 110f, 46f);

            _characterTitleText = MakeLabel(_showcaseInner, "青莲剑仙", InkWashTheme.FontRole.Display, 22f,
                InkWashTheme.PaperAged, 0f, 0f, ShowcaseWidth, 28f);
            _characterTitleText.HorizontalAlignment = TextAlignment.Center;

            _showcaseVerticalText = new InkVerticalTitle
            {
                Text = "天生我材",
                FontSize = 13f,
                Size = new Float2(18f, 90f),
                Parent = _showcaseInner
            };

            _showcaseFooter = new Panel
            {
                Size = new Float2(ShowcaseWidth, ShowcaseFooterHeight),
                BackgroundColor = WithAlpha(InkWashTheme.BaseDefault, 0.85f),
                Parent = _showcase
            };

            _showcaseFooterName = MakeLabel(_showcaseFooter, Pools[0].ShowcaseName, InkWashTheme.FontRole.Heading, 22f,
                InkWashTheme.TextDefault, 0f, 0f, 220f, 28f);

            _showcaseFooterDesc = MakeLabel(_showcaseFooter, Pools[0].ShowcaseDesc, InkWashTheme.FontRole.Body, 13f,
                InkWashTheme.PaperFaded, 0f, 0f, 240f, 18f);

            _showcaseUpBadge = new InkTag
            {
                TagVariant = InkTagVariant.Brand,
                Text = "UP",
                Size = new Float2(56f, 22f),
                Parent = _showcaseFooter
            };

            _showcaseQualityBadge = new InkTag
            {
                TagVariant = InkTagVariant.Vermilion,
                Text = "传说",
                Size = new Float2(62f, 22f),
                Parent = _showcaseFooter
            };

            // ---- 概率概览面板 ----
            _overviewPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Parent = _centerStage
            };

            _overviewTitle = MakeLabel(_overviewPanel, "概率概览（未接入）", InkWashTheme.FontRole.Heading, 13f,
                InkWashTheme.PaperFaded, 0f, 0f, OverviewPanelWidth - 32f, 18f);

            _overviewRows.Clear();
            for (int i = 0; i < RateRows.Length; i++)
            {
                GachaRateRow row = RateRows[i];

                GachaDot dot = new GachaDot
                {
                    DotColor = row.Color,
                    Parent = _overviewPanel
                };

                Label nameLabel = MakeLabel(_overviewPanel, row.Name, InkWashTheme.FontRole.Body, 13f,
                    InkWashTheme.PaperAged, 0f, 0f, 120f, 18f);

                Label rateLabel = MakeLabel(_overviewPanel, row.Rate, InkWashTheme.FontRole.Number, 13f,
                    InkWashTheme.TextBrand, 0f, 0f, 64f, 18f);
                rateLabel.HorizontalAlignment = TextAlignment.Far;

                _overviewRows.Add(new OverviewRowView
                {
                    Dot = dot,
                    NameLabel = nameLabel,
                    RateLabel = rateLabel
                });
            }

            _overviewDivider = new InkDivider
            {
                Height = 1f,
                Parent = _overviewPanel
            };

            _guaranteeLabels.Clear();
            for (int i = 0; i < GuaranteeTexts.Length; i++)
            {
                Label guarantee = MakeLabel(_overviewPanel, GuaranteeTexts[i], InkWashTheme.FontRole.Body, 12f,
                    InkWashTheme.JadeBright, 0f, 0f, OverviewPanelWidth - 32f, 18f);
                _guaranteeLabels.Add(guarantee);
            }
        }

        // ================= 5. 底部抽卡区 =================

        /// <summary>
        /// 底部抽卡区：祈愿一次（元宝 × 160）+ 祈愿十次（元宝 × 1600，附"节省160元宝"角标）
        /// + 抽卡进度（已祈愿次数 / 距离保底还有 / 进度条）。
        /// </summary>
        private void BuildDrawArea()
        {
            _drawArea = new Panel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = WithAlpha(InkWashTheme.BaseDefault, 0.9f),
                Parent = this
            };

            _singleDrawButton = new InkButton
            {
                ButtonSize = InkButtonSize.Lg,
                Variant = InkButtonVariant.Ghost,
                Text = "祈愿一次",
                Width = DrawButtonWidth,
                Height = DrawButtonHeight,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 15f),
                Parent = _drawArea
            };
            _singleDrawButton.Clicked += () => OnDrawRequested(1);

            // 未绑定价格前不摆设计稿的 160（BindDrawCost 才会写入真实价格）
            _singleDrawCost = MakeLabel(_drawArea, "元宝 × —", InkWashTheme.FontRole.Body, 11f,
                InkWashTheme.GoldBright, 0f, 0f, 140f, 16f);
            _singleDrawCost.Enabled = false;

            _multiDrawButton = new InkButton
            {
                ButtonSize = InkButtonSize.Lg,
                Variant = InkButtonVariant.Primary,
                Text = "祈愿十次",
                Width = DrawButtonWidth,
                Height = DrawButtonHeight,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 15f),
                Parent = _drawArea
            };
            _multiDrawButton.Clicked += () => OnDrawRequested(10);

            // 未绑定价格前不摆设计稿的 1600
            _multiDrawCost = MakeLabel(_drawArea, "元宝 × —", InkWashTheme.FontRole.Body, 11f,
                InkWashTheme.PaperBright, 0f, 0f, 160f, 16f);
            _multiDrawCost.Enabled = false;

            // 「节省 160 元宝」是设计稿的定价文案；祈愿未接服务端、价格也没有数据源，
            // 直接不显示，免得在"未接入"的页面上摆一个具体折扣。
            _multiDrawSaveBadge = new InkTag
            {
                TagVariant = InkTagVariant.Vermilion,
                Text = "节省160元宝",
                Size = new Float2(86f, 20f),
                Enabled = false,
                Visible = false,
                Parent = _drawArea
            };

            _drawCountLabel = MakeLabel(_drawArea, string.Empty, InkWashTheme.FontRole.Body, 12f,
                InkWashTheme.PaperFaded, 0f, 0f, 160f, 18f);

            _drawPityLabel = MakeLabel(_drawArea, string.Empty, InkWashTheme.FontRole.Body, 12f,
                InkWashTheme.PaperFaded, 0f, 0f, 260f, 18f);

            _drawProgressBar = new InkBar
            {
                FillVariant = InkBarFillVariant.Gold,
                Height = 6f,
                Parent = _drawArea
            };
        }

        // ================= 6. 右下角链接 =================

        /// <summary>
        /// 右下角链接：活动（跳转活动页）/ 祈愿记录 / 概率公示 / 详情规则。
        /// 活动对应设计稿 data-dom-id="nav-activities"，其余三项在设计稿中无目标页，仅提供点击反馈。
        /// </summary>
        private void BuildGachaLinks()
        {
            _linkButtons.Clear();

            CreateLinkButton("活动", 0, () => NavigationRequested?.Invoke(InkPageDomIds.NavActivities));
            CreateLinkButton("祈愿记录", 1, () => OnPlaceholderLinkClicked("祈愿记录"));
            CreateLinkButton("概率公示", 2, () => OnPlaceholderLinkClicked("概率公示"));
            CreateLinkButton("详情规则", 3, () => OnPlaceholderLinkClicked("详情规则"));
        }

        /// <summary>创建单个右下角链接按钮（父级为本页，绘制在抽卡区之上）。</summary>
        private void CreateLinkButton(string text, int index, Action onClick)
        {
            InkButton button = new InkButton
            {
                ButtonSize = InkButtonSize.Sm,
                Variant = InkButtonVariant.Ghost,
                Text = text,
                Width = 140f,
                Height = 32f,
                HorizontalAlignment = TextAlignment.Near,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 13f),
                Parent = this
            };
            button.Clicked += onClick;
            button.Tag = index;
            _linkButtons.Add(button);
        }

        /// <summary>设计稿未给出目标页的链接点击反馈（保持可交互，不触发未知路由）。</summary>
        private void OnPlaceholderLinkClicked(string linkName)
        {
            FlaxEngine.Debug.Log($"[MenuGachaPage] 链接点击: {linkName}");
        }

        // ================= 7. 左下角 UP 名侠预览 =================

        /// <summary>
        /// 左下角 UP 名侠预览：李白·青莲剑仙 / 杜甫·诗圣 / 王维·诗佛，各带 UP 角标。
        /// </summary>
        private void BuildUpPreview()
        {
            _upPreviewPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Parent = this
            };

            _upPreviewTitle = MakeLabel(_upPreviewPanel, "UP名侠", InkWashTheme.FontRole.Heading, 13f,
                InkWashTheme.PaperFaded, 0f, 0f, UpPreviewWidth - 32f, 18f);

            _upPreviewItems.Clear();
            for (int i = 0; i < UpEntries.Length; i++)
            {
                GachaUpEntry entry = UpEntries[i];

                ContainerControl item = new ContainerControl
                {
                    Size = new Float2(UpPreviewWidth - 24f, 52f),
                    BackgroundColor = WithAlpha(InkWashTheme.Abyss, 0.35f),
                    Parent = _upPreviewPanel
                };

                InkAvatar avatar = new InkAvatar
                {
                    Size = new Float2(36f, 36f),
                    Location = new Float2(8f, 8f),
                    Parent = item
                };

                Label charLabel = MakeLabel(avatar, entry.Char, InkWashTheme.FontRole.Display, 18f,
                    entry.Color, 0f, 0f, 36f, 36f);
                charLabel.HorizontalAlignment = TextAlignment.Center;
                charLabel.Enabled = false;

                MakeLabel(item, entry.Name, InkWashTheme.FontRole.Heading, 14f,
                    InkWashTheme.TextDefault, 52f, 8f, 100f, 18f);

                MakeLabel(item, entry.Title, InkWashTheme.FontRole.Body, 11f,
                    InkWashTheme.TextTertiary, 52f, 28f, 100f, 16f);

                InkTag rateTag = new InkTag
                {
                    TagVariant = i == 0 ? InkTagVariant.Vermilion : InkTagVariant.Brand,
                    Text = "UP",
                    Size = new Float2(34f, 20f),
                    Enabled = false,
                    Parent = item
                };

                _upPreviewItems.Add(new UpPreviewItemView
                {
                    Root = item,
                    RateTag = rateTag
                });
            }
        }

        // ================= 布局 =================

        /// <summary>按当前屏幕尺寸重排全部控件（对齐设计稿绝对定位布局）。</summary>
        private void ApplyLayout()
        {
            float sw = _screenSize.X;
            float sh = _screenSize.Y;
            float stageLeft = PoolSelectorWidth;
            float stageWidth = Mathf.Max(320f, sw - PoolSelectorWidth);
            float stageHeight = Mathf.Max(320f, sh - TopbarHeight);

            // ---- 背景（墨底/暗角由外壳提供，这里只摆本页专属晕染） ----
            if (_splashGold != null) _splashGold.Location = new Float2(sw * 0.15f, sh * 0.10f);
            if (_splashCenter != null) _splashCenter.Location = new Float2(sw * 0.5f - 300f, sh * 0.30f);
            if (_splashVermilion != null) _splashVermilion.Location = new Float2(sw * 0.80f, sh * 0.62f);

            // ---- 顶部栏 ----
            if (_topbar != null)
            {
                _topbar.Location = Float2.Zero;
                _topbar.Size = new Float2(sw, TopbarHeight);
            }

            if (_topbarDivider != null)
            {
                _topbarDivider.Location = new Float2(0f, TopbarHeight - 1f);
                _topbarDivider.Size = new Float2(sw, 1f);
            }

            if (_backButton != null) _backButton.Location = new Float2(ContentPadding, (TopbarHeight - 36f) * 0.5f);
            if (_titleLabel != null) _titleLabel.Location = new Float2(ContentPadding + 52f, 4f);
            if (_titleEnLabel != null) _titleEnLabel.Location = new Float2(ContentPadding + 54f, 38f);
            if (_titleDivider != null) _titleDivider.Location = new Float2(ContentPadding + 296f, (TopbarHeight - 28f) * 0.5f);
            if (_poolNameLabel != null) _poolNameLabel.Location = new Float2(ContentPadding + 312f, (TopbarHeight - 20f) * 0.5f);

            float yuanbaoWidth = 172f;
            float mingxingWidth = 150f;
            float currencyGap = 16f;
            float rightEdge = sw - ContentPadding;
            float mingxingX = rightEdge - mingxingWidth;
            float dividerX = mingxingX - currencyGap;
            float yuanbaoX = dividerX - currencyGap - yuanbaoWidth;

            LayoutCurrencyDisplay(_yuanbaoDisplay, yuanbaoX, 10f, yuanbaoWidth, 40f,
                _yuanbaoIcon, _yuanbaoIconChar, _yuanbaoLabel, _yuanbaoValue, _rechargeButton);
            if (_currencyDivider != null) _currencyDivider.Location = new Float2(dividerX, (TopbarHeight - 32f) * 0.5f);
            LayoutCurrencyDisplay(_mingxingDisplay, mingxingX, 10f, mingxingWidth, 40f,
                _mingxingIcon, _mingxingIconChar, _mingxingLabel, _mingxingValue, null);

            // ---- 左侧卡池选择器 ----
            float countdownHeight = 118f;
            float countdownBottomMargin = 24f;

            if (_poolSelector != null)
            {
                _poolSelector.Location = new Float2(0f, TopbarHeight);
                _poolSelector.Size = new Float2(PoolSelectorWidth, stageHeight);
            }

            if (_poolSelectorDivider != null)
            {
                _poolSelectorDivider.Location = new Float2(PoolSelectorWidth - 1f, 0f);
                _poolSelectorDivider.Size = new Float2(1f, stageHeight);
            }

            if (_poolSelectorTitle != null) _poolSelectorTitle.Location = new Float2(16f, 24f);

            float tabTop = 58f;
            for (int i = 0; i < _poolTabs.Count; i++)
            {
                PoolTabView view = _poolTabs[i];
                if (view?.Root == null) continue;

                view.Root.Location = new Float2(0f, tabTop + i * 76f);
                if (view.Indicator != null) view.Indicator.Location = Float2.Zero;
                if (view.NameLabel != null) view.NameLabel.Location = new Float2(16f, 12f);
                if (view.DescLabel != null) view.DescLabel.Location = new Float2(16f, 34f);
                if (view.Badge != null) view.Badge.Location = new Float2(PoolSelectorWidth - 16f - 38f, 14f);
                if (view.Dot != null) view.Dot.Location = new Float2(PoolSelectorWidth - 20f, 31f);
            }

            if (_countdownPanel != null)
            {
                _countdownPanel.Location = new Float2(12f, stageHeight - countdownBottomMargin - countdownHeight);
                _countdownPanel.Size = new Float2(PoolSelectorWidth - 24f, countdownHeight);
            }

            if (_countdownLabel != null) _countdownLabel.Location = new Float2(12f, 10f);
            for (int i = 0; i < _countdownNumbers.Count; i++)
            {
                float numX = 14f + i * 46f;
                if (_countdownNumbers[i] != null) _countdownNumbers[i].Location = new Float2(numX, 40f);
                if (i < _countdownUnits.Count && _countdownUnits[i] != null)
                {
                    _countdownUnits[i].Location = new Float2(numX + 32f, 44f);
                }
            }

            // ---- 中央展示区 ----
            if (_centerStage != null)
            {
                _centerStage.Location = new Float2(stageLeft, TopbarHeight);
                _centerStage.Size = new Float2(stageWidth, stageHeight);
            }

            float pityHeight = 148f;
            if (_pityPanel != null)
            {
                _pityPanel.Location = new Float2(ContentPadding, ContentPadding);
                _pityPanel.Size = new Float2(PityPanelWidth, pityHeight);
            }

            if (_pityTitle != null) _pityTitle.Location = new Float2(16f, 14f);
            LayoutPityRow(_greatPityLabel, _greatPityCurrent, _greatPitySeparator, _greatPityTotal,
                _greatPityBar, 44f, PityPanelWidth);
            LayoutPityRow(_smallPityLabel, _smallPityCurrent, _smallPitySeparator, _smallPityTotal,
                _smallPityBar, 96f, PityPanelWidth);

            if (_showcase != null)
            {
                _showcase.Location = new Float2(
                    Mathf.Max(ContentPadding, (stageWidth - ShowcaseWidth) * 0.5f),
                    Mathf.Max(ContentPadding + pityHeight + 24f, (stageHeight - ShowcaseHeight) * 0.5f));
            }

            if (_showcaseInner != null) _showcaseInner.Size = new Float2(ShowcaseWidth, ShowcaseHeight);
            if (_showcaseSilhouette != null) _showcaseSilhouette.Location = new Float2((ShowcaseWidth - 180f) * 0.5f, 70f);

            float nameBlockWidth = 40f + 16f + 110f;
            float nameBlockLeft = (ShowcaseWidth - nameBlockWidth) * 0.5f;
            if (_characterNamePrefix != null) _characterNamePrefix.Location = new Float2(nameBlockLeft, 320f);
            if (_characterNameDot != null) _characterNameDot.Location = new Float2(nameBlockLeft + 40f, 320f);
            if (_characterNameMain != null) _characterNameMain.Location = new Float2(nameBlockLeft + 56f, 314f);
            if (_characterTitleText != null) _characterTitleText.Location = new Float2(0f, 372f);
            if (_showcaseVerticalText != null) _showcaseVerticalText.Location = new Float2(ShowcaseWidth - 50f, ContentPadding);

            if (_showcaseFooter != null)
            {
                _showcaseFooter.Location = new Float2(0f, ShowcaseHeight - ShowcaseFooterHeight);
            }

            if (_showcaseFooterName != null) _showcaseFooterName.Location = new Float2(20f, 14f);
            if (_showcaseFooterDesc != null) _showcaseFooterDesc.Location = new Float2(20f, 46f);
            if (_showcaseUpBadge != null) _showcaseUpBadge.Location = new Float2(ShowcaseWidth - 20f - 56f, 14f);
            if (_showcaseQualityBadge != null) _showcaseQualityBadge.Location = new Float2(ShowcaseWidth - 20f - 62f, 42f);

            float overviewHeight = 232f;
            if (_overviewPanel != null)
            {
                _overviewPanel.Location = new Float2(Mathf.Max(ContentPadding, stageWidth - ContentPadding - OverviewPanelWidth), ContentPadding);
                _overviewPanel.Size = new Float2(OverviewPanelWidth, overviewHeight);
            }

            if (_overviewTitle != null) _overviewTitle.Location = new Float2(16f, 14f);
            for (int i = 0; i < _overviewRows.Count; i++)
            {
                OverviewRowView row = _overviewRows[i];
                float rowY = 44f + i * 26f;
                if (row.Dot != null) row.Dot.Location = new Float2(16f, rowY + 5f);
                if (row.NameLabel != null) row.NameLabel.Location = new Float2(32f, rowY);
                if (row.RateLabel != null) row.RateLabel.Location = new Float2(OverviewPanelWidth - 16f - 64f, rowY);
            }

            if (_overviewDivider != null)
            {
                _overviewDivider.Location = new Float2(16f, 44f + RateRows.Length * 26f + 6f);
                _overviewDivider.Size = new Float2(OverviewPanelWidth - 32f, 1f);
            }

            for (int i = 0; i < _guaranteeLabels.Count; i++)
            {
                if (_guaranteeLabels[i] != null)
                {
                    _guaranteeLabels[i].Location = new Float2(16f, 44f + RateRows.Length * 26f + 22f + i * 24f);
                }
            }

            // ---- 底部抽卡区 ----
            if (_drawArea != null)
            {
                _drawArea.Location = new Float2(stageLeft, sh - DrawAreaHeight);
                _drawArea.Size = new Float2(stageWidth, DrawAreaHeight);
            }

            float progressWidth = 280f;
            float rowWidth = DrawButtonWidth * 2f + 16f + 32f + progressWidth;
            float rowLeft = Mathf.Max(ContentPadding, (stageWidth - rowWidth) * 0.5f);
            float buttonTop = (DrawAreaHeight - DrawButtonHeight) * 0.5f - 4f;

            if (_singleDrawButton != null) _singleDrawButton.Location = new Float2(rowLeft, buttonTop);
            // 价格放到按钮下方：按钮内文字是垂直居中的，落在 +28 会与"祈愿一次"重叠。
            if (_singleDrawCost != null)
                _singleDrawCost.Location = new Float2(rowLeft + (DrawButtonWidth - 140f) * 0.5f, buttonTop + DrawButtonHeight + 2f);

            float multiX = rowLeft + DrawButtonWidth + 16f;
            if (_multiDrawButton != null) _multiDrawButton.Location = new Float2(multiX, buttonTop);
            if (_multiDrawCost != null)
                _multiDrawCost.Location = new Float2(multiX + (DrawButtonWidth - 160f) * 0.5f, buttonTop + DrawButtonHeight + 2f);
            // 角标抬到按钮上方：buttonTop-12 会压住按钮上沿。
            if (_multiDrawSaveBadge != null) _multiDrawSaveBadge.Location = new Float2(multiX + DrawButtonWidth - 86f, buttonTop - 24f);

            float progressX = rowLeft + DrawButtonWidth * 2f + 16f + 32f;
            if (_drawCountLabel != null) _drawCountLabel.Location = new Float2(progressX, buttonTop + 2f);
            if (_drawPityLabel != null) _drawPityLabel.Location = new Float2(progressX, buttonTop + 24f);
            if (_drawProgressBar != null)
            {
                _drawProgressBar.Location = new Float2(progressX, buttonTop + 52f);
                _drawProgressBar.Size = new Float2(progressWidth, 6f);
            }

            // ---- 右下角链接 ----
            float linkWidth = 140f;
            float linkHeight = 32f;
            float linkGap = 8f;
            float linkBlockHeight = _linkButtons.Count * linkHeight + Mathf.Max(0, _linkButtons.Count - 1) * linkGap;
            float linkX = sw - ContentPadding - linkWidth;
            float linkTop = sh - 24f - linkBlockHeight;
            for (int i = 0; i < _linkButtons.Count; i++)
            {
                if (_linkButtons[i] != null)
                {
                    _linkButtons[i].Location = new Float2(linkX, linkTop + i * (linkHeight + linkGap));
                }
            }

            // ---- 左下角 UP 名侠预览（置于卡池栏右侧，避免被卡池栏遮挡）----
            float upPanelHeight = 230f;
            if (_upPreviewPanel != null)
            {
                _upPreviewPanel.Location = new Float2(stageLeft + ContentPadding, sh - 24f - upPanelHeight);
                _upPreviewPanel.Size = new Float2(UpPreviewWidth, upPanelHeight);
            }

            if (_upPreviewTitle != null) _upPreviewTitle.Location = new Float2(12f, 12f);
            for (int i = 0; i < _upPreviewItems.Count; i++)
            {
                UpPreviewItemView item = _upPreviewItems[i];
                if (item?.Root == null) continue;

                item.Root.Location = new Float2(12f, 46f + i * 60f);
                if (item.RateTag != null) item.RateTag.Location = new Float2(UpPreviewWidth - 24f - 12f - 34f, 16f);
            }
        }

        /// <summary>布局顶部栏货币显示块（图标 + 标签 + 数值 + 可选充值按钮）。</summary>
        private static void LayoutCurrencyDisplay(InkPanel panel, float x, float y, float width, float height,
            InkAvatar icon, Label iconChar, Label label, Label value, InkButton addButton)
        {
            if (panel == null) return;

            panel.Location = new Float2(x, y);
            panel.Size = new Float2(width, height);

            if (icon != null) icon.Location = new Float2(6f, 6f);
            if (iconChar != null) iconChar.Location = Float2.Zero;
            if (label != null) label.Location = new Float2(42f, 4f);
            if (value != null) value.Location = new Float2(42f, 18f);
            if (addButton != null) addButton.Location = new Float2(width - 32f, (height - 24f) * 0.5f);
        }

        /// <summary>布局保底计数面板中的一行（标签 + 当前值/分隔/总量 + 进度条）。</summary>
        private static void LayoutPityRow(Label label, Label current, Label separator, Label total, InkBar bar,
            float rowY, float panelWidth)
        {
            if (label != null) label.Location = new Float2(16f, rowY);
            if (current != null) current.Location = new Float2(panelWidth - 16f - 76f, rowY);
            if (separator != null) separator.Location = new Float2(panelWidth - 16f - 46f, rowY);
            if (total != null) total.Location = new Float2(panelWidth - 16f - 36f, rowY);
            if (bar != null)
            {
                bar.Location = new Float2(16f, rowY + 26f);
                bar.Size = new Float2(panelWidth - 32f, 4f);
            }
        }

        // ================= 数据刷新 =================

        /// <summary>刷新全部动态数据：卡池选中态、余额、保底计数、抽卡进度。</summary>
        private void RefreshAllData()
        {
            ApplyPoolSelection();
            RefreshPity();
            RefreshDrawProgress();
        }

        /// <summary>切换卡池并刷新联动文案与选中态。</summary>
        private void SelectPool(int poolIndex)
        {
            if (poolIndex < 0 || poolIndex >= Pools.Length) return;

            _activePoolIndex = poolIndex;
            ApplyPoolSelection();
        }

        /// <summary>应用当前卡池的选中视觉与联动文案（顶部栏卡池名、展示框名称/说明）。</summary>
        private void ApplyPoolSelection()
        {
            for (int i = 0; i < _poolTabs.Count; i++)
            {
                PoolTabView view = _poolTabs[i];
                if (view == null) continue;

                bool isActive = i == _activePoolIndex;
                Color activeBackground = WithAlpha(InkWashTheme.GoldPrimary, 0.10f);

                if (view.Root != null)
                {
                    view.Root.NormalColor = isActive ? activeBackground : Color.Transparent;
                    view.Root.HoverColor = WithAlpha(InkWashTheme.GoldPrimary, isActive ? 0.14f : 0.05f);
                    view.Root.BackgroundColor = view.Root.NormalColor;
                }

                if (view.Indicator != null)
                {
                    view.Indicator.BackgroundColor = isActive ? InkWashTheme.GoldPrimary : Color.Transparent;
                }

                if (view.NameLabel != null)
                {
                    view.NameLabel.TextColor = isActive ? InkWashTheme.GoldBright : InkWashTheme.PaperDark;
                }

                if (view.DescLabel != null)
                {
                    view.DescLabel.TextColor = isActive ? InkWashTheme.PaperFaded : InkWashTheme.TextTertiary;
                }

                if (view.Dot != null)
                {
                    view.Dot.DotColor = isActive ? InkWashTheme.GoldBright : Color.Transparent;
                }
            }

            GachaPoolData pool = Pools[_activePoolIndex];
            if (_poolNameLabel != null) _poolNameLabel.Text = pool.TopbarName;
            if (_showcaseFooterName != null) _showcaseFooterName.Text = pool.ShowcaseName;
            if (_showcaseFooterDesc != null) _showcaseFooterDesc.Text = pool.ShowcaseDesc;
        }

        /// <summary>刷新大/小保底计数文本与进度条。</summary>
        private void RefreshPity()
        {
            // 未绑定服务端数据前，别把设计稿的「47 / 90」摆成玩家自己的保底进度
            if (!_hasServerData)
            {
                if (_greatPityCurrent != null) _greatPityCurrent.Text = "—";
                if (_greatPityTotal != null) _greatPityTotal.Text = "—";
                if (_greatPityBar != null) _greatPityBar.Value = 0f;
                if (_smallPityCurrent != null) _smallPityCurrent.Text = "—";
                if (_smallPityTotal != null) _smallPityTotal.Text = "—";
                if (_smallPityBar != null) _smallPityBar.Value = 0f;
                return;
            }

            if (_greatPityCurrent != null) _greatPityCurrent.Text = _greatPity.ToString();
            if (_greatPityTotal != null) _greatPityTotal.Text = _pityTotal.ToString();
            if (_greatPityBar != null) _greatPityBar.Value = _pityTotal > 0 ? (float)_greatPity / _pityTotal : 0f;

            if (_smallPityCurrent != null) _smallPityCurrent.Text = _smallPity.ToString();
            if (_smallPityTotal != null) _smallPityTotal.Text = _pityTotal.ToString();
            if (_smallPityBar != null) _smallPityBar.Value = _pityTotal > 0 ? (float)_smallPity / _pityTotal : 0f;
        }

        /// <summary>
        /// 刷新抽卡进度文本与进度条（已祈愿次数 / 距离保底还有）。
        /// 剩余次数取自 <c>_pityRemaining</c>（初值 = 设计稿示例数据，可被 <see cref="BindPity"/> 覆盖）。
        /// </summary>
        private void RefreshDrawProgress()
        {
            if (!_hasServerData)
            {
                if (_drawCountLabel != null) _drawCountLabel.Text = "祈愿记录尚未接入";
                if (_drawPityLabel != null) _drawPityLabel.Text = "保底次数尚未接入";
                if (_drawProgressBar != null) _drawProgressBar.Value = 0f;
                return;
            }

            if (_drawCountLabel != null) _drawCountLabel.Text = $"已祈愿 {_drawCount} 次";
            if (_drawPityLabel != null) _drawPityLabel.Text = $"距离保底还有 {Mathf.Max(0, _pityRemaining)} 次";
            if (_drawProgressBar != null) _drawProgressBar.Value = _pityTotal > 0 ? (float)_smallPity / _pityTotal : 0f;
        }

        /// <summary>刷新卡池剩余倒计时数值（天 / 时 / 分）。</summary>
        private void RefreshCountdown()
        {
            string[] texts = CountdownNumberTexts();
            string[] unitTexts = CountdownUnitTexts();
            for (int i = 0; i < _countdownNumbers.Count && i < texts.Length; i++)
            {
                if (_countdownNumbers[i] != null)
                    _countdownNumbers[i].Text = texts[i];
                if (i < _countdownUnits.Count && _countdownUnits[i] != null)
                    _countdownUnits[i].Text = unitTexts[i];
            }
        }

        // ================= 数据绑定 API =================

        /// <summary>
        /// 绑定账号余额：元宝与命星（顶栏右上角）。
        /// 不调用时保持设计稿初值（元宝 328 / 命星 5）。
        /// </summary>
        /// <param name="ingot">元宝余额</param>
        /// <param name="fateStar">命星余额</param>
        public void BindAccount(int ingot, int fateStar)
        {
            _ingot = ingot;
            _fateStar = fateStar;
            if (_yuanbaoValue != null) _yuanbaoValue.Text = _ingot >= 0 ? _ingot.ToString() : "—";
            if (_mingxingValue != null) _mingxingValue.Text = _fateStar >= 0 ? _fateStar.ToString() : "—";
        }

        /// <summary>
        /// 绑定祈愿数据：已祈愿次数、大小保底计数与剩余保底次数，并立即刷新保底面板与底部进度。
        /// </summary>
        /// <param name="drawnCount">已祈愿次数</param>
        /// <param name="smallPity">小保底累计次数</param>
        /// <param name="greatPity">大保底累计次数</param>
        /// <param name="pityRemaining">距离保底剩余次数；传负数时按 <paramref name="pityTotal"/> - <paramref name="smallPity"/> 计算</param>
        /// <param name="pityTotal">保底总次数（默认沿用设计稿 90）</param>
        public void BindPity(int drawnCount, int smallPity, int greatPity, int pityRemaining = -1, int pityTotal = 0)
        {
            _drawCount = drawnCount;
            _smallPity = smallPity;
            _greatPity = greatPity;
            _hasServerData = true;
            if (pityTotal > 0)
                _pityTotal = pityTotal;
            _pityRemaining = pityRemaining >= 0 ? pityRemaining : Mathf.Max(0, _pityTotal - _smallPity);
            RefreshPity();
            RefreshDrawProgress();
        }

        /// <summary>
        /// 绑定卡池剩余倒计时（天 / 时 / 分）。
        /// 不调用时保持设计稿初值（12 天 07 时 23 分）。
        /// </summary>
        /// <param name="days">剩余天数</param>
        /// <param name="hours">剩余小时数</param>
        /// <param name="minutes">剩余分钟数</param>
        public void BindPoolCountdown(int days, int hours, int minutes)
        {
            _countdownDay = days;
            _countdownHour = hours;
            _countdownMinute = minutes;
            RefreshCountdown();
        }

        /// <summary>
        /// 绑定祈愿价格（元宝）：单抽与十连。
        /// 不调用时保持设计稿初值（单抽 160 / 十连 1600）。
        /// </summary>
        /// <param name="singleCost">单抽价格（元宝）</param>
        /// <param name="tenCost">十连价格（元宝）</param>
        public void BindDrawCost(int singleCost, int tenCost)
        {
            _singleDrawPrice = singleCost;
            _tenDrawPrice = tenCost;
            if (_singleDrawCost != null) _singleDrawCost.Text = $"元宝 × {_singleDrawPrice}";
            if (_multiDrawCost != null) _multiDrawCost.Text = $"元宝 × {_tenDrawPrice}";
        }

        // ================= 交互 =================

        /// <summary>返回按钮：回到战斗 HUD。</summary>
        private void OnBackClicked()
        {
            try
            {
                NavigationRequested?.Invoke(InkPageDomIds.BackOneLevel);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[MenuGachaPage] NavigationRequested(back-hud) 触发失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 祈愿按钮：祈愿没有服务端链路（没有卡池、没有概率、没有发放），
        /// 点了只提示不可用——绝不能像以前那样在本地自增次数与保底，那会让玩家以为真抽了。
        /// </summary>
        private void OnDrawRequested(int count)
        {
            HundunWorld.Game.UI.UIHelper.ShowToast("祈愿系统尚未接入服务端，暂不可祈愿", HundunWorld.Game.UI.ToastType.Info);
        }

        // ================= 布局刷新 =================

        /// <inheritdoc />
        public void RefreshLayout()
        {
            _screenSize = new Float2(Width, Height);
            ApplyLayout();
        }

        // ================= 私有辅助类型 =================

        /// <summary>创建主题字体标签的辅助方法（统一字号/字色/垂直居中）。</summary>
        private static Label MakeLabel(ContainerControl parent, string text, InkWashTheme.FontRole role, float fontSize,
            Color color, float x, float y, float width, float height)
        {
            return new Label
            {
                Text = text,
                Font = InkRenderHelper.GetFontRef(role, fontSize),
                TextColor = color,
                Location = new Float2(x, y),
                Size = new Float2(width, height),
                VerticalAlignment = TextAlignment.Center,
                Parent = parent
            };
        }

        /// <summary>基于既有主题 token 派生指定透明度的颜色（避免硬编码色值）。</summary>
        private static Color WithAlpha(Color color, float alpha)
        {
            return new Color(color.R, color.G, color.B, alpha);
        }

        /// <summary>卡池标签视图引用（选中态样式联动用）。</summary>
        private sealed class PoolTabView
        {
            public GachaTabButton Root;
            public Panel Indicator;
            public Label NameLabel;
            public Label DescLabel;
            public InkTag Badge;
            public GachaDot Dot;
        }

        /// <summary>概率概览行视图引用。</summary>
        private sealed class OverviewRowView
        {
            public GachaDot Dot;
            public Label NameLabel;
            public Label RateLabel;
        }

        /// <summary>UP 名侠预览条目视图引用。</summary>
        private sealed class UpPreviewItemView
        {
            public ContainerControl Root;
            public InkTag RateTag;
        }

        /// <summary>可点击的卡池标签容器（含悬停底色，对应 .pool-tab:hover）。</summary>
        private sealed class GachaTabButton : ContainerControl
        {
            /// <summary>点击事件。</summary>
            public event Action Clicked;

            /// <summary>常态背景色（选中态由页面注入）。</summary>
            public Color NormalColor { get; set; } = Color.Transparent;

            /// <summary>悬停背景色。</summary>
            public Color HoverColor { get; set; } = InkWashTheme.BgHover;

            private bool _pressed;

            public GachaTabButton()
            {
                BackgroundColor = Color.Transparent;
                ClipChildren = false;
                AutoFocus = true;
            }

            /// <inheritdoc />
            public override void OnMouseEnter(Float2 location)
            {
                base.OnMouseEnter(location);
                BackgroundColor = HoverColor;
            }

            /// <inheritdoc />
            public override void OnMouseLeave()
            {
                base.OnMouseLeave();
                _pressed = false;
                BackgroundColor = NormalColor;
            }

            /// <inheritdoc />
            public override bool OnMouseDown(Float2 location, MouseButton button)
            {
                base.OnMouseDown(location, button);
                if (button == MouseButton.Left)
                {
                    _pressed = true;
                }
                return true;
            }

            /// <inheritdoc />
            public override bool OnMouseUp(Float2 location, MouseButton button)
            {
                base.OnMouseUp(location, button);
                if (button == MouseButton.Left && _pressed)
                {
                    _pressed = false;
                    if (location.X >= 0f && location.X <= Width && location.Y >= 0f && location.Y <= Height)
                    {
                        Clicked?.Invoke();
                    }
                }
                return true;
            }
        }

        /// <summary>圆形指示点（品质点 / 卡池选中点），颜色由主题 token 注入。</summary>
        private sealed class GachaDot : Control
        {
            /// <summary>圆点颜色。</summary>
            public Color DotColor { get; set; } = Color.Transparent;

            public GachaDot()
            {
                Size = new Float2(8f, 8f);
                BackgroundColor = Color.Transparent;
                Enabled = false;
            }

            /// <inheritdoc />
            public override void Draw()
            {
                if (!Visible || Width <= 0f || Height <= 0f || DotColor.A <= 0f)
                {
                    return;
                }

                InkRenderHelper.FillCircle(
                    new Float2(Width * 0.5f, Height * 0.5f),
                    Mathf.Min(Width, Height) * 0.5f,
                    DotColor);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.StyleSystem;

namespace HundunWorld.Game.UI.Ink.Pages.Bestiary
{
    /// <summary>
    /// 菜单-博物志页面（设计稿 hundun-yy-ui/pages/menu-bestiary.html）。
    /// 结构：左侧 240px 导航栏（竖排"博物志" + 禁用组/功能组导航）
    /// → 顶部 60px 角色信息栏（头像/名号/等级/门派 + 三种货币）
    /// → 左侧收集区（页面标题 + 总收集进度 + 分类标签栏 + 收集品卡片网格）
    /// → 右侧 400px 详情预览面板（水墨插图 + 分类 + 物志描述 + 获取途径 + 收集进度）。
    /// 颜色/字体一律取自 <see cref="InkWashTheme"/> Token，不硬编码色值。
    /// 导航通过 <see cref="NavigationRequested"/> 交由路由器处理。
    /// </summary>
    public class MenuBestiaryPage : ContainerControl, IInkPage
    {
        #region 设计稿文案常量

        /// <summary>页面主标题（设计稿 h1 + 侧栏竖排标题）</summary>
        private const string PageTitleText = "博物志";

        /// <summary>页面副标题（设计稿 h1 下方说明）</summary>
        private const string PageSubtitleText = "收录天下奇珍异物，探寻江湖万物之灵";

        /// <summary>总收集进度说明</summary>
        private const string TotalProgressKeyText = "总收集进度";

        /// <summary>详情面板 — 分类行标签</summary>
        private const string DetailCategoryKeyText = "分类：";

        /// <summary>详情面板 — 物志描述小标题</summary>
        private const string DetailDescTitleText = "物志描述";

        /// <summary>详情面板 — 获取途径小标题</summary>
        private const string DetailObtainTitleText = "获取途径";

        /// <summary>详情面板 — 获取地点标签</summary>
        private const string DetailPlaceKeyText = "获取地点";

        /// <summary>详情面板 — 获取方式标签</summary>
        private const string DetailMethodKeyText = "获取方式";

        /// <summary>详情面板 — 收集进度标签</summary>
        private const string DetailProgressKeyText = "收集进度";

        /// <summary>未收集条目的名称占位</summary>
        private const string UncollectedNameText = "???";

        /// <summary>未收集条目的状态标签</summary>
        private const string UncollectedQualityText = "未发现";

        /// <summary>设计稿：品质标签 — 稀有（ink-tag-brand）</summary>
        private const string QualityRareText = "稀有";

        /// <summary>设计稿：品质标签 — 珍品（ink-tag-jade）</summary>
        private const string QualityPreciousText = "珍品";

        /// <summary>设计稿：品质标签 — 传说（ink-tag-vermilion）</summary>
        private const string QualityLegendText = "传说";

        /// <summary>设计稿：品质标签 — 凡品（ink-tag-common）</summary>
        private const string QualityCommonText = "凡品";

        #endregion

        #region 数据常量与数据表

        /// <summary>已收录数量（设计稿 56/120；接入真实数据源后由 <see cref="BindCollectionProgress"/> 覆盖）</summary>
        private int _collectedCount = 56;

        /// <summary>收录总量（设计稿 56/120；接入真实数据源后由 <see cref="BindCollectionProgress"/> 覆盖）</summary>
        private int _collectionTotal = 120;

        // 顶栏货币余额（设计稿示例数据：宝玉 12,580 / 铜钱 8,320 / 战功 3,450）

        /// <summary>宝玉余额（设计稿 12,580）</summary>
        private int _gem = 12580;

        /// <summary>铜钱余额（设计稿 8,320）</summary>
        private int _coin = 8320;

        /// <summary>战功余额（设计稿 3,450）</summary>
        private int _trophy = 3450;

        private const float SidebarWidth = 240f;
        private const float TopHeaderHeight = 60f;
        private const float DetailPanelWidth = 400f;
        private const int GridColumns = 4;
        private const float GridGap = 16f;
        private const float MaxCardWidth = 200f;
        private const float TabWidth = 88f;
        private const float NavItemHeight = 40f;

        /// <summary>左侧导航 — 角色养成组（设计稿中为禁用态）</summary>
        private static readonly string[] NavDisabledBefore = { "角色", "装备", "外观", "备战", "门派", "个人信息", "时间" };

        /// <summary>左侧导航 — 功能入口组（当前页为"博物志"）</summary>
        private static readonly string[] NavEnabled = { "任务", "博物志", "武林录", "营生", "组队", "邮箱", "商店" };

        /// <summary>功能入口组跳转目标（与设计稿 data-dom-id / 路由表一致）</summary>
        private static readonly string[] NavEnabledTargets =
        {
            InkPageDomIds.NavQuests,
            InkPageDomIds.NavBestiary,
            InkPageDomIds.NavMartialRecord,
            InkPageDomIds.NavLivelihood,
            InkPageDomIds.NavTeam,
            InkPageDomIds.NavMail,
            InkPageDomIds.NavShop,
        };

        /// <summary>当前所在导航项下标（博物志）</summary>
        private const int NavActiveIndex = 1;

        /// <summary>左侧导航 — 设置（设计稿中为禁用态）</summary>
        private const string NavDisabledAfter = "设置";

        /// <summary>分类标签（设计稿 6 个 cat-tab）</summary>
        private static readonly string[] CategoryNames = { "花草", "禽鸟", "走兽", "鱼虫", "矿石", "器物" };

        /// <summary>花草类收集品名称（设计稿前 8 张卡片，后 4 张为未收集占位）</summary>
        private static readonly string[] FlowerNames =
        {
            "雪莲", "灵芝", "天山雪菊", "寒梅", "幽兰", "龙胆草", "忘忧草", "石斛兰"
        };

        /// <summary>花草类收集品品质（设计稿卡片品质标签）</summary>
        private static readonly string[] FlowerQualities =
        {
            QualityRareText, QualityPreciousText, QualityLegendText, QualityCommonText,
            QualityPreciousText, QualityCommonText, QualityCommonText, QualityRareText
        };

        /// <summary>花草类收集品物志描述（首条为设计稿原文，其余为同结构的收录条目）</summary>
        private static readonly string[] FlowerDescriptions =
        {
            "生长于天山极寒之巅的珍稀灵药，花瓣洁白如玉，根茎泛着淡淡金光。传闻有起死回生之效，江湖中人为之趋之若鹜。每逢雪融初春方才绽放，花期仅三日，采撷极难。",
            "生于幽谷古木之下的灵菌，芝盖如云，色泽赤褐。服之可安神益气，武林中人常以之入药，然野生者极为罕见。",
            "天山绝壁之上的异种雪菊，花瓣如金，蕊心似血。相传百年方开一次，其香可解百毒，乃传说中的灵药。",
            "寒冬腊月傲雪而开的野梅，枝干虬劲，暗香浮动。虽为寻常草木，却生于绝境而不改其节，文人侠客多爱之。",
            "空谷幽兰，叶如翠带，花色素雅。不与群芳争艳，唯有知音可寻，采之者须循其香而非其形。",
            "茎叶如龙须，根味极苦，故名龙胆。山野之间随处可见，却是清热降火的良药，行走江湖者常备于行囊。",
            "又称萱草，花色橙黄如暖阳。古人云见之可忘忧，游子远行时多植于庭前，以慰思乡之情。",
            "附生于悬崖石缝之间，根不入土而汲晨露。花色金紫相间，因其难得的生长环境与品相，被视为兰中稀有之品。"
        };

        /// <summary>花草类收集品获取地点</summary>
        private static readonly string[] FlowerPlaces =
        {
            "天山雪峰", "幽谷古木", "天山绝壁", "寒山野岭", "空谷幽涧", "山野荒坡", "山野庭前", "悬崖石缝"
        };

        /// <summary>花草类收集品获取方式</summary>
        private static readonly string[] FlowerMethods =
        {
            "采摘", "采掘", "采摘", "折枝", "采摘", "采掘", "采摘", "采摘"
        };

        /// <summary>卡片总数（设计稿网格 12 格）</summary>
        private const int CardCount = 12;

        #endregion

        #region 控件字段

        private Float2 _screenSize;

        // 背景
        private Panel _backgroundLayer;
        private Panel _vignette;

        // 左侧导航栏
        private Panel _sidebar;
        private InkVerticalTitle _sidebarTitle;
        private InkDivider _sidebarTitleDivider;
        private InkDivider _sidebarNavDividerTop;
        private InkDivider _sidebarNavDividerBottom;
        private List<InkButton> _sidebarNavItems;

        // 返回按钮（设计稿 ink-back-btn，隐藏出口 → combat-hud）
        private InkBackButton _backButton;

        // 主区域 + 顶部信息栏
        private Panel _mainArea;
        private Panel _topHeader;
        private InkPanel _playerAvatar;
        private Label _playerNameLabel;
        private InkTag _playerLevelTag;
        private Label _playerSectLabel;
        private Label _currencyGemLabel;
        private Label _currencyCoinLabel;
        private Label _currencyTrophyLabel;

        // 内容区 = 左侧收集区 + 右侧详情面板
        private Panel _contentArea;
        private Panel _listSection;

        // 标题 + 总收集进度
        private Label _pageTitleLabel;
        private Label _pageSubtitleLabel;
        private Label _totalProgressKeyLabel;
        private InkBar _totalProgressBar;
        private Label _totalProgressValueLabel;

        // 分类标签栏
        private Panel _categoryBar;
        private Panel _categoryBarBorder;
        private List<InkButton> _categoryTabs;
        private List<Label> _categoryTabLabels;
        private List<Panel> _categoryTabUnderlines;

        // 收集品网格
        private Panel _gridArea;
        private List<InkButton> _cards;
        private List<Panel> _cardThumbs;
        private List<Label> _cardGlyphs;
        private List<Label> _cardNameLabels;
        private List<InkTag> _cardQualityTags;
        private List<Panel> _cardSelectedTints;
        private List<Panel> _cardUncollectedVeils;
        private List<InkPanel> _cardSelectedFrames;

        // 右侧详情面板
        private Panel _detailPanel;
        private Panel _detailIllust;
        private Label _detailIllustGlyph;
        private Panel _detailIllustFade;
        private Label _detailNameLabel;
        private InkTag _detailQualityTag;
        private Label _detailCategoryKeyLabel;
        private Label _detailCategoryValueLabel;
        private InkDivider _detailDividerTop;
        private Label _detailDescTitleLabel;
        private Label _detailDescLabel;
        private InkDivider _detailDividerMid;
        private Label _detailObtainTitleLabel;
        private Label _detailPlaceKeyLabel;
        private Label _detailPlaceValueLabel;
        private Label _detailMethodKeyLabel;
        private Label _detailMethodValueLabel;
        private InkDivider _detailDividerBottom;
        private Label _detailProgressKeyLabel;
        private Label _detailProgressValueLabel;
        private InkBar _detailProgressBar;
        private Label _detailProgressCaptionLabel;

        // 当前状态
        private int _activeCategoryIndex;
        private int _selectedCardIndex;

        #endregion

        /// <summary>
        /// 标准导航事件（对齐 Ink 页面契约，由工厂挂钩后经路由器跳转）。
        /// </summary>
        public event Action<string> NavigationRequested;

        public MenuBestiaryPage()
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

            _activeCategoryIndex = 0;
            _selectedCardIndex = 0;

            try
            {
                BuildBackground();
                BuildSidebar();
                BuildBackButton();
                BuildMainArea();
                BuildTopHeader();
                BuildListSection();
                BuildCategoryTabs();
                BuildCardGrid();
                BuildDetailPanel();

                RefreshAllData();
                ApplyLayout();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[MenuBestiaryPage] 初始化失败: {ex.Message}");
            }
        }

        #region 构建

        /// <summary>背景层：深墨底 + 暗角晕影。</summary>
        private void BuildBackground()
        {
            _backgroundLayer = new Panel
            {
                BackgroundColor = InkWashTheme.BaseDefault,
                Parent = this
            };

            _vignette = new Panel
            {
                BackgroundColor = WithAlpha(InkWashTheme.Abyss, 0.55f),
                Parent = this
            };
        }

        /// <summary>左侧 240px 导航栏：竖排标题 + 禁用组 + 功能组 + 设置。</summary>
        private void BuildSidebar()
        {
            _sidebar = new Panel
            {
                BackgroundColor = InkWashTheme.BaseSecondary,
                Parent = this
            };

            _sidebarTitle = new InkVerticalTitle
            {
                Text = PageTitleText,
                FontSize = 28f,
                Parent = _sidebar
            };

            _sidebarTitleDivider = new InkDivider { Parent = _sidebar };

            _sidebarNavItems = new List<InkButton>();
            for (int i = 0; i < NavDisabledBefore.Length; i++)
            {
                _sidebarNavItems.Add(CreateNavItem(NavDisabledBefore[i], true, null));
            }

            _sidebarNavDividerTop = new InkDivider { Parent = _sidebar };

            for (int i = 0; i < NavEnabled.Length; i++)
            {
                _sidebarNavItems.Add(CreateNavItem(NavEnabled[i], false, NavEnabledTargets[i]));
            }

            _sidebarNavDividerBottom = new InkDivider { Parent = _sidebar };

            _sidebarNavItems.Add(CreateNavItem(NavDisabledAfter, true, null));
        }

        /// <summary>
        /// 创建单个导航项。禁用项不绑定点击（设计稿 nav-item-disabled），
        /// 当前页（博物志）使用 nav-item-active 的金色高亮 + 左侧金线。
        /// </summary>
        private InkButton CreateNavItem(string text, bool disabled, string targetDomId)
        {
            bool isActive = !disabled && text == NavEnabled[NavActiveIndex];

            InkButton item = new InkButton
            {
                Text = text,
                Variant = InkButtonVariant.Ghost,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                TextColor = disabled
                    ? InkWashTheme.TextDisabled
                    : (isActive ? InkWashTheme.GoldBright : InkWashTheme.PaperBright),
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 15f),
                Parent = _sidebar
            };

            if (isActive)
            {
                item.BackgroundColor = WithAlpha(InkWashTheme.GoldPrimary, 0.1f);

                // nav-item-active 左侧 2px 金线
                var activeBar = new Panel
                {
                    Location = new Float2(0f, 4f),
                    Size = new Float2(2f, NavItemHeight - 8f),
                    BackgroundColor = InkWashTheme.GoldPrimary,
                    Parent = item
                };
                activeBar.Enabled = false;
            }

            if (!disabled && !string.IsNullOrEmpty(targetDomId))
            {
                string target = targetDomId;
                item.Clicked += () => NavigationRequested?.Invoke(target);
            }

            return item;
        }

        /// <summary>返回按钮（设计稿隐藏出口 back-hud → 战斗 HUD）。</summary>
        private void BuildBackButton()
        {
            _backButton = new InkBackButton { Parent = this };
            _backButton.Clicked += () => NavigationRequested?.Invoke(InkPageDomIds.BackHud);
        }

        private void BuildMainArea()
        {
            _mainArea = new Panel
            {
                BackgroundColor = Color.Transparent,
                Parent = this
            };

            _contentArea = new Panel
            {
                BackgroundColor = Color.Transparent,
                Parent = _mainArea
            };
        }

        /// <summary>顶部 60px 角色信息栏：头像 + 名号 + 等级 + 门派 + 三种货币。</summary>
        private void BuildTopHeader()
        {
            _topHeader = new Panel
            {
                BackgroundColor = InkWashTheme.BaseTertiary,
                Parent = _mainArea
            };

            _playerAvatar = new InkPanel
            {
                Size = new Float2(40f, 40f),
                Radius = 20f,
                BackgroundColor = InkWashTheme.VermilionPrimary,
                Parent = _topHeader
            };

            Label avatarLabel = new Label
            {
                Text = "无",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 18f),
                TextColor = InkWashTheme.PaperBright,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.StretchAll,
                Offsets = Margin.Zero,
                Parent = _playerAvatar
            };

            _playerNameLabel = new Label
            {
                Text = "无名侠",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 18f),
                TextColor = InkWashTheme.PaperBright,
                VerticalAlignment = TextAlignment.Center,
                Size = new Float2(100f, 28f),
                Parent = _topHeader
            };

            _playerLevelTag = new InkTag
            {
                Text = "Lv.42",
                TagVariant = InkTagVariant.Vermilion,
                Size = new Float2(56f, 22f),
                Parent = _topHeader
            };

            _playerSectLabel = new Label
            {
                Text = "逍遥派",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                TextColor = InkWashTheme.GoldPrimary,
                VerticalAlignment = TextAlignment.Center,
                Size = new Float2(100f, 22f),
                Parent = _topHeader
            };

            _currencyGemLabel = CreateCurrencyLabel(_gem.ToString("N0"), InkWashTheme.GoldBright);
            _currencyCoinLabel = CreateCurrencyLabel(_coin.ToString("N0"), InkWashTheme.PaperAged);
            _currencyTrophyLabel = CreateCurrencyLabel(_trophy.ToString("N0"), InkWashTheme.JadeBright);
        }

        /// <summary>货币数值标签（设计稿：宝玉/铜钱/战功三项，数字字体 + 右对齐数值）。</summary>
        private Label CreateCurrencyLabel(string text, Color color)
        {
            return new Label
            {
                Text = text,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 13f),
                TextColor = color,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                Size = new Float2(80f, 22f),
                Parent = _topHeader
            };
        }

        /// <summary>左侧收集区：标题 + 副标题 + 总收集进度。</summary>
        private void BuildListSection()
        {
            _listSection = new Panel
            {
                BackgroundColor = Color.Transparent,
                Parent = _contentArea
            };

            _pageTitleLabel = new Label
            {
                Text = PageTitleText,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 32f),
                TextColor = InkWashTheme.GoldBright,
                VerticalAlignment = TextAlignment.Center,
                Size = new Float2(280f, 40f),
                Parent = _listSection
            };

            _pageSubtitleLabel = new Label
            {
                Text = PageSubtitleText,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                TextColor = InkWashTheme.PaperAged,
                VerticalAlignment = TextAlignment.Center,
                Size = new Float2(420f, 22f),
                Parent = _listSection
            };

            _totalProgressKeyLabel = new Label
            {
                Text = TotalProgressKeyText,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                TextColor = InkWashTheme.PaperAged,
                VerticalAlignment = TextAlignment.Center,
                Size = new Float2(96f, 22f),
                Parent = _listSection
            };

            _totalProgressBar = new InkBar
            {
                Size = new Float2(180f, 8f),
                FillVariant = InkBarFillVariant.Gold,
                Parent = _listSection
            };

            _totalProgressValueLabel = new Label
            {
                Text = _collectedCount + "/" + _collectionTotal,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 13f),
                TextColor = InkWashTheme.GoldPrimary,
                VerticalAlignment = TextAlignment.Center,
                Size = new Float2(70f, 22f),
                Parent = _listSection
            };
        }

        /// <summary>分类标签栏（设计稿 cat-tab：花草/禽鸟/走兽/鱼虫/矿石/器物）。</summary>
        private void BuildCategoryTabs()
        {
            _categoryBar = new Panel
            {
                BackgroundColor = Color.Transparent,
                Parent = _listSection
            };

            _categoryBarBorder = new Panel
            {
                Height = 1f,
                BackgroundColor = InkWashTheme.BorderNeutralL1,
                Parent = _categoryBar
            };

            _categoryTabs = new List<InkButton>();
            _categoryTabLabels = new List<Label>();
            _categoryTabUnderlines = new List<Panel>();

            for (int i = 0; i < CategoryNames.Length; i++)
            {
                InkButton tab = new InkButton
                {
                    Text = string.Empty,
                    Variant = InkButtonVariant.Ghost,
                    BorderThickness = 0f,
                    Parent = _categoryBar
                };

                int index = i;
                tab.Clicked += () => SelectCategory(index);

                Label tabLabel = new Label
                {
                    Text = CategoryNames[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 14f),
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.StretchAll,
                    Offsets = Margin.Zero,
                    Parent = tab
                };

                Panel underline = new Panel
                {
                    Height = 2f,
                    BackgroundColor = InkWashTheme.GoldPrimary,
                    Parent = tab
                };

                _categoryTabs.Add(tab);
                _categoryTabLabels.Add(tabLabel);
                _categoryTabUnderlines.Add(underline);
            }
        }

        /// <summary>收集品卡片网格（设计稿 grid-cols-4，12 张卡片）。</summary>
        private void BuildCardGrid()
        {
            _gridArea = new Panel
            {
                BackgroundColor = Color.Transparent,
                Parent = _listSection
            };

            _cards = new List<InkButton>();
            _cardThumbs = new List<Panel>();
            _cardGlyphs = new List<Label>();
            _cardNameLabels = new List<Label>();
            _cardQualityTags = new List<InkTag>();
            _cardSelectedTints = new List<Panel>();
            _cardUncollectedVeils = new List<Panel>();
            _cardSelectedFrames = new List<InkPanel>();

            for (int i = 0; i < CardCount; i++)
            {
                InkButton card = new InkButton
                {
                    Text = string.Empty,
                    Variant = InkButtonVariant.Secondary,
                    Parent = _gridArea
                };

                int index = i;
                card.Clicked += () => SelectCard(index);

                // 选中态金色微弱背景（子控件，不受按钮悬停态复位影响）
                Panel selectedTint = new Panel
                {
                    BackgroundColor = WithAlpha(InkWashTheme.GoldPrimary, 0.08f),
                    Visible = false,
                    Parent = card
                };
                selectedTint.Enabled = false;

                Panel thumb = new Panel
                {
                    BackgroundColor = InkWashTheme.BaseElevated,
                    Parent = card
                };

                Label glyph = new Label
                {
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 28f),
                    TextColor = InkWashTheme.GoldBright,
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.StretchAll,
                    Offsets = Margin.Zero,
                    Parent = thumb
                };

                Label name = new Label
                {
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 13f),
                    TextColor = InkWashTheme.PaperBright,
                    VerticalAlignment = TextAlignment.Center,
                    Parent = card
                };

                InkTag quality = new InkTag { Parent = card };

                // 未收集态压暗遮罩（对应设计稿 opacity .45 + grayscale）
                Panel veil = new Panel
                {
                    BackgroundColor = WithAlpha(InkWashTheme.BaseDefault, 0.62f),
                    Visible = false,
                    Parent = card
                };
                veil.Enabled = false;

                // 选中态 2px 金框（InkPanel 恒绘制金色描边）
                InkPanel frame = new InkPanel
                {
                    BackgroundColor = Color.Transparent,
                    Radius = InkWashTheme.RadiusMd,
                    Visible = false,
                    Parent = card
                };
                frame.Enabled = false;

                _cards.Add(card);
                _cardThumbs.Add(thumb);
                _cardGlyphs.Add(glyph);
                _cardNameLabels.Add(name);
                _cardQualityTags.Add(quality);
                _cardSelectedTints.Add(selectedTint);
                _cardUncollectedVeils.Add(veil);
                _cardSelectedFrames.Add(frame);
            }
        }

        /// <summary>右侧 400px 详情预览面板：插图 + 分类 + 物志描述 + 获取途径 + 收集进度。</summary>
        private void BuildDetailPanel()
        {
            _detailPanel = new Panel
            {
                BackgroundColor = InkWashTheme.BaseSecondary,
                Parent = _contentArea
            };

            _detailIllust = new Panel
            {
                BackgroundColor = InkWashTheme.BaseTertiary,
                Parent = _detailPanel
            };

            _detailIllustGlyph = new Label
            {
                Text = FlowerNames[0].Substring(0, 1),
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 64f),
                TextColor = WithAlpha(InkWashTheme.GoldPrimary, 0.55f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                Parent = _detailIllust
            };

            _detailIllustFade = new Panel
            {
                BackgroundColor = WithAlpha(InkWashTheme.BaseSecondary, 0.9f),
                Parent = _detailIllust
            };

            _detailNameLabel = new Label
            {
                Text = FlowerNames[0],
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 22f),
                TextColor = InkWashTheme.PaperBright,
                VerticalAlignment = TextAlignment.Center,
                Size = new Float2(240f, 32f),
                Parent = _detailIllustFade
            };

            _detailQualityTag = new InkTag
            {
                Text = FlowerQualities[0],
                Size = new Float2(56f, 22f),
                Parent = _detailIllustFade
            };
            ApplyQualityTagStyle(_detailQualityTag, FlowerQualities[0]);

            _detailCategoryKeyLabel = CreateDetailKeyLabel(DetailCategoryKeyText);
            _detailCategoryValueLabel = CreateDetailValueLabel(CategoryNames[0]);

            _detailDividerTop = new InkDivider { Parent = _detailPanel };

            _detailDescTitleLabel = CreateDetailTitleLabel(DetailDescTitleText);
            _detailDescLabel = new Label
            {
                Text = FlowerDescriptions[0],
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                TextColor = InkWashTheme.Paper,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Near,
                Wrapping = TextWrapping.WrapWords,
                Parent = _detailPanel
            };

            _detailDividerMid = new InkDivider { Parent = _detailPanel };

            _detailObtainTitleLabel = CreateDetailTitleLabel(DetailObtainTitleText);

            _detailPlaceKeyLabel = CreateDetailKeyLabel(DetailPlaceKeyText);
            _detailPlaceValueLabel = CreateDetailValueLabel(FlowerPlaces[0]);
            _detailPlaceValueLabel.HorizontalAlignment = TextAlignment.Far;

            _detailMethodKeyLabel = CreateDetailKeyLabel(DetailMethodKeyText);
            _detailMethodValueLabel = CreateDetailValueLabel(FlowerMethods[0]);
            _detailMethodValueLabel.HorizontalAlignment = TextAlignment.Far;

            _detailDividerBottom = new InkDivider { Parent = _detailPanel };

            _detailProgressKeyLabel = CreateDetailKeyLabel(DetailProgressKeyText);
            _detailProgressValueLabel = new Label
            {
                Text = _collectedCount + "/" + _collectionTotal,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 13f),
                TextColor = InkWashTheme.GoldPrimary,
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
                Parent = _detailPanel
            };

            _detailProgressBar = new InkBar
            {
                FillVariant = InkBarFillVariant.Gold,
                Parent = _detailPanel
            };

            _detailProgressCaptionLabel = new Label
            {
                Text = $"已收录 {_collectedCount} 株，继续探索可解锁更多记录",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextTertiary,
                VerticalAlignment = TextAlignment.Center,
                Parent = _detailPanel
            };
        }

        /// <summary>详情面板 — 键名标签（次级说明文字）。</summary>
        private Label CreateDetailKeyLabel(string text)
        {
            return new Label
            {
                Text = text,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                TextColor = InkWashTheme.PaperAged,
                VerticalAlignment = TextAlignment.Center,
                Parent = _detailPanel
            };
        }

        /// <summary>详情面板 — 取值标签（纸色正文）。</summary>
        private Label CreateDetailValueLabel(string text)
        {
            return new Label
            {
                Text = text,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                TextColor = InkWashTheme.PaperBright,
                VerticalAlignment = TextAlignment.Center,
                Parent = _detailPanel
            };
        }

        /// <summary>详情面板 — 小标题（鎏金 16px）。</summary>
        private Label CreateDetailTitleLabel(string text)
        {
            return new Label
            {
                Text = text,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 16f),
                TextColor = InkWashTheme.GoldPrimary,
                VerticalAlignment = TextAlignment.Center,
                Parent = _detailPanel
            };
        }

        #endregion

        #region 数据刷新与交互

        /// <summary>
        /// 刷新全部数据（总进度、分类卡片、详情面板）。
        /// 进度与货币数据取自实例字段（初值 = 设计稿示例数据，可被「数据绑定 API」覆盖），
        /// 本方法在构造末尾与每次绑定后调用。
        /// </summary>
        private void RefreshAllData()
        {
            float ratio = _collectionTotal > 0
                ? Mathf.Clamp(_collectedCount / (float)_collectionTotal, 0f, 1f)
                : 0f;

            if (_totalProgressBar != null) _totalProgressBar.Value = ratio;
            if (_detailProgressBar != null) _detailProgressBar.Value = ratio;

            string progressText = _collectedCount + "/" + _collectionTotal;
            if (_totalProgressValueLabel != null) _totalProgressValueLabel.Text = progressText;
            if (_detailProgressValueLabel != null) _detailProgressValueLabel.Text = progressText;
            if (_detailProgressCaptionLabel != null)
                _detailProgressCaptionLabel.Text = $"已收录 {_collectedCount} 株，继续探索可解锁更多记录";

            if (_currencyGemLabel != null) _currencyGemLabel.Text = _gem.ToString("N0");
            if (_currencyCoinLabel != null) _currencyCoinLabel.Text = _coin.ToString("N0");
            if (_currencyTrophyLabel != null) _currencyTrophyLabel.Text = _trophy.ToString("N0");

            RefreshTabVisuals();
            RefreshCardData();
        }

        // ==================================================================
        // 数据绑定 API（接入真实数据源时覆盖设计稿示例初值）
        // ==================================================================

        /// <summary>
        /// 绑定收集进度（总进度与详情面板进度同步刷新）。
        /// 不调用时保持设计稿初值 56/120。
        /// </summary>
        /// <param name="collected">已收录数量</param>
        /// <param name="total">收录总量（≤0 时进度按 0 处理）</param>
        public void BindCollectionProgress(int collected, int total)
        {
            _collectedCount = collected;
            _collectionTotal = total;
            RefreshAllData();
        }

        /// <summary>
        /// 绑定顶栏货币余额（宝玉 / 铜钱 / 战功）。
        /// 不调用时保持设计稿初值 12,580 / 8,320 / 3,450。
        /// </summary>
        /// <param name="gem">宝玉余额</param>
        /// <param name="coin">铜钱余额</param>
        /// <param name="trophy">战功余额</param>
        public void BindCurrency(int gem, int coin, int trophy)
        {
            _gem = gem;
            _coin = coin;
            _trophy = trophy;
            if (_currencyGemLabel != null) _currencyGemLabel.Text = _gem.ToString("N0");
            if (_currencyCoinLabel != null) _currencyCoinLabel.Text = _coin.ToString("N0");
            if (_currencyTrophyLabel != null) _currencyTrophyLabel.Text = _trophy.ToString("N0");
        }

        /// <summary>刷新分类标签的激活态（金线 + 金色文字）。</summary>
        private void RefreshTabVisuals()
        {
            for (int i = 0; i < _categoryTabs.Count; i++)
            {
                bool isActive = i == _activeCategoryIndex;
                _categoryTabLabels[i].TextColor = isActive ? InkWashTheme.GoldBright : InkWashTheme.PaperAged;
                _categoryTabUnderlines[i].Visible = isActive;
            }
        }

        /// <summary>
        /// 按当前分类刷新卡片数据。设计稿仅给出"花草"类的收录内容，
        /// 其余分类按未收集状态（??? / 未发现）呈现。
        /// </summary>
        private void RefreshCardData()
        {
            bool isFlowerCategory = _activeCategoryIndex == 0;
            _selectedCardIndex = isFlowerCategory ? 0 : -1;

            for (int i = 0; i < _cards.Count; i++)
            {
                bool discovered = isFlowerCategory && i < FlowerNames.Length;
                string name = discovered ? FlowerNames[i] : UncollectedNameText;
                string quality = discovered ? FlowerQualities[i] : UncollectedQualityText;

                _cardGlyphs[i].Text = discovered ? name.Substring(0, 1) : "?";
                _cardGlyphs[i].TextColor = GetQualityGlyphColor(quality);
                _cardThumbs[i].BackgroundColor = discovered
                    ? InkWashTheme.BaseElevated
                    : InkWashTheme.BaseSecondary;
                _cardNameLabels[i].Text = name;
                _cardNameLabels[i].TextColor = discovered ? InkWashTheme.PaperBright : InkWashTheme.TextTertiary;
                _cardQualityTags[i].Text = quality;
                ApplyQualityTagStyle(_cardQualityTags[i], quality);
                _cardUncollectedVeils[i].Visible = !discovered;
            }

            UpdateCardSelectionVisuals();
            UpdateDetailPanel(_selectedCardIndex);
        }

        /// <summary>切换分类标签。</summary>
        private void SelectCategory(int index)
        {
            if (index < 0 || index >= CategoryNames.Length || index == _activeCategoryIndex)
            {
                return;
            }

            _activeCategoryIndex = index;
            RefreshTabVisuals();
            RefreshCardData();
        }

        /// <summary>点击卡片：选中已收集条目并同步右侧详情（未收集条目不可选，对应设计稿 cursor: default）。</summary>
        private void SelectCard(int index)
        {
            if (index < 0 || index >= _cards.Count || _activeCategoryIndex != 0 || index >= FlowerNames.Length)
            {
                return;
            }

            _selectedCardIndex = index;
            UpdateCardSelectionVisuals();
            UpdateDetailPanel(index);
        }

        /// <summary>刷新卡片选中态（金色微底 + 金框）。</summary>
        private void UpdateCardSelectionVisuals()
        {
            for (int i = 0; i < _cards.Count; i++)
            {
                bool selected = i == _selectedCardIndex;
                _cardSelectedTints[i].Visible = selected;
                _cardSelectedFrames[i].Visible = selected;
            }
        }

        /// <summary>同步右侧详情面板内容。</summary>
        private void UpdateDetailPanel(int index)
        {
            if (index < 0 || index >= FlowerNames.Length)
            {
                return;
            }

            _detailNameLabel.Text = FlowerNames[index];
            _detailIllustGlyph.Text = FlowerNames[index].Substring(0, 1);
            _detailQualityTag.Text = FlowerQualities[index];
            ApplyQualityTagStyle(_detailQualityTag, FlowerQualities[index]);
            _detailCategoryValueLabel.Text = CategoryNames[_activeCategoryIndex];
            _detailDescLabel.Text = FlowerDescriptions[index];
            _detailPlaceValueLabel.Text = FlowerPlaces[index];
            _detailMethodValueLabel.Text = FlowerMethods[index];
        }

        /// <summary>品质 → 标签样式（对应设计稿 ink-tag-brand / jade / vermilion / common）。</summary>
        private static void ApplyQualityTagStyle(InkTag tag, string quality)
        {
            if (tag == null)
            {
                return;
            }

            if (quality == QualityLegendText)
            {
                tag.TagVariant = InkTagVariant.Vermilion;
                return;
            }

            if (quality == QualityRareText)
            {
                tag.TagVariant = InkTagVariant.Brand;
                return;
            }

            tag.TagVariant = InkTagVariant.Default;

            if (quality == QualityPreciousText)
            {
                tag.TextColor = InkWashTheme.JadeBright;
                tag.BackgroundColor = WithAlpha(InkWashTheme.JadePrimary, 0.12f);
            }
            else if (quality == QualityCommonText)
            {
                tag.TextColor = InkWashTheme.PaperDark;
                tag.BackgroundColor = WithAlpha(InkWashTheme.PaperDark, 0.1f);
            }
            else
            {
                tag.TextColor = InkWashTheme.TextTertiary;
                tag.BackgroundColor = WithAlpha(InkWashTheme.TextTertiary, 0.1f);
            }
        }

        /// <summary>品质 → 缩略图字色（对齐设计稿卡片图标配色）。</summary>
        private static Color GetQualityGlyphColor(string quality)
        {
            if (quality == QualityLegendText) return InkWashTheme.VermilionBright;
            if (quality == QualityRareText) return InkWashTheme.GoldBright;
            if (quality == QualityPreciousText) return InkWashTheme.JadeBright;
            if (quality == QualityCommonText) return InkWashTheme.PaperAged;
            return InkWashTheme.TextTertiary;
        }

        /// <summary>取同色系指定透明度（Token 派生，避免硬编码色值）。</summary>
        private static Color WithAlpha(Color color, float alpha)
        {
            return new Color(color.R, color.G, color.B, alpha);
        }

        #endregion

        #region 布局

        private void ApplyLayout()
        {
            float sw = _screenSize.X;
            float sh = _screenSize.Y;

            if (_backgroundLayer != null)
            {
                _backgroundLayer.Location = Float2.Zero;
                _backgroundLayer.Size = new Float2(sw, sh);
            }

            if (_vignette != null)
            {
                _vignette.Location = Float2.Zero;
                _vignette.Size = new Float2(sw, sh);
            }

            // ---- 返回按钮（设计稿：屏幕左上角 20/20 悬浮）----
            if (_backButton != null)
            {
                _backButton.Location = new Float2(20f, 20f);
            }

            // ---- 左侧导航栏 ----
            if (_sidebar != null)
            {
                _sidebar.Location = Float2.Zero;
                _sidebar.Size = new Float2(SidebarWidth, sh);
            }

            if (_sidebarTitle != null)
            {
                _sidebarTitle.Location = new Float2((SidebarWidth - 40f) * 0.5f, 24f);
                _sidebarTitle.Size = new Float2(40f, 112f);
            }

            if (_sidebarTitleDivider != null)
            {
                _sidebarTitleDivider.Location = new Float2(16f, 160f);
                _sidebarTitleDivider.Width = SidebarWidth - 32f;
            }

            float navY = 172f;
            int disabledBeforeCount = NavDisabledBefore.Length;
            for (int i = 0; i < _sidebarNavItems.Count; i++)
            {
                if (_sidebarNavItems[i] != null)
                {
                    _sidebarNavItems[i].Location = new Float2(0f, navY);
                    _sidebarNavItems[i].Size = new Float2(SidebarWidth, NavItemHeight);
                }

                navY += NavItemHeight;

                if (i == disabledBeforeCount - 1)
                {
                    if (_sidebarNavDividerTop != null)
                    {
                        _sidebarNavDividerTop.Location = new Float2(16f, navY + 12f);
                        _sidebarNavDividerTop.Width = SidebarWidth - 32f;
                    }
                    navY += 28f;
                }
                else if (i == disabledBeforeCount + NavEnabled.Length - 1)
                {
                    if (_sidebarNavDividerBottom != null)
                    {
                        _sidebarNavDividerBottom.Location = new Float2(16f, navY + 12f);
                        _sidebarNavDividerBottom.Width = SidebarWidth - 32f;
                    }
                    navY += 28f;
                }
            }

            // ---- 主区域 ----
            float mainW = sw - SidebarWidth;
            if (_mainArea != null)
            {
                _mainArea.Location = new Float2(SidebarWidth, 0f);
                _mainArea.Size = new Float2(mainW, sh);
            }

            if (_topHeader != null)
            {
                _topHeader.Location = Float2.Zero;
                _topHeader.Size = new Float2(mainW, TopHeaderHeight);
            }

            if (_contentArea != null)
            {
                _contentArea.Location = new Float2(0f, TopHeaderHeight);
                _contentArea.Size = new Float2(mainW, sh - TopHeaderHeight);
            }

            LayoutTopHeader(mainW);
            LayoutContent(mainW, sh - TopHeaderHeight);
        }

        /// <summary>顶部信息栏内部布局。</summary>
        private void LayoutTopHeader(float mainW)
        {
            if (_playerAvatar != null) _playerAvatar.Location = new Float2(32f, 10f);
            if (_playerNameLabel != null) _playerNameLabel.Location = new Float2(88f, 16f);
            if (_playerLevelTag != null) _playerLevelTag.Location = new Float2(196f, 19f);
            if (_playerSectLabel != null) _playerSectLabel.Location = new Float2(268f, 19f);

            const float currencyWidth = 80f;
            const float currencyGap = 24f;
            float currencyX = mainW - 32f - (currencyWidth * 3f + currencyGap * 2f);

            if (_currencyGemLabel != null) _currencyGemLabel.Location = new Float2(currencyX, 19f);
            if (_currencyCoinLabel != null) _currencyCoinLabel.Location = new Float2(currencyX + currencyWidth + currencyGap, 19f);
            if (_currencyTrophyLabel != null) _currencyTrophyLabel.Location = new Float2(currencyX + (currencyWidth + currencyGap) * 2f, 19f);
        }

        /// <summary>内容区布局：左侧收集区（标题/进度/分类/网格）+ 右侧详情面板。</summary>
        private void LayoutContent(float mainW, float contentH)
        {
            float listW = mainW - DetailPanelWidth;

            if (_listSection != null)
            {
                _listSection.Location = Float2.Zero;
                _listSection.Size = new Float2(listW, contentH);
            }

            if (_detailPanel != null)
            {
                _detailPanel.Location = new Float2(listW, 0f);
                _detailPanel.Size = new Float2(DetailPanelWidth, contentH);
            }

            // 标题 + 副标题
            if (_pageTitleLabel != null) _pageTitleLabel.Location = new Float2(32f, 24f);
            if (_pageSubtitleLabel != null) _pageSubtitleLabel.Location = new Float2(32f, 70f);

            // 总收集进度（右对齐：说明 + 进度条 + 56/120）
            float progressRight = listW - 32f;
            if (_totalProgressValueLabel != null) _totalProgressValueLabel.Location = new Float2(progressRight - 70f, 48f);
            if (_totalProgressBar != null) _totalProgressBar.Location = new Float2(progressRight - 70f - 16f - 180f, 55f);
            if (_totalProgressKeyLabel != null) _totalProgressKeyLabel.Location = new Float2(progressRight - 70f - 16f - 180f - 12f - 96f, 48f);

            // 分类标签栏
            if (_categoryBar != null)
            {
                _categoryBar.Location = new Float2(0f, 110f);
                _categoryBar.Size = new Float2(listW, 46f);
            }

            if (_categoryBarBorder != null)
            {
                _categoryBarBorder.Location = new Float2(0f, 45f);
                _categoryBarBorder.Width = listW;
            }

            for (int i = 0; i < _categoryTabs.Count; i++)
            {
                float tabX = 32f + i * (TabWidth + 4f);
                _categoryTabs[i].Location = new Float2(tabX, 0f);
                _categoryTabs[i].Size = new Float2(TabWidth, 46f);
                _categoryTabUnderlines[i].Location = new Float2(0f, 44f);
                _categoryTabUnderlines[i].Size = new Float2(TabWidth, 2f);
            }

            // 收集品网格
            float gridTop = 172f;
            float gridW = listW - 64f;
            float gridH = contentH - gridTop - 24f;

            if (_gridArea != null)
            {
                _gridArea.Location = new Float2(32f, gridTop);
                _gridArea.Size = new Float2(gridW, gridH);
            }

            // 卡片宽度：4 列均分，且受最大宽度与可用高度（3 行）双重约束
            int cardRows = (CardCount + GridColumns - 1) / GridColumns;
            float cardWByWidth = (gridW - GridGap * (GridColumns - 1)) / GridColumns;
            float cardWByHeight = (gridH - GridGap * (cardRows - 1)) / cardRows - 34f;
            float cardW = Mathf.Max(72f, Mathf.Min(Mathf.Min(cardWByWidth, cardWByHeight), MaxCardWidth));
            float cardH = cardW + 34f;
            float rowW = cardW * GridColumns + GridGap * (GridColumns - 1);
            float startX = (gridW - rowW) * 0.5f;

            for (int i = 0; i < _cards.Count; i++)
            {
                int col = i % GridColumns;
                int row = i / GridColumns;
                LayoutCard(i, startX + col * (cardW + GridGap), row * (cardH + GridGap), cardW, cardH);
            }

            LayoutDetailPanel(contentH);
        }

        /// <summary>单张卡片内部布局。</summary>
        private void LayoutCard(int i, float x, float y, float cardW, float cardH)
        {
            _cards[i].Location = new Float2(x, y);
            _cards[i].Size = new Float2(cardW, cardH);

            float thumbSize = cardW - 24f;
            _cardThumbs[i].Location = new Float2(12f, 12f);
            _cardThumbs[i].Size = new Float2(thumbSize, thumbSize);

            float infoY = 12f + thumbSize + 10f;
            _cardNameLabels[i].Location = new Float2(12f, infoY);
            _cardNameLabels[i].Size = new Float2(cardW - 24f - 56f, 22f);

            _cardQualityTags[i].Location = new Float2(cardW - 12f - 52f, infoY);
            _cardQualityTags[i].Size = new Float2(52f, 22f);

            _cardSelectedTints[i].Location = new Float2(2f, 2f);
            _cardSelectedTints[i].Size = new Float2(cardW - 4f, cardH - 4f);

            _cardUncollectedVeils[i].Location = new Float2(2f, 2f);
            _cardUncollectedVeils[i].Size = new Float2(cardW - 4f, cardH - 4f);

            _cardSelectedFrames[i].Location = Float2.Zero;
            _cardSelectedFrames[i].Size = new Float2(cardW, cardH);
        }

        /// <summary>右侧详情面板内部布局（面板宽度固定 400px）。</summary>
        private void LayoutDetailPanel(float contentH)
        {
            const float pad = 24f;
            const float innerW = DetailPanelWidth - pad * 2f;
            const float illustH = 220f;

            if (_detailIllust != null)
            {
                _detailIllust.Location = Float2.Zero;
                _detailIllust.Size = new Float2(DetailPanelWidth, illustH);
            }

            if (_detailIllustGlyph != null)
            {
                _detailIllustGlyph.Location = new Float2(pad, 36f);
                _detailIllustGlyph.Size = new Float2(140f, 120f);
            }

            if (_detailIllustFade != null)
            {
                _detailIllustFade.Location = new Float2(0f, illustH - 72f);
                _detailIllustFade.Size = new Float2(DetailPanelWidth, 72f);
            }

            if (_detailNameLabel != null) _detailNameLabel.Location = new Float2(pad, 20f);
            if (_detailQualityTag != null) _detailQualityTag.Location = new Float2(DetailPanelWidth - pad - 56f, 25f);

            if (_detailCategoryKeyLabel != null)
            {
                _detailCategoryKeyLabel.Location = new Float2(pad, illustH + 24f);
                _detailCategoryKeyLabel.Size = new Float2(52f, 20f);
            }

            if (_detailCategoryValueLabel != null)
            {
                _detailCategoryValueLabel.Location = new Float2(pad + 56f, illustH + 24f);
                _detailCategoryValueLabel.Size = new Float2(120f, 20f);
            }

            if (_detailDividerTop != null)
            {
                _detailDividerTop.Location = new Float2(pad, illustH + 56f);
                _detailDividerTop.Width = innerW;
            }

            if (_detailDescTitleLabel != null)
            {
                _detailDescTitleLabel.Location = new Float2(pad, illustH + 72f);
                _detailDescTitleLabel.Size = new Float2(200f, 24f);
            }

            if (_detailDescLabel != null)
            {
                _detailDescLabel.Location = new Float2(pad, illustH + 102f);
                _detailDescLabel.Size = new Float2(innerW, 112f);
            }

            if (_detailDividerMid != null)
            {
                _detailDividerMid.Location = new Float2(pad, illustH + 228f);
                _detailDividerMid.Width = innerW;
            }

            if (_detailObtainTitleLabel != null)
            {
                _detailObtainTitleLabel.Location = new Float2(pad, illustH + 244f);
                _detailObtainTitleLabel.Size = new Float2(200f, 24f);
            }

            if (_detailPlaceKeyLabel != null)
            {
                _detailPlaceKeyLabel.Location = new Float2(pad, illustH + 282f);
                _detailPlaceKeyLabel.Size = new Float2(120f, 20f);
            }

            if (_detailPlaceValueLabel != null)
            {
                _detailPlaceValueLabel.Location = new Float2(pad + 152f, illustH + 282f);
                _detailPlaceValueLabel.Size = new Float2(200f, 20f);
            }

            if (_detailMethodKeyLabel != null)
            {
                _detailMethodKeyLabel.Location = new Float2(pad, illustH + 314f);
                _detailMethodKeyLabel.Size = new Float2(120f, 20f);
            }

            if (_detailMethodValueLabel != null)
            {
                _detailMethodValueLabel.Location = new Float2(pad + 152f, illustH + 314f);
                _detailMethodValueLabel.Size = new Float2(200f, 20f);
            }

            if (_detailDividerBottom != null)
            {
                _detailDividerBottom.Location = new Float2(pad, illustH + 350f);
                _detailDividerBottom.Width = innerW;
            }

            if (_detailProgressKeyLabel != null)
            {
                _detailProgressKeyLabel.Location = new Float2(pad, illustH + 366f);
                _detailProgressKeyLabel.Size = new Float2(120f, 20f);
            }

            if (_detailProgressValueLabel != null)
            {
                _detailProgressValueLabel.Location = new Float2(pad + 152f, illustH + 366f);
                _detailProgressValueLabel.Size = new Float2(200f, 20f);
            }

            if (_detailProgressBar != null)
            {
                _detailProgressBar.Location = new Float2(pad, illustH + 396f);
                _detailProgressBar.Size = new Float2(innerW, 8f);
            }

            if (_detailProgressCaptionLabel != null)
            {
                _detailProgressCaptionLabel.Location = new Float2(pad, illustH + 416f);
                _detailProgressCaptionLabel.Size = new Float2(innerW, 20f);
            }
        }

        #endregion

        /// <inheritdoc />
        public void RefreshLayout()
        {
            _screenSize = new Float2(Width, Height);
            if (_screenSize.X <= 0f || _screenSize.Y <= 0f)
            {
                _screenSize = FlaxEngine.Screen.Size;
            }

            ApplyLayout();
        }
    }
}

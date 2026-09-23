using System;
using System.Collections.Generic;
using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.Ink.Components;
using HundunWorld.Game.UI.StyleSystem;

namespace HundunWorld.Game.UI.Ink.Pages.Activities
{
    /// <summary>
    /// 活动页面（dom-id: nav-activities）。
    /// 逐条落地设计稿 hundun-yy-ui/pages/menu-activities.html：
    /// 左侧导航栏（240px，Logo / 七个导航项 / 版本页脚）→ 顶部信息栏（60px）→
    /// 精选活动横幅 → 内容分栏（左栏活动列表：标签栏 + 六个活动条目；右栏活动详情面板）。
    /// 颜色与字体一律取自 <see cref="InkWashTheme"/> 设计 Token（无硬编码色值），
    /// 导航出口经 <see cref="NavigationRequested"/> 交由 <see cref="InkPageRouter"/> 统一处理。
    /// </summary>
    public class MenuActivitiesPage : ContainerControl, IInkPage
    {
        // ==================================================================
        // 布局常量（对应设计稿参考分辨率）
        // ==================================================================

        /// <summary>左侧导航宽度（设计 240px）</summary>
        private const float SidebarWidth = 240f;

        /// <summary>顶部信息栏高度（设计 60px）</summary>
        private const float TopBarHeight = 60f;

        /// <summary>内容区内边距（设计 --ink-space-5 = 24px）</summary>
        private const float ContentPadding = 24f;

        /// <summary>区块间距（设计 --ink-space-5 = 24px）</summary>
        private const float SectionGap = 24f;

        /// <summary>精选活动横幅高度（设计 min-height: 160px）</summary>
        private const float BannerHeight = 160f;

        /// <summary>标签栏高度</summary>
        private const float TabBarHeight = 44f;

        /// <summary>活动条目高度</summary>
        private const float EntryHeight = 84f;

        /// <summary>活动条目间距（设计 --ink-space-1）</summary>
        private const float EntryGap = 4f;

        /// <summary>活动列表左右留白（设计 .activity-list padding --ink-space-2）</summary>
        private const float ListPadding = 8f;

        /// <summary>左栏（活动列表）占内容宽度比例（设计 flex: 0 0 60%）</summary>
        private const float ListColumnRatio = 0.60f;

        /// <summary>左侧导航项高度（设计 padding 16px 32px + 20px 图标）</summary>
        private const float NavRowHeight = 52f;

        /// <summary>导航项左侧激活竖线宽度（设计 border-left: 3px）</summary>
        private const float NavActiveBarWidth = 3f;

        /// <summary>详情面板内边距（设计 .detail-panel-content padding --ink-space-5）</summary>
        private const float DetailPadding = 24f;

        /// <summary>详情奖励行高度</summary>
        private const float DetailRewardRowHeight = 44f;

        /// <summary>详情奖励行间距（设计 --ink-space-2）</summary>
        private const float DetailRewardRowGap = 8f;

        /// <summary>详情奖励行区域顶部 Y</summary>
        private const float DetailRewardTop = 340f;

        // ==================================================================
        // 设计稿文案常量
        // ==================================================================

        private const string BrandTitleText = "混沌世界";
        private const string BrandSubText = "HUNDUN WORLD";
        private const string SidebarFooterText = "v2.0.1 · 江湖公测";
        private const string BannerLabelText = "精选活动 · FEATURED";
        private const string BannerTitleText = "江湖盛会";
        private const string BannerSubtitleText = "限时活动 · 武林大会";
        private const string BannerCountdownLabelText = "剩余";
        private const string BannerCountdownDaysText = "3天";
        private const string BannerCountdownTimerText = "14:23:08";
        private const string BannerDecorationText = "武";
        private const string BannerActionText = "立即参与";
        private const string PlayerNameText = "江湖过客";
        private const string PlayerLevelText = "Lv. 42";
        private const string CurrencyCoinText = "铜钱";
        private const string CurrencyCoinValueText = "12,450";
        private const string CurrencyIngotText = "元宝";
        private const string CurrencyIngotValueText = "328";
        private const string TimeText = "戌时 三刻";
        private const string SectionTimeText = "活动时间";
        private const string SectionDescText = "活动简介";
        private const string SectionRequirementText = "参与条件";
        private const string SectionRewardText = "活动奖励";
        private const string ProgressCaptionText = "参与进度";
        private const string RewardQtyLabelText = "数量";
        private const string ParticipateText = "参与活动";
        private const string RulesLinkText = "查看规则";
        private const string BackText = "返回战斗";

        /// <summary>标签栏文案（对应 tab-bar 四项）。</summary>
        private static readonly string[] TabNames = { "全部", "进行中", "即将开始", "已结束" };

        /// <summary>左侧导航文案（对应 .nav-item 七项）。</summary>
        private static readonly string[] NavLabels = { "不肝", "抽卡", "活动", "战令", "属性", "奇珍阁", "设置" };

        /// <summary>左侧导航图标字形（替代设计稿 lucide 图标）。</summary>
        private static readonly string[] NavGlyphs = { "逸", "签", "斗", "令", "属", "珍", "设" };

        /// <summary>左侧导航跳转目标（“活动”指向本页，无跳转）。</summary>
        private static readonly string[] NavTargets =
        {
            InkPageDomIds.NavCasualMode,
            InkPageDomIds.NavGacha,
            InkPageDomIds.NavActivities,
            InkPageDomIds.NavBattlePass,
            InkPageDomIds.NavCharacterV2,
            InkPageDomIds.NavShopRare,
            InkPageDomIds.NavSettings,
        };

        // ==================================================================
        // 活动数据规格（设计稿静态数据）
        // ==================================================================

        /// <summary>条目奖励预览图标规格。</summary>
        private sealed class RewardPreview
        {
            /// <summary>图标字形</summary>
            public string Glyph;

            /// <summary>品质</summary>
            public InkWashTheme.InkQuality Quality;
        }

        /// <summary>详情奖励条目规格。</summary>
        private sealed class DetailReward
        {
            /// <summary>奖励名称</summary>
            public string Name;

            /// <summary>奖励数量（含 × 前缀）</summary>
            public string Quantity;

            /// <summary>图标字形</summary>
            public string Glyph;

            /// <summary>品质</summary>
            public InkWashTheme.InkQuality Quality;
        }

        /// <summary>单个活动条目规格（对应 .activity-entry 及其详情面板内容）。</summary>
        private sealed class ActivitySpec
        {
            public string Name;
            public string StatusText;
            public string TimeGlyph;
            public string TimeText;
            public bool Urgent;
            public float Progress;
            public InkBarFillVariant BarVariant;
            public string ProgressCaption;
            public string ProgressValueText;
            public string IconGlyph;
            public Color IconAccent;
            public RewardPreview[] Preview;
            public string MoreText;
            public bool Ended;
            public string[] Badges;
            public string DetailTime;
            public string Description;
            public string[] Requirements;
            public DetailReward[] Rewards;
            public string Rules;
        }

        /// <summary>设计稿六个活动条目。</summary>
        private static readonly ActivitySpec[] Activities =
        {
            new ActivitySpec
            {
                Name = "武林大会",
                StatusText = "进行中",
                TimeGlyph = "时",
                TimeText = "3天剩余",
                Progress = 0.60f,
                BarVariant = InkBarFillVariant.Gold,
                ProgressCaption = "参与进度",
                ProgressValueText = "60%",
                IconGlyph = "剑",
                IconAccent = InkWashTheme.VermilionBright,
                Preview = new[]
                {
                    new RewardPreview { Glyph = "宝", Quality = InkWashTheme.InkQuality.Epic },
                    new RewardPreview { Glyph = "卷", Quality = InkWashTheme.InkQuality.Rare },
                    new RewardPreview { Glyph = "冠", Quality = InkWashTheme.InkQuality.Legendary },
                },
                MoreText = "+2",
                Badges = new[] { "限时", "PVP" },
                DetailTime = "2026.07.08 — 2026.07.15",
                Description = "江湖各路豪杰齐聚一堂，比武论剑，争夺武林盟主之位。每日参与可获得丰厚奖励，排名前列者更可得绝世武学秘籍与稀有装备。此乃江湖一年一度之盛事，切莫错过。",
                Requirements = new[] { "等级 ≥ 30", "战力 ≥ 15000" },
                Rewards = new[]
                {
                    new DetailReward { Name = "元宝", Quantity = "×500", Glyph = "宝", Quality = InkWashTheme.InkQuality.Epic },
                    new DetailReward { Name = "武林秘籍", Quantity = "×1", Glyph = "卷", Quality = InkWashTheme.InkQuality.Legendary },
                    new DetailReward { Name = "铜钱", Quantity = "×5000", Glyph = "钱", Quality = InkWashTheme.InkQuality.Common },
                    new DetailReward { Name = "稀有装备", Quantity = "×1", Glyph = "盾", Quality = InkWashTheme.InkQuality.Rare },
                },
                Rules = "每日可挑战三次，胜场积分记入排行榜；活动结束后按排名发放奖励，积分相同者以战力高者优先。",
            },
            new ActivitySpec
            {
                Name = "灯会寻宝",
                StatusText = "进行中",
                TimeGlyph = "时",
                TimeText = "5天剩余",
                Progress = 0.35f,
                BarVariant = InkBarFillVariant.Jade,
                ProgressCaption = "参与进度",
                ProgressValueText = "35%",
                IconGlyph = "灯",
                IconAccent = InkWashTheme.GoldBright,
                Preview = new[]
                {
                    new RewardPreview { Glyph = "礼", Quality = InkWashTheme.InkQuality.Uncommon },
                    new RewardPreview { Glyph = "宝", Quality = InkWashTheme.InkQuality.Epic },
                },
                MoreText = "+1",
                Badges = new[] { "限时", "寻宝" },
                DetailTime = "2026.07.06 — 2026.07.11",
                Description = "元宵灯会，灯火辉煌，藏有无数奇珍异宝。收集灯谜碎片，兑换珍稀奖励。",
                Requirements = new[] { "等级 ≥ 20", "战力 ≥ 8000" },
                Rewards = new[]
                {
                    new DetailReward { Name = "灯谜碎片", Quantity = "×10", Glyph = "谜", Quality = InkWashTheme.InkQuality.Uncommon },
                    new DetailReward { Name = "元宝", Quantity = "×200", Glyph = "宝", Quality = InkWashTheme.InkQuality.Epic },
                    new DetailReward { Name = "铜钱", Quantity = "×3000", Glyph = "钱", Quality = InkWashTheme.InkQuality.Common },
                    new DetailReward { Name = "花灯礼盒", Quantity = "×1", Glyph = "灯", Quality = InkWashTheme.InkQuality.Rare },
                },
                Rules = "每日可点亮三盏花灯，点亮后随机获得灯谜碎片；集齐十枚碎片可兑换花灯礼盒。",
            },
            new ActivitySpec
            {
                Name = "诗词大会",
                StatusText = "即将开始",
                TimeGlyph = "漏",
                TimeText = "1天后开始",
                Progress = 0f,
                BarVariant = InkBarFillVariant.Gold,
                ProgressCaption = "未开始",
                ProgressValueText = "—",
                IconGlyph = "书",
                IconAccent = InkWashTheme.Info,
                Preview = new[]
                {
                    new RewardPreview { Glyph = "卷", Quality = InkWashTheme.InkQuality.Rare },
                    new RewardPreview { Glyph = "书", Quality = InkWashTheme.InkQuality.Epic },
                },
                MoreText = string.Empty,
                Badges = new[] { "限时", "文会" },
                DetailTime = "2026.07.09 — 2026.07.13",
                Description = "文人墨客齐聚，以诗会友。答对诗词题目，获得文采值与丰厚奖励。",
                Requirements = new[] { "等级 ≥ 25", "文采 ≥ 500" },
                Rewards = new[]
                {
                    new DetailReward { Name = "诗词残卷", Quantity = "×3", Glyph = "卷", Quality = InkWashTheme.InkQuality.Rare },
                    new DetailReward { Name = "文采值", Quantity = "×800", Glyph = "文", Quality = InkWashTheme.InkQuality.Uncommon },
                    new DetailReward { Name = "铜钱", Quantity = "×2000", Glyph = "钱", Quality = InkWashTheme.InkQuality.Common },
                    new DetailReward { Name = "御笔亲题", Quantity = "×1", Glyph = "书", Quality = InkWashTheme.InkQuality.Epic },
                },
                Rules = "活动开启后每日可答题五道，答对越多文采值越高；累计文采值可兑换诗词礼盒。",
            },
            new ActivitySpec
            {
                Name = "帮派攻防",
                StatusText = "进行中",
                TimeGlyph = "刻",
                TimeText = "12小时剩余",
                Urgent = true,
                Progress = 0.80f,
                BarVariant = InkBarFillVariant.Vermilion,
                ProgressCaption = "参与进度",
                ProgressValueText = "80%",
                IconGlyph = "盾",
                IconAccent = InkWashTheme.JadeBright,
                Preview = new[]
                {
                    new RewardPreview { Glyph = "盾", Quality = InkWashTheme.InkQuality.Uncommon },
                    new RewardPreview { Glyph = "杯", Quality = InkWashTheme.InkQuality.Epic },
                },
                MoreText = "+3",
                Badges = new[] { "帮派", "PVP" },
                DetailTime = "2026.07.08 — 2026.07.08",
                Description = "帮派之间的攻防战，守护帮派荣耀，夺取敌方资源。",
                Requirements = new[] { "等级 ≥ 30", "已加入帮派" },
                Rewards = new[]
                {
                    new DetailReward { Name = "帮派贡献", Quantity = "×500", Glyph = "贡", Quality = InkWashTheme.InkQuality.Uncommon },
                    new DetailReward { Name = "守城功勋", Quantity = "×1", Glyph = "杯", Quality = InkWashTheme.InkQuality.Epic },
                    new DetailReward { Name = "铜钱", Quantity = "×4000", Glyph = "钱", Quality = InkWashTheme.InkQuality.Common },
                    new DetailReward { Name = "玄铁护腕", Quantity = "×1", Glyph = "腕", Quality = InkWashTheme.InkQuality.Rare },
                },
                Rules = "每晚八点开启攻防战，帮派成员共同守城；守城成功可夺取敌方资源，失败则损失部分资源。",
            },
            new ActivitySpec
            {
                Name = "寻宝奇缘",
                StatusText = "进行中",
                TimeGlyph = "时",
                TimeText = "6天剩余",
                Progress = 0.15f,
                BarVariant = InkBarFillVariant.Jade,
                ProgressCaption = "参与进度",
                ProgressValueText = "15%",
                IconGlyph = "匣",
                IconAccent = InkWashTheme.GoldPrimary,
                Preview = new[]
                {
                    new RewardPreview { Glyph = "钱", Quality = InkWashTheme.InkQuality.Common },
                    new RewardPreview { Glyph = "钥", Quality = InkWashTheme.InkQuality.Rare },
                },
                MoreText = "+2",
                Badges = new[] { "限时", "寻宝" },
                DetailTime = "2026.07.05 — 2026.07.11",
                Description = "探索神秘藏宝图，寻找失落的宝藏，获得珍稀物品。",
                Requirements = new[] { "等级 ≥ 20", "战力 ≥ 6000" },
                Rewards = new[]
                {
                    new DetailReward { Name = "铜钱", Quantity = "×8000", Glyph = "钱", Quality = InkWashTheme.InkQuality.Common },
                    new DetailReward { Name = "秘境钥匙", Quantity = "×2", Glyph = "钥", Quality = InkWashTheme.InkQuality.Rare },
                    new DetailReward { Name = "藏宝图残页", Quantity = "×5", Glyph = "图", Quality = InkWashTheme.InkQuality.Uncommon },
                    new DetailReward { Name = "失落宝箱", Quantity = "×1", Glyph = "匣", Quality = InkWashTheme.InkQuality.Epic },
                },
                Rules = "每日可寻宝五次，藏宝图碎片随机掉落；集齐碎片可开启秘境宝箱。",
            },
            new ActivitySpec
            {
                Name = "江湖悬赏",
                StatusText = "已结束",
                TimeGlyph = "成",
                TimeText = "已结束",
                Progress = 1f,
                BarVariant = InkBarFillVariant.Gold,
                ProgressCaption = "已完成",
                ProgressValueText = "100%",
                IconGlyph = "勋",
                IconAccent = InkWashTheme.PaperDark,
                Preview = new[]
                {
                    new RewardPreview { Glyph = "钱", Quality = InkWashTheme.InkQuality.Common },
                    new RewardPreview { Glyph = "礼", Quality = InkWashTheme.InkQuality.Uncommon },
                },
                MoreText = string.Empty,
                Ended = true,
                Badges = new[] { "已结束", "悬赏" },
                DetailTime = "2026.06.20 — 2026.07.01",
                Description = "江湖悬赏已结束，感谢各位侠士的参与。",
                Requirements = new[] { "等级 ≥ 15", "战力 ≥ 3000" },
                Rewards = new[]
                {
                    new DetailReward { Name = "铜钱", Quantity = "×2500", Glyph = "钱", Quality = InkWashTheme.InkQuality.Common },
                    new DetailReward { Name = "悬赏礼盒", Quantity = "×1", Glyph = "礼", Quality = InkWashTheme.InkQuality.Uncommon },
                    new DetailReward { Name = "侠义值", Quantity = "×300", Glyph = "侠", Quality = InkWashTheme.InkQuality.Common },
                    new DetailReward { Name = "江湖令", Quantity = "×1", Glyph = "令", Quality = InkWashTheme.InkQuality.Rare },
                },
                Rules = "悬赏任务已于活动结束后关闭，未领取的奖励将通过邮件补发。",
            },
        };

        // ==================================================================
        // 页面字段
        // ==================================================================

        private Float2 _screenSize;

        private InkBackgroundLayer _backgroundLayer;
        private InkVignette _vignette;

        // 左侧导航
        private InkPanel _leftNav;
        private Label _logoText;
        private Label _logoSub;
        private InkDivider _logoDivider;
        private List<NavRow> _navRows;
        private InkDivider _navFooterDivider;
        private Label _navFooterText;

        // 主区域 / 顶部信息栏
        private InkPanel _mainArea;
        private InkPanel _topBar;
        private InkButton _backButton;
        private ContainerControl _playerAvatar;
        private Label _playerNameLabel;
        private Label _playerLevelLabel;
        private Label _currencyCoinGlyph;
        private Label _currencyCoinLabel;
        private Label _currencyCoinValue;
        private Label _currencyIngotGlyph;
        private Label _currencyIngotLabel;
        private Label _currencyIngotValue;
        private InkDividerVertical _currencyDivider;
        private Label _timeGlyph;
        private Label _timeLabel;

        // 精选活动横幅
        private InkBrushBorder _featuredBanner;
        private InkSplash _bannerSplashGold;
        private InkSplash _bannerSplashVermilion;
        private Label _bannerDecoration;
        private InkDivider _bannerLabelRule;
        private Label _bannerLabel;
        private Label _bannerTitle;
        private Label _bannerSubtitle;
        private Label _countdownLabel;
        private Label _countdownDays;
        private Label _countdownTimer;
        private InkButton _bannerActionButton;

        // 左栏：活动列表
        private InkBrushBorder _activityListPanel;
        private InkCornerDeco _listCorners;
        private List<ActivityTab> _tabs;
        private InkDivider _tabBarDivider;
        private List<ActivityEntry> _entries;

        // 右栏：活动详情
        private InkBrushBorder _detailPanel;
        private InkCornerDeco _detailCorners;
        private InkSplash _detailSplash;
        private Label _detailTitle;
        private InkTag _detailBadgePrimary;
        private InkTag _detailBadgeSecondary;
        private InkButton _detailCloseButton;
        private InkDivider _detailHeaderDivider;
        private Label _sectionTimeLabel;
        private Label _sectionTimeGlyph;
        private Label _detailTime;
        private Label _sectionDescLabel;
        private Label _detailDescription;
        private Label _sectionRequirementLabel;
        private RequirementTag[] _requirementTags;
        private Label _sectionRewardLabel;
        private DetailRewardRow[] _rewardRows;
        private InkButton _detailParticipateButton;
        private InkButton _detailRulesButton;
        private Label _rulesGlyph;
        private Label _detailRulesText;
        private Label _detailHint;
        private Label _detailArrowGlyph;

        private int _selectedIndex;
        private int _activeTab;
        private bool _detailVisible = true;

        /// <summary>
        /// 导航请求事件。由 <c>MainUIManager.HookNavigationRequested</c> 反射订阅后转交 <see cref="InkPageRouter"/>。
        /// </summary>
        public event Action<string> NavigationRequested;

        /// <summary>
        /// 构造函数：构建全部区块并应用布局。
        /// </summary>
        public MenuActivitiesPage()
        {
            _screenSize = FlaxEngine.Screen.Size;
            if (_screenSize.X <= 0f || _screenSize.Y <= 0f)
            {
                _screenSize = new Float2(UILayout.RefWidth, UILayout.RefHeight);
            }

            AnchorPreset = AnchorPresets.StretchAll;
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
            AutoFocus = false;
            Location = Float2.Zero;
            Size = _screenSize;

            try
            {
                BuildBackground();
                BuildLeftNav();
                BuildMainArea();
                BuildTopBar();
                BuildFeaturedBanner();
                BuildActivityList();
                BuildDetailPanel();

                ApplyLayout();
                RefreshAllData();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[MenuActivitiesPage] 初始化失败: {ex.Message}");
            }
        }

        // ==================================================================
        // 通用辅助（一律使用既有设计 Token 派生颜色，避免硬编码色值）
        // ==================================================================

        /// <summary>以既有 Token 派生同色系的半透明色。</summary>
        private static Color Alpha(Color token, float alpha)
        {
            return new Color(token.R, token.G, token.B, alpha);
        }

        /// <summary>创建统一样式的文本控件。</summary>
        private static Label NewLabel(
            ContainerControl parent,
            string text,
            InkWashTheme.FontRole role,
            float size,
            Color color,
            TextAlignment alignment = TextAlignment.Near)
        {
            return new Label
            {
                Text = text,
                Font = InkRenderHelper.GetFontRef(role, size),
                TextColor = color,
                HorizontalAlignment = alignment,
                VerticalAlignment = TextAlignment.Center,
                Parent = parent,
            };
        }

        /// <summary>统一摆放控件的位置与尺寸（容器不会自动布局）。</summary>
        private static void Place(Control control, float x, float y, float width, float height)
        {
            if (control == null)
                return;
            control.Location = new Float2(x, y);
            control.Size = new Float2(Mathf.Max(0f, width), Mathf.Max(0f, height));
        }

        // ==================================================================
        // 背景层
        // ==================================================================

        /// <summary>构建全局水墨背景层与暗角晕影（对应 .ink-bg-layer / .ink-vignette）。</summary>
        private void BuildBackground()
        {
            _backgroundLayer = new InkBackgroundLayer
            {
                DrawBaseFill = true,
                AnchorPreset = AnchorPresets.StretchAll,
                Parent = this,
            };

            _vignette = new InkVignette
            {
                AnchorPreset = AnchorPresets.StretchAll,
                Parent = this,
            };
        }

        // ==================================================================
        // 左侧导航
        // ==================================================================

        /// <summary>构建左侧导航栏：品牌 Logo、七个导航项、版本页脚。</summary>
        private void BuildLeftNav()
        {
            _leftNav = new InkPanel
            {
                BackgroundColor = InkWashTheme.BaseDefault,
                Parent = this,
            };

            _logoText = NewLabel(_leftNav, BrandTitleText, InkWashTheme.FontRole.Display, 22f, InkWashTheme.GoldBright, TextAlignment.Center);
            _logoSub = NewLabel(_leftNav, BrandSubText, InkWashTheme.FontRole.Body, 11f, InkWashTheme.PaperDark, TextAlignment.Center);
            _logoDivider = new InkDivider { Parent = _leftNav };

            _navRows = new List<NavRow>();
            for (int i = 0; i < NavLabels.Length; i++)
            {
                bool active = NavTargets[i] == InkPageDomIds.NavActivities;
                NavRow row = new NavRow(_leftNav, NavGlyphs[i], NavLabels[i], active);
                row.Activated += OnNavRowActivated;
                _navRows.Add(row);
            }

            _navFooterDivider = new InkDivider { Parent = _leftNav };
            _navFooterText = NewLabel(_leftNav, SidebarFooterText, InkWashTheme.FontRole.Body, 11f, InkWashTheme.TextTertiary, TextAlignment.Center);
        }

        // ==================================================================
        // 主区域 / 顶部信息栏
        // ==================================================================

        /// <summary>构建右侧主区域容器。</summary>
        private void BuildMainArea()
        {
            _mainArea = new InkPanel
            {
                BackgroundColor = Color.Transparent,
                Parent = this,
            };
        }

        /// <summary>构建顶部信息栏：返回、头像、玩家信息、货币与时辰。</summary>
        private void BuildTopBar()
        {
            _topBar = new InkPanel
            {
                Height = TopBarHeight,
                BackgroundColor = InkWashTheme.Panel,
                Parent = _mainArea,
            };

            _backButton = new InkButton
            {
                Text = "←",
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Md,
                Parent = _topBar,
            };
            _backButton.Clicked += OnBackClicked;

            _playerAvatar = new InkPanel
            {
                Size = new Float2(36f, 36f),
                BackgroundColor = InkWashTheme.BaseTertiary,
                Parent = _topBar,
            };
            NewLabel(_playerAvatar, "客", InkWashTheme.FontRole.Display, 16f, InkWashTheme.PaperBright, TextAlignment.Center).AnchorPreset = AnchorPresets.StretchAll;

            _playerNameLabel = NewLabel(_topBar, PlayerNameText, InkWashTheme.FontRole.Heading, 14f, InkWashTheme.TextDefault);
            _playerLevelLabel = NewLabel(_topBar, PlayerLevelText, InkWashTheme.FontRole.Body, 11f, InkWashTheme.TextTertiary);

            _currencyCoinGlyph = NewLabel(_topBar, "钱", InkWashTheme.FontRole.Body, 12f, InkWashTheme.GoldPrimary, TextAlignment.Center);
            _currencyCoinLabel = NewLabel(_topBar, CurrencyCoinText, InkWashTheme.FontRole.Body, 12f, InkWashTheme.TextTertiary);
            _currencyCoinValue = NewLabel(_topBar, CurrencyCoinValueText, InkWashTheme.FontRole.Number, 12f, InkWashTheme.TextDefault);
            _currencyIngotGlyph = NewLabel(_topBar, "宝", InkWashTheme.FontRole.Body, 12f, InkWashTheme.VermilionBright, TextAlignment.Center);
            _currencyIngotLabel = NewLabel(_topBar, CurrencyIngotText, InkWashTheme.FontRole.Body, 12f, InkWashTheme.TextTertiary);
            _currencyIngotValue = NewLabel(_topBar, CurrencyIngotValueText, InkWashTheme.FontRole.Number, 12f, InkWashTheme.TextDefault);

            _currencyDivider = new InkDividerVertical { Parent = _topBar };
            _timeGlyph = NewLabel(_topBar, "时", InkWashTheme.FontRole.Body, 12f, InkWashTheme.Info, TextAlignment.Center);
            _timeLabel = NewLabel(_topBar, TimeText, InkWashTheme.FontRole.Heading, 13f, InkWashTheme.Info);
        }

        // ==================================================================
        // 精选活动横幅
        // ==================================================================

        /// <summary>构建精选活动横幅：光晕、书法水印、标题栏、倒计时与立即参与。</summary>
        private void BuildFeaturedBanner()
        {
            _featuredBanner = new InkBrushBorder
            {
                Parent = _mainArea,
            };

            _bannerSplashGold = new InkSplash
            {
                Variant = InkSplashVariant.Normal,
                Opacity = 0.5f,
                Parent = _featuredBanner,
            };
            _bannerSplashVermilion = new InkSplash
            {
                Variant = InkSplashVariant.Vermilion,
                Opacity = 0.5f,
                Parent = _featuredBanner,
            };

            _bannerDecoration = NewLabel(_featuredBanner, BannerDecorationText, InkWashTheme.FontRole.Display, 120f, Alpha(InkWashTheme.GoldPrimary, 0.05f), TextAlignment.Center);

            _bannerLabelRule = new InkDivider { Parent = _featuredBanner };
            _bannerLabel = NewLabel(_featuredBanner, BannerLabelText, InkWashTheme.FontRole.Body, 11f, InkWashTheme.GoldPrimary);
            _bannerTitle = NewLabel(_featuredBanner, BannerTitleText, InkWashTheme.FontRole.Display, 36f, InkWashTheme.GoldBright);
            _bannerSubtitle = NewLabel(_featuredBanner, BannerSubtitleText, InkWashTheme.FontRole.Body, 13f, InkWashTheme.PaperAged);

            _countdownLabel = NewLabel(_featuredBanner, BannerCountdownLabelText, InkWashTheme.FontRole.Body, 11f, InkWashTheme.PaperDark);
            _countdownDays = NewLabel(_featuredBanner, BannerCountdownDaysText, InkWashTheme.FontRole.Number, 18f, InkWashTheme.VermilionPrimary);
            _countdownTimer = NewLabel(_featuredBanner, BannerCountdownTimerText, InkWashTheme.FontRole.Number, 15f, InkWashTheme.VermilionBright);

            _bannerActionButton = new InkButton
            {
                Text = BannerActionText,
                Variant = InkButtonVariant.Primary,
                ButtonSize = InkButtonSize.Lg,
                Parent = _featuredBanner,
            };
            _bannerActionButton.Clicked += OnBannerActionClicked;
        }

        // ==================================================================
        // 左栏：活动列表
        // ==================================================================

        /// <summary>构建活动列表面板：标签栏 + 六个活动条目。</summary>
        private void BuildActivityList()
        {
            _activityListPanel = new InkBrushBorder
            {
                Parent = _mainArea,
            };
            _listCorners = new InkCornerDeco { Parent = _activityListPanel };

            _tabs = new List<ActivityTab>();
            for (int i = 0; i < TabNames.Length; i++)
            {
                int index = i;
                ActivityTab tab = new ActivityTab(_activityListPanel, TabNames[i], CountForTab(i), i == 0);
                tab.Clicked += OnTabClicked;
                _tabs.Add(tab);
                // index 仅用于闭包可读性，实际点击由 ActivityTab.TabIndex 回传
                if (index < 0)
                {
                    FlaxEngine.Debug.LogWarning("[MenuActivitiesPage] 标签索引异常");
                }
            }

            _tabBarDivider = new InkDivider { Parent = _activityListPanel };

            _entries = new List<ActivityEntry>();
            for (int i = 0; i < Activities.Length; i++)
            {
                ActivityEntry entry = new ActivityEntry(_activityListPanel, i, Activities[i], i == 0);
                entry.Clicked += OnEntryClicked;
                _entries.Add(entry);
            }
        }

        /// <summary>统计某标签下的活动数量（对应设计稿 tab-count）。</summary>
        private static int CountForTab(int tabIndex)
        {
            if (tabIndex <= 0)
                return Activities.Length;

            int count = 0;
            string status = TabNames[tabIndex];
            for (int i = 0; i < Activities.Length; i++)
            {
                if (Activities[i].StatusText == status)
                    count++;
            }
            return count;
        }

        // ==================================================================
        // 右栏：活动详情
        // ==================================================================

        /// <summary>构建活动详情面板：标题/徽章、活动时间、简介、参与条件、奖励与底部操作。</summary>
        private void BuildDetailPanel()
        {
            _detailPanel = new InkBrushBorder
            {
                Parent = _mainArea,
            };
            _detailCorners = new InkCornerDeco { Parent = _detailPanel };
            _detailSplash = new InkSplash
            {
                Variant = InkSplashVariant.Vermilion,
                Opacity = 0.6f,
                Parent = _detailPanel,
            };

            _detailTitle = NewLabel(_detailPanel, Activities[0].Name, InkWashTheme.FontRole.Heading, 22f, InkWashTheme.GoldBright);
            _detailBadgePrimary = new InkTag
            {
                Text = Activities[0].Badges[0],
                TagVariant = InkTagVariant.Vermilion,
                Parent = _detailPanel,
            };
            _detailBadgeSecondary = new InkTag
            {
                Text = Activities[0].Badges[1],
                TagVariant = InkTagVariant.Brand,
                Parent = _detailPanel,
            };

            _detailCloseButton = new InkButton
            {
                Text = "×",
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Sm,
                Parent = _detailPanel,
            };
            _detailCloseButton.Clicked += OnDetailCloseClicked;

            _detailHeaderDivider = new InkDivider { Parent = _detailPanel };

            _sectionTimeLabel = NewLabel(_detailPanel, SectionTimeText, InkWashTheme.FontRole.Body, 11f, InkWashTheme.GoldPrimary);
            _sectionTimeGlyph = NewLabel(_detailPanel, "历", InkWashTheme.FontRole.Body, 11f, InkWashTheme.Info, TextAlignment.Center);
            _detailTime = NewLabel(_detailPanel, Activities[0].DetailTime, InkWashTheme.FontRole.Number, 13f, InkWashTheme.PaperAged);

            _sectionDescLabel = NewLabel(_detailPanel, SectionDescText, InkWashTheme.FontRole.Body, 11f, InkWashTheme.GoldPrimary);
            _detailDescription = NewLabel(_detailPanel, Activities[0].Description, InkWashTheme.FontRole.Body, 13f, InkWashTheme.PaperAged);

            _sectionRequirementLabel = NewLabel(_detailPanel, SectionRequirementText, InkWashTheme.FontRole.Body, 11f, InkWashTheme.GoldPrimary);
            _requirementTags = new RequirementTag[2];
            for (int i = 0; i < _requirementTags.Length; i++)
            {
                _requirementTags[i] = new RequirementTag(_detailPanel, Activities[0].Requirements[i]);
            }

            _sectionRewardLabel = NewLabel(_detailPanel, SectionRewardText, InkWashTheme.FontRole.Body, 11f, InkWashTheme.GoldPrimary);
            _rewardRows = new DetailRewardRow[4];
            for (int i = 0; i < _rewardRows.Length; i++)
            {
                _rewardRows[i] = new DetailRewardRow(_detailPanel, Activities[0].Rewards[i]);
            }

            _detailParticipateButton = new InkButton
            {
                Text = ParticipateText,
                Variant = InkButtonVariant.Vermilion,
                ButtonSize = InkButtonSize.Lg,
                Parent = _detailPanel,
            };
            _detailParticipateButton.Clicked += OnParticipateClicked;

            _detailArrowGlyph = NewLabel(_detailPanel, "›", InkWashTheme.FontRole.Body, 14f, InkWashTheme.Info, TextAlignment.Center);
            _rulesGlyph = NewLabel(_detailPanel, "文", InkWashTheme.FontRole.Body, 11f, InkWashTheme.Info, TextAlignment.Center);
            _detailRulesButton = new InkButton
            {
                Text = RulesLinkText,
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Sm,
                Parent = _detailPanel,
            };
            _detailRulesButton.Clicked += OnRulesClicked;

            _detailRulesText = NewLabel(_detailPanel, Activities[0].Rules, InkWashTheme.FontRole.Body, 11f, InkWashTheme.TextTertiary);
            _detailRulesText.Visible = false;
            _detailHint = NewLabel(_detailPanel, string.Empty, InkWashTheme.FontRole.Body, 11f, InkWashTheme.TextTertiary);
        }

        // ==================================================================
        // 布局
        // ==================================================================

        /// <summary>按当前屏幕尺寸重排全部区块。</summary>
        private void ApplyLayout()
        {
            float w = _screenSize.X;
            float h = _screenSize.Y;
            if (w <= 0f)
                w = UILayout.RefWidth;
            if (h <= 0f)
                h = UILayout.RefHeight;

            Place(_backgroundLayer, 0f, 0f, w, h);
            Place(_vignette, 0f, 0f, w, h);

            LayoutSidebar(h);

            float mainW = Mathf.Max(360f, w - SidebarWidth);
            Place(_mainArea, SidebarWidth, 0f, mainW, h);
            LayoutTopBar(mainW);

            float contentTop = TopBarHeight + ContentPadding;
            float contentBottom = Mathf.Max(contentTop + 120f, h - ContentPadding);
            float contentHeight = contentBottom - contentTop;

            LayoutFeaturedBanner(mainW, contentTop);
            LayoutContentSplit(mainW, contentTop + BannerHeight + SectionGap, contentHeight - BannerHeight - SectionGap);
        }

        /// <summary>左侧导航栏布局。</summary>
        private void LayoutSidebar(float height)
        {
            Place(_leftNav, 0f, 0f, SidebarWidth, height);

            Place(_logoText, 0f, 32f, SidebarWidth, 28f);
            Place(_logoSub, 0f, 60f, SidebarWidth, 18f);
            Place(_logoDivider, 16f, 92f, SidebarWidth - 32f, 1f);

            float navTop = 104f;
            for (int i = 0; i < _navRows.Count; i++)
            {
                Place(_navRows[i], 0f, navTop + i * NavRowHeight, SidebarWidth, NavRowHeight);
            }

            float footerTop = Mathf.Max(navTop + _navRows.Count * NavRowHeight + 16f, height - 64f);
            Place(_navFooterDivider, 16f, footerTop, SidebarWidth - 32f, 1f);
            Place(_navFooterText, 0f, footerTop + 12f, SidebarWidth, 18f);
        }

        /// <summary>顶部信息栏布局。</summary>
        private void LayoutTopBar(float mainW)
        {
            Place(_topBar, 0f, 0f, mainW, TopBarHeight);

            float cy = TopBarHeight * 0.5f;
            Place(_backButton, 16f, cy - 18f, 36f, 36f);
            Place(_playerAvatar, 60f, cy - 18f, 36f, 36f);
            Place(_playerNameLabel, 106f, cy - 16f, 180f, 18f);
            Place(_playerLevelLabel, 106f, cy + 2f, 180f, 16f);

            float cursor = mainW - 20f;
            Place(_timeLabel, cursor - 100f, cy - 9f, 100f, 18f);
            cursor -= 100f;
            Place(_timeGlyph, cursor - 16f, cy - 8f, 16f, 16f);
            cursor -= 16f + 12f;
            Place(_currencyDivider, cursor, cy - 10f, 1f, 20f);
            cursor -= 18f;

            Place(_currencyIngotValue, cursor - 56f, cy - 9f, 56f, 18f);
            Place(_currencyIngotLabel, cursor - 94f, cy - 9f, 34f, 18f);
            cursor -= 94f;
            Place(_currencyIngotGlyph, cursor - 18f, cy - 8f, 18f, 16f);
            cursor -= 18f + 20f;

            Place(_currencyCoinValue, cursor - 56f, cy - 9f, 56f, 18f);
            Place(_currencyCoinLabel, cursor - 94f, cy - 9f, 34f, 18f);
            cursor -= 94f;
            Place(_currencyCoinGlyph, cursor - 18f, cy - 8f, 18f, 16f);
        }

        /// <summary>精选活动横幅布局。</summary>
        private void LayoutFeaturedBanner(float mainW, float top)
        {
            float bannerW = Mathf.Max(320f, mainW - ContentPadding * 2f);
            Place(_featuredBanner, ContentPadding, top, bannerW, BannerHeight);
            Place(_listCorners, 0f, 0f, bannerW, BannerHeight);

            Place(_bannerSplashGold, bannerW - 360f, -120f, 280f, 280f);
            Place(_bannerSplashVermilion, bannerW * 0.1f, 80f, 220f, 220f);
            Place(_bannerDecoration, bannerW * 0.55f, 10f, 200f, 140f);

            Place(_bannerLabelRule, 48f, 31f, 24f, 1f);
            Place(_bannerLabel, 80f, 24f, 320f, 16f);
            Place(_bannerTitle, 80f, 44f, 420f, 46f);
            Place(_bannerSubtitle, 82f, 96f, 420f, 20f);
            Place(_countdownLabel, 82f, 120f, 40f, 18f);
            Place(_countdownDays, 126f, 116f, 44f, 24f);
            Place(_countdownTimer, 172f, 120f, 140f, 18f);
            Place(_bannerActionButton, bannerW - 48f - 160f, 58f, 160f, 44f);
        }

        /// <summary>内容分栏布局：左栏活动列表 + 右栏详情面板。</summary>
        private void LayoutContentSplit(float mainW, float top, float height)
        {
            float availW = Mathf.Max(320f, mainW - ContentPadding * 2f - SectionGap);
            float panelH = Mathf.Max(200f, height - ContentPadding);

            float listW = _detailVisible ? availW * ListColumnRatio : availW;
            Place(_activityListPanel, ContentPadding, top, listW, panelH);
            LayoutActivityList(listW, panelH);

            if (_detailVisible)
            {
                float detailW = Mathf.Max(260f, availW - listW);
                Place(_detailPanel, ContentPadding + listW + SectionGap, top, detailW, panelH);
                LayoutDetailPanel(detailW, panelH);
            }
        }

        /// <summary>活动列表面板内部布局。</summary>
        private void LayoutActivityList(float panelW, float panelH)
        {
            Place(_listCorners, 0f, 0f, panelW, panelH);
            Place(_tabBarDivider, 0f, TabBarHeight - 1f, panelW, 1f);

            float tabW = panelW / Mathf.Max(1f, _tabs.Count);
            for (int i = 0; i < _tabs.Count; i++)
            {
                Place(_tabs[i], i * tabW, 0f, tabW, TabBarHeight);
            }

            float entryW = Mathf.Max(160f, panelW - ListPadding * 2f);
            float y = TabBarHeight + ListPadding;
            for (int i = 0; i < _entries.Count; i++)
            {
                ActivityEntry entry = _entries[i];
                bool visible = entry.MatchesTab(_activeTab);
                entry.Visible = visible;
                if (!visible)
                    continue;

                Place(entry, ListPadding, y, entryW, EntryHeight);
                y += EntryHeight + EntryGap;
            }
        }

        /// <summary>活动详情面板内部布局。</summary>
        private void LayoutDetailPanel(float panelW, float panelH)
        {
            Place(_detailCorners, 0f, 0f, panelW, panelH);
            Place(_detailSplash, panelW - 120f, -60f, 180f, 180f);

            float innerW = Mathf.Max(120f, panelW - DetailPadding * 2f);

            Place(_detailTitle, DetailPadding, 22f, innerW - 60f, 32f);
            Place(_detailBadgePrimary, DetailPadding, 60f, 60f, 22f);
            Place(_detailBadgeSecondary, DetailPadding + 68f, 60f, 60f, 22f);
            Place(_detailCloseButton, panelW - DetailPadding - 28f, 22f, 28f, 28f);
            Place(_detailHeaderDivider, DetailPadding, 92f, innerW, 1f);

            Place(_sectionTimeLabel, DetailPadding, 104f, innerW, 16f);
            Place(_sectionTimeGlyph, DetailPadding, 126f, 16f, 16f);
            Place(_detailTime, DetailPadding + 22f, 124f, innerW - 22f, 18f);

            Place(_sectionDescLabel, DetailPadding, 154f, innerW, 16f);
            Place(_detailDescription, DetailPadding, 176f, innerW, 76f);

            Place(_sectionRequirementLabel, DetailPadding, 262f, innerW, 16f);
            float reqX = DetailPadding;
            for (int i = 0; i < _requirementTags.Length; i++)
            {
                if (_requirementTags[i] == null)
                    continue;
                float reqW = _requirementTags[i].PreferredWidth;
                Place(_requirementTags[i], reqX, 284f, reqW, 22f);
                reqX += reqW + 8f;
            }

            Place(_sectionRewardLabel, DetailPadding, 318f, innerW, 16f);
            for (int i = 0; i < _rewardRows.Length; i++)
            {
                if (_rewardRows[i] == null)
                    continue;
                Place(_rewardRows[i], DetailPadding, DetailRewardTop + i * (DetailRewardRowHeight + DetailRewardRowGap), innerW, DetailRewardRowHeight);
            }

            float rewardsBottom = DetailRewardTop + _rewardRows.Length * (DetailRewardRowHeight + DetailRewardRowGap);
            float footerTop = Mathf.Max(rewardsBottom + 12f, panelH - 116f);
            Place(_detailParticipateButton, DetailPadding, footerTop, innerW, 44f);

            float rulesTop = footerTop + 52f;
            Place(_rulesGlyph, DetailPadding + innerW * 0.5f - 46f, rulesTop + 5f, 14f, 14f);
            Place(_detailRulesButton, DetailPadding + innerW * 0.5f - 30f, rulesTop, 60f, 24f);
            Place(_detailArrowGlyph, DetailPadding + innerW * 0.5f + 30f, rulesTop + 4f, 12f, 16f);

            Place(_detailRulesText, DetailPadding, rulesTop + 28f, innerW, 32f);
            Place(_detailHint, DetailPadding, rulesTop + 62f, innerW, 18f);
        }

        // ==================================================================
        // 数据刷新与交互
        // ==================================================================

        /// <summary>刷新全部活动数据与选中态（默认选中设计稿中的“武林大会”）。</summary>
        private void RefreshAllData()
        {
            _selectedIndex = 0;
            _activeTab = 0;
            ApplySelection();
            ApplyTabFilter(_activeTab);
            _detailVisible = true;
            if (_detailPanel != null)
                _detailPanel.Visible = true;
        }

        /// <summary>把当前选中活动写入详情面板（文案/徽章/条件/奖励/规则）。</summary>
        private void ApplySelection()
        {
            ActivitySpec spec = Activities[_selectedIndex];

            if (_detailTitle != null)
                _detailTitle.Text = spec.Name;

            if (_detailBadgePrimary != null)
                _detailBadgePrimary.Text = spec.Badges.Length > 0 ? spec.Badges[0] : string.Empty;

            if (_detailBadgeSecondary != null)
                _detailBadgeSecondary.Text = spec.Badges.Length > 1 ? spec.Badges[1] : string.Empty;

            if (_detailTime != null)
                _detailTime.Text = spec.DetailTime;

            if (_detailDescription != null)
                _detailDescription.Text = spec.Description;

            for (int i = 0; i < _requirementTags.Length; i++)
            {
                if (_requirementTags[i] == null)
                    continue;
                string text = i < spec.Requirements.Length ? spec.Requirements[i] : string.Empty;
                _requirementTags[i].SetText(text);
            }

            for (int i = 0; i < _rewardRows.Length; i++)
            {
                if (_rewardRows[i] == null)
                    continue;
                if (i < spec.Rewards.Length)
                {
                    _rewardRows[i].Visible = true;
                    _rewardRows[i].Apply(spec.Rewards[i]);
                }
                else
                {
                    _rewardRows[i].Visible = false;
                }
            }

            if (_detailRulesText != null)
            {
                _detailRulesText.Text = spec.Rules;
                _detailRulesText.Visible = false;
            }

            if (_detailHint != null)
                _detailHint.Text = string.Empty;

            if (_detailParticipateButton != null)
                _detailParticipateButton.Visible = !spec.Ended;

            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i] != null)
                    _entries[i].SetSelected(i == _selectedIndex);
            }
        }

        /// <summary>切换标签栏筛选并重排条目。</summary>
        private void ApplyTabFilter(int tabIndex)
        {
            _activeTab = tabIndex;
            for (int i = 0; i < _tabs.Count; i++)
            {
                if (_tabs[i] != null)
                    _tabs[i].SetActive(i == tabIndex);
            }
            LayoutActivityList(_activityListPanel != null ? _activityListPanel.Width : 0f, _activityListPanel != null ? _activityListPanel.Height : 0f);
        }

        /// <summary>选中某个活动条目（并确保详情面板展开）。</summary>
        private void SelectActivity(int index)
        {
            if (index < 0 || index >= Activities.Length)
                return;

            _selectedIndex = index;
            if (!_detailVisible)
            {
                _detailVisible = true;
                if (_detailPanel != null)
                    _detailPanel.Visible = true;
                ApplyLayout();
            }
            ApplySelection();
        }

        /// <summary>收起/展开右栏详情面板（对应设计稿 .detail-close）。</summary>
        private void SetDetailVisible(bool visible)
        {
            _detailVisible = visible;
            if (_detailPanel != null)
                _detailPanel.Visible = visible;
            ApplyLayout();
        }

        // ==================================================================
        // 事件出口
        // ==================================================================

        /// <summary>左侧导航项点击 → 标准导航请求。</summary>
        private void OnNavRowActivated(string target)
        {
            if (string.IsNullOrEmpty(target) || target == InkPageDomIds.NavActivities)
                return;
            NavigationRequested?.Invoke(target);
        }

        /// <summary>返回战斗 HUD（设计隐藏出口 back-hud）。</summary>
        private void OnBackClicked()
        {
            NavigationRequested?.Invoke(InkPageDomIds.BackHud);
        }

        /// <summary>横幅“立即参与”→ 打开精选活动（首条）详情。</summary>
        private void OnBannerActionClicked()
        {
            SelectActivity(0);
        }

        /// <summary>标签栏切换。</summary>
        private void OnTabClicked(int tabIndex)
        {
            ApplyTabFilter(tabIndex);
        }

        /// <summary>活动条目选中。</summary>
        private void OnEntryClicked(int index)
        {
            SelectActivity(index);
        }

        /// <summary>详情面板关闭。</summary>
        private void OnDetailCloseClicked()
        {
            SetDetailVisible(false);
        }

        /// <summary>“查看规则”展开/收起规则说明。</summary>
        private void OnRulesClicked()
        {
            if (_detailRulesText == null)
                return;
            _detailRulesText.Visible = !_detailRulesText.Visible;
        }

        /// <summary>“参与活动”：可参与活动进入备战，否则给出状态提示。</summary>
        private void OnParticipateClicked()
        {
            ActivitySpec spec = Activities[_selectedIndex];
            if (spec.Ended)
            {
                if (_detailHint != null)
                    _detailHint.Text = "活动已结束，奖励已通过邮件补发";
                return;
            }

            if (spec.StatusText == "即将开始")
            {
                if (_detailHint != null)
                    _detailHint.Text = $"活动尚未开始（{spec.TimeText}），敬请期待";
                return;
            }

            NavigationRequested?.Invoke(InkPageDomIds.NavBattlePrep);
        }

        /// <summary>按当前控件尺寸刷新布局（IInkPage 契约）。</summary>
        public void RefreshLayout()
        {
            _screenSize = new Float2(Width, Height);
            ApplyLayout();
        }

        // ==================================================================
        // 私有辅助控件
        // ==================================================================

        /// <summary>
        /// 左侧导航项（对应 .nav-item）：
        /// 图标 + 文案，active 态绘制左侧 3px 鎏金竖线与微金底，hover 态提亮。
        /// </summary>
        private sealed class NavRow : ContainerControl
        {
            private readonly Label _icon;
            private readonly Label _text;
            private readonly string _target;
            private bool _hovered;

            /// <summary>导航目标（dom-id）。</summary>
            public event Action<string> Activated;

            /// <summary>是否处于激活态。</summary>
            public bool Active { get; private set; }

            /// <summary>构造一个导航项。</summary>
            public NavRow(ContainerControl parent, string glyph, string label, bool active)
            {
                Parent = parent;
                BackgroundColor = Color.Transparent;
                ClipChildren = false;
                _target = label;
                Active = active;

                _icon = NewLabel(this, glyph, InkWashTheme.FontRole.Heading, 15f, active ? InkWashTheme.GoldBright : InkWashTheme.PaperAged, TextAlignment.Center);
                _text = NewLabel(this, label, InkWashTheme.FontRole.Heading, 15f, active ? InkWashTheme.GoldBright : InkWashTheme.PaperAged);
            }

            /// <summary>设置导航目标 dom-id。</summary>
            public void SetTarget(string target)
            {
                _targetValue = target;
            }

            private string _targetValue;

            /// <inheritdoc />
            protected override void OnSizeChanged()
            {
                base.OnSizeChanged();
                Place(_icon, 32f, (Height - 20f) * 0.5f, 20f, 20f);
                Place(_text, 68f, 0f, Mathf.Max(0f, Width - 80f), Height);
            }

            /// <inheritdoc />
            public override void OnMouseEnter(Float2 location)
            {
                base.OnMouseEnter(location);
                _hovered = true;
            }

            /// <inheritdoc />
            public override void OnMouseLeave()
            {
                base.OnMouseLeave();
                _hovered = false;
            }

            /// <inheritdoc />
            public override bool OnMouseDown(Float2 location, MouseButton button)
            {
                base.OnMouseDown(location, button);
                if (button != MouseButton.Left)
                    return false;

                Activated?.Invoke(!string.IsNullOrEmpty(_targetValue) ? _targetValue : _target);
                return true;
            }

            /// <inheritdoc />
            public override void Draw()
            {
                if (!Visible || Width <= 0f || Height <= 0f)
                    return;

                if (Active)
                {
                    Render2D.FillRectangle(new Rectangle(0f, 0f, Width, Height), Alpha(InkWashTheme.GoldPrimary, 0.08f));
                }
                else if (_hovered)
                {
                    Render2D.FillRectangle(new Rectangle(0f, 0f, Width, Height), Alpha(InkWashTheme.GoldPrimary, 0.05f));
                }

                base.Draw();

                if (Active)
                {
                    Render2D.FillRectangle(new Rectangle(0f, 0f, NavActiveBarWidth, Height), InkWashTheme.GoldPrimary);
                }
            }
        }

        /// <summary>
        /// 标签栏单项（对应 .tab-item）：文案 + 计数徽章，激活态绘制底部 2px 鎏金下划线。
        /// </summary>
        private sealed class ActivityTab : ContainerControl
        {
            private readonly InkButton _hit;
            private readonly Label _name;
            private readonly Label _count;
            private bool _active;

            /// <summary>该标签在标签栏中的序号。</summary>
            public int TabIndex { get; }

            /// <summary>点击事件（回传标签序号）。</summary>
            public event Action<int> Clicked;

            /// <summary>构造一个标签。</summary>
            public ActivityTab(ContainerControl parent, string name, int count, bool active)
            {
                Parent = parent;
                BackgroundColor = Color.Transparent;
                ClipChildren = false;
                _active = active;

                _hit = new InkButton
                {
                    Text = string.Empty,
                    Variant = InkButtonVariant.Ghost,
                    ButtonSize = InkButtonSize.Sm,
                    BorderThickness = 0f,
                    Parent = this,
                };
                _hit.Clicked += () => Clicked?.Invoke(TabIndex);

                _name = NewLabel(this, name, InkWashTheme.FontRole.Heading, 13f, active ? InkWashTheme.GoldBright : InkWashTheme.PaperDark, TextAlignment.Center);
                _count = NewLabel(this, count.ToString(), InkWashTheme.FontRole.Body, 10f, active ? InkWashTheme.GoldBright : InkWashTheme.PaperDark, TextAlignment.Center);
                _count.BackgroundColor = active ? Alpha(InkWashTheme.GoldPrimary, 0.15f) : Alpha(InkWashTheme.PaperDark, 0.15f);
            }

            /// <summary>设置激活态。</summary>
            public void SetActive(bool active)
            {
                _active = active;
                if (_name != null)
                    _name.TextColor = active ? InkWashTheme.GoldBright : InkWashTheme.PaperDark;
                if (_count != null)
                {
                    _count.TextColor = active ? InkWashTheme.GoldBright : InkWashTheme.PaperDark;
                    _count.BackgroundColor = active ? Alpha(InkWashTheme.GoldPrimary, 0.15f) : Alpha(InkWashTheme.PaperDark, 0.15f);
                }
            }

            /// <inheritdoc />
            protected override void OnSizeChanged()
            {
                base.OnSizeChanged();
                Place(_hit, 0f, 0f, Width, Height);
                Place(_name, 0f, 0f, Mathf.Max(0f, Width - 34f), Height);
                Place(_count, Mathf.Max(0f, Width - 30f), (Height - 18f) * 0.5f, 24f, 18f);
            }

            /// <inheritdoc />
            public override void Draw()
            {
                base.Draw();
                if (_active && Width > 0f && Height > 0f)
                {
                    Render2D.FillRectangle(new Rectangle(0f, Height - 2f, Width, 2f), InkWashTheme.GoldPrimary);
                }
            }
        }

        /// <summary>带品质描边的字形图标盒（替代设计稿 lucide 图标 / quality border 图标）。</summary>
        private sealed class GlyphBox : ContainerControl
        {
            private readonly Label _glyph;
            private readonly float _radius;
            private Color _fill;
            private Color _border;

            /// <summary>构造图标盒。</summary>
            public GlyphBox(ContainerControl parent, float width, float height, float radius, Color fill, Color border, float glyphSize, Color glyphColor)
            {
                Parent = parent;
                BackgroundColor = Color.Transparent;
                ClipChildren = false;
                Size = new Float2(width, height);
                _radius = radius;
                _fill = fill;
                _border = border;
                _glyph = NewLabel(this, string.Empty, InkWashTheme.FontRole.Heading, glyphSize, glyphColor, TextAlignment.Center);
                _glyph.AnchorPreset = AnchorPresets.StretchAll;
            }

            /// <summary>更新图标样式。</summary>
            public void SetStyle(string glyph, Color fill, Color border, Color glyphColor)
            {
                _fill = fill;
                _border = border;
                if (_glyph != null)
                {
                    _glyph.Text = glyph;
                    _glyph.TextColor = glyphColor;
                }
            }

            /// <inheritdoc />
            public override void Draw()
            {
                if (!Visible || Width <= 0f || Height <= 0f)
                    return;

                var bounds = new Rectangle(0f, 0f, Width, Height);
                InkRenderHelper.FillRoundedRectangle(bounds, _radius, _fill);
                base.Draw();
                if (_border.A > 0f)
                {
                    InkRenderHelper.DrawRoundedRectangle(bounds, _radius, _border, 1f);
                }
            }
        }

        /// <summary>
        /// 活动条目（对应 .activity-entry）：图标盒 + 名称/状态 + 剩余时间 + 进度条 + 奖励预览 + 箭头。
        /// selected 态绘制微金底与金边，hover 态提亮，点击回传条目序号。
        /// </summary>
        private sealed class ActivityEntry : ContainerControl
        {
            private readonly int _index;
            private readonly ActivitySpec _spec;
            private readonly GlyphBox _iconBox;
            private readonly Label _name;
            private readonly InkTag _statusTag;
            private readonly Label _timeGlyph;
            private readonly Label _timeLabel;
            private readonly InkBar _progressBar;
            private readonly Label _progressCaption;
            private readonly Label _progressValue;
            private readonly Label _moreLabel;
            private readonly Label _arrow;
            private readonly List<GlyphBox> _rewardIcons;

            private bool _selected;
            private bool _hovered;

            /// <summary>点击事件（回传条目序号）。</summary>
            public event Action<int> Clicked;

            /// <summary>构造一个活动条目。</summary>
            public ActivityEntry(ContainerControl parent, int index, ActivitySpec spec, bool selected)
            {
                Parent = parent;
                BackgroundColor = Color.Transparent;
                ClipChildren = false;
                Height = EntryHeight;

                _index = index;
                _spec = spec;
                _selected = selected;

                Color nameColor = spec.Ended ? InkWashTheme.PaperDark : InkWashTheme.PaperBright;
                _iconBox = new GlyphBox(
                    this,
                    48f,
                    48f,
                    InkWashTheme.RadiusMd,
                    InkWashTheme.BaseTertiary,
                    InkWashTheme.BorderGold,
                    20f,
                    spec.IconAccent);
                _iconBox.SetStyle(spec.IconGlyph, InkWashTheme.BaseTertiary, InkWashTheme.BorderGold, spec.IconAccent);

                _name = NewLabel(this, spec.Name, InkWashTheme.FontRole.Heading, 15f, nameColor);

                _statusTag = new InkTag
                {
                    Text = spec.StatusText,
                    Parent = this,
                };
                ApplyStatusTagStyle(_statusTag, spec.StatusText);

                Color timeColor = spec.Urgent ? InkWashTheme.VermilionBright : InkWashTheme.PaperDark;
                _timeGlyph = NewLabel(this, spec.TimeGlyph, InkWashTheme.FontRole.Body, 11f, timeColor, TextAlignment.Center);
                _timeLabel = NewLabel(this, spec.TimeText, InkWashTheme.FontRole.Body, 11f, timeColor);

                _progressBar = new InkBar
                {
                    Height = 5f,
                    FillVariant = spec.Ended ? InkBarFillVariant.Gold : spec.BarVariant,
                    BorderColor = Color.Transparent,
                    Parent = this,
                };
                _progressBar.Value = spec.Progress;

                _progressCaption = NewLabel(this, spec.ProgressCaption, InkWashTheme.FontRole.Body, 10f, InkWashTheme.TextTertiary);
                _progressValue = NewLabel(this, spec.ProgressValueText, InkWashTheme.FontRole.Number, 10f, InkWashTheme.PaperAged, TextAlignment.Far);

                _rewardIcons = new List<GlyphBox>();
                for (int i = 0; i < spec.Preview.Length; i++)
                {
                    Color quality = InkWashTheme.QualityColor(spec.Preview[i].Quality);
                    GlyphBox box = new GlyphBox(
                        this,
                        24f,
                        24f,
                        InkWashTheme.RadiusSm,
                        Alpha(quality, 0.10f),
                        quality,
                        12f,
                        quality);
                    box.SetStyle(spec.Preview[i].Glyph, Alpha(quality, 0.10f), quality, quality);
                    _rewardIcons.Add(box);
                }

                _moreLabel = NewLabel(this, spec.MoreText, InkWashTheme.FontRole.Body, 10f, InkWashTheme.TextTertiary);
                _arrow = NewLabel(this, "›", InkWashTheme.FontRole.Body, 16f, _selected ? InkWashTheme.GoldPrimary : InkWashTheme.TextTertiary, TextAlignment.Center);
            }

            /// <summary>是否匹配给定标签筛选（0 = 全部）。</summary>
            public bool MatchesTab(int tabIndex)
            {
                if (tabIndex <= 0)
                    return true;
                return _spec.StatusText == TabNames[tabIndex];
            }

            /// <summary>设置选中态。</summary>
            public void SetSelected(bool selected)
            {
                _selected = selected;
                if (_name != null)
                    _name.TextColor = _spec.Ended ? InkWashTheme.PaperDark : InkWashTheme.PaperBright;
                if (_arrow != null)
                    _arrow.TextColor = selected ? InkWashTheme.GoldPrimary : InkWashTheme.TextTertiary;
            }

            /// <inheritdoc />
            protected override void OnSizeChanged()
            {
                base.OnSizeChanged();

                float w = Width;
                float h = Height;
                if (w <= 0f)
                    return;

                Place(_iconBox, 16f, (h - 48f) * 0.5f, 48f, 48f);

                const float bodyX = 80f;
                Place(_name, bodyX, 6f, 96f, 20f);
                Place(_statusTag, bodyX + 96f + 8f, 7f, 56f, 20f);
                Place(_timeGlyph, bodyX, 30f, 14f, 16f);
                Place(_timeLabel, bodyX + 16f, 30f, Mathf.Max(0f, w - bodyX - 60f), 16f);

                float rewardsW = _rewardIcons.Count * 24f + Mathf.Max(0, _rewardIcons.Count - 1) * 4f;
                if (!string.IsNullOrEmpty(_spec.MoreText))
                    rewardsW += 22f;
                float rewardsX = Mathf.Max(bodyX + 120f, w - 40f - rewardsW);

                float cursor = rewardsX;
                for (int i = 0; i < _rewardIcons.Count; i++)
                {
                    Place(_rewardIcons[i], cursor, 52f, 24f, 24f);
                    cursor += 28f;
                }
                if (!string.IsNullOrEmpty(_spec.MoreText))
                {
                    Place(_moreLabel, cursor, 55f, 22f, 18f);
                }
                else
                {
                    Place(_moreLabel, cursor, 55f, 0f, 0f);
                }

                float barW = Mathf.Max(40f, rewardsX - 12f - bodyX);
                Place(_progressBar, bodyX, 56f, barW, 5f);
                Place(_progressCaption, bodyX, 63f, barW * 0.6f, 14f);
                Place(_progressValue, bodyX + barW * 0.4f, 63f, barW * 0.6f, 14f);

                Place(_arrow, w - 32f, (h - 18f) * 0.5f, 18f, 18f);
            }

            /// <inheritdoc />
            public override void OnMouseEnter(Float2 location)
            {
                base.OnMouseEnter(location);
                _hovered = true;
            }

            /// <inheritdoc />
            public override void OnMouseLeave()
            {
                base.OnMouseLeave();
                _hovered = false;
            }

            /// <inheritdoc />
            public override bool OnMouseDown(Float2 location, MouseButton button)
            {
                base.OnMouseDown(location, button);
                if (button != MouseButton.Left)
                    return false;

                Clicked?.Invoke(_index);
                return true;
            }

            /// <inheritdoc />
            public override void Draw()
            {
                if (!Visible || Width <= 0f || Height <= 0f)
                    return;

                var bounds = new Rectangle(0f, 0f, Width, Height);
                if (_selected)
                {
                    InkRenderHelper.FillRoundedRectangle(bounds, InkWashTheme.RadiusMd, Alpha(InkWashTheme.GoldPrimary, 0.08f));
                }
                else if (_hovered)
                {
                    InkRenderHelper.FillRoundedRectangle(bounds, InkWashTheme.RadiusMd, Alpha(InkWashTheme.GoldPrimary, 0.05f));
                }

                base.Draw();

                InkRenderHelper.DrawRoundedRectangle(
                    bounds,
                    InkWashTheme.RadiusMd,
                    _selected ? InkWashTheme.BorderGold : InkWashTheme.BorderNeutralL1,
                    1f);
            }

            /// <summary>按状态应用标签配色（进行中=春青 / 即将开始=鎏金 / 已结束=纸暗）。</summary>
            private static void ApplyStatusTagStyle(InkTag tag, string status)
            {
                if (tag == null)
                    return;

                switch (status)
                {
                    case "进行中":
                        tag.TagVariant = InkTagVariant.Default;
                        tag.BackgroundColor = Alpha(InkWashTheme.JadePrimary, 0.12f);
                        tag.TextColor = InkWashTheme.JadeBright;
                        break;
                    case "即将开始":
                        tag.TagVariant = InkTagVariant.Brand;
                        break;
                    default:
                        tag.TagVariant = InkTagVariant.Default;
                        tag.BackgroundColor = Alpha(InkWashTheme.PaperDark, 0.10f);
                        tag.TextColor = InkWashTheme.PaperDark;
                        break;
                }
            }
        }

        /// <summary>参与条件标签（对应 .requirement-tag.met：春青描边 + ✓ 前缀）。</summary>
        private sealed class RequirementTag : ContainerControl
        {
            private readonly Label _label;

            /// <summary>建议宽度（按文案长度估算，用于横向排布）。</summary>
            public float PreferredWidth { get; private set; }

            /// <summary>构造条件标签。</summary>
            public RequirementTag(ContainerControl parent, string text)
            {
                Parent = parent;
                BackgroundColor = Color.Transparent;
                ClipChildren = false;
                _label = NewLabel(this, string.Empty, InkWashTheme.FontRole.Body, 11f, InkWashTheme.JadeBright);
                _label.AnchorPreset = AnchorPresets.StretchAll;
                SetText(text);
            }

            /// <summary>更新条件文案（Label.Text 为本地化字符串，长度须经 ToString）。</summary>
            public void SetText(string text)
            {
                if (_label == null)
                    return;

                string line = string.IsNullOrEmpty(text) ? string.Empty : "✓ " + text;
                _label.Text = line;
                PreferredWidth = _label.Text.ToString().Length * 12f + 20f;
            }

            /// <inheritdoc />
            public override void Draw()
            {
                if (!Visible || Width <= 0f || Height <= 0f)
                    return;

                var bounds = new Rectangle(0f, 0f, Width, Height);
                InkRenderHelper.FillRoundedRectangle(bounds, InkWashTheme.RadiusSm, Alpha(InkWashTheme.Abyss, 0.6f));
                base.Draw();
                InkRenderHelper.DrawRoundedRectangle(bounds, InkWashTheme.RadiusSm, InkWashTheme.JadePrimary, 1f);
            }
        }

        /// <summary>详情奖励行（对应 .detail-reward-item：品质图标 + 名称 + 数量）。</summary>
        private sealed class DetailRewardRow : ContainerControl
        {
            private readonly GlyphBox _icon;
            private readonly Label _name;
            private readonly Label _qtyLabel;
            private readonly Label _qtyValue;

            /// <summary>构造奖励行。</summary>
            public DetailRewardRow(ContainerControl parent, DetailReward reward)
            {
                Parent = parent;
                BackgroundColor = Color.Transparent;
                ClipChildren = false;
                Height = DetailRewardRowHeight;

                _icon = new GlyphBox(
                    this,
                    36f,
                    36f,
                    InkWashTheme.RadiusSm,
                    Alpha(InkWashTheme.QualityColor(reward.Quality), 0.10f),
                    InkWashTheme.QualityColor(reward.Quality),
                    16f,
                    InkWashTheme.QualityColor(reward.Quality));

                _name = NewLabel(this, reward.Name, InkWashTheme.FontRole.Heading, 13f, InkWashTheme.QualityColor(reward.Quality));
                _qtyLabel = NewLabel(this, RewardQtyLabelText, InkWashTheme.FontRole.Body, 11f, InkWashTheme.TextTertiary);
                _qtyValue = NewLabel(this, reward.Quantity, InkWashTheme.FontRole.Number, 12f, InkWashTheme.PaperAged);

                Apply(reward);
            }

            /// <summary>更新该行显示的奖励数据。</summary>
            public void Apply(DetailReward reward)
            {
                if (reward == null)
                    return;

                Color quality = InkWashTheme.QualityColor(reward.Quality);
                if (_icon != null)
                    _icon.SetStyle(reward.Glyph, Alpha(quality, 0.10f), quality, quality);
                if (_name != null)
                {
                    _name.Text = reward.Name;
                    _name.TextColor = quality;
                }
                if (_qtyValue != null)
                    _qtyValue.Text = reward.Quantity;
            }

            /// <inheritdoc />
            protected override void OnSizeChanged()
            {
                base.OnSizeChanged();
                Place(_icon, 8f, 4f, 36f, 36f);
                Place(_name, 56f, 5f, Mathf.Max(0f, Width - 130f), 18f);
                Place(_qtyLabel, 56f, 25f, 40f, 14f);
                Place(_qtyValue, 100f, 23f, 90f, 16f);
            }

            /// <inheritdoc />
            public override void Draw()
            {
                if (!Visible || Width <= 0f || Height <= 0f)
                    return;

                var bounds = new Rectangle(0f, 0f, Width, Height);
                InkRenderHelper.FillRoundedRectangle(bounds, InkWashTheme.RadiusSm, Alpha(InkWashTheme.Abyss, 0.5f));
                base.Draw();
                InkRenderHelper.DrawRoundedRectangle(bounds, InkWashTheme.RadiusSm, InkWashTheme.BorderNeutralL1, 1f);
            }
        }
    }
}

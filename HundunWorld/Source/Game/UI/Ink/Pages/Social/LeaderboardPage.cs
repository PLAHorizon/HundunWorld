using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.StyleSystem;
using System;
using System.Collections.Generic;

namespace HundunWorld.Game.UI.Ink.Pages.Social
{
    /// <summary>
    /// 江湖风云榜页面 — 对应 leaderboard.html 设计原型。
    /// <para>
    /// 三栏布局：
    /// <list type="bullet">
    ///   <item>顶部：标题"江湖风云榜" + 当前赛季 + 剩余天数 + 关闭按钮</item>
    ///   <item>左栏：6 个榜单分类（战力/等级/财富/门派/竞技/帮派）+ 4 个时间筛选按钮</item>
    ///   <item>中栏：Top 3 颁奖台（金/银/铜）+ 前 12 名排名表格 + 加载更多指示</item>
    ///   <item>右栏：我的排名卡 + 距上一名差距 + 本周趋势柱状图 + 赛季奖励预览</item>
    ///   <item>底部：返回沉浸模式</item>
    /// </list>
    /// 通过 <see cref="NavigationRequested"/> 事件向路由器暴露导航请求。
    /// </para>
    /// </summary>
    public class LeaderboardPage : ContainerControl, IInkPage
    {
        // ===================================================================
        // 布局常量
        // =======================================================================

        /// <summary>顶部标题栏高度</summary>
        private const float TopHeaderHeight = 52f;

        /// <summary>底部按钮栏高度</summary>
        private const float BottomBarHeight = 48f;

        /// <summary>屏幕边缘留白</summary>
        private const float ScreenEdge = 16f;

        /// <summary>列间距</summary>
        private const float ColumnGap = 12f;

        /// <summary>左栏宽度（榜单分类）</summary>
        private const float LeftColumnWidth = 240f;

        /// <summary>右栏宽度（个人排名对比）</summary>
        private const float RightColumnWidth = 280f;

        /// <summary>颁奖台高度（Top 3）</summary>
        private const float PodiumHeight = 170f;

        /// <summary>表格表头高度</summary>
        private const float TableHeaderHeight = 32f;

        /// <summary>表格行高</summary>
        private const float TableRowHeight = 36f;

        /// <summary>分类条目高度</summary>
        private const float CategoryItemHeight = 50f;

        /// <summary>分类条目间距</summary>
        private const float CategoryItemGap = 4f;

        /// <summary>时间筛选按钮高度</summary>
        private const float TimeBtnHeight = 28f;

        /// <summary>时间筛选按钮间距</summary>
        private const float TimeBtnGap = 6f;

        /// <summary>底部按钮宽度</summary>
        private const float BottomBtnWidth = 160f;

        /// <summary>操作按钮高度</summary>
        private const float ActionBtnHeight = 36f;

        // ===================================================================
        // 子控件引用 — 顶部标题栏
        // =======================================================================

        /// <summary>顶部标题栏面板</summary>
        private InkPanel _topHeader;

        /// <summary>页面主标题</summary>
        private Label _titleLabel;

        /// <summary>英文副标题</summary>
        private Label _subtitleLabel;

        /// <summary>赛季信息标签</summary>
        private Label _seasonLabel;

        /// <summary>剩余天数标签</summary>
        private Label _remainDaysLabel;

        /// <summary>关闭（返回沉浸）按钮</summary>
        private InkButton _closeButton;

        // ===================================================================
        // 子控件引用 — 左栏（榜单分类与时间筛选）
        // =======================================================================

        /// <summary>左栏容器</summary>
        private InkPanel _leftPanel;

        /// <summary>分类列表宿主容器</summary>
        private ContainerControl _categoryHost;

        /// <summary>分类列表标题</summary>
        private Label _categoryTitleLabel;

        /// <summary>分类条目容器列表</summary>
        private readonly List<ContainerControl> _categoryItems = new List<ContainerControl>();

        /// <summary>时间筛选面板</summary>
        private InkPanel _timeFilterPanel;

        /// <summary>时间筛选标题</summary>
        private Label _timeFilterTitleLabel;

        /// <summary>时间筛选按钮列表（4 个）</summary>
        private readonly List<InkButton> _timeFilterButtons = new List<InkButton>();

        // ===================================================================
        // 子控件引用 — 中栏（颁奖台与排名表格）
        // =======================================================================

        /// <summary>中栏容器</summary>
        private InkPanel _middlePanel;

        /// <summary>颁奖台宿主容器</summary>
        private ContainerControl _podiumHost;

        /// <summary>2nd 青玉颁奖卡片</summary>
        private InkPanel _podiumSilver;

        /// <summary>1st 鎏金颁奖卡片</summary>
        private InkPanel _podiumGold;

        /// <summary>3rd 古铜颁奖卡片</summary>
        private InkPanel _podiumBronze;

        /// <summary>排名表格宿主容器</summary>
        private InkPanel _tablePanel;

        /// <summary>表格表头容器</summary>
        private ContainerControl _tableHeader;

        /// <summary>表格行宿主容器</summary>
        private ContainerControl _tableBodyHost;

        /// <summary>加载更多指示器标签</summary>
        private Label _loadMoreLabel;

        /// <summary>表格行容器列表</summary>
        private readonly List<ContainerControl> _tableRows = new List<ContainerControl>();

        // ===================================================================
        // 子控件引用 — 右栏（个人排名对比）
        // =======================================================================

        /// <summary>右栏容器</summary>
        private InkPanel _rightPanel;

        /// <summary>我的排名卡片</summary>
        private InkPanel _myRankCard;

        /// <summary>我的排名标题</summary>
        private Label _myRankTitleLabel;

        /// <summary>我的角色名标签</summary>
        private Label _myNameLabel;

        /// <summary>我的门派等级标签</summary>
        private Label _mySectLevelLabel;

        /// <summary>我的当前排名数值标签</summary>
        private Label _myRankValueLabel;

        /// <summary>我的战力值标签</summary>
        private Label _myPowerValueLabel;

        /// <summary>距上一名卡片</summary>
        private InkPanel _gapCard;

        /// <summary>距上一名标题</summary>
        private Label _gapTitleLabel;

        /// <summary>上一名战力值标签</summary>
        private Label _prevRankPowerLabel;

        /// <summary>差距数值标签</summary>
        private Label _gapValueLabel;

        /// <summary>距上一名进度条</summary>
        private InkBar _gapProgressBar;

        /// <summary>本周趋势卡片</summary>
        private InkPanel _trendCard;

        /// <summary>本周趋势标题</summary>
        private Label _trendTitleLabel;

        /// <summary>本周变化数值标签</summary>
        private Label _trendChangeLabel;

        /// <summary>本周趋势柱状图宿主</summary>
        private ContainerControl _trendChartHost;

        /// <summary>本周起始排名标签</summary>
        private Label _trendStartLabel;

        /// <summary>赛季奖励卡片</summary>
        private InkPanel _rewardCard;

        /// <summary>赛季奖励标题</summary>
        private Label _rewardTitleLabel;

        /// <summary>奖励条目宿主容器</summary>
        private ContainerControl _rewardListHost;

        /// <summary>额外奖励提示标签</summary>
        private Label _rewardHintLabel;

        // ===================================================================
        // 子控件引用 — 底部按钮栏
        // =======================================================================

        /// <summary>底部按钮栏面板</summary>
        private InkPanel _bottomBar;

        /// <summary>返回沉浸模式按钮</summary>
        private InkButton _backToHudButton;

        // ===================================================================
        // 模拟数据 — 榜单分类
        // =======================================================================

        /// <summary>榜单分类信息结构</summary>
        private struct CategoryInfo
        {
            public string Name;
            public string Desc;
            public string Symbol;
            public bool Active;
        }

        /// <summary>6 个榜单分类数据</summary>
        private readonly CategoryInfo[] _categories =
        {
            new CategoryInfo { Name = "战力榜", Desc = "综合战力排名", Symbol = "⚡", Active = true },
            new CategoryInfo { Name = "等级榜", Desc = "角色等级排名", Symbol = "▲", Active = false },
            new CategoryInfo { Name = "财富榜", Desc = "银两资产排名", Symbol = "◆", Active = false },
            new CategoryInfo { Name = "门派贡献", Desc = "门派贡献排名", Symbol = "❖", Active = false },
            new CategoryInfo { Name = "竞技场榜", Desc = "PVP胜场排名", Symbol = "✦", Active = false },
            new CategoryInfo { Name = "帮派战力", Desc = "帮派总战力", Symbol = "◈", Active = false },
        };

        /// <summary>4 个时间筛选标签</summary>
        private readonly string[] _timeFilters = { "本日", "本周", "本月", "总榜" };

        /// <summary>当前激活的时间筛选索引（本周）</summary>
        private const int ActiveTimeFilterIndex = 1;

        // ===================================================================
        // 模拟数据 — 颁奖台 Top 3
        // =======================================================================

        /// <summary>颁奖台信息结构</summary>
        private struct PodiumInfo
        {
            public int Rank;
            public string AvatarChar;
            public string Name;
            public string Sect;
            public int Level;
            public int Power;
            public string Tier; // Gold / Silver / Bronze
        }

        /// <summary>Top 3 颁奖台数据（按 Silver/Gold/Bronze 顺序对应左中右）</summary>
        private readonly PodiumInfo[] _podiums =
        {
            new PodiumInfo { Rank = 2, AvatarChar = "沈", Name = "沈青鸾", Sect = "峨眉派", Level = 76, Power = 121830, Tier = "Silver" },
            new PodiumInfo { Rank = 1, AvatarChar = "剑", Name = "剑无痕", Sect = "华山派", Level = 78, Power = 128450, Tier = "Gold" },
            new PodiumInfo { Rank = 3, AvatarChar = "萧", Name = "萧别离", Sect = "丐帮", Level = 75, Power = 118920, Tier = "Bronze" },
        };

        // ===================================================================
        // 模拟数据 — 排名表格
        // =======================================================================

        /// <summary>排名行信息结构</summary>
        private struct RankRowInfo
        {
            public int Rank;
            public string AvatarChar;
            public string Name;
            public string Sect;
            public int Level;
            public int Power;
            public int Trend; // 正数上升，负数下降，0 不变
            public bool IsMe;
        }

        /// <summary>12 行排名数据（第 4-15 名）</summary>
        private readonly RankRowInfo[] _rankRows =
        {
            new RankRowInfo { Rank = 4, AvatarChar = "楚", Name = "楚留香", Sect = "武当派", Level = 74, Power = 112340, Trend = 2, IsMe = false },
            new RankRowInfo { Rank = 5, AvatarChar = "叶", Name = "叶孤城", Sect = "昆仑派", Level = 73, Power = 109580, Trend = -1, IsMe = false },
            new RankRowInfo { Rank = 6, AvatarChar = "陆", Name = "陆小凤", Sect = "唐门", Level = 72, Power = 105420, Trend = 0, IsMe = false },
            new RankRowInfo { Rank = 7, AvatarChar = "西", Name = "西门吹雪", Sect = "华山派", Level = 71, Power = 101870, Trend = 3, IsMe = false },
            new RankRowInfo { Rank = 8, AvatarChar = "花", Name = "花满楼", Sect = "慕容世家", Level = 70, Power = 98650, Trend = -2, IsMe = false },
            new RankRowInfo { Rank = 9, AvatarChar = "司", Name = "司空摘星", Sect = "丐帮", Level = 69, Power = 95320, Trend = 0, IsMe = false },
            new RankRowInfo { Rank = 10, AvatarChar = "傅", Name = "傅红雪", Sect = "明教", Level = 68, Power = 92180, Trend = 1, IsMe = false },
            new RankRowInfo { Rank = 11, AvatarChar = "柳", Name = "柳随风", Sect = "峨眉派", Level = 67, Power = 89450, Trend = -3, IsMe = false },
            new RankRowInfo { Rank = 12, AvatarChar = "燕", Name = "燕十三", Sect = "华山派", Level = 66, Power = 86720, Trend = 2, IsMe = false },
            new RankRowInfo { Rank = 13, AvatarChar = "谢", Name = "谢晓峰", Sect = "武当派", Level = 65, Power = 84180, Trend = 0, IsMe = false },
            new RankRowInfo { Rank = 14, AvatarChar = "独", Name = "独孤求败", Sect = "昆仑派", Level = 64, Power = 81560, Trend = -1, IsMe = false },
            new RankRowInfo { Rank = 15, AvatarChar = "李", Name = "李寻欢", Sect = "唐门", Level = 63, Power = 78930, Trend = 4, IsMe = false },
        };

        // ===================================================================
        // 模拟数据 — 个人排名对比
        // =======================================================================

        /// <summary>我的排名信息</summary>
        private readonly PodiumInfo _myInfo = new PodiumInfo
        {
            Rank = 47,
            AvatarChar = "侠",
            Name = "逍遥客",
            Sect = "武当派",
            Level = 60,
            Power = 85420,
            Tier = "Self",
        };

        /// <summary>上一名（第 46 名）信息</summary>
        private readonly PodiumInfo _prevRank = new PodiumInfo
        {
            Rank = 46,
            AvatarChar = "风",
            Name = "风清扬",
            Sect = "华山派",
            Level = 60,
            Power = 86650,
            Tier = "Prev",
        };

        /// <summary>本周每日排名数据（高最差，低最好）</summary>
        private readonly int[] _weeklyRanks = { 55, 53, 54, 51, 50, 48, 47 };

        /// <summary>本周每日标签</summary>
        private readonly string[] _weekdayLabels = { "一", "二", "三", "四", "五", "六", "日" };

        /// <summary>赛季奖励信息结构</summary>
        private struct RewardInfo
        {
            public string Name;
            public string Desc;
            public int Count;
            public string Quality; // Legendary / Rare / Epic
            public string Symbol;
        }

        /// <summary>3 个赛季奖励</summary>
        private readonly RewardInfo[] _rewards =
        {
            new RewardInfo { Name = "声望令", Desc = "江湖声望", Count = 500, Quality = "Legendary", Symbol = "★" },
            new RewardInfo { Name = "精铁锭", Desc = "锻造材料", Count = 20, Quality = "Rare", Symbol = "◆" },
            new RewardInfo { Name = "灵石", Desc = "修炼资源", Count = 10, Quality = "Epic", Symbol = "✦" },
        };

        // ===================================================================
        // 接口与属性
        // =======================================================================

        /// <summary>
        /// 导航请求事件。按钮点击后通过此事件向路由器暴露目标 dom-id。
        /// </summary>
        public event Action<string> NavigationRequested;

        /// <summary>
        /// 粒子动效系统引用（可选，由 MainUIManager 注入）。
        /// </summary>
        public InkParticleSystem ParticleSystem { get; set; }

        // ===================================================================
        // 构造函数
        // =======================================================================

        /// <summary>
        /// 构造函数：初始化所有子控件。
        /// </summary>
        public LeaderboardPage()
        {
            try
            {
                AnchorPreset = AnchorPresets.StretchAll;
                Offsets = Margin.Zero;
                BackgroundColor = Color.Transparent;
                ClipChildren = false;
                AutoFocus = false;

                BuildTopHeader();
                BuildLeftPanel();
                BuildMiddlePanel();
                BuildRightPanel();
                BuildBottomBar();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[LeaderboardPage] 初始化失败: {ex.Message}");
            }
        }

        // ===================================================================
        // Build 方法 — 顶部标题栏
        // =======================================================================

        /// <summary>
        /// 构建顶部标题栏：主标题 + 副标题 + 赛季信息 + 关闭按钮。
        /// </summary>
        private void BuildTopHeader()
        {
            _topHeader = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(800f, TopHeaderHeight),
                Variant = InkPanelVariant.Default,
            };

            // 主标题"江湖风云榜"
            _titleLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(20f, 14f),
                Size = new Float2(180f, 24f),
                Text = "江湖风云榜",
                TextColor = InkWashTheme.GoldPrimary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 18f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _topHeader.AddChild(_titleLabel);

            // 英文副标题 LEADERBOARD
            _subtitleLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(208f, 18f),
                Size = new Float2(140f, 18f),
                Text = "LEADERBOARD",
                TextColor = InkWashTheme.TextTertiary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 11f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _topHeader.AddChild(_subtitleLabel);

            // 赛季信息
            _seasonLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(560f, 18f),
                Size = new Float2(100f, 18f),
                Text = "第3赛季",
                TextColor = InkWashTheme.TextSecondary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 12f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _topHeader.AddChild(_seasonLabel);

            // 剩余天数
            _remainDaysLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(664f, 18f),
                Size = new Float2(100f, 18f),
                Text = "剩余12天",
                TextColor = InkWashTheme.GoldPrimary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 12f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _topHeader.AddChild(_remainDaysLabel);

            // 关闭（返回沉浸）按钮
            _closeButton = new InkButton
            {
                Variant = InkButtonVariant.Default,
                ButtonSize = InkButtonSize.Sm,
                Text = "✕",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(760f, 10f),
                Size = new Float2(32f, 32f),
            };
            _closeButton.ButtonClicked += (b) => OnNavigationButtonClicked(InkPageDomIds.CombatHud, b);
            _topHeader.AddChild(_closeButton);

            AddChild(_topHeader);
        }

        // ===================================================================
        // Build 方法 — 左栏（榜单分类与时间筛选）
        // =======================================================================

        /// <summary>
        /// 构建左栏：榜单分类列表（6 个）+ 时间筛选面板（4 个按钮）。
        /// </summary>
        private void BuildLeftPanel()
        {
            _leftPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(LeftColumnWidth, 600f),
            };

            // ===== 榜单分类卡片 =====
            var categoryCard = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(ScreenEdge, ScreenEdge),
                Size = new Float2(LeftColumnWidth - ScreenEdge * 2f, 380f),
            };
            _leftPanel.AddChild(categoryCard);

            // 分类列表标题
            _categoryTitleLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 8f),
                Size = new Float2(LeftColumnWidth - ScreenEdge * 2f - 24f, 22f),
                Text = "◆ 榜单分类",
                TextColor = InkWashTheme.TextSecondary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            categoryCard.AddChild(_categoryTitleLabel);

            // 分类列表宿主
            _categoryHost = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(8f, 36f),
                Size = new Float2(LeftColumnWidth - ScreenEdge * 2f - 16f, 336f),
            };
            categoryCard.AddChild(_categoryHost);

            // 6 个分类条目
            float cursorY = 0f;
            for (int i = 0; i < _categories.Length; i++)
            {
                var cat = _categories[i];
                var item = new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(0f, cursorY),
                    Size = new Float2(_categoryHost.Width, CategoryItemHeight),
                    BackgroundColor = cat.Active
                        ? new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.10f)
                        : Color.Transparent,
                };

                // 符号标签
                var symbolLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(10f, 14f),
                    Size = new Float2(20f, 22f),
                    Text = cat.Symbol,
                    TextColor = cat.Active ? InkWashTheme.GoldBright : InkWashTheme.TextTertiary,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 16f),
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                };
                item.AddChild(symbolLabel);

                // 分类名称
                var nameLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(36f, 6f),
                    Size = new Float2(140f, 20f),
                    Text = cat.Name,
                    TextColor = cat.Active ? InkWashTheme.GoldBright : InkWashTheme.TextSecondary,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 13f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                item.AddChild(nameLabel);

                // 分类描述
                var descLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(36f, 26f),
                    Size = new Float2(160f, 18f),
                    Text = cat.Desc,
                    TextColor = InkWashTheme.TextTertiary,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 10f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                item.AddChild(descLabel);

                // 激活指示符（chevron-right）
                if (cat.Active)
                {
                    var arrowLabel = new Label
                    {
                        AnchorPreset = AnchorPresets.TopLeft,
                        Location = new Float2(_categoryHost.Width - 22f, 14f),
                        Size = new Float2(20f, 22f),
                        Text = "›",
                        TextColor = InkWashTheme.GoldBright,
                        Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 18f),
                        HorizontalAlignment = TextAlignment.Center,
                        VerticalAlignment = TextAlignment.Center,
                    };
                    item.AddChild(arrowLabel);
                }

                _categoryItems.Add(item);
                _categoryHost.AddChild(item);
                cursorY += CategoryItemHeight + CategoryItemGap;
            }

            // ===== 时间筛选面板 =====
            _timeFilterPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(ScreenEdge, ScreenEdge + 380f + 8f),
                Size = new Float2(LeftColumnWidth - ScreenEdge * 2f, 120f),
            };
            _leftPanel.AddChild(_timeFilterPanel);

            _timeFilterTitleLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 8f),
                Size = new Float2(LeftColumnWidth - ScreenEdge * 2f - 24f, 22f),
                Text = "◆ 时间筛选",
                TextColor = InkWashTheme.TextSecondary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 12f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _timeFilterPanel.AddChild(_timeFilterTitleLabel);

            // 4 个时间筛选按钮（2x2 网格）
            float btnWidth = (_timeFilterPanel.Width - 24f - TimeBtnGap) * 0.5f;
            for (int i = 0; i < _timeFilters.Length; i++)
            {
                int col = i % 2;
                int row = i / 2;
                bool active = i == ActiveTimeFilterIndex;
                var btn = new InkButton
                {
                    Variant = active ? InkButtonVariant.Primary : InkButtonVariant.Ghost,
                    ButtonSize = InkButtonSize.Sm,
                    Text = _timeFilters[i],
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(12f + col * (btnWidth + TimeBtnGap), 36f + row * (TimeBtnHeight + TimeBtnGap)),
                    Size = new Float2(btnWidth, TimeBtnHeight),
                };
                btn.ButtonClicked += (b) => EmitGoldAtButton(b);
                _timeFilterButtons.Add(btn);
                _timeFilterPanel.AddChild(btn);
            }

            AddChild(_leftPanel);
        }

        // ===================================================================
        // Build 方法 — 中栏（颁奖台与排名表格）
        // =======================================================================

        /// <summary>
        /// 构建中栏：Top 3 颁奖台 + 排名表格（表头 + 12 行 + 加载更多）。
        /// </summary>
        private void BuildMiddlePanel()
        {
            _middlePanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(800f, 600f),
            };

            // ===== Top 3 颁奖台 =====
            _podiumHost = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(ScreenEdge, ScreenEdge),
                Size = new Float2(800f, PodiumHeight),
            };
            _middlePanel.AddChild(_podiumHost);

            BuildPodiumCard(_podiums[0], 0, "Silver");   // 2nd 青玉（左）
            BuildPodiumCard(_podiums[1], 1, "Gold");     // 1st 鎏金（中，最高）
            BuildPodiumCard(_podiums[2], 2, "Bronze");   // 3rd 古铜（右）

            // ===== 排名表格 =====
            _tablePanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(ScreenEdge, ScreenEdge + PodiumHeight + 8f),
                Size = new Float2(800f, 380f),
            };
            _middlePanel.AddChild(_tablePanel);

            BuildTableHeader();
            BuildTableBody();
            BuildLoadMoreIndicator();

            AddChild(_middlePanel);
        }

        /// <summary>
        /// 构建颁奖台卡片：根据 tier 选择配色（Gold 鎏金 / Silver 青玉 / Bronze 古铜）。
        /// </summary>
        private void BuildPodiumCard(PodiumInfo info, int slotIndex, string tier)
        {
            Color borderColor;
            Color textColor;
            Color avatarBg;
            float marginTop;
            float avatarSize;
            float padding;

            switch (tier)
            {
                case "Gold":
                    borderColor = InkWashTheme.GoldPrimary;
                    textColor = InkWashTheme.GoldBright;
                    avatarBg = new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.15f);
                    marginTop = 0f;
                    avatarSize = 56f;
                    padding = 18f;
                    break;
                case "Silver":
                    borderColor = InkWashTheme.JadePrimary;
                    textColor = InkWashTheme.JadeBright;
                    avatarBg = new Color(InkWashTheme.JadePrimary.R, InkWashTheme.JadePrimary.G, InkWashTheme.JadePrimary.B, 0.15f);
                    marginTop = 12f;
                    avatarSize = 48f;
                    padding = 14f;
                    break;
                default: // Bronze
                    borderColor = InkWashTheme.BronzePrimary;
                    textColor = InkWashTheme.BronzePrimary;
                    avatarBg = new Color(InkWashTheme.BronzePrimary.R, InkWashTheme.BronzePrimary.G, InkWashTheme.BronzePrimary.B, 0.15f);
                    marginTop = 16f;
                    avatarSize = 44f;
                    padding = 12f;
                    break;
            }

            // 三栏均分（每栏占 1/3 宽度）
            float cardWidth = (_podiumHost.Width - ColumnGap * 2f) / 3f;
            float cardX = slotIndex * (cardWidth + ColumnGap);

            var card = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(cardX, marginTop),
                Size = new Float2(cardWidth, PodiumHeight - marginTop),
            };
            card.BackgroundColor = new Color(borderColor.R, borderColor.G, borderColor.B, 0.08f);

            // 排名徽章（顶部居中圆形）
            float badgeSize = tier == "Gold" ? 32f : 28f;
            var badge = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(cardWidth * 0.5f - badgeSize * 0.5f, 6f),
                Size = new Float2(badgeSize, badgeSize),
                BackgroundColor = InkWashTheme.BaseSecondary,
            };
            var rankNumLabel = new Label
            {
                AnchorPreset = AnchorPresets.StretchAll,
                Offsets = Margin.Zero,
                Text = info.Rank.ToString(),
                TextColor = textColor,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), tier == "Gold" ? 16f : 14f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            badge.AddChild(rankNumLabel);
            card.AddChild(badge);

            // 头像（圆形背景 + 单字）
            float avatarY = badgeSize + 8f;
            var avatar = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(cardWidth * 0.5f - avatarSize * 0.5f, avatarY),
                Size = new Float2(avatarSize, avatarSize),
                BackgroundColor = avatarBg,
            };
            var avatarCharLabel = new Label
            {
                AnchorPreset = AnchorPresets.StretchAll,
                Offsets = Margin.Zero,
                Text = info.AvatarChar,
                TextColor = textColor,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), avatarSize == 56f ? 24f : (avatarSize == 48f ? 20f : 18f)),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            avatar.AddChild(avatarCharLabel);
            card.AddChild(avatar);

            // 姓名标签
            float nameY = avatarY + avatarSize + 6f;
            var nameLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, nameY),
                Size = new Float2(cardWidth, 20f),
                Text = info.Name,
                TextColor = tier == "Gold" ? InkWashTheme.GoldBright : InkWashTheme.TextDefault,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), tier == "Gold" ? 15f : 14f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            card.AddChild(nameLabel);

            // 门派标签
            var sectLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, nameY + 20f),
                Size = new Float2(cardWidth, 18f),
                Text = info.Sect,
                TextColor = InkWashTheme.TextTertiary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 11f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            card.AddChild(sectLabel);

            // 等级标签
            var levelLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, nameY + 38f),
                Size = new Float2(cardWidth, 18f),
                Text = "Lv." + info.Level,
                TextColor = InkWashTheme.TextSecondary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 13f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            card.AddChild(levelLabel);

            // 战力值徽章
            var powerLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, nameY + 58f),
                Size = new Float2(cardWidth, 22f),
                Text = "⚡ " + info.Power.ToString("N0"),
                TextColor = textColor,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), tier == "Gold" ? 15f : 13f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            card.AddChild(powerLabel);

            // 按层级保存引用
            switch (tier)
            {
                case "Gold":
                    _podiumGold = card;
                    break;
                case "Silver":
                    _podiumSilver = card;
                    break;
                default:
                    _podiumBronze = card;
                    break;
            }
            _podiumHost.AddChild(card);
        }

        /// <summary>
        /// 构建表格表头：排名 / 头像 / 角色名 / 门派 / 等级 / 战力值 / 变化。
        /// </summary>
        private void BuildTableHeader()
        {
            _tableHeader = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 0f),
                Size = new Float2(_tablePanel.Width, TableHeaderHeight),
                BackgroundColor = new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.04f),
            };
            _tablePanel.AddChild(_tableHeader);

            // 6 列固定宽度（与 HTML 原型对齐）
            BuildHeaderCell(0f, 48f, "排名", TextAlignment.Center);
            BuildHeaderCell(48f, 44f, "", TextAlignment.Center);
            BuildHeaderCell(92f, 200f, "角色名", TextAlignment.Near);
            BuildHeaderCell(292f, 90f, "门派", TextAlignment.Near);
            BuildHeaderCell(382f, 60f, "等级", TextAlignment.Far);
            BuildHeaderCell(442f, 100f, "战力值", TextAlignment.Far);
            BuildHeaderCell(542f, 60f, "变化", TextAlignment.Center);
        }

        /// <summary>
        /// 构建表头单元格。
        /// </summary>
        private void BuildHeaderCell(float x, float w, string text, TextAlignment align)
        {
            var cell = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(x, 0f),
                Size = new Float2(w, TableHeaderHeight),
                Text = text,
                TextColor = InkWashTheme.TextTertiary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 11f),
                HorizontalAlignment = align,
                VerticalAlignment = TextAlignment.Center,
            };
            _tableHeader.AddChild(cell);
        }

        /// <summary>
        /// 构建表格主体：12 行排名数据。
        /// </summary>
        private void BuildTableBody()
        {
            _tableBodyHost = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, TableHeaderHeight),
                Size = new Float2(_tablePanel.Width, 12 * TableRowHeight),
            };
            _tablePanel.AddChild(_tableBodyHost);

            for (int i = 0; i < _rankRows.Length; i++)
            {
                BuildTableRow(_rankRows[i], i * TableRowHeight);
            }
        }

        /// <summary>
        /// 构建单行排名数据。
        /// </summary>
        private void BuildTableRow(RankRowInfo info, float yPos)
        {
            // 自己排名高亮（金色背景）
            Color rowBg = info.IsMe
                ? new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.12f)
                : Color.Transparent;

            var row = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, yPos),
                Size = new Float2(_tableBodyHost.Width, TableRowHeight),
                BackgroundColor = rowBg,
            };

            // 排名
            var rankLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 0f),
                Size = new Float2(48f, TableRowHeight),
                Text = info.Rank.ToString(),
                TextColor = InkWashTheme.TextSecondary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 14f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            row.AddChild(rankLabel);

            // 头像
            var avatar = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(58f, (TableRowHeight - 28f) * 0.5f),
                Size = new Float2(28f, 28f),
                BackgroundColor = InkWashTheme.BaseElevated,
            };
            var avatarCharLabel = new Label
            {
                AnchorPreset = AnchorPresets.StretchAll,
                Offsets = Margin.Zero,
                Text = info.AvatarChar,
                TextColor = InkWashTheme.TextSecondary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 12f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            avatar.AddChild(avatarCharLabel);
            row.AddChild(avatar);

            // 角色名
            var nameLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(92f, 0f),
                Size = new Float2(200f, TableRowHeight),
                Text = info.Name,
                TextColor = info.IsMe ? InkWashTheme.GoldBright : InkWashTheme.TextDefault,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            row.AddChild(nameLabel);

            // 门派
            var sectLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(292f, 0f),
                Size = new Float2(90f, TableRowHeight),
                Text = info.Sect,
                TextColor = InkWashTheme.TextSecondary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 12f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            row.AddChild(sectLabel);

            // 等级
            var levelLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(382f, 0f),
                Size = new Float2(60f, TableRowHeight),
                Text = "Lv." + info.Level,
                TextColor = InkWashTheme.TextSecondary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 13f),
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
            };
            row.AddChild(levelLabel);

            // 战力值
            var powerLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(442f, 0f),
                Size = new Float2(100f, TableRowHeight),
                Text = info.Power.ToString("N0"),
                TextColor = InkWashTheme.GoldPrimary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 14f),
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
            };
            row.AddChild(powerLabel);

            // 变化趋势（▲上升 / ▼下降 / —不变）
            string trendText;
            Color trendColor;
            if (info.Trend > 0)
            {
                trendText = "▲ " + info.Trend;
                trendColor = InkWashTheme.GoldBright;
            }
            else if (info.Trend < 0)
            {
                trendText = "▼ " + Math.Abs(info.Trend);
                trendColor = InkWashTheme.TextTertiary;
            }
            else
            {
                trendText = "—";
                trendColor = InkWashTheme.TextTertiary;
            }

            var trendLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(542f, 0f),
                Size = new Float2(60f, TableRowHeight),
                Text = trendText,
                TextColor = trendColor,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 11f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            row.AddChild(trendLabel);

            _tableRows.Add(row);
            _tableBodyHost.AddChild(row);
        }

        /// <summary>
        /// 构建加载更多指示器。
        /// </summary>
        private void BuildLoadMoreIndicator()
        {
            _loadMoreLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, TableHeaderHeight + 12 * TableRowHeight + 8f),
                Size = new Float2(_tablePanel.Width, 24f),
                Text = "• • •  滚动加载更多",
                TextColor = InkWashTheme.TextTertiary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 11f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            _tablePanel.AddChild(_loadMoreLabel);
        }

        // ===================================================================
        // Build 方法 — 右栏（个人排名对比）
        // =======================================================================

        /// <summary>
        /// 构建右栏：我的排名卡 + 距上一名卡 + 本周趋势卡 + 赛季奖励卡。
        /// </summary>
        private void BuildRightPanel()
        {
            _rightPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(RightColumnWidth, 600f),
            };

            float cursorY = ScreenEdge;
            float cardWidth = RightColumnWidth - ScreenEdge * 2f;

            // ===== 我的排名卡 =====
            _myRankCard = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(ScreenEdge, cursorY),
                Size = new Float2(cardWidth, 130f),
            };
            _myRankCard.BackgroundColor = new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.06f);
            _rightPanel.AddChild(_myRankCard);

            _myRankTitleLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 10f),
                Size = new Float2(cardWidth - 24f, 20f),
                Text = "◆ 我的排名",
                TextColor = InkWashTheme.TextTertiary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 12f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _myRankCard.AddChild(_myRankTitleLabel);

            // 头像
            var myAvatar = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(14f, 38f),
                Size = new Float2(48f, 48f),
                BackgroundColor = new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.12f),
            };
            var myAvatarChar = new Label
            {
                AnchorPreset = AnchorPresets.StretchAll,
                Offsets = Margin.Zero,
                Text = _myInfo.AvatarChar,
                TextColor = InkWashTheme.GoldBright,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 20f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            myAvatar.AddChild(myAvatarChar);
            _myRankCard.AddChild(myAvatar);

            // 姓名标签
            _myNameLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(70f, 40f),
                Size = new Float2(cardWidth - 80f, 20f),
                Text = _myInfo.Name,
                TextColor = InkWashTheme.TextDefault,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 15f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _myRankCard.AddChild(_myNameLabel);

            // 门派 + 等级
            _mySectLevelLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(70f, 60f),
                Size = new Float2(cardWidth - 80f, 18f),
                Text = _myInfo.Sect + "  Lv." + _myInfo.Level,
                TextColor = InkWashTheme.TextSecondary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 11f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _myRankCard.AddChild(_mySectLevelLabel);

            // 当前排名（左侧）
            _myRankValueLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(14f, 96f),
                Size = new Float2(cardWidth * 0.5f - 14f, 26f),
                Text = "第 " + _myInfo.Rank + " 名",
                TextColor = InkWashTheme.GoldBright,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 18f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _myRankCard.AddChild(_myRankValueLabel);

            // 战力值（右侧）
            _myPowerValueLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(cardWidth * 0.5f, 96f),
                Size = new Float2(cardWidth * 0.5f - 14f, 26f),
                Text = "⚡ " + _myInfo.Power.ToString("N0"),
                TextColor = InkWashTheme.GoldPrimary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 16f),
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
            };
            _myRankCard.AddChild(_myPowerValueLabel);

            cursorY += 130f + 8f;

            // ===== 距上一名卡 =====
            _gapCard = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(ScreenEdge, cursorY),
                Size = new Float2(cardWidth, 120f),
            };
            _rightPanel.AddChild(_gapCard);

            _gapTitleLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 10f),
                Size = new Float2(cardWidth - 24f, 20f),
                Text = "▲ 距上一名",
                TextColor = InkWashTheme.TextSecondary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 12f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _gapCard.AddChild(_gapTitleLabel);

            // 上一名战力
            var prevRankLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(14f, 36f),
                Size = new Float2(120f, 18f),
                Text = "第" + _prevRank.Rank + "名战力",
                TextColor = InkWashTheme.TextTertiary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 10f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _gapCard.AddChild(prevRankLabel);

            _prevRankPowerLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(14f, 54f),
                Size = new Float2(120f, 22f),
                Text = _prevRank.Power.ToString("N0"),
                TextColor = InkWashTheme.TextSecondary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 16f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _gapCard.AddChild(_prevRankPowerLabel);

            // 差距数值（右侧徽章式）
            var gapBg = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(cardWidth - 100f, 40f),
                Size = new Float2(86f, 40f),
                BackgroundColor = new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.08f),
            };
            var gapDescLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 4f),
                Size = new Float2(86f, 16f),
                Text = "差距",
                TextColor = InkWashTheme.TextTertiary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 9f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            gapBg.AddChild(gapDescLabel);
            _gapValueLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 18f),
                Size = new Float2(86f, 20f),
                Text = (_prevRank.Power - _myInfo.Power).ToString("N0"),
                TextColor = InkWashTheme.GoldPrimary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 15f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            gapBg.AddChild(_gapValueLabel);
            _gapCard.AddChild(gapBg);

            // 进度条
            _gapProgressBar = new InkBar
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(14f, 88f),
                Size = new Float2(cardWidth - 28f, 8f),
                Value = (float)_myInfo.Power / _prevRank.Power,
                FillVariant = InkBarFillVariant.Gold,
            };
            _gapCard.AddChild(_gapProgressBar);

            cursorY += 120f + 8f;

            // ===== 本周趋势卡 =====
            _trendCard = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(ScreenEdge, cursorY),
                Size = new Float2(cardWidth, 140f),
            };
            _rightPanel.AddChild(_trendCard);

            _trendTitleLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 10f),
                Size = new Float2(120f, 20f),
                Text = "◆ 本周趋势",
                TextColor = InkWashTheme.TextSecondary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 12f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _trendCard.AddChild(_trendTitleLabel);

            // +5 变化数值
            _trendChangeLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(cardWidth - 80f, 10f),
                Size = new Float2(68f, 20f),
                Text = "▲ +5",
                TextColor = InkWashTheme.GoldBright,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 12f),
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
            };
            _trendCard.AddChild(_trendChangeLabel);

            // 迷你柱状图宿主
            _trendChartHost = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 36f),
                Size = new Float2(cardWidth - 24f, 70f),
            };
            _trendCard.AddChild(_trendChartHost);

            BuildTrendChart();

            // 本周起始排名
            _trendStartLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 110f),
                Size = new Float2(cardWidth - 24f, 20f),
                Text = "本周起始：第55名",
                TextColor = InkWashTheme.TextTertiary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 10f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _trendCard.AddChild(_trendStartLabel);

            cursorY += 140f + 8f;

            // ===== 赛季奖励卡 =====
            _rewardCard = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(ScreenEdge, cursorY),
                Size = new Float2(cardWidth, 220f),
            };
            _rightPanel.AddChild(_rewardCard);

            _rewardTitleLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 10f),
                Size = new Float2(cardWidth - 24f, 20f),
                Text = "◆ 赛季奖励",
                TextColor = InkWashTheme.TextSecondary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 12f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _rewardCard.AddChild(_rewardTitleLabel);

            // 奖励条目宿主
            _rewardListHost = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 36f),
                Size = new Float2(cardWidth - 24f, 120f),
            };
            _rewardCard.AddChild(_rewardListHost);

            BuildRewardList();

            // 额外奖励提示
            _rewardHintLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 168f),
                Size = new Float2(cardWidth - 24f, 40f),
                Text = "排名进入前30可获额外奖励",
                TextColor = InkWashTheme.TextTertiary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 10f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            _rewardCard.AddChild(_rewardHintLabel);

            AddChild(_rightPanel);
        }

        /// <summary>
        /// 构建本周趋势迷你柱状图（7 天）。
        /// 排名数值越大表示越差（柱越高），数值越小越好（柱越短）。
        /// </summary>
        private void BuildTrendChart()
        {
            float chartWidth = _trendChartHost.Width;
            float chartHeight = _trendChartHost.Height - 16f; // 底部预留 16px 给星期标签
            float barGap = 4f;
            float barWidth = (chartWidth - barGap * 6f) / 7f;

            // 计算排名范围用于柱高归一化
            int maxRank = _weeklyRanks[0];
            int minRank = _weeklyRanks[0];
            for (int i = 1; i < _weeklyRanks.Length; i++)
            {
                if (_weeklyRanks[i] > maxRank) maxRank = _weeklyRanks[i];
                if (_weeklyRanks[i] < minRank) minRank = _weeklyRanks[i];
            }
            int rankRange = Math.Max(maxRank - minRank, 1);

            for (int i = 0; i < _weeklyRanks.Length; i++)
            {
                int rank = _weeklyRanks[i];
                // 排名越差（数值越大），柱越高
                float normalizedHeight = (float)(rank - minRank) / rankRange;
                float barH = Mathf.Lerp(chartHeight * 0.4f, chartHeight, 1f - normalizedHeight);
                // 排名最好（最后一天）使用渐变亮金色
                bool isCurrent = i == _weeklyRanks.Length - 1;

                float barX = i * (barWidth + barGap);
                float barY = chartHeight - barH;

                Color barColor;
                if (isCurrent)
                {
                    barColor = InkWashTheme.GoldBright;
                }
                else if (normalizedHeight < 0.4f)
                {
                    barColor = new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.55f);
                }
                else
                {
                    barColor = new Color(InkWashTheme.TextTertiary.R, InkWashTheme.TextTertiary.G, InkWashTheme.TextTertiary.B, 0.45f);
                }

                var bar = new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(barX, barY),
                    Size = new Float2(barWidth, barH),
                    BackgroundColor = barColor,
                };
                _trendChartHost.AddChild(bar);

                // 星期标签
                var dayLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(barX, chartHeight + 2f),
                    Size = new Float2(barWidth, 14f),
                    Text = _weekdayLabels[i],
                    TextColor = isCurrent ? InkWashTheme.GoldPrimary : InkWashTheme.TextTertiary,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 9f),
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                };
                _trendChartHost.AddChild(dayLabel);
            }
        }

        /// <summary>
        /// 构建赛季奖励条目列表（3 个奖励）。
        /// </summary>
        private void BuildRewardList()
        {
            float cursorY = 0f;
            float itemHeight = 36f;
            float itemGap = 6f;

            for (int i = 0; i < _rewards.Length; i++)
            {
                var reward = _rewards[i];
                Color qualityColor;
                switch (reward.Quality)
                {
                    case "Legendary":
                        qualityColor = InkWashTheme.GoldBright;
                        break;
                    case "Rare":
                        qualityColor = InkWashTheme.JadePrimary;
                        break;
                    case "Epic":
                        qualityColor = InkWashTheme.VermilionPrimary;
                        break;
                    default:
                        qualityColor = InkWashTheme.TextSecondary;
                        break;
                }

                var item = new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(0f, cursorY),
                    Size = new Float2(_rewardListHost.Width, itemHeight),
                    BackgroundColor = new Color(qualityColor.R, qualityColor.G, qualityColor.B, 0.08f),
                };

                // 奖励图标徽章
                var iconBox = new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(6f, 4f),
                    Size = new Float2(28f, 28f),
                    BackgroundColor = new Color(qualityColor.R, qualityColor.G, qualityColor.B, 0.15f),
                };
                var iconLabel = new Label
                {
                    AnchorPreset = AnchorPresets.StretchAll,
                    Offsets = Margin.Zero,
                    Text = reward.Symbol,
                    TextColor = qualityColor,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 14f),
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                };
                iconBox.AddChild(iconLabel);
                item.AddChild(iconBox);

                // 名称
                var nameLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(40f, 4f),
                    Size = new Float2(_rewardListHost.Width - 100f, 16f),
                    Text = reward.Name,
                    TextColor = InkWashTheme.TextDefault,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 12f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                item.AddChild(nameLabel);

                // 描述
                var descLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(40f, 18f),
                    Size = new Float2(_rewardListHost.Width - 100f, 16f),
                    Text = reward.Desc,
                    TextColor = InkWashTheme.TextTertiary,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 10f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                item.AddChild(descLabel);

                // 数量
                var countLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(_rewardListHost.Width - 70f, 4f),
                    Size = new Float2(64f, 28f),
                    Text = "x" + reward.Count,
                    TextColor = qualityColor,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 14f),
                    HorizontalAlignment = TextAlignment.Far,
                    VerticalAlignment = TextAlignment.Center,
                };
                item.AddChild(countLabel);

                _rewardListHost.AddChild(item);
                cursorY += itemHeight + itemGap;
            }
        }

        // ===================================================================
        // Build 方法 — 底部按钮栏
        // =======================================================================

        /// <summary>
        /// 构建底部按钮栏：返回沉浸模式。
        /// </summary>
        private void BuildBottomBar()
        {
            _bottomBar = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(BottomBtnWidth, BottomBarHeight),
            };

            _backToHudButton = new InkButton
            {
                Variant = InkButtonVariant.Default,
                ButtonSize = InkButtonSize.Md,
                Text = "返回沉浸模式",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 6f),
                Size = new Float2(BottomBtnWidth, ActionBtnHeight),
            };
            _backToHudButton.ButtonClicked += (b) => OnNavigationButtonClicked(InkPageDomIds.CombatHud, b);
            _bottomBar.AddChild(_backToHudButton);

            AddChild(_bottomBar);
        }

        // ===================================================================
        // 事件处理
        // =======================================================================

        /// <summary>
        /// 导航按钮点击处理：发射金粉粒子 + 触发导航请求。
        /// </summary>
        private void OnNavigationButtonClicked(string domId, Button sourceButton)
        {
            try
            {
                EmitGoldAtButton(sourceButton);
                NavigationRequested?.Invoke(domId);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[LeaderboardPage] NavigationRequested({domId}) 触发失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 在按钮中心位置触发金粉爆发粒子反馈。
        /// </summary>
        private void EmitGoldAtButton(Button button)
        {
            try
            {
                if (ParticleSystem == null || button == null)
                    return;

                var buttonCenter = new Float2(button.Width * 0.5f, button.Height * 0.5f);
                var screenPos = button.PointToScreen(buttonCenter);
                var localPos = ParticleSystem.PointFromScreen(screenPos);
                ParticleSystem.EmitGoldBurst(localPos, count: 14, isLarge: false);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogWarning($"[LeaderboardPage] EmitGoldAtButton 失败: {ex.Message}");
            }
        }

        // ===================================================================
        // IInkPage 实现
        // =======================================================================

        /// <inheritdoc />
        public void RefreshLayout()
        {
            try
            {
                float sw = Width;
                float sh = Height;
                float contentTop = TopHeaderHeight + ScreenEdge;
                float contentBottom = sh - BottomBarHeight - ScreenEdge;
                float contentH = contentBottom - contentTop;
                float middleWidth = sw - LeftColumnWidth - RightColumnWidth - ColumnGap * 2f - ScreenEdge * 2f;

                // 顶部标题栏：顶部全宽
                if (_topHeader != null)
                {
                    _topHeader.Location = new Float2(ScreenEdge, ScreenEdge);
                    _topHeader.Size = new Float2(sw - ScreenEdge * 2f, TopHeaderHeight);
                }

                // 左栏：左上角
                if (_leftPanel != null)
                {
                    _leftPanel.Location = new Float2(ScreenEdge, contentTop);
                    _leftPanel.Size = new Float2(LeftColumnWidth, contentH);
                }

                // 中栏：左栏右侧
                if (_middlePanel != null)
                {
                    _middlePanel.Location = new Float2(ScreenEdge + LeftColumnWidth + ColumnGap, contentTop);
                    _middlePanel.Size = new Float2(middleWidth, contentH);

                    // 同步颁奖台宿主宽度
                    if (_podiumHost != null)
                    {
                        _podiumHost.Size = new Float2(middleWidth - ScreenEdge * 2f, PodiumHeight);
                        RebuildPodiumLayout(middleWidth - ScreenEdge * 2f);
                    }

                    // 同步表格面板宽度
                    if (_tablePanel != null)
                    {
                        float tableWidth = middleWidth - ScreenEdge * 2f;
                        _tablePanel.Size = new Float2(tableWidth, contentH - PodiumHeight - 16f);
                        if (_tableHeader != null)
                        {
                            _tableHeader.Size = new Float2(tableWidth, TableHeaderHeight);
                        }
                        if (_tableBodyHost != null)
                        {
                            _tableBodyHost.Size = new Float2(tableWidth, 12 * TableRowHeight);
                            // 同步所有行的宽度
                            foreach (var row in _tableRows)
                            {
                                row.Size = new Float2(tableWidth, TableRowHeight);
                            }
                        }
                        if (_loadMoreLabel != null)
                        {
                            _loadMoreLabel.Location = new Float2(0f, TableHeaderHeight + 12 * TableRowHeight + 8f);
                            _loadMoreLabel.Size = new Float2(tableWidth, 24f);
                        }
                    }
                }

                // 右栏：右侧
                if (_rightPanel != null)
                {
                    _rightPanel.Location = new Float2(sw - RightColumnWidth - ScreenEdge, contentTop);
                    _rightPanel.Size = new Float2(RightColumnWidth, contentH);
                }

                // 底部按钮栏：底部居中
                if (_bottomBar != null)
                {
                    _bottomBar.Location = new Float2(sw * 0.5f - BottomBtnWidth * 0.5f, sh - BottomBarHeight - ScreenEdge * 0.5f);
                    _bottomBar.Size = new Float2(BottomBtnWidth, BottomBarHeight);
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[LeaderboardPage] RefreshLayout 失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 重新计算颁奖台 3 张卡片的位置与尺寸（RefreshLayout 时调用）。
        /// </summary>
        private void RebuildPodiumLayout(float totalWidth)
        {
            if (_podiumHost == null)
                return;

            float cardWidth = (totalWidth - ColumnGap * 2f) / 3f;

            if (_podiumSilver != null)
            {
                _podiumSilver.Location = new Float2(0f, 12f);
                _podiumSilver.Size = new Float2(cardWidth, PodiumHeight - 12f);
            }
            if (_podiumGold != null)
            {
                _podiumGold.Location = new Float2(cardWidth + ColumnGap, 0f);
                _podiumGold.Size = new Float2(cardWidth, PodiumHeight);
            }
            if (_podiumBronze != null)
            {
                _podiumBronze.Location = new Float2((cardWidth + ColumnGap) * 2f, 16f);
                _podiumBronze.Size = new Float2(cardWidth, PodiumHeight - 16f);
            }
        }

        /// <inheritdoc />
        public override void OnParentResized()
        {
            base.OnParentResized();
            RefreshLayout();
        }
    }
}

using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.StyleSystem;
using System;

namespace HundunWorld.Game.UI.Ink.Pages.Social
{
    /// <summary>
    /// 江湖门派页面 — 对应 social-guild.html 设计原型。
    /// <para>
    /// 水墨古风门派管理界面，承担玩家查看所属门派、申请加入他派、
    /// 浏览门派成员与门派周常任务/活动的核心入口。
    /// 整体布局沿用 HTML 原型的三栏式结构：
    /// <list type="bullet">
    ///   <item>顶部：标题"江湖门派" + 当前门派名 + 职位 + 关闭按钮</item>
    ///   <item>左栏：门派列表（已加入 + 可申请，共 6 个门派条目）</item>
    ///   <item>中栏：门派详情（徽章/名称/等级/声望条/描述/掌门人/成员列表 12 名）</item>
    ///   <item>右栏：门派周常任务（4 项）+ 活动时间表（3 项）+ 福利领取按钮</item>
    ///   <item>底部：返回沉浸模式 + 跳转好友列表（NavFriends）</item>
    /// </list>
    /// 通过 <see cref="NavigationRequested"/> 事件向路由器暴露导航请求，
    /// 关闭按钮与底部"返回沉浸模式"按钮均触发 <see cref="InkPageDomIds.CombatHud"/>。
    /// </para>
    /// <para>
    /// 当前实现全部使用 mock 数据；后续接入门派系统时，
    /// 通过刷新方法替换列表内容即可。
    /// </para>
    /// </summary>
    public class SocialGuildPage : ContainerControl, IInkPage
    {
        // ===================================================================
        // 布局常量
        // =======================================================================

        /// <summary>顶部标题栏高度（像素）</summary>
        private const float HeaderHeight = 60f;

        /// <summary>底部导航按钮栏高度（像素）</summary>
        private const float BottomNavHeight = 36f;

        /// <summary>屏幕边距（像素）</summary>
        private const float ScreenEdge = 16f;

        /// <summary>区域间距（像素）</summary>
        private const float RegionGap = 12f;

        /// <summary>左栏门派列表宽度占比（占内容区宽度）</summary>
        private const float LeftRatio = 0.22f;

        /// <summary>右栏门派任务宽度占比（占内容区宽度）</summary>
        private const float RightRatio = 0.30f;

        /// <summary>导航按钮宽度（像素）</summary>
        private const float NavBtnWidth = 140f;

        /// <summary>导航按钮间距（像素）</summary>
        private const float NavBtnGap = 8f;

        /// <summary>门派列表项高度（像素）</summary>
        private const float GuildItemHeight = 56f;

        /// <summary>门派列表项间距（像素）</summary>
        private const float GuildItemGap = 4f;

        /// <summary>成员列表项高度（像素）</summary>
        private const float MemberItemHeight = 28f;

        /// <summary>成员列表项间距（像素）</summary>
        private const float MemberItemGap = 2f;

        // ===================================================================
        // 子控件引用 — 顶部
        // =======================================================================

        /// <summary>顶部标题栏面板</summary>
        private InkPanel _headerPanel;

        /// <summary>页面标题"江湖门派"</summary>
        private Label _titleLabel;

        /// <summary>当前门派名标签</summary>
        private Label _currentGuildLabel;

        /// <summary>当前职位标签</summary>
        private Label _currentPositionLabel;

        /// <summary>关闭按钮（关闭门派页，返回 HUD）</summary>
        private InkButton _closeButton;

        // ===================================================================
        // 子控件引用 — 左栏门派列表
        // =======================================================================

        /// <summary>左栏门派列表面板</summary>
        private InkPanel _guildListPanel;

        /// <summary>左栏标题"门派列表"</summary>
        private Label _guildListTitle;

        /// <summary>门派列表项容器数组（6 个）</summary>
        private InkListItem[] _guildItems;

        /// <summary>门派列表项名称标签数组</summary>
        private Label[] _guildItemNames;

        /// <summary>门派列表项等级标签数组</summary>
        private Label[] _guildItemLevels;

        /// <summary>当前选中的门派索引</summary>
        private int _selectedGuildIndex = 0;

        // ===================================================================
        // 子控件引用 — 中栏门派详情
        // =======================================================================

        /// <summary>中栏门派详情面板</summary>
        private InkPanel _detailPanel;

        /// <summary>门派徽章占位（用 InkCell 显示）</summary>
        private InkCell _guildBadgeCell;

        /// <summary>门派名称标签</summary>
        private Label _detailNameLabel;

        /// <summary>门派等级标签</summary>
        private Label _detailLevelLabel;

        /// <summary>门派声望进度条</summary>
        private InkBar _reputationBar;

        /// <summary>门派声望数值标签</summary>
        private Label _reputationLabel;

        /// <summary>门派描述标签</summary>
        private Label _descLabel;

        /// <summary>掌门人信息标签</summary>
        private Label _masterLabel;

        /// <summary>副掌门信息标签</summary>
        private Label _viceMasterLabel;

        /// <summary>成员列表区标题</summary>
        private Label _memberListTitle;

        /// <summary>成员列表项容器数组（12 名）</summary>
        private InkListItem[] _memberItems;

        /// <summary>成员姓名标签数组</summary>
        private Label[] _memberNames;

        /// <summary>成员职位标签数组</summary>
        private Label[] _memberPositions;

        /// <summary>成员在线状态标签数组</summary>
        private Label[] _memberStatus;

        /// <summary>成员贡献标签数组</summary>
        private Label[] _memberContrib;

        // ===================================================================
        // 子控件引用 — 右栏门派任务/活动
        // =======================================================================

        /// <summary>右栏任务/活动面板</summary>
        private InkPanel _taskPanel;

        /// <summary>周常任务区标题</summary>
        private Label _weeklyTaskTitle;

        /// <summary>周常任务条目标签数组（4 项）</summary>
        private Label[] _weeklyTaskLabels;

        /// <summary>周常任务进度条数组（4 项）</summary>
        private InkBar[] _weeklyTaskBars;

        /// <summary>活动时间表区标题</summary>
        private Label _activityTitle;

        /// <summary>活动条目标签数组（3 项）</summary>
        private Label[] _activityLabels;

        /// <summary>福利领取区标题</summary>
        private Label _welfareTitle;

        /// <summary>福利领取按钮</summary>
        private InkButton _claimWelfareButton;

        // ===================================================================
        // 子控件引用 — 底部
        // =======================================================================

        /// <summary>底部导航按钮面板</summary>
        private InkPanel _bottomNavPanel;

        /// <summary>"返回沉浸模式"按钮</summary>
        private InkButton _returnHudButton;

        /// <summary>"好友列表"按钮</summary>
        private InkButton _gotoFriendsButton;

        // ===================================================================
        // 公共 API
        // =======================================================================

        /// <summary>
        /// 导航请求事件。由关闭按钮与底部导航按钮触发，
        /// 参数为 <see cref="InkPageDomIds"/> 中定义的 dom-id 字符串。
        /// </summary>
        public event Action<string> NavigationRequested;

        /// <summary>
        /// 粒子动效系统引用（可选，由外部注入）。
        /// 用于在按钮点击位置触发金粉爆发反馈。
        /// </summary>
        public InkParticleSystem ParticleSystem { get; set; }

        // ===================================================================
        // 构造函数
        // =======================================================================

        /// <summary>
        /// 构造函数：初始化全部子控件并填充 mock 数据。
        /// </summary>
        public SocialGuildPage()
        {
            try
            {
                AnchorPreset = AnchorPresets.StretchAll;
                Offsets = Margin.Zero;
                BackgroundColor = Color.Transparent;
                ClipChildren = false;
                AutoFocus = false;

                BuildHeader();
                BuildGuildList();
                BuildDetailPanel();
                BuildTaskPanel();
                BuildBottomNav();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[SocialGuildPage] 初始化失败: {ex.Message}");
            }
        }

        // ===================================================================
        // Build 方法
        // =======================================================================

        /// <summary>
        /// 构建顶部标题栏：标题 + 当前门派名 + 职位 + 关闭按钮。
        /// </summary>
        private void BuildHeader()
        {
            _headerPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
            };

            // 页面标题"江湖门派"
            _titleLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(20f, 0f),
                Size = new Float2(160f, HeaderHeight),
                Text = "江湖门派",
                TextColor = InkWashTheme.TextGold,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 22f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _headerPanel.AddChild(_titleLabel);

            // 当前门派名
            _currentGuildLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(200f, 0f),
                Size = new Float2(200f, HeaderHeight),
                Text = "当前门派：武当派",
                TextColor = InkWashTheme.TextDefault,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 14f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _headerPanel.AddChild(_currentGuildLabel);

            // 当前职位
            _currentPositionLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(400f, 0f),
                Size = new Float2(160f, HeaderHeight),
                Text = "职位：精英弟子",
                TextColor = InkWashTheme.TextBrand,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 14f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _headerPanel.AddChild(_currentPositionLabel);

            // 关闭按钮（右上方，RefreshLayout 中靠右定位）
            _closeButton = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Sm,
                Text = "✕",
                AnchorPreset = AnchorPresets.TopRight,
                Size = new Float2(32f, 32f),
            };
            _closeButton.ButtonClicked += (b) => OnSystemNavButtonClicked(InkPageDomIds.CombatHud, b);
            _headerPanel.AddChild(_closeButton);

            AddChild(_headerPanel);
        }

        /// <summary>
        /// 构建左栏门派列表：6 个门派条目（已加入 1 + 可申请 5）。
        /// </summary>
        private void BuildGuildList()
        {
            _guildListPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
            };

            _guildListTitle = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 10f),
                Size = new Float2(200f, 20f),
                Text = "◆ 门派列表",
                TextColor = InkWashTheme.TextBrand,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 14f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _guildListPanel.AddChild(_guildListTitle);

            // 6 个门派 mock 数据：名称 + 等级
            string[] guildNames =
            {
                "武当派",     // 已加入（当前）
                "少林派",
                "峨眉派",
                "丐帮",
                "唐门",
                "明教",
            };
            string[] guildLevels = { "Lv.10", "Lv.9", "Lv.8", "Lv.9", "Lv.7", "Lv.8" };

            _guildItems = new InkListItem[guildNames.Length];
            _guildItemNames = new Label[guildNames.Length];
            _guildItemLevels = new Label[guildNames.Length];

            for (int i = 0; i < guildNames.Length; i++)
            {
                var item = new InkListItem
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Size = new Float2(220f, GuildItemHeight),
                    Active = (i == _selectedGuildIndex),
                };
                _guildItems[i] = item;
                _guildListPanel.AddChild(item);

                // 门派名称
                var nameLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(14f, 6f),
                    Size = new Float2(150f, 22f),
                    Text = (i == 0 ? "★ " : "  ") + guildNames[i],
                    TextColor = (i == 0) ? InkWashTheme.TextGold : InkWashTheme.TextDefault,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 14f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                _guildItemNames[i] = nameLabel;
                item.AddChild(nameLabel);

                // 门派等级
                var levelLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(14f, 30f),
                    Size = new Float2(180f, 18f),
                    Text = guildLevels[i] + (i == 0 ? "  · 已加入" : "  · 可申请"),
                    TextColor = InkWashTheme.TextSecondary,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 12f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                _guildItemLevels[i] = levelLabel;
                item.AddChild(levelLabel);
            }

            // 注：InkListItem 未内置 Clicked 事件，门派选中态保留构造时的默认值（第 0 项激活）。
            // 后续接入门派系统时，可通过派生子类或外部 OnMouseDown 路由实现点击切换。

            AddChild(_guildListPanel);
        }

        /// <summary>
        /// 构建中栏门派详情：徽章 + 名称 + 等级 + 声望条 + 描述 + 掌门/副掌门 + 成员列表（12 名）。
        /// </summary>
        private void BuildDetailPanel()
        {
            _detailPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
            };

            // 门派徽章（72×72 传说品质边框）
            _guildBadgeCell = new InkCell
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(20f, 16f),
                Size = new Float2(72f, 72f),
                Quality = InkWashTheme.InkQuality.Legendary,
            };
            _detailPanel.AddChild(_guildBadgeCell);

            // 门派名称
            _detailNameLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(108f, 16f),
                Size = new Float2(260f, 30f),
                Text = "武当派",
                TextColor = InkWashTheme.QualityTextColor(InkWashTheme.InkQuality.Legendary),
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 22f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _detailPanel.AddChild(_detailNameLabel);

            // 门派等级
            _detailLevelLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(108f, 48f),
                Size = new Float2(260f, 20f),
                Text = "门派等级  Lv.10",
                TextColor = InkWashTheme.TextBrand,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _detailPanel.AddChild(_detailLevelLabel);

            // 声望进度条
            _reputationBar = new InkBar
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(108f, 72f),
                Size = new Float2(200f, 8f),
                Value = 0.75f,
                FillVariant = InkBarFillVariant.Gold,
            };
            _detailPanel.AddChild(_reputationBar);

            // 声望数值
            _reputationLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(316f, 68f),
                Size = new Float2(100f, 16f),
                Text = "声望 7500/10000",
                TextColor = InkWashTheme.TextSecondary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 11f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _detailPanel.AddChild(_reputationLabel);

            // 门派描述
            _descLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(20f, 100f),
                Size = new Float2(400f, 50f),
                Text = "武当派立派于湖北武当山，以太极内功与剑法闻名天下。门派讲究以柔克刚、以静制动，弟子多修道兼修武。",
                TextColor = InkWashTheme.TextSecondary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 12f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Near,
            };
            _detailPanel.AddChild(_descLabel);

            // 掌门人
            _masterLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(20f, 156f),
                Size = new Float2(400f, 20f),
                Text = "掌门：玄虚真人  ·  等级 Lv.99  ·  在线",
                TextColor = InkWashTheme.TextGold,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _detailPanel.AddChild(_masterLabel);

            // 副掌门
            _viceMasterLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(20f, 178f),
                Size = new Float2(400f, 20f),
                Text = "副掌门：清风道长  ·  等级 Lv.95  ·  在线",
                TextColor = InkWashTheme.TextDefault,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _detailPanel.AddChild(_viceMasterLabel);

            // 成员列表区标题
            _memberListTitle = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(20f, 206f),
                Size = new Float2(400f, 20f),
                Text = "◆ 门派成员（12 / 80）",
                TextColor = InkWashTheme.TextBrand,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _detailPanel.AddChild(_memberListTitle);

            // 12 名成员 mock 数据：姓名 / 职位 / 在线状态 / 贡献
            string[][] members =
            {
                new[] { "玄虚真人", "掌门",     "在线", "12800" },
                new[] { "清风道长", "副掌门",   "在线", "9600"  },
                new[] { "苍松子",   "长老",     "在线", "7200"  },
                new[] { "白云散人", "长老",     "离线", "6800"  },
                new[] { "青衣客",   "精英弟子", "在线", "4500"  },
                new[] { "太极剑",   "精英弟子", "在线", "4200"  },
                new[] { "玉虚子",   "弟子",     "在线", "3100"  },
                new[] { "凌霄子",   "弟子",     "离线", "2800"  },
                new[] { "紫阳子",   "弟子",     "在线", "2500"  },
                new[] { "玄机子",   "弟子",     "在线", "2100"  },
                new[] { "清虚子",   "弟子",     "离线", "1800"  },
                new[] { "青云子",   "记名弟子", "在线", "900"   },
            };

            _memberItems = new InkListItem[members.Length];
            _memberNames = new Label[members.Length];
            _memberPositions = new Label[members.Length];
            _memberStatus = new Label[members.Length];
            _memberContrib = new Label[members.Length];

            float memberStartY = 230f;
            for (int i = 0; i < members.Length; i++)
            {
                var item = new InkListItem
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(20f, memberStartY + i * (MemberItemHeight + MemberItemGap)),
                    Size = new Float2(440f, MemberItemHeight),
                };
                _memberItems[i] = item;
                _detailPanel.AddChild(item);

                bool isOnline = members[i][2] == "在线";

                // 姓名
                var nameLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(8f, 0f),
                    Size = new Float2(110f, MemberItemHeight),
                    Text = members[i][0],
                    TextColor = isOnline ? InkWashTheme.TextDefault : InkWashTheme.TextTertiary,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 12f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                _memberNames[i] = nameLabel;
                item.AddChild(nameLabel);

                // 职位
                var posLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(130f, 0f),
                    Size = new Float2(90f, MemberItemHeight),
                    Text = members[i][1],
                    TextColor = InkWashTheme.TextBrand,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 12f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                _memberPositions[i] = posLabel;
                item.AddChild(posLabel);

                // 在线状态
                var statusLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(230f, 0f),
                    Size = new Float2(80f, MemberItemHeight),
                    Text = members[i][2],
                    TextColor = isOnline ? InkWashTheme.TextJade : InkWashTheme.TextTertiary,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 12f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                _memberStatus[i] = statusLabel;
                item.AddChild(statusLabel);

                // 贡献
                var contribLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(320f, 0f),
                    Size = new Float2(110f, MemberItemHeight),
                    Text = "贡献 " + members[i][3],
                    TextColor = InkWashTheme.TextGold,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 12f),
                    HorizontalAlignment = TextAlignment.Far,
                    VerticalAlignment = TextAlignment.Center,
                };
                _memberContrib[i] = contribLabel;
                item.AddChild(contribLabel);
            }

            AddChild(_detailPanel);
        }

        /// <summary>
        /// 构建右栏门派任务/活动面板：周常任务（4 项）+ 活动时间表（3 项）+ 福利领取按钮。
        /// </summary>
        private void BuildTaskPanel()
        {
            _taskPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
            };

            // 周常任务区标题
            _weeklyTaskTitle = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 10f),
                Size = new Float2(300f, 20f),
                Text = "◆ 门派周常任务",
                TextColor = InkWashTheme.TextBrand,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 14f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _taskPanel.AddChild(_weeklyTaskTitle);

            // 4 项周常任务 mock 数据
            string[] weeklyTasks =
            {
                "门派巡逻  3/5",
                "门派捐献  500/1000",
                "护派任务  2/3",
                "门派试炼  0/2",
            };
            float[] weeklyProgress = { 0.6f, 0.5f, 0.66f, 0f };

            _weeklyTaskLabels = new Label[weeklyTasks.Length];
            _weeklyTaskBars = new InkBar[weeklyTasks.Length];

            float taskStartY = 36f;
            for (int i = 0; i < weeklyTasks.Length; i++)
            {
                var taskLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(16f, taskStartY + i * 36f),
                    Size = new Float2(280f, 18f),
                    Text = "• " + weeklyTasks[i],
                    TextColor = InkWashTheme.TextDefault,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 12f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                _weeklyTaskLabels[i] = taskLabel;
                _taskPanel.AddChild(taskLabel);

                var taskBar = new InkBar
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(16f, taskStartY + i * 36f + 20f),
                    Size = new Float2(280f, 6f),
                    Value = weeklyProgress[i],
                    FillVariant = InkBarFillVariant.Jade,
                };
                _weeklyTaskBars[i] = taskBar;
                _taskPanel.AddChild(taskBar);
            }

            // 活动时间表区标题
            float activityTitleY = taskStartY + weeklyTasks.Length * 36f + 12f;
            _activityTitle = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, activityTitleY),
                Size = new Float2(300f, 20f),
                Text = "◆ 门派活动时间表",
                TextColor = InkWashTheme.TextBrand,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 14f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _taskPanel.AddChild(_activityTitle);

            // 3 项活动 mock 数据
            string[] activities =
            {
                "今晚 20:00  门派试炼",
                "周三 21:00  门派战",
                "周日 19:00  门派集会",
            };
            _activityLabels = new Label[activities.Length];
            for (int i = 0; i < activities.Length; i++)
            {
                var actLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(16f, activityTitleY + 26f + i * 22f),
                    Size = new Float2(280f, 20f),
                    Text = "▸ " + activities[i],
                    TextColor = InkWashTheme.TextSecondary,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 12f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                _activityLabels[i] = actLabel;
                _taskPanel.AddChild(actLabel);
            }

            // 福利领取区标题
            float welfareTitleY = activityTitleY + 26f + activities.Length * 22f + 12f;
            _welfareTitle = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, welfareTitleY),
                Size = new Float2(300f, 20f),
                Text = "◆ 门派福利",
                TextColor = InkWashTheme.TextBrand,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 14f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _taskPanel.AddChild(_welfareTitle);

            // 福利描述
            var welfareDesc = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, welfareTitleY + 24f),
                Size = new Float2(280f, 36f),
                Text = "每日门派贡献宝箱 ×1\n灵石 ×500、门派声望 ×100",
                TextColor = InkWashTheme.TextSecondary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 12f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Near,
            };
            _taskPanel.AddChild(welfareDesc);

            // 福利领取按钮
            _claimWelfareButton = new InkButton
            {
                Variant = InkButtonVariant.Primary,
                ButtonSize = InkButtonSize.Md,
                Text = "领取福利",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, welfareTitleY + 66f),
                Size = new Float2(280f, 32f),
            };
            _claimWelfareButton.ButtonClicked += (b) => EmitGoldAtButton(b);
            _taskPanel.AddChild(_claimWelfareButton);

            AddChild(_taskPanel);
        }

        /// <summary>
        /// 构建底部导航按钮栏：返回沉浸模式 / 好友列表。
        /// </summary>
        private void BuildBottomNav()
        {
            _bottomNavPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.BottomLeft,
            };

            _returnHudButton = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Md,
                Text = "返回沉浸模式",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 0f),
                Size = new Float2(NavBtnWidth, BottomNavHeight),
            };
            _returnHudButton.ButtonClicked += (b) => OnSystemNavButtonClicked(InkPageDomIds.CombatHud, b);
            _bottomNavPanel.AddChild(_returnHudButton);

            _gotoFriendsButton = new InkButton
            {
                Variant = InkButtonVariant.Default,
                ButtonSize = InkButtonSize.Md,
                Text = "好友列表",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(NavBtnWidth + NavBtnGap, 0f),
                Size = new Float2(NavBtnWidth, BottomNavHeight),
            };
            _gotoFriendsButton.ButtonClicked += (b) =>
                OnSystemNavButtonClicked(InkPageDomIds.NavFriends, b);
            _bottomNavPanel.AddChild(_gotoFriendsButton);

            AddChild(_bottomNavPanel);
        }

        // ===================================================================
        // 事件处理
        // =======================================================================

        /// <summary>
        /// 门派列表项点击处理：切换选中门派并刷新视觉态。
        /// </summary>
        private void OnGuildItemClicked(int index)
        {
            try
            {
                if (index < 0 || index >= _guildItems.Length)
                    return;
                _selectedGuildIndex = index;
                ApplyGuildSelectionHighlight();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[SocialGuildPage] 门派切换失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 根据当前选中的门派索引更新所有门派列表项的视觉态。
        /// </summary>
        private void ApplyGuildSelectionHighlight()
        {
            if (_guildItems == null)
                return;
            for (int i = 0; i < _guildItems.Length; i++)
            {
                if (_guildItems[i] == null)
                    continue;
                _guildItems[i].Active = (i == _selectedGuildIndex);
            }
        }

        /// <summary>
        /// 系统导航按钮点击处理：发射金粉粒子 + 触发导航请求。
        /// </summary>
        private void OnSystemNavButtonClicked(string domId, Button sourceButton)
        {
            try
            {
                EmitGoldAtButton(sourceButton);
                NavigationRequested?.Invoke(domId);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError(
                    $"[SocialGuildPage] NavigationRequested({domId}) 触发失败: {ex.Message}");
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
                FlaxEngine.Debug.LogWarning($"[SocialGuildPage] EmitGoldAtButton 失败: {ex.Message}");
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
                float w = Width;
                float h = Height;
                float panelX = ScreenEdge;
                float panelW = w - ScreenEdge * 2f;

                // 1. 顶部标题栏：顶部全宽
                if (_headerPanel != null)
                {
                    _headerPanel.Location = new Float2(panelX, ScreenEdge);
                    _headerPanel.Size = new Float2(panelW, HeaderHeight);

                    // 关闭按钮靠右
                    if (_closeButton != null)
                    {
                        _closeButton.Location = new Float2(panelW - 40f, (HeaderHeight - 32f) * 0.5f);
                    }
                }

                // 2. 底部导航栏：底部全宽
                float bottomNavY = h - ScreenEdge - BottomNavHeight;
                if (_bottomNavPanel != null)
                {
                    _bottomNavPanel.Location = new Float2(panelX, bottomNavY);
                    _bottomNavPanel.Size = new Float2(panelW, BottomNavHeight);
                }

                // 3. 内容区：顶部下方 → 底部上方
                float contentTop = ScreenEdge + HeaderHeight + RegionGap;
                float contentBottom = bottomNavY - RegionGap;
                float contentH = contentBottom - contentTop;
                if (contentH < 100f)
                    contentH = 100f;

                float leftW = panelW * LeftRatio;
                float rightW = panelW * RightRatio;
                float centerW = panelW - leftW - rightW - RegionGap * 2f;

                // 4. 左栏门派列表
                if (_guildListPanel != null)
                {
                    _guildListPanel.Location = new Float2(panelX, contentTop);
                    _guildListPanel.Size = new Float2(leftW, contentH);

                    // 门派列表项按列宽重新布局
                    float itemW = leftW - 24f;
                    float itemStartY = 40f;
                    if (_guildItems != null)
                    {
                        for (int i = 0; i < _guildItems.Length; i++)
                        {
                            if (_guildItems[i] == null)
                                continue;
                            _guildItems[i].Location = new Float2(12f, itemStartY + i * (GuildItemHeight + GuildItemGap));
                            _guildItems[i].Size = new Float2(itemW, GuildItemHeight);
                        }
                    }
                }

                // 5. 中栏门派详情
                if (_detailPanel != null)
                {
                    float detailX = panelX + leftW + RegionGap;
                    _detailPanel.Location = new Float2(detailX, contentTop);
                    _detailPanel.Size = new Float2(centerW, contentH);

                    // 成员列表项按详情面板宽度重新布局
                    float memberRowW = centerW - 40f;
                    if (_memberItems != null)
                    {
                        float memberStartY = 230f;
                        for (int i = 0; i < _memberItems.Length; i++)
                        {
                            if (_memberItems[i] == null)
                                continue;
                            _memberItems[i].Location = new Float2(20f, memberStartY + i * (MemberItemHeight + MemberItemGap));
                            _memberItems[i].Size = new Float2(memberRowW, MemberItemHeight);
                        }
                    }

                    // 描述区按宽度自适应
                    if (_descLabel != null)
                    {
                        _descLabel.Size = new Float2(centerW - 40f, 50f);
                    }
                    if (_masterLabel != null)
                    {
                        _masterLabel.Size = new Float2(centerW - 40f, 20f);
                    }
                    if (_viceMasterLabel != null)
                    {
                        _viceMasterLabel.Size = new Float2(centerW - 40f, 20f);
                    }
                    if (_memberListTitle != null)
                    {
                        _memberListTitle.Size = new Float2(centerW - 40f, 20f);
                    }
                }

                // 6. 右栏任务/活动面板
                if (_taskPanel != null)
                {
                    float taskX = panelX + leftW + RegionGap + centerW + RegionGap;
                    _taskPanel.Location = new Float2(taskX, contentTop);
                    _taskPanel.Size = new Float2(rightW, contentH);

                    // 内部子控件按宽度自适应
                    float innerW = rightW - 32f;
                    if (_weeklyTaskTitle != null)
                        _weeklyTaskTitle.Size = new Float2(innerW, 20f);
                    if (_weeklyTaskLabels != null)
                    {
                        for (int i = 0; i < _weeklyTaskLabels.Length; i++)
                        {
                            if (_weeklyTaskLabels[i] != null)
                                _weeklyTaskLabels[i].Size = new Float2(innerW, 18f);
                            if (_weeklyTaskBars != null && _weeklyTaskBars[i] != null)
                                _weeklyTaskBars[i].Size = new Float2(innerW, 6f);
                        }
                    }
                    if (_activityTitle != null)
                        _activityTitle.Size = new Float2(innerW, 20f);
                    if (_activityLabels != null)
                    {
                        for (int i = 0; i < _activityLabels.Length; i++)
                        {
                            if (_activityLabels[i] != null)
                                _activityLabels[i].Size = new Float2(innerW, 20f);
                        }
                    }
                    if (_welfareTitle != null)
                        _welfareTitle.Size = new Float2(innerW, 20f);
                    if (_claimWelfareButton != null)
                        _claimWelfareButton.Size = new Float2(innerW, 32f);
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[SocialGuildPage] RefreshLayout 失败: {ex.Message}");
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

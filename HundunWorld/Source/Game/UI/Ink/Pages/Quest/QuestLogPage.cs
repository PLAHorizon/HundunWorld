using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.StyleSystem;
using System;

namespace HundunWorld.Game.UI.Ink.Pages.Quest
{
    /// <summary>
    /// 江湖任务志页面 — 对应 quest-log.html 设计原型。
    /// <para>
    /// 水墨古风任务管理界面，承担玩家查看与追踪所接江湖任务的核心入口。
    /// 整体布局沿用 HTML 原型的三栏式结构：
    /// <list type="bullet">
    ///   <item>顶部：页面标题"江湖任务志" + 玩家信息（姓名/等级/门派）+ 关闭按钮</item>
    ///   <item>左栏：6 个垂直任务分类 Tab（主线/支线/日常/师门/门派/奇遇）</item>
    ///   <item>中栏：选中分类下的任务列表，每条显示任务名/等级/状态/进度条</item>
    ///   <item>右栏：任务详情面板（标题+品质徽章/剧情描述/目标列表/奖励预览/追踪·放弃·传送按钮）</item>
    ///   <item>底部：返回沉浸模式按钮（触发 <see cref="InkPageDomIds.CombatHud"/>）</item>
    /// </list>
    /// 通过 <see cref="NavigationRequested"/> 事件向路由器暴露导航请求，
    /// 关闭按钮与底部"返回沉浸模式"按钮均触发 <see cref="InkPageDomIds.CombatHud"/>。
    /// 当前实现全部使用 mock 数据；后续接入任务系统时，通过刷新方法替换内容即可。
    /// </para>
    /// </summary>
    public class QuestLogPage : ContainerControl, IInkPage
    {
        // ===================================================================
        // 布局常量
        // =======================================================================

        /// <summary>屏幕边距（像素）</summary>
        private const float ScreenEdge = 16f;

        /// <summary>顶部标题栏高度（像素）</summary>
        private const float HeaderHeight = 60f;

        /// <summary>底部导航栏高度（像素）</summary>
        private const float BottomNavHeight = 40f;

        /// <summary>区域间距（像素）</summary>
        private const float RegionGap = 12f;

        /// <summary>左侧分类 Tab 面板宽度（像素）</summary>
        private const float LeftTabWidth = 130f;

        /// <summary>中间任务列表面板宽度（像素）</summary>
        private const float MiddleListWidth = 380f;

        /// <summary>分类 Tab 按钮高度（像素）</summary>
        private const float TabBtnHeight = 40f;

        /// <summary>分类 Tab 按钮间距（像素）</summary>
        private const float TabBtnGap = 4f;

        /// <summary>任务条目高度（像素）</summary>
        private const float QuestItemHeight = 72f;

        /// <summary>任务条目间距（像素）</summary>
        private const float QuestItemGap = 6f;

        /// <summary>底部导航按钮宽度（像素）</summary>
        private const float NavBtnWidth = 160f;

        /// <summary>详情面板操作按钮宽度（像素）</summary>
        private const float ActionBtnWidth = 88f;

        /// <summary>详情面板操作按钮间距（像素）</summary>
        private const float ActionBtnGap = 8f;

        // ===================================================================
        // mock 数据
        // =======================================================================

        /// <summary>6 个任务分类名（与 Tab 一一对应）</summary>
        private static readonly string[] CategoryNames =
        {
            "主线", "支线", "日常", "师门", "门派", "奇遇"
        };

        /// <summary>任务列表 mock 数据：名称/等级/状态/进度</summary>
        private static readonly (string name, int level, string status, float progress)[] MockQuests =
        {
            ("初入江湖", 1,  "进行中", 0.40f),
            ("拜师学艺", 5,  "未开始", 0.00f),
            ("江湖初探", 8,  "进行中", 0.66f),
            ("山贼为患", 10, "进行中", 0.20f),
            ("名剑出世", 15, "已完成", 1.00f),
        };

        /// <summary>任务目标 mock 数据：描述/是否完成/进度</summary>
        private static readonly (string text, bool done, float progress)[] MockObjectives =
        {
            ("前往开封城",       true,  1.00f),
            ("与王铁匠对话",     false, 0.50f),
            ("击败山贼 0/5",     false, 0.00f),
            ("取得精铁护腕",     false, 0.00f),
            ("回报王铁匠",       false, 0.00f),
        };

        // ===================================================================
        // 子控件引用 — 顶部
        // =======================================================================

        /// <summary>顶部标题栏面板</summary>
        private InkPanel _headerPanel;

        /// <summary>页面标题"江湖任务志"</summary>
        private Label _titleLabel;

        /// <summary>玩家信息标签（姓名/等级/门派）</summary>
        private Label _playerInfoLabel;

        /// <summary>"关闭"按钮（返回 HUD）</summary>
        private InkButton _closeButton;

        // ===================================================================
        // 子控件引用 — 左侧分类 Tab
        // =======================================================================

        /// <summary>左侧分类 Tab 面板</summary>
        private InkPanel _tabPanel;

        /// <summary>6 个分类 Tab 按钮</summary>
        private InkButton[] _tabButtons;

        /// <summary>当前激活的分类索引</summary>
        private int _activeTabIndex = 0;

        // ===================================================================
        // 子控件引用 — 中间任务列表
        // =======================================================================

        /// <summary>中间任务列表面板</summary>
        private InkPanel _questListPanel;

        /// <summary>列表区标题"任务列表"</summary>
        private Label _listTitleLabel;

        /// <summary>5 条任务条目容器</summary>
        private InkListItem[] _questItems;

        /// <summary>每条任务名称标签</summary>
        private Label[] _questNameLabels;

        /// <summary>每条任务等级标签</summary>
        private Label[] _questLevelLabels;

        /// <summary>每条任务状态标签</summary>
        private Label[] _questStatusLabels;

        /// <summary>每条任务进度条</summary>
        private InkBar[] _questProgressBars;

        /// <summary>当前选中的任务索引</summary>
        private int _activeQuestIndex = 0;

        // ===================================================================
        // 子控件引用 — 右侧任务详情
        // =======================================================================

        /// <summary>右侧任务详情面板</summary>
        private InkPanel _detailPanel;

        /// <summary>详情标题（如"初入江湖"）</summary>
        private Label _detailTitleLabel;

        /// <summary>品质徽章（如"普通"）</summary>
        private InkTag _qualityTag;

        /// <summary>类型徽章（如"主线"）</summary>
        private InkTag _typeTag;

        /// <summary>Meta 标签：地点</summary>
        private Label _metaLocationLabel;

        /// <summary>Meta 标签：NPC</summary>
        private Label _metaNpcLabel;

        /// <summary>Meta 标签：时限</summary>
        private Label _metaTimeLabel;

        /// <summary>任务描述卷轴面板（纸色背景）</summary>
        private InkPaperPanel _descScrollPanel;

        /// <summary>任务描述正文</summary>
        private Label _descLabel;

        /// <summary>"任务目标"区标题</summary>
        private Label _objectiveTitleLabel;

        /// <summary>5 个目标条目标签（含完成状态符号）</summary>
        private Label[] _objectiveLabels;

        /// <summary>"任务奖励"区标题</summary>
        private Label _rewardTitleLabel;

        /// <summary>经验奖励标签</summary>
        private Label _expRewardLabel;

        /// <summary>银两奖励标签</summary>
        private Label _silverRewardLabel;

        /// <summary>道具奖励标签</summary>
        private Label _itemRewardLabel;

        /// <summary>"追踪任务"按钮</summary>
        private InkButton _trackButton;

        /// <summary>"放弃任务"按钮</summary>
        private InkButton _abandonButton;

        /// <summary>"传送至任务地点"按钮</summary>
        private InkButton _teleportButton;

        // ===================================================================
        // 子控件引用 — 底部
        // =======================================================================

        /// <summary>底部导航栏面板</summary>
        private InkPanel _bottomNavPanel;

        /// <summary>"返回沉浸模式"按钮</summary>
        private InkButton _returnHudButton;

        // ===================================================================
        // 公共 API
        // =======================================================================

        /// <summary>
        /// 导航请求事件。由关闭按钮与底部"返回沉浸模式"按钮触发，
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
        public QuestLogPage()
        {
            try
            {
                AnchorPreset = AnchorPresets.StretchAll;
                Offsets = Margin.Zero;
                BackgroundColor = Color.Transparent;
                ClipChildren = false;
                AutoFocus = false;

                BuildHeader();
                BuildLeftTabs();
                BuildQuestList();
                BuildDetailPanel();
                BuildBottomNav();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[QuestLogPage] 初始化失败: {ex.Message}");
            }
        }

        // ===================================================================
        // Build 方法
        // =======================================================================

        /// <summary>
        /// 构建顶部标题栏：标题"江湖任务志" + 玩家信息 + 关闭按钮。
        /// </summary>
        private void BuildHeader()
        {
            _headerPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
            };

            // 页面标题"江湖任务志"
            _titleLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(20f, 0f),
                Size = new Float2(220f, HeaderHeight),
                Text = "江湖任务志",
                TextColor = InkWashTheme.TextGold,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 22f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _headerPanel.AddChild(_titleLabel);

            // 玩家信息：姓名 · 等级 · 门派
            _playerInfoLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(260f, 0f),
                Size = new Float2(360f, HeaderHeight),
                Text = "慕容凌霄 · Lv.50 · 华山派",
                TextColor = InkWashTheme.TextSecondary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _headerPanel.AddChild(_playerInfoLabel);

            // 关闭按钮（靠右定位，RefreshLayout 中设置）
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
        /// 构建左侧分类 Tab 面板：6 个垂直分类按钮（主线/支线/日常/师门/门派/奇遇）。
        /// </summary>
        private void BuildLeftTabs()
        {
            _tabPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
            };

            // 区标题
            var sectionTitle = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 8f),
                Size = new Float2(LeftTabWidth - 24f, 20f),
                Text = "◆ 任务分类",
                TextColor = InkWashTheme.TextGold,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _tabPanel.AddChild(sectionTitle);

            // 6 个分类 Tab 按钮（垂直堆叠）
            _tabButtons = new InkButton[CategoryNames.Length];
            float tabY = 36f;
            for (int i = 0; i < _tabButtons.Length; i++)
            {
                int capturedIndex = i;
                var btn = new InkButton
                {
                    Variant = InkButtonVariant.Ghost,
                    ButtonSize = InkButtonSize.Md,
                    Text = CategoryNames[i],
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(8f, tabY),
                    Size = new Float2(LeftTabWidth - 16f, TabBtnHeight),
                };
                btn.ButtonClicked += (b) => OnTabButtonClicked(capturedIndex, b);
                _tabPanel.AddChild(btn);
                _tabButtons[i] = btn;
                tabY += TabBtnHeight + TabBtnGap;
            }

            // 高亮初始 Tab 0
            ApplyTabHighlight();

            AddChild(_tabPanel);
        }

        /// <summary>
        /// 构建中间任务列表面板：5 条任务条目，每条显示名称/等级/状态/进度条。
        /// </summary>
        private void BuildQuestList()
        {
            _questListPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
            };

            // 列表区标题
            _listTitleLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, 8f),
                Size = new Float2(MiddleListWidth - 32f, 22f),
                Text = "◆ 任务列表",
                TextColor = InkWashTheme.TextGold,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 14f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _questListPanel.AddChild(_listTitleLabel);

            // 5 条任务条目
            int questCount = MockQuests.Length;
            _questItems = new InkListItem[questCount];
            _questNameLabels = new Label[questCount];
            _questLevelLabels = new Label[questCount];
            _questStatusLabels = new Label[questCount];
            _questProgressBars = new InkBar[questCount];

            float itemY = 38f;
            float itemWidth = MiddleListWidth - 24f;
            for (int i = 0; i < questCount; i++)
            {
                var item = new InkListItem
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(12f, itemY),
                    Size = new Float2(itemWidth, QuestItemHeight),
                    Active = (i == _activeQuestIndex),
                };

                // 任务名（左上）
                var nameLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(12f, 6f),
                    Size = new Float2(200f, 20f),
                    Text = MockQuests[i].name,
                    TextColor = InkWashTheme.TextDefault,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 14f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                item.AddChild(nameLabel);
                _questNameLabels[i] = nameLabel;

                // 等级标签（右上）
                var levelLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopRight,
                    Location = new Float2(itemWidth - 72f, 6f),
                    Size = new Float2(60f, 20f),
                    Text = "Lv." + MockQuests[i].level,
                    TextColor = InkWashTheme.TextGold,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 12f),
                    HorizontalAlignment = TextAlignment.Far,
                    VerticalAlignment = TextAlignment.Center,
                };
                item.AddChild(levelLabel);
                _questLevelLabels[i] = levelLabel;

                // 状态标签（左下）
                var statusLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(12f, 28f),
                    Size = new Float2(120f, 16f),
                    Text = MockQuests[i].status,
                    TextColor = MockQuests[i].status == "已完成"
                        ? InkWashTheme.TextJade
                        : (MockQuests[i].status == "进行中"
                            ? InkWashTheme.TextBrand
                            : InkWashTheme.TextTertiary),
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 11f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                item.AddChild(statusLabel);
                _questStatusLabels[i] = statusLabel;

                // 进度条（底部）
                var progressBar = new InkBar
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(12f, 48f),
                    Size = new Float2(itemWidth - 24f, 10f),
                    Value = MockQuests[i].progress,
                    FillVariant = MockQuests[i].status == "已完成"
                        ? InkBarFillVariant.Jade
                        : InkBarFillVariant.Gold,
                };
                item.AddChild(progressBar);
                _questProgressBars[i] = progressBar;

                _questListPanel.AddChild(item);
                _questItems[i] = item;
                itemY += QuestItemHeight + QuestItemGap;
            }

            AddChild(_questListPanel);
        }

        /// <summary>
        /// 构建右侧任务详情面板：标题 + 品质/类型徽章 + meta + 描述卷轴 + 目标 + 奖励 + 操作按钮。
        /// </summary>
        private void BuildDetailPanel()
        {
            _detailPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
            };

            // 详情标题（如"初入江湖"）
            _detailTitleLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(20f, 12f),
                Size = new Float2(300f, 36f),
                Text = "初入江湖",
                TextColor = InkWashTheme.TextGold,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 24f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _detailPanel.AddChild(_detailTitleLabel);

            // 品质徽章（右上）
            _qualityTag = new InkTag
            {
                AnchorPreset = AnchorPresets.TopRight,
                Text = "普通",
                TagVariant = InkTagVariant.Default,
            };
            _detailPanel.AddChild(_qualityTag);

            // 类型徽章（品质徽章左侧，RefreshLayout 中定位）
            _typeTag = new InkTag
            {
                AnchorPreset = AnchorPresets.TopRight,
                Text = "主线",
                TagVariant = InkTagVariant.Brand,
            };
            _detailPanel.AddChild(_typeTag);

            // Meta 标签：地点/NPC/时限（标题下方一行）
            _metaLocationLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(20f, 50f),
                Size = new Float2(120f, 16f),
                Text = "◎ 开封城",
                TextColor = InkWashTheme.TextSecondary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 11f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _detailPanel.AddChild(_metaLocationLabel);

            _metaNpcLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(140f, 50f),
                Size = new Float2(120f, 16f),
                Text = "◆ 王铁匠",
                TextColor = InkWashTheme.TextSecondary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 11f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _detailPanel.AddChild(_metaNpcLabel);

            _metaTimeLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(260f, 50f),
                Size = new Float2(120f, 16f),
                Text = "◌ 无时限",
                TextColor = InkWashTheme.TextSecondary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 11f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _detailPanel.AddChild(_metaTimeLabel);

            // 任务描述卷轴面板（纸色背景）
            _descScrollPanel = new InkPaperPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(20f, 74f),
                Size = new Float2(400f, 100f),
            };
            _detailPanel.AddChild(_descScrollPanel);

            _descLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 10f),
                Size = new Float2(376f, 80f),
                Text = "你初到开封城，听闻城中有位隐世高人，身怀绝世武学。前往城中各处探访，拜访各派长老，了解武林格局，或可寻得机缘。城郊山贼为患，亦可借此磨砺武艺。",
                TextColor = InkWashTheme.TextOnPaper,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 12f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Near,
            };
            _descScrollPanel.AddChild(_descLabel);

            // "任务目标"区标题
            _objectiveTitleLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(20f, 184f),
                Size = new Float2(200f, 20f),
                Text = "◆ 任务目标",
                TextColor = InkWashTheme.TextGold,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _detailPanel.AddChild(_objectiveTitleLabel);

            // 5 个目标条目标签
            _objectiveLabels = new Label[MockObjectives.Length];
            float objY = 208f;
            for (int i = 0; i < MockObjectives.Length; i++)
            {
                string symbol = MockObjectives[i].done ? "✓" : "○";
                Color symColor = MockObjectives[i].done
                    ? InkWashTheme.TextJade
                    : InkWashTheme.TextSecondary;
                var objLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(28f, objY),
                    Size = new Float2(380f, 18f),
                    Text = $"{symbol}  {MockObjectives[i].text}",
                    TextColor = symColor,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 12f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                _detailPanel.AddChild(objLabel);
                _objectiveLabels[i] = objLabel;
                objY += 20f;
            }

            // "任务奖励"区标题
            _rewardTitleLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(20f, objY + 8f),
                Size = new Float2(200f, 20f),
                Text = "◆ 任务奖励",
                TextColor = InkWashTheme.TextGold,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _detailPanel.AddChild(_rewardTitleLabel);

            // 3 个奖励标签（经验/银两/道具）
            float rewardY = objY + 32f;
            _expRewardLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(28f, rewardY),
                Size = new Float2(120f, 20f),
                Text = "✦ 经验 +5000",
                TextColor = InkWashTheme.TextJade,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 12f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _detailPanel.AddChild(_expRewardLabel);

            _silverRewardLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(160f, rewardY),
                Size = new Float2(120f, 20f),
                Text = "✦ 银两 +200",
                TextColor = InkWashTheme.TextGold,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 12f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _detailPanel.AddChild(_silverRewardLabel);

            _itemRewardLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(292f, rewardY),
                Size = new Float2(140f, 20f),
                Text = "✦ 精铁护腕",
                TextColor = InkWashTheme.QualityTextColor(InkWashTheme.InkQuality.Uncommon),
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 12f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _detailPanel.AddChild(_itemRewardLabel);

            // 操作按钮：追踪 / 放弃 / 传送（RefreshLayout 中定位底部）
            _trackButton = new InkButton
            {
                Variant = InkButtonVariant.Primary,
                ButtonSize = InkButtonSize.Md,
                Text = "追踪任务",
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(ActionBtnWidth, 32f),
            };
            _trackButton.ButtonClicked += (b) => EmitGoldAtButton(b);
            _detailPanel.AddChild(_trackButton);

            _abandonButton = new InkButton
            {
                Variant = InkButtonVariant.Vermilion,
                ButtonSize = InkButtonSize.Md,
                Text = "放弃任务",
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(ActionBtnWidth, 32f),
            };
            _abandonButton.ButtonClicked += (b) => EmitGoldAtButton(b);
            _detailPanel.AddChild(_abandonButton);

            _teleportButton = new InkButton
            {
                Variant = InkButtonVariant.Default,
                ButtonSize = InkButtonSize.Md,
                Text = "传送至地点",
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(ActionBtnWidth, 32f),
            };
            _teleportButton.ButtonClicked += (b) => EmitGoldAtButton(b);
            _detailPanel.AddChild(_teleportButton);

            AddChild(_detailPanel);
        }

        /// <summary>
        /// 构建底部导航栏：返回沉浸模式按钮。
        /// </summary>
        private void BuildBottomNav()
        {
            _bottomNavPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
            };

            _returnHudButton = new InkButton
            {
                Variant = InkButtonVariant.Default,
                ButtonSize = InkButtonSize.Md,
                Text = "返回沉浸模式",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 4f),
                Size = new Float2(NavBtnWidth, 32f),
            };
            _returnHudButton.ButtonClicked += (b) => OnSystemNavButtonClicked(InkPageDomIds.CombatHud, b);
            _bottomNavPanel.AddChild(_returnHudButton);

            AddChild(_bottomNavPanel);
        }

        // ===================================================================
        // 事件处理
        // =======================================================================

        /// <summary>
        /// 分类 Tab 按钮点击处理：切换激活态并发射金粉粒子。
        /// </summary>
        private void OnTabButtonClicked(int tabIndex, Button sourceButton)
        {
            try
            {
                EmitGoldAtButton(sourceButton);
                _activeTabIndex = tabIndex;
                ApplyTabHighlight();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[QuestLogPage] Tab 切换失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 根据当前激活的 Tab 索引更新所有 Tab 按钮的视觉状态。
        /// 激活的 Tab 使用品牌色文字，其余使用次级文字色。
        /// </summary>
        private void ApplyTabHighlight()
        {
            if (_tabButtons == null)
                return;
            for (int i = 0; i < _tabButtons.Length; i++)
            {
                if (_tabButtons[i] == null)
                    continue;
                _tabButtons[i].TextColor = (i == _activeTabIndex)
                    ? InkWashTheme.TextGold
                    : InkWashTheme.TextSecondary;
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
                    $"[QuestLogPage] NavigationRequested({domId}) 触发失败: {ex.Message}");
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
                FlaxEngine.Debug.LogWarning($"[QuestLogPage] EmitGoldAtButton 失败: {ex.Message}");
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

                    // 返回按钮居中
                    if (_returnHudButton != null)
                    {
                        _returnHudButton.Location = new Float2(
                            (panelW - NavBtnWidth) * 0.5f, 4f);
                    }
                }

                // 3. 内容区：顶部标题栏下方 → 底部导航栏上方
                float contentTop = ScreenEdge + HeaderHeight + RegionGap;
                float contentBottom = bottomNavY - RegionGap;
                float contentH = contentBottom - contentTop;
                if (contentH < 100f)
                    contentH = 100f;

                // 三栏水平布局：左 Tab / 中列表 / 右详情
                float leftX = panelX;
                float middleX = leftX + LeftTabWidth + RegionGap;
                float rightX = middleX + MiddleListWidth + RegionGap;
                float rightW = panelW - (rightX - panelX);
                if (rightW < 200f)
                    rightW = 200f;

                // 4. 左侧分类 Tab 面板
                if (_tabPanel != null)
                {
                    _tabPanel.Location = new Float2(leftX, contentTop);
                    _tabPanel.Size = new Float2(LeftTabWidth, contentH);
                }

                // 5. 中间任务列表面板
                if (_questListPanel != null)
                {
                    _questListPanel.Location = new Float2(middleX, contentTop);
                    _questListPanel.Size = new Float2(MiddleListWidth, contentH);
                }

                // 6. 右侧任务详情面板
                if (_detailPanel != null)
                {
                    _detailPanel.Location = new Float2(rightX, contentTop);
                    _detailPanel.Size = new Float2(rightW, contentH);

                    // 描述卷轴面板宽度自适应
                    if (_descScrollPanel != null)
                    {
                        _descScrollPanel.Size = new Float2(rightW - 40f, 100f);
                        if (_descLabel != null)
                        {
                            _descLabel.Size = new Float2(rightW - 64f, 80f);
                        }
                    }

                    // 品质/类型徽章靠右上角排列
                    float tagY = 18f;
                    if (_qualityTag != null)
                    {
                        _qualityTag.Location = new Float2(rightW - 80f, tagY);
                        _qualityTag.Size = new Float2(60f, 22f);
                    }
                    if (_typeTag != null)
                    {
                        _typeTag.Location = new Float2(rightW - 148f, tagY);
                        _typeTag.Size = new Float2(60f, 22f);
                    }

                    // 操作按钮：底部水平排列
                    float actionY = contentH - 44f;
                    float actionStartX = 20f;
                    if (_trackButton != null)
                    {
                        _trackButton.Location = new Float2(actionStartX, actionY);
                    }
                    if (_abandonButton != null)
                    {
                        _abandonButton.Location = new Float2(
                            actionStartX + ActionBtnWidth + ActionBtnGap, actionY);
                    }
                    if (_teleportButton != null)
                    {
                        _teleportButton.Location = new Float2(
                            actionStartX + (ActionBtnWidth + ActionBtnGap) * 2f, actionY);
                    }
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[QuestLogPage] RefreshLayout 失败: {ex.Message}");
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

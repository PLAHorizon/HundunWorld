using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Ink.Components;
using HundunWorld.Game.UI.StyleSystem;
using System;

namespace HundunWorld.Game.UI.Ink.Pages.Character
{
    /// <summary>
    /// 武学技能面板页面 — 对应 skill-panel.html 设计原型（dom-id: nav-skill-panel）。
    /// <para>
    /// 采用"顶部标签栏 + 左侧武学分类列表 + 右侧武学详情/经脉图 + 底部导航"四区结构，
    /// 居中显示 1200x720 主面板。通过 <see cref="NavigationRequested"/> 事件向路由器
    /// 暴露导航跳转：返回沉浸模式（combat-hud）、跳转角色面板（nav-character-panel）。
    /// </para>
    /// <list type="bullet">
    ///   <item>顶部：返回按钮 + 武学/心法/奇术 Tabs + "武学心法"标题</item>
    ///   <item>左侧：按内功/外功/轻功/暗器/奇术分组的武学列表 + 熟练度进度条</item>
    ///   <item>右侧：武学详情（名称/品级/类型/描述/招式列表）+ 经脉穴位图</item>
    ///   <item>底部：返回沉浸模式 / 跳转角色面板</item>
    /// </list>
    /// </summary>
    public class SkillPanelPage : ContainerControl, IInkPage
    {
        // ===================================================================
        // 布局常量
        // =======================================================================

        /// <summary>主面板尺寸（居中显示）</summary>
        private static readonly Float2 MainPanelSize = new Float2(1200f, 720f);

        /// <summary>顶部标签栏高度</summary>
        private const float TopBarHeight = 56f;

        /// <summary>底部导航栏高度</summary>
        private const float BottomBarHeight = 56f;

        /// <summary>面板内边距</summary>
        private const float Padding = 12f;

        /// <summary>子面板间距</summary>
        private const float PanelGap = 10f;

        /// <summary>分区标题装饰竖线宽度</summary>
        private const float TitleBarWidth = 3f;

        /// <summary>分区标题装饰竖线高度</summary>
        private const float TitleBarHeight = 16f;

        /// <summary>分区标题字号</summary>
        private const float TitleFontSize = 14f;

        /// <summary>分区标题与内容间距</summary>
        private const float TitleToContentGap = 8f;

        /// <summary>左侧武学列表面板宽度</summary>
        private const float LeftPanelWidth = 380f;

        /// <summary>Tab 按钮宽度</summary>
        private const float TabButtonWidth = 72f;

        /// <summary>Tab 按钮高度</summary>
        private const float TabButtonHeight = 32f;

        /// <summary>Tab 按钮间距</summary>
        private const float TabButtonGap = 6f;

        /// <summary>武学列表项高度</summary>
        private const float SkillItemHeight = 36f;

        /// <summary>武学列表项间距</summary>
        private const float SkillItemGap = 4f;

        /// <summary>招式条目高度</summary>
        private const float MoveItemHeight = 28f;

        /// <summary>招式条目间距</summary>
        private const float MoveItemGap = 4f;

        /// <summary>导航按钮宽度</summary>
        private const float NavButtonWidth = 140f;

        /// <summary>导航按钮高度</summary>
        private const float NavButtonHeight = 36f;

        /// <summary>导航按钮间距</summary>
        private const float NavButtonGap = 12f;

        /// <summary>熟练度进度条高度</summary>
        private const float ProficiencyBarHeight = 8f;

        // ===================================================================
        // 子控件引用 — 主面板与顶部栏
        // =======================================================================

        /// <summary>主面板容器（居中 1200x720）</summary>
        private InkPanelElevated _mainPanel;

        /// <summary>顶部标签栏</summary>
        private InkPanel _topBar;

        /// <summary>左上角返回按钮</summary>
        private InkBackButton _backButton;

        /// <summary>顶部"武学心法"标题</summary>
        private Label _topTitleLabel;

        /// <summary>顶部"SKILL"副标题</summary>
        private Label _topSubtitleLabel;

        /// <summary>武学 Tab 按钮</summary>
        private InkButton _tabMartial;

        /// <summary>心法 Tab 按钮</summary>
        private InkButton _tabHeart;

        /// <summary>奇术 Tab 按钮</summary>
        private InkButton _tabSpecial;

        // ===================================================================
        // 子控件引用 — 左侧武学列表
        // =======================================================================

        /// <summary>左侧武学列表面板</summary>
        private InkPanel _leftPanel;

        /// <summary>5 个分类分组容器（内功/外功/轻功/暗器/奇术）</summary>
        private ContainerControl[] _categoryGroups;

        /// <summary>当前选中武学索引（-1 表示无选中）</summary>
        private int _selectedSkillIndex = -1;

        // ===================================================================
        // 子控件引用 — 右侧详情与经脉图
        // =======================================================================

        /// <summary>右侧详情面板</summary>
        private InkPanel _rightPanel;

        /// <summary>武学名称大字标签</summary>
        private Label _skillNameLabel;

        /// <summary>武学品级标签</summary>
        private Label _skillGradeLabel;

        /// <summary>武学类型标签</summary>
        private Label _skillTypeLabel;

        /// <summary>武学门派标签</summary>
        private Label _skillSectLabel;

        /// <summary>武学描述标签</summary>
        private Label _skillDescLabel;

        /// <summary>熟练度进度条</summary>
        private InkBar _proficiencyBar;

        /// <summary>熟练度数值标签</summary>
        private Label _proficiencyValueLabel;

        /// <summary>招式列表容器</summary>
        private ContainerControl _moveListContainer;

        /// <summary>经脉穴位图</summary>
        private InkMeridianDiagram _meridianDiagram;

        /// <summary>经脉图标题</summary>
        private Label _meridianTitleLabel;

        // ===================================================================
        // 子控件引用 — 底部导航栏
        // =======================================================================

        /// <summary>底部导航栏</summary>
        private InkPanel _bottomBar;

        /// <summary>返回沉浸模式按钮</summary>
        private InkButton _btnReturnHud;

        /// <summary>跳转角色面板按钮</summary>
        private InkButton _btnGotoCharacter;

        // ===================================================================
        // 公共 API
        // =======================================================================

        /// <summary>
        /// 导航请求事件。触发后由 MainUIManager 订阅并调用 InkPageRouter.NavigateTo。
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
        /// 构造函数：初始化主面板与所有子控件。
        /// </summary>
        public SkillPanelPage()
        {
            try
            {
                AnchorPreset = AnchorPresets.StretchAll;
                Offsets = Margin.Zero;
                BackgroundColor = InkWashTheme.Scrim;
                ClipChildren = false;
                AutoFocus = false;

                BuildMainPanel();
                BuildTopBar();
                BuildLeftColumn();
                BuildRightColumn();
                BuildBottomBar();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[SkillPanelPage] 初始化失败: {ex.Message}");
            }
        }

        // ===================================================================
        // Build 方法
        // =======================================================================

        /// <summary>
        /// 构建居中主面板容器（1200x720，带抬升阴影）。
        /// </summary>
        private void BuildMainPanel()
        {
            _mainPanel = new InkPanelElevated
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = MainPanelSize,
            };
            AddChild(_mainPanel);
        }

        /// <summary>
        /// 构建顶部标签栏：返回按钮 + Tabs + "武学心法"标题 + SKILL 副标题。
        /// </summary>
        private void BuildTopBar()
        {
            _topBar = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Float2(MainPanelSize.X, TopBarHeight),
            };
            _mainPanel.AddChild(_topBar);

            // 返回按钮（左上角）
            _backButton = new InkBackButton
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(Padding, (TopBarHeight - 40f) * 0.5f),
            };
            _backButton.Clicked += OnBackButtonClicked;
            _topBar.AddChild(_backButton);

            // Tabs（返回按钮右侧）
            float tabX = Padding + 40f + 16f;
            float tabY = (TopBarHeight - TabButtonHeight) * 0.5f;

            _tabMartial = new InkButton
            {
                Variant = InkButtonVariant.Primary,
                ButtonSize = InkButtonSize.Sm,
                Text = "武学",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(tabX, tabY),
                Size = new Float2(TabButtonWidth, TabButtonHeight),
            };
            _topBar.AddChild(_tabMartial);

            _tabHeart = new InkButton
            {
                Variant = InkButtonVariant.Default,
                ButtonSize = InkButtonSize.Sm,
                Text = "心法",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(tabX + TabButtonWidth + TabButtonGap, tabY),
                Size = new Float2(TabButtonWidth, TabButtonHeight),
            };
            _topBar.AddChild(_tabHeart);

            _tabSpecial = new InkButton
            {
                Variant = InkButtonVariant.Default,
                ButtonSize = InkButtonSize.Sm,
                Text = "奇术",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(tabX + (TabButtonWidth + TabButtonGap) * 2, tabY),
                Size = new Float2(TabButtonWidth, TabButtonHeight),
            };
            _topBar.AddChild(_tabSpecial);

            // "武学心法"标题（右侧）
            _topTitleLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(140f, TopBarHeight),
                Text = "武学心法",
                TextColor = InkWashTheme.TextGold,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 16f),
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
            };
            _topBar.AddChild(_topTitleLabel);

            // "SKILL"副标题（最右侧）
            _topSubtitleLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(70f, TopBarHeight),
                Text = "SKILL",
                TextColor = InkWashTheme.TextTertiary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 10f),
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
            };
            _topBar.AddChild(_topSubtitleLabel);
        }

        /// <summary>
        /// 构建左侧武学分类列表：按内功/外功/轻功/暗器/奇术 5 类分组。
        /// </summary>
        private void BuildLeftColumn()
        {
            float contentTop = TopBarHeight + PanelGap;
            float contentBottom = MainPanelSize.Y - BottomBarHeight - PanelGap;
            float contentH = contentBottom - contentTop;

            _leftPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(Padding, contentTop),
                Size = new Float2(LeftPanelWidth, contentH),
            };
            _mainPanel.AddChild(_leftPanel);

            // 分类标题与列表数据
            string[] categoryNames = { "内功", "外功", "轻功", "暗器", "奇术" };
            string[][] categorySkills =
            {
                new[] { "纯阳内功", "九阳神功", "太玄经" },
                new[] { "太极剑法", "降龙十八掌", "独孤九剑" },
                new[] { "凌波微步", "梯云纵" },
                new[] { "漫天花雨", "暴雨梨花" },
                new[] { "乾坤大挪移", "北冥神功" }
            };
            var categoryQualities = new InkWashTheme.InkQuality[][]
            {
                new[] { InkWashTheme.InkQuality.Epic, InkWashTheme.InkQuality.Legendary, InkWashTheme.InkQuality.Rare },
                new[] { InkWashTheme.InkQuality.Legendary, InkWashTheme.InkQuality.Legendary, InkWashTheme.InkQuality.Epic },
                new[] { InkWashTheme.InkQuality.Epic, InkWashTheme.InkQuality.Rare },
                new[] { InkWashTheme.InkQuality.Rare, InkWashTheme.InkQuality.Uncommon },
                new[] { InkWashTheme.InkQuality.Legendary, InkWashTheme.InkQuality.Legendary }
            };

            _categoryGroups = new ContainerControl[categoryNames.Length];
            float cursorY = Padding;
            float innerW = LeftPanelWidth - Padding * 2;
            int globalIndex = 0;

            for (int i = 0; i < categoryNames.Length; i++)
            {
                // 分类标题（装饰竖线 + 中文标题）
                var titleBar = new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(Padding, cursorY),
                    Size = new Float2(TitleBarWidth, TitleBarHeight),
                    BackgroundColor = InkWashTheme.GoldPrimary,
                };
                _leftPanel.AddChild(titleBar);

                var catTitleLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(Padding + TitleBarWidth + 6f, cursorY - 2f),
                    Size = new Float2(120f, TitleBarHeight + 4f),
                    Text = categoryNames[i],
                    TextColor = InkWashTheme.TextGold,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), TitleFontSize),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                _leftPanel.AddChild(catTitleLabel);

                // 分类下武学数量小标签
                var countLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(Padding + innerW - 40f, cursorY - 2f),
                    Size = new Float2(40f, TitleBarHeight + 4f),
                    Text = categorySkills[i].Length.ToString(),
                    TextColor = InkWashTheme.TextTertiary,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 11f),
                    HorizontalAlignment = TextAlignment.Far,
                    VerticalAlignment = TextAlignment.Center,
                };
                _leftPanel.AddChild(countLabel);

                cursorY += TitleBarHeight + TitleToContentGap;

                // 武学列表项
                var groupContainer = new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(Padding, cursorY),
                    Size = new Float2(innerW, categorySkills[i].Length * (SkillItemHeight + SkillItemGap)),
                };
                _leftPanel.AddChild(groupContainer);
                _categoryGroups[i] = groupContainer;

                for (int j = 0; j < categorySkills[i].Length; j++)
                {
                    int idx = globalIndex;
                    float itemY = j * (SkillItemHeight + SkillItemGap);

                    // 列表项背景容器
                    var itemPanel = new InkPanel
                    {
                        AnchorPreset = AnchorPresets.TopLeft,
                        Location = new Float2(0f, itemY),
                        Size = new Float2(innerW, SkillItemHeight),
                    };
                    groupContainer.AddChild(itemPanel);

                    // 品质色边框格子（左侧）
                    var qualityCell = new InkCell
                    {
                        AnchorPreset = AnchorPresets.TopLeft,
                        Location = new Float2(4f, (SkillItemHeight - 28f) * 0.5f),
                        Size = new Float2(28f, 28f),
                        Quality = categoryQualities[i][j],
                    };
                    itemPanel.AddChild(qualityCell);

                    // 武学名称
                    var nameLabel = new Label
                    {
                        AnchorPreset = AnchorPresets.TopLeft,
                        Location = new Float2(40f, 0f),
                        Size = new Float2(innerW - 80f, SkillItemHeight),
                        Text = categorySkills[i][j],
                        TextColor = idx == 0 ? InkWashTheme.TextGold : InkWashTheme.TextDefault,
                        Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 13f),
                        HorizontalAlignment = TextAlignment.Near,
                        VerticalAlignment = TextAlignment.Center,
                    };
                    itemPanel.AddChild(nameLabel);

                    // 已学习标记
                    if (idx == 0)
                    {
                        var learnedTag = new Label
                        {
                            AnchorPreset = AnchorPresets.TopLeft,
                            Location = new Float2(innerW - 40f, 0f),
                            Size = new Float2(36f, SkillItemHeight),
                            Text = "已学",
                            TextColor = InkWashTheme.TextBrand,
                            Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 10f),
                            HorizontalAlignment = TextAlignment.Far,
                            VerticalAlignment = TextAlignment.Center,
                        };
                        itemPanel.AddChild(learnedTag);
                    }

                    globalIndex++;
                }

                cursorY += categorySkills[i].Length * (SkillItemHeight + SkillItemGap) + PanelGap;
            }
        }

        /// <summary>
        /// 构建右侧武学详情面板：名称 + 品级/类型/门派 + 描述 + 熟练度 + 招式列表 + 经脉图。
        /// </summary>
        private void BuildRightColumn()
        {
            float contentTop = TopBarHeight + PanelGap;
            float contentBottom = MainPanelSize.Y - BottomBarHeight - PanelGap;
            float contentH = contentBottom - contentTop;
            float rightW = MainPanelSize.X - Padding * 2 - PanelGap - LeftPanelWidth;
            float rightX = Padding + LeftPanelWidth + PanelGap;

            _rightPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(rightX, contentTop),
                Size = new Float2(rightW, contentH),
            };
            _mainPanel.AddChild(_rightPanel);

            float innerX = Padding;
            float innerW = rightW - Padding * 2;
            float cursorY = Padding;

            // ===== 武学名称大字 =====
            _skillNameLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(innerX, cursorY),
                Size = new Float2(innerW, 36f),
                Text = "太极剑法",
                TextColor = InkWashTheme.TextGold,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 22f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _rightPanel.AddChild(_skillNameLabel);
            cursorY += 40f;

            // ===== 品级 / 类型 / 门派 标签行 =====
            float tagRowH = 22f;
            var gradeBg = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(innerX, cursorY),
                Size = new Float2(72f, tagRowH),
                BackgroundColor = new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.18f),
            };
            _rightPanel.AddChild(gradeBg);

            _skillGradeLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(innerX, cursorY),
                Size = new Float2(72f, tagRowH),
                Text = "传说",
                TextColor = InkWashTheme.GoldBright,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 12f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            _rightPanel.AddChild(_skillGradeLabel);

            _skillTypeLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(innerX + 80f, cursorY),
                Size = new Float2(80f, tagRowH),
                Text = "外功·剑",
                TextColor = InkWashTheme.TextSecondary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 12f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _rightPanel.AddChild(_skillTypeLabel);

            _skillSectLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(innerX + 160f, cursorY),
                Size = new Float2(120f, tagRowH),
                Text = "武当派",
                TextColor = InkWashTheme.TextSecondary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 12f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _rightPanel.AddChild(_skillSectLabel);
            cursorY += tagRowH + PanelGap;

            // ===== 熟练度进度条 =====
            var profTitleBar = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(innerX, cursorY),
                Size = new Float2(TitleBarWidth, TitleBarHeight),
                BackgroundColor = InkWashTheme.GoldPrimary,
            };
            _rightPanel.AddChild(profTitleBar);

            var profTitleLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(innerX + TitleBarWidth + 6f, cursorY - 2f),
                Size = new Float2(160f, TitleBarHeight + 4f),
                Text = "熟练度",
                TextColor = InkWashTheme.TextGold,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), TitleFontSize),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _rightPanel.AddChild(profTitleLabel);
            cursorY += TitleBarHeight + TitleToContentGap;

            _proficiencyBar = new InkBar
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(innerX, cursorY),
                Size = new Float2(innerW - 80f, ProficiencyBarHeight),
                Value = 0.75f,
                FillVariant = InkBarFillVariant.Gold,
            };
            _rightPanel.AddChild(_proficiencyBar);

            _proficiencyValueLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(innerX + innerW - 76f, cursorY - 6f),
                Size = new Float2(76f, ProficiencyBarHeight + 12f),
                Text = "750 / 1000",
                TextColor = InkWashTheme.TextSecondary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 12f),
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
            };
            _rightPanel.AddChild(_proficiencyValueLabel);
            cursorY += ProficiencyBarHeight + 16f;

            // ===== 武学描述 =====
            var descTitleBar = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(innerX, cursorY),
                Size = new Float2(TitleBarWidth, TitleBarHeight),
                BackgroundColor = InkWashTheme.GoldPrimary,
            };
            _rightPanel.AddChild(descTitleBar);

            var descTitleLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(innerX + TitleBarWidth + 6f, cursorY - 2f),
                Size = new Float2(160f, TitleBarHeight + 4f),
                Text = "武学描述",
                TextColor = InkWashTheme.TextGold,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), TitleFontSize),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _rightPanel.AddChild(descTitleLabel);
            cursorY += TitleBarHeight + TitleToContentGap;

            _skillDescLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(innerX, cursorY),
                Size = new Float2(innerW, 44f),
                Text = "太极生两仪，两仪化四象。以柔克刚，借力打力，剑意圆转不绝。",
                TextColor = InkWashTheme.TextSecondary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 12f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Near,
            };
            _rightPanel.AddChild(_skillDescLabel);
            cursorY += 48f;

            // ===== 招式列表 =====
            var moveTitleBar = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(innerX, cursorY),
                Size = new Float2(TitleBarWidth, TitleBarHeight),
                BackgroundColor = InkWashTheme.GoldPrimary,
            };
            _rightPanel.AddChild(moveTitleBar);

            var moveTitleLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(innerX + TitleBarWidth + 6f, cursorY - 2f),
                Size = new Float2(160f, TitleBarHeight + 4f),
                Text = "招式列表",
                TextColor = InkWashTheme.TextGold,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), TitleFontSize),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _rightPanel.AddChild(moveTitleLabel);
            cursorY += TitleBarHeight + TitleToContentGap;

            _moveListContainer = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(innerX, cursorY),
                Size = new Float2(innerW, 5 * (MoveItemHeight + MoveItemGap)),
            };
            _rightPanel.AddChild(_moveListContainer);

            // 5 招招式
            string[] moveNames = { "起手式·揽雀尾", "第二式·单鞭", "第三式·白鹤亮翅", "第四式·搂膝拗步", "第五式·手挥琵琶" };
            string[] moveCosts = { "内力 80", "内力 120", "内力 150", "内力 180", "内力 220" };
            for (int i = 0; i < 5; i++)
            {
                float itemY = i * (MoveItemHeight + MoveItemGap);

                var movePanel = new InkPanel
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(0f, itemY),
                    Size = new Float2(innerW, MoveItemHeight),
                };
                _moveListContainer.AddChild(movePanel);

                // 序号
                var indexLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(8f, 0f),
                    Size = new Float2(24f, MoveItemHeight),
                    Text = (i + 1).ToString(),
                    TextColor = InkWashTheme.GoldBright,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 13f),
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                };
                movePanel.AddChild(indexLabel);

                // 招式名
                var moveNameLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(36f, 0f),
                    Size = new Float2(innerW - 160f, MoveItemHeight),
                    Text = moveNames[i],
                    TextColor = InkWashTheme.TextDefault,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 12f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                movePanel.AddChild(moveNameLabel);

                // 内力消耗
                var costLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(innerW - 110f, 0f),
                    Size = new Float2(100f, MoveItemHeight),
                    Text = moveCosts[i],
                    TextColor = InkWashTheme.TextTertiary,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 11f),
                    HorizontalAlignment = TextAlignment.Far,
                    VerticalAlignment = TextAlignment.Center,
                };
                movePanel.AddChild(costLabel);
            }
            cursorY += 5 * (MoveItemHeight + MoveItemGap) + PanelGap;

            // ===== 经脉穴位图 =====
            var meridianTitleBar = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(innerX, cursorY),
                Size = new Float2(TitleBarWidth, TitleBarHeight),
                BackgroundColor = InkWashTheme.GoldPrimary,
            };
            _rightPanel.AddChild(meridianTitleBar);

            _meridianTitleLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(innerX + TitleBarWidth + 6f, cursorY - 2f),
                Size = new Float2(160f, TitleBarHeight + 4f),
                Text = "经脉穴位",
                TextColor = InkWashTheme.TextGold,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), TitleFontSize),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _rightPanel.AddChild(_meridianTitleLabel);
            cursorY += TitleBarHeight + TitleToContentGap;

            // 经脉图（右侧居中，200x320 默认尺寸）
            float diagramW = 180f;
            float diagramH = contentTop + contentH - cursorY - Padding;
            if (diagramH < 200f) diagramH = 200f;
            float diagramX = innerX + (innerW - diagramW) * 0.5f;

            _meridianDiagram = new InkMeridianDiagram
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(diagramX, cursorY),
                Size = new Float2(diagramW, diagramH),
            };
            _meridianDiagram.SetActiveAcupoint(3); // 默认高亮"膻中"
            _rightPanel.AddChild(_meridianDiagram);

            // 经脉图右侧穴位说明
            float noteX = diagramX + diagramW + 16f;
            float noteW = innerW - (noteX - innerX);
            if (noteW > 60f)
            {
                var noteLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(noteX, cursorY),
                    Size = new Float2(noteW, 80f),
                    Text = "当前激活：膻中\n属性加成：内力 +120\n招式增幅：剑法伤害 +8%",
                    TextColor = InkWashTheme.TextSecondary,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 11f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Near,
                };
                _rightPanel.AddChild(noteLabel);
            }
        }

        /// <summary>
        /// 构建底部导航栏：返回沉浸模式 + 跳转角色面板。
        /// </summary>
        private void BuildBottomBar()
        {
            _bottomBar = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, MainPanelSize.Y - BottomBarHeight),
                Size = new Float2(MainPanelSize.X, BottomBarHeight),
            };
            _mainPanel.AddChild(_bottomBar);

            // 两个按钮总宽
            float totalBtnW = NavButtonWidth * 2 + NavButtonGap;
            float startX = (MainPanelSize.X - totalBtnW) * 0.5f;
            float btnY = (BottomBarHeight - NavButtonHeight) * 0.5f;

            _btnReturnHud = new InkButton
            {
                Variant = InkButtonVariant.Default,
                ButtonSize = InkButtonSize.Md,
                Text = "返回沉浸模式",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(startX, btnY),
                Size = new Float2(NavButtonWidth, NavButtonHeight),
            };
            _btnReturnHud.ButtonClicked += (b) => OnSystemNavButtonClicked(InkPageDomIds.CombatHud, b);
            _bottomBar.AddChild(_btnReturnHud);

            _btnGotoCharacter = new InkButton
            {
                Variant = InkButtonVariant.Primary,
                ButtonSize = InkButtonSize.Md,
                Text = "角色面板",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(startX + NavButtonWidth + NavButtonGap, btnY),
                Size = new Float2(NavButtonWidth, NavButtonHeight),
            };
            _btnGotoCharacter.ButtonClicked += (b) => OnSystemNavButtonClicked(InkPageDomIds.NavCharacterPanel, b);
            _bottomBar.AddChild(_btnGotoCharacter);
        }

        // ===================================================================
        // 事件处理
        // =======================================================================

        /// <summary>
        /// 顶部返回按钮点击：触发返回沉浸模式导航。
        /// </summary>
        private void OnBackButtonClicked()
        {
            try
            {
                if (_backButton != null)
                {
                    EmitGoldAtButton(_backButton);
                }
                NavigationRequested?.Invoke(InkPageDomIds.CombatHud);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[SkillPanelPage] 返回按钮触发失败: {ex.Message}");
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
                FlaxEngine.Debug.LogError($"[SkillPanelPage] NavigationRequested({domId}) 触发失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 在按钮中心位置触发金粉爆发粒子反馈。
        /// </summary>
        private void EmitGoldAtButton(Control button)
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
                FlaxEngine.Debug.LogWarning($"[SkillPanelPage] EmitGoldAtButton 失败: {ex.Message}");
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

                // 主面板居中
                if (_mainPanel != null)
                {
                    float panelX = (sw - MainPanelSize.X) * 0.5f;
                    float panelY = (sh - MainPanelSize.Y) * 0.5f;
                    _mainPanel.Location = new Float2(
                        panelX > 0f ? panelX : 0f,
                        panelY > 0f ? panelY : 0f);
                }

                // 顶部标题/副标题靠右对齐
                if (_topBar != null && _topTitleLabel != null && _topSubtitleLabel != null)
                {
                    float rightEdge = MainPanelSize.X - Padding;
                    _topSubtitleLabel.Location = new Float2(rightEdge - 70f, 0f);
                    _topTitleLabel.Location = new Float2(rightEdge - 70f - 140f - 8f, 0f);
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[SkillPanelPage] RefreshLayout 失败: {ex.Message}");
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

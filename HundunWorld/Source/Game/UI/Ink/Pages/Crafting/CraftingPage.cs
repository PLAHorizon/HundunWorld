using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.StyleSystem;
using System;

namespace HundunWorld.Game.UI.Ink.Pages.Crafting
{
    /// <summary>
    /// 制造技艺页面 — 对应 crafting.html 设计原型（dom-id: nav-crafting）。
    /// <para>
    /// 采用"顶部标题栏 + 左侧技艺列表 + 中央配方/详情 + 右侧工坊台 + 底部导航"五区结构，
    /// 居中显示 1400x900 主面板。通过 <see cref="NavigationRequested"/> 事件向路由器
    /// 暴露导航跳转：返回沉浸模式（combat-hud）。
    /// </para>
    /// <list type="bullet">
    ///   <item>顶部：返回按钮 + "制造技艺"标题 + "江湖百业工坊"副标题 + 搜索按钮</item>
    ///   <item>左侧（280px）：采集/制造 Tab 切换 + 5 项制造技艺列表（五行标识 + 等级 + 熟练度条）</item>
    ///   <item>中央：锻造配方 4×2 网格（8 张配方卡，按品质色描边）+ 配方详情（图标/名称/标签 + 4 项材料 + 4 格信息）</item>
    ///   <item>右侧（320px）：产出预览 + 4 条属性 + 制作进度 + 批量 1/5/10 + 制造按钮 + 5 条制作日志</item>
    ///   <item>底部：返回沉浸模式按钮</item>
    /// </list>
    /// </summary>
    public class CraftingPage : ContainerControl, IInkPage
    {
        // ===================================================================
        // 布局常量
        // =======================================================================

        /// <summary>主面板尺寸（居中显示，对应 .craft-panel 1400×900）</summary>
        private static readonly Float2 MainPanelSize = new Float2(1400f, 900f);

        /// <summary>顶部标题栏高度（padding 14px + 标题 22px）</summary>
        private const float TopBarHeight = 52f;

        /// <summary>底部导航栏高度</summary>
        private const float BottomBarHeight = 56f;

        /// <summary>面板内边距</summary>
        private const float Padding = 12f;

        /// <summary>子面板间距</summary>
        private const float PanelGap = 10f;

        /// <summary>左侧技艺列表面板宽度</summary>
        private const float LeftSidebarWidth = 280f;

        /// <summary>右侧工坊台面板宽度</summary>
        private const float RightBenchWidth = 320f;

        /// <summary>顶部 Tab 切换栏高度</summary>
        private const float TabBarHeight = 40f;

        /// <summary>技艺列表项高度</summary>
        private const float SkillItemHeight = 60f;

        /// <summary>技艺列表项间距</summary>
        private const float SkillItemGap = 8f;

        /// <summary>配方网格区高度（对应 .craft-recipe-list-wrap 340px）</summary>
        private const float RecipeListHeight = 340f;

        /// <summary>配方卡片尺寸（4 列网格，单卡宽度按列宽自适应）</summary>
        private const float RecipeCardHeight = 86f;

        /// <summary>配方卡片间距</summary>
        private const float RecipeCardGap = 10f;

        /// <summary>材料行高度</summary>
        private const float MaterialRowHeight = 36f;

        /// <summary>材料行间距</summary>
        private const float MaterialRowGap = 6f;

        /// <summary>信息网格行高/列间距</summary>
        private const float InfoGridGap = 8f;

        /// <summary>日志条目高度</summary>
        private const float LogItemHeight = 24f;

        /// <summary>日志条目间距</summary>
        private const float LogItemGap = 4f;

        /// <summary>批量按钮宽度</summary>
        private const float BatchBtnWidth = 56f;

        /// <summary>批量按钮高度</summary>
        private const float BatchBtnHeight = 30f;

        /// <summary>导航按钮宽度</summary>
        private const float NavButtonWidth = 160f;

        /// <summary>导航按钮高度</summary>
        private const float NavButtonHeight = 36f;

        // ===================================================================
        // 子控件引用 — 主面板与顶部栏
        // =======================================================================

        /// <summary>主面板容器（居中 1400x900，带抬升阴影）</summary>
        private InkPanelElevated _mainPanel;

        /// <summary>顶部标题栏</summary>
        private InkPanel _topBar;

        /// <summary>左上角返回按钮</summary>
        private InkBackButton _backButton;

        /// <summary>顶部"制造技艺"主标题</summary>
        private Label _titleLabel;

        /// <summary>顶部"江湖百业工坊"副标题</summary>
        private Label _subtitleLabel;

        /// <summary>顶部右侧"搜索配方"按钮</summary>
        private InkButton _searchButton;

        // ===================================================================
        // 子控件引用 — 左侧技艺列表
        // =======================================================================

        /// <summary>左侧技艺列表面板</summary>
        private InkPanel _leftSidebar;

        /// <summary>"采集"Tab 按钮</summary>
        private InkButton _tabGather;

        /// <summary>"制造"Tab 按钮（默认激活）</summary>
        private InkButton _tabCraft;

        /// <summary>5 项制造技艺列表项容器</summary>
        private ContainerControl[] _skillItems;

        /// <summary>5 条技艺熟练度进度条</summary>
        private InkBar[] _skillBars;

        // ===================================================================
        // 子控件引用 — 中央配方与详情
        // =======================================================================

        /// <summary>中央内容容器</summary>
        private InkPanel _middleContent;

        /// <summary>8 张配方卡片容器</summary>
        private ContainerControl[] _recipeCards;

        /// <summary>配方详情面板</summary>
        private InkPanel _detailPanel;

        /// <summary>配方详情图标格</summary>
        private InkCell _detailIconCell;

        /// <summary>配方详情名称标签</summary>
        private Label _detailNameLabel;

        /// <summary>4 条材料行容器</summary>
        private ContainerControl[] _materialRows;

        /// <summary>4 格信息项容器</summary>
        private ContainerControl[] _infoItems;

        // ===================================================================
        // 子控件引用 — 右侧工坊台
        // =======================================================================

        /// <summary>右侧工坊台面板</summary>
        private InkPanel _rightBench;

        /// <summary>产出预览图标格（大）</summary>
        private InkCell _previewCell;

        /// <summary>产出预览名称标签</summary>
        private Label _previewNameLabel;

        /// <summary>正在制作进度条</summary>
        private InkBar _craftProgressBar;

        /// <summary>3 个批量按钮（1/5/10）</summary>
        private InkButton[] _batchButtons;

        /// <summary>制造按钮</summary>
        private InkButton _craftButton;

        // ===================================================================
        // 子控件引用 — 底部导航栏
        // =======================================================================

        /// <summary>底部导航栏</summary>
        private InkPanel _bottomBar;

        /// <summary>返回沉浸模式按钮</summary>
        private InkButton _btnReturnHud;

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
        public CraftingPage()
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
                BuildLeftSidebar();
                BuildMiddleContent();
                BuildRightBench();
                BuildBottomBar();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[CraftingPage] 初始化失败: {ex.Message}");
            }
        }

        // ===================================================================
        // Build 方法
        // =======================================================================

        /// <summary>
        /// 构建居中主面板容器（1400x900，带抬升阴影）。
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
        /// 构建顶部标题栏：返回按钮 + 标题 + 副标题 + 搜索按钮。
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

            // "制造技艺" 主标题
            _titleLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(Padding + 40f + 16f, 0f),
                Size = new Float2(180f, TopBarHeight),
                Text = "制造技艺",
                TextColor = InkWashTheme.TextGold,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 22f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _topBar.AddChild(_titleLabel);

            // "江湖百业工坊" 副标题
            _subtitleLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(Padding + 40f + 16f + 180f + 12f, 0f),
                Size = new Float2(180f, TopBarHeight),
                Text = "江湖百业工坊",
                TextColor = InkWashTheme.TextTertiary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 12f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _topBar.AddChild(_subtitleLabel);

            // 右侧"搜索配方"按钮
            _searchButton = new InkButton
            {
                Variant = InkButtonVariant.Default,
                ButtonSize = InkButtonSize.Sm,
                Text = "搜索配方",
                AnchorPreset = AnchorPresets.TopRight,
                Location = new Float2(MainPanelSize.X - 100f - Padding, (TopBarHeight - 28f) * 0.5f),
                Size = new Float2(100f, 28f),
            };
            _topBar.AddChild(_searchButton);
        }

        /// <summary>
        /// 构建左侧技艺列表面板：采集/制造 Tab + 5 项制造技艺。
        /// </summary>
        private void BuildLeftSidebar()
        {
            float contentTop = TopBarHeight + PanelGap;
            float contentBottom = MainPanelSize.Y - BottomBarHeight - PanelGap;
            float contentH = contentBottom - contentTop;

            _leftSidebar = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(Padding, contentTop),
                Size = new Float2(LeftSidebarWidth, contentH),
            };
            _mainPanel.AddChild(_leftSidebar);

            // 顶部 Tab 切换栏（采集 / 制造）
            _tabGather = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Sm,
                Text = "采集",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 0f),
                Size = new Float2(LeftSidebarWidth * 0.5f, TabBarHeight),
                TextColor = InkWashTheme.TextTertiary,
            };
            _leftSidebar.AddChild(_tabGather);

            _tabCraft = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Sm,
                Text = "制造",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(LeftSidebarWidth * 0.5f, 0f),
                Size = new Float2(LeftSidebarWidth * 0.5f, TabBarHeight),
                TextColor = InkWashTheme.TextGold,
                BackgroundColor = new Color(
                    InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G,
                    InkWashTheme.GoldPrimary.B, 0.1f),
            };
            _leftSidebar.AddChild(_tabCraft);

            // 5 项制造技艺列表
            string[] skillNames = { "锻造", "制药", "织造", "烹饪", "机关" };
            string[] skillSubs = { "武器·防具", "丹药", "衣袍", "食物", "暗器·道具" };
            int[] skillLevels = { 40, 35, 28, 42, 20 };
            float[] skillProgress = { 0.40f, 0.35f, 0.28f, 0.42f, 0.20f };
            Color[] skillElemColors =
            {
                InkWashTheme.ElementMetal,
                InkWashTheme.ElementWood,
                InkWashTheme.ElementWater,
                InkWashTheme.ElementFire,
                InkWashTheme.ElementMetal,
            };
            string[] skillElemChars = { "金", "木", "水", "火", "金" };

            _skillItems = new ContainerControl[5];
            _skillBars = new InkBar[5];

            float listTop = TabBarHeight + Padding;
            float itemW = LeftSidebarWidth - Padding * 2;
            for (int i = 0; i < 5; i++)
            {
                float itemY = listTop + i * (SkillItemHeight + SkillItemGap);
                bool isActive = (i == 0);

                var item = new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(Padding, itemY),
                    Size = new Float2(itemW, SkillItemHeight),
                    BackgroundColor = isActive
                        ? new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G,
                                    InkWashTheme.GoldPrimary.B, 0.08f)
                        : Color.Transparent,
                };
                _skillItems[i] = item;
                _leftSidebar.AddChild(item);

                // 五行标识色块（左上）
                var elemBox = new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(10f, 10f),
                    Size = new Float2(20f, 20f),
                    BackgroundColor = skillElemColors[i],
                };
                item.AddChild(elemBox);

                var elemLabel = new Label
                {
                    AnchorPreset = AnchorPresets.StretchAll,
                    Text = skillElemChars[i],
                    TextColor = InkWashTheme.Abyss,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 11f),
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                };
                elemBox.AddChild(elemLabel);

                // 技艺名称
                var nameLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(38f, 8f),
                    Size = new Float2(120f, 18f),
                    Text = skillNames[i],
                    TextColor = InkWashTheme.TextDefault,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 14f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                item.AddChild(nameLabel);

                // 等级标签（右上）
                var lvlLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopRight,
                    Location = new Float2(itemW - 70f, 8f),
                    Size = new Float2(60f, 18f),
                    Text = "Lv." + skillLevels[i],
                    TextColor = InkWashTheme.TextTertiary,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 12f),
                    HorizontalAlignment = TextAlignment.Far,
                    VerticalAlignment = TextAlignment.Center,
                };
                item.AddChild(lvlLabel);

                // 子类描述
                var subLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(38f, 28f),
                    Size = new Float2(itemW - 48f, 14f),
                    Text = skillSubs[i],
                    TextColor = InkWashTheme.TextTertiary,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 11f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                item.AddChild(subLabel);

                // 熟练度进度条
                var bar = new InkBar
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(10f, SkillItemHeight - 14f),
                    Size = new Float2(itemW - 20f, 4f),
                    Value = skillProgress[i],
                    FillVariant = InkBarFillVariant.Gold,
                };
                _skillBars[i] = bar;
                item.AddChild(bar);
            }
        }

        /// <summary>
        /// 构建中央内容：配方网格 + 配方详情。
        /// </summary>
        private void BuildMiddleContent()
        {
            float contentTop = TopBarHeight + PanelGap;
            float contentBottom = MainPanelSize.Y - BottomBarHeight - PanelGap;
            float contentH = contentBottom - contentTop;
            float middleX = Padding + LeftSidebarWidth + PanelGap;
            float middleW = MainPanelSize.X - Padding * 2 - LeftSidebarWidth - RightBenchWidth - PanelGap * 2;

            _middleContent = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(middleX, contentTop),
                Size = new Float2(middleW, contentH),
            };
            _mainPanel.AddChild(_middleContent);

            // ===== 配方网格区（上方 340px） =====
            var recipeHeader = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 0f),
                Size = new Float2(middleW, 40f),
                BackgroundColor = Color.Transparent,
            };
            _middleContent.AddChild(recipeHeader);

            var recipeSectionTitle = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(8f, 0f),
                Size = new Float2(160f, 40f),
                Text = "锻造配方",
                TextColor = InkWashTheme.TextGold,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 16f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            recipeHeader.AddChild(recipeSectionTitle);

            var recipeCountLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopRight,
                Location = new Float2(middleW - 110f, 0f),
                Size = new Float2(100f, 40f),
                Text = "8 个配方",
                TextColor = InkWashTheme.TextTertiary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 12f),
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
            };
            recipeHeader.AddChild(recipeCountLabel);

            // 8 张配方卡片（4 列 × 2 行）
            _recipeCards = new ContainerControl[8];
            string[] recipeNames = { "玄铁剑", "寒铁刀", "玄铁盾", "精铁护腕", "精钢头盔", "铁质长枪", "青铜匕首", "寒铁战甲" };
            string[] recipeLevels = { "40", "35", "30", "25", "20", "10", "5", "45" };
            InkWashTheme.InkQuality[] recipeQualities =
            {
                InkWashTheme.InkQuality.Legendary,
                InkWashTheme.InkQuality.Epic,
                InkWashTheme.InkQuality.Rare,
                InkWashTheme.InkQuality.Rare,
                InkWashTheme.InkQuality.Uncommon,
                InkWashTheme.InkQuality.Common,
                InkWashTheme.InkQuality.Common,
                InkWashTheme.InkQuality.Common,
            };
            bool[] recipeLocked = { false, false, false, false, false, false, false, true };

            int cols = 4;
            float colGap = RecipeCardGap;
            float cardW = (middleW - Padding * 2 - colGap * (cols - 1)) / cols;
            float gridTop = 40f + Padding * 0.5f;

            for (int i = 0; i < 8; i++)
            {
                int col = i % cols;
                int row = i / cols;
                float cardX = Padding + col * (cardW + colGap);
                float cardY = gridTop + row * (RecipeCardHeight + RecipeCardGap);
                bool isSelected = (i == 0);
                bool isLocked = recipeLocked[i];
                Color qualityColor = InkWashTheme.QualityColor(recipeQualities[i]);

                var card = new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(cardX, cardY),
                    Size = new Float2(cardW, RecipeCardHeight),
                    BackgroundColor = isLocked
                        ? new Color(InkWashTheme.BaseElevated.R, InkWashTheme.BaseElevated.G,
                                    InkWashTheme.BaseElevated.B, 0.5f)
                        : (isSelected
                            ? new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G,
                                        InkWashTheme.GoldPrimary.B, 0.1f)
                            : InkWashTheme.BaseElevated),
                };
                _recipeCards[i] = card;
                _middleContent.AddChild(card);

                // 品质色边框绘制：用 InkCell 作为内嵌图标格（自带品质边框）
                var iconCell = new InkCell
                {
                    AnchorPreset = AnchorPresets.TopCenter,
                    Location = new Float2((cardW - 44f) * 0.5f, 6f),
                    Size = new Float2(44f, 44f),
                    Quality = recipeQualities[i],
                };
                card.AddChild(iconCell);

                // 配方名称
                var nameLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(4f, 52f),
                    Size = new Float2(cardW - 8f, 16f),
                    Text = recipeNames[i],
                    TextColor = isLocked ? InkWashTheme.TextTertiary : InkWashTheme.TextDefault,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 13f),
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                };
                card.AddChild(nameLabel);

                // 等级需求
                var reqLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(4f, 68f),
                    Size = new Float2(cardW - 8f, 14f),
                    Text = "Lv." + recipeLevels[i],
                    TextColor = isLocked ? InkWashTheme.TextBlood : InkWashTheme.TextTertiary,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 11f),
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                };
                card.AddChild(reqLabel);
            }

            // ===== 配方详情区（下方 flex） =====
            float detailTop = gridTop + 2 * (RecipeCardHeight + RecipeCardGap) + Padding;
            float detailH = contentH - detailTop;

            _detailPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, detailTop),
                Size = new Float2(middleW, detailH),
            };
            _middleContent.AddChild(_detailPanel);

            // 详情标题栏
            var detailHeader = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 0f),
                Size = new Float2(middleW, 40f),
                BackgroundColor = Color.Transparent,
            };
            _detailPanel.AddChild(detailHeader);

            var detailTitle = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(8f, 0f),
                Size = new Float2(160f, 40f),
                Text = "配方详情",
                TextColor = InkWashTheme.TextGold,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 16f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            detailHeader.AddChild(detailTitle);

            var craftableLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopRight,
                Location = new Float2(middleW - 130f, 0f),
                Size = new Float2(120f, 40f),
                Text = "可制 0 个",
                TextColor = InkWashTheme.TextSecondary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 13f),
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
            };
            detailHeader.AddChild(craftableLabel);

            // 详情头部：图标 + 名称 + 标签
            float headTop = 44f;
            _detailIconCell = new InkCell
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(Padding, headTop),
                Size = new Float2(56f, 56f),
                Quality = InkWashTheme.InkQuality.Legendary,
            };
            _detailPanel.AddChild(_detailIconCell);

            _detailNameLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(Padding + 56f + 16f, headTop),
                Size = new Float2(180f, 24f),
                Text = "玄铁剑",
                TextColor = InkWashTheme.TextDefault,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 18f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _detailPanel.AddChild(_detailNameLabel);

            // 标签行（传说 / 武器·剑 / 五行·金）
            string[] tagTexts = { "传说", "武器·剑", "五行·金" };
            InkTagVariant[] tagVariants =
            {
                InkTagVariant.Brand,
                InkTagVariant.Default,
                InkTagVariant.Default,
            };
            float tagX = Padding + 56f + 16f;
            for (int i = 0; i < 3; i++)
            {
                float tagW = 64f;
                var tag = new InkTag
                {
                    TagVariant = tagVariants[i],
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(tagX, headTop + 28f),
                    Size = new Float2(tagW, 20f),
                    Text = tagTexts[i],
                };
                _detailPanel.AddChild(tag);
                tagX += tagW + 6f;
            }

            // 所需材料清单
            float matTitleY = headTop + 56f + 16f;
            var matTitle = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(Padding, matTitleY),
                Size = new Float2(160f, 20f),
                Text = "所需材料",
                TextColor = InkWashTheme.TextSecondary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _detailPanel.AddChild(matTitle);

            string[] matNames = { "玄铁矿", "寒铁锭", "火灵石", "千年木炭" };
            string[] matQuantities = { "5/3", "3/5", "1/2", "2/4" };
            bool[] matSufficient = { false, true, true, true };
            InkWashTheme.InkQuality[] matQualities =
            {
                InkWashTheme.InkQuality.Rare,
                InkWashTheme.InkQuality.Epic,
                InkWashTheme.InkQuality.Rare,
                InkWashTheme.InkQuality.Common,
            };
            _materialRows = new ContainerControl[4];
            float matRowY = matTitleY + 24f;
            float matRowW = middleW - Padding * 2;
            for (int i = 0; i < 4; i++)
            {
                var row = new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(Padding, matRowY + i * (MaterialRowHeight + MaterialRowGap)),
                    Size = new Float2(matRowW, MaterialRowHeight),
                    BackgroundColor = InkWashTheme.BaseElevated,
                };
                _materialRows[i] = row;
                _detailPanel.AddChild(row);

                // 材料图标格
                var matCell = new InkCell
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(8f, 4f),
                    Size = new Float2(28f, 28f),
                    Quality = matQualities[i],
                };
                row.AddChild(matCell);

                // 材料名称
                var nameL = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(44f, 0f),
                    Size = new Float2(matRowW - 44f - 120f - 60f, MaterialRowHeight),
                    Text = matNames[i],
                    TextColor = InkWashTheme.TextDefault,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 13f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                row.AddChild(nameL);

                // 数量（需求/持有）
                var qtyL = new Label
                {
                    AnchorPreset = AnchorPresets.TopRight,
                    Location = new Float2(matRowW - 120f, 0f),
                    Size = new Float2(60f, MaterialRowHeight),
                    Text = matQuantities[i],
                    TextColor = matSufficient[i] ? InkWashTheme.TextJade : InkWashTheme.TextBlood,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 13f),
                    HorizontalAlignment = TextAlignment.Far,
                    VerticalAlignment = TextAlignment.Center,
                };
                row.AddChild(qtyL);

                // 状态徽章
                var statusTag = new InkTag
                {
                    TagVariant = matSufficient[i] ? InkTagVariant.Default : InkTagVariant.Vermilion,
                    AnchorPreset = AnchorPresets.TopRight,
                    Location = new Float2(matRowW - 56f, (MaterialRowHeight - 20f) * 0.5f),
                    Size = new Float2(48f, 20f),
                    Text = matSufficient[i] ? "充足" : "不足",
                };
                row.AddChild(statusTag);
            }

            // 4 格信息网格（制作时间/成功率/银两消耗/技艺经验）
            float infoTop = matTitleY + 24f + 4 * (MaterialRowHeight + MaterialRowGap) + 8f;
            string[] infoLabels = { "制作时间", "成功率", "银两消耗", "技艺经验" };
            string[] infoValues = { "30 秒", "85%", "2,000 两", "+150" };
            _infoItems = new ContainerControl[4];
            int infoCols = 2;
            float infoColW = (middleW - Padding * 2 - InfoGridGap) * 0.5f;
            for (int i = 0; i < 4; i++)
            {
                int col = i % infoCols;
                int row = i / infoCols;
                float itemX = Padding + col * (infoColW + InfoGridGap);
                float itemY = infoTop + row * (40f + InfoGridGap);

                var item = new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(itemX, itemY),
                    Size = new Float2(infoColW, 40f),
                    BackgroundColor = InkWashTheme.BaseElevated,
                };
                _infoItems[i] = item;
                _detailPanel.AddChild(item);

                var labL = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(12f, 0f),
                    Size = new Float2(80f, 40f),
                    Text = infoLabels[i],
                    TextColor = InkWashTheme.TextTertiary,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 12f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                item.AddChild(labL);

                var valL = new Label
                {
                    AnchorPreset = AnchorPresets.TopRight,
                    Location = new Float2(infoColW - 92f, 0f),
                    Size = new Float2(80f, 40f),
                    Text = infoValues[i],
                    TextColor = i == 1 ? InkWashTheme.TextJade : InkWashTheme.TextDefault,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 13f),
                    HorizontalAlignment = TextAlignment.Far,
                    VerticalAlignment = TextAlignment.Center,
                };
                item.AddChild(valL);
            }
        }

        /// <summary>
        /// 构建右侧工坊台：产出预览 + 属性 + 进度 + 批量 + 制造按钮 + 日志。
        /// </summary>
        private void BuildRightBench()
        {
            float contentTop = TopBarHeight + PanelGap;
            float contentBottom = MainPanelSize.Y - BottomBarHeight - PanelGap;
            float contentH = contentBottom - contentTop;
            float benchX = MainPanelSize.X - Padding - RightBenchWidth;

            _rightBench = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(benchX, contentTop),
                Size = new Float2(RightBenchWidth, contentH),
            };
            _mainPanel.AddChild(_rightBench);

            float cursorY = Padding;
            float innerW = RightBenchWidth - Padding * 2;

            // ===== 产出预览区 =====
            var previewPanel = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(Padding, cursorY),
                Size = new Float2(innerW, 120f),
                BackgroundColor = new Color(
                    InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G,
                    InkWashTheme.GoldPrimary.B, 0.04f),
            };
            _rightBench.AddChild(previewPanel);

            _previewCell = new InkCell
            {
                AnchorPreset = AnchorPresets.TopCenter,
                Location = new Float2((innerW - 64f) * 0.5f, 8f),
                Size = new Float2(64f, 64f),
                Quality = InkWashTheme.InkQuality.Legendary,
            };
            previewPanel.AddChild(_previewCell);

            _previewNameLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopCenter,
                Location = new Float2((innerW - 160f) * 0.5f, 76f),
                Size = new Float2(160f, 20f),
                Text = "玄铁剑",
                TextColor = InkWashTheme.TextGold,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 16f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            previewPanel.AddChild(_previewNameLabel);

            var previewTags = new Label
            {
                AnchorPreset = AnchorPresets.TopCenter,
                Location = new Float2((innerW - 200f) * 0.5f, 96f),
                Size = new Float2(200f, 18f),
                Text = "传说 · 武器·剑",
                TextColor = InkWashTheme.TextSecondary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 11f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            previewPanel.AddChild(previewTags);

            cursorY += 120f + Padding;

            // ===== 属性区（4 行） =====
            string[] attrLabels = { "五行", "攻击力", "暴击率", "会心率" };
            string[] attrValues = { "金", "+120", "+5%", "+3%" };
            for (int i = 0; i < 4; i++)
            {
                var row = new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(Padding, cursorY + i * 22f),
                    Size = new Float2(innerW, 22f),
                    BackgroundColor = Color.Transparent,
                };
                _rightBench.AddChild(row);

                var labL = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(8f, 0f),
                    Size = new Float2(100f, 22f),
                    Text = attrLabels[i],
                    TextColor = InkWashTheme.TextTertiary,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 13f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                row.AddChild(labL);

                var valL = new Label
                {
                    AnchorPreset = AnchorPresets.TopRight,
                    Location = new Float2(innerW - 88f, 0f),
                    Size = new Float2(80f, 22f),
                    Text = attrValues[i],
                    TextColor = i == 3 ? InkWashTheme.TextJade : InkWashTheme.TextDefault,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 13f),
                    HorizontalAlignment = TextAlignment.Far,
                    VerticalAlignment = TextAlignment.Center,
                };
                row.AddChild(valL);
            }
            cursorY += 4 * 22f + Padding;

            // ===== 制作进度区 =====
            var progHeadRow = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(Padding, cursorY),
                Size = new Float2(innerW, 20f),
                BackgroundColor = Color.Transparent,
            };
            _rightBench.AddChild(progHeadRow);

            var progLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(8f, 0f),
                Size = new Float2(60f, 20f),
                Text = "正在制作",
                TextColor = InkWashTheme.TextTertiary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 11f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            progHeadRow.AddChild(progLabel);

            var progName = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(70f, 0f),
                Size = new Float2(innerW - 70f - 50f, 20f),
                Text = "精铁护腕",
                TextColor = InkWashTheme.TextDefault,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            progHeadRow.AddChild(progName);

            var progPct = new Label
            {
                AnchorPreset = AnchorPresets.TopRight,
                Location = new Float2(innerW - 48f, 0f),
                Size = new Float2(40f, 20f),
                Text = "65%",
                TextColor = InkWashTheme.TextGold,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 12f),
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
            };
            progHeadRow.AddChild(progPct);

            _craftProgressBar = new InkBar
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(Padding, cursorY + 22f),
                Size = new Float2(innerW, 4f),
                Value = 0.65f,
                FillVariant = InkBarFillVariant.Gold,
            };
            _rightBench.AddChild(_craftProgressBar);

            cursorY += 22f + 4f + Padding;

            // ===== 批量选择 + 制造按钮 =====
            var batchRow = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(Padding, cursorY),
                Size = new Float2(innerW, BatchBtnHeight),
                BackgroundColor = Color.Transparent,
            };
            _rightBench.AddChild(batchRow);

            var batchLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(8f, 0f),
                Size = new Float2(40f, BatchBtnHeight),
                Text = "批量",
                TextColor = InkWashTheme.TextTertiary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 12f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            batchRow.AddChild(batchLabel);

            _batchButtons = new InkButton[3];
            int[] batchCounts = { 1, 5, 10 };
            float batchStartX = 52f;
            float batchGap = 8f;
            for (int i = 0; i < 3; i++)
            {
                bool isActive = (i == 0);
                var btn = new InkButton
                {
                    Variant = isActive ? InkButtonVariant.Primary : InkButtonVariant.Ghost,
                    ButtonSize = InkButtonSize.Sm,
                    Text = batchCounts[i].ToString(),
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(batchStartX + i * (BatchBtnWidth + batchGap), 0f),
                    Size = new Float2(BatchBtnWidth, BatchBtnHeight),
                };
                _batchButtons[i] = btn;
                batchRow.AddChild(btn);
            }

            cursorY += BatchBtnHeight + 8f;

            // 制造按钮
            _craftButton = new InkButton
            {
                Variant = InkButtonVariant.Primary,
                ButtonSize = InkButtonSize.Md,
                Text = "材料不足 · 2,000 两",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(Padding, cursorY),
                Size = new Float2(innerW, 40f),
            };
            _rightBench.AddChild(_craftButton);

            cursorY += 40f + Padding;

            // ===== 制作日志区 =====
            var logTitle = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(Padding, cursorY),
                Size = new Float2(120f, 20f),
                Text = "制作日志",
                TextColor = InkWashTheme.TextSecondary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _rightBench.AddChild(logTitle);

            cursorY += 24f;

            string[] logNames = { "精铁护腕", "铁质长枪", "铁质长枪", "寒铁刀", "铁质长枪" };
            string[] logTimes = { "14:32", "14:25", "14:18", "14:10", "14:02" };
            bool[] logSuccess = { true, true, true, false, true };
            for (int i = 0; i < 5; i++)
            {
                var row = new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(Padding, cursorY + i * (LogItemHeight + LogItemGap)),
                    Size = new Float2(innerW, LogItemHeight),
                    BackgroundColor = InkWashTheme.BaseElevated,
                };
                _rightBench.AddChild(row);

                var dot = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(8f, 0f),
                    Size = new Float2(12f, LogItemHeight),
                    Text = logSuccess[i] ? "✓" : "✕",
                    TextColor = logSuccess[i] ? InkWashTheme.TextJade : InkWashTheme.TextBlood,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 12f),
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                };
                row.AddChild(dot);

                var nameL = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(24f, 0f),
                    Size = new Float2(innerW - 24f - 50f, LogItemHeight),
                    Text = logNames[i],
                    TextColor = InkWashTheme.TextDefault,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 12f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                row.AddChild(nameL);

                var timeL = new Label
                {
                    AnchorPreset = AnchorPresets.TopRight,
                    Location = new Float2(innerW - 48f, 0f),
                    Size = new Float2(40f, LogItemHeight),
                    Text = logTimes[i],
                    TextColor = InkWashTheme.TextTertiary,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 11f),
                    HorizontalAlignment = TextAlignment.Far,
                    VerticalAlignment = TextAlignment.Center,
                };
                row.AddChild(timeL);
            }
        }

        /// <summary>
        /// 构建底部导航栏：返回沉浸模式按钮。
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

            _btnReturnHud = new InkButton
            {
                Variant = InkButtonVariant.Default,
                ButtonSize = InkButtonSize.Md,
                Text = "返回沉浸模式",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(
                    (MainPanelSize.X - NavButtonWidth) * 0.5f,
                    (BottomBarHeight - NavButtonHeight) * 0.5f),
                Size = new Float2(NavButtonWidth, NavButtonHeight),
            };
            _btnReturnHud.ButtonClicked += (b) => OnSystemNavButtonClicked(InkPageDomIds.CombatHud, b);
            _bottomBar.AddChild(_btnReturnHud);
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
                FlaxEngine.Debug.LogError($"[CraftingPage] 返回按钮触发失败: {ex.Message}");
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
                FlaxEngine.Debug.LogError($"[CraftingPage] NavigationRequested({domId}) 触发失败: {ex.Message}");
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
                FlaxEngine.Debug.LogWarning($"[CraftingPage] EmitGoldAtButton 失败: {ex.Message}");
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
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[CraftingPage] RefreshLayout 失败: {ex.Message}");
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

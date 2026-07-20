using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.StyleSystem;
using System;

namespace HundunWorld.Game.UI.Ink.Pages.Inventory
{
    /// <summary>
    /// 装备强化页面 — 对应 equipment-enhance.html 设计原型。
    /// <para>
    /// 水墨古风锻造工坊界面，承担装备强化/镶嵌/精炼/调律/淬火等装备养成入口。
    /// 整体布局沿用 HTML 原型的三段式结构：
    /// <list type="bullet">
    ///   <item>顶部：返回按钮 + "装备强化"标题 + "锻造工坊"徽章 + 提示信息</item>
    ///   <item>Tab 栏：强化/镶嵌/精炼/调律/淬火 5 个 Tab + "强化失败将降级"警告</item>
    ///   <item>中部三栏：
    ///     <list type="bullet">
    ///       <item>左侧：品质筛选（全部/普通/优良/稀有/史诗/传说）+ 装备列表（8 项 mock）</item>
    ///       <item>中央：装备预览 + 装备名 + 强化等级 + 属性对比表（攻击/暴击/命中/穿透/会心）+
    ///             成功率进度条 + 材料消耗 + "执行强化"按钮</item>
    ///       <item>右侧：所需材料 + 强化进度 + 套装效果 + 五行分布</item>
    ///     </list>
    ///   </item>
    ///   <item>底部：返回沉浸模式 + 跳转背包（<see cref="InkPageDomIds.NavInventory"/>）</item>
    /// </list>
    /// 通过 <see cref="NavigationRequested"/> 事件向路由器暴露导航请求。
    /// 当前实现全部使用 mock 数据；后续接入装备系统时，
    /// 通过 <see cref="RefreshEquipmentList"/> 等公共方法替换内容即可。
    /// </para>
    /// </summary>
    public class EquipmentEnhancePage : ContainerControl, IInkPage
    {
        // ===================================================================
        // 布局常量
        // =======================================================================

        /// <summary>顶部标题栏高度（像素）</summary>
        private const float HeaderHeight = 52f;

        /// <summary>Tab 栏高度（像素）</summary>
        private const float TabBarHeight = 44f;

        /// <summary>底部导航按钮栏高度（像素）</summary>
        private const float BottomNavHeight = 36f;

        /// <summary>屏幕边距（像素）</summary>
        private const float ScreenEdge = 16f;

        /// <summary>区域间距（像素）</summary>
        private const float RegionGap = 12f;

        /// <summary>左栏宽度（像素）</summary>
        private const float LeftColumnWidth = 250f;

        /// <summary>右栏宽度（像素）</summary>
        private const float RightColumnWidth = 300f;

        /// <summary>三栏之间的水平间距（像素）</summary>
        private const float ColumnGap = 12f;

        /// <summary>导航按钮宽度（像素）</summary>
        private const float NavBtnWidth = 140f;

        /// <summary>导航按钮间距（像素）</summary>
        private const float NavBtnGap = 8f;

        /// <summary>顶部 Tab 按钮宽度（像素）</summary>
        private const float TabBtnWidth = 80f;

        /// <summary>顶部 Tab 按钮间距（像素）</summary>
        private const float TabBtnGap = 4f;

        /// <summary>装备列表项高度（像素）</summary>
        private const float EquipItemHeight = 48f;

        /// <summary>装备列表项间距（像素）</summary>
        private const float EquipItemGap = 6f;

        /// <summary>属性对比表行高（像素）</summary>
        private const float AttrRowHeight = 22f;

        // ===================================================================
        // 子控件引用 — 顶部
        // =======================================================================

        /// <summary>顶部标题栏面板</summary>
        private InkPanel _headerPanel;

        /// <summary>返回按钮（返回 HUD）</summary>
        private InkButton _backButton;

        /// <summary>页面标题"装备强化"</summary>
        private Label _titleLabel;

        /// <summary>"锻造工坊"徽章</summary>
        private Label _forgeBadge;

        /// <summary>提示信息标签</summary>
        private Label _hintLabel;

        /// <summary>Tab 栏面板</summary>
        private InkPanel _tabBarPanel;

        /// <summary>5 个 Tab 按钮（强化/镶嵌/精炼/调律/淬火）</summary>
        private InkButton[] _tabButtons;

        /// <summary>当前激活的 Tab 索引</summary>
        private int _activeTabIndex = 0;

        /// <summary>"强化失败将降级"警告标签</summary>
        private Label _warnLabel;

        // ===================================================================
        // 子控件引用 — 左栏
        // =======================================================================

        /// <summary>左栏面板（品质筛选 + 装备列表）</summary>
        private InkPanel _leftPanel;

        /// <summary>品质筛选按钮组（全部/普通/优良/稀有/史诗/传说）</summary>
        private InkButton[] _qualityFilterButtons;

        /// <summary>当前激活的品质筛选索引</summary>
        private int _activeQualityFilter = 0;

        /// <summary>装备列表标题</summary>
        private Label _equipListTitle;

        /// <summary>装备数量统计标签</summary>
        private Label _equipCountLabel;

        /// <summary>8 个装备列表项容器</summary>
        private ContainerControl[] _equipItems;

        /// <summary>当前选中的装备索引</summary>
        private int _selectedEquipIndex = 0;

        // ===================================================================
        // 子控件引用 — 中栏
        // =======================================================================

        /// <summary>中栏面板</summary>
        private InkPanel _middlePanel;

        /// <summary>装备预览区（用 InkCell 显示品质边框 + 占位）</summary>
        private InkCell _previewCell;

        /// <summary>"装备预览"提示文字</summary>
        private Label _previewHintLabel;

        /// <summary>装备名称标签</summary>
        private Label _equipNameLabel;

        /// <summary>品质徽章（如"传说"）</summary>
        private Label _qualityTagLabel;

        /// <summary>强化等级标签（如"+12"）</summary>
        private Label _enhanceLevelLabel;

        /// <summary>装备类型徽章（如"双手剑"）</summary>
        private Label _equipTypeTagLabel;

        /// <summary>装备等级徽章（如"60级"）</summary>
        private Label _equipLevelTagLabel;

        /// <summary>"属性对比"小节标题</summary>
        private Label _attrCompareTitle;

        /// <summary>属性对比表表头（属性/当前/强化后/变化）</summary>
        private Label _attrTableHeader;

        /// <summary>5 行属性对比数据（属性名/当前值/强化后值/变化值）</summary>
        private Label[] _attrNameLabels;
        private Label[] _attrCurrentLabels;
        private Label[] _attrNextLabels;
        private Label[] _attrDeltaLabels;

        /// <summary>"成功率"标签</summary>
        private Label _successRateLabel;

        /// <summary>成功率数值标签</summary>
        private Label _successRateValueLabel;

        /// <summary>成功率进度条</summary>
        private InkBar _successRateBar;

        /// <summary>"失败将降低1级强化等级"提示</summary>
        private Label _failWarnLabel;

        /// <summary>"强化石"消耗标签</summary>
        private Label _stoneCostLabel;

        /// <summary>"银两"消耗标签</summary>
        private Label _silverCostLabel;

        /// <summary>"执行强化"按钮</summary>
        private InkButton _enhanceButton;

        // ===================================================================
        // 子控件引用 — 右栏
        // =======================================================================

        /// <summary>右栏面板（所需材料/强化进度/套装效果/五行分布）</summary>
        private InkPanel _rightPanel;

        /// <summary>"所需材料"小节标题</summary>
        private Label _materialsTitle;

        /// <summary>3 项材料标签（强化石/精炼砂/银两）</summary>
        private Label[] _materialLabels;

        /// <summary>"强化进度"小节标题</summary>
        private Label _progressTitle;

        /// <summary>当前强化等级标签（如"+12"）</summary>
        private Label _progressCurrentLabel;

        /// <summary>下一强化等级标签（如"+13"）</summary>
        private Label _progressNextLabel;

        /// <summary>强化进度条</summary>
        private InkBar _progressBar;

        /// <summary>"上限 +20"提示</summary>
        private Label _progressMaxLabel;

        /// <summary>"套装效果"小节标题</summary>
        private Label _setTitle;

        /// <summary>套装名称标签</summary>
        private Label _setNameLabel;

        /// <summary>套装件数标签</summary>
        private Label _setCountLabel;

        /// <summary>2 件套效果标签</summary>
        private Label _setBonus2Label;

        /// <summary>4 件套效果标签</summary>
        private Label _setBonus4Label;

        /// <summary>"五行分布"小节标题</summary>
        private Label _elementTitle;

        /// <summary>5 个五行行标签（金/木/水/火/土）</summary>
        private Label[] _elementLabels;
        private InkBar[] _elementBars;

        // ===================================================================
        // 子控件引用 — 底部
        // =======================================================================

        /// <summary>底部导航按钮面板</summary>
        private InkPanel _bottomNavPanel;

        /// <summary>"返回沉浸模式"按钮</summary>
        private InkButton _returnHudButton;

        /// <summary>"跳转背包"按钮</summary>
        private InkButton _gotoInventoryButton;

        // ===================================================================
        // 公共 API
        // =======================================================================

        /// <summary>
        /// 导航请求事件。由返回按钮与底部导航按钮触发，
        /// 参数为 <see cref="InkPageDomIds"/> 中定义的 dom-id 字符串。
        /// </summary>
        public event Action<string> NavigationRequested;

        /// <summary>
        /// 粒子动效系统引用（可选，由外部注入）。
        /// </summary>
        public InkParticleSystem ParticleSystem { get; set; }

        // ===================================================================
        // 构造函数
        // =======================================================================

        /// <summary>
        /// 构造函数：初始化全部子控件并填充 mock 数据。
        /// </summary>
        public EquipmentEnhancePage()
        {
            try
            {
                AnchorPreset = AnchorPresets.StretchAll;
                Offsets = Margin.Zero;
                BackgroundColor = Color.Transparent;
                ClipChildren = false;
                AutoFocus = false;

                BuildHeader();
                BuildTabBar();
                BuildLeftPanel();
                BuildMiddlePanel();
                BuildRightPanel();
                BuildBottomNav();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[EquipmentEnhancePage] 初始化失败: {ex.Message}");
            }
        }

        // ===================================================================
        // Build 方法
        // =======================================================================

        /// <summary>
        /// 构建顶部标题栏：返回按钮 + 标题 + 锻造工坊徽章 + 提示信息。
        /// </summary>
        private void BuildHeader()
        {
            _headerPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
            };

            // 返回按钮（返回 HUD）
            _backButton = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Md,
                Text = "←",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, (HeaderHeight - 32f) * 0.5f),
                Size = new Float2(36f, 32f),
            };
            _backButton.ButtonClicked += (b) => OnSystemNavButtonClicked(InkPageDomIds.CombatHud, b);
            _headerPanel.AddChild(_backButton);

            // 标题"装备强化"
            _titleLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(60f, 0f),
                Size = new Float2(160f, HeaderHeight),
                Text = "装备强化",
                TextColor = InkWashTheme.TextGold,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 22f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _headerPanel.AddChild(_titleLabel);

            // "锻造工坊"徽章
            _forgeBadge = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(220f, (HeaderHeight - 22f) * 0.5f),
                Size = new Float2(80f, 22f),
                Text = "锻造工坊",
                TextColor = InkWashTheme.TextTertiary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 11f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                BackgroundColor = new Color(
                    InkWashTheme.BorderFaint.R, InkWashTheme.BorderFaint.G,
                    InkWashTheme.BorderFaint.B, 0.3f),
            };
            _headerPanel.AddChild(_forgeBadge);

            // 提示信息（靠右对齐）
            _hintLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopRight,
                Location = new Float2(0f, 0f),
                Size = new Float2(360f, HeaderHeight),
                Text = "ℹ  选择装备后进行锻造，不同模式消耗不同材料",
                TextColor = InkWashTheme.TextTertiary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 12f),
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
            };
            _headerPanel.AddChild(_hintLabel);

            AddChild(_headerPanel);
        }

        /// <summary>
        /// 构建 Tab 栏：强化/镶嵌/精炼/调律/淬火 5 个 Tab + "强化失败将降级"警告。
        /// </summary>
        private void BuildTabBar()
        {
            _tabBarPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
            };

            // 5 个 Tab 按钮
            _tabButtons = new InkButton[5];
            string[] tabNames = { "强化", "镶嵌", "精炼", "调律", "淬火" };
            float tabX = 16f;
            for (int i = 0; i < _tabButtons.Length; i++)
            {
                int capturedIndex = i;
                var btn = new InkButton
                {
                    Variant = InkButtonVariant.Ghost,
                    ButtonSize = InkButtonSize.Md,
                    Text = tabNames[i],
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(tabX, (TabBarHeight - 32f) * 0.5f),
                    Size = new Float2(TabBtnWidth, 32f),
                };
                btn.ButtonClicked += (b) => OnTabButtonClicked(capturedIndex, b);
                _tabBarPanel.AddChild(btn);
                _tabButtons[i] = btn;
                tabX += TabBtnWidth + TabBtnGap;
            }

            // 初始高亮 Tab 0
            ApplyTabHighlight();

            // "强化失败将降级"警告（靠右对齐）
            _warnLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopRight,
                Location = new Float2(0f, 0f),
                Size = new Float2(220f, TabBarHeight),
                Text = "⚠ 强化失败将降级",
                TextColor = InkWashTheme.TextBlood,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 11f),
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
            };
            _tabBarPanel.AddChild(_warnLabel);

            AddChild(_tabBarPanel);
        }

        /// <summary>
        /// 构建左栏：品质筛选 + 装备列表（8 项 mock）。
        /// </summary>
        private void BuildLeftPanel()
        {
            _leftPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
            };

            // "品质筛选"标题
            var filterTitle = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 10f),
                Size = new Float2(LeftColumnWidth - 24f, 18f),
                Text = "◆ 品质筛选",
                TextColor = InkWashTheme.TextSecondary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 12f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _leftPanel.AddChild(filterTitle);

            // 6 个品质筛选按钮（全部/普通/优良/稀有/史诗/传说）
            _qualityFilterButtons = new InkButton[6];
            string[] filterNames = { "全部", "普通", "优良", "稀有", "史诗", "传说" };
            float filterX = 12f;
            float filterY = 32f;
            float filterBtnW = (LeftColumnWidth - 24f - 5f * 4f) / 3f;
            for (int i = 0; i < _qualityFilterButtons.Length; i++)
            {
                int capturedIndex = i;
                var btn = new InkButton
                {
                    Variant = InkButtonVariant.Ghost,
                    ButtonSize = InkButtonSize.Sm,
                    Text = filterNames[i],
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(filterX, filterY),
                    Size = new Float2(filterBtnW, 22f),
                };
                btn.ButtonClicked += (b) => OnQualityFilterClicked(capturedIndex, b);
                _leftPanel.AddChild(btn);
                _qualityFilterButtons[i] = btn;

                filterX += filterBtnW + 4f;
                if (i == 2) // 3 个一行，换行
                {
                    filterX = 12f;
                    filterY += 26f;
                }
            }
            ApplyQualityFilterHighlight();

            // 分割线（用 1px 高的 Label 近似）
            var divider = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, filterY + 30f),
                Size = new Float2(LeftColumnWidth - 24f, 1f),
                BackgroundColor = InkWashTheme.Divider,
            };
            _leftPanel.AddChild(divider);

            // 装备列表标题
            _equipListTitle = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, filterY + 38f),
                Size = new Float2(120f, 18f),
                Text = "装备列表",
                TextColor = InkWashTheme.TextSecondary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 12f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _leftPanel.AddChild(_equipListTitle);

            _equipCountLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopRight,
                Location = new Float2(0f, filterY + 38f),
                Size = new Float2(60f, 18f),
                Text = "8 件",
                TextColor = InkWashTheme.TextTertiary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 11f),
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
            };
            _leftPanel.AddChild(_equipCountLabel);

            // 8 个装备列表项
            // mock 数据：(name, type, quality, enhanceLevel)
            var mockEquips = new (string name, string type, InkWashTheme.InkQuality quality, int enhance)[]
            {
                ("玄铁重剑", "双手剑 · 60级", InkWashTheme.InkQuality.Legendary, 12),
                ("赤霄枪",   "长枪 · 60级",   InkWashTheme.InkQuality.Legendary, 15),
                ("紫金冠",   "头冠 · 55级",   InkWashTheme.InkQuality.Epic,      10),
                ("天罡袍",   "法袍 · 55级",   InkWashTheme.InkQuality.Epic,       9),
                ("碧玉杖",   "法杖 · 50级",   InkWashTheme.InkQuality.Rare,       5),
                ("玄武盾",   "盾牌 · 50级",   InkWashTheme.InkQuality.Rare,       6),
                ("寒月刀",   "单刀 · 45级",   InkWashTheme.InkQuality.Uncommon,   2),
                ("铁甲护腕", "护腕 · 40级",   InkWashTheme.InkQuality.Common,     3),
            };

            _equipItems = new ContainerControl[mockEquips.Length];
            float itemY = filterY + 60f;
            for (int i = 0; i < mockEquips.Length; i++)
            {
                var item = CreateEquipListItem(
                    12f, itemY,
                    mockEquips[i].name,
                    mockEquips[i].type,
                    mockEquips[i].quality,
                    mockEquips[i].enhance);
                _equipItems[i] = item;
                _leftPanel.AddChild(item);
                itemY += EquipItemHeight + EquipItemGap;
            }

            // 选中第 0 项
            ApplyEquipSelectionHighlight();

            AddChild(_leftPanel);
        }

        /// <summary>
        /// 创建一个装备列表项（图标占位 + 名称 + 类型 + 强化等级）。
        /// </summary>
        private ContainerControl CreateEquipListItem(
            float x, float y,
            string name, string typeText,
            InkWashTheme.InkQuality quality, int enhance)
        {
            var container = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(x, y),
                Size = new Float2(LeftColumnWidth - 24f, EquipItemHeight),
            };

            // 图标占位（小 InkCell）
            var iconCell = new InkCell
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(4f, 4f),
                Size = new Float2(40f, 40f),
                Quality = quality,
            };
            container.AddChild(iconCell);

            // 装备名
            var nameLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(52f, 4f),
                Size = new Float2(LeftColumnWidth - 24f - 52f - 40f, 20f),
                Text = name,
                TextColor = InkWashTheme.TextDefault,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            container.AddChild(nameLabel);

            // 装备类型 + 等级
            var typeLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(52f, 24f),
                Size = new Float2(LeftColumnWidth - 24f - 52f - 40f, 16f),
                Text = typeText,
                TextColor = InkWashTheme.TextTertiary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 10f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            container.AddChild(typeLabel);

            // 强化等级（右对齐）
            var enhanceLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopRight,
                Location = new Float2(0f, 8f),
                Size = new Float2(48f, 32f),
                Text = "+" + enhance,
                TextColor = enhance > 0 ? InkWashTheme.TextGold : InkWashTheme.TextSecondary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 14f),
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
            };
            container.AddChild(enhanceLabel);

            return container;
        }

        /// <summary>
        /// 构建中栏：装备预览 + 装备名 + 属性对比表 + 成功率 + 材料消耗 + 强化按钮。
        /// </summary>
        private void BuildMiddlePanel()
        {
            _middlePanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
            };

            // 装备预览区（顶部 120 高）
            _previewCell = new InkCell
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(20f, 16f),
                Size = new Float2(96f, 96f),
                Quality = InkWashTheme.InkQuality.Legendary,
            };
            _middlePanel.AddChild(_previewCell);

            _previewHintLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(20f, 116f),
                Size = new Float2(96f, 16f),
                Text = "装备预览",
                TextColor = InkWashTheme.TextTertiary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 10f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            _middlePanel.AddChild(_previewHintLabel);

            // 装备名 + 品质 + 强化等级 + 类型 + 等级
            _equipNameLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(140f, 24f),
                Size = new Float2(160f, 28f),
                Text = "玄铁重剑",
                TextColor = InkWashTheme.QualityTextColor(InkWashTheme.InkQuality.Legendary),
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 20f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _middlePanel.AddChild(_equipNameLabel);

            _qualityTagLabel = CreateTagLabel(140f, 56f, "传说", InkWashTheme.TextGold);
            _enhanceLevelLabel = CreateTagLabel(196f, 56f, "+12", InkWashTheme.TextBrand, 16f);
            _equipTypeTagLabel = CreateTagLabel(252f, 56f, "双手剑", InkWashTheme.TextSecondary);
            _equipLevelTagLabel = CreateTagLabel(308f, 56f, "60级", InkWashTheme.TextSecondary);
            _middlePanel.AddChild(_qualityTagLabel);
            _middlePanel.AddChild(_enhanceLevelLabel);
            _middlePanel.AddChild(_equipTypeTagLabel);
            _middlePanel.AddChild(_equipLevelTagLabel);

            // 属性对比小节标题
            _attrCompareTitle = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(20f, 148f),
                Size = new Float2(200f, 18f),
                Text = "◆ 属性对比  · 强化后预览",
                TextColor = InkWashTheme.TextSecondary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 12f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _middlePanel.AddChild(_attrCompareTitle);

            // 表头
            _attrTableHeader = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(20f, 170f),
                Size = new Float2(440f, 20f),
                Text = "属性                  当前        强化后      变化",
                TextColor = InkWashTheme.TextTertiary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 12f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _middlePanel.AddChild(_attrTableHeader);

            // 5 行属性对比数据
            var attrData = new (string name, string current, string next, string delta)[]
            {
                ("攻击力", "1245",   "1320",  "+75"),
                ("暴击率", "12.5%",  "13.8%", "+1.3%"),
                ("命中",   "320",    "340",   "+20"),
                ("穿透",   "85",     "92",    "+7"),
                ("会心",   "45",     "45",    "—"),
            };

            _attrNameLabels = new Label[5];
            _attrCurrentLabels = new Label[5];
            _attrNextLabels = new Label[5];
            _attrDeltaLabels = new Label[5];

            float rowY = 192f;
            for (int i = 0; i < attrData.Length; i++)
            {
                _attrNameLabels[i] = CreateAttrCell(20f, rowY, 140f, attrData[i].name, InkWashTheme.TextSecondary, false);
                _attrCurrentLabels[i] = CreateAttrCell(160f, rowY, 100f, attrData[i].current, InkWashTheme.TextDefault, false);
                _attrNextLabels[i] = CreateAttrCell(260f, rowY, 100f, attrData[i].next, InkWashTheme.TextBrand, false);
                Color deltaColor = attrData[i].delta == "—" ? InkWashTheme.TextTertiary : InkWashTheme.TextJade;
                _attrDeltaLabels[i] = CreateAttrCell(360f, rowY, 100f, attrData[i].delta, deltaColor, false);

                _middlePanel.AddChild(_attrNameLabels[i]);
                _middlePanel.AddChild(_attrCurrentLabels[i]);
                _middlePanel.AddChild(_attrNextLabels[i]);
                _middlePanel.AddChild(_attrDeltaLabels[i]);

                rowY += AttrRowHeight;
            }

            // 成功率区
            _successRateLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(20f, rowY + 12f),
                Size = new Float2(120f, 18f),
                Text = "成功率",
                TextColor = InkWashTheme.TextSecondary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 12f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _middlePanel.AddChild(_successRateLabel);

            _successRateValueLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopRight,
                Location = new Float2(0f, rowY + 12f),
                Size = new Float2(120f, 18f),
                Text = "78%",
                TextColor = InkWashTheme.TextBrand,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 16f),
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
            };
            _middlePanel.AddChild(_successRateValueLabel);

            _successRateBar = new InkBar
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(20f, rowY + 34f),
                Size = new Float2(440f, 8f),
                Value = 0.78f,
                FillVariant = InkBarFillVariant.Gold,
            };
            _middlePanel.AddChild(_successRateBar);

            _failWarnLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(20f, rowY + 46f),
                Size = new Float2(440f, 16f),
                Text = "⚠ 失败将降低 1 级强化等级，建议使用护身符",
                TextColor = InkWashTheme.TextBlood,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 10f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _middlePanel.AddChild(_failWarnLabel);

            // 材料消耗区
            _stoneCostLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(20f, rowY + 70f),
                Size = new Float2(200f, 36f),
                Text = "玄铁强化石  x5",
                TextColor = InkWashTheme.TextBrand,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _middlePanel.AddChild(_stoneCostLabel);

            _silverCostLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(240f, rowY + 70f),
                Size = new Float2(200f, 36f),
                Text = "银两  x50,000",
                TextColor = InkWashTheme.TextBrand,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _middlePanel.AddChild(_silverCostLabel);

            // 执行强化按钮（底部居中）
            _enhanceButton = new InkButton
            {
                Variant = InkButtonVariant.Primary,
                ButtonSize = InkButtonSize.Lg,
                Text = "执行强化",
                AnchorPreset = AnchorPresets.BottomLeft,
                Location = new Float2(20f, 0f),
                Size = new Float2(200f, 44f),
            };
            _enhanceButton.ButtonClicked += (b) => EmitGoldAtButton(b);
            _middlePanel.AddChild(_enhanceButton);

            AddChild(_middlePanel);
        }

        /// <summary>
        /// 构建右栏：所需材料 + 强化进度 + 套装效果 + 五行分布。
        /// </summary>
        private void BuildRightPanel()
        {
            _rightPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
            };

            float y = 12f;

            // "所需材料"小节
            _materialsTitle = CreateSectionLabel(12f, y, "所需材料");
            _rightPanel.AddChild(_materialsTitle);
            y += 24f;

            // 3 项材料
            string[] materialTexts =
            {
                "玄铁强化石  拥有 23 / 需要 5",
                "精炼砂      拥有 8 / 需要 3",
                "银两        拥有 128,000 / 需要 50,000",
            };
            _materialLabels = new Label[3];
            for (int i = 0; i < _materialLabels.Length; i++)
            {
                _materialLabels[i] = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(12f, y),
                    Size = new Float2(RightColumnWidth - 24f, 20f),
                    Text = "• " + materialTexts[i],
                    TextColor = InkWashTheme.TextDefault,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 11f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                _rightPanel.AddChild(_materialLabels[i]);
                y += 22f;
            }

            y += 12f;

            // "强化进度"小节
            _progressTitle = CreateSectionLabel(12f, y, "强化进度");
            _rightPanel.AddChild(_progressTitle);
            y += 24f;

            _progressCurrentLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, y),
                Size = new Float2(100f, 28f),
                Text = "+12",
                TextColor = InkWashTheme.TextBrand,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 22f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _rightPanel.AddChild(_progressCurrentLabel);

            _progressNextLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopRight,
                Location = new Float2(0f, y),
                Size = new Float2(100f, 28f),
                Text = "+13",
                TextColor = InkWashTheme.TextTertiary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 22f),
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
            };
            _rightPanel.AddChild(_progressNextLabel);
            y += 32f;

            _progressBar = new InkBar
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, y),
                Size = new Float2(RightColumnWidth - 24f, 6f),
                Value = 0.60f,
                FillVariant = InkBarFillVariant.Gold,
            };
            _rightPanel.AddChild(_progressBar);
            y += 10f;

            _progressMaxLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, y),
                Size = new Float2(RightColumnWidth - 24f, 14f),
                Text = "当前等级                      上限 +20",
                TextColor = InkWashTheme.TextTertiary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 10f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _rightPanel.AddChild(_progressMaxLabel);
            y += 24f;

            // "套装效果"小节
            _setTitle = CreateSectionLabel(12f, y, "套装效果");
            _rightPanel.AddChild(_setTitle);
            y += 24f;

            _setNameLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, y),
                Size = new Float2(160f, 20f),
                Text = "玄铁战意",
                TextColor = InkWashTheme.QualityTextColor(InkWashTheme.InkQuality.Legendary),
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _rightPanel.AddChild(_setNameLabel);

            _setCountLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopRight,
                Location = new Float2(0f, y),
                Size = new Float2(80f, 20f),
                Text = "2 / 4 件",
                TextColor = InkWashTheme.TextTertiary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 11f),
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
            };
            _rightPanel.AddChild(_setCountLabel);
            y += 22f;

            _setBonus2Label = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, y),
                Size = new Float2(RightColumnWidth - 24f, 16f),
                Text = "[2] 攻击力 +5%   ✓",
                TextColor = InkWashTheme.TextJade,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 11f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _rightPanel.AddChild(_setBonus2Label);
            y += 18f;

            _setBonus4Label = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, y),
                Size = new Float2(RightColumnWidth - 24f, 16f),
                Text = "[4] 暴击伤害 +20%   未激活",
                TextColor = InkWashTheme.TextTertiary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 11f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _rightPanel.AddChild(_setBonus4Label);
            y += 28f;

            // "五行分布"小节
            _elementTitle = CreateSectionLabel(12f, y, "五行分布");
            _rightPanel.AddChild(_elementTitle);
            y += 24f;

            // 5 个五行行
            string[] elementNames = { "金", "木", "水", "火", "土" };
            float[] elementPercents = { 0.35f, 0.15f, 0.20f, 0.20f, 0.10f };
            Color[] elementColors =
            {
                InkWashTheme.ElementMetal,
                InkWashTheme.ElementWood,
                InkWashTheme.ElementWater,
                InkWashTheme.ElementFire,
                InkWashTheme.ElementEarth,
            };

            _elementLabels = new Label[5];
            _elementBars = new InkBar[5];
            for (int i = 0; i < 5; i++)
            {
                _elementLabels[i] = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(12f, y),
                    Size = new Float2(200f, 16f),
                    Text = elementNames[i] + "  " + (elementPercents[i] * 100f).ToString("0") + "%",
                    TextColor = elementColors[i],
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 11f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                _rightPanel.AddChild(_elementLabels[i]);

                _elementBars[i] = new InkBar
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(12f, y + 16f),
                    Size = new Float2(RightColumnWidth - 24f, 4f),
                    Value = elementPercents[i],
                    FillVariant = InkBarFillVariant.Gold,
                };
                _rightPanel.AddChild(_elementBars[i]);
                y += 24f;
            }

            AddChild(_rightPanel);
        }

        /// <summary>
        /// 构建底部导航按钮栏：返回沉浸模式 + 跳转背包。
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

            _gotoInventoryButton = new InkButton
            {
                Variant = InkButtonVariant.Default,
                ButtonSize = InkButtonSize.Md,
                Text = "跳转背包",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(NavBtnWidth + NavBtnGap, 0f),
                Size = new Float2(NavBtnWidth, BottomNavHeight),
            };
            _gotoInventoryButton.ButtonClicked += (b) =>
                OnSystemNavButtonClicked(InkPageDomIds.NavInventory, b);
            _bottomNavPanel.AddChild(_gotoInventoryButton);

            AddChild(_bottomNavPanel);
        }

        // ===================================================================
        // 辅助构建方法
        // =======================================================================

        /// <summary>
        /// 创建一个小节标题（带"◆"前缀）。
        /// </summary>
        private Label CreateSectionLabel(float x, float y, string text)
        {
            return new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(x, y),
                Size = new Float2(200f, 18f),
                Text = "◆ " + text,
                TextColor = InkWashTheme.TextSecondary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 12f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
        }

        /// <summary>
        /// 创建一个标签徽章（带边框 + 半透明底色）。
        /// </summary>
        private Label CreateTagLabel(float x, float y, string text, Color textColor, float fontSize = 11f)
        {
            return new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(x, y),
                Size = new Float2(56f, 22f),
                Text = text,
                TextColor = textColor,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), fontSize),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                BackgroundColor = new Color(
                    InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G,
                    InkWashTheme.GoldPrimary.B, 0.10f),
            };
        }

        /// <summary>
        /// 创建属性对比表中的一个单元格。
        /// </summary>
        private Label CreateAttrCell(float x, float y, float w, string text, Color textColor, bool isHeader)
        {
            return new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(x, y),
                Size = new Float2(w, AttrRowHeight),
                Text = text,
                TextColor = textColor,
                Font = new FontReference(
                    InkWashTheme.GetFont(isHeader ? InkWashTheme.FontRole.Heading : InkWashTheme.FontRole.Number),
                    isHeader ? 12f : 13f),
                HorizontalAlignment = isHeader ? TextAlignment.Near : TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
            };
        }

        // ===================================================================
        // 事件处理
        // =======================================================================

        /// <summary>
        /// Tab 按钮点击处理：切换激活态并发射金粉粒子。
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
                FlaxEngine.Debug.LogError($"[EquipmentEnhancePage] Tab 切换失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 品质筛选按钮点击处理。
        /// </summary>
        private void OnQualityFilterClicked(int filterIndex, Button sourceButton)
        {
            try
            {
                EmitGoldAtButton(sourceButton);
                _activeQualityFilter = filterIndex;
                ApplyQualityFilterHighlight();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[EquipmentEnhancePage] 品质筛选切换失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 根据当前激活的 Tab 索引更新所有 Tab 按钮的视觉状态。
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
        /// 根据当前激活的品质筛选索引更新所有筛选按钮的视觉状态。
        /// </summary>
        private void ApplyQualityFilterHighlight()
        {
            if (_qualityFilterButtons == null)
                return;
            for (int i = 0; i < _qualityFilterButtons.Length; i++)
            {
                if (_qualityFilterButtons[i] == null)
                    continue;
                _qualityFilterButtons[i].TextColor = (i == _activeQualityFilter)
                    ? InkWashTheme.TextBrand
                    : InkWashTheme.TextSecondary;
            }
        }

        /// <summary>
        /// 根据当前选中的装备索引更新装备列表项的视觉状态（高亮选中项背景）。
        /// </summary>
        private void ApplyEquipSelectionHighlight()
        {
            if (_equipItems == null)
                return;
            for (int i = 0; i < _equipItems.Length; i++)
            {
                if (_equipItems[i] == null)
                    continue;
                _equipItems[i].BackgroundColor = (i == _selectedEquipIndex)
                    ? new Color(
                        InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G,
                        InkWashTheme.GoldPrimary.B, 0.12f)
                    : Color.Transparent;
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
                    $"[EquipmentEnhancePage] NavigationRequested({domId}) 触发失败: {ex.Message}");
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
                FlaxEngine.Debug.LogWarning($"[EquipmentEnhancePage] EmitGoldAtButton 失败: {ex.Message}");
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

                // 1. 顶部标题栏
                if (_headerPanel != null)
                {
                    _headerPanel.Location = new Float2(panelX, ScreenEdge);
                    _headerPanel.Size = new Float2(panelW, HeaderHeight);

                    // 提示信息靠右对齐
                    if (_hintLabel != null)
                    {
                        _hintLabel.Location = new Float2(panelW - 380f, 0f);
                        _hintLabel.Size = new Float2(360f, HeaderHeight);
                    }
                }

                // 2. Tab 栏
                float tabBarY = ScreenEdge + HeaderHeight + RegionGap;
                if (_tabBarPanel != null)
                {
                    _tabBarPanel.Location = new Float2(panelX, tabBarY);
                    _tabBarPanel.Size = new Float2(panelW, TabBarHeight);

                    // 警告标签靠右对齐
                    if (_warnLabel != null)
                    {
                        _warnLabel.Location = new Float2(panelW - 240f, 0f);
                        _warnLabel.Size = new Float2(220f, TabBarHeight);
                    }
                }

                // 3. 底部导航按钮栏
                float bottomNavY = h - ScreenEdge - BottomNavHeight;
                if (_bottomNavPanel != null)
                {
                    _bottomNavPanel.Location = new Float2(panelX, bottomNavY);
                    _bottomNavPanel.Size = new Float2(panelW, BottomNavHeight);
                }

                // 4. 内容区：Tab 栏下方 → 底部导航栏上方
                float contentTop = tabBarY + TabBarHeight + RegionGap;
                float contentBottom = bottomNavY - RegionGap;
                float contentH = contentBottom - contentTop;
                if (contentH < 100f)
                    contentH = 100f;

                float leftX = panelX;
                float middleX = leftX + LeftColumnWidth + ColumnGap;
                float rightX = middleX + (panelW - LeftColumnWidth - RightColumnWidth - ColumnGap * 2f) + ColumnGap;
                float middleW = panelW - LeftColumnWidth - RightColumnWidth - ColumnGap * 2f;

                // 5. 左栏
                if (_leftPanel != null)
                {
                    _leftPanel.Location = new Float2(leftX, contentTop);
                    _leftPanel.Size = new Float2(LeftColumnWidth, contentH);
                }

                // 6. 中栏
                if (_middlePanel != null)
                {
                    _middlePanel.Location = new Float2(middleX, contentTop);
                    _middlePanel.Size = new Float2(middleW, contentH);

                    // 执行强化按钮重新定位（底部居中）
                    if (_enhanceButton != null)
                    {
                        float btnX = (middleW - 200f) * 0.5f;
                        _enhanceButton.Location = new Float2(btnX, contentH - 52f);
                        _enhanceButton.Size = new Float2(200f, 44f);
                    }
                }

                // 7. 右栏
                if (_rightPanel != null)
                {
                    _rightPanel.Location = new Float2(rightX, contentTop);
                    _rightPanel.Size = new Float2(RightColumnWidth, contentH);
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[EquipmentEnhancePage] RefreshLayout 失败: {ex.Message}");
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

using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.StyleSystem;
using System;

namespace HundunWorld.Game.UI.Ink.Pages.Inventory
{
    /// <summary>
    /// 背包行囊页面 — 对应 inventory.html 设计原型。
    /// <para>
    /// 水墨古风物品管理界面，承担玩家在游戏世界中查看与管理所携物品的核心入口。
    /// 整体布局沿用 HTML 原型 1400×900 的三段式结构：
    /// <list type="bullet">
    ///   <item>顶部：标题"行囊" + 4 个分类 Tab（主背包/材料/任务/时装）+ 整理/仓库/关闭按钮</item>
    ///   <item>中部：左侧 8×6 物品网格（<see cref="InkCell"/>）+ 容量进度条；
    ///         右侧选中物品详情（图标/名称/品质徽章/meta/耐久条/基础与附加属性/套装效果/操作按钮）</item>
    ///   <item>底部：货币栏（铜币/银两/金锭/元宝）+ 导航按钮（返回沉浸模式/装备强化/角色面板）</item>
    /// </list>
    /// 通过 <see cref="NavigationRequested"/> 事件向路由器暴露导航请求，
    /// 关闭按钮与底部"返回沉浸模式"按钮均触发 <see cref="InkPageDomIds.CombatHud"/>。
    /// </para>
    /// <para>
    /// 当前实现全部使用 mock 数据；后续接入背包系统时，
    /// 通过 <see cref="RefreshGrid"/> 等公共方法替换网格内容即可。
    /// </para>
    /// </summary>
    public class InventoryPage : ContainerControl, IInkPage
    {
        // ===================================================================
        // 布局常量
        // =======================================================================

        /// <summary>顶部标题栏高度（像素）</summary>
        private const float HeaderHeight = 60f;

        /// <summary>底部货币栏高度（像素）</summary>
        private const float CurrencyHeight = 40f;

        /// <summary>底部导航按钮栏高度（像素）</summary>
        private const float BottomNavHeight = 36f;

        /// <summary>屏幕边距（像素）</summary>
        private const float ScreenEdge = 16f;

        /// <summary>区域间距（像素）</summary>
        private const float RegionGap = 12f;

        /// <summary>网格区域宽度占比（占内容区宽度）</summary>
        private const float GridWidthRatio = 0.58f;

        /// <summary>物品网格列数</summary>
        private const int GridColumns = 8;

        /// <summary>物品网格行数</summary>
        private const int GridRows = 6;

        /// <summary>物品格子尺寸（正方形，像素）</summary>
        private const float CellSize = 56f;

        /// <summary>物品格子间距（像素）</summary>
        private const float CellGap = 6f;

        /// <summary>导航按钮宽度（像素）</summary>
        private const float NavBtnWidth = 140f;

        /// <summary>导航按钮间距（像素）</summary>
        private const float NavBtnGap = 8f;

        /// <summary>顶部 Tab 按钮宽度（像素）</summary>
        private const float TabBtnWidth = 88f;

        /// <summary>顶部 Tab 按钮间距（像素）</summary>
        private const float TabBtnGap = 4f;

        /// <summary>顶部工具按钮宽度（整理/仓库/关闭，像素）</summary>
        private const float ToolBtnWidth = 72f;

        /// <summary>顶部工具按钮间距（像素）</summary>
        private const float ToolBtnGap = 6f;

        // ===================================================================
        // 子控件引用 — 顶部
        // =======================================================================

        /// <summary>顶部标题栏面板</summary>
        private InkPanel _headerPanel;

        /// <summary>页面标题"行囊"</summary>
        private Label _titleLabel;

        /// <summary>顶部 4 个分类 Tab 按钮（主背包/材料/任务/时装）</summary>
        private InkButton[] _tabButtons;

        /// <summary>当前激活的 Tab 索引</summary>
        private int _activeTabIndex = 0;

        /// <summary>"整理"按钮</summary>
        private InkButton _sortButton;

        /// <summary>"仓库"按钮</summary>
        private InkButton _warehouseButton;

        /// <summary>"关闭"按钮（关闭背包，返回 HUD）</summary>
        private InkButton _closeButton;

        // ===================================================================
        // 子控件引用 — 物品网格
        // =======================================================================

        /// <summary>左侧网格面板</summary>
        private InkPanel _gridPanel;

        /// <summary>8×6 = 48 个物品格子</summary>
        private InkCell[] _gridCells;

        /// <summary>容量文字标签（如"15/80"）</summary>
        private Label _capacityLabel;

        /// <summary>容量进度条</summary>
        private InkBar _capacityBar;

        // ===================================================================
        // 子控件引用 — 右侧详情面板
        // =======================================================================

        /// <summary>右侧详情面板</summary>
        private InkPanel _detailPanel;

        /// <summary>详情大图标占位（用 InkCell 显示品质边框）</summary>
        private InkCell _detailIconCell;

        /// <summary>物品名称标签</summary>
        private Label _detailNameLabel;

        /// <summary>品质徽章标签（如"传说"）</summary>
        private Label _qualityBadge;

        /// <summary>类型徽章标签（如"武器"）</summary>
        private Label _typeBadge;

        /// <summary>数量徽章标签</summary>
        private Label _qtyBadge;

        /// <summary>Meta 标签：强化等级</summary>
        private Label _metaEnhanceLabel;

        /// <summary>Meta 标签：五行</summary>
        private Label _metaElementLabel;

        /// <summary>Meta 标签：绑定</summary>
        private Label _metaBindLabel;

        /// <summary>Meta 标签：耐久数值</summary>
        private Label _metaDurabilityLabel;

        /// <summary>耐久进度条</summary>
        private InkBar _durabilityBar;

        /// <summary>基础属性区标题</summary>
        private Label _baseAttrTitle;

        /// <summary>基础属性：攻击力数值</summary>
        private Label _baseAttackLabel;

        /// <summary>基础属性：暴击率数值</summary>
        private Label _baseCritLabel;

        /// <summary>附加属性区标题</summary>
        private Label _extraAttrTitle;

        /// <summary>附加属性：会心率数值</summary>
        private Label _extraFocusLabel;

        /// <summary>套装效果区标题</summary>
        private Label _setTitle;

        /// <summary>套装名称标签</summary>
        private Label _setNameLabel;

        /// <summary>套装件数标签</summary>
        private Label _setCountLabel;

        /// <summary>套装描述标签</summary>
        private Label _setDescLabel;

        /// <summary>"使用"按钮</summary>
        private InkButton _useButton;

        /// <summary>"装备"按钮</summary>
        private InkButton _equipButton;

        /// <summary>"丢弃"按钮</summary>
        private InkButton _dropButton;

        // ===================================================================
        // 子控件引用 — 底部
        // =======================================================================

        /// <summary>底部货币栏面板</summary>
        private InkPanel _currencyPanel;

        /// <summary>铜币数值</summary>
        private Label _copperLabel;

        /// <summary>银两数值</summary>
        private Label _silverLabel;

        /// <summary>金锭数值</summary>
        private Label _goldLabel;

        /// <summary>元宝数值</summary>
        private Label _yuanbaoLabel;

        /// <summary>底部导航按钮面板</summary>
        private InkPanel _bottomNavPanel;

        /// <summary>"返回沉浸模式"按钮</summary>
        private InkButton _returnHudButton;

        /// <summary>"装备强化"按钮</summary>
        private InkButton _gotoEnhanceButton;

        /// <summary>"角色面板"按钮</summary>
        private InkButton _gotoCharButton;

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
        public InventoryPage()
        {
            try
            {
                AnchorPreset = AnchorPresets.StretchAll;
                Offsets = Margin.Zero;
                BackgroundColor = Color.Transparent;
                ClipChildren = false;
                AutoFocus = false;

                BuildHeader();
                BuildGrid();
                BuildDetailPanel();
                BuildCurrencyBar();
                BuildBottomNav();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[InventoryPage] 初始化失败: {ex.Message}");
            }
        }

        // ===================================================================
        // Build 方法
        // =======================================================================

        /// <summary>
        /// 构建顶部标题栏：标题 + 4 个分类 Tab + 整理/仓库/关闭按钮。
        /// </summary>
        private void BuildHeader()
        {
            _headerPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
            };

            // 页面标题"行囊"
            _titleLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(20f, 0f),
                Size = new Float2(80f, HeaderHeight),
                Text = "行囊",
                TextColor = InkWashTheme.TextGold,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 22f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _headerPanel.AddChild(_titleLabel);

            // 4 个分类 Tab
            _tabButtons = new InkButton[4];
            string[] tabNames = { "主背包", "材料背包", "任务背包", "时装背包" };
            float tabX = 120f;
            for (int i = 0; i < _tabButtons.Length; i++)
            {
                int capturedIndex = i;
                var btn = new InkButton
                {
                    Variant = InkButtonVariant.Ghost,
                    ButtonSize = InkButtonSize.Md,
                    Text = tabNames[i],
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(tabX, (HeaderHeight - 32f) * 0.5f),
                    Size = new Float2(TabBtnWidth, 32f),
                };
                btn.ButtonClicked += (b) => OnTabButtonClicked(capturedIndex, b);
                _headerPanel.AddChild(btn);
                _tabButtons[i] = btn;
                tabX += TabBtnWidth + TabBtnGap;
            }

            // 高亮初始 Tab 0
            ApplyTabHighlight();

            // 右侧工具按钮：整理/仓库/关闭（按钮在 RefreshLayout 中靠右定位）
            _sortButton = new InkButton
            {
                Variant = InkButtonVariant.Default,
                ButtonSize = InkButtonSize.Sm,
                Text = "整理",
                AnchorPreset = AnchorPresets.TopRight,
                Size = new Float2(ToolBtnWidth, 32f),
            };
            _sortButton.ButtonClicked += (b) => EmitGoldAtButton(b);
            _headerPanel.AddChild(_sortButton);

            _warehouseButton = new InkButton
            {
                Variant = InkButtonVariant.Default,
                ButtonSize = InkButtonSize.Sm,
                Text = "仓库",
                AnchorPreset = AnchorPresets.TopRight,
                Size = new Float2(ToolBtnWidth, 32f),
            };
            _warehouseButton.ButtonClicked += (b) => EmitGoldAtButton(b);
            _headerPanel.AddChild(_warehouseButton);

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
        /// 构建左侧物品网格：8×6 = 48 格 <see cref="InkCell"/> + 容量进度条。
        /// 使用 mock 数据填充前 15 格（武器/消耗品/材料/任务物品），其余为空格。
        /// </summary>
        private void BuildGrid()
        {
            _gridPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
            };

            // 物品网格（48 格）
            _gridCells = new InkCell[GridColumns * GridRows];

            // mock 物品数据：品质 + 数量
            var mockItems = new (InkWashTheme.InkQuality quality, string badge)[]
            {
                (InkWashTheme.InkQuality.Legendary, "+12"), // 玄铁剑
                (InkWashTheme.InkQuality.Epic,      "+8"),  // 寒铁刀
                (InkWashTheme.InkQuality.Rare,      "+5"),  // 精铁护腕
                (InkWashTheme.InkQuality.Common,    null),  // 布衣
                (InkWashTheme.InkQuality.Common,    null),  // 铁剑
                (InkWashTheme.InkQuality.Common,    null),  // 皮甲
                (InkWashTheme.InkQuality.Uncommon, "9"),    // 生命药水
                (InkWashTheme.InkQuality.Uncommon, "3"),    // 内力药水
                (InkWashTheme.InkQuality.Uncommon, "5"),    // 活血丹
                (InkWashTheme.InkQuality.Common,    "3"),    // 铁矿
                (InkWashTheme.InkQuality.Common,    "5"),    // 草药
                (InkWashTheme.InkQuality.Common,    "2"),    // 兽皮
                (InkWashTheme.InkQuality.Common,    "4"),    // 铁矿
                (InkWashTheme.InkQuality.Common,    "2"),    // 草药
                (InkWashTheme.InkQuality.Epic,      null),  // 密信（任务）
            };

            for (int i = 0; i < _gridCells.Length; i++)
            {
                var cell = new InkCell
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Size = new Float2(CellSize, CellSize),
                };

                // 填充 mock 数据
                if (i < mockItems.Length)
                {
                    cell.Quality = mockItems[i].quality;
                    if (!string.IsNullOrEmpty(mockItems[i].badge))
                    {
                        cell.Badge = mockItems[i].badge;
                    }
                }

                _gridCells[i] = cell;
                _gridPanel.AddChild(cell);
            }

            // 容量进度条 + 标签
            _capacityLabel = new Label
            {
                AnchorPreset = AnchorPresets.BottomLeft,
                Location = new Float2(0f, 0f),
                Size = new Float2(120f, 16f),
                Text = "格子  15 / 80",
                TextColor = InkWashTheme.TextSecondary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 12f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _gridPanel.AddChild(_capacityLabel);

            _capacityBar = new InkBar
            {
                AnchorPreset = AnchorPresets.BottomLeft,
                Location = new Float2(130f, 0f),
                Size = new Float2(200f, 6f),
                Value = 15f / 80f,
                FillVariant = InkBarFillVariant.Gold,
            };
            _gridPanel.AddChild(_capacityBar);

            AddChild(_gridPanel);
        }

        /// <summary>
        /// 构建右侧选中物品详情面板：图标 + 名称 + 品质徽章 + meta + 耐久条 +
        /// 基础属性 + 附加属性 + 套装效果 + 操作按钮。
        /// 默认显示 mock 选中物品"玄铁剑"（传说品质）。
        /// </summary>
        private void BuildDetailPanel()
        {
            _detailPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
            };

            // 物品大图标（80×80，传说品质边框）
            _detailIconCell = new InkCell
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(20f, 16f),
                Size = new Float2(80f, 80f),
                Quality = InkWashTheme.InkQuality.Legendary,
            };
            _detailPanel.AddChild(_detailIconCell);

            // 物品名称
            _detailNameLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(120f, 16f),
                Size = new Float2(260f, 28f),
                Text = "玄铁剑",
                TextColor = InkWashTheme.QualityTextColor(InkWashTheme.InkQuality.Legendary),
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 20f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _detailPanel.AddChild(_detailNameLabel);

            // 品质徽章 / 类型徽章 / 数量徽章
            _qualityBadge = CreateBadge(120f, 48f, "传说", InkWashTheme.TextGold, InkWashTheme.BorderGold);
            _typeBadge = CreateBadge(180f, 48f, "武器", InkWashTheme.TextSecondary, InkWashTheme.BorderNeutralL2);
            _qtyBadge = CreateBadge(232f, 48f, "数量 1", InkWashTheme.TextSecondary, InkWashTheme.BorderNeutralL2);
            _detailPanel.AddChild(_qualityBadge);
            _detailPanel.AddChild(_typeBadge);
            _detailPanel.AddChild(_qtyBadge);

            // Meta 网格：强化/五行/绑定/耐久（4 项，2×2）
            _metaEnhanceLabel = CreateMetaLabel(20f, 110f, "强化", "+12", InkWashTheme.TextGold);
            _metaElementLabel = CreateMetaLabel(180f, 110f, "五行", "金", InkWashTheme.ElementMetal);
            _metaBindLabel = CreateMetaLabel(20f, 140f, "绑定", "拾取绑定", InkWashTheme.TextSecondary);
            _metaDurabilityLabel = CreateMetaLabel(180f, 140f, "耐久", "850 / 1000", InkWashTheme.TextSecondary);
            _detailPanel.AddChild(_metaEnhanceLabel);
            _detailPanel.AddChild(_metaElementLabel);
            _detailPanel.AddChild(_metaBindLabel);
            _detailPanel.AddChild(_metaDurabilityLabel);

            // 耐久进度条
            _durabilityBar = new InkBar
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(20f, 170f),
                Size = new Float2(360f, 6f),
                Value = 0.85f,
                FillVariant = InkBarFillVariant.Gold,
            };
            _detailPanel.AddChild(_durabilityBar);

            // 基础属性
            _baseAttrTitle = CreateSectionTitle(20f, 188f, "基础属性");
            _detailPanel.AddChild(_baseAttrTitle);

            _baseAttackLabel = CreateAttrRow(20f, 210f, "攻击力", "+120", InkWashTheme.TextDefault);
            _baseCritLabel = CreateAttrRow(20f, 230f, "暴击率", "+5%", InkWashTheme.TextDefault);
            _detailPanel.AddChild(_baseAttackLabel);
            _detailPanel.AddChild(_baseCritLabel);

            // 附加属性
            _extraAttrTitle = CreateSectionTitle(20f, 258f, "附加属性");
            _detailPanel.AddChild(_extraAttrTitle);

            _extraFocusLabel = CreateAttrRow(20f, 280f, "会心率", "+3%", InkWashTheme.TextJade);
            _detailPanel.AddChild(_extraFocusLabel);

            // 套装效果
            _setTitle = CreateSectionTitle(20f, 308f, "套装效果");
            _setNameLabel = CreateLabel(20f, 330f, 100f, 18f, "玄铁套",
                InkWashTheme.TextGold, InkWashTheme.FontRole.Heading, 13f);
            _setCountLabel = CreateLabel(120f, 330f, 60f, 18f, "2 / 4",
                InkWashTheme.TextSecondary, InkWashTheme.FontRole.Number, 12f);
            _setDescLabel = CreateLabel(20f, 350f, 360f, 18f, "2 件套：攻击力 +10%",
                InkWashTheme.TextJade, InkWashTheme.FontRole.Body, 12f);
            _detailPanel.AddChild(_setTitle);
            _detailPanel.AddChild(_setNameLabel);
            _detailPanel.AddChild(_setCountLabel);
            _detailPanel.AddChild(_setDescLabel);

            // 操作按钮：使用/装备/丢弃（RefreshLayout 中水平等分）
            _useButton = new InkButton
            {
                Variant = InkButtonVariant.Primary,
                ButtonSize = InkButtonSize.Md,
                Text = "使用",
                AnchorPreset = AnchorPresets.BottomLeft,
                Location = new Float2(20f, 0f),
                Size = new Float2(110f, 36f),
            };
            _useButton.ButtonClicked += (b) => EmitGoldAtButton(b);
            _detailPanel.AddChild(_useButton);

            _equipButton = new InkButton
            {
                Variant = InkButtonVariant.Default,
                ButtonSize = InkButtonSize.Md,
                Text = "装备",
                AnchorPreset = AnchorPresets.BottomLeft,
                Location = new Float2(140f, 0f),
                Size = new Float2(110f, 36f),
            };
            _equipButton.ButtonClicked += (b) => EmitGoldAtButton(b);
            _detailPanel.AddChild(_equipButton);

            _dropButton = new InkButton
            {
                Variant = InkButtonVariant.Vermilion,
                ButtonSize = InkButtonSize.Md,
                Text = "丢弃",
                AnchorPreset = AnchorPresets.BottomLeft,
                Location = new Float2(260f, 0f),
                Size = new Float2(110f, 36f),
            };
            _dropButton.ButtonClicked += (b) => EmitGoldAtButton(b);
            _detailPanel.AddChild(_dropButton);

            AddChild(_detailPanel);
        }

        /// <summary>
        /// 构建底部货币栏：铜币/银两/金锭/元宝 4 项数值。
        /// </summary>
        private void BuildCurrencyBar()
        {
            _currencyPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.BottomLeft,
            };

            _copperLabel = CreateLabel(0f, 0f, 200f, CurrencyHeight, "铜币  12,450",
                InkWashTheme.Warning, InkWashTheme.FontRole.Number, 14f);
            _silverLabel = CreateLabel(200f, 0f, 200f, CurrencyHeight, "银两  3,200",
                InkWashTheme.TextSecondary, InkWashTheme.FontRole.Number, 14f);
            _goldLabel = CreateLabel(400f, 0f, 200f, CurrencyHeight, "金锭  85",
                InkWashTheme.TextGold, InkWashTheme.FontRole.Number, 14f);
            _yuanbaoLabel = CreateLabel(600f, 0f, 200f, CurrencyHeight, "元宝  12",
                InkWashTheme.TextBrand, InkWashTheme.FontRole.Number, 14f);

            _currencyPanel.AddChild(_copperLabel);
            _currencyPanel.AddChild(_silverLabel);
            _currencyPanel.AddChild(_goldLabel);
            _currencyPanel.AddChild(_yuanbaoLabel);

            AddChild(_currencyPanel);
        }

        /// <summary>
        /// 构建底部导航按钮栏：返回沉浸模式 / 装备强化 / 角色面板。
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

            _gotoEnhanceButton = new InkButton
            {
                Variant = InkButtonVariant.Default,
                ButtonSize = InkButtonSize.Md,
                Text = "装备强化",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(NavBtnWidth + NavBtnGap, 0f),
                Size = new Float2(NavBtnWidth, BottomNavHeight),
            };
            _gotoEnhanceButton.ButtonClicked += (b) =>
                OnSystemNavButtonClicked(InkPageDomIds.NavEquipmentEnhance, b);
            _bottomNavPanel.AddChild(_gotoEnhanceButton);

            _gotoCharButton = new InkButton
            {
                Variant = InkButtonVariant.Default,
                ButtonSize = InkButtonSize.Md,
                Text = "角色面板",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2((NavBtnWidth + NavBtnGap) * 2f, 0f),
                Size = new Float2(NavBtnWidth, BottomNavHeight),
            };
            _gotoCharButton.ButtonClicked += (b) =>
                OnSystemNavButtonClicked(InkPageDomIds.NavCharacterPanel, b);
            _bottomNavPanel.AddChild(_gotoCharButton);

            AddChild(_bottomNavPanel);
        }

        // ===================================================================
        // 辅助构建方法
        // =======================================================================

        /// <summary>
        /// 创建一个徽章标签（带边框的小标签，用于品质/类型/数量）。
        /// </summary>
        /// <param name="x">相对父控件的 X 坐标</param>
        /// <param name="y">相对父控件的 Y 坐标</param>
        /// <param name="text">标签文字</param>
        /// <param name="textColor">文字颜色</param>
        /// <param name="borderColor">边框颜色（仅用于判断显隐，实际由 Label 自身绘制）</param>
        /// <returns>已配置的 Label</returns>
        private Label CreateBadge(float x, float y, string text, Color textColor, Color borderColor)
        {
            return new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(x, y),
                Size = new Float2(50f, 22f),
                Text = text,
                TextColor = textColor,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 12f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                BackgroundColor = new Color(borderColor.R, borderColor.G, borderColor.B, 0.12f),
            };
        }

        /// <summary>
        /// 创建一个 meta 键值对标签（左侧标签 + 右侧数值，共占一行）。
        /// </summary>
        private Label CreateMetaLabel(float x, float y, string key, string value, Color valueColor)
        {
            var label = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(x, y),
                Size = new Float2(160f, 22f),
                Text = key + "：  " + value,
                TextColor = valueColor,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            return label;
        }

        /// <summary>
        /// 创建一个区域小节标题（左侧带金色竖线感）。
        /// </summary>
        private Label CreateSectionTitle(float x, float y, string text)
        {
            return new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(x, y),
                Size = new Float2(360f, 20f),
                Text = "◆ " + text,
                TextColor = InkWashTheme.TextBrand,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
        }

        /// <summary>
        /// 创建一个属性行（左标签 + 右数值）。
        /// </summary>
        private Label CreateAttrRow(float x, float y, string name, string value, Color valueColor)
        {
            return new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(x, y),
                Size = new Float2(360f, 18f),
                Text = name + "        " + value,
                TextColor = valueColor,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
        }

        /// <summary>
        /// 创建一个通用 Label。
        /// </summary>
        private Label CreateLabel(float x, float y, float w, float h, string text,
            Color textColor, InkWashTheme.FontRole role, float fontSize)
        {
            return new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(x, y),
                Size = new Float2(w, h),
                Text = text,
                TextColor = textColor,
                Font = new FontReference(InkWashTheme.GetFont(role), fontSize),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
        }

        // ===================================================================
        // 事件处理
        // =======================================================================

        /// <summary>
        /// 顶部 Tab 按钮点击处理：切换激活态并发射金粉粒子。
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
                FlaxEngine.Debug.LogError($"[InventoryPage] Tab 切换失败: {ex.Message}");
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
                    $"[InventoryPage] NavigationRequested({domId}) 触发失败: {ex.Message}");
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
                FlaxEngine.Debug.LogWarning($"[InventoryPage] EmitGoldAtButton 失败: {ex.Message}");
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

                    // 整理/仓库/关闭按钮靠右排列
                    float toolRightX = panelW - 8f;
                    if (_closeButton != null)
                    {
                        _closeButton.Location = new Float2(toolRightX - 32f, (HeaderHeight - 32f) * 0.5f);
                        toolRightX -= 32f + ToolBtnGap;
                    }
                    if (_warehouseButton != null)
                    {
                        _warehouseButton.Location = new Float2(toolRightX - ToolBtnWidth, (HeaderHeight - 32f) * 0.5f);
                        toolRightX -= ToolBtnWidth + ToolBtnGap;
                    }
                    if (_sortButton != null)
                    {
                        _sortButton.Location = new Float2(toolRightX - ToolBtnWidth, (HeaderHeight - 32f) * 0.5f);
                    }
                }

                // 2. 底部导航按钮栏：底部全宽
                float bottomNavY = h - ScreenEdge - BottomNavHeight;
                if (_bottomNavPanel != null)
                {
                    _bottomNavPanel.Location = new Float2(panelX, bottomNavY);
                    _bottomNavPanel.Size = new Float2(panelW, BottomNavHeight);
                }

                // 3. 货币栏：底部导航栏上方
                float currencyY = bottomNavY - RegionGap - CurrencyHeight;
                if (_currencyPanel != null)
                {
                    _currencyPanel.Location = new Float2(panelX, currencyY);
                    _currencyPanel.Size = new Float2(panelW, CurrencyHeight);

                    // 货币项按等分宽度排列
                    float currencyItemW = panelW / 4f;
                    if (_copperLabel != null)
                    {
                        _copperLabel.Location = new Float2(0f, 0f);
                        _copperLabel.Size = new Float2(currencyItemW, CurrencyHeight);
                    }
                    if (_silverLabel != null)
                    {
                        _silverLabel.Location = new Float2(currencyItemW, 0f);
                        _silverLabel.Size = new Float2(currencyItemW, CurrencyHeight);
                    }
                    if (_goldLabel != null)
                    {
                        _goldLabel.Location = new Float2(currencyItemW * 2f, 0f);
                        _goldLabel.Size = new Float2(currencyItemW, CurrencyHeight);
                    }
                    if (_yuanbaoLabel != null)
                    {
                        _yuanbaoLabel.Location = new Float2(currencyItemW * 3f, 0f);
                        _yuanbaoLabel.Size = new Float2(currencyItemW, CurrencyHeight);
                    }
                }

                // 4. 内容区：顶部标题栏下方 → 货币栏上方
                float contentTop = ScreenEdge + HeaderHeight + RegionGap;
                float contentBottom = currencyY - RegionGap;
                float contentH = contentBottom - contentTop;
                if (contentH < 100f)
                    contentH = 100f;

                float gridW = panelW * GridWidthRatio;
                float detailW = panelW - gridW - RegionGap;

                // 5. 左侧网格面板
                if (_gridPanel != null)
                {
                    _gridPanel.Location = new Float2(panelX, contentTop);
                    _gridPanel.Size = new Float2(gridW, contentH);

                    // 网格内子控件按相对坐标布局
                    float gridPadding = 16f;
                    float gridInnerW = gridW - gridPadding * 2f;
                    // 根据可用宽度计算实际格子尺寸（保持正方形）
                    float actualCellSize = (gridInnerW - CellGap * (GridColumns - 1)) / GridColumns;
                    if (actualCellSize < 32f)
                        actualCellSize = 32f;
                    float actualCellGap = CellGap;
                    float gridHeight = actualCellSize * GridRows + CellGap * (GridRows - 1);

                    // 居中网格
                    float gridStartX = gridPadding + (gridInnerW - (actualCellSize * GridColumns + actualCellGap * (GridColumns - 1))) * 0.5f;
                    float gridStartY = gridPadding;
                    if (_gridCells != null)
                    {
                        for (int i = 0; i < _gridCells.Length; i++)
                        {
                            if (_gridCells[i] == null)
                                continue;
                            int col = i % GridColumns;
                            int row = i / GridColumns;
                            _gridCells[i].Location = new Float2(
                                gridStartX + col * (actualCellSize + actualCellGap),
                                gridStartY + row * (actualCellSize + actualCellGap));
                            _gridCells[i].Size = new Float2(actualCellSize, actualCellSize);
                        }
                    }

                    // 容量标签与进度条放在网格下方
                    float capacityY = gridStartY + gridHeight + 12f;
                    if (_capacityLabel != null)
                    {
                        _capacityLabel.Location = new Float2(gridPadding, capacityY);
                    }
                    if (_capacityBar != null)
                    {
                        _capacityBar.Location = new Float2(gridPadding + 130f, capacityY + 5f);
                        _capacityBar.Size = new Float2(Mathf.Max(100f, gridW - gridPadding * 2f - 140f), 6f);
                    }
                }

                // 6. 右侧详情面板
                if (_detailPanel != null)
                {
                    _detailPanel.Location = new Float2(panelX + gridW + RegionGap, contentTop);
                    _detailPanel.Size = new Float2(detailW, contentH);

                    // 详情面板内操作按钮按宽度等分重新定位
                    float btnWidth = (detailW - 40f - 20f * 2f) / 3f;
                    if (btnWidth < 80f)
                        btnWidth = 80f;
                    float btnY = contentH - 44f;
                    if (_useButton != null)
                    {
                        _useButton.Location = new Float2(20f, btnY);
                        _useButton.Size = new Float2(btnWidth, 36f);
                    }
                    if (_equipButton != null)
                    {
                        _equipButton.Location = new Float2(20f + btnWidth + 10f, btnY);
                        _equipButton.Size = new Float2(btnWidth, 36f);
                    }
                    if (_dropButton != null)
                    {
                        _dropButton.Location = new Float2(20f + (btnWidth + 10f) * 2f, btnY);
                        _dropButton.Size = new Float2(btnWidth, 36f);
                    }
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[InventoryPage] RefreshLayout 失败: {ex.Message}");
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

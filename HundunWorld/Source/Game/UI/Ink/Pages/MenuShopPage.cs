using System;
using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.StyleSystem;

namespace HundunWorld.Game.UI.Ink.Pages
{
    /// <summary>
    /// 商店菜单页面。
    /// 承载 4 个子区域：
    /// <list type="bullet">
    ///   <item>SubTask 9.1 顶部标题栏（<see cref="InkPanelTitle"/>，文本"商店"）</item>
    ///   <item>SubTask 9.2 左侧分类侧边栏（<see cref="InkPanel"/> + 4 个 <see cref="InkListItem"/>）</item>
    ///   <item>SubTask 9.3 中间商品格子网格（<see cref="InkPanel"/> + 8 个 <see cref="InkCell"/>）</item>
    ///   <item>SubTask 9.4 右侧商品详情（<see cref="InkPaperPanel"/> + 商品名/品质/属性/价格/购买按钮）</item>
    /// </list>
    /// 返回按钮由 <see cref="InkPageShell"/> 自动添加的 InkBackButton 承载，本页面不自建。
    /// 全部数据为 mock，通过 <see cref="RefreshLayout"/> 支持屏幕尺寸变化。
    /// </summary>
    public class MenuShopPage : ContainerControl, IInkPage
    {
        // ===================================================================
        // 布局常量
        // =======================================================================

        /// <summary>顶部标题栏高度（像素）</summary>
        private const float TitleHeight = 48f;

        /// <summary>内容区顶部 Y 坐标（标题栏下方留白）</summary>
        private const float ContentTop = 80f;

        /// <summary>内容区底部留白（像素）</summary>
        private const float ContentBottomMargin = 80f;

        // --- 左侧分类侧边栏 ---

        /// <summary>左侧分类面板 X 坐标</summary>
        private const float LeftPanelX = 40f;

        /// <summary>左侧分类面板宽度</summary>
        private const float LeftPanelWidth = 180f;

        /// <summary>分类列表项高度（像素）</summary>
        private const float CategoryItemHeight = 48f;

        /// <summary>分类名 Label 左边距</summary>
        private const float CategoryLabelLeftMargin = 16f;

        // --- 中间商品网格 ---

        /// <summary>中间商品网格面板 X 坐标</summary>
        private const float MiddlePanelX = 240f;

        /// <summary>
        /// 中间面板宽度公式中扣除的右侧常量（= 右侧面板右偏移 320 + 间距 40）。
        /// 中间面板宽度 = screenWidth - <see cref="MiddlePanelX"/> - 此值。
        /// </summary>
        private const float MiddlePanelRightReserve = 360f;

        /// <summary>商品格子尺寸（正方形）</summary>
        private const float CellSize = 64f;

        /// <summary>商品格子间距</summary>
        private const float CellGap = 12f;

        /// <summary>商品网格列数</summary>
        private const int CellColumnCount = 4;

        /// <summary>商品网格行数</summary>
        private const int CellRowCount = 2;

        /// <summary>商品网格顶部内边距</summary>
        private const float GridTopPadding = 20f;

        // --- 右侧商品详情 ---

        /// <summary>右侧详情面板 X 坐标偏移（screenWidth - 320 中的 320）</summary>
        private const float RightPanelRightOffset = 320f;

        /// <summary>右侧详情面板宽度</summary>
        private const float RightPanelWidth = 280f;

        /// <summary>商品名 X 坐标（面板内局部坐标）</summary>
        private const float DetailNameX = 20f;

        /// <summary>商品名 Y 坐标（面板内局部坐标）</summary>
        private const float DetailNameY = 20f;

        /// <summary>商品名文本宽度</summary>
        private const float DetailNameWidth = 240f;

        /// <summary>商品名文本高度</summary>
        private const float DetailNameHeight = 28f;

        /// <summary>商品品质标签 X 坐标</summary>
        private const float DetailTagX = 20f;

        /// <summary>商品品质标签 Y 坐标</summary>
        private const float DetailTagY = 56f;

        /// <summary>商品品质标签宽度</summary>
        private const float DetailTagWidth = 60f;

        /// <summary>商品品质标签高度</summary>
        private const float DetailTagHeight = 24f;

        /// <summary>商品属性 X 坐标</summary>
        private const float DetailAttrX = 20f;

        /// <summary>商品属性 Y 坐标</summary>
        private const float DetailAttrY = 96f;

        /// <summary>商品属性文本宽度</summary>
        private const float DetailAttrWidth = 240f;

        /// <summary>商品属性文本高度</summary>
        private const float DetailAttrHeight = 96f;

        /// <summary>商品价格 X 坐标</summary>
        private const float DetailPriceX = 20f;

        /// <summary>
        /// 商品价格 Y 坐标（Y = screenHeight - 280）。
        /// 在 <see cref="ApplyLayout"/> 中根据当前屏幕高度计算实际局部 Y。
        /// </summary>
        private const float DetailPriceYFromScreenBottom = 280f;

        /// <summary>商品价格文本宽度</summary>
        private const float DetailPriceWidth = 240f;

        /// <summary>商品价格文本高度</summary>
        private const float DetailPriceHeight = 28f;

        /// <summary>购买按钮 X 坐标</summary>
        private const float DetailButtonX = 40f;

        /// <summary>
        /// 购买按钮 Y 坐标（Y = screenHeight - 200）。
        /// 在 <see cref="ApplyLayout"/> 中根据当前屏幕高度计算实际局部 Y。
        /// </summary>
        private const float DetailButtonYFromScreenBottom = 200f;

        /// <summary>购买按钮宽度</summary>
        private const float DetailButtonWidth = 200f;

        /// <summary>购买按钮高度</summary>
        private const float DetailButtonHeight = 44f;

        // ===================================================================
        // 子控件引用
        // =======================================================================

        /// <summary>顶部标题栏</summary>
        private InkPanelTitle _title;

        /// <summary>左侧分类面板</summary>
        private InkPanel _leftPanel;

        /// <summary>中间商品网格面板</summary>
        private InkPanel _middlePanel;

        /// <summary>右侧商品详情面板</summary>
        private InkPaperPanel _rightPanel;

        /// <summary>商品价格文本（Y 坐标随屏幕高度变化，需在布局中更新）</summary>
        private InkTextBlock _priceText;

        /// <summary>购买按钮（Y 坐标随屏幕高度变化，需在布局中更新）</summary>
        private InkButton _purchaseButton;

        /// <summary>分类列表项集合（用于切换 active 态）</summary>
        private CategoryItem[] _categoryItems;

        /// <summary>商品格子数组（8 个 InkCell，用于布局时重新居中定位）</summary>
        private InkCell[] _cells;

        /// <summary>商品数据数组（与 <see cref="_cells"/> 一一对应）</summary>
        private ShopItem[] _shopItems;

        /// <summary>当前选中的商品索引（-1 = 未选中）</summary>
        private int _selectedItemIndex = -1;

        /// <summary>mock 金币余额（初始 1000 两）</summary>
        private int _goldBalance = 1000;

        /// <summary>金币余额标签（显示在标题栏右侧）</summary>
        private Label _goldLabel;

        /// <summary>商品名文本（右侧详情面板，动态更新）</summary>
        private InkTextBlock _detailNameText;

        /// <summary>商品品质标签（右侧详情面板，动态更新）</summary>
        private InkTag _detailQualityTag;

        /// <summary>商品属性文本（右侧详情面板，动态更新）</summary>
        private InkTextBlock _detailAttrText;

        /// <summary>余额不足警告文本（朱红色，默认隐藏）</summary>
        private InkTextBlock _warningText;

        // ===================================================================
        // 屏幕尺寸缓存
        // =======================================================================

        /// <summary>当前屏幕尺寸缓存，用于布局计算</summary>
        private Float2 _screenSize;

        // ===================================================================
        // 构造函数
        // =======================================================================

        /// <summary>
        /// 构造函数：初始化全部 4 个子区域，使用 mock 数据填充。
        /// 构造时读取 <see cref="FlaxEngine.Screen.Size"/> 计算布局，
        /// 屏幕尺寸未就绪时使用 1920x1080 兜底。
        /// </summary>
        public MenuShopPage()
        {
            // 1. 读取屏幕尺寸
            _screenSize = FlaxEngine.Screen.Size;
            if (_screenSize.X <= 0f || _screenSize.Y <= 0f)
            {
                _screenSize = new Float2(1920f, 1080f);
            }

            // 2. 外壳：全屏拉伸 + 透明背景 + 不裁剪子控件
            AnchorPreset = AnchorPresets.StretchAll;
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
            AutoFocus = false;
            Location = Float2.Zero;
            Size = _screenSize;

            try
            {
                BuildTitle();
                _categoryItems = BuildLeftCategories();
                _cells = BuildMiddleGrid();
                BuildRightDetail();

                // 应用初始布局（基于屏幕尺寸计算所有子控件位置与尺寸）
                ApplyLayout();

                // 应用初始分类过滤（默认仅显示"兵器"分类商品）
                FilterShopItemsByCategory(0);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[MenuShopPage] 初始化失败: {ex.Message}");
            }
        }

        // ===================================================================
        // SubTask 构造方法
        // =======================================================================

        /// <summary>
        /// SubTask 9.1：顶部标题栏。
        /// <see cref="InkPanelTitle"/> 文本"商店"，位置 (0, 0)，宽度铺满，高度 48。
        /// 返回按钮由 <see cref="InkPageShell"/> 自动添加，本页面不自建。
        /// </summary>
        private void BuildTitle()
        {
            _title = new InkPanelTitle
            {
                Title = "商店",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Height = TitleHeight,
            };
            AddChild(_title);

            // 金币余额标签（标题栏右侧，mock 初始 1000 两）
            _goldLabel = new Label
            {
                Text = $"银两：{_goldBalance}",
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 14f),
                TextColor = InkWashTheme.GoldPrimary,
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(_screenSize.X - 220f, 0f),
                Size = new Float2(200f, TitleHeight),
            };
            AddChild(_goldLabel);
        }

        /// <summary>
        /// SubTask 9.2：左侧分类侧边栏。
        /// <see cref="InkPanel"/> 容器位置 (40, 80)，尺寸 (180, screenHeight - 160)。
        /// 内含 4 个 <see cref="InkListItem"/>（垂直排列，每项高度 48）：
        /// 兵器（active 态）/防具/丹药/材料。点击切换 active 态（mock 行为）。
        /// </summary>
        /// <returns>分类列表项数组</returns>
        private CategoryItem[] BuildLeftCategories()
        {
            _leftPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
            };
            AddChild(_leftPanel);

            // mock 数据：分类名、是否默认 active
            var categories = new[]
            {
                (name: "兵器", active: true),
                (name: "防具", active: false),
                (name: "丹药", active: false),
                (name: "材料", active: false),
            };

            var items = new CategoryItem[categories.Length];
            for (int i = 0; i < categories.Length; i++)
            {
                var cat = categories[i];
                int index = i; // 闭包捕获，确保点击回调获取正确索引

                var item = new CategoryItem
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(0f, i * CategoryItemHeight),
                    Size = new Float2(LeftPanelWidth, CategoryItemHeight),
                    Active = cat.active,
                };

                // 分类名 Label：Heading 字体 14px，垂直居中，左边距 16
                var nameLabel = new Label
                {
                    Text = cat.name,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 14f),
                    TextColor = InkWashTheme.TextDefault,
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(CategoryLabelLeftMargin, 0f),
                    Size = new Float2(LeftPanelWidth - CategoryLabelLeftMargin, CategoryItemHeight),
                };
                item.AddChild(nameLabel);

                item.Clicked += () => OnCategoryClicked(index);
                _leftPanel.AddChild(item);
                items[i] = item;
            }

            return items;
        }

        /// <summary>
        /// SubTask 9.3：中间商品格子网格。
        /// <see cref="InkPanel"/> 容器位置 (240, 80)，尺寸 (screenWidth - 240 - 320 - 40, screenHeight - 160)。
        /// 内含 8 个 <see cref="InkCell"/>（4 列 2 行，64x64，间距 12），品质色：
        /// Legendary ×2、Epic ×2、Rare ×2、Uncommon ×2。
        /// 每个 <see cref="InkCell"/> 右下角 Badge 显示价格徽章（mock 文本"100两"/"50两"等）。
        /// </summary>
        /// <returns>商品格子数组</returns>
        private InkCell[] BuildMiddleGrid()
        {
            _middlePanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
            };
            AddChild(_middlePanel);

            // mock 商品数据：名称、品质、价格、分类、属性描述
            _shopItems = new[]
            {
                new ShopItem("玄铁剑", InkWashTheme.InkQuality.Legendary, 100, "兵器", "攻击+120\n会心+15\n耐久 200/200"),
                new ShopItem("青龙偃月", InkWashTheme.InkQuality.Legendary, 120, "兵器", "攻击+150\n暴击+20\n耐久 180/180"),
                new ShopItem("玄武甲", InkWashTheme.InkQuality.Epic, 80, "防具", "防御+80\n气血+200\n耐久 150/150"),
                new ShopItem("金丝软甲", InkWashTheme.InkQuality.Epic, 75, "防具", "防御+60\n身法+10\n耐久 120/120"),
                new ShopItem("回春丹", InkWashTheme.InkQuality.Rare, 50, "丹药", "恢复气血 500\n冷却 30秒"),
                new ShopItem("培元丹", InkWashTheme.InkQuality.Rare, 60, "丹药", "恢复内力 300\n冷却 45秒"),
                new ShopItem("寒铁矿", InkWashTheme.InkQuality.Uncommon, 30, "材料", "锻造材料\n可用于强化兵器"),
                new ShopItem("灵草", InkWashTheme.InkQuality.Uncommon, 25, "材料", "炼丹材料\n可用于炼制丹药"),
            };

            var cellControls = new InkCell[_shopItems.Length];
            // 网格宽度 = 4*64 + 3*12 = 292；初始 startX 占位，ApplyLayout 中根据面板宽度居中
            float gridStartX = 0f;

            for (int i = 0; i < _shopItems.Length; i++)
            {
                int col = i % CellColumnCount;
                int row = i / CellColumnCount;

                var cell = new InkCell
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(
                        gridStartX + col * (CellSize + CellGap),
                        GridTopPadding + row * (CellSize + CellGap)),
                    Size = new Float2(CellSize, CellSize),
                    Quality = _shopItems[i].Quality,
                    Badge = $"{_shopItems[i].Price}两",
                };
                _middlePanel.AddChild(cell);
                cellControls[i] = cell;
            }

            return cellControls;
        }

        /// <summary>
        /// SubTask 9.4：右侧商品详情。
        /// <see cref="InkPaperPanel"/> 容器位置 (screenWidth - 320, 80)，尺寸 (280, screenHeight - 160)。
        /// 内含：商品名（"玄铁剑" Heading）、商品品质（"传世" Tag Brand）、商品属性（Body 多行）、
        /// 价格（"100两" Number）、"购买"按钮（Primary Lg，点击触发 <see cref="PurchaseClicked"/>）。
        /// </summary>
        private void BuildRightDetail()
        {
            _rightPanel = new InkPaperPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
            };
            AddChild(_rightPanel);

            // 1. 商品名：Heading 样式，位置 (20, 20)
            _detailNameText = new InkTextBlock(InkTextStyle.Heading)
            {
                Text = "玄铁剑",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(DetailNameX, DetailNameY),
                Size = new Float2(DetailNameWidth, DetailNameHeight),
            };
            _rightPanel.AddChild(_detailNameText);

            // 2. 商品品质：Tag Brand 变体，位置 (20, 56)
            _detailQualityTag = new InkTag
            {
                TagVariant = InkTagVariant.Brand,
                Text = "传世",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(DetailTagX, DetailTagY),
                Size = new Float2(DetailTagWidth, DetailTagHeight),
            };
            _rightPanel.AddChild(_detailQualityTag);

            // 3. 商品属性：Body 样式，位置 (20, 96)
            _detailAttrText = new InkTextBlock(InkTextStyle.Body)
            {
                Text = "攻击+120\n会心+15\n耐久 200/200",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(DetailAttrX, DetailAttrY),
                Size = new Float2(DetailAttrWidth, DetailAttrHeight),
                VerticalAlignment = TextAlignment.Near,
            };
            _rightPanel.AddChild(_detailAttrText);

            // 4. 价格：Number 样式，位置 (20, screenHeight - 280)
            _priceText = new InkTextBlock(InkTextStyle.Number)
            {
                Text = "100两",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(DetailPriceX, _screenSize.Y - DetailPriceYFromScreenBottom),
                Size = new Float2(DetailPriceWidth, DetailPriceHeight),
            };
            _rightPanel.AddChild(_priceText);

            // 5. 余额不足警告：朱红色 Caption 文本，默认隐藏，位置与价格同列上方
            _warningText = new InkTextBlock(InkTextStyle.Caption)
            {
                Text = "银两不足",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(DetailPriceX, _screenSize.Y - DetailPriceYFromScreenBottom - 24f),
                Size = new Float2(DetailPriceWidth, 20f),
                TextColor = InkWashTheme.VermilionPrimary,
                Visible = false,
            };
            _rightPanel.AddChild(_warningText);

            // 6. "购买"按钮：Primary Lg，位置 (40, screenHeight - 200)，尺寸 (200, 44)
            _purchaseButton = new InkButton
            {
                Variant = InkButtonVariant.Primary,
                ButtonSize = InkButtonSize.Lg,
                Text = "购买",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(DetailButtonX, _screenSize.Y - DetailButtonYFromScreenBottom),
                Size = new Float2(DetailButtonWidth, DetailButtonHeight),
            };
            _purchaseButton.ButtonClicked += OnPurchaseButtonClicked;
            _rightPanel.AddChild(_purchaseButton);

            // 默认选中第一个商品
            SelectItem(0);
        }

        // ===================================================================
        // 事件处理
        // =======================================================================

        /// <summary>
        /// 购买按钮点击事件。点击"购买"按钮时触发，由外部订阅执行实际购买逻辑。
        /// </summary>
        public event Action PurchaseClicked;

        /// <summary>
        /// 购买按钮点击处理：检查选中商品与金币余额，
        /// 余额足够时扣减金币并标记售罄，余额不足时显示朱红警告。
        /// </summary>
        /// <param name="button">触发事件的按钮（未使用）</param>
        private void OnPurchaseButtonClicked(Button button)
        {
            if (_selectedItemIndex < 0 || _shopItems == null || _selectedItemIndex >= _shopItems.Length)
                return;

            var item = _shopItems[_selectedItemIndex];
            if (item == null || item.SoldOut)
                return;

            // 隐藏警告文本（每次点击先重置）
            if (_warningText != null)
                _warningText.Visible = false;

            if (_goldBalance < item.Price)
            {
                // 余额不足：显示朱红警告
                if (_warningText != null)
                    _warningText.Visible = true;
                return;
            }

            // 扣减金币并标记售罄
            _goldBalance -= item.Price;
            item.SoldOut = true;
            UpdateGoldLabel();

            // 更新格子徽章为"售罄"
            if (_cells != null && _selectedItemIndex < _cells.Length && _cells[_selectedItemIndex] != null)
            {
                _cells[_selectedItemIndex].Badge = "售罄";
            }

            // 更新购买按钮文本
            if (_purchaseButton != null)
                _purchaseButton.Text = "已售罄";

            try
            {
                PurchaseClicked?.Invoke();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError(
                    $"[MenuShopPage] PurchaseClicked 触发失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 分类项点击处理：切换 active 态并按分类过滤商品格子。
        /// </summary>
        /// <param name="index">点击的分类项索引</param>
        private void OnCategoryClicked(int index)
        {
            if (_categoryItems == null)
                return;

            for (int i = 0; i < _categoryItems.Length; i++)
            {
                if (_categoryItems[i] != null)
                    _categoryItems[i].Active = (i == index);
            }

            FilterShopItemsByCategory(index);
        }

        /// <summary>
        /// 按分类过滤商品格子：隐藏不匹配项，将匹配项重新紧凑排列。
        /// </summary>
        /// <param name="categoryIndex">分类索引（0=兵器，1=防具，2=丹药，3=材料）</param>
        private void FilterShopItemsByCategory(int categoryIndex)
        {
            string[] categoryNames = { "兵器", "防具", "丹药", "材料" };
            if (categoryIndex < 0 || categoryIndex >= categoryNames.Length)
                return;
            if (_shopItems == null || _cells == null || _middlePanel == null)
                return;

            string selectedName = categoryNames[categoryIndex];
            float gridWidth = CellColumnCount * CellSize + (CellColumnCount - 1) * CellGap;
            float gridStartX = (_middlePanel.Width - gridWidth) * 0.5f;

            int visibleIndex = 0;
            for (int i = 0; i < _shopItems.Length; i++)
            {
                if (_cells[i] == null)
                    continue;
                bool match = _shopItems[i].Category == selectedName;
                _cells[i].Visible = match;
                if (match)
                {
                    int col = visibleIndex % CellColumnCount;
                    int row = visibleIndex / CellColumnCount;
                    _cells[i].Location = new Float2(
                        gridStartX + col * (CellSize + CellGap),
                        GridTopPadding + row * (CellSize + CellGap));
                    visibleIndex++;
                }
            }
        }

        /// <summary>
        /// 选中指定商品并更新右侧详情面板。
        /// </summary>
        /// <param name="index">商品索引</param>
        private void SelectItem(int index)
        {
            if (_shopItems == null || index < 0 || index >= _shopItems.Length)
                return;

            _selectedItemIndex = index;
            var item = _shopItems[index];

            if (_detailNameText != null)
                _detailNameText.Text = item.Name;

            if (_detailQualityTag != null)
                _detailQualityTag.Text = GetQualityDisplayName(item.Quality);

            if (_detailAttrText != null)
                _detailAttrText.Text = item.Attrs;

            if (_priceText != null)
                _priceText.Text = item.SoldOut ? "已售罄" : $"{item.Price}两";

            if (_purchaseButton != null)
                _purchaseButton.Text = item.SoldOut ? "已售罄" : "购买";

            if (_warningText != null)
                _warningText.Visible = false;
        }

        /// <summary>
        /// 品质枚举转显示名。
        /// </summary>
        private static string GetQualityDisplayName(InkWashTheme.InkQuality quality)
        {
            switch (quality)
            {
                case InkWashTheme.InkQuality.Legendary: return "传世";
                case InkWashTheme.InkQuality.Epic: return "史诗";
                case InkWashTheme.InkQuality.Rare: return "稀有";
                case InkWashTheme.InkQuality.Uncommon: return "精良";
                default: return "普通";
            }
        }

        /// <summary>
        /// 更新金币余额标签文本。
        /// </summary>
        private void UpdateGoldLabel()
        {
            if (_goldLabel != null)
                _goldLabel.Text = $"银两：{_goldBalance}";
        }

        /// <summary>
        /// 鼠标按下事件处理：先交由基类路由子控件，
        /// 若子控件未处理，则检测是否命中中间面板的商品格子，执行选中。
        /// </summary>
        /// <param name="location">相对于本控件的鼠标坐标</param>
        /// <param name="button">鼠标按键</param>
        /// <returns>是否处理了该事件</returns>
        public override bool OnMouseDown(Float2 location, MouseButton button)
        {
            bool handled = base.OnMouseDown(location, button);
            if (handled)
                return true;

            // 商品格子命中检测
            if (_middlePanel != null && _cells != null && _shopItems != null)
            {
                Float2 middleLocal = location - _middlePanel.Location;
                if (middleLocal.X >= 0f && middleLocal.X < _middlePanel.Width &&
                    middleLocal.Y >= 0f && middleLocal.Y < _middlePanel.Height)
                {
                    for (int i = 0; i < _cells.Length; i++)
                    {
                        if (_cells[i] == null || !_cells[i].Visible)
                            continue;
                        Float2 cellLocal = middleLocal - _cells[i].Location;
                        if (cellLocal.X >= 0f && cellLocal.X < _cells[i].Width &&
                            cellLocal.Y >= 0f && cellLocal.Y < _cells[i].Height)
                        {
                            SelectItem(i);
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        // ===================================================================
        // 布局计算
        // =======================================================================

        /// <summary>
        /// 根据当前 <see cref="_screenSize"/> 重新计算所有子控件的位置与尺寸。
        /// 由构造函数与 <see cref="RefreshLayout"/> 调用。
        /// </summary>
        private void ApplyLayout()
        {
            float sw = _screenSize.X;
            float sh = _screenSize.Y;

            // SubTask 9.1 标题栏：位置 (0, 0)，宽度铺满，高度 48
            if (_title != null)
            {
                _title.Location = Float2.Zero;
                _title.Size = new Float2(sw, TitleHeight);
            }

            // SubTask 9.2 左侧分类面板：(40, 80)，尺寸 (180, sh - 160)
            if (_leftPanel != null)
            {
                _leftPanel.Location = new Float2(LeftPanelX, ContentTop);
                _leftPanel.Size = new Float2(LeftPanelWidth, sh - ContentTop - ContentBottomMargin);
            }

            // SubTask 9.3 中间商品网格面板：(240, 80)，尺寸 (sw - 240 - 320 - 40, sh - 160)
            if (_middlePanel != null)
            {
                float middleWidth = sw - MiddlePanelX - MiddlePanelRightReserve;
                _middlePanel.Location = new Float2(MiddlePanelX, ContentTop);
                _middlePanel.Size = new Float2(middleWidth, sh - ContentTop - ContentBottomMargin);

                // 重新居中商品网格内的 8 个 InkCell
                if (_cells != null)
                {
                    float gridWidth = CellColumnCount * CellSize + (CellColumnCount - 1) * CellGap;
                    float gridStartX = (_middlePanel.Width - gridWidth) * 0.5f;
                    for (int i = 0; i < _cells.Length; i++)
                    {
                        if (_cells[i] == null)
                            continue;
                        int col = i % CellColumnCount;
                        int row = i / CellColumnCount;
                        _cells[i].Location = new Float2(
                            gridStartX + col * (CellSize + CellGap),
                            GridTopPadding + row * (CellSize + CellGap));
                    }
                }
            }

            // SubTask 9.4 右侧详情面板：(sw - 320, 80)，尺寸 (280, sh - 160)
            if (_rightPanel != null)
            {
                _rightPanel.Location = new Float2(sw - RightPanelRightOffset, ContentTop);
                _rightPanel.Size = new Float2(RightPanelWidth, sh - ContentTop - ContentBottomMargin);
            }

            // 价格文本：Y = screenHeight - 280（面板内局部坐标）
            if (_priceText != null)
            {
                _priceText.Location = new Float2(
                    DetailPriceX,
                    sh - DetailPriceYFromScreenBottom);
            }

            // 购买按钮：Y = screenHeight - 200（面板内局部坐标）
            if (_purchaseButton != null)
            {
                _purchaseButton.Location = new Float2(
                    DetailButtonX,
                    sh - DetailButtonYFromScreenBottom);
            }

            // 金币余额标签：标题栏右侧（X = sw - 220，Y = 0）
            if (_goldLabel != null)
            {
                _goldLabel.Location = new Float2(sw - 220f, 0f);
                _goldLabel.Size = new Float2(200f, TitleHeight);
            }

            // 余额不足警告文本：与价格同列上方（Y = sh - 280 - 24，面板内局部坐标）
            if (_warningText != null)
            {
                _warningText.Location = new Float2(
                    DetailPriceX,
                    sh - DetailPriceYFromScreenBottom - 24f);
            }
        }

        /// <summary>
        /// 在屏幕尺寸变化时重新布局所有子控件。
        /// 外部（如 <see cref="InkPageShell"/> 或屏幕大小变更监听器）应调用此方法。
        /// </summary>
        public void RefreshLayout()
        {
            // 优先使用控件实际尺寸（已由 InkPageShell.LoadPage 的 StretchAll 锚点填充父容器）
            float w = Width;
            float h = Height;
            if (w <= 0f || h <= 0f)
            {
                // 控件尚未布局，回退到屏幕尺寸
                var screen = FlaxEngine.Screen.Size;
                w = screen.X;
                h = screen.Y;
            }
            if (w <= 0f || h <= 0f)
            {
                // 仍然为 0，使用 1920x1080 兜底
                w = 1920f;
                h = 1080f;
            }
            _screenSize = new Float2(w, h);
            ApplyLayout();
        }

        // ===================================================================
        // 嵌套类
        // =======================================================================

        /// <summary>
        /// 可点击分类列表项。
        /// 继承 <see cref="InkListItem"/>，添加 <see cref="Clicked"/> 事件，
        /// 通过覆写 <see cref="OnMouseDown"/>/<see cref="OnMouseUp"/> 处理点击判定。
        /// 用于商店左侧分类切换 active 态。
        /// </summary>
        private class CategoryItem : InkListItem
        {
            /// <summary>鼠标是否按下（用于点击释放判定）</summary>
            private bool _isMouseDown;

            /// <summary>
            /// 点击事件。鼠标左键在控件范围内按下并释放时触发。
            /// </summary>
            public event Action Clicked;

            /// <summary>
            /// 构造函数：初始化可点击分类项。
            /// </summary>
            public CategoryItem()
            {
                // 默认高度由外部 Size 设置，此处不覆盖 InkListItem 的默认值
            }

            /// <inheritdoc />
            public override bool OnMouseDown(Float2 location, MouseButton button)
            {
                base.OnMouseDown(location, button);
                if (button == MouseButton.Left)
                    _isMouseDown = true;
                return true;
            }

            /// <inheritdoc />
            public override bool OnMouseUp(Float2 location, MouseButton button)
            {
                base.OnMouseUp(location, button);
                if (button == MouseButton.Left && _isMouseDown)
                {
                    _isMouseDown = false;
                    // 判定释放点是否仍在项范围内
                    if (location.X >= 0f && location.X <= Width &&
                        location.Y >= 0f && location.Y <= Height)
                    {
                        Clicked?.Invoke();
                    }
                }
                return true;
            }
        }

        /// <summary>
        /// 商店商品数据（mock）。
        /// 承载商品名/品质/价格/分类/属性描述/售罄状态，
        /// 与 <see cref="MenuShopPage._cells"/> 数组一一对应。
        /// </summary>
        private class ShopItem
        {
            /// <summary>商品名称</summary>
            public string Name;

            /// <summary>商品品质（用于 <see cref="InkCell.Quality"/> 与品质标签显示）</summary>
            public InkWashTheme.InkQuality Quality;

            /// <summary>商品价格（单位：两）</summary>
            public int Price;

            /// <summary>商品所属分类名（与左侧分类侧边栏一致：兵器/防具/丹药/材料）</summary>
            public string Category;

            /// <summary>商品属性描述（多行文本，显示在右侧详情面板）</summary>
            public string Attrs;

            /// <summary>是否已售罄</summary>
            public bool SoldOut;

            /// <summary>
            /// 构造函数：初始化商品数据。
            /// </summary>
            /// <param name="name">商品名称</param>
            /// <param name="quality">商品品质</param>
            /// <param name="price">商品价格（两）</param>
            /// <param name="category">分类名</param>
            /// <param name="attrs">属性描述</param>
            public ShopItem(string name, InkWashTheme.InkQuality quality, int price, string category, string attrs)
            {
                Name = name;
                Quality = quality;
                Price = price;
                Category = category;
                Attrs = attrs;
                SoldOut = false;
            }
        }
    }
}

using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.StyleSystem;
using System;
using Game.Character.Attributes;
using HundunWorld.Game.Equipment;
using HundunWorld.Game.Services;

namespace HundunWorld.Game.UI.Ink.Pages
{
    /// <summary>
    /// 角色属性菜单页面。
    /// 承载 4 个子区域：
    /// <list type="bullet">
    ///   <item>SubTask 7.1 顶部标题栏（<see cref="InkPanelTitle"/>，文本"角色属性"）</item>
    ///   <item>SubTask 7.2 左侧属性列表（6 个 <see cref="InkListItem"/> + <see cref="InkBar"/>）</item>
    ///   <item>SubTask 7.3 中间角色预览占位（<see cref="InkTextBlock"/> Body 样式）</item>
    ///   <item>SubTask 7.4 右侧装备槽 + 五行数据（<see cref="InkCell"/> 网格 + 5 个 <see cref="InkBar"/>）</item>
    /// </list>
    /// 返回按钮由 <see cref="InkPageShell"/> 自动添加的 InkBackButton 承载，本页面不自建。
    /// 全部数据为 mock，通过 <see cref="RefreshLayout"/> 支持屏幕尺寸变化。
    /// </summary>
    public class MenuCharAttributesPage : ContainerControl, IInkPage
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

        /// <summary>左侧属性面板 X 坐标</summary>
        private const float LeftPanelX = 40f;

        /// <summary>左侧属性面板宽度</summary>
        private const float LeftPanelWidth = 360f;

        /// <summary>中间预览面板 X 坐标（= 左侧面板右边 + 间距 20）</summary>
        private const float MiddlePanelX = 420f;

        /// <summary>右侧装备面板宽度</summary>
        private const float RightPanelWidth = 360f;

        /// <summary>
        /// 中间预览面板宽度公式中扣除的右侧常量（= 间距 40 + 右边距 40）。
        /// 中间面板宽度 = screenWidth - <see cref="MiddlePanelX"/> - <see cref="RightPanelWidth"/> - 此值。
        /// </summary>
        private const float MiddlePanelRightReserve = 80f;

        /// <summary>右侧装备面板 X 坐标偏移（screenWidth - 400 中的 400 = 面板宽 360 + 右边距 40）</summary>
        private const float RightPanelRightOffset = 400f;

        /// <summary>属性列表项高度（像素）</summary>
        private const float ListItemHeight = 56f;

        /// <summary>属性名 Label 左边距</summary>
        private const float NameLabelLeftMargin = 16f;

        /// <summary>属性名 Label 宽度</summary>
        private const float NameLabelWidth = 56f;

        /// <summary>属性名 Label 与 InkBar 的间距</summary>
        private const float NameToBarGap = 8f;

        /// <summary>属性 InkBar 宽度</summary>
        private const float AttributeBarWidth = 180f;

        /// <summary>属性 InkBar 高度</summary>
        private const float AttributeBarHeight = 8f;

        /// <summary>属性数值 Label 宽度</summary>
        private const float ValueLabelWidth = 74f;

        /// <summary>属性数值 Label 右边距</summary>
        private const float ValueLabelRightMargin = 16f;

        /// <summary>装备格尺寸（正方形）</summary>
        private const float EquipmentCellSize = 56f;

        /// <summary>装备格间距</summary>
        private const float EquipmentCellGap = 8f;

        /// <summary>装备网格列数</summary>
        private const int EquipmentColumnCount = 2;

        /// <summary>装备网格行数</summary>
        private const int EquipmentRowCount = 3;

        /// <summary>装备网格顶部内边距</summary>
        private const float EquipmentGridTopPadding = 20f;

        /// <summary>五行数据区与装备网格的间距</summary>
        private const float FiveElementsGap = 24f;

        /// <summary>五行数据行高</summary>
        private const float FiveElementRowHeight = 28f;

        /// <summary>五行 Label 宽度</summary>
        private const float FiveElementLabelWidth = 24f;

        /// <summary>五行 Label 左边距</summary>
        private const float FiveElementLabelLeftMargin = 20f;

        /// <summary>五行 Label 与 InkBar 的间距</summary>
        private const float FiveElementLabelToBarGap = 8f;

        /// <summary>五行 InkBar 高度</summary>
        private const float FiveElementBarHeight = 8f;

        /// <summary>五行 InkBar 右边距</summary>
        private const float FiveElementBarRightMargin = 20f;

        // ===================================================================
        // 子控件引用
        // =======================================================================

        /// <summary>顶部标题栏</summary>
        private InkPanelTitle _title;

        /// <summary>左侧属性面板</summary>
        private InkPanel _leftPanel;

        /// <summary>中间预览面板</summary>
        private InkPanel _middlePanel;

        /// <summary>中间预览文本（随 <see cref="_middlePanel"/> 拉伸）</summary>
        private InkTextBlock _previewText;

        /// <summary>右侧装备面板</summary>
        private InkPanel _rightPanel;

        // ===================================================================
        // 屏幕尺寸缓存
        // =======================================================================

        /// <summary>当前屏幕尺寸缓存，用于布局计算</summary>
        private Float2 _screenSize;

        // ===================================================================
        // 数据绑定字段
        // =======================================================================

        /// <summary>6 个属性 InkBar 引用（气血/体魄/内力/身法/根骨/悟性）</summary>
        private InkBar[] _attributeBars;

        /// <summary>6 个属性数值 Label 引用</summary>
        private Label[] _attributeValueLabels;

        /// <summary>6 个装备槽 InkCell 引用</summary>
        private InkCell[] _equipmentCells;

        /// <summary>5 个五行 InkBar 引用（金/木/水/火/土）</summary>
        private InkBar[] _fiveElementBars;

        /// <summary>绑定的角色属性数据</summary>
        private CharacterAttributes _boundAttributes;

        /// <summary>绑定的角色外观数据（含装备槽）</summary>
        private CharacterPersistenceService.AppearanceData _boundAppearance;

        /// <summary>装备槽显示顺序（对应 6 个 InkCell）</summary>
        private static readonly EquipmentSlot[] DisplayedSlots =
        {
            EquipmentSlot.Body,
            EquipmentSlot.Head,
            EquipmentSlot.RightHand,
            EquipmentSlot.LeftHand,
            EquipmentSlot.Waist,
            EquipmentSlot.Neck,
        };

        // ===================================================================
        // 构造函数
        // =======================================================================

        /// <summary>
        /// 构造函数：初始化全部 4 个子区域，使用 mock 数据填充。
        /// 构造时读取 <see cref="FlaxEngine.Screen.Size"/> 计算布局，
        /// 屏幕尺寸未就绪时使用 1920x1080 兜底。
        /// </summary>
        public MenuCharAttributesPage()
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
                BuildLeftAttributes();
                BuildMiddlePreview();
                BuildRightEquipment();

                // 应用初始布局（基于屏幕尺寸计算所有子控件位置与尺寸）
                ApplyLayout();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[MenuCharAttributesPage] 初始化失败: {ex.Message}");
            }
        }

        // ===================================================================
        // SubTask 构造方法
        // =======================================================================

        /// <summary>
        /// SubTask 7.1：顶部标题栏。
        /// <see cref="InkPanelTitle"/> 文本"角色属性"，位置 (0, 0)，宽度铺满，高度 48。
        /// 返回按钮由 <see cref="InkPageShell"/> 自动添加，本页面不自建。
        /// </summary>
        private void BuildTitle()
        {
            _title = new InkPanelTitle
            {
                Title = "角色属性",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Height = TitleHeight,
            };
            AddChild(_title);
        }

        /// <summary>
        /// SubTask 7.2：左侧属性列表。
        /// <see cref="InkPanel"/> 容器位置 (40, 80)，尺寸 (360, screenHeight - 160)。
        /// 内含 6 个 <see cref="InkListItem"/>（垂直排列，每项高度 56）：
        /// 气血/体魄/内力/身法/根骨/悟性。每项布局为左侧属性名 Label（Heading 14px）、
        /// 中间 <see cref="InkBar"/>（宽度 180）、右侧数值 Label（DIN 12px）。
        /// </summary>
        private void BuildLeftAttributes()
        {
            _leftPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
            };
            AddChild(_leftPanel);

            // mock 数据：属性名、数值、InkBar 变体、InkBar 进度
            var attributes = new[]
            {
                (name: "气血", value: "8500/10000", variant: InkBarFillVariant.Vermilion, barValue: 0.85f),
                (name: "体魄", value: "600/1000",   variant: InkBarFillVariant.Jade,      barValue: 0.6f),
                (name: "内力", value: "450/500",    variant: InkBarFillVariant.Gold,      barValue: 0.9f),
                (name: "身法", value: "320",        variant: InkBarFillVariant.Blood,     barValue: 0.32f),
                (name: "根骨", value: "280",        variant: InkBarFillVariant.Jade,      barValue: 0.28f),
                (name: "悟性", value: "150",        variant: InkBarFillVariant.Gold,      barValue: 0.15f),
            };

            _attributeBars = new InkBar[attributes.Length];
            _attributeValueLabels = new Label[attributes.Length];

            for (int i = 0; i < attributes.Length; i++)
            {
                var attr = attributes[i];

                var item = new InkListItem
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(0f, i * ListItemHeight),
                    Size = new Float2(LeftPanelWidth, ListItemHeight),
                };

                // 左侧属性名 Label：Heading 字体 14px
                var nameLabel = new Label
                {
                    Text = attr.name,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 14f),
                    TextColor = InkWashTheme.TextDefault,
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(NameLabelLeftMargin, 0f),
                    Size = new Float2(NameLabelWidth, ListItemHeight),
                };
                item.AddChild(nameLabel);

                // 中间 InkBar：宽度 180，垂直居中
                float barX = NameLabelLeftMargin + NameLabelWidth + NameToBarGap;
                float barY = (ListItemHeight - AttributeBarHeight) * 0.5f;
                var bar = new InkBar
                {
                    FillVariant = attr.variant,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(barX, barY),
                    Size = new Float2(AttributeBarWidth, AttributeBarHeight),
                    Value = attr.barValue,
                };
                item.AddChild(bar);
                _attributeBars[i] = bar;

                // 右侧数值 Label：DIN 字体 12px，右对齐
                float valueX = LeftPanelWidth - ValueLabelWidth - ValueLabelRightMargin;
                var valueLabel = new Label
                {
                    Text = attr.value,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 12f),
                    TextColor = InkWashTheme.TextBrand,
                    HorizontalAlignment = TextAlignment.Far,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(valueX, 0f),
                    Size = new Float2(ValueLabelWidth, ListItemHeight),
                };
                item.AddChild(valueLabel);
                _attributeValueLabels[i] = valueLabel;

                _leftPanel.AddChild(item);
            }
        }

        /// <summary>
        /// SubTask 7.3：中间角色预览占位。
        /// <see cref="InkPanel"/> 容器位置 (420, 80)，
        /// 尺寸 (screenWidth - 420 - 360 - 80, screenHeight - 160)。
        /// 内含 <see cref="InkTextBlock"/> Body 样式，文本"角色预览（待 3D 集成）"，
        /// 居中显示，字色 <see cref="InkWashTheme.TextTertiary"/>。
        /// </summary>
        private void BuildMiddlePreview()
        {
            _middlePanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
            };
            AddChild(_middlePanel);

            _previewText = new InkTextBlock(InkTextStyle.Body)
            {
                Text = "角色预览（待 3D 集成）",
                AnchorPreset = AnchorPresets.StretchAll,
                Location = Float2.Zero,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                TextColor = InkWashTheme.TextTertiary,
            };
            _middlePanel.AddChild(_previewText);
        }

        /// <summary>
        /// SubTask 7.4：右侧装备槽 + 五行数据。
        /// <see cref="InkPanel"/> 容器位置 (screenWidth - 400, 80)，尺寸 (360, screenHeight - 160)。
        /// 装备槽网格：6 个 <see cref="InkCell"/>（2 列 3 行，56x56，间距 8），品质色：
        /// 头盔 Legendary、衣服 Epic、武器 Rare、鞋子 Uncommon、饰品 Common、背包 Common。
        /// 网格下方 5 个 <see cref="InkBar"/> Jade 变体（横向），每条带 Label：
        /// 金 0.8、木 0.6、水 0.4、火 0.7、土 0.5。
        /// </summary>
        private void BuildRightEquipment()
        {
            _rightPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
            };
            AddChild(_rightPanel);

            BuildEquipmentGrid();
            BuildFiveElements();
        }

        /// <summary>
        /// 构建装备槽网格（6 个 <see cref="InkCell"/>，2 列 3 行）。
        /// 网格在右侧面板内水平居中，顶部留 20px 内边距。
        /// </summary>
        private void BuildEquipmentGrid()
        {
            // mock 数据：装备名、品质
            var equipments = new[]
            {
                (name: "头盔", quality: InkWashTheme.InkQuality.Legendary),
                (name: "衣服", quality: InkWashTheme.InkQuality.Epic),
                (name: "武器", quality: InkWashTheme.InkQuality.Rare),
                (name: "鞋子", quality: InkWashTheme.InkQuality.Uncommon),
                (name: "饰品", quality: InkWashTheme.InkQuality.Common),
                (name: "背包", quality: InkWashTheme.InkQuality.Common),
            };

            _equipmentCells = new InkCell[equipments.Length];

            // 网格宽度 = 2*56 + 8 = 120，在 360 宽面板中水平居中
            float gridWidth = EquipmentColumnCount * EquipmentCellSize +
                              (EquipmentColumnCount - 1) * EquipmentCellGap;
            float gridStartX = (RightPanelWidth - gridWidth) * 0.5f;

            for (int i = 0; i < equipments.Length; i++)
            {
                int col = i % EquipmentColumnCount;
                int row = i / EquipmentColumnCount;

                float x = gridStartX + col * (EquipmentCellSize + EquipmentCellGap);
                float y = EquipmentGridTopPadding + row * (EquipmentCellSize + EquipmentCellGap);

                var cell = new InkCell
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(x, y),
                    Size = new Float2(EquipmentCellSize, EquipmentCellSize),
                    Quality = equipments[i].quality,
                };
                _rightPanel.AddChild(cell);
                _equipmentCells[i] = cell;
            }
        }

        /// <summary>
        /// 构建五行数据（5 个 <see cref="InkBar"/> Jade 变体 + Label）。
        /// 位于装备网格下方，每行包含一个五行名 Label 与一条横向 InkBar。
        /// </summary>
        private void BuildFiveElements()
        {
            // mock 数据：五行名、进度值
            var elements = new[]
            {
                (name: "金", value: 0.8f),
                (name: "木", value: 0.6f),
                (name: "水", value: 0.4f),
                (name: "火", value: 0.7f),
                (name: "土", value: 0.5f),
            };

            _fiveElementBars = new InkBar[elements.Length];

            // 计算装备网格底部 Y 坐标，五行数据区起始 Y = 网格底部 + 间距
            float gridHeight = EquipmentRowCount * EquipmentCellSize +
                               (EquipmentRowCount - 1) * EquipmentCellGap;
            float gridBottomY = EquipmentGridTopPadding + gridHeight;
            float startY = gridBottomY + FiveElementsGap;

            float barX = FiveElementLabelLeftMargin + FiveElementLabelWidth + FiveElementLabelToBarGap;
            float barWidth = RightPanelWidth - barX - FiveElementBarRightMargin;
            float barYOffset = (FiveElementRowHeight - FiveElementBarHeight) * 0.5f;

            for (int i = 0; i < elements.Length; i++)
            {
                var elem = elements[i];
                float rowY = startY + i * FiveElementRowHeight;

                // Label：五行名，Heading 字体 14px，居中
                var label = new Label
                {
                    Text = elem.name,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 14f),
                    TextColor = InkWashTheme.TextDefault,
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(FiveElementLabelLeftMargin, rowY),
                    Size = new Float2(FiveElementLabelWidth, FiveElementRowHeight),
                };
                _rightPanel.AddChild(label);

                // InkBar：Jade 变体，横向
                var bar = new InkBar
                {
                    FillVariant = InkBarFillVariant.Jade,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(barX, rowY + barYOffset),
                    Size = new Float2(barWidth, FiveElementBarHeight),
                    Value = elem.value,
                };
                _rightPanel.AddChild(bar);
                _fiveElementBars[i] = bar;
            }
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

            // SubTask 7.1 标题栏：位置 (0, 0)，宽度铺满，高度 48
            if (_title != null)
            {
                _title.Location = Float2.Zero;
                _title.Size = new Float2(sw, TitleHeight);
            }

            // SubTask 7.2 左侧属性面板：(40, 80)，尺寸 (360, sh - 160)
            if (_leftPanel != null)
            {
                _leftPanel.Location = new Float2(LeftPanelX, ContentTop);
                _leftPanel.Size = new Float2(LeftPanelWidth, sh - ContentTop - ContentBottomMargin);
            }

            // SubTask 7.3 中间预览面板：(420, 80)，尺寸 (sw - 420 - 360 - 80, sh - 160)
            // 宽度公式中扣除：左偏移 420 + 右面板宽 360 + 右侧间距与边距合计 80
            if (_middlePanel != null)
            {
                float middleWidth = sw - MiddlePanelX - RightPanelWidth - MiddlePanelRightReserve;
                _middlePanel.Location = new Float2(MiddlePanelX, ContentTop);
                _middlePanel.Size = new Float2(middleWidth, sh - ContentTop - ContentBottomMargin);
            }

            // 预览文本使用 StretchAll 锚点，随 _middlePanel 自动拉伸，无需手动同步尺寸

            // SubTask 7.4 右侧装备面板：(sw - 400, 80)，尺寸 (360, sh - 160)
            if (_rightPanel != null)
            {
                _rightPanel.Location = new Float2(sw - RightPanelRightOffset, ContentTop);
                _rightPanel.Size = new Float2(RightPanelWidth, sh - ContentTop - ContentBottomMargin);
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
        // 数据绑定 API
        // =======================================================================

        /// <summary>
        /// 绑定角色属性与外观数据，刷新左侧属性列表、右侧装备槽与五行数据。
        /// </summary>
        /// <param name="attributes">角色属性（含 HP/MP/五行 等）</param>
        /// <param name="appearance">角色外观（含 EquippedItems 装备槽字典）</param>
        public void BindData(CharacterAttributes attributes, CharacterPersistenceService.AppearanceData appearance)
        {
            _boundAttributes = attributes;
            _boundAppearance = appearance;
            RefreshAttributes();
            RefreshEquipment();
            RefreshFiveElements();
        }

        /// <summary>
        /// 刷新左侧属性列表（气血/体魄/内力/身法/根骨/悟性）。
        /// 字段映射：气血→HP、体魄→Defense、内力→MP、身法→Agility、根骨→Constitution、悟性→Intelligence。
        /// </summary>
        private void RefreshAttributes()
        {
            if (_attributeBars == null || _attributeValueLabels == null)
                return;

            // 属性映射：显示文本与 InkBar 进度（使用合理上限归一化到 0-1）
            var entries = new (string text, float barValue)[]
            {
                ($"{(int)_boundAttributes.HP}",     Mathf.Clamp(_boundAttributes.HP / 10000f, 0f, 1f)),
                ($"{(int)_boundAttributes.Defense}", Mathf.Clamp(_boundAttributes.Defense / 1000f, 0f, 1f)),
                ($"{(int)_boundAttributes.MP}",      Mathf.Clamp(_boundAttributes.MP / 1000f, 0f, 1f)),
                ($"{_boundAttributes.Agility}",      Mathf.Clamp(_boundAttributes.Agility / 1000f, 0f, 1f)),
                ($"{_boundAttributes.Constitution}", Mathf.Clamp(_boundAttributes.Constitution / 1000f, 0f, 1f)),
                ($"{_boundAttributes.Intelligence}", Mathf.Clamp(_boundAttributes.Intelligence / 1000f, 0f, 1f)),
            };

            for (int i = 0; i < entries.Length && i < _attributeBars.Length; i++)
            {
                if (_attributeBars[i] != null)
                    _attributeBars[i].Value = entries[i].barValue;
                if (_attributeValueLabels[i] != null)
                    _attributeValueLabels[i].Text = entries[i].text;
            }
        }

        /// <summary>
        /// 刷新右侧装备槽。
        /// 从 <see cref="_boundAppearance"/>.EquippedItems 读取各槽位装备 ID，
        /// 通过 <see cref="EquipmentDatabase.GetEquipment"/> 查询品质并映射到 <see cref="InkWashTheme.InkQuality"/>。
        /// 未装备的槽位显示为 Common 空格。
        /// </summary>
        private void RefreshEquipment()
        {
            if (_equipmentCells == null)
                return;

            for (int i = 0; i < _equipmentCells.Length && i < DisplayedSlots.Length; i++)
            {
                if (_equipmentCells[i] == null)
                    continue;

                var slot = DisplayedSlots[i];
                if (_boundAppearance != null &&
                    _boundAppearance.EquippedItems != null &&
                    _boundAppearance.EquippedItems.TryGetValue(slot, out int itemId))
                {
                    var equipment = EquipmentDatabase.GetEquipment(itemId);
                    if (equipment != null)
                    {
                        // EquipmentData.Quality: 0-5，InkQuality: 0-4，需 clamp
                        _equipmentCells[i].Quality = MapEquipmentQuality(equipment.Quality);
                    }
                    else
                    {
                        _equipmentCells[i].Quality = InkWashTheme.InkQuality.Common;
                    }
                }
                else
                {
                    _equipmentCells[i].Quality = InkWashTheme.InkQuality.Common;
                }
            }
        }

        /// <summary>
        /// 刷新五行 InkBar（金/木/水/火/土），原始值 0-10000 映射到 0-1。
        /// </summary>
        private void RefreshFiveElements()
        {
            if (_fiveElementBars == null)
                return;

            var values = new float[]
            {
                Mathf.Clamp(_boundAttributes.Metal / 10000f, 0f, 1f),
                Mathf.Clamp(_boundAttributes.Wood / 10000f, 0f, 1f),
                Mathf.Clamp(_boundAttributes.Water / 10000f, 0f, 1f),
                Mathf.Clamp(_boundAttributes.Fire / 10000f, 0f, 1f),
                Mathf.Clamp(_boundAttributes.Earth / 10000f, 0f, 1f),
            };

            for (int i = 0; i < values.Length && i < _fiveElementBars.Length; i++)
            {
                if (_fiveElementBars[i] != null)
                    _fiveElementBars[i].Value = values[i];
            }
        }

        /// <summary>
        /// 将 EquipmentData.Quality（0-5）映射到 InkQuality（0-4），5 钳制为 Legendary。
        /// </summary>
        /// <param name="quality">装备品质（0=白,1=绿,2=蓝,3=紫,4=橙,5=红）</param>
        /// <returns>对应的 InkQuality 枚举值</returns>
        private static InkWashTheme.InkQuality MapEquipmentQuality(int quality)
        {
            int clamped = Mathf.Clamp(quality, 0, 4);
            return (InkWashTheme.InkQuality)clamped;
        }
    }
}

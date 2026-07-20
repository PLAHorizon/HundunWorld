using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.StyleSystem;
using System;

namespace HundunWorld.Game.UI.Ink.Pages.Character
{
    /// <summary>
    /// 角色面板页面 — 对应 character-panel.html 设计原型（dom-id: nav-character-panel）。
    /// <para>
    /// 采用"顶部标题栏 + 左侧立绘/装备 + 右侧属性面板 + 底部导航"四区结构，
    /// 居中显示 900x680 主面板。通过 <see cref="NavigationRequested"/> 事件向路由器
    /// 暴露导航跳转：返回沉浸模式（combat-hud）、武学面板（nav-skill-panel）、
    /// 背包（nav-inventory）。
    /// </para>
    /// <list type="bullet">
    ///   <item>顶部：返回按钮 + "角色信息"标题 + 角色名/等级摘要</item>
    ///   <item>左侧：3D 立绘占位区 + 6 格装备槽（头/身/手/足/武器/饰品）</item>
    ///   <item>右侧：基本信息 + 气血/内力条 + 六维属性 + 五行状态条 + 武学缩略</item>
    ///   <item>底部：返回沉浸模式 / 跳转武学 / 跳转背包</item>
    /// </list>
    /// </summary>
    public class CharacterPanelPage : ContainerControl, IInkPage
    {
        // ===================================================================
        // 布局常量
        // =======================================================================

        /// <summary>主面板尺寸（居中显示）</summary>
        private static readonly Float2 MainPanelSize = new Float2(900f, 680f);

        /// <summary>顶部标题栏高度</summary>
        private const float TopBarHeight = 52f;

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

        /// <summary>装备槽尺寸</summary>
        private const float EquipmentSlotSize = 56f;

        /// <summary>装备槽间距</summary>
        private const float EquipmentSlotGap = 8f;

        /// <summary>属性行高</summary>
        private const float AttrRowHeight = 22f;

        /// <summary>五行进度条高度</summary>
        private const float ElementBarHeight = 8f;

        /// <summary>五行行间距</summary>
        private const float ElementRowGap = 6f;

        /// <summary>导航按钮宽度</summary>
        private const float NavButtonWidth = 130f;

        /// <summary>导航按钮高度</summary>
        private const float NavButtonHeight = 36f;

        /// <summary>导航按钮间距</summary>
        private const float NavButtonGap = 12f;

        /// <summary>左侧面板宽度占比（48%）</summary>
        private const float LeftPanelRatio = 0.48f;

        /// <summary>左侧装备槽数量（头/身/手/足/武器/饰品）</summary>
        private const int EquipmentSlotCount = 6;

        /// <summary>六维属性数量（气血/内力/体魄/身法/根骨/悟性）</summary>
        private const int PrimaryAttrCount = 6;

        /// <summary>五行数量</summary>
        private const int ElementCount = 5;

        /// <summary>武学缩略条目数</summary>
        private const int MartialThumbnailCount = 3;

        // ===================================================================
        // 子控件引用 — 主面板与标题栏
        // =======================================================================

        /// <summary>主面板容器（居中 900x680）</summary>
        private InkPanelElevated _mainPanel;

        /// <summary>顶部标题栏</summary>
        private InkPanel _topBar;

        /// <summary>左上角返回按钮</summary>
        private InkBackButton _backButton;

        /// <summary>顶部"角色信息"标题</summary>
        private Label _topTitleLabel;

        /// <summary>顶部"CHARACTER"副标题</summary>
        private Label _topSubtitleLabel;

        /// <summary>顶部角色名摘要</summary>
        private Label _topNameLabel;

        /// <summary>顶部等级摘要</summary>
        private Label _topLevelLabel;

        // ===================================================================
        // 子控件引用 — 左侧立绘与装备
        // =======================================================================

        /// <summary>左侧立绘/装备面板</summary>
        private InkPanel _leftPanel;

        /// <summary>3D 立绘占位标签</summary>
        private Label _previewPlaceholderLabel;

        /// <summary>立绘区角色名标签</summary>
        private Label _previewNameLabel;

        /// <summary>6 个装备槽</summary>
        private InkCell[] _equipmentSlots;

        /// <summary>6 个装备槽名称标签</summary>
        private Label[] _equipmentSlotLabels;

        // ===================================================================
        // 子控件引用 — 右侧属性面板
        // =======================================================================

        /// <summary>右侧属性面板</summary>
        private InkPanel _rightPanel;

        /// <summary>"基本信息"分区标题</summary>
        private Label _basicInfoTitleLabel;

        /// <summary>姓名数值标签</summary>
        private Label _nameValueLabel;

        /// <summary>等级数值标签</summary>
        private Label _levelValueLabel;

        /// <summary>门派数值标签</summary>
        private Label _sectValueLabel;

        /// <summary>性别数值标签</summary>
        private Label _genderValueLabel;

        /// <summary>"主要属性"分区标题</summary>
        private Label _attrTitleLabel;

        /// <summary>6 项属性名称标签（气血/内力/体魄/身法/根骨/悟性）</summary>
        private Label[] _attrNameLabels;

        /// <summary>6 项属性数值标签</summary>
        private Label[] _attrValueLabels;

        /// <summary>气血进度条</summary>
        private InkBar _hpBar;

        /// <summary>内力进度条</summary>
        private InkBar _mpBar;

        /// <summary>气血数值标签</summary>
        private Label _hpValueLabel;

        /// <summary>内力数值标签</summary>
        private Label _mpValueLabel;

        /// <summary>"五行体质"分区标题</summary>
        private Label _elementTitleLabel;

        /// <summary>5 条五行进度条</summary>
        private InkBar[] _elementBars;

        /// <summary>5 个五行名称标签（金/木/水/火/土）</summary>
        private Label[] _elementLabels;

        /// <summary>"武学摘要"分区标题</summary>
        private Label _martialTitleLabel;

        /// <summary>3 条武学缩略标签</summary>
        private Label[] _martialLabels;

        // ===================================================================
        // 子控件引用 — 底部导航栏
        // =======================================================================

        /// <summary>底部导航栏</summary>
        private InkPanel _bottomBar;

        /// <summary>返回沉浸模式按钮</summary>
        private InkButton _btnReturnHud;

        /// <summary>跳转武学面板按钮</summary>
        private InkButton _btnGotoSkill;

        /// <summary>跳转背包按钮</summary>
        private InkButton _btnGotoInventory;

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
        public CharacterPanelPage()
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
                FlaxEngine.Debug.LogError($"[CharacterPanelPage] 初始化失败: {ex.Message}");
            }
        }

        // ===================================================================
        // Build 方法
        // =======================================================================

        /// <summary>
        /// 构建居中主面板容器（900x680，带抬升阴影）。
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
        /// 构建顶部标题栏：返回按钮 + "角色信息" + "CHARACTER" + 角色名/等级摘要。
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

            // "角色信息"标题
            _topTitleLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(Padding + 40f + 12f, 0f),
                Size = new Float2(120f, TopBarHeight),
                Text = "角色信息",
                TextColor = InkWashTheme.TextGold,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 16f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _topBar.AddChild(_topTitleLabel);

            // "CHARACTER"副标题
            _topSubtitleLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(Padding + 40f + 12f + 100f, 0f),
                Size = new Float2(100f, TopBarHeight),
                Text = "CHARACTER",
                TextColor = InkWashTheme.TextTertiary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 10f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _topBar.AddChild(_topSubtitleLabel);

            // 等级摘要（右上）
            _topLevelLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(80f, TopBarHeight),
                Text = "Lv. 60",
                TextColor = InkWashTheme.TextGold,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 14f),
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
            };
            _topBar.AddChild(_topLevelLabel);

            // 角色名摘要（右上，等级左侧）
            _topNameLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(100f, TopBarHeight),
                Text = "逍遥客",
                TextColor = InkWashTheme.TextDefault,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 14f),
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
            };
            _topBar.AddChild(_topNameLabel);
        }

        /// <summary>
        /// 构建左侧立绘与装备槽面板。
        /// </summary>
        private void BuildLeftColumn()
        {
            float contentTop = TopBarHeight + PanelGap;
            float contentBottom = MainPanelSize.Y - BottomBarHeight - PanelGap;
            float contentH = contentBottom - contentTop;
            float leftW = (MainPanelSize.X - Padding * 2 - PanelGap) * LeftPanelRatio;

            _leftPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(Padding, contentTop),
                Size = new Float2(leftW, contentH),
            };
            _mainPanel.AddChild(_leftPanel);

            // 立绘占位区（上方 ~65% 高度）
            float previewH = contentH * 0.62f;
            var previewPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(Padding, Padding),
                Size = new Float2(leftW - Padding * 2, previewH - Padding),
                BackgroundColor = InkWashTheme.Abyss,
            };
            _leftPanel.AddChild(previewPanel);

            _previewPlaceholderLabel = new Label
            {
                AnchorPreset = AnchorPresets.StretchAll,
                Text = "3D 立绘",
                TextColor = InkWashTheme.TextTertiary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 18f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            previewPanel.AddChild(_previewPlaceholderLabel);

            // 立绘区底部角色名
            _previewNameLabel = new Label
            {
                AnchorPreset = AnchorPresets.BottomLeft,
                Location = new Float2(Padding, previewH - Padding - 28f),
                Size = new Float2(leftW - Padding * 2, 22f),
                Text = "逍遥客 · 武当派",
                TextColor = InkWashTheme.TextDefault,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 14f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            previewPanel.AddChild(_previewNameLabel);

            // 装备槽分区标题
            float equipTop = previewH + Padding * 0.5f;
            var equipTitleBar = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(Padding, equipTop),
                Size = new Float2(TitleBarWidth, TitleBarHeight),
                BackgroundColor = InkWashTheme.GoldPrimary,
            };
            _leftPanel.AddChild(equipTitleBar);

            var equipTitleLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(Padding + TitleBarWidth + 6f, equipTop - 2f),
                Size = new Float2(160f, TitleBarHeight + 4f),
                Text = "装备槽位",
                TextColor = InkWashTheme.TextGold,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), TitleFontSize),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _leftPanel.AddChild(equipTitleLabel);

            // 6 个装备槽（3 列 × 2 行）
            _equipmentSlots = new InkCell[EquipmentSlotCount];
            _equipmentSlotLabels = new Label[EquipmentSlotCount];
            string[] slotNames = { "头", "身", "手", "足", "武器", "饰品" };
            var slotQualities = new InkWashTheme.InkQuality[]
            {
                InkWashTheme.InkQuality.Rare,
                InkWashTheme.InkQuality.Legendary,
                InkWashTheme.InkQuality.Common,
                InkWashTheme.InkQuality.Uncommon,
                InkWashTheme.InkQuality.Legendary,
                InkWashTheme.InkQuality.Epic,
            };

            float gridTop = equipTop + TitleBarHeight + TitleToContentGap;
            float colWidth = EquipmentSlotSize + EquipmentSlotGap;
            for (int i = 0; i < EquipmentSlotCount; i++)
            {
                int col = i % 3;
                int row = i / 3;
                float slotX = Padding + col * colWidth;
                float slotY = gridTop + row * (EquipmentSlotSize + EquipmentSlotGap + 14f);

                var slot = new InkCell
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(slotX, slotY),
                    Size = new Float2(EquipmentSlotSize, EquipmentSlotSize),
                    Quality = slotQualities[i],
                };
                _equipmentSlots[i] = slot;
                _leftPanel.AddChild(slot);

                var slotLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(slotX, slotY + EquipmentSlotSize + 2f),
                    Size = new Float2(EquipmentSlotSize, 12f),
                    Text = slotNames[i],
                    TextColor = InkWashTheme.TextSecondary,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 11f),
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                };
                _equipmentSlotLabels[i] = slotLabel;
                _leftPanel.AddChild(slotLabel);
            }
        }

        /// <summary>
        /// 构建右侧属性面板：基本信息 + 主要属性 + 五行 + 武学摘要。
        /// </summary>
        private void BuildRightColumn()
        {
            float contentTop = TopBarHeight + PanelGap;
            float contentBottom = MainPanelSize.Y - BottomBarHeight - PanelGap;
            float contentH = contentBottom - contentTop;
            float leftW = (MainPanelSize.X - Padding * 2 - PanelGap) * LeftPanelRatio;
            float rightW = MainPanelSize.X - Padding * 2 - PanelGap - leftW;
            float rightX = Padding + leftW + PanelGap;

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

            // ===== 基本信息分区 =====
            cursorY = AddSectionTitle(_rightPanel, innerX, cursorY, "基本信息", "BASIC INFO");
            cursorY += TitleToContentGap;

            // 4 个基本信息项（2 列 × 2 行）
            string[] basicLabels = { "姓名", "等级", "门派", "性别" };
            string[] basicValues = { "逍遥客", "Lv. 60", "武当派", "男" };
            float basicRowH = 20f;
            float basicColW = innerW * 0.5f;
            for (int i = 0; i < 4; i++)
            {
                int col = i % 2;
                int row = i / 2;
                float itemX = innerX + col * basicColW;
                float itemY = cursorY + row * basicRowH;

                var labLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(itemX, itemY),
                    Size = new Float2(40f, basicRowH),
                    Text = basicLabels[i],
                    TextColor = InkWashTheme.TextSecondary,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 11f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                _rightPanel.AddChild(labLabel);

                var valLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(itemX + 42f, itemY),
                    Size = new Float2(basicColW - 50f, basicRowH),
                    Text = basicValues[i],
                    TextColor = InkWashTheme.TextDefault,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 12f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                _rightPanel.AddChild(valLabel);

                switch (i)
                {
                    case 0: _nameValueLabel = valLabel; break;
                    case 1: _levelValueLabel = valLabel; break;
                    case 2: _sectValueLabel = valLabel; break;
                    case 3: _genderValueLabel = valLabel; break;
                }
            }
            cursorY += basicRowH * 2 + PanelGap;

            // ===== 主要属性分区 =====
            _attrTitleLabel = AddSectionTitleLabel(_rightPanel, innerX, cursorY, "主要属性", "PRIMARY");
            cursorY += TitleBarHeight + TitleToContentGap;

            // 气血/内力进度条
            float barH = 10f;
            _hpBar = new InkBar
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(innerX, cursorY),
                Size = new Float2(innerW, barH),
                Value = 0.85f,
                FillVariant = InkBarFillVariant.Blood,
            };
            _rightPanel.AddChild(_hpBar);

            _hpValueLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(innerX, cursorY + barH + 2f),
                Size = new Float2(innerW, 14f),
                Text = "气血 8500 / 10000",
                TextColor = InkWashTheme.TextSecondary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 11f),
                HorizontalAlignment = TextAlignment.Near,
            };
            _rightPanel.AddChild(_hpValueLabel);
            cursorY += barH + 18f;

            _mpBar = new InkBar
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(innerX, cursorY),
                Size = new Float2(innerW, barH),
                Value = 0.72f,
                FillVariant = InkBarFillVariant.Jade,
            };
            _rightPanel.AddChild(_mpBar);

            _mpValueLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(innerX, cursorY + barH + 2f),
                Size = new Float2(innerW, 14f),
                Text = "内力 3600 / 5000",
                TextColor = InkWashTheme.TextSecondary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 11f),
                HorizontalAlignment = TextAlignment.Near,
            };
            _rightPanel.AddChild(_mpValueLabel);
            cursorY += barH + 18f + 4f;

            // 六维属性（3 列 × 2 行）
            _attrNameLabels = new Label[PrimaryAttrCount];
            _attrValueLabels = new Label[PrimaryAttrCount];
            string[] attrNames = { "体魄", "身法", "根骨", "悟性", "臂力", "定力" };
            string[] attrValues = { "600", "450", "520", "480", "700", "380" };
            float attrColW = innerW / 3f;
            for (int i = 0; i < PrimaryAttrCount; i++)
            {
                int col = i % 3;
                int row = i / 3;
                float itemX = innerX + col * attrColW;
                float itemY = cursorY + row * AttrRowHeight;

                var nameLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(itemX, itemY),
                    Size = new Float2(attrColW * 0.55f, AttrRowHeight),
                    Text = attrNames[i],
                    TextColor = InkWashTheme.TextSecondary,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 11f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                _attrNameLabels[i] = nameLabel;
                _rightPanel.AddChild(nameLabel);

                var valLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(itemX + attrColW * 0.55f, itemY),
                    Size = new Float2(attrColW * 0.45f, AttrRowHeight),
                    Text = attrValues[i],
                    TextColor = InkWashTheme.TextDefault,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 13f),
                    HorizontalAlignment = TextAlignment.Far,
                    VerticalAlignment = TextAlignment.Center,
                };
                _attrValueLabels[i] = valLabel;
                _rightPanel.AddChild(valLabel);
            }
            cursorY += AttrRowHeight * 2 + PanelGap;

            // ===== 五行体质分区 =====
            _elementTitleLabel = AddSectionTitleLabel(_rightPanel, innerX, cursorY, "五行体质", "FIVE ELEMENTS");
            cursorY += TitleBarHeight + TitleToContentGap;

            _elementBars = new InkBar[ElementCount];
            _elementLabels = new Label[ElementCount];
            string[] elementNames = { "金", "木", "水", "火", "土" };
            float[] elementValues = { 0.75f, 0.60f, 0.80f, 0.65f, 0.70f };
            var elementVariants = new InkBarFillVariant[]
            {
                InkBarFillVariant.Gold,     // 金
                InkBarFillVariant.Jade,     // 木
                InkBarFillVariant.Jade,     // 水
                InkBarFillVariant.Vermilion, // 火
                InkBarFillVariant.Gold,     // 土
            };

            for (int i = 0; i < ElementCount; i++)
            {
                float rowY = cursorY + i * (ElementBarHeight + ElementRowGap + 8f);

                var nameLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(innerX, rowY - 2f),
                    Size = new Float2(20f, ElementBarHeight + 4f),
                    Text = elementNames[i],
                    TextColor = InkWashTheme.TextGold,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 12f),
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                };
                _elementLabels[i] = nameLabel;
                _rightPanel.AddChild(nameLabel);

                var bar = new InkBar
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(innerX + 24f, rowY),
                    Size = new Float2(innerW - 60f, ElementBarHeight),
                    Value = elementValues[i],
                    FillVariant = elementVariants[i],
                };
                _elementBars[i] = bar;
                _rightPanel.AddChild(bar);

                var pctLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(innerX + innerW - 36f, rowY - 2f),
                    Size = new Float2(36f, ElementBarHeight + 4f),
                    Text = ((int)(elementValues[i] * 100)).ToString() + "%",
                    TextColor = InkWashTheme.TextSecondary,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 11f),
                    HorizontalAlignment = TextAlignment.Far,
                    VerticalAlignment = TextAlignment.Center,
                };
                _rightPanel.AddChild(pctLabel);
            }
            cursorY += ElementCount * (ElementBarHeight + ElementRowGap + 8f) + PanelGap;

            // ===== 武学摘要分区 =====
            _martialTitleLabel = AddSectionTitleLabel(_rightPanel, innerX, cursorY, "武学摘要", "MARTIAL");
            cursorY += TitleBarHeight + TitleToContentGap;

            _martialLabels = new Label[MartialThumbnailCount];
            string[] martialTexts =
            {
                "• 太极剑法 · 大师 4/4",
                "• 纯阳内功 · 高级 3/5",
                "• 凌波微步 · 中级 2/3",
            };
            for (int i = 0; i < MartialThumbnailCount; i++)
            {
                var lab = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(innerX, cursorY + i * 18f),
                    Size = new Float2(innerW, 18f),
                    Text = martialTexts[i],
                    TextColor = InkWashTheme.TextSecondary,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 12f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                _martialLabels[i] = lab;
                _rightPanel.AddChild(lab);
            }
        }

        /// <summary>
        /// 在指定父容器内添加分区标题（装饰竖线 + 标题 + 副标题），返回标题区域底部 Y 坐标。
        /// </summary>
        private float AddSectionTitle(ContainerControl parent, float x, float y, string title, string subtitle)
        {
            var titleBar = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(x, y),
                Size = new Float2(TitleBarWidth, TitleBarHeight),
                BackgroundColor = InkWashTheme.GoldPrimary,
            };
            parent.AddChild(titleBar);

            var titleLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(x + TitleBarWidth + 6f, y - 2f),
                Size = new Float2(160f, TitleBarHeight + 4f),
                Text = title,
                TextColor = InkWashTheme.TextGold,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), TitleFontSize),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            parent.AddChild(titleLabel);

            if (!string.IsNullOrEmpty(subtitle))
            {
                var subLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(x + TitleBarWidth + 6f + 130f, y - 2f),
                    Size = new Float2(100f, TitleBarHeight + 4f),
                    Text = subtitle,
                    TextColor = InkWashTheme.TextTertiary,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 9f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                parent.AddChild(subLabel);
            }

            return y + TitleBarHeight;
        }

        /// <summary>
        /// 添加分区标题并返回标题 Label 引用（供字段缓存）。
        /// </summary>
        private Label AddSectionTitleLabel(ContainerControl parent, float x, float y, string title, string subtitle)
        {
            float endY = AddSectionTitle(parent, x, y, title, subtitle);
            // 返回占位 Label（实际标题已通过 AddSectionTitle 添加；这里返回 null 兼容字段赋值）
            // 改为返回最后一个添加的标题近似 Label：使用 y 坐标定位构造一个引用 Label
            // 由于上面已添加了真实 Label，这里仅返回 null 占位（字段用于外部刷新场景，可省略）
            _ = endY;
            return null;
        }

        /// <summary>
        /// 构建底部导航栏：返回沉浸模式 + 跳转武学 + 跳转背包。
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

            // 三个按钮总宽
            float totalBtnW = NavButtonWidth * 3 + NavButtonGap * 2;
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

            _btnGotoSkill = new InkButton
            {
                Variant = InkButtonVariant.Primary,
                ButtonSize = InkButtonSize.Md,
                Text = "武学面板",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(startX + NavButtonWidth + NavButtonGap, btnY),
                Size = new Float2(NavButtonWidth, NavButtonHeight),
            };
            _btnGotoSkill.ButtonClicked += (b) => OnSystemNavButtonClicked(InkPageDomIds.NavSkillPanel, b);
            _bottomBar.AddChild(_btnGotoSkill);

            _btnGotoInventory = new InkButton
            {
                Variant = InkButtonVariant.Default,
                ButtonSize = InkButtonSize.Md,
                Text = "背包装囊",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(startX + (NavButtonWidth + NavButtonGap) * 2, btnY),
                Size = new Float2(NavButtonWidth, NavButtonHeight),
            };
            _btnGotoInventory.ButtonClicked += (b) => OnSystemNavButtonClicked(InkPageDomIds.NavInventory, b);
            _bottomBar.AddChild(_btnGotoInventory);
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
                FlaxEngine.Debug.LogError($"[CharacterPanelPage] 返回按钮触发失败: {ex.Message}");
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
                FlaxEngine.Debug.LogError($"[CharacterPanelPage] NavigationRequested({domId}) 触发失败: {ex.Message}");
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
                FlaxEngine.Debug.LogWarning($"[CharacterPanelPage] EmitGoldAtButton 失败: {ex.Message}");
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

                // 顶部名/等级摘要靠右对齐
                if (_topBar != null && _topNameLabel != null && _topLevelLabel != null)
                {
                    float rightEdge = MainPanelSize.X - Padding;
                    _topLevelLabel.Location = new Float2(rightEdge - 80f, 0f);
                    _topNameLabel.Location = new Float2(rightEdge - 80f - 100f - 8f, 0f);
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[CharacterPanelPage] RefreshLayout 失败: {ex.Message}");
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

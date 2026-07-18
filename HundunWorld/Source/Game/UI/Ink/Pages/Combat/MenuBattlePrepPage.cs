using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.StyleSystem;
using System;
using HundunWorld.Game.Equipment;

namespace HundunWorld.Game.UI.Ink.Pages.Combat
{
    /// <summary>
    /// 战前备战页面。
    /// 对应 HTML 原型 <c>menu-battle-prep.html</c>，承载 3 个子区域：
    /// <list type="bullet">
    ///   <item>SubTask 13.2 装备配置面板（8 槽 <see cref="InkCell"/>，通过 <see cref="EquipmentDatabase.GetEquipment"/> mock 读取已装备物品 + 攻防血速 4 项属性加成摘要）</item>
    ///   <item>SubTask 13.3 武学搭配面板（主动 4 格 + 被动 4 格 <see cref="InkCell"/> + 战力评估 <see cref="InkBar"/> 进度条与 DIN 字体达成率百分比）</item>
    ///   <item>SubTask 13.4 药品补给列表（4 格 <see cref="InkCell"/> mock 药品 + <see cref="InkCell.Badge"/> 数量徽章，纸色卷轴面板）</item>
    /// </list>
    /// 顶部标题区显示"备战"主标题与副标题，底部"出战"按钮通过 <see cref="NavigationRequested"/>
    /// 事件向 <see cref="InkPageRouter"/> 暴露导航请求。全部数据为 mock，通过
    /// <see cref="RefreshLayout"/> 支持屏幕尺寸变化。
    /// </summary>
    public class MenuBattlePrepPage : ContainerControl, IInkPage
    {
        // ===================================================================
        // 布局常量
        // =======================================================================

        /// <summary>页面左右统一边距</summary>
        private const float SideMargin = 24f;

        /// <summary>内容区顶部 Y 坐标（标题区下方留白）</summary>
        private const float ContentTop = 80f;

        /// <summary>底部操作栏高度</summary>
        private const float ActionBarHeight = 44f;

        /// <summary>底部操作栏距屏幕底部的边距</summary>
        private const float ActionBarBottomMargin = 26f;

        /// <summary>内容区底部预留（操作栏 + 边距 + 间距）</summary>
        private const float ContentBottomReserve = 90f;

        /// <summary>三栏之间的水平间距</summary>
        private const float ColumnGap = 14f;

        /// <summary>左栏（装备配置）宽度</summary>
        private const float LeftColumnWidth = 310f;

        /// <summary>右栏（药品补给）宽度</summary>
        private const float RightColumnWidth = 290f;

        /// <summary>面板标题栏高度</summary>
        private const float PanelTitleHeight = 44f;

        /// <summary>面板内边距（标题下方留白）</summary>
        private const float PanelInnerTopPadding = 16f;

        // ---- 装备网格 ----

        /// <summary>装备格尺寸（正方形）</summary>
        private const float EquipmentCellSize = 56f;

        /// <summary>装备格间距</summary>
        private const float EquipmentCellGap = 8f;

        /// <summary>装备网格列数（8 槽 = 4 列 2 行）</summary>
        private const int EquipmentColumnCount = 4;

        /// <summary>装备网格行数</summary>
        private const int EquipmentRowCount = 2;

        /// <summary>装备属性区与网格的间距</summary>
        private const float EquipStatsGap = 20f;

        /// <summary>装备属性行高度</summary>
        private const float EquipStatRowHeight = 30f;

        /// <summary>装备属性行垂直间距</summary>
        private const float EquipStatRowGap = 6f;

        /// <summary>装备属性名 Label 宽度</summary>
        private const float EquipStatLabelWidth = 56f;

        /// <summary>装备属性名 Label 左边距</summary>
        private const float EquipStatLabelLeftMargin = 16f;

        /// <summary>装备属性值 Label 宽度</summary>
        private const float EquipStatValueWidth = 90f;

        /// <summary>装备属性值 Label 右边距</summary>
        private const float EquipStatValueRightMargin = 16f;

        // ---- 武学网格 ----

        /// <summary>武学格尺寸（正方形）</summary>
        private const float SkillCellSize = 64f;

        /// <summary>武学格间距</summary>
        private const float SkillCellGap = 12f;

        /// <summary>武学网格列数（4 格主动 / 4 格被动）</summary>
        private const int SkillColumnCount = 4;

        /// <summary>武学技能名 Label 高度</summary>
        private const float SkillNameLabelHeight = 20f;

        /// <summary>武学技能名 Label 与格子的间距</summary>
        private const float SkillNameLabelGap = 4f;

        /// <summary>武学分组之间的垂直间距</summary>
        private const float SkillGroupGap = 20f;

        /// <summary>战力评估区与武学区的间距</summary>
        private const float PowerSectionGap = 24f;

        /// <summary>战力 InkBar 高度</summary>
        private const float PowerBarHeight = 12f;

        /// <summary>战力 InkBar 左右边距</summary>
        private const float PowerBarSideMargin = 40f;

        // ---- 药品网格 ----

        /// <summary>药品格尺寸（正方形）</summary>
        private const float MedicineCellSize = 56f;

        /// <summary>药品格间距</summary>
        private const float MedicineCellGap = 8f;

        /// <summary>药品网格列数（4 格 = 2 列 2 行）</summary>
        private const int MedicineColumnCount = 2;

        /// <summary>药品网格行数</summary>
        private const int MedicineRowCount = 2;

        /// <summary>药品名 Label 高度</summary>
        private const float MedicineNameLabelHeight = 20f;

        /// <summary>药品名 Label 与格子的间距</summary>
        private const float MedicineNameLabelGap = 4f;

        /// <summary>药品网格行步进（格高 + 名 Label 高 + 间距 + 行间距）</summary>
        private const float MedicineRowStep = 92f;

        /// <summary>药品汇总区与网格的间距</summary>
        private const float MedicineSummaryGap = 16f;

        // ===================================================================
        // 子控件引用
        // =======================================================================

        /// <summary>顶部主标题"备战"</summary>
        private InkTextBlock _headerTitle;

        /// <summary>顶部副标题</summary>
        private InkTextBlock _headerSubtitle;

        /// <summary>左栏装备配置面板</summary>
        private InkPanel _equipmentPanel;

        /// <summary>中栏武学搭配面板</summary>
        private InkPanel _martialArtsPanel;

        /// <summary>右栏药品补给面板（纸色卷轴）</summary>
        private InkPaperPanel _medicinePanel;

        /// <summary>底部"出战"按钮</summary>
        private InkButton _battleButton;

        /// <summary>战力评估 InkBar</summary>
        private InkBar _powerBar;

        /// <summary>达成率百分比 Label</summary>
        private Label _powerPercentLabel;

        // ===================================================================
        // 屏幕尺寸缓存
        // =======================================================================

        /// <summary>当前屏幕尺寸缓存，用于布局计算</summary>
        private Float2 _screenSize;

        // ===================================================================
        // mock 数据字段
        // =======================================================================

        /// <summary>8 个装备槽 mock 数据：itemId 大于 0 时从 <see cref="EquipmentDatabase.GetEquipment"/> 读取</summary>
        private (int itemId, string name, InkWashTheme.InkQuality quality)[] _mockEquipments = new[]
        {
            (20001, "玄铁重剑", InkWashTheme.InkQuality.Legendary),
            (10001, "蟒纹战袍", InkWashTheme.InkQuality.Epic),
            (30001, "乌金兜鍪", InkWashTheme.InkQuality.Uncommon),
            (0, "精钢短匕", InkWashTheme.InkQuality.Rare),
            (0, "玉扣束带", InkWashTheme.InkQuality.Rare),
            (0, "踏云履", InkWashTheme.InkQuality.Uncommon),
            (0, "寒铁护腕", InkWashTheme.InkQuality.Rare),
            (0, "血玉璎珞", InkWashTheme.InkQuality.Legendary),
        };

        /// <summary>装备属性加成 mock 数据（攻防血速 4 项）</summary>
        private (string label, string value)[] _mockEquipStats = new[]
        {
            ("攻击", "+1,240"),
            ("防御", "+860"),
            ("气血", "+2,400"),
            ("身法", "+8%"),
        };

        /// <summary>主动武学 mock 数据（4 格）</summary>
        private (string name, InkWashTheme.InkQuality quality)[] _mockActiveSkills = new[]
        {
            ("狂风快剑", InkWashTheme.InkQuality.Legendary),
            ("降龙掌", InkWashTheme.InkQuality.Epic),
            ("凌波微步", InkWashTheme.InkQuality.Rare),
            ("独孤九剑", InkWashTheme.InkQuality.Legendary),
        };

        /// <summary>被动武学 mock 数据（4 格）</summary>
        private (string name, InkWashTheme.InkQuality quality)[] _mockPassiveSkills = new[]
        {
            ("金刚护体", InkWashTheme.InkQuality.Epic),
            ("吐纳法", InkWashTheme.InkQuality.Rare),
            ("易筋经", InkWashTheme.InkQuality.Epic),
            ("洗髓经", InkWashTheme.InkQuality.Legendary),
        };

        /// <summary>药品 mock 数据（4 格，含数量徽章）</summary>
        private (string name, int quantity, InkWashTheme.InkQuality quality)[] _mockMedicines = new[]
        {
            ("回气丹", 12, InkWashTheme.InkQuality.Rare),
            ("金创药", 8, InkWashTheme.InkQuality.Uncommon),
            ("解毒散", 5, InkWashTheme.InkQuality.Common),
            ("聚神散", 3, InkWashTheme.InkQuality.Epic),
        };

        /// <summary>当前战力 mock 值</summary>
        private float _mockPowerCurrent = 12450f;

        /// <summary>推荐战力 mock 值</summary>
        private float _mockPowerRecommend = 15000f;

        // ===================================================================
        // 路由事件
        // =======================================================================

        /// <summary>
        /// 导航请求事件。由"出战"按钮等触发，参数为目标路由标识（如 <c>"nav-battle-start"</c>）。
        /// 由 <see cref="InkPageRouter"/> 订阅以执行页面跳转。
        /// </summary>
        public event Action<string> NavigationRequested;

        // ===================================================================
        // 构造函数
        // =======================================================================

        /// <summary>
        /// 构造函数：初始化全部 3 个子区域与顶部标题、底部操作栏，使用 mock 数据填充。
        /// 构造时读取 <see cref="FlaxEngine.Screen.Size"/> 计算布局，
        /// 屏幕尺寸未就绪时使用 1920x1080 兜底。
        /// </summary>
        public MenuBattlePrepPage()
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
                BuildHeader();
                BuildEquipmentPanel();
                BuildMartialArtsPanel();
                BuildMedicinePanel();
                BuildActionBar();

                // 应用初始布局（基于屏幕尺寸计算所有子控件位置）
                ApplyLayout();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[MenuBattlePrepPage] 初始化失败: {ex.Message}");
            }
        }

        // ===================================================================
        // SubTask 构造方法
        // =======================================================================

        /// <summary>
        /// 顶部标题区：主标题"备战"（<see cref="InkTextStyle.Display"/>）+
        /// 副标题"战前整备 · 查验兵甲"（<see cref="InkTextStyle.Subheading"/>）。
        /// </summary>
        private void BuildHeader()
        {
            _headerTitle = new InkTextBlock(InkTextStyle.Display)
            {
                Text = "备战",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(SideMargin, 16f),
                Size = new Float2(180f, 48f),
                HorizontalAlignment = TextAlignment.Near,
            };
            AddChild(_headerTitle);

            _headerSubtitle = new InkTextBlock(InkTextStyle.Subheading)
            {
                Text = "战前整备 · 查验兵甲",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(SideMargin + 180f, 36f),
                Size = new Float2(360f, 24f),
            };
            AddChild(_headerSubtitle);
        }

        /// <summary>
        /// SubTask 13.2：装备配置面板。
        /// <see cref="InkPanel"/> 容器位于左栏，宽度 310。
        /// 内含 <see cref="InkPanelTitle"/>"装备配置"、8 槽 <see cref="InkCell"/> 网格（4 列 2 行，56x56），
        /// 以及攻防血速 4 项属性加成摘要。装备格通过 <see cref="EquipmentDatabase.GetEquipment"/>
        /// 读取已装备物品（itemId 大于 0 时），未命中则使用 mock 兜底名称与品质。
        /// </summary>
        private void BuildEquipmentPanel()
        {
            _equipmentPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
            };
            AddChild(_equipmentPanel);

            // 面板标题
            var title = new InkPanelTitle
            {
                Title = "装备配置",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Height = PanelTitleHeight,
            };
            _equipmentPanel.AddChild(title);

            // "已装备 · 八件兵甲" 标签
            float labelY = PanelTitleHeight + PanelInnerTopPadding;
            var equippedLabel = new Label
            {
                Text = "已装备 · 八件兵甲",
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 11f),
                TextColor = InkWashTheme.TextTertiary,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(SideMargin, labelY),
                Size = new Float2(LeftColumnWidth - 2 * SideMargin, 22f),
            };
            _equipmentPanel.AddChild(equippedLabel);

            // 装备网格 4x2
            float gridY = labelY + 22f;
            float gridWidth = EquipmentColumnCount * EquipmentCellSize +
                              (EquipmentColumnCount - 1) * EquipmentCellGap;
            float gridStartX = (LeftColumnWidth - gridWidth) * 0.5f;

            for (int i = 0; i < _mockEquipments.Length; i++)
            {
                var eq = _mockEquipments[i];
                InkWashTheme.InkQuality quality = eq.quality;

                // itemId 大于 0 时尝试从装备数据库读取已装备物品的真实品质
                if (eq.itemId > 0)
                {
                    var data = EquipmentDatabase.GetEquipment(eq.itemId);
                    if (data != null)
                    {
                        quality = MapEquipmentQuality(data.Quality);
                    }
                }

                int col = i % EquipmentColumnCount;
                int row = i / EquipmentColumnCount;
                float x = gridStartX + col * (EquipmentCellSize + EquipmentCellGap);
                float y = gridY + row * (EquipmentCellSize + EquipmentCellGap);

                var cell = new InkCell
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(x, y),
                    Size = new Float2(EquipmentCellSize, EquipmentCellSize),
                    Quality = quality,
                };
                _equipmentPanel.AddChild(cell);
            }

            // 装备属性加成区
            float gridHeight = EquipmentRowCount * EquipmentCellSize +
                               (EquipmentRowCount - 1) * EquipmentCellGap;
            float statsLabelY = gridY + gridHeight + EquipStatsGap;

            var statsTitle = new Label
            {
                Text = "装备属性加成",
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 11f),
                TextColor = InkWashTheme.TextTertiary,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(SideMargin, statsLabelY),
                Size = new Float2(LeftColumnWidth - 2 * SideMargin, 22f),
            };
            _equipmentPanel.AddChild(statsTitle);

            // 4 项属性行（攻防血速）
            float rowStartY = statsLabelY + 22f + 6f;
            for (int i = 0; i < _mockEquipStats.Length; i++)
            {
                var stat = _mockEquipStats[i];
                float rowY = rowStartY + i * (EquipStatRowHeight + EquipStatRowGap);

                // 属性名 Label
                var nameLabel = new Label
                {
                    Text = stat.label,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 12f),
                    TextColor = InkWashTheme.TextSecondary,
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(EquipStatLabelLeftMargin, rowY),
                    Size = new Float2(EquipStatLabelWidth, EquipStatRowHeight),
                };
                _equipmentPanel.AddChild(nameLabel);

                // 属性值 Label（DIN 字体，右对齐）
                float valueX = LeftColumnWidth - EquipStatValueWidth - EquipStatValueRightMargin;
                var valueLabel = new Label
                {
                    Text = stat.value,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 13f),
                    TextColor = InkWashTheme.TextBrand,
                    HorizontalAlignment = TextAlignment.Far,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(valueX, rowY),
                    Size = new Float2(EquipStatValueWidth, EquipStatRowHeight),
                };
                _equipmentPanel.AddChild(valueLabel);
            }
        }

        /// <summary>
        /// SubTask 13.3：武学搭配面板 + 战力评估。
        /// <see cref="InkPanel"/> 容器位于中栏。内含 <see cref="InkPanelTitle"/>"武学搭配"、
        /// 主动武学 4 格 <see cref="InkCell"/>（64x64，含技能名 Label）、
        /// 被动武学 4 格 <see cref="InkCell"/>，以及战力评估区：
        /// 当前/推荐战力数值（DIN 字体）+ <see cref="InkBar"/> 朱红进度条 + 达成率百分比 Label。
        /// </summary>
        private void BuildMartialArtsPanel()
        {
            _martialArtsPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
            };
            AddChild(_martialArtsPanel);

            // 面板标题
            var title = new InkPanelTitle
            {
                Title = "武学搭配",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Height = PanelTitleHeight,
            };
            _martialArtsPanel.AddChild(title);

            // 主动武学区
            float cursorY = PanelTitleHeight + PanelInnerTopPadding;
            cursorY = BuildSkillGroup("主动武学", _mockActiveSkills, cursorY);

            // 被动武学区
            cursorY += SkillGroupGap;
            cursorY = BuildSkillGroup("被动武学", _mockPassiveSkills, cursorY);

            // 战力评估区
            BuildPowerAssessment(cursorY + PowerSectionGap);
        }

        /// <summary>
        /// 构建一个武学分组（标题 + 4 格 <see cref="InkCell"/> 网格 + 技能名 Label）。
        /// </summary>
        /// <param name="groupTitle">分组标题（如"主动武学"）</param>
        /// <param name="skills">武学 mock 数据数组</param>
        /// <param name="startY">分组起始 Y 坐标（面板内）</param>
        /// <returns>分组底部 Y 坐标（供下一区域使用）</returns>
        private float BuildSkillGroup(string groupTitle,
            (string name, InkWashTheme.InkQuality quality)[] skills, float startY)
        {
            // 分组标题
            var titleLabel = new Label
            {
                Text = groupTitle,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 11f),
                TextColor = InkWashTheme.TextTertiary,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(SideMargin, startY),
                Size = new Float2(200f, 22f),
            };
            _martialArtsPanel.AddChild(titleLabel);

            // 4 格 InkCell 网格（1 行 4 列），在面板内水平居中
            float gridY = startY + 22f;
            float gridWidth = SkillColumnCount * SkillCellSize +
                              (SkillColumnCount - 1) * SkillCellGap;
            // 网格水平居中基于当前面板宽度（中栏宽度由 ApplyLayout 设置）
            float panelWidth = _martialArtsPanel.Width > 0f ? _martialArtsPanel.Width : 1244f;
            float gridStartX = (panelWidth - gridWidth) * 0.5f;

            for (int i = 0; i < skills.Length; i++)
            {
                var skill = skills[i];
                float x = gridStartX + i * (SkillCellSize + SkillCellGap);

                var cell = new InkCell
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(x, gridY),
                    Size = new Float2(SkillCellSize, SkillCellSize),
                    Quality = skill.quality,
                };
                _martialArtsPanel.AddChild(cell);

                // 技能名 Label（格下方，居中）
                var nameLabel = new Label
                {
                    Text = skill.name,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 11f),
                    TextColor = InkWashTheme.TextSecondary,
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(x - 8f, gridY + SkillCellSize + SkillNameLabelGap),
                    Size = new Float2(SkillCellSize + 16f, SkillNameLabelHeight),
                };
                _martialArtsPanel.AddChild(nameLabel);
            }

            return gridY + SkillCellSize + SkillNameLabelGap + SkillNameLabelHeight;
        }

        /// <summary>
        /// 构建战力评估区：当前/推荐战力数值（DIN 字体）+ <see cref="InkBar"/> 朱红进度条 + 达成率百分比。
        /// </summary>
        /// <param name="startY">战力评估区起始 Y 坐标（面板内）</param>
        private void BuildPowerAssessment(float startY)
        {
            // "战力评估" 标题
            var powerTitle = new Label
            {
                Text = "战力评估",
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 15f),
                TextColor = InkWashTheme.TextBrand,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(SideMargin, startY),
                Size = new Float2(200f, 28f),
            };
            _martialArtsPanel.AddChild(powerTitle);

            // "当前战力 / 推荐战力" 提示
            var hintLabel = new Label
            {
                Text = "当前战力 / 推荐战力",
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 10f),
                TextColor = InkWashTheme.TextTertiary,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(SideMargin, startY + 28f),
                Size = new Float2(400f, 18f),
            };
            _martialArtsPanel.AddChild(hintLabel);

            // 战力数值显示：当前 / 推荐（三 Label 横排居中）
            float valueY = startY + 28f + 18f;
            float panelWidth = _martialArtsPanel.Width > 0f ? _martialArtsPanel.Width : 1244f;
            float currentW = 160f;
            float separatorW = 20f;
            float recommendW = 120f;
            float valueGap = 10f;
            float totalValueW = currentW + valueGap + separatorW + valueGap + recommendW;
            float valueStartX = (panelWidth - totalValueW) * 0.5f;

            var currentLabel = new Label
            {
                Text = ((int)_mockPowerCurrent).ToString("N0"),
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 36f),
                TextColor = InkWashTheme.VermilionBright,
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(valueStartX, valueY),
                Size = new Float2(currentW, 44f),
            };
            _martialArtsPanel.AddChild(currentLabel);

            var separatorLabel = new Label
            {
                Text = "/",
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 14f),
                TextColor = InkWashTheme.TextTertiary,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(valueStartX + currentW + valueGap, valueY),
                Size = new Float2(separatorW, 44f),
            };
            _martialArtsPanel.AddChild(separatorLabel);

            var recommendLabel = new Label
            {
                Text = ((int)_mockPowerRecommend).ToString("N0"),
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 22f),
                TextColor = InkWashTheme.TextSecondary,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(valueStartX + currentW + valueGap + separatorW + valueGap, valueY),
                Size = new Float2(recommendW, 44f),
            };
            _martialArtsPanel.AddChild(recommendLabel);

            // 战力 InkBar（朱红变体）
            float barY = valueY + 44f + 8f;
            float barWidth = panelWidth - 2f * PowerBarSideMargin;
            float ratio = _mockPowerRecommend > 0f
                ? Mathf.Clamp(_mockPowerCurrent / _mockPowerRecommend, 0f, 1f)
                : 0f;
            _powerBar = new InkBar
            {
                FillVariant = InkBarFillVariant.Vermilion,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(PowerBarSideMargin, barY),
                Size = new Float2(barWidth, PowerBarHeight),
                Value = ratio,
            };
            _martialArtsPanel.AddChild(_powerBar);

            // 达成率百分比 Label（DIN 字体，朱红文字）
            int percent = Mathf.RoundToInt(ratio * 100f);
            _powerPercentLabel = new Label
            {
                Text = $"达成率 {percent}% · 建议提升装备品阶",
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 11f),
                TextColor = InkWashTheme.TextVermilion,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(PowerBarSideMargin, barY + PowerBarHeight + 6f),
                Size = new Float2(barWidth, 18f),
            };
            _martialArtsPanel.AddChild(_powerPercentLabel);
        }

        /// <summary>
        /// SubTask 13.4：药品补给列表。
        /// <see cref="InkPaperPanel"/> 纸色卷轴面板位于右栏，宽度 290。
        /// 内含 <see cref="InkPanelTitle"/>"药品补给"、4 格 <see cref="InkCell"/>（2 列 2 行），
        /// 每格通过 <see cref="InkCell.Badge"/> 显示数量徽章（如"×12"），格下方为药品名 Label。
        /// </summary>
        private void BuildMedicinePanel()
        {
            _medicinePanel = new InkPaperPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
            };
            AddChild(_medicinePanel);

            // 面板标题
            var title = new InkPanelTitle
            {
                Title = "药品补给",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Height = PanelTitleHeight,
            };
            _medicinePanel.AddChild(title);

            // 4 格药品网格（2 列 2 行）
            float gridY = PanelTitleHeight + PanelInnerTopPadding;
            float gridWidth = MedicineColumnCount * MedicineCellSize +
                              (MedicineColumnCount - 1) * MedicineCellGap;
            float gridStartX = (RightColumnWidth - gridWidth) * 0.5f;

            for (int i = 0; i < _mockMedicines.Length; i++)
            {
                var med = _mockMedicines[i];
                int col = i % MedicineColumnCount;
                int row = i / MedicineColumnCount;
                float x = gridStartX + col * (MedicineCellSize + MedicineCellGap);
                float y = gridY + row * MedicineRowStep;

                var cell = new InkCell
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(x, y),
                    Size = new Float2(MedicineCellSize, MedicineCellSize),
                    Quality = med.quality,
                    Badge = $"×{med.quantity}",
                };
                _medicinePanel.AddChild(cell);

                // 药品名 Label（格下方，居中，纸色上文字）
                var nameLabel = new Label
                {
                    Text = med.name,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 11f),
                    TextColor = InkWashTheme.TextOnPaper,
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(x - 8f, y + MedicineCellSize + MedicineNameLabelGap),
                    Size = new Float2(MedicineCellSize + 16f, MedicineNameLabelHeight),
                };
                _medicinePanel.AddChild(nameLabel);
            }

            // 补给充足度汇总
            float gridBottomY = gridY + MedicineRowCount * MedicineRowStep - 12f;
            float summaryY = gridBottomY + MedicineSummaryGap;

            var summaryLabel = new Label
            {
                Text = "补给充足度",
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 11f),
                TextColor = InkWashTheme.PaperDark,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(SideMargin, summaryY),
                Size = new Float2(120f, 24f),
            };
            _medicinePanel.AddChild(summaryLabel);

            var summaryValue = new Label
            {
                Text = "良好",
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 14f),
                TextColor = InkWashTheme.VermilionPrimary,
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(RightColumnWidth - SideMargin - 100f, summaryY),
                Size = new Float2(100f, 24f),
            };
            _medicinePanel.AddChild(summaryValue);
        }

        /// <summary>
        /// 底部操作栏：朱红"出战"按钮，点击触发 <see cref="NavigationRequested"/>("nav-battle-start")。
        /// </summary>
        private void BuildActionBar()
        {
            _battleButton = new InkButton
            {
                Variant = InkButtonVariant.Vermilion,
                ButtonSize = InkButtonSize.Lg,
                Text = "出 战",
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(200f, ActionBarHeight),
            };
            _battleButton.ButtonClicked += OnBattleButtonClicked;
            AddChild(_battleButton);
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

            // 内容区高度（顶部 ContentTop 到底部 sh - ContentBottomReserve）
            float contentHeight = sh - ContentTop - ContentBottomReserve;

            // 三栏 X 坐标
            float leftX = SideMargin;
            float leftW = LeftColumnWidth;
            float rightW = RightColumnWidth;
            float middleX = leftX + leftW + ColumnGap;
            float middleW = sw - 2f * SideMargin - leftW - rightW - 2f * ColumnGap;
            float rightX = middleX + middleW + ColumnGap;

            // 左栏装备面板
            if (_equipmentPanel != null)
            {
                _equipmentPanel.Location = new Float2(leftX, ContentTop);
                _equipmentPanel.Size = new Float2(leftW, contentHeight);
            }

            // 中栏武学面板
            if (_martialArtsPanel != null)
            {
                _martialArtsPanel.Location = new Float2(middleX, ContentTop);
                _martialArtsPanel.Size = new Float2(middleW, contentHeight);
                // 战力 InkBar 宽度随中栏宽度同步
                if (_powerBar != null)
                {
                    float barWidth = middleW - 2f * PowerBarSideMargin;
                    _powerBar.Size = new Float2(barWidth, PowerBarHeight);
                }
                if (_powerPercentLabel != null)
                {
                    float barWidth = middleW - 2f * PowerBarSideMargin;
                    _powerPercentLabel.Size = new Float2(barWidth, 18f);
                }
            }

            // 右栏药品面板
            if (_medicinePanel != null)
            {
                _medicinePanel.Location = new Float2(rightX, ContentTop);
                _medicinePanel.Size = new Float2(rightW, contentHeight);
            }

            // 底部"出战"按钮（水平居中）
            if (_battleButton != null)
            {
                float btnW = 200f;
                float btnX = (sw - btnW) * 0.5f;
                float btnY = sh - ActionBarBottomMargin - ActionBarHeight;
                _battleButton.Location = new Float2(btnX, btnY);
                _battleButton.Size = new Float2(btnW, ActionBarHeight);
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
        // 渲染
        // =======================================================================

        /// <inheritdoc />
        public override void Draw()
        {
            // 1. 深墨黑底色
            var bounds = new Rectangle(0, 0, Width, Height);
            Render2D.FillRectangle(bounds, InkWashTheme.BaseDefault);

            // 2. 朱红战意晕染（左上角，近似 HTML 原型 .prep-bg 径向渐变）
            if (Width > 0f && Height > 0f)
            {
                var vermilionGlow = new Color(
                    InkWashTheme.VermilionPrimary.R,
                    InkWashTheme.VermilionPrimary.G,
                    InkWashTheme.VermilionPrimary.B, 0.09f);
                Render2D.FillRectangle(
                    new Rectangle(0f, 0f, Width * 0.4f, Height * 0.5f),
                    vermilionGlow);

                // 鎏金晕染（右下角）
                var goldGlow = new Color(
                    InkWashTheme.GoldPrimary.R,
                    InkWashTheme.GoldPrimary.G,
                    InkWashTheme.GoldPrimary.B, 0.06f);
                Render2D.FillRectangle(
                    new Rectangle(Width * 0.6f, Height * 0.5f, Width * 0.4f, Height * 0.5f),
                    goldGlow);
            }

            // 3. 绘制子控件
            base.Draw();
        }

        // ===================================================================
        // 事件处理
        // =======================================================================

        /// <summary>
        /// "出战"按钮点击处理：触发 <see cref="NavigationRequested"/>("nav-battle-start")，
        /// 由 <see cref="InkPageRouter"/> 订阅后跳转战斗场景。
        /// </summary>
        /// <param name="button">触发事件的按钮（未使用）</param>
        private void OnBattleButtonClicked(Button button)
        {
            try
            {
                NavigationRequested?.Invoke("nav-battle-start");
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[MenuBattlePrepPage] NavigationRequested(nav-battle-start) 触发失败: {ex.Message}");
            }
        }

        // ===================================================================
        // 辅助方法
        // =======================================================================

        /// <summary>
        /// 将 <see cref="EquipmentData.Quality"/>（0-5）映射到 <see cref="InkWashTheme.InkQuality"/>（0-4），
        /// 5 钳制为 Legendary。
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

using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.StyleSystem;
using System;
using System.Collections.Generic;
using HundunWorld.Game.Equipment;

namespace HundunWorld.Game.UI.Ink.Pages.Combat
{
    /// <summary>
    /// 战前备战页面。
    /// 对应 HTML 原型 <c>menu-battle-prep.html</c>，承载 3 个子区域：
    /// <list type="bullet">
    ///   <item>SubTask 13.2 装备配置面板（8 槽 <see cref="InkCell"/>「图标 + 名称」+ 攻/防/血/内/暴/身 6 项属性加成摘要）</item>
    ///   <item>SubTask 13.3 武学搭配面板（主动 3 张 + 被动 2 张武学卡「图标 + 名称 + 说明 + 主动/被动 + 等级 <see cref="InkTag"/>」+ 战力评估 <see cref="InkBar"/> 进度条与四项数值条）</item>
    ///   <item>SubTask 13.4 药品补给列表（4 条药品行「图标 + 名称 + 效果 + 数量」+ 补给充足度汇总，纸色卷轴面板）</item>
    /// </list>
    /// 顶部标题区显示"备战"主标题、副标题、"强敌当前"朱红标签与"门派"入口，
    /// 左上角返回按钮回战斗 HUD（设计隐藏出口 <c>back-hud</c>），底部操作栏
    /// "调整配置" / "出战" / "快速补给" 三个按钮，其中"出战"通过 <see cref="NavigationRequested"/>
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

        /// <summary>返回按钮尺寸</summary>
        private const float BackButtonSize = 40f;

        /// <summary>返回按钮顶部 Y 坐标</summary>
        private const float BackButtonTop = 20f;

        /// <summary>主标题 X 坐标（返回按钮右侧）</summary>
        private const float HeaderTitleX = 76f;

        /// <summary>主标题区宽度</summary>
        private const float HeaderTitleWidth = 110f;

        /// <summary>副标题 X 坐标</summary>
        private const float HeaderSubtitleX = 190f;

        /// <summary>副标题区宽度</summary>
        private const float HeaderSubtitleWidth = 170f;

        /// <summary>"强敌当前"标签 X 坐标</summary>
        private const float HeaderTagX = 366f;

        /// <summary>头部标签高度</summary>
        private const float HeaderTagHeight = 22f;

        /// <summary>"门派"入口按钮宽度</summary>
        private const float HeaderActionWidth = 88f;

        /// <summary>头部底部分割线 Y 坐标</summary>
        private const float HeaderDividerY = 68f;

        /// <summary>内容区顶部 Y 坐标（标题区下方留白）</summary>
        private const float ContentTop = 82f;

        /// <summary>底部操作栏高度（大号按钮 44，对应设计 .ink-btn-lg）</summary>
        private const float ActionBarHeight = 44f;

        /// <summary>底部操作栏幽灵按钮高度（对应设计 .ink-btn 默认 36）</summary>
        private const float ActionBarGhostHeight = InkWashTheme.ControlHMd;

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

        /// <summary>面板内边距</summary>
        private const float PanelPadding = 16f;

        /// <summary>面板内容区距标题栏的下边距</summary>
        private const float PanelInnerTopPadding = 14f;

        /// <summary>面板内小节标题高度（"已装备 · 八件兵甲"等）</summary>
        private const float SectionLabelHeight = 18f;

        // ---- 装备网格 ----

        /// <summary>装备格间距（设计 .equip-grid gap: 6px）</summary>
        private const float EquipmentCellGap = 6f;

        /// <summary>装备网格列数（8 槽 = 4 列 2 行）</summary>
        private const int EquipmentColumnCount = 4;

        /// <summary>装备网格行数</summary>
        private const int EquipmentRowCount = 2;

        /// <summary>装备格图标 Label 高度</summary>
        private const float EquipmentGlyphHeight = 24f;

        /// <summary>装备格图标 Label 距格顶部的偏移</summary>
        private const float EquipmentGlyphTopInset = 9f;

        /// <summary>装备格名称 Label 高度（对应设计 .slot-name 9px）</summary>
        private const float EquipmentNameHeight = 13f;

        /// <summary>装备格名称 Label 距格底部的偏移</summary>
        private const float EquipmentNameBottomInset = 4f;

        /// <summary>装备属性区与网格的间距</summary>
        private const float EquipStatsGap = 14f;

        /// <summary>装备属性行高度</summary>
        private const float EquipStatRowHeight = 26f;

        /// <summary>装备属性行垂直间距</summary>
        private const float EquipStatRowGap = 6f;

        /// <summary>装备属性行水平列间距</summary>
        private const float EquipStatColumnGap = 12f;

        /// <summary>装备属性行左侧强调竖线宽度</summary>
        private const float EquipStatAccentWidth = 2f;

        /// <summary>装备属性行内文字左边距</summary>
        private const float EquipStatTextLeftMargin = 10f;

        /// <summary>装备属性行数值 Label 宽度</summary>
        private const float EquipStatValueWidth = 62f;

        /// <summary>装备属性小节与网格之间的间距</summary>
        private const float EquipSectionLabelGap = 8f;

        // ---- 武学区 ----

        /// <summary>武学分组标题高度</summary>
        private const float SkillGroupTitleHeight = 22f;

        /// <summary>武学分组标题左侧金竖线宽度</summary>
        private const float SkillGroupBarWidth = 2f;

        /// <summary>武学分组标题左侧金竖线高度</summary>
        private const float SkillGroupBarHeight = 12f;

        /// <summary>武学分组标题与卡片列表的间距</summary>
        private const float SkillGroupTitleGap = 8f;

        /// <summary>武学分组之间的垂直间距</summary>
        private const float SkillGroupGap = 12f;

        /// <summary>武学卡高度</summary>
        private const float SkillCardHeight = 54f;

        /// <summary>武学卡之间的间距</summary>
        private const float SkillCardGap = 6f;

        /// <summary>武学卡图标底座尺寸</summary>
        private const float SkillIconBoxSize = 34f;

        /// <summary>武学卡图标底座左边距</summary>
        private const float SkillIconBoxLeftMargin = 10f;

        /// <summary>武学卡文字区左边距（图标底座右侧）</summary>
        private const float SkillTextLeftMargin = 54f;

        /// <summary>武学卡右侧标签区宽度（主动 + 等级两枚标签 + 间距）</summary>
        private const float SkillTagAreaWidth = 108f;

        /// <summary>武学卡"主动/被动"标签宽度</summary>
        private const float SkillRoleTagWidth = 48f;

        /// <summary>武学卡等级标签宽度</summary>
        private const float SkillLevelTagWidth = 46f;

        /// <summary>武学卡标签高度</summary>
        private const float SkillTagHeight = 20f;

        /// <summary>武学卡标签之间的间距</summary>
        private const float SkillTagGap = 4f;

        /// <summary>武学卡名称 Label 高度</summary>
        private const float SkillNameLabelHeight = 20f;

        /// <summary>武学卡说明 Label 高度</summary>
        private const float SkillDescLabelHeight = 16f;

        // ---- 战力评估 ----

        /// <summary>战力评估区与武学区的间距</summary>
        private const float PowerSectionGap = 14f;

        /// <summary>"战力评估"标题高度</summary>
        private const float PowerTitleHeight = 26f;

        /// <summary>"当前战力 / 推荐战力"提示高度</summary>
        private const float PowerHintLabelHeight = 16f;

        /// <summary>战力数值行高度</summary>
        private const float PowerValueHeight = 44f;

        /// <summary>当前战力 Label 宽度</summary>
        private const float PowerCurrentWidth = 170f;

        /// <summary>分隔符 Label 宽度</summary>
        private const float PowerSeparatorWidth = 20f;

        /// <summary>推荐战力 Label 宽度</summary>
        private const float PowerRecommendWidth = 130f;

        /// <summary>战力数值之间的间距</summary>
        private const float PowerValueGap = 10f;

        /// <summary>战力 InkBar 高度</summary>
        private const float PowerBarHeight = 12f;

        /// <summary>战力达成率提示高度</summary>
        private const float PowerPercentLabelHeight = 18f;

        /// <summary>属性条行高度</summary>
        private const float StatBarRowHeight = 20f;

        /// <summary>属性条行间距</summary>
        private const float StatBarRowGap = 8f;

        /// <summary>属性条名称 Label 宽度</summary>
        private const float StatBarLabelWidth = 36f;

        /// <summary>属性条数值 Label 宽度</summary>
        private const float StatBarValueWidth = 72f;

        /// <summary>属性条名称与轨道之间的间距</summary>
        private const float StatBarLabelGap = 10f;

        /// <summary>属性细条高度（对应设计 .ink-bar-thin 4px）</summary>
        private const float StatBarThinHeight = 6f;

        // ---- 药品区 ----

        /// <summary>药品行高度</summary>
        private const float MedicineRowHeight = 50f;

        /// <summary>药品行间距</summary>
        private const float MedicineRowGap = 7f;

        /// <summary>药品图标底座尺寸</summary>
        private const float MedicineIconBoxSize = 30f;

        /// <summary>药品图标底座左边距</summary>
        private const float MedicineIconBoxLeftMargin = 10f;

        /// <summary>药品文字区左边距</summary>
        private const float MedicineTextLeftMargin = 50f;

        /// <summary>药品数量 Label 宽度</summary>
        private const float MedicineQtyWidth = 46f;

        /// <summary>药品数量 Label 右边距</summary>
        private const float MedicineQtyRightMargin = 10f;

        /// <summary>药品名称 Label 高度</summary>
        private const float MedicineNameLabelHeight = 18f;

        /// <summary>药品效果 Label 高度</summary>
        private const float MedicineEffectLabelHeight = 16f;

        /// <summary>"快速补给"后每种药品补满的数量</summary>
        private const int MedicineFullQuantity = 20;

        /// <summary>补给汇总条高度</summary>
        private const float SupplySummaryHeight = 36f;

        /// <summary>补给汇总条内文字边距</summary>
        private const float SupplySummaryTextMargin = 10f;

        // ---- 底部操作栏 ----

        /// <summary>"调整配置"按钮宽度</summary>
        private const float AdjustButtonWidth = 120f;

        /// <summary>"出战"按钮宽度</summary>
        private const float BattleButtonWidth = 180f;

        /// <summary>"快速补给"按钮宽度</summary>
        private const float SupplyButtonWidth = 120f;

        /// <summary>底部操作栏按钮之间的间距</summary>
        private const float ActionButtonGap = 12f;

        // ===================================================================
        // 子控件引用
        // =======================================================================

        /// <summary>左上角返回按钮（设计隐藏出口 back-hud）</summary>
        private InkBackButton _backButton;

        /// <summary>顶部主标题"备战"</summary>
        private InkTextBlock _headerTitle;

        /// <summary>顶部副标题"战前整备 · 查验兵甲"</summary>
        private InkTextBlock _headerSubtitle;

        /// <summary>顶部"强敌当前"朱红标签</summary>
        private InkTag _dangerTag;

        /// <summary>顶部"门派"入口按钮（设计 nav-sect）</summary>
        private InkButton _sectButton;

        /// <summary>头部底部分割线</summary>
        private InkDivider _headerDivider;

        /// <summary>左栏装备配置面板</summary>
        private InkPanel _equipmentPanel;

        /// <summary>中栏武学搭配面板</summary>
        private InkPanel _martialArtsPanel;

        /// <summary>右栏药品补给面板（纸色卷轴）</summary>
        private InkPaperPanel _medicinePanel;

        /// <summary>底部"出战"按钮</summary>
        private InkButton _battleButton;

        /// <summary>底部"调整配置"按钮</summary>
        private InkButton _adjustButton;

        /// <summary>底部"快速补给"按钮</summary>
        private InkButton _supplyButton;

        /// <summary>底部操作栏顶部分割线</summary>
        private InkDivider _actionBarDivider;

        /// <summary>战力评估 InkBar</summary>
        private InkBar _powerBar;

        /// <summary>达成率百分比 Label</summary>
        private Label _powerPercentLabel;

        /// <summary>"当前战力 / 推荐战力"提示 Label</summary>
        private Label _powerValueHintLabel;

        /// <summary>当前战力数值 Label</summary>
        private Label _powerCurrentLabel;

        /// <summary>战力分隔符 Label</summary>
        private Label _powerSeparatorLabel;

        /// <summary>推荐战力数值 Label</summary>
        private Label _powerRecommendLabel;

        /// <summary>战力评估前分割线</summary>
        private InkDivider _powerDivider;

        /// <summary>装备属性加成数值 Label 列表（6 项，与 <c>_mockEquipStats</c> 同序，供数据绑定刷新）</summary>
        private readonly List<Label> _equipStatValueLabels = new List<Label>();

        /// <summary>中栏武学卡行引用列表（宽度变化时重排）</summary>
        private readonly List<SkillRowRefs> _skillRows = new List<SkillRowRefs>();

        /// <summary>中栏属性条行引用列表（宽度变化时重排）</summary>
        private readonly List<StatBarRefs> _statBarRows = new List<StatBarRefs>();

        /// <summary>右栏药品行引用列表（"快速补给"时刷新数量）</summary>
        private readonly List<MedicineRowRefs> _medicineRows = new List<MedicineRowRefs>();

        /// <summary>补给充足度数值 Label</summary>
        private Label _supplyValueLabel;

        // ===================================================================
        // 屏幕尺寸缓存
        // =======================================================================

        /// <summary>当前屏幕尺寸缓存，用于布局计算</summary>
        private Float2 _screenSize;

        // ===================================================================
        // mock 数据字段
        // =======================================================================

        /// <summary>8 个装备槽 mock 数据：itemId 大于 0 时从 <see cref="EquipmentDatabase.GetEquipment"/> 读取品质</summary>
        private (int itemId, string name, string glyph, InkWashTheme.InkQuality quality)[] _mockEquipments = new[]
        {
            (20001, "玄铁重剑", "剑", InkWashTheme.InkQuality.Legendary),
            (0, "精钢短匕", "匕", InkWashTheme.InkQuality.Rare),
            (30001, "乌金兜鍪", "盔", InkWashTheme.InkQuality.Uncommon),
            (10001, "蟒纹战袍", "袍", InkWashTheme.InkQuality.Epic),
            (0, "玉扣束带", "带", InkWashTheme.InkQuality.Rare),
            (0, "踏云履", "履", InkWashTheme.InkQuality.Uncommon),
            (0, "寒铁护腕", "护", InkWashTheme.InkQuality.Rare),
            (0, "血玉璎珞", "玉", InkWashTheme.InkQuality.Legendary),
        };

        /// <summary>装备属性加成 mock 数据（攻/防/血/内/暴/身 6 项，2 列 3 行）</summary>
        private (string label, string value)[] _mockEquipStats = new[]
        {
            ("攻击", "+1,240"),
            ("防御", "+860"),
            ("气血", "+2,400"),
            ("内力", "+600"),
            ("暴击", "+12%"),
            ("身法", "+8%"),
        };

        /// <summary>主动武学 mock 数据（名称 / 说明 / 等级 / 图标字）</summary>
        private (string name, string desc, string level, string glyph)[] _mockActiveSkills = new[]
        {
            ("狂风快剑", "剑法 · 单体 · 连击三式", "Lv.7", "剑"),
            ("降龙掌", "掌法 · 范围 · 击退", "Lv.6", "掌"),
            ("凌波微步", "身法 · 闪避 · 加速", "Lv.5", "步"),
        };

        /// <summary>被动武学 mock 数据（名称 / 说明 / 等级 / 图标字）</summary>
        private (string name, string desc, string level, string glyph)[] _mockPassiveSkills = new[]
        {
            ("金刚护体", "防御 · 减伤 15%", "Lv.4", "盾"),
            ("吐纳法", "内功 · 回气 +20%", "Lv.6", "息"),
        };

        /// <summary>药品 mock 数据（名称 / 效果 / 图标字 / 数量）</summary>
        private (string name, string effect, string glyph, int quantity)[] _mockMedicines = new[]
        {
            ("回气丹", "恢复内力 300 点", "丹", 12),
            ("金创药", "恢复气血 500 点", "药", 8),
            ("解毒散", "解除中毒状态", "散", 5),
            ("聚神散", "恢复神识 100 点", "神", 3),
        };

        /// <summary>属性条 mock 数据（名称 / 数值 / 比例 / 填充变体）</summary>
        private (string label, string value, float ratio, InkBarFillVariant variant)[] _mockStatBars = new[]
        {
            ("气血", "4,200", 1.00f, InkBarFillVariant.Vermilion),
            ("内力", "1,800", 0.90f, InkBarFillVariant.Gold),
            ("体力", "650", 0.81f, InkBarFillVariant.Jade),
        };

        /// <summary>当前战力 mock 值</summary>
        private float _mockPowerCurrent = 12450f;

        /// <summary>推荐战力 mock 值</summary>
        private float _mockPowerRecommend = 15000f;

        // ===================================================================
        // 数据绑定 API（接入真实数据源时覆盖设计稿示例初值）
        //
        // 页面构造即已按 hundun-yy-ui/pages/menu-battle-prep.html 的示例数据填充
        // 战力评估 / 装备属性加成 / 属性细条 / 药品补给；以下方法供数据源接入后调用，
        // 不调用则始终保留设计稿初值。
        // ===================================================================

        /// <summary>
        /// 绑定战力评估数据（当前战力 / 推荐战力）。
        /// 进度条与"达成率 x%"同步刷新；<paramref name="recommended"/> ≤0 时达成率按 0 处理。
        /// 不调用时保持设计稿初值 12,450 / 15,000（达成率 83%）。
        /// </summary>
        /// <param name="current">当前战力</param>
        /// <param name="recommended">推荐战力</param>
        public void BindCombatPower(int current, int recommended)
        {
            _mockPowerCurrent = current;
            _mockPowerRecommend = recommended;
            RefreshCombatPower();
        }

        /// <summary>
        /// 绑定装备属性加成数值（与设计稿同序：攻击 / 防御 / 气血 / 内力 / 暴击 / 身法）。
        /// 未传入的项保留原值，传入 null 或空数组时不变更。
        /// </summary>
        /// <param name="values">6 项属性加成文本（如 "+1,240"、"+12%"）</param>
        public void BindEquipmentBonus(string[] values)
        {
            if (values == null)
                return;

            for (int i = 0; i < _mockEquipStats.Length && i < values.Length; i++)
            {
                if (string.IsNullOrEmpty(values[i]))
                    continue;

                var stat = _mockEquipStats[i];
                _mockEquipStats[i] = (stat.label, values[i]);

                if (i < _equipStatValueLabels.Count && _equipStatValueLabels[i] != null)
                    _equipStatValueLabels[i].Text = values[i];
            }
        }

        /// <summary>
        /// 绑定气血 / 内力 / 体力三条属性细条的数值与上限（比例 = 数值 / 上限，上限 ≤0 时按 0 处理）。
        /// 不调用时保持设计稿初值 4,200 / 1,800 / 650。
        /// </summary>
        /// <param name="hp">气血当前值</param>
        /// <param name="hpMax">气血上限</param>
        /// <param name="mp">内力当前值</param>
        /// <param name="mpMax">内力上限</param>
        /// <param name="stamina">体力当前值</param>
        /// <param name="staminaMax">体力上限</param>
        public void BindStatBars(int hp, int hpMax, int mp, int mpMax, int stamina, int staminaMax)
        {
            _mockStatBars[0] = ("气血", hp.ToString("N0"), Ratio(hp, hpMax), InkBarFillVariant.Vermilion);
            _mockStatBars[1] = ("内力", mp.ToString("N0"), Ratio(mp, mpMax), InkBarFillVariant.Gold);
            _mockStatBars[2] = ("体力", stamina.ToString("N0"), Ratio(stamina, staminaMax), InkBarFillVariant.Jade);
            RefreshStatBars();
        }

        /// <summary>
        /// 绑定药品数量（与设计稿同序：回气丹 / 金创药 / 解毒散 / 聚神散），并刷新每行数量角标。
        /// 不调用时保持设计稿初值 12 / 8 / 5 / 3。
        /// </summary>
        /// <param name="quantities">各药品数量，未传入的项保留原值</param>
        public void BindSupplies(int[] quantities)
        {
            if (quantities == null)
                return;

            for (int i = 0; i < _mockMedicines.Length && i < quantities.Length; i++)
            {
                var med = _mockMedicines[i];
                _mockMedicines[i] = (med.name, med.effect, med.glyph, quantities[i]);

                if (i < _medicineRows.Count && _medicineRows[i].QtyLabel != null)
                    _medicineRows[i].QtyLabel.Text = $"×{quantities[i]}";
            }
        }

        /// <summary>数值 / 上限 的填充比例（上限 ≤0 时返回 0）。</summary>
        private static float Ratio(int value, int max)
        {
            return max > 0 ? Mathf.Clamp(value / (float)max, 0f, 1f) : 0f;
        }

        /// <summary>把战力数据写入数值 Label、进度条与达成率提示。</summary>
        private void RefreshCombatPower()
        {
            float ratio = _mockPowerRecommend > 0f
                ? Mathf.Clamp(_mockPowerCurrent / _mockPowerRecommend, 0f, 1f)
                : 0f;

            if (_powerCurrentLabel != null)
                _powerCurrentLabel.Text = ((int)_mockPowerCurrent).ToString("N0");
            if (_powerRecommendLabel != null)
                _powerRecommendLabel.Text = ((int)_mockPowerRecommend).ToString("N0");
            if (_powerBar != null)
                _powerBar.Value = ratio;
            if (_powerPercentLabel != null)
                _powerPercentLabel.Text = $"达成率 {Mathf.RoundToInt(ratio * 100f)}% · 建议提升装备品阶";
        }

        /// <summary>把属性细条数据写入数值 Label 与进度条。</summary>
        private void RefreshStatBars()
        {
            for (int i = 0; i < _statBarRows.Count && i < _mockStatBars.Length; i++)
            {
                var stat = _mockStatBars[i];
                var row = _statBarRows[i];

                if (row.Value != null)
                    row.Value.Text = stat.value;
                if (row.Bar != null)
                    row.Bar.Value = stat.ratio;
            }
        }

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
        /// 顶部标题区：返回按钮（隐藏出口 back-hud）+ 主标题"备战"
        /// （<see cref="InkTextStyle.Display"/>）+ 副标题"战前整备 · 查验兵甲"
        /// （<see cref="InkTextStyle.Subheading"/>）+ "强敌当前"朱红标签 +
        /// 右侧"门派"幽灵按钮（导航 nav-sect）+ 底部分割线。
        /// </summary>
        private void BuildHeader()
        {
            // 返回按钮（设计 .ink-back-btn，40x40 金线描边）
            _backButton = new InkBackButton
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(BackButtonTop, BackButtonTop),
                Size = new Float2(BackButtonSize, BackButtonSize),
            };
            _backButton.Clicked += OnBackButtonClicked;
            AddChild(_backButton);

            // 主标题"备战"
            _headerTitle = new InkTextBlock(InkTextStyle.Display)
            {
                Text = "备战",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(HeaderTitleX, 14f),
                Size = new Float2(HeaderTitleWidth, 46f),
                HorizontalAlignment = TextAlignment.Near,
            };
            AddChild(_headerTitle);

            // 副标题"战前整备 · 查验兵甲"
            _headerSubtitle = new InkTextBlock(InkTextStyle.Subheading)
            {
                Text = "战前整备 · 查验兵甲",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 13f),
                TextColor = InkWashTheme.TextTertiary,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(HeaderSubtitleX, 32f),
                Size = new Float2(HeaderSubtitleWidth, HeaderTagHeight),
                HorizontalAlignment = TextAlignment.Near,
            };
            AddChild(_headerSubtitle);

            // "强敌当前"朱红标签（设计 .ink-tag-vermilion + alert 图标）
            _dangerTag = new InkTag
            {
                Text = "强敌当前",
                TagVariant = InkTagVariant.Vermilion,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(HeaderTagX, 32f),
                Size = new Float2(96f, HeaderTagHeight),
            };
            AddChild(_dangerTag);

            // "门派"入口按钮（设计 nav-sect，幽灵小号）
            _sectButton = new InkButton
            {
                Text = "门派",
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Sm,
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(HeaderActionWidth, InkWashTheme.ControlHSm),
            };
            _sectButton.ButtonClicked += OnSectButtonClicked;
            AddChild(_sectButton);

            // 头部底部分割线
            _headerDivider = new InkDivider
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(SideMargin, HeaderDividerY),
                Size = new Float2(_screenSize.X - 2f * SideMargin, 1f),
            };
            AddChild(_headerDivider);
        }

        /// <summary>
        /// SubTask 13.2：装备配置面板。
        /// <see cref="InkPanel"/> 容器位于左栏，宽度 310。
        /// 内含 <see cref="InkPanelTitle"/>"装备配置"、"已装备 · 八件兵甲"小节标题、
        /// 8 槽 <see cref="InkCell"/> 网格（4 列 2 行，品质描边 + 图标字 + 名称），
        /// 以及攻/防/血/内/暴/身 6 项属性加成摘要（2 列 3 行，左侧强调线）。
        /// 装备格通过 <see cref="EquipmentDatabase.GetEquipment"/> 读取已装备物品品质
        /// （itemId 大于 0 时），未命中则使用 mock 品质。
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

            // "已装备 · 八件兵甲" 小节标题
            float labelY = PanelTitleHeight + PanelInnerTopPadding;
            _equipmentPanel.AddChild(CreateSectionLabel("已装备 · 八件兵甲", PanelPadding, labelY));

            // 装备网格 4x2（格宽 = 面板内宽均分，对应设计 aspect-ratio: 1）
            float contentWidth = LeftColumnWidth - 2f * PanelPadding;
            float cellSize = (contentWidth - (EquipmentColumnCount - 1) * EquipmentCellGap) / EquipmentColumnCount;
            float gridY = labelY + SectionLabelHeight + EquipSectionLabelGap;

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
                float x = PanelPadding + col * (cellSize + EquipmentCellGap);
                float y = gridY + row * (cellSize + EquipmentCellGap);

                var cell = new InkCell
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(x, y),
                    Size = new Float2(cellSize, cellSize),
                    Quality = quality,
                };
                _equipmentPanel.AddChild(cell);

                // 槽位图标字（设计 .slot-icon，传世用朱红亮色、其余用品质色）
                bool legendary = quality == InkWashTheme.InkQuality.Legendary;
                var glyphLabel = new Label
                {
                    Text = eq.glyph,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 16f),
                    TextColor = legendary
                        ? InkWashTheme.VermilionBright
                        : InkWashTheme.QualityColor(quality),
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(x, y + EquipmentGlyphTopInset),
                    Size = new Float2(cellSize, EquipmentGlyphHeight),
                };
                _equipmentPanel.AddChild(glyphLabel);

                // 槽位名称（设计 .slot-name，传世用朱红文字、其余三级文字色）
                var nameLabel = new Label
                {
                    Text = eq.name,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 9f),
                    TextColor = legendary ? InkWashTheme.TextVermilion : InkWashTheme.TextTertiary,
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(x, y + cellSize - EquipmentNameHeight - EquipmentNameBottomInset),
                    Size = new Float2(cellSize, EquipmentNameHeight),
                };
                _equipmentPanel.AddChild(nameLabel);
            }

            // "装备属性加成" 小节标题
            float gridHeight = EquipmentRowCount * cellSize + (EquipmentRowCount - 1) * EquipmentCellGap;
            float statsLabelY = gridY + gridHeight + EquipStatsGap;
            _equipmentPanel.AddChild(CreateSectionLabel("装备属性加成", PanelPadding, statsLabelY));

            // 6 项属性行（2 列 3 行，对应设计 .equip-stats 网格）
            float statColumnWidth = (contentWidth - EquipStatColumnGap) * 0.5f;
            float rowStartY = statsLabelY + SectionLabelHeight + EquipSectionLabelGap;
            for (int i = 0; i < _mockEquipStats.Length; i++)
            {
                var stat = _mockEquipStats[i];
                int col = i % 2;
                int row = i / 2;
                float rowX = PanelPadding + col * (statColumnWidth + EquipStatColumnGap);
                float rowY = rowStartY + row * (EquipStatRowHeight + EquipStatRowGap);

                _equipStatValueLabels.Add(BuildEquipStatRow(stat.label, stat.value, rowX, rowY, statColumnWidth));
            }
        }

        /// <summary>
        /// 构建一行装备属性加成（左侧强调竖线 + 属性名 + DIN 数值），
        /// 对应设计 .equip-stat-row。
        /// </summary>
        /// <param name="label">属性名（如"攻击"）</param>
        /// <param name="value">属性数值文本（如"+1,240"）</param>
        /// <param name="x">行左边距（面板内坐标）</param>
        /// <param name="y">行顶部 Y 坐标（面板内坐标）</param>
        /// <param name="width">行宽度</param>
        /// <returns>该行的数值 Label（留存用于数据绑定刷新）</returns>
        private Label BuildEquipStatRow(string label, string value, float x, float y, float width)
        {
            var row = new PrepBox
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(x, y),
                Size = new Float2(width, EquipStatRowHeight),
                CornerRadius = InkWashTheme.RadiusMd,
                FillColor = InkWashTheme.ShadowSoft,
                AccentWidth = EquipStatAccentWidth,
                AccentColor = InkWashTheme.BorderNeutralL2,
            };
            _equipmentPanel.AddChild(row);

            var nameLabel = new Label
            {
                Text = label,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 11f),
                TextColor = InkWashTheme.TextSecondary,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(EquipStatTextLeftMargin, 0f),
                Size = new Float2(width - EquipStatTextLeftMargin - EquipStatValueWidth - 6f, EquipStatRowHeight),
            };
            row.AddChild(nameLabel);

            var valueLabel = new Label
            {
                Text = value,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 13f),
                TextColor = InkWashTheme.TextBrand,
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(width - EquipStatValueWidth - 6f, 0f),
                Size = new Float2(EquipStatValueWidth, EquipStatRowHeight),
            };            row.AddChild(valueLabel);
            return valueLabel;
        }




        /// <summary>
        /// SubTask 13.3：武学搭配面板 + 战力评估。
        /// <see cref="InkPanel"/> 容器位于中栏。内含 <see cref="InkPanelTitle"/>"武学搭配"、
        /// 主动武学 3 张卡 + 被动武学 2 张卡（图标底座 + 名称 + 说明 + 主动/被动与等级标签）、
        /// 分隔线，以及战力评估区：当前/推荐战力数值（DIN 字体）+
        /// <see cref="InkBar"/> 朱红进度条 + 达成率提示 + 气血/内力/体力 3 条属性细条。
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
            cursorY = BuildSkillGroup("主动武学", "主动", _mockActiveSkills, true, cursorY);

            // 被动武学区
            cursorY += SkillGroupGap;
            cursorY = BuildSkillGroup("被动武学", "被动", _mockPassiveSkills, false, cursorY);

            // 战力评估区
            BuildPowerAssessment(cursorY + PowerSectionGap);
        }

        /// <summary>
        /// 构建一个武学分组（金竖线小节标题 + 若干武学卡）。
        /// </summary>
        /// <param name="groupTitle">分组标题（如"主动武学"）</param>
        /// <param name="roleTag">卡上角色标签文本（"主动" / "被动"）</param>
        /// <param name="skills">武学 mock 数据数组</param>
        /// <param name="active">是否为主动武学（决定图标/标签配色）</param>
        /// <param name="startY">分组起始 Y 坐标（面板内）</param>
        /// <returns>分组底部 Y 坐标（供下一区域使用）</returns>
        private float BuildSkillGroup(string groupTitle, string roleTag,
            (string name, string desc, string level, string glyph)[] skills, bool active, float startY)
        {
            // 分组标题前置金竖线（设计 .skill-group-title::before）
            var groupBar = new PrepBox
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(PanelPadding, startY + (SkillGroupTitleHeight - SkillGroupBarHeight) * 0.5f),
                Size = new Float2(SkillGroupBarWidth, SkillGroupBarHeight),
                CornerRadius = InkWashTheme.RadiusSm,
                FillColor = InkWashTheme.GoldPrimary,
            };
            _martialArtsPanel.AddChild(groupBar);

            var groupLabel = new Label
            {
                Text = groupTitle,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 11f),
                TextColor = InkWashTheme.TextTertiary,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(PanelPadding + SkillGroupBarWidth + 8f, startY),
                Size = new Float2(220f, SkillGroupTitleHeight),
            };
            _martialArtsPanel.AddChild(groupLabel);

            float cardY = startY + SkillGroupTitleHeight + SkillGroupTitleGap;
            for (int i = 0; i < skills.Length; i++)
            {
                BuildSkillCard(skills[i], roleTag, active, cardY);
                cardY += SkillCardHeight + SkillCardGap;
            }

            return cardY - SkillCardGap;
        }

        /// <summary>
        /// 构建一张武学卡：图标底座（图标字）+ 名称 + 说明 + 角色/等级标签，
        /// 对应设计 .skill-card。卡片宽度在中栏重排时由
        /// <see cref="ApplyMartialArtsInnerLayout"/> 统一设置。
        /// </summary>
        /// <param name="skill">武学数据（名称 / 说明 / 等级 / 图标字）</param>
        /// <param name="roleTag">角色标签文本（"主动" / "被动"）</param>
        /// <param name="active">是否为主动武学</param>
        /// <param name="y">卡片顶部 Y 坐标（面板内）</param>
        private void BuildSkillCard((string name, string desc, string level, string glyph) skill,
            string roleTag, bool active, float y)
        {
            var card = new PrepBox
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(PanelPadding, y),
                Size = new Float2(320f, SkillCardHeight),
                CornerRadius = InkWashTheme.RadiusMd,
                FillColor = InkWashTheme.ShadowSoft,
                BorderColor = InkWashTheme.BorderNeutralL2,
                BorderThickness = 1f,
            };
            _martialArtsPanel.AddChild(card);

            // 图标底座（主动=朱红，被动=鎏金）
            var iconBox = new PrepBox
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(SkillIconBoxLeftMargin, (SkillCardHeight - SkillIconBoxSize) * 0.5f),
                Size = new Float2(SkillIconBoxSize, SkillIconBoxSize),
                CornerRadius = InkWashTheme.RadiusMd,
                FillColor = WithAlpha(active ? InkWashTheme.VermilionPrimary : InkWashTheme.GoldPrimary, 0.10f),
                BorderColor = active ? InkWashTheme.BorderVermilion : InkWashTheme.BorderGold,
                BorderThickness = 1f,
            };
            card.AddChild(iconBox);

            var glyphLabel = new Label
            {
                Text = skill.glyph,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 15f),
                TextColor = active ? InkWashTheme.TextVermilion : InkWashTheme.TextBrand,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Float2(SkillIconBoxSize, SkillIconBoxSize),
            };
            iconBox.AddChild(glyphLabel);

            // 武学名
            var nameLabel = new Label
            {
                Text = skill.name,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 13f),
                TextColor = InkWashTheme.TextDefault,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(SkillTextLeftMargin, 8f),
                Size = new Float2(180f, SkillNameLabelHeight),
            };
            card.AddChild(nameLabel);

            // 武学说明（如"剑法 · 单体 · 连击三式"）
            var descLabel = new Label
            {
                Text = skill.desc,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f),
                TextColor = InkWashTheme.TextTertiary,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(SkillTextLeftMargin, 28f),
                Size = new Float2(180f, SkillDescLabelHeight),
            };
            card.AddChild(descLabel);

            // 角色标签（主动=朱红，被动=默认）
            var roleLabel = new InkTag
            {
                Text = roleTag,
                TagVariant = active ? InkTagVariant.Vermilion : InkTagVariant.Default,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(220f, (SkillCardHeight - SkillTagHeight) * 0.5f),
                Size = new Float2(SkillRoleTagWidth, SkillTagHeight),
            };
            card.AddChild(roleLabel);

            // 等级标签
            var levelLabel = new InkTag
            {
                Text = skill.level,
                TagVariant = InkTagVariant.Brand,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(220f + SkillRoleTagWidth + SkillTagGap, (SkillCardHeight - SkillTagHeight) * 0.5f),
                Size = new Float2(SkillLevelTagWidth, SkillTagHeight),
            };
            card.AddChild(levelLabel);

            _skillRows.Add(new SkillRowRefs
            {
                Box = card,
                NameLabel = nameLabel,
                DescLabel = descLabel,
                RoleTag = roleLabel,
                LevelTag = levelLabel,
            });
        }

        /// <summary>
        /// 构建战力评估区："战力评估"标题 + "当前战力 / 推荐战力"提示 +
        /// 当前/推荐战力数值（DIN 字体）+ <see cref="InkBar"/> 朱红进度条 +
        /// 达成率提示 + 气血/内力/体力 3 条属性细条。
        /// </summary>
        /// <param name="startY">战力评估区起始 Y 坐标（面板内）</param>
        private void BuildPowerAssessment(float startY)
        {
            // 分割线（设计 .ink-divider）
            _powerDivider = new InkDivider
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(PanelPadding, startY),
                Size = new Float2(320f, 1f),
            };
            _martialArtsPanel.AddChild(_powerDivider);

            float cursorY = startY + 14f;

            // "战力评估" 标题
            var powerTitle = new Label
            {
                Text = "战力评估",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 15f),
                TextColor = InkWashTheme.TextBrand,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(PanelPadding, cursorY),
                Size = new Float2(220f, PowerTitleHeight),
            };
            _martialArtsPanel.AddChild(powerTitle);
            cursorY += PowerTitleHeight;

            // "当前战力 / 推荐战力" 提示
            _powerValueHintLabel = new Label
            {
                Text = "当前战力 / 推荐战力",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 10f),
                TextColor = InkWashTheme.TextTertiary,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(PanelPadding, cursorY),
                Size = new Float2(320f, PowerHintLabelHeight),
            };
            _martialArtsPanel.AddChild(_powerValueHintLabel);
            cursorY += PowerHintLabelHeight;

            // 战力数值显示：当前 / 推荐（三 Label 横排居中）
            _powerCurrentLabel = new Label
            {
                Text = ((int)_mockPowerCurrent).ToString("N0"),
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 38f),
                TextColor = InkWashTheme.VermilionBright,
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, cursorY),
                Size = new Float2(PowerCurrentWidth, PowerValueHeight),
            };
            _martialArtsPanel.AddChild(_powerCurrentLabel);

            _powerSeparatorLabel = new Label
            {
                Text = "/",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 14f),
                TextColor = InkWashTheme.TextTertiary,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(PowerCurrentWidth + PowerValueGap, cursorY),
                Size = new Float2(PowerSeparatorWidth, PowerValueHeight),
            };
            _martialArtsPanel.AddChild(_powerSeparatorLabel);

            _powerRecommendLabel = new Label
            {
                Text = ((int)_mockPowerRecommend).ToString("N0"),
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 22f),
                TextColor = InkWashTheme.TextSecondary,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(
                    PowerCurrentWidth + PowerValueGap + PowerSeparatorWidth + PowerValueGap, cursorY),
                Size = new Float2(PowerRecommendWidth, PowerValueHeight),
            };
            _martialArtsPanel.AddChild(_powerRecommendLabel);
            cursorY += PowerValueHeight + 8f;

            // 战力 InkBar（朱红变体）
            float ratio = _mockPowerRecommend > 0f
                ? Mathf.Clamp(_mockPowerCurrent / _mockPowerRecommend, 0f, 1f)
                : 0f;
            _powerBar = new InkBar
            {
                FillVariant = InkBarFillVariant.Vermilion,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(PanelPadding, cursorY),
                Size = new Float2(320f, PowerBarHeight),
                Value = ratio,
            };
            _martialArtsPanel.AddChild(_powerBar);
            cursorY += PowerBarHeight + 6f;

            // 达成率提示（朱红文字）
            int percent = Mathf.RoundToInt(ratio * 100f);
            _powerPercentLabel = new Label
            {
                Text = $"达成率 {percent}% · 建议提升装备品阶",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 11f),
                TextColor = InkWashTheme.TextVermilion,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(PanelPadding, cursorY),
                Size = new Float2(320f, PowerPercentLabelHeight),
            };
            _martialArtsPanel.AddChild(_powerPercentLabel);
            cursorY += PowerPercentLabelHeight + 14f;

            // 气血 / 内力 / 体力 属性细条
            for (int i = 0; i < _mockStatBars.Length; i++)
            {
                var stat = _mockStatBars[i];
                float rowY = cursorY + i * (StatBarRowHeight + StatBarRowGap);

                var statLabel = new Label
                {
                    Text = stat.label,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 11f),
                    TextColor = InkWashTheme.TextSecondary,
                    HorizontalAlignment = TextAlignment.Far,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(PanelPadding, rowY),
                    Size = new Float2(StatBarLabelWidth, StatBarRowHeight),
                };
                _martialArtsPanel.AddChild(statLabel);

                var statBar = new InkBar
                {
                    FillVariant = stat.variant,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(
                        PanelPadding + StatBarLabelWidth + StatBarLabelGap,
                        rowY + (StatBarRowHeight - StatBarThinHeight) * 0.5f),
                    Size = new Float2(160f, StatBarThinHeight),
                    Value = stat.ratio,
                };
                _martialArtsPanel.AddChild(statBar);

                var statValue = new Label
                {
                    Text = stat.value,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 11f),
                    TextColor = InkWashTheme.TextBrand,
                    HorizontalAlignment = TextAlignment.Far,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(240f, rowY),
                    Size = new Float2(StatBarValueWidth, StatBarRowHeight),
                };
                _martialArtsPanel.AddChild(statValue);

                _statBarRows.Add(new StatBarRefs
                {
                    Label = statLabel,
                    Bar = statBar,
                    Value = statValue,
                });
            }
        }

        /// <summary>
        /// SubTask 13.4：药品补给列表。
        /// <see cref="InkPaperPanel"/> 纸色卷轴面板位于右栏，宽度 290。
        /// 内含 <see cref="InkPanelTitle"/>"药品补给"、4 条药品行
        /// （图标底座 + 名称 + 效果说明 + 数量）、分割线，以及"补给充足度"汇总条。
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

            // 4 条药品行
            float contentWidth = RightColumnWidth - 2f * PanelPadding;
            float rowY = PanelTitleHeight + PanelInnerTopPadding;
            for (int i = 0; i < _mockMedicines.Length; i++)
            {
                BuildMedicineRow(_mockMedicines[i], rowY, contentWidth);
                rowY += MedicineRowHeight + MedicineRowGap;
            }
            rowY -= MedicineRowGap;

            // 分割线
            var divider = new InkDivider
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(PanelPadding, rowY + 14f),
                Size = new Float2(contentWidth, 1f),
            };
            _medicinePanel.AddChild(divider);

            // "补给充足度"汇总条
            float summaryY = rowY + 14f + 14f;
            var summaryBox = new PrepBox
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(PanelPadding, summaryY),
                Size = new Float2(contentWidth, SupplySummaryHeight),
                CornerRadius = InkWashTheme.RadiusMd,
                FillColor = WithAlpha(InkWashTheme.PaperDark, 0.10f),
            };
            _medicinePanel.AddChild(summaryBox);

            var summaryLabel = new Label
            {
                Text = "补给充足度",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 11f),
                TextColor = InkWashTheme.PaperDark,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(SupplySummaryTextMargin, 0f),
                Size = new Float2(140f, SupplySummaryHeight),
            };
            summaryBox.AddChild(summaryLabel);

            _supplyValueLabel = new Label
            {
                Text = "良好",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 14f),
                TextColor = InkWashTheme.VermilionPrimary,
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(contentWidth - 100f - SupplySummaryTextMargin, 0f),
                Size = new Float2(100f, SupplySummaryHeight),
            };
            summaryBox.AddChild(_supplyValueLabel);
        }

        /// <summary>
        /// 构建一条药品行：图标底座（图标字）+ 名称 + 效果说明 + 数量，
        /// 对应设计 .medicine-item。
        /// </summary>
        /// <param name="medicine">药品数据（名称 / 效果 / 图标字 / 数量）</param>
        /// <param name="y">行顶部 Y 坐标（面板内）</param>
        /// <param name="width">行宽度</param>
        private void BuildMedicineRow((string name, string effect, string glyph, int quantity) medicine,
            float y, float width)
        {
            var row = new PrepBox
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(PanelPadding, y),
                Size = new Float2(width, MedicineRowHeight),
                CornerRadius = InkWashTheme.RadiusMd,
                FillColor = WithAlpha(InkWashTheme.PaperDark, 0.12f),
                BorderColor = InkWashTheme.PaperPanelBorder,
                BorderThickness = 1f,
            };
            _medicinePanel.AddChild(row);

            // 图标底座（朱红描边，对应设计 .medicine-icon）
            var iconBox = new PrepBox
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(MedicineIconBoxLeftMargin, (MedicineRowHeight - MedicineIconBoxSize) * 0.5f),
                Size = new Float2(MedicineIconBoxSize, MedicineIconBoxSize),
                CornerRadius = InkWashTheme.RadiusMd,
                FillColor = WithAlpha(InkWashTheme.VermilionPrimary, 0.10f),
                BorderColor = InkWashTheme.BorderVermilion,
                BorderThickness = 1f,
            };
            row.AddChild(iconBox);

            var glyphLabel = new Label
            {
                Text = medicine.glyph,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 14f),
                TextColor = InkWashTheme.VermilionPrimary,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Float2(MedicineIconBoxSize, MedicineIconBoxSize),
            };
            iconBox.AddChild(glyphLabel);

            float textWidth = width - MedicineTextLeftMargin - MedicineQtyWidth - MedicineQtyRightMargin;

            var nameLabel = new Label
            {
                Text = medicine.name,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 13f),
                TextColor = InkWashTheme.TextOnPaper,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(MedicineTextLeftMargin, 8f),
                Size = new Float2(textWidth, MedicineNameLabelHeight),
            };
            row.AddChild(nameLabel);

            var effectLabel = new Label
            {
                Text = medicine.effect,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f),
                TextColor = InkWashTheme.PaperDark,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(MedicineTextLeftMargin, 26f),
                Size = new Float2(textWidth, MedicineEffectLabelHeight),
            };
            row.AddChild(effectLabel);

            var qtyLabel = new Label
            {
                Text = $"×{medicine.quantity}",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 16f),
                TextColor = InkWashTheme.VermilionPrimary,
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(width - MedicineQtyWidth - MedicineQtyRightMargin, 0f),
                Size = new Float2(MedicineQtyWidth, MedicineRowHeight),
            };
            row.AddChild(qtyLabel);

            _medicineRows.Add(new MedicineRowRefs
            {
                Box = row,
                NameLabel = nameLabel,
                EffectLabel = effectLabel,
                QtyLabel = qtyLabel,
            });
        }

        /// <summary>
        /// 底部操作栏（设计 .prep-action-bar）：顶部分割线 +
        /// "调整配置"（幽灵）+"出 战"（朱红大号）+"快速补给"（幽灵）三按钮水平居中。
        /// </summary>
        private void BuildActionBar()
        {
            _actionBarDivider = new InkDivider
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(_screenSize.X - 2f * SideMargin, 1f),
            };
            AddChild(_actionBarDivider);

            _adjustButton = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Md,
                Text = "调整配置",
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(AdjustButtonWidth, ActionBarGhostHeight),
            };
            _adjustButton.ButtonClicked += OnAdjustConfigClicked;
            AddChild(_adjustButton);

            _battleButton = new InkButton
            {
                Variant = InkButtonVariant.Vermilion,
                ButtonSize = InkButtonSize.Lg,
                Text = "出 战",
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(BattleButtonWidth, ActionBarHeight),
            };
            _battleButton.ButtonClicked += OnBattleButtonClicked;
            AddChild(_battleButton);

            _supplyButton = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Md,
                Text = "快速补给",
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(SupplyButtonWidth, ActionBarGhostHeight),
            };
            _supplyButton.ButtonClicked += OnQuickSupplyClicked;
            AddChild(_supplyButton);
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

            // ---- 顶部标题区 ----
            if (_backButton != null)
            {
                _backButton.Location = new Float2(BackButtonTop, BackButtonTop);
            }
            if (_sectButton != null)
            {
                _sectButton.Location = new Float2(
                    sw - SideMargin - HeaderActionWidth,
                    (HeaderDividerY - InkWashTheme.ControlHSm) * 0.5f);
            }
            if (_headerDivider != null)
            {
                _headerDivider.Location = new Float2(SideMargin, HeaderDividerY);
                _headerDivider.Size = new Float2(sw - 2f * SideMargin, 1f);
            }

            // ---- 三栏 X 坐标 ----
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
                ApplyMartialArtsInnerLayout(middleW);
            }

            // 右栏药品面板
            if (_medicinePanel != null)
            {
                _medicinePanel.Location = new Float2(rightX, ContentTop);
                _medicinePanel.Size = new Float2(rightW, contentHeight);
            }

            // ---- 底部操作栏（水平居中）----
            float totalBarWidth = AdjustButtonWidth + BattleButtonWidth + SupplyButtonWidth +
                                  2f * ActionButtonGap;
            float barX = (sw - totalBarWidth) * 0.5f;
            float barY = sh - ActionBarBottomMargin - ActionBarHeight;

            if (_actionBarDivider != null)
            {
                _actionBarDivider.Location = new Float2(SideMargin, barY - 12f);
                _actionBarDivider.Size = new Float2(sw - 2f * SideMargin, 1f);
            }
            if (_adjustButton != null)
            {
                _adjustButton.Location = new Float2(barX, barY + (ActionBarHeight - ActionBarGhostHeight) * 0.5f);
                _adjustButton.Size = new Float2(AdjustButtonWidth, ActionBarGhostHeight);
            }
            if (_battleButton != null)
            {
                _battleButton.Location = new Float2(barX + AdjustButtonWidth + ActionButtonGap, barY);
                _battleButton.Size = new Float2(BattleButtonWidth, ActionBarHeight);
            }
            if (_supplyButton != null)
            {
                _supplyButton.Location = new Float2(
                    barX + AdjustButtonWidth + ActionButtonGap + BattleButtonWidth + ActionButtonGap,
                    barY + (ActionBarHeight - ActionBarGhostHeight) * 0.5f);
                _supplyButton.Size = new Float2(SupplyButtonWidth, ActionBarGhostHeight);
            }
        }

        /// <summary>
        /// 中栏（武学搭配）内部随中栏宽度变化的元素重排：
        /// 武学卡宽度与卡内文字/标签、战力数值居中、战力条与属性条宽度。
        /// </summary>
        /// <param name="middleW">中栏面板宽度</param>
        private void ApplyMartialArtsInnerLayout(float middleW)
        {
            float contentWidth = Mathf.Max(160f, middleW - 2f * PanelPadding);

            // 武学卡：宽度铺满 + 文字区与标签右对齐
            for (int i = 0; i < _skillRows.Count; i++)
            {
                var row = _skillRows[i];
                if (row.Box != null)
                {
                    row.Box.Size = new Float2(contentWidth, SkillCardHeight);
                }

                float textWidth = Mathf.Max(60f, contentWidth - SkillTextLeftMargin - SkillTagAreaWidth);
                if (row.NameLabel != null)
                {
                    row.NameLabel.Size = new Float2(textWidth, SkillNameLabelHeight);
                }
                if (row.DescLabel != null)
                {
                    row.DescLabel.Size = new Float2(textWidth, SkillDescLabelHeight);
                }

                float roleX = contentWidth - SkillTagAreaWidth;
                if (row.RoleTag != null)
                {
                    row.RoleTag.Location = new Float2(roleX, (SkillCardHeight - SkillTagHeight) * 0.5f);
                }
                if (row.LevelTag != null)
                {
                    row.LevelTag.Location = new Float2(
                        roleX + SkillRoleTagWidth + SkillTagGap,
                        (SkillCardHeight - SkillTagHeight) * 0.5f);
                }
            }

            // 战力数值行（整体居中）
            float totalValueW = PowerCurrentWidth + PowerValueGap + PowerSeparatorWidth +
                                PowerValueGap + PowerRecommendWidth;
            float valueStartX = (middleW - totalValueW) * 0.5f;
            if (_powerCurrentLabel != null)
            {
                _powerCurrentLabel.Location = new Float2(valueStartX, _powerCurrentLabel.Location.Y);
            }
            if (_powerSeparatorLabel != null)
            {
                _powerSeparatorLabel.Location = new Float2(
                    valueStartX + PowerCurrentWidth + PowerValueGap, _powerSeparatorLabel.Location.Y);
            }
            if (_powerRecommendLabel != null)
            {
                _powerRecommendLabel.Location = new Float2(
                    valueStartX + PowerCurrentWidth + PowerValueGap + PowerSeparatorWidth + PowerValueGap,
                    _powerRecommendLabel.Location.Y);
            }

            // 战力条 / 达成率提示 / 分割线
            if (_powerBar != null)
            {
                _powerBar.Size = new Float2(contentWidth, PowerBarHeight);
            }
            if (_powerPercentLabel != null)
            {
                _powerPercentLabel.Size = new Float2(contentWidth, PowerPercentLabelHeight);
            }
            if (_powerValueHintLabel != null)
            {
                _powerValueHintLabel.Size = new Float2(contentWidth, PowerHintLabelHeight);
            }
            if (_powerDivider != null)
            {
                _powerDivider.Size = new Float2(contentWidth, 1f);
            }

            // 属性细条：名称 + 轨道（自适应）+ 数值
            float barWidth = Mathf.Max(
                40f,
                contentWidth - StatBarLabelWidth - StatBarLabelGap - StatBarValueWidth - StatBarLabelGap);
            for (int i = 0; i < _statBarRows.Count; i++)
            {
                var row = _statBarRows[i];
                if (row.Bar != null)
                {
                    row.Bar.Size = new Float2(barWidth, StatBarThinHeight);
                }
                if (row.Value != null)
                {
                    row.Value.Location = new Float2(middleW - PanelPadding - StatBarValueWidth, row.Value.Location.Y);
                }
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
                var vermilionGlow = WithAlpha(InkWashTheme.VermilionPrimary, 0.09f);
                Render2D.FillRectangle(
                    new Rectangle(0f, 0f, Width * 0.4f, Height * 0.5f),
                    vermilionGlow);

                // 鎏金晕染（右下角）
                var goldGlow = WithAlpha(InkWashTheme.GoldPrimary, 0.06f);
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
        /// 返回按钮点击处理：请求返回战斗 HUD（设计隐藏出口 back-hud）。
        /// </summary>
        private void OnBackButtonClicked()
        {
            RequestNavigation(InkPageDomIds.BackHud);
        }

        /// <summary>
        /// "门派"按钮点击处理：请求跳转门派页面（设计 nav-sect）。
        /// </summary>
        /// <param name="button">触发事件的按钮（未使用）</param>
        private void OnSectButtonClicked(Button button)
        {
            RequestNavigation(InkPageDomIds.NavSect);
        }

        /// <summary>
        /// "调整配置"按钮点击处理：请求跳转装备配置页面重新调整兵甲。
        /// </summary>
        /// <param name="button">触发事件的按钮（未使用）</param>
        private void OnAdjustConfigClicked(Button button)
        {
            RequestNavigation(InkPageDomIds.NavEquipment);
        }

        /// <summary>
        /// "快速补给"按钮点击处理：把各药品补至满额并刷新数量与充足度汇总。
        /// </summary>
        /// <param name="button">触发事件的按钮（未使用）</param>
        private void OnQuickSupplyClicked(Button button)
        {
            try
            {
                for (int i = 0; i < _mockMedicines.Length; i++)
                {
                    var med = _mockMedicines[i];
                    _mockMedicines[i] = (med.name, med.effect, med.glyph, MedicineFullQuantity);

                    if (i < _medicineRows.Count && _medicineRows[i].QtyLabel != null)
                    {
                        _medicineRows[i].QtyLabel.Text = $"×{MedicineFullQuantity}";
                    }
                }

                if (_supplyValueLabel != null)
                {
                    _supplyValueLabel.Text = "充足";
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[MenuBattlePrepPage] 快速补给失败: {ex.Message}");
            }
        }

        /// <summary>
        /// "出战"按钮点击处理：触发 <see cref="NavigationRequested"/>("nav-battle-start")，
        /// 由 <see cref="InkPageRouter"/> 订阅后跳转战斗场景。
        /// </summary>
        /// <param name="button">触发事件的按钮（未使用）</param>
        private void OnBattleButtonClicked(Button button)
        {
            RequestNavigation(InkPageDomIds.NavBattleStart);
        }

        /// <summary>
        /// 统一发起导航请求并记录异常，避免单个出口异常影响其它交互。
        /// </summary>
        /// <param name="domId">目标页面 dom-id</param>
        private void RequestNavigation(string domId)
        {
            try
            {
                NavigationRequested?.Invoke(domId);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[MenuBattlePrepPage] NavigationRequested({domId}) 触发失败: {ex.Message}");
            }
        }

        // ===================================================================
        // 辅助方法
        // =======================================================================

        /// <summary>
        /// 创建面板内的小节标题 Label（"已装备 · 八件兵甲"/"装备属性加成"等）。
        /// </summary>
        /// <param name="text">标题文本</param>
        /// <param name="x">左边距（面板内坐标）</param>
        /// <param name="y">顶部 Y 坐标（面板内坐标）</param>
        /// <returns>配置完成的 Label</returns>
        private static Label CreateSectionLabel(string text, float x, float y)
        {
            return new Label
            {
                Text = text,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 11f),
                TextColor = InkWashTheme.TextTertiary,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(x, y),
                Size = new Float2(240f, SectionLabelHeight),
            };
        }

        /// <summary>
        /// 以既有设计 Token 颜色为基底派生半透明色（禁止硬编码色值）。
        /// </summary>
        /// <param name="color">基底 Token 颜色</param>
        /// <param name="alpha">目标透明度（0~1）</param>
        /// <returns>派生色</returns>
        private static Color WithAlpha(Color color, float alpha)
        {
            return new Color(color.R, color.G, color.B, alpha);
        }

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

        // ===================================================================
        // 私有辅助控件 / 引用结构
        // =======================================================================

        /// <summary>
        /// 备战页通用圆角底座：可选填充色、1px 描边与左侧强调竖线。
        /// 全部颜色来自 <see cref="InkWashTheme"/> 设计 Token，
        /// 对应设计稿 .skill-card / .medicine-item / .equip-stat-row 等圆角容器。
        /// </summary>
        private sealed class PrepBox : ContainerControl
        {
            /// <summary>圆角半径</summary>
            public float CornerRadius { get; set; } = InkWashTheme.RadiusMd;

            /// <summary>填充色（透明则不填充）</summary>
            public Color FillColor { get; set; } = Color.Transparent;

            /// <summary>描边色（透明则不描边）</summary>
            public Color BorderColor { get; set; } = Color.Transparent;

            /// <summary>描边厚度</summary>
            public float BorderThickness { get; set; } = 1f;

            /// <summary>左侧强调竖线色（透明则不绘制）</summary>
            public Color AccentColor { get; set; } = Color.Transparent;

            /// <summary>左侧强调竖线宽度</summary>
            public float AccentWidth { get; set; }

            /// <summary>
            /// 构造函数：透明背景，不裁剪子控件。
            /// </summary>
            public PrepBox()
            {
                BackgroundColor = Color.Transparent;
                ClipChildren = false;
            }

            /// <inheritdoc />
            public override void Draw()
            {
                if (!Visible || Width <= 0f || Height <= 0f)
                    return;

                var bounds = new Rectangle(0f, 0f, Width, Height);

                // 1. 圆角填充
                if (FillColor.A > 0f)
                {
                    InkRenderHelper.FillRoundedRectangle(bounds, CornerRadius, FillColor);
                }

                // 2. 子控件
                base.Draw();

                // 3. 圆角描边
                if (BorderThickness > 0f && BorderColor.A > 0f)
                {
                    InkRenderHelper.DrawRoundedRectangle(bounds, CornerRadius, BorderColor, BorderThickness);
                }

                // 4. 左侧强调竖线
                if (AccentWidth > 0f && AccentColor.A > 0f)
                {
                    Render2D.FillRectangle(new Rectangle(0f, 0f, AccentWidth, Height), AccentColor);
                }
            }
        }

        /// <summary>
        /// 武学卡行引用（中栏宽度变化时重排）。
        /// </summary>
        private sealed class SkillRowRefs
        {
            /// <summary>卡片底座</summary>
            public PrepBox Box;

            /// <summary>武学名 Label</summary>
            public Label NameLabel;

            /// <summary>武学说明 Label</summary>
            public Label DescLabel;

            /// <summary>主动/被动标签</summary>
            public InkTag RoleTag;

            /// <summary>等级标签</summary>
            public InkTag LevelTag;
        }

        /// <summary>
        /// 属性条行引用（中栏宽度变化时重排）。
        /// </summary>
        private sealed class StatBarRefs
        {
            /// <summary>属性名 Label</summary>
            public Label Label;

            /// <summary>属性条</summary>
            public InkBar Bar;

            /// <summary>属性数值 Label</summary>
            public Label Value;
        }

        /// <summary>
        /// 药品行引用（"快速补给"时刷新数量）。
        /// </summary>
        private sealed class MedicineRowRefs
        {
            /// <summary>药品行底座</summary>
            public PrepBox Box;

            /// <summary>药品名 Label</summary>
            public Label NameLabel;

            /// <summary>药品效果 Label</summary>
            public Label EffectLabel;

            /// <summary>药品数量 Label</summary>
            public Label QtyLabel;
        }
    }
}

using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.Ink.Components;
using HundunWorld.Game.UI.StyleSystem;
using System;
using System.Collections.Generic;
using Game.Character.Attributes;
using HundunWorld.Game.ECS.Components;
using HundunWorld.Game.Equipment;
using HundunWorld.Game.Services;

namespace HundunWorld.Game.UI.Ink.Pages.Character
{
    /// <summary>
/// 角色属性菜单 V2 页面（两栏布局，对齐 HTML 原型）。
/// 基于 HTML 原型 <c>menu-char-attributes-v2.html</c> 实现，
/// 采用"顶部导航 + 左侧角色预览 + 右侧属性面板 + 底部操作栏"结构。
/// <list type="bullet">
///   <item>左侧 <see cref="_previewPanel"/>：背景图 + 水墨晕染 + 角色名/等级/门派徽章/称号/门派标识</item>
///   <item>右侧 <see cref="_attrPanel"/>：战力区 → 基础属性卡片（2×2） → 进阶属性（2×3） → 装备摘要 → 武学摘要</item>
///   <item>装备摘要显示已装备的6件装备（图标+名称+类型+强化等级）</item>
///   <item><see cref="InkAttributeTooltip"/> 绑定到基础属性、进阶属性、装备槽</item>
///   <item><see cref="BindCharacter"/> 数据绑定</item>
/// </list>
/// 基础属性绑定 <see cref="CharacterAttributesComponent"/>，null 时回退 mock 数据。
/// 通过 <see cref="RefreshLayout"/> 支持屏幕尺寸变化。
/// 导航跳转通过 <see cref="NavigationRequested"/> 事件通知外部路由器。
/// </summary>
    public class MenuCharAttributesV2Page : ContainerControl, IInkPage
    {
        // ===================================================================
        // 布局常量
        // ===================================================================

        /// <summary>顶部导航栏高度（像素）</summary>
        private const float TopBarHeight = 60f;

        /// <summary>底部操作栏高度（像素）</summary>
        private const float BottomBarHeight = 60f;

        /// <summary>内容区顶部 Y 坐标（= 顶部导航栏高度）</summary>
        private const float ContentTop = TopBarHeight;

        /// <summary>内容区底部留白（= 底部操作栏高度）</summary>
        private const float ContentBottomReserve = BottomBarHeight;

        /// <summary>左右面板宽度各占 50%</summary>
        private const float PanelWidthRatio = 0.5f;

        /// <summary>属性面板内边距</summary>
        private const float PanelPadding = 24f;

        /// <summary>顶部导航栏内边距</summary>
        private const float TopBarPadding = 24f;

        /// <summary>顶部返回按钮尺寸</summary>
        private const float TopBackButtonSize = 36f;

        /// <summary>顶部栏元素间距</summary>
        private const float TopBarItemGap = 16f;

        /// <summary>底部操作栏按钮宽度</summary>
        private const float BottomButtonWidth = 140f;

        /// <summary>底部操作栏按钮高度</summary>
        private const float BottomButtonHeight = 32f;

        /// <summary>分区标题高度</summary>
        private const float SectionTitleHeight = 28f;

        /// <summary>分区标题左侧金色装饰竖线宽度</summary>
        private const float TitleBarWidth = 3f;

        /// <summary>分区标题左侧金色装饰竖线与文字的间距</summary>
        private const float TitleBarToTextGap = 8f;

        /// <summary>分区间距</summary>
        private const float SectionGap = 16f;

        /// <summary>基础属性项数量（气血/攻击/防御/暴击）</summary>
        private const int BasicAttrCount = 4;

        /// <summary>基础属性卡片列数</summary>
        private const int BasicAttrColumns = 2;

        /// <summary>基础属性卡片高度</summary>
        private const float BasicAttrCardHeight = 76f;

        /// <summary>基础属性卡片横向间距</summary>
        private const float BasicAttrCardGapX = 8f;

        /// <summary>基础属性卡片纵向间距</summary>
        private const float BasicAttrCardGapY = 8f;

        /// <summary>基础属性图标尺寸</summary>
        private const float BasicAttrIconSize = 28f;

        /// <summary>进阶属性项数量（穿透/格挡/闪避/命中/暴伤/韧性）</summary>
        private const int AdvancedAttrCount = 6;

        /// <summary>进阶属性列数</summary>
        private const int AdvancedAttrColumns = 2;

        /// <summary>进阶属性行高</summary>
        private const float AdvancedAttrRowHeight = 44f;

        /// <summary>进阶属性列间距</summary>
        private const float AdvancedAttrColumnGap = 14f;

        /// <summary>装备摘要项数量</summary>
        private const int EquipmentSummaryCount = 6;

        /// <summary>装备摘要图标尺寸</summary>
        private const float EquipmentSummaryIconSize = 48f;

        /// <summary>装备摘要列数</summary>
        private const int EquipmentSummaryColumns = 6;

        /// <summary>装备摘要间距</summary>
        private const float EquipmentSummaryGap = 8f;

        /// <summary>武学摘要项数量</summary>
        private const int MartialArtsCount = 3;

        /// <summary>武学摘要卡片高度</summary>
        private const float MartialArtsCardHeight = 72f;

        /// <summary>武学摘要卡片间距</summary>
        private const float MartialArtsCardGap = 8f;

        /// <summary>Tooltip 自动隐藏延时（秒）</summary>
        private const float TooltipHideDelay = 0.5f;

        /// <summary>装备槽数量</summary>
        private const int EquipmentSlotCount = 10;

        /// <summary>装备槽尺寸</summary>
        private const float EquipmentSlotSize = 56f;

        /// <summary>背包高度</summary>
        private const float BackpackHeight = 240f;

        /// <summary>3D预览宽度</summary>
        private const float Preview3DWidth = 400f;

        /// <summary>3D预览高度</summary>
        private const float Preview3DHeight = 500f;

        /// <summary>雷达图尺寸</summary>
        private const float RadarChartSize = 200f;

        /// <summary>背包列数</summary>
        private const int BackpackColumns = 4;

        /// <summary>面板间距</summary>
        private const float PanelGap = 12f;

        /// <summary>纸娃娃宽度</summary>
        private const float PaperDollWidth = 280f;

        /// <summary>纸娃娃高度</summary>
        private const float PaperDollHeight = 420f;

        // ===================================================================
        // 子控件引用 — 全局氛围层
        // ===================================================================

        /// <summary>全局水墨背景层</summary>
        private InkBackgroundLayer _backgroundLayer;

        /// <summary>全局暗角晕影层</summary>
        private InkVignette _vignette;

        // ===================================================================
        // 子控件引用 — 顶部导航栏
        // ===================================================================

        /// <summary>顶部导航栏面板</summary>
        private GradientBarPanel _topBar;

        /// <summary>顶部返回按钮（触发 <see cref="NavigationRequested"/>）</summary>
        private InkBackButton _topBackButton;

        /// <summary>顶部栏标题"角色属性"</summary>
        private Label _topTitleLabel;

        /// <summary>顶部栏副标题"属性面板"</summary>
        private Label _topSubtitleLabel;

        /// <summary>顶部栏标题与副标题分隔线</summary>
        private ContainerControl _topSeparator;

        /// <summary>顶部栏右侧按钮（稀有商品 / 详细属性 / 分享）</summary>
        private InkButton[] _topRightButtons;

        // ===================================================================
        // 子控件引用 — 左侧角色预览面板
        // ===================================================================

        /// <summary>左侧角色预览面板</summary>
        private InkPanel _previewPanel;

        /// <summary>左侧面板背景图层（背景图 + 渐变遮罩）</summary>
        private PreviewBackgroundLayer _previewBgLayer;

        /// <summary>左侧面板水墨晕染装饰（左上）</summary>
        private InkSplash _previewSplashTL;

        /// <summary>左侧面板水墨晕染装饰（右下）</summary>
        private InkSplash _previewSplashBR;

        /// <summary>左侧面板水墨晕染装饰（左下）</summary>
        private InkSplash _previewSplashBL;

        /// <summary>预览区角色名 Label（带 text-shadow）</summary>
        private ShadowedNameLabel _previewNameLabel;

        /// <summary>预览区等级容器（"Lv." + 数值 两段式）</summary>
        private ContainerControl _previewLevelContainer;

        /// <summary>预览区等级前缀 "Lv." Label</summary>
        private Label _previewLevelPrefixLabel;

        /// <summary>预览区等级数值 Label（带金色辉光）</summary>
        private GlowLevelLabel _previewLevelValueLabel;

        /// <summary>预览区门派徽章 InkTag</summary>
        private InkTag _previewSectLabel;

        /// <summary>预览区称号区域容器</summary>
        private ContainerControl _previewTitleContainer;

        /// <summary>称号左侧装饰线（渐变）</summary>
        private GradientLine _titleLineLeft;

        /// <summary>称号右侧装饰线（渐变）</summary>
        private GradientLine _titleLineRight;

        /// <summary>预览区称号 Label</summary>
        private Label _previewTitleLabel;

        /// <summary>底部门派标识 Label</summary>
        private Label _sectEmblemLabel;

        // ===================================================================
        // 子控件引用 — 右侧属性面板
        // ===================================================================

        /// <summary>右侧属性面板</summary>
        private GradientBarPanel _attrPanel;

        /// <summary>战力区标题</summary>
        private Label _combatPowerTitleLabel;

        /// <summary>战力阶段标签</summary>
        private InkTag _combatPowerStageTag;

        /// <summary>战力数值 Label（DIN 大字号）</summary>
        private Label _combatPowerValue;

        /// <summary>战力趋势图标</summary>
        private Label _combatPowerTrendLabel;

        /// <summary>战力增量</summary>
        private Label _combatPowerDeltaLabel;

        /// <summary>战力进度条</summary>
        private InkBar _combatPowerBar;

        /// <summary>战力进度条标签</summary>
        private Label _combatPowerBarCurrentLabel;

        /// <summary>战力进度条下一阶标签</summary>
        private Label _combatPowerBarNextLabel;

        /// <summary>基础属性分区标题</summary>
        private Label _basicAttrTitleLabel;

        /// <summary>基础属性分区标题装饰竖线</summary>
        private ContainerControl _basicAttrTitleBar;

        /// <summary>基础属性分区提示文字</summary>
        private Label _basicAttrHintLabel;

        /// <summary>4 个基础属性卡片背景</summary>
        private ContainerControl[] _basicAttrCards;

        /// <summary>4 个基础属性图标 Label</summary>
        private Label[] _basicAttrIcons;

        /// <summary>4 个基础属性名 Label（支持悬停）</summary>
        private HoverableLabel[] _basicAttrNameLabels;

        /// <summary>4 个基础属性数值 Label</summary>
        private Label[] _basicAttrValueLabels;

        /// <summary>4 个基础属性单位 Label</summary>
        private Label[] _basicAttrUnitLabels;

        /// <summary>4 个基础属性趋势 Label</summary>
        private Label[] _basicAttrTrendLabels;

        /// <summary>进阶属性分区标题</summary>
        private Label _advancedAttrTitleLabel;

        /// <summary>进阶属性分区标题装饰竖线</summary>
        private ContainerControl _advancedAttrTitleBar;

        /// <summary>进阶属性分区提示文字</summary>
        private Label _advancedAttrHintLabel;

        /// <summary>6 个进阶属性项背景</summary>
        private ContainerControl[] _advancedAttrCards;

        /// <summary>6 个进阶属性名 Label（支持悬停）</summary>
        private HoverableLabel[] _advancedAttrNameLabels;

        /// <summary>6 个进阶属性数值 Label</summary>
        private Label[] _advancedAttrValueLabels;

        /// <summary>6 个进阶属性图标 Label</summary>
        private Label[] _advancedAttrIcons;

        /// <summary>装备摘要分区标题</summary>
        private Label _equipmentTitleLabel;

        /// <summary>装备摘要分区标题装饰竖线</summary>
        private ContainerControl _equipmentTitleBar;

        /// <summary>装备摘要分区提示文字</summary>
        private Label _equipmentHintLabel;

        /// <summary>6 个装备摘要卡片背景</summary>
        private ContainerControl[] _equipmentSummaryCards;

        /// <summary>6 个装备摘要图标 Label</summary>
        private Label[] _equipmentSummaryIcons;

        /// <summary>6 个装备摘要名称 Label</summary>
        private Label[] _equipmentSummaryNameLabels;

        /// <summary>6 个装备摘要类型 Label</summary>
        private Label[] _equipmentSummaryTypeLabels;

        /// <summary>6 个装备摘要强化等级 Label</summary>
        private Label[] _equipmentSummaryEnhanceLabels;

        /// <summary>武学摘要分区标题</summary>
        private Label _martialArtsTitleLabel;

        /// <summary>武学摘要分区标题装饰竖线</summary>
        private ContainerControl _martialArtsTitleBar;

        /// <summary>武学摘要分区提示文字</summary>
        private Label _martialArtsHintLabel;

        /// <summary>3 个武学摘要卡片背景</summary>
        private ContainerControl[] _martialArtsCards;

        /// <summary>3 个武学摘要图标 Label</summary>
        private Label[] _martialArtsIcons;

        /// <summary>3 个武学摘要名称 Label</summary>
        private Label[] _martialArtsNameLabels;

        /// <summary>3 个武学摘要品质标签</summary>
        private InkTag[] _martialArtsQualityTags;

        /// <summary>3 个武学摘要元信息类型图标 Label</summary>
        private Label[] _martialArtsMetaTypeIcons;

        /// <summary>3 个武学摘要元信息类型文本 Label</summary>
        private Label[] _martialArtsMetaTypeTexts;

        /// <summary>3 个武学摘要元信息等级图标 Label</summary>
        private Label[] _martialArtsMetaLevelIcons;

        /// <summary>3 个武学摘要元信息等级文本 Label</summary>
        private Label[] _martialArtsMetaLevelTexts;

        /// <summary>3 个武学摘要威力数值 Label</summary>
        private Label[] _martialArtsPowerLabels;

        /// <summary>3 个武学摘要威力标签 Label</summary>
        private Label[] _martialArtsPowerLabelLabels;

        /// <summary>属性面板滚动容器</summary>
        private ContainerControl _attrPanelScroll;

        /// <summary>战力阶段标签</summary>
        private HoverableInkTag _stageTag;

        /// <summary>雷达图控件</summary>
        private HexRadarChartOverlay _radarChart;

        /// <summary>装备槽数组</summary>
        private InkEquipmentSlot[] _equipmentSlots;

        /// <summary>背包网格</summary>
        private InkBackpackGrid _backpackGrid;

        /// <summary>武学摘要左侧装饰条</summary>
        private ContainerControl[] _martialArtsLeftBars;

        /// <summary>3D角色预览</summary>
        private CharacterPreview3D _preview3D;

        /// <summary>中间面板背景层</summary>
        private PreviewBackgroundLayer _centerBgLayer;

        /// <summary>中间面板水墨晕染装饰（左上）</summary>
        private InkSplash _centerSplashTL;

        /// <summary>中间面板水墨晕染装饰（右上）</summary>
        private InkSplash _centerSplashTR;

        /// <summary>中间面板水墨晕染装饰（右下）</summary>
        private InkSplash _centerSplashBR;

        /// <summary>中间面板水墨晕染装饰（左下）</summary>
        private InkSplash _centerSplashBL;

        /// <summary>右侧面板水墨装饰</summary>
        private InkSplash _rightSpringSplash;

        /// <summary>右侧面板金色辉光</summary>
        private ContainerControl _rightGoldGlow;

        /// <summary>背包标题Label</summary>
        private Label _backpackTitleLabel;

        /// <summary>背包标题装饰条</summary>
        private ContainerControl _backpackTitleBar;

        /// <summary>纸娃娃背景</summary>
        private ContainerControl _paperDollBackground;

        /// <summary>右侧面板</summary>
        private InkPanel _rightPanel;

        /// <summary>中间面板</summary>
        private InkPanel _centerPanel;

        /// <summary>左侧面板</summary>
        private InkPanel _leftPanel;

        // ===================================================================
        // 子控件引用 — 顶层 Tooltip
        // ===================================================================

        /// <summary>全局属性 Tooltip 实例（页面顶层子控件）</summary>
        private InkAttributeTooltip _tooltip;

        // ===================================================================
        // 子控件引用 — 底部操作栏
        // ===================================================================

        /// <summary>底部操作栏面板</summary>
        private GradientBarPanel _bottomBar;

        /// <summary>底部操作按钮（详细属性 / 装备详情 / 武学详情）</summary>
        private InkButton[] _bottomActionButtons;

        // ===================================================================
        // 屏幕尺寸缓存
        // ===================================================================

        /// <summary>当前屏幕尺寸缓存，用于布局计算</summary>
        private Float2 _screenSize;

        // ===================================================================
        // 装备状态
        // ===================================================================

        /// <summary>已装备的装备字典（槽位 → 装备数据）</summary>
        private Dictionary<EquipmentSlot, EquipmentData> _equippedItems = new Dictionary<EquipmentSlot, EquipmentData>();

        /// <summary>背包装备列表</summary>
        private List<EquipmentData> _backpackItems = new List<EquipmentData>();

        /// <summary>已装备的扩展背包列表（魔兽世界式背包槽，0-3 共 4 个槽位）</summary>
        private List<EquippedBag> _equippedBags = new List<EquippedBag>();

        // ===================================================================
        // 属性缓存（用于 Tooltip 显示与雷达图数据）
        // ===================================================================

        /// <summary>基础属性基础值（不含装备加成）</summary>
        private float[] _basicAttrBaseValues = new float[BasicAttrCount];

        /// <summary>基础属性装备加成值</summary>
        private float[] _basicAttrBonusValues = new float[BasicAttrCount];

        /// <summary>基础属性总数值（基础 + 加成）</summary>
        private float[] _basicAttrTotalValues = new float[BasicAttrCount];

        /// <summary>五行属性总数值（金/木/水/火/土）</summary>
        private float[] _wuxingTotalValues = new float[5];

        // ===================================================================
        // Tooltip 悬停跟踪
        // ===================================================================

        /// <summary>最后一次 Tooltip 悬停时间（用于自动隐藏）</summary>
        private float _lastHoverTime = -1f;

        // ===================================================================
        // mock 数据字段
        // ===================================================================

        /// <summary>mock 角色名</summary>
        private string _mockName = "江湖过客";

        /// <summary>mock 角色等级</summary>
        private int _mockLevel = 42;

        /// <summary>mock 门派</summary>
        private string _mockSect = "青城派";

        /// <summary>mock 称号</summary>
        private string _mockTitle = "剑胆琴心";

        /// <summary>mock 武学数据（名称/品质/类型/等级/威力）</summary>
        private (string name, InkWashTheme.InkQuality quality, string type, int level, int power)[] _mockMartialArts =
        {
            ("青莲剑歌", InkWashTheme.InkQuality.Legendary, "剑法", 15, 8520),
            ("凌波微步", InkWashTheme.InkQuality.Epic, "轻功", 12, 6180),
            ("紫霞神功", InkWashTheme.InkQuality.Epic, "内功", 10, 5340),
        };

        /// <summary>进阶属性名（穿透/格挡/闪避/命中/暴伤/韧性）</summary>
        private static readonly string[] AdvancedAttrNames = { "穿透", "格挡", "闪避", "命中", "暴伤", "韧性" };

        /// <summary>进阶属性数值</summary>
        private static readonly float[] AdvancedAttrValues = { 1580f, 1240f, 18.2f, 95.8f, 156f, 85f };

        /// <summary>进阶属性是否为百分比</summary>
        private static readonly bool[] AdvancedAttrIsPercent = { false, false, true, true, true, false };

        /// <summary>进阶属性 mock 数值（用于雷达图和 tooltip）</summary>
        private float[] _mockAdvancedAttrValues = { 0.35f, 0.28f, 0.92f, 0.18f, 1.56f, 0.85f };

        /// <summary>进阶属性 mock 名称（用于 tooltip）</summary>
        private string[] _mockAdvancedAttrNames = { "暴击", "抗暴", "命中", "闪避", "暴伤", "韧性" };

        /// <summary>进阶属性图标 Unicode 符号</summary>
        private static readonly string[] AdvancedAttrIcons = { "\u26A1", "\u26E9", "\u26A1", "\u26E9", "\u2665", "\u2693" };

        /// <summary>基础属性名（气血/攻击/防御/暴击）</summary>
        private static readonly string[] BasicAttrNames = { "气血", "攻击", "防御", "暴击" };

        /// <summary>基础属性说明</summary>
        private static readonly string[] BasicAttrDescriptions =
        {
            "角色生命值上限，决定可承受的伤害量",
            "角色攻击力，影响造成的伤害值",
            "角色防御力，减少受到的伤害",
            "攻击触发暴击的概率，造成额外伤害"
        };

        /// <summary>基础属性图标 Unicode 符号</summary>
        private static readonly string[] BasicAttrIconSymbols = { "\u2764", "\u2694", "\u26E9", "\u2605" };

        /// <summary>基础属性图标颜色（气血=翡翠，攻击=血色，防御=青色，暴击=金色）</summary>
        private static readonly Color[] BasicAttrIconColors =
        {
            InkWashTheme.JadeBright,        // 气血 — 翡翠
            InkWashTheme.BloodBright,       // 攻击 — 血色（设计方案朱砂系仅用于战斗/危险）
            InkWashTheme.Info,              // 防御 — 青色
            InkWashTheme.GoldBright,        // 暴击 — 金色
        };

        /// <summary>基础属性图标边框色</summary>
        private static readonly Color[] BasicAttrIconBorderColors =
        {
            InkWashTheme.JadePrimary,       // 气血 — 翡翠边框
            InkWashTheme.BloodDeep,         // 攻击 — 血色边框
            InkWashTheme.Info,              // 防御 — 青色边框
            InkWashTheme.GoldDeep,          // 暴击 — 金色边框
        };

        /// <summary>基础属性是否为百分比</summary>
        private static readonly bool[] BasicAttrIsPercent = { false, false, false, true };

        /// <summary>战力阶段标签文本</summary>
        private static readonly string[] CombatPowerStageNames = { "初入江湖", "江湖二流", "江湖一流", "绝世高手", "一代宗师", "武林神话" };

        // ===================================================================
        // 数据绑定字段
        // ===================================================================

        /// <summary>绑定的角色属性组件，null 时使用 mock 数据</summary>
        private CharacterAttributesComponent _boundCharacter;

        /// <summary>当前角色成长阶段缓存（用于阶段标签 Tooltip 显示）</summary>
        private CharacterStage _currentStage = CharacterStage.Wuxia;

        /// <summary>
        /// 装备槽显示顺序（对应右侧 5×3 人体拓扑）。
        /// 中间列自上而下为人体中轴：头颈 → 身躯 → 腰 → 腿 → 足；
        /// 左右两列对称分布肩/手/腕/戒指，面部单独置于右上角。
        /// </summary>
        private static readonly EquipmentSlot[] DisplayedSlots =
        {
            // Row 0: 头 / 颈 / 面
            EquipmentSlot.Head,
            EquipmentSlot.Neck,
            EquipmentSlot.Face,
            // Row 1: 肩 / 身 / 背
            EquipmentSlot.Shoulder,
            EquipmentSlot.Body,
            EquipmentSlot.Back,
            // Row 2: 右手 / 腰 / 左手
            EquipmentSlot.RightHand,
            EquipmentSlot.Waist,
            EquipmentSlot.LeftHand,
            // Row 3: 右腕 / 腿 / 左腕
            EquipmentSlot.RightWrist,
            EquipmentSlot.Legs,
            EquipmentSlot.LeftWrist,
            // Row 4: 右戒 / 足 / 左戒
            EquipmentSlot.RightRing,
            EquipmentSlot.Feet,
            EquipmentSlot.LeftRing,
        };

        // ===================================================================
        // 公共 API：事件
        // ===================================================================

        /// <summary>
        /// 导航请求事件。
        /// 由顶部返回按钮、底部按钮触发，参数为目标页面的 dom-id
        /// （如 <c>"back-hud"</c>）。由 <see cref="InkPageRouter"/> 订阅以执行页面跳转。
        /// </summary>
        public event Action<string> NavigationRequested;

        // ===================================================================
        // 构造函数
        // ===================================================================

        /// <summary>
        /// 构造函数：初始化三栏布局 + 顶层 Tooltip，使用 mock 数据填充。
        /// 构造时读取 <see cref="FlaxEngine.Screen.Size"/> 计算布局，
        /// 屏幕尺寸未就绪时使用 1920x1080 兜底。
        /// </summary>
        public MenuCharAttributesV2Page()
        {
            _screenSize = FlaxEngine.Screen.Size;
            if (_screenSize.X <= 0f || _screenSize.Y <= 0f)
            {
                _screenSize = new Float2(1920f, 1080f);
            }

            AnchorPreset = AnchorPresets.StretchAll;
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
            AutoFocus = false;
            Location = Float2.Zero;
            Size = _screenSize;

            try
            {
                BuildAtmosphere();
                BuildTopBar();
                BuildPreviewPanel();
                BuildAttributePanel();
                BuildBottomBar();
                BuildTooltip();

                ApplyLayout();
                RefreshAllData();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[MenuCharAttributesV2Page] 初始化失败: {ex.Message}");
            }
        }

        // ===================================================================
        // 构建方法 — 全局氛围层
        // ===================================================================

        /// <summary>
        /// 全局氛围层：水墨背景 + 暗角晕影。
        /// </summary>
        private void BuildAtmosphere()
        {
            _backgroundLayer = new InkBackgroundLayer
            {
                AnchorPreset = AnchorPresets.StretchAll,
                Location = Float2.Zero,
                Size = _screenSize,
            };
            AddChild(_backgroundLayer);

            _vignette = new InkVignette
            {
                AnchorPreset = AnchorPresets.StretchAll,
                Location = Float2.Zero,
                Size = _screenSize,
            };
            AddChild(_vignette);
        }

        // ===================================================================
        // 构建方法 — 顶部导航栏
        // ===================================================================

        /// <summary>
        /// 顶部导航栏：返回按钮 + 页面标题 + 副标题 + 右侧按钮。
        /// </summary>
        private void BuildTopBar()
        {
            _topBar = new GradientBarPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                GradientDirection = GradientBarPanel.GradientDirectionKind.Horizontal,
                GradientColors = new[]
                {
                    WithAlpha(InkWashTheme.BaseDefault, 0.98f),
                    WithAlpha(InkWashTheme.BaseSecondary, 0.95f),
                    WithAlpha(InkWashTheme.BaseDefault, 0.98f),
                },
                BorderSide = GradientBarPanel.BorderSideKind.Bottom,
                BorderColor = InkWashTheme.BorderGold,
            };
            AddChild(_topBar);

            _topBackButton = new InkBackButton
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(TopBackButtonSize, TopBackButtonSize),
            };
            _topBackButton.Clicked += OnBackToHud;
            _topBar.AddChild(_topBackButton);

            _topTitleLabel = new Label
            {
                Text = "角色属性",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 20f),
                TextColor = InkWashTheme.PaperBright,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
            };
            _topBar.AddChild(_topTitleLabel);

            _topSeparator = new ContainerControl
            {
                BackgroundColor = InkWashTheme.BorderNeutralL2,
                AnchorPreset = AnchorPresets.TopLeft,
            };
            _topBar.AddChild(_topSeparator);

            _topSubtitleLabel = new Label
            {
                Text = "属性面板",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 13f),
                TextColor = InkWashTheme.TextSecondary,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
            };
            _topBar.AddChild(_topSubtitleLabel);

            _topRightButtons = new InkButton[3];
            string[] labels = { "稀有商品", "详细属性", "分享" };
            InkButtonVariant[] variants = { InkButtonVariant.Ghost, InkButtonVariant.Ghost, InkButtonVariant.Default };
            for (int i = 0; i < _topRightButtons.Length; i++)
            {
                var btn = new InkButton
                {
                    Text = labels[i],
                    Variant = variants[i],
                    ButtonSize = InkButtonSize.Sm,
                    AnchorPreset = AnchorPresets.TopRight,
                };
                _topBar.AddChild(btn);
                _topRightButtons[i] = btn;
            }
            _topRightButtons[0].ButtonClicked += OnTopButtonRareItems;
            _topRightButtons[1].ButtonClicked += OnTopButtonDetailedAttributes;
            _topRightButtons[2].ButtonClicked += OnTopButtonShare;
        }

        // ===================================================================
        // 构建方法 — 左侧角色预览面板
        // ===================================================================

        /// <summary>
        /// 左侧角色预览面板：背景图 + 水墨晕染 + 角色名/等级/门派徽章/称号/门派标识。
        /// </summary>
        private void BuildPreviewPanel()
        {
            _previewPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = InkWashTheme.BaseSecondary,
            };
            AddChild(_previewPanel);

            _previewBgLayer = new PreviewBackgroundLayer
            {
                AnchorPreset = AnchorPresets.StretchAll,
            };
            _previewPanel.AddChild(_previewBgLayer);

            _previewSplashTL = new InkSplash
            {
                Variant = InkSplashVariant.Normal,
                Opacity = 0.18f,
                AnchorPreset = AnchorPresets.TopLeft,
            };
            _previewPanel.AddChild(_previewSplashTL);

            _previewSplashBR = new InkSplash
            {
                Variant = InkSplashVariant.Elevated,
                Opacity = 0.15f,
                AnchorPreset = AnchorPresets.BottomRight,
            };
            _previewPanel.AddChild(_previewSplashBR);

            _previewSplashBL = new InkSplash
            {
                Variant = InkSplashVariant.Normal,
                Opacity = 0.10f,
                AnchorPreset = AnchorPresets.BottomLeft,
            };
            _previewPanel.AddChild(_previewSplashBL);

            BuildPreviewCharacterInfo();
        }

        /// <summary>构建角色预览区信息。</summary>
        private void BuildPreviewCharacterInfo()
        {
            _previewNameLabel = new ShadowedNameLabel
            {
                Text = _mockName,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 32f),
                TextColor = InkWashTheme.PaperBright,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
            };
            _previewPanel.AddChild(_previewNameLabel);

            _previewLevelContainer = new ContainerControl
            {
                BackgroundColor = Color.Transparent,
                AnchorPreset = AnchorPresets.TopLeft,
            };
            _previewPanel.AddChild(_previewLevelContainer);

            _previewLevelPrefixLabel = new Label
            {
                Text = "Lv.",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 18f),
                TextColor = InkWashTheme.GoldDeep,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
            };
            _previewLevelContainer.AddChild(_previewLevelPrefixLabel);

            _previewLevelValueLabel = new GlowLevelLabel
            {
                Text = _mockLevel.ToString(),
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 28f),
                TextColor = InkWashTheme.GoldBright,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
            };
            _previewLevelContainer.AddChild(_previewLevelValueLabel);

            _previewSectLabel = new InkTag
            {
                Text = $"\u2694 {_mockSect} · 内门弟子",
                TagVariant = InkTagVariant.Brand,
                AnchorPreset = AnchorPresets.TopLeft,
            };
            _previewPanel.AddChild(_previewSectLabel);

            _previewTitleContainer = new ContainerControl
            {
                BackgroundColor = Color.Transparent,
                AnchorPreset = AnchorPresets.TopLeft,
                ClipChildren = false,
            };
            _previewPanel.AddChild(_previewTitleContainer);

            _titleLineLeft = new GradientLine
            {
                Direction = GradientLine.GradientDirectionKind.Horizontal,
                StartColor = Color.Transparent,
                MidColor = InkWashTheme.GoldPrimary,
                EndColor = Color.Transparent,
                AnchorPreset = AnchorPresets.TopLeft,
            };
            _previewTitleContainer.AddChild(_titleLineLeft);

            _previewTitleLabel = new Label
            {
                Text = _mockTitle,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 24f),
                TextColor = InkWashTheme.GoldBright,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
            };
            _previewTitleContainer.AddChild(_previewTitleLabel);

            _titleLineRight = new GradientLine
            {
                Direction = GradientLine.GradientDirectionKind.Horizontal,
                StartColor = Color.Transparent,
                MidColor = InkWashTheme.GoldPrimary,
                EndColor = Color.Transparent,
                AnchorPreset = AnchorPresets.TopLeft,
            };
            _previewTitleContainer.AddChild(_titleLineRight);

            _sectEmblemLabel = new Label
            {
                Text = "\u26F0 青城",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 14f),
                TextColor = WithAlpha(InkWashTheme.PaperDark, 0.5f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
            };
            _previewPanel.AddChild(_sectEmblemLabel);
        }

        /// <summary>构建战力区。</summary>
        private void BuildCombatPowerSection()
        {
            _combatPowerTitleLabel = new Label
            {
                Text = "战力",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 13f),
                TextColor = InkWashTheme.PaperAged,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
            };
            _attrPanelScroll.AddChild(_combatPowerTitleLabel);

            _combatPowerStageTag = new InkTag
            {
                Text = "江湖一流",
                TagVariant = InkTagVariant.Brand,
                AnchorPreset = AnchorPresets.TopRight,
            };
            _attrPanelScroll.AddChild(_combatPowerStageTag);

            _combatPowerValue = new GlowLabel
            {
                Text = "0",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 48f),
                TextColor = InkWashTheme.GoldBright,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
            };
            _attrPanelScroll.AddChild(_combatPowerValue);

            _combatPowerTrendLabel = new Label
            {
                Text = "\u2197",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 16f),
                TextColor = InkWashTheme.JadeBright,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
            };
            _attrPanelScroll.AddChild(_combatPowerTrendLabel);

            _combatPowerDeltaLabel = new Label
            {
                Text = "+320",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                TextColor = InkWashTheme.JadeBright,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
            };
            _attrPanelScroll.AddChild(_combatPowerDeltaLabel);

            _combatPowerBar = new InkBar
            {
                FillVariant = InkBarFillVariant.Gold,
                AnchorPreset = AnchorPresets.TopLeft,
                Height = 8f,
                Value = 0.72f,
            };
            _attrPanelScroll.AddChild(_combatPowerBar);

            _combatPowerBarCurrentLabel = new Label
            {
                Text = "当前",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.PaperAged,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
            };
            _attrPanelScroll.AddChild(_combatPowerBarCurrentLabel);

            _combatPowerBarNextLabel = new Label
            {
                Text = "下一阶 · 绝世高手",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.GoldDeep,
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
            };
            _attrPanelScroll.AddChild(_combatPowerBarNextLabel);
        }

        /// <summary>构建基础属性 4 项卡片（2 列 2 行）。</summary>
        private void BuildBasicAttributes()
        {
            _basicAttrTitleBar = CreateTitleBar();
            _attrPanelScroll.AddChild(_basicAttrTitleBar);

            _basicAttrTitleLabel = new Label
            {
                Text = "基础属性",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 16f),
                TextColor = InkWashTheme.PaperBright,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
            };
            _attrPanelScroll.AddChild(_basicAttrTitleLabel);

            _basicAttrHintLabel = new Label
            {
                Text = "核心战斗数值",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextTertiary,
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
            };
            _attrPanelScroll.AddChild(_basicAttrHintLabel);

            _basicAttrCards = new ContainerControl[BasicAttrCount];
            _basicAttrIcons = new Label[BasicAttrCount];
            _basicAttrNameLabels = new HoverableLabel[BasicAttrCount];
            _basicAttrValueLabels = new Label[BasicAttrCount];
            _basicAttrUnitLabels = new Label[BasicAttrCount];
            _basicAttrTrendLabels = new Label[BasicAttrCount];

            for (int i = 0; i < BasicAttrCount; i++)
            {
                var card = new BasicAttrCard
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                };
                _attrPanelScroll.AddChild(card);
                _basicAttrCards[i] = card;

                var icon = new BorderedIcon
                {
                    Text = BasicAttrIconSymbols[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 18f),
                    TextColor = BasicAttrIconColors[i],
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.TopLeft,
                    BackgroundColor = new Color(
                        BasicAttrIconColors[i].R,
                        BasicAttrIconColors[i].G,
                        BasicAttrIconColors[i].B,
                        0.1f),
                    IconBorderColor = BasicAttrIconBorderColors[i],
                    IconBorderThickness = 1f,
                };
                card.AddChild(icon);
                _basicAttrIcons[i] = icon;

                var nameLabel = new HoverableLabel
                {
                    Text = BasicAttrNames[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 13f),
                    TextColor = InkWashTheme.PaperAged,
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.TopLeft,
                    AttributeIndex = i,
                };
                nameLabel.MouseEntered += OnBasicAttrHovered;
                nameLabel.MouseLeft += OnAttributeHoverEnded;
                card.AddChild(nameLabel);
                _basicAttrNameLabels[i] = nameLabel;

                var valueLabel = new Label
                {
                    Text = "0",
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 18f),
                    TextColor = InkWashTheme.PaperBright,
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.TopLeft,
                };
                card.AddChild(valueLabel);
                _basicAttrValueLabels[i] = valueLabel;

                var unitLabel = new Label
                {
                    Text = BasicAttrIsPercent[i] ? "%" : "",
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                    TextColor = InkWashTheme.TextTertiary,
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.TopLeft,
                };
                card.AddChild(unitLabel);
                _basicAttrUnitLabels[i] = unitLabel;

                var trendLabel = new Label
                {
                    Text = BasicAttrIsPercent[i] ? "+1.2%" : "+12",
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                    TextColor = InkWashTheme.JadeBright,
                    HorizontalAlignment = TextAlignment.Far,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.TopLeft,
                    BackgroundColor = new Color(
                        InkWashTheme.JadePrimary.R,
                        InkWashTheme.JadePrimary.G,
                        InkWashTheme.JadePrimary.B,
                        0.15f),
                };
                card.AddChild(trendLabel);
                _basicAttrTrendLabels[i] = trendLabel;
            }
        }

        /// <summary>构建进阶属性 6 项（2 列 3 行）。</summary>
        private void BuildAdvancedAttributes()
        {
            _advancedAttrTitleBar = CreateTitleBar();
            _attrPanelScroll.AddChild(_advancedAttrTitleBar);

            _advancedAttrTitleLabel = new Label
            {
                Text = "进阶属性",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 16f),
                TextColor = InkWashTheme.PaperBright,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
            };
            _attrPanelScroll.AddChild(_advancedAttrTitleLabel);

            _advancedAttrHintLabel = new Label
            {
                Text = "精细化战斗数值",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextTertiary,
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
            };
            _attrPanelScroll.AddChild(_advancedAttrHintLabel);

            _advancedAttrCards = new ContainerControl[AdvancedAttrCount];
            _advancedAttrNameLabels = new HoverableLabel[AdvancedAttrCount];
            _advancedAttrValueLabels = new Label[AdvancedAttrCount];
            _advancedAttrIcons = new Label[AdvancedAttrCount];

            for (int i = 0; i < AdvancedAttrCount; i++)
            {
                var card = new AdvancedAttrRow
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                };
                _attrPanelScroll.AddChild(card);
                _advancedAttrCards[i] = card;

                var iconLabel = new Label
                {
                    Text = AdvancedAttrIcons[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                    TextColor = InkWashTheme.TextTertiary,
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.TopLeft,
                };
                card.AddChild(iconLabel);
                _advancedAttrIcons[i] = iconLabel;

                var nameLabel = new HoverableLabel
                {
                    Text = AdvancedAttrNames[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 13f),
                    TextColor = InkWashTheme.PaperAged,
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.TopLeft,
                    AttributeIndex = i,
                };
                nameLabel.MouseEntered += OnAdvancedAttrHovered;
                nameLabel.MouseLeft += OnAttributeHoverEnded;
                card.AddChild(nameLabel);
                _advancedAttrNameLabels[i] = nameLabel;

                string valueText = AdvancedAttrIsPercent[i]
                    ? $"{AdvancedAttrValues[i]}"
                    : $"{(int)AdvancedAttrValues[i]}";
                var valueLabel = new Label
                {
                    Text = valueText,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 16f),
                    TextColor = InkWashTheme.PaperBright,
                    HorizontalAlignment = TextAlignment.Far,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.TopLeft,
                };
                card.AddChild(valueLabel);
                _advancedAttrValueLabels[i] = valueLabel;
            }
        }

        /// <summary>构建装备摘要（6 件装备，水平排列）。</summary>
        private void BuildEquipmentSummary()
        {
            _equipmentTitleBar = CreateTitleBar();
            _attrPanelScroll.AddChild(_equipmentTitleBar);

            _equipmentTitleLabel = new Label
            {
                Text = "装备摘要",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 16f),
                TextColor = InkWashTheme.GoldBright,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
            };
            _attrPanelScroll.AddChild(_equipmentTitleLabel);

            _equipmentHintLabel = new Label
            {
                Text = "已装备 6 件",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextTertiary,
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
            };
            _attrPanelScroll.AddChild(_equipmentHintLabel);

            _equipmentSummaryCards = new ContainerControl[EquipmentSummaryCount];
            _equipmentSummaryIcons = new Label[EquipmentSummaryCount];
            _equipmentSummaryNameLabels = new Label[EquipmentSummaryCount];
            _equipmentSummaryTypeLabels = new Label[EquipmentSummaryCount];
            _equipmentSummaryEnhanceLabels = new Label[EquipmentSummaryCount];

            string[] equipNames = { "青锋剑", "玄铁盔", "云锦袍", "踏风靴", "玉佩", "龙纹戒" };
            string[] equipTypes = { "武器", "头盔", "护甲", "鞋子", "饰品", "饰品" };
            string[] equipIcons = { "\u2694", "\u26D1", "\u2705", "\u26C4", "\u25C6", "\u26AB" };
            int[] equipEnhance = { 12, 10, 11, 9, 8, 10 };

            for (int i = 0; i < EquipmentSummaryCount; i++)
            {
                var card = new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    BackgroundColor = new Color(InkWashTheme.BaseDefault.R, InkWashTheme.BaseDefault.G, InkWashTheme.BaseDefault.B, 0.9f),
                };
                _attrPanelScroll.AddChild(card);
                _equipmentSummaryCards[i] = card;

                var icon = new BorderedIcon
                {
                    Text = equipIcons[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 20f),
                    TextColor = i == 0 ? InkWashTheme.BloodBright : InkWashTheme.GoldBright,
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.TopLeft,
                    BackgroundColor = Color.Transparent,
                    IconBorderColor = i == 0 ? InkWashTheme.BloodDeep : InkWashTheme.GoldDeep,
                    IconBorderThickness = 1f,
                };
                card.AddChild(icon);
                _equipmentSummaryIcons[i] = icon;

                var nameLabel = new Label
                {
                    Text = equipNames[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 12f),
                    TextColor = InkWashTheme.PaperBright,
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.TopLeft,
                };
                card.AddChild(nameLabel);
                _equipmentSummaryNameLabels[i] = nameLabel;

                var typeLabel = new Label
                {
                    Text = equipTypes[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f),
                    TextColor = InkWashTheme.TextTertiary,
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.TopLeft,
                };
                card.AddChild(typeLabel);
                _equipmentSummaryTypeLabels[i] = typeLabel;

                var enhanceLabel = new Label
                {
                    Text = $"+{equipEnhance[i]}",
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 11f),
                    TextColor = InkWashTheme.GoldBright,
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.TopLeft,
                };
                card.AddChild(enhanceLabel);
                _equipmentSummaryEnhanceLabels[i] = enhanceLabel;
            }
        }

        /// <summary>构建武学摘要 3 项卡片。</summary>
        private void BuildMartialArtsSummary()
        {
            _martialArtsTitleBar = CreateTitleBar();
            _attrPanelScroll.AddChild(_martialArtsTitleBar);

            _martialArtsTitleLabel = new Label
            {
                Text = "武学摘要",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 16f),
                TextColor = InkWashTheme.GoldBright,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
            };
            _attrPanelScroll.AddChild(_martialArtsTitleLabel);

            _martialArtsHintLabel = new Label
            {
                Text = "已装备 3 门武学",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextTertiary,
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
            };
            _attrPanelScroll.AddChild(_martialArtsHintLabel);

            _martialArtsCards = new ContainerControl[MartialArtsCount];
            _martialArtsIcons = new Label[MartialArtsCount];
            _martialArtsNameLabels = new Label[MartialArtsCount];
            _martialArtsQualityTags = new InkTag[MartialArtsCount];
            _martialArtsMetaTypeIcons = new Label[MartialArtsCount];
            _martialArtsMetaTypeTexts = new Label[MartialArtsCount];
            _martialArtsMetaLevelIcons = new Label[MartialArtsCount];
            _martialArtsMetaLevelTexts = new Label[MartialArtsCount];
            _martialArtsPowerLabels = new Label[MartialArtsCount];
            _martialArtsPowerLabelLabels = new Label[MartialArtsCount];

            for (int i = 0; i < MartialArtsCount; i++)
            {
                var card = new MartialArtCard
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                };
                _attrPanelScroll.AddChild(card);
                _martialArtsCards[i] = card;

                // 武学图标容器：42×42px，按品质分色描边与文字
                var icon = new BorderedIcon
                {
                    Text = "\u2694",
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 20f),
                    TextColor = _mockMartialArts[i].quality switch
                    {
                        InkWashTheme.InkQuality.Legendary => InkWashTheme.QualityLegendary,
                        InkWashTheme.InkQuality.Epic => InkWashTheme.GoldBright,
                        _ => InkWashTheme.GoldPrimary,
                    },
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.TopLeft,
                    BackgroundColor = new Color(
                        InkWashTheme.BaseDefault.R,
                        InkWashTheme.BaseDefault.G,
                        InkWashTheme.BaseDefault.B,
                        0.9f),
                    IconBorderColor = _mockMartialArts[i].quality switch
                    {
                        InkWashTheme.InkQuality.Legendary => InkWashTheme.GoldDeep,
                        InkWashTheme.InkQuality.Epic => InkWashTheme.GoldDeep,
                        _ => InkWashTheme.BorderNeutralL2,
                    },
                    IconBorderThickness = 1f,
                };
                card.AddChild(icon);
                _martialArtsIcons[i] = icon;

                var nameLabel = new Label
                {
                    Text = _mockMartialArts[i].name,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 15f),
                    TextColor = InkWashTheme.PaperBright,
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.TopLeft,
                    // 通过 Scale 模拟 1px 字距，强化武学名视觉节奏
                    Scale = new Float2(1.02f, 1f),
                };
                card.AddChild(nameLabel);
                _martialArtsNameLabels[i] = nameLabel;

                var qualityTag = new InkTag
                {
                    Text = QualityName(_mockMartialArts[i].quality),
                    TagVariant = QualityTagVariant(_mockMartialArts[i].quality),
                    AnchorPreset = AnchorPresets.TopLeft,
                };
                card.AddChild(qualityTag);
                _martialArtsQualityTags[i] = qualityTag;

                // 元信息双段拆分：类型图标 + 类型文本 + 等级图标 + 等级文本
                var typeIcon = new Label
                {
                    Text = "\u26A1",
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                    TextColor = InkWashTheme.Info,
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.TopLeft,
                };
                card.AddChild(typeIcon);
                _martialArtsMetaTypeIcons[i] = typeIcon;

                var typeText = new Label
                {
                    Text = _mockMartialArts[i].type,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                    TextColor = InkWashTheme.PaperAged,
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.TopLeft,
                };
                card.AddChild(typeText);
                _martialArtsMetaTypeTexts[i] = typeText;

                var levelIcon = new Label
                {
                    Text = "\u2605",
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                    TextColor = InkWashTheme.GoldPrimary,
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.TopLeft,
                };
                card.AddChild(levelIcon);
                _martialArtsMetaLevelIcons[i] = levelIcon;

                var levelText = new Label
                {
                    Text = $"Lv.{_mockMartialArts[i].level}",
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                    TextColor = InkWashTheme.PaperAged,
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.TopLeft,
                };
                card.AddChild(levelText);
                _martialArtsMetaLevelTexts[i] = levelText;

                var powerValue = new Label
                {
                    Text = _mockMartialArts[i].power.ToString("N0"),
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 18f),
                    TextColor = InkWashTheme.GoldBright,
                    HorizontalAlignment = TextAlignment.Far,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.TopLeft,
                };
                card.AddChild(powerValue);
                _martialArtsPowerLabels[i] = powerValue;

                var powerLabel = new Label
                {
                    Text = "威力",
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f),
                    TextColor = InkWashTheme.TextTertiary,
                    HorizontalAlignment = TextAlignment.Far,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.TopLeft,
                };
                card.AddChild(powerLabel);
                _martialArtsPowerLabelLabels[i] = powerLabel;

                var leftBar = new ContainerControl
                {
                    BackgroundColor = _mockMartialArts[i].quality == InkWashTheme.InkQuality.Legendary
                        ? InkWashTheme.VermilionPrimary
                        : InkWashTheme.GoldPrimary,
                    AnchorPreset = AnchorPresets.TopLeft,
                };
                card.AddChild(leftBar);
                _martialArtsLeftBars[i] = leftBar;
            }
        }

        // ===================================================================
        // 构建方法 — 中间预览面板
        // ===================================================================

        /// <summary>
        /// 中间预览面板：水墨氛围 + 中部 <see cref="CharacterPreview3D"/> + 底部角色信息。
        /// </summary>
        private void BuildCenterPanel()
        {
            _centerPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = InkWashTheme.BaseSecondary,
            };
            AddChild(_centerPanel);

            _centerBgLayer = new PreviewBackgroundLayer
            {
                AnchorPreset = AnchorPresets.StretchAll,
            };
            _centerPanel.AddChild(_centerBgLayer);

            _centerSplashTL = new InkSplash
            {
                Variant = InkSplashVariant.Normal,
                Opacity = 0.18f,
                AnchorPreset = AnchorPresets.TopLeft,
            };
            _centerPanel.AddChild(_centerSplashTL);

            _centerSplashTR = new InkSplash
            {
                Variant = InkSplashVariant.Spring,
                Opacity = 0.14f,
                AnchorPreset = AnchorPresets.TopRight,
            };
            _centerPanel.AddChild(_centerSplashTR);

            _centerSplashBR = new InkSplash
            {
                Variant = InkSplashVariant.Elevated,
                Opacity = 0.15f,
                AnchorPreset = AnchorPresets.BottomRight,
            };
            _centerPanel.AddChild(_centerSplashBR);

            _centerSplashBL = new InkSplash
            {
                Variant = InkSplashVariant.Normal,
                Opacity = 0.10f,
                AnchorPreset = AnchorPresets.BottomLeft,
            };
            _centerPanel.AddChild(_centerSplashBL);

            _preview3D = new CharacterPreview3D
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(Preview3DWidth, Preview3DHeight),
            };
            _centerPanel.AddChild(_preview3D);

            _previewNameLabel = new ShadowedNameLabel
            {
                Text = _mockName,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 32f),
                TextColor = InkWashTheme.PaperBright,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
            };
            _leftPanel.AddChild(_previewNameLabel);

            // 等级两段式：容器内水平排列 "Lv." 前缀 + 数值（数值带辉光）
            _previewLevelContainer = new ContainerControl
            {
                BackgroundColor = Color.Transparent,
                AnchorPreset = AnchorPresets.TopLeft,
            };
            _leftPanel.AddChild(_previewLevelContainer);

            _previewLevelPrefixLabel = new Label
            {
                Text = "Lv.",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 18f),
                TextColor = InkWashTheme.GoldDeep,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
            };
            _previewLevelContainer.AddChild(_previewLevelPrefixLabel);

            _previewLevelValueLabel = new GlowLevelLabel
            {
                Text = _mockLevel.ToString(),
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 28f),
                TextColor = InkWashTheme.GoldBright,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
            };
            _previewLevelContainer.AddChild(_previewLevelValueLabel);

            _previewSectLabel = new InkTag
            {
                Text = $"\u2694 {_mockSect} · 内门弟子",
                TagVariant = InkTagVariant.Brand,
                AnchorPreset = AnchorPresets.TopLeft,
            };
            _leftPanel.AddChild(_previewSectLabel);

            _previewTitleContainer = new ContainerControl
            {
                BackgroundColor = Color.Transparent,
                AnchorPreset = AnchorPresets.TopLeft,
                ClipChildren = false,
            };
            _leftPanel.AddChild(_previewTitleContainer);

            _titleLineLeft = new GradientLine
            {
                Direction = GradientLine.GradientDirectionKind.Horizontal,
                StartColor = Color.Transparent,
                MidColor = InkWashTheme.GoldPrimary,
                EndColor = Color.Transparent,
                AnchorPreset = AnchorPresets.TopLeft,
            };
            _previewTitleContainer.AddChild(_titleLineLeft);

            _previewTitleLabel = new Label
            {
                Text = _mockTitle,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 24f),
                TextColor = InkWashTheme.GoldBright,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
            };
            _previewTitleContainer.AddChild(_previewTitleLabel);

            _titleLineRight = new GradientLine
            {
                Direction = GradientLine.GradientDirectionKind.Horizontal,
                StartColor = Color.Transparent,
                MidColor = InkWashTheme.GoldPrimary,
                EndColor = Color.Transparent,
                AnchorPreset = AnchorPresets.TopLeft,
            };
            _previewTitleContainer.AddChild(_titleLineRight);

            _stageTag = new HoverableInkTag
            {
                Text = "武侠能力",
                TagVariant = InkTagVariant.Default,
                AnchorPreset = AnchorPresets.TopLeft,
            };
            _stageTag.MouseEntered += OnStageTagHovered;
            _stageTag.MouseLeft += OnAttributeHoverEnded;
            _leftPanel.AddChild(_stageTag);

            _sectEmblemLabel = new Label
            {
                Text = "\u26F0 青城",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 14f),
                TextColor = WithAlpha(InkWashTheme.PaperDark, 0.5f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
            };
            _leftPanel.AddChild(_sectEmblemLabel);
        }

        // ===================================================================
        // 构建方法 — 右侧属性面板
        // ===================================================================

        /// <summary>
        /// 右侧属性面板：战力区 → 基础属性卡片（2×2） → 进阶属性（2×3） → 装备摘要 → 武学摘要。
        /// </summary>
        private void BuildAttributePanel()
        {
            _attrPanel = new GradientBarPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                GradientDirection = GradientBarPanel.GradientDirectionKind.Vertical,
                GradientColors = new[]
                {
                    WithAlpha(InkWashTheme.BaseSecondary, 0.98f),
                    WithAlpha(InkWashTheme.BaseDefault, 0.98f),
                },
                BorderSide = GradientBarPanel.BorderSideKind.Left,
                BorderColor = InkWashTheme.BorderGold,
            };
            AddChild(_attrPanel);

            _attrPanelScroll = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                ClipChildren = true,
            };
            _attrPanel.AddChild(_attrPanelScroll);

            BuildCombatPowerSection();
            BuildBasicAttributes();
            BuildAdvancedAttributes();
            BuildEquipmentSummary();
            BuildMartialArtsSummary();
        }

        /// <summary>构建装备槽区：15 个 <see cref="InkEquipmentSlot"/> 环绕纸娃娃人体轮廓分布。</summary>
        private void BuildEquipmentSlots()
        {
            _equipmentTitleBar = CreateTitleBar();
            _centerPanel.AddChild(_equipmentTitleBar);

            _equipmentTitleLabel = new Label
            {
                Text = "装备",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 16f),
                TextColor = InkWashTheme.PaperBright,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
            };
            _centerPanel.AddChild(_equipmentTitleLabel);

            _equipmentHintLabel = new Label
            {
                Text = "已装备 0 件",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextTertiary,
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
            };
            _centerPanel.AddChild(_equipmentHintLabel);

            _paperDollBackground = new PaperDollBackground
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(PaperDollWidth, PaperDollHeight),
            };
            _centerPanel.AddChild(_paperDollBackground);

            _equipmentSlots = new InkEquipmentSlot[EquipmentSlotCount];

            for (int i = 0; i < EquipmentSlotCount; i++)
            {
                var slot = new InkEquipmentSlot
                {
                    SlotType = DisplayedSlots[i],
                    AnchorPreset = AnchorPresets.TopLeft,
                    Size = new Float2(EquipmentSlotSize, EquipmentSlotSize),
                };
                slot.DoubleClicked += OnEquipmentSlotDoubleClicked;
                slot.Hovered += OnEquipmentSlotHovered;
                slot.HoverEnded += OnAttributeHoverEnded;
                _centerPanel.AddChild(slot);
                _equipmentSlots[i] = slot;
            }
        }

        /// <summary>构建背包格子区。</summary>
        private void BuildBackpackGrid()
        {
            _backpackTitleBar = CreateTitleBar();
            _rightPanel.AddChild(_backpackTitleBar);

            _backpackTitleLabel = new Label
            {
                Text = "背包",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 16f),
                TextColor = InkWashTheme.PaperBright,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
            };
            _rightPanel.AddChild(_backpackTitleLabel);

            _backpackGrid = new InkBackpackGrid
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Columns = BackpackColumns,
            };
            _backpackGrid.CellHovered += OnBackpackCellHovered;
            _backpackGrid.CellHoverEnded += OnAttributeHoverEnded;
            _backpackGrid.CellDoubleClicked += OnBackpackCellDoubleClicked;
            _backpackGrid.BagSlotDoubleClicked += OnBagSlotDoubleClicked;
            _backpackGrid.BagSlotHovered += OnBagSlotHovered;
            _backpackGrid.BagSlotHoverEnded += OnBagSlotHoverEnded;
            _rightPanel.AddChild(_backpackGrid);
        }

        // ===================================================================
        // 构建方法 — 底部操作栏
        // ===================================================================

        /// <summary>
        /// 底部操作栏：详细属性 / 装备详情 / 武学详情。
        /// </summary>
        private void BuildBottomBar()
        {
            _bottomBar = new GradientBarPanel
            {
                AnchorPreset = AnchorPresets.BottomLeft,
                GradientDirection = GradientBarPanel.GradientDirectionKind.Vertical,
                GradientColors = new[]
                {
                    WithAlpha(InkWashTheme.BaseSecondary, 0.95f),
                    WithAlpha(InkWashTheme.BaseDefault, 0.98f),
                },
                BorderSide = GradientBarPanel.BorderSideKind.Top,
                BorderColor = InkWashTheme.BorderGold,
            };
            AddChild(_bottomBar);

            _bottomActionButtons = new InkButton[3];
            string[] labels = { "详细属性", "装备详情", "武学详情" };
            for (int i = 0; i < _bottomActionButtons.Length; i++)
            {
                var btn = new InkButton
                {
                    Text = labels[i],
                    Variant = InkButtonVariant.Ghost,
                    ButtonSize = InkButtonSize.Md,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Size = new Float2(BottomButtonWidth, BottomButtonHeight),
                };
                btn.ButtonClicked += OnBottomActionButtonClicked;
                _bottomBar.AddChild(btn);
                _bottomActionButtons[i] = btn;
            }
        }

        /// <summary>构建顶层 Tooltip 控件，作为页面最后一个子控件以确保置顶。</summary>
        private void BuildTooltip()
        {
            _tooltip = new InkAttributeTooltip
            {
                AnchorPreset = AnchorPresets.TopLeft,
            };
            AddChild(_tooltip);
        }

        /// <summary>创建金色装饰竖线控件。</summary>
        private ContainerControl CreateTitleBar()
        {
            return new ContainerControl
            {
                BackgroundColor = InkWashTheme.GoldPrimary,
                AnchorPreset = AnchorPresets.TopLeft,
            };
        }

        // ===================================================================
        // 布局计算
        // ===================================================================

        /// <summary>
        /// 根据当前 <see cref="_screenSize"/> 重新计算所有子控件的位置与尺寸。
        /// </summary>
        private void ApplyLayout()
        {
            float sw = _screenSize.X;
            float sh = _screenSize.Y;
            float contentH = sh - ContentTop - ContentBottomReserve;

            // 氛围层全屏
            if (_backgroundLayer != null)
            {
                _backgroundLayer.Location = Float2.Zero;
                _backgroundLayer.Size = new Float2(sw, sh);
            }
            if (_vignette != null)
            {
                _vignette.Location = Float2.Zero;
                _vignette.Size = new Float2(sw, sh);
            }

            // 顶部导航栏
            if (_topBar != null)
            {
                _topBar.Location = Float2.Zero;
                _topBar.Size = new Float2(sw, TopBarHeight);
            }
            LayoutTopBar(sw);

            // 两栏面板：各占 50%，中间留间隙
            float panelW = (sw - PanelGap) * PanelWidthRatio;
            float previewX = PanelGap * 0.5f;
            float attrX = previewX + panelW + PanelGap;

            if (_previewPanel != null)
            {
                _previewPanel.Location = new Float2(previewX, ContentTop);
                _previewPanel.Size = new Float2(panelW, contentH);
            }
            LayoutPreviewPanel(panelW, contentH);

            if (_attrPanel != null)
            {
                _attrPanel.Location = new Float2(attrX, ContentTop);
                _attrPanel.Size = new Float2(panelW, contentH);
            }
            if (_attrPanelScroll != null)
            {
                _attrPanelScroll.Location = new Float2(PanelPadding, PanelPadding);
                _attrPanelScroll.Size = new Float2(panelW - PanelPadding * 2f, contentH - PanelPadding * 2f);
            }
            LayoutAttributePanel(panelW, contentH);

            // 底部操作栏：X 起点对齐属性面板，宽度等于属性面板宽度
            if (_bottomBar != null)
            {
                _bottomBar.Location = new Float2(attrX, sh - BottomBarHeight);
                _bottomBar.Size = new Float2(panelW, BottomBarHeight);
            }
            LayoutBottomBar(panelW);
        }

        /// <summary>布局顶部导航栏内部元素。</summary>
        private void LayoutTopBar(float sw)
        {
            float topY = (TopBarHeight - TopBackButtonSize) * 0.5f;

            if (_topBackButton != null)
                _topBackButton.Location = new Float2(TopBarPadding, topY);

            float titleX = TopBarPadding + TopBackButtonSize + TopBarItemGap;
            if (_topTitleLabel != null)
            {
                _topTitleLabel.Location = new Float2(titleX, 0f);
                _topTitleLabel.Size = new Float2(110f, TopBarHeight);
            }

            float sepX = titleX + 110f + TopBarItemGap;
            if (_topSeparator != null)
            {
                _topSeparator.Location = new Float2(sepX, (TopBarHeight - 20f) * 0.5f);
                _topSeparator.Size = new Float2(1f, 20f);
            }

            float subtitleX = sepX + TopBarItemGap + 1f;
            if (_topSubtitleLabel != null)
            {
                _topSubtitleLabel.Location = new Float2(subtitleX, 0f);
                _topSubtitleLabel.Size = new Float2(120f, TopBarHeight);
            }

            if (_topRightButtons != null)
            {
                float btnX = sw - TopBarPadding;
                for (int i = _topRightButtons.Length - 1; i >= 0; i--)
                {
                    if (_topRightButtons[i] == null)
                        continue;
                    float btnW = 90f;
                    btnX -= btnW;
                    _topRightButtons[i].Location = new Float2(btnX, (TopBarHeight - InkWashTheme.ControlHSm) * 0.5f);
                    _topRightButtons[i].Size = new Float2(btnW, InkWashTheme.ControlHSm);
                    btnX -= TopBarItemGap;
                }
            }
        }

        /// <summary>布局左侧角色预览面板内部子控件。</summary>
        private void LayoutPreviewPanel(float panelWidth, float panelHeight)
        {
            float contentWidth = panelWidth - PanelPadding * 2f;
            float y = PanelPadding;

            // 水墨晕染装饰
            if (_previewSplashTL != null)
                _previewSplashTL.Location = new Float2(-70f, -90f);
            if (_previewSplashBR != null)
                _previewSplashBR.Location = new Float2(panelWidth - _previewSplashBR.Width + 40f, panelHeight - _previewSplashBR.Height + 50f);
            if (_previewSplashBL != null)
                _previewSplashBL.Location = new Float2(-90f, panelHeight * 0.55f);

            // 角色信息区
            if (_previewNameLabel != null)
            {
                _previewNameLabel.Location = new Float2(PanelPadding, y);
                _previewNameLabel.Size = new Float2(contentWidth, 36f);
            }
            y += 36f + 6f;

            if (_previewLevelContainer != null)
            {
                float levelW = 100f;
                _previewLevelContainer.Location = new Float2((panelWidth - levelW) * 0.5f, y);
                _previewLevelContainer.Size = new Float2(levelW, 22f);

                if (_previewLevelPrefixLabel != null)
                {
                    _previewLevelPrefixLabel.Location = new Float2(0f, 0f);
                    _previewLevelPrefixLabel.Size = new Float2(32f, 22f);
                }
                if (_previewLevelValueLabel != null)
                {
                    _previewLevelValueLabel.Location = new Float2(30f, 0f);
                    _previewLevelValueLabel.Size = new Float2(70f, 22f);
                }
            }
            y += 22f + 6f;

            if (_previewSectLabel != null)
            {
                _previewSectLabel.Location = new Float2(PanelPadding, y);
                _previewSectLabel.Size = new Float2(contentWidth, 22f);
            }
            y += 22f + 6f;

            if (_previewTitleContainer != null)
            {
                _previewTitleContainer.Location = new Float2(PanelPadding, y);
                _previewTitleContainer.Size = new Float2(contentWidth, 26f);
            }
            float titleCenterX = contentWidth * 0.5f;
            if (_titleLineLeft != null)
            {
                _titleLineLeft.Location = new Float2(titleCenterX - 90f - 120f, 26f * 0.5f - 1f);
                _titleLineLeft.Size = new Float2(120f, 2f);
            }
            if (_previewTitleLabel != null)
            {
                _previewTitleLabel.Location = new Float2(titleCenterX - 80f, 0f);
                _previewTitleLabel.Size = new Float2(160f, 26f);
            }
            if (_titleLineRight != null)
            {
                _titleLineRight.Location = new Float2(titleCenterX + 90f, 26f * 0.5f - 1f);
                _titleLineRight.Size = new Float2(120f, 2f);
            }
            y += 26f + 6f;

            if (_sectEmblemLabel != null)
            {
                _sectEmblemLabel.Location = new Float2((panelWidth - 80f) * 0.5f, panelHeight - 30f);
                _sectEmblemLabel.Size = new Float2(80f, 20f);
            }
        }

        /// <summary>布局右侧属性面板内部子控件。</summary>
        private void LayoutAttributePanel(float panelWidth, float panelHeight)
        {
            float contentWidth = panelWidth - PanelPadding * 2f;
            float y = 0f;

            y = LayoutCombatPowerSection(0f, y, contentWidth);

            y += SectionGap;
            y = LayoutSectionTitle(_basicAttrTitleLabel, _basicAttrTitleBar, 0f, y, contentWidth);
            if (_basicAttrHintLabel != null)
            {
                _basicAttrHintLabel.Location = new Float2(contentWidth - 80f, y - SectionTitleHeight);
                _basicAttrHintLabel.Size = new Float2(80f, SectionTitleHeight);
            }
            y = LayoutBasicAttributes(0f, y, contentWidth);

            y += SectionGap;
            y = LayoutSectionTitle(_advancedAttrTitleLabel, _advancedAttrTitleBar, 0f, y, contentWidth);
            if (_advancedAttrHintLabel != null)
            {
                _advancedAttrHintLabel.Location = new Float2(contentWidth - 100f, y - SectionTitleHeight);
                _advancedAttrHintLabel.Size = new Float2(100f, SectionTitleHeight);
            }
            y = LayoutAdvancedAttributes(0f, y, contentWidth);

            y += SectionGap;
            y = LayoutSectionTitle(_equipmentTitleLabel, _equipmentTitleBar, 0f, y, contentWidth);
            if (_equipmentHintLabel != null)
            {
                _equipmentHintLabel.Location = new Float2(contentWidth - 70f, y - SectionTitleHeight);
                _equipmentHintLabel.Size = new Float2(70f, SectionTitleHeight);
            }
            y = LayoutEquipmentSummary(0f, y, contentWidth);

            y += SectionGap;
            y = LayoutSectionTitle(_martialArtsTitleLabel, _martialArtsTitleBar, 0f, y, contentWidth);
            if (_martialArtsHintLabel != null)
            {
                _martialArtsHintLabel.Location = new Float2(contentWidth - 80f, y - SectionTitleHeight);
                _martialArtsHintLabel.Size = new Float2(80f, SectionTitleHeight);
            }
            y = LayoutMartialArtsSummary(0f, y, contentWidth);
        }

        /// <summary>布局战力区。</summary>
        private float LayoutCombatPowerSection(float x, float y, float width)
        {
            float labelRowY = y;
            if (_combatPowerTitleLabel != null)
            {
                _combatPowerTitleLabel.Location = new Float2(x, labelRowY);
                _combatPowerTitleLabel.Size = new Float2(80f, 20f);
            }
            if (_combatPowerStageTag != null)
            {
                _combatPowerStageTag.Location = new Float2(x + width - 80f, labelRowY);
                _combatPowerStageTag.Size = new Float2(80f, 20f);
            }
            y += 22f;

            if (_combatPowerValue != null)
            {
                _combatPowerValue.Location = new Float2(x, y);
                _combatPowerValue.Size = new Float2(width - 80f, 56f);
            }
            if (_combatPowerTrendLabel != null)
            {
                _combatPowerTrendLabel.Location = new Float2(x + width - 70f, y + 10f);
                _combatPowerTrendLabel.Size = new Float2(20f, 20f);
            }
            if (_combatPowerDeltaLabel != null)
            {
                _combatPowerDeltaLabel.Location = new Float2(x + width - 50f, y + 10f);
                _combatPowerDeltaLabel.Size = new Float2(50f, 20f);
            }
            y += 58f;

            if (_combatPowerBar != null)
            {
                _combatPowerBar.Location = new Float2(x, y);
                _combatPowerBar.Size = new Float2(width, 8f);
            }
            y += 12f;

            if (_combatPowerBarCurrentLabel != null)
            {
                _combatPowerBarCurrentLabel.Location = new Float2(x, y);
                _combatPowerBarCurrentLabel.Size = new Float2(60f, 16f);
            }
            if (_combatPowerBarNextLabel != null)
            {
                _combatPowerBarNextLabel.Location = new Float2(x + width - 120f, y);
                _combatPowerBarNextLabel.Size = new Float2(120f, 16f);
            }
            y += 18f;

            return y;
        }

        /// <summary>布局中间预览面板。</summary>
        private void LayoutCenterPanel(float panelWidth, float panelHeight)
        {
            if (_centerBgLayer != null)
            {
                _centerBgLayer.Location = Float2.Zero;
                _centerBgLayer.Size = new Float2(panelWidth, panelHeight);
            }

            if (_centerSplashTL != null)
                _centerSplashTL.Location = new Float2(-70f, -90f);
            if (_centerSplashTR != null)
                _centerSplashTR.Location = new Float2(panelWidth - _centerSplashTR.Width + 60f, -60f);
            if (_centerSplashBR != null)
                _centerSplashBR.Location = new Float2(panelWidth - _centerSplashBR.Width + 40f, panelHeight - _centerSplashBR.Height + 50f);
            if (_centerSplashBL != null)
                _centerSplashBL.Location = new Float2(-90f, panelHeight * 0.55f);

            float contentWidth = panelWidth - PanelPadding * 2f;

            // 合并布局：3D 预览位于上部居中，装备槽位于 3D 预览下方（紧凑网格，不遮挡 3D 渲染区域）
            float previewX = (panelWidth - Preview3DWidth) * 0.5f;
            float previewY = PanelPadding;

            if (_preview3D != null)
            {
                _preview3D.Location = new Float2(previewX, previewY);
                _preview3D.Size = new Float2(Preview3DWidth, Preview3DHeight);
                try
                {
                    _preview3D.RefreshLayout();
                }
                catch (Exception ex)
                {
                    FlaxEngine.Debug.LogError($"[MenuCharAttributesV2Page] _preview3D.RefreshLayout 失败: {ex.Message}");
                }
            }

            // 装备区：3D 预览下方，装备槽采用紧凑网格布局（LayoutEquipmentSlots）
            float equipY = previewY + Preview3DHeight + SectionGap;
            LayoutSectionTitle(_equipmentTitleLabel, _equipmentTitleBar, PanelPadding, equipY, contentWidth);
            if (_equipmentHintLabel != null)
            {
                _equipmentHintLabel.Location = new Float2(PanelPadding + contentWidth - 80f, equipY);
                _equipmentHintLabel.Size = new Float2(80f, SectionTitleHeight);
            }
            LayoutEquipmentSlots(PanelPadding, equipY + SectionTitleHeight, contentWidth);
        }

        /// <summary>布局右侧装备面板内部子控件（从上到下）。</summary>
        private void LayoutRightPanel(float panelWidth, float panelHeight)
        {
            float contentWidth = panelWidth - PanelPadding * 2f;
            float y = PanelPadding;

            // 春色晕染：位于面板顶部偏左，部分溢出顶部与左侧，与左面板呼应
            if (_rightSpringSplash != null)
            {
                _rightSpringSplash.Location = new Float2(-150f, -110f);
            }

            // 金色辉光：位于武学摘要区附近（面板中下部偏右），柔化武学卡片
            if (_rightGoldGlow != null)
            {
                _rightGoldGlow.Location = new Float2(panelWidth * 0.55f, panelHeight * 0.55f);
            }

            // 1. 背包区
            y = LayoutSectionTitle(_backpackTitleLabel, _backpackTitleBar, PanelPadding, y, contentWidth);
            y = LayoutBackpack(PanelPadding, y, contentWidth);

            // 2. 武学摘要区
            y += SectionGap;
            y = LayoutSectionTitle(_martialArtsTitleLabel, _martialArtsTitleBar, PanelPadding, y, contentWidth);
            y = LayoutMartialArtsSummary(PanelPadding, y, contentWidth);
        }

        /// <summary>布局底部操作栏内部按钮（在底栏宽度内居中）。</summary>
        private void LayoutBottomBar(float panelWidth)
        {
            if (_bottomActionButtons == null)
                return;

            float totalBtnW = _bottomActionButtons.Length * BottomButtonWidth + (_bottomActionButtons.Length - 1) * TopBarItemGap;
            float startX = (panelWidth - totalBtnW) * 0.5f;
            float btnY = (BottomBarHeight - BottomButtonHeight) * 0.5f;

            for (int i = 0; i < _bottomActionButtons.Length; i++)
            {
                if (_bottomActionButtons[i] == null)
                    continue;
                _bottomActionButtons[i].Location = new Float2(startX + i * (BottomButtonWidth + TopBarItemGap), btnY);
                _bottomActionButtons[i].Size = new Float2(BottomButtonWidth, BottomButtonHeight);
            }
        }

        /// <summary>布局分区标题与左侧金色装饰竖线。</summary>
        private float LayoutSectionTitle(Label title, ContainerControl bar, float x, float y, float width)
        {
            if (bar != null)
            {
                bar.Location = new Float2(x, y + (SectionTitleHeight - 16f) * 0.5f);
                bar.Size = new Float2(TitleBarWidth, 16f);
            }

            if (title != null)
            {
                float titleX = x + TitleBarWidth + TitleBarToTextGap;
                title.Location = new Float2(titleX, y);
                title.Size = new Float2(Mathf.Max(0f, width - (titleX - x)), SectionTitleHeight);
            }

            return y + SectionTitleHeight;
        }

        /// <summary>布局基础属性区 2×2 卡片。</summary>
        private float LayoutBasicAttributes(float x, float y, float width)
        {
            float cardW = (width - BasicAttrCardGapX) * 0.5f;

            for (int i = 0; i < BasicAttrCount; i++)
            {
                int col = i % BasicAttrColumns;
                int row = i / BasicAttrColumns;
                float cardX = x + col * (cardW + BasicAttrCardGapX);
                float cardY = y + row * (BasicAttrCardHeight + BasicAttrCardGapY);

                if (_basicAttrCards[i] != null)
                {
                    _basicAttrCards[i].Location = new Float2(cardX, cardY);
                    _basicAttrCards[i].Size = new Float2(cardW, BasicAttrCardHeight);
                }

                float iconX = 12f;
                float iconY = (BasicAttrCardHeight - BasicAttrIconSize) * 0.5f;
                if (_basicAttrIcons[i] != null)
                {
                    _basicAttrIcons[i].Location = new Float2(iconX, iconY);
                    _basicAttrIcons[i].Size = new Float2(BasicAttrIconSize, BasicAttrIconSize);
                }

                float textX = iconX + BasicAttrIconSize + 8f;
                if (_basicAttrNameLabels[i] != null)
                {
                    _basicAttrNameLabels[i].Location = new Float2(textX, 10f);
                    _basicAttrNameLabels[i].Size = new Float2(cardW - textX - 8f, 18f);
                }

                if (_basicAttrValueLabels[i] != null)
                {
                    _basicAttrValueLabels[i].Location = new Float2(textX, 32f);
                    _basicAttrValueLabels[i].Size = new Float2(cardW - textX - 60f, 22f);
                }

                if (_basicAttrUnitLabels[i] != null)
                {
                    _basicAttrUnitLabels[i].Location = new Float2(textX + cardW - textX - 60f + 50f, 34f);
                    _basicAttrUnitLabels[i].Size = new Float2(20f, 18f);
                }

                if (_basicAttrTrendLabels[i] != null)
                {
                    _basicAttrTrendLabels[i].Location = new Float2(cardW - 44f, 34f);
                    _basicAttrTrendLabels[i].Size = new Float2(36f, 18f);
                }
            }

            int rows = (BasicAttrCount + BasicAttrColumns - 1) / BasicAttrColumns;
            return y + rows * BasicAttrCardHeight + (rows - 1) * BasicAttrCardGapY;
        }

        /// <summary>布局进阶属性区（2 列布局）。</summary>
        private float LayoutAdvancedAttributes(float x, float y, float width)
        {
            float columnWidth = (width - AdvancedAttrColumnGap) * 0.5f;

            for (int i = 0; i < AdvancedAttrCount; i++)
            {
                int col = i % AdvancedAttrColumns;
                int row = i / AdvancedAttrColumns;
                float colX = x + col * (columnWidth + AdvancedAttrColumnGap);
                float rowY = y + row * AdvancedAttrRowHeight;

                if (_advancedAttrCards[i] != null)
                {
                    _advancedAttrCards[i].Location = new Float2(colX, rowY);
                    _advancedAttrCards[i].Size = new Float2(columnWidth, AdvancedAttrRowHeight);
                }

                // 图标 + 间距 4 + 标签 + 数值
                if (_advancedAttrIcons[i] != null)
                {
                    _advancedAttrIcons[i].Location = new Float2(8f, 4f);
                    _advancedAttrIcons[i].Size = new Float2(12f, 18f);
                }

                if (_advancedAttrNameLabels[i] != null)
                {
                    _advancedAttrNameLabels[i].Location = new Float2(24f, 4f);
                    _advancedAttrNameLabels[i].Size = new Float2(columnWidth * 0.45f, 18f);
                }

                if (_advancedAttrValueLabels[i] != null)
                {
                    _advancedAttrValueLabels[i].Location = new Float2(8f, 22f);
                    _advancedAttrValueLabels[i].Size = new Float2(columnWidth * 0.55f, 22f);
                }
            }

            int rows = (AdvancedAttrCount + AdvancedAttrColumns - 1) / AdvancedAttrColumns;
            return y + rows * AdvancedAttrRowHeight;
        }

        /// <summary>布局雷达图区。</summary>
        private float LayoutRadarChart(float x, float y, float width)
        {
            if (_radarChart != null)
            {
                float chartX = x + (width - RadarChartSize) * 0.5f;
                _radarChart.Location = new Float2(chartX, y);
                _radarChart.Size = new Float2(RadarChartSize, RadarChartSize);
            }
            return y + RadarChartSize;
        }

        /// <summary>布局装备摘要区（6 个装备槽水平排列）。</summary>
        private float LayoutEquipmentSummary(float x, float y, float width)
        {
            float slotW = (width - EquipmentSummaryGap * (EquipmentSummaryColumns - 1)) / EquipmentSummaryColumns;

            for (int i = 0; i < EquipmentSummaryCount; i++)
            {
                float slotX = x + i * (slotW + EquipmentSummaryGap);
                float slotY = y;

                if (_equipmentSummaryCards[i] != null)
                {
                    _equipmentSummaryCards[i].Location = new Float2(slotX, slotY);
                    _equipmentSummaryCards[i].Size = new Float2(slotW, EquipmentSummaryIconSize + 30f);
                }

                if (_equipmentSummaryIcons[i] != null)
                {
                    _equipmentSummaryIcons[i].Location = new Float2(2f, 2f);
                    _equipmentSummaryIcons[i].Size = new Float2(EquipmentSummaryIconSize - 4f, EquipmentSummaryIconSize - 4f);
                }

                if (_equipmentSummaryNameLabels[i] != null)
                {
                    _equipmentSummaryNameLabels[i].Location = new Float2(0f, EquipmentSummaryIconSize + 4f);
                    _equipmentSummaryNameLabels[i].Size = new Float2(slotW, 14f);
                }

                if (_equipmentSummaryTypeLabels[i] != null)
                {
                    _equipmentSummaryTypeLabels[i].Location = new Float2(0f, EquipmentSummaryIconSize + 18f);
                    _equipmentSummaryTypeLabels[i].Size = new Float2(slotW, 12f);
                }

                if (_equipmentSummaryEnhanceLabels[i] != null)
                {
                    _equipmentSummaryEnhanceLabels[i].Location = new Float2(slotW - 24f, 4f);
                    _equipmentSummaryEnhanceLabels[i].Size = new Float2(20f, 14f);
                }
            }

            return y + EquipmentSummaryIconSize + 30f;
        }

        /// <summary>布局装备槽区（纸娃娃人体环绕布局）。</summary>
        private float LayoutEquipmentSlots(float x, float y, float width)
        {
            // 紧凑网格布局：3 列 × 5 行（与 DisplayedSlots 顺序一致），居中显示。
            // 取代原纸娃娃人体拓扑，避免装备槽与 3D 预览渲染区域重叠。
            const int columns = 3;
            const int rows = 5;
            float gap = 8f;

            float gridWidth = columns * EquipmentSlotSize + (columns - 1) * gap;
            float gridHeight = rows * EquipmentSlotSize + (rows - 1) * gap;
            float startX = x + (width - gridWidth) * 0.5f;

            // 纸娃娃背景不再按人体拓扑布局：隐藏装饰控件，避免与网格布局冲突
            if (_paperDollBackground != null)
            {
                _paperDollBackground.Visible = false;
            }

            for (int i = 0; i < EquipmentSlotCount; i++)
            {
                if (_equipmentSlots == null || i >= _equipmentSlots.Length || _equipmentSlots[i] == null)
                    continue;

                int col = i % columns;
                int row = i / columns;
                float slotX = startX + col * (EquipmentSlotSize + gap);
                float slotY = y + row * (EquipmentSlotSize + gap);

                _equipmentSlots[i].Location = new Float2(slotX, slotY);
                _equipmentSlots[i].Size = new Float2(EquipmentSlotSize, EquipmentSlotSize);
            }

            return y + gridHeight;
        }

        /// <summary>布局背包格子区。</summary>
        private float LayoutBackpack(float x, float y, float width)
        {
            if (_backpackGrid != null)
            {
                _backpackGrid.Location = new Float2(x, y);
                _backpackGrid.Size = new Float2(width, BackpackHeight);
            }

            return y + BackpackHeight;
        }

        /// <summary>布局武学摘要区卡片。</summary>
        private float LayoutMartialArtsSummary(float x, float y, float width)
        {
            for (int i = 0; i < MartialArtsCount; i++)
            {
                float cardY = y + i * (MartialArtsCardHeight + MartialArtsCardGap);
                if (_martialArtsCards[i] != null)
                {
                    _martialArtsCards[i].Location = new Float2(x, cardY);
                    _martialArtsCards[i].Size = new Float2(width, MartialArtsCardHeight);
                }

                if (_martialArtsIcons[i] != null)
                {
                    _martialArtsIcons[i].Location = new Float2(12f, (MartialArtsCardHeight - 42f) * 0.5f);
                    _martialArtsIcons[i].Size = new Float2(42f, 42f);
                }

                if (_martialArtsNameLabels[i] != null)
                {
                    _martialArtsNameLabels[i].Location = new Float2(64f, 10f);
                    _martialArtsNameLabels[i].Size = new Float2(width * 0.45f, 22f);
                }

                if (_martialArtsQualityTags[i] != null)
                {
                    _martialArtsQualityTags[i].Location = new Float2(width * 0.55f, 12f);
                    _martialArtsQualityTags[i].Size = new Float2(50f, 18f);
                }

                // 元信息双段布局：图标 + 间距 2 + 文本 + 间距 8 + 图标 + 间距 2 + 文本
                const float metaY = 34f;
                const float metaH = 18f;
                float metaX = 64f;
                if (_martialArtsMetaTypeIcons[i] != null)
                {
                    _martialArtsMetaTypeIcons[i].Location = new Float2(metaX, metaY);
                    _martialArtsMetaTypeIcons[i].Size = new Float2(14f, metaH);
                }
                metaX += 14f + 2f;
                if (_martialArtsMetaTypeTexts[i] != null)
                {
                    _martialArtsMetaTypeTexts[i].Location = new Float2(metaX, metaY);
                    _martialArtsMetaTypeTexts[i].Size = new Float2(36f, metaH);
                }
                metaX += 36f + 8f;
                if (_martialArtsMetaLevelIcons[i] != null)
                {
                    _martialArtsMetaLevelIcons[i].Location = new Float2(metaX, metaY);
                    _martialArtsMetaLevelIcons[i].Size = new Float2(14f, metaH);
                }
                metaX += 14f + 2f;
                if (_martialArtsMetaLevelTexts[i] != null)
                {
                    _martialArtsMetaLevelTexts[i].Location = new Float2(metaX, metaY);
                    _martialArtsMetaLevelTexts[i].Size = new Float2(50f, metaH);
                }

                if (_martialArtsPowerLabels[i] != null)
                {
                    _martialArtsPowerLabels[i].Location = new Float2(width - 80f, 14f);
                    _martialArtsPowerLabels[i].Size = new Float2(70f, 22f);
                }

                if (_martialArtsPowerLabelLabels[i] != null)
                {
                    _martialArtsPowerLabelLabels[i].Location = new Float2(width - 80f, 38f);
                    _martialArtsPowerLabelLabels[i].Size = new Float2(70f, 14f);
                }

                if (_martialArtsLeftBars[i] != null)
                {
                    _martialArtsLeftBars[i].Location = new Float2(0f, 0f);
                    _martialArtsLeftBars[i].Size = new Float2(3f, MartialArtsCardHeight);
                }
            }

            return y + MartialArtsCount * MartialArtsCardHeight + (MartialArtsCount - 1) * MartialArtsCardGap;
        }

        /// <summary>
        /// 在屏幕尺寸变化时重新布局所有子控件。
        /// </summary>
        public void RefreshLayout()
        {
            float w = Width;
            float h = Height;
            if (w <= 0f || h <= 0f)
            {
                var screen = FlaxEngine.Screen.Size;
                w = screen.X;
                h = screen.Y;
            }
            if (w <= 0f || h <= 0f)
            {
                w = 1920f;
                h = 1080f;
            }
            _screenSize = new Float2(w, h);
            ApplyLayout();
        }

        // ===================================================================
        // 数据绑定 API
        // ===================================================================

        /// <summary>
        /// 绑定角色属性组件，刷新全部显示（昵称、等级、阶段、3D 预览、属性、装备、雷达图）。
        /// 传入 null 时回退 mock 数据。
        /// </summary>
        /// <param name="component">角色属性组件，null 解除绑定</param>
        public void BindCharacter(CharacterAttributesComponent component)
        {
            _boundCharacter = component;

            try
            {
                if (_boundCharacter != null)
                {
                    string nickname = !string.IsNullOrEmpty(_boundCharacter.Nickname) ? _boundCharacter.Nickname : _mockName;
                    if (_previewNameLabel != null)
                        _previewNameLabel.Text = nickname;
                    if (_previewLevelValueLabel != null)
                        _previewLevelValueLabel.Text = _boundCharacter.Level.ToString();

                    UpdateStageTag(_boundCharacter.CurrentStage);

                    if (_preview3D != null)
                        _preview3D.SetCharacter(_boundCharacter);
                }
                else
                {
                    if (_previewNameLabel != null)
                        _previewNameLabel.Text = _mockName;
                    if (_previewLevelValueLabel != null)
                        _previewLevelValueLabel.Text = _mockLevel.ToString();
                    UpdateStageTag(CharacterStage.Wuxia);
                }

                RefreshAllData();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[MenuCharAttributesV2Page] BindCharacter 失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 根据成长阶段更新阶段标签的文本与配色。
        /// </summary>
        private void UpdateStageTag(CharacterStage stage)
        {
            if (_stageTag == null)
                return;

            _currentStage = stage;

            try
            {
                switch (stage)
                {
                    case CharacterStage.Wuxia:
                        _stageTag.Text = "武侠能力";
                        _stageTag.TagVariant = InkTagVariant.Default;
                        break;
                    case CharacterStage.Xianxia:
                        _stageTag.Text = "仙侠能力";
                        _stageTag.TagVariant = InkTagVariant.Brand;
                        break;
                    case CharacterStage.Xuanhuan:
                        _stageTag.Text = "玄幻能力";
                        _stageTag.TagVariant = InkTagVariant.Vermilion;
                        break;
                    default:
                        _stageTag.Text = "武侠能力";
                        _stageTag.TagVariant = InkTagVariant.Default;
                        break;
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[MenuCharAttributesV2Page] UpdateStageTag 失败: {ex.Message}");
            }
        }

        /// <summary>刷新全部数据（属性、装备、武学、战力）。</summary>
        private void RefreshAllData()
        {
            RecalculateAttributes();
            RefreshAdvancedAttributes();
            RefreshMartialArts();
        }

        // ===================================================================
        // 装备状态管理
        // ===================================================================

        /// <summary>
        /// 初始化 mock 装备数据。
        /// </summary>
        private void InitializeMockEquipment()
        {
            try
            {
                _equippedItems.Clear();
                _backpackItems.Clear();

                var allEquipments = EquipmentDatabase.GetAllEquipments();
                if (allEquipments != null)
                {
                    _backpackItems.AddRange(allEquipments);
                }

                EquipFromBackpack(EquipmentDatabase.DefaultBody);
                EquipFromBackpack(EquipmentDatabase.DefaultLongsword);
                EquipFromBackpack(EquipmentDatabase.DefaultHeadScarf);
                EquipFromBackpack(EquipmentDatabase.DefaultNecklace);
                EquipFromBackpack(EquipmentDatabase.DefaultShoulderGuard);
                EquipFromBackpack(EquipmentDatabase.DefaultCloak);
                EquipFromBackpack(EquipmentDatabase.DefaultBelt);
                EquipFromBackpack(EquipmentDatabase.DefaultLeggings);
                EquipFromBackpack(EquipmentDatabase.DefaultShoes);
                EquipFromBackpack(EquipmentDatabase.DefaultDagger);

                // 默认装备 1 个小包到背包槽 0（演示魔兽世界式背包系统）
                _equippedBags.Clear();
                _backpackItems.Remove(EquipmentDatabase.SmallClothBag);
                _equippedBags.Add(new EquippedBag(0, EquipmentDatabase.SmallClothBagId, EquipmentDatabase.SmallClothBag.ExtraSlots));

                RefreshEquipmentSlots();
                RefreshBackpack();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[MenuCharAttributesV2Page] InitializeMockEquipment 失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 将装备从背包装备到对应槽位（仅初始化用，不触发刷新）。
        /// </summary>
        private void EquipFromBackpack(EquipmentData equipment)
        {
            if (equipment == null)
                return;

            _backpackItems.Remove(equipment);
            _equippedItems[equipment.Slot] = equipment;
        }

        /// <summary>刷新所有装备槽显示。</summary>
        private void RefreshEquipmentSlots()
        {
            if (_equipmentSlots == null)
                return;

            try
            {
                int equippedCount = 0;
                for (int i = 0; i < _equipmentSlots.Length; i++)
                {
                    if (_equipmentSlots[i] == null)
                        continue;

                    EquipmentSlot slotType = DisplayedSlots[i];
                    EquipmentData equipped = _equippedItems.ContainsKey(slotType) ? _equippedItems[slotType] : null;

                    _equipmentSlots[i].Refresh(equipped);

                    if (equipped != null)
                    {
                        equippedCount++;
                        if (!string.IsNullOrEmpty(equipped.IconPath))
                        {
                            try
                            {
                                _equipmentSlots[i].Icon = Content.LoadAsync<Texture>(equipped.IconPath);
                            }
                            catch (Exception ex)
                            {
                                FlaxEngine.Debug.LogWarning($"[MenuCharAttributesV2Page] 加载装备图标失败: {equipped.IconPath} — {ex.Message}");
                            }
                        }
                        else
                        {
                            _equipmentSlots[i].Icon = null;
                        }
                    }
                    else
                    {
                        _equipmentSlots[i].Icon = null;
                    }
                }

                if (_equipmentHintLabel != null)
                    _equipmentHintLabel.Text = $"已装备 {equippedCount} 件";
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[MenuCharAttributesV2Page] RefreshEquipmentSlots 失败: {ex.Message}");
            }
        }

        /// <summary>刷新背包格子显示（同步更新背包槽与扩展格）。</summary>
        private void RefreshBackpack()
        {
            if (_backpackGrid == null)
                return;

            try
            {
                _backpackGrid.Populate(_backpackItems, _equippedBags);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[MenuCharAttributesV2Page] RefreshBackpack 失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 同步背包槽数据并刷新背包显示。
        /// 注：当前 _boundCharacter 为 CharacterAttributesComponent，未直接持有 ECS InventoryComponent，
        /// 故使用 mock 数据 _equippedBags；待接入 ECS world/playerId 后从此处读取 BagSlots。
        /// </summary>
        private void UpdateBagSlots()
        {
            // TODO: 接入 ECS 后，若 _boundCharacter 持有 InventoryComponent，则从其 BagSlots 读取
            // if (_boundCharacter != null) { _equippedBags = ... ; }
            RefreshBackpack();
        }

        /// <summary>
        /// 装备槽双击事件处理：卸下装备到背包。
        /// </summary>
        private void OnEquipmentSlotDoubleClicked(EquipmentSlot slot)
        {
            try
            {
                if (!_equippedItems.ContainsKey(slot))
                    return;

                if (_backpackGrid != null && _backpackGrid.IsFull)
                {
                    FlaxEngine.Debug.LogWarning("[MenuCharAttributesV2Page] 背包已满，无法卸下装备");
                    return;
                }

                EquipmentData equipment = _equippedItems[slot];
                _equippedItems.Remove(slot);
                _backpackItems.Add(equipment);

                RecalculateAttributes();
                RefreshEquipmentSlots();
                RefreshBackpack();

                FlaxEngine.Debug.Log($"[MenuCharAttributesV2Page] 卸下装备: {equipment.Name} (槽位: {slot})");
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[MenuCharAttributesV2Page] OnEquipmentSlotDoubleClicked 失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 背包格子双击事件处理：装备到对应槽位。
        /// </summary>
        private void OnBackpackCellDoubleClicked(int cellIndex)
        {
            try
            {
                if (cellIndex < 0 || cellIndex >= _backpackItems.Count)
                    return;

                EquipmentData equipment = _backpackItems[cellIndex];
                if (equipment == null)
                    return;

                // 背包类型物品 → 装备到背包槽（魔兽世界式扩展背包系统）
                if (equipment.Type == EquipmentType.Bag)
                {
                    EquipBagFromBackpack(cellIndex, equipment);
                    return;
                }

                int characterLevel = _boundCharacter?.Level ?? _mockLevel;
                if (equipment.RequiredLevel > characterLevel)
                {
                    FlaxEngine.Debug.LogWarning($"[MenuCharAttributesV2Page] 等级不足，需要 Lv.{equipment.RequiredLevel}，当前 Lv.{characterLevel}");
                    return;
                }

                EquipmentSlot targetSlot = equipment.Slot;

                EquipmentData previousEquipment = null;
                if (_equippedItems.ContainsKey(targetSlot))
                {
                    previousEquipment = _equippedItems[targetSlot];
                }

                _equippedItems[targetSlot] = equipment;
                _backpackItems.RemoveAt(cellIndex);

                if (previousEquipment != null)
                {
                    _backpackItems.Add(previousEquipment);
                }

                RecalculateAttributes();
                RefreshEquipmentSlots();
                RefreshBackpack();

                FlaxEngine.Debug.Log($"[MenuCharAttributesV2Page] 装备: {equipment.Name} (槽位: {targetSlot})");
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[MenuCharAttributesV2Page] OnBackpackCellDoubleClicked 失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 将背包装备物品装备到第一个空闲背包槽（0-3）。
        /// 注：当前未接入 ECS InventorySystem，直接操作 _equippedBags；
        /// 接入后替换为 _inventorySystem.EquipBag(world, playerId, bagTemplateId, bagSlotIndex)。
        /// </summary>
        private void EquipBagFromBackpack(int cellIndex, EquipmentData bagEquipment)
        {
            int freeIndex = FindFreeBagSlot();
            if (freeIndex < 0)
            {
                FlaxEngine.Debug.LogWarning("[MenuCharAttributesV2Page] 背包槽已满，无法装备新背包");
                return;
            }

            _equippedBags.Add(new EquippedBag(freeIndex, bagEquipment.Id, bagEquipment.ExtraSlots));
            _backpackItems.RemoveAt(cellIndex);

            RefreshBackpack();
            FlaxEngine.Debug.Log($"[MenuCharAttributesV2Page] 装备背包: {bagEquipment.Name} (背包槽: {freeIndex + 1}, 扩展格: +{bagEquipment.ExtraSlots})");
        }

        /// <summary>查找第一个空闲背包槽（0-3），无空闲返回 -1。</summary>
        private int FindFreeBagSlot()
        {
            for (int i = 0; i < InventoryComponent.MaxBagSlots; i++)
            {
                bool occupied = false;
                for (int j = 0; j < _equippedBags.Count; j++)
                {
                    if (_equippedBags[j].BagSlotIndex == i)
                    {
                        occupied = true;
                        break;
                    }
                }
                if (!occupied)
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// 背包槽双击事件处理：卸下该槽位的扩展背包。
        /// 卸下后背包装备放回 _backpackItems；若扩展格有物品导致容量不足则拒绝卸下。
        /// 注：当前未接入 ECS InventorySystem，直接操作 _equippedBags；
        /// 接入后替换为 _inventorySystem.UnequipBag(world, playerId, bagSlotIndex)。
        /// </summary>
        private void OnBagSlotDoubleClicked(int bagSlotIndex)
        {
            try
            {
                int foundIndex = -1;
                EquippedBag foundBag = default;
                for (int i = 0; i < _equippedBags.Count; i++)
                {
                    if (_equippedBags[i].BagSlotIndex == bagSlotIndex)
                    {
                        foundIndex = i;
                        foundBag = _equippedBags[i];
                        break;
                    }
                }

                if (foundIndex < 0)
                {
                    FlaxEngine.Debug.LogWarning($"[MenuCharAttributesV2Page] 背包槽 {bagSlotIndex + 1} 未装备背包，无需卸下");
                    return;
                }

                // 容量校验：卸下后新容量需容纳原物品 + 放回的背包装备本身
                int currentExtra = 0;
                for (int i = 0; i < _equippedBags.Count; i++)
                    currentExtra += _equippedBags[i].ExtraSlots;
                int newCapacity = InventoryComponent.BaseCapacity + (currentExtra - foundBag.ExtraSlots);
                int newCount = _backpackItems.Count + 1; // 背包装备放回占用 1 格
                if (newCount > newCapacity)
                {
                    FlaxEngine.Debug.LogWarning($"[MenuCharAttributesV2Page] 背包槽 {bagSlotIndex + 1} 扩展格有物品，无法卸下背包");
                    return;
                }

                _equippedBags.RemoveAt(foundIndex);

                EquipmentData bagEquipment = EquipmentDatabase.GetEquipment(foundBag.TemplateId);
                if (bagEquipment != null)
                {
                    _backpackItems.Add(bagEquipment);
                }

                RefreshBackpack();
                FlaxEngine.Debug.Log($"[MenuCharAttributesV2Page] 卸下背包: 槽位 {bagSlotIndex + 1}");
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[MenuCharAttributesV2Page] OnBagSlotDoubleClicked 失败: {ex.Message}");
            }
        }

        /// <summary>背包槽悬停：显示该槽位装备的背包详情或空槽提示。</summary>
        private void OnBagSlotHovered(Float2 screenPos, int bagSlotIndex, EquippedBag? equippedBag)
        {
            if (_tooltip == null)
                return;

            try
            {
                if (equippedBag.HasValue)
                {
                    EquippedBag bag = equippedBag.Value;
                    EquipmentData bagEquipment = EquipmentDatabase.GetEquipment(bag.TemplateId);
                    string bagName = bagEquipment != null ? bagEquipment.Name : $"背包#{bag.TemplateId}";
                    int quality = bagEquipment != null ? bagEquipment.Quality : 0;
                    string[] qualityNames = { "普通", "优秀", "精良", "史诗", "传说", "传世" };
                    string qualityName = quality < qualityNames.Length ? qualityNames[quality] : "未知";

                    string coreInfo = $"背包槽：{bagSlotIndex + 1}\n品质：{qualityName}\n扩展格子：+{bag.ExtraSlots}";
                    string additionalInfo = bagEquipment != null ? (bagEquipment.Description ?? "已装备的扩展背包。") : "已装备的扩展背包。";
                    _tooltip.SetData(null, bagName, coreInfo, additionalInfo, null);

                    Color? qualityColor = quality switch
                    {
                        0 => InkWashTheme.QualityCommon,
                        1 => InkWashTheme.QualityUncommon,
                        2 => InkWashTheme.QualityRare,
                        3 => InkWashTheme.QualityEpic,
                        4 => InkWashTheme.QualityLegendary,
                        5 => InkWashTheme.QualityLegendary,
                        _ => null
                    };
                    _tooltip.SetQualityBorderColor(qualityColor);
                }
                else
                {
                    string coreInfo = $"背包槽：{bagSlotIndex + 1}";
                    string additionalInfo = "未装备背包\n双击背包物品可装备到此槽位";
                    _tooltip.SetData(null, "空背包槽", coreInfo, additionalInfo, null);
                    _tooltip.SetQualityBorderColor(null);
                }

                _tooltip.Show(screenPos);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[MenuCharAttributesV2Page] OnBagSlotHovered 失败: {ex.Message}");
            }
        }

        /// <summary>背包槽悬停结束：隐藏 Tooltip。</summary>
        private void OnBagSlotHoverEnded()
        {
            if (_tooltip != null)
                _tooltip.Hide();
        }

        // ===================================================================
        // 属性重新计算
        // ===================================================================

        /// <summary>
        /// 重新计算角色属性：基础属性 = 角色基础值 + 装备 BaseStats 加成；
        /// 五行属性 = 角色基础值 + 装备 WuxingBonus 加成。
        /// 更新基础属性条、雷达图、战力数值。
        /// </summary>
        private void RecalculateAttributes()
        {
            try
            {
                CharacterAttributes attr = _boundCharacter != null
                    ? CharacterAttributes.FromComponent(_boundCharacter)
                    : CharacterAttributes.GetDefault();

                _basicAttrBaseValues[0] = attr.HP;
                _basicAttrBaseValues[1] = attr.Attack;
                _basicAttrBaseValues[2] = attr.Defense;
                _basicAttrBaseValues[3] = 15f;

                for (int i = 0; i < BasicAttrCount; i++)
                {
                    _basicAttrBonusValues[i] = 0f;
                }

                foreach (var kvp in _equippedItems)
                {
                    EquipmentData equipment = kvp.Value;
                    if (equipment?.BaseStats == null)
                        continue;

                    if (equipment.BaseStats.ContainsKey("HP"))
                        _basicAttrBonusValues[0] += equipment.BaseStats["HP"];
                    if (equipment.BaseStats.ContainsKey("Attack"))
                        _basicAttrBonusValues[1] += equipment.BaseStats["Attack"];
                    if (equipment.BaseStats.ContainsKey("Defense"))
                        _basicAttrBonusValues[2] += equipment.BaseStats["Defense"];
                    if (equipment.BaseStats.ContainsKey("CritRate"))
                        _basicAttrBonusValues[3] += equipment.BaseStats["CritRate"];
                }

                for (int i = 0; i < BasicAttrCount; i++)
                {
                    _basicAttrTotalValues[i] = _basicAttrBaseValues[i] + _basicAttrBonusValues[i];
                }

                RefreshBasicAttributes();
                RefreshCombatPower();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[MenuCharAttributesV2Page] RecalculateAttributes 失败: {ex.Message}");
            }
        }

        /// <summary>刷新基础属性 4 项的数值 Label。</summary>
        private void RefreshBasicAttributes()
        {
            if (_basicAttrValueLabels == null)
                return;

            for (int i = 0; i < BasicAttrCount; i++)
            {
                float total = _basicAttrTotalValues[i];
                if (_basicAttrValueLabels[i] != null)
                {
                    _basicAttrValueLabels[i].Text = BasicAttrIsPercent[i]
                        ? $"{total:F1}"
                        : $"{(int)total}";
                }
            }
        }

        /// <summary>刷新雷达图数据（五行 + 关键属性）。</summary>
        private void RefreshRadarChart()
        {
            if (_radarChart == null)
                return;

            try
            {
                _radarChart.SetWuxingValues(
                    _wuxingTotalValues[0],
                    _wuxingTotalValues[1],
                    _wuxingTotalValues[2],
                    _wuxingTotalValues[3],
                    _wuxingTotalValues[4]);

                float attack = _basicAttrTotalValues[0] * 0.1f;
                float defense = _basicAttrTotalValues[1];
                float hp = _basicAttrTotalValues[0] * 0.1f;
                float crit = _mockAdvancedAttrValues[0] * 100f;
                float hit = _mockAdvancedAttrValues[2] * 100f;
                float dodge = _mockAdvancedAttrValues[3] * 100f;

                _radarChart.SetKeyAttributeValues(new float[] { attack, defense, hp, crit, hit, dodge });

                float maxVal = 200f;
                for (int i = 0; i < 5; i++)
                {
                    if (_wuxingTotalValues[i] > maxVal)
                        maxVal = _wuxingTotalValues[i];
                }
                _radarChart.MaxValue = maxVal;
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[MenuCharAttributesV2Page] RefreshRadarChart 失败: {ex.Message}");
            }
        }

        /// <summary>刷新进阶属性 6 项。</summary>
        private void RefreshAdvancedAttributes()
        {
            if (_advancedAttrValueLabels == null)
                return;

            for (int i = 0; i < AdvancedAttrCount; i++)
            {
                if (i < _advancedAttrValueLabels.Length && _advancedAttrValueLabels[i] != null)
                {
                    string valueText = AdvancedAttrIsPercent[i]
                        ? $"{AdvancedAttrValues[i]}"
                        : $"{(int)AdvancedAttrValues[i]}";
                    _advancedAttrValueLabels[i].Text = valueText;
                }
            }
        }

        /// <summary>刷新武学摘要 3 项 mock。</summary>
        private void RefreshMartialArts()
        {
            if (_martialArtsNameLabels == null)
                return;

            for (int i = 0; i < _martialArtsNameLabels.Length && i < _mockMartialArts.Length; i++)
            {
                if (_martialArtsNameLabels[i] != null)
                    _martialArtsNameLabels[i].Text = _mockMartialArts[i].name;
            }
        }

        /// <summary>刷新战力数值（基础属性总和 * 10 + 装备加成 * 5）。</summary>
        private void RefreshCombatPower()
        {
            if (_combatPowerValue == null)
                return;

            float sum = 0f;
            for (int i = 0; i < BasicAttrCount; i++)
            {
                sum += _basicAttrTotalValues[i];
            }
            int combatPower = (int)(sum * 10f);
            _combatPowerValue.Text = combatPower.ToString("N0");

            if (_combatPowerBar != null)
                _combatPowerBar.Value = Mathf.Clamp(combatPower / 100000f, 0f, 1f);

            int stageIndex = Mathf.Clamp(combatPower / 20000, 0, CombatPowerStageNames.Length - 1);
            if (_combatPowerStageTag != null)
                _combatPowerStageTag.Text = CombatPowerStageNames[stageIndex];

            if (stageIndex + 1 < CombatPowerStageNames.Length && _combatPowerBarNextLabel != null)
                _combatPowerBarNextLabel.Text = $"下一阶 · {CombatPowerStageNames[stageIndex + 1]}";
        }

        // ===================================================================
        // Tooltip 事件处理
        // ===================================================================

        /// <summary>基础属性名悬停：显示当前值/基础值/装备加成/属性说明。</summary>
        private void OnBasicAttrHovered(int attrIndex, Float2 localPos)
        {
            if (_tooltip == null || attrIndex < 0 || attrIndex >= BasicAttrCount)
                return;

            try
            {
                // 使用触发控件本地坐标转换，确保与 UI 坐标系（窗口客户区）一致，
                // 避免 MouseScreenPosition 在编辑器/窗口模式下使用显示器坐标导致的偏移。
                Float2 screenPos = _basicAttrNameLabels[attrIndex] != null
                    ? _basicAttrNameLabels[attrIndex].PointToScreen(localPos)
                    : FlaxEngine.Input.MouseScreenPosition;
                float current = _basicAttrTotalValues[attrIndex];
                float baseVal = _basicAttrBaseValues[attrIndex];
                float bonus = _basicAttrBonusValues[attrIndex];

                string coreInfo = $"当前：{(int)current}\n基础：{(int)baseVal}\n装备加成：+{(int)bonus}";
                string additionalInfo = attrIndex < BasicAttrDescriptions.Length
                    ? BasicAttrDescriptions[attrIndex]
                    : string.Empty;

                _tooltip.SetData(null, BasicAttrNames[attrIndex], coreInfo, additionalInfo, null);
                _tooltip.SetQualityBorderColor(null);
                _tooltip.Show(screenPos);
                _lastHoverTime = Time.UnscaledGameTime;
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[MenuCharAttributesV2Page] OnBasicAttrHovered 失败: {ex.Message}");
            }
        }

        /// <summary>进阶属性名悬停：显示百分比与具体数值。</summary>
        private void OnAdvancedAttrHovered(int attrIndex, Float2 localPos)
        {
            if (_tooltip == null || attrIndex < 0 || attrIndex >= AdvancedAttrCount)
                return;

            try
            {
                // 使用触发控件本地坐标转换，确保与 UI 坐标系一致
                Float2 screenPos = _advancedAttrNameLabels[attrIndex] != null
                    ? _advancedAttrNameLabels[attrIndex].PointToScreen(localPos)
                    : FlaxEngine.Input.MouseScreenPosition;
                float percent = _mockAdvancedAttrValues[attrIndex] * 100f;
                string name = attrIndex < _mockAdvancedAttrNames.Length ? _mockAdvancedAttrNames[attrIndex] : "未知";

                string[] descriptions = {
                    "攻击触发暴击的概率，造成额外伤害",
                    "降低被暴击的概率",
                    "攻击命中目标的概率",
                    "躲避攻击的概率"
                };
                string additionalInfo = attrIndex < descriptions.Length ? descriptions[attrIndex] : string.Empty;

                string coreInfo = $"当前：{percent:F0}%\n具体数值：{percent * 10:F0}";
                _tooltip.SetData(null, name, coreInfo, additionalInfo, null);
                _tooltip.SetQualityBorderColor(null);
                _tooltip.Show(screenPos);
                _lastHoverTime = Time.UnscaledGameTime;
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[MenuCharAttributesV2Page] OnAdvancedAttrHovered 失败: {ex.Message}");
            }
        }

        /// <summary>装备槽悬停：已装备显示装备详情，空槽显示槽位类型提示。</summary>
        private void OnEquipmentSlotHovered(EquipmentSlot slot, Float2 screenPos)
        {
            if (_tooltip == null)
                return;

            try
            {
                FlaxEngine.Debug.Log($"[MenuCharAttributesV2Page] OnEquipmentSlotHovered slot={slot} screenPos={screenPos}");
                if (!_equippedItems.ContainsKey(slot) || _equippedItems[slot] == null)
                {
                    // 空槽提示：槽位名称 + 说明
                    string slotName = GetSlotDisplayName(slot);
                    string coreInfo = "未装备";
                    string additionalInfo = $"可装备：{GetSlotEquipableHint(slot)}";
                    _tooltip.SetData(null, slotName, coreInfo, additionalInfo, null);
                    _tooltip.SetQualityBorderColor(null);
                    _tooltip.Show(screenPos);
                    return;
                }

                EquipmentData equipment = _equippedItems[slot];
                ShowEquipmentTooltip(equipment, screenPos);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[MenuCharAttributesV2Page] OnEquipmentSlotHovered 失败: {ex.Message}");
            }
        }

        /// <summary>获取装备槽位的显示名称。</summary>
        private static string GetSlotDisplayName(EquipmentSlot slot)
        {
            return slot switch
            {
                EquipmentSlot.Head => "头部",
                EquipmentSlot.Neck => "颈部",
                EquipmentSlot.Shoulder => "肩部",
                EquipmentSlot.Back => "背部",
                EquipmentSlot.Body => "身体",
                EquipmentSlot.Waist => "腰部",
                EquipmentSlot.Legs => "腿部",
                EquipmentSlot.Feet => "脚部",
                EquipmentSlot.RightHand => "右手",
                EquipmentSlot.LeftHand => "左手",
                EquipmentSlot.RightRing => "右戒指",
                EquipmentSlot.LeftRing => "左戒指",
                EquipmentSlot.RightWrist => "右手腕",
                EquipmentSlot.LeftWrist => "左手腕",
                EquipmentSlot.Face => "面部",
                _ => slot.ToString()
            };
        }

        /// <summary>获取装备槽位可装备的物品类型提示。</summary>
        private static string GetSlotEquipableHint(EquipmentSlot slot)
        {
            return slot switch
            {
                EquipmentSlot.Head => "头盔、帽子",
                EquipmentSlot.Neck => "项链、护符",
                EquipmentSlot.Shoulder => "肩甲、披肩",
                EquipmentSlot.Back => "披风、斗篷",
                EquipmentSlot.Body => "衣服、铠甲",
                EquipmentSlot.Waist => "腰带",
                EquipmentSlot.Legs => "裤子、护腿",
                EquipmentSlot.Feet => "鞋子、靴子",
                EquipmentSlot.RightHand => "主武器（剑、刀、枪等）",
                EquipmentSlot.LeftHand => "副武器、盾牌",
                EquipmentSlot.RightRing => "戒指",
                EquipmentSlot.LeftRing => "戒指",
                EquipmentSlot.RightWrist => "护腕、手镯",
                EquipmentSlot.LeftWrist => "护腕、手镯",
                EquipmentSlot.Face => "面具、面纱",
                _ => "装备"
            };
        }

        /// <summary>背包格子悬停：有装备显示装备详情，空格子显示空槽提示。</summary>
        private void OnBackpackCellHovered(int cellIndex, Float2 screenPos)
        {
            if (_tooltip == null)
                return;

            try
            {
                // 空格子或超出范围 → 显示"空格子"提示
                if (cellIndex < 0 || cellIndex >= _backpackItems.Count || _backpackItems[cellIndex] == null)
                {
                    _tooltip.SetData(null, "空格子", "未存放物品", "双击装备槽可卸下装备到背包", null);
                    _tooltip.SetQualityBorderColor(null);
                    _tooltip.Show(screenPos);
                    return;
                }

                EquipmentData equipment = _backpackItems[cellIndex];
                ShowEquipmentTooltip(equipment, screenPos);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[MenuCharAttributesV2Page] OnBackpackCellHovered 失败: {ex.Message}");
            }
        }

        /// <summary>雷达图顶点悬停：显示对应属性详情。</summary>
        private void OnRadarChartTooltipRequested(int group, int index, Float2 localPos)
        {
            if (_tooltip == null)
                return;

            try
            {
                Float2 screenPos = _radarChart != null
                    ? _radarChart.PointToScreen(localPos)
                    : FlaxEngine.Input.MouseScreenPosition;

                if (group == 0)
                {
                    string[] wuxingNames = { "金", "木", "水", "火", "土", "中和" };
                    string name = index < wuxingNames.Length ? wuxingNames[index] : "未知";
                    float value = index < 5 ? _wuxingTotalValues[index] : 0f;
                    string coreInfo = $"亲和度：{(int)value}";
                    string additionalInfo = index < 5
                        ? $"{name}属性亲和度，影响对应五行武学效果。"
                        : "五行平衡度，各项亲和度的平均值。";
                    _tooltip.SetData(null, name, coreInfo, additionalInfo, null);
                }
                else
                {
                    string[] keyAttrNames = { "攻击", "防御", "气血", "暴击", "命中", "闪避" };
                    string name = index < keyAttrNames.Length ? keyAttrNames[index] : "未知";

                    float value = 0f;
                    switch (index)
                    {
                        case 0: value = _basicAttrTotalValues[0] * 0.1f; break;
                        case 1: value = _basicAttrTotalValues[1]; break;
                        case 2: value = _basicAttrTotalValues[0] * 0.1f; break;
                        case 3: value = _mockAdvancedAttrValues[0] * 100f; break;
                        case 4: value = _mockAdvancedAttrValues[2] * 100f; break;
                        case 5: value = _mockAdvancedAttrValues[3] * 100f; break;
                    }

                    string coreInfo = $"数值：{value:F0}";
                    string additionalInfo = $"关键战斗属性：{name}";
                    _tooltip.SetData(null, name, coreInfo, additionalInfo, null);
                }

                _tooltip.SetQualityBorderColor(null);
                _tooltip.Show(screenPos);
                _lastHoverTime = Time.UnscaledGameTime;
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[MenuCharAttributesV2Page] OnRadarChartTooltipRequested 失败: {ex.Message}");
            }
        }

        /// <summary>显示装备 Tooltip（图标+名称+品质+等级+属性加成列表）。</summary>
        private void ShowEquipmentTooltip(EquipmentData equipment, Float2 screenPos)
        {
            if (_tooltip == null || equipment == null)
                return;

            try
            {
                Texture icon = null;
                if (!string.IsNullOrEmpty(equipment.IconPath))
                {
                    try
                    {
                        icon = Content.LoadAsync<Texture>(equipment.IconPath);
                    }
                    catch
                    {
                        icon = null;
                    }
                }

                string[] qualityNames = { "普通", "优秀", "精良", "史诗", "传说", "传世" };
                string qualityName = equipment.Quality < qualityNames.Length
                    ? qualityNames[equipment.Quality]
                    : "未知";

                string coreInfo = $"品质：{qualityName}\n等级：Lv.{equipment.ItemLevel}\n需求等级：Lv.{equipment.RequiredLevel}";
                string additionalInfo = equipment.Description ?? string.Empty;

                var appendableItems = new List<string>();
                if (equipment.BaseStats != null)
                {
                    foreach (var kvp in equipment.BaseStats)
                    {
                        appendableItems.Add($"{kvp.Key} +{kvp.Value}");
                    }
                }
                if (equipment.WuxingBonus != null)
                {
                    foreach (var kvp in equipment.WuxingBonus)
                    {
                        string elementName = kvp.Key switch
                        {
                            WuxingElement.Metal => "金",
                            WuxingElement.Wood => "木",
                            WuxingElement.Water => "水",
                            WuxingElement.Fire => "火",
                            WuxingElement.Earth => "土",
                            _ => kvp.Key.ToString()
                        };
                        appendableItems.Add($"{elementName} +{kvp.Value}");
                    }
                }

                _tooltip.SetData(icon, equipment.Name, coreInfo, additionalInfo, appendableItems);

                // 根据装备品质设置边框强调色，强化品质识别度
                Color? qualityColor = equipment.Quality switch
                {
                    0 => InkWashTheme.QualityCommon,
                    1 => InkWashTheme.QualityUncommon,
                    2 => InkWashTheme.QualityRare,
                    3 => InkWashTheme.QualityEpic,
                    4 => InkWashTheme.QualityLegendary,
                    5 => InkWashTheme.QualityLegendary, // 传世品质用朱红
                    _ => null
                };
                _tooltip.SetQualityBorderColor(qualityColor);

                _tooltip.Show(screenPos);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[MenuCharAttributesV2Page] ShowEquipmentTooltip 失败: {ex.Message}");
            }
        }

        /// <summary>阶段标签悬停：显示当前阶段的名称与描述。</summary>
        private void OnStageTagHovered(Float2 localPos)
        {
            if (_tooltip == null || _stageTag == null)
                return;

            try
            {
                string stageName;
                string stageDescription;
                switch (_currentStage)
                {
                    case CharacterStage.Xianxia:
                        stageName = "仙侠";
                        stageDescription = "仙侠阶段（51-150级），踏入仙途，灵力觉醒";
                        break;
                    case CharacterStage.Xuanhuan:
                        stageName = "玄幻";
                        stageDescription = "玄幻阶段（151-300级），元力通神，逆天改命";
                        break;
                    default:
                        stageName = "武侠";
                        stageDescription = "武侠阶段（1-50级），以武技与内力为主";
                        break;
                }

                // 使用触发控件本地坐标转换，确保与 UI 坐标系一致
                Float2 screenPos = _stageTag != null
                    ? _stageTag.PointToScreen(localPos)
                    : FlaxEngine.Input.MouseScreenPosition;
                _tooltip.SetData(null, stageName, stageDescription, "阶段决定角色能力上限", null);
                _tooltip.SetQualityBorderColor(null);
                _tooltip.Show(screenPos);
                _lastHoverTime = Time.UnscaledGameTime;
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[MenuCharAttributesV2Page] OnStageTagHovered 失败: {ex.Message}");
            }
        }

        /// <summary>属性悬停结束：隐藏 Tooltip。</summary>
        private void OnAttributeHoverEnded()
        {
            if (_tooltip != null)
                _tooltip.Hide();
        }

        // ===================================================================
        // 生命周期
        // ===================================================================

        /// <summary>
        /// 每帧更新。
        /// 绑定角色组件时同步基础属性与战力数值。
        /// Tooltip 由悬停目标的 MouseLeave/HoverEnded 事件驱动隐藏，不再使用定时器。
        /// </summary>
        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);

            try
            {
                if (_boundCharacter != null)
                {
                    RecalculateAttributes();
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[MenuCharAttributesV2Page] Update 失败: {ex.Message}");
            }
        }

        // ===================================================================
        // 事件处理
        // ===================================================================

        /// <summary>顶部返回按钮点击处理。</summary>
        private void OnBackToHud()
        {
            try
            {
                NavigationRequested?.Invoke("back-hud");
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[MenuCharAttributesV2Page] NavigationRequested(back-hud) 触发失败: {ex.Message}");
            }
        }

        /// <summary>顶部稀有商品按钮点击处理。</summary>
        private void OnTopButtonRareItems(Button button)
        {
            FlaxEngine.Debug.Log("[MenuCharAttributesV2Page] 稀有商品功能待落地");
        }

        /// <summary>顶部详细属性按钮点击处理。</summary>
        private void OnTopButtonDetailedAttributes(Button button)
        {
            FlaxEngine.Debug.Log("[MenuCharAttributesV2Page] 详细属性功能待落地");
        }

        /// <summary>顶部分享按钮点击处理。</summary>
        private void OnTopButtonShare(Button button)
        {
            FlaxEngine.Debug.Log("[MenuCharAttributesV2Page] 分享功能待落地");
        }

        /// <summary>底部操作按钮点击处理。</summary>
        private void OnBottomActionButtonClicked(Button button)
        {
            try
            {
                if (_bottomActionButtons == null)
                    return;

                for (int i = 0; i < _bottomActionButtons.Length; i++)
                {
                    if (_bottomActionButtons[i] == button)
                    {
                        string target = i switch
                        {
                            0 => "detailed-attributes",
                            1 => "equipment-details",
                            2 => "martial-arts-details",
                            _ => string.Empty
                        };
                        if (!string.IsNullOrEmpty(target))
                            NavigationRequested?.Invoke(target);
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[MenuCharAttributesV2Page] OnBottomActionButtonClicked 失败: {ex.Message}");
            }
        }

        // ===================================================================
        // 辅助方法
        // ===================================================================

        /// <summary>获取品质中文名。</summary>
        private static string QualityName(InkWashTheme.InkQuality quality)
        {
            return quality switch
            {
                InkWashTheme.InkQuality.Common => "普通",
                InkWashTheme.InkQuality.Uncommon => "优秀",
                InkWashTheme.InkQuality.Rare => "精良",
                InkWashTheme.InkQuality.Epic => "史诗",
                InkWashTheme.InkQuality.Legendary => "传说",
                _ => "普通"
            };
        }

        /// <summary>获取品质对应标签变体（设计方案 §4.1 Legendary=鎏金，非朱红）。</summary>
        private static InkTagVariant QualityTagVariant(InkWashTheme.InkQuality quality)
        {
            return quality switch
            {
                InkWashTheme.InkQuality.Legendary => InkTagVariant.Brand,
                InkWashTheme.InkQuality.Epic => InkTagVariant.Brand,
                _ => InkTagVariant.Default
            };
        }

        // ===================================================================
        // 渲染辅助方法（供嵌套卡片控件共享）
        // ===================================================================

        /// <summary>返回带指定 Alpha 通道的颜色副本。</summary>
        /// <param name="color">源颜色</param>
        /// <param name="alpha">目标透明度（0~1）</param>
        /// <returns>仅替换 Alpha 的新颜色</returns>
        private static Color WithAlpha(Color color, float alpha)
        {
            return new Color(color.R, color.G, color.B, alpha);
        }

        /// <summary>
        /// 绘制 135° 对角线渐变（左上 → 右下）：
        /// 将矩形分为 strips×strips 网格，每个格子的颜色基于其对角线位置插值
        /// t = (col/w + row/h) / 2，模拟 CSS 135° 对角线渐变。
        /// 相比纯水平/垂直条带，网格方案在视觉上更贴近真实对角线方向。
        /// </summary>
        /// <param name="bounds">目标矩形</param>
        /// <param name="topLeft">左上角颜色</param>
        /// <param name="bottomRight">右下角颜色</param>
        /// <param name="strips">每边分段数（默认 6，即 6×6=36 格）</param>
        private static void DrawDiagonalGradient(Rectangle bounds, Color topLeft, Color bottomRight, int strips = 6)
        {
            if (strips <= 0 || bounds.Height <= 0f || bounds.Width <= 0f)
                return;

            float cellW = bounds.Width / strips;
            float cellH = bounds.Height / strips;
            for (int row = 0; row < strips; row++)
            {
                for (int col = 0; col < strips; col++)
                {
                    // 对角线插值：t = (x/w + y/h) / 2，模拟 135° 对角线
                    float t = ((col + 0.5f) / strips + (row + 0.5f) / strips) * 0.5f;
                    Color c = Color.Lerp(topLeft, bottomRight, t);
                    // +1f 像素覆盖格子间隙，避免渐变中出现细缝
                    var rect = new Rectangle(
                        bounds.X + col * cellW,
                        bounds.Y + row * cellH,
                        cellW + 1f,
                        cellH + 1f);
                    Render2D.FillRectangle(rect, c);
                }
            }
        }

        // ===================================================================
        // 嵌套类：纸娃娃人体轮廓背景
        // ===================================================================

        /// <summary>
        /// 纸娃娃人体轮廓背景。
        /// 参考 <c>menu-equipment.html</c> 中 SVG 角色轮廓，使用简单几何图形
        /// （圆形 + 矩形）在装备槽后方绘制半透明的人体占位轮廓，
        /// 帮助玩家将装备槽与身体部位对应。
        /// </summary>
        private class PaperDollBackground : ContainerControl
        {
            /// <summary>轮廓填充色（纸色，极淡）</summary>
            private static readonly Color BodyFill = new Color(
                InkWashTheme.PaperBright.R,
                InkWashTheme.PaperBright.G,
                InkWashTheme.PaperBright.B,
                0.05f);

            /// <summary>轮廓描边色（纸色，更淡）</summary>
            private static readonly Color BodyStroke = new Color(
                InkWashTheme.PaperBright.R,
                InkWashTheme.PaperBright.G,
                InkWashTheme.PaperBright.B,
                0.08f);

            /// <summary>地面阴影色</summary>
            private static readonly Color ShadowFill = new Color(0f, 0f, 0f, 0.25f);

            public PaperDollBackground()
            {
                BackgroundColor = Color.Transparent;
                ClipChildren = false;
                AutoFocus = false;
                Enabled = false; // 装饰性控件，禁用交互避免拦截装备槽鼠标事件
            }

            /// <inheritdoc />
            public override void Draw()
            {
                if (!Visible || Width <= 0f || Height <= 0f)
                    return;

                float w = Width;
                float h = Height;
                float cx = w * 0.5f;

                // 地面阴影
                InkRenderHelper.FillRadialGradient(
                    new Float2(cx, h * 0.88f),
                    w * 0.35f,
                    ShadowFill,
                    Color.Transparent,
                    12);

                // 头部
                InkRenderHelper.FillCircle(new Float2(cx, h * 0.08f), w * 0.09f, BodyFill);

                // 颈部
                Render2D.FillRectangle(
                    new Rectangle(cx - w * 0.03f, h * 0.125f, w * 0.06f, h * 0.03f),
                    BodyFill);

                // 肩部（左 + 右）
                Render2D.FillRectangle(
                    new Rectangle(cx - w * 0.32f, h * 0.155f, w * 0.26f, h * 0.04f),
                    BodyFill);
                Render2D.FillRectangle(
                    new Rectangle(cx + w * 0.06f, h * 0.155f, w * 0.26f, h * 0.04f),
                    BodyFill);

                // 胸部/身躯
                Render2D.FillRectangle(
                    new Rectangle(cx - w * 0.18f, h * 0.19f, w * 0.36f, h * 0.22f),
                    BodyFill);
                Render2D.DrawRectangle(
                    new Rectangle(cx - w * 0.18f, h * 0.19f, w * 0.36f, h * 0.22f),
                    BodyStroke, 1f);

                // 手臂
                Render2D.FillRectangle(
                    new Rectangle(cx - w * 0.34f, h * 0.18f, w * 0.10f, h * 0.26f),
                    BodyFill);
                Render2D.FillRectangle(
                    new Rectangle(cx + w * 0.24f, h * 0.18f, w * 0.10f, h * 0.26f),
                    BodyFill);

                // 手腕
                Render2D.FillRectangle(
                    new Rectangle(cx - w * 0.36f, h * 0.355f, w * 0.08f, h * 0.03f),
                    BodyFill);
                Render2D.FillRectangle(
                    new Rectangle(cx + w * 0.28f, h * 0.355f, w * 0.08f, h * 0.03f),
                    BodyFill);

                // 腰部
                Render2D.FillRectangle(
                    new Rectangle(cx - w * 0.14f, h * 0.41f, w * 0.28f, h * 0.08f),
                    BodyFill);

                // 腿部
                Render2D.FillRectangle(
                    new Rectangle(cx - w * 0.14f, h * 0.49f, w * 0.11f, h * 0.30f),
                    BodyFill);
                Render2D.FillRectangle(
                    new Rectangle(cx + w * 0.03f, h * 0.49f, w * 0.11f, h * 0.30f),
                    BodyFill);

                // 脚部
                Render2D.FillRectangle(
                    new Rectangle(cx - w * 0.18f, h * 0.79f, w * 0.14f, h * 0.04f),
                    BodyFill);
                Render2D.FillRectangle(
                    new Rectangle(cx + w * 0.04f, h * 0.79f, w * 0.14f, h * 0.04f),
                    BodyFill);
            }
        }

        // ===================================================================
        // 嵌套类：中间预览区背景图层
        // ===================================================================

        /// <summary>
        /// 中间 3D 预览区背景图层。
        /// 绘制背景纹理 <see cref="InkWashTheme.TexAssetPathCharPreviewV2"/>，
        /// 并叠加深墨黑渐变遮罩，确保角色信息可读。
        /// 对应 HTML 原型中的 <c>.char-preview-bg</c> + <c>.char-preview-overlay</c>。
        /// </summary>
        private class PreviewBackgroundLayer : ContainerControl
        {
            /// <summary>背景纹理</summary>
            private Texture _backgroundTexture;

            /// <summary>纹理是否尝试加载过</summary>
            private bool _textureRequested;

            /// <summary>左上角渐变遮罩色</summary>
            private static readonly Color OverlayTopLeft = new Color(
                InkWashTheme.BaseDefault.R,
                InkWashTheme.BaseDefault.G,
                InkWashTheme.BaseDefault.B,
                0.50f);

            /// <summary>中央渐变遮罩色</summary>
            private static readonly Color OverlayCenter = new Color(
                InkWashTheme.BaseDefault.R,
                InkWashTheme.BaseDefault.G,
                InkWashTheme.BaseDefault.B,
                0.30f);

            /// <summary>右下角渐变遮罩色</summary>
            private static readonly Color OverlayBottomRight = new Color(
                InkWashTheme.BaseDefault.R,
                InkWashTheme.BaseDefault.G,
                InkWashTheme.BaseDefault.B,
                0.60f);

            public PreviewBackgroundLayer()
            {
                BackgroundColor = InkWashTheme.BaseDefault;
                ClipChildren = false;
                AutoFocus = false;
                Enabled = false; // 装饰性控件，禁用交互避免拦截鼠标事件
            }

            /// <inheritdoc />
            public override void Draw()
            {
                if (!Visible || Width <= 0f || Height <= 0f)
                    return;

                var bounds = new Rectangle(0, 0, Width, Height);

                // 1. 深墨黑底色
                Render2D.FillRectangle(bounds, BackgroundColor);

                // 2. 背景图（按需加载）
                EnsureBackgroundTexture();
                if (_backgroundTexture != null && _backgroundTexture.IsLoaded)
                {
                    Render2D.DrawTexture(_backgroundTexture, bounds, Color.White);
                }

                // 3. 渐变遮罩（左上 → 中央 → 右下）
                var topLeft = new Float2(Width * 0.25f, Height * 0.25f);
                var bottomRight = new Float2(Width * 0.75f, Height * 0.75f);
                float radius = Mathf.Max(Width, Height) * 0.55f;

                InkRenderHelper.FillRadialGradient(topLeft, radius, OverlayTopLeft, Color.Transparent, 16);
                InkRenderHelper.FillRadialGradient(
                    new Float2(Width * 0.5f, Height * 0.5f),
                    radius * 0.8f,
                    OverlayCenter,
                    Color.Transparent,
                    16);
                InkRenderHelper.FillRadialGradient(bottomRight, radius, OverlayBottomRight, Color.Transparent, 16);
            }

            /// <summary>按需异步加载背景纹理。</summary>
            private void EnsureBackgroundTexture()
            {
                if (_textureRequested)
                    return;

                _textureRequested = true;
                try
                {
                    if (!string.IsNullOrEmpty(InkWashTheme.TexAssetPathCharPreviewV2))
                    {
                        _backgroundTexture = Content.LoadAsync<Texture>(InkWashTheme.TexAssetPathCharPreviewV2);
                    }
                }
                catch (Exception ex)
                {
                    FlaxEngine.Debug.LogWarning($"[PreviewBackgroundLayer] 加载角色预览背景图失败: {ex.Message}");
                }
            }
        }

        // ===================================================================
        // 嵌套类：支持悬停事件的 Label
        // ===================================================================

        /// <summary>
        /// 支持鼠标悬停事件的 Label。
        /// </summary>
        private class HoverableLabel : Label
        {
            /// <summary>属性索引</summary>
            public int AttributeIndex = -1;

            /// <summary>鼠标进入事件</summary>
            public event Action<int, Float2> MouseEntered;

            /// <summary>鼠标离开事件</summary>
            public event Action MouseLeft;

            /// <inheritdoc />
            public override void OnMouseEnter(Float2 location)
            {
                base.OnMouseEnter(location);
                try
                {
                    MouseEntered?.Invoke(AttributeIndex, location);
                }
                catch (Exception ex)
                {
                    FlaxEngine.Debug.LogError($"[HoverableLabel] MouseEntered 触发失败: {ex.Message}");
                }
            }

            /// <inheritdoc />
            public override void OnMouseLeave()
            {
                base.OnMouseLeave();
                try
                {
                    MouseLeft?.Invoke();
                }
                catch (Exception ex)
                {
                    FlaxEngine.Debug.LogError($"[HoverableLabel] MouseLeft 触发失败: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 支持鼠标悬停事件的 <see cref="InkTag"/>。
        /// </summary>
        private class HoverableInkTag : InkTag
        {
            /// <summary>鼠标进入事件</summary>
            public event Action<Float2> MouseEntered;

            /// <summary>鼠标离开事件</summary>
            public event Action MouseLeft;

            /// <inheritdoc />
            public override void OnMouseEnter(Float2 location)
            {
                base.OnMouseEnter(location);
                try
                {
                    MouseEntered?.Invoke(location);
                }
                catch (Exception ex)
                {
                    FlaxEngine.Debug.LogError($"[HoverableInkTag] MouseEntered 触发失败: {ex.Message}");
                }
            }

            /// <inheritdoc />
            public override void OnMouseLeave()
            {
                base.OnMouseLeave();
                try
                {
                    MouseLeft?.Invoke();
                }
                catch (Exception ex)
                {
                    FlaxEngine.Debug.LogError($"[HoverableInkTag] MouseLeft 触发失败: {ex.Message}");
                }
            }
        }

        // ===================================================================
        // 嵌套类：带金色辉光的战力数值 Label
        // ===================================================================

        /// <summary>
        /// 支持文字辉光阴影的 Label。
        /// 在正式文字绘制前，先以半透明金色在多方向偏移绘制模糊文字，
        /// 模拟 CSS text-shadow 的辉光效果，强化战力数字视觉焦点。
        /// </summary>
        private class GlowLabel : Label
        {
            /// <summary>辉光颜色（半透明金色，对应 CSS text-shadow 色）</summary>
            public Color GlowColor = new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.2f);

            /// <summary>辉光偏移半径（像素），越大越模糊</summary>
            public float GlowRadius = 2f;

            /// <summary>3 层辉光参数：偏移半径与 alpha 逐层递减，形成扩散辉光</summary>
            private static readonly float[] _layerRadii = { 2f, 4f, 8f };
            private static readonly float[] _layerAlphas = { 0.15f, 0.10f, 0.05f };

            /// <inheritdoc />
            public override void Draw()
            {
                // 先绘制 3 层辉光：逐层增大偏移（2/4/8px）、降低 alpha（0.15/0.10/0.05）
                if (Visible && Width > 0f && Height > 0f && GlowColor.A > 0f)
                {
                    var font = Font.GetFont();
                    if (font != null && !string.IsNullOrEmpty(Text))
                    {
                        for (int layer = 0; layer < _layerRadii.Length; layer++)
                        {
                            Color c = WithAlpha(GlowColor, _layerAlphas[layer]);
                            float r = _layerRadii[layer];
                            // 8 方向偏移：模拟模糊扩散
                            Float2[] offsets =
                            {
                                new Float2(r, 0), new Float2(-r, 0),
                                new Float2(0, r), new Float2(0, -r),
                                new Float2(r, r), new Float2(-r, r),
                                new Float2(r, -r), new Float2(-r, -r),
                            };
                            foreach (var off in offsets)
                            {
                                var glowRect = new Rectangle(off.X, off.Y, Width, Height);
                                Render2D.DrawText(font, Text, glowRect, c,
                                    HorizontalAlignment, VerticalAlignment, TextWrapping.NoWrap);
                            }
                        }
                    }
                }

                // 再绘制正式文字（含背景）
                base.Draw();
            }
        }

        // ===================================================================
        // 嵌套类：带描边的图标 Label
        // ===================================================================

        /// <summary>
        /// 支持 1px 描边的 Label，用于基础属性图标与武学图标容器。
        /// 在背景与文字绘制完成后叠加描边，实现五行分色/品质分色边框。
        /// </summary>
        private class BorderedIcon : Label
        {
            /// <summary>描边颜色</summary>
            public Color IconBorderColor = Color.Transparent;

            /// <summary>描边厚度（像素）</summary>
            public float IconBorderThickness = 1f;

            /// <inheritdoc />
            public override void Draw()
            {
                base.Draw();
                if (IconBorderColor.A > 0f && IconBorderThickness > 0f && Width > 0f && Height > 0f)
                {
                    Render2D.DrawRectangle(
                        new Rectangle(0, 0, Width, Height),
                        IconBorderColor,
                        IconBorderThickness);
                }
            }
        }

        // ===================================================================
        // 嵌套类：基础属性卡片（对角线渐变 + hover）
        // ===================================================================

        /// <summary>
        /// 基础属性卡片控件。
        /// 以 6 条横向条带模拟 135° 对角线渐变背景（左上→右下），
        /// hover 时切换为更亮的渐变并使用金色边框，强化交互反馈。
        /// </summary>
        private class BasicAttrCard : ContainerControl
        {
            /// <summary>是否被鼠标悬停</summary>
            private bool _isHovered;

            public BasicAttrCard()
            {
                // 渐变由 Draw 自行绘制，背景透明避免叠加
                BackgroundColor = Color.Transparent;
                ClipChildren = false;
            }

            /// <inheritdoc />
            public override void OnMouseEnter(Float2 location)
            {
                base.OnMouseEnter(location);
                _isHovered = true;
            }

            /// <inheritdoc />
            public override void OnMouseLeave()
            {
                base.OnMouseLeave();
                _isHovered = false;
            }

            /// <inheritdoc />
            public override void Draw()
            {
                if (Visible && Width > 0f && Height > 0f)
                {
                    var bounds = new Rectangle(0, 0, Width, Height);
                    Color topLeft, bottomRight, borderColor;
                    if (_isHovered)
                    {
                        // hover：更亮渐变 + 金色边框
                        topLeft = WithAlpha(InkWashTheme.BaseElevated, 0.7f);
                        bottomRight = WithAlpha(InkWashTheme.BaseTertiary, 0.7f);
                        borderColor = InkWashTheme.BorderGold;
                    }
                    else
                    {
                        // 默认：柔和渐变 + 中性边框
                        topLeft = WithAlpha(InkWashTheme.BaseTertiary, 0.6f);
                        bottomRight = WithAlpha(InkWashTheme.BaseSecondary, 0.6f);
                        borderColor = InkWashTheme.BorderNeutralL2;
                    }
                    DrawDiagonalGradient(bounds, topLeft, bottomRight, 6);
                    Render2D.DrawRectangle(bounds, borderColor, 1f);
                }
                base.Draw();
            }
        }

        // ===================================================================
        // 嵌套类：进阶属性行（2px 左边框强调 + hover）
        // ===================================================================

        /// <summary>
        /// 进阶属性行控件。
        /// 左侧绘制 2px 强调边框，hover 时边框变金色、背景提亮，
        /// 对应 CSS 左边框强调与 hover 反馈。
        /// </summary>
        private class AdvancedAttrRow : ContainerControl
        {
            /// <summary>是否被鼠标悬停</summary>
            private bool _isHovered;

            public AdvancedAttrRow()
            {
                BackgroundColor = Color.Transparent;
                ClipChildren = false;
            }

            /// <inheritdoc />
            public override void OnMouseEnter(Float2 location)
            {
                base.OnMouseEnter(location);
                _isHovered = true;
            }

            /// <inheritdoc />
            public override void OnMouseLeave()
            {
                base.OnMouseLeave();
                _isHovered = false;
            }

            /// <inheritdoc />
            public override void Draw()
            {
                if (Visible && Width > 0f && Height > 0f)
                {
                    var bounds = new Rectangle(0, 0, Width, Height);
                    Color bgColor, leftBorderColor;
                    if (_isHovered)
                    {
                        // hover：提亮背景 + 金色左边框
                        bgColor = WithAlpha(InkWashTheme.BaseTertiary, 0.5f);
                        leftBorderColor = InkWashTheme.GoldPrimary;
                    }
                    else
                    {
                        // 默认：柔和背景 + 中性左边框
                        bgColor = WithAlpha(InkWashTheme.BaseSecondary, 0.4f);
                        leftBorderColor = InkWashTheme.BorderNeutralL2;
                    }
                    Render2D.FillRectangle(bounds, bgColor);
                    // 2px 左边框强调
                    Render2D.FillRectangle(new Rectangle(0, 0, 2f, Height), leftBorderColor);
                }
                base.Draw();
            }
        }

        // ===================================================================
        // 嵌套类：武学卡片（对角线渐变 + hover 右移 + 图标品质分色）
        // ===================================================================

        /// <summary>
        /// 武学摘要卡片控件。
        /// 绘制 135° 对角线渐变背景，hover 时整体右移 2px 并提亮渐变，
        /// 对应 CSS transform 与 hover 反馈。
        /// </summary>
        private class MartialArtCard : ContainerControl
        {
            /// <summary>是否被鼠标悬停</summary>
            private bool _isHovered;

            /// <summary>记录 hover 前的原始 X 坐标，用于离开时复位</summary>
            private float _originalX;

            public MartialArtCard()
            {
                BackgroundColor = Color.Transparent;
                ClipChildren = false;
            }

            /// <inheritdoc />
            public override void OnMouseEnter(Float2 location)
            {
                base.OnMouseEnter(location);
                if (!_isHovered)
                {
                    _isHovered = true;
                    _originalX = Location.X;
                    Location = new Float2(_originalX + 2f, Location.Y);
                }
            }

            /// <inheritdoc />
            public override void OnMouseLeave()
            {
                base.OnMouseLeave();
                if (_isHovered)
                {
                    _isHovered = false;
                    Location = new Float2(_originalX, Location.Y);
                }
            }

            /// <inheritdoc />
            public override void Draw()
            {
                if (Visible && Width > 0f && Height > 0f)
                {
                    var bounds = new Rectangle(0, 0, Width, Height);
                    Color topLeft, bottomRight;
                    if (_isHovered)
                    {
                        // hover：更亮渐变
                        topLeft = WithAlpha(InkWashTheme.BaseElevated, 0.7f);
                        bottomRight = WithAlpha(InkWashTheme.BaseTertiary, 0.7f);
                    }
                    else
                    {
                        // 默认：柔和渐变
                        topLeft = WithAlpha(InkWashTheme.BaseTertiary, 0.6f);
                        bottomRight = WithAlpha(InkWashTheme.BaseSecondary, 0.6f);
                    }
                    DrawDiagonalGradient(bounds, topLeft, bottomRight, 6);

                    // 1px 外边框：hover 时金色，默认中性色
                    Color borderColor = _isHovered
                        ? InkWashTheme.BorderGold
                        : InkWashTheme.BorderNeutralL2;
                    Render2D.DrawRectangle(bounds, borderColor, 1f);
                }
                base.Draw();
            }
        }

        // ===================================================================
        // 嵌套类：等级数值辉光 Label
        // ===================================================================

        /// <summary>
        /// 等级数值专用 Label：在主文字绘制前，先绘制 8 方向 4px 偏移的
        /// 半透明金色文字（rgba(200,168,88,0.3)），形成柔和辉光。
        /// </summary>
        private class GlowLevelLabel : Label
        {
            /// <summary>辉光颜色（半透明金色）</summary>
            private static readonly Color GlowColor = new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.3f);

            /// <summary>8 方向 4px 偏移量</summary>
            private static readonly Float2[] GlowOffsets =
            {
                new Float2(4, 0), new Float2(-4, 0),
                new Float2(0, 4), new Float2(0, -4),
                new Float2(4, 4), new Float2(-4, 4),
                new Float2(4, -4), new Float2(-4, -4),
            };

            /// <inheritdoc />
            public override void Draw()
            {
                // 先绘制辉光层：8 方向 4px 偏移半透明金色文字
                if (Visible && Width > 0f && Height > 0f)
                {
                    var font = Font.GetFont();
                    if (font != null && !string.IsNullOrEmpty(Text))
                    {
                        foreach (var off in GlowOffsets)
                        {
                            var glowRect = new Rectangle(off.X, off.Y, Width, Height);
                            Render2D.DrawText(font, Text, glowRect, GlowColor,
                                HorizontalAlignment, VerticalAlignment, TextWrapping.NoWrap);
                        }
                    }
                }

                // 再绘制正式文字（含背景）
                base.Draw();
            }
        }

        // ===================================================================
        // 嵌套类：渐变装饰线
        // ===================================================================

        /// <summary>
        /// 渐变装饰线：按水平/垂直方向以 8 段线性插值绘制，
        /// 颜色序列为 StartColor(透明) → MidColor(主色) → EndColor(透明)，
        /// 用于称号左右装饰线，营造两端淡出效果。
        /// </summary>
        private class GradientLine : ContainerControl
        {
            /// <summary>渐变方向</summary>
            public enum GradientDirectionKind
            {
                /// <summary>水平：沿 X 方向渐变</summary>
                Horizontal,
                /// <summary>垂直：沿 Y 方向渐变</summary>
                Vertical,
            }

            /// <summary>渐变方向（默认水平）</summary>
            public GradientDirectionKind Direction = GradientDirectionKind.Horizontal;

            /// <summary>起始色（通常透明）</summary>
            public Color StartColor = Color.Transparent;

            /// <summary>中段主色（不透明，默认鎏金主色）</summary>
            public Color MidColor = InkWashTheme.GoldPrimary;

            /// <summary>结束色（通常透明）</summary>
            public Color EndColor = Color.Transparent;

            /// <summary>分段数</summary>
            private const int Segments = 8;

            public GradientLine()
            {
                BackgroundColor = Color.Transparent;
            }

            /// <inheritdoc />
            public override void Draw()
            {
                if (Visible && Width > 0f && Height > 0f)
                {
                    Color[] colors = { StartColor, MidColor, EndColor };
                    if (Direction == GradientDirectionKind.Horizontal)
                    {
                        float stripW = Width / Segments;
                        for (int i = 0; i < Segments; i++)
                        {
                            float t = (i + 0.5f) / Segments;
                            Color c = LerpMulti(colors, t);
                            // +1f 像素覆盖条带间隙
                            var rect = new Rectangle(i * stripW, 0, stripW + 1f, Height);
                            Render2D.FillRectangle(rect, c);
                        }
                    }
                    else
                    {
                        float stripH = Height / Segments;
                        for (int i = 0; i < Segments; i++)
                        {
                            float t = (i + 0.5f) / Segments;
                            Color c = LerpMulti(colors, t);
                            var rect = new Rectangle(0, i * stripH, Width, stripH + 1f);
                            Render2D.FillRectangle(rect, c);
                        }
                    }
                }
                base.Draw();
            }

            private static Color LerpMulti(Color[] colors, float t)
            {
                if (colors.Length == 1)
                    return colors[0];
                if (t <= 0f)
                    return colors[0];
                if (t >= 1f)
                    return colors[colors.Length - 1];
                float seg = 1f / (colors.Length - 1);
                int idx = (int)(t / seg);
                if (idx >= colors.Length - 1)
                    idx = colors.Length - 2;
                float localT = (t - idx * seg) / seg;
                return Color.Lerp(colors[idx], colors[idx + 1], localT);
            }
        }

        // ===================================================================
        // 嵌套类：带 text-shadow 的角色名 Label
        // ===================================================================

        /// <summary>
        /// 角色名专用 Label：在主文字绘制前，先绘制 8 方向 2px 偏移的
        /// 半透明黑色文字（rgba(0,0,0,0.6)），形成 text-shadow 效果。
        /// </summary>
        private class ShadowedNameLabel : Label
        {
            /// <summary>阴影颜色（半透明黑色）</summary>
            private static readonly Color ShadowColor = new Color(0f, 0f, 0f, 0.6f);

            /// <summary>8 方向 2px 偏移量</summary>
            private static readonly Float2[] ShadowOffsets =
            {
                new Float2(2, 0), new Float2(-2, 0),
                new Float2(0, 2), new Float2(0, -2),
                new Float2(2, 2), new Float2(-2, 2),
                new Float2(2, -2), new Float2(-2, -2),
            };

            /// <inheritdoc />
            public override void Draw()
            {
                // 先绘制阴影层：8 方向 2px 偏移半透明黑色文字
                if (Visible && Width > 0f && Height > 0f)
                {
                    var font = Font.GetFont();
                    if (font != null && !string.IsNullOrEmpty(Text))
                    {
                        foreach (var off in ShadowOffsets)
                        {
                            var shadowRect = new Rectangle(off.X, off.Y, Width, Height);
                            Render2D.DrawText(font, Text, shadowRect, ShadowColor,
                                HorizontalAlignment, VerticalAlignment, TextWrapping.NoWrap);
                        }
                    }
                }

                // 再绘制正式文字（含背景）
                base.Draw();
            }
        }

        // ===================================================================
        // 嵌套类：渐变背景 + 单边框面板
        // ===================================================================

        /// <summary>
        /// 渐变条带面板：按水平/垂直方向以多段线性插值绘制渐变背景，
        /// 并支持绘制单边 1px 边框线，用于顶栏/底栏/左右面板替换纯色背景。
        /// </summary>
        private class GradientBarPanel : ContainerControl
        {
            /// <summary>渐变方向：Horizontal=90°（沿 X），Vertical=180°（沿 Y）</summary>
            public enum GradientDirectionKind
            {
                /// <summary>水平 90°：沿 X 方向分段渐变</summary>
                Horizontal,
                /// <summary>垂直 180°：沿 Y 方向分段渐变</summary>
                Vertical,
            }

            /// <summary>单边边框位置</summary>
            public enum BorderSideKind
            {
                None,
                Top,
                Bottom,
                Left,
                Right,
            }

            /// <summary>渐变方向（默认垂直）</summary>
            public GradientDirectionKind GradientDirection = GradientDirectionKind.Vertical;

            /// <summary>多段渐变色（至少 1 种；多种时按位置线性插值）</summary>
            public Color[] GradientColors = { Color.Transparent };

            /// <summary>单边边框位置（默认无）</summary>
            public BorderSideKind BorderSide = BorderSideKind.None;

            /// <summary>单边边框颜色</summary>
            public Color BorderColor = Color.Transparent;

            /// <summary>边框厚度（像素）</summary>
            public float BorderThickness = 1f;

            /// <summary>渐变分段数（6~8 段平衡视觉与性能）</summary>
            private const int Segments = 8;

            public GradientBarPanel()
            {
                BackgroundColor = Color.Transparent;
            }

            /// <inheritdoc />
            public override void Draw()
            {
                if (Visible && Width > 0f && Height > 0f)
                {
                    var bounds = new Rectangle(0, 0, Width, Height);
                    DrawGradient(bounds);
                    DrawBorder();
                }
                base.Draw();
            }

            private void DrawGradient(Rectangle bounds)
            {
                Color[] colors = GradientColors;
                if (colors == null || colors.Length == 0)
                    return;
                if (colors.Length == 1)
                {
                    Render2D.FillRectangle(bounds, colors[0]);
                    return;
                }

                if (GradientDirection == GradientDirectionKind.Horizontal)
                {
                    float stripW = bounds.Width / Segments;
                    for (int i = 0; i < Segments; i++)
                    {
                        float t = (i + 0.5f) / Segments;
                        Color c = LerpMulti(colors, t);
                        // +1f 像素覆盖条带间隙，避免渐变中出现细缝
                        var rect = new Rectangle(bounds.X + i * stripW, bounds.Y, stripW + 1f, bounds.Height);
                        Render2D.FillRectangle(rect, c);
                    }
                }
                else
                {
                    float stripH = bounds.Height / Segments;
                    for (int i = 0; i < Segments; i++)
                    {
                        float t = (i + 0.5f) / Segments;
                        Color c = LerpMulti(colors, t);
                        var rect = new Rectangle(bounds.X, bounds.Y + i * stripH, bounds.Width, stripH + 1f);
                        Render2D.FillRectangle(rect, c);
                    }
                }
            }

            private static Color LerpMulti(Color[] colors, float t)
            {
                if (colors.Length == 1)
                    return colors[0];
                if (t <= 0f)
                    return colors[0];
                if (t >= 1f)
                    return colors[colors.Length - 1];
                float seg = 1f / (colors.Length - 1);
                int idx = (int)(t / seg);
                if (idx >= colors.Length - 1)
                    idx = colors.Length - 2;
                float localT = (t - idx * seg) / seg;
                return Color.Lerp(colors[idx], colors[idx + 1], localT);
            }

            private void DrawBorder()
            {
                if (BorderSide == BorderSideKind.None || BorderColor.A <= 0f)
                    return;
                float th = BorderThickness;
                switch (BorderSide)
                {
                    case BorderSideKind.Top:
                        Render2D.FillRectangle(new Rectangle(0, 0, Width, th), BorderColor);
                        break;
                    case BorderSideKind.Bottom:
                        Render2D.FillRectangle(new Rectangle(0, Height - th, Width, th), BorderColor);
                        break;
                    case BorderSideKind.Left:
                        Render2D.FillRectangle(new Rectangle(0, 0, th, Height), BorderColor);
                        break;
                    case BorderSideKind.Right:
                        Render2D.FillRectangle(new Rectangle(Width - th, 0, th, Height), BorderColor);
                        break;
                }
            }
        }
    }
}

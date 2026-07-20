using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.Ink.Components;
using HundunWorld.Game.UI.StyleSystem;
using System;
using Game.Character.Attributes;

namespace HundunWorld.Game.UI.Ink.Pages.Combat
{
    /// <summary>
    /// 战斗 HUD V2 页面。
    /// 作为战斗场景的增强型 UI 枢纽，承载 8 个子区域：
    /// <list type="bullet">
    ///   <item>小地图（左上角，<see cref="InkMinimap"/>，玩家中心 + 友方/敌方/NPC mock 点位）</item>
    ///   <item>目标信息（顶部中央，BOSS 头像 + HP 条 + 弱点/狂暴提示）</item>
    ///   <item>队伍状态（右上角，3 名 mock 成员，HP/MP 条 + 名称 + 等级）</item>
    ///   <item>增益/减益列表（右上角，队伍下方，状态效果 + 倒计时）</item>
    ///   <item>连击计数器（左侧中央，连击数 + 倍率 + 计时器）</item>
    ///   <item>玩家状态（左下角，头像 + HP/MP/XP 条 + 自身增益图标）</item>
    ///   <item>技能栏（底部中央，8 槽 + 奇术槽，快捷键 1-8/QERF）</item>
    ///   <item>道具栏（右下角，4 格 mock，<see cref="InkCell"/>）</item>
    /// </list>
    /// 通过 <see cref="NavigationRequested"/> 事件向 <see cref="InkPageRouter"/> 暴露导航请求。
    /// </summary>
    public class CombatHudV2Page : ContainerControl, IInkPage
    {
        // ===================================================================
        // 布局常量
        // =======================================================================

        /// <summary>屏幕顶部/侧面统一边距</summary>
        private const float Margin = 20f;

        /// <summary>小地图尺寸（正方形）</summary>
        private const float MinimapSize = 120f;

        /// <summary>目标信息面板宽度</summary>
        private const float TargetPanelWidth = 360f;

        /// <summary>目标信息面板高度</summary>
        private const float TargetPanelHeight = 80f;

        /// <summary>队伍面板宽度</summary>
        private const float PartyPanelWidth = 220f;

        /// <summary>队伍成员状态卡高度</summary>
        private const float PartyCardHeight = 50f;

        /// <summary>队伍成员状态卡间距</summary>
        private const float PartyCardGap = 4f;

        /// <summary>队伍职业图标占位尺寸（正方形）</summary>
        private const float PartyIconSize = 32f;

        /// <summary>队伍成员 HP 条宽度</summary>
        private const float PartyHpBarWidth = 120f;

        /// <summary>队伍成员 HP 条高度</summary>
        private const float PartyHpBarHeight = 6f;

        /// <summary>队伍成员 MP 条高度</summary>
        private const float PartyMpBarHeight = 3f;

        /// <summary>增益/减益面板宽度</summary>
        private const float BuffsPanelWidth = 220f;

        /// <summary>增益/减益条目高度</summary>
        private const float BuffItemHeight = 36f;

        /// <summary>连击计数器面板尺寸</summary>
        private const float ComboPanelSize = 140f;

        /// <summary>玩家状态面板宽度</summary>
        private const float PlayerStatsWidth = 320f;

        /// <summary>玩家状态面板高度</summary>
        private const float PlayerStatsHeight = 140f;

        /// <summary>玩家头像按钮尺寸（正方形）</summary>
        private const float AvatarSize = 48f;

        /// <summary>玩家 HP/MP 条宽度</summary>
        private const float PlayerBarWidth = 280f;

        /// <summary>玩家 HP 条高度</summary>
        private const float PlayerHpBarHeight = 12f;

        /// <summary>玩家 MP 条高度</summary>
        private const float PlayerMpBarHeight = 8f;

        /// <summary>玩家 XP 条高度</summary>
        private const float PlayerXpBarHeight = 2f;

        /// <summary>道具栏格子尺寸（正方形）</summary>
        private const float ItemCellSize = 56f;

        /// <summary>道具栏格子间距</summary>
        private const float ItemCellGap = 8f;

        /// <summary>技能栏距屏幕底部的偏移</summary>
        private const float SkillBarBottomOffset = 40f;

        // ===================================================================
        // 子控件引用
        // =======================================================================

        // 小地图（左上角）
        /// <summary>小地图组件</summary>
        private InkMinimap _minimap;

        /// <summary>小地图坐标标签</summary>
        private Label _minimapCoordLabel;

        // 目标信息（顶部中央）
        /// <summary>目标信息面板容器</summary>
        private InkPanel _targetPanel;

        /// <summary>目标头像</summary>
        private Label _targetAvatar;

        /// <summary>目标名称标签</summary>
        private Label _targetNameLabel;

        /// <summary>目标等级标签</summary>
        private Label _targetLevelLabel;

        /// <summary>目标距离标签</summary>
        private Label _targetDistanceLabel;

        /// <summary>目标 HP 条</summary>
        private InkBar _targetHpBar;

        /// <summary>目标 HP 数值标签</summary>
        private Label _targetHpLabel;

        // 队伍状态（右上角）
        /// <summary>队伍面板容器</summary>
        private ContainerControl _partyContainer;

        /// <summary>队伍标题标签</summary>
        private Label _partyTitleLabel;

        /// <summary>3 张队伍成员状态卡</summary>
        private InkPanel[] _partyCards;

        /// <summary>3 名队伍成员的职业图标</summary>
        private Label[] _partyAvatarLabels;

        /// <summary>3 名队伍成员的名称标签</summary>
        private Label[] _partyNameLabels;

        /// <summary>3 名队伍成员的等级标签</summary>
        private Label[] _partyLevelLabels;

        /// <summary>3 名队伍成员的 HP 条</summary>
        private InkBar[] _partyHpBars;

        /// <summary>3 名队伍成员的 MP 条</summary>
        private InkBar[] _partyMpBars;

        // 增益/减益列表（队伍下方）
        /// <summary>增益/减益面板容器</summary>
        private ContainerControl _buffsContainer;

        /// <summary>增益/减益标题标签</summary>
        private Label _buffsTitleLabel;

        /// <summary>增益/减益条目容器</summary>
        private InkPanel[] _buffItems;

        /// <summary>增益/减益图标</summary>
        private Label[] _buffGlyphLabels;

        /// <summary>增益/减益名称标签</summary>
        private Label[] _buffNameLabels;

        /// <summary>增益/减益时间标签</summary>
        private Label[] _buffTimeLabels;

        // 连击计数器（左侧中央）
        /// <summary>连击计数器面板</summary>
        private InkPanel _comboPanel;

        /// <summary>连击数标签</summary>
        private Label _comboNumberLabel;

        /// <summary>连击倍率标签</summary>
        private Label _comboHintLabel;

        /// <summary>连击计时器</summary>
        private InkBar _comboTimerBar;

        // 玩家状态（左下角）
        /// <summary>玩家状态面板容器</summary>
        private ContainerControl _playerStats;

        /// <summary>玩家头像</summary>
        private InkButton _playerAvatar;

        /// <summary>玩家名称标签</summary>
        private Label _playerNameLabel;

        /// <summary>玩家等级标签</summary>
        private Label _playerLevelLabel;

        /// <summary>玩家职业标签</summary>
        private Label _playerClassLabel;

        /// <summary>玩家气血条（HP）</summary>
        private InkBar _hpBar;

        /// <summary>玩家气血数值标签</summary>
        private Label _hpLabel;

        /// <summary>玩家内力条（MP）</summary>
        private InkBar _mpBar;

        /// <summary>玩家内力数值标签</summary>
        private Label _mpLabel;

        /// <summary>玩家经验条（XP）</summary>
        private InkBar _xpBar;

        /// <summary>玩家增益图标容器</summary>
        private ContainerControl _playerBuffsContainer;

        /// <summary>玩家增益图标</summary>
        private Label[] _playerBuffIcons;

        /// <summary>玩家增益时间标签</summary>
        private Label[] _playerBuffTimeLabels;

        // 技能栏（底部中央）
        /// <summary>技能槽网格（8 常规 + 1 奇术）</summary>
        private InkSkillSlotGrid _skillGrid;

        /// <summary>技能栏提示标签</summary>
        private Label _skillHintLabel;

        // 道具栏（右下角）
        /// <summary>道具栏容器</summary>
        private ContainerControl _itemBar;

        /// <summary>道具栏标题标签</summary>
        private Label _itemBarTitleLabel;

        /// <summary>4 个道具格</summary>
        private InkCell[] _itemCells;

        // ===================================================================
        // mock 数据
        // =======================================================================

        /// <summary>目标名称（mock）</summary>
        private string _targetName = "墨麒麟";

        /// <summary>目标等级（mock）</summary>
        private int _targetLevel = 50;

        /// <summary>目标当前 HP（mock）</summary>
        private int _targetHpCurrent = 18500;

        /// <summary>目标最大 HP（mock）</summary>
        private int _targetHpMax = 25000;

        /// <summary>目标距离（mock）</summary>
        private int _targetDistance = 18;

        /// <summary>3 名队伍成员的名称（mock）</summary>
        private string[] _partyNames = { "青云剑客", "紫霞仙子", "铁拳和尚" };

        /// <summary>3 名队伍成员的头像文字（mock）</summary>
        private string[] _partyAvatars = { "青", "紫", "铁" };

        /// <summary>3 名队伍成员的等级（mock）</summary>
        private int[] _partyLevels = { 40, 38, 41 };

        /// <summary>3 名队伍成员 HP 比例（mock，0-1）</summary>
        private float[] _partyHpRatio = { 0.80f, 0.65f, 0.25f };

        /// <summary>3 名队伍成员 MP 比例（mock，0-1）</summary>
        private float[] _partyMpRatio = { 0.92f, 0.70f, 0.48f };

        /// <summary>增益/减益效果名称（mock）</summary>
        private string[] _buffNames = { "攻击力 +10%", "防御力 +15%", "中毒 · 每秒 -120" };

        /// <summary>增益/减益图标（mock）</summary>
        private string[] _buffGlyphs = { "攻", "防", "毒" };

        /// <summary>增益/减益剩余时间（mock）</summary>
        private string[] _buffTimes = { "5:30", "8:45", "2:15" };

        /// <summary>增益/减益类型（mock，true=增益，false=减益）</summary>
        private bool[] _buffIsPositive = { true, true, false };

        /// <summary>连击数（mock）</summary>
        private int _comboCount = 23;

        /// <summary>连击倍率（mock）</summary>
        private float _comboMultiplier = 1.8f;

        /// <summary>连击计时器比例（mock，0-1）</summary>
        private float _comboTimerRatio = 0.68f;

        /// <summary>9 个技能槽（0-7 常规 + 8 奇术）冷却进度（mock，0=就绪，1=冷却中）</summary>
        private float[] _skillCooldowns = { 0f, 0.3f, 0f, 0.7f, 0f, 0.5f, 0f, 0.2f, 0f };

        /// <summary>4 个道具格的图标（mock）</summary>
        private string[] _itemGlyphs = { "血", "气", "解", "烟" };

        /// <summary>4 个道具格的数量徽章（mock）</summary>
        private string[] _itemBadges = { "×5", "×3", "×2", "×1" };

        /// <summary>4 个道具格的品质（mock）</summary>
        private InkWashTheme.InkQuality[] _itemQualities =
        {
            InkWashTheme.InkQuality.Legendary,
            InkWashTheme.InkQuality.Epic,
            InkWashTheme.InkQuality.Rare,
            InkWashTheme.InkQuality.Common
        };

        /// <summary>玩家角色名（mock）</summary>
        private string _playerName = "江湖过客";

        /// <summary>玩家职业（mock）</summary>
        private string _playerClass = "剑客";

        /// <summary>玩家等级（mock）</summary>
        private int _mockLevel = 42;

        /// <summary>玩家气血比例（mock，0-1）</summary>
        private float _mockHpRatio = 1.0f;

        /// <summary>玩家内力比例（mock，0-1）</summary>
        private float _mockMpRatio = 0.925f;

        /// <summary>玩家经验比例（mock，0-1）</summary>
        private float _mockXpRatio = 0.54f;

        /// <summary>玩家当前气血值（mock）</summary>
        private int _mockHpCurrent = 12450;

        /// <summary>玩家最大气血值（mock）</summary>
        private int _mockHpMax = 12450;

        /// <summary>玩家当前内力值（mock）</summary>
        private int _mockMpCurrent = 1850;

        /// <summary>玩家最大内力值（mock）</summary>
        private int _mockMpMax = 2000;

        /// <summary>玩家增益图标（mock）</summary>
        private string[] _playerBuffGlyphs = { "攻", "防", "轻" };

        /// <summary>玩家增益剩余时间（mock）</summary>
        private string[] _playerBuffTimes = { "5:30", "8:45", "0:18" };

        // ===================================================================
        // 屏幕尺寸缓存与数据绑定
        // =======================================================================

        /// <summary>当前屏幕尺寸缓存，用于布局计算</summary>
        private Float2 _screenSize;

        /// <summary>绑定的角色属性组件，null 时回退到 mock 数据</summary>
        private CharacterAttributesComponent _boundCharacter;

        // ===================================================================
        // 公共 API：事件
        // =======================================================================

        /// <summary>
        /// 导航请求事件。
        /// 由头像按钮等触发，参数为目标页面的 dom-id（如 <c>"nav-character-v2"</c>）。
        /// 由 <see cref="InkPageRouter"/> 订阅以执行页面跳转。
        /// </summary>
        public event Action<string> NavigationRequested;

        // ===================================================================
        // 构造函数
        // =======================================================================

        /// <summary>
        /// 构造函数：初始化全部 8 个子区域，使用 mock 数据填充。
        /// 构造时读取 <see cref="FlaxEngine.Screen.Size"/> 计算布局，
        /// 屏幕尺寸未就绪时使用 1920x1080 兜底。
        /// </summary>
        public CombatHudV2Page()
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
                BuildMinimap();
                BuildTargetInfo();
                BuildPartyCards();
                BuildBuffsList();
                BuildComboCounter();
                BuildPlayerStats();
                BuildSkillGrid();
                BuildItemBar();

                ApplyLayout();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[CombatHudV2Page] 初始化失败: {ex.Message}");
            }
        }

        // ===================================================================
        // SubTask 构造方法
        // =======================================================================

        /// <summary>
        /// 小地图（左上角）。
        /// 创建 <see cref="InkMinimap"/> 120x120，mock 实体点位：
        /// 玩家居中 + 3 友方 + 2 敌方。
        /// 实体坐标 relativeX/relativeZ 范围 -1~1。
        /// </summary>
        private void BuildMinimap()
        {
            _minimap = new InkMinimap
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(MinimapSize, MinimapSize),
            };

            _minimap.AddEntity(InkMinimapEntityType.Player, 0f, 0f);
            _minimap.AddEntity(InkMinimapEntityType.Friendly, -0.4f, -0.2f);
            _minimap.AddEntity(InkMinimapEntityType.Friendly, 0.3f, -0.3f);
            _minimap.AddEntity(InkMinimapEntityType.Friendly, -0.3f, 0.3f);
            _minimap.AddEntity(InkMinimapEntityType.Enemy, 0.55f, -0.1f);
            _minimap.AddEntity(InkMinimapEntityType.Enemy, 0.6f, 0.2f);

            AddChild(_minimap);

            _minimapCoordLabel = new Label
            {
                Text = "昆仑墟 · 深渊",
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 11f),
                TextColor = InkWashTheme.PaperAged,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(MinimapSize, 20f),
            };
            AddChild(_minimapCoordLabel);
        }

        /// <summary>
        /// 目标信息（顶部中央）。
        /// 创建 BOSS 头像、名称、等级、距离、HP 条及弱点/狂暴提示。
        /// </summary>
        private void BuildTargetInfo()
        {
            _targetPanel = new InkPanel
            {
                Variant = InkPanelVariant.Default,
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(TargetPanelWidth, TargetPanelHeight),
                BackgroundColor = new Color(0.11f, 0.12f, 0.16f, 0.92f),
            };

            _targetAvatar = new Label
            {
                Text = "麟",
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 18f),
                TextColor = InkWashTheme.PaperBright,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 8f),
                Size = new Float2(36f, 36f),
                BackgroundColor = InkWashTheme.VermilionPrimary,
            };
            _targetPanel.AddChild(_targetAvatar);

            _targetNameLabel = new Label
            {
                Text = _targetName,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 16f),
                TextColor = InkWashTheme.VermilionBright,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(56f, 8f),
                Size = new Float2(180f, 20f),
            };
            _targetPanel.AddChild(_targetNameLabel);

            _targetLevelLabel = new Label
            {
                Text = $"Lv.{_targetLevel} · 首领",
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 12f),
                TextColor = InkWashTheme.PaperFaded,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(56f, 32f),
                Size = new Float2(120f, 16f),
            };
            _targetPanel.AddChild(_targetLevelLabel);

            _targetDistanceLabel = new Label
            {
                Text = $"{_targetDistance}m",
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 11f),
                TextColor = InkWashTheme.PaperAged,
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(300f, 8f),
                Size = new Float2(50f, 20f),
            };
            _targetPanel.AddChild(_targetDistanceLabel);

            float hpRatio = (float)_targetHpCurrent / _targetHpMax;
            _targetHpBar = new InkBar
            {
                FillVariant = InkBarFillVariant.Vermilion,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 52f),
                Size = new Float2(TargetPanelWidth - 120f, 10f),
                Value = hpRatio,
            };
            _targetPanel.AddChild(_targetHpBar);

            _targetHpLabel = new Label
            {
                Text = $"{_targetHpCurrent:N0} / {_targetHpMax:N0}",
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 10f),
                TextColor = InkWashTheme.PaperBright,
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(TargetPanelWidth - 100f, 52f),
                Size = new Float2(88f, 10f),
            };
            _targetPanel.AddChild(_targetHpLabel);

            AddChild(_targetPanel);
        }

        /// <summary>
        /// 队伍状态（右上角）。
        /// 创建 3 名队伍成员状态卡，每张卡片包含头像、名称、等级、HP 条、MP 条。
        /// </summary>
        private void BuildPartyCards()
        {
            float titleHeight = 24f;
            _partyContainer = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = Color.Transparent,
                ClipChildren = false,
                Size = new Float2(PartyPanelWidth, titleHeight + 3 * PartyCardHeight + 2 * PartyCardGap),
            };

            _partyTitleLabel = new Label
            {
                Text = "队伍 (4/4)",
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 13f),
                TextColor = InkWashTheme.GoldBright,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 0f),
                Size = new Float2(PartyPanelWidth, titleHeight),
            };
            _partyContainer.AddChild(_partyTitleLabel);

            _partyCards = new InkPanel[3];
            _partyAvatarLabels = new Label[3];
            _partyNameLabels = new Label[3];
            _partyLevelLabels = new Label[3];
            _partyHpBars = new InkBar[3];
            _partyMpBars = new InkBar[3];

            for (int i = 0; i < 3; i++)
            {
                var card = new InkPanel
                {
                    Variant = InkPanelVariant.Lightweight,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(0f, titleHeight + i * (PartyCardHeight + PartyCardGap)),
                    Size = new Float2(PartyPanelWidth, PartyCardHeight),
                };

                bool isLowHp = _partyHpRatio[i] < 0.3f;

                var avatarLabel = new Label
                {
                    Text = _partyAvatars[i],
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 13f),
                    TextColor = InkWashTheme.PaperBright,
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(8f, 9f),
                    Size = new Float2(PartyIconSize, PartyIconSize),
                    BackgroundColor = isLowHp ? InkWashTheme.VermilionPrimary : InkWashTheme.JadePrimary,
                };
                card.AddChild(avatarLabel);
                _partyAvatarLabels[i] = avatarLabel;

                var nameLabel = new Label
                {
                    Text = _partyNames[i],
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 12f),
                    TextColor = InkWashTheme.PaperBright,
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(48f, 6f),
                    Size = new Float2(120f, 14f),
                };
                card.AddChild(nameLabel);
                _partyNameLabels[i] = nameLabel;

                var levelLabel = new Label
                {
                    Text = $"Lv.{_partyLevels[i]}",
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 10f),
                    TextColor = InkWashTheme.PaperFaded,
                    HorizontalAlignment = TextAlignment.Far,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(170f, 6f),
                    Size = new Float2(42f, 14f),
                };
                card.AddChild(levelLabel);
                _partyLevelLabels[i] = levelLabel;

                var hpBar = new InkBar
                {
                    FillVariant = isLowHp ? InkBarFillVariant.Vermilion : InkBarFillVariant.Jade,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(48f, 24f),
                    Size = new Float2(PartyHpBarWidth, PartyHpBarHeight),
                    Value = _partyHpRatio[i],
                };
                card.AddChild(hpBar);
                _partyHpBars[i] = hpBar;

                var mpLabel = new Label
                {
                    Text = "内力",
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 9f),
                    TextColor = InkWashTheme.PaperDark,
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(48f, 34f),
                    Size = new Float2(30f, 12f),
                };
                card.AddChild(mpLabel);

                var mpBar = new InkBar
                {
                    FillVariant = InkBarFillVariant.Gold,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(82f, 34f),
                    Size = new Float2(PartyHpBarWidth - 34f, PartyMpBarHeight),
                    Value = _partyMpRatio[i],
                };
                card.AddChild(mpBar);
                _partyMpBars[i] = mpBar;

                _partyCards[i] = card;
                _partyContainer.AddChild(card);
            }

            AddChild(_partyContainer);
        }

        /// <summary>
        /// 增益/减益列表（右上角，队伍下方）。
        /// 创建状态效果列表，包含图标、名称、倒计时。
        /// </summary>
        private void BuildBuffsList()
        {
            float titleHeight = 24f;
            _buffsContainer = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = Color.Transparent,
                ClipChildren = false,
                Size = new Float2(BuffsPanelWidth, titleHeight + 3 * BuffItemHeight + 8f),
            };

            _buffsTitleLabel = new Label
            {
                Text = "状态效果",
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 13f),
                TextColor = InkWashTheme.GoldBright,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 0f),
                Size = new Float2(BuffsPanelWidth, titleHeight),
            };
            _buffsContainer.AddChild(_buffsTitleLabel);

            _buffItems = new InkPanel[3];
            _buffGlyphLabels = new Label[3];
            _buffNameLabels = new Label[3];
            _buffTimeLabels = new Label[3];

            for (int i = 0; i < 3; i++)
            {
                var item = new InkPanel
                {
                    Variant = InkPanelVariant.Lightweight,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(0f, titleHeight + i * (BuffItemHeight + 4f)),
                    Size = new Float2(BuffsPanelWidth, BuffItemHeight),
                };

                bool isPositive = _buffIsPositive[i];
                var bgColor = isPositive ? InkWashTheme.GoldPrimary : InkWashTheme.VermilionPrimary;
                var borderColor = isPositive ? InkWashTheme.GoldDeep : InkWashTheme.VermilionDeep;
                var textColor = isPositive ? InkWashTheme.GoldBright : InkWashTheme.VermilionBright;

                var glyphLabel = new Label
                {
                    Text = _buffGlyphs[i],
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 13f),
                    TextColor = textColor,
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(8f, 6f),
                    Size = new Float2(24f, 24f),
                    BackgroundColor = bgColor * 0.15f,
                };
                item.AddChild(glyphLabel);
                _buffGlyphLabels[i] = glyphLabel;

                var nameLabel = new Label
                {
                    Text = _buffNames[i],
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 11f),
                    TextColor = isPositive ? InkWashTheme.PaperBright : InkWashTheme.VermilionBright,
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(40f, 6f),
                    Size = new Float2(150f, 14f),
                };
                item.AddChild(nameLabel);
                _buffNameLabels[i] = nameLabel;

                var timeLabel = new Label
                {
                    Text = _buffTimes[i],
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 10f),
                    TextColor = isPositive ? InkWashTheme.PaperFaded : InkWashTheme.VermilionBright,
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(40f, 20f),
                    Size = new Float2(50f, 12f),
                };
                item.AddChild(timeLabel);
                _buffTimeLabels[i] = timeLabel;

                _buffItems[i] = item;
                _buffsContainer.AddChild(item);
            }

            AddChild(_buffsContainer);
        }

        /// <summary>
        /// 连击计数器（左侧中央）。
        /// 创建连击数、倍率、计时器。
        /// </summary>
        private void BuildComboCounter()
        {
            _comboPanel = new InkPanel
            {
                Variant = InkPanelVariant.Default,
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(ComboPanelSize, 180f),
                BackgroundColor = new Color(0.11f, 0.12f, 0.16f, 0.7f),
            };

            var comboLabel = new Label
            {
                Text = "连击",
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 14f),
                TextColor = InkWashTheme.GoldBright,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 8f),
                Size = new Float2(ComboPanelSize, 20f),
            };
            _comboPanel.AddChild(comboLabel);

            _comboNumberLabel = new Label
            {
                Text = $"{_comboCount}",
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 48f),
                TextColor = InkWashTheme.GoldBright,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 32f),
                Size = new Float2(ComboPanelSize, 56f),
            };
            _comboPanel.AddChild(_comboNumberLabel);

            var comboSubLabel = new Label
            {
                Text = "COMBO",
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 12f),
                TextColor = InkWashTheme.PaperFaded,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 92f),
                Size = new Float2(ComboPanelSize, 16f),
            };
            _comboPanel.AddChild(comboSubLabel);

            _comboTimerBar = new InkBar
            {
                FillVariant = InkBarFillVariant.Gold,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(40f, 112f),
                Size = new Float2(60f, 4f),
                Value = _comboTimerRatio,
            };
            _comboPanel.AddChild(_comboTimerBar);

            _comboHintLabel = new Label
            {
                Text = $"倍率 ×{_comboMultiplier:F1}",
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 10f),
                TextColor = InkWashTheme.VermilionBright,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 124f),
                Size = new Float2(ComboPanelSize, 16f),
            };
            _comboPanel.AddChild(_comboHintLabel);

            AddChild(_comboPanel);
        }

        /// <summary>
        /// 玩家状态（左下角）。
        /// 创建头像、名称、等级、职业、HP/MP/XP 条及自身增益图标。
        /// </summary>
        private void BuildPlayerStats()
        {
            _playerStats = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(PlayerStatsWidth, PlayerStatsHeight),
                BackgroundColor = Color.Transparent,
                ClipChildren = false,
            };

            _playerAvatar = new InkButton
            {
                Text = _playerName[0].ToString(),
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 28f),
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 0f),
                Size = new Float2(AvatarSize, AvatarSize),
                BackgroundColor = InkWashTheme.GoldPrimary,
            };
            _playerAvatar.Clicked += OnAvatarButtonClicked;
            _playerStats.AddChild(_playerAvatar);

            _playerNameLabel = new Label
            {
                Text = _playerName,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 14f),
                TextColor = InkWashTheme.GoldBright,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(AvatarSize + 8f, 2f),
                Size = new Float2(120f, 18f),
            };
            _playerStats.AddChild(_playerNameLabel);

            _playerLevelLabel = new Label
            {
                Text = $"Lv.{_mockLevel}",
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 12f),
                TextColor = InkWashTheme.PaperFaded,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(AvatarSize + 8f + 80f, 2f),
                Size = new Float2(50f, 18f),
            };
            _playerStats.AddChild(_playerLevelLabel);

            _playerClassLabel = new Label
            {
                Text = _playerClass,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 10f),
                TextColor = InkWashTheme.TextBrand,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(AvatarSize + 8f + 134f, 4f),
                Size = new Float2(50f, 14f),
            };
            _playerStats.AddChild(_playerClassLabel);

            _hpBar = new InkBar
            {
                FillVariant = InkBarFillVariant.Vermilion,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(AvatarSize + 8f, 24f),
                Size = new Float2(PlayerBarWidth, PlayerHpBarHeight),
                Value = _mockHpRatio,
            };
            _playerStats.AddChild(_hpBar);

            _hpLabel = new Label
            {
                Text = $"{_mockHpCurrent:N0} / {_mockHpMax:N0}",
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 10f),
                TextColor = InkWashTheme.PaperBright,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(AvatarSize + 8f, 24f),
                Size = new Float2(PlayerBarWidth, PlayerHpBarHeight),
            };
            _playerStats.AddChild(_hpLabel);

            _mpBar = new InkBar
            {
                FillVariant = InkBarFillVariant.Gold,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(AvatarSize + 8f, 38f),
                Size = new Float2(PlayerBarWidth, PlayerMpBarHeight),
                Value = _mockMpRatio,
            };
            _playerStats.AddChild(_mpBar);

            _mpLabel = new Label
            {
                Text = $"{_mockMpCurrent:N0} / {_mockMpMax:N0}",
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 10f),
                TextColor = InkWashTheme.PaperBright,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(AvatarSize + 8f, 38f),
                Size = new Float2(PlayerBarWidth, PlayerMpBarHeight),
            };
            _playerStats.AddChild(_mpLabel);

            var xpLabel = new Label
            {
                Text = "经验",
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 9f),
                TextColor = InkWashTheme.PaperDark,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(AvatarSize + 8f, 50f),
                Size = new Float2(40f, 12f),
            };
            _playerStats.AddChild(xpLabel);

            _xpBar = new InkBar
            {
                FillVariant = InkBarFillVariant.Jade,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(AvatarSize + 8f + 46f, 52f),
                Size = new Float2(PlayerBarWidth - 46f, PlayerXpBarHeight),
                Value = _mockXpRatio,
            };
            _playerStats.AddChild(_xpBar);

            _playerBuffsContainer = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(AvatarSize + 8f, 60f),
                Size = new Float2(PlayerBarWidth, 30f),
                BackgroundColor = Color.Transparent,
            };

            _playerBuffIcons = new Label[3];
            _playerBuffTimeLabels = new Label[3];

            for (int i = 0; i < 3; i++)
            {
                var buffIcon = new Label
                {
                    Text = _playerBuffGlyphs[i],
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 12f),
                    TextColor = InkWashTheme.JadeBright,
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(i * 30f, 0f),
                    Size = new Float2(24f, 24f),
                    BackgroundColor = new Color(0.06f, 0.07f, 0.11f, 0.8f),
                };
                _playerBuffsContainer.AddChild(buffIcon);
                _playerBuffIcons[i] = buffIcon;

                var timeLabel = new Label
                {
                    Text = _playerBuffTimes[i],
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 8f),
                    TextColor = InkWashTheme.PaperFaded,
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(i * 30f, 24f),
                    Size = new Float2(24f, 12f),
                };
                _playerBuffsContainer.AddChild(timeLabel);
                _playerBuffTimeLabels[i] = timeLabel;
            }

            _playerStats.AddChild(_playerBuffsContainer);

            AddChild(_playerStats);
        }

        /// <summary>
        /// SubTask 8.4：右下角技能槽网格。
        /// 创建 <see cref="InkSkillSlotGrid"/>（8 常规槽 + 1 奇术槽，尺寸自动计算），
        /// 应用 mock 冷却进度（含冷却扇形遮罩 + 快捷键 1-8/Q + 奇术槽脉冲动画由组件内部实现）。
        /// </summary>
        private void BuildSkillGrid()
        {
            _skillGrid = new InkSkillSlotGrid
            {
                AnchorPreset = AnchorPresets.TopLeft,
            };

            // 应用 mock 冷却（0-7 常规槽，8 奇术槽）
            for (int i = 0; i < _skillCooldowns.Length && i < 9; i++)
            {
                _skillGrid.SetCooldown(i, _skillCooldowns[i]);
            }

            AddChild(_skillGrid);
        }

        /// <summary>
        /// SubTask 8.5：底部道具栏（4 格 mock）。
        /// <see cref="ContainerControl"/> 内含 4 个 <see cref="InkCell"/>，56x56，间距 8px。
        /// 每个 cell 设置品质色边框（<see cref="InkCell.Quality"/>）与数量徽章（<see cref="InkCell.Badge"/>）。
        /// </summary>
        private void BuildItemBar()
        {
            float barWidth = 4f * ItemCellSize + 3f * ItemCellGap;
            _itemBar = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(barWidth, ItemCellSize),
                BackgroundColor = Color.Transparent,
                ClipChildren = false,
            };

            _itemCells = new InkCell[4];
            for (int i = 0; i < 4; i++)
            {
                var cell = new InkCell
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(i * (ItemCellSize + ItemCellGap), 0f),
                    Size = new Float2(ItemCellSize, ItemCellSize),
                    Quality = _itemQualities[i],
                    Badge = _itemBadges[i],
                };
                _itemCells[i] = cell;
                _itemBar.AddChild(cell);
            }

            AddChild(_itemBar);
        }

        // ===================================================================
        // 布局计算
        // =======================================================================

        /// <summary>
        /// 根据当前 <see cref="_screenSize"/> 重新计算所有子控件的位置。
        /// 由构造函数与 <see cref="RefreshLayout"/> 调用。
        /// </summary>
        private void ApplyLayout()
        {
            float sw = _screenSize.X;
            float sh = _screenSize.Y;

            // 小地图：左上角
            if (_minimap != null)
            {
                _minimap.Location = new Float2(Margin, Margin);
            }
            if (_minimapCoordLabel != null)
            {
                _minimapCoordLabel.Location = new Float2(Margin, Margin + MinimapSize + 4f);
            }

            // 目标信息：顶部中央
            if (_targetPanel != null)
            {
                _targetPanel.Location = new Float2(sw * 0.5f - TargetPanelWidth * 0.5f, Margin);
            }

            // 队伍状态：右上角
            if (_partyContainer != null)
            {
                _partyContainer.Location = new Float2(sw - PartyPanelWidth - Margin, Margin);
            }

            // 增益/减益列表：右上角，队伍下方
            if (_buffsContainer != null)
            {
                float partyBottom = Margin + _partyContainer.Height;
                _buffsContainer.Location = new Float2(sw - BuffsPanelWidth - Margin, partyBottom);
            }

            // 连击计数器：左侧中央
            if (_comboPanel != null)
            {
                _comboPanel.Location = new Float2(Margin, sh * 0.5f - _comboPanel.Height * 0.5f);
            }

            // 玩家状态：左下角
            if (_playerStats != null)
            {
                _playerStats.Location = new Float2(Margin, sh - PlayerStatsHeight - Margin);
            }

            // 技能栏：底部中央
            if (_skillGrid != null)
            {
                _skillGrid.Location = new Float2(sw * 0.5f - _skillGrid.Width * 0.5f, sh - SkillBarBottomOffset - _skillGrid.Height);
            }
            if (_skillHintLabel != null)
            {
                float skillGridBottom = sh - SkillBarBottomOffset;
                _skillHintLabel.Location = new Float2(sw * 0.5f - 150f, skillGridBottom + 8f);
            }

            // 道具栏：右下角
            if (_itemBar != null)
            {
                float itemBarWidth = 4f * ItemCellSize + 3f * ItemCellGap;
                float itemBarHeight = ItemCellSize + 30f;
                _itemBar.Location = new Float2(sw - itemBarWidth - Margin, sh - itemBarHeight - Margin);
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
        /// 绑定角色属性组件。
        /// 绑定后气血/内力/体魄条每帧从组件读取真实数据（气血=Health，内力=Energy，体魄=Stamina），
        /// 传入 null 解除绑定回退到 mock。
        /// </summary>
        /// <param name="component">角色属性组件，null 解除绑定</param>
        public void BindCharacter(CharacterAttributesComponent component)
        {
            _boundCharacter = component;
            // 绑定瞬间立即刷新身份信息（角色名/等级/阶段），避免等下一帧 Update
            RefreshPlayerIdentity();
        }

        // ===================================================================
        // 生命周期
        // =======================================================================

        /// <inheritdoc />
        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);
            RefreshBoundData();
        }

        /// <summary>
        /// 每帧从绑定的数据源刷新气血/内力/体魄。
        /// 未绑定时保持 mock 数据不变。
        /// </summary>
        private void RefreshBoundData()
        {
            if (_boundCharacter == null)
                return;

            // 刷新角色名/等级/阶段（每帧同步，防止运行时改名）
            RefreshPlayerIdentity();

            // 气血（HP）= CurrentHealth / MaxHealth
            float hpRatio = _boundCharacter.MaxHealth > 0f
                ? Mathf.Clamp(_boundCharacter.CurrentHealth / _boundCharacter.MaxHealth, 0f, 1f)
                : 0f;
            // 内力（MP）= CurrentEnergy / MaxEnergy
            float mpRatio = _boundCharacter.MaxEnergy > 0f
                ? Mathf.Clamp(_boundCharacter.CurrentEnergy / _boundCharacter.MaxEnergy, 0f, 1f)
                : 0f;
            // 体魄（XP）= CurrentStamina / MaxStamina
            float xpRatio = _boundCharacter.MaxStamina > 0f
                ? Mathf.Clamp(_boundCharacter.CurrentStamina / _boundCharacter.MaxStamina, 0f, 1f)
                : 0f;

            if (_hpBar != null)
                _hpBar.Value = hpRatio;
            if (_hpLabel != null)
                _hpLabel.Text = $"{(int)_boundCharacter.CurrentHealth}/{(int)_boundCharacter.MaxHealth}";

            if (_mpBar != null)
                _mpBar.Value = mpRatio;
            if (_mpLabel != null)
                _mpLabel.Text = $"{(int)_boundCharacter.CurrentEnergy}/{(int)_boundCharacter.MaxEnergy}";

            if (_xpBar != null)
                _xpBar.Value = xpRatio;
        }

        /// <summary>
        /// 刷新玩家身份信息（角色名、等级）。
        /// 由 <see cref="BindCharacter"/> 立即调用，并由 <see cref="RefreshBoundData"/> 每帧调用以保持同步。
        /// 未绑定时保留 mock 数据，不覆盖。
        /// </summary>
        private void RefreshPlayerIdentity()
        {
            if (_boundCharacter == null)
                return;

            if (_playerNameLabel != null && !string.IsNullOrEmpty(_boundCharacter.Nickname))
            {
                _playerNameLabel.Text = _boundCharacter.Nickname;
            }

            if (_playerLevelLabel != null)
            {
                _playerLevelLabel.Text = $"Lv.{_boundCharacter.Level}";
            }
        }

        // ===================================================================
        // 事件处理
        // =======================================================================

        /// <summary>
        /// 头像点击处理：触发 <see cref="NavigationRequested"/>("nav-character-v2")，
        /// 由 <see cref="InkPageRouter"/> 订阅后跳转角色属性页 V2。
        /// </summary>
        private void OnAvatarButtonClicked()
        {
            try
            {
                NavigationRequested?.Invoke("nav-character-v2");
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[CombatHudV2Page] NavigationRequested(nav-character-v2) 触发失败: {ex.Message}");
            }
        }
    }
}

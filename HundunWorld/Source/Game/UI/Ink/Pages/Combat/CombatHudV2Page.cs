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
    /// 作为战斗场景的增强型 UI 枢纽，承载 5 个子区域：
    /// <list type="bullet">
    ///   <item>SubTask 8.2 右上角小地图（<see cref="InkMinimap"/>，玩家中心 + 友方/敌方/NPC mock 点位）</item>
    ///   <item>SubTask 8.3 左侧队伍成员状态卡（3 名 mock 成员，HP/MP 条 + 名称 + 职业图标占位）</item>
    ///   <item>SubTask 8.4 右下角技能槽网格（<see cref="InkSkillSlotGrid"/>，8 槽 + 奇术槽脉冲）</item>
    ///   <item>SubTask 8.5 底部道具栏（4 格 mock，<see cref="InkCell"/>）</item>
    ///   <item>SubTask 8.6 玩家 HP/MP/XP 条绑定 <see cref="CharacterAttributesComponent"/>（气血/体魄/内力）</item>
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

        /// <summary>右上角小地图尺寸（正方形）</summary>
        private const float MinimapSize = 180f;

        /// <summary>队伍成员状态卡宽度</summary>
        private const float PartyCardWidth = 220f;

        /// <summary>队伍成员状态卡高度</summary>
        private const float PartyCardHeight = 64f;

        /// <summary>队伍成员状态卡间距</summary>
        private const float PartyCardGap = 8f;

        /// <summary>队伍容器起始 Y 坐标</summary>
        private const float PartyStartY = 90f;

        /// <summary>队伍职业图标占位尺寸（正方形）</summary>
        private const float PartyIconSize = 40f;

        /// <summary>队伍成员 HP/MP 条宽度</summary>
        private const float PartyBarWidth = 156f;

        /// <summary>队伍成员 HP/MP 条高度</summary>
        private const float PartyBarHeight = 8f;

        /// <summary>玩家状态面板宽度</summary>
        private const float PlayerStatsWidth = 300f;

        /// <summary>玩家状态面板高度</summary>
        private const float PlayerStatsHeight = 180f;

        /// <summary>玩家状态面板距屏幕底部的偏移</summary>
        private const float PlayerStatsBottomOffset = 200f;

        /// <summary>玩家头像按钮尺寸（正方形）</summary>
        private const float AvatarSize = 56f;

        /// <summary>玩家 HP/MP/XP 条宽度</summary>
        private const float PlayerBarWidth = 180f;

        /// <summary>玩家 HP/MP/XP 条高度</summary>
        private const float PlayerBarHeight = 12f;

        /// <summary>玩家 HP/MP 条高度（数值叠加显示，比 XP 条更高以容纳文字）</summary>
        private const float PlayerHpMpBarHeight = 18f;

        /// <summary>道具栏格子尺寸（正方形）</summary>
        private const float ItemCellSize = 56f;

        /// <summary>道具栏格子间距</summary>
        private const float ItemCellGap = 8f;

        /// <summary>道具栏距屏幕底部的偏移</summary>
        private const float ItemBarBottomOffset = 70f;

        /// <summary>技能槽网格距屏幕底部的偏移</summary>
        private const float SkillGridBottomOffset = 100f;

        // ===================================================================
        // 子控件引用
        // =======================================================================

        // SubTask 8.2 小地图
        /// <summary>右上角小地图组件</summary>
        private InkMinimap _minimap;

        // SubTask 8.3 队伍成员状态卡
        /// <summary>左侧队伍成员容器</summary>
        private ContainerControl _partyContainer;

        /// <summary>3 张队伍成员状态卡</summary>
        private InkPanel[] _partyCards;

        /// <summary>3 名队伍成员的 HP 条</summary>
        private InkBar[] _partyHpBars;

        /// <summary>3 名队伍成员的 MP 条</summary>
        private InkBar[] _partyMpBars;

        // SubTask 8.4 技能槽网格
        /// <summary>右下角技能槽网格（8 常规 + 1 奇术）</summary>
        private InkSkillSlotGrid _skillGrid;

        // SubTask 8.5 道具栏
        /// <summary>底部道具栏容器</summary>
        private ContainerControl _itemBar;

        /// <summary>4 个道具格</summary>
        private InkCell[] _itemCells;

        // SubTask 8.6 玩家 HP/MP/XP
        /// <summary>玩家状态面板容器</summary>
        private ContainerControl _playerStats;

        /// <summary>玩家头像按钮（点击跳转角色页）</summary>
        private InkButton _avatarButton;

        /// <summary>玩家竖排角色名</summary>
        private InkVerticalTitle _playerNameLabel;

        /// <summary>玩家气血条（HP，绑定 CurrentHealth）</summary>
        private InkBar _hpBar;

        /// <summary>玩家内力条（MP，绑定 CurrentEnergy）</summary>
        private InkBar _mpBar;

        /// <summary>玩家体魄条（XP，绑定 CurrentStamina）</summary>
        private InkBar _xpBar;

        /// <summary>玩家气血数值标签</summary>
        private Label _hpLabel;

        /// <summary>玩家内力数值标签</summary>
        private Label _mpLabel;

        /// <summary>玩家体魄数值标签</summary>
        private Label _xpLabel;

        /// <summary>玩家等级与阶段标签（头像正下方，格式 "Lv.42 · 武侠"）</summary>
        private Label _levelStageLabel;

        // ===================================================================
        // mock 数据
        // =======================================================================

        /// <summary>3 名队伍成员的名称（mock）</summary>
        private string[] _partyNames = { "燕归人", "沈莘蕾", "陆孤寒" };

        /// <summary>3 名队伍成员的职业（mock）</summary>
        private string[] _partyClasses = { "剑客", "医者", "侠盗" };

        /// <summary>3 名队伍成员 HP 比例（mock，0-1）</summary>
        private float[] _partyHpRatio = { 0.85f, 0.62f, 1.0f };

        /// <summary>3 名队伍成员 MP 比例（mock，0-1）</summary>
        private float[] _partyMpRatio = { 0.45f, 0.80f, 0.30f };

        /// <summary>9 个技能槽（0-7 常规 + 8 奇术）冷却进度（mock，0=就绪，1=冷却中）</summary>
        private float[] _skillCooldowns = { 0f, 0.3f, 0f, 0.7f, 0f, 0.5f, 0f, 0.2f, 0f };

        /// <summary>4 个道具格的数量徽章（mock）</summary>
        private string[] _itemBadges = { "99", "12", "3", string.Empty };

        /// <summary>4 个道具格的品质（mock）</summary>
        private InkWashTheme.InkQuality[] _itemQualities =
        {
            InkWashTheme.InkQuality.Rare,
            InkWashTheme.InkQuality.Uncommon,
            InkWashTheme.InkQuality.Epic,
            InkWashTheme.InkQuality.Common
        };

        /// <summary>玩家角色名（mock）— 与队伍成员区分，采用正式武侠姓氏命名</summary>
        private string _playerName = "慕容凌霄";

        /// <summary>玩家气血比例（mock，0-1）</summary>
        private float _mockHpRatio = 0.72f;

        /// <summary>玩家内力比例（mock，0-1）</summary>
        private float _mockMpRatio = 0.55f;

        /// <summary>玩家体魄比例（mock，0-1）</summary>
        private float _mockXpRatio = 0.88f;

        /// <summary>玩家当前气血值（mock）</summary>
        private int _mockHpCurrent = 7200;

        /// <summary>玩家最大气血值（mock）</summary>
        private int _mockHpMax = 10000;

        /// <summary>玩家当前内力值（mock）</summary>
        private int _mockMpCurrent = 550;

        /// <summary>玩家最大内力值（mock）</summary>
        private int _mockMpMax = 1000;

        /// <summary>玩家当前体魄值（mock）</summary>
        private int _mockXpCurrent = 88;

        /// <summary>玩家最大体魄值（mock）</summary>
        private int _mockXpMax = 100;

        /// <summary>玩家等级（mock，未绑定时显示）</summary>
        private int _mockLevel = 42;

        /// <summary>玩家成长阶段（mock，未绑定时显示）</summary>
        private CharacterStage _mockStage = CharacterStage.Wuxia;

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
        /// 构造函数：初始化全部 5 个子区域，使用 mock 数据填充。
        /// 构造时读取 <see cref="FlaxEngine.Screen.Size"/> 计算布局，
        /// 屏幕尺寸未就绪时使用 1920x1080 兜底。
        /// </summary>
        public CombatHudV2Page()
        {
            // 1. 读取屏幕尺寸
            _screenSize = FlaxEngine.Screen.Size;
            if (_screenSize.X <= 0f || _screenSize.Y <= 0f)
            {
                _screenSize = new Float2(1920f, 1080f);
            }

            // 2. 外壳本身：全屏拉伸 + 透明背景 + 不裁剪子控件
            AnchorPreset = AnchorPresets.StretchAll;
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
            AutoFocus = false;
            Location = Float2.Zero;
            Size = _screenSize;

            try
            {
                BuildMinimap();
                BuildPartyCards();
                BuildPlayerStats();
                BuildSkillGrid();
                BuildItemBar();

                // 应用初始布局（基于屏幕尺寸计算所有子控件位置）
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
        /// SubTask 8.2：右上角小地图。
        /// 创建 <see cref="InkMinimap"/> 180x180，mock 实体点位：
        /// 玩家居中 + 2 友方 + 2 敌方 + 1 NPC。
        /// 实体坐标 relativeX/relativeZ 范围 -1~1。
        /// </summary>
        private void BuildMinimap()
        {
            _minimap = new InkMinimap
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(MinimapSize, MinimapSize),
            };

            // mock 实体点位：玩家中心 + 2 友方 + 2 敌方 + 1 NPC
            _minimap.AddEntity(InkMinimapEntityType.Player, 0f, 0f);
            _minimap.AddEntity(InkMinimapEntityType.Friendly, -0.4f, -0.3f);
            _minimap.AddEntity(InkMinimapEntityType.Friendly, 0.35f, 0.45f);
            _minimap.AddEntity(InkMinimapEntityType.Enemy, 0.55f, -0.5f);
            _minimap.AddEntity(InkMinimapEntityType.Enemy, -0.6f, 0.5f);
            _minimap.AddEntity(InkMinimapEntityType.NPC, 0.2f, -0.25f);

            AddChild(_minimap);
        }

        /// <summary>
        /// SubTask 8.3：左侧队伍成员状态卡（3 名 mock 成员）。
        /// 每张卡片为 <see cref="InkPanel"/>，内含职业图标占位（<see cref="InkCell"/>）+
        /// 名称标签 + 职业 标签 + HP 条（朱红）+ MP 条（翡翠）。
        /// 卡片垂直排列，间距 8px。
        /// </summary>
        private void BuildPartyCards()
        {
            _partyContainer = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = Color.Transparent,
                ClipChildren = false,
                Size = new Float2(PartyCardWidth, 3 * PartyCardHeight + 2 * PartyCardGap),
            };

            _partyCards = new InkPanel[3];
            _partyHpBars = new InkBar[3];
            _partyMpBars = new InkBar[3];

            for (int i = 0; i < 3; i++)
            {
                var card = new InkPanel
                {
                    Variant = InkPanelVariant.Lightweight,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(0f, i * (PartyCardHeight + PartyCardGap)),
                    Size = new Float2(PartyCardWidth, PartyCardHeight),
                };

                // 职业图标占位（品质色边框）
                var classIcon = new InkCell
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(8f, 12f),
                    Size = new Float2(PartyIconSize, PartyIconSize),
                    Quality = InkWashTheme.InkQuality.Rare,
                };
                card.AddChild(classIcon);

                // 名称标签
                var nameLabel = new Label
                {
                    Text = _partyNames[i],
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 13f),
                    TextColor = InkWashTheme.TextDefault,
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(56f, 4f),
                    Size = new Float2(100f, 18f),
                };
                card.AddChild(nameLabel);

                // 职业副标签
                var classLabel = new Label
                {
                    Text = _partyClasses[i],
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 10f),
                    TextColor = InkWashTheme.TextTertiary,
                    HorizontalAlignment = TextAlignment.Far,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(156f, 4f),
                    Size = new Float2(56f, 18f),
                };
                card.AddChild(classLabel);

                // HP 条（朱红）
                var hpBar = new InkBar
                {
                    FillVariant = InkBarFillVariant.Vermilion,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(56f, 26f),
                    Size = new Float2(PartyBarWidth, PartyBarHeight),
                    Value = _partyHpRatio[i],
                };
                card.AddChild(hpBar);
                _partyHpBars[i] = hpBar;

                // MP 条（翡翠）
                var mpBar = new InkBar
                {
                    FillVariant = InkBarFillVariant.Jade,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(56f, 40f),
                    Size = new Float2(PartyBarWidth, PartyBarHeight),
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
        /// SubTask 8.6：左下角玩家状态面板（头像 + 竖排角色名 + HP/MP/XP 条）。
        /// <see cref="ContainerControl"/> 尺寸 300x180。
        /// 内含 <see cref="InkButton"/> 头像 56x56、<see cref="InkVerticalTitle"/> 角色名、
        /// 气血条（<see cref="InkBarFillVariant.Vermilion"/>）、
        /// 内力条（<see cref="InkBarFillVariant.Gold"/>）、
        /// 体魄条（<see cref="InkBarFillVariant.Jade"/>）及对应数值标签。
        /// 头像点击触发 <see cref="NavigationRequested"/>("nav-character-v2")。
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

            // 头像按钮：56x56
            _avatarButton = new InkButton
            {
                Variant = InkButtonVariant.Default,
                ButtonSize = InkButtonSize.Sm,
                Text = string.Empty,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 0f),
                Size = new Float2(AvatarSize, AvatarSize),
            };
            _avatarButton.ButtonClicked += OnAvatarButtonClicked;
            _playerStats.AddChild(_avatarButton);

            // 等级与阶段标签：头像正下方，与头像同宽
            _levelStageLabel = new Label
            {
                Text = $"Lv.{_mockLevel} · {StageToDisplayName(_mockStage)}",
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 11f),
                TextColor = InkWashTheme.TextBrand,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 62f),
                Size = new Float2(AvatarSize, 18f),
            };
            _playerStats.AddChild(_levelStageLabel);

            // 竖排角色名
            _playerNameLabel = new InkVerticalTitle
            {
                Text = _playerName,
                FontSize = 18f,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(70f, 0f),
                Size = new Float2(30f, 120f),
            };
            _playerStats.AddChild(_playerNameLabel);

            // 气血条（HP，朱红）
            _hpBar = new InkBar
            {
                FillVariant = InkBarFillVariant.Vermilion,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(110f, 8f),
                Size = new Float2(PlayerBarWidth, PlayerHpMpBarHeight),
                Value = _mockHpRatio,
            };
            _playerStats.AddChild(_hpBar);

            // 气血数值标签（与 HP 条同位置同尺寸，居中叠加显示）
            _hpLabel = new Label
            {
                Text = $"{_mockHpCurrent}/{_mockHpMax}",
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 11f),
                TextColor = InkWashTheme.Paper,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(110f, 8f),
                Size = new Float2(PlayerBarWidth, PlayerHpMpBarHeight),
            };
            _playerStats.AddChild(_hpLabel);

            // 内力条（MP，鎏金）
            _mpBar = new InkBar
            {
                FillVariant = InkBarFillVariant.Gold,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(110f, 32f),
                Size = new Float2(PlayerBarWidth, PlayerHpMpBarHeight),
                Value = _mockMpRatio,
            };
            _playerStats.AddChild(_mpBar);

            // 内力数值标签（与 MP 条同位置同尺寸，居中叠加显示）
            _mpLabel = new Label
            {
                Text = $"{_mockMpCurrent}/{_mockMpMax}",
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 11f),
                TextColor = InkWashTheme.Paper,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(110f, 32f),
                Size = new Float2(PlayerBarWidth, PlayerHpMpBarHeight),
            };
            _playerStats.AddChild(_mpLabel);

            // 体魄条（XP，翡翠）
            _xpBar = new InkBar
            {
                FillVariant = InkBarFillVariant.Jade,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(110f, 56f),
                Size = new Float2(PlayerBarWidth, PlayerBarHeight),
                Value = _mockXpRatio,
            };
            _playerStats.AddChild(_xpBar);

            // 体魄数值标签
            _xpLabel = new Label
            {
                Text = $"{_mockXpCurrent}/{_mockXpMax}",
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 11f),
                TextColor = InkWashTheme.TextBrand,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(110f, 70f),
                Size = new Float2(PlayerBarWidth, 16f),
            };
            _playerStats.AddChild(_xpLabel);

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

            // SubTask 8.2 小地图：右上角
            if (_minimap != null)
            {
                _minimap.Location = new Float2(sw - MinimapSize - Margin, Margin);
            }

            // SubTask 8.3 队伍成员卡：左侧上方
            if (_partyContainer != null)
            {
                _partyContainer.Location = new Float2(Margin, PartyStartY);
            }

            // SubTask 8.6 玩家状态面板：左下角
            if (_playerStats != null)
            {
                _playerStats.Location = new Float2(Margin, sh - PlayerStatsBottomOffset);
            }

            // SubTask 8.4 技能槽网格：右下角
            if (_skillGrid != null)
            {
                _skillGrid.Location = new Float2(sw - _skillGrid.Width - Margin, sh - SkillGridBottomOffset);
            }

            // SubTask 8.5 道具栏：底部居中
            if (_itemBar != null)
            {
                _itemBar.Location = new Float2(sw * 0.5f - _itemBar.Width * 0.5f, sh - ItemBarBottomOffset);
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
            if (_xpLabel != null)
                _xpLabel.Text = $"{(int)_boundCharacter.CurrentStamina}/{(int)_boundCharacter.MaxStamina}";
        }

        /// <summary>
        /// 将成长阶段枚举转换为中文显示名。
        /// </summary>
        /// <param name="stage">成长阶段枚举</param>
        /// <returns>中文显示名（武侠/仙侠/玄幻）</returns>
        private static string StageToDisplayName(CharacterStage stage)
        {
            switch (stage)
            {
                case CharacterStage.Wuxia:
                    return "武侠";
                case CharacterStage.Xianxia:
                    return "仙侠";
                case CharacterStage.Xuanhuan:
                    return "玄幻";
                default:
                    return "武侠";
            }
        }

        /// <summary>
        /// 刷新玩家身份信息（角色名、等级、阶段）。
        /// 由 <see cref="BindCharacter"/> 立即调用，并由 <see cref="RefreshBoundData"/> 每帧调用以保持同步。
        /// 未绑定时保留 mock 数据，不覆盖。
        /// </summary>
        private void RefreshPlayerIdentity()
        {
            if (_boundCharacter == null)
                return;

            // 角色名：Nickname 非空才覆盖
            if (_playerNameLabel != null && !string.IsNullOrEmpty(_boundCharacter.Nickname))
            {
                _playerNameLabel.Text = _boundCharacter.Nickname;
            }

            // 等级与阶段标签
            if (_levelStageLabel != null)
            {
                _levelStageLabel.Text = $"Lv.{_boundCharacter.Level} · {StageToDisplayName(_boundCharacter.CurrentStage)}";
            }
        }

        // ===================================================================
        // 事件处理
        // =======================================================================

        /// <summary>
        /// 头像按钮点击处理：触发 <see cref="NavigationRequested"/>("nav-character-v2")，
        /// 由 <see cref="InkPageRouter"/> 订阅后跳转角色属性页 V2。
        /// </summary>
        /// <param name="button">触发事件的按钮（未使用）</param>
        private void OnAvatarButtonClicked(Button button)
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

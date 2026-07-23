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
    /// 战斗 HUD V2 页面（沉浸式自由探索布局，对应 combat-hud.html）。
    /// 六大区域：
    /// <list type="bullet">
    ///   <item>左上：角色状态面板（八角头像 + 门派徽章 + 名称/等级 + 血/气/体三条）</item>
    ///   <item>顶部中央：任务追踪器（可折叠，目标清单 + 进度）</item>
    ///   <item>右上：小地图（160 圆形 + 区域名标签）</item>
    ///   <item>左下：增益/减益列表（32 图标 + 左边框色区分增益/减益）</item>
    ///   <item>底部中央：技能栏（双武器 + Q/W/E/R/F + 大招）+ 双行功能导航栏</item>
    ///   <item>右下：快捷道具栏（10 格 36x36）</item>
    /// </list>
    /// 通过 <see cref="NavigationRequested"/> 事件向 <see cref="InkPageRouter"/> 暴露导航请求。
    /// </summary>
    public class CombatHudV2Page : ContainerControl, IInkPage
    {
        // ===================================================================
        // 布局常量（1920x1080 参考分辨率，像素值对齐 combat-hud.html）
        // =======================================================================

        /// <summary>屏幕边缘统一边距（top/left/right/bottom 均 16px）</summary>
        private const float Margin = 16f;

        // --- 左上：角色状态面板 ---
        private const float PlayerPanelPadding = 12f;
        private const float AvatarSize = 80f;
        private const float SectBadgeSize = 26f;
        private const float InfoColumnWidth = 200f;
        private const float InfoColumnGap = 12f;
        private const float NameRowHeight = 18f;
        private const float HpBarHeight = 12f;
        private const float MpBarHeight = 8f;
        private const float SpBarHeight = 4f;
        private const float BarLabelWidth = 14f;
        private const float BarValueWidth = 70f;
        private const float BarRowGap = 6f;

        // --- 顶部中央：任务追踪器 ---
        private const float QuestTrackerWidth = 300f;
        private const float QuestHeaderHeight = 36f;
        private const float QuestBodyHeight = 150f;

        // --- 右上：小地图 ---
        private const float MinimapSize = 160f;
        private const float RegionLabelHeight = 24f;

        // --- 左下：增益/减益 ---
        private const float BuffIconSize = 32f;
        private const float BuffItemHeight = 38f;
        private const float BuffItemWidth = 120f;
        private const float BuffItemGap = 6f;
        private const float BuffBottomExtra = 4f;

        // --- 底部中央：技能栏 + 导航 ---
        private const float SkillSlotSize = 48f;
        private const float SkillSlotGap = 6f;
        private const float SkillBarPaddingX = 12f;
        private const float SkillBarPaddingY = 8f;
        private const float DividerWidth = 1f;
        private const float DividerHeight = 40f;
        private const float NavButtonWidth = 48f;
        private const float NavRowHeight = 36f;
        private const float SkillToNavGap = 8f;
        private const float NavRowGap = 2f;

        // --- 右下：快捷道具栏 ---
        private const float ItemSlotSize = 36f;
        private const float ItemSlotGap = 4f;
        private const float ItemBarPaddingX = 8f;
        private const float ItemBarPaddingY = 6f;
        private const float ItemBottomExtra = 4f;
        private const int ItemSlotCount = 10;

        // ===================================================================
        // 子控件引用
        // =======================================================================

        // 左上：角色状态面板
        private InkPanel _playerPanel;
        private OctagonAvatar _playerAvatar;
        private Label _playerNameLabel;
        private Label _playerLevelLabel;
        private InkBar _hpBar;
        private Label _hpValueLabel;
        private InkBar _mpBar;
        private Label _mpValueLabel;
        private InkBar _spBar;

        // 顶部中央：任务追踪器
        private InkPanel _questPanel;
        private ContainerControl _questBody;
        private Label _questChevron;
        private bool _questExpanded = true;

        // 右上：小地图
        private InkMinimap _minimap;
        private Label _regionLabel;

        // 左下：增益/减益
        private ContainerControl _buffContainer;

        // 底部中央：技能栏 + 导航
        private ContainerControl _skillBar;
        private HudSkillSlot[] _skillSlots;
        private ContainerControl _navRow1;
        private ContainerControl _navRow2;

        // 右下：快捷道具栏
        private ContainerControl _itemBar;

        // ===================================================================
        // mock 数据（对齐 combat-hud.html）
        // =======================================================================

        private string _playerName = "逍遥客";
        private int _playerLevel = 60;
        private int _hpCurrent = 12450;
        private int _hpMax = 15000;
        private int _mpCurrent = 800;
        private int _mpMax = 1000;
        private float _spRatio = 0.65f;

        /// <summary>技能槽配置：字符、快捷键、冷却秒数（0=就绪）、是否大招</summary>
        private static readonly (string glyph, string key, float cooldown, bool ultimate)[] SkillConfig =
        {
            ("剑", "", 0f, false),   // 武器1（激活）
            ("枪", "", 0f, false),   // 武器2
            ("斩", "Q", 0f, false),
            ("疾", "W", 0f, false),
            ("焰", "E", 8f, false),
            ("阵", "R", 0f, false),
            ("雷", "F", 15f, false),
            ("万", "G", 0f, true),   // 大招
        };

        /// <summary>道具槽配置：字符、数量、色调（亮色变体，背景按 15% 混入墨底）</summary>
        private static readonly (string glyph, int count, Color tint)[] ItemConfig =
        {
            ("药", 5, InkWashTheme.BloodBright),
            ("气", 3, InkWashTheme.JadeBright),
            ("食", 2, InkWashTheme.AlertHover),
            ("符", 1, InkWashTheme.GoldBright),
        };

        // ===================================================================
        // 屏幕尺寸缓存与数据绑定
        // =======================================================================

        private Float2 _screenSize;
        private CharacterAttributesComponent _boundCharacter;

        /// <summary>
        /// 导航请求事件。参数为目标页面的 dom-id。
        /// 由 <see cref="InkPageRouter"/> 订阅以执行页面跳转。
        /// </summary>
        public event Action<string> NavigationRequested;

        // ===================================================================
        // 构造函数
        // ===================================================================

        /// <summary>
        /// 构造函数：初始化全部六大区域，使用 mock 数据填充。
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
                BuildPlayerPanel();
                BuildQuestTracker();
                BuildMinimap();
                BuildBuffList();
                BuildSkillBar();
                BuildNavBars();
                BuildItemBar();

                ApplyLayout();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[CombatHudV2Page] 初始化失败: {ex.Message}");
            }
        }

        // ===================================================================
        // 区域构造：左上角色状态面板
        // =======================================================================

        /// <summary>
        /// 左上角色状态面板：八角头像 + 门派徽章 + 名称/等级 + 血/气/体三条。
        /// </summary>
        private void BuildPlayerPanel()
        {
            float panelW = PlayerPanelPadding + AvatarSize + InfoColumnGap + InfoColumnWidth + PlayerPanelPadding;
            float panelH = PlayerPanelPadding + AvatarSize + PlayerPanelPadding;

            _playerPanel = new InkPanel
            {
                Variant = InkPanelVariant.Default,
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(panelW, panelH),
                Radius = InkWashTheme.RadiusLg,
            };

            // 八角头像（含金色描边 + 门派徽章）
            _playerAvatar = new OctagonAvatar
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(PlayerPanelPadding, PlayerPanelPadding),
                Size = new Float2(AvatarSize, AvatarSize),
                Glyph = _playerName.Length > 0 ? _playerName[0].ToString() : "侠",
            };
            _playerAvatar.Clicked += () => RequestNavigation(InkPageDomIds.NavCharacterV2);
            _playerPanel.AddChild(_playerAvatar);

            // 信息列
            float colX = PlayerPanelPadding + AvatarSize + InfoColumnGap;
            float rowY = PlayerPanelPadding;

            // 名称 + 等级
            _playerNameLabel = new Label
            {
                Text = _playerName,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 15f),
                TextColor = InkWashTheme.PaperBright,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(colX, rowY),
                Size = new Float2(InfoColumnWidth - 60f, NameRowHeight),
            };
            _playerPanel.AddChild(_playerNameLabel);

            _playerLevelLabel = new Label
            {
                Text = $"Lv.{_playerLevel}",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 16f),
                TextColor = InkWashTheme.GoldPrimary,
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(colX + InfoColumnWidth - 60f, rowY),
                Size = new Float2(60f, NameRowHeight),
            };
            _playerPanel.AddChild(_playerLevelLabel);

            rowY += NameRowHeight + BarRowGap;

            // 血条（12px，朱砂渐变）
            rowY = BuildStatBar(_playerPanel, colX, rowY, "血", HpBarHeight,
                InkBarFillVariant.Vermilion, out _hpBar, out _hpValueLabel, true);
            _hpBar.Value = (float)_hpCurrent / _hpMax;
            _hpValueLabel.Text = $"{_hpCurrent}/{_hpMax}";

            // 气条（8px，青渐变）
            rowY = BuildStatBar(_playerPanel, colX, rowY, "气", MpBarHeight,
                InkBarFillVariant.Jade, out _mpBar, out _mpValueLabel, true);
            _mpBar.Value = (float)_mpCurrent / _mpMax;
            _mpValueLabel.Text = $"{_mpCurrent}/{_mpMax}";

            // 体条（4px，暖金，无数值）
            BuildStatBar(_playerPanel, colX, rowY, "体", SpBarHeight,
                InkBarFillVariant.Alert, out _spBar, out _, false);
            _spBar.Value = _spRatio;

            AddChild(_playerPanel);
        }

        /// <summary>
        /// 构建单条状态条行（标签 + 进度条 + 可选数值）。返回下一行 Y。
        /// </summary>
        private float BuildStatBar(ContainerControl parent, float colX, float rowY, string label,
            float barHeight, InkBarFillVariant variant, out InkBar bar, out Label valueLabel, bool showValue)
        {
            var barLabel = new Label
            {
                Text = label,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f),
                TextColor = InkWashTheme.TextSecondary,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(colX, rowY),
                Size = new Float2(BarLabelWidth, barHeight),
            };
            parent.AddChild(barLabel);

            float valueW = showValue ? BarValueWidth : 0f;
            float barX = colX + BarLabelWidth + 6f;
            float barW = InfoColumnWidth - BarLabelWidth - 6f - (showValue ? valueW + 6f : 0f);

            bar = new InkBar
            {
                FillVariant = variant,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(barX, rowY),
                Size = new Float2(barW, barHeight),
            };
            parent.AddChild(bar);

            valueLabel = null;
            if (showValue)
            {
                valueLabel = new Label
                {
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 10f),
                    TextColor = InkWashTheme.TextDefault,
                    HorizontalAlignment = TextAlignment.Far,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(barX + barW + 6f, rowY),
                    Size = new Float2(valueW, barHeight),
                };
                parent.AddChild(valueLabel);
            }

            return rowY + barHeight + BarRowGap;
        }

        // ===================================================================
        // 区域构造：顶部中央任务追踪器
        // =======================================================================

        /// <summary>
        /// 顶部中央任务追踪器（可折叠）：卷轴图标 + 标题 + 进度 + 目标清单。
        /// </summary>
        private void BuildQuestTracker()
        {
            _questPanel = new InkPanel
            {
                Variant = InkPanelVariant.Default,
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(QuestTrackerWidth, QuestHeaderHeight + QuestBodyHeight),
                Radius = InkWashTheme.RadiusLg,
            };

            // 头部（可点击折叠）
            var header = new Button
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Float2(QuestTrackerWidth, QuestHeaderHeight),
                BackgroundColor = Color.Transparent,
                BorderThickness = 0f,
            };
            header.Clicked += ToggleQuestTracker;
            _questPanel.AddChild(header);

            // 卷轴图标占位（金色文字符号）
            var scrollIcon = new Label
            {
                Text = "卷",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 14f),
                TextColor = InkWashTheme.GoldPrimary,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, 0f),
                Size = new Float2(16f, QuestHeaderHeight),
            };
            _questPanel.AddChild(scrollIcon);

            var questTitle = new Label
            {
                Text = "当前任务：初入江湖",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 14f),
                TextColor = InkWashTheme.PaperBright,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(38f, 0f),
                Size = new Float2(170f, QuestHeaderHeight),
            };
            _questPanel.AddChild(questTitle);

            var questProgress = new Label
            {
                Text = "2/5",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 12f),
                TextColor = InkWashTheme.GoldPrimary,
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(QuestTrackerWidth - 60f, 0f),
                Size = new Float2(30f, QuestHeaderHeight),
            };
            _questPanel.AddChild(questProgress);

            _questChevron = new Label
            {
                Text = "▾",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                TextColor = InkWashTheme.TextSecondary,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(QuestTrackerWidth - 26f, 0f),
                Size = new Float2(14f, QuestHeaderHeight),
            };
            _questPanel.AddChild(_questChevron);

            // 主体（目标清单）
            _questBody = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, QuestHeaderHeight),
                Size = new Float2(QuestTrackerWidth, QuestBodyHeight),
                BackgroundColor = Color.Transparent,
                ClipChildren = true,
            };
            _questPanel.AddChild(_questBody);

            // 顶部分隔线
            var separator = new Control
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Float2(QuestTrackerWidth, 1f),
                BackgroundColor = InkWashTheme.BorderFaint,
            };
            _questBody.AddChild(separator);

            var desc = new Label
            {
                Text = "初入江湖，拜访开封城内各派长老，了解武林格局。",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextSecondary,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Near,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, 8f),
                Size = new Float2(QuestTrackerWidth - 32f, 34f),
                AutoHeight = true,
            };
            _questBody.AddChild(desc);

            // 目标清单：done=已完成青勾, current=当前金点, todo=待办灰圈
            var objectives = new (string state, string text)[]
            {
                ("done", "前往武当山拜见掌门"),
                ("done", "领取入门心法"),
                ("current", "前往开封城郊探访"),
                ("todo", "击败山贼头目"),
                ("todo", "回报开封府衙"),
            };

            float objY = 48f;
            foreach (var (state, text) in objectives)
            {
                Color iconColor = state == "done" ? InkWashTheme.JadePrimary
                    : state == "current" ? InkWashTheme.GoldPrimary
                    : InkWashTheme.TextTertiary;
                Color textColor = state == "done" ? InkWashTheme.TextSecondary
                    : state == "current" ? InkWashTheme.PaperBright
                    : InkWashTheme.TextTertiary;
                string icon = state == "done" ? "✓" : state == "current" ? "◉" : "○";

                var iconLabel = new Label
                {
                    Text = icon,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                    TextColor = iconColor,
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(16f, objY),
                    Size = new Float2(12f, 16f),
                };
                _questBody.AddChild(iconLabel);

                var textLabel = new Label
                {
                    Text = text,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                    TextColor = textColor,
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(34f, objY),
                    Size = new Float2(QuestTrackerWidth - 50f, 16f),
                };
                _questBody.AddChild(textLabel);

                objY += 21f;
            }

            AddChild(_questPanel);
            ApplyQuestCollapse();
        }

        /// <summary>切换任务追踪器折叠状态。</summary>
        private void ToggleQuestTracker()
        {
            _questExpanded = !_questExpanded;
            ApplyQuestCollapse();
        }

        /// <summary>应用任务追踪器折叠/展开状态。</summary>
        private void ApplyQuestCollapse()
        {
            if (_questPanel == null)
                return;
            _questPanel.Height = _questExpanded ? QuestHeaderHeight + QuestBodyHeight : QuestHeaderHeight;
            if (_questBody != null)
                _questBody.Visible = _questExpanded;
            if (_questChevron != null)
                _questChevron.Text = _questExpanded ? "▾" : "▸";
        }

        // ===================================================================
        // 区域构造：右上方小地图
        // =======================================================================

        /// <summary>
        /// 右上方小地图：160 圆形 + 区域名标签。
        /// </summary>
        private void BuildMinimap()
        {
            _minimap = new InkMinimap
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(MinimapSize, MinimapSize),
            };

            // mock 实体点位（对齐 combat-hud.html：NPC 金 / 敌红 / 友青 / 玩家居中）
            _minimap.AddEntity(InkMinimapEntityType.Player, 0f, 0f);
            _minimap.AddEntity(InkMinimapEntityType.NPC, -0.16f, -0.30f);
            _minimap.AddEntity(InkMinimapEntityType.NPC, 0.36f, 0.16f);
            _minimap.AddEntity(InkMinimapEntityType.NPC, -0.36f, 0.44f);
            _minimap.AddEntity(InkMinimapEntityType.Enemy, 0.24f, -0.44f);
            _minimap.AddEntity(InkMinimapEntityType.Enemy, 0.10f, 0.36f);
            _minimap.AddEntity(InkMinimapEntityType.Friendly, 0.04f, -0.10f);

            AddChild(_minimap);

            _regionLabel = new Label
            {
                Text = "清河 · 开封城郊",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 12f),
                TextColor = InkWashTheme.PaperBright,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(MinimapSize, RegionLabelHeight),
                BackgroundColor = InkWashTheme.Panel,
            };
            AddChild(_regionLabel);
        }

        // ===================================================================
        // 区域构造：左下增益/减益列表
        // =======================================================================

        /// <summary>
        /// 左下增益/减益列表：32 图标 + 名称 + 倒计时，左边框色区分增益（青）/减益（朱砂）。
        /// </summary>
        private void BuildBuffList()
        {
            var buffs = new (string glyph, string name, string time, bool positive)[]
            {
                ("轻", "轻功加速", "5:23", true),
                ("盾", "内力护盾", "12:00", true),
                ("毒", "中毒", "0:15", false),
                ("缓", "减速", "0:08", false),
            };

            float totalH = buffs.Length * BuffItemHeight + (buffs.Length - 1) * BuffItemGap;
            _buffContainer = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(BuffItemWidth, totalH),
                BackgroundColor = Color.Transparent,
                ClipChildren = false,
            };

            for (int i = 0; i < buffs.Length; i++)
            {
                var (glyph, name, time, positive) = buffs[i];
                BuildBuffItem(_buffContainer, i * (BuffItemHeight + BuffItemGap), glyph, name, time, positive);
            }

            AddChild(_buffContainer);
        }

        /// <summary>构建单个增益/减益条目。</summary>
        private void BuildBuffItem(ContainerControl parent, float y, string glyph, string name, string time, bool positive)
        {
            Color accent = positive ? InkWashTheme.JadePrimary : InkWashTheme.BloodPrimary;
            Color iconBgBase = positive ? InkWashTheme.JadeDeep : InkWashTheme.BloodPrimary;
            Color glyphColor = positive ? InkWashTheme.JadeBright : InkWashTheme.BloodBright;

            var item = new InkPanel
            {
                Variant = InkPanelVariant.Lightweight,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, y),
                Size = new Float2(BuffItemWidth, BuffItemHeight),
                Radius = InkWashTheme.RadiusSm + 2f,
            };
            parent.AddChild(item);

            // 左边框色条（2px，区分增益/减益）
            var leftBorder = new Control
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Float2(2f, BuffItemHeight),
                BackgroundColor = accent,
            };
            item.AddChild(leftBorder);

            // 图标
            var iconLabel = new Label
            {
                Text = glyph,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 16f),
                TextColor = glyphColor,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(6f, (BuffItemHeight - BuffIconSize) * 0.5f),
                Size = new Float2(BuffIconSize, BuffIconSize),
                BackgroundColor = new Color(iconBgBase.R, iconBgBase.G, iconBgBase.B, 0.3f),
            };
            item.AddChild(iconLabel);

            // 名称
            var nameLabel = new Label
            {
                Text = name,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f),
                TextColor = InkWashTheme.PaperBright,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(6f + BuffIconSize + 6f, 4f),
                Size = new Float2(BuffItemWidth - BuffIconSize - 20f, 14f),
            };
            item.AddChild(nameLabel);

            // 倒计时
            var timeLabel = new Label
            {
                Text = time,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 10f),
                TextColor = glyphColor,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(6f + BuffIconSize + 6f, 20f),
                Size = new Float2(BuffItemWidth - BuffIconSize - 20f, 14f),
            };
            item.AddChild(timeLabel);
        }

        // ===================================================================
        // 区域构造：底部中央技能栏
        // =======================================================================

        /// <summary>
        /// 底部中央技能栏：双武器槽 + 分隔线 + Q/W/E/R/F 技能槽 + 分隔线 + 大招槽。
        /// 槽位 48x48，冷却 conic 遮罩 + 大招充能条与脉冲由 <see cref="HudSkillSlot"/> 实现。
        /// </summary>
        private void BuildSkillBar()
        {
            int slotCount = SkillConfig.Length;
            float slotsW = slotCount * SkillSlotSize + (slotCount - 1) * SkillSlotGap;
            float divExtra = 2f * (DividerWidth + 4f);
            float barW = SkillBarPaddingX * 2f + slotsW + divExtra;
            float barH = SkillBarPaddingY * 2f + SkillSlotSize;

            _skillBar = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(barW, barH),
                BackgroundColor = new Color(InkWashTheme.PanelSolid.R, InkWashTheme.PanelSolid.G, InkWashTheme.PanelSolid.B, 0.80f),
                ClipChildren = false,
            };

            _skillSlots = new HudSkillSlot[slotCount];
            float x = SkillBarPaddingX;
            for (int i = 0; i < slotCount; i++)
            {
                var (glyph, key, cooldown, ultimate) = SkillConfig[i];

                var slot = new HudSkillSlot
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(x, SkillBarPaddingY),
                    Size = new Float2(SkillSlotSize, SkillSlotSize),
                    Glyph = glyph,
                    Hotkey = key,
                    IsUltimate = ultimate,
                    IsActiveWeapon = i == 0,
                    CooldownSeconds = cooldown,
                    MaxCooldownSeconds = cooldown,
                    ChargeRatio = ultimate ? 0.70f : 0f,
                };
                if (i == 1) // 副武器未激活
                    slot.AlphaMultiplier = 0.5f;
                _skillSlots[i] = slot;
                _skillBar.AddChild(slot);
                x += SkillSlotSize + SkillSlotGap;

                // 在武器组后与大招前插入分隔线
                if (i == 1 || i == slotCount - 2)
                {
                    var divider = new Control
                    {
                        AnchorPreset = AnchorPresets.TopLeft,
                        Location = new Float2(x - SkillSlotGap + 2f, (barH - DividerHeight) * 0.5f),
                        Size = new Float2(DividerWidth, DividerHeight),
                        BackgroundColor = InkWashTheme.GoldFaint,
                    };
                    _skillBar.AddChild(divider);
                    x += DividerWidth + 4f;
                }
            }

            AddChild(_skillBar);
        }

        // ===================================================================
        // 区域构造：底部中央功能导航栏（双行）
        // ===================================================================

        /// <summary>
        /// 功能导航栏：第一行 8 主入口 + 传统模式切换，第二行 9 子系统入口。
        /// </summary>
        private void BuildNavBars()
        {
            var row1 = new (string label, string domId)[]
            {
                ("角色", InkPageDomIds.NavCharacterV2),
                ("武学", InkPageDomIds.NavSkillPanel),
                ("背包", InkPageDomIds.NavInventory),
                ("任务", InkPageDomIds.NavQuestLog),
                ("地图", InkPageDomIds.NavWorldMap),
                ("社交", InkPageDomIds.NavSocialGuild),
                ("商城", InkPageDomIds.NavSocialShop),
                ("罗盘", InkPageDomIds.NavCompass),
            };
            var row2 = new (string label, string domId)[]
            {
                ("强化", InkPageDomIds.NavEquipmentEnhance),
                ("制造", InkPageDomIds.NavCrafting),
                ("坐骑", InkPageDomIds.NavMountPet),
                ("好友", InkPageDomIds.NavFriends),
                ("邮件", InkPageDomIds.NavSocialMail),
                ("排行", InkPageDomIds.NavLeaderboard),
                ("师徒", InkPageDomIds.NavMentor),
                ("成就", InkPageDomIds.NavAchievement),
                ("副本", InkPageDomIds.NavDungeonEntry),
            };

            _navRow1 = BuildNavRow(row1, true);
            _navRow2 = BuildNavRow(row2, false);
            AddChild(_navRow1);
            AddChild(_navRow2);
        }

        /// <summary>构建单行导航栏。</summary>
        private ContainerControl BuildNavRow((string label, string domId)[] entries, bool includeTraditionalToggle)
        {
            int count = entries.Length + (includeTraditionalToggle ? 2 : 0);
            float rowW = count * NavButtonWidth + 8f;
            var row = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(rowW, NavRowHeight),
                BackgroundColor = new Color(InkWashTheme.PanelSolid.R, InkWashTheme.PanelSolid.G, InkWashTheme.PanelSolid.B, 0.75f),
                ClipChildren = false,
            };

            float x = 4f;
            foreach (var (label, domId) in entries)
            {
                AddNavButton(row, x, label, domId, false);
                x += NavButtonWidth;
            }

            if (includeTraditionalToggle)
            {
                var divider = new Control
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(x + NavButtonWidth * 0.5f - 0.5f, (NavRowHeight - 20f) * 0.5f),
                    Size = new Float2(1f, 20f),
                    BackgroundColor = InkWashTheme.BorderGold,
                };
                row.AddChild(divider);
                x += NavButtonWidth;

                AddNavButton(row, x, "传统模式", InkPageDomIds.ToggleTraditional, true);
            }

            return row;
        }

        /// <summary>添加单个导航按钮（悬停金色高亮由 Ghost 变体提供）。</summary>
        private void AddNavButton(ContainerControl row, float x, string label, string domId, bool highlighted)
        {
            var btn = new InkButton
            {
                Text = label,
                Variant = InkButtonVariant.Ghost,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(x, 0f),
                Size = new Float2(NavButtonWidth, NavRowHeight),
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 10f),
            };
            btn.TextColor = highlighted ? InkWashTheme.GoldPrimary : InkWashTheme.TextSecondary;
            string target = domId;
            btn.Clicked += () => RequestNavigation(target);
            row.AddChild(btn);
        }

        // ===================================================================
        // 区域构造：右下快捷道具栏
        // ===================================================================

        /// <summary>
        /// 右下快捷道具栏：10 格 36x36（4 格有物 + 6 格空槽），快捷键 1-0。
        /// </summary>
        private void BuildItemBar()
        {
            float barW = ItemBarPaddingX * 2f + ItemSlotCount * ItemSlotSize + (ItemSlotCount - 1) * ItemSlotGap;
            float barH = ItemBarPaddingY * 2f + ItemSlotSize;

            _itemBar = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(barW, barH),
                BackgroundColor = new Color(InkWashTheme.PanelSolid.R, InkWashTheme.PanelSolid.G, InkWashTheme.PanelSolid.B, 0.80f),
                ClipChildren = false,
            };

            string[] keys = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0" };
            for (int i = 0; i < ItemSlotCount; i++)
            {
                float x = ItemBarPaddingX + i * (ItemSlotSize + ItemSlotGap);
                bool filled = i < ItemConfig.Length;

                var slot = new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(x, ItemBarPaddingY),
                    Size = new Float2(ItemSlotSize, ItemSlotSize),
                    BackgroundColor = filled ? Color.Transparent : new Color(0f, 0f, 0f, 0.10f),
                };
                _itemBar.AddChild(slot);

                if (filled)
                {
                    var (glyph, count, tint) = ItemConfig[i];
                    var inner = new Label
                    {
                        Text = glyph,
                        Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 18f),
                        TextColor = tint,
                        HorizontalAlignment = TextAlignment.Center,
                        VerticalAlignment = TextAlignment.Center,
                        AnchorPreset = AnchorPresets.TopLeft,
                        Location = new Float2(1f, 1f),
                        Size = new Float2(ItemSlotSize - 2f, ItemSlotSize - 2f),
                        BackgroundColor = Color.Lerp(InkWashTheme.BaseTertiary, tint, 0.15f),
                    };
                    slot.AddChild(inner);

                    var countLabel = new Label
                    {
                        Text = count.ToString(),
                        Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 10f),
                        TextColor = InkWashTheme.PaperBright,
                        HorizontalAlignment = TextAlignment.Far,
                        VerticalAlignment = TextAlignment.Far,
                        AnchorPreset = AnchorPresets.TopLeft,
                        Location = new Float2(ItemSlotSize - 14f, ItemSlotSize - 12f),
                        Size = new Float2(12f, 11f),
                    };
                    slot.AddChild(countLabel);
                }

                var keyLabel = new Label
                {
                    Text = keys[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 10f),
                    TextColor = InkWashTheme.TextTertiary,
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Near,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(2f, 1f),
                    Size = new Float2(10f, 10f),
                };
                slot.AddChild(keyLabel);
            }

            AddChild(_itemBar);
        }

        // ===================================================================
        // 布局计算
        // ===================================================================

        /// <summary>根据当前 <see cref="_screenSize"/> 重新计算所有子控件的位置。</summary>
        private void ApplyLayout()
        {
            float sw = _screenSize.X;
            float sh = _screenSize.Y;

            if (_playerPanel != null)
                _playerPanel.Location = new Float2(Margin, Margin);

            if (_questPanel != null)
                _questPanel.Location = new Float2(sw * 0.5f - QuestTrackerWidth * 0.5f, Margin);

            if (_minimap != null)
                _minimap.Location = new Float2(sw - MinimapSize - Margin, Margin);
            if (_regionLabel != null)
                _regionLabel.Location = new Float2(sw - MinimapSize - Margin, Margin + MinimapSize + 4f);

            if (_buffContainer != null)
                _buffContainer.Location = new Float2(Margin, sh - _buffContainer.Height - Margin - BuffBottomExtra);

            // 底部中央：技能栏 + 双行导航（自下而上堆叠）
            float nav2Y = sh - NavRowHeight - Margin;
            float nav1Y = nav2Y - NavRowHeight - NavRowGap;
            if (_navRow1 != null)
                _navRow1.Location = new Float2(sw * 0.5f - _navRow1.Width * 0.5f, nav1Y);
            if (_navRow2 != null)
                _navRow2.Location = new Float2(sw * 0.5f - _navRow2.Width * 0.5f, nav2Y);
            if (_skillBar != null)
                _skillBar.Location = new Float2(sw * 0.5f - _skillBar.Width * 0.5f, nav1Y - SkillToNavGap - _skillBar.Height);

            if (_itemBar != null)
                _itemBar.Location = new Float2(sw - _itemBar.Width - Margin, sh - _itemBar.Height - Margin - ItemBottomExtra);
        }

        /// <summary>在屏幕尺寸变化时重新布局所有子控件。</summary>
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

        /// <summary>绑定角色属性组件。绑定后血/气/体条每帧读取真实数据，传入 null 解除绑定回退 mock。</summary>
        public void BindCharacter(CharacterAttributesComponent component)
        {
            _boundCharacter = component;
            RefreshPlayerIdentity();
        }

        // ===================================================================
        // 生命周期
        // ===================================================================

        /// <inheritdoc />
        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);
            RefreshBoundData();
        }

        /// <summary>每帧从绑定数据源刷新血/气/体。未绑定时保持 mock。</summary>
        private void RefreshBoundData()
        {
            if (_boundCharacter == null)
                return;

            RefreshPlayerIdentity();

            float hpRatio = _boundCharacter.MaxHealth > 0f
                ? Mathf.Clamp(_boundCharacter.CurrentHealth / _boundCharacter.MaxHealth, 0f, 1f) : 0f;
            float mpRatio = _boundCharacter.MaxEnergy > 0f
                ? Mathf.Clamp(_boundCharacter.CurrentEnergy / _boundCharacter.MaxEnergy, 0f, 1f) : 0f;
            float spRatio = _boundCharacter.MaxStamina > 0f
                ? Mathf.Clamp(_boundCharacter.CurrentStamina / _boundCharacter.MaxStamina, 0f, 1f) : 0f;

            if (_hpBar != null) _hpBar.Value = hpRatio;
            if (_hpValueLabel != null)
                _hpValueLabel.Text = $"{(int)_boundCharacter.CurrentHealth}/{(int)_boundCharacter.MaxHealth}";
            if (_mpBar != null) _mpBar.Value = mpRatio;
            if (_mpValueLabel != null)
                _mpValueLabel.Text = $"{(int)_boundCharacter.CurrentEnergy}/{(int)_boundCharacter.MaxEnergy}";
            if (_spBar != null) _spBar.Value = spRatio;
        }

        /// <summary>刷新玩家身份信息（名称、等级）。未绑定时保留 mock。</summary>
        private void RefreshPlayerIdentity()
        {
            if (_boundCharacter == null)
                return;

            if (_playerNameLabel != null && !string.IsNullOrEmpty(_boundCharacter.Nickname))
            {
                _playerNameLabel.Text = _boundCharacter.Nickname;
                if (_playerAvatar != null)
                    _playerAvatar.Glyph = _boundCharacter.Nickname[0].ToString();
            }
            if (_playerLevelLabel != null)
                _playerLevelLabel.Text = $"Lv.{_boundCharacter.Level}";
        }

        // ===================================================================
        // 事件处理
        // ===================================================================

        /// <summary>触发导航请求（带异常保护）。</summary>
        private void RequestNavigation(string domId)
        {
            try
            {
                NavigationRequested?.Invoke(domId);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[CombatHudV2Page] NavigationRequested({domId}) 触发失败: {ex.Message}");
            }
        }

        // ===================================================================
        // 嵌套控件：八角头像
        // ===================================================================

        /// <summary>
        /// 八角头像控件：金色八角描边 + 墨底渐变 + 中央门派字 + 右下门派徽章。
        /// 对应 combat-hud.html 的 .hud-avatar-octagon（clip-path 八角 + 2px 金边）。
        /// </summary>
        private sealed class OctagonAvatar : Button
        {
            /// <summary>中央显示的字（默认取角色名首字）</summary>
            public string Glyph { get; set; } = "侠";

            /// <summary>八角切点比例（对齐 CSS clip-path polygon）</summary>
            private static readonly Float2[] OctagonRatios =
            {
                new Float2(0.30f, 0f), new Float2(0.70f, 0f),
                new Float2(1f, 0.30f), new Float2(1f, 0.70f),
                new Float2(0.70f, 1f), new Float2(0.30f, 1f),
                new Float2(0f, 0.70f), new Float2(0f, 0.30f),
            };

            public OctagonAvatar()
            {
                BackgroundColor = Color.Transparent;
                BorderThickness = 0f;
            }

            /// <inheritdoc />
            public override void Draw()
            {
                base.Draw();
                if (Width <= 0f || Height <= 0f)
                    return;

                // 外层八角（金色，作为 2px 描边）
                var outer = GetOctagonPoints(0f);
                FillOctagon(outer, InkWashTheme.GoldPrimary);

                // 内层八角（墨底 + 淡金叠加，近似 135deg mist→paper 渐变）
                var inner = GetOctagonPoints(2f);
                FillOctagon(inner, InkWashTheme.BaseTertiary);
                FillOctagon(inner, InkWashTheme.BgMist);

                // 中央字（楷书 36px 金色）
                var fontRef = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 36f);
                var font = fontRef.GetFont();
                if (font != null)
                {
                    Render2D.DrawText(font, Glyph, new Rectangle(0, 0, Width, Height),
                        InkWashTheme.GoldPrimary, TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
                }

                // 门派徽章（右下 26px 圆形，墨底 + 金边 + 金字）
                float badge = SectBadgeSize;
                var bc = new Float2(Width - badge + 3f, Height - badge + 3f) + new Float2(badge * 0.5f, badge * 0.5f);
                InkRenderHelper.FillCircle(bc, badge * 0.5f, InkWashTheme.PanelSolid);
                InkRenderHelper.DrawCircle(bc, badge * 0.5f, InkWashTheme.GoldPrimary, 1f);
                var badgeFont = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 14f).GetFont();
                if (badgeFont != null)
                {
                    Render2D.DrawText(badgeFont, "山",
                        new Rectangle(bc.X - badge * 0.5f, bc.Y - badge * 0.5f, badge, badge),
                        InkWashTheme.GoldPrimary, TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
                }
            }

            /// <summary>按指定内缩量获取八角形顶点（绝对坐标）。</summary>
            private Float2[] GetOctagonPoints(float inset)
            {
                var pts = new Float2[8];
                for (int i = 0; i < 8; i++)
                {
                    float px = OctagonRatios[i].X * Width;
                    float py = OctagonRatios[i].Y * Height;
                    // 向中心内缩 inset
                    float cx = Width * 0.5f, cy = Height * 0.5f;
                    float dx = px - cx, dy = py - cy;
                    float len = Mathf.Sqrt(dx * dx + dy * dy);
                    if (len > 0f)
                    {
                        px -= dx / len * inset;
                        py -= dy / len * inset;
                    }
                    pts[i] = new Float2(px, py);
                }
                return pts;
            }

            /// <summary>用三角形扇填充八角形。</summary>
            private static void FillOctagon(Float2[] pts, Color color)
            {
                var center = new Float2(pts[0].X, pts[0].Y);
                for (int i = 0; i < 8; i++)
                {
                    center += pts[i];
                }
                center *= 1f / 8f;

                var vertices = new Float2[8 * 3];
                for (int i = 0; i < 8; i++)
                {
                    vertices[i * 3] = center;
                    vertices[i * 3 + 1] = pts[i];
                    vertices[i * 3 + 2] = pts[(i + 1) % 8];
                }
                Render2D.FillTriangles(vertices, color);
            }
        }

        // ===================================================================
        // 嵌套控件：技能槽
        // ===================================================================

        /// <summary>
        /// HUD 技能槽控件（48x48 方形，4px 圆角）。
        /// 支持：武器激活态（2px 金边 + 辉光）、冷却 conic 遮罩 + 倒计时、大招脉冲 + 充能条。
        /// 对应 combat-hud.html 的 .hud-skill-slot / .hud-weapon-active / .hud-ultimate。
        /// </summary>
        private sealed class HudSkillSlot : ContainerControl
        {
            private const int SectorSegments = 32;

            /// <summary>技能字符</summary>
            public string Glyph { get; set; } = "";

            /// <summary>快捷键标签（空则不显示）</summary>
            public string Hotkey { get; set; } = "";

            /// <summary>是否大招槽（金边 + 脉冲 + 充能条）</summary>
            public bool IsUltimate { get; set; }

            /// <summary>是否激活武器（2px 金边 + 辉光）</summary>
            public bool IsActiveWeapon { get; set; }

            /// <summary>当前冷却剩余秒数</summary>
            public float CooldownSeconds { get; set; }

            /// <summary>冷却总时长（秒）</summary>
            public float MaxCooldownSeconds { get; set; }

            /// <summary>大招充能比例 0-1</summary>
            public float ChargeRatio { get; set; }

            /// <summary>整体透明度倍率 0-1（用于未激活武器减淡）</summary>
            public float AlphaMultiplier { get; set; } = 1f;

            private bool _hovered;
            private float _pulseTime;

            /// <summary>应用透明度倍率。</summary>
            private Color A(Color c) => new Color(c.R, c.G, c.B, c.A * AlphaMultiplier);

            /// <inheritdoc />
            public override void Update(float deltaTime)
            {
                base.Update(deltaTime);
                if (CooldownSeconds > 0f)
                    CooldownSeconds = Mathf.Max(0f, CooldownSeconds - deltaTime);
                if (IsUltimate)
                    _pulseTime += deltaTime;
            }

            /// <inheritdoc />
            public override void Draw()
            {
                base.Draw();
                if (Width <= 0f || Height <= 0f)
                    return;

                var rect = new Rectangle(0, 0, Width, Height);
                float radius = InkWashTheme.RadiusSm + 2f; // 4px

                // 大招脉冲外辉光
                if (IsUltimate)
                {
                    float pulse = 0.3f + 0.35f * (0.5f + 0.5f * Mathf.Sin(_pulseTime * 2.5f));
                    InkRenderHelper.FillRoundedRectangle(rect, radius + 3f,
                        new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, pulse * 0.35f));
                }

                // 背景（墨纸 70%）
                InkRenderHelper.FillRoundedRectangle(rect, radius,
                    A(new Color(InkWashTheme.BaseTertiary.R, InkWashTheme.BaseTertiary.G, InkWashTheme.BaseTertiary.B, 0.70f)));

                // 技能字符（冷却中灰，正常亮）
                bool onCooldown = CooldownSeconds > 0f;
                Color glyphColor = A((IsActiveWeapon || IsUltimate) ? InkWashTheme.GoldBright
                    : onCooldown ? InkWashTheme.TextSecondary
                    : InkWashTheme.PaperBright);
                var glyphFont = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 24f).GetFont();
                if (glyphFont != null)
                {
                    Render2D.DrawText(glyphFont, Glyph, rect, glyphColor,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
                }

                // 冷却 conic 遮罩 + 倒计时
                if (onCooldown && MaxCooldownSeconds > 0f)
                {
                    float ratio = CooldownSeconds / MaxCooldownSeconds;
                    DrawCooldownSector(new Float2(Width * 0.5f, Height * 0.5f), Width * 0.5f, ratio,
                        new Color(0f, 0f, 0f, 0.78f));
                    var cdFont = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 14f).GetFont();
                    if (cdFont != null)
                    {
                        Render2D.DrawText(cdFont, $"{(int)Mathf.Ceil(CooldownSeconds)}s", rect,
                            InkWashTheme.PaperBright, TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
                    }
                }

                // 大招充能条（底部 3px）
                if (IsUltimate && ChargeRatio > 0f)
                {
                    float barH = 3f;
                    var barRect = new Rectangle(0, Height - barH, Width, barH);
                    Render2D.FillRectangle(barRect, new Color(0f, 0f, 0f, 0.6f));
                    float fillW = Width * ChargeRatio;
                    int strips = 8;
                    for (int i = 0; i < strips; i++)
                    {
                        float t = strips > 1 ? (float)i / (strips - 1) : 0f;
                        Color c = Color.Lerp(InkWashTheme.GoldDeep, InkWashTheme.GoldBright, t);
                        Render2D.FillRectangle(new Rectangle(fillW * i / strips, Height - barH, fillW / strips, barH), c);
                    }
                }

                // 边框（激活武器/大招 2px 金，普通 1px 淡金，悬停亮金）
                Color borderColor = A((IsActiveWeapon || IsUltimate) ? InkWashTheme.GoldPrimary
                    : _hovered ? InkWashTheme.GoldPrimary
                    : InkWashTheme.BorderGold);
                float thickness = (IsActiveWeapon || IsUltimate) ? 2f : 1f;
                InkRenderHelper.DrawRoundedRectangle(rect, radius, borderColor, thickness);

                // 快捷键标签（右下角）
                if (!string.IsNullOrEmpty(Hotkey))
                {
                    var keyFont = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 10f).GetFont();
                    if (keyFont != null)
                    {
                        Color keyColor = A(IsUltimate ? InkWashTheme.GoldPrimary : InkWashTheme.TextSecondary);
                        Render2D.DrawText(keyFont, Hotkey,
                            new Rectangle(0, 0, Width - 3f, Height - 2f), keyColor,
                            TextAlignment.Far, TextAlignment.Far, TextWrapping.NoWrap);
                    }
                }
            }

            /// <inheritdoc />
            public override void OnMouseEnter(Float2 location)
            {
                _hovered = true;
                base.OnMouseEnter(location);
            }

            /// <inheritdoc />
            public override void OnMouseLeave()
            {
                _hovered = false;
                base.OnMouseLeave();
            }

            /// <summary>绘制冷却扇形遮罩（从正上方顺时针）。</summary>
            private static void DrawCooldownSector(Float2 center, float radius, float progress, Color color)
            {
                if (progress <= 0f || radius <= 0f)
                    return;
                if (progress >= 1f)
                {
                    InkRenderHelper.FillCircle(center, radius, color);
                    return;
                }
                int segments = Mathf.Max(1, Mathf.CeilToInt(progress * SectorSegments));
                float startAngle = -Mathf.Pi * 0.5f;
                float totalAngle = progress * Mathf.TwoPi;
                var vertices = new Float2[segments * 3];
                for (int i = 0; i < segments; i++)
                {
                    float a1 = startAngle + (float)i / segments * totalAngle;
                    float a2 = startAngle + (float)(i + 1) / segments * totalAngle;
                    vertices[i * 3] = center;
                    vertices[i * 3 + 1] = center + new Float2(Mathf.Cos(a1) * radius, Mathf.Sin(a1) * radius);
                    vertices[i * 3 + 2] = center + new Float2(Mathf.Cos(a2) * radius, Mathf.Sin(a2) * radius);
                }
                Render2D.FillTriangles(vertices, color);
            }
        }
    }
}

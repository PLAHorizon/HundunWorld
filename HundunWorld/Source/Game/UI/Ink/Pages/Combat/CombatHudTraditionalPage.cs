using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Ink.Components;
using HundunWorld.Game.UI.StyleSystem;
using System;

namespace HundunWorld.Game.UI.Ink.Pages.Combat
{
    /// <summary>
    /// 传统模式战斗 HUD 页面 — 对应 combat-hud-traditional.html 设计原型。
    /// <para>
    /// 与沉浸式 <see cref="CombatHudPage"/> 并存，作为星型导航拓扑的另一个根节点。
    /// 传统模式提供更高密度的信息展示：
    /// <list type="bullet">
    ///   <item>顶部：角色名 + 等级 + HP/MP/SP 三条数值条（带数字标签）</item>
    ///   <item>左上：小地图 + 区域名 + 坐标</item>
    ///   <item>右上：任务追踪面板（3 条任务条目）</item>
    ///   <item>左下：8 格快捷栏（数字键 1-8）</item>
    ///   <item>右下：10 格完整技能槽（比沉浸式多 5 格）+ 奇术槽</item>
    ///   <item>底部中央：buff/debuff 条 + 主导航栏 + 扩展导航栏</item>
    ///   <item>主导航栏的"传统模式"按钮替换为"沉浸模式"按钮，点击返回 combat-hud</item>
    /// </list>
    /// 通过 <see cref="NavigationRequested"/> 事件向路由器暴露导航请求，
    /// 与 <see cref="CombatHudPage"/> 共用同一套 dom-id 导航契约。
    /// </para>
    /// </summary>
    public class CombatHudTraditionalPage : ContainerControl, IInkPage
    {
        // ===================================================================
        // 布局常量
        // =======================================================================

        /// <summary>顶部角色属性面板尺寸（角色名/等级/HP/MP/SP）</summary>
        private static readonly Float2 TopStatsPanelSize = new Float2(420f, 80f);

        /// <summary>左上角小地图尺寸（正方形）</summary>
        private const float MinimapSize = 160f;

        /// <summary>右上角任务追踪面板尺寸</summary>
        private static readonly Float2 QuestTrackerSize = new Float2(260f, 180f);

        /// <summary>左下角快捷栏尺寸（8 格 + 7 间距）</summary>
        private static readonly Float2 QuickBarSize = new Float2(360f, 44f);

        /// <summary>右下角完整技能栏尺寸（10 槽 + 1 奇术槽）</summary>
        private static readonly Float2 SkillBarSize = new Float2(560f, 64f);

        /// <summary>buff/debuff 图标条尺寸</summary>
        private static readonly Float2 BuffBarSize = new Float2(360f, 42f);

        /// <summary>主导航栏尺寸（与沉浸式一致，760x36）</summary>
        private static readonly Float2 SysNavSize = new Float2(760f, 36f);

        /// <summary>扩展导航栏尺寸（与沉浸式一致）</summary>
        private static readonly Float2 SysNavExtendedSize = new Float2(760f, 36f);

        /// <summary>导航行间距</summary>
        private const float SysNavRowGap = 4f;

        /// <summary>技能槽尺寸（正方形，略小于沉浸式以容纳更多槽）</summary>
        private const float SkillSlotSize = 48f;

        /// <summary>技能槽间距</summary>
        private const float SkillSlotGap = 6f;

        /// <summary>奇术槽尺寸（正方形）</summary>
        private const float QishuSlotSize = 60f;

        /// <summary>奇术槽与技能槽的间距</summary>
        private const float QishuSlotGap = 14f;

        /// <summary>快捷栏格子尺寸（正方形）</summary>
        private const float QuickSlotSize = 36f;

        /// <summary>快捷栏格子间距</summary>
        private const float QuickSlotGap = 4f;

        /// <summary>导航按钮宽度（统一）</summary>
        private const float NavBtnWidth = 70f;

        /// <summary>导航按钮宽度（传统/沉浸切换按钮，略宽）</summary>
        private const float ToggleBtnWidth = 80f;

        /// <summary>导航按钮间距</summary>
        private const float NavBtnGap = 4f;

        /// <summary>分隔符宽度</summary>
        private const float DividerWidth = 8f;

        /// <summary>传统模式切换按钮的金色强调背景</summary>
        private static readonly Color ToggleBg = new Color(
            InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G,
            InkWashTheme.GoldPrimary.B, 0.12f);

        // ===================================================================
        // 子控件引用
        // =======================================================================

        /// <summary>顶部角色属性面板</summary>
        private InkPanel _topStatsPanel;

        /// <summary>角色名标签</summary>
        private Label _characterNameLabel;

        /// <summary>角色等级标签</summary>
        private Label _levelLabel;

        /// <summary>HP 数值条</summary>
        private InkBar _hpBar;

        /// <summary>MP 数值条</summary>
        private InkBar _mpBar;

        /// <summary>SP 数值条</summary>
        private InkBar _spBar;

        /// <summary>HP 数值标签</summary>
        private Label _hpLabel;

        /// <summary>MP 数值标签</summary>
        private Label _mpLabel;

        /// <summary>SP 数值标签</summary>
        private Label _spLabel;

        /// <summary>左上角小地图</summary>
        private InkMinimap _minimap;

        /// <summary>小地图坐标标签</summary>
        private Label _minimapCoordLabel;

        /// <summary>右上角任务追踪面板</summary>
        private InkPanel _questTracker;

        /// <summary>左下角快捷栏</summary>
        private InkPanel _quickBar;

        /// <summary>右下角技能栏容器</summary>
        private ContainerControl _skillBar;

        /// <summary>10 个技能槽</summary>
        private InkCell[] _skillSlots;

        /// <summary>奇术槽</summary>
        private InkCell _qishuSlot;

        /// <summary>buff/debuff 条</summary>
        private InkPanel _buffBar;

        /// <summary>主导航栏</summary>
        private InkPanel _sysNav;

        /// <summary>扩展导航栏</summary>
        private InkPanel _sysNavExtended;

        // ===================================================================
        // 公共 API
        // =======================================================================

        /// <summary>
        /// 导航请求事件（与 <see cref="CombatHudPage.NavigationRequested"/> 同一契约）。
        /// </summary>
        public event Action<string> NavigationRequested;

        /// <summary>
        /// 粒子动效系统引用（可选，由 <see cref="MainUIManager"/> 注入）。
        /// </summary>
        public InkParticleSystem ParticleSystem { get; set; }

        // ===================================================================
        // 构造函数
        // =======================================================================

        /// <summary>
        /// 构造函数：初始化所有子控件。
        /// </summary>
        public CombatHudTraditionalPage()
        {
            try
            {
                AnchorPreset = AnchorPresets.StretchAll;
                Offsets = Margin.Zero;
                BackgroundColor = Color.Transparent;
                ClipChildren = false;
                AutoFocus = false;

                BuildTopStatsPanel();
                BuildMinimap();
                BuildQuestTracker();
                BuildQuickBar();
                BuildSkillBar();
                BuildBuffBar();
                BuildSystemNav();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[CombatHudTraditionalPage] 初始化失败: {ex.Message}");
            }
        }

        // ===================================================================
        // Build 方法
        // =======================================================================

        /// <summary>
        /// 构建顶部角色属性面板：角色名 + 等级 + HP/MP/SP 三条数值条。
        /// </summary>
        private void BuildTopStatsPanel()
        {
            _topStatsPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = TopStatsPanelSize,
            };

            // 角色名标签（左上）
            _characterNameLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 6f),
                Size = new Float2(180f, 20f),
                Text = "慕容凌霄",
                TextColor = InkWashTheme.TextDefault,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 16f),
                HorizontalAlignment = TextAlignment.Near,
            };
            _topStatsPanel.AddChild(_characterNameLabel);

            // 等级标签（右上）
            _levelLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopRight,
                Location = new Float2(TopStatsPanelSize.X - 80f, 6f),
                Size = new Float2(68f, 20f),
                Text = "Lv. 50",
                TextColor = InkWashTheme.TextGold,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 14f),
                HorizontalAlignment = TextAlignment.Far,
            };
            _topStatsPanel.AddChild(_levelLabel);

            // HP 条（左半）
            _hpBar = new InkBar
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 30f),
                Size = new Float2(190f, 12f),
                Value = 0.85f,
                FillVariant = InkBarFillVariant.Blood,
            };
            _topStatsPanel.AddChild(_hpBar);

            // HP 数值标签
            _hpLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 42f),
                Size = new Float2(190f, 14f),
                Text = "8500 / 10000",
                TextColor = InkWashTheme.TextSecondary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 11f),
                HorizontalAlignment = TextAlignment.Near,
            };
            _topStatsPanel.AddChild(_hpLabel);

            // MP 条（右半）
            _mpBar = new InkBar
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(218f, 30f),
                Size = new Float2(190f, 12f),
                Value = 0.72f,
                FillVariant = InkBarFillVariant.Jade,
            };
            _topStatsPanel.AddChild(_mpBar);

            // MP 数值标签
            _mpLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(218f, 42f),
                Size = new Float2(190f, 14f),
                Text = "3600 / 5000",
                TextColor = InkWashTheme.TextSecondary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 11f),
                HorizontalAlignment = TextAlignment.Near,
            };
            _topStatsPanel.AddChild(_mpLabel);

            // SP 条（体魄，下方）
            _spBar = new InkBar
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 60f),
                Size = new Float2(396f, 8f),
                Value = 0.60f,
                FillVariant = InkBarFillVariant.Gold,
            };
            _topStatsPanel.AddChild(_spBar);

            // SP 数值标签
            _spLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 68f),
                Size = new Float2(396f, 12f),
                Text = "体魄 600 / 1000",
                TextColor = InkWashTheme.TextTertiary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 10f),
                HorizontalAlignment = TextAlignment.Near,
            };
            _topStatsPanel.AddChild(_spLabel);

            AddChild(_topStatsPanel);
        }

        /// <summary>
        /// 构建左上角小地图 + 坐标标签。
        /// </summary>
        private void BuildMinimap()
        {
            _minimap = new InkMinimap
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(MinimapSize, MinimapSize),
            };
            AddChild(_minimap);

            _minimapCoordLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(MinimapSize, 16f),
                Text = "江南 · 姑苏城外",
                TextColor = InkWashTheme.TextSecondary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 11f),
                HorizontalAlignment = TextAlignment.Center,
            };
            AddChild(_minimapCoordLabel);
        }

        /// <summary>
        /// 构建右上角任务追踪面板（3 条任务条目）。
        /// </summary>
        private void BuildQuestTracker()
        {
            _questTracker = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = QuestTrackerSize,
            };

            // 标题
            var titleLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(10f, 6f),
                Size = new Float2(QuestTrackerSize.X - 20f, 18f),
                Text = "◆ 江湖任务",
                TextColor = InkWashTheme.TextGold,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 13f),
                HorizontalAlignment = TextAlignment.Near,
            };
            _questTracker.AddChild(titleLabel);

            // 3 条任务条目
            string[] questTexts =
            {
                "寻访江湖名士  3/10",
                "收集灵草材料  5/8",
                "击败山贼头目  0/1",
            };
            float yPos = 30f;
            foreach (var text in questTexts)
            {
                var questLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(14f, yPos),
                    Size = new Float2(QuestTrackerSize.X - 28f, 18f),
                    Text = "• " + text,
                    TextColor = InkWashTheme.TextSecondary,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 12f),
                    HorizontalAlignment = TextAlignment.Near,
                };
                _questTracker.AddChild(questLabel);
                yPos += 22f;
            }

            AddChild(_questTracker);
        }

        /// <summary>
        /// 构建左下角 8 格快捷栏（数字键 1-8）。
        /// </summary>
        private void BuildQuickBar()
        {
            _quickBar = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = QuickBarSize,
            };

            float cursorX = 0f;
            for (int i = 0; i < 8; i++)
            {
                var slot = new InkCell
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(cursorX, 4f),
                    Size = new Float2(QuickSlotSize, QuickSlotSize),
                };
                _quickBar.AddChild(slot);

                // 数字键标签
                var keyLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(cursorX + 2f, 4f),
                    Size = new Float2(12f, 12f),
                    Text = (i + 1).ToString(),
                    TextColor = InkWashTheme.TextGold,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 10f),
                    HorizontalAlignment = TextAlignment.Near,
                };
                _quickBar.AddChild(keyLabel);

                cursorX += QuickSlotSize + QuickSlotGap;
            }

            AddChild(_quickBar);
        }

        /// <summary>
        /// 构建右下角完整技能栏：10 个技能槽 + 1 个奇术槽。
        /// </summary>
        private void BuildSkillBar()
        {
            _skillBar = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = SkillBarSize,
            };

            _skillSlots = new InkCell[10];
            float cursorX = 0f;
            float slotY = (SkillBarSize.Y - SkillSlotSize) * 0.5f;

            for (int i = 0; i < 10; i++)
            {
                var slot = new InkCell
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(cursorX, slotY),
                    Size = new Float2(SkillSlotSize, SkillSlotSize),
                };
                _skillSlots[i] = slot;
                _skillBar.AddChild(slot);
                cursorX += SkillSlotSize + SkillSlotGap;
            }

            // 奇术槽（更宽间距 + 更大尺寸）
            cursorX += QishuSlotGap - SkillSlotGap;
            _qishuSlot = new InkCell
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(cursorX, (SkillBarSize.Y - QishuSlotSize) * 0.5f),
                Size = new Float2(QishuSlotSize, QishuSlotSize),
            };
            _skillBar.AddChild(_qishuSlot);

            AddChild(_skillBar);
        }

        /// <summary>
        /// 构建 buff/debuff 条（占位）。
        /// </summary>
        private void BuildBuffBar()
        {
            _buffBar = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = BuffBarSize,
            };
            AddChild(_buffBar);
        }

        /// <summary>
        /// 构建底部双行导航栏（与沉浸式布局一致，但"传统模式"按钮替换为"沉浸模式"）。
        /// </summary>
        private void BuildSystemNav()
        {
            // ========== 第一行：主导航栏 ==========
            _sysNav = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = SysNavSize,
            };

            var mainEntries = new[]
            {
                (label: "角色", domId: InkPageDomIds.NavCharacterPanel),
                (label: "武学", domId: InkPageDomIds.NavSkillPanel),
                (label: "背包", domId: InkPageDomIds.NavInventory),
                (label: "任务", domId: InkPageDomIds.NavQuests),
                (label: "地图", domId: InkPageDomIds.NavWorldMap),
                (label: "社交", domId: InkPageDomIds.NavFriends),
                (label: "商城", domId: InkPageDomIds.NavShop),
                (label: "罗盘", domId: InkPageDomIds.NavCompass),
            };

            float mainBtnWidth = (SysNavSize.X - NavBtnGap * 9 - DividerWidth - ToggleBtnWidth) / 8f;
            float mainBtnHeight = SysNavSize.Y - 4f;
            float mainBtnY = (SysNavSize.Y - mainBtnHeight) * 0.5f;

            float cursorX = 0f;
            for (int i = 0; i < mainEntries.Length; i++)
            {
                var entry = mainEntries[i];
                var btn = new InkButton
                {
                    Variant = InkButtonVariant.Default,
                    ButtonSize = InkButtonSize.Md,
                    Text = entry.label,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(cursorX, mainBtnY),
                    Size = new Float2(mainBtnWidth, mainBtnHeight),
                };

                string domId = entry.domId;
                btn.ButtonClicked += (b) => OnSystemNavButtonClicked(domId, b);

                _sysNav.AddChild(btn);
                cursorX += mainBtnWidth + NavBtnGap;
            }

            // 分隔符
            var divider = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(cursorX, (SysNavSize.Y - 20f) * 0.5f),
                Size = new Float2(1f, 20f),
                BackgroundColor = InkWashTheme.BorderNeutralL3,
            };
            _sysNav.AddChild(divider);
            cursorX += DividerWidth + NavBtnGap;

            // 沉浸模式切换按钮（金色强调背景，点击返回 combat-hud）
            var toggleBtn = new InkButton
            {
                Variant = InkButtonVariant.Default,
                ButtonSize = InkButtonSize.Md,
                Text = "沉浸模式",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(cursorX, mainBtnY),
                Size = new Float2(ToggleBtnWidth, mainBtnHeight),
                BackgroundColor = ToggleBg,
            };
            toggleBtn.ButtonClicked += (b) => OnSystemNavButtonClicked(InkPageDomIds.CombatHud, b);
            _sysNav.AddChild(toggleBtn);

            AddChild(_sysNav);

            // ========== 第二行：扩展导航栏 ==========
            _sysNavExtended = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = SysNavExtendedSize,
            };

            var extendedEntries = new[]
            {
                (label: "强化", domId: InkPageDomIds.NavEquipmentEnhance),
                (label: "制造", domId: InkPageDomIds.NavCrafting),
                (label: "坐骑", domId: InkPageDomIds.NavMountPet),
                (label: "好友", domId: InkPageDomIds.NavFriends),
                (label: "邮件", domId: InkPageDomIds.NavMail),
                (label: "排行", domId: InkPageDomIds.NavLeaderboard),
                (label: "师徒", domId: InkPageDomIds.NavMentor),
                (label: "成就", domId: InkPageDomIds.NavAchievement),
                (label: "副本", domId: InkPageDomIds.NavDungeonEntry),
            };

            float extBtnWidth = (SysNavExtendedSize.X - NavBtnGap * (extendedEntries.Length - 1)) / extendedEntries.Length;
            float extBtnHeight = SysNavExtendedSize.Y - 4f;
            float extBtnY = (SysNavExtendedSize.Y - extBtnHeight) * 0.5f;

            float cursorX2 = 0f;
            for (int i = 0; i < extendedEntries.Length; i++)
            {
                var entry = extendedEntries[i];
                var btn = new InkButton
                {
                    Variant = InkButtonVariant.Default,
                    ButtonSize = InkButtonSize.Md,
                    Text = entry.label,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(cursorX2, extBtnY),
                    Size = new Float2(extBtnWidth, extBtnHeight),
                };

                string domId = entry.domId;
                btn.ButtonClicked += (b) => OnSystemNavButtonClicked(domId, b);

                _sysNavExtended.AddChild(btn);
                cursorX2 += extBtnWidth + NavBtnGap;
            }

            AddChild(_sysNavExtended);
        }

        // ===================================================================
        // 事件处理
        // =======================================================================

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
                FlaxEngine.Debug.LogError($"[CombatHudTraditionalPage] NavigationRequested({domId}) 触发失败: {ex.Message}");
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
                FlaxEngine.Debug.LogWarning($"[CombatHudTraditionalPage] EmitGoldAtButton 失败: {ex.Message}");
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
                float screenEdge = 16f;

                // 顶部属性面板：左上角
                if (_topStatsPanel != null)
                {
                    _topStatsPanel.Location = new Float2(screenEdge, screenEdge);
                }

                // 小地图：右上角
                if (_minimap != null)
                {
                    _minimap.Location = new Float2(sw - MinimapSize - screenEdge, screenEdge);
                }

                // 小地图坐标标签：小地图下方
                if (_minimapCoordLabel != null)
                {
                    _minimapCoordLabel.Location = new Float2(
                        sw - MinimapSize - screenEdge,
                        screenEdge + MinimapSize + 4f);
                }

                // 任务追踪面板：左上角，属性面板下方
                if (_questTracker != null)
                {
                    _questTracker.Location = new Float2(
                        screenEdge,
                        screenEdge + TopStatsPanelSize.Y + 8f);
                }

                // 快捷栏：左下角
                if (_quickBar != null)
                {
                    _quickBar.Location = new Float2(screenEdge, sh - 50f - QuickBarSize.Y);
                }

                // 技能栏：右下角，导航栏上方
                if (_skillBar != null)
                {
                    _skillBar.Location = new Float2(
                        sw - SkillBarSize.X - screenEdge,
                        sh - 50f - SysNavExtendedSize.Y - SysNavRowGap - SkillBarSize.Y - 8f);
                }

                // buff 条：技能栏左侧
                if (_buffBar != null)
                {
                    _buffBar.Location = new Float2(
                        sw * 0.5f - BuffBarSize.X * 0.5f,
                        sh - 50f - SysNavExtendedSize.Y - SysNavRowGap - BuffBarSize.Y - 8f);
                }

                // 主导航栏：底部居中
                if (_sysNav != null)
                {
                    _sysNav.Location = new Float2(sw * 0.5f - SysNavSize.X * 0.5f, sh - 50f);
                }

                // 扩展导航栏：主导航栏上方
                if (_sysNavExtended != null)
                {
                    _sysNavExtended.Location = new Float2(
                        sw * 0.5f - SysNavExtendedSize.X * 0.5f,
                        sh - 50f - SysNavExtendedSize.Y - SysNavRowGap);
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[CombatHudTraditionalPage] RefreshLayout 失败: {ex.Message}");
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

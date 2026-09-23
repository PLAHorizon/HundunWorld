using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.StyleSystem;
using System;
using System.Collections.Generic;

namespace HundunWorld.Game.UI.Ink.Pages
{
    /// <summary>
    /// 任务菜单页面。
    /// 设计稿：hundun-yy-ui/pages/menu-quests.html（燕云融合 · 深墨黑 + 鎏金 + 纸色卷轴）。
    /// 结构：
    ///   左侧 240px 导航栏（竖排「任务」+ 角色养成/功能入口/设置 三组导航）
    ///   ＋ 顶部 60px 角色信息栏（头像 / 无名侠 Lv.42 逍遥派 / 铜钱 8,320 / 银两 328）
    ///   ＋ 任务卷宗列表（左 40%：标题「任务卷宗 6 项」+ 主线/支线/世界/日常 标签页 + 任务卡）
    ///   ＋ 任务详情（右 60%：标题 + 状态标签 + 推荐等级/难度/类型 + 任务背景纸卷 + 任务目标 + 任务奖励
    ///      + 委托人/区域/时限 + 追踪/放弃/分享 操作按钮）。
    /// 交互：导航跳转（博物志）、标签页筛选、任务选择、追踪/放弃/分享；
    ///       返回战斗 HUD 由页面外壳返回按钮承担（设计稿左上 back-hud 出口）。
    /// </summary>
    public class MenuQuestsPage : ContainerControl, IInkPage
    {
        // ───────────────────────── 数据模型 ─────────────────────────

        /// <summary>任务数据（对应设计稿卷宗列表项 + 右侧详情面板）。</summary>
        private class QuestData
        {
            public string Name;
            /// <summary>任务类型：主线/支线/世界/日常（对应设计稿标签页）。</summary>
            public string Category;
            /// <summary>任务状态：进行中/未开始/已完成。</summary>
            public string Status;
            /// <summary>推荐等级，如 Lv.42。</summary>
            public string Level;
            /// <summary>列表进度百分比（0~100）。</summary>
            public int Progress;
            /// <summary>难度星级（★★★★★）。</summary>
            public string Difficulty;
            /// <summary>委托人。</summary>
            public string Npc;
            /// <summary>区域。</summary>
            public string Region;
            /// <summary>时限。</summary>
            public string TimeLimit;
            /// <summary>任务背景（纸色卷轴正文）。</summary>
            public string Background;
            public string[] Objectives;
            public bool[] ObjectiveDone;
            public RewardData[] Rewards;
        }

        /// <summary>任务奖励项（经验 / 铜钱 / 装备 / 道具）。</summary>
        private class RewardData
        {
            public string Label;
            public string Value;
            public Color ValueColor;
            public Color IconColor;
            /// <summary>传世品质奖励（设计稿 .reward-legendary 朱红描边）。</summary>
            public bool Legendary;
        }

        // ───────────────────────── 布局常量 ─────────────────────────

        /// <summary>左侧导航栏宽度（设计稿 240px）。</summary>
        private const float SidebarWidth = 240f;

        /// <summary>顶部角色信息栏高度（设计稿 60px）。</summary>
        private const float TopHeaderHeight = 60f;

        /// <summary>任务卷宗列表占内容区宽度比例（设计稿 lg:w-2/5）。</summary>
        private const float ListRatio = 0.4f;

        /// <summary>详情面板内边距（设计稿 --ink-space-6）。</summary>
        private const float DetailPadding = 32f;

        /// <summary>卷宗列表项高度。</summary>
        private const float QuestItemHeight = 84f;

        /// <summary>奖励卡高度。</summary>
        private const float RewardCardHeight = 84f;

        /// <summary>导航项高度（设计稿 padding 10px + 15px 字）。</summary>
        private const float NavItemHeight = 40f;

        /// <summary>标签页高度（含 2px 底部指示线）。</summary>
        private const float TabHeight = 42f;

        /// <summary>设计稿标签页：主线 / 支线 / 世界 / 日常。</summary>
        private static readonly string[] TabNames = { "主线", "支线", "世界", "日常" };

        /// <summary>设计稿左侧导航项（按设计稿顺序，含禁用项）。</summary>
        private static readonly string[] NavNames =
        {
            "角色", "装备", "外观", "备战", "门派", "个人信息", "时间",
            "任务", "博物志", "武林录", "营生", "组队", "邮箱", "商店",
            "设置"
        };

        /// <summary>导航项跳转目标（null = 设计稿 aria-disabled 的禁用项，不响应点击）。</summary>
        private static readonly string[] NavTargets =
        {
            null, null, null, null, null, null, null,
            "nav-quests", "nav-bestiary", null, null, null, null, null,
            null
        };

        /// <summary>当前所在导航项索引（设计稿 nav-item-active：任务）。</summary>
        private const int NavActiveIndex = 7;

        /// <summary>导航分隔线插入位置（该索引之前插入一条 .ink-divider）。</summary>
        private static readonly int[] NavDividerBefore = { 7, 14 };

        /// <summary>设计稿默认选中任务（卷宗第 6 项「百年恩怨」）。</summary>
        private const int DefaultSelectedQuest = 5;

        // ───────────────────────── 字段 ─────────────────────────

        private Float2 _screenSize;

        // 氛围层
        private Panel _backgroundLayer;
        private Panel _vignette;

        // 左侧导航栏
        private Panel _sidebar;
        private Panel _sidebarTitleBlock;
        private InkVerticalTitle _sidebarTitle;
        private Panel _sidebarNavPanel;
        private List<TextRowButton> _navButtons;
        private List<Control> _navRows;

        // 顶部角色信息栏
        private Panel _mainArea;
        private Panel _topHeader;
        private CircleAvatar _playerAvatar;
        private Label _playerNameLabel;
        private InkTag _playerLevelTag;
        private Label _playerSectLabel;
        private Label _copperTitleLabel;
        private Label _copperValueLabel;
        private Label _silverTitleLabel;
        private Label _silverValueLabel;

        // 任务卷宗列表
        private Panel _listSection;
        private Label _listTitleLabel;
        private Label _listCountLabel;
        private Panel _tabBar;
        private List<TextRowButton> _tabs;
        private Panel _listScroll;
        private Panel _listContent;
        private List<QuestListItem> _questItems;
        private Label _listEmptyHint;

        // 任务详情
        private Panel _detailSection;
        private Panel _detailScroll;
        private Panel _detailContent;
        private Label _detailTitleLabel;
        private QuestTag _detailStatusTag;
        private Label _detailLevelPrefix;
        private Label _detailLevelValue;
        private Label _detailDifficultyPrefix;
        private Label _detailDifficultyValue;
        private Label _detailTypePrefix;
        private Label _detailTypeValue;
        private InkDivider _detailDividerMeta;
        private InkPaperPanel _detailBackgroundPanel;
        private Label _detailBackgroundTitle;
        private Label _detailBackgroundText;
        private InkDivider _detailDividerBackground;
        private Label _detailObjectiveTitle;
        private List<ObjectiveRow> _detailObjectiveRows;
        private InkDivider _detailDividerObjective;
        private Label _detailRewardTitle;
        private List<RewardCard> _detailRewardCards;
        private InkDivider _detailDividerReward;
        private Label _detailNpcPrefix;
        private Label _detailNpcValue;
        private Label _detailRegionPrefix;
        private Label _detailRegionValue;
        private Label _detailTimePrefix;
        private Label _detailTimeValue;
        private InkDivider _detailDividerInfo;
        private InkButton _trackBtn;
        private InkButton _abandonBtn;
        private InkButton _shareBtn;
        private Label _actionHintLabel;

        // 状态
        private List<QuestData> _allQuests;
        private List<QuestData> _visibleQuests;
        private QuestData _selectedQuest;
        private QuestData _trackedQuest;
        private int _selectedTab;

        /// <summary>页面导航请求（由 MainUIManager 通过 HookNavigationRequested 订阅）。</summary>
        public event Action<string> NavigationRequested;

        // ───────────────────────── 私有控件 ─────────────────────────

        /// <summary>
        /// 纯文本行按钮，对应设计稿 .nav-item（左对齐 + 2px 金竖线激活态）
        /// 与 .quest-tab（居中 + 2px 金底边激活态）。
        /// 自绘背景与激活指示线，避免依赖引擎 Button 的固定居中排版。
        /// </summary>
        private class TextRowButton : ContainerControl
        {
            /// <summary>激活指示线宽/高（设计稿 2px）。</summary>
            private const float ActiveBarSize = 2f;

            private readonly Label _label;
            private readonly float _textLeft;
            private readonly bool _bottomBar;
            private bool _active;
            private bool _hovered;
            private bool _pressed;

            public event Action Clicked;

            /// <summary>是否处于激活态（当前页 / 当前标签页）。</summary>
            public bool Active
            {
                get => _active;
                set
                {
                    _active = value;
                    ApplyColors();
                }
            }

            /// <summary>行文案。</summary>
            public string Text
            {
                get => _label.Text;
                set => _label.Text = value;
            }

            public TextRowButton(string text, float textLeft, InkWashTheme.FontRole fontRole, float fontSize, bool bottomBar)
            {
                _textLeft = textLeft;
                _bottomBar = bottomBar;
                BackgroundColor = Color.Transparent;
                ClipChildren = false;

                _label = new Label
                {
                    Text = text,
                    Font = InkRenderHelper.GetFontRef(fontRole, fontSize),
                    TextColor = InkWashTheme.PaperAged,
                    HorizontalAlignment = bottomBar ? TextAlignment.Center : TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                    Parent = this
                };

                ApplyColors();
            }

            /// <summary>设置文字字体（标签页激活态切换为标题字体）。</summary>
            public void SetFont(InkWashTheme.FontRole fontRole, float fontSize)
            {
                _label.Font = InkRenderHelper.GetFontRef(fontRole, fontSize);
            }

            /// <summary>设置禁用态配色（设计稿 .nav-item-disabled）。</summary>
            public void SetDisabled()
            {
                Enabled = false;
                _label.TextColor = InkWashTheme.TextTertiary;
            }

            private void ApplyColors()
            {
                _label.TextColor = _active ? InkWashTheme.GoldBright : InkWashTheme.PaperAged;
            }

            protected override void OnSizeChanged()
            {
                base.OnSizeChanged();

                if (_label != null)
                {
                    _label.Location = new Float2(_textLeft, 0f);
                    _label.Size = new Float2(Mathf.Max(0f, Width - _textLeft), Height);
                }
            }

            public override void OnMouseEnter(Float2 location)
            {
                base.OnMouseEnter(location);
                _hovered = true;
            }

            public override void OnMouseLeave()
            {
                base.OnMouseLeave();
                _hovered = false;
                _pressed = false;
            }

            public override bool OnMouseDown(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left)
                {
                    _pressed = true;
                    return true;
                }
                return base.OnMouseDown(location, button);
            }

            public override bool OnMouseUp(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left && _pressed)
                {
                    _pressed = false;
                    if (ContainsPoint(ref location))
                        Clicked?.Invoke();
                    return true;
                }
                return base.OnMouseUp(location, button);
            }

            public override void Draw()
            {
                if (!Visible || Width <= 0f || Height <= 0f)
                    return;

                if (_active)
                    Render2D.FillRectangle(new Rectangle(0f, 0f, Width, Height), WithAlpha(InkWashTheme.GoldPrimary, 0.1f));
                else if (_hovered)
                    Render2D.FillRectangle(new Rectangle(0f, 0f, Width, Height), WithAlpha(InkWashTheme.GoldPrimary, 0.06f));

                base.Draw();

                if (_active)
                {
                    var bar = _bottomBar
                        ? new Rectangle(0f, Height - ActiveBarSize, Width, ActiveBarSize)
                        : new Rectangle(0f, 0f, ActiveBarSize, Height);
                    Render2D.FillRectangle(bar, InkWashTheme.GoldPrimary);
                }
            }
        }

        /// <summary>
        /// 圆形玩家头像（设计稿 header：朱红圆底 + 金环 + 姓氏字）。
        /// 先绘制圆形底，再交由基类绘制子控件（姓氏字），保证文字压在圆底之上。
        /// </summary>
        private class CircleAvatar : ContainerControl
        {
            public CircleAvatar(string initial)
            {
                BackgroundColor = Color.Transparent;
                ClipChildren = false;

                new Label
                {
                    Text = initial,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 18f),
                    TextColor = InkWashTheme.PaperBright,
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.StretchAll,
                    Parent = this
                };
            }

            public override void Draw()
            {
                if (Visible && Width > 0f && Height > 0f)
                {
                    var center = new Float2(Width * 0.5f, Height * 0.5f);
                    float radius = Mathf.Min(Width, Height) * 0.5f - 1f;
                    InkRenderHelper.FillCircle(center, radius, InkWashTheme.VermilionPrimary);
                    InkRenderHelper.DrawCircle(center, radius, InkWashTheme.BorderGold, 1f);
                }

                base.Draw();
            }
        }

        /// <summary>
        /// 任务状态标签（设计稿 .ink-tag / .ink-tag-brand / .quest-tag-done / .ink-tag-vermilion）。
        /// 进行中=鎏金、未开始=中性、已完成=翡翠（.quest-tag-done）、详情标题=朱红强调。
        /// </summary>
        private class QuestTag : Label
        {
            private Color _borderColor = InkWashTheme.BorderNeutralL2;

            public QuestTag()
            {
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f);
                HorizontalAlignment = TextAlignment.Center;
                VerticalAlignment = TextAlignment.Center;
                ApplyStatus("未开始", false);
            }

            /// <summary>按任务状态刷新标签配色。</summary>
            public void ApplyStatus(string status, bool highlight)
            {
                if (highlight)
                {
                    BackgroundColor = WithAlpha(InkWashTheme.VermilionPrimary, 0.12f);
                    _borderColor = InkWashTheme.BorderVermilion;
                    TextColor = InkWashTheme.TextVermilion;
                }
                else if (status == "已完成")
                {
                    BackgroundColor = WithAlpha(InkWashTheme.JadePrimary, 0.12f);
                    _borderColor = InkWashTheme.BorderJade;
                    TextColor = InkWashTheme.JadeBright;
                }
                else if (status == "进行中")
                {
                    BackgroundColor = WithAlpha(InkWashTheme.GoldPrimary, 0.12f);
                    _borderColor = InkWashTheme.BorderGold;
                    TextColor = InkWashTheme.TextBrand;
                }
                else
                {
                    BackgroundColor = InkWashTheme.GoldTrace;
                    _borderColor = InkWashTheme.BorderNeutralL2;
                    TextColor = InkWashTheme.TextSecondary;
                }
            }

            public override void Draw()
            {
                base.Draw();

                if (_borderColor.A > 0f && Width > 0f && Height > 0f)
                {
                    Render2D.DrawRectangle(new Rectangle(0f, 0f, Width, Height), _borderColor, 1f);
                }
            }
        }

        /// <summary>
        /// 任务卷宗列表项（设计稿 .quest-item）：
        /// 任务名 + 等级 + 状态标签 + 细进度条 + 百分比；选中态为金竖线 + 微金底。
        /// </summary>
        private class QuestListItem : ContainerControl
        {
            public readonly QuestData Quest;
            public readonly Label NameLabel;
            public readonly Label LevelLabel;
            public readonly QuestTag StatusTag;
            public readonly InkBar ProgressBar;
            public readonly Label PercentLabel;

            private readonly Panel _selectionAccent;

            public event Action Clicked;

            public QuestListItem(QuestData quest)
            {
                Quest = quest;
                ClipChildren = false;
                BackgroundColor = WithAlpha(InkWashTheme.BaseTertiary, 0.6f);

                bool done = quest.Status == "已完成";

                _selectionAccent = new Panel
                {
                    BackgroundColor = InkWashTheme.GoldPrimary,
                    Visible = false,
                    Parent = this
                };

                NameLabel = new Label
                {
                    Text = quest.Name,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 15f),
                    TextColor = done ? InkWashTheme.PaperAged : InkWashTheme.PaperBright,
                    VerticalAlignment = TextAlignment.Center,
                    Parent = this
                };

                LevelLabel = new Label
                {
                    Text = quest.Level,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 13f),
                    TextColor = InkWashTheme.PaperAged,
                    HorizontalAlignment = TextAlignment.Far,
                    VerticalAlignment = TextAlignment.Center,
                    Parent = this
                };

                StatusTag = new QuestTag
                {
                    Text = quest.Status,
                    Parent = this
                };
                StatusTag.ApplyStatus(quest.Status, false);

                ProgressBar = new InkBar
                {
                    Value = Mathf.Clamp(quest.Progress / 100f, 0f, 1f),
                    FillVariant = done ? InkBarFillVariant.Jade : InkBarFillVariant.Gold,
                    Parent = this
                };

                PercentLabel = new Label
                {
                    Text = quest.Progress + "%",
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 11f),
                    TextColor = done ? InkWashTheme.JadeBright : InkWashTheme.PaperAged,
                    HorizontalAlignment = TextAlignment.Far,
                    VerticalAlignment = TextAlignment.Center,
                    Parent = this
                };
            }

            /// <summary>切换选中态（设计稿 .quest-item-selected）。</summary>
            public void SetSelected(bool selected)
            {
                BackgroundColor = selected
                    ? WithAlpha(InkWashTheme.GoldPrimary, 0.08f)
                    : WithAlpha(InkWashTheme.BaseTertiary, 0.6f);

                _selectionAccent.Visible = selected;

                LevelLabel.TextColor = selected ? InkWashTheme.GoldBright : InkWashTheme.PaperAged;
                PercentLabel.TextColor = Quest.Status == "已完成"
                    ? InkWashTheme.JadeBright
                    : selected ? InkWashTheme.GoldBright : InkWashTheme.PaperAged;
            }

            /// <summary>布局内部元素（由页面布局统一调用）。</summary>
            public void LayoutContent()
            {
                _selectionAccent.Location = Float2.Zero;
                _selectionAccent.Size = new Float2(2f, Height);

                NameLabel.Location = new Float2(16f, 12f);
                NameLabel.Size = new Float2(Mathf.Max(0f, Width - 160f), 22f);

                LevelLabel.Location = new Float2(Width - 116f, 14f);
                LevelLabel.Size = new Float2(44f, 18f);

                StatusTag.Location = new Float2(Width - 64f, 12f);
                StatusTag.Size = new Float2(52f, 22f);

                ProgressBar.Location = new Float2(16f, 52f);
                ProgressBar.Size = new Float2(Mathf.Max(0f, Width - 104f), 4f);

                PercentLabel.Location = new Float2(Width - 96f, 44f);
                PercentLabel.Size = new Float2(84f, 18f);
            }

            public override bool OnMouseUp(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left)
                    Clicked?.Invoke();
                return base.OnMouseUp(location, button);
            }
        }

        /// <summary>
        /// 圆形勾选标记（设计稿 .quest-check / .quest-check-done / .quest-check-pending）。
        /// 已完成：翡翠实心圆 + 纸色勾；未完成：中性金细圆环。
        /// </summary>
        private class QuestCheckMark : ContainerControl
        {
            private readonly bool _done;

            public QuestCheckMark(bool done)
            {
                _done = done;
                BackgroundColor = Color.Transparent;
                ClipChildren = false;
            }

            public override void Draw()
            {
                base.Draw();

                if (!Visible || Width <= 0f || Height <= 0f)
                    return;

                var center = new Float2(Width * 0.5f, Height * 0.5f);
                float radius = Mathf.Min(Width, Height) * 0.5f - 1f;

                if (_done)
                {
                    InkRenderHelper.FillCircle(center, radius, InkWashTheme.JadeDeep);
                    InkRenderHelper.DrawCircle(center, radius, InkWashTheme.JadeBright, 1f);

                    var p1 = center + new Float2(-radius * 0.42f, 0f);
                    var p2 = center + new Float2(-radius * 0.10f, radius * 0.40f);
                    var p3 = center + new Float2(radius * 0.45f, -radius * 0.38f);
                    Render2D.DrawLine(p1, p2, InkWashTheme.PaperBright, 1.6f);
                    Render2D.DrawLine(p2, p3, InkWashTheme.PaperBright, 1.6f);
                }
                else
                {
                    InkRenderHelper.DrawCircle(center, radius, InkWashTheme.BorderNeutralL3, 1f);
                }
            }
        }

        /// <summary>任务目标行（设计稿：圆形勾选 + 目标文案）。</summary>
        private class ObjectiveRow : ContainerControl
        {
            public ObjectiveRow(string text, bool done)
            {
                ClipChildren = false;

                new QuestCheckMark(done)
                {
                    Parent = this
                };

                new Label
                {
                    Text = text,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                    TextColor = done ? InkWashTheme.PaperAged : InkWashTheme.PaperBright,
                    VerticalAlignment = TextAlignment.Center,
                    Parent = this
                };
            }

            /// <summary>布局内部元素（圆形勾选 + 文案）。</summary>
            public void LayoutContent()
            {
                foreach (var child in Children)
                {
                    if (child is QuestCheckMark)
                    {
                        child.Location = new Float2(0f, (Height - 20f) * 0.5f);
                        child.Size = new Float2(20f, 20f);
                    }
                    else if (child is Label)
                    {
                        child.Location = new Float2(32f, 0f);
                        child.Size = new Float2(Mathf.Max(0f, Width - 32f), Height);
                    }
                }
            }
        }

        /// <summary>任务奖励卡（设计稿 .reward-item / .reward-legendary）。</summary>
        private class RewardCard : ContainerControl
        {
            private readonly Color _borderColor;
            private readonly Label _iconLabel;
            private readonly Label _typeLabel;
            private readonly Label _valueLabel;

            public RewardCard(RewardData reward)
            {
                ClipChildren = false;
                BackgroundColor = reward.Legendary
                    ? WithAlpha(InkWashTheme.VermilionPrimary, 0.06f)
                    : WithAlpha(InkWashTheme.Abyss, 0.5f);
                _borderColor = reward.Legendary
                    ? InkWashTheme.BorderVermilion
                    : InkWashTheme.BorderNeutralL2;

                _iconLabel = new Label
                {
                    Text = RewardIcon(reward.Label),
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 16f),
                    TextColor = reward.IconColor,
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                    Parent = this
                };

                _typeLabel = new Label
                {
                    Text = reward.Label,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                    TextColor = InkWashTheme.PaperAged,
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                    Parent = this
                };

                _valueLabel = new Label
                {
                    Text = reward.Value,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 13f),
                    TextColor = reward.ValueColor,
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                    Parent = this
                };
            }

            /// <summary>布局内部元素（图标 / 类型 / 数值 三行居中）。</summary>
            public void LayoutContent()
            {
                _iconLabel.Location = new Float2(0f, 10f);
                _iconLabel.Size = new Float2(Width, 22f);

                _typeLabel.Location = new Float2(0f, 40f);
                _typeLabel.Size = new Float2(Width, 18f);

                _valueLabel.Location = new Float2(0f, 58f);
                _valueLabel.Size = new Float2(Width, 20f);
            }

            public override void Draw()
            {
                base.Draw();

                if (Width > 0f && Height > 0f)
                {
                    Render2D.DrawRectangle(new Rectangle(0f, 0f, Width, Height), _borderColor, 1f);
                }
            }
        }

        // ───────────────────────── 构造 ─────────────────────────

        public MenuQuestsPage()
        {
            _screenSize = FlaxEngine.Screen.Size;
            if (_screenSize.X <= 0f || _screenSize.Y <= 0f)
                _screenSize = new Float2(1920f, 1080f);

            AnchorPreset = AnchorPresets.StretchAll;
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
            AutoFocus = false;
            Location = Float2.Zero;
            Size = _screenSize;

            InitData();

            try
            {
                BuildAtmosphere();
                BuildSidebar();
                BuildMainArea();
                BuildTopHeader();
                BuildQuestList();
                BuildQuestDetail();

                ApplyLayout();
                SelectTab(0);
                SelectQuest(_allQuests.Count > DefaultSelectedQuest ? _allQuests[DefaultSelectedQuest] : null);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[MenuQuestsPage] 初始化失败: {ex.Message}");
            }
        }

        /// <summary>设计稿静态数据（任务卷宗 6 项 + 百年恩怨详情）。</summary>
        private void InitData()
        {
            _visibleQuests = new List<QuestData>();
            _questItems = new List<QuestListItem>();
            _detailObjectiveRows = new List<ObjectiveRow>();
            _detailRewardCards = new List<RewardCard>();

            _allQuests = new List<QuestData>
            {
                new QuestData
                {
                    Name = "初入江湖", Category = "主线", Status = "进行中", Level = "Lv.1", Progress = 35,
                    Difficulty = "★☆☆☆☆", Npc = "王铁匠", Region = "洛阳城东", TimeLimit = "无",
                    Background = "你初入江湖，落脚洛阳城东。老铁匠王铁匠见你面生却筋骨清奇，指点你先去茶馆打探江湖近况，再决定拜入何门何派。城中近日山贼出没，也是磨炼身手的好去处。",
                    Objectives = new[] { "前往洛阳城东茶馆，打探江湖近况", "与老铁匠王铁匠交谈", "击败城外山贼 ×5" },
                    ObjectiveDone = new[] { true, true, false },
                    Rewards = new[]
                    {
                        new RewardData { Label = "经验", Value = "5,000", ValueColor = InkWashTheme.PaperBright, IconColor = InkWashTheme.GoldPrimary },
                        new RewardData { Label = "铜钱", Value = "200", ValueColor = InkWashTheme.PaperBright, IconColor = InkWashTheme.PaperAged },
                        new RewardData { Label = "道具", Value = "行囊布包×1", ValueColor = InkWashTheme.PaperBright, IconColor = InkWashTheme.JadeBright }
                    }
                },
                new QuestData
                {
                    Name = "逍遥秘境", Category = "主线", Status = "进行中", Level = "Lv.15", Progress = 50,
                    Difficulty = "★★★☆☆", Npc = "逍遥派掌门", Region = "逍遥谷", TimeLimit = "无",
                    Background = "逍遥谷深处藏有一处秘境，传闻其中有前人留下的武学残卷。掌门命你携令牌入内探查，秘境之中机关重重，需步步为营。",
                    Objectives = new[] { "前往逍遥谷入口", "破解秘境机关", "取得武学残卷" },
                    ObjectiveDone = new[] { true, false, false },
                    Rewards = new[]
                    {
                        new RewardData { Label = "经验", Value = "8,000", ValueColor = InkWashTheme.PaperBright, IconColor = InkWashTheme.GoldPrimary },
                        new RewardData { Label = "铜钱", Value = "300", ValueColor = InkWashTheme.PaperBright, IconColor = InkWashTheme.PaperAged },
                        new RewardData { Label = "装备", Value = "逍遥玉佩", ValueColor = InkWashTheme.PaperBright, IconColor = InkWashTheme.VermilionBright }
                    }
                },
                new QuestData
                {
                    Name = "武林盟主", Category = "主线", Status = "未开始", Level = "Lv.40", Progress = 0,
                    Difficulty = "★★★★★", Npc = "武林盟长老", Region = "嵩山之巅", TimeLimit = "无",
                    Background = "武林大会将启，各路豪杰齐聚嵩山。盟主之位空缺已久，唯有技压群雄者方可登顶。你若有意，需先取得三位长老的举荐。",
                    Objectives = new[] { "取得三位长老的举荐", "在武林大会上连胜三场", "登上嵩山之巅接受盟主印" },
                    ObjectiveDone = new[] { false, false, false },
                    Rewards = new[]
                    {
                        new RewardData { Label = "经验", Value = "30,000", ValueColor = InkWashTheme.PaperBright, IconColor = InkWashTheme.GoldPrimary },
                        new RewardData { Label = "铜钱", Value = "2,000", ValueColor = InkWashTheme.PaperBright, IconColor = InkWashTheme.PaperAged },
                        new RewardData { Label = "装备", Value = "盟主印", ValueColor = InkWashTheme.VermilionBright, IconColor = InkWashTheme.VermilionBright, Legendary = true }
                    }
                },
                new QuestData
                {
                    Name = "天山雪莲", Category = "主线", Status = "进行中", Level = "Lv.35", Progress = 75,
                    Difficulty = "★★★★☆", Npc = "药王谷医者", Region = "天山 · 冰崖", TimeLimit = "无",
                    Background = "药王谷医者所配的解毒方尚缺一味天山雪莲。雪莲生于天山冰崖绝壁之上，采之须趁日出前的瞬息，迟则冰化花谢。",
                    Objectives = new[] { "前往天山冰崖", "在日出前寻得雪莲", "护送雪莲返回药王谷" },
                    ObjectiveDone = new[] { true, true, false },
                    Rewards = new[]
                    {
                        new RewardData { Label = "经验", Value = "18,000", ValueColor = InkWashTheme.PaperBright, IconColor = InkWashTheme.GoldPrimary },
                        new RewardData { Label = "铜钱", Value = "1,200", ValueColor = InkWashTheme.PaperBright, IconColor = InkWashTheme.PaperAged },
                        new RewardData { Label = "道具", Value = "天山雪莲×1", ValueColor = InkWashTheme.PaperBright, IconColor = InkWashTheme.JadeBright }
                    }
                },
                new QuestData
                {
                    Name = "暗影追踪", Category = "主线", Status = "已完成", Level = "Lv.28", Progress = 100,
                    Difficulty = "★★★☆☆", Npc = "六扇门捕头", Region = "洛阳城东 · 漕渠", TimeLimit = "无",
                    Background = "漕渠沿岸接连失踪数名商旅，六扇门查无头绪。你沿暗影一路追踪，终于在废弃盐仓中揭出主谋，将这桩悬案了结。",
                    Objectives = new[] { "调查漕渠失踪案", "追踪黑衣人踪迹", "在盐仓中擒下主谋" },
                    ObjectiveDone = new[] { true, true, true },
                    Rewards = new[]
                    {
                        new RewardData { Label = "经验", Value = "15,000", ValueColor = InkWashTheme.PaperBright, IconColor = InkWashTheme.GoldPrimary },
                        new RewardData { Label = "铜钱", Value = "900", ValueColor = InkWashTheme.PaperBright, IconColor = InkWashTheme.PaperAged },
                        new RewardData { Label = "装备", Value = "影踪短刃", ValueColor = InkWashTheme.PaperBright, IconColor = InkWashTheme.VermilionBright }
                    }
                },
                new QuestData
                {
                    Name = "百年恩怨", Category = "主线", Status = "进行中", Level = "Lv.42", Progress = 80,
                    Difficulty = "★★★★★", Npc = "李沧海", Region = "洛阳城东 · 太行山巅", TimeLimit = "无",
                    Background = "百年前，江湖中一场惊天动地的恩怨纠葛埋下祸根。如今你无意间得到一封泛黄的血书，揭开了一段尘封已久的往事。李沧海老人在洛阳城东的茶馆中苦苦等待有缘人，他将告诉你一段关于师门血仇的真相。太行山巅风雪依旧，仇敌赵无极盘踞已久，而你将成为这段百年恩怨的终结者。",
                    Objectives = new[]
                    {
                        "前往洛阳城东茶馆，寻找老者李沧海",
                        "聆听血书往事，了解百年前师门恩怨",
                        "前往太行山巅，击败仇敌赵无极"
                    },
                    ObjectiveDone = new[] { true, true, false },
                    Rewards = new[]
                    {
                        new RewardData { Label = "经验", Value = "12,000", ValueColor = InkWashTheme.PaperBright, IconColor = InkWashTheme.GoldPrimary },
                        new RewardData { Label = "铜钱", Value = "5,000", ValueColor = InkWashTheme.PaperBright, IconColor = InkWashTheme.PaperAged },
                        new RewardData { Label = "装备", Value = "沧海剑", ValueColor = InkWashTheme.VermilionBright, IconColor = InkWashTheme.VermilionBright, Legendary = true },
                        new RewardData { Label = "道具", Value = "血书残页×3", ValueColor = InkWashTheme.PaperBright, IconColor = InkWashTheme.JadeBright }
                    }
                }
            };
        }

        // ───────────────────────── 构建 ─────────────────────────

        /// <summary>深墨底 + 暗角晕影（设计稿 .quest-bg-layer / .ink-vignette）。</summary>
        private void BuildAtmosphere()
        {
            _backgroundLayer = new Panel
            {
                AnchorPreset = AnchorPresets.StretchAll,
                BackgroundColor = InkWashTheme.BaseDefault,
                Parent = this
            };

            _vignette = new Panel
            {
                AnchorPreset = AnchorPresets.StretchAll,
                BackgroundColor = WithAlpha(InkWashTheme.Abyss, 0.4f),
                Parent = this
            };
        }

        /// <summary>左侧 240px 导航栏：竖排「任务」+ 三组导航项。</summary>
        private void BuildSidebar()
        {
            _sidebar = new Panel
            {
                BackgroundColor = InkWashTheme.BaseTertiary,
                Parent = this
            };

            _sidebarTitleBlock = new Panel
            {
                BackgroundColor = Color.Transparent,
                Parent = _sidebar
            };

            _sidebarTitle = new InkVerticalTitle
            {
                Text = "任务",
                FontSize = 28f,
                Parent = _sidebarTitleBlock
            };

            _sidebarNavPanel = new Panel
            {
                BackgroundColor = Color.Transparent,
                Parent = _sidebar
            };

            _navButtons = new List<TextRowButton>();
            _navRows = new List<Control>();

            for (int i = 0; i < NavNames.Length; i++)
            {
                foreach (int dividerIndex in NavDividerBefore)
                {
                    if (dividerIndex == i)
                        _navRows.Add(new InkDivider { Parent = _sidebarNavPanel });
                }

                bool disabled = string.IsNullOrEmpty(NavTargets[i]);
                bool active = i == NavActiveIndex;

                var navButton = new TextRowButton(
                    NavNames[i],
                    24f,
                    InkWashTheme.FontRole.Heading,
                    15f,
                    false)
                {
                    Active = active,
                    Parent = _sidebarNavPanel
                };

                if (disabled)
                {
                    // 设计稿 .nav-item-disabled：不可点击（aria-disabled）
                    navButton.SetDisabled();
                }
                else if (!active)
                {
                    string target = NavTargets[i];
                    navButton.Clicked += () => NavigationRequested?.Invoke(target);
                }

                _navButtons.Add(navButton);
                _navRows.Add(navButton);
            }
        }

        /// <summary>右侧主区域容器（顶部信息栏 + 内容区）。</summary>
        private void BuildMainArea()
        {
            _mainArea = new Panel
            {
                BackgroundColor = Color.Transparent,
                Parent = this
            };
        }

        /// <summary>顶部 60px 角色信息栏：头像 / 名字 / 等级 / 门派 / 铜钱 / 银两。</summary>
        private void BuildTopHeader()
        {
            _topHeader = new Panel
            {
                BackgroundColor = InkWashTheme.BaseTertiary,
                Parent = _mainArea
            };

            _playerAvatar = new CircleAvatar("无")
            {
                Parent = _topHeader
            };

            _playerNameLabel = new Label
            {
                Text = "无名侠",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 18f),
                TextColor = InkWashTheme.PaperBright,
                VerticalAlignment = TextAlignment.Center,
                Parent = _topHeader
            };

            _playerLevelTag = new InkTag
            {
                Text = "Lv.42",
                TagVariant = InkTagVariant.Vermilion,
                Parent = _topHeader
            };

            _playerSectLabel = new Label
            {
                Text = "逍遥派",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                TextColor = InkWashTheme.GoldPrimary,
                VerticalAlignment = TextAlignment.Center,
                Parent = _topHeader
            };

            _copperTitleLabel = new Label
            {
                Text = "铜钱",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                TextColor = InkWashTheme.PaperAged,
                VerticalAlignment = TextAlignment.Center,
                Parent = _topHeader
            };

            _copperValueLabel = new Label
            {
                Text = "8,320",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 13f),
                TextColor = InkWashTheme.PaperBright,
                VerticalAlignment = TextAlignment.Center,
                Parent = _topHeader
            };

            _silverTitleLabel = new Label
            {
                Text = "银两",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                TextColor = InkWashTheme.PaperAged,
                VerticalAlignment = TextAlignment.Center,
                Parent = _topHeader
            };

            _silverValueLabel = new Label
            {
                Text = "328",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 13f),
                TextColor = InkWashTheme.GoldBright,
                VerticalAlignment = TextAlignment.Center,
                Parent = _topHeader
            };
        }

        /// <summary>左侧任务卷宗：标题 + 计数 + 标签页 + 可滚动任务列表。</summary>
        private void BuildQuestList()
        {
            _listSection = new Panel
            {
                BackgroundColor = InkWashTheme.BaseSecondary,
                ClipChildren = true,
                Parent = _mainArea
            };

            _listTitleLabel = new Label
            {
                Text = "任务卷宗",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 15f),
                TextColor = InkWashTheme.GoldPrimary,
                VerticalAlignment = TextAlignment.Center,
                Parent = _listSection
            };

            _listCountLabel = new Label
            {
                Text = "6 项",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 11f),
                TextColor = InkWashTheme.PaperAged,
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
                Parent = _listSection
            };

            _tabBar = new Panel
            {
                BackgroundColor = InkWashTheme.BaseDefault,
                Parent = _listSection
            };

            _tabs = new List<TextRowButton>();
            for (int i = 0; i < TabNames.Length; i++)
            {
                int tabIndex = i;
                var tab = new TextRowButton(TabNames[i], 0f, InkWashTheme.FontRole.Body, 13f, true)
                {
                    Parent = _tabBar
                };
                tab.Clicked += () => SelectTab(tabIndex);
                _tabs.Add(tab);
            }

            _listScroll = new Panel
            {
                BackgroundColor = Color.Transparent,
                ClipChildren = true,
                ScrollBars = ScrollBars.Vertical,
                Parent = _listSection
            };

            _listContent = new Panel
            {
                BackgroundColor = Color.Transparent,
                Parent = _listScroll
            };

            _listEmptyHint = new Label
            {
                Text = "暂无任务记录",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                TextColor = InkWashTheme.TextTertiary,
                Visible = false,
                Parent = _listContent
            };
        }

        /// <summary>右侧任务详情：标题/状态/元信息/背景/目标/奖励/委托人/操作按钮。</summary>
        private void BuildQuestDetail()
        {
            _detailSection = new Panel
            {
                BackgroundColor = WithAlpha(InkWashTheme.Abyss, 0.4f),
                ClipChildren = true,
                Parent = _mainArea
            };

            _detailScroll = new Panel
            {
                BackgroundColor = Color.Transparent,
                ClipChildren = true,
                ScrollBars = ScrollBars.Vertical,
                Parent = _detailSection
            };

            _detailContent = new Panel
            {
                BackgroundColor = Color.Transparent,
                Parent = _detailScroll
            };

            _detailTitleLabel = new Label
            {
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 32f),
                TextColor = InkWashTheme.TextBrand,
                VerticalAlignment = TextAlignment.Center,
                Parent = _detailContent
            };

            _detailStatusTag = new QuestTag { Parent = _detailContent };

            // 元信息行：推荐等级 / 难度 / 类型
            _detailLevelPrefix = CreateDetailMetaLabel("推荐等级：", InkWashTheme.PaperAged, false);
            _detailLevelValue = CreateDetailMetaLabel("Lv.42", InkWashTheme.PaperBright, true);
            _detailDifficultyPrefix = CreateDetailMetaLabel("难度：", InkWashTheme.PaperAged, false);
            _detailDifficultyValue = CreateDetailMetaLabel("★★★★★", InkWashTheme.GoldBright, false);
            _detailTypePrefix = CreateDetailMetaLabel("类型：", InkWashTheme.PaperAged, false);
            _detailTypeValue = CreateDetailMetaLabel("主线", InkWashTheme.PaperBright, false);

            _detailDividerMeta = CreateDetailDivider();

            // 任务背景（纸色卷轴）
            _detailBackgroundPanel = new InkPaperPanel { Parent = _detailContent };
            _detailBackgroundTitle = new Label
            {
                Text = "任务背景",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 16f),
                TextColor = InkWashTheme.TextOnPaper,
                VerticalAlignment = TextAlignment.Center,
                Parent = _detailBackgroundPanel
            };
            _detailBackgroundText = new Label
            {
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                TextColor = InkWashTheme.TextOnPaper,
                Wrapping = TextWrapping.WrapWords,
                Parent = _detailBackgroundPanel
            };

            _detailDividerBackground = CreateDetailDivider();

            // 任务目标
            _detailObjectiveTitle = new Label
            {
                Text = "任务目标",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 16f),
                TextColor = InkWashTheme.GoldPrimary,
                VerticalAlignment = TextAlignment.Center,
                Parent = _detailContent
            };

            _detailDividerObjective = CreateDetailDivider();

            // 任务奖励
            _detailRewardTitle = new Label
            {
                Text = "任务奖励",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 16f),
                TextColor = InkWashTheme.GoldPrimary,
                VerticalAlignment = TextAlignment.Center,
                Parent = _detailContent
            };

            _detailDividerReward = CreateDetailDivider();

            // 委托人 / 区域 / 时限
            _detailNpcPrefix = CreateDetailMetaLabel("委托人：", InkWashTheme.PaperAged, false);
            _detailNpcValue = CreateDetailMetaLabel("李沧海", InkWashTheme.PaperBright, false);
            _detailRegionPrefix = CreateDetailMetaLabel("区域：", InkWashTheme.PaperAged, false);
            _detailRegionValue = CreateDetailMetaLabel("洛阳城东 · 太行山巅", InkWashTheme.PaperBright, false);
            _detailTimePrefix = CreateDetailMetaLabel("时限：", InkWashTheme.PaperAged, false);
            _detailTimeValue = CreateDetailMetaLabel("无", InkWashTheme.PaperBright, false);

            _detailDividerInfo = CreateDetailDivider();

            // 操作按钮（设计稿：追踪任务=朱红主操作，放弃/分享=幽灵）
            _trackBtn = new InkButton
            {
                Text = "追踪任务",
                Variant = InkButtonVariant.Vermilion,
                ButtonSize = InkButtonSize.Md,
                Parent = _detailContent
            };
            _trackBtn.Clicked += OnTrackClicked;

            _abandonBtn = new InkButton
            {
                Text = "放弃任务",
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Md,
                Parent = _detailContent
            };
            _abandonBtn.Clicked += OnAbandonClicked;

            _shareBtn = new InkButton
            {
                Text = "分享",
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Md,
                Parent = _detailContent
            };
            _shareBtn.Clicked += OnShareClicked;

            _actionHintLabel = new Label
            {
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextTertiary,
                VerticalAlignment = TextAlignment.Center,
                Parent = _detailContent
            };
        }

        /// <summary>创建详情面板的元信息标签（统一字体与对齐）。</summary>
        private Label CreateDetailMetaLabel(string text, Color color, bool numberFont)
        {
            return new Label
            {
                Text = text,
                Font = InkRenderHelper.GetFontRef(
                    numberFont ? InkWashTheme.FontRole.Number : InkWashTheme.FontRole.Body, 13f),
                TextColor = color,
                VerticalAlignment = TextAlignment.Center,
                Parent = _detailContent
            };
        }

        /// <summary>创建详情面板的分组分隔线（设计稿 &lt;hr class="ink-divider"&gt;）。</summary>
        private InkDivider CreateDetailDivider()
        {
            return new InkDivider { Parent = _detailContent };
        }

        // ───────────────────────── 交互 ─────────────────────────

        /// <summary>切换标签页并刷新卷宗列表（设计稿 .quest-tab-active）。</summary>
        private void SelectTab(int index)
        {
            _selectedTab = Mathf.Clamp(index, 0, TabNames.Length - 1);

            for (int i = 0; i < _tabs.Count; i++)
            {
                bool active = i == _selectedTab;
                _tabs[i].SetFont(
                    active ? InkWashTheme.FontRole.Heading : InkWashTheme.FontRole.Body, 13f);
                _tabs[i].Active = active;
            }

            RefreshQuestList();
        }

        /// <summary>按当前标签页重建卷宗列表，并同步计数与选中态。</summary>
        private void RefreshQuestList()
        {
            if (_questItems == null || _detailContent == null)
                return;

            foreach (var item in _questItems)
                item.Dispose();
            _questItems.Clear();

            _visibleQuests.Clear();
            string category = TabNames[_selectedTab];
            foreach (var quest in _allQuests)
            {
                if (quest.Category == category)
                    _visibleQuests.Add(quest);
            }

            _listCountLabel.Text = _visibleQuests.Count + " 项";
            _listEmptyHint.Visible = _visibleQuests.Count == 0;

            foreach (var quest in _visibleQuests)
            {
                var item = new QuestListItem(quest)
                {
                    Parent = _listContent
                };
                item.Clicked += () => SelectQuest(quest);
                _questItems.Add(item);
            }

            if (_visibleQuests.Count > 0 && (_selectedQuest == null || !_visibleQuests.Contains(_selectedQuest)))
                _selectedQuest = _visibleQuests[0];

            ApplyLayout();
            UpdateSelectionVisuals();
            UpdateQuestDetail();
        }

        /// <summary>选中任务：刷新列表选中态与右侧详情。</summary>
        private void SelectQuest(QuestData quest)
        {
            if (quest != null)
            {
                _selectedQuest = quest;
                _abandonBtn.Text = "放弃任务";
                _abandonBtn.Variant = InkButtonVariant.Ghost;
            }

            UpdateSelectionVisuals();
            UpdateQuestDetail();
        }

        /// <summary>刷新卷宗列表项的选中高亮。</summary>
        private void UpdateSelectionVisuals()
        {
            foreach (var item in _questItems)
                item.SetSelected(item.Quest == _selectedQuest);
        }

        /// <summary>把当前选中任务的数据写入右侧详情面板。</summary>
        private void UpdateQuestDetail()
        {
            var quest = _selectedQuest;
            if (quest == null)
                return;

            _detailTitleLabel.Text = quest.Name;
            _detailStatusTag.Text = quest.Status;
            _detailStatusTag.ApplyStatus(quest.Status, true);

            _detailLevelValue.Text = quest.Level;
            _detailDifficultyValue.Text = quest.Difficulty;
            _detailTypeValue.Text = quest.Category;

            _detailBackgroundText.Text = quest.Background;

            // 任务目标
            foreach (var row in _detailObjectiveRows)
                row.Dispose();
            _detailObjectiveRows.Clear();

            for (int i = 0; i < quest.Objectives.Length; i++)
            {
                bool done = i < quest.ObjectiveDone.Length && quest.ObjectiveDone[i];
                _detailObjectiveRows.Add(new ObjectiveRow(quest.Objectives[i], done)
                {
                    Parent = _detailContent
                });
            }

            // 任务奖励
            foreach (var card in _detailRewardCards)
                card.Dispose();
            _detailRewardCards.Clear();

            foreach (var reward in quest.Rewards)
            {
                _detailRewardCards.Add(new RewardCard(reward)
                {
                    Parent = _detailContent
                });
            }

            _detailNpcValue.Text = quest.Npc;
            _detailRegionValue.Text = quest.Region;
            _detailTimeValue.Text = quest.TimeLimit;

            _trackBtn.Text = _trackedQuest == quest ? "取消追踪" : "追踪任务";
            _actionHintLabel.Text = string.Empty;

            ApplyLayout();
        }

        /// <summary>追踪/取消追踪当前任务（设计稿「追踪任务」按钮）。</summary>
        private void OnTrackClicked()
        {
            var quest = _selectedQuest;
            if (quest == null)
                return;

            if (_trackedQuest == quest)
            {
                _trackedQuest = null;
                _trackBtn.Text = "追踪任务";
                _actionHintLabel.Text = "已取消追踪";
            }
            else
            {
                _trackedQuest = quest;
                _trackBtn.Text = "取消追踪";
                _actionHintLabel.Text = $"已追踪任务「{quest.Name}」";
            }
        }

        /// <summary>放弃任务：二次确认后从卷宗中移除该任务（设计稿「放弃任务」按钮）。</summary>
        private void OnAbandonClicked()
        {
            var quest = _selectedQuest;
            if (quest == null)
                return;

            if (_abandonBtn.Text != "确认放弃")
            {
                _abandonBtn.Text = "确认放弃";
                _abandonBtn.Variant = InkButtonVariant.Danger;
                _actionHintLabel.Text = $"再次点击「确认放弃」将放弃任务「{quest.Name}」";
                return;
            }

            _abandonBtn.Text = "放弃任务";
            _abandonBtn.Variant = InkButtonVariant.Ghost;

            if (_trackedQuest == quest)
                _trackedQuest = null;

            _allQuests.Remove(quest);
            _selectedQuest = null;
            _actionHintLabel.Text = $"已放弃任务「{quest.Name}」";
            RefreshQuestList();
        }

        /// <summary>分享任务（设计稿「分享」按钮）。</summary>
        private void OnShareClicked()
        {
            var quest = _selectedQuest;
            if (quest == null)
                return;

            _actionHintLabel.Text = $"已分享任务「{quest.Name}」";
        }

        // ───────────────────────── 布局 ─────────────────────────

        private void ApplyLayout()
        {
            float sw = Width > 0f ? Width : _screenSize.X;
            float sh = Height > 0f ? Height : _screenSize.Y;
            if (sw <= 0f || sh <= 0f)
                return;

            // ── 左侧导航栏 ──
            _sidebar.Location = Float2.Zero;
            _sidebar.Size = new Float2(SidebarWidth, sh);

            _sidebarTitleBlock.Location = Float2.Zero;
            _sidebarTitleBlock.Size = new Float2(SidebarWidth, 104f);

            _sidebarTitle.Location = new Float2((SidebarWidth - 40f) * 0.5f, 18f);
            _sidebarTitle.Size = new Float2(40f, 68f);

            _sidebarNavPanel.Location = new Float2(0f, 104f);
            _sidebarNavPanel.Size = new Float2(SidebarWidth, Mathf.Max(0f, sh - 104f));

            float navY = 8f;
            foreach (var row in _navRows)
            {
                if (row is InkDivider)
                {
                    navY += 8f;
                    row.Location = new Float2(24f, navY);
                    row.Size = new Float2(SidebarWidth - 48f, 1f);
                    navY += 9f;
                }
                else
                {
                    row.Location = new Float2(0f, navY);
                    row.Size = new Float2(SidebarWidth, NavItemHeight);
                    navY += NavItemHeight + 2f;
                }
            }

            // ── 右侧主区域 ──
            float mainW = Mathf.Max(0f, sw - SidebarWidth);
            _mainArea.Location = new Float2(SidebarWidth, 0f);
            _mainArea.Size = new Float2(mainW, sh);

            _topHeader.Location = Float2.Zero;
            _topHeader.Size = new Float2(mainW, TopHeaderHeight);

            float headerX = 32f;
            _playerAvatar.Location = new Float2(headerX, (TopHeaderHeight - 40f) * 0.5f);
            _playerAvatar.Size = new Float2(40f, 40f);
            headerX += 52f;

            _playerNameLabel.Location = new Float2(headerX, 18f);
            _playerNameLabel.Size = new Float2(90f, 24f);
            headerX += 96f;

            _playerLevelTag.Location = new Float2(headerX, 20f);
            _playerLevelTag.Size = new Float2(52f, 20f);
            headerX += 62f;

            _playerSectLabel.Location = new Float2(headerX, 20f);
            _playerSectLabel.Size = new Float2(80f, 20f);

            float rightEdge = mainW - 32f;
            const float valueW = 64f;
            const float titleW = 42f;
            _silverValueLabel.Location = new Float2(rightEdge - valueW, 20f);
            _silverValueLabel.Size = new Float2(valueW, 20f);
            _silverTitleLabel.Location = new Float2(rightEdge - valueW - titleW, 20f);
            _silverTitleLabel.Size = new Float2(titleW, 20f);

            float copperEdge = rightEdge - valueW - titleW - 24f;
            _copperValueLabel.Location = new Float2(copperEdge - valueW, 20f);
            _copperValueLabel.Size = new Float2(valueW, 20f);
            _copperTitleLabel.Location = new Float2(copperEdge - valueW - titleW, 20f);
            _copperTitleLabel.Size = new Float2(titleW, 20f);

            // ── 内容区：任务卷宗（左 40%）+ 任务详情（右 60%）──
            float contentY = TopHeaderHeight;
            float contentH = Mathf.Max(0f, sh - TopHeaderHeight);
            float listW = mainW * ListRatio;
            float detailW = mainW - listW;

            _listSection.Location = new Float2(0f, contentY);
            _listSection.Size = new Float2(listW, contentH);

            LayoutQuestList(listW, contentH);

            _detailSection.Location = new Float2(listW, contentY);
            _detailSection.Size = new Float2(detailW, contentH);

            LayoutQuestDetail(detailW, contentH);
        }

        /// <summary>布局任务卷宗列表（标题栏 + 标签页 + 滚动列表）。</summary>
        private void LayoutQuestList(float listW, float contentH)
        {
            const float titleH = 56f;

            _listTitleLabel.Location = new Float2(24f, 18f);
            _listTitleLabel.Size = new Float2(140f, 22f);

            _listCountLabel.Location = new Float2(listW - 24f - 64f, 19f);
            _listCountLabel.Size = new Float2(64f, 20f);

            _tabBar.Location = new Float2(0f, titleH);
            _tabBar.Size = new Float2(listW, TabHeight);

            float tabW = _tabs.Count > 0 ? listW / _tabs.Count : listW;
            for (int i = 0; i < _tabs.Count; i++)
            {
                _tabs[i].Location = new Float2(i * tabW, 0f);
                _tabs[i].Size = new Float2(tabW, TabHeight);
            }

            float scrollY = titleH + TabHeight;
            _listScroll.Location = new Float2(0f, scrollY);
            _listScroll.Size = new Float2(listW, Mathf.Max(0f, contentH - scrollY));

            const float itemGap = 12f;
            float contentHeight = itemGap;
            foreach (var item in _questItems)
                contentHeight += QuestItemHeight + itemGap;

            _listContent.Location = Float2.Zero;
            _listContent.Size = new Float2(listW, Mathf.Max(_listScroll.Height, contentHeight));

            float itemY = itemGap;
            foreach (var item in _questItems)
            {
                item.Location = new Float2(itemGap, itemY);
                item.Size = new Float2(Mathf.Max(0f, listW - itemGap * 2f), QuestItemHeight);
                item.LayoutContent();
                itemY += QuestItemHeight + itemGap;
            }

            _listEmptyHint.Location = new Float2(24f, 16f);
            _listEmptyHint.Size = new Float2(Mathf.Max(0f, listW - 48f), 24f);
        }

        /// <summary>布局任务详情面板（自顶向下顺序排布，底部按内容高度撑开滚动区）。</summary>
        private void LayoutQuestDetail(float detailW, float contentH)
        {
            _detailScroll.Location = Float2.Zero;
            _detailScroll.Size = new Float2(detailW, contentH);

            float innerW = Mathf.Max(0f, detailW - DetailPadding * 2f);
            float dy = DetailPadding;

            // 标题 + 状态标签
            _detailTitleLabel.Location = new Float2(DetailPadding, dy);
            _detailTitleLabel.Size = new Float2(Mathf.Max(0f, innerW - 100f), 44f);

            _detailStatusTag.Location = new Float2(detailW - DetailPadding - 72f, dy + 10f);
            _detailStatusTag.Size = new Float2(72f, 24f);

            dy += 56f;

            // 元信息行：推荐等级 / 难度 / 类型
            float metaX = DetailPadding;
            _detailLevelPrefix.Location = new Float2(metaX, dy);
            _detailLevelPrefix.Size = new Float2(72f, 20f);
            metaX += 72f;
            _detailLevelValue.Location = new Float2(metaX, dy);
            _detailLevelValue.Size = new Float2(60f, 20f);
            metaX += 84f;

            _detailDifficultyPrefix.Location = new Float2(metaX, dy);
            _detailDifficultyPrefix.Size = new Float2(48f, 20f);
            metaX += 48f;
            _detailDifficultyValue.Location = new Float2(metaX, dy);
            _detailDifficultyValue.Size = new Float2(90f, 20f);
            metaX += 114f;

            _detailTypePrefix.Location = new Float2(metaX, dy);
            _detailTypePrefix.Size = new Float2(48f, 20f);
            metaX += 48f;
            _detailTypeValue.Location = new Float2(metaX, dy);
            _detailTypeValue.Size = new Float2(80f, 20f);

            dy += 30f;

            _detailDividerMeta.Location = new Float2(DetailPadding, dy);
            _detailDividerMeta.Size = new Float2(innerW, 1f);
            dy += 20f;

            // 任务背景纸色卷轴
            const float backgroundPanelH = 150f;
            _detailBackgroundPanel.Location = new Float2(DetailPadding, dy);
            _detailBackgroundPanel.Size = new Float2(innerW, backgroundPanelH);

            _detailBackgroundTitle.Location = new Float2(24f, 18f);
            _detailBackgroundTitle.Size = new Float2(Mathf.Max(0f, innerW - 48f), 24f);

            _detailBackgroundText.Location = new Float2(24f, 50f);
            _detailBackgroundText.Size = new Float2(Mathf.Max(0f, innerW - 48f), backgroundPanelH - 68f);

            dy += backgroundPanelH + 24f;

            _detailDividerBackground.Location = new Float2(DetailPadding, dy);
            _detailDividerBackground.Size = new Float2(innerW, 1f);
            dy += 24f;

            // 任务目标
            _detailObjectiveTitle.Location = new Float2(DetailPadding, dy);
            _detailObjectiveTitle.Size = new Float2(160f, 24f);
            dy += 34f;

            const float objectiveRowH = 26f;
            foreach (var row in _detailObjectiveRows)
            {
                row.Location = new Float2(DetailPadding, dy);
                row.Size = new Float2(innerW, objectiveRowH);
                row.LayoutContent();
                dy += objectiveRowH + 8f;
            }

            dy += 12f;

            _detailDividerObjective.Location = new Float2(DetailPadding, dy);
            _detailDividerObjective.Size = new Float2(innerW, 1f);
            dy += 24f;

            // 任务奖励
            _detailRewardTitle.Location = new Float2(DetailPadding, dy);
            _detailRewardTitle.Size = new Float2(160f, 24f);
            dy += 34f;

            if (_detailRewardCards.Count > 0)
            {
                const int columns = 4;
                const float gap = 12f;
                float cardW = Mathf.Max(60f, (innerW - gap * (columns - 1)) / columns);

                for (int i = 0; i < _detailRewardCards.Count; i++)
                {
                    int rowIndex = i / columns;
                    int col = i % columns;

                    var card = _detailRewardCards[i];
                    card.Location = new Float2(
                        DetailPadding + col * (cardW + gap),
                        dy + rowIndex * (RewardCardHeight + gap));
                    card.Size = new Float2(cardW, RewardCardHeight);
                    card.LayoutContent();
                }

                int rows = (_detailRewardCards.Count + columns - 1) / columns;
                dy += rows * (RewardCardHeight + gap);
            }

            _detailDividerReward.Location = new Float2(DetailPadding, dy);
            _detailDividerReward.Size = new Float2(innerW, 1f);
            dy += 24f;

            // 委托人 / 区域 / 时限
            float infoX = DetailPadding;
            _detailNpcPrefix.Location = new Float2(infoX, dy);
            _detailNpcPrefix.Size = new Float2(56f, 22f);
            infoX += 56f;
            _detailNpcValue.Location = new Float2(infoX, dy);
            _detailNpcValue.Size = new Float2(72f, 22f);
            infoX += 104f;

            _detailRegionPrefix.Location = new Float2(infoX, dy);
            _detailRegionPrefix.Size = new Float2(44f, 22f);
            infoX += 44f;
            _detailRegionValue.Location = new Float2(infoX, dy);
            _detailRegionValue.Size = new Float2(200f, 22f);
            infoX += 224f;

            _detailTimePrefix.Location = new Float2(infoX, dy);
            _detailTimePrefix.Size = new Float2(44f, 22f);
            infoX += 44f;
            _detailTimeValue.Location = new Float2(infoX, dy);
            _detailTimeValue.Size = new Float2(120f, 22f);

            dy += 34f;

            _detailDividerInfo.Location = new Float2(DetailPadding, dy);
            _detailDividerInfo.Size = new Float2(innerW, 1f);
            dy += 24f;

            // 操作按钮
            const float btnH = 36f;
            _trackBtn.Location = new Float2(DetailPadding, dy);
            _trackBtn.Size = new Float2(112f, btnH);

            _abandonBtn.Location = new Float2(DetailPadding + 124f, dy);
            _abandonBtn.Size = new Float2(100f, btnH);

            _shareBtn.Location = new Float2(DetailPadding + 236f, dy);
            _shareBtn.Size = new Float2(88f, btnH);

            dy += btnH + 16f;

            _actionHintLabel.Location = new Float2(DetailPadding, dy);
            _actionHintLabel.Size = new Float2(innerW, 20f);

            dy += 32f;

            _detailContent.Location = Float2.Zero;
            _detailContent.Size = new Float2(detailW, Mathf.Max(_detailScroll.Height, dy));
        }

        /// <summary>奖励类型图标字形（引擎无图标字体，沿用项目既有字形约定）。</summary>
        private static string RewardIcon(string label)
        {
            switch (label)
            {
                case "经验": return "✨";
                case "铜钱": return "￥";
                case "装备": return "⚔";
                default: return "☆";
            }
        }

        /// <summary>基于设计 Token 的颜色派生（仅调整透明度，不引入硬编码色值）。</summary>
        private static Color WithAlpha(Color color, float alpha)
        {
            return new Color(color.R, color.G, color.B, alpha);
        }

        /// <inheritdoc />
        public void RefreshLayout()
        {
            _screenSize = new Float2(Width, Height);
            ApplyLayout();
        }
    }
}

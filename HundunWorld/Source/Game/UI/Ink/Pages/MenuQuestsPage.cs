using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.Ink.Components;
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
    ///   ＋ 任务卷宗列表（左 40%：标题「任务卷宗 N 项」+ 主线/支线/日常/周常 标签页 + 任务卡）
    ///   ＋ 任务详情（右 60%：标题 + 状态标签 + 类型 + 任务背景纸卷 + 任务目标 + 任务奖励
    ///      + 委托人/区域/时限 + 一个主动作按钮）。
    /// 数据源：<c>QuestClientService</c> 的服务端快照（身上的任务 + 目录里的可接任务），
    ///       本页不再有任何写死卷宗；服务端没给的字段（难度/区域）显示占位而不编造。
    /// 交互：导航跳转（博物志）、标签页筛选、任务选择、接取/交付；
    ///       返回战斗 HUD 由页面外壳返回按钮承担（设计稿左上 back-hud 出口）。
    /// </summary>
    public class MenuQuestsPage : ContainerControl, IInkPage, IInkNavigationSource
    {
        // ───────────────────────── 数据模型 ─────────────────────────

        /// <summary>任务数据（对应设计稿卷宗列表项 + 右侧详情面板）。</summary>
        private class QuestData
        {
            /// <summary>服务端任务模板 id；0 = 不是任务系统里的真任务（本页现在不会出这种条目）。</summary>
            public int QuestId;
            /// <summary>true = 还没接的"可接任务"（来自任务目录），false = 身上已有的任务。</summary>
            public bool IsOffer;
            /// <summary>服务端状态码：0进行中 1可交付 2已完成 3已放弃；可接条目恒为 -1。</summary>
            public int StatusCode = -1;

            /// <summary>可接条目的发布者实体（接取时要报给服务端核对；身上任务为 0）。</summary>
            public ulong GiverEntityId;

            public string Name;
            /// <summary>任务类型：主线/支线/世界/日常（对应设计稿标签页）。</summary>
            public string Category;
            /// <summary>任务状态：进行中/未开始/已完成。</summary>
            public string Status;
            /// <summary>推荐等级，如 Lv.42。</summary>
            public string Level;
            /// <summary>列表进度百分比（0~100）。</summary>
            public int Progress;
            /// <summary>难度星级（◆◆◆◆◆）。</summary>
            public string Difficulty;
            /// <summary>委托人。</summary>
            public string Npc;
            /// <summary>区域。</summary>
            public string Region;
            /// <summary>时限。</summary>
            public string TimeLimit;
            /// <summary>任务背景（暗纸卷轴正文）。</summary>
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

        /// <summary>
        /// 从别的页面（战斗 HUD 的任务追踪）点进来时要定位到的任务 id；0 = 不指定。
        /// <para>
        /// 页面由路由器按需 new 出来（不是在启动时缓存），所以静态值能在构造期被读到；
        /// 构造期消费后立即清零，避免下一次正常打开任务日志时又跳到旧条目。
        /// </para>
        /// </summary>
        public static int PendingFocusQuestId { get; set; }

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
        private static readonly string[] TabNames = { "主线", "支线", "日常", "周常" };

        // 左侧导航不再本页自维护：条目与分组统一由 InkSidebarNav 提供，由共用控件 InkSidebarList 渲染
        // （原实现里 7/15 项是 aria-disabled 灰条，现在全部可达）。

        /// <summary>导航分隔线插入位置（该索引之前插入一条 .ink-divider）。</summary>

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
        private InkSidebarList _sidebarList;

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
        private QuestScrollPanel _detailBackgroundPanel;
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
        private Label _actionHintLabel;

        // 状态
        private List<QuestData> _allQuests;
        private List<QuestData> _visibleQuests;
        private QuestData _selectedQuest;
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


            /// <summary>
            /// 头像尺寸由调用方在构造后设置；子标签用 StretchAll 时不会跟着变（实测停在父尺寸+100），
            /// 这里在尺寸变化时把唯一子标签钉回父尺寸。
            /// </summary>
            protected override void OnSizeChanged()
            {
                base.OnSizeChanged();
                if (ChildrenCount > 0 && Children[0] is Control child)
                {
                    child.Location = Float2.Zero;
                    child.Size = Size;
                }
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

        /// <summary>任务背景卷轴（暗纸底 + 金发丝边 + 四角金饰，沿用原任务日志页的描述卷轴画法）。</summary>
        private sealed class QuestScrollPanel : ContainerControl
        {
            public QuestScrollPanel()
            {
                AutoFocus = false;
                BackgroundColor = Color.Transparent;
            }

            public override void Draw()
            {
                var rect = new Rectangle(Float2.Zero, Size);
                var paper = InkWashTheme.BaseTertiary;
                InkRenderHelper.FillRoundedRectangle(rect, 4f,
                    new Color(paper.R, paper.G, paper.B, 0.50f));
                InkRenderHelper.DrawRoundedRectangle(rect, 4f, InkWashTheme.BorderFaint, 1f);

                var gold = InkWashTheme.GoldPrimary;
                var corner = new Color(gold.R, gold.G, gold.B, 0.35f);
                float w = Width, h = Height;
                Render2D.FillRectangle(new Rectangle(0f, 0f, 8f, 1f), corner);
                Render2D.FillRectangle(new Rectangle(0f, 0f, 1f, 8f), corner);
                Render2D.FillRectangle(new Rectangle(w - 8f, 0f, 8f, 1f), corner);
                Render2D.FillRectangle(new Rectangle(w - 1f, 0f, 1f, 8f), corner);
                Render2D.FillRectangle(new Rectangle(0f, h - 1f, 8f, 1f), corner);
                Render2D.FillRectangle(new Rectangle(0f, h - 8f, 1f, 8f), corner);
                Render2D.FillRectangle(new Rectangle(w - 8f, h - 1f, 8f, 1f), corner);
                Render2D.FillRectangle(new Rectangle(w - 1f, h - 8f, 1f, 8f), corner);

                base.Draw();
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
                SelectQuest(ResolveInitialSelection());

                // 顶栏身份接真实数据（铜钱在该页没有独立字段，暂保留设计示例）
                InkPlayerHeaderBinder.Bind(_playerNameLabel, _playerLevelTag);
                InkPlayerHeaderBinder.BindSect(_playerSectLabel);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[MenuQuestsPage] 初始化失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 初始选中项：优先用别的页面指定的任务（并清掉请求），否则退回设计稿的默认项。
        /// 指定的任务不在当前标签页时先切到它所属的类型页，否则左侧列表里看不到高亮行、
        /// 右侧却显示着另一类任务，看起来像选错了。
        /// </summary>
        private QuestData ResolveInitialSelection()
        {
            int focus = PendingFocusQuestId;
            PendingFocusQuestId = 0;

            if (focus > 0)
            {
                foreach (var quest in _allQuests)
                {
                    if (quest == null || quest.QuestId != focus) continue;

                    int tab = Array.IndexOf(TabNames, quest.Category);
                    if (tab >= 0 && tab != _selectedTab)
                        SelectTab(tab);

                    return quest;
                }
            }

            return _allQuests.Count > DefaultSelectedQuest ? _allQuests[DefaultSelectedQuest] : null;
        }

        /// <summary>设计稿静态数据（任务卷宗 6 项 + 百年恩怨详情）。</summary>
        private void InitData()
        {
            _visibleQuests = new List<QuestData>();
            _questItems = new List<QuestListItem>();
            _detailObjectiveRows = new List<ObjectiveRow>();
            _detailRewardCards = new List<RewardCard>();

            // 任务清单一律来自服务端（QuestClientService 的缓存快照）：
            // 这一页曾经整块写死设计稿卷宗，用户在导航里点进来看到的全是假任务。
            _allQuests = new List<QuestData>();
            RebuildFromServer();
            _pendingInteractHint = false;
        }

        // ───────────────────────── 服务端数据源 ─────────────────────────

        /// <summary>上一次用来重建列表的数据版本号（服务端每回包一次它就变）。</summary>
        private int _dataRevision = -1;

        /// <summary>主动拉清单的节流计时（秒）。</summary>
        private float _requestTimer;

        /// <summary>刚点过接取/交付，等一次清单回包后给一句提示。</summary>
        private bool _pendingInteractHint;

        /// <summary>任务类型（服务端 0..3）→ 本页标签页名。</summary>
        private static string CategoryOf(int questType)
        {
            if (questType < 0 || questType >= TabNames.Length) return TabNames[0];
            return TabNames[questType];
        }

        /// <summary>
        /// 把服务端两份快照（身上的任务 + 这个世界的可接任务）映射成本页的卷宗条目。
        /// <para>
        /// <b>只填服务端给得出的字段</b>：难度星级、区域、委托人这些设计稿里有、协议里没有的东西，
        /// 一律留 "—" 而不是本地编一个——编出来的值会被当成权威数据读进下一层的坑里。
        /// </para>
        /// </summary>
        private void RebuildFromServer()
        {
            var service = HundunWorld.Game.Network.QuestClientService.Instance;
            var list = new List<QuestData>();

            foreach (var quest in service.GetQuests())
            {
                if (quest == null) continue;
                list.Add(new QuestData
                {
                    QuestId = quest.QuestId,
                    IsOffer = false,
                    StatusCode = quest.Status,
                    Name = quest.QuestName,
                    Category = CategoryOf(quest.QuestType),
                    Status = quest.Status switch
                    {
                        1 => "可交付",
                        2 => "已完成",
                        3 => "已放弃",
                        _ => "进行中",
                    },
                    Level = "—",
                    Progress = quest.TargetProgress > 0
                        ? quest.CurrentProgress * 100 / quest.TargetProgress
                        : 0,
                    Difficulty = "未评级",
                    Npc = "—",
                    Region = "—",
                    TimeLimit = RepeatOf(quest.QuestType),
                    Background = quest.Description ?? string.Empty,
                    Objectives = ObjectivesOf(quest.Objectives),
                    ObjectiveDone = ObjectiveDoneOf(quest.Objectives),
                    Rewards = RewardsOf(quest.Reward),
                });
            }

            foreach (var (giverId, offer) in service.GetOffersWithGiver())
            {
                if (offer == null || offer.AlreadyAccepted) continue;
                list.Add(new QuestData
                {
                    QuestId = offer.QuestId,
                    IsOffer = true,
                    StatusCode = -1,
                    GiverEntityId = giverId,
                    Name = offer.QuestName,
                    Category = CategoryOf(offer.QuestType),
                    Status = offer.AlreadyFinished ? "已完成" : "未开始",
                    Level = offer.LevelRequire > 0 ? "Lv." + offer.LevelRequire : "Lv.1",
                    Progress = 0,
                    Difficulty = "未评级",
                    Npc = string.IsNullOrEmpty(offer.GiverLabel) ? "—" : offer.GiverLabel,
                    Region = "—",
                    TimeLimit = RepeatOf(offer.QuestType),
                    Background = offer.Description ?? string.Empty,
                    Objectives = ObjectivesOf(offer.Objectives),
                    ObjectiveDone = ObjectiveDoneOf(offer.Objectives),
                    Rewards = RewardsOf(offer.Reward),
                });
            }

            _allQuests = list;
        }

        /// <summary>日常/周常的"时限"由任务类型决定（服务端 repeat 语义），其余无时限。</summary>
        private static string RepeatOf(int questType) => questType switch
        {
            2 => "每日重置",
            3 => "每周重置",
            _ => "无",
        };

        private static string[] ObjectivesOf(System.Collections.Generic.List<Horizon.Game.Message.Network.QuestObjectiveWire> objectives)
        {
            if (objectives == null || objectives.Count == 0) return System.Array.Empty<string>();
            var texts = new string[objectives.Count];
            for (var i = 0; i < objectives.Count; i++)
            {
                var objective = objectives[i];
                string text = objective == null ? string.Empty : (objective.Description ?? string.Empty);
                int current = objective?.Current ?? 0;
                int required = objective?.Required ?? 0;
                texts[i] = required > 0 ? text + "（" + current + "/" + required + "）" : text;
            }
            return texts;
        }

        private static bool[] ObjectiveDoneOf(System.Collections.Generic.List<Horizon.Game.Message.Network.QuestObjectiveWire> objectives)
        {
            if (objectives == null || objectives.Count == 0) return System.Array.Empty<bool>();
            var done = new bool[objectives.Count];
            for (var i = 0; i < objectives.Count; i++) done[i] = objectives[i] != null && objectives[i].Completed;
            return done;
        }

        /// <summary>
        /// 奖励卡：经验/铜钱/信物来自服务端数值，物品名用本地 <c>ItemCatalog</c> 按模板 id 解析
        /// （模板表里有的名字才显示，解析不出来就显示编号，绝不显示"道具"这种占位词）。
        /// </summary>
        private static RewardData[] RewardsOf(Horizon.Game.Message.Network.QuestRewardWire reward)
        {
            var cards = new List<RewardData>();
            if (reward == null) return cards.ToArray();

            if (reward.Exp > 0)
                cards.Add(new RewardData
                {
                    Label = "经验",
                    Value = reward.Exp.ToString("N0"),
                    ValueColor = InkWashTheme.PaperBright,
                    IconColor = InkWashTheme.GoldPrimary,
                });

            if (reward.Gold > 0)
                cards.Add(new RewardData
                {
                    Label = "铜钱",
                    Value = reward.Gold.ToString("N0"),
                    ValueColor = InkWashTheme.PaperBright,
                    IconColor = InkWashTheme.PaperAged,
                });

            if (reward.Items != null)
            {
                foreach (var item in reward.Items)
                {
                    if (item == null || item.TemplateId <= 0) continue;
                    var definition = HundunWorld.Game.Inventory.ItemCatalog.Resolve(item.TemplateId);
                    cards.Add(new RewardData
                    {
                        Label = "物品",
                        Value = (definition?.Name ?? ("#" + item.TemplateId)) + "×" + item.Count,
                        ValueColor = InkWashTheme.PaperBright,
                        IconColor = InkWashTheme.JadeBright,
                    });
                }
            }

            if (reward.Favor != null)
            {
                foreach (var favor in reward.Favor)
                {
                    if (favor == null || favor.Points == 0) continue;
                    cards.Add(new RewardData
                    {
                        Label = "信物·" + DomainName(favor.Domain),
                        Value = favor.Points.ToString(),
                        ValueColor = InkWashTheme.PaperBright,
                        IconColor = InkWashTheme.VermilionBright,
                    });
                }
            }

            return cards.ToArray();
        }

        private static string DomainName(int domain) => domain switch
        {
            1 => "金",
            2 => "木",
            3 => "水",
            4 => "火",
            5 => "土",
            _ => "未知",
        };

        /// <summary>
        /// 主线程轮询服务端数据版本：回包都落在连接线程，界面只在这里重画（与任务日志同一口径）。
        /// </summary>
        public override void Update(float deltaSeconds)
        {
            base.Update(deltaSeconds);

            var service = HundunWorld.Game.Network.QuestClientService.Instance;

            // 打开这一页就该自己去要数据：缓存是空的时列表只会显示"没有任务"，
            // 那看起来像"你没有任务"而不是"还没问过服务端"。节流 8 秒，避免每帧一枪。
            _requestTimer += deltaSeconds;
            if (_dataRevision < 0 || _requestTimer >= 8f)
            {
                _requestTimer = 0f;
                service.RequestList();
            }

            if (_dataRevision == service.Revision) return;
            _dataRevision = service.Revision;

            int keepId = _selectedQuest != null ? _selectedQuest.QuestId : 0;
            RebuildFromServer();
            _allQuests.Sort((a, b) => a.QuestId.CompareTo(b.QuestId));

            QuestData next = null;
            foreach (var quest in _allQuests)
            {
                if (quest.QuestId == keepId) { next = quest; break; }
            }
            _selectedQuest = next;

            RefreshQuestList();

            if (_pendingInteractHint)
            {
                _pendingInteractHint = false;
                HundunWorld.Game.UI.UIHelper.ShowToast(service.LastNotice, HundunWorld.Game.UI.ToastType.Quest);
            }
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

            // 侧边栏导航：共用控件渲染 InkSidebarNav 的完整清单（分组 + 超出可滚动）
            _sidebarList = new InkSidebarList(InkPageDomIds.NavQuests)
            {
                Parent = _sidebar,
            };
            _sidebarList.NavigationRequested += domId => NavigationRequested?.Invoke(domId);
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
                Text = "—",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 18f),
                TextColor = InkWashTheme.PaperBright,
                VerticalAlignment = TextAlignment.Center,
                Parent = _topHeader
            };

            _playerLevelTag = new InkTag
            {
                Text = "—",
                TagVariant = InkTagVariant.Vermilion,
                Parent = _topHeader
            };

            _playerSectLabel = new Label
            {
                Text = "—",
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
                Text = "—",
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
                Text = "—",
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
            _detailLevelValue = CreateDetailMetaLabel("—", InkWashTheme.PaperBright, true);
            _detailDifficultyPrefix = CreateDetailMetaLabel("难度：", InkWashTheme.PaperAged, false);
            _detailDifficultyValue = CreateDetailMetaLabel("—", InkWashTheme.GoldBright, false);
            _detailTypePrefix = CreateDetailMetaLabel("类型：", InkWashTheme.PaperAged, false);
            _detailTypeValue = CreateDetailMetaLabel("—", InkWashTheme.PaperBright, false);

            _detailDividerMeta = CreateDetailDivider();

            // 任务背景（暗纸卷轴，样式取自原任务日志页的描述卷轴）
            _detailBackgroundPanel = new QuestScrollPanel { Parent = _detailContent };
            _detailBackgroundTitle = new Label
            {
                Text = "任务背景",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 16f),
                TextColor = InkWashTheme.GoldPrimary,
                VerticalAlignment = TextAlignment.Center,
                Parent = _detailBackgroundPanel
            };
            _detailBackgroundText = new Label
            {
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                TextColor = InkWashTheme.TextDefault,
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
            _detailNpcValue = CreateDetailMetaLabel("—", InkWashTheme.PaperBright, false);
            _detailRegionPrefix = CreateDetailMetaLabel("区域：", InkWashTheme.PaperAged, false);
            _detailRegionValue = CreateDetailMetaLabel("—", InkWashTheme.PaperBright, false);
            _detailTimePrefix = CreateDetailMetaLabel("时限：", InkWashTheme.PaperAged, false);
            _detailTimeValue = CreateDetailMetaLabel("—", InkWashTheme.PaperBright, false);

            _detailDividerInfo = CreateDetailDivider();

            // 操作按钮：只留一个真能用的主动作（接取/交付）。
            // 设计稿上的"追踪/放弃/分享"都没有服务端通道，留着就是点了没反应的死按钮——
            // 任务日志页改数据驱动时已经踩过一次同一个坑。
            _trackBtn = new InkButton
            {
                Text = "接取任务",
                Variant = InkButtonVariant.Vermilion,
                ButtonSize = InkButtonSize.Md,
                Visible = false, // 没选中任务前不显示
                Parent = _detailContent
            };
            _trackBtn.Clicked += OnPrimaryActionClicked;

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
                _selectedQuest = quest;

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
            {
                // 列表为空（服务端还没给卷宗）：详情回到占位，别留构造期那套设计稿示例
                _detailTitleLabel.Text = "未选择任务";
                _detailStatusTag.Text = "—";
                _detailLevelValue.Text = "—";
                _detailDifficultyValue.Text = "—";
                _detailTypeValue.Text = "—";
                _detailBackgroundText.Text = "—";
                _detailNpcValue.Text = "—";
                _detailRegionValue.Text = "—";
                _detailTimeValue.Text = "—";
                if (_trackBtn != null)
                    _trackBtn.Visible = false;
                _actionHintLabel.Text = string.Empty;

                foreach (var row in _detailObjectiveRows)
                    row.Dispose();
                _detailObjectiveRows.Clear();
                foreach (var card in _detailRewardCards)
                    card.Dispose();
                _detailRewardCards.Clear();

                ApplyLayout();
                return;
            }

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

            if (_trackBtn != null)
            {
                bool canAccept = quest.IsOffer;
                bool canSubmit = !quest.IsOffer && quest.StatusCode == 1;
                _trackBtn.Visible = canAccept || canSubmit;
                _trackBtn.Text = canAccept ? "接取任务" : "交付任务";
            }
            _actionHintLabel.Text = string.Empty;

            ApplyLayout();
        }

        /// <summary>
        /// 主动作：可接条目→接取；身上"可交付"的任务→交付。
        /// <para>
        /// 只把<b>模板 id</b> 交给服务端：名称/目标/奖励全部由服务端读模板表（客户端自带奖励=凭空造奖励）。
        /// 成败也不在这里下结论——回包到达后版本号变化，<see cref="Update"/> 重建清单并补一句服务端原话。
        /// </para>
        /// </summary>
        private void OnPrimaryActionClicked()
        {
            var quest = _selectedQuest;
            if (quest == null || quest.QuestId <= 0)
                return;

            var service = HundunWorld.Game.Network.QuestClientService.Instance;
            bool sent = quest.IsOffer
                ? service.Accept(quest.QuestId, quest.GiverEntityId)
                : service.Complete(quest.QuestId);

            if (!sent)
            {
                _actionHintLabel.Text = "还没连上服务端，稍后再试";
                return;
            }

            _actionHintLabel.Text = quest.IsOffer ? "已发送接取请求…" : "已发送交付请求…";
            _pendingInteractHint = true;
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

            _sidebarList.Layout(0f, 104f, SidebarWidth, Mathf.Max(0f, sh - 104f));

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
                case "装备": return "装";
                default: return "◇";
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

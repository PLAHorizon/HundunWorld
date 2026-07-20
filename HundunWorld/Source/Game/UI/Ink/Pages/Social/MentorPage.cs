using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.StyleSystem;
using System;
using System.Collections.Generic;

namespace HundunWorld.Game.UI.Ink.Pages.Social
{
    /// <summary>
    /// 师徒传承页面 — 对应 mentor.html 设计原型。
    /// <para>
    /// 三栏布局：
    /// <list type="bullet">
    ///   <item>顶部：标题"师徒传承" + 师徒等级 + 传承值 + 当前角色等级</item>
    ///   <item>左栏：3 个垂直 Tab（我的师父/我的徒弟/拜师申请）+ 师父信息卡 / 徒弟列表</item>
    ///   <item>中栏：师徒任务列表（6 个，带奖励与完成状态）+ 出师条件检查清单</item>
    ///   <item>右栏：师徒贡献进度条 + 操作按钮（传功/赠礼/解除关系/领取奖励）+ 传承记录</item>
    ///   <item>底部：返回沉浸模式 + 跳转江湖秘境</item>
    /// </list>
    /// 通过 <see cref="NavigationRequested"/> 事件向路由器暴露导航请求。
    /// </para>
    /// </summary>
    public class MentorPage : ContainerControl, IInkPage
    {
        // ===================================================================
        // 布局常量
        // =======================================================================

        /// <summary>顶部标题栏高度</summary>
        private const float TopHeaderHeight = 56f;

        /// <summary>底部按钮栏高度</summary>
        private const float BottomBarHeight = 48f;

        /// <summary>屏幕边缘留白</summary>
        private const float ScreenEdge = 16f;

        /// <summary>列间距</summary>
        private const float ColumnGap = 12f;

        /// <summary>左栏宽度（师徒关系列表）</summary>
        private const float LeftColumnWidth = 260f;

        /// <summary>右栏宽度（详情面板）</summary>
        private const float RightColumnWidth = 300f;

        /// <summary>角色 Tab 高度</summary>
        private const float RoleTabHeight = 36f;

        /// <summary>角色 Tab 间距</summary>
        private const float RoleTabGap = 6f;

        /// <summary>徒弟条目高度</summary>
        private const float DiscipleItemHeight = 54f;

        /// <summary>徒弟条目间距</summary>
        private const float DiscipleItemGap = 6f;

        /// <summary>任务条目高度</summary>
        private const float TaskItemHeight = 68f;

        /// <summary>任务条目间距</summary>
        private const float TaskItemGap = 8f;

        /// <summary>操作按钮高度</summary>
        private const float ActionBtnHeight = 36f;

        /// <summary>操作按钮间距</summary>
        private const float ActionBtnGap = 8f;

        /// <summary>底部按钮宽度</summary>
        private const float BottomBtnWidth = 140f;

        // ===================================================================
        // 子控件引用 — 顶部标题栏
        // =======================================================================

        /// <summary>顶部标题栏面板</summary>
        private InkPanel _topHeader;

        /// <summary>页面主标题</summary>
        private Label _titleLabel;

        /// <summary>英文副标题</summary>
        private Label _subtitleLabel;

        /// <summary>师徒等级标签</summary>
        private Label _mentorLevelLabel;

        /// <summary>传承值标签</summary>
        private Label _legacyValueLabel;

        /// <summary>当前角色等级标签</summary>
        private Label _charLevelLabel;

        // ===================================================================
        // 子控件引用 — 左栏（师徒关系）
        // =======================================================================

        /// <summary>左栏容器</summary>
        private InkPanel _leftPanel;

        /// <summary>角色 Tab 容器（3 个垂直 Tab）</summary>
        private ContainerControl _roleTabBar;

        /// <summary>3 个角色 Tab 按钮</summary>
        private InkButton[] _roleTabs;

        /// <summary>师父信息卡容器</summary>
        private InkPanel _masterInfoPanel;

        /// <summary>师父大头像</summary>
        private ContainerControl _masterAvatar;

        /// <summary>师父姓名标签</summary>
        private Label _masterNameLabel;

        /// <summary>师父等级与门派标签</summary>
        private Label _masterSectLabel;

        /// <summary>师徒亲密度进度条</summary>
        private InkBar _intimacyBar;

        /// <summary>师徒亲密度数值标签</summary>
        private Label _intimacyLabel;

        /// <summary>拜师天数标签</summary>
        private Label _masterDaysLabel;

        /// <summary>徒弟列表容器</summary>
        private ContainerControl _discipleListHost;

        /// <summary>徒弟条目集合</summary>
        private readonly List<ContainerControl> _discipleItems = new List<ContainerControl>();

        // ===================================================================
        // 子控件引用 — 中栏（师徒任务）
        // =======================================================================

        /// <summary>中栏容器</summary>
        private InkPanel _middlePanel;

        /// <summary>任务分类标题</summary>
        private Label _taskSectionTitle;

        /// <summary>任务列表容器</summary>
        private ContainerControl _taskListHost;

        /// <summary>任务条目集合</summary>
        private readonly List<ContainerControl> _taskItems = new List<ContainerControl>();

        /// <summary>出师条件检查清单容器</summary>
        private InkPanel _graduationPanel;

        /// <summary>出师进度标题</summary>
        private Label _graduationTitleLabel;

        /// <summary>出师进度条</summary>
        private InkBar _graduationBar;

        /// <summary>出师条件标签集合</summary>
        private readonly List<Label> _graduationConditionLabels = new List<Label>();

        // ===================================================================
        // 子控件引用 — 右栏（详情与操作）
        // =======================================================================

        /// <summary>右栏容器</summary>
        private InkPanel _rightPanel;

        /// <summary>师徒贡献进度条</summary>
        private InkBar _contributionBar;

        /// <summary>师徒贡献数值标签</summary>
        private Label _contributionLabel;

        /// <summary>操作按钮集合（传功/赠礼/解除关系/领取奖励）</summary>
        private readonly InkButton[] _actionButtons = new InkButton[4];

        /// <summary>传承记录容器</summary>
        private ContainerControl _legacyRecordsHost;

        // ===================================================================
        // 子控件引用 — 底部按钮栏
        // =======================================================================

        /// <summary>底部按钮栏容器</summary>
        private InkPanel _bottomBar;

        /// <summary>返回沉浸模式按钮</summary>
        private InkButton _backToHudButton;

        /// <summary>跳转江湖秘境按钮</summary>
        private InkButton _goDungeonButton;

        // ===================================================================
        // 模拟数据
        // =======================================================================

        /// <summary>师父信息</summary>
        private struct MasterInfo
        {
            public string Name;
            public int Level;
            public string Sect;
            public string Title;
            public int Intimacy;
            public int IntimacyMax;
            public int Days;
        }

        /// <summary>徒弟信息</summary>
        private struct DiscipleInfo
        {
            public string Name;
            public int Level;
            public string Sect;
            public float GraduationProgress;
            public bool Online;
            public int EnrollDays;
        }

        /// <summary>任务信息</summary>
        private struct TaskInfo
        {
            public string Name;
            public string Desc;
            public string Reward;
            public string Status; // 已完成 / 进行中 / 未开始
            public int ProgressCur;
            public int ProgressMax;
        }

        /// <summary>当前师父</summary>
        private MasterInfo _master = new MasterInfo
        {
            Name = "孤云长老",
            Level = 85,
            Sect = "少林派",
            Title = "武学宗师",
            Intimacy = 2840,
            IntimacyMax = 3000,
            Days = 42,
        };

        /// <summary>门下徒弟列表</summary>
        private readonly DiscipleInfo[] _disciples =
        {
            new DiscipleInfo { Name = "林清河", Level = 42, Sect = "峨眉", GraduationProgress = 0.68f, Online = true,  EnrollDays = 30 },
            new DiscipleInfo { Name = "慕晴雪", Level = 38, Sect = "唐门", GraduationProgress = 0.52f, Online = true,  EnrollDays = 25 },
            new DiscipleInfo { Name = "苏问剑", Level = 45, Sect = "武当", GraduationProgress = 0.78f, Online = false, EnrollDays = 35 },
            new DiscipleInfo { Name = "柳含烟", Level = 33, Sect = "华山", GraduationProgress = 0.40f, Online = true,  EnrollDays = 18 },
            new DiscipleInfo { Name = "萧无尘", Level = 50, Sect = "丐帮", GraduationProgress = 0.92f, Online = false, EnrollDays = 48 },
        };

        /// <summary>师徒任务列表</summary>
        private readonly TaskInfo[] _tasks =
        {
            new TaskInfo { Name = "师徒同心·行侠仗义", Desc = "组队击败山贼头目 ×5",       Reward = "+50 传承 / +10000 经验", Status = "已完成", ProgressCur = 5,  ProgressMax = 5  },
            new TaskInfo { Name = "传道授业·武学切磋", Desc = "与师父武学切磋 3 场",        Reward = "+50 传承 / +8000 经验",  Status = "进行中", ProgressCur = 2,  ProgressMax = 3  },
            new TaskInfo { Name = "晨昏定省·采药炼丹", Desc = "采集灵草 ×10 并炼制丹药",   Reward = "+50 传承 / +6000 经验",  Status = "未开始", ProgressCur = 2,  ProgressMax = 10 },
            new TaskInfo { Name = "游历江湖·名山访胜", Desc = "与师门共游名山胜景 ×3 处",  Reward = "+40 传承 / +5000 经验",  Status = "进行中", ProgressCur = 1,  ProgressMax = 3  },
            new TaskInfo { Name = "尊师重道·奉茶请安", Desc = "向师父奉茶 1 次",            Reward = "+30 传承 / +3000 经验",  Status = "未开始", ProgressCur = 0,  ProgressMax = 1  },
            new TaskInfo { Name = "师门试炼·组队通关", Desc = "通关「千机塔」第 3 层",      Reward = "+200 传承",              Status = "进行中", ProgressCur = 1,  ProgressMax = 3  },
        };

        /// <summary>出师条件（true=已达成，false=未达成）</summary>
        private readonly string[] _graduationConditions =
        {
            "✓ 师徒亲密度 ≥ 2000",
            "✓ 累计在线 ≥ 100 时",
            "✓ 完成日常 ≥ 20 次",
            "○ 等级达到 Lv.50",
            "○ 通关出师试炼副本",
        };

        /// <summary>传承记录条目</summary>
        private readonly string[] _legacyRecords =
        {
            "完成师徒同心  +50",
            "指点林清河武学  +30",
            "通关千机塔  +200",
            "切磋论剑胜出  +36",
            "签到奖励  +20",
        };

        // ===================================================================
        // 公共 API
        // =======================================================================

        /// <summary>
        /// 导航请求事件。按钮点击后通过此事件向路由器暴露目标 dom-id。
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
        /// 构造函数：初始化所有子控件。
        /// </summary>
        public MentorPage()
        {
            try
            {
                AnchorPreset = AnchorPresets.StretchAll;
                Offsets = Margin.Zero;
                BackgroundColor = Color.Transparent;
                ClipChildren = false;
                AutoFocus = false;

                BuildTopHeader();
                BuildLeftPanel();
                BuildMiddlePanel();
                BuildRightPanel();
                BuildBottomBar();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[MentorPage] 初始化失败: {ex.Message}");
            }
        }

        // ===================================================================
        // Build 方法 — 顶部标题栏
        // =======================================================================

        /// <summary>
        /// 构建顶部标题栏：返回区域 + 主标题 + 师徒等级 + 传承值 + 角色等级。
        /// </summary>
        private void BuildTopHeader()
        {
            _topHeader = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(800f, TopHeaderHeight),
            };

            // 主标题"师徒传承"
            _titleLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(ScreenEdge, 8f),
                Size = new Float2(160f, 28f),
                Text = "师徒传承",
                TextColor = InkWashTheme.TextGold,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 20f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _topHeader.AddChild(_titleLabel);

            // 英文副标题
            _subtitleLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(ScreenEdge + 160f, 16f),
                Size = new Float2(140f, 20f),
                Text = "MENTOR LINEAGE",
                TextColor = InkWashTheme.TextTertiary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 11f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _topHeader.AddChild(_subtitleLabel);

            // 师徒等级标签（右上区）
            _mentorLevelLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(560f, 18f),
                Size = new Float2(80f, 20f),
                Text = "师徒 IV",
                TextColor = InkWashTheme.TextSecondary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 13f),
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
            };
            _topHeader.AddChild(_mentorLevelLabel);

            // 传承值标签
            _legacyValueLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(640f, 18f),
                Size = new Float2(100f, 20f),
                Text = "传承 3,820",
                TextColor = InkWashTheme.TextBrand,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 13f),
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
            };
            _topHeader.AddChild(_legacyValueLabel);

            // 当前角色等级标签
            _charLevelLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(740f, 18f),
                Size = new Float2(56f, 20f),
                Text = "Lv.65",
                TextColor = InkWashTheme.TextJade,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 13f),
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
            };
            _topHeader.AddChild(_charLevelLabel);

            AddChild(_topHeader);
        }

        // ===================================================================
        // Build 方法 — 左栏（师徒关系）
        // =======================================================================

        /// <summary>
        /// 构建左栏：3 个垂直角色 Tab + 师父信息卡 + 徒弟列表。
        /// </summary>
        private void BuildLeftPanel()
        {
            _leftPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(LeftColumnWidth, 600f),
            };

            // ===== 角色 Tab 栏（3 个垂直 Tab）=====
            _roleTabBar = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(ScreenEdge, ScreenEdge),
                Size = new Float2(LeftColumnWidth - ScreenEdge * 2f, RoleTabHeight),
            };
            _leftPanel.AddChild(_roleTabBar);

            string[] roleTabLabels = { "我的师父", "我的徒弟", "拜师申请" };
            _roleTabs = new InkButton[3];
            float tabWidth = (LeftColumnWidth - ScreenEdge * 2f - RoleTabGap * 2f) / 3f;
            for (int i = 0; i < 3; i++)
            {
                var btn = new InkButton
                {
                    Variant = i == 0 ? InkButtonVariant.Primary : InkButtonVariant.Default,
                    ButtonSize = InkButtonSize.Sm,
                    Text = roleTabLabels[i],
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(i * (tabWidth + RoleTabGap), 0f),
                    Size = new Float2(tabWidth, RoleTabHeight),
                };
                _roleTabs[i] = btn;
                _roleTabBar.AddChild(btn);
            }

            // ===== 师父信息卡 =====
            BuildMasterInfoCard();

            // ===== 徒弟列表 =====
            BuildDiscipleList();

            AddChild(_leftPanel);
        }

        /// <summary>
        /// 构建师父信息卡：大头像 + 姓名 + 门派 + 亲密度进度条 + 拜师天数。
        /// </summary>
        private void BuildMasterInfoCard()
        {
            _masterInfoPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(ScreenEdge, ScreenEdge + RoleTabHeight + 12f),
                Size = new Float2(LeftColumnWidth - ScreenEdge * 2f, 130f),
            };
            _leftPanel.AddChild(_masterInfoPanel);

            // 大头像
            _masterAvatar = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 14f),
                Size = new Float2(56f, 56f),
                BackgroundColor = new Color(
                    InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G,
                    InkWashTheme.GoldPrimary.B, 0.18f),
            };
            _masterInfoPanel.AddChild(_masterAvatar);

            var avatarChar = new Label
            {
                AnchorPreset = AnchorPresets.StretchAll,
                Offsets = Margin.Zero,
                Text = "孤",
                TextColor = InkWashTheme.TextBrand,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 24f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            _masterAvatar.AddChild(avatarChar);

            // 师父姓名
            _masterNameLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(80f, 14f),
                Size = new Float2(150f, 22f),
                Text = _master.Name,
                TextColor = InkWashTheme.TextDefault,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 15f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _masterInfoPanel.AddChild(_masterNameLabel);

            // 师父门派与称号
            _masterSectLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(80f, 38f),
                Size = new Float2(160f, 18f),
                Text = $"{_master.Sect} · {_master.Title}",
                TextColor = InkWashTheme.TextSecondary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 12f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _masterInfoPanel.AddChild(_masterSectLabel);

            // 亲密度数值标签
            _intimacyLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(80f, 60f),
                Size = new Float2(160f, 16f),
                Text = $"亲密度 {_master.Intimacy} / {_master.IntimacyMax}",
                TextColor = InkWashTheme.TextTertiary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 11f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _masterInfoPanel.AddChild(_intimacyLabel);

            // 亲密度进度条
            _intimacyBar = new InkBar
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(80f, 78f),
                Size = new Float2(160f, 8f),
                Value = (float)_master.Intimacy / _master.IntimacyMax,
                FillVariant = InkBarFillVariant.Gold,
            };
            _masterInfoPanel.AddChild(_intimacyBar);

            // 拜师天数
            _masterDaysLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 102f),
                Size = new Float2(LeftColumnWidth - ScreenEdge * 2f - 24f, 18f),
                Text = $"拜师 {_master.Days} 日 · 在师门",
                TextColor = InkWashTheme.TextJade,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 11f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _masterInfoPanel.AddChild(_masterDaysLabel);
        }

        /// <summary>
        /// 构建徒弟列表：5 个徒弟条目，每个显示姓名/等级/门派/出师进度/在线状态。
        /// </summary>
        private void BuildDiscipleList()
        {
            // 列表标题
            var listTitle = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(ScreenEdge, ScreenEdge + RoleTabHeight + 12f + 130f + 8f),
                Size = new Float2(LeftColumnWidth - ScreenEdge * 2f, 20f),
                Text = "◆ 门下弟子 (" + _disciples.Length + " / 3)",
                TextColor = InkWashTheme.TextGold,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _leftPanel.AddChild(listTitle);

            // 列表容器
            _discipleListHost = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(ScreenEdge, ScreenEdge + RoleTabHeight + 12f + 130f + 8f + 24f),
                Size = new Float2(LeftColumnWidth - ScreenEdge * 2f, 400f),
            };
            _leftPanel.AddChild(_discipleListHost);

            float cursorY = 0f;
            for (int i = 0; i < _disciples.Length; i++)
            {
                var d = _disciples[i];
                var item = new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(0f, cursorY),
                    Size = new Float2(LeftColumnWidth - ScreenEdge * 2f, DiscipleItemHeight),
                    BackgroundColor = i == 0
                        ? new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.10f)
                        : Color.Transparent,
                };

                // 在线状态圆点
                var dot = new InkDot
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(8f, (DiscipleItemHeight - 8f) * 0.5f),
                    Size = new Float2(8f, 8f),
                    Online = d.Online,
                };
                item.AddChild(dot);

                // 姓名标签
                var nameLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(22f, 6f),
                    Size = new Float2(110f, 20f),
                    Text = d.Name,
                    TextColor = InkWashTheme.TextDefault,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 13f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                item.AddChild(nameLabel);

                // 等级标签
                var levelLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(132f, 6f),
                    Size = new Float2(50f, 20f),
                    Text = "Lv." + d.Level,
                    TextColor = InkWashTheme.TextGold,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 12f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                item.AddChild(levelLabel);

                // 门派与入门时间
                var sectLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(22f, 28f),
                    Size = new Float2(180f, 16f),
                    Text = $"{d.Sect} · 入门 {d.EnrollDays} 日",
                    TextColor = InkWashTheme.TextSecondary,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 11f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                item.AddChild(sectLabel);

                // 出师进度条
                var gradBar = new InkBar
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(22f, 44f),
                    Size = new Float2(140f, 6f),
                    Value = d.GraduationProgress,
                    FillVariant = d.GraduationProgress >= 0.9f ? InkBarFillVariant.Jade : InkBarFillVariant.Gold,
                };
                item.AddChild(gradBar);

                // 出师进度百分比
                var gradLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(166f, 40f),
                    Size = new Float2(50f, 14f),
                    Text = ((int)(d.GraduationProgress * 100)).ToString() + "%",
                    TextColor = InkWashTheme.TextBrand,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 11f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                item.AddChild(gradLabel);

                _discipleItems.Add(item);
                _discipleListHost.AddChild(item);
                cursorY += DiscipleItemHeight + DiscipleItemGap;
            }
        }

        // ===================================================================
        // Build 方法 — 中栏（师徒任务）
        // =======================================================================

        /// <summary>
        /// 构建中栏：任务分类标题 + 6 个师徒任务条目 + 出师条件检查清单。
        /// </summary>
        private void BuildMiddlePanel()
        {
            _middlePanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(500f, 600f),
            };

            // 任务分类标题
            _taskSectionTitle = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(ScreenEdge, ScreenEdge),
                Size = new Float2(400f, 24f),
                Text = "◆ 日常师徒任务 · 师徒组队完成，增进情谊",
                TextColor = InkWashTheme.TextGold,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 14f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _middlePanel.AddChild(_taskSectionTitle);

            // 任务列表容器
            _taskListHost = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(ScreenEdge, ScreenEdge + 28f),
                Size = new Float2(460f, 420f),
            };
            _middlePanel.AddChild(_taskListHost);

            float cursorY = 0f;
            for (int i = 0; i < _tasks.Length; i++)
            {
                var t = _tasks[i];
                var item = BuildTaskItem(t, cursorY, i);
                _taskItems.Add(item);
                _taskListHost.AddChild(item);
                cursorY += TaskItemHeight + TaskItemGap;
            }

            // 出师条件检查清单
            BuildGraduationPanel(cursorY + 8f);

            AddChild(_middlePanel);
        }

        /// <summary>
        /// 构建单个任务条目：任务名 + 状态标签 + 描述 + 进度条 + 奖励。
        /// </summary>
        private ContainerControl BuildTaskItem(TaskInfo t, float yPos, int index)
        {
            var item = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, yPos),
                Size = new Float2(460f, TaskItemHeight),
            };

            // 状态色映射
            Color statusColor;
            InkBarFillVariant fillVariant;
            if (t.Status == "已完成")
            {
                statusColor = InkWashTheme.TextJade;
                fillVariant = InkBarFillVariant.Jade;
            }
            else if (t.Status == "进行中")
            {
                statusColor = InkWashTheme.TextGold;
                fillVariant = InkBarFillVariant.Gold;
            }
            else
            {
                statusColor = InkWashTheme.TextTertiary;
                fillVariant = InkBarFillVariant.Blood;
            }

            // 任务名
            var nameLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 6f),
                Size = new Float2(240f, 20f),
                Text = t.Name,
                TextColor = InkWashTheme.TextDefault,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            item.AddChild(nameLabel);

            // 状态标签
            var statusLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(252f, 6f),
                Size = new Float2(56f, 20f),
                Text = t.Status,
                TextColor = statusColor,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 11f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            item.AddChild(statusLabel);

            // 任务描述
            var descLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 26f),
                Size = new Float2(280f, 16f),
                Text = t.Desc,
                TextColor = InkWashTheme.TextSecondary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 11f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            item.AddChild(descLabel);

            // 进度条
            var progressBar = new InkBar
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 44f),
                Size = new Float2(220f, 8f),
                Value = t.ProgressMax > 0 ? (float)t.ProgressCur / t.ProgressMax : 0f,
                FillVariant = fillVariant,
            };
            item.AddChild(progressBar);

            // 进度数值
            var progressLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(236f, 40f),
                Size = new Float2(56f, 14f),
                Text = t.ProgressCur + " / " + t.ProgressMax,
                TextColor = statusColor,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 11f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            item.AddChild(progressLabel);

            // 奖励文本
            var rewardLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(310f, 26f),
                Size = new Float2(140f, 32f),
                Text = t.Reward,
                TextColor = InkWashTheme.TextBrand,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 11f),
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
            };
            item.AddChild(rewardLabel);

            // 领取按钮（仅已完成任务可领）
            var claimBtn = new InkButton
            {
                Variant = t.Status == "已完成" ? InkButtonVariant.Primary : InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Sm,
                Text = t.Status == "已完成" ? "领取" : "未完成",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(310f, 6f),
                Size = new Float2(140f, 22f),
            };
            // 已完成任务通过粒子反馈模拟领取
            if (t.Status == "已完成")
            {
                claimBtn.ButtonClicked += (b) => EmitGoldAtButton(b);
            }
            item.AddChild(claimBtn);

            return item;
        }

        /// <summary>
        /// 构建出师条件检查清单：出师进度条 + 5 个条件标签。
        /// </summary>
        private void BuildGraduationPanel(float yPos)
        {
            _graduationPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, yPos),
                Size = new Float2(460f, 140f),
            };
            _middlePanel.AddChild(_graduationPanel);

            // 出师进度标题
            _graduationTitleLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 8f),
                Size = new Float2(300f, 20f),
                Text = "◆ 出师试炼 · 达成条件，独立江湖 (3 / 5 项达成)",
                TextColor = InkWashTheme.TextGold,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _graduationPanel.AddChild(_graduationTitleLabel);

            // 出师进度条
            _graduationBar = new InkBar
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 32f),
                Size = new Float2(436f, 10f),
                Value = 0.6f,
                FillVariant = InkBarFillVariant.Gold,
            };
            _graduationPanel.AddChild(_graduationBar);

            // 5 个出师条件标签（2 列布局）
            for (int i = 0; i < _graduationConditions.Length; i++)
            {
                int col = i % 2;
                int row = i / 2;
                var condLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(12f + col * 218f, 50f + row * 22f),
                    Size = new Float2(210f, 20f),
                    Text = _graduationConditions[i],
                    TextColor = i < 3 ? InkWashTheme.TextJade : InkWashTheme.TextTertiary,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 12f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                _graduationConditionLabels.Add(condLabel);
                _graduationPanel.AddChild(condLabel);
            }
        }

        // ===================================================================
        // Build 方法 — 右栏（详情与操作）
        // =======================================================================

        /// <summary>
        /// 构建右栏：师徒贡献进度条 + 4 个操作按钮 + 传承记录列表。
        /// </summary>
        private void BuildRightPanel()
        {
            _rightPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(RightColumnWidth, 600f),
            };

            // ===== 师徒贡献进度条区块 =====
            var contributionTitle = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(ScreenEdge, ScreenEdge),
                Size = new Float2(RightColumnWidth - ScreenEdge * 2f, 20f),
                Text = "◆ 师徒贡献",
                TextColor = InkWashTheme.TextGold,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _rightPanel.AddChild(contributionTitle);

            _contributionLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(ScreenEdge, ScreenEdge + 24f),
                Size = new Float2(RightColumnWidth - ScreenEdge * 2f, 18f),
                Text = "3,820 / 5,000 传承值",
                TextColor = InkWashTheme.TextBrand,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 12f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _rightPanel.AddChild(_contributionLabel);

            _contributionBar = new InkBar
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(ScreenEdge, ScreenEdge + 46f),
                Size = new Float2(RightColumnWidth - ScreenEdge * 2f, 10f),
                Value = 3820f / 5000f,
                FillVariant = InkBarFillVariant.Gold,
            };
            _rightPanel.AddChild(_contributionBar);

            // ===== 4 个操作按钮 =====
            string[] actionLabels = { "传功", "赠礼", "解除关系", "领取奖励" };
            InkButtonVariant[] actionVariants =
            {
                InkButtonVariant.Primary,
                InkButtonVariant.Default,
                InkButtonVariant.Vermilion,
                InkButtonVariant.Primary,
            };
            float actionStartY = ScreenEdge + 46f + 10f + 16f;
            for (int i = 0; i < 4; i++)
            {
                var btn = new InkButton
                {
                    Variant = actionVariants[i],
                    ButtonSize = InkButtonSize.Md,
                    Text = actionLabels[i],
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(ScreenEdge, actionStartY + i * (ActionBtnHeight + ActionBtnGap)),
                    Size = new Float2(RightColumnWidth - ScreenEdge * 2f, ActionBtnHeight),
                };
                // 操作按钮触发金粉粒子反馈
                btn.ButtonClicked += (b) => EmitGoldAtButton(b);
                _actionButtons[i] = btn;
                _rightPanel.AddChild(btn);
            }

            // ===== 传承记录列表 =====
            BuildLegacyRecords(actionStartY + 4 * (ActionBtnHeight + ActionBtnGap) + 8f);

            AddChild(_rightPanel);
        }

        /// <summary>
        /// 构建传承记录列表：标题 + 5 条记录条目。
        /// </summary>
        private void BuildLegacyRecords(float yPos)
        {
            var recordsTitle = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(ScreenEdge, yPos),
                Size = new Float2(RightColumnWidth - ScreenEdge * 2f, 20f),
                Text = "◆ 传承记录",
                TextColor = InkWashTheme.TextGold,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _rightPanel.AddChild(recordsTitle);

            _legacyRecordsHost = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(ScreenEdge, yPos + 24f),
                Size = new Float2(RightColumnWidth - ScreenEdge * 2f, 200f),
            };
            _rightPanel.AddChild(_legacyRecordsHost);

            float cursorY = 0f;
            for (int i = 0; i < _legacyRecords.Length; i++)
            {
                var record = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(0f, cursorY),
                    Size = new Float2(RightColumnWidth - ScreenEdge * 2f, 22f),
                    Text = "• " + _legacyRecords[i],
                    TextColor = i == 0 ? InkWashTheme.TextJade : InkWashTheme.TextSecondary,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 12f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                _legacyRecordsHost.AddChild(record);
                cursorY += 24f;
            }
        }

        // ===================================================================
        // Build 方法 — 底部按钮栏
        // =======================================================================

        /// <summary>
        /// 构建底部按钮栏：返回沉浸模式 + 跳转江湖秘境。
        /// </summary>
        private void BuildBottomBar()
        {
            _bottomBar = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(BottomBtnWidth * 2f + 12f, BottomBarHeight),
            };

            // 返回沉浸模式按钮
            _backToHudButton = new InkButton
            {
                Variant = InkButtonVariant.Default,
                ButtonSize = InkButtonSize.Md,
                Text = "返回沉浸模式",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 6f),
                Size = new Float2(BottomBtnWidth, ActionBtnHeight),
            };
            _backToHudButton.ButtonClicked += (b) => OnNavigationButtonClicked(InkPageDomIds.CombatHud, b);
            _bottomBar.AddChild(_backToHudButton);

            // 跳转江湖秘境按钮
            _goDungeonButton = new InkButton
            {
                Variant = InkButtonVariant.Primary,
                ButtonSize = InkButtonSize.Md,
                Text = "跳转江湖秘境",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(BottomBtnWidth + 12f, 6f),
                Size = new Float2(BottomBtnWidth, ActionBtnHeight),
            };
            _goDungeonButton.ButtonClicked += (b) => OnNavigationButtonClicked(InkPageDomIds.ActionMentorToDungeon, b);
            _bottomBar.AddChild(_goDungeonButton);

            AddChild(_bottomBar);
        }

        // ===================================================================
        // 事件处理
        // =======================================================================

        /// <summary>
        /// 导航按钮点击处理：发射金粉粒子 + 触发导航请求。
        /// </summary>
        private void OnNavigationButtonClicked(string domId, Button sourceButton)
        {
            try
            {
                EmitGoldAtButton(sourceButton);
                NavigationRequested?.Invoke(domId);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[MentorPage] NavigationRequested({domId}) 触发失败: {ex.Message}");
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
                FlaxEngine.Debug.LogWarning($"[MentorPage] EmitGoldAtButton 失败: {ex.Message}");
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
                float contentTop = TopHeaderHeight + ScreenEdge;
                float contentBottom = sh - BottomBarHeight - ScreenEdge;
                float contentH = contentBottom - contentTop;
                float middleWidth = sw - LeftColumnWidth - RightColumnWidth - ColumnGap * 2f - ScreenEdge * 2f;

                // 顶部标题栏：顶部全宽
                if (_topHeader != null)
                {
                    _topHeader.Location = new Float2(ScreenEdge, ScreenEdge);
                    _topHeader.Size = new Float2(sw - ScreenEdge * 2f, TopHeaderHeight);
                }

                // 左栏：左上角
                if (_leftPanel != null)
                {
                    _leftPanel.Location = new Float2(ScreenEdge, contentTop);
                    _leftPanel.Size = new Float2(LeftColumnWidth, contentH);
                }

                // 中栏：左栏右侧
                if (_middlePanel != null)
                {
                    _middlePanel.Location = new Float2(ScreenEdge + LeftColumnWidth + ColumnGap, contentTop);
                    _middlePanel.Size = new Float2(middleWidth, contentH);

                    // 同步任务列表容器宽度
                    if (_taskListHost != null)
                    {
                        _taskListHost.Size = new Float2(middleWidth - ScreenEdge * 2f, _taskListHost.Height);
                    }
                    if (_taskSectionTitle != null)
                    {
                        _taskSectionTitle.Size = new Float2(middleWidth - ScreenEdge * 2f, 24f);
                    }

                    // 同步出师条件面板宽度与位置
                    if (_graduationPanel != null)
                    {
                        _graduationPanel.Size = new Float2(middleWidth - ScreenEdge * 2f, 140f);
                        if (_graduationBar != null)
                        {
                            _graduationBar.Size = new Float2(middleWidth - ScreenEdge * 2f - 24f, 10f);
                        }
                    }
                }

                // 右栏：右侧
                if (_rightPanel != null)
                {
                    _rightPanel.Location = new Float2(sw - RightColumnWidth - ScreenEdge, contentTop);
                    _rightPanel.Size = new Float2(RightColumnWidth, contentH);
                }

                // 底部按钮栏：底部居中
                if (_bottomBar != null)
                {
                    float barWidth = BottomBtnWidth * 2f + 12f;
                    _bottomBar.Location = new Float2(sw * 0.5f - barWidth * 0.5f, sh - BottomBarHeight - ScreenEdge * 0.5f);
                    _bottomBar.Size = new Float2(barWidth, BottomBarHeight);
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[MentorPage] RefreshLayout 失败: {ex.Message}");
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

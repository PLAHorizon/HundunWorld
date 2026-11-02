using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.Ink.Components;
using HundunWorld.Game.UI.StyleSystem;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HundunWorld.Game.UI.Ink.Pages.Sect
{
    public class MenuSectPage : ContainerControl, IInkPage, IInkNavigationSource
    {
        private Float2 _screenSize;

        private Panel _backgroundLayer;
        private Panel _vignette;

        private Panel _sidebar;
        private Label _sidebarLogo;
        private Label _sidebarLogoSub;
        private InkSidebarList _sidebarList;

        private Panel _mainArea;
        private Panel _topHeader;
        private InkButton _backButton;
        private ContainerControl _playerAvatar;
        private Label _playerNameLabel;
        private Label _playerLevelLabel;
        private Label _playerSectLabel;
        private Label _goldLabel;
        private Label _jadeLabel;

        private Label _pageTitle;

        /// <summary>左栏竖排标识「门派志」（设计稿 .ink-vertical-title）</summary>
        private InkVerticalTitle _verticalPageTitle;

        private Panel _sectListSection;
        private List<ContainerControl> _sectListItems;

        private Panel _sectDetailSection;
        private InkPanel _sectInfoPanel;
        private ContainerControl _sectEmblem;
        private Label _sectNameLabel;
        private Label _sectRankLabel;
        private Label _sectDescLabel;
        private Label _contributionLabel;

        /// <summary>门派贡献度标题（设计稿 menu-sect.html「门派贡献度」）</summary>
        private Label _contributionTitleLabel;
        private InkBar _contributionBar;
        private Label _contributionHintLabel;

        private InkPanel _skillTreePanel;
        private Label _skillTreeTitle;
        private Label _skillTreeCountLabel;
        private List<Panel> _skillBranches;

        private InkPanel _dailyTasksPanel;
        private Label _dailyTasksTitle;
        private Label _dailyTasksRefreshLabel;
        private List<DailyTaskRefs> _dailyTaskItems;

        private InkPaperPanel _historyPanel;
        private Label _historyOriginTitle;
        private Label _historyOriginContent;
        private Label _historyRulesTitle;
        private List<Label> _historyRules;

        private const float SidebarWidth = 240f;
        private const float TopHeaderHeight = 60f;
        private const float PanelPadding = 20f;
        private const float SectListRatio = 0.3f;
        private const float SectDetailRatio = 0.7f;

        public event Action<string> NavigationRequested;

        private string[] _sectNames = { "逍遥派", "少林寺", "武当派", "峨眉派", "华山派", "丐帮", "唐门", "全真教" };
        private string[] _sectEmblems = { "逍", "少", "武", "峨", "华", "丐", "唐", "全" };
        private string[] _sectFlavors = { "逍遥自在", "外家正宗", "内家太极", "剑法灵动", "剑气冲霄", "降龙掌法", "暗器机关", "先天功法" };
        /// <summary>列表副标题（风味描述 · 加入状态），由 <see cref="InitSectMembership"/> 按真实数据生成。</summary>
        private string[] _sectDescs;
        /// <summary>各门派的加入状态，由 <see cref="InitSectMembership"/> 按角色真实门派生成。</summary>
        private bool[] _sectJoined;
        private string[] _sectRanks = { "内门弟子", "", "", "", "", "", "", "" };
        // 门派贡献没有服务端数据：全部归零（2026-10-29）
        private int[] _sectContributions = { 0, 0, 0, 0, 0, 0, 0, 0 };

        private string[] _skillBranchNames = { "逍遥剑法", "凌波微步", "北冥神功" };
        private int[] _skillBranchLearned = { 0, 0, 0 };
        private int[] _skillBranchTotal = { 4, 4, 4 };
        private string[][] _skillNodeNames = {
            new[] { "逍遥剑意", "剑气纵横", "万剑归宗", "天外飞仙" },
            new[] { "步法入门", "踏雪无痕", "飞花逐影", "凌空虚渡" },
            new[] { "吐纳归元", "北冥吸功", "真气化海", "逍遥御风" }
        };
        // 门派武学没有服务端数据：等级/学习状态全部按"未修习"呈现，节点名保留（2026-10-29）
        private string[][] _skillNodeLevels = {
            new[] { "未修习", "未修习", "未修习", "未修习" },
            new[] { "未修习", "未修习", "未修习", "未修习" },
            new[] { "未修习", "未修习", "未修习", "未修习" }
        };
        private bool[][] _skillNodeLearned = {
            new[] { false, false, false, false },
            new[] { false, false, false, false },
            new[] { false, false, false, false }
        };

        /// <summary>门派日常一行的控件引用：布局只认引用，不再按文案去猜子控件是谁。</summary>
        private sealed class DailyTaskRefs
        {
            public Panel Item;
            public Panel Icon;
            public Label Name;
            public Label Status;
            public Label Desc;
            public Label Progress;
            public Label Reward;
        }

        private string[] _dailyTaskNames = { "门派巡逻", "传功解惑", "门派试炼" };
        private string[] _dailyTaskDescs = { "巡视门派领地，驱逐入侵之敌", "指导门派新弟子修炼基础功法", "通过门派武学考核，证明实力" };
        private string[] _dailyTaskStatus = { "未开始", "未开始", "未开始" };
        private string[] _dailyTaskProgress = { "0 / 5", "0 / 3", "0 / 1" };
        private string[] _dailyTaskRewards = { "贡献 +200", "贡献 +150", "贡献 +500" };

        /// <summary>
        /// 门派成员身份取自角色真实数据：创角不再选门派，未加入前所有门派都显示「未加入」。
        /// 这里原本写死「逍遥派已加入」，会让无门派角色一打开门派页就以为自己已入门派。
        /// </summary>
        private void InitSectMembership()
        {
            _sectJoined = new bool[_sectNames.Length];
            _sectDescs = new string[_sectNames.Length];

            var role = global::Game.UI.Character.CharacterManager.Instance?.SelectedCharacter;
            string current = global::HundunWorld.Game.UI.Ink.Pages.CharacterCreation.CcSectNames.Get(
                role?.Profession ?? Horizon.Game.Message.Enums.Profession.None);

            for (int i = 0; i < _sectNames.Length; i++)
            {
                _sectJoined[i] = !string.IsNullOrEmpty(current) && _sectNames[i] == current;
                _sectDescs[i] = $"{_sectFlavors[i]} · {(_sectJoined[i] ? "已加入" : "未加入")}";
            }
        }

        public MenuSectPage()
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

            // 门派成员身份必须在 BuildUI 画列表之前按真实数据算好
            InitSectMembership();
            Size = _screenSize;

            try
            {
                BuildAtmosphere();
                BuildSidebar();
                BuildMainArea();
                BuildTopHeader();
                BuildPageTitle();
                BuildSectList();
                BuildSectDetail();

                ApplyLayout();
                RefreshAllData();

                // 顶栏身份接真实数据
                InkPlayerHeaderBinder.Bind(_playerNameLabel, _playerLevelLabel);
                InkPlayerHeaderBinder.BindSect(_playerSectLabel);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[MenuSectPage] 初始化失败: {ex.Message}");
            }
        }

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
                BackgroundColor = new Color(InkWashTheme.BaseDefault.R, InkWashTheme.BaseDefault.G, InkWashTheme.BaseDefault.B, 0.4f),
                Parent = this
            };
        }

        private void BuildSidebar()
        {
            _sidebar = new Panel
            {
                BackgroundColor = InkWashTheme.BaseDefault,
                Parent = this
            };

            _sidebarLogo = new Label
            {
                Text = "混沌世界",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 20f),
                TextColor = InkWashTheme.GoldBright,
                Parent = _sidebar
            };

            _sidebarLogoSub = new Label
            {
                Text = "江湖公测",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.PaperAged,
                Parent = _sidebar
            };

            // 侧边栏导航：共用控件渲染 InkSidebarNav 的完整清单（分组 + 超出可滚动）。
            _sidebarList = new InkSidebarList(InkPageDomIds.NavSect)
            {
                Parent = _sidebar,
            };
            _sidebarList.NavigationRequested += OnNavClicked;
        }

        private void BuildMainArea()
        {
            _mainArea = new Panel
            {
                BackgroundColor = Color.Transparent,
                Parent = this
            };
        }

        private void BuildTopHeader()
        {
            _topHeader = new Panel
            {
                BackgroundColor = InkWashTheme.BaseSecondary,
                Parent = _mainArea
            };

            _backButton = new InkButton
            {
                Text = "返回战场",
                Width = 100,
                Height = 36,
                BackgroundColor = Color.Transparent,
                TextColor = InkWashTheme.GoldBright,
                Parent = _topHeader
            };
            _backButton.Clicked += OnBackToHud;

            _playerAvatar = new ContainerControl
            {
                Size = new Float2(36, 36),
                BackgroundColor = InkWashTheme.GoldPrimary * 0.2f,
                Parent = _topHeader
            };
            Label avatarText = new Label
            {
                Text = "无",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 16f),
                TextColor = InkWashTheme.GoldBright,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                // 头像圈固定 36×36：StretchAll 不跟父尺寸（实测 136×56），字会被裁
                Location = Float2.Zero,
                Size = new Vector2(36f, 36f),
                Parent = _playerAvatar
            };

            _playerNameLabel = new Label
            {
                // 初值留空位，真实昵称由 InkPlayerHeaderBinder 覆盖
                Text = "—",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 14f),
                TextColor = InkWashTheme.TextDefault,
                Parent = _topHeader
            };

            _playerLevelLabel = new Label
            {
                Text = "—",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.GoldBright,
                Parent = _topHeader
            };

            _playerSectLabel = new Label
            {
                Text = "逍遥派",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.PaperAged,
                Parent = _topHeader
            };

            _goldLabel = new Label
            {
                Text = "—",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 11f),
                TextColor = InkWashTheme.TextDefault,
                Parent = _topHeader
            };

            _jadeLabel = new Label
            {
                Text = "—",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 11f),
                TextColor = InkWashTheme.TextDefault,
                Parent = _topHeader
            };
        }

        private void BuildPageTitle()
        {
            _pageTitle = new Label
            {
                Text = "门派",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 14f),
                TextColor = InkWashTheme.GoldBright,
                Parent = _mainArea
            };

            // 设计稿左栏竖排标识「门派志」（.ink-vertical-title text-sm）
            _verticalPageTitle = new InkVerticalTitle
            {
                Text = "门派志",
                FontSize = 13f,
                Parent = _mainArea
            };
        }

        private void BuildSectList()
        {
            _sectListSection = new Panel
            {
                BackgroundColor = Color.Transparent,
                Parent = _mainArea
            };

            _sectListItems = new List<ContainerControl>();
            for (int i = 0; i < _sectNames.Length; i++)
            {
                ContainerControl item = new ContainerControl
                {
                    BackgroundColor = _sectJoined[i] ? InkWashTheme.GoldPrimary * 0.1f : Color.Transparent,
                    Parent = _sectListSection
                };

                ContainerControl emblemBg = new ContainerControl
                {
                    Size = new Float2(32, 32),
                    BackgroundColor = _sectJoined[i] ? InkWashTheme.GoldPrimary * 0.2f : InkWashTheme.BaseTertiary,
                    Parent = item
                };
                Label emblemLabel = new Label
                {
                    Text = _sectEmblems[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 16f),
                    TextColor = _sectJoined[i] ? InkWashTheme.GoldBright : InkWashTheme.PaperDark,
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                    // 徽章框固定 32×32：StretchAll 在这里不会跟着父尺寸走（实测停默认 180×100），
                    // 居中后字整个落到框外，画面上就是一个个空白方块。
                    Location = Float2.Zero,
                    Size = new Vector2(32f, 32f),
                    Parent = emblemBg
                };

                Label nameLabel = new Label
                {
                    Text = _sectNames[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 14f),
                    TextColor = _sectJoined[i] ? InkWashTheme.TextDefault : InkWashTheme.PaperDark,
                    Parent = item
                };

                Label descLabel = new Label
                {
                    Text = _sectJoined[i] ? $"内门弟子 · 贡献 {_sectContributions[i]}" : _sectDescs[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                    TextColor = _sectJoined[i] ? InkWashTheme.PaperAged : InkWashTheme.TextTertiary,
                    Parent = item
                };

                Label statusLabel = new Label
                {
                    Text = _sectJoined[i] ? "已加入" : "未加入",
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                    TextColor = _sectJoined[i] ? InkWashTheme.GoldBright : InkWashTheme.TextTertiary,
                    Parent = item
                };

                _sectListItems.Add(item);
            }
        }

        private void BuildSectDetail()
        {
            _sectDetailSection = new Panel
            {
                BackgroundColor = Color.Transparent,
                Parent = _mainArea
            };

            _sectInfoPanel = new InkPanel
            {
                Parent = _sectDetailSection
            };

            _sectEmblem = new ContainerControl
            {
                Size = new Float2(80, 80),
                BackgroundColor = InkWashTheme.GoldPrimary * 0.2f,
                Parent = _sectInfoPanel
            };
            Label emblemText = new Label
            {
                Text = "逍",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 32f),
                TextColor = InkWashTheme.GoldBright,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                // 徽章框固定 80×80：StretchAll 实测仍是 180×100，字被裁成半个
                Location = Float2.Zero,
                Size = new Vector2(80f, 80f),
                Parent = _sectEmblem
            };

            _sectNameLabel = new Label
            {
                Text = "逍遥派",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 24f),
                TextColor = InkWashTheme.GoldBright,
                Parent = _sectInfoPanel
            };

            _sectRankLabel = new Label
            {
                Text = "内门弟子",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.GoldBright,
                Parent = _sectInfoPanel
            };

            _sectDescLabel = new Label
            {
                Text = "逍遥自在，无拘无束。以逍遥游为心法根基，剑法轻灵飘逸，步法出神入化，内功深不可测。",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.PaperAged,
                Parent = _sectInfoPanel
            };

            _contributionTitleLabel = new Label
            {
                Text = "门派贡献度",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                TextColor = InkWashTheme.TextOnPaper,
                Parent = _sectInfoPanel
            };

            _contributionLabel = new Label
            {
                Text = "—",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 11f),
                TextColor = InkWashTheme.GoldBright,
                Parent = _sectInfoPanel
            };

            _contributionBar = new InkBar
            {
                Value = 0f,
                Height = 8f,
                Parent = _sectInfoPanel
            };

            _contributionHintLabel = new Label
            {
                Text = "尚未加入门派 · 贡献度暂不可用",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextTertiary,
                Parent = _sectInfoPanel
            };

            _skillTreePanel = new InkPanel
            {
                Parent = _sectDetailSection
            };

            _skillTreeTitle = new Label
            {
                Text = "门派武学",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 14f),
                TextColor = InkWashTheme.PaperBright,
                Parent = _skillTreePanel
            };

            _skillTreeCountLabel = new Label
            {
                Text = "已学 0 / 12",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextTertiary,
                Parent = _skillTreePanel
            };

            _skillBranches = new List<Panel>();
            for (int branchIdx = 0; branchIdx < _skillBranchNames.Length; branchIdx++)
            {
                Panel branch = new Panel
                {
                    BackgroundColor = Color.Transparent,
                    Parent = _skillTreePanel
                };

                Label branchTitle = new Label
                {
                    Text = _skillBranchNames[branchIdx],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 14f),
                    TextColor = InkWashTheme.PaperBright,
                    Parent = branch
                };

                Label branchCount = new Label
                {
                    Text = $"已学 {_skillBranchLearned[branchIdx]} / {_skillBranchTotal[branchIdx]}",
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                    TextColor = InkWashTheme.GoldPrimary,
                    Parent = branch
                };

                for (int nodeIdx = 0; nodeIdx < _skillNodeNames[branchIdx].Length; nodeIdx++)
                {
                    Panel node = new Panel
                    {
                        BackgroundColor = Color.Transparent,
                        Parent = branch
                    };

                    Panel nodeCircle = new Panel
                    {
                        Size = new Float2(24, 24),
                        BackgroundColor = _skillNodeLearned[branchIdx][nodeIdx] ? InkWashTheme.GoldPrimary : InkWashTheme.BaseElevated,
                        Parent = node
                    };

                    Label nodeName = new Label
                    {
                        Text = _skillNodeNames[branchIdx][nodeIdx],
                        Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                        TextColor = _skillNodeLearned[branchIdx][nodeIdx] ? InkWashTheme.TextDefault : InkWashTheme.PaperDark,
                        Parent = node
                    };

                    Label nodeLevel = new Label
                    {
                        Text = _skillNodeLevels[branchIdx][nodeIdx],
                        Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 11f),
                        TextColor = _skillNodeLearned[branchIdx][nodeIdx] ? InkWashTheme.GoldBright : InkWashTheme.TextTertiary,
                        Parent = node
                    };
                }

                _skillBranches.Add(branch);
            }

            _dailyTasksPanel = new InkPanel
            {
                Parent = _sectDetailSection
            };

            _dailyTasksTitle = new Label
            {
                Text = "门派日常",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 14f),
                TextColor = InkWashTheme.PaperBright,
                Parent = _dailyTasksPanel
            };

            _dailyTasksRefreshLabel = new Label
            {
                Text = "每日 05:00 刷新",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextTertiary,
                Parent = _dailyTasksPanel
            };

            _dailyTaskItems = new List<DailyTaskRefs>();
            for (int i = 0; i < _dailyTaskNames.Length; i++)
            {
                Panel item = new Panel
                {
                    BackgroundColor = Color.Transparent,
                    Parent = _dailyTasksPanel
                };

                Panel iconWrap = new Panel
                {
                    Size = new Float2(32, 32),
                    BackgroundColor = InkWashTheme.BaseTertiary,
                    Parent = item
                };

                Label taskName = new Label
                {
                    Text = _dailyTaskNames[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 13f),
                    TextColor = InkWashTheme.PaperBright,
                    Parent = item
                };

                Label taskStatus = new Label
                {
                    Text = _dailyTaskStatus[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                    TextColor = _dailyTaskStatus[i] == "进行中" ? InkWashTheme.JadeBright : InkWashTheme.TextTertiary,
                    Parent = item
                };

                Label taskDesc = new Label
                {
                    Text = _dailyTaskDescs[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                    TextColor = InkWashTheme.TextTertiary,
                    Parent = item
                };

                Label taskProgress = new Label
                {
                    Text = _dailyTaskProgress[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 13f),
                    TextColor = _dailyTaskStatus[i] == "进行中" ? InkWashTheme.GoldBright : InkWashTheme.PaperDark,
                    Parent = item
                };

                Label taskReward = new Label
                {
                    Text = _dailyTaskRewards[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                    TextColor = InkWashTheme.TextTertiary,
                    Parent = item
                };

                _dailyTaskItems.Add(new DailyTaskRefs
                {
                    Item = item,
                    Icon = iconWrap,
                    Name = taskName,
                    Status = taskStatus,
                    Desc = taskDesc,
                    Progress = taskProgress,
                    Reward = taskReward,
                });
            }

            _historyPanel = new InkPaperPanel
            {
                Parent = _sectDetailSection
            };

            _historyOriginTitle = new Label
            {
                Text = "门派渊源",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 16f),
                TextColor = InkWashTheme.TextOnPaper,
                Parent = _historyPanel
            };

            _historyOriginContent = new Label
            {
                Text = "逍遥派创于北宋年间，由逍遥子所立。其武学博采众长，以道法自然为宗，讲究无为而无不为。门中弟子行事随性，不拘俗礼，然入门之难，冠绝江湖。逍遥子传有三大绝学：逍遥剑法轻灵飘逸，凌波微步出神入化，北冥神功深不可测，三者皆需悟性超绝者方可修成。",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                TextColor = InkWashTheme.TextOnPaper,
                Parent = _historyPanel
            };

            _historyRulesTitle = new Label
            {
                Text = "门派规矩",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 16f),
                TextColor = InkWashTheme.TextOnPaper,
                Parent = _historyPanel
            };

            _historyRules = new List<Label>();
            string[] rules = {
                "一、不得以门派武学欺凌弱小，违者逐出师门。",
                "二、每月须完成三件门派日常，否则扣减贡献度。",
                "三、同门之间切磋武艺，点到为止，不可伤及性命。",
                "四、门派秘籍不可外传，违者废除武功，逐出门墙。"
            };
            for (int i = 0; i < rules.Length; i++)
            {
                Label rule = new Label
                {
                    Text = rules[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                    TextColor = InkWashTheme.TextOnPaper,
                    Parent = _historyPanel
                };
                _historyRules.Add(rule);
            }
        }

        private void ApplyLayout()
        {
            float sw = _screenSize.X;
            float sh = _screenSize.Y;

            if (_sidebar != null)
            {
                _sidebar.Location = Float2.Zero;
                _sidebar.Size = new Float2(SidebarWidth, sh);
            }

            float sidebarLogoY = (TopHeaderHeight - 33f) * 0.5f;
            if (_sidebarLogo != null)
            {
                _sidebarLogo.Location = new Float2(20f, sidebarLogoY);
                // 品牌改成「混沌世界」（4 字）后 20px Display 约 96px 宽：宽度与副标起点都要跟着放大，
                // 否则品牌被裁成两字、副标压在它身上。
                _sidebarLogo.Size = new Float2(100f, 33f);
            }
            if (_sidebarLogoSub != null)
            {
                _sidebarLogoSub.Location = new Float2(20f + 100f + 8f, sidebarLogoY + 15f);
                _sidebarLogoSub.Size = new Float2(90f, 16f);
            }

            _sidebarList.Layout(0f, TopHeaderHeight + 16f, SidebarWidth,
                Mathf.Max(0f, sh - TopHeaderHeight - 16f));

            if (_mainArea != null)
            {
                _mainArea.Location = new Float2(SidebarWidth, 0f);
                _mainArea.Size = new Float2(sw - SidebarWidth, sh);
            }

            if (_topHeader != null)
            {
                _topHeader.Location = Float2.Zero;
                _topHeader.Size = new Float2(sw - SidebarWidth, TopHeaderHeight);
            }

            float headerX = 20f;
            float headerCenterY = TopHeaderHeight * 0.5f;
            if (_backButton != null)
            {
                _backButton.Location = new Float2(headerX, headerCenterY - 18f);
            }
            headerX += 110f;
            if (_playerAvatar != null)
            {
                _playerAvatar.Location = new Float2(headerX, headerCenterY - 18f);
            }
            headerX += 46f;
            if (_playerNameLabel != null)
            {
                _playerNameLabel.Location = new Float2(headerX, headerCenterY - 20f);
            }
            if (_playerLevelLabel != null)
            {
                _playerLevelLabel.Location = new Float2(headerX + 120f, headerCenterY - 18f);
            }
            if (_playerSectLabel != null)
            {
                _playerSectLabel.Location = new Float2(headerX + 170f, headerCenterY - 16f);
            }

            float rightX = (sw - SidebarWidth) - 180f;
            if (_goldLabel != null)
            {
                _goldLabel.Location = new Float2(rightX, headerCenterY - 16f);
            }
            rightX += 80f;
            if (_jadeLabel != null)
            {
                _jadeLabel.Location = new Float2(rightX, headerCenterY - 16f);
            }

            float contentY = TopHeaderHeight + 32f;
            if (_pageTitle != null)
            {
                _pageTitle.Location = new Float2(32f, contentY);
            }

            if (_verticalPageTitle != null)
            {
                _verticalPageTitle.Location = new Float2(8f, contentY + 30f);
                _verticalPageTitle.Size = new Float2(20f, 90f);
            }
            contentY += 50f;

            float listW = (sw - SidebarWidth - 32f * 2f) * SectListRatio;
            float detailW = (sw - SidebarWidth - 32f * 2f) * SectDetailRatio;
            float listX = 32f;
            float detailX = listX + listW + 20f;
            float contentH = sh - contentY - 32f;

            if (_sectListSection != null)
            {
                _sectListSection.Location = new Float2(listX, contentY);
                _sectListSection.Size = new Float2(listW, contentH);
            }

            float listItemY = 0f;
            foreach (var item in _sectListItems)
            {
                item.Location = new Float2(0f, listItemY);
                item.Size = new Float2(listW, 56f);
                listItemY += 58f;
            }

            for (int idx = 0; idx < _sectListItems.Count; idx++)
            {
                var item = _sectListItems[idx];

                // 子控件顺序固定：0=徽记底(ContainerControl) 1=门派名 2=描述 3=加入状态。
                // 旧实现只认 InkAvatar 才给徽记让位，而徽记底是 ContainerControl ⇒ 名字/描述都从 x=12 起、
                // y 只差 10px，两行文字叠在一起（2026-10-29 截图实测）。
                var kids = item.Children;
                const float emblemSize = 32f;
                const float emblemX = 12f;
                float textX = emblemX + emblemSize + 12f;
                float textW = Mathf.Max(40f, listW - textX - 70f);

                if (kids.Count > 0 && kids[0] != null)
                    kids[0].Bounds = new Rectangle(emblemX, 28f - emblemSize * 0.5f, emblemSize, emblemSize);

                if (kids.Count > 1 && kids[1] is Label nameLabel)
                    nameLabel.Bounds = new Rectangle(textX, 7f, textW, 20f);

                if (kids.Count > 2 && kids[2] is Label descLabel)
                    descLabel.Bounds = new Rectangle(textX, 29f, textW, 16f);

                if (kids.Count > 3 && kids[3] is Label statusLabel)
                    statusLabel.Bounds = new Rectangle(listW - 62f, 20f, 50f, 16f);
            }

            if (_sectDetailSection != null)
            {
                _sectDetailSection.Location = new Float2(detailX, contentY);
                _sectDetailSection.Size = new Float2(detailW, contentH);
            }

            float detailY = 0f;

            if (_sectInfoPanel != null)
            {
                _sectInfoPanel.Location = new Float2(0f, detailY);
                _sectInfoPanel.Size = new Float2(detailW, 200f);
            }

            float infoX = PanelPadding;
            float infoY = PanelPadding;
            if (_sectEmblem != null)
            {
                _sectEmblem.Location = new Float2(infoX, infoY);
            }
            infoX += 90f;
            if (_sectNameLabel != null)
            {
                _sectNameLabel.Location = new Float2(infoX, infoY);
            }
            if (_sectRankLabel != null)
            {
                _sectRankLabel.Location = new Float2(infoX + 150f, infoY + 10f);
            }
            infoY += 40f;
            if (_sectDescLabel != null)
            {
                _sectDescLabel.Location = new Float2(infoX, infoY);
                _sectDescLabel.Size = new Float2(detailW - PanelPadding * 2f - 90f, 40f);
            }

            infoY += 60f;
            if (_contributionTitleLabel != null)
            {
                _contributionTitleLabel.Location = new Float2(infoX, infoY - 2f);
                _contributionTitleLabel.Size = new Float2(120f, 18f);
            }
            if (_contributionLabel != null)
            {
                _contributionLabel.Location = new Float2(detailW - PanelPadding - 100f, infoY);
            }
            infoY += 20f;
            if (_contributionBar != null)
            {
                _contributionBar.Location = new Float2(infoX, infoY);
                _contributionBar.Size = new Float2(detailW - PanelPadding * 2f - 90f, 8f);
            }
            infoY += 16f;
            if (_contributionHintLabel != null)
            {
                _contributionHintLabel.Location = new Float2(infoX, infoY);
            }

            detailY += 210f;
            if (_skillTreePanel != null)
            {
                _skillTreePanel.Location = new Float2(0f, detailY);
                _skillTreePanel.Size = new Float2(detailW, 380f);
            }

            infoX = PanelPadding;
            infoY = PanelPadding;
            if (_skillTreeTitle != null)
            {
                _skillTreeTitle.Location = new Float2(infoX, infoY);
            }
            if (_skillTreeCountLabel != null)
            {
                _skillTreeCountLabel.Location = new Float2(detailW - PanelPadding - 60f, infoY + 4f);
            }

            infoY += 40f;
            float branchY = infoY;
            float branchW = detailW - PanelPadding * 2f;
            for (int branchIdx = 0; branchIdx < _skillBranches.Count; branchIdx++)
            {
                var branch = _skillBranches[branchIdx];
                branch.Location = new Float2(infoX, branchY);
                branch.Size = new Float2(branchW, 100f);

                // 子控件顺序固定：0=分支名，1=已学计数，其后都是节点。按顺序摆，不按文案猜。
                var children = branch.Children;
                // 两列节点只占左半边：列宽封顶 340，不让它们被拉成横跨整行的稀疏排布
                float nodeW = Mathf.Min(340f, (branchW - 8f) * 0.5f);
                if (children.Count > 0 && children[0] is Label branchTitle)
                {
                    branchTitle.Location = new Float2(0f, 0f);
                    branchTitle.Size = new Float2(Mathf.Max(80f, branchW - 90f), 22f);
                    branchTitle.HorizontalAlignment = TextAlignment.Near;
                }
                if (children.Count > 1 && children[1] is Label branchCount)
                {
                    branchCount.Location = new Float2(nodeW * 2f + 12f, 3f);
                    branchCount.Size = new Float2(86f, 18f);
                }

                int nodeIndex = 0;
                for (int i = 2; i < children.Count; i++)
                {
                    if (children[i] is not Panel node)
                        continue;

                    int col = nodeIndex % 2;
                    int row = nodeIndex / 2;
                    node.Location = new Float2(col * (nodeW + 8f), 28f + row * 34f);
                    node.Size = new Float2(nodeW, 32f);
                    nodeIndex++;

                    // 节点子控件顺序固定：0=圆形图标，1=节点名，2=修习状态。按序摆，避免两个 Label 都命中同一分支
                    var nodeChildren = node.Children;
                    if (nodeChildren.Count > 0)
                    {
                        nodeChildren[0].Location = new Float2(0f, 4f);
                        nodeChildren[0].Size = new Float2(32f, 24f);
                    }
                    float nameW = Mathf.Max(60f, nodeW - 32f - 64f);
                    if (nodeChildren.Count > 1)
                    {
                        nodeChildren[1].Location = new Float2(36f, 5f);
                        nodeChildren[1].Size = new Float2(nameW, 18f);
                    }
                    if (nodeChildren.Count > 2)
                    {
                        nodeChildren[2].Location = new Float2(36f + nameW, 7f);
                        nodeChildren[2].Size = new Float2(58f, 16f);
                    }
                }

                branchY += 110f;
            }

            detailY += 390f;
            if (_dailyTasksPanel != null)
            {
                _dailyTasksPanel.Location = new Float2(0f, detailY);
                _dailyTasksPanel.Size = new Float2(detailW, 200f);
            }

            infoX = PanelPadding;
            infoY = PanelPadding;
            if (_dailyTasksTitle != null)
            {
                _dailyTasksTitle.Location = new Float2(infoX, infoY);
            }
            if (_dailyTasksRefreshLabel != null)
            {
                _dailyTasksRefreshLabel.Location = new Float2(detailW - PanelPadding - 100f, infoY + 4f);
            }

            infoY += 40f;
            float taskY = infoY;
            float rowW = detailW - PanelPadding * 2f;
            foreach (var task in _dailyTaskItems)
            {
                if (task?.Item == null)
                    continue;

                task.Item.Location = new Float2(infoX, taskY);
                task.Item.Size = new Float2(rowW, 52f);

                if (task.Icon != null)
                    task.Icon.Location = new Float2(0f, 10f);
                if (task.Name != null)
                {
                    task.Name.Location = new Float2(40f, 8f);
                    task.Name.Size = new Float2(96f, 18f);
                }
                if (task.Status != null)
                {
                    task.Status.Location = new Float2(40f, 28f);
                    task.Status.Size = new Float2(96f, 14f);
                }
                if (task.Desc != null)
                {
                    task.Desc.Location = new Float2(144f, 8f);
                    task.Desc.Size = new Float2(Mathf.Max(120f, rowW - 264f), 16f);
                }
                if (task.Progress != null)
                {
                    task.Progress.Location = new Float2(rowW - 112f, 8f);
                    task.Progress.Size = new Float2(60f, 18f);
                }
                if (task.Reward != null)
                {
                    task.Reward.Location = new Float2(rowW - 112f, 28f);
                    task.Reward.Size = new Float2(100f, 14f);
                }

                taskY += 56f;
            }


            detailY += 210f;
            if (_historyPanel != null)
            {
                _historyPanel.Location = new Float2(0f, detailY);
                _historyPanel.Size = new Float2(detailW, 340f);
            }

            infoX = PanelPadding;
            infoY = PanelPadding;
            if (_historyOriginTitle != null)
            {
                _historyOriginTitle.Location = new Float2(infoX + 40f, infoY);
            }
            infoY += 30f;
            if (_historyOriginContent != null)
            {
                _historyOriginContent.Location = new Float2(infoX, infoY);
                _historyOriginContent.Size = new Float2(detailW - PanelPadding * 2f, 120f);
                _historyOriginContent.Wrapping = TextWrapping.WrapWords;
            }

            infoY += 150f;
            if (_historyRulesTitle != null)
            {
                _historyRulesTitle.Location = new Float2(infoX, infoY);
            }

            infoY += 24f;
            foreach (var rule in _historyRules)
            {
                rule.Location = new Float2(infoX, infoY);
                infoY += 24f;
            }
        }

        private void RefreshAllData()
        {
        }

        private void OnNavClicked(string path)
        {
            NavigationRequested?.Invoke(path);
        }

        private void OnBackToHud()
        {
            NavigationRequested?.Invoke(InkPageDomIds.CombatHud);
        }

        public void RefreshLayout()
        {
            _screenSize = new Float2(Width, Height);
            ApplyLayout();
        }
    }
}
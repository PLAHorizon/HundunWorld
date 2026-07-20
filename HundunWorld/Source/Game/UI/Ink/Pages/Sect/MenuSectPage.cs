using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.StyleSystem;
using System;
using System.Collections.Generic;

namespace HundunWorld.Game.UI.Ink.Pages.Sect
{
    public class MenuSectPage : ContainerControl, IInkPage
    {
        private Float2 _screenSize;

        private Panel _backgroundLayer;
        private Panel _vignette;

        private Panel _sidebar;
        private Label _sidebarLogo;
        private Label _sidebarLogoSub;
        private List<InkButton> _sidebarNavButtons;

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

        private Panel _sectListSection;
        private List<ContainerControl> _sectListItems;

        private Panel _sectDetailSection;
        private InkPanel _sectInfoPanel;
        private ContainerControl _sectEmblem;
        private Label _sectNameLabel;
        private Label _sectRankLabel;
        private Label _sectDescLabel;
        private Label _contributionLabel;
        private InkBar _contributionBar;
        private Label _contributionHintLabel;

        private InkPanel _skillTreePanel;
        private Label _skillTreeTitle;
        private Label _skillTreeCountLabel;
        private List<Panel> _skillBranches;

        private InkPanel _dailyTasksPanel;
        private Label _dailyTasksTitle;
        private Label _dailyTasksRefreshLabel;
        private List<Panel> _dailyTaskItems;

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
        private string[] _sectDescs = { "逍遥自在 · 已加入", "外家正宗 · 未加入", "内家太极 · 未加入", "剑法灵动 · 未加入", "剑气冲霄 · 未加入", "降龙掌法 · 未加入", "暗器机关 · 未加入", "先天功法 · 未加入" };
        private bool[] _sectJoined = { true, false, false, false, false, false, false, false };
        private string[] _sectRanks = { "内门弟子", "", "", "", "", "", "", "" };
        private int[] _sectContributions = { 3200, 0, 0, 0, 0, 0, 0, 0 };

        private string[] _skillBranchNames = { "逍遥剑法", "凌波微步", "北冥神功" };
        private int[] _skillBranchLearned = { 3, 2, 1 };
        private int[] _skillBranchTotal = { 4, 4, 4 };
        private string[][] _skillNodeNames = {
            new[] { "逍遥剑意", "剑气纵横", "万剑归宗", "天外飞仙" },
            new[] { "步法入门", "踏雪无痕", "飞花逐影", "凌空虚渡" },
            new[] { "吐纳归元", "北冥吸功", "真气化海", "逍遥御风" }
        };
        private string[][] _skillNodeLevels = {
            new[] { "Lv.5", "Lv.3", "Lv.2", "可学习" },
            new[] { "Lv.4", "Lv.3", "可学习", "未解锁" },
            new[] { "Lv.3", "可学习", "未解锁", "未解锁" }
        };
        private bool[][] _skillNodeLearned = {
            new[] { true, true, true, false },
            new[] { true, true, false, false },
            new[] { true, false, false, false }
        };

        private string[] _dailyTaskNames = { "门派巡逻", "传功解惑", "门派试炼" };
        private string[] _dailyTaskDescs = { "巡视门派领地，驱逐入侵之敌", "指导门派新弟子修炼基础功法", "通过门派武学考核，证明实力" };
        private string[] _dailyTaskStatus = { "进行中", "进行中", "未开始" };
        private string[] _dailyTaskProgress = { "2 / 5", "1 / 3", "0 / 1" };
        private string[] _dailyTaskRewards = { "贡献 +200", "贡献 +150", "贡献 +500" };

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
                Text = "燕云",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 20f),
                TextColor = InkWashTheme.GoldBright,
                Parent = _sidebar
            };

            _sidebarLogoSub = new Label
            {
                Text = "十六声",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.PaperAged,
                Parent = _sidebar
            };

            _sidebarNavButtons = new List<InkButton>();
            string[] navItems = { "任务", "博物志", "武林录", "营生", "组队", "邮箱", "商店", "门派", "装备", "设置" };
            string[] navPaths = { "menu-quests", "menu-bestiary", "menu-martial-record", "menu-livelihood", "menu-team", "menu-mail", "menu-shop", "menu-sect", "menu-equipment", "settings" };

            for (int i = 0; i < navItems.Length; i++)
            {
                bool isActive = navItems[i] == "门派";
                InkButton btn = new InkButton
                {
                    Text = navItems[i],
                    Height = 36,
                    BackgroundColor = isActive ? InkWashTheme.GoldPrimary * 0.1f : Color.Transparent,
                    TextColor = isActive ? InkWashTheme.GoldBright : InkWashTheme.PaperAged,
                    Parent = _sidebar
                };
                btn.Clicked += () => OnNavClicked(navPaths[i]);
                _sidebarNavButtons.Add(btn);
            }
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
                AnchorPreset = AnchorPresets.StretchAll,
                Parent = _playerAvatar
            };

            _playerNameLabel = new Label
            {
                Text = "无名侠",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 14f),
                TextColor = InkWashTheme.TextDefault,
                Parent = _topHeader
            };

            _playerLevelLabel = new Label
            {
                Text = "Lv.42",
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
                Text = "12,450",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 11f),
                TextColor = InkWashTheme.TextDefault,
                Parent = _topHeader
            };

            _jadeLabel = new Label
            {
                Text = "328",
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
                    AnchorPreset = AnchorPresets.StretchAll,
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
                AnchorPreset = AnchorPresets.StretchAll,
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

            _contributionLabel = new Label
            {
                Text = "3,200 / 5,000",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 11f),
                TextColor = InkWashTheme.GoldBright,
                Parent = _sectInfoPanel
            };

            _contributionBar = new InkBar
            {
                Value = 0.64f,
                Height = 8f,
                Parent = _sectInfoPanel
            };

            _contributionHintLabel = new Label
            {
                Text = "距下一职位「核心弟子」还需 1,800 贡献度",
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
                Text = "已学 6 / 12",
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

            _dailyTaskItems = new List<Panel>();
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

                _dailyTaskItems.Add(item);
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
            }
            if (_sidebarLogoSub != null)
            {
                _sidebarLogoSub.Location = new Float2(20f + 22f * 2f + 6f, sidebarLogoY + 11f);
            }

            float navY = TopHeaderHeight + 16f;
            foreach (var btn in _sidebarNavButtons)
            {
                btn.Location = new Float2(12f, navY);
                btn.Size = new Float2(SidebarWidth - 24f, 36f);
                navY += 38f;
            }

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
                float itemX = 12f;
                float itemCenterY = 28f;

                foreach (var child in item.Children)
                {
                    if (child is InkAvatar)
                    {
                        child.Location = new Float2(itemX, itemCenterY - 16f);
                        itemX += 40f;
                    }
                    else if (child is Label label)
                    {
                        if (label.Text == _sectNames[idx])
                        {
                            child.Location = new Float2(itemX, itemCenterY - 18f);
                        }
                        else if (_sectJoined[idx] && label.Text == $"内门弟子 · 贡献 {_sectContributions[idx]}")
                        {
                            child.Location = new Float2(itemX, itemCenterY - 8f);
                        }
                        else if (!_sectJoined[idx] && label.Text == _sectDescs[idx])
                        {
                            child.Location = new Float2(itemX, itemCenterY - 8f);
                        }
                        else if (label.Text == "已加入" || label.Text == "未加入")
                        {
                            child.Location = new Float2(listW - 60f, itemCenterY - 8f);
                        }
                    }
                }
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
                _skillTreePanel.Size = new Float2(detailW, 360f);
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
            for (int branchIdx = 0; branchIdx < _skillBranches.Count; branchIdx++)
            {
                var branch = _skillBranches[branchIdx];
                branch.Location = new Float2(infoX, branchY);
                branch.Size = new Float2(detailW - PanelPadding * 2f, 100f);

                float nodeY = 0f;
                foreach (var child in branch.Children)
                {
                    if (child is Label label && label.Text == _skillBranchNames[branchIdx])
                    {
                        child.Location = new Float2(0f, nodeY);
                    }
                    else if (child is Label label2 && label2.TextColor == InkWashTheme.GoldPrimary)
                    {
                        child.Location = new Float2(0f, nodeY + 20f);
                        nodeY += 30f;
                    }
                    else if (child is Panel node)
                    {
                        node.Location = new Float2(0f, nodeY);
                        node.Size = new Float2(detailW - PanelPadding * 2f, 32f);

                        float nodeX = 0f;
                        foreach (var nodeChild in node.Children)
                        {
                            if (nodeChild is Panel circle)
                            {
                                nodeChild.Location = new Float2(nodeX, 4f);
                                nodeX += 32f;
                            }
                            else if (nodeChild is Label name)
                            {
                                nodeChild.Location = new Float2(nodeX, 4f);
                                nodeX += 100f;
                            }
                            else if (nodeChild is Label level)
                            {
                                nodeChild.Location = new Float2(nodeX, 6f);
                            }
                        }
                        nodeY += 34f;
                    }
                }

                branchY += 110f;
            }

            detailY += 370f;
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
            foreach (var task in _dailyTaskItems)
            {
                task.Location = new Float2(infoX, taskY);
                task.Size = new Float2(detailW - PanelPadding * 2f, 52f);

                float taskX = 0f;
                foreach (var child in task.Children)
                {
                    if (child is Panel icon)
                    {
                        child.Location = new Float2(taskX, 10f);
                        taskX += 40f;
                    }
                    else if (child is Label label)
                    {
                        if (_dailyTaskNames.Contains(label.Text) && label.TextColor == InkWashTheme.PaperBright)
                        {
                            child.Location = new Float2(taskX, 8f);
                            taskX += 100f;
                        }
                        else if (_dailyTaskDescs.Contains(label.Text))
                        {
                            if (taskX < 140f)
                            {
                                child.Location = new Float2(taskX, 10f);
                                taskX += 50f;
                            }
                            else if (taskX < 190f)
                            {
                                child.Location = new Float2(taskX, 30f);
                                taskX += 150f;
                            }
                            else
                            {
                                child.Location = new Float2(detailW - PanelPadding * 2f - 60f, 30f);
                            }
                        }
                        else if (_dailyTaskStatus.Contains(label.Text) && label.TextColor == InkWashTheme.GoldBright)
                        {
                            child.Location = new Float2(detailW - PanelPadding * 2f - 50f, 8f);
                        }
                    }
                }

                taskY += 56f;
            }

            detailY += 210f;
            if (_historyPanel != null)
            {
                _historyPanel.Location = new Float2(0f, detailY);
                _historyPanel.Size = new Float2(detailW, 280f);
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
                _historyOriginContent.Size = new Float2(detailW - PanelPadding * 2f, 80f);
            }

            infoY += 100f;
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
            NavigationRequested?.Invoke("combat-hud");
        }

        public void RefreshLayout()
        {
            _screenSize = new Float2(Width, Height);
            ApplyLayout();
        }
    }
}
using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.StyleSystem;
using System;

namespace HundunWorld.Game.UI.Ink.Pages
{
    public class MenuQuestsPage : ContainerControl, IInkPage
    {
        private const float LeftNavWidth = 240f;
        private const float TopBarHeight = 60f;
        private const float QuestListWidthRatio = 0.4f;

        private InkPanel _leftNavPanel;
        private InkPanelSolid _topBarPanel;
        private InkPanelSolid _questListPanel;
        private InkPanel _questDetailPanel;
        private InkPaperPanel _storyPanel;

        private InkTextBlock _navVerticalTitle;
        private InkTextBlock _topBarName;
        private InkTag _topBarLevel;
        private InkTextBlock _topBarSect;
        private InkTextBlock _topBarCopper;
        private InkTextBlock _topBarSilver;

        private InkTextBlock _listTitle;
        private InkTextBlock _listCount;
        private TabButton[] _tabButtons;
        private QuestEntry[] _questEntries;

        private InkTextBlock _detailTitle;
        private InkTag _detailStatus;
        private InkTextBlock _detailLevel;
        private InkTextBlock _detailDifficulty;
        private InkTextBlock _detailType;

        private InkTextBlock _storyTitle;
        private InkTextBlock _storyContent;

        private InkTextBlock _objectiveTitle;
        private ObjectiveItem[] _objectiveItems;

        private InkTextBlock _rewardTitle;
        private RewardItem[] _rewardItems;

        private int _selectedTab = 0;
        private int _selectedQuest = 5;

        private Float2 _screenSize;

        public MenuQuestsPage()
        {
            _screenSize = FlaxEngine.Screen.Size;
            if (_screenSize.X <= 0f || _screenSize.Y <= 0f)
            {
                _screenSize = new Float2(1920f, 1080f);
            }

            AnchorPreset = AnchorPresets.StretchAll;
            BackgroundColor = new Color(InkWashTheme.BaseDefault.R, InkWashTheme.BaseDefault.G, InkWashTheme.BaseDefault.B, 0.55f);
            ClipChildren = false;
            AutoFocus = false;
            Location = Float2.Zero;
            Size = _screenSize;

            try
            {
                BuildLeftNavigation();
                BuildTopBar();
                BuildQuestList();
                BuildQuestDetail();

                ApplyLayout();
                SelectQuest(_selectedQuest);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[MenuQuestsPage] 初始化失败: {ex.Message}");
            }
        }

        private void BuildLeftNavigation()
        {
            _leftNavPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
            };
            AddChild(_leftNavPanel);

            _navVerticalTitle = new InkTextBlock(InkTextStyle.Display)
            {
                Text = "任务",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(100f, 24f),
                Size = new Float2(40f, 100f),
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 28f),
                TextColor = new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.5f),
            };
            _leftNavPanel.AddChild(_navVerticalTitle);

            string[] navItems = { "角色", "装备", "外观", "备战", "门派", "个人信息", "时间", "", "任务", "博物志", "武林录", "营生", "组队", "邮箱", "商店", "", "设置" };
            bool[] navActive = { false, false, false, false, false, false, false, false, true, false, false, false, false, false, false, false, false };

            for (int i = 0; i < navItems.Length; i++)
            {
                if (string.IsNullOrEmpty(navItems[i]))
                    continue;

                var item = new InkListItem
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(0f, 100f + i * 32f),
                    Size = new Float2(LeftNavWidth, 32f),
                    Active = navActive[i],
                };

                var label = new InkTextBlock(InkTextStyle.Body)
                {
                    Text = navItems[i],
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(24f, 0f),
                    Size = new Float2(LeftNavWidth - 24f, 32f),
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 15f),
                    TextColor = navActive[i] ? InkWashTheme.TextDefault : InkWashTheme.TextSecondary,
                };
                item.AddChild(label);
                _leftNavPanel.AddChild(item);
            }
        }

        private void BuildTopBar()
        {
            _topBarPanel = new InkPanelSolid
            {
                AnchorPreset = AnchorPresets.TopLeft,
            };
            AddChild(_topBarPanel);

            InkPanel avatarPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(LeftNavWidth + 32f, 10f),
                Size = new Float2(40f, 40f),
                BackgroundColor = InkWashTheme.BaseTertiary,
            };
            _topBarPanel.AddChild(avatarPanel);

            InkTextBlock avatarText = new InkTextBlock(InkTextStyle.Display)
            {
                Text = "无",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Float2(40f, 40f),
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 18f),
                TextColor = InkWashTheme.PaperBright,
            };
            avatarPanel.AddChild(avatarText);

            _topBarName = new InkTextBlock(InkTextStyle.Heading)
            {
                Text = "无名侠",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(LeftNavWidth + 80f, 16f),
                Size = new Float2(120f, 28f),
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 18f),
                TextColor = InkWashTheme.PaperBright,
            };
            _topBarPanel.AddChild(_topBarName);

            _topBarLevel = new InkTag
            {
                TagVariant = InkTagVariant.Brand,
                Text = "Lv.42",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(LeftNavWidth + 210f, 18f),
                Size = new Float2(56f, 24f),
            };
            _topBarPanel.AddChild(_topBarLevel);

            _topBarSect = new InkTextBlock(InkTextStyle.Body)
            {
                Text = "逍遥派",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(LeftNavWidth + 274f, 20f),
                Size = new Float2(80f, 20f),
                TextColor = InkWashTheme.GoldPrimary,
            };
            _topBarPanel.AddChild(_topBarSect);

            _topBarCopper = new InkTextBlock(InkTextStyle.Number)
            {
                Text = "8,320",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(_screenSize.X - 180f, 20f),
                Size = new Float2(80f, 20f),
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 13f),
                TextColor = InkWashTheme.PaperBright,
            };
            _topBarPanel.AddChild(_topBarCopper);

            InkTextBlock copperLabel = new InkTextBlock(InkTextStyle.Caption)
            {
                Text = "铜钱",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(_screenSize.X - 240f, 22f),
                Size = new Float2(50f, 16f),
                TextColor = InkWashTheme.PaperAged,
            };
            _topBarPanel.AddChild(copperLabel);

            _topBarSilver = new InkTextBlock(InkTextStyle.Number)
            {
                Text = "328",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(_screenSize.X - 80f, 20f),
                Size = new Float2(60f, 20f),
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 13f),
                TextColor = InkWashTheme.GoldBright,
            };
            _topBarPanel.AddChild(_topBarSilver);

            InkTextBlock silverLabel = new InkTextBlock(InkTextStyle.Caption)
            {
                Text = "银两",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(_screenSize.X - 130f, 22f),
                Size = new Float2(40f, 16f),
                TextColor = InkWashTheme.GoldPrimary,
            };
            _topBarPanel.AddChild(silverLabel);
        }

        private void BuildQuestList()
        {
            _questListPanel = new InkPanelSolid
            {
                AnchorPreset = AnchorPresets.TopLeft,
            };
            AddChild(_questListPanel);

            InkPanel listHeader = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 0f),
                Size = new Float2(1f, 48f),
                BackgroundColor = Color.Transparent,
            };
            _questListPanel.AddChild(listHeader);

            _listTitle = new InkTextBlock(InkTextStyle.Subheading)
            {
                Text = "任务卷宗",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(20f, 12f),
                Size = new Float2(120f, 24f),
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 15f),
                TextColor = InkWashTheme.GoldPrimary,
            };
            listHeader.AddChild(_listTitle);

            _listCount = new InkTextBlock(InkTextStyle.Caption)
            {
                Text = "6 项",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(1f - 60f, 14f),
                Size = new Float2(50f, 20f),
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 11f),
                TextColor = InkWashTheme.PaperAged,
            };
            listHeader.AddChild(_listCount);

            InkPanel tabsPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 48f),
                Size = new Float2(1f, 36f),
                BackgroundColor = InkWashTheme.BaseDefault,
            };
            _questListPanel.AddChild(tabsPanel);

            string[] tabs = { "主线", "支线", "世界", "日常" };
            _tabButtons = new TabButton[4];
            float tabWidth = 1f / 4f;
            for (int i = 0; i < 4; i++)
            {
                var tab = new TabButton(tabs[i], i)
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(i * tabWidth, 0f),
                    Size = new Float2(tabWidth, 36f),
                    Active = (i == 0),
                };
                tab.Clicked += OnTabClicked;
                _tabButtons[i] = tab;
                tabsPanel.AddChild(tab);
            }

            var quests = new[]
            {
                (name: "初入江湖", level: "Lv.1", status: "进行中", progress: 0.35f, category: 0),
                (name: "逍遥秘境", level: "Lv.15", status: "进行中", progress: 0.50f, category: 0),
                (name: "武林盟主", level: "Lv.40", status: "未开始", progress: 0f, category: 0),
                (name: "天山雪莲", level: "Lv.35", status: "进行中", progress: 0.75f, category: 0),
                (name: "暗影追踪", level: "Lv.28", status: "已完成", progress: 1f, category: 0),
                (name: "百年恩怨", level: "Lv.42", status: "进行中", progress: 0.80f, category: 0),
            };

            _questEntries = new QuestEntry[6];
            for (int i = 0; i < quests.Length; i++)
            {
                var quest = quests[i];
                var entry = new QuestEntry(quest.name, quest.level, quest.status, quest.progress, i)
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(0f, 84f + i * 60f),
                    Size = new Float2(1f, 60f),
                    Selected = (i == _selectedQuest),
                };
                entry.Clicked += OnQuestClicked;
                _questEntries[i] = entry;
                _questListPanel.AddChild(entry);
            }
        }

        private void BuildQuestDetail()
        {
            _questDetailPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
            };
            AddChild(_questDetailPanel);

            _detailTitle = new InkTextBlock(InkTextStyle.Display)
            {
                Text = "百年恩怨",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(32f, 32f),
                Size = new Float2(400f, 40f),
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 32f),
                TextColor = InkWashTheme.TextBrand,
            };
            _questDetailPanel.AddChild(_detailTitle);

            // 任务状态"进行中"用 Jade 系（设计方案 §3.8 进行中=jade-primary）
            _detailStatus = new InkTag
            {
                TagVariant = InkTagVariant.Default,
                Text = "进行中",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(1f - 100f, 40f),
                Size = new Float2(80f, 24f),
                TextColor = InkWashTheme.JadePrimary,
            };
            _questDetailPanel.AddChild(_detailStatus);

            _detailLevel = new InkTextBlock(InkTextStyle.Body)
            {
                Text = "推荐等级：Lv.42",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(32f, 84f),
                Size = new Float2(160f, 20f),
                TextColor = InkWashTheme.PaperAged,
            };
            _questDetailPanel.AddChild(_detailLevel);

            _detailDifficulty = new InkTextBlock(InkTextStyle.Body)
            {
                Text = "难度：★★★★★",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(200f, 84f),
                Size = new Float2(160f, 20f),
                TextColor = InkWashTheme.GoldBright,
            };
            _questDetailPanel.AddChild(_detailDifficulty);

            _detailType = new InkTextBlock(InkTextStyle.Body)
            {
                Text = "类型：主线",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(368f, 84f),
                Size = new Float2(100f, 20f),
                TextColor = InkWashTheme.PaperBright,
            };
            _questDetailPanel.AddChild(_detailType);

            InkDivider divider1 = new InkDivider
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(32f, 120f),
                Size = new Float2(1f - 64f, 1f),
            };
            _questDetailPanel.AddChild(divider1);

            _storyPanel = new InkPaperPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(32f, 136f),
                Size = new Float2(1f - 64f, 120f),
            };
            _questDetailPanel.AddChild(_storyPanel);

            InkCornerDeco storyCorners = new InkCornerDeco
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = _storyPanel.Size,
            };
            _storyPanel.AddChild(storyCorners);

            _storyTitle = new InkTextBlock(InkTextStyle.Subheading)
            {
                Text = "任务背景",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(20f, 16f),
                Size = new Float2(120f, 24f),
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 16f),
                TextColor = InkWashTheme.TextOnPaper,
            };
            _storyPanel.AddChild(_storyTitle);

            _storyContent = new InkTextBlock(InkTextStyle.Body)
            {
                Text = "百年前，江湖中一场惊天动地的恩怨纠葛埋下祸根。如今你无意间得到一封泛黄的血书，揭开了一段尘封已久的往事。李沧海老人在洛阳城东的茶馆中苦苦等待有缘人，他将告诉你一段关于师门血仇的真相。太行山巅风雪依旧，仇敌赵无极盘踞已久，而你将成为这段百年恩怨的终结者。",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(20f, 48f),
                Size = new Float2(1f - 64f - 40f, 60f),
                VerticalAlignment = TextAlignment.Near,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                TextColor = InkWashTheme.TextOnPaper,
            };
            _storyPanel.AddChild(_storyContent);

            InkDivider divider2 = new InkDivider
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(32f, 272f),
                Size = new Float2(1f - 64f, 1f),
            };
            _questDetailPanel.AddChild(divider2);

            _objectiveTitle = new InkTextBlock(InkTextStyle.Subheading)
            {
                Text = "任务目标",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(32f, 288f),
                Size = new Float2(120f, 24f),
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 16f),
                TextColor = InkWashTheme.GoldPrimary,
            };
            _questDetailPanel.AddChild(_objectiveTitle);

            _objectiveItems = new ObjectiveItem[3];
            string[] objectives = { "前往洛阳城东茶馆，寻找老者李沧海", "聆听血书往事，了解百年前师门恩怨", "前往太行山巅，击败仇敌赵无极" };
            bool[] objectiveDone = { true, true, false };
            for (int i = 0; i < 3; i++)
            {
                var item = new ObjectiveItem(objectives[i], objectiveDone[i])
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(32f, 320f + i * 28f),
                    Size = new Float2(1f - 64f, 28f),
                };
                _objectiveItems[i] = item;
                _questDetailPanel.AddChild(item);
            }

            InkDivider divider3 = new InkDivider
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(32f, 410f),
                Size = new Float2(1f - 64f, 1f),
            };
            _questDetailPanel.AddChild(divider3);

            _rewardTitle = new InkTextBlock(InkTextStyle.Subheading)
            {
                Text = "任务奖励",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(32f, 426f),
                Size = new Float2(120f, 24f),
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 16f),
                TextColor = InkWashTheme.GoldPrimary,
            };
            _questDetailPanel.AddChild(_rewardTitle);

            _rewardItems = new RewardItem[4];
            string[] rewardLabels = { "经验", "铜钱", "装备", "声望" };
            string[] rewardValues = { "12,000", "5,000", "玄铁剑", "200" };
            // 奖励色：经验=金、铜钱=宣纸白、装备=品质鎏金、声望=翡翠（设计方案禁止非战斗场景用朱红）
            Color[] rewardColors = { InkWashTheme.GoldPrimary, InkWashTheme.PaperBright, InkWashTheme.QualityLegendary, InkWashTheme.JadeBright };
            for (int i = 0; i < 4; i++)
            {
                var item = new RewardItem(rewardLabels[i], rewardValues[i], rewardColors[i])
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(32f + i * ((1f - 64f) / 4f), 460f),
                    Size = new Float2((1f - 64f) / 4f - 8f, 60f),
                };
                _rewardItems[i] = item;
                _questDetailPanel.AddChild(item);
            }
        }

        private void OnTabClicked(int index)
        {
            _selectedTab = index;
            for (int i = 0; i < _tabButtons.Length; i++)
            {
                _tabButtons[i].Active = (i == index);
            }
        }

        private void OnQuestClicked(int index)
        {
            SelectQuest(index);
        }

        private void SelectQuest(int index)
        {
            _selectedQuest = index;
            for (int i = 0; i < _questEntries.Length; i++)
            {
                _questEntries[i].Selected = (i == index);
            }

            var quests = new[]
            {
                (name: "初入江湖", level: "Lv.1", status: "进行中", difficulty: "★★★"),
                (name: "逍遥秘境", level: "Lv.15", status: "进行中", difficulty: "★★★★"),
                (name: "武林盟主", level: "Lv.40", status: "未开始", difficulty: "★★★★★"),
                (name: "天山雪莲", level: "Lv.35", status: "进行中", difficulty: "★★★★"),
                (name: "暗影追踪", level: "Lv.28", status: "已完成", difficulty: "★★★"),
                (name: "百年恩怨", level: "Lv.42", status: "进行中", difficulty: "★★★★★"),
            };

            var quest = quests[index];
            _detailTitle.Text = quest.name;
            _detailStatus.Text = quest.status;
            _detailLevel.Text = $"推荐等级：{quest.level}";
            _detailDifficulty.Text = $"难度：{quest.difficulty}";
        }

        private void ApplyLayout()
        {
            float sw = _screenSize.X;
            float sh = _screenSize.Y;

            if (_leftNavPanel != null)
            {
                _leftNavPanel.Location = new Float2(0f, 0f);
                _leftNavPanel.Size = new Float2(LeftNavWidth, sh);
            }

            if (_topBarPanel != null)
            {
                _topBarPanel.Location = new Float2(LeftNavWidth, 0f);
                _topBarPanel.Size = new Float2(sw - LeftNavWidth, TopBarHeight);
            }

            if (_topBarCopper != null)
            {
                _topBarCopper.Location = new Float2(sw - 180f, 20f);
            }
            if (_topBarSilver != null)
            {
                _topBarSilver.Location = new Float2(sw - 80f, 20f);
            }

            float listWidth = (sw - LeftNavWidth) * QuestListWidthRatio;
            float detailWidth = (sw - LeftNavWidth) * (1f - QuestListWidthRatio);

            if (_questListPanel != null)
            {
                _questListPanel.Location = new Float2(LeftNavWidth, TopBarHeight);
                _questListPanel.Size = new Float2(listWidth, sh - TopBarHeight);
            }

            if (_listCount != null)
            {
                _listCount.Location = new Float2(listWidth - 60f, 14f);
            }

            if (_tabButtons != null)
            {
                float tabWidth = listWidth / 4f;
                for (int i = 0; i < _tabButtons.Length; i++)
                {
                    _tabButtons[i].Size = new Float2(tabWidth, 36f);
                }
            }

            if (_questEntries != null)
            {
                for (int i = 0; i < _questEntries.Length; i++)
                {
                    _questEntries[i].Size = new Float2(listWidth, 60f);
                }
            }

            if (_questDetailPanel != null)
            {
                _questDetailPanel.Location = new Float2(LeftNavWidth + listWidth, TopBarHeight);
                _questDetailPanel.Size = new Float2(detailWidth, sh - TopBarHeight);
            }

            if (_detailStatus != null)
            {
                _detailStatus.Location = new Float2(detailWidth - 100f, 40f);
            }

            if (_storyPanel != null)
            {
                _storyPanel.Size = new Float2(detailWidth - 64f, 120f);
            }

            if (_storyContent != null)
            {
                _storyContent.Size = new Float2(detailWidth - 64f - 40f, 60f);
            }

            if (_rewardItems != null)
            {
                float rewardWidth = (detailWidth - 64f) / 4f - 8f;
                for (int i = 0; i < _rewardItems.Length; i++)
                {
                    _rewardItems[i].Location = new Float2(32f + i * (rewardWidth + 8f), 460f);
                    _rewardItems[i].Size = new Float2(rewardWidth, 60f);
                }
            }
        }

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

        private class TabButton : ContainerControl
        {
            private string _text;
            private int _index;
            private bool _active;
            private InkTextBlock _label;

            public event Action<int> Clicked;

            public bool Active
            {
                get => _active;
                set
                {
                    _active = value;
                    if (_label != null)
                    {
                        _label.TextColor = _active ? InkWashTheme.GoldPrimary : InkWashTheme.TextSecondary;
                    }
                    BackgroundColor = _active ? new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.08f) : Color.Transparent;
                }
            }

            public TabButton(string text, int index)
            {
                _text = text;
                _index = index;
                ClipChildren = false;

                _label = new InkTextBlock(InkTextStyle.Body)
                {
                    Text = text,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = Float2.Zero,
                    Size = new Float2(1f, 36f),
                    HorizontalAlignment = TextAlignment.Center,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 14f),
                    TextColor = InkWashTheme.TextSecondary,
                };
                AddChild(_label);
            }

            public override bool OnMouseDown(Float2 location, MouseButton button)
            {
                base.OnMouseDown(location, button);
                if (button == MouseButton.Left)
                {
                    Clicked?.Invoke(_index);
                }
                return true;
            }
        }

        private class QuestEntry : ContainerControl
        {
            private string _name;
            private string _level;
            private string _status;
            private float _progress;
            private int _index;
            private bool _selected;
            private InkTextBlock _nameLabel;
            private InkTextBlock _levelLabel;
            private InkTag _statusTag;
            private InkBar _progressBar;
            private InkTextBlock _progressValue;

            public event Action<int> Clicked;

            public bool Selected
            {
                get => _selected;
                set
                {
                    _selected = value;
                    BackgroundColor = _selected ? new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.08f) : Color.Transparent;
                }
            }

            public QuestEntry(string name, string level, string status, float progress, int index)
            {
                _name = name;
                _level = level;
                _status = status;
                _progress = progress;
                _index = index;
                ClipChildren = false;

                _nameLabel = new InkTextBlock(InkTextStyle.Heading)
                {
                    Text = name,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(16f, 8f),
                    Size = new Float2(200f, 24f),
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 15f),
                    TextColor = InkWashTheme.PaperBright,
                };
                AddChild(_nameLabel);

                _levelLabel = new InkTextBlock(InkTextStyle.Number)
                {
                    Text = level,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(1f - 120f, 10f),
                    Size = new Float2(50f, 20f),
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 13f),
                    TextColor = InkWashTheme.PaperAged,
                };
                AddChild(_levelLabel);

                _statusTag = new InkTag
                {
                    TagVariant = status == "进行中" ? InkTagVariant.Brand : status == "已完成" ? InkTagVariant.Default : InkTagVariant.Default,
                    Text = status,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(1f - 60f, 10f),
                    Size = new Float2(50f, 22f),
                };
                if (status == "已完成")
                    _statusTag.TextColor = InkWashTheme.JadeBright;
                AddChild(_statusTag);

                _progressBar = new InkBar
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(16f, 38f),
                    Size = new Float2(1f - 80f, 4f),
                    Value = progress,
                };
                if (status == "已完成")
                    _progressBar.FillVariant = InkBarFillVariant.Jade;
                AddChild(_progressBar);

                _progressValue = new InkTextBlock(InkTextStyle.Caption)
                {
                    Text = $"{(int)(progress * 100f)}%",
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(1f - 50f, 36f),
                    Size = new Float2(40f, 18f),
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 11f),
                    TextColor = status == "已完成" ? InkWashTheme.JadeBright : InkWashTheme.PaperAged,
                };
                AddChild(_progressValue);
            }

            public override bool OnMouseDown(Float2 location, MouseButton button)
            {
                base.OnMouseDown(location, button);
                if (button == MouseButton.Left)
                {
                    Clicked?.Invoke(_index);
                }
                return true;
            }
        }

        private class ObjectiveItem : ContainerControl
        {
            private InkPanel _checkPanel;
            private InkTextBlock _text;

            public ObjectiveItem(string text, bool done)
            {
                ClipChildren = false;

                _checkPanel = new InkPanel
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(0f, 4f),
                    Size = new Float2(16f, 16f),
                    BackgroundColor = done ? InkWashTheme.JadePrimary : Color.Transparent,
                };
                AddChild(_checkPanel);

                if (done)
                {
                    InkTextBlock check = new InkTextBlock(InkTextStyle.Caption)
                    {
                        Text = "✓",
                        AnchorPreset = AnchorPresets.TopLeft,
                        Location = Float2.Zero,
                        Size = new Float2(16f, 16f),
                        HorizontalAlignment = TextAlignment.Center,
                        VerticalAlignment = TextAlignment.Center,
                        TextColor = InkWashTheme.PaperBright,
                    };
                    _checkPanel.AddChild(check);
                }

                _text = new InkTextBlock(InkTextStyle.Body)
                {
                    Text = text,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(24f, 4f),
                    Size = new Float2(1f - 24f, 20f),
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                    TextColor = done ? InkWashTheme.PaperAged : InkWashTheme.PaperBright,
                };
                if (done)
                    _text.TextColor = InkWashTheme.PaperAged;
                AddChild(_text);
            }
        }

        private class RewardItem : ContainerControl
        {
            public RewardItem(string label, string value, Color valueColor)
            {
                ClipChildren = false;

                InkTextBlock labelText = new InkTextBlock(InkTextStyle.Caption)
                {
                    Text = label,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(0f, 8f),
                    Size = new Float2(1f, 16f),
                    HorizontalAlignment = TextAlignment.Center,
                    TextColor = InkWashTheme.PaperAged,
                };
                AddChild(labelText);

                InkTextBlock valueText = new InkTextBlock(InkTextStyle.Number)
                {
                    Text = value,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(0f, 28f),
                    Size = new Float2(1f, 24f),
                    HorizontalAlignment = TextAlignment.Center,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 15f),
                    TextColor = valueColor,
                };
                AddChild(valueText);
            }
        }
    }
}
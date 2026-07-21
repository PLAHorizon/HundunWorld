using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.StyleSystem;
using System;
using System.Collections.Generic;

namespace HundunWorld.Game.UI.Ink.Pages
{
    public class MenuQuestsPage : ContainerControl, IInkPage
    {
        // ───────────── Data Types ─────────────

        private class QuestData
        {
            public string Name;
            public string Category; // 主线/支线/日常/周常
            public string Status; // 进行中/未开始/已完成
            public string Location;
            public string Npc;
            public string TimeLimit;
            public string Difficulty;
            public string Description;
            public int ProgressCurrent;
            public int ProgressMax;
            public int Level;
            public string[] ObjectiveTexts;
            public bool[] ObjectiveDone;
            public int[] ObjectiveProgress;
            public int[] ObjectiveMax;
            public RewardData[] Rewards;
        }

        private class RewardData
        {
            public string Label;
            public string Value;
            public Color ValueColor;
        }

        // ───────────── Fields ─────────────

        private Float2 _screenSize;

        // Scrim
        private Panel _scrim;

        // Main panel

        // Header
        private Panel _header;
        private Label _title;
        private Label _subtitle;
        private InkButton _closeBtn;
        private List<InkButton> _tabs;
        private List<Label> _tabBadges;

        // Body
        private Panel _body;

        // Left: Quest list
        private Panel _questListPanel;
        private List<QuestGroup> _questGroups;
        private Panel _bottomPanel;
        private Label _totalProgressLabel;
        private Panel _totalProgressBar;
        private Panel _totalProgressFill;

        // Right: Quest detail
        private Panel _questDetailPanel;
        private Label _detailTitle;
        private InkButton _detailCategoryTag;
        private InkButton _detailDifficultyTag;
        private Label _detailLocation;
        private Label _detailNpc;
        private Label _detailTimeLimit;
        private InkPaperPanel _detailDescriptionPanel;
        private Label _detailDescription;
        private Label _detailObjectiveTitle;
        private List<ObjectiveRow> _detailObjectives;
        private Label _detailRewardTitle;
        private Panel _detailRewardsPanel;
        private List<RewardCard> _detailRewardCards;
        private InkButton _abandonBtn;
        private InkButton _trackBtn;
        private Label _detailLevelInfo;

        private int _selectedTab;
        private int _selectedQuest;
        private List<QuestData> _allQuests;

        private const float PanelMaxW = 1400f;
        private const float PanelMaxH = 900f;
        private const float QuestListW = 450f;

        public event Action<string> NavigationRequested;

        // ───────────── Quest Group ─────────────

        private class QuestGroup : ContainerControl
        {
            public Label TitleLabel;
            public Label CountLabel;
            public List<QuestItem> Items = new List<QuestItem>();
            public bool Expanded = true;

            private Panel _headerPanel;
            public Panel _bodyPanel;
            private Label _chevron;

            public QuestGroup(string title, int count)
            {
                ClipChildren = false;

                _headerPanel = new Panel
                {
                    BackgroundColor = Color.Transparent,
                    Parent = this
                };

                _chevron = new Label
                {
                    Text = "\u25BC",
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f),
                    TextColor = InkWashTheme.TextSecondary,
                    Parent = _headerPanel
                };

                TitleLabel = new Label
                {
                    Text = title,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 13f),
                    TextColor = InkWashTheme.TextDefault,
                    Parent = _headerPanel
                };

                CountLabel = new Label
                {
                    Text = count.ToString(),
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 11f),
                    TextColor = InkWashTheme.TextSecondary,
                    BackgroundColor = new Color(InkWashTheme.Void.R, InkWashTheme.Void.G, InkWashTheme.Void.B, 0.4f),
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                    Parent = _headerPanel
                };

                _bodyPanel = new Panel
                {
                    BackgroundColor = Color.Transparent,
                    Parent = this
                };
            }

            public void Toggle()
            {
                Expanded = !Expanded;
                _bodyPanel.Visible = Expanded;
            }

            public void Layout(float width)
            {
                float headerH = 34f;
                _headerPanel.Location = Float2.Zero;
                _headerPanel.Size = new Float2(width, headerH);

                _chevron.Location = new Float2(8f, (headerH - 14f) / 2f);
                _chevron.Size = new Float2(14f, 14f);

                TitleLabel.Location = new Float2(28f, (headerH - 18f) / 2f);
                TitleLabel.Size = new Float2(width - 100f, 18f);

                CountLabel.Location = new Float2(width - 50f, (headerH - 18f) / 2f);
                CountLabel.Size = new Float2(36f, 18f);

                _bodyPanel.Location = new Float2(0f, headerH);
                _bodyPanel.Size = new Float2(width, Items.Count * 40f);
                _bodyPanel.Visible = Expanded;

                float iy = 4f;
                foreach (var item in Items)
                {
                    item.Location = new Float2(6f, iy);
                    item.Size = new Float2(width - 12f, 36f);
                    iy += 38f;
                }
            }
        }

        // ───────────── Quest Item ─────────────

        private class QuestItem : ContainerControl
        {
            public Label StatusIcon;
            public Label NameLabel;
            public Label ProgressLabel;
            public bool Active;
            public bool Done;
            public event Action Clicked;

            public QuestItem(string name, string progress, bool active, bool done)
            {
                Active = active;
                Done = done;
                ClipChildren = false;

                string icon = done ? "\u2713" : active ? "\u2605" : "\u25CB";
                Color iconColor = done ? InkWashTheme.JadePrimary : active ? InkWashTheme.GoldPrimary : InkWashTheme.TextTertiary;

                StatusIcon = new Label
                {
                    Text = icon,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 14f),
                    TextColor = iconColor,
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                    Parent = this
                };

                NameLabel = new Label
                {
                    Text = name,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 13f),
                    TextColor = active ? InkWashTheme.GoldBright : done ? InkWashTheme.TextTertiary : InkWashTheme.TextDefault,
                    VerticalAlignment = TextAlignment.Center,
                    Parent = this
                };

                ProgressLabel = new Label
                {
                    Text = progress,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f),
                    TextColor = InkWashTheme.TextTertiary,
                    VerticalAlignment = TextAlignment.Center,
                    HorizontalAlignment = TextAlignment.Far,
                    Parent = this
                };

                if (done)
                    NameLabel.TextColor = InkWashTheme.TextTertiary;
            }

            public override bool OnMouseUp(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left) Clicked?.Invoke();
                return base.OnMouseUp(location, button);
            }
        }

        // ───────────── Objective Row ─────────────

        private class ObjectiveRow : ContainerControl
        {
            public Label Icon;
            public Label Text;
            public Label Status;
            public Panel ProgressBar;
            public Panel ProgressFill;
            public Label CountLabel;

            public ObjectiveRow(string text, bool done, bool active, int current, int max)
            {
                ClipChildren = false;

                string icon = done ? "\u2713" : active ? "\u25CB" : "\u25CB";
                Color iconColor = done ? InkWashTheme.JadePrimary : active ? InkWashTheme.GoldPrimary : InkWashTheme.TextTertiary;

                Icon = new Label
                {
                    Text = icon,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 14f),
                    TextColor = iconColor,
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                    Parent = this
                };

                Text = new Label
                {
                    Text = text,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                    TextColor = done ? InkWashTheme.TextTertiary : active ? InkWashTheme.TextDefault : InkWashTheme.TextSecondary,
                    VerticalAlignment = TextAlignment.Center,
                    Parent = this
                };

                if (max > 0)
                {
                    CountLabel = new Label
                    {
                        Text = $"{current}/{max}",
                        Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 11f),
                        TextColor = InkWashTheme.TextSecondary,
                        HorizontalAlignment = TextAlignment.Far,
                        VerticalAlignment = TextAlignment.Center,
                        Parent = this
                    };

                    ProgressBar = new Panel
                    {
                        BackgroundColor = new Color(InkWashTheme.Void.R, InkWashTheme.Void.G, InkWashTheme.Void.B, 0.5f),
                        Parent = this
                    };

                    float pct = max > 0 ? (float)current / max : 0f;
                    ProgressFill = new Panel
                    {
                        BackgroundColor = InkWashTheme.GoldPrimary,
                        Parent = ProgressBar
                    };
                }

                if (done)
                    Text.TextColor = InkWashTheme.TextTertiary;
            }
        }

        // ───────────── Reward Card ─────────────

        private class RewardCard : ContainerControl
        {
            public Label ValueLabel;

            public RewardCard(string label, string value, Color valueColor)
            {
                ClipChildren = false;

                var iconPanel = new Panel
                {
                    Size = new Float2(32f, 32f),
                    BackgroundColor = new Color(valueColor.R, valueColor.G, valueColor.B, 0.15f),
                    Parent = this
                };

                new Label
                {
                    Text = label == "\u7ECF\u9A8C" ? "\u2728" : label == "\u94F6\u4E24" ? "\uFFE6" : label == "\u88C5\u5907" ? "\u26E8" : "\u2606",
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 16f),
                    TextColor = valueColor,
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.StretchAll,
                    Parent = iconPanel
                };

                new Label
                {
                    Text = label,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f),
                    TextColor = InkWashTheme.TextTertiary,
                    Parent = this
                };

                ValueLabel = new Label
                {
                    Text = value,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 13f),
                    TextColor = valueColor,
                    Parent = this
                };
            }
        }

        // ───────────── Constructor ─────────────

        public MenuQuestsPage()
        {
            _screenSize = new Float2(Width, Height);
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
                BuildScrim();
                BuildMainPanel();
                ApplyLayout();
                SelectQuest(0);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MenuQuestsPage] init: {ex.Message}");
            }
        }

        private void InitData()
        {
            _allQuests = new List<QuestData>
            {
                new QuestData
                {
                    Name = "\u521D\u5165\u6C5F\u6E56", Category = "\u4E3B\u7EBF", Difficulty = "\u666E\u901A",
                    Location = "\u5F00\u5C01\u57CE", Npc = "\u738B\u94C1\u5320", TimeLimit = "\u65E0\u65F6\u9650",
                    Level = 1, Description = "\u4F60\u521D\u5230\u5F00\u5C01\u57CE\uFF0C\u542C\u95FB\u57CE\u4E2D\u6709\u4F4D\u9690\u4E16\u9AD8\u4EBA\uFF0C\u8EAB\u6000\u7EDD\u4E16\u6B66\u5B66\u3002\u524D\u5F80\u57CE\u4E2D\u5404\u5904\u63A2\u8BBF\uFF0C\u62DC\u8BBF\u5404\u6D3E\u957F\u8001\uFF0C\u4E86\u89E3\u6B66\u6797\u683C\u5C40\uFF0C\u6216\u53EF\u5BFB\u5F97\u673A\u7F18\u3002\u57CE\u90CA\u5C71\u8D3C\u4E3A\u60A3\uFF0C\u4EA6\u53EF\u501F\u6B64\u78E8\u7EC3\u6B66\u827A\u3002",
                    ProgressCurrent = 2, ProgressMax = 5,
                    ObjectiveTexts = new[] { "\u524D\u5F80\u5F00\u5C01\u57CE", "\u4E0ENPC\u5BF9\u8BDD", "\u51FB\u8D25\u5C71\u8D3C" },
                    ObjectiveDone = new[] { true, false, false },
                    ObjectiveProgress = new[] { 1, 0, 0 },
                    ObjectiveMax = new[] { 1, 1, 5 },
                    Rewards = new[]
                    {
                        new RewardData { Label = "\u7ECF\u9A8C", Value = "+5000", ValueColor = InkWashTheme.JadeBright },
                        new RewardData { Label = "\u94F6\u4E24", Value = "+200", ValueColor = InkWashTheme.GoldBright },
                        new RewardData { Label = "\u88C5\u5907", Value = "\u7CBE\u94C1\u62A4\u8155", ValueColor = InkWashTheme.QualityUncommon },
                    }
                },
                new QuestData
                {
                    Name = "\u62DC\u5E08\u5B66\u827A", Category = "\u4E3B\u7EBF", Difficulty = "\u666E\u901A",
                    Location = "\u5C11\u6797\u5BFA", Npc = "\u65B9\u4E08", TimeLimit = "\u65E0\u65F6\u9650",
                    Level = 1, Description = "\u524D\u5F80\u5C11\u6797\u5BFA\u62DC\u8BBF\u65B9\u4E08\uFF0C\u5B66\u4E60\u6B66\u529F\u57FA\u7840\u3002",
                    ProgressCurrent = 0, ProgressMax = 3,
                    ObjectiveTexts = new[] { "\u524D\u5F80\u5C11\u6797\u5BFA", "\u62DC\u89C1\u65B9\u4E08", "\u5B66\u4E60\u57FA\u7840\u62F3\u6CD5" },
                    ObjectiveDone = new[] { false, false, false },
                    ObjectiveProgress = new[] { 0, 0, 0 },
                    ObjectiveMax = new[] { 1, 1, 1 },
                    Rewards = new[]
                    {
                        new RewardData { Label = "\u7ECF\u9A8C", Value = "+2000", ValueColor = InkWashTheme.JadeBright },
                        new RewardData { Label = "\u94F6\u4E24", Value = "+100", ValueColor = InkWashTheme.GoldBright },
                    }
                },
                new QuestData
                {
                    Name = "\u6C5F\u6E56\u521D\u63A2", Category = "\u4E3B\u7EBF", Difficulty = "\u666E\u901A",
                    Location = "\u6D66\u6C5F\u9547", Npc = "\u9152\u5E97\u8001\u677F", TimeLimit = "\u65E0\u65F6\u9650",
                    Level = 1, Description = "\u6D66\u6C5F\u9547\u9152\u5E97\u8001\u677F\u6709\u4E9B\u6D88\u606F\u8981\u544A\u8BC9\u4F60\u3002",
                    ProgressCurrent = 0, ProgressMax = 2,
                    ObjectiveTexts = new[] { "\u524D\u5F80\u6D66\u6C5F\u9547\u9152\u5E97", "\u4E0E\u9152\u5E97\u8001\u677F\u8C08\u8BDD" },
                    ObjectiveDone = new[] { false, false },
                    ObjectiveProgress = new[] { 0, 0 },
                    ObjectiveMax = new[] { 1, 1 },
                    Rewards = new[]
                    {
                        new RewardData { Label = "\u7ECF\u9A8C", Value = "+1000", ValueColor = InkWashTheme.JadeBright },
                    }
                },
                new QuestData
                {
                    Name = "\u5BFB\u4EBA\u542F\u4E8B", Category = "\u652F\u7EBF", Difficulty = "\u666E\u901A",
                    Location = "\u6D66\u6C5F\u9547", Npc = "\u5C0F\u5B69", TimeLimit = "\u65E0\u65F6\u9650",
                    Level = 5, Description = "\u5C0F\u5B69\u7684\u7236\u4EB2\u5931\u8E2A\u591A\u65E5\uFF0C\u5E0C\u671B\u4F60\u80FD\u5E2E\u5FD9\u5BFB\u627E\u3002",
                    ProgressCurrent = 1, ProgressMax = 3,
                    ObjectiveTexts = new[] { "\u8BE2\u95EE\u8857\u574A", "\u524D\u5F80\u5C71\u4E2D\u5BFB\u627E", "\u8FD4\u56DE\u6D66\u6C5F\u9547" },
                    ObjectiveDone = new[] { true, false, false },
                    ObjectiveProgress = new[] { 1, 0, 0 },
                    ObjectiveMax = new[] { 1, 1, 1 },
                    Rewards = new[]
                    {
                        new RewardData { Label = "\u7ECF\u9A8C", Value = "+3000", ValueColor = InkWashTheme.JadeBright },
                        new RewardData { Label = "\u94F6\u4E24", Value = "+150", ValueColor = InkWashTheme.GoldBright },
                    }
                },
                new QuestData
                {
                    Name = "\u91C7\u96C6\u836F\u6750", Category = "\u652F\u7EBF", Difficulty = "\u666E\u901A",
                    Location = "\u9752\u5C71", Npc = "\u91C7\u836F\u4EBA", TimeLimit = "\u65E0\u65F6\u9650",
                    Level = 3, Description = "\u5E2E\u52A9\u91C7\u836F\u4EBA\u91C7\u96C6\u836F\u6750\u3002",
                    ProgressCurrent = 3, ProgressMax = 3,
                    ObjectiveTexts = new[] { "\u91C7\u96C6\u7D2B\u8349", "\u91C7\u96C6\u767D\u8349", "\u91C7\u96C6\u9EC4\u8349" },
                    ObjectiveDone = new[] { true, true, true },
                    ObjectiveProgress = new[] { 1, 1, 1 },
                    ObjectiveMax = new[] { 1, 1, 1 },
                    Rewards = new[]
                    {
                        new RewardData { Label = "\u7ECF\u9A8C", Value = "+1500", ValueColor = InkWashTheme.JadeBright },
                        new RewardData { Label = "\u94F6\u4E24", Value = "+80", ValueColor = InkWashTheme.GoldBright },
                    }
                },
                new QuestData
                {
                    Name = "\u6BCF\u65E5\u4FEE\u884C", Category = "\u65E5\u5E38", Difficulty = "\u666E\u901A",
                    Location = "\u5404\u5730", Npc = "\u65E0", TimeLimit = "\u4ECA\u65E5",
                    Level = 1, Description = "\u6BCF\u65E5\u4FEE\u884C\u4EFB\u52A1\uFF0C\u5B8C\u6210\u540E\u83B7\u5F97\u5927\u91CF\u7ECF\u9A8C\u3002",
                    ProgressCurrent = 3, ProgressMax = 5,
                    ObjectiveTexts = new[] { "\u51FB\u8D25\u602A\u7269 x3", "\u91C7\u96C6\u8D44\u6E90 x2" },
                    ObjectiveDone = new[] { true, false },
                    ObjectiveProgress = new[] { 3, 0 },
                    ObjectiveMax = new[] { 3, 2 },
                    Rewards = new[]
                    {
                        new RewardData { Label = "\u7ECF\u9A8C", Value = "+8000", ValueColor = InkWashTheme.JadeBright },
                        new RewardData { Label = "\u94F6\u4E24", Value = "+500", ValueColor = InkWashTheme.GoldBright },
                    }
                },
                new QuestData
                {
                    Name = "\u95E8\u6D3E\u8BD5\u70BC", Category = "\u5468\u5E38", Difficulty = "\u56F0\u96BE",
                    Location = "\u95E8\u6D3E\u5927\u6BBF", Npc = "\u95E8\u6D3E\u957F\u8001", TimeLimit = "\u672C\u5468",
                    Level = 10, Description = "\u95E8\u6D3E\u8BD5\u70BC\uFF0C\u5C55\u793A\u4F60\u7684\u5B9E\u529B\u3002",
                    ProgressCurrent = 0, ProgressMax = 1,
                    ObjectiveTexts = new[] { "\u901A\u8FC7\u95E8\u6D3E\u8BD5\u70BC" },
                    ObjectiveDone = new[] { false },
                    ObjectiveProgress = new[] { 0 },
                    ObjectiveMax = new[] { 1 },
                    Rewards = new[]
                    {
                        new RewardData { Label = "\u7ECF\u9A8C", Value = "+15000", ValueColor = InkWashTheme.JadeBright },
                        new RewardData { Label = "\u94F6\u4E24", Value = "+1000", ValueColor = InkWashTheme.GoldBright },
                        new RewardData { Label = "\u88C5\u5907", Value = "\u7384\u94C1\u5251", ValueColor = InkWashTheme.QualityEpic },
                    }
                },
            };
        }

        // ───────────── Build ─────────────

        private void BuildScrim()
        {
            _scrim = new Panel
            {
                AnchorPreset = AnchorPresets.StretchAll,
                BackgroundColor = new Color(InkWashTheme.Void.R, InkWashTheme.Void.G, InkWashTheme.Void.B, 0.88f),
                Parent = this
            };
        }

        private void BuildMainPanel()
        {
            // Header
            _header = new Panel
            {
                BackgroundColor = new Color(InkWashTheme.Void.R, InkWashTheme.Void.G, InkWashTheme.Void.B, 0.4f),
                Parent = this
            };

            _title = new Label
            {
                Text = "\u4EFB\u52A1\u65E5\u5FD7",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 22f),
                TextColor = InkWashTheme.GoldPrimary,
                Parent = _header
            };

            _subtitle = new Label
            {
                Text = "QUEST LOG",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextTertiary,
                Parent = _header
            };

            _closeBtn = new InkButton
            {
                Text = "\u2715",
                ButtonSize = InkButtonSize.Sm,
                Variant = InkButtonVariant.Ghost,
                Parent = _header
            };
            _closeBtn.Clicked += () => NavigationRequested?.Invoke("back-hud");

            // Tabs
            _tabs = new List<InkButton>();
            _tabBadges = new List<Label>();
            string[] tabNames = { "\u4E3B\u7EBF", "\u652F\u7EBF", "\u65E5\u5E38", "\u5468\u5E38", "\u6D3B\u52A8" };
            int[] tabCounts = { 3, 2, 1, 1, 0 };
            for (int i = 0; i < tabNames.Length; i++)
            {
                int ci = i;
                var tab = new InkButton
                {
                    Text = tabNames[i],
                    ButtonSize = InkButtonSize.Sm,
                    Variant = InkButtonVariant.Ghost,
                    Parent = _header
                };
                tab.Clicked += () => SelectTab(ci);
                _tabs.Add(tab);

                var badge = new Label
                {
                    Text = tabCounts[i].ToString(),
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 10f),
                    TextColor = tabCounts[i] > 0 ? InkWashTheme.GoldBright : InkWashTheme.TextTertiary,
                    BackgroundColor = tabCounts[i] > 0
                        ? new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.2f)
                        : new Color(InkWashTheme.Void.R, InkWashTheme.Void.G, InkWashTheme.Void.B, 0.5f),
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                    Parent = _header
                };
                _tabBadges.Add(badge);
            }
            SelectTab(0);

            // Body
            _body = new Panel
            {
                BackgroundColor = Color.Transparent,
                Parent = this
            };

            // Left: Quest list
            _questListPanel = new Panel
            {
                BackgroundColor = new Color(InkWashTheme.Panel.R, InkWashTheme.Panel.G, InkWashTheme.Panel.B, 0.92f),
                ClipChildren = true,
                Parent = _body
            };

            BuildQuestGroups();

            // Bottom progress
            _bottomPanel = new Panel
            {
                BackgroundColor = new Color(InkWashTheme.Void.R, InkWashTheme.Void.G, InkWashTheme.Void.B, 0.5f),
                Parent = _questListPanel
            };

            new Label
            {
                Text = "\u603B\u8FDB\u5EA6",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 12f),
                TextColor = InkWashTheme.TextSecondary,
                Parent = _bottomPanel
            };

            _totalProgressLabel = new Label
            {
                Text = "8/24",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 15f),
                TextColor = InkWashTheme.GoldPrimary,
                HorizontalAlignment = TextAlignment.Far,
                Parent = _bottomPanel
            };

            _totalProgressBar = new Panel
            {
                BackgroundColor = new Color(InkWashTheme.Void.R, InkWashTheme.Void.G, InkWashTheme.Void.B, 0.5f),
                Parent = _bottomPanel
            };

            _totalProgressFill = new Panel
            {
                BackgroundColor = InkWashTheme.GoldPrimary,
                Parent = _totalProgressBar
            };

            // Right: Quest detail
            _questDetailPanel = new Panel
            {
                BackgroundColor = new Color(InkWashTheme.Abyss.R, InkWashTheme.Abyss.G, InkWashTheme.Abyss.B, 0.4f),
                ClipChildren = true,
                Parent = _body
            };

            _detailTitle = new Label
            {
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 28f),
                TextColor = InkWashTheme.GoldPrimary,
                Parent = _questDetailPanel
            };

            _detailCategoryTag = new InkButton
            {
                ButtonSize = InkButtonSize.Sm,
                Variant = InkButtonVariant.Ghost,
                Parent = _questDetailPanel
            };

            _detailDifficultyTag = new InkButton
            {
                ButtonSize = InkButtonSize.Sm,
                Variant = InkButtonVariant.Ghost,
                Parent = _questDetailPanel
            };

            _detailLocation = new Label
            {
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextSecondary,
                Parent = _questDetailPanel
            };

            _detailNpc = new Label
            {
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextSecondary,
                Parent = _questDetailPanel
            };

            _detailTimeLimit = new Label
            {
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextSecondary,
                Parent = _questDetailPanel
            };

            // Description (ink paper scroll)
            _detailDescriptionPanel = new InkPaperPanel
            {
                Parent = _questDetailPanel
            };

            _detailDescription = new Label
            {
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                TextColor = InkWashTheme.TextOnPaper,
                Parent = _detailDescriptionPanel
            };

            // Objectives
            _detailObjectiveTitle = new Label
            {
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 14f),
                TextColor = InkWashTheme.GoldPrimary,
                Parent = _questDetailPanel
            };

            _detailObjectives = new List<ObjectiveRow>();

            // Rewards
            _detailRewardTitle = new Label
            {
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 14f),
                TextColor = InkWashTheme.GoldPrimary,
                Parent = _questDetailPanel
            };

            _detailRewardsPanel = new Panel
            {
                BackgroundColor = new Color(InkWashTheme.Panel.R, InkWashTheme.Panel.G, InkWashTheme.Panel.B, 0.6f),
                Parent = _questDetailPanel
            };

            _detailRewardCards = new List<RewardCard>();

            // Action buttons
            _abandonBtn = new InkButton
            {
                Text = "\u653E\u5F03\u4EFB\u52A1",
                ButtonSize = InkButtonSize.Sm,
                Variant = InkButtonVariant.Ghost,
                Parent = _questDetailPanel
            };

            _trackBtn = new InkButton
            {
                Text = "\u53D6\u6D88\u8FFD\u8E2A",
                ButtonSize = InkButtonSize.Sm,
                Variant = InkButtonVariant.Primary,
                Parent = _questDetailPanel
            };

            _detailLevelInfo = new Label
            {
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextTertiary,
                HorizontalAlignment = TextAlignment.Far,
                Parent = _questDetailPanel
            };
        }

        private void BuildQuestGroups()
        {
            _questGroups = new List<QuestGroup>();

            string[][] groupDefs = {
                new[] { "\u4E3B\u7EBF\u4EFB\u52A1", "3" },
                new[] { "\u652F\u7EBF\u4EFB\u52A1", "2" },
                new[] { "\u65E5\u5E38\u4EFB\u52A1", "1" },
                new[] { "\u5468\u5E38\u4EFB\u52A1", "1" },
            };

            for (int gi = 0; gi < groupDefs.Length; gi++)
            {
                var g = new QuestGroup(groupDefs[gi][0], int.Parse(groupDefs[gi][1]))
                {
                    Parent = _questListPanel
                };
                _questGroups.Add(g);
            }

            // Populate quest items
            foreach (var q in _allQuests)
            {
                string cat = q.Category;
                int groupIdx = cat == "\u4E3B\u7EBF" ? 0 : cat == "\u652F\u7EBF" ? 1 : cat == "\u65E5\u5E38" ? 2 : 3;
                if (groupIdx < _questGroups.Count)
                {
                    bool active = q.Status == "\u8FDB\u884C\u4E2D";
                    bool done = q.Status == "\u5DF2\u5B8C\u6210";
                    string progress = done ? "\u5DF2\u5B8C\u6210" : active ? $"\u8FDB\u884C\u4E2D \u00B7 {q.ProgressCurrent}/{q.ProgressMax}" : "\u672A\u5F00\u59CB";
                    var item = new QuestItem(q.Name, progress, active, done)
                    {
                        Parent = _questGroups[groupIdx]._bodyPanel
                    };
                    int qi = _allQuests.IndexOf(q);
                    item.Clicked += () => SelectQuest(qi);
                    _questGroups[groupIdx].Items.Add(item);
                }
            }
        }

        private void SelectTab(int idx)
        {
            _selectedTab = idx;
            for (int i = 0; i < _tabs.Count; i++)
            {
                _tabs[i].TextColor = i == idx ? InkWashTheme.GoldPrimary : InkWashTheme.TextSecondary;
            }
        }

        private void SelectQuest(int index)
        {
            if (index < 0 || index >= _allQuests.Count) return;
            _selectedQuest = index;
            var q = _allQuests[index];

            // Update list item selection
            foreach (var group in _questGroups)
            {
                foreach (var item in group.Items)
                {
                    item.BackgroundColor = Color.Transparent;
                }
            }
            // Find and highlight the selected item
            int itemIdx = 0;
            foreach (var group in _questGroups)
            {
                foreach (var item in group.Items)
                {
                    if (itemIdx == index)
                    {
                        item.BackgroundColor = new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.08f);
                    }
                    itemIdx++;
                }
            }

            // Update detail
            _detailTitle.Text = q.Name;

            _detailCategoryTag.Text = q.Category;
            _detailDifficultyTag.Text = q.Difficulty;

            _detailLocation.Text = q.Location;
            _detailNpc.Text = q.Npc;
            _detailTimeLimit.Text = q.TimeLimit;

            _detailDescription.Text = q.Description;

            // Rebuild objectives
            foreach (var obj in _detailObjectives)
                obj.Dispose();
            _detailObjectives.Clear();

            for (int i = 0; i < q.ObjectiveTexts.Length; i++)
            {
                bool done = q.ObjectiveDone[i];
                bool active = !done && q.ObjectiveMax[i] > 0;
                var row = new ObjectiveRow(q.ObjectiveTexts[i], done, active, q.ObjectiveProgress[i], q.ObjectiveMax[i])
                {
                    Parent = _questDetailPanel
                };
                _detailObjectives.Add(row);
            }

            // Rebuild rewards
            foreach (var card in _detailRewardCards)
                card.Dispose();
            _detailRewardCards.Clear();

            foreach (var r in q.Rewards)
            {
                var card = new RewardCard(r.Label, r.Value, r.ValueColor)
                {
                    Parent = _detailRewardsPanel
                };
                _detailRewardCards.Add(card);
            }

            _detailLevelInfo.Text = $"\u4EFB\u52A1\u7B49\u7EA7 Lv.{q.Level}";
        }

        // ───────────── Layout ─────────────

        private void ApplyLayout()
        {
            float sw = Width > 0 ? Width : _screenSize.X;
            float sh = Height > 0 ? Height : _screenSize.Y;

            float panelW = Math.Min(sw - 40f, PanelMaxW);
            float panelH = Math.Min(sh - 40f, PanelMaxH);
            float panelX = (sw - panelW) / 2f;
            float panelY = (sh - panelH) / 2f;

            // Header
            float headerH = 60f;
            _header.Location = new Float2(panelX, panelY);
            _header.Size = new Float2(panelW, headerH);

            _title.Location = new Float2(24f, 14f);
            _title.Size = new Float2(120f, 28f);

            _subtitle.Location = new Float2(150f, 18f);
            _subtitle.Size = new Float2(80f, 20f);

            _closeBtn.Location = new Float2(panelW - 40f, 14f);
            _closeBtn.Size = new Float2(32f, 32f);

            // Tabs
            float tabStartX = 24f;
            float tabY = 40f;
            float tabGap = 36f;
            for (int i = 0; i < _tabs.Count; i++)
            {
                float tx = tabStartX + i * tabGap;
                _tabs[i].Location = new Float2(tx, tabY);
                _tabs[i].Size = new Float2(50f, 20f);

                _tabBadges[i].Location = new Float2(tx + 50f, tabY + 2f);
                _tabBadges[i].Size = new Float2(18f, 18f);
            }

            // Body
            float bodyY = panelY + 60f;
            float bodyH = panelH - 60f;
            _body.Location = new Float2(panelX, bodyY);
            _body.Size = new Float2(panelW, bodyH);

            // Left: Quest list (450px)
            float listW = Math.Min(QuestListW, panelW * 0.4f);
            _questListPanel.Location = Float2.Zero;
            _questListPanel.Size = new Float2(listW, bodyH);

            // Quest groups
            float gy = 12f;
            foreach (var group in _questGroups)
            {
                group.Location = new Float2(6f, gy);
                group.Size = new Float2(listW - 12f, 0f);
                group.Layout(listW - 12f);
                gy += group.Expanded ? 34f + group.Items.Count * 38f + 4f : 34f + 4f;
            }

            // Bottom progress
            float bottomH = 60f;
            _bottomPanel.Location = new Float2(0f, bodyH - bottomH);
            _bottomPanel.Size = new Float2(listW, bottomH);

            foreach (var child in _bottomPanel.Children)
            {
                if (child is Label l)
                {
                    if (l.Text == "\u603B\u8FDB\u5EA6")
                    {
                        l.Location = new Float2(16f, 10f);
                        l.Size = new Float2(60f, 18f);
                    }
                    else if (l == _totalProgressLabel)
                    {
                        l.Location = new Float2(listW - 80f, 10f);
                        l.Size = new Float2(64f, 18f);
                    }
                }
            }

            _totalProgressBar.Location = new Float2(16f, 34f);
            _totalProgressBar.Size = new Float2(listW - 32f, 4f);
            _totalProgressFill.Size = new Float2(_totalProgressBar.Width * 0.333f, 4f);

            // Right: Quest detail
            float detailX = listW + 1f;
            float detailW = panelW - listW - 1f;
            _questDetailPanel.Location = new Float2(detailX, 0f);
            _questDetailPanel.Size = new Float2(detailW, bodyH);

            float dy = 20f;
            _detailTitle.Location = new Float2(28f, dy);
            _detailTitle.Size = new Float2(detailW - 120f, 36f);
            dy += 40f;

            // Tags row
            _detailCategoryTag.Location = new Float2(28f, dy);
            _detailCategoryTag.Size = new Float2(50f, 22f);

            _detailDifficultyTag.Location = new Float2(84f, dy);
            _detailDifficultyTag.Size = new Float2(50f, 22f);
            dy += 30f;

            // Location / NPC / Time
            float infoY = dy;
            _detailLocation.Location = new Float2(28f, infoY);
            _detailLocation.Size = new Float2(120f, 18f);

            _detailNpc.Location = new Float2(160f, infoY);
            _detailNpc.Size = new Float2(100f, 18f);

            _detailTimeLimit.Location = new Float2(280f, infoY);
            _detailTimeLimit.Size = new Float2(100f, 18f);
            dy += 28f;

            // Description
            float descPanelH = 100f;
            _detailDescriptionPanel.Location = new Float2(28f, dy);
            _detailDescriptionPanel.Size = new Float2(detailW - 56f, descPanelH);
            dy += descPanelH + 20f;

            _detailDescription.Location = new Float2(16f, 16f);
            _detailDescription.Size = new Float2(detailW - 88f, descPanelH - 32f);

            // Objectives title
            _detailObjectiveTitle.Location = new Float2(28f, dy);
            _detailObjectiveTitle.Size = new Float2(120f, 20f);
            dy += 28f;

            // Objective rows
            foreach (var obj in _detailObjectives)
            {
                obj.Location = new Float2(28f, dy);
                obj.Size = new Float2(detailW - 56f, 36f);
                dy += 40f;
            }

            // Rewards title
            dy += 8f;
            _detailRewardTitle.Location = new Float2(28f, dy);
            _detailRewardTitle.Size = new Float2(120f, 20f);
            dy += 28f;

            // Rewards panel
            _detailRewardsPanel.Location = new Float2(28f, dy);
            _detailRewardsPanel.Size = new Float2(detailW - 56f, 60f);

            float rx = 12f;
            foreach (var card in _detailRewardCards)
            {
                card.Location = new Float2(rx, 8f);
                card.Size = new Float2(120f, 44f);
                rx += 140f;
            }
            dy += 68f;

            // Action buttons
            dy += 8f;
            _abandonBtn.Location = new Float2(28f, dy);
            _abandonBtn.Size = new Float2(100f, 32f);

            _trackBtn.Location = new Float2(136f, dy);
            _trackBtn.Size = new Float2(110f, 32f);

            _detailLevelInfo.Location = new Float2(detailW - 160f, dy);
            _detailLevelInfo.Size = new Float2(140f, 32f);
        }

        public void RefreshLayout()
        {
            _screenSize = new Float2(Width, Height);
            ApplyLayout();
        }
    }
}

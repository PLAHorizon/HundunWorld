using FlaxEngine;
using FlaxEngine.GUI;
using Game.Character.Attributes;
using HundunWorld.Game.UI.StyleSystem;
using System;
using System.Collections.Generic;

namespace HundunWorld.Game.UI.Ink.Pages.Social
{
    public class LeaderboardPage : ContainerControl, IInkPage
    {
        private const float TopHeaderHeight = 52f;
        private const float ContentPadding = 12f;
        private const float ScreenEdge = 16f;
        private const float ColumnGap = 12f;
        private const float LeftColumnWidth = 240f;
        private const float RightColumnWidth = 280f;
        private const float PodiumHeight = 170f;
        private const float TableHeaderHeight = 32f;
        private const float TableRowHeight = 36f;
        private const float CategoryItemHeight = 50f;
        private const float CategoryItemGap = 4f;
        private const float TimeBtnHeight = 28f;
        private const float TimeBtnGap = 6f;

        private InkPanel _topHeader;
        private Label _titleLabel;
        private Label _subtitleLabel;
        private Label _seasonLabel;
        private Label _remainDaysLabel;
        private InkButton _closeButton;

        private InkPanel _leftPanel;
        private ContainerControl _categoryHost;
        private Label _categoryTitleLabel;
        private readonly List<ContainerControl> _categoryItems = new List<ContainerControl>();
        private InkPanel _timeFilterPanel;
        private Label _timeFilterTitleLabel;
        private readonly List<InkButton> _timeFilterButtons = new List<InkButton>();

        private InkPanel _middlePanel;
        private ContainerControl _podiumHost;
        private InkPanel _podiumSilver;
        private InkPanel _podiumGold;
        private InkPanel _podiumBronze;
        private InkPanel _tablePanel;
        private ContainerControl _tableHeader;
        private ContainerControl _tableBodyHost;
        private Label _loadMoreLabel;
        private readonly List<ContainerControl> _tableRows = new List<ContainerControl>();

        private InkPanel _rightPanel;
        private InkPanel _myRankCard;
        private Label _myRankTitleLabel;
        private Label _myNameLabel;
        private Label _mySectLevelLabel;
        private Label _myRankValueLabel;
        private Label _myPowerValueLabel;
        private InkPanel _gapCard;
        private Label _gapTitleLabel;
        private Label _prevRankPowerLabel;
        private Label _gapValueLabel;
        private InkBar _gapProgressBar;
        private InkPanel _trendCard;
        private Label _trendTitleLabel;
        private Label _trendChangeLabel;
        private ContainerControl _trendChartHost;
        private Label _trendStartLabel;
        private InkPanel _rewardCard;
        private Label _rewardTitleLabel;
        private ContainerControl _rewardListHost;
        private Label _rewardHintLabel;

        private struct CategoryInfo
        {
            public string Name;
            public string Desc;
            public bool Active;
        }

        private readonly CategoryInfo[] _categories =
        {
            new CategoryInfo { Name = "战力榜", Desc = "综合战力排名", Active = true },
            new CategoryInfo { Name = "等级榜", Desc = "角色等级排名", Active = false },
            new CategoryInfo { Name = "财富榜", Desc = "银两资产排名", Active = false },
            new CategoryInfo { Name = "门派贡献", Desc = "门派贡献排名", Active = false },
            new CategoryInfo { Name = "竞技场榜", Desc = "PVP胜场排名", Active = false },
            new CategoryInfo { Name = "帮派战力", Desc = "帮派总战力", Active = false },
        };

        private readonly string[] _timeFilters = { "本日", "本周", "本月", "总榜" };
        private const int ActiveTimeFilterIndex = 1;

        private struct PodiumInfo
        {
            public int Rank;
            public string AvatarChar;
            public string Name;
            public string Sect;
            public int Level;
            public int Power;
        }

        private readonly PodiumInfo[] _podiums =
        {
            new PodiumInfo { Rank = 2, AvatarChar = "沈", Name = "沈青鸾", Sect = "峨眉派", Level = 76, Power = 121830 },
            new PodiumInfo { Rank = 1, AvatarChar = "剑", Name = "剑无痕", Sect = "华山派", Level = 78, Power = 128450 },
            new PodiumInfo { Rank = 3, AvatarChar = "萧", Name = "萧别离", Sect = "丐帮", Level = 75, Power = 118920 },
        };

        private struct RankRowInfo
        {
            public int Rank;
            public string AvatarChar;
            public string Name;
            public string Sect;
            public int Level;
            public int Power;
            public int Trend;
            public bool IsMe;
        }

        private readonly RankRowInfo[] _rankRows =
        {
            new RankRowInfo { Rank = 4, AvatarChar = "楚", Name = "楚留香", Sect = "武当派", Level = 74, Power = 112340, Trend = 2, IsMe = false },
            new RankRowInfo { Rank = 5, AvatarChar = "叶", Name = "叶孤城", Sect = "昆仑派", Level = 73, Power = 109580, Trend = -1, IsMe = false },
            new RankRowInfo { Rank = 6, AvatarChar = "陆", Name = "陆小凤", Sect = "唐门", Level = 72, Power = 105420, Trend = 0, IsMe = false },
            new RankRowInfo { Rank = 7, AvatarChar = "西", Name = "西门吹雪", Sect = "华山派", Level = 71, Power = 101870, Trend = 3, IsMe = false },
            new RankRowInfo { Rank = 8, AvatarChar = "花", Name = "花满楼", Sect = "慕容世家", Level = 70, Power = 98650, Trend = -2, IsMe = false },
            new RankRowInfo { Rank = 9, AvatarChar = "司", Name = "司空摘星", Sect = "丐帮", Level = 69, Power = 95320, Trend = 0, IsMe = false },
            new RankRowInfo { Rank = 10, AvatarChar = "傅", Name = "傅红雪", Sect = "明教", Level = 68, Power = 92180, Trend = 1, IsMe = false },
            new RankRowInfo { Rank = 11, AvatarChar = "柳", Name = "柳随风", Sect = "峨眉派", Level = 67, Power = 89450, Trend = -3, IsMe = false },
            new RankRowInfo { Rank = 12, AvatarChar = "燕", Name = "燕十三", Sect = "华山派", Level = 66, Power = 86720, Trend = 2, IsMe = false },
            new RankRowInfo { Rank = 13, AvatarChar = "谢", Name = "谢晓峰", Sect = "武当派", Level = 65, Power = 84180, Trend = 0, IsMe = false },
            new RankRowInfo { Rank = 14, AvatarChar = "独", Name = "独孤求败", Sect = "昆仑派", Level = 64, Power = 81560, Trend = -1, IsMe = false },
            new RankRowInfo { Rank = 15, AvatarChar = "李", Name = "李寻欢", Sect = "唐门", Level = 63, Power = 78930, Trend = 4, IsMe = false },
        };

        private readonly PodiumInfo _myInfo = new PodiumInfo
        {
            Rank = 47, AvatarChar = "侠", Name = "逍遥客", Sect = "武当派", Level = 60, Power = 85420,
        };

        private readonly PodiumInfo _prevRank = new PodiumInfo
        {
            Rank = 46, AvatarChar = "风", Name = "风清扬", Sect = "华山派", Level = 60, Power = 86650,
        };

        private readonly int[] _weeklyRanks = { 55, 53, 54, 51, 50, 48, 47 };
        private readonly string[] _weekdayLabels = { "一", "二", "三", "四", "五", "六", "日" };

        private struct RewardInfo
        {
            public string Name;
            public string Desc;
            public int Count;
            public string Quality;
        }

        private readonly RewardInfo[] _rewards =
        {
            new RewardInfo { Name = "声望令", Desc = "江湖声望", Count = 500, Quality = "Legendary" },
            new RewardInfo { Name = "精铁锭", Desc = "锻造材料", Count = 20, Quality = "Rare" },
            new RewardInfo { Name = "灵石", Desc = "修炼资源", Count = 10, Quality = "Epic" },
        };

        public event Action<string> NavigationRequested;
        public InkParticleSystem ParticleSystem { get; set; }
        private CharacterAttributesComponent _boundCharacter;

        public void BindCharacter(CharacterAttributesComponent component)
        {
            _boundCharacter = component;
        }

        public LeaderboardPage()
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
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[LeaderboardPage] 初始化失败: {ex.Message}");
            }
        }

        private void BuildTopHeader()
        {
            _topHeader = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(800f, TopHeaderHeight),
            };

            var trophyLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(24f, 15f),
                Size = new Float2(20f, 22f),
                Text = "♛",
                TextColor = InkWashTheme.GoldPrimary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 16f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            _topHeader.AddChild(trophyLabel);

            _titleLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(52f, 14f),
                Size = new Float2(160f, 24f),
                Text = "江湖风云榜",
                TextColor = InkWashTheme.GoldPrimary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 18f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _topHeader.AddChild(_titleLabel);

            _subtitleLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(220f, 18f),
                Size = new Float2(120f, 16f),
                Text = "LEADERBOARD",
                TextColor = InkWashTheme.TextTertiary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _topHeader.AddChild(_subtitleLabel);

            var calendarLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(560f, 16f),
                Size = new Float2(16f, 20f),
                Text = "◷",
                TextColor = InkWashTheme.TextTertiary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            _topHeader.AddChild(calendarLabel);

            _seasonLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(580f, 18f),
                Size = new Float2(60f, 16f),
                Text = "第3赛季",
                TextColor = InkWashTheme.TextSecondary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _topHeader.AddChild(_seasonLabel);

            _remainDaysLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(644f, 18f),
                Size = new Float2(70f, 16f),
                Text = "剩余12天",
                TextColor = InkWashTheme.GoldPrimary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 12f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _topHeader.AddChild(_remainDaysLabel);

            var divider = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(726f, 16f),
                Size = new Float2(1f, 20f),
                BackgroundColor = new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.5f),
            };
            _topHeader.AddChild(divider);

            _closeButton = new InkButton
            {
                Variant = InkButtonVariant.Default,
                Text = "✕",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(740f, 10f),
                Size = new Float2(32f, 32f),
            };
            _closeButton.Clicked += () => NavigationRequested?.Invoke(InkPageDomIds.CombatHud);
            _topHeader.AddChild(_closeButton);

            AddChild(_topHeader);
        }

        private void BuildLeftPanel()
        {
            _leftPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(LeftColumnWidth, 600f),
            };

            var categoryCard = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(ScreenEdge, ScreenEdge),
                Size = new Float2(LeftColumnWidth - ScreenEdge * 2f, 380f),
            };
            _leftPanel.AddChild(categoryCard);

            _categoryTitleLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 8f),
                Size = new Float2(LeftColumnWidth - ScreenEdge * 2f - 48f, 22f),
                Text = "榜单分类",
                TextColor = InkWashTheme.TextSecondary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            categoryCard.AddChild(_categoryTitleLabel);

            var listIcon = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(LeftColumnWidth - ScreenEdge * 2f - 28f, 8f),
                Size = new Float2(16f, 22f),
                Text = "☰",
                TextColor = InkWashTheme.TextTertiary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            categoryCard.AddChild(listIcon);

            _categoryHost = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(8f, 36f),
                Size = new Float2(LeftColumnWidth - ScreenEdge * 2f - 16f, 336f),
            };
            categoryCard.AddChild(_categoryHost);

            float cursorY = 0f;
            for (int i = 0; i < _categories.Length; i++)
            {
                var cat = _categories[i];
                var item = new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(0f, cursorY),
                    Size = new Float2(_categoryHost.Width, CategoryItemHeight),
                    BackgroundColor = cat.Active
                        ? new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.10f)
                        : Color.Transparent,
                };

                var symbolLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(10f, 14f),
                    Size = new Float2(20f, 22f),
                    Text = cat.Active ? "⚡" : "◈",
                    TextColor = cat.Active ? InkWashTheme.GoldBright : InkWashTheme.TextTertiary,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 16f),
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                };
                item.AddChild(symbolLabel);

                var nameLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(36f, 6f),
                    Size = new Float2(140f, 20f),
                    Text = cat.Name,
                    TextColor = cat.Active ? InkWashTheme.GoldPrimary : InkWashTheme.TextSecondary,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 13f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                item.AddChild(nameLabel);

                var descLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(36f, 26f),
                    Size = new Float2(160f, 18f),
                    Text = cat.Desc,
                    TextColor = InkWashTheme.TextTertiary,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                item.AddChild(descLabel);

                if (cat.Active)
                {
                    var arrowLabel = new Label
                    {
                        AnchorPreset = AnchorPresets.TopLeft,
                        Location = new Float2(_categoryHost.Width - 22f, 14f),
                        Size = new Float2(20f, 22f),
                        Text = "›",
                        TextColor = InkWashTheme.GoldPrimary,
                        Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 18f),
                        HorizontalAlignment = TextAlignment.Center,
                        VerticalAlignment = TextAlignment.Center,
                    };
                    item.AddChild(arrowLabel);
                }

                _categoryItems.Add(item);
                _categoryHost.AddChild(item);
                cursorY += CategoryItemHeight + CategoryItemGap;
            }

            _timeFilterPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(ScreenEdge, ScreenEdge + 380f + 8f),
                Size = new Float2(LeftColumnWidth - ScreenEdge * 2f, 120f),
            };
            _leftPanel.AddChild(_timeFilterPanel);

            _timeFilterTitleLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 8f),
                Size = new Float2(LeftColumnWidth - ScreenEdge * 2f - 48f, 22f),
                Text = "时间筛选",
                TextColor = InkWashTheme.TextSecondary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 12f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _timeFilterPanel.AddChild(_timeFilterTitleLabel);

            var clockIcon = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(LeftColumnWidth - ScreenEdge * 2f - 28f, 8f),
                Size = new Float2(16f, 22f),
                Text = "◷",
                TextColor = InkWashTheme.TextTertiary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            _timeFilterPanel.AddChild(clockIcon);

            float btnWidth = (_timeFilterPanel.Width - 24f - TimeBtnGap) * 0.5f;
            for (int i = 0; i < _timeFilters.Length; i++)
            {
                int col = i % 2;
                int row = i / 2;
                bool active = i == ActiveTimeFilterIndex;
                var btn = new InkButton
                {
                    Variant = active ? InkButtonVariant.Primary : InkButtonVariant.Ghost,
                    Text = _timeFilters[i],
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(12f + col * (btnWidth + TimeBtnGap), 36f + row * (TimeBtnHeight + TimeBtnGap)),
                    Size = new Float2(btnWidth, TimeBtnHeight),
                };
                btn.Clicked += () => EmitGoldAtButton(btn);
                _timeFilterButtons.Add(btn);
                _timeFilterPanel.AddChild(btn);
            }

            AddChild(_leftPanel);
        }

        private void BuildMiddlePanel()
        {
            _middlePanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(800f, 600f),
            };

            _podiumHost = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(ScreenEdge, ScreenEdge),
                Size = new Float2(800f, PodiumHeight),
            };
            _middlePanel.AddChild(_podiumHost);

            BuildPodiumCard(_podiums[0], 0, "Silver");
            BuildPodiumCard(_podiums[1], 1, "Gold");
            BuildPodiumCard(_podiums[2], 2, "Bronze");

            _tablePanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(ScreenEdge, ScreenEdge + PodiumHeight + 8f),
                Size = new Float2(800f, 380f),
            };
            _middlePanel.AddChild(_tablePanel);

            BuildTableHeader();
            BuildTableBody();
            BuildLoadMoreIndicator();

            AddChild(_middlePanel);
        }

        private void BuildPodiumCard(PodiumInfo info, int slotIndex, string tier)
        {
            Color tierPrimary;
            Color tierBright;
            float marginTop;
            float avatarSize;
            float padding;

            switch (tier)
            {
                case "Gold":
                    tierPrimary = InkWashTheme.GoldPrimary;
                    tierBright = InkWashTheme.GoldBright;
                    marginTop = 0f;
                    avatarSize = 56f;
                    padding = 18f;
                    break;
                case "Silver":
                    tierPrimary = InkWashTheme.JadePrimary;
                    tierBright = InkWashTheme.JadeBright;
                    marginTop = 12f;
                    avatarSize = 48f;
                    padding = 14f;
                    break;
                default:
                    tierPrimary = InkWashTheme.BronzePrimary;
                    tierBright = InkWashTheme.BronzePrimary;
                    marginTop = 16f;
                    avatarSize = 44f;
                    padding = 12f;
                    break;
            }

            float cardWidth = (_podiumHost.Width - ColumnGap * 2f) / 3f;
            float cardX = slotIndex * (cardWidth + ColumnGap);

            var card = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(cardX, marginTop),
                Size = new Float2(cardWidth, PodiumHeight - marginTop),
            };
            card.BackgroundColor = new Color(tierPrimary.R, tierPrimary.G, tierPrimary.B, 0.08f);
            _podiumHost.AddChild(card);

            if (tier == "Gold")
            {
                var crownLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(cardWidth * 0.5f - 12f, -14f),
                    Size = new Float2(24f, 24f),
                    Text = "👑",
                    TextColor = InkWashTheme.GoldBright,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 20f),
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                };
                card.AddChild(crownLabel);
            }

            float badgeSize = tier == "Gold" ? 32f : 28f;
            var badge = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(cardWidth * 0.5f - badgeSize * 0.5f, tier == "Gold" ? 12f : 6f),
                Size = new Float2(badgeSize, badgeSize),
                BackgroundColor = InkWashTheme.BaseSecondary,
            };
            var rankNumLabel = new Label
            {
                AnchorPreset = AnchorPresets.StretchAll,
                Offsets = Margin.Zero,
                Text = info.Rank.ToString(),
                TextColor = tierBright,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, tier == "Gold" ? 16f : 14f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            badge.AddChild(rankNumLabel);
            card.AddChild(badge);

            float avatarY = (tier == "Gold" ? 12f : 6f) + badgeSize + 8f;
            var avatar = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(cardWidth * 0.5f - avatarSize * 0.5f, avatarY),
                Size = new Float2(avatarSize, avatarSize),
                BackgroundColor = new Color(tierPrimary.R, tierPrimary.G, tierPrimary.B, 0.15f),
            };
            var avatarCharLabel = new Label
            {
                AnchorPreset = AnchorPresets.StretchAll,
                Offsets = Margin.Zero,
                Text = info.AvatarChar,
                TextColor = tierBright,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, avatarSize == 56f ? 24f : (avatarSize == 48f ? 20f : 18f)),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            avatar.AddChild(avatarCharLabel);
            card.AddChild(avatar);

            float nameY = avatarY + avatarSize + 6f;
            var nameLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, nameY),
                Size = new Float2(cardWidth, 20f),
                Text = info.Name,
                TextColor = tier == "Gold" ? InkWashTheme.GoldBright : InkWashTheme.TextDefault,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, tier == "Gold" ? 15f : 14f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            card.AddChild(nameLabel);

            var sectLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, nameY + 20f),
                Size = new Float2(cardWidth, 18f),
                Text = info.Sect,
                TextColor = InkWashTheme.TextTertiary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            card.AddChild(sectLabel);

            var levelLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, nameY + 38f),
                Size = new Float2(cardWidth, 18f),
                Text = "Lv." + info.Level,
                TextColor = InkWashTheme.TextSecondary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 13f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            card.AddChild(levelLabel);

            var powerLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, nameY + 58f),
                Size = new Float2(cardWidth, 22f),
                Text = "⚡ " + info.Power.ToString("N0"),
                TextColor = tierBright,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, tier == "Gold" ? 15f : 13f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            card.AddChild(powerLabel);

            switch (tier)
            {
                case "Gold":
                    _podiumGold = card;
                    break;
                case "Silver":
                    _podiumSilver = card;
                    break;
                default:
                    _podiumBronze = card;
                    break;
            }
        }

        private void BuildTableHeader()
        {
            _tableHeader = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 0f),
                Size = new Float2(_tablePanel.Width, TableHeaderHeight),
                BackgroundColor = new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.04f),
            };
            _tablePanel.AddChild(_tableHeader);

            BuildHeaderCell(16f, 48f, "排名", TextAlignment.Center);
            BuildHeaderCell(64f, 44f, "", TextAlignment.Center);
            float nameX = 108f;
            float nameW = _tablePanel.Width - 32f - 380f;
            BuildHeaderCell(nameX, Mathf.Max(nameW, 120f), "角色名", TextAlignment.Near);
            BuildHeaderCell(nameX + Mathf.Max(nameW, 120f), 80f, "门派", TextAlignment.Near);
            BuildHeaderCell(nameX + Mathf.Max(nameW, 120f) + 80f, 56f, "等级", TextAlignment.Far);
            BuildHeaderCell(nameX + Mathf.Max(nameW, 120f) + 80f + 56f, 100f, "战力值", TextAlignment.Far);
            BuildHeaderCell(nameX + Mathf.Max(nameW, 120f) + 80f + 56f + 100f, 52f, "变化", TextAlignment.Center);

            var headerBorder = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, TableHeaderHeight),
                Size = new Float2(_tablePanel.Width, 1f),
                BackgroundColor = new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.3f),
            };
            _tablePanel.AddChild(headerBorder);
        }

        private void BuildHeaderCell(float x, float w, string text, TextAlignment align)
        {
            var cell = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(x, 0f),
                Size = new Float2(w, TableHeaderHeight),
                Text = text,
                TextColor = InkWashTheme.TextTertiary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                HorizontalAlignment = align,
                VerticalAlignment = TextAlignment.Center,
            };
            _tableHeader.AddChild(cell);
        }

        private void BuildTableBody()
        {
            _tableBodyHost = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, TableHeaderHeight + 1f),
                Size = new Float2(_tablePanel.Width, 12 * TableRowHeight),
            };
            _tablePanel.AddChild(_tableBodyHost);

            for (int i = 0; i < _rankRows.Length; i++)
            {
                BuildTableRow(_rankRows[i], i * TableRowHeight);
            }
        }

        private void BuildTableRow(RankRowInfo info, float yPos)
        {
            Color rowBg = info.IsMe
                ? new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.12f)
                : Color.Transparent;

            var row = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, yPos),
                Size = new Float2(_tableBodyHost.Width, TableRowHeight),
                BackgroundColor = rowBg,
            };

            float tw = _tableBodyHost.Width;
            float nameW = tw - 32f - 380f;
            if (nameW < 120f) nameW = 120f;

            var rankLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, 0f),
                Size = new Float2(48f, TableRowHeight),
                Text = info.Rank.ToString(),
                TextColor = InkWashTheme.TextSecondary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 14f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            row.AddChild(rankLabel);

            var avatar = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(72f, (TableRowHeight - 28f) * 0.5f),
                Size = new Float2(28f, 28f),
                BackgroundColor = InkWashTheme.BaseElevated,
            };
            var avatarCharLabel = new Label
            {
                AnchorPreset = AnchorPresets.StretchAll,
                Offsets = Margin.Zero,
                Text = info.AvatarChar,
                TextColor = InkWashTheme.TextSecondary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 12f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            avatar.AddChild(avatarCharLabel);
            row.AddChild(avatar);

            var nameLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(108f, 0f),
                Size = new Float2(nameW, TableRowHeight),
                Text = info.Name,
                TextColor = info.IsMe ? InkWashTheme.GoldBright : InkWashTheme.TextDefault,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            row.AddChild(nameLabel);

            float sectX = 108f + nameW;
            var sectLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(sectX, 0f),
                Size = new Float2(80f, TableRowHeight),
                Text = info.Sect,
                TextColor = InkWashTheme.TextSecondary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            row.AddChild(sectLabel);

            float levelX = sectX + 80f;
            var levelLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(levelX, 0f),
                Size = new Float2(56f, TableRowHeight),
                Text = "Lv." + info.Level,
                TextColor = InkWashTheme.TextSecondary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 13f),
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
            };
            row.AddChild(levelLabel);

            float powerX = levelX + 56f;
            var powerLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(powerX, 0f),
                Size = new Float2(100f, TableRowHeight),
                Text = info.Power.ToString("N0"),
                TextColor = InkWashTheme.GoldPrimary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 14f),
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
            };
            row.AddChild(powerLabel);

            float trendX = powerX + 100f;
            string trendText;
            Color trendColor;
            if (info.Trend > 0)
            {
                trendText = "↑ " + info.Trend;
                trendColor = InkWashTheme.GoldBright;
            }
            else if (info.Trend < 0)
            {
                trendText = "↓ " + Math.Abs(info.Trend);
                trendColor = InkWashTheme.TextTertiary;
            }
            else
            {
                trendText = "−";
                trendColor = InkWashTheme.TextTertiary;
            }

            var trendLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(trendX, 0f),
                Size = new Float2(52f, TableRowHeight),
                Text = trendText,
                TextColor = trendColor,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 11f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            row.AddChild(trendLabel);

            _tableRows.Add(row);
            _tableBodyHost.AddChild(row);
        }

        private void BuildLoadMoreIndicator()
        {
            _loadMoreLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, TableHeaderHeight + 1f + 12 * TableRowHeight + 8f),
                Size = new Float2(_tablePanel.Width, 24f),
                Text = "• • •  滚动加载更多",
                TextColor = InkWashTheme.TextTertiary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            _tablePanel.AddChild(_loadMoreLabel);
        }

        private void BuildRightPanel()
        {
            _rightPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(RightColumnWidth, 600f),
            };

            float cursorY = ScreenEdge;
            float cardWidth = RightColumnWidth - ScreenEdge * 2f;

            _myRankCard = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(ScreenEdge, cursorY),
                Size = new Float2(cardWidth, 144f),
            };
            _myRankCard.BackgroundColor = new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.06f);
            _rightPanel.AddChild(_myRankCard);

            _myRankTitleLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 10f),
                Size = new Float2(cardWidth - 48f, 20f),
                Text = "我的排名",
                TextColor = InkWashTheme.TextTertiary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 12f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _myRankCard.AddChild(_myRankTitleLabel);

            var awardIcon = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(cardWidth - 30f, 10f),
                Size = new Float2(16f, 20f),
                Text = "🏅",
                TextColor = InkWashTheme.GoldPrimary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 14f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            _myRankCard.AddChild(awardIcon);

            var myAvatar = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(14f, 38f),
                Size = new Float2(48f, 48f),
                BackgroundColor = new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.12f),
            };
            var myAvatarChar = new Label
            {
                AnchorPreset = AnchorPresets.StretchAll,
                Offsets = Margin.Zero,
                Text = _myInfo.AvatarChar,
                TextColor = InkWashTheme.GoldBright,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 20f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            myAvatar.AddChild(myAvatarChar);
            _myRankCard.AddChild(myAvatar);

            _myNameLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(70f, 40f),
                Size = new Float2(cardWidth - 80f, 20f),
                Text = _myInfo.Name,
                TextColor = InkWashTheme.TextDefault,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 15f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _myRankCard.AddChild(_myNameLabel);

            _mySectLevelLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(70f, 60f),
                Size = new Float2(cardWidth - 80f, 18f),
                Text = _myInfo.Sect + "  Lv." + _myInfo.Level,
                TextColor = InkWashTheme.TextSecondary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _myRankCard.AddChild(_mySectLevelLabel);

            var rankDivider = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 94f),
                Size = new Float2(cardWidth - 24f, 1f),
                BackgroundColor = new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.25f),
            };
            _myRankCard.AddChild(rankDivider);

            _myRankValueLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(14f, 102f),
                Size = new Float2(cardWidth * 0.5f - 14f, 34f),
                Text = "第 " + _myInfo.Rank + " 名",
                TextColor = InkWashTheme.GoldBright,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 22f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _myRankCard.AddChild(_myRankValueLabel);

            _myPowerValueLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(cardWidth * 0.5f, 104f),
                Size = new Float2(cardWidth * 0.5f - 14f, 28f),
                Text = "⚡ " + _myInfo.Power.ToString("N0"),
                TextColor = InkWashTheme.GoldPrimary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 16f),
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
            };
            _myRankCard.AddChild(_myPowerValueLabel);

            cursorY += 144f + 8f;

            _gapCard = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(ScreenEdge, cursorY),
                Size = new Float2(cardWidth, 130f),
            };
            _rightPanel.AddChild(_gapCard);

            _gapTitleLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 10f),
                Size = new Float2(cardWidth - 48f, 20f),
                Text = "距上一名",
                TextColor = InkWashTheme.TextSecondary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 12f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _gapCard.AddChild(_gapTitleLabel);

            var arrowIcon = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(cardWidth - 28f, 10f),
                Size = new Float2(16f, 20f),
                Text = "↑",
                TextColor = InkWashTheme.GoldBright,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 16f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            _gapCard.AddChild(arrowIcon);

            var prevRankLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(14f, 36f),
                Size = new Float2(120f, 18f),
                Text = "第" + _prevRank.Rank + "名战力",
                TextColor = InkWashTheme.TextTertiary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _gapCard.AddChild(prevRankLabel);

            _prevRankPowerLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(14f, 54f),
                Size = new Float2(120f, 22f),
                Text = _prevRank.Power.ToString("N0"),
                TextColor = InkWashTheme.TextSecondary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 16f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _gapCard.AddChild(_prevRankPowerLabel);

            var gapBg = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(cardWidth - 100f, 38f),
                Size = new Float2(86f, 44f),
                BackgroundColor = new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.08f),
            };
            var gapDescLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 4f),
                Size = new Float2(86f, 16f),
                Text = "差距",
                TextColor = InkWashTheme.TextTertiary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 9f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            gapBg.AddChild(gapDescLabel);
            _gapValueLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 18f),
                Size = new Float2(86f, 22f),
                Text = (_prevRank.Power - _myInfo.Power).ToString("N0"),
                TextColor = InkWashTheme.GoldPrimary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 15f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            gapBg.AddChild(_gapValueLabel);
            _gapCard.AddChild(gapBg);

            _gapProgressBar = new InkBar
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(14f, 100f),
                Size = new Float2(cardWidth - 28f, 8f),
                Value = (float)_myInfo.Power / _prevRank.Power,
                FillVariant = InkBarFillVariant.Gold,
            };
            _gapCard.AddChild(_gapProgressBar);

            cursorY += 130f + 8f;

            _trendCard = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(ScreenEdge, cursorY),
                Size = new Float2(cardWidth, 150f),
            };
            _rightPanel.AddChild(_trendCard);

            _trendTitleLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 10f),
                Size = new Float2(120f, 20f),
                Text = "本周趋势",
                TextColor = InkWashTheme.TextSecondary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 12f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _trendCard.AddChild(_trendTitleLabel);

            _trendChangeLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(cardWidth - 80f, 10f),
                Size = new Float2(68f, 20f),
                Text = "↑ +5",
                TextColor = InkWashTheme.GoldBright,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 12f),
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
            };
            _trendCard.AddChild(_trendChangeLabel);

            _trendChartHost = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 36f),
                Size = new Float2(cardWidth - 24f, 80f),
            };
            _trendCard.AddChild(_trendChartHost);

            BuildTrendChart();

            _trendStartLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 120f),
                Size = new Float2(cardWidth - 24f, 20f),
                Text = "本周起始：第55名",
                TextColor = InkWashTheme.TextTertiary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _trendCard.AddChild(_trendStartLabel);

            cursorY += 150f + 8f;

            _rewardCard = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(ScreenEdge, cursorY),
                Size = new Float2(cardWidth, 230f),
            };
            _rightPanel.AddChild(_rewardCard);

            _rewardTitleLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 10f),
                Size = new Float2(cardWidth - 48f, 20f),
                Text = "赛季奖励",
                TextColor = InkWashTheme.TextSecondary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 12f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _rewardCard.AddChild(_rewardTitleLabel);

            var giftIcon = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(cardWidth - 28f, 10f),
                Size = new Float2(16f, 20f),
                Text = "♦",
                TextColor = InkWashTheme.GoldPrimary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 16f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            _rewardCard.AddChild(giftIcon);

            _rewardListHost = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 36f),
                Size = new Float2(cardWidth - 24f, 130f),
            };
            _rewardCard.AddChild(_rewardListHost);

            BuildRewardList();

            _rewardHintLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 178f),
                Size = new Float2(cardWidth - 24f, 40f),
                Text = "排名进入前30可获额外奖励",
                TextColor = InkWashTheme.TextTertiary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            _rewardCard.AddChild(_rewardHintLabel);

            AddChild(_rightPanel);
        }

        private void BuildTrendChart()
        {
            float chartWidth = _trendChartHost.Width;
            float chartHeight = _trendChartHost.Height - 16f;
            float barGap = 4f;
            float barWidth = (chartWidth - barGap * 6f) / 7f;

            int maxRank = _weeklyRanks[0];
            int minRank = _weeklyRanks[0];
            for (int i = 1; i < _weeklyRanks.Length; i++)
            {
                if (_weeklyRanks[i] > maxRank) maxRank = _weeklyRanks[i];
                if (_weeklyRanks[i] < minRank) minRank = _weeklyRanks[i];
            }
            int rankRange = Math.Max(maxRank - minRank, 1);

            for (int i = 0; i < _weeklyRanks.Length; i++)
            {
                int rank = _weeklyRanks[i];
                float normalizedHeight = (float)(rank - minRank) / rankRange;
                float barH = Mathf.Lerp(chartHeight * 0.4f, chartHeight, 1f - normalizedHeight);
                bool isCurrent = i == _weeklyRanks.Length - 1;

                float barX = i * (barWidth + barGap);
                float barY = chartHeight - barH;

                Color barColor;
                if (isCurrent)
                {
                    barColor = InkWashTheme.GoldBright;
                }
                else if (normalizedHeight < 0.4f)
                {
                    barColor = new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.55f);
                }
                else
                {
                    barColor = new Color(InkWashTheme.TextTertiary.R, InkWashTheme.TextTertiary.G, InkWashTheme.TextTertiary.B, 0.45f);
                }

                var bar = new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(barX, barY),
                    Size = new Float2(barWidth, barH),
                    BackgroundColor = barColor,
                };
                _trendChartHost.AddChild(bar);

                var dayLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(barX, chartHeight + 2f),
                    Size = new Float2(barWidth, 14f),
                    Text = _weekdayLabels[i],
                    TextColor = isCurrent ? InkWashTheme.GoldPrimary : InkWashTheme.TextTertiary,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 9f),
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                };
                _trendChartHost.AddChild(dayLabel);
            }
        }

        private void BuildRewardList()
        {
            float cursorY = 0f;
            float itemHeight = 36f;
            float itemGap = 6f;

            for (int i = 0; i < _rewards.Length; i++)
            {
                var reward = _rewards[i];
                Color qualityColor;
                switch (reward.Quality)
                {
                    case "Legendary":
                        qualityColor = InkWashTheme.GoldBright;
                        break;
                    case "Rare":
                        qualityColor = InkWashTheme.JadePrimary;
                        break;
                    case "Epic":
                        qualityColor = InkWashTheme.VermilionPrimary;
                        break;
                    default:
                        qualityColor = InkWashTheme.TextSecondary;
                        break;
                }

                string qualitySymbol;
                switch (reward.Quality)
                {
                    case "Legendary":
                        qualitySymbol = "★";
                        break;
                    case "Rare":
                        qualitySymbol = "◆";
                        break;
                    default:
                        qualitySymbol = "✦";
                        break;
                }

                var item = new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(0f, cursorY),
                    Size = new Float2(_rewardListHost.Width, itemHeight),
                    BackgroundColor = new Color(qualityColor.R, qualityColor.G, qualityColor.B, 0.08f),
                };

                var iconBox = new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(6f, 4f),
                    Size = new Float2(28f, 28f),
                    BackgroundColor = new Color(qualityColor.R, qualityColor.G, qualityColor.B, 0.15f),
                };
                var iconLabel = new Label
                {
                    AnchorPreset = AnchorPresets.StretchAll,
                    Offsets = Margin.Zero,
                    Text = qualitySymbol,
                    TextColor = qualityColor,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 14f),
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                };
                iconBox.AddChild(iconLabel);
                item.AddChild(iconBox);

                var nameLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(40f, 4f),
                    Size = new Float2(_rewardListHost.Width - 100f, 16f),
                    Text = reward.Name,
                    TextColor = InkWashTheme.TextDefault,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 12f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                item.AddChild(nameLabel);

                var descLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(40f, 18f),
                    Size = new Float2(_rewardListHost.Width - 100f, 16f),
                    Text = reward.Desc,
                    TextColor = InkWashTheme.TextTertiary,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                item.AddChild(descLabel);

                var countLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(_rewardListHost.Width - 70f, 4f),
                    Size = new Float2(64f, 28f),
                    Text = "x" + reward.Count,
                    TextColor = qualityColor,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 14f),
                    HorizontalAlignment = TextAlignment.Far,
                    VerticalAlignment = TextAlignment.Center,
                };
                item.AddChild(countLabel);

                _rewardListHost.AddChild(item);
                cursorY += itemHeight + itemGap;
            }
        }

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
                FlaxEngine.Debug.LogWarning($"[LeaderboardPage] EmitGoldAtButton 失败: {ex.Message}");
            }
        }

        public void RefreshLayout()
        {
            try
            {
                float sw = Width;
                float sh = Height;
                float contentTop = TopHeaderHeight + ContentPadding;
                float contentBottom = sh - ContentPadding;
                float contentH = contentBottom - contentTop;
                float middleWidth = sw - LeftColumnWidth - RightColumnWidth - ColumnGap * 2f - ScreenEdge * 2f;

                if (_topHeader != null)
                {
                    _topHeader.Location = Float2.Zero;
                    _topHeader.Size = new Float2(sw, TopHeaderHeight);
                }

                if (_leftPanel != null)
                {
                    _leftPanel.Location = new Float2(ScreenEdge, contentTop);
                    _leftPanel.Size = new Float2(LeftColumnWidth, contentH);
                }

                if (_middlePanel != null)
                {
                    _middlePanel.Location = new Float2(ScreenEdge + LeftColumnWidth + ColumnGap, contentTop);
                    _middlePanel.Size = new Float2(middleWidth, contentH);

                    if (_podiumHost != null)
                    {
                        _podiumHost.Size = new Float2(middleWidth - ScreenEdge * 2f, PodiumHeight);
                        RebuildPodiumLayout(middleWidth - ScreenEdge * 2f);
                    }

                    if (_tablePanel != null)
                    {
                        float tableWidth = middleWidth - ScreenEdge * 2f;
                        _tablePanel.Size = new Float2(tableWidth, contentH - PodiumHeight - 16f);
                        if (_tableHeader != null)
                        {
                            _tableHeader.Size = new Float2(tableWidth, TableHeaderHeight);
                        }
                        if (_tableBodyHost != null)
                        {
                            _tableBodyHost.Size = new Float2(tableWidth, 12 * TableRowHeight);
                            foreach (var row in _tableRows)
                            {
                                row.Size = new Float2(tableWidth, TableRowHeight);
                            }
                        }
                        if (_loadMoreLabel != null)
                        {
                            _loadMoreLabel.Location = new Float2(0f, TableHeaderHeight + 1f + 12 * TableRowHeight + 8f);
                            _loadMoreLabel.Size = new Float2(tableWidth, 24f);
                        }
                    }
                }

                if (_rightPanel != null)
                {
                    _rightPanel.Location = new Float2(sw - RightColumnWidth - ScreenEdge, contentTop);
                    _rightPanel.Size = new Float2(RightColumnWidth, contentH);
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[LeaderboardPage] RefreshLayout 失败: {ex.Message}");
            }
        }

        private void RebuildPodiumLayout(float totalWidth)
        {
            if (_podiumHost == null)
                return;

            float cardWidth = (totalWidth - ColumnGap * 2f) / 3f;

            if (_podiumSilver != null)
            {
                _podiumSilver.Location = new Float2(0f, 12f);
                _podiumSilver.Size = new Float2(cardWidth, PodiumHeight - 12f);
            }
            if (_podiumGold != null)
            {
                _podiumGold.Location = new Float2(cardWidth + ColumnGap, 0f);
                _podiumGold.Size = new Float2(cardWidth, PodiumHeight);
            }
            if (_podiumBronze != null)
            {
                _podiumBronze.Location = new Float2((cardWidth + ColumnGap) * 2f, 16f);
                _podiumBronze.Size = new Float2(cardWidth, PodiumHeight - 16f);
            }
        }

        public override void OnParentResized()
        {
            base.OnParentResized();
            RefreshLayout();
        }
    }
}

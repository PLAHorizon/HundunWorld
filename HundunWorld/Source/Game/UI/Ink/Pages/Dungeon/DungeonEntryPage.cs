using FlaxEngine;
using FlaxEngine.GUI;
using Game.Character.Attributes;
using HundunWorld.Game.UI.StyleSystem;
using System;

namespace HundunWorld.Game.UI.Ink.Pages.Dungeon
{
    public class DungeonEntryPage : ContainerControl, IInkPage
    {
        private const float PanelW = 1400f;
        private const float PanelH = 900f;
        private const float HeaderH = 86f;
        private const float LeftW = 260f;
        private const float RightW = 300f;
        private const float CardH = 330f;
        private const float CardGap = 16f;
        private const float DividerW = 1f;

        private ContainerControl _mainPanel;
        private ContainerControl _header;
        private ContainerControl _body;
        private ContainerControl _leftPanel;
        private ContainerControl _middlePanel;
        private ContainerControl _rightPanel;
        private ContainerControl _rightScroll;
        private Label _titleLabel;
        private InkButton _backButton;
        private Label _selectedNameLabel;
        private Label _partyPowerLabel;
        private Label _powerStatusLabel;
        private Label _strategyLabel;
        private ContainerControl _powerBarFill;
        private Label _powerBarMarker;
        private InkButton _enterButton;
        private ContainerControl[] _cards;
        private InkButton[] _selectButtons;
        private InkButton[] _rightDiffButtons;
        private ContainerControl[] _historyRows;

        public event Action<string> NavigationRequested;
        public InkParticleSystem ParticleSystem { get; set; }

        private CharacterAttributesComponent _boundCharacter;

        public void BindCharacter(CharacterAttributesComponent component)
        {
            _boundCharacter = component;
        }

        public DungeonEntryPage()
        {
            AnchorPreset = AnchorPresets.StretchAll;
            Offsets = Margin.Zero;
            BackgroundColor = InkWashTheme.Scrim;
            ClipChildren = false;
            AutoFocus = false;
            BuildLayout();
        }

        private void BuildLayout()
        {
            _mainPanel = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(PanelW, PanelH),
            };
            AddChild(_mainPanel);
            BuildHeader();
            BuildBody();
        }

        private void BuildHeader()
        {
            _header = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Float2(PanelW, HeaderH),
                BackgroundColor = new Color(20f / 255f, 23f / 255f, 30f / 255f, 0.85f),
            };
            _mainPanel.AddChild(_header);

            float tY = 16f;
            _titleLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(24f, tY),
                Size = new Float2(180f, 28f),
                Text = "江湖秘境",
                TextColor = InkWashTheme.GoldPrimary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 22f),
                HorizontalAlignment = TextAlignment.Near,
            };
            _header.AddChild(_titleLabel);

            var subtitleLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(24f, tY + 28f),
                Size = new Float2(200f, 14f),
                Text = "DUNGEON ENTRY",
                TextColor = InkWashTheme.TextTertiary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f),
                HorizontalAlignment = TextAlignment.Near,
            };
            _header.AddChild(subtitleLabel);

            var timeLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(PanelW - 300f, tY + 4f),
                Size = new Float2(120f, 20f),
                Text = "辰时三刻",
                TextColor = InkWashTheme.TextSecondary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 12f),
                HorizontalAlignment = TextAlignment.Far,
            };
            _header.AddChild(timeLabel);

            _backButton = new InkButton
            {
                Variant = InkButtonVariant.Default,
                ButtonSize = InkButtonSize.Sm,
                Text = "返回",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(PanelW - 80f - 24f, tY),
                Size = new Float2(80f, 32f),
            };
            _backButton.Clicked += () => NavigationRequested?.Invoke(InkPageDomIds.CombatHud);
            _header.AddChild(_backButton);

            float sY = tY + 50f;
            var powerLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(24f, sY),
                Size = new Float2(200f, 18f),
                Text = "总战力 32,450",
                TextColor = InkWashTheme.GoldBright,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 14f),
                HorizontalAlignment = TextAlignment.Near,
            };
            _header.AddChild(powerLabel);

            var d1 = new Panel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(190f, sY + 2f),
                Size = new Float2(1f, 14f),
                BackgroundColor = InkWashTheme.BorderGold,
            };
            _header.AddChild(d1);

            var clearLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(204f, sY),
                Size = new Float2(150f, 18f),
                Text = "通关数 47",
                TextColor = InkWashTheme.JadeBright,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 14f),
                HorizontalAlignment = TextAlignment.Near,
            };
            _header.AddChild(clearLabel);

            var d2 = new Panel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(360f, sY + 2f),
                Size = new Float2(1f, 14f),
                BackgroundColor = InkWashTheme.BorderGold,
            };
            _header.AddChild(d2);

            var scoreLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(374f, sY),
                Size = new Float2(200f, 18f),
                Text = "秘境积分 1,280",
                TextColor = InkWashTheme.BloodBright,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 14f),
                HorizontalAlignment = TextAlignment.Near,
            };
            _header.AddChild(scoreLabel);

            var hd = new Panel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, HeaderH - 1f),
                Size = new Float2(PanelW, 1f),
                BackgroundColor = InkWashTheme.BorderGold,
            };
            _header.AddChild(hd);
        }

        private void BuildBody()
        {
            float bodyY = HeaderH;
            float bodyH = PanelH - HeaderH;

            _body = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, bodyY),
                Size = new Float2(PanelW, bodyH),
                BackgroundColor = InkWashTheme.BorderGold,
            };
            _mainPanel.AddChild(_body);

            BuildLeftPanel();
            BuildMiddlePanel();
            BuildRightPanel();
        }

        private void BuildLeftPanel()
        {
            float bodyH = _body.Height;
            _leftPanel = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Float2(LeftW, bodyH),
                BackgroundColor = new Color(20f / 255f, 23f / 255f, 30f / 255f, 0.92f),
            };
            _body.AddChild(_leftPanel);

            var catHeader = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, 12f),
                Size = new Float2(LeftW - 32f, 20f),
                Text = "秘境分类",
                TextColor = InkWashTheme.TextSecondary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 13f),
                HorizontalAlignment = TextAlignment.Near,
            };
            _leftPanel.AddChild(catHeader);

            string[][] catItems =
            {
                new[] { "修行洞府|普通|日 3/3|shilian", "试炼塔|困难|日 2/5|shilian", "心魔幻境|噩梦|周 1/3|xinmo" },
                new[] { "幽冥洞|困难|日 2/5|youming", "天劫阵|噩梦|日 1/3|tianjie", "龙渊秘境|地狱|周 0/2|longyuan" },
                new[] { "太虚阁|噩梦|周 1/2|taixu" },
                new[] { "古墓探秘|双倍|剩2时|gumu", "中秋灯会|节日|剩3日|denghui" },
            };
            string[] catNames = { "单人秘境", "组队秘境", "门派秘境", "限时活动" };
            string[] catCounts = { "3", "3", "1", "2" };
            Color[] catTagColors = { InkWashTheme.QualityCommon, InkWashTheme.QualityRare, InkWashTheme.QualityEpic, InkWashTheme.QualityLegendary, InkWashTheme.BloodBright };

            float cY = 40f;
            for (int g = 0; g < catItems.Length; g++)
            {
                var group = new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(0f, cY),
                    Size = new Float2(LeftW, 28f + catItems[g].Length * 52f),
                    BackgroundColor = Color.Transparent,
                };

                var groupHeader = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(16f, 6f),
                    Size = new Float2(LeftW - 60f, 18f),
                    Text = catNames[g],
                    TextColor = g == 0 ? InkWashTheme.GoldPrimary : InkWashTheme.TextTertiary,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 13f),
                    HorizontalAlignment = TextAlignment.Near,
                };
                group.AddChild(groupHeader);

                var countLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(LeftW - 48f, 6f),
                    Size = new Float2(32f, 18f),
                    Text = catCounts[g],
                    TextColor = g == 3 ? InkWashTheme.BloodBright : InkWashTheme.TextTertiary,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 11f),
                    HorizontalAlignment = TextAlignment.Center,
                };
                group.AddChild(countLabel);

                for (int i = 0; i < catItems[g].Length; i++)
                {
                    var parts = catItems[g][i].Split('|');
                    string name = parts[0];
                    string tag = parts[1];
                    string remain = parts[2];
                    bool isActive = g == 0 && i == 0;

                    var item = new ContainerControl
                    {
                        AnchorPreset = AnchorPresets.TopLeft,
                        Location = new Float2(8f, 28f + i * 52f),
                        Size = new Float2(LeftW - 16f, 48f),
                        BackgroundColor = isActive ? new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.10f) : Color.Transparent,
                    };

                    var nameLabel = new Label
                    {
                        AnchorPreset = AnchorPresets.TopLeft,
                        Location = new Float2(8f, 3f),
                        Size = new Float2(LeftW - 40f, 20f),
                        Text = name,
                        TextColor = isActive ? InkWashTheme.GoldBright : InkWashTheme.TextDefault,
                        Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 12f),
                        HorizontalAlignment = TextAlignment.Near,
                    };
                    item.AddChild(nameLabel);

                    Color tagColor;
                    switch (tag)
                    {
                        case "普通": tagColor = InkWashTheme.QualityCommon; break;
                        case "困难": tagColor = InkWashTheme.QualityRare; break;
                        case "噩梦": tagColor = InkWashTheme.QualityEpic; break;
                        case "地狱": tagColor = InkWashTheme.QualityLegendary; break;
                        default: tagColor = InkWashTheme.BloodBright; break;
                    }

                    var tagLabel = new Label
                    {
                        AnchorPreset = AnchorPresets.TopLeft,
                        Location = new Float2(8f, 26f),
                        Size = new Float2(32f, 16f),
                        Text = tag,
                        TextColor = tagColor,
                        Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 9f),
                        BackgroundColor = new Color(tagColor.R, tagColor.G, tagColor.B, 0.12f),
                        HorizontalAlignment = TextAlignment.Center,
                        VerticalAlignment = TextAlignment.Center,
                    };
                    item.AddChild(tagLabel);

                    Color remainColor = tag == "双倍" || tag == "节日" ? InkWashTheme.BloodBright : InkWashTheme.TextTertiary;
                    var remainLabel = new Label
                    {
                        AnchorPreset = AnchorPresets.TopLeft,
                        Location = new Float2(44f, 26f),
                        Size = new Float2(LeftW - 68f, 16f),
                        Text = remain,
                        TextColor = remainColor,
                        Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 10f),
                        HorizontalAlignment = TextAlignment.Near,
                        VerticalAlignment = TextAlignment.Center,
                    };
                    item.AddChild(remainLabel);

                    group.AddChild(item);
                }

                _leftPanel.AddChild(group);
                cY += 28f + catItems[g].Length * 52f + 4f;
            }

            var leftDivider = new Panel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(LeftW, 0f),
                Size = new Float2(DividerW, bodyH),
                BackgroundColor = Color.Transparent,
            };
            _body.AddChild(leftDivider);
        }

        private void BuildMiddlePanel()
        {
            float bodyH = _body.Height;
            float midX = LeftW + DividerW;
            float midW = PanelW - LeftW - RightW - DividerW * 2f;

            _middlePanel = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(midX, 0f),
                Size = new Float2(midW, bodyH),
                BackgroundColor = new Color(14f / 255f, 16f / 255f, 22f / 255f, 0.60f),
            };
            _body.AddChild(_middlePanel);

            var midTitle = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(20f, 12f),
                Size = new Float2(300f, 20f),
                Text = "单人秘境 · 共 3 处秘境",
                TextColor = InkWashTheme.GoldPrimary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 14f),
                HorizontalAlignment = TextAlignment.Near,
            };
            _middlePanel.AddChild(midTitle);

            var midDivider = new Panel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 36f),
                Size = new Float2(midW, 1f),
                BackgroundColor = InkWashTheme.BorderFaint,
            };
            _middlePanel.AddChild(midDivider);

            string[] cardNames = { "修行洞府", "试炼塔", "心魔幻境" };
            string[] cardEnNames = { "CULTIVATION CAVE", "TRIAL PAGODA", "INNER DEMON" };
            string[] cardDiffTexts = { "普通", "困难", "噩梦" };
            Color[] cardDiffColors = { InkWashTheme.QualityCommon, InkWashTheme.QualityRare, InkWashTheme.QualityEpic };
            string[] cardPowers = { "12,000", "25,000", "45,000" };
            string[] cardTimes = { "~8分钟", "~15分钟", "~20分钟" };
            string[] cardDescs =
            {
                "隐于深山的修行之地，内有前辈留下的武学残卷，可助修行者参悟心法奥义。",
                "七层试炼之塔，每层皆有不同考验，登顶者可获心法秘籍与上古遗宝。",
                "直面内心深处的魔障，唯有心志坚定者方可破幻而出，超脱凡尘。",
            };
            string[][] cardBosses =
            {
                new[] { "石傀儡", "玄蛇长老", "洞府守灵" },
                new[] { "铁掌门人", "幻影剑客", "塔灵", "千面书生" },
                new[] { "贪念之魔", "嗔怒之魔", "痴念之魔", "心魔本相" },
            };
            string[] cardRewards = { "普通 → 稀有", "稀有 → 史诗", "史诗 → 传说" };
            string[] cardRemains = { "今日 3/3", "今日 2/5", "本周 1/3" };
            bool[] cardSelected = { false, true, false };

            float cardW = midW - 40f;
            _cards = new ContainerControl[3];
            _selectButtons = new InkButton[3];

            float cardY = 48f;
            for (int i = 0; i < 3; i++)
            {
                var card = new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(20f, cardY),
                    Size = new Float2(cardW, CardH),
                    BackgroundColor = cardSelected[i]
                        ? new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.08f)
                        : new Color(20f / 255f, 23f / 255f, 30f / 255f, 1f),
                };

                var nameLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(14f, 10f),
                    Size = new Float2(280f, 24f),
                    Text = cardNames[i],
                    TextColor = InkWashTheme.TextDefault,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 18f),
                    HorizontalAlignment = TextAlignment.Near,
                };
                card.AddChild(nameLabel);

                var enLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(14f, 34f),
                    Size = new Float2(280f, 14f),
                    Text = cardEnNames[i],
                    TextColor = InkWashTheme.TextTertiary,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f),
                    HorizontalAlignment = TextAlignment.Near,
                };
                card.AddChild(enLabel);

                var diffBadge = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(cardW - 70f, 14f),
                    Size = new Float2(56f, 22f),
                    Text = cardDiffTexts[i],
                    TextColor = cardDiffColors[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                    BackgroundColor = new Color(cardDiffColors[i].R, cardDiffColors[i].G, cardDiffColors[i].B, 0.15f),
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                };
                card.AddChild(diffBadge);

                var statsLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(14f, 58f),
                    Size = new Float2(cardW - 28f, 18f),
                    Text = $"推荐战力 {cardPowers[i]}    通关时间 {cardTimes[i]}",
                    TextColor = InkWashTheme.TextSecondary,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 12f),
                    HorizontalAlignment = TextAlignment.Near,
                };
                card.AddChild(statsLabel);

                var descLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(14f, 80f),
                    Size = new Float2(cardW - 28f, 48f),
                    Text = cardDescs[i],
                    TextColor = InkWashTheme.TextSecondary,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Near,
                };
                card.AddChild(descLabel);

                var bossTitle = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(14f, 134f),
                    Size = new Float2(cardW - 28f, 16f),
                    Text = "守关首领",
                    TextColor = InkWashTheme.TextTertiary,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 11f),
                    HorizontalAlignment = TextAlignment.Near,
                };
                card.AddChild(bossTitle);

                var bossText = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(14f, 152f),
                    Size = new Float2(cardW - 28f, 40f),
                    Text = string.Join("  ", cardBosses[i]),
                    TextColor = InkWashTheme.TextDefault,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Near,
                };
                card.AddChild(bossText);

                var rewardTitle = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(14f, 200f),
                    Size = new Float2(cardW - 28f, 16f),
                    Text = "可能掉落",
                    TextColor = InkWashTheme.TextTertiary,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 11f),
                    HorizontalAlignment = TextAlignment.Near,
                };
                card.AddChild(rewardTitle);

                var rewardLbl = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(14f, 218f),
                    Size = new Float2(cardW - 28f, 18f),
                    Text = cardRewards[i],
                    TextColor = InkWashTheme.TextBrand,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                    HorizontalAlignment = TextAlignment.Near,
                };
                card.AddChild(rewardLbl);

                var cardDivider = new Panel
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(0f, CardH - 56f),
                    Size = new Float2(cardW, 1f),
                    BackgroundColor = InkWashTheme.Divider,
                };
                card.AddChild(cardDivider);

                var remainLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(14f, CardH - 46f),
                    Size = new Float2(200f, 32f),
                    Text = cardRemains[i],
                    TextColor = InkWashTheme.TextSecondary,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 13f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                card.AddChild(remainLabel);

                var selectBtn = new InkButton
                {
                    Variant = cardSelected[i] ? InkButtonVariant.Primary : InkButtonVariant.Default,
                    ButtonSize = InkButtonSize.Sm,
                    Text = cardSelected[i] ? "已选" : "选择",
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(cardW - 90f - 14f, CardH - 44f),
                    Size = new Float2(90f, 30f),
                };
                card.AddChild(selectBtn);
                _selectButtons[i] = selectBtn;

                _cards[i] = card;
                _middlePanel.AddChild(card);
                cardY += CardH + CardGap;
            }

            var rightDiv = new Panel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(midX + midW, 0f),
                Size = new Float2(DividerW, bodyH),
                BackgroundColor = Color.Transparent,
            };
            _body.AddChild(rightDiv);
        }

        private void BuildRightPanel()
        {
            float bodyH = _body.Height;
            float rX = PanelW - RightW;

            _rightPanel = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(rX, 0f),
                Size = new Float2(RightW, bodyH),
                BackgroundColor = new Color(20f / 255f, 23f / 255f, 30f / 255f, 0.92f),
            };
            _body.AddChild(_rightPanel);

            var partyTitle = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, 12f),
                Size = new Float2(RightW - 32f, 20f),
                Text = "队伍配置",
                TextColor = InkWashTheme.TextSecondary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 13f),
                HorizontalAlignment = TextAlignment.Near,
            };
            _rightPanel.AddChild(partyTitle);

            var partyDivider = new Panel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 36f),
                Size = new Float2(RightW, 1f),
                BackgroundColor = InkWashTheme.BorderFaint,
            };
            _rightPanel.AddChild(partyDivider);

            _rightScroll = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 40f),
                Size = new Float2(RightW, bodyH - 40f - 72f),
                BackgroundColor = Color.Transparent,
            };
            _rightPanel.AddChild(_rightScroll);

            float y = 8f;

            var selectedTitle = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, y),
                Size = new Float2(RightW - 32f, 16f),
                Text = "当前选择  困难",
                TextColor = InkWashTheme.TextTertiary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f),
                HorizontalAlignment = TextAlignment.Near,
            };
            _rightScroll.AddChild(selectedTitle);

            _selectedNameLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, y + 18f),
                Size = new Float2(RightW - 32f, 24f),
                Text = "试炼塔",
                TextColor = InkWashTheme.GoldPrimary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 16f),
                HorizontalAlignment = TextAlignment.Near,
            };
            _rightScroll.AddChild(_selectedNameLabel);
            y += 50f;

            var memberTitle = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, y),
                Size = new Float2(RightW - 32f, 16f),
                Text = "队伍成员  1/1",
                TextColor = InkWashTheme.TextTertiary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 11f),
                HorizontalAlignment = TextAlignment.Near,
            };
            _rightScroll.AddChild(memberTitle);
            y += 22f;

            var memberRow = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, y),
                Size = new Float2(RightW - 32f, 44f),
                BackgroundColor = new Color(20f / 255f, 23f / 255f, 30f / 255f, 1f),
            };
            var avatarLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(8f, 6f),
                Size = new Float2(32f, 32f),
                Text = "游",
                TextColor = InkWashTheme.TextInverse,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 14f),
                BackgroundColor = InkWashTheme.GoldPrimary,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            memberRow.AddChild(avatarLabel);
            var memberInfo = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(48f, 4f),
                Size = new Float2(RightW - 96f, 36f),
                Text = "游侠 (队长)\n剑客 · 32,450",
                TextColor = InkWashTheme.TextDefault,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            memberRow.AddChild(memberInfo);
            _rightScroll.AddChild(memberRow);
            y += 54f;

            var powerTitle = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, y),
                Size = new Float2(RightW - 32f, 16f),
                Text = "战力对比",
                TextColor = InkWashTheme.TextTertiary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 11f),
                HorizontalAlignment = TextAlignment.Near,
            };
            _rightScroll.AddChild(powerTitle);
            y += 22f;

            var powerBox = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, y),
                Size = new Float2(RightW - 32f, 60f),
                BackgroundColor = new Color(20f / 255f, 23f / 255f, 30f / 255f, 1f),
            };

            _partyPowerLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(10f, 6f),
                Size = new Float2(RightW - 52f, 14f),
                Text = "队伍战力  32,450",
                TextColor = InkWashTheme.GoldBright,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 13f),
                HorizontalAlignment = TextAlignment.Near,
            };
            powerBox.AddChild(_partyPowerLabel);

            _powerBarFill = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(10f, 24f),
                Size = new Float2(RightW - 52f, 4f),
                BackgroundColor = new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.12f),
            };
            var fill = new Panel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Float2(RightW - 52f, 4f),
                BackgroundColor = InkWashTheme.GoldPrimary,
            };
            _powerBarFill.AddChild(fill);

            _powerBarMarker = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2((RightW - 52f) * 0.77f - 1f, -3f),
                Size = new Float2(2f, 10f),
                Text = string.Empty,
                BackgroundColor = InkWashTheme.TextTertiary,
            };
            _powerBarFill.AddChild(_powerBarMarker);
            powerBox.AddChild(_powerBarFill);

            var recommendedLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(10f, 32f),
                Size = new Float2(RightW - 52f, 14f),
                Text = "推荐战力  25,000",
                TextColor = InkWashTheme.TextSecondary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 11f),
                HorizontalAlignment = TextAlignment.Near,
            };
            powerBox.AddChild(recommendedLabel);

            _powerStatusLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(10f, 46f),
                Size = new Float2(RightW - 52f, 14f),
                Text = "战力达标",
                TextColor = InkWashTheme.JadePrimary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                HorizontalAlignment = TextAlignment.Near,
            };
            powerBox.AddChild(_powerStatusLabel);
            _rightScroll.AddChild(powerBox);
            y += 66f;

            var diffTitle = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, y),
                Size = new Float2(RightW - 32f, 16f),
                Text = "难度选择",
                TextColor = InkWashTheme.TextTertiary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 11f),
                HorizontalAlignment = TextAlignment.Near,
            };
            _rightScroll.AddChild(diffTitle);
            y += 22f;

            _rightDiffButtons = new InkButton[4];
            string[] diffNames = { "普通", "困难", "噩梦", "地狱" };
            bool[] diffActive = { false, true, false, false };
            float btnW = (RightW - 32f - 8f) * 0.5f;
            float btnH = 30f;
            for (int i = 0; i < 4; i++)
            {
                int row = i / 2;
                int col = i % 2;
                var btn = new InkButton
                {
                    Variant = diffActive[i] ? InkButtonVariant.Primary : InkButtonVariant.Default,
                    ButtonSize = InkButtonSize.Sm,
                    Text = diffNames[i],
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(16f + col * (btnW + 8f), y + row * (btnH + 6f)),
                    Size = new Float2(btnW, btnH),
                };
                _rightDiffButtons[i] = btn;
                _rightScroll.AddChild(btn);
            }
            y += 2 * (btnH + 6f) + 8f;

            var strategyTitle = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, y),
                Size = new Float2(RightW - 32f, 16f),
                Text = "攻略提示",
                TextColor = InkWashTheme.TextTertiary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 11f),
                HorizontalAlignment = TextAlignment.Near,
            };
            _rightScroll.AddChild(strategyTitle);
            y += 22f;

            _strategyLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, y),
                Size = new Float2(RightW - 32f, 52f),
                Text = "第三层塔灵会施展群体封印，建议携带解封符箓。千面书生形态切换时有三秒破绽窗口，把握时机连招可速通。",
                TextColor = InkWashTheme.TextSecondary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                BackgroundColor = new Color(126f / 255f, 171f / 255f, 158f / 255f, 0.06f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Near,
            };
            _rightScroll.AddChild(_strategyLabel);
            y += 58f;

            var historyTitle = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, y),
                Size = new Float2(RightW - 32f, 16f),
                Text = "最近通关",
                TextColor = InkWashTheme.TextTertiary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 11f),
                HorizontalAlignment = TextAlignment.Near,
            };
            _rightScroll.AddChild(historyTitle);
            y += 22f;

            _historyRows = new ContainerControl[3];
            string[] hIcons = { "噩", "普", "困" };
            string[] hNames = { "心魔幻境", "修行洞府", "试炼塔" };
            string[] hMetas = { "噩梦 · 20分12秒", "普通 · 7分35秒", "困难 · 14分08秒" };
            string[] hTimes = { "07-14", "07-14", "07-13" };
            Color[] hColors = { InkWashTheme.QualityEpic, InkWashTheme.QualityCommon, InkWashTheme.QualityRare };

            for (int i = 0; i < 3; i++)
            {
                var row = new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(16f, y + i * 36f),
                    Size = new Float2(RightW - 32f, 32f),
                    BackgroundColor = new Color(20f / 255f, 23f / 255f, 30f / 255f, 1f),
                };

                var icon = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(6f, 5f),
                    Size = new Float2(22f, 22f),
                    Text = hIcons[i],
                    TextColor = InkWashTheme.TextInverse,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 10f),
                    BackgroundColor = hColors[i],
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                };
                row.AddChild(icon);

                var info = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(34f, 2f),
                    Size = new Float2(RightW - 100f, 28f),
                    Text = $"{hNames[i]}\n{hMetas[i]}",
                    TextColor = InkWashTheme.TextDefault,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                row.AddChild(info);

                var timeLbl = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(RightW - 32f - 48f, 0f),
                    Size = new Float2(48f, 32f),
                    Text = hTimes[i],
                    TextColor = InkWashTheme.TextTertiary,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 10f),
                    HorizontalAlignment = TextAlignment.Far,
                    VerticalAlignment = TextAlignment.Center,
                };
                row.AddChild(timeLbl);

                _historyRows[i] = row;
                _rightScroll.AddChild(row);
            }

            _enterButton = new InkButton
            {
                Variant = InkButtonVariant.Primary,
                ButtonSize = InkButtonSize.Lg,
                Text = "进入秘境",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, bodyH - 68f),
                Size = new Float2(RightW - 32f, 42f),
            };
            _rightPanel.AddChild(_enterButton);

            var tipLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, bodyH - 22f),
                Size = new Float2(RightW - 32f, 14f),
                Text = "进入后将消耗今日次数",
                TextColor = InkWashTheme.TextTertiary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f),
                HorizontalAlignment = TextAlignment.Center,
            };
            _rightPanel.AddChild(tipLabel);
        }

        public void RefreshLayout()
        {
            float sw = Width;
            float sh = Height;
            if (_mainPanel != null)
            {
                _mainPanel.Location = new Float2(
                    sw * 0.5f - PanelW * 0.5f,
                    sh * 0.5f - PanelH * 0.5f);
            }
        }

        public override void OnParentResized()
        {
            base.OnParentResized();
            RefreshLayout();
        }
    }
}

using FlaxEngine;
using FlaxEngine.GUI;
using Game.Character.Attributes;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.StyleSystem;
using System;

namespace HundunWorld.Game.UI.Ink.Pages.Social
{
    public class AchievementPage : ContainerControl, IInkPage
    {
        private const float ScreenEdge = 16f;
        private const float HeaderHeight = 60f;
        private const float TreeWidth = 240f;
        private const float DetailWidth = 300f;
        private const float CardGap = 12f;
        private const float CardWidth = 180f;
        private const float CardHeight = 158f;

        private InkBackButton _backButton;
        private Label _titleLabel;
        private Label _subtitleLabel;
        private Label _overallLabel;
        private Label _overallCount;
        private Label _overallPct;
        private InkBar _overallBar;
        private Label _pointsValue;
        private InkButton _filterBtn;
        private InkButton _closeBtn;

        private Panel _tree;
        private Label _treeHeadTitle;
        private Label _treeHeadHint;
        private CategoryItem[] _catItems;
        private ContainerControl _treeFoot;

        private Panel _gridSection;
        private ContainerControl _gridHead;
        private Label _breadcrumb;
        private InkButton[] _filterChips;
        private Panel _gridScroll;
        private AchievementCard[] _cards;

        private Panel _detail;
        private ContainerControl _detailScroll;
        private ContainerControl _detailHero;
        private ContainerControl _detailHeroIcon;
        private Label _detailTier;
        private Label _detailName;
        private Label _detailPoints;
        private Label _detailStatus;
        private Label _detailDesc;
        private ContainerControl _condSection;
        private Label _condTitle;
        private Label _condCount;
        private ConditionItem[] _condItems;
        private InkBar _condBar;
        private Label _condPct;
        private ContainerControl _rewardSection;
        private Label _rewardTitle;
        private Label _rewardTag;
        private RewardItem[] _rewardItems;
        private ContainerControl _detailFoot;
        private InkButton _claimButton;
        private Label _claimNote;

        public event Action<string> NavigationRequested;
        public InkParticleSystem ParticleSystem { get; set; }

        private CharacterAttributesComponent _boundCharacter;

        public void BindCharacter(CharacterAttributesComponent component)
        {
            _boundCharacter = component;
        }

        public AchievementPage()
        {
            AnchorPreset = AnchorPresets.StretchAll;
            Offsets = Margin.Zero;
            BackgroundColor = InkWashTheme.BaseSecondary;
            ClipChildren = false;
            AutoFocus = false;

            BuildLayout();
        }

        private void BuildLayout()
        {
            BuildHeader();
            BuildTree();
            BuildGridSection();
            BuildDetail();
        }

        private void BuildHeader()
        {
            var header = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(Width > 0f ? Width : 1400f, HeaderHeight),
                BackgroundColor = Color.Transparent,
                ClipChildren = false,
            };

            float headerX = ScreenEdge;
            float headerW = (Width > 0f ? Width : 1400f) - 2f * ScreenEdge;
            float headerY = ScreenEdge;

            _backButton = new InkBackButton
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, (HeaderHeight - 40f) * 0.5f),
            };
            _backButton.Clicked += OnBackClicked;
            header.AddChild(_backButton);

            _titleLabel = new Label
            {
                Text = "江湖百艺录",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 22f),
                TextColor = InkWashTheme.GoldPrimary,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(52f, 6f),
                Size = new Float2(180f, 24f),
                BackgroundColor = Color.Transparent,
            };
            header.AddChild(_titleLabel);

            _subtitleLabel = new Label
            {
                Text = "百艺通鉴 · 武学考工",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextTertiary,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(52f, 32f),
                Size = new Float2(180f, 16f),
                BackgroundColor = Color.Transparent,
            };
            header.AddChild(_subtitleLabel);

            float overallX = 260f;
            _overallLabel = new Label
            {
                Text = "总体完成度",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                TextColor = InkWashTheme.TextTertiary,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(overallX, 8f),
                Size = new Float2(80f, 16f),
                BackgroundColor = Color.Transparent,
            };
            header.AddChild(_overallLabel);

            _overallCount = new Label
            {
                Text = "238",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 18f),
                TextColor = InkWashTheme.TextDefault,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(overallX + 80f, 6f),
                Size = new Float2(100f, 20f),
                BackgroundColor = Color.Transparent,
            };
            header.AddChild(_overallCount);

            _overallPct = new Label
            {
                Text = "45%",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 13f),
                TextColor = InkWashTheme.GoldPrimary,
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(overallX + 170f, 8f),
                Size = new Float2(60f, 16f),
                BackgroundColor = Color.Transparent,
            };
            header.AddChild(_overallPct);

            _overallBar = new InkBar
            {
                FillVariant = InkBarFillVariant.Gold,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(overallX, 30f),
                Size = new Float2(220f, 8f),
                Value = 0.45f,
            };
            header.AddChild(_overallBar);

            float rightX = headerW - 200f;
            var pointsPanel = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(rightX, 10f),
                Size = new Float2(130f, 30f),
                BackgroundColor = InkWashTheme.BaseElevated,
                ClipChildren = false,
            };
            var pointsLabel = new Label
            {
                Text = "成就点",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                TextColor = InkWashTheme.TextTertiary,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(8f, 0f),
                Size = new Float2(50f, 30f),
                BackgroundColor = Color.Transparent,
            };
            pointsPanel.AddChild(pointsLabel);
            _pointsValue = new Label
            {
                Text = "2,380",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 15f),
                TextColor = InkWashTheme.GoldBright,
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(58f, 0f),
                Size = new Float2(64f, 30f),
                BackgroundColor = Color.Transparent,
            };
            pointsPanel.AddChild(_pointsValue);
            header.AddChild(pointsPanel);

            _filterBtn = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Sm,
                Text = "筛选",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(rightX + 138f, 12f),
                Size = new Float2(56f, 28f),
            };
            _filterBtn.ButtonClicked += (b) => EmitGoldAtButton(b);
            header.AddChild(_filterBtn);

            _closeBtn = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Sm,
                Text = "×",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(headerW - 40f, 12f),
                Size = new Float2(32f, 28f),
            };
            _closeBtn.ButtonClicked += (b) => NavigationRequested?.Invoke(InkPageDomIds.CombatHud);
            header.AddChild(_closeBtn);

            AddChild(header);
        }

        private void BuildTree()
        {
            _tree = new Panel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(TreeWidth, 600f),
                BackgroundColor = InkWashTheme.BgMist,
                ClipChildren = false,
            };

            _treeHeadTitle = new Label
            {
                Text = "成就分类",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 14f),
                TextColor = InkWashTheme.TextSecondary,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, 12f),
                Size = new Float2(140f, 20f),
                BackgroundColor = Color.Transparent,
            };
            _tree.AddChild(_treeHeadTitle);

            _treeHeadHint = new Label
            {
                Text = "6 类",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextTertiary,
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(TreeWidth - 64f, 12f),
                Size = new Float2(48f, 20f),
                BackgroundColor = Color.Transparent,
            };
            _tree.AddChild(_treeHeadHint);

            string[] catNames = { "江湖历练", "武学修为", "装备收集", "生活技艺", "门派贡献", "特殊成就" };
            string[] catCounts = { "45/120", "32/85", "28/90", "56/110", "41/75", "36/40" };
            float[] catProgs = { 0.38f, 0.38f, 0.31f, 0.51f, 0.55f, 0.90f };
            _catItems = new CategoryItem[6];
            for (int i = 0; i < 6; i++)
            {
                bool isFirst = i == 0;
                var item = new CategoryItem
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(8f, 40f + i * 60f),
                    Size = new Float2(TreeWidth - 16f, 56f),
                    CatName = catNames[i],
                    CatCount = catCounts[i],
                    CatProgress = catProgs[i],
                    IsExpanded = isFirst,
                };
                if (isFirst)
                {
                    item.AddSubItem("战斗", "12/24");
                    item.AddSubItem("探索", "18/40");
                    item.AddSubItem("社交", "15/56");
                }
                item.Selected += OnCategorySelected;
                _catItems[i] = item;
                _tree.AddChild(item);
            }

            _treeFoot = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(TreeWidth, 44f),
                BackgroundColor = Color.Transparent,
                ClipChildren = false,
            };
            var footLeft = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, 8f),
                Size = new Float2(90f, 28f),
                BackgroundColor = Color.Transparent,
                ClipChildren = false,
            };
            var footLbl1 = new Label
            {
                Text = "本月新增",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f),
                TextColor = InkWashTheme.TextTertiary,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Float2(90f, 14f),
                BackgroundColor = Color.Transparent,
            };
            footLeft.AddChild(footLbl1);
            var footVal1 = new Label
            {
                Text = "+7",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 13f),
                TextColor = InkWashTheme.JadePrimary,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 14f),
                Size = new Float2(90f, 14f),
                BackgroundColor = Color.Transparent,
            };
            footLeft.AddChild(footVal1);
            _treeFoot.AddChild(footLeft);

            var footDiv = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(114f, 10f),
                Size = new Float2(1f, 24f),
                BackgroundColor = InkWashTheme.Divider,
                ClipChildren = false,
            };
            _treeFoot.AddChild(footDiv);

            var footRight = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(124f, 8f),
                Size = new Float2(100f, 28f),
                BackgroundColor = Color.Transparent,
                ClipChildren = false,
            };
            var footLbl2 = new Label
            {
                Text = "最近解锁",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f),
                TextColor = InkWashTheme.TextTertiary,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Float2(100f, 14f),
                BackgroundColor = Color.Transparent,
            };
            footRight.AddChild(footLbl2);
            var footVal2 = new Label
            {
                Text = "嗜血修罗",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 13f),
                TextColor = InkWashTheme.TextSecondary,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 14f),
                Size = new Float2(100f, 14f),
                BackgroundColor = Color.Transparent,
            };
            footRight.AddChild(footVal2);
            _treeFoot.AddChild(footRight);

            AddChild(_tree);
        }

        private void OnCategorySelected(string name)
        {
        }

        private void BuildGridSection()
        {
            _gridSection = new Panel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(600f, 600f),
                BackgroundColor = Color.Transparent,
                ClipChildren = false,
            };

            _gridHead = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(600f, 44f),
                BackgroundColor = Color.Transparent,
                ClipChildren = false,
            };

            _breadcrumb = new Label
            {
                Text = "江湖历练  ▸  战斗  12/24",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                TextColor = InkWashTheme.TextTertiary,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(20f, 0f),
                Size = new Float2(260f, 44f),
                BackgroundColor = Color.Transparent,
            };
            _gridHead.AddChild(_breadcrumb);

            string[] chipLabels = { "全部", "已解锁", "进行中", "未解锁" };
            _filterChips = new InkButton[4];
            float chipX = 280f;
            for (int i = 0; i < 4; i++)
            {
                var chip = new InkButton
                {
                    Variant = InkButtonVariant.Ghost,
                    ButtonSize = InkButtonSize.Sm,
                    Text = chipLabels[i],
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(chipX + i * 72f, 8f),
                    Size = new Float2(64f, 28f),
                };
                chip.ButtonClicked += OnChipClicked;
                _filterChips[i] = chip;
                _gridHead.AddChild(chip);
            }
            _filterChips[0].BackgroundColor = new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.12f);

            _gridSection.AddChild(_gridHead);

            _gridScroll = new Panel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(600f, 556f),
                BackgroundColor = Color.Transparent,
                ClipChildren = false,
            };

            _cards = new AchievementCard[12];
            string[] cardNames = {
                "初出茅庐", "百战之将", "武林盟主", "嗜血修罗",
                "剑气长江", "一击必杀", "不动如山", "连斩之鬼",
                "团战先锋", "龙战于野", "枪出如龙", "战神降临"
            };
            string[] cardDescs = {
                "完成你的第一场战斗", "累计胜利一百场战斗", "击败十大门派掌门，问鼎武林", "单场战斗击杀五十名敌人",
                "??????????", "造成十万点单次伤害", "累计格挡一千次攻击", "??????????",
                "参与五十场帮派战役", "击败五位世界首领", "??????????", "??????????"
            };
            InkWashTheme.InkQuality[] qualities = {
                InkWashTheme.InkQuality.Common, InkWashTheme.InkQuality.Epic, InkWashTheme.InkQuality.Legendary, InkWashTheme.InkQuality.Rare,
                InkWashTheme.InkQuality.Common, InkWashTheme.InkQuality.Rare, InkWashTheme.InkQuality.Uncommon, InkWashTheme.InkQuality.Common,
                InkWashTheme.InkQuality.Uncommon, InkWashTheme.InkQuality.Epic, InkWashTheme.InkQuality.Legendary, InkWashTheme.InkQuality.Legendary,
            };
            int[] states = { 0, 0, 1, 0, 2, 1, 0, 2, 0, 1, 2, 2 };
            float[] progs = { 1f, 1f, 0.70f, 1f, 0f, 0.73f, 1f, 0f, 1f, 0.80f, 0f, 0f };
            string[] progTexts = {
                "2025.03.12", "2025.05.08", "7 / 10", "2025.06.21",
                "剑法等级达到 80 级", "73%", "2025.04.15", "单次连击达到 999",
                "2025.07.02", "4 / 5", "枪法等级达到 90 级", "隐藏成就 · 条件未知"
            };

            for (int i = 0; i < 12; i++)
            {
                int col = i % 4;
                int row = i / 4;
                var card = new AchievementCard
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(20f + col * (CardWidth + CardGap), 16f + row * (CardHeight + CardGap)),
                    Size = new Float2(CardWidth, CardHeight),
                    CardName = cardNames[i],
                    CardDesc = cardDescs[i],
                    CardQuality = qualities[i],
                    CardState = states[i],
                    CardProgress = progs[i],
                    CardProgressText = progTexts[i],
                    IsHidden = (i == 11),
                    IsSelected = (i == 2),
                };
                card.Clicked += OnCardClicked;
                _cards[i] = card;
                _gridScroll.AddChild(card);
            }

            _gridSection.AddChild(_gridScroll);
            AddChild(_gridSection);
        }

        private void OnChipClicked(Button btn)
        {
            for (int i = 0; i < _filterChips.Length; i++)
            {
                bool active = _filterChips[i] == btn;
                _filterChips[i].BackgroundColor = active
                    ? new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.12f)
                    : Color.Transparent;
            }
        }

        private void OnCardClicked(string name)
        {
        }

        private void BuildDetail()
        {
            _detail = new Panel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(DetailWidth, 600f),
                BackgroundColor = Color.Transparent,
                ClipChildren = false,
            };

            _detailScroll = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(DetailWidth, 540f),
                BackgroundColor = Color.Transparent,
                ClipChildren = false,
            };

            _detailHero = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 0f),
                Size = new Float2(DetailWidth, 100f),
                BackgroundColor = Color.Transparent,
                ClipChildren = false,
            };

            _detailHeroIcon = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, 14f),
                Size = new Float2(56f, 56f),
                BackgroundColor = InkWashTheme.Abyss,
                ClipChildren = false,
            };

            var heroMeta = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(84f, 14f),
                Size = new Float2(140f, 56f),
                BackgroundColor = Color.Transparent,
                ClipChildren = false,
            };
            _detailTier = new Label
            {
                Text = "传说成就",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.QualityLegendary,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Float2(140f, 16f),
                BackgroundColor = Color.Transparent,
            };
            heroMeta.AddChild(_detailTier);
            _detailName = new Label
            {
                Text = "武林盟主",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 18f),
                TextColor = InkWashTheme.TextDefault,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 16f),
                Size = new Float2(140f, 22f),
                BackgroundColor = Color.Transparent,
            };
            heroMeta.AddChild(_detailName);
            _detailPoints = new Label
            {
                Text = "成就点 +120",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 11f),
                TextColor = InkWashTheme.GoldPrimary,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 38f),
                Size = new Float2(140f, 16f),
                BackgroundColor = Color.Transparent,
            };
            heroMeta.AddChild(_detailPoints);
            _detailHero.AddChild(heroMeta);

            _detailStatus = new Label
            {
                Text = "进行中",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f),
                TextColor = InkWashTheme.GoldPrimary,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(DetailWidth - 80f, 18f),
                Size = new Float2(64f, 20f),
                BackgroundColor = InkWashTheme.BaseElevated,
            };
            _detailHero.AddChild(_detailStatus);
            _detailScroll.AddChild(_detailHero);

            _detailDesc = new Label
            {
                Text = "击败天下十大门派掌门，证明自身武学造诣已臻化境，可号令群雄，问鼎武林至尊之位。",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                TextColor = InkWashTheme.TextSecondary,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Near,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, 108f),
                Size = new Float2(DetailWidth - 32f, 56f),
                BackgroundColor = Color.Transparent,
            };
            _detailScroll.AddChild(_detailDesc);

            _condSection = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 172f),
                Size = new Float2(DetailWidth, 260f),
                BackgroundColor = Color.Transparent,
                ClipChildren = false,
            };
            _condTitle = new Label
            {
                Text = "完成条件",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 13f),
                TextColor = InkWashTheme.TextSecondary,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, 0f),
                Size = new Float2(140f, 20f),
                BackgroundColor = Color.Transparent,
            };
            _condSection.AddChild(_condTitle);
            _condCount = new Label
            {
                Text = "7 / 10",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 12f),
                TextColor = InkWashTheme.GoldPrimary,
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(DetailWidth - 80f, 0f),
                Size = new Float2(64f, 20f),
                BackgroundColor = Color.Transparent,
            };
            _condSection.AddChild(_condCount);

            string[] condTexts = {
                "击败少林方丈 · 玄慈", "击败武当掌门 · 冲虚", "击败峨眉掌门 · 灭绝",
                "击败丐帮帮主 · 乔峰", "击败华山掌门 · 岳不群", "击败昆仑掌门 · 何太冲",
                "击败点苍掌门 · 柳青", "击败崆峒掌门 · 飞龙子", "击败青城掌门 · 余沧海",
                "击败蜀山掌门 · 剑圣"
            };
            _condItems = new ConditionItem[10];
            for (int i = 0; i < 10; i++)
            {
                bool done = i < 7;
                var ci = new ConditionItem
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(16f, 26f + i * 22f),
                    Size = new Float2(DetailWidth - 32f, 20f),
                    CondText = condTexts[i],
                    IsDone = done,
                };
                _condItems[i] = ci;
                _condSection.AddChild(ci);
            }

            _condBar = new InkBar
            {
                FillVariant = InkBarFillVariant.Gold,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, 252f),
                Size = new Float2(DetailWidth - 64f, 6f),
                Value = 0.70f,
            };
            _condSection.AddChild(_condBar);

            _condPct = new Label
            {
                Text = "70%",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 11f),
                TextColor = InkWashTheme.GoldPrimary,
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(DetailWidth - 48f, 248f),
                Size = new Float2(32f, 16f),
                BackgroundColor = Color.Transparent,
            };
            _condSection.AddChild(_condPct);
            _detailScroll.AddChild(_condSection);

            _rewardSection = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 440f),
                Size = new Float2(DetailWidth, 160f),
                BackgroundColor = Color.Transparent,
                ClipChildren = false,
            };
            _rewardTitle = new Label
            {
                Text = "奖励预览",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 13f),
                TextColor = InkWashTheme.TextSecondary,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, 0f),
                Size = new Float2(100f, 20f),
                BackgroundColor = Color.Transparent,
            };
            _rewardSection.AddChild(_rewardTitle);
            _rewardTag = new Label
            {
                Text = "未领取",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f),
                TextColor = InkWashTheme.TextTertiary,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(DetailWidth - 80f, 2f),
                Size = new Float2(64f, 18f),
                BackgroundColor = InkWashTheme.BaseElevated,
            };
            _rewardSection.AddChild(_rewardTag);

            string[] rewardLabels = { "经验", "银两", "称号", "道具" };
            string[] rewardValues = { "+50,000", "+10,000", "武林至尊", "盟主令 ×1" };
            _rewardItems = new RewardItem[4];
            for (int i = 0; i < 4; i++)
            {
                bool isTitle = i == 2;
                var ri = new RewardItem
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(16f, 26f + i * 34f),
                    Size = new Float2(DetailWidth - 32f, 30f),
                    RewardLabel = rewardLabels[i],
                    RewardValue = rewardValues[i],
                    IsTitle = isTitle,
                };
                _rewardItems[i] = ri;
                _rewardSection.AddChild(ri);
            }
            _detailScroll.AddChild(_rewardSection);

            _detail.AddChild(_detailScroll);

            _detailFoot = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(DetailWidth, 60f),
                BackgroundColor = Color.Transparent,
                ClipChildren = false,
            };
            _claimButton = new InkButton
            {
                Variant = InkButtonVariant.Primary,
                ButtonSize = InkButtonSize.Md,
                Text = "领取奖励",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, 12f),
                Size = new Float2(DetailWidth - 32f, 36f),
                BackgroundColor = new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.28f),
            };
            _claimButton.ButtonClicked += (b) => OnClaimClicked(b);
            _detailFoot.AddChild(_claimButton);

            _claimNote = new Label
            {
                Text = "完成全部条件后可领取",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextTertiary,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, 48f),
                Size = new Float2(DetailWidth - 32f, 14f),
                BackgroundColor = Color.Transparent,
            };
            _detailFoot.AddChild(_claimNote);
            _detail.AddChild(_detailFoot);

            AddChild(_detail);
        }

        private void OnBackClicked()
        {
            EmitGoldAtButton(_backButton);
            NavigationRequested?.Invoke(InkPageDomIds.CombatHud);
        }

        private void OnClaimClicked(Button source)
        {
            EmitGoldAtButton(source);
            NavigationRequested?.Invoke(InkPageDomIds.NavAchievement);
        }

        private void EmitGoldAtButton(Control button)
        {
            if (ParticleSystem == null || button == null) return;
            var center = new Float2(button.Width * 0.5f, button.Height * 0.5f);
            var screenPos = button.PointToScreen(center);
            var localPos = ParticleSystem.PointFromScreen(screenPos);
            ParticleSystem.EmitGoldBurst(localPos, count: 14, isLarge: false);
        }

        public void RefreshLayout()
        {
            float sw = Width > 0f ? Width : 1400f;
            float sh = Height > 0f ? Height : 900f;

            float headerY = ScreenEdge;
            float contentTop = headerY + HeaderHeight;
            float contentH = sh - contentTop - ScreenEdge;

            float treeH = contentH;
            float gridH = contentH;
            float detailH = contentH;

            var header = Children[0] as ContainerControl;
            if (header != null)
            {
                header.Location = new Float2(ScreenEdge, headerY);
                header.Size = new Float2(sw - 2f * ScreenEdge, HeaderHeight);
            }

            if (_tree != null)
            {
                _tree.Location = new Float2(ScreenEdge, contentTop);
                _tree.Size = new Float2(TreeWidth, treeH);
                _treeFoot.Location = new Float2(0f, treeH - 44f);
            }

            if (_detail != null)
            {
                _detail.Location = new Float2(sw - ScreenEdge - DetailWidth, contentTop);
                _detail.Size = new Float2(DetailWidth, detailH);
                _detailScroll.Size = new Float2(DetailWidth, detailH - 60f);
                _detailFoot.Location = new Float2(0f, detailH - 60f);
            }

            if (_gridSection != null)
            {
                float listX = ScreenEdge + TreeWidth;
                float listW = sw - 2f * ScreenEdge - TreeWidth - DetailWidth;
                _gridSection.Location = new Float2(listX, contentTop);
                _gridSection.Size = new Float2(listW, gridH);
                _gridHead.Size = new Float2(listW, 44f);
                _gridScroll.Size = new Float2(listW, gridH - 44f);

                _breadcrumb.Size = new Float2(listW * 0.5f, 44f);
                float chipStart = listW - 4f * 72f - 20f;
                if (chipStart < listW * 0.5f) chipStart = listW * 0.5f;
                for (int i = 0; i < _filterChips.Length; i++)
                {
                    _filterChips[i].Location = new Float2(chipStart + i * 72f, 8f);
                }
            }
        }

        public override void OnParentResized()
        {
            base.OnParentResized();
            RefreshLayout();
        }

        // ===================================================================
        // Inner classes
        // ===================================================================

        private class ClickableContainer : ContainerControl
        {
            public event Action<string> Clicked;
            public string DataTag { get; set; }

            public override bool OnMouseUp(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left)
                {
                    Clicked?.Invoke(DataTag);
                }
                return base.OnMouseUp(location, button);
            }
        }

        private class CategoryItem : ContainerControl
        {
            private Label _nameLabel;
            private Label _countLabel;
            private InkBar _progBar;
            private ContainerControl _subContainer;
            private bool _expanded;

            public string CatName
            {
                get => _nameLabel?.Text ?? "";
                set { if (_nameLabel != null) _nameLabel.Text = value; }
            }

            public string CatCount
            {
                get => _countLabel?.Text ?? "";
                set { if (_countLabel != null) _countLabel.Text = value; }
            }

            public float CatProgress
            {
                set { if (_progBar != null) _progBar.Value = value; }
            }

            public bool IsExpanded
            {
                get => _expanded;
                set
                {
                    _expanded = value;
                    if (_subContainer != null)
                        _subContainer.Visible = value;
                }
            }

            public event Action<string> Selected;

            public CategoryItem()
            {
                BackgroundColor = Color.Transparent;
                ClipChildren = false;

                _nameLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(8f, 4f),
                    Size = new Float2(140f, 18f),
                    TextColor = InkWashTheme.TextDefault,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 14f),
                    HorizontalAlignment = TextAlignment.Near,
                    BackgroundColor = Color.Transparent,
                };
                AddChild(_nameLabel);

                _countLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(152f, 4f),
                    Size = new Float2(56f, 18f),
                    TextColor = InkWashTheme.TextTertiary,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 11f),
                    HorizontalAlignment = TextAlignment.Far,
                    BackgroundColor = Color.Transparent,
                };
                AddChild(_countLabel);

                _progBar = new InkBar
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(8f, 26f),
                    Size = new Float2(200f, 2f),
                    Value = 0f,
                    FillVariant = InkBarFillVariant.Gold,
                };
                AddChild(_progBar);

                _subContainer = new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(8f, 32f),
                    Size = new Float2(200f, 60f),
                    BackgroundColor = Color.Transparent,
                    ClipChildren = false,
                    Visible = false,
                };
                AddChild(_subContainer);
            }

            public void AddSubItem(string name, string count)
            {
                var sub = new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(0f, _subContainer.ChildrenCount * 22f),
                    Size = new Float2(200f, 20f),
                    BackgroundColor = Color.Transparent,
                    ClipChildren = false,
                };
                var dot = new Label
                {
                    Text = "●",
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 5f),
                    TextColor = InkWashTheme.GoldPrimary,
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(4f, 4f),
                    Size = new Float2(8f, 12f),
                    BackgroundColor = Color.Transparent,
                };
                sub.AddChild(dot);
                var nameLbl = new Label
                {
                    Text = name,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                    TextColor = InkWashTheme.GoldPrimary,
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(18f, 0f),
                    Size = new Float2(120f, 20f),
                    BackgroundColor = Color.Transparent,
                };
                sub.AddChild(nameLbl);
                var countLbl = new Label
                {
                    Text = count,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 11f),
                    TextColor = InkWashTheme.GoldPrimary,
                    HorizontalAlignment = TextAlignment.Far,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(140f, 0f),
                    Size = new Float2(56f, 20f),
                    BackgroundColor = Color.Transparent,
                };
                sub.AddChild(countLbl);
                _subContainer.AddChild(sub);
                _subContainer.Visible = _expanded;
            }

            public override bool OnMouseUp(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left)
                    Selected?.Invoke(_nameLabel?.Text ?? "");
                return base.OnMouseUp(location, button);
            }
        }

        private class AchievementCard : ContainerControl
        {
            private Label _tierLabel;
            private Label _nameLabel;
            private Label _descLabel;
            private ContainerControl _iconBox;
            private InkBar _progBar;
            private Label _footLabel;
            private Label _statusIcon;
            private int _state;
            private InkWashTheme.InkQuality _quality;
            private bool _selected;
            private bool _hidden;

            public string CardName
            {
                set { if (_nameLabel != null) _nameLabel.Text = value; }
            }

            public string CardDesc
            {
                set { if (_descLabel != null) _descLabel.Text = value; }
            }

            public InkWashTheme.InkQuality CardQuality
            {
                get => _quality;
                set
                {
                    _quality = value;
                    UpdateQualityStyle();
                }
            }

            public int CardState
            {
                get => _state;
                set
                {
                    _state = value;
                    ApplyState();
                }
            }

            public float CardProgress
            {
                set { if (_progBar != null) _progBar.Value = value; }
            }

            public string CardProgressText
            {
                set { if (_footLabel != null) _footLabel.Text = value; }
            }

            public bool IsSelected
            {
                get => _selected;
                set { _selected = value; }
            }

            public bool IsHidden
            {
                get => _hidden;
                set { _hidden = value; }
            }

            public event Action<string> Clicked;
            public string DataTag => _nameLabel?.Text ?? "";

            public AchievementCard()
            {
                BackgroundColor = InkWashTheme.BaseElevated;
                ClipChildren = false;

                _iconBox = new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(8f, 8f),
                    Size = new Float2(38f, 38f),
                    BackgroundColor = InkWashTheme.Abyss,
                    ClipChildren = false,
                };
                AddChild(_iconBox);

                _tierLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(54f, 10f),
                    Size = new Float2(118f, 16f),
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                    HorizontalAlignment = TextAlignment.Far,
                    BackgroundColor = Color.Transparent,
                };
                AddChild(_tierLabel);

                _nameLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(8f, 52f),
                    Size = new Float2(164f, 20f),
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 15f),
                    TextColor = InkWashTheme.TextDefault,
                    HorizontalAlignment = TextAlignment.Near,
                    BackgroundColor = Color.Transparent,
                };
                AddChild(_nameLabel);

                _descLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(8f, 74f),
                    Size = new Float2(164f, 34f),
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                    TextColor = InkWashTheme.TextTertiary,
                    HorizontalAlignment = TextAlignment.Near,
                    BackgroundColor = Color.Transparent,
                };
                AddChild(_descLabel);

                _progBar = new InkBar
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(8f, 114f),
                    Size = new Float2(164f, 4f),
                    Value = 0f,
                    FillVariant = InkBarFillVariant.Gold,
                };
                AddChild(_progBar);

                _footLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(8f, 124f),
                    Size = new Float2(140f, 16f),
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 11f),
                    TextColor = InkWashTheme.TextTertiary,
                    HorizontalAlignment = TextAlignment.Near,
                    BackgroundColor = Color.Transparent,
                };
                AddChild(_footLabel);

                _statusIcon = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(152f, 124f),
                    Size = new Float2(20f, 16f),
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                    TextColor = InkWashTheme.JadePrimary,
                    HorizontalAlignment = TextAlignment.Center,
                    BackgroundColor = Color.Transparent,
                };
                AddChild(_statusIcon);

                ApplyState();
            }

            private void UpdateQualityStyle()
            {
                string[] tierNames = { "普通", "优秀", "精良", "史诗", "传说" };
                if (_tierLabel != null)
                {
                    _tierLabel.Text = tierNames[(int)_quality];
                    _tierLabel.TextColor = InkWashTheme.QualityTextColor(_quality);
                }
            }

            private void ApplyState()
            {
                UpdateQualityStyle();
                if (_progBar == null || _footLabel == null || _nameLabel == null || _descLabel == null)
                    return;

                switch (_state)
                {
                    case 0:
                        _progBar.Visible = false;
                        _footLabel.TextColor = InkWashTheme.TextTertiary;
                        _nameLabel.TextColor = InkWashTheme.TextDefault;
                        _descLabel.TextColor = InkWashTheme.TextTertiary;
                        _statusIcon.Text = "✔";
                        _statusIcon.TextColor = InkWashTheme.JadePrimary;
                        _statusIcon.Visible = true;
                        break;
                    case 1:
                        _progBar.Visible = true;
                        _footLabel.TextColor = InkWashTheme.GoldPrimary;
                        _nameLabel.TextColor = InkWashTheme.TextDefault;
                        _descLabel.TextColor = InkWashTheme.TextTertiary;
                        _statusIcon.Visible = false;
                        break;
                    default:
                        _progBar.Visible = false;
                        _footLabel.TextColor = InkWashTheme.TextTertiary;
                        _nameLabel.TextColor = InkWashTheme.TextTertiary;
                        _descLabel.TextColor = InkWashTheme.TextDisabled;
                        _statusIcon.Text = "🔒";
                        _statusIcon.TextColor = InkWashTheme.TextTertiary;
                        _statusIcon.Visible = true;
                        break;
                }
            }

            public override void Draw()
            {
                if (!Visible || Width <= 0f || Height <= 0f)
                    return;

                var bounds = new Rectangle(0, 0, Width, Height);

                if (_hidden)
                {
                    Render2D.FillRectangle(bounds, new Color(InkWashTheme.BaseDefault.R, InkWashTheme.BaseDefault.G, InkWashTheme.BaseDefault.B, 0.6f));
                    Render2D.FillRectangle(bounds, InkWashTheme.BgMist);
                }
                else
                {
                    Render2D.FillRectangle(bounds, BackgroundColor);
                }

                Color borderColor = InkWashTheme.BorderFaint;
                if (_selected)
                {
                    borderColor = InkWashTheme.GoldPrimary;
                    Render2D.DrawRectangle(bounds, borderColor, 1f);
                    Render2D.FillRectangle(new Rectangle(Width - 10f, 6f, 6f, 6f), InkWashTheme.GoldBright);
                }
                else if (_state == 0 || _state == 1)
                {
                    borderColor = InkWashTheme.QualityColor(_quality);
                    Render2D.DrawRectangle(bounds, borderColor, 1f);
                }
                else
                {
                    Render2D.DrawRectangle(bounds, InkWashTheme.BorderFaint, 1f);
                }

                if (_state == 0)
                {
                    Render2D.DrawRectangle(new Rectangle(0, 0, Width, Height), InkWashTheme.BorderGoldSubtle, 1f);
                }

                base.Draw();
            }

            public override bool OnMouseUp(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left)
                    Clicked?.Invoke(DataTag);
                return base.OnMouseUp(location, button);
            }
        }

        private class ConditionItem : ContainerControl
        {
            private Label _iconLabel;
            private Label _textLabel;
            private bool _done;

            public string CondText
            {
                set { if (_textLabel != null) _textLabel.Text = value; }
            }

            public bool IsDone
            {
                get => _done;
                set
                {
                    _done = value;
                    if (_iconLabel != null)
                    {
                        _iconLabel.Text = value ? "✔" : "○";
                        _iconLabel.TextColor = value ? InkWashTheme.JadePrimary : InkWashTheme.TextTertiary;
                    }
                    if (_textLabel != null)
                        _textLabel.TextColor = value ? InkWashTheme.TextSecondary : InkWashTheme.TextTertiary;
                }
            }

            public ConditionItem()
            {
                BackgroundColor = Color.Transparent;
                ClipChildren = false;

                _iconLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(0f, 2f),
                    Size = new Float2(18f, 16f),
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                    TextColor = InkWashTheme.JadePrimary,
                    HorizontalAlignment = TextAlignment.Center,
                    BackgroundColor = Color.Transparent,
                };
                AddChild(_iconLabel);

                _textLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(20f, 0f),
                    Size = new Float2(240f, 20f),
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                    TextColor = InkWashTheme.TextTertiary,
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                    BackgroundColor = Color.Transparent,
                };
                AddChild(_textLabel);
            }
        }

        private class RewardItem : ContainerControl
        {
            private Label _labelLbl;
            private Label _valueLbl;
            private bool _isTitle;

            public string RewardLabel
            {
                set { if (_labelLbl != null) _labelLbl.Text = value; }
            }

            public string RewardValue
            {
                set { if (_valueLbl != null) _valueLbl.Text = value; }
            }

            public bool IsTitle
            {
                get => _isTitle;
                set
                {
                    _isTitle = value;
                    if (value)
                    {
                        BackgroundColor = new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.08f);
                    }
                    else
                    {
                        BackgroundColor = Color.Transparent;
                    }
                }
            }

            public RewardItem()
            {
                BackgroundColor = Color.Transparent;
                ClipChildren = false;

                _labelLbl = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(8f, 0f),
                    Size = new Float2(80f, 30f),
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                    TextColor = InkWashTheme.TextTertiary,
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                    BackgroundColor = Color.Transparent,
                };
                AddChild(_labelLbl);

                _valueLbl = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(88f, 0f),
                    Size = new Float2(180f, 30f),
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 13f),
                    TextColor = InkWashTheme.TextDefault,
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                    BackgroundColor = Color.Transparent,
                };
                AddChild(_valueLbl);
            }

            public override void Draw()
            {
                if (!Visible || Width <= 0f || Height <= 0f)
                    return;

                var bounds = new Rectangle(0, 0, Width, Height);
                Render2D.FillRectangle(bounds, BackgroundColor);
                Render2D.DrawRectangle(bounds, InkWashTheme.BorderFaint, 1f);
                base.Draw();
            }
        }
    }
}

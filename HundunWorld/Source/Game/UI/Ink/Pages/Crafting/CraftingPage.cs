using FlaxEngine;
using FlaxEngine.GUI;
using Game.Character.Attributes;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.StyleSystem;
using System;

namespace HundunWorld.Game.UI.Ink.Pages.Crafting
{
    public class CraftingPage : ContainerControl, IInkPage
    {
        private static readonly Float2 MainPanelSize = new Float2(1400f, 900f);
        private const float TopBarHeight = 52f;
        private const float Padding = 12f;
        private const float PanelGap = 10f;
        private const float LeftSidebarWidth = 280f;
        private const float RightBenchWidth = 320f;
        private const float TabBarHeight = 40f;
        private const float SkillItemHeight = 60f;
        private const float SkillItemGap = 8f;
        private const float RecipeCardHeight = 86f;
        private const float RecipeCardGap = 10f;
        private const float MaterialRowHeight = 36f;
        private const float MaterialRowGap = 6f;
        private const float InfoGridGap = 8f;
        private const float LogItemHeight = 24f;
        private const float LogItemGap = 4f;
        private const float BatchBtnWidth = 56f;
        private const float BatchBtnHeight = 30f;

        private InkPaperPanel _mainPanel;

        private Panel _topBar;
        private Label _titleLabel;
        private Label _subtitleLabel;
        private InkButton _searchButton;
        private InkButton _backButton;

        private Panel _leftSidebar;
        private InkButton _tabGather;
        private InkButton _tabCraft;
        private ContainerControl[] _skillItems;

        private Panel _middleContent;
        private ContainerControl[] _recipeCards;
        private Panel _detailPanel;
        private ContainerControl _detailIconCell;
        private Label _detailNameLabel;
        private ContainerControl[] _materialRows;
        private ContainerControl[] _infoItems;

        private Panel _rightBench;
        private ContainerControl _previewCell;
        private Label _previewNameLabel;
        private ContainerControl _craftProgressBar;
        private InkButton[] _batchButtons;
        private InkButton _craftButton;

        public event Action<string> NavigationRequested;

        public InkParticleSystem ParticleSystem { get; set; }

        private CharacterAttributesComponent _boundCharacter;

        public void BindCharacter(CharacterAttributesComponent component)
        {
            _boundCharacter = component;
        }

        public CraftingPage()
        {
            try
            {
                AnchorPreset = AnchorPresets.StretchAll;
                Offsets = Margin.Zero;
                BackgroundColor = InkWashTheme.Scrim;
                ClipChildren = false;
                AutoFocus = false;

                BuildMainPanel();
                BuildTopBar();
                BuildLeftSidebar();
                BuildMiddleContent();
                BuildRightBench();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[CraftingPage] 初始化失败: {ex.Message}");
            }
        }

        private void BuildMainPanel()
        {
            _mainPanel = new InkPaperPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = MainPanelSize,
            };
            AddChild(_mainPanel);
        }

        private void BuildTopBar()
        {
            _topBar = new Panel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Float2(MainPanelSize.X, TopBarHeight),
                BackgroundColor = Color.Transparent,
            };
            _mainPanel.AddChild(_topBar);

            _titleLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(Padding, 0f),
                Size = new Float2(180f, TopBarHeight),
                Text = "制造技艺",
                TextColor = InkWashTheme.TextGold,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 22f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _topBar.AddChild(_titleLabel);

            _subtitleLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(Padding + 180f + 16f, 0f),
                Size = new Float2(180f, TopBarHeight),
                Text = "江湖百业工坊",
                TextColor = InkWashTheme.TextTertiary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _topBar.AddChild(_subtitleLabel);

            _backButton = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Sm,
                Text = "←",
                AnchorPreset = AnchorPresets.TopRight,
                Location = new Float2(MainPanelSize.X - Padding - 36f, (TopBarHeight - 28f) * 0.5f),
                Size = new Float2(36f, 28f),
            };
            _backButton.Clicked += () =>
            {
                EmitGoldAtButton(_backButton);
                NavigationRequested?.Invoke(InkPageDomIds.CombatHud);
            };
            _topBar.AddChild(_backButton);

            _searchButton = new InkButton
            {
                Variant = InkButtonVariant.Default,
                ButtonSize = InkButtonSize.Sm,
                Text = "搜索配方",
                AnchorPreset = AnchorPresets.TopRight,
                Location = new Float2(MainPanelSize.X - Padding - 36f - 8f - 100f, (TopBarHeight - 28f) * 0.5f),
                Size = new Float2(100f, 28f),
            };
            _topBar.AddChild(_searchButton);
        }

        private void BuildLeftSidebar()
        {
            float contentTop = TopBarHeight + PanelGap;
            float contentBottom = MainPanelSize.Y - PanelGap;
            float contentH = contentBottom - contentTop;

            _leftSidebar = new Panel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(Padding, contentTop),
                Size = new Float2(LeftSidebarWidth, contentH),
                BackgroundColor = Color.Transparent,
            };
            _mainPanel.AddChild(_leftSidebar);

            _tabGather = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Sm,
                Text = "采集",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 0f),
                Size = new Float2(LeftSidebarWidth * 0.5f, TabBarHeight),
                TextColor = InkWashTheme.TextTertiary,
            };
            _leftSidebar.AddChild(_tabGather);

            _tabCraft = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Sm,
                Text = "制造",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(LeftSidebarWidth * 0.5f, 0f),
                Size = new Float2(LeftSidebarWidth * 0.5f, TabBarHeight),
                TextColor = InkWashTheme.TextGold,
                BackgroundColor = new Color(
                    InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G,
                    InkWashTheme.GoldPrimary.B, 0.1f),
            };
            _leftSidebar.AddChild(_tabCraft);

            string[] skillNames = { "锻造", "制药", "织造", "烹饪", "机关" };
            string[] skillSubs = { "武器·防具", "丹药", "衣袍", "食物", "暗器·道具" };
            int[] skillLevels = { 40, 35, 28, 42, 20 };
            float[] skillProgress = { 0.40f, 0.35f, 0.28f, 0.42f, 0.20f };
            Color[] skillElemColors =
            {
                InkWashTheme.ElementMetal,
                InkWashTheme.ElementWood,
                InkWashTheme.ElementWater,
                InkWashTheme.ElementFire,
                InkWashTheme.ElementMetal,
            };
            string[] skillElemChars = { "金", "木", "水", "火", "金" };

            _skillItems = new ContainerControl[5];

            float listTop = TabBarHeight + Padding;
            float itemW = LeftSidebarWidth - Padding * 2;
            for (int i = 0; i < 5; i++)
            {
                float itemY = listTop + i * (SkillItemHeight + SkillItemGap);
                bool isActive = (i == 0);

                var item = new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(Padding, itemY),
                    Size = new Float2(itemW, SkillItemHeight),
                    BackgroundColor = isActive
                        ? new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G,
                                    InkWashTheme.GoldPrimary.B, 0.08f)
                        : Color.Transparent,
                };
                _skillItems[i] = item;
                _leftSidebar.AddChild(item);

                var elemBox = new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(10f, 10f),
                    Size = new Float2(20f, 20f),
                    BackgroundColor = skillElemColors[i],
                };
                item.AddChild(elemBox);

                var elemLabel = new Label
                {
                    AnchorPreset = AnchorPresets.StretchAll,
                    Text = skillElemChars[i],
                    TextColor = InkWashTheme.Abyss,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 11f),
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                };
                elemBox.AddChild(elemLabel);

                var nameLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(38f, 8f),
                    Size = new Float2(120f, 18f),
                    Text = skillNames[i],
                    TextColor = InkWashTheme.TextDefault,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 14f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                item.AddChild(nameLabel);

                var lvlLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopRight,
                    Location = new Float2(itemW - 70f, 8f),
                    Size = new Float2(60f, 18f),
                    Text = "Lv." + skillLevels[i],
                    TextColor = InkWashTheme.TextTertiary,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 12f),
                    HorizontalAlignment = TextAlignment.Far,
                    VerticalAlignment = TextAlignment.Center,
                };
                item.AddChild(lvlLabel);

                var subLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(38f, 28f),
                    Size = new Float2(itemW - 48f, 14f),
                    Text = skillSubs[i],
                    TextColor = InkWashTheme.TextTertiary,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                item.AddChild(subLabel);

                var barBg = new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(10f, SkillItemHeight - 14f),
                    Size = new Float2(itemW - 20f, 4f),
                    BackgroundColor = new Color(
                        InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G,
                        InkWashTheme.GoldPrimary.B, 0.08f),
                };
                item.AddChild(barBg);

                var barFill = new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = Float2.Zero,
                    Size = new Float2((itemW - 20f) * skillProgress[i], 4f),
                    BackgroundColor = skillElemColors[i],
                };
                barBg.AddChild(barFill);
            }
        }

        private void BuildMiddleContent()
        {
            float contentTop = TopBarHeight + PanelGap;
            float contentBottom = MainPanelSize.Y - PanelGap;
            float contentH = contentBottom - contentTop;
            float middleX = Padding + LeftSidebarWidth + PanelGap;
            float middleW = MainPanelSize.X - Padding * 2 - LeftSidebarWidth - RightBenchWidth - PanelGap * 2;

            _middleContent = new Panel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(middleX, contentTop),
                Size = new Float2(middleW, contentH),
                BackgroundColor = Color.Transparent,
            };
            _mainPanel.AddChild(_middleContent);

            var recipeHeader = new Panel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 0f),
                Size = new Float2(middleW, 40f),
                BackgroundColor = Color.Transparent,
            };
            _middleContent.AddChild(recipeHeader);

            var recipeSectionTitle = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(8f, 0f),
                Size = new Float2(160f, 40f),
                Text = "锻造配方",
                TextColor = InkWashTheme.TextGold,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 16f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            recipeHeader.AddChild(recipeSectionTitle);

            var recipeCountLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopRight,
                Location = new Float2(middleW - 110f, 0f),
                Size = new Float2(100f, 40f),
                Text = "8 个配方",
                TextColor = InkWashTheme.TextTertiary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
            };
            recipeHeader.AddChild(recipeCountLabel);

            _recipeCards = new ContainerControl[8];
            string[] recipeNames = { "玄铁剑", "寒铁刀", "玄铁盾", "精铁护腕", "精钢头盔", "铁质长枪", "青铜匕首", "寒铁战甲" };
            string[] recipeLevels = { "40", "35", "30", "25", "20", "10", "5", "45" };
            InkWashTheme.InkQuality[] recipeQualities =
            {
                InkWashTheme.InkQuality.Legendary,
                InkWashTheme.InkQuality.Epic,
                InkWashTheme.InkQuality.Rare,
                InkWashTheme.InkQuality.Rare,
                InkWashTheme.InkQuality.Uncommon,
                InkWashTheme.InkQuality.Common,
                InkWashTheme.InkQuality.Common,
                InkWashTheme.InkQuality.Common,
            };
            bool[] recipeLocked = { false, false, false, false, false, false, false, true };

            int cols = 4;
            float colGap = RecipeCardGap;
            float cardW = (middleW - Padding * 2 - colGap * (cols - 1)) / cols;
            float gridTop = 40f + Padding * 0.5f;

            for (int i = 0; i < 8; i++)
            {
                int col = i % cols;
                int row = i / cols;
                float cardX = Padding + col * (cardW + colGap);
                float cardY = gridTop + row * (RecipeCardHeight + RecipeCardGap);
                bool isSelected = (i == 0);
                bool isLocked = recipeLocked[i];
                Color qualityColor = InkWashTheme.QualityColor(recipeQualities[i]);

                var card = new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(cardX, cardY),
                    Size = new Float2(cardW, RecipeCardHeight),
                    BackgroundColor = isLocked
                        ? new Color(InkWashTheme.BaseElevated.R, InkWashTheme.BaseElevated.G,
                                    InkWashTheme.BaseElevated.B, 0.5f)
                        : (isSelected
                            ? new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G,
                                        InkWashTheme.GoldPrimary.B, 0.1f)
                            : InkWashTheme.BaseElevated),
                };
                _recipeCards[i] = card;
                _middleContent.AddChild(card);

                var iconCell = new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopCenter,
                    Location = new Float2((cardW - 44f) * 0.5f, 6f),
                    Size = new Float2(44f, 44f),
                    BackgroundColor = new Color(
                        qualityColor.R * 0.15f, qualityColor.G * 0.15f,
                        qualityColor.B * 0.15f, 0.3f),
                };
                card.AddChild(iconCell);

                var nameLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(4f, 52f),
                    Size = new Float2(cardW - 8f, 16f),
                    Text = recipeNames[i],
                    TextColor = isLocked ? InkWashTheme.TextTertiary : InkWashTheme.TextDefault,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                };
                card.AddChild(nameLabel);

                var reqLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(4f, 68f),
                    Size = new Float2(cardW - 8f, 14f),
                    Text = "Lv." + recipeLevels[i],
                    TextColor = isLocked ? InkWashTheme.TextBlood : InkWashTheme.TextTertiary,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 11f),
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                };
                card.AddChild(reqLabel);
            }

            float detailTop = gridTop + 2 * (RecipeCardHeight + RecipeCardGap) + Padding;
            float detailH = contentH - detailTop;

            _detailPanel = new Panel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, detailTop),
                Size = new Float2(middleW, detailH),
                BackgroundColor = Color.Transparent,
            };
            _middleContent.AddChild(_detailPanel);

            var detailHeader = new Panel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 0f),
                Size = new Float2(middleW, 40f),
                BackgroundColor = Color.Transparent,
            };
            _detailPanel.AddChild(detailHeader);

            var detailTitle = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(8f, 0f),
                Size = new Float2(160f, 40f),
                Text = "配方详情",
                TextColor = InkWashTheme.TextGold,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 16f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            detailHeader.AddChild(detailTitle);

            var craftableLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopRight,
                Location = new Float2(middleW - 130f, 0f),
                Size = new Float2(120f, 40f),
                Text = "可制 0 个",
                TextColor = InkWashTheme.TextSecondary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
            };
            detailHeader.AddChild(craftableLabel);

            float headTop = 44f;
            _detailIconCell = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(Padding, headTop),
                Size = new Float2(56f, 56f),
                BackgroundColor = new Color(
                    InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G,
                    InkWashTheme.GoldPrimary.B, 0.12f),
            };
            _detailPanel.AddChild(_detailIconCell);

            _detailNameLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(Padding + 56f + 16f, headTop),
                Size = new Float2(180f, 24f),
                Text = "玄铁剑",
                TextColor = InkWashTheme.TextDefault,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 18f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _detailPanel.AddChild(_detailNameLabel);

            string[] tagTexts = { "传说", "武器·剑", "五行·金" };
            Color[] tagColors =
            {
                InkWashTheme.TextGold,
                InkWashTheme.TextSecondary,
                InkWashTheme.ElementMetal,
            };
            float tagX = Padding + 56f + 16f;
            for (int i = 0; i < 3; i++)
            {
                float tagW = 64f;
                var tagBg = new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(tagX, headTop + 28f),
                    Size = new Float2(tagW, 20f),
                    BackgroundColor = new Color(
                        InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G,
                        InkWashTheme.GoldPrimary.B, 0.08f),
                };
                _detailPanel.AddChild(tagBg);

                var tagText = new Label
                {
                    AnchorPreset = AnchorPresets.StretchAll,
                    Text = tagTexts[i],
                    TextColor = tagColors[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                };
                tagBg.AddChild(tagText);

                tagX += tagW + 6f;
            }

            float matTitleY = headTop + 56f + 16f;
            var matTitle = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(Padding, matTitleY),
                Size = new Float2(160f, 20f),
                Text = "所需材料",
                TextColor = InkWashTheme.TextSecondary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _detailPanel.AddChild(matTitle);

            string[] matNames = { "玄铁矿", "寒铁锭", "火灵石", "千年木炭" };
            string[] matQuantities = { "5/3", "3/5", "1/2", "2/4" };
            bool[] matSufficient = { false, true, true, true };
            InkWashTheme.InkQuality[] matQualities =
            {
                InkWashTheme.InkQuality.Rare,
                InkWashTheme.InkQuality.Epic,
                InkWashTheme.InkQuality.Rare,
                InkWashTheme.InkQuality.Common,
            };
            _materialRows = new ContainerControl[4];
            float matRowY = matTitleY + 24f;
            float matRowW = middleW - Padding * 2;
            for (int i = 0; i < 4; i++)
            {
                var row = new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(Padding, matRowY + i * (MaterialRowHeight + MaterialRowGap)),
                    Size = new Float2(matRowW, MaterialRowHeight),
                    BackgroundColor = InkWashTheme.BaseElevated,
                };
                _materialRows[i] = row;
                _detailPanel.AddChild(row);

                var matCell = new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(8f, 4f),
                    Size = new Float2(28f, 28f),
                    BackgroundColor = InkWashTheme.QualityColor(matQualities[i]),
                };
                row.AddChild(matCell);

                var nameL = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(44f, 0f),
                    Size = new Float2(matRowW - 44f - 120f - 60f, MaterialRowHeight),
                    Text = matNames[i],
                    TextColor = InkWashTheme.TextDefault,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                row.AddChild(nameL);

                var qtyL = new Label
                {
                    AnchorPreset = AnchorPresets.TopRight,
                    Location = new Float2(matRowW - 120f, 0f),
                    Size = new Float2(60f, MaterialRowHeight),
                    Text = matQuantities[i],
                    TextColor = matSufficient[i] ? InkWashTheme.TextJade : InkWashTheme.TextBlood,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 13f),
                    HorizontalAlignment = TextAlignment.Far,
                    VerticalAlignment = TextAlignment.Center,
                };
                row.AddChild(qtyL);

                var statusBg = new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopRight,
                    Location = new Float2(matRowW - 56f, (MaterialRowHeight - 20f) * 0.5f),
                    Size = new Float2(48f, 20f),
                    BackgroundColor = matSufficient[i]
                        ? new Color(InkWashTheme.JadeBright.R, InkWashTheme.JadeBright.G,
                                    InkWashTheme.JadeBright.B, 0.12f)
                        : new Color(InkWashTheme.BloodBright.R, InkWashTheme.BloodBright.G,
                                    InkWashTheme.BloodBright.B, 0.12f),
                };
                row.AddChild(statusBg);

                var statusText = new Label
                {
                    AnchorPreset = AnchorPresets.StretchAll,
                    Text = matSufficient[i] ? "充足" : "不足",
                    TextColor = matSufficient[i] ? InkWashTheme.TextJade : InkWashTheme.TextBlood,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                };
                statusBg.AddChild(statusText);
            }

            float infoTop = matTitleY + 24f + 4 * (MaterialRowHeight + MaterialRowGap) + 8f;
            string[] infoLabels = { "制作时间", "成功率", "银两消耗", "技艺经验" };
            string[] infoValues = { "30 秒", "85%", "2,000 两", "+150" };
            _infoItems = new ContainerControl[4];
            int infoCols = 2;
            float infoColW = (middleW - Padding * 2 - InfoGridGap) * 0.5f;
            for (int i = 0; i < 4; i++)
            {
                int col = i % infoCols;
                int row = i / infoCols;
                float itemX = Padding + col * (infoColW + InfoGridGap);
                float itemY = infoTop + row * (40f + InfoGridGap);

                var item = new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(itemX, itemY),
                    Size = new Float2(infoColW, 40f),
                    BackgroundColor = InkWashTheme.BaseElevated,
                };
                _infoItems[i] = item;
                _detailPanel.AddChild(item);

                var labL = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(12f, 0f),
                    Size = new Float2(80f, 40f),
                    Text = infoLabels[i],
                    TextColor = InkWashTheme.TextTertiary,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                item.AddChild(labL);

                var valL = new Label
                {
                    AnchorPreset = AnchorPresets.TopRight,
                    Location = new Float2(infoColW - 92f, 0f),
                    Size = new Float2(80f, 40f),
                    Text = infoValues[i],
                    TextColor = i == 1 ? InkWashTheme.TextJade : InkWashTheme.TextDefault,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 13f),
                    HorizontalAlignment = TextAlignment.Far,
                    VerticalAlignment = TextAlignment.Center,
                };
                item.AddChild(valL);
            }
        }

        private void BuildRightBench()
        {
            float contentTop = TopBarHeight + PanelGap;
            float contentBottom = MainPanelSize.Y - PanelGap;
            float contentH = contentBottom - contentTop;
            float benchX = MainPanelSize.X - Padding - RightBenchWidth;

            _rightBench = new Panel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(benchX, contentTop),
                Size = new Float2(RightBenchWidth, contentH),
                BackgroundColor = Color.Transparent,
            };
            _mainPanel.AddChild(_rightBench);

            float cursorY = Padding;
            float innerW = RightBenchWidth - Padding * 2;

            var previewPanel = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(Padding, cursorY),
                Size = new Float2(innerW, 120f),
                BackgroundColor = new Color(
                    InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G,
                    InkWashTheme.GoldPrimary.B, 0.04f),
            };
            _rightBench.AddChild(previewPanel);

            _previewCell = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopCenter,
                Location = new Float2((innerW - 64f) * 0.5f, 8f),
                Size = new Float2(64f, 64f),
                BackgroundColor = new Color(
                    InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G,
                    InkWashTheme.GoldPrimary.B, 0.12f),
            };
            previewPanel.AddChild(_previewCell);

            _previewNameLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopCenter,
                Location = new Float2((innerW - 160f) * 0.5f, 76f),
                Size = new Float2(160f, 20f),
                Text = "玄铁剑",
                TextColor = InkWashTheme.TextGold,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 16f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            previewPanel.AddChild(_previewNameLabel);

            var previewTags = new Label
            {
                AnchorPreset = AnchorPresets.TopCenter,
                Location = new Float2((innerW - 200f) * 0.5f, 96f),
                Size = new Float2(200f, 18f),
                Text = "传说 · 武器·剑",
                TextColor = InkWashTheme.TextSecondary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            previewPanel.AddChild(previewTags);

            cursorY += 120f + Padding;

            string[] attrLabels = { "五行", "攻击力", "暴击率", "会心率" };
            string[] attrValues = { "金", "+120", "+5%", "+3%" };
            for (int i = 0; i < 4; i++)
            {
                var row = new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(Padding, cursorY + i * 22f),
                    Size = new Float2(innerW, 22f),
                    BackgroundColor = Color.Transparent,
                };
                _rightBench.AddChild(row);

                var labL = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(8f, 0f),
                    Size = new Float2(100f, 22f),
                    Text = attrLabels[i],
                    TextColor = InkWashTheme.TextTertiary,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                row.AddChild(labL);

                var valL = new Label
                {
                    AnchorPreset = AnchorPresets.TopRight,
                    Location = new Float2(innerW - 88f, 0f),
                    Size = new Float2(80f, 22f),
                    Text = attrValues[i],
                    TextColor = i == 3 ? InkWashTheme.TextJade : InkWashTheme.TextDefault,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 13f),
                    HorizontalAlignment = TextAlignment.Far,
                    VerticalAlignment = TextAlignment.Center,
                };
                row.AddChild(valL);
            }
            cursorY += 4 * 22f + Padding;

            var progHeadRow = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(Padding, cursorY),
                Size = new Float2(innerW, 20f),
                BackgroundColor = Color.Transparent,
            };
            _rightBench.AddChild(progHeadRow);

            var progLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(8f, 0f),
                Size = new Float2(60f, 20f),
                Text = "正在制作",
                TextColor = InkWashTheme.TextTertiary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            progHeadRow.AddChild(progLabel);

            var progName = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(70f, 0f),
                Size = new Float2(innerW - 70f - 50f, 20f),
                Text = "精铁护腕",
                TextColor = InkWashTheme.TextDefault,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            progHeadRow.AddChild(progName);

            var progPct = new Label
            {
                AnchorPreset = AnchorPresets.TopRight,
                Location = new Float2(innerW - 48f, 0f),
                Size = new Float2(40f, 20f),
                Text = "65%",
                TextColor = InkWashTheme.TextGold,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 12f),
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
            };
            progHeadRow.AddChild(progPct);

            var progressTrack = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(Padding, cursorY + 22f),
                Size = new Float2(innerW, 4f),
                BackgroundColor = new Color(
                    InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G,
                    InkWashTheme.GoldPrimary.B, 0.08f),
            };
            _rightBench.AddChild(progressTrack);

            _craftProgressBar = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Float2(innerW * 0.65f, 4f),
                BackgroundColor = InkWashTheme.GoldPrimary,
            };
            progressTrack.AddChild(_craftProgressBar);

            cursorY += 22f + 4f + Padding;

            var batchRow = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(Padding, cursorY),
                Size = new Float2(innerW, BatchBtnHeight),
                BackgroundColor = Color.Transparent,
            };
            _rightBench.AddChild(batchRow);

            var batchLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(8f, 0f),
                Size = new Float2(40f, BatchBtnHeight),
                Text = "批量",
                TextColor = InkWashTheme.TextTertiary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            batchRow.AddChild(batchLabel);

            _batchButtons = new InkButton[3];
            int[] batchCounts = { 1, 5, 10 };
            float batchStartX = 52f;
            float batchGap = 8f;
            for (int i = 0; i < 3; i++)
            {
                bool isActive = (i == 0);
                var btn = new InkButton
                {
                    Variant = isActive ? InkButtonVariant.Primary : InkButtonVariant.Ghost,
                    ButtonSize = InkButtonSize.Sm,
                    Text = batchCounts[i].ToString(),
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(batchStartX + i * (BatchBtnWidth + batchGap), 0f),
                    Size = new Float2(BatchBtnWidth, BatchBtnHeight),
                };
                _batchButtons[i] = btn;
                batchRow.AddChild(btn);
            }

            cursorY += BatchBtnHeight + 8f;

            _craftButton = new InkButton
            {
                Variant = InkButtonVariant.Primary,
                ButtonSize = InkButtonSize.Md,
                Text = "材料不足 · 2,000 两",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(Padding, cursorY),
                Size = new Float2(innerW, 40f),
            };
            _rightBench.AddChild(_craftButton);

            cursorY += 40f + Padding;

            var logTitle = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(Padding, cursorY),
                Size = new Float2(120f, 20f),
                Text = "制作日志",
                TextColor = InkWashTheme.TextSecondary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _rightBench.AddChild(logTitle);

            cursorY += 24f;

            string[] logNames = { "精铁护腕", "铁质长枪", "铁质长枪", "寒铁刀", "铁质长枪" };
            string[] logTimes = { "14:32", "14:25", "14:18", "14:10", "14:02" };
            bool[] logSuccess = { true, true, true, false, true };
            for (int i = 0; i < 5; i++)
            {
                var row = new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(Padding, cursorY + i * (LogItemHeight + LogItemGap)),
                    Size = new Float2(innerW, LogItemHeight),
                    BackgroundColor = InkWashTheme.BaseElevated,
                };
                _rightBench.AddChild(row);

                var dot = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(8f, 0f),
                    Size = new Float2(12f, LogItemHeight),
                    Text = logSuccess[i] ? "✓" : "✕",
                    TextColor = logSuccess[i] ? InkWashTheme.TextJade : InkWashTheme.TextBlood,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                };
                row.AddChild(dot);

                var nameL = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(24f, 0f),
                    Size = new Float2(innerW - 24f - 50f, LogItemHeight),
                    Text = logNames[i],
                    TextColor = InkWashTheme.TextDefault,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                row.AddChild(nameL);

                var timeL = new Label
                {
                    AnchorPreset = AnchorPresets.TopRight,
                    Location = new Float2(innerW - 48f, 0f),
                    Size = new Float2(40f, LogItemHeight),
                    Text = logTimes[i],
                    TextColor = InkWashTheme.TextTertiary,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 11f),
                    HorizontalAlignment = TextAlignment.Far,
                    VerticalAlignment = TextAlignment.Center,
                };
                row.AddChild(timeL);
            }
        }

        private void EmitGoldAtButton(Control button)
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
                FlaxEngine.Debug.LogWarning($"[CraftingPage] EmitGoldAtButton 失败: {ex.Message}");
            }
        }

        public void RefreshLayout()
        {
            try
            {
                float sw = Width;
                float sh = Height;

                if (_mainPanel != null)
                {
                    float panelX = (sw - MainPanelSize.X) * 0.5f;
                    float panelY = (sh - MainPanelSize.Y) * 0.5f;
                    _mainPanel.Location = new Float2(
                        panelX > 0f ? panelX : 0f,
                        panelY > 0f ? panelY : 0f);
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[CraftingPage] RefreshLayout 失败: {ex.Message}");
            }
        }

        public override void OnParentResized()
        {
            base.OnParentResized();
            RefreshLayout();
        }
    }
}

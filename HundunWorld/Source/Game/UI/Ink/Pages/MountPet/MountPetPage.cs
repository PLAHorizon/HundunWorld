using System;
using FlaxEngine;
using FlaxEngine.GUI;
using Game.Character.Attributes;
using HundunWorld.Game.UI.StyleSystem;

namespace HundunWorld.Game.UI.Ink.Pages.MountPet
{
    public class MountPetPage : ContainerControl, IInkPage
    {
        private static readonly Float2 MainPanelSize = new Float2(1400f, 900f);
        private const float TopBarHeight = 56f;
        private const float Padding = 12f;
        private const float PanelGap = 10f;
        private const float LeftListWidth = 300f;
        private const float RightOpsWidth = 340f;
        private const float ListHeaderHeight = 40f;
        private const float MountItemHeight = 60f;
        private const float MountItemGap = 6f;
        private const float PreviewStageHeight = 280f;
        private const float AttrCardHeight = 78f;
        private const float AttrCardGap = 12f;
        private const float SkillSlotSize = 64f;
        private const float SkillSlotGap = 12f;
        private const float MorphThumbSize = 84f;
        private const float MorphThumbGap = 8f;

        private ContainerControl _mainPanel;
        private ContainerControl _topBar;
        private InkButton _tabMount;
        private InkButton _tabPet;
        private Label _centerTitleLabel;
        private InkButton _backButton;
        private ContainerControl _leftList;
        private ContainerControl[] _mountItems;
        private ContainerControl _middleContent;
        private ContainerControl _previewStage;
        private Label _previewGlyphLabel;
        private Label _mountNameLabel;
        private Label _mountSubLabel;
        private ContainerControl[] _attrCards;
        private ContainerControl[] _skillSlots;
        private Label _loreLabel;
        private ContainerControl _rightOps;
        private Label _deployStatusLabel;
        private InkButton _btnRest;
        private InkButton _btnFeed;
        private InkButton _btnTrain;
        private InkButton _btnSkillUp;
        private InkButton _btnSkillLearn;
        private InkButton[] _morphThumbs;

        public event Action<string> NavigationRequested;
        public InkParticleSystem ParticleSystem { get; set; }

        private CharacterAttributesComponent _boundCharacter;

        public void BindCharacter(CharacterAttributesComponent component)
        {
            _boundCharacter = component;
        }

        public MountPetPage()
        {
            AnchorPreset = AnchorPresets.StretchAll;
            Offsets = Margin.Zero;
            BackgroundColor = InkWashTheme.Scrim;
            ClipChildren = false;
            AutoFocus = false;

            BuildMainPanel();
            BuildTopBar();
            BuildLeftList();
            BuildMiddleContent();
            BuildRightOps();
        }

        private void BuildMainPanel()
        {
            _mainPanel = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = MainPanelSize,
                BackgroundColor = new Color(14f / 255f, 16f / 255f, 22f / 255f, 0.88f),
            };
            AddChild(_mainPanel);
        }

        private void BuildTopBar()
        {
            _topBar = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Float2(MainPanelSize.X, TopBarHeight),
            };
            _mainPanel.AddChild(_topBar);

            _tabMount = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Md,
                Text = "坐骑",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(20f, (TopBarHeight - 28f) * 0.5f),
                Size = new Float2(80f, 28f),
            };
            _topBar.AddChild(_tabMount);

            _tabPet = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Md,
                Text = "灵兽",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(20f + 80f + 24f, (TopBarHeight - 28f) * 0.5f),
                Size = new Float2(80f, 28f),
            };
            _topBar.AddChild(_tabPet);

            _centerTitleLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopCenter,
                Location = new Float2((MainPanelSize.X - 200f) * 0.5f, 0f),
                Size = new Float2(200f, TopBarHeight),
                Text = "坐骑灵兽",
                TextColor = InkWashTheme.GoldPrimary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 20f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            _topBar.AddChild(_centerTitleLabel);

            _backButton = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Md,
                Text = "✕",
                AnchorPreset = AnchorPresets.TopRight,
                Location = new Float2(MainPanelSize.X - 50f - Padding, (TopBarHeight - 36f) * 0.5f),
                Size = new Float2(36f, 36f),
            };
            _backButton.Clicked += () => NavigationRequested?.Invoke("back-hud");
            _topBar.AddChild(_backButton);
        }

        private void BuildLeftList()
        {
            float contentTop = TopBarHeight + PanelGap;

            _leftList = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(Padding, contentTop),
                Size = new Float2(LeftListWidth, MainPanelSize.Y - contentTop - Padding),
            };
            _mainPanel.AddChild(_leftList);

            var headerPanel = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Float2(LeftListWidth, ListHeaderHeight),
            };
            _leftList.AddChild(headerPanel);

            var headerTitle = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, 0f),
                Size = new Float2(160f, ListHeaderHeight),
                Text = "坐骑名册",
                TextColor = InkWashTheme.TextDefault,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            headerPanel.AddChild(headerTitle);

            var headerCount = new Label
            {
                AnchorPreset = AnchorPresets.TopRight,
                Location = new Float2(LeftListWidth - 60f, 0f),
                Size = new Float2(44f, ListHeaderHeight),
                Text = "06",
                TextColor = InkWashTheme.TextTertiary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 12f),
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
            };
            headerPanel.AddChild(headerCount);

            string[] mountNames = { "墨麒麟", "踏雪乌骓", "雪羽鹤", "青骢追风", "黄骠马", "雪原驯鹿" };
            string[] mountChars = { "麟", "骓", "鹤", "骢", "马", "鹿" };
            string[] mountBadges = { "出战", "史诗", "飞禽", "稀有", "良好", "普通" };
            int[] mountLevels = { 85, 72, 68, 60, 45, 30 };
            int[] mountSpeeds = { 320, 280, 265, 250, 210, 180 };
            string[] mountQualityNames = { "legendary", "epic", "epic", "rare", "uncommon", "common" };

            _mountItems = new ContainerControl[6];
            float itemW = LeftListWidth - Padding * 2;
            float listTop = ListHeaderHeight + Padding;

            for (int i = 0; i < 6; i++)
            {
                float itemY = listTop + i * (MountItemHeight + MountItemGap);
                bool isActive = (i == 0);
                Color qualityColor = mountQualityNames[i] switch
                {
                    "legendary" => InkWashTheme.QualityLegendary,
                    "epic" => InkWashTheme.QualityEpic,
                    "rare" => InkWashTheme.QualityRare,
                    "uncommon" => InkWashTheme.QualityUncommon,
                    _ => InkWashTheme.QualityCommon,
                };

                var item = new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(Padding, itemY),
                    Size = new Float2(itemW, MountItemHeight),
                };
                _mountItems[i] = item;
                _leftList.AddChild(item);

                var iconBox = new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(8f, 8f),
                    Size = new Float2(44f, 44f),
                };
                item.AddChild(iconBox);

                var charLabel = new Label
                {
                    AnchorPreset = AnchorPresets.StretchAll,
                    Text = mountChars[i],
                    TextColor = isActive ? InkWashTheme.GoldBright : qualityColor,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 22f),
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                };
                iconBox.AddChild(charLabel);

                var nameLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(60f, 8f),
                    Size = new Float2(itemW - 60f - 56f, 22f),
                    Text = mountNames[i],
                    TextColor = InkWashTheme.TextDefault,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 14f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                item.AddChild(nameLabel);

                var badgeLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopRight,
                    Location = new Float2(itemW - 54f, 8f),
                    Size = new Float2(46f, 18f),
                    Text = mountBadges[i],
                    TextColor = isActive ? InkWashTheme.TextOnBrand : qualityColor,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 10f),
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                };
                item.AddChild(badgeLabel);

                var statLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(60f, 30f),
                    Size = new Float2(itemW - 68f, 18f),
                    Text = "Lv." + mountLevels[i] + "  ·  速 " + mountSpeeds[i],
                    TextColor = InkWashTheme.TextSecondary,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 11f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                item.AddChild(statLabel);
            }
        }

        private void BuildMiddleContent()
        {
            float contentTop = TopBarHeight + PanelGap;
            float middleX = Padding + LeftListWidth + PanelGap;
            float middleW = MainPanelSize.X - Padding * 2 - LeftListWidth - RightOpsWidth - PanelGap * 2;

            _middleContent = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(middleX, contentTop),
                Size = new Float2(middleW, MainPanelSize.Y - contentTop - Padding),
            };
            _mainPanel.AddChild(_middleContent);

            float innerW = middleW - Padding * 2;
            float cursorY = Padding;

            _previewStage = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(Padding, cursorY),
                Size = new Float2(innerW, PreviewStageHeight),
            };
            _middleContent.AddChild(_previewStage);

            var glyphCircle = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2((innerW - 120f) * 0.5f, (PreviewStageHeight - 120f) * 0.5f),
                Size = new Float2(120f, 120f),
            };
            _previewStage.AddChild(glyphCircle);

            _previewGlyphLabel = new Label
            {
                AnchorPreset = AnchorPresets.StretchAll,
                Text = "麟",
                TextColor = InkWashTheme.GoldBright,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 64f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            glyphCircle.AddChild(_previewGlyphLabel);

            var hintLabel = new Label
            {
                AnchorPreset = AnchorPresets.BottomCenter,
                Location = new Float2((innerW - 200f) * 0.5f, PreviewStageHeight - 28f),
                Size = new Float2(200f, 18f),
                Text = "拖拽旋转 · 滚轮缩放",
                TextColor = InkWashTheme.TextTertiary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 11f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            _previewStage.AddChild(hintLabel);

            cursorY += PreviewStageHeight + 16f;

            _mountNameLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(Padding, cursorY),
                Size = new Float2(200f, 36f),
                Text = "墨麒麟",
                TextColor = InkWashTheme.TextDefault,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 28f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _middleContent.AddChild(_mountNameLabel);

            _mountSubLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(Padding, cursorY + 36f),
                Size = new Float2(innerW - Padding * 2, 18f),
                Text = "瑞兽 · 异兽类 · 已驯服",
                TextColor = InkWashTheme.TextTertiary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _middleContent.AddChild(_mountSubLabel);

            var wuxingBadge = new Label
            {
                AnchorPreset = AnchorPresets.TopRight,
                Location = new Float2(innerW - 150f, cursorY + 6f),
                Size = new Float2(70f, 22f),
                Text = "五行 · 金",
                TextColor = InkWashTheme.ElementMetal,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 11f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            _middleContent.AddChild(wuxingBadge);

            var typeBadge = new Label
            {
                AnchorPreset = AnchorPresets.TopRight,
                Location = new Float2(innerW - 72f, cursorY + 6f),
                Size = new Float2(60f, 22f),
                Text = "坐骑",
                TextColor = InkWashTheme.TextSecondary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 11f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            _middleContent.AddChild(typeBadge);

            cursorY += 36f + 18f + 16f;

            string[] attrLabels = { "速度", "耐力", "跳跃力" };
            string[] attrValues = { "320", "4500", "8.5" };
            string[] attrUnits = { "尺/息", "/4500", "丈" };
            float[] attrProgress = { 1.0f, 1.0f, 0.85f };

            _attrCards = new ContainerControl[3];
            float attrColW = (innerW - AttrCardGap * 2) / 3f;
            for (int i = 0; i < 3; i++)
            {
                float cardX = Padding + i * (attrColW + AttrCardGap);
                var card = new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(cardX, cursorY),
                    Size = new Float2(attrColW, AttrCardHeight),
                };
                _attrCards[i] = card;
                _middleContent.AddChild(card);

                var labL = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(12f, 10f),
                    Size = new Float2(attrColW - 24f, 16f),
                    Text = attrLabels[i],
                    TextColor = InkWashTheme.TextTertiary,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                card.AddChild(labL);

                var valL = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(12f, 28f),
                    Size = new Float2(attrColW - 24f, 24f),
                    Text = attrValues[i] + " " + attrUnits[i],
                    TextColor = i == 0 ? InkWashTheme.GoldBright : InkWashTheme.TextDefault,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 20f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                card.AddChild(valL);

                if (i == 1)
                {
                    var track = new ContainerControl
                    {
                        AnchorPreset = AnchorPresets.TopLeft,
                        Location = new Float2(12f, AttrCardHeight - 14f),
                        Size = new Float2(attrColW - 24f, 6f),
                    };
                    card.AddChild(track);

                    var fill = new ContainerControl
                    {
                        AnchorPreset = AnchorPresets.TopLeft,
                        Location = Float2.Zero,
                        Size = new Float2((attrColW - 24f) * attrProgress[i], 6f),
                    };
                    track.AddChild(fill);
                }
            }

            cursorY += AttrCardHeight + 16f;

            var skillHeader = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(Padding, cursorY),
                Size = new Float2(innerW, 20f),
            };
            _middleContent.AddChild(skillHeader);

            var skillTitle = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 0f),
                Size = new Float2(120f, 20f),
                Text = "特殊技能",
                TextColor = InkWashTheme.TextDefault,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            skillHeader.AddChild(skillTitle);

            var skillCount = new Label
            {
                AnchorPreset = AnchorPresets.TopRight,
                Location = new Float2(innerW - 60f, 0f),
                Size = new Float2(50f, 20f),
                Text = "2 / 3",
                TextColor = InkWashTheme.TextTertiary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 11f),
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
            };
            skillHeader.AddChild(skillCount);

            cursorY += 24f;

            _skillSlots = new ContainerControl[3];
            string[] skillChars = { "火", "撞", "+" };
            string[] skillNames = { "踏火穿云", "神威冲撞", "未学习" };
            for (int i = 0; i < 3; i++)
            {
                bool filled = (i < 2);
                var slot = new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(Padding + i * (SkillSlotSize + SkillSlotGap), cursorY),
                    Size = new Float2(SkillSlotSize, SkillSlotSize),
                };
                _skillSlots[i] = slot;
                _middleContent.AddChild(slot);

                var slotChar = new Label
                {
                    AnchorPreset = AnchorPresets.TopCenter,
                    Location = new Float2((SkillSlotSize - 48f) * 0.5f, 8f),
                    Size = new Float2(48f, 28f),
                    Text = skillChars[i],
                    TextColor = filled ? InkWashTheme.GoldBright : InkWashTheme.TextTertiary,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 20f),
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                };
                slot.AddChild(slotChar);

                var slotName = new Label
                {
                    AnchorPreset = AnchorPresets.TopCenter,
                    Location = new Float2((SkillSlotSize - 60f) * 0.5f, 38f),
                    Size = new Float2(60f, 16f),
                    Text = skillNames[i],
                    TextColor = InkWashTheme.TextSecondary,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 9f),
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                };
                slot.AddChild(slotName);
            }

            float skillDetailX = Padding + 3 * (SkillSlotSize + SkillSlotGap);
            float skillDetailW = innerW - skillDetailX - Padding;
            var skillDetail = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(skillDetailX, cursorY),
                Size = new Float2(skillDetailW, SkillSlotSize),
            };
            _middleContent.AddChild(skillDetail);

            var sdName = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(10f, 6f),
                Size = new Float2(120f, 18f),
                Text = "踏火穿云",
                TextColor = InkWashTheme.GoldBright,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 12f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            skillDetail.AddChild(sdName);

            var sdLvl = new Label
            {
                AnchorPreset = AnchorPresets.TopRight,
                Location = new Float2(skillDetailW - 60f, 6f),
                Size = new Float2(50f, 18f),
                Text = "Lv.3",
                TextColor = InkWashTheme.TextTertiary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 11f),
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
            };
            skillDetail.AddChild(sdLvl);

            var sdDesc = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(10f, 28f),
                Size = new Float2(skillDetailW - 20f, 32f),
                Text = "奔腾时蹄生烈焰，无视减速地形，持续 8 息。",
                TextColor = InkWashTheme.TextSecondary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Near,
            };
            skillDetail.AddChild(sdDesc);

            cursorY += SkillSlotSize + 16f;

            var lorePanel = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(Padding, cursorY),
                Size = new Float2(innerW, 56f),
            };
            _middleContent.AddChild(lorePanel);

            _loreLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 8f),
                Size = new Float2(innerW - 24f, 40f),
                Text = "麒麟踏火而出，墨鳞如甲，乃瑞兽之首。性烈而忠，唯修为深厚者可驭。",
                TextColor = InkWashTheme.TextSecondary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 12f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            lorePanel.AddChild(_loreLabel);
        }

        private void BuildRightOps()
        {
            float contentTop = TopBarHeight + PanelGap;
            float opsX = MainPanelSize.X - Padding - RightOpsWidth;

            _rightOps = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(opsX, contentTop),
                Size = new Float2(RightOpsWidth, MainPanelSize.Y - contentTop - Padding),
            };
            _mainPanel.AddChild(_rightOps);

            float innerW = RightOpsWidth - Padding * 2;
            float cursorY = Padding;

            var deployCard = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(Padding, cursorY),
                Size = new Float2(innerW, 76f),
            };
            _rightOps.AddChild(deployCard);

            var deployHead = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 10f),
                Size = new Float2(innerW - 24f, 24f),
            };
            deployCard.AddChild(deployHead);

            _deployStatusLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(14f, 0f),
                Size = new Float2(100f, 24f),
                Text = "出战中",
                TextColor = InkWashTheme.GoldBright,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            deployHead.AddChild(_deployStatusLabel);

            _btnRest = new InkButton
            {
                Variant = InkButtonVariant.Default,
                ButtonSize = InkButtonSize.Sm,
                Text = "休息",
                AnchorPreset = AnchorPresets.TopRight,
                Location = new Float2(innerW - 24f - 80f, 0f),
                Size = new Float2(80f, 24f),
            };
            deployHead.AddChild(_btnRest);

            var deployHint = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 40f),
                Size = new Float2(innerW - 24f, 28f),
                Text = "出战期间持续消耗耐力，归厩后自动恢复。",
                TextColor = InkWashTheme.TextTertiary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            deployCard.AddChild(deployHint);

            cursorY += 76f + 14f;

            var feedTitle = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(Padding, cursorY),
                Size = new Float2(120f, 20f),
                Text = "驯养喂养",
                TextColor = InkWashTheme.TextDefault,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _rightOps.AddChild(feedTitle);

            cursorY += 24f;

            float feedBtnW = (innerW - 8f) * 0.5f;
            _btnFeed = new InkButton
            {
                Variant = InkButtonVariant.Default,
                ButtonSize = InkButtonSize.Md,
                Text = "喂养",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(Padding, cursorY),
                Size = new Float2(feedBtnW, 34f),
            };
            _rightOps.AddChild(_btnFeed);

            _btnTrain = new InkButton
            {
                Variant = InkButtonVariant.Primary,
                ButtonSize = InkButtonSize.Md,
                Text = "驯养",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(Padding + feedBtnW + 8f, cursorY),
                Size = new Float2(feedBtnW, 34f),
            };
            _rightOps.AddChild(_btnTrain);

            cursorY += 34f + 8f;

            var costRow = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(Padding, cursorY),
                Size = new Float2(innerW, 30f),
            };
            _rightOps.AddChild(costRow);

            var costLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(10f, 0f),
                Size = new Float2(70f, 30f),
                Text = "消耗材料",
                TextColor = InkWashTheme.TextTertiary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            costRow.AddChild(costLabel);

            var costDetail = new Label
            {
                AnchorPreset = AnchorPresets.TopRight,
                Location = new Float2(innerW - 170f, 0f),
                Size = new Float2(160f, 30f),
                Text = "灵草 ×3   驯兽丹 ×1",
                TextColor = InkWashTheme.TextSecondary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 11f),
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
            };
            costRow.AddChild(costDetail);

            cursorY += 30f + 14f;

            var skillUpTitle = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(Padding, cursorY),
                Size = new Float2(120f, 20f),
                Text = "技能研习",
                TextColor = InkWashTheme.TextDefault,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _rightOps.AddChild(skillUpTitle);

            cursorY += 24f;

            var skillUpRow = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(Padding, cursorY),
                Size = new Float2(innerW, 50f),
            };
            _rightOps.AddChild(skillUpRow);

            var skillUpIcon = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(8f, 5f),
                Size = new Float2(40f, 40f),
            };
            skillUpRow.AddChild(skillUpIcon);

            var skillUpIconChar = new Label
            {
                AnchorPreset = AnchorPresets.StretchAll,
                Text = "火",
                TextColor = InkWashTheme.GoldBright,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 18f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            skillUpIcon.AddChild(skillUpIconChar);

            var skillUpName = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(56f, 6f),
                Size = new Float2(innerW - 56f - 90f - 8f, 18f),
                Text = "踏火穿云",
                TextColor = InkWashTheme.TextDefault,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 12f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            skillUpRow.AddChild(skillUpName);

            var skillUpLvl = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(56f, 24f),
                Size = new Float2(innerW - 56f - 90f - 8f, 14f),
                Text = "Lv.3 → 4",
                TextColor = InkWashTheme.TextTertiary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 11f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            skillUpRow.AddChild(skillUpLvl);

            var skillTrack = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(56f, 40f),
                Size = new Float2(innerW - 56f - 90f - 8f, 6f),
            };
            skillUpRow.AddChild(skillTrack);

            var skillFill = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Float2((innerW - 56f - 90f - 8f) * 0.72f, 6f),
            };
            skillTrack.AddChild(skillFill);

            _btnSkillUp = new InkButton
            {
                Variant = InkButtonVariant.Default,
                ButtonSize = InkButtonSize.Sm,
                Text = "升级",
                AnchorPreset = AnchorPresets.TopRight,
                Location = new Float2(innerW - 8f - 60f, 10f),
                Size = new Float2(60f, 30f),
            };
            skillUpRow.AddChild(_btnSkillUp);

            cursorY += 50f + 8f;

            _btnSkillLearn = new InkButton
            {
                Variant = InkButtonVariant.Default,
                ButtonSize = InkButtonSize.Md,
                Text = "+ 学习新技能",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(Padding, cursorY),
                Size = new Float2(innerW, 34f),
            };
            _rightOps.AddChild(_btnSkillLearn);

            cursorY += 34f + 14f;

            var morphTitle = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(Padding, cursorY),
                Size = new Float2(120f, 20f),
                Text = "外观幻化",
                TextColor = InkWashTheme.TextDefault,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _rightOps.AddChild(morphTitle);

            cursorY += 24f;

            _morphThumbs = new InkButton[3];
            string[] morphChars = { "麟", "焰", "霜" };
            float morphColW = (innerW - MorphThumbGap * 2) / 3f;
            for (int i = 0; i < 3; i++)
            {
                bool isActive = (i == 0);
                var thumb = new InkButton
                {
                    Variant = isActive ? InkButtonVariant.Primary : InkButtonVariant.Default,
                    ButtonSize = InkButtonSize.Md,
                    Text = morphChars[i],
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(Padding + i * (morphColW + MorphThumbGap), cursorY),
                    Size = new Float2(morphColW, MorphThumbSize),
                };
                _morphThumbs[i] = thumb;
                _rightOps.AddChild(thumb);
            }

            cursorY += MorphThumbSize + 14f;

            var speedPanel = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(Padding, cursorY),
                Size = new Float2(innerW, 100f),
            };
            _rightOps.AddChild(speedPanel);

            var speedTitle = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 10f),
                Size = new Float2(160f, 20f),
                Text = "移速加成",
                TextColor = InkWashTheme.TextDefault,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            speedPanel.AddChild(speedTitle);

            var baseSpeedLab = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 34f),
                Size = new Float2(60f, 14f),
                Text = "基础移速",
                TextColor = InkWashTheme.TextTertiary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            speedPanel.AddChild(baseSpeedLab);

            var baseSpeedVal = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 48f),
                Size = new Float2(60f, 20f),
                Text = "100",
                TextColor = InkWashTheme.TextSecondary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 16f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            speedPanel.AddChild(baseSpeedVal);

            var rideSpeedLab = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(120f, 34f),
                Size = new Float2(120f, 14f),
                Text = "骑乘移速",
                TextColor = InkWashTheme.TextTertiary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            speedPanel.AddChild(rideSpeedLab);

            var rideSpeedVal = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(120f, 48f),
                Size = new Float2(120f, 22f),
                Text = "160  +60%",
                TextColor = InkWashTheme.GoldBright,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 18f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            speedPanel.AddChild(rideSpeedVal);

            var speedTrack = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 76f),
                Size = new Float2(innerW - 24f, 6f),
            };
            speedPanel.AddChild(speedTrack);

            var speedFill = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Float2((innerW - 24f) * 0.80f, 6f),
            };
            speedTrack.AddChild(speedFill);
        }

        public void RefreshLayout()
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

        public override void OnParentResized()
        {
            base.OnParentResized();
            RefreshLayout();
        }
    }
}

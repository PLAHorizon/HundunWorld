using FlaxEngine;
using FlaxEngine.GUI;
using Game.Character.Attributes;
using HundunWorld.Game.UI.StyleSystem;
using Game.Combat.Skills;
using System;

namespace HundunWorld.Game.UI.Ink.Pages.Character
{
    public class SkillPanelPage : ContainerControl, IInkPage
    {
        private static readonly Float2 MainPanelSize = new Float2(1400f, 900f);
        private const float TopBarHeight = 56f;
        private const float BottomBarHeight = 52f;
        private const float Padding = 20f;
        private const float LeftPanelWidth = 400f;
        private const float SkillIconSize = 48f;
        private const float RightPanelHPad = 20f;

        private InkPanelElevated _mainPanel;

        private ContainerControl _topBar;
        private Label _tabMartial;
        private Label _tabHeart;
        private Label _tabSpecial;
        private Label _tabActiveLine;
        private Label _topTitleLabel;
        private InkButton _closeBtn;

        private ContainerControl _leftCol;
        private ContainerControl _rightCol;

        private ContainerControl _bottomBar;
        private Label _upgradeCostLabel;
        private InkButton _btnUnequip;
        private InkButton _btnEquip;
        private InkButton _btnUpgrade;

        public event Action<string> NavigationRequested;
        public InkParticleSystem ParticleSystem { get; set; }

        private CharacterAttributesComponent _boundCharacter;

        public void BindCharacter(CharacterAttributesComponent component)
        {
            _boundCharacter = component;
        }

        public void BindSkills(SkillBase[] slots)
        {
        }

        public SkillPanelPage()
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
                BuildLeftColumn();
                BuildRightColumn();
                BuildBottomBar();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[SkillPanelPage] init failed: {ex.Message}");
            }
        }

        private void BuildMainPanel()
        {
            _mainPanel = new InkPanelElevated
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = MainPanelSize,
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

            float tabStartX = Padding;
            float tabY = (TopBarHeight - 28f) * 0.5f;

            _tabMartial = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(tabStartX, tabY),
                Size = new Float2(48f, 28f),
                Text = "武学",
                TextColor = InkWashTheme.TextGold,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 16f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            _topBar.AddChild(_tabMartial);

            _tabHeart = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(tabStartX + 80f, tabY),
                Size = new Float2(48f, 28f),
                Text = "心法",
                TextColor = InkWashTheme.TextSecondary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 16f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            _topBar.AddChild(_tabHeart);

            _tabSpecial = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(tabStartX + 160f, tabY),
                Size = new Float2(48f, 28f),
                Text = "奇术",
                TextColor = InkWashTheme.TextSecondary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 16f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            _topBar.AddChild(_tabSpecial);

            _tabActiveLine = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(tabStartX, TopBarHeight - 2f),
                Size = new Float2(48f, 2f),
                BackgroundColor = InkWashTheme.GoldPrimary,
            };
            _topBar.AddChild(_tabActiveLine);

            float centerX = MainPanelSize.X * 0.5f;

            var dividerL = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(centerX - 62f, (TopBarHeight - 24f) * 0.5f),
                Size = new Float2(1f, 24f),
                BackgroundColor = new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.2f),
            };
            _topBar.AddChild(dividerL);

            _topTitleLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(centerX - 50f, 0f),
                Size = new Float2(100f, TopBarHeight),
                Text = "武学心法",
                TextColor = InkWashTheme.GoldPrimary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 20f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            _topBar.AddChild(_topTitleLabel);

            var dividerR = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(centerX + 62f, (TopBarHeight - 24f) * 0.5f),
                Size = new Float2(1f, 24f),
                BackgroundColor = new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.2f),
            };
            _topBar.AddChild(dividerR);

            _closeBtn = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Sm,
                Text = "✕",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(MainPanelSize.X - Padding - 36f, (TopBarHeight - 36f) * 0.5f),
                Size = new Float2(36f, 36f),
            };
            _closeBtn.Clicked += OnCloseClicked;
            _topBar.AddChild(_closeBtn);
        }

        private void BuildLeftColumn()
        {
            float contentTop = TopBarHeight;
            float contentBottom = MainPanelSize.Y - BottomBarHeight;
            float contentH = contentBottom - contentTop;

            _leftCol = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, contentTop),
                Size = new Float2(LeftPanelWidth, contentH),
            };
            _mainPanel.AddChild(_leftCol);

            float borderX = LeftPanelWidth - 1f;
            var rightBorder = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(borderX, 0f),
                Size = new Float2(1f, contentH),
                BackgroundColor = new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.08f),
            };
            _leftCol.AddChild(rightBorder);

            float cursorY = 16f;
            float innerW = LeftPanelWidth - 32f;

            BuildSkillGroup(_leftCol, ref cursorY, innerW, "主动攻击",
                new string[] { "剑", "刀", "枪", "拳" },
                new string[] { "太极剑法", "狂风刀法", "杨家枪法", "降龙掌" },
                new string[] { "大师", "高级", "中级", "高级" },
                new Color[] { InkWashTheme.GoldPrimary, InkWashTheme.QualityRare, InkWashTheme.QualityUncommon, InkWashTheme.QualityEpic },
                new string[] { "武当派·外功", "丐帮·外功", "杨家·外功", "丐帮·刚猛" },
                0);

            BuildSkillGroup(_leftCol, ref cursorY, innerW, "被动强化",
                new string[] { "功", "步" },
                new string[] { "纯阳内功", "凌波微步" },
                new string[] { "高级", "中级" },
                new Color[] { InkWashTheme.QualityRare, InkWashTheme.QualityUncommon },
                new string[] { "全真教·内功", "逍遥派·轻功" },
                -1);

            BuildSkillGroup(_leftCol, ref cursorY, innerW, "特殊",
                new string[] { "针", "门" },
                new string[] { "暴雨梨花针", "奇门遁甲" },
                new string[] { "高级", "初级" },
                new Color[] { InkWashTheme.QualityEpic, InkWashTheme.QualityCommon },
                new string[] { "唐门·暗器", "奇门·阵法" },
                -1);

            cursorY += 8f;

            BuildSecretLibrary(_leftCol, ref cursorY, innerW);

            float progressY = contentH - 40f - 186f - 48f;
            BuildLearningProgress(_leftCol, ref progressY, innerW);

            float slotsY = progressY + 48f;
            BuildSlotsArea(_leftCol, ref slotsY, innerW);

            float synergyY = slotsY + 8f;
            BuildSynergyHint(_leftCol, ref synergyY, innerW);
        }

        private void BuildSkillGroup(ContainerControl parent, ref float cursorY, float innerW,
            string groupName,
            string[] icons, string[] names, string[] grades, Color[] gradeColors,
            string[] affixes, int selectedIndex)
        {
            float x = 16f;

            var groupLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(x + 22f, cursorY),
                Size = new Float2(120f, 18f),
                Text = groupName,
                TextColor = InkWashTheme.TextDefault,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            parent.AddChild(groupLabel);

            var groupLine = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(x + 22f + 120f + 8f, cursorY + 9f),
                Size = new Float2(innerW - x - 22f - 120f - 8f - x, 1f),
                BackgroundColor = new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.1f),
            };
            parent.AddChild(groupLine);

            cursorY += 24f;

            for (int i = 0; i < names.Length; i++)
            {
                float itemH = 56f;
                float itemGap = 6f;
                float itemY = cursorY;

                bool isSelected = i == selectedIndex;

                var itemBg = new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(x, itemY),
                    Size = new Float2(innerW, itemH),
                    BackgroundColor = isSelected
                        ? new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.08f)
                        : Color.Transparent,
                };
                parent.AddChild(itemBg);

                var iconBoxBorder = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(x + 8f, itemY + (itemH - SkillIconSize) * 0.5f),
                    Size = new Float2(SkillIconSize, SkillIconSize),
                    BackgroundColor = new Color(
                        gradeColors[i].R, gradeColors[i].G, gradeColors[i].B, 0.12f),
                };
                parent.AddChild(iconBoxBorder);

                var iconLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(x + 8f, itemY + (itemH - SkillIconSize) * 0.5f),
                    Size = new Float2(SkillIconSize, SkillIconSize),
                    Text = icons[i],
                    TextColor = gradeColors[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 24f),
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                };
                parent.AddChild(iconLabel);

                float nameX = x + 8f + SkillIconSize + 12f;
                float nameW = innerW - nameX;

                var nameLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(nameX, itemY + 6f),
                    Size = new Float2(nameW - 60f, 20f),
                    Text = names[i],
                    TextColor = isSelected ? InkWashTheme.TextDefault : InkWashTheme.TextDefault,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 14f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                parent.AddChild(nameLabel);

                var gradeBadge = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(nameX + nameW - 60f - 80f, itemY + 7f),
                    Size = new Float2(72f, 18f),
                    Text = grades[i],
                    TextColor = isSelected ? InkWashTheme.TextInverse : gradeColors[i],
                    BackgroundColor = isSelected
                        ? InkWashTheme.GoldPrimary
                        : new Color(gradeColors[i].R, gradeColors[i].G, gradeColors[i].B, 0.2f),
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 10f),
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                };
                parent.AddChild(gradeBadge);



                var affixLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(nameX, itemY + 28f),
                    Size = new Float2(nameW, 16f),
                    Text = affixes[i],
                    TextColor = InkWashTheme.TextSecondary,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                parent.AddChild(affixLabel);

                cursorY += itemH + itemGap;
            }

            cursorY += 8f;
        }

        private void BuildSecretLibrary(ContainerControl parent, ref float cursorY, float innerW)
        {
            float x = 16f;

            var libLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(x + 22f, cursorY),
                Size = new Float2(80f, 18f),
                Text = "秘籍库",
                TextColor = InkWashTheme.TextDefault,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            parent.AddChild(libLabel);

            var libLine = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(x + 22f + 80f + 8f, cursorY + 9f),
                Size = new Float2(innerW - x - 22f - 80f - 8f - x, 1f),
                BackgroundColor = new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.1f),
            };
            parent.AddChild(libLine);

            cursorY += 26f;

            string[] libNames = { "完整秘籍", "残卷", "心得", "传承玉简" };
            string[] libValues = { "8", "15", "6", "3" };
            Color[] libColors = { InkWashTheme.GoldBright, InkWashTheme.QualityUncommon, InkWashTheme.QualityRare, InkWashTheme.QualityEpic };
            string[] libIcons = { "◆", "◇", "○", "◇" };
            Color[] libIconColors = { InkWashTheme.GoldPrimary, InkWashTheme.QualityUncommon, InkWashTheme.QualityRare, InkWashTheme.QualityEpic };

            float gridW = innerW;
            float itemW = (gridW - 8f) * 0.5f;
            float itemH = 44f;

            for (int i = 0; i < 4; i++)
            {
                float colX = x + (i % 2) * (itemW + 8f);
                float rowY = cursorY + (i / 2) * (itemH + 8f);

                var bookBg = new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(colX, rowY),
                    Size = new Float2(itemW, itemH),
                    BackgroundColor = new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.04f),
                };
                parent.AddChild(bookBg);

                var bookIcon = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(colX + 8f, rowY + (itemH - 14f) * 0.5f),
                    Size = new Float2(14f, 14f),
                    Text = libIcons[i],
                    TextColor = libIconColors[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                };
                parent.AddChild(bookIcon);

                var bookName = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(colX + 28f, rowY + 4f),
                    Size = new Float2(itemW - 28f - 8f, 14f),
                    Text = libNames[i],
                    TextColor = InkWashTheme.TextSecondary,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                parent.AddChild(bookName);

                var bookValue = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(colX + 28f, rowY + 18f),
                    Size = new Float2(itemW - 28f - 8f, 20f),
                    Text = libValues[i],
                    TextColor = libColors[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 13f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                parent.AddChild(bookValue);
            }

            cursorY += 2f * (itemH + 8f) + 8f;
        }

        private void BuildLearningProgress(ContainerControl parent, ref float cursorY, float innerW)
        {
            float x = 16f;
            float barW = 80f;

            var progressContainer = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, cursorY),
                Size = new Float2(LeftPanelWidth, 40f),
                BackgroundColor = new Color(
                    InkWashTheme.Void.R, InkWashTheme.Void.G, InkWashTheme.Void.B, 0.3f),
            };
            parent.AddChild(progressContainer);

            var borderT = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 0f),
                Size = new Float2(LeftPanelWidth, 1f),
                BackgroundColor = new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.08f),
            };
            progressContainer.AddChild(borderT);

            var learnLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(x, 0f),
                Size = new Float2(60f, 40f),
                Text = "已学习",
                TextColor = InkWashTheme.TextSecondary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 12f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            progressContainer.AddChild(learnLabel);

            var progBar = new InkBar
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(LeftPanelWidth - x - 36f - barW, (40f - 6f) * 0.5f),
                Size = new Float2(barW, 6f),
                Value = 0.6f,
                FillVariant = InkBarFillVariant.Gold,
            };
            progressContainer.AddChild(progBar);

            var countLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(LeftPanelWidth - x - 36f, 0f),
                Size = new Float2(36f, 40f),
                Text = "12/20",
                TextColor = InkWashTheme.GoldBright,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 14f),
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
            };
            progressContainer.AddChild(countLabel);

            cursorY += 40f;
        }

        private void BuildSlotsArea(ContainerControl parent, ref float cursorY, float innerW)
        {
            float x = 16f;

            var slotsContainer = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, cursorY),
                Size = new Float2(LeftPanelWidth, 186f),
                BackgroundColor = new Color(
                    InkWashTheme.Void.R, InkWashTheme.Void.G, InkWashTheme.Void.B, 0.4f),
            };
            parent.AddChild(slotsContainer);

            var borderT = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 0f),
                Size = new Float2(LeftPanelWidth, 1f),
                BackgroundColor = new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.08f),
            };
            slotsContainer.AddChild(borderT);

            float sy = 12f;

            var xinfaTitle = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(x, sy),
                Size = new Float2(80f, 16f),
                Text = "心法槽位",
                TextColor = InkWashTheme.TextSecondary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 11f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            slotsContainer.AddChild(xinfaTitle);

            var xinfaCount = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(LeftPanelWidth - x - 40f, sy),
                Size = new Float2(40f, 16f),
                Text = "2/4",
                TextColor = InkWashTheme.TextTertiary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 10f),
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
            };
            slotsContainer.AddChild(xinfaCount);

            sy += 22f;

            float slotSize = 48f;
            float slotGap = 8f;
            string[] xinfaSlots = { "纯", "易", "", "" };
            bool[] xinfaActive = { true, true, false, false };

            for (int i = 0; i < 4; i++)
            {
                float sx = x + i * (slotSize + slotGap);

                var slotBg = new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(sx, sy),
                    Size = new Float2(slotSize, slotSize),
                    BackgroundColor = xinfaActive[i]
                        ? new Color(138f / 255f, 116f / 255f, 56f / 255f, 0.15f)
                        : new Color(0f, 0f, 0f, 0.3f),
                };
                slotsContainer.AddChild(slotBg);

                if (xinfaActive[i])
                {
                    var slotChar = new Label
                    {
                        AnchorPreset = AnchorPresets.TopLeft,
                        Location = new Float2(sx, sy),
                        Size = new Float2(slotSize, slotSize),
                        Text = xinfaSlots[i],
                        TextColor = InkWashTheme.GoldBright,
                        Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 22f),
                        HorizontalAlignment = TextAlignment.Center,
                        VerticalAlignment = TextAlignment.Center,
                    };
                    slotsContainer.AddChild(slotChar);
                }
                else
                {
                    var plusChar = new Label
                    {
                        AnchorPreset = AnchorPresets.TopLeft,
                        Location = new Float2(sx, sy),
                        Size = new Float2(slotSize, slotSize),
                        Text = "+",
                        TextColor = InkWashTheme.TextTertiary,
                        Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 16f),
                        HorizontalAlignment = TextAlignment.Center,
                        VerticalAlignment = TextAlignment.Center,
                    };
                    slotsContainer.AddChild(plusChar);
                }
            }

            sy += slotSize + 12f;

            var qishuTitle = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(x, sy),
                Size = new Float2(80f, 16f),
                Text = "奇术槽位",
                TextColor = InkWashTheme.TextSecondary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 11f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            slotsContainer.AddChild(qishuTitle);

            var qishuCount = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(LeftPanelWidth - x - 40f, sy),
                Size = new Float2(40f, 16f),
                Text = "1/3",
                TextColor = InkWashTheme.TextTertiary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 10f),
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
            };
            slotsContainer.AddChild(qishuCount);

            sy += 22f;

            string[] qishuSlots = { "火", "", "" };
            bool[] qishuActive = { true, false, false };

            for (int i = 0; i < 3; i++)
            {
                float sx = x + i * (slotSize + slotGap);

                var slotBg = new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(sx, sy),
                    Size = new Float2(slotSize, slotSize),
                    BackgroundColor = qishuActive[i]
                        ? new Color(138f / 255f, 116f / 255f, 56f / 255f, 0.15f)
                        : new Color(0f, 0f, 0f, 0.3f),
                };
                slotsContainer.AddChild(slotBg);

                if (qishuActive[i])
                {
                    var slotChar = new Label
                    {
                        AnchorPreset = AnchorPresets.TopLeft,
                        Location = new Float2(sx, sy),
                        Size = new Float2(slotSize, slotSize),
                        Text = qishuSlots[i],
                        TextColor = InkWashTheme.GoldBright,
                        Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 22f),
                        HorizontalAlignment = TextAlignment.Center,
                        VerticalAlignment = TextAlignment.Center,
                    };
                    slotsContainer.AddChild(slotChar);
                }
                else
                {
                    var plusChar = new Label
                    {
                        AnchorPreset = AnchorPresets.TopLeft,
                        Location = new Float2(sx, sy),
                        Size = new Float2(slotSize, slotSize),
                        Text = "+",
                        TextColor = InkWashTheme.TextTertiary,
                        Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 16f),
                        HorizontalAlignment = TextAlignment.Center,
                        VerticalAlignment = TextAlignment.Center,
                    };
                    slotsContainer.AddChild(plusChar);
                }
            }

            cursorY += 186f;
        }

        private void BuildSynergyHint(ContainerControl parent, ref float cursorY, float innerW)
        {
            float x = 16f;
            float hintH = 40f;

            var hintContainer = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(x, cursorY + 8f),
                Size = new Float2(innerW, hintH),
                BackgroundColor = new Color(94f / 255f, 139f / 255f, 126f / 255f, 0.06f),
            };
            parent.AddChild(hintContainer);

            var hintIcon = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(x + 6f, cursorY + 8f + 4f),
                Size = new Float2(12f, 12f),
                Text = "⛓",
                TextColor = InkWashTheme.JadePrimary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            parent.AddChild(hintIcon);

            var hintText = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(x + 24f, cursorY + 8f + 2f),
                Size = new Float2(innerW - 40f, hintH - 4f),
                Text = "纯阳内功与太极剑法联动：外功伤害提升15%，内力消耗降低10%",
                TextColor = InkWashTheme.TextJade,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Near,
            };
            parent.AddChild(hintText);

            cursorY += 8f + hintH + 8f;
        }

        private void BuildRightColumn()
        {
            float contentTop = TopBarHeight;
            float contentBottom = MainPanelSize.Y - BottomBarHeight;
            float contentH = contentBottom - contentTop;
            float rightW = MainPanelSize.X - LeftPanelWidth;
            float rightX = LeftPanelWidth;

            _rightCol = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(rightX, contentTop),
                Size = new Float2(rightW, contentH),
            };
            _mainPanel.AddChild(_rightCol);

            float ix = RightPanelHPad;
            float iw = rightW - RightPanelHPad * 2;
            float cursorY = 16f;

            BuildDemoArea(_rightCol, ref cursorY, iw, ix);
            BuildSkillInfo(_rightCol, ref cursorY, iw, ix);
            BuildSkillEffects(_rightCol, ref cursorY, iw, ix);
            BuildTalentTree(_rightCol, ref cursorY, iw, ix);
        }

        private void BuildDemoArea(ContainerControl parent, ref float cursorY, float iw, float ix)
        {
            float demoW = 300f;
            float demoH = 200f;
            float demoX = ix + (iw - demoW) * 0.5f;

            var demoContainer = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(demoX, cursorY),
                Size = new Float2(demoW, demoH),
                BackgroundColor = InkWashTheme.Void,
            };
            parent.AddChild(demoContainer);

            var demoLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(10f, demoH - 22f),
                Size = new Float2(80f, 16f),
                Text = "技能演示",
                TextColor = InkWashTheme.TextSecondary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            demoContainer.AddChild(demoLabel);

            cursorY += demoH + 8f;
        }

        private void BuildSkillInfo(ContainerControl parent, ref float cursorY, float iw, float ix)
        {
            var nameLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(ix, cursorY),
                Size = new Float2(iw * 0.7f, 34f),
                Text = "太极剑法",
                TextColor = InkWashTheme.GoldBright,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 28f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            parent.AddChild(nameLabel);

            float profX = ix + iw - 120f;
            var profLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(profX, cursorY + 2f),
                Size = new Float2(120f, 14f),
                Text = "熟练度",
                TextColor = InkWashTheme.TextTertiary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f),
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
            };
            parent.AddChild(profLabel);

            cursorY += 38f;

            var profValue = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(profX, cursorY - 20f),
                Size = new Float2(120f, 20f),
                Text = "9,860",
                TextColor = InkWashTheme.GoldPrimary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 16f),
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
            };
            parent.AddChild(profValue);

            var profBar = new InkBar
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(profX, cursorY),
                Size = new Float2(120f, 4f),
                Value = 0.92f,
                FillVariant = InkBarFillVariant.Gold,
            };
            parent.AddChild(profBar);

            cursorY += 12f;

            var sectTag = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(ix, cursorY),
                Size = new Float2(64f, 20f),
                Text = "武当派",
                TextColor = InkWashTheme.GoldPrimary,
                BackgroundColor = new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.1f),
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            parent.AddChild(sectTag);

            var typeTag = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(ix + 72f, cursorY),
                Size = new Float2(64f, 20f),
                Text = "主动攻击",
                TextColor = new Color(216f / 255f, 116f / 255f, 112f / 255f, 1f),
                BackgroundColor = new Color(184f / 255f, 84f / 255f, 80f / 255f, 0.1f),
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            parent.AddChild(typeTag);

            var gradeTag = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(ix + 144f, cursorY),
                Size = new Float2(48f, 20f),
                Text = "大师",
                TextColor = InkWashTheme.TextInverse,
                BackgroundColor = InkWashTheme.GoldPrimary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            parent.AddChild(gradeTag);

            cursorY += 28f;

            var descLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(ix, cursorY),
                Size = new Float2(iw, 40f),
                Text = "以柔克刚，借力打力。施展时剑走弧线，化解敌方攻势并反击，太极拳理融于剑术之中，攻守兼备。",
                TextColor = InkWashTheme.TextSecondary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Near,
            };
            parent.AddChild(descLabel);

            cursorY += 48f;
        }

        private void BuildSkillEffects(ContainerControl parent, ref float cursorY, float iw, float ix)
        {
            var effectsTitle = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(ix + 20f, cursorY),
                Size = new Float2(80f, 18f),
                Text = "技能效果",
                TextColor = InkWashTheme.TextDefault,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 12f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            parent.AddChild(effectsTitle);

            float lineX = ix + 20f + 80f + 8f;
            var effectsLine = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(lineX, cursorY + 9f),
                Size = new Float2(iw - (lineX - ix), 1f),
                BackgroundColor = new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.1f),
            };
            parent.AddChild(effectsLine);

            cursorY += 26f;

            float gridGap = 8f;
            float gridW = (iw - gridGap * 3) * 0.25f;

            string[] statNames = { "伤害", "冷却", "消耗", "范围" };
            string[] statValues = { "120%", "8秒", "30内力", "前方扇形" };
            string[] statSubs = { "外功", "中等", "较低", "3米" };

            for (int i = 0; i < 4; i++)
            {
                float sx = ix + i * (gridW + gridGap);

                var statBg = new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(sx, cursorY),
                    Size = new Float2(gridW, 72f),
                    BackgroundColor = new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.04f),
                };
                parent.AddChild(statBg);

                var statName = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(sx, cursorY + 4f),
                    Size = new Float2(gridW, 14f),
                    Text = statNames[i],
                    TextColor = InkWashTheme.TextTertiary,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 9f),
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                };
                parent.AddChild(statName);

                var statValue = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(sx, cursorY + 20f),
                    Size = new Float2(gridW, 22f),
                    Text = statValues[i],
                    TextColor = InkWashTheme.TextDefault,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, i == 3 ? 12f : 15f),
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                };
                parent.AddChild(statValue);

                var statSub = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(sx, cursorY + 44f),
                    Size = new Float2(gridW, 14f),
                    Text = statSubs[i],
                    TextColor = InkWashTheme.TextTertiary,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 8f),
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                };
                parent.AddChild(statSub);
            }

            cursorY += 72f + 8f;
        }

        private void BuildTalentTree(ContainerControl parent, ref float cursorY, float iw, float ix)
        {
            var treeTitle = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(ix + 20f, cursorY),
                Size = new Float2(80f, 18f),
                Text = "天赋树",
                TextColor = InkWashTheme.TextDefault,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 12f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            parent.AddChild(treeTitle);

            var treeCount = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(ix + 100f, cursorY),
                Size = new Float2(100f, 18f),
                Text = "已激活 5/7",
                TextColor = InkWashTheme.TextTertiary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 10f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            parent.AddChild(treeCount);

            float lineX = ix + 200f;
            var treeLine = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(lineX, cursorY + 9f),
                Size = new Float2(iw - (lineX - ix), 1f),
                BackgroundColor = new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.1f),
            };
            parent.AddChild(treeLine);

            cursorY += 26f;

            float treeW = Math.Min(iw, 560f);
            float treeH = 280f;
            float treeX = ix + (iw - treeW) * 0.5f;

            var treeContainer = new TalentTreeContainer
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(treeX, cursorY),
                Size = new Float2(treeW, treeH),
                BackgroundColor = Color.Transparent,
            };
            parent.AddChild(treeContainer);

            BuildTalentNodes(treeContainer, treeW, treeH);

            cursorY += treeH + 12f;

            var descBg = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(ix, cursorY),
                Size = new Float2(iw, 56f),
                BackgroundColor = new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.04f),
            };
            parent.AddChild(descBg);

            var descTitle = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(ix + 12f, cursorY + 6f),
                Size = new Float2(iw - 24f, 18f),
                Text = "四两拨千斤   已激活",
                TextColor = InkWashTheme.GoldPrimary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 12f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            parent.AddChild(descTitle);

            var descBody = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(ix + 12f, cursorY + 26f),
                Size = new Float2(iw - 24f, 26f),
                Text = "受到攻击时，20%概率将敌方劲力反弹，造成相当于自身外功防御50%的伤害。",
                TextColor = InkWashTheme.TextSecondary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Near,
            };
            parent.AddChild(descBody);

            cursorY += 56f + 8f;
        }

        private void BuildTalentNodes(ContainerControl container, float treeW, float treeH)
        {
            float cx = treeW * 0.5f;

            var nodeData = new (float x, float y, string text, string label, bool active, bool isRoot, bool isUltimate)[]
            {
                (cx, 30f, "极", "太极入门", true, true, false),
                (treeW * 0.25f, 100f, "柔", "以柔克刚", true, false, false),
                (cx, 100f, "借", "借力打力", true, false, false),
                (treeW * 0.75f, 100f, "绵", "连绵不绝", true, false, false),
                (cx, 170f, "拨", "四两拨千斤", true, false, false),
                (treeW * 0.82f, 170f, "调", "阴阳调和", false, false, false),
                (cx, 230f, "元", "太极归元", true, false, true),
            };

            foreach (var nd in nodeData)
            {
                float nodeSize = nd.isRoot || nd.isUltimate ? 44f : 40f;
                float nx = nd.x - nodeSize * 0.5f;
                float ny = nd.y - nodeSize * 0.5f;

                var node = new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(nx, ny),
                    Size = new Float2(nodeSize, nodeSize),
                    BackgroundColor = nd.active
                        ? new Color(138f / 255f, 116f / 255f, 56f / 255f, 0.25f)
                        : new Color(0f, 0f, 0f, 0.4f),
                };
                container.AddChild(node);

                var nodeText = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(nx, ny),
                    Size = new Float2(nodeSize, nodeSize),
                    Text = nd.text,
                    TextColor = nd.active ? InkWashTheme.GoldBright : InkWashTheme.TextTertiary,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, nd.isRoot || nd.isUltimate ? 18f : 16f),
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                };
                container.AddChild(nodeText);

                var labelLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(nd.x - 60f, nd.y + nodeSize * 0.5f + 4f),
                    Size = new Float2(120f, 16f),
                    Text = nd.label,
                    TextColor = nd.active ? InkWashTheme.TextSecondary : InkWashTheme.TextTertiary,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f),
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                };
                container.AddChild(labelLabel);
            }
        }

        private void BuildBottomBar()
        {
            _bottomBar = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, MainPanelSize.Y - BottomBarHeight),
                Size = new Float2(MainPanelSize.X, BottomBarHeight),
                BackgroundColor = new Color(
                    InkWashTheme.Void.R, InkWashTheme.Void.G, InkWashTheme.Void.B, 0.5f),
            };
            _mainPanel.AddChild(_bottomBar);

            var borderT = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 0f),
                Size = new Float2(MainPanelSize.X, 1f),
                BackgroundColor = new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.08f),
            };
            _bottomBar.AddChild(borderT);

            _upgradeCostLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(Padding, 0f),
                Size = new Float2(300f, BottomBarHeight),
                Text = "升级需要：秘籍残卷 x5 + 银两 2000",
                TextColor = InkWashTheme.TextTertiary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _bottomBar.AddChild(_upgradeCostLabel);

            float btnY = (BottomBarHeight - 32f) * 0.5f;

            _btnUnequip = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Sm,
                Text = "卸下",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(MainPanelSize.X - Padding - 72f - 8f - 72f - 76f - 8f, btnY),
                Size = new Float2(72f, 32f),
            };
            _btnUnequip.Clicked += () => EmitGoldAtButton(_btnUnequip);
            _bottomBar.AddChild(_btnUnequip);

            _btnEquip = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Sm,
                Text = "装备",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(MainPanelSize.X - Padding - 72f - 76f - 8f, btnY),
                Size = new Float2(72f, 32f),
            };
            _btnEquip.Clicked += () => EmitGoldAtButton(_btnEquip);
            _bottomBar.AddChild(_btnEquip);

            _btnUpgrade = new InkButton
            {
                Variant = InkButtonVariant.Primary,
                ButtonSize = InkButtonSize.Sm,
                Text = "升级",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(MainPanelSize.X - Padding - 76f, btnY),
                Size = new Float2(76f, 32f),
            };
            _btnUpgrade.Clicked += () => EmitGoldAtButton(_btnUpgrade);
            _bottomBar.AddChild(_btnUpgrade);
        }

        private void OnCloseClicked()
        {
            try
            {
                EmitGoldAtButton(_closeBtn);
                NavigationRequested?.Invoke(InkPageDomIds.CombatHud);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[SkillPanelPage] close failed: {ex.Message}");
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
                FlaxEngine.Debug.LogWarning($"[SkillPanelPage] EmitGoldAtButton failed: {ex.Message}");
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
                FlaxEngine.Debug.LogError($"[SkillPanelPage] RefreshLayout failed: {ex.Message}");
            }
        }

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);
        }

        public override void OnParentResized()
        {
            base.OnParentResized();
            RefreshLayout();
        }

        private class TalentTreeContainer : ContainerControl
        {
            public override void Draw()
            {
                var bounds = new Rectangle(0, 0, Width, Height);

                Render2D.DrawRectangle(bounds,
                    new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.08f));

                base.Draw();
            }
        }
    }
}

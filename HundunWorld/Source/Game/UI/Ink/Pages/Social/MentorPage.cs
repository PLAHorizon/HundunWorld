using FlaxEngine;
using FlaxEngine.GUI;
using Game.Character.Attributes;
using HundunWorld.Game.UI.StyleSystem;
using System;
using System.Collections.Generic;

namespace HundunWorld.Game.UI.Ink.Pages.Social
{
    public class MentorPage : ContainerControl, IInkPage
    {
        private const float TopHeaderHeight = 56f;
        private const float ScreenEdge = 16f;
        private const float ColumnGap = 12f;
        private const float LeftColumnWidth = 280f;
        private const float RightColumnWidth = 300f;
        private const float RoleTabHeight = 32f;
        private const float DiscipleItemHeight = 52f;
        private const float DiscipleItemGap = 6f;
        private const float TaskItemHeight = 68f;
        private const float TaskItemGap = 10f;
        private const float ActionBtnHeight = 36f;
        private const float ActionBtnGap = 8f;

        private static Color Gold(float a) => new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, a);
        private static Color Jade(float a) => new Color(InkWashTheme.JadeDeep.R, InkWashTheme.JadeDeep.G, InkWashTheme.JadeDeep.B, a);

        // Top header
        private InkPanel _topHeader;
        private InkButton _backButton;
        private Label _titleLabel;
        private Label _subtitleLabel;
        private Label _mentorLevelLabel;
        private Label _legacyValueLabel;
        private Label _charLevelLabel;

        // Left panel
        private InkPanel _leftPanel;
        private InkButton[] _roleTabs;
        private ContainerControl _masterInfoPanel;
        private Label _masterNameLabel;
        private Label _masterSectLabel;
        private Label _intimacyLabel;
        private InkBar _intimacyBar;
        private Label _masterDaysLabel;
        private ContainerControl _discipleListHost;
        private readonly List<ContainerControl> _discipleItems = new List<ContainerControl>();
        private InkButton _btnRecruit;
        private InkButton _btnSeekMaster;
        private InkButton _btnDismiss;

        // Middle panel
        private InkPanel _middlePanel;
        private InkButton[] _taskTabs;
        private ContainerControl _taskListHost;
        private readonly List<ContainerControl> _taskItems = new List<ContainerControl>();
        private InkPanel _graduationPanel;
        private InkBar _graduationBar;
        private readonly List<Label> _graduationConditionLabels = new List<Label>();

        // Right panel
        private InkPanel _rightPanel;
        private InkBar _levelBar;
        private Label _levelValueLabel;
        private Label _levelTitleLabel;
        private ContainerControl _legacyRecordsHost;
        private readonly List<ContainerControl> _shopItems = new List<ContainerControl>();
        private ContainerControl _rewardPreviewHost;
        private ContainerControl _titlesHost;

        public event Action<string> NavigationRequested;
        public InkParticleSystem ParticleSystem { get; set; }

        private CharacterAttributesComponent _boundCharacter;

        public void BindCharacter(CharacterAttributesComponent component)
        {
            _boundCharacter = component;
        }

        public MentorPage()
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

        // ===================================================================
        // Top Header
        // ===================================================================

        private void BuildTopHeader()
        {
            _topHeader = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(1200f, TopHeaderHeight),
            };

            _backButton = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Sm,
                Text = "←",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(ScreenEdge, 10f),
                Size = new Float2(32f, 32f),
            };
            _backButton.Clicked += () => NavigationRequested?.Invoke(InkPageDomIds.CombatHud);
            _topHeader.AddChild(_backButton);

            _titleLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(56f, 8f),
                Size = new Float2(160f, 28f),
                Text = "师徒传承",
                TextColor = InkWashTheme.TextGold,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 20f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _topHeader.AddChild(_titleLabel);

            _subtitleLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(56f + 160f, 16f),
                Size = new Float2(140f, 20f),
                Text = "MENTOR LINEAGE",
                TextColor = InkWashTheme.TextTertiary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _topHeader.AddChild(_subtitleLabel);

            _mentorLevelLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(560f, 18f),
                Size = new Float2(80f, 20f),
                Text = "师徒 IV",
                TextColor = InkWashTheme.TextSecondary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 13f),
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
            };
            _topHeader.AddChild(_mentorLevelLabel);

            _legacyValueLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(640f, 18f),
                Size = new Float2(100f, 20f),
                Text = "传承 3,820",
                TextColor = InkWashTheme.TextBrand,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 13f),
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
            };
            _topHeader.AddChild(_legacyValueLabel);

            _charLevelLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(740f, 18f),
                Size = new Float2(56f, 20f),
                Text = "Lv.65",
                TextColor = InkWashTheme.TextJade,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 13f),
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
            };
            _topHeader.AddChild(_charLevelLabel);

            AddChild(_topHeader);
        }

        // ===================================================================
        // Left Panel
        // ===================================================================

        private void BuildLeftPanel()
        {
            _leftPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(LeftColumnWidth, 600f),
            };

            BuildRoleSwitcher();
            BuildMasterInfoCard();
            BuildDiscipleList();
            BuildLeftActionButtons();

            AddChild(_leftPanel);
        }

        private void BuildRoleSwitcher()
        {
            var container = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(ScreenEdge, ScreenEdge),
                Size = new Float2(LeftColumnWidth - ScreenEdge * 2f, RoleTabHeight + 8f),
            };

            string[] labels = { "师父", "徒弟" };
            _roleTabs = new InkButton[2];
            float tabWidth = (LeftColumnWidth - ScreenEdge * 2f - 4f) / 2f;
            for (int i = 0; i < 2; i++)
            {
                var btn = new InkButton
                {
                    Variant = i == 0 ? InkButtonVariant.Primary : InkButtonVariant.Ghost,
                    ButtonSize = InkButtonSize.Sm,
                    Text = labels[i],
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(2f + i * (tabWidth + 4f), 4f),
                    Size = new Float2(tabWidth, RoleTabHeight),
                };
                _roleTabs = _roleTabs ?? new InkButton[2];
                _roleTabs[i] = btn;
                container.AddChild(btn);
            }

            _leftPanel.AddChild(container);
        }

        private void BuildMasterInfoCard()
        {
            _masterInfoPanel = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(ScreenEdge, ScreenEdge + RoleTabHeight + 12f + 8f),
                Size = new Float2(LeftColumnWidth - ScreenEdge * 2f, 130f),
            };

            var avatar = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 14f),
                Size = new Float2(56f, 56f),
                BackgroundColor = Gold(0.25f),
            };
            _masterInfoPanel.AddChild(avatar);

            var avatarChar = new Label
            {
                AnchorPreset = AnchorPresets.StretchAll,
                Offsets = Margin.Zero,
                Text = "孤",
                TextColor = InkWashTheme.TextBrand,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 24f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            avatar.AddChild(avatarChar);

            _masterNameLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(80f, 14f),
                Size = new Float2(150f, 22f),
                Text = "孤云长老",
                TextColor = InkWashTheme.TextDefault,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 15f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _masterInfoPanel.AddChild(_masterNameLabel);

            _masterSectLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(80f, 38f),
                Size = new Float2(160f, 18f),
                Text = "少林派 · 武学宗师",
                TextColor = InkWashTheme.TextSecondary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _masterInfoPanel.AddChild(_masterSectLabel);

            _intimacyLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(80f, 60f),
                Size = new Float2(160f, 16f),
                Text = "2,840 / 3,000",
                TextColor = InkWashTheme.TextBrand,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 11f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _masterInfoPanel.AddChild(_intimacyLabel);

            _intimacyBar = new InkBar
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(80f, 56f),
                Size = new Float2(160f, 6f),
                Value = 2840f / 3000f,
                FillVariant = InkBarFillVariant.Gold,
            };
            _masterInfoPanel.AddChild(_intimacyBar);

            _masterDaysLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 80f),
                Size = new Float2(LeftColumnWidth - ScreenEdge * 2f - 24f, 18f),
                Text = "拜师 42 日 · 在师门",
                TextColor = InkWashTheme.TextJade,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _masterInfoPanel.AddChild(_masterDaysLabel);

            _leftPanel.AddChild(_masterInfoPanel);
        }

        private void BuildDiscipleList()
        {
            var listTitle = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(ScreenEdge, ScreenEdge + RoleTabHeight + 12f + 8f + 130f + 8f),
                Size = new Float2(LeftColumnWidth - ScreenEdge * 2f, 20f),
                Text = "门下弟子  2 / 3",
                TextColor = InkWashTheme.TextGold,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _leftPanel.AddChild(listTitle);

            _discipleListHost = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(ScreenEdge, ScreenEdge + RoleTabHeight + 12f + 8f + 130f + 8f + 24f),
                Size = new Float2(LeftColumnWidth - ScreenEdge * 2f, 200f),
            };
            _leftPanel.AddChild(_discipleListHost);

            string[][] disciples = {
                new[] { "林", "林清河", "42", "峨眉", "68" },
                new[] { "慕", "慕晴雪", "38", "唐门", "52" },
            };

            float cursorY = 0f;
            for (int i = 0; i < disciples.Length; i++)
            {
                var d = disciples[i];
                var item = new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(0f, cursorY),
                    Size = new Float2(LeftColumnWidth - ScreenEdge * 2f, DiscipleItemHeight),
                    BackgroundColor = i == 0
                        ? Gold(0.10f)
                        : Color.Transparent,
                };

                var avatarSmall = new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(10f, 6f),
                    Size = new Float2(36f, 36f),
                    BackgroundColor = Jade(0.15f),
                };
                item.AddChild(avatarSmall);

                var avatarChar = new Label
                {
                    AnchorPreset = AnchorPresets.StretchAll,
                    Offsets = Margin.Zero,
                    Text = d[0],
                    TextColor = InkWashTheme.TextJade,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 14f),
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                };
                avatarSmall.AddChild(avatarChar);

                var nameLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(52f, 6f),
                    Size = new Float2(110f, 20f),
                    Text = d[1],
                    TextColor = InkWashTheme.TextDefault,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 13f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                item.AddChild(nameLabel);

                var levelLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(162f, 6f),
                    Size = new Float2(40f, 20f),
                    Text = d[2],
                    TextColor = InkWashTheme.TextGold,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 12f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                item.AddChild(levelLabel);

                var sectLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(52f, 28f),
                    Size = new Float2(60f, 16f),
                    Text = d[3],
                    TextColor = InkWashTheme.TextSecondary,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                item.AddChild(sectLabel);

                var gradBar = new InkBar
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(110f, 30f),
                    Size = new Float2(80f, 4f),
                    Value = int.Parse(d[4]) / 100f,
                    FillVariant = InkBarFillVariant.Gold,
                };
                item.AddChild(gradBar);

                var gradLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(194f, 28f),
                    Size = new Float2(40f, 14f),
                    Text = d[4] + "%",
                    TextColor = InkWashTheme.TextBrand,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 11f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                item.AddChild(gradLabel);

                _discipleItems.Add(item);
                _discipleListHost.AddChild(item);
                cursorY += DiscipleItemHeight + DiscipleItemGap;
            }

            // Empty slot
            var emptySlot = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, cursorY),
                Size = new Float2(LeftColumnWidth - ScreenEdge * 2f, DiscipleItemHeight),
                BackgroundColor = Gold(0.03f),
            };

            var emptyIcon = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(10f, 6f),
                Size = new Float2(36f, 36f),
                Text = "+",
                TextColor = InkWashTheme.TextTertiary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 20f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            emptySlot.AddChild(emptyIcon);

            var emptyLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(52f, 6f),
                Size = new Float2(120f, 20f),
                Text = "虚位以待",
                TextColor = InkWashTheme.TextTertiary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            emptySlot.AddChild(emptyLabel);

            var emptySub = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(52f, 28f),
                Size = new Float2(120f, 16f),
                Text = "可再收 1 名弟子",
                TextColor = InkWashTheme.TextTertiary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            emptySlot.AddChild(emptySub);

            _discipleListHost.AddChild(emptySlot);
        }

        private void BuildLeftActionButtons()
        {
            float yPos = ScreenEdge + RoleTabHeight + 12f + 8f + 130f + 8f + 24f + 200f + 12f;

            _btnRecruit = new InkButton
            {
                Variant = InkButtonVariant.Primary,
                ButtonSize = InkButtonSize.Md,
                Text = "收徒纳贤",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(ScreenEdge, yPos),
                Size = new Float2(LeftColumnWidth - ScreenEdge * 2f, ActionBtnHeight),
            };
            _btnRecruit.Clicked += () => EmitGoldBurstAt(_btnRecruit);
            _leftPanel.AddChild(_btnRecruit);

            _btnSeekMaster = new InkButton
            {
                Variant = InkButtonVariant.Default,
                ButtonSize = InkButtonSize.Md,
                Text = "寻访明师",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(ScreenEdge, yPos + ActionBtnHeight + ActionBtnGap),
                Size = new Float2(LeftColumnWidth - ScreenEdge * 2f, ActionBtnHeight),
            };
            _btnSeekMaster.Clicked += () => EmitGoldBurstAt(_btnSeekMaster);
            _leftPanel.AddChild(_btnSeekMaster);

            _btnDismiss = new InkButton
            {
                Variant = InkButtonVariant.Vermilion,
                ButtonSize = InkButtonSize.Md,
                Text = "解除师徒",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(ScreenEdge, yPos + (ActionBtnHeight + ActionBtnGap) * 2f),
                Size = new Float2(LeftColumnWidth - ScreenEdge * 2f, ActionBtnHeight),
            };
            _btnDismiss.Clicked += () => EmitGoldBurstAt(_btnDismiss);
            _leftPanel.AddChild(_btnDismiss);
        }

        // ===================================================================
        // Middle Panel
        // ===================================================================

        private void BuildMiddlePanel()
        {
            _middlePanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(500f, 600f),
            };

            BuildTaskTabs();
            BuildTaskList();

            AddChild(_middlePanel);
        }

        private void BuildTaskTabs()
        {
            string[] tabLabels = { "日常师徒", "师徒历练", "出师试炼" };
            _taskTabs = new InkButton[3];
            float tabWidth = 120f;
            for (int i = 0; i < 3; i++)
            {
                var btn = new InkButton
                {
                    Variant = i == 0 ? InkButtonVariant.Primary : InkButtonVariant.Ghost,
                    ButtonSize = InkButtonSize.Sm,
                    Text = tabLabels[i],
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(ScreenEdge + i * (tabWidth + 4f), ScreenEdge),
                    Size = new Float2(tabWidth, 32f),
                };
                _taskTabs[i] = btn;
                _middlePanel.AddChild(btn);
            }
        }

        private void BuildTaskList()
        {
            _taskListHost = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(ScreenEdge, ScreenEdge + 40f),
                Size = new Float2(460f, 500f),
            };
            _middlePanel.AddChild(_taskListHost);

            float cursorY = 0f;

            // Daily section label
            var dailySection = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, cursorY),
                Size = new Float2(460f, 20f),
                Text = "日常师徒任务  师徒组队完成，增进情谊",
                TextColor = InkWashTheme.TextGold,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _taskListHost.AddChild(dailySection);
            cursorY += 28f;

            // Daily tasks
            string[][] dailyTasks = {
                new[] { "师徒同心·行侠仗义", "与师父组队击败山贼头目 ×5", "+50 传承 / +10000 经验", "已完成", "5", "5" },
                new[] { "传道授业·武学切磋", "与师父进行武学切磋 3 场", "+50 传承 / +8000 经验", "进行中", "2", "3" },
                new[] { "晨昏定省·采药炼丹", "采集灵草 ×10 并炼制丹药", "+50 传承 / +6000 经验", "未开始", "2", "10" },
                new[] { "游历江湖·名山访胜", "与师门共游名山胜景 ×3 处", "+40 传承 / +5000 经验", "进行中", "1", "3" },
                new[] { "尊师重道·奉茶请安", "向师父奉茶 1 次，以尽弟子之礼", "+30 传承 / +3000 经验", "未开始", "0", "1" },
            };

            for (int i = 0; i < dailyTasks.Length; i++)
            {
                var t = dailyTasks[i];
                var item = BuildTaskItem(t[0], t[1], t[2], t[3], t[4], t[5], cursorY);
                _taskItems.Add(item);
                _taskListHost.AddChild(item);
                cursorY += TaskItemHeight + TaskItemGap;
            }

            // Weekly section label
            cursorY += 4f;
            var weeklySection = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, cursorY),
                Size = new Float2(460f, 20f),
                Text = "师徒历练·周常  每周重置，丰厚传承",
                TextColor = InkWashTheme.TextGold,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _taskListHost.AddChild(weeklySection);
            cursorY += 28f;

            // Weekly tasks
            string[][] weeklyTasks = {
                new[] { "师门试炼·组队通关副本", "与师父组队通关「千机塔」第 3 层", "+200 传承", "进行中", "1", "3" },
                new[] { "传授武学·指点招式", "师父指点弟子武学招式 5 次", "+150 传承", "进行中", "3", "5" },
                new[] { "切磋武艺·师徒论剑", "与同门师兄弟切磋 10 场", "+180 传承", "进行中", "8", "10" },
            };

            for (int i = 0; i < weeklyTasks.Length; i++)
            {
                var t = weeklyTasks[i];
                var item = BuildTaskItem(t[0], t[1], t[2], t[3], t[4], t[5], cursorY);
                _taskItems.Add(item);
                _taskListHost.AddChild(item);
                cursorY += TaskItemHeight + TaskItemGap;
            }

            // Graduation section
            cursorY += 4f;
            BuildGraduationPanel(cursorY);
        }

        private ContainerControl BuildTaskItem(string name, string desc, string reward, string status, string cur, string max, float yPos)
        {
            var item = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, yPos),
                Size = new Float2(460f, TaskItemHeight),
            };

            Color statusColor;
            InkBarFillVariant fillVariant;
            if (status == "已完成")
            {
                statusColor = InkWashTheme.TextJade;
                fillVariant = InkBarFillVariant.Jade;
            }
            else if (status == "进行中")
            {
                statusColor = InkWashTheme.TextGold;
                fillVariant = InkBarFillVariant.Gold;
            }
            else
            {
                statusColor = InkWashTheme.TextTertiary;
                fillVariant = InkBarFillVariant.Gold;
            }

            var nameLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 6f),
                Size = new Float2(240f, 20f),
                Text = name,
                TextColor = InkWashTheme.TextDefault,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            item.AddChild(nameLabel);

            var statusLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(252f, 6f),
                Size = new Float2(56f, 20f),
                Text = status,
                TextColor = statusColor,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            item.AddChild(statusLabel);

            var descLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 26f),
                Size = new Float2(280f, 16f),
                Text = desc,
                TextColor = InkWashTheme.TextSecondary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            item.AddChild(descLabel);

            int curVal = int.Parse(cur);
            int maxVal = int.Parse(max);
            var progressBar = new InkBar
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 44f),
                Size = new Float2(220f, 6f),
                Value = maxVal > 0 ? (float)curVal / maxVal : 0f,
                FillVariant = fillVariant,
            };
            item.AddChild(progressBar);

            var progressLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(236f, 40f),
                Size = new Float2(56f, 14f),
                Text = cur + " / " + max,
                TextColor = statusColor,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 11f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            item.AddChild(progressLabel);

            var rewardLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(310f, 26f),
                Size = new Float2(140f, 32f),
                Text = reward,
                TextColor = InkWashTheme.TextBrand,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 11f),
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
            };
            item.AddChild(rewardLabel);

            return item;
        }

        private void BuildGraduationPanel(float yPos)
        {
            _graduationPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, yPos),
                Size = new Float2(460f, 160f),
            };
            _taskListHost.AddChild(_graduationPanel);

            var gradTitle = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 8f),
                Size = new Float2(300f, 20f),
                Text = "出师试炼  达成条件，独立江湖",
                TextColor = InkWashTheme.TextGold,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _graduationPanel.AddChild(gradTitle);

            var gradCount = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(320f, 8f),
                Size = new Float2(120f, 20f),
                Text = "3 / 5 项达成",
                TextColor = InkWashTheme.TextBrand,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 12f),
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
            };
            _graduationPanel.AddChild(gradCount);

            _graduationBar = new InkBar
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 32f),
                Size = new Float2(436f, 8f),
                Value = 0.6f,
                FillVariant = InkBarFillVariant.Gold,
            };
            _graduationPanel.AddChild(_graduationBar);

            string[][] conditions = {
                new[] { "✓", "师徒亲密度 ≥2000" },
                new[] { "✓", "累计在线 ≥100 时" },
                new[] { "✓", "完成日常 ≥20 次" },
                new[] { "○", "等级达到 Lv.50" },
                new[] { "○", "通关出师试炼副本" },
            };

            for (int i = 0; i < conditions.Length; i++)
            {
                int col = i % 2;
                int row = i / 2;
                bool met = i < 3;
                var condLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(12f + col * 218f, 50f + row * 22f),
                    Size = new Float2(210f, 20f),
                    Text = conditions[i][0] + " " + conditions[i][1],
                    TextColor = met ? InkWashTheme.TextJade : InkWashTheme.TextTertiary,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                _graduationConditionLabels.Add(condLabel);
                _graduationPanel.AddChild(condLabel);
            }
        }

        // ===================================================================
        // Right Panel
        // ===================================================================

        private void BuildRightPanel()
        {
            _rightPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(RightColumnWidth, 600f),
            };

            BuildMentorLevelProgress();
            BuildLegacyRecords();
            BuildMentorShop();
            BuildGraduationRewardPreview();
            BuildMentorTitles();

            AddChild(_rightPanel);
        }

        private void BuildMentorLevelProgress()
        {
            float x = ScreenEdge;
            float w = RightColumnWidth - ScreenEdge * 2f;

            var title = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(x, ScreenEdge),
                Size = new Float2(w, 20f),
                Text = "师徒等级",
                TextColor = InkWashTheme.TextGold,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _rightPanel.AddChild(title);

            _levelTitleLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(x, ScreenEdge + 24f),
                Size = new Float2(w, 24f),
                Text = "IV  名震一方  → V",
                TextColor = InkWashTheme.TextDefault,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 15f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _rightPanel.AddChild(_levelTitleLabel);

            _levelBar = new InkBar
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(x, ScreenEdge + 52f),
                Size = new Float2(w, 8f),
                Value = 3820f / 5000f,
                FillVariant = InkBarFillVariant.Gold,
            };
            _rightPanel.AddChild(_levelBar);

            _levelValueLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(x, ScreenEdge + 64f),
                Size = new Float2(w, 18f),
                Text = "3,820 / 5,000  传承值",
                TextColor = InkWashTheme.TextBrand,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 12f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _rightPanel.AddChild(_levelValueLabel);
        }

        private void BuildLegacyRecords()
        {
            float x = ScreenEdge;
            float w = RightColumnWidth - ScreenEdge * 2f;
            float yPos = ScreenEdge + 90f;

            var title = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(x, yPos),
                Size = new Float2(w, 20f),
                Text = "传承记录",
                TextColor = InkWashTheme.TextGold,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _rightPanel.AddChild(title);

            _legacyRecordsHost = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(x, yPos + 24f),
                Size = new Float2(w, 140f),
            };
            _rightPanel.AddChild(_legacyRecordsHost);

            var records = new (string desc, string value, Color color)[]
            {
                ("完成师徒同心", "+50", InkWashTheme.TextJade),
                ("指点林清河武学", "+30", InkWashTheme.TextBrand),
                ("通关千机塔", "+200", InkWashTheme.TextBrand),
                ("切磋论剑胜出", "+36", InkWashTheme.TextBrand),
                ("签到奖励", "+20", InkWashTheme.TextTertiary),
            };

            float cursorY = 0f;
            for (int i = 0; i < records.Length; i++)
            {
                var r = records[i];
                var item = new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(0f, cursorY),
                    Size = new Float2(w, 24f),
                };

                var dot = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(0f, 4f),
                    Size = new Float2(6f, 6f),
                    Text = "●",
                    TextColor = i == 0 ? InkWashTheme.TextJade : InkWashTheme.TextGold,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 6f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                item.AddChild(dot);

                var descLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(12f, 0f),
                    Size = new Float2(w - 60f, 24f),
                    Text = r.desc,
                    TextColor = InkWashTheme.TextSecondary,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                item.AddChild(descLabel);

                var valueLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(w - 48f, 0f),
                    Size = new Float2(48f, 24f),
                    Text = r.value,
                    TextColor = r.color,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 12f),
                    HorizontalAlignment = TextAlignment.Far,
                    VerticalAlignment = TextAlignment.Center,
                };
                item.AddChild(valueLabel);

                _legacyRecordsHost.AddChild(item);
                cursorY += 24f;
            }
        }

        private void BuildMentorShop()
        {
            float x = ScreenEdge;
            float w = RightColumnWidth - ScreenEdge * 2f;
            float yPos = ScreenEdge + 90f + 24f + 140f + 8f;

            var title = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(x, yPos),
                Size = new Float2(w, 20f),
                Text = "传承商店",
                TextColor = InkWashTheme.TextGold,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _rightPanel.AddChild(title);

            var balanceLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(x, yPos),
                Size = new Float2(w, 20f),
                Text = "3,820 传承",
                TextColor = InkWashTheme.TextBrand,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 12f),
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
            };
            _rightPanel.AddChild(balanceLabel);

            string[][] shopItems = {
                new[] { "传功玉简", "传说", "直接提升武学熟练度", "2,000" },
                new[] { "洗髓丹", "史诗", "重置属性分配", "1,200" },
                new[] { "悟道香", "稀有", "双倍经验 1 小时", "500" },
                new[] { "回春丹", "良品", "恢复全部生命", "200" },
            };

            float cursorY = yPos + 24f;
            for (int i = 0; i < shopItems.Length; i++)
            {
                var s = shopItems[i];
                var item = new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(x, cursorY),
                    Size = new Float2(w, 44f),
                };

                var nameLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(0f, 4f),
                    Size = new Float2(w - 60f, 20f),
                    Text = s[0],
                    TextColor = i == 0 ? InkWashTheme.TextBrand : InkWashTheme.TextDefault,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 13f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                item.AddChild(nameLabel);

                var qualityLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(0f, 24f),
                    Size = new Float2(w - 60f, 16f),
                    Text = s[2],
                    TextColor = InkWashTheme.TextTertiary,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                item.AddChild(qualityLabel);

                var buyBtn = new InkButton
                {
                    Variant = InkButtonVariant.Ghost,
                    ButtonSize = InkButtonSize.Sm,
                    Text = s[3],
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(w - 60f, 8f),
                    Size = new Float2(60f, 28f),
                };
                buyBtn.Clicked += () => EmitGoldBurstAt(buyBtn);
                item.AddChild(buyBtn);

                _shopItems.Add(item);
                _rightPanel.AddChild(item);
                cursorY += 48f;
            }
        }

        private void BuildGraduationRewardPreview()
        {
            float x = ScreenEdge;
            float w = RightColumnWidth - ScreenEdge * 2f;
            float yPos = ScreenEdge + 90f + 24f + 140f + 8f + 24f + 48f * 4 + 8f;

            var title = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(x, yPos),
                Size = new Float2(w, 20f),
                Text = "出师奖励预览",
                TextColor = InkWashTheme.TextGold,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _rightPanel.AddChild(title);

            _rewardPreviewHost = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(x, yPos + 24f),
                Size = new Float2(w, 80f),
            };
            _rightPanel.AddChild(_rewardPreviewHost);

            string[][] rewards = {
                new[] { "绝学秘籍", "scroll" },
                new[] { "洗髓丹×3", "gem" },
                new[] { "金币×5000", "coins" },
                new[] { "出师称号", "award" },
            };

            float cellW = (w - 6f * 3f) / 4f;
            for (int i = 0; i < rewards.Length; i++)
            {
                var cell = new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(i * (cellW + 6f), 0f),
                    Size = new Float2(cellW, 60f),
                };

                var icon = new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2((cellW - 40f) * 0.5f, 0f),
                    Size = new Float2(40f, 40f),
                    BackgroundColor = Gold(0.15f),
                };
                cell.AddChild(icon);

                var label = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(0f, 44f),
                    Size = new Float2(cellW, 16f),
                    Text = rewards[i][0],
                    TextColor = InkWashTheme.TextSecondary,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f),
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                };
                cell.AddChild(label);

                _rewardPreviewHost.AddChild(cell);
            }
        }

        private void BuildMentorTitles()
        {
            float x = ScreenEdge;
            float w = RightColumnWidth - ScreenEdge * 2f;
            float yPos = ScreenEdge + 90f + 24f + 140f + 8f + 24f + 48f * 4 + 8f + 24f + 80f + 8f;

            var title = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(x, yPos),
                Size = new Float2(w, 20f),
                Text = "师徒称号",
                TextColor = InkWashTheme.TextGold,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _rightPanel.AddChild(title);

            _titlesHost = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(x, yPos + 24f),
                Size = new Float2(w, 160f),
            };
            _rightPanel.AddChild(_titlesHost);

            string[][] titles = {
                new[] { "名震一方", "师徒等级 IV 解锁", "已佩戴" },
                new[] { "桃李满门", "收徒 3 人解锁", "佩戴" },
                new[] { "一代宗师", "师徒等级 VI 解锁", "未解锁" },
                new[] { "万世师表", "出师弟子 5 人", "未解锁" },
            };

            float cursorY = 0f;
            for (int i = 0; i < titles.Length; i++)
            {
                var t = titles[i];
                var item = new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(0f, cursorY),
                    Size = new Float2(w, 36f),
                };

                var nameLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(0f, 2f),
                    Size = new Float2(w - 60f, 18f),
                    Text = t[0],
                    TextColor = i == 0 ? InkWashTheme.TextBrand : (i < 2 ? InkWashTheme.TextDefault : InkWashTheme.TextTertiary),
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 13f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                item.AddChild(nameLabel);

                var descLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(0f, 20f),
                    Size = new Float2(w - 60f, 16f),
                    Text = t[1],
                    TextColor = InkWashTheme.TextTertiary,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                item.AddChild(descLabel);

                var statusLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(w - 60f, 8f),
                    Size = new Float2(60f, 20f),
                    Text = t[2],
                    TextColor = i == 0 ? InkWashTheme.TextBrand : (i < 2 ? InkWashTheme.TextJade : InkWashTheme.TextTertiary),
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                    HorizontalAlignment = TextAlignment.Far,
                    VerticalAlignment = TextAlignment.Center,
                };
                item.AddChild(statusLabel);

                _titlesHost.AddChild(item);
                cursorY += 38f;
            }
        }

        // ===================================================================
        // Particle helpers
        // ===================================================================

        private void EmitGoldBurstAt(Control control)
        {
            try
            {
                if (ParticleSystem == null || control == null)
                    return;

                var center = new Float2(control.Width * 0.5f, control.Height * 0.5f);
                var screenPos = control.PointToScreen(center);
                var localPos = ParticleSystem.PointFromScreen(screenPos);
                ParticleSystem.EmitGoldBurst(localPos, count: 14, isLarge: false);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogWarning($"[MentorPage] EmitGoldBurstAt 失败: {ex.Message}");
            }
        }

        // ===================================================================
        // IInkPage
        // ===================================================================

        public void RefreshLayout()
        {
            try
            {
                float sw = Width;
                float sh = Height;
                float contentTop = TopHeaderHeight + ScreenEdge;
                float contentBottom = sh - ScreenEdge;
                float contentH = contentBottom - contentTop;
                float middleWidth = sw - LeftColumnWidth - RightColumnWidth - ColumnGap * 2f - ScreenEdge * 2f;

                if (_topHeader != null)
                {
                    _topHeader.Location = new Float2(ScreenEdge, ScreenEdge);
                    _topHeader.Size = new Float2(sw - ScreenEdge * 2f, TopHeaderHeight);
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

                    if (_taskListHost != null)
                        _taskListHost.Size = new Float2(middleWidth - ScreenEdge * 2f, _taskListHost.Height);
                }

                if (_rightPanel != null)
                {
                    _rightPanel.Location = new Float2(sw - RightColumnWidth - ScreenEdge, contentTop);
                    _rightPanel.Size = new Float2(RightColumnWidth, contentH);
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[MentorPage] RefreshLayout 失败: {ex.Message}");
            }
        }

        public override void OnParentResized()
        {
            base.OnParentResized();
            RefreshLayout();
        }
    }
}

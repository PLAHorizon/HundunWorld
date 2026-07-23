using FlaxEngine;
using FlaxEngine.GUI;
using Game.Character.Attributes;
using HundunWorld.Game.UI.StyleSystem;
using Game.Combat.Skills;
using System;

namespace HundunWorld.Game.UI.Ink.Pages.Character
{
    /// <summary>
    /// 武学心法面板 — 对应设计方案 skill-panel.html。
    /// 1400x900 居中面板：顶部 Tab 栏 + 左列技能列表/秘籍库/槽位 + 右列演示/详情/天赋树 + 底部操作栏。
    /// 严格遵循水墨主题 Token，禁止硬编码色值。
    /// </summary>
    public class SkillPanelPage : ContainerControl, IInkPage
    {
        private static readonly Float2 MainPanelSize = new Float2(1400f, 900f);
        private const float TopBarHeight = 56f;
        private const float BottomBarHeight = 56f;
        private const float Padding = 20f;
        private const float LeftPanelWidth = 400f;

        public event Action<string> NavigationRequested;
        public InkParticleSystem ParticleSystem { get; set; }

        private CharacterAttributesComponent _boundCharacter;

        private InkPanelElevated _mainPanel;

        // 顶栏
        private ContainerControl _topBar;
        private TopTab[] _tabs;
        private ContainerControl _tabActiveLine;
        private InkButton _closeBtn;

        // 左列
        private ContainerControl _leftCol;
        private SkillListItem[] _skillItems;

        // 右列
        private ContainerControl _rightCol;
        private TalentTreeCanvas _talentCanvas;

        // 底栏
        private ContainerControl _bottomBar;
        private InkButton _btnUnequip;
        private InkButton _btnEquip;
        private InkButton _btnUpgrade;

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

        // ===================================================================
        // 顶栏：武学/心法/奇术 Tab + 中央标题 + 关闭按钮
        // ===================================================================

        private void BuildTopBar()
        {
            _topBar = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Float2(MainPanelSize.X, TopBarHeight),
                BackgroundColor = new Color(InkWashTheme.Void.R, InkWashTheme.Void.G, InkWashTheme.Void.B, 0.5f),
            };
            _mainPanel.AddChild(_topBar);

            _topBar.AddChild(new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, TopBarHeight - 1f),
                Size = new Float2(MainPanelSize.X, 1f),
                BackgroundColor = InkWashTheme.BorderGoldSubtle,
            });

            // Tab：武学 / 心法 / 奇术（gap 32）
            string[] tabNames = { "武学", "心法", "奇术" };
            _tabs = new TopTab[tabNames.Length];
            float tabX = Padding;
            for (int i = 0; i < tabNames.Length; i++)
            {
                var tab = new TopTab(tabNames[i], i == 0 ? InkWashTheme.GoldPrimary : InkWashTheme.TextSecondary)
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(tabX, 0f),
                    Size = new Float2(48f, TopBarHeight),
                };
                int captured = i;
                tab.Clicked += () => OnTabClicked(captured);
                _tabs[i] = tab;
                _topBar.AddChild(tab);
                tabX += 48f + 32f;
            }

            // 激活下划线 2px 金色
            _tabActiveLine = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(Padding, TopBarHeight - 2f),
                Size = new Float2(48f, 2f),
                BackgroundColor = InkWashTheme.GoldPrimary,
            };
            _topBar.AddChild(_tabActiveLine);

            // 中央标题：武学心法（两侧 1x24 分隔线）
            float centerX = MainPanelSize.X * 0.5f;
            _topBar.AddChild(new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(centerX - 66f, (TopBarHeight - 24f) * 0.5f),
                Size = new Float2(1f, 24f),
                BackgroundColor = new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.2f),
            });
            _topBar.AddChild(MakeLabel("武学心法", centerX - 55f, 0f, 110f, TopBarHeight,
                InkWashTheme.GoldPrimary, 20f, InkWashTheme.FontRole.Display, TextAlignment.Center));
            _topBar.AddChild(new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(centerX + 66f, (TopBarHeight - 24f) * 0.5f),
                Size = new Float2(1f, 24f),
                BackgroundColor = new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.2f),
            });

            // 关闭按钮 36x36
            _closeBtn = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Sm,
                Text = "✕",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(MainPanelSize.X - Padding - 36f, (TopBarHeight - 36f) * 0.5f),
                Size = new Float2(36f, 36f),
                BorderColor = InkWashTheme.BorderGoldSubtle,
                BorderThickness = 1f,
            };
            _closeBtn.Clicked += OnCloseClicked;
            _topBar.AddChild(_closeBtn);
        }

        // ===================================================================
        // 左列：技能列表 + 秘籍库 + 学习进度 + 心法/奇术槽位 + 联动提示
        // ===================================================================

        private void BuildLeftColumn()
        {
            float contentTop = TopBarHeight;
            float contentH = MainPanelSize.Y - BottomBarHeight - contentTop;

            _leftCol = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, contentTop),
                Size = new Float2(LeftPanelWidth, contentH),
                ClipChildren = true,
            };
            _mainPanel.AddChild(_leftCol);

            // 右边框 1px gold-subtle
            _leftCol.AddChild(new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(LeftPanelWidth - 1f, 0f),
                Size = new Float2(1f, contentH),
                BackgroundColor = InkWashTheme.BorderGoldSubtle,
            });

            float innerW = LeftPanelWidth - 32f;
            float cursorY = 16f;

            BuildSkillGroups(ref cursorY, innerW);
            BuildSecretLibrary(ref cursorY, innerW);

            // 底部固定区：学习进度 + 槽位 + 联动提示（自下而上布局）
            float bottomH = 40f + 186f + 56f;
            float progressY = contentH - bottomH;
            BuildLearningProgress(progressY);
            BuildSlotsArea(progressY + 40f);
        }

        private void BuildSkillGroups(ref float cursorY, float innerW)
        {
            // (group, iconColor, skills[])
            var groups = new (string name, Color iconColor, (string glyph, string name, string grade, string affix,
                              InkWashTheme.InkQuality q, bool selected)[] skills)[]
            {
                ("主动攻击", InkWashTheme.GoldPrimary, new[]
                {
                    ("剑", "太极剑法", "大师", "武当派 · 外功", InkWashTheme.InkQuality.Legendary, true),
                    ("刀", "狂风刀法", "高级", "丐帮 · 外功", InkWashTheme.InkQuality.Rare, false),
                    ("枪", "杨家枪法", "中级", "杨家 · 外功", InkWashTheme.InkQuality.Uncommon, false),
                    ("拳", "降龙掌", "高级", "丐帮 · 刚猛", InkWashTheme.InkQuality.Epic, false),
                }),
                ("被动强化", InkWashTheme.JadePrimary, new[]
                {
                    ("功", "纯阳内功", "高级", "全真教 · 内功", InkWashTheme.InkQuality.Rare, false),
                    ("步", "凌波微步", "中级", "逍遥派 · 轻功", InkWashTheme.InkQuality.Uncommon, false),
                }),
                ("特殊", InkWashTheme.QualityEpic, new[]
                {
                    ("针", "暴雨梨花针", "高级", "唐门 · 暗器", InkWashTheme.InkQuality.Epic, false),
                    ("门", "奇门遁甲", "初级", "奇门 · 阵法", InkWashTheme.InkQuality.Common, false),
                }),
            };

            int totalSkills = 0;
            foreach (var g in groups) totalSkills += g.skills.Length;
            _skillItems = new SkillListItem[totalSkills];
            int idx = 0;

            foreach (var g in groups)
            {
                // 分组标题：图标 + 名称 + 延伸线
                _leftCol.AddChild(MakeLabel("◆", 16f, cursorY, 14f, 18f, g.iconColor,
                    11f, InkWashTheme.FontRole.Body, TextAlignment.Center));
                _leftCol.AddChild(MakeLabel(g.name, 36f, cursorY, 80f, 18f, InkWashTheme.TextDefault,
                    13f, InkWashTheme.FontRole.Display, TextAlignment.Near));
                _leftCol.AddChild(new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(36f + 84f, cursorY + 9f),
                    Size = new Float2(innerW - 84f - 20f, 1f),
                    BackgroundColor = new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.1f),
                });
                cursorY += 26f;

                foreach (var s in g.skills)
                {
                    var item = new SkillListItem(s.glyph, s.name, s.grade, s.affix,
                        InkWashTheme.QualityColor(s.q), s.selected)
                    {
                        Location = new Float2(16f, cursorY),
                        Size = new Float2(innerW, 64f),
                    };
                    int captured = idx;
                    item.Clicked += () => OnSkillClicked(captured);
                    _skillItems[idx] = item;
                    _leftCol.AddChild(item);
                    cursorY += 64f + 6f;
                    idx++;
                }
                cursorY += 10f;
            }
        }

        private void BuildSecretLibrary(ref float cursorY, float innerW)
        {
            _leftCol.AddChild(MakeLabel("◆", 16f, cursorY, 14f, 18f, InkWashTheme.GoldPrimary,
                11f, InkWashTheme.FontRole.Body, TextAlignment.Center));
            _leftCol.AddChild(MakeLabel("秘籍库", 36f, cursorY, 80f, 18f, InkWashTheme.TextDefault,
                13f, InkWashTheme.FontRole.Display, TextAlignment.Near));
            _leftCol.AddChild(new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(36f + 84f, cursorY + 9f),
                Size = new Float2(innerW - 84f - 20f, 1f),
                BackgroundColor = new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.1f),
            });
            cursorY += 26f;

            var books = new (string icon, string name, string val, Color color)[]
            {
                ("◆", "完整秘籍", "8", InkWashTheme.GoldBright),
                ("◇", "残卷", "15", InkWashTheme.QualityUncommon),
                ("○", "心得", "6", InkWashTheme.QualityRare),
                ("◇", "传承玉简", "3", InkWashTheme.QualityEpic),
            };
            float itemW = (innerW - 8f) * 0.5f;
            for (int i = 0; i < books.Length; i++)
            {
                float colX = 16f + (i % 2) * (itemW + 8f);
                float rowY = cursorY + (i / 2) * (44f + 8f);
                var book = new BookItem(books[i].icon, books[i].name, books[i].val, books[i].color)
                {
                    Location = new Float2(colX, rowY),
                    Size = new Float2(itemW, 44f),
                };
                _leftCol.AddChild(book);
            }
            cursorY += 2f * (44f + 8f) + 8f;
        }

        private void BuildLearningProgress(float y)
        {
            var container = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, y),
                Size = new Float2(LeftPanelWidth, 40f),
                BackgroundColor = new Color(InkWashTheme.Void.R, InkWashTheme.Void.G, InkWashTheme.Void.B, 0.3f),
            };
            _leftCol.AddChild(container);

            container.AddChild(new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Float2(LeftPanelWidth, 1f),
                BackgroundColor = InkWashTheme.BorderGoldSubtle,
            });

            container.AddChild(MakeLabel("已学习", 16f, 0f, 60f, 40f,
                InkWashTheme.TextSecondary, 12f, InkWashTheme.FontRole.Display, TextAlignment.Near));

            // 进度条 80x6（金渐变 60%）
            container.AddChild(new InkBar
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(LeftPanelWidth - 16f - 36f - 8f - 80f, (40f - 6f) * 0.5f),
                Size = new Float2(80f, 6f),
                Value = 0.6f,
                FillVariant = InkBarFillVariant.Gold,
            });

            container.AddChild(MakeLabel("12/20", LeftPanelWidth - 16f - 36f, 0f, 36f, 40f,
                InkWashTheme.GoldBright, 14f, InkWashTheme.FontRole.Number, TextAlignment.Far));
        }

        private void BuildSlotsArea(float y)
        {
            var container = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, y),
                Size = new Float2(LeftPanelWidth, 186f + 56f),
                BackgroundColor = new Color(InkWashTheme.Void.R, InkWashTheme.Void.G, InkWashTheme.Void.B, 0.4f),
            };
            _leftCol.AddChild(container);

            container.AddChild(new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Float2(LeftPanelWidth, 1f),
                BackgroundColor = InkWashTheme.BorderGoldSubtle,
            });

            float sy = 12f;

            // 心法槽位 2/4
            container.AddChild(MakeLabel("心法槽位", 16f, sy, 80f, 16f,
                InkWashTheme.TextSecondary, 11f, InkWashTheme.FontRole.Display, TextAlignment.Near));
            container.AddChild(MakeLabel("2/4", LeftPanelWidth - 16f - 40f, sy, 40f, 16f,
                InkWashTheme.TextTertiary, 10f, InkWashTheme.FontRole.Number, TextAlignment.Far));
            sy += 22f;

            string[] xinfa = { "纯", "易", "", "" };
            bool[] xinfaOn = { true, true, false, false };
            for (int i = 0; i < 4; i++)
            {
                container.AddChild(new SkillSlotBox(xinfa[i], xinfaOn[i])
                {
                    Location = new Float2(16f + i * 56f, sy),
                });
            }
            sy += 48f + 12f;

            // 奇术槽位 1/3
            container.AddChild(MakeLabel("奇术槽位", 16f, sy, 80f, 16f,
                InkWashTheme.TextSecondary, 11f, InkWashTheme.FontRole.Display, TextAlignment.Near));
            container.AddChild(MakeLabel("1/3", LeftPanelWidth - 16f - 40f, sy, 40f, 16f,
                InkWashTheme.TextTertiary, 10f, InkWashTheme.FontRole.Number, TextAlignment.Far));
            sy += 22f;

            string[] qishu = { "火", "", "" };
            bool[] qishuOn = { true, false, false };
            for (int i = 0; i < 3; i++)
            {
                container.AddChild(new SkillSlotBox(qishu[i], qishuOn[i])
                {
                    Location = new Float2(16f + i * 56f, sy),
                });
            }
            sy += 48f + 12f;

            // 联动提示（青玉左边框）
            var hint = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, sy),
                Size = new Float2(LeftPanelWidth - 32f, 44f),
                BackgroundColor = new Color(InkWashTheme.JadeDeep.R, InkWashTheme.JadeDeep.G, InkWashTheme.JadeDeep.B, 0.06f),
            };
            hint.AddChild(new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Float2(2f, 44f),
                BackgroundColor = InkWashTheme.JadePrimary,
            });
            hint.AddChild(MakeLabel("⛓", 8f, 2f, 12f, 12f, InkWashTheme.JadePrimary,
                10f, InkWashTheme.FontRole.Body, TextAlignment.Center));
            hint.AddChild(MakeLabel("纯阳内功与太极剑法联动：外功伤害提升15%，内力消耗降低10%",
                24f, 2f, LeftPanelWidth - 32f - 32f, 40f, InkWashTheme.TextJade,
                10f, InkWashTheme.FontRole.Body, TextAlignment.Near));
            container.AddChild(hint);
        }

        // ===================================================================
        // 右列：演示区 + 技能信息 + 技能效果 + 天赋树
        // ===================================================================

        private void BuildRightColumn()
        {
            float contentTop = TopBarHeight;
            float contentH = MainPanelSize.Y - BottomBarHeight - contentTop;
            float rightW = MainPanelSize.X - LeftPanelWidth;

            _rightCol = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(LeftPanelWidth, contentTop),
                Size = new Float2(rightW, contentH),
                ClipChildren = true,
            };
            _mainPanel.AddChild(_rightCol);

            float ix = 20f;
            float iw = rightW - 40f;
            float cursorY = 16f;

            BuildDemoArea(ref cursorY, iw, ix);
            BuildSkillInfo(ref cursorY, iw, ix);
            BuildSkillEffects(ref cursorY, iw, ix);
            BuildTalentTree(ref cursorY, iw, ix);
        }

        private void BuildDemoArea(ref float cursorY, float iw, float ix)
        {
            float demoW = 300f, demoH = 200f;
            float demoX = ix + (iw - demoW) * 0.5f;
            var demo = new DemoArea
            {
                Location = new Float2(demoX, cursorY),
                Size = new Float2(demoW, demoH),
            };
            _rightCol.AddChild(demo);
            cursorY += demoH + 16f;
        }

        private void BuildSkillInfo(ref float cursorY, float iw, float ix)
        {
            // 技能名 28px 金亮
            _rightCol.AddChild(MakeLabel("太极剑法", ix, cursorY, 300f, 36f,
                InkWashTheme.GoldBright, 28f, InkWashTheme.FontRole.Display, TextAlignment.Near));

            // 右侧熟练度
            float profX = ix + iw - 100f;
            _rightCol.AddChild(MakeLabel("熟练度", profX, cursorY, 100f, 14f,
                InkWashTheme.TextTertiary, 10f, InkWashTheme.FontRole.Body, TextAlignment.Far));
            _rightCol.AddChild(MakeLabel("9,860", profX, cursorY + 14f, 100f, 20f,
                InkWashTheme.GoldPrimary, 16f, InkWashTheme.FontRole.Number, TextAlignment.Far));
            _rightCol.AddChild(new InkBar
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(profX, cursorY + 36f),
                Size = new Float2(100f, 4f),
                Value = 0.92f,
                FillVariant = InkBarFillVariant.Gold,
            });

            cursorY += 42f;

            // 标签：武当派 / 主动攻击 / 大师 4/4
            _rightCol.AddChild(new TagBox("武当派", InkWashTheme.GoldPrimary,
                new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.1f))
            {
                Location = new Float2(ix, cursorY),
                Size = new Float2(64f, 20f),
            });
            _rightCol.AddChild(new TagBox("主动攻击", InkWashTheme.BloodBright,
                new Color(InkWashTheme.BloodPrimary.R, InkWashTheme.BloodPrimary.G, InkWashTheme.BloodPrimary.B, 0.1f))
            {
                Location = new Float2(ix + 72f, cursorY),
                Size = new Float2(72f, 20f),
            });
            _rightCol.AddChild(new TagBox("大师 4/4", InkWashTheme.TextInverse, InkWashTheme.GoldPrimary)
            {
                Location = new Float2(ix + 152f, cursorY),
                Size = new Float2(72f, 20f),
            });

            cursorY += 28f;

            // 描述
            _rightCol.AddChild(MakeLabel("以柔克刚，借力打力。施展时剑走弧线，化解敌方攻势并反击，太极拳理融于剑术之中，攻守兼备。",
                ix, cursorY, iw, 42f, InkWashTheme.TextSecondary, 12f, InkWashTheme.FontRole.Body, TextAlignment.Near));
            cursorY += 50f;
        }

        private void BuildSkillEffects(ref float cursorY, float iw, float ix)
        {
            // 标题行
            _rightCol.AddChild(MakeLabel("⚡", ix, cursorY, 13f, 18f, InkWashTheme.GoldPrimary,
                11f, InkWashTheme.FontRole.Body, TextAlignment.Center));
            _rightCol.AddChild(MakeLabel("技能效果", ix + 20f, cursorY, 80f, 18f, InkWashTheme.TextDefault,
                12f, InkWashTheme.FontRole.Display, TextAlignment.Near));
            _rightCol.AddChild(new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(ix + 104f, cursorY + 9f),
                Size = new Float2(iw - 104f, 1f),
                BackgroundColor = new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.1f),
            });
            cursorY += 26f;

            // 4 列属性格
            var stats = new (string icon, Color iconColor, string name, string val, string unit, string sub)[]
            {
                ("⚔", InkWashTheme.BloodBright, "伤害", "120", "%", "外功"),
                ("◷", InkWashTheme.JadePrimary, "冷却", "8", "秒", "中等"),
                ("💧", InkWashTheme.JadeBright, "消耗", "30", "内力", "较低"),
                ("◎", InkWashTheme.GoldPrimary, "范围", "前方扇形", "", "3米"),
            };
            float gap = 8f;
            float gridW = (iw - gap * 3f) * 0.25f;
            for (int i = 0; i < stats.Length; i++)
            {
                var box = new StatBox(stats[i].icon, stats[i].iconColor, stats[i].name, stats[i].val, stats[i].unit, stats[i].sub)
                {
                    Location = new Float2(ix + i * (gridW + gap), cursorY),
                    Size = new Float2(gridW, 76f),
                };
                _rightCol.AddChild(box);
            }
            cursorY += 76f + 12f;
        }

        private void BuildTalentTree(ref float cursorY, float iw, float ix)
        {
            // 标题行：天赋树 + 已激活 5/7
            _rightCol.AddChild(MakeLabel("⑂", ix, cursorY, 13f, 18f, InkWashTheme.GoldPrimary,
                11f, InkWashTheme.FontRole.Body, TextAlignment.Center));
            _rightCol.AddChild(MakeLabel("天赋树", ix + 20f, cursorY, 70f, 18f, InkWashTheme.TextDefault,
                12f, InkWashTheme.FontRole.Display, TextAlignment.Near));
            _rightCol.AddChild(MakeLabel("已激活 5/7", ix + 94f, cursorY, 90f, 18f, InkWashTheme.TextTertiary,
                10f, InkWashTheme.FontRole.Number, TextAlignment.Near));
            _rightCol.AddChild(new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(ix + 188f, cursorY + 9f),
                Size = new Float2(iw - 188f, 1f),
                BackgroundColor = new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.1f),
            });
            cursorY += 28f;

            // 天赋树画布（自绘连线 + 圆形节点）
            float treeW = Math.Min(iw, 560f);
            float treeH = 280f;
            float treeX = ix + (iw - treeW) * 0.5f;
            _talentCanvas = new TalentTreeCanvas
            {
                Location = new Float2(treeX, cursorY),
                Size = new Float2(treeW, treeH),
            };
            _rightCol.AddChild(_talentCanvas);
            cursorY += treeH + 14f;

            // 天赋描述卡
            var desc = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(ix, cursorY),
                Size = new Float2(iw, 64f),
                BackgroundColor = new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.04f),
            };
            desc.AddChild(new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 12f),
                Size = new Float2(8f, 8f),
                BackgroundColor = InkWashTheme.GoldPrimary,
            });
            desc.AddChild(MakeLabel("四两拨千斤", 28f, 6f, 120f, 18f, InkWashTheme.GoldPrimary,
                12f, InkWashTheme.FontRole.Display, TextAlignment.Near));
            desc.AddChild(new TagBox("已激活", InkWashTheme.JadeBright,
                new Color(InkWashTheme.JadeDeep.R, InkWashTheme.JadeDeep.G, InkWashTheme.JadeDeep.B, 0.15f))
            {
                Location = new Float2(152f, 7f),
                Size = new Float2(48f, 16f),
            });
            desc.AddChild(MakeLabel("受到攻击时，20%概率将敌方劲力反弹，造成相当于自身外功防御50%的伤害。与“借力打力”联动时，反弹概率提升至35%。",
                12f, 30f, iw - 24f, 30f, InkWashTheme.TextSecondary, 11f, InkWashTheme.FontRole.Body, TextAlignment.Near));
            _rightCol.AddChild(desc);
            cursorY += 64f + 12f;
        }

        // ===================================================================
        // 底栏：升级消耗 + 卸下/装备/升级
        // ===================================================================

        private void BuildBottomBar()
        {
            _bottomBar = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, MainPanelSize.Y - BottomBarHeight),
                Size = new Float2(MainPanelSize.X, BottomBarHeight),
                BackgroundColor = new Color(InkWashTheme.Void.R, InkWashTheme.Void.G, InkWashTheme.Void.B, 0.5f),
            };
            _mainPanel.AddChild(_bottomBar);

            _bottomBar.AddChild(new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Float2(MainPanelSize.X, 1f),
                BackgroundColor = InkWashTheme.BorderGoldSubtle,
            });

            _bottomBar.AddChild(MakeLabel("升级需要：秘籍残卷 x5 + 银两 2000", Padding, 0f, 320f, BottomBarHeight,
                InkWashTheme.TextTertiary, 11f, InkWashTheme.FontRole.Body, TextAlignment.Near));

            float btnY = (BottomBarHeight - 32f) * 0.5f;
            _btnUnequip = new InkButton
            {
                Variant = InkButtonVariant.Danger,
                ButtonSize = InkButtonSize.Sm,
                Text = "卸下",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(MainPanelSize.X - Padding - 72f - 8f - 72f - 8f - 84f, btnY),
                Size = new Float2(72f, 32f),
            };
            _btnUnequip.Clicked += () => EmitGoldAtButton(_btnUnequip);
            _bottomBar.AddChild(_btnUnequip);

            _btnEquip = new InkButton
            {
                Variant = InkButtonVariant.Secondary,
                ButtonSize = InkButtonSize.Sm,
                Text = "装备",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(MainPanelSize.X - Padding - 72f - 8f - 84f, btnY),
                Size = new Float2(72f, 32f),
            };
            _btnEquip.Clicked += () => EmitGoldAtButton(_btnEquip);
            _bottomBar.AddChild(_btnEquip);

            _btnUpgrade = new InkButton
            {
                Variant = InkButtonVariant.Brand,
                ButtonSize = InkButtonSize.Sm,
                Text = "升级",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(MainPanelSize.X - Padding - 84f, btnY),
                Size = new Float2(84f, 32f),
            };
            _btnUpgrade.Clicked += () => EmitGoldAtButton(_btnUpgrade);
            _bottomBar.AddChild(_btnUpgrade);
        }

        // ===================================================================
        // 事件处理
        // ===================================================================

        private void OnTabClicked(int index)
        {
            if (_tabs == null) return;
            for (int i = 0; i < _tabs.Length; i++)
                _tabs[i].TextColor = i == index ? InkWashTheme.GoldPrimary : InkWashTheme.TextSecondary;
            if (_tabActiveLine != null)
                _tabActiveLine.Location = new Float2(Padding + index * 80f, TopBarHeight - 2f);
        }

        private void OnSkillClicked(int index)
        {
            if (_skillItems == null) return;
            for (int i = 0; i < _skillItems.Length; i++)
                _skillItems[i].IsSelected = (i == index);
            if (index >= 0 && index < _skillItems.Length)
                EmitGoldAtButton(_skillItems[index]);
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
                if (ParticleSystem == null || button == null) return;
                var center = new Float2(button.Width * 0.5f, button.Height * 0.5f);
                var screenPos = button.PointToScreen(center);
                var localPos = ParticleSystem.PointFromScreen(screenPos);
                ParticleSystem.EmitGoldBurst(localPos, count: 12, isLarge: false);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogWarning($"[SkillPanelPage] EmitGoldAtButton failed: {ex.Message}");
            }
        }

        // ===================================================================
        // 布局
        // ===================================================================

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
                    _mainPanel.Location = new Float2(panelX > 0f ? panelX : 0f, panelY > 0f ? panelY : 0f);
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[SkillPanelPage] RefreshLayout failed: {ex.Message}");
            }
        }

        public override void OnParentResized()
        {
            base.OnParentResized();
            RefreshLayout();
        }

        // ===================================================================
        // 辅助方法
        // ===================================================================

        private static Label MakeLabel(string text, float x, float y, float w, float h,
            Color color, float fontSize, InkWashTheme.FontRole role, TextAlignment hAlign)
        {
            return new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(x, y),
                Size = new Float2(w, h),
                Text = text,
                TextColor = color,
                Font = InkRenderHelper.GetFontRef(role, fontSize),
                HorizontalAlignment = hAlign,
                VerticalAlignment = TextAlignment.Center,
                AutoFocus = false,
            };
        }

        // ===================================================================
        // 嵌套控件：技能列表项
        // ===================================================================

        /// <summary>技能列表项：48x48 图标框（品质色边框）+ 名称 + 等级徽章 + 词缀。选中态金边全底。</summary>
        internal class SkillListItem : Control
        {
            private readonly string _glyph;
            private readonly string _name;
            private readonly string _grade;
            private readonly string _affix;
            private readonly Color _qualityColor;
            private bool _isSelected;
            private bool _isHovered;
            private bool _isPressed;

            public event Action Clicked;

            public bool IsSelected
            {
                get => _isSelected;
                set { _isSelected = value; }
            }

            public SkillListItem(string glyph, string name, string grade, string affix, Color qualityColor, bool selected)
            {
                _glyph = glyph;
                _name = name;
                _grade = grade;
                _affix = affix;
                _qualityColor = qualityColor;
                _isSelected = selected;
                AutoFocus = false;
            }

            public override void Draw()
            {
                base.Draw();
                if (!Visible) return;
                var rect = new Rectangle(Float2.Zero, Size);
                Color gold = InkWashTheme.GoldPrimary;

                // 条目背景与边框
                if (_isSelected)
                {
                    InkRenderHelper.FillRoundedRectangle(rect, 4f, new Color(gold.R, gold.G, gold.B, 0.08f));
                    InkRenderHelper.DrawRoundedRectangle(rect, 4f, new Color(gold.R, gold.G, gold.B, 0.25f), 1f);
                }
                else if (_isHovered)
                {
                    InkRenderHelper.FillRoundedRectangle(rect, 4f, new Color(gold.R, gold.G, gold.B, 0.04f));
                    InkRenderHelper.DrawRoundedRectangle(rect, 4f, new Color(gold.R, gold.G, gold.B, 0.1f), 1f);
                }

                // 图标框 48x48
                var iconRect = new Rectangle(8f, (Height - 48f) * 0.5f, 48f, 48f);
                Color iconBorder = _isSelected ? gold : _qualityColor;
                float iconThick = _isSelected ? 2f : 1f;
                Color iconBg = _isSelected
                    ? Color.Lerp(InkWashTheme.BaseDefault, InkWashTheme.GoldDeep, 0.2f)
                    : Color.Lerp(InkWashTheme.BaseDefault, _qualityColor, 0.12f);
                if (_isSelected)
                    InkRenderHelper.FillRoundedRectangle(new Rectangle(iconRect.X - 2f, iconRect.Y - 2f, 52f, 52f), 6f,
                        new Color(gold.R, gold.G, gold.B, 0.2f));
                InkRenderHelper.FillRoundedRectangle(iconRect, 4f, iconBg);
                InkRenderHelper.DrawRoundedRectangle(iconRect, 4f, iconBorder, iconThick);
                var gf = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 24f).GetFont();
                if (gf != null)
                    Render2D.DrawText(gf, _glyph, iconRect, _isSelected ? InkWashTheme.GoldBright : _qualityColor,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);

                // 名称 14px
                float textX = 8f + 48f + 12f;
                var nf = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 14f).GetFont();
                if (nf != null)
                    Render2D.DrawText(nf, _name, new Rectangle(textX, 10f, Width - textX - 70f, 20f),
                        _isSelected ? InkWashTheme.TextDefault : InkWashTheme.TextDefault,
                        TextAlignment.Near, TextAlignment.Center, TextWrapping.NoWrap);

                // 等级徽章
                var badgeRect = new Rectangle(Width - 8f - 56f, 11f, 56f, 18f);
                if (_isSelected)
                {
                    InkRenderHelper.FillRoundedRectangle(badgeRect, 2f, gold);
                    var btf = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 10f).GetFont();
                    if (btf != null)
                        Render2D.DrawText(btf, _grade, badgeRect, InkWashTheme.TextInverse,
                            TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
                }
                else
                {
                    InkRenderHelper.FillRoundedRectangle(badgeRect, 2f, new Color(_qualityColor.R, _qualityColor.G, _qualityColor.B, 0.2f));
                    InkRenderHelper.DrawRoundedRectangle(badgeRect, 2f, _qualityColor, 1f);
                    var btf = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 10f).GetFont();
                    if (btf != null)
                        Render2D.DrawText(btf, _grade, badgeRect, _qualityColor,
                            TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
                }

                // 词缀 11px
                var af = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f).GetFont();
                if (af != null)
                    Render2D.DrawText(af, _affix, new Rectangle(textX, 34f, Width - textX - 8f, 16f),
                        InkWashTheme.TextSecondary, TextAlignment.Near, TextAlignment.Center, TextWrapping.NoWrap);
            }

            public override void OnMouseEnter(Float2 location) { _isHovered = true; base.OnMouseEnter(location); }
            public override void OnMouseLeave() { _isHovered = false; base.OnMouseLeave(); }

            public override bool OnMouseDown(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left) _isPressed = true;
                return base.OnMouseDown(location, button);
            }

            public override bool OnMouseUp(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left && _isPressed)
                {
                    _isPressed = false;
                    if (ContainsPoint(ref location)) Clicked?.Invoke();
                }
                return base.OnMouseUp(location, button);
            }
        }

        // ===================================================================
        // 嵌套控件：秘籍库条目
        // ===================================================================

        /// <summary>秘籍库条目：图标 + 名称 + 数量（金发丝边框）。</summary>
        internal class BookItem : Control
        {
            private readonly string _icon;
            private readonly string _name;
            private readonly string _value;
            private readonly Color _color;

            public BookItem(string icon, string name, string value, Color color)
            {
                _icon = icon;
                _name = name;
                _value = value;
                _color = color;
                AutoFocus = false;
            }

            public override void Draw()
            {
                base.Draw();
                if (!Visible) return;
                var rect = new Rectangle(Float2.Zero, Size);
                InkRenderHelper.FillRoundedRectangle(rect, 4f,
                    new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.04f));
                InkRenderHelper.DrawRoundedRectangle(rect, 4f, InkWashTheme.BorderFaint, 1f);

                var icf = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f).GetFont();
                if (icf != null)
                    Render2D.DrawText(icf, _icon, new Rectangle(8f, (Height - 14f) * 0.5f, 14f, 14f), _color,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
                var nf = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f).GetFont();
                if (nf != null)
                    Render2D.DrawText(nf, _name, new Rectangle(28f, 5f, Width - 36f, 14f), InkWashTheme.TextSecondary,
                        TextAlignment.Near, TextAlignment.Center, TextWrapping.NoWrap);
                var vf = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 13f).GetFont();
                if (vf != null)
                    Render2D.DrawText(vf, _value, new Rectangle(28f, 21f, Width - 36f, 18f), _color,
                        TextAlignment.Near, TextAlignment.Center, TextWrapping.NoWrap);
            }
        }

        // ===================================================================
        // 嵌套控件：心法/奇术槽位
        // ===================================================================

        /// <summary>技能槽位：已装备（金边 + 金底 + 辉光 + 字符）/ 空槽（虚线边框 + 加号）。</summary>
        internal class SkillSlotBox : Control
        {
            private readonly string _glyph;
            private readonly bool _filled;
            private bool _isHovered;

            public SkillSlotBox(string glyph, bool filled)
            {
                _glyph = glyph;
                _filled = filled;
                Size = new Float2(48f, 48f);
                AutoFocus = false;
            }

            public override void Draw()
            {
                base.Draw();
                if (!Visible) return;
                var rect = new Rectangle(Float2.Zero, Size);
                Color gold = InkWashTheme.GoldPrimary;

                if (_filled)
                {
                    if (_isHovered)
                        InkRenderHelper.FillRoundedRectangle(new Rectangle(-2f, -2f, 52f, 52f), 6f,
                            new Color(gold.R, gold.G, gold.B, 0.2f));
                    InkRenderHelper.FillRoundedRectangle(rect, 4f, Color.Lerp(InkWashTheme.BaseDefault, InkWashTheme.GoldDeep, 0.15f));
                    InkRenderHelper.DrawRoundedRectangle(rect, 4f, gold, 1f);
                    var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 22f).GetFont();
                    if (font != null)
                        Render2D.DrawText(font, _glyph, rect, InkWashTheme.GoldBright,
                            TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
                }
                else
                {
                    InkRenderHelper.FillRoundedRectangle(rect, 4f, new Color(0f, 0f, 0f, 0.3f));
                    InkRenderHelper.DrawRoundedRectangle(rect, 4f,
                        new Color(gold.R, gold.G, gold.B, _isHovered ? 0.3f : 0.2f), 1f);
                    var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 14f).GetFont();
                    if (font != null)
                        Render2D.DrawText(font, "+", rect, InkWashTheme.TextTertiary,
                            TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
                }
            }

            public override void OnMouseEnter(Float2 location) { _isHovered = true; base.OnMouseEnter(location); }
            public override void OnMouseLeave() { _isHovered = false; base.OnMouseLeave(); }
        }

        // ===================================================================
        // 嵌套控件：技能效果属性格
        // ===================================================================

        /// <summary>技能效果属性格：图标 + 名称 + 数值 + 补充。</summary>
        internal class StatBox : Control
        {
            private readonly string _icon;
            private readonly Color _iconColor;
            private readonly string _name;
            private readonly string _value;
            private readonly string _unit;
            private readonly string _sub;

            public StatBox(string icon, Color iconColor, string name, string value, string unit, string sub)
            {
                _icon = icon;
                _iconColor = iconColor;
                _name = name;
                _value = value;
                _unit = unit;
                _sub = sub;
                AutoFocus = false;
            }

            public override void Draw()
            {
                base.Draw();
                if (!Visible) return;
                var rect = new Rectangle(Float2.Zero, Size);
                InkRenderHelper.FillRoundedRectangle(rect, 4f,
                    new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.04f));
                InkRenderHelper.DrawRoundedRectangle(rect, 4f, InkWashTheme.BorderFaint, 1f);

                float cx = Width * 0.5f;
                var icf = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 14f).GetFont();
                if (icf != null)
                    Render2D.DrawText(icf, _icon, new Rectangle(cx - 10f, 6f, 20f, 14f), _iconColor,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
                var nf = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f).GetFont();
                if (nf != null)
                    Render2D.DrawText(nf, _name, new Rectangle(0f, 22f, Width, 12f), InkWashTheme.TextTertiary,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
                var vf = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 15f).GetFont();
                if (vf != null)
                    Render2D.DrawText(vf, _value + _unit, new Rectangle(0f, 36f, Width, 20f), InkWashTheme.TextDefault,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
                var sf = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f).GetFont();
                if (sf != null)
                    Render2D.DrawText(sf, _sub, new Rectangle(0f, 58f, Width, 12f), InkWashTheme.TextTertiary,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
            }
        }

        // ===================================================================
        // 嵌套控件：标签框
        // ===================================================================

        /// <summary>小标签：背景色 + 文字色（radius 2）。</summary>
        internal class TagBox : Control
        {
            private readonly string _text;
            private readonly Color _textColor;
            private readonly Color _bgColor;

            public TagBox(string text, Color textColor, Color bgColor)
            {
                _text = text;
                _textColor = textColor;
                _bgColor = bgColor;
                AutoFocus = false;
            }

            public override void Draw()
            {
                base.Draw();
                if (!Visible) return;
                var rect = new Rectangle(Float2.Zero, Size);
                InkRenderHelper.FillRoundedRectangle(rect, 2f, _bgColor);
                var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f).GetFont();
                if (font != null)
                    Render2D.DrawText(font, _text, rect, _textColor,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
            }
        }

        // ===================================================================
        // 嵌套控件：天赋树画布
        // ===================================================================

        /// <summary>天赋树：自绘连线（激活金实线/未激活灰虚线）+ 圆形节点 + 标签。</summary>
        internal class TalentTreeCanvas : Control
        {
            private readonly (float x, float y, string glyph, string label, bool active, bool big, bool ultimate)[] _nodes =
            {
                (0.50f, 52f,  "极", "太极入门",   true,  true,  false),
                (0.25f, 120f, "柔", "以柔克刚",   true,  false, false),
                (0.50f, 120f, "借", "借力打力",   true,  false, false),
                (0.75f, 120f, "绵", "连绵不绝",   true,  false, false),
                (0.50f, 190f, "拨", "四两拨千斤", true,  false, false),
                (0.82f, 190f, "调", "阴阳调和",   false, false, false),
                (0.50f, 252f, "元", "太极归元",   true,  true,  true),
            };

            private readonly (float x1, float y1, float x2, float y2, bool active)[] _lines =
            {
                (280f, 50f,  140f, 120f, true),
                (280f, 50f,  280f, 120f, true),
                (280f, 50f,  420f, 120f, true),
                (140f, 120f, 280f, 190f, true),
                (280f, 120f, 280f, 190f, true),
                (420f, 120f, 280f, 190f, false),
                (280f, 190f, 280f, 250f, true),
                (420f, 120f, 460f, 190f, false),
            };

            public TalentTreeCanvas()
            {
                AutoFocus = false;
            }

            public override void Draw()
            {
                base.Draw();
                if (!Visible) return;
                var rect = new Rectangle(Float2.Zero, Size);
                Color gold = InkWashTheme.GoldPrimary;

                // 背景径向渐变 + 边框
                InkRenderHelper.FillRadialGradient(new Float2(Width * 0.5f, Height * 0.5f), Mathf.Max(Width, Height) * 0.5f,
                    new Color(gold.R, gold.G, gold.B, 0.03f), Color.Transparent);
                InkRenderHelper.DrawRoundedRectangle(rect, 8f, InkWashTheme.BorderFaint, 1f);

                float sx = Width / 560f;
                float sy = Height / 280f;

                // 连线
                foreach (var ln in _lines)
                {
                    var a = new Float2(ln.x1 * sx, ln.y1 * sy);
                    var b = new Float2(ln.x2 * sx, ln.y2 * sy);
                    if (ln.active)
                        Render2D.DrawLine(a, b, new Color(gold.R, gold.G, gold.B, 0.5f), 1.5f);
                    else
                        DrawDashed(a, b, new Color(InkWashTheme.TextTertiary.R, InkWashTheme.TextTertiary.G, InkWashTheme.TextTertiary.B, 0.2f), 1.5f);
                }

                // 节点
                foreach (var nd in _nodes)
                {
                    float size = nd.big ? 44f : 40f;
                    var c = new Float2(nd.x * sx, nd.y * sy);
                    float r = size * 0.5f;

                    if (nd.active)
                    {
                        InkRenderHelper.FillCircle(c, r + 3f, new Color(gold.R, gold.G, gold.B, 0.15f));
                        InkRenderHelper.FillCircle(c, r, Color.Lerp(InkWashTheme.BaseDefault, InkWashTheme.GoldDeep, 0.25f));
                        InkRenderHelper.DrawCircle(c, r, gold, 2f);
                    }
                    else
                    {
                        InkRenderHelper.FillCircle(c, r, new Color(0f, 0f, 0f, 0.4f));
                        InkRenderHelper.DrawCircle(c, r, new Color(gold.R, gold.G, gold.B, 0.15f), 2f);
                    }

                    var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, nd.big ? 18f : 16f).GetFont();
                    if (font != null)
                        Render2D.DrawText(font, nd.glyph, new Rectangle(c.X - r, c.Y - r, size, size),
                            nd.active ? InkWashTheme.GoldBright : InkWashTheme.TextTertiary,
                            TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);

                    // 标签
                    var lf = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f).GetFont();
                    if (lf != null)
                        Render2D.DrawText(lf, nd.label, new Rectangle(c.X - 60f, c.Y + r + 4f, 120f, 14f),
                            nd.ultimate ? InkWashTheme.GoldPrimary : (nd.active ? InkWashTheme.TextSecondary : InkWashTheme.TextTertiary),
                            TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
                }
            }

            private static void DrawDashed(Float2 a, Float2 b, Color color, float thickness)
            {
                var dir = b - a;
                float len = dir.Length;
                if (len <= 0f) return;
                dir /= len;
                const float dash = 4f, gapLen = 4f;
                float t = 0f;
                while (t < len)
                {
                    float end = Mathf.Min(t + dash, len);
                    Render2D.DrawLine(a + dir * t, a + dir * end, color, thickness);
                    t += dash + gapLen;
                }
            }
        }

        // ===================================================================
        // 嵌套控件：技能演示区
        // ===================================================================

        /// <summary>技能演示区：径向渐变 + 剑影 + 太极圆 + 脉冲点 + 播放/暂停按钮。</summary>
        internal class DemoArea : Control
        {
            private float _pulseTime;

            public DemoArea()
            {
                AutoFocus = false;
            }

            public override void Update(float deltaTime)
            {
                base.Update(deltaTime);
                _pulseTime += deltaTime;
            }

            public override void Draw()
            {
                base.Draw();
                if (!Visible) return;
                var rect = new Rectangle(Float2.Zero, Size);
                Color gold = InkWashTheme.GoldPrimary;

                Render2D.FillRectangle(rect, InkWashTheme.Void);
                InkRenderHelper.FillRadialGradient(new Float2(Width * 0.5f, Height * 0.5f), Mathf.Max(Width, Height) * 0.5f,
                    new Color(gold.R, gold.G, gold.B, 0.06f), Color.Transparent);
                InkRenderHelper.DrawRoundedRectangle(rect, 8f, InkWashTheme.BorderGoldSubtle, 1f);

                // 剑影（居中）
                float cx = Width * 0.5f;
                float cy = Height * 0.5f - 10f;
                var bladeTop = new Float2(cx, cy - 45f);
                var bladeR = new Float2(cx + 5f, cy);
                var bladeBot = new Float2(cx, cy + 5f);
                var bladeL = new Float2(cx - 5f, cy);
                Render2D.DrawLine(bladeTop, bladeR, new Color(gold.R, gold.G, gold.B, 0.8f), 1.5f);
                Render2D.DrawLine(bladeR, bladeBot, new Color(gold.R, gold.G, gold.B, 0.8f), 1.5f);
                Render2D.DrawLine(bladeBot, bladeL, new Color(gold.R, gold.G, gold.B, 0.8f), 1.5f);
                Render2D.DrawLine(bladeL, bladeTop, new Color(gold.R, gold.G, gold.B, 0.8f), 1.5f);
                // 剑格
                Render2D.DrawLine(new Float2(cx - 12f, cy + 7f), new Float2(cx + 12f, cy + 7f),
                    new Color(gold.R, gold.G, gold.B, 0.7f), 2f);
                // 剑柄
                Render2D.FillRectangle(new Rectangle(cx - 2f, cy + 9f, 4f, 24f),
                    new Color(InkWashTheme.GoldDeep.R, InkWashTheme.GoldDeep.G, InkWashTheme.GoldDeep.B, 0.6f));
                // 剑镡
                InkRenderHelper.FillCircle(new Float2(cx, cy + 37f), 3f, new Color(gold.R, gold.G, gold.B, 0.7f));
                // 太极圆（虚线金 + 实线青）
                DrawDashedCircle(new Float2(cx, cy), 38f, new Color(gold.R, gold.G, gold.B, 0.2f), 1f);
                InkRenderHelper.DrawCircle(new Float2(cx, cy), 28f,
                    new Color(InkWashTheme.JadePrimary.R, InkWashTheme.JadePrimary.G, InkWashTheme.JadePrimary.B, 0.15f), 1f);

                // 脉冲点 + “技能演示”（左下）
                float pulse = 0.4f + 0.6f * Mathf.Abs(Mathf.Sin(_pulseTime * Mathf.Pi));
                InkRenderHelper.FillCircle(new Float2(13f, Height - 14f), 3f,
                    new Color(gold.R, gold.G, gold.B, pulse));
                var lf = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f).GetFont();
                if (lf != null)
                    Render2D.DrawText(lf, "技能演示", new Rectangle(22f, Height - 22f, 60f, 14f),
                        InkWashTheme.TextSecondary, TextAlignment.Near, TextAlignment.Center, TextWrapping.NoWrap);

                // 播放/暂停按钮（右下）
                DrawControlButton(Width - 10f - 24f - 4f - 24f, Height - 8f - 24f, "▶", gold);
                DrawControlButton(Width - 10f - 24f, Height - 8f - 24f, "❚❚", InkWashTheme.TextSecondary);
            }

            private void DrawControlButton(float x, float y, string glyph, Color glyphColor)
            {
                var r = new Rectangle(x, y, 24f, 24f);
                InkRenderHelper.FillRoundedRectangle(r, 2f,
                    new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.08f));
                InkRenderHelper.DrawRoundedRectangle(r, 2f,
                    new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.15f), 1f);
                var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f).GetFont();
                if (font != null)
                    Render2D.DrawText(font, glyph, r, glyphColor,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
            }

            private static void DrawDashedCircle(Float2 center, float radius, Color color, float thickness)
            {
                const int segments = 24;
                for (int i = 0; i < segments; i += 2)
                {
                    float a0 = i / (float)segments * Mathf.TwoPi;
                    float a1 = (i + 1) / (float)segments * Mathf.TwoPi;
                    var p0 = new Float2(center.X + Mathf.Cos(a0) * radius, center.Y + Mathf.Sin(a0) * radius);
                    var p1 = new Float2(center.X + Mathf.Cos(a1) * radius, center.Y + Mathf.Sin(a1) * radius);
                    Render2D.DrawLine(p0, p1, color, thickness);
                }
            }
        }

        // ===================================================================
        // 嵌套控件：顶部可点击 Tab
        // ===================================================================

        /// <summary>顶栏 Tab（自绘文字 + Clicked 事件，替代 Label 无点击事件的局限）。</summary>
        internal class TopTab : Control
        {
            private string _text;
            private Color _textColor;
            private bool _isPressed;

            public event Action Clicked;

            public string Text { get => _text; set => _text = value; }
            public Color TextColor { get => _textColor; set => _textColor = value; }

            public TopTab(string text, Color color)
            {
                _text = text;
                _textColor = color;
                AutoFocus = false;
            }

            public override void Draw()
            {
                base.Draw();
                if (!Visible) return;
                var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 16f).GetFont();
                if (font != null)
                    Render2D.DrawText(font, _text, new Rectangle(Float2.Zero, Size), _textColor,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
            }

            public override bool OnMouseDown(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left) _isPressed = true;
                return base.OnMouseDown(location, button);
            }

            public override bool OnMouseUp(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left && _isPressed)
                {
                    _isPressed = false;
                    if (ContainsPoint(ref location)) Clicked?.Invoke();
                }
                return base.OnMouseUp(location, button);
            }
        }
    }
}

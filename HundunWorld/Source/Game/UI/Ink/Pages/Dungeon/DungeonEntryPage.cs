using FlaxEngine;
using FlaxEngine.GUI;
using Game.Character.Attributes;
using HundunWorld.Game.UI.StyleSystem;
using System;

namespace HundunWorld.Game.UI.Ink.Pages.Dungeon
{
    /// <summary>
    /// 秘境入口页面 — 对应设计方案 dungeon-entry.html。
    /// 1400x900 居中面板：顶栏（标题 + 总战力/通关数/秘境积分）/ 三栏主体
    /// （左 260 分类列表 / 中 卡片网格 / 右 300 队伍配置 + 进入按钮）。
    /// 严格遵循水墨主题 Token，禁止硬编码色值。
    /// </summary>
    public class DungeonEntryPage : ContainerControl, IInkPage
    {
        private const float PanelW = 1400f;
        private const float PanelH = 900f;
        private const float HeaderH = 80f;
        private const float LeftW = 260f;
        private const float RightW = 300f;
        private const float MidW = PanelW - LeftW - RightW - 2f; // 838
        private const float BodyH = PanelH - HeaderH;            // 820
        private const float CardW = 391f;
        private const float CardH = 264f;

        public event Action<string> NavigationRequested;
        public InkParticleSystem ParticleSystem { get; set; }

        private CharacterAttributesComponent _boundCharacter;

        private InkPanelElevated _mainPanel;
        private ContainerControl _middle;
        private readonly SelectBtn[] _selectBtns = new SelectBtn[3];
        private readonly DBox[] _cardBorders = new DBox[3];
        private readonly DiffButton[] _diffBtns = new DiffButton[4];

        public void BindCharacter(CharacterAttributesComponent component)
        {
            _boundCharacter = component;
        }

        public DungeonEntryPage()
        {
            try
            {
                AnchorPreset = AnchorPresets.StretchAll;
                Offsets = Margin.Zero;
                BackgroundColor = InkWashTheme.Scrim;
                ClipChildren = false;
                AutoFocus = false;

                _mainPanel = new InkPanelElevated
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Size = new Float2(PanelW, PanelH),
                };
                AddChild(_mainPanel);

                BuildHeader();
                BuildBody();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[DungeonEntryPage] init failed: {ex.Message}");
            }
        }

        // ===================================================================
        // 顶栏
        // ===================================================================

        private void BuildHeader()
        {
            var header = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Float2(PanelW, HeaderH),
                BackgroundColor = WithAlpha(InkWashTheme.Void, 0.40f),
                AutoFocus = false,
            };
            _mainPanel.AddChild(header);

            // 标题行
            header.AddChild(MakeLabel("⛰", 24f, 14f, 26f, 26f, InkWashTheme.GoldPrimary, 20f, InkWashTheme.FontRole.Body, TextAlignment.Center));
            header.AddChild(MakeLabel("江湖秘境", 58f, 14f, 140f, 28f, InkWashTheme.GoldPrimary, 22f, InkWashTheme.FontRole.Display, TextAlignment.Near));
            header.AddChild(MakeLabel("DUNDEON ENTRY", 202f, 22f, 110f, 16f, InkWashTheme.TextTertiary, 11f, InkWashTheme.FontRole.Body, TextAlignment.Near));

            // 右侧：时辰 + 返回按钮
            var backBtn = new InkButton
            {
                Variant = InkButtonVariant.Secondary,
                ButtonSize = InkButtonSize.Sm,
                Text = "← 返回",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(PanelW - 24f - 84f, 14f),
                Size = new Float2(84f, 28f),
            };
            backBtn.ButtonClicked += (b) => NavigationRequested?.Invoke(InkPageDomIds.BackHud);
            header.AddChild(backBtn);
            header.AddChild(MakeLabel("辰时三刻", PanelW - 24f - 84f - 16f - 80f, 17f, 80f, 22f,
                InkWashTheme.TextSecondary, 12f, InkWashTheme.FontRole.Number, TextAlignment.Far));
            header.AddChild(MakeLabel("◷", PanelW - 24f - 84f - 16f - 80f - 20f, 17f, 16f, 22f,
                InkWashTheme.TextTertiary, 13f, InkWashTheme.FontRole.Body, TextAlignment.Center));

            // 副信息栏：总战力 / 通关数 / 秘境积分
            float sy = 50f;
            header.AddChild(MakeLabel("⚔", 24f, sy + 1f, 16f, 18f, InkWashTheme.GoldPrimary, 13f, InkWashTheme.FontRole.Body, TextAlignment.Center));
            header.AddChild(MakeLabel("总战力", 44f, sy + 2f, 44f, 16f, InkWashTheme.TextTertiary, 11f, InkWashTheme.FontRole.Body, TextAlignment.Near));
            header.AddChild(MakeLabel("32,450", 92f, sy, 60f, 20f, InkWashTheme.GoldBright, 14f, InkWashTheme.FontRole.Number, TextAlignment.Near));
            header.AddChild(VLine(160f, sy + 3f, 14f, InkWashTheme.BorderGold));
            header.AddChild(MakeLabel("★", 176f, sy + 1f, 16f, 18f, InkWashTheme.JadePrimary, 13f, InkWashTheme.FontRole.Body, TextAlignment.Center));
            header.AddChild(MakeLabel("通关数", 196f, sy + 2f, 44f, 16f, InkWashTheme.TextTertiary, 11f, InkWashTheme.FontRole.Body, TextAlignment.Near));
            header.AddChild(MakeLabel("47", 244f, sy, 34f, 20f, InkWashTheme.JadeBright, 14f, InkWashTheme.FontRole.Number, TextAlignment.Near));
            header.AddChild(VLine(286f, sy + 3f, 14f, InkWashTheme.BorderGold));
            header.AddChild(MakeLabel("◆", 302f, sy + 1f, 16f, 18f, InkWashTheme.BloodPrimary, 13f, InkWashTheme.FontRole.Body, TextAlignment.Center));
            header.AddChild(MakeLabel("秘境积分", 322f, sy + 2f, 56f, 16f, InkWashTheme.TextTertiary, 11f, InkWashTheme.FontRole.Body, TextAlignment.Near));
            header.AddChild(MakeLabel("1,280", 382f, sy, 52f, 20f, InkWashTheme.BloodBright, 14f, InkWashTheme.FontRole.Number, TextAlignment.Near));

            // 底部金边
            header.AddChild(HLine(0f, HeaderH - 1f, PanelW, InkWashTheme.BorderGold));
        }

        // ===================================================================
        // 主体三栏
        // ===================================================================

        private void BuildBody()
        {
            var body = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, HeaderH),
                Size = new Float2(PanelW, BodyH),
                BackgroundColor = InkWashTheme.BorderGold, // 1px 金色分隔
                AutoFocus = false,
            };
            _mainPanel.AddChild(body);

            BuildLeft(body);
            BuildMiddle(body);
            BuildRight(body);
        }

        // ===================================================================
        // 左栏：秘境分类
        // ===================================================================

        private void BuildLeft(ContainerControl body)
        {
            var left = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Float2(LeftW, BodyH),
                BackgroundColor = WithAlpha(InkWashTheme.BaseSecondary, 0.92f),
                AutoFocus = false,
            };
            body.AddChild(left);

            left.AddChild(MakeLabel("秘境分类", 16f, 12f, 150f, 20f, InkWashTheme.TextSecondary, 13f, InkWashTheme.FontRole.Display, TextAlignment.Near));
            left.AddChild(MakeLabel("≡", LeftW - 30f, 12f, 14f, 20f, InkWashTheme.TextTertiary, 14f, InkWashTheme.FontRole.Body, TextAlignment.Center));

            string[] groupNames = { "单人秘境", "组队秘境", "门派秘境", "限时活动" };
            string[] groupIcons = { "◉", "◎", "▣", "✦" };
            string[] groupCounts = { "3", "3", "1", "2" };
            string[][] items =
            {
                new[] { "修行洞府|普通|日 3/3", "试炼塔|困难|日 2/5", "心魔幻境|噩梦|周 1/3" },
                new[] { "幽冥洞|困难|日 2/5", "天劫阵|噩梦|日 1/3", "龙渊秘境|地狱|周 0/2" },
                new[] { "太虚阁|噩梦|周 1/2" },
                new[] { "古墓探秘|双倍|剩2时", "中秋灯会|节日|剩3日" },
            };

            float gy = 40f;
            for (int g = 0; g < groupNames.Length; g++)
            {
                bool groupActive = g == 0;
                bool isEvent = g == 3;

                var gh = new CatGroupHeader(groupIcons[g], groupNames[g], groupCounts[g], groupActive, isEvent)
                {
                    Location = new Float2(8f, gy),
                    Size = new Float2(LeftW - 16f, 32f),
                };
                left.AddChild(gh);
                gy += 32f;

                float iy = gy + 2f;
                for (int i = 0; i < items[g].Length; i++)
                {
                    var parts = items[g][i].Split('|');
                    bool itemActive = g == 0 && i == 0;
                    var ci = new CatItem(parts[0], parts[1], TagColor(parts[1]), parts[2], itemActive, isEvent)
                    {
                        Location = new Float2(8f, iy),
                        Size = new Float2(LeftW - 16f, 48f),
                    };
                    ci.Clicked += () => EmitGoldAtControl(ci);
                    left.AddChild(ci);
                    iy += 50f;
                }
                gy = iy + 6f + 4f;
            }
        }

        // ===================================================================
        // 中栏：秘境卡片
        // ===================================================================

        private void BuildMiddle(ContainerControl body)
        {
            _middle = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(LeftW + 1f, 0f),
                Size = new Float2(MidW, BodyH),
                BackgroundColor = WithAlpha(InkWashTheme.Void, 0.60f),
                AutoFocus = false,
            };
            body.AddChild(_middle);

            // 中栏头
            _middle.AddChild(MakeLabel("单人秘境", 20f, 12f, 100f, 20f, InkWashTheme.GoldPrimary, 14f, InkWashTheme.FontRole.Display, TextAlignment.Near));
            _middle.AddChild(MakeLabel("共 3 处秘境", 126f, 14f, 90f, 18f, InkWashTheme.TextTertiary, 11f, InkWashTheme.FontRole.Body, TextAlignment.Near));

            var filterBtn = new SortBtn("▽ 筛选", false)
            {
                Location = new Float2(MidW - 20f - 64f - 8f - 92f, 7f),
                Size = new Float2(64f, 26f),
            };
            _middle.AddChild(filterBtn);
            var sortBtn = new SortBtn("⇅ 难度排序", true)
            {
                Location = new Float2(MidW - 20f - 92f, 7f),
                Size = new Float2(92f, 26f),
            };
            _middle.AddChild(sortBtn);
            _middle.AddChild(HLine(0f, 36f, MidW, InkWashTheme.BorderFaint));

            // 卡片网格（2 列，gap 16，padding 16 20）
            BuildCard(0, 20f, 52f, "修行洞府", "CULTIVATION CAVE", "普通", InkWashTheme.QualityCommon,
                "12,000", "~8分钟", "隐于深山的修行之地，内有前辈留下的武学残卷，可助修行者参悟心法奥义。",
                new[] { "石", "蛇", "灵" }, new[] { "石傀儡", "玄蛇长老", "洞府守灵" },
                new[] { InkWashTheme.QualityCommon, InkWashTheme.QualityRare, InkWashTheme.QualityRare },
                "普通", InkWashTheme.QualityCommon, "稀有", InkWashTheme.QualityRare,
                "今日", "3", "/3", false, false);

            BuildCard(1, 20f + CardW + 16f, 52f, "试炼塔", "TRIAL PAGODA", "困难", InkWashTheme.QualityRare,
                "25,000", "~15分钟", "七层试炼之塔，每层皆有不同考验，登顶者可获心法秘籍与上古遗宝。",
                new[] { "铁", "影", "塔", "书" }, new[] { "铁掌门人", "幻影剑客", "塔灵", "千面书生" },
                new[] { InkWashTheme.QualityRare, InkWashTheme.QualityRare, InkWashTheme.QualityEpic, InkWashTheme.QualityEpic },
                "稀有", InkWashTheme.QualityRare, "史诗", InkWashTheme.QualityEpic,
                "今日", "2", "/5", true, false);

            BuildCard(2, 20f, 52f + CardH + 16f, "心魔幻境", "INNER DEMON", "噩梦", InkWashTheme.QualityEpic,
                "45,000", "~20分钟", "直面内心深处的魔障，唯有心志坚定者方可破幻而出，超脱凡尘。",
                new[] { "贪", "嗔", "痴", "相" }, new[] { "贪念之魔", "嗔怒之魔", "痴念之魔", "心魔本相" },
                new[] { InkWashTheme.QualityEpic, InkWashTheme.QualityEpic, InkWashTheme.QualityEpic, InkWashTheme.QualityLegendary },
                "史诗", InkWashTheme.QualityEpic, "传说", InkWashTheme.QualityLegendary,
                "本周", "1", "/3", false, true);
        }

        private void BuildCard(int idx, float x, float y, string name, string en, string diffText, Color diffColor,
            string power, string time, string desc,
            string[] bossGlyphs, string[] bossNames, Color[] bossColors,
            string r1Text, Color r1Color, string r2Text, Color r2Color,
            string remainLabel, string remainVal, string remainTotal, bool selected, bool cleared)
        {
            var card = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(x, y),
                Size = new Float2(CardW, CardH),
                BackgroundColor = Color.Transparent,
                AutoFocus = false,
            };
            _middle.AddChild(card);

            // 卡片底 + 边框（选中=金边）
            var border = new DBox(InkWashTheme.BaseTertiary, selected ? InkWashTheme.GoldPrimary : InkWashTheme.BorderFaint, 8f);
            card.AddChild(border);
            _cardBorders[idx] = border;

            // 封面（品质色晕染）
            card.AddChild(new CoverTint(diffColor) { Size = new Float2(CardW, 62f) });
            card.AddChild(HLine(0f, 62f, CardW, InkWashTheme.BorderFaint));

            card.AddChild(MakeLabel(name, 16f, 14f, 240f, 24f, InkWashTheme.TextDefault, 18f, InkWashTheme.FontRole.Display, TextAlignment.Near));
            card.AddChild(MakeLabel(en, 16f, 40f, 240f, 14f, InkWashTheme.TextTertiary, 10f, InkWashTheme.FontRole.Body, TextAlignment.Near));
            card.AddChild(new DiffTag(diffText, diffColor) { Location = new Float2(CardW - 16f - 56f, 14f), Size = new Float2(56f, 22f) });
            if (cleared)
                card.AddChild(new ClearedBadge { Location = new Float2(CardW - 10f - 70f, 10f), Size = new Float2(70f, 20f) });

            // 属性行
            card.AddChild(new StatItem("⚡", InkWashTheme.GoldPrimary, "推荐战力", power, InkWashTheme.GoldBright)
            { Location = new Float2(16f, 73f), Size = new Float2(170f, 18f) });
            card.AddChild(new StatItem("◷", InkWashTheme.JadePrimary, "通关时间", time, InkWashTheme.JadeBright)
            { Location = new Float2(196f, 73f), Size = new Float2(170f, 18f) });

            // 描述
            card.AddChild(MakeLabel(desc, 16f, 97f, CardW - 32f, 18f, InkWashTheme.TextSecondary, 11f, InkWashTheme.FontRole.Body, TextAlignment.Near));

            // 守关首领
            card.AddChild(MakeLabel("守关首领", 16f, 123f, 100f, 16f, InkWashTheme.TextTertiary, 10f, InkWashTheme.FontRole.Body, TextAlignment.Near));
            float bx = 16f;
            for (int i = 0; i < bossNames.Length; i++)
            {
                var pill = new BossPill(bossGlyphs[i], bossNames[i], bossColors[i])
                {
                    Location = new Float2(bx, 143f),
                };
                card.AddChild(pill);
                bx += pill.Width + 6f;
            }

            // 可能掉落
            card.AddChild(MakeLabel("可能掉落", 16f, 173f, 100f, 16f, InkWashTheme.TextTertiary, 10f, InkWashTheme.FontRole.Body, TextAlignment.Near));
            var rt1 = new RewardTag(r1Text, r1Color) { Location = new Float2(16f, 193f) };
            card.AddChild(rt1);
            card.AddChild(MakeLabel("›", 16f + rt1.Width + 5f, 193f, 10f, 18f, InkWashTheme.TextTertiary, 12f, InkWashTheme.FontRole.Body, TextAlignment.Center));
            card.AddChild(new RewardTag(r2Text, r2Color) { Location = new Float2(16f + rt1.Width + 20f, 193f) });

            // 底栏
            card.AddChild(HLine(0f, 221f, CardW, InkWashTheme.BorderFaint));
            card.AddChild(new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 222f),
                Size = new Float2(CardW, CardH - 222f),
                BackgroundColor = WithAlpha(InkWashTheme.Void, 0.30f),
                AutoFocus = false,
            });
            card.AddChild(MakeLabel(remainLabel, 16f, 231f, 40f, 18f, InkWashTheme.TextTertiary, 10f, InkWashTheme.FontRole.Body, TextAlignment.Near));
            card.AddChild(MakeLabel(remainVal, 58f, 228f, 26f, 24f, InkWashTheme.GoldPrimary, 14f, InkWashTheme.FontRole.Number, TextAlignment.Near));
            card.AddChild(MakeLabel(remainTotal, 86f, 231f, 30f, 18f, InkWashTheme.TextTertiary, 11f, InkWashTheme.FontRole.Number, TextAlignment.Near));

            int captured = idx;
            var sel = new SelectBtn(selected) { Location = new Float2(CardW - 16f - 76f, 229f), Size = new Float2(76f, 26f) };
            sel.Clicked += () => OnCardSelected(captured);
            card.AddChild(sel);
            _selectBtns[idx] = sel;
        }

        private void OnCardSelected(int idx)
        {
            for (int i = 0; i < 3; i++)
            {
                _selectBtns[i].SetSelected(i == idx);
                _cardBorders[i].SetBorder(i == idx ? InkWashTheme.GoldPrimary : InkWashTheme.BorderFaint);
            }
            EmitGoldAtControl(_selectBtns[idx]);
        }

        // ===================================================================
        // 右栏：队伍配置
        // ===================================================================

        private void BuildRight(ContainerControl body)
        {
            var right = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(LeftW + 1f + MidW + 1f, 0f),
                Size = new Float2(RightW, BodyH),
                BackgroundColor = WithAlpha(InkWashTheme.BaseSecondary, 0.92f),
                AutoFocus = false,
            };
            body.AddChild(right);

            // 头
            right.AddChild(MakeLabel("队伍配置", 16f, 12f, 150f, 20f, InkWashTheme.TextSecondary, 13f, InkWashTheme.FontRole.Display, TextAlignment.Near));
            var invite = new InviteBtn { Location = new Float2(RightW - 16f - 28f, 6f), Size = new Float2(28f, 28f) };
            invite.Clicked += () => EmitGoldAtControl(invite);
            right.AddChild(invite);
            right.AddChild(HLine(0f, 36f, RightW, InkWashTheme.BorderFaint));

            float cw = RightW - 32f; // 268 内容宽

            // 当前选择
            right.AddChild(new DBox(WithAlpha(InkWashTheme.GoldPrimary, 0.06f), InkWashTheme.BorderGold, 8f)
            { Location = new Float2(16f, 48f), Size = new Float2(cw, 56f) });
            right.AddChild(MakeLabel("当前选择", 28f, 58f, 80f, 16f, InkWashTheme.TextTertiary, 10f, InkWashTheme.FontRole.Body, TextAlignment.Near));
            right.AddChild(new DiffTag("困难", InkWashTheme.QualityRare) { Location = new Float2(16f + cw - 12f - 46f, 56f), Size = new Float2(46f, 18f) });
            right.AddChild(MakeLabel("试炼塔", 28f, 78f, 160f, 22f, InkWashTheme.GoldPrimary, 16f, InkWashTheme.FontRole.Display, TextAlignment.Near));

            // 队伍成员
            right.AddChild(MakeLabel("▸ 队伍成员", 16f, 116f, 120f, 16f, InkWashTheme.TextTertiary, 11f, InkWashTheme.FontRole.Body, TextAlignment.Near));
            right.AddChild(MakeLabel("1/1", 16f + cw - 40f, 116f, 40f, 16f, InkWashTheme.TextSecondary, 11f, InkWashTheme.FontRole.Number, TextAlignment.Far));
            right.AddChild(new DBox(InkWashTheme.BaseTertiary, InkWashTheme.BorderFaint, 8f)
            { Location = new Float2(16f, 138f), Size = new Float2(cw, 48f) });
            right.AddChild(new CircleGlyph(InkWashTheme.GoldPrimary, "游", 32f) { Location = new Float2(26f, 146f) });
            right.AddChild(MakeLabel("游侠", 66f, 144f, 50f, 18f, InkWashTheme.TextDefault, 13f, InkWashTheme.FontRole.Display, TextAlignment.Near));
            right.AddChild(new RolePill("队长") { Location = new Float2(118f, 146f), Size = new Float2(34f, 15f) });
            right.AddChild(MakeLabel("剑客 · 32,450", 66f, 164f, 140f, 16f, InkWashTheme.TextTertiary, 10f, InkWashTheme.FontRole.Number, TextAlignment.Near));

            // 战力对比
            right.AddChild(MakeLabel("▸ 战力对比", 16f, 200f, 120f, 16f, InkWashTheme.TextTertiary, 11f, InkWashTheme.FontRole.Body, TextAlignment.Near));
            right.AddChild(new DBox(InkWashTheme.BaseTertiary, InkWashTheme.BorderFaint, 8f)
            { Location = new Float2(16f, 222f), Size = new Float2(cw, 80f) });
            right.AddChild(MakeLabel("队伍战力", 28f, 232f, 80f, 16f, InkWashTheme.TextTertiary, 10f, InkWashTheme.FontRole.Body, TextAlignment.Near));
            right.AddChild(MakeLabel("32,450", 16f + cw - 12f - 60f, 230f, 60f, 20f, InkWashTheme.GoldBright, 13f, InkWashTheme.FontRole.Number, TextAlignment.Far));
            right.AddChild(new PowerBar(1.0f, 0.77f) { Location = new Float2(28f, 252f), Size = new Float2(cw - 24f, 4f) });
            right.AddChild(MakeLabel("推荐战力", 28f, 262f, 80f, 16f, InkWashTheme.TextTertiary, 10f, InkWashTheme.FontRole.Body, TextAlignment.Near));
            right.AddChild(MakeLabel("25,000", 16f + cw - 12f - 60f, 260f, 60f, 20f, InkWashTheme.TextSecondary, 13f, InkWashTheme.FontRole.Number, TextAlignment.Far));
            right.AddChild(MakeLabel("✓ 战力达标", 28f, 282f, 120f, 16f, InkWashTheme.JadePrimary, 11f, InkWashTheme.FontRole.Body, TextAlignment.Near));

            // 难度选择
            right.AddChild(MakeLabel("▸ 难度选择", 16f, 316f, 120f, 16f, InkWashTheme.TextTertiary, 11f, InkWashTheme.FontRole.Body, TextAlignment.Near));
            string[] diffNames = { "普通", "困难", "噩梦", "地狱" };
            Color[] diffColors = { InkWashTheme.QualityCommon, InkWashTheme.QualityRare, InkWashTheme.QualityEpic, InkWashTheme.QualityLegendary };
            float dw = (cw - 6f) * 0.5f;
            for (int i = 0; i < 4; i++)
            {
                int captured = i;
                var db = new DiffButton(diffNames[i], diffColors[i], i == 1)
                {
                    Location = new Float2(16f + (i % 2) * (dw + 6f), 338f + (i / 2) * 36f),
                    Size = new Float2(dw, 30f),
                };
                db.Clicked += () => OnDiffSelected(captured);
                right.AddChild(db);
                _diffBtns[i] = db;
            }

            // 攻略提示
            right.AddChild(MakeLabel("▸ 攻略提示", 16f, 418f, 120f, 16f, InkWashTheme.TextTertiary, 11f, InkWashTheme.FontRole.Body, TextAlignment.Near));
            right.AddChild(new DBox(WithAlpha(InkWashTheme.JadePrimary, 0.06f), InkWashTheme.BorderJade, 8f)
            { Location = new Float2(16f, 440f), Size = new Float2(cw, 78f) });
            right.AddChild(MakeLabel(
                "第三层塔灵会施展群体封印，建议携带解封符箓。\n千面书生形态切换时有三秒破绽窗口，\n把握时机连招可速通。",
                28f, 448f, cw - 24f, 62f, InkWashTheme.TextSecondary, 11f, InkWashTheme.FontRole.Body, TextAlignment.Near));

            // 最近通关
            right.AddChild(MakeLabel("▸ 最近通关", 16f, 532f, 120f, 16f, InkWashTheme.TextTertiary, 11f, InkWashTheme.FontRole.Body, TextAlignment.Near));
            BuildHistoryRow(right, 554f, "噩", InkWashTheme.QualityEpic, "心魔幻境", "噩梦 · 20分12秒", "07-14", cw);
            BuildHistoryRow(right, 592f, "普", InkWashTheme.QualityCommon, "修行洞府", "普通 · 7分35秒", "07-14", cw);
            BuildHistoryRow(right, 630f, "困", InkWashTheme.QualityRare, "试炼塔", "困难 · 14分08秒", "07-13", cw);

            // 底部：进入按钮
            right.AddChild(HLine(0f, BodyH - 90f, RightW, InkWashTheme.BorderGold));
            right.AddChild(new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, BodyH - 89f),
                Size = new Float2(RightW, 89f),
                BackgroundColor = WithAlpha(InkWashTheme.Void, 0.50f),
                AutoFocus = false,
            });
            var enter = new EnterBtn { Location = new Float2(16f, BodyH - 77f), Size = new Float2(cw, 42f) };
            enter.Clicked += () => { EmitGoldAtControl(enter); NavigationRequested?.Invoke("enter-dungeon"); };
            right.AddChild(enter);
            right.AddChild(MakeLabel("ⓘ 进入后将消耗今日次数", 16f, BodyH - 27f, cw, 14f,
                InkWashTheme.TextTertiary, 10f, InkWashTheme.FontRole.Body, TextAlignment.Center));
        }

        private void BuildHistoryRow(ContainerControl parent, float y, string glyph, Color quality,
            string name, string meta, string time, float cw)
        {
            parent.AddChild(new DBox(InkWashTheme.BaseTertiary, InkWashTheme.BorderFaint, 4f)
            { Location = new Float2(16f, y), Size = new Float2(cw, 34f) });
            parent.AddChild(new SquareGlyph(quality, glyph, 22f) { Location = new Float2(24f, y + 6f) });
            parent.AddChild(MakeLabel(name, 54f, y + 3f, 120f, 16f, InkWashTheme.TextDefault, 12f, InkWashTheme.FontRole.Display, TextAlignment.Near));
            parent.AddChild(MakeLabel(meta, 54f, y + 17f, 140f, 14f, InkWashTheme.TextTertiary, 10f, InkWashTheme.FontRole.Body, TextAlignment.Near));
            parent.AddChild(MakeLabel(time, 16f + cw - 12f - 44f, y + 9f, 44f, 16f, InkWashTheme.TextTertiary, 10f, InkWashTheme.FontRole.Number, TextAlignment.Far));
        }

        private void OnDiffSelected(int idx)
        {
            for (int i = 0; i < 4; i++)
                _diffBtns[i].SetActive(i == idx);
        }

        // ===================================================================
        // 工具
        // ===================================================================

        private static Color TagColor(string tag)
        {
            switch (tag)
            {
                case "普通": return InkWashTheme.QualityCommon;
                case "困难": return InkWashTheme.QualityRare;
                case "噩梦": return InkWashTheme.QualityEpic;
                case "地狱": return InkWashTheme.QualityLegendary;
                default: return InkWashTheme.BloodBright; // 双倍 / 节日（限时活动）
            }
        }

        private static Color WithAlpha(Color c, float a) => new Color(c.R, c.G, c.B, a);

        private static Label MakeLabel(string text, float x, float y, float w, float h, Color color,
            float size, InkWashTheme.FontRole role, TextAlignment hAlign)
        {
            return new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(x, y),
                Size = new Float2(w, h),
                Text = text,
                TextColor = color,
                Font = InkRenderHelper.GetFontRef(role, size),
                HorizontalAlignment = hAlign,
                VerticalAlignment = TextAlignment.Center,
                AutoFocus = false,
            };
        }

        private static ContainerControl HLine(float x, float y, float w, Color c) => new ContainerControl
        {
            AnchorPreset = AnchorPresets.TopLeft,
            Location = new Float2(x, y),
            Size = new Float2(w, 1f),
            BackgroundColor = c,
            AutoFocus = false,
        };

        private static ContainerControl VLine(float x, float y, float h, Color c) => new ContainerControl
        {
            AnchorPreset = AnchorPresets.TopLeft,
            Location = new Float2(x, y),
            Size = new Float2(1f, h),
            BackgroundColor = c,
            AutoFocus = false,
        };

        private void EmitGoldAtControl(Control control)
        {
            if (ParticleSystem == null || control == null) return;
            try
            {
                var center = new Float2(control.Width * 0.5f, control.Height * 0.5f);
                var screenPos = control.PointToScreen(center);
                var localPos = ParticleSystem.PointFromScreen(screenPos);
                ParticleSystem.EmitGoldBurst(localPos, count: 12, isLarge: false);
            }
            catch { }
        }

        public void RefreshLayout()
        {
            if (_mainPanel != null)
            {
                _mainPanel.Location = new Float2(
                    Width * 0.5f - PanelW * 0.5f,
                    Height * 0.5f - PanelH * 0.5f);
            }
        }

        public override void OnParentResized()
        {
            base.OnParentResized();
            RefreshLayout();
        }

        // ===================================================================
        // 嵌套绘制组件
        // ===================================================================

        /// <summary>圆角背景 + 1px 边框（自绘）。</summary>
        private sealed class DBox : Control
        {
            private readonly Color _bg;
            private Color _border;
            private readonly float _radius;

            public DBox(Color bg, Color border, float radius)
            {
                _bg = bg; _border = border; _radius = radius;
                BackgroundColor = Color.Transparent;
                AutoFocus = false;
            }

            public void SetBorder(Color c) { _border = c; }

            public override void Draw()
            {
                if (!Visible) return;
                base.Draw();
                var r = new Rectangle(Float2.Zero, Size);
                if (_bg.A > 0f) InkRenderHelper.FillRoundedRectangle(r, _radius, _bg);
                if (_border.A > 0f) InkRenderHelper.DrawRoundedRectangle(r, _radius, _border, 1f);
            }
        }

        /// <summary>卡片封面品质色晕染（圆角）。</summary>
        private sealed class CoverTint : Control
        {
            private readonly Color _quality;

            public CoverTint(Color quality)
            {
                _quality = quality;
                BackgroundColor = Color.Transparent;
                AutoFocus = false;
            }

            public override void Draw()
            {
                if (!Visible) return;
                base.Draw();
                var r = new Rectangle(Float2.Zero, Size);
                InkRenderHelper.FillRoundedRectangle(r, 8f, Color.Lerp(InkWashTheme.BaseTertiary, _quality, 0.10f));
                InkRenderHelper.FillRoundedRectangle(new Rectangle(0f, 0f, r.Width, r.Height * 0.45f), 8f, Color.Lerp(InkWashTheme.BaseTertiary, _quality, 0.06f));
            }
        }

        /// <summary>左栏分类组头（图标 + 标题 + 计数徽章）。</summary>
        private sealed class CatGroupHeader : Control
        {
            private readonly string _icon, _title, _count;
            private readonly bool _active, _isEvent;

            public CatGroupHeader(string icon, string title, string count, bool active, bool isEvent)
            {
                _icon = icon; _title = title; _count = count; _active = active; _isEvent = isEvent;
                BackgroundColor = Color.Transparent;
                AutoFocus = false;
            }

            public override void Draw()
            {
                if (!Visible) return;
                base.Draw();
                var headColor = _active ? InkWashTheme.GoldPrimary : InkWashTheme.TextTertiary;
                var ifont = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f).GetFont();
                if (ifont != null)
                    Render2D.DrawText(ifont, _icon, new Rectangle(12f, 0f, 16f, Height), headColor,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
                var tfont = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 13f).GetFont();
                if (tfont != null)
                    Render2D.DrawText(tfont, _title, new Rectangle(36f, 0f, 140f, Height), headColor,
                        TextAlignment.Near, TextAlignment.Center, TextWrapping.NoWrap);
                float pw = 26f, ph = 16f;
                var pillRect = new Rectangle(Width - 12f - pw, Height * 0.5f - ph * 0.5f, pw, ph);
                var pillBg = _isEvent ? WithAlpha(InkWashTheme.BloodPrimary, 0.12f) : WithAlpha(InkWashTheme.GoldPrimary, 0.08f);
                InkRenderHelper.FillRoundedRectangle(pillRect, 8f, pillBg);
                var pillText = _isEvent ? InkWashTheme.BloodBright : InkWashTheme.TextTertiary;
                var cfont = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 11f).GetFont();
                if (cfont != null)
                    Render2D.DrawText(cfont, _count, pillRect, pillText,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
            }
        }

        /// <summary>左栏秘境条目（名称 + 难度标签 + 剩余次数，选中态金色左边框）。</summary>
        private sealed class CatItem : Control
        {
            private readonly string _name, _tag, _remain;
            private readonly Color _tagColor;
            private readonly bool _active, _eventRemain;
            private bool _hovered;

            public event Action Clicked;

            public CatItem(string name, string tag, Color tagColor, string remain, bool active, bool eventRemain)
            {
                _name = name; _tag = tag; _tagColor = tagColor; _remain = remain;
                _active = active; _eventRemain = eventRemain;
                BackgroundColor = Color.Transparent;
                AutoFocus = false;
            }

            public override void Draw()
            {
                if (!Visible) return;
                base.Draw();
                var r = new Rectangle(Float2.Zero, Size);
                var bg = _active ? WithAlpha(InkWashTheme.GoldPrimary, 0.10f)
                    : (_hovered ? WithAlpha(InkWashTheme.GoldPrimary, 0.06f) : Color.Transparent);
                if (bg.A > 0f) InkRenderHelper.FillRoundedRectangle(r, 4f, bg);
                if (_active) Render2D.FillRectangle(new Rectangle(0f, 0f, 2f, Height), InkWashTheme.GoldPrimary);
                var nameColor = _active ? InkWashTheme.GoldBright : InkWashTheme.TextDefault;
                var nfont = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 13f).GetFont();
                if (nfont != null)
                    Render2D.DrawText(nfont, _name, new Rectangle(16f, 4f, Width - 26f, 20f), nameColor,
                        TextAlignment.Near, TextAlignment.Center, TextWrapping.NoWrap);
                float tagW = _tag.Length * 9f + 10f;
                var tagRect = new Rectangle(16f, 27f, tagW, 14f);
                InkRenderHelper.FillRoundedRectangle(tagRect, 4f, WithAlpha(_tagColor, 0.12f));
                var tfont = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 9f).GetFont();
                if (tfont != null)
                    Render2D.DrawText(tfont, _tag, tagRect, _tagColor,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
                var remainColor = _eventRemain ? InkWashTheme.BloodBright : InkWashTheme.TextTertiary;
                var rfont = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 10f).GetFont();
                if (rfont != null)
                    Render2D.DrawText(rfont, _remain, new Rectangle(16f + tagW + 6f, 27f, 80f, 14f), remainColor,
                        TextAlignment.Near, TextAlignment.Center, TextWrapping.NoWrap);
            }

            public override void OnMouseEnter(Float2 location) { _hovered = true; base.OnMouseEnter(location); }
            public override void OnMouseLeave() { _hovered = false; base.OnMouseLeave(); }

            public override bool OnMouseUp(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left && ContainsPoint(ref location)) Clicked?.Invoke();
                return base.OnMouseUp(location, button);
            }
        }

        /// <summary>中栏头部排序/筛选按钮。</summary>
        private sealed class SortBtn : Control
        {
            private readonly string _text;
            private readonly bool _active;
            private bool _hovered;

            public SortBtn(string text, bool active)
            {
                _text = text; _active = active;
                BackgroundColor = Color.Transparent;
                AutoFocus = false;
            }

            public override void Draw()
            {
                if (!Visible) return;
                base.Draw();
                var r = new Rectangle(Float2.Zero, Size);
                var bg = _active ? WithAlpha(InkWashTheme.GoldPrimary, 0.08f) : Color.Transparent;
                if (bg.A > 0f) InkRenderHelper.FillRoundedRectangle(r, 4f, bg);
                var border = (_active || _hovered) ? InkWashTheme.BorderGold : InkWashTheme.BorderFaint;
                InkRenderHelper.DrawRoundedRectangle(r, 4f, border, 1f);
                var textColor = _active ? InkWashTheme.GoldPrimary : (_hovered ? InkWashTheme.TextSecondary : InkWashTheme.TextTertiary);
                var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f).GetFont();
                if (font != null)
                    Render2D.DrawText(font, _text, r, textColor,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
            }

            public override void OnMouseEnter(Float2 location) { _hovered = true; base.OnMouseEnter(location); }
            public override void OnMouseLeave() { _hovered = false; base.OnMouseLeave(); }
        }

        /// <summary>难度标签（品质色文字 + 品质色底）。</summary>
        private sealed class DiffTag : Control
        {
            private readonly string _text;
            private readonly Color _quality;

            public DiffTag(string text, Color quality)
            {
                _text = text; _quality = quality;
                BackgroundColor = Color.Transparent;
                AutoFocus = false;
            }

            public override void Draw()
            {
                if (!Visible) return;
                base.Draw();
                var r = new Rectangle(Float2.Zero, Size);
                InkRenderHelper.FillRoundedRectangle(r, 4f, WithAlpha(_quality, 0.12f));
                var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 11f).GetFont();
                if (font != null)
                    Render2D.DrawText(font, _text, r, _quality,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
            }
        }

        /// <summary>已通关徽章（金色胶囊）。</summary>
        private sealed class ClearedBadge : Control
        {
            public ClearedBadge() { BackgroundColor = Color.Transparent; AutoFocus = false; }

            public override void Draw()
            {
                if (!Visible) return;
                base.Draw();
                var r = new Rectangle(Float2.Zero, Size);
                InkRenderHelper.FillRoundedRectangle(r, 10f, WithAlpha(InkWashTheme.GoldPrimary, 0.16f));
                InkRenderHelper.DrawRoundedRectangle(r, 10f, InkWashTheme.BorderGold, 1f);
                var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 10f).GetFont();
                if (font != null)
                    Render2D.DrawText(font, "✓ 已通关", r, InkWashTheme.GoldBright,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
            }
        }

        /// <summary>卡片属性项（图标 + 标签 + 数值）。</summary>
        private sealed class StatItem : Control
        {
            private readonly string _icon, _label, _value;
            private readonly Color _iconColor, _valueColor;

            public StatItem(string icon, Color iconColor, string label, string value, Color valueColor)
            {
                _icon = icon; _iconColor = iconColor; _label = label; _value = value; _valueColor = valueColor;
                BackgroundColor = Color.Transparent;
                AutoFocus = false;
            }

            public override void Draw()
            {
                if (!Visible) return;
                base.Draw();
                var ifont = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f).GetFont();
                if (ifont != null)
                    Render2D.DrawText(ifont, _icon, new Rectangle(0f, 0f, 14f, Height), _iconColor,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
                var lfont = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f).GetFont();
                if (lfont != null)
                    Render2D.DrawText(lfont, _label, new Rectangle(17f, 0f, 52f, Height), InkWashTheme.TextTertiary,
                        TextAlignment.Near, TextAlignment.Center, TextWrapping.NoWrap);
                var vfont = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 13f).GetFont();
                if (vfont != null)
                    Render2D.DrawText(vfont, _value, new Rectangle(72f, 0f, 90f, Height), _valueColor,
                        TextAlignment.Near, TextAlignment.Center, TextWrapping.NoWrap);
            }
        }

        /// <summary>守关首领胶囊（品质色头像 + 名称）。</summary>
        private sealed class BossPill : Control
        {
            private readonly string _glyph, _name;
            private readonly Color _quality;

            public BossPill(string glyph, string name, Color quality)
            {
                _glyph = glyph; _name = name; _quality = quality;
                Size = new Float2(24f + name.Length * 11f + 10f, 22f);
                BackgroundColor = Color.Transparent;
                AutoFocus = false;
            }

            public override void Draw()
            {
                if (!Visible) return;
                base.Draw();
                var r = new Rectangle(Float2.Zero, Size);
                InkRenderHelper.FillRoundedRectangle(r, 11f, InkWashTheme.BaseElevated);
                InkRenderHelper.DrawRoundedRectangle(r, 11f, WithAlpha(_quality, 0.25f), 1f);
                InkRenderHelper.FillCircle(new Float2(11f, Height * 0.5f), 9f, _quality);
                var gfont = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 10f).GetFont();
                if (gfont != null)
                    Render2D.DrawText(gfont, _glyph, new Rectangle(2f, 0f, 18f, Height), InkWashTheme.TextInverse,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
                var nfont = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f).GetFont();
                if (nfont != null)
                    Render2D.DrawText(nfont, _name, new Rectangle(24f, 0f, Width - 32f, Height), InkWashTheme.TextSecondary,
                        TextAlignment.Near, TextAlignment.Center, TextWrapping.NoWrap);
            }
        }

        /// <summary>掉落品质标签。</summary>
        private sealed class RewardTag : Control
        {
            private readonly string _text;
            private readonly Color _quality;

            public RewardTag(string text, Color quality)
            {
                _text = text; _quality = quality;
                Size = new Float2(text.Length * 10f + 12f, 18f);
                BackgroundColor = Color.Transparent;
                AutoFocus = false;
            }

            public override void Draw()
            {
                if (!Visible) return;
                base.Draw();
                var r = new Rectangle(Float2.Zero, Size);
                InkRenderHelper.FillRoundedRectangle(r, 4f, WithAlpha(_quality, 0.10f));
                var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f).GetFont();
                if (font != null)
                    Render2D.DrawText(font, _text, r, _quality,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
            }
        }

        /// <summary>卡片选择按钮（未选金框 / 选中金底反色）。</summary>
        private sealed class SelectBtn : Control
        {
            private bool _selected;
            private bool _hovered;

            public event Action Clicked;

            public SelectBtn(bool selected)
            {
                _selected = selected;
                BackgroundColor = Color.Transparent;
                AutoFocus = false;
            }

            public void SetSelected(bool v) { _selected = v; }

            public override void Draw()
            {
                if (!Visible) return;
                base.Draw();
                var r = new Rectangle(Float2.Zero, Size);
                if (_selected)
                {
                    InkRenderHelper.FillRoundedRectangle(r, 4f, _hovered ? InkWashTheme.GoldBright : InkWashTheme.GoldPrimary);
                    var fSel = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 12f).GetFont();
                    if (fSel != null)
                        Render2D.DrawText(fSel, "✓ 已选", r, InkWashTheme.TextInverse,
                            TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
                }
                else
                {
                    InkRenderHelper.FillRoundedRectangle(r, 4f, WithAlpha(InkWashTheme.GoldPrimary, _hovered ? 0.16f : 0.08f));
                    InkRenderHelper.DrawRoundedRectangle(r, 4f, InkWashTheme.BorderGold, 1f);
                    var fNor = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 12f).GetFont();
                    if (fNor != null)
                        Render2D.DrawText(fNor, "选择", r, _hovered ? InkWashTheme.GoldBright : InkWashTheme.GoldPrimary,
                            TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
                }
            }

            public override void OnMouseEnter(Float2 location) { _hovered = true; base.OnMouseEnter(location); }
            public override void OnMouseLeave() { _hovered = false; base.OnMouseLeave(); }

            public override bool OnMouseUp(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left && ContainsPoint(ref location)) Clicked?.Invoke();
                return base.OnMouseUp(location, button);
            }
        }

        /// <summary>右栏难度选择按钮（品质色圆点，选中态品质色边框）。</summary>
        private sealed class DiffButton : Control
        {
            private readonly string _text;
            private readonly Color _quality;
            private bool _active;
            private bool _hovered;

            public event Action Clicked;

            public DiffButton(string text, Color quality, bool active)
            {
                _text = text; _quality = quality; _active = active;
                BackgroundColor = Color.Transparent;
                AutoFocus = false;
            }

            public void SetActive(bool v) { _active = v; }

            public override void Draw()
            {
                if (!Visible) return;
                base.Draw();
                var r = new Rectangle(Float2.Zero, Size);
                if (_active) InkRenderHelper.FillRoundedRectangle(r, 4f, WithAlpha(_quality, 0.10f));
                var border = _active ? _quality : (_hovered ? InkWashTheme.BorderGold : InkWashTheme.BorderFaint);
                InkRenderHelper.DrawRoundedRectangle(r, 4f, border, 1f);
                InkRenderHelper.FillCircle(new Float2(14f, Height * 0.5f), 3f, _quality);
                var textColor = _active ? InkWashTheme.TextDefault : InkWashTheme.TextSecondary;
                var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 12f).GetFont();
                if (font != null)
                    Render2D.DrawText(font, _text, new Rectangle(24f, 0f, Width - 32f, Height), textColor,
                        TextAlignment.Near, TextAlignment.Center, TextWrapping.NoWrap);
            }

            public override void OnMouseEnter(Float2 location) { _hovered = true; base.OnMouseEnter(location); }
            public override void OnMouseLeave() { _hovered = false; base.OnMouseLeave(); }

            public override bool OnMouseUp(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left && ContainsPoint(ref location)) Clicked?.Invoke();
                return base.OnMouseUp(location, button);
            }
        }

        /// <summary>圆形头像（品质/金色底 + 单字）。</summary>
        private sealed class CircleGlyph : Control
        {
            private readonly Color _bg;
            private readonly string _glyph;

            public CircleGlyph(Color bg, string glyph, float size)
            {
                _bg = bg; _glyph = glyph;
                Size = new Float2(size, size);
                BackgroundColor = Color.Transparent;
                AutoFocus = false;
            }

            public override void Draw()
            {
                if (!Visible) return;
                base.Draw();
                var c = new Float2(Width * 0.5f, Height * 0.5f);
                InkRenderHelper.FillCircle(c, Width * 0.5f, _bg);
                var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, Width * 0.44f).GetFont();
                if (font != null)
                    Render2D.DrawText(font, _glyph, new Rectangle(Float2.Zero, Size), InkWashTheme.TextInverse,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
            }
        }

        /// <summary>方形圆角图标（品质色底 + 单字，用于通关历史）。</summary>
        private sealed class SquareGlyph : Control
        {
            private readonly Color _bg;
            private readonly string _glyph;

            public SquareGlyph(Color bg, string glyph, float size)
            {
                _bg = bg; _glyph = glyph;
                Size = new Float2(size, size);
                BackgroundColor = Color.Transparent;
                AutoFocus = false;
            }

            public override void Draw()
            {
                if (!Visible) return;
                base.Draw();
                InkRenderHelper.FillRoundedRectangle(new Rectangle(Float2.Zero, Size), 4f, _bg);
                var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, Width * 0.45f).GetFont();
                if (font != null)
                    Render2D.DrawText(font, _glyph, new Rectangle(Float2.Zero, Size), InkWashTheme.TextInverse,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
            }
        }

        /// <summary>职务胶囊（队长）。</summary>
        private sealed class RolePill : Control
        {
            private readonly string _text;

            public RolePill(string text)
            {
                _text = text;
                BackgroundColor = Color.Transparent;
                AutoFocus = false;
            }

            public override void Draw()
            {
                if (!Visible) return;
                base.Draw();
                var r = new Rectangle(Float2.Zero, Size);
                InkRenderHelper.FillRoundedRectangle(r, 4f, WithAlpha(InkWashTheme.GoldPrimary, 0.16f));
                var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 9f).GetFont();
                if (font != null)
                    Render2D.DrawText(font, _text, r, InkWashTheme.GoldBright,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
            }
        }

        /// <summary>战力对比条（金色渐变填充 + 推荐线标记）。</summary>
        private sealed class PowerBar : Control
        {
            private readonly float _fill;
            private readonly float _marker;

            public PowerBar(float fill, float marker)
            {
                _fill = Mathf.Clamp(fill, 0f, 1f);
                _marker = Mathf.Clamp(marker, 0f, 1f);
                BackgroundColor = Color.Transparent;
                AutoFocus = false;
            }

            public override void Draw()
            {
                if (!Visible) return;
                base.Draw();
                var r = new Rectangle(Float2.Zero, Size);
                InkRenderHelper.FillRoundedRectangle(r, 2f, WithAlpha(InkWashTheme.GoldPrimary, 0.12f));
                int steps = 8;
                float fw = r.Width * _fill;
                for (int i = 0; i < steps; i++)
                {
                    float t0 = (float)i / steps;
                    float x0 = fw * t0;
                    float x1 = fw * ((float)(i + 1) / steps);
                    var col = Color.Lerp(InkWashTheme.GoldPrimary, InkWashTheme.GoldBright, t0);
                    Render2D.FillRectangle(new Rectangle(x0, 0f, x1 - x0 + 0.5f, r.Height), col);
                }
                float mx = r.Width * _marker;
                Render2D.FillRectangle(new Rectangle(mx - 1f, -3f, 2f, r.Height + 6f), InkWashTheme.TextTertiary);
            }
        }

        /// <summary>进入秘境按钮（金色渐变 + 亮金边框）。</summary>
        private sealed class EnterBtn : Control
        {
            private bool _hovered;

            public event Action Clicked;

            public EnterBtn() { BackgroundColor = Color.Transparent; AutoFocus = false; }

            public override void Draw()
            {
                if (!Visible) return;
                base.Draw();
                var r = new Rectangle(Float2.Zero, Size);
                if (_hovered)
                    InkRenderHelper.FillRoundedRectangle(new Rectangle(r.X - 2f, r.Y - 2f, r.Width + 4f, r.Height + 4f), 10f, WithAlpha(InkWashTheme.GoldPrimary, 0.20f));
                int steps = 10;
                for (int i = 0; i < steps; i++)
                {
                    float t0 = (float)i / steps;
                    float y0 = r.Height * t0;
                    float y1 = r.Height * ((float)(i + 1) / steps);
                    var col = Color.Lerp(InkWashTheme.GoldPrimary, InkWashTheme.GoldDeep, t0);
                    Render2D.FillRectangle(new Rectangle(0f, y0, r.Width, y1 - y0 + 0.5f), col);
                }
                InkRenderHelper.DrawRoundedRectangle(r, 8f, InkWashTheme.GoldBright, 1f);
                var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 15f).GetFont();
                if (font != null)
                    Render2D.DrawText(font, "≫ 进入秘境", r, InkWashTheme.TextInverse,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
            }

            public override void OnMouseEnter(Float2 location) { _hovered = true; base.OnMouseEnter(location); }
            public override void OnMouseLeave() { _hovered = false; base.OnMouseLeave(); }

            public override bool OnMouseUp(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left && ContainsPoint(ref location)) Clicked?.Invoke();
                return base.OnMouseUp(location, button);
            }
        }

        /// <summary>邀请队友按钮（28x28 金色方按钮）。</summary>
        private sealed class InviteBtn : Control
        {
            private bool _hovered;

            public event Action Clicked;

            public InviteBtn() { BackgroundColor = Color.Transparent; AutoFocus = false; }

            public override void Draw()
            {
                if (!Visible) return;
                base.Draw();
                var r = new Rectangle(Float2.Zero, Size);
                InkRenderHelper.FillRoundedRectangle(r, 4f, WithAlpha(InkWashTheme.GoldPrimary, _hovered ? 0.16f : 0.08f));
                InkRenderHelper.DrawRoundedRectangle(r, 4f, InkWashTheme.BorderGold, 1f);
                var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 15f).GetFont();
                if (font != null)
                    Render2D.DrawText(font, "＋", r, _hovered ? InkWashTheme.GoldBright : InkWashTheme.GoldPrimary,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
            }

            public override void OnMouseEnter(Float2 location) { _hovered = true; base.OnMouseEnter(location); }
            public override void OnMouseLeave() { _hovered = false; base.OnMouseLeave(); }

            public override bool OnMouseUp(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left && ContainsPoint(ref location)) Clicked?.Invoke();
                return base.OnMouseUp(location, button);
            }
        }
    }
}

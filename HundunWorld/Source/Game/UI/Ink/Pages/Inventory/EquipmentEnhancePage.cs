using FlaxEngine;
using FlaxEngine.GUI;
using Game.Character.Attributes;
using HundunWorld.Game.UI.StyleSystem;
using System;

namespace HundunWorld.Game.UI.Ink.Pages.Inventory
{
    /// <summary>
    /// 装备强化面板 — 对应设计方案 equipment-enhance.html。
    /// 全屏三栏布局：顶栏（返回+标题+提示）+ Tab栏（强化/镶嵌/精炼/调律/淬火）
    /// + 左栏（品质筛选+装备列表）+ 中栏（预览+属性对比+成功率+执行）+ 右栏（材料/进度/套装/五行）。
    /// 严格遵循水墨主题 Token，禁止硬编码色值。
    /// </summary>
    public class EquipmentEnhancePage : ContainerControl, IInkPage
    {
        private const float TopBarHeight = 56f;
        private const float TabBarHeight = 44f;
        private const float Edge = 16f;
        private const float ColGap = 16f;
        private const float LeftWidth = 250f;
        private const float RightWidth = 300f;

        public event Action<string> NavigationRequested;
        public InkParticleSystem ParticleSystem { get; set; }

        private CharacterAttributesComponent _boundCharacter;

        // 顶栏
        private ContainerControl _topBar;
        private InkButton _backBtn;

        // Tab 栏
        private ContainerControl _tabBar;
        private EnhTab[] _tabs;

        // 左栏
        private ContainerControl _leftCol;
        private FilterPill[] _pills;
        private EquipItem[] _equipItems;
        private int _selectedEquip = 0;

        // 中栏
        private ContainerControl _centerCol;
        private ContainerControl _attrTable;
        private PreviewBox _previewBox;
        private ContainerControl _rateTrack;
        private Label _rateLabel;
        private InkButton _enhanceBtn;

        // 右栏
        private ContainerControl _rightCol;

        public void BindCharacter(CharacterAttributesComponent component)
        {
            _boundCharacter = component;
        }

        public EquipmentEnhancePage()
        {
            try
            {
                AnchorPreset = AnchorPresets.StretchAll;
                Offsets = Margin.Zero;
                BackgroundColor = InkWashTheme.Void;
                ClipChildren = false;
                AutoFocus = false;

                BuildTopBar();
                BuildTabBar();
                BuildLeftColumn();
                BuildCenterColumn();
                BuildRightColumn();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[EquipmentEnhancePage] init failed: {ex.Message}");
            }
        }

        // ===================================================================
        // 顶栏：返回 + 标题 + 徽章 + 提示
        // ===================================================================

        private void BuildTopBar()
        {
            _topBar = new ContainerControl
            {
                AnchorPreset = AnchorPresets.HorizontalStretchTop,
                Location = Float2.Zero,
                Size = new Float2(Width, TopBarHeight),
                BackgroundColor = InkWashTheme.BaseSecondary,
            };
            AddChild(_topBar);

            _topBar.AddChild(new ContainerControl
            {
                AnchorPreset = AnchorPresets.HorizontalStretchTop,
                Location = new Float2(0f, TopBarHeight - 1f),
                Size = new Float2(Width, 1f),
                BackgroundColor = InkWashTheme.BorderGold,
            });

            _backBtn = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Sm,
                Text = "←",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(Edge, (TopBarHeight - 36f) * 0.5f),
                Size = new Float2(36f, 36f),
                BorderColor = InkWashTheme.BorderGold,
                BorderThickness = 1f,
            };
            _backBtn.ButtonClicked += (b) => NavigationRequested?.Invoke(InkPageDomIds.CombatHud);
            _topBar.AddChild(_backBtn);

            _topBar.AddChild(MakeLabel("装备强化", Edge + 36f + 16f, 0f, 140f, TopBarHeight,
                InkWashTheme.GoldPrimary, 22f, InkWashTheme.FontRole.Display, TextAlignment.Near));

            // “锻造工坊”徽章
            var badge = new TagPill("锻造工坊", InkWashTheme.TextTertiary, InkWashTheme.BorderFaint, Color.Transparent)
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(Edge + 36f + 16f + 140f + 8f, (TopBarHeight - 22f) * 0.5f),
                Size = new Float2(72f, 22f),
            };
            _topBar.AddChild(badge);
        }

        // ===================================================================
        // Tab 栏：强化/镶嵌/精炼/调律/淬火 + 右侧警告
        // ===================================================================

        private void BuildTabBar()
        {
            _tabBar = new ContainerControl
            {
                AnchorPreset = AnchorPresets.HorizontalStretchTop,
                Location = new Float2(0f, TopBarHeight),
                Size = new Float2(Width, TabBarHeight),
                BackgroundColor = InkWashTheme.BaseSecondary,
            };
            AddChild(_tabBar);

            _tabBar.AddChild(new ContainerControl
            {
                AnchorPreset = AnchorPresets.HorizontalStretchTop,
                Location = new Float2(0f, TabBarHeight - 1f),
                Size = new Float2(Width, 1f),
                BackgroundColor = InkWashTheme.Divider,
            });

            string[] tabNames = { "强化", "镶嵌", "精炼", "调律", "淬火" };
            _tabs = new EnhTab[tabNames.Length];
            float tabX = Edge;
            for (int i = 0; i < tabNames.Length; i++)
            {
                var tab = new EnhTab(tabNames[i], i == 0)
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(tabX, 0f),
                    Size = new Float2(84f, TabBarHeight),
                };
                int captured = i;
                tab.Clicked += () => OnTabClicked(captured);
                _tabs[i] = tab;
                _tabBar.AddChild(tab);
                tabX += 84f + 4f;
            }
        }

        // ===================================================================
        // 左栏：品质筛选 + 装备列表
        // ===================================================================

        private void BuildLeftColumn()
        {
            float contentTop = TopBarHeight + TabBarHeight + Edge;
            _leftCol = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(Edge, contentTop),
                Size = new Float2(LeftWidth, 100f),
                BackgroundColor = Color.Transparent,
            };
            AddChild(_leftCol);
            _leftCol.AddChild(new RoundedPanel(InkWashTheme.BaseSecondary, InkWashTheme.BorderGold, 8f));

            float pad = 12f;
            float cy = pad;

            // 品质筛选标题
            _leftCol.AddChild(MakeLabel("◆ 品质筛选", pad, cy, 140f, 18f,
                InkWashTheme.TextSecondary, 12f, InkWashTheme.FontRole.Body, TextAlignment.Near));
            cy += 18f + 8f;

            // 筛选药丸：全部/普通/优良/稀有/史诗/传说
            var pillDefs = new (string name, Color color, bool active)[]
            {
                ("全部", InkWashTheme.GoldBright, true),
                ("普通", InkWashTheme.QualityCommon, false),
                ("优良", InkWashTheme.QualityUncommon, false),
                ("稀有", InkWashTheme.QualityRare, false),
                ("史诗", InkWashTheme.QualityEpic, false),
                ("传说", InkWashTheme.QualityLegendary, false),
            };
            _pills = new FilterPill[pillDefs.Length];
            float px = pad;
            for (int i = 0; i < pillDefs.Length; i++)
            {
                var d = pillDefs[i];
                Color bg = d.active ? InkWashTheme.GoldTrace : Color.Transparent;
                var pill = new FilterPill(d.name, d.color, bg, d.active ? InkWashTheme.GoldPrimary : d.color)
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(px, cy),
                    Size = new Float2(44f, 24f),
                };
                _pills[i] = pill;
                _leftCol.AddChild(pill);
                px += 44f + 6f;
                if (px > LeftWidth - pad - 44f && i < pillDefs.Length - 1) { px = pad; cy += 24f + 6f; }
            }
            cy += 24f + 10f;

            // 分隔线
            _leftCol.AddChild(new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(pad, cy),
                Size = new Float2(LeftWidth - pad * 2f, 1f),
                BackgroundColor = InkWashTheme.Divider,
            });
            cy += 1f + 10f;

            // 装备列表标题 + 计数
            _leftCol.AddChild(MakeLabel("装备列表", pad, cy, 100f, 18f,
                InkWashTheme.TextSecondary, 12f, InkWashTheme.FontRole.Body, TextAlignment.Near));
            _leftCol.AddChild(MakeLabel("8件", LeftWidth - pad - 40f, cy, 40f, 18f,
                InkWashTheme.TextTertiary, 11f, InkWashTheme.FontRole.Number, TextAlignment.Far));
            cy += 18f + 8f;

            // 装备项（8 个）
            var equips = new (string glyph, string name, string sub, string enh,
                              InkWashTheme.InkQuality q, bool selected)[]
            {
                ("剑", "玄铁重剑", "双手剑 · 60级", "+12", InkWashTheme.InkQuality.Legendary, true),
                ("枪", "赤霄枪",   "长枪 · 60级",   "+15", InkWashTheme.InkQuality.Legendary, false),
                ("冠", "紫金冠",   "头冠 · 55级",   "+10", InkWashTheme.InkQuality.Epic,      false),
                ("袍", "天罡袍",   "法袍 · 55级",   "+9",  InkWashTheme.InkQuality.Epic,      false),
                ("杖", "碧玉杖",   "法杖 · 50级",   "+5",  InkWashTheme.InkQuality.Rare,      false),
                ("盾", "玄武盾",   "盾牌 · 50级",   "+6",  InkWashTheme.InkQuality.Rare,      false),
                ("刀", "寒月刀",   "单刀 · 45级",   "+2",  InkWashTheme.InkQuality.Uncommon,  false),
                ("腕", "铁甲护腕", "护腕 · 40级",   "+3",  InkWashTheme.InkQuality.Common,    false),
            };
            _equipItems = new EquipItem[equips.Length];
            for (int i = 0; i < equips.Length; i++)
            {
                var e = equips[i];
                var item = new EquipItem(e.glyph, e.name, e.sub, e.enh, e.q, e.selected)
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(pad, cy),
                    Size = new Float2(LeftWidth - pad * 2f, 52f),
                };
                int captured = i;
                item.Clicked += () => OnEquipClicked(captured);
                _equipItems[i] = item;
                _leftCol.AddChild(item);
                cy += 52f + 8f;
            }
        }

        // ===================================================================
        // 中栏：预览 + 名称标签 + 属性对比 + 成功率 + 执行
        // ===================================================================

        private void BuildCenterColumn()
        {
            float contentTop = TopBarHeight + TabBarHeight + Edge;
            float centerX = Edge + LeftWidth + ColGap;
            _centerCol = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(centerX, contentTop),
                Size = new Float2(400f, 100f),
                BackgroundColor = Color.Transparent,
            };
            AddChild(_centerCol);
            _centerCol.AddChild(new RoundedPanel(InkWashTheme.BaseSecondary, InkWashTheme.BorderGold, 8f));

            float pad = 16f;
            float cy = pad;

            // 预览区 200px（径向渐变金辉 + 剑影 + 拖拽旋转）
            _previewBox = new PreviewBox
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(pad, cy),
                Size = new Float2(100f, 200f),
            };
            _centerCol.AddChild(_previewBox);
            cy += 200f + 16f;

            // 名称行：玄铁重剑 + 传说 + +12 + 双手剑 + 60级
            _centerCol.AddChild(MakeLabel("玄铁重剑", pad, cy, 130f, 26f,
                InkWashTheme.QualityLegendary, 20f, InkWashTheme.FontRole.Display, TextAlignment.Near));
            var qTag = new TagPill("传说", InkWashTheme.QualityLegendary, InkWashTheme.QualityLegendary, Color.Transparent)
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(pad + 134f, cy + 2f),
                Size = new Float2(48f, 22f),
            };
            _centerCol.AddChild(qTag);
            _centerCol.AddChild(MakeLabel("+12", pad + 186f, cy, 50f, 26f,
                InkWashTheme.GoldBright, 16f, InkWashTheme.FontRole.Number, TextAlignment.Near));
            var tTag = new TagPill("双手剑", InkWashTheme.TextTertiary, InkWashTheme.BorderFaint, Color.Transparent)
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(pad + 240f, cy + 2f),
                Size = new Float2(56f, 22f),
            };
            _centerCol.AddChild(tTag);
            var lTag = new TagPill("60级", InkWashTheme.TextTertiary, InkWashTheme.BorderFaint, Color.Transparent)
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(pad + 300f, cy + 2f),
                Size = new Float2(48f, 22f),
            };
            _centerCol.AddChild(lTag);
            cy += 26f + 14f;

            // 属性对比标题
            _centerCol.AddChild(MakeLabel("▲ 属性对比", pad, cy, 100f, 18f,
                InkWashTheme.TextSecondary, 12f, InkWashTheme.FontRole.Body, TextAlignment.Near));
            _centerCol.AddChild(MakeLabel("强化后预览", pad + 104f, cy, 80f, 18f,
                InkWashTheme.TextTertiary, 10f, InkWashTheme.FontRole.Body, TextAlignment.Near));
            cy += 18f + 6f;

            // 属性对比表
            _attrTable = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(pad, cy),
                Size = new Float2(100f, 150f),
                BackgroundColor = Color.Transparent,
            };
            _centerCol.AddChild(_attrTable);
            BuildAttrTable();
            cy += 150f + 14f;

            // 成功率
            _centerCol.AddChild(MakeLabel("% 成功率", pad, cy, 80f, 18f,
                InkWashTheme.TextSecondary, 12f, InkWashTheme.FontRole.Body, TextAlignment.Near));
            _rateLabel = MakeLabel("78%", 200f, cy - 2f, 80f, 22f,
                InkWashTheme.GoldBright, 16f, InkWashTheme.FontRole.Number, TextAlignment.Far);
            _centerCol.AddChild(_rateLabel);
            cy += 22f + 6f;

            _rateTrack = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(pad, cy),
                Size = new Float2(100f, 10f),
                BackgroundColor = InkWashTheme.Void,
            };
            _centerCol.AddChild(_rateTrack);
            _rateTrack.AddChild(new HGradientBar(0.78f,
                InkWashTheme.GoldDeep, InkWashTheme.GoldPrimary, InkWashTheme.GoldBright)
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Float2(100f, 10f),
            });
            cy += 10f + 6f;

            _centerCol.AddChild(MakeLabel("⚠ 失败将降低1级强化等级，建议使用护身符", pad, cy, 320f, 16f,
                InkWashTheme.BloodPrimary, 10f, InkWashTheme.FontRole.Body, TextAlignment.Near));
            cy += 16f + 12f;

            // 消耗 + 执行强化按钮
            _centerCol.AddChild(MakeLabel("玄铁强化石", pad + 40f, cy, 90f, 16f,
                InkWashTheme.TextSecondary, 11f, InkWashTheme.FontRole.Body, TextAlignment.Near));
            _centerCol.AddChild(MakeLabel("x5", pad + 40f, cy + 16f, 40f, 18f,
                InkWashTheme.GoldBright, 13f, InkWashTheme.FontRole.Number, TextAlignment.Near));
            _centerCol.AddChild(new MatIcon("石", pad, cy));
            _centerCol.AddChild(MakeLabel("银两", pad + 40f + 110f, cy, 60f, 16f,
                InkWashTheme.TextSecondary, 11f, InkWashTheme.FontRole.Body, TextAlignment.Near));
            _centerCol.AddChild(MakeLabel("x50,000", pad + 40f + 110f, cy + 16f, 70f, 18f,
                InkWashTheme.GoldBright, 13f, InkWashTheme.FontRole.Number, TextAlignment.Near));
            _centerCol.AddChild(new MatIcon("银", pad + 110f, cy));

            _enhanceBtn = new InkButton
            {
                Variant = InkButtonVariant.Brand,
                ButtonSize = InkButtonSize.Lg,
                Text = "执行强化",
                AnchorPreset = AnchorPresets.TopRight,
                Location = new Float2(-140f, cy),
                Size = new Float2(124f, 36f),
            };
            _enhanceBtn.ButtonClicked += (b) => EmitGoldAtButton(b);
            _centerCol.AddChild(_enhanceBtn);
        }

        /// <summary>属性对比表：属性/当前/强化后/变化（5 行）。</summary>
        private void BuildAttrTable()
        {
            var rows = new (string name, string cur, string next, string delta, bool changed)[]
            {
                ("攻击力", "1245",  "1320",  "+75",   true),
                ("暴击率", "12.5%", "13.8%", "+1.3%", true),
                ("命中",   "320",   "340",   "+20",   true),
                ("穿透",   "85",    "92",    "+7",    true),
                ("会心",   "45",    "45",    "—",     false),
            };

            // 表头
            _attrTable.AddChild(MakeLabel("属性", 0f, 0f, 120f, 20f,
                InkWashTheme.TextTertiary, 12f, InkWashTheme.FontRole.Body, TextAlignment.Near));
            _attrTable.AddChild(MakeLabel("当前", 150f, 0f, 100f, 20f,
                InkWashTheme.TextTertiary, 12f, InkWashTheme.FontRole.Body, TextAlignment.Far));
            _attrTable.AddChild(MakeLabel("强化后", 290f, 0f, 100f, 20f,
                InkWashTheme.TextTertiary, 12f, InkWashTheme.FontRole.Body, TextAlignment.Far));
            _attrTable.AddChild(MakeLabel("变化", 400f, 0f, 80f, 20f,
                InkWashTheme.TextTertiary, 12f, InkWashTheme.FontRole.Body, TextAlignment.Far));
            _attrTable.AddChild(new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 22f),
                Size = new Float2(480f, 1f),
                BackgroundColor = InkWashTheme.Divider,
            });

            float ry = 26f;
            foreach (var r in rows)
            {
                _attrTable.AddChild(MakeLabel(r.name, 0f, ry, 120f, 20f,
                    InkWashTheme.TextSecondary, 13f, InkWashTheme.FontRole.Body, TextAlignment.Near));
                _attrTable.AddChild(MakeLabel(r.cur, 150f, ry, 100f, 20f,
                    InkWashTheme.TextDefault, 13f, InkWashTheme.FontRole.Number, TextAlignment.Far));
                _attrTable.AddChild(MakeLabel(r.next, 290f, ry, 100f, 20f,
                    InkWashTheme.GoldBright, 13f, InkWashTheme.FontRole.Number, TextAlignment.Far));
                _attrTable.AddChild(MakeLabel(r.delta, 400f, ry, 80f, 20f,
                    r.changed ? InkWashTheme.JadeBright : InkWashTheme.TextTertiary,
                    13f, InkWashTheme.FontRole.Number, TextAlignment.Far));
                ry += 20f;
                _attrTable.AddChild(new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(0f, ry),
                    Size = new Float2(480f, 1f),
                    BackgroundColor = InkWashTheme.Divider,
                });
                ry += 1f + 4f;
            }
        }

        // ===================================================================
        // 右栏：所需材料 + 强化进度 + 套装效果 + 五行分布
        // ===================================================================

        private void BuildRightColumn()
        {
            float contentTop = TopBarHeight + TabBarHeight + Edge;
            _rightCol = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(100f, contentTop),
                Size = new Float2(RightWidth, 100f),
                BackgroundColor = Color.Transparent,
            };
            AddChild(_rightCol);
            _rightCol.AddChild(new RoundedPanel(InkWashTheme.BaseSecondary, InkWashTheme.BorderGold, 8f));

            float pad = 16f;
            float cy = pad;
            float innerW = RightWidth - pad * 2f;

            // 所需材料
            _rightCol.AddChild(MakeLabel("◆ 所需材料", pad, cy, 120f, 18f,
                InkWashTheme.TextSecondary, 12f, InkWashTheme.FontRole.Body, TextAlignment.Near));
            cy += 18f + 8f;
            var mats = new (string glyph, string name, string have)[]
            {
                ("石", "玄铁强化石", "拥有 23 / 需要 5"),
                ("砂", "精炼砂",     "拥有 8 / 需要 3"),
                ("银", "银两",       "拥有 128,000 / 需要 50,000"),
            };
            foreach (var m in mats)
            {
                var row = new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(pad, cy),
                    Size = new Float2(innerW, 44f),
                    BackgroundColor = InkWashTheme.BaseTertiary,
                };
                _rightCol.AddChild(row);
                row.AddChild(new MatIcon(m.glyph, 8f, 6f));
                row.AddChild(MakeLabel(m.name, 48f, 5f, 140f, 16f,
                    InkWashTheme.TextDefault, 12f, InkWashTheme.FontRole.Body, TextAlignment.Near));
                row.AddChild(MakeLabel(m.have, 48f, 22f, 160f, 15f,
                    InkWashTheme.TextTertiary, 11f, InkWashTheme.FontRole.Number, TextAlignment.Near));
                row.AddChild(MakeLabel("✓", innerW - 28f, 12f, 20f, 20f,
                    InkWashTheme.JadePrimary, 14f, InkWashTheme.FontRole.Body, TextAlignment.Center));
                cy += 44f + 8f;
            }
            cy += 6f;

            // 强化进度
            _rightCol.AddChild(MakeLabel("▲ 强化进度", pad, cy, 120f, 18f,
                InkWashTheme.TextSecondary, 12f, InkWashTheme.FontRole.Body, TextAlignment.Near));
            cy += 18f + 8f;
            _rightCol.AddChild(MakeLabel("+12", pad, cy, 60f, 28f,
                InkWashTheme.GoldBright, 22f, InkWashTheme.FontRole.Number, TextAlignment.Near));
            _rightCol.AddChild(MakeLabel("→", pad + 70f, cy, 30f, 28f,
                InkWashTheme.TextTertiary, 16f, InkWashTheme.FontRole.Body, TextAlignment.Center));
            _rightCol.AddChild(MakeLabel("+13", pad + 104f, cy, 60f, 28f,
                InkWashTheme.TextTertiary, 22f, InkWashTheme.FontRole.Number, TextAlignment.Near));
            cy += 28f + 8f;

            var progTrack = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(pad, cy),
                Size = new Float2(innerW, 8f),
                BackgroundColor = InkWashTheme.Void,
            };
            _rightCol.AddChild(progTrack);
            progTrack.AddChild(new HGradientBar(0.6f, InkWashTheme.GoldDeep, InkWashTheme.GoldPrimary)
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Float2(innerW, 8f),
            });
            cy += 8f + 6f;
            _rightCol.AddChild(MakeLabel("当前等级", pad, cy, 80f, 14f,
                InkWashTheme.TextTertiary, 10f, InkWashTheme.FontRole.Body, TextAlignment.Near));
            _rightCol.AddChild(MakeLabel("上限 +20", pad + innerW - 70f, cy, 70f, 14f,
                InkWashTheme.TextTertiary, 10f, InkWashTheme.FontRole.Number, TextAlignment.Far));
            cy += 14f + 12f;

            // 套装效果
            _rightCol.AddChild(MakeLabel("▤ 套装效果", pad, cy, 120f, 18f,
                InkWashTheme.TextSecondary, 12f, InkWashTheme.FontRole.Body, TextAlignment.Near));
            cy += 18f + 8f;
            var setBox = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(pad, cy),
                Size = new Float2(innerW, 84f),
                BackgroundColor = InkWashTheme.BaseTertiary,
            };
            _rightCol.AddChild(setBox);
            setBox.AddChild(MakeLabel("玄铁战意", 12f, 8f, 100f, 18f,
                InkWashTheme.QualityLegendary, 13f, InkWashTheme.FontRole.Display, TextAlignment.Near));
            setBox.AddChild(MakeLabel("2 / 4 件", innerW - 70f, 8f, 58f, 18f,
                InkWashTheme.TextTertiary, 11f, InkWashTheme.FontRole.Number, TextAlignment.Far));
            // 2件套（已激活）
            setBox.AddChild(new SetBadge("2", true) { Location = new Float2(12f, 34f) });
            setBox.AddChild(MakeLabel("攻击力 +5%", 40f, 34f, 100f, 20f,
                InkWashTheme.JadeBright, 11f, InkWashTheme.FontRole.Body, TextAlignment.Near));
            setBox.AddChild(MakeLabel("✓", innerW - 30f, 34f, 18f, 20f,
                InkWashTheme.JadePrimary, 12f, InkWashTheme.FontRole.Body, TextAlignment.Center));
            // 4件套（未激活）
            setBox.AddChild(new SetBadge("4", false) { Location = new Float2(12f, 58f) });
            setBox.AddChild(MakeLabel("暴击伤害 +20%", 40f, 58f, 110f, 20f,
                InkWashTheme.TextTertiary, 11f, InkWashTheme.FontRole.Body, TextAlignment.Near));
            setBox.AddChild(MakeLabel("未激活", innerW - 52f, 58f, 40f, 20f,
                InkWashTheme.TextTertiary, 10f, InkWashTheme.FontRole.Body, TextAlignment.Far));
            cy += 84f + 12f;

            // 五行分布
            _rightCol.AddChild(MakeLabel("● 五行分布", pad, cy, 120f, 18f,
                InkWashTheme.TextSecondary, 12f, InkWashTheme.FontRole.Body, TextAlignment.Near));
            cy += 18f + 8f;
            var elems = new (string name, InkWashTheme.InkElement elem, float pct)[]
            {
                ("金", InkWashTheme.InkElement.Metal, 0.35f),
                ("木", InkWashTheme.InkElement.Wood,  0.15f),
                ("水", InkWashTheme.InkElement.Water, 0.20f),
                ("火", InkWashTheme.InkElement.Fire,  0.20f),
                ("土", InkWashTheme.InkElement.Earth, 0.10f),
            };
            foreach (var el in elems)
            {
                Color ec = InkWashTheme.ElementColor(el.elem);
                float ex = pad;
                var dot = new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(ex, cy + 4f),
                    Size = new Float2(10f, 10f),
                    BackgroundColor = ec,
                };
                _rightCol.AddChild(dot);
                _rightCol.AddChild(MakeLabel(el.name, ex + 16f, cy, 20f, 18f,
                    InkWashTheme.TextSecondary, 11f, InkWashTheme.FontRole.Body, TextAlignment.Near));
                float barX = ex + 40f;
                float barW = innerW - 40f - 44f;
                var track = new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(barX, cy + 6f),
                    Size = new Float2(barW, 6f),
                    BackgroundColor = InkWashTheme.Void,
                };
                _rightCol.AddChild(track);
                track.AddChild(new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = Float2.Zero,
                    Size = new Float2(barW * el.pct, 6f),
                    BackgroundColor = ec,
                });
                _rightCol.AddChild(MakeLabel(((int)(el.pct * 100f)) + "%", pad + innerW - 38f, cy, 38f, 18f,
                    InkWashTheme.TextDefault, 11f, InkWashTheme.FontRole.Number, TextAlignment.Far));
                cy += 18f + 8f;
            }
        }

        // ===================================================================
        // 事件处理
        // ===================================================================

        private void OnTabClicked(int index)
        {
            if (_tabs == null) return;
            for (int i = 0; i < _tabs.Length; i++)
                _tabs[i].IsActive = (i == index);
        }

        private void OnEquipClicked(int index)
        {
            if (_equipItems == null) return;
            for (int i = 0; i < _equipItems.Length; i++)
                _equipItems[i].IsSelected = (i == index);
            _selectedEquip = index;
            if (index >= 0 && index < _equipItems.Length)
                EmitGoldAtControl(_equipItems[index]);
        }

        private void EmitGoldAtButton(Button button)
        {
            if (ParticleSystem == null || button == null) return;
            try
            {
                var center = new Float2(button.Width * 0.5f, button.Height * 0.5f);
                var screenPos = button.PointToScreen(center);
                var localPos = ParticleSystem.PointFromScreen(screenPos);
                ParticleSystem.EmitGoldBurst(localPos, count: 16, isLarge: true);
            }
            catch { }
        }

        private void EmitGoldAtControl(Control control)
        {
            if (ParticleSystem == null || control == null) return;
            try
            {
                var center = new Float2(control.Width * 0.5f, control.Height * 0.5f);
                var screenPos = control.PointToScreen(center);
                var localPos = ParticleSystem.PointFromScreen(screenPos);
                ParticleSystem.EmitGoldBurst(localPos, count: 8, isLarge: false);
            }
            catch { }
        }

        // ===================================================================
        // 布局
        // ===================================================================

        public void RefreshLayout()
        {
            try
            {
                float w = Width;
                float h = Height;
                if (w <= 0f || h <= 0f) return;

                float contentTop = TopBarHeight + TabBarHeight + Edge;
                float contentH = h - contentTop - Edge;
                if (contentH < 200f) contentH = 200f;

                if (_topBar != null) _topBar.Size = new Float2(w, TopBarHeight);
                if (_tabBar != null) _tabBar.Size = new Float2(w, TabBarHeight);

                if (_leftCol != null)
                {
                    _leftCol.Location = new Float2(Edge, contentTop);
                    _leftCol.Size = new Float2(LeftWidth, contentH);
                }

                float centerX = Edge + LeftWidth + ColGap;
                float centerW = w - Edge * 2f - LeftWidth - RightWidth - ColGap * 2f;
                if (centerW < 300f) centerW = 300f;
                if (_centerCol != null)
                {
                    _centerCol.Location = new Float2(centerX, contentTop);
                    _centerCol.Size = new Float2(centerW, contentH);
                }

                float innerW = centerW - 32f;
                if (innerW < 260f) innerW = 260f;
                if (_previewBox != null) _previewBox.Size = new Float2(innerW, 200f);
                if (_attrTable != null) _attrTable.Size = new Float2(innerW, 150f);
                if (_rateTrack != null)
                {
                    _rateTrack.Size = new Float2(innerW, 10f);
                    if (_rateTrack.Children.Count > 0)
                        _rateTrack.Children[0].Size = new Float2(innerW, 10f);
                }
                if (_rateLabel != null)
                    _rateLabel.Location = new Float2(innerW - 64f, _rateLabel.Location.Y);

                if (_rightCol != null)
                {
                    _rightCol.Location = new Float2(w - Edge - RightWidth, contentTop);
                    _rightCol.Size = new Float2(RightWidth, contentH);
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[EquipmentEnhancePage] RefreshLayout failed: {ex.Message}");
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
        // 嵌套控件
        // ===================================================================

        /// <summary>自绘圆角面板背景 + 边框（StretchAll）。</summary>
        private sealed class RoundedPanel : Control
        {
            private readonly Color _bg;
            private readonly Color _border;
            private readonly float _radius;

            public RoundedPanel(Color bg, Color border, float radius)
            {
                _bg = bg;
                _border = border;
                _radius = radius;
                AutoFocus = false;
                AnchorPreset = AnchorPresets.StretchAll;
                Offsets = Margin.Zero;
            }

            public override void Draw()
            {
                base.Draw();
                if (!Visible) return;
                var rect = new Rectangle(Float2.Zero, Size);
                if (_bg.A > 0f)
                    InkRenderHelper.FillRoundedRectangle(rect, _radius, _bg);
                if (_border.A > 0f)
                    InkRenderHelper.DrawRoundedRectangle(rect, _radius, _border, 1f);
            }
        }

        /// <summary>顶部功能 Tab（14px 楷书，激活金色 + 2px 下划线）。</summary>
        private sealed class EnhTab : Control
        {
            private readonly string _text;
            private bool _isActive;
            private bool _isHovered;

            public event Action Clicked;
            public bool IsActive { get => _isActive; set => _isActive = value; }

            public EnhTab(string text, bool active)
            {
                _text = text;
                _isActive = active;
                AutoFocus = false;
            }

            public override void Draw()
            {
                base.Draw();
                if (!Visible) return;
                Color color = _isActive ? InkWashTheme.GoldPrimary
                    : (_isHovered ? InkWashTheme.TextDefault : InkWashTheme.TextTertiary);
                var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 14f).GetFont();
                if (font != null)
                    Render2D.DrawText(font, _text, new Rectangle(Float2.Zero, Size), color,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
                if (_isActive)
                    Render2D.FillRectangle(new Rectangle(0f, Height - 2f, Width, 2f), InkWashTheme.GoldPrimary);
            }

            public override void OnMouseEnter(Float2 location) { _isHovered = true; base.OnMouseEnter(location); }
            public override void OnMouseLeave() { _isHovered = false; base.OnMouseLeave(); }

            public override bool OnMouseUp(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left && ContainsPoint(ref location))
                    Clicked?.Invoke();
                return base.OnMouseUp(location, button);
            }
        }

        /// <summary>品质筛选药丸（11px，radius4，hover 金底）。</summary>
        private sealed class FilterPill : Control
        {
            private readonly string _text;
            private readonly Color _textColor;
            private readonly Color _bg;
            private readonly Color _border;
            private bool _isHovered;

            public FilterPill(string text, Color textColor, Color bg, Color border)
            {
                _text = text;
                _textColor = textColor;
                _bg = bg;
                _border = border;
                AutoFocus = false;
            }

            public override void Draw()
            {
                base.Draw();
                if (!Visible) return;
                var rect = new Rectangle(Float2.Zero, Size);
                Color bg = (_isHovered && _bg.A <= 0f) ? InkWashTheme.BgHover : _bg;
                if (bg.A > 0f)
                    InkRenderHelper.FillRoundedRectangle(rect, 4f, bg);
                if (_border.A > 0f)
                    InkRenderHelper.DrawRoundedRectangle(rect, 4f, _border, 1f);
                var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f).GetFont();
                if (font != null)
                    Render2D.DrawText(font, _text, rect, _textColor,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
            }

            public override void OnMouseEnter(Float2 location) { _isHovered = true; base.OnMouseEnter(location); }
            public override void OnMouseLeave() { _isHovered = false; base.OnMouseLeave(); }
        }

        /// <summary>装备列表项（52px：40x40 图标 + 名/副 + 强化值）。</summary>
        private sealed class EquipItem : Control
        {
            private readonly string _glyph;
            private readonly string _name;
            private readonly string _sub;
            private readonly string _enh;
            private readonly InkWashTheme.InkQuality _quality;
            private bool _isSelected;
            private bool _isHovered;

            public event Action Clicked;
            public bool IsSelected { get => _isSelected; set => _isSelected = value; }

            public EquipItem(string glyph, string name, string sub, string enh,
                InkWashTheme.InkQuality quality, bool selected)
            {
                _glyph = glyph;
                _name = name;
                _sub = sub;
                _enh = enh;
                _quality = quality;
                _isSelected = selected;
                AutoFocus = false;
            }

            public override void Draw()
            {
                base.Draw();
                if (!Visible) return;
                var rect = new Rectangle(Float2.Zero, Size);
                Color qc = InkWashTheme.QualityColor(_quality);

                if (_isSelected)
                {
                    InkRenderHelper.FillRoundedRectangle(rect, 4f, InkWashTheme.BgHover);
                    InkRenderHelper.DrawRoundedRectangle(rect, 4f, InkWashTheme.GoldPrimary, 1f);
                }
                else
                {
                    if (_isHovered)
                        InkRenderHelper.FillRoundedRectangle(rect, 4f, InkWashTheme.BgHover);
                    InkRenderHelper.DrawRoundedRectangle(rect, 4f, InkWashTheme.BorderFaint, 1f);
                }

                var iconRect = new Rectangle(8f, (Height - 40f) * 0.5f, 40f, 40f);
                InkRenderHelper.FillRoundedRectangle(iconRect, 4f, InkWashTheme.Void);
                InkRenderHelper.DrawRoundedRectangle(iconRect, 4f, qc, 1f);
                var iconFont = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 20f).GetFont();
                if (iconFont != null)
                    Render2D.DrawText(iconFont, _glyph, iconRect, qc,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);

                float textX = 56f;
                var nameFont = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f).GetFont();
                if (nameFont != null)
                    Render2D.DrawText(nameFont, _name,
                        new Rectangle(textX, 8f, Width - textX - 50f, 18f),
                        _isSelected ? InkWashTheme.TextDefault : InkWashTheme.TextSecondary,
                        TextAlignment.Near, TextAlignment.Center, TextWrapping.NoWrap);
                var subFont = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f).GetFont();
                if (subFont != null)
                    Render2D.DrawText(subFont, _sub,
                        new Rectangle(textX, 28f, Width - textX - 50f, 16f),
                        InkWashTheme.TextTertiary,
                        TextAlignment.Near, TextAlignment.Center, TextWrapping.NoWrap);

                Color enhColor;
                if (_isSelected) enhColor = InkWashTheme.GoldBright;
                else if (_quality == InkWashTheme.InkQuality.Legendary
                    || _quality == InkWashTheme.InkQuality.Epic
                    || _quality == InkWashTheme.InkQuality.Rare)
                    enhColor = InkWashTheme.GoldPrimary;
                else enhColor = InkWashTheme.TextSecondary;
                var enhFont = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 14f).GetFont();
                if (enhFont != null)
                    Render2D.DrawText(enhFont, _enh,
                        new Rectangle(Width - 48f, 0f, 40f, Height),
                        enhColor, TextAlignment.Far, TextAlignment.Center, TextWrapping.NoWrap);
            }

            public override void OnMouseEnter(Float2 location) { _isHovered = true; base.OnMouseEnter(location); }
            public override void OnMouseLeave() { _isHovered = false; base.OnMouseLeave(); }

            public override bool OnMouseUp(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left && ContainsPoint(ref location))
                    Clicked?.Invoke();
                return base.OnMouseUp(location, button);
            }
        }

        /// <summary>标签药丸（11px，radius4）。</summary>
        private sealed class TagPill : Control
        {
            private readonly string _text;
            private readonly Color _textColor;
            private readonly Color _border;
            private readonly Color _bg;

            public TagPill(string text, Color textColor, Color border, Color bg)
            {
                _text = text;
                _textColor = textColor;
                _border = border;
                _bg = bg;
                AutoFocus = false;
            }

            public override void Draw()
            {
                base.Draw();
                if (!Visible) return;
                var rect = new Rectangle(Float2.Zero, Size);
                if (_bg.A > 0f)
                    InkRenderHelper.FillRoundedRectangle(rect, 4f, _bg);
                if (_border.A > 0f)
                    InkRenderHelper.DrawRoundedRectangle(rect, 4f, _border, 1f);
                var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f).GetFont();
                if (font != null)
                    Render2D.DrawText(font, _text, rect, _textColor,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
            }
        }

        /// <summary>装备预览区（200px：径向金辉 + 剑影 + 拖拽旋转）。</summary>
        private sealed class PreviewBox : Control
        {
            public PreviewBox()
            {
                AutoFocus = false;
            }

            public override void Draw()
            {
                base.Draw();
                if (!Visible) return;
                var rect = new Rectangle(Float2.Zero, Size);
                InkRenderHelper.FillRoundedRectangle(rect, 6f, InkWashTheme.Void);
                var center = new Float2(Width * 0.5f, Height * 0.5f);
                InkRenderHelper.FillRadialGradient(center, Mathf.Max(Width, Height) * 0.5f,
                    InkWashTheme.GoldTrace, Color.Transparent);
                InkRenderHelper.FillRadialGradient(center, 72f,
                    InkWashTheme.GoldGlow, Color.Transparent);
                InkRenderHelper.DrawRoundedRectangle(rect, 6f, InkWashTheme.BorderFaint, 1f);

                var swordFont = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 64f).GetFont();
                if (swordFont != null)
                    Render2D.DrawText(swordFont, "剑",
                        new Rectangle(center.X - 40f, center.Y - 52f, 80f, 70f),
                        InkWashTheme.QualityLegendary,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);

                var capFont = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f).GetFont();
                if (capFont != null)
                {
                    Render2D.DrawText(capFont, "装备预览",
                        new Rectangle(center.X - 60f, center.Y + 24f, 120f, 16f),
                        InkWashTheme.TextTertiary,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
                    Render2D.DrawText(capFont, "拖拽旋转",
                        new Rectangle(Width - 84f, 8f, 74f, 14f),
                        InkWashTheme.TextTertiary,
                        TextAlignment.Far, TextAlignment.Center, TextWrapping.NoWrap);
                }
            }
        }

        /// <summary>材料图标（32x32：void 底 + faint 边 + 金字）。</summary>
        private sealed class MatIcon : Control
        {
            private readonly string _glyph;

            public MatIcon(string glyph, float x, float y)
            {
                _glyph = glyph;
                AutoFocus = false;
                AnchorPreset = AnchorPresets.TopLeft;
                Location = new Float2(x, y);
                Size = new Float2(32f, 32f);
            }

            public override void Draw()
            {
                base.Draw();
                if (!Visible) return;
                var rect = new Rectangle(Float2.Zero, Size);
                InkRenderHelper.FillRoundedRectangle(rect, 4f, InkWashTheme.Void);
                InkRenderHelper.DrawRoundedRectangle(rect, 4f, InkWashTheme.BorderFaint, 1f);
                var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 16f).GetFont();
                if (font != null)
                    Render2D.DrawText(font, _glyph, rect, InkWashTheme.GoldPrimary,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
            }
        }

        /// <summary>水平渐变进度条（按 fillRatio 填充，垂直条纹插值）。</summary>
        private sealed class HGradientBar : Control
        {
            private readonly float _fillRatio;
            private readonly Color[] _gradient;

            public HGradientBar(float fillRatio, params Color[] gradient)
            {
                _fillRatio = Mathf.Clamp(fillRatio, 0f, 1f);
                _gradient = gradient;
                AutoFocus = false;
            }

            public override void Draw()
            {
                base.Draw();
                if (!Visible) return;
                float fillW = Width * _fillRatio;
                if (fillW <= 0f || _gradient == null || _gradient.Length == 0) return;
                int steps = Mathf.Clamp(Mathf.FloorToInt(fillW), 2, 64);
                float stepW = fillW / steps;
                for (int i = 0; i < steps; i++)
                {
                    float t = steps == 1 ? 0f : (float)i / (steps - 1);
                    Render2D.FillRectangle(new Rectangle(i * stepW, 0f, stepW + 0.5f, Height), Sample(t));
                }
            }

            private Color Sample(float t)
            {
                if (_gradient.Length == 1) return _gradient[0];
                float scaled = t * (_gradient.Length - 1);
                int idx = Mathf.FloorToInt(scaled);
                if (idx >= _gradient.Length - 1) return _gradient[_gradient.Length - 1];
                return Color.Lerp(_gradient[idx], _gradient[idx + 1], scaled - idx);
            }
        }

        /// <summary>套装徽章（20px 圆形：激活青 / 未激活灰）。</summary>
        private sealed class SetBadge : Control
        {
            private readonly string _num;
            private readonly bool _active;

            public SetBadge(string num, bool active)
            {
                _num = num;
                _active = active;
                AutoFocus = false;
                Size = new Float2(20f, 20f);
            }

            public override void Draw()
            {
                base.Draw();
                if (!Visible) return;
                var center = new Float2(Width * 0.5f, Height * 0.5f);
                float r = Width * 0.5f;
                if (_active)
                {
                    InkRenderHelper.FillCircle(center, r, InkWashTheme.JadeFaint);
                }
                else
                {
                    InkRenderHelper.FillCircle(center, r, InkWashTheme.Void);
                    InkRenderHelper.DrawCircle(center, r, InkWashTheme.BorderFaint, 1f);
                }
                var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 11f).GetFont();
                if (font != null)
                    Render2D.DrawText(font, _num, new Rectangle(Float2.Zero, Size),
                        _active ? InkWashTheme.JadeBright : InkWashTheme.TextTertiary,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
            }
        }
    }
}

using FlaxEngine;
using FlaxEngine.GUI;
using Game.Character.Attributes;
using HundunWorld.Game.UI.StyleSystem;
using System;

namespace HundunWorld.Game.UI.Ink.Pages.Crafting
{
    /// <summary>
    /// 制造技艺面板 — 对应设计方案 crafting.html。
    /// 1400x900 居中面板：顶栏（标题+搜索+返回）+ 左栏（采集/制造Tab+技艺列表）
    /// + 中栏（配方网格+配方详情）+ 右栏（预览+属性+进度+批量+日志）。
    /// 严格遵循水墨主题 Token，禁止硬编码色值。
    /// </summary>
    public class CraftingPage : ContainerControl, IInkPage
    {
        private static readonly Float2 MainPanelSize = new Float2(1400f, 900f);
        private const float TopBarHeight = 52f;
        private const float LeftWidth = 280f;
        private const float RightWidth = 320f;
        private const float TabHeight = 44f;
        private const float SectionHeadHeight = 44f;
        private const float RecipeListHeight = 340f;
        private const float Pad = 12f;

        public event Action<string> NavigationRequested;
        public InkParticleSystem ParticleSystem { get; set; }

        private CharacterAttributesComponent _boundCharacter;

        private InkPanelElevated _mainPanel;
        private InkButton _backBtn;

        // 左栏
        private ContainerControl _leftCol;
        private CraftTab _tabGather;
        private CraftTab _tabCraft;
        private ContainerControl _skillList;

        // 中栏
        private ContainerControl _midCol;
        private ContainerControl _recipeGrid;
        private RecipeCard[] _recipeCards;
        private int _selectedRecipe = 0;

        // 右栏
        private ContainerControl _rightCol;
        private BatchBtn[] _batchBtns;

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
                BuildLeftColumn();
                BuildMiddleColumn();
                BuildRightColumn();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[CraftingPage] init failed: {ex.Message}");
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
        // 顶栏：标题 + 副标题 + 搜索 + 返回
        // ===================================================================

        private void BuildTopBar()
        {
            var topBar = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Float2(MainPanelSize.X, TopBarHeight),
                BackgroundColor = Color.Transparent,
            };
            _mainPanel.AddChild(topBar);

            // 底部金边
            topBar.AddChild(new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, TopBarHeight - 1f),
                Size = new Float2(MainPanelSize.X, 1f),
                BackgroundColor = InkWashTheme.BorderGold,
            });

            topBar.AddChild(MakeLabel("制造技艺", 24f, 0f, 140f, TopBarHeight,
                InkWashTheme.GoldPrimary, 22f, InkWashTheme.FontRole.Display, TextAlignment.Near));
            topBar.AddChild(MakeLabel("江湖百业工坊", 180f, 0f, 140f, TopBarHeight,
                InkWashTheme.TextTertiary, 12f, InkWashTheme.FontRole.Body, TextAlignment.Near));

            var searchBtn = new InkButton
            {
                Variant = InkButtonVariant.Secondary,
                ButtonSize = InkButtonSize.Sm,
                Text = "搜索配方",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(MainPanelSize.X - 24f - 36f - 8f - 100f, (TopBarHeight - 28f) * 0.5f),
                Size = new Float2(100f, 28f),
            };
            topBar.AddChild(searchBtn);

            _backBtn = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Sm,
                Text = "←",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(MainPanelSize.X - 24f - 36f, (TopBarHeight - 28f) * 0.5f),
                Size = new Float2(36f, 28f),
            };
            _backBtn.ButtonClicked += (b) => NavigationRequested?.Invoke(InkPageDomIds.CombatHud);
            topBar.AddChild(_backBtn);
        }

        // ===================================================================
        // 左栏：采集/制造 Tab + 技艺列表
        // ===================================================================

        private void BuildLeftColumn()
        {
            _leftCol = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, TopBarHeight),
                Size = new Float2(LeftWidth, MainPanelSize.Y - TopBarHeight),
                BackgroundColor = Color.Transparent,
            };
            _mainPanel.AddChild(_leftCol);

            // 右边框金线
            _leftCol.AddChild(new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(LeftWidth - 1f, 0f),
                Size = new Float2(1f, MainPanelSize.Y - TopBarHeight),
                BackgroundColor = InkWashTheme.BorderGold,
            });

            // Tab 切换：采集 / 制造
            _tabGather = new CraftTab("采集", false)
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Float2(LeftWidth * 0.5f, TabHeight),
            };
            _leftCol.AddChild(_tabGather);

            _tabCraft = new CraftTab("制造", true)
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(LeftWidth * 0.5f, 0f),
                Size = new Float2(LeftWidth * 0.5f, TabHeight),
            };
            _leftCol.AddChild(_tabCraft);

            // Tab 底部金边
            _leftCol.AddChild(new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, TabHeight - 1f),
                Size = new Float2(LeftWidth, 1f),
                BackgroundColor = InkWashTheme.BorderGold,
            });

            // 技艺列表（制造技艺）
            _skillList = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, TabHeight),
                Size = new Float2(LeftWidth, MainPanelSize.Y - TopBarHeight - TabHeight),
                BackgroundColor = Color.Transparent,
            };
            _leftCol.AddChild(_skillList);

            var skills = new (string elemChar, InkWashTheme.InkElement elem, string name,
                              string sub, int lvl, float prog)[]
            {
                ("金", InkWashTheme.InkElement.Metal, "锻造", "武器·防具", 40, 0.40f),
                ("木", InkWashTheme.InkElement.Wood,  "制药", "丹药",       35, 0.35f),
                ("水", InkWashTheme.InkElement.Water, "织造", "衣袍",       28, 0.28f),
                ("火", InkWashTheme.InkElement.Fire,  "烹饪", "食物",       42, 0.42f),
                ("金", InkWashTheme.InkElement.Metal, "机关", "暗器·道具", 20, 0.20f),
            };

            float itemW = LeftWidth - Pad * 2f;
            float itemH = 72f;
            float cy = Pad;
            for (int i = 0; i < skills.Length; i++)
            {
                var s = skills[i];
                bool isActive = (i == 0);
                Color ec = InkWashTheme.ElementColor(s.elem);

                var item = new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(Pad, cy),
                    Size = new Float2(itemW, itemH),
                    BackgroundColor = Color.Transparent,
                };
                _skillList.AddChild(item);

                Color bg = isActive ? InkWashTheme.GoldTrace : Color.Transparent;
                Color border = isActive ? InkWashTheme.BorderGold : Color.Transparent;
                item.AddChild(new CraftBox(bg, border, 6f));

                // 元素徽章
                item.AddChild(new ElemBadge(s.elemChar, ec) { Location = new Float2(Pad, Pad) });

                item.AddChild(MakeLabel(s.name, 40f, Pad, 130f, 20f,
                    InkWashTheme.TextDefault, 14f, InkWashTheme.FontRole.Body, TextAlignment.Near));
                item.AddChild(MakeLabel("Lv." + s.lvl, itemW - 70f, Pad, 58f, 20f,
                    InkWashTheme.TextTertiary, 12f, InkWashTheme.FontRole.Number, TextAlignment.Far));
                item.AddChild(MakeLabel(s.sub, 40f, 36f, itemW - 52f, 16f,
                    InkWashTheme.TextTertiary, 11f, InkWashTheme.FontRole.Body, TextAlignment.Near));

                // 进度条
                float barW = itemW - Pad * 2f;
                var barTrack = new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(Pad, 56f),
                    Size = new Float2(barW, 4f),
                    BackgroundColor = InkWashTheme.GoldTrace,
                };
                item.AddChild(barTrack);
                barTrack.AddChild(new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = Float2.Zero,
                    Size = new Float2(barW * s.prog, 4f),
                    BackgroundColor = ec,
                });

                cy += itemH + 8f;
            }
        }

        // ===================================================================
        // 中栏：配方网格 + 配方详情
        // ===================================================================

        private void BuildMiddleColumn()
        {
            float midW = MainPanelSize.X - LeftWidth - RightWidth;
            _midCol = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(LeftWidth, TopBarHeight),
                Size = new Float2(midW, MainPanelSize.Y - TopBarHeight),
                BackgroundColor = Color.Transparent,
            };
            _mainPanel.AddChild(_midCol);

            BuildRecipeList(midW);
            BuildRecipeDetail(midW);
        }

        private void BuildRecipeList(float midW)
        {
            // 配方列表区（340px，底部金边）
            var listWrap = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Float2(midW, RecipeListHeight),
                BackgroundColor = Color.Transparent,
            };
            _midCol.AddChild(listWrap);
            listWrap.AddChild(new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, RecipeListHeight - 1f),
                Size = new Float2(midW, 1f),
                BackgroundColor = InkWashTheme.BorderGold,
            });

            // 区头：锻造配方 + 8 个配方 + 全部
            listWrap.AddChild(MakeLabel("锻造配方", 20f, 0f, 160f, SectionHeadHeight,
                InkWashTheme.GoldPrimary, 16f, InkWashTheme.FontRole.Display, TextAlignment.Near));
            listWrap.AddChild(MakeLabel("8 个配方", midW - 20f - 150f, 0f, 80f, SectionHeadHeight,
                InkWashTheme.TextTertiary, 12f, InkWashTheme.FontRole.Body, TextAlignment.Far));
            var filterBtn = new TagBox("全部", InkWashTheme.TextSecondary, InkWashTheme.BorderFaint, Color.Transparent)
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(midW - 20f - 60f, (SectionHeadHeight - 24f) * 0.5f),
                Size = new Float2(60f, 24f),
            };
            listWrap.AddChild(filterBtn);

            // 配方网格（4 列 x 2 行）
            _recipeGrid = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, SectionHeadHeight),
                Size = new Float2(midW, RecipeListHeight - SectionHeadHeight),
                BackgroundColor = Color.Transparent,
            };
            listWrap.AddChild(_recipeGrid);

            var recipes = new (string glyph, string name, string lvl,
                               InkWashTheme.InkQuality q, bool locked)[]
            {
                ("剑", "玄铁剑",   "40", InkWashTheme.InkQuality.Legendary, false),
                ("刀", "寒铁刀",   "35", InkWashTheme.InkQuality.Epic,      false),
                ("盾", "玄铁盾",   "30", InkWashTheme.InkQuality.Rare,      false),
                ("腕", "精铁护腕", "25", InkWashTheme.InkQuality.Rare,      false),
                ("盔", "精钢头盔", "20", InkWashTheme.InkQuality.Uncommon,  false),
                ("枪", "铁质长枪", "10", InkWashTheme.InkQuality.Common,    false),
                ("匕", "青铜匕首", "5",  InkWashTheme.InkQuality.Common,    false),
                ("甲", "寒铁战甲", "45", InkWashTheme.InkQuality.Common,    true),
            };

            int cols = 4;
            float gridPadX = 20f;
            float gap = 12f;
            float cardW = (midW - gridPadX * 2f - gap * (cols - 1)) / cols;
            float cardH = 100f;

            _recipeCards = new RecipeCard[recipes.Length];
            for (int i = 0; i < recipes.Length; i++)
            {
                var r = recipes[i];
                int col = i % cols;
                int row = i / cols;
                float cx = gridPadX + col * (cardW + gap);
                float cyy = row * (cardH + gap);

                var card = new RecipeCard(r.glyph, r.name, "Lv." + r.lvl, r.q, r.locked, i == 0)
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(cx, cyy),
                    Size = new Float2(cardW, cardH),
                };
                int captured = i;
                card.Clicked += () => OnRecipeClicked(captured);
                _recipeCards[i] = card;
                _recipeGrid.AddChild(card);
            }
        }

        private void BuildRecipeDetail(float midW)
        {
            float detailTop = RecipeListHeight;
            var detail = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, detailTop),
                Size = new Float2(midW, MainPanelSize.Y - TopBarHeight - detailTop),
                BackgroundColor = Color.Transparent,
            };
            _midCol.AddChild(detail);

            // 区头：配方详情 + 可制 0 个
            detail.AddChild(MakeLabel("配方详情", 20f, 0f, 160f, SectionHeadHeight,
                InkWashTheme.GoldPrimary, 16f, InkWashTheme.FontRole.Display, TextAlignment.Near));
            detail.AddChild(MakeLabel("可制 0 个", midW - 20f - 120f, 0f, 120f, SectionHeadHeight,
                InkWashTheme.TextSecondary, 13f, InkWashTheme.FontRole.Body, TextAlignment.Far));

            float cy = SectionHeadHeight + 8f;

            // 详情头：图标 + 名称 + 标签
            var iconBox = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(20f, cy),
                Size = new Float2(56f, 56f),
                BackgroundColor = Color.Transparent,
            };
            detail.AddChild(iconBox);
            iconBox.AddChild(new CraftBox(InkWashTheme.GoldFaint, InkWashTheme.BorderGold, 8f));
            iconBox.AddChild(MakeLabel("剑", 0f, 0f, 56f, 56f,
                InkWashTheme.QualityLegendary, 36f, InkWashTheme.FontRole.Display, TextAlignment.Center));

            detail.AddChild(MakeLabel("玄铁剑", 92f, cy, 200f, 26f,
                InkWashTheme.TextDefault, 18f, InkWashTheme.FontRole.Display, TextAlignment.Near));

            float tagX = 92f;
            var tagDefs = new (string text, Color tc, Color bc, Color bg)[]
            {
                ("传说", InkWashTheme.GoldBright, InkWashTheme.QualityLegendary, InkWashTheme.GoldTrace),
                ("武器·剑", InkWashTheme.TextSecondary, InkWashTheme.BorderFaint, Color.Transparent),
                ("五行·金", InkWashTheme.ElementMetal, InkWashTheme.ElementMetal, Color.Transparent),
            };
            foreach (var t in tagDefs)
            {
                var tag = new TagBox(t.text, t.tc, t.bc, t.bg)
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(tagX, cy + 30f),
                    Size = new Float2(64f, 20f),
                };
                detail.AddChild(tag);
                tagX += 64f + 6f;
            }
            cy += 56f + 16f;

            // 所需材料
            detail.AddChild(MakeLabel("所需材料", 20f, cy, 160f, 20f,
                InkWashTheme.TextSecondary, 13f, InkWashTheme.FontRole.Body, TextAlignment.Near));
            cy += 20f + 8f;

            var mats = new (string glyph, string name, string qty, bool enough,
                            InkWashTheme.InkQuality q)[]
            {
                ("矿", "玄铁矿",   "5/3", false, InkWashTheme.InkQuality.Rare),
                ("锭", "寒铁锭",   "3/5", true,  InkWashTheme.InkQuality.Epic),
                ("焰", "火灵石",   "1/2", true,  InkWashTheme.InkQuality.Rare),
                ("炭", "千年木炭", "2/4", true,  InkWashTheme.InkQuality.Common),
            };
            float rowW = midW - 40f;
            foreach (var m in mats)
            {
                var row = new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(20f, cy),
                    Size = new Float2(rowW, 44f),
                    BackgroundColor = Color.Transparent,
                };
                detail.AddChild(row);
                row.AddChild(new CraftBox(InkWashTheme.BaseElevated, InkWashTheme.BorderFaint, 6f));
                row.AddChild(new MatIconBox(m.glyph, InkWashTheme.QualityColor(m.q))
                {
                    Location = new Float2(12f, 8f),
                });
                row.AddChild(MakeLabel(m.name, 50f, 0f, 300f, 44f,
                    InkWashTheme.TextDefault, 13f, InkWashTheme.FontRole.Body, TextAlignment.Near));
                row.AddChild(MakeLabel(m.qty, rowW - 130f, 0f, 70f, 44f,
                    m.enough ? InkWashTheme.JadeBright : InkWashTheme.BloodBright,
                    13f, InkWashTheme.FontRole.Number, TextAlignment.Far));
                row.AddChild(new StatusPill(m.enough)
                {
                    Location = new Float2(rowW - 56f, 12f),
                });
                cy += 44f + 6f;
            }
            cy += 10f;

            // 信息网格（2 列 x 2 行）
            var infos = new (string label, string value, bool jade)[]
            {
                ("制作时间", "30 秒",   false),
                ("成功率",   "85%",     true),
                ("银两消耗", "2,000 两", false),
                ("技艺经验", "+150",    false),
            };
            float infoW = (midW - 40f - 8f) * 0.5f;
            for (int i = 0; i < infos.Length; i++)
            {
                var info = infos[i];
                int col = i % 2;
                int row = i / 2;
                float ix = 20f + col * (infoW + 8f);
                float iy = cy + row * 48f;

                var item = new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(ix, iy),
                    Size = new Float2(infoW, 40f),
                    BackgroundColor = Color.Transparent,
                };
                detail.AddChild(item);
                item.AddChild(new CraftBox(InkWashTheme.BaseElevated, InkWashTheme.BorderFaint, 6f));
                item.AddChild(MakeLabel(info.label, 12f, 0f, 120f, 40f,
                    InkWashTheme.TextTertiary, 12f, InkWashTheme.FontRole.Body, TextAlignment.Near));
                item.AddChild(MakeLabel(info.value, infoW - 100f, 0f, 88f, 40f,
                    info.jade ? InkWashTheme.JadeBright : InkWashTheme.TextDefault,
                    13f, InkWashTheme.FontRole.Number, TextAlignment.Far));
            }
        }

        // ===================================================================
        // 右栏：预览 + 属性 + 进度 + 批量 + 日志
        // ===================================================================

        private void BuildRightColumn()
        {
            float benchX = MainPanelSize.X - RightWidth;
            _rightCol = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(benchX, TopBarHeight),
                Size = new Float2(RightWidth, MainPanelSize.Y - TopBarHeight),
                BackgroundColor = Color.Transparent,
            };
            _mainPanel.AddChild(_rightCol);

            // 左边框金线
            _rightCol.AddChild(new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Float2(1f, MainPanelSize.Y - TopBarHeight),
                BackgroundColor = InkWashTheme.BorderGold,
            });

            float innerW = RightWidth - 32f;
            float cy = 0f;

            // 预览区
            var preview = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, cy),
                Size = new Float2(RightWidth, 190f),
                BackgroundColor = Color.Transparent,
            };
            _rightCol.AddChild(preview);
            preview.AddChild(new PreviewGlow
            {
                Location = new Float2((RightWidth - 160f) * 0.5f, 15f),
            });
            preview.AddChild(MakeLabel("剑", 0f, 30f, RightWidth, 70f,
                InkWashTheme.QualityLegendary, 64f, InkWashTheme.FontRole.Display, TextAlignment.Center));
            preview.AddChild(MakeLabel("玄铁剑", 0f, 104f, RightWidth, 24f,
                InkWashTheme.GoldPrimary, 18f, InkWashTheme.FontRole.Display, TextAlignment.Center));

            float ptagX = (RightWidth - 134f) * 0.5f;
            var ptag1 = new TagBox("传说", InkWashTheme.GoldBright, InkWashTheme.QualityLegendary, InkWashTheme.GoldTrace)
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(ptagX, 134f),
                Size = new Float2(64f, 20f),
            };
            preview.AddChild(ptag1);
            var ptag2 = new TagBox("武器·剑", InkWashTheme.TextSecondary, InkWashTheme.BorderFaint, Color.Transparent)
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(ptagX + 70f, 134f),
                Size = new Float2(64f, 20f),
            };
            preview.AddChild(ptag2);
            cy += 190f;

            // 属性区
            var attrSection = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, cy),
                Size = new Float2(RightWidth, 130f),
                BackgroundColor = Color.Transparent,
            };
            _rightCol.AddChild(attrSection);
            attrSection.AddChild(new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Float2(RightWidth, 1f),
                BackgroundColor = InkWashTheme.BorderFaint,
            });
            attrSection.AddChild(new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 129f),
                Size = new Float2(RightWidth, 1f),
                BackgroundColor = InkWashTheme.BorderFaint,
            });

            var attrs = new (string label, string value, Color vc)[]
            {
                ("五行",   "金",    InkWashTheme.ElementMetal),
                ("攻击力", "+120",  InkWashTheme.TextDefault),
                ("暴击率", "+5%",   InkWashTheme.TextDefault),
                ("会心率", "+3%",   InkWashTheme.JadeBright),
            };
            float ay = 13f;
            foreach (var a in attrs)
            {
                attrSection.AddChild(MakeLabel(a.label, 16f, ay, 100f, 22f,
                    InkWashTheme.TextTertiary, 13f, InkWashTheme.FontRole.Body, TextAlignment.Near));
                attrSection.AddChild(MakeLabel(a.value, RightWidth - 16f - 80f, ay, 80f, 22f,
                    a.vc, 14f, InkWashTheme.FontRole.Number, TextAlignment.Far));
                ay += 28f;
            }
            cy += 130f;

            // 进度区
            var progSection = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, cy),
                Size = new Float2(RightWidth, 54f),
                BackgroundColor = Color.Transparent,
            };
            _rightCol.AddChild(progSection);
            progSection.AddChild(new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 53f),
                Size = new Float2(RightWidth, 1f),
                BackgroundColor = InkWashTheme.BorderFaint,
            });
            progSection.AddChild(MakeLabel("正在制作", 16f, 12f, 60f, 20f,
                InkWashTheme.TextTertiary, 11f, InkWashTheme.FontRole.Body, TextAlignment.Near));
            progSection.AddChild(MakeLabel("精铁护腕", 78f, 12f, innerW - 78f - 44f, 20f,
                InkWashTheme.TextDefault, 13f, InkWashTheme.FontRole.Body, TextAlignment.Near));
            progSection.AddChild(MakeLabel("65%", RightWidth - 16f - 44f, 12f, 44f, 20f,
                InkWashTheme.GoldBright, 12f, InkWashTheme.FontRole.Number, TextAlignment.Far));

            var progTrack = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, 38f),
                Size = new Float2(innerW, 4f),
                BackgroundColor = InkWashTheme.GoldTrace,
            };
            progSection.AddChild(progTrack);
            progTrack.AddChild(new HGradientBar(0.65f,
                InkWashTheme.GoldDeep, InkWashTheme.GoldPrimary, InkWashTheme.GoldBright)
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Float2(innerW, 4f),
            });
            cy += 54f;

            // 操作区：批量 + 制造按钮
            var actionSection = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, cy),
                Size = new Float2(RightWidth, 104f),
                BackgroundColor = Color.Transparent,
            };
            _rightCol.AddChild(actionSection);
            actionSection.AddChild(new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 103f),
                Size = new Float2(RightWidth, 1f),
                BackgroundColor = InkWashTheme.BorderFaint,
            });
            actionSection.AddChild(MakeLabel("批量", 16f, 12f, 40f, 30f,
                InkWashTheme.TextTertiary, 12f, InkWashTheme.FontRole.Body, TextAlignment.Near));

            _batchBtns = new BatchBtn[3];
            string[] batchVals = { "1", "5", "10" };
            float bx = 64f;
            for (int i = 0; i < 3; i++)
            {
                var btn = new BatchBtn(batchVals[i], i == 0)
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(bx, 12f),
                    Size = new Float2(74f, 30f),
                };
                int captured = i;
                btn.Clicked += () => OnBatchClicked(captured);
                _batchBtns[i] = btn;
                actionSection.AddChild(btn);
                bx += 74f + 8f;
            }

            var craftBtn = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, 52f),
                Size = new Float2(innerW, 40f),
                BackgroundColor = Color.Transparent,
            };
            actionSection.AddChild(craftBtn);
            craftBtn.AddChild(new CraftBox(InkWashTheme.GoldDeep, Color.Transparent, 6f));
            craftBtn.AddChild(MakeLabel("材料不足 · 2,000 两", 0f, 0f, innerW, 40f,
                InkWashTheme.TextTertiary, 14f, InkWashTheme.FontRole.Body, TextAlignment.Center));
            cy += 104f;

            // 制作日志
            var logSection = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, cy),
                Size = new Float2(RightWidth, MainPanelSize.Y - TopBarHeight - cy),
                BackgroundColor = Color.Transparent,
            };
            _rightCol.AddChild(logSection);
            logSection.AddChild(MakeLabel("制作日志", 16f, 12f, 120f, 20f,
                InkWashTheme.TextSecondary, 13f, InkWashTheme.FontRole.Body, TextAlignment.Near));

            var logs = new (string name, string time, bool success)[]
            {
                ("精铁护腕", "14:32", true),
                ("铁质长枪", "14:25", true),
                ("铁质长枪", "14:18", true),
                ("寒铁刀",   "14:10", false),
                ("铁质长枪", "14:02", true),
            };
            float ly = 40f;
            foreach (var l in logs)
            {
                var row = new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(16f, ly),
                    Size = new Float2(innerW, 24f),
                    BackgroundColor = Color.Transparent,
                };
                logSection.AddChild(row);
                row.AddChild(new CraftBox(InkWashTheme.BaseElevated, Color.Transparent, 4f));
                row.AddChild(MakeLabel(l.success ? "✓" : "✕", 8f, 0f, 14f, 24f,
                    l.success ? InkWashTheme.JadeBright : InkWashTheme.BloodBright,
                    12f, InkWashTheme.FontRole.Body, TextAlignment.Center));
                row.AddChild(MakeLabel(l.name, 28f, 0f, innerW - 28f - 48f, 24f,
                    InkWashTheme.TextDefault, 12f, InkWashTheme.FontRole.Body, TextAlignment.Near));
                row.AddChild(MakeLabel(l.time, innerW - 44f, 0f, 40f, 24f,
                    InkWashTheme.TextTertiary, 11f, InkWashTheme.FontRole.Number, TextAlignment.Far));
                ly += 24f + 4f;
            }
        }

        // ===================================================================
        // 事件处理
        // ===================================================================

        private void OnRecipeClicked(int index)
        {
            if (_recipeCards == null) return;
            for (int i = 0; i < _recipeCards.Length; i++)
                _recipeCards[i].IsSelected = (i == index);
            _selectedRecipe = index;
        }

        private void OnBatchClicked(int index)
        {
            if (_batchBtns == null) return;
            for (int i = 0; i < _batchBtns.Length; i++)
                _batchBtns[i].IsActive = (i == index);
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
                    float px = (sw - MainPanelSize.X) * 0.5f;
                    float py = (sh - MainPanelSize.Y) * 0.5f;
                    _mainPanel.Location = new Float2(px > 0f ? px : 0f, py > 0f ? py : 0f);
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[CraftingPage] RefreshLayout failed: {ex.Message}");
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

        /// <summary>自绘圆角背景 + 边框（StretchAll）。</summary>
        private sealed class CraftBox : Control
        {
            private readonly Color _bg;
            private readonly Color _border;
            private readonly float _radius;

            public CraftBox(Color bg, Color border, float radius)
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

        /// <summary>采集/制造 Tab（14px，激活金色 + 2px 下划线）。</summary>
        private sealed class CraftTab : Control
        {
            private readonly string _text;
            private bool _isActive;
            private bool _isHovered;

            public event Action Clicked;
            public bool IsActive { get => _isActive; set => _isActive = value; }

            public CraftTab(string text, bool active)
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
                    : (_isHovered ? InkWashTheme.TextSecondary : InkWashTheme.TextTertiary);
                var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 14f).GetFont();
                if (font != null)
                    Render2D.DrawText(font, _text, new Rectangle(Float2.Zero, Size), color,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
                if (_isActive)
                {
                    float lineW = Width * 0.5f;
                    Render2D.FillRectangle(new Rectangle((Width - lineW) * 0.5f, Height - 2f, lineW, 2f),
                        InkWashTheme.GoldPrimary);
                }
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

        /// <summary>元素徽章（20x20：元素色底 + void 字）。</summary>
        private sealed class ElemBadge : Control
        {
            private readonly string _char;
            private readonly Color _color;

            public ElemBadge(string elemChar, Color elemColor)
            {
                _char = elemChar;
                _color = elemColor;
                AutoFocus = false;
                Size = new Float2(20f, 20f);
            }

            public override void Draw()
            {
                base.Draw();
                if (!Visible) return;
                InkRenderHelper.FillRoundedRectangle(new Rectangle(Float2.Zero, Size), 4f, _color);
                var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f).GetFont();
                if (font != null)
                    Render2D.DrawText(font, _char, new Rectangle(Float2.Zero, Size), InkWashTheme.Void,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
            }
        }

        /// <summary>配方卡片（品质边框 + 图标字 + 名 + 等级，选中金辉/锁定灰）。</summary>
        private sealed class RecipeCard : Control
        {
            private readonly string _glyph;
            private readonly string _name;
            private readonly string _req;
            private readonly InkWashTheme.InkQuality _quality;
            private readonly bool _locked;
            private bool _isSelected;
            private bool _isHovered;

            public event Action Clicked;
            public bool IsSelected { get => _isSelected; set => _isSelected = value; }

            public RecipeCard(string glyph, string name, string req,
                InkWashTheme.InkQuality quality, bool locked, bool selected)
            {
                _glyph = glyph;
                _name = name;
                _req = req;
                _quality = quality;
                _locked = locked;
                _isSelected = selected;
                AutoFocus = false;
            }

            public override void Draw()
            {
                base.Draw();
                if (!Visible) return;
                var rect = new Rectangle(Float2.Zero, Size);
                Color qc = InkWashTheme.QualityColor(_quality);

                if (_locked)
                {
                    var bg = new Color(InkWashTheme.BaseElevated.R, InkWashTheme.BaseElevated.G,
                        InkWashTheme.BaseElevated.B, 0.5f);
                    InkRenderHelper.FillRoundedRectangle(rect, 6f, bg);
                    InkRenderHelper.DrawRoundedRectangle(rect, 6f, InkWashTheme.BorderFaint, 1f);
                }
                else if (_isSelected)
                {
                    InkRenderHelper.FillRoundedRectangle(rect, 6f, InkWashTheme.GoldTrace);
                    InkRenderHelper.DrawRoundedRectangle(rect, 6f, InkWashTheme.GoldPrimary, 1f);
                }
                else
                {
                    InkRenderHelper.FillRoundedRectangle(rect, 6f, InkWashTheme.BaseElevated);
                    InkRenderHelper.DrawRoundedRectangle(rect, 6f,
                        _isHovered ? InkWashTheme.BorderGoldBright : qc, 1f);
                }

                Color iconColor = _locked ? InkWashTheme.TextTertiary : qc;
                var iconFont = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 24f).GetFont();
                if (iconFont != null)
                    Render2D.DrawText(iconFont, _glyph, new Rectangle(0f, 12f, Width, 44f), iconColor,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);

                var nameFont = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f).GetFont();
                if (nameFont != null)
                    Render2D.DrawText(nameFont, _name, new Rectangle(4f, 60f, Width - 8f, 16f),
                        _locked ? InkWashTheme.TextTertiary : InkWashTheme.TextDefault,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);

                var reqFont = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 11f).GetFont();
                if (reqFont != null)
                    Render2D.DrawText(reqFont, _req, new Rectangle(4f, 78f, Width - 8f, 14f),
                        _locked ? InkWashTheme.BloodPrimary : InkWashTheme.TextTertiary,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
            }

            public override void OnMouseEnter(Float2 location) { _isHovered = true; base.OnMouseEnter(location); }
            public override void OnMouseLeave() { _isHovered = false; base.OnMouseLeave(); }

            public override bool OnMouseUp(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left && !_locked && ContainsPoint(ref location))
                    Clicked?.Invoke();
                return base.OnMouseUp(location, button);
            }
        }

        /// <summary>材料图标（28x28：void 底 + 品质边框 + 品质字）。</summary>
        private sealed class MatIconBox : Control
        {
            private readonly string _glyph;
            private readonly Color _qc;

            public MatIconBox(string glyph, Color qualityColor)
            {
                _glyph = glyph;
                _qc = qualityColor;
                AutoFocus = false;
                Size = new Float2(28f, 28f);
            }

            public override void Draw()
            {
                base.Draw();
                if (!Visible) return;
                var rect = new Rectangle(Float2.Zero, Size);
                InkRenderHelper.FillRoundedRectangle(rect, 4f, InkWashTheme.Void);
                InkRenderHelper.DrawRoundedRectangle(rect, 4f, _qc, 1f);
                var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 14f).GetFont();
                if (font != null)
                    Render2D.DrawText(font, _glyph, rect, _qc,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
            }
        }

        /// <summary>材料状态药丸（充足青 / 不足血）。</summary>
        private sealed class StatusPill : Control
        {
            private readonly bool _enough;

            public StatusPill(bool sufficient)
            {
                _enough = sufficient;
                AutoFocus = false;
                Size = new Float2(48f, 20f);
            }

            public override void Draw()
            {
                base.Draw();
                if (!Visible) return;
                var rect = new Rectangle(Float2.Zero, Size);
                Color bg = _enough ? InkWashTheme.JadeFaint : InkWashTheme.BloodFaint;
                Color tc = _enough ? InkWashTheme.JadeBright : InkWashTheme.BloodBright;
                InkRenderHelper.FillRoundedRectangle(rect, 4f, bg);
                var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f).GetFont();
                if (font != null)
                    Render2D.DrawText(font, _enough ? "充足" : "不足", rect, tc,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
            }
        }

        /// <summary>标签药丸（11px，radius4）。</summary>
        private sealed class TagBox : Control
        {
            private readonly string _text;
            private readonly Color _tc;
            private readonly Color _border;
            private readonly Color _bg;

            public TagBox(string text, Color textColor, Color border, Color bg)
            {
                _text = text;
                _tc = textColor;
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
                    Render2D.DrawText(font, _text, rect, _tc,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
            }
        }

        /// <summary>批量按钮（1/5/10，激活金）。</summary>
        private sealed class BatchBtn : Control
        {
            private readonly string _text;
            private bool _isActive;
            private bool _isHovered;

            public event Action Clicked;
            public bool IsActive { get => _isActive; set => _isActive = value; }

            public BatchBtn(string text, bool active)
            {
                _text = text;
                _isActive = active;
                AutoFocus = false;
            }

            public override void Draw()
            {
                base.Draw();
                if (!Visible) return;
                var rect = new Rectangle(Float2.Zero, Size);
                if (_isActive)
                {
                    InkRenderHelper.FillRoundedRectangle(rect, 4f, InkWashTheme.GoldTrace);
                    InkRenderHelper.DrawRoundedRectangle(rect, 4f, InkWashTheme.GoldPrimary, 1f);
                }
                else
                {
                    if (_isHovered)
                        InkRenderHelper.FillRoundedRectangle(rect, 4f, InkWashTheme.BgHover);
                    InkRenderHelper.DrawRoundedRectangle(rect, 4f, InkWashTheme.BorderFaint, 1f);
                }
                Color tc = _isActive ? InkWashTheme.GoldBright : InkWashTheme.TextSecondary;
                var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 13f).GetFont();
                if (font != null)
                    Render2D.DrawText(font, _text, rect, tc,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
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

        /// <summary>水平渐变进度条（按 fillRatio 填充）。</summary>
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

        /// <summary>预览区径向金辉（160px）。</summary>
        private sealed class PreviewGlow : Control
        {
            public PreviewGlow()
            {
                AutoFocus = false;
                Size = new Float2(160f, 160f);
            }

            public override void Draw()
            {
                base.Draw();
                if (!Visible) return;
                var center = new Float2(Width * 0.5f, Height * 0.5f);
                InkRenderHelper.FillRadialGradient(center, Width * 0.5f,
                    InkWashTheme.GoldGlow, Color.Transparent);
            }
        }
    }
}

using FlaxEngine;
using FlaxEngine.GUI;
using Game.Character.Attributes;
using HundunWorld.Game.UI.StyleSystem;
using System;

namespace HundunWorld.Game.UI.Ink.Pages.Inventory
{
    /// <summary>
    /// 背包行囊面板 — 对应设计方案 inventory.html。
    /// 1400x900 居中面板：顶栏（标题+4背包Tab+整理/仓库/关闭）+ 左8列网格（品质边框+强化/数量角标）
    /// + 右侧物品详情（大图标+标签+属性+套装+操作）+ 底部货币栏。
    /// 严格遵循水墨主题 Token，禁止硬编码色值。
    /// </summary>
    public class InventoryPage : ContainerControl, IInkPage
    {
        private static readonly Float2 MainPanelSize = new Float2(1400f, 900f);
        private const float HeaderHeight = 56f;
        private const float CurrencyHeight = 52f;
        private const float ContentPadX = 24f;
        private const float ContentPadY = 20f;
        private const float ContentGap = 24f;
        private const float GridSectionWidth = 800f;
        private const float CellSize = 64f;
        private const float CellGap = 12f;
        private const int GridCols = 8;
        private const int GridRows = 5;
        private const float GridFooterHeight = 44f;
        private const float DetailPad = 20f;

        public event Action<string> NavigationRequested;
        public InkParticleSystem ParticleSystem { get; set; }

        private CharacterAttributesComponent _boundCharacter;

        private InkPanelElevated _mainPanel;

        // 顶栏
        private ContainerControl _header;
        private InvTab[] _tabs;
        private ContainerControl _tabActiveLine;
        private InkButton _sortBtn;
        private InkButton _warehouseBtn;
        private InkButton _closeBtn;

        // 网格区
        private ContainerControl _gridSection;
        private ContainerControl _gridScroll;
        private InvCell[] _cells;
        private int _selectedCell = -1;
        private ContainerControl _capBarFill;

        // 详情区
        private ContainerControl _detail;
        private DetailIconBox _detailIcon;
        private Label _detailName;
        private TagBox _tagQuality;
        private TagBox _tagType;
        private TagBox _tagQty;
        private Label _metaEnhanceVal;
        private Label _metaElementVal;
        private Label _metaBindVal;
        private Label _metaDuraVal;
        private InkButton _useBtn;
        private InkButton _equipBtn;
        private InkButton _dropBtn;

        // 货币栏
        private ContainerControl _currencyBar;

        internal struct CellData
        {
            public InkWashTheme.InkQuality Quality;
            public string Glyph;
            public string Enhance;
            public string Qty;
            public bool IsQuest;
            public bool HasItem;
            public string Name;
            public string Type;
        }
        private CellData[] _cellData;

        public void BindCharacter(CharacterAttributesComponent component)
        {
            _boundCharacter = component;
        }

        public InventoryPage()
        {
            try
            {
                AnchorPreset = AnchorPresets.StretchAll;
                Offsets = Margin.Zero;
                BackgroundColor = InkWashTheme.Scrim;
                ClipChildren = false;
                AutoFocus = false;

                InitCellData();
                BuildMainPanel();
                BuildHeader();
                BuildGridSection();
                BuildDetailPanel();
                BuildCurrencyBar();
                SelectCell(0);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[InventoryPage] init failed: {ex.Message}");
            }
        }

        private void InitCellData()
        {
            _cellData = new CellData[GridCols * GridRows];
            // (glyph, name, type, quality, enhance, qty, quest)
            var items = new (string glyph, string name, string type, InkWashTheme.InkQuality q,
                             string enh, string qty, bool quest)[]
            {
                ("剑", "玄铁剑",   "武器", InkWashTheme.InkQuality.Legendary, "+12", null, false),
                ("刀", "寒铁刀",   "武器", InkWashTheme.InkQuality.Epic,      "+8",  null, false),
                ("腕", "精铁护腕", "防具", InkWashTheme.InkQuality.Rare,      "+5",  null, false),
                ("衣", "布衣",     "防具", InkWashTheme.InkQuality.Common,    null,  null, false),
                ("剑", "铁剑",     "武器", InkWashTheme.InkQuality.Common,    null,  null, false),
                ("甲", "皮甲",     "防具", InkWashTheme.InkQuality.Common,    null,  null, false),
                ("药", "生命药水", "丹药", InkWashTheme.InkQuality.Uncommon,  null,  "9", false),
                ("液", "内力药水", "丹药", InkWashTheme.InkQuality.Uncommon,  null,  "3", false),
                ("丹", "活血丹",   "丹药", InkWashTheme.InkQuality.Uncommon,  null,  "5", false),
                ("矿", "铁矿",     "材料", InkWashTheme.InkQuality.Common,    null,  "3", false),
                ("草", "草药",     "材料", InkWashTheme.InkQuality.Common,    null,  "5", false),
                ("皮", "兽皮",     "材料", InkWashTheme.InkQuality.Common,    null,  "2", false),
                ("矿", "铁矿",     "材料", InkWashTheme.InkQuality.Common,    null,  "4", false),
                ("草", "草药",     "材料", InkWashTheme.InkQuality.Common,    null,  "2", false),
                ("信", "密信",     "任务", InkWashTheme.InkQuality.Common,    null,  null, true),
            };
            // 按设计方案的行布局填充（前6格为第1行，随后跳过空位）
            int[] slots = { 0, 1, 2, 3, 4, 5, 8, 9, 10, 16, 17, 18, 19, 20, 24 };
            for (int i = 0; i < _cellData.Length; i++)
                _cellData[i] = new CellData { Quality = InkWashTheme.InkQuality.Common, HasItem = false };
            for (int k = 0; k < items.Length; k++)
            {
                var it = items[k];
                _cellData[slots[k]] = new CellData
                {
                    Quality = it.q,
                    Glyph = it.glyph,
                    Enhance = it.enh,
                    Qty = it.qty,
                    IsQuest = it.quest,
                    HasItem = true,
                    Name = it.name,
                    Type = it.type,
                };
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
        // 顶栏：标题 + 4 背包 Tab + 整理/仓库/关闭
        // ===================================================================

        private void BuildHeader()
        {
            _header = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Float2(MainPanelSize.X, HeaderHeight),
                BackgroundColor = Color.Transparent,
            };
            _mainPanel.AddChild(_header);

            // 底边框 gold-subtle
            _header.AddChild(new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, HeaderHeight - 1f),
                Size = new Float2(MainPanelSize.X, 1f),
                BackgroundColor = InkWashTheme.BorderGoldSubtle,
            });

            // 标题“行囊”（22px 楷书金色，letter-spacing 6px，右侧 1px 分隔线）
            _header.AddChild(MakeLabel("行囊", ContentPadX, 0f, 60f, HeaderHeight,
                InkWashTheme.GoldPrimary, 22f, InkWashTheme.FontRole.Display, TextAlignment.Near));
            _header.AddChild(new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(ContentPadX + 60f + 16f, (HeaderHeight - 22f) * 0.5f),
                Size = new Float2(1f, 22f),
                BackgroundColor = InkWashTheme.BorderGoldSubtle,
            });

            // Tab：主背包/材料背包/任务背包/时装背包（gap 28，14px 黑体）
            string[] tabNames = { "主背包", "材料背包", "任务背包", "时装背包" };
            _tabs = new InvTab[tabNames.Length];
            float tabX = ContentPadX + 60f + 16f + 1f + 32f;
            const float tabW = 70f;
            const float tabGap = 28f;
            for (int i = 0; i < tabNames.Length; i++)
            {
                var tab = new InvTab(tabNames[i], i == 0)
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(tabX, 0f),
                    Size = new Float2(tabW, HeaderHeight),
                };
                int captured = i;
                tab.Clicked += () => OnTabClicked(captured);
                _tabs[i] = tab;
                _header.AddChild(tab);
                tabX += tabW + tabGap;
            }

            // 激活下划线 2px 金色
            _tabActiveLine = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(ContentPadX + 60f + 16f + 1f + 32f, HeaderHeight - 2f),
                Size = new Float2(tabW, 2f),
                BackgroundColor = InkWashTheme.GoldPrimary,
            };
            _header.AddChild(_tabActiveLine);

            // 右侧按钮：关闭（ghost 36x36）+ 仓库 + 整理（secondary sm）
            _closeBtn = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Sm,
                Text = "✕",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(MainPanelSize.X - ContentPadX - 36f, (HeaderHeight - 36f) * 0.5f),
                Size = new Float2(36f, 36f),
                BorderColor = InkWashTheme.BorderGoldSubtle,
                BorderThickness = 1f,
            };
            _closeBtn.ButtonClicked += (b) => OnCloseClicked();
            _header.AddChild(_closeBtn);

            _warehouseBtn = new InkButton
            {
                Variant = InkButtonVariant.Secondary,
                ButtonSize = InkButtonSize.Sm,
                Text = "仓库",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(MainPanelSize.X - ContentPadX - 36f - 12f - 64f, (HeaderHeight - 24f) * 0.5f),
                Size = new Float2(64f, 24f),
            };
            _warehouseBtn.ButtonClicked += (b) => EmitGoldAtButton(b);
            _header.AddChild(_warehouseBtn);

            _sortBtn = new InkButton
            {
                Variant = InkButtonVariant.Secondary,
                ButtonSize = InkButtonSize.Sm,
                Text = "整理",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(MainPanelSize.X - ContentPadX - 36f - 12f - 64f - 12f - 64f, (HeaderHeight - 24f) * 0.5f),
                Size = new Float2(64f, 24f),
            };
            _sortBtn.ButtonClicked += (b) => EmitGoldAtButton(b);
            _header.AddChild(_sortBtn);
        }

        // ===================================================================
        // 网格区：8 列 x 5 行格子 + 容量页脚
        // ===================================================================

        private void BuildGridSection()
        {
            float contentTop = HeaderHeight + ContentPadY;
            float contentBottom = MainPanelSize.Y - CurrencyHeight - ContentPadY;
            float sectionH = contentBottom - contentTop;

            _gridSection = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(ContentPadX, contentTop),
                Size = new Float2(GridSectionWidth, sectionH),
                BackgroundColor = Color.Transparent,
            };
            _mainPanel.AddChild(_gridSection);

            // 网格滚动区（居中放置 8x5 格子）
            float footerH = GridFooterHeight;
            float scrollH = sectionH - footerH - 16f;
            _gridScroll = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Float2(GridSectionWidth, scrollH),
                BackgroundColor = Color.Transparent,
            };
            _gridSection.AddChild(_gridScroll);

            float gridW = CellSize * GridCols + CellGap * (GridCols - 1);
            float gridH = CellSize * GridRows + CellGap * (GridRows - 1);
            float startX = (GridSectionWidth - gridW) * 0.5f;
            float startY = (scrollH - gridH) * 0.5f;

            _cells = new InvCell[GridCols * GridRows];
            for (int i = 0; i < _cells.Length; i++)
            {
                int col = i % GridCols;
                int row = i / GridCols;
                var cell = new InvCell(_cellData[i])
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(startX + col * (CellSize + CellGap), startY + row * (CellSize + CellGap)),
                    Size = new Float2(CellSize, CellSize),
                };
                int captured = i;
                cell.Clicked += () => OnCellClicked(captured);
                _cells[i] = cell;
                _gridScroll.AddChild(cell);
            }

            // 容量页脚：格子 + 15/80 + 容量条
            float footerY = sectionH - footerH;
            var footer = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, footerY),
                Size = new Float2(GridSectionWidth, footerH),
                BackgroundColor = Color.Transparent,
            };
            _gridSection.AddChild(footer);

            footer.AddChild(new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 0f),
                Size = new Float2(GridSectionWidth, 1f),
                BackgroundColor = InkWashTheme.BorderFaint,
            });

            footer.AddChild(MakeLabel("格子", 12f, 0f, 40f, footerH,
                InkWashTheme.TextSecondary, 13f, InkWashTheme.FontRole.Body, TextAlignment.Near));
            footer.AddChild(MakeLabel("15/80", 56f, 0f, 60f, footerH,
                InkWashTheme.PaperBright, 14f, InkWashTheme.FontRole.Number, TextAlignment.Near));

            var capTrack = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(126f, (footerH - 4f) * 0.5f),
                Size = new Float2(GridSectionWidth - 126f - 12f, 4f),
                BackgroundColor = InkWashTheme.BorderFaint,
            };
            footer.AddChild(capTrack);

            _capBarFill = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Float2((GridSectionWidth - 138f) * 0.1875f, 4f),
                BackgroundColor = InkWashTheme.GoldPrimary,
            };
            capTrack.AddChild(_capBarFill);
        }

        // ===================================================================
        // 详情区：大图标 + 名称标签 + 属性 + 套装 + 操作按钮
        // ===================================================================

        private void BuildDetailPanel()
        {
            float contentTop = HeaderHeight + ContentPadY;
            float contentBottom = MainPanelSize.Y - CurrencyHeight - ContentPadY;
            float detailH = contentBottom - contentTop;
            float detailX = ContentPadX + GridSectionWidth + ContentGap;
            float detailW = MainPanelSize.X - ContentPadX * 2f - GridSectionWidth - ContentGap;

            _detail = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(detailX, contentTop),
                Size = new Float2(detailW, detailH),
                BackgroundColor = Color.Transparent,
            };
            _mainPanel.AddChild(_detail);
            _detail.AddChild(new RoundedBox(
                new Color(InkWashTheme.Void.R, InkWashTheme.Void.G, InkWashTheme.Void.B, 0.6f),
                new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.12f),
                8f));

            float innerW = detailW - DetailPad * 2f;
            float cx = DetailPad;
            float dy = DetailPad;

            // 大图标 120x120（居中，径向渐变金辉 + 金边）
            _detailIcon = new DetailIconBox("剑", InkWashTheme.QualityLegendary)
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(cx + (innerW - 120f) * 0.5f, dy),
                Size = new Float2(120f, 120f),
            };
            _detail.AddChild(_detailIcon);
            dy += 120f + 2f + 14f;

            // 名称 24px 楷书（品质色）
            _detailName = MakeLabel("玄铁剑", cx, dy, innerW, 30f,
                InkWashTheme.QualityLegendary, 24f, InkWashTheme.FontRole.Display, TextAlignment.Center);
            _detail.AddChild(_detailName);
            dy += 30f + 8f;

            // 标签行：传说 / 武器 / 数量 1
            float tagW = 56f;
            float qtyW = 72f;
            float tagsTotal = tagW * 2f + qtyW + 6f * 2f;
            float tagX = cx + (innerW - tagsTotal) * 0.5f;
            _tagQuality = new TagBox("传说",
                new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.12f),
                InkWashTheme.QualityLegendary,
                new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.25f))
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(tagX, dy),
                Size = new Float2(tagW, 22f),
            };
            _detail.AddChild(_tagQuality);
            _tagType = new TagBox("武器",
                new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.08f),
                InkWashTheme.TextSecondary,
                new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.15f))
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(tagX + tagW + 6f, dy),
                Size = new Float2(tagW, 22f),
            };
            _detail.AddChild(_tagType);
            _tagQty = new TagBox("数量 1",
                new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.08f),
                InkWashTheme.TextSecondary,
                new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.15f))
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(tagX + (tagW + 6f) * 2f, dy),
                Size = new Float2(qtyW, 22f),
            };
            _detail.AddChild(_tagQty);
            dy += 22f + 14f;

            // 元信息网格 2x2（强化/五行/绑定/耐久），上下发丝线
            var metaTop = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(cx, dy),
                Size = new Float2(innerW, 1f),
                BackgroundColor = InkWashTheme.BorderFaint,
            };
            _detail.AddChild(metaTop);
            dy += 1f + 12f;

            float halfW = innerW * 0.5f - 10f;
            AddMetaRow(_detail, cx, dy, halfW, "强化", out _metaEnhanceVal);
            _metaEnhanceVal.Text = "+12";
            _metaEnhanceVal.TextColor = InkWashTheme.GoldPrimary;
            AddMetaRow(_detail, cx + halfW + 20f, dy, halfW, "五行", out _metaElementVal);
            _metaElementVal.Text = "金";
            _metaElementVal.TextColor = InkWashTheme.ElementMetal;
            dy += 22f + 8f;
            AddMetaRow(_detail, cx, dy, halfW, "绑定", out _metaBindVal);
            _metaBindVal.Text = "拾取绑定";
            AddMetaRow(_detail, cx + halfW + 20f, dy, halfW, "耐久", out _metaDuraVal);
            _metaDuraVal.Text = "850/1000";
            dy += 22f + 12f;

            var metaBottom = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(cx, dy),
                Size = new Float2(innerW, 1f),
                BackgroundColor = InkWashTheme.BorderFaint,
            };
            _detail.AddChild(metaBottom);
            dy += 1f + 10f;

            // 耐久度条 4px（85%）
            var duraTrack = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(cx + 2f, dy),
                Size = new Float2(innerW - 4f, 4f),
                BackgroundColor = InkWashTheme.BorderFaint,
            };
            _detail.AddChild(duraTrack);
            duraTrack.AddChild(new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Float2((innerW - 4f) * 0.85f, 4f),
                BackgroundColor = InkWashTheme.GoldPrimary,
            });
            dy += 4f + 14f;

            // 基础属性
            dy = AddAttrSection(_detail, cx, dy, innerW, "基础属性",
                new[] { ("攻击力", "+120"), ("暴击率", "+5%") },
                new[] { InkWashTheme.PaperBright, InkWashTheme.PaperBright });

            // 附加属性
            dy = AddAttrSection(_detail, cx, dy, innerW, "附加属性",
                new[] { ("会心率", "+3%") },
                new[] { InkWashTheme.TextJade });

            // 套装效果
            _detail.AddChild(MakeSectionTitle("套装效果", cx, dy, innerW));
            dy += 20f + 4f + 8f;
            var setBox = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(cx, dy),
                Size = new Float2(innerW, 60f),
                BackgroundColor = Color.Transparent,
            };
            _detail.AddChild(setBox);
            setBox.AddChild(new RoundedBox(
                new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.06f),
                new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.12f),
                4f));
            setBox.AddChild(MakeLabel("◆", 12f, 8f, 14f, 18f,
                InkWashTheme.GoldPrimary, 11f, InkWashTheme.FontRole.Body, TextAlignment.Center));
            setBox.AddChild(MakeLabel("玄铁套", 30f, 8f, 80f, 18f,
                InkWashTheme.GoldPrimary, 13f, InkWashTheme.FontRole.Body, TextAlignment.Near));
            setBox.AddChild(MakeLabel("2/4", 112f, 8f, 40f, 18f,
                InkWashTheme.TextSecondary, 12f, InkWashTheme.FontRole.Number, TextAlignment.Near));
            setBox.AddChild(MakeLabel("2件套：攻击力 +10%", 12f, 32f, innerW - 24f, 18f,
                InkWashTheme.TextSecondary, 12f, InkWashTheme.FontRole.Body, TextAlignment.Near));

            // 操作按钮行（底部固定：使用/装备/丢弃）
            float actionY = detailH - DetailPad - 36f;
            var actionLine = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(cx, actionY - 12f - 1f),
                Size = new Float2(innerW, 1f),
                BackgroundColor = InkWashTheme.BorderFaint,
            };
            _detail.AddChild(actionLine);

            float btnW = (innerW - 12f * 2f) / 3f;
            _useBtn = new InkButton
            {
                Variant = InkButtonVariant.Brand,
                ButtonSize = InkButtonSize.Lg,
                Text = "使用",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(cx, actionY),
                Size = new Float2(btnW, 36f),
            };
            _useBtn.ButtonClicked += (b) => EmitGoldAtButton(b);
            _detail.AddChild(_useBtn);

            _equipBtn = new InkButton
            {
                Variant = InkButtonVariant.Secondary,
                ButtonSize = InkButtonSize.Lg,
                Text = "装备",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(cx + btnW + 12f, actionY),
                Size = new Float2(btnW, 36f),
            };
            _equipBtn.ButtonClicked += (b) => EmitGoldAtButton(b);
            _detail.AddChild(_equipBtn);

            _dropBtn = new InkButton
            {
                Variant = InkButtonVariant.Danger,
                ButtonSize = InkButtonSize.Lg,
                Text = "丢弃",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(cx + (btnW + 12f) * 2f, actionY),
                Size = new Float2(btnW, 36f),
            };
            _dropBtn.ButtonClicked += (b) => EmitGoldAtButton(b);
            _detail.AddChild(_dropBtn);
        }

        /// <summary>元信息行：标签（12px 弱色）+ 值（13px）。</summary>
        private void AddMetaRow(ContainerControl parent, float x, float y, float w, string label, out Label val)
        {
            parent.AddChild(MakeLabel(label, x, y, w * 0.4f, 22f,
                InkWashTheme.TextSecondary, 12f, InkWashTheme.FontRole.Body, TextAlignment.Near));
            val = MakeLabel("", x + w * 0.4f, y, w * 0.6f, 22f,
                InkWashTheme.PaperBright, 13f, InkWashTheme.FontRole.Number, TextAlignment.Far);
            parent.AddChild(val);
        }

        /// <summary>区块标题（14px 楷书金色 + 下划发丝线）。</summary>
        private ContainerControl MakeSectionTitle(string title, float x, float y, float w)
        {
            var c = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(x, y),
                Size = new Float2(w, 24f),
                BackgroundColor = Color.Transparent,
            };
            c.AddChild(MakeLabel(title, 0f, 0f, 120f, 20f,
                InkWashTheme.GoldPrimary, 14f, InkWashTheme.FontRole.Display, TextAlignment.Near));
            c.AddChild(new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 23f),
                Size = new Float2(w, 1f),
                BackgroundColor = InkWashTheme.BorderFaint,
            });
            return c;
        }

        /// <summary>属性区块：标题 + 属性行（标签 13px 弱色 + 值 14px 数字）。</summary>
        private float AddAttrSection(ContainerControl parent, float x, float y, float w,
            string title, (string label, string val)[] rows, Color[] valColors)
        {
            parent.AddChild(MakeSectionTitle(title, x, y, w));
            y += 20f + 4f + 8f;
            for (int i = 0; i < rows.Length; i++)
            {
                parent.AddChild(MakeLabel(rows[i].label, x, y, w * 0.5f, 20f,
                    InkWashTheme.TextSecondary, 13f, InkWashTheme.FontRole.Body, TextAlignment.Near));
                parent.AddChild(MakeLabel(rows[i].val, x + w * 0.5f, y, w * 0.5f, 20f,
                    valColors[i], 14f, InkWashTheme.FontRole.Number, TextAlignment.Far));
                y += 20f + 6f;
            }
            y += 8f;
            return y;
        }

        // ===================================================================
        // 货币栏：铜币 / 银两 / 金锭 / 元宝（分隔线分隔）
        // ===================================================================

        private void BuildCurrencyBar()
        {
            float barTop = MainPanelSize.Y - CurrencyHeight;
            _currencyBar = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, barTop),
                Size = new Float2(MainPanelSize.X, CurrencyHeight),
                BackgroundColor = Color.Transparent,
            };
            _mainPanel.AddChild(_currencyBar);

            _currencyBar.AddChild(new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 0f),
                Size = new Float2(MainPanelSize.X, 1f),
                BackgroundColor = InkWashTheme.BorderGoldSubtle,
            });

            // (glyph, label, value, iconColor)
            var defs = new (string glyph, string label, string val, Color iconColor)[]
            {
                ("铜", "铜币", "12,450", InkWashTheme.Alert),
                ("银", "银两", "3,200",  InkWashTheme.TextSecondary),
                ("金", "金锭", "85",     InkWashTheme.GoldPrimary),
                ("宝", "元宝", "12",     InkWashTheme.GoldBright),
            };

            float itemW = (MainPanelSize.X - ContentPadX * 2f - 3f) / 4f;
            float ix = ContentPadX;
            for (int i = 0; i < defs.Length; i++)
            {
                var d = defs[i];
                var item = new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(ix, 0f),
                    Size = new Float2(itemW, CurrencyHeight),
                    BackgroundColor = Color.Transparent,
                };
                _currencyBar.AddChild(item);

                item.AddChild(MakeLabel(d.glyph, 0f, 0f, 22f, CurrencyHeight,
                    d.iconColor, 16f, InkWashTheme.FontRole.Body, TextAlignment.Center));
                item.AddChild(MakeLabel(d.label, 32f, 0f, 48f, CurrencyHeight,
                    InkWashTheme.TextSecondary, 12f, InkWashTheme.FontRole.Body, TextAlignment.Near));
                item.AddChild(MakeLabel(d.val, itemW - 110f, 0f, 110f, CurrencyHeight,
                    InkWashTheme.PaperBright, 17f, InkWashTheme.FontRole.Number, TextAlignment.Far));

                ix += itemW;
                if (i < defs.Length - 1)
                {
                    _currencyBar.AddChild(new ContainerControl
                    {
                        AnchorPreset = AnchorPresets.TopLeft,
                        Location = new Float2(ix, (CurrencyHeight - 28f) * 0.5f),
                        Size = new Float2(1f, 28f),
                        BackgroundColor = new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.12f),
                    });
                    ix += 1f;
                }
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
            if (_tabActiveLine != null)
                _tabActiveLine.Location = new Float2(
                    ContentPadX + 60f + 16f + 1f + 32f + index * (70f + 28f), HeaderHeight - 2f);
        }

        private void OnCellClicked(int index)
        {
            if (_cellData[index].HasItem)
                SelectCell(index);
        }

        private void SelectCell(int index)
        {
            if (_cells == null) return;
            for (int i = 0; i < _cells.Length; i++)
                _cells[i].IsSelected = (i == index);
            _selectedCell = index;
            PopulateDetail(index);
            if (index >= 0 && index < _cells.Length)
                EmitGoldAtControl(_cells[index]);
        }

        private void PopulateDetail(int index)
        {
            if (index < 0 || index >= _cellData.Length || !_cellData[index].HasItem) return;
            var data = _cellData[index];

            string qualityName = data.IsQuest ? "任务" : data.Quality switch
            {
                InkWashTheme.InkQuality.Legendary => "传说",
                InkWashTheme.InkQuality.Epic => "史诗",
                InkWashTheme.InkQuality.Rare => "稀有",
                InkWashTheme.InkQuality.Uncommon => "良好",
                _ => "普通",
            };
            Color qColor = data.IsQuest ? InkWashTheme.Warning : InkWashTheme.QualityColor(data.Quality);

            _detailName.Text = data.Name;
            _detailName.TextColor = qColor;
            _tagQuality.SetText(qualityName);
            _tagQuality.SetColors(new Color(qColor.R, qColor.G, qColor.B, 0.12f), qColor,
                new Color(qColor.R, qColor.G, qColor.B, 0.25f));
            _tagType.SetText(data.Type);
            _tagQty.SetText("数量 " + (data.Qty ?? "1"));
            _detailIcon.SetGlyph(data.Glyph, qColor);
            _metaEnhanceVal.Text = data.Enhance ?? "-";
            _metaEnhanceVal.TextColor = data.Enhance != null ? InkWashTheme.GoldPrimary : InkWashTheme.TextSecondary;
            _metaDuraVal.Text = data.Enhance != null ? "850/1000" : "-/-";
        }

        private void OnCloseClicked()
        {
            NavigationRequested?.Invoke(InkPageDomIds.CombatHud);
        }

        private void EmitGoldAtButton(Button button)
        {
            if (ParticleSystem == null || button == null) return;
            try
            {
                var center = new Float2(button.Width * 0.5f, button.Height * 0.5f);
                var screenPos = button.PointToScreen(center);
                var localPos = ParticleSystem.PointFromScreen(screenPos);
                ParticleSystem.EmitGoldBurst(localPos, count: 12, isLarge: false);
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
                FlaxEngine.Debug.LogError($"[InventoryPage] RefreshLayout failed: {ex.Message}");
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
        // 嵌套控件：圆角背景+边框盒
        // ===================================================================

        /// <summary>自绘圆角背景 + 边框（StretchAll 填充父容器）。</summary>
        internal class RoundedBox : Control
        {
            private readonly Color _bg;
            private readonly Color _border;
            private readonly float _radius;

            public RoundedBox(Color bg, Color border, float radius)
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

        // ===================================================================
        // 嵌套控件：顶部背包 Tab
        // ===================================================================

        /// <summary>顶栏背包 Tab（14px 黑体，激活金色，自绘 + Clicked 事件）。</summary>
        internal class InvTab : Control
        {
            private readonly string _text;
            private bool _isActive;

            public event Action Clicked;

            public bool IsActive { get => _isActive; set => _isActive = value; }

            public InvTab(string text, bool active)
            {
                _text = text;
                _isActive = active;
                AutoFocus = false;
            }

            public override void Draw()
            {
                base.Draw();
                if (!Visible) return;
                Color color = _isActive ? InkWashTheme.GoldPrimary : InkWashTheme.TextSecondary;
                var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 14f).GetFont();
                if (font != null)
                    Render2D.DrawText(font, _text, new Rectangle(Float2.Zero, Size), color,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
            }

            public override bool OnMouseUp(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left && ContainsPoint(ref location))
                    Clicked?.Invoke();
                return base.OnMouseUp(location, button);
            }
        }

        // ===================================================================
        // 嵌套控件：背包格子
        // ===================================================================

        /// <summary>背包格子（64x64）：品质边框 + 图标字 + 强化/数量角标 + 选中辉光。</summary>
        internal class InvCell : Control
        {
            private readonly CellData _data;
            private bool _isSelected;
            private bool _isHovered;

            public event Action Clicked;

            public bool IsSelected { get => _isSelected; set => _isSelected = value; }

            public InvCell(CellData data)
            {
                _data = data;
                AutoFocus = false;
            }

            public override void Draw()
            {
                base.Draw();
                if (!Visible) return;
                var rect = new Rectangle(Float2.Zero, Size);
                Color gold = InkWashTheme.GoldPrimary;
                Color baseBg = InkWashTheme.BaseTertiary;

                // 背景：选中 gold0.06 / 悬停 BaseTertiary0.8 / 默认 BaseTertiary0.5
                Color bg;
                if (_isSelected)
                    bg = new Color(gold.R, gold.G, gold.B, 0.06f);
                else if (_isHovered && _data.HasItem)
                    bg = new Color(baseBg.R, baseBg.G, baseBg.B, 0.8f);
                else
                    bg = new Color(baseBg.R, baseBg.G, baseBg.B, 0.5f);
                InkRenderHelper.FillRoundedRectangle(rect, 4f, bg);

                // 边框：选中金 / 任务预警色 / 品质色（普通 0.35）
                Color border;
                if (_isSelected)
                    border = gold;
                else if (!_data.HasItem)
                    border = InkWashTheme.BorderFaint;
                else if (_data.IsQuest)
                    border = InkWashTheme.Warning;
                else if (_data.Quality == InkWashTheme.InkQuality.Common)
                    border = new Color(InkWashTheme.QualityCommon.R, InkWashTheme.QualityCommon.G, InkWashTheme.QualityCommon.B, 0.35f);
                else
                    border = InkWashTheme.QualityColor(_data.Quality);
                InkRenderHelper.DrawRoundedRectangle(rect, 4f, border, 1f);

                // 辉光：选中 gold0.25 / 传说 gold0.15
                if (_isSelected)
                {
                    var glowRect = new Rectangle(-2f, -2f, Size.X + 4f, Size.Y + 4f);
                    InkRenderHelper.DrawRoundedRectangle(glowRect, 6f, new Color(gold.R, gold.G, gold.B, 0.25f), 3f);
                }
                else if (_data.HasItem && _data.Quality == InkWashTheme.InkQuality.Legendary)
                {
                    var glowRect = new Rectangle(-1.5f, -1.5f, Size.X + 3f, Size.Y + 3f);
                    InkRenderHelper.DrawRoundedRectangle(glowRect, 5.5f, new Color(gold.R, gold.G, gold.B, 0.15f), 2.5f);
                }

                if (!_data.HasItem) return;

                // 图标字（居中，品质色）
                Color iconColor = _data.IsQuest ? InkWashTheme.Warning : InkWashTheme.QualityColor(_data.Quality);
                var iconFont = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 26f).GetFont();
                if (iconFont != null)
                    Render2D.DrawText(iconFont, _data.Glyph, rect, iconColor,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);

                // 强化角标（左上，金色 11px）
                if (!string.IsNullOrEmpty(_data.Enhance))
                {
                    var ef = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 11f).GetFont();
                    if (ef != null)
                        Render2D.DrawText(ef, _data.Enhance, new Rectangle(5f, 3f, 30f, 12f), gold,
                            TextAlignment.Near, TextAlignment.Near, TextWrapping.NoWrap);
                }

                // 数量角标（右下，亮白 12px）
                if (!string.IsNullOrEmpty(_data.Qty))
                {
                    var qf = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 12f).GetFont();
                    if (qf != null)
                        Render2D.DrawText(qf, _data.Qty, new Rectangle(Size.X - 35f, Size.Y - 16f, 30f, 13f),
                            InkWashTheme.PaperBright, TextAlignment.Far, TextAlignment.Far, TextWrapping.NoWrap);
                }

                // 锁标记（任务物品，右下，预警色）
                if (_data.IsQuest)
                {
                    var lf = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f).GetFont();
                    if (lf != null)
                        Render2D.DrawText(lf, "锁", new Rectangle(Size.X - 19f, Size.Y - 16f, 14f, 13f),
                            InkWashTheme.Warning, TextAlignment.Far, TextAlignment.Far, TextWrapping.NoWrap);
                }
            }

            public override void OnMouseEnter(Float2 location) { _isHovered = true; base.OnMouseEnter(location); }
            public override void OnMouseLeave() { _isHovered = false; base.OnMouseLeave(); }

            public override bool OnMouseUp(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left && _data.HasItem && ContainsPoint(ref location))
                    Clicked?.Invoke();
                return base.OnMouseUp(location, button);
            }
        }

        // ===================================================================
        // 嵌套控件：详情大图标
        // ===================================================================

        /// <summary>详情大图标（120x120）：径向渐变金辉 + 金边 + 辉光 + 64px 图标字。</summary>
        internal class DetailIconBox : Control
        {
            private string _glyph;
            private Color _glyphColor;

            public DetailIconBox(string glyph, Color color)
            {
                _glyph = glyph;
                _glyphColor = color;
                AutoFocus = false;
            }

            public void SetGlyph(string glyph, Color color)
            {
                _glyph = glyph;
                _glyphColor = color;
            }

            public override void Draw()
            {
                base.Draw();
                if (!Visible) return;
                var rect = new Rectangle(Float2.Zero, Size);
                Color gold = InkWashTheme.GoldPrimary;

                // 径向渐变背景（gold0.15 -> 透明）
                InkRenderHelper.FillRadialGradient(new Float2(Width * 0.5f, Height * 0.5f), Width * 0.5f,
                    new Color(gold.R, gold.G, gold.B, 0.15f), Color.Transparent);
                // 外辉光 + 边框 gold0.3
                var glowRect = new Rectangle(-2f, -2f, Size.X + 4f, Size.Y + 4f);
                InkRenderHelper.DrawRoundedRectangle(glowRect, 10f, new Color(gold.R, gold.G, gold.B, 0.2f), 3f);
                InkRenderHelper.DrawRoundedRectangle(rect, 8f, new Color(gold.R, gold.G, gold.B, 0.3f), 1f);

                // 图标字（56px）
                var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 56f).GetFont();
                if (font != null)
                    Render2D.DrawText(font, _glyph, rect, _glyphColor,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
            }
        }

        // ===================================================================
        // 嵌套控件：标签
        // ===================================================================

        /// <summary>详情标签（背景 + 边框 + 文字，radius 2）。</summary>
        internal class TagBox : Control
        {
            private string _text;
            private Color _bg;
            private Color _textColor;
            private Color _border;

            public TagBox(string text, Color bg, Color textColor, Color border)
            {
                _text = text;
                _bg = bg;
                _textColor = textColor;
                _border = border;
                AutoFocus = false;
            }

            public void SetText(string text) { _text = text; }

            public void SetColors(Color bg, Color textColor, Color border)
            {
                _bg = bg;
                _textColor = textColor;
                _border = border;
            }

            public override void Draw()
            {
                base.Draw();
                if (!Visible) return;
                var rect = new Rectangle(Float2.Zero, Size);
                InkRenderHelper.FillRoundedRectangle(rect, 2f, _bg);
                InkRenderHelper.DrawRoundedRectangle(rect, 2f, _border, 1f);
                var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f).GetFont();
                if (font != null)
                    Render2D.DrawText(font, _text, rect, _textColor,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
            }
        }
    }
}

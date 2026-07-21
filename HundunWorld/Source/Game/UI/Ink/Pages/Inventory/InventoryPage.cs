using FlaxEngine;
using FlaxEngine.GUI;
using Game.Character.Attributes;
using HundunWorld.Game.UI.StyleSystem;
using System;

namespace HundunWorld.Game.UI.Ink.Pages.Inventory
{
    public class InventoryPage : ContainerControl, IInkPage
    {
        private const float PanelWidth = 1400f;
        private const float PanelHeight = 900f;
        private const float HeaderHeight = 56f;
        private const float ContentGap = 24f;
        private const float GridFixedWidth = 800f;
        private const float CellSize = 64f;
        private const float CellGap = 12f;
        private const int GridCols = 8;
        private const int GridRows = 6;
        private const float CurrencyHeight = 52f;

        private ContainerControl _headerPanel;
        private Label _titleLabel;
        private InkButton[] _tabButtons;
        private int _activeTabIndex;
        private InkButton _sortButton;
        private InkButton _warehouseButton;
        private InkButton _closeButton;

        private ContainerControl _gridSection;
        private InkButton[] _gridCells;
        private int _selectedCellIndex = -1;
        private Label _capLabel;
        private Label _capNum;
        private Panel _capBarTrack;
        private Panel _capBarFill;

        private ContainerControl _detailPanel;
        private ContainerControl _detailIconOuter;
        private Panel _detailIconGlow;
        private Label _detailName;
        private Label _detailTagQuality;
        private Label _detailTagType;
        private Label _detailTagQty;
        private Label _metaEnhance;
        private Label _metaElement;
        private Label _metaBind;
        private Label _metaDura;
        private Panel _durabilityTrack;
        private Panel _durabilityFill;
        private Label _baseAttrTitle;
        private Label _baseAtk;
        private Label _baseCrit;
        private Label _extraAttrTitle;
        private Label _extraFocus;
        private ContainerControl _setBox;
        private Label _setTitle;
        private Label _setName;
        private Label _setCount;
        private Label _setDesc;
        private ContainerControl _actionRow;
        private InkButton _useButton;
        private InkButton _equipButton;
        private InkButton _dropButton;

        private ContainerControl _currencyBar;
        private ContainerControl _currCopper;
        private ContainerControl _currSilver;
        private ContainerControl _currGold;
        private ContainerControl _currYuanbao;
        private Panel _currencySep1;
        private Panel _currencySep2;
        private Panel _currencySep3;

        private struct CellData
        {
            public InkWashTheme.InkQuality Quality;
            public string Badge;
            public bool IsQuest;
            public bool HasItem;
        }
        private CellData[] _cellData;

        public event Action<string> NavigationRequested;
        public InkParticleSystem ParticleSystem { get; set; }

        public void BindCharacter(CharacterAttributesComponent component)
        {
        }

        public InventoryPage()
        {
            AnchorPreset = AnchorPresets.StretchAll;
            Offsets = Margin.Zero;
            BackgroundColor = new Color(20f / 255f, 23f / 255f, 30f / 255f, 0.85f);
            ClipChildren = false;
            AutoFocus = false;

            InitCellData();
            BuildHeader();
            BuildGridSection();
            BuildDetailPanel();
            BuildCurrencyBar();
            PopulateDetailMock();
            ApplyTabHighlight();
            SelectCell(0);
        }

        private void InitCellData()
        {
            _cellData = new CellData[GridCols * GridRows];
            var items = new (InkWashTheme.InkQuality q, string badge, bool quest)[]
            {
                (InkWashTheme.InkQuality.Legendary, "+12", false),
                (InkWashTheme.InkQuality.Epic,      "+8",  false),
                (InkWashTheme.InkQuality.Rare,      "+5",  false),
                (InkWashTheme.InkQuality.Common,     null,  false),
                (InkWashTheme.InkQuality.Common,     null,  false),
                (InkWashTheme.InkQuality.Common,     null,  false),
                (InkWashTheme.InkQuality.Uncommon,   "9",   false),
                (InkWashTheme.InkQuality.Uncommon,   "3",   false),
                (InkWashTheme.InkQuality.Uncommon,   "5",   false),
                (InkWashTheme.InkQuality.Common,     "3",   false),
                (InkWashTheme.InkQuality.Common,     "5",   false),
                (InkWashTheme.InkQuality.Common,     "2",   false),
                (InkWashTheme.InkQuality.Common,     "4",   false),
                (InkWashTheme.InkQuality.Common,     "2",   false),
                (InkWashTheme.InkQuality.Epic,       null,  true),
            };
            for (int i = 0; i < _cellData.Length; i++)
            {
                if (i < items.Length)
                {
                    _cellData[i] = new CellData
                    {
                        Quality = items[i].q,
                        Badge = items[i].badge,
                        IsQuest = items[i].quest,
                        HasItem = true,
                    };
                }
                else
                {
                    _cellData[i] = new CellData { Quality = InkWashTheme.InkQuality.Common, HasItem = false };
                }
            }
        }

        private void BuildHeader()
        {
            _headerPanel = new ContainerControl { BackgroundColor = Color.Transparent };

            _titleLabel = new Label
            {
                Text = "行囊",
                TextColor = InkWashTheme.TextGold,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 22f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _headerPanel.AddChild(_titleLabel);

            string[] tabNames = { "主背包", "材料背包", "任务背包", "时装背包" };
            _tabButtons = new InkButton[4];
            for (int i = 0; i < _tabButtons.Length; i++)
            {
                int idx = i;
                var btn = new InkButton
                {
                    Variant = InkButtonVariant.Ghost,
                    ButtonSize = InkButtonSize.Sm,
                    Text = tabNames[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 14f),
                };
                btn.ButtonClicked += (b) => OnTabClicked(idx);
                _tabButtons[i] = btn;
                _headerPanel.AddChild(btn);
            }

            _sortButton = new InkButton
            {
                Variant = InkButtonVariant.Default,
                ButtonSize = InkButtonSize.Sm,
                Text = "整理",
            };
            _sortButton.ButtonClicked += (b) => EmitGoldAtButton(b);
            _headerPanel.AddChild(_sortButton);

            _warehouseButton = new InkButton
            {
                Variant = InkButtonVariant.Default,
                ButtonSize = InkButtonSize.Sm,
                Text = "仓库",
            };
            _warehouseButton.ButtonClicked += (b) => EmitGoldAtButton(b);
            _headerPanel.AddChild(_warehouseButton);

            _closeButton = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Sm,
                Text = "✕",
            };
            _closeButton.ButtonClicked += (b) => NavigationRequested?.Invoke("combat-hud");
            _headerPanel.AddChild(_closeButton);

            AddChild(_headerPanel);
        }

        private void BuildGridSection()
        {
            _gridSection = new ContainerControl { BackgroundColor = Color.Transparent };
            AddChild(_gridSection);

            _gridCells = new InkButton[GridCols * GridRows];
            for (int i = 0; i < _gridCells.Length; i++)
            {
                int idx = i;
                var data = _cellData[i];
                var cell = new InkButton
                {
                    Variant = InkButtonVariant.Default,
                    ButtonSize = InkButtonSize.Sm,
                    Text = "",
                };
                ApplyCellVisual(cell, data, false);

                if (data.HasItem && !string.IsNullOrEmpty(data.Badge))
                {
                    bool isEnhance = data.Badge.StartsWith("+");
                    var badge = new Label
                    {
                        Text = data.Badge,
                        TextColor = isEnhance ? InkWashTheme.TextGold : InkWashTheme.TextDefault,
                        Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 12f),
                        HorizontalAlignment = isEnhance ? TextAlignment.Near : TextAlignment.Far,
                        VerticalAlignment = isEnhance ? TextAlignment.Near : TextAlignment.Far,
                    };
                    cell.AddChild(badge);
                }

                cell.ButtonClicked += (b) => SelectCell(idx);
                _gridCells[i] = cell;
                _gridSection.AddChild(cell);
            }

            _capLabel = new Label
            {
                Text = "格子",
                TextColor = InkWashTheme.TextSecondary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _gridSection.AddChild(_capLabel);

            _capNum = new Label
            {
                Text = "15/80",
                TextColor = InkWashTheme.TextDefault,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 14f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _gridSection.AddChild(_capNum);

            _capBarTrack = new Panel
            {
                BackgroundColor = new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.08f),
            };
            _gridSection.AddChild(_capBarTrack);

            _capBarFill = new Panel
            {
                BackgroundColor = InkWashTheme.GoldPrimary,
            };
            _capBarTrack.AddChild(_capBarFill);
        }

        private void ApplyCellVisual(InkButton cell, CellData data, bool selected)
        {
            Color bg;
            Color border;
            if (selected)
            {
                bg = new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.06f);
                border = InkWashTheme.GoldPrimary;
            }
            else if (!data.HasItem)
            {
                bg = new Color(28f / 255f, 31f / 255f, 40f / 255f, 0.5f);
                border = new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.08f);
            }
            else
            {
                bg = new Color(28f / 255f, 31f / 255f, 40f / 255f, 0.5f);
                border = data.IsQuest ? InkWashTheme.Warning : InkWashTheme.QualityColor(data.Quality);
            }
            cell.BackgroundColor = bg;
            cell.BorderColor = border;
            cell.BorderThickness = 1f;
        }

        private void SelectCell(int index)
        {
            if (_selectedCellIndex >= 0 && _selectedCellIndex < _gridCells.Length)
            {
                ApplyCellVisual(_gridCells[_selectedCellIndex], _cellData[_selectedCellIndex], false);
            }
            _selectedCellIndex = index;
            if (index >= 0 && index < _gridCells.Length)
            {
                ApplyCellVisual(_gridCells[index], _cellData[index], true);
            }
            PopulateDetailForCell(index);
        }

        private void BuildDetailPanel()
        {
            _detailPanel = new ContainerControl
            {
                BackgroundColor = new Color(14f / 255f, 16f / 255f, 22f / 255f, 0.6f),
            };
            AddChild(_detailPanel);

            _detailIconOuter = new ContainerControl { BackgroundColor = Color.Transparent };
            _detailPanel.AddChild(_detailIconOuter);

            _detailIconGlow = new Panel
            {
                BackgroundColor = new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.15f),
            };
            _detailIconOuter.AddChild(_detailIconGlow);

            _detailName = new Label
            {
                TextColor = InkWashTheme.QualityLegendary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 24f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            _detailPanel.AddChild(_detailName);

            _detailTagQuality = new Label
            {
                TextColor = InkWashTheme.TextGold,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                BackgroundColor = new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.12f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            _detailPanel.AddChild(_detailTagQuality);

            _detailTagType = new Label
            {
                TextColor = InkWashTheme.TextSecondary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                BackgroundColor = new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.08f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            _detailPanel.AddChild(_detailTagType);

            _detailTagQty = new Label
            {
                TextColor = InkWashTheme.TextSecondary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                BackgroundColor = new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.08f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            _detailPanel.AddChild(_detailTagQty);

            _metaEnhance = new Label
            {
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                TextColor = InkWashTheme.TextGold,
            };
            _detailPanel.AddChild(_metaEnhance);

            _metaElement = new Label
            {
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                TextColor = InkWashTheme.ElementMetal,
            };
            _detailPanel.AddChild(_metaElement);

            _metaBind = new Label
            {
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                TextColor = InkWashTheme.TextDefault,
            };
            _detailPanel.AddChild(_metaBind);

            _metaDura = new Label
            {
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                TextColor = InkWashTheme.TextDefault,
            };
            _detailPanel.AddChild(_metaDura);

            _durabilityTrack = new Panel
            {
                BackgroundColor = new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.08f),
            };
            _detailPanel.AddChild(_durabilityTrack);

            _durabilityFill = new Panel
            {
                BackgroundColor = InkWashTheme.GoldPrimary,
            };
            _durabilityTrack.AddChild(_durabilityFill);

            _baseAttrTitle = new Label
            {
                Text = "基础属性",
                TextColor = InkWashTheme.TextGold,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 14f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _detailPanel.AddChild(_baseAttrTitle);

            _baseAtk = new Label
            {
                TextColor = InkWashTheme.TextDefault,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _detailPanel.AddChild(_baseAtk);

            _baseCrit = new Label
            {
                TextColor = InkWashTheme.TextDefault,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _detailPanel.AddChild(_baseCrit);

            _extraAttrTitle = new Label
            {
                Text = "附加属性",
                TextColor = InkWashTheme.TextGold,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 14f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _detailPanel.AddChild(_extraAttrTitle);

            _extraFocus = new Label
            {
                TextColor = InkWashTheme.TextJade,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _detailPanel.AddChild(_extraFocus);

            _setTitle = new Label
            {
                Text = "套装效果",
                TextColor = InkWashTheme.TextGold,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 14f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _detailPanel.AddChild(_setTitle);

            _setBox = new ContainerControl
            {
                BackgroundColor = new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.06f),
            };
            _detailPanel.AddChild(_setBox);

            _setName = new Label
            {
                TextColor = InkWashTheme.TextGold,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _setBox.AddChild(_setName);

            _setCount = new Label
            {
                TextColor = InkWashTheme.TextSecondary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 12f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _setBox.AddChild(_setCount);

            _setDesc = new Label
            {
                TextColor = InkWashTheme.TextSecondary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Near,
            };
            _setBox.AddChild(_setDesc);

            _actionRow = new ContainerControl { BackgroundColor = Color.Transparent };
            _detailPanel.AddChild(_actionRow);

            _useButton = new InkButton
            {
                Variant = InkButtonVariant.Primary,
                ButtonSize = InkButtonSize.Md,
                Text = "使用",
            };
            _useButton.ButtonClicked += (b) => EmitGoldAtButton(b);
            _actionRow.AddChild(_useButton);

            _equipButton = new InkButton
            {
                Variant = InkButtonVariant.Default,
                ButtonSize = InkButtonSize.Md,
                Text = "装备",
            };
            _equipButton.ButtonClicked += (b) => EmitGoldAtButton(b);
            _actionRow.AddChild(_equipButton);

            _dropButton = new InkButton
            {
                Variant = InkButtonVariant.Vermilion,
                ButtonSize = InkButtonSize.Md,
                Text = "丢弃",
            };
            _dropButton.ButtonClicked += (b) => EmitGoldAtButton(b);
            _actionRow.AddChild(_dropButton);
        }

        private void PopulateDetailMock()
        {
            _detailName.Text = "玄铁剑";
            _detailTagQuality.Text = "传说";
            _detailTagType.Text = "武器";
            _detailTagQty.Text = "数量 1";
            _metaEnhance.Text = "强化  +12";
            _metaElement.Text = "五行  金";
            _metaBind.Text = "绑定  拾取绑定";
            _metaDura.Text = "耐久  850/1000";
            _baseAtk.Text = "攻击力        +120";
            _baseCrit.Text = "暴击率        +5%";
            _extraFocus.Text = "会心率        +3%";
            _setName.Text = "玄铁套";
            _setCount.Text = "2/4";
            _setDesc.Text = "2件套：攻击力 +10%";
        }

        private void PopulateDetailForCell(int index)
        {
            if (index < 0 || index >= _cellData.Length || !_cellData[index].HasItem)
            {
                _detailName.Text = "";
                _detailTagQuality.Text = "";
                _detailTagType.Text = "";
                _detailTagQty.Text = "";
                _metaEnhance.Text = "";
                _metaElement.Text = "";
                _metaBind.Text = "";
                _metaDura.Text = "";
                _baseAtk.Text = "";
                _baseCrit.Text = "";
                _extraFocus.Text = "";
                _setName.Text = "";
                _setCount.Text = "";
                _setDesc.Text = "";
                _detailIconGlow.BackgroundColor = new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.05f);
                _detailName.TextColor = InkWashTheme.TextSecondary;
                return;
            }

            var data = _cellData[index];
            string qualityName = data.Quality switch
            {
                InkWashTheme.InkQuality.Legendary => "传说",
                InkWashTheme.InkQuality.Epic => "史诗",
                InkWashTheme.InkQuality.Rare => "精良",
                InkWashTheme.InkQuality.Uncommon => "优秀",
                _ => "普通",
            };
            if (data.IsQuest) qualityName = "任务";

            string itemName = data.IsQuest ? "密信" : (index switch { 0 => "玄铁剑", 1 => "寒铁刀", 2 => "精铁护腕", 3 => "布衣", 4 => "铁剑", 5 => "皮甲", 6 => "生命药水", 7 => "内力药水", 8 => "活血丹", 9 => "铁矿", 10 => "草药", 11 => "兽皮", 12 => "铁矿", 13 => "草药", _ => "物品" });

            _detailName.Text = itemName;
            _detailTagQuality.Text = qualityName;
            _detailTagType.Text = data.IsQuest ? "任务" : "道具";
            _detailTagQty.Text = "数量 " + (data.Badge ?? "1");
            _metaEnhance.Text = "强化  " + (data.Badge ?? "-");
            _metaElement.Text = "五行  金";
            _metaBind.Text = "绑定  拾取绑定";
            _metaDura.Text = "耐久  -/-";
            _baseAtk.Text = data.Badge != null ? "属性        " + data.Badge : "";
            _baseCrit.Text = "";
            _extraFocus.Text = "";
            _setName.Text = "";
            _setCount.Text = "";
            _setDesc.Text = "";

            Color qColor = data.IsQuest ? InkWashTheme.Warning : InkWashTheme.QualityColor(data.Quality);
            _detailName.TextColor = qColor;
            _detailTagQuality.TextColor = qColor;
            _detailTagQuality.BackgroundColor = new Color(qColor.R, qColor.G, qColor.B, 0.12f);
            _detailIconGlow.BackgroundColor = new Color(qColor.R, qColor.G, qColor.B, 0.15f);
        }

        private void BuildCurrencyBar()
        {
            _currencyBar = new ContainerControl { BackgroundColor = Color.Transparent };
            AddChild(_currencyBar);

            string[][] defs = {
                new[] { "\U0001fa99", "铜币", "12,450" },
                new[] { "\U0001f4a0", "银两", "3,200" },
                new[] { "\U0001f3c5", "金锭", "85" },
                new[] { "\U0001f48e", "元宝", "12" },
            };
            Color[] iconColors = {
                InkWashTheme.Warning,
                InkWashTheme.TextSecondary,
                InkWashTheme.GoldPrimary,
                InkWashTheme.GoldBright,
            };
            ContainerControl[] items = new ContainerControl[4];
            for (int i = 0; i < 4; i++)
            {
                var item = new ContainerControl { BackgroundColor = Color.Transparent };

                var icon = new Label
                {
                    Text = defs[i][0],
                    TextColor = iconColors[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 14f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                item.AddChild(icon);

                var label = new Label
                {
                    Text = defs[i][1],
                    TextColor = InkWashTheme.TextSecondary,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                item.AddChild(label);

                var val = new Label
                {
                    Text = defs[i][2],
                    TextColor = InkWashTheme.TextDefault,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 17f),
                    HorizontalAlignment = TextAlignment.Far,
                    VerticalAlignment = TextAlignment.Center,
                };
                item.AddChild(val);

                _currencyBar.AddChild(item);
                items[i] = item;
            }

            _currCopper = items[0];
            _currSilver = items[1];
            _currGold = items[2];
            _currYuanbao = items[3];

            for (int i = 0; i < 3; i++)
            {
                var sep = new Panel
                {
                    BackgroundColor = new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.12f),
                };
                _currencyBar.AddChild(sep);
                switch (i)
                {
                    case 0: _currencySep1 = sep; break;
                    case 1: _currencySep2 = sep; break;
                    case 2: _currencySep3 = sep; break;
                }
            }
        }

        private void OnTabClicked(int index)
        {
            _activeTabIndex = index;
            ApplyTabHighlight();
        }

        private void ApplyTabHighlight()
        {
            if (_tabButtons == null) return;
            for (int i = 0; i < _tabButtons.Length; i++)
            {
                if (_tabButtons[i] == null) continue;
                _tabButtons[i].TextColor = (i == _activeTabIndex) ? InkWashTheme.TextGold : InkWashTheme.TextSecondary;
            }
        }

        private void EmitGoldAtButton(Button button)
        {
            if (ParticleSystem == null || button == null) return;
            try
            {
                var center = new Float2(button.Width * 0.5f, button.Height * 0.5f);
                var screenPos = button.PointToScreen(center);
                var localPos = ParticleSystem.PointFromScreen(screenPos);
                ParticleSystem.EmitGoldBurst(localPos, count: 14, isLarge: false);
            }
            catch { }
        }

        public void RefreshLayout()
        {
            try
            {
                float w = Width;
                float h = Height;
                if (w <= 0f || h <= 0f) return;

                float pad = 16f;
                float panelX = pad;
                float panelW = w - pad * 2f;

                if (_headerPanel != null)
                {
                    _headerPanel.Location = new Float2(panelX, pad);
                    _headerPanel.Size = new Float2(panelW, HeaderHeight);

                    float titleW = 80f;
                    _titleLabel.Location = new Float2(0f, 0f);
                    _titleLabel.Size = new Float2(titleW, HeaderHeight);

                    float tabStartX = titleW + 24f + 16f;
                    float tabGap = 28f;
                    for (int i = 0; i < _tabButtons.Length; i++)
                    {
                        _tabButtons[i].Location = new Float2(tabStartX + i * (80f + tabGap), 0f);
                        _tabButtons[i].Size = new Float2(80f, HeaderHeight);
                    }

                    float rightX = panelW - 4f;
                    float btnSize = 32f;
                    _closeButton.Location = new Float2(rightX - btnSize, (HeaderHeight - 28f) * 0.5f);
                    _closeButton.Size = new Float2(btnSize, 28f);
                    rightX -= btnSize + 6f;

                    btnSize = 56f;
                    _warehouseButton.Location = new Float2(rightX - btnSize, (HeaderHeight - 28f) * 0.5f);
                    _warehouseButton.Size = new Float2(btnSize, 28f);
                    rightX -= btnSize + 6f;

                    _sortButton.Location = new Float2(rightX - btnSize, (HeaderHeight - 28f) * 0.5f);
                    _sortButton.Size = new Float2(btnSize, 28f);
                }

                float headerBottom = pad + HeaderHeight + 14f;
                float currencyTop = h - pad - CurrencyHeight;
                float contentTop = headerBottom;
                float contentH = currencyTop - contentTop - 12f;
                if (contentH < 200f) contentH = 200f;

                float innerX = 4f;
                float innerW = panelW - 8f;
                _gridSection.Location = new Float2(panelX + innerX, contentTop);
                _gridSection.Size = new Float2(GridFixedWidth, contentH);

                float cellTotalW = CellSize * GridCols + CellGap * (GridCols - 1);
                float cellTotalH = CellSize * GridRows + CellGap * (GridRows - 1);
                float gridStartX = (GridFixedWidth - cellTotalW) * 0.5f;
                float gridStartY = 4f;

                for (int i = 0; i < _gridCells.Length; i++)
                {
                    int col = i % GridCols;
                    int row = i / GridCols;
                    _gridCells[i].Location = new Float2(gridStartX + col * (CellSize + CellGap), gridStartY + row * (CellSize + CellGap));
                    _gridCells[i].Size = new Float2(CellSize, CellSize);
                    _gridCells[i].Height = CellSize;

                    foreach (var child in _gridCells[i].Children)
                    {
                        if (child is Label badge)
                        {
                            badge.Location = new Float2(2f, 2f);
                            badge.Size = new Float2(CellSize - 4f, CellSize - 4f);
                        }
                    }
                }

                float footerY = gridStartY + cellTotalH + 16f;
                float footerH = 24f;
                _capLabel.Location = new Float2(gridStartX, footerY);
                _capLabel.Size = new Float2(40f, footerH);

                _capNum.Location = new Float2(gridStartX + 44f, footerY);
                _capNum.Size = new Float2(70f, footerH);

                float capBarX = gridStartX + 120f;
                float capBarW = GridFixedWidth - capBarX - gridStartX;
                _capBarTrack.Location = new Float2(capBarX, footerY + (footerH - 4f) * 0.5f);
                _capBarTrack.Size = new Float2(Mathf.Max(60f, capBarW), 4f);

                _capBarFill.Location = Float2.Zero;
                _capBarFill.Size = new Float2(_capBarTrack.Width * 0.1875f, 4f);

                float detailW = innerW - GridFixedWidth - ContentGap;
                if (detailW < 300f) detailW = 300f;
                _detailPanel.Location = new Float2(panelX + innerX + GridFixedWidth + ContentGap, contentTop);
                _detailPanel.Size = new Float2(detailW, contentH);

                float dp = 16f;
                float dw = detailW - dp * 2f;

                float iconSize = 120f;
                _detailIconOuter.Location = new Float2(dp + (dw - iconSize) * 0.5f, dp);
                _detailIconOuter.Size = new Float2(iconSize, iconSize);

                _detailIconGlow.Location = Float2.Zero;
                _detailIconGlow.Size = new Float2(iconSize, iconSize);

                float nameY = dp + iconSize + 12f;
                _detailName.Location = new Float2(dp, nameY);
                _detailName.Size = new Float2(dw, 28f);

                float tagsY = nameY + 30f;
                float tagW = 50f;
                _detailTagQuality.Location = new Float2(dp + (dw - tagW * 3f - 12f) * 0.5f, tagsY);
                _detailTagQuality.Size = new Float2(tagW, 22f);

                _detailTagType.Location = new Float2(dp + (dw - tagW * 3f - 12f) * 0.5f + tagW + 6f, tagsY);
                _detailTagType.Size = new Float2(tagW, 22f);

                _detailTagQty.Location = new Float2(dp + (dw - tagW * 3f - 12f) * 0.5f + (tagW + 6f) * 2f, tagsY);
                _detailTagQty.Size = new Float2(tagW + 12f, 22f);

                float metaY = tagsY + 28f;
                float halfW = dw * 0.5f - 4f;
                _metaEnhance.Location = new Float2(dp, metaY);
                _metaEnhance.Size = new Float2(halfW, 22f);
                _metaElement.Location = new Float2(dp + halfW + 8f, metaY);
                _metaElement.Size = new Float2(halfW, 22f);
                _metaBind.Location = new Float2(dp, metaY + 24f);
                _metaBind.Size = new Float2(halfW, 22f);
                _metaDura.Location = new Float2(dp + halfW + 8f, metaY + 24f);
                _metaDura.Size = new Float2(halfW, 22f);

                float duraY = metaY + 50f + 4f;
                _durabilityTrack.Location = new Float2(dp + 2f, duraY);
                _durabilityTrack.Size = new Float2(dw - 4f, 4f);
                _durabilityFill.Location = Float2.Zero;
                _durabilityFill.Size = new Float2(_durabilityTrack.Width * 0.85f, 4f);

                float attrY = duraY + 16f;
                _baseAttrTitle.Location = new Float2(dp, attrY);
                _baseAttrTitle.Size = new Float2(dw, 20f);
                _baseAtk.Location = new Float2(dp, attrY + 22f);
                _baseAtk.Size = new Float2(dw, 20f);
                _baseCrit.Location = new Float2(dp, attrY + 44f);
                _baseCrit.Size = new Float2(dw, 20f);

                float extraY = attrY + 68f;
                _extraAttrTitle.Location = new Float2(dp, extraY);
                _extraAttrTitle.Size = new Float2(dw, 20f);
                _extraFocus.Location = new Float2(dp, extraY + 22f);
                _extraFocus.Size = new Float2(dw, 20f);

                float setTitleY = extraY + 48f;
                _setTitle.Location = new Float2(dp, setTitleY);
                _setTitle.Size = new Float2(dw, 20f);

                float setBoxY = setTitleY + 22f;
                _setBox.Location = new Float2(dp, setBoxY);
                _setBox.Size = new Float2(dw, 56f);
                _setName.Location = new Float2(8f, 6f);
                _setName.Size = new Float2(100f, 18f);
                _setCount.Location = new Float2(108f, 6f);
                _setCount.Size = new Float2(60f, 18f);
                _setDesc.Location = new Float2(8f, 26f);
                _setDesc.Size = new Float2(dw - 16f, 22f);

                float actionY = setBoxY + 60f + 8f;
                _actionRow.Location = new Float2(dp, actionY);
                _actionRow.Size = new Float2(dw, 40f);
                float abtnW = (dw - 12f * 2f) / 3f;
                _useButton.Size = new Float2(abtnW, 36f);
                _useButton.Location = new Float2(0f, 2f);
                _equipButton.Size = new Float2(abtnW, 36f);
                _equipButton.Location = new Float2(abtnW + 12f, 2f);
                _dropButton.Size = new Float2(abtnW, 36f);
                _dropButton.Location = new Float2((abtnW + 12f) * 2f, 2f);

                if (_currencyBar != null)
                {
                    _currencyBar.Location = new Float2(panelX, currencyTop);
                    _currencyBar.Size = new Float2(panelW, CurrencyHeight);

                    float sepW = 1f;
                    float itemW = (panelW - sepW * 3f) / 4f;
                    ContainerControl[] currItems = { _currCopper, _currSilver, _currGold, _currYuanbao };
                    Panel[] seps = { _currencySep1, _currencySep2, _currencySep3 };

                    for (int i = 0; i < 4; i++)
                    {
                        var item = currItems[i];
                        float ix = i * (itemW + sepW);
                        item.Location = new Float2(ix, 0f);
                        item.Size = new Float2(itemW, CurrencyHeight);

                        if (item.ChildrenCount >= 3 && item.Children[2] is Label val)
                        {
                            val.Location = new Float2(itemW - 100f, (CurrencyHeight - 20f) * 0.5f);
                            val.Size = new Float2(100f, 20f);
                        }
                        if (item.ChildrenCount >= 2 && item.Children[1] is Label lbl)
                        {
                            lbl.Location = new Float2(28f, (CurrencyHeight - 20f) * 0.5f);
                            lbl.Size = new Float2(itemW - 130f, 20f);
                        }
                        if (item.ChildrenCount >= 1 && item.Children[0] is Label icon)
                        {
                            icon.Location = new Float2(0f, (CurrencyHeight - 20f) * 0.5f);
                            icon.Size = new Float2(24f, 20f);
                        }

                        if (i < 3)
                        {
                            seps[i].Location = new Float2(ix + itemW, (CurrencyHeight - 24f) * 0.5f);
                            seps[i].Size = new Float2(sepW, 24f);
                        }
                    }
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
    }
}

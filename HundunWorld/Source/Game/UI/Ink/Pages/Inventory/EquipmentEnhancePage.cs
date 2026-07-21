using FlaxEngine;
using FlaxEngine.GUI;
using Game.Character.Attributes;
using HundunWorld.Game.UI.StyleSystem;
using System;

namespace HundunWorld.Game.UI.Ink.Pages.Inventory
{
    public class EquipmentEnhancePage : ContainerControl, IInkPage
    {
        private const float HeaderHeight = 52f;
        private const float TabBarHeight = 44f;
        private const float ScreenEdge = 16f;
        private const float RegionGap = 12f;
        private const float LeftColumnWidth = 250f;
        private const float RightColumnWidth = 300f;
        private const float ColumnGap = 12f;
        private const float TabBtnWidth = 80f;
        private const float TabBtnGap = 4f;
        private const float EquipItemHeight = 48f;
        private const float EquipItemGap = 6f;
        private const float AttrRowHeight = 22f;

        private static readonly Color BorderGold = new Color(200f / 255f, 168f / 255f, 88f / 255f, 1f);
        private static readonly Color BorderFaint = new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.25f);
        private static readonly Color DividerColor = new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.15f);
        private static readonly Color GoldTrace = new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.08f);
        private static readonly Color GoldFaint = new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.35f);
        private static readonly Color VoidBg = new Color(8f / 255f, 9f / 255f, 12f / 255f, 1f);
        private static readonly Color PanelBg = new Color(20f / 255f, 23f / 255f, 30f / 255f, 0.85f);
        private static readonly Color PaperBg = new Color(20f / 255f, 23f / 255f, 30f / 255f, 0.50f);
        private static readonly Color JadeBright = new Color(106f / 255f, 191f / 255f, 155f / 255f, 1f);
        private static readonly Color TextPrimary = new Color(220f / 255f, 224f / 255f, 232f / 255f, 1f);
        private static readonly Color GoldBright = new Color(255f / 255f, 215f / 255f, 128f / 255f, 1f);

        private Panel _header;
        private InkButton _backButton;
        private Label _title;
        private Label _badge;
        private Label _hint;

        private Panel _tabBar;
        private InkButton[] _tabs;
        private int _activeTab = 0;
        private Label _tabWarn;

        private Panel _leftPanel;
        private InkButton[] _filterBtns;
        private int _activeFilter = 0;
        private Label _listCount;
        private ContainerControl[] _equipItems;
        private int _selectedEquip = 0;

        private Panel _centerPanel;
        private Panel _previewBox;
        private Label _previewIcon;
        private Label _previewLabel;
        private Label _previewRotate;
        private Label _equipName;
        private Label _qualityTag;
        private Label _enhanceTag;
        private Label _typeTag;
        private Label _levelTag;
        private Label[] _attrNames;
        private Label[] _attrCurrents;
        private Label[] _attrNexts;
        private Label[] _attrDeltas;
        private Label _rateLabel;
        private Label _rateValue;
        private Panel _rateTrack;
        private Panel _rateFill;
        private Label _failWarn;
        private Label _stoneIcon;
        private Label _stoneName;
        private Label _stoneQty;
        private Label _silverIcon;
        private Label _silverName;
        private Label _silverQty;
        private InkButton _enhanceBtn;

        private Panel _rightPanel;
        private Label[] _matLabels;
        private Label _progressCur;
        private Label _progressNext;
        private Panel _progressTrack;
        private Panel _progressFill;
        private Label _progressMax;
        private Label _setName;
        private Label _setCount;
        private Label _setBonus2;
        private Label _setBonus4;
        private Label[] _elemNames;
        private Panel[] _elemTracks;
        private Panel[] _elemFills;
        private Label[] _elemPcts;

        private CharacterAttributesComponent _boundCharacter;

        public event Action<string> NavigationRequested;

        public InkParticleSystem ParticleSystem { get; set; }

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
                BackgroundColor = VoidBg;
                ClipChildren = false;
                AutoFocus = false;

                BuildHeader();
                BuildTabBar();
                BuildLeftColumn();
                BuildCenterColumn();
                BuildRightColumn();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[EquipmentEnhancePage] init: {ex.Message}");
            }
        }

        // ===================================================================
        //  HEADER
        // ===================================================================

        private void BuildHeader()
        {
            _header = new Panel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = PanelBg,
            };
            var headerBorder = new Panel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = BorderGold,
            };
            _header.AddChild(headerBorder);

            _backButton = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Sm,
                Text = "\u2190",
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(36f, 32f),
            };
            _backButton.Clicked += () => NavigationRequested?.Invoke(InkPageDomIds.BackHud);
            _header.AddChild(_backButton);

            _title = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Text = "\u88c5\u5907\u5f3a\u5316",
                TextColor = InkWashTheme.GoldPrimary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 22f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _header.AddChild(_title);

            _badge = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Text = "\u953b\u9020\u5de5\u574a",
                TextColor = InkWashTheme.TextTertiary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                BackgroundColor = new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.08f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            _header.AddChild(_badge);

            _hint = new Label
            {
                AnchorPreset = AnchorPresets.TopRight,
                Text = "\u9009\u62e9\u88c5\u5907\u540e\u8fdb\u884c\u953b\u9020\uff0c\u4e0d\u540c\u6a21\u5f0f\u6d88\u8017\u4e0d\u540c\u6750\u6599",
                TextColor = InkWashTheme.TextTertiary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
            };
            _header.AddChild(_hint);

            AddChild(_header);
        }

        // ===================================================================
        //  TAB BAR
        // ===================================================================

        private void BuildTabBar()
        {
            _tabBar = new Panel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = PanelBg,
            };
            var tabBorder = new Panel
            {
                AnchorPreset = AnchorPresets.BottomLeft,
                BackgroundColor = DividerColor,
            };
            _tabBar.AddChild(tabBorder);

            string[] tabNames = { "\u5f3a\u5316", "\u9576\u5d4c", "\u7cbe\u70bc", "\u8c03\u5f8b", "\u6dec\u706b" };
            _tabs = new InkButton[5];
            float tx = 24f;
            for (int i = 0; i < 5; i++)
            {
                int idx = i;
                var btn = new InkButton
                {
                    Variant = InkButtonVariant.Ghost,
                    ButtonSize = InkButtonSize.Md,
                    Text = tabNames[i],
                    AnchorPreset = AnchorPresets.TopLeft,
                    Size = new Float2(TabBtnWidth, 32f),
                    Location = new Float2(tx, (TabBarHeight - 32f) * 0.5f),
                };
                btn.Clicked += () => OnTabClicked(idx);
                _tabBar.AddChild(btn);
                _tabs[i] = btn;
                tx += TabBtnWidth + TabBtnGap;
            }
            ApplyTabHighlight();

            _tabWarn = new Label
            {
                AnchorPreset = AnchorPresets.TopRight,
                Text = "\u5f3a\u5316\u5931\u8d25\u5c06\u964d\u7ea7",
                TextColor = InkWashTheme.TextBlood,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
            };
            _tabBar.AddChild(_tabWarn);

            AddChild(_tabBar);
        }

        // ===================================================================
        //  LEFT COLUMN
        // ===================================================================

        private void BuildLeftColumn()
        {
            _leftPanel = new Panel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = PanelBg,
            };

            var filterTitle = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 10f),
                Size = new Float2(LeftColumnWidth - 24f, 18f),
                Text = "\u54c1\u8d28\u7b5b\u9009",
                TextColor = InkWashTheme.TextSecondary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 12f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _leftPanel.AddChild(filterTitle);

            string[] filterNames = { "\u5168\u90e8", "\u666e\u901a", "\u4f18\u826f", "\u7a00\u6709", "\u53f2\u8bd7", "\u4f20\u8bf4" };
            _filterBtns = new InkButton[6];
            float fx = 12f, fy = 32f;
            float fw = (LeftColumnWidth - 24f - 5f * 4f) / 3f;
            for (int i = 0; i < 6; i++)
            {
                int idx = i;
                var btn = new InkButton
                {
                    Variant = InkButtonVariant.Ghost,
                    ButtonSize = InkButtonSize.Sm,
                    Text = filterNames[i],
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(fx, fy),
                    Size = new Float2(fw, 22f),
                };
                btn.Clicked += () => OnFilterClicked(idx);
                _leftPanel.AddChild(btn);
                _filterBtns[i] = btn;
                fx += fw + 4f;
                if (i == 2) { fx = 12f; fy += 26f; }
            }
            ApplyFilterHighlight();

            fy += 30f;
            var div = new Panel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, fy),
                Size = new Float2(LeftColumnWidth - 24f, 1f),
                BackgroundColor = DividerColor,
            };
            _leftPanel.AddChild(div);

            fy += 8f;
            var listHeader = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, fy),
                Size = new Float2(120f, 18f),
                Text = "\u88c5\u5907\u5217\u8868",
                TextColor = InkWashTheme.TextSecondary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 12f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _leftPanel.AddChild(listHeader);

            _listCount = new Label
            {
                AnchorPreset = AnchorPresets.TopRight,
                Location = new Float2(0f, fy),
                Size = new Float2(60f, 18f),
                Text = "8\u4ef6",
                TextColor = InkWashTheme.TextTertiary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 11f),
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
            };
            _leftPanel.AddChild(_listCount);

            fy += 22f;
            var mockEquips = new (string name, string type, InkWashTheme.InkQuality quality, int enhance)[]
            {
                ("\u7384\u94c1\u91cd\u5251", "\u53cc\u624b\u5251 \u00b7 60\u7ea7", InkWashTheme.InkQuality.Legendary, 12),
                ("\u8d64\u9704\u67aa",   "\u957f\u67aa \u00b7 60\u7ea7",   InkWashTheme.InkQuality.Legendary, 15),
                ("\u7d2b\u91d1\u51a0",   "\u5934\u51a0 \u00b7 55\u7ea7",   InkWashTheme.InkQuality.Epic,      10),
                ("\u5929\u7f61\u888d",   "\u6cd5\u888d \u00b7 55\u7ea7",   InkWashTheme.InkQuality.Epic,       9),
                ("\u78a7\u7389\u6756",   "\u6cd5\u6748 \u00b7 50\u7ea7",   InkWashTheme.InkQuality.Rare,       5),
                ("\u7384\u6b66\u76fe",   "\u76fe\u724c \u00b7 50\u7ea7",   InkWashTheme.InkQuality.Rare,       6),
                ("\u5bd2\u6708\u5200",   "\u5355\u5200 \u00b7 45\u7ea7",   InkWashTheme.InkQuality.Uncommon,   2),
                ("\u94c1\u7532\u62a4\u8155", "\u62a4\u8155 \u00b7 40\u7ea7", InkWashTheme.InkQuality.Common,     3),
            };

            _equipItems = new ContainerControl[mockEquips.Length];
            float iy = fy;
            for (int i = 0; i < mockEquips.Length; i++)
            {
                var item = CreateEquipItem(12f, iy, mockEquips[i].name, mockEquips[i].type, mockEquips[i].quality, mockEquips[i].enhance, i);
                _equipItems[i] = item;
                _leftPanel.AddChild(item);
                iy += EquipItemHeight + EquipItemGap;
            }
            ApplyEquipHighlight();

            AddChild(_leftPanel);
        }

        private class ClickableContainer : ContainerControl
        {
            public event Action Clicked;
            public override bool OnMouseUp(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left) Clicked?.Invoke();
                return base.OnMouseUp(location, button);
            }
        }

        private ContainerControl CreateEquipItem(float x, float y, string name, string type, InkWashTheme.InkQuality quality, int enhance, int index)
        {
            var c = new ClickableContainer
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(x, y),
                Size = new Float2(LeftColumnWidth - 24f, EquipItemHeight),
            };

            int idx = index;
            c.Clicked += () =>
            {
                _selectedEquip = idx;
                ApplyEquipHighlight();
            };

            var icon = new Panel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(4f, 4f),
                Size = new Float2(40f, 40f),
                BackgroundColor = VoidBg,
            };
            c.AddChild(icon);

            var n = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(52f, 4f),
                Size = new Float2(LeftColumnWidth - 24f - 52f - 40f, 20f),
                Text = name,
                TextColor = InkWashTheme.TextDefault,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            c.AddChild(n);

            var t = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(52f, 24f),
                Size = new Float2(LeftColumnWidth - 24f - 52f - 40f, 16f),
                Text = type,
                TextColor = InkWashTheme.TextTertiary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            c.AddChild(t);

            var el = new Label
            {
                AnchorPreset = AnchorPresets.TopRight,
                Location = new Float2(0f, 8f),
                Size = new Float2(48f, 32f),
                Text = "+" + enhance,
                TextColor = enhance > 0 ? InkWashTheme.GoldPrimary : InkWashTheme.TextSecondary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 14f),
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
            };
            c.AddChild(el);

            return c;
        }

        // ===================================================================
        //  CENTER COLUMN
        // ===================================================================

        private void BuildCenterColumn()
        {
            _centerPanel = new Panel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = PanelBg,
            };

            float cx = 16f, cy = 16f, cw = 0f;

            _previewBox = new Panel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(cx, cy),
                Size = new Float2(200f, 200f),
                BackgroundColor = VoidBg,
            };
            _centerPanel.AddChild(_previewBox);

            _previewIcon = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Text = "\u2694",
                TextColor = InkWashTheme.QualityColor(InkWashTheme.InkQuality.Legendary),
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 64f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            _previewBox.AddChild(_previewIcon);

            _previewLabel = new Label
            {
                AnchorPreset = AnchorPresets.BottomLeft,
                Text = "\u88c5\u5907\u9884\u89c8",
                TextColor = InkWashTheme.TextTertiary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            _previewBox.AddChild(_previewLabel);

            _previewRotate = new Label
            {
                AnchorPreset = AnchorPresets.TopRight,
                Text = "\u62d6\u62fd\u65cb\u8f6c",
                TextColor = InkWashTheme.TextTertiary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f),
                HorizontalAlignment = TextAlignment.Far,
            };
            _previewBox.AddChild(_previewRotate);

            float nx = cx + 220f;
            _equipName = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(nx, cy + 8f),
                Size = new Float2(160f, 28f),
                Text = "\u7384\u94c1\u91cd\u5251",
                TextColor = InkWashTheme.QualityColor(InkWashTheme.InkQuality.Legendary),
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 20f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _centerPanel.AddChild(_equipName);

            float ty = cy + 44f;
            _qualityTag = MakeTag(nx, ty, "\u4f20\u8bf4", InkWashTheme.QualityColor(InkWashTheme.InkQuality.Legendary));
            _enhanceTag = MakeTag(nx + 64f, ty, "+12", GoldBright, 16f);
            _typeTag = MakeTag(nx + 120f, ty, "\u53cc\u624b\u5251", InkWashTheme.TextSecondary);
            _levelTag = MakeTag(nx + 180f, ty, "60\u7ea7", InkWashTheme.TextSecondary);
            _centerPanel.AddChild(_qualityTag);
            _centerPanel.AddChild(_enhanceTag);
            _centerPanel.AddChild(_typeTag);
            _centerPanel.AddChild(_levelTag);

            float ay = cy + 80f;
            var attrTitle = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(cx, ay),
                Size = new Float2(200f, 18f),
                Text = "\u5c5e\u6027\u5bf9\u6bd4  \u00b7 \u5f3a\u5316\u540e\u9884\u89c8",
                TextColor = InkWashTheme.TextSecondary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 12f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _centerPanel.AddChild(attrTitle);

            ay += 22f;
            var attrHeader = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(cx, ay),
                Size = new Float2(440f, 20f),
                Text = "\u5c5e\u6027                  \u5f53\u524d          \u5f3a\u5316\u540e        \u53d8\u5316",
                TextColor = InkWashTheme.TextTertiary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _centerPanel.AddChild(attrHeader);

            var attrData = new (string name, string cur, string next, string delta)[]
            {
                ("\u653b\u51fb\u529b", "1245",   "1320",  "+75"),
                ("\u66b4\u51fb\u7387", "12.5%",  "13.8%", "+1.3%"),
                ("\u547d\u4e2d",   "320",    "340",   "+20"),
                ("\u7a7f\u900f",   "85",     "92",    "+7"),
                ("\u4f1a\u5fc3",   "45",     "45",    "\u2014"),
            };

            _attrNames = new Label[5];
            _attrCurrents = new Label[5];
            _attrNexts = new Label[5];
            _attrDeltas = new Label[5];

            ay += 22f;
            for (int i = 0; i < 5; i++)
            {
                _attrNames[i] = MakeAttrCell(cx, ay, 120f, attrData[i].name, InkWashTheme.TextSecondary, TextAlignment.Near);
                _attrCurrents[i] = MakeAttrCell(cx + 140f, ay, 100f, attrData[i].cur, InkWashTheme.TextDefault, TextAlignment.Far);
                _attrNexts[i] = MakeAttrCell(cx + 240f, ay, 100f, attrData[i].next, GoldBright, TextAlignment.Far);
                Color dc = attrData[i].delta == "\u2014" ? InkWashTheme.TextTertiary : JadeBright;
                _attrDeltas[i] = MakeAttrCell(cx + 340f, ay, 100f, attrData[i].delta, dc, TextAlignment.Far);
                ay += AttrRowHeight;
            }

            ay += 12f;
            _rateLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(cx, ay),
                Size = new Float2(120f, 18f),
                Text = "\u6210\u529f\u7387",
                TextColor = InkWashTheme.TextSecondary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _centerPanel.AddChild(_rateLabel);

            _rateValue = new Label
            {
                AnchorPreset = AnchorPresets.TopRight,
                Location = new Float2(0f, ay),
                Size = new Float2(80f, 18f),
                Text = "78%",
                TextColor = GoldBright,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 16f),
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
            };
            _centerPanel.AddChild(_rateValue);

            ay += 22f;
            _rateTrack = new Panel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(cx, ay),
                Size = new Float2(440f, 10f),
                BackgroundColor = VoidBg,
            };
            _centerPanel.AddChild(_rateTrack);

            _rateFill = new Panel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Float2(440f * 0.78f, 10f),
                BackgroundColor = BorderGold,
            };
            _rateTrack.AddChild(_rateFill);

            ay += 14f;
            _failWarn = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(cx, ay),
                Size = new Float2(440f, 16f),
                Text = "\u5931\u8d25\u5c06\u964d\u4f4e1\u7ea7\u5f3a\u5316\u7b49\u7ea7\uff0c\u5efa\u8bae\u4f7f\u7528\u62a4\u8eab\u7b26",
                TextColor = InkWashTheme.TextBlood,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _centerPanel.AddChild(_failWarn);

            ay += 20f;
            float mcw = 200f;
            _stoneIcon = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(cx, ay),
                Size = new Float2(32f, 32f),
                Text = "\u25a0",
                TextColor = InkWashTheme.GoldPrimary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 14f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                BackgroundColor = VoidBg,
            };
            _centerPanel.AddChild(_stoneIcon);

            _stoneName = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(cx + 36f, ay),
                Size = new Float2(mcw, 16f),
                Text = "\u7384\u94c1\u5f3a\u5316\u77f3",
                TextColor = InkWashTheme.TextSecondary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _centerPanel.AddChild(_stoneName);

            _stoneQty = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(cx + 36f, ay + 16f),
                Size = new Float2(mcw, 16f),
                Text = "x5",
                TextColor = GoldBright,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _centerPanel.AddChild(_stoneQty);

            float sx = cx + 240f;
            _silverIcon = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(sx, ay),
                Size = new Float2(32f, 32f),
                Text = "\u25a0",
                TextColor = InkWashTheme.GoldPrimary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 14f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                BackgroundColor = VoidBg,
            };
            _centerPanel.AddChild(_silverIcon);

            _silverName = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(sx + 36f, ay),
                Size = new Float2(mcw, 16f),
                Text = "\u94f6\u4e24",
                TextColor = InkWashTheme.TextSecondary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _centerPanel.AddChild(_silverName);

            _silverQty = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(sx + 36f, ay + 16f),
                Size = new Float2(mcw, 16f),
                Text = "x50,000",
                TextColor = GoldBright,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _centerPanel.AddChild(_silverQty);

            _enhanceBtn = new InkButton
            {
                Variant = InkButtonVariant.Primary,
                ButtonSize = InkButtonSize.Lg,
                Text = "\u6267\u884c\u5f3a\u5316",
                AnchorPreset = AnchorPresets.BottomLeft,
                Size = new Float2(200f, 44f),
            };
            _enhanceBtn.Clicked += () => { };
            _centerPanel.AddChild(_enhanceBtn);

            AddChild(_centerPanel);
        }

        // ===================================================================
        //  RIGHT COLUMN
        // ===================================================================

        private void BuildRightColumn()
        {
            _rightPanel = new Panel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = PanelBg,
            };

            float y = 12f;

            var matTitle = SectionLabel(12f, y, "\u6240\u9700\u6750\u6599");
            _rightPanel.AddChild(matTitle);
            y += 24f;

            string[] matTexts =
            {
                "\u7384\u94c1\u5f3a\u5316\u77f3  \u62e5\u6709 23 / \u9700\u8981 5",
                "\u7cbe\u70bc\u7802      \u62e5\u6709 8 / \u9700\u8981 3",
                "\u94f6\u4e24        \u62e5\u6709 128,000 / \u9700\u8981 50,000",
            };
            _matLabels = new Label[3];
            for (int i = 0; i < 3; i++)
            {
                var card = new Panel
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(12f, y),
                    Size = new Float2(RightColumnWidth - 24f, 44f),
                    BackgroundColor = PaperBg,
                };
                _rightPanel.AddChild(card);

                _matLabels[i] = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(40f, 4f),
                    Size = new Float2(RightColumnWidth - 24f - 48f, 36f),
                    Text = matTexts[i],
                    TextColor = InkWashTheme.TextDefault,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                card.AddChild(_matLabels[i]);
                y += 48f;
            }

            y += 8f;
            var progTitle = SectionLabel(12f, y, "\u5f3a\u5316\u8fdb\u5ea6");
            _rightPanel.AddChild(progTitle);
            y += 24f;

            _progressCur = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, y),
                Size = new Float2(120f, 28f),
                Text = "+12",
                TextColor = GoldBright,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 22f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _rightPanel.AddChild(_progressCur);

            _progressNext = new Label
            {
                AnchorPreset = AnchorPresets.TopRight,
                Location = new Float2(0f, y),
                Size = new Float2(80f, 28f),
                Text = "+13",
                TextColor = InkWashTheme.TextTertiary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 22f),
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
            };
            _rightPanel.AddChild(_progressNext);
            y += 32f;

            _progressTrack = new Panel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, y),
                Size = new Float2(RightColumnWidth - 24f, 8f),
                BackgroundColor = VoidBg,
            };
            _rightPanel.AddChild(_progressTrack);

            _progressFill = new Panel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Float2((RightColumnWidth - 24f) * 0.60f, 8f),
                BackgroundColor = InkWashTheme.GoldDeep,
            };
            _progressTrack.AddChild(_progressFill);
            y += 12f;

            _progressMax = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, y),
                Size = new Float2(RightColumnWidth - 24f, 14f),
                Text = "\u5f53\u524d\u7b49\u7ea7                         \u4e0a\u9650 +20",
                TextColor = InkWashTheme.TextTertiary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _rightPanel.AddChild(_progressMax);
            y += 28f;

            var setTitle = SectionLabel(12f, y, "\u5957\u88c5\u6548\u679c");
            _rightPanel.AddChild(setTitle);
            y += 24f;

            var setCard = new Panel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, y),
                Size = new Float2(RightColumnWidth - 24f, 80f),
                BackgroundColor = PaperBg,
            };
            _rightPanel.AddChild(setCard);

            _setName = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 8f),
                Size = new Float2(140f, 20f),
                Text = "\u7384\u94c1\u6218\u610f",
                TextColor = InkWashTheme.QualityColor(InkWashTheme.InkQuality.Legendary),
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            setCard.AddChild(_setName);

            _setCount = new Label
            {
                AnchorPreset = AnchorPresets.TopRight,
                Location = new Float2(0f, 8f),
                Size = new Float2(60f, 20f),
                Text = "2 / 4 \u4ef6",
                TextColor = InkWashTheme.TextTertiary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 11f),
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
            };
            setCard.AddChild(_setCount);

            _setBonus2 = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 32f),
                Size = new Float2(RightColumnWidth - 48f, 16f),
                Text = "[2] \u653b\u51fb\u529b +5%",
                TextColor = JadeBright,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            setCard.AddChild(_setBonus2);

            _setBonus4 = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 50f),
                Size = new Float2(RightColumnWidth - 48f, 16f),
                Text = "[4] \u66b4\u51fb\u4f24\u5bb3 +20%    \u672a\u6fc0\u6d3b",
                TextColor = InkWashTheme.TextTertiary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            setCard.AddChild(_setBonus4);

            y += 84f + 12f;
            var elemTitle = SectionLabel(12f, y, "\u4e94\u884c\u5206\u5e03");
            _rightPanel.AddChild(elemTitle);
            y += 24f;

            string[] elemNames = { "\u91d1", "\u6728", "\u6c34", "\u706b", "\u571f" };
            float[] elemPcts = { 0.35f, 0.15f, 0.20f, 0.20f, 0.10f };
            Color[] elemColors =
            {
                InkWashTheme.ElementMetal,
                InkWashTheme.ElementWood,
                InkWashTheme.ElementWater,
                InkWashTheme.ElementFire,
                InkWashTheme.ElementEarth,
            };

            _elemNames = new Label[5];
            _elemTracks = new Panel[5];
            _elemFills = new Panel[5];
            _elemPcts = new Label[5];

            for (int i = 0; i < 5; i++)
            {
                _elemNames[i] = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(12f, y),
                    Size = new Float2(16f, 20f),
                    Text = elemNames[i],
                    TextColor = InkWashTheme.TextSecondary,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                _rightPanel.AddChild(_elemNames[i]);

                _elemTracks[i] = new Panel
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(32f, y + 6f),
                    Size = new Float2(RightColumnWidth - 24f - 80f, 8f),
                    BackgroundColor = VoidBg,
                };
                _rightPanel.AddChild(_elemTracks[i]);

                float ew = (RightColumnWidth - 24f - 80f) * elemPcts[i];
                _elemFills[i] = new Panel
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = Float2.Zero,
                    Size = new Float2(ew, 8f),
                    BackgroundColor = elemColors[i],
                };
                _elemTracks[i].AddChild(_elemFills[i]);

                _elemPcts[i] = new Label
                {
                    AnchorPreset = AnchorPresets.TopRight,
                    Location = new Float2(0f, y),
                    Size = new Float2(44f, 20f),
                    Text = (elemPcts[i] * 100f).ToString("0") + "%",
                    TextColor = InkWashTheme.TextDefault,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 11f),
                    HorizontalAlignment = TextAlignment.Far,
                    VerticalAlignment = TextAlignment.Center,
                };
                _rightPanel.AddChild(_elemPcts[i]);

                y += 26f;
            }

            AddChild(_rightPanel);
        }

        // ===================================================================
        //  HELPERS
        // ===================================================================

        private Label MakeTag(float x, float y, string text, Color color, float fontSize = 11f)
        {
            return new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(x, y),
                Size = new Float2(60f, 22f),
                Text = text,
                TextColor = color,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, fontSize),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                BackgroundColor = new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.10f),
            };
        }

        private Label MakeAttrCell(float x, float y, float w, string text, Color color, TextAlignment align)
        {
            return new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(x, y),
                Size = new Float2(w, AttrRowHeight),
                Text = text,
                TextColor = color,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 13f),
                HorizontalAlignment = align,
                VerticalAlignment = TextAlignment.Center,
            };
        }

        private Label SectionLabel(float x, float y, string text)
        {
            return new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, y),
                Size = new Float2(RightColumnWidth - 24f, 18f),
                Text = text,
                TextColor = InkWashTheme.TextSecondary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 12f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
        }

        // ===================================================================
        //  EVENTS
        // ===================================================================

        private void OnTabClicked(int index)
        {
            _activeTab = index;
            ApplyTabHighlight();
        }

        private void OnFilterClicked(int index)
        {
            _activeFilter = index;
            ApplyFilterHighlight();
        }

        private void ApplyTabHighlight()
        {
            if (_tabs == null) return;
            for (int i = 0; i < _tabs.Length; i++)
            {
                if (_tabs[i] == null) continue;
                _tabs[i].TextColor = i == _activeTab ? InkWashTheme.GoldPrimary : InkWashTheme.TextSecondary;
            }
        }

        private void ApplyFilterHighlight()
        {
            if (_filterBtns == null) return;
            for (int i = 0; i < _filterBtns.Length; i++)
            {
                if (_filterBtns[i] == null) continue;
                _filterBtns[i].TextColor = i == _activeFilter ? GoldBright : InkWashTheme.TextSecondary;
            }
        }

        private void ApplyEquipHighlight()
        {
            if (_equipItems == null) return;
            for (int i = 0; i < _equipItems.Length; i++)
            {
                if (_equipItems[i] == null) continue;
                _equipItems[i].BackgroundColor = i == _selectedEquip
                    ? new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.12f)
                    : Color.Transparent;
            }
        }

        // ===================================================================
        //  LAYOUT
        // ===================================================================

        public void RefreshLayout()
        {
            try
            {
                float w = Width;
                float h = Height;
                float px = ScreenEdge;
                float pw = w - ScreenEdge * 2f;

                if (_header != null)
                {
                    _header.Location = new Float2(px, 0f);
                    _header.Size = new Float2(pw, HeaderHeight);

                    if (_backButton != null)
                    {
                        _backButton.Location = new Float2(12f, (HeaderHeight - 32f) * 0.5f);
                    }
                    if (_title != null)
                    {
                        _title.Location = new Float2(56f, 0f);
                        _title.Size = new Float2(160f, HeaderHeight);
                    }
                    if (_badge != null)
                    {
                        _badge.Location = new Float2(210f, (HeaderHeight - 22f) * 0.5f);
                        _badge.Size = new Float2(80f, 22f);
                    }
                    if (_hint != null)
                    {
                        _hint.Location = new Float2(pw - 400f, 0f);
                        _hint.Size = new Float2(380f, HeaderHeight);
                    }
                }

                float tabY = HeaderHeight;
                if (_tabBar != null)
                {
                    _tabBar.Location = new Float2(px, tabY);
                    _tabBar.Size = new Float2(pw, TabBarHeight);

                    if (_tabWarn != null)
                    {
                        _tabWarn.Location = new Float2(pw - 220f, 0f);
                        _tabWarn.Size = new Float2(200f, TabBarHeight);
                    }
                }

                float contentTop = tabY + TabBarHeight + RegionGap;
                float contentH = h - contentTop - ScreenEdge;
                if (contentH < 100f) contentH = 100f;

                float leftX = px;
                float leftW = LeftColumnWidth;
                float rightW = RightColumnWidth;
                float middleW = pw - leftW - rightW - ColumnGap * 2f;
                float middleX = leftX + leftW + ColumnGap;
                float rightX = middleX + middleW + ColumnGap;

                if (_leftPanel != null)
                {
                    _leftPanel.Location = new Float2(leftX, contentTop);
                    _leftPanel.Size = new Float2(leftW, contentH);
                }

                if (_centerPanel != null)
                {
                    _centerPanel.Location = new Float2(middleX, contentTop);
                    _centerPanel.Size = new Float2(middleW, contentH);

                    if (_enhanceBtn != null)
                    {
                        float btnX = (middleW - 200f) * 0.5f;
                        _enhanceBtn.Location = new Float2(btnX, contentH - 56f);
                    }

                    if (_previewBox != null)
                    {
                        _previewBox.Size = new Float2(middleW - 32f, 200f);
                        float iconSize = Mathf.Min(_previewBox.Width, _previewBox.Height) * 0.5f;
                        if (_previewIcon != null)
                        {
                            _previewIcon.Location = new Float2((_previewBox.Width - iconSize) * 0.5f, (_previewBox.Height - iconSize) * 0.5f - 10f);
                            _previewIcon.Size = new Float2(iconSize, iconSize);
                            _previewIcon.Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, iconSize * 0.6f);
                        }
                        if (_previewLabel != null)
                        {
                            _previewLabel.Location = new Float2((_previewBox.Width - 100f) * 0.5f, _previewBox.Height - 24f);
                            _previewLabel.Size = new Float2(100f, 16f);
                        }
                        if (_previewRotate != null)
                        {
                            _previewRotate.Location = new Float2(_previewBox.Width - 80f, 8f);
                            _previewRotate.Size = new Float2(70f, 16f);
                        }
                    }
                }

                if (_rightPanel != null)
                {
                    _rightPanel.Location = new Float2(rightX, contentTop);
                    _rightPanel.Size = new Float2(rightW, contentH);
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[EquipmentEnhancePage] RefreshLayout: {ex.Message}");
            }
        }

        public override void OnParentResized()
        {
            base.OnParentResized();
            RefreshLayout();
        }
    }
}

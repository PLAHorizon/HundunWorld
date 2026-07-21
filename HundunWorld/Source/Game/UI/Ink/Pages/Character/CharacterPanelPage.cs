using FlaxEngine;
using FlaxEngine.GUI;
using Game.Character.Attributes;
using HundunWorld.Game.UI.Ink.Components;
using HundunWorld.Game.UI.StyleSystem;
using System;

namespace HundunWorld.Game.UI.Ink.Pages.Character
{
    public class CharacterPanelPage : ContainerControl, IInkPage
    {
        private const float TopBarHeight = 52f;
        private const float BottomBarHeight = 56f;
        private const float Padding = 12f;
        private const float LeftPanelRatio = 0.48f;
        private const float SlotBtnSize = 40f;
        private const float NavButtonWidth = 130f;
        private const float NavButtonHeight = 36f;
        private const float NavButtonGap = 12f;

        public event Action<string> NavigationRequested;

        private InkParticleSystem _particleSystem;
        public InkParticleSystem ParticleSystem
        {
            get => _particleSystem;
            set
            {
                _particleSystem = value;
                if (_navTabs != null)
                    foreach (var t in _navTabs) t.ParticleSystem = value;
            }
        }

        private CharacterAttributesComponent _boundCharacter;

        private ContainerControl _topBar;
        private Label _titleLabel;
        private Label _subtitleLabel;
        private Label _nameLabel;
        private Label _levelLabel;
        private InkButton _closeButton;

        private ContainerControl _leftPanel;
        private ContainerControl _modelStage;
        private InkButton[] _floatingSlots;
        private int _activeSlotIndex = -1;
        private ContainerControl _tabPanel;
        private ContainerControl _cornerTL;
        private ContainerControl _cornerTR;
        private ContainerControl _cornerBL;
        private ContainerControl _cornerBR;
        private const float CornerSize = 20f;
        private const float CornerInset = 6f;
        private Label _previewBadge;
        private Label _rotateHint;

        private ContainerControl _rightPanel;

        private ContainerControl _bottomBar;
        private InkButton _btnReturnHud;
        private InkTabButton _tabSkill;
        private InkTabButton _tabInventory;
        private InkTabButton _tabAttributes;
        private InkTabButton[] _navTabs;

        private float[] _slotLeftPct;
        private float[] _slotTopPct;

        public CharacterPanelPage()
        {
            AnchorPreset = AnchorPresets.StretchAll;
            Offsets = Margin.Zero;
            BackgroundColor = InkWashTheme.Scrim;
            ClipChildren = false;
            AutoFocus = false;

            BuildTopBar();
            BuildLeftColumn();
            BuildRightColumn();
            BuildBottomBar();
        }

        public void BindCharacter(CharacterAttributesComponent component)
        {
            _boundCharacter = component;
            if (component != null)
            {
                if (_nameLabel != null) _nameLabel.Text = component.Nickname;
                if (_levelLabel != null) _levelLabel.Text = "Lv." + component.Level.ToString();
            }
        }

        public void RefreshLayout()
        {
            try
            {
                float w = Width;
                float h = Height;
                float contentH = h - TopBarHeight - BottomBarHeight;

                if (_topBar != null)
                {
                    _topBar.Size = new Float2(w, TopBarHeight);
                }

                if (_leftPanel != null)
                {
                    _leftPanel.Size = new Float2(w * LeftPanelRatio, contentH);
                    _leftPanel.Location = new Float2(Padding, TopBarHeight);
                }

                if (_rightPanel != null)
                {
                    _rightPanel.Size = new Float2(w * (1f - LeftPanelRatio) - Padding * 2, contentH);
                    _rightPanel.Location = new Float2(w * LeftPanelRatio + Padding * 1.5f, TopBarHeight);
                }

                if (_bottomBar != null)
                {
                    _bottomBar.Location = new Float2(0f, h - BottomBarHeight);
                    _bottomBar.Size = new Float2(w, BottomBarHeight);

                    float btnY = (BottomBarHeight - NavButtonHeight) * 0.5f;

                    if (_btnReturnHud != null)
                        _btnReturnHud.Location = new Float2(Padding, btnY);

                    float tabW = NavButtonWidth;
                    float tabGap = NavButtonGap;
                    float tabTotal = tabW * _navTabs.Length + tabGap * (_navTabs.Length - 1);
                    float tabStartX = (w - tabTotal) * 0.5f;
                    if (tabStartX < Padding + NavButtonWidth + NavButtonGap * 2)
                        tabStartX = Padding + NavButtonWidth + NavButtonGap * 2;

                    for (int i = 0; i < _navTabs.Length; i++)
                    {
                        _navTabs[i].Location = new Float2(tabStartX + i * (tabW + tabGap), btnY);
                        _navTabs[i].Size = new Float2(tabW, NavButtonHeight);
                    }
                }

                if (_closeButton != null && _topBar != null)
                {
                    _closeButton.Location = new Float2(_topBar.Width - Padding - SlotBtnSize, (TopBarHeight - SlotBtnSize) * 0.5f);
                }
                if (_levelLabel != null && _closeButton != null && _topBar != null)
                {
                    _levelLabel.Location = new Float2(_closeButton.Left - 90f, 0f);
                }
                if (_nameLabel != null && _levelLabel != null)
                {
                    _nameLabel.Location = new Float2(_levelLabel.Left - 8f - 100f, 0f);
                }

                if (_modelStage != null && _floatingSlots != null)
                {
                    float mw = _modelStage.Width;
                    float mh = _modelStage.Height;
                    for (int i = 0; i < _floatingSlots.Length; i++)
                    {
                        float sx = _slotLeftPct[i] * mw - SlotBtnSize * 0.5f;
                        float sy = _slotTopPct[i] * mh - SlotBtnSize * 0.5f;
                        _floatingSlots[i].Location = new Float2(sx, sy);
                    }
                }

                if (_modelStage != null)
                {
                    float mw = _modelStage.Width;
                    float mh = _modelStage.Height;

                    if (_cornerTL != null)
                        _cornerTL.Location = new Float2(CornerInset, CornerInset);
                    if (_cornerTR != null)
                        _cornerTR.Location = new Float2(mw - CornerInset - CornerSize, CornerInset);
                    if (_cornerBL != null)
                        _cornerBL.Location = new Float2(CornerInset, mh - CornerInset - CornerSize);
                    if (_cornerBR != null)
                        _cornerBR.Location = new Float2(mw - CornerInset - CornerSize, mh - CornerInset - CornerSize);
                }

                if (_tabPanel != null && _modelStage != null)
                {
                    _tabPanel.Location = new Float2((_modelStage.Width - _tabPanel.Width) * 0.5f, _modelStage.Height - _tabPanel.Height - 8f);
                }

                if (_rightPanel != null)
                {
                    float rw = _rightPanel.Width;
                    float ry = Padding;
                    foreach (var child in _rightPanel.Children)
                    {
                        if (child is ContainerControl card)
                        {
                            card.Width = rw - Padding * 2;
                            card.Location = new Float2(Padding, ry);
                            ry += card.Height + Padding;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[CharacterPanelPage] RefreshLayout: {ex.Message}");
            }
        }

        public override void OnParentResized()
        {
            base.OnParentResized();
            RefreshLayout();
        }

        // ===================================================================
        // Top Bar
        // ===================================================================

        private void BuildTopBar()
        {
            _topBar = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Float2(Width, TopBarHeight),
                BackgroundColor = new Color(InkWashTheme.BaseSecondary.R, InkWashTheme.BaseSecondary.G, InkWashTheme.BaseSecondary.B, 0.9f),
            };
            AddChild(_topBar);

            var bottomBorder = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, TopBarHeight - 1f),
                Size = new Float2(Width, 1f),
                BackgroundColor = InkWashTheme.BorderGoldSubtle,
            };
            _topBar.AddChild(bottomBorder);

            var iconLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(Padding, (TopBarHeight - 18f) * 0.5f),
                Size = new Float2(18f, 18f),
                Text = "\u2630",
                TextColor = InkWashTheme.GoldPrimary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 14f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            _topBar.AddChild(iconLabel);

            _titleLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(Padding + 18f + 10f, 0f),
                Size = new Float2(120f, TopBarHeight),
                Text = "角色信息",
                TextColor = InkWashTheme.GoldPrimary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 18f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _topBar.AddChild(_titleLabel);

            _subtitleLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(Padding + 18f + 10f + 118f, 0f),
                Size = new Float2(100f, TopBarHeight),
                Text = "CHARACTER",
                TextColor = InkWashTheme.TextTertiary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _topBar.AddChild(_subtitleLabel);

            _closeButton = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Sm,
                Text = "X",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(Width > 0f ? Width - Padding - SlotBtnSize : 900f - Padding - SlotBtnSize, (TopBarHeight - SlotBtnSize) * 0.5f),
                Size = new Float2(SlotBtnSize, SlotBtnSize),
            };
            _closeButton.Clicked += OnCloseClicked;
            _topBar.AddChild(_closeButton);

            _levelLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(80f, TopBarHeight),
                Text = "Lv.60",
                TextColor = InkWashTheme.GoldPrimary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 14f),
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
            };
            _topBar.AddChild(_levelLabel);

            _nameLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(100f, TopBarHeight),
                Text = "逍遥客",
                TextColor = InkWashTheme.TextDefault,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 14f),
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
            };
            _topBar.AddChild(_nameLabel);
        }

        // ===================================================================
        // Left Column - Model Stage
        // ===================================================================

        private void BuildLeftColumn()
        {
            _leftPanel = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(Padding, TopBarHeight),
                Size = new Float2(Width * LeftPanelRatio, Height - TopBarHeight - BottomBarHeight),
                ClipChildren = false,
            };
            AddChild(_leftPanel);

            _modelStage = new ContainerControl
            {
                AnchorPreset = AnchorPresets.StretchAll,
                Offsets = Margin.Zero,
                BackgroundColor = InkWashTheme.BaseDefault,
                ClipChildren = false,
            };
            _leftPanel.AddChild(_modelStage);

            var modelPlaceholder = new Label
            {
                Size = new Float2(200f, 40f),
                Text = "3D 角色模型",
                TextColor = InkWashTheme.TextTertiary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 16f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            _modelStage.AddChild(modelPlaceholder);

            BuildFloatingSlots();

            var bubble = new InkToolBarBubble();
            _modelStage.AddChild(bubble);

            _previewBadge = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(8f, 8f),
                Size = new Float2(70f, 24f),
                Text = "3D预览",
                TextColor = InkWashTheme.GoldPrimary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 11f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                BackgroundColor = new Color(14f / 255f, 16f / 255f, 22f / 255f, 0.7f),
            };
            _modelStage.AddChild(_previewBadge);

            _rotateHint = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(82f, 8f),
                Size = new Float2(80f, 24f),
                Text = "拖拽旋转",
                TextColor = InkWashTheme.TextTertiary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                BackgroundColor = new Color(14f / 255f, 16f / 255f, 22f / 255f, 0.7f),
            };
            _modelStage.AddChild(_rotateHint);

            BuildCornerDecorations();
            BuildAppearanceTabs();
        }

        private void BuildFloatingSlots()
        {
            string[] slotChars = { "头", "颈", "面", "肩", "背", "右", "腰", "左", "右戒", "左戒", "右腕", "身", "腿", "左腕", "足" };
            _floatingSlots = new InkButton[slotChars.Length];
            _slotLeftPct = new float[slotChars.Length];
            _slotTopPct = new float[slotChars.Length];

            var slotPositions = new (float leftPct, float topPct, bool equipped, string icon, string name, string type, string attr, string extra, int enhance)[]
            {
                (0.30f, 0.04f, true,  "头", "玄铁重盔",   "头部", "防+47", "耐久 200", 3),
                (0.50f, 0.04f, true,  "颈", "镇魂项链",   "颈部", "气+32", "耐久 180", 5),
                (0.70f, 0.04f, true,  "面", "天蚕面纱",   "面部", "敏+28", "耐久 160", 2),
                (0.08f, 0.24f, true,  "肩", "龙鳞护肩",   "肩部", "防+35", "耐久 220", 4),
                (0.84f, 0.24f, true,  "背", "玄龟背甲",   "背部", "体+40", "耐久 240", 1),
                (0.04f, 0.42f, true,  "右", "破军剑",     "右手", "攻+88", "耐久 150", 7),
                (0.46f, 0.42f, true,  "腰", "蟒筋腰带",   "腰部", "气+25", "耐久 190", 2),
                (0.90f, 0.42f, true,  "左", "玄铁盾",     "左手", "防+62", "耐久 210", 6),
                (0.08f, 0.60f, true,  "右戒", "镇魂戒",   "右戒", "攻+22", "耐久 130", 3),
                (0.84f, 0.60f, true,  "左戒", "灵犀戒",   "左戒", "气+20", "耐久 130", 4),
                (0.04f, 0.78f, true,  "右腕", "玄铁护腕", "右腕", "防+18", "耐久 170", 1),
                (0.24f, 0.78f, true,  "身", "天蚕宝甲",   "身体", "防+55", "耐久 280", 5),
                (0.44f, 0.78f, true,  "腿", "龙鳞战腿",   "腿部", "防+38", "耐久 230", 3),
                (0.64f, 0.78f, true,  "左腕", "灵犀护腕", "左腕", "防+16", "耐久 170", 2),
                (0.84f, 0.78f, true,  "足", "追风靴",     "脚部", "敏+30", "耐久 200", 4),
            };

            var bubbleData = new (string icon, string name, string type, string attr, string extra, int enhance)[slotPositions.Length];
            for (int i = 0; i < slotChars.Length; i++)
            {
                var pos = slotPositions[i];
                _slotLeftPct[i] = pos.leftPct;
                _slotTopPct[i] = pos.topPct;
                bubbleData[i] = (pos.icon, pos.name, pos.type, pos.attr, pos.extra, pos.enhance);

                var btn = new InkButton
                {
                    Variant = InkButtonVariant.Ghost,
                    Text = pos.icon,
                    Size = new Float2(SlotBtnSize - 4f, SlotBtnSize - 4f),
                    BackgroundColor = pos.equipped
                        ? new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.1f)
                        : new Color(14f / 255f, 16f / 255f, 22f / 255f, 0.4f),
                    BorderColor = pos.equipped ? InkWashTheme.BorderGold : InkWashTheme.BorderFaint,
                    BorderThickness = 1f,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 11f),
                    TextColor = pos.equipped ? InkWashTheme.TextDefault : InkWashTheme.TextTertiary,
                };
                _floatingSlots[i] = btn;
                _modelStage.AddChild(btn);

                int captured = i;
                btn.Clicked += () => OnSlotClicked(captured);
            }
        }

        private void ShowSlotBubble(int index)
        {
            if (_floatingSlots == null || index < 0 || index >= _floatingSlots.Length) return;

            string[][] bubbleSlots =
            {
                new[] { "头", "玄铁重盔", "头部", "防+47", "耐久 200", "3" },
                new[] { "颈", "镇魂项链", "颈部", "气+32", "耐久 180", "5" },
                new[] { "面", "天蚕面纱", "面部", "敏+28", "耐久 160", "2" },
                new[] { "肩", "龙鳞护肩", "肩部", "防+35", "耐久 220", "4" },
                new[] { "背", "玄龟背甲", "背部", "体+40", "耐久 240", "1" },
                new[] { "右", "破军剑",   "右手", "攻+88", "耐久 150", "7" },
                new[] { "腰", "蟒筋腰带", "腰部", "气+25", "耐久 190", "2" },
                new[] { "左", "玄铁盾",   "左手", "防+62", "耐久 210", "6" },
                new[] { "右戒", "镇魂戒", "右戒", "攻+22", "耐久 130", "3" },
                new[] { "左戒", "灵犀戒", "左戒", "气+20", "耐久 130", "4" },
                new[] { "右腕", "玄铁护腕", "右腕", "防+18", "耐久 170", "1" },
                new[] { "身", "天蚕宝甲", "身体", "防+55", "耐久 280", "5" },
                new[] { "腿", "龙鳞战腿", "腿部", "防+38", "耐久 230", "3" },
                new[] { "左腕", "灵犀护腕", "左腕", "防+16", "耐久 170", "2" },
                new[] { "足", "追风靴",   "脚部", "敏+30", "耐久 200", "4" },
            };

            if (index >= bubbleSlots.Length) return;
            var d = bubbleSlots[index];
            var btn = _floatingSlots[index];
            InkToolBarBubble.Instance.ShowAt(btn, d[1], d[2], d[3], d[4], int.Parse(d[5]));
            _activeSlotIndex = index;
        }

        private void BuildCornerDecorations()
        {
            float thick = 2f;
            var gold = new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.5f);

            _cornerTL = new ContainerControl { Size = new Float2(CornerSize, CornerSize) };
            _modelStage.AddChild(_cornerTL);
            MakeLBars(_cornerTL, thick, gold);

            _cornerTR = new ContainerControl { Size = new Float2(CornerSize, CornerSize) };
            _modelStage.AddChild(_cornerTR);
            MakeRBars(_cornerTR, thick, gold);

            _cornerBL = new ContainerControl { Size = new Float2(CornerSize, CornerSize) };
            _modelStage.AddChild(_cornerBL);
            MakeLBars(_cornerBL, thick, gold);

            _cornerBR = new ContainerControl { Size = new Float2(CornerSize, CornerSize) };
            _modelStage.AddChild(_cornerBR);
            MakeRBars(_cornerBR, thick, gold);
        }

        private static void MakeLBars(ContainerControl parent, float thick, Color color)
        {
            parent.AddChild(new ContainerControl { Size = new Float2(parent.Width, thick), BackgroundColor = color });
            parent.AddChild(new ContainerControl { Size = new Float2(thick, parent.Height), BackgroundColor = color });
        }

        private static void MakeRBars(ContainerControl parent, float thick, Color color)
        {
            parent.AddChild(new ContainerControl { Location = new Float2(0f, 0f), Size = new Float2(parent.Width, thick), BackgroundColor = color });
            parent.AddChild(new ContainerControl { Location = new Float2(parent.Width - thick, 0f), Size = new Float2(thick, parent.Height), BackgroundColor = color });
        }

        private void BuildAppearanceTabs()
        {
            _tabPanel = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(220f, 36f),
                BackgroundColor = new Color(20f / 255f, 23f / 255f, 30f / 255f, 0.85f),
            };
            _modelStage.AddChild(_tabPanel);

            string[] tabs = { "发型", "脸型", "肤色" };
            float tabW = _tabPanel.Width / 3f;
            for (int i = 0; i < 3; i++)
            {
                var tab = new InkButton
                {
                    Variant = InkButtonVariant.Ghost,
                    ButtonSize = InkButtonSize.Sm,
                    Text = tabs[i],
                    Location = new Float2(i * tabW, 2f),
                    Size = new Float2(tabW - 2f, 32f),
                    BackgroundColor = i == 0 ? InkWashTheme.BgHover : Color.Transparent,
                    BorderColor = i == 0 ? InkWashTheme.BorderGoldSubtle : Color.Transparent,
                    BorderThickness = i == 0 ? 1f : 0f,
                    TextColor = i == 0 ? InkWashTheme.TextBrand : InkWashTheme.TextSecondary,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 13f),
                };
                _tabPanel.AddChild(tab);
            }
        }

        // ===================================================================
        // Right Column - Attribute Panels
        // ===================================================================

        private void BuildRightColumn()
        {
            _rightPanel = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(Width * LeftPanelRatio + Padding * 1.5f, TopBarHeight),
                Size = new Float2(Width * (1f - LeftPanelRatio) - Padding * 2, Height - TopBarHeight - BottomBarHeight),
                ClipChildren = false,
            };
            AddChild(_rightPanel);

            float ry = Padding;
            float rw = _rightPanel.Width - Padding * 2;

            ry = BuildFiveAttributesCard(ry, rw);
            ry = BuildFiveElementsCard(ry, rw);
            ry = BuildBaseStatsCard(ry, rw);
            ry = BuildCombatCard(ry, rw);
            BuildEquipmentDetailCard(ry, rw);
        }

        private float BuildFiveAttributesCard(float y, float w)
        {
            var card = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(Padding, y),
                Size = new Float2(w, 110f),
                BackgroundColor = InkWashTheme.BaseSecondary,
            };
            _rightPanel.AddChild(card);

            var header = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(8f, 6f),
                Size = new Float2(160f, 20f),
                Text = "五维属性",
                TextColor = InkWashTheme.GoldPrimary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 14f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            card.AddChild(header);

            var sub = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(130f, 6f),
                Size = new Float2(140f, 20f),
                Text = "FIVE ATTRIBUTES",
                TextColor = InkWashTheme.TextTertiary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            card.AddChild(sub);

            string[] names = { "体", "御", "敏", "势", "劲" };
            float[] vals = { 60f, 45f, 80f, 50f, 95f };
            float cellW = (w - 16f) / 5f;

            for (int i = 0; i < 5; i++)
            {
                var nLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(8f + i * cellW, 34f),
                    Size = new Float2(cellW, 18f),
                    Text = names[i],
                    TextColor = InkWashTheme.GoldPrimary,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 14f),
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                };
                card.AddChild(nLabel);

                var vLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(8f + i * cellW, 52f),
                    Size = new Float2(cellW, 18f),
                    Text = ((int)vals[i]).ToString(),
                    TextColor = InkWashTheme.TextBrand,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 13f),
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                };
                card.AddChild(vLabel);

                float barW = cellW * 0.7f;
                var barBg = new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(8f + i * cellW + (cellW - barW) * 0.5f, 78f),
                    Size = new Float2(barW, 4f),
                    BackgroundColor = InkWashTheme.BorderNeutralL1,
                };
                card.AddChild(barBg);

                var barFill = new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = Float2.Zero,
                    Size = new Float2(barW * (vals[i] / 100f), barBg.Height),
                    BackgroundColor = InkWashTheme.GoldPrimary,
                };
                barBg.AddChild(barFill);
            }

            return y + card.Height + Padding;
        }

        private float BuildFiveElementsCard(float y, float w)
        {
            var card = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(Padding, y),
                Size = new Float2(w, 170f),
                BackgroundColor = InkWashTheme.BaseSecondary,
            };
            _rightPanel.AddChild(card);

            var header = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(8f, 6f),
                Size = new Float2(160f, 20f),
                Text = "五行体质",
                TextColor = InkWashTheme.GoldPrimary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 14f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            card.AddChild(header);

            var sub = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(130f, 6f),
                Size = new Float2(140f, 20f),
                Text = "FIVE ELEMENTS",
                TextColor = InkWashTheme.TextTertiary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            card.AddChild(sub);

            string[] names = { "金", "木", "水", "火", "土" };
            float[] vals = { 75f, 60f, 80f, 65f, 70f };
            Color[] elemColors =
            {
                InkWashTheme.ElementMetal,
                InkWashTheme.ElementWood,
                InkWashTheme.ElementWater,
                InkWashTheme.ElementFire,
                InkWashTheme.ElementEarth,
            };
            float barH = 12f;
            float barGap = 26f;
            float nameW = 24f;
            float pctW = 30f;

            for (int i = 0; i < 5; i++)
            {
                float rowY = 34f + i * barGap;

                var nLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(8f, rowY),
                    Size = new Float2(nameW, barH + 4f),
                    Text = names[i],
                    TextColor = elemColors[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 13f),
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                };
                card.AddChild(nLabel);

                float barW = w - 16f - nameW - pctW - 10f;
                var barBg = new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(8f + nameW + 6f, rowY + 2f),
                    Size = new Float2(barW, barH),
                    BackgroundColor = InkWashTheme.BorderNeutralL1,
                };
                card.AddChild(barBg);

                var barFill = new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = Float2.Zero,
                    Size = new Float2(barW * (vals[i] / 100f), barH),
                    BackgroundColor = elemColors[i],
                };
                barBg.AddChild(barFill);

                var vLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(8f + nameW + 6f + barW + 4f, rowY),
                    Size = new Float2(pctW, barH + 4f),
                    Text = ((int)vals[i]).ToString(),
                    TextColor = InkWashTheme.TextSecondary,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 11f),
                    HorizontalAlignment = TextAlignment.Far,
                    VerticalAlignment = TextAlignment.Center,
                };
                card.AddChild(vLabel);
            }

            return y + card.Height + Padding;
        }

        private float BuildBaseStatsCard(float y, float w)
        {
            var card = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(Padding, y),
                Size = new Float2(w, 130f),
                BackgroundColor = InkWashTheme.BaseSecondary,
            };
            _rightPanel.AddChild(card);

            var header = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(8f, 6f),
                Size = new Float2(160f, 20f),
                Text = "基础属性",
                TextColor = InkWashTheme.GoldPrimary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 14f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            card.AddChild(header);

            var sub = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(130f, 6f),
                Size = new Float2(140f, 20f),
                Text = "BASE STATS",
                TextColor = InkWashTheme.TextTertiary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            card.AddChild(sub);

            string[] statNames = { "攻击力", "防御力", "暴击率", "暴击伤害", "命中率", "闪避率" };
            string[] statValues = { "1234", "567", "45%", "250%", "95%", "12%" };
            float colW = (w - 16f) / 3f;
            float rowH = 30f;

            for (int i = 0; i < 6; i++)
            {
                int col = i % 3;
                int row = i / 3;
                float sx = 8f + col * colW;
                float sy = 34f + row * rowH;

                var nLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(sx, sy),
                    Size = new Float2(colW * 0.55f, rowH),
                    Text = statNames[i],
                    TextColor = InkWashTheme.TextSecondary,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                card.AddChild(nLabel);

                var vLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(sx + colW * 0.55f, sy),
                    Size = new Float2(colW * 0.45f, rowH),
                    Text = statValues[i],
                    TextColor = InkWashTheme.TextDefault,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 14f),
                    HorizontalAlignment = TextAlignment.Far,
                    VerticalAlignment = TextAlignment.Center,
                };
                card.AddChild(vLabel);
            }

            return y + card.Height + Padding;
        }

        private float BuildCombatCard(float y, float w)
        {
            var card = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(Padding, y),
                Size = new Float2(w, 210f),
                BackgroundColor = InkWashTheme.BaseSecondary,
            };
            _rightPanel.AddChild(card);

            var header = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(8f, 6f),
                Size = new Float2(160f, 20f),
                Text = "战斗属性",
                TextColor = InkWashTheme.GoldPrimary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 14f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            card.AddChild(header);

            var sub = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(130f, 6f),
                Size = new Float2(80f, 20f),
                Text = "COMBAT",
                TextColor = InkWashTheme.TextTertiary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            card.AddChild(sub);

            var powerLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(8f, 32f),
                Size = new Float2(60f, 28f),
                Text = "战斗力",
                TextColor = InkWashTheme.TextSecondary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 12f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            card.AddChild(powerLabel);

            var powerVal = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(72f, 28f),
                Size = new Float2(180f, 36f),
                Text = "45,678",
                TextColor = InkWashTheme.GoldPrimary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 32f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            card.AddChild(powerVal);

            var separator = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(8f, 68f),
                Size = new Float2(w - 16f, 1f),
                BackgroundColor = InkWashTheme.BorderNeutralL2,
            };
            card.AddChild(separator);

            string[] combatNames = { "境界", "门派", "阵营", "侠义值", "恶名值", "修炼经验" };
            string[] combatValues = { "筑基后期", "武当派", "正派", "1200", "0", "82%" };
            float cColW = (w - 16f) / 2f;

            for (int i = 0; i < 6; i++)
            {
                int col = i % 2;
                int row = i / 2;
                float cy = 76f + row * 34f;
                float cx = 8f + col * cColW;

                var nLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(cx, cy),
                    Size = new Float2(cColW * 0.5f, 28f),
                    Text = combatNames[i],
                    TextColor = InkWashTheme.TextSecondary,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                card.AddChild(nLabel);

                var vLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(cx + cColW * 0.5f, cy),
                    Size = new Float2(cColW * 0.5f, 28f),
                    Text = combatValues[i],
                    TextColor = InkWashTheme.TextDefault,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 12f),
                    HorizontalAlignment = TextAlignment.Far,
                    VerticalAlignment = TextAlignment.Center,
                };
                card.AddChild(vLabel);
            }

            return y + card.Height + Padding;
        }

        private void BuildEquipmentDetailCard(float y, float w)
        {
            var card = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(Padding, y),
                Size = new Float2(w, 130f),
                BackgroundColor = InkWashTheme.BaseSecondary,
            };
            _rightPanel.AddChild(card);

            var header = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(8f, 6f),
                Size = new Float2(160f, 20f),
                Text = "装备详情",
                TextColor = InkWashTheme.GoldPrimary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 14f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            card.AddChild(header);

            var sub = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(120f, 6f),
                Size = new Float2(60f, 20f),
                Text = "DETAIL",
                TextColor = InkWashTheme.TextTertiary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 9f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            card.AddChild(sub);

            var iconBg = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(8f, 34f),
                Size = new Float2(52f, 52f),
                BackgroundColor = InkWashTheme.Void,
            };
            card.AddChild(iconBg);

            var iconLabel = new Label
            {
                AnchorPreset = AnchorPresets.StretchAll,
                Offsets = Margin.Zero,
                Text = "\u2694",
                TextColor = InkWashTheme.GoldPrimary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 22f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            iconBg.AddChild(iconLabel);

            var itemName = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(68f, 34f),
                Size = new Float2(w - 84f, 22f),
                Text = "玄铁剑",
                TextColor = InkWashTheme.QualityLegendary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 15f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            card.AddChild(itemName);

            var itemInfo = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(68f, 56f),
                Size = new Float2(w - 84f, 18f),
                Text = "五行: 金  类型: 长剑",
                TextColor = InkWashTheme.TextSecondary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            card.AddChild(itemInfo);

            var durLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(8f, 100f),
                Size = new Float2(30f, 14f),
                Text = "耐久",
                TextColor = InkWashTheme.TextTertiary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            card.AddChild(durLabel);

            float durBarW = w - 16f - 30f - 80f;
            if (durBarW < 20f) durBarW = 20f;
            var durBarBg = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(38f, 104f),
                Size = new Float2(durBarW, 6f),
                BackgroundColor = new Color(0f, 0f, 0f, 0.5f),
            };
            card.AddChild(durBarBg);

            var durBarFill = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Float2(durBarW * 0.85f, durBarBg.Height),
                BackgroundColor = InkWashTheme.JadePrimary,
            };
            durBarBg.AddChild(durBarFill);

            var durVal = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(38f + durBarW + 4f, 100f),
                Size = new Float2(80f, 14f),
                Text = "850/1000",
                TextColor = InkWashTheme.TextDefault,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 10f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            card.AddChild(durVal);
        }

        // ===================================================================
        // Bottom Navigation Bar
        // ===================================================================

        private void BuildBottomBar()
        {
            _bottomBar = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, Height - BottomBarHeight),
                Size = new Float2(Width, BottomBarHeight),
                BackgroundColor = InkWashTheme.BaseSecondary,
            };
            AddChild(_bottomBar);

            var topBorder = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Float2(Width, 1f),
                BackgroundColor = InkWashTheme.BorderGoldSubtle,
            };
            _bottomBar.AddChild(topBorder);

            float btnY = (BottomBarHeight - NavButtonHeight) * 0.5f;

            _btnReturnHud = new InkButton
            {
                Variant = InkButtonVariant.Default,
                ButtonSize = InkButtonSize.Md,
                Text = "返回沉浸模式",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(Padding, btnY),
                Size = new Float2(NavButtonWidth, NavButtonHeight),
            };
            _btnReturnHud.Clicked += OnReturnHudClicked;
            _bottomBar.AddChild(_btnReturnHud);

            float tabStartX = Padding + NavButtonWidth + NavButtonGap * 2;

            _tabAttributes = new InkTabButton
            {
                Text = "角色属性",
                IsSelected = true,
                ParticleSystem = ParticleSystem,
            };
            _tabAttributes.Clicked += OnTabAttributesClicked;
            _bottomBar.AddChild(_tabAttributes);

            _tabSkill = new InkTabButton
            {
                Text = "武学面板",
                ParticleSystem = ParticleSystem,
            };
            _tabSkill.Clicked += OnGotoSkillClicked;
            _bottomBar.AddChild(_tabSkill);

            _tabInventory = new InkTabButton
            {
                Text = "背包装囊",
                ParticleSystem = ParticleSystem,
            };
            _tabInventory.Clicked += OnGotoInventoryClicked;
            _bottomBar.AddChild(_tabInventory);

            _navTabs = new[] { _tabAttributes, _tabSkill, _tabInventory };
        }

        // ===================================================================
        // Event Handlers
        // ===================================================================

        private void OnCloseClicked()
        {
            EmitGoldAtButton(_closeButton);
            NavigationRequested?.Invoke("combat-hud");
        }

        private void SelectTab(InkTabButton selected)
        {
            foreach (var tab in _navTabs)
                tab.IsSelected = tab == selected;
        }

        private void OnTabAttributesClicked()
        {
            SelectTab(_tabAttributes);
        }

        private void OnSlotClicked(int index)
        {
            if (_floatingSlots != null && index >= 0 && index < _floatingSlots.Length)
            {
                EmitGoldAtButton(_floatingSlots[index]);
                _activeSlotIndex = index;
                ShowSlotBubble(index);
            }
        }

        private void OnSlotHovered(int index)
        {
        }

        private void OnReturnHudClicked()
        {
            EmitGoldAtButton(_btnReturnHud);
            NavigationRequested?.Invoke("combat-hud");
        }

        private void OnGotoSkillClicked()
        {
            SelectTab(_tabSkill);
            EmitGoldAtButton(_tabSkill);
            NavigationRequested?.Invoke("nav-skill-panel");
        }

        private void OnGotoInventoryClicked()
        {
            SelectTab(_tabInventory);
            EmitGoldAtButton(_tabInventory);
            NavigationRequested?.Invoke("nav-inventory");
        }

        private void EmitGoldAtButton(Control target)
        {
            try
            {
                if (ParticleSystem == null || target == null)
                    return;

                var center = new Float2(target.Width * 0.5f, target.Height * 0.5f);
                var screenPos = target.PointToScreen(center);
                var localPos = ParticleSystem.PointFromScreen(screenPos);
                ParticleSystem.EmitGoldBurst(localPos, count: 14, isLarge: false);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogWarning($"[CharacterPanelPage] EmitGold: {ex.Message}");
            }
        }

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);
        }
    }
}

using FlaxEngine;
using FlaxEngine.GUI;
using Game.Character.Attributes;
using Game.Combat.Skills;
using HundunWorld.Game.UI.Ink.Components;
using HundunWorld.Game.UI.StyleSystem;
using System;

namespace HundunWorld.Game.UI.Ink.Pages.Combat
{
    public class CombatHudTraditionalPage : ContainerControl, IInkPage
    {
        // ===================================================================
        // 颜色快捷引用
        // =======================================================================
        private static readonly Color BloodFaint = new Color(
            InkWashTheme.BloodPrimary.R, InkWashTheme.BloodPrimary.G,
            InkWashTheme.BloodPrimary.B, 0.18f);
        private static readonly Color GoldTrace = new Color(
            InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G,
            InkWashTheme.GoldPrimary.B, 0.08f);

        // ===================================================================
        // 布局常量
        // =======================================================================
        private static readonly Float2 CharPanelSize = new Float2(300f, 90f);
        private static readonly Float2 PartyPanelSize = new Float2(300f, 154f);
        private const float MinimapContainerSize = 200f;
        private const float MinimapMapSize = 160f;
        private const float MinimapBtnSize = 28f;
        private static readonly Float2 QuestTrackerSize = new Float2(220f, 170f);
        private const float NavBtnSizeX = 50f;
        private const float NavBtnSizeY = 36f;
        private const float NavBarGap = 2f;
        private const float NavBtnIconSize = 14f;
        private const float NavBtnTextSize = 9f;
        private static readonly Float2 SkillPanelSize = new Float2(560f, 156f);
        private const float SkillSlotSize = 36f;
        private const float SkillSlotGap = 2f;
        private const float SkillRowGap = 4f;
        private const float HeartMethodSize = 36f;
        private static readonly Float2 ChatBoxSize = new Float2(340f, 210f);
        private const float FuncBtnWidth = 42f;
        private const float FuncBtnHeight = 38f;

        // ===================================================================
        // 子控件引用
        // =======================================================================
        private InkPanel _targetInfoPanel;
        private InkBar _targetHpBar;
        private Label _targetHpLabel;

        private InkPanel _charPanel;
        private Label _charNameLabel;
        private Label _factionLabel;
        private ContainerControl _levelBadge;
        private Label _levelBadgeLabel;
        private InkBar _hpBar;
        private Label _hpLabel;
        private InkBar _mpBar;
        private Label _mpLabel;
        private InkBar _expBar;
        private Label _expLabel;

        private InkPanel _partyPanel;

        private ContainerControl _minimapContainer;
        private InkMinimap _minimap;
        private Label _minimapAreaLabel;
        private Label _minimapCoordLabel;

        private InkPanel _questTracker;

        private InkPanel _navBar;

        private InkPanel _skillPanel;

        private InkPanel _chatBox;

        private InkPanel _funcButtons;

        // ===================================================================
        // 数据绑定
        // =======================================================================
        private CharacterAttributesComponent _boundCharacter;
        private SkillBase[] _boundSkills;
        private float _minimapPlayerYaw;
        private float _mockExpProgress = 0.45f;

        // ===================================================================
        // 公共 API
        // =======================================================================
        public event Action<string> NavigationRequested;
        public InkParticleSystem ParticleSystem { get; set; }

        public float MinimapPlayerYaw
        {
            get => _minimapPlayerYaw;
            set => _minimapPlayerYaw = ((value % 360f) + 360f) % 360f;
        }

        public void BindCharacter(CharacterAttributesComponent component)
        {
            _boundCharacter = component;
            RefreshBoundCharacterDisplay();
        }

        public void BindSkills(SkillBase[] slots)
        {
            _boundSkills = slots;
        }

        private void RefreshBoundCharacterDisplay()
        {
            if (_boundCharacter == null) return;
            try
            {
                var c = _boundCharacter;
                if (_charNameLabel != null)
                    _charNameLabel.Text = !string.IsNullOrEmpty(c.Nickname) ? c.Nickname : "无名侠";
                if (_levelBadgeLabel != null)
                    _levelBadgeLabel.Text = c.Level.ToString();
                if (_hpBar != null && c.MaxHealth > 0f)
                    _hpBar.Value = Mathf.Saturate(c.CurrentHealth / c.MaxHealth);
                if (_hpLabel != null)
                    _hpLabel.Text = $"{(int)c.CurrentHealth}/{(int)c.MaxHealth}";
                if (_mpBar != null && c.MaxEnergy > 0f)
                    _mpBar.Value = Mathf.Saturate(c.CurrentEnergy / c.MaxEnergy);
                if (_mpLabel != null)
                    _mpLabel.Text = $"{(int)c.CurrentEnergy}/{(int)c.MaxEnergy}";
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[CombatHudTraditionalPage] RefreshBoundCharacterDisplay: {ex.Message}");
            }
        }

        // ===================================================================
        // 按钮控件（支持悬停+点击，图标文字由子控件实现）
        // =======================================================================
        private class InkGlyphBtn : ContainerControl
        {
            public event Action<InkGlyphBtn> Clicked;
            public string DomId { get; set; }

            public InkGlyphBtn(float w, float h)
            {
                Size = new Float2(w, h);
                BackgroundColor = Color.Transparent;
                ClipChildren = false;
            }

            public override void OnMouseEnter(Float2 location)
            {
                BackgroundColor = InkWashTheme.BgHover;
            }

            public override void OnMouseLeave()
            {
                BackgroundColor = Color.Transparent;
            }

            public override bool OnMouseUp(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left && ContainsPoint(ref location))
                {
                    Clicked?.Invoke(this);
                    return true;
                }
                return false;
            }
        }

        private InkGlyphBtn MakeNavBtn(string glyph, string text, float w, float h, float iconSize, float textSize)
        {
            var btn = new InkGlyphBtn(w, h);

            var glyphLbl = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, (h - iconSize - textSize - 4f) * 0.5f),
                Size = new Float2(w, iconSize + 4f),
                Text = glyph,
                TextColor = InkWashTheme.GoldPrimary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), iconSize),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            btn.AddChild(glyphLbl);

            var textLbl = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, (h - iconSize - textSize - 4f) * 0.5f + iconSize + 4f),
                Size = new Float2(w, textSize),
                Text = text,
                TextColor = InkWashTheme.TextSecondary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), textSize),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            btn.AddChild(textLbl);

            return btn;
        }

        // ===================================================================
        // 构造函数
        // =======================================================================
        public CombatHudTraditionalPage()
        {
            try
            {
                AnchorPreset = AnchorPresets.StretchAll;
                Offsets = Margin.Zero;
                BackgroundColor = Color.Transparent;
                ClipChildren = false;
                AutoFocus = false;

                BuildTargetInfo();
                BuildCharacterPanel();
                BuildPartyList();
                BuildMinimap();
                BuildQuestTracker();
                BuildNavBar();
                BuildSkillBars();
                BuildChatBox();
                BuildFunctionButtons();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[CombatHudTraditionalPage] 初始化失败: {ex.Message}");
            }
        }

        // ===================================================================
        // Build 方法
        // =======================================================================

        private Label MakeLabel(string text, float x, float y, float w, float h, Color color, float fontSize)
        {
            return new Label
            {
                Text = text,
                Location = new Float2(x, y),
                Size = new Float2(w, h),
                TextColor = color,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), fontSize),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
            };
        }

        private InkBar MakeBar(float x, float y, float w, float h, float val, InkBarFillVariant variant)
        {
            return new InkBar
            {
                Location = new Float2(x, y),
                Size = new Float2(w, h),
                Value = val,
                FillVariant = variant,
                AnchorPreset = AnchorPresets.TopLeft,
            };
        }

        private void BuildTargetInfo()
        {
            _targetInfoPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(300f, 64f),
            };

            var avatar = new ContainerControl
            {
                Location = new Float2(8f, 8f),
                Size = new Float2(32f, 32f),
                BackgroundColor = BloodFaint,
                AnchorPreset = AnchorPresets.TopLeft,
            };
            _targetInfoPanel.AddChild(avatar);

            var avatarChar = new Label
            {
                AnchorPreset = AnchorPresets.StretchAll,
                Text = "魁",
                TextColor = InkWashTheme.BloodBright,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 14f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            avatar.AddChild(avatarChar);

            _targetInfoPanel.AddChild(MakeLabel("黑风寨首领", 46f, 8f, 160f, 18f, InkWashTheme.TextDefault, 13f));

            var levelLbl = new Label
            {
                Location = new Float2(210f, 8f),
                Size = new Float2(80f, 18f),
                Text = "Lv.62",
                TextColor = InkWashTheme.BloodBright,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 11f),
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
            };
            _targetInfoPanel.AddChild(levelLbl);

            _targetHpBar = MakeBar(46f, 28f, 244f, 8f, 0.65f, InkBarFillVariant.Blood);
            _targetInfoPanel.AddChild(_targetHpBar);

            _targetHpLabel = MakeLabel("9750/15000", 46f, 38f, 180f, 14f, InkWashTheme.TextTertiary, 9f);
            _targetInfoPanel.AddChild(_targetHpLabel);

            _targetInfoPanel.AddChild(MakeLabel("精英", 230f, 38f, 60f, 14f, InkWashTheme.TextTertiary, 9f));

            AddChild(_targetInfoPanel);
        }

        private void BuildCharacterPanel()
        {
            _charPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = CharPanelSize,
            };

            var avatarBg = new ContainerControl
            {
                Location = new Float2(10f, 10f),
                Size = new Float2(56f, 56f),
                BackgroundColor = GoldTrace,
                AnchorPreset = AnchorPresets.TopLeft,
            };
            _charPanel.AddChild(avatarBg);

            var avatarChar = new Label
            {
                AnchorPreset = AnchorPresets.StretchAll,
                Text = "侠",
                TextColor = InkWashTheme.GoldPrimary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 28f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            avatarBg.AddChild(avatarChar);

            _levelBadge = new ContainerControl
            {
                Location = new Float2(42f, 44f),
                Size = new Float2(22f, 22f),
                BackgroundColor = InkWashTheme.Void,
                AnchorPreset = AnchorPresets.TopLeft,
            };
            _charPanel.AddChild(_levelBadge);

            _levelBadgeLabel = new Label
            {
                AnchorPreset = AnchorPresets.StretchAll,
                Text = "60",
                TextColor = InkWashTheme.GoldBright,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 10f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            _levelBadge.AddChild(_levelBadgeLabel);

            _charNameLabel = MakeLabel("逍遥客", 74f, 10f, 140f, 18f, InkWashTheme.TextDefault, 14f);
            _charNameLabel.Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 14f);
            _charPanel.AddChild(_charNameLabel);

            _factionLabel = new Label
            {
                Location = new Float2(218f, 10f),
                Size = new Float2(72f, 18f),
                Text = "武当",
                TextColor = InkWashTheme.JadeBright,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 10f),
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
            };
            _charPanel.AddChild(_factionLabel);

            float barLeft = 74f;
            float barRight = 286f;
            float barW = barRight - barLeft;

            var hpHint = new Label
            {
                Location = new Float2(barLeft - 14f, 30f),
                Size = new Float2(12f, 12f),
                Text = "血",
                TextColor = InkWashTheme.TextSecondary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 9f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
            };
            _charPanel.AddChild(hpHint);

            _hpBar = MakeBar(barLeft, 30f, barW, 12f, 0.83f, InkBarFillVariant.Blood);
            _charPanel.AddChild(_hpBar);

            _hpLabel = new Label
            {
                Location = new Float2(barLeft, 30f),
                Size = new Float2(barW, 12f),
                Text = "12450/15000",
                TextColor = InkWashTheme.TextDefault,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 9f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
            };
            _charPanel.AddChild(_hpLabel);

            var mpHint = new Label
            {
                Location = new Float2(barLeft - 14f, 44f),
                Size = new Float2(12f, 10f),
                Text = "气",
                TextColor = InkWashTheme.TextSecondary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 9f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
            };
            _charPanel.AddChild(mpHint);

            _mpBar = MakeBar(barLeft, 44f, barW, 10f, 0.80f, InkBarFillVariant.Jade);
            _charPanel.AddChild(_mpBar);

            _mpLabel = new Label
            {
                Location = new Float2(barLeft, 44f),
                Size = new Float2(barW, 10f),
                Text = "800/1000",
                TextColor = InkWashTheme.TextDefault,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 8f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
            };
            _charPanel.AddChild(_mpLabel);

            var expHint = new Label
            {
                Location = new Float2(barLeft - 14f, 56f),
                Size = new Float2(12f, 8f),
                Text = "修",
                TextColor = InkWashTheme.TextTertiary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 8f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
            };
            _charPanel.AddChild(expHint);

            _expBar = MakeBar(barLeft, 56f, barW, 4f, _mockExpProgress, InkBarFillVariant.Gold);
            _charPanel.AddChild(_expBar);

            _expLabel = MakeLabel("45%", barLeft + barW + 2f, 56f, 30f, 8f, InkWashTheme.TextTertiary, 8f);
            _expLabel.Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 8f);
            _charPanel.AddChild(_expLabel);

            var buffDefs = new[]
            {
                (glyph: "极", color: InkWashTheme.GoldBright),
                (glyph: "罡", color: InkWashTheme.JadeBright),
                (glyph: "风", color: InkWashTheme.BloodBright),
            };

            float buffX = 10f;
            for (int i = 0; i < buffDefs.Length; i++)
            {
                var cell = new ContainerControl
                {
                    Location = new Float2(buffX, 66f),
                    Size = new Float2(20f, 20f),
                    BackgroundColor = GoldTrace,
                    AnchorPreset = AnchorPresets.TopLeft,
                };
                var glyph = new Label
                {
                    AnchorPreset = AnchorPresets.StretchAll,
                    Text = buffDefs[i].glyph,
                    TextColor = buffDefs[i].color,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 10f),
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                };
                cell.AddChild(glyph);
                _charPanel.AddChild(cell);
                buffX += 22f;
            }

            var addCell = new ContainerControl
            {
                Location = new Float2(buffX, 66f),
                Size = new Float2(20f, 20f),
                BackgroundColor = InkWashTheme.BaseElevated,
                AnchorPreset = AnchorPresets.TopLeft,
            };
            var addGlyph = new Label
            {
                AnchorPreset = AnchorPresets.StretchAll,
                Text = "+",
                TextColor = InkWashTheme.TextTertiary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 12f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            addCell.AddChild(addGlyph);
            _charPanel.AddChild(addCell);

            AddChild(_charPanel);
        }

        private void BuildPartyList()
        {
            _partyPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = PartyPanelSize,
            };

            _partyPanel.AddChild(MakeLabel("队", 8f, 6f, 14f, 14f, InkWashTheme.GoldPrimary, 11f));
            _partyPanel.AddChild(MakeLabel("队伍", 22f, 6f, 80f, 14f, InkWashTheme.TextSecondary, 11f));

            var countLbl = new Label
            {
                Location = new Float2(240f, 6f),
                Size = new Float2(50f, 14f),
                Text = "4/5",
                TextColor = InkWashTheme.TextTertiary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 10f),
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
            };
            _partyPanel.AddChild(countLbl);

            var members = new[]
            {
                (glyph: "青", color: InkWashTheme.JadeBright, name: "青衣客", level: "Lv.58", hp: 0.85f),
                (glyph: "红", color: InkWashTheme.BloodBright, name: "红袖招", level: "Lv.57", hp: 0.92f),
                (glyph: "醉", color: InkWashTheme.GoldBright, name: "醉道人", level: "Lv.60", hp: 1.0f),
                (glyph: "玉", color: InkWashTheme.QualityEpic, name: "玉面狐", level: "Lv.55", hp: 0.70f),
            };

            float mY = 22f;
            foreach (var m in members)
            {
                var avatar = new ContainerControl
                {
                    Location = new Float2(8f, mY + 3f),
                    Size = new Float2(24f, 24f),
                    BackgroundColor = InkWashTheme.BaseElevated,
                    AnchorPreset = AnchorPresets.TopLeft,
                };
                var glyph = new Label
                {
                    AnchorPreset = AnchorPresets.StretchAll,
                    Text = m.glyph,
                    TextColor = m.color,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 11f),
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                };
                avatar.AddChild(glyph);
                _partyPanel.AddChild(avatar);

                _partyPanel.AddChild(MakeLabel(m.name, 36f, mY + 3f, 120f, 14f, InkWashTheme.TextDefault, 10f));

                var lvlLbl = new Label
                {
                    Location = new Float2(240f, mY + 3f),
                    Size = new Float2(50f, 14f),
                    Text = m.level,
                    TextColor = InkWashTheme.TextTertiary,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 9f),
                    HorizontalAlignment = TextAlignment.Far,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.TopLeft,
                };
                _partyPanel.AddChild(lvlLbl);

                _partyPanel.AddChild(MakeBar(36f, mY + 19f, 254f, 5f, m.hp, InkBarFillVariant.Blood));

                mY += 32f;
            }

            AddChild(_partyPanel);
        }

        private void BuildMinimap()
        {
            _minimapContainer = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(MinimapContainerSize, MinimapContainerSize + 24f),
            };

            _minimap = new InkMinimap
            {
                Location = new Float2(20f, 20f),
                Size = new Float2(MinimapMapSize, MinimapMapSize),
                AnchorPreset = AnchorPresets.TopLeft,
            };
            _minimap.AddLandmark(0.1f, 0.2f, 0.3f, new Color(InkWashTheme.JadeFaint.R, InkWashTheme.JadeFaint.G, InkWashTheme.JadeFaint.B, 0.5f));
            _minimap.AddLandmark(-0.2f, -0.15f, 0.25f, new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.15f));
            _minimap.AddLandmark(0.4f, -0.3f, 0.15f, new Color(InkWashTheme.JadeFaint.R, InkWashTheme.JadeFaint.G, InkWashTheme.JadeFaint.B, 0.3f));
            _minimap.AddEntity(InkMinimapEntityType.Player, 0f, 0f);
            _minimap.AddEntity(InkMinimapEntityType.Friendly, 0.24f, -0.32f);
            _minimap.AddEntity(InkMinimapEntityType.Enemy, -0.24f, 0.36f);
            _minimap.AddEntity(InkMinimapEntityType.NPC, 0.44f, -0.16f);
            _minimap.AddEntity(InkMinimapEntityType.NPC, -0.1f, 0.5f);
            _minimapContainer.AddChild(_minimap);

            var btnDefs = new[]
            {
                (lx: 0.5f, ly: 0f,  icon: "图"),
                (lx: 0.85f, ly: 0.15f, icon: "+"),
                (lx: 1f, ly: 0.5f, icon: "-"),
                (lx: 0.85f, ly: 0.85f, icon: "任"),
                (lx: 0.5f, ly: 1f,  icon: "队"),
                (lx: 0.15f, ly: 0.85f, icon: "邮"),
                (lx: 0f, ly: 0.5f,  icon: "设"),
                (lx: 0.15f, ly: 0.15f, icon: "助"),
            };

            float halfBtn = MinimapBtnSize * 0.5f;
            foreach (var btnDef in btnDefs)
            {
                float bx = btnDef.lx * MinimapContainerSize - halfBtn;
                float by = btnDef.ly * MinimapContainerSize - halfBtn;

                var btn = new ContainerControl
                {
                    Location = new Float2(bx, by),
                    Size = new Float2(MinimapBtnSize, MinimapBtnSize),
                    BackgroundColor = InkWashTheme.Void,
                    AnchorPreset = AnchorPresets.TopLeft,
                };
                var glyph = new Label
                {
                    AnchorPreset = AnchorPresets.StretchAll,
                    Text = btnDef.icon,
                    TextColor = InkWashTheme.GoldPrimary,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 12f),
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                };
                btn.AddChild(glyph);
                _minimapContainer.AddChild(btn);
            }

            var coordPanel = new ContainerControl
            {
                Location = new Float2(0f, MinimapContainerSize),
                Size = new Float2(MinimapContainerSize, 24f),
                BackgroundColor = InkWashTheme.Panel,
                AnchorPreset = AnchorPresets.TopLeft,
            };

            _minimapAreaLabel = MakeLabel("洛阳城", 10f, 4f, 100f, 16f, InkWashTheme.GoldBright, 11f);
            _minimapAreaLabel.Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 11f);
            coordPanel.AddChild(_minimapAreaLabel);

            _minimapCoordLabel = new Label
            {
                Location = new Float2(110f, 4f),
                Size = new Float2(80f, 16f),
                Text = "1234, 567",
                TextColor = InkWashTheme.TextTertiary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 10f),
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
            };
            coordPanel.AddChild(_minimapCoordLabel);

            _minimapContainer.AddChild(coordPanel);
            AddChild(_minimapContainer);
        }

        private void BuildQuestTracker()
        {
            _questTracker = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = QuestTrackerSize,
            };

            _questTracker.AddChild(MakeLabel("任", 8f, 6f, 14f, 14f, InkWashTheme.GoldPrimary, 11f));

            var titleLbl = MakeLabel("任务追踪", 22f, 6f, 120f, 14f, InkWashTheme.GoldBright, 12f);
            titleLbl.Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 12f);
            _questTracker.AddChild(titleLbl);

            var quests = new[]
            {
                (name: "黑风寨剿匪", count: "8/10", progress: 0.8f, color: InkWashTheme.JadeBright),
                (name: "寻访名剑", count: "3/5", progress: 0.6f, color: InkWashTheme.GoldBright),
                (name: "门派日常", count: "1/3", progress: 0.33f, color: InkWashTheme.BloodBright),
            };

            float qY = 26f;
            foreach (var q in quests)
            {
                var nameLbl = MakeLabel(q.name, 10f, qY + 2f, 140f, 16f, InkWashTheme.TextDefault, 11f);
                nameLbl.Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 11f);
                _questTracker.AddChild(nameLbl);

                var countLbl = new Label
                {
                    Location = new Float2(150f, qY + 2f),
                    Size = new Float2(60f, 16f),
                    Text = q.count,
                    TextColor = q.color,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 9f),
                    HorizontalAlignment = TextAlignment.Far,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.TopLeft,
                };
                _questTracker.AddChild(countLbl);

                _questTracker.AddChild(MakeBar(10f, qY + 20f, 200f, 3f, q.progress, InkBarFillVariant.Gold));
                qY += 38f;
            }

            AddChild(_questTracker);
        }

        private void BuildNavBar()
        {
            _navBar = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(832f, NavBtnSizeY + 6f),
            };

            var entries = new[]
            {
                (glyph: "人", label: "角色", domId: InkPageDomIds.NavCharacterPanel),
                (glyph: "技", label: "技能", domId: InkPageDomIds.NavSkillPanel),
                (glyph: "包", label: "背包", domId: InkPageDomIds.NavInventory),
                (glyph: "任", label: "任务", domId: InkPageDomIds.NavQuests),
                (glyph: "图", label: "地图", domId: InkPageDomIds.NavWorldMap),
                (glyph: "帮", label: "帮会", domId: InkPageDomIds.NavFriends),
                (glyph: "商", label: "商城", domId: InkPageDomIds.NavShop),
                (glyph: "路", label: "寻路", domId: InkPageDomIds.NavCompass),
                (glyph: "强", label: "强化", domId: InkPageDomIds.NavEquipmentEnhance),
                (glyph: "锻", label: "锻造", domId: InkPageDomIds.NavCrafting),
                (glyph: "骑", label: "坐骑", domId: InkPageDomIds.NavMountPet),
                (glyph: "友", label: "好友", domId: InkPageDomIds.NavFriends),
                (glyph: "邮", label: "邮件", domId: InkPageDomIds.NavMail),
                (glyph: "榜", label: "排行", domId: InkPageDomIds.NavLeaderboard),
                (glyph: "师", label: "师门", domId: InkPageDomIds.NavMentor),
                (glyph: "境", label: "秘境", domId: InkPageDomIds.NavDungeonEntry),
            };

            float cursorX = 2f;
            foreach (var e in entries)
            {
                var btn = MakeNavBtn(e.glyph, e.label, NavBtnSizeX, NavBtnSizeY, NavBtnIconSize, NavBtnTextSize);
                btn.Location = new Float2(cursorX, 3f);
                btn.DomId = e.domId;
                btn.Clicked += (b) => OnNavButtonClicked(b.DomId);
                _navBar.AddChild(btn);
                cursorX += NavBtnSizeX + NavBarGap;
            }

            AddChild(_navBar);
        }

        private void BuildSkillBars()
        {
            _skillPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = SkillPanelSize,
            };

            var rowGlyphs = new[]
            {
                new[] { "剑", "气", "掌", "绝", "步", "疗", "空", "空", "空", "空", "空", "空" },
                new[] { "刀", "盾", "闪", "破", "御", "空", "空", "空", "空", "空", "空", "空" },
                new[] { "丹", "药", "符", "空", "空", "空", "空", "空", "空", "空", "空", "空" },
                new[] { "空", "空", "空", "空", "空", "空", "空", "空", "空", "空", "空", "空" },
            };

            var rowColors = new[]
            {
                new[] { InkWashTheme.QualityRare, InkWashTheme.QualityUncommon, InkWashTheme.QualityEpic, InkWashTheme.QualityLegendary, InkWashTheme.GoldPrimary, InkWashTheme.JadeBright, InkWashTheme.TextTertiary, InkWashTheme.TextTertiary, InkWashTheme.TextTertiary, InkWashTheme.TextTertiary, InkWashTheme.TextTertiary, InkWashTheme.TextTertiary },
                new[] { InkWashTheme.QualityRare, InkWashTheme.QualityUncommon, InkWashTheme.GoldPrimary, InkWashTheme.BloodBright, InkWashTheme.JadeBright, InkWashTheme.TextTertiary, InkWashTheme.TextTertiary, InkWashTheme.TextTertiary, InkWashTheme.TextTertiary, InkWashTheme.TextTertiary, InkWashTheme.TextTertiary, InkWashTheme.TextTertiary },
                new[] { InkWashTheme.QualityUncommon, InkWashTheme.QualityUncommon, InkWashTheme.GoldPrimary, InkWashTheme.TextTertiary, InkWashTheme.TextTertiary, InkWashTheme.TextTertiary, InkWashTheme.TextTertiary, InkWashTheme.TextTertiary, InkWashTheme.TextTertiary, InkWashTheme.TextTertiary, InkWashTheme.TextTertiary, InkWashTheme.TextTertiary },
                new[] { InkWashTheme.TextTertiary, InkWashTheme.TextTertiary, InkWashTheme.TextTertiary, InkWashTheme.TextTertiary, InkWashTheme.TextTertiary, InkWashTheme.TextTertiary, InkWashTheme.TextTertiary, InkWashTheme.TextTertiary, InkWashTheme.TextTertiary, InkWashTheme.TextTertiary, InkWashTheme.TextTertiary, InkWashTheme.TextTertiary },
            };

            for (int row = 0; row < 4; row++)
            {
                float rowY = 4f + row * (SkillSlotSize + SkillRowGap);
                float startX = (row == 0) ? 4f + HeartMethodSize + 4f : 44f;
                float slotH = SkillSlotSize - ((row == 0) ? 0f : 4f);

                if (row == 0)
                {
                    var heartBtn = new ContainerControl
                    {
                        Location = new Float2(4f, (SkillSlotSize - HeartMethodSize) * 0.5f + 4f),
                        Size = new Float2(HeartMethodSize, HeartMethodSize),
                        BackgroundColor = InkWashTheme.Void,
                        AnchorPreset = AnchorPresets.TopLeft,
                    };
                    var heartGlyph = new Label
                    {
                        AnchorPreset = AnchorPresets.StretchAll,
                        Text = "心",
                        TextColor = InkWashTheme.GoldBright,
                        Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 14f),
                        HorizontalAlignment = TextAlignment.Center,
                        VerticalAlignment = TextAlignment.Center,
                    };
                    heartBtn.AddChild(heartGlyph);
                    _skillPanel.AddChild(heartBtn);
                }

                float slotX = startX;
                for (int i = 0; i < 12; i++)
                {
                    var slot = new ContainerControl
                    {
                        Location = new Float2(slotX, rowY + (SkillSlotSize - slotH) * 0.5f),
                        Size = new Float2(SkillSlotSize - 2f, slotH),
                        BackgroundColor = InkWashTheme.BaseElevated,
                        AnchorPreset = AnchorPresets.TopLeft,
                    };

                    var slotGlyph = new Label
                    {
                        AnchorPreset = AnchorPresets.StretchAll,
                        Text = rowGlyphs[row][i],
                        TextColor = rowColors[row][i],
                        Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), (row == 0) ? 16f : 14f),
                        HorizontalAlignment = TextAlignment.Center,
                        VerticalAlignment = TextAlignment.Center,
                    };
                    slot.AddChild(slotGlyph);

                    if (row == 0 && i == 0)
                    {
                        var hotkey = new Label
                        {
                            Location = new Float2(SkillSlotSize - 16f, 0f),
                            Size = new Float2(12f, 10f),
                            Text = "1",
                            TextColor = InkWashTheme.TextTertiary,
                            Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 8f),
                            HorizontalAlignment = TextAlignment.Far,
                            VerticalAlignment = TextAlignment.Near,
                            AnchorPreset = AnchorPresets.TopLeft,
                        };
                        slot.AddChild(hotkey);
                    }

                    _skillPanel.AddChild(slot);
                    slotX += SkillSlotSize + SkillSlotGap;
                }

                if (row == 0)
                {
                    var lockBtn = new ContainerControl
                    {
                        Location = new Float2(slotX + 2f, rowY + (SkillSlotSize - 36f) * 0.5f),
                        Size = new Float2(24f, 36f),
                        BackgroundColor = InkWashTheme.Void,
                        AnchorPreset = AnchorPresets.TopLeft,
                    };
                    var lockGlyph = new Label
                    {
                        AnchorPreset = AnchorPresets.StretchAll,
                        Text = "锁",
                        TextColor = InkWashTheme.TextTertiary,
                        Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 10f),
                        HorizontalAlignment = TextAlignment.Center,
                        VerticalAlignment = TextAlignment.Center,
                    };
                    lockBtn.AddChild(lockGlyph);
                    _skillPanel.AddChild(lockBtn);
                }
            }

            AddChild(_skillPanel);
        }

        private void BuildChatBox()
        {
            _chatBox = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = ChatBoxSize,
            };

            var tabs = new[] { "近聊", "世界", "门派", "队伍", "密聊", "系统" };
            float tabX = 4f;
            for (int i = 0; i < tabs.Length; i++)
            {
                var tabBg = new ContainerControl
                {
                    Location = new Float2(tabX, 3f),
                    Size = new Float2(34f, 18f),
                    BackgroundColor = (i == 0) ? InkWashTheme.BaseElevated : Color.Transparent,
                    AnchorPreset = AnchorPresets.TopLeft,
                };
                var tabLbl = new Label
                {
                    AnchorPreset = AnchorPresets.StretchAll,
                    Text = tabs[i],
                    TextColor = (i == 0) ? InkWashTheme.GoldBright : InkWashTheme.TextSecondary,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 10f),
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                };
                tabBg.AddChild(tabLbl);
                _chatBox.AddChild(tabBg);
                tabX += 36f;
            }

            var sep = new ContainerControl
            {
                Location = new Float2(4f, 22f),
                Size = new Float2(ChatBoxSize.X - 8f, 1f),
                BackgroundColor = InkWashTheme.BorderFaint,
                AnchorPreset = AnchorPresets.TopLeft,
            };
            _chatBox.AddChild(sep);

            var messages = new[]
            {
                "[门派] 醉道人：今晚帮战几时开？",
                "[世界] 剑无痕：出售六十级紫剑，洛阳城东摆摊",
                "[系统] 恭喜侠客逍遥客完成「黑风寨剿匪」",
                "[门派] 青衣客：已到，等人齐",
                "[队伍] 红袖招：BOSS刷新了，速来",
                "[世界] 玉面狐：收四十级蓝装，有的MMM",
                "[系统] 门派日常任务已刷新，请前往领取",
                "[门派] 醉道人：逍遥客拉怪稳一点",
            };

            float msgY = 24f;
            foreach (var msg in messages)
            {
                if (msgY + 16f > ChatBoxSize.Y - 30f)
                    break;
                _chatBox.AddChild(MakeLabel(msg, 8f, msgY, ChatBoxSize.X - 16f, 16f, InkWashTheme.TextSecondary, 11f));
                msgY += 18f;
            }

            var inputBg = new ContainerControl
            {
                Location = new Float2(6f, ChatBoxSize.Y - 28f),
                Size = new Float2(ChatBoxSize.X - 44f, 24f),
                BackgroundColor = InkWashTheme.Abyss,
                AnchorPreset = AnchorPresets.TopLeft,
            };
            _chatBox.AddChild(inputBg);

            _chatBox.AddChild(MakeLabel("输入消息...", 8f, ChatBoxSize.Y - 28f, ChatBoxSize.X - 52f, 24f, InkWashTheme.TextTertiary, 11f));

            var smileBtn = new ContainerControl
            {
                Location = new Float2(ChatBoxSize.X - 34f, ChatBoxSize.Y - 28f),
                Size = new Float2(24f, 24f),
                BackgroundColor = InkWashTheme.BaseElevated,
                AnchorPreset = AnchorPresets.TopLeft,
            };
            var smileGlyph = new Label
            {
                AnchorPreset = AnchorPresets.StretchAll,
                Text = ":)",
                TextColor = InkWashTheme.TextSecondary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 11f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            smileBtn.AddChild(smileGlyph);
            _chatBox.AddChild(smileBtn);

            AddChild(_chatBox);
        }

        private void BuildFunctionButtons()
        {
            _funcButtons = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(180f, 108f),
            };

            var toggleBtn = new InkGlyphBtn(180f, 28f);
            toggleBtn.Location = new Float2(0f, 0f);
            toggleBtn.Clicked += (b) => NavigationRequested?.Invoke(InkPageDomIds.CombatHud);

            var toggleIcon = new Label
            {
                Location = new Float2(6f, 2f),
                Size = new Float2(16f, 24f),
                Text = "眼",
                TextColor = InkWashTheme.GoldBright,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 12f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
            };
            toggleBtn.AddChild(toggleIcon);

            var toggleText = new Label
            {
                Location = new Float2(24f, 2f),
                Size = new Float2(150f, 24f),
                Text = "切换沉浸模式",
                TextColor = InkWashTheme.GoldBright,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 10f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
            };
            toggleBtn.AddChild(toggleText);
            _funcButtons.AddChild(toggleBtn);

            var funcDefs = new[]
            {
                (glyph: "包", label: "背包 B"),
                (glyph: "人", label: "角色 C"),
                (glyph: "技", label: "技能 K"),
                (glyph: "设", label: "设置 ESC"),
            };

            float btnX = 0f;
            foreach (var f in funcDefs)
            {
                var fnBtn = MakeNavBtn(f.glyph, f.label, FuncBtnWidth, FuncBtnHeight, 14f, 8f);
                fnBtn.Location = new Float2(btnX, 32f);
                _funcButtons.AddChild(fnBtn);
                btnX += FuncBtnWidth + 2f;
            }

            AddChild(_funcButtons);
        }

        // ===================================================================
        // 事件处理
        // =======================================================================

        private void OnNavButtonClicked(string domId)
        {
            try
            {
                EmitGoldAtCenter();
                NavigationRequested?.Invoke(domId);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[CombatHudTraditionalPage] NavigationRequested({domId}): {ex.Message}");
            }
        }

        private void EmitGoldAtCenter()
        {
            try
            {
                if (ParticleSystem == null) return;
                var center = new Float2(Width * 0.5f, Height * 0.5f);
                var screenPos = PointToScreen(center);
                var localPos = ParticleSystem.PointFromScreen(screenPos);
                ParticleSystem.EmitGoldBurst(localPos, count: 14, isLarge: false);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogWarning($"[CombatHudTraditionalPage] EmitGoldAtCenter: {ex.Message}");
            }
        }

        // ===================================================================
        // IInkPage 实现
        // =======================================================================

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);
            if (_boundCharacter != null)
                RefreshBoundCharacterDisplay();
            if (_minimap != null)
                _minimap.PlayerYaw = _minimapPlayerYaw;
        }

        public void RefreshLayout()
        {
            try
            {
                float sw = Width;
                float sh = Height;
                float edge = 12f;

                if (_targetInfoPanel != null)
                    _targetInfoPanel.Location = new Float2(sw * 0.5f - 150f, edge);

                if (_charPanel != null)
                    _charPanel.Location = new Float2(edge, edge);

                if (_partyPanel != null)
                    _partyPanel.Location = new Float2(edge, edge + CharPanelSize.Y + 6f);

                if (_minimapContainer != null)
                    _minimapContainer.Location = new Float2(sw - MinimapContainerSize - edge, edge);

                if (_questTracker != null)
                    _questTracker.Location = new Float2(sw - QuestTrackerSize.X - edge, 270f);

                float navBottom = 240f;
                if (_navBar != null)
                    _navBar.Location = new Float2(sw * 0.5f - _navBar.Width * 0.5f, sh - navBottom);

                if (_skillPanel != null)
                    _skillPanel.Location = new Float2(sw * 0.5f - _skillPanel.Width * 0.5f, sh - 8f - _skillPanel.Height);

                if (_chatBox != null)
                    _chatBox.Location = new Float2(edge, sh - 8f - _chatBox.Height);

                if (_funcButtons != null)
                    _funcButtons.Location = new Float2(sw - _funcButtons.Width - edge, sh - 8f - _funcButtons.Height - 20f);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[CombatHudTraditionalPage] RefreshLayout: {ex.Message}");
            }
        }

        public override void OnParentResized()
        {
            base.OnParentResized();
            RefreshLayout();
        }
    }
}

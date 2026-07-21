using FlaxEngine;
using FlaxEngine.GUI;
using Game.Character.Attributes;
using HundunWorld.Game.UI.StyleSystem;
using System;

namespace HundunWorld.Game.UI.Ink.Pages.Map
{
    public class CompassPage : ContainerControl, IInkPage
    {
        private const float HeaderHeight = 56f;
        private const float BottomPanelWidth = 780f;
        private const float BottomPanelHeight = 300f;
        private const float DialDiameter = 500f;
        private const float DialRadius = DialDiameter * 0.5f;
        private const float RingTianganRadius = 220f;
        private const float RingBaguaRadius = 168f;
        private const float RingCardinalRadius = 112f;
        private const float RingPoiRadius = 246f;
        private const float HubRadius = 24f;
        private const float NeedleLength = 110f;
        private const float NeedleBaseHalf = 7f;
        private const float ScreenEdge = 16f;
        private const int CircleStrokeSegments = 48;
        private static readonly Color DividerColor = new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.15f);
        private static readonly Color PanelBg = new Color(20f / 255f, 23f / 255f, 30f / 255f, 0.85f);
        private static readonly Color GoldVeryDim = new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.12f);

        private Panel _header;
        private Panel _headerBorder;
        private Label _titleLabel;
        private Label _subtitleLabel;
        private InkButton _closeButton;
        private InkPaperPanel _bottomPanel;
        private Label _facingLabel;
        private Label _coordLabel;
        private Label _regionLabel;
        private Label _poiCountLabel;
        private ContainerControl[] _poiRows;
        private InkButton _compassModeBtn;
        private InkButton _mapModeBtn;
        private InkButton _trackingTargetBtn;

        private CharacterAttributesComponent _boundCharacter;

        public event Action<string> NavigationRequested;

        public InkParticleSystem ParticleSystem { get; set; }

        public void BindCharacter(CharacterAttributesComponent component)
        {
            _boundCharacter = component;
        }

        public CompassPage()
        {
            try
            {
                AnchorPreset = AnchorPresets.StretchAll;
                Offsets = Margin.Zero;
                BackgroundColor = new Color(8f / 255f, 9f / 255f, 12f / 255f, 1f);
                ClipChildren = false;
                AutoFocus = false;

                BuildHeader();
                BuildBottomPanel();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[CompassPage] init failed: {ex.Message}");
            }
        }

        private void BuildHeader()
        {
            _header = new Panel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Float2(Width, HeaderHeight),
                BackgroundColor = Color.Transparent,
            };

            var iconBox = new Panel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(24f, 11f),
                Size = new Float2(34f, 34f),
                BackgroundColor = new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.1f),
            };
            var iconLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(8f, 8f),
                Size = new Float2(18f, 18f),
                Text = "\u2316",
                TextColor = InkWashTheme.GoldPrimary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 18f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            iconBox.AddChild(iconLabel);
            _header.AddChild(iconBox);

            _titleLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(66f, 12f),
                Size = new Float2(100f, 32f),
                Text = "司南",
                TextColor = InkWashTheme.GoldPrimary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 22f),
                HorizontalAlignment = TextAlignment.Near,
            };
            _header.AddChild(_titleLabel);

            _subtitleLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(174f, 18f),
                Size = new Float2(240f, 20f),
                Text = "天机方位 · 寻龙点脉",
                TextColor = InkWashTheme.TextSecondary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                HorizontalAlignment = TextAlignment.Near,
            };
            _header.AddChild(_subtitleLabel);

            _closeButton = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Sm,
                Text = "✕ 关闭",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(Width - 90f, 12f),
                Size = new Float2(80f, 32f),
                BorderColor = new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.15f),
            };
            _closeButton.Clicked += () => OnSystemNavButtonClicked(InkPageDomIds.CombatHud, null);
            _header.AddChild(_closeButton);

            _headerBorder = new Panel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, HeaderHeight - 1f),
                Size = new Float2(Width, 1f),
                BackgroundColor = DividerColor,
            };
            _header.AddChild(_headerBorder);

            AddChild(_header);
        }

        private void BuildBottomPanel()
        {
            _bottomPanel = new InkPaperPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(BottomPanelWidth, BottomPanelHeight),
                BackgroundColor = PanelBg,
            };

            float colWidth = BottomPanelWidth / 3f;
            float infoY = 12f;
            float colHeight = 52f;

            for (int col = 0; col < 3; col++)
            {
                float colX = col * colWidth;
                string title = col == 0 ? "朝向" : col == 1 ? "坐标" : "区域";
                Label valueLabel;

                var colContainer = new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(colX + 12f, infoY),
                    Size = new Float2(colWidth - 24f, colHeight),
                    BackgroundColor = Color.Transparent,
                };

                var titleLbl = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = Float2.Zero,
                    Size = new Float2(colWidth - 24f, 16f),
                    Text = title,
                    TextColor = InkWashTheme.TextSecondary,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                    HorizontalAlignment = TextAlignment.Near,
                };
                colContainer.AddChild(titleLbl);

                if (col == 0)
                {
                    _facingLabel = new Label
                    {
                        AnchorPreset = AnchorPresets.TopLeft,
                        Location = new Float2(0f, 18f),
                        Size = new Float2(colWidth - 24f, 22f),
                        Text = "北偏东 15°",
                        TextColor = InkWashTheme.TextDefault,
                        Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 15f),
                        HorizontalAlignment = TextAlignment.Near,
                    };
                    valueLabel = _facingLabel;
                }
                else if (col == 1)
                {
                    _coordLabel = new Label
                    {
                        AnchorPreset = AnchorPresets.TopLeft,
                        Location = new Float2(0f, 18f),
                        Size = new Float2(colWidth - 24f, 22f),
                        Text = "X:1234 Y:5678 Z:100",
                        TextColor = InkWashTheme.TextDefault,
                        Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 14f),
                        HorizontalAlignment = TextAlignment.Near,
                    };
                    valueLabel = _coordLabel;
                }
                else
                {
                    _regionLabel = new Label
                    {
                        AnchorPreset = AnchorPresets.TopLeft,
                        Location = new Float2(0f, 18f),
                        Size = new Float2(colWidth - 24f, 22f),
                        Text = "清河 · 开封城郊",
                        TextColor = InkWashTheme.TextDefault,
                        Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 14f),
                        HorizontalAlignment = TextAlignment.Near,
                    };
                    valueLabel = _regionLabel;
                }
                colContainer.AddChild(valueLabel);
                _bottomPanel.AddChild(colContainer);

                if (col < 2)
                {
                    var vDivider = new Panel
                    {
                        AnchorPreset = AnchorPresets.TopLeft,
                        Location = new Float2(colX + colWidth, infoY + 4f),
                        Size = new Float2(1f, colHeight - 8f),
                        BackgroundColor = DividerColor,
                    };
                    _bottomPanel.AddChild(vDivider);
                }
            }

            float divider1Y = infoY + colHeight + 8f;
            var hDivider1 = new Panel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, divider1Y),
                Size = new Float2(BottomPanelWidth, 1f),
                BackgroundColor = DividerColor,
            };
            _bottomPanel.AddChild(hDivider1);

            float poiTitleY = divider1Y + 10f;
            var poiListTitle = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, poiTitleY),
                Size = new Float2(200f, 18f),
                Text = "附近方位",
                TextColor = InkWashTheme.GoldPrimary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 12f),
                HorizontalAlignment = TextAlignment.Near,
            };
            _bottomPanel.AddChild(poiListTitle);

            _poiCountLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(BottomPanelWidth - 80f, poiTitleY),
                Size = new Float2(64f, 18f),
                Text = "3 处",
                TextColor = InkWashTheme.TextSecondary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 11f),
                HorizontalAlignment = TextAlignment.Far,
            };
            _bottomPanel.AddChild(_poiCountLabel);

            _poiRows = new ContainerControl[3];
            string[] poiIcons = { "◆", "●", "★" };
            string[] poiTags = { "界碑", "NPC", "任务" };
            string[] poiNames = { "开封城门", "药师张老", "山贼营地" };
            string[] poiDistances = { "北 200m", "东南 50m", "西 500m" };
            Color[] poiIconColors = { InkWashTheme.GoldBright, InkWashTheme.GoldBright, InkWashTheme.JadeBright };
            Color[] poiTagColors = { GoldVeryDim, GoldVeryDim, new Color(100f / 255f, 200f / 255f, 140f / 255f, 0.15f) };
            Color[] poiDistanceColors = { InkWashTheme.TextSecondary, InkWashTheme.TextSecondary, InkWashTheme.JadeBright };

            float poiRowY = poiTitleY + 22f;
            float poiRowH = 28f;
            for (int i = 0; i < 3; i++)
            {
                var row = new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(16f, poiRowY + i * poiRowH),
                    Size = new Float2(BottomPanelWidth - 32f, poiRowH),
                    BackgroundColor = Color.Transparent,
                };

                var iconLbl = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(0f, 0f),
                    Size = new Float2(20f, poiRowH),
                    Text = poiIcons[i],
                    TextColor = poiIconColors[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 15f),
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                };
                row.AddChild(iconLbl);

                var tagLbl = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(28f, 4f),
                    Size = new Float2(50f, poiRowH - 8f),
                    Text = poiTags[i],
                    TextColor = InkWashTheme.TextBrand,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                    BackgroundColor = poiTagColors[i],
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                };
                row.AddChild(tagLbl);

                var nameLbl = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(90f, 0f),
                    Size = new Float2(BottomPanelWidth - 32f - 90f - 100f, poiRowH),
                    Text = poiNames[i],
                    TextColor = InkWashTheme.TextDefault,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                row.AddChild(nameLbl);

                var distLbl = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(BottomPanelWidth - 32f - 100f, 0f),
                    Size = new Float2(100f, poiRowH),
                    Text = poiDistances[i],
                    TextColor = poiDistanceColors[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 12f),
                    HorizontalAlignment = TextAlignment.Far,
                    VerticalAlignment = TextAlignment.Center,
                };
                row.AddChild(distLbl);

                _poiRows[i] = row;
                _bottomPanel.AddChild(row);
            }

            float divider2Y = poiRowY + 3 * poiRowH + 4f;
            var hDivider2 = new Panel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, divider2Y),
                Size = new Float2(BottomPanelWidth, 1f),
                BackgroundColor = DividerColor,
            };
            _bottomPanel.AddChild(hDivider2);

            float modeRowY = divider2Y + 10f;

            _compassModeBtn = new InkButton
            {
                Variant = InkButtonVariant.Primary,
                ButtonSize = InkButtonSize.Sm,
                Text = "罗盘模式",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, modeRowY),
                Size = new Float2(108f, 30f),
                Height = 30f,
            };
            _bottomPanel.AddChild(_compassModeBtn);

            _mapModeBtn = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Sm,
                Text = "地图模式",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(128f, modeRowY),
                Size = new Float2(108f, 30f),
                Height = 30f,
            };
            _mapModeBtn.Clicked += () => OnSystemNavButtonClicked(InkPageDomIds.NavWorldMap, null);
            _bottomPanel.AddChild(_mapModeBtn);

            var trackingLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(BottomPanelWidth - 270f, modeRowY),
                Size = new Float2(70f, 34f),
                Text = "追踪目标",
                TextColor = InkWashTheme.TextSecondary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _bottomPanel.AddChild(trackingLabel);

            _trackingTargetBtn = new InkButton
            {
                Variant = InkButtonVariant.Default,
                ButtonSize = InkButtonSize.Sm,
                Text = "山贼营地 ▼",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(BottomPanelWidth - 200f, modeRowY),
                Size = new Float2(184f, 30f),
                Height = 30f,
                BorderColor = new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.28f),
            };
            _bottomPanel.AddChild(_trackingTargetBtn);

            AddChild(_bottomPanel);
        }

        public override void Draw()
        {
            try
            {
                DrawCompassDial();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[CompassPage] DrawCompassDial failed: {ex.Message}");
            }
            base.Draw();
        }

        private void DrawCompassDial()
        {
            if (Width <= 0f || Height <= 0f)
                return;

            float cx = Width * 0.5f;
            float availTop = HeaderHeight + ScreenEdge + 8f;
            float availBottom = Height - BottomPanelHeight - ScreenEdge - 8f;
            if (availBottom <= availTop)
                return;
            float cy = (availTop + availBottom) * 0.5f;

            InkRenderHelper.FillCircle(new Float2(cx, cy), DialRadius + 4f,
                new Color(8f / 255f, 9f / 255f, 12f / 255f, 0.72f));
            InkRenderHelper.FillCircle(new Float2(cx, cy), DialRadius,
                new Color(20f / 255f, 23f / 255f, 30f / 255f, 0.85f));

            DrawCircleStroke(new Float2(cx, cy), DialRadius, InkWashTheme.GoldPrimary, 2f);

            InkRenderHelper.FillCircle(new Float2(cx, cy), DialRadius - 6f,
                new Color(20f / 255f, 23f / 255f, 30f / 255f, 0.34f));

            string[] tiangan = { "甲", "乙", "丙", "丁", "戊", "己", "庚", "辛", "壬", "癸" };
            for (int i = 0; i < 10; i++)
            {
                float angle = i * 36f;
                var pos = CompassToScreen(cx, cy, angle, RingTianganRadius);
                DrawChar(pos, tiangan[i], InkWashTheme.TextSecondary, 15f);
            }

            string[] bagua = { "坎", "艮", "震", "巽", "离", "坤", "兑", "乾" };
            for (int i = 0; i < 8; i++)
            {
                float angle = i * 45f;
                var pos = CompassToScreen(cx, cy, angle, RingBaguaRadius);
                DrawChar(pos, bagua[i], InkWashTheme.GoldDeep, 17f);
            }

            string[] cardinals = { "北", "东", "南", "西" };
            for (int i = 0; i < 4; i++)
            {
                float angle = i * 90f;
                var pos = CompassToScreen(cx, cy, angle, RingCardinalRadius);
                DrawChar(pos, cardinals[i], InkWashTheme.GoldPrimary, 30f);
            }

            DrawPoiMarker(CompassToScreen(cx, cy, 0f, RingPoiRadius), "◆", InkWashTheme.GoldBright);
            DrawPoiMarker(CompassToScreen(cx, cy, 135f, RingPoiRadius), "●", InkWashTheme.GoldBright);
            DrawPoiMarker(CompassToScreen(cx, cy, 270f, RingPoiRadius), "★", InkWashTheme.JadeBright);

            InkRenderHelper.FillCircle(new Float2(cx, cy), HubRadius,
                new Color(26f / 255f, 29f / 255f, 38f / 255f, 1f));
            DrawCircleStroke(new Float2(cx, cy), HubRadius, InkWashTheme.GoldPrimary, 1f);

            DrawChar(new Float2(cx, cy), "★", InkWashTheme.JadeBright, 26f);

            float swayDeg = Mathf.Sin(Time.GameTime * 1.0f) * 4f;
            DrawNeedle(new Float2(cx, cy), swayDeg);
        }

        private Float2 CompassToScreen(float cx, float cy, float angleDeg, float radius)
        {
            float rad = angleDeg * Mathf.DegreesToRadians;
            return new Float2(
                cx + radius * Mathf.Sin(rad),
                cy - radius * Mathf.Cos(rad));
        }

        private void DrawChar(Float2 pos, string ch, Color color, float size)
        {
            var fontRef = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, size);
            var font = fontRef.GetFont();
            if (font == null)
                return;

            var metrics = font.MeasureText(ch);
            var rect = new Rectangle(
                pos.X - metrics.X * 0.5f,
                pos.Y - metrics.Y * 0.5f,
                metrics.X,
                metrics.Y);
            Render2D.DrawText(font, ch, rect, color,
                TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
        }

        private void DrawPoiMarker(Float2 pos, string symbol, Color color)
        {
            Color glow1 = new Color(color.R, color.G, color.B, 0.25f);
            Color glow2 = new Color(color.R, color.G, color.B, 0.5f);
            InkRenderHelper.FillCircle(pos, 14f, glow1);
            InkRenderHelper.FillCircle(pos, 10f, glow2);
            DrawChar(pos, symbol, color, 17f);
        }

        private void DrawCircleStroke(Float2 center, float radius, Color color, float thickness)
        {
            if (radius <= 0f)
                return;

            Float2 prev = center + new Float2(radius, 0f);
            for (int i = 1; i <= CircleStrokeSegments; i++)
            {
                float a = (i / (float)CircleStrokeSegments) * Mathf.TwoPi;
                var curr = center + new Float2(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius);
                Render2D.DrawLine(prev, curr, color, thickness);
                prev = curr;
            }
        }

        private void DrawNeedle(Float2 center, float swayDeg)
        {
            float rad = swayDeg * Mathf.DegreesToRadians;
            float dirX = Mathf.Sin(rad);
            float dirY = -Mathf.Cos(rad);

            var tip = center + new Float2(dirX * NeedleLength, dirY * NeedleLength);
            float perpX = dirY;
            float perpY = -dirX;
            var base1 = center + new Float2(perpX * NeedleBaseHalf, perpY * NeedleBaseHalf);
            var base2 = center - new Float2(perpX * NeedleBaseHalf, perpY * NeedleBaseHalf);

            var vertices = new Float2[3];
            vertices[0] = tip;
            vertices[1] = base1;
            vertices[2] = base2;
            Render2D.FillTriangles(vertices,
                new Color(InkWashTheme.VermilionBright.R, InkWashTheme.VermilionBright.G,
                          InkWashTheme.VermilionBright.B, 0.95f));

            var tipS = center - new Float2(dirX * NeedleLength * 0.8f, dirY * NeedleLength * 0.8f);
            vertices[0] = tipS;
            Render2D.FillTriangles(vertices,
                new Color(InkWashTheme.TextSecondary.R, InkWashTheme.TextSecondary.G,
                          InkWashTheme.TextSecondary.B, 0.32f));
        }

        private void OnSystemNavButtonClicked(string domId, Button sourceButton)
        {
            try
            {
                EmitGoldAtButton(sourceButton);
                NavigationRequested?.Invoke(domId);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[CompassPage] NavigationRequested({domId}) failed: {ex.Message}");
            }
        }

        private void EmitGoldAtButton(Button button)
        {
            try
            {
                if (ParticleSystem == null || button == null)
                    return;

                var buttonCenter = new Float2(button.Width * 0.5f, button.Height * 0.5f);
                var screenPos = button.PointToScreen(buttonCenter);
                var localPos = ParticleSystem.PointFromScreen(screenPos);
                ParticleSystem.EmitGoldBurst(localPos, count: 14, isLarge: false);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogWarning($"[CompassPage] EmitGoldAtButton failed: {ex.Message}");
            }
        }

        public void RefreshLayout()
        {
            try
            {
                float sw = Width;
                float sh = Height;

                if (_header != null)
                {
                    _header.Size = new Float2(sw, HeaderHeight);
                    _header.Location = Float2.Zero;

                    if (_closeButton != null)
                        _closeButton.Location = new Float2(sw - 90f, 12f);

                    if (_headerBorder != null)
                        _headerBorder.Size = new Float2(sw, 1f);
                }

                if (_bottomPanel != null)
                {
                    _bottomPanel.Location = new Float2(
                        sw * 0.5f - BottomPanelWidth * 0.5f,
                        sh - ScreenEdge - BottomPanelHeight);
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[CompassPage] RefreshLayout failed: {ex.Message}");
            }
        }

        public override void OnParentResized()
        {
            base.OnParentResized();
            RefreshLayout();
        }
    }
}

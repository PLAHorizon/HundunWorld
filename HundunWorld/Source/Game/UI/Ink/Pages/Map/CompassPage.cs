using FlaxEngine;
using FlaxEngine.GUI;
using Game.Character.Attributes;
using HundunWorld.Game.UI.StyleSystem;
using System;

namespace HundunWorld.Game.UI.Ink.Pages.Map
{
    /// <summary>
    /// 司南（罗盘）页面 — 对应设计方案 compass.html。
    /// 全屏布局：顶栏（图标+司南+副标题+关闭）/ 中央 500x500 罗盘刻度盘
    /// （天干/八卦/四正/POI 四环 + 中心枢纽 + 玩家星 + 朝向金针）/ 底部信息面板
    /// （朝向/坐标/区域 + 附近方位列表 + 模式切换 + 追踪目标）。
    /// 严格遵循水墨主题 Token，禁止硬编码色值。
    /// </summary>
    public class CompassPage : ContainerControl, IInkPage
    {
        private const float HeaderHeight = 66f;
        private const float DialDiameter = 500f;
        private const float BottomPanelWidth = 780f;
        private const float BottomPanelHeight = 252f;
        private const float BottomMargin = 16f;

        public event Action<string> NavigationRequested;
        public InkParticleSystem ParticleSystem { get; set; }

        private CharacterAttributesComponent _boundCharacter;

        private InkButton _closeBtn;
        private CompassDial _dial;
        private InkPanel _bottomPanel;
        private InkButton _compassModeBtn, _mapModeBtn;

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
                BackgroundColor = InkWashTheme.Void;
                ClipChildren = false;
                AutoFocus = false;

                BuildHeader();
                BuildDial();
                BuildBottomPanel();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[CompassPage] init failed: {ex.Message}");
            }
        }

        // ===================================================================
        // 顶栏
        // ===================================================================

        private void BuildHeader()
        {
            var header = new ContainerControl
            {
                AnchorPreset = AnchorPresets.HorizontalStretchTop,
                Offsets = new Margin(0f, 0f, 0f, HeaderHeight),
                BackgroundColor = Color.Transparent,
                AutoFocus = false,
            };
            AddChild(header);

            // 底部 gold-subtle 边线
            header.AddChild(new ContainerControl
            {
                AnchorPreset = AnchorPresets.HorizontalStretchBottom,
                Offsets = new Margin(0f, 0f, HeaderHeight - 1f, 1f),
                BackgroundColor = InkWashTheme.BorderGoldSubtle,
                AutoFocus = false,
            });

            // 罗盘图标块（34x34，radius 8，gold10% 底 + gold-subtle 边）
            header.AddChild(new CompassIcon());

            header.AddChild(MakeLabel("司南", 60f, 14f, 120f, 30f,
                InkWashTheme.GoldPrimary, 22f, InkWashTheme.FontRole.Display, TextAlignment.Near));
            header.AddChild(MakeLabel("天机方位 · 寻龙点脉", 150f, 22f, 220f, 20f,
                InkWashTheme.TextSecondary, 12f, InkWashTheme.FontRole.Body, TextAlignment.Near));

            // 关闭按钮（secondary sm）
            _closeBtn = new InkButton
            {
                Variant = InkButtonVariant.Secondary,
                ButtonSize = InkButtonSize.Sm,
                Text = "✕ 关闭",
                AnchorPreset = AnchorPresets.TopRight,
                Location = new Float2(-90f, 16f),
                Size = new Float2(74f, 26f),
            };
            _closeBtn.ButtonClicked += (b) => NavigationRequested?.Invoke(InkPageDomIds.BackHud);
            header.AddChild(_closeBtn);
        }

        // ===================================================================
        // 中央罗盘
        // ===================================================================

        private void BuildDial()
        {
            _dial = new CompassDial
            {
                AnchorPreset = AnchorPresets.MiddleCenter,
                Size = new Float2(DialDiameter, DialDiameter),
            };
            AddChild(_dial);
        }

        // ===================================================================
        // 底部信息面板
        // ===================================================================

        private void BuildBottomPanel()
        {
            _bottomPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.BottomCenter,
                Size = new Float2(BottomPanelWidth, BottomPanelHeight),
            };
            AddChild(_bottomPanel);

            // ── 信息三列：朝向 / 坐标 / 区域 ──
            float colW = (BottomPanelWidth - 2f) / 3f;
            BuildInfoCell(0f, colW, "朝向", "北偏东 15°", InkWashTheme.FontRole.Number, 15f);
            BuildInfoCell(colW + 1f, colW, "坐标", "X:1234 Y:5678 Z:100", InkWashTheme.FontRole.Number, 14f);
            BuildInfoCell((colW + 1f) * 2f, colW, "区域", "清河 · 开封城郊", InkWashTheme.FontRole.Display, 14f);

            // 列分隔线
            _bottomPanel.AddChild(new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(colW, 10f),
                Size = new Float2(1f, 40f),
                BackgroundColor = InkWashTheme.BorderGoldSubtle,
                AutoFocus = false,
            });
            _bottomPanel.AddChild(new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(colW * 2f + 1f, 10f),
                Size = new Float2(1f, 40f),
                BackgroundColor = InkWashTheme.BorderGoldSubtle,
                AutoFocus = false,
            });

            // ── 附近方位 ──
            float secTop = 60f;
            _bottomPanel.AddChild(new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, secTop),
                Size = new Float2(BottomPanelWidth, 1f),
                BackgroundColor = InkWashTheme.BorderGoldSubtle,
                AutoFocus = false,
            });
            _bottomPanel.AddChild(MakeLabel("附近方位", 16f, secTop + 6f, 120f, 18f,
                InkWashTheme.GoldPrimary, 12f, InkWashTheme.FontRole.Display, TextAlignment.Near));
            _bottomPanel.AddChild(MakeLabel("3 处", BottomPanelWidth - 16f - 60f, secTop + 6f, 60f, 18f,
                InkWashTheme.TextSecondary, 11f, InkWashTheme.FontRole.Number, TextAlignment.Far));

            float rowY = secTop + 28f;
            BuildPoiRow(rowY, "◆", InkWashTheme.GoldBright, "界碑", TagKind.Brand, "开封城门", "北 200m", false);
            rowY += 34f;
            BuildPoiRow(rowY, "●", InkWashTheme.GoldBright, "NPC", TagKind.Neutral, "药师张老", "东南 50m", false);
            rowY += 34f;
            BuildPoiRow(rowY, "★", InkWashTheme.JadeBright, "任务", TagKind.Success, "山贼营地", "西 500m", true);

            // ── 模式切换 + 追踪目标 ──
            float modeTop = BottomPanelHeight - 44f;
            _bottomPanel.AddChild(new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, modeTop - 6f),
                Size = new Float2(BottomPanelWidth, 1f),
                BackgroundColor = InkWashTheme.BorderGoldSubtle,
                AutoFocus = false,
            });

            _compassModeBtn = new InkButton
            {
                Variant = InkButtonVariant.Brand,
                ButtonSize = InkButtonSize.Sm,
                Text = "◎ 罗盘模式",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, modeTop),
                Size = new Float2(104f, 28f),
            };
            _compassModeBtn.ButtonClicked += (b) => SetMode(true);
            _bottomPanel.AddChild(_compassModeBtn);

            _mapModeBtn = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Sm,
                Text = "▦ 地图模式",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f + 104f + 8f, modeTop),
                Size = new Float2(104f, 28f),
            };
            _mapModeBtn.ButtonClicked += (b) => SetMode(false);
            _bottomPanel.AddChild(_mapModeBtn);

            // 追踪目标
            _bottomPanel.AddChild(MakeLabel("◎ 追踪目标", BottomPanelWidth - 16f - 240f, modeTop + 4f, 90f, 20f,
                InkWashTheme.TextSecondary, 12f, InkWashTheme.FontRole.Body, TextAlignment.Near));
            _bottomPanel.AddChild(new TrackSelect());
        }

        private void SetMode(bool compass)
        {
            _compassModeBtn.Variant = compass ? InkButtonVariant.Brand : InkButtonVariant.Ghost;
            _mapModeBtn.Variant = compass ? InkButtonVariant.Ghost : InkButtonVariant.Brand;
        }

        private void BuildInfoCell(float x, float w, string label, string value,
            InkWashTheme.FontRole valueRole, float valueSize)
        {
            _bottomPanel.AddChild(MakeLabel(label, x + 40f, 10f, w - 56f, 16f,
                InkWashTheme.TextSecondary, 11f, InkWashTheme.FontRole.Body, TextAlignment.Near));
            _bottomPanel.AddChild(MakeLabel(value, x + 40f, 28f, w - 56f, 22f,
                InkWashTheme.TextDefault, valueSize, valueRole, TextAlignment.Near));
            // 金色图标（代替 lucide）
            _bottomPanel.AddChild(new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(x + 14f, 18f),
                Size = new Float2(14f, 14f),
                BackgroundColor = InkWashTheme.GoldPrimary,
                AutoFocus = false,
            });
        }

        private enum TagKind { Brand, Neutral, Success }

        private void BuildPoiRow(float y, string glyph, Color glyphColor, string tag, TagKind kind,
            string name, string dist, bool jadeDist)
        {
            var row = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, y),
                Size = new Float2(BottomPanelWidth - 32f, 32f),
                BackgroundColor = Color.Transparent,
                AutoFocus = false,
            };
            _bottomPanel.AddChild(row);

            // 行底边线
            row.AddChild(new ContainerControl
            {
                AnchorPreset = AnchorPresets.HorizontalStretchBottom,
                Offsets = new Margin(0f, 0f, 31f, 1f),
                BackgroundColor = InkWashTheme.GoldTrace,
                AutoFocus = false,
            });

            row.AddChild(MakeLabel(glyph, 0f, 4f, 18f, 24f, glyphColor, 15f,
                InkWashTheme.FontRole.Body, TextAlignment.Center));

            Color tagTc, tagBorder, tagBg;
            if (kind == TagKind.Brand)
            {
                tagTc = InkWashTheme.TextOnBrand; tagBorder = InkWashTheme.GoldPrimary; tagBg = InkWashTheme.GoldPrimary;
            }
            else if (kind == TagKind.Success)
            {
                tagTc = InkWashTheme.JadeBright; tagBorder = InkWashTheme.JadeDim; tagBg = InkWashTheme.JadeFaint;
            }
            else
            {
                tagTc = InkWashTheme.TextSecondary; tagBorder = InkWashTheme.BorderFaint;
                tagBg = VoidBg(0.40f);
            }
            row.AddChild(new TagPill(tag, tagTc, tagBorder, tagBg)
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(28f, 6f),
                Size = new Float2(48f, 20f),
            });

            row.AddChild(MakeLabel(name, 88f, 4f, 300f, 24f,
                InkWashTheme.TextDefault, 13f, InkWashTheme.FontRole.Body, TextAlignment.Near));
            row.AddChild(MakeLabel(dist, row.Size.X - 16f - 90f, 4f, 90f, 24f,
                jadeDist ? InkWashTheme.JadeBright : InkWashTheme.TextSecondary, 12f,
                InkWashTheme.FontRole.Number, TextAlignment.Far));
        }

        // ===================================================================
        // 辅助
        // ===================================================================

        private static Color VoidBg(float alpha)
        {
            var c = InkWashTheme.Void;
            return new Color(c.R, c.G, c.B, alpha);
        }

        private Label MakeLabel(string text, float x, float y, float w, float h,
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

        public void RefreshLayout()
        {
            if (_bottomPanel != null)
            {
                float bx = (Width - BottomPanelWidth) * 0.5f;
                _bottomPanel.Location = new Float2(bx > 0f ? bx : 0f, Height - BottomMargin - BottomPanelHeight);
            }
            if (_dial != null)
            {
                float dx = (Width - DialDiameter) * 0.5f;
                float dy = (Height - DialDiameter) * 0.5f - 30f;
                _dial.Location = new Float2(dx > 0f ? dx : 0f, dy > HeaderHeight ? dy : HeaderHeight);
            }
        }

        public override void OnParentResized()
        {
            base.OnParentResized();
            RefreshLayout();
        }

        // ===============================================================
        // 嵌套自绘控件
        // ===============================================================

        /// <summary>顶栏罗盘图标块（34x34，radius 8，gold10% 底 + gold-subtle 边）。</summary>
        private sealed class CompassIcon : Control
        {
            public CompassIcon()
            {
                AutoFocus = false;
                AnchorPreset = AnchorPresets.TopLeft;
                Location = new Float2(24f, 16f);
                Size = new Float2(34f, 34f);
            }

            public override void Draw()
            {
                base.Draw();
                if (!Visible) return;
                var rect = new Rectangle(Float2.Zero, Size);
                var g = InkWashTheme.GoldPrimary;
                InkRenderHelper.FillRoundedRectangle(rect, 8f, new Color(g.R, g.G, g.B, 0.10f));
                InkRenderHelper.DrawRoundedRectangle(rect, 8f, InkWashTheme.BorderGoldSubtle, 1f);
                var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 18f).GetFont();
                if (font != null)
                    Render2D.DrawText(font, "◎", rect, InkWashTheme.GoldPrimary,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
            }
        }

        /// <summary>标签药丸（radius 2）。</summary>
        private sealed class TagPill : Control
        {
            private readonly string _text;
            private readonly Color _tc;
            private readonly Color _border;
            private readonly Color _bg;

            public TagPill(string text, Color textColor, Color border, Color bg)
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
                    InkRenderHelper.FillRoundedRectangle(rect, 2f, _bg);
                if (_border.A > 0f)
                    InkRenderHelper.DrawRoundedRectangle(rect, 2f, _border, 1f);
                var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 11f).GetFont();
                if (font != null)
                    Render2D.DrawText(font, _text, rect, _tc,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
            }
        }

        /// <summary>追踪目标下拉框（金6% 底 + gold-subtle 边 + 山贼营地 + ▼）。</summary>
        private sealed class TrackSelect : Control
        {
            public TrackSelect()
            {
                AutoFocus = false;
                AnchorPreset = AnchorPresets.TopLeft;
                Location = new Float2(BottomPanelWidth - 16f - 150f, BottomPanelHeight - 44f);
                Size = new Float2(150f, 28f);
            }

            public override void Draw()
            {
                base.Draw();
                if (!Visible) return;
                var rect = new Rectangle(Float2.Zero, Size);
                var g = InkWashTheme.GoldPrimary;
                InkRenderHelper.FillRoundedRectangle(rect, 6f, new Color(g.R, g.G, g.B, 0.06f));
                InkRenderHelper.DrawRoundedRectangle(rect, 6f, InkWashTheme.BorderGoldSubtle, 1f);
                var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 13f).GetFont();
                if (font != null)
                    Render2D.DrawText(font, "山贼营地", new Rectangle(12f, 0f, Width - 40f, Height),
                        InkWashTheme.TextDefault, TextAlignment.Near, TextAlignment.Center, TextWrapping.NoWrap);
                var cf = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f).GetFont();
                if (cf != null)
                    Render2D.DrawText(cf, "▼", new Rectangle(Width - 26f, 0f, 18f, Height),
                        InkWashTheme.GoldPrimary, TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
            }
        }

        /// <summary>罗盘刻度盘（500x500：天干/八卦/四正/POI 四环 + 枢纽 + 玩家星 + 朝向金针）。</summary>
        private sealed class CompassDial : Control
        {
            private static readonly string[] Tiangan = { "甲", "乙", "丙", "丁", "戊", "己", "庚", "辛", "壬", "癸" };
            private static readonly string[] Bagua = { "坎", "艮", "震", "巽", "离", "坤", "兑", "乾" };
            private static readonly string[] Cardinals = { "北", "东", "南", "西" };

            private float _t;

            public CompassDial() { AutoFocus = false; }

            public override void Update(float dt)
            {
                base.Update(dt);
                _t += dt;
            }

            public override void Draw()
            {
                base.Draw();
                if (!Visible || Width <= 0f) return;

                float cx = Width * 0.5f;
                float cy = Height * 0.5f;
                var center = new Float2(cx, cy);
                float R = Width * 0.5f - 2f;

                DrawBase(center, R);
                DrawTicks(center, R);
                DrawRings(center);
                DrawPois(center);
                DrawHub(center);
                DrawNeedle(center);
                DrawPlayerStar(center);
            }

            /// <summary>环底：径向渐变 + 2px 金边 + 外辉光 + 暗纱。</summary>
            private void DrawBase(Float2 c, float R)
            {
                // 外辉光（多层淡金圈）
                var g = InkWashTheme.GoldPrimary;
                for (int i = 3; i >= 1; i--)
                    InkRenderHelper.DrawCircle(c, R + i * 3f, new Color(g.R, g.G, g.B, 0.05f), 2f);

                // 环底（代替 compass-ring.jpg：深墨径向渐变）
                InkRenderHelper.FillRadialGradient(c, R, InkWashTheme.BaseTertiary, InkWashTheme.Abyss, 24);
                // 暗纱（中心 panel 0.52 → 边缘 abyss 0.62）
                var p = InkWashTheme.Panel;
                var ab = InkWashTheme.Abyss;
                InkRenderHelper.FillRadialGradient(c, R,
                    new Color(p.R, p.G, p.B, 0.52f), new Color(ab.R, ab.G, ab.B, 0.62f), 24);
                // 2px 金边
                InkRenderHelper.DrawCircle(c, R, InkWashTheme.GoldPrimary, 2f);
                // 内阴影圈
                InkRenderHelper.DrawCircle(c, R - 6f, new Color(ab.R, ab.G, ab.B, 0.50f), 4f);
            }

            /// <summary>刻度线（每 15°，四正加长）。</summary>
            private void DrawTicks(Float2 c, float R)
            {
                var gold = InkWashTheme.GoldPrimary;
                for (int i = 0; i < 24; i++)
                {
                    float ang = i * (Mathf.TwoPi / 24f);
                    bool cardinal = (i % 6 == 0);
                    float r1 = R - (cardinal ? 18f : 12f);
                    float r2 = R - 6f;
                    var p1 = c + new Float2(Mathf.Sin(ang) * r1, -Mathf.Cos(ang) * r1);
                    var p2 = c + new Float2(Mathf.Sin(ang) * r2, -Mathf.Cos(ang) * r2);
                    Render2D.DrawLine(p1, p2,
                        new Color(gold.R, gold.G, gold.B, cardinal ? 0.55f : 0.28f), cardinal ? 1.5f : 1f);
                }
            }

            /// <summary>天干 / 八卦 / 四正 三环文字。</summary>
            private void DrawRings(Float2 c)
            {
                DrawRingText(c, Tiangan, 220f, 15f, InkWashTheme.TextSecondary, 10);
                DrawRingText(c, Bagua, 168f, 17f, InkWashTheme.GoldDeep, 8);
                DrawRingText(c, Cardinals, 112f, 30f, InkWashTheme.GoldPrimary, 4);
            }

            private void DrawRingText(Float2 c, string[] chars, float r, float fontSize, Color color, int count)
            {
                var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, fontSize).GetFont();
                if (font == null) return;
                float span = fontSize + 8f;
                for (int i = 0; i < count; i++)
                {
                    float ang = i * (Mathf.TwoPi / count);
                    float x = c.X + Mathf.Sin(ang) * r;
                    float y = c.Y - Mathf.Cos(ang) * r;
                    Render2D.DrawText(font, chars[i],
                        new Rectangle(x - span * 0.5f, y - span * 0.5f, span, span), color,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
                }
            }

            /// <summary>POI 方向标记（◆0° 金 / ●135° 金 / ★270° 青追踪脉冲）。</summary>
            private void DrawPois(Float2 c)
            {
                var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 17f).GetFont();
                if (font == null) return;
                DrawPoi(font, c, 0f, "◆", InkWashTheme.GoldBright, false);
                DrawPoi(font, c, 135f * Mathf.DegreesToRadians, "●", InkWashTheme.GoldBright, false);
                DrawPoi(font, c, 270f * Mathf.DegreesToRadians, "★", InkWashTheme.JadeBright, true);
            }

            private void DrawPoi(Font font, Float2 c, float ang, string glyph, Color color, bool tracked)
            {
                float r = 246f;
                float x = c.X + Mathf.Sin(ang) * r;
                float y = c.Y - Mathf.Cos(ang) * r;
                float alpha = tracked
                    ? Mathf.Lerp(0.82f, 1f, 0.5f + 0.5f * Mathf.Sin(_t * Mathf.TwoPi / 2.6f))
                    : 1f;
                var clr = new Color(color.R, color.G, color.B, alpha);
                Render2D.DrawText(font, glyph, new Rectangle(x - 12f, y - 12f, 24f, 24f), clr,
                    TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
            }

            /// <summary>中心枢纽（48px 圆，径向渐变 + 金边）。</summary>
            private void DrawHub(Float2 c)
            {
                InkRenderHelper.FillRadialGradient(c, 24f, InkWashTheme.BgMist, InkWashTheme.BaseSecondary, 12);
                InkRenderHelper.DrawCircle(c, 24f, InkWashTheme.GoldPrimary, 1f);
            }

            /// <summary>朝向金针（红三角指北 + 灰三角尾，微摆 ±2.2°）。</summary>
            private void DrawNeedle(Float2 c)
            {
                float sway = Mathf.Sin(_t * Mathf.TwoPi / 6.5f) * 2.2f * Mathf.DegreesToRadians;

                // 前针（指北，红）
                var n1 = Rot(c + new Float2(0f, -56f), c, sway);
                var n2 = Rot(c + new Float2(-12f, 2f), c, sway);
                var n3 = Rot(c + new Float2(12f, 2f), c, sway);
                var red = InkWashTheme.VermilionBright;
                var redClr = new Color(red.R, red.G, red.B, 0.95f);
                Render2D.FillTriangle(n1, n2, n3, redClr);

                // 尾针（指南，灰）
                var s1 = Rot(c + new Float2(0f, 56f), c, sway);
                var s2 = Rot(c + new Float2(-10f, 0f), c, sway);
                var s3 = Rot(c + new Float2(10f, 0f), c, sway);
                var gray = InkWashTheme.TextSecondary;
                var grayClr = new Color(gray.R, gray.G, gray.B, 0.32f);
                Render2D.FillTriangle(s1, s2, s3, grayClr);
            }

            private static Float2 Rot(Float2 p, Float2 c, float a)
            {
                float dx = p.X - c.X, dy = p.Y - c.Y;
                float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                return new Float2(c.X + dx * ca - dy * sa, c.Y + dx * sa + dy * ca);
            }

            /// <summary>玩家星（★ 26px 青亮，中心）。</summary>
            private void DrawPlayerStar(Float2 c)
            {
                var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 26f).GetFont();
                if (font == null) return;
                Render2D.DrawText(font, "★", new Rectangle(c.X - 16f, c.Y - 16f, 32f, 32f),
                    InkWashTheme.JadeBright, TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
            }
        }
    }
}

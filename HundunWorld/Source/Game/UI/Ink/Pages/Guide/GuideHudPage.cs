using System;
using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.StyleSystem;

namespace HundunWorld.Game.UI.Ink.Pages.Guide
{
    public class GuideHudPage : ContainerControl, IInkPage
    {
        private InkBackButton _backButton;
        private GuideStepPanel _stepPanel;
        private SpotlightControl _spotlight;
        private InkArrowControl _inkArrow;
        private VerticalTextControl _verticalText;
        private GuideHintControl _hintControl;
        private InkButton _visionButton;
        private GuideDimMask _dimMask;

        private Float2 _screenSize;

        public GuideHudPage()
        {
            _screenSize = FlaxEngine.Screen.Size;
            if (_screenSize.X <= 0f || _screenSize.Y <= 0f)
            {
                _screenSize = new Float2(1920f, 1080f);
            }

            AnchorPreset = AnchorPresets.StretchAll;
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
            AutoFocus = true;
            Location = Float2.Zero;
            Size = _screenSize;

            try
            {
                // 子控件添加顺序即 z 序（后加者在上）：暗化遮罩 → 聚光区 → 箭头 → 竖排文字/提示 → 步骤面板/返回
                BuildDimMask();
                BuildSpotlight();
                BuildInkArrow();
                BuildVerticalText();
                BuildHintAndVision();
                BuildStepPanel();
                BuildBackButton();

                ApplyLayout();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[GuideHudPage] 初始化失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 全屏暗化遮罩（对齐设计 .guide-spotlight 的 box-shadow 0 0 0 9999px rgba(14,16,22,0.65)）：
        /// 在聚光区域四周绘制四条暗带形成"高亮洞"；不拦截鼠标事件（Enabled=false 仍参与绘制）。
        /// </summary>
        private void BuildDimMask()
        {
            _dimMask = new GuideDimMask
            {
                AnchorPreset = AnchorPresets.StretchAll,
                Offsets = Margin.Zero,
                ClipChildren = false,
                AutoFocus = false,
                Enabled = false,
            };
            AddChild(_dimMask);
        }

        private void BuildBackButton()
        {
            _backButton = new InkBackButton
            {
                AnchorPreset = AnchorPresets.TopRight,
                Location = new Float2(-20f, 20f),
                Size = new Float2(40f, 40f),
                Parent = this
            };
            _backButton.Clicked += OnBackClicked;
        }

        private void BuildStepPanel()
        {
            _stepPanel = new GuideStepPanel
            {
                CurrentStep = 2,
                TotalSteps = 5,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(32f, 32f),
                Size = new Float2(180f, 36f),
                Parent = this
            };
        }

        private void BuildSpotlight()
        {
            _spotlight = new SpotlightControl
            {
                AnchorPreset = AnchorPresets.BottomLeft,
                Location = new Float2(24f, -112f),
                Size = new Float2(380f, 88f),
                Parent = this
            };
        }

        private void BuildInkArrow()
        {
            _inkArrow = new InkArrowControl
            {
                AnchorPreset = AnchorPresets.BottomLeft,
                // 设计 bottom:100 left:270（元素高 110 → 底锚偏移 -210）
                Location = new Float2(270f, -210f),
                Size = new Float2(160f, 110f),
                Parent = this
            };
        }

        private void BuildVerticalText()
        {
            _verticalText = new VerticalTextControl
            {
                Text = "气血存亡，武者根本",
                AnchorPreset = AnchorPresets.TopRight,
                // 设计 right:72（元素宽 40 → 右锚偏移 -72）
                Location = new Float2(-72f, _screenSize.Y * 0.5f - 100f),
                Size = new Float2(40f, 200f),
                Parent = this
            };
        }

        private void BuildHintAndVision()
        {
            float hintWidth = 200f;
            float hintHeight = 40f;
            float visionWidth = 112f;
            float gap = 16f;
            float totalWidth = hintWidth + gap + visionWidth;

            _hintControl = new GuideHintControl
            {
                AnchorPreset = AnchorPresets.BottomCenter,
                // 设计 bottom-10 = 40px
                Location = new Float2(-totalWidth * 0.5f, -40f),
                Size = new Float2(hintWidth, hintHeight),
                Parent = this
            };

            _visionButton = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Sm,
                Text = "元素视野",
                AnchorPreset = AnchorPresets.BottomCenter,
                Location = new Float2(-totalWidth * 0.5f + hintWidth + gap, -40f),
                Size = new Float2(visionWidth, hintHeight),
                Parent = this
            };
            _visionButton.ButtonClicked += OnVisionClicked;
        }

        /// <summary>返回战斗 HUD（兼容旧事件）。</summary>
        public event Action Back;

        /// <summary>进入元素视野（兼容旧事件）。</summary>
        public event Action Vision;

        /// <summary>
        /// 标准导航事件（对齐其余 Ink 页面契约，由工厂订阅后调用路由器执行跳转）。
        /// 返回按钮 → back-hud；元素视野按钮 → nav-element-vision。
        /// </summary>
        public event Action<string> NavigationRequested;

        private void OnBackClicked()
        {
            Back?.Invoke();
            NavigationRequested?.Invoke(InkPageDomIds.BackHud);
        }

        private void OnVisionClicked(Button button)
        {
            Vision?.Invoke();
            NavigationRequested?.Invoke(InkPageDomIds.NavElementVision);
        }

        public void SetStep(int current, int total)
        {
            if (_stepPanel != null)
            {
                _stepPanel.CurrentStep = current;
                _stepPanel.TotalSteps = total;
            }
        }

        public void SetVerticalText(string text)
        {
            if (_verticalText != null)
                _verticalText.Text = text ?? string.Empty;
        }

        public void SetHint(string key, string text)
        {
            if (_hintControl != null)
            {
                _hintControl.Key = key ?? string.Empty;
                _hintControl.Text = text ?? string.Empty;
            }
        }

        /// <summary>
        /// 依据当前屏幕尺寸刷新与尺寸相关的布局：
        /// 暗化遮罩的高亮洞（设计 bottom-24 left-24，380x88）。其余子控件均使用锚点偏移（分辨率无关）。
        /// </summary>
        private void ApplyLayout()
        {
            float sw = _screenSize.X;
            float sh = _screenSize.Y;

            if (_dimMask != null)
            {
                _dimMask.HoleRect = new Rectangle(24f, sh - 24f - 88f, 380f, 88f);
            }
        }

        public void RefreshLayout()
        {
            float w = Width;
            float h = Height;
            if (w <= 0f || h <= 0f)
            {
                var screen = FlaxEngine.Screen.Size;
                w = screen.X;
                h = screen.Y;
            }
            if (w <= 0f || h <= 0f)
            {
                w = 1920f;
                h = 1080f;
            }
            _screenSize = new Float2(w, h);

            if (_verticalText != null)
            {
                _verticalText.Location = new Float2(-72f, _screenSize.Y * 0.5f - 100f);
            }

            ApplyLayout();
        }

        /// <summary>
        /// 引导暗化遮罩：在聚光洞四周绘制四条半透明暗带（近似设计 spotlight 的巨型扩散阴影
        /// box-shadow 0 0 0 9999px），使引导焦点区域"亮起"、其余屏幕变暗。
        /// 不接收鼠标事件（Enabled=false，仍参与绘制）。
        /// </summary>
        private class GuideDimMask : ContainerControl
        {
            /// <summary>高亮洞（相对本控件/页面坐标系）。</summary>
            public Rectangle HoleRect { get; set; } = new Rectangle(0f, 0f, 0f, 0f);

            /// <summary>遮罩色：rgba(14,16,22,0.65)（对齐设计）。</summary>
            private static readonly Color MaskColor = new Color(14f / 255f, 16f / 255f, 22f / 255f, 0.65f);

            public GuideDimMask()
            {
                BackgroundColor = Color.Transparent;
                ClipChildren = false;
            }

            public override void Draw()
            {
                base.Draw();

                if (Width <= 0f || Height <= 0f || HoleRect.Width <= 0f || HoleRect.Height <= 0f)
                    return;

                var hole = HoleRect;
                float midTop = Mathf.Max(hole.Top, 0f);
                float midBottom = Mathf.Min(hole.Bottom, Height);
                float midH = midBottom - midTop;

                // 上带
                Render2D.FillRectangle(new Rectangle(0f, 0f, Width, midTop), MaskColor);
                // 下带
                Render2D.FillRectangle(new Rectangle(0f, midBottom, Width, Height - midBottom), MaskColor);
                // 左带（仅中段，避免与上下带重叠）
                Render2D.FillRectangle(new Rectangle(0f, midTop, Mathf.Max(hole.Left, 0f), midH), MaskColor);
                // 右带
                float rightLeft = Mathf.Min(hole.Right, Width);
                Render2D.FillRectangle(new Rectangle(rightLeft, midTop, Width - rightLeft, midH), MaskColor);
            }
        }

        private class GuideStepPanel : ContainerControl
        {
            private int _currentStep;
            private int _totalSteps;
            private float _pulseTime;

            public int CurrentStep
            {
                get => _currentStep;
                set
                {
                    _currentStep = value;
                    if (_currentStep < 1) _currentStep = 1;
                }
            }

            public int TotalSteps
            {
                get => _totalSteps;
                set
                {
                    _totalSteps = value;
                    if (_totalSteps < 1) _totalSteps = 1;
                }
            }

            public GuideStepPanel()
            {
                BackgroundColor = InkWashTheme.Panel;
                ClipChildren = false;
            }

            public override void Update(float deltaTime)
            {
                base.Update(deltaTime);
                _pulseTime += deltaTime;
            }

            public override void Draw()
            {
                base.Draw();

                if (Width <= 0f || Height <= 0f)
                    return;

                float pulseAlpha = 0.5f + 0.5f * Mathf.Sin(_pulseTime * 2f);

                Color borderColor = new Color(
                    InkWashTheme.BorderGold.R, InkWashTheme.BorderGold.G,
                    InkWashTheme.BorderGold.B, InkWashTheme.BorderGold.A);
                Render2D.DrawRectangle(new Rectangle(0, 0, Width, Height), borderColor, borderColor, borderColor, borderColor, 1f);

                float titleX = 12f;
                float titleY = (Height - 24f) * 0.5f;
                var titleFont = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 18f).GetFont();
                if (titleFont != null)
                {
                    Render2D.DrawText(titleFont, "引",
                        new Rectangle(titleX, titleY, 30f, 24f),
                        InkWashTheme.GoldBright, TextAlignment.Near, TextAlignment.Near, TextWrapping.NoWrap);
                }

                float trackX = titleX + 36f;
                float trackY = (Height - 4f) * 0.5f;
                float trackWidth = 80f;
                float trackHeight = 4f;

                Render2D.FillRectangle(new Rectangle(trackX, trackY, trackWidth, trackHeight),
                    new Color(0f, 0f, 0f, 0.8f));

                float fillPercent = (float)_currentStep / _totalSteps;
                Color fillColor = new Color(
                    InkWashTheme.GoldDeep.R + (InkWashTheme.GoldBright.R - InkWashTheme.GoldDeep.R) * pulseAlpha,
                    InkWashTheme.GoldDeep.G + (InkWashTheme.GoldBright.G - InkWashTheme.GoldDeep.G) * pulseAlpha,
                    InkWashTheme.GoldDeep.B + (InkWashTheme.GoldBright.B - InkWashTheme.GoldDeep.B) * pulseAlpha,
                    1f);
                Render2D.FillRectangle(new Rectangle(trackX, trackY, trackWidth * fillPercent, trackHeight),
                    fillColor);

                float countX = trackX + trackWidth + 16f;
                float countY = (Height - 24f) * 0.5f;

                var countFont = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 18f).GetFont();
                if (countFont != null)
                {
                    Render2D.DrawText(countFont, _currentStep.ToString(),
                        new Rectangle(countX, countY, 30f, 24f),
                        InkWashTheme.GoldBright, TextAlignment.Near, TextAlignment.Near, TextWrapping.NoWrap);
                }

                var slashFont = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 13f).GetFont();
                if (slashFont != null)
                {
                    Render2D.DrawText(slashFont, "/",
                        new Rectangle(countX + 18f, countY + 3f, 20f, 20f),
                        InkWashTheme.PaperDark, TextAlignment.Near, TextAlignment.Near, TextWrapping.NoWrap);
                }

                if (slashFont != null)
                {
                    Render2D.DrawText(slashFont, _totalSteps.ToString(),
                        new Rectangle(countX + 28f, countY + 3f, 30f, 20f),
                        InkWashTheme.PaperDark, TextAlignment.Near, TextAlignment.Near, TextWrapping.NoWrap);
                }
            }
        }

        private class SpotlightControl : ContainerControl
        {
            private float _pulseTime;

            public SpotlightControl()
            {
                BackgroundColor = Color.Transparent;
                ClipChildren = false;
            }

            public override void Update(float deltaTime)
            {
                base.Update(deltaTime);
                _pulseTime += deltaTime;
            }

            public override void Draw()
            {
                base.Draw();

                if (Width <= 0f || Height <= 0f)
                    return;

                float pulseAlpha = 0.5f + 0.5f * Mathf.Sin(_pulseTime * (Mathf.TwoPi / 3f)); // 对齐 3s 脉冲

                // 1. 虚线金色边框（对齐设计 border: 2px dashed，金↔金亮脉冲）
                Color borderColor = new Color(
                    InkWashTheme.GoldPrimary.R + (InkWashTheme.GoldBright.R - InkWashTheme.GoldPrimary.R) * pulseAlpha,
                    InkWashTheme.GoldPrimary.G + (InkWashTheme.GoldBright.G - InkWashTheme.GoldPrimary.G) * pulseAlpha,
                    InkWashTheme.GoldPrimary.B + (InkWashTheme.GoldBright.B - InkWashTheme.GoldPrimary.B) * pulseAlpha,
                    1f);
                DrawDashedRect(new Rectangle(0f, 0f, Width, Height), borderColor, 2f, 10f, 6f);

                // 2. 外侧柔光（近似设计 box-shadow 0 0 28~40px gold）
                var glowColor = new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G,
                    InkWashTheme.GoldPrimary.B, 0.08f + pulseAlpha * 0.10f);
                Render2D.DrawRectangle(new Rectangle(-4f, -4f, Width + 8f, Height + 8f),
                    glowColor, glowColor, glowColor, glowColor, 4f);

                // 3. 竖排角色名（对齐设计 .ink-vertical-title 18px 纸白，左侧）
                var nameFont = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 18f).GetFont();
                if (nameFont != null)
                {
                    const string name = "燕归人";
                    const float nameCharH = 20f;
                    const float nameX = 14f;
                    float nameBottomY = Height - 12f;
                    for (int i = 0; i < name.Length; i++)
                    {
                        Render2D.DrawText(nameFont, name[i].ToString(),
                            new Rectangle(nameX, nameBottomY - (i + 1) * nameCharH, 20f, nameCharH),
                            InkWashTheme.PaperBright, TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
                    }
                }

                // 4. 气血/体魄条（对齐设计：气血 10px 朱红 72%，体魄 6px 玉色 88%）
                float barsX = 52f;
                float barsW = Width - barsX - 52f;
                float hpBarY = Height - 42f;
                const float hpBarH = 10f;
                const float vigorBarH = 6f;
                float vigorBarY = hpBarY - 8f - vigorBarH;

                var labelFont = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 13f).GetFont();
                var valueFont = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 11f).GetFont();

                DrawBar(barsX, vigorBarY, barsW, vigorBarH, 0.88f, InkWashTheme.JadePrimary,
                    labelFont, valueFont, "体魄", "88");
                DrawBar(barsX, hpBarY, barsW, hpBarH, 0.72f, InkWashTheme.VermilionDeep,
                    labelFont, valueFont, "气血", "720");
            }

            /// <summary>绘制一条带标签与数值的气血/体魄条（标签居左、数值居右）。</summary>
            private static void DrawBar(float x, float y, float w, float h, float fill, Color fillColor,
                Font labelFont, Font valueFont, string label, string value)
            {
                Render2D.FillRectangle(new Rectangle(x, y, w, h), new Color(0f, 0f, 0f, 0.4f));
                Render2D.DrawRectangle(new Rectangle(x, y, w, h),
                    InkWashTheme.BorderNeutralL2, InkWashTheme.BorderNeutralL2, InkWashTheme.BorderNeutralL2, InkWashTheme.BorderNeutralL2, 1f);
                Render2D.FillRectangle(new Rectangle(x, y, w * fill, h), fillColor);

                if (labelFont != null)
                {
                    Render2D.DrawText(labelFont, label, new Rectangle(x - 44f, y - 2f, 40f, 16f),
                        InkWashTheme.PaperAged, TextAlignment.Near, TextAlignment.Near, TextWrapping.NoWrap);
                }
                if (valueFont != null)
                {
                    Render2D.DrawText(valueFont, value, new Rectangle(x + w + 8f, y - 2f, 40f, 16f),
                        InkWashTheme.PaperDark, TextAlignment.Near, TextAlignment.Near, TextWrapping.NoWrap);
                }
            }

            /// <summary>绘制虚线矩形边框（对齐设计 dashed 边框：2px、10px 划 6px 空）。</summary>
            private static void DrawDashedRect(Rectangle rect, Color color, float thickness, float dashLen, float gapLen)
            {
                DrawDashedLine(new Float2(rect.Left, rect.Top), new Float2(rect.Right, rect.Top), color, thickness, dashLen, gapLen);
                DrawDashedLine(new Float2(rect.Left, rect.Bottom), new Float2(rect.Right, rect.Bottom), color, thickness, dashLen, gapLen);
                DrawDashedLine(new Float2(rect.Left, rect.Top), new Float2(rect.Left, rect.Bottom), color, thickness, dashLen, gapLen);
                DrawDashedLine(new Float2(rect.Right, rect.Top), new Float2(rect.Right, rect.Bottom), color, thickness, dashLen, gapLen);
            }

            private static void DrawDashedLine(Float2 from, Float2 to, Color color, float thickness, float dashLen, float gapLen)
            {
                var dir = to - from;
                float len = dir.Length;
                if (len <= 0.01f)
                    return;

                var unit = dir / len;
                float pos = 0f;
                while (pos < len)
                {
                    float segEnd = Mathf.Min(pos + dashLen, len);
                    Render2D.DrawLine(from + unit * pos, from + unit * segEnd, color, thickness);
                    pos = segEnd + gapLen;
                }
            }
        }

        private class InkArrowControl : ContainerControl
        {
            private float _bobTime;

            public InkArrowControl()
            {
                BackgroundColor = Color.Transparent;
                ClipChildren = false;
            }

            public override void Update(float deltaTime)
            {
                base.Update(deltaTime);
                _bobTime += deltaTime;
            }

            public override void Draw()
            {
                base.Draw();

                if (Width <= 0f || Height <= 0f)
                    return;

                float bobX = Mathf.Sin(_bobTime * 0.8f) * 4f;
                float bobY = Mathf.Cos(_bobTime * 0.8f) * 3f;

                Float2 start = new Float2(Width - 10f, 10f);
                Float2 mid1 = new Float2(Width * 0.6f, 15f);
                Float2 mid2 = new Float2(Width * 0.4f, Height * 0.4f);
                Float2 tip = new Float2(22f + bobX, Height - 12f + bobY);

                int steps = 20;
                for (int i = 0; i < steps; i++)
                {
                    float t1 = (float)i / steps;
                    float t2 = (float)(i + 1) / steps;

                    Float2 p1 = CatmullRom(start, mid1, mid2, tip, t1);
                    Float2 p2 = CatmullRom(start, mid1, mid2, tip, t2);

                    float alpha;
                    if (t1 < 0.25f)
                        alpha = t1 / 0.25f * 0.5f;
                    else if (t1 < 0.6f)
                        alpha = 0.5f + (t1 - 0.25f) / 0.35f * 0.35f;
                    else
                        alpha = 0.85f + (t1 - 0.6f) / 0.4f * 0.15f;

                    Color arrowColor = new Color(
                        InkWashTheme.PaperDark.R + (InkWashTheme.PaperBright.R - InkWashTheme.PaperDark.R) * alpha,
                        InkWashTheme.PaperDark.G + (InkWashTheme.PaperBright.G - InkWashTheme.PaperDark.G) * alpha,
                        InkWashTheme.PaperDark.B + (InkWashTheme.PaperBright.B - InkWashTheme.PaperDark.B) * alpha,
                        alpha);

                    Render2D.DrawLine(p1, p2, arrowColor, 4.5f);
                }

                Float2 arrowHead1 = tip + new Float2(16f, -16f);
                Float2 arrowHead2 = tip + new Float2(20f, -8f);

                Render2D.DrawLine(tip, arrowHead1, InkWashTheme.PaperBright, 3.5f);
                Render2D.DrawLine(tip, arrowHead2, InkWashTheme.PaperBright, 3.5f);
            }

            private Float2 CatmullRom(Float2 p0, Float2 p1, Float2 p2, Float2 p3, float t)
            {
                float t2 = t * t;
                float t3 = t2 * t;

                return 0.5f * (
                    (-t3 + 2f * t2 - t) * p0 +
                    (3f * t3 - 5f * t2 + 2f) * p1 +
                    (-3f * t3 + 4f * t2 + t) * p2 +
                    (t3 - t2) * p3);
            }
        }

        private class VerticalTextControl : ContainerControl
        {
            private string _text;

            public string Text
            {
                get => _text;
                set => _text = value;
            }

            public VerticalTextControl()
            {
                BackgroundColor = Color.Transparent;
                ClipChildren = false;
            }

            public override void Draw()
            {
                base.Draw();

                if (Width <= 0f || Height <= 0f || string.IsNullOrEmpty(_text))
                    return;

                float charWidth = 24f;
                float charHeight = 56f;
                float startX = Width * 0.5f;
                float startY = Height - charHeight;

                var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 28f).GetFont();
                if (font == null)
                    return;

                for (int i = 0; i < _text.Length; i++)
                {
                    char c = _text[i];
                    float x = startX - (i % 2) * charWidth * 0.3f;
                    float y = startY - i * charHeight;

                    Render2D.DrawText(font, c.ToString(),
                        new Rectangle(x - 14f, y, charWidth, charHeight),
                        InkWashTheme.PaperBright, TextAlignment.Center, TextAlignment.Near, TextWrapping.NoWrap);
                }
            }
        }

        private class GuideHintControl : ContainerControl
        {
            private string _key;
            private string _text;

            public string Key
            {
                get => _key;
                set => _key = value;
            }

            public string Text
            {
                get => _text;
                set => _text = value;
            }

            public GuideHintControl()
            {
                BackgroundColor = InkWashTheme.Panel;
                ClipChildren = false;
            }

            public override void Draw()
            {
                base.Draw();

                if (Width <= 0f || Height <= 0f)
                    return;

                Render2D.DrawRectangle(new Rectangle(0, 0, Width, Height),
                    InkWashTheme.BorderGold, InkWashTheme.BorderGold, InkWashTheme.BorderGold, InkWashTheme.BorderGold, 1f);

                float textX = 16f;
                float textY = (Height - 20f) * 0.5f;
                var bodyFont = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f).GetFont();
                if (bodyFont != null)
                {
                    Render2D.DrawText(bodyFont, "按",
                        new Rectangle(textX, textY, 20f, 20f),
                        InkWashTheme.PaperBright, TextAlignment.Near, TextAlignment.Near, TextWrapping.NoWrap);
                }

                float keyX = textX + 28f;
                float keyY = (Height - 28f) * 0.5f;
                float keySize = 28f;

                Render2D.FillRectangle(new Rectangle(keyX, keyY, keySize, keySize),
                    new Color(0f, 0f, 0f, 0.9f));
                Render2D.DrawRectangle(new Rectangle(keyX, keyY, keySize, keySize),
                    InkWashTheme.GoldPrimary, InkWashTheme.GoldPrimary, InkWashTheme.GoldPrimary, InkWashTheme.GoldPrimary, 1f);

                var keyFont = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 15f).GetFont();
                if (keyFont != null)
                {
                    Render2D.DrawText(keyFont, _key ?? "J",
                        new Rectangle(keyX, keyY, keySize, keySize),
                        InkWashTheme.GoldBright, TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
                }

                float hintX = keyX + keySize + 16f;
                if (bodyFont != null)
                {
                    Render2D.DrawText(bodyFont, _text ?? "键查看属性面板",
                        new Rectangle(hintX, textY, Width - hintX - 16f, 20f),
                        InkWashTheme.PaperBright, TextAlignment.Near, TextAlignment.Near, TextWrapping.NoWrap);
                }
            }
        }
    }
}
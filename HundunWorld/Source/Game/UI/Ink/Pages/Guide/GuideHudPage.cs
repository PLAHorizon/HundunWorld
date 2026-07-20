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
                BuildBackButton();
                BuildStepPanel();
                BuildSpotlight();
                BuildInkArrow();
                BuildVerticalText();
                BuildHintAndVision();

                ApplyLayout();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[GuideHudPage] 初始化失败: {ex.Message}");
            }
        }

        private void BuildBackButton()
        {
            _backButton = new InkBackButton
            {
                AnchorPreset = AnchorPresets.TopRight,
                Location = new Float2(-60f, 20f),
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
                Location = new Float2(270f, -190f),
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
                Location = new Float2(-112f, _screenSize.Y * 0.5f - 100f),
                Size = new Float2(40f, 200f),
                Parent = this
            };
        }

        private void BuildHintAndVision()
        {
            float hintWidth = 220f;
            float hintHeight = 40f;
            float visionWidth = 100f;
            float gap = 16f;
            float totalWidth = hintWidth + gap + visionWidth;

            _hintControl = new GuideHintControl
            {
                AnchorPreset = AnchorPresets.BottomCenter,
                Location = new Float2(-totalWidth * 0.5f, -50f),
                Size = new Float2(hintWidth, hintHeight),
                Parent = this
            };

            _visionButton = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Sm,
                Text = "元素视野",
                AnchorPreset = AnchorPresets.BottomCenter,
                Location = new Float2(-totalWidth * 0.5f + hintWidth + gap, -50f),
                Size = new Float2(visionWidth, hintHeight),
                Parent = this
            };
            _visionButton.ButtonClicked += OnVisionClicked;
        }

        public event Action Back;
        public event Action Vision;

        private void OnBackClicked()
        {
            Back?.Invoke();
        }

        private void OnVisionClicked(Button button)
        {
            Vision?.Invoke();
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

        private void ApplyLayout()
        {
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
                _verticalText.Location = new Float2(-112f, _screenSize.Y * 0.5f - 100f);
            }

            ApplyLayout();
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

                float pulseAlpha = 0.5f + 0.5f * Mathf.Sin(_pulseTime * 1f);

                Color borderColor = new Color(
                    InkWashTheme.GoldPrimary.R + (InkWashTheme.GoldBright.R - InkWashTheme.GoldPrimary.R) * pulseAlpha,
                    InkWashTheme.GoldPrimary.G + (InkWashTheme.GoldBright.G - InkWashTheme.GoldPrimary.G) * pulseAlpha,
                    InkWashTheme.GoldPrimary.B + (InkWashTheme.GoldBright.B - InkWashTheme.GoldPrimary.B) * pulseAlpha,
                    1f);
                Render2D.DrawRectangle(new Rectangle(0, 0, Width, Height), borderColor, borderColor, borderColor, borderColor, 2f);

                float shadowRadius = 30f + pulseAlpha * 10f;
                Color shadowColor = new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G,
                    InkWashTheme.GoldPrimary.B, 0.1f + pulseAlpha * 0.1f);
                Render2D.DrawRectangle(new Rectangle(-shadowRadius, -shadowRadius, Width + shadowRadius * 2f, Height + shadowRadius * 2f),
                    shadowColor, shadowColor, shadowColor, shadowColor, 0f);

                float hpBarX = 60f;
                float hpBarY = Height - 40f;
                float hpBarWidth = Width - 140f;
                float hpBarHeight = 10f;

                Render2D.FillRectangle(new Rectangle(hpBarX, hpBarY, hpBarWidth, hpBarHeight),
                    new Color(0f, 0f, 0f, 0.4f));
                Render2D.DrawRectangle(new Rectangle(hpBarX, hpBarY, hpBarWidth, hpBarHeight),
                    InkWashTheme.BorderNeutralL2, InkWashTheme.BorderNeutralL2, InkWashTheme.BorderNeutralL2, InkWashTheme.BorderNeutralL2, 1f);

                float hpFill = 0.72f;
                Render2D.FillRectangle(new Rectangle(hpBarX, hpBarY, hpBarWidth * hpFill, hpBarHeight),
                    new Color(InkWashTheme.VermilionDeep.R, InkWashTheme.VermilionDeep.G,
                        InkWashTheme.VermilionDeep.B, 1f));

                var hpLabelFont = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 13f).GetFont();
                if (hpLabelFont != null)
                {
                    Render2D.DrawText(hpLabelFont, "气血",
                        new Rectangle(hpBarX - 40f, hpBarY, 40f, 14f),
                        InkWashTheme.PaperAged, TextAlignment.Near, TextAlignment.Near, TextWrapping.NoWrap);
                }

                var hpValueFont = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 11f).GetFont();
                if (hpValueFont != null)
                {
                    Render2D.DrawText(hpValueFont, "720",
                        new Rectangle(hpBarX + hpBarWidth + 8f, hpBarY + 2f, 50f, 14f),
                        InkWashTheme.PaperDark, TextAlignment.Near, TextAlignment.Near, TextWrapping.NoWrap);
                }

                float vigorBarY = hpBarY - 18f;
                float vigorBarHeight = 6f;

                Render2D.FillRectangle(new Rectangle(hpBarX, vigorBarY, hpBarWidth, vigorBarHeight),
                    new Color(0f, 0f, 0f, 0.4f));
                Render2D.DrawRectangle(new Rectangle(hpBarX, vigorBarY, hpBarWidth, vigorBarHeight),
                    InkWashTheme.BorderNeutralL2, InkWashTheme.BorderNeutralL2, InkWashTheme.BorderNeutralL2, InkWashTheme.BorderNeutralL2, 1f);

                float vigorFill = 0.88f;
                Render2D.FillRectangle(new Rectangle(hpBarX, vigorBarY, hpBarWidth * vigorFill, vigorBarHeight),
                    new Color(InkWashTheme.JadePrimary.R, InkWashTheme.JadePrimary.G,
                        InkWashTheme.JadePrimary.B, 1f));

                if (hpLabelFont != null)
                {
                    Render2D.DrawText(hpLabelFont, "体魄",
                        new Rectangle(hpBarX - 40f, vigorBarY, 40f, 14f),
                        InkWashTheme.PaperAged, TextAlignment.Near, TextAlignment.Near, TextWrapping.NoWrap);
                }

                if (hpValueFont != null)
                {
                    Render2D.DrawText(hpValueFont, "88",
                        new Rectangle(hpBarX + hpBarWidth + 8f, vigorBarY + 1f, 50f, 14f),
                        InkWashTheme.PaperDark, TextAlignment.Near, TextAlignment.Near, TextWrapping.NoWrap);
                }

                float nameX = 8f;
                float nameY = Height - 20f;
                var nameFont = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 18f).GetFont();
                if (nameFont != null)
                {
                    Render2D.DrawText(nameFont, "燕归人",
                        new Rectangle(nameX, nameY, 80f, 20f),
                        InkWashTheme.PaperBright, TextAlignment.Near, TextAlignment.Near, TextWrapping.NoWrap);
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
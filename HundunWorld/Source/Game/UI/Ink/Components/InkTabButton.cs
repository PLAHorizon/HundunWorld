using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.StyleSystem;
using System;

namespace HundunWorld.Game.UI.Ink.Components
{
    public class InkTabButton : ContainerControl
    {
        public event Action Clicked;

        public InkParticleSystem ParticleSystem { get; set; }

        public string Text
        {
            get => _label.Text;
            set => _label.Text = value;
        }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                ApplyVisualState();
            }
        }

        private Label _label;
        private bool _isSelected;
        private bool _isAnimating;
        private float _pulseTime;
        private bool _isHovered;
        private bool _isPressed;

        public InkTabButton()
        {
            Size = new Float2(130f, 36f);
            AutoFocus = true;
            ClipChildren = false;

            _label = new Label
            {
                AnchorPreset = AnchorPresets.StretchAll,
                Offsets = Margin.Zero,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 14f),
                TextColor = InkWashTheme.TextSecondary,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            AddChild(_label);

            ApplyVisualState();
        }

        private void ApplyVisualState()
        {
            // ds-tabs §4.5：激活标签文字 --ink-text-primary，非激活 --ink-text-secondary
            _label.TextColor = _isSelected ? InkWashTheme.TextDefault : InkWashTheme.TextSecondary;
        }

        public override void OnMouseEnter(Float2 location)
        {
            _isHovered = true;
            // ds-tabs §4.5：悬停态文字色 --ink-text-primary
            if (!_isSelected)
                _label.TextColor = InkWashTheme.TextDefault;
            base.OnMouseEnter(location);
        }

        public override void OnMouseLeave()
        {
            _isHovered = false;
            if (!_isSelected)
                _label.TextColor = InkWashTheme.TextSecondary;
            base.OnMouseLeave();
        }

        public override bool OnMouseDown(Float2 location, MouseButton button)
        {
            if (button == MouseButton.Left)
            {
                _isPressed = true;
                Focus();
            }
            return base.OnMouseDown(location, button);
        }

        public override bool OnMouseUp(Float2 location, MouseButton button)
        {
            if (button == MouseButton.Left && _isPressed)
            {
                _isPressed = false;
                if (ContainsPoint(ref location))
                {
                    Clicked?.Invoke();
                    TriggerClickEffect();
                }
            }
            return base.OnMouseUp(location, button);
        }

        private void TriggerClickEffect()
        {
            _isAnimating = true;
            _pulseTime = 0f;

            EmitGoldBurst();
        }

        private void EmitGoldBurst()
        {
            if (ParticleSystem == null) return;
            var center = new Float2(Width * 0.5f, Height * 0.5f);
            var screenPos = PointToScreen(center);
            var localPos = ParticleSystem.PointFromScreen(screenPos);
            ParticleSystem.EmitGoldBurst(localPos, count: 10, isLarge: false);
        }

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);
            if (_isAnimating)
            {
                _pulseTime += deltaTime;
                if (_pulseTime > 0.8f)
                {
                    _pulseTime = 0f;
                    _isAnimating = false;
                }
            }
        }

        public override void Draw()
        {
            if (_isAnimating)
                DrawPulseOverlay();

            base.Draw();

            // ds-tabs §4.5：激活态 2px 下划线，颜色 --ink-jade-primary
            if (_isSelected)
            {
                Render2D.FillRectangle(new Rectangle(0f, Height - 2f, Width, 2f), InkWashTheme.JadePrimary);
            }
        }

        private void DrawPulseOverlay()
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin(_pulseTime * 14f);
            var pulseClr = new Color(InkWashTheme.JadeBright.R, InkWashTheme.JadeBright.G,
                InkWashTheme.JadeBright.B, pulse * 0.15f);
            Render2D.FillRectangle(new Rectangle(Float2.Zero, Size), pulseClr);
        }
    }
}

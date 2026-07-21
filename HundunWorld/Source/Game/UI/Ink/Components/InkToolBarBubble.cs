using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.StyleSystem;

namespace HundunWorld.Game.UI.Ink.Components
{
    public class InkToolBarBubble : ContainerControl
    {
        private const float BubbleW = 200f;
        private const float BubbleH = 96f;
        private const float PortraitSize = 52f;
        private const float ArrowW = 8f;
        private const float ArrowH = 14f;
        private const float Seg1H = 30f;
        private const float Seg2H = 28f;
        private const float Seg3H = 24f;

        public static InkToolBarBubble Instance { get; private set; }

        private Label _nameLabel;
        private Label _enhanceLabel;
        private Label _typeLabel;
        private Label _attrLabel;
        private Label _extraLabel;
        private Texture _portraitTexture;
        private Control _target;
        private float _pulseTime;

        public InkToolBarBubble()
        {
            Instance = this;
            Size = new Float2(BubbleW + ArrowW, BubbleH);
            AutoFocus = false;
            ClipChildren = false;
            Visible = false;

            float lx = ArrowW + PortraitSize + 8f;
            float rw = BubbleW - PortraitSize - 12f;

            _nameLabel = new Label
            {
                Location = new Float2(lx, 2f),
                Size = new Float2(rw - 40f, 24f),
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 13f),
                TextColor = InkWashTheme.TextDefault,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            AddChild(_nameLabel);

            _enhanceLabel = new Label
            {
                Location = new Float2(BubbleW - 44f, 2f),
                Size = new Float2(40f, 24f),
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 12f),
                TextColor = InkWashTheme.TextBrand,
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
            };
            AddChild(_enhanceLabel);

            _typeLabel = new Label
            {
                Location = new Float2(lx, Seg1H + 2f),
                Size = new Float2(rw, 22f),
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextSecondary,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            AddChild(_typeLabel);

            _attrLabel = new Label
            {
                Location = new Float2(lx, Seg1H + Seg2H * 0.5f + 3f),
                Size = new Float2(rw, Seg2H * 0.5f),
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 11f),
                TextColor = InkWashTheme.JadeBright,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            AddChild(_attrLabel);

            _extraLabel = new Label
            {
                Location = new Float2(lx, Seg1H + Seg2H + 2f),
                Size = new Float2(rw, Seg3H - 4f),
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f),
                TextColor = InkWashTheme.TextTertiary,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            AddChild(_extraLabel);
        }

        public Control Target => _target;

        public void ShowAt(Control target, string name, string type, string attr, string extra, int enhance, Texture portrait = null)
        {
            _target = target;
            _portraitTexture = portrait;
            _nameLabel.Text = name;
            _typeLabel.Text = type;
            _attrLabel.Text = attr;
            _extraLabel.Text = extra;
            _enhanceLabel.Text = enhance > 0 ? $"+{enhance}" : "";

            if (target != null)
            {
                var pos = target.PointToParent(target.Parent, Float2.Zero);
                Location = new Float2(pos.X + target.Width + 6f, pos.Y);
            }

            Visible = true;
        }

        public void Hide()
        {
            _target = null;
            Visible = false;
        }

        public void SetPortraitTexture(Texture texture)
        {
            _portraitTexture = texture;
        }

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);
            _pulseTime += deltaTime;

            if (Visible && _target != null && !_target.IsFocused && !ContainsFocus)
                Hide();
        }

        public override void Draw()
        {
            var bg = new Color(18f / 255f, 20f / 255f, 26f / 255f, 0.94f);
            var border = InkWashTheme.BorderGoldSubtle;

            var bodyRect = new Rectangle(ArrowW, 0f, BubbleW, BubbleH);
            Render2D.FillRectangle(bodyRect, bg);
            Render2D.DrawRectangle(bodyRect, border, 1f);

            var arrowTip = new Float2(0f, BubbleH * 0.5f);
            var arrowMid = new Float2(ArrowW, BubbleH * 0.5f - ArrowH * 0.5f);
            var arrowBot = new Float2(ArrowW, BubbleH * 0.5f + ArrowH * 0.5f);
            Render2D.FillTriangle(arrowTip, arrowMid, arrowBot, bg);
            Render2D.DrawLine(arrowMid, arrowTip, border, 1f);
            Render2D.DrawLine(arrowTip, arrowBot, border, 1f);

            var portraitRect = new Rectangle(ArrowW + 4f, (BubbleH - PortraitSize) * 0.5f, PortraitSize, PortraitSize);
            if (_portraitTexture != null)
            {
                Render2D.DrawTexture(_portraitTexture, portraitRect);
            }
            else
            {
                Render2D.FillRectangle(portraitRect, new Color(30f / 255f, 33f / 255f, 42f / 255f, 1f));
            }
            float pulseAlpha = 0.15f + 0.1f * Mathf.Sin(_pulseTime * 3f);
            var glowCenter = new Float2(portraitRect.X + portraitRect.Width * 0.5f, portraitRect.Y + portraitRect.Height * 0.5f);
            InkRenderHelper.FillRadialGradient(glowCenter, PortraitSize * 0.7f,
                new Color(InkWashTheme.JadeGlow.R, InkWashTheme.JadeGlow.G, InkWashTheme.JadeGlow.B, pulseAlpha),
                Color.Transparent, 8);

            var sparkAlpha = 0.3f + 0.3f * Mathf.Sin(_pulseTime * 5.7f + 1.2f);
            var sparkPos = new Float2(
                portraitRect.X + portraitRect.Width * (0.5f + 0.35f * Mathf.Sin(_pulseTime * 2.3f)),
                portraitRect.Y + portraitRect.Height * (0.5f + 0.35f * Mathf.Cos(_pulseTime * 3.1f)));
            InkRenderHelper.FillRadialGradient(sparkPos, 8f,
                new Color(InkWashTheme.SpringGreenBright.R, InkWashTheme.SpringGreenBright.G, InkWashTheme.SpringGreenBright.B, sparkAlpha),
                Color.Transparent, 6);

            Render2D.DrawRectangle(portraitRect, InkWashTheme.BorderGold, 1.5f);

            float infoL = ArrowW + 4f;
            Render2D.DrawLine(new Float2(infoL, Seg1H), new Float2(ArrowW + BubbleW, Seg1H), InkWashTheme.BorderFaint);
            Render2D.DrawLine(new Float2(infoL, Seg1H + Seg2H), new Float2(ArrowW + BubbleW, Seg1H + Seg2H), InkWashTheme.BorderFaint);

            base.Draw();
        }
    }
}

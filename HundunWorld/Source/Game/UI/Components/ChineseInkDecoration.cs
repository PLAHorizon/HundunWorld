using FlaxEngine;
using FlaxEngine.GUI;

namespace HundunWorld.Game.UI.Components
{
    /// <summary>
    /// 水墨风格装饰元素 - 用于武侠游戏UI装饰
    /// </summary>
    public class ChineseInkDecoration : ContainerControl
    {
        public enum InkDecorationType
        {
            VerticalDivider,
            HorizontalBrush,
            VerticalBrush,
            InkDot,
        }

        private InkDecorationType _decorationType = InkDecorationType.VerticalDivider;
        // 默认装饰色：鎏金辉光（出处：--ink-gold-glow rgba(200,168,88,0.4)）
        private Color _inkColor = UIStyleTokens.GoldGlow;
        private float _lineThickness = 2.0f;
        private float _fadeLength = 0.3f;
        private float _brushWidth = 4.0f;

        public InkDecorationType DecorationType
        {
            get => _decorationType;
            set
            {
                _decorationType = value;
                ApplyDefaultSize();
            }
        }

        public Color InkColor
        {
            get => _inkColor;
            set => _inkColor = value;
        }

        public float LineThickness
        {
            get => _lineThickness;
            set => _lineThickness = value;
        }

        public float FadeLength
        {
            get => _fadeLength;
            set => _fadeLength = Mathf.Clamp(value, 0f, 0.5f);
        }

        public float BrushWidth
        {
            get => _brushWidth;
            set => _brushWidth = value;
        }

        public ChineseInkDecoration()
        {
            ApplyDefaultSize();
        }

        public ChineseInkDecoration(InkDecorationType type)
        {
            _decorationType = type;
            ApplyDefaultSize();
        }

        private void ApplyDefaultSize()
        {
            switch (_decorationType)
            {
                case InkDecorationType.VerticalDivider:
                    Size = new Float2(2f, 40f);
                    break;
                case InkDecorationType.HorizontalBrush:
                    Size = new Float2(80f, 4f);
                    break;
                case InkDecorationType.VerticalBrush:
                    Size = new Float2(4f, 80f);
                    break;
                case InkDecorationType.InkDot:
                    Size = new Float2(8f, 8f);
                    break;
            }
        }

        public override void DrawSelf()
        {
            base.DrawSelf();

            switch (_decorationType)
            {
                case InkDecorationType.VerticalDivider:
                    DrawVerticalDivider();
                    break;
                case InkDecorationType.HorizontalBrush:
                    DrawHorizontalBrush();
                    break;
                case InkDecorationType.VerticalBrush:
                    DrawVerticalBrush();
                    break;
                case InkDecorationType.InkDot:
                    DrawInkDot();
                    break;
            }
        }

        private void DrawVerticalDivider()
        {
            float x = Width / 2f;
            float top = 0f;
            float bottom = Height;
            float fadeLen = Height * _fadeLength;

            int segments = 32;
            float step = (bottom - top) / segments;

            for (int i = 0; i < segments; i++)
            {
                float y0 = top + i * step;
                float y1 = y0 + step;
                float yMid = (y0 + y1) / 2f;

                float alpha = ComputeFadeAlpha(yMid, top, bottom, fadeLen);

                var color = new Color(_inkColor.R, _inkColor.G, _inkColor.B, _inkColor.A * alpha);
                Render2D.DrawLine(new Float2(x, y0), new Float2(x, y1), color, _lineThickness);
            }
        }

        private void DrawHorizontalBrush()
        {
            float y = Height / 2f;
            float left = 0f;
            float right = Width;
            float fadeLen = Width * _fadeLength;

            int segments = 32;
            float step = (right - left) / segments;

            for (int i = 0; i < segments; i++)
            {
                float x0 = left + i * step;
                float x1 = x0 + step;
                float xMid = (x0 + x1) / 2f;

                float alpha = ComputeFadeAlpha(xMid, left, right, fadeLen);
                float thickness = ComputeBrushThickness(xMid, left, right);

                var color = new Color(_inkColor.R, _inkColor.G, _inkColor.B, _inkColor.A * alpha);
                Render2D.DrawLine(new Float2(x0, y), new Float2(x1, y), color, thickness);
            }
        }

        private void DrawVerticalBrush()
        {
            float x = Width / 2f;
            float top = 0f;
            float bottom = Height;
            float fadeLen = Height * _fadeLength;

            int segments = 32;
            float step = (bottom - top) / segments;

            for (int i = 0; i < segments; i++)
            {
                float y0 = top + i * step;
                float y1 = y0 + step;
                float yMid = (y0 + y1) / 2f;

                float alpha = ComputeFadeAlpha(yMid, top, bottom, fadeLen);
                float thickness = ComputeBrushThickness(yMid, top, bottom);

                var color = new Color(_inkColor.R, _inkColor.G, _inkColor.B, _inkColor.A * alpha);
                Render2D.DrawLine(new Float2(x, y0), new Float2(x, y1), color, thickness);
            }
        }

        private void DrawInkDot()
        {
            float cx = Width / 2f;
            float cy = Height / 2f;
            float radius = Mathf.Min(Width, Height) / 2f;

            int rings = 8;
            for (int i = rings; i >= 1; i--)
            {
                float t = (float)i / rings;
                float r = radius * t;
                float alpha = _inkColor.A * (1f - t * 0.7f);

                var color = new Color(_inkColor.R, _inkColor.G, _inkColor.B, alpha);
                Render2D.FillRectangle(new Rectangle(cx - r, cy - r, r * 2, r * 2), color);
            }
        }

        /// <summary>
        /// Compute alpha that fades at both ends of a line.
        /// Full alpha in the middle, fading to 0 at the edges within fadeLen.
        /// </summary>
        private float ComputeFadeAlpha(float pos, float start, float end, float fadeLen)
        {
            float mid = (start + end) / 2f;
            float halfLen = (end - start) / 2f;

            if (halfLen <= 0f) return 1f;

            float dist = Mathf.Abs(pos - mid);
            float solidEnd = halfLen - fadeLen;

            if (dist <= solidEnd) return 1f;

            float fadeT = (dist - solidEnd) / fadeLen;
            return 1f - Mathf.Clamp(fadeT, 0f, 1f);
        }

        /// <summary>
        /// Compute brush thickness that is thicker in the middle and thinner at ends.
        /// </summary>
        private float ComputeBrushThickness(float pos, float start, float end)
        {
            float mid = (start + end) / 2f;
            float halfLen = (end - start) / 2f;

            if (halfLen <= 0f) return _brushWidth;

            float t = Mathf.Abs(pos - mid) / halfLen;
            // Thicker in middle, thinner at ends
            float scale = 1f - 0.5f * t * t;
            return _lineThickness + (_brushWidth - _lineThickness) * scale;
        }
    }
}

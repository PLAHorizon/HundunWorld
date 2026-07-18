using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.StyleSystem;

namespace HundunWorld.Game.UI.Ink
{
    /// <summary>
    /// 横向分割线。
    /// 对应 CSS <c>.ink-divider</c>，高度 1px，
    /// 渐变背景：transparent → Divider(20%) → Divider(80%) → transparent。
    /// 通过 <see cref="Draw"/> 绘制多段渐变填充近似 CSS linear-gradient。
    /// </summary>
    public class InkDivider : ContainerControl
    {
        /// <summary>渐变分段数</summary>
        private const int GradientSteps = 32;

        /// <summary>
        /// 构造函数：默认高度 1px。
        /// </summary>
        public InkDivider()
        {
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
            Height = 1f;
        }

        /// <inheritdoc />
        public override void Draw()
        {
            if (!Visible || Width <= 0f || Height <= 0f)
                return;

            int steps = Mathf.Max(2, Mathf.Min(GradientSteps, (int)(Width / 2f)));
            float stripWidth = Width / steps;

            for (int i = 0; i < steps; i++)
            {
                float t = (float)i / (steps - 1);
                Color c = EvaluateGradient(t);
                Render2D.FillRectangle(
                    new Rectangle(i * stripWidth, 0, stripWidth + 1f, Height),
                    c);
            }
        }

        /// <summary>
        /// 计算渐变在参数 t（0~1）处的颜色。
        /// 0~0.2：transparent → Divider；0.2~0.8：Divider；0.8~1.0：Divider → transparent。
        /// </summary>
        private static Color EvaluateGradient(float t)
        {
            if (t < 0.2f)
                return Color.Lerp(Color.Transparent, InkWashTheme.Divider, t / 0.2f);
            if (t <= 0.8f)
                return InkWashTheme.Divider;
            return Color.Lerp(InkWashTheme.Divider, Color.Transparent, (t - 0.8f) / 0.2f);
        }
    }

    // =======================================================================

    /// <summary>
    /// 竖向分割线。
    /// 对应 CSS <c>.ink-divider-v</c>，宽度 1px，
    /// 渐变背景：transparent → Divider(20%) → Divider(80%) → transparent（竖向）。
    /// </summary>
    public class InkDividerVertical : ContainerControl
    {
        /// <summary>渐变分段数</summary>
        private const int GradientSteps = 32;

        /// <summary>
        /// 构造函数：默认宽度 1px。
        /// </summary>
        public InkDividerVertical()
        {
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
            Width = 1f;
        }

        /// <inheritdoc />
        public override void Draw()
        {
            if (!Visible || Width <= 0f || Height <= 0f)
                return;

            int steps = Mathf.Max(2, Mathf.Min(GradientSteps, (int)(Height / 2f)));
            float stripHeight = Height / steps;

            for (int i = 0; i < steps; i++)
            {
                float t = (float)i / (steps - 1);
                Color c = EvaluateGradient(t);
                Render2D.FillRectangle(
                    new Rectangle(0, i * stripHeight, Width, stripHeight + 1f),
                    c);
            }
        }

        /// <summary>
        /// 计算渐变在参数 t（0~1）处的颜色。
        /// </summary>
        private static Color EvaluateGradient(float t)
        {
            if (t < 0.2f)
                return Color.Lerp(Color.Transparent, InkWashTheme.Divider, t / 0.2f);
            if (t <= 0.8f)
                return InkWashTheme.Divider;
            return Color.Lerp(InkWashTheme.Divider, Color.Transparent, (t - 0.8f) / 0.2f);
        }
    }

    // =======================================================================

    /// <summary>
    /// 竖排书法标题。
    /// 对应 CSS <c>.ink-vertical-title</c>，等效 <c>writing-mode: vertical-rl</c>，
    /// 使用 <see cref="InkWashTheme.FontRole.Display"/>（马善政体毛笔书法），
    /// 按字符从上到下竖向绘制，品牌文字色。
    /// 用于加载页、章节过场等场景。
    /// </summary>
    public class InkVerticalTitle : ContainerControl
    {
        /// <summary>默认字号</summary>
        private const float DefaultFontSize = 32f;

        /// <summary>字间距系数（对应 CSS letter-spacing: 0.2em）</summary>
        private const float LetterSpacingRatio = 0.2f;

        /// <summary>标题文字</summary>
        private string _text = string.Empty;

        /// <summary>字号</summary>
        private float _fontSize = DefaultFontSize;

        /// <summary>
        /// 标题文字。设置后按字符竖向排列绘制。
        /// </summary>
        public string Text
        {
            get => _text;
            set => _text = value ?? string.Empty;
        }

        /// <summary>
        /// 字号（默认 32px）。
        /// </summary>
        public float FontSize
        {
            get => _fontSize;
            set => _fontSize = Mathf.Max(1f, value);
        }

        /// <summary>
        /// 构造函数：默认透明背景。
        /// </summary>
        public InkVerticalTitle()
        {
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
        }

        /// <inheritdoc />
        public override void Draw()
        {
            if (!Visible || string.IsNullOrEmpty(_text) || Width <= 0f || Height <= 0f)
                return;

            var fontRef = InkRenderHelper.GetFontRef(
                InkWashTheme.FontRole.Display, _fontSize);

            float charSpacing = _fontSize * (1f + LetterSpacingRatio);

            var font = fontRef.GetFont();
            if (font == null)
                return;

            for (int i = 0; i < _text.Length; i++)
            {
                string ch = _text[i].ToString();
                float y = i * charSpacing;
                var charRect = new Rectangle(0, y, Width, _fontSize);
                Render2D.DrawText(
                    font,
                    ch,
                    charRect,
                    InkWashTheme.TextBrand,
                    TextAlignment.Center,
                    TextAlignment.Near,
                    TextWrapping.NoWrap);
            }
        }
    }
}

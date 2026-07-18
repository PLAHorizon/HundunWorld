using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.StyleSystem;

namespace HundunWorld.Game.UI.Ink
{
    /// <summary>
    /// 进度条填充色变体。
    /// 对应 CSS <c>.ink-bar-fill</c> / <c>.ink-bar-fill-jade</c> /
    /// <c>.ink-bar-fill-blood</c> / <c>.ink-bar-fill-vermilion</c>。
    /// </summary>
    public enum InkBarFillVariant
    {
        /// <summary>鎏金填充 — GoldDeep → GoldBright 渐变，对应 .ink-bar-fill</summary>
        Gold,

        /// <summary>翡翠填充 — 暗翡翠 → JadeBright 渐变，对应 .ink-bar-fill-jade</summary>
        Jade,

        /// <summary>血色填充 — 暗血红 → BloodBright 渐变，对应 .ink-bar-fill-blood</summary>
        Blood,

        /// <summary>朱红填充 — VermilionDeep → VermilionBright 渐变，对应 .ink-bar-fill-vermilion</summary>
        Vermilion
    }

    /// <summary>
    /// 横向（或竖向）进度条。
    /// 对应 CSS <c>.ink-bar</c> + <c>.ink-bar-fill</c> 系列，
    /// 通过 <see cref="Draw"/> 自定义渲染背景槽 + 渐变填充。
    /// <see cref="Vertical"/> 为 true 时切换为竖向模式（从下往上填充）。
    /// </summary>
    public class InkBar : ContainerControl
    {
        /// <summary>进度条背景色（rgba(0,0,0,0.4)）</summary>
        private static readonly Color BarBackground = new Color(0f, 0f, 0f, 0.4f);

        /// <summary>翡翠深色（CSS #3E6B5E）</summary>
        private static readonly Color JadeDeep = new Color(62f / 255f, 107f / 255f, 94f / 255f, 1f);

        /// <summary>血色深色（CSS #8A3E3A）</summary>
        private static readonly Color BloodDeep = new Color(138f / 255f, 62f / 255f, 58f / 255f, 1f);

        /// <summary>渐变分段数（越大越平滑）</summary>
        private const int GradientSteps = 24;

        /// <summary>当前进度值（0.0~1.0）</summary>
        private float _value;

        /// <summary>填充色变体</summary>
        private InkBarFillVariant _fillVariant = InkBarFillVariant.Gold;

        /// <summary>是否为竖向进度条</summary>
        private bool _vertical;

        /// <summary>
        /// 进度值（0.0~1.0），自动钳制到有效范围。
        /// </summary>
        public float Value
        {
            get => _value;
            set => _value = Mathf.Clamp(value, 0f, 1f);
        }

        /// <summary>
        /// 填充色变体。
        /// </summary>
        public InkBarFillVariant FillVariant
        {
            get => _fillVariant;
            set => _fillVariant = value;
        }

        /// <summary>
        /// 是否为竖向进度条。true 时从下往上填充。
        /// </summary>
        public bool Vertical
        {
            get => _vertical;
            set => _vertical = value;
        }

        /// <summary>
        /// 构造函数：默认横向 8px 高、鎏金填充。
        /// </summary>
        public InkBar()
        {
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
            Height = 8f;
        }

        /// <summary>
        /// 获取当前变体对应的渐变起止色。
        /// </summary>
        /// <param name="deep">渐变起始色（深色端）</param>
        /// <param name="bright">渐变终止色（亮色端）</param>
        private void GetGradientColors(out Color deep, out Color bright)
        {
            switch (_fillVariant)
            {
                case InkBarFillVariant.Jade:
                    deep = JadeDeep;
                    bright = InkWashTheme.JadeBright;
                    break;
                case InkBarFillVariant.Blood:
                    deep = BloodDeep;
                    bright = InkWashTheme.BloodBright;
                    break;
                case InkBarFillVariant.Vermilion:
                    deep = InkWashTheme.VermilionDeep;
                    bright = InkWashTheme.VermilionBright;
                    break;
                default:
                    deep = InkWashTheme.GoldDeep;
                    bright = InkWashTheme.GoldBright;
                    break;
            }
        }

        /// <inheritdoc />
        public override void Draw()
        {
            if (!Visible || Width <= 0f || Height <= 0f)
                return;

            // 1. 绘制背景槽
            Render2D.FillRectangle(new Rectangle(0, 0, Width, Height), BarBackground);

            // 2. 绘制边框
            Render2D.DrawRectangle(
                new Rectangle(0, 0, Width, Height),
                InkWashTheme.BorderNeutralL2, 1f);

            // 3. 绘制渐变填充
            if (_value <= 0f)
                return;

            GetGradientColors(out Color deep, out Color bright);

            if (_vertical)
                DrawVerticalFill(deep, bright);
            else
                DrawHorizontalFill(deep, bright);
        }

        /// <summary>
        /// 绘制横向渐变填充（从左到右）。
        /// </summary>
        private void DrawHorizontalFill(Color deep, Color bright)
        {
            float fillWidth = Width * _value;
            if (fillWidth <= 0f)
                return;

            int stripCount = Mathf.Max(1, (int)(fillWidth / 2f));
            if (stripCount > GradientSteps)
                stripCount = GradientSteps;
            float stripWidth = fillWidth / stripCount;

            for (int i = 0; i < stripCount; i++)
            {
                float t = stripCount > 1 ? (float)i / (stripCount - 1) : 0f;
                Color c = Color.Lerp(deep, bright, t);
                Render2D.FillRectangle(
                    new Rectangle(i * stripWidth, 0, stripWidth + 1f, Height),
                    c);
            }
        }

        /// <summary>
        /// 绘制竖向渐变填充（从下到上）。
        /// </summary>
        private void DrawVerticalFill(Color deep, Color bright)
        {
            float fillHeight = Height * _value;
            if (fillHeight <= 0f)
                return;

            int stripCount = Mathf.Max(1, (int)(fillHeight / 2f));
            if (stripCount > GradientSteps)
                stripCount = GradientSteps;
            float stripHeight = fillHeight / stripCount;
            float startY = Height - fillHeight;

            for (int i = 0; i < stripCount; i++)
            {
                // 底部=deep，顶部=bright
                float t = stripCount > 1 ? (float)i / (stripCount - 1) : 0f;
                Color c = Color.Lerp(deep, bright, t);
                Render2D.FillRectangle(
                    new Rectangle(0, startY + i * stripHeight, Width, stripHeight + 1f),
                    c);
            }
        }
    }

    // =======================================================================

    /// <summary>
    /// 竖向进度条（独立类）。
    /// 对应 CSS <c>.ink-bar-v</c> + <c>.ink-bar-v-fill</c>，
    /// 默认宽度 8px，从底部向上填充，鎏金渐变。
    /// </summary>
    public class InkBarVertical : ContainerControl
    {
        /// <summary>进度条背景色</summary>
        private static readonly Color BarBackground = new Color(0f, 0f, 0f, 0.4f);

        /// <summary>渐变分段数</summary>
        private const int GradientSteps = 24;

        /// <summary>当前进度值</summary>
        private float _value;

        /// <summary>填充色变体</summary>
        private InkBarFillVariant _fillVariant = InkBarFillVariant.Gold;

        /// <summary>
        /// 进度值（0.0~1.0），自动钳制。
        /// </summary>
        public float Value
        {
            get => _value;
            set => _value = Mathf.Clamp(value, 0f, 1f);
        }

        /// <summary>
        /// 填充色变体。
        /// </summary>
        public InkBarFillVariant FillVariant
        {
            get => _fillVariant;
            set => _fillVariant = value;
        }

        /// <summary>
        /// 构造函数：默认 8px 宽、鎏金填充。
        /// </summary>
        public InkBarVertical()
        {
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
            Width = 8f;
        }

        /// <inheritdoc />
        public override void Draw()
        {
            if (!Visible || Width <= 0f || Height <= 0f)
                return;

            // 1. 背景槽
            Render2D.FillRectangle(new Rectangle(0, 0, Width, Height), BarBackground);

            // 2. 边框
            Render2D.DrawRectangle(
                new Rectangle(0, 0, Width, Height),
                InkWashTheme.BorderNeutralL2, 1f);

            // 3. 从底部向上的渐变填充
            if (_value <= 0f)
                return;

            Color deep, bright;
            switch (_fillVariant)
            {
                case InkBarFillVariant.Jade:
                    deep = new Color(62f / 255f, 107f / 255f, 94f / 255f, 1f);
                    bright = InkWashTheme.JadeBright;
                    break;
                case InkBarFillVariant.Blood:
                    deep = new Color(138f / 255f, 62f / 255f, 58f / 255f, 1f);
                    bright = InkWashTheme.BloodBright;
                    break;
                case InkBarFillVariant.Vermilion:
                    deep = InkWashTheme.VermilionDeep;
                    bright = InkWashTheme.VermilionBright;
                    break;
                default:
                    deep = InkWashTheme.GoldDeep;
                    bright = InkWashTheme.GoldBright;
                    break;
            }

            float fillHeight = Height * _value;
            int stripCount = Mathf.Max(1, (int)(fillHeight / 2f));
            if (stripCount > GradientSteps)
                stripCount = GradientSteps;
            float stripHeight = fillHeight / stripCount;
            float startY = Height - fillHeight;

            for (int i = 0; i < stripCount; i++)
            {
                float t = stripCount > 1 ? (float)i / (stripCount - 1) : 0f;
                Color c = Color.Lerp(deep, bright, t);
                Render2D.FillRectangle(
                    new Rectangle(0, startY + i * stripHeight, Width, stripHeight + 1f),
                    c);
            }
        }
    }
}

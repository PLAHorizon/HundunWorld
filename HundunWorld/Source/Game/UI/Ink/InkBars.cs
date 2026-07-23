using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.StyleSystem;

namespace HundunWorld.Game.UI.Ink
{
    /// <summary>
    /// 进度条填充色变体。
    /// 对应设计方案中的渐变填充规范。
    /// </summary>
    public enum InkBarFillVariant
    {
        /// <summary>鎏金填充 — GoldDeep → GoldBright 渐变</summary>
        Gold,

        /// <summary>春青填充 — JadePrimary → JadeBright 渐变（--ink-jade-primary → --ink-jade-bright）</summary>
        Jade,

        /// <summary>血色填充 — BloodPrimary → BloodBright 渐变（--status-error-default → hover）</summary>
        Blood,

        /// <summary>朱红填充 — VermilionDeep → VermilionBright 渐变</summary>
        Vermilion,

        /// <summary>暖金填充 — Alert → AlertHover 渐变（--status-alert-default → hover，体力条用）</summary>
        Alert
    }

    /// <summary>
    /// 横向（或竖向）进度条。
    /// 对应 CSS <c>.ink-bar</c> + <c>.ink-bar-fill</c> 系列，
    /// 通过 <see cref="Draw"/> 自定义渲染背景槽 + 渐变填充。
    /// <see cref="Vertical"/> 为 true 时切换为竖向模式（从下往上填充）。
    /// </summary>
    public class InkBar : ContainerControl
    {
        /// <summary>进度条背景色 rgba(0,0,0,0.5)（设计方案 HUD 条规范）</summary>
        private static readonly Color BarBackground = new Color(0f, 0f, 0f, 0.5f);

        /// <summary>圆角半径 2px（设计方案 HUD 条规范）</summary>
        private const float BarRadius = 2f;

        /// <summary>渐变分段数（越大越平滑）</summary>
        private const int GradientSteps = 24;

        /// <summary>过渡动画时长 400ms（ds-progress §4.6）</summary>
        private const float TransitionDuration = 0.4f;

        /// <summary>目标进度值（0.0~1.0）</summary>
        private float _value;

        /// <summary>当前显示进度（动画插值用）</summary>
        private float _displayValue;

        /// <summary>填充色变体</summary>
        private InkBarFillVariant _fillVariant = InkBarFillVariant.Gold;

        /// <summary>是否为竖向进度条</summary>
        private bool _vertical;

        /// <summary>
        /// 背景槽颜色（默认 rgba(0,0,0,0.5)；传统 HUD 可设为 Abyss 对齐 --ink-bg-abyss）。
        /// </summary>
        public Color SlotColor { get; set; } = BarBackground;

        /// <summary>
        /// 边框颜色（默认 BorderNeutralL2；传统 HUD 血条设 BorderGold、气条设 BorderJade）。
        /// 设为 <see cref="Color.Transparent"/> 可隐藏边框（如修为条）。
        /// </summary>
        public Color BorderColor { get; set; } = InkWashTheme.BorderNeutralL2;

        /// <summary>
        /// 进度值（0.0~1.0），自动钳制到有效范围，400ms 平滑过渡。
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

        /// <inheritdoc />
        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);
            // 400ms 线性插值过渡（cubic-bezier 近似）
            if (Mathf.Abs(_displayValue - _value) > 0.001f)
            {
                float step = deltaTime / TransitionDuration;
                _displayValue = Mathf.MoveTowards(_displayValue, _value, step);
            }
            else
            {
                _displayValue = _value;
            }
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
                    deep = InkWashTheme.JadePrimary;
                    bright = InkWashTheme.JadeBright;
                    break;
                case InkBarFillVariant.Blood:
                    deep = InkWashTheme.BloodPrimary;
                    bright = InkWashTheme.BloodBright;
                    break;
                case InkBarFillVariant.Vermilion:
                    deep = InkWashTheme.VermilionDeep;
                    bright = InkWashTheme.VermilionBright;
                    break;
                case InkBarFillVariant.Alert:
                    deep = InkWashTheme.Alert;
                    bright = InkWashTheme.AlertHover;
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

            var bounds = new Rectangle(0, 0, Width, Height);

            // 1. 绘制背景槽（2px 圆角）
            InkRenderHelper.FillRoundedRectangle(bounds, BarRadius, SlotColor);

            // 2. 绘制边框（2px 圆角）
            if (BorderColor.A > 0f)
                InkRenderHelper.DrawRoundedRectangle(bounds, BarRadius, BorderColor, 1f);

            // 3. 绘制渐变填充
            if (_displayValue <= 0f)
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
            float fillWidth = Width * _displayValue;
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
            float fillHeight = Height * _displayValue;
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
        /// <summary>进度条背景色 rgba(0,0,0,0.5)</summary>
        private static readonly Color BarBackground = new Color(0f, 0f, 0f, 0.5f);

        /// <summary>圆角半径 2px</summary>
        private const float BarRadius = 2f;

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

            var bounds = new Rectangle(0, 0, Width, Height);

            // 1. 背景槽（2px 圆角）
            InkRenderHelper.FillRoundedRectangle(bounds, BarRadius, BarBackground);

            // 2. 边框（2px 圆角）
            InkRenderHelper.DrawRoundedRectangle(bounds, BarRadius, InkWashTheme.BorderNeutralL2, 1f);

            // 3. 从底部向上的渐变填充
            if (_value <= 0f)
                return;

            Color deep, bright;
            switch (_fillVariant)
            {
                case InkBarFillVariant.Jade:
                    deep = InkWashTheme.JadePrimary;
                    bright = InkWashTheme.JadeBright;
                    break;
                case InkBarFillVariant.Blood:
                    deep = InkWashTheme.BloodPrimary;
                    bright = InkWashTheme.BloodBright;
                    break;
                case InkBarFillVariant.Vermilion:
                    deep = InkWashTheme.VermilionDeep;
                    bright = InkWashTheme.VermilionBright;
                    break;
                case InkBarFillVariant.Alert:
                    deep = InkWashTheme.Alert;
                    bright = InkWashTheme.AlertHover;
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

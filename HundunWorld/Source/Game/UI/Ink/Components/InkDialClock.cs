using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.StyleSystem;

namespace HundunWorld.Game.UI.Ink.Components
{
    /// <summary>
    /// 12 时辰表盘时钟控件。
    /// 绘制圆形表盘（金色边框 + 墨色背景）+ 12 刻度线 + 12 时辰文字（子-亥）+ 当前时辰指针。
    /// 指针角度 = <see cref="_currentHour"/> × 30°，从正上方（子位）开始顺时针。
    /// 通过 <see cref="SetCurrentHour"/> 设置当前时辰。
    /// </summary>
    public class InkDialClock : ContainerControl
    {
        /// <summary>12 时辰名称（索引 0-11 对应 子-亥）</summary>
        public static readonly string[] HourNames =
        {
            "子", "丑", "寅", "卯", "辰", "巳",
            "午", "未", "申", "酉", "戌", "亥"
        };

        /// <summary>时辰总数</summary>
        private const int HourCount = 12;

        /// <summary>圆形边框分段数</summary>
        private const int CircleSegments = 64;

        /// <summary>边框厚度（像素）</summary>
        private const float BorderThickness = 2f;

        /// <summary>刻度线长度（像素）</summary>
        private const float TickLength = 8f;

        /// <summary>刻度线宽度</summary>
        private const float TickThickness = 1f;

        /// <summary>时辰文字距圆心的距离因子（相对半径）</summary>
        private const float HourTextRadiusFactor = 0.78f;

        /// <summary>时辰文字绘制矩形尺寸（像素，正方形）</summary>
        private const float HourTextSize = 18f;

        /// <summary>时辰文字字号</summary>
        private const float HourTextFontSize = 12f;

        /// <summary>指针长度因子（相对半径）</summary>
        private const float PointerLengthFactor = 0.68f;

        /// <summary>指针线宽</summary>
        private const float PointerThickness = 3f;

        /// <summary>中心装饰点半径</summary>
        private const float CenterDotRadius = 3f;

        /// <summary>当前时辰索引（0-11，对应 子-亥）</summary>
        private int _currentHour = 0;

        /// <summary>
        /// 构造函数：默认尺寸 160x160，透明背景。
        /// </summary>
        public InkDialClock()
        {
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
            Size = new Float2(160f, 160f);
            AutoFocus = false;
        }

        /// <summary>
        /// 设置当前时辰。
        /// </summary>
        /// <param name="hour">时辰索引 0-11，对应 子-亥</param>
        public void SetCurrentHour(int hour)
        {
            if (hour >= 0 && hour < HourCount)
                _currentHour = hour;
        }

        /// <inheritdoc />
        public override void Draw()
        {
            if (!Visible || Width <= 0f || Height <= 0f)
                return;

            var center = new Float2(Width * 0.5f, Height * 0.5f);
            float radius = Mathf.Min(Width, Height) * 0.5f;
            if (radius <= 0f)
                return;

            // 1. 圆形背景（BaseSecondary）
            InkRenderHelper.FillCircle(center, radius, InkWashTheme.BaseSecondary);

            // 2. 金色边框（BorderGold）
            DrawCircleOutline(center, radius, InkWashTheme.BorderGold, BorderThickness);

            // 3. 12 刻度线（每 30 度，TextTertiary）
            // 第 i 个时辰角度：从正上方（-π/2）开始顺时针递增
            float tickInner = Mathf.Max(0f, radius - TickLength);
            float angleStep = Mathf.TwoPi / HourCount;
            for (int i = 0; i < HourCount; i++)
            {
                float angle = -Mathf.Pi * 0.5f + i * angleStep;
                var p1 = center + new Float2(Mathf.Cos(angle) * tickInner, Mathf.Sin(angle) * tickInner);
                var p2 = center + new Float2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);
                Render2D.DrawLine(p1, p2, InkWashTheme.TextTertiary, TickThickness);
            }

            // 4. 12 时辰文字（围绕圆周，思源宋体 12f）
            var fontRef = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, HourTextFontSize);
            var font = fontRef.GetFont();
            if (font != null)
            {
                float textRadius = radius * HourTextRadiusFactor;
                for (int i = 0; i < HourCount; i++)
                {
                    float angle = -Mathf.Pi * 0.5f + i * angleStep;
                    var textPos = new Float2(
                        center.X + Mathf.Cos(angle) * textRadius,
                        center.Y + Mathf.Sin(angle) * textRadius);
                    var textRect = new Rectangle(
                        textPos.X - HourTextSize * 0.5f,
                        textPos.Y - HourTextSize * 0.5f,
                        HourTextSize, HourTextSize);
                    Render2D.DrawText(
                        font, HourNames[i], textRect,
                        InkWashTheme.TextDefault,
                        TextAlignment.Center, TextAlignment.Center,
                        TextWrapping.NoWrap);
                }
            }

            // 5. 指针（从中心到当前时辰位置，GoldBright 粗线）
            float pointerAngle = -Mathf.Pi * 0.5f + _currentHour * angleStep;
            float pointerLength = radius * PointerLengthFactor;
            var pointerEnd = center + new Float2(
                Mathf.Cos(pointerAngle) * pointerLength,
                Mathf.Sin(pointerAngle) * pointerLength);
            Render2D.DrawLine(center, pointerEnd, InkWashTheme.GoldBright, PointerThickness);

            // 6. 中心装饰点（GoldBright）
            InkRenderHelper.FillCircle(center, CenterDotRadius, InkWashTheme.GoldBright);
        }

        /// <summary>
        /// 使用多段 <see cref="Render2D.DrawLine"/> 近似绘制圆形描边。
        /// </summary>
        /// <param name="center">圆心</param>
        /// <param name="radius">半径</param>
        /// <param name="color">描边颜色</param>
        /// <param name="thickness">线宽</param>
        private static void DrawCircleOutline(Float2 center, float radius, Color color, float thickness)
        {
            if (radius <= 0f)
                return;

            float step = Mathf.TwoPi / CircleSegments;
            for (int i = 0; i < CircleSegments; i++)
            {
                float a1 = i * step;
                float a2 = (i + 1) * step;
                var p1 = center + new Float2(Mathf.Cos(a1) * radius, Mathf.Sin(a1) * radius);
                var p2 = center + new Float2(Mathf.Cos(a2) * radius, Mathf.Sin(a2) * radius);
                Render2D.DrawLine(p1, p2, color, thickness);
            }
        }
    }
}

using System;
using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.StyleSystem;

namespace HundunWorld.Game.UI.Ink.Components
{
    /// <summary>
    /// 经脉图控件。
    /// 绘制 SVG 风格的简化人体轮廓 + 8 个可点击穴位点。
    /// 穴位点位置使用归一化坐标（0-1）存储，绘制时映射到控件实际尺寸。
    /// 点击穴位触发 <see cref="AcupointClicked"/> 事件，可通过
    /// <see cref="SetActiveAcupoint"/> 高亮指定穴位。
    /// </summary>
    public class InkMeridianDiagram : ContainerControl
    {
        /// <summary>8 个穴位名称（索引 0-7 对应穴位顺序）</summary>
        public static readonly string[] AcupointNames =
        {
            "百会", "太阳", "风池", "膻中", "神阙", "合谷", "关元", "涌泉"
        };

        /// <summary>8 个穴位的归一化坐标 (x, y)，范围 0-1（相对控件尺寸）</summary>
        private static readonly Float2[] AcupointPositions =
        {
            new Float2(0.50f, 0.08f), // 0 百会
            new Float2(0.65f, 0.12f), // 1 太阳
            new Float2(0.35f, 0.12f), // 2 风池
            new Float2(0.50f, 0.35f), // 3 膻中
            new Float2(0.50f, 0.50f), // 4 神阙
            new Float2(0.20f, 0.55f), // 5 合谷
            new Float2(0.50f, 0.62f), // 6 关元
            new Float2(0.50f, 0.95f)  // 7 涌泉
        };

        /// <summary>椭圆轮廓分段数</summary>
        private const int EllipseSegments = 32;

        /// <summary>人体轮廓线宽（像素）</summary>
        private const float BodyLineWidth = 1.5f;

        /// <summary>穴位点半径（像素）</summary>
        private const float AcupointRadius = 4f;

        /// <summary>活动穴位光晕半径（像素）</summary>
        private const float ActiveGlowRadius = 10f;

        /// <summary>点击命中半径（像素）</summary>
        private const float HitRadius = 14f;

        /// <summary>当前活动穴位索引（-1 表示无高亮）</summary>
        private int _activeAcupoint = -1;

        /// <summary>
        /// 穴位点击事件。参数为被点击的穴位索引（0-7）。
        /// </summary>
        public event Action<int> AcupointClicked;

        /// <summary>
        /// 构造函数：默认尺寸 200x400，透明背景。
        /// </summary>
        public InkMeridianDiagram()
        {
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
            Size = new Float2(200f, 400f);
            AutoFocus = false;
        }

        /// <summary>
        /// 设置活动穴位索引（高亮指定穴位）。
        /// </summary>
        /// <param name="index">穴位索引 0-7，传 -1 取消高亮</param>
        public void SetActiveAcupoint(int index)
        {
            if (index >= -1 && index < AcupointNames.Length)
                _activeAcupoint = index;
        }

        /// <inheritdoc />
        public override void Draw()
        {
            if (!Visible || Width <= 0f || Height <= 0f)
                return;

            // 1. 绘制人体轮廓
            DrawBodyOutline();

            // 2. 绘制穴位点
            for (int i = 0; i < AcupointPositions.Length; i++)
            {
                var pos = new Float2(
                    AcupointPositions[i].X * Width,
                    AcupointPositions[i].Y * Height);

                bool isActive = (i == _activeAcupoint);
                Color pointColor = isActive
                    ? InkWashTheme.GoldBright
                    : InkWashTheme.TextSecondary;

                // 活动穴位先绘制外光晕
                if (isActive)
                {
                    Color glow = new Color(pointColor.R, pointColor.G, pointColor.B, 0.35f);
                    InkRenderHelper.FillCircle(pos, ActiveGlowRadius, glow);
                }

                InkRenderHelper.FillCircle(pos, AcupointRadius, pointColor);
            }
        }

        /// <summary>
        /// 绘制简化人体轮廓（头/颈/躯干/四肢）。
        /// 颜色使用 <see cref="InkWashTheme.TextTertiary"/>，线宽 <see cref="BodyLineWidth"/>。
        /// </summary>
        private void DrawBodyOutline()
        {
            Color lineColor = InkWashTheme.TextTertiary;

            // 头部：椭圆轮廓
            var headCenter = new Float2(0.5f * Width, 0.13f * Height);
            float headRx = 0.07f * Width;
            float headRy = 0.05f * Height;
            DrawEllipseOutline(headCenter, headRx, headRy, lineColor, BodyLineWidth);

            // 颈部
            DrawBodyLine(0.5f, 0.18f, 0.5f, 0.22f, lineColor);
            // 脊柱（躯干主干）
            DrawBodyLine(0.5f, 0.22f, 0.5f, 0.58f, lineColor);
            // 肩部
            DrawBodyLine(0.38f, 0.24f, 0.62f, 0.24f, lineColor);
            // 左臂（从左肩斜向下）
            DrawBodyLine(0.38f, 0.24f, 0.28f, 0.42f, lineColor);
            // 右臂（从右肩斜向下）
            DrawBodyLine(0.62f, 0.24f, 0.72f, 0.42f, lineColor);
            // 髋部
            DrawBodyLine(0.42f, 0.58f, 0.58f, 0.58f, lineColor);
            // 左腿（从左髋向下）
            DrawBodyLine(0.45f, 0.58f, 0.42f, 0.95f, lineColor);
            // 右腿（从右髋向下）
            DrawBodyLine(0.55f, 0.58f, 0.58f, 0.95f, lineColor);
        }

        /// <summary>
        /// 按归一化坐标绘制一段人体轮廓线。
        /// </summary>
        /// <param name="x1">起点归一化 X</param>
        /// <param name="y1">起点归一化 Y</param>
        /// <param name="x2">终点归一化 X</param>
        /// <param name="y2">终点归一化 Y</param>
        /// <param name="color">线颜色</param>
        private void DrawBodyLine(float x1, float y1, float x2, float y2, Color color)
        {
            var p1 = new Float2(x1 * Width, y1 * Height);
            var p2 = new Float2(x2 * Width, y2 * Height);
            Render2D.DrawLine(p1, p2, color, BodyLineWidth);
        }

        /// <summary>
        /// 使用多段 <see cref="Render2D.DrawLine"/> 近似绘制椭圆轮廓。
        /// </summary>
        /// <param name="center">椭圆中心</param>
        /// <param name="rx">水平半径</param>
        /// <param name="ry">垂直半径</param>
        /// <param name="color">描边颜色</param>
        /// <param name="thickness">线宽</param>
        private static void DrawEllipseOutline(Float2 center, float rx, float ry, Color color, float thickness)
        {
            if (rx <= 0f || ry <= 0f)
                return;

            float step = Mathf.TwoPi / EllipseSegments;
            for (int i = 0; i < EllipseSegments; i++)
            {
                float a1 = i * step;
                float a2 = (i + 1) * step;
                var p1 = center + new Float2(Mathf.Cos(a1) * rx, Mathf.Sin(a1) * ry);
                var p2 = center + new Float2(Mathf.Cos(a2) * rx, Mathf.Sin(a2) * ry);
                Render2D.DrawLine(p1, p2, color, thickness);
            }
        }

        /// <inheritdoc />
        public override bool OnMouseDown(Float2 location, MouseButton button)
        {
            base.OnMouseDown(location, button);

            if (button != MouseButton.Left)
                return false;

            // 检测点击命中哪个穴位点（距离平方比较，避免开方）
            float hitRadiusSq = HitRadius * HitRadius;
            for (int i = 0; i < AcupointPositions.Length; i++)
            {
                var pos = new Float2(
                    AcupointPositions[i].X * Width,
                    AcupointPositions[i].Y * Height);
                float dx = location.X - pos.X;
                float dy = location.Y - pos.Y;
                if (dx * dx + dy * dy <= hitRadiusSq)
                {
                    AcupointClicked?.Invoke(i);
                    return true;
                }
            }

            return false;
        }
    }
}

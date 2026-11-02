using System.Collections.Generic;
using FlaxEngine;

namespace HundunWorld.Game.UI.Ink.Components
{
    /// <summary>
    /// 导航面包屑折线的 2D 绘制器，小地图与大地图共用同一套虚线样式。
    /// <para>
    /// 只接**已投影到控件本地像素坐标**的点列：两张地图的投影方式本就不同
    /// （小地图是以玩家为圆心的线性相对坐标，大地图是 0~1 归一化坐标），
    /// 把投影留在调用方、把虚线与裁剪收敛到这里，才不会长出第二套绘制分支。
    /// </para>
    /// </summary>
    public static class InkNavPathPainter
    {
        /// <summary>路径主线宽度（像素）。</summary>
        public const float DefaultThickness = 2f;

        /// <summary>虚线段长（像素）。</summary>
        public const float DefaultDashLength = 7f;

        /// <summary>虚线间隔（像素）。</summary>
        public const float DefaultGapLength = 5f;

        /// <summary>
        /// 在圆形区域内绘制路径（小地图）。伸出圆外的部分按圆弧裁剪，
        /// 于是路径会整齐地止于边框，而不是穿到圆外污染界面。
        /// </summary>
        public static void DrawInDisc(IReadOnlyList<Float2> points, Float2 center, float radius,
            Color color, float thickness = DefaultThickness,
            float dashLength = DefaultDashLength, float gapLength = DefaultGapLength)
        {
            if (points == null || radius <= 0f) return;

            for (int i = 1; i < points.Count; i++)
            {
                if (!ClipSegmentToDisc(points[i - 1], points[i], center, radius, out var a, out var b))
                    continue;

                DrawDashed(a, b, color, thickness, dashLength, gapLength);
            }
        }

        /// <summary>在矩形区域内绘制路径（大地图）。落在矩形外的部分按矩形裁剪。</summary>
        public static void DrawInRect(IReadOnlyList<Float2> points, Rectangle clip,
            Color color, float thickness = DefaultThickness,
            float dashLength = DefaultDashLength, float gapLength = DefaultGapLength)
        {
            if (points == null) return;

            for (int i = 1; i < points.Count; i++)
            {
                if (!ClipSegmentToRect(points[i - 1], points[i], clip, out var a, out var b))
                    continue;

                DrawDashed(a, b, color, thickness, dashLength, gapLength);
            }
        }

        /// <summary>绘制一段虚线（沿方向按 dash/gap 步进）。</summary>
        public static void DrawDashed(Float2 from, Float2 to, Color color,
            float thickness, float dashLength, float gapLength)
        {
            var dir = to - from;
            float len = dir.Length;
            if (len <= 0.01f) return;

            float dash = dashLength > 0.01f ? dashLength : len;
            float gap = gapLength > 0f ? gapLength : 0f;
            var unit = dir / len;

            float pos = 0f;
            while (pos < len)
            {
                float end = Mathf.Min(pos + dash, len);
                Render2D.DrawLine(from + unit * pos, from + unit * end, color, thickness);
                pos = end + gap;
            }
        }

        /// <summary>
        /// 线段-圆裁剪：解 |P + t·D − C|² = r² 得到落在圆内的 t 区间。
        /// </summary>
        private static bool ClipSegmentToDisc(Float2 p0, Float2 p1, Float2 center, float radius,
            out Float2 a, out Float2 b)
        {
            a = p0;
            b = p1;

            var d = p1 - p0;
            var f = p0 - center;

            float qa = Float2.Dot(d, d);
            if (qa <= 0f) return false;

            float qb = 2f * Float2.Dot(f, d);
            float qc = Float2.Dot(f, f) - radius * radius;
            float discriminant = qb * qb - 4f * qa * qc;
            if (discriminant < 0f) return false;

            float sqrt = Mathf.Sqrt(discriminant);
            float t0 = Mathf.Clamp((-qb - sqrt) / (2f * qa), 0f, 1f);
            float t1 = Mathf.Clamp((-qb + sqrt) / (2f * qa), 0f, 1f);
            if (t1 - t0 <= 0f) return false;

            a = p0 + d * t0;
            b = p0 + d * t1;
            return true;
        }

        /// <summary>
        /// 线段-矩形裁剪（Liang–Barsky）。p[i]==0 时该维平行：q[i]&lt;0 表示整段在该边外侧。
        /// </summary>
        private static bool ClipSegmentToRect(Float2 p0, Float2 p1, Rectangle clip,
            out Float2 a, out Float2 b)
        {
            a = p0;
            b = p1;

            var d = p1 - p0;

            float t0 = 0f;
            float t1 = 1f;

            for (int i = 0; i < 4; i++)
            {
                float p = i switch
                {
                    0 => -d.X,
                    1 => d.X,
                    2 => -d.Y,
                    _ => d.Y,
                };
                float q = i switch
                {
                    0 => p0.X - clip.Left,
                    1 => clip.Right - p0.X,
                    2 => p0.Y - clip.Top,
                    _ => clip.Bottom - p0.Y,
                };

                if (Mathf.Abs(p) <= 0.000001f)
                {
                    if (q < 0f) return false;
                    continue;
                }

                float r = q / p;
                if (p < 0f)
                {
                    if (r > t1) return false;
                    if (r > t0) t0 = r;
                }
                else
                {
                    if (r < t0) return false;
                    if (r < t1) t1 = r;
                }
            }

            if (t1 - t0 <= 0f) return false;

            a = p0 + d * t0;
            b = p0 + d * t1;
            return true;
        }
    }
}

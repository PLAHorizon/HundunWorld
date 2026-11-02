// PCG 插件 - 曲线细分（纯核）
// FlaxEngine PCG 插件的一部分。见 docs/PCG_PLUGIN_DESIGN.md §5.1.14。

using System;
using FlaxEngine;

namespace PCG
{
    /// <summary>
    /// 靠细分把折线的拐角磨圆——均匀参数的 Catmull-Rom。
    /// </summary>
    /// <remarks>
    /// <para>
    /// UE5 的样条每个点都带一种插值模式，所以图可以要一条曲线而不是一串
    /// 直线段。本插件的样条按设计就是折线（见 <see cref="PCGSpline"/>），而它仍然保持折线：
    /// 平滑是靠<b>细分</b>做的，所以弧长、切线、采样与曲线网格排布
    /// 继续在它们本来就认识的同一个折线类型上工作。没有一个消费者需要为此改动。
    /// </para>
    /// <para>
    /// <b>它承诺什么、不承诺什么。</b>曲线穿过每一个控制点——这正是
    /// 让它能被安全换进来的性质；而它精确复现一段直线的前提是<i>控制点等距</i>。
    /// 间距不均时，均匀 Catmull-Rom 会略微鼓到拐角外侧，那是标准行为，
    /// 也是"要精确形状就该把途经点摆均匀"这一提醒的理由。
    /// 切线不取自相邻段的长度，
    /// 所以这里不尝试做按弦长的重参数化。
    /// </para>
    /// </remarks>
    public static class PCGSplineCurves
    {
        /// <summary>每段细分数的上限，免得一个随手写的值把顶点数炸开。</summary>
        public const int MaxSubdivisions = 32;

        /// <summary>
        /// <paramref name="p1"/> 与 <paramref name="p2"/> 之间那一段上的 Catmull-Rom 位置，
        /// 以 <paramref name="p0"/> 与 <paramref name="p3"/> 为相邻的控制点。
        /// </summary>
        public static Vector3 Interpolate(in Vector3 p0, in Vector3 p1, in Vector3 p2, in Vector3 p3, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;

            var a = p1 * 2.0f;
            var b = (p2 - p0) * t;
            var c = (p0 * 2.0f - p1 * 5.0f + p2 * 4.0f - p3) * t2;
            var d = (p1 * 3.0f - p2 * 3.0f - p0 + p3) * t3;

            return (a + b + c + d) * 0.5f;
        }

        /// <summary>
        /// 在每一段里插入 <paramref name="subdivisionsPerSegment"/> - 1 个点，
        /// 得到一条穿过同样这些控制点的、更细的折线。
        /// </summary>
        /// <remarks>
        /// 开放的路径会精确保住首尾两个顶点，而在端点处使用的"邻居"就是
        /// 端点自己（通常所说的 clamp），这正是让两端不会朝侧面甩出去的原因。闭合的路径
        /// 会把邻居绕回来，于是接缝和其他拐角一样平滑。
        /// </remarks>
        public static Vector3[] Subdivide(Vector3[] points, bool closed, int subdivisionsPerSegment)
        {
            if (points == null)
                return Array.Empty<Vector3>();

            if (points.Length < 2 || subdivisionsPerSegment <= 1)
                return (Vector3[])points.Clone();

            int perSegment = Math.Min(subdivisionsPerSegment, MaxSubdivisions);
            bool loop = closed && points.Length > 2;

            int segments = loop ? points.Length : points.Length - 1;
            int resultCount = loop ? segments * perSegment : segments * perSegment + 1;
            var result = new Vector3[resultCount];

            int write = 0;

            for (int i = 0; i < segments; i++)
            {
                int next = (i + 1) % points.Length;

                var p1 = points[i];
                var p2 = points[next];

                var p0 = loop ? points[(i - 1 + points.Length) % points.Length] : points[Math.Max(0, i - 1)];
                var p3 = loop ? points[(i + 2) % points.Length] : points[Math.Min(points.Length - 1, i + 2)];

                for (int s = 0; s < perSegment; s++)
                {
                    // t = 0 精确落在 p1 上，这就是每个控制点都能存活的原因：这一段自己的
                    // 起点在任何插值点之前就被发出来。
                    result[write++] = Interpolate(p0, p1, p2, p3, (float)s / perSegment);
                }
            }

            if (!loop)
                result[write] = points[points.Length - 1];

            return result;
        }
    }
}

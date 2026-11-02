// PCG 插件 - 样条（折线）数据
// 属于 FlaxEngine PCG 插件。见 docs/PCG_PLUGIN_DESIGN.md §5.1.14。

using System;
using System.Collections.Generic;
using FlaxEngine;

namespace PCG
{
    /// <summary>
    /// 一条带弧长参数化的世界坐标折线：图在节点之间能携带的那种样条。
    /// </summary>
    /// <remarks>
    /// <para>
    /// UE5 把样条当作与点并列的一等数据类型，这一份就是插件对此的回答：一个
    /// 节点可以把点集折成一条曲线，另一个节点可以按距离沿那条曲线采样。真正在图里流动的
    /// 是一条<b>折线</b>，不是带曲率的样条——曲线那一档活在引擎的 Actor 样条里，
    /// 而基于 Actor 的采样器已经会去读它；在这里再插值曲线等于把引擎的切线
    /// 实现重做一遍。把点折成折线，正是"从点创建样条"这件事做的。
    /// </para>
    /// <para>
    /// 这里全是向量上的普通算术，这是刻意的：距离、切线、均匀采样
    /// 都不用场景就能断言，所以一张基于样条的图的行为能在离屏
    /// 钉住，而不是只能在编辑器里目视。
    /// </para>
    /// </remarks>
    public sealed class PCGSpline
    {
        private const float Epsilon = 1e-6f;

        /// <summary>顶点：世界坐标，按路径先后排。</summary>
        public readonly Vector3[] Points;

        /// <summary>最后一个顶点连回第一个时为真。</summary>
        public readonly bool IsClosed;

        /// <summary>总弧长，闭合折线要把收尾那一段算进去。</summary>
        public float Length { get; }

        private readonly float[] _cumulative;

        /// <summary>用给定的顶点造一条折线，并接管这个数组的所有权。</summary>
        /// <param name="points">按路径先后排的顶点。</param>
        /// <param name="closed">路径是否首尾相接。</param>
        public PCGSpline(Vector3[] points, bool closed)
        {
            Points = points ?? Array.Empty<Vector3>();
            IsClosed = closed && Points.Length > 2;

            _cumulative = new float[Points.Length];
            float running = 0.0f;

            for (int i = 0; i < Points.Length; i++)
            {
                _cumulative[i] = running;

                if (i + 1 < Points.Length)
                    running += Distance(i, i + 1);
            }

            if (IsClosed)
                running += Distance(Points.Length - 1, 0);

            Length = running;
        }

        /// <summary>折线有可用长度、能被采样时为真。</summary>
        public bool IsUsable => Points.Length >= 2 && Length > Epsilon;

        /// <summary>沿路径走这么远的位置：开放路径钳到两端，闭合路径则绕回。</summary>
        public Vector3 PositionAt(float distance)
        {
            if (Points.Length == 0)
                return Vector3.Zero;

            if (Points.Length == 1 || Length <= Epsilon)
                return Points[0];

            distance = Wrap(distance);
            int segment = SegmentAt(distance, out float t);
            var from = Points[segment];
            var to = Points[(segment + 1) % Points.Length];

            return from + (to - from) * t;
        }

        /// <summary>路径在该距离处的单位方向；那里没有方向时给正前方。</summary>
        public Vector3 TangentAt(float distance)
        {
            if (Points.Length < 2)
                return Vector3.Forward;

            distance = Wrap(distance);
            int segment = SegmentAt(distance, out _);
            var direction = Points[(segment + 1) % Points.Length] - Points[segment];
            float length = direction.Length;

            // 两个重合的顶点是图把一个点重复了一遍，不是一个方向；答"正前方"是为了
            // 让调用方的旋转保持有限，而不是交给他一个 NaN。
            return length > Epsilon ? direction / length : Vector3.Forward;
        }

        /// <summary>
        /// 沿路径每隔 <paramref name="spacing"/> 给一个位置，开放路径的两端一定都在。
        /// </summary>
        /// <remarks>
        /// 终点即使不是间距的整数倍也要收进来：一条差一步没到终点的折线，
        /// 读起来像图里出了 bug，而不像一种间距选择。闭合路径不需要这份
        /// 客气——它的终点就是它的起点。
        /// </remarks>
        public List<Vector3> SampleBySpacing(float spacing)
        {
            var samples = new List<Vector3>();

            if (!IsUsable || spacing <= Epsilon)
                return samples;

            for (float distance = 0.0f; distance < Length - Epsilon; distance += spacing)
                samples.Add(PositionAt(distance));

            if (!IsClosed)
                samples.Add(PositionAt(Length));

            return samples;
        }

        /// <summary>把固定个数的样本按弧长均匀摊开。</summary>
        public List<Vector3> SampleByCount(int count)
        {
            var samples = new List<Vector3>();

            if (!IsUsable || count <= 0)
                return samples;

            if (count == 1)
            {
                samples.Add(PositionAt(0.0f));
                return samples;
            }

            // 闭合路径的最后一个样本会正好压在第一个上，所以它少走一步。
            float step = IsClosed ? Length / count : Length / (count - 1);

            for (int i = 0; i < count; i++)
                samples.Add(PositionAt(step * i));

            return samples;
        }

        private float Wrap(float distance)
        {
            if (IsClosed && Length > Epsilon)
            {
                distance %= Length;

                if (distance < 0.0f)
                    distance += Length;
            }

            return Math.Min(Math.Max(distance, 0.0f), Length);
        }

        private int SegmentAt(float distance, out float t)
        {
            int last = IsClosed ? Points.Length : Points.Length - 1;

            for (int i = 0; i < last; i++)
            {
                int next = (i + 1) % Points.Length;
                float start = _cumulative[i];
                float end = i + 1 < Points.Length ? _cumulative[i + 1] : Length;

                if (distance > end && i + 1 < last)
                    continue;

                float span = end - start;
                t = span > Epsilon ? (distance - start) / span : 0.0f;

                return i;
            }

            t = 1.0f;

            return Math.Max(0, Points.Length - 2);
        }

        private float Distance(int a, int b) => (Points[b] - Points[a]).Length;
    }
}

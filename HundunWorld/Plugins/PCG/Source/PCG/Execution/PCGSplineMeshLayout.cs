// PCG 插件 - 曲线网格排布
// FlaxEngine PCG 插件的一部分。见 docs/PCG_PLUGIN_DESIGN.md §5.1.16。

using System;
using System.Collections.Generic;
using FlaxEngine;

namespace PCG.Execution
{
    /// <summary>
    /// 沿曲线摆放的一份网格：它坐在哪儿、朝哪一面、有多长。
    /// </summary>
    public readonly struct PCGSplineSegment
    {
        /// <summary>这一件的中心，落在曲线上。</summary>
        public readonly Vector3 Position;

        /// <summary>朝向，前进轴沿着曲线的切线。</summary>
        public readonly Quaternion Rotation;

        /// <summary>要施加到单位尺寸网格上的缩放，让它正好铺满这一件。</summary>
        public readonly Float3 Scale;

        /// <summary>这一件覆盖的弧长，施加缩放之前的值。</summary>
        public readonly float Length;

        internal PCGSplineSegment(Vector3 position, Quaternion rotation, Float3 scale, float length)
        {
            Position = position;
            Rotation = rotation;
            Scale = scale;
            Length = length;
        }

        /// <inheritdoc />
        public override string ToString() => $"{Position} len {Length:0.##}";
    }

    /// <summary>
    /// 把一条曲线切成单件网格能覆盖的若干段——栅栏、管道、路缘石就是由这些段做成的。
    /// </summary>
    /// <remarks>
    /// <para>
    /// UE5 有一个沿样条拉伸一份网格的曲线网格生成器；本插件对应的这份 Flax 构建
    /// 没有可驱动的曲线网格 actor 类型，所以同一结果反过来做：把曲线按选定的长度
    /// 分成若干段，每段摆一份网格、各自转向沿切线。
    /// 对栅栏或一排柱子来说这是同一幅画面，而且它不需要一个未必存在的引擎类型。
    /// </para>
    /// <para>
    /// 请求的长度是上限而不是固定件长：曲线被分成件数最少、又能装进该上限的等长段。
    /// 一条二十单位的线要求六单位一件的栅栏，会得到四件五单位的，
    /// 而不是三件六单位再加一截零头，因为一排以明显更矮的柱子收尾，
    /// 是设计者第一个就会注意到的东西。
    /// </para>
    /// </remarks>
    public static class PCGSplineMeshLayout
    {
        /// <summary>单件最长长度，这样一千单位的曲线配一单位的网格不会索要一千个 actor。</summary>
        public const int MaxSegments = 4096;

        /// <summary>
        /// 把每条曲线分成至多 <paramref name="segmentLength"/> 长的等长段，按曲线顺序。
        /// </summary>
        /// <param name="splines">要排布的曲线；null 项与不可用的曲线被跳过。</param>
        /// <param name="segmentLength">最长件，世界单位。用更短的件来保持各件等长。</param>
        /// <param name="thickness">沿曲线横向的缩放，也就是网格自身的 Y 与 Z。</param>
        /// <param name="into">接收这些段；调用前会先清空。</param>
        /// <returns>写出了多少件。</returns>
        public static int Layout(
            IReadOnlyList<PCGSpline> splines, float segmentLength, float thickness, List<PCGSplineSegment> into)
        {
            if (splines == null)
                throw new ArgumentNullException(nameof(splines));
            if (into == null)
                throw new ArgumentNullException(nameof(into));

            into.Clear();

            if (segmentLength <= 0.0f)
                return 0;

            float width = Math.Max(0.0f, thickness);

            for (int s = 0; s < splines.Count; s++)
            {
                var spline = splines[s];

                if (spline == null || !spline.IsUsable)
                    continue;

                int pieces = (int)MathF.Ceiling(spline.Length / segmentLength);
                if (pieces <= 0)
                    continue;

                // 等长且不超过请求长度：件数更少、单件更长，看上去像真正建起来的一排，
                // 而固定长度加一截短零头看上去像出了错。
                float step = spline.Length / pieces;

                for (int i = 0; i < pieces; i++)
                {
                    if (into.Count >= MaxSegments)
                        return into.Count;

                    // 由这一件的中点决定它坐在哪儿、朝哪一面：于是拐角处的那一件
                    // 跟着拐角转，而不是吸附到它起始的那一端。
                    float middle = (i + 0.5f) * step;
                    var tangent = spline.TangentAt(middle);

                    into.Add(new PCGSplineSegment(
                        spline.PositionAt(middle),
                        RotationFor(tangent),
                        new Float3(step, width, width),
                        step));
                }
            }

            return into.Count;
        }

        /// <summary>
        /// 一件的朝向，并给 <c>LookRotation</c> 答不上来的那种情形准备一个备用上方向。
        /// </summary>
        /// <remarks>
        /// 一条爬悬崖的曲线，它的切线与世界上方平行，而前进轴与上方平行的 <c>Quaternion.LookRotation</c>
        /// 是退化的——Flax 返回的四元数甚至没有归一化，于是一段竖直的栅栏
        /// 出来带着 (0, 0.5, 0.5) 的缠绕。把上方向提示换成与它垂直的东西，
        /// 就让这一件的前进轴留在切线上，而那正是要紧的部分。
        /// </remarks>
        private static Quaternion RotationFor(in Vector3 tangent)
        {
            var direction = tangent.LengthSquared > 1e-8f ? Vector3.Normalize(tangent) : Vector3.Forward;
            var up = Math.Abs(Vector3.Dot(direction, Vector3.Up)) > 0.999f ? Vector3.Forward : Vector3.Up;

            return Quaternion.LookRotation(direction, up);
        }
    }
}

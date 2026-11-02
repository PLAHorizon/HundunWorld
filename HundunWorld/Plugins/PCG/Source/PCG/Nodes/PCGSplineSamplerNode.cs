// PCG 插件 - 曲线采样节点
// FlaxEngine PCG 插件的一部分。见 docs/PCG_PLUGIN_DESIGN.md §5.1.1。

using System;
using FlaxEngine;
using PCG.Execution;

namespace PCG.Nodes
{
    /// <summary>曲线采样器沿自己的曲线定点的方式。</summary>
    public enum PCGSplineSampleMode
    {
        /// <summary>每 <c>Spacing</c> 世界单位的弧长放一个点。</summary>
        BySpacing = 0,

        /// <summary>固定数量的点，均匀铺在整段弧长上。</summary>
        ByCount = 1,
    }

    /// <summary>
    /// 沿一个 <see cref="Spline"/> actor 放置点。
    /// </summary>
    /// <remarks>
    /// <para>
    /// knot 参数不是弧长：参数上相距一个单位的两个 knot，在世界里可能是一米也可能是
    /// 一公里，所以按参数采样会长出一团一疏。本节点用 <see cref="Spline.GetSplineSegmentLength"/>
    /// 建出一张累计段长表并把它反解，这是得到均匀铺法的
    /// 唯一办法。
    /// </para>
    /// <para>
    /// 这张表覆盖 knot 到 knot。闭环样条首尾相接那一段不采样：API 没有定义
    /// 最后一个 knot 之后的 knot 时间，硬造一个会让产出的位置随着样条被编辑而漂移。
    /// 若要在接缝处有一个点，就把最后一个 knot 摞到第一个上面。
    /// </para>
    /// </remarks>
    public sealed class PCGSplineSamplerNode : PCGNodeBase
    {
        /// <summary>稳定的类型 id。</summary>
        public const string Id = "SplineSampler";

        // 参数下标。顺序属于存档图格式，只能追加。
        private const int PSplineActorId = 0;
        private const int PSplineName = 1;
        private const int PMode = 2;
        private const int PSpacing = 3;
        private const int PPointCount = 4;
        private const int PArcJitter = 5;
        private const int PAlignToTangent = 6;
        private const int PSideOffsetMin = 7;
        private const int PSideOffsetMax = 8;
        private const int PHeightOffset = 9;
        private const int PScale = 10;
        private const int PSeedOffset = 11;

        /// <summary>本节点读取的参数个数。</summary>
        public const int ParameterCount = 12;

        /// <summary>产出点数的硬上限，于是一根长曲线上一个极小的间距不会无限分配下去。</summary>
        public const int MaxPoints = 100000;

        private readonly string _typeId;
        private readonly PCGNodeParameters _params;

        /// <summary>要采样的曲线 actor 的 id。</summary>
        public Guid SplineActorId;

        /// <summary>id 解析不到时使用的 actor 名字。</summary>
        public string SplineName;

        /// <summary>定点方式。</summary>
        public PCGSplineSampleMode Mode = PCGSplineSampleMode.BySpacing;

        /// <summary>点与点之间的弧长，世界单位；<see cref="PCGSplineSampleMode.BySpacing"/> 使用。</summary>
        public float Spacing = 50.0f;

        /// <summary>点的数量；<see cref="PCGSplineSampleMode.ByCount"/> 使用。</summary>
        public int PointCount = 100;

        /// <summary>一个点沿曲线可以离开它那一格多远，按步长的分数计。</summary>
        public float ArcJitter;

        /// <summary>设置时，每个点的旋转朝着曲线的切线方向。</summary>
        public bool AlignToTangent = true;

        /// <summary>下边界沿曲线的右向量横向偏移。</summary>
        public float SideOffsetMin;

        /// <summary>上边界沿曲线的右向量横向偏移。</summary>
        public float SideOffsetMax;

        /// <summary>沿世界上方轴的偏移。</summary>
        public float HeightOffset;

        /// <summary>作用到产出点上的统一缩放。</summary>
        public float Scale = 1.0f;

        /// <summary>叠加到图的 seed 上，让同一根曲线上的两个采样器不会抖成一样。</summary>
        public int SeedOffset;

        /// <summary>由序列化的参数创建节点。</summary>
        public PCGSplineSamplerNode(PCGNodeRegistry.PCGNodeSerializationData data)
        {
            _typeId = Id;
            _params = new PCGNodeParameters(data.Values, data.TypeId);

            SplineActorId = _params.GetAssetId(PSplineActorId);
            SplineName = _params.Get(PSplineName, (string)null);
            Mode = _params.Get(PMode, PCGSplineSampleMode.BySpacing);
            Spacing = Math.Max(0.01f, _params.Get(PSpacing, 50.0f));
            PointCount = Math.Max(0, _params.Get(PPointCount, 100));
            ArcJitter = Math.Min(1.0f, Math.Max(0.0f, _params.Get(PArcJitter, 0.0f)));
            AlignToTangent = _params.Get(PAlignToTangent, true);
            SideOffsetMin = _params.Get(PSideOffsetMin, 0.0f);
            SideOffsetMax = Math.Max(SideOffsetMin, _params.Get(PSideOffsetMax, SideOffsetMin));
            HeightOffset = _params.Get(PHeightOffset, 0.0f);
            Scale = Math.Max(0.0001f, _params.Get(PScale, 1.0f));
            SeedOffset = _params.Get(PSeedOffset, 0);
        }

        /// <inheritdoc />
        public override string TypeId => _typeId;


        /// <inheritdoc />
        public override string DisplayName => "Spline Sampler";

        /// <inheritdoc />
        /// <remarks>
        /// 引擎 actor 的查询（knot 长度、变换）必须留在主线程，所以本节点
        /// 不是并行安全的；见 docs/PCG_PLUGIN_DESIGN.md §6.3 注 12。
        /// </remarks>
        public override bool IsThreadSafe => false;

        /// <inheritdoc />
        public override PCGPinDefinition[] InputPins => Array.Empty<PCGPinDefinition>();

        /// <inheritdoc />
        public override PCGPinDefinition[] OutputPins => new[]
        {
            new PCGPinDefinition("Out", PCGPinDirection.Output, tooltip: "Points along the curve."),
        };

        /// <inheritdoc />
        public override string DescribeParameters() =>
            $"spline={(SplineActorId != Guid.Empty ? SplineActorId.ToString("N") : SplineName)}, mode={Mode}, " +
            $"spacing={Spacing:F1}, count={PointCount}, align={AlignToTangent}";

        /// <inheritdoc />
        public override PCGData OnExecute(PCGExecutionContext context, PCGData[] inputs)
        {
            LastError = null;

            var spline = ResolveSpline(context);
            if (spline == null)
            {
                LastError = "No spline actor resolved from the node's id or name.";
                Debug.LogWarning($"[PCG] {Id}: {LastError}");
                return PCGData.Empty;
            }

            int knots = spline.SplinePointsCount;
            if (knots < 2)
            {
                LastError = $"Spline '{spline.Name}' has {knots} knot(s); at least 2 are required.";
                Debug.LogWarning($"[PCG] {Id}: {LastError}");
                return PCGData.Empty;
            }

            int segments = knots - 1;

            // 每段起点处的弧长。**下标从 1 起**：引擎 `Spline::GetSplineSegmentLength` 的约定是
            // `index == 0` 恒返回 0、第 i 段的长度记在**终止 knot** 的下标 i 上（实现里读 Curve[i-1]/Curve[i]），
            // 有效下标是 1..knots-1。按 0..knots-2 累加会整整少一段——一条 1000 单位的路按 100 间距
            // 本该摆出 11 个点，实测只有 5，而且每个点的参数还被映射到错开一段的位置上。
            var startLength = CumulativeLengths(segments, i => spline.GetSplineSegmentLength(i), out float total);

            if (total <= 0.0f)
            {
                LastError = "Spline has zero arc length.";
                Debug.LogWarning($"[PCG] {Id}: {LastError}");
                return PCGData.Empty;
            }

            // 段内弧长与参数不成比例：结点切线为零时曲线在一段的两头"走得慢"，只按段长做线性反解
            // 会让点往段头挤——一条 1000 单位长的直线样条，前两点会相距 52 而不是 100
            // （实测，自检抓到的就是这个）。细采一张参数→弧长表，反解便与曲线形状无关，
            // 而段的两端仍按引擎给的段长严格对齐。
            BuildArcTable(spline, startLength, segments, total,
                out float[] arcTimes, out float[] arcDistances, out int arcStride);

            int count = Mode == PCGSplineSampleMode.BySpacing
                ? (int)MathF.Floor(total / Spacing) + 1
                : PointCount;

            count = Math.Min(count, MaxPoints);
            if (count <= 0)
                return PCGData.Empty;

            float step = Mode == PCGSplineSampleMode.BySpacing ? Spacing : total / Math.Max(1, count - 1);
            float cellStep = Math.Max(0.01f, step);

            var points = new PCGPoint[count];
            int written = 0;

            for (int i = 0; i < count; i++)
            {
                context.ThrowIfCancelled(i);

                var rng = new PCGDeterministicRng(context.Seed + SeedOffset, context.CurrentNodeId, i);

                float distanceAlong = i * step;
                distanceAlong += rng.NextRange(-0.5f, 0.5f) * step * ArcJitter;
                distanceAlong = Math.Min(Math.Max(distanceAlong, 0.0f), total);

                float t = TimeAt(arcTimes, arcDistances, arcStride, segments, startLength, total, distanceAlong);

                var transform = spline.GetSplineTransform(t);
                var tangent = spline.GetSplineDirection(t);

                float side = rng.NextRange(SideOffsetMin, SideOffsetMax);
                var right = Vector3.Normalize(Vector3.Cross(Vector3.Up, tangent));
                if (right.LengthSquared < 1e-6f)
                {
                    // 切线笔直朝上，于是没法从它构造出横向的向量；改取
                    // knot 自己的右轴，那也正是作者编辑这条曲线时看到的朝向。
                    right = Vector3.Transform(Vector3.Right, transform.Orientation);
                }

                var position = transform.Translation + right * side + Vector3.Up * HeightOffset;

                var point = PCGPoint.Create(position, 1.0f, i + SeedOffset);
                point.Scale = new Float3(Scale);

                if (AlignToTangent && tangent.LengthSquared > 1e-6f)
                {
                    point.Rotation = Quaternion.LookRotation(
                        new Float3(tangent.X, tangent.Y, tangent.Z),
                        new Float3(Vector3.Up.X, Vector3.Up.Y, Vector3.Up.Z));
                }
                else
                {
                    point.Rotation = transform.Orientation;
                }

                point.UpdateBounds(new Vector3(cellStep * 0.5f, 0.0f, cellStep * 0.5f));
                points[written++] = point;
            }

            if (written < points.Length)
                Array.Resize(ref points, written);

            return new PCGData(points);
        }

        /// <summary>
        /// 按引擎的下标约定把每段长度累加成"每段起点处的弧长"与总长；纯函数，进离屏门。
        /// </summary>
        /// <param name="segmentCount">段数 = knots - 1。</param>
        /// <param name="lengthOf">按**引擎下标**取段长（0 号恒为 0，第 i 段记在终止 knot 的 i 上）。</param>
        /// <param name="total">总弧长。</param>
        /// <remarks>
        /// 单独拎出来是为了让离屏断言能直接量这条约定：引擎那个"0 号返回 0、段长记在终止 knot 上"的口径
        /// 在 C# 侧看不出来，照直觉写 0..knots-2 就会漏掉最后一段，而症状是"路只摆了一半"——
        /// 屏幕上看不出是漏了哪一段，只知道东西少。
        /// </remarks>
        public static float[] CumulativeLengths(int segmentCount, Func<int, float> lengthOf, out float total)
        {
            int count = Math.Max(1, segmentCount);
            var startLength = new float[count];
            total = 0.0f;

            for (int segment = 1; segment <= segmentCount; segment++)
            {
                startLength[segment - 1] = total;
                total += Math.Max(0.0f, lengthOf(segment));
            }

            return startLength;
        }

        private Spline ResolveSpline(PCGExecutionContext context)
        {
            var world = context?.World;
            if (world == null)
                return null;

            if (SplineActorId != Guid.Empty && world.ResolveActor(SplineActorId) is Spline byId)
                return byId;

            if (!string.IsNullOrEmpty(SplineName) && world.ResolveActor(SplineName) is Spline byName)
                return byName;

            return null;
        }

        /// <summary>每段在参数上的细采样数。一段 32 个采样把段内弧长与参数的关系量到千分位以内。</summary>
        private const int ArcSamplesPerSegment = 32;

        /// <summary>
        /// 采一张"参数 ↔ 弧长"表：逐段在参数上细采样、量出相邻采样点之间的**真实**距离，
        /// 再按引擎给出的段长把每段归一。
        /// </summary>
        /// <remarks>
        /// 引擎只暴露"每段多长"（见 <see cref="CumulativeLengths"/>），段内的弧长与参数并不成比例：
        /// 结点切线为零时，一段的两头"走得慢"、中间快，于是只按段长做线性反解会让点往段头挤。
        /// 细采样量的是曲线自己走的距离，所以反解结果与曲线形状无关；而按段长归一让段两端
        /// 与结点严格对齐（弦长略短于弧长，不归一就会累积误差）。
        /// </remarks>
        private static void BuildArcTable(
            Spline spline, float[] startLength, int segments, float total,
            out float[] times, out float[] distances, out int stride)
        {
            stride = ArcSamplesPerSegment + 1;
            times = new float[segments * stride];
            distances = new float[segments * stride];

            for (int s = 0; s < segments; s++)
            {
                float tStart = spline.GetSplineTime(s);
                float tEnd = spline.GetSplineTime(s + 1);
                float segmentLength = (s + 1 < segments ? startLength[s + 1] : total) - startLength[s];
                int b = s * stride;

                var previous = spline.GetSplineTransform(tStart).Translation;
                times[b] = tStart;
                distances[b] = 0.0f;

                float travelled = 0.0f;

                for (int k = 1; k <= ArcSamplesPerSegment; k++)
                {
                    float t = tStart + (tEnd - tStart) * ((float)k / ArcSamplesPerSegment);
                    var position = spline.GetSplineTransform(t).Translation;

                    travelled += (position - previous).Length;
                    previous = position;

                    times[b + k] = t;
                    distances[b + k] = travelled;
                }

                if (travelled > 1e-6f)
                {
                    float scale = segmentLength / travelled;
                    for (int k = 1; k <= ArcSamplesPerSegment; k++)
                        distances[b + k] *= scale;
                }
                else
                {
                    // 这一段缩成了一点（零长，或数值上量不出长度）：把距离摊平，反解时落到段起点。
                    for (int k = 1; k <= ArcSamplesPerSegment; k++)
                        distances[b + k] = segmentLength * ((float)k / ArcSamplesPerSegment);
                }
            }
        }

        /// <summary>
        /// 在细采表上反解：给弧长求参数。纯函数，进离屏门（只在表上做算术，不碰引擎）。
        /// </summary>
        /// <remarks>
        /// 先在段一级定位（段边界用的是引擎段长，所以结点处严格对齐），再在段内的采样点上线性插值。
        /// 采样足够密，段内那点残余误差是千分位量级，而引擎 API 也不暴露更细的弧长。
        /// 单独拎出来是为了让离屏断言能直接量它：段内弧长与参数不成比例时，这条反解必须仍然等距——
        /// 旧实现按段长线性映射，在零切线的曲线上第一个点间距会缩到 52（按 100 沿弧长摆件时）。
        /// </remarks>
        internal static float TimeAt(
            float[] times, float[] distances, int stride, int segments,
            float[] startLength, float total, float distanceAlong)
        {
            if (distanceAlong <= 0.0f)
                return times[0];

            if (distanceAlong >= total)
                return times[times.Length - 1];

            for (int s = 0; s < segments; s++)
            {
                if (s + 1 < segments && distanceAlong > startLength[s + 1])
                    continue;

                int b = s * stride;
                float local = distanceAlong - startLength[s];
                if (local < 0.0f)
                    local = 0.0f;

                for (int k = 0; k < stride - 1; k++)
                {
                    float d0 = distances[b + k];
                    float d1 = distances[b + k + 1];

                    if (local <= d1 || k == stride - 2)
                    {
                        float span = d1 - d0;
                        float fraction = span > 1e-6f ? (local - d0) / span : 0.0f;

                        if (fraction < 0.0f)
                            fraction = 0.0f;
                        else if (fraction > 1.0f)
                            fraction = 1.0f;

                        float t0 = times[b + k];
                        return t0 + (times[b + k + 1] - t0) * fraction;
                    }
                }

                return times[b + stride - 1];
            }

            return times[times.Length - 1];
        }
    }
}

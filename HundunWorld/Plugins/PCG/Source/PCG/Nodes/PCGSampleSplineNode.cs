// PCG 插件 - 采样样条（样条 → 点）
// 属于 FlaxEngine PCG 插件。见 docs/PCG_PLUGIN_DESIGN.md §5.1.14。

using System;
using FlaxEngine;
using PCG.Execution;

namespace PCG.Nodes
{
    /// <summary>
    /// 沿数据集携带的曲线布点——基于 Actor 的样条采样器的纯数据版孪生。
    /// </summary>
    /// <remarks>
    /// <para>
    /// Actor 采样器从场景里读曲线；这一个读的是上游 <see cref="PCGCreateSplineNode"/>
    /// 产出的东西，这才让“路”成为图的数据而不是它场景的一部分。两者
    /// 都存在，是因为它们回答不同的问题：“沿设计师画的那条路”与“沿这张图
    /// 刚从一片散点推出来的那条路”。
    /// </para>
    /// <para>
    /// 两种采样模式都在这儿，理由与 Actor 采样器里一样：按间距是树篱或
    /// 栅栏要的，按个数是“这条路上十二盏灯”要的。
    /// </para>
    /// </remarks>
    public sealed class PCGSampleSplineNode : PCGNodeBase
    {
        /// <summary>稳定的类型 id。</summary>
        public const string Id = "SampleSpline";

        // 参数下标。顺序是已存图格式的一部分；只许追加。
        private const int PMode = 0;
        private const int PSpacing = 1;
        private const int PCount = 2;
        private const int PArcJitter = 3;
        private const int PAlignToTangent = 4;
        private const int PHeightOffset = 5;
        private const int PScale = 6;
        private const int PSeedOffset = 7;

        /// <summary>本节点读取的参数个数。</summary>
        public const int ParameterCount = 8;

        /// <summary>产出点数上限，与 Actor 采样器的一致。</summary>
        public const int MaxPoints = 100000;

        /// <summary>由哪个旋钮决定一条曲线得几个点。</summary>
        public enum SampleMode
        {
            BySpacing = 0,
            ByCount = 1,
        }

        private readonly string _typeId;
        private readonly PCGNodeParameters _params;

        /// <summary>由间距还是个数决定。</summary>
        public SampleMode Mode = SampleMode.BySpacing;

        /// <summary>间距模式下点与点之间的弧长。</summary>
        public float Spacing = 200.0f;

        /// <summary>个数模式下每条曲线的点数。</summary>
        public int Count = 10;

        /// <summary>沿曲线的随机偏移（世界单位），逐点施加。</summary>
        public float ArcJitter;

        /// <summary>打开时每个点朝向沿曲线的方向。</summary>
        public bool AlignToTangent = true;

        /// <summary>把点从曲线上抬起，给“坐在它上面”的道具用。</summary>
        public float HeightOffset;

        /// <summary>产出点的等比例缩放。</summary>
        public float Scale = 1.0f;

        /// <summary>把同一张图里的两个样条采样器区分开。</summary>
        public int SeedOffset;

        /// <summary>用反序列化出来的参数建节点。</summary>
        public PCGSampleSplineNode(PCGNodeRegistry.PCGNodeSerializationData data)
        {
            _typeId = Id;
            _params = new PCGNodeParameters(data.Values, data.TypeId);

            int mode = _params.Get(PMode, (int)SampleMode.BySpacing);
            Mode = (SampleMode)Math.Min((int)SampleMode.ByCount, Math.Max(0, mode));
            Spacing = Math.Max(0.0f, _params.Get(PSpacing, 200.0f));
            Count = Math.Max(0, _params.Get(PCount, 10));
            ArcJitter = Math.Max(0.0f, _params.Get(PArcJitter, 0.0f));
            AlignToTangent = _params.Get(PAlignToTangent, true);
            HeightOffset = _params.Get(PHeightOffset, 0.0f);
            Scale = Math.Max(0.0f, _params.Get(PScale, 1.0f));
            SeedOffset = _params.Get(PSeedOffset, 0);
        }

        /// <inheritdoc />
        public override string TypeId => _typeId;

        /// <inheritdoc />
        public override string DisplayName => "Sample Spline";

        /// <inheritdoc />
        public override bool IsThreadSafe => true;

        /// <inheritdoc />
        public override PCGPinDefinition[] InputPins => new[]
        {
            new PCGPinDefinition("In", PCGPinDirection.Input, isRequired: true,
                tooltip: "Data carrying the curves to sample."),
        };

        /// <inheritdoc />
        public override PCGPinDefinition[] OutputPins => new[]
        {
            new PCGPinDefinition("Out", PCGPinDirection.Output,
                tooltip: "Points placed along every curve, which pass through as well."),
        };

        /// <inheritdoc />
        public override string DescribeParameters()
        {
            string how = Mode == SampleMode.BySpacing
                ? $"spacing={Spacing:F1}"
                : $"count={Count}";

            return $"{how}, jitter={ArcJitter:F1}, align={AlignToTangent}, height={HeightOffset:F1}";
        }

        /// <inheritdoc />
        public override PCGData OnExecute(PCGExecutionContext context, PCGData[] inputs)
        {
            var input = inputs[0];
            if (input == null)
                return PCGData.Empty;

            var splines = input.Splines;

            if (splines == null || splines.Length == 0)
            {
                LastError = "Sample Spline found no curves on its input. Wire a Create Spline node in front of " +
                            "it, or use the actor-based sampler for a curve that lives in the scene. " +
                            "The layer passes through unchanged.";
                return input.Clone();
            }

            if (Mode == SampleMode.BySpacing && Spacing <= 0.0f)
            {
                LastError = "Sample Spline needs a positive spacing in spacing mode; the layer passes through.";
                return input.Clone();
            }

            // 每个采样点记住自己来自哪条曲线：输入有多条曲线时，切线必须
            // 来自它自己那条，而不是碰巧排在最前的那条。
            var samples = new System.Collections.Generic.List<(PCGSpline Curve, Vector3 Position)>();

            for (int s = 0; s < splines.Length; s++)
            {
                var spline = splines[s];
                if (spline == null || !spline.IsUsable)
                    continue;

                var positions = Mode == SampleMode.BySpacing
                    ? spline.SampleBySpacing(Spacing)
                    : spline.SampleByCount(Count);

                for (int i = 0; i < positions.Count; i++)
                {
                    if (samples.Count >= MaxPoints)
                    {
                        LastError = $"Sample Spline stopped at its ceiling of {MaxPoints} points.";
                        break;
                    }

                    samples.Add((spline, ApplyJitter(spline, positions[i], context, s * 100000 + i)));
                }

                if (samples.Count >= MaxPoints)
                    break;
            }

            if (samples.Count == 0)
            {
                LastError = "Sample Spline produced no points: every curve on its input was degenerate.";
                return input.Clone();
            }

            var result = PCGData.Create(samples.Count);
            int seed = context.Seed + SeedOffset;
            int nodeId = context.CurrentNodeId;

            for (int i = 0; i < samples.Count; i++)
            {
                context.ThrowIfCancelled(i);

                var rng = new PCGDeterministicRng(seed, nodeId, i);
                var point = PCGPoint.Create(samples[i].Position, 1.0f, i);
                point.Scale = new Float3(Scale);

                if (AlignToTangent)
                {
                    var direction = TangentNear(samples[i].Curve, samples[i].Position);
                    point.Rotation = Quaternion.LookRotation(direction, Vector3.Up);
                }
                else if (rng.NextBool(0.5f))
                {
                    point.Rotation = rng.NextRotation();
                }

                point.UpdateBounds(new Vector3(50.0f, 50.0f, 50.0f));
                result.Points[i] = point;
            }

            // 曲线跟着点一起传下去：后面的节点可以用不同方式采同一条路，而不必
            // 让图仅仅为了保住形状而一直留着原始点集。
            result.Splines = (PCGSpline[])splines.Clone();

            return result;
        }

        private Vector3 ApplyJitter(PCGSpline spline, Vector3 position, PCGExecutionContext context, int index)
        {
            var lifted = position + Vector3.Up * HeightOffset;

            if (ArcJitter <= 0.0f)
                return lifted;

            var rng = new PCGDeterministicRng(context.Seed + SeedOffset, context.CurrentNodeId, index);
            float offset = rng.NextRange(-ArcJitter, ArcJitter);

            return lifted + TangentNear(spline, position) * offset;
        }

        /// <summary>
        /// 离某个位置最近的那条曲线的方向：按弧长试探，取最近的试探点。
        /// </summary>
        /// <remarks>
        /// 采样点其实知道自己沿曲线的距离，但那样就得把它穿过
        /// 两种采样模式与抖动一起传下去；试探只是几次向量比较，能让调用点
        /// 保持简单。试探次数固定，所以答案不依赖曲线有多长。
        /// </remarks>
        private static Vector3 TangentNear(PCGSpline spline, Vector3 position)
        {
            if (spline == null || !spline.IsUsable)
                return Vector3.Forward;

            const int probes = 64;
            float bestDistance = float.MaxValue;
            float bestAt = 0.0f;

            for (int i = 0; i <= probes; i++)
            {
                float at = spline.Length * i / probes;
                float distance = (spline.PositionAt(at) - position).LengthSquared;

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestAt = at;
                }
            }

            return spline.TangentAt(bestAt);
        }
    }
}

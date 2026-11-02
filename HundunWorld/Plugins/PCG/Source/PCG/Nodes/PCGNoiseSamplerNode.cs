// PCG 插件 - 噪声采样节点
// FlaxEngine PCG 插件的一部分。见 docs/PCG_PLUGIN_DESIGN.md §5.1.1 与 §3.4。

using System;
using FlaxEngine;
using PCG.Execution;

namespace PCG.Nodes
{
    /// <summary>
    /// 在种子噪声场超过阈值的那些位置产出点。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 让这个森林看起来像森林的正是这个采样器：噪声决定密度落在哪里，
    /// 而点自身的密度由噪声值给定，好让下游的过滤器或生成器
    /// 把边缘当作"稀疏"而不是"不存在"。
    /// </para>
    /// <para>
    /// 它采样的是插件自己的 <see cref="PCGSeededNoise"/>，从不用 <c>FlaxEngine.Utilities.Noise</c>：
    /// 引擎那份噪声没有 seed 参数，于是一张换了 seed 的图会产出同一个场，
    /// 而"换个 seed 重跑"就静默地什么都不做（docs/PCG_PLUGIN_DESIGN.md §3.4）。
    /// </para>
    /// </remarks>
    public sealed class PCGNoiseSamplerNode : PCGNodeBase
    {
        /// <summary>稳定的类型 id。</summary>
        public const string Id = "NoiseSampler";

        // 参数下标。顺序属于存档图格式，只能追加。
        private const int PKind = 0;
        private const int PFrequency = 1;
        private const int PThreshold = 2;
        private const int POctaves = 3;
        private const int PCellSize = 4;
        private const int PJitter = 5;
        private const int PHeight = 6;
        private const int PSeedOffset = 7;
        private const int PScale = 8;

        /// <summary>本节点读取的参数个数。</summary>
        public const int ParameterCount = 9;

        private readonly string _typeId;
        private readonly PCGNodeParameters _params;

        /// <summary>采样哪一份种子噪声场。</summary>
        public PCGNoiseKind Kind = PCGNoiseKind.Perlin2D;

        /// <summary>噪声频率，单位是每世界单位的周期数。</summary>
        public float Frequency = 0.002f;

        /// <summary>噪声值低于此数的格子被跳过。</summary>
        public float Threshold = 0.5f;

        /// <summary>分形倍频数；1 就是单带普通采样。</summary>
        public int Octaves = 1;

        /// <summary>格子边长（世界单位）。为 0 时由频率推出。</summary>
        public float CellSize;

        /// <summary>以格子边长的分数计的偏移，好让点阵看上去不像点阵。</summary>
        public float Jitter = 0.5f;

        /// <summary>放点用的世界 Y；除此之外生成边界在垂直方向不参与。</summary>
        public float Height;

        /// <summary>叠加到图的 seed 上，让两个噪声采样器不至于给出同一个场。</summary>
        public int SeedOffset;

        /// <summary>作用到产出点上的统一缩放。</summary>
        public float Scale = 1.0f;

        /// <summary>由序列化的参数创建节点。</summary>
        public PCGNoiseSamplerNode(PCGNodeRegistry.PCGNodeSerializationData data)
        {
            _typeId = Id;
            _params = new PCGNodeParameters(data.Values, data.TypeId);

            Kind = _params.Get(PKind, PCGNoiseKind.Perlin2D);
            Frequency = Math.Max(1e-6f, _params.Get(PFrequency, 0.002f));
            Threshold = _params.Get(PThreshold, 0.5f);
            Octaves = Math.Max(1, Math.Min(8, _params.Get(POctaves, 1)));
            CellSize = Math.Max(0.0f, _params.Get(PCellSize, 0.0f));
            Jitter = Math.Min(1.0f, Math.Max(0.0f, _params.Get(PJitter, 0.5f)));
            Height = _params.Get(PHeight, 0.0f);
            SeedOffset = _params.Get(PSeedOffset, 0);
            Scale = Math.Max(0.0001f, _params.Get(PScale, 1.0f));
        }

        /// <inheritdoc />
        public override string TypeId => _typeId;

        /// <inheritdoc />
        public override string DisplayName => "Noise Sampler";

        /// <inheritdoc />
        public override bool IsThreadSafe => true;

        /// <inheritdoc />
        public override PCGPinDefinition[] InputPins => Array.Empty<PCGPinDefinition>();

        /// <inheritdoc />
        public override PCGPinDefinition[] OutputPins => new[]
        {
            new PCGPinDefinition("Out", PCGPinDirection.Output, tooltip: "Points where the noise field is dense."),
        };

        /// <inheritdoc />
        public override string DescribeParameters() =>
            $"kind={Kind}, frequency={Frequency:E2}, threshold={Threshold:F2}, octaves={Octaves}, " +
            $"cell={CellSize:F1}, jitter={Jitter:F2}";

        /// <inheritdoc />
        public override PCGData OnExecute(PCGExecutionContext context, PCGData[] inputs)
        {
            LastError = null;

            var bounds = context.GenerationBounds;
            float widthX = bounds.Maximum.X - bounds.Minimum.X;
            float widthZ = bounds.Maximum.Z - bounds.Minimum.Z;

            if (widthX <= 0.0f || widthZ <= 0.0f)
            {
                LastError = "Generation bounds have no XZ extent.";
                return PCGData.Empty;
            }

            // 频率为 f 的噪声场半周期是 1/(2f)，比它密得多地采样换不来什么、
            // 只换来内存，所以没写格子边长时退回半周期的五分之一。
            float cell = CellSize > 0.0f
                ? CellSize
                : Math.Max(1.0f, (1.0f / (2.0f * Frequency)) * 0.2f);

            int cellsX = (int)MathF.Ceiling(widthX / cell);
            int cellsZ = (int)MathF.Ceiling(widthZ / cell);

            if (cellsX <= 0 || cellsZ <= 0)
                return PCGData.Empty;

            // 先定上界：存活的点被收进一个列表，因为件数由噪声决定。
            int capacity = cellsX * cellsZ;
            if (capacity > 4_000_000)
            {
                LastError = $"Noise grid would hold {capacity} cells; raise the cell size or shrink the volume.";
                return PCGData.Empty;
            }

            var points = new System.Collections.Generic.List<PCGPoint>(capacity / 4 + 16);

            for (int iz = 0; iz < cellsZ; iz++)
            {
                context.ThrowIfCancelled(iz, 1);

                for (int ix = 0; ix < cellsX; ix++)
                {
                    int cellIndex = iz * cellsX + ix;
                    float x = bounds.Minimum.X + (ix + 0.5f) * cell;
                    float z = bounds.Minimum.Z + (iz + 0.5f) * cell;

                    // 一次调用覆盖两种频带：Sample2D 会把 Fbm 那几档按倍频数走。
                    float value = PCGSeededNoise.Sample2D(
                        Kind, new Vector2(x, z), context.Seed + SeedOffset, Frequency, Octaves, 2.0f, 0.5f);

                    if (value < Threshold)
                        continue;

                    var rng = new PCGDeterministicRng(context.Seed + SeedOffset, context.CurrentNodeId, cellIndex);

                    var position = new Vector3(
                        x + rng.NextRange(-0.5f, 0.5f) * cell * Jitter,
                        Height,
                        z + rng.NextRange(-0.5f, 0.5f) * cell * Jitter);

                    var point = PCGPoint.Create(position, value, cellIndex + SeedOffset);
                    point.Scale = new Float3(Scale);
                    point.UpdateBounds(new Vector3(cell * 0.5f, 0.0f, cell * 0.5f));

                    points.Add(point);
                }
            }

            if (points.Count == 0)
            {
                LastError = "The noise field never reached the threshold; lower it or check the frequency.";
                return PCGData.Empty;
            }

            return new PCGData(points.ToArray());
        }
    }
}

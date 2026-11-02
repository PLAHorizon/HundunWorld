// PCG 插件 - 体积采样节点
// FlaxEngine PCG 插件的一部分。见 docs/PCG_PLUGIN_DESIGN.md §5.1.1。

using System;
using System.Collections.Generic;
using FlaxEngine;
using PCG.Execution;

namespace PCG.Nodes
{
    /// <summary>
    /// 在生成体积内均匀随机地撒固定数量的点。
    /// </summary>
    /// <remarks>
    /// 与噪声采样不同，它不承诺均匀覆盖——均匀随机采样会成团，而这就是它的用途：
    /// 那是尘埃、洞穴填充物或碎屑的形状。要铺开得均匀就用地形采样器，
    /// 或者在本节点后面接一个自剪枝过滤器来打散那些团。
    /// </remarks>
    public sealed class PCGVolumeSamplerNode : PCGNodeBase
    {
        /// <summary>稳定的类型 id。</summary>
        public const string Id = "VolumeSampler";

        // 参数下标。顺序属于存档图格式，只能追加。
        private const int PPointCount = 0;
        private const int PSeedOffset = 1;
        private const int PScale = 2;
        private const int PRandomRotation = 3;
        private const int PDensity = 4;
        private const int PPointsPerBlock = 5;

        /// <summary>本节点读取的参数个数。</summary>
        public const int ParameterCount = 6;

        private readonly string _typeId;
        private readonly PCGNodeParameters _params;

        /// <summary>放多少个点。</summary>
        public int PointCount = 1000;

        /// <summary>叠加到图的 seed 上，让同一张图里的两个采样器不至于给出同一批点。</summary>
        public int SeedOffset;

        /// <summary>作用到产出点上的统一缩放。</summary>
        public float Scale = 1.0f;

        /// <summary>设置时，每个点拿到一个随机朝向。</summary>
        public bool RandomRotation = true;

        /// <summary>写进每个点的密度，好让下游生成器把它当作生成概率。</summary>
        public float Density = 1.0f;

        /// <summary>
        /// 每 <see cref="AreaBlockSide"/>² 的 XZ 面积采样多少个点；为正时替代 Point Count。
        /// </summary>
        /// <remarks>
        /// 它存在的理由是分区：一次分区的运行是每格执行一遍图，所以 Point Count 在那里的意思是"每格"，
        /// 于是设计者把流式格子尺寸调到面积的一半，就神不知鬼不觉地把场景里的道具翻倍了——
        /// 连生成它们的开销也一起翻。按面积算密度才是那个不随格子变化的单位，
        /// 正因如此格子尺寸才是流式旋钮而不是密度旋钮。
        /// 为零时保留历史的固定数量行为。
        /// </remarks>
        public float PointsPerBlock;

        /// <summary><see cref="PointsPerBlock"/> 所用的方块边长，世界单位。</summary>
        public const float AreaBlockSide = 1000.0f;

        /// <summary>由序列化的参数创建节点。</summary>
        public PCGVolumeSamplerNode(PCGNodeRegistry.PCGNodeSerializationData data)
        {
            _typeId = Id;
            _params = new PCGNodeParameters(data.Values, data.TypeId);

            PointCount = Math.Max(0, _params.Get(PPointCount, 1000));
            SeedOffset = _params.Get(PSeedOffset, 0);
            Scale = Math.Max(0.0001f, _params.Get(PScale, 1.0f));
            RandomRotation = _params.Get(PRandomRotation, true);
            Density = Math.Min(1.0f, Math.Max(0.0f, _params.Get(PDensity, 1.0f)));
            PointsPerBlock = Math.Max(0.0f, _params.Get(PPointsPerBlock, 0.0f));
        }

        /// <inheritdoc />
        public override string TypeId => _typeId;

        /// <inheritdoc />
        public override string DisplayName => "Volume Sampler";

        /// <inheritdoc />
        public override bool IsThreadSafe => true;

        /// <inheritdoc />
        public override PCGPinDefinition[] InputPins => Array.Empty<PCGPinDefinition>();

        /// <inheritdoc />
        public override PCGPinDefinition[] OutputPins => new[]
        {
            new PCGPinDefinition("Out", PCGPinDirection.Output, tooltip: "Points scattered through the volume."),
        };

        /// <inheritdoc />
        /// <summary>
        /// 给定密度下一块 XZ 面积应得多少个点，向上取整。
        /// </summary>
        /// <remarks>
        /// 向上取整是因为：比一个方块还小的格子否则会一个点都不采，
        /// 场景就正好按设计者选的格子尺寸长出洞。这样引入的误差——每格一个点——
        /// 会随格子变大而相对面积变小；该在意它的场合正是同一张图整体跑与分窗跑，
        /// 而那恰好是误差相对变小的方向。
        /// </remarks>
        private static int PointsForArea(in BoundingBox bounds, float pointsPerBlock)
        {
            float area = (bounds.Maximum.X - bounds.Minimum.X) * (bounds.Maximum.Z - bounds.Minimum.Z);
            if (area <= 0.0f)
                return 0;

            int count = (int)MathF.Ceiling(area / (AreaBlockSide * AreaBlockSide) * pointsPerBlock);
            return count > 0 ? count : 1;
        }

        public override string DescribeParameters() =>
            $"count={PointCount}, scale={Scale:F2}, randomRotation={RandomRotation}, density={Density:F2}, " +
            $"perBlock={PointsPerBlock:F1}";

        /// <inheritdoc />
        public override PCGData OnExecute(PCGExecutionContext context, PCGData[] inputs)
        {
            LastError = null;

            var bounds = context.GenerationBounds;

            // 在数量闸门之前解析，因为 Points Per Block 是替代 Point Count 的：
            // 一张数量为 0、面积密度为正的图照样要采样。
            int count = PointsPerBlock > 0.0f ? PointsForArea(in bounds, PointsPerBlock) : PointCount;

            if (count <= 0)
                return PCGData.Empty;

            if (bounds.Maximum.X - bounds.Minimum.X <= 0.0f ||
                bounds.Maximum.Y - bounds.Minimum.Y <= 0.0f ||
                bounds.Maximum.Z - bounds.Minimum.Z <= 0.0f)
            {
                LastError = "Generation bounds are flat; a volume sampler needs extent on all three axes.";
                return PCGData.Empty;
            }

            var data = PCGData.Create(count);
            var half = new Vector3(
                (bounds.Maximum.X - bounds.Minimum.X) * 0.5f,
                (bounds.Maximum.Y - bounds.Minimum.Y) * 0.5f,
                (bounds.Maximum.Z - bounds.Minimum.Z) * 0.5f);

            // 给一组固定的范围尺寸，好让自剪枝过滤器在生成器还不知道真实资产尺寸之前，
            // 也有一件可比的东西。
            float extents = MathF.Max(half.X, MathF.Max(half.Y, half.Z)) * 0.02f;

            // 这里只做按下标的工作，所以这个循环可以分到多个 worker 上跑，
            // 交回来的点与串行跑完全一致、顺序也一致。
            PCG.Execution.PCGParallel.For(count, i =>
            {
                context.ThrowIfCancelled(i);

                var rng = new PCGDeterministicRng(context.Seed + SeedOffset, context.CurrentNodeId, i);

                var point = PCGPoint.Create(rng.NextPointInBox(bounds), Density, i + SeedOffset);
                point.Scale = new Float3(Scale);

                if (RandomRotation)
                    point.Rotation = rng.NextRotation();

                point.UpdateBounds(new Vector3(extents, extents, extents));
                data.Points[i] = point;
            });

            return data;
        }
    }
}

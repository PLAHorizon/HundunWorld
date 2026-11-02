// PCG 插件 - 复制点（点运算）
// 属于 FlaxEngine PCG 插件。见 docs/PCG_PLUGIN_DESIGN.md §5.1.4。

using System;
using FlaxEngine;
using PCG.Execution;

namespace PCG.Nodes
{
    /// <summary>
    /// 把每个点换成一簇小复制品——一片散布层变成一丛，一块石头变成
    /// 一片碎石滩，一枚贴花变成一串贴花。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 复制品**按引用**共享源点的属性集，而不是每个克隆拷一份值。这样
    /// 最省，而且属性天然一致；本插件的契约是：**写**属性的节点
    /// 必须先克隆属性集，所以下游的写方不会反手改到
    /// 这些复制品的来源层。
    /// </para>
    /// <para>
    /// 随机性以 <c>（种子、节点 id、源下标 × 个数 + 第几个副本）</c> 为键，所以同一张图能复现
    /// 同一丛——即便输入层被重新生成；而且偏移为零时每个点的第一个副本就是它
    /// 自己——这正是“复制 3 个、沿 up 轴偏移”看起来像一摞
    /// 而不是像一片散点的原因。
    /// </para>
    /// </remarks>
    public sealed class PCGCopyPointsNode : PCGNodeBase
    {
        /// <summary>稳定的类型 id。</summary>
        public const string Id = "CopyPoints";

        // 参数下标。顺序是已存图格式的一部分；只许追加。
        private const int PCount = 0;
        private const int POffset = 1;
        private const int PLocalSpace = 2;
        private const int PRotationSpread = 3;
        private const int PScaleMin = 4;
        private const int PScaleMax = 5;
        private const int PSeedOffset = 6;

        /// <summary>本节点读取的参数个数。</summary>
        public const int ParameterCount = 7;

        /// <summary>每个输入点产出几个副本。1 表示原样透传。</summary>
        public const int DefaultCount = 3;

        /// <summary>每点副本数上限，免得手填一个数在预算之前就把内存吃光。</summary>
        public const int MaxCount = 4096;

        private readonly string _typeId;
        private readonly PCGNodeParameters _params;

        /// <summary>每个点变成几个副本；第一个是它自己。</summary>
        public int Count = DefaultCount;

        /// <summary>相邻副本之间的偏移。</summary>
        public Vector3 Offset;

        /// <summary>打开时偏移跟着每个点自己的朝向——堆叠沿表面法线走。</summary>
        public bool LocalSpace = true;

        /// <summary>每个副本随机旋转的半幅（度）。</summary>
        public Vector3 RotationSpread;

        /// <summary>副本缩放倍数的最小值。</summary>
        public float ScaleMin = 1.0f;

        /// <summary>副本缩放倍数的最大值。</summary>
        public float ScaleMax = 1.0f;

        /// <summary>把同一张图里的两个复制节点区分开。</summary>
        public int SeedOffset;

        /// <summary>用反序列化出来的参数建节点。</summary>
        public PCGCopyPointsNode(PCGNodeRegistry.PCGNodeSerializationData data)
        {
            _typeId = Id;
            _params = new PCGNodeParameters(data.Values, data.TypeId);

            Count = Math.Min(MaxCount, Math.Max(1, _params.Get(PCount, DefaultCount)));
            Offset = _params.GetVector3(POffset, Vector3.Zero);
            LocalSpace = _params.Get(PLocalSpace, true);
            RotationSpread = _params.GetVector3(PRotationSpread, Vector3.Zero);
            ScaleMin = Math.Max(0.0f, _params.Get(PScaleMin, 1.0f));
            ScaleMax = Math.Max(0.0f, _params.Get(PScaleMax, 1.0f));
            SeedOffset = _params.Get(PSeedOffset, 0);

            if (ScaleMax < ScaleMin)
                (ScaleMin, ScaleMax) = (ScaleMax, ScaleMin);
        }

        /// <inheritdoc />
        public override string TypeId => _typeId;

        /// <inheritdoc />
        public override string DisplayName => "Copy Points";

        /// <inheritdoc />
        public override bool IsThreadSafe => true;

        /// <inheritdoc />
        public override PCGPinDefinition[] InputPins => new[]
        {
            new PCGPinDefinition("In", PCGPinDirection.Input, isRequired: true,
                tooltip: "Points to duplicate."),
        };

        /// <inheritdoc />
        public override PCGPinDefinition[] OutputPins => new[]
        {
            new PCGPinDefinition("Out", PCGPinDirection.Output,
                tooltip: "Every input point plus its copies, in source order."),
        };

        /// <inheritdoc />
        public override string DescribeParameters() =>
            $"count={Count}, offset={Offset}, localSpace={LocalSpace}, spread={RotationSpread:F1}, " +
            $"scale=[{ScaleMin:F2},{ScaleMax:F2}]";

        /// <inheritdoc />
        public override PCGData OnExecute(PCGExecutionContext context, PCGData[] inputs)
        {
            var input = inputs[0];
            if (input == null || input.Count == 0)
                return PCGData.Empty;

            if (Count == 1 && Offset == Vector3.Zero)
                return input.Clone();

            var source = input.Points;
            int perPoint = Count;
            long total = (long)source.Length * perPoint;

            if (total > int.MaxValue)
                throw new PCGNodeExecutionException(
                    $"{Id}: {source.Length} points x {perPoint} copies overflows the point array.");

            var data = PCGData.Create((int)total);
            var result = data.Points;
            int seed = context.Seed + SeedOffset;
            int nodeId = context.CurrentNodeId;

            PCGParallel.For(source.Length, i =>
            {
                context.ThrowIfCancelled(i);

                var origin = source[i];

                for (int c = 0; c < perPoint; c++)
                {
                    int slot = i * perPoint + c;
                    var copy = origin;
                    copy.Seed = slot;

                    var step = Offset * c;
                    copy.Position = LocalSpace
                        ? origin.Position + Vector3.Transform(step, origin.Rotation)
                        : origin.Position + step;

                    var rng = new PCGDeterministicRng(seed, nodeId, slot);

                    var jitter = new Vector3(
                        RotationSpread.X > 0.0f ? rng.NextRange(-RotationSpread.X, RotationSpread.X) : 0.0f,
                        RotationSpread.Y > 0.0f ? rng.NextRange(-RotationSpread.Y, RotationSpread.Y) : 0.0f,
                        RotationSpread.Z > 0.0f ? rng.NextRange(-RotationSpread.Z, RotationSpread.Z) : 0.0f);

                    var delta = PCGPointTransform.EulerDegrees(jitter);
                    copy.Rotation = Quaternion.Multiply(origin.Rotation, delta);

                    // 只要作者要的缩放不是“点自己的缩放”，就施加缩放。
                    // 用 “Max > Min” 作判据会让固定倍数变成空操作，而固定倍数正是
                    // 一摞相同道具的常见写法。
                    bool rescale = ScaleMax != 1.0f || ScaleMin != 1.0f;

                    if (rescale)
                    {
                        float factor = ScaleMax > ScaleMin ? rng.NextRange(ScaleMin, ScaleMax) : ScaleMin;
                        copy.Scale = origin.Scale * factor;
                    }

                    // 旋转或缩放过的副本需要一个仍能包住它的包围盒，否则下游的剪枝与布尔
                    // 运算会拿一个已经装不下它的盒子去判定。
                    var half = PCGPointTransform.EnclosingExtents(
                        PCGPointTransform.HalfExtents(origin.Bounds), delta);

                    copy.Bounds = PCGPointTransform.MakeBox(copy.Position, new Vector3(
                        half.X * copy.Scale.X, half.Y * copy.Scale.Y, half.Z * copy.Scale.Z));

                    result[slot] = copy;
                }
            });

            return data;
        }
    }
}

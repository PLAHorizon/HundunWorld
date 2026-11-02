// PCG 插件 - 到点距离（点运算）
// FlaxEngine PCG 插件的一部分。见 docs/PCG_PLUGIN_DESIGN.md §5.1.18。

using System;
using FlaxEngine;
using PCG.Execution;

namespace PCG.Nodes
{
    /// <summary>
    /// 把每个点到另一层最近点的距离写进一列——这正是一条路、
    /// 一条河或一片林间空地所要求的衰减。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="PCGDistanceFilterNode"/> 从一个固定位置量距离，然后把量到的数丢掉，
    /// 只留下一个是/否。那够回答"在圈内吗"，却不够回答"往边上稀一点"：
    /// 设计者要拿去重映射成密度的正是**这个数本身**，而且它得对着另一层的
    /// *形状* 来量，不是对着单个坐标。这就是 UE5 的 Distance 节点。
    /// </para>
    /// <para>
    /// 可达范围就是查询格子，所以 <see cref="MaxDistance"/> 之内的参照点一定落在
    /// <see cref="PCGPointIndex"/> 会走的 3x3 邻域里——这是构造上正确，不是调出来的近似。
    /// 范围内什么都没有的目标写成 <see cref="MaxDistance"/>，UE5 也施加同一个钳位；
    /// 于是"够不着任何参照"是一个真答案（"比上限还远"），
    /// 不是一个缺失的答案。
    /// </para>
    /// </remarks>
    public sealed class PCGDistanceToPointsNode : PCGNodeBase
    {
        /// <summary>稳定的类型 id。</summary>
        public const string Id = "DistanceToPoints";

        // 参数下标。顺序是存档图格式的一部分，只能往后追加。
        private const int PAttribute = 0;
        private const int PMaxDistance = 1;
        private const int PPlaneOnly = 2;

        /// <summary>本节点读取的参数个数。</summary>
        public const int ParameterCount = 3;

        /// <summary>节点在没有序列化值的情况下被构造时，距离写进哪一列。</summary>
        public const string DefaultAttribute = "distance";

        /// <summary>可达范围，也是范围内没有东西时写下的那个值。</summary>
        public const float DefaultMaxDistance = 1000.0f;

        private readonly string _typeId;
        private readonly PCGNodeParameters _params;

        /// <summary>距离写进的列。留空则写点的密度。</summary>
        public string AttributeName = DefaultAttribute;

        /// <summary>距离钳到这个值；够不着任何参照的目标就拿到这个值。</summary>
        public float MaxDistance = DefaultMaxDistance;

        /// <summary>置位时测量不看竖轴。</summary>
        public bool PlaneOnly = true;

        /// <summary>用序列化参数构造节点。</summary>
        public PCGDistanceToPointsNode(PCGNodeRegistry.PCGNodeSerializationData data)
        {
            _typeId = Id;
            _params = new PCGNodeParameters(data.Values, data.TypeId);

            AttributeName = _params.Get(PAttribute, DefaultAttribute) ?? string.Empty;
            MaxDistance = Math.Max(0.0f, _params.Get(PMaxDistance, DefaultMaxDistance));
            PlaneOnly = _params.Get(PPlaneOnly, true);
        }

        /// <inheritdoc />
        public override string TypeId => _typeId;

        /// <inheritdoc />
        public override string DisplayName => "Distance To Points";

        /// <inheritdoc />
        public override bool IsThreadSafe => true;

        /// <inheritdoc />
        public override PCGPinDefinition[] InputPins => new[]
        {
            new PCGPinDefinition("In", PCGPinDirection.Input, isRequired: true,
                tooltip: "Points the distance column is written onto."),
            new PCGPinDefinition("Reference", PCGPinDirection.Input, isRequired: true,
                tooltip: "Points the distance is measured to; the nearest one wins."),
        };

        /// <inheritdoc />
        public override PCGPinDefinition[] OutputPins => new[]
        {
            new PCGPinDefinition("Out", PCGPinDirection.Output,
                tooltip: "In's points, each carrying its distance to the nearest reference point."),
        };

        /// <inheritdoc />
        public override string DescribeParameters()
        {
            string name = string.IsNullOrEmpty(AttributeName) ? PCGAttributeSet.DensityName : AttributeName;
            return $"{name} = nearest distance, cap {MaxDistance:F1}, planeOnly={PlaneOnly}";
        }

        /// <inheritdoc />
        public override PCGData OnExecute(PCGExecutionContext context, PCGData[] inputs)
        {
            var input = inputs != null && inputs.Length > 0 ? inputs[0] : null;
            if (input == null || input.Count == 0)
                return PCGData.Empty;

            var reference = inputs != null && inputs.Length > 1 ? inputs[1] : null;
            if (reference == null || reference.Count == 0)
            {
                LastError = "Distance To Points has no Reference layer, so there is nothing to measure to. " +
                            "The input passes through unchanged.";
                return input.Clone();
            }

            bool toDensity = string.IsNullOrEmpty(AttributeName) ||
                             string.Equals(AttributeName, PCGAttributeSet.DensityName, StringComparison.Ordinal);

            var index = new PCGPointIndex(reference.Points, Math.Max(MaxDistance, 1e-3f), PlaneOnly);
            var referencePositions = reference.Points;

            if (toDensity)
            {
                var result = input.Clone();
                var targetPoints = result.Points;

                for (int i = 0; i < targetPoints.Length; i++)
                {
                    context.ThrowIfCancelled(i);
                    targetPoints[i].Density = Distance(index, referencePositions, targetPoints[i].Position);
                }

                return result;
            }

            var cloned = input.Clone();
            var attributes = EnsureAttributes(cloned);

            if (!attributes.Contains(AttributeName))
                attributes.DeclareAttributeUnsafe(AttributeName, PCGAttributeType.Float);

            var column = attributes.GetWritableFloatArray(AttributeName);
            var points = cloned.Points;

            for (int i = 0; i < points.Length; i++)
            {
                context.ThrowIfCancelled(i);
                column[i] = Distance(index, referencePositions, points[i].Position);
            }

            return cloned;
        }

        /// <summary>
        /// 从 <paramref name="position"/> 到最近参照点的距离，钳到可达范围内。
        /// </summary>
        /// <remarks>
        /// 可达范围就是建索引时用的那个格子，所以这里走访的候选就是全部够格的候选。
        /// 打平时取下标更小的那个参照，于是答案不依赖索引
        /// 恰好按什么顺序把候选交回来。
        /// </remarks>
        private float Distance(PCGPointIndex index, PCGPoint[] reference, in Vector3 position)
        {
            float cap = MaxDistance;
            float bestSquared = cap * cap;
            int best = -1;

            // 从 `in` 参数里拷出来，好让候选走访的委托能捕获它。
            var query = position;

            index.ForEachNearby(position, j =>
            {
                var delta = reference[j].Position - query;

                float squared = PlaneOnly
                    ? delta.X * delta.X + delta.Z * delta.Z
                    : delta.X * delta.X + delta.Y * delta.Y + delta.Z * delta.Z;

                if (squared < bestSquared || (squared == bestSquared && (best < 0 || j < best)))
                {
                    bestSquared = squared;
                    best = j;
                }
            });

            return best < 0 ? cap : MathF.Sqrt(bestSquared);
        }

        /// <summary>数据集的属性存储；缺失时就地创建并盖到它的点上。</summary>
        private static PCGAttributeSet EnsureAttributes(PCGData data)
        {
            var attributes = data.SharedAttributes;

            if (attributes == null)
            {
                attributes = new PCGAttributeSet(data.Count);
                data.SharedAttributes = attributes;

                for (int i = 0; i < data.Points.Length; i++)
                    data.Points[i].Attributes = attributes;
            }

            return attributes;
        }
    }
}

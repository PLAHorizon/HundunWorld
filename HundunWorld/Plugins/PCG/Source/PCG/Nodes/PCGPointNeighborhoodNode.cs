// PCG 插件 - 点邻域（点运算）
// FlaxEngine PCG 插件的一部分。见 docs/PCG_PLUGIN_DESIGN.md §5.1.9。

using System;
using FlaxEngine;
using PCG.Execution;

namespace PCG.Nodes
{
    /// <summary>
    /// 数出每个点在半径内有多少邻居，并按需把它们某个属性的均值也算出来。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 这就是"我这儿有多挤"那一列：密度值来自点实际站在哪儿，
    /// 而不是来自当初告诉采样器的数量、也不是来自地面有多陡。把它喂给
    /// <see cref="PCGRemapAttributeNode"/> 就是一个散点层在村庄里自己变稀、
    /// 在林线边上自己变密的做法——和 UE5 用邻域节点搭出来的图是同一个形状。
    /// </para>
    /// <para>
    /// 拥挤度是**量**出来的，不是**定**出来的：本节点从不删除或挪动任何东西，只发布数字。
    /// 这样剔除规则只待在一个地方（<see cref="PCGSelfPruningFilterNode"/>），
    /// 图却照样能拿拥挤度来讲道理——而且这意味着一次邻域遍历可以先作为编辑器里的
    /// 一列被检视，再决定要不要有人对它动手。
    /// </para>
    /// <para>
    /// 开销正比于点数乘以平均邻居数，而查询网格的格子就取半径那么大，
    /// 所以只会走访一圈的那批格子。半径为 0 会被点名拒绝：
    /// 空邻域不是一条统计，而是一张从没填完的图。
    /// </para>
    /// </remarks>
    public sealed class PCGPointNeighborhoodNode : PCGNodeBase
    {
        /// <summary>稳定的类型 id。</summary>
        public const string Id = "PointNeighborhood";

        // 参数下标。顺序是存档图格式的一部分，只能往后追加。
        private const int PRadius = 0;
        private const int PCountAttribute = 1;
        private const int PAverageSourceAttribute = 2;
        private const int PAverageResultAttribute = 3;
        private const int PIncludeSelf = 4;
        private const int PPlaneOnly = 5;

        /// <summary>本节点读取的参数个数。</summary>
        public const int ParameterCount = 6;

        /// <summary>邻居数写进哪个属性名。</summary>
        public const string DefaultCountAttribute = "neighbors";

        private readonly string _typeId;
        private readonly PCGNodeParameters _params;

        /// <summary>围绕每个点搜索的半径。</summary>
        public float Radius = 200.0f;

        /// <summary>邻居数写进哪个属性。</summary>
        public string CountAttribute = DefaultCountAttribute;

        /// <summary>在邻居之间求均值的属性；留空就不写均值。</summary>
        public string AverageSourceAttribute = string.Empty;

        /// <summary>邻居均值写进哪个属性。</summary>
        public string AverageResultAttribute = "neighborAverage";

        /// <summary>置位时，一个点算作自己的邻居。</summary>
        public bool IncludeSelf;

        /// <summary>置位时搜索不看竖轴，落地散点要的正是这个。</summary>
        public bool PlaneOnly = true;

        /// <summary>用序列化参数构造节点。</summary>
        public PCGPointNeighborhoodNode(PCGNodeRegistry.PCGNodeSerializationData data)
        {
            _typeId = Id;
            _params = new PCGNodeParameters(data.Values, data.TypeId);

            Radius = Math.Max(0.0f, _params.Get(PRadius, 200.0f));
            CountAttribute = _params.Get(PCountAttribute, DefaultCountAttribute) ?? string.Empty;
            AverageSourceAttribute = _params.Get(PAverageSourceAttribute, string.Empty) ?? string.Empty;
            AverageResultAttribute = _params.Get(PAverageResultAttribute, "neighborAverage") ?? string.Empty;
            IncludeSelf = _params.Get(PIncludeSelf, false);
            PlaneOnly = _params.Get(PPlaneOnly, true);
        }

        /// <inheritdoc />
        public override string TypeId => _typeId;

        /// <inheritdoc />
        public override string DisplayName => "Point Neighborhood";

        /// <inheritdoc />
        public override bool IsThreadSafe => true;

        /// <inheritdoc />
        public override PCGPinDefinition[] InputPins => new[]
        {
            new PCGPinDefinition("In", PCGPinDirection.Input, isRequired: true,
                tooltip: "Points whose crowding is measured."),
        };

        /// <inheritdoc />
        public override PCGPinDefinition[] OutputPins => new[]
        {
            new PCGPinDefinition("Out", PCGPinDirection.Output,
                tooltip: "The same points, each carrying its neighbour count."),
        };

        /// <inheritdoc />
        public override string DescribeParameters() =>
            $"radius={Radius:F1}, count->'{CountAttribute}', " +
            $"average={(string.IsNullOrEmpty(AverageSourceAttribute) ? "none" : $"'{AverageSourceAttribute}'->'{AverageResultAttribute}'")}, " +
            $"includeSelf={IncludeSelf}, planeOnly={PlaneOnly}";

        /// <inheritdoc />
        public override PCGData OnExecute(PCGExecutionContext context, PCGData[] inputs)
        {
            var input = inputs[0];
            if (input == null || input.Count == 0)
                return PCGData.Empty;

            if (Radius <= 0.0f || string.IsNullOrEmpty(CountAttribute))
            {
                LastError = "Point Neighborhood needs a positive radius and a count attribute name; " +
                            "the input passes through unchanged.";
                return input.Clone();
            }

            var result = input.Clone();
            var attributes = EnsureAttributes(result);
            var points = result.Points;

            bool average = !string.IsNullOrEmpty(AverageSourceAttribute) &&
                           !string.IsNullOrEmpty(AverageResultAttribute);
            bool sourcePresent = average && attributes.Contains(AverageSourceAttribute);

            if (average && !sourcePresent)
            {
                LastError = $"Point Neighborhood could not average '{AverageSourceAttribute}': " +
                            "no such attribute on this layer. Only the count is written.";
                average = false;
            }

            if (!attributes.Contains(CountAttribute))
                attributes.DeclareAttributeUnsafe(CountAttribute, PCGAttributeType.Float);

            if (average && !attributes.Contains(AverageResultAttribute))
                attributes.DeclareAttributeUnsafe(AverageResultAttribute, PCGAttributeType.Float);

            var counts = attributes.GetWritableFloatArray(CountAttribute);
            var averages = average ? attributes.GetWritableFloatArray(AverageResultAttribute) : null;

            // 格子就是半径，所以半径之内的点要么在这个格子里、要么在它八个
            // 邻居格子之一——和棱柱用的是同一圈论证，只是单位换成了查询半径。
            var index = new PCGPointIndex(points, Radius, PlaneOnly);
            float squaredRadius = Radius * Radius;

            for (int i = 0; i < points.Length; i++)
            {
                context.ThrowIfCancelled(i);

                var origin = points[i].Position;
                int neighbours = IncludeSelf ? 1 : 0;
                double sum = IncludeSelf && sourcePresent ? ReadAttribute(attributes, AverageSourceAttribute, i) : 0.0;

                index.ForEachNearby(origin, other =>
                {
                    if (other == i)
                        return;

                    float dx = points[other].Position.X - origin.X;
                    float dz = points[other].Position.Z - origin.Z;
                    float squared = dx * dx + dz * dz;

                    if (!PlaneOnly)
                    {
                        float dy = points[other].Position.Y - origin.Y;
                        squared += dy * dy;
                    }

                    if (squared > squaredRadius)
                        return;

                    neighbours++;

                    if (sourcePresent)
                        sum += ReadAttribute(attributes, AverageSourceAttribute, other);
                });

                counts[i] = neighbours;

                if (averages != null)
                    averages[i] = neighbours > 0 ? (float)(sum / neighbours) : 0.0f;
            }

            return result;
        }

        private static float ReadAttribute(PCGAttributeSet attributes, string name, int index)
        {
            var set = attributes;
            return set != null && set.Contains(name) ? set.GetFloat(name, index) : 0.0f;
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

// PCG 插件 - 按属性位移（Modifier）
// 属于 FlaxEngine PCG 插件。见 docs/PCG_PLUGIN_DESIGN.md §5.1.27。

using System;
using FlaxEngine;
using PCG.Execution;

namespace PCG.Nodes
{
    /// <summary>
    /// 把每个点沿一根轴挪动一段从某列取来的距离——把图层放到、或放进
    /// 地形查询看不见的那样东西，缺的就是这一步。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 它与 <see cref="PCGScaleByAttributeNode"/> 一起把"列回到几何"那一侧闭合了：
    /// 一张图能先算出一个数，再把它花在这个点的大小或位置上。
    /// 没有它，"逐点不同的位移"就表达不出来——Transform Points 只会按常量挪，
    /// Random Offset 只能按概率抖——于是"地面最湿的地方把摆件沉下去"没有对应的节点。
    /// </para>
    /// <para>
    /// 它刻意做成单轴而不是向量位移：一列就是一个数，
    /// 假成向量等于再凭空造出两列的含义。真需要整条向量的图就把这三样
    /// 各挂一列串起来用，而那也正好是让每一根轴的来源仍然读得懂的唯一办法。
    /// </para>
    /// <para>
    /// 点的包围盒跟着它一起走，与别处一改位置就同样的规则：盒子存的是世界坐标，
    /// 所以一个点挪了而盒子没挪，剪枝器测到的还是它**原来**在的地方。
    /// be.
    /// </para>
    /// </remarks>
    public sealed class PCGOffsetByAttributeNode : PCGNodeBase
    {
        /// <summary>稳定的类型 id。</summary>
        public const string Id = "OffsetByAttribute";

        // 参数下标。顺序是存档图格式的一部分，只能往后追加。
        private const int PAttribute = 0;
        private const int PAxis = 1;
        private const int POffsetMin = 2;
        private const int POffsetMax = 3;
        private const int PInvert = 4;

        /// <summary>本节点读取的参数个数。</summary>
        public const int ParameterCount = 5;

        /// <summary>位移沿哪一根世界轴发生。</summary>
        public enum OffsetAxis
        {
            X = 0,
            Y = 1,
            Z = 2,
        }

        private readonly string _typeId;
        private readonly PCGNodeParameters _params;

        /// <summary>距离从哪一列读；为空表示读点自己的密度。</summary>
        public string AttributeName = string.Empty;

        /// <summary>点沿哪根轴移动。</summary>
        public OffsetAxis Axis = OffsetAxis.Y;

        /// <summary>列为 0 时施加的位移，世界单位。</summary>
        public float OffsetMin;

        /// <summary>列为 1 时施加的位移，世界单位。</summary>
        public float OffsetMax = 100.0f;

        /// <summary>置位时，列值越小反而位移越大。</summary>
        public bool Invert;

        /// <summary>用序列化下来的参数构造节点。</summary>
        public PCGOffsetByAttributeNode(PCGNodeRegistry.PCGNodeSerializationData data)
        {
            _typeId = Id;
            _params = new PCGNodeParameters(data.Values, data.TypeId);

            AttributeName = _params.Get(PAttribute, string.Empty) ?? string.Empty;

            int axis = _params.Get(PAxis, (int)OffsetAxis.Y);
            Axis = (OffsetAxis)Math.Min((int)OffsetAxis.Z, Math.Max(0, axis));

            OffsetMin = _params.Get(POffsetMin, 0.0f);
            OffsetMax = _params.Get(POffsetMax, 100.0f);
            Invert = _params.Get(PInvert, false);
        }

        /// <inheritdoc />
        public override string TypeId => _typeId;

        /// <inheritdoc />
        public override string DisplayName => "Offset By Attribute";

        /// <inheritdoc />
        public override bool IsThreadSafe => true;

        /// <inheritdoc />
        public override PCGPinDefinition[] InputPins => new[]
        {
            new PCGPinDefinition("In", PCGPinDirection.Input, isRequired: true,
                tooltip: "Points to displace."),
        };

        /// <inheritdoc />
        public override PCGPinDefinition[] OutputPins => new[]
        {
            new PCGPinDefinition("Out", PCGPinDirection.Output,
                tooltip: "The same points, moved along the chosen axis."),
        };

        /// <inheritdoc />
        public override string DescribeParameters()
        {
            string name = string.IsNullOrEmpty(AttributeName) ? PCGAttributeSet.DensityName : AttributeName;
            return $"{name} -> {Axis} offset [{OffsetMin:F1},{OffsetMax:F1}], invert={Invert}";
        }

        /// <inheritdoc />
        public override PCGData OnExecute(PCGExecutionContext context, PCGData[] inputs)
        {
            var input = inputs != null && inputs.Length > 0 ? inputs[0] : null;
            if (input == null || input.Count == 0)
                return PCGData.Empty;

            bool byDensity = string.IsNullOrEmpty(AttributeName) ||
                             string.Equals(AttributeName, PCGAttributeSet.DensityName, StringComparison.Ordinal);

            float[] values = null;

            if (!byDensity)
            {
                var attributes = input.SharedAttributes;
                values = attributes != null ? attributes.TryGetFloatArray(AttributeName) : null;

                if (values == null)
                {
                    LastError = $"Offset By Attribute could not read '{AttributeName}': no such float attribute " +
                                "on this layer. The input passes through unchanged.";
                    return input.Clone();
                }
            }

            var result = input.Clone();
            var points = result.Points;

            for (int i = 0; i < points.Length; i++)
            {
                context.ThrowIfCancelled(i);

                float value = byDensity ? points[i].Density : values[i];

                // 一个权重，钳法与缩放那节一致：一个离谱的值不该把摆件甩到天上去。
                value = Math.Min(1.0f, Math.Max(0.0f, value));

                if (Invert)
                    value = 1.0f - value;

                float distance = OffsetMin + (OffsetMax - OffsetMin) * value;

                if (distance == 0.0f)
                    continue;

                var delta = Axis switch
                {
                    OffsetAxis.X => new Vector3(distance, 0.0f, 0.0f),
                    OffsetAxis.Z => new Vector3(0.0f, 0.0f, distance),
                    _ => new Vector3(0.0f, distance, 0.0f),
                };

                var half = PCGPointTransform.HalfExtents(points[i].Bounds);

                points[i].Position += delta;

                // 挪动的方式是绕着新位置把盒子重建一遍，而不是走 UpdateBounds：那个助手
                // 会再把点的旋转与缩放重新套一遍，于是一个歪着或放大过的点，
                // 它的盒子就被套了两层。
                points[i].Bounds = PCGPointTransform.MakeBox(points[i].Position, half);
            }

            return result;
        }
    }
}

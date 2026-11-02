// PCG 插件 - Remap（属性运算）
// 属于 FlaxEngine PCG 插件。见 docs/PCG_PLUGIN_DESIGN.md §5.1.7。

using System;
using FlaxEngine;
using PCG.Execution;

namespace PCG.Nodes
{
    /// <summary>
    /// 把一个区间映到另一个区间——把"这块地有多陡"变成"这里该不该长东西"的那个节点。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 映射的两边都可以是某个属性列，也可以是点自己的密度——这正是它充当
    /// 插件的派生列与各个生成器之间那座桥的原因：把坡度列映进密度，或者把
    /// 噪声列映进一个叫 growth 的属性、再交给别的节点去筛。输出区间还允许
    /// 反着写（Out Min 高于 Out Max），"越陡就越少"于是无需第二个节点。
    /// </para>
    /// <para>
    /// 输入区间宽度为零是"这张图还没标定"，不是除以零：每个值都映到输出区间的
    /// 同一端，取哪一端由这个值落在输入区间的哪一侧决定。钳位默认开着，
    /// 因为不钳位的重映射正是密度变成 40 或 -3 的来路，而生成器把密度
    /// 当概率来用。
    /// </para>
    /// </remarks>
    public sealed class PCGRemapAttributeNode : PCGNodeBase
    {
        /// <summary>稳定的类型 id。</summary>
        public const string Id = "RemapAttribute";

        // 参数下标。顺序是存档图格式的一部分，只能往后追加。
        private const int PSourceName = 0;
        private const int PTargetName = 1;
        private const int PInMin = 2;
        private const int PInMax = 3;
        private const int POutMin = 4;
        private const int POutMax = 5;
        private const int PClamp = 6;

        /// <summary>本节点读取的参数个数。</summary>
        public const int ParameterCount = 7;

        private readonly string _typeId;
        private readonly PCGNodeParameters _params;

        /// <summary>读的属性列；为空表示读点自己的密度。</summary>
        public string SourceName = string.Empty;

        /// <summary>写的属性列；为空表示写点的密度。</summary>
        public string TargetName = string.Empty;

        /// <summary>输入区间的下界。</summary>
        public float InMin;

        /// <summary>输入区间的上界。</summary>
        public float InMax = 1.0f;

        /// <summary>下界映到的那个值。</summary>
        public float OutMin;

        /// <summary>上界映到的那个值。</summary>
        public float OutMax = 1.0f;

        /// <summary>置位时，结果被留在输出区间之内。</summary>
        public bool Clamp = true;

        /// <summary>用序列化下来的参数构造节点。</summary>
        public PCGRemapAttributeNode(PCGNodeRegistry.PCGNodeSerializationData data)
        {
            _typeId = Id;
            _params = new PCGNodeParameters(data.Values, data.TypeId);

            SourceName = _params.Get(PSourceName, string.Empty) ?? string.Empty;
            TargetName = _params.Get(PTargetName, string.Empty) ?? string.Empty;
            InMin = _params.Get(PInMin, 0.0f);
            InMax = _params.Get(PInMax, 1.0f);
            OutMin = _params.Get(POutMin, 0.0f);
            OutMax = _params.Get(POutMax, 1.0f);
            Clamp = _params.Get(PClamp, true);
        }

        /// <inheritdoc />
        public override string TypeId => _typeId;

        /// <inheritdoc />
        public override string DisplayName => "Remap";

        /// <inheritdoc />
        public override bool IsThreadSafe => true;

        /// <inheritdoc />
        public override PCGPinDefinition[] InputPins => new[]
        {
            new PCGPinDefinition("In", PCGPinDirection.Input, isRequired: true, tooltip: "Points to remap."),
        };

        /// <inheritdoc />
        public override PCGPinDefinition[] OutputPins => new[]
        {
            new PCGPinDefinition("Out", PCGPinDirection.Output, tooltip: "The same points with the remapped value."),
        };

        /// <inheritdoc />
        public override string DescribeParameters()
        {
            string source = string.IsNullOrEmpty(SourceName) ? "$Density" : SourceName;
            string target = string.IsNullOrEmpty(TargetName) ? "$Density" : TargetName;
            return $"{source} [{InMin:F2},{InMax:F2}] -> {target} [{OutMin:F2},{OutMax:F2}], clamp={Clamp}";
        }

        /// <inheritdoc />
        public override PCGData OnExecute(PCGExecutionContext context, PCGData[] inputs)
        {
            var input = inputs[0];
            if (input == null || input.Count == 0)
                return PCGData.Empty;

            var result = input.Clone();
            var attributes = result.SharedAttributes;
            bool readAttribute = !string.IsNullOrEmpty(SourceName);
            bool writeAttribute = !string.IsNullOrEmpty(TargetName);

            if (readAttribute && (attributes == null || !attributes.Contains(SourceName)))
            {
                // 一列没人写过，说明图接在自己的采样器前面：说一次，然后把这一层
                // 原样递出去，而不是悄悄把每个密度都压成同一个值。
                LastError = $"Remap could not read '{SourceName}': no such attribute on this layer.";
                return result;
            }

            if (writeAttribute)
            {
                if (attributes == null)
                {
                    attributes = new PCGAttributeSet(result.Count);
                    result.SharedAttributes = attributes;

                    for (int i = 0; i < result.Points.Length; i++)
                        result.Points[i].Attributes = attributes;
                }

                if (!attributes.Contains(TargetName))
                    attributes.DeclareAttributeUnsafe(TargetName, PCGAttributeType.Float);
            }

            float span = InMax - InMin;
            var column = writeAttribute ? attributes.GetWritableFloatArray(TargetName) : null;
            var points = result.Points;

            for (int i = 0; i < points.Length; i++)
            {
                context.ThrowIfCancelled(i);

                float value = readAttribute ? attributes.GetFloat(SourceName, i) : points[i].Density;

                // 输入区间被压平时内部是空的：每个值都归给它最近的那一端，这样一张
                // 没标定好的图仍然读得懂，而不是产出一堆 NaN。
                float t = Math.Abs(span) < 1e-6f
                    ? (value >= InMin ? 1.0f : 0.0f)
                    : (value - InMin) / span;

                if (Clamp)
                    t = Math.Min(1.0f, Math.Max(0.0f, t));

                float mapped = OutMin + t * (OutMax - OutMin);

                if (writeAttribute)
                    column[i] = mapped;
                else
                    points[i].Density = mapped;
            }

            return result;
        }
    }
}

// PCG 插件 - 属性算术（Attribute ops）
// 属于 FlaxEngine PCG 插件。见 docs/PCG_PLUGIN_DESIGN.md §5.1.11。

using System;
using FlaxEngine;
using PCG.Execution;

namespace PCG.Nodes
{
    /// <summary>
    /// 把两个数——通常是两列属性——逐点合成一个。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 有 <see cref="PCGSetAttributeNode"/> 把列写出来、这个节点把它们合起来，一张图就能
    /// 用零件拼出一条生成规则：坡度列、噪声列与采样器自己的密度相乘成一个
    /// 概率，而每一件零件在编辑器里仍然看得见。UE5 用它的属性算术
    /// 节点表达同一件事。
    /// </para>
    /// <para>
    /// 两边都可以是点自己的密度（名字留空），也可以是一个常量，这样常见写法
    /// （"全部减半"、"两列相乘"）就不必为了放一个数再多挂一个节点。
    /// </para>
    /// <para>
    /// 默认钳到 0..1，因为这里的主用途是概率算术：不钳位的两列相乘
    /// 正是密度变成 3.7 的来路，而生成器把密度当概率读。
    /// 关掉钳位，留给"结果是距离、高度或个数"那些场合。
    /// </para>
    /// </remarks>
    public sealed class PCGAttributeMathNode : PCGNodeBase
    {
        /// <summary>稳定的类型 id。</summary>
        public const string Id = "AttributeMath";

        // 参数下标。顺序是存档图格式的一部分，只能往后追加。
        private const int POperation = 0;
        private const int PLeftAttribute = 1;
        private const int PRightAttribute = 2;
        private const int PRightConstant = 3;
        private const int PTargetAttribute = 4;
        private const int PClamp = 5;

        /// <summary>本节点读取的参数个数。</summary>
        public const int ParameterCount = 6;

        /// <summary>两个数怎么合。</summary>
        public enum MathOperation
        {
            Add = 0,
            Subtract = 1,
            Multiply = 2,
            Divide = 3,
            Min = 4,
            Max = 5,
        }

        private readonly string _typeId;
        private readonly PCGNodeParameters _params;

        /// <summary>施加哪一种运算。</summary>
        public MathOperation Operation = MathOperation.Multiply;

        /// <summary>左操作数的列；为空表示读点的密度。</summary>
        public string LeftAttribute = string.Empty;

        /// <summary>右操作数的列；为空表示用下面那个常量。</summary>
        public string RightAttribute = string.Empty;

        /// <summary>没写右侧列时使用的值。</summary>
        public float RightConstant = 1.0f;

        /// <summary>结果写进哪一列；为空表示写点的密度。</summary>
        public string TargetAttribute = string.Empty;

        /// <summary>置位时结果被留在 0..1 之内——概率需要的就是这个。</summary>
        public bool Clamp = true;

        /// <summary>用序列化下来的参数构造节点。</summary>
        public PCGAttributeMathNode(PCGNodeRegistry.PCGNodeSerializationData data)
        {
            _typeId = Id;
            _params = new PCGNodeParameters(data.Values, data.TypeId);

            int operation = _params.Get(POperation, (int)MathOperation.Multiply);
            Operation = (MathOperation)Math.Min((int)MathOperation.Max, Math.Max(0, operation));
            LeftAttribute = _params.Get(PLeftAttribute, string.Empty) ?? string.Empty;
            RightAttribute = _params.Get(PRightAttribute, string.Empty) ?? string.Empty;
            RightConstant = _params.Get(PRightConstant, 1.0f);
            TargetAttribute = _params.Get(PTargetAttribute, string.Empty) ?? string.Empty;
            Clamp = _params.Get(PClamp, true);
        }

        /// <inheritdoc />
        public override string TypeId => _typeId;

        /// <inheritdoc />
        public override string DisplayName => "Attribute Math";

        /// <inheritdoc />
        public override bool IsThreadSafe => true;

        /// <inheritdoc />
        public override PCGPinDefinition[] InputPins => new[]
        {
            new PCGPinDefinition("In", PCGPinDirection.Input, isRequired: true, tooltip: "Points to combine onto."),
        };

        /// <inheritdoc />
        public override PCGPinDefinition[] OutputPins => new[]
        {
            new PCGPinDefinition("Out", PCGPinDirection.Output, tooltip: "The same points with the combined value."),
        };

        /// <inheritdoc />
        public override string DescribeParameters()
        {
            string left = string.IsNullOrEmpty(LeftAttribute) ? "$Density" : LeftAttribute;
            string right = string.IsNullOrEmpty(RightAttribute) ? RightConstant.ToString("0.##") : RightAttribute;
            string target = string.IsNullOrEmpty(TargetAttribute) ? "$Density" : TargetAttribute;
            return $"{target} = {left} {Operation} {right}, clamp={Clamp}";
        }

        /// <inheritdoc />
        public override PCGData OnExecute(PCGExecutionContext context, PCGData[] inputs)
        {
            var input = inputs[0];
            if (input == null || input.Count == 0)
                return PCGData.Empty;

            var result = input.Clone();
            var attributes = result.SharedAttributes;
            bool writeAttribute = !string.IsNullOrEmpty(TargetAttribute);
            bool leftIsColumn = !string.IsNullOrEmpty(LeftAttribute);
            bool rightIsColumn = !string.IsNullOrEmpty(RightAttribute);

            if ((leftIsColumn && (attributes == null || !attributes.Contains(LeftAttribute))) ||
                (rightIsColumn && (attributes == null || !attributes.Contains(RightAttribute))))
            {
                // 一列被点了名却没人写过，说明图只接了一半。把它当零读会安静地
                // 把整层清零，所以这里选择报出来，并原样把这一层递出去。
                LastError = $"Attribute Math could not read " +
                            $"{(leftIsColumn && (attributes == null || !attributes.Contains(LeftAttribute)) ? LeftAttribute : RightAttribute)}" +
                            ": no such attribute on this layer.";
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

                if (!attributes.Contains(TargetAttribute))
                    attributes.DeclareAttributeUnsafe(TargetAttribute, PCGAttributeType.Float);
            }

            var column = writeAttribute ? attributes.GetWritableFloatArray(TargetAttribute) : null;
            var points = result.Points;
            bool dividedByZero = false;

            for (int i = 0; i < points.Length; i++)
            {
                context.ThrowIfCancelled(i);

                float left = leftIsColumn ? attributes.GetFloat(LeftAttribute, i) : points[i].Density;
                float right = rightIsColumn ? attributes.GetFloat(RightAttribute, i) : RightConstant;
                float value;

                if (Operation == MathOperation.Divide && Math.Abs(right) < 1e-6f)
                {
                    // 除以一个为零的列是图的错误，但 NaN 的密度会毒害后面每一级；
                    // 零是让世界仍然读得懂的答案，而且只报一次。
                    value = 0.0f;
                    dividedByZero = true;
                }
                else
                {
                    value = Operation switch
                    {
                        MathOperation.Add => left + right,
                        MathOperation.Subtract => left - right,
                        MathOperation.Divide => left / right,
                        MathOperation.Min => Math.Min(left, right),
                        MathOperation.Max => Math.Max(left, right),
                        _ => left * right,
                    };
                }

                if (Clamp)
                    value = Math.Min(1.0f, Math.Max(0.0f, value));

                if (writeAttribute)
                    column[i] = value;
                else
                    points[i].Density = value;
            }

            if (dividedByZero)
                LastError = "Attribute Math divided by a zero-valued column; those points were set to 0.";

            return result;
        }
    }
}

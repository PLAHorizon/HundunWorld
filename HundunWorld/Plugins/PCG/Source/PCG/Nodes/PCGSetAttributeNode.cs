// PCG 插件 - 写属性（属性运算）
// FlaxEngine PCG 插件的一部分。见 docs/PCG_PLUGIN_DESIGN.md §5.1.7。

using System;
using FlaxEngine;
using PCG.Execution;

namespace PCG.Nodes
{
    /// <summary>
    /// 把一个值以具名属性的形式发布到每一个点上，好让后续阶段按它过滤或生成。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 插件自己的过滤器算出一些值（坡度、高度、距离）然后又把它们丢掉；属性就是
    /// 一张图说"这个数留着"的方式。设计者于是可以把一个坡度阈值接线一次、
    /// 把那一列复用去做密度重映射、并在编辑器里读到同一个值——
    /// 而不是每个节点各算一遍、算出彼此略有出入的答案。
    /// </para>
    /// <para>
    /// 写入经 <see cref="PCGData.Clone"/> 加上属性集合的"脱离共享"访问器完成，
    /// 这就是插件的写时复制约定：交回来供写入的那份数组专属于本节点的产出，
    /// 所以无论是什么产出了输入，都不会看到自己底下多出了新的一列。
    /// </para>
    /// </remarks>
    public sealed class PCGSetAttributeNode : PCGNodeBase
    {
        /// <summary>稳定的类型 id。</summary>
        public const string Id = "SetAttribute";

        // 参数下标。顺序属于存档图格式，只能追加。
        private const int PName = 0;
        private const int PSource = 1;
        private const int PConstant = 2;
        private const int PScale = 3;
        private const int PBias = 4;
        private const int PSourceName = 5;

        /// <summary>本节点读取的参数个数。</summary>
        public const int ParameterCount = 6;

        /// <summary>在施加缩放与偏置之前，被写的值来自哪里。</summary>
        public enum ValueSource
        {
            Constant = 0,
            Density = 1,
            PositionX = 2,
            PositionY = 3,
            PositionZ = 4,
            Attribute = 5,
        }

        private readonly string _typeId;
        private readonly PCGNodeParameters _params;

        /// <summary>被写入的属性名。</summary>
        public string Name = string.Empty;

        /// <summary>写入哪一个值。</summary>
        public ValueSource Source = ValueSource.Constant;

        /// <summary><see cref="ValueSource.Constant"/> 使用的值。</summary>
        public float Constant = 1.0f;

        /// <summary>在偏置之前施加的乘数。</summary>
        public float Scale = 1.0f;

        /// <summary>在缩放之后加上的偏移。</summary>
        public float Bias;

        /// <summary>当 <see cref="Source"/> 是 <see cref="ValueSource.Attribute"/> 时读取的属性。</summary>
        public string SourceName = string.Empty;

        /// <summary>由序列化的参数创建节点。</summary>
        public PCGSetAttributeNode(PCGNodeRegistry.PCGNodeSerializationData data)
        {
            _typeId = Id;
            _params = new PCGNodeParameters(data.Values, data.TypeId);

            Name = _params.Get(PName, string.Empty) ?? string.Empty;
            int source = _params.Get(PSource, (int)ValueSource.Constant);
            Source = (ValueSource)Math.Min((int)ValueSource.Attribute, Math.Max(0, source));
            Constant = _params.Get(PConstant, 1.0f);
            Scale = _params.Get(PScale, 1.0f);
            Bias = _params.Get(PBias, 0.0f);
            SourceName = _params.Get(PSourceName, string.Empty) ?? string.Empty;
        }

        /// <inheritdoc />
        public override string TypeId => _typeId;

        /// <inheritdoc />
        public override string DisplayName => "Set Attribute";

        /// <inheritdoc />
        public override bool IsThreadSafe => true;

        /// <inheritdoc />
        public override PCGPinDefinition[] InputPins => new[]
        {
            new PCGPinDefinition("In", PCGPinDirection.Input, isRequired: true,
                tooltip: "Points the attribute is written onto."),
        };

        /// <inheritdoc />
        public override PCGPinDefinition[] OutputPins => new[]
        {
            new PCGPinDefinition("Out", PCGPinDirection.Output,
                tooltip: "The same points, each carrying the new column."),
        };

        /// <inheritdoc />
        public override string DescribeParameters() =>
            Source == ValueSource.Attribute
                ? $"{Name} = '{SourceName}' * {Scale:F2} + {Bias:F2} (attribute)"
                : $"{Name} = {Source} * {Scale:F2} + {Bias:F2}";

        /// <inheritdoc />
        public override PCGData OnExecute(PCGExecutionContext context, PCGData[] inputs)
        {
            var input = inputs[0];
            if (input == null || input.Count == 0)
                return PCGData.Empty;

            // 通过 LastError 按名字拒绝，而不是用 Debug：执行器会把它收进这次运行的
            // 警告里，而这条路径从离线 harness 也仍然可达。
            if (string.IsNullOrEmpty(Name))
            {
                LastError = "Set Attribute needs an attribute name; the input passes through unchanged.";
                return input.Clone();
            }

            var result = input.Clone();
            var attributes = EnsureAttributes(result);
            bool readAttribute = Source == ValueSource.Attribute && !string.IsNullOrEmpty(SourceName);

            if (Source == ValueSource.Attribute && !readAttribute)
            {
                LastError = "Set Attribute is set to read an attribute but no source name was given.";
                return result;
            }

            if (!attributes.Contains(Name))
                attributes.DeclareAttributeUnsafe(Name, PCGAttributeType.Float);

            var column = attributes.GetWritableFloatArray(Name);
            var points = result.Points;

            for (int i = 0; i < points.Length; i++)
            {
                context.ThrowIfCancelled(i);

                float value = Source switch
                {
                    ValueSource.Density => points[i].Density,
                    ValueSource.PositionX => points[i].Position.X,
                    ValueSource.PositionY => points[i].Position.Y,
                    ValueSource.PositionZ => points[i].Position.Z,
                    ValueSource.Attribute => attributes.Contains(SourceName)
                        ? attributes.GetFloat(SourceName, i)
                        : 0.0f,
                    _ => Constant,
                };

                column[i] = value * Scale + Bias;
            }

            return result;
        }

        /// <summary>这份数据的属性存储；缺失时创建它并挂到它的点上。</summary>
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

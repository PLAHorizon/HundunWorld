// PCG 插件 - 通道转属性（点运算）
// 属于 FlaxEngine PCG 插件。见 docs/PCG_PLUGIN_DESIGN.md §5.1.20。

using System;
using FlaxEngine;
using PCG.Execution;

namespace PCG.Nodes
{
    /// <summary>
    /// 把点自己的向量通道里的某个数（有多大、朝哪）写进一列。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 本插件已经能把点的位置与密度当属性读（<see cref="PCGSetAttributeNode"/>），
    /// 但它的<i>大小</i>与<i>朝向</i>没有出口。这有影响：“只留立着的
    /// 道具”“按这块石头多大排序”“把小碎石去掉”，这些都是
    /// 关于一些目前只存在点结构体内的数的过滤与重映射问题。这就是 UE5 的 BreakVector / GetComponent
    /// 那一家子搬到点的通道上。
    /// </para>
    /// <para>
    /// 方向经 <see cref="Vector3.Transform(Vector3, Quaternion)"/>，与
    /// <see cref="PCGPointTransform.SlopeDegrees"/> 完全一致，所以本节点读的朝向与
    /// 坡度过滤器看到的同一个——没有第二套约定会打架。刻意不提供欧拉角：
    /// 四元数的分解不唯一，所以“这个点的俯仰”会是一个两套正确实现
    /// 都可能不一致的数，而“它朝上偏了多少”不会。
    /// </para>
    /// </remarks>
    public sealed class PCGChannelToAttributeNode : PCGNodeBase
    {
        /// <summary>稳定的类型 id。</summary>
        public const string Id = "ChannelToAttribute";

        // 参数下标。顺序是已存图格式的一部分；只许追加。
        private const int PAttribute = 0;
        private const int PChannel = 1;
        private const int PComponent = 2;

        /// <summary>本节点读取的参数个数。</summary>
        public const int ParameterCount = 3;

        /// <summary>没有反序列化值时建节点所写的列。</summary>
        public const string DefaultAttribute = "value";

        /// <summary>读点上的哪条向量通道。</summary>
        public enum Channel
        {
            /// <summary>点的等比例缩放（它的 <c>Scale</c> 字段）。</summary>
            Scale = 0,

            /// <summary>点包围盒的半长。</summary>
            Bounds = 1,

            /// <summary>点的局部 +Z 在世界空间里朝的方向。</summary>
            Forward = 2,

            /// <summary>点的局部 +Y 在世界空间里朝的方向。</summary>
            Up = 3,

            /// <summary>点的局部 +X 在世界空间里朝的方向。</summary>
            Right = 4,
        }

        /// <summary>写通道的哪一部分。</summary>
        public enum Component
        {
            X = 0,
            Y = 1,
            Z = 2,

            /// <summary>通道的模长。对 <see cref="Channel.Scale"/> 来说就是整体大小。</summary>
            Length = 3,
        }

        private readonly string _typeId;
        private readonly PCGNodeParameters _params;

        /// <summary>值写进哪一列。留空则写点的密度。</summary>
        public string AttributeName = DefaultAttribute;

        /// <summary>读哪条通道。</summary>
        public Channel Source = Channel.Scale;

        /// <summary>写通道的哪一部分。</summary>
        public Component Part = Component.Length;

        /// <summary>用反序列化出来的参数建节点。</summary>
        public PCGChannelToAttributeNode(PCGNodeRegistry.PCGNodeSerializationData data)
        {
            _typeId = Id;
            _params = new PCGNodeParameters(data.Values, data.TypeId);

            AttributeName = _params.Get(PAttribute, DefaultAttribute) ?? string.Empty;
            Source = _params.Get(PChannel, Channel.Scale);
            Part = _params.Get(PComponent, Component.Length);
        }

        /// <inheritdoc />
        public override string TypeId => _typeId;

        /// <inheritdoc />
        public override string DisplayName => "Channel To Attribute";

        /// <inheritdoc />
        public override bool IsThreadSafe => true;

        /// <inheritdoc />
        public override PCGPinDefinition[] InputPins => new[]
        {
            new PCGPinDefinition("In", PCGPinDirection.Input, isRequired: true,
                tooltip: "Points the value is read from and written onto."),
        };

        /// <inheritdoc />
        public override PCGPinDefinition[] OutputPins => new[]
        {
            new PCGPinDefinition("Out", PCGPinDirection.Output,
                tooltip: "The same points, each carrying the requested number."),
        };

        /// <inheritdoc />
        public override string DescribeParameters()
        {
            string name = string.IsNullOrEmpty(AttributeName) ? PCGAttributeSet.DensityName : AttributeName;
            return $"{name} = {Source}.{Part}";
        }

        /// <inheritdoc />
        public override PCGData OnExecute(PCGExecutionContext context, PCGData[] inputs)
        {
            var input = inputs != null && inputs.Length > 0 ? inputs[0] : null;
            if (input == null || input.Count == 0)
                return PCGData.Empty;

            bool toDensity = string.IsNullOrEmpty(AttributeName) ||
                             string.Equals(AttributeName, PCGAttributeSet.DensityName, StringComparison.Ordinal);

            if (toDensity)
            {
                var result = input.Clone();
                var targetPoints = result.Points;

                for (int i = 0; i < targetPoints.Length; i++)
                {
                    context.ThrowIfCancelled(i);
                    targetPoints[i].Density = Read(targetPoints[i]);
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
                column[i] = Read(points[i]);
            }

            return cloned;
        }

        /// <summary>某个点上、所请求通道的所请求分量。</summary>
        private float Read(in PCGPoint point)
        {
            var vector = Source switch
            {
                Channel.Bounds => PCGPointTransform.HalfExtents(point.Bounds),
                Channel.Forward => Vector3.Transform(new Vector3(0.0f, 0.0f, 1.0f), point.Rotation),
                Channel.Up => Vector3.Transform(new Vector3(0.0f, 1.0f, 0.0f), point.Rotation),
                Channel.Right => Vector3.Transform(new Vector3(1.0f, 0.0f, 0.0f), point.Rotation),
                _ => new Vector3(point.Scale.X, point.Scale.Y, point.Scale.Z),
            };

            return Part switch
            {
                Component.X => vector.X,
                Component.Y => vector.Y,
                Component.Z => vector.Z,
                _ => vector.Length,
            };
        }

        /// <summary>数据集里的属性存储；缺失时创建并盖到它的点上。</summary>
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

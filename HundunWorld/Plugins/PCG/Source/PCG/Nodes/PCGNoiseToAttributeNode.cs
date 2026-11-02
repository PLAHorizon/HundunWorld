// PCG 插件 - 噪声写属性（点运算）
// FlaxEngine PCG 插件的一部分。见 docs/PCG_PLUGIN_DESIGN.md §5.1.19。

using System;
using FlaxEngine;
using PCG.Execution;

namespace PCG.Nodes
{
    /// <summary>
    /// 把带种子噪声场在每个点上的值写进一列——散点需要的是那种变化，
    /// 而不必把散点器换成另一个。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="PCGNoiseSamplerNode"/> 回答"噪声在哪里密"的方式是自己在格点上产出点。
    /// 一旦点已经存在、而且来自别处——地形散点、模型表面、手摆的一排——它就是错的工具，
    /// 因为采样器会把这些点丢掉、交回它自己产的那些。
    /// 这个节点留下输入、只把那个数加上，设计者说"让物件成片的稀下去"时要的就是这个。
    /// 也就是 UE5 的"空间噪声写进属性"那个模式。
    /// </para>
    /// <para>
    /// 它采样插件自己的 <see cref="PCGSeededNoise"/>，理由和采样器那条一样：
    /// 引擎的噪声没有种子，于是两张不同种子的图会产出同一个场、重掷种子静默无效
    /// （docs/PCG_PLUGIN_DESIGN.md §3.4）。种子取图的种子加上本节点的偏移量，
    /// 所以同一张图里两个噪声节点互不相关，而不用去动图的种子。
    /// </para>
    /// <para>
    /// 每种形态都返回 [0,1] 里的值，所以那一列可以直接当权重用。平面模式只采样 XZ
    /// （林地的地面不该跟着地形的起伏重掷）；关掉它才把高度算进去。
    /// </para>
    /// </remarks>
    public sealed class PCGNoiseToAttributeNode : PCGNodeBase
    {
        /// <summary>稳定的类型 id。</summary>
        public const string Id = "NoiseToAttribute";

        // 参数下标。顺序是存档图格式的一部分，只能往后追加。
        private const int PAttribute = 0;
        private const int PKind = 1;
        private const int PFrequency = 2;
        private const int POctaves = 3;
        private const int PSeedOffset = 4;
        private const int PPlaneOnly = 5;

        /// <summary>本节点读取的参数个数。</summary>
        public const int ParameterCount = 6;

        /// <summary>节点在没有序列化值的情况下被构造时，噪声写进哪一列。</summary>
        public const string DefaultAttribute = "noise";

        private readonly string _typeId;
        private readonly PCGNodeParameters _params;

        /// <summary>噪声值写进的列。留空则写点的密度。</summary>
        public string AttributeName = DefaultAttribute;

        /// <summary>采样哪一个带种子的噪声场。</summary>
        public PCGNoiseKind Kind = PCGNoiseKind.Perlin2D;

        /// <summary>噪声频率，单位是每世界单位多少个周期。</summary>
        public float Frequency = 0.01f;

        /// <summary>分形倍频数；1 就是单带的普通采样。</summary>
        public int Octaves = 1;

        /// <summary>加在图的种子上，让两个噪声节点不要想到一起。</summary>
        public int SeedOffset;

        /// <summary>置位时只采样 XZ，于是高度不改变取值。</summary>
        public bool PlaneOnly = true;

        /// <summary>用序列化参数构造节点。</summary>
        public PCGNoiseToAttributeNode(PCGNodeRegistry.PCGNodeSerializationData data)
        {
            _typeId = Id;
            _params = new PCGNodeParameters(data.Values, data.TypeId);

            AttributeName = _params.Get(PAttribute, DefaultAttribute) ?? string.Empty;
            Kind = _params.Get(PKind, PCGNoiseKind.Perlin2D);
            Frequency = Math.Max(1e-6f, _params.Get(PFrequency, 0.01f));
            Octaves = Math.Max(1, Math.Min(8, _params.Get(POctaves, 1)));
            SeedOffset = _params.Get(PSeedOffset, 0);
            PlaneOnly = _params.Get(PPlaneOnly, true);
        }

        /// <inheritdoc />
        public override string TypeId => _typeId;

        /// <inheritdoc />
        public override string DisplayName => "Noise To Attribute";

        /// <inheritdoc />
        public override bool IsThreadSafe => true;

        /// <inheritdoc />
        public override PCGPinDefinition[] InputPins => new[]
        {
            new PCGPinDefinition("In", PCGPinDirection.Input, isRequired: true,
                tooltip: "Points the noise value is written onto."),
        };

        /// <inheritdoc />
        public override PCGPinDefinition[] OutputPins => new[]
        {
            new PCGPinDefinition("Out", PCGPinDirection.Output,
                tooltip: "The same points, each carrying the noise value at its position."),
        };

        /// <inheritdoc />
        public override string DescribeParameters()
        {
            string name = string.IsNullOrEmpty(AttributeName) ? PCGAttributeSet.DensityName : AttributeName;
            return $"{name} = {Kind} at each point, frequency={Frequency:E2}, octaves={Octaves}, " +
                   $"seedOffset={SeedOffset}, planeOnly={PlaneOnly}";
        }

        /// <inheritdoc />
        public override PCGData OnExecute(PCGExecutionContext context, PCGData[] inputs)
        {
            var input = inputs != null && inputs.Length > 0 ? inputs[0] : null;
            if (input == null || input.Count == 0)
                return PCGData.Empty;

            bool toDensity = string.IsNullOrEmpty(AttributeName) ||
                             string.Equals(AttributeName, PCGAttributeSet.DensityName, StringComparison.Ordinal);

            int seed = context.Seed + SeedOffset;

            if (toDensity)
            {
                var result = input.Clone();
                var targetPoints = result.Points;

                for (int i = 0; i < targetPoints.Length; i++)
                {
                    context.ThrowIfCancelled(i);
                    targetPoints[i].Density = Value(targetPoints[i].Position, seed);
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
                column[i] = Value(points[i].Position, seed);
            }

            return cloned;
        }

        /// <summary>噪声场在 <paramref name="position"/> 处的取值，范围 [0,1]。</summary>
        private float Value(in Vector3 position, int seed)
        {
            return PlaneOnly
                ? PCGSeededNoise.Sample2D(Kind, position.X, position.Z, seed, Frequency, Octaves)
                : PCGSeededNoise.Sample3D(Kind, position.X, position.Y, position.Z, seed, Frequency, Octaves);
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

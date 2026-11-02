// PCG 插件 - 包围盒 modifier
// FlaxEngine PCG 插件的一部分。见 docs/PCG_PLUGIN_DESIGN.md §5.1.4。

using System;
using FlaxEngine;
using PCG.Execution;

namespace PCG.Nodes
{
    /// <summary>
    /// 设定、撑大或缩放每个点带着的那个盒子——自剪枝按它来量间距，
    /// 生成器也按它来摆物件。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 点的包围盒是本插件衡量"一件物件要占多大地方"的单位，所以这个节点是把这句话讲老实的写法：
    /// 一片小石头的散点想要比采样器猜的更小的盒子，好让它们能挨着站；
    /// 树一层想要更大的盒子，好让剪枝把树冠彼此隔开。没有它，间距就是采样器
    /// 顺手写下的那个数，剩下唯一能拧的旋钮只有生成器的模型。
    /// </para>
    /// <para>
    /// 盒子不会塌成没有：半尺寸有一个小 epsilon 作下限，因为零盒子是一个没有体积的点，
    /// 剪枝会让所有东西都跟它重叠，而距离测试会把它当成退化情形，
    /// 不会把它当成作者做过的一次决定。
    /// </para>
    /// </remarks>
    public sealed class PCGBoundsModifierNode : PCGNodeBase
    {
        /// <summary>稳定的类型 id。</summary>
        public const string Id = "BoundsModifier";

        // 参数下标。顺序是存档图格式的一部分，只能往后追加。
        private const int PMode = 0;
        private const int PExtents = 1;
        private const int PScale = 2;

        /// <summary>本节点读取的参数个数。</summary>
        public const int ParameterCount = 3;

        /// <summary>半尺寸怎么被施加。</summary>
        public enum BoundsMode
        {
            /// <summary>半尺寸整个替换掉。</summary>
            Set = 0,

            /// <summary>半尺寸按给出的量增大（负值就是缩小）。</summary>
            Add = 1,

            /// <summary>半尺寸乘上一个倍数。</summary>
            Scale = 2,
        }

        /// <summary>一个盒子最终允许的最小半尺寸。</summary>
        public const float MinHalfExtent = 1e-3f;

        private readonly string _typeId;
        private readonly PCGNodeParameters _params;

        /// <summary>三种操作里用哪一种。</summary>
        public BoundsMode Mode = BoundsMode.Set;

        /// <summary><see cref="BoundsMode.Set"/> 与 <see cref="BoundsMode.Add"/> 用的半尺寸。</summary>
        public Vector3 Extents = new Vector3(100.0f, 100.0f, 100.0f);

        /// <summary><see cref="BoundsMode.Scale"/> 用的倍数。</summary>
        public float Scale = 1.0f;

        /// <summary>用序列化参数构造节点。</summary>
        public PCGBoundsModifierNode(PCGNodeRegistry.PCGNodeSerializationData data)
        {
            _typeId = Id;
            _params = new PCGNodeParameters(data.Values, data.TypeId);

            int mode = _params.Get(PMode, (int)BoundsMode.Set);
            Mode = (BoundsMode)Math.Min((int)BoundsMode.Scale, Math.Max(0, mode));
            Extents = _params.GetVector3(PExtents, new Vector3(100.0f, 100.0f, 100.0f));
            Scale = Math.Max(0.0f, _params.Get(PScale, 1.0f));
        }

        /// <inheritdoc />
        public override string TypeId => _typeId;

        /// <inheritdoc />
        public override string DisplayName => "Bounds Modifier";

        /// <inheritdoc />
        public override bool IsThreadSafe => true;

        /// <inheritdoc />
        public override PCGPinDefinition[] InputPins => new[]
        {
            new PCGPinDefinition("In", PCGPinDirection.Input, isRequired: true,
                tooltip: "Points whose boxes are set, grown or scaled."),
        };

        /// <inheritdoc />
        public override PCGPinDefinition[] OutputPins => new[]
        {
            new PCGPinDefinition("Out", PCGPinDirection.Output,
                tooltip: "The same points with the box every later stage measures by."),
        };

        /// <inheritdoc />
        public override string DescribeParameters() => Mode switch
        {
            BoundsMode.Set => $"mode=set, extents={Extents}",
            BoundsMode.Add => $"mode=add, extents={Extents}",
            _ => $"mode=scale, factor={Scale:F2}",
        };

        /// <inheritdoc />
        public override PCGData OnExecute(PCGExecutionContext context, PCGData[] inputs)
        {
            var input = inputs[0];
            if (input == null || input.Count == 0)
                return PCGData.Empty;

            // 用克隆而不是新搭一份：盒子要变，但点上那些属性与种子是下游要读的东西，
            // 而 PCGData.Create 会把属性集合丢在后面。
            var result = input.Clone();
            var points = input.Points;

            for (int i = 0; i < points.Length; i++)
            {
                context.ThrowIfCancelled(i);

                var point = points[i];
                var half = PCGPointTransform.HalfExtents(point.Bounds);

                var modified = Mode switch
                {
                    BoundsMode.Add => half + Extents,
                    BoundsMode.Scale => half * Scale,
                    _ => Extents,
                };

                // 是取下限，不是取绝对值：内缩得比盒子还大，是作者在说"能多小就多小"，
                // 而取幅值会还回一个跟内缩量一样大的盒子——
                // -1000000 就给你一公里宽。这个下限是本节点施加的唯一钳制；
                // 盒子要大是作者自己的事。
                point.Bounds = PCGPointTransform.MakeBox(point.Position, new Vector3(
                    Math.Max(modified.X, MinHalfExtent),
                    Math.Max(modified.Y, MinHalfExtent),
                    Math.Max(modified.Z, MinHalfExtent)));

                result.Points[i] = point;
            }

            return result;
        }
    }
}

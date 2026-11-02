// PCG 插件 - 区域过滤器（筛选器）
// 属于 FlaxEngine PCG 插件。见 docs/PCG_PLUGIN_DESIGN.md §5.1.41。

using System;
using System.Collections.Generic;
using FlaxEngine;
using PCG.Execution;

namespace PCG.Nodes
{
    /// <summary>
    /// 按**水平面上的一个矩形**保留点：X 与 Z 各一段闭区间，两轴都在区间内才算"在这片区域里"。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 插件此前能按高度（<c>HeightFilter</c>）与到某点的距离（<c>DistanceFilter</c>）筛点，却没有任何办法说
    /// "只要西南角那一片"。而"按设定描述生成"这句话里，**位置**是最常出现的一类信息：北部是针叶林、南边是
    /// 草原、西侧那片湖周围种芦苇。缺了这一步，描述只能决定"长什么"，决定不了"长在哪"——每一层都会铺满
    /// 整个生成范围。
    /// </para>
    /// <para>
    /// 为什么是矩形而不是圆：<c>DistanceFilter</c> 已经是圆了（半径），而"北半部""西南角""这块地"这类说法
    /// 在矩形下才是**精确**的，在圆下只能是近似。两个轴各一段区间也正好是描述语言能写清楚的形状。
    /// </para>
    /// <para>
    /// 只看 X 与 Z：高度由地形与 <c>HeightFilter</c> 管，把 Y 混进来只会让"这片区域"变成一个盒子，而写描述的人
    /// 脑子里想的是俯视图。范围是**闭区间**（与其余的筛选器一致），两轴写反了在构造时就换过来，免得一个
    /// 写反的区间静默地把整层清空。
    /// </para>
    /// </remarks>
    public sealed class PCGAreaFilterNode : PCGNodeBase
    {
        /// <summary>稳定的类型 id。</summary>
        public const string Id = "AreaFilter";

        // 参数下标。顺序是存档格式的一部分；只能往后追加。
        private const int PMinX = 0;
        private const int PMaxX = 1;
        private const int PMinZ = 2;
        private const int PMaxZ = 3;
        private const int PInvert = 4;

        /// <summary>这个节点读取的参数个数。</summary>
        public const int ParameterCount = 5;

        private readonly string _typeId;
        private readonly PCGNodeParameters _params;

        /// <summary>X 方向闭区间的下界。</summary>
        public float MinX = float.NegativeInfinity;

        /// <summary>X 方向闭区间的上界。</summary>
        public float MaxX = float.PositiveInfinity;

        /// <summary>Z 方向闭区间的下界。</summary>
        public float MinZ = float.NegativeInfinity;

        /// <summary>Z 方向闭区间的上界。</summary>
        public float MaxZ = float.PositiveInfinity;

        /// <summary>置位时反过来：只留区域**以外**的点。</summary>
        public bool Invert;

        /// <summary>按序列化参数创建节点。</summary>
        public PCGAreaFilterNode(PCGNodeRegistry.PCGNodeSerializationData data)
        {
            _typeId = Id;
            _params = new PCGNodeParameters(data.Values, data.TypeId);

            MinX = _params.Get(PMinX, float.NegativeInfinity);
            MaxX = _params.Get(PMaxX, float.PositiveInfinity);
            MinZ = _params.Get(PMinZ, float.NegativeInfinity);
            MaxZ = _params.Get(PMaxZ, float.PositiveInfinity);
            Invert = _params.Get(PInvert, false);

            // 写反的区间会一个点都不留，而"看着跑通了、世界是空的"是最难查的一类；换过来就好，作者的两个数
            // 显然就是他想表达的两端。
            if (MaxX < MinX)
                (MinX, MaxX) = (MaxX, MinX);

            if (MaxZ < MinZ)
                (MinZ, MaxZ) = (MaxZ, MinZ);
        }

        /// <inheritdoc />
        public override string TypeId => _typeId;

        /// <inheritdoc />
        public override string DisplayName => "Area Filter";

        /// <inheritdoc />
        public override bool IsThreadSafe => true;

        /// <inheritdoc />
        public override PCGPinDefinition[] InputPins => new[]
        {
            new PCGPinDefinition("In", PCGPinDirection.Input, isRequired: true,
                tooltip: "Points to filter."),
        };

        /// <inheritdoc />
        public override PCGPinDefinition[] OutputPins => new[]
        {
            new PCGPinDefinition("Out", PCGPinDirection.Output,
                tooltip: "Points inside the rectangle (or outside it, when inverted)."),
        };

        /// <inheritdoc />
        public override string DescribeParameters() =>
            $"x=[{MinX:F1},{MaxX:F1}] z=[{MinZ:F1},{MaxZ:F1}], invert={Invert}";

        /// <inheritdoc />
        public override PCGData OnExecute(PCGExecutionContext context, PCGData[] inputs)
        {
            var input = inputs[0];
            if (input == null || input.Count == 0)
                return PCGData.Empty;

            var points = input.Points;
            var keep = new List<int>(points.Length);

            for (int i = 0; i < points.Length; i++)
            {
                context.ThrowIfCancelled(i);

                var position = points[i].Position;
                bool inside = position.X >= MinX && position.X <= MaxX &&
                              position.Z >= MinZ && position.Z <= MaxZ;

                if (inside != Invert)
                    keep.Add(i);
            }

            // 一个都没筛掉时把这一层原样交下去：下游还在共享同一份数据，白复制一遍只是浪费。
            if (keep.Count == points.Length)
                return input.Clone();

            if (keep.Count == 0)
                return PCGData.Empty;

            return PCGData.Gather(input, keep.ToArray(), keep.Count);
        }
    }
}

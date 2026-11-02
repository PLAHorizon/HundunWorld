// PCG 插件 - 多边形过滤器（筛选器）
// 属于 FlaxEngine PCG 插件。见 docs/PCG_PLUGIN_DESIGN.md §5.1.42。

using System;
using System.Collections.Generic;
using FlaxEngine;
using PCG.Data;
using PCG.Execution;

namespace PCG.Nodes
{
    /// <summary>
    /// 按**水平面上的一个任意多边形**保留点：顶点串围出的那块地里（或地外）才算命中。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 这是 <c>AreaFilter</c> 的推广：同一条"回答「长在哪」"的职责，但形状不再是轴对齐矩形。
    /// 可写描述的人心里的地块常常不是矩形——弯的河谷、<b>L 形的院子</b>、斜着的一条地界；
    /// 对这些形状，矩形只能给一个更大或更小的近似，而"只在这块地里"是描述里很常见的一句。
    /// </para>
    /// <para>
    /// 顶点串写成 <c>"x z; x z; x z"</c>（分号分顶点、每个顶点两个数），与描述层里
    /// <c>terrain … shapes "…"</c> 那种"一段字符串表达一张小表"的写法一致。**顶点数不是固定槽位**：
    /// 地块的形状本来就自由，固定成八个槽位会让"九个顶点的河湾"写不出来。
    /// </para>
    /// <para>
    /// 判定本身在 <see cref="PCGPolygon"/> 里（纯算术，进离屏门）。这里只负责把参数解析成它、
    /// 逐点问一句、然后按 <c>Gather</c> 交下去——所以这个节点**不调任何引擎 API**，连带它的
    /// "配置写错了"也必须走 <see cref="PCGNodeBase.LastError"/> 而不是日志（离线跑会 DllNotFound）。
    /// </para>
    /// <para>
    /// <b>写坏了不静默清空</b>：顶点串解析不了、或顶点少于三个（围不出面积）时，这一层**原样透传**
    /// 并留下 <see cref="PCGNodeBase.LastError"/>。理由与其余节点一致——"整层点被悄悄扔光"是这一层
    /// 最难查的症状（世界看着正常，只是这里什么都没长），而"没筛"至少还是可解释的行为，且错误就摆在
    /// 面板与本次运行的告警里。
    /// </para>
    /// </remarks>
    public sealed class PCGPolygonFilterNode : PCGNodeBase
    {
        /// <summary>稳定的类型 id。</summary>
        public const string Id = "PolygonFilter";

        // 参数下标。顺序是存档格式的一部分；只能往后追加。
        private const int PVertices = 0;
        private const int PInvert = 1;

        /// <summary>这个节点读取的参数个数。</summary>
        public const int ParameterCount = 2;

        /// <summary>
        /// 没写顶点串时的默认值：原点旁一个 100×100 的正方形。
        /// </summary>
        /// <remarks>
        /// 与槽位表里的默认值**必须是同一个数**（有一条断言专门比这两处）：新拖出来的节点不该是退化的
        /// （空顶点串等于"什么都不收"，作者会以为节点坏了），而两处各写一份就迟早会漂。
        /// </remarks>
        public const string DefaultVertices = "0 0; 100 0; 100 100; 0 100";

        private readonly string _typeId;
        private readonly PCGNodeParameters _params;

        /// <summary>顶点串（原样留着，供参数描述与重解析）。</summary>
        public string Vertices = string.Empty;

        /// <summary>置位时反过来：只留多边形**以外**的点。</summary>
        public bool Invert;

        /// <summary>解析好的多边形；解析失败时是一个空多边形（判定恒不命中，但这条路径根本走不到）。</summary>
        private readonly PCGPolygon _polygon;

        /// <summary>顶点串的问题；null 表示没问题。</summary>
        public string ParseError { get; private set; }

        /// <summary>按序列化参数创建节点。</summary>
        public PCGPolygonFilterNode(PCGNodeRegistry.PCGNodeSerializationData data)
        {
            _typeId = Id;
            _params = new PCGNodeParameters(data.Values, data.TypeId);

            Vertices = _params.Get(PVertices, DefaultVertices) ?? string.Empty;
            Invert = _params.Get(PInvert, false);

            if (PCGPolygon.TryParse(Vertices, out var polygon, out string error))
                _polygon = polygon;
            else
                _polygon = PCGPolygon.FromVertices(Array.Empty<Vector2>());

            ParseError = error;

            if (error == null && !_polygon.IsUsable)
                ParseError = $"顶点只有 {_polygon.Count} 个，围不出面积（至少要三个）";
        }

        /// <inheritdoc />
        public override string TypeId => _typeId;

        /// <inheritdoc />
        public override string DisplayName => "Polygon Filter";

        /// <summary>判定是纯算术、多边形只读，所以可以并行。</summary>
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
                tooltip: "Points inside the polygon (or outside it, when inverted)."),
        };

        /// <inheritdoc />
        public override string DescribeParameters() =>
            (_polygon?.Describe() ?? "unparsed") + $", invert={Invert}";

        /// <inheritdoc />
        public override PCGData OnExecute(PCGExecutionContext context, PCGData[] inputs)
        {
            var input = inputs[0];
            if (input == null || input.Count == 0)
                return PCGData.Empty;

            // 写坏了就原样交下去，并把原因摆出来：见类型注释里"写坏了不静默清空"。
            if (ParseError != null)
            {
                LastError = $"Polygon Filter cannot read its vertex list: {ParseError}";
                return input;
            }

            var points = input.Points;
            var keep = new List<int>(points.Length);

            for (int i = 0; i < points.Length; i++)
            {
                context.ThrowIfCancelled(i);

                var position = points[i].Position;
                bool inside = _polygon.Contains(position.X, position.Z);

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

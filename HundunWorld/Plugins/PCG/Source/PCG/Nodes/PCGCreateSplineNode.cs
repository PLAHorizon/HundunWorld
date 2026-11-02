// PCG 插件 - 创建曲线（点 → 曲线）
// FlaxEngine PCG 插件的一部分。见 docs/PCG_PLUGIN_DESIGN.md §5.1.14。

using System;
using System.Collections.Generic;
using FlaxEngine;
using PCG.Execution;

namespace PCG.Nodes
{
    /// <summary>
    /// 把一批点折成一条曲线，于是一次撒点就能变成一条路径——由途经点得到一条路，
    /// 由一根根柱子得到一排栅栏线。
    /// </summary>
    /// <remarks>
    /// <para>
    /// UE5 允许图把点转成样条、再沿它们采样；这一步对应的就是那个。这些点成为折线的顶点，
    /// 所以默认情况下曲线的平滑程度正好等于点集本来的样子。
    /// <c>Curve Subdivisions</c> 是图要求"把路径磨圆"的方式：它沿同样这些顶点的
    /// Catmull-Rom 曲线插入若干点（见 <see cref="PCGSplineCurves"/>），
    /// 这让下游每一个消费者——弧长、切线、均匀采样、曲线网格排布——
    /// 仍在它们本来就认识的折线上工作。细分不会挪动端点，
    /// 所以一条路径依然从它的途经点所写明的地方开始、也在那里结束。
    /// </para>
    /// <para>
    /// 顺序才是要点，也是全部的难度：一批点没有顺序，所以节点要么接受交到它手里的那个顺序
    /// （那正是上游曲线采样器产出的东西），要么自己走一遍最近邻路径。
    /// 后者是贪心启发式，不是旅行商问题的解，并且照实这么写着——
    /// 因为一条回头一次的路径通常完全够用，而最优解不值得那份搜索。
    /// </para>
    /// </remarks>
    public sealed class PCGCreateSplineNode : PCGNodeBase
    {
        /// <summary>稳定的类型 id。</summary>
        public const string Id = "CreateSpline";

        // 参数下标。顺序属于存档图格式，只能追加。
        private const int POrder = 0;
        private const int PClosed = 1;
        private const int PKeepPoints = 2;
        private const int PCurveSubdivisions = 3;

        /// <summary>本节点读取的参数个数。</summary>
        public const int ParameterCount = 4;

        /// <summary>贪心排序的上限，因为它要比较每一对点。</summary>
        public const int MaxGreedyPoints = 8192;

        /// <summary>顶点按什么顺序排。</summary>
        public enum VertexOrder
        {
            /// <summary>就按输入已经给它们的顺序。</summary>
            InputOrder = 0,

            /// <summary>从第一个点出发的贪心最近邻走法。</summary>
            NearestNeighbour = 1,
        }

        private readonly string _typeId;
        private readonly PCGNodeParameters _params;

        /// <summary>顶点使用哪一种排序。</summary>
        public VertexOrder Order = VertexOrder.InputOrder;

        /// <summary>路径是否闭回它的第一个顶点。</summary>
        public bool Closed;

        /// <summary>输入的点是同时也原样传递下去。</summary>
        public bool KeepPoints;

        /// <summary>
        /// 每段里插入多少个点来把路径磨圆；1 表示不动那条折线。
        /// </summary>
        public int CurveSubdivisions = 1;

        /// <summary>由序列化的参数创建节点。</summary>
        public PCGCreateSplineNode(PCGNodeRegistry.PCGNodeSerializationData data)
        {
            _typeId = Id;
            _params = new PCGNodeParameters(data.Values, data.TypeId);

            int order = _params.Get(POrder, (int)VertexOrder.InputOrder);
            Order = (VertexOrder)Math.Min((int)VertexOrder.NearestNeighbour, Math.Max(0, order));
            Closed = _params.Get(PClosed, false);
            KeepPoints = _params.Get(PKeepPoints, false);
            CurveSubdivisions = Math.Min(PCGSplineCurves.MaxSubdivisions,
                Math.Max(1, _params.Get(PCurveSubdivisions, 1)));
        }

        /// <inheritdoc />
        public override string TypeId => _typeId;

        /// <inheritdoc />
        public override string DisplayName => "Create Spline";

        /// <inheritdoc />
        public override bool IsThreadSafe => true;

        /// <inheritdoc />
        public override PCGPinDefinition[] InputPins => new[]
        {
            new PCGPinDefinition("In", PCGPinDirection.Input, isRequired: true,
                tooltip: "Points that become the path's vertices."),
        };

        /// <inheritdoc />
        public override PCGPinDefinition[] OutputPins => new[]
        {
            new PCGPinDefinition("Out", PCGPinDirection.Output,
                tooltip: "A curve built from those points (and optionally the points themselves)."),
        };

        /// <inheritdoc />
        public override string DescribeParameters() =>
            CurveSubdivisions > 1
                ? $"order={Order}, closed={Closed}, keepPoints={KeepPoints}, curve={CurveSubdivisions}x"
                : $"order={Order}, closed={Closed}, keepPoints={KeepPoints}";

        /// <inheritdoc />
        public override PCGData OnExecute(PCGExecutionContext context, PCGData[] inputs)
        {
            var input = inputs[0];
            if (input == null || input.Count == 0)
                return PCGData.Empty;

            if (input.Count < 2)
            {
                LastError = "Create Spline needs at least two points; a single point has no path. " +
                            "The layer passes through unchanged.";
                return input.Clone();
            }

            if (Order == VertexOrder.NearestNeighbour && input.Count > MaxGreedyPoints)
            {
                LastError = $"Create Spline's greedy ordering was refused for {input.Count} points " +
                            $"(limit {MaxGreedyPoints}); the input order was used instead.";
                return Build(input, InputOrderIndices(input.Count), context);
            }

            return Build(input, Order == VertexOrder.NearestNeighbour
                ? NearestNeighbourOrder(input.Points, context)
                : InputOrderIndices(input.Count), context);
        }

        private PCGData Build(PCGData input, int[] order, PCGExecutionContext context)
        {
            var vertices = new Vector3[order.Length];

            for (int i = 0; i < order.Length; i++)
            {
                context.ThrowIfCancelled(i);
                vertices[i] = input.Points[order[i]].Position;
            }

            // 把路径磨圆是对折线做细分，而不是引入第二种曲线类型，所以弧长、切线、
            // 采样与曲线网格排布都继续在它们本来就认识的类型上工作。
            if (CurveSubdivisions > 1)
                vertices = PCGSplineCurves.Subdivide(vertices, Closed, CurveSubdivisions);

            var result = KeepPoints ? input.Clone() : new PCGData();
            var splines = new List<PCGSpline>(1) { new PCGSpline(vertices, Closed) };

            // 曲线是追加在进来的东西之后的，所以一张把几层转成路径的图
            // 会保住它本来就有的那些，而不是把它们替换掉。
            if (KeepPoints && input.Splines != null)
                splines.InsertRange(0, input.Splines);

            result.Splines = splines.ToArray();

            return result;
        }

        private static int[] InputOrderIndices(int count)
        {
            var order = new int[count];

            for (int i = 0; i < count; i++)
                order[i] = i;

            return order;
        }

        /// <summary>从第一个点出发的贪心最近邻走法，每一步比较剩下的每一对。</summary>
        private static int[] NearestNeighbourOrder(PCGPoint[] points, PCGExecutionContext context)
        {
            int count = points.Length;
            var order = new int[count];
            var used = new bool[count];

            order[0] = 0;
            used[0] = true;

            for (int step = 1; step < count; step++)
            {
                context.ThrowIfCancelled(step);

                var from = points[order[step - 1]].Position;
                int best = -1;
                float bestSquared = float.MaxValue;

                for (int candidate = 0; candidate < count; candidate++)
                {
                    if (used[candidate])
                        continue;

                    float squared = (points[candidate].Position - from).LengthSquared;

                    // 平手时给下标更低的那个，这样这条走法不会依赖候选
                    // 恰好被比较的顺序。
                    if (squared < bestSquared)
                    {
                        bestSquared = squared;
                        best = candidate;
                    }
                }

                order[step] = best;
                used[best] = true;
            }

            return order;
        }
    }
}

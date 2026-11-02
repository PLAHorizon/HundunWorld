// PCG 插件 - 字面点（按参数写出一个个点）
// 属于 FlaxEngine PCG 插件。见 docs/PCG_PLUGIN_DESIGN.md §5.1.40。

using System;
using FlaxEngine;
using PCG.Execution;

namespace PCG.Nodes
{
    /// <summary>
    /// 按参数直接写出若干个点（位置是写死的，不是采样出来的）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 插件里其它点源都是**采样器**：给一个范围，撒一堆点。于是"从这个地方到那个地方"这类**字面位置**一直
    /// 无法表达——道路的途经点、村庄的中心、洞穴的入口都属于这一类，它们的价值恰恰在于"就放在这里"。这个节点
    /// 补的就是这一格：八个位置槽位加一个"用几个"，位置原样变成点。
    /// </para>
    /// <para>
    /// 为什么是**固定八个槽位**而不是一段文本：Visject 画布是按参数槽位生成控件的，八个 `Vector3` 槽位在面板
    /// 上就是八个可拖数、可见、可撤销的向量框；换成一行文本语法，就等于给插件再加一门小语言，而设计师还得记住
    /// 它。八个够画一条带拐弯的路或排一串房子，要更多就把两个节点接起来（`Merge Points`）。
    /// </para>
    /// <para>
    /// 点的其它字段照旧成组给出：`Scale` 写进点的缩放、`Density` 写进密度（下游生成器按它抽样）、
    /// `Seed Offset` 只影响每个点的随机种子，位置不受它影响——位置是写死的，这一点有断言钉住。
    /// </para>
    /// </remarks>
    public sealed class PCGPointListNode : PCGNodeBase
    {
        /// <summary>稳定的类型 id。</summary>
        public const string Id = "PointList";

        // 参数下标。顺序是存档格式的一部分，只允许追加。
        private const int PCount = 0;
        private const int PFirstPoint = 1;
        private const int PScale = 9;
        private const int PDensity = 10;
        private const int PSeedOffset = 11;

        /// <summary>本节点读取的参数个数。</summary>
        public const int ParameterCount = 12;

        /// <summary>位置槽位的个数。</summary>
        public const int SlotCount = 8;

        private readonly string _typeId;
        private readonly PCGNodeParameters _params;

        /// <summary>用几个位置槽位。</summary>
        public int Count = 2;

        /// <summary>位置槽位，按顺序取前 <see cref="Count"/> 个。</summary>
        public Vector3[] Positions = DefaultPositions();

        /// <summary>写进每个点的缩放。</summary>
        public float Scale = 1.0f;

        /// <summary>写进每个点的密度。</summary>
        public float Density = 1.0f;

        /// <summary>加在图的种子上，用来把同一个图里两个字面点节点的点种子分开。</summary>
        public int SeedOffset;

        /// <summary>由序列化参数构造节点。</summary>
        public PCGPointListNode(PCGNodeRegistry.PCGNodeSerializationData data)
        {
            _typeId = Id;
            _params = new PCGNodeParameters(data.Values, data.TypeId);

            Count = Math.Min(SlotCount, Math.Max(0, _params.Get(PCount, 2)));
            Scale = Math.Max(0.0001f, _params.Get(PScale, 1.0f));
            Density = Mathf.Saturate(_params.Get(PDensity, 1.0f));
            SeedOffset = _params.Get(PSeedOffset, 0);

            var defaults = DefaultPositions();

            for (int i = 0; i < SlotCount; i++)
                Positions[i] = _params.GetVector3(PFirstPoint + i, defaults[i]);
        }

        /// <inheritdoc />
        public override string TypeId => _typeId;

        /// <inheritdoc />
        public override string DisplayName => "Point List";

        /// <inheritdoc />
        public override bool IsThreadSafe => true;

        /// <inheritdoc />
        public override PCGPinDefinition[] InputPins => Array.Empty<PCGPinDefinition>();

        /// <inheritdoc />
        public override PCGPinDefinition[] OutputPins => new[]
        {
            new PCGPinDefinition("Out", PCGPinDirection.Output,
                tooltip: "写出来的那些点，按槽位顺序。"),
        };

        /// <inheritdoc />
        public override string DescribeParameters() => $"{Count} point(s) of {SlotCount} slot(s), scale {Scale:F2}";

        /// <inheritdoc />
        public override PCGData OnExecute(PCGExecutionContext context, PCGData[] inputs)
        {
            LastError = null;

            int count = Math.Min(SlotCount, Math.Max(0, Count));

            if (count == 0)
            {
                // 一个都不用就等于没点：如实给空，而不是把八个槽位全撒出去。
                LastError = $"{nameof(Count)} is zero, so no point is written.";
                return PCGData.Empty;
            }

            var data = PCGData.Create(count);
            float extents = 0.5f * Scale;

            for (int i = 0; i < count; i++)
            {
                context.ThrowIfCancelled(i);

                // 位置是参数写死的，所以这里不摇随机数；种子只决定这个点的随机流（下游按点取随机数时用得上）。
                var position = new Vector3(Positions[i].X, Positions[i].Y, Positions[i].Z);
                var point = PCGPoint.Create(position, Density, unchecked(context.Seed + SeedOffset + i));

                point.Scale = new Float3(Scale);
                point.UpdateBounds(new Vector3(extents, extents, extents));
                data.Points[i] = point;
            }

            return data;
        }

        /// <summary>
        /// 八个槽位的默认位置：沿 +X 每 500 一格一字排开。
        /// </summary>
        /// <remarks>
        /// 排成一条线而不是全零，是为了让新拖出来的节点**不是退化的**：两个重合的点连不出路，而"两点加一条直线"
        /// 一接 `Path` 就有东西看，设计师改数字就是改路。
        /// </remarks>
        private static Vector3[] DefaultPositions()
        {
            var positions = new Vector3[SlotCount];

            for (int i = 0; i < SlotCount; i++)
                positions[i] = new Vector3(i * 500.0f, 0.0f, 0.0f);

            return positions;
        }
    }
}

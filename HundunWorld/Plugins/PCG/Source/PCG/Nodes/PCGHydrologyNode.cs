// PCG 插件 - 水文（采样器）
// 属于 FlaxEngine PCG 插件。见 docs/PCG_PLUGIN_DESIGN.md §5.1.36。

using System;
using System.Collections.Generic;
using FlaxEngine;
using PCG.Execution;

namespace PCG.Nodes
{
    /// <summary>
    /// 从地形算出水：汇流成河的地方出河（带一条中心线样条），积水成湖的地方出湖。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 湖泊与江河是地形本身的结果，不是能随手撒出来的：水往低处走，走不掉的地方积成湖，汇到一起的地方成河。
    /// 这个节点把这件事做出来——填洼定出水面、流向定出水怎么走、汇流累积定出哪里够格叫河——于是"按设定描述
    /// 生成一条河、一片湖"有了可依据的东西，而不是手摆一条样条。
    /// </para>
    /// <para>
    /// 输出与既有词汇对齐：河是一串点（带 <c>Flow</c> 汇流量列）<b>加一条中心线样条</b>，所以已有的样条网格生成器
    /// 就能沿着河铺一道水面；湖是每处一个点（带水面高与面积），接一个生成器就是一片水面。高度场与路径规划共用
    /// 同一个注入点（<see cref="PCGWorldContext.HeightSource"/>），因此两者对"地形是什么样"永远给同一个答案。
    /// </para>
    /// <para>
    /// 本节点会被离屏执行，所以只经 <see cref="PCGNodeBase.LastError"/> 报告，不调用 <c>Debug.*</c>。
    /// </para>
    /// </remarks>
    public sealed class PCGHydrologyNode : PCGNodeBase
    {
        /// <summary>稳定的类型 id。</summary>
        public const string Id = "Hydrology";

        // 参数下标。顺序是存档格式的一部分，只允许追加。
        private const int PCellSize = 0;
        private const int PRiverThreshold = 1;
        private const int PMinLakeCells = 2;
        private const int PMinLakeDepth = 3;
        private const int PWaterOffset = 4;
        private const int PMaxRivers = 5;
        private const int PEmitRivers = 6;
        private const int PEmitLakes = 7;
        private const int PWriteFlow = 8;

        /// <summary>区域盒子的两个角（世界单位；只框水平面，按格心判）。</summary>
        private const int PRegionMin = 9;
        private const int PRegionMax = 10;

        /// <summary>区域反转：勾上表示这块矩形**不许发源**（绕开它），不勾表示只准在这里发源。</summary>
        private const int PRegionInvert = 11;

        /// <summary>本节点读取的参数个数。</summary>
        public const int ParameterCount = 12;

        /// <summary>汇流量所在的列名（河流点）。</summary>
        public const string FlowColumn = "Flow";

        /// <summary>湖面面积所在的列名（湖泊点）。</summary>
        public const string LakeAreaColumn = "LakeArea";

        /// <summary>水面高度所在的列名（湖泊点）。</summary>
        public const string WaterLevelColumn = "WaterLevel";

        /// <summary>最多输出多少个点，防止一格一河把预算吃光。</summary>
        public const int MaxPoints = 100000;

        private readonly string _typeId;
        private readonly PCGNodeParameters _params;

        /// <summary>规划网格的边长。</summary>
        public float CellSize = 50.0f;

        /// <summary>汇流累积达到多少格才算河。</summary>
        public int RiverThreshold = 200;

        /// <summary>小于这么多格子的洼地不算湖。</summary>
        public int MinLakeCells = 12;

        /// <summary>水浅于这个值不算湖。</summary>
        public float MinLakeDepth = 0.5f;

        /// <summary>水面整体抬高或降低的量。</summary>
        public float WaterOffset;

        /// <summary>最多输出多少条河。</summary>
        public int MaxRivers = 64;

        /// <summary>是否输出河流。</summary>
        public bool EmitRivers = true;

        /// <summary>是否输出湖泊。</summary>
        public bool EmitLakes = true;

        /// <summary>是否给河流点写汇流量列。</summary>
        public bool WriteFlow = true;

        /// <summary>
        /// 区域下限：河只准从这块地里发源、湖只准在这块地里成心。两个角写平（或写反）时这一档不存在。
        /// </summary>
        public Vector3 RegionMin;

        /// <summary><see cref="RegionMin"/> 的对角。</summary>
        public Vector3 RegionMax;

        /// <summary>
        /// 区域反转：true 表示这块矩形**不许发源**（河与湖都从别处起），false 表示只准在这里发源。
        /// 判据与 within 是同一条的取反——水系这一侧"绕开"是能表达的，因为它筛的是**发源格**，
        /// 不是从中间截断已经成形的河（那才会把连续体切成两段）。
        /// </summary>
        public bool RegionInvert;

        /// <summary>上次产出的河流条数。</summary>
        public int LastRiverCount { get; private set; }

        /// <summary>上次产出的湖泊个数。</summary>
        public int LastLakeCount { get; private set; }

        /// <summary>
        /// 上一次运行实际生效的区域说明；没框（或两个角写平/写反）时为 null。
        /// 存在的理由：区域静默不生效与"这一带就是没水"在屏幕上长得一样，检视面板必须能分清。
        /// </summary>
        public string LastRegionNote { get; private set; }

        /// <summary>由序列化参数构造节点。</summary>
        public PCGHydrologyNode(PCGNodeRegistry.PCGNodeSerializationData data)
        {
            _typeId = Id;
            _params = new PCGNodeParameters(data.Values, data.TypeId);

            CellSize = Math.Max(0.1f, _params.Get(PCellSize, 50.0f));
            RiverThreshold = Math.Max(0, _params.Get(PRiverThreshold, 200));
            MinLakeCells = Math.Max(1, _params.Get(PMinLakeCells, 12));
            MinLakeDepth = Math.Max(0.0f, _params.Get(PMinLakeDepth, 0.5f));
            WaterOffset = _params.Get(PWaterOffset, 0.0f);
            MaxRivers = Math.Max(0, _params.Get(PMaxRivers, 64));
            EmitRivers = _params.Get(PEmitRivers, true);
            EmitLakes = _params.Get(PEmitLakes, true);
            WriteFlow = _params.Get(PWriteFlow, true);
            RegionMin = _params.Get(PRegionMin, Vector3.Zero);
            RegionMax = _params.Get(PRegionMax, Vector3.Zero);
            RegionInvert = _params.Get(PRegionInvert, false);
        }

        /// <inheritdoc />
        public override string TypeId => _typeId;

        /// <inheritdoc />
        public override string DisplayName => "Hydrology";

        /// <inheritdoc />
        public override bool IsThreadSafe => true;

        /// <inheritdoc />
        public override PCGPinDefinition[] InputPins => Array.Empty<PCGPinDefinition>();

        /// <inheritdoc />
        public override PCGPinDefinition[] OutputPins => new[]
        {
            new PCGPinDefinition("Out", PCGPinDirection.Output,
                tooltip: "河流点与湖泊点，以及每条河的中心线样条。"),
        };

        /// <inheritdoc />
        public override string DescribeParameters() =>
            $"cell {CellSize:F0}, river >= {RiverThreshold} cell(s), lake >= {MinLakeCells} cell(s) " +
            $"deeper than {MinLakeDepth:F1}" +
            (WaterOffset != 0.0f ? $", water offset {WaterOffset:F1}" : string.Empty) +
            (EmitRivers ? string.Empty : ", rivers off") +
            (EmitLakes ? string.Empty : ", lakes off");

        /// <inheritdoc />
        public override PCGData OnExecute(PCGExecutionContext context, PCGData[] inputs)
        {
            LastRiverCount = 0;
            LastLakeCount = 0;
            LastError = null;

            var world = context.World;

            if (world == null || !world.HasHeightSource)
            {
                LastError = "Hydrology found no terrain in the execution context. It produces nothing.";
                return PCGData.Empty;
            }

            var bounds = context.GenerationBounds;

            // 网格覆盖生成范围；范围退化时给一句人话，而不是算出一张 1×1 的网格。
            float spanX = bounds.Maximum.X - bounds.Minimum.X;
            float spanZ = bounds.Maximum.Z - bounds.Minimum.Z;

            if (spanX <= CellSize || spanZ <= CellSize)
            {
                LastError = "Hydrology needs a generation region at least one cell across.";
                return PCGData.Empty;
            }

            var origin = new Vector3(bounds.Minimum.X, 0.0f, bounds.Minimum.Z);
            int width = (int)Math.Ceiling(spanX / CellSize) + 1;
            int height = (int)Math.Ceiling(spanZ / CellSize) + 1;
            int cells = width * height;

            var heights = new float[cells];
            var known = new bool[cells];

            for (int z = 0; z < height; z++)
            {
                for (int x = 0; x < width; x++)
                {
                    float worldX = origin.X + x * CellSize;
                    float worldZ = origin.Z + z * CellSize;
                    var sample = world.HeightSource(worldX, worldZ);

                    if (sample.HasValue)
                    {
                        heights[z * width + x] = sample.Value;
                        known[z * width + x] = true;
                    }
                }
            }

            var filled = PCGHydrology.Fill(heights, known, width, height);
            var flow = PCGHydrology.FlowDirections(filled, known, width, height);
            var accumulation = PCGHydrology.Accumulate(filled, flow, known, width, height);

            var rivers = new List<PCGRiver>();
            var lakes = new List<PCGLake>();

            // 区域是**发源判据**，不是产出之后的删点：河与湖都是连续体，从中间删一格就断成两截。
            // 世界矩形→格的换算与产出点用的是同一个式子（`origin + (i + 0.5) * CellSize`），
            // 两边不同源就会出现"判据说在框内、点却画在框外"那种对不上。
            Predicate<int> allowOrigin = null;

            if (RegionMax.X > RegionMin.X && RegionMax.Z > RegionMin.Z)
            {
                allowOrigin = index =>
                {
                    float worldX = origin.X + (index % width + 0.5f) * CellSize;
                    float worldZ = origin.Z + (index / width + 0.5f) * CellSize;

                    bool inside = worldX >= RegionMin.X && worldX <= RegionMax.X &&
                                  worldZ >= RegionMin.Z && worldZ <= RegionMax.Z;

                    // 取反这一支是 `outside` 的全部含义：筛的是**发源格**，所以"这块地不许发源"与
                    // "只准在这块发源"共用同一个 `inside`，只是要的结果相反。离屏实测两者都生效
                    // （两处等深洼地合并成一湖、湖心在框内 ⇒ outside 把它整片判掉，产出为空是正确结果）。
                    return RegionInvert ? !inside : inside;
                };

                LastRegionNote = $"水系{(RegionInvert ? "不许在" : "只准在")} " +
                                 $"x[{RegionMin.X:F0},{RegionMax.X:F0}] z[{RegionMin.Z:F0},{RegionMax.Z:F0}] 这片发源";
            }
            else
            {
                LastRegionNote = null;
            }

            if (EmitRivers && RiverThreshold > 0 && MaxRivers > 0)
            {
                PCGHydrology.FindRivers(
                    accumulation, flow, known, width, height, RiverThreshold, MaxRivers, rivers, allowOrigin);
            }

            if (EmitLakes)
            {
                PCGHydrology.FindLakes(
                    heights, filled, known, flow, width, CellSize, MinLakeCells, MinLakeDepth, lakes, allowOrigin);
            }

            int riverPoints = 0;

            if (EmitRivers)
            {
                for (int i = 0; i < rivers.Count; i++)
                    riverPoints += rivers[i].Centerline.Length;
            }

            int total = Math.Min(MaxPoints, riverPoints + lakes.Count);

            if (total == 0)
            {
                // 没有水不是错误：地形本来就可能哪都不积水、也没有够格的汇流。
                return PCGData.Empty;
            }

            var result = PCGData.Create(total);
            var points = result.Points;
            var flows = WriteFlow && EmitRivers ? PCGCellSplit.DeclareColumn(result, FlowColumn) : null;
            var areas = EmitLakes ? PCGCellSplit.DeclareColumn(result, LakeAreaColumn) : null;
            var levels = EmitLakes ? PCGCellSplit.DeclareColumn(result, WaterLevelColumn) : null;
            var splines = new List<PCGSpline>(rivers.Count);
            int write = 0;

            for (int i = 0; i < rivers.Count && write < total; i++)
            {
                var river = rivers[i];
                var line = new Vector3[river.Centerline.Length];

                for (int p = 0; p < river.Centerline.Length && write < total; p++)
                {
                    int cell = (int)river.Centerline[p].Z * width + (int)river.Centerline[p].X;
                    float water = filled[cell] + WaterOffset;

                    line[p] = new Vector3(
                        origin.X + (river.Centerline[p].X + 0.5f) * CellSize,
                        water,
                        origin.Z + (river.Centerline[p].Z + 0.5f) * CellSize);

                    points[write] = PCGPoint.Create(line[p], 1.0f, unchecked(context.Seed + write));
                    points[write].UpdateBounds(new Vector3(0.5f, 0.5f, 0.5f));

                    if (flows != null)
                        flows[write] = accumulation[cell];

                    write++;
                }

                if (line.Length > 1)
                    splines.Add(new PCGSpline(line, false));
            }

            for (int i = 0; i < lakes.Count && write < total; i++)
            {
                var lake = lakes[i];
                var position = new Vector3(
                    origin.X + (lake.Center.X + 0.5f) * CellSize,
                    lake.WaterLevel + WaterOffset,
                    origin.Z + (lake.Center.Z + 0.5f) * CellSize);

                points[write] = PCGPoint.Create(position, 1.0f, unchecked(context.Seed + 7919 + write));
                points[write].UpdateBounds(new Vector3(0.5f, 0.5f, 0.5f));

                if (areas != null)
                    areas[write] = lake.CellCount;

                if (levels != null)
                    levels[write] = lake.WaterLevel + WaterOffset;

                write++;
            }

            result.Splines = splines.Count > 0 ? splines.ToArray() : null;
            LastRiverCount = rivers.Count;
            LastLakeCount = lakes.Count;

            // 输出点数按实际写入收窄：Min 只是为了不越过预算，write 才是真实产出。
            if (write < result.Count)
            {
                var trimmed = PCGData.Create(write);

                for (int i = 0; i < write; i++)
                    trimmed.Points[i] = points[i];

                trimmed.SharedAttributes = result.SharedAttributes;
                trimmed.Splines = result.Splines;
                return trimmed;
            }

            return result;
        }
    }
}

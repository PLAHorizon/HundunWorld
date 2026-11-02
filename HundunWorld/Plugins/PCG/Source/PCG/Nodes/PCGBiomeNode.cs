// PCG 插件 - 生物群系（采样器 / 打标签）
// 属于 FlaxEngine PCG 插件。见 docs/PCG_PLUGIN_DESIGN.md §5.1.37。

using System;
using System.Collections.Generic;
using FlaxEngine;
using PCG.Execution;

namespace PCG.Nodes
{
    /// <summary>
    /// 判定地形上每一处算什么地貌（平原/丘陵/山地/盆地/水面），并把它写成列。
    /// </summary>
    /// <remarks>
    /// <para>
    /// "温带平原、丘陵、盆地"这类说法要落到图里，就得先有东西回答"这一格算什么"。这个节点有两种用法，取决于
    /// 有没有接输入：**不接输入**时它按 <c>Cell Size</c> 在生成范围上铺一张点阵，于是整片区域得到一张**地貌底图**，
    /// 下游可以用 <c>Biome</c> 列去分叉——平原撒草、丘陵长树、盆地积水；**接了输入**时它只给现有点打标签并原样
    /// 传下去，于是"先按密度撒一批点，再按地貌决定它们长成什么"也顺手可写。
    /// </para>
    /// <para>
    /// 判据看的是**起伏**与**相对高度**而不是绝对高度：高原上的平台也是平原、海边的坡也是坡。阈值全部暴露成参数，
    /// 因为"多陡算山"是设计问题而不是实现细节。
    /// </para>
    /// <para>
    /// 代价要说清：每个点要在两个半径上各取若干个方向的高度（默认 4+4 次查询），所以这是**重采样**的节点——
    /// <c>Relief Radius</c>／<c>Region Radius</c> 调大或点阵调密都会线性变慢。它会被离屏执行，因此只经
    /// <see cref="PCGNodeBase.LastError"/> 报告，不调用 <c>Debug.*</c>。地形查询走注入的高度源，所以这个节点
    /// 刻意不声明为线程安全：那条委托背后可能是引擎地形。
    /// </para>
    /// </remarks>
    public sealed class PCGBiomeNode : PCGNodeBase
    {
        /// <summary>稳定的类型 id。</summary>
        public const string Id = "Biome";

        // 参数下标。顺序是存档格式的一部分，只允许追加。
        private const int PCellSize = 0;
        private const int PWaterLevel = 1;
        private const int PWaterMargin = 2;
        private const int PReliefRadius = 3;
        private const int PRegionRadius = 4;
        private const int PSamples = 5;
        private const int PMountainSlope = 6;
        private const int PMountainRelief = 7;
        private const int PHillSlope = 8;
        private const int PHillRelief = 9;
        private const int PBasinDepth = 10;
        private const int PBasinRelief = 11;
        private const int PWriteNames = 12;
        private const int PWriteRelief = 13;

        /// <summary>本节点读取的参数个数。</summary>
        public const int ParameterCount = 14;

        /// <summary>地貌种类所在的列名，供 Filter/Math 驱动。</summary>
        public const string BiomeColumn = "Biome";

        /// <summary>地貌名字所在的列名，供人读。</summary>
        public const string BiomeNameColumn = "BiomeName";

        /// <summary>起伏所在的列名。</summary>
        public const string ReliefColumn = "Relief";

        /// <summary>点阵模式一次最多铺多少点，防止范围大格子小把预算吃光。</summary>
        public const int MaxCells = 200000;

        private readonly string _typeId;
        private readonly PCGNodeParameters _params;

        /// <summary>点阵模式的格子边长。</summary>
        public float CellSize = 50.0f;

        /// <summary>地貌阈值。</summary>
        public PCGBiomeOptions Options = new PCGBiomeOptions
        {
            WaterLevel = 0.0f,
            WaterMargin = 0.5f,
            ReliefRadius = 200.0f,
            RegionRadius = 1000.0f,
            Samples = 4,
            MountainSlope = 25.0f,
            MountainRelief = 120.0f,
            HillSlope = 8.0f,
            HillRelief = 20.0f,
            BasinDepth = 30.0f,
            BasinRelief = 15.0f,
        };

        /// <summary>是否写地貌名字列。</summary>
        public bool WriteNames = true;

        /// <summary>是否写起伏列。</summary>
        public bool WriteRelief = true;

        /// <summary>上次各类地貌各有多少点，下标即种类。</summary>
        public int[] LastCounts = new int[PCGBiomeKind.Count];

        /// <summary>由序列化参数构造节点。</summary>
        public PCGBiomeNode(PCGNodeRegistry.PCGNodeSerializationData data)
        {
            _typeId = Id;
            _params = new PCGNodeParameters(data.Values, data.TypeId);

            CellSize = Math.Max(1.0f, _params.Get(PCellSize, 50.0f));

            Options.WaterLevel = _params.Get(PWaterLevel, 0.0f);
            Options.WaterMargin = Math.Max(0.0f, _params.Get(PWaterMargin, 0.5f));
            Options.ReliefRadius = Math.Max(0.0f, _params.Get(PReliefRadius, 200.0f));
            Options.RegionRadius = Math.Max(0.0f, _params.Get(PRegionRadius, 1000.0f));
            Options.Samples = Math.Max(4, _params.Get(PSamples, 4));
            Options.MountainSlope = _params.Get(PMountainSlope, 25.0f);
            Options.MountainRelief = _params.Get(PMountainRelief, 120.0f);
            Options.HillSlope = _params.Get(PHillSlope, 8.0f);
            Options.HillRelief = _params.Get(PHillRelief, 20.0f);
            Options.BasinDepth = _params.Get(PBasinDepth, 30.0f);
            Options.BasinRelief = _params.Get(PBasinRelief, 15.0f);

            WriteNames = _params.Get(PWriteNames, true);
            WriteRelief = _params.Get(PWriteRelief, true);
        }

        /// <inheritdoc />
        public override string TypeId => _typeId;

        /// <inheritdoc />
        public override string DisplayName => "Biome";

        /// <inheritdoc />
        public override PCGPinDefinition[] InputPins => new[]
        {
            new PCGPinDefinition("In", PCGPinDirection.Input,
                tooltip: "可选：要打地貌标签的点。不接时按 Cell Size 在生成范围上铺一张地貌底图。"),
        };

        /// <inheritdoc />
        public override PCGPinDefinition[] OutputPins => new[]
        {
            new PCGPinDefinition("Out", PCGPinDirection.Output,
                tooltip: "带 Biome / BiomeName / Relief / $Slope 列的点。"),
        };

        /// <inheritdoc />
        public override string DescribeParameters() =>
            $"cell {CellSize:F0}, water {Options.WaterLevel:F0}+{Options.WaterMargin:F1}, " +
            $"relief {Options.ReliefRadius:F0}/{Options.RegionRadius:F0} ({Options.Samples} dir), " +
            $"mountain {Options.MountainSlope:F0} deg / {Options.MountainRelief:F0}, " +
            $"hill {Options.HillSlope:F0} deg / {Options.HillRelief:F0}, " +
            $"basin {Options.BasinDepth:F0}/{Options.BasinRelief:F0}";

        /// <inheritdoc />
        public override PCGData OnExecute(PCGExecutionContext context, PCGData[] inputs)
        {
            for (int i = 0; i < LastCounts.Length; i++)
                LastCounts[i] = 0;

            LastError = null;

            var world = context.World;

            if (world == null || !world.HasHeightSource)
            {
                LastError = "Biome found no terrain in the execution context. The layer passes through unchanged.";
                var passthrough = inputs != null && inputs.Length > 0 ? inputs[0] : null;
                return passthrough ?? PCGData.Empty;
            }

            var input = inputs != null && inputs.Length > 0 ? inputs[0] : null;

            return input != null && input.Count > 0
                ? ClassifyExisting(context, world, input)
                : BuildMap(context, world);
        }

        /// <summary>给现有点打标签，点位与走向都不动。</summary>
        private PCGData ClassifyExisting(PCGExecutionContext context, PCGWorldContext world, PCGData input)
        {
            var result = input.Clone();
            var attributes = EnsureAttributes(result);
            var biomes = DeclareColumn(attributes, BiomeColumn, result.Count);
            var names = WriteNames ? DeclareTextColumn(attributes, BiomeNameColumn, result.Count) : null;
            var reliefs = WriteRelief ? DeclareColumn(attributes, ReliefColumn, result.Count) : null;
            var slopes = DeclareColumn(attributes, PCGAttributeSet.SlopeName, result.Count);

            for (int i = 0; i < result.Count; i++)
            {
                context.ThrowIfCancelled(i);

                var position = result.Points[i].Position;
                int kind = PCGBiomeClassifier.Classify(
                    world.HeightSource, position.X, position.Z, Options, out var sample);

                biomes[i] = kind;
                LastCounts[kind]++;

                if (names != null)
                    names[i] = PCGBiomeKind.NameOf(kind);

                if (reliefs != null)
                    reliefs[i] = sample.Relief;

                slopes[i] = sample.Slope;
            }

            return result;
        }

        /// <summary>在生成范围上铺一张地貌底图：每格一个点，带它的地貌。</summary>
        private PCGData BuildMap(PCGExecutionContext context, PCGWorldContext world)
        {
            var bounds = context.GenerationBounds;
            float spanX = bounds.Maximum.X - bounds.Minimum.X;
            float spanZ = bounds.Maximum.Z - bounds.Minimum.Z;

            if (spanX <= 0.0f || spanZ <= 0.0f)
            {
                LastError = "Biome needs a generation region with area to build a map from.";
                return PCGData.Empty;
            }

            int columns = Math.Min(MaxCells, (int)Math.Floor(spanX / CellSize) + 1);
            int rows = Math.Min(MaxCells, (int)Math.Floor(spanZ / CellSize) + 1);

            if (columns <= 0 || rows <= 0)
            {
                LastError = "Biome's cell size is larger than the generation region, so the map would be empty.";
                return PCGData.Empty;
            }

            int total = Math.Min(MaxCells, columns * rows);
            var result = PCGData.Create(total);
            var attributes = result.SharedAttributes;
            var biomes = DeclareColumn(attributes, BiomeColumn, total);
            var names = WriteNames ? DeclareTextColumn(attributes, BiomeNameColumn, total) : null;
            var reliefs = WriteRelief ? DeclareColumn(attributes, ReliefColumn, total) : null;
            var slopes = DeclareColumn(attributes, PCGAttributeSet.SlopeName, total);
            int write = 0;

            for (int z = 0; z < rows && write < total; z++)
            {
                for (int x = 0; x < columns && write < total; x++)
                {
                    context.ThrowIfCancelled(write);

                    float worldX = bounds.Minimum.X + (x + 0.5f) * CellSize;
                    float worldZ = bounds.Minimum.Z + (z + 0.5f) * CellSize;

                    int kind = PCGBiomeClassifier.Classify(
                        world.HeightSource, worldX, worldZ, Options, out var sample);

                    // 点位取地形高度，于是这张底图本身也能直接当点云用（例如再叠一层噪声筛）。
                    float height = sample.Known ? sample.Height : bounds.Center.Y;

                    result.Points[write] = PCGPoint.Create(new Vector3(worldX, height, worldZ), 1.0f,
                        unchecked(context.Seed + write));
                    result.Points[write].UpdateBounds(new Vector3(0.5f, 0.5f, 0.5f));

                    biomes[write] = kind;
                    LastCounts[kind]++;

                    if (names != null)
                        names[write] = PCGBiomeKind.NameOf(kind);

                    if (reliefs != null)
                        reliefs[write] = sample.Relief;

                    slopes[write] = sample.Slope;
                    write++;
                }
            }

            if (write == total)
                return result;

            var trimmed = PCGData.Create(write);

            for (int i = 0; i < write; i++)
                trimmed.Points[i] = result.Points[i];

            trimmed.SharedAttributes = attributes;
            return trimmed;
        }

        /// <summary>确保数据集有可以写的属性存储；点要指到同一份存储上。</summary>
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

        /// <summary>声明（或复用）一个浮点列。</summary>
        private static float[] DeclareColumn(PCGAttributeSet attributes, string name, int count)
        {
            if (attributes == null)
                return null;

            if (!attributes.Contains(name))
                attributes.DeclareAttributeUnsafe(name, PCGAttributeType.Float);

            return attributes.GetWritableFloatArray(name);
        }

        /// <summary>声明（或复用）一个文本列。</summary>
        private static string[] DeclareTextColumn(PCGAttributeSet attributes, string name, int count)
        {
            if (attributes == null)
                return null;

            if (!attributes.Contains(name))
                attributes.DeclareAttributeUnsafe(name, PCGAttributeType.String);

            return attributes.GetWritableStringArray(name);
        }
    }
}

// PCG 插件 - 水文的可执行断言（见 docs/PCG_PLUGIN_DESIGN.md §5.1.36）
//
// 水文的错法都很"安静"：填洼少填一格，河会从一条本不存在的裂缝流出去；汇流累积算错，河的位置会整体偏移。
// 所以判据落在手算得出来的地形上——平面上每一行的汇流量必须等于行宽、一个凹坑必须变成恰好一处湖且水面高等于坑沿、
// 一条 V 形谷必须把河引到谷底线上。

using System;
using System.Collections.Generic;
using FlaxEngine;
using PCG;
using PCG.Execution;
using PCG.Nodes;

using CheckDelegate = PCGGoldenRunner.NodeBehaviourTests.CheckDelegate;

namespace PCGGoldenRunner
{
    internal static class HydrologyTests
    {
        private const int Width = 9;
        private const int Height = 9;
        private const float Cell = 50.0f;

        internal static void Run(CheckDelegate check)
        {
            FillTests(check);
            FlowTests(check);
            LakeTests(check);
            RiverTests(check);
            NodeTests(check);
        }

        /// <summary>填洼：只抬不降，且填完之后处处能流出去。</summary>
        private static void FillTests(CheckDelegate check)
        {
            var (heights, known) = Pit();
            var filled = PCGHydrology.Fill(heights, known, Width, Height);

            bool neverLowered = true;

            for (int i = 0; i < heights.Length; i++)
            {
                if (known[i] && filled[i] < heights[i] - 1e-4f)
                    neverLowered = false;
            }

            check("填洼只会抬高、不会降低", neverLowered);

            // 关键不变量：填完之后，内部每一格至少有一个不高于自己的邻格——否则水就出不去，
            // 那说明填洼没填到溢出点。
            bool drains = true;

            for (int z = 1; z < Height - 1 && drains; z++)
            {
                for (int x = 1; x < Width - 1 && drains; x++)
                {
                    int index = z * Width + x;

                    if (!known[index])
                        continue;

                    bool hasDownhill = false;

                    for (int dz = -1; dz <= 1 && !hasDownhill; dz++)
                    {
                        for (int dx = -1; dx <= 1 && !hasDownhill; dx++)
                        {
                            if (dx == 0 && dz == 0)
                                continue;

                            hasDownhill = filled[(z + dz) * Width + x + dx] <= filled[index] + 1e-4f;
                        }
                    }

                    drains = hasDownhill;
                }
            }

            check("填完之后内部每一格都有不高于自己的邻格（水能出去）", drains);

            // 凹坑被抬到坑沿：中心格（4,4）原来是 -10，四周是 0，所以填后应为 0。
            var (pitHeights, pitKnown) = Pit();
            var pitFilled = PCGHydrology.Fill(pitHeights, pitKnown, Width, Height);

            check("凹坑被抬到坑沿高度",
                Near(pitFilled[4 * Width + 4], 0.0f) && Near(pitHeights[4 * Width + 4], -10.0f),
                pitFilled[4 * Width + 4].ToString("F2"));

            // 平地处处已是自己的出口，填洼不该改动任何一格。
            var (flatHeights, flatKnown) = Plane();
            var flatFilled = PCGHydrology.Fill(flatHeights, flatKnown, Width, Height);

            bool unchanged = true;

            for (int i = 0; i < flatHeights.Length; i++)
                unchanged &= Near(flatFilled[i], flatHeights[i], 1e-4f);

            check("平地上填洼不改动任何一格", unchanged);
        }

        /// <summary>流向与汇流：平面上的汇流量可以手算。</summary>
        private static void FlowTests(CheckDelegate check)
        {
            // 沿 +x 下坡的平面：每一格都该流向自己的 +x 邻居（直步的下坡率大于斜步）。
            var (heights, known) = Plane();
            var filled = PCGHydrology.Fill(heights, known, Width, Height);
            var flow = PCGHydrology.FlowDirections(filled, known, Width, Height);

            bool straightDownhill = true;

            for (int z = 0; z < Height && straightDownhill; z++)
            {
                for (int x = 0; x < Width - 1 && straightDownhill; x++)
                    straightDownhill = flow[z * Width + x] == z * Width + x + 1;
            }

            check("沿 +x 下坡的平面上每一格都流向右邻", straightDownhill);

            var accumulation = PCGHydrology.Accumulate(filled, flow, known, Width, Height);

            // 每一行的水都流到该行最右格，所以最右列的累积量恰好等于行宽——这是能手算的事实。
            bool rowWidths = true;

            for (int z = 0; z < Height && rowWidths; z++)
                rowWidths = accumulation[z * Width + Width - 1] == Width;

            check("平面最右列的汇流量等于行宽", rowWidths,
                string.Join(",", new[]
                {
                    accumulation[Width - 1], accumulation[Width - 1 + Width], accumulation[Width - 1 + 2 * Width],
                }));

            check("边界格也有自己的 1 格汇流", accumulation[0] == 1 || accumulation[0] == Width);
        }

        /// <summary>湖：一个凹坑变成恰好一处湖，水面高等于坑沿。</summary>
        private static void LakeTests(CheckDelegate check)
        {
            var (heights, known) = Pit();
            var filled = PCGHydrology.Fill(heights, known, Width, Height);
            var flow = PCGHydrology.FlowDirections(filled, known, Width, Height);
            var lakes = new List<PCGLake>();

            int count = PCGHydrology.FindLakes(
                heights, filled, known, flow, Width, Cell, 1, 0.05f, lakes);

            check("凹坑变成一处湖", count == 1 && lakes.Count == 1, count.ToString());

            if (lakes.Count == 0)
                return;

            check("湖只有一个格子（凹坑只有一个格）", lakes[0].CellCount == 1, lakes[0].CellCount.ToString());
            check("水面高等于坑沿（0）", Near(lakes[0].WaterLevel, 0.0f), lakes[0].WaterLevel.ToString("F2"));
            check("湖心落在坑的位置（4,4）",
                Near(lakes[0].Center.X, 4.0f) && Near(lakes[0].Center.Z, 4.0f),
                lakes[0].Center.ToString());

            // 平的与非水地形不该报出湖：判据要是"填洼抬高了"，而不是"高度低"。
            var (planeHeights, planeKnown) = Plane();
            var planeFilled = PCGHydrology.Fill(planeHeights, planeKnown, Width, Height);
            var planeFlow = PCGHydrology.FlowDirections(planeFilled, planeKnown, Width, Height);
            var planeLakes = new List<PCGLake>();

            check("平面上不报湖",
                PCGHydrology.FindLakes(planeHeights, planeFilled, planeKnown, planeFlow, Width, Cell, 1, 0.05f,
                    planeLakes) == 0);
        }

        /// <summary>河：V 形谷把河引到谷底线上，且心线一路向下。</summary>
        private static void RiverTests(CheckDelegate check)
        {
            // ---- 区域是**发源判据**：越界的源头不该产出河，界内的照旧 ----
            // 一个 8×8 的纯斜面（水往 +X 汇），源头落在右侧列；于是"只准左三列发源"应当一条不出，
            // 而"只准右侧"应当与不设区域一模一样——两条一起才挡住"判据写反"和"一律不许"。
            const int RegionWidth = 8;
            const int RegionHeight = 8;

            var regionHeights = new float[RegionWidth * RegionHeight];
            var regionKnown = new bool[RegionWidth * RegionHeight];

            for (int rz = 0; rz < RegionHeight; rz++)
            {
                for (int rx = 0; rx < RegionWidth; rx++)
                {
                    regionHeights[rz * RegionWidth + rx] = -rx;
                    regionKnown[rz * RegionWidth + rx] = true;
                }
            }

            var regionFilled = PCGHydrology.Fill(regionHeights, regionKnown, RegionWidth, RegionHeight);
            var regionFlow = PCGHydrology.FlowDirections(regionFilled, regionKnown, RegionWidth, RegionHeight);
            var regionAccumulation = PCGHydrology.Accumulate(regionFilled, regionFlow, regionKnown,
                RegionWidth, RegionHeight);

            var regionNone = new List<PCGRiver>();
            PCGHydrology.FindRivers(regionAccumulation, regionFlow, regionKnown, RegionWidth, RegionHeight,
                3, 64, regionNone);

            var regionLeft = new List<PCGRiver>();
            PCGHydrology.FindRivers(regionAccumulation, regionFlow, regionKnown, RegionWidth, RegionHeight,
                3, 64, regionLeft, index => (index % RegionWidth) < 3);

            var regionRight = new List<PCGRiver>();
            PCGHydrology.FindRivers(regionAccumulation, regionFlow, regionKnown, RegionWidth, RegionHeight,
                3, 64, regionRight, index => (index % RegionWidth) >= 3);

            check("区域判据的对照：不设区域时这条斜面确实出河（否则下面两条是空转）",
                regionNone.Count > 0, $"{regionNone.Count} 条");

            // 实测（不是推的）：斜面 8×8、阈值 3 时源头落在 x=2 那一列——"上游都还没到阈值"的那一格，
            // 于是它**在左侧**。我原先按"水往低处汇，源头该在右侧"写反了判据的期望，两条一起红才暴露出来。
            check("区域盖住发源那一列时河一条不少（判据不该误杀界内的源）",
                regionLeft.Count == regionNone.Count && regionLeft.Count > 0,
                $"{regionLeft.Count} vs {regionNone.Count} 条");

            check("区域只准发源列的下游时一条河都不该有（判据真的在筛发源格，不是摆设）",
                regionRight.Count == 0, $"{regionRight.Count} 条");
            var (heights, known) = Valley();
            var filled = PCGHydrology.Fill(heights, known, Width, Height);
            var flow = PCGHydrology.FlowDirections(filled, known, Width, Height);
            var accumulation = PCGHydrology.Accumulate(filled, flow, known, Width, Height);
            var rivers = new List<PCGRiver>();

            int count = PCGHydrology.FindRivers(accumulation, flow, known, Width, Height, Width, 8, rivers);

            check("V 形谷里找得到河", count > 0 && rivers.Count > 0, count.ToString());

            if (rivers.Count == 0)
                return;

            // 谷底线是 z = 4 那一行（那里最低），所以每条河的心线都该贴着它。
            bool onValleyFloor = true;
            var river = rivers[0];

            for (int i = 0; i < river.Centerline.Length && onValleyFloor; i++)
                onValleyFloor = Math.Abs(river.Centerline[i].Z - 4.0f) <= 1.0f;

            check("河的心线贴着谷底线", onValleyFloor, river.Centerline.Length.ToString());

            // 心线必须一路往下（填洼后严格不升），否则就是流向算错了。
            bool downhill = true;

            for (int i = 1; i < river.Centerline.Length && downhill; i++)
            {
                int a = (int)river.Centerline[i - 1].Z * Width + (int)river.Centerline[i - 1].X;
                int b = (int)river.Centerline[i].Z * Width + (int)river.Centerline[i].X;

                downhill = filled[b] <= filled[a] + 1e-4f;
            }

            check("河的心线一路向下", downhill);
            check("河的峰值汇流达到阈值", river.PeakFlow >= Width, river.PeakFlow.ToString());

            // 阈值以下不该有河：把阈值抬到整个网格的格子数之上，一条也不该有。
            var none = new List<PCGRiver>();

            check("阈值高于最多可能汇流时没有河",
                PCGHydrology.FindRivers(accumulation, flow, known, Width, Height, Width * Height + 1, 8, none) == 0);

            // 确定性：同样的地形跑两次得到同样多的河与同样的心线。
            var again = new List<PCGRiver>();
            PCGHydrology.FindRivers(accumulation, flow, known, Width, Height, Width, 8, again);

            bool same = again.Count == rivers.Count;

            for (int i = 0; same && i < rivers.Count; i++)
                same = again[i].Centerline.Length == rivers[i].Centerline.Length;

            check("两次算出的河一致", same);
        }

        /// <summary>节点层：产出、列、以及没有地形时的契约。</summary>
        private static void NodeTests(CheckDelegate check)
        {
            var world = new PCGWorldContext { HeightSource = (x, z) => HeightAt(x, z) };
            var context = Context(world);

            var node = Make(riverThreshold: 9);
            var result = node.Execute(context, Array.Empty<PCGData>());

            check("节点在 V 形谷上产出水", result.Count > 0 && string.IsNullOrEmpty(node.LastError),
                $"{result.Count} 点：{node.LastError}");
            check("节点报告了河与湖的数量", node.LastRiverCount > 0, node.LastRiverCount.ToString());

            var flows = result.SharedAttributes?.TryGetFloatArray(PCGHydrologyNode.FlowColumn);
            check("河流点带汇流量列", flows != null && flows.Length == result.Count);

            check("每条河都输出了一条样条",
                result.Splines != null && result.Splines.Length == node.LastRiverCount,
                $"{result.Splines?.Length ?? 0} 条");

            check("水面高度取自地形（高于谷底）",
                result.Count > 0 && result.Points[0].Position.Y > -1.0f,
                result.Count > 0 ? result.Points[0].Position.Y.ToString("F1") : "无点");

            // 洼地地形：应当产出湖点，并带水面高与面积两列。
            var pitWorld = new PCGWorldContext { HeightSource = (x, z) => PitHeightAt(x, z) };
            var pitNode = Make(riverThreshold: 0);

            var pitResult = pitNode.Execute(Context(pitWorld), Array.Empty<PCGData>());

            check("凹坑地形上产出一处湖", pitNode.LastLakeCount == 1 && pitResult.Count == 1,
                $"{pitResult.Count} 点 / {pitNode.LastLakeCount} 湖");

            var levels = pitResult.SharedAttributes?.TryGetFloatArray(PCGHydrologyNode.WaterLevelColumn);
            var areas = pitResult.SharedAttributes?.TryGetFloatArray(PCGHydrologyNode.LakeAreaColumn);

            check("湖点带水面高与面积两列",
                levels != null && areas != null && Near(levels[0], 0.0f) && Near(areas[0], 1.0f),
                levels == null ? "没有列" : levels[0].ToString("F2"));

            // 没有地形：说清原因并产出为空，而不是给一片凭空的水。
            var blind = Make(9);
            var blindResult = blind.Execute(Context(new PCGWorldContext()), Array.Empty<PCGData>());

            check("没有高度源时说明原因并产出为空",
                blindResult.Count == 0 && !string.IsNullOrEmpty(blind.LastError) &&
                blind.LastError.Contains("terrain"),
                blind.LastError);

            // 范围退化：说人话而不是算一张 1×1 的网格。
            var tiny = Make(9);
            var tinyContext = Context(new PCGWorldContext { HeightSource = (x, z) => 0.0f });
            tinyContext.GenerationBounds = new BoundingBox(Vector3.Zero, new Vector3(10.0f, 10.0f, 10.0f));

            tiny.Execute(tinyContext, Array.Empty<PCGData>());
            check("生成范围不足一格时说明原因", !string.IsNullOrEmpty(tiny.LastError), tiny.LastError);
        }

        // ---- 夹具：地形都由世界的 XZ 位置直接算出，节点与纯函数用同一套 -------------------------------------

        private static PCGExecutionContext Context(PCGWorldContext world) => new PCGExecutionContext
        {
            Seed = 20261008,
            World = world,
            GenerationBounds = new BoundingBox(
                new Vector3(0.0f, -100.0f, 0.0f), new Vector3((Width - 1) * Cell, 100.0f, (Height - 1) * Cell)),
        };

        private static PCGHydrologyNode Make(int riverThreshold)
        {
            return new PCGHydrologyNode(new PCGNodeRegistry.PCGNodeSerializationData(
                PCGHydrologyNode.Id,
                new object[] { Cell, riverThreshold, 1, 0.05f, 0.0f, 8, true, true, true },
                0));
        }

        /// <summary>沿 +x 下坡的平面：高度 = -x。</summary>
        private static (float[] heights, bool[] known) Plane()
        {
            var heights = new float[Width * Height];
            var known = new bool[Width * Height];

            for (int z = 0; z < Height; z++)
            {
                for (int x = 0; x < Width; x++)
                {
                    heights[z * Width + x] = -x;
                    known[z * Width + x] = true;
                }
            }

            return (heights, known);
        }

        /// <summary>中心一个格子的凹坑（其余为 0，坑底 -10）。</summary>
        private static (float[] heights, bool[] known) Pit()
        {
            var heights = new float[Width * Height];
            var known = new bool[Width * Height];

            for (int i = 0; i < heights.Length; i++)
            {
                heights[i] = 0.0f;
                known[i] = true;
            }

            heights[4 * Width + 4] = -10.0f;
            return (heights, known);
        }

        /// <summary>V 形谷：谷底线在 z = 4，且沿 +x 下降——有水沿谷底汇成河所必需的纵向落差。</summary>
        private static (float[] heights, bool[] known) Valley()
        {
            var heights = new float[Width * Height];
            var known = new bool[Width * Height];

            for (int z = 0; z < Height; z++)
            {
                for (int x = 0; x < Width; x++)
                {
                    heights[z * Width + x] = Math.Abs(z - 4) * 10.0f - x * 2.0f;
                    known[z * Width + x] = true;
                }
            }

            return (heights, known);
        }

        /// <summary>节点用的地形：按世界 XZ 采样同一条 V 形谷（含纵向落差）。</summary>
        private static float? HeightAt(float worldX, float worldZ)
        {
            int x = (int)Math.Round(worldX / Cell);
            int z = (int)Math.Round(worldZ / Cell);

            return Math.Abs(z - 4) * 10.0f - x * 2.0f;
        }

        /// <summary>节点用的地形：按世界 XZ 采样凹坑。</summary>
        private static float? PitHeightAt(float worldX, float worldZ)
        {
            int x = (int)Math.Round(worldX / Cell);
            int z = (int)Math.Round(worldZ / Cell);

            return x == 4 && z == 4 ? -10.0f : 0.0f;
        }

        private static bool Near(float a, float b, float tolerance = 1e-3f) => Math.Abs(a - b) <= tolerance;
    }
}

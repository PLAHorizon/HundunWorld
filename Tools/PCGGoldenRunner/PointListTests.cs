// PCG 插件 - 字面点节点的可执行断言（见 docs/PCG_PLUGIN_DESIGN.md §5.1.40）
//
// 这个节点只有一个承诺："位置就是我写的那几个，不多不少"。所以断言围着这句话：槽位顺序、个数钳制、
// 缩放/密度/种子各归各位、以及"换种子不许动位置"。最后一组把它接进 `Path`，因为字面点真正的用途就是给路
// 当端点——它存在的前提是"从这里修到那里"能用图表达出来。

using System;
using System.Collections.Generic;
using FlaxEngine;
using PCG;
using PCG.Execution;
using PCG.Nodes;

using CheckDelegate = PCGGoldenRunner.NodeBehaviourTests.CheckDelegate;

namespace PCGGoldenRunner
{
    internal static class PointListTests
    {
        internal static void Run(CheckDelegate check)
        {
            LayoutTests(check);
            ClampTests(check);
            WaypointTests(check);
        }

        /// <summary>位置按槽位顺序原样出来，缩放/密度/种子各归各位。</summary>
        private static void LayoutTests(CheckDelegate check)
        {
            var node = Make(new Vector3(10, 5, -20), new Vector3(300, 0, 400), new Vector3(999, 999, 999), new Vector3(0, 0, 0),
                count: 3, scale: 2.0f, density: 0.25f, seedOffset: 7);

            var result = node.Execute(Context(), Array.Empty<PCGData>());

            check("几个槽位就出几个点", result != null && result.Count == 3,
                result?.Count.ToString() ?? "null");

            if (result == null || result.Count != 3)
                return;

            // 逐点对坐标：位置是写死的，任何"顺手挪一下"都是缺陷。
            check("位置与槽位逐个相等，顺序不变",
                Same(result.Points[0].Position, new Vector3(10, 5, -20)) &&
                Same(result.Points[1].Position, new Vector3(300, 0, 400)) &&
                Same(result.Points[2].Position, new Vector3(999, 999, 999)),
                $"{result.Points[0].Position} / {result.Points[1].Position} / {result.Points[2].Position}");

            check("没用到的那几个槽位不产生点（第 3 维的 999 不该出现在输出里）",
                result.Count == 3 && !Same(result.Points[2].Position, new Vector3(0, 0, 0)));

            check("缩放写进了点的缩放",
                Near(result.Points[0].Scale.X, 2.0f) && Near(result.Points[1].Scale.Z, 2.0f),
                result.Points[0].Scale.ToString());

            check("密度写进了点的密度", Near(result.Points[0].Density, 0.25f),
                result.Points[0].Density.ToString("F3"));

            // 包围盒不能是退化的：下游的剪枝/自碰撞要靠它。
            var bounds = result.Points[0].Bounds;
            check("包围盒跟着缩放长出来（不是零体积）",
                bounds.Maximum.X - bounds.Minimum.X > 0.0f &&
                bounds.Maximum.Y - bounds.Minimum.Y > 0.0f,
                $"{bounds.Minimum}..{bounds.Maximum}");

            // 只有出点，没有输入：字面点是源头。
            check("字面点没有输入引脚",
                node.InputPins != null && node.InputPins.Length == 0);

            // 换种子偏移：位置一个字都不许动，只有点的种子变。
            var shifted = Make(new Vector3(10, 5, -20), new Vector3(300, 0, 400), new Vector3(999, 999, 999),
                new Vector3(0, 0, 0), count: 3, scale: 2.0f, density: 0.25f, seedOffset: 99);

            var other = shifted.Execute(Context(), Array.Empty<PCGData>());

            check("换种子偏移不移动任何位置",
                other != null && other.Count == 3 &&
                Same(other.Points[0].Position, result.Points[0].Position) &&
                Same(other.Points[2].Position, result.Points[2].Position));

            check("换种子偏移确实换了点的种子",
                other != null && other.Count == 3 && other.Points[0].Seed != result.Points[0].Seed,
                $"{other?.Points[0].Seed} vs {result.Points[0].Seed}");

            // 同参数跑两次必须逐点相同。
            var repeat = Make(new Vector3(10, 5, -20), new Vector3(300, 0, 400), new Vector3(999, 999, 999),
                new Vector3(0, 0, 0), count: 3, scale: 2.0f, density: 0.25f, seedOffset: 7);

            var again = repeat.Execute(Context(), Array.Empty<PCGData>());

            check("同参数两次运行逐点相同",
                again != null && again.Count == 3 && Same(again.Points[1].Position, result.Points[1].Position) &&
                again.Points[1].Seed == result.Points[1].Seed);
        }

        /// <summary>个数被钳在 [0, 槽位数]；一个都不用就如实给空并说明原因。</summary>
        private static void ClampTests(CheckDelegate check)
        {
            var zero = Make(Vector3.Zero, Vector3.Zero, Vector3.Zero, Vector3.Zero, count: 0);
            var empty = zero.Execute(Context(), Array.Empty<PCGData>());

            check("个数为 0 时给空层并说明原因",
                empty != null && empty.Count == 0 && !string.IsNullOrEmpty(zero.LastError),
                zero.LastError);

            // 上面那条的正面同夹具：同一次构造、个数改成 1，就必须有点。否则"空层"可能只是节点根本没跑。
            var one = Make(Vector3.Zero, Vector3.Zero, Vector3.Zero, Vector3.Zero, count: 1);
            var single = one.Execute(Context(), Array.Empty<PCGData>());

            check("个数为 1 时恰好一个点（上一条的正面同夹具）",
                single != null && single.Count == 1, single?.Count.ToString());

            var many = Make(Vector3.One, Vector3.One, Vector3.One, Vector3.One, count: 99);
            var clamped = many.Execute(Context(), Array.Empty<PCGData>());

            check("个数超过槽位数时按槽位数封顶",
                clamped != null && clamped.Count == PCGPointListNode.SlotCount,
                clamped?.Count.ToString() ?? "null");

            var negative = Make(Vector3.One, Vector3.One, Vector3.One, Vector3.One, count: -3);
            var none = negative.Execute(Context(), Array.Empty<PCGData>());

            check("个数为负时按 0 处理（不抛异常）", none != null && none.Count == 0);
        }

        /// <summary>字面点作为途经点喂给 `Path`：规划出来的路要从写下的地方开始、到写下的地方结束。</summary>
        private static void WaypointTests(CheckDelegate check)
        {
            // 默认槽位排成一条 +X 的线——新拖出来的节点不该是退化的（两个重合的点连不出路）。
            var defaults = Make(new Vector3(0, 0, 0), new Vector3(500, 0, 0), new Vector3(1000, 0, 0),
                new Vector3(1500, 0, 0), count: 2);

            var ends = defaults.Execute(Context(), Array.Empty<PCGData>());

            check("默认前两个槽位是分开的两点（新节点不退化）",
                ends != null && ends.Count == 2 &&
                (ends.Points[1].Position - ends.Points[0].Position).Length > 1.0f,
                ends == null ? "null" : $"{ends.Points[0].Position} -> {ends.Points[1].Position}");

            // 字面端点 → 路：用一条对角线段，两端各落在平地上。
            var list = Make(new Vector3(0, 0, 0), new Vector3(1000, 0, 1000), Vector3.Zero, Vector3.Zero, count: 2);

            var waypoints = list.Execute(Context(), Array.Empty<PCGData>());

            if (waypoints == null || waypoints.Count != 2)
            {
                check("路：字面端点能喂给路径规划器", false, "端点没出来");
                return;
            }

            var path = new PCGPathNode(new PCGNodeRegistry.PCGNodeSerializationData(
                PCGPathNode.Id, new object[] { 50.0f, 25.0f, 3.0f, -1000000.0f, 900.0f, 3, 50.0f, true }, 0));

            var world = new PCGWorldContext { HeightSource = Flat };
            var route = path.Execute(Context(world), new[] { waypoints });

            check("路：字面端点能喂给路径规划器并出路", route != null && route.Count > 1,
                route == null ? "null" : $"{route.Count} point(s)");

            if (route == null || route.Count < 2)
                return;

            // 端点会落到规划网格的格心上，所以允许一个格的偏差。
            var first = route.Points[0].Position;
            var last = route.Points[route.Count - 1].Position;

            check("路的起点在写下的起点附近",
                Math.Abs(first.X) <= 50.0f && Math.Abs(first.Z) <= 50.0f, first.ToString());

            check("路的终点在写下的终点附近",
                Math.Abs(last.X - 1000.0f) <= 50.0f && Math.Abs(last.Z - 1000.0f) <= 50.0f, last.ToString());

            // 一路点应当大致沿对角线走，而不是绕远——这正是"低代价路径"的意思，也证明端点真的被用上了。
            float maxOffLine = 0.0f;

            for (int i = 0; i < route.Count; i++)
            {
                var p = route.Points[i].Position;
                maxOffLine = Math.Max(maxOffLine, Math.Abs(p.X - p.Z));
            }

            check("一路点大致贴在对角线上（不是绕远路）", maxOffLine <= 150.0f, maxOffLine.ToString("F1"));
        }

        // ---- 夹具 --------------------------------------------------------------------------------------

        private static PCGPointListNode Make(Vector3 p1, Vector3 p2, Vector3 p3, Vector3 p4,
            int count = 2, float scale = 1.0f, float density = 1.0f, int seedOffset = 0)
        {
            return new PCGPointListNode(new PCGNodeRegistry.PCGNodeSerializationData(
                PCGPointListNode.Id,
                new object[]
                {
                    count, p1, p2, p3, p4,
                    new Vector3(0, 0, 0), new Vector3(0, 0, 0), new Vector3(0, 0, 0), new Vector3(0, 0, 0),
                    scale, density, seedOffset,
                },
                0));
        }

        private static PCGExecutionContext Context(PCGWorldContext world = null) => new PCGExecutionContext
        {
            Seed = 20261008,
            World = world ?? new PCGWorldContext(),
            GenerationBounds = new BoundingBox(new Vector3(-2000, -100, -2000), new Vector3(2000, 100, 2000)),
        };

        /// <summary>平地。</summary>
        private static float? Flat(float x, float z) => 0.0f;

        private static bool Same(Vector3 a, Vector3 b) => (a - b).Length < 1e-4f;

        private static bool Near(float a, float b, float tolerance = 1e-3f) => Math.Abs(a - b) <= tolerance;
    }
}

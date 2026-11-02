// PCG 插件 - 流式格子选择的可执行测试。
//
// 流式的全部承诺就是一个窗口：把近的生成出来、让两个半径之间那一圈别动、
// 把远的交回来。三部分都是规划好的格子上的纯几何，所以它们在这里对着
// 一份独立写出的距离参照被钉住，而不是只靠看着一次在世界里的走动来验。

using System;
using System.Collections.Generic;
using FlaxEngine;
using PCG;
using PCG.Execution;
using PCG.Graph;
using PCG.Nodes;

using CheckDelegate = PCGGoldenRunner.NodeBehaviourTests.CheckDelegate;

namespace PCGGoldenRunner
{
    internal static class StreamingTests
    {
        private static CheckDelegate _check;

        // 先写条件，于是一句断言读起来是"这件事成立，因为……"。
        private static void check(bool ok, string what, string detail = null) => _check(what, ok, detail);

        private const float RegionHalf = 1200.0f;
        private const float CellSize = 400.0f;

        internal static void Run(CheckDelegate callback)
        {
            _check = callback;

            PCGNodeRegistry.Clear();
            PCG.Runtime.PCGNodeRegistration.RegisterAll();

            try
            {
                SelectionTests();
                ChunkedTests();
                VisibleChunkTests();
                ChunkSourceTests();
                HysteresisTests();
                BudgetTests();
                BudgetWorkSplitTests();
                TriggerTests();
                MultiViewerTests();
                ViewerReductionTests();
                ViewerSourceTests();
                HierarchyTests();
                DensityModeTests();
                ParameterSlotTests();
                CacheEvictionTests();
                WindowEquivalenceTests();
            }
            finally
            {
                PCGNodeRegistry.Clear();
            }
        }

        // ---- 选择 ------------------------------------------------------

        private static void SelectionTests()
        {
            var cells = Cells();

            // 夹具必须是一张真网格，否则下面每一条断言比的都是两份空列表。
            check(cells.Count == 36, "the fixture plans a 6x6 window over the region", $"{cells.Count} cell(s)");

            var origin = new Vector3();

            var active = new List<PCGPartitionCell>();
            var evicted = new List<PCGPartitionCell>();

            PCGCellStreaming.Select(cells, origin, 0.0f, 0.0f, active, evicted);
            check(active.Count == 4 && evicted.Count == 32,
                "a zero radius keeps only the cells the centre lies in, and hands the rest back",
                $"active={active.Count} evicted={evicted.Count}");

            // 正好落在格子边上的点，在 gap 为 0 时同时处在两个格子的矩形内，这就是上面
            // 那个计数是 4 而不是 1 的原因：窗口是按到矩形的距离切的，
            // 不是按中心名义上落在哪个格子切的。
            PCGCellStreaming.Select(cells, origin, 500.0f, 500.0f, active, evicted);
            check(active.Count > 4 && active.Count < cells.Count,
                "a mid-size radius selects a proper subset, not all or nothing",
                $"active={active.Count}");

            PCGCellStreaming.Select(cells, origin, 100000.0f, 100000.0f, active, evicted);
            check(active.Count == cells.Count && evicted.Count == 0,
                "a radius past the region selects everything and evicts nothing",
                $"active={active.Count} evicted={evicted.Count}");

            var far = new Vector3(100000.0f, 0.0f, -100000.0f);
            PCGCellStreaming.Select(cells, far, 100.0f, 200.0f, active, evicted);
            check(active.Count == 0 && evicted.Count == cells.Count,
                "a centre far outside the region empties it",
                $"active={active.Count} evicted={evicted.Count}");

            // 窗口是个圆，不是四分之一象限：中心落在区域的角上时，
            // 仍要够到半径之内的每一个格子，而它够不着的是区域的另一头。
            var corner = new Vector3(-RegionHalf, 0.0f, -RegionHalf);
            PCGCellStreaming.Select(cells, corner, 500.0f, 500.0f, active, evicted);
            int cornerActive = active.Count;
            PCGCellStreaming.Select(cells, new Vector3(RegionHalf, 0.0f, RegionHalf), 500.0f, 500.0f, active, evicted);
            check(cornerActive == active.Count,
                "the same radius at opposite corners selects the same number of cells",
                $"{cornerActive} vs {active.Count}");

            // 垂直距离刻意不进这条测试。
            var high = new PCGPartitionCell(
                new BoundingBox(new Vector3(0.0f, 5000.0f, 0.0f), new Vector3(400.0f, 5400.0f, 400.0f)),
                new BoundingBox(new Vector3(0.0f, 5000.0f, 0.0f), new Vector3(400.0f, 5400.0f, 400.0f)),
                0, 0);
            PCGCellStreaming.Select(new List<PCGPartitionCell> { high }, origin, 0.0f, 0.0f, active, evicted);
            check(active.Count == 1 && evicted.Count == 0,
                "a cell 5000 units above the centre is still inside a zero-radius window",
                $"active={active.Count} evicted={evicted.Count}");

            // 复用调用方的两个列表是体积那边的用法，所以残留内容会静默地把
            // 上一次请求的窗口漏进这一次来。
            PCGCellStreaming.Select(cells, origin, 0.0f, 0.0f, active, evicted);
            int before = active.Count;
            PCGCellStreaming.Select(new List<PCGPartitionCell>(), origin, 0.0f, 0.0f, active, evicted);
            check(before == 4 && active.Count == 0 && evicted.Count == 0,
                "Select clears the caller's lists before filling them",
                $"active={active.Count} evicted={evicted.Count}");

            CheckThrows(() => PCGCellStreaming.Select(null, origin, 10.0f, 10.0f, active, evicted), "null cells");
            CheckThrows(() => PCGCellStreaming.Select(cells, origin, 10.0f, 10.0f, null, evicted), "null active list");
            CheckThrows(() => PCGCellStreaming.Select(cells, origin, 10.0f, 10.0f, active, null), "null evicted list");
        }

        // ---- 滞回与参照 ------------------------------------

        private static void HysteresisTests()
        {
            var cells = Cells();
            var origin = new Vector3();

            var active = new List<PCGPartitionCell>();
            var evicted = new List<PCGPartitionCell>();

            // 两个半径之间那一圈正是"为什么要两个半径"的全部理由：踩着单条边界走，
            // 就会每一步都在重建并销毁同一个格子。
            PCGCellStreaming.Select(cells, origin, 400.0f, 1000.0f, active, evicted);
            int untouched = cells.Count - active.Count - evicted.Count;
            check(untouched > 0,
                "cells between the two radii are neither generated nor evicted",
                $"{untouched} of {cells.Count} left standing");

            var keptKeys = new HashSet<(int, int)>();
            foreach (var cell in active)
                keptKeys.Add((cell.IndexX, cell.IndexZ));
            int ring = 0;
            for (int i = 0; i < cells.Count; i++)
            {
                float d = DistanceToCell(cells[i], origin);
                if (d > 400.0f && d <= 1000.0f)
                    ring++;
            }
            check(untouched == ring,
                "the standing ring is exactly the cells between the two distances",
                $"{untouched} vs {ring}");

            // 保持半径若小于流式半径，会把一次请求正在生成的格子赶走。
            foreach (var pair in new[] { (400.0f, 100.0f), (400.0f, 400.0f), (400.0f, 0.0f), (0.0f, 0.0f),
                                         (250.0f, 5000.0f), (900.0f, 899.5f) })
            {
                PCGCellStreaming.Select(cells, origin, pair.Item1, pair.Item2, active, evicted);

                keptKeys.Clear();
                foreach (var cell in active)
                    keptKeys.Add((cell.IndexX, cell.IndexZ));

                bool overlap = false;
                for (int i = 0; i < evicted.Count; i++)
                {
                    if (keptKeys.Contains((evicted[i].IndexX, evicted[i].IndexZ)))
                    {
                        overlap = true;
                        break;
                    }
                }

                check(!overlap,
                    $"no cell is both generated and handed back at radius {pair.Item1:0.#} keep {pair.Item2:0.#}",
                    $"{active.Count} active, {evicted.Count} evicted");

                // 对每一个格子都和参照比对，而不只是对进了列表的那些。
                int expectedActive = 0, expectedEvicted = 0;
                float keep = Math.Max(pair.Item1, pair.Item2);
                for (int i = 0; i < cells.Count; i++)
                {
                    float d = DistanceToCell(cells[i], origin);
                    if (d <= pair.Item1)
                        expectedActive++;
                    else if (d > keep)
                        expectedEvicted++;
                }

                check(active.Count == expectedActive && evicted.Count == expectedEvicted,
                    $"the window and the eviction set match the distance reference at " +
                    $"{pair.Item1:0.#}/{pair.Item2:0.#}",
                    $"got {active.Count}/{evicted.Count}, reference {expectedActive}/{expectedEvicted}");
            }
        }

        // ---- 每请求的格子预算 -----------------------------------------

        private static void BudgetTests()
        {
            var cells = Cells();
            var origin = new Vector3();

            var active = new List<PCGPartitionCell>();
            var evicted = new List<PCGPartitionCell>();

            PCGCellStreaming.Select(cells, origin, 400.0f, 1000.0f, active, evicted);
            int window = active.Count;
            var evictedUnbudgeted = new List<PCGPartitionCell>(evicted);
            var wholeWindow = new List<PCGPartitionCell>(active);

            // 预算 0 或负数的请求必须和过去那版"无预算"完全一致，否则"0"就会因为
            // 谁先读到这个参数而有两个意思。
            foreach (int unlimited in new[] { 0, -1, -4096 })
            {
                PCGCellStreaming.Select(cells, origin, 400.0f, 1000.0f, active, evicted, unlimited);
                check(active.Count == window && evicted.Count == evictedUnbudgeted.Count &&
                          ReportedDeferred(cells, origin, unlimited) == 0,
                      $"a budget of {unlimited} means the whole window in one request",
                      $"active={active.Count} of {window}");
            }

            check(window > 3, "the fixture's window is wider than the budgets under test", $"{window} cell(s)");

            const int Budget = 3;
            int deferred = PCGCellStreaming.Select(cells, origin, 400.0f, 1000.0f, active, evicted, Budget);
            check(active.Count == Budget && deferred == window - Budget,
                "the budget keeps exactly its own number of cells and reports the rest as deferred",
                $"active={active.Count} deferred={deferred} of {window}");

            // 让"延后"安全的那条性质：被预算落下的格子不会被赶走。赶走它们
            // 就等于销毁这次请求只想推迟的那些道具。
            bool anyDeferredWasEvicted = false;
            foreach (var cell in wholeWindow)
            {
                if (Contains(active, cell))
                    continue;
                if (Contains(evicted, cell))
                    anyDeferredWasEvicted = true;
            }
            check(!anyDeferredWasEvicted, "a deferred cell is neither generated nor handed back");

            check(evicted.Count == evictedUnbudgeted.Count && SameSet(evicted, evictedUnbudgeted),
                "the eviction set is identical with and without a budget",
                $"{evicted.Count} vs {evictedUnbudgeted.Count}");

            // 被选中的每一个都至少和每一个被延后的一样近。预算要落在两个距离档之间，
            // 这条陈述才有意义：半径 400 时这个窗口有 4 个距离 0 的格子和
            // 8 个距离 400 的格子，所以预算 3 会留下一个距离 0 的被延后格子，
            // 而"最远的保留 <= 最近的延后"就会读成 0 <= 0，什么也证明不了。
            var nearestTier = new List<PCGPartitionCell>();
            foreach (var cell in wholeWindow)
            {
                if (DistanceToCell(cell, origin) == 0.0f)
                    nearestTier.Add(cell);
            }

            check(nearestTier.Count > 1 && nearestTier.Count < window,
                "the fixture has a nearest tier and something further out to defer",
                $"{nearestTier.Count} of {window}");

            PCGCellStreaming.Select(cells, origin, 400.0f, 1000.0f, active, evicted, nearestTier.Count);
            check(active.Count == nearestTier.Count && SameSet(active, nearestTier),
                "a budget exactly the size of the nearest tier keeps precisely that tier",
                $"kept {active.Count} of a {nearestTier.Count}-cell tier");

            float worstSelected = 0.0f;
            foreach (var cell in active)
                worstSelected = Math.Max(worstSelected, DistanceToCell(cell, origin));

            float bestDeferred = float.MaxValue;
            foreach (var cell in wholeWindow)
            {
                if (Contains(active, cell))
                    continue;
                bestDeferred = Math.Min(bestDeferred, DistanceToCell(cell, origin));
            }
            check(worstSelected < bestDeferred,
                "the budget spends itself on the nearest cells of the window",
                $"farthest kept {worstSelected:0.#}, nearest deferred {bestDeferred:0.#}");

            // 对称网格会精确平手（中心不落在角上时有四个格子同为距离 0），所以
            // 平手的胜者必须由格子自己的下标决定，而不是由规划器发出的顺序决定。
            var reversed = new List<PCGPartitionCell>(cells);
            reversed.Reverse();

            PCGCellStreaming.Select(cells, origin, 0.0f, 0.0f, active, evicted, 1);
            PCGCellStreaming.Select(reversed, origin, 0.0f, 0.0f, active, evicted, 1);
            var firstChoice = new List<PCGPartitionCell>(active);
            PCGCellStreaming.Select(cells, origin, 0.0f, 0.0f, active, evicted, 1);
            check(firstChoice.Count == 1 && active.Count == 1 &&
                      firstChoice[0].IndexX == active[0].IndexX && firstChoice[0].IndexZ == active[0].IndexZ,
                "a tied budget decision does not depend on the plan's cell order",
                $"{firstChoice[0].Label} vs {active[0].Label}");

            // 而装得下的预算不是一次裁剪：窗口仍按规划顺序、原样不动。
            int fitted = PCGCellStreaming.Select(cells, origin, 400.0f, 1000.0f, active, evicted, window);
            check(fitted == 0 && active.Count == window && active[0].Label == wholeWindow[0].Label,
                "a budget equal to the window changes nothing",
                $"deferred={fitted} active={active.Count}");
        }

        // ---- 分块对齐的窗口 -------------------------------------------

        private static void ChunkedTests()
        {
            var cells = Cells();
            var active = new List<PCGPartitionCell>();
            var evicted = new List<PCGPartitionCell>();

            // 站在第一个 chunk 内、半径小于一个 chunk：分块的窗口是一个 chunk 方块，
            // 而同一个半径切出来的圆盘明显更小。
            var origin = new Vector3(50.0f, 0.0f, 50.0f);
            const float radius = 100.0f;

            PCGCellStreaming.Select(cells, origin, radius, radius, active, evicted);
            var circular = new List<PCGPartitionCell>(active);

            PCGCellStreaming.Select(cells, origin, radius, radius, active, evicted, 0, null, 0.0f);
            check(active.Count == circular.Count && SameSet(active, circular),
                "a chunk size of zero is the circular window, unchanged",
                $"{active.Count} vs {circular.Count}");

            PCGCellStreaming.Select(cells, origin, radius, radius, active, evicted, 0, null, CellSize);
            var chunked = new List<PCGPartitionCell>(active);

            check(chunked.Count > circular.Count && SameSet(circular, chunked),
                "chunking only ever grows the window over what the radius carved",
                $"chunked={chunked.Count} circular={circular.Count}");

            // 在 400 的 chunk 里半径 100 会朝上进到每个方向一个 chunk，也就是每轴 3 个 chunk。
            // 算的是重叠（不是相接），所以是一整块 3x3 的格子——这里 chunk 尺寸与格子尺寸相等，
            // 而区域起点正好在 chunk 边界上。
            check(chunked.Count == 9, "a radius inside one chunk yields a 3x3 block of whole chunks",
                chunked.Count.ToString());
            check(IsSolidBlock(chunked), "the chunked window is a solid block of cells, with no holes",
                Block(chunked));

            // 整个模式存在的那条性质：观察者在自己的 chunk *里面*站在哪儿不该有影响。
            PCGCellStreaming.Select(cells, new Vector3(350.0f, 0.0f, 350.0f), radius, radius, active, evicted, 0, null, CellSize);
            check(active.Count == chunked.Count && SameSet(active, chunked),
                "moving to the other corner of the same chunk changes nothing",
                $"{active.Count} vs {chunked.Count}");

            // 那条陈述的正向孪生：跨进下一个 chunk 必须让窗口移动，否则上面那条断言
            // 对一个干脆忽略观察者的实现也同样成立。
            PCGCellStreaming.Select(cells, new Vector3(450.0f, 0.0f, 350.0f), radius, radius, active, evicted, 0, null, CellSize);
            check(!SameSet(active, chunked) || active.Count != chunked.Count,
                "crossing into the next chunk does move the window",
                $"{active.Count} vs {chunked.Count}");

            // 舍入是向上取整：400 的 chunk 里 500 单位是每方向两个 chunk，于是每轴 5 个。
            PCGCellStreaming.Select(cells, origin, 500.0f, 500.0f, active, evicted, 0, null, CellSize);
            check(active.Count == 25 && IsSolidBlock(active),
                "a radius needing two chunks each way gives a 5x5 block",
                $"{active.Count} cell(s), {Block(active)}");

            // 分块模式下驱逐规则不变。
            PCGCellStreaming.Select(cells, new Vector3(100000.0f, 0.0f, 0.0f), radius, radius, active, evicted, 0, null, CellSize);
            check(active.Count == 0 && evicted.Count == cells.Count,
                "a viewer far away still evicts every cell when chunked",
                $"active={active.Count} evicted={evicted.Count}");

            PCGCellStreaming.Select(cells, new Vector3(0.0f, 0.0f, 0.0f), radius, radius, active, evicted, 0, null, CellSize);
            evicted.Clear();
            PCGCellStreaming.SelectAround(cells, new List<Vector3>(), radius, radius, active, evicted, 0, null, CellSize);
            check(active.Count == 0 && evicted.Count == cells.Count,
                "no viewers is still an empty window in chunked mode",
                $"active={active.Count} evicted={evicted.Count}");
        }

        // ---- 世界自己的可见 chunk 清单 --------------------------------

        private static void VisibleChunkTests()
        {
            var cells = Cells();
            var active = new List<PCGPartitionCell>();
            var evicted = new List<PCGPartitionCell>();
            var noViewers = new List<Vector3>();

            // 这里 chunk 尺寸等于格子尺寸，而区域起点在 chunk 边界上，所以一个 chunk 正好
            // 是一个格子：算式保持可读，错误也没法藏在一次舍入里。
            check(cells.Count == 36, "the fixture is still the 6x6 grid", cells.Count.ToString());

            PCGCellStreaming.SelectChunks(cells, new[] { new Int2(0, 0) }, CellSize, noViewers, active, evicted);
            check(active.Count == 1 && evicted.Count == 35,
                "the world listing one chunk generates exactly that chunk and hands the rest back",
                $"active={active.Count} evicted={evicted.Count}");

            PCGCellStreaming.SelectChunks(cells, new[] { new Int2(0, 0), new Int2(0, 0) }, CellSize, noViewers, active, evicted);
            check(active.Count == 1, "listing the same chunk twice does not duplicate its cells",
                active.Count.ToString());

            var block = new[] { new Int2(0, 0), new Int2(1, 0), new Int2(0, 1), new Int2(1, 1) };
            PCGCellStreaming.SelectChunks(cells, block, CellSize, noViewers, active, evicted);
            var four = new List<PCGPartitionCell>(active);
            check(active.Count == 4 && IsSolidBlock(active),
                "a square of four listed chunks is a solid 2x2 block of cells",
                $"{active.Count} cell(s), {Block(active)}");

            // 相距很远的两个 chunk 必须给出两个格子，而不是它们之间的包围盒——这是
            // "照清单办事"与"只拿清单的两个角"的区别。
            PCGCellStreaming.SelectChunks(cells, new[] { new Int2(-3, -3), new Int2(2, 2) }, CellSize, noViewers, active, evicted);
            check(active.Count == 2, "chunks that are not adjacent do not fill the space between them",
                $"{active.Count} cell(s), {Block(active)}");

            // 这条边界正是本模式表达滞回的方式——径向模式是靠第二个半径得到的。
            PCGCellStreaming.SelectChunks(cells, new[] { new Int2(0, 0) }, CellSize, noViewers, active, evicted, 0, null, 1);
            check(active.Count == 9 && IsSolidBlock(active),
                "a border of one chunk grows the window to the surrounding 3x3",
                $"{active.Count} cell(s), {Block(active)}");

            // 与派生那条路交叉验证：由半径推出的分块窗口所覆盖的那批 chunk，
            // 显式交进来时必须选到同一批格子。两者一旦不一致，插件就在对同一个窗口
            // 向世界说出两种不同的话。
            var origin = new Vector3(50.0f, 0.0f, 50.0f);
            const float radius = 100.0f;

            PCGCellStreaming.Select(cells, origin, radius, radius, active, evicted, 0, null, CellSize);
            var derived = new List<PCGPartitionCell>(active);

            var covering = new List<Int2>();
            for (int z = -1; z <= 1; z++)
            {
                for (int x = -1; x <= 1; x++)
                    covering.Add(new Int2(x, z));
            }

            PCGCellStreaming.SelectChunks(cells, covering, CellSize, noViewers, active, evicted);
            check(active.Count == derived.Count && SameSet(active, derived),
                "the explicit chunk set that covers the derived window selects the same cells",
                $"explicit={active.Count} derived={derived.Count}");

            // 一个什么都没加载的世界是合法状态：窗口为空，而不是填满。
            PCGCellStreaming.SelectChunks(cells, new Int2[0], CellSize, noViewers, active, evicted);
            check(active.Count == 0 && evicted.Count == cells.Count,
                "an empty chunk list empties the window rather than generating everything",
                $"active={active.Count} evicted={evicted.Count}");

            // 预算与其他所有模式共用，所以在这里也得管用。
            int deferred = PCGCellStreaming.SelectChunks(cells, block, CellSize, noViewers, active, evicted, 2);
            check(deferred == 2 && active.Count == 2,
                "the per-request budget defers cells in this mode as well",
                $"deferred={deferred} active={active.Count}");

            check(PCGCellStreaming.SelectChunks(cells, block, CellSize, new[] { origin }, active, evicted, 2) >= 0,
                "a viewer list is accepted for ranking without changing the selection rule");
        }

        // ---- 读世界的 chunk 清单 --------------------------------------

        private static void ChunkSourceTests()
        {
            var scratch = new List<Int2>();
            var into = new List<Int2>();

            int honest = PCGStreamChunks.FromSource(
                list =>
                {
                    list.Add(new Int2(1, 2));
                    list.Add(new Int2(3, 4));
                    list.Add(new Int2(5, 6));
                    return 3;
                }, scratch, into);

            check(honest == 3 && into.Count == 3 && into[2].X == 5 && into[2].Y == 6,
                "an honest source's chunks come through in order", $"{honest} chunk(s)");

            // 要紧的那一条：缓冲区是复用的，所以一个声称写得比实际多的来源，
            // 否则就会把上一次请求的那些 chunk 重放一遍。
            int overclaim = PCGStreamChunks.FromSource(
                list =>
                {
                    list.Add(new Int2(7, 8));
                    list.Add(new Int2(9, 10));
                    return 7;
                }, scratch, into);

            check(overclaim == 2 && into.Count == 2 && into[1].X == 9,
                "a source claiming more than it wrote is clamped to what the list holds, with no stale tail",
                $"{overclaim} chunk(s): {string.Join(" ", into)}");
            check(scratch.Count == 2, "the working list is trimmed to what the source really produced",
                scratch.Count.ToString());

            check(PCGStreamChunks.FromSource(list => -1, scratch, into) == 0,
                "a negative count reads as nothing rather than as everything");

            check(PCGStreamChunks.FromSource(null, scratch, into) == 0 && into.Count == 0,
                "no source at all is no chunks, not a crash");

            // 跨调用复用不许漏：后一次调用只看到自己那个来源写进去的东西。
            int single = PCGStreamChunks.FromSource(
                list =>
                {
                    list.Add(new Int2(0, 0));
                    return 1;
                }, scratch, into);

            check(single == 1 && into.Count == 1 && into[0].X == 0,
                "a reused buffer carries nothing over from the previous request",
                $"{single} chunk(s)");
        }

        /// <summary>格子恰好填满它们的下标包围盒时——一块没有洞的矩形——为真。</summary>
        private static bool IsSolidBlock(List<PCGPartitionCell> cells)
        {
            if (cells.Count == 0)
                return false;

            int minX = int.MaxValue, maxX = int.MinValue, minZ = int.MaxValue, maxZ = int.MinValue;

            for (int i = 0; i < cells.Count; i++)
            {
                minX = Math.Min(minX, cells[i].IndexX);
                maxX = Math.Max(maxX, cells[i].IndexX);
                minZ = Math.Min(minZ, cells[i].IndexZ);
                maxZ = Math.Max(maxZ, cells[i].IndexZ);
            }

            return cells.Count == (maxX - minX + 1) * (maxZ - minZ + 1);
        }

        private static string Block(List<PCGPartitionCell> cells)
        {
            int minX = int.MaxValue, maxX = int.MinValue, minZ = int.MaxValue, maxZ = int.MinValue;

            for (int i = 0; i < cells.Count; i++)
            {
                minX = Math.Min(minX, cells[i].IndexX);
                maxX = Math.Max(maxX, cells[i].IndexX);
                minZ = Math.Min(minZ, cells[i].IndexZ);
                maxZ = Math.Max(maxZ, cells[i].IndexZ);
            }

            return cells.Count == 0
                ? "empty"
                : $"{maxX - minX + 1}x{maxZ - minZ + 1} from {minX},{minZ}";
        }

        private static int ReportedDeferred(List<PCGPartitionCell> cells, in Vector3 center, int budget)        {
            var active = new List<PCGPartitionCell>();
            var evicted = new List<PCGPartitionCell>();
            return PCGCellStreaming.Select(cells, center, 400.0f, 1000.0f, active, evicted, budget);
        }

        private static bool Contains(List<PCGPartitionCell> cells, in PCGPartitionCell cell)
        {
            for (int i = 0; i < cells.Count; i++)
            {
                if (cells[i].IndexX == cell.IndexX && cells[i].IndexZ == cell.IndexZ)
                    return true;
            }

            return false;
        }

        private static bool SameSet(List<PCGPartitionCell> a, List<PCGPartitionCell> b)
        {
            for (int i = 0; i < a.Count; i++)
            {
                if (!Contains(b, a[i]))
                    return false;
            }

            return true;
        }

        // ---- 预算对上一份缓存 ------------------------------------------

        private static void BudgetWorkSplitTests()
        {
            var cells = Cells();
            var origin = new Vector3();

            var active = new List<PCGPartitionCell>();
            var evicted = new List<PCGPartitionCell>();

            PCGCellStreaming.Select(cells, origin, 400.0f, 1000.0f, active, evicted);
            int window = active.Count;
            var wholeWindow = new List<PCGPartitionCell>(active);

            // 缓存已经持有的格子在一次请求里不花预算，所以无论预算怎么写它们都必须留下，
            // 需要设上限的只是那份活儿。
            var cached = new HashSet<(int, int)>();
            int cachedCount = 0;
            foreach (var cell in wholeWindow)
            {
                if (DistanceToCell(cell, origin) > 0.0f)
                {
                    cached.Add((cell.IndexX, cell.IndexZ));
                    cachedCount++;
                }
            }

            const int Budget = 2;
            int deferred = PCGCellStreaming.Select(
                cells, origin, 400.0f, 1000.0f, active, evicted, Budget,
                cell => !cached.Contains((cell.IndexX, cell.IndexZ)));

            int keptCached = 0;
            foreach (var cell in active)
            {
                if (cached.Contains((cell.IndexX, cell.IndexZ)))
                    keptCached++;
            }

            check(keptCached == cachedCount && active.Count == cachedCount + Budget &&
                      deferred == window - cachedCount - Budget,
                "cached cells ride along for free and only the work is capped",
                $"active={active.Count} (cached {keptCached}/{cachedCount}) deferred={deferred}");

            // 防"饿死"的那条性质：从同一处反复请求要朝外走，
            // 而不是永远把同样那几个最近的格子交给每一次请求。
            var done = new HashSet<(int, int)>();
            var everWorked = new HashSet<(int, int)>();
            int rounds = 0;

            while (done.Count < window && rounds <= window)
            {
                var needingWork = new List<PCGPartitionCell>();

                PCGCellStreaming.Select(
                    cells, origin, 400.0f, 1000.0f, active, evicted, Budget,
                    cell =>
                    {
                        if (done.Contains((cell.IndexX, cell.IndexZ)))
                            return false;

                        needingWork.Add(cell);
                        return true;
                    });

                int worked = 0;
                int before = everWorked.Count;
                foreach (var cell in active)
                {
                    if (!Contains(needingWork, cell))
                        continue;

                    done.Add((cell.IndexX, cell.IndexZ));
                    everWorked.Add((cell.IndexX, cell.IndexZ));
                    worked++;
                }

                check(worked == Math.Min(Budget, window - before),
                    $"round {rounds + 1}: the request spends its whole budget while cells remain",
                    $"worked={worked} of budget {Budget}, {window - before} left");

                rounds++;
            }

            check(everWorked.Count == window,
                "requests from one unmoving centre eventually fill the whole window instead of starving it",
                $"{everWorked.Count} of {window} after {rounds} request(s)");
            check(rounds == (window + Budget - 1) / Budget,
                "and they finish in exactly ceil(window / budget) requests",
                $"{rounds} rounds for {window} cells at budget {Budget}");

            // 这次拆分防住的那个失败，被钉成断言而不是留在注释里：同一个中心、
            // 且不知道哪些已经做完时，预算每一次都重新挑中同样那几个最近的格子。
            var reached = new List<PCGPartitionCell>();
            for (int r = 0; r < window; r++)
            {
                PCGCellStreaming.Select(cells, origin, 400.0f, 1000.0f, active, evicted, Budget, null);
                foreach (var cell in active)
                {
                    if (!Contains(reached, cell))
                        reached.Add(cell);
                }
            }

            check(reached.Count == Budget,
                "without the work split a budget would only ever reach its own nearest cells",
                $"{reached.Count} distinct cell(s) after {window} requests");
        }

        // ---- 跟随触发器 ---------------------------------------------------

        private static void TriggerTests()
        {
            const float Step = 400.0f;

            // 坐标的下标必须朝负无穷取整，而坑在于一次普通强转是朝零取整：
            // -0.1 坐在原点西侧那个格子里，不是和 +0.1 一起在第 0 格。
            check(PCGStreamTrigger.CellIndexOf(0.0f, Step) == 0 &&
                      PCGStreamTrigger.CellIndexOf(399.9f, Step) == 0 &&
                      PCGStreamTrigger.CellIndexOf(400.0f, Step) == 1 &&
                      PCGStreamTrigger.CellIndexOf(1000.0f, Step) == 2,
                "positive coordinates land in the cell that contains them", "0,0,1,2");

            check(PCGStreamTrigger.CellIndexOf(-0.1f, Step) == -1 &&
                      PCGStreamTrigger.CellIndexOf(-400.0f, Step) == -1 &&
                      PCGStreamTrigger.CellIndexOf(-400.1f, Step) == -2 &&
                      PCGStreamTrigger.CellIndexOf(-800.0f, Step) == -2,
                "negative coordinates keep their own cell west of the origin", "-1,-1,-2,-2");

            // 对照"一个粗心的实现会长成的那个样子"来断言，这样上面那两条不会
            // 被一个恰好在读者期望的样本上一致的强转一起糊弄过去。
            check((int)(-0.1f / Step) == 0 && PCGStreamTrigger.CellIndexOf(-0.1f, Step) == -1,
                "the trigger is not just a cast: truncation would call -0.1 cell 0");

            check(PCGStreamTrigger.CellIndexOf(1000.0f, 0.0f) == 0 &&
                      PCGStreamTrigger.CellIndexOf(1000.0f, -400.0f) == 0,
                "a zero or negative step yields 0 instead of dividing by it");

            var both = PCGStreamTrigger.CellOf(new Vector3(400.0f, 1234.0f, -400.1f), Step);
            check(both.X == 1 && both.Z == -2,
                "CellOf maps X and Z and ignores the vertical axis entirely",
                $"{both.X},{both.Z}");

            // 同一个会坑到设计者的差别：朝西跨过原点走过去。
            var floorCells = new HashSet<int>();
            var castCells = new HashSet<int>();
            for (float p = 200.0f; p >= -600.0f; p -= 100.0f)
            {
                floorCells.Add(PCGStreamTrigger.CellIndexOf(p, Step));
                castCells.Add((int)(p / Step));
            }

            check(floorCells.Count == 3 && castCells.Count == 2,
                "a westward walk across the origin crosses three cells where truncation only sees two",
                $"floor={floorCells.Count} cast={castCells.Count}");

            // 请求的判定，写成一张真值表——每一行错了都是一种不同的失败。
            check(PCGStreamTrigger.ShouldRequest(true, 0, false),
                "crossing into a new cell always asks, even with nothing pending");
            check(!PCGStreamTrigger.ShouldRequest(false, 0, true),
                "an idle frame with nothing pending asks for nothing");
            check(PCGStreamTrigger.ShouldRequest(false, 3, true),
                "a pending backlog keeps asking while it is shrinking");
            check(!PCGStreamTrigger.ShouldRequest(false, 3, false),
                "a pending backlog stops asking once the retries stop making progress");
            check(!PCGStreamTrigger.ShouldRequest(false, -1, true),
                "no budget (nothing pending) is never read as a backlog");

            // 跟随步长必须跟最细的那一层，否则细层的窗口会落后于粗层。
            var layers = new[]
            {
                new PCGPartitionLayer { Name = "coarse", CellSize = 800.0f },
                new PCGPartitionLayer { Name = "fine", CellSize = 100.0f },
            };

            check(PCGStreamTrigger.FollowStep(50.0f, 400.0f, layers) == 50.0f,
                "an explicit step wins outright, whatever the layers say");
            check(PCGStreamTrigger.FollowStep(-1.0f, 400.0f, layers) == 100.0f,
                "with layers present the finest cell size is the step",
                $"{PCGStreamTrigger.FollowStep(-1.0f, 400.0f, layers):0.#}");
            check(PCGStreamTrigger.FollowStep(-1.0f, 400.0f, null) == 400.0f,
                "no layers falls back to the volume's own cell size");
            check(PCGStreamTrigger.FollowStep(-1.0f, 400.0f, Array.Empty<PCGPartitionLayer>()) == 400.0f,
                "an empty layer list is the same as none");

            var mixed = new[]
            {
                new PCGPartitionLayer { Name = "off", CellSize = 10.0f, Enabled = false },
                new PCGPartitionLayer { Name = "unset", CellSize = 0.0f },
                null,
                new PCGPartitionLayer { Name = "real", CellSize = 250.0f },
            };

            check(PCGStreamTrigger.FollowStep(-1.0f, 400.0f, mixed) == 250.0f,
                "disabled, zero-sized and missing layers are skipped rather than winning the minimum",
                $"{PCGStreamTrigger.FollowStep(-1.0f, 400.0f, mixed):0.#}");

            check(PCGStreamTrigger.FollowStep(-1.0f, 0.0f, mixed) == 250.0f &&
                      PCGStreamTrigger.FollowStep(-1.0f, 0.0f, new[] { new PCGPartitionLayer { CellSize = 0.0f } }) == 0.0f,
                "a volume with nothing usable to say about size yields 0, which the cell index maps to no steps");
        }

        // ---- 多个观察者 ------------------------------------------------

        /// <summary>
        /// MMO 需要的并集语义：地面在<i>每个</i>玩家脚下都会出现，而一个格子只在
        /// 它离开了所有人之后才消失。
        /// </summary>
        /// <remarks>
        /// 两个相距很远的观察者，选得让他们的窗口不可能重叠（他们之间的距离超过
        /// 半径的两倍，所以没有格子能同时在两人的半径内）。这让并集的计数可算，
        /// 而不是"看上去差不多"。
        /// </remarks>
        private static void MultiViewerTests()
        {
            var cells = Cells();
            var active = new List<PCGPartitionCell>();
            var evicted = new List<PCGPartitionCell>();
            var otherActive = new List<PCGPartitionCell>();
            var otherEvicted = new List<PCGPartitionCell>();

            var near = new Vector3(-800.0f, 0.0f, -800.0f);
            var far = new Vector3(800.0f, 0.0f, 800.0f);

            PCGCellStreaming.SelectAround(cells, new[] { near, far }, 400.0f, 400.0f, active, evicted);
            PCGCellStreaming.Select(cells, near, 400.0f, 400.0f, otherActive, otherEvicted);
            int nearOnly = otherActive.Count;

            PCGCellStreaming.Select(cells, far, 400.0f, 400.0f, otherActive, otherEvicted);
            int farOnly = otherActive.Count;

            check(active.Count == nearOnly + farOnly,
                "two distant viewers generate the union of their windows, with nothing double-counted",
                $"{active.Count} vs {nearOnly} + {farOnly}");

            // 地图正中不属于任何人的邻域：玩家隔这么远时它保持不生成，
            // 而不是为了填补两人之间那段距离被填上。这里按位置来算，因为规划器
            // 是从区域最小值开始编号的——下标 0,0 是角上的格子，而观察者正坐在它上面。
            var middle = CellAt(cells, 0.0f, 0.0f);

            check(!Contains(active, middle),
                "the space between two far-apart viewers is not generated just because it lies between them",
                middle.Label);

            // 驱逐的条件是"离开了每一个观察者"，不是"离开了某一个"：
            // 近处那位已经走过、但远处那位仍然持有的格子必须继续站着。
            var between = new Vector3(-800.0f, 0.0f, 800.0f);
            var previous = new List<PCGPartitionCell> { CellAt(cells, -800.0f, 800.0f) };

            PCGCellStreaming.SelectAround(cells, new[] { far }, 400.0f, 400.0f, active, evicted);
            bool evictedWithOneViewer = Contains(evicted, previous[0]);

            PCGCellStreaming.SelectAround(cells, new[] { far, between }, 400.0f, 400.0f, active, evicted);

            check(evictedWithOneViewer && !Contains(evicted, previous[0]),
                "a cell one viewer has left stays while another viewer still holds it",
                $"one viewer: evicted={evictedWithOneViewer}, two viewers: evicted={Contains(evicted, previous[0])}");

            // 没有观察者是真实状态——这一片世界里没人——而它会把窗口清空。
            PCGCellStreaming.SelectAround(cells, Array.Empty<Vector3>(), 400.0f, 400.0f, active, evicted);
            check(active.Count == 0 && evicted.Count == cells.Count,
                "with no viewers nothing is generated and everything is handed back",
                $"active={active.Count} evicted={evicted.Count}");

            // 只有一个观察者时与更早那个单中心调用选出同一批，所以用它的那些东西都没变。
            PCGCellStreaming.Select(cells, near, 400.0f, 400.0f, active, evicted);
            PCGCellStreaming.SelectAround(cells, new[] { near }, 400.0f, 400.0f, otherActive, otherEvicted);

            check(SameSet(active, otherActive) && SameSet(evicted, otherEvicted),
                "the single-centre call is the one-viewer case of the union, not a second implementation",
                $"active {active.Count}/{otherActive.Count}, evicted {evicted.Count}/{otherEvicted.Count}");

            // 预算按最近的观察者排序。与一份独立算出的排序按集合比对，而不是
            // "最远的保留 < 最近的延后"那种比法：6x6 的网格上距离频繁平手，
            // 而跨越一次平手的严格不等式会把正确的代码读成失败。
            var viewers = new[] { near, far };
            PCGCellStreaming.SelectAround(cells, viewers, 1600.0f, 1600.0f, active, evicted, 2);
            PCGCellStreaming.SelectAround(cells, viewers, 1600.0f, 1600.0f, otherActive, otherEvicted);

            var ranked = new List<PCGPartitionCell>(otherActive);
            ranked.Sort((a, b) =>
            {
                int byDistance = Nearest(a, near, far).CompareTo(Nearest(b, near, far));
                if (byDistance != 0)
                    return byDistance;

                int byRow = a.IndexZ.CompareTo(b.IndexZ);
                return byRow != 0 ? byRow : a.IndexX.CompareTo(b.IndexX);
            });

            var expected = new List<PCGPartitionCell> { ranked[0], ranked[1] };

            check(active.Count == 2 && SameSet(active, expected),
                "the budget keeps the cells nearest to either viewer, by the same ranking the caller can compute",
                $"{active.Count} kept of {otherActive.Count} in the window");

            CheckThrows(() => PCGCellStreaming.SelectAround(cells, null, 10.0f, 10.0f, active, evicted),
                "null viewers");
        }

        // ---- 限制一次请求覆盖多少个观察者 ---------------------

        /// <summary>
        /// 世界拥挤时让一次请求有界的那个上限，以及决定谁能留下的那条规则。
        /// </summary>
        /// <remarks>
        /// 要紧的对照是覆盖率而不是件数：从三个聚类里挑出的三个观察者必须比
        /// 从列表前端随手取的三个覆盖更多地图，而后者正是"取前 N 个"会给出的结果。
        /// </remarks>
        private static void ViewerReductionTests()
        {
            var viewers = new List<Vector3>();
            var clusters = new[]
            {
                new Vector3(-1000.0f, 0.0f, -1000.0f),
                new Vector3(1000.0f, 0.0f, -1000.0f),
                new Vector3(1000.0f, 0.0f, 1000.0f),
            };

            foreach (var cluster in clusters)
            {
                viewers.Add(cluster);
                viewers.Add(cluster + new Vector3(60.0f, 0.0f, 0.0f));
                viewers.Add(cluster + new Vector3(0.0f, 0.0f, 60.0f));
                viewers.Add(cluster + new Vector3(60.0f, 0.0f, 60.0f));
            }

            var picked = new List<Vector3>();
            int taken = PCGStreamViewers.Reduce(viewers, 3, picked);

            check(taken == 3 && picked.Count == 3,
                "the cap keeps exactly the number of viewers it was given",
                $"{taken} of {viewers.Count}");

            var distinctClusters = new HashSet<int>();
            foreach (var viewer in picked)
            {
                for (int i = 0; i < clusters.Length; i++)
                {
                    if ((viewer - clusters[i]).Length < 200.0f)
                        distinctClusters.Add(i);
                }
            }

            check(distinctClusters.Count == 3,
                "three viewers out of three clusters come from three different clusters",
                $"{distinctClusters.Count} cluster(s) represented");

            // 给这条规则正名的那组对照：列表前三项全都在同一个聚类里。
            var firstThree = new List<Vector3> { viewers[0], viewers[1], viewers[2] };
            var firstClusters = new HashSet<int>();

            foreach (var viewer in firstThree)
            {
                for (int i = 0; i < clusters.Length; i++)
                {
                    if ((viewer - clusters[i]).Length < 200.0f)
                        firstClusters.Add(i);
                }
            }

            check(firstClusters.Count == 1,
                "taking the first entries instead would spend the whole cap on one neighbourhood",
                $"{firstClusters.Count} cluster(s) covered by the first three");

            // 在真正的选择结果上量覆盖率：这个窗口能到达多少个格子。
            var cells = Cells();
            var active = new List<PCGPartitionCell>();
            var evicted = new List<PCGPartitionCell>();

            PCGCellStreaming.SelectAround(cells, picked, 500.0f, 500.0f, active, evicted);
            int spreadCoverage = active.Count;

            PCGCellStreaming.SelectAround(cells, firstThree, 500.0f, 500.0f, active, evicted);
            int clusteredCoverage = active.Count;

            check(spreadCoverage > clusteredCoverage,
                "the spread-out choice covers strictly more cells for the same cap",
                $"{spreadCoverage} vs {clusteredCoverage} cell(s)");

            // 小集合原样通过，而上限为 0 的意思是"不设上限"，不是"没有观察者"。
            var small = new List<Vector3> { new Vector3(10.0f, 0.0f, 20.0f), new Vector3(30.0f, 0.0f, 40.0f) };
            PCGStreamViewers.Reduce(small, 8, picked);
            check(picked.Count == 2 && picked[0] == small[0] && picked[1] == small[1],
                "fewer viewers than the cap are copied as they are");

            PCGStreamViewers.Reduce(viewers, 0, picked);
            check(picked.Count == viewers.Count,
                "a cap of zero is read as no cap, so it cannot empty the world by accident",
                $"{picked.Count} of {viewers.Count}");

            check(PCGStreamViewers.Reduce(new List<Vector3>(), 4, picked) == 0 && picked.Count == 0,
                "no viewers stays no viewers");

            // 确定性：同一份列表两次归约选到的是同一批观察者。
            var repeat = new List<Vector3>();
            var again = new List<Vector3>();
            PCGStreamViewers.Reduce(viewers, 3, repeat);
            PCGStreamViewers.Reduce(viewers, 3, again);

            bool identical = again.Count == repeat.Count;

            for (int i = 0; identical && i < again.Count; i++)
            {
                if (again[i] != repeat[i])
                    identical = false;
            }

            check(identical && again.Count == 3,
                "the same viewers reduce to the same choice every run",
                $"{again.Count} vs {repeat.Count}");

            var crowd = new List<Vector3>();
            for (int i = 0; i < 12; i++)
                crowd.Add(new Vector3(5.0f, 0.0f, 5.0f));

            var crowdPicked = new List<Vector3>();
            PCGStreamViewers.Reduce(crowd, 3, crowdPicked);

            bool finite = crowdPicked.Count == 3;
            for (int i = 0; i < crowdPicked.Count; i++)
            {
                if (float.IsNaN(crowdPicked[i].X) || float.IsNaN(crowdPicked[i].Y) || float.IsNaN(crowdPicked[i].Z))
                    finite = false;
            }

            check(finite, "a crowd standing on one spot still reduces to a finite set");
        }

        // ---- 由宿主提供的观察者 -------------------------------------

        /// <summary>
        /// 游戏用来按自己的世界状态驱动窗口的那个钩子，以及它的返回值约定——
        /// 这条约定防的是一个粗心的来源读到上一帧的残留。
        /// </summary>
        private static void ViewerSourceTests()
        {
            var scratch = new List<Vector3>();
            var into = new List<Vector3>();
            int calls = 0;

            check(PCGStreamViewers.FromSource(null, 4, scratch, into) == 0 && into.Count == 0,
                "no source means no viewers rather than a crash");

            // 老实回答的来源照常通过，并受上限约束。
            Func<List<Vector3>, int> honest = list =>
            {
                calls++;
                list.Add(new Vector3(0.0f, 0.0f, 0.0f));
                list.Add(new Vector3(100.0f, 0.0f, 0.0f));
                list.Add(new Vector3(200.0f, 0.0f, 0.0f));
                return 3;
            };

            check(PCGStreamViewers.FromSource(honest, 8, scratch, into) == 3 && calls == 1 && scratch.Count == 3,
                "an honest source is taken at its word, and called once",
                $"{into.Count} viewer(s) after {calls} call(s)");

            // 要紧的那一例：声称写得比实际多。答案必须是列表里真有的那些，而不是它声称的数——
            // 那个暂存列表每帧都复用，相信声称值就会把
            // 更早一次调用的条目交给这个窗口。
            Func<List<Vector3>, int> overclaiming = list =>
            {
                list.Add(new Vector3(0.0f, 0.0f, 0.0f));
                list.Add(new Vector3(50.0f, 0.0f, 0.0f));
                return 7;
            };

            check(PCGStreamViewers.FromSource(overclaiming, 8, scratch, into) == 2 && scratch.Count == 2,
                "a source that claims more entries than it wrote is capped at what it wrote",
                $"{into.Count} viewer(s), scratch {scratch.Count}");

            // 负数或零的回答是一个空窗口，既不是"无界"，也不是"整份列表都读"。
            Func<List<Vector3>, int> refusing = list =>
            {
                list.Add(new Vector3(1.0f, 0.0f, 1.0f));
                return -1;
            };

            check(PCGStreamViewers.FromSource(refusing, 8, scratch, into) == 0 && scratch.Count == 0,
                "a source that reports a negative count yields an empty window",
                $"{into.Count} viewer(s)");

            // 而无论来源产出什么，上限都照样施加在其上。
            Func<List<Vector3>, int> crowded = list =>
            {
                for (int i = 0; i < 12; i++)
                    list.Add(new Vector3(i * 100.0f, 0.0f, 0.0f));

                return 12;
            };

            PCGStreamViewers.FromSource(crowded, 3, scratch, into);
            check(into.Count == 3,
                "the viewer cap applies to a source-driven window too",
                $"{into.Count} of 12");
        }

        // ---- 两级（层级）规划 ----------------------------------------

        /// <summary>
        /// 一层层网格的级联：每一级决定下一级哪里值得生成，而它们之间的父子链接
        /// 正是把这个决定往下传的东西。
        /// </summary>
        private static void HierarchyTests()
        {
            var region = new BoundingBox(new Vector3(-1200.0f, -50.0f, -1200.0f), new Vector3(1200.0f, 50.0f, 1200.0f));
            var two = new[] { 800.0f, 200.0f };

            var hierarchy = PCGPartitionHierarchyPlanner.Plan(region, two, 20.0f, out string error);

            check(hierarchy != null && error == null,
                "a coarse grid with a finer grid inside it plans", error ?? "ok");

            if (hierarchy == null)
                return;

            // 两级的案例必须和它历来一模一样：同样的网格、同样的计数，而生成级是第二级。
            // 这就是保护每一个现有体积的那条回归。
            check(hierarchy.LevelCount == 2 && hierarchy.Generation.CellSize == 200.0f,
                "two levels means the generation level is the second one",
                $"{hierarchy.LevelCount} level(s), generation {hierarchy.Generation.CellSize}");

            var coarseCells = hierarchy.Levels[0].Cells;
            var fineCells = hierarchy.Generation.Cells;

            check(coarseCells.Count == 9 && fineCells.Count == 144,
                "2400 units over 800 and over 200 give nine coarse cells and a hundred and forty-four fine ones",
                $"{coarseCells.Count} coarse, {fineCells.Count} fine");

            // 尺寸能整除时，每个粗格子恰好拥有同样多的细格子——这正是下面那道过滤器
            // 有意义的性质。
            var perParent = new Dictionary<int, int>();
            bool parentInRange = true;

            for (int i = 0; i < fineCells.Count; i++)
            {
                int parent = hierarchy.ParentOf(1, i);

                if (parent < 0 || parent >= coarseCells.Count)
                    parentInRange = false;

                perParent.TryGetValue(parent, out int count);
                perParent[parent] = count + 1;
            }

            bool even = true;
            foreach (var pair in perParent)
                even &= pair.Value == 16;

            check(parentInRange && even && perParent.Count == 9,
                "each fine cell has a parent and an even 800/200 ratio gives sixteen fine cells per coarse one",
                $"{perParent.Count} parent(s), even={even}");

            // 活着的集合完全等于平铺那份规划：层级加的是一道过滤器，不是一套新的铺法。
            var all = new HashSet<(int X, int Z)>();

            foreach (var cell in coarseCells)
                all.Add((cell.IndexX, cell.IndexZ));

            var underAll = new List<PCGPartitionCell>();
            int liveFine = hierarchy.CellsUnder(1, all, underAll);
            var flat = PCGPartitionPlanner.Plan(region, 200.0f, 20.0f, out _);

            check(liveFine == flat.Count && fineCells.Count == flat.Count,
                "with every coarse cell live the fine set is the whole fine plan",
                $"{liveFine} vs {flat.Count}");

            // 两个活着的父格：只有它们各自的那十六个。
            var live = new HashSet<(int X, int Z)>
            {
                (coarseCells[0].IndexX, coarseCells[0].IndexZ),
                (coarseCells[4].IndexX, coarseCells[4].IndexZ),
            };

            var underTwo = new List<PCGPartitionCell>();
            check(hierarchy.CellsUnder(1, live, underTwo) == 32,
                "only the fine cells under a live coarse cell are handed over",
                $"{underTwo.Count} of {fineCells.Count}");

            check(hierarchy.CellsUnder(1, new HashSet<(int X, int Z)>(), underTwo) == 0,
                "with nothing coarse-cell live there is nothing fine to run");

            // 比例除不尽时：每个细格子仍恰好属于一个父格，选的是
            // 包含它中心的那一个。
            var unevenPlan = PCGPartitionHierarchyPlanner.Plan(region, new[] { 800.0f, 300.0f }, 20.0f,
                out string unevenError);

            bool centresInside = unevenPlan != null;
            int straddling = 0;

            for (int i = 0; unevenPlan != null && i < unevenPlan.Generation.Cells.Count; i++)
            {
                var parent = unevenPlan.Levels[0].Cells[unevenPlan.ParentOf(1, i)];
                var fineCell = unevenPlan.Generation.Cells[i];
                var centre = new Vector3(
                    (fineCell.ClipBounds.Minimum.X + fineCell.ClipBounds.Maximum.X) * 0.5f, 0.0f,
                    (fineCell.ClipBounds.Minimum.Z + fineCell.ClipBounds.Maximum.Z) * 0.5f);

                if (!PCGPartitionPlanner.IsInsideCell(in parent, centre))
                    centresInside = false;

                // 比两级之差还窄的细格子会跨过一条粗边。
                if (fineCell.ClipBounds.Minimum.X < parent.ClipBounds.Minimum.X ||
                    fineCell.ClipBounds.Maximum.X > parent.ClipBounds.Maximum.X)
                {
                    straddling++;
                }
            }

            check(unevenPlan != null && unevenError == null && centresInside && straddling > 0,
                "800 over 300 straddles coarse edges, and every fine cell is still assigned by its centre",
                unevenError ?? $"{straddling} straddling cell(s), centres inside={centresInside}");

            // 拒绝路径：某一级并不比它上面那级更细，以及一张突破
            // 格子数上限的生成网格。
            check(PCGPartitionHierarchyPlanner.Plan(region, new[] { 800.0f, 800.0f }, 20.0f, out string sameError) == null &&
                      sameError != null,
                "a level that is not finer than the one above it is refused",
                sameError);

            var tooFine = PCGPartitionHierarchyPlanner.Plan(region, new[] { 800.0f, 10.0f }, 20.0f,
                out string ceilingError);

            check(tooFine == null && ceilingError != null &&
                      ceilingError.Contains("generation level", StringComparison.OrdinalIgnoreCase),
                "a generation grid past the cell ceiling is refused, and the message says which level",
                ceilingError);

            DeeperHierarchyTests(region);
        }

        /// <summary>
        /// 三级以上：尺寸、铺法、链接，以及一级比一级窄的级联。
        /// </summary>
        /// <remarks>
        /// 上面那个两级案例，一份只有两级的实现也能满足，所以"这是层级而不是成对"的
        /// 一切都在这里验：三张网格都以各自的分辨率铺同一片区域、链接只在相邻两级之间，
        /// 而活着的集合朝下走时只能收缩。
        /// </remarks>
        private static void DeeperHierarchyTests(BoundingBox region)
        {
            // 中间那些尺寸是插值出来的，所以设计者本来就有的那两个旋钮就够了。
            var sizes = PCGPartitionHierarchyPlanner.GeometricSizes(800.0f, 200.0f, 3);

            check(sizes.Length == 3 && Near(sizes[0], 800.0f) && Near(sizes[1], 400.0f) && Near(sizes[2], 200.0f),
                "three levels interpolate geometrically between the coarsest and the finest",
                string.Join(",", sizes));

            var twoOnly = PCGPartitionHierarchyPlanner.GeometricSizes(800.0f, 200.0f, 2);
            check(twoOnly.Length == 2 && Near(twoOnly[0], 800.0f) && Near(twoOnly[1], 200.0f),
                "two levels return exactly the two sizes given, which is what the volume relies on",
                string.Join(",", twoOnly));

            var five = PCGPartitionHierarchyPlanner.GeometricSizes(1024.0f, 64.0f, 5);
            bool descending = five.Length == 5;

            for (int i = 1; descending && i < five.Length; i++)
                descending = five[i] < five[i - 1];

            // 最细的那一档尺寸是钉住的、不是算出来的：反复乘一个不精确的比例，不许
            // 成为决定设计者敲进的那张网格的东西。
            check(descending && Near(five[4], 64.0f),
                "a five-level spread is strictly finer at every step and ends exactly on the size asked for",
                string.Join(",", five));

            check(PCGPartitionHierarchyPlanner.GeometricSizes(800.0f, 800.0f, 3).Length == 0 &&
                  PCGPartitionHierarchyPlanner.GeometricSizes(800.0f, 200.0f, 1).Length == 0 &&
                  PCGPartitionHierarchyPlanner.GeometricSizes(800.0f, 200.0f,
                      PCGPartitionHierarchyPlanner.MaxLevels + 1).Length == 0,
                "a spread that cannot describe a hierarchy produces nothing rather than a wrong grid");

            var three = PCGPartitionHierarchyPlanner.Plan(region, sizes, 20.0f, out string error);

            check(three != null && error == null && three.LevelCount == 3,
                "three levels plan", error ?? $"{three?.LevelCount} level(s)");

            if (three == null)
                return;

            // 每一级都以自己的分辨率铺同一片区域：2400 分别按 800、400 和 200 铺。
            check(three.Levels[0].Cells.Count == 9 && three.Levels[1].Cells.Count == 36 &&
                  three.Levels[2].Cells.Count == 144,
                "every level is the flat plan of its own size",
                $"{three.Levels[0].Cells.Count}/{three.Levels[1].Cells.Count}/{three.Levels[2].Cells.Count}");

            // 链接只在相邻两级之间，而每个格子恰好有一个包含它中心的父格——
            // 这是拿格子自己来核对的，不是把规划器自己的规则再跑一遍。
            bool linked = true;
            int checkedLinks = 0;

            for (int level = 1; level < three.LevelCount && linked; level++)
            {
                var parents = three.Levels[level - 1].Cells;
                var children = three.Levels[level].Cells;
                var owners = new Dictionary<int, int>();

                for (int i = 0; i < children.Count; i++)
                {
                    int parent = three.ParentOf(level, i);
                    linked &= parent >= 0 && parent < parents.Count;

                    if (!linked)
                        break;

                    var centre = new Vector3(
                        (children[i].ClipBounds.Minimum.X + children[i].ClipBounds.Maximum.X) * 0.5f, 0.0f,
                        (children[i].ClipBounds.Minimum.Z + children[i].ClipBounds.Maximum.Z) * 0.5f);

                    var parentCell = parents[parent];

                    linked &= PCGPartitionPlanner.IsInsideCell(in parentCell, centre);

                    owners.TryGetValue(parent, out int seen);
                    owners[parent] = seen + 1;
                    checkedLinks++;
                }
            }

            check(linked && checkedLinks == 36 + 144,
                "every cell links to one parent level up, and that parent contains its centre",
                $"{checkedLinks} link(s)");

            // 级联：顶层一个活格，它下面四个孩子，每个下面再四个。尺寸是
            // 800/400/200 铺在 2400 的区域上，所以每一步正好是 2x2 的分裂。
            var live = new HashSet<(int X, int Z)>
            {
                (three.Levels[0].Cells[0].IndexX, three.Levels[0].Cells[0].IndexZ),
            };

            var middle = new List<PCGPartitionCell>();
            int middleCount = three.CellsUnder(1, live, middle);

            check(middleCount == 4,
                "one live cell at the top brings in its own four children and no others",
                $"{middleCount} of {three.Levels[1].Cells.Count}");

            var liveMiddle = new HashSet<(int X, int Z)>();

            foreach (var cell in middle)
                liveMiddle.Add((cell.IndexX, cell.IndexZ));

            var leaves = new List<PCGPartitionCell>();
            int leafCount = three.CellsUnder(2, liveMiddle, leaves);

            check(leafCount == 16 && leaves.Count == 16,
                "the level below narrows again: four live cells bring in sixteen leaves",
                $"{leafCount} of {three.Levels[2].Cells.Count}");

            // 一个"从顶层往下走"的实现会弄错的那个方向：一份无法收缩的活集合，
            // 会把整张最细的网格交给执行器，于是在一无所有周围生成出一片森林。
            check(leafCount < three.Levels[2].Cells.Count / 8,
                "the cascade actually narrows the work rather than passing the whole grid down",
                $"{leafCount} of {three.Levels[2].Cells.Count}");

            // 上面任何一级都没有活格就意味着什么都不运行，任何深度都不——那是上面那条走法的拒绝孪生。
            check(three.CellsUnder(1, new HashSet<(int X, int Z)>(), middle) == 0 &&
                  middle.Count == 0,
                "an empty live set at the top leaves the middle level with nothing to consider");

            // 突破格子数上限时必须点名是哪一级破的，因为修法是去改
            // 那一级的尺寸，而那样的级别可能有六个。
            var overFine = PCGPartitionHierarchyPlanner.Plan(region, new[] { 800.0f, 400.0f, 5.0f }, 20.0f,
                out string levelError);

            check(overFine == null && levelError != null && levelError.Contains("generation level"),
                "a bad generation level is named as such", levelError);

            var overMiddle = PCGPartitionHierarchyPlanner.Plan(
                new BoundingBox(new Vector3(-200000.0f, -50.0f, -200000.0f), new Vector3(200000.0f, 50.0f, 200000.0f)),
                new[] { 8000.0f, 5.0f, 1.0f }, 0.0f, out string middleError);

            check(overMiddle == null && middleError != null && middleError.Contains("Level 1"),
                "a bad deciding level is named by its index", middleError);

            check(PCGPartitionHierarchyPlanner.Plan(region, new[] { 800.0f }, 20.0f, out string oneError) == null &&
                  oneError != null,
                "a single level is refused: with one grid there is nothing to decide", oneError);

            check(PCGPartitionHierarchyPlanner.Plan(region, new[] { 800.0f, 0.0f }, 20.0f, out string zeroError) == null &&
                  zeroError != null && zeroError.Contains("Level 1"),
                "a level with no size is refused by index", zeroError);

            // 第 0 级没有上一级，所以问它要父格是编程错误，而不是规划层面的问题。
            bool threw = false;

            try
            {
                three.CellsUnder(0, new HashSet<(int X, int Z)>(), new List<PCGPartitionCell>());
            }
            catch (ArgumentOutOfRangeException)
            {
                threw = true;
            }

            check(threw, "asking the coarsest level for a parent is refused rather than answered with level 0");
        }

        private static bool Near(float a, float b, float tolerance = 1e-3f) => Math.Abs(a - b) <= tolerance;

        private static PCGPartitionCell CellAt(List<PCGPartitionCell> cells, float x, float z)
        {
            foreach (var cell in cells)
            {
                if (x >= cell.ClipBounds.Minimum.X && x <= cell.ClipBounds.Maximum.X &&
                    z >= cell.ClipBounds.Minimum.Z && z <= cell.ClipBounds.Maximum.Z)
                {
                    return cell;
                }
            }

            throw new InvalidOperationException($"no cell contains {x},{z}");
        }

        /// <summary>到最近观察者的距离，用本文件自己那份独立写出的参照算出来。</summary>
        private static float Nearest(in PCGPartitionCell cell, Vector3 a, Vector3 b) =>
            Math.Min(DistanceToCell(cell, a), DistanceToCell(cell, b));

        // ---- 窗口等价性 ----------------------------------------------

        /// <summary>
        /// 流式每一部分都倚着的那个陈述：把区域分窗生成，无论怎么切，
        /// 产出的点与一次完整的分区运行产出的完全相同。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 这里钉的是缝合：运行器的裁剪与合并对同一批格子无论怎么分组都给出同样结果，
        /// 两个相邻窗口不会双双报告同一件道具，而一份共享缓存
        /// 无论窗口以什么顺序到达都返回同样的内容——最后这条要紧，
        /// 因为流式玩家以他们走路的顺序访问格子。每格独立在这里是构造保证的
        /// （执行器在两次运行之间会清自己的缓存），所以这些断言不是"构造成立"的证明；
        /// 它们是"缝合没有丢件、没有重复、没有乱序"的证明。
        /// </para>
        /// <para>
        /// padding 接缝这条性质被本文件刻意地不主张。拿一次分区运行
        /// 与另一次对同样格子的分区运行相比，显不出它——两边看到的都是同样带 padding 的
        /// 区域，这恰恰是分裂会一致的原因——而这份测试的头一稿用零 padding
        /// 做对照，就是因为这个返回了完全相同的结果。那条主张住在 PartitionTests 里，
        /// 它拿一次分区运行与一次整区域运行在固定点集上相比：
        /// 那是 padding 有可观察效果时唯一的对照。
        /// </para>
        /// <para>
        /// 按点集加原始计数来比，不是按序列：一次部分运行的合并顺序
        /// 合理地与完整那次不同，而计数会抓住集合会藏起来的东西
        /// （两个相同的点坍缩成一个条目）。
        /// </para>
        /// </remarks>
        private static void WindowEquivalenceTests()
        {
            var graph = ScatterDataGraph();
            var cells = PCGPartitionPlanner.Plan(DataRegion(), DataCell, Spacing, out string planError);

            check(cells != null && cells.Count == 4,
                "the data fixture plans exactly four cells to split",
                planError ?? $"{cells?.Count} cell(s)");

            if (cells == null)
                return;

            var full = PCGPartitionRunner.Run(Executor(graph), DataContext(), cells);
            var fullSet = PointSet(full);

            check(full.PrimaryOutput.Count > 100 && fullSet.Count == full.PrimaryOutput.Count,
                "the fixture produces plenty of distinct points (a thin or duplicated run proves nothing here)",
                $"{full.PrimaryOutput.Count} point(s), {fullSet.Count} distinct");

            // 两个窗口，当作两次独立的请求跑。
            var firstHalf = new List<PCGPartitionCell> { cells[0], cells[1] };
            var secondHalf = new List<PCGPartitionCell> { cells[2], cells[3] };

            var a = PCGPartitionRunner.Run(Executor(graph), DataContext(), firstHalf);
            var b = PCGPartitionRunner.Run(Executor(graph), DataContext(), secondHalf);

            check(a.PrimaryOutput.Count + b.PrimaryOutput.Count == full.PrimaryOutput.Count &&
                      SamePoints(a, b, fullSet),
                "two window runs together hold exactly the points of one full run",
                $"{a.PrimaryOutput.Count} + {b.PrimaryOutput.Count} vs {full.PrimaryOutput.Count}");

            // 同样四个格子的另一种切法：这条性质不许依赖请求在哪里被切开。
            var diagonal = new List<PCGPartitionCell> { cells[0], cells[3] };
            var otherDiagonal = new List<PCGPartitionCell> { cells[1], cells[2] };

            var c = PCGPartitionRunner.Run(Executor(graph), DataContext(), diagonal);
            var d = PCGPartitionRunner.Run(Executor(graph), DataContext(), otherDiagonal);

            // 上下文字段必须跟着进每一次格子运行，而检视器是离线也能观察到的那一个：
            // 一次把它丢掉的分区运行什么也没记录，这正是编辑器的按节点报告
            // 在分区图上曾经为空的原因。
            var inspectedContext = DataContext();
            inspectedContext.Inspection = new PCGNodeInspection();
            PCGPartitionRunner.Run(Executor(graph), inspectedContext, firstHalf);

            check(inspectedContext.Inspection.Records.Count >= firstHalf.Count,
                "a partitioned run hands the context on to its cells, so it can report what each node produced",
                $"{inspectedContext.Inspection.Records.Count} record(s) for {firstHalf.Count} cell(s)");

            check(c.PrimaryOutput.Count + d.PrimaryOutput.Count == full.PrimaryOutput.Count &&
                      SamePoints(c, d, fullSet),
                "splitting the same cells diagonally still lands on the same points",
                $"{c.PrimaryOutput.Count} + {d.PrimaryOutput.Count}");

            // 每次请求一个格子，这正是 StreamCellsPerRequest = 1 在四帧里做的事。
            var perCellSet = new SortedSet<string>(StringComparer.Ordinal);
            int perCellCount = 0;

            for (int i = 0; i < cells.Count; i++)
            {
                var one = PCGPartitionRunner.Run(
                    Executor(graph), DataContext(), new List<PCGPartitionCell> { cells[i] });

                perCellCount += one.PrimaryOutput.Count;
                foreach (var key in PointSet(one))
                    perCellSet.Add(key);
            }

            check(perCellCount == full.PrimaryOutput.Count &&
                      perCellSet.Count == fullSet.Count && SameKeys(perCellSet, fullSet),
                "four single-cell requests produce the same world as one four-cell run",
                $"{perCellCount} vs {full.PrimaryOutput.Count}");

            // 同一种切法、共享缓存、两个方向各跑一遍：复用必须在两个方向上都与内容无关，
            // 因为流式玩家以他们走路的顺序访问格子。
            var cache = new PCGPartitionCache();
            var request = new PCGPartitionCacheRequest(cache, 7, 0, 1, Spacing);

            var forwardA = PCGPartitionRunner.Run(Executor(graph), DataContext(), firstHalf, 0, request);
            var forwardB = PCGPartitionRunner.Run(Executor(graph), DataContext(), secondHalf, 0, request);
            var reversedB = PCGPartitionRunner.Run(Executor(graph), DataContext(), secondHalf, 0,
                new PCGPartitionCacheRequest(new PCGPartitionCache(), 7, 0, 1, Spacing));
            var reversedA = PCGPartitionRunner.Run(Executor(graph), DataContext(), firstHalf, 0,
                new PCGPartitionCacheRequest(new PCGPartitionCache(), 7, 0, 1, Spacing));

            check(forwardA.PrimaryOutput.Count + forwardB.PrimaryOutput.Count ==
                      reversedA.PrimaryOutput.Count + reversedB.PrimaryOutput.Count &&
                      SamePoints(forwardA, forwardB, fullSet) && SamePoints(reversedA, reversedB, fullSet),
                "a shared cache gives the same points whichever order the windows arrive in",
                $"cached {cache.CellCount} cell(s)");

            // 而一个窗口的运行绝不把它的 padding 漏出来：它返回的每个点都在被要求的那些格子之内，
            // 这正是两个相邻窗口不会都摆同一件道具的原因。
            int leakChecked = 0;
            bool leaked = false;
            string leakedKey = null;

            foreach (var key in PointSet(a))
            {
                var parts = key.Split('|');
                var position = new Vector3(
                    float.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture),
                    float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture),
                    float.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture));

                if (PCGPartitionPlanner.IsInsideCell(firstHalf[0], position) ||
                    PCGPartitionPlanner.IsInsideCell(firstHalf[1], position))
                {
                    leakChecked++;
                    continue;
                }

                leaked = true;
                leakedKey = key;
                break;
            }

            // 计数也一起断言：一个从没跑过的循环否则会报告出与
            // "每个点确实都在里面"的窗口同样的绿灯。
            string leakDetail = leaked
                ? $"{leakChecked} of {a.PrimaryOutput.Count} inside; first leak {leakedKey}"
                : $"{leakChecked}/{a.PrimaryOutput.Count} inside, none leaked";

            check(!leaked && leakChecked == a.PrimaryOutput.Count && leakChecked > 100,
                "a window run reports no point outside the cells it was asked for",
                leakDetail);
        }

        private const float DataCell = 600.0f;
        private const float Spacing = 30.0f;

        private static BoundingBox DataRegion() => new BoundingBox(
            new Vector3(-600.0f, -50.0f, -600.0f), new Vector3(600.0f, 50.0f, 600.0f));

        private static PCGExecutionContext DataContext() => new PCGExecutionContext
        {
            Seed = 20261006,
            GenerationBounds = DataRegion(),
        };

        private static PCGGraphExecutor Executor(PCGGraph graph)
        {
            if (!PCGGraphCompiler.TryCompile(graph, out var compiled))
                throw new InvalidOperationException("the window-equivalence graph failed to compile");

            return new PCGGraphExecutor(compiled);
        }

        private static PCGGraph ScatterDataGraph() => new PCGGraph
        {
            Name = "WindowEquivalence",
            Nodes = new[]
            {
                new PCGNode { TypeId = PCGVolumeSamplerNode.Id, Values = new object[] { 1200, 0, 1.0f, true, 1.0f } },
                new PCGNode { TypeId = PCGSelfPruningFilterNode.Id, Values = new object[] { Spacing, false, true } },
            },
            Edges = new[]
            {
                new PCGEdge { OutputNodeIndex = 0, InputNodeIndex = 1 },
            },
        };

        /// <summary>
        /// 把结果的位置量化成有序集合：与顺序无关，
        /// 于是一次部分运行的合并顺序不会被误读成内容差异。
        /// </summary>
        private static SortedSet<string> PointSet(PCGExecutionResult result)
        {
            var set = new SortedSet<string>(StringComparer.Ordinal);
            var points = result?.PrimaryOutput?.Points;

            for (int i = 0; points != null && i < points.Length; i++)
            {
                var p = points[i].Position;
                set.Add(string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "{0:0.###}|{1:0.###}|{2:0.###}", p.X, p.Y, p.Z));
            }

            return set;
        }

        private static bool SamePoints(PCGExecutionResult a, PCGExecutionResult b, SortedSet<string> expected)
        {
            var union = PointSet(a);
            foreach (var key in PointSet(b))
                union.Add(key);

            return union.Count == expected.Count && SameKeys(union, expected);
        }

        private static bool SameKeys(SortedSet<string> left, SortedSet<string> right)
        {
            foreach (var key in left)
            {
                if (!right.Contains(key))
                    return false;
            }

            return true;
        }

        // ---- 面积密度对格子尺寸 ---------------------------------------

        /// <summary>
        /// 格子尺寸可以当成流式旋钮的理由：按面积采样时，任何格子尺寸都给出同一个世界，
        /// 而每次运行固定数量意味着"每格固定数量"，会跟着它把世界成倍改变。
        /// </summary>
        private static void DensityModeTests()
        {
            const float RegionHalfSize = 1000.0f;
            const float ExpectedWhole = 8000.0f;      // 2000 per 1000x1000 block over 2000 x 2000

            var graph = DensityGraph(2000.0f);
            var context = new PCGExecutionContext
            {
                Seed = 20261006,
                GenerationBounds = new BoundingBox(
                    new Vector3(-RegionHalfSize, -50.0f, -RegionHalfSize),
                    new Vector3(RegionHalfSize, 50.0f, RegionHalfSize)),
            };

            var whole = new PCGGraphExecutor(CompileGraph(graph)).Execute(context);
            check(whole.PrimaryOutput.Count == 8000,
                "sampling by area over the whole region gives 2000 points per 1000x1000 block",
                $"{whole.PrimaryOutput.Count}");

            // 面积能被一个方块整除的格子，采样数正好等于整片区域的应采数；更小的格子
            // 向上取整，每格最多多出一个点。两个界都要断言，因为"大致不变"
            // 不是设计者能拿来规划的东西——每格一个点才是。
            foreach (var fixture in new (int CellSize, bool Exact)[]
                     {
                         (1000, true), (500, true), (250, true), (125, false),
                     })
            {
                var cells = PCGPartitionPlanner.Plan(
                    context.GenerationBounds, fixture.CellSize, 0.0f, out _);
                if (cells == null)
                {
                    check(false, $"the {fixture.CellSize}-unit partition planned", "planner refused the plan");
                    continue;
                }

                var partitioned = PCGPartitionRunner.Run(
                    new PCGGraphExecutor(CompileGraph(graph)), context, cells);

                int delta = partitioned.PrimaryOutput.Count - whole.PrimaryOutput.Count;

                if (fixture.Exact)
                {
                    check(delta == 0,
                        $"a {fixture.CellSize}-unit partition samples exactly the whole region's count",
                        $"{cells.Count} cells -> {partitioned.PrimaryOutput.Count} (delta {delta})");
                }
                else
                {
                    check(delta > 0 && delta <= cells.Count,
                        $"a {fixture.CellSize}-unit partition adds at most one point per cell " +
                        "(the rounding-up, not a density change)",
                        $"{cells.Count} cells -> {partitioned.PrimaryOutput.Count} (delta {delta})");
                }
            }

            // 这个模式为之而生的那个坑，钉成断言而不是留在描述里：固定数量下，同样的格子尺寸
            // 变化就会把世界成倍地改变。
            var countGraph = DensityGraph(0.0f, 2000);
            var countWhole = new PCGGraphExecutor(CompileGraph(countGraph)).Execute(context);
            var countCells = PCGPartitionPlanner.Plan(context.GenerationBounds, 250.0f, 0.0f, out _);
            var countPartitioned = PCGPartitionRunner.Run(
                new PCGGraphExecutor(CompileGraph(countGraph)), context, countCells);

            check(countWhole.PrimaryOutput.Count == 2000 &&
                      countPartitioned.PrimaryOutput.Count == 2000 * countCells.Count,
                "a fixed Point Count means per cell under partitioning, so the world scales with cell count",
                $"{countWhole.PrimaryOutput.Count} vs {countPartitioned.PrimaryOutput.Count} over {countCells.Count} cells");

            // 舍入：面积不足一个方块时仍然要采样，因为否则就是
            // 按格子尺寸大小长出来的洞。
            var tiny = new PCGExecutionContext
            {
                Seed = 1,
                GenerationBounds = new BoundingBox(
                    new Vector3(-750.0f, -10.0f, -750.0f), new Vector3(750.0f, 10.0f, 750.0f)),
            };

            var onePerBlock = new PCGGraphExecutor(CompileGraph(DensityGraph(1.0f))).Execute(tiny);
            check(onePerBlock.PrimaryOutput.Count == 3,
                "2.25 blocks at one point per block rounds up to 3 rather than down to 2",
                $"{onePerBlock.PrimaryOutput.Count}");
        }

        private static PCGGraph DensityGraph(float pointsPerBlock, int pointCount = 0) => new PCGGraph
        {
            Name = "DensityMode",
            Nodes = new[]
            {
                new PCGNode
                {
                    TypeId = PCGVolumeSamplerNode.Id,
                    Values = new object[] { pointCount, 0, 1.0f, true, 1.0f, pointsPerBlock },
                },
            },
        };

        private static PCGCompiledGraph CompileGraph(PCGGraph graph)
        {
            if (!PCGGraphCompiler.TryCompile(graph, out var compiled))
                throw new InvalidOperationException("the density fixture graph failed to compile");

            return compiled;
        }

        /// <summary>
        /// 图文件存下来的参数下标，只有在节点读的就是编辑器写进去的那个槽位时才有意义，
        /// 所以追加一个参数必须在接缝处验：邻居们必须仍然读到自己的值。
        /// 这里静默地错一位不会抛异常——它会把每一份已存图里之后所有参数都贴错标签，
        /// 那种缺陷会一路发布出去，然后被归咎于编辑器。
        /// </summary>
        private static void ParameterSlotTests()
        {
            check(PCGVolumeSamplerNode.ParameterCount == 6 && PCGLandscapeSamplerNode.ParameterCount == 10,
                "the two samplers read the number of slots their registrations declare",
                $"volume {PCGVolumeSamplerNode.ParameterCount}, landscape {PCGLandscapeSamplerNode.ParameterCount}");

            var volume = new PCGVolumeSamplerNode(new PCGNodeRegistry.PCGNodeSerializationData(
                PCGVolumeSamplerNode.Id,
                new object[] { 5000, 7, 1.5f, true, 0.4f, 120.0f },
                0));

            check(volume.PointCount == 5000 && volume.SeedOffset == 7 && volume.Scale == 1.5f &&
                      volume.RandomRotation && System.Math.Abs(volume.Density - 0.4f) < 1e-6f &&
                      System.Math.Abs(volume.PointsPerBlock - 120.0f) < 1e-6f,
                "the volume sampler reads each of its six slots, including the appended area density",
                volume.DescribeParameters());

            // 在这个参数存在之前存下的图只有五个值：新来的那个必须退回
            // 历史行为，而不是越过数组边界读，也不许拿起邻居的值。
            var legacy = new PCGVolumeSamplerNode(new PCGNodeRegistry.PCGNodeSerializationData(
                PCGVolumeSamplerNode.Id,
                new object[] { 100, 0, 1.0f, false, 1.0f },
                0));

            check(legacy.PointCount == 100 && legacy.PointsPerBlock == 0.0f,
                "a five-value graph still reads its count and gets fixed-count sampling",
                legacy.DescribeParameters());

            var landscape = new PCGLandscapeSamplerNode(new PCGNodeRegistry.PCGNodeSerializationData(
                PCGLandscapeSamplerNode.Id,
                new object[] { 800, 0.25f, 10.0f, false, 1, true, 2.0f, 35.0f, 11, 60.0f },
                0));

            check(landscape.PointCount == 800 && System.Math.Abs(landscape.Jitter - 0.25f) < 1e-6f &&
                      System.Math.Abs(landscape.Scale - 2.0f) < 1e-6f &&
                      System.Math.Abs(landscape.MaxSlopeDegrees - 35.0f) < 1e-6f &&
                      landscape.SeedOffset == 11 &&
                      System.Math.Abs(landscape.PointsPerBlock - 60.0f) < 1e-6f,
                "the landscape sampler reads its tenth slot without shifting the nine before it",
                landscape.DescribeParameters());
        }

        // ---- 缓存驱逐 --------------------------------------------------

        private static void CacheEvictionTests()
        {
            var cells = Cells();
            var cache = new PCGPartitionCache();

            var first = cells[0];
            var second = cells[1];
            var signature = new PCGCellSignature(1234, 77, 1, 1, first, 20.0f);
            var outputs = new PCGData[] { PCGData.Empty };

            cache.Store(in first, in signature, outputs, new[] { 4 });
            cache.Store(in second, new PCGCellSignature(1234, 77, 1, 1, second, 20.0f), outputs, new[] { 4 });
            check(cache.CellCount == 2, "two cells stored", $"{cache.CellCount}");

            var request = new PCGPartitionCacheRequest(cache, 1234, 1, 1, 20.0f);
            var window = new List<PCGPartitionCell> { first, second };
            check(cache.DirtyCells(window, request, 77).Count == 0,
                "both cells are reusable before eviction", "dirty=0");

            check(cache.Evict(first.IndexX, first.IndexZ), "evicting a stored cell reports that it dropped one");
            check(cache.CellCount == 1, "only the evicted cell is gone", $"{cache.CellCount}");
            check(cache.IsFresh(in second, new PCGCellSignature(1234, 77, 1, 1, second, 20.0f)),
                "the other cell is untouched by its neighbour's eviction");

            var dirty = cache.DirtyCells(window, request, 77);
            check(dirty.Count == 1 && dirty[0].IndexX == first.IndexX && dirty[0].IndexZ == first.IndexZ,
                "the evicted cell is the one the next request has to regenerate",
                $"dirty={dirty.Count}");

            check(!cache.Evict(9999, 9999), "evicting a cell that was never stored reports false");
            check(cache.CellCount == 1, "and it takes nothing with it", $"{cache.CellCount}");

            // 层级规划会在同一片区域上铺两张网格，于是一个粗格子和一个细格子可能带着
            // 同样的 (x,z) 下标。缓存以这些下标为键，所以签名必须不同——而它们确实不同，
            // 因为签名带着格子自己的边界。正是这条断言让两级
            // 共用一份缓存，而不是各需一份。
            var coarseCell = cells[0];
            var fineCell = new PCGPartitionCell(
                new BoundingBox(coarseCell.ClipBounds.Minimum, coarseCell.ClipBounds.Minimum + new Vector3(100.0f, 0.0f, 100.0f)),
                new BoundingBox(coarseCell.ClipBounds.Minimum, coarseCell.ClipBounds.Minimum + new Vector3(100.0f, 0.0f, 100.0f)),
                coarseCell.IndexX, coarseCell.IndexZ);

            var coarseSignature = new PCGCellSignature(7, 1, 1, 1, coarseCell, 20.0f);
            var fineSignature = new PCGCellSignature(7, 1, 1, 1, fineCell, 20.0f);

            cache.Clear();
            cache.Store(in coarseCell, in coarseSignature, new[] { PCGData.Empty }, new[] { 0 });

            check(cache.IsFresh(in coarseCell, in coarseSignature) &&
                      !cache.IsFresh(in fineCell, in fineSignature),
                "a coarse cell and a fine cell sharing indices are not each other's cached result",
                $"coarse fresh, fine fresh={cache.IsFresh(in fineCell, in fineSignature)}");
        }

        // ---- 助手 ---------------------------------------------------------

        private static List<PCGPartitionCell> Cells() =>
            PCGPartitionPlanner.Plan(
                new BoundingBox(new Vector3(-RegionHalf, 0.0f, -RegionHalf),
                                new Vector3(RegionHalf, 200.0f, RegionHalf)),
                CellSize, 20.0f, out _);

        /// <summary>
        /// 一个点到格子裁剪矩形的距离，写成"点在每一段区间之外多远"，
        /// 而不是写成"钳位后的最近点"。
        /// </summary>
        /// <remarks>
        /// 两种写法必须有区别，否则它们互相一致证明不了什么——生产那份写法在
        /// <see cref="PCGCellStreaming"/> 里钳位到最近点并比较平方。
        /// </remarks>
        private static float DistanceToCell(in PCGPartitionCell cell, in Vector3 center)
        {
            var min = cell.ClipBounds.Minimum;
            var max = cell.ClipBounds.Maximum;

            float gapX = 0.0f;
            if (center.X < min.X)
                gapX = min.X - center.X;
            else if (center.X > max.X)
                gapX = center.X - max.X;

            float gapZ = 0.0f;
            if (center.Z < min.Z)
                gapZ = min.Z - center.Z;
            else if (center.Z > max.Z)
                gapZ = center.Z - max.Z;

            return (float)Math.Sqrt((double)gapX * gapX + (double)gapZ * gapZ);
        }

        private static void CheckThrows(Action action, string what)
        {
            bool threw = false;
            try
            {
                action();
            }
            catch (ArgumentNullException)
            {
                threw = true;
            }
            catch (Exception)
            {
                // 其他异常都是不对的那一个，会在下面作为失败落出去。
            }

            check(threw, $"{what} is refused rather than silently ignored");
        }
    }
}

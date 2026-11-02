// PCG 插件 - 分区格子复用的可执行测试。
//
// 缓存只有在"命中缓存的一跑"与"完整的一跑"不可区分时才安全，所以这里的核心断言不是
// "它跑得快了些"，而是"点与点逐个相同，且任何可能改变某个格子答案的输入都会让它失效"。
// 速度那部分在 Tools/PCGBench 里量；正确性才是本文件负责的。

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
    internal static class PartitionCacheTests
    {
        private static CheckDelegate _check;

        // 先写条件，于是一句断言读起来是"这件事成立，因为……"。
        private static void check(bool ok, string what, string detail = null) => _check(what, ok, detail);

        private const float RegionHalf = 400.0f;
        private const float CellSize = 400.0f;
        private const float Spacing = 30.0f;

        /// <summary>
        /// 体积喂给它那些签名的版本 token：手动旋钮与宿主的编号，两者合并。
        /// </summary>
        /// <remarks>
        /// 钉在这里是因为：一个悄悄忽略掉两个输入之一的合并，会把过期的格子留着，
        /// 而除了"世界没更新"之外没有任何症状——那正是手动版本
        /// 一开始就是为了避开的那类失败。
        /// </remarks>
        private static void VersionCombineTests()
        {
            int baseline = PCGPartitionCache.CombineWorldVersion(7, 3);

            check(PCGPartitionCache.CombineWorldVersion(8, 3) != baseline,
                "raising the manual partition version moves the token");
            check(PCGPartitionCache.CombineWorldVersion(7, 4) != baseline,
                "a change in the host's number moves the token as well");
            check(PCGPartitionCache.CombineWorldVersion(7, 3) == baseline,
                "the same pair gives the same token, so a run that changed nothing stays cached");
            check(PCGPartitionCache.CombineWorldVersion(1, 0) != PCGPartitionCache.CombineWorldVersion(0, 1),
                "neither side is ignored: swapping the two changes the answer");

            // 这个 token 必须真的进到缓存自己的比较里，否则那个助手就只是装饰。
            var cell = new PCGPartitionCell(
                new BoundingBox(new Vector3(0.0f, 0.0f, 0.0f), new Vector3(1.0f, 1.0f, 1.0f)),
                new BoundingBox(new Vector3(0.0f, 0.0f, 0.0f), new Vector3(1.0f, 1.0f, 1.0f)),
                0, 0);

            var before = new PCGCellSignature(11, 22, 33, baseline, in cell, 0.0f);
            var after = new PCGCellSignature(11, 22, 33, PCGPartitionCache.CombineWorldVersion(8, 3), in cell, 0.0f);

            check(!before.Equals(after),
                "a moved token makes two otherwise identical cells different, which is what invalidates them",
                after.ToString());
        }

        internal static void Run(CheckDelegate callback)
        {
            _check = callback;

            PCGNodeRegistry.Clear();
            PCG.Runtime.PCGNodeRegistration.RegisterAll();

            try
            {
                ReuseTests();
                InvalidationTests();
                VersionCombineTests();
                OwnershipTests();
            LayerTests();
            }
            finally
            {
                PCGNodeRegistry.Clear();
            }
        }

        // ---- 复用 ----------------------------------------------------------

        private static void ReuseTests()
        {
            var cache = new PCGPartitionCache();
            var graph = ScatterGraph(2000, Spacing);

            var first = PCGPartitionRunner.Run(Executor(graph), Context(), Cells(CellSize, Spacing), 0,
                    Request(cache, graph, Spacing));

            GC.KeepAlive(first);

            var fresh = PCGPartitionRunner.Run(Executor(graph), Context(), Cells(CellSize, Spacing), 0);

            check(cache.LastRunExecutedCells == 4 && cache.LastRunReusedCells == 0,
                "a cold cache executes every cell",
                $"executed={cache.LastRunExecutedCells} reused={cache.LastRunReusedCells}");

            check(fresh.PrimaryOutput.Count > 100,
                "the fixture actually produces points (a zero-point run would make every comparison below vacuous)",
                $"points={fresh.PrimaryOutput.Count}");

            var fingerprintFirst = Fingerprint(fresh);
            check(Fingerprint(first) == fingerprintFirst,
                "a cached run merges the same points, in the same order, as a run with no cache at all",
                $"{first.PrimaryOutput.Count} vs {fresh.PrimaryOutput.Count}");

            var second = PCGPartitionRunner.Run(Executor(graph), Context(), Cells(CellSize, Spacing), 0,
                    Request(cache, graph, Spacing));

            check(cache.LastRunReusedCells == 4 && cache.LastRunExecutedCells == 0,
                "with nothing changed, every cell is reused and none is executed",
                $"executed={cache.LastRunExecutedCells} reused={cache.LastRunReusedCells}");
            check(Fingerprint(second) == fingerprintFirst,
                "the reused run is point-for-point identical to the full one",
                $"{second.PrimaryOutput.Count} vs {fresh.PrimaryOutput.Count}");
            check(second.PrimaryOutput.Count == fresh.PrimaryOutput.Count,
                "reuse neither duplicates nor loses points",
                $"{second.PrimaryOutput.Count} vs {fresh.PrimaryOutput.Count}");

            var third = PCGPartitionRunner.Run(Executor(graph), Context(), Cells(CellSize, Spacing), 0,
                    Request(cache, graph, Spacing));
            check(Fingerprint(third) == fingerprintFirst && cache.CellCount == 4,
                "reuse is stable over repeated runs rather than drifting",
                $"cells stored={cache.CellCount}");

            check(second.Stats != null && second.Stats.Length == 0,
                "a fully reused run honestly reports no node execution instead of replaying old timings",
                $"stats={second.Stats?.Length ?? -1}");
        }

        // ---- 失效 ---------------------------------------------------

        private static void InvalidationTests()
        {
            var graph = ScatterGraph(1200, Spacing);
            var cells = Cells(CellSize, Spacing);

            // 换 seed 这一路。
            var cache = new PCGPartitionCache();
            PCGPartitionRunner.Run(Executor(graph), Context(), cells, 0, Request(cache, graph, Spacing));
            var reseeded = Request(cache, graph, Spacing);
            var context = new PCGExecutionContext { Seed = 99, GenerationBounds = Region() };
            PCGPartitionRunner.Run(Executor(graph), context, cells, 0, reseeded);
            check(cache.LastRunExecutedCells == 4 && cache.LastRunReusedCells == 0,
                "changing the seed re-executes every cell",
                $"executed={cache.LastRunExecutedCells}");

            // 格子几何：不同的格子尺寸意味着不同的区域，所以带过来的任何东西都不再有效。
            cache = new PCGPartitionCache();
            PCGPartitionRunner.Run(Executor(graph), Context(), cells, 0, Request(cache, graph, Spacing));
            var narrower = Cells(CellSize / 2.0f, Spacing);
            PCGPartitionRunner.Run(Executor(graph), Context(), narrower, 0, Request(cache, graph, Spacing));
            check(cache.LastRunReusedCells == 0,
                "re-splitting the region re-executes rather than reusing across different cell bounds",
                $"reused={cache.LastRunReusedCells} over {narrower.Count} cells");

            // padding：它是一个格子所见内容的一部分，所以把它加宽必须让缓存失效。
            cache = new PCGPartitionCache();
            PCGPartitionRunner.Run(Executor(graph), Context(), cells, 0, Request(cache, graph, Spacing));
            PCGPartitionRunner.Run(Executor(graph), Context(), cells, 0, Request(cache, graph, Spacing * 2.0f));
            check(cache.LastRunReusedCells == 0,
                "changing the padding re-executes every cell (padding is part of what a cell saw)");

            // 世界版本：调用方那些图看不见的输入所用的通道。
            cache = new PCGPartitionCache();
            PCGPartitionRunner.Run(Executor(graph), Context(), cells, 0, Request(cache, graph, Spacing));
            var bumped = Request(cache, graph, Spacing, worldVersion: 2);
            PCGPartitionRunner.Run(Executor(graph), Context(), cells, 0, bumped);
            check(cache.LastRunReusedCells == 0,
                "bumping the world version re-executes every cell",
                $"reused={cache.LastRunReusedCells}");

            var again = Request(cache, graph, Spacing, worldVersion: 2);
            PCGPartitionRunner.Run(Executor(graph), Context(), cells, 0, again);
            check(cache.LastRunReusedCells == 4,
                "the new world version then stays reusable, rather than invalidating forever",
                $"reused={cache.LastRunReusedCells}");

            // 显式拆掉。
            cache.InvalidateAll();
            check(cache.CellCount == 0, "InvalidateAll drops every stored cell", $"cells={cache.CellCount}");
            PCGPartitionRunner.Run(Executor(graph), Context(), cells, 0, Request(cache, graph, Spacing));
            check(cache.LastRunExecutedCells == 4, "after invalidation everything runs again");
        }

        // ---- 拒绝 -------------------------------------------------------

        private static void OwnershipTests()
        {
            // 只有当一个脏格子的道具能在不碰新格子道具的前提下被移除时，复用才安全，
            // 所以那个名字 token 就是归属记录，而这几条检查正是体积的清理所依赖的。
            string name = PCGSpawnNaming.ActorName("rock", 7, 1, 2);
            check(PCGSpawnNaming.IsGeneratedName(name),
                "a cell-tagged name is still recognisable as PCG output", name);
            check(PCGSpawnNaming.BelongsToCell(name, 1, 2),
                "the name says which cell produced it", name);
            check(!PCGSpawnNaming.BelongsToCell(name, 11, 2) &&
                  !PCGSpawnNaming.BelongsToCell(name, 1, 12) &&
                  !PCGSpawnNaming.BelongsToCell(name, 0, 0),
                "and does not answer to a neighbouring cell", name);

            check(PCGSpawnNaming.TryReadCell(name, out int readX, out int readZ) && readX == 1 && readZ == 2,
                "the cell can be read back out of the name", $"{readX},{readZ}");
            check(!PCGSpawnNaming.TryReadCell("SomeHandPlacedRock", out _, out _),
                "a hand-placed actor name yields no cell rather than a guessed one");

            // 规划自己的格子编号必须与名字里带的编号一致，否则清理会漏掉
            // 它本来要移除的每一个 actor。
            var cells = Cells(CellSize, Spacing);
            check(cells.Count == 4 && cells[0].IndexX == 0 && cells[0].IndexZ == 0 &&
                  cells[cells.Count - 1].IndexX == 1 && cells[cells.Count - 1].IndexZ == 1,
                "planner cell indices line up with the token range", $"cells={cells.Count}");

            // 脏格子的记账：体积在运行之前清掉的恰好是这些。
            var ownGraph = ScatterGraph(800, Spacing);
            var ownCache = new PCGPartitionCache();
            var ownRequest = Request(ownCache, ownGraph, Spacing);

            check(ownCache.DirtyCells(cells, ownRequest, 20261006).Count == 4,
                "an empty cache marks every cell dirty", $"dirty={ownCache.DirtyCells(cells, ownRequest, 20261006).Count}");

            PCGPartitionRunner.Run(Executor(ownGraph), Context(), cells, 0, ownRequest);
            check(ownCache.DirtyCells(cells, ownRequest, 20261006).Count == 0,
                "after a run, no cell is dirty", $"dirty={ownCache.DirtyCells(cells, ownRequest, 20261006).Count}");
            check(ownCache.DirtyCells(cells, ownRequest, 20261007).Count == 4,
                "a different seed marks every cell dirty before it is ever run",
                "the dirty list drives cleanup, so it must move in step with the seed");

            var widerPadding = Request(ownCache, ownGraph, Spacing * 2.0f);
            check(ownCache.DirtyCells(cells, widerPadding, 20261006).Count == 4,
                "changed padding marks every cell dirty too");
            check(ownCache.DirtyCells(null, ownRequest, 20261006).Count == 0,
                "no cells means nothing to clear");

            // 截断：一次提前停下的运行不许留下半幅画面。
            var graph = ScatterGraph(1200, Spacing);
            var cache = new PCGPartitionCache();
            PCGPartitionRunner.Run(Executor(graph), Context(), Cells(CellSize, Spacing), 0, Request(cache, graph, Spacing));
            check(cache.CellCount == 4, "the warm-up run stored every cell", $"cells={cache.CellCount}");

            var truncated = PCGPartitionRunner.Run(Executor(graph), Context(), Cells(CellSize, Spacing), 3,
                Request(cache, graph, Spacing));
            check(truncated.Warnings != null && Array.Exists(truncated.Warnings, w => w.Contains("point ceiling")),
                "the ceiling is still reported when reuse is on");
            check(cache.CellCount == 0,
                "a truncated run empties the cache rather than storing an incomplete picture",
                $"cells={cache.CellCount}");
        }

        // ---- 分层 ---------------------------------------------------------

        /// <summary>
        /// 一个分层的体积按格子粒度每档跑一张图，各有自己的缓存、各占一份
        /// 对象名字。这些是体积的清理所依赖的算术与命名保证。
        /// </summary>
        private static void LayerTests()
        {
            var region = Region();
            var plans = PCGPartitionPlanner.PlanLayers(
                region, new[] { 800.0f, 400.0f, 100.0f }, new[] { 0.0f, 30.0f, 10.0f },
                new[] { "boulders", "props", "detail" }, out List<string> errors);

            check(errors == null && plans != null && plans.Length == 3,
                "three layers plan side by side over one region", errors == null ? "ok" : string.Join(" | ", errors));

            check(plans != null && plans[0].Count == 1 && plans[1].Count == 4 && plans[2].Count == 64,
                "each layer gets its own grid, not the first one's",
                plans == null ? "null" : $"{plans[0].Count}/{plans[1].Count}/{plans[2].Count}");

            // 每一层都必须自己覆盖整片区域，否则某些道具种类会悄悄地不再出现在
            // 由别的那些层到达的角落里。
            int uncovered = 0;
            foreach (int x in new[] { -399, -1, 0, 1, 399 })
            {
                foreach (int z in new[] { -399, 0, 399 })
                {
                    for (int layer = 0; layer < plans.Length; layer++)
                    {
                        int hits = 0;
                        for (int c = 0; c < plans[layer].Count; c++)
                        {
                            if (PCGPartitionPlanner.IsInsideCell(plans[layer][c], new Vector3(x, 0.0f, z)))
                                hits++;
                        }

                        if (hits != 1)
                            uncovered++;
                    }
                }
            }

            check(uncovered == 0, "every layer tiles the region exactly once", $"uncovered={uncovered}");

            var broken = PCGPartitionPlanner.PlanLayers(
                region, new[] { 400.0f, 0.0f }, null, new[] { "props", "detail" }, out List<string> brokenErrors);
            check(broken == null && brokenErrors != null && brokenErrors.Count == 1 &&
                  brokenErrors[0].Contains("detail"),
                "a layer that cannot be planned is reported by name and aborts the set",
                brokenErrors == null ? "no errors" : brokenErrors[0]);

            check(PCGPartitionPlanner.PlanLayers(region, null, null, null, out List<string> emptyErrors) == null &&
                  emptyErrors.Count == 1,
                "no layers at all is an error rather than a silent no-op", emptyErrors[0]);

            // 归属：两个层里的同一个格子不许听同一个名字调遣。
            string layerOne = PCGSpawnNaming.ActorName("moss", 3, 0, 1, 2);
            string layerTwo = PCGSpawnNaming.ActorName("moss", 3, 1, 1, 2);
            check(layerOne != layerTwo,
                "two layers name the same point in the same cell differently", $"{layerOne} vs {layerTwo}");
            check(PCGSpawnNaming.BelongsToCell(layerTwo, 1, 1, 2) &&
                  !PCGSpawnNaming.BelongsToCell(layerTwo, 0, 1, 2),
                "and each name answers only to its own layer", layerTwo);

            // 每层各自的缓存：新的一层的缓存不可能知道别层的格子任何事。
            var cells = plans[1];
            var graph = ScatterGraph(600, Spacing);
            var executor = Executor(graph);
            var cacheA = new PCGPartitionCache();
            var cacheB = new PCGPartitionCache();
            var requestA = new PCGPartitionCacheRequest(cacheA, 0, 0, 1, Spacing);
            var requestB = new PCGPartitionCacheRequest(cacheB, 0, 0, 1, Spacing);

            PCGPartitionRunner.Run(executor, Context(), cells, 0, requestA);
            check(cacheA.CellCount == cells.Count && cacheB.CellCount == 0,
                "one layer's run fills only its own cache",
                $"a={cacheA.CellCount} b={cacheB.CellCount}");
            check(cacheB.DirtyCells(cells, requestB, 20261006).Count == cells.Count,
                "another layer's cells stay dirty until that layer actually runs");
            check(cacheA.DirtyCells(cells, requestA, 20261006).Count == 0,
                "the layer that did run has nothing left to redo");
        }

        // ---- 夹具 -------------------------------------------------------

        private static BoundingBox Region() => new BoundingBox(
            new Vector3(-RegionHalf, -100.0f, -RegionHalf), new Vector3(RegionHalf, 100.0f, RegionHalf));

        private static PCGExecutionContext Context() => new PCGExecutionContext
        {
            Seed = 20261006,
            GenerationBounds = Region(),
        };

        private static List<PCGPartitionCell> Cells(float cellSize, float padding)
        {
            var cells = PCGPartitionPlanner.Plan(Region(), cellSize, padding, out string error);
            if (cells == null)
                throw new InvalidOperationException("the cache test partition could not be planned: " + error);

            return cells;
        }

        private static PCGPartitionCacheRequest Request(
            PCGPartitionCache cache, PCGGraph graph, float padding, int worldVersion = 1, int structuralHash = 0) =>
            new PCGPartitionCacheRequest(cache, structuralHash, 0, worldVersion, padding);

        private static PCGGraph ScatterGraph(int pointCount, float spacing) => new PCGGraph
        {
            Name = "CacheScatter",
            Nodes = new[]
            {
                new PCGNode { TypeId = PCGVolumeSamplerNode.Id, Values = new object[] { pointCount, 0, 1.0f, true, 1.0f } },
                new PCGNode { TypeId = PCGSelfPruningFilterNode.Id, Values = new object[] { spacing, false, true } },
                new PCGNode { TypeId = PCGDensityFilterNode.Id, Values = new object[] { 0.2f, 1.0f, false } },
            },
            Edges = new[]
            {
                new PCGEdge { OutputNodeIndex = 0, InputNodeIndex = 1 },
                new PCGEdge { OutputNodeIndex = 1, InputNodeIndex = 2 },
            },
        };

        private static PCGGraph SpawnerGraph() => new PCGGraph
        {
            Name = "CacheSpawner",
            Nodes = new[]
            {
                new PCGNode { TypeId = PCGVolumeSamplerNode.Id, Values = new object[] { 20, 0, 1.0f, true, 1.0f } },
                new PCGNode { TypeId = PCGSpawnStaticModelNode.Id, Values = new object[] { Guid.Empty, 0.0f, 1.0f, true, 50 } },
            },
            Edges = new[] { new PCGEdge { OutputNodeIndex = 0, InputNodeIndex = 1 } },
        };

        private static PCGCompiledGraph Compile(PCGGraph graph)
        {
            if (!PCGGraphCompiler.TryCompile(graph, out var compiled))
            {
                var diagnostics = compiled?.Diagnostics;
                throw new InvalidOperationException("cache test graph would not compile: " +
                    (diagnostics == null || diagnostics.Count == 0 ? "no diagnostics" : diagnostics[0].Message));
            }

            return compiled;
        }

        private static PCGGraphExecutor Executor(PCGGraph graph) => new PCGGraphExecutor(Compile(graph));

        private static long Fingerprint(PCGExecutionResult result)
        {
            var points = result?.PrimaryOutput?.Points;
            long hash = 1469598103934665603L;

            for (int i = 0; points != null && i < points.Length; i++)
            {
                var p = points[i].Position;
                long value = (long)Math.Round(p.X * 1000.0) * 4093 +
                             (long)Math.Round(p.Y * 1000.0) * 719 +
                             (long)Math.Round(p.Z * 1000.0);
                hash = (hash ^ value) * 1099511628211L;
            }

            return hash;
        }
    }
}

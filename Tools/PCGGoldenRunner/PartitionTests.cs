// PCG Plugin - Executable tests for partitioned generation (docs/PCG_PLUGIN_DESIGN.md §7.3).
//
// Partitioning is the part of the plugin whose whole purpose is a property rather than a value: split a region,
// and the result must still look like one continuous world. That property - no point lost at a border, none
// doubled, and the spacing rules still honoured across a border - is exactly the kind of thing that cannot be
// checked by reading the planner, and does not need a scene to check: the cells are arithmetic, and the pruner is
// pure data. So both run here.
//
// The seam test feeds every cell the same fixed point set instead of letting a sampler generate per cell. That is
// deliberate: a sampler's random draws are keyed on point index, which restarts inside each cell, so a sampler
// would blur the one question this test asks - does the neighbourhood logic see across the border?

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
    internal static class PartitionTests
    {
        private const float RegionMin = -400.0f;
        private const float RegionMax = 400.0f;

        /// <summary>Spacing the seam test's pruner enforces, in world units.</summary>
        private const float MinDistance = 60.0f;

        private static CheckDelegate _check;

        // Condition first, so a long assertion reads as "this holds, because ...".
        private static void Check(bool ok, string what, string detail = null) => _check(what, ok, detail);

        internal static void Run(CheckDelegate check)
        {
            _check = check;
            PCGNodeRegistry.Clear();
            PCGNodeRegistry.Register(StaticSourceNode.Id, "Sampler", d => new StaticSourceNode(d), "Static Source");
            PCGNodeRegistry.Register(PCGSelfPruningFilterNode.Id, "Filter", d => new PCGSelfPruningFilterNode(d), "Self-Pruning Filter");

            PlannerTests(check);
            PaddingTests(check);
            RunnerTests(check);

            PCGNodeRegistry.Clear();
        }

        // ---- planner --------------------------------------------------------

        private static void PlannerTests(CheckDelegate check)
        {
            var region = Region(RegionMin, RegionMax);

            var cells = PCGPartitionPlanner.Plan(region, 400.0f, 0.0f, out string error);
            Check(cells != null && error == null, "a four-cell partition planned", error);
            Check(cells?.Count == 4, "a region of 800x800 with cell size 400 gives 4 cells", $"count={cells?.Count ?? -1}");

            // Every clip box must be a proper box: a zero-width cell would run the whole graph, generate a
            // padding's worth of work and then clip away every point it produced.
            bool allProper = cells != null;
            for (int i = 0; cells != null && i < cells.Count; i++)
            {
                allProper &= cells[i].ClipBounds.Maximum.X > cells[i].ClipBounds.Minimum.X &&
                             cells[i].ClipBounds.Maximum.Z > cells[i].ClipBounds.Minimum.Z;
            }
            Check(allProper, "no planned cell is zero-width");

            // Tiling: the cells' clip regions cover the region, and no position belongs to two of them. Sampled
            // on the borders on purpose - that is where both failure modes live.
            int[] xs = { -400, -399, -200, -1, 0, 1, 200, 399, 400 };
            int[] zs = { -400, 0, 400 };
            int gaps = 0, doubles = 0;
            foreach (int x in xs)
            {
                foreach (int z in zs)
                {
                    int hits = 0;
                    for (int i = 0; i < cells.Count; i++)
                    {
                        if (PCGPartitionPlanner.IsInsideCell(cells[i], new Vector3(x, 0.0f, z)))
                            hits++;
                    }

                    if (hits == 0) gaps++;
                    else if (hits > 1) doubles++;
                }
            }
            Check(gaps == 0, "every sampled position including the borders falls in some cell", $"gaps={gaps}");
            Check(doubles == 0, "no sampled position falls in two cells", $"duplicates={doubles}");

            // The region's own edges are the exception to the half-open rule; a grid sampler that lands a point
            // exactly on the maximum must not have it dropped by the last cell.
            var wide = PCGPartitionPlanner.Plan(Region(-900.0f, 900.0f), 600.0f, 0.0f, out _);
            Check(wide?.Count == 9, "a region of 1800x1800 with cell size 600 gives 9 cells", $"count={wide?.Count ?? -1}");
            int edgeHits = 0;
            for (int i = 0; wide != null && i < wide.Count; i++)
            {
                if (PCGPartitionPlanner.IsInsideCell(wide[i], new Vector3(900.0f, 0.0f, 0.0f)))
                    edgeHits++;
                if (PCGPartitionPlanner.IsInsideCell(wide[i], new Vector3(-900.0f, 0.0f, 0.0f)))
                    edgeHits++;
            }
            Check(edgeHits == 2, "both region extremes belong to exactly one cell each", $"hits={edgeHits}");

            // Rounding up a division that barely exceeded the cell count must not leave an empty trailing strip.
            var nearlyExact = PCGPartitionPlanner.Plan(Region(-900.0f, 900.0f), 300.0f, 0.0f, out _);
            int empty = 0;
            for (int i = 0; nearlyExact != null && i < nearlyExact.Count; i++)
            {
                if (nearlyExact[i].ClipBounds.Maximum.X - nearlyExact[i].ClipBounds.Minimum.X <= 0.0f)
                    empty++;
            }
            Check(empty == 0, "an exact multiple leaves no empty trailing cell", $"empty={empty}");

            var rejected = PCGPartitionPlanner.Plan(Region(-4000.0f, 4000.0f), 1.0f, 0.0f, out string ceilingError);
            Check(rejected == null && ceilingError != null && ceilingError.Contains("limit"),
                "an over-fine partition is rejected with a message", ceilingError);

            PCGPartitionPlanner.Plan(Region(-400.0f, 400.0f), 0.0f, 0.0f, out string zeroSizeError);
            Check(zeroSizeError != null, "a cell size of zero is rejected", zeroSizeError);

            var flat = PCGPartitionPlanner.Plan(new BoundingBox(new Vector3(-10, 0, -10), new Vector3(-10, 0, 10)),
                5.0f, 0.0f, out string flatError);
            Check(flat == null && flatError != null,
                "a region with no X extent is rejected", flatError);

            // Padding reaches past the cell but never so far that neighbours overlap more than they disagree.
            var padded = PCGPartitionPlanner.Plan(region, 400.0f, 5000.0f, out _);
            float grown = padded != null && padded.Count > 0
                ? padded[0].GenerationBounds.Minimum.X - padded[0].ClipBounds.Minimum.X
                : 0.0f;
            Check(Math.Abs(grown + 200.0f) < 0.01f, "padding is clamped to half a cell", $"grown={grown:0.##}");
        }

        // ---- padding derivation --------------------------------------------

        private static void PaddingTests(CheckDelegate check)
        {
            Check(Math.Abs(PCGPartitionPlanner.SuggestedPadding(MinDistance, false, 0.0f) - MinDistance) < 0.01f,
                "centre-distance spacing suggests padding of exactly that distance");

            float overlap = PCGPartitionPlanner.SuggestedPadding(MinDistance, true, 30.0f);
            Check(Math.Abs(overlap - (MinDistance + 60.0f)) < 0.01f,
                "bounds-overlap spacing adds the boxes that reach past their centres", $"padding={overlap:0.##}");

            Check(PCGPartitionPlanner.SuggestedPadding(0.0f, false, 99.0f) == 0.0f,
                "a pruner that rejects nothing needs no padding");

            var graph = new PCGGraph
            {
                Name = "PaddingFromGraph",
                Nodes = new[]
                {
                    new PCGNode { TypeId = StaticSourceNode.Id },
                    new PCGNode { TypeId = PCGSelfPruningFilterNode.Id, Values = new object[] { MinDistance, false, true } },
                },
                Edges = new[] { new PCGEdge { OutputNodeIndex = 0, InputNodeIndex = 1 } },
            };

            float fromNodes = PCGPartitionPlanner.SuggestedPaddingForGraph(CompileNodes(graph));
            Check(Math.Abs(fromNodes - MinDistance) < 0.01f,
                "padding read off the graph matches its pruner's own spacing", $"padding={fromNodes:0.##}");

            var noPruner = new PCGGraph
            {
                Name = "NoPruner",
                Nodes = new[] { new PCGNode { TypeId = StaticSourceNode.Id } },
                Edges = Array.Empty<PCGEdge>(),
            };
            Check(PCGPartitionPlanner.SuggestedPaddingForGraph(CompileNodes(noPruner)) == 0.0f,
                "a graph with no spacing rule derives no padding");

            Check(PCGPartitionPlanner.SuggestedPaddingForGraph(null) == 0.0f, "a null node list derives no padding");
        }

        // ---- runner ---------------------------------------------------------

        private static void RunnerTests(CheckDelegate check)
        {
            // A strip one cell deep, so the only border is the one the seam test is about.
            var region = new BoundingBox(
                new Vector3(RegionMin, -100.0f, -200.0f), new Vector3(RegionMax, 100.0f, 200.0f));

            // One point either side of the border, close enough to conflict and with the cell-side one the
            // denser of the pair. Continuous: the sparse one dies. Partitioned with enough padding: same.
            // Partitioned without padding: the cell holding the sparse point never sees the dense one across the
            // border, so it survives and the border shows a prop that should not be there.
            StaticSourceNode.Produced = FixedPoints(
                Point(0.9f, -300.0f),
                Point(0.3f, -30.0f),
                Point(0.9f, 10.0f),
                Point(0.5f, 300.0f));

            var graph = PrunerGraph(MinDistance);

            var continuous = SortedXs(RunOnce(graph, region));
            Check(continuous.Count == 3 && !continuous.Contains(-30.0f),
                "the continuous run prunes the crowded point", Describe(continuous));

            var cells = PCGPartitionPlanner.Plan(region, 400.0f, MinDistance, out string planError);
            Check(cells != null, "the padded partition planned", planError);

            var padded = RunPartitioned(graph, region, cells, 0);
            var paddedXs = SortedXs(padded);
            Check(paddedXs.Count == continuous.Count && paddedXs.Count == 3,
                "a padded partition yields the same number of points as one continuous run",
                $"partitioned={Describe(paddedXs)} continuous={Describe(continuous)}");
            Check(SameSet(paddedXs, continuous),
                "a padded partition keeps the same points as one continuous run (no seam)",
                $"partitioned={Describe(paddedXs)}");
            Check(padded.Warnings != null && padded.Warnings.Length == 0,
                "the padded partition reports no problems of its own",
                padded.Warnings != null && padded.Warnings.Length > 0 ? padded.Warnings[0] : null);

            var unpadded = SortedXs(RunPartitioned(graph, region,
                PCGPartitionPlanner.Plan(region, 400.0f, 0.0f, out _), 0));
            Check(unpadded.Contains(-30.0f) && unpadded.Count == continuous.Count + 1,
                "without padding the border does show a seam, which is what the padding is for",
                Describe(unpadded));

            // Points generated in a cell's padding must never reach the output, and the runner is the only thing
            // standing between them and the scene for the data side.
            StaticSourceNode.Produced = FixedPoints(Point(0.9f, -300.0f), Point(0.9f, 30.0f));
            var straddle = RunPartitioned(PrunerGraph(0.0f), region,
                PCGPartitionPlanner.Plan(region, 400.0f, 100.0f, out _), 0);
            var straddlex = SortedXs(straddle);
            Check(straddlex.Count == 2 && straddlex.Contains(-300.0f) && straddlex.Contains(30.0f),
                "padding points are clipped out of the merged output", Describe(straddlex));

            // The aggregate ceiling: the executor's own budget is per run, so a partitioned generation that
            // trusts it would allow cells x budget actors.
            StaticSourceNode.Produced = FixedPoints(
                Point(0.9f, -350.0f), Point(0.9f, -250.0f), Point(0.9f, 250.0f), Point(0.9f, 350.0f));
            var capped = RunPartitioned(PrunerGraph(0.0f), region, cells, 3);
            Check(capped.Warnings != null && Array.Exists(capped.Warnings, w => w.Contains("point ceiling")),
                "the combined point ceiling stops the run and says so",
                capped.Warnings != null && capped.Warnings.Length > 0 ? string.Join(" | ", capped.Warnings) : "no warnings");
            Check(capped.PrimaryOutput.Count <= 3,
                "the ceiling is honoured rather than reported only", $"kept={capped.PrimaryOutput.Count}");

            // A cell that throws must not lose the cells that worked, and must name itself.
            StaticSourceNode.Thrash = true;
            var broken = RunPartitioned(PrunerGraph(0.0f), region, cells, 0);
            StaticSourceNode.Thrash = false;
            Check(broken.Warnings != null && broken.Warnings.Length == cells.Count,
                "every failing cell is reported separately", $"warnings={broken.Warnings?.Length ?? -1}");
            Check(broken.Warnings != null && Array.Exists(broken.Warnings, w => w.StartsWith("cell ")),
                "a failure names the cell it came from",
                broken.Warnings != null && broken.Warnings.Length > 0 ? broken.Warnings[0] : null);

            // Two sinks must still come out as two data sets; merging them into one pile would silently change
            // what a downstream consumer reads.
            StaticSourceNode.Produced = FixedPoints(Point(0.9f, -300.0f), Point(0.9f, 30.0f));
            var twoSinkResult = PCGPartitionRunner.Run(
                new PCGGraphExecutor(Compile(TwoSinkGraph(0.0f))), BaseContext(region),
                PCGPartitionPlanner.Plan(region, 400.0f, 0.0f, out _), 0);
            Check(twoSinkResult.Outputs?.Length == 2,
                "a two-sink graph keeps two outputs through a partitioned run",
                $"outputs={twoSinkResult.Outputs?.Length ?? -1}");
            Check(twoSinkResult.OutputNodeIndices?.Length == 2,
                "the merged result still says which nodes those outputs came from",
                $"indices={twoSinkResult.OutputNodeIndices?.Length ?? -1}");
            Check(twoSinkResult.Outputs != null && twoSinkResult.Outputs.Length == 2 &&
                  twoSinkResult.Outputs[0].Count == 2 && twoSinkResult.Outputs[1].Count == 2,
                "both sinks come out whole rather than sharing one pile",
                twoSinkResult.Outputs != null && twoSinkResult.Outputs.Length == 2
                    ? $"a={twoSinkResult.Outputs[0].Count} b={twoSinkResult.Outputs[1].Count}"
                    : "no outputs");

            Check(PCGPartitionRunner.Run(new PCGGraphExecutor(Compile(TwoSinkGraph(0.0f))), BaseContext(region), null).Outputs.Length == 0,
                "no cells means no outputs, and no exception");
        }

        // ---- fixtures -------------------------------------------------------

        private static BoundingBox Region(float min, float max) =>
            new BoundingBox(new Vector3(min, -100.0f, min), new Vector3(max, 100.0f, max));

        private static PCGPoint Point(float density, float x) =>
            PCGPoint.Create(new Vector3(x, 0.0f, 0.0f), density, 0);

        private static PCGData FixedPoints(params PCGPoint[] points)
        {
            for (int i = 0; i < points.Length; i++)
                points[i].UpdateBounds(new Vector3(5.0f, 5.0f, 5.0f));

            return new PCGData((PCGPoint[])points.Clone());
        }

        /// <summary>Source -> pruner, with the pruner's spacing set.</summary>
        private static PCGGraph PrunerGraph(float minDistance) => new PCGGraph
        {
            Name = "PartitionedPruner",
            Nodes = new[]
            {
                new PCGNode { TypeId = StaticSourceNode.Id },
                new PCGNode { TypeId = PCGSelfPruningFilterNode.Id, Values = new object[] { minDistance, false, true } },
            },
            Edges = new[] { new PCGEdge { OutputNodeIndex = 0, InputNodeIndex = 1 } },
        };

        /// <summary>The same source feeding two pruners, so the graph has two sinks to keep apart.</summary>
        private static PCGGraph TwoSinkGraph(float minDistance) => new PCGGraph
        {
            Name = "PartitionedTwoSinks",
            Nodes = new[]
            {
                new PCGNode { TypeId = StaticSourceNode.Id },
                new PCGNode { TypeId = PCGSelfPruningFilterNode.Id, Values = new object[] { minDistance, false, true } },
                new PCGNode { TypeId = PCGSelfPruningFilterNode.Id, Values = new object[] { minDistance, false, true } },
            },
            Edges = new[]
            {
                new PCGEdge { OutputNodeIndex = 0, InputNodeIndex = 1 },
                new PCGEdge { OutputNodeIndex = 0, InputNodeIndex = 2 },
            },
        };

        private static PCGNodeBase[] CompileNodes(PCGGraph graph) => Compile(graph)?.Nodes;

        private static PCGCompiledGraph Compile(PCGGraph graph)
        {
            if (!PCGGraphCompiler.TryCompile(graph, out var compiled))
            {
                var diagnostics = compiled?.Diagnostics;
                throw new InvalidOperationException(
                    $"test graph failed to compile: {(diagnostics == null ? "no diagnostics" : diagnostics[0].Message)}");
            }

            return compiled;
        }

        private static PCGExecutionContext BaseContext(BoundingBox region) => new PCGExecutionContext
        {
            Seed = 4242,
            GenerationBounds = region,
        };

        private static PCGExecutionResult RunOnce(PCGGraph graph, BoundingBox region) =>
            new PCGGraphExecutor(Compile(graph)).Execute(BaseContext(region));

        private static PCGExecutionResult RunPartitioned(
            PCGGraph graph, BoundingBox region, IReadOnlyList<PCGPartitionCell> cells, int maxTotalPoints)
        {
            return PCGPartitionRunner.Run(new PCGGraphExecutor(Compile(graph)), BaseContext(region), cells, maxTotalPoints);
        }

        private static List<float> SortedXs(PCGExecutionResult result)
        {
            var xs = new List<float>();
            var points = result?.PrimaryOutput?.Points;
            if (points == null)
                return xs;

            for (int i = 0; i < points.Length; i++)
                xs.Add(points[i].Position.X);

            xs.Sort();
            return xs;
        }

        private static bool SameSet(List<float> a, List<float> b)
        {
            if (a.Count != b.Count)
                return false;

            for (int i = 0; i < a.Count; i++)
            {
                if (Math.Abs(a[i] - b[i]) > 0.01f)
                    return false;
            }

            return true;
        }

        private static string Describe(List<float> xs) => "x=[" + string.Join(",", xs) + "]";

        /// <summary>Emits a fixed data set, so a graph can be run without a scene or a sampler.</summary>
        internal sealed class StaticSourceNode : PCGNodeBase
        {
            public const string Id = "TestStaticSource";

            /// <summary>What every cell is handed; independent of the cell on purpose, see the file header.</summary>
            public static PCGData Produced = PCGData.Empty;

            /// <summary>When set, execution throws, standing in for a cell that fails.</summary>
            public static bool Thrash;

            public StaticSourceNode(PCGNodeRegistry.PCGNodeSerializationData data)
            {
            }

            public override string TypeId => Id;

            public override string DisplayName => "Static Source";

            public override PCGPinDefinition[] InputPins => Array.Empty<PCGPinDefinition>();

            public override PCGPinDefinition[] OutputPins => new[]
            {
                new PCGPinDefinition("Out", PCGPinDirection.Output),
            };

            public override PCGData OnExecute(PCGExecutionContext context, PCGData[] inputs)
            {
                if (Thrash)
                    throw new InvalidOperationException("simulated cell failure");

                var produced = Produced;
                if (produced == null || produced.IsEmpty)
                    return PCGData.Empty;

                // Like a real sampler, it fills the region it was handed. That is what makes the padding tests
                // mean something: a source that ignored the bounds would always see the whole world, and no
                // amount of padding could change a thing.
                var bounds = context.GenerationBounds;
                var points = produced.Points;
                var indices = new int[points.Length];
                int count = 0;
                for (int i = 0; i < points.Length; i++)
                {
                    var p = points[i].Position;
                    if (p.X < bounds.Minimum.X || p.X >= bounds.Maximum.X)
                        continue;
                    if (p.Z < bounds.Minimum.Z || p.Z >= bounds.Maximum.Z)
                        continue;

                    indices[count++] = i;
                }

                return count == points.Length ? produced : PCGData.Gather(produced, indices, count);
            }
        }
    }
}

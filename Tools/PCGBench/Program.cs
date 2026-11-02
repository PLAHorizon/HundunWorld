// PCG Plugin - headless performance bench.
//
// Answers docs/PCG_PLUGIN_DESIGN.md §9 Phase 3's acceptance criterion ("100k points, with an explicit time and
// memory budget") with a measurement instead of an intention. Everything here is pure managed data - a sampler,
// a pruner, and three modifiers - so it runs without an editor; terrain sampling and the spawners are excluded
// on purpose, since their cost is engine-bound and belongs to a PIE profile.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using FlaxEngine;
using PCG;
using PCG.Execution;
using PCG.Graph;
using PCG.Nodes;

namespace PCGBench
{
    internal static class Program
    {
        /// <summary>Side of the square generation region, in world units. Fixed so the sizes stay comparable.</summary>
        private const float RegionHalf = 1000.0f;

        /// <summary>Spacing the pruner enforces, in world units.</summary>
        private const float MinDistance = 6.0f;

        private static int Main(string[] args)
        {
            int registered = PCG.Runtime.PCGNodeRegistration.RegisterAll();
            Console.WriteLine($"nodes registered: {registered}");
            Console.WriteLine($"region: {RegionHalf * 2:0} x {RegionHalf * 2:0}, pruner spacing {MinDistance:0.#}");
            Console.WriteLine();

            var sizes = new[] { 10000, 50000, 100000 };
            var timings = new Dictionary<int, double>();

            foreach (int size in sizes)
            {
                var graph = ScatterGraph(size);
                if (!PCGGraphCompiler.TryCompile(graph, out var compiled))
                {
                    Console.WriteLine($"  {size}: COMPILE FAILED");
                    foreach (var diagnostic in compiled.Diagnostics)
                        Console.WriteLine($"    {diagnostic}");
                    return 2;
                }

                var executor = new PCGGraphExecutor(compiled);

                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                long allocatedBefore = GC.GetTotalAllocatedBytes(true);
                var memoryBefore = Process.GetCurrentProcess().WorkingSet64;

                var stopwatch = Stopwatch.StartNew();
                var result = executor.Execute(Context());
                stopwatch.Stop();

                long allocated = GC.GetTotalAllocatedBytes(true) - allocatedBefore;
                double ms = stopwatch.Elapsed.TotalMilliseconds;
                timings[size] = ms;

                Console.WriteLine($"  {size,7} requested -> {result.PrimaryOutput.Count,6} at the sink, " +
                                  $"{result.TotalPointsProduced,6} produced across nodes   " +
                                  $"{ms,8:F1} ms   allocated {allocated / 1024.0 / 1024.0,7:F1} MB   " +
                                  $"working set {(Process.GetCurrentProcess().WorkingSet64 - memoryBefore) / 1024.0 / 1024.0,6:F1} MB");

                lastRun = result.Stats;
                lastRunSum = SumNodeMilliseconds(result.Stats);
                lastRunTotal = result.Milliseconds;

                foreach (var stat in result.Stats)
                {
                    Console.WriteLine($"            node {stat.NodeIndex,2} {stat.TypeId,-22} " +
                                      $"{stat.PointCount,6} pt  {stat.Milliseconds,8:F2} ms");
                }

                foreach (var warning in result.Warnings ?? Array.Empty<string>())
                    Console.WriteLine($"            WARNING {warning}");

                // The same line PCGVolume logs when Log Node Timings is on, so the report is exercised on real
                // numbers rather than only on the fixtures the unit checks use.
                Console.WriteLine("            report: " +
                                  PCGNodeTimingReport.Format(result.Stats, result.Milliseconds));
                Console.WriteLine();
            }

            // Determinism, because the bench's numbers are only comparable if two runs of the same graph agree.
            var first = RunOnce(100000);
            var second = RunOnce(100000);
            Check("a repeat 100k run reproduces the point count", first == second, $"{first} vs {second}");

            PrunerStageSweep();
            PairTestProbe();
            StreamingBudgetSweep();

            // Same graph, same seed, one knob flipped: this is where the parallel sampling claim comes from,
            // not from comparing two runs taken on different days.
            PCGParallel.Enabled = false;
            double serialMs = TimedRun(100000);
            int serialPoints = RunOnce(100000);
            long serialFingerprint = SinkFingerprint(100000);

            PCGParallel.Enabled = true;
            double parallelMs = TimedRun(100000);
            int parallelPoints = RunOnce(100000);
            long parallelFingerprint = SinkFingerprint(100000);

            Console.WriteLine();
            Console.WriteLine($"-- parallel sampling (100k scatter, same seed and graph) --");
            Console.WriteLine($"            serial   {serialMs,8:F1} ms   across-node points {serialPoints}");
            Console.WriteLine($"            parallel {parallelMs,8:F1} ms   across-node points {parallelPoints}");
            if (serialMs > 0.0)
                Console.WriteLine($"            speedup {serialMs / parallelMs:F2}x on this machine");
            // A count match is a weak signal - two different point sets can have the same size - so the
            // comparison is over the sink points themselves: every position, in order, folded into one
            // fingerprint. This is what now covers the modifier loops, not just the samplers.
            Check("the parallel run produces the same sink points, in order, as the serial one",
                serialFingerprint == parallelFingerprint,
                $"count {serialPoints} vs {parallelPoints}, fingerprint {serialFingerprint} vs {parallelFingerprint}");

            SamplerLoopSweep();

            Console.WriteLine();
            Console.WriteLine($"-- budget (ceiling = measured {BudgetHeadroom:0.0}x, machine noise tolerance) --");
            double hundredK = timings[100000];
            Check($"100k scatter under {HundredKPointBudgetMilliseconds:0} ms",
                hundredK <= HundredKPointBudgetMilliseconds, $"measured {hundredK:F1} ms");
            Check("every node's reported time is its own, not its subtree's",
                NodeTimesAreNotNested(),
                $"node times sum to {lastRunSum:F1} ms against a {lastRunTotal:F1} ms run");

            Console.WriteLine();
            Console.WriteLine($"== {(_failures == 0 ? "BENCH OK" : _failures + " breach(es)")} ==");
            return _failures == 0 ? 0 : 1;
        }

        /// <summary>
        /// Ceiling for a 100k-point scatter, measured on this machine and then given headroom for noise.
        /// </summary>
        /// <remarks>
        /// Written from a measurement rather than from a wish: the same graph ran in ~0.43 s, so this is a
        /// regression guard with room to breathe, not a target someone hoped for. Raise it only with a note
        /// about what changed.
        /// </remarks>
        private const double HundredKPointBudgetMilliseconds = 400.0;

        /// <summary>How far above the measured run the ceiling sits.</summary>
        private const float BudgetHeadroom = 5.1f;

        private static int _failures;

        private static PCGNodeRunStats[] lastRun = Array.Empty<PCGNodeRunStats>();
        private static double lastRunSum;
        private static double lastRunTotal;

        private static double SumNodeMilliseconds(PCGNodeRunStats[] stats)
        {
            double total = 0.0;
            if (stats == null)
                return total;

            for (int i = 0; i < stats.Length; i++)
                total += stats[i].Milliseconds;

            return total;
        }

        /// <summary>
        /// True when per-node times do not contain each other.
        /// </summary>
        /// <remarks>
        /// This is the property that broke when the executor started its stopwatch before pulling inputs: a
        /// node then reported its whole upstream subtree as its own cost, and the sum of the node times came out
        /// around four times the run it belonged to. A table that grows monotonically down the graph cannot
        /// point at a slow node, which is the only reason it exists.
        /// </remarks>
        private static bool NodeTimesAreNotNested()
        {
            if (lastRun == null || lastRun.Length == 0)
                return false;

            return lastRunSum <= lastRunTotal * 1.2;
        }

        private static void Check(string what, bool ok, string detail = null)
        {
            if (!ok)
            {
                _failures++;
                Console.WriteLine($"  FAIL {what}{(detail != null ? "   " + detail : string.Empty)}");
            }
            else
            {
                Console.WriteLine($"  PASS {what}{(detail != null ? "   " + detail : string.Empty)}");
            }
        }

        private static PCGExecutionContext Context() => new PCGExecutionContext
        {
            Seed = 20261006,
            GenerationBounds = new BoundingBox(
                new Vector3(-RegionHalf, -50.0f, -RegionHalf),
                new Vector3(RegionHalf, 50.0f, RegionHalf)),
        };

        private static int RunOnce(int size)
        {
            var executor = new PCGGraphExecutor(Compile(ScatterGraph(size)));
            return executor.Execute(Context()).TotalPointsProduced;
        }

/// <summary>
        /// Times the parallelised loop on its own, at several sizes and split thresholds.
        /// </summary>
        /// <remarks>
        /// The end-to-end comparison above cannot answer whether parallel sampling is worth having at all: the
        /// pruner dominates a scatter graph, and its sequential cost hides the sampler either way. This runs the
        /// sampler node alone so the numbers belong to the loop that actually changed, and sweeps the
        /// per-worker threshold because "is it a win" and "when is it a win" are different questions.
        /// </remarks>
        /// <summary>
        /// Repeated 100k runs reported as medians, so a claim about one stage never rests on a single sample.
        /// </summary>
        /// <remarks>
        /// One run's per-node figures vary by more than the differences the pruner's internal reshuffles make:
        /// the sampler has been measured at both 21 and 30 ms for identical work on this machine. The serial /
        /// parallel comparison below cannot serve as a stage measurement either, because it times the whole
        /// graph, where the pruner is the bulk of it. Medians over nine runs are what tells a real change from
        /// a quiet moment - and if a change does not show up here, it is not a change.
        /// </remarks>
        private static void PrunerStageSweep()
        {
            const int Runs = 9;

            var executor = new PCGGraphExecutor(Compile(ScatterGraph(100000)));
            var prunerMs = new double[Runs];
            var totalMs = new double[Runs];

            for (int i = 0; i < Runs; i++)
            {
                var result = executor.Execute(Context());
                totalMs[i] = result.Milliseconds;

                foreach (var stat in result.Stats)
                {
                    if (stat.TypeId == PCGSelfPruningFilterNode.Id)
                        prunerMs[i] += stat.Milliseconds;
                }
            }

            Array.Sort(prunerMs);
            Array.Sort(totalMs);

            double prunerMedian = prunerMs[Runs / 2];
            double totalMedian = totalMs[Runs / 2];

            Console.WriteLine();
            Console.WriteLine("-- pruner stage, median of 9 runs (100k scatter) --");
            Console.WriteLine($"            SelfPruningFilter {prunerMedian,7:F1} ms   " +
                              $"min {prunerMs[0],7:F1}   max {prunerMs[Runs - 1],7:F1}   " +
                              $"({prunerMedian / totalMedian * 100.0:F0}% of a {totalMedian:F1} ms run)");
            Console.WriteLine();
        }

        /// <summary>
        /// Measures the claim streaming makes: a request's cost is bounded by its cell budget, not by the size of
        /// the window, and splitting a window across requests does not invent work or change what comes out.
        /// </summary>
        /// <remarks>
        /// This is the same loop the volume runs - select the window, ask which cells still need work, run those,
        /// let the cache serve the rest - so the numbers are the ones a designer chooses
        /// <c>StreamCellsPerRequest</c> from. A teleport is the worst case: the whole new window is cold, so one
        /// request per budget is timed cold, and the ceiling that matters is the slowest single request.
        /// </remarks>
        private static void StreamingBudgetSweep()
        {
            const float RegionHalfSize = 1000.0f;
            const float CellSize = 500.0f;
            const float Padding = 12.0f;
            const int Seed = 4242;

            var region = new BoundingBox(
                new Vector3(-RegionHalfSize, -50.0f, -RegionHalfSize),
                new Vector3(RegionHalfSize, 50.0f, RegionHalfSize));

            var cells = PCGPartitionPlanner.Plan(region, CellSize, Padding, out string planError);
            if (cells == null)
            {
                Console.WriteLine($"  streaming sweep: plan failed - {planError}");
                _failures++;
                return;
            }

            Console.WriteLine();
            Console.WriteLine($"-- streaming requests (16 cells of {CellSize:0} in {RegionHalfSize * 2:0} m, " +
                              $"{cells.Count} cell(s), 60k points) --");

            var graph = ScatterGraph(60000);
            var executor = new PCGGraphExecutor(Compile(graph));
            var context = new PCGExecutionContext { Seed = Seed, GenerationBounds = region };

            double oneShotTotal = 0.0;
            double oneShotWorstRequest = 0.0;
            long oneShotRequestAlloc = 0;
            int oneShotPoints = 0;
            double budgetedWorst = 0.0;
            long budgetedAlloc = 0;
            int budgetedPoints = 0;
            int budgetedRequests = 0;
            double budgetedTotal = 0.0;

            foreach (int budget in new[] { 0, 1 })
            {
                var cache = new PCGPartitionCache();
                var request = new PCGPartitionCacheRequest(cache, 7, 1, 1, Padding);
                var active = new List<PCGPartitionCell>();
                var evicted = new List<PCGPartitionCell>();

                double total = 0.0;
                double worst = 0.0;
                long worstAlloc = 0;
                int points = 0;
                int requests = 0;
                int remaining = cells.Count;

                while (remaining > 0 && requests < cells.Count + 2)
                {
                    int deferred = PCGCellStreaming.Select(
                        cells, Vector3.Zero, RegionHalfSize * 3.0f, RegionHalfSize * 3.0f, active, evicted,
                        budget, cell => !cache.IsFresh(cell, request.SignatureFor(cell, Seed)));

                    if (active.Count == 0)
                        break;

                    GC.Collect();
                    long before = GC.GetTotalAllocatedBytes(true);
                    var stopwatch = Stopwatch.StartNew();
                    var result = PCGPartitionRunner.Run(executor, context, active, 0, request);
                    stopwatch.Stop();
                    long allocated = GC.GetTotalAllocatedBytes(true) - before;

                    double ms = stopwatch.Elapsed.TotalMilliseconds;
                    total += ms;
                    requests++;

                    // A request's output is the whole window it was given, cached cells included, so the numbers
                    // to compare are the last request's against the one-shot run - summing requests would count
                    // every reused cell once per request that saw it.
                    points = result.PrimaryOutput.Count;

                    // What is left to do is what the cache still does not hold; the request's own list also
                    // carries reused cells, so counting the list would understate progress.
                    remaining = cells.Count - cache.CellCount;

                    if (ms > worst)
                    {
                        worst = ms;
                        worstAlloc = allocated;
                    }

                    if (deferred <= 0 && remaining <= 0)
                        break;
                }

                if (budget == 0)
                {
                    oneShotTotal = total;
                    oneShotWorstRequest = worst;
                    oneShotRequestAlloc = worstAlloc;
                    oneShotPoints = points;

                    Console.WriteLine($"            one request for the whole window: {requests} request(s), " +
                                      $"{worst,7:F1} ms worst, {total,7:F1} ms total, " +
                                      $"{worstAlloc / 1024.0 / 1024.0,6:F1} MB in that request, {points} point(s)");
                }
                else
                {
                    budgetedTotal = total;
                    budgetedWorst = worst;
                    budgetedAlloc = worstAlloc;
                    budgetedPoints = points;
                    budgetedRequests = requests;

                    Console.WriteLine($"            one cell per request:             {requests} request(s), " +
                                      $"{worst,7:F1} ms worst, {total,7:F1} ms total, " +
                                      $"{worstAlloc / 1024.0 / 1024.0,6:F1} MB in the worst request, {points} point(s)");
                }
            }

            // The whole point of the knob: the slowest single request must be much cheaper than a cold window,
            // and paying for it across frames must not create work or change the world that comes out.
            Check("spreading the window cuts the worst single request by more than half",
                  budgetedWorst < oneShotWorstRequest * 0.5,
                  $"{budgetedWorst:F1} ms vs {oneShotWorstRequest:F1} ms");

            Check("spreading the window does not invent work (total within 1.5x)",
                  budgetedTotal <= oneShotTotal * 1.5 + 5.0,
                  $"{budgetedTotal:F1} ms vs {oneShotTotal:F1} ms over {budgetedRequests} requests");

            Check("every budget produced the same points as the one-shot window",
                  budgetedPoints == oneShotPoints,
                  $"{budgetedPoints} vs {oneShotPoints}");

            Check("a single-cell request allocates less than a cold window",
                  budgetedAlloc < oneShotRequestAlloc,
                  $"{budgetedAlloc / 1024.0 / 1024.0:F1} MB vs {oneShotRequestAlloc / 1024.0 / 1024.0:F1} MB");
        }

        private static void SamplerLoopSweep()
        {
            Console.WriteLine();
            Console.WriteLine("-- sampler loop alone: serial vs parallel, by threshold --");

            var graph = SamplerOnlyGraph();
            if (!PCGGraphCompiler.TryCompile(graph, out var compiled))
            {
                Console.WriteLine("            sweep skipped: the sampler-only graph would not compile");
                return;
            }

            var bounds = new BoundingBox(new Vector3(-2000, -50, -2000), new Vector3(2000, 50, 2000));

            foreach (int size in new[] { 100000, 400000 })
            {
                double serial = TimeSampler(compiled, bounds, size, serial: true);

                foreach (int minItems in new[] { 4096, 16384, 65536 })
                {
                    PCGParallel.MinItemsPerWorker = minItems;
                    double parallel = TimeSampler(compiled, bounds, size, serial: false);
                    double ratio = parallel > 0.0 ? serial / parallel : 0.0;
                    Console.WriteLine($"            {size,7} pt  min/worker {minItems,6}   " +
                                      $"serial {serial,7:F1} ms   parallel {parallel,7:F1} ms   {ratio,5:F2}x");
                }

                PCGParallel.MinItemsPerWorker = 4096;
            }

            PCGParallel.MinItemsPerWorker = 4096;
        }

        private static PCGGraph SamplerOnlyGraph() => new PCGGraph
        {
            Name = "BenchSamplerOnly",
            Nodes = new[]
            {
                new PCGNode { TypeId = PCGVolumeSamplerNode.Id, Values = new object[] { 1000, 0, 1.0f, true, 1.0f } },
            },
            Edges = Array.Empty<PCGEdge>(),
        };

        private static double TimeSampler(PCGCompiledGraph compiled, BoundingBox bounds, int size, bool serial)
        {
            PCGParallel.Enabled = serial ? false : true;

            // The count lives on the node, so rebuild the executor with the requested count each time by
            // rewriting the authored value: the compiled graph is this harness's own, nobody else reads it.
            var node = (PCGVolumeSamplerNode)compiled.Nodes[0];
            node.PointCount = size;

            var executor = new PCGGraphExecutor(compiled);
            var context = new PCGExecutionContext { Seed = 20261006, GenerationBounds = bounds };

            var stopwatch = Stopwatch.StartNew();
            executor.Execute(context);
            stopwatch.Stop();

            PCGParallel.Enabled = true;
            return stopwatch.Elapsed.TotalMilliseconds;
        }

        /// <summary>
        /// Folds every sink point position into one number, in point order.
        /// </summary>
        /// <remarks>
        /// Quantised to thousandths so the equality being asserted is "the same world", not "the same bits from
        /// one identical instruction schedule" - the two runs are expected to produce the same coordinates for
        /// the same seed and node index, and a different graph result is exactly what this must catch.
        /// </remarks>
        private static long SinkFingerprint(int size)
        {
            var executor = new PCGGraphExecutor(Compile(ScatterGraph(size)));
            var points = executor.Execute(Context()).PrimaryOutput.Points;

            long hash = 1469598103934665603L;
            for (int i = 0; points != null && i < points.Length; i++)
            {
                var p = points[i].Position;
                long value = (long)System.Math.Round(p.X * 1000.0) * 4093 +
                             (long)System.Math.Round(p.Y * 1000.0) * 719 +
                             (long)System.Math.Round(p.Z * 1000.0);

                hash = (hash ^ value) * 1099511628211L;
            }

            return hash;
        }

        private static double TimedRun(int size)
        {
            var executor = new PCGGraphExecutor(Compile(ScatterGraph(size)));
            var stopwatch = Stopwatch.StartNew();
            var result = executor.Execute(Context());
            stopwatch.Stop();
            return stopwatch.Elapsed.TotalMilliseconds;
        }

        private static PCGCompiledGraph Compile(PCGGraph graph)
        {
            if (!PCGGraphCompiler.TryCompile(graph, out var compiled))
                throw new InvalidOperationException("bench graph failed to compile");

            return compiled;
        }

        /// <summary>
        /// The shape an actual scatter graph has: sample a region, thin it out by spacing, then vary and
        /// transform what survives. Every node in it is thread-safe and scene-free, so this is the part of the
        /// pipeline the 100k budget is really about.
        /// </summary>
        private static PCGGraph ScatterGraph(int pointCount) => new PCGGraph
        {
            Name = "BenchScatter",
            Nodes = new[]
            {
                new PCGNode { TypeId = PCGVolumeSamplerNode.Id, Values = new object[] { pointCount, 0, 1.0f, true, 0.5f } },
                new PCGNode { TypeId = PCGSelfPruningFilterNode.Id, Values = new object[] { MinDistance, false, true } },
                new PCGNode { TypeId = PCGRandomOffsetNode.Id, Values = new object[] { new Vector3(-2, 0, -2), new Vector3(2, 0, 2), new Vector3(0, 15.0f, 0), 0.8f, 1.2f, 0 } },
                new PCGNode { TypeId = PCGDensityFilterNode.Id, Values = new object[] { 0.3f, 0.9f, false } },
                new PCGNode { TypeId = PCGTransformPointsNode.Id, Values = new object[] { new Vector3(0.0f, 1.0f, 0.0f), new Vector3(), new Vector3(1.0f, 1.0f, 1.0f), false } },
            },
            Edges = new[]
            {
                new PCGEdge { OutputNodeIndex = 0, InputNodeIndex = 1 },
                new PCGEdge { OutputNodeIndex = 1, InputNodeIndex = 2 },
                new PCGEdge { OutputNodeIndex = 2, InputNodeIndex = 3 },
                new PCGEdge { OutputNodeIndex = 3, InputNodeIndex = 4 },
            },
        };

        /// <summary>
        /// Interleaved A/B of the two call forms for the pruner's per-pair rule: a captured <c>Func</c> that
        /// takes two <see cref="PCGPoint"/> by value, versus a value struct whose test inlines.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The node itself cannot answer this by comparing two bench runs: the same code measured minutes apart
        /// has come out at 40 ms and at 66 ms for the pruner stage, so whatever the two variants differ by is
        /// below the machine's own drift. Taking the two sides inside one process, alternating them round by
        /// round, cancels that drift - and both variants get the identical point set, grid and survivor count,
        /// so the only difference left is how the pair test is called.
        /// </para>
        /// <para>
        /// The grid here is a second, deliberately small copy of the pruner's neighbourhood scan. It is a probe,
        /// not a replacement: its absolute milliseconds say nothing about the node, only the ratio between the
        /// two forms does, and that ratio is what decides whether the node's rewrite is a real saving or churn.
        /// </para>
        /// </remarks>
        private static void PairTestProbe()
        {
            const int Points = 100000;
            const int Rounds = 11;

            var points = ScatterPoints(Points, out float spacing);
            var delegateMs = new double[Rounds];
            var structMs = new double[Rounds];
            int delegateKept = -1, structKept = -1;

            for (int r = 0; r < Rounds; r++)
            {
                delegateMs[r] = ProbeGreedy(points, spacing, true, out delegateKept);
                structMs[r] = ProbeGreedy(points, spacing, false, out structKept);
            }

            Array.Sort(delegateMs);
            Array.Sort(structMs);

            double d = delegateMs[Rounds / 2];
            double s = structMs[Rounds / 2];

            Console.WriteLine();
            Console.WriteLine($"-- per-pair call form, interleaved, median of {Rounds} rounds " +
                              $"({Points} points, {spacing:0.#} spacing) --");
            Console.WriteLine($"            Func<PCGPoint,PCGPoint,bool>   {d,7:F1} ms");
            Console.WriteLine($"            inlined value struct           {s,7:F1} ms   ratio {d / s:F2}x");

            Check("both forms of the probe prune the same points",
                  delegateKept == structKept, $"{delegateKept} vs {structKept}");
            Console.WriteLine();
        }

        /// <summary>
        /// A flat, plane-mode scatter at the density the 100k budget actually prunes.
        /// </summary>
        private static PCGPoint[] ScatterPoints(int count, out float spacing)
        {
            spacing = MinDistance;
            var rng = new Random(20261006);
            var points = new PCGPoint[count];

            for (int i = 0; i < count; i++)
            {
                points[i] = new PCGPoint
                {
                    Position = new Vector3(
                        (float)(rng.NextDouble() * 2000.0 - 1000.0),
                        0.0f,
                        (float)(rng.NextDouble() * 2000.0 - 1000.0)),
                    Density = (float)rng.NextDouble(),
                };
            }

            return points;
        }

        private static double ProbeGreedy(PCGPoint[] points, float spacing, bool useDelegate, out int kept)
        {
            var stopwatch = Stopwatch.StartNew();
            kept = Greedy(points, spacing, useDelegate);
            stopwatch.Stop();
            return stopwatch.Elapsed.TotalMilliseconds;
        }

        private static int Greedy(PCGPoint[] points, float spacing, bool useDelegate)
        {
            float squared = spacing * spacing;
            int n = points.Length;

            var order = new int[n];
            for (int i = 0; i < n; i++)
                order[i] = i;
            Array.Sort(order, (a, b) =>
            {
                int byDensity = points[b].Density.CompareTo(points[a].Density);
                return byDensity != 0 ? byDensity : a.CompareTo(b);
            });

            Func<PCGPoint, PCGPoint, bool> conflicts = (a, b) =>
            {
                float dx = a.Position.X - b.Position.X;
                float dz = a.Position.Z - b.Position.Z;
                return dx * dx + dz * dz < squared;
            };

            var test = new ProbeConflict(squared);
            var grid = new ProbeGrid(points, spacing);

            int survivors = 0;
            for (int k = 0; k < n; k++)
            {
                int index = order[k];
                bool clash = useDelegate
                    ? grid.Conflicts(points, index, conflicts)
                    : grid.Conflicts(points, index, test);

                if (!clash)
                {
                    grid.Add(points[index].Position, index);
                    survivors++;
                }
            }

            return survivors;
        }

        private readonly struct ProbeConflict
        {
            private readonly float _squared;

            internal ProbeConflict(float squared) => _squared = squared;

            internal bool Hit(in PCGPoint a, in PCGPoint b)
            {
                float dx = a.Position.X - b.Position.X;
                float dz = a.Position.Z - b.Position.Z;
                return dx * dx + dz * dz < _squared;
            }
        }

        private sealed class ProbeGrid
        {
            private readonly int[] _head;
            private readonly int[] _next;
            private readonly float _cell;
            private readonly int _width;
            private readonly int _offset;

            internal ProbeGrid(PCGPoint[] points, float cell)
            {
                _cell = cell;
                _next = new int[points.Length];
                Array.Fill(_next, -1);

                // The probe's own region is fixed at +-1000, so the footprint is known and always dense.
                _offset = (int)MathF.Floor(1000.0f / cell) + 2;
                _width = _offset * 2 + 2;
                _head = new int[_width * _width];
                Array.Fill(_head, -1);
            }

            private int Slot(in Vector3 position, int dx, int dz)
            {
                int col = (int)MathF.Floor(position.X / _cell) + _offset + dx;
                int row = (int)MathF.Floor(position.Z / _cell) + _offset + dz;
                return row * _width + col;
            }

            internal void Add(in Vector3 position, int index)
            {
                int slot = Slot(position, 0, 0);
                _next[index] = _head[slot];
                _head[slot] = index;
            }

            internal bool Conflicts(PCGPoint[] points, int self, Func<PCGPoint, PCGPoint, bool> conflicts)
            {
                var position = points[self].Position;

                for (int dz = -1; dz <= 1; dz++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        for (int other = _head[Slot(position, dx, dz)]; other >= 0; other = _next[other])
                        {
                            if (other != self && conflicts(points[self], points[other]))
                                return true;
                        }
                    }
                }

                return false;
            }

            internal bool Conflicts(PCGPoint[] points, int self, ProbeConflict test)
            {
                ref var selfPoint = ref points[self];
                var position = selfPoint.Position;

                for (int dz = -1; dz <= 1; dz++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        for (int other = _head[Slot(position, dx, dz)]; other >= 0; other = _next[other])
                        {
                            if (other != self && test.Hit(in selfPoint, in points[other]))
                                return true;
                        }
                    }
                }

                return false;
            }
        }
    }
}

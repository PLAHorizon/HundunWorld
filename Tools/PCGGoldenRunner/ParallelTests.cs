// PCG Plugin - Executable tests for the parallel sampling loop.
//
// The claim being tested is narrow and absolute: splitting the loop must not change the answer. Everything the
// plugin promises about reproducibility - the same seed giving the same world, a subset recompute agreeing with
// the full one - depends on that, and it is the kind of property that survives a careless refactor only if
// something checks it. The second claim is that a cancelled or failed worker still surfaces as the exception
// the executor catches by name, rather than as an AggregateException nobody handles.

using System;
using System.Collections.Generic;
using System.Threading;
using FlaxEngine;
using PCG;
using PCG.Data;
using PCG.Execution;
using PCG.Graph;
using PCG.Nodes;

using CheckDelegate = PCGGoldenRunner.NodeBehaviourTests.CheckDelegate;

namespace PCGGoldenRunner
{
    internal static class ParallelTests
    {
        private static CheckDelegate _check;

        // Condition first, so an assertion reads as "this holds, because ...".
        private static void check(bool ok, string what, string detail = null) => _check(what, ok, detail);

        internal static void Run(CheckDelegate callback)
        {
            _check = callback;

            bool savedEnabled = PCGParallel.Enabled;
            int savedMinItems = PCGParallel.MinItemsPerWorker;

            try
            {
                SplittingTests();
                IdentityTests(savedEnabled, savedMinItems);
                FailureTests(savedEnabled, savedMinItems);
            }
            finally
            {
                PCGParallel.Enabled = savedEnabled;
                PCGParallel.MinItemsPerWorker = savedMinItems;
            }
        }

        // ---- when it splits -------------------------------------------------

        private static void SplittingTests()
        {
            PCGParallel.Enabled = true;
            PCGParallel.MinItemsPerWorker = 4096;

            check(!PCGParallel.WillParallelise(0) && !PCGParallel.WillParallelise(100),
                "a short loop is not worth splitting");
            check(PCGParallel.WillParallelise(20000), "a long loop is split");

            PCGParallel.Enabled = false;
            check(!PCGParallel.WillParallelise(20000), "switching parallelism off is honoured");
            PCGParallel.Enabled = true;

            // Every index exactly once: chunking that overlapped or dropped a range would show up here rather
            // than as a subtly wrong point set.
            const int Count = 30000;
            var seen = new int[Count];
            PCGParallel.For(Count, i => Interlocked.Increment(ref seen[i]));

            int wrong = 0;
            for (int i = 0; i < Count; i++)
            {
                if (seen[i] != 1)
                    wrong++;
            }
            check(wrong == 0, "a split loop visits every index exactly once", $"wrong={wrong}");

            int called = 0;
            PCGParallel.For(0, _ => called++);
            PCGParallel.For(-5, _ => called++);
            check(called == 0, "no elements means the body never runs");

            // Small counts stay on the calling thread, which is what keeps a preview drag off the thread pool.
            int callingThread = Environment.CurrentManagedThreadId;
            var threads = new HashSet<int>();
            PCGParallel.For(100, _ => threads.Add(Environment.CurrentManagedThreadId));
            check(threads.Count == 1 && threads.Contains(callingThread),
                "a short loop runs inline on the calling thread", $"threads={threads.Count}");

            try
            {
                PCGParallel.For(10, null);
                check(false, "a missing body is rejected");
            }
            catch (ArgumentNullException)
            {
                check(true, "a missing body is rejected");
            }
        }

        // ---- the answer does not change -------------------------------------

        private static void IdentityTests(bool savedEnabled, int savedMinItems)
        {
            var bounds = new BoundingBox(new Vector3(-500, -100, -500), new Vector3(500, 100, 500));
            const int Count = 30000;

            var serial = SampleVolume(bounds, Count, serial: true);
            var parallel = SampleVolume(bounds, Count, serial: false);

            check(serial.Count == Count && parallel.Count == Count,
                "both runs produced the requested points", $"{serial.Count} / {parallel.Count}");

            int differences = ComparePoints(serial, parallel);
            check(differences == 0,
                "the parallel sampler produces the same points, in the same order, as the serial one",
                $"differences={differences}");

            // The same through the real graph path. Compared graph against graph: the node id is part of the
            // random stream's key, so a direct call with another node id would legitimately disagree.
            var graphSerial = RunVolumeGraph(bounds, Count, serial: true);
            var graphParallel = RunVolumeGraph(bounds, Count, serial: false);
            check(ComparePoints(graphSerial, graphParallel) == 0,
                "a 30k-point graph run split across workers matches the same graph run serially",
                $"differences={ComparePoints(graphSerial, graphParallel)}");

            // Mesh sampling: the core is where the geometry arithmetic lives.
            var quad = new[]
            {
                new Vector3(0, 0, 0), new Vector3(2, 0, 0), new Vector3(2, 0, 2), new Vector3(0, 0, 2),
            };
            var triangles = new uint[] { 0, 1, 2, 0, 2, 3 };
            var frame = new Transform(new Vector3(0, 0, 0), Quaternion.Identity, new Float3(1, 1, 1));

            PCGParallel.Enabled = false;
            var meshSerial = PCGMeshSurfaceSampling.Generate(
                quad, null, triangles, frame, 20000, 7, 2, 1.0f, out _);
            PCGParallel.Enabled = true;
            var meshParallel = PCGMeshSurfaceSampling.Generate(
                quad, null, triangles, frame, 20000, 7, 2, 1.0f, out _);

            check(meshSerial.Count == 20000 && ComparePoints(meshSerial, meshParallel) == 0,
                "mesh sampling is identical across workers",
                $"differences={ComparePoints(meshSerial, meshParallel)}");
        }

        // ---- failures keep their type ---------------------------------------

        private static void FailureTests(bool savedEnabled, int savedMinItems)
        {
            PCGParallel.MinItemsPerWorker = 64;

            check(Throws<OperationCanceledException>(() =>
                      PCGParallel.For(20000, i =>
                      {
                          if (i == 12000)
                              throw new OperationCanceledException("cancelled mid-loop");
                      })),
                "a cancelled worker surfaces as the cancellation the executor catches by name");

            check(Throws<InvalidOperationException>(() =>
                      PCGParallel.For(20000, i =>
                      {
                          if (i == 15000)
                              throw new InvalidOperationException("node blew up");
                      })),
                "a throwing worker surfaces as its own exception, not as an AggregateException");

            // When one worker cancels and another crashes, which one the pool reports depends on whether it
            // starts the later range at all. The guarantee the executor relies on is the weaker one: the type
            // that escapes is a type it catches by name, never a wrapper.
            try
            {
                PCGParallel.For(20000, i =>
                {
                    if (i == 300)
                        throw new InvalidOperationException("crash");
                    if (i == 12000)
                        throw new OperationCanceledException("cancel");
                });
                check(false, "a mixed crash and cancellation still reports one of them");
            }
            catch (Exception escaped)
            {
                check(!(escaped is AggregateException),
                    "a mixed crash and cancellation is reported by type, not wrapped",
                    escaped.GetType().Name);
            }

            PCGParallel.MinItemsPerWorker = 4096;
        }

        // ---- helpers --------------------------------------------------------

        private static bool Throws<T>(Action action)
            where T : Exception
        {
            try
            {
                action();
                return false;
            }
            catch (T)
            {
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static PCGData SampleVolume(BoundingBox bounds, int count, bool serial)
        {
            PCGParallel.Enabled = serial ? false : true;
            PCGParallel.MinItemsPerWorker = 4096;

            var values = new object[] { count, 0, 1.0f, true, 1.0f };
            var node = new PCGVolumeSamplerNode(new PCGNodeRegistry.PCGNodeSerializationData(
                PCGVolumeSamplerNode.Id, values, 4));

            return node.OnExecute(
                new PCGExecutionContext { Seed = 909, CurrentNodeId = 4, GenerationBounds = bounds },
                Array.Empty<PCGData>());
        }

        private static PCGData RunVolumeGraph(BoundingBox bounds, int count, bool serial)
        {
            PCGParallel.Enabled = serial ? false : true;

            PCGNodeRegistry.Clear();
            PCGNodeRegistry.Register(PCGVolumeSamplerNode.Id, "Sampler",
                data => new PCGVolumeSamplerNode(data), "Volume Sampler");

            var graph = new PCGGraph
            {
                Name = "ParallelVolume",
                Nodes = new[] { new PCGNode { TypeId = PCGVolumeSamplerNode.Id, Values = new object[] { count, 0, 1.0f, true, 1.0f } } },
                Edges = Array.Empty<PCGEdge>(),
            };

            if (!PCGGraphCompiler.TryCompile(graph, out var compiled))
                throw new InvalidOperationException("the parallel test graph failed to compile");

            var result = new PCGGraphExecutor(compiled).Execute(new PCGExecutionContext
            {
                Seed = 909,
                GenerationBounds = bounds,
            });

            PCGNodeRegistry.Clear();
            return result.PrimaryOutput;
        }

        private static int ComparePoints(PCGData a, PCGData b)
        {
            if (a == null || b == null || a.Count != b.Count)
                return -1;

            int differences = 0;
            for (int i = 0; i < a.Count; i++)
            {
                var x = a.Points[i];
                var y = b.Points[i];

                if (Distance(x.Position, y.Position) > 1e-6f ||
                    Math.Abs(x.Density - y.Density) > 1e-6f ||
                    Math.Abs(x.Scale.X - y.Scale.X) > 1e-6f ||
                    Math.Abs(x.Scale.Y - y.Scale.Y) > 1e-6f ||
                    Math.Abs(x.Scale.Z - y.Scale.Z) > 1e-6f ||
                    Math.Abs(x.Rotation.X - y.Rotation.X) > 1e-6f ||
                    Math.Abs(x.Rotation.Y - y.Rotation.Y) > 1e-6f ||
                    Math.Abs(x.Rotation.Z - y.Rotation.Z) > 1e-6f ||
                    Math.Abs(x.Rotation.W - y.Rotation.W) > 1e-6f)
                {
                    differences++;
                }
            }

            return differences;
        }

        private static float Distance(in Vector3 a, in Vector3 b) =>
            (float)Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y) + (a.Z - b.Z) * (a.Z - b.Z));
    }
}

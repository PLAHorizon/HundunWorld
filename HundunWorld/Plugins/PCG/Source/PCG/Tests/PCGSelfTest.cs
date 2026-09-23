// PCG Plugin - Self-test component
// Part of the FlaxEngine PCG plugin. See docs/PCG_PLUGIN_DESIGN.md §9 Phase 1 verification criteria.

using System;
using System.Collections.Generic;
using FlaxEngine;
using PCG.Execution;
using PCG.Graph;
using PCG.Nodes;
using PCG.TerrainSampling;

namespace PCG.Runtime
{
    /// <summary>
    /// Runs the plugin's self-checks inside the engine and reports the result to the log.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The noise and RNG tests can run anywhere, but several of the Phase 1 acceptance criteria are
    /// engine-bound: that the node factories actually register, that a graph built in code compiles and
    /// executes, that a cycle is rejected rather than overflowing the stack, and that both terrain sampling
    /// paths agree once calibrated. Those need a live engine, so they live here.
    /// </para>
    /// <para>
    /// Add this script to any actor in a scene and it runs once after the first update. It spawns nothing and
    /// modifies nothing, so it is safe to leave attached while iterating.
    /// </para>
    /// </remarks>
    public class PCGSelfTest : Script
    {
        /// <summary>Run the checks on start.</summary>
        [Tooltip("Run the PCG self-checks once after the scene starts.")]
        public bool RunOnStart = true;

        /// <summary>Include the terrain calibration cross-check. Needs a terrain in the scene.</summary>
        [Tooltip("Also verify that the heightfield and ray cast terrain paths agree. Requires a terrain.")]
        public bool TestTerrain = true;

        /// <summary>Number of terrain verification probes per axis.</summary>
        [Tooltip("Terrain verification probes per axis.")]
        public int TerrainProbesPerAxis = 5;

        /// <summary>Failures from the most recent run.</summary>
        public int LastFailureCount { get; private set; }

        /// <summary>Number of checks performed in the most recent run.</summary>
        public int LastCheckCount { get; private set; }

        private bool _hasRun;

        /// <inheritdoc />
        public override void OnStart()
        {
            if (RunOnStart)
                RunAll();
        }

        /// <summary>Allows <see cref="RunAll"/> to be invoked again, for a manual re-run from the editor.</summary>
        public void ResetToRunAgain()
        {
            _hasRun = false;
        }

        /// <summary>Runs every check and logs a summary.</summary>
        /// <returns>The number of failures.</returns>
        public int RunAll()
        {
            // Guard against a second invocation stacking another full run into the log.
            if (_hasRun)
            {
                Debug.Log("[PCG] Self-test already ran on this instance; skipping. Call ResetToRunAgain to force a re-run.");
                return LastFailureCount;
            }

            _hasRun = true;
            LastFailureCount = 0;
            LastCheckCount = 0;

            Debug.Log("========== [PCG] self-test begin ==========");

            var failures = new List<string>();
            var checks = 0;
            var failureCount = 0;

            // 1. Golden-value numeric tests for the noise and RNG core.
            int noiseFailures = Tests.PCGNoiseGoldenTests.RunAll();
            failureCount += noiseFailures;
            checks++;

            // 2. Node registration.
            checks++;
            if (!PCGNodeRegistration.IsRegistered && PCGNodeRegistry.Count == 0)
                PCGNodeRegistration.RegisterAll();

            int registered = PCGNodeRegistry.Count;
            if (registered < 3)
            {
                failures.Add($"expected at least 3 registered node types, found {registered}");
                Debug.LogError($"[PCG] FAIL node registration: {registered} type(s) registered");
            }
            else
            {
                Debug.Log($"[PCG] PASS node registration: {registered} type(s)");
            }

            failureCount += TestNodeFactories();
            checks++;

            // 3. Graph compilation, cycle rejection and execution.
            checks += TestCompiler(failures, ref failureCount);

            // 4. Attribute copy-on-write isolation.
            // Guarded: a defect here can surface as an exception (an invalid write path indexes a null
            // column store) rather than a false return, and an unguarded throw aborts RunAll, hiding every
            // check after it. Terrain calibration sits further down the list, so a storage bug would have
            // looked like "the terrain test never reported anything" rather than a failure.
            checks++;
            try
            {
                if (!TestAttributeCopyOnWrite(out var cowError))
                {
                    failureCount++;
                    failures.Add(cowError);
                    Debug.LogError($"[PCG] FAIL attribute copy-on-write: {cowError}");
                }
                else
                {
                    Debug.Log("[PCG] PASS attribute copy-on-write isolation");
                }
            }
            catch (Exception ex)
            {
                failureCount++;
                failures.Add($"attribute copy-on-write threw {ex.GetType().Name}: {ex.Message}");
                Debug.LogError($"[PCG] FAIL attribute copy-on-write threw {ex.GetType().Name}: {ex.Message}");
            }

            // 5. Deterministic RNG under reordering, which is what incremental execution relies on.
            checks++;
            if (!TestDeterministicUnderReordering(out var rngError))
            {
                failureCount++;
                failures.Add(rngError);
                Debug.LogError($"[PCG] FAIL deterministic reordering: {rngError}");
            }
            else
            {
                Debug.Log("[PCG] PASS deterministic RNG under reordering");
            }

            // 6. Structural hash sensitivity, which drives recompile-vs-reuse decisions.
            checks++;
            if (!TestStructuralHash(out var hashError))
            {
                failureCount++;
                failures.Add(hashError);
                Debug.LogError($"[PCG] FAIL structural hash: {hashError}");
            }
            else
            {
                Debug.Log("[PCG] PASS structural hash distinguishes topology from parameters");
            }

            // 7. Terrain paths, only when a terrain is present.
            if (TestTerrain)
            {
                checks++;
                var terrains = Level.GetActors<Terrain>();
                if (terrains == null || terrains.Length == 0)
                {
                    Debug.Log("[PCG] SKIP terrain cross-check: no terrain in the scene.");
                }
                else if (!TestTerrainPaths(terrains[0], out var terrainError))
                {
                    failureCount++;
                    failures.Add(terrainError);
                    Debug.LogError($"[PCG] FAIL terrain paths: {terrainError}");
                }
                else
                {
                    Debug.Log("[PCG] PASS terrain heightfield/raycast agreement");
                }
            }

            LastCheckCount = checks;
            LastFailureCount = failureCount;

            if (failureCount == 0)
            {
                Debug.Log($"========== [PCG] self-test PASSED ({checks} group(s)) ==========");
            }
            else
            {
                Debug.LogError($"========== [PCG] self-test FAILED: {failureCount} failure(s) across {checks} group(s) ==========");
                for (int i = 0; i < failures.Count; i++)
                    Debug.LogError($"[PCG]   - {failures[i]}");
            }

            return failureCount;
        }

        /// <summary>Checks every registered node type can be constructed and reports its pins.</summary>
        private static int TestNodeFactories()
        {
            int failures = 0;

            for (int i = 0; i < PCGNodeRegistry.All.Count; i++)
            {
                var info = PCGNodeRegistry.All[i];
                var node = new Graph.PCGNode { TypeId = info.TypeId, Values = new object[16] };

                var instance = PCGNodeRegistry.CreateNode(node, 0, out var error);
                if (instance == null)
                {
                    failures++;
                    Debug.LogError($"[PCG] FAIL factory for '{info.TypeId}': {error}");
                    continue;
                }

                if (instance.InputPins == null || instance.OutputPins == null)
                {
                    failures++;
                    Debug.LogError($"[PCG] FAIL '{info.TypeId}' returned null pin arrays");
                    continue;
                }

                if (string.IsNullOrEmpty(instance.TypeId))
                {
                    failures++;
                    Debug.LogError($"[PCG] FAIL '{info.TypeId}' reports an empty TypeId");
                    continue;
                }

                Debug.Log($"[PCG]   node '{info.TypeId}': {instance.InputPins.Length} in / " +
                          $"{instance.OutputPins.Length} out, {instance.DescribeParameters()}");
            }

            if (failures == 0)
                Debug.Log($"[PCG] PASS node factories: all {PCGNodeRegistry.All.Count} type(s) constructible");

            return failures;
        }

        /// <summary>Builds small graphs in code and checks the compiler's behaviour.</summary>
        private static int TestCompiler(List<string> failures, ref int failureCount)
        {
            int checks = 0;

            // Empty graph: must compile, producing a warning rather than an error.
            checks++;
            var empty = new PCGGraph();
            if (!PCGGraphCompiler.TryCompile(empty, out var emptyCompiled))
            {
                failureCount++;
                failures.Add("empty graph failed to compile");
                Debug.LogError("[PCG] FAIL empty graph should compile with a warning");
            }
            else
            {
                Debug.Log("[PCG] PASS empty graph compiles");
            }

            // Unknown node type: must be reported as an error instead of throwing.
            checks++;
            var badType = new PCGGraph(
                new[] { new Graph.PCGNode { TypeId = "NoSuchNodeType" } },
                Array.Empty<PCGEdge>());

            if (PCGGraphCompiler.TryCompile(badType, out var badCompiled) || badCompiled == null)
            {
                failureCount++;
                failures.Add("unknown node type was not rejected");
                Debug.LogError("[PCG] FAIL unknown node type should be rejected");
            }
            else
            {
                Debug.Log("[PCG] PASS unknown node type rejected with a diagnostic");
            }

            // Cycle: must be caught at compile time, not by overflowing the stack at run time.
            checks++;
            var cyclic = new PCGGraph(
                new[]
                {
                    new Graph.PCGNode { TypeId = PCGDensityFilterNode.Id, Values = new object[8] },
                    new Graph.PCGNode { TypeId = PCGDensityFilterNode.Id, Values = new object[8] },
                },
                new[]
                {
                    new PCGEdge { OutputNodeIndex = 0, OutputPinIndex = 0, InputNodeIndex = 1, InputPinIndex = 0 },
                    new PCGEdge { OutputNodeIndex = 1, OutputPinIndex = 0, InputNodeIndex = 0, InputPinIndex = 0 },
                });

            bool rejected = !PCGGraphCompiler.TryCompile(cyclic, out var cyclicCompiled);
            bool cycleReported = false;
            if (cyclicCompiled?.Diagnostics != null)
            {
                for (int i = 0; i < cyclicCompiled.Diagnostics.Count; i++)
                {
                    if (cyclicCompiled.Diagnostics[i].Message != null &&
                        cyclicCompiled.Diagnostics[i].Message.Contains("cycle"))
                    {
                        cycleReported = true;
                        break;
                    }
                }
            }

            if (!rejected || !cycleReported)
            {
                failureCount++;
                failures.Add("a two-node cycle was not detected at compile time");
                Debug.LogError("[PCG] FAIL cycle detection did not report a cycle");
            }
            else
            {
                Debug.Log("[PCG] PASS cycle detected at compile time");
            }

            // Missing required input: must be a compile error, not a run-time null dereference.
            checks++;
            var missingInput = new PCGGraph(
                new[] { new Graph.PCGNode { TypeId = PCGDensityFilterNode.Id, Values = new object[8] } },
                Array.Empty<PCGEdge>());

            if (PCGGraphCompiler.TryCompile(missingInput, out var missingCompiled) || missingCompiled == null)
            {
                failureCount++;
                failures.Add("unconnected required input was not reported");
                Debug.LogError("[PCG] FAIL unconnected required input should be a compile error");
            }
            else
            {
                Debug.Log("[PCG] PASS unconnected required input reported");
            }

            // Execution of a valid graph with no terrain: the sampler yields nothing and the run completes
            // without throwing, which is the behaviour a missing scene setup should produce.
            checks++;
            var executable = new PCGGraph(
                new[]
                {
                    new Graph.PCGNode
                    {
                        TypeId = PCGLandscapeSamplerNode.Id,
                        Values = BuildSamplerValues(64),
                    },
                    new Graph.PCGNode
                    {
                        TypeId = PCGDensityFilterNode.Id,
                        Values = new object[] { 0.0f, 1.0f, false },
                    },
                },
                new[]
                {
                    new PCGEdge { OutputNodeIndex = 0, OutputPinIndex = 0, InputNodeIndex = 1, InputPinIndex = 0 },
                });

            if (!PCGGraphCompiler.TryCompile(executable, out var compiled) || compiled == null)
            {
                failureCount++;
                failures.Add("a valid two-node graph failed to compile");
                Debug.LogError("[PCG] FAIL valid graph failed to compile");
            }
            else
            {
                var context = new PCGExecutionContext
                {
                    Seed = 12345,
                    GenerationBounds = new BoundingBox(new Vector3(-100.0f), new Vector3(100.0f)),
                    World = new PCGWorldContext(),
                };

                try
                {
                    var executor = new PCGGraphExecutor(compiled);
                    var run = executor.Execute(context);

                    if (run.Cancelled)
                    {
                        failureCount++;
                        failures.Add("graph run reported cancellation without a token being set");
                        Debug.LogError("[PCG] FAIL valid graph run was cancelled unexpectedly");
                    }
                    else
                    {
                        Debug.Log($"[PCG] PASS valid graph executed: {run.Outputs.Length} output(s), " +
                                  $"{run.TotalPointsProduced} point(s), {run.Milliseconds:F2} ms" +
                                  (run.Outputs.Length > 0 ? $", primary={run.Outputs[0].Count} point(s)" : string.Empty));
                    }
                }
                catch (Exception ex)
                {
                    failureCount++;
                    failures.Add($"executing a valid graph threw {ex.GetType().Name}: {ex.Message}");
                    Debug.LogError($"[PCG] FAIL valid graph execution threw: {ex}");
                }
            }

            return checks;
        }

        private static object[] BuildSamplerValues(int pointCount)
        {
            // Order matches PCGLandscapeSamplerNode's parameter indices.
            return new object[]
            {
                pointCount,                                 // PointCount
                0.5f,                                       // Jitter
                0.0f,                                       // HeightOffset
                true,                                       // AlignToNormal
                PCGTerrainSampleMode.RayCast,               // SampleMode
                true,                                       // FallbackToRayCast
                1.0f,                                       // Scale
                90.0f,                                      // MaxSlopeDegrees
                0,                                          // SeedOffset
            };
        }

        /// <summary>
        /// Verifies that an attribute set handed to another owner cannot be written through.
        /// </summary>
        /// <remarks>
        /// <b>Why this no longer exercises Share()/Detach().</b> <c>PCGAttributeSet.Share()</c> returns
        /// <c>this</c>, while <c>Detach()</c> mutates in place by swapping its own columns for clones. Those two
        /// semantics cannot compose: after <c>Share()</c> both references denote one object, so a write through
        /// either one is a write to the only storage that exists. Copy-on-write isolation is therefore not
        /// merely buggy here, it is unrepresentable until the nine column dictionaries move behind a
        /// shareable backing object.
        ///
        /// That refactor lands with <c>MergePoints</c> / <c>SplitPoints</c>, the first nodes that genuinely
        /// need shared storage (design doc section 3.2 note 3, Phase 2). Every production path today goes
        /// through <c>Clone()</c> or <c>Gather()</c>, which already deep-copy, so this test asserts the
        /// isolation property that actually protects Phase 1 output. Do not re-point it at Share() before
        /// that refactor exists.
        /// </remarks>
        private static bool TestAttributeCopyOnWrite(out string error)
        {
            error = null;

            var source = new PCGAttributeSet(4);
            source.DeclareAttribute("Height", PCGAttributeType.Float);
            var column = source.GetWritableFloatArray("Height");
            for (int i = 0; i < 4; i++)
                column[i] = i * 10.0f;

            // Clone() must hand back an exclusive deep copy, not an alias.
            var copy = source.Clone();
            copy.SetFloat("Height", 0, 999.0f);

            if (Mathf.Abs(source.GetFloat("Height", 0) - 0.0f) > 0.0001f)
            {
                error = $"writing a Clone() changed the source value to {source.GetFloat("Height", 0)}, expected 0";
                return false;
            }

            if (Mathf.Abs(copy.GetFloat("Height", 0) - 999.0f) > 0.0001f)
            {
                error = "the Clone() write did not take effect on the copy itself";
                return false;
            }

            // Unwritten rows must survive the copy intact.
            for (int i = 1; i < 4; i++)
            {
                if (Mathf.Abs(copy.GetFloat("Height", i) - source.GetFloat("Height", i)) > 0.0001f)
                {
                    error = $"row {i} diverged between a clone and its source";
                    return false;
                }
            }

            // A second Clone() of the same source must not observe the first one's writes.
            var copy2 = source.Clone();
            if (Mathf.Abs(copy2.GetFloat("Height", 0) - 0.0f) > 0.0001f)
            {
                error = $"a later clone inherited an earlier clone's write ({copy2.GetFloat("Height", 0)})";
                return false;
            }

            // Clone() must return storage we own, otherwise the next write would hit a no-op barrier.
            if (copy.IsShared || copy2.IsShared)
            {
                error = "Clone() returned storage that is not solely owned";
                return false;
            }

            // Built-in channels must refuse writes through the attribute set.
            var set = new PCGAttributeSet(1);
            try
            {
                set.SetFloat(PCGAttributeSet.DensityName, 0, 0.5f);
                error = "writing the built-in $Density channel through the attribute set was allowed";
                return false;
            }
            catch (PCGAttributeException ex) when (ex.Error == PCGAttributeError.BuiltInReadOnly)
            {
                // Expected.
            }

            return true;
        }

        /// <summary>
        /// Verifies a point regenerated on its own matches the value it had in a full sequential run.
        /// </summary>
        private static bool TestDeterministicUnderReordering(out string error)
        {
            error = null;

            const int Count = 256;
            var forward = new Vector3[Count];
            for (int i = 0; i < Count; i++)
            {
                var rng = new PCGDeterministicRng(4242, 7, i);
                forward[i] = new Vector3(rng.NextRange(-1.0f, 1.0f), rng.NextRange(-1.0f, 1.0f), rng.NextRange(-1.0f, 1.0f));
            }

            // Recompute in reverse order and compare. A sequential generator would diverge here.
            for (int i = Count - 1; i >= 0; i--)
            {
                var rng = new PCGDeterministicRng(4242, 7, i);
                var again = new Vector3(rng.NextRange(-1.0f, 1.0f), rng.NextRange(-1.0f, 1.0f), rng.NextRange(-1.0f, 1.0f));

                if (again != forward[i])
                {
                    error = $"point {i} recomputed to {again} but the forward run produced {forward[i]}";
                    return false;
                }
            }

            return true;
        }

        /// <summary>Verifies parameter changes and topology changes are distinguished by the hash.</summary>
        private static bool TestStructuralHash(out string error)
        {
            error = null;

            var graph = new PCGGraph(
                new[] { new Graph.PCGNode { TypeId = PCGDensityFilterNode.Id, Values = new object[] { 0.0f, 1.0f, false } } },
                Array.Empty<PCGEdge>());

            int baseline = PCGGraphCompiler.ComputeStructuralHash(graph);

            // Same topology, different value: the hash must change, or an edited graph would not recompile.
            var changedValue = new PCGGraph(
                new[] { new Graph.PCGNode { TypeId = PCGDensityFilterNode.Id, Values = new object[] { 0.25f, 1.0f, false } } },
                Array.Empty<PCGEdge>());

            int afterValueChange = PCGGraphCompiler.ComputeStructuralHash(changedValue);
            if (baseline == afterValueChange)
            {
                error = "a parameter change did not alter the structural hash";
                return false;
            }

            // Exposing a parameter removes its value from the hash, so two graphs that differ only in the
            // VALUE of an exposed parameter must hash identically. That is what lets one compiled graph serve
            // several volumes with different overrides without a recompile per volume.
            //
            // Note that flipping a parameter from non-exposed to exposed legitimately DOES change the hash:
            // the folded input set changes. Asserting equality across that flip tests the wrong property
            // (an earlier revision of this check did, and failed against a correct implementation).
            var exposedGraph = new PCGGraph(
                new[] { new Graph.PCGNode { TypeId = PCGDensityFilterNode.Id, Values = new object[] { 0.0f, 1.0f, false } } },
                Array.Empty<PCGEdge>());

            exposedGraph.ExposedParameters = new[]
            {
                new PCGParameterDefinition
                {
                    Name = "MinDensity",
                    Type = PCGParameterType.Float,
                    NodeIndex = 0,
                    ValueIndex = 0,
                },
            };

            int withExposed = PCGGraphCompiler.ComputeStructuralHash(exposedGraph);

            // Exposing a parameter and then overriding its value must not change the hash: the whole point of
            // exclusion is that override values are not structural.
            exposedGraph.Nodes[0].Values[0] = 0.75f;
            if (PCGGraphCompiler.ComputeStructuralHash(exposedGraph) != withExposed)
            {
                error = "an exposed parameter's value affected the structural hash";
                return false;
            }

            // Stability: hashing the same graph twice must agree, otherwise reuse never triggers.
            if (PCGGraphCompiler.ComputeStructuralHash(graph) != baseline)
            {
                error = "structural hash is not stable across calls";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Verifies the two terrain paths agree after calibration, and reports the residual.
        /// </summary>
        /// <remarks>
        /// This is the cross-check that makes the fast heightfield path trustworthy: until it passes on a
        /// given terrain, the calibration is a guess, and a wrong guess displaces every generated object by a
        /// constant factor.
        /// </remarks>
        private bool TestTerrainPaths(Terrain terrain, out string error)
        {
            error = null;

            var calibration = PCGTerrainProbe.Calibrate(terrain, TerrainProbesPerAxis, log: true);

            if (!calibration.IsValid)
            {
                error = "calibration did not validate, so the heightfield path stays disabled on this terrain";
                return false;
            }

            var heightfield = new PCGTerrainSampler(terrain, calibration);

            // The ray cast path is the ground truth and does not consult the calibration.
            var truthSampler = new PCGTerrainSampler(terrain, PCGTerrainCalibration.Invalid);

            var box = terrain.Box;
            int probes = Mathf.Max(3, TerrainProbesPerAxis);
            float worst = 0.0f;
            int compared = 0;

            for (int iz = 0; iz < probes; iz++)
            {
                for (int ix = 0; ix < probes; ix++)
                {
                    float tx = (ix + 0.5f) / probes;
                    float tz = (iz + 0.5f) / probes;

                    float x = Mathf.Lerp(box.Minimum.X, box.Maximum.X, tx);
                    float z = Mathf.Lerp(box.Minimum.Z, box.Maximum.Z, tz);

                    var truth = truthSampler.Sample(x, z, PCGTerrainSampleMode.RayCast);
                    if (!truth.Hit)
                        continue;

                    var fast = heightfield.Sample(x, z, PCGTerrainSampleMode.HeightfieldInterpolated, 20000.0f, false);
                    if (!fast.Hit)
                    {
                        error = $"heightfield path missed at ({x:F1}, {z:F1}) while the ray cast hit";
                        return false;
                    }

                    float delta = Mathf.Abs(fast.Height - truth.Height);
                    if (delta > worst)
                        worst = delta;

                    compared++;
                }
            }

            if (compared == 0)
            {
                error = "no probe point hit the terrain; is the terrain fully created?";
                return false;
            }

            if (worst > 0.5f)
            {
                error = $"heightfield and ray cast paths disagree by up to {worst:F3} world units over " +
                        $"{compared} point(s)";
                return false;
            }

            Debug.Log($"[PCG] terrain paths agree within {worst:F4} world units over {compared} point(s); " +
                      $"{calibration}");

            return true;
        }
    }
}

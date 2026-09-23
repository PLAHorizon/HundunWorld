// PCG Plugin - PCG Volume actor
// Part of the FlaxEngine PCG plugin. See docs/PCG_PLUGIN_DESIGN.md §7.1 and §7.2.

using System;
using System.Collections.Generic;
using FlaxEngine;
using PCG.Execution;
using PCG.Graph;

namespace PCG.Runtime
{
    /// <summary>How a volume applies its graph output.</summary>
    public enum PCGGenerationMode
    {
        /// <summary>Run in the editor and keep the generated objects in the scene. Final production output.</summary>
        Bake = 0,

        /// <summary>Run at runtime and keep regenerating as needed.</summary>
        Runtime = 1,

        /// <summary>Run in the editor for debug drawing only; nothing is added to the scene.</summary>
        Preview = 2,
    }

    /// <summary>Per-instance override of a graph parameter.</summary>
    [Serializable]
    public struct PCGParameterOverride
    {
        /// <summary>Name of the exposed parameter to override.</summary>
        public string ParameterName;

        /// <summary>Replacement value.</summary>
        public PCGParameterValue Value;

        /// <inheritdoc />
        public override string ToString() => $"{ParameterName} = {Value}";
    }

    /// <summary>Spawned-object bookkeeping for one graph run, so a regenerate can replace exactly its own output.</summary>
    public sealed class PCGGenerationResult
    {
        /// <summary>Actors created by the run.</summary>
        public List<Actor> SpawnedActors = new List<Actor>();

        /// <summary>Point count of the primary output.</summary>
        public int PointCount;

        /// <summary>Wall-clock duration of the run.</summary>
        public double Milliseconds;

        /// <summary>Non-fatal messages from the run.</summary>
        public string[] Warnings = Array.Empty<string>();

        /// <summary>Unix timestamp of the run.</summary>
        public long TimestampUtcTicks;

        /// <inheritdoc />
        public override string ToString() =>
            $"PCGGenerationResult(actors={SpawnedActors.Count}, points={PointCount}, ms={Milliseconds:F1})";
    }

    /// <summary>
    /// Scene component that runs a PCG graph over a box volume.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Volume space.</b> <see cref="Volume"/> is in the actor's local space, so the generation region
    /// follows the actor when it is moved or rotated. Points are produced in world space, so the actor's
    /// transform is composed in once, at generation time. Moving the volume afterwards does not move existing
    /// output; regenerate instead. That is a deliberate choice: continuously re-generating on every transform
    /// change would make dragging the actor unusable on a large graph.
    /// </para>
    /// <para>
    /// <b>Output ownership.</b> Generated actors are parented under this actor and named with a fixed prefix,
    /// so <see cref="Clear"/> can remove precisely this volume's output without touching hand-placed scene
    /// content.
    /// </para>
    /// </remarks>
    public class PCGVolume : Actor
    {
        /// <summary>Prefix given to every actor this volume generates.</summary>
        public const string GeneratedNamePrefix = PCGSpawnNaming.GeneratedPrefix;

        /// <summary>Graph to execute. Null means the volume is inert.</summary>
        [Tooltip("PCG graph to execute.")]
        public PCGGraphAsset Graph;

        /// <summary>Generation region, in this actor's local space.</summary>
        [Tooltip("Generation region, in this actor's local space.")]
        public BoundingBox Volume = new BoundingBox(new Vector3(-500.0f), new Vector3(500.0f));

        /// <summary>Per-instance overrides of the graph's exposed parameters.</summary>
        [Tooltip("Overrides for the graph's exposed parameters.")]
        public PCGParameterOverride[] ParameterOverrides = Array.Empty<PCGParameterOverride>();

        /// <summary>Graph-level random seed.</summary>
        [Tooltip("Graph-level random seed. Change it to re-roll the result.")]
        public int Seed;

        /// <summary>How the output is applied.</summary>
        [Tooltip("How the graph output is applied.")]
        public PCGGenerationMode Mode = PCGGenerationMode.Bake;

        /// <summary>Draw generated points in the viewport.</summary>
        [Tooltip("Draw generated points in the viewport.")]
        public bool DebugDrawPoints = true;

        /// <summary>Skip points whose density is below this value when debug drawing.</summary>
        [Range(0.0f, 1.0f)]
        [Tooltip("Hide debug points below this density.")]
        public float DebugDrawMinDensity;

        /// <summary>Maximum debug points drawn, to keep the editor responsive.</summary>
        [Tooltip("Maximum debug points drawn with density colouring. Higher values cost more per frame.")]
        public int DebugDrawMaxPoints = 20000;

        private PCGCompiledGraph _compiled;
        private PCGGenerationResult _result;
        private PCGData _lastOutput;
        private int _compiledHash;
        private bool _isGenerating;

        /// <summary>Result of the most recent generation, or null.</summary>
        public PCGGenerationResult LastResult => _result;

        /// <summary>Points produced by the most recent generation, or null.</summary>
        public PCGData LastOutput => _lastOutput;

        /// <summary>True when a generation is currently in progress.</summary>
        public bool IsGenerating => _isGenerating;

        /// <summary>Number of actors this volume has generated and not yet cleared.</summary>
        public int GeneratedActorCount => _result?.SpawnedActors?.Count ?? 0;

        /// <summary>
        /// Runs the graph and applies its output according to <see cref="Mode"/>.
        /// </summary>
        /// <returns>True when the graph ran and produced output.</returns>
        public bool Generate()
        {
            if (_isGenerating)
            {
                Debug.LogWarning($"[PCG] {Name}: generation already in progress; ignoring re-entrant call.");
                return false;
            }

            if (Graph == null)
            {
                Debug.LogWarning($"[PCG] {Name}: no graph assigned.");
                return false;
            }

            var graph = Graph.GetGraph();
            if (graph == null)
            {
                Debug.LogWarning($"[PCG] {Name}: assigned graph asset holds no graph data.");
                return false;
            }

            _isGenerating = true;

            try
            {
                // Recompile only when the topology changed. A parameter change reuses the compiled graph,
                // which is what keeps editing a value cheap.
                int hash = PCGGraphCompiler.ComputeStructuralHash(graph);
                if (_compiled == null || _compiledHash != hash)
                {
                    if (!PCGGraphCompiler.TryCompile(graph, out var compiled))
                    {
                        var message = compiled != null && compiled.Diagnostics != null
                            ? string.Join("\n  ", GetErrorMessages(compiled.Diagnostics))
                            : "unknown reason";

                        Debug.LogError($"[PCG] {Name}: graph failed to compile:\n  {message}");
                        return false;
                    }

                    _compiled = compiled;
                    _compiledHash = hash;

                    for (int i = 0; i < (_compiled.Diagnostics?.Count ?? 0); i++)
                    {
                        var diagnostic = _compiled.Diagnostics[i];
                        if (diagnostic.Severity == PCGCompileSeverity.Warning)
                            Debug.LogWarning($"[PCG] {Name}: {diagnostic}");
                    }
                }

                var context = BuildContext();

                // Bake and Runtime replace previous output; Preview must not touch the scene at all.
                if (Mode != PCGGenerationMode.Preview)
                    Clear();

                var executor = new PCGGraphExecutor(_compiled);
                var run = executor.Execute(context);

                if (run.Cancelled)
                {
                    Debug.LogWarning($"[PCG] {Name}: generation cancelled.");
                    return false;
                }

                _lastOutput = run.PrimaryOutput;

                _result = new PCGGenerationResult
                {
                    PointCount = _lastOutput?.Count ?? 0,
                    Milliseconds = run.Milliseconds,
                    Warnings = run.Warnings ?? Array.Empty<string>(),
                    TimestampUtcTicks = DateTime.UtcNow.Ticks,
                };

                for (int i = 0; i < _result.Warnings.Length; i++)
                    Debug.LogWarning($"[PCG] {Name}: {_result.Warnings[i]}");

                AdoptGeneratedActors();

                Debug.Log($"[PCG] {Name}: generated {_result.PointCount} point(s) in {run.Milliseconds:F1} ms " +
                          $"({_result.SpawnedActors.Count} actor(s), mode {Mode}).");

                return _result.PointCount > 0;
            }
            catch (PCGCompilationException ex)
            {
                Debug.LogError($"[PCG] {Name}: {ex.Message}");
                return false;
            }
            catch (PCGNodeExecutionException ex)
            {
                Debug.LogError($"[PCG] {Name}: node execution failed: {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PCG] {Name}: generation failed: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
                return false;
            }
            finally
            {
                _isGenerating = false;
            }
        }

        /// <summary>
        /// Removes everything this volume generated, leaving hand-placed scene content alone.
        /// </summary>
        /// <returns>The number of actors destroyed.</returns>
        public int Clear()
        {
            int removed = 0;

            // Ownership is structural: generated objects are parented under this volume, so clearing is just
            // "destroy my children". This survives a scene reload because the parent-child relationship is
            // part of the saved scene, unlike an in-memory tracking list.
            //
            // A tag lookup was the obvious alternative and does not work here: Level.FindActors takes a
            // Tag, and Flax's Tag is an index into the project's tag list with no runtime construction from
            // an arbitrary string, so a per-volume tag cannot be minted on the fly.
            var children = GetChildren<Actor>();
            if (children != null)
            {
                for (int i = children.Length - 1; i >= 0; i--)
                {
                    var actor = children[i];
                    if (actor == null)
                        continue;

                    Actor.Destroy(actor);
                    removed++;
                }
            }

            if (_result?.SpawnedActors != null)
                _result.SpawnedActors.Clear();

            _lastOutput = null;
            _result = null;

            return removed;
        }

        /// <summary>Discards the compiled graph, forcing a recompile on the next generate.</summary>
        public void InvalidateCompilation()
        {
            _compiled = null;
            _compiledHash = 0;
        }

        private PCGExecutionContext BuildContext()
        {
            var world = new PCGWorldContext();
            world.CollectTerrainsFromScene();

            world.ActorResolver = id => Level.FindActor(id);
            world.ActorNameResolver = name => Level.FindActor(name);

            return new PCGExecutionContext
            {
                Seed = Seed,
                GenerationBounds = TransformVolumeToWorld(),
                World = world,
                DebugMode = DebugDrawPoints ? PCGDebugMode.PointsDensity : PCGDebugMode.None,
                Source = this,
            };
        }

        /// <summary>
        /// Converts the local-space <see cref="Volume"/> into a world-space box.
        /// </summary>
        public BoundingBox TransformVolumeToWorld()
        {
            return BoundingBox.Transform(Volume, Transform);
        }

        /// <summary>
        /// Reparents newly spawned actors under this volume and records them.
        /// </summary>
        /// <remarks>
        /// The spawner node creates actors at the scene root and knows nothing about the volume, so ownership
        /// is established here. Parenting is the whole mechanism: because the relationship is part of the
        /// saved scene, <see cref="Clear"/> can reclaim this volume's output after a reload without needing
        /// any runtime side table.
        /// </remarks>
        private void AdoptGeneratedActors()
        {
            if (_result == null)
                return;

            var scene = Scene;
            if (scene == null)
                return;

            // Scene.GetChildren returns only root-level actors, which is exactly the set a spawner adds to.
            var roots = scene.GetChildren<Actor>();
            if (roots == null)
                return;

            for (int i = 0; i < roots.Length; i++)
            {
                var actor = roots[i];
                if (actor == null || actor == this)
                    continue;

                // Only claim actors this plugin produced, identified by the shared prefix.
                if (!PCGSpawnNaming.IsGeneratedName(actor.Name))
                    continue;

                actor.Parent = this;
                _result.SpawnedActors.Add(actor);
            }
        }

        /// <inheritdoc />
        public override void OnDebugDraw()
        {
            base.OnDebugDraw();

            if (!DebugDrawPoints || _lastOutput == null)
                return;

            var points = _lastOutput.Points;
            if (points == null || points.Length == 0)
                return;

            // Draw the generation volume so the region is visible even before anything spawns.
            DrawWireBox(TransformVolumeToWorld(), new Color(0.2f, 0.8f, 1.0f), 0.0f, false);

            int step = 1;
            if (DebugDrawMaxPoints > 0 && points.Length > DebugDrawMaxPoints)
                step = Mathf.Max(1, points.Length / DebugDrawMaxPoints);

            // Batch: the per-point DrawSphere call would be one draw per point, which stalls the editor at a
            // few thousand points. DrawLines takes a flat vertex array, so points are accumulated into one
            // call per density band with the cross built from four vertices each.
            const int Bands = 4;
            var bandVertices = new List<Vector3>[Bands];
            var bandCounts = new int[Bands];

            for (int i = 0; i < points.Length; i += step)
            {
                var point = points[i];
                if (point.Density < DebugDrawMinDensity)
                    continue;

                int band = Mathf.Clamp((int)(point.Density * Bands), 0, Bands - 1);
                bandVertices[band] ??= new List<Vector3>();
                bandVertices[band].Add(point.Position);
                bandCounts[band]++;
            }

            for (int band = 0; band < Bands; band++)
            {
                if (bandVertices[band] == null || bandCounts[band] == 0)
                    continue;

                // Low density leans blue, high density leans red.
                float t = Bands > 1 ? band / (float)(Bands - 1) : 0.0f;
                var color = new Color(Mathf.Lerp(0.2f, 1.0f, t), Mathf.Lerp(0.4f, 0.3f, t), Mathf.Lerp(1.0f, 0.2f, t));

                var positions = bandVertices[band];
                float size = step > 1 ? 6.0f : 4.0f;
                DrawBatchCrosses(positions, size, color);
            }
        }

        private static void DrawBatchCrosses(List<Vector3> positions, float size, Color color)
        {
            // Two segments per point, so four vertices per point, laid out flat as required by DrawLines.
            var lines = new Float3[positions.Count * 4];

            for (int i = 0; i < positions.Count; i++)
            {
                var p = positions[i];
                int o = i * 4;

                lines[o + 0] = new Float3(p.X - size, p.Y, p.Z);
                lines[o + 1] = new Float3(p.X + size, p.Y, p.Z);

                // A second vertical segment reads better against sloped terrain than a second horizontal one.
                lines[o + 2] = new Float3(p.X, p.Y - size, p.Z);
                lines[o + 3] = new Float3(p.X, p.Y + size, p.Z);
            }

            if (lines.Length > 0)
                DebugDraw.DrawLines(lines, Matrix.Identity, color, 0.0f, false);
        }

        private void DrawWireBox(BoundingBox box, Color color, float duration, bool depthTest)
        {
            DebugDraw.DrawWireBox(box, color, duration, depthTest);
        }

        private static IEnumerable<string> GetErrorMessages(IReadOnlyList<PCGCompileDiagnostic> diagnostics)
        {
            for (int i = 0; i < diagnostics.Count; i++)
            {
                if (diagnostics[i].Severity == PCGCompileSeverity.Error)
                    yield return diagnostics[i].ToString();
            }
        }
    }
}

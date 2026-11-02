// PCG Plugin - Spawn Foliage node
// Part of the FlaxEngine PCG plugin. See docs/PCG_PLUGIN_DESIGN.md §5.1.5.

using System;
using System.Collections.Generic;
using FlaxEngine;
using PCG.Execution;

namespace PCG.Nodes
{
    /// <summary>
    /// Places input points as foliage instances on one <see cref="Foliage"/> actor.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why this exists next to Spawn Static Model.</b> A spawned actor is a scene object: name, transform,
    /// component list, outliner entry, and a place in the actor registry. That is the right thing for a hand
    /// placed prop and the wrong thing for forty thousand grass tufts. The engine's instancing path is
    /// <c>Foliage</c> - <c>AddInstance</c> stores a transform without creating an actor, and
    /// <c>RebuildClusters</c> bakes the culling structure once - so a forest is one actor plus N instances
    /// rather than N actors. The design document's own note on the static mesh spawner says to use foliage for
    /// dense placement; this is that spawner.
    /// </para>
    /// <para>
    /// <b>One actor per node, reused between runs.</b> A fresh <c>Foliage</c> per run would leave the previous
    /// run's instances standing: they are not actors, so the volume's structural cleanup cannot reach them
    /// individually. The node therefore looks for the actor it created before (by the same naming rule the
    /// static mesh spawner uses, keyed on graph position), clears its instances, and writes the new set. It is
    /// parented under the volume, so <see cref="FlaxEngine.Actor.Destroy"/> on the parent takes it along.
    /// </para>
    /// <para>
    /// <b>The instance transforms are written in world space and the actor keeps an identity transform</b>, so
    /// "world space" and "the actor's space" are the same thing here and there is no conversion left to get
    /// wrong when the volume is moved.
    /// </para>
    /// <para>
    /// <b>Main thread only</b>, like every node that touches the scene.
    /// </para>
    /// </remarks>
    public sealed class PCGSpawnFoliageNode : PCGNodeBase
    {
        /// <summary>Stable type id.</summary>
        public const string Id = "SpawnFoliage";

        /// <summary>Name prefix of the foliage actor this node owns; the graph index and cell are appended.</summary>
        /// <remarks>
        /// The cell has to be part of the identity: instances live inside the host rather than being actors of
        /// their own, so a shared host would let the last cell's <c>RemoveAllInstances</c> erase every earlier
        /// cell's scatter - and a reused cell would report points whose instances no longer exist.
        /// </remarks>
        public const string ActorNamePrefix = "PCG_Foliage_";

        private const int PModelId = 0;
        private const int PDensityThreshold = 1;
        private const int PScaleMultiplier = 2;
        private const int PRandomYaw = 3;
        private const int PMaxInstances = 4;
        private const int PCullDistance = 5;

        /// <summary>Number of parameters this node reads.</summary>
        public const int ParameterCount = 6;

        /// <summary>
        /// Default ceiling on instances placed in one run.
        /// </summary>
        /// <remarks>
        /// Instances are far cheaper than actors, so the ceiling is a runaway-graph guard rather than a
        /// planning tool: a graph that wants ten times this should raise it deliberately instead of silently
        /// filling the level and the editor's memory.
        /// </remarks>
        public const int DefaultMaxInstances = 50000;

        private readonly string _typeId;
        private readonly PCGNodeParameters _params;

        /// <summary>Asset id of the model to instance.</summary>
        public Guid ModelId;

        /// <summary>Points with a density below a random draw in [0,1] are skipped; 0 places at every point.</summary>
        public float DensityThreshold;

        /// <summary>Multiplier applied to each point's scale.</summary>
        public float ScaleMultiplier = 1.0f;

        /// <summary>When set, each instance gets a random yaw about its own up axis.</summary>
        public bool RandomYaw = true;

        /// <summary>Maximum instances this node may place in one run.</summary>
        public int MaxInstances = DefaultMaxInstances;

        /// <summary>
        /// Cull distance written to the foliage type. Zero leaves the engine's own default alone, which is
        /// what a designer who has not looked at the number expects.
        /// </summary>
        public float CullDistance;

        /// <summary>Instances placed by the most recent run.</summary>
        public int PlacedCount { get; private set; }

        /// <summary>True when the most recent run stopped at <see cref="MaxInstances"/>.</summary>
        public bool LastRunWasCapped { get; private set; }

        /// <summary>Creates the node from serialized parameters.</summary>
        public PCGSpawnFoliageNode(PCGNodeRegistry.PCGNodeSerializationData data)
        {
            _typeId = Id;
            _params = new PCGNodeParameters(data.Values, data.TypeId);

            ModelId = _params.GetAssetId(PModelId);
            DensityThreshold = Mathf.Clamp(_params.Get(PDensityThreshold, 0.0f), 0.0f, 1.0f);
            ScaleMultiplier = Mathf.Max(0.0001f, _params.Get(PScaleMultiplier, 1.0f));
            RandomYaw = _params.Get(PRandomYaw, true);
            MaxInstances = Mathf.Max(0, _params.Get(PMaxInstances, DefaultMaxInstances));
            CullDistance = Mathf.Max(0.0f, _params.Get(PCullDistance, 0.0f));
        }

        /// <inheritdoc />
        public override string TypeId => _typeId;

        /// <inheritdoc />
        public override string DisplayName => "Spawn Foliage";

        /// <inheritdoc />
        public override bool IsThreadSafe => false;

        /// <inheritdoc />
        public override PCGPinDefinition[] InputPins => new[]
        {
            new PCGPinDefinition("In", PCGPinDirection.Input, isRequired: true,
                tooltip: "Points to place as instances."),
        };

        /// <inheritdoc />
        public override PCGPinDefinition[] OutputPins => new[]
        {
            new PCGPinDefinition("Out", PCGPinDirection.Output,
                tooltip: "Points that were placed, for chaining further output nodes."),
        };

        /// <inheritdoc />
        public override string DescribeParameters() =>
            $"model={(ModelId == Guid.Empty ? "none" : ModelId.ToString())}, " +
            $"densityThreshold={DensityThreshold:F2}, maxInstances={MaxInstances}, " +
            $"cullDistance={(CullDistance <= 0.0f ? "engine default" : CullDistance.ToString("F0"))}, " +
            $"randomYaw={RandomYaw}";

        /// <inheritdoc />
        public override PCGData OnExecute(PCGExecutionContext context, PCGData[] inputs)
        {
            PlacedCount = 0;
            LastRunWasCapped = false;
            LastError = null;

            var input = inputs[0];
            if (input == null || input.Count == 0)
                return PCGData.Empty;

            if (ModelId == Guid.Empty)
            {
                LastError = "No model is assigned.";

                // 干跑连日志都不打：`Debug.LogWarning` 也是引擎调用，离屏预览会因此崩掉。诊断不会丢——LastError
                // 由执行器收进本次运行的警告里，那本来就是"该由执行器报出来"的正主。
                if (!context.DryRun)
                    Debug.LogWarning($"[PCG] {Id}: {LastError}");

                return input;
            }

            if (MaxInstances <= 0)
            {
                LastError = "MaxInstances is zero, so nothing can be placed.";
                return input;
            }

            // Preview and decider runs hand the layer on untouched: no host actor is created, no instances are
            // written, and - this is why it sits before the lookup below - **no asset is resolved either**.
            // A dry run that still needed the asset database would make preview depend on whether the model
            // happens to be loaded, and would make headless dry runs impossible.
            if (context.DryRun)
                return input;

            // 只查已载入的资产池会把"还没人加载过的一个好模型"判成配置错误（实测：同一条 Cone 的 Guid
            // 在本会话早先被报 not loaded，晚些被别的图正常取用），所以取不到时要请求一次真加载。
            var model = PCGAssetLoading.Resolve<Model>(ModelId);
            if (model == null)
            {
                LastError = $"Model asset {ModelId} is not loaded or is not a Model.";
                Debug.LogWarning($"[PCG] {Id}: {LastError}");
                return input;
            }

            var parent = context.Source as Actor;
            var foliage = FindOrCreateFoliage(context, parent, model);
            if (foliage == null)
            {
                LastError = "Could not create or reuse a Foliage actor.";
                Debug.LogWarning($"[PCG] {Id}: {LastError}");
                return input;
            }

            int typeIndex = ResolveTypeIndex(foliage, model);
            if (typeIndex < 0)
            {
                LastError = $"Could not register {model.Path} as a foliage type on {foliage.Name}.";
                Debug.LogWarning($"[PCG] {Id}: {LastError}");
                return input;
            }

            // Clearing happens before the loop rather than destroying the actor: the type list, and with it any
            // cull distance a designer set on the foliage type in the editor, survives a regenerate.
            foliage.RemoveAllInstances();

            var points = input.Points;
            var placed = new List<int>(Mathf.Min(points.Length, MaxInstances));

            // 两个筛子各自数一遍：最后的读数要说清"是谁把点吃光的"，只说"摆了 0 件"等于没说。
            int skippedOutsideClip = 0;
            int skippedByDensity = 0;

            // 落进裁切区那些点的**密度之和**：抽签是 `Density >= rng.NextFloat()`，
            // 这个和就是"这一层期望摆出几丛"。合计不足一件时，摆没摆出来是一次抛硬币。
            float expectedPlacementSum = 0.0f;

            for (int i = 0; i < points.Length; i++)
            {
                context.ThrowIfCancelled(i);

                if (placed.Count >= MaxInstances)
                {
                    LastRunWasCapped = true;
                    break;
                }

                var point = points[i];

                // The run covers a padded region so crowding can be decided across a cell border; placing the
                // padding's instances as well would double the props a neighbouring cell also places.
                if (!context.IsInsideClip(point.Position))
                {
                    skippedOutsideClip++;
                    continue;
                }

                expectedPlacementSum += point.Density;

                if (DensityThreshold > 0.0f)
                {
                    // Deterministic per-point draw so re-running the graph reproduces the same selection.
                    var rng = context.GetRng(i);
                    if (point.Density < rng.NextFloat())
                    {
                        skippedByDensity++;
                        continue;
                    }
                }

                var scale = new Float3(
                    point.Scale.X * ScaleMultiplier,
                    point.Scale.Y * ScaleMultiplier,
                    point.Scale.Z * ScaleMultiplier);

                var rotation = point.Rotation;
                if (RandomYaw)
                {
                    var yawRng = context.GetRng(i + 7919);
                    // Local, not world: a tuft on a slope spins about its surface normal instead of being tipped
                    // off it.
                    rotation = PCGPointTransform.YawAboutLocalUp(point.Rotation, yawRng.NextRange(0.0f, Mathf.TwoPi));
                }

                // AddInstance takes the struct by ref, so it has to be a local rather than an inline value.
                var instance = new FoliageInstance
                {
                    Type = (ushort)typeIndex,
                    Transform = new Transform(point.Position, rotation, scale),

                    // The engine's own per-instance random input; feeding it the point's density keeps the
                    // variation of a re-run identical to the run it replaces.
                    Random = point.Density,
                };

                foliage.AddInstance(ref instance);

                placed.Add(i);
                PlacedCount++;
            }

            // Once, after every instance is in: rebuilding per instance is what makes the cluster structure
            // cheap, and doing it inside the loop would rebuild it as many times as there are points.
            foliage.RebuildClusters();

            // 摆出来了**不等于这一层长出来了**：期望不足一件时这次是运气好，换个种子整层就没，
            // 屏幕上只是"稀"。与静态模型 / 程序化几何两条生成器同一条判据、同一种说法。
            if (placed.Count > 0 && DensityThreshold > 0.0f && expectedPlacementSum < 1.0f && !LastRunWasCapped)
            {
                LastError = $"Placed {placed.Count} instance(s) from {points.Length} point(s), but this layer " +
                            $"expected only {expectedPlacementSum:0.###} instance(s) in total, so whether it shows " +
                            "up at all is one coin flip. Raise points / per-block, or raise density.";

                // 干跑不打日志（`Debug.*` 在离屏预览里是会崩的），诊断留在 LastError 里由上层收集。
                if (!context.DryRun)
                    Debug.LogWarning($"[PCG] {Id}: {LastError}");
            }

            // **非空输入摆出 0 件**是这一层最难查的失败：图上看不出区别，运行结果也看不出区别，
            // 设计者只会看到一片空场景（实测：某张图 909 个点、0 件、0 条警告）。
            // 所以这里必须点名"是谁把所有点都吃掉了"，并把两个筛子的读数一起交出去。
            if (placed.Count == 0 && points.Length > 0 && !LastRunWasCapped)
            {
                LastError = $"Placed nothing from {points.Length} point(s): {skippedOutsideClip} fell outside " +
                            $"the region being generated, {skippedByDensity} were dropped by the density draw " +
                            $"(threshold {DensityThreshold:0.###}). A layer that yields no props usually means the " +
                            "density column is zero, the sampler put the points elsewhere, or the clip region " +
                            "does not cover them.";
                Debug.LogWarning($"[PCG] {Id}: {LastError}");
            }

            if (LastRunWasCapped)
            {
                Debug.LogWarning(
                    $"[PCG] {Id}: stopped at MaxInstances={MaxInstances} of {points.Length} point(s). " +
                    "Raise MaxInstances deliberately, or lower the sampler density.");
            }

            if (placed.Count == 0)
                return PCGData.Empty;

            if (placed.Count == points.Length)
                return input;

            return PCGData.Gather(input, placed.ToArray(), placed.Count);
        }

        /// <summary>
        /// Finds the foliage actor this node owns, or creates it.
        /// </summary>
        /// <remarks>
        /// Keyed on graph position rather than on the node instance, because a recompile replaces the node
        /// objects while the previous run's actor is still in the scene - and instances, unlike actors, are
        /// only reachable through the actor that holds them.
        /// </remarks>
        private static Foliage FindOrCreateFoliage(PCGExecutionContext context, Actor parent, Model model)
        {
            string name = ActorNamePrefix + Math.Max(0, context.CurrentNodeId) +
                          $"_l{Math.Max(0, context.LayerIndex)}_" +
                          (context.ClipCell.HasValue
                              ? $"{context.ClipCell.Value.IndexX}x{context.ClipCell.Value.IndexZ}z"
                              : "whole");

            if (parent != null)
            {
                var children = parent.GetChildren<Foliage>();
                if (children != null)
                {
                    for (int i = 0; i < children.Length; i++)
                    {
                        var existing = children[i];
                        if (existing != null && existing.Name == name)
                            return existing;
                    }
                }
            }

            var foliage = new Foliage
            {
                Name = name,

                // Identity on purpose: the instance transforms written below are world space, and an identity
                // actor makes those two the same frame.
                Transform = new Transform(Vector3.Zero, Quaternion.Identity, Float3.One),
            };

            if (!PCGActors.TrySpawn(foliage, parent))
            {
                Debug.LogWarning($"[PCG] {Id}: failed to spawn the foliage actor for {model.Path}.");
                return null;
            }

            return foliage;
        }

        /// <summary>
        /// Ensures the model is registered as a foliage type and returns its index.
        /// </summary>
        private int ResolveTypeIndex(Foliage foliage, Model model)
        {
            int count = foliage.FoliageTypesCount;
            for (int i = 0; i < count; i++)
            {
                var type = foliage.GetFoliageType(i);
                if (type?.Model != null && IsSameAsset(type.Model, model))
                {
                    ApplyCullDistance(type);
                    return i;
                }
            }

            foliage.AddFoliageType(model);

            count = foliage.FoliageTypesCount;
            if (count <= 0)
                return -1;

            var added = foliage.GetFoliageType(count - 1);
            if (added == null)
                return -1;

            ApplyCullDistance(added);
            return count - 1;
        }

        private void ApplyCullDistance(FoliageType type)
        {
            if (CullDistance > 0.0f && type.CullDistance != CullDistance)
                type.CullDistance = CullDistance;
        }

        private static bool IsSameAsset(Asset a, Asset b)
        {
            if (ReferenceEquals(a, b))
                return true;

            // Path is the identity an asset always reports; the managed wrapper for the same native asset is
            // not guaranteed to be the same object, and comparing references would re-add the type every run.
            return !string.IsNullOrEmpty(a.Path) && string.Equals(a.Path, b.Path, StringComparison.Ordinal);
        }
    }
}

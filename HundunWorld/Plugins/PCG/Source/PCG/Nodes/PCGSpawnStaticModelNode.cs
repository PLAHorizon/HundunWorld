// PCG Plugin - Spawn Static Model node
// Part of the FlaxEngine PCG plugin. See docs/PCG_PLUGIN_DESIGN.md §5.1.5.

using System;
using System.Collections.Generic;
using FlaxEngine;
using PCG.Execution;

namespace PCG.Nodes
{
    /// <summary>
    /// Spawns a static mesh at every input point.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Main thread only.</b> Actor creation is not thread-safe, so <see cref="IsThreadSafe"/> is false and
    /// the executor must be driven from the main thread. A background graph run has to hand its
    /// <see cref="PCGData"/> back to the main thread instead of applying it directly.
    /// </para>
    /// <para>
    /// <b>On batching.</b> The design document originally proposed routing large point counts through
    /// <c>ModelInstanceActor</c> on the assumption that it holds many instances in one actor. Inspecting the
    /// engine shows <c>ModelInstanceActor.Entries</c> is a <c>ModelInstanceEntry[]</c> whose fields are only
    /// material, visibility, shadow mode and decal reception - there is no transform field, and the per-mesh
    /// reference carries the transform. Instances are therefore still separate scene objects rather than one
    /// cheap draw, so batching does not deliver the large win the document assumed.
    /// </para>
    /// <para>
    /// For genuinely dense placement (tens of thousands of objects), <c>Foliage</c> is the engine's real
    /// instancing path: <c>AddFoliageType</c> plus <c>AddInstance</c> stores instances without creating
    /// actors, and rebuilds clusters once. Use a foliage spawner for that case instead of raising this node's
    /// budget.
    /// </para>
    /// </remarks>
    public sealed class PCGSpawnStaticModelNode : PCGNodeBase
    {
        /// <summary>Stable type id.</summary>
        public const string Id = "SpawnStaticModel";

        private const int PModelId = 0;
        private const int PDensityThreshold = 1;
        private const int PScaleMultiplier = 2;
        private const int PRandomYaw = 3;
        private const int PMaxActors = 4;

        /// <summary>Number of parameters this node reads.</summary>
        public const int ParameterCount = 5;

        /// <summary>
        /// Default ceiling on spawned actors in a single run.
        /// </summary>
        /// <remarks>
        /// Each spawned object is a full scene actor, so an accidental five-figure point count would stall
        /// the editor long before it ran out of memory. The cap converts that into a clear message.
        /// </remarks>
        public const int DefaultMaxActors = 5000;

        private readonly string _typeId;
        private readonly PCGNodeParameters _params;

        /// <summary>Asset id of the model to spawn.</summary>
        public Guid ModelId;

        /// <summary>
        /// Points with a density below a random draw in [0,1] are skipped, so density acts as spawn
        /// probability. Set to 0 to spawn at every point regardless of density.
        /// </summary>
        public float DensityThreshold;

        /// <summary>Multiplier applied to each point's scale.</summary>
        public float ScaleMultiplier = 1.0f;

        /// <summary>When set, each instance gets a random yaw about its own up axis.</summary>
        public bool RandomYaw = true;

        /// <summary>Maximum actors this node may spawn in one run.</summary>
        public int MaxActors = DefaultMaxActors;

        /// <summary>Actors spawned by the most recent run.</summary>
        public int SpawnedCount { get; private set; }

        /// <summary>True when the most recent run stopped at <see cref="MaxActors"/>.</summary>
        public bool LastRunWasCapped { get; private set; }

        /// <summary>Reported when the node could not spawn, for surfacing in the editor.</summary>
        public string LastError { get; private set; }

        /// <summary>Creates the node from serialized parameters.</summary>
        public PCGSpawnStaticModelNode(PCGNodeRegistry.PCGNodeSerializationData data)
        {
            _typeId = Id;
            _params = new PCGNodeParameters(data.Values, data.TypeId);

            ModelId = _params.GetAssetId(PModelId);
            DensityThreshold = Mathf.Clamp(_params.Get(PDensityThreshold, 0.0f), 0.0f, 1.0f);
            ScaleMultiplier = Mathf.Max(0.0001f, _params.Get(PScaleMultiplier, 1.0f));
            RandomYaw = _params.Get(PRandomYaw, true);
            MaxActors = Mathf.Max(0, _params.Get(PMaxActors, DefaultMaxActors));
        }

        /// <inheritdoc />
        public override string TypeId => _typeId;

        /// <inheritdoc />
        public override string DisplayName => "Spawn Static Model";

        /// <inheritdoc />
        public override bool IsThreadSafe => false;

        /// <inheritdoc />
        public override PCGPinDefinition[] InputPins => new[]
        {
            new PCGPinDefinition("In", PCGPinDirection.Input, isRequired: true,
                tooltip: "Points at which to spawn the model."),
        };

        /// <inheritdoc />
        public override PCGPinDefinition[] OutputPins => new[]
        {
            new PCGPinDefinition("Out", PCGPinDirection.Output,
                tooltip: "Points that were spawned, for chaining further output nodes."),
        };

        /// <inheritdoc />
        public override string DescribeParameters() =>
            $"model={(ModelId == Guid.Empty ? "none" : ModelId.ToString())}, " +
            $"densityThreshold={DensityThreshold:F2}, maxActors={MaxActors}, randomYaw={RandomYaw}";

        /// <inheritdoc />
        public override PCGData OnExecute(PCGExecutionContext context, PCGData[] inputs)
        {
            SpawnedCount = 0;
            LastRunWasCapped = false;
            LastError = null;

            var input = inputs[0];
            if (input == null || input.Count == 0)
                return PCGData.Empty;

            if (ModelId == Guid.Empty)
            {
                LastError = "No model is assigned.";
                Debug.LogWarning($"[PCG] {Id}: {LastError}");
                return input;
            }

            // Content.GetAsset is synchronous and returns null when the asset is not loaded. LoadAsync would
            // be better for streaming, but spawning must complete within the graph run, and an unloaded model
            // is a configuration error worth reporting rather than silently deferring.
            var model = Content.GetAsset(ModelId) as Model;
            if (model == null)
            {
                LastError = $"Model asset {ModelId} is not loaded or is not a Model.";
                Debug.LogWarning($"[PCG] {Id}: {LastError}");
                return input;
            }

            if (MaxActors <= 0)
            {
                LastError = "MaxActors is zero, so nothing can be spawned.";
                return input;
            }

            var parent = ResolveParent(context);
            var points = input.Points;
            var spawned = new List<int>(Mathf.Min(points.Length, MaxActors));

            for (int i = 0; i < points.Length; i++)
            {
                context.ThrowIfCancelled(i);

                if (spawned.Count >= MaxActors)
                {
                    LastRunWasCapped = true;
                    break;
                }

                var point = points[i];

                if (DensityThreshold > 0.0f)
                {
                    // Deterministic per-point draw so re-running the graph reproduces the same selection.
                    var rng = context.GetRng(i);
                    if (point.Density < rng.NextFloat())
                        continue;
                }

                // Derive a readable name from the asset path: Flax assets expose Path and TypeName but no
                // short Name, and scene objects named "asset-<guid>" are hard to work with in the outliner.
                // The shared prefix is what a PCGVolume uses to recognise and adopt this node's output.
                var modelLabel = string.IsNullOrEmpty(model.Path)
                    ? "Model"
                    : System.IO.Path.GetFileNameWithoutExtension(model.Path);

                var actor = new StaticModel
                {
                    Model = model,
                    Name = PCGSpawnNaming.GeneratedPrefix + modelLabel + "_" + i,
                };

                // Match the engine's own scale type rather than widening to Vector3, so the assignment is
                // exact and no precision is introduced.
                var scale = new Float3(
                    point.Scale.X * ScaleMultiplier,
                    point.Scale.Y * ScaleMultiplier,
                    point.Scale.Z * ScaleMultiplier);

                if (RandomYaw)
                {
                    // Yaw about the point's own up axis, keeping the surface alignment intact.
                    var yawRng = context.GetRng(i + 7919);
                    var axis = new Float3(0.0f, 1.0f, 0.0f);
                    var yaw = Quaternion.RotationAxis(axis, yawRng.NextRange(0.0f, Mathf.TwoPi));
                    actor.Transform = new Transform(point.Position, Quaternion.Multiply(yaw, point.Rotation), scale);
                }
                else
                {
                    actor.Transform = new Transform(point.Position, point.Rotation, scale);
                }

                if (!Level.SpawnActor(actor, parent))
                {
                    // SpawnActor reports failure by return value rather than throwing; ignoring it would
                    // silently drop objects.
                    Debug.LogWarning($"[PCG] {Id}: failed to spawn actor for point {i}.");
                    continue;
                }

                spawned.Add(i);
                SpawnedCount++;
            }

            if (LastRunWasCapped)
            {
                Debug.LogWarning(
                    $"[PCG] {Id}: stopped at MaxActors={MaxActors} of {points.Length} point(s). " +
                    "Raise MaxActors deliberately, or use a foliage spawner for dense placement.");
            }

            if (spawned.Count == 0)
                return PCGData.Empty;

            // Pass through the points that actually produced something, so a downstream node sees exactly
            // what exists in the scene rather than the pre-threshold set.
            if (spawned.Count == points.Length)
                return input;

            return PCGData.Gather(input, spawned.ToArray(), spawned.Count);
        }

        /// <summary>
        /// Resolves the actor that should own the spawned objects.
        /// </summary>
        /// <remarks>
        /// Parenting under one container keeps the generated result addressable, so a subsequent regenerate
        /// can find and clear exactly its own output instead of leaving orphans behind.
        /// </remarks>
        private static Actor ResolveParent(PCGExecutionContext context)
        {
            if (context.Source is Actor actor)
                return actor;

            return null;
        }
    }
}

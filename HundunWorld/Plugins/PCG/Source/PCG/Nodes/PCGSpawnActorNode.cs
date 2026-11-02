// PCG Plugin - Spawn Actor node
// Part of the FlaxEngine PCG plugin. See docs/PCG_PLUGIN_DESIGN.md §5.1.5.

using System;
using System.Collections.Generic;
using FlaxEngine;
using PCG.Execution;

namespace PCG.Nodes
{
    /// <summary>
    /// Spawns a prefab's actor hierarchy at every input point.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>What this adds over Spawn Static Model.</b> A static mesh is a visual; a prefab is a piece of
    /// gameplay - a campfire with its light, a pickup with its collider and interact script, an NPC spawn
    /// marker with its patrol config. An open world is populated with those, not with bare meshes, and this
    /// node is how a graph reaches them.
    /// </para>
    /// <para>
    /// <b>Spawning goes through <c>PrefabManager.SpawnPrefab(Prefab, Actor, Transform)</c></b>, the overload
    /// that takes the owning parent and the world transform together. Handing the parent to the engine rather
    /// than reparenting afterwards matters: a prefab root that is briefly unparented can run its own
    /// <c>OnStart</c> in the wrong place, and the volume's structural cleanup depends on the parent link being
    /// there from the first moment the actor exists.
    /// </para>
    /// <para>
    /// <b>Every spawned point is a real actor tree.</b> That is the cost of generality, so the ceiling is
    /// lower than the foliage spawner's and by default equals the static mesh spawner's: dense scatter belongs
    /// on <see cref="PCGSpawnFoliageNode"/>, this node is for objects whose count a designer can name.
    /// </para>
    /// </remarks>
    public sealed class PCGSpawnActorNode : PCGNodeBase
    {
        /// <summary>Stable type id.</summary>
        public const string Id = "SpawnActor";

        private const int PPrefabId = 0;
        private const int PDensityThreshold = 1;
        private const int PScaleMultiplier = 2;
        private const int PRandomYaw = 3;
        private const int PMaxActors = 4;

        /// <summary>Number of parameters this node reads.</summary>
        public const int ParameterCount = 5;

        /// <summary>Default ceiling on spawned actor trees in a single run.</summary>
        /// <remarks>
        /// Each point here can be a whole hierarchy, so this is deliberately not higher than the static mesh
        /// spawner's cap.
        /// </remarks>
        public const int DefaultMaxActors = 2000;

        /// <summary>
        /// How long an unloadable-but-present prefab may block the run.
        /// </summary>
        /// <remarks>
        /// Bounded and mirrored from the game module's own equipment path, which waits rather than skipping
        /// because a not-yet-loaded asset and a missing asset are different problems worth telling apart.
        /// </remarks>
        private const double LoadWaitMilliseconds = 10000.0;

        private readonly string _typeId;
        private readonly PCGNodeParameters _params;

        /// <summary>Asset id of the prefab to spawn.</summary>
        public Guid PrefabId;

        /// <summary>Points with a density below a random draw in [0,1] are skipped; 0 spawns at every point.</summary>
        public float DensityThreshold;

        /// <summary>Multiplier applied to each point's scale.</summary>
        public float ScaleMultiplier = 1.0f;

        /// <summary>When set, each instance gets a random yaw about its own up axis.</summary>
        public bool RandomYaw = true;

        /// <summary>Maximum actor trees this node may spawn in one run.</summary>
        public int MaxActors = DefaultMaxActors;

        /// <summary>Actors spawned by the most recent run.</summary>
        public int SpawnedCount { get; private set; }

        /// <summary>True when the most recent run stopped at <see cref="MaxActors"/>.</summary>
        public bool LastRunWasCapped { get; private set; }

        /// <summary>Creates the node from serialized parameters.</summary>
        public PCGSpawnActorNode(PCGNodeRegistry.PCGNodeSerializationData data)
        {
            _typeId = Id;
            _params = new PCGNodeParameters(data.Values, data.TypeId);

            PrefabId = _params.GetAssetId(PPrefabId);
            DensityThreshold = Mathf.Clamp(_params.Get(PDensityThreshold, 0.0f), 0.0f, 1.0f);
            ScaleMultiplier = Mathf.Max(0.0001f, _params.Get(PScaleMultiplier, 1.0f));
            RandomYaw = _params.Get(PRandomYaw, true);
            MaxActors = Mathf.Max(0, _params.Get(PMaxActors, DefaultMaxActors));
        }

        /// <inheritdoc />
        public override string TypeId => _typeId;

        /// <inheritdoc />
        public override string DisplayName => "Spawn Actor";

        /// <inheritdoc />
        public override bool IsThreadSafe => false;

        /// <inheritdoc />
        public override PCGPinDefinition[] InputPins => new[]
        {
            new PCGPinDefinition("In", PCGPinDirection.Input, isRequired: true,
                tooltip: "Points at which to spawn the prefab."),
        };

        /// <inheritdoc />
        public override PCGPinDefinition[] OutputPins => new[]
        {
            new PCGPinDefinition("Out", PCGPinDirection.Output,
                tooltip: "Points that were spawned, for chaining further output nodes."),
        };

        /// <inheritdoc />
        public override string DescribeParameters() =>
            $"prefab={(PrefabId == Guid.Empty ? "none" : PrefabId.ToString())}, " +
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

            if (PrefabId == Guid.Empty)
            {
                LastError = "No prefab is assigned.";

                // 干跑连日志都不打：`Debug.LogWarning` 也是引擎调用，离屏预览会因此崩掉。诊断不会丢——LastError
                // 由执行器收进本次运行的警告里，那本来就是"该由执行器报出来"的正主。
                if (!context.DryRun)
                    Debug.LogWarning($"[PCG] {Id}: {LastError}");

                return input;
            }

            if (MaxActors <= 0)
            {
                LastError = "MaxActors is zero, so nothing can be spawned.";
                return input;
            }

            // Preview and decider runs hand the layer on untouched: nothing is built, nothing has to be tracked
            // or cleaned up - and **no asset is resolved and none is waited for either**, which is why this sits
            // before the lookup below. A dry run that still loaded prefabs would make preview depend on asset
            // state, and would make headless dry runs impossible.
            if (context.DryRun)
                return input;

            // `Content.GetAsset` 只翻已经载入的资产池：没人先碰过的一个 prefab 会被读成"不是 prefab"。
            var prefab = PCGAssetLoading.Resolve<Prefab>(PrefabId);
            if (prefab == null)
            {
                LastError = $"Asset {PrefabId} is not loaded or is not a Prefab.";
                Debug.LogWarning($"[PCG] {Id}: {LastError}");
                return input;
            }

            if (!prefab.IsLoaded)
            {
                prefab.WaitForLoaded(LoadWaitMilliseconds);

                if (!prefab.IsLoaded)
                {
                    LastError = $"Prefab {LabelFor(prefab)} did not finish loading within {LoadWaitMilliseconds / 1000.0} s.";
                    Debug.LogWarning($"[PCG] {Id}: {LastError}");
                    return input;
                }
            }

            var parent = context.Source as Actor;
            var points = input.Points;
            var label = LabelFor(prefab);
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

                // A partitioned run samples a padded region so crowding can be decided across the border;
                // spawning the padding's objects as well would double up whatever the neighbouring cell also
                // spawns. Same rule the other spawners follow.
                if (!context.IsInsideClip(point.Position))
                    continue;

                if (DensityThreshold > 0.0f)
                {
                    // Deterministic per-point draw so re-running the graph reproduces the same selection.
                    var rng = context.GetRng(i);
                    if (point.Density < rng.NextFloat())
                        continue;
                }

                var scale = new Vector3(
                    point.Scale.X * ScaleMultiplier,
                    point.Scale.Y * ScaleMultiplier,
                    point.Scale.Z * ScaleMultiplier);

                var rotation = point.Rotation;
                if (RandomYaw)
                {
                    var yawRng = context.GetRng(i + 7919);
                    // Local, not world: a prop on a slope spins about its surface normal instead of being tipped
                    // off it.
                    rotation = PCGPointTransform.YawAboutLocalUp(point.Rotation, yawRng.NextRange(0.0f, Mathf.TwoPi));
                }

                var transform = new Transform(point.Position, rotation, scale);

                Actor actor;
                try
                {
                    actor = PrefabManager.SpawnPrefab(prefab, parent, transform);
                }
                catch (Exception ex)
                {
                    // One bad prefab instance must not abort the run: the points already spawned are real, and
                    // losing them silently behind an exception would be worse than a partial generation.
                    LastError = $"Spawning {label} at point {i} threw {ex.GetType().Name}: {ex.Message}";
                    Debug.LogWarning($"[PCG] {Id}: {LastError}");
                    break;
                }

                if (actor == null)
                {
                    // Reported rather than skipped quietly: an empty prefab, or a parent that is being torn
                    // down mid-run, both land here, and both look like "the graph produced nothing".
                    LastError = $"PrefabManager.SpawnPrefab returned no actor for point {i}.";
                    Debug.LogWarning($"[PCG] {Id}: {LastError}");
                    break;
                }

                // The shared prefix is what lets PCGVolume recognise and adopt this output; the label keeps the
                // outliner readable, where "actor-<guid>" would not be.
                // The cell token is what lets a regenerate destroy only the cells it is redoing, which in turn
                // is what makes reusing the other cells' points legitimate for a graph that writes into the scene.
                actor.Name = PCGSpawnNaming.ActorName(label, i, context.LayerIndex,
                          context.ClipCell.HasValue ? context.ClipCell.Value.IndexX : 0,
                          context.ClipCell.HasValue ? context.ClipCell.Value.IndexZ : 0);

                spawned.Add(i);
                SpawnedCount++;
            }

            if (LastRunWasCapped)
            {
                Debug.LogWarning(
                    $"[PCG] {Id}: stopped at MaxActors={MaxActors} of {points.Length} point(s). " +
                    "Raise MaxActors deliberately, or move dense scatter onto Spawn Foliage.");
            }

            if (spawned.Count == 0)
                return PCGData.Empty;

            if (spawned.Count == points.Length)
                return input;

            return PCGData.Gather(input, spawned.ToArray(), spawned.Count);
        }

        private static string LabelFor(Asset asset) =>
            string.IsNullOrEmpty(asset.Path) ? "Prefab" : System.IO.Path.GetFileNameWithoutExtension(asset.Path);
    }
}

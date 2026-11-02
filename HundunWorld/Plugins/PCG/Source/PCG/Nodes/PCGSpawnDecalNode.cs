// PCG Plugin - Spawn Decal node
// Part of the FlaxEngine PCG plugin. See docs/PCG_PLUGIN_DESIGN.md §5.1.5 and §10.

using System;
using System.Collections.Generic;
using FlaxEngine;
using PCG.Execution;

namespace PCG.Nodes
{
    /// <summary>
    /// Spawns a projected decal at every input point.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>What it is for.</b> Ground scatter that should not be geometry: moss patches, scorch marks, cracks,
    /// blood, path blending, the "this rock sits in the dirt" contact shadow. An MMO uses these to hide the
    /// seams between hand-placed props and procedural ground, which is exactly where a purely volumetric
    /// scatter looks worst.
    /// </para>
    /// <para>
    /// <b>Orientation comes from the point, not from a fresh terrain query.</b> A decal projects along its own
    /// -Y, so its actor's up axis has to be the surface normal. The landscape sampler already builds that frame
    /// when <c>Align To Normal</c> is set - which is its default - and the graph's slope filtering was decided
    /// from that same sample. Re-sampling the terrain here would be a second opinion on the very quantity the
    /// filters already voted on, so the node trusts the point it was handed. A decal placed from an unaligned
    /// sampler will lie flat in world space rather than hug the surface; that is the upstream node's contract,
    /// not this one's.
    /// </para>
    /// <para>
    /// <b>One actor per decal, so the count is capped low.</b> Flax has no instanced-decal path, which is the
    /// real reason this node arrived later than the others - not that it could not be written, but that it
    /// cannot be made cheap. The ceiling is a per-run guard, and hitting it is reported instead of silent.
    /// </para>
    /// <para>
    /// <b>The material must be a Decal-domain material</b>, checked here at run time through
    /// <c>MaterialBase.IsDecal</c> and reported with the asset's own path. There is no compile-time hook for
    /// asset domains, so the earliest point a designer can be told is the first run, and that is what this does.
    /// </para>
    /// </remarks>
    public sealed class PCGSpawnDecalNode : PCGNodeBase
    {
        /// <summary>Stable type id.</summary>
        public const string Id = "SpawnDecal";

        private const int PMaterialId = 0;
        private const int PDensityThreshold = 1;
        private const int PSize = 2;
        private const int PSurfaceOffset = 3;
        private const int PSortOrder = 4;
        private const int PMaxDecals = 5;
        private const int PRandomYaw = 6;

        /// <summary>Number of parameters this node reads.</summary>
        public const int ParameterCount = 7;

        /// <summary>Default ceiling on decals in a single run; each one is a scene actor.</summary>
        public const int DefaultMaxDecals = 1000;

        private readonly string _typeId;
        private readonly PCGNodeParameters _params;

        /// <summary>Asset id of the decal material.</summary>
        public Guid MaterialId;

        /// <summary>Points with a density below a random draw in [0,1] are skipped; 0 places at every point.</summary>
        public float DensityThreshold;

        /// <summary>Decal extent in its own X/Y/Z, in world units. Y is the projection depth.</summary>
        public Vector3 Size = new Vector3(100.0f, 100.0f, 100.0f);

        /// <summary>
        /// How far the decal is lifted along the surface normal. Zero makes the projector box sit exactly on
        /// the ground, where it z-fights with it; a few units is what ground decals need.
        /// </summary>
        public float SurfaceOffset = 5.0f;

        /// <summary>Decal sort order; higher wins where decals overlap.</summary>
        public int SortOrder;

        /// <summary>Maximum decals this node may spawn in one run.</summary>
        public int MaxDecals = DefaultMaxDecals;

        /// <summary>
        /// When set, each decal is spun about the surface normal. Off by default, unlike the mesh spawners:
        /// here the value rotates the projected texture, which is a look decision a designer makes on purpose
        /// rather than variation that is wanted by reflex.
        /// </summary>
        public bool RandomYaw;

        /// <summary>Decals spawned by the most recent run.</summary>
        public int SpawnedCount { get; private set; }

        /// <summary>True when the most recent run stopped at <see cref="MaxDecals"/>.</summary>
        public bool LastRunWasCapped { get; private set; }

        /// <summary>Creates the node from serialized parameters.</summary>
        public PCGSpawnDecalNode(PCGNodeRegistry.PCGNodeSerializationData data)
        {
            _typeId = Id;
            _params = new PCGNodeParameters(data.Values, data.TypeId);

            MaterialId = _params.GetAssetId(PMaterialId);
            DensityThreshold = Mathf.Clamp(_params.Get(PDensityThreshold, 0.0f), 0.0f, 1.0f);
            Size = _params.GetVector3(PSize, new Vector3(100.0f, 100.0f, 100.0f));
            SurfaceOffset = _params.Get(PSurfaceOffset, 5.0f);
            SortOrder = _params.Get(PSortOrder, 0);
            MaxDecals = Mathf.Max(0, _params.Get(PMaxDecals, DefaultMaxDecals));
            RandomYaw = _params.Get(PRandomYaw, false);
        }

        /// <inheritdoc />
        public override string TypeId => _typeId;

        /// <inheritdoc />
        public override string DisplayName => "Spawn Decal";

        /// <inheritdoc />
        public override bool IsThreadSafe => false;

        /// <inheritdoc />
        public override PCGPinDefinition[] InputPins => new[]
        {
            new PCGPinDefinition("In", PCGPinDirection.Input, isRequired: true,
                tooltip: "Points at which to project decals."),
        };

        /// <inheritdoc />
        public override PCGPinDefinition[] OutputPins => new[]
        {
            new PCGPinDefinition("Out", PCGPinDirection.Output,
                tooltip: "Points that were decalled, for chaining further output nodes."),
        };

        /// <inheritdoc />
        public override string DescribeParameters() =>
            $"material={(MaterialId == Guid.Empty ? "none" : MaterialId.ToString())}, " +
            $"size={Size}, surfaceOffset={SurfaceOffset:F1}, sortOrder={SortOrder}, " +
            $"maxDecals={MaxDecals}, randomYaw={RandomYaw}";

        /// <inheritdoc />
        public override PCGData OnExecute(PCGExecutionContext context, PCGData[] inputs)
        {
            SpawnedCount = 0;
            LastRunWasCapped = false;
            LastError = null;

            var input = inputs[0];
            if (input == null || input.Count == 0)
                return PCGData.Empty;

            if (MaterialId == Guid.Empty)
            {
                LastError = "No decal material is assigned.";

                // 干跑连日志都不打：`Debug.LogWarning` 也是引擎调用，离屏预览会因此崩掉。诊断不会丢——LastError
                // 由执行器收进本次运行的警告里，那本来就是"该由执行器报出来"的正主。
                if (!context.DryRun)
                    Debug.LogWarning($"[PCG] {Id}: {LastError}");

                return input;
            }

            if (MaxDecals <= 0)
            {
                LastError = "MaxDecals is zero, so nothing can be spawned.";
                return input;
            }

            // Preview and decider runs hand the layer on untouched: nothing is projected, nothing has to be
            // cleaned up - and **no asset is resolved either**, which is why this sits before the lookup below.
            // A dry run that still needed the asset database would make preview depend on whether the material
            // happens to be loaded, and would make headless dry runs impossible.
            if (context.DryRun)
                return input;

            // `Content.GetAsset` 只翻已经载入的资产池：还没被加载的一块贴花材质会被读成"不是材质"。
            var material = PCGAssetLoading.Resolve<MaterialBase>(MaterialId);
            if (material == null)
            {
                LastError = $"Material asset {MaterialId} is not loaded or is not a Material.";
                Debug.LogWarning($"[PCG] {Id}: {LastError}");
                return input;
            }

            // The engine draws a non-decal material nowhere on a Decal actor, so a graph that "worked" with the
            // wrong asset would show nothing at all and leave the designer hunting the sampler.
            if (!material.IsDecal)
            {
                LastError = $"{LabelFor(material)} is not a Decal-domain material; " +
                            "set the material's Domain to Decal or point this node at a decal material.";
                Debug.LogError($"[PCG] {Id}: {LastError}");
                return input;
            }

            var parent = context.Source as Actor;
            var points = input.Points;
            var label = LabelFor(material);
            var spawned = new List<int>(Mathf.Min(points.Length, MaxDecals));

            // 与另外三个生成器同一把尺子：零产出时必须能报出**每个数各去了哪里**，还要摆上"这一层本来期望几片"
            // （密度合计就是期望件数）。判据只写"进来了 N 个点"会把密度问题指到裁切或材质上去，照着查白查一轮。
            int skippedOutsideClip = 0;
            int skippedByDensity = 0;
            float expectedPlacementSum = 0.0f;

            for (int i = 0; i < points.Length; i++)
            {
                context.ThrowIfCancelled(i);

                if (spawned.Count >= MaxDecals)
                {
                    LastRunWasCapped = true;
                    break;
                }

                var point = points[i];

                // Same padding rule as the other spawners: a partitioned run may see across the border, but
                // only the cell's own points are placed.
                if (!context.IsInsideClip(point.Position))
                {
                    skippedOutsideClip++;
                    continue;
                }

                // 尺子按"落在被生成区域里的点"累加，与是否抽中无关——它要回答的是"这一层本来该有几片"。
                expectedPlacementSum += point.Density;

                if (DensityThreshold > 0.0f)
                {
                    var rng = context.GetRng(i);
                    if (point.Density < rng.NextFloat())
                    {
                        skippedByDensity++;
                        continue;
                    }
                }

                var rotation = point.Rotation;
                if (RandomYaw)
                {
                    var yawRng = context.GetRng(i + 7919);
                    // Local, not world: a decal on a slope spins about its surface normal instead of being tipped
                    // off it.
                    rotation = PCGPointTransform.YawAboutLocalUp(point.Rotation, yawRng.NextRange(0.0f, Mathf.TwoPi));
                }

                // Lifted along the point's own up axis, which is the surface normal for an aligned point. In
                // world Y the same offset would float decals off vertical rock faces and bury them on slopes.
                var offset = Vector3.Transform(new Vector3(0.0f, SurfaceOffset, 0.0f), rotation);

                var decal = new Decal
                {
                    Name = PCGSpawnNaming.ActorName(label, i, context.LayerIndex,
                          context.ClipCell.HasValue ? context.ClipCell.Value.IndexX : 0,
                          context.ClipCell.HasValue ? context.ClipCell.Value.IndexZ : 0),
                    Material = material,
                    Size = Size,
                    SortOrder = SortOrder,
                    Transform = new Transform(point.Position + offset, rotation, Vector3.One),
                };

                if (!PCGActors.TrySpawn(decal, parent))
                {
                    // Reported and stopped rather than skipped: half a scatter appearing with no message is worse
                    // than a generation that says where it broke.
                    LastError = $"Failed to spawn the decal actor at point {i}.";
                    Debug.LogWarning($"[PCG] {Id}: {LastError}");
                    break;
                }

                spawned.Add(i);
                SpawnedCount++;
            }

            if (LastRunWasCapped)
            {
                Debug.LogWarning(
                    $"[PCG] {Id}: stopped at MaxDecals={MaxDecals} of {points.Length} point(s). " +
                    "Flax has no instanced decal path, so raise this deliberately.");
            }

            if (spawned.Count == 0)
            {
                LastError = $"Spawn Decal placed nothing from {points.Length} point(s): " +
                            $"{skippedOutsideClip} fell outside the region being generated, " +
                            $"{skippedByDensity} were dropped by the density draw (threshold " +
                            $"{DensityThreshold:0.###}); the points inside the region together expected " +
                            $"{expectedPlacementSum:0.###} decal(s)." +
                            (skippedOutsideClip + skippedByDensity == 0
                                ? " Nothing was skipped here, so the input carried no placeable point at all."
                                : string.Empty);

                // 干跑不打日志：`Debug.LogWarning` 是引擎调用，离屏预览会因此崩掉；诊断不丢，它在 LastError 里。
                if (!context.DryRun)
                    Debug.LogWarning($"[PCG] {Id}: {LastError}");

                return PCGData.Empty;
            }

            // 摆出来了**不等于这一层长出来了**：期望不足一片时这一次抽签站到了作者这边，换个种子整层就没了，
            // 而屏幕上只是"稀"，没人会去查。与另外三个生成器同一条判据、同一种说法。
            if (DensityThreshold > 0.0f && expectedPlacementSum < 1.0f && !LastRunWasCapped)
            {
                LastError = $"Spawn Decal placed {spawned.Count} decal(s) from {points.Length} point(s), but this " +
                            $"layer expected only {expectedPlacementSum:0.###} decal(s) in total, so whether it " +
                            "shows up at all is one coin flip. Raise points / per-block, or raise density.";

                if (!context.DryRun)
                    Debug.LogWarning($"[PCG] {Id}: {LastError}");
            }

            if (spawned.Count == points.Length)
                return input;

            return PCGData.Gather(input, spawned.ToArray(), spawned.Count);
        }

        private static string LabelFor(Asset asset) =>
            string.IsNullOrEmpty(asset.Path) ? "Material" : System.IO.Path.GetFileNameWithoutExtension(asset.Path);
    }
}

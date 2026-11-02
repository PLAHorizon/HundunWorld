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

            // Preview and decider runs hand the layer on untouched: no models are placed, and - this is the point
            // of putting it before the lookup below - **no asset is resolved either**. A dry run that still needed
            // the asset database would make "preview" depend on whether the model happens to be loaded, and would
            // make it impossible to dry-run a graph headlessly at all.
            if (context.DryRun)
                return input;

            // 生成必须在一次图运行里做完，所以这里同步等；但"还没被加载"不等于"不存在"——
            // `Content.GetAsset` 只翻已经载入的资产池，冷启动那一次会把一个好模型判成配置错误。
            var model = PCGAssetLoading.Resolve<Model>(ModelId);
            if (model == null)
            {
                LastError = $"Model asset {ModelId} is not loaded or is not a Model.";
                Debug.LogWarning($"[PCG] {Id}: {LastError}");
                return input;
            }

            var parent = ResolveParent(context);
            var points = input.Points;
            var spawned = new List<int>(Mathf.Min(points.Length, MaxActors));

            // 名字要从资产路径里取：Flax 资产只暴露 Path 与 TypeName，没有短 Name，而叫
            // "asset-<guid>" 的场景对象在大纲里几乎没法用。共享前缀是 PCGVolume 认领本节点
            // 产出所依据的记号。`model` 在一趟循环里是不变的，所以这里算一次：
            // 放进循环就是每个点两次字符串分配。
            var modelLabel = string.IsNullOrEmpty(model.Path)
                ? "Model"
                : System.IO.Path.GetFileNameWithoutExtension(model.Path);

            // 三道筛子各数一遍：最后的读数要说清"是谁把点吃光的"，只说"摆了 0 件"等于没说。
            int skippedOutsideClip = 0;
            int skippedByDensity = 0;

            // 落进裁切区那些点的**密度之和**。抽签是 `Density >= rng.NextFloat()`，
            // 所以这一个数就是"这一层期望摆出几件"——它比"实际摆了几件"更早说话：
            // 期望不足一件时这一层多半就是空的，而屏幕上看不出来是"没长"还是"长稀"。
            float expectedPlacementSum = 0.0f;

            for (int i = 0; i < points.Length; i++)
            {
                context.ThrowIfCancelled(i);

                if (spawned.Count >= MaxActors)
                {
                    LastRunWasCapped = true;
                    break;
                }

                var point = points[i];

                // A partitioned run executes over a padded region so crowding can be decided across the
                // border; spawning the padding's points as well would drop duplicates into the neighbouring
                // cell, which will spawn its own.
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

                var actor = new StaticModel
                {
                    Model = model,
                    Name = PCGSpawnNaming.ActorName(modelLabel, i, context.LayerIndex,
                          context.ClipCell.HasValue ? context.ClipCell.Value.IndexX : 0,
                          context.ClipCell.HasValue ? context.ClipCell.Value.IndexZ : 0),
                };

                // Match the engine's own scale type rather than widening to Vector3, so the assignment is
                // exact and no precision is introduced.
                var scale = new Float3(
                    point.Scale.X * ScaleMultiplier,
                    point.Scale.Y * ScaleMultiplier,
                    point.Scale.Z * ScaleMultiplier);

                if (RandomYaw)
                {
                    // Composed in the point's own frame, so a prop on a slope spins about its surface normal
                    // instead of being tipped off it.
                    var yawRng = context.GetRng(i + 7919);
                    actor.Transform = new Transform(
                        point.Position,
                        PCGPointTransform.YawAboutLocalUp(point.Rotation, yawRng.NextRange(0.0f, Mathf.TwoPi)),
                        scale);
                }
                else
                {
                    actor.Transform = new Transform(point.Position, point.Rotation, scale);
                }

                if (!PCGActors.TrySpawn(actor, parent))
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

            // **非空的输入摆出 0 件**是这一层最难查的失败：图上看不出区别，日志也看不出区别，
            // 设计者只会看到一片空场景（实测某张图 909 个点、0 件、0 条警告）。
            // 生成失败的逐点警告上面已经一条条打过，所以这里要补的是"被筛子吃掉"这一类——它此前完全无声。
            if (spawned.Count == 0 && points.Length > 0 && !LastRunWasCapped)
            {
                LastError = $"Spawned nothing from {points.Length} point(s): {skippedOutsideClip} fell outside " +
                            $"the region being generated, {skippedByDensity} were dropped by the density draw " +
                            $"(threshold {DensityThreshold:0.###}); the whole layer expected only " +
                            $"{expectedPlacementSum:0.###} prop(s). A layer that yields no props usually means the " +
                            "density column is zero, the sampler put the points elsewhere, or the clip region " +
                            "does not cover them; if both counts are zero, look for the per-point spawn failures " +
                            "above.";
                Debug.LogWarning($"[PCG] {Id}: {LastError}");
            }
            else if (spawned.Count > 0 && DensityThreshold > 0.0f && expectedPlacementSum < 1.0f &&
                     !LastRunWasCapped)
            {
                // 摆出了东西**不等于这一层长出来了**：期望不足一件时这一次抽签站到了作者这边，
                // 换一个种子、挪一下体积就整层消失——而屏幕上只是"稀"，没人会去查。
                // 演示里那两条"路肩灌木/林带"就是这个形状：22 个点、期望 0.61 件、引擎里 0 件。
                LastError = $"Placed {spawned.Count} prop(s) from {points.Length} point(s), but this layer " +
                            $"expected only {expectedPlacementSum:0.###} prop(s) in total, so whether it shows up " +
                            "at all is one coin flip. Raise points / per-block, or raise density.";
                Debug.LogWarning($"[PCG] {Id}: {LastError}");
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

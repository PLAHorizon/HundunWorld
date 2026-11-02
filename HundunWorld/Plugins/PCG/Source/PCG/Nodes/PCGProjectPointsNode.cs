// PCG Plugin - Project Points node
// Part of the FlaxEngine PCG plugin. See docs/PCG_PLUGIN_DESIGN.md §5.1.24.

using System;
using System.Collections.Generic;
using FlaxEngine;
using PCG.Execution;
using PCG.TerrainSampling;

namespace PCG.Nodes
{
    /// <summary>
    /// Drops existing points onto the terrain surface - the step between "where a layer is" and "what the ground
    /// is actually at".
    /// </summary>
    /// <remarks>
    /// <para>
    /// The landscape sampler <i>invents</i> points by sampling a grid, which is the wrong tool once the points
    /// already exist: a hand-placed row, a noise field, a layer that was moved sideways. What those need is the
    /// surface query applied to <i>their</i> positions, keeping their count, order, seeds and columns. This is
    /// UE5's projection step, and it is the difference between "scatter over this area" and "put these exactly
    /// here, on the ground".
    /// </para>
    /// <para>
    /// <b>It moves, it does not filter.</b> A point with no terrain under it is left exactly where it was and
    /// only counted, because a projection that silently deleted the half of a layer that hangs over a lake would
    /// be indistinguishable from a layer that was never placed. <c>Drop Misses</c> is there for the graphs that
    /// do want the deletion, and it says so in its name.
    /// </para>
    /// <para>
    /// <b>Rotation and slope.</b> With alignment on, the point's up follows the surface via
    /// <see cref="PCGSurfaceAlign"/> - the same helper the landscape sampler uses, so a projected layer and a
    /// sampled one sit identically. The sampled slope is written into the <c>$Slope</c> column for the same
    /// reason the sampler writes it: a downstream slope filter should not have to ask the terrain again.
    /// </para>
    /// <para>
    /// <b>Bounds follow the point.</b> A point's box is stored in world space, so moving the position without
    /// moving the box would leave every downstream pruning decision testing a rectangle the point has left.
    /// </para>
    /// </remarks>
    public sealed class PCGProjectPointsNode : PCGNodeBase
    {
        /// <summary>Stable type id.</summary>
        public const string Id = "ProjectPoints";

        // Parameter indices. Order is part of the saved graph format; append only.
        private const int PSampleMode = 0;
        private const int PFallbackToRayCast = 1;
        private const int PHeightOffset = 2;
        private const int PAlignToNormal = 3;
        private const int PDropMisses = 4;

        /// <summary>Number of parameters this node reads.</summary>
        public const int ParameterCount = 5;

        private readonly string _typeId;
        private readonly PCGNodeParameters _params;

        /// <summary>Which terrain query answers the projection.</summary>
        public PCGTerrainSampleMode SampleMode = PCGTerrainSampleMode.RayCast;

        /// <summary>Whether an unusable heightfield falls back to a ray cast.</summary>
        public bool FallbackToRayCast = true;

        /// <summary>Lift applied after the point lands, so a prop can sit proud of the ground.</summary>
        public float HeightOffset;

        /// <summary>When set, the point's up follows the surface normal.</summary>
        public bool AlignToNormal;

        /// <summary>When set, points with no terrain under them are removed instead of being left alone.</summary>
        public bool DropMisses;

        /// <summary>Points moved by the last run.</summary>
        public int ProjectedCount;

        /// <summary>Points the last run found no terrain for.</summary>
        public int MissedCount;

        /// <summary>Creates the node from serialized parameters.</summary>
        public PCGProjectPointsNode(PCGNodeRegistry.PCGNodeSerializationData data)
        {
            _typeId = Id;
            _params = new PCGNodeParameters(data.Values, data.TypeId);

            SampleMode = _params.Get(PSampleMode, PCGTerrainSampleMode.RayCast);
            FallbackToRayCast = _params.Get(PFallbackToRayCast, true);
            HeightOffset = _params.Get(PHeightOffset, 0.0f);
            AlignToNormal = _params.Get(PAlignToNormal, false);
            DropMisses = _params.Get(PDropMisses, false);
        }

        /// <inheritdoc />
        public override string TypeId => _typeId;

        /// <inheritdoc />
        public override string DisplayName => "Project Points";

        /// <inheritdoc />
        /// <remarks>
        /// False because the ray-cast path goes through the engine's geometry query layer, the same reason the
        /// landscape sampler is not declared thread safe.
        /// </remarks>
        public override bool IsThreadSafe => false;

        /// <inheritdoc />
        public override PCGPinDefinition[] InputPins => new[]
        {
            new PCGPinDefinition("In", PCGPinDirection.Input, isRequired: true,
                tooltip: "Points to drop onto the terrain."),
        };

        /// <inheritdoc />
        public override PCGPinDefinition[] OutputPins => new[]
        {
            new PCGPinDefinition("Out", PCGPinDirection.Output,
                tooltip: "The same points, moved onto the surface."),
        };

        /// <inheritdoc />
        public override string DescribeParameters() =>
            $"mode={SampleMode}, offset={HeightOffset:F1}, align={AlignToNormal}, dropMisses={DropMisses}";

        /// <inheritdoc />
        public override PCGData OnExecute(PCGExecutionContext context, PCGData[] inputs)
        {
            ProjectedCount = 0;
            MissedCount = 0;
            LastError = null;

            var input = inputs != null && inputs.Length > 0 ? inputs[0] : null;
            if (input == null || input.Count == 0)
                return PCGData.Empty;

            if (context.World == null || !context.World.HasTerrain)
            {
                LastError = "Project Points found no terrain in the execution context. " +
                            "The layer passes through unchanged.";
                return input.Clone();
            }

            var result = input.Clone();
            var attributes = EnsureAttributes(result);

            if (!attributes.Contains(PCGAttributeSet.SlopeName))
                attributes.DeclareAttributeUnsafe(PCGAttributeSet.SlopeName, PCGAttributeType.Float);

            var slopes = attributes.GetWritableFloatArray(PCGAttributeSet.SlopeName);
            var points = result.Points;

            // One sampler per terrain, built once: a sampler caches the patch geometry it walks, so building one
            // per point would throw that away on every iteration.
            var samplers = new Dictionary<Guid, PCGTerrainSampler>();
            var keep = DropMisses ? new List<int>(points.Length) : null;

            for (int i = 0; i < points.Length; i++)
            {
                context.ThrowIfCancelled(i);

                var position = points[i].Position;
                var terrain = context.World.FindTerrainAt(position);

                bool moved = false;

                if (terrain != null)
                {
                    if (!samplers.TryGetValue(terrain.ID, out var sampler))
                    {
                        sampler = new PCGTerrainSampler(terrain, PCGTerrainCalibrationStore.Get(terrain));
                        samplers.Add(terrain.ID, sampler);
                    }

                    // Start the cast above both the point and sea level: a point placed below the surface has to
                    // be pulled up to it, not missed because the origin was already underground.
                    float startY = MathF.Max(position.Y, 0.0f) + 1000.0f;
                    var sample = sampler.Sample(position.X, position.Z, SampleMode, startY, FallbackToRayCast);

                    if (sample.Hit)
                    {
                        var half = PCGPointTransform.HalfExtents(points[i].Bounds);

                        points[i].Position = new Vector3(position.X, sample.Height + HeightOffset, position.Z);

                        // Rebuilt rather than passed to UpdateBounds: that helper re-applies the point's own
                        // transform, so a tilted point would have its box enclosed a second time on the way in.
                        points[i].Bounds = PCGPointTransform.MakeBox(points[i].Position, half);

                        if (AlignToNormal)
                            points[i].Rotation = PCGSurfaceAlign.RotationFromNormal(sample.Normal);

                        slopes[i] = sample.SlopeDegrees;
                        moved = true;
                        ProjectedCount++;
                    }
                }

                if (moved)
                {
                    keep?.Add(i);
                    continue;
                }

                // No terrain here: the point stays exactly as it was, or leaves the layer when asked to.
                MissedCount++;
            }

            if (keep == null)
                return result;

            if (keep.Count == points.Length)
                return result;

            if (keep.Count == 0)
            {
                LastError = "Project Points dropped every point: no terrain was found under any of them.";
                return PCGData.Empty;
            }

            // Gather keeps the rows in point order and carries every column, $Slope included.
            return PCGData.Gather(result, keep.ToArray(), keep.Count);
        }

        /// <summary>The data set's attribute storage, created and stamped onto its points when absent.</summary>
        private static PCGAttributeSet EnsureAttributes(PCGData data)
        {
            var attributes = data.SharedAttributes;

            if (attributes == null)
            {
                attributes = new PCGAttributeSet(data.Count);
                data.SharedAttributes = attributes;

                for (int i = 0; i < data.Points.Length; i++)
                    data.Points[i].Attributes = attributes;
            }

            return attributes;
        }
    }
}

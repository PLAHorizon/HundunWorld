// PCG Plugin - Ray Cast Project (Modifier)
// Part of the FlaxEngine PCG plugin. See docs/PCG_PLUGIN_DESIGN.md §5.1.30.

using System;
using System.Collections.Generic;
using FlaxEngine;
using PCG.Execution;

namespace PCG.Nodes
{
    /// <summary>
    /// Drops each point onto whatever the scene's physics finds under it - meshes and props, not only terrain.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="PCGProjectPointsNode"/> asks the terrain heightfield, which is cheap and exact but blind to
    /// everything that is not terrain: a road deck, a bridge, a roof, a rock or a crate is not in the heightfield,
    /// so a point meant to sit on it either lands on the ground underneath or misses entirely. This node asks the
    /// physics scene instead, so a scatter can be snapped to real geometry - UE5's "world ray hit query" next to
    /// its terrain projection.
    /// </para>
    /// <para>
    /// The two are deliberately separate rather than two modes of one node: the heightfield path needs no physics
    /// and works in a headless run, and the two answer different questions ("where is the ground" versus "what is
    /// the first solid thing along this ray"). Their parameters overlap but their failure modes do not, and a
    /// designer picking one should be able to see which they picked.
    /// </para>
    /// <para>
    /// The hit is looked up through <see cref="PCGWorldContext.RayCaster"/>, which the run installs. A run with no
    /// caster - a headless test, or a scene with no physics - reports that and passes its input through untouched,
    /// the same contract every other world-dependent node follows.
    /// </para>
    /// <para>
    /// A miss is not an error by default: the point keeps the position it was authored with, so a layer whose
    /// points partly hang over the edge of a platform stays countable and visible. <c>Drop Misses</c> is how a
    /// designer says the misses are unwanted.
    /// </para>
    /// </remarks>
    public sealed class PCGWorldRayProjectNode : PCGNodeBase
    {
        /// <summary>Stable type id.</summary>
        public const string Id = "RayCastProject";

        // Parameter indices. Order is part of the saved graph format; append only.
        private const int PLayerMask = 0;
        private const int PHitTriggers = 1;
        private const int PDirection = 2;
        private const int PMaxDistance = 3;
        private const int PHeightOffset = 4;
        private const int PAlignToNormal = 5;
        private const int PDropMisses = 6;

        /// <summary>Number of parameters this node reads.</summary>
        public const int ParameterCount = 7;

        private readonly string _typeId;
        private readonly PCGNodeParameters _params;

        /// <summary>Collision layers the ray may hit; -1 means every layer.</summary>
        public int LayerMask = -1;

        /// <summary>When set, trigger colliders count as hits.</summary>
        public bool HitTriggers;

        /// <summary>Direction the ray travels. Normalised before use; zero falls back to straight down.</summary>
        public Vector3 Direction = new Vector3(0.0f, -1.0f, 0.0f);

        /// <summary>How far a ray may travel before it is called a miss.</summary>
        public float MaxDistance = 10000.0f;

        /// <summary>Lift along the hit normal, so a point starts just off the surface rather than inside it.</summary>
        public float HeightOffset;

        /// <summary>When set, each hit point is rotated so its up matches the surface normal.</summary>
        public bool AlignToNormal = true;

        /// <summary>When set, points that hit nothing are removed instead of being left where they were.</summary>
        public bool DropMisses;

        /// <summary>How many points a ray landed on in the last run.</summary>
        public int HitCount { get; private set; }

        /// <summary>How many rays found nothing in the last run.</summary>
        public int MissedCount { get; private set; }

        /// <summary>Creates the node from serialized parameters.</summary>
        public PCGWorldRayProjectNode(PCGNodeRegistry.PCGNodeSerializationData data)
        {
            _typeId = Id;
            _params = new PCGNodeParameters(data.Values, data.TypeId);

            LayerMask = _params.Get(PLayerMask, -1);
            HitTriggers = _params.Get(PHitTriggers, false);
            Direction = _params.GetVector3(PDirection, new Vector3(0.0f, -1.0f, 0.0f));
            MaxDistance = Math.Max(0.0f, _params.Get(PMaxDistance, 10000.0f));
            HeightOffset = _params.Get(PHeightOffset, 0.0f);
            AlignToNormal = _params.Get(PAlignToNormal, true);
            DropMisses = _params.Get(PDropMisses, false);
        }

        /// <inheritdoc />
        public override string TypeId => _typeId;

        /// <inheritdoc />
        public override string DisplayName => "Ray Cast Project";

        /// <inheritdoc />
        public override PCGPinDefinition[] InputPins => new[]
        {
            new PCGPinDefinition("In", PCGPinDirection.Input, isRequired: true,
                tooltip: "Points to snap onto the scene's geometry."),
        };

        /// <inheritdoc />
        public override PCGPinDefinition[] OutputPins => new[]
        {
            new PCGPinDefinition("Out", PCGPinDirection.Output,
                tooltip: "The same points, placed on whatever the ray found."),
        };

        /// <inheritdoc />
        public override string DescribeParameters() =>
            $"rays {Direction} over {MaxDistance:F0}, mask {LayerMask}, offset {HeightOffset:F1}, " +
            $"align={AlignToNormal}, dropMisses={DropMisses}";

        /// <inheritdoc />
        public override PCGData OnExecute(PCGExecutionContext context, PCGData[] inputs)
        {
            HitCount = 0;
            MissedCount = 0;
            LastError = null;

            var input = inputs != null && inputs.Length > 0 ? inputs[0] : null;
            if (input == null || input.Count == 0)
                return PCGData.Empty;

            var world = context.World;

            if (world == null || !world.HasRayCaster)
            {
                LastError = "Ray Cast Project found no physics to cast against in the execution context. " +
                            "The layer passes through unchanged.";
                return input.Clone();
            }

            // A zero direction would cast nowhere; the intent behind a zero vector is always "straight down".
            var direction = Direction;
            float length = direction.Length;
            direction = length > 1e-6f ? direction / length : new Vector3(0.0f, -1.0f, 0.0f);

            var result = input.Clone();

            // Clone() copies the fields per point but shares the attribute storage, so the storage is detached
            // before anything is written to it.
            var attributes = EnsureAttributes(result);

            if (!attributes.Contains(PCGAttributeSet.SlopeName))
                attributes.DeclareAttributeUnsafe(PCGAttributeSet.SlopeName, PCGAttributeType.Float);

            var slopes = attributes.GetWritableFloatArray(PCGAttributeSet.SlopeName);
            var points = result.Points;
            var keep = DropMisses ? new List<int>(points.Length) : null;

            for (int i = 0; i < points.Length; i++)
            {
                context.ThrowIfCancelled(i);

                var point = points[i];

                if (world.TryRayCast(point.Position, direction, MaxDistance, ToMask(LayerMask), HitTriggers,
                        out var hit))
                {
                    var normal = NormalOf(hit);
                    var half = PCGPointTransform.HalfExtents(point.Bounds);

                    // Lifted along the surface normal rather than along world up: on a slope, up leaves the point
                    // half buried, and the whole point of a hit normal is that it says which way is out.
                    points[i].Position = hit.Point + normal * HeightOffset;
                    points[i].Bounds = PCGPointTransform.MakeBox(points[i].Position, half);

                    if (AlignToNormal)
                        points[i].Rotation = PCGSurfaceAlign.RotationFromNormal(normal);

                    slopes[i] = PCGPointTransform.SlopeDegrees(points[i]);
                    HitCount++;
                    keep?.Add(i);
                }
                else
                {
                    // Added only when dropping is on: an unconditional add here is how "drop misses" ends up
                    // dropping nothing while still looking implemented.
                    MissedCount++;
                }
            }

            if (keep == null)
                return result;

            return PCGData.Gather(result, keep.ToArray(), keep.Count);
        }

        /// <summary>An unset mask means every layer, which is what the engine spells <c>uint.MaxValue</c>.</summary>
        private static uint ToMask(int mask) => mask < 0 ? uint.MaxValue : (uint)mask;

        /// <summary>
        /// The hit's surface normal, or world up when the collider reported none.
        /// </summary>
        /// <remarks>
        /// A zero normal is what a collider with no face (a sphere's default, or a hit against a collider type
        /// that does not carry one) returns. Feeding that to the alignment helper would rotate the point to
        /// something arbitrary, and feeding it to the offset would put the point back on the surface - so up is
        /// both the safe answer and the one that means "no tilt".
        /// </remarks>
        private static Vector3 NormalOf(in RayCastHit hit) =>
            hit.Normal.LengthSquared > 1e-8f ? hit.Normal : new Vector3(0.0f, 1.0f, 0.0f);

        /// <summary>
        /// The data set's attribute storage, created and attached to every point when the input carried none.
        /// </summary>
        /// <remarks>
        /// Writes go through the set's writable-array accessors, which is where the copy-on-write contract is
        /// enforced, so this only has to deal with the "no storage at all" case that a sampler's output has.
        /// </remarks>
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

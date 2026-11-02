// PCG Plugin - Attribute Transfer (Attribute ops)
// Part of the FlaxEngine PCG plugin. See docs/PCG_PLUGIN_DESIGN.md §5.1.17.

using System;
using FlaxEngine;
using PCG.Execution;

namespace PCG.Nodes
{
    /// <summary>
    /// Copies a column from one point set onto another by nearest position - the join that lets a layer borrow
    /// a value another layer already paid to compute.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every other attribute op reads and writes the same points, so a value can only travel along the layer
    /// that produced it. That leaves a real gap: a terrain field that was sampled once and then filtered keeps
    /// its height column, but a second scatter that wants the same number has to sample the terrain again and
    /// hope the two answers agree. This node closes that gap - the source layer keeps the column, and every
    /// target point takes the value of the nearest source point, which is UE5's "Transfer Attributes".
    /// </para>
    /// <para>
    /// The search volume is the cell size, so a point inside <see cref="MaxDistance"/> is always in the 3x3
    /// neighbourhood <see cref="PCGPointIndex"/> walks: correct by construction rather than by a tuned
    /// heuristic. Ties are broken by the lower source index, which makes the result independent of the order
    /// the index happens to hand candidates back in - the same inputs always produce the same output.
    /// </para>
    /// <para>
    /// A target with no source inside the reach keeps whatever it had (zero, for a column this node had to
    /// create). That is deliberately not a filter: dropping the far points would turn "the reach was too
    /// small" into "the layer is empty", and the two are hard to tell apart from the output alone.
    /// </para>
    /// </remarks>
    public sealed class PCGAttributeTransferNode : PCGNodeBase
    {
        /// <summary>Stable type id.</summary>
        public const string Id = "AttributeTransfer";

        // Parameter indices. Order is part of the saved graph format; append only.
        private const int PAttribute = 0;
        private const int PMaxDistance = 1;
        private const int PPlaneOnly = 2;
        private const int PMatch = 3;

        /// <summary>Number of parameters this node reads.</summary>
        public const int ParameterCount = 4;

        /// <summary>Reach used when the node is built without a serialized value.</summary>
        public const float DefaultMaxDistance = 500.0f;

        private readonly string _typeId;
        private readonly PCGNodeParameters _params;

        /// <summary>Column copied from the source. Empty means the point's density.</summary>
        public string AttributeName = string.Empty;

        /// <summary>Largest distance a source point may be from a target and still contribute, in world units.</summary>
        public float MaxDistance = DefaultMaxDistance;

        /// <summary>When set, the vertical axis is left out of the distance, so a flat layer matches a hilly one.</summary>
        public bool PlaneOnly = true;

        /// <summary>How much of the nearest source point's placement a target also adopts.</summary>
        /// <remarks>
        /// The column copy answers "take this number"; this answers "stand here", which is the other half of
        /// UE5's match-and-set. Both use the same nearest search, so a target cannot pick one donor's value and
        /// another donor's placement.
        /// </remarks>
        public enum MatchMode
        {
            /// <summary>Only the column is copied; the target keeps its own placement.</summary>
            None = 0,

            /// <summary>The target is moved onto the source point, keeping its own rotation and scale.</summary>
            Position = 1,

            /// <summary>The target adopts the source point's position, rotation, scale and bounds.</summary>
            Transform = 2,
        }

        /// <summary>Which parts of the source point's placement are adopted.</summary>
        public MatchMode Match = MatchMode.None;

        /// <summary>Creates the node from serialized parameters.</summary>
        public PCGAttributeTransferNode(PCGNodeRegistry.PCGNodeSerializationData data)
        {
            _typeId = Id;
            _params = new PCGNodeParameters(data.Values, data.TypeId);

            AttributeName = _params.Get(PAttribute, string.Empty) ?? string.Empty;
            MaxDistance = Math.Max(0.0f, _params.Get(PMaxDistance, DefaultMaxDistance));
            PlaneOnly = _params.Get(PPlaneOnly, true);

            int match = _params.Get(PMatch, (int)MatchMode.None);
            Match = (MatchMode)Math.Min((int)MatchMode.Transform, Math.Max(0, match));
        }

        /// <inheritdoc />
        public override string TypeId => _typeId;

        /// <inheritdoc />
        public override string DisplayName => "Attribute Transfer";

        /// <inheritdoc />
        public override bool IsThreadSafe => true;

        /// <inheritdoc />
        public override PCGPinDefinition[] InputPins => new[]
        {
            new PCGPinDefinition("In", PCGPinDirection.Input, isRequired: true,
                tooltip: "Points the copied value is written onto."),
            new PCGPinDefinition("Source", PCGPinDirection.Input, isRequired: true,
                tooltip: "Points the value is read from; the nearest one wins."),
        };

        /// <inheritdoc />
        public override PCGPinDefinition[] OutputPins => new[]
        {
            new PCGPinDefinition("Out", PCGPinDirection.Output,
                tooltip: "In's points, each carrying the nearest source value."),
        };

        /// <inheritdoc />
        public override string DescribeParameters()
        {
            string name = string.IsNullOrEmpty(AttributeName) ? PCGAttributeSet.DensityName : AttributeName;
            string match = Match == MatchMode.None ? string.Empty : $", match={Match}";
            return $"{name} from nearest source within {MaxDistance:F1}, planeOnly={PlaneOnly}{match}";
        }

        /// <inheritdoc />
        public override PCGData OnExecute(PCGExecutionContext context, PCGData[] inputs)
        {
            var input = inputs != null && inputs.Length > 0 ? inputs[0] : null;
            if (input == null || input.Count == 0)
                return PCGData.Empty;

            var source = inputs != null && inputs.Length > 1 ? inputs[1] : null;
            if (source == null || source.Count == 0)
                return input.Clone();

            // Writing the point's own density is not an attribute at all, so it is split out before the column
            // lookup: the set answers false for "$Density" by design (its data lives on the point).
            bool toDensity = string.IsNullOrEmpty(AttributeName) ||
                             string.Equals(AttributeName, PCGAttributeSet.DensityName, StringComparison.Ordinal);

            float[] values;

            if (toDensity)
            {
                var sourcePoints = source.Points;
                values = new float[sourcePoints.Length];
                for (int j = 0; j < sourcePoints.Length; j++)
                    values[j] = sourcePoints[j].Density;
            }
            else
            {
                var sourceAttributes = source.SharedAttributes;
                values = sourceAttributes != null ? sourceAttributes.TryGetFloatArray(AttributeName) : null;

                if (values == null && Match == MatchMode.None)
                {
                    LastError = $"Attribute Transfer could not read '{AttributeName}': no such float attribute on " +
                                "the Source layer. The input passes through unchanged.";
                    return input.Clone();
                }

                if (values == null)
                {
                    // The placement match is still worth doing, so this is reported rather than fatal. The column
                    // is still created, left at its default, so a downstream read finds the name it expects.
                    LastError = $"Attribute Transfer could not read '{AttributeName}': no such float attribute on " +
                                "the Source layer. The placement was matched; the column was left at its default.";
                }
            }

            var index = new PCGPointIndex(source.Points, Math.Max(MaxDistance, 1e-3f), PlaneOnly);
            var sourcePositions = source.Points;

            if (toDensity)
            {
                var result = input.Clone();
                var targetPoints = result.Points;

                for (int i = 0; i < targetPoints.Length; i++)
                {
                    context.ThrowIfCancelled(i);

                    int nearest = Nearest(index, sourcePositions, targetPoints[i].Position);
                    if (nearest >= 0)
                    {
                        targetPoints[i].Density = values[nearest];
                        ApplyMatch(ref targetPoints[i], sourcePositions[nearest]);
                    }
                }

                return result;
            }

            var cloned = input.Clone();
            var attributes = EnsureAttributes(cloned);

            if (!attributes.Contains(AttributeName))
                attributes.DeclareAttributeUnsafe(AttributeName, PCGAttributeType.Float);

            var column = attributes.GetWritableFloatArray(AttributeName);
            var points = cloned.Points;

            for (int i = 0; i < points.Length; i++)
            {
                context.ThrowIfCancelled(i);

                int nearest = Nearest(index, sourcePositions, points[i].Position);
                if (nearest < 0)
                    continue;

                if (values != null)
                    column[i] = values[nearest];

                ApplyMatch(ref points[i], sourcePositions[nearest]);
            }

            return cloned;
        }

        /// <summary>
        /// Index of the source point nearest to <paramref name="position"/> within the reach, or -1 when none is.
        /// </summary>
        /// <remarks>
        /// The reach is what the index was built with, so the candidates visited here are all that can qualify.
        /// The strict "&lt;" plus the index tie-break keeps the answer independent of traversal order: a caller
        /// must not be able to change the output by reordering the source layer.
        /// </remarks>
        private int Nearest(PCGPointIndex index, PCGPoint[] source, in Vector3 position)
        {
            float bestSquared = MaxDistance * MaxDistance;
            int best = -1;

            // Copied out of the `in` parameter so the candidate visitor may capture it.
            var query = position;

            index.ForEachNearby(position, j =>
            {
                var delta = source[j].Position - query;

                float squared = PlaneOnly
                    ? delta.X * delta.X + delta.Z * delta.Z
                    : delta.X * delta.X + delta.Y * delta.Y + delta.Z * delta.Z;

                if (squared < bestSquared || (squared == bestSquared && (best < 0 || j < best)))
                {
                    bestSquared = squared;
                    best = j;
                }
            });

            return best;
        }

        /// <summary>Applies the requested part of the donor's placement to a target point.</summary>
        /// <remarks>
        /// A moved point carries its bounds with it: the box is stored in world space, so leaving it behind would
        /// make every downstream pruning decision test a rectangle the point has left. <see cref="MatchMode.Position"/>
        /// re-centres the point's own extents; <see cref="MatchMode.Transform"/> adopts the donor's box outright,
        /// which is what matching means for a point that is standing in for another.
        /// </remarks>
        private void ApplyMatch(ref PCGPoint target, in PCGPoint donor)
        {
            if (Match == MatchMode.None)
                return;

            if (Match == MatchMode.Transform)
            {
                target.Position = donor.Position;
                target.Rotation = donor.Rotation;
                target.Scale = donor.Scale;
                target.Bounds = donor.Bounds;
                return;
            }

            var half = PCGPointTransform.HalfExtents(target.Bounds);

            target.Position = donor.Position;

            // Rebuilt rather than passed to UpdateBounds: that helper re-applies the point's own transform, so a
            // tilted or scaled target would have its box enclosed a second time on the way in.
            target.Bounds = PCGPointTransform.MakeBox(target.Position, half);
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

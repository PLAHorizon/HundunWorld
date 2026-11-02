// PCG Plugin - Intersection (Point Ops)
// Part of the FlaxEngine PCG plugin. See docs/PCG_PLUGIN_DESIGN.md §5.1.4.

using System;
using System.Collections.Generic;
using FlaxEngine;
using PCG.Execution;

namespace PCG.Nodes
{
    /// <summary>
    /// Keeps the points that fall inside another point set - the complement of
    /// <see cref="PCGDifferencePointsNode"/>, and the usual way to restrict a scatter to a region that only
    /// exists as another layer (a village footprint, a flooded area, a quest zone).
    /// </summary>
    /// <remarks>
    /// Deliberately the same containment test as Difference, down to the shared probe: two implementations of
    /// "inside" would eventually disagree and the pair would stop partitioning the input, which is the property
    /// the harness checks by asserting every point lands in exactly one of the two outputs.
    /// </remarks>
    public sealed class PCGIntersectionPointsNode : PCGNodeBase
    {
        /// <summary>Stable type id.</summary>
        public const string Id = "IntersectionPoints";

        // Parameter indices. Order is part of the saved graph format; append only.
        private const int PMode = 0;
        private const int PMaxDistance = 1;
        private const int PPadding = 2;
        private const int PPlaneOnly = 3;

        /// <summary>Number of parameters this node reads.</summary>
        public const int ParameterCount = 4;

        private readonly string _typeId;
        private readonly PCGNodeParameters _params;

        /// <summary>Cut by the other set's point bounds (false) or by distance to its centres (true).</summary>
        public bool DistanceMode;

        /// <summary>Reach used in distance mode, in world units.</summary>
        public float MaxDistance = 200.0f;

        /// <summary>Grows the volume a point has to stand in to survive.</summary>
        public float Padding;

        /// <summary>When set, the vertical axis is left out of the containment test.</summary>
        public bool PlaneOnly = true;

        /// <summary>Creates the node from serialized parameters.</summary>
        public PCGIntersectionPointsNode(PCGNodeRegistry.PCGNodeSerializationData data)
        {
            _typeId = Id;
            _params = new PCGNodeParameters(data.Values, data.TypeId);

            DistanceMode = _params.Get(PMode, 0) != 0;
            MaxDistance = Math.Max(0.0f, _params.Get(PMaxDistance, 200.0f));
            Padding = Math.Max(0.0f, _params.Get(PPadding, 0.0f));
            PlaneOnly = _params.Get(PPlaneOnly, true);
        }

        /// <inheritdoc />
        public override string TypeId => _typeId;

        /// <inheritdoc />
        public override string DisplayName => "Intersection";

        /// <inheritdoc />
        public override bool IsThreadSafe => true;

        /// <inheritdoc />
        public override PCGPinDefinition[] InputPins => new[]
        {
            new PCGPinDefinition("In", PCGPinDirection.Input, isRequired: true,
                tooltip: "Points to select from."),
            new PCGPinDefinition("Keep", PCGPinDirection.Input, isRequired: true,
                tooltip: "Points whose bounds (or centres) define the region In has to be inside."),
        };

        /// <inheritdoc />
        public override PCGPinDefinition[] OutputPins => new[]
        {
            new PCGPinDefinition("Out", PCGPinDirection.Output, tooltip: "In's points that fall inside Keep."),
        };

        /// <inheritdoc />
        public override string DescribeParameters() =>
            $"mode={(DistanceMode ? "distance" : "bounds")}, maxDistance={MaxDistance:F1}, " +
            $"padding={Padding:F1}, planeOnly={PlaneOnly}";

        /// <inheritdoc />
        public override PCGData OnExecute(PCGExecutionContext context, PCGData[] inputs)
        {
            var input = inputs != null && inputs.Length > 0 ? inputs[0] : null;
            if (input == null || input.Count == 0)
                return PCGData.Empty;

            var keep = inputs != null && inputs.Length > 1 ? inputs[1] : null;
            if (keep == null || keep.Count == 0)
                return PCGData.Empty;

            var probe = new PCGPointProbe(keep.Points, DistanceMode, MaxDistance, PlaneOnly, Padding);
            var points = input.Points;
            var inside = new bool[points.Length];

            PCGParallel.For(points.Length, i =>
            {
                context.ThrowIfCancelled(i);
                inside[i] = probe.IsInside(points[i].Position);
            });

            var survivors = new List<int>(points.Length);
            for (int i = 0; i < points.Length; i++)
            {
                if (inside[i])
                    survivors.Add(i);
            }

            if (survivors.Count == points.Length)
                return input.Clone();

            return survivors.Count == 0
                ? PCGData.Empty
                : PCGData.Gather(input, survivors.ToArray(), survivors.Count);
        }
    }
}

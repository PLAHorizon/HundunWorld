// PCG Plugin - Difference (Point Ops)
// Part of the FlaxEngine PCG plugin. See docs/PCG_PLUGIN_DESIGN.md §5.1.4.

using System;
using System.Collections.Generic;
using FlaxEngine;
using PCG.Execution;

namespace PCG.Nodes
{
    /// <summary>
    /// Removes the points that fall inside another point set - the operation a road corridor, a building
    /// footprint or a lake does to a field of scattered props.
    /// </summary>
    /// <remarks>
    /// The cutting set is treated as a volume, not as a list to subtract by identity: a tree is removed when it
    /// stands inside the road's points' bounds, which is what makes the two layers independent - neither has to
    /// know the other's indexing. An empty cutting input means nothing was subtracted and the input passes
    /// through, rather than the graph silently emptying itself when one branch is disabled.
    /// </remarks>
    public sealed class PCGDifferencePointsNode : PCGNodeBase
    {
        /// <summary>Stable type id.</summary>
        public const string Id = "DifferencePoints";

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

        /// <summary>Grows the cutting volume, so a corridor can be widened without moving its geometry.</summary>
        public float Padding;

        /// <summary>When set, the vertical axis is left out of the containment test.</summary>
        public bool PlaneOnly = true;

        /// <summary>Creates the node from serialized parameters.</summary>
        public PCGDifferencePointsNode(PCGNodeRegistry.PCGNodeSerializationData data)
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
        public override string DisplayName => "Difference";

        /// <inheritdoc />
        public override bool IsThreadSafe => true;

        /// <inheritdoc />
        public override PCGPinDefinition[] InputPins => new[]
        {
            new PCGPinDefinition("In", PCGPinDirection.Input, isRequired: true,
                tooltip: "Points to subtract from."),
            new PCGPinDefinition("Cut", PCGPinDirection.Input, isRequired: true,
                tooltip: "Points whose bounds (or centres) carve into In."),
        };

        /// <inheritdoc />
        public override PCGPinDefinition[] OutputPins => new[]
        {
            new PCGPinDefinition("Out", PCGPinDirection.Output, tooltip: "In's points that fall outside Cut."),
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

            var cut = inputs != null && inputs.Length > 1 ? inputs[1] : null;
            if (cut == null || cut.Count == 0)
                return input.Clone();

            var probe = new PCGPointProbe(cut.Points, DistanceMode, MaxDistance, PlaneOnly, Padding);
            var points = input.Points;
            var inside = new bool[points.Length];

            PCGParallel.For(points.Length, i =>
            {
                context.ThrowIfCancelled(i);
                inside[i] = probe.IsInside(points[i].Position);
            });

            // Gathered in point order after the parallel pass, because a filter's output order is part of its
            // contract - downstream nodes index points by position.
            var keep = new List<int>(points.Length);
            for (int i = 0; i < points.Length; i++)
            {
                if (!inside[i])
                    keep.Add(i);
            }

            if (keep.Count == points.Length)
                return input.Clone();

            return keep.Count == 0 ? PCGData.Empty : PCGData.Gather(input, keep.ToArray(), keep.Count);
        }
    }
}

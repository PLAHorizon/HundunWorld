// PCG Plugin - Distance Filter node
// Part of the FlaxEngine PCG plugin. See docs/PCG_PLUGIN_DESIGN.md §5.1.2.

using System;
using System.Collections.Generic;
using FlaxEngine;
using PCG.Execution;

namespace PCG.Nodes
{
    /// <summary>
    /// Keeps points by their distance to a reference position.
    /// </summary>
    /// <remarks>
    /// Defaults to measuring on the XZ plane: an MMORPG field of rocks around a campsite should not be judged
    /// by how high the terrain rises underneath it. Turn plane mode off for cave fill and similar volumetric
    /// use. Like the other filters it republishes its survivors so downstream writes cannot leak back into the
    /// unfiltered set.
    /// </remarks>
    public sealed class PCGDistanceFilterNode : PCGNodeBase
    {
        /// <summary>Stable type id.</summary>
        public const string Id = "DistanceFilter";

        // Parameter indices. Order is part of the saved graph format; append only.
        private const int PReference = 0;
        private const int PMinDistance = 1;
        private const int PMaxDistance = 2;
        private const int PPlaneOnly = 3;
        private const int PInvert = 4;

        /// <summary>Number of parameters this node reads.</summary>
        public const int ParameterCount = 5;

        private readonly string _typeId;
        private readonly PCGNodeParameters _params;

        /// <summary>Position the distances are measured from, in world space.</summary>
        public Vector3 Reference;

        /// <summary>Inclusive nearest allowed distance.</summary>
        public float MinDistance;

        /// <summary>Inclusive furthest allowed distance.</summary>
        public float MaxDistance = float.PositiveInfinity;

        /// <summary>When set, the vertical axis is left out of the measurement.</summary>
        public bool PlaneOnly = true;

        /// <summary>When set, the test is inverted and points outside the range are kept.</summary>
        public bool Invert;

        /// <summary>Creates the node from serialized parameters.</summary>
        public PCGDistanceFilterNode(PCGNodeRegistry.PCGNodeSerializationData data)
        {
            _typeId = Id;
            _params = new PCGNodeParameters(data.Values, data.TypeId);

            Reference = _params.GetVector3(PReference, Vector3.Zero);
            MinDistance = Math.Max(0.0f, _params.Get(PMinDistance, 0.0f));
            MaxDistance = _params.Get(PMaxDistance, float.PositiveInfinity);
            PlaneOnly = _params.Get(PPlaneOnly, true);
            Invert = _params.Get(PInvert, false);

            if (MaxDistance < MinDistance)
            {
                // A reversed range would silently reject everything; swap so the author's intent survives.
                (MinDistance, MaxDistance) = (MaxDistance, MinDistance);
            }
        }

        /// <inheritdoc />
        public override string TypeId => _typeId;

        /// <inheritdoc />
        public override string DisplayName => "Distance Filter";

        /// <inheritdoc />
        public override bool IsThreadSafe => true;

        /// <inheritdoc />
        public override PCGPinDefinition[] InputPins => new[]
        {
            new PCGPinDefinition("In", PCGPinDirection.Input, isRequired: true,
                tooltip: "Points to filter."),
        };

        /// <inheritdoc />
        public override PCGPinDefinition[] OutputPins => new[]
        {
            new PCGPinDefinition("Out", PCGPinDirection.Output, tooltip: "Points within the distance range."),
        };

        /// <inheritdoc />
        public override string DescribeParameters() =>
            $"reference={Reference}, distance=[{MinDistance:F1},{MaxDistance:F1}], planeOnly={PlaneOnly}, invert={Invert}";

        /// <inheritdoc />
        public override PCGData OnExecute(PCGExecutionContext context, PCGData[] inputs)
        {
            var input = inputs[0];
            if (input == null || input.Count == 0)
                return PCGData.Empty;

            var points = input.Points;
            var keep = new List<int>(points.Length);

            float minSquared = MinDistance * MinDistance;
            float maxSquared = float.IsPositiveInfinity(MaxDistance) ? float.PositiveInfinity : MaxDistance * MaxDistance;

            for (int i = 0; i < points.Length; i++)
            {
                context.ThrowIfCancelled(i);

                float dx = points[i].Position.X - Reference.X;
                float dz = points[i].Position.Z - Reference.Z;
                float squared = dx * dx + dz * dz;

                if (!PlaneOnly)
                {
                    float dy = points[i].Position.Y - Reference.Y;
                    squared += dy * dy;
                }

                bool inRange = squared >= minSquared && squared <= maxSquared;
                if (inRange != Invert)
                    keep.Add(i);
            }

            if (keep.Count == points.Length)
                return input.Clone();

            if (keep.Count == 0)
                return PCGData.Empty;

            return PCGData.Gather(input, keep.ToArray(), keep.Count);
        }
    }
}

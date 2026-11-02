// PCG Plugin - Height Filter node
// Part of the FlaxEngine PCG plugin. See docs/PCG_PLUGIN_DESIGN.md §5.1.2.

using System;
using System.Collections.Generic;
using FlaxEngine;
using PCG.Execution;

namespace PCG.Nodes
{
    /// <summary>
    /// Keeps points whose world Y falls inside a range.
    /// </summary>
    /// <remarks>
    /// Filters on <see cref="PCGPoint.Position"/> rather than a stored attribute, so it works on any upstream
    /// sampler. Compaction follows the same ownership rule as <see cref="PCGDensityFilterNode"/>: the output
    /// owns its attribute storage so a downstream write cannot leak back into the unfiltered set.
    /// </remarks>
    public sealed class PCGHeightFilterNode : PCGNodeBase
    {
        /// <summary>Stable type id.</summary>
        public const string Id = "HeightFilter";

        // Parameter indices. Order is part of the saved graph format; append only.
        private const int PMinHeight = 0;
        private const int PMaxHeight = 1;
        private const int PInvert = 2;

        /// <summary>Number of parameters this node reads.</summary>
        public const int ParameterCount = 3;

        private readonly string _typeId;
        private readonly PCGNodeParameters _params;

        /// <summary>Inclusive lower height in world units.</summary>
        public float MinHeight = float.NegativeInfinity;

        /// <summary>Inclusive upper height in world units.</summary>
        public float MaxHeight = float.PositiveInfinity;

        /// <summary>When set, the test is inverted and points outside the range are kept.</summary>
        public bool Invert;

        /// <summary>Creates the node from serialized parameters.</summary>
        public PCGHeightFilterNode(PCGNodeRegistry.PCGNodeSerializationData data)
        {
            _typeId = Id;
            _params = new PCGNodeParameters(data.Values, data.TypeId);

            MinHeight = _params.Get(PMinHeight, float.NegativeInfinity);
            MaxHeight = _params.Get(PMaxHeight, float.PositiveInfinity);
            Invert = _params.Get(PInvert, false);

            if (MaxHeight < MinHeight)
            {
                // A reversed range would silently reject everything; swap so the author's intent survives.
                (MinHeight, MaxHeight) = (MaxHeight, MinHeight);
            }
        }

        /// <inheritdoc />
        public override string TypeId => _typeId;

        /// <inheritdoc />
        public override string DisplayName => "Height Filter";

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
            new PCGPinDefinition("Out", PCGPinDirection.Output, tooltip: "Points inside the height range."),
        };

        /// <inheritdoc />
        public override string DescribeParameters() =>
            $"height=[{MinHeight:F1},{MaxHeight:F1}], invert={Invert}";

        /// <inheritdoc />
        public override PCGData OnExecute(PCGExecutionContext context, PCGData[] inputs)
        {
            var input = inputs[0];
            if (input == null || input.Count == 0)
                return PCGData.Empty;

            var points = input.Points;
            var keep = new List<int>(points.Length);

            for (int i = 0; i < points.Length; i++)
            {
                context.ThrowIfCancelled(i);

                float y = points[i].Position.Y;
                bool inRange = y >= MinHeight && y <= MaxHeight;
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

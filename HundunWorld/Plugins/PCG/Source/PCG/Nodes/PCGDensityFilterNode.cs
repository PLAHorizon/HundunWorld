// PCG Plugin - Density Filter node
// Part of the FlaxEngine PCG plugin. See docs/PCG_PLUGIN_DESIGN.md §5.1.2.

using System;
using System.Collections.Generic;
using FlaxEngine;
using PCG.Execution;

namespace PCG.Nodes
{
    /// <summary>
    /// Keeps points whose density falls inside a range.
    /// </summary>
    /// <remarks>
    /// Produces a compacted copy of its input via <see cref="PCGData.Gather"/>, so the surviving points own
    /// their attribute storage. Sharing the upstream storage instead would be faster but would let a
    /// downstream modifier write through to the unfiltered set - the aliasing failure described in
    /// docs/PCG_PLUGIN_DESIGN.md §3.2 note 3.
    /// </remarks>
    public sealed class PCGDensityFilterNode : PCGNodeBase
    {
        /// <summary>Stable type id.</summary>
        public const string Id = "DensityFilter";

        private const int PMinDensity = 0;
        private const int PMaxDensity = 1;
        private const int PInvert = 2;

        /// <summary>Number of parameters this node reads.</summary>
        public const int ParameterCount = 3;

        private readonly string _typeId;
        private readonly PCGNodeParameters _params;

        /// <summary>Inclusive lower density bound.</summary>
        public float MinDensity;

        /// <summary>Inclusive upper density bound.</summary>
        public float MaxDensity = 1.0f;

        /// <summary>When set, the test is inverted and points outside the range are kept.</summary>
        public bool Invert;

        /// <summary>Creates the node from serialized parameters.</summary>
        public PCGDensityFilterNode(PCGNodeRegistry.PCGNodeSerializationData data)
        {
            _typeId = Id;
            _params = new PCGNodeParameters(data.Values, data.TypeId);

            MinDensity = Mathf.Clamp(_params.Get(PMinDensity, 0.0f), 0.0f, 1.0f);
            MaxDensity = Mathf.Clamp(_params.Get(PMaxDensity, 1.0f), 0.0f, 1.0f);
            Invert = _params.Get(PInvert, false);

            if (MaxDensity < MinDensity)
            {
                // A reversed range would silently reject everything; swap instead so the intent is preserved.
                (MinDensity, MaxDensity) = (MaxDensity, MinDensity);
            }
        }

        /// <inheritdoc />
        public override string TypeId => _typeId;

        /// <inheritdoc />
        public override string DisplayName => "Density Filter";

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
            new PCGPinDefinition("Out", PCGPinDirection.Output, tooltip: "Points inside the density range."),
        };

        /// <inheritdoc />
        public override string DescribeParameters() =>
            $"range=[{MinDensity:F2},{MaxDensity:F2}], invert={Invert}";

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

                bool inRange = points[i].Density >= MinDensity && points[i].Density <= MaxDensity;
                if (inRange != Invert)
                    keep.Add(i);
            }

            if (keep.Count == points.Length)
            {
                // Nothing was removed. Return a compacted copy anyway rather than the input with its shared
                // storage, so downstream writes cannot leak back into the unfiltered set.
                return input.Clone();
            }

            if (keep.Count == 0)
                return PCGData.Empty;

            return PCGData.Gather(input, keep.ToArray(), keep.Count);
        }
    }
}

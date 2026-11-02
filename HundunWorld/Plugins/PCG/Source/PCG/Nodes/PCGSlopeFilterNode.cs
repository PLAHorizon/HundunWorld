// PCG Plugin - Slope Filter node
// Part of the FlaxEngine PCG plugin. See docs/PCG_PLUGIN_DESIGN.md §5.1.2 and §3.5 point 3.

using System;
using System.Collections.Generic;
using FlaxEngine;
using PCG.Execution;

namespace PCG.Nodes
{
    /// <summary>
    /// Keeps points whose surface steepness falls inside a range.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The sampler that read the terrain already paid for a normal, so it caches the result in the built-in
    /// <c>$Slope</c> column and this node reads that column instead of issuing a second terrain query per
    /// point. When no column is present the steepness is derived from the point's own orientation, which keeps
    /// the node usable on spline- and volume-generated points.
    /// </para>
    /// <para>
    /// A derived value is written back into <c>$Slope</c> on the output so a second slope-based node downstream
    /// does not repeat the trigonometry.
    /// </para>
    /// </remarks>
    public sealed class PCGSlopeFilterNode : PCGNodeBase
    {
        /// <summary>Stable type id.</summary>
        public const string Id = "SlopeFilter";

        // Parameter indices. Order is part of the saved graph format; append only.
        private const int PMinSlopeDegrees = 0;
        private const int PMaxSlopeDegrees = 1;
        private const int PInvert = 2;

        /// <summary>Number of parameters this node reads.</summary>
        public const int ParameterCount = 3;

        private readonly string _typeId;
        private readonly PCGNodeParameters _params;

        /// <summary>Inclusive lower steepness in degrees from vertical.</summary>
        public float MinSlopeDegrees;

        /// <summary>Inclusive upper steepness in degrees from vertical.</summary>
        public float MaxSlopeDegrees = 90.0f;

        /// <summary>When set, the test is inverted and points outside the range are kept.</summary>
        public bool Invert;

        /// <summary>Creates the node from serialized parameters.</summary>
        public PCGSlopeFilterNode(PCGNodeRegistry.PCGNodeSerializationData data)
        {
            _typeId = Id;
            _params = new PCGNodeParameters(data.Values, data.TypeId);

            MinSlopeDegrees = Mathf.Clamp(_params.Get(PMinSlopeDegrees, 0.0f), 0.0f, 90.0f);
            MaxSlopeDegrees = Mathf.Clamp(_params.Get(PMaxSlopeDegrees, 90.0f), 0.0f, 90.0f);
            Invert = _params.Get(PInvert, false);

            if (MaxSlopeDegrees < MinSlopeDegrees)
            {
                // A reversed range would silently reject everything; swap so the author's intent survives.
                (MinSlopeDegrees, MaxSlopeDegrees) = (MaxSlopeDegrees, MinSlopeDegrees);
            }
        }

        /// <inheritdoc />
        public override string TypeId => _typeId;

        /// <inheritdoc />
        public override string DisplayName => "Slope Filter";

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
            new PCGPinDefinition("Out", PCGPinDirection.Output, tooltip: "Points on slopes inside the range."),
        };

        /// <inheritdoc />
        public override string DescribeParameters() =>
            $"slope=[{MinSlopeDegrees:F1},{MaxSlopeDegrees:F1}] deg, invert={Invert}";

        /// <inheritdoc />
        public override PCGData OnExecute(PCGExecutionContext context, PCGData[] inputs)
        {
            var input = inputs[0];
            if (input == null || input.Count == 0)
                return PCGData.Empty;

            var points = input.Points;
            int n = points.Length;

            // Prefer the cached column; a length mismatch means it does not describe this data set.
            var cached = input.SharedAttributes?.TryGetFloatArray(PCGAttributeSet.SlopeName);
            bool useCached = cached != null && cached.Length == n;

            var slopes = useCached ? null : new float[n];
            var keep = new List<int>(n);

            for (int i = 0; i < n; i++)
            {
                context.ThrowIfCancelled(i);

                float slope = useCached ? cached[i] : PCGPointTransform.SlopeDegrees(in points[i]);
                if (slopes != null)
                    slopes[i] = slope;

                bool inRange = slope >= MinSlopeDegrees && slope <= MaxSlopeDegrees;
                if (inRange != Invert)
                    keep.Add(i);
            }

            PCGData result;
            if (keep.Count == n)
                result = input.Clone();
            else if (keep.Count == 0)
                return PCGData.Empty;
            else
                result = PCGData.Gather(input, keep.ToArray(), keep.Count);

            if (useCached)
                return result;

            // Publish what we derived so the next slope consumer reads it instead of recomputing.
            var attributes = result.SharedAttributes;
            if (attributes == null)
            {
                attributes = new PCGAttributeSet(result.Count);
                result.SharedAttributes = attributes;
                for (int i = 0; i < result.Points.Length; i++)
                    result.Points[i].Attributes = attributes;
            }

            if (!attributes.Contains(PCGAttributeSet.SlopeName))
                attributes.DeclareAttributeUnsafe(PCGAttributeSet.SlopeName, PCGAttributeType.Float);

            var column = attributes.GetWritableFloatArray(PCGAttributeSet.SlopeName);
            for (int i = 0; i < keep.Count; i++)
                column[i] = slopes[keep[i]];

            return result;
        }
    }
}

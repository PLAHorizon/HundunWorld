// PCG Plugin - Scale By Attribute (Modifier)
// Part of the FlaxEngine PCG plugin. See docs/PCG_PLUGIN_DESIGN.md §5.1.26.

using System;
using FlaxEngine;
using PCG.Execution;

namespace PCG.Nodes
{
    /// <summary>
    /// Sizes each point from a column - the step that makes a scatter stop looking stamped out of one mould.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The plugin can already derive a number per point (noise, distance, height, remap) and read a point's size
    /// back out; what was missing is the other direction. Without it, "bigger where it is wetter" is not
    /// expressible: Transform Points scales by a constant and Bounds Modifier sets extents from its own fields,
    /// so every prop in a layer comes out the same size however carefully the density was shaped. UE5 gets this
    /// from attribute-driven extents; this is the same idea with one compact node.
    /// </para>
    /// <para>
    /// The factor <b>multiplies</b> the point's own scale rather than replacing it, so two of these compose and a
    /// scale set upstream by Transform Points survives. The value is clamped to [0,1] before it is mapped: a
    /// column is a weight here, and a weight of 3 has no sensible answer that clamping to 1 does not already give.
    /// </para>
    /// <para>
    /// The point's bounds are scaled with it. Extents are what every downstream pruning decision tests, so a
    /// point that grew without its box growing is a point that will be culled while it is still visible.
    /// </para>
    /// </remarks>
    public sealed class PCGScaleByAttributeNode : PCGNodeBase
    {
        /// <summary>Stable type id.</summary>
        public const string Id = "ScaleByAttribute";

        // Parameter indices. Order is part of the saved graph format; append only.
        private const int PAttribute = 0;
        private const int PScaleMin = 1;
        private const int PScaleMax = 2;
        private const int PInvert = 3;
        private const int PScaleBounds = 4;

        /// <summary>Number of parameters this node reads.</summary>
        public const int ParameterCount = 5;

        private readonly string _typeId;
        private readonly PCGNodeParameters _params;

        /// <summary>Column the size is read from. Empty reads the point's density.</summary>
        public string AttributeName = string.Empty;

        /// <summary>Factor applied where the column is 0.</summary>
        public float ScaleMin = 0.5f;

        /// <summary>Factor applied where the column is 1.</summary>
        public float ScaleMax = 2.0f;

        /// <summary>When set, a low column value means a large point.</summary>
        public bool Invert;

        /// <summary>When set, the point's bounds grow and shrink with it.</summary>
        public bool ScaleBounds = true;

        /// <summary>Creates the node from serialized parameters.</summary>
        public PCGScaleByAttributeNode(PCGNodeRegistry.PCGNodeSerializationData data)
        {
            _typeId = Id;
            _params = new PCGNodeParameters(data.Values, data.TypeId);

            AttributeName = _params.Get(PAttribute, string.Empty) ?? string.Empty;
            ScaleMin = Math.Max(0.0f, _params.Get(PScaleMin, 0.5f));
            ScaleMax = Math.Max(0.0f, _params.Get(PScaleMax, 2.0f));
            Invert = _params.Get(PInvert, false);
            ScaleBounds = _params.Get(PScaleBounds, true);
        }

        /// <inheritdoc />
        public override string TypeId => _typeId;

        /// <inheritdoc />
        public override string DisplayName => "Scale By Attribute";

        /// <inheritdoc />
        public override bool IsThreadSafe => true;

        /// <inheritdoc />
        public override PCGPinDefinition[] InputPins => new[]
        {
            new PCGPinDefinition("In", PCGPinDirection.Input, isRequired: true,
                tooltip: "Points to resize."),
        };

        /// <inheritdoc />
        public override PCGPinDefinition[] OutputPins => new[]
        {
            new PCGPinDefinition("Out", PCGPinDirection.Output,
                tooltip: "The same points, sized from the column."),
        };

        /// <inheritdoc />
        public override string DescribeParameters()
        {
            string name = string.IsNullOrEmpty(AttributeName) ? PCGAttributeSet.DensityName : AttributeName;
            return $"{name} -> scale [{ScaleMin:F2},{ScaleMax:F2}], invert={Invert}, bounds={ScaleBounds}";
        }

        /// <inheritdoc />
        public override PCGData OnExecute(PCGExecutionContext context, PCGData[] inputs)
        {
            var input = inputs != null && inputs.Length > 0 ? inputs[0] : null;
            if (input == null || input.Count == 0)
                return PCGData.Empty;

            bool byDensity = string.IsNullOrEmpty(AttributeName) ||
                             string.Equals(AttributeName, PCGAttributeSet.DensityName, StringComparison.Ordinal);

            float[] values = null;

            if (!byDensity)
            {
                var attributes = input.SharedAttributes;
                values = attributes != null ? attributes.TryGetFloatArray(AttributeName) : null;

                if (values == null)
                {
                    LastError = $"Scale By Attribute could not read '{AttributeName}': no such float attribute " +
                                "on this layer. The input passes through unchanged.";
                    return input.Clone();
                }
            }

            var result = input.Clone();
            var points = result.Points;

            for (int i = 0; i < points.Length; i++)
            {
                context.ThrowIfCancelled(i);

                float value = byDensity ? points[i].Density : values[i];

                // Clamped before mapping: the column is a weight, and extrapolating past [0,1] would let a stray
                // value silently turn a prop into a landmark.
                value = Math.Min(1.0f, Math.Max(0.0f, value));

                if (Invert)
                    value = 1.0f - value;

                float factor = ScaleMin + (ScaleMax - ScaleMin) * value;

                if (factor == 1.0f)
                    continue;

                var scale = points[i].Scale;
                var half = PCGPointTransform.HalfExtents(points[i].Bounds);

                points[i].Scale = new Float3(scale.X * factor, scale.Y * factor, scale.Z * factor);

                if (ScaleBounds)
                {
                    points[i].UpdateBounds(new Vector3(
                        half.X * factor, half.Y * factor, half.Z * factor));
                }
            }

            return result;
        }
    }
}

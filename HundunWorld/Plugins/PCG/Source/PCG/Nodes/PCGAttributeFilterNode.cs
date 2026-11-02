// PCG Plugin - Attribute Filter
// Part of the FlaxEngine PCG plugin. See docs/PCG_PLUGIN_DESIGN.md §5.1.12.

using System;
using System.Collections.Generic;
using FlaxEngine;
using PCG.Execution;

namespace PCG.Nodes
{
    /// <summary>
    /// Keeps the points whose named attribute falls inside a range - the filter that makes a derived column
    /// worth having.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The plugin already filters by density, height, slope and distance, each of which is really "an attribute
    /// this node happened to know how to compute". This node filters by *any* column, which is what closes the
    /// loop with <see cref="PCGSetAttributeNode"/> and <see cref="PCGAttributeMathNode"/>: a graph can derive a
    /// number nobody anticipated and then select on it, without a new node per idea. It is UE5's "Filter by
    /// Attribute" by another name.
    /// </para>
    /// <para>
    /// A column that nobody wrote is reported and the layer passes through whole. The alternative - keeping
    /// nothing, or treating the missing value as zero - would turn a half-wired graph into an empty world, and
    /// an empty world is indistinguishable from a filter that works and matches nothing.
    /// </para>
    /// </remarks>
    public sealed class PCGAttributeFilterNode : PCGNodeBase
    {
        /// <summary>Stable type id.</summary>
        public const string Id = "AttributeFilter";

        // Parameter indices. Order is part of the saved graph format; append only.
        private const int PAttributeName = 0;
        private const int PMin = 1;
        private const int PMax = 2;
        private const int PInvert = 3;

        /// <summary>Number of parameters this node reads.</summary>
        public const int ParameterCount = 4;

        private readonly string _typeId;
        private readonly PCGNodeParameters _params;

        /// <summary>Column tested; empty means the point's density.</summary>
        public string AttributeName = string.Empty;

        /// <summary>Inclusive lower bound.</summary>
        public float Min;

        /// <summary>Inclusive upper bound.</summary>
        public float Max = 1.0f;

        /// <summary>When set, points inside the range are removed instead.</summary>
        public bool Invert;

        /// <summary>Creates the node from serialized parameters.</summary>
        public PCGAttributeFilterNode(PCGNodeRegistry.PCGNodeSerializationData data)
        {
            _typeId = Id;
            _params = new PCGNodeParameters(data.Values, data.TypeId);

            AttributeName = _params.Get(PAttributeName, string.Empty) ?? string.Empty;
            Min = _params.Get(PMin, 0.0f);
            Max = _params.Get(PMax, 1.0f);
            Invert = _params.Get(PInvert, false);

            if (Max < Min)
            {
                // A reversed range would reject everything; swapping keeps the author's two numbers as the
                // bounds they obviously meant.
                (Min, Max) = (Max, Min);
            }
        }

        /// <inheritdoc />
        public override string TypeId => _typeId;

        /// <inheritdoc />
        public override string DisplayName => "Attribute Filter";

        /// <inheritdoc />
        public override bool IsThreadSafe => true;

        /// <inheritdoc />
        public override PCGPinDefinition[] InputPins => new[]
        {
            new PCGPinDefinition("In", PCGPinDirection.Input, isRequired: true, tooltip: "Points to filter."),
        };

        /// <inheritdoc />
        public override PCGPinDefinition[] OutputPins => new[]
        {
            new PCGPinDefinition("Out", PCGPinDirection.Output,
                tooltip: "Points whose attribute is inside the range."),
        };

        /// <inheritdoc />
        public override string DescribeParameters()
        {
            string name = string.IsNullOrEmpty(AttributeName) ? "$Density" : AttributeName;
            return $"{name} in [{Min:F2},{Max:F2}], invert={Invert}";
        }

        /// <inheritdoc />
        public override PCGData OnExecute(PCGExecutionContext context, PCGData[] inputs)
        {
            var input = inputs[0];
            if (input == null || input.Count == 0)
                return PCGData.Empty;

            bool byColumn = !string.IsNullOrEmpty(AttributeName);
            var attributes = input.SharedAttributes;

            if (byColumn && (attributes == null || !attributes.Contains(AttributeName)))
            {
                LastError = $"Attribute Filter could not read '{AttributeName}': no such attribute on this " +
                            "layer. The layer passes through unfiltered.";
                return input.Clone();
            }

            var points = input.Points;
            var keep = new List<int>(points.Length);

            for (int i = 0; i < points.Length; i++)
            {
                context.ThrowIfCancelled(i);

                float value = byColumn ? attributes.GetFloat(AttributeName, i) : points[i].Density;
                bool inside = value >= Min && value <= Max;

                if (inside != Invert)
                    keep.Add(i);
            }

            if (keep.Count == points.Length)
                return input.Clone();

            return keep.Count == 0 ? PCGData.Empty : PCGData.Gather(input, keep.ToArray(), keep.Count);
        }
    }
}

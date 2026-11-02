// PCG Plugin - Merge Points node
// Part of the FlaxEngine PCG plugin. See docs/PCG_PLUGIN_DESIGN.md §5.1.4 note 10.

using System;
using FlaxEngine;
using PCG.Execution;

namespace PCG.Nodes
{
    /// <summary>
    /// Concatenates every incoming data set into one, keeping input order.
    /// </summary>
    /// <remarks>
    /// Attribute columns are unioned by name. A name that carries different types in two inputs is rejected
    /// loudly instead of being coerced: silently casting a string tag to zero would fabricate data that looks
    /// legitimate downstream. A column one input lacks is filled with that type's default, which the merge
    /// helper documents as the intended behaviour.
    /// </remarks>
    public sealed class PCGMergePointsNode : PCGNodeBase
    {
        /// <summary>Stable type id.</summary>
        public const string Id = "MergePoints";

        /// <summary>Number of parameters this node reads.</summary>
        public const int ParameterCount = 0;

        private readonly string _typeId;

        /// <summary>Creates the node from serialized parameters.</summary>
        public PCGMergePointsNode(PCGNodeRegistry.PCGNodeSerializationData data)
        {
            _typeId = Id;
        }

        /// <inheritdoc />
        public override string TypeId => _typeId;

        /// <inheritdoc />
        public override string DisplayName => "Merge Points";

        /// <inheritdoc />
        public override bool IsThreadSafe => true;

        /// <inheritdoc />
        public override PCGPinDefinition[] InputPins => new[]
        {
            new PCGPinDefinition("In", PCGPinDirection.Input, allowMultiple: true,
                tooltip: "Point sets to concatenate. Several edges may attach."),
        };

        /// <inheritdoc />
        public override PCGPinDefinition[] OutputPins => new[]
        {
            new PCGPinDefinition("Out", PCGPinDirection.Output, tooltip: "All input points in edge order."),
        };

        /// <inheritdoc />
        public override PCGData OnExecute(PCGExecutionContext context, PCGData[] inputs)
        {
            var aggregate = inputs[0];
            if (aggregate == null || aggregate.IsEmpty && aggregate.Parts == null)
                return PCGData.Empty;

            var parts = aggregate.Flatten();
            if (parts.Length == 0)
                return PCGData.Empty;

            // A single source still has to be republished: returning it untouched would let a downstream
            // modifier write straight into the upstream node's cached result.
            if (parts.Length == 1)
                return parts[0] == null ? PCGData.Empty : parts[0].Clone();

            var merged = PCGData.Merge(parts, out string error);
            if (error != null)
                throw new PCGNodeExecutionException($"{Id}: {error}");

            return merged;
        }
    }
}

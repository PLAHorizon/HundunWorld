// PCG Plugin - Branch (Control Flow)
// Part of the FlaxEngine PCG plugin. See docs/PCG_PLUGIN_DESIGN.md §5.1.21.

using System;
using FlaxEngine;
using PCG.Execution;

namespace PCG.Nodes
{
    /// <summary>
    /// Passes the input through, or blocks it, according to one boolean - the switch a graph parameter can flip.
    /// </summary>
    /// <remarks>
    /// <para>
    /// UE5's Switch cannot be expressed here: an edge carries no output pin index, so one node cannot route two
    /// ways (docs §5.1.8). Branch is the half of that family which does fit - one input, either republished or
    /// nothing - and it is the half a designer reaches for when they want to keep a half-finished layer wired
    /// but switched off.
    /// </para>
    /// <para>
    /// The condition is a node parameter, which makes it an exposed parameter, so a Volume can override it
    /// without touching the graph: the same city graph carries ruins in one region and not in another. That is
    /// the point of the node - "off" is per-placement, not per-graph.
    /// </para>
    /// <para>
    /// A blocked branch produces an empty layer rather than an error: switched off is a normal state, not a
    /// mis-wiring, and whatever is downstream sees the same thing a filter that matched nothing would give it.
    /// </para>
    /// </remarks>
    public sealed class PCGBranchNode : PCGNodeBase
    {
        /// <summary>Stable type id.</summary>
        public const string Id = "Branch";

        private const int PCondition = 0;

        /// <summary>Number of parameters this node reads.</summary>
        public const int ParameterCount = 1;

        private readonly string _typeId;
        private readonly PCGNodeParameters _params;

        /// <summary>When set the input passes; when clear the branch produces nothing.</summary>
        public bool Condition = true;

        /// <summary>Creates the node from serialized parameters.</summary>
        public PCGBranchNode(PCGNodeRegistry.PCGNodeSerializationData data)
        {
            _typeId = Id;
            _params = new PCGNodeParameters(data.Values, data.TypeId);

            Condition = _params.Get(PCondition, true);
        }

        /// <inheritdoc />
        public override string TypeId => _typeId;

        /// <inheritdoc />
        public override string DisplayName => "Branch";

        /// <inheritdoc />
        public override bool IsThreadSafe => true;

        /// <inheritdoc />
        public override PCGPinDefinition[] InputPins => new[]
        {
            new PCGPinDefinition("In", PCGPinDirection.Input, isRequired: true,
                tooltip: "Points to pass through when the condition is set."),
        };

        /// <inheritdoc />
        public override PCGPinDefinition[] OutputPins => new[]
        {
            new PCGPinDefinition("Out", PCGPinDirection.Output,
                tooltip: "The input when the condition is set; nothing otherwise."),
        };

        /// <inheritdoc />
        public override string DescribeParameters() => $"condition={(Condition ? "pass" : "block")}";

        /// <inheritdoc />
        public override PCGData OnExecute(PCGExecutionContext context, PCGData[] inputs)
        {
            var input = inputs != null && inputs.Length > 0 ? inputs[0] : null;
            if (input == null || input.Count == 0)
                return PCGData.Empty;

            // Republished rather than handed back: a downstream modifier writes into whatever it is given, and
            // the input here is the upstream node's cached result.
            return Condition ? input.Clone() : PCGData.Empty;
        }
    }
}

// PCG Plugin - Normal To Density
// Part of the FlaxEngine PCG plugin. See docs/PCG_PLUGIN_DESIGN.md §5.1.15.

using System;
using FlaxEngine;
using PCG.Execution;

namespace PCG.Nodes
{
    /// <summary>
    /// Turns how a point is facing into a density - flat ground is likely, a cliff face is not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The slope filter answers this from terrain, which works when a landscape sampler produced the points. A
    /// mesh sampler has no terrain under it and no slope column, but every point it emits carries a normal, so
    /// this is the node that lets a scattered rock get pebbles on its upper faces and none on its underside. UE5
    /// has the same node for the same reason.
    /// </para>
    /// <para>
    /// What is written is the clamped dot product of the point's up axis and the given direction, so the output
    /// is already a probability and can go straight into a spawner or into
    /// <see cref="PCGAttributeMathNode"/> next to other probabilities. The vertical axis of the point comes from
    /// its rotation, which is what a sampler aligned to a surface normal set earlier in the graph.
    /// </para>
    /// </remarks>
    public sealed class PCGNormalToDensityNode : PCGNodeBase
    {
        /// <summary>Stable type id.</summary>
        public const string Id = "NormalToDensity";

        // Parameter indices. Order is part of the saved graph format; append only.
        private const int PDirection = 0;
        private const int PTargetAttribute = 1;
        private const int PInvert = 2;

        /// <summary>Number of parameters this node reads.</summary>
        public const int ParameterCount = 3;

        private readonly string _typeId;
        private readonly PCGNodeParameters _params;

        /// <summary>Direction a point is measured against; up means "flat ground is the likely one".</summary>
        public Vector3 Direction = Vector3.Up;

        /// <summary>Column the result is written to; empty means the point's density.</summary>
        public string TargetAttribute = string.Empty;

        /// <summary>When set, the answer is flipped, so a point facing away from the direction scores high.</summary>
        public bool Invert;

        /// <summary>Creates the node from serialized parameters.</summary>
        public PCGNormalToDensityNode(PCGNodeRegistry.PCGNodeSerializationData data)
        {
            _typeId = Id;
            _params = new PCGNodeParameters(data.Values, data.TypeId);

            Direction = _params.GetVector3(PDirection, Vector3.Up);
            TargetAttribute = _params.Get(PTargetAttribute, string.Empty) ?? string.Empty;
            Invert = _params.Get(PInvert, false);

            // A zero direction has no "facing towards it" to measure; up is the answer that keeps the graph
            // producing a probability instead of a field of zeroes.
            if (Direction.LengthSquared < 1e-8f)
                Direction = Vector3.Up;

            Direction = Vector3.Normalize(Direction);
        }

        /// <inheritdoc />
        public override string TypeId => _typeId;

        /// <inheritdoc />
        public override string DisplayName => "Normal To Density";

        /// <inheritdoc />
        public override bool IsThreadSafe => true;

        /// <inheritdoc />
        public override PCGPinDefinition[] InputPins => new[]
        {
            new PCGPinDefinition("In", PCGPinDirection.Input, isRequired: true,
                tooltip: "Points whose facing is turned into a density."),
        };

        /// <inheritdoc />
        public override PCGPinDefinition[] OutputPins => new[]
        {
            new PCGPinDefinition("Out", PCGPinDirection.Output,
                tooltip: "The same points with the computed density."),
        };

        /// <inheritdoc />
        public override string DescribeParameters()
        {
            string target = string.IsNullOrEmpty(TargetAttribute) ? "$Density" : TargetAttribute;
            return $"{target} = dot(up, {Direction}){(Invert ? " inverted" : string.Empty)}";
        }

        /// <inheritdoc />
        public override PCGData OnExecute(PCGExecutionContext context, PCGData[] inputs)
        {
            var input = inputs[0];
            if (input == null || input.Count == 0)
                return PCGData.Empty;

            var result = input.Clone();
            var attributes = result.SharedAttributes;
            var points = result.Points;
            bool writeAttribute = !string.IsNullOrEmpty(TargetAttribute);

            if (writeAttribute)
            {
                if (attributes == null)
                {
                    attributes = new PCGAttributeSet(result.Count);
                    result.SharedAttributes = attributes;

                    for (int i = 0; i < points.Length; i++)
                        points[i].Attributes = attributes;
                }

                if (!attributes.Contains(TargetAttribute))
                    attributes.DeclareAttributeUnsafe(TargetAttribute, PCGAttributeType.Float);
            }

            var column = writeAttribute ? attributes.GetWritableFloatArray(TargetAttribute) : null;

            for (int i = 0; i < points.Length; i++)
            {
                context.ThrowIfCancelled(i);

                // The point's own up axis: a surface-aligned rotation set upstream is the normal, and an identity
                // rotation means the point is standing upright.
                var up = Vector3.Transform(Vector3.Up, points[i].Rotation);
                float facing = Vector3.Dot(Vector3.Normalize(up), Direction);
                float density = Math.Min(1.0f, Math.Max(0.0f, Invert ? 1.0f - facing : facing));

                if (writeAttribute)
                    column[i] = density;
                else
                    points[i].Density = density;
            }

            return result;
        }
    }
}

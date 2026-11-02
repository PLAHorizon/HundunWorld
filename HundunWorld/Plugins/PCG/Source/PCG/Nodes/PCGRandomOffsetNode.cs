// PCG Plugin - Random Offset node
// Part of the FlaxEngine PCG plugin. See docs/PCG_PLUGIN_DESIGN.md §5.1.3 and §3.4 note 7.

using System;
using FlaxEngine;
using PCG.Execution;

namespace PCG.Nodes
{
    /// <summary>
    /// Nudges, turns and rescales each point by a deterministic random amount.
    /// </summary>
    /// <remarks>
    /// The stream for a point is derived from <c>(graph seed, node id, point index)</c> rather than pulled
    /// from one sequential generator over the array. That is the property that lets a filter drop a point
    /// without every later point jumping to a new offset, and it is what makes an incremental re-run of a
    /// subset agree with a full re-run - see docs/PCG_PLUGIN_DESIGN.md §3.4 note 7.
    /// </remarks>
    public sealed class PCGRandomOffsetNode : PCGNodeBase
    {
        /// <summary>Stable type id.</summary>
        public const string Id = "RandomOffset";

        // Parameter indices. Order is part of the saved graph format; append only.
        private const int PMinOffset = 0;
        private const int PMaxOffset = 1;
        private const int PRotationJitterDegrees = 2;
        private const int PScaleMin = 3;
        private const int PScaleMax = 4;
        private const int PSeedOffset = 5;

        /// <summary>Number of parameters this node reads.</summary>
        public const int ParameterCount = 6;

        private readonly string _typeId;
        private readonly PCGNodeParameters _params;

        /// <summary>Lower bound of the position offset, in world units.</summary>
        public Vector3 MinOffset = new Vector3(-20f, 0f, -20f);

        /// <summary>Upper bound of the position offset, in world units.</summary>
        public Vector3 MaxOffset = new Vector3(20f, 0f, 20f);

        /// <summary>Maximum absolute rotation applied on each axis, in degrees.</summary>
        public Vector3 RotationJitterDegrees = new Vector3(0f, 180f, 0f);

        /// <summary>Lower bound of the multiplicative scale factor.</summary>
        public float ScaleMin = 1.0f;

        /// <summary>Upper bound of the multiplicative scale factor.</summary>
        public float ScaleMax = 1.0f;

        /// <summary>Added to the graph seed so two offset nodes in one graph do not agree.</summary>
        public int SeedOffset;

        /// <summary>Creates the node from serialized parameters.</summary>
        public PCGRandomOffsetNode(PCGNodeRegistry.PCGNodeSerializationData data)
        {
            _typeId = Id;
            _params = new PCGNodeParameters(data.Values, data.TypeId);

            MinOffset = _params.GetVector3(PMinOffset, MinOffset);
            MaxOffset = _params.GetVector3(PMaxOffset, MaxOffset);
            RotationJitterDegrees = _params.GetVector3(PRotationJitterDegrees, RotationJitterDegrees);
            ScaleMin = Math.Max(0.0001f, _params.Get(PScaleMin, 1.0f));
            ScaleMax = Math.Max(ScaleMin, _params.Get(PScaleMax, ScaleMin));
            SeedOffset = _params.Get(PSeedOffset, 0);
        }

        /// <inheritdoc />
        public override string TypeId => _typeId;

        /// <inheritdoc />
        public override string DisplayName => "Random Offset";

        /// <inheritdoc />
        public override bool IsThreadSafe => true;

        /// <inheritdoc />
        public override PCGPinDefinition[] InputPins => new[]
        {
            new PCGPinDefinition("In", PCGPinDirection.Input, isRequired: true,
                tooltip: "Points to jitter."),
        };

        /// <inheritdoc />
        public override PCGPinDefinition[] OutputPins => new[]
        {
            new PCGPinDefinition("Out", PCGPinDirection.Output, tooltip: "Jittered points."),
        };

        /// <inheritdoc />
        public override string DescribeParameters() =>
            $"offset=[{MinOffset}..{MaxOffset}], rotationJitter={RotationJitterDegrees}, " +
            $"scale=[{ScaleMin:F2},{ScaleMax:F2}], seedOffset={SeedOffset}";

        /// <inheritdoc />
        public override PCGData OnExecute(PCGExecutionContext context, PCGData[] inputs)
        {
            var input = inputs[0];
            if (input == null || input.Count == 0)
                return PCGData.Empty;

            var result = input.Clone();
            var points = result.Points;

            PCG.Execution.PCGParallel.For(points.Length, i =>
            {
                context.ThrowIfCancelled(i);

                var rng = new PCGDeterministicRng(context.Seed + SeedOffset, context.CurrentNodeId, i);
                var point = points[i];

                var offset = new Vector3(
                    rng.NextRange(MinOffset.X, MaxOffset.X),
                    rng.NextRange(MinOffset.Y, MaxOffset.Y),
                    rng.NextRange(MinOffset.Z, MaxOffset.Z));

                var jitter = new Vector3(
                    rng.NextRange(-Math.Abs(RotationJitterDegrees.X), Math.Abs(RotationJitterDegrees.X)),
                    rng.NextRange(-Math.Abs(RotationJitterDegrees.Y), Math.Abs(RotationJitterDegrees.Y)),
                    rng.NextRange(-Math.Abs(RotationJitterDegrees.Z), Math.Abs(RotationJitterDegrees.Z)));

                float scale = rng.NextRange(ScaleMin, ScaleMax);

                point.Position = point.Position + offset;

                var delta = PCGPointTransform.EulerDegrees(jitter);
                point.Rotation = Quaternion.Multiply(point.Rotation, delta);
                point.Scale = new Float3(point.Scale.X * scale, point.Scale.Y * scale, point.Scale.Z * scale);

                var half = PCGPointTransform.EnclosingExtents(PCGPointTransform.HalfExtents(point.Bounds), delta);
                point.Bounds = PCGPointTransform.MakeBox(point.Position, half * scale);

                points[i] = point;
            });

            return result;
        }
    }
}

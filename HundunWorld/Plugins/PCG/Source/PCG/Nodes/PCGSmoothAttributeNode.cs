// PCG Plugin - Smooth Attribute (Attribute ops)
// Part of the FlaxEngine PCG plugin. See docs/PCG_PLUGIN_DESIGN.md §5.1.13.

using System;
using FlaxEngine;
using PCG.Execution;

namespace PCG.Nodes
{
    /// <summary>
    /// Blurs an attribute column over the neighbourhood, so a value that jumps point to point becomes a gradient.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Raw columns are noisy by nature: a noise sampler's value changes every point, and a neighbourhood count is
    /// an integer that pops. Feeding either straight into density gives a scatter with hard patches of nothing
    /// next to hard patches of everything. One or two smoothing passes turn that into the gradient a designer
    /// expects, without another sampler pass and without leaving the column data.
    /// </para>
    /// <para>
    /// Each pass reads the previous pass's values from a snapshot and writes into a second buffer, so the result
    /// does not depend on the order points are visited - the same rule the relaxation node follows, and the only
    /// reason two runs of one graph agree.
    /// </para>
    /// <para>
    /// Smoothing a column is not the same as blurring positions: nothing moves. A point whose value was pulled
    /// towards its neighbours stays exactly where it was, which is what keeps the smoothing usable before a
    /// pruner that has already decided where things should be.
    /// </para>
    /// </remarks>
    public sealed class PCGSmoothAttributeNode : PCGNodeBase
    {
        /// <summary>Stable type id.</summary>
        public const string Id = "SmoothAttribute";

        // Parameter indices. Order is part of the saved graph format; append only.
        private const int PSourceAttribute = 0;
        private const int PTargetAttribute = 1;
        private const int PRadius = 2;
        private const int PIterations = 3;
        private const int PPlaneOnly = 4;
        private const int PIncludeSelf = 5;

        /// <summary>Number of parameters this node reads.</summary>
        public const int ParameterCount = 6;

        /// <summary>Ceiling on passes, so a typed-in number cannot turn a graph into a stall.</summary>
        public const int MaxIterations = 16;

        /// <summary>
        /// Scratch column used when a side is the point's density.
        /// </summary>
        /// <remarks>
        /// Deliberately not named "$Density": the dollar prefix is the plugin's convention for columns it owns,
        /// and a scratch buffer must never collide with one of those. It is left in the data afterwards, so the
        /// smoothed values are inspectable in the editor rather than only in the density field.
        /// </remarks>
        public const string WorkingAttribute = "__smoothed";

        private readonly string _typeId;
        private readonly PCGNodeParameters _params;

        /// <summary>Column read; empty means the point's density.</summary>
        public string SourceAttribute = string.Empty;

        /// <summary>Column written; empty writes back over the source column.</summary>
        public string TargetAttribute = string.Empty;

        /// <summary>Neighbours inside this distance contribute to the average.</summary>
        public float Radius = 150.0f;

        /// <summary>How many smoothing passes run.</summary>
        public int Iterations = 1;

        /// <summary>When set, the vertical axis is left out of the neighbourhood, matching ground scatter.</summary>
        public bool PlaneOnly = true;

        /// <summary>When set, a point's own value takes part in its average.</summary>
        public bool IncludeSelf = true;

        /// <summary>Creates the node from serialized parameters.</summary>
        public PCGSmoothAttributeNode(PCGNodeRegistry.PCGNodeSerializationData data)
        {
            _typeId = Id;
            _params = new PCGNodeParameters(data.Values, data.TypeId);

            SourceAttribute = _params.Get(PSourceAttribute, string.Empty) ?? string.Empty;
            TargetAttribute = _params.Get(PTargetAttribute, string.Empty) ?? string.Empty;
            Radius = Math.Max(0.0f, _params.Get(PRadius, 150.0f));
            Iterations = Math.Min(MaxIterations, Math.Max(0, _params.Get(PIterations, 1)));
            PlaneOnly = _params.Get(PPlaneOnly, true);
            IncludeSelf = _params.Get(PIncludeSelf, true);
        }

        /// <inheritdoc />
        public override string TypeId => _typeId;

        /// <inheritdoc />
        public override string DisplayName => "Smooth Attribute";

        /// <inheritdoc />
        public override bool IsThreadSafe => true;

        /// <inheritdoc />
        public override PCGPinDefinition[] InputPins => new[]
        {
            new PCGPinDefinition("In", PCGPinDirection.Input, isRequired: true, tooltip: "Points whose column is smoothed."),
        };

        /// <inheritdoc />
        public override PCGPinDefinition[] OutputPins => new[]
        {
            new PCGPinDefinition("Out", PCGPinDirection.Output,
                tooltip: "The same points, in the same places, with a smoother column."),
        };

        /// <inheritdoc />
        public override string DescribeParameters()
        {
            string source = string.IsNullOrEmpty(SourceAttribute) ? WorkingAttribute : SourceAttribute;
            string target = string.IsNullOrEmpty(TargetAttribute) ? "in place" : $"to {TargetAttribute}";
            return $"{source} {target}, radius={Radius:F1}, passes={Iterations}, " +
                   $"planeOnly={PlaneOnly}, includeSelf={IncludeSelf}";
        }

        /// <inheritdoc />
        public override PCGData OnExecute(PCGExecutionContext context, PCGData[] inputs)
        {
            var input = inputs[0];
            if (input == null || input.Count == 0)
                return PCGData.Empty;

            if (Radius <= 0.0f || Iterations == 0)
                return input.Clone();

            var result = input.Clone();
            var points = result.Points;
            var attributes = result.SharedAttributes;

            bool sourceIsColumn = !string.IsNullOrEmpty(SourceAttribute);
            bool targetIsColumn = !string.IsNullOrEmpty(TargetAttribute);

            if (sourceIsColumn && (attributes == null || !attributes.Contains(SourceAttribute)))
            {
                LastError = $"Smooth Attribute could not read '{SourceAttribute}': no such attribute on this " +
                            "layer. The layer passes through unchanged.";
                return result;
            }

            // The passes read and write columns, so a side that names the point's density is materialised into one
            // first - and written back to density at the end when that is where the answer belongs. Without this,
            // "smooth the density" would have to read a column that does not exist.
            if (attributes == null)
            {
                attributes = new PCGAttributeSet(result.Count);
                result.SharedAttributes = attributes;

                for (int i = 0; i < points.Length; i++)
                    points[i].Attributes = attributes;
            }

            // Where the values come from and where they go back. An empty target means "in place": back into the
            // source column when one was named, and back into the density when the source *was* the density. The
            // first version sent every unnamed target to the density, which made the documented in-place case a
            // silent no-op on the column that was actually named.
            string sourceName = sourceIsColumn ? SourceAttribute : WorkingAttribute;
            string targetName = targetIsColumn ? TargetAttribute : sourceName;
            bool writeBackToDensity = !targetIsColumn && !sourceIsColumn;

            EnsureColumn(attributes, sourceName);

            if (targetName != sourceName)
                EnsureColumn(attributes, targetName);

            if (!sourceIsColumn)
                SeedFromDensity(points, attributes, sourceName);

            for (int pass = 0; pass < Iterations; pass++)
                Smooth(points, attributes, sourceName, targetName);

            if (writeBackToDensity)
                CopyToDensity(points, attributes, targetName);

            return result;
        }

        private static void EnsureColumn(PCGAttributeSet attributes, string name)
        {
            if (!attributes.Contains(name))
                attributes.DeclareAttributeUnsafe(name, PCGAttributeType.Float);
        }

        private static void SeedFromDensity(PCGPoint[] points, PCGAttributeSet attributes, string name)
        {
            var column = attributes.GetWritableFloatArray(name);

            for (int i = 0; i < points.Length; i++)
                column[i] = points[i].Density;
        }

        private static void CopyToDensity(PCGPoint[] points, PCGAttributeSet attributes, string name)
        {
            var column = attributes.GetWritableFloatArray(name);

            for (int i = 0; i < points.Length; i++)
                points[i].Density = column[i];
        }

        /// <summary>One pass of neighbour averaging, read from a snapshot and written into the target column.</summary>
        private void Smooth(PCGPoint[] points, PCGAttributeSet attributes, string source, string target)
        {
            int count = points.Length;
            var current = new float[count];

            for (int i = 0; i < count; i++)
                current[i] = attributes.GetFloat(source, i);

            var index = new PCGPointIndex(Snapshot(points), Radius, PlaneOnly);
            float squaredRadius = Radius * Radius;
            var averages = new float[count];

            for (int i = 0; i < count; i++)
            {
                var origin = points[i].Position;
                double sum = IncludeSelf ? current[i] : 0.0;
                int samples = IncludeSelf ? 1 : 0;

                index.ForEachNearby(origin, other =>
                {
                    if (other == i)
                        return;

                    float dx = points[other].Position.X - origin.X;
                    float dz = points[other].Position.Z - origin.Z;
                    float squared = dx * dx + dz * dz;

                    if (!PlaneOnly)
                    {
                        float dy = points[other].Position.Y - origin.Y;
                        squared += dy * dy;
                    }

                    if (squared > squaredRadius)
                        return;

                    sum += current[other];
                    samples++;
                });

                averages[i] = samples > 0 ? (float)(sum / samples) : current[i];
            }

            var column = attributes.GetWritableFloatArray(target);

            for (int i = 0; i < count; i++)
                column[i] = averages[i];
        }

        /// <summary>
        /// A point array carrying the current positions, because the shared index reads positions out of a point
        /// array rather than a bare vector list.
        /// </summary>
        private static PCGPoint[] Snapshot(PCGPoint[] points)
        {
            var snapshot = new PCGPoint[points.Length];
            Array.Copy(points, snapshot, points.Length);
            return snapshot;
        }
    }
}

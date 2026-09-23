// PCG Plugin - Point data container
// Part of the FlaxEngine PCG plugin. See docs/PCG_PLUGIN_DESIGN.md §4.3.

using System;
using System.Collections.Generic;
using FlaxEngine;

namespace PCG
{
    /// <summary>
    /// A set of points flowing along graph edges.
    /// </summary>
    /// <remarks>
    /// Nodes exchange <see cref="PCGData"/> instances and may return one of their inputs directly
    /// (a filter that keeps everything) or build a new one. The attribute storage is shared by reference,
    /// so see the copy-on-write contract on <see cref="PCGAttributeSet"/>: builders of new data sets call
    /// <see cref="PCGAttributeSet.Share"/> once when handing the same storage to an additional data set.
    /// </remarks>
    public sealed class PCGData
    {
        /// <summary>An empty data set.</summary>
        public static readonly PCGData Empty = new PCGData(Array.Empty<PCGPoint>());

        /// <summary>Points in this data set.</summary>
        public PCGPoint[] Points;

        /// <summary>
        /// Custom attribute storage shared by the points, or null when the set carries no custom attributes.
        /// </summary>
        public PCGAttributeSet SharedAttributes;

        /// <summary>
        /// When this data set was produced by aggregating several sources into one multi-input pin, the
        /// individual sources; otherwise null.
        /// </summary>
        /// <remarks>
        /// A combiner node receives its multi-connection input as a single aggregate so the executor's input
        /// array stays uniformly typed. Combiners that need to reason about the sources separately (Merge,
        /// Difference, Intersection) read this and treat each part independently rather than working on an
        /// already-flattened blob they cannot attribute.
        /// </remarks>
        public PCGData[] Parts;

        /// <summary>True when this set is an aggregate of several parts rather than a plain point set.</summary>
        public bool IsAggregate => Parts != null;

        /// <summary>Creates an empty data set.</summary>
        public PCGData()
        {
            Points = Array.Empty<PCGPoint>();
        }

        /// <summary>Creates a data set over an existing point array, taking ownership of it.</summary>
        public PCGData(PCGPoint[] points)
        {
            Points = points ?? Array.Empty<PCGPoint>();
        }

        /// <summary>Creates a data set over an existing point array and attribute storage.</summary>
        public PCGData(PCGPoint[] points, PCGAttributeSet attributes)
        {
            Points = points ?? Array.Empty<PCGPoint>();
            SharedAttributes = attributes;
        }

        /// <summary>Gets the number of points.</summary>
        public int Count => Points?.Length ?? 0;

        /// <summary>True when this set holds no points.</summary>
        public bool IsEmpty => Count == 0;

        /// <summary>
        /// Creates a new set sized for <paramref name="count"/> points with an owned attribute set.
        /// Use this in samplers, which produce fresh points.
        /// </summary>
        public static PCGData Create(int count)
        {
            var points = count > 0 ? new PCGPoint[count] : Array.Empty<PCGPoint>();
            var attributes = new PCGAttributeSet(count);
            return new PCGData(points, attributes);
        }

        /// <summary>
        /// Returns a copy of this set whose attribute storage is safe to write to.
        /// </summary>
        /// <remarks>
        /// Points are copied by value, which copies the <see cref="PCGPoint.Attributes"/> reference; the
        /// storage is detached separately. Call this before modifying points of a data set that came from
        /// another node and may be shared.
        /// </remarks>
        public PCGData Clone(bool clonePoints = true, bool cloneAttributes = true)
        {
            var points = clonePoints ? (PCGPoint[])Points.Clone() : Points;
            var attributes = cloneAttributes && SharedAttributes != null ? SharedAttributes.Clone() : SharedAttributes;
            var result = new PCGData(points, attributes);

            // Re-point every point at the storage the new data set owns, otherwise the points would keep
            // referring to the old shared instance and the clone would be pointless.
            if (cloneAttributes && attributes != null)
            {
                for (int i = 0; i < result.Points.Length; i++)
                {
                    result.Points[i].Attributes = attributes;
                }
            }

            return result;
        }

        /// <summary>
        /// Builds a new data set containing only the rows in <paramref name="indices"/>, compacted in order.
        /// </summary>
        public static PCGData Gather(PCGData source, int[] indices, int count)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            if (indices == null)
                throw new ArgumentNullException(nameof(indices));

            if (count < 0 || count > indices.Length)
                throw new ArgumentOutOfRangeException(nameof(count));

            var points = count > 0 ? new PCGPoint[count] : Array.Empty<PCGPoint>();
            for (int i = 0; i < count; i++)
                points[i] = source.Points[indices[i]];

            var attributes = source.SharedAttributes?.Gather(indices, count);

            if (attributes != null)
            {
                for (int i = 0; i < count; i++)
                    points[i].Attributes = attributes;
            }

            return new PCGData(points, attributes);
        }

        /// <summary>
        /// Concatenates data sets into one. Attribute columns are unioned by name; see the merge rules in
        /// docs/PCG_PLUGIN_DESIGN.md §5.1.4 note 10.
        /// </summary>
        /// <param name="parts">Data sets to merge. Null entries are skipped.</param>
        /// <param name="error">Set to a message when the merge is rejected, otherwise null.</param>
        public static PCGData Merge(PCGData[] parts, out string error)
        {
            error = null;

            if (parts == null || parts.Length == 0)
                return Empty;

            int total = 0;
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i] != null)
                    total += parts[i].Count;
            }

            if (total == 0)
                return Empty;

            // Collect the union of attribute names and reject type conflicts up front, so a partially
            // built result is never produced on error.
            var merged = new System.Collections.Generic.Dictionary<string, PCGAttributeType>(StringComparer.Ordinal);
            for (int p = 0; p < parts.Length; p++)
            {
                var part = parts[p];
                if (part?.SharedAttributes == null)
                    continue;

                foreach (var descriptor in part.SharedAttributes.GetCustomDescriptors())
                {
                    if (merged.TryGetValue(descriptor.Name, out var existingType))
                    {
                        if (existingType != descriptor.Type)
                        {
                            error = $"Merge rejected: attribute '{descriptor.Name}' is {existingType} in one input " +
                                    $"and {descriptor.Type} in another.";
                            return null;
                        }
                    }
                    else
                    {
                        merged.Add(descriptor.Name, descriptor.Type);
                    }
                }
            }

            var points = new PCGPoint[total];
            PCGAttributeSet attributes = merged.Count > 0 ? new PCGAttributeSet(total) : null;

            if (attributes != null)
            {
                foreach (var kv in merged)
                    attributes.DeclareAttributeUnsafe(kv.Key, kv.Value);
            }

            int offset = 0;
            for (int p = 0; p < parts.Length; p++)
            {
                var part = parts[p];
                if (part == null || part.Count == 0)
                    continue;

                Array.Copy(part.Points, 0, points, offset, part.Count);

                if (attributes != null)
                {
                    // Copy each column, filling with the type default when a part lacks that attribute.
                    // Deliberately no value invention: a missing attribute becomes zero/default and the
                    // node reports it, rather than silently fabricating plausible-looking data.
                    foreach (var kv in merged)
                    {
                        CopyColumn(part.SharedAttributes, kv.Key, kv.Value, attributes, offset, part.Count);
                    }
                }

                offset += part.Count;
            }

            if (attributes != null)
            {
                for (int i = 0; i < total; i++)
                    points[i].Attributes = attributes;
            }

            return new PCGData(points, attributes);
        }

        private static void CopyColumn(
            PCGAttributeSet source, string name, PCGAttributeType type,
            PCGAttributeSet destination, int destinationOffset, int count)
        {
            // A part that does not carry this attribute contributes defaults, which the destination
            // columns already hold.
            if (source == null || !source.Contains(name))
                return;

            switch (type)
            {
            case PCGAttributeType.Float:
            {
                var src = source.TryGetFloatArray(name);
                if (src == null) return;
                var dst = destination.GetWritableFloatArray(name);
                Array.Copy(src, 0, dst, destinationOffset, count);
                break;
            }
            case PCGAttributeType.Vector3:
            {
                var src = source.TryGetVector3Array(name);
                if (src == null) return;
                var dst = destination.GetWritableVector3Array(name);
                Array.Copy(src, 0, dst, destinationOffset, count);
                break;
            }
            case PCGAttributeType.Int:
            {
                if (!source.Contains(name)) return;
                var dst = destination.GetWritableIntArray(name);
                for (int i = 0; i < count; i++) dst[destinationOffset + i] = source.GetInt(name, i);
                break;
            }
            case PCGAttributeType.Bool:
            {
                var dst = destination.GetWritableBoolArray(name);
                for (int i = 0; i < count; i++) dst[destinationOffset + i] = source.GetBool(name, i);
                break;
            }
            case PCGAttributeType.Vector2:
            {
                var dst = destination.GetWritableVector2Array(name);
                for (int i = 0; i < count; i++) dst[destinationOffset + i] = source.GetVector2(name, i);
                break;
            }
            case PCGAttributeType.Vector4:
            {
                var dst = destination.GetWritableVector4Array(name);
                for (int i = 0; i < count; i++) dst[destinationOffset + i] = source.GetVector4(name, i);
                break;
            }
            case PCGAttributeType.Quaternion:
            {
                var dst = destination.GetWritableQuaternionArray(name);
                for (int i = 0; i < count; i++) dst[destinationOffset + i] = source.GetQuaternion(name, i);
                break;
            }
            case PCGAttributeType.String:
            {
                var dst = destination.GetWritableStringArray(name);
                for (int i = 0; i < count; i++) dst[destinationOffset + i] = source.GetString(name, i);
                break;
            }
            }
        }

        /// <summary>
        /// Wraps several data sets as a single aggregate for delivery over a multi-connection pin.
        /// The parts are not copied; they are referenced until the aggregate is consumed.
        /// </summary>
        public static PCGData Aggregate(PCGData[] parts)
        {
            if (parts == null || parts.Length == 0)
                return Empty;

            // A single part needs no wrapping.
            if (parts.Length == 1)
                return parts[0] ?? Empty;

            return new PCGData(Array.Empty<PCGPoint>())
            {
                Parts = parts,
            };
        }

        /// <summary>
        /// Flattens this set into its constituent parts. A plain point set yields itself.
        /// </summary>
        public PCGData[] Flatten()
        {
            if (Parts == null)
                return new[] { this };

            var result = new List<PCGData>(Parts.Length);
            for (int i = 0; i < Parts.Length; i++)
            {
                if (Parts[i] == null)
                    continue;

                if (Parts[i].Parts != null)
                    result.AddRange(Parts[i].Flatten());
                else
                    result.Add(Parts[i]);
            }

            return result.ToArray();
        }

        /// <summary>Returns the combined world-space bounds of all points, or an empty box when there are none.</summary>
        public BoundingBox ComputeBounds()
        {
            if (Count == 0)
                return BoundingBox.Empty;

            var result = Points[0].Bounds;
            for (int i = 1; i < Points.Length; i++)
                result = BoundingBox.Merge(result, Points[i].Bounds);

            return result;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"PCGData(Points={Count}, Attributes={(SharedAttributes != null ? SharedAttributes.AttributeCount : 0)})";
        }
    }
}

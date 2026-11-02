// PCG Plugin - Self-Pruning Filter node
// Part of the FlaxEngine PCG plugin. See docs/PCG_PLUGIN_DESIGN.md §5.1.2.

using System;
using System.Collections.Generic;
using FlaxEngine;
using PCG.Execution;

namespace PCG.Nodes
{
    /// <summary>
    /// Removes points that crowd each other, keeping the denser one of each conflicting pair.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Naive pairwise testing is O(n squared): ten thousand points would be fifty million comparisons, which
    /// is why this node uses a uniform grid instead. The cell size is chosen to be at least as large as the
    /// conflict distance, so a conflict can only ever reach into the 27-cell neighbourhood of the candidate.
    /// </para>
    /// <para>
    /// In plane mode the Y component is deliberately left out of the cell key. Two points at the same XZ but
    /// a hundred units apart vertically are still a conflict there, and keying the grid on Y would place them
    /// outside each other's neighbourhood and silently let both through.
    /// </para>
    /// <para>
    /// Points are visited highest-density first, so "which of a crowded pair survives" is decided by the
    /// designer's density rather than by array order. Survivors are emitted in original point order to keep
    /// downstream nodes stable.
    /// </para>
    /// </remarks>
    public sealed class PCGSelfPruningFilterNode : PCGNodeBase
    {
        /// <summary>Stable type id.</summary>
        public const string Id = "SelfPruningFilter";

        // Parameter indices. Order is part of the saved graph format; append only.
        private const int PMinDistance = 0;
        private const int PUseBoundsOverlap = 1;
        private const int PPlaneOnly = 2;

        /// <summary>Number of parameters this node reads.</summary>
        public const int ParameterCount = 3;

        private const float Epsilon = 1e-4f;

        private readonly string _typeId;
        private readonly PCGNodeParameters _params;

        /// <summary>
        /// Minimum centre-to-centre distance kept between surviving points, in world units. Zero falls back to
        /// the bounds-overlap test, since a zero spacing would reject nothing at all.
        /// </summary>
        public float MinDistance = 100.0f;

        /// <summary>When set, points conflict by axis-aligned bounds overlap instead of centre distance.</summary>
        public bool UseBoundsOverlap;

        /// <summary>When set, the vertical axis is ignored, which is what ground scatter needs.</summary>
        public bool PlaneOnly = true;

        /// <summary>Creates the node from serialized parameters.</summary>
        public PCGSelfPruningFilterNode(PCGNodeRegistry.PCGNodeSerializationData data)
        {
            _typeId = Id;
            _params = new PCGNodeParameters(data.Values, data.TypeId);

            MinDistance = Math.Max(0.0f, _params.Get(PMinDistance, 100.0f));
            UseBoundsOverlap = _params.Get(PUseBoundsOverlap, false);
            PlaneOnly = _params.Get(PPlaneOnly, true);
        }

        /// <inheritdoc />
        public override string TypeId => _typeId;

        /// <inheritdoc />
        public override string DisplayName => "Self-Pruning Filter";

        /// <inheritdoc />
        public override bool IsThreadSafe => true;

        /// <inheritdoc />
        public override PCGPinDefinition[] InputPins => new[]
        {
            new PCGPinDefinition("In", PCGPinDirection.Input, isRequired: true,
                tooltip: "Points to prune."),
        };

        /// <inheritdoc />
        public override PCGPinDefinition[] OutputPins => new[]
        {
            new PCGPinDefinition("Out", PCGPinDirection.Output, tooltip: "Points with the crowd removed."),
        };

        /// <inheritdoc />
        public override string DescribeParameters() =>
            $"minDistance={MinDistance:F1}, overlap={UseBoundsOverlap}, planeOnly={PlaneOnly}";

        /// <inheritdoc />
        public override PCGData OnExecute(PCGExecutionContext context, PCGData[] inputs)
        {
            var input = inputs[0];
            if (input == null || input.Count == 0)
                return PCGData.Empty;

            var points = input.Points;
            int n = points.Length;

            float cell = CellSize(points);
            float minDistanceSquared = UseBoundsOverlap ? 0.0f : MinDistance * MinDistance;

            // Densest first; ties break on the point index so the outcome does not depend on dictionary or
            // sort implementation details.
            var order = new int[n];
            for (int i = 0; i < n; i++)
                order[i] = i;

            Array.Sort(order, (a, b) =>
            {
                int byDensity = points[b].Density.CompareTo(points[a].Density);
                return byDensity != 0 ? byDensity : a.CompareTo(b);
            });

            // The per-pair rule lives in one delegate now, so the grid can be swapped without changing what a
            // conflict means. Distance mode and bounds-overlap mode still decide exactly as before, including the
            // zero-spacing fall-through to overlap.
            //
            // This was tested against carrying the rule as a value struct whose Hit inlines (measured with the
            // bench's interleaved probe, both forms on one point set in one process): identical survivor counts,
            // and no measurable time difference. The delegate stays because it is the smaller statement of the
            // intent, not because the alternative was untried.
            Func<PCGPoint, PCGPoint, bool> conflicts = minDistanceSquared > 0.0f
                ? (Func<PCGPoint, PCGPoint, bool>)((a, b) =>
                {
                    float dx = a.Position.X - b.Position.X;
                    float dz = a.Position.Z - b.Position.Z;
                    float squared = dx * dx + dz * dz;

                    if (!PlaneOnly)
                    {
                        float dy = a.Position.Y - b.Position.Y;
                        squared += dy * dy;
                    }

                    return squared < minDistanceSquared;
                })
                : BoxesOverlapStatic;

            var grid = new SurvivorGrid(points, cell, PlaneOnly);
            var survivors = new List<int>(n);

            for (int k = 0; k < n; k++)
            {
                int index = order[k];
                context.ThrowIfCancelled(k, 32);

                // Only already-accepted points are in the grid, so "no conflict here" is the same question the
                // old dictionary-of-lists asked; the grid shape is what changed.
                if (!grid.ConflictsWithSurvivor(points, index, conflicts))
                {
                    grid.Add(points[index].Position, index);
                    survivors.Add(index);
                }
            }

            if (survivors.Count == n)
                return input.Clone();

            if (survivors.Count == 0)
                return PCGData.Empty;

            survivors.Sort();
            return PCGData.Gather(input, survivors.ToArray(), survivors.Count);
        }

        /// <summary>
        /// The reach of the test this node will actually run, which is not the same question as which flags
        /// were set.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A zero spacing falls through to the bounds-overlap test rather than pruning nothing, so a cell sized
        /// from "spacing" would be a hair wide while the test compares boxes that reach several units - and the
        /// one-ring scan would then miss real overlaps and let crowded props survive. The brute-force reference
        /// in the harness caught exactly that, on input where spacing was zero.
        /// </para>
        /// <para>
        /// Two axis-aligned boxes can only intersect when their centres are within the sum of their half-extents
        /// on every axis, so doubling the largest half-extent is the smallest cell that keeps a true conflict
        /// inside the neighbourhood. In plane mode the vertical axis is folded out of the bucketing, so every
        /// height already shares a column and only X and Z need the reach.
        /// </para>
        /// </remarks>
        private float CellSize(PCGPoint[] points)
        {
            bool distanceTest = !UseBoundsOverlap && MinDistance > 0.0f;
            if (distanceTest)
                return Math.Max(MinDistance, Epsilon);

            float largest = 0.0f;
            for (int i = 0; i < points.Length; i++)
            {
                var half = PCGPointTransform.HalfExtents(points[i].Bounds);
                largest = PlaneOnly
                    ? Math.Max(largest, Math.Max(half.X, half.Z))
                    : Math.Max(largest, Math.Max(half.X, Math.Max(half.Y, half.Z)));
            }

            return Math.Max(largest * 2.0f, Epsilon);
        }

        private static bool BoxesOverlapStatic(PCGPoint a, PCGPoint b) => BoxesOverlap(in a, in b);

        /// <summary>
        /// Cells holding the points already accepted by the greedy pass.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Plane mode - which is the default, and what ground scatter uses - folds the vertical axis out of the
        /// key, so the occupied cells form a flat rectangle over the point set. That makes the neighbourhood a
        /// direct index into two arrays instead of nine hash lookups per point: a head slot per cell, and one
        /// link per point. Survivors chain into their cell, so there is no per-cell list to allocate and no
        /// count to precompute.
        /// </para>
        /// <para>
        /// A very sparse footprint (a handful of points spread over a huge region) would make the head array
        /// absurd, so that case falls back to hashed buckets. The result cannot differ between the two: the
        /// conflict question is "does any accepted point conflict", and a cell's chain order is irrelevant to
        /// it - the harness pins that by comparing both against a brute-force reference.
        /// </para>
        /// </remarks>
        private sealed class SurvivorGrid
        {
            private const int MaxDenseCells = 1 << 20;
            private const int DenseCellBudget = 12;

            private readonly int[] _head;
            private readonly int[] _next;
            private readonly float _cell;
            private readonly bool _planeOnly;
            private readonly int _minX;
            private readonly int _minZ;
            private readonly int _width;
            private readonly int _depth;

            // Fallback shape.
            private readonly Dictionary<long, List<int>> _buckets;

            internal SurvivorGrid(PCGPoint[] points, float cell, bool planeOnly)
            {
                _cell = cell;
                _planeOnly = planeOnly;
                _next = new int[points.Length];
                Array.Fill(_next, -1);

                int minX = int.MaxValue, maxX = int.MinValue;
                int minZ = int.MaxValue, maxZ = int.MinValue;

                for (int i = 0; i < points.Length; i++)
                {
                    var position = points[i].Position;
                    int cx = (int)MathF.Floor(position.X / cell);
                    int cz = (int)MathF.Floor(position.Z / cell);

                    if (cx < minX) minX = cx;
                    if (cx > maxX) maxX = cx;
                    if (cz < minZ) minZ = cz;
                    if (cz > maxZ) maxZ = cz;
                }

                long width = (long)maxX - minX + 1;
                long height = (long)maxZ - minZ + 1;
                long cells = planeOnly ? width * height : long.MaxValue;

                // The dense layout pays for itself only while the footprint is comparable to the point count.
                bool dense = planeOnly &&
                             cells <= MaxDenseCells &&
                             cells <= (long)points.Length * DenseCellBudget;

                if (dense)
                {
                    _minX = minX;
                    _minZ = minZ;
                    _width = (int)width;
                    _depth = (int)height;
                    _head = new int[(int)cells];
                    Array.Fill(_head, -1);
                }
                else
                {
                    _buckets = new Dictionary<long, List<int>>(points.Length / 2 + 1);
                }
            }

            internal void Add(in Vector3 position, int index)
            {
                if (_head != null)
                {
                    int slot = SlotOf(position, 0, 0);
                    if (slot >= 0)
                    {
                        _next[index] = _head[slot];
                        _head[slot] = index;
                        return;
                    }
                }

                long key = KeyAt(position, 0, 0, 0);
                if (!_buckets.TryGetValue(key, out var bucket))
                {
                    bucket = new List<int>(4);
                    _buckets[key] = bucket;
                }

                bucket.Add(index);
            }

            internal bool ConflictsWithSurvivor(PCGPoint[] points, int self, Func<PCGPoint, PCGPoint, bool> conflicts)
            {
                var position = points[self].Position;

                if (_head != null)
                {
                    int cx = (int)MathF.Floor(position.X / _cell) - _minX;
                    int cz = (int)MathF.Floor(position.Z / _cell) - _minZ;

                    for (int dz = -1; dz <= 1; dz++)
                    {
                        int row = cz + dz;
                        if (row < 0 || row >= _depth)
                            continue;

                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int col = cx + dx;
                            if (col < 0 || col >= _width)
                                continue;

                            for (int other = _head[row * _width + col]; other >= 0; other = _next[other])
                            {
                                if (other != self && conflicts(points[self], points[other]))
                                    return true;
                            }
                        }
                    }

                    return false;
                }

                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (!_buckets.TryGetValue(KeyAt(position, dx, dy, dz), out var bucket))
                                continue;

                            for (int b = 0; b < bucket.Count; b++)
                            {
                                int other = bucket[b];
                                if (other != self && conflicts(points[self], points[other]))
                                    return true;
                            }
                        }
                    }
                }

                return false;
            }

            private int SlotOf(in Vector3 position, int dx, int dz)
            {
                int col = (int)MathF.Floor(position.X / _cell) - _minX + dx;
                int row = (int)MathF.Floor(position.Z / _cell) - _minZ + dz;

                if (col < 0 || col >= _width || row < 0 || row >= _depth)
                    return -1;

                return row * _width + col;
            }

            private long KeyAt(in Vector3 position, int dx, int dy, int dz)
            {
                int cx = (int)MathF.Floor(position.X / _cell) + dx;
                int cy = (_planeOnly ? 0 : (int)MathF.Floor(position.Y / _cell)) + dy;
                int cz = (int)MathF.Floor(position.Z / _cell) + dz;

                unchecked
                {
                    return ((long)cx * 73856093L) ^ ((long)cy * 19349663L) ^ ((long)cz * 83492791L);
                }
            }
        }

        private static bool BoxesOverlap(in PCGPoint a, in PCGPoint b)
        {
            // Strict inequalities: boxes that merely touch are not a crowd. A jittered grid tiles its cells so
            // exactly that neighbouring points touch, and treating contact as overlap would prune all but one.
            bool x = a.Bounds.Minimum.X < b.Bounds.Maximum.X && b.Bounds.Minimum.X < a.Bounds.Maximum.X;
            bool z = a.Bounds.Minimum.Z < b.Bounds.Maximum.Z && b.Bounds.Minimum.Z < a.Bounds.Maximum.Z;
            if (!x || !z)
                return false;

            bool y = a.Bounds.Minimum.Y < b.Bounds.Maximum.Y && b.Bounds.Minimum.Y < a.Bounds.Maximum.Y;
            return y;
        }
    }
}

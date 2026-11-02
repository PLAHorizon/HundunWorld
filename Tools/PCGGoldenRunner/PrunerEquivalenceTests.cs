// PCG Plugin - Equivalence tests for the self-pruning filter's grid.
//
// The pruning rule is a greedy pass in priority order: densest first, ties by index, a point survives when no
// already-surviving point conflicts with it. That is the specification, and it can be written down directly in
// the slow obvious way - compare every point against every earlier point. The grid is only allowed to change how
// fast that answers, never what it answers, so every case here runs both implementations over identical input
// and requires identical output. A brute-force reference is worth more than a comparison against the previous
// grid code, because the previous grid code could itself have been wrong.

using System;
using System.Collections.Generic;
using FlaxEngine;
using PCG;
using PCG.Execution;
using PCG.Graph;
using PCG.Nodes;

using CheckDelegate = PCGGoldenRunner.NodeBehaviourTests.CheckDelegate;

namespace PCGGoldenRunner
{
    internal static class PrunerEquivalenceTests
    {
        private static CheckDelegate _check;

        // Condition first, so an assertion reads as "this holds, because ...".
        private static void check(bool ok, string what, string detail = null) => _check(what, ok, detail);

        internal static void Run(CheckDelegate callback)
        {
            _check = callback;

            PCGNodeRegistry.Clear();
            PCGNodeRegistry.Register(PCGSelfPruningFilterNode.Id, "Filter",
                data => new PCGSelfPruningFilterNode(data), "Self-Pruning Filter");

            try
            {
                foreach (float spacing in new[] { 1.0f, 12.0f, 60.0f, 250.0f })
                {
                    foreach (bool planeOnly in new[] { true, false })
                    {
                        CompareCase("uniform grid", RandomPoints(400, 777 + (int)spacing), spacing,
                            useBoundsOverlap: false, planeOnly);

                        // A few points spread across a continent-sized footprint: the dense layout would need
                        // absurd memory, so this is the case that has to fall back - and the fallback must still
                        // answer like the specification.
                        CompareCase("sparse footprint", SparsePoints(60, 3131 + (int)spacing), spacing,
                            useBoundsOverlap: false, planeOnly);
                        CompareCase("clustered points", ClusteredPoints(350, 909 + (int)spacing), spacing,
                            useBoundsOverlap: false, planeOnly);
                        CompareCase("identical positions", DuplicatePoints(60, 42), spacing,
                            useBoundsOverlap: false, planeOnly);
                        CompareCase("equal densities", FlatDensityPoints(240, 5150 + (int)spacing), spacing,
                            useBoundsOverlap: false, planeOnly);
                    }

                    // The other half of the node: conflict by axis-aligned bounds overlap instead of distance.
                    CompareCase("uniform grid", RandomPoints(300, 1234 + (int)spacing), spacing,
                        useBoundsOverlap: true, planeOnly: true);
                    CompareCase("clustered points", ClusteredPoints(260, 4321 + (int)spacing), spacing,
                        useBoundsOverlap: true, planeOnly: false);
                }

                foreach (float spacing in new[] { 0.0f, 5.0f })
                {
                    CompareCase("mixed densities", MixedDensityPoints(320, 2468), spacing,
                        useBoundsOverlap: false, planeOnly: true);
                }
            }
            finally
            {
                PCGNodeRegistry.Clear();
            }
        }

        // ---- the two implementations ----------------------------------------

        private static void CompareCase(string label, PCGPoint[] points, float spacing, bool useBoundsOverlap, bool planeOnly)
        {
            var data = new PCGData(points);

            var node = new PCGSelfPruningFilterNode(new PCGNodeRegistry.PCGNodeSerializationData(
                PCGSelfPruningFilterNode.Id,
                new object[] { spacing, useBoundsOverlap, planeOnly },
                3));

            var context = new PCGExecutionContext { Seed = 5, CurrentNodeId = 3 };
            var viaGrid = node.OnExecute(context, new[] { data });
            var reference = BruteForce(points, spacing, useBoundsOverlap, planeOnly);

            string name = $"{label}: grid matches brute force (spacing {spacing:0.#}, " +
                          $"{(useBoundsOverlap ? "bounds" : "distance")}, {(planeOnly ? "plane" : "3d")})";

            if (viaGrid == null || viaGrid.Count != reference.Count)
            {
                check(false, name, $"{viaGrid?.Count ?? -1} survived vs {reference.Count}");
                return;
            }

            int mismatches = 0;
            for (int i = 0; i < viaGrid.Count; i++)
            {
                if (Distance(viaGrid.Points[i].Position, reference.Points[i].Position) > 1e-5f ||
                    !PointsEqual(viaGrid.Points[i], reference.Points[i]))
                {
                    mismatches++;
                }
            }

            check(mismatches == 0, name, mismatches == 0 ? $"{reference.Count} survived" : $"mismatches={mismatches}");
        }

        /// <summary>
        /// The specification, written the slow way: no grid, no buckets, every earlier survivor compared.
        /// </summary>
        private static PCGData BruteForce(PCGPoint[] points, float spacing, bool useBoundsOverlap, bool planeOnly)
        {
            int n = points.Length;

            var order = new int[n];
            for (int i = 0; i < n; i++)
                order[i] = i;

            Array.Sort(order, (a, b) =>
            {
                int byDensity = points[b].Density.CompareTo(points[a].Density);
                return byDensity != 0 ? byDensity : a.CompareTo(b);
            });

            bool distanceMode = !useBoundsOverlap && spacing > 0.0f;
            float squared = distanceMode ? spacing * spacing : 0.0f;

            var survivors = new List<int>();

            for (int k = 0; k < n; k++)
            {
                int index = order[k];
                var self = points[index];

                bool conflict = false;
                for (int s = 0; s < survivors.Count; s++)
                {
                    var other = points[survivors[s]];

                    if (distanceMode)
                    {
                        float dx = self.Position.X - other.Position.X;
                        float dz = self.Position.Z - other.Position.Z;
                        float sum = dx * dx + dz * dz;

                        if (!planeOnly)
                        {
                            float dy = self.Position.Y - other.Position.Y;
                            sum += dy * dy;
                        }

                        if (sum < squared)
                        {
                            conflict = true;
                            break;
                        }

                        continue;
                    }

                    // Bounds-overlap mode, including the case where a zero spacing falls through to it.
                    if (Overlap(self, other))
                    {
                        conflict = true;
                        break;
                    }
                }

                if (!conflict)
                    survivors.Add(index);
            }

            survivors.Sort();

            if (survivors.Count == 0)
                return PCGData.Empty;

            var indices = new int[survivors.Count];
            for (int i = 0; i < survivors.Count; i++)
                indices[i] = survivors[i];

            return PCGData.Gather(new PCGData(points), indices, survivors.Count);
        }

        private static bool Overlap(in PCGPoint a, in PCGPoint b)
        {
            bool x = a.Bounds.Minimum.X < b.Bounds.Maximum.X && b.Bounds.Minimum.X < a.Bounds.Maximum.X;
            bool z = a.Bounds.Minimum.Z < b.Bounds.Maximum.Z && b.Bounds.Minimum.Z < a.Bounds.Maximum.Z;
            if (!x || !z)
                return false;

            // The node's bounds-overlap mode tests all three axes even when the spacing rule is plane-only;
            // the reference mirrors that rather than "fixing" it, so an equivalence failure means the grid
            // changed the answer, not that the two disagree about what plane mode should mean.
            return a.Bounds.Minimum.Y < b.Bounds.Maximum.Y && b.Bounds.Minimum.Y < a.Bounds.Maximum.Y;
        }

        private static float Distance(in Vector3 a, in Vector3 b) =>
            (float)Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y) + (a.Z - b.Z) * (a.Z - b.Z));

        private static bool PointsEqual(in PCGPoint a, in PCGPoint b) =>
            Math.Abs(a.Density - b.Density) < 1e-6f &&
            Math.Abs(a.Scale.X - b.Scale.X) < 1e-6f &&
            Math.Abs(a.Rotation.X - b.Rotation.X) < 1e-6f &&
            Math.Abs(a.Rotation.Y - b.Rotation.Y) < 1e-6f &&
            Math.Abs(a.Rotation.Z - b.Rotation.Z) < 1e-6f &&
            Math.Abs(a.Rotation.W - b.Rotation.W) < 1e-6f;

        // ---- fixtures -------------------------------------------------------

        private static PCGPoint Point(float x, float y, float z, float density, int index)
        {
            var point = PCGPoint.Create(new Vector3(x, y, z), density, index);
            point.UpdateBounds(new Vector3(3.0f, 3.0f, 3.0f));
            return point;
        }

        private static PCGPoint[] RandomPoints(int count, int seed)
        {
            var points = new PCGPoint[count];
            var rng = new PCGDeterministicRng(seed, 1, 0);

            for (int i = 0; i < count; i++)
            {
                points[i] = Point(
                    rng.NextRange(-100.0f, 100.0f),
                    rng.NextRange(-20.0f, 20.0f),
                    rng.NextRange(-100.0f, 100.0f),
                    rng.NextFloat(),
                    i);
            }

            return points;
        }

        private static PCGPoint[] SparsePoints(int count, int seed)
        {
            var points = new PCGPoint[count];
            var rng = new PCGDeterministicRng(seed, 6, 0);

            for (int i = 0; i < count; i++)
            {
                // Two far-flung camps: the occupied cells are almost all empty.
                float camp = i % 2 == 0 ? -1_500_000.0f : 1_500_000.0f;

                points[i] = Point(
                    camp + rng.NextRange(-40.0f, 40.0f),
                    rng.NextRange(-5.0f, 5.0f),
                    rng.NextRange(-40.0f, 40.0f),
                    rng.NextFloat(),
                    i);
            }

            return points;
        }

        private static PCGPoint[] ClusteredPoints(int count, int seed)
        {
            var points = new PCGPoint[count];
            var rng = new PCGDeterministicRng(seed, 2, 0);

            for (int i = 0; i < count; i++)
            {
                // A handful of tight piles on a wide grid: this is what makes the cell neighbourhood matter,
                // because a point's conflicts sit both inside and just outside its own cell.
                float centreX = (i % 7) * 40.0f - 120.0f;
                float centreZ = (i % 5) * 40.0f - 80.0f;

                points[i] = Point(
                    centreX + rng.NextRange(-6.0f, 6.0f),
                    rng.NextRange(-6.0f, 6.0f),
                    centreZ + rng.NextRange(-6.0f, 6.0f),
                    rng.NextFloat(),
                    i);
            }

            return points;
        }

        private static PCGPoint[] DuplicatePoints(int count, int seed)
        {
            var points = new PCGPoint[count];
            var rng = new PCGDeterministicRng(seed, 3, 0);

            for (int i = 0; i < count; i++)
            {
                // Same position, different densities: only the priority order can decide who survives, and the
                // grid must not let a later duplicate be judged against an earlier rejected one.
                points[i] = Point(7.5f, 0.0f, -3.25f, rng.NextFloat(), i);
            }

            return points;
        }

        private static PCGPoint[] FlatDensityPoints(int count, int seed)
        {
            var points = new PCGPoint[count];
            var rng = new PCGDeterministicRng(seed, 4, 0);

            for (int i = 0; i < count; i++)
            {
                // Every density equal, so the tie-break on index is the only order left - the case a sort that
                // is not stable would silently change.
                points[i] = Point(
                    rng.NextRange(-80.0f, 80.0f),
                    0.0f,
                    rng.NextRange(-80.0f, 80.0f),
                    0.5f,
                    i);
            }

            return points;
        }

        private static PCGPoint[] MixedDensityPoints(int count, int seed)
        {
            var points = new PCGPoint[count];
            var rng = new PCGDeterministicRng(seed, 5, 0);

            for (int i = 0; i < count; i++)
            {
                float density = i % 3 == 0 ? 1.0f : (i % 3 == 1 ? 0.5f : 0.0f);
                points[i] = Point(
                    rng.NextRange(-60.0f, 60.0f),
                    rng.NextRange(-5.0f, 5.0f),
                    rng.NextRange(-60.0f, 60.0f),
                    density,
                    i);
            }

            return points;
        }
    }
}

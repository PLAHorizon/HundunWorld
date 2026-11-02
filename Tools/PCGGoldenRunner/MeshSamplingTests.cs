// PCG Plugin - Executable tests for mesh surface sampling.
//
// The geometry is built by hand here - a quad of known area, a collapsed triangle, a scaled and translated
// frame - so every expectation below is an answer worked out in advance rather than "whatever the code did".
// This is the part of the mesh sampler that an asset-dependent check could never prove: that the selection is
// proportional to area, that degenerate triangles are skipped, and that the world transform is composed in.

using System;
using FlaxEngine;
using PCG;
using PCG.Data;

using CheckDelegate = PCGGoldenRunner.NodeBehaviourTests.CheckDelegate;

namespace PCGGoldenRunner
{
    internal static class MeshSamplingTests
    {
        /// <summary>A 2x2 quad in the XZ plane, split into two triangles of area 2 each.</summary>
        private static readonly Vector3[] Quad =
        {
            new Vector3(0, 0, 0),
            new Vector3(2, 0, 0),
            new Vector3(2, 0, 2),
            new Vector3(0, 0, 2),
        };

        private static readonly uint[] QuadTriangles = { 0, 1, 2, 0, 2, 3 };

        private static CheckDelegate _check;

        // Condition first, so an assertion reads as "this holds, because ...".
        private static void check(bool ok, string what, string detail = null) => _check(what, ok, detail);

        internal static void Run(CheckDelegate callback)
        {
            _check = callback;

            AreaTests();
            SelectionTests();
            PointGenerationTests();
        }

        // ---- areas ----------------------------------------------------------

        private static void AreaTests()
        {
            float right = PCGMeshSurfaceSampling.TriangleArea(
                new Vector3(0, 0, 0), new Vector3(3, 0, 0), new Vector3(0, 0, 4));
            check(Math.Abs(right - 6.0f) < 1e-3f, "a 3-4-5 right triangle has area 6", right.ToString("F4"));

            var collapsed = PCGMeshSurfaceSampling.TriangleArea(
                new Vector3(1, 1, 1), new Vector3(1, 1, 1), new Vector3(5, 0, 0));
            check(collapsed <= 0.0f, "a collapsed triangle has no area", collapsed.ToString("F6"));

            var cumulative = PCGMeshSurfaceSampling.BuildCumulativeAreas(Quad, QuadTriangles, out float total);
            check(cumulative != null && cumulative.Length == 2,
                "a quad describes two triangles", cumulative?.Length.ToString() ?? "null");
            check(Math.Abs(total - 4.0f) < 1e-3f, "the quad's total area is 4", total.ToString("F4"));
            check(cumulative != null && Math.Abs(cumulative[0] - 2.0f) < 1e-3f && Math.Abs(cumulative[1] - 4.0f) < 1e-3f,
                "cumulative areas reach each triangle's share in turn",
                cumulative == null ? "null" : $"{cumulative[0]},{cumulative[1]}");

            check(PCGMeshSurfaceSampling.BuildCumulativeAreas(null, QuadTriangles, out _) == null,
                "missing positions is refused rather than sampled");
            check(PCGMeshSurfaceSampling.BuildCumulativeAreas(Quad, null, out _) == null,
                "missing triangles is refused rather than sampled");

            var partial = PCGMeshSurfaceSampling.BuildCumulativeAreas(Quad, new uint[] { 0, 1 }, out _);
            check(partial == null, "an index list that is not a whole number of triangles is refused");

            var outOfRange = PCGMeshSurfaceSampling.BuildCumulativeAreas(Quad, new uint[] { 0, 1, 99 }, out _);
            check(outOfRange == null, "a triangle index past the vertex list is refused");

            // Only-degenerate geometry has nothing to sample, and must say so instead of dividing by zero.
            var allFlat = PCGMeshSurfaceSampling.BuildCumulativeAreas(
                Quad, new uint[] { 0, 0, 0, 1, 1, 2 }, out float flatTotal);
            check(allFlat == null && flatTotal <= 0.0f, "a mesh of collapsed triangles yields no sampleable area",
                flatTotal.ToString("F6"));
        }

        // ---- selection ------------------------------------------------------

        private static void SelectionTests()
        {
            var cumulative = PCGMeshSurfaceSampling.BuildCumulativeAreas(Quad, QuadTriangles, out float total);

            check(PCGMeshSurfaceSampling.PickTriangle(cumulative, total, 0.0f) == 0,
                "the first roll selects the first triangle");
            check(PCGMeshSurfaceSampling.PickTriangle(cumulative, total, 1.999f) == 0,
                "a roll just inside the first band stays in it");
            check(PCGMeshSurfaceSampling.PickTriangle(cumulative, total, 2.0f) == 1,
                "a roll at the boundary moves to the next triangle");
            check(PCGMeshSurfaceSampling.PickTriangle(cumulative, total, 3.999f) == 1,
                "the last band is reachable");
            check(PCGMeshSurfaceSampling.PickTriangle(null, 0.0f, 0.0f) < 0,
                "no bands means no selection");

            // A collapsed triangle repeats the previous cumulative value, so it owns no interval at all.
            var withDegenerate = PCGMeshSurfaceSampling.BuildCumulativeAreas(
                Quad, new uint[] { 0, 1, 2, 0, 0, 2 }, out float degenTotal);
            int landedOnDegenerate = 0;
            for (int i = 0; i < 5000; i++)
            {
                int picked = PCGMeshSurfaceSampling.PickTriangle(withDegenerate, degenTotal, (i / 5000.0f) * degenTotal);
                if (picked == 1)
                    landedOnDegenerate++;
            }
            check(landedOnDegenerate == 0, "a zero-area triangle is never selected",
                $"hits={landedOnDegenerate}");

            // Proportionality, the property area weighting exists for: an equal split of the quad must come out
            // roughly half and half, and would come out badly skewed if the roll ignored area.
            var rng = new PCGDeterministicRng(12345, 3, 0);
            int first = 0;
            const int Rolls = 20000;
            for (int i = 0; i < Rolls; i++)
            {
                if (PCGMeshSurfaceSampling.PickTriangle(cumulative, total, rng.NextFloat() * total) == 0)
                    first++;
            }

            float share = first / (float)Rolls;
            check(Math.Abs(share - 0.5f) < 0.02f, "equal-area triangles are chosen equally often",
                $"share={share:F4}");

            // One triangle three times the area of the other must take three times the picks.
            var skewed = new[]
            {
                new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 0, 1),   // area 0.5
                new Vector3(2, 0, 2), new Vector3(8, 0, 2), new Vector3(2, 0, 8),   // area 18
            };
            var skewedCumulative = PCGMeshSurfaceSampling.BuildCumulativeAreas(
                skewed, new uint[] { 0, 1, 2, 3, 4, 5 }, out float skewedTotal);
            float smallArea = PCGMeshSurfaceSampling.TriangleArea(skewed[0], skewed[1], skewed[2]);
            float bigArea = PCGMeshSurfaceSampling.TriangleArea(skewed[3], skewed[4], skewed[5]);
            check(Math.Abs(skewedTotal - (smallArea + bigArea)) < 1e-3f,
                "the total is the sum of the triangle areas", skewedTotal.ToString("F4"));

            int bigPicks = 0;
            var skewedRng = new PCGDeterministicRng(999, 1, 0);
            for (int i = 0; i < Rolls; i++)
            {
                if (PCGMeshSurfaceSampling.PickTriangle(skewedCumulative, skewedTotal, skewedRng.NextFloat() * skewedTotal) == 1)
                    bigPicks++;
            }

            float expectedShare = bigArea / skewedTotal;
            float actualShare = bigPicks / (float)Rolls;
            check(Math.Abs(actualShare - expectedShare) < 0.02f,
                "selection frequency follows the area share",
                $"expected={expectedShare:F3} actual={actualShare:F3}");
        }

        // ---- generated points ----------------------------------------------

        private static void PointGenerationTests()
        {
            // u = v = 0 must land exactly on the first corner, which is what pins the barycentric convention.
            PCGMeshSurfaceSampling.SampleTriangle(
                new Vector3(1, 2, 3), new Vector3(5, 0, 0), new Vector3(0, 0, 7), 0.0f, 0.0f,
                Vector3.Up, Vector3.Up, Vector3.Up, out var corner, out _);
            check(Math.Abs(corner.X - 1f) < 1e-4f && Math.Abs(corner.Y - 2f) < 1e-4f && Math.Abs(corner.Z - 3f) < 1e-4f,
                "zero coordinates land on the first corner", corner.ToString());

            // Points must lie inside their triangle: the three sub-triangles it forms with the corners add up
            // to the whole, and any point off the plane or outside the edges breaks that identity.
            var identity = new Transform(new Vector3(0, 0, 0), Quaternion.Identity, new Float3(1, 1, 1));
            var data = PCGMeshSurfaceSampling.Generate(
                Quad, Normals(Quad), QuadTriangles, identity, 2000, 7, 2, 1.0f, out string error);

            check(error == null && data.Count == 2000, "a quad can be sampled", error ?? $"count={data.Count}");

            int outside = 0;
            for (int i = 0; i < data.Count; i++)
            {
                var p = data.Points[i].Position;

                if (p.X < -1e-3f || p.X > 2.001f || p.Z < -1e-3f || p.Z > 2.001f)
                {
                    outside++;
                    continue;
                }

                if (Math.Abs(p.Y) > 1e-3f)
                    outside++;   // the quad is flat at Y = 0

                float totalArea = PCGMeshSurfaceSampling.TriangleArea(Quad[0], Quad[1], Quad[2]);
                float parts = PCGMeshSurfaceSampling.TriangleArea(p, Quad[1], Quad[2]) +
                              PCGMeshSurfaceSampling.TriangleArea(Quad[0], p, Quad[2]) +
                              PCGMeshSurfaceSampling.TriangleArea(Quad[0], Quad[1], p);

                bool inFirst = Math.Abs(parts - totalArea) < 1e-2f;
                float otherArea = PCGMeshSurfaceSampling.TriangleArea(Quad[0], Quad[2], Quad[3]);
                float otherParts = PCGMeshSurfaceSampling.TriangleArea(p, Quad[2], Quad[3]) +
                                   PCGMeshSurfaceSampling.TriangleArea(Quad[0], p, Quad[3]) +
                                   PCGMeshSurfaceSampling.TriangleArea(Quad[0], Quad[2], p);
                bool inSecond = Math.Abs(otherParts - otherArea) < 1e-2f;

                if (!inFirst && !inSecond)
                    outside++;
            }

            check(outside == 0, "every sampled point lies inside the quad's triangles", $"outside={outside}");

            // Same seed and node index must reproduce the layout point for point.
            var again = PCGMeshSurfaceSampling.Generate(
                Quad, Normals(Quad), QuadTriangles, identity, 500, 7, 2, 1.0f, out _);
            bool identical = again.Count == 500;
            for (int i = 0; identical && i < 500; i++)
            {
                var a = data.Points[i].Position;
                var b = again.Points[i].Position;
                identical = Math.Abs(a.X - b.X) < 1e-6f && Math.Abs(a.Y - b.Y) < 1e-6f && Math.Abs(a.Z - b.Z) < 1e-6f;
            }
            check(identical, "the same seed and node index reproduce the same layout");

            var otherNode = PCGMeshSurfaceSampling.Generate(
                Quad, Normals(Quad), QuadTriangles, identity, 1, 7, 3, 1.0f, out _);
            var thisNode = PCGMeshSurfaceSampling.Generate(
                Quad, Normals(Quad), QuadTriangles, identity, 1, 7, 4, 1.0f, out _);
            check(otherNode.Count == 1 && thisNode.Count == 1 &&
                  otherNode.Points[0].Position != thisNode.Points[0].Position,
                "two mesh samplers in one graph do not agree by accident");

            // The world frame is composed in: translation moves the whole set, scale sizes it.
            var placed = new Transform(new Vector3(10, 20, 30), Quaternion.Identity, new Float3(2, 2, 2));
            var inWorld = PCGMeshSurfaceSampling.Generate(
                Quad, Normals(Quad), QuadTriangles, placed, 800, 11, 5, 1.0f, out _);

            bool framed = inWorld.Count == 800;
            for (int i = 0; framed && i < inWorld.Count; i++)
            {
                var p = inWorld.Points[i].Position;
                framed = p.X >= 10.0f - 1e-3f && p.X <= 14.0f + 1e-3f &&
                         p.Z >= 30.0f - 1e-3f && p.Z <= 34.0f + 1e-3f &&
                         Math.Abs(p.Y - 20.0f) < 1e-2f;
            }
            check(framed, "the sampled points land inside the actor's transformed footprint");

            var scaled = PCGMeshSurfaceSampling.Generate(
                Quad, Normals(Quad), QuadTriangles, placed, 10, 11, 5, 0.4f, out _);
            check(scaled.Count == 10 && Math.Abs(scaled.Points[0].Scale.X - 2.0f) < 1e-4f &&
                  Math.Abs(scaled.Points[0].Density - 0.4f) < 1e-4f,
                "the actor's scale and the node's density reach the points",
                scaled.Count > 0 ? $"scale={scaled.Points[0].Scale.X} density={scaled.Points[0].Density}" : "none");

            // Without vertex normals the face normal has to carry the orientation instead.
            var faceOnly = PCGMeshSurfaceSampling.Generate(
                Quad, null, QuadTriangles, identity, 200, 7, 2, 1.0f, out _);
            int offUp = 0;
            for (int i = 0; i < faceOnly.Count; i++)
            {
                var up = Vector3.Transform(Vector3.Up, faceOnly.Points[i].Rotation);
                if (Math.Abs(Math.Abs(up.Y) - 1.0f) > 1e-2f)
                    offUp++;
            }
            check(faceOnly.Count == 200 && offUp == 0,
                "a flat mesh without vertex normals still points its points along the face",
                $"off={offUp}");

            check(PCGMeshSurfaceSampling.Generate(
                Quad, null, QuadTriangles, identity, 0, 1, 1, 1.0f, out string zeroError).Count == 0 &&
                zeroError != null,
                "a point count of zero is reported, not silently empty", zeroError);

            check(PCGMeshSurfaceSampling.Generate(
                Quad, null, new uint[] { 0, 0, 0 }, identity, 10, 1, 1, 1.0f, out string flatError).Count == 0 &&
                flatError != null,
                "degenerate-only geometry is reported, not silently empty", flatError);
        }

        private static Vector3[] Normals(Vector3[] positions)
        {
            var normals = new Vector3[positions.Length];
            for (int i = 0; i < positions.Length; i++)
                normals[i] = Vector3.Up;
            return normals;
        }
    }
}

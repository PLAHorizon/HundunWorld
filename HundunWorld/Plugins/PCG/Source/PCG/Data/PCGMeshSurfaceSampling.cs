// PCG Plugin - mesh surface sampling (pure geometry)
// Part of the FlaxEngine PCG plugin. See docs/PCG_PLUGIN_DESIGN.md §5.1.1.

using System;
using FlaxEngine;

namespace PCG.Data
{
    /// <summary>
    /// Triangular surface sampling, kept free of engine objects so it can be measured and asserted headless.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The node that uses this (<c>PCGMeshSamplerNode</c>) has to reach through a loaded asset to get its
    /// geometry, and an asset-dependent test can only ever say "it ran". Splitting the arithmetic out means the
    /// properties that actually matter - points land inside their triangle, triangles are chosen in proportion
    /// to their area, degenerate triangles are never chosen, and a re-run reproduces the layout - are checked
    /// against hand-built geometry with known answers.
    /// </para>
    /// </remarks>
    public static class PCGMeshSurfaceSampling
    {
        /// <summary>Below this area a triangle is treated as degenerate.</summary>
        /// <remarks>
        /// Exported meshes are full of zero-area triangles (collapsed seams, split UV shells). A weight of zero
        /// keeps them out of the selection instead of concentrating samples on a line.
        /// </remarks>
        public const float MinTriangleArea = 1e-8f;

        /// <summary>Area of the triangle through the origin-relative vectors.</summary>
        public static float TriangleArea(in Vector3 a, in Vector3 b, in Vector3 c)
        {
            var ab = b - a;
            var ac = c - a;
            return Cross(ab, ac).Length * 0.5f;
        }

        /// <summary>
        /// Cumulative area weights per triangle, in the order the triangles appear.
        /// </summary>
        /// <param name="positions">Vertex positions.</param>
        /// <param name="triangles">Triangle indices, three per triangle.</param>
        /// <param name="lodIndex">Reserved for parity with the node's LOD handling; unused.</param>
        /// <param name="totalArea">Sum of the usable triangle areas; zero when nothing is sampleable.</param>
        /// <returns>Cumulative areas, or null when the input cannot describe triangles.</returns>
        public static float[] BuildCumulativeAreas(Vector3[] positions, uint[] triangles, out float totalArea)
        {
            totalArea = 0.0f;

            if (positions == null || triangles == null || triangles.Length % 3 != 0)
                return null;

            int count = triangles.Length / 3;
            if (count == 0)
                return null;

            var cumulative = new float[count];

            for (int t = 0; t < count; t++)
            {
                uint ia = triangles[t * 3];
                uint ib = triangles[t * 3 + 1];
                uint ic = triangles[t * 3 + 2];

                if (ia >= positions.Length || ib >= positions.Length || ic >= positions.Length)
                    return null;

                float area = TriangleArea(positions[ia], positions[ib], positions[ic]);
                if (area < MinTriangleArea)
                    area = 0.0f;

                totalArea += area;
                cumulative[t] = totalArea;
            }

            if (totalArea <= 0.0f)
                return null;

            return cumulative;
        }

        /// <summary>
        /// Index of the triangle whose cumulative area band contains <paramref name="roll"/>.
        /// </summary>
        /// <param name="cumulative">From <see cref="BuildCumulativeAreas"/>.</param>
        /// <param name="totalArea">Its total.</param>
        /// <param name="roll">A value in [0, totalArea).</param>
        /// <remarks>
        /// Degenerate triangles carry no band of their own (their cumulative entry repeats the previous one),
        /// so a binary search that returns the first band reaching the roll cannot land on one - which is why
        /// the search looks for the strictly-greater bound rather than greater-or-equal.
        /// </remarks>
        public static int PickTriangle(float[] cumulative, float totalArea, float roll)
        {
            if (cumulative == null || cumulative.Length == 0)
                return -1;

            float target = Math.Max(0.0f, Math.Min(roll, totalArea));

            int low = 0;
            int high = cumulative.Length - 1;

            while (low < high)
            {
                int mid = low + (high - low) / 2;
                if (cumulative[mid] > target)
                    high = mid;
                else
                    low = mid + 1;
            }

            return low;
        }

        /// <summary>
        /// A point inside the triangle at barycentric coordinates (1-u-v, u, v).
        /// </summary>
        /// <remarks>
        /// The square-root on one coordinate is what makes the mapping uniform over the triangle: picking u and
        /// v as independent uniforms concentrates samples toward the vertex where the two edges meet.
        /// </remarks>
        /// <param name="a">First corner.</param>
        /// <param name="b">Second corner.</param>
        /// <param name="c">Third corner.</param>
        /// <param name="u">Uniform in [0,1).</param>
        /// <param name="v">Uniform in [0,1).</param>
        /// <param name="normalU">First corner normal.</param>
        /// <param name="normalV">Second corner normal.</param>
        /// <param name="normalW">Third corner normal.</param>
        /// <param name="position">Sampled position.</param>
        /// <param name="normal">Interpolated normal, normalised when it has a direction of its own.</param>
        public static void SampleTriangle(
            in Vector3 a, in Vector3 b, in Vector3 c, float u, float v,
            in Vector3 normalU, in Vector3 normalV, in Vector3 normalW,
            out Vector3 position, out Vector3 normal)
        {
            float su = Mathf.Sqrt(Mathf.Max(0.0f, u));
            float w = 1.0f - su;
            float tv = v * su;

            position = a * w + b * tv + c * (su - tv);

            normal = normalU * w + normalV * tv + normalW * (su - tv);
            float length = normal.Length;
            if (length > 1e-6f)
                normal = normal / length;
        }

        /// <summary>
        /// Builds a point set spread over the given mesh surface, in world space.
        /// </summary>
        /// <param name="positions">Vertex positions in the mesh's own space.</param>
        /// <param name="normals">Per-vertex normals, or null to use each triangle's face normal.</param>
        /// <param name="triangles">Triangle indices, three per triangle.</param>
        /// <param name="toWorld">Transform taking a mesh-space point into the scene.</param>
        /// <param name="count">Points to produce.</param>
        /// <param name="seed">Run seed.</param>
        /// <param name="nodeId">Node index, so two mesh samplers in one graph disagree.</param>
        /// <param name="density">Density written to every point.</param>
        /// <param name="error">Why nothing could be produced, when the result is empty.</param>
        public static PCGData Generate(
            Vector3[] positions, Vector3[] normals, uint[] triangles, Transform toWorld,
            int count, int seed, int nodeId, float density, out string error)
        {
            error = null;

            if (count <= 0)
            {
                error = "A mesh sampler with a point count of zero produces nothing.";
                return PCGData.Empty;
            }

            var cumulative = BuildCumulativeAreas(positions, triangles, out float totalArea);
            if (cumulative == null)
            {
                error = "The mesh has no triangles with a usable area.";
                return PCGData.Empty;
            }

            var points = new PCGPoint[count];

            // Every element depends on its index alone - the random stream is keyed on (seed, node id, index),
            // and each worker writes only its own slot - which is what lets the same loop run either inline or
            // across workers and still produce the same points in the same order.
            Execution.PCGParallel.For(count, i =>
            {
                // One stream per point index, keyed the same way every other node keys its randomness, so a
                // re-run reproduces the layout and a subset recompute agrees with the full one.
                var rng = new PCGDeterministicRng(seed, nodeId, i);
                float roll = rng.NextFloat() * totalArea;

                // A cumulative table that BuildCumulativeAreas accepted always contains the roll, so there is
                // no per-point failure left to report - and reporting one from here would have been a race on
                // the shared error slot. Clamped rather than trusted, so an unreachable path cannot corrupt
                // the point set.
                int triangle = Math.Max(0, Math.Min(PickTriangle(cumulative, totalArea, roll), cumulative.Length - 1));

                int ia = (int)triangles[triangle * 3];
                int ib = (int)triangles[triangle * 3 + 1];
                int ic = (int)triangles[triangle * 3 + 2];

                var na = normals != null ? normals[ia] : Vector3.Zero;
                var nb = normals != null ? normals[ib] : Vector3.Zero;
                var nc = normals != null ? normals[ic] : Vector3.Zero;

                SampleTriangle(
                    positions[ia], positions[ib], positions[ic], rng.NextFloat(), rng.NextFloat(),
                    na, nb, nc, out var local, out var localNormal);

                var orientation = toWorld.Orientation;

                // Normals are directions, so the position's translation must not leak into them.
                var worldNormal = Vector3.Transform(localNormal, orientation);

                if (normals == null || localNormal.Length <= 1e-6f)
                    worldNormal = FaceNormal(positions[ia], positions[ib], positions[ic], orientation);

                var scaled = new Vector3(
                    local.X * toWorld.Scale.X,
                    local.Y * toWorld.Scale.Y,
                    local.Z * toWorld.Scale.Z);

                var world = Vector3.Transform(scaled, orientation) + toWorld.Translation;

                // The point's frame carries the surface direction, so a downstream spawner or align-to-normal
                // consumer reads the mesh's own shading rather than the world up axis. Same construction the
                // landscape sampler uses, so both samplers hand the next node the same convention.
                var up = worldNormal;
                var forward = Vector3.Normalize(Cross(up, Vector3.Right));
                if (forward.LengthSquared < 1e-6f)
                    forward = Vector3.Normalize(Cross(up, Vector3.Forward));

                var point = PCGPoint.Create(world, density, i);
                point.Rotation = Quaternion.LookRotation(
                    new Float3(forward.X, forward.Y, forward.Z), new Float3(up.X, up.Y, up.Z));

                // Averaged scale: a mesh sampler has no per-point scale of its own, and the actor's scale is the
                // only one worth passing on.
                point.Scale = new Float3(
                    (toWorld.Scale.X + toWorld.Scale.Y + toWorld.Scale.Z) / 3.0f);

                points[i] = point;
            });

            return new PCGData(points);
        }

        /// <summary>Face normal of a triangle, turned into world directions.</summary>
        private static Vector3 FaceNormal(in Vector3 a, in Vector3 b, in Vector3 c, Quaternion orientation)
        {
            var n = Cross(b - a, c - a);
            float length = n.Length;
            if (length <= 1e-8f)
                n = Vector3.Up;
            else
                n = n / length;

            return Vector3.Transform(n, orientation);
        }

        private static Vector3 Cross(in Vector3 a, in Vector3 b) =>
            new Vector3(
                a.Y * b.Z - a.Z * b.Y,
                a.Z * b.X - a.X * b.Z,
                a.X * b.Y - a.Y * b.X);
    }
}

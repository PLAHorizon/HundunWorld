// PCG Plugin - Executable tests for the surface alignment helper.
//
// A rotation built from a normal is easy to get subtly wrong - a flipped cross product, a NaN on a vertical face
// - and impossible to eyeball. It is pure arithmetic, so it is pinned here instead of watched in a viewport.

using System;
using FlaxEngine;
using PCG;

using CheckDelegate = PCGGoldenRunner.NodeBehaviourTests.CheckDelegate;

namespace PCGGoldenRunner
{
    internal static class SurfaceAlignTests
    {
        private static CheckDelegate _check;

        // Condition first, so an assertion reads as "this holds, because ...".
        private static void check(bool ok, string what, string detail = null) => _check(what, ok, detail);

        internal static void Run(CheckDelegate callback)
        {
            _check = callback;

            var up = new Vector3(0.0f, 1.0f, 0.0f);

            check(AlignsUp(PCGSurfaceAlign.RotationFromNormal(up), up),
                "a flat surface leaves the point upright");

            var wall = new Vector3(1.0f, 0.0f, 0.0f);
            check(AlignsUp(PCGSurfaceAlign.RotationFromNormal(wall), wall),
                "a vertical face tilts the point's up onto it");

            var slope = Vector3.Normalize(new Vector3(1.0f, 1.0f, 0.0f));
            check(AlignsUp(PCGSurfaceAlign.RotationFromNormal(slope), slope),
                "a 45-degree slope points the up axis along the normal");

            // The case a naive cross product turns into NaN: a surface facing straight down.
            var ceiling = new Vector3(0.0f, -1.0f, 0.0f);
            var down = PCGSurfaceAlign.RotationFromNormal(ceiling);
            check(AlignsUp(down, ceiling) && Finite(down),
                "a downward-facing surface is handled rather than producing a NaN rotation", down.ToString());

            var zero = PCGSurfaceAlign.RotationFromNormal(Vector3.Zero);
            check(Finite(zero) && AlignsUp(zero, up),
                "a zero-length normal falls back to upright instead of being normalised", zero.ToString());

            check(UnitLength(PCGSurfaceAlign.RotationFromNormal(slope)),
                "the rotation is a unit quaternion, so it cannot scale what it is applied to");

            // What the yaw actually does on flat ground. Both axes are asserted rather than described, because the
            // construction's forward axis is the part a sign error would flip silently - and it did: the sampler
            // used to yaw every aligned point 180 degrees here.
            var flat = PCGSurfaceAlign.RotationFromNormal(up);
            var forward = Vector3.Transform(Vector3.Forward, flat);
            check(Close(forward, Vector3.Forward),
                "an upright point's forward stays along world +Z", forward.ToString());
            check(CloseQ(flat, Quaternion.Identity),
                "flat ground is the identity rotation, so alignment changes nothing where there is nothing to align",
                flat.ToString());

            // A positive twin for the check above: it has to be able to fail, or "aligns up" would be vacuous.
            var yawed = Quaternion.RotationAxis(new Float3(0.0f, 1.0f, 0.0f), MathF.PI * 0.5f);
            check(!AlignsUp(yawed, wall), "the alignment check rejects a rotation whose up is wrong");
        }

        private static bool AlignsUp(Quaternion rotation, Vector3 normal)
        {
            var up = Vector3.Transform(Vector3.Up, rotation);
            var target = normal.LengthSquared > 1e-12f ? Vector3.Normalize(normal) : Vector3.Up;

            return (up - target).LengthSquared < 1e-4f;
        }

        private static bool UnitLength(Quaternion q) => MathF.Abs(q.Length - 1.0f) < 1e-4f;

        private static bool Finite(Quaternion q) =>
            !float.IsNaN(q.X) && !float.IsInfinity(q.X) &&
            !float.IsNaN(q.Y) && !float.IsInfinity(q.Y) &&
            !float.IsNaN(q.Z) && !float.IsInfinity(q.Z) &&
            !float.IsNaN(q.W) && !float.IsInfinity(q.W);

        private static bool Close(Vector3 a, Vector3 b) => (a - b).LengthSquared < 1e-6f;

        private static bool CloseQ(Quaternion a, Quaternion b) =>
            MathF.Abs(a.X - b.X) < 1e-4f && MathF.Abs(a.Y - b.Y) < 1e-4f &&
            MathF.Abs(a.Z - b.Z) < 1e-4f && MathF.Abs(a.W - b.W) < 1e-4f;
    }
}

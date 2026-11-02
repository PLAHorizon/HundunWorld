// PCG Plugin - Surface alignment (pure)
// Part of the FlaxEngine PCG plugin. See docs/PCG_PLUGIN_DESIGN.md §5.1.24.

using System;
using FlaxEngine;

namespace PCG
{
    /// <summary>
    /// Turns a surface normal into the rotation a point standing on that surface should carry.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Lifted out of the landscape sampler so that every node that puts something "on the ground" agrees about
    /// which way is up: a scatter dropped on a slope and a scatter projected onto the same slope must end up
    /// rotated identically, or props visibly disagree at the boundary between two halves of one graph. It is
    /// also pure, which is what lets the degenerate cases be asserted instead of eyeballed.
    /// </para>
    /// <para>
    /// The frame is built with <c>LookRotation(forward, up)</c>, so the point's local +Y follows the normal and
    /// its local +Z stays as close to world +Z as the surface allows - on flat ground that makes the whole
    /// rotation the identity, which is asserted. When the normal is parallel to world +X the forward choice
    /// collapses, so it is rebuilt from world +Z; that is the same fallback, for the same reason, as the
    /// spline-mesh layout's vertical-tangent case.
    /// </para>
    /// <para>
    /// <b>Measured, because it was wrong.</b> The forward axis used to be built as <c>cross(up, Right)</c>, which
    /// is world <i>−Z</i> on flat ground - so every aligned point came out yawed 180 degrees while the comment
    /// claimed the opposite, and nothing asserted either way. It is now <c>cross(Right, up)</c>, and the identity
    /// on flat ground is pinned by a test rather than described in a comment.
    /// </para>
    /// </remarks>
    public static class PCGSurfaceAlign
    {
        /// <summary>
        /// Rotation whose local up is <paramref name="normal"/>. A zero-length normal falls back to world up.
        /// </summary>
        public static Quaternion RotationFromNormal(in Vector3 normal)
        {
            // Floor the length before normalising: a near-zero normal would be blown up into noise, and a normal
            // of exactly zero would produce NaN and poison the point's rotation for every downstream node.
            var up = normal.LengthSquared > 1e-12f ? Vector3.Normalize(normal) : Vector3.Up;

            // Right x up, not up x Right: the latter is world -Z on flat ground, which yawed every aligned point
            // by 180 degrees. This order gives +Z, so a flat surface is the identity rotation.
            var forward = Vector3.Cross(Vector3.Right, up);

            if (forward.LengthSquared < 1e-6f)
                forward = Vector3.Cross(Vector3.Forward, up);

            forward = Vector3.Normalize(forward);

            return Quaternion.LookRotation(
                new Float3(forward.X, forward.Y, forward.Z),
                new Float3(up.X, up.Y, up.Z));
        }
    }
}

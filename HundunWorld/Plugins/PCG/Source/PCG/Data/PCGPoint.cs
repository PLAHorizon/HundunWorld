// PCG Plugin - Point data model
// Part of the FlaxEngine PCG plugin. See docs/PCG_PLUGIN_DESIGN.md.

using System;
using FlaxEngine;

namespace PCG
{
    /// <summary>
    /// A single procedural content generation point.
    /// </summary>
    /// <remarks>
    /// Layout follows docs/PCG_PLUGIN_DESIGN.md §3.1:
    /// <list type="bullet">
    /// <item><see cref="Position"/> is <see cref="Vector3"/> (float32) because <c>Float3</c> is a
    /// half-precision type and loses precision badly at world-space coordinates.</item>
    /// <item><see cref="Scale"/> is <see cref="Float3"/> to match <c>Transform.Scale</c> / <c>Actor.Scale</c>
    /// exactly, avoiding a conversion per spawned instance.</item>
    /// <item><see cref="Seed"/> is <see cref="int"/> because Flax's <c>RandomStream</c> only accepts int.</item>
    /// <item><see cref="Attributes"/> is a reference type shared across points; every write path must go
    /// through <see cref="PCGAttributeSet.EnsureOwned"/>. See the copy-on-write contract in §3.2.</item>
    /// </list>
    /// </remarks>
    [Serializable]
    public struct PCGPoint
    {
        /// <summary>World-space position.</summary>
        public Vector3 Position;

        /// <summary>Rotation.</summary>
        public Quaternion Rotation;

        /// <summary>Scale. Matches the engine's native <c>Float3</c> scale type.</summary>
        public Float3 Scale;

        /// <summary>Density in [0,1]. Drives filtering and spawn probability.</summary>
        public float Density;

        /// <summary>Per-point random seed. Required for deterministic incremental re-execution.</summary>
        public int Seed;

        /// <summary>Axis-aligned world-space bounds, used for pruning and overlap tests.</summary>
        /// <remarks>
        /// Flax's <see cref="BoundingBox"/> is axis-aligned, so pruning cannot account for point rotation.
        /// Use <c>OrientedBoundingBox</c> if that is ever required; it is intentionally not the default here.
        /// </remarks>
        public BoundingBox Bounds;

        /// <summary>Shared per-point attribute storage. May be null or shared between points.</summary>
        public PCGAttributeSet Attributes;

        /// <summary>Gets the transform represented by this point.</summary>
        public Transform GetTransform()
        {
            return new Transform(Position, Rotation, Scale);
        }

        /// <summary>Creates a point with identity rotation and unit scale.</summary>
        public static PCGPoint Create(Vector3 position, float density = 1.0f, int seed = 0)
        {
            return new PCGPoint
            {
                Position = position,
                Rotation = Quaternion.Identity,
                Scale = Float3.One,
                Density = Mathf.Saturate(density),
                Seed = seed,
                Bounds = new BoundingBox(position, position),
                Attributes = null,
            };
        }

        /// <summary>Creates a point with an explicit transform.</summary>
        public static PCGPoint Create(in Transform transform, float density = 1.0f, int seed = 0)
        {
            return new PCGPoint
            {
                Position = transform.Translation,
                Rotation = transform.Orientation,
                Scale = transform.Scale,
                Density = Mathf.Saturate(density),
                Seed = seed,
                Bounds = new BoundingBox(transform.Translation, transform.Translation),
                Attributes = null,
            };
        }

        /// <summary>
        /// Recomputes <see cref="Bounds"/> from a local-space extents box (half-size around the origin).
        /// Call this after any change to Position/Rotation/Scale, or after a sampler assigns an asset size.
        /// </summary>
        /// <param name="localExtents">Half-size of the point's content in local space. Zero yields a degenerate box.</param>
        public void UpdateBounds(Vector3 localExtents)
        {
            var local = new BoundingBox(-localExtents, localExtents);
            Bounds = BoundingBox.Transform(local, GetTransform());
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"PCGPoint(Pos={Position}, Density={Density:F3}, Seed={Seed})";
        }
    }
}

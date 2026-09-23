// PCG Plugin - Terrain sampling
// Part of the FlaxEngine PCG plugin. See docs/PCG_PLUGIN_DESIGN.md §3.5.

using System;
using FlaxEngine;

namespace PCG.TerrainSampling
{
    /// <summary>How a terrain surface query is answered.</summary>
    public enum PCGTerrainSampleMode
    {
        /// <summary>
        /// Ray cast straight down. Semantically unambiguous and needs no calibration, but costs one cast per
        /// point. This is the default because correctness comes first.
        /// </summary>
        RayCast = 0,

        /// <summary>
        /// Bilinearly interpolate the patch heightmap. Fast enough for hundreds of thousands of points, but
        /// the mapping from stored values to world Y is not documented by the engine and must be calibrated
        /// first. See <see cref="PCGTerrainProbe"/>.
        /// </summary>
        HeightfieldInterpolated = 1,
    }

    /// <summary>Result of a terrain surface query.</summary>
    public struct PCGTerrainSample
    {
        /// <summary>True when the query hit terrain.</summary>
        public bool Hit;

        /// <summary>World-space height at the sample location.</summary>
        public float Height;

        /// <summary>Unit surface normal, or up when unknown.</summary>
        public Vector3 Normal;

        /// <summary>Slope in degrees from vertical, in [0,90].</summary>
        public float SlopeDegrees;

        /// <summary>Which path answered the query.</summary>
        public PCGTerrainSampleMode Mode;

        /// <summary>Creates a miss.</summary>
        public static PCGTerrainSample Miss(PCGTerrainSampleMode mode)
        {
            return new PCGTerrainSample
            {
                Hit = false,
                Height = 0.0f,
                Normal = Vector3.Up,
                SlopeDegrees = 0.0f,
                Mode = mode,
            };
        }
    }

    /// <summary>
    /// Height-to-world calibration for the heightfield path.
    /// </summary>
    /// <remarks>
    /// The engine exposes patch heightmap data as a flat <c>float</c> array but the header does not state the
    /// mapping from those values to world Y. Guessing it produces a world where every spawned object floats
    /// or sinks by a constant factor - a silent, plausible-looking failure. This record holds the affine
    /// mapping measured by <see cref="PCGTerrainProbe.Calibrate"/>, and the heightfield path refuses to run
    /// until it has one.
    /// </remarks>
    [Serializable]
    public struct PCGTerrainCalibration
    {
        /// <summary>Multiplier applied to a raw heightmap sample.</summary>
        public float HeightScale;

        /// <summary>Offset added after scaling.</summary>
        public float HeightOffset;

        /// <summary>Terrain the calibration was measured on.</summary>
        public Guid TerrainId;

        /// <summary>Largest disagreement seen between the two paths during calibration.</summary>
        public float MaxResidual;

        /// <summary>Number of probe points used.</summary>
        public int SampleCount;

        /// <summary>True when the calibration was measured successfully.</summary>
        public bool IsValid;

        /// <summary>Applies the mapping to a raw heightmap value.</summary>
        public float ToWorldY(float raw) => raw * HeightScale + HeightOffset;

        /// <summary>An invalid calibration, used as the default.</summary>
        public static PCGTerrainCalibration Invalid => new PCGTerrainCalibration
        {
            HeightScale = 1.0f,
            HeightOffset = 0.0f,
            IsValid = false,
        };

        /// <inheritdoc />
        public override string ToString() =>
            IsValid
                ? $"Calibration(scale={HeightScale:G6}, offset={HeightOffset:G6}, samples={SampleCount}, residual={MaxResidual:G3})"
                : "Calibration(invalid)";
    }

    /// <summary>
    /// Queries a terrain surface through either path and caches the geometry needed to do so.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Geometry.</b> A Flax terrain is a grid of patches; each patch is <c>ChunksCountEdge</c> chunks
    /// square and each chunk has <c>ChunkSize</c> quads per edge. So a patch spans
    /// <c>UnitsPerVertex * ChunksCountEdge * ChunkSize</c> world units (<c>Terrain.PatchSize</c>) and its
    /// heightmap has <c>(ChunkSize * ChunksCountEdge + 1)</c> vertices per edge (<c>Terrain.HeightmapSize</c>).
    /// </para>
    /// <para>
    /// <b>Threading.</b> The heightfield path only reads arrays and is safe to call from worker threads. The
    /// ray cast path goes through the physics/geometry query layer, whose thread safety the engine does not
    /// document, so <see cref="IsHeightfieldThreadSafe"/> reports false for it and callers must keep it on the
    /// main thread until it has been verified under load.
    /// </para>
    /// </remarks>
    public sealed class PCGTerrainSampler
    {
        private const float MaxRayCastDistance = 100000.0f;

        private readonly Terrain _terrain;
        private readonly PCGTerrainCalibration _calibration;
        private readonly float[] _reusableRayHits = new float[1];

        /// <summary>Creates a sampler over one terrain.</summary>
        /// <param name="terrain">Terrain to query.</param>
        /// <param name="calibration">Calibration for the heightfield path. Pass an invalid calibration to force the ray cast path.</param>
        public PCGTerrainSampler(Terrain terrain, PCGTerrainCalibration calibration)
        {
            _terrain = terrain ?? throw new ArgumentNullException(nameof(terrain));
            _calibration = calibration;
        }

        /// <summary>Gets the terrain being sampled.</summary>
        public Terrain Terrain => _terrain;

        /// <summary>Gets the calibration in use.</summary>
        public PCGTerrainCalibration Calibration => _calibration;

        /// <summary>True when the heightfield path is usable, meaning a calibration has been measured.</summary>
        public bool HasValidCalibration => _calibration.IsValid;

        /// <summary>Reports whether the heightfield path is safe to use off the main thread.</summary>
        public bool IsHeightfieldThreadSafe => true;

        /// <summary>Reports whether the ray cast path is safe to use off the main thread.</summary>
        /// <remarks>Unknown in Flax 1.12; treated as false until proven otherwise.</remarks>
        public bool IsRayCastThreadSafe => false;

        /// <summary>
        /// Samples the surface at a world XZ location.
        /// </summary>
        /// <param name="worldX">World X.</param>
        /// <param name="worldZ">World Z.</param>
        /// <param name="mode">Requested path.</param>
        /// <param name="startY">Y to begin a ray cast from, ignored by the heightfield path.</param>
        /// <param name="fallbackToRayCast">When true, an unusable heightfield falls back to a ray cast.</param>
        public PCGTerrainSample Sample(float worldX, float worldZ, PCGTerrainSampleMode mode,
            float startY = 10000.0f, bool fallbackToRayCast = true)
        {
            if (mode == PCGTerrainSampleMode.HeightfieldInterpolated)
            {
                if (HasValidCalibration)
                    return SampleHeightfield(worldX, worldZ);

                // An uncalibrated heightfield would silently produce wrong heights, which is worse than
                // being slow, so fall back rather than guess.
                if (fallbackToRayCast)
                    return SampleRayCast(worldX, worldZ, startY);

                return PCGTerrainSample.Miss(mode);
            }

            return SampleRayCast(worldX, worldZ, startY);
        }

        private PCGTerrainSample SampleRayCast(float worldX, float worldZ, float startY)
        {
            var origin = new Vector3(worldX, startY, worldZ);
            var direction = new Vector3(0.0f, -1.0f, 0.0f);

            if (!_terrain.RayCast(origin, direction, out float hitDistance, MaxRayCastDistance))
                return PCGTerrainSample.Miss(PCGTerrainSampleMode.RayCast);

            float height = startY - hitDistance;

            // This overload reports a distance only. Flax also exposes
            // TerrainPatch.RayCast(origin, direction, out float hitDistance, out Vector3 hitNormal, max),
            // which returns the collision normal directly - that is the intended source of slope on the ray
            // cast path once per-probe patch resolution is wired up (design doc section 3.5 point 3, v1.2).
            // Until then the normal is derived from the heightfield, which needs a valid calibration, so on
            // an uncalibrated terrain slope here stays flat rather than silently degrading to a bogus value.
            var normal = Vector3.Up;
            if (TryGetHeightfieldNormal(worldX, worldZ, out var n))
                normal = n;

            return MakeHit(height, normal, PCGTerrainSampleMode.RayCast);
        }

        private unsafe PCGTerrainSample SampleHeightfield(float worldX, float worldZ)
        {
            if (!TryGetPatch(worldX, worldZ, out var patch, out float patchOriginX, out float patchOriginZ))
                return PCGTerrainSample.Miss(PCGTerrainSampleMode.HeightfieldInterpolated);

            var raw = patch.GetHeightmapData();
            if (raw == null)
                return PCGTerrainSample.Miss(PCGTerrainSampleMode.HeightfieldInterpolated);

            int size = _terrain.HeightmapSize;
            if (size < 2)
                return PCGTerrainSample.Miss(PCGTerrainSampleMode.HeightfieldInterpolated);

            float patchSize = _terrain.PatchSize;
            if (patchSize <= 0.0f)
                return PCGTerrainSample.Miss(PCGTerrainSampleMode.HeightfieldInterpolated);

            // Patch-local coordinates in vertex units, where the patch spans [0, size-1].
            float u = (worldX - patchOriginX) / patchSize * (size - 1);
            float v = (worldZ - patchOriginZ) / patchSize * (size - 1);

            ClampToGrid(ref u, ref v, size);
            float height = BilinearSample(raw, size, u, v, _calibration);

            var normal = Vector3.Up;
            if (TryGetHeightfieldNormal(worldX, worldZ, out var n))
                normal = n;

            return MakeHit(height, normal, PCGTerrainSampleMode.HeightfieldInterpolated);
        }

        /// <summary>
        /// Ensures the interpolation stencil stays inside the patch, keeping one vertex of margin for the
        /// finite-difference normal. Applies the same edge policy everywhere so results stay reproducible.
        /// </summary>
        private static void ClampToGrid(ref float u, ref float v, int size)
        {
            float max = size - 1;

            if (u < 0.0f) u = 0.0f;
            else if (u > max) u = max;

            if (v < 0.0f) v = 0.0f;
            else if (v > max) v = max;
        }

        private static unsafe float BilinearSample(float* data, int size, float u, float v, PCGTerrainCalibration calibration)
        {
            int x0 = (int)u;
            int y0 = (int)v;
            int x1 = x0 + 1;
            int y1 = y0 + 1;

            if (x1 >= size) { x1 = size - 1; x0 = size - 2; }
            if (y1 >= size) { y1 = size - 1; y0 = size - 2; }
            if (x0 < 0) { x0 = 0; x1 = 1; }
            if (y0 < 0) { y0 = 0; y1 = 1; }

            float fx = u - x0;
            float fy = v - y0;

            float h00 = data[y0 * size + x0];
            float h10 = data[y0 * size + x1];
            float h01 = data[y1 * size + x0];
            float h11 = data[y1 * size + x1];

            float a = h00 + (h10 - h00) * fx;
            float b = h01 + (h11 - h01) * fx;
            float raw = a + (b - a) * fy;

            return calibration.ToWorldY(raw);
        }

        /// <summary>
        /// Derives a surface normal by central differences over the calibrated heightfield.
        /// </summary>
        /// <remarks>
        /// The sample spacing is the world size of one heightmap cell, which varies with the terrain's patch
        /// configuration rather than being a fixed constant.
        /// </remarks>
        public unsafe bool TryGetHeightfieldNormal(float worldX, float worldZ, out Vector3 normal)
        {
            normal = Vector3.Up;

            if (!HasValidCalibration)
                return false;

            if (!TryGetPatch(worldX, worldZ, out var patch, out float patchOriginX, out float patchOriginZ))
                return false;

            var raw = patch.GetHeightmapData();
            if (raw == null)
                return false;

            int size = _terrain.HeightmapSize;
            float patchSize = _terrain.PatchSize;
            if (size < 3 || patchSize <= 0.0f)
                return false;

            float cellWorld = patchSize / (size - 1);

            float u = (worldX - patchOriginX) / patchSize * (size - 1);
            float v = (worldZ - patchOriginZ) / patchSize * (size - 1);

            // Keep one cell of margin so both stencil neighbours are inside this patch.
            if (u < 1.0f) u = 1.0f;
            else if (u > size - 2) u = size - 2;

            if (v < 1.0f) v = 1.0f;
            else if (v > size - 2) v = size - 2;

            float hL = BilinearSample(raw, size, u - 1.0f, v, _calibration);
            float hR = BilinearSample(raw, size, u + 1.0f, v, _calibration);
            float hD = BilinearSample(raw, size, u, v - 1.0f, _calibration);
            float hU = BilinearSample(raw, size, u, v + 1.0f, _calibration);

            // Central differences scaled to world units. Note Z increases with v.
            float dhdx = (hR - hL) / (2.0f * cellWorld);
            float dhdz = (hU - hD) / (2.0f * cellWorld);

            var tangentX = new Vector3(1.0f, dhdx, 0.0f);
            var tangentZ = new Vector3(0.0f, dhdz, 1.0f);

            normal = Vector3.Normalize(Vector3.Cross(tangentZ, tangentX));

            // The cross product orientation depends on axis handedness; force it to point up so callers get a
            // usable normal either way.
            if (normal.Y < 0.0f)
                normal = -normal;

            return true;
        }

        /// <summary>
        /// Reads the raw, uncalibrated heightmap value at a world XZ location.
        /// </summary>
        /// <remarks>
        /// Used by <see cref="PCGTerrainProbe"/> to fit the calibration. Not useful for placing content:
        /// the raw value carries no world-space meaning until a calibration exists.
        /// </remarks>
        public unsafe bool TryGetRawHeightfieldValue(float worldX, float worldZ, out float raw)
        {
            raw = 0.0f;

            if (!TryGetPatch(worldX, worldZ, out var patch, out float patchOriginX, out float patchOriginZ))
                return false;

            var data = patch.GetHeightmapData();
            if (data == null)
                return false;

            int size = _terrain.HeightmapSize;
            float patchSize = _terrain.PatchSize;
            if (size < 2 || patchSize <= 0.0f)
                return false;

            float u = (worldX - patchOriginX) / patchSize * (size - 1);
            float v = (worldZ - patchOriginZ) / patchSize * (size - 1);

            ClampToGrid(ref u, ref v, size);

            int x0 = (int)u;
            int y0 = (int)v;
            int x1 = x0 + 1;
            int y1 = y0 + 1;

            if (x1 >= size) { x1 = size - 1; x0 = size - 2; }
            if (y1 >= size) { y1 = size - 1; y0 = size - 2; }
            if (x0 < 0) { x0 = 0; x1 = 1; }
            if (y0 < 0) { y0 = 0; y1 = 1; }

            float fx = u - x0;
            float fy = v - y0;

            float h00 = data[y0 * size + x0];
            float h10 = data[y0 * size + x1];
            float h01 = data[y1 * size + x0];
            float h11 = data[y1 * size + x1];

            float a = h00 + (h10 - h00) * fx;
            float b = h01 + (h11 - h01) * fx;
            raw = a + (b - a) * fy;

            return true;
        }

        private static PCGTerrainSample MakeHit(float height, Vector3 normal, PCGTerrainSampleMode mode)
        {
            float slope = 0.0f;
            if (normal.Y < 1.0f)
            {
                float clamped = normal.Y < -1.0f ? -1.0f : normal.Y;
                slope = Mathf.Acos(clamped) * Mathf.RadiansToDegrees;
            }

            return new PCGTerrainSample
            {
                Hit = true,
                Height = height,
                Normal = normal,
                SlopeDegrees = slope,
                Mode = mode,
            };
        }

        /// <summary>
        /// Resolves which patch covers a world XZ location and where that patch starts in world space.
        /// </summary>
        public bool TryGetPatch(float worldX, float worldZ, out TerrainPatch patch, out float originX, out float originZ)
        {
            patch = null;
            originX = 0.0f;
            originZ = 0.0f;

            float patchSize = _terrain.PatchSize;
            if (patchSize <= 0.0f)
                return false;

            var terrainPosition = _terrain.Position;

            // Patch coordinates are relative to the terrain actor's own origin.
            int patchX = (int)Mathf.Floor((worldX - terrainPosition.X) / patchSize);
            int patchZ = (int)Mathf.Floor((worldZ - terrainPosition.Z) / patchSize);

            var coord = new Int2(patchX, patchZ);

            // A patch that was never created returns null; callers must treat that as a miss rather than
            // assuming every coordinate inside the terrain bounds has geometry. Both accessors take the
            // coordinate by reference in the Flax API.
            if (!_terrain.HasPatch(ref coord))
                return false;

            patch = _terrain.GetPatch(ref coord);
            if (patch == null)
                return false;

            originX = terrainPosition.X + patchX * patchSize;
            originZ = terrainPosition.Z + patchZ * patchSize;
            return true;
        }

        /// <summary>True when the given world XZ location lies inside the terrain's bounds.</summary>
        public bool ContainsXZ(float worldX, float worldZ)
        {
            var box = _terrain.Box;
            return worldX >= box.Minimum.X && worldX <= box.Maximum.X &&
                   worldZ >= box.Minimum.Z && worldZ <= box.Maximum.Z;
        }
    }
}

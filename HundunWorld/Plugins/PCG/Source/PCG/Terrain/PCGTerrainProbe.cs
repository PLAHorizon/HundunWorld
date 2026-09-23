// PCG Plugin - Terrain calibration probe
// Part of the FlaxEngine PCG plugin. See docs/PCG_PLUGIN_DESIGN.md §3.5 point 1.

using System;
using FlaxEngine;

namespace PCG.TerrainSampling
{
    /// <summary>
    /// Measures the mapping from raw patch heightmap values to world Y.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The problem this solves.</b> <c>TerrainPatch.GetHeightmapData()</c> hands back a flat float array,
    /// but nothing in the engine headers states what those floats mean in world units. Assuming "it is
    /// already metres" is a coin flip: if it is not, every spawned object is displaced by a constant factor,
    /// which looks plausible enough to survive a code review and only shows up as vegetation sunk into hills
    /// or floating above them.
    /// </para>
    /// <para>
    /// <b>The approach.</b> Measure instead of assume. Probe several locations across the terrain, obtain the
    /// truth from the ray cast path (which is unambiguous, since it returns a distance), read the raw
    /// heightmap value at the same spot, and least-squares fit an affine mapping between them. The residual
    /// is reported so a bad fit - meaning the relationship is not actually affine - is caught immediately
    /// rather than baked into a calibration that silently misplaces everything.
    /// </para>
    /// <para>
    /// Until a calibration exists, <see cref="PCGTerrainSampler"/> refuses the heightfield path and falls
    /// back to ray casts. Failing slow is the right trade against failing wrong.
    /// </para>
    /// </remarks>
    public static class PCGTerrainProbe
    {
        /// <summary>Default ceiling to start downward ray casts from.</summary>
        public const float DefaultProbeStartY = 20000.0f;

        /// <summary>
        /// Measures the calibration for a terrain.
        /// </summary>
        /// <param name="terrain">Terrain to probe. Must have at least one created patch.</param>
        /// <param name="probeCountPerAxis">Probes per axis; the total is this squared. Clamped to at least 3.</param>
        /// <param name="startY">Y to begin ray casts from.</param>
        /// <param name="maxResidualTolerance">
        /// Largest acceptable disagreement in world units between the fitted model and the ray cast truth.
        /// Exceeding it marks the calibration invalid rather than returning a doubtful one.
        /// </param>
        /// <param name="log">When true, writes progress and the outcome to the Flax log.</param>
        /// <returns>The measured calibration, or an invalid one when probing failed.</returns>
        public static PCGTerrainCalibration Calibrate(
            Terrain terrain,
            int probeCountPerAxis = 5,
            float startY = DefaultProbeStartY,
            float maxResidualTolerance = 0.05f,
            bool log = true)
        {
            if (terrain == null)
            {
                if (log)
                    Debug.LogWarning("[PCG] Terrain calibration skipped: no terrain supplied.");

                return PCGTerrainCalibration.Invalid;
            }

            if (probeCountPerAxis < 3)
                probeCountPerAxis = 3;

            var box = terrain.Box;
            float minX = box.Minimum.X;
            float maxX = box.Maximum.X;
            float minZ = box.Minimum.Z;
            float maxZ = box.Maximum.Z;

            if (maxX - minX <= 0.0f || maxZ - minZ <= 0.0f)
            {
                if (log)
                    Debug.LogWarning("[PCG] Terrain calibration failed: terrain bounds are degenerate.");

                return PCGTerrainCalibration.Invalid;
            }

            // A sampler with an invalid calibration uses the ray cast path, which is exactly what we want as
            // ground truth here.
            var probe = new PCGTerrainSampler(terrain, PCGTerrainCalibration.Invalid);

            // Affine least squares: worldY = scale * raw + offset.
            double sumRaw = 0.0, sumWorld = 0.0, sumRawRaw = 0.0, sumRawWorld = 0.0;
            int n = 0;
            int misses = 0;

            // Split by cause. Without this a total failure is indistinguishable from "terrain has no
            // patches", "patch exists but heightmap is not resident", and "ray casts miss", which need
            // completely different fixes.
            int heightfieldMisses = 0;
            int rayMisses = 0;

            for (int iy = 0; iy < probeCountPerAxis; iy++)
            {
                for (int ix = 0; ix < probeCountPerAxis; ix++)
                {
                    // Inset from the terrain edge so probes land on real geometry rather than the boundary,
                    // where patches may not exist.
                    float tx = (ix + 0.5f) / probeCountPerAxis;
                    float tz = (iy + 0.5f) / probeCountPerAxis;

                    float x = Mathf.Lerp(minX, maxX, tx);
                    float z = Mathf.Lerp(minZ, maxZ, tz);

                    if (!probe.TryGetRawHeightfieldValue(x, z, out float raw))
                    {
                        misses++;
                        heightfieldMisses++;
                        continue;
                    }

                    var truth = probe.Sample(x, z, PCGTerrainSampleMode.RayCast, startY);
                    if (!truth.Hit)
                    {
                        misses++;
                        rayMisses++;
                        continue;
                    }

                    sumRaw += raw;
                    sumWorld += truth.Height;
                    sumRawRaw += (double)raw * raw;
                    sumRawWorld += (double)raw * truth.Height;
                    n++;
                }
            }

            if (n < 3)
            {
                if (log)
                {
                    // Report the terrain state the miss counters cannot express on their own: whether the
                    // actor has any geometry at all, and whether the probe grid actually lands on it.
                    // TryGetPatch is reused rather than re-deriving patch coordinates here, so the diagnosis
                    // cannot drift from the sampler's own resolution (which takes the coordinate by ref).
                    float midX = minX + (maxX - minX) * 0.5f;
                    float midZ = minZ + (maxZ - minZ) * 0.5f;
                    bool centreFound = probe.TryGetPatch(midX, midZ, out var centrePatch, out float originX, out float originZ);

                    string dataState = "n/a";
                    if (centrePatch != null)
                    {
                        unsafe
                        {
                            dataState = centrePatch.GetHeightmapData() == null ? "null" : "present";
                        }
                    }

                    Debug.LogWarning($"[PCG] Terrain calibration failed: only {n} usable probe(s) " +
                                     $"({misses} missed). Is the terrain fully created and above the ray start height?");
                    Debug.LogWarning($"[PCG]   miss breakdown: heightfield={heightfieldMisses}, ray={rayMisses}");
                    Debug.LogWarning($"[PCG]   terrain pos={terrain.Position} box=[{box.Minimum} .. {box.Maximum}] " +
                                     $"patchSize={terrain.PatchSize} heightmapSize={terrain.HeightmapSize} " +
                                     $"patches={terrain.PatchesCount}");
                    Debug.LogWarning($"[PCG]   centre probe x={midX} z={midZ} resolved={centreFound} " +
                                     $"patch={(centrePatch == null ? "null" : "present")} " +
                                     $"origin=({originX}, {originZ}) heightmapData={dataState}");
                }

                return PCGTerrainCalibration.Invalid;
            }

            double meanRaw = sumRaw / n;
            double meanWorld = sumWorld / n;

            double covariance = sumRawWorld - n * meanRaw * meanWorld;
            double variance = sumRawRaw - n * meanRaw * meanRaw;

            float scale;
            float offset;

            if (Math.Abs(variance) < 1e-12)
            {
                // Every probe returned an identical raw value, so the slope is unidentifiable. Treat as flat
                // terrain at the measured height rather than dividing by ~0.
                scale = 1.0f;
                offset = (float)meanWorld;

                if (log)
                    Debug.Log("[PCG] Terrain calibration: raw heightmap is constant; treating terrain as flat.");
            }
            else
            {
                scale = (float)(covariance / variance);
                offset = (float)(meanWorld - scale * meanRaw);
            }

            var candidate = new PCGTerrainCalibration
            {
                HeightScale = scale,
                HeightOffset = offset,
                TerrainId = terrain.ID,
                SampleCount = n,
                IsValid = true,
            };

            // Independent verification pass: re-read the heightfield through the candidate calibration and
            // compare against fresh ray casts. A non-affine relationship shows up here as a large residual.
            var check = new PCGTerrainSampler(terrain, candidate);
            float maxResidual = 0.0f;
            int checkedCount = 0;

            for (int iy = 0; iy < probeCountPerAxis; iy++)
            {
                for (int ix = 0; ix < probeCountPerAxis; ix++)
                {
                    float tx = (ix + 0.5f) / probeCountPerAxis;
                    float tz = (iy + 0.5f) / probeCountPerAxis;

                    // Offset the verification points slightly so they do not coincide with the fit points.
                    float x = Mathf.Lerp(minX, maxX, Mathf.Clamp(tx + 0.37f / probeCountPerAxis, 0.02f, 0.98f));
                    float z = Mathf.Lerp(minZ, maxZ, Mathf.Clamp(tz + 0.41f / probeCountPerAxis, 0.02f, 0.98f));

                    var truth = probe.Sample(x, z, PCGTerrainSampleMode.RayCast, startY);
                    if (!truth.Hit)
                        continue;

                    var fitted = check.Sample(x, z, PCGTerrainSampleMode.HeightfieldInterpolated, startY, false);
                    if (!fitted.Hit)
                        continue;

                    float residual = Mathf.Abs(fitted.Height - truth.Height);
                    if (residual > maxResidual)
                        maxResidual = residual;

                    checkedCount++;
                }
            }

            candidate.MaxResidual = maxResidual;

            if (checkedCount > 0 && maxResidual > maxResidualTolerance)
            {
                candidate.IsValid = false;

                if (log)
                {
                    Debug.LogWarning(
                        $"[PCG] Terrain calibration rejected: fitted model disagrees with ray casts by up to " +
                        $"{maxResidual:G6} world units (tolerance {maxResidualTolerance:G6}) over {checkedCount} " +
                        "verification point(s). The heightmap-to-world mapping is probably not affine on this " +
                        "terrain, so the PCG heightfield path will keep using ray casts.");
                }

                return candidate;
            }

            if (log)
            {
                Debug.Log($"[PCG] Terrain calibrated: {candidate} " +
                          $"(verified over {checkedCount} point(s), {misses} probe(s) missed).");
            }

            return candidate;
        }
    }
}

// PCG Plugin - Terrain calibration cache

using System;
using System.Collections.Generic;
using FlaxEngine;
using PCG.TerrainSampling;

namespace PCG.TerrainSampling
{
    /// <summary>
    /// Holds a measured <see cref="PCGTerrainCalibration"/> per terrain so the expensive probing pass runs
    /// once rather than on every graph execution.
    /// </summary>
    /// <remarks>
    /// Static and dictionary-backed, so it is cleared on a scripts reload together with the other plugin
    /// static state. Keyed by terrain id rather than by actor reference: a terrain can be reloaded and
    /// replaced, and a stale reference would keep the whole actor alive.
    /// </remarks>
    public static class PCGTerrainCalibrationStore
    {
        private static readonly Dictionary<Guid, PCGTerrainCalibration> Cache =
            new Dictionary<Guid, PCGTerrainCalibration>();

        /// <summary>Raised when an entry changes, so the editor can refresh any displayed value.</summary>
        public static event Action Changed;

        /// <summary>Number of cached calibrations.</summary>
        public static int Count => Cache.Count;

        /// <summary>
        /// Gets the calibration for a terrain, measuring it on first use.
        /// </summary>
        /// <param name="terrain">Terrain to query.</param>
        /// <param name="autoCalibrate">
        /// When true, a missing entry is measured immediately. Set false to query without side effects.
        /// </param>
        /// <param name="log">Passed through to the probe.</param>
        /// <returns>
        /// The calibration, or an invalid one when none is available. An invalid calibration makes samplers
        /// fall back to ray casts, which is correct but slower.
        /// </returns>
        public static PCGTerrainCalibration Get(Terrain terrain, bool autoCalibrate = true, bool log = true)
        {
            if (terrain == null)
                return PCGTerrainCalibration.Invalid;

            var id = terrain.ID;

            if (Cache.TryGetValue(id, out var cached))
            {
                // Invalidate when the terrain no longer exists, so a deleted-and-recreated terrain does not
                // inherit an unrelated calibration.
                if (terrain.IsActiveInHierarchy)
                    return cached;

                Cache.Remove(id);
                Changed?.Invoke();
            }

            if (!autoCalibrate)
                return PCGTerrainCalibration.Invalid;

            var measured = PCGTerrainProbe.Calibrate(terrain, log: log);
            Cache[id] = measured;
            Changed?.Invoke();
            return measured;
        }

        /// <summary>Stores a calibration explicitly, for example one loaded from a settings asset.</summary>
        public static void Set(Terrain terrain, PCGTerrainCalibration calibration)
        {
            if (terrain == null)
                return;

            Cache[terrain.ID] = calibration;
            Changed?.Invoke();
        }

        /// <summary>True when a calibration is cached for the terrain.</summary>
        public static bool Has(Terrain terrain)
        {
            return terrain != null && Cache.ContainsKey(terrain.ID);
        }

        /// <summary>Drops the entry for one terrain, forcing a re-measure on next use.</summary>
        public static void Invalidate(Terrain terrain)
        {
            if (terrain == null)
                return;

            if (Cache.Remove(terrain.ID))
                Changed?.Invoke();
        }

        /// <summary>Drops every entry.</summary>
        public static void Clear()
        {
            if (Cache.Count == 0)
                return;

            Cache.Clear();
            Changed?.Invoke();
        }

        /// <summary>Summarises every cached calibration, for diagnostics.</summary>
        public static string DescribeAll()
        {
            if (Cache.Count == 0)
                return "No terrain calibrations cached.";

            var text = new System.Text.StringBuilder();
            text.Append(Cache.Count).Append(" terrain calibration(s):");

            foreach (var kv in Cache)
                text.Append("\n  ").Append(kv.Key).Append(": ").Append(kv.Value);

            return text.ToString();
        }
    }
}

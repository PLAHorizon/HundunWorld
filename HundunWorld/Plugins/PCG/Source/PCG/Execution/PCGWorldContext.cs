// PCG Plugin - World queries available to nodes
// Part of the FlaxEngine PCG plugin. See docs/PCG_PLUGIN_DESIGN.md §3.5.

using System;
using System.Collections.Generic;
using FlaxEngine;

namespace PCG.Execution
{
    /// <summary>
    /// Scene-side lookup services a graph run needs: which terrain to sample, and how to reach actors
    /// referenced by nodes (splines, spawn parents).
    /// </summary>
    /// <remarks>
    /// Kept separate from <see cref="PCGExecutionContext"/> so headless tests can substitute a stub without
    /// a loaded scene, and so nodes never reach for global state directly.
    /// </remarks>
    public sealed class PCGWorldContext
    {
        private readonly List<Terrain> _terrains = new List<Terrain>();

        /// <summary>Terrain actors available for sampling, in scene order.</summary>
        public IReadOnlyList<Terrain> Terrains => _terrains;

        /// <summary>
        /// Resolves actor references by id. Populated by the caller; nodes use it for spline and parent
        /// lookups rather than scanning the level themselves.
        /// </summary>
        public Func<Guid, Actor> ActorResolver { get; set; }

        /// <summary>When set, overrides <see cref="ActorResolver"/> name lookups by actor name.</summary>
        public Func<string, Actor> ActorNameResolver { get; set; }

        /// <summary>True when a terrain is available for sampling.</summary>
        public bool HasTerrain => _terrains.Count > 0;

        /// <summary>Registers a terrain for sampling. Duplicates are ignored.</summary>
        public void AddTerrain(Terrain terrain)
        {
            if (terrain != null && !_terrains.Contains(terrain))
                _terrains.Add(terrain);
        }

        /// <summary>Removes all registered terrains.</summary>
        public void ClearTerrains()
        {
            _terrains.Clear();
        }

        /// <summary>
        /// Returns the terrain whose world bounds contain the given point on the XZ plane, or null.
        /// </summary>
        public Terrain FindTerrainAt(Vector3 worldPosition)
        {
            for (int i = 0; i < _terrains.Count; i++)
            {
                var terrain = _terrains[i];
                if (terrain == null || !terrain.IsActiveInHierarchy)
                    continue;

                var box = terrain.Box;
                if (worldPosition.X >= box.Minimum.X && worldPosition.X <= box.Maximum.X &&
                    worldPosition.Z >= box.Minimum.Z && worldPosition.Z <= box.Maximum.Z)
                {
                    return terrain;
                }
            }

            // Fall back to the only registered terrain; a single-terrain scene is the common case and
            // bounds can be conservative for a terrain whose patches are only partly created.
            return _terrains.Count == 1 ? _terrains[0] : null;
        }

        /// <summary>Populates the terrain list from the loaded scenes.</summary>
        /// <remarks>
        /// Must be called on the main thread; actor enumeration is not thread-safe.
        /// </remarks>
        public void CollectTerrainsFromScene()
        {
            _terrains.Clear();

            var terrains = Level.GetActors<Terrain>();
            if (terrains != null)
            {
                for (int i = 0; i < terrains.Length; i++)
                    AddTerrain(terrains[i]);
            }
        }

        /// <summary>Resolves an actor by id using the configured resolver.</summary>
        public Actor ResolveActor(Guid id)
        {
            if (id == Guid.Empty)
                return null;

            return ActorResolver?.Invoke(id);
        }

        /// <summary>Resolves an actor by name using the configured resolver.</summary>
        public Actor ResolveActor(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;

            return ActorNameResolver?.Invoke(name);
        }
    }
}

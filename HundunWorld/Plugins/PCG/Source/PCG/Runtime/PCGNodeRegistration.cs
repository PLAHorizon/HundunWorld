// PCG Plugin - Node registration

using FlaxEngine;
using PCG.Execution;
using PCG.Nodes;

namespace PCG.Runtime
{
    /// <summary>Node categories used to group entries in the graph editor's add-node menu.</summary>
    public static class PCGNodeCategories
    {
        /// <summary>Nodes that create points from the world.</summary>
        public const string Sampler = "Sampler";

        /// <summary>Nodes that remove points.</summary>
        public const string Filter = "Filter";

        /// <summary>Nodes that change points in place.</summary>
        public const string Modifier = "Modifier";

        /// <summary>Nodes that combine or split point sets.</summary>
        public const string Combinator = "Combinator";

        /// <summary>Nodes that materialise points as scene content.</summary>
        public const string Spawner = "Spawner";
    }

    /// <summary>
    /// Registers every built-in node type with <see cref="PCGNodeRegistry"/>.
    /// </summary>
    /// <remarks>
    /// Kept in one place so the registry can be torn down and rebuilt on a scripts reload, and so the editor
    /// and the runtime cannot drift apart over which nodes exist.
    /// </remarks>
    public static class PCGNodeRegistration
    {
        private static bool _registered;

        /// <summary>True once <see cref="RegisterAll"/> has run without a subsequent clear.</summary>
        public static bool IsRegistered => _registered;

        /// <summary>
        /// Registers every built-in node type. Safe to call repeatedly; re-registration replaces entries in
        /// place rather than duplicating them.
        /// </summary>
        /// <param name="log">When true, writes the resulting node list to the log.</param>
        /// <returns>The number of registered node types.</returns>
        public static int RegisterAll(bool log = false)
        {
            // Samplers.
            PCGNodeRegistry.Register(
                PCGLandscapeSamplerNode.Id,
                PCGNodeCategories.Sampler,
                data => new PCGLandscapeSamplerNode(data),
                "Landscape Sampler",
                "Places points across the terrain surface inside the generation bounds.");

            // Filters.
            PCGNodeRegistry.Register(
                PCGDensityFilterNode.Id,
                PCGNodeCategories.Filter,
                data => new PCGDensityFilterNode(data),
                "Density Filter",
                "Keeps points whose density falls inside a range.");

            // Spawners.
            PCGNodeRegistry.Register(
                PCGSpawnStaticModelNode.Id,
                PCGNodeCategories.Spawner,
                data => new PCGSpawnStaticModelNode(data),
                "Spawn Static Model",
                "Spawns a static mesh at every input point.");

            _registered = true;

            if (log)
                PCGNodeRegistry.LogRegisteredTypes();

            return PCGNodeRegistry.Count;
        }

        /// <summary>
        /// Clears the registrations. Must run on a scripts reload, because the stored factories reference
        /// types in the assembly that is being unloaded.
        /// </summary>
        public static void UnregisterAll()
        {
            PCGNodeRegistry.Clear();
            _registered = false;
        }
    }
}

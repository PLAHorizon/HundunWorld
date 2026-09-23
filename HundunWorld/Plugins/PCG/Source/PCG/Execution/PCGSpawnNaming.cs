// PCG Plugin - Shared naming contract for generated scene objects

namespace PCG.Execution
{
    /// <summary>
    /// Naming contract between spawner nodes and the volumes that own their output.
    /// </summary>
    /// <remarks>
    /// Spawner nodes create actors without knowing which volume requested the run, and volumes need to
    /// recognise exactly their own output so a regenerate can replace it instead of stacking a second copy.
    /// Both sides therefore agree on one prefix. It lives here rather than on either participant so neither
    /// can drift from the other unnoticed.
    /// </remarks>
    public static class PCGSpawnNaming
    {
        /// <summary>
        /// Prefix every PCG-spawned actor's name begins with.
        /// </summary>
        /// <remarks>
        /// A runtime tag, not this prefix, is the authoritative ownership marker: names are not queryable
        /// without enumerating the scene, so the tag is what a volume uses to reclaim output after a reload.
        /// The prefix is for human readability in the outliner plus first-pass identification.
        /// </remarks>
        public const string GeneratedPrefix = "PCG_Gen_";

        /// <summary>True when an actor name marks it as PCG-generated.</summary>
        public static bool IsGeneratedName(string name)
        {
            return !string.IsNullOrEmpty(name) &&
                   name.StartsWith(GeneratedPrefix, System.StringComparison.Ordinal);
        }
    }
}

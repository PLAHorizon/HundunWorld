// PCG Plugin - one generation layer of a volume
// Part of the FlaxEngine PCG plugin. See docs/PCG_PLUGIN_DESIGN.md §7.3 (分层网格).

using System;

namespace PCG.Graph
{
    /// <summary>
    /// One graph at one cell granularity inside a <c>PCGVolume</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Layering exists because one region needs props at different densities: boulders and ruined walls want a
    /// coarse grid (few points, few runs, big budget each) while ground detail wants a fine one. Splitting them
    /// keeps the cell count of the sparse layer reasonable instead of forcing every prop through the same grid,
    /// and lets the dense layer's points be thinned per cell rather than all at once.
    /// </para>
    /// <para>
    /// Each layer is a whole generation of its own: its graph file, its own cell size, padding, seed offset and
    /// result cache. Objects it creates carry the layer in their names, so one layer's per-cell cleanup cannot
    /// reach another layer's props that happen to sit in the same cell.
    /// </para>
    /// </remarks>
    [Serializable]
    public sealed class PCGPartitionLayer
    {
        /// <summary>Human-readable label, used in logs. Empty falls back to the layer index.</summary>
        public string Name;

        /// <summary>Graph file for this layer; empty means the volume's own graph file.</summary>
        public string GraphPath;

        /// <summary>Cell edge on X and Z for this layer, in world units.</summary>
        public float CellSize = 500.0f;

        /// <summary>Padding per cell; -1 derives it from that graph's own pruning spacing.</summary>
        public float Padding = -1.0f;

        /// <summary>Added to the volume seed so layers do not produce the same layout.</summary>
        public int SeedOffset;

        /// <summary>Set when this layer takes part in the generation.</summary>
        public bool Enabled = true;

        /// <summary>The label used in log lines.</summary>
        public string DisplayName => string.IsNullOrEmpty(Name) ? "layer" : Name;

        /// <inheritdoc />
        public override string ToString() =>
            $"{Name ?? "(unnamed)"}: graph={(string.IsNullOrEmpty(GraphPath) ? "(volume's own)" : GraphPath)}, " +
            $"cell={CellSize:0.##}, padding={(Padding < 0.0f ? "auto" : Padding.ToString("0.##"))}, seed+{SeedOffset}";
    }
}

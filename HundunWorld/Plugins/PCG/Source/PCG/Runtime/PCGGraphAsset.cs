// PCG Plugin - Graph asset
// Part of the FlaxEngine PCG plugin. See docs/PCG_PLUGIN_DESIGN.md §3.3.

using System;
using FlaxEngine;
using PCG.Graph;

namespace PCG.Runtime
{
    /// <summary>
    /// Flax asset wrapping a <see cref="PCGGraph"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The graph is held as a live object graph in a <see cref="Script"/> field, so Flax's own serializer
    /// persists it with the asset and it survives editor script reloads without a separate format. The
    /// document originally proposed starting on a JSON asset and migrating later; holding the graph directly
    /// turned out to be simpler, because the model is plain serializable state with no dependency on the
    /// plugin's runtime types.
    /// </para>
    /// <para>
    /// An explicit <see cref="GraphData"/> entry point is also provided for importing a graph authored
    /// elsewhere, and <see cref="EditorData"/> carries editor-only layout that the runtime ignores.
    /// </para>
    /// </remarks>
    public class PCGGraphAsset : Asset
    {
        /// <summary>The graph this asset owns. This is the single source of truth.</summary>
        [Tooltip("The graph topology, node parameters and settings.")]
        public PCGGraph GraphData;

        /// <summary>
        /// Graph editor layout and any editor-only state, kept opaque to the runtime so the editor can change
        /// its own persistence without touching this type.
        /// </summary>
        [Tooltip("Editor-only layout data. Not used at runtime.")]
        public string EditorData;

        /// <summary>
        /// Gets the graph, creating an empty one on first access so a freshly created asset is immediately
        /// editable rather than null-checked everywhere.
        /// </summary>
        public PCGGraph GetGraph()
        {
            GraphData ??= new PCGGraph();
            return GraphData;
        }

        /// <summary>Replaces the graph wholesale.</summary>
        public void SetGraph(PCGGraph graph)
        {
            GraphData = graph ?? new PCGGraph();
        }

        /// <summary>True when the asset has a graph with at least one node.</summary>
        public bool HasContent => GraphData != null && GraphData.NodeCount > 0;

        /// <summary>
        /// Structural hash of the contained graph, used to decide whether a recompile is needed.
        /// </summary>
        public int ComputeStructuralHash()
        {
            return GraphData == null ? 0 : Execution.PCGGraphCompiler.ComputeStructuralHash(GraphData);
        }

        /// <inheritdoc />
        public override string ToString() =>
            GraphData == null
                ? $"{Path} (empty)"
                : $"{Path} ({GraphData.NodeCount} node(s), {GraphData.EdgeCount} edge(s))";
    }
}

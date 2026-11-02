// PCG Plugin - Partitioned generation planning
// Part of the FlaxEngine PCG plugin. See docs/PCG_PLUGIN_DESIGN.md §7.3.

using System;
using System.Collections.Generic;
using FlaxEngine;

namespace PCG.Execution
{
    /// <summary>
    /// One cell of a partitioned generation run.
    /// </summary>
    public readonly struct PCGPartitionCell
    {
        /// <summary>Region handed to the graph: the cell grown by the padding on X and Z.</summary>
        public readonly BoundingBox GenerationBounds;

        /// <summary>Region whose contents survive: the cell itself.</summary>
        public readonly BoundingBox ClipBounds;

        /// <summary>Cell column index.</summary>
        public readonly int IndexX;

        /// <summary>Cell row index.</summary>
        public readonly int IndexZ;

        /// <summary>True when this cell ends the region on X, so its upper X edge must still be kept.</summary>
        public readonly bool IsLastX;

        /// <summary>True when this cell ends the region on Z, so its upper Z edge must still be kept.</summary>
        public readonly bool IsLastZ;

        public PCGPartitionCell(
            BoundingBox generationBounds, BoundingBox clipBounds, int indexX, int indexZ,
            bool isLastX = false, bool isLastZ = false)
        {
            GenerationBounds = generationBounds;
            ClipBounds = clipBounds;
            IndexX = indexX;
            IndexZ = indexZ;
            IsLastX = isLastX;
            IsLastZ = isLastZ;
        }

        /// <summary>Cell coordinates as they appear in diagnostics, e.g. "cell 3,-1".</summary>
        public string Label => $"cell {IndexX},{IndexZ}";

        /// <inheritdoc />
        public override string ToString() => $"{Label}: gen {GenerationBounds} clip {ClipBounds}";
    }

    /// <summary>
    /// Splits a generation region into cells, each grown by a padding margin.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Partitioning exists so a large world can be generated in bounded pieces. Its cost is that anything
    /// looking at neighbours - the self-pruning filter above all - stops seeing across a border, and the
    /// result is a visible line of missing or duplicated props along every cell edge. Growing each cell by a
    /// margin and then clipping to the cell lets a point's fate be decided by neighbours that happen to sit in
    /// the adjacent cell: the padding must therefore be at least the distance the pruner enforces, which is
    /// what <see cref="SuggestedPadding"/> is for.
    /// </para>
    /// <para>
    /// Partitioning is on the XZ plane only, matching how an open world streams: a cell's Y span stays the
    /// region's full height, so a terrain sampler inside a cell still sees the whole column of terrain rather
    /// than a slice of it. Padding is applied to X and Z for the same reason.
    /// </para>
    /// <para>
    /// What partitioning does <b>not</b> preserve: a sampler that indexes points by its own grid inside its
    /// bounds (the landscape and noise samplers) generates different per-point random draws once it runs per
    /// cell, so the layout will differ from a single unpartitioned run. That is inherent to the decomposition,
    /// not a bug to fix; the guarantee this provides is narrower and is the one that matters visually - the
    /// neighbourhood-dependent stages behave as if the world were continuous.
    /// </para>
    /// </remarks>
    public static class PCGPartitionPlanner
    {
        /// <summary>Ceiling on cell count, so a fat-fingered cell size cannot ask for millions of runs.</summary>
        public const int MaxCells = 4096;

        /// <summary>
        /// Plans the cells covering <paramref name="region"/>.
        /// </summary>
        /// <param name="region">Whole generation region.</param>
        /// <param name="cellSize">Cell edge length on X and Z, in world units.</param>
        /// <param name="padding">How far each cell reaches past its own edge; clamped to half a cell.</param>
        /// <param name="error">Why nothing could be planned, when the result is empty.</param>
        public static List<PCGPartitionCell> Plan(BoundingBox region, float cellSize, float padding, out string error)
        {
            error = null;

            float widthX = region.Maximum.X - region.Minimum.X;
            float widthZ = region.Maximum.Z - region.Minimum.Z;

            if (cellSize <= 0.0f || !float.IsFinite(cellSize))
            {
                error = "Partitioning needs a cell size greater than zero.";
                return null;
            }

            if (widthX <= 0.0f || widthZ <= 0.0f)
            {
                error = "The generation region has no XZ extent to partition.";
                return null;
            }

            // A margin wider than half a cell would make neighbouring cells agree on more than they disagree,
            // so points would be generated twice and then clipped down to the same answer at a cost. Clamping
            // keeps the overlap meaningful: enough to see across the border, not more.
            float pad = Math.Max(0.0f, Math.Min(padding, cellSize * 0.5f));

            int cellsX = (int)Math.Ceiling(widthX / cellSize);
            int cellsZ = (int)Math.Ceiling(widthZ / cellSize);

            // Rounding up a division that was only just over an edge count leaves a trailing cell with no width
            // of its own. It would still run the whole graph, spend the padding's work and then clip every point
            // away, so the empty strip is cut rather than planned.
            while (cellsX > 1 && region.Minimum.X + (cellsX - 1) * cellSize >= region.Maximum.X)
                cellsX--;
            while (cellsZ > 1 && region.Minimum.Z + (cellsZ - 1) * cellSize >= region.Maximum.Z)
                cellsZ--;

            long count = (long)cellsX * cellsZ;
            if (count > MaxCells)
            {
                error = $"Partitioning this region would produce {count} cells, above the {MaxCells} limit. " +
                        $"Raise the cell size (currently {cellSize:0.##}) or shrink the volume.";
                return null;
            }

            var cells = new List<PCGPartitionCell>((int)count);

            for (int iz = 0; iz < cellsZ; iz++)
            {
                for (int ix = 0; ix < cellsX; ix++)
                {
                    // Edges are clamped to the region so the clip boxes tile it exactly; the padding then
                    // reaches past the edge anyway, which is harmless outside the region and keeps corner
                    // cells as well-populated as interior ones.
                    float minX = region.Minimum.X + ix * cellSize;
                    float minZ = region.Minimum.Z + iz * cellSize;
                    float maxX = Math.Min(minX + cellSize, region.Maximum.X);
                    float maxZ = Math.Min(minZ + cellSize, region.Maximum.Z);

                    var clip = new BoundingBox(
                        new Vector3(minX, region.Minimum.Y, minZ),
                        new Vector3(maxX, region.Maximum.Y, maxZ));

                    var generation = new BoundingBox(
                        new Vector3(minX - pad, region.Minimum.Y, minZ - pad),
                        new Vector3(maxX + pad, region.Maximum.Y, maxZ + pad));

                    cells.Add(new PCGPartitionCell(
                        generation, clip, ix, iz, ix == cellsX - 1, iz == cellsZ - 1));
                }
            }

            return cells;
        }

        /// <summary>
        /// Plans several cell grids over one region, one per generation layer.
        /// </summary>
        /// <remarks>
        /// A layer exists so large sparse props and dense ground detail can live in one volume at different
        /// granularities: detail wants small cells (few points per run), boulders want big ones (few runs).
        /// Planning them together is arithmetic, so the guarantees worth asserting are made here rather than in
        /// the scene: every layer still tiles the region, and a layer that would blow past the cell ceiling is
        /// reported by name instead of being silently coarsened.
        /// </remarks>
        /// <param name="region">Whole generation region, shared by every layer.</param>
        /// <param name="cellSizes">Cell edge per layer.</param>
        /// <param name="paddings">Padding per layer; may be shorter, missing entries mean none.</param>
        /// <param name="layerNames">Names used in error messages; may be null.</param>
        /// <param name="errors">One entry per layer that could not be planned; null for a fully good set.</param>
        /// <returns>One cell list per layer, or null when any layer failed.</returns>
        public static List<PCGPartitionCell>[] PlanLayers(
            BoundingBox region, float[] cellSizes, float[] paddings, string[] layerNames, out List<string> errors)
        {
            errors = null;

            if (cellSizes == null || cellSizes.Length == 0)
            {
                errors = new List<string> { "No generation layers were given." };
                return null;
            }

            var plans = new List<PCGPartitionCell>[cellSizes.Length];

            for (int i = 0; i < cellSizes.Length; i++)
            {
                float padding = paddings != null && i < paddings.Length ? paddings[i] : 0.0f;
                string name = layerNames != null && i < layerNames.Length && !string.IsNullOrEmpty(layerNames[i])
                    ? layerNames[i]
                    : $"layer {i}";

                var cells = Plan(region, cellSizes[i], padding, out string error);
                if (cells == null)
                {
                    errors = errors ?? new List<string>();
                    errors.Add($"{name}: {error}");
                    continue;
                }

                plans[i] = cells;
            }

            return errors == null ? plans : null;
        }

        /// <summary>
        /// Padding that makes the given conflict distance continuous across a cell border.
        /// </summary>
        /// <remarks>
        /// A point is safe to clip only once every point that could have pruned it has been seen; that is the
        /// pruner's own distance, plus a margin for the fact that a point's <em>bounds</em> can reach further
        /// than its centre in overlap mode.
        /// </remarks>
        public static float SuggestedPadding(float conflictDistance, bool boundsOverlapMode, float largestHalfExtent)
        {
            if (conflictDistance <= 0.0f && !boundsOverlapMode)
                return 0.0f;

            return boundsOverlapMode
                ? conflictDistance + 2.0f * Math.Max(0.0f, largestHalfExtent)
                : conflictDistance;
        }

        /// <summary>
        /// True when a position belongs to the cell proper rather than to its padding.
        /// </summary>
        /// <remarks>
        /// X and Z are half-open intervals, lower bound included: a point sitting exactly on a border would
        /// otherwise survive in both neighbouring cells and be generated twice, which is the doubling this
        /// whole file exists to avoid. The region's own far edges are closed by <see cref="IsLastX"/> /
        /// <see cref="IsLastZ"/>, otherwise points a grid sampler placed exactly on the region maximum would be
        /// dropped by the last cell and appear in no cell at all. Y is not partitioned, so both its edges stay
        /// closed.
        /// </remarks>
        public static bool IsInsideCell(in PCGPartitionCell cell, Vector3 position)
        {
            if (position.Y < cell.ClipBounds.Minimum.Y || position.Y > cell.ClipBounds.Maximum.Y)
                return false;

            return EdgeInside(cell.ClipBounds.Minimum.X, cell.ClipBounds.Maximum.X, position.X, cell.IsLastX) &&
                   EdgeInside(cell.ClipBounds.Minimum.Z, cell.ClipBounds.Maximum.Z, position.Z, cell.IsLastZ);
        }

        private static bool EdgeInside(float min, float max, float value, bool maxIncluded)
        {
            if (value < min)
                return false;

            return maxIncluded ? value <= max : value < max;
        }

        /// <summary>
        /// Padding a partitioned run needs so the graph's own spacing rules keep working across cell borders.
        /// </summary>
        /// <remarks>
        /// Derived from the graph rather than guessed by the designer: a run whose padding is shorter than the
        /// spacing its pruners enforce shows a seam no matter how correct the rest of the planner is, so the
        /// one number that must not be wrong is taken from the nodes that use it.
        /// </remarks>
        /// <param name="nodes">Compiled graph nodes, scanned for self-pruning filters.</param>
        /// <param name="largestHalfExtent">
        /// Largest point half-extent in the run, used only by pruners in bounds-overlap mode. Unknown before the
        /// points exist, so 0 falls back to the centre-distance spacing, which is the usual case.
        /// </param>
        public static float SuggestedPaddingForGraph(PCGNodeBase[] nodes, float largestHalfExtent = 0.0f)
        {
            float padding = 0.0f;
            if (nodes == null)
                return padding;

            for (int i = 0; i < nodes.Length; i++)
            {
                if (!(nodes[i] is Nodes.PCGSelfPruningFilterNode pruner))
                    continue;

                padding = Math.Max(
                    padding,
                    SuggestedPadding(pruner.MinDistance, pruner.UseBoundsOverlap, largestHalfExtent));
            }

            return padding;
        }
    }
}

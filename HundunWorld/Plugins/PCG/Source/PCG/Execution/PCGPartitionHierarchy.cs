// PCG Plugin - Hierarchical partition planning
// Part of the FlaxEngine PCG plugin. See docs/PCG_PLUGIN_DESIGN.md §7.4.

using System;
using System.Collections.Generic;
using FlaxEngine;

namespace PCG.Execution
{
    /// <summary>One level of a hierarchy: a grid over the region, and the size of its cells.</summary>
    public sealed class PCGPartitionLevel
    {
        /// <summary>Edge length of this level's cells.</summary>
        public readonly float CellSize;

        /// <summary>The cells of this level, in plan order.</summary>
        public readonly List<PCGPartitionCell> Cells;

        internal PCGPartitionLevel(float cellSize, List<PCGPartitionCell> cells)
        {
            CellSize = cellSize;
            Cells = cells;
        }
    }

    /// <summary>
    /// N levels of cells over one region: each level decides where the next one is worth generating.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is UE5's hierarchical generation: a cheap coarse pass asks "is there anything in this square at all",
    /// and only the squares that answered yes pay for the finer pass inside them. Two levels are the common case,
    /// but the same idea nests - a region can be decided at 8 km, then 2 km, then 500 m, then generated at 125 m -
    /// and stopping at two was an arbitrary limit rather than a property of the technique. A mostly empty world
    /// costs a handful of coarse runs per level instead of hundreds of leaf runs, and each additional level can
    /// only narrow the work further.
    /// </para>
    /// <para>
    /// The level at index 0 is the coarsest; the last one is the <b>generation level</b>, whose cells are what
    /// actually runs. Every other level is a deciding level and is asked for its points in a dry run, never for
    /// real - see <c>PCGVolume.ExecuteHierarchical</c>, which is the code that walks the cascade.
    /// </para>
    /// <para>
    /// One deliberate difference from UE5, stated here so nobody assumes otherwise: a finer level does not
    /// receive the coarser level's points as an input. This plugin's executor has no cross-run data channel, so a
    /// coarser result is used as a *filter* - "generate under me or not" - and a graph that wants to reuse coarse
    /// points in a finer pass has to put them in the same run. That is a smaller feature than UE5's, and it is
    /// the honest description of what this is.
    /// </para>
    /// </remarks>
    public sealed class PCGPartitionHierarchy
    {
        /// <summary>The levels, coarsest first, with the generation level last.</summary>
        public readonly List<PCGPartitionLevel> Levels;

        private readonly List<int[]> _parents;

        internal PCGPartitionHierarchy(List<PCGPartitionLevel> levels, List<int[]> parents)
        {
            Levels = levels;
            _parents = parents;
        }

        /// <summary>How many levels this hierarchy has; always at least two.</summary>
        public int LevelCount => Levels.Count;

        /// <summary>The finest level - the one whose cells are actually generated.</summary>
        public PCGPartitionLevel Generation => Levels[Levels.Count - 1];

        /// <summary>Index into the previous level of the cell a given cell sits in.</summary>
        /// <param name="level">Level to look up; must be 1 or more.</param>
        /// <param name="index">Cell index within that level.</param>
        public int ParentOf(int level, int index) => _parents[level][index];

        /// <summary>
        /// The cells of <paramref name="level"/> whose parent is in <paramref name="liveParents"/>, written into
        /// <paramref name="into"/>.
        /// </summary>
        /// <remarks>
        /// The parent is identified by its own grid indices rather than by object identity, so a caller can
        /// collect the live set from the previous level's per-cell results without keeping a reference to the plan
        /// around. This is the single step the whole cascade is built from: one call per level.
        /// </remarks>
        /// <returns>How many cells were written.</returns>
        public int CellsUnder(int level, HashSet<(int X, int Z)> liveParents, List<PCGPartitionCell> into)
        {
            if (into == null)
                throw new ArgumentNullException(nameof(into));

            if (level <= 0 || level >= Levels.Count)
                throw new ArgumentOutOfRangeException(nameof(level),
                    $"Level {level} has no parent level; the hierarchy has {Levels.Count} level(s).");

            into.Clear();

            if (liveParents == null || liveParents.Count == 0)
                return 0;

            var cells = Levels[level].Cells;
            var parents = _parents[level];
            var parentCells = Levels[level - 1].Cells;

            for (int i = 0; i < cells.Count; i++)
            {
                var parent = parentCells[parents[i]];

                if (liveParents.Contains((parent.IndexX, parent.IndexZ)))
                    into.Add(cells[i]);
            }

            return into.Count;
        }
    }

    /// <summary>
    /// Builds <see cref="PCGPartitionHierarchy"/> plans out of the ordinary <see cref="PCGPartitionPlanner"/>.
    /// </summary>
    /// <remarks>
    /// Every level is planned by the same function that plans a flat grid, so the edge rules - half-open cells,
    /// the trailing empty strip, the padding clamp, the cell ceiling - are one implementation rather than one per
    /// level that could drift. What this layer adds is the parent links between consecutive levels.
    /// </remarks>
    public static class PCGPartitionHierarchyPlanner
    {
        /// <summary>Most levels a hierarchy may have, so a typo cannot ask for a thousand grids.</summary>
        public const int MaxLevels = 8;

        /// <summary>
        /// Plans every level over one region, coarsest first.
        /// </summary>
        /// <param name="region">Whole generation region.</param>
        /// <param name="cellSizes">Cell size per level, from the coarsest to the generation level.</param>
        /// <param name="padding">Padding for every level, clamped per level as usual.</param>
        /// <param name="error">Why nothing could be planned, when the result is null.</param>
        /// <returns>The hierarchy, or null when the level list or any level was refused.</returns>
        public static PCGPartitionHierarchy Plan(
            BoundingBox region, float[] cellSizes, float padding, out string error)
        {
            error = null;

            if (cellSizes == null || cellSizes.Length < 2)
            {
                error = "Hierarchical generation needs at least two levels; with one there is nothing to decide.";
                return null;
            }

            if (cellSizes.Length > MaxLevels)
            {
                error = $"Hierarchical generation accepts at most {MaxLevels} levels, not {cellSizes.Length}.";
                return null;
            }

            for (int i = 0; i < cellSizes.Length; i++)
            {
                if (cellSizes[i] <= 0.0f)
                {
                    error = $"Level {i} needs a cell size greater than zero.";
                    return null;
                }

                if (i > 0 && cellSizes[i] >= cellSizes[i - 1])
                {
                    // The same check the two-level version made, generalised: a level that is not finer than the one
                    // above it decides over nothing - every cell of the next grid would be inside a single parent.
                    error = $"Level {i} ({cellSizes[i]:0.##}) must be smaller than level {i - 1} " +
                            $"({cellSizes[i - 1]:0.##}), otherwise it decides nothing.";
                    return null;
                }
            }

            var levels = new List<PCGPartitionLevel>(cellSizes.Length);
            var parents = new List<int[]>(cellSizes.Length);

            for (int i = 0; i < cellSizes.Length; i++)
            {
                var cells = PCGPartitionPlanner.Plan(region, cellSizes[i], padding, out string planError);

                if (cells == null)
                {
                    // Named by level so a designer knows which grid to change: the finest level is the one that can
                    // blow the cell ceiling, and the coarsest is the one that can produce no cells at all.
                    error = i == cellSizes.Length - 1
                        ? "The generation level could not be planned: " + planError
                        : $"Level {i} could not be planned: {planError}";
                    return null;
                }

                levels.Add(new PCGPartitionLevel(cellSizes[i], cells));

                if (i == 0)
                {
                    parents.Add(Array.Empty<int>());
                    continue;
                }

                var links = new int[cells.Count];

                for (int c = 0; c < cells.Count; c++)
                    links[c] = FindParent(levels[i - 1].Cells, cells[c]);

                parents.Add(links);
            }

            return new PCGPartitionHierarchy(levels, parents);
        }

        /// <summary>
        /// Cell sizes for <paramref name="levels"/> levels from a coarsest and a finest one, geometric in between.
        /// </summary>
        /// <remarks>
        /// Two knobs and a count, rather than one knob per level: the two sizes are already what a designer sets
        /// (the volume's coarse size and its cell size), and interpolating between them keeps the work per level
        /// roughly constant - each step shrinks the cells by the same factor, so each level subdivides its parent
        /// into the same number of children. With two levels this returns exactly the two sizes given.
        /// </remarks>
        /// <returns>The sizes, coarsest first; empty when the arguments cannot describe a hierarchy.</returns>
        public static float[] GeometricSizes(float coarseSize, float fineSize, int levels)
        {
            if (levels < 2 || levels > MaxLevels || coarseSize <= 0.0f || fineSize <= 0.0f || fineSize >= coarseSize)
                return Array.Empty<float>();

            var sizes = new float[levels];
            float ratio = fineSize / coarseSize;

            for (int i = 0; i < levels; i++)
                sizes[i] = (float)(coarseSize * Math.Pow(ratio, (double)i / (levels - 1)));

            // The last one is pinned rather than computed: repeated multiplication by a non-exact ratio must not be
            // what decides the finest grid, because the generation level's size is what the designer typed.
            sizes[levels - 1] = fineSize;

            return sizes;
        }

        /// <summary>
        /// The coarser cell a finer cell belongs to: the one containing its centre.
        /// </summary>
        /// <remarks>
        /// The centre rather than an edge, because a finer cell can straddle a coarser boundary - nothing requires
        /// the two sizes to divide evenly, and 800 over 300 does not. Choosing by the centre makes every cell belong
        /// to exactly one parent, so a live parent brings in all the children that are mostly its own, and the
        /// split is stable for a given pair of sizes.
        /// </remarks>
        private static int FindParent(List<PCGPartitionCell> coarse, in PCGPartitionCell fine)
        {
            var minimum = fine.ClipBounds.Minimum;
            var maximum = fine.ClipBounds.Maximum;

            var centre = new Vector3((minimum.X + maximum.X) * 0.5f, 0.0f, (minimum.Z + maximum.Z) * 0.5f);

            for (int i = 0; i < coarse.Count; i++)
            {
                var candidate = coarse[i];

                if (PCGPartitionPlanner.IsInsideCell(in candidate, centre))
                    return i;
            }

            // A cell whose centre is on the region's far edge with a half-open rule can fall outside every
            // candidate; the last coarse cell is the answer that keeps the plan total rather than dropping cells.
            return coarse.Count - 1;
        }
    }
}

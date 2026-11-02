// PCG Plugin - Viewer reduction
// Part of the FlaxEngine PCG plugin. See docs/PCG_PLUGIN_DESIGN.md §7.3.

using System;
using System.Collections.Generic;
using FlaxEngine;

namespace PCG.Execution
{
    /// <summary>
    /// Trims a set of viewers down to the few the window can afford, keeping the ones that spread over it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// More viewers means a bigger union, and the union is what gets generated: forty players - or forty loaded
    /// chunks - would ask for the whole map at once, which is exactly what streaming exists to avoid. A cap is
    /// therefore not a nicety but the difference between a bounded request and an unbounded one.
    /// </para>
    /// <para>
    /// Which viewers survive matters more than how many: taking the first N in list order is the cheap answer and
    /// gives the worst coverage, because a crowd standing together fills the budget on one neighbourhood. The
    /// rule here is greedy farthest-point sampling - start from the viewer nearest the set's centroid, then
    /// repeatedly take the one furthest from everything already chosen - which spreads the survivors over the
    /// same ground the full set covered. It is O(n·k), deterministic, and needs no clustering parameters.
    /// </para>
    /// </remarks>
    public static class PCGStreamViewers
    {
        /// <summary>Default number of viewers a request is cut around.</summary>
        public const int DefaultMaxViewers = 8;

        /// <summary>
        /// Asks a host-provided source for the viewers, then caps them.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is where the world gets to say who is watching: a game can hand over the positions of its players,
        /// its loaded chunks or its visible blocks, and the plugin never needs to know which of those it was. The
        /// source writes into a caller-owned list and returns how many entries it wrote, so a per-frame call
        /// allocates nothing.
        /// </para>
        /// <para>
        /// A source that claims more entries than it wrote is capped at what the list actually holds - trusting
        /// the return value would read whatever the list had left over from the previous frame, which is a bug
        /// that looks like props appearing around a player who left.
        /// </para>
        /// </remarks>
        /// <param name="source">Fills <paramref name="scratch"/> and returns how many entries it wrote.</param>
        /// <param name="maxViewers">Cap applied to what the source returned; zero or less means no cap.</param>
        /// <param name="scratch">Working list the source writes into.</param>
        /// <param name="into">Receives the capped viewers.</param>
        /// <returns>How many viewers were written to <paramref name="into"/>.</returns>
        public static int FromSource(
            Func<List<Vector3>, int> source, int maxViewers, List<Vector3> scratch, List<Vector3> into)
        {
            if (scratch == null)
                throw new ArgumentNullException(nameof(scratch));
            if (into == null)
                throw new ArgumentNullException(nameof(into));

            if (source == null)
            {
                into.Clear();
                return 0;
            }

            scratch.Clear();
            int claimed = source(scratch);

            // Clamped from below as well as above: a source in the middle of being written can plausibly return
            // -1 or a stale count, and neither should turn into "generate nothing" or "read past the end".
            int count = Math.Min(Math.Max(claimed, 0), scratch.Count);

            // Trimmed in place so the caller's scratch list describes what the source really produced - the
            // volume logs "N of M viewers" straight off it.
            if (count < scratch.Count)
                scratch.RemoveRange(count, scratch.Count - count);

            return Reduce(scratch, maxViewers, into);
        }

        /// <summary>
        /// Writes at most <paramref name="maxViewers"/> of <paramref name="viewers"/> into <paramref name="into"/>.
        /// </summary>
        /// <remarks>
        /// The output order is the sampling order, not the input order, so the result is reproducible but not a
        /// subsequence to compare positionally - the harness checks the set and the coverage, which is what the
        /// window actually depends on.
        /// </remarks>
        /// <returns>How many viewers were written.</returns>
        public static int Reduce(IReadOnlyList<Vector3> viewers, int maxViewers, List<Vector3> into)
        {
            if (viewers == null)
                throw new ArgumentNullException(nameof(viewers));
            if (into == null)
                throw new ArgumentNullException(nameof(into));

            into.Clear();

            if (viewers.Count == 0)
                return 0;

            // A cap of zero or less means "no cap", not "no viewers": the two readings differ by an empty world,
            // and a designer typing 0 means "don't limit this" far more often than "generate nothing".
            if (maxViewers <= 0 || viewers.Count <= maxViewers)
            {
                for (int i = 0; i < viewers.Count; i++)
                    into.Add(viewers[i]);

                return into.Count;
            }

            // The centroid only picks the first viewer, and it is the one place where the input order could leak
            // in: a tie there is broken by index so two runs of the same world agree.
            var centroid = Vector3.Zero;

            for (int i = 0; i < viewers.Count; i++)
                centroid += viewers[i];

            centroid /= viewers.Count;

            int first = 0;
            float nearestToCentre = float.MaxValue;

            for (int i = 0; i < viewers.Count; i++)
            {
                float distance = (viewers[i] - centroid).LengthSquared;

                if (distance < nearestToCentre)
                {
                    nearestToCentre = distance;
                    first = i;
                }
            }

            into.Add(viewers[first]);

            // Farthest-point sampling: each new viewer is the one furthest from every viewer already chosen.
            var bestDistance = new float[viewers.Count];

            for (int i = 0; i < viewers.Count; i++)
                bestDistance[i] = (viewers[i] - viewers[first]).LengthSquared;

            while (into.Count < maxViewers)
            {
                int next = -1;
                float farthest = -1.0f;

                for (int i = 0; i < viewers.Count; i++)
                {
                    if (bestDistance[i] < 0.0f)
                        continue;

                    if (bestDistance[i] > farthest)
                    {
                        farthest = bestDistance[i];
                        next = i;
                    }
                }

                if (next < 0)
                    break;

                into.Add(viewers[next]);
                bestDistance[next] = -1.0f;

                for (int i = 0; i < viewers.Count; i++)
                {
                    if (bestDistance[i] < 0.0f)
                        continue;

                    float distance = (viewers[i] - viewers[next]).LengthSquared;

                    if (distance < bestDistance[i])
                        bestDistance[i] = distance;
                }
            }

            return into.Count;
        }
    }
}

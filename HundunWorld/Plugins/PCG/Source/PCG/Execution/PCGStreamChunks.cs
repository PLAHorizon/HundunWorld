// PCG Plugin - Visible chunk source
// Part of the FlaxEngine PCG plugin. See docs/PCG_PLUGIN_DESIGN.md §7.3.

using System;
using System.Collections.Generic;
using FlaxEngine;

namespace PCG.Execution
{
    /// <summary>
    /// Reads the world's list of visible chunks, clamped to what the source actually wrote.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The counterpart of <see cref="PCGStreamViewers.FromSource"/> for the chunk-driven window: a world that
    /// streams by chunk knows which chunks it has loaded, so it hands them over instead of the plugin inferring a
    /// window from player positions and disagreeing with it.
    /// </para>
    /// <para>
    /// The clamping is the whole reason this is a function rather than two lines at the call site. The list is
    /// reused between requests, so a source that claims seven entries after writing two would replay the previous
    /// request's chunks as though they were visible now - content generated for ground nobody is looking at, with
    /// nothing in the log to explain it. Living here, the rule is asserted offline; at the call site it could only
    /// be believed.
    /// </para>
    /// </remarks>
    public static class PCGStreamChunks
    {
        /// <summary>
        /// Asks a host-provided source for the visible chunks, clamped to what it wrote.
        /// </summary>
        /// <param name="source">Fills <paramref name="scratch"/> and returns how many entries it wrote.</param>
        /// <param name="scratch">Working list the source writes into; trimmed to what it really produced.</param>
        /// <param name="into">Receives the chunk list; cleared first.</param>
        /// <returns>How many chunks were written to <paramref name="into"/>.</returns>
        public static int FromSource(Func<List<Int2>, int> source, List<Int2> scratch, List<Int2> into)
        {
            if (scratch == null)
                throw new ArgumentNullException(nameof(scratch));
            if (into == null)
                throw new ArgumentNullException(nameof(into));

            into.Clear();
            scratch.Clear();

            if (source == null)
                return 0;

            int claimed = source(scratch);

            // Clamped from below as well as above, for the same reason the viewer source is: a list still being
            // written can plausibly report -1 or a stale count, and neither should mean "everything" or "nothing".
            int count = Math.Min(Math.Max(claimed, 0), scratch.Count);

            // Trimmed in place, so the caller's scratch list describes what the source really produced - the
            // volume's own log line reads straight off it.
            if (count < scratch.Count)
                scratch.RemoveRange(count, scratch.Count - count);

            for (int i = 0; i < scratch.Count; i++)
                into.Add(scratch[i]);

            return into.Count;
        }
    }
}

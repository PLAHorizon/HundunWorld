// PCG Plugin - per-node timing report
// Part of the FlaxEngine PCG plugin. See docs/PCG_PLUGIN_DESIGN.md §9 Phase 3 (performance budget).

using System;
using System.Collections.Generic;

namespace PCG.Execution
{
    /// <summary>
    /// Turns the executor's per-node statistics into one line a designer can act on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The executor already measured per-node cost, but nothing downstream read it, so the numbers existed only
    /// for a profiler to miss. This is that consumer: <c>PCGVolume</c> logs it when <c>Log Node Timings</c> is
    /// set, which is the difference between "generation took 2.4 seconds" and "the self-pruning filter was 2.1
    /// of them".
    /// </para>
    /// <para>
    /// Grouping is by type id rather than by node index because a partitioned run reports one row set per cell:
    /// the same node appears as many times as there were cells, and a per-index view of that is a list of
    /// duplicates rather than a cost breakdown.
    /// </para>
    /// </remarks>
    public static class PCGNodeTimingReport
    {
        /// <summary>Rows kept in the printed summary; slower rows first.</summary>
        public const int TopRows = 6;

        /// <summary>One row of the report: a node type's share of the run.</summary>
        public readonly struct Row
        {
            public readonly string TypeId;
            public readonly double Milliseconds;
            public readonly int PointCount;

            /// <summary>How many times this type ran (one per cell in a partitioned run).</summary>
            public readonly int Runs;

            public Row(string typeId, double milliseconds, int pointCount, int runs)
            {
                TypeId = typeId;
                Milliseconds = milliseconds;
                PointCount = pointCount;
                Runs = runs;
            }
        }

        /// <summary>
        /// Sums the statistics per node type, slowest first.
        /// </summary>
        public static List<Row> Summarize(PCGNodeRunStats[] stats)
        {
            var rows = new List<Row>();
            if (stats == null || stats.Length == 0)
                return rows;

            var byType = new Dictionary<string, int>(StringComparer.Ordinal);
            var order = new List<string>();

            for (int i = 0; i < stats.Length; i++)
            {
                var stat = stats[i];
                string key = stat.TypeId ?? "(unnamed)";

                if (!byType.TryGetValue(key, out int index))
                {
                    index = rows.Count;
                    byType[key] = index;
                    order.Add(key);
                    rows.Add(new Row(key, 0.0, 0, 0));
                }

                var row = rows[index];
                rows[index] = new Row(
                    row.TypeId,
                    row.Milliseconds + stat.Milliseconds,
                    row.PointCount + stat.PointCount,
                    row.Runs + 1);
            }

            rows.Sort((a, b) =>
            {
                int byTime = b.Milliseconds.CompareTo(a.Milliseconds);
                return byTime != 0 ? byTime : string.CompareOrdinal(a.TypeId, b.TypeId);
            });

            return rows;
        }

        /// <summary>
        /// Formats the report as one line, naming the share of the whole run each row took.
        /// </summary>
        /// <param name="stats">Raw per-node statistics, from one run or from a partitioned one.</param>
        /// <param name="runMilliseconds">Total run time, used for the percentage.</param>
        public static string Format(PCGNodeRunStats[] stats, double runMilliseconds)
        {
            var rows = Summarize(stats);
            if (rows.Count == 0)
                return "no node timings";

            int shown = Math.Min(TopRows, rows.Count);
            var parts = new List<string>(shown);

            for (int i = 0; i < shown; i++)
            {
                var row = rows[i];
                double share = runMilliseconds > 0.0 ? row.Milliseconds / runMilliseconds * 100.0 : 0.0;

                parts.Add($"{row.TypeId} {row.Milliseconds:F1} ms ({share:F0}%, {row.PointCount} pt" +
                          (row.Runs > 1 ? $", {row.Runs} runs" : string.Empty) + ")");
            }

            if (rows.Count > shown)
                parts.Add($"+{rows.Count - shown} more");

            return string.Join(", ", parts);
        }
    }
}

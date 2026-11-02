// PCG Plugin - Executable tests for the per-node timing report.
//
// The report is the only thing that makes the executor's measurements visible to a designer, and its two jobs
// are arithmetic: fold the repeated rows of one node type together, and order the result by cost. Both are pure
// data, so they are checked here rather than only by whoever happens to read a log line.

using System;
using PCG.Execution;

using CheckDelegate = PCGGoldenRunner.NodeBehaviourTests.CheckDelegate;

namespace PCGGoldenRunner
{
    internal static class TimingReportTests
    {
        private static CheckDelegate _check;

        // Condition first, so an assertion reads as "this holds, because ...".
        private static void check(bool ok, string what, string detail = null) => _check(what, ok, detail);

        private static PCGNodeRunStats Stat(int index, string typeId, int points, double ms) =>
            new PCGNodeRunStats { NodeIndex = index, TypeId = typeId, PointCount = points, Milliseconds = ms };

        internal static void Run(CheckDelegate callback)
        {
            _check = callback;

            var noRows = PCGNodeTimingReport.Summarize(null);
            var noRowsAgain = PCGNodeTimingReport.Summarize(Array.Empty<PCGNodeRunStats>());
            check(noRows.Count == 0 && noRowsAgain.Count == 0, "no stats yields no rows");

            string emptyReport = PCGNodeTimingReport.Format(null, 0.0);
            check(emptyReport.Contains("no node timings"),
                "no stats still says something a designer can read", emptyReport);

            // Two cells of the same graph report one row per node type per cell; the report folds them, because
            // a list naming the same node eight times is not a cost breakdown.
            var twoCells = new[]
            {
                Stat(0, "VolumeSampler", 100, 10.0),
                Stat(1, "SelfPruningFilter", 60, 30.0),
                Stat(0, "VolumeSampler", 120, 20.0),
                Stat(1, "SelfPruningFilter", 70, 50.0),
            };

            var rows = PCGNodeTimingReport.Summarize(twoCells);
            check(rows.Count == 2, "a partitioned run folds each node's per-cell rows into one",
                $"rows={rows.Count}");

            var pruner = rows[0];
            check(pruner.TypeId == "SelfPruningFilter", "the slowest type leads the report", pruner.TypeId);
            check(Math.Abs(pruner.Milliseconds - 80.0) < 0.001 && pruner.PointCount == 130 && pruner.Runs == 2,
                "a folded row sums time, points and run count",
                $"{pruner.Milliseconds} ms / {pruner.PointCount} pt / {pruner.Runs} runs");

            string folded = PCGNodeTimingReport.Format(twoCells, 100.0);
            check(folded.Contains("SelfPruningFilter 80.0 ms (80%, 130 pt, 2 runs)"),
                "the formatted line names the share of the run and the fold", folded);

            // Equal costs must not depend on insertion order, or the same graph reads differently between runs.
            var tied = new[] { Stat(0, "Zeta", 1, 5.0), Stat(1, "Alpha", 1, 5.0) };
            var tieRows = PCGNodeTimingReport.Summarize(tied);
            check(tieRows[0].TypeId == "Alpha" && tieRows[1].TypeId == "Zeta",
                "equal cost breaks the tie by type name so two runs of the same data read alike",
                $"{tieRows[0].TypeId},{tieRows[1].TypeId}");

            var many = new PCGNodeRunStats[8];
            for (int i = 0; i < many.Length; i++)
                many[i] = Stat(i, "Type" + i, i + 1, 100.0 - i);

            string longReport = PCGNodeTimingReport.Format(many, 1000.0);
            check(longReport.Contains($"+{8 - PCGNodeTimingReport.TopRows} more"),
                "a wide graph is truncated rather than flooding the log", longReport);
            check(CountOccurrences(longReport, " ms (") == PCGNodeTimingReport.TopRows,
                $"the report shows at most {PCGNodeTimingReport.TopRows} node types",
                $"entries={CountOccurrences(longReport, " ms (")}");

            string unnamed = PCGNodeTimingReport.Format(new[] { Stat(0, null, 3, 7.0) }, 100.0);
            check(unnamed.Contains("(unnamed)"),
                "a node with no type id is reported rather than dropped", unnamed);

            string zeroLength = PCGNodeTimingReport.Format(new[] { Stat(0, "Sampler", 1, 0.0) }, 0.0);
            check(zeroLength.Contains("Sampler") && zeroLength.Contains("(0%"),
                "a run measured as zero length still reports its nodes without dividing by zero", zeroLength);
        }

        private static int CountOccurrences(string text, string needle)
        {
            int count = 0;
            int at = 0;
            while ((at = text.IndexOf(needle, at, StringComparison.Ordinal)) >= 0)
            {
                count++;
                at += needle.Length;
            }

            return count;
        }
    }
}

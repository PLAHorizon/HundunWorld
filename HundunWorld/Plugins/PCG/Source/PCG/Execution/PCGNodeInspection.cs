// PCG Plugin - Per-node inspection
// Part of the FlaxEngine PCG plugin. See docs/PCG_PLUGIN_DESIGN.md §6.5.

using System;
using System.Collections.Generic;
using System.Text;
using FlaxEngine;

namespace PCG.Execution
{
    /// <summary>
    /// Records what each node produced during a run, so a designer can see the output of one node instead of
    /// guessing from the end of the graph.
    /// </summary>
    /// <remarks>
    /// <para>
    /// UE5 lets you inspect any node's data; this is the plugin's data-side answer to it. A graph is a chain of
    /// transformations and the interesting question is almost always "what did *this* node hand on?" - a pruner
    /// that removed nine tenths of a layer, a filter that matched nothing, a sampler that produced far more than
    /// expected. Answering that from the final output is guesswork.
    /// </para>
    /// <para>
    /// Attaching one of these to a run costs a point count per node and a bounded sample of positions; nothing is
    /// copied wholesale, so a layer of a hundred thousand points is described by its size and a handful of
    /// samples rather than by the whole array. A run without one behaves exactly as before - the recorder is
    /// consulted through a null check on the hot path.
    /// </para>
    /// </remarks>
    public sealed class PCGNodeInspection
    {
        /// <summary>How many sample positions are kept per node.</summary>
        public const int SampleLimit = 8;

        /// <summary>How many nodes a whole-graph draw shows at most, before the picture stops being readable.</summary>
        public const int DrawNodeLimit = 32;

        /// <summary>
        /// Colours handed out to nodes, in order. Distinct by construction rather than by hashing a hue, so two
        /// neighbouring nodes can never come out looking alike.
        /// </summary>
        private static readonly Color[] Palette =
        {
            new Color(0.20f, 1.00f, 0.40f, 1.0f),
            new Color(1.00f, 0.70f, 0.20f, 1.0f),
            new Color(0.35f, 0.70f, 1.00f, 1.0f),
            new Color(1.00f, 0.35f, 0.55f, 1.0f),
            new Color(0.75f, 0.55f, 1.00f, 1.0f),
            new Color(0.45f, 1.00f, 0.90f, 1.0f),
            new Color(1.00f, 0.95f, 0.35f, 1.0f),
            new Color(0.60f, 0.85f, 0.30f, 1.0f),
        };

        /// <summary>
        /// The colour a node is drawn in, from its index alone - so the same node keeps the same colour across
        /// runs and a designer can talk about "the orange one" without everyone having to re-find it.
        /// </summary>
        public static Color ColorFor(int nodeIndex)
        {
            int slot = nodeIndex % Palette.Length;

            if (slot < 0)
                slot += Palette.Length;

            return Palette[slot];
        }

        /// <summary>One node's samples, with the colour the viewport should draw them in.</summary>
        public readonly struct NodeDraw
        {
            /// <summary>Index of the node in the compiled graph.</summary>
            public readonly int NodeIndex;

            /// <summary>Node type id.</summary>
            public readonly string TypeId;

            /// <summary>Positions to mark, at most the recorder's own per-node limit.</summary>
            public readonly Vector3[] Samples;

            /// <summary>Colour for this node.</summary>
            public readonly Color Color;

            internal NodeDraw(int nodeIndex, string typeId, Vector3[] samples, Color color)
            {
                NodeIndex = nodeIndex;
                TypeId = typeId;
                Samples = samples;
                Color = color;
            }

            /// <inheritdoc />
            public override string ToString() => $"[{NodeIndex}] {TypeId}: {Samples.Length} sample(s)";
        }

        /// <summary>
        /// Fills <paramref name="into"/> with one entry per node that produced points, in execution order, so the
        /// whole graph can be shown at once instead of one node at a time.
        /// </summary>
        /// <remarks>
        /// A node is listed once however many times it ran: a partitioned run records it per cell, and forty
        /// entries for one node would draw forty overlapping clusters rather than one shape. Nodes that produced
        /// nothing are skipped - there is nothing to mark - which is why <see cref="FormatSummaries"/> exists for
        /// the "it ran and produced nothing" answer.
        /// </remarks>
        /// <param name="into">Receives the draws; cleared first.</param>
        /// <param name="maxNodes">Ceiling on entries; zero or negative means no ceiling.</param>
        /// <returns>How many entries were added.</returns>
        public int CollectDraws(List<NodeDraw> into, int maxNodes = DrawNodeLimit)
        {
            if (into == null)
                throw new ArgumentNullException(nameof(into));

            into.Clear();

            int limit = maxNodes > 0 ? maxNodes : int.MaxValue;
            var seen = new HashSet<int>();

            for (int i = 0; i < _records.Count && into.Count < limit; i++)
            {
                int node = _records[i].NodeIndex;

                if (!seen.Add(node))
                    continue;

                var summary = Summarize(node);
                if (summary.Samples.Length == 0)
                    continue;

                into.Add(new NodeDraw(node, _records[i].TypeId, summary.Samples, ColorFor(node)));
            }

            return into.Count;
        }

        /// <summary>
        /// One line per node, folded across its runs - the whole graph's shape in a form that fits in a status
        /// line, rather than one line per record.
        /// </summary>
        public string FormatSummaries()
        {
            if (_records.Count == 0)
                return "no nodes were recorded";

            var builder = new StringBuilder();
            var seen = new HashSet<int>();

            for (int i = 0; i < _records.Count; i++)
            {
                int node = _records[i].NodeIndex;

                if (!seen.Add(node))
                    continue;

                builder.Append('\n').Append('[').Append(node).Append("] ").Append(_records[i].TypeId)
                       .Append(": ").Append(Summarize(node));
            }

            return builder.ToString();
        }

        /// <summary>One node's recorded output.</summary>
        public readonly struct NodeRecord
        {
            /// <summary>Index of the node in the compiled graph.</summary>
            public readonly int NodeIndex;

            /// <summary>Node type id.</summary>
            public readonly string TypeId;

            /// <summary>Points the node produced.</summary>
            public readonly int PointCount;

            /// <summary>Curves the node carried, which a points-only view would miss.</summary>
            public readonly int SplineCount;

            /// <summary>Attribute names the data set offered, sorted.</summary>
            public readonly string AttributeNames;

            /// <summary>First few positions, in output order.</summary>
            public readonly Vector3[] Samples;

            internal NodeRecord(
                int nodeIndex, string typeId, int pointCount, int splineCount, string attributeNames, Vector3[] samples)
            {
                NodeIndex = nodeIndex;
                TypeId = typeId;
                PointCount = pointCount;
                SplineCount = splineCount;
                AttributeNames = attributeNames;
                Samples = samples;
            }

            /// <inheritdoc />
            public override string ToString() =>
                $"[{NodeIndex}] {TypeId}: {PointCount} point(s)" +
                (SplineCount > 0 ? $", {SplineCount} curve(s)" : string.Empty) +
                (string.IsNullOrEmpty(AttributeNames) ? string.Empty : $", columns: {AttributeNames}");
        }

        private readonly List<NodeRecord> _records = new List<NodeRecord>();
        private readonly int _sampleLimit;

        /// <summary>Creates a recorder keeping at most <paramref name="sampleLimit"/> positions per node.</summary>
        public PCGNodeInspection(int sampleLimit = SampleLimit)
        {
            _sampleLimit = Math.Max(0, sampleLimit);
        }

        /// <summary>Records, in the order the nodes ran.</summary>
        public IReadOnlyList<NodeRecord> Records => _records;

        /// <summary>Drops everything recorded so far, for the next run.</summary>
        public void Clear() => _records.Clear();

        /// <summary>
        /// Records one node's output.
        /// </summary>
        /// <remarks>
        /// Called by the executor after a node evaluates. Null-tolerant on the data set so a disabled or empty
        /// node is still listed - "this node produced nothing" is one of the answers a designer is looking for.
        /// </remarks>
        public void Record(int nodeIndex, string typeId, PCGData output)
        {
            int count = output?.Count ?? 0;
            int splines = output?.Splines?.Length ?? 0;
            var samples = new Vector3[Math.Min(_sampleLimit, count)];

            for (int i = 0; i < samples.Length; i++)
                samples[i] = output.Points[i].Position;

            _records.Add(new NodeRecord(nodeIndex, typeId, count, splines, ColumnNames(output), samples));
        }

        /// <summary>One line per node, in execution order.</summary>
        public string Format()
        {
            if (_records.Count == 0)
                return "no nodes were recorded";

            var builder = new StringBuilder();

            for (int i = 0; i < _records.Count; i++)
                builder.Append('\n').Append(_records[i]);

            return builder.ToString();
        }

        /// <summary>
        /// What one node produced across the whole run, which is more than one record whenever the run was
        /// partitioned.
        /// </summary>
        public readonly struct NodeSummary
        {
            /// <summary>False when the node never ran, which is itself worth saying: a disabled node and an empty
            /// node look the same in the graph.</summary>
            public readonly bool Ran;

            /// <summary>How many times the node ran - one per cell in a partitioned run.</summary>
            public readonly int Runs;

            /// <summary>Points across every run.</summary>
            public readonly int PointCount;

            /// <summary>Curves the node handed on, from the last run.</summary>
            public readonly int SplineCount;

            /// <summary>Columns the node published, from the first run that had any.</summary>
            public readonly string AttributeNames;

            /// <summary>A few positions, taken in run order, for a caller that wants to show or draw them.</summary>
            public readonly Vector3[] Samples;

            internal NodeSummary(int runs, int pointCount, int splineCount, string attributeNames, Vector3[] samples)
            {
                Ran = runs > 0;
                Runs = runs;
                PointCount = pointCount;
                SplineCount = splineCount;
                AttributeNames = attributeNames;
                Samples = samples;
            }

            /// <inheritdoc />
            public override string ToString() =>
                Ran
                    ? $"{Runs} run(s), {PointCount} point(s)" +
                      (SplineCount > 0 ? $", {SplineCount} curve(s)" : string.Empty) +
                      (string.IsNullOrEmpty(AttributeNames) ? string.Empty : $" - columns: {AttributeNames}")
                    : "did not run";
        }

        /// <summary>
        /// Folds every record of one node into a summary, in the order the runs happened.
        /// </summary>
        /// <remarks>
        /// The folding is what makes this usable in a partitioned world: the node a designer selected ran once per
        /// cell, and four hundred records of ten points each is not an answer - "forty runs, four thousand points,
        /// here are the first few" is.
        /// </remarks>
        public NodeSummary Summarize(int nodeIndex)
        {
            int runs = 0;
            int points = 0;
            int splines = 0;
            string columns = string.Empty;
            var samples = new List<Vector3>();

            for (int i = 0; i < _records.Count; i++)
            {
                var record = _records[i];

                if (record.NodeIndex != nodeIndex)
                    continue;

                runs++;
                points += record.PointCount;
                splines = record.SplineCount;

                if (string.IsNullOrEmpty(columns) && !string.IsNullOrEmpty(record.AttributeNames))
                    columns = record.AttributeNames;

                for (int s = 0; s < record.Samples.Length && samples.Count < _sampleLimit; s++)
                    samples.Add(record.Samples[s]);
            }

            return new NodeSummary(runs, points, splines, columns, samples.ToArray());
        }

        private static string ColumnNames(PCGData data)
        {
            var set = data?.SharedAttributes;

            if (set == null)
                return string.Empty;

            var names = new List<string>();

            foreach (var descriptor in set.GetDescriptors())
                names.Add(descriptor.Name);

            names.Sort(StringComparer.Ordinal);

            return string.Join(",", names);
        }
    }
}

// PCG Plugin - Graph executor (pull-based)
// Part of the FlaxEngine PCG plugin. See docs/PCG_PLUGIN_DESIGN.md §4.1 and §4.4.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using FlaxEngine;
using PCG.Graph;

namespace PCG.Execution
{
    /// <summary>Per-node statistics gathered during a run.</summary>
    public struct PCGNodeRunStats
    {
        /// <summary>Index of the node within the graph.</summary>
        public int NodeIndex;

        /// <summary>Node type id.</summary>
        public string TypeId;

        /// <summary>Point count produced by this node.</summary>
        public int PointCount;

        /// <summary>Wall-clock milliseconds spent evaluating this node.</summary>
        public double Milliseconds;
    }

    /// <summary>Result of one graph execution.</summary>
    public sealed class PCGExecutionResult
    {
        /// <summary>Output data per sink node, in <see cref="PCGCompiledGraph.SinkNodes"/> order.</summary>
        public PCGData[] Outputs;

        /// <summary>Sink node indices matching <see cref="Outputs"/>.</summary>
        public int[] OutputNodeIndices;

        /// <summary>Total points produced across all nodes, summed over evaluations.</summary>
        public int TotalPointsProduced;

        /// <summary>Wall-clock duration of the run.</summary>
        public double Milliseconds;

        /// <summary>True when the run was cancelled.</summary>
        public bool Cancelled;

        /// <summary>Per-node statistics in evaluation order.</summary>
        public PCGNodeRunStats[] Stats;

        /// <summary>Non-fatal messages raised during the run.</summary>
        public string[] Warnings;

        /// <summary>Points produced by the first sink, for convenience.</summary>
        public PCGData PrimaryOutput => Outputs != null && Outputs.Length > 0 ? Outputs[0] : PCGData.Empty;
    }

    /// <summary>
    /// Evaluates a compiled graph by pulling data from its sinks.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Values are cached per node for the duration of a run, so a node feeding two consumers is evaluated
    /// once. Cache entries are dropped when the run ends; nothing is retained between runs, which keeps the
    /// executor free of the cross-run staleness that incremental execution would otherwise have to reason
    /// about.
    /// </para>
    /// <para>
    /// <b>Threading.</b> The executor itself must run on whichever thread owns the scene, because nodes may
    /// create actors. Individual nodes marked <see cref="PCGNodeBase.IsThreadSafe"/> may parallelise their
    /// own inner loops; they must not touch scene state while doing so. In particular, actor creation,
    /// foliage writes and debug drawing are main-thread only, so a background run has to hand its
    /// <see cref="PCGData"/> back to the main thread rather than applying it directly.
    /// </para>
    /// </remarks>
    public sealed class PCGGraphExecutor
    {
        private readonly PCGCompiledGraph _graph;
        private readonly PCGData[] _cache;
        private readonly bool[] _evaluated;
        private readonly List<int> _evaluationOrder = new List<int>();
        private readonly List<string> _warnings = new List<string>();
        private readonly Dictionary<int, double> _timings = new Dictionary<int, double>();

        private int _pointsProduced;
        private int _pointBudget;

        /// <summary>Creates an executor for a compiled graph.</summary>
        public PCGGraphExecutor(PCGCompiledGraph graph)
        {
            _graph = graph ?? throw new ArgumentNullException(nameof(graph));
            _cache = new PCGData[_graph.NodeCount];
            _evaluated = new bool[_graph.NodeCount];
        }

        /// <summary>
        /// Runs the graph.
        /// </summary>
        /// <param name="context">Execution context. Its <c>Seed</c> and <c>GenerationBounds</c> drive generation.</param>
        /// <returns>The run result. Check <see cref="PCGExecutionResult.Cancelled"/> before using outputs.</returns>
        public PCGExecutionResult Execute(PCGExecutionContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            var result = new PCGExecutionResult();
            var stopwatch = Stopwatch.StartNew();

            _pointBudget = _graph.Source?.Settings?.MaxPointBudget ?? 2000000;
            _pointsProduced = 0;
            _evaluationOrder.Clear();
            _warnings.Clear();
            _timings.Clear();
            Array.Clear(_cache, 0, _cache.Length);
            Array.Clear(_evaluated, 0, _evaluated.Length);

            // Reset the node id used for seeding so every run reproduces the same values.
            int previousNodeId = context.CurrentNodeId;

            var sinks = _graph.SinkNodes ?? Array.Empty<int>();
            var outputs = new PCGData[sinks.Length];

            try
            {
                for (int i = 0; i < sinks.Length; i++)
                {
                    context.ThrowIfCancelled(i, 1);
                    outputs[i] = Evaluate(sinks[i], context);
                }
            }
            catch (OperationCanceledException)
            {
                result.Cancelled = true;
                context.CurrentNodeId = previousNodeId;
                stopwatch.Stop();
                result.Milliseconds = stopwatch.Elapsed.TotalMilliseconds;
                result.Warnings = _warnings.ToArray();
                result.Outputs = Array.Empty<PCGData>();
                result.OutputNodeIndices = Array.Empty<int>();
                return result;
            }
            finally
            {
                context.CurrentNodeId = previousNodeId;
            }

            stopwatch.Stop();

            result.Outputs = outputs;
            result.OutputNodeIndices = sinks;
            result.TotalPointsProduced = _pointsProduced;
            result.Milliseconds = stopwatch.Elapsed.TotalMilliseconds;
            result.Warnings = _warnings.ToArray();
            result.Stats = BuildStats();

            return result;
        }

        private PCGNodeRunStats[] BuildStats()
        {
            var stats = new PCGNodeRunStats[_evaluationOrder.Count];
            for (int i = 0; i < _evaluationOrder.Count; i++)
            {
                int nodeIndex = _evaluationOrder[i];
                var node = _graph.Nodes[nodeIndex];
                stats[i] = new PCGNodeRunStats
                {
                    NodeIndex = nodeIndex,
                    TypeId = node?.TypeId,
                    PointCount = _cache[nodeIndex]?.Count ?? 0,
                    Milliseconds = _timings.TryGetValue(nodeIndex, out var ms) ? ms : 0.0,
                };
            }

            return stats;
        }

        /// <summary>
        /// Evaluates a node, pulling its inputs first. The result is cached for the rest of the run.
        /// </summary>
        private PCGData Evaluate(int nodeIndex, PCGExecutionContext context)
        {
            if (nodeIndex < 0 || nodeIndex >= _graph.NodeCount)
                return PCGData.Empty;

            if (_evaluated[nodeIndex])
                return _cache[nodeIndex];

            var node = _graph.Nodes[nodeIndex];
            if (node == null)
            {
                // Compilation guarantees non-null here; treat as empty rather than throwing mid-run.
                _evaluated[nodeIndex] = true;
                _cache[nodeIndex] = PCGData.Empty;
                return _cache[nodeIndex];
            }

            // Disabled nodes are pass-throughs: the first input wins, otherwise the node runs anyway so a
            // disabled sampler still yields an (empty) data set rather than breaking the graph.
            var start = Stopwatch.GetTimestamp();
            var inputs = BuildInputs(nodeIndex, context);

            PCGData output;
            try
            {
                context.CurrentNodeId = nodeIndex;
                output = node.Execute(context, inputs);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (PCGNodeExecutionException)
            {
                throw;
            }
            catch (PCGAttributeException ex)
            {
                // Attribute misuse is a node bug; surface it with the node identity attached.
                throw new PCGNodeExecutionException(
                    $"Node '{node.TypeId}' (index {nodeIndex}) misused an attribute: {ex.Message}", ex);
            }
            catch (Exception ex)
            {
                throw new PCGNodeExecutionException(
                    $"Node '{node.TypeId}' (index {nodeIndex}) threw {ex.GetType().Name}: {ex.Message}", ex);
            }

            output ??= PCGData.Empty;

            _pointsProduced += output.Count;
            if (_pointsProduced > _pointBudget)
            {
                throw new PCGNodeExecutionException(
                    $"Graph exceeded its point budget of {_pointBudget} after node '{node.TypeId}' (index {nodeIndex}). " +
                    "Lower the sampler counts or raise PCGGraphSettings.MaxPointBudget if this is intended.");
            }

            _evaluated[nodeIndex] = true;
            _cache[nodeIndex] = output;
            _evaluationOrder.Add(nodeIndex);

            var elapsed = Stopwatch.GetTimestamp() - start;
            _timings[nodeIndex] = elapsed * 1000.0 / Stopwatch.Frequency;

            return output;
        }

        /// <summary>
        /// Builds the input array for a node, in pin declaration order.
        /// </summary>
        /// <remarks>
        /// For a multi-connection pin the entry is a <see cref="PCGData"/> array; for a normal pin it is a
        /// single <see cref="PCGData"/> or null when unconnected. Nodes interpret the two shapes according to
        /// their own <see cref="PCGPinDefinition.AllowMultiple"/> declarations.
        /// </remarks>
        private PCGData[] BuildInputs(int nodeIndex, PCGExecutionContext context)
        {
            var node = _graph.Nodes[nodeIndex];
            var pinDefs = node.InputPins ?? Array.Empty<PCGPinDefinition>();
            var links = _graph.Links[nodeIndex] ?? Array.Empty<PCGInputLink[]>();

            // The array is typed PCGData[] because a single entry may hold either a data set or an array of
            // them, and PCGData is not an array type. Nodes cast the multi-input entries.
            var inputs = new PCGData[pinDefs.Length];

            for (int pin = 0; pin < pinDefs.Length; pin++)
            {
                if (pin >= links.Length)
                {
                    inputs[pin] = null;
                    continue;
                }

                var bucket = links[pin] ?? Array.Empty<PCGInputLink>();

                if (pinDefs[pin].AllowMultiple)
                {
                    // Multi-input pins are evaluated and flattened into an aggregate so the entry stays a
                    // single PCGData; combiners read the parts from PCGData.Parts.
                    var parts = new PCGData[bucket.Length];
                    for (int k = 0; k < bucket.Length; k++)
                    {
                        parts[k] = Evaluate(bucket[k].SourceNodeIndex, context);
                    }

                    inputs[pin] = PCGData.Aggregate(parts);
                    continue;
                }

                inputs[pin] = bucket.Length > 0
                    ? Evaluate(bucket[0].SourceNodeIndex, context)
                    : null;
            }

            return inputs;
        }

        /// <summary>
        /// Evaluates a single node in isolation, evaluating its inputs as needed.
        /// </summary>
        /// <remarks>
        /// Intended for editors that want to preview one node's contribution without running the whole
        /// graph. A fresh executor is used, so no state leaks between calls.
        /// </remarks>
        public static PCGData ExecuteSingleNode(PCGCompiledGraph graph, int nodeIndex, PCGExecutionContext context)
        {
            if (graph == null)
                throw new ArgumentNullException(nameof(graph));

            var executor = new PCGGraphExecutor(graph);
            executor._pointBudget = graph.Source?.Settings?.MaxPointBudget ?? 2000000;
            return executor.Evaluate(nodeIndex, context);
        }

        /// <summary>
        /// Cached result of the node, valid only during a run.
        /// </summary>
        public PCGData GetCachedResult(int nodeIndex)
        {
            if (nodeIndex < 0 || nodeIndex >= _graph.NodeCount || !_evaluated[nodeIndex])
                return null;

            return _cache[nodeIndex];
        }
    }
}

// PCG Plugin - Graph compiler
// Part of the FlaxEngine PCG plugin. See docs/PCG_PLUGIN_DESIGN.md §4.1 note 5 and §4.5.

using System;
using System.Collections.Generic;
using System.Text;
using FlaxEngine;
using PCG.Graph;

namespace PCG.Execution
{
    /// <summary>Severity of a compilation diagnostic.</summary>
    public enum PCGCompileSeverity
    {
        /// <summary>Reported but the graph still runs.</summary>
        Warning = 0,

        /// <summary>The graph cannot run.</summary>
        Error = 1,
    }

    /// <summary>One compilation diagnostic, addressable back to graph elements for editor highlighting.</summary>
    public sealed class PCGCompileDiagnostic
    {
        /// <summary>Severity.</summary>
        public PCGCompileSeverity Severity;

        /// <summary>Message.</summary>
        public string Message;

        /// <summary>Index of the offending node, or -1.</summary>
        public int NodeIndex = -1;

        /// <summary>Index of the offending edge, or -1.</summary>
        public int EdgeIndex = -1;

        /// <inheritdoc />
        public override string ToString() =>
            $"[{Severity}] {(NodeIndex >= 0 ? $"node {NodeIndex}: " : string.Empty)}{Message}";
    }

    /// <summary>Raised when a graph is executed without compiling successfully.</summary>
    public sealed class PCGCompilationException : Exception
    {
        /// <summary>Diagnostics that caused the failure.</summary>
        public IReadOnlyList<PCGCompileDiagnostic> Diagnostics { get; }

        /// <summary>Creates the exception.</summary>
        public PCGCompilationException(IReadOnlyList<PCGCompileDiagnostic> diagnostics)
            : base(Summarize(diagnostics))
        {
            Diagnostics = diagnostics ?? Array.Empty<PCGCompileDiagnostic>();
        }

        private static string Summarize(IReadOnlyList<PCGCompileDiagnostic> diagnostics)
        {
            if (diagnostics == null || diagnostics.Count == 0)
                return "PCG graph compilation failed.";

            var sb = new StringBuilder("PCG graph compilation failed:");
            int shown = 0;
            for (int i = 0; i < diagnostics.Count && shown < 8; i++)
            {
                if (diagnostics[i].Severity != PCGCompileSeverity.Error)
                    continue;

                sb.Append("\n  ").Append(diagnostics[i]);
                shown++;
            }

            return sb.ToString();
        }
    }

    /// <summary>A compiled, executable graph.</summary>
    public sealed class PCGCompiledGraph
    {
        /// <summary>Node instances, indexed by graph node index.</summary>
        public PCGNodeBase[] Nodes;

        /// <summary>
        /// Upstream inputs for each node, indexed by graph node index. Each entry is a per-input-pin array
        /// of <c>(upstreamNodeIndex, upstreamOutputPinIndex)</c> pairs, in edge order.
        /// </summary>
        public PCGInputLink[][][] Links;

        /// <summary>Node indices in dependency order: every node appears after all of its inputs.</summary>
        public int[] ExecutionOrder;

        /// <summary>Nodes that feed no consumer, in graph order. These are the outputs to evaluate.</summary>
        public int[] SinkNodes;

        /// <summary>Stable structural hash of the topology. Parameter values are deliberately excluded.</summary>
        public int StructuralHash;

        /// <summary>Diagnostics from compilation, including warnings that did not stop execution.</summary>
        public IReadOnlyList<PCGCompileDiagnostic> Diagnostics;

        /// <summary>The graph this was compiled from.</summary>
        public PCGGraph Source;

        /// <summary>Gets the number of executable nodes.</summary>
        public int NodeCount => Nodes?.Length ?? 0;
    }

    /// <summary>A resolved upstream connection for one input pin.</summary>
    public struct PCGInputLink
    {
        /// <summary>Index of the node producing the data.</summary>
        public int SourceNodeIndex;

        /// <summary>Output pin index on the source node.</summary>
        public int SourcePinIndex;

        /// <inheritdoc />
        public override string ToString() => $"{SourceNodeIndex}[{SourcePinIndex}]";
    }

    /// <summary>
    /// Turns a <see cref="PCGGraph"/> into an executable <see cref="PCGCompiledGraph"/>.
    /// </summary>
    /// <remarks>
    /// <para>Compilation is where four things happen that cannot be deferred to run time:</para>
    /// <list type="number">
    /// <item><b>Cycle detection.</b> A pull-based executor recurses through inputs, so a cycle (A feeds B
    /// feeds A) would recurse until the stack overflows. Users can create one in seconds in a graph editor,
    /// so it must be caught here and reported against the offending nodes and edges, never left to a
    /// run-time depth guard.</item>
    /// <item><b>Topological order.</b> Gives the executor a dependency-ordered plan, which is what makes
    /// result caching and future dirty-marking propagation possible.</item>
    /// <item><b>Parameter and connection validation.</b> Surfaces unknown node types, bad pin indices and
    /// missing required inputs as editor-facing diagnostics rather than run-time exceptions.</item>
    /// <item><b>Structural hashing.</b> Distinguishes "only a parameter changed" (incremental re-run) from
    /// "the topology changed" (full recompile).</item>
    /// </list>
    /// </remarks>
    public static class PCGGraphCompiler
    {
        /// <summary>
        /// Compiles a graph.
        /// </summary>
        /// <param name="graph">Graph to compile.</param>
        /// <param name="compiled">The compiled graph, or null when a hard error occurred.</param>
        /// <returns>True when the graph compiled without errors.</returns>
        public static bool TryCompile(PCGGraph graph, out PCGCompiledGraph compiled)
        {
            compiled = null;

            if (graph == null)
                return false;

            var diagnostics = new List<PCGCompileDiagnostic>();
            int nodeCount = graph.NodeCount;

            if (nodeCount == 0)
            {
                diagnostics.Add(new PCGCompileDiagnostic
                {
                    Severity = PCGCompileSeverity.Warning,
                    Message = "Graph has no nodes.",
                });

                compiled = new PCGCompiledGraph
                {
                    Nodes = Array.Empty<PCGNodeBase>(),
                    Links = Array.Empty<PCGInputLink[][]>(),
                    ExecutionOrder = Array.Empty<int>(),
                    SinkNodes = Array.Empty<int>(),
                    StructuralHash = 0,
                    Diagnostics = diagnostics,
                    Source = graph,
                };

                return true;
            }

            var settings = graph.Settings ?? new PCGGraphSettings();

            // ---- Instantiate nodes -------------------------------------------------
            var nodes = new PCGNodeBase[nodeCount];
            for (int i = 0; i < nodeCount; i++)
            {
                var node = graph.Nodes[i];
                if (node == null)
                {
                    diagnostics.Add(new PCGCompileDiagnostic
                    {
                        Severity = PCGCompileSeverity.Error,
                        Message = "Graph contains a null node entry.",
                        NodeIndex = i,
                    });
                    continue;
                }

                var instance = PCGNodeRegistry.CreateNode(node, i, out var error);
                if (instance == null)
                {
                    diagnostics.Add(new PCGCompileDiagnostic
                    {
                        Severity = PCGCompileSeverity.Error,
                        Message = error,
                        NodeIndex = i,
                    });
                    continue;
                }

                // Defensive: the instance's declared id must match the serialized one, otherwise the graph
                // would silently execute a different node than it names.
                if (!string.Equals(instance.TypeId, node.TypeId, StringComparison.Ordinal))
                {
                    diagnostics.Add(new PCGCompileDiagnostic
                    {
                        Severity = PCGCompileSeverity.Error,
                        Message = $"Node type mismatch: graph says '{node.TypeId}' but the instance reports '{instance.TypeId}'.",
                        NodeIndex = i,
                    });
                    continue;
                }

                nodes[i] = instance;
            }

            // ---- Resolve edges -----------------------------------------------------
            // Each node's per-pin link buckets are built as lists first, then frozen to arrays, so edges can
            // be appended without knowing the final counts up front.
            var buckets = new List<PCGInputLink>[nodeCount][];
            var links = new PCGInputLink[nodeCount][][];

            for (int i = 0; i < nodeCount; i++)
            {
                links[i] = Array.Empty<PCGInputLink[]>();

                if (nodes[i] == null)
                {
                    buckets[i] = Array.Empty<List<PCGInputLink>>();
                    continue;
                }

                var pins = nodes[i].InputPins ?? Array.Empty<PCGPinDefinition>();
                var perPin = new List<PCGInputLink>[pins.Length];
                for (int p = 0; p < pins.Length; p++)
                    perPin[p] = new List<PCGInputLink>();

                buckets[i] = perPin;
            }

            var edgeCount = graph.EdgeCount;
            for (int e = 0; e < edgeCount; e++)
            {
                var edge = graph.Edges[e];
                if (edge == null)
                {
                    diagnostics.Add(new PCGCompileDiagnostic
                    {
                        Severity = PCGCompileSeverity.Error,
                        Message = "Graph contains a null edge entry.",
                        EdgeIndex = e,
                    });
                    continue;
                }

                if (edge.OutputNodeIndex < 0 || edge.OutputNodeIndex >= nodeCount ||
                    edge.InputNodeIndex < 0 || edge.InputNodeIndex >= nodeCount)
                {
                    diagnostics.Add(new PCGCompileDiagnostic
                    {
                        Severity = PCGCompileSeverity.Error,
                        Message = $"Edge {e} references a node index outside the graph (nodes: {nodeCount}).",
                        EdgeIndex = e,
                    });
                    continue;
                }

                var sourceNode = nodes[edge.OutputNodeIndex];
                var targetNode = nodes[edge.InputNodeIndex];
                if (sourceNode == null || targetNode == null)
                {
                    // Already reported as a node error; do not pile on.
                    continue;
                }

                var outPins = sourceNode.OutputPins ?? Array.Empty<PCGPinDefinition>();
                var inPins = targetNode.InputPins ?? Array.Empty<PCGPinDefinition>();

                if (edge.OutputPinIndex < 0 || edge.OutputPinIndex >= outPins.Length)
                {
                    diagnostics.Add(new PCGCompileDiagnostic
                    {
                        Severity = PCGCompileSeverity.Error,
                        Message = $"Edge {e} targets output pin {edge.OutputPinIndex}, but " +
                                  $"'{sourceNode.TypeId}' has {outPins.Length} output pin(s).",
                        EdgeIndex = e,
                        NodeIndex = edge.OutputNodeIndex,
                    });
                    continue;
                }

                if (edge.InputPinIndex < 0 || edge.InputPinIndex >= inPins.Length)
                {
                    diagnostics.Add(new PCGCompileDiagnostic
                    {
                        Severity = PCGCompileSeverity.Error,
                        Message = $"Edge {e} targets input pin {edge.InputPinIndex}, but " +
                                  $"'{targetNode.TypeId}' has {inPins.Length} input pin(s).",
                        EdgeIndex = e,
                        NodeIndex = edge.InputNodeIndex,
                    });
                    continue;
                }

                // Self-connection is a one-node cycle; report it as a cycle so the message is familiar.
                if (edge.OutputNodeIndex == edge.InputNodeIndex)
                {
                    diagnostics.Add(new PCGCompileDiagnostic
                    {
                        Severity = PCGCompileSeverity.Error,
                        Message = $"Node '{targetNode.TypeId}' is connected to itself, which is a cycle.",
                        EdgeIndex = e,
                        NodeIndex = edge.InputNodeIndex,
                    });
                    continue;
                }

                var targetPin = inPins[edge.InputPinIndex];
                var bucket = buckets[edge.InputNodeIndex][edge.InputPinIndex];

                if (bucket.Count > 0 && !targetPin.AllowMultiple)
                {
                    diagnostics.Add(new PCGCompileDiagnostic
                    {
                        Severity = PCGCompileSeverity.Error,
                        Message = $"Input pin '{targetPin.Name}' on '{targetNode.TypeId}' accepts a single connection " +
                                  $"but has {bucket.Count + 1}.",
                        EdgeIndex = e,
                        NodeIndex = edge.InputNodeIndex,
                    });
                    continue;
                }

                bucket.Add(new PCGInputLink
                {
                    SourceNodeIndex = edge.OutputNodeIndex,
                    SourcePinIndex = edge.OutputPinIndex,
                });
            }

            // Freeze the buckets into the link arrays the executor consumes.
            for (int i = 0; i < nodeCount; i++)
            {
                var perPin = buckets[i];
                var frozen = new PCGInputLink[perPin.Length][];
                for (int p = 0; p < perPin.Length; p++)
                    frozen[p] = perPin[p].ToArray();

                links[i] = frozen;
            }

            // ---- Required inputs ---------------------------------------------------
            for (int i = 0; i < nodeCount; i++)
            {
                var node = nodes[i];
                if (node == null)
                    continue;

                var pins = node.InputPins ?? Array.Empty<PCGPinDefinition>();
                for (int p = 0; p < pins.Length; p++)
                {
                    if (!pins[p].IsRequired)
                        continue;

                    if (links[i][p].Length == 0)
                    {
                        diagnostics.Add(new PCGCompileDiagnostic
                        {
                            Severity = PCGCompileSeverity.Error,
                            Message = $"Required input '{pins[p].Name}' on '{node.TypeId}' is not connected.",
                            NodeIndex = i,
                        });
                    }
                }
            }

            // ---- Cycle detection (DFS three-colour) --------------------------------
            var colour = new byte[nodeCount]; // 0 = white, 1 = grey, 2 = black
            var stack = new Stack<int>();
            bool hasCycle = false;

            for (int i = 0; i < nodeCount && !hasCycle; i++)
            {
                if (nodes[i] == null || colour[i] != 0)
                    continue;

                if (DetectCycle(i, links, colour, stack, diagnostics))
                    hasCycle = true;
            }

            if (hasCycle)
            {
                // A cyclic graph has no valid order, so stop before producing a plan.
                compiled = new PCGCompiledGraph
                {
                    Nodes = nodes,
                    Links = links,
                    ExecutionOrder = Array.Empty<int>(),
                    SinkNodes = Array.Empty<int>(),
                    StructuralHash = ComputeStructuralHash(graph),
                    Diagnostics = diagnostics,
                    Source = graph,
                };

                return false;
            }

            // ---- Validate node count budget ---------------------------------------
            if (nodeCount > settings.MaxExpandedNodeCount)
            {
                diagnostics.Add(new PCGCompileDiagnostic
                {
                    Severity = PCGCompileSeverity.Error,
                    Message = $"Graph has {nodeCount} nodes, exceeding the configured maximum of " +
                              $"{settings.MaxExpandedNodeCount}.",
                });
            }

            // ---- Topological order (Kahn) -----------------------------------------
            var executionOrder = TopologicalOrder(nodeCount, nodes, links, diagnostics);

            // ---- Find sinks -------------------------------------------------------
            var hasConsumer = new bool[nodeCount];
            for (int i = 0; i < nodeCount; i++)
            {
                if (links[i] == null)
                    continue;

                for (int p = 0; p < links[i].Length; p++)
                {
                    var bucket = links[i][p];
                    for (int k = 0; k < bucket.Length; k++)
                    {
                        var src = bucket[k].SourceNodeIndex;
                        if (src >= 0 && src < nodeCount)
                            hasConsumer[src] = true;
                    }
                }
            }

            var sinks = new List<int>();
            for (int i = 0; i < nodeCount; i++)
            {
                if (nodes[i] != null && !hasConsumer[i])
                    sinks.Add(i);
            }

            bool hasError = false;
            for (int i = 0; i < diagnostics.Count; i++)
            {
                if (diagnostics[i].Severity == PCGCompileSeverity.Error)
                {
                    hasError = true;
                    break;
                }
            }

            compiled = new PCGCompiledGraph
            {
                Nodes = nodes,
                Links = links,
                ExecutionOrder = executionOrder,
                SinkNodes = sinks.ToArray(),
                StructuralHash = ComputeStructuralHash(graph),
                Diagnostics = diagnostics,
                Source = graph,
            };

            return !hasError;
        }

        /// <summary>Compiles a graph, throwing when it cannot run.</summary>
        public static PCGCompiledGraph Compile(PCGGraph graph)
        {
            if (!TryCompile(graph, out var compiled))
                throw new PCGCompilationException(compiled?.Diagnostics ?? Array.Empty<PCGCompileDiagnostic>());

            return compiled;
        }

        private static bool DetectCycle(
            int start, PCGInputLink[][][] links, byte[] colour, Stack<int> path, List<PCGCompileDiagnostic> diagnostics)
        {
            // Iterative DFS: recursion here would be the very thing that overflows on a deep graph.
            var frames = new Stack<(int node, int pin, int link)>();
            colour[start] = 1;
            path.Push(start);
            frames.Push((start, 0, 0));

            while (frames.Count > 0)
            {
                var frame = frames.Pop();
                var perPin = links[frame.node] ?? Array.Empty<PCGInputLink[]>();

                if (frame.pin >= perPin.Length)
                {
                    colour[frame.node] = 2;
                    if (path.Count > 0 && path.Peek() == frame.node)
                        path.Pop();

                    continue;
                }

                var bucket = perPin[frame.pin] ?? Array.Empty<PCGInputLink>();

                if (frame.link >= bucket.Length)
                {
                    frames.Push((frame.node, frame.pin + 1, 0));
                    continue;
                }

                var next = bucket[frame.link].SourceNodeIndex;

                // Resume this frame at the next link once the child is processed.
                frames.Push((frame.node, frame.pin, frame.link + 1));

                if (next < 0 || next >= colour.Length)
                    continue;

                if (colour[next] == 1)
                {
                    var chain = new StringBuilder();
                    var ordered = new List<int>(path.ToArray());
                    ordered.Reverse();

                    int from = ordered.IndexOf(next);
                    if (from < 0)
                        from = 0;

                    for (int i = from; i < ordered.Count; i++)
                        chain.Append(ordered[i]).Append(" -> ");

                    chain.Append(next);

                    diagnostics.Add(new PCGCompileDiagnostic
                    {
                        Severity = PCGCompileSeverity.Error,
                        Message = $"Graph contains a cycle: {chain}. Cycles cannot be evaluated because the " +
                                  "executor resolves inputs by pulling upstream.",
                        NodeIndex = next,
                    });

                    return true;
                }

                if (colour[next] == 0)
                {
                    colour[next] = 1;
                    path.Push(next);
                    frames.Push((next, 0, 0));
                }
            }

            return false;
        }

        private static int[] TopologicalOrder(
            int nodeCount, PCGNodeBase[] nodes, PCGInputLink[][][] links, List<PCGCompileDiagnostic> diagnostics)
        {
            // Kahn's algorithm over the "depends on" graph: a node becomes ready once all upstream nodes it
            // consumes have been emitted.
            var indegree = new int[nodeCount];
            var consumers = new List<int>[nodeCount];
            for (int i = 0; i < nodeCount; i++)
                consumers[i] = new List<int>();

            for (int i = 0; i < nodeCount; i++)
            {
                if (nodes[i] == null || links[i] == null)
                    continue;

                var seen = new HashSet<int>();
                for (int p = 0; p < links[i].Length; p++)
                {
                    var bucket = links[i][p];
                    for (int k = 0; k < bucket.Length; k++)
                    {
                        var src = bucket[k].SourceNodeIndex;
                        if (src < 0 || src >= nodeCount || nodes[src] == null)
                            continue;

                        // Multiple edges from the same node to the same node count once.
                        if (seen.Add(src))
                        {
                            indegree[i]++;
                            consumers[src].Add(i);
                        }
                    }
                }
            }

            var queue = new Queue<int>();
            for (int i = 0; i < nodeCount; i++)
            {
                if (nodes[i] != null && indegree[i] == 0)
                    queue.Enqueue(i);
            }

            var order = new List<int>(nodeCount);
            while (queue.Count > 0)
            {
                int node = queue.Dequeue();
                order.Add(node);

                var list = consumers[node];
                for (int i = 0; i < list.Count; i++)
                {
                    int consumer = list[i];
                    if (--indegree[consumer] == 0)
                        queue.Enqueue(consumer);
                }
            }

            if (order.Count != nodeCount)
            {
                // Reached only if a cycle slipped past detection; report rather than return a partial plan.
                diagnostics.Add(new PCGCompileDiagnostic
                {
                    Severity = PCGCompileSeverity.Error,
                    Message = $"Topological sort produced {order.Count} of {nodeCount} nodes, which indicates " +
                              "an unresolved dependency cycle.",
                });
            }

            return order.ToArray();
        }

        /// <summary>
        /// Hashes the graph structure: node types, node identity by index, edge endpoints and non-exposed
        /// parameter values.
        /// </summary>
        /// <remarks>
        /// Parameter values matter here because a literal node parameter (a sampler's point count, say)
        /// changes what the graph computes. Values that are exposed as graph parameters are <b>not</b>
        /// included, because those are overridden per volume and must not force a recompile.
        /// </remarks>
        public static int ComputeStructuralHash(PCGGraph graph)
        {
            if (graph == null)
                return 0;

            unchecked
            {
                int hash = 17;

                for (int i = 0; i < graph.NodeCount; i++)
                {
                    var node = graph.Nodes[i];
                    hash = hash * 31 + i;
                    hash = hash * 31 + StableStringHash(node?.TypeId);
                    hash = hash * 31 + (node?.Values?.Length ?? 0);
                }

                for (int i = 0; i < graph.EdgeCount; i++)
                {
                    var edge = graph.Edges[i];
                    if (edge == null)
                    {
                        hash = hash * 31 + 1;
                        continue;
                    }

                    hash = hash * 31 + edge.OutputNodeIndex;
                    hash = hash * 31 + edge.OutputPinIndex;
                    hash = hash * 31 + edge.InputNodeIndex;
                    hash = hash * 31 + edge.InputPinIndex;
                }

                // Fold in the non-exposed parameter values.
                var exposed = new HashSet<(int, int)>();
                if (graph.ExposedParameters != null)
                {
                    for (int i = 0; i < graph.ExposedParameters.Length; i++)
                    {
                        var p = graph.ExposedParameters[i];
                        if (p != null)
                            exposed.Add((p.NodeIndex, p.ValueIndex));
                    }
                }

                for (int i = 0; i < graph.NodeCount; i++)
                {
                    var node = graph.Nodes[i];
                    if (node?.Values == null)
                        continue;

                    for (int v = 0; v < node.Values.Length; v++)
                    {
                        if (exposed.Contains((i, v)))
                            continue;

                        hash = hash * 31 + StableValueHash(node.Values[v]);
                    }
                }

                return hash;
            }
        }

        private static int StableStringHash(string s)
        {
            if (s == null)
                return 0;

            // Deliberately not string.GetHashCode: that is randomised per process on modern .NET, which
            // would make the structural hash differ between runs and defeat incremental execution.
            unchecked
            {
                int hash = 23;
                for (int i = 0; i < s.Length; i++)
                    hash = hash * 31 + s[i];

                return hash;
            }
        }

        private static int StableValueHash(object value)
        {
            if (value == null)
                return 0;

            switch (value)
            {
            case float f: return f.GetHashCode();
            case int i: return i;
            case bool b: return b ? 1 : 0;
            case string s: return StableStringHash(s);
            case Vector2 v2: return v2.X.GetHashCode() * 31 + v2.Y.GetHashCode();
            case Vector3 v3: return (v3.X.GetHashCode() * 31 + v3.Y.GetHashCode()) * 31 + v3.Z.GetHashCode();
            case Vector4 v4:
                return ((v4.X.GetHashCode() * 31 + v4.Y.GetHashCode()) * 31 + v4.Z.GetHashCode()) * 31 +
                       v4.W.GetHashCode();
            case Color c:
                return ((c.R.GetHashCode() * 31 + c.G.GetHashCode()) * 31 + c.B.GetHashCode()) * 31 + c.A.GetHashCode();
            case Guid g: return g.GetHashCode();
            default: return StableStringHash(value.ToString());
            }
        }
    }
}

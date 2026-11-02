// PCG 插件 - 图编译器
// FlaxEngine PCG 插件的一部分。见 docs/PCG_PLUGIN_DESIGN.md §4.1 注 5 与 §4.5。

using System;
using System.Collections.Generic;
using System.Text;
using FlaxEngine;
using PCG.Graph;

namespace PCG.Execution
{
    /// <summary>一次编译诊断的严重度。</summary>
    public enum PCGCompileSeverity
    {
        /// <summary>会报告，但图照样能运行。</summary>
        Warning = 0,

        /// <summary>图不能运行。</summary>
        Error = 1,
    }

    /// <summary>一条编译诊断，可回溯到图元素以便编辑器高亮。</summary>
    public sealed class PCGCompileDiagnostic
    {
        /// <summary>严重度。</summary>
        public PCGCompileSeverity Severity;

        /// <summary>消息。</summary>
        public string Message;

        /// <summary>出问题的节点下标，或 -1。</summary>
        public int NodeIndex = -1;

        /// <summary>出问题的边下标，或 -1。</summary>
        public int EdgeIndex = -1;

        /// <inheritdoc />
        public override string ToString() =>
            $"[{Severity}] {(NodeIndex >= 0 ? $"node {NodeIndex}: " : string.Empty)}{Message}";
    }

    /// <summary>在未编译成功的情况下执行图时抛出。</summary>
    public sealed class PCGCompilationException : Exception
    {
        /// <summary>造成这次失败的诊断项。</summary>
        public IReadOnlyList<PCGCompileDiagnostic> Diagnostics { get; }

        /// <summary>创建这个异常。</summary>
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

    /// <summary>一份已编译、可执行的图。</summary>
    public sealed class PCGCompiledGraph
    {
        /// <summary>节点实例，按图里的节点下标索引。</summary>
        public PCGNodeBase[] Nodes;

        /// <summary>
        /// 每个节点的上游输入，按图里的节点下标索引。每个条目是一个按输入 pin 排的数组，
        /// 里面装 <c>(上游节点下标, 上游输出 pin 下标)</c> 对，按边顺序。
        /// </summary>
        public PCGInputLink[][][] Links;

        /// <summary>依赖顺序下的节点下标：每个节点都出现在它所有输入之后。</summary>
        public int[] ExecutionOrder;

        /// <summary>不喂给任何消费者的节点，按图顺序。这些就是要求值的输出。</summary>
        public int[] SinkNodes;

        /// <summary>拓扑的稳定结构哈希。参数值刻意排除在外。</summary>
        public int StructuralHash;

        /// <summary>编译产生的诊断，包括那些没挡住执行的警告。</summary>
        public IReadOnlyList<PCGCompileDiagnostic> Diagnostics;

        /// <summary>它是从哪张图编译来的。</summary>
        public PCGGraph Source;

        /// <summary>取得可执行节点的数量。</summary>
        public int NodeCount => Nodes?.Length ?? 0;
    }

    /// <summary>一个已解析的上游连接，对应一个输入 pin。</summary>
    public struct PCGInputLink
    {
        /// <summary>产出这份数据的节点下标。</summary>
        public int SourceNodeIndex;

        /// <summary>来源节点上的输出 pin 下标。</summary>
        public int SourcePinIndex;

        /// <inheritdoc />
        public override string ToString() => $"{SourceNodeIndex}[{SourcePinIndex}]";
    }

    /// <summary>
    /// 把一张 <see cref="PCGGraph"/> 变成可执行的 <see cref="PCGCompiledGraph"/>。
    /// </summary>
    /// <remarks>
    /// <para>编译这一步做了四件没法推迟到运行时的事情：</para>
    /// <list type="number">
    /// <item><b>成环检测。</b>拉取式的执行器会顺着输入递归，所以一个环（A 喂 B
    /// 喂 A）会一路递归到栈溢出。用户在图编辑器里几秒就能造出一个，
    /// 所以它必须在这里被抓住、并报到出问题的节点与边上，绝不能留给
    /// 运行时的深度守卫。</item>
    /// <item><b>拓扑排序。</b>给执行器一份按依赖排好的计划，这正是
    /// 结果缓存与将来脏标记传播得以可能的前提。</item>
    /// <item><b>参数与连接校验。</b>把认不出的节点类型、错的 pin 下标、
    /// 缺失的必需输入变成给编辑器看的诊断，而不是运行时的异常。</item>
    /// <item><b>结构哈希。</b>区分"只改了参数"（增量重跑）与
    /// "拓扑变了"（整体重编译）。</item>
    /// </list>
    /// </remarks>
    public static class PCGGraphCompiler
    {
        /// <summary>
        /// 编译一张图。
        /// </summary>
        /// <param name="graph">要编译的图。</param>
        /// <param name="compiled">编译结果；出现硬错误时为 null。</param>
        /// <returns>图在无错误的前提下编译成功时为真。</returns>
        public static bool TryCompile(PCGGraph graph, out PCGCompiledGraph compiled)
        {
            compiled = null;

            if (graph == null)
                return false;

            // 编译前先确保**节点表装好了**。这一步是补一个真缺陷：编辑器那一侧由画布安装（`PCGNodeArchetypes.Build`
            // 见到空表就注册），而运行期是另一条入口——PIE / 游戏里没有画布，表可能被脚本重载清过而没人重装，
            // 于是本该能跑的图在运行时报「Unknown node type 'LandscapeSampler' … a plugin that is not loaded」，
            // 表现就是"编辑器里跑得好好的图，一进 PIE 就编译失败"。`RegisterAll` 是幂等的（重复调用只覆盖同一张表），
            // 所以这里无条件调一次，代价可以忽略。
            PCG.Runtime.PCGNodeRegistration.RegisterAll();

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

            // ---- 实例化节点 -------------------------------------------------
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

                // 防御性的：实例自己声明的 id 必须与序列化下来的那个一致，否则这张图
                // 会静静地执行一个与它名字所指不同的节点。
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

                ValidateParameterSlots(node, i, diagnostics);
                nodes[i] = instance;
            }

            // ---- 解析边 -----------------------------------------------------
            // 每个节点按 pin 分桶的链接先建成列表、再冻成数组，于是边的追加
            // 不需要预先知道最终的个数。
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
                    // 已经作为节点错误报过了；不要再叠一条。
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

                // 自连接是一个单节点的环；按环来报，好让消息是熟悉的。
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

            // 把这些桶冻成执行器消费的那几份链接数组。
            for (int i = 0; i < nodeCount; i++)
            {
                var perPin = buckets[i];
                var frozen = new PCGInputLink[perPin.Length][];
                for (int p = 0; p < perPin.Length; p++)
                    frozen[p] = perPin[p].ToArray();

                links[i] = frozen;
            }

            // ---- 必需输入 ---------------------------------------------------
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

            // ---- 成环检测（DFS 三色标记） --------------------------------
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
                // 有环的图没有合法的顺序，所以在产出计划之前停下。
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

            // ---- 校验节点数量预算 ---------------------------------------
            if (nodeCount > settings.MaxExpandedNodeCount)
            {
                diagnostics.Add(new PCGCompileDiagnostic
                {
                    Severity = PCGCompileSeverity.Error,
                    Message = $"Graph has {nodeCount} nodes, exceeding the configured maximum of " +
                              $"{settings.MaxExpandedNodeCount}.",
                });
            }

            // ---- 拓扑排序（Kahn） -----------------------------------------
            var executionOrder = TopologicalOrder(nodeCount, nodes, links, diagnostics);

            // ---- 找汇点 -------------------------------------------------------
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

        /// <summary>编译一张图，跑不了就抛。</summary>
        public static PCGCompiledGraph Compile(PCGGraph graph)
        {
            if (!TryCompile(graph, out var compiled))
                throw new PCGCompilationException(compiled?.Diagnostics ?? Array.Empty<PCGCompileDiagnostic>());

            return compiled;
        }

        private static bool DetectCycle(
            int start, PCGInputLink[][][] links, byte[] colour, Stack<int> path, List<PCGCompileDiagnostic> diagnostics)
        {
            // 迭代式 DFS：这里用递归的话，递归本身就会在深图上溢出。
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

                // 子节点处理完之后，从下一条链接继续这一帧。
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
            // 在"依赖于"这张图上跑 Kahn 算法：一个节点在它所有上游
            // 都已被发出之后就就绪。
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

                        // 同一个节点到同一个节点的多条边只算一次。
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
                // 只有当某个环漏过了检测才会走到这里；宁可报出来，也不交回一份半成品计划。
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
        /// 给图的结构做哈希：节点类型、按下标认定的节点身份、边的两端，以及未被暴露的
        /// 参数值。
        /// </summary>
        /// <remarks>
        /// 参数值之所以在这里要紧，是因为一个字面量的节点参数（比如采样器的点数）
        /// 会改变这张图算的是什么；而作为图参数被暴露出去的值<b>不</b>计入，
        /// 因为那些是按体积覆盖的，不该逼着整体重编译。
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

                // 把未被暴露的参数值一起折进来。
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

        /// <summary>
        /// 拿节点存下的值与它的类型声明的参数槽位做核对。
        /// </summary>
        /// <remarks>
        /// 少了这一步，写进错槽位的值就是隐形的：<c>PCGNodeParameters</c> 对类型不符
        /// 回答的是节点自己的默认值，于是这张图会跑起来，并静静地无视
        /// 设计者输入的东西。种类写错是错误，因为跑出来的会与图所展示的不同；
        /// 多出来的槽位只是警告，因为没人读它——那浪费力气
        /// 但不会误导。
        /// </remarks>
        private static void ValidateParameterSlots(PCGNode node, int nodeIndex, List<PCGCompileDiagnostic> diagnostics)
        {
            if (node?.Values == null || node.Values.Length == 0)
                return;

            if (!PCGNodeRegistry.TryGet(node.TypeId ?? string.Empty, out var info))
                return;

            var schema = info.Parameters;

            if (schema == null || schema.Length == 0)
            {
                diagnostics.Add(new PCGCompileDiagnostic
                {
                    Severity = PCGCompileSeverity.Warning,
                    Message = $"'{node.TypeId}' declares no parameters but the graph stores {node.Values.Length} " +
                              $"value(s) on node {nodeIndex}; nothing reads them.",
                    NodeIndex = nodeIndex,
                });
                return;
            }

            for (int v = 0; v < node.Values.Length; v++)
            {
                var value = node.Values[v];
                if (value == null)
                    continue;

                if (v >= schema.Length)
                {
                    diagnostics.Add(new PCGCompileDiagnostic
                    {
                        Severity = PCGCompileSeverity.Warning,
                        Message = $"'{node.TypeId}' node {nodeIndex} stores a value at slot {v} " +
                                  $"({value.GetType().Name}), past its {schema.Length} declared parameter slot(s); " +
                                  "the node never reads it.",
                        NodeIndex = nodeIndex,
                    });
                    continue;
                }

                var slot = schema[v];
                if (slot.IsCompatible(value))
                    continue;

                diagnostics.Add(new PCGCompileDiagnostic
                {
                    Severity = PCGCompileSeverity.Error,
                    Message = $"'{node.TypeId}' node {nodeIndex}, parameter '{slot.Name}' expects {slot.Kind} " +
                              $"but the graph holds a {value.GetType().Name}. The node would ignore this value " +
                              "and use its default, so the run would not match the graph.",
                    NodeIndex = nodeIndex,
                });
            }
        }

        private static int StableStringHash(string s)
        {
            if (s == null)
                return 0;

            // 刻意不用 string.GetHashCode：现代 .NET 上它每个进程都随机化，
            // 那会让结构哈希在两次运行之间不同，从而把增量执行废掉。
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

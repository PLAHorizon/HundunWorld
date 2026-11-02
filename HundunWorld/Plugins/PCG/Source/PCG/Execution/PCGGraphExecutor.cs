// PCG 插件 - 图执行器（拉取式）
// FlaxEngine PCG 插件的一部分。见 docs/PCG_PLUGIN_DESIGN.md §4.1 与 §4.4。

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using FlaxEngine;
using PCG.Graph;

namespace PCG.Execution
{
    /// <summary>一次运行期间收集的按节点统计。</summary>
    public struct PCGNodeRunStats
    {
        /// <summary>节点在图里的下标。</summary>
        public int NodeIndex;

        /// <summary>节点类型 id。</summary>
        public string TypeId;

        /// <summary>这个节点产出的点数。</summary>
        public int PointCount;

        /// <summary>求值这个节点花掉的墙上毫秒数。</summary>
        public double Milliseconds;
    }

    /// <summary>一次图执行的结果。</summary>
    public sealed class PCGExecutionResult
    {
        /// <summary>每个汇节点的产出数据，按 <see cref="PCGCompiledGraph.SinkNodes"/> 的顺序。</summary>
        public PCGData[] Outputs;

        /// <summary>与 <see cref="Outputs"/> 一一对应的汇节点下标。</summary>
        public int[] OutputNodeIndices;

        /// <summary>所有节点跨各次求值累加出的总点数。</summary>
        public int TotalPointsProduced;

        /// <summary>这次运行的墙上时长。</summary>
        public double Milliseconds;

        /// <summary>运行被取消时为真。</summary>
        public bool Cancelled;

        /// <summary>按求值顺序排列的按节点统计。</summary>
        public PCGNodeRunStats[] Stats;

        /// <summary>运行期间产生的非致命消息。</summary>
        public string[] Warnings;

        /// <summary>顺手给出的：第一个汇产出的点。</summary>
        public PCGData PrimaryOutput => Outputs != null && Outputs.Length > 0 ? Outputs[0] : PCGData.Empty;
    }

    /// <summary>
    /// 通过从汇点拉数据来求值一张已编译的图。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 每个节点的值在一次运行内缓存，所以一个喂给两个消费者的节点只
    /// 求值一次。缓存条目在运行结束时丢弃；运行之间什么都不保留，这让
    /// 执行器不必去推理增量执行否则必须面对的跨运行陈旧
    /// 问题。
    /// </para>
    /// <para>
    /// <b>线程。</b>执行器自己必须跑在拥有场景的那个线程上，因为节点可能
    /// 创建 actor。被标记为 <see cref="PCGNodeBase.IsThreadSafe"/> 的单个节点可以把自己的
    /// 内层循环并行化；但那样做时不得触碰场景状态。具体说，actor 创建、
    /// 植被写入与调试绘制都只在主线程，所以一次后台运行必须把自己的
    /// <see cref="PCGData"/> 交回主线程，而不是直接应用它。
    /// </para>
    /// </remarks>
    public sealed class PCGGraphExecutor
    {
        private readonly PCGCompiledGraph _graph;
        private readonly PCGNodeBase[] _nodes;
        private readonly PCGData[] _cache;
        private readonly bool[] _evaluated;
        private readonly List<int> _evaluationOrder = new List<int>();
        private readonly List<string> _warnings = new List<string>();
        private readonly string[] _planWarnings;
        private readonly Dictionary<int, double> _timings = new Dictionary<int, double>();

        private int _pointsProduced;
        private int _pointBudget;

        /// <summary>
        /// 为一张已编译的图创建执行器。
        /// </summary>
        /// <param name="graph">已编译的图，提供拓扑与已编排的节点实例。</param>
        /// <param name="plan">
        /// 可选的、每次运行的节点实例，来自 <see cref="PCGExecutionPlan.Build"/>。传它进来是为了
        /// 带着已暴露参数的覆盖值跑这张图；已编译的图本身永不被修改，于是一次编译
        /// 可以服务任意多个取值不同的体积。
        /// </param>
        public PCGGraphExecutor(PCGCompiledGraph graph, PCGExecutionPlan plan = null)
        {
            _graph = graph ?? throw new ArgumentNullException(nameof(graph));
            _nodes = plan?.Nodes ?? graph.Nodes;

            // 参数问题属于这次运行，而且必须活过 Execute 的每次运行警告清空：
            // 清掉那个列表会静静吞掉一个什么都没匹配上的覆盖值。
            _planWarnings = plan?.Warnings ?? Array.Empty<string>();

            _cache = new PCGData[_graph.NodeCount];
            _evaluated = new bool[_graph.NodeCount];
        }

        /// <summary>
        /// 运行这张图。
        /// </summary>
        /// <param name="context">执行上下文。它的 <c>Seed</c> 与 <c>GenerationBounds</c> 驱动生成。</param>
        /// <returns>这次运行的结果。使用产出之前先查 <see cref="PCGExecutionResult.Cancelled"/>。</returns>
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

            // 重置用于播种的节点 id，好让每一次运行都复现出同样的值。
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
                result.Warnings = BuildWarnings();
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
            result.Warnings = BuildWarnings();
            result.Stats = BuildStats();

            return result;
        }

        /// <summary>
        /// 把随计划一起给出的参数层警告与这次运行期间产生的警告合在一起。
        /// </summary>
        /// <remarks>
        /// 计划里那些条目不能改为推进 <c>_warnings</c>：<c>Execute</c> 在每次运行开头会清空
        /// 那个列表，那会静静吞掉一个没匹配上任何已暴露参数的覆盖值。
        /// </remarks>
        /// <summary>
        /// 把节点自报的问题搬进这次运行的警告里。
        /// </summary>
        /// <remarks>
        /// 正是这个消费者让 <c>PCGNodeBase.LastError</c> 不止是一个测试能读到的字段：一个
        /// 因为地形缺失而什么都没产出的采样器，或一个模型资产加载失败的生成器，否则会把设计者
        /// 留在一个空场景里、没有任何一行指向这张图。错误在收走之后被清掉，
        /// 于是一个被缓存的节点不会在每个消费者拉取时都被报一遍。
        /// </remarks>
        private void CollectNodeError(PCGNodeBase node, int nodeIndex)
        {
            var message = node?.LastError;
            if (string.IsNullOrEmpty(message))
                return;

            _warnings.Add($"node {nodeIndex} ('{node.TypeId}'): {message}");
            node.ClearError();
        }

        private string[] BuildWarnings()
        {
            if (_planWarnings.Length == 0)
                return _warnings.ToArray();

            var all = new string[_planWarnings.Length + _warnings.Count];
            Array.Copy(_planWarnings, 0, all, 0, _planWarnings.Length);
            _warnings.CopyTo(0, all, _planWarnings.Length, _warnings.Count);
            return all;
        }

        private PCGNodeRunStats[] BuildStats()
        {
            var stats = new PCGNodeRunStats[_evaluationOrder.Count];
            for (int i = 0; i < _evaluationOrder.Count; i++)
            {
                int nodeIndex = _evaluationOrder[i];
                var node = _nodes[nodeIndex];
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
        /// 求值一个节点，先拉它的输入。结果在本次运行剩余时间里缓存。
        /// </summary>
        private PCGData Evaluate(int nodeIndex, PCGExecutionContext context)
        {
            if (nodeIndex < 0 || nodeIndex >= _graph.NodeCount)
                return PCGData.Empty;

            if (_evaluated[nodeIndex])
                return _cache[nodeIndex];

            var node = _nodes[nodeIndex];
            if (node == null)
            {
                // 编译已保证这里非空；当作空处理，而不是在运行中途抛。
                _evaluated[nodeIndex] = true;
                _cache[nodeIndex] = PCGData.Empty;
                return _cache[nodeIndex];
            }

            // 被禁用的节点是原样递出去：第一个输入胜出，否则节点照样求值，
            // 好让一个被禁用的采样器仍然交出一份（空的）数据，而不是把图弄断。
            var inputs = BuildInputs(nodeIndex, context);

            // 秒表从这里开始计时，而不是 BuildInputs 之前：拉输入会递归求值这个节点整个
            // 上游子树，所以在它上面启动的秒表会把每个节点的时间报成"它自己的加上它
            // 消费的一切"。那样按节点的表沿图单调递增，而对找出慢节点毫无用处——
            // 而那张表存在的唯一理由就是找出慢节点。
            var start = Stopwatch.GetTimestamp();

            PCGData output;
            try
            {
                context.CurrentNodeId = nodeIndex;
                output = node.Execute(context, inputs);
                CollectNodeError(node, nodeIndex);
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
                // 属性误用是节点的缺陷；把节点身份附上一起报出来。
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

            // 在缓存填好之后再记录，于是检视器看到的正是一个下游节点本来会拉到的
            // 那份东西——并且做了判空，所以没有记录器的运行一如既往地行为。
            context.Inspection?.Record(nodeIndex, node.TypeId, output);

            var elapsed = Stopwatch.GetTimestamp() - start;
            _timings[nodeIndex] = elapsed * 1000.0 / Stopwatch.Frequency;

            return output;
        }

        /// <summary>
        /// 为一个节点构造输入数组，按 pin 声明的顺序。
        /// </summary>
        /// <remarks>
        /// 允许多连接的 pin，其条目是一个 <see cref="PCGData"/> 数组；普通 pin 的条目是
        /// 单个 <see cref="PCGData"/>，未连接时为 null。节点按自己的
        /// <see cref="PCGPinDefinition.AllowMultiple"/> 声明来理解这两种形状。
        /// </remarks>
        private PCGData[] BuildInputs(int nodeIndex, PCGExecutionContext context)
        {
            var node = _nodes[nodeIndex];
            var pinDefs = node.InputPins ?? Array.Empty<PCGPinDefinition>();
            var links = _graph.Links[nodeIndex] ?? Array.Empty<PCGInputLink[]>();

            // 那个数组之所以定型为 PCGData[]，是因为单个条目可能装着一份数据、也可能装着一份
            // 数据的数组，而 PCGData 本身不是数组类型。节点自己转读多输入的那些条目。
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
                    // 多输入的 pin 会被求值并展平成一个聚合，好让条目始终是
                    // 单个 PCGData；合并类节点从 PCGData.Parts 里读各部分。
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
        /// 单独求值一个节点，需要时先求值它的输入。
        /// </summary>
        /// <remarks>
        /// 给那些想预览单个节点的贡献、又不想跑完整张图的编辑器用。这里用的是全新的
        /// 执行器，所以调用之间不会漏状态。
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
        /// 该节点的缓存结果，仅在一次运行期间有效。
        /// </summary>
        public PCGData GetCachedResult(int nodeIndex)
        {
            if (nodeIndex < 0 || nodeIndex >= _graph.NodeCount || !_evaluated[nodeIndex])
                return null;

            return _cache[nodeIndex];
        }
    }
}

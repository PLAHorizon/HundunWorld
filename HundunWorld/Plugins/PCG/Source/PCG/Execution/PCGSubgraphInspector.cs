// PCG 插件 - 子图解析与跨资产环检测
// FlaxEngine PCG 插件的一部分。见 docs/PCG_PLUGIN_DESIGN.md §5.1.4 注 11。

using System;
using System.Collections.Generic;
using PCG.Graph;

namespace PCG.Execution
{
    /// <summary>
    /// 走一遍这张图以及它引用的所有子图，报告那些否则只会以栈溢出
    /// 或一次永不停止的生成冒出水面的东西。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 图内的环检查住在编译器里，但子图引用造出的是跨文件的环：
    /// 图 A 包含 B、B 又包含 A。这种情况下任何单张图看上去都没毛病，所以这一趟
    /// 必须带着一个栈顺着引用走、并把话说清楚——这也是它自成一趟
    /// 并且在编译之前跑的原因。
    /// </para>
    /// <para>
    /// 解析是注入进来的，不在这里执行：由调用方决定子图从磁盘来、
    /// 从编辑器缓冲区来，还是从测试夹具来。于是这段逻辑不依赖引擎也能跑。
    /// </para>
    /// </remarks>
    public static class PCGSubgraphInspector
    {
        /// <summary>子图节点里存放其子图名称的参数槽位。</summary>
        public const int SubgraphNameSlot = 0;

        /// <summary>展开深度的默认上限；链到这么长是出错，不是设计。</summary>
        public const int DefaultMaxDepth = 16;

        /// <summary>整趟走查里所有图的展开节点总数默认上限。</summary>
        public const int DefaultMaxExpandedNodes = 4096;

        /// <summary>
        /// 走一遍这张图和它能到达的每一张子图。
        /// </summary>
        /// <param name="graph">顶层图。</param>
        /// <param name="name">顶层图的名字，用在环的链条里。</param>
        /// <param name="resolve">按名字载入子图；为 null 表示解析不出来。</param>
        /// <param name="maxDepth">展开深度上限。</param>
        /// <param name="maxExpandedNodes">整张展开图里的节点总数上限。</param>
        /// <returns>诊断项；只要有一条是 <see cref="PCGCompileSeverity.Error"/>，这张图就不能运行。</returns>
        public static List<PCGCompileDiagnostic> Inspect(
            PCGGraph graph, string name, Func<string, PCGGraph> resolve,
            int maxDepth = DefaultMaxDepth, int maxExpandedNodes = DefaultMaxExpandedNodes)
        {
            var diagnostics = new List<PCGCompileDiagnostic>();

            if (graph == null)
                return diagnostics;

            Walk(graph, name ?? string.Empty, resolve,
                new List<string>(), 0, new Counters(), diagnostics,
                Math.Max(1, maxDepth), Math.Max(1, maxExpandedNodes));

            return diagnostics;
        }

        /// <summary>只要有一条诊断是错误就为真。</summary>
        public static bool HasErrors(List<PCGCompileDiagnostic> diagnostics)
        {
            if (diagnostics == null)
                return false;

            for (int i = 0; i < diagnostics.Count; i++)
            {
                if (diagnostics[i].Severity == PCGCompileSeverity.Error)
                    return true;
            }

            return false;
        }

        /// <summary>从子图节点的位置参数里读出子图的名字。</summary>
        public static string ChildNameOf(PCGNode node)
        {
            var values = node?.Values;
            if (values == null || values.Length <= SubgraphNameSlot)
                return null;

            return values[SubgraphNameSlot] as string;
        }

        private sealed class Counters
        {
            public int Nodes;
            public bool BudgetReported;
        }

        private static void Walk(
            PCGGraph graph, string name, Func<string, PCGGraph> resolve,
            List<string> chain, int depth, Counters counters,
            List<PCGCompileDiagnostic> diagnostics, int maxDepth, int maxExpandedNodes)
        {
            int cycleAt = -1;
            for (int i = 0; i < chain.Count; i++)
            {
                if (string.Equals(chain[i], name, StringComparison.Ordinal))
                {
                    cycleAt = i;
                    break;
                }
            }

            if (cycleAt >= 0)
            {
                // 报告并停下：继续递归正是这条检查要防的那件事。
                diagnostics.Add(new PCGCompileDiagnostic
                {
                    Severity = PCGCompileSeverity.Error,
                    Message = $"Sub-graph cycle: {ChainText(chain, name, cycleAt)}. Sub-graph references must form a tree.",
                });
                return;
            }

            if (depth >= maxDepth)
            {
                diagnostics.Add(new PCGCompileDiagnostic
                {
                    Severity = PCGCompileSeverity.Error,
                    Message = $"Sub-graph nesting exceeds the limit of {maxDepth} levels at '{name}' " +
                              $"({ChainText(chain, name, -1)}). Flatten the graph or raise the limit deliberately.",
                });
                return;
            }

            chain.Add(name);

            var nodes = graph.Nodes;
            for (int i = 0; i < (nodes?.Length ?? 0); i++)
            {
                var node = nodes[i];
                if (node == null)
                    continue;

                counters.Nodes++;

                if (counters.Nodes > maxExpandedNodes && !counters.BudgetReported)
                {
                    counters.BudgetReported = true;
                    diagnostics.Add(new PCGCompileDiagnostic
                    {
                        Severity = PCGCompileSeverity.Warning,
                        Message = $"The expanded graph passes {maxExpandedNodes} nodes; generation may be slower " +
                                  "than intended. This is a warning, not a refusal.",
                    });
                }

                if (!string.Equals(node.TypeId, Nodes.PCGSubgraphNode.Id, StringComparison.Ordinal))
                    continue;

                var requested = ChildNameOf(node);
                if (string.IsNullOrWhiteSpace(requested))
                {
                    diagnostics.Add(new PCGCompileDiagnostic
                    {
                        Severity = PCGCompileSeverity.Error,
                        Message = "Sub-graph node has no child graph name; it would produce nothing.",
                        NodeIndex = i,
                    });
                    continue;
                }

                // 自引用是一个长度为 1 的环；直接点出它比给出整条链条更好读。
                PCGGraph child = null;
                try
                {
                    child = resolve?.Invoke(requested);
                }
                catch (Exception ex)
                {
                    diagnostics.Add(new PCGCompileDiagnostic
                    {
                        Severity = PCGCompileSeverity.Error,
                        Message = $"Sub-graph '{requested}' could not be loaded: {ex.Message}",
                        NodeIndex = i,
                    });
                    continue;
                }

                if (child == null)
                {
                    diagnostics.Add(new PCGCompileDiagnostic
                    {
                        Severity = PCGCompileSeverity.Error,
                        Message = $"Sub-graph '{requested}' was not found. Check the name, or create the graph file first.",
                        NodeIndex = i,
                    });
                    continue;
                }

                Walk(child, requested, resolve, chain, depth + 1, counters, diagnostics, maxDepth, maxExpandedNodes);
            }

            chain.RemoveAt(chain.Count - 1);
        }

        private static string ChainText(List<string> chain, string name, int cycleAt)
        {
            var builder = new System.Text.StringBuilder();

            for (int i = cycleAt >= 0 ? cycleAt : 0; i < chain.Count; i++)
            {
                if (builder.Length > 0)
                    builder.Append(" -> ");

                builder.Append(chain[i]);
            }

            if (builder.Length > 0)
                builder.Append(" -> ");

            builder.Append(name);
            return builder.ToString();
        }
    }
}

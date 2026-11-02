// PCG 插件 - 每次运行的参数实例化
// FlaxEngine PCG 插件的一部分。见 docs/PCG_PLUGIN_DESIGN.md §5.2 与 §4.5 注 9。

using System;
using System.Collections.Generic;
using FlaxEngine;
using PCG.Graph;

namespace PCG.Execution
{
    /// <summary>
    /// 一张图跑一次所用的节点实例，并把暴露参数的值叠在编译好的图之上。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 编译好的图缓存按写死的值建出来的节点实例，而它的结构哈希**故意**不看暴露参数的值。
    /// 这个类就是这两者相遇的地方：调用方给一张"名字→值"的表，计划只重建那些值真正影响得到的
    /// 节点实例，编译出来的拓扑连同它的哈希原样复用。没有这一层，"一张图、多个体积"要么做不到，
    /// 要么每个实例都得整体重编一次——那正是
    /// docs/PCG_PLUGIN_DESIGN.md §4.5 注 9 当初要挡掉的结果。
    /// </para>
    /// <para>
    /// 优先序就是 §5.2 那条链：先节点上写死的值，然后在该槽位没有值的地方用图的暴露默认值，
    /// 实例覆盖永远排在最后。
    /// </para>
    /// </remarks>
    public sealed class PCGExecutionPlan
    {
        /// <summary>本次运行的节点实例，按图里的节点下标索引。</summary>
        public PCGNodeBase[] Nodes;

        /// <summary>应用参数时发现的不致命问题，供编辑器里显示出来。</summary>
        public string[] Warnings = Array.Empty<string>();

        /// <summary>没有任何值与写死的值不同（于是编译好的实例被原样复用）时为真。</summary>
        public bool ReusedCompiledNodes { get; private set; }

        /// <summary>
        /// 建出这次运行的节点实例。
        /// </summary>
        /// <param name="compiled">编译好的图，其 <c>Source</c> 提供写死的值。</param>
        /// <param name="overrides">按名字给出的暴露参数覆盖，可以为 null 或空。</param>
        public static PCGExecutionPlan Build(
            PCGCompiledGraph compiled, IReadOnlyDictionary<string, PCGParameterValue> overrides)
        {
            if (compiled == null)
                throw new ArgumentNullException(nameof(compiled));

            var graph = compiled.Source;
            var exposed = graph?.ExposedParameters;
            int nodeCount = compiled.Nodes?.Length ?? 0;
            bool hasOverrides = overrides != null && overrides.Count > 0;
            var warnings = new List<string>();

            if (nodeCount == 0 || ((exposed == null || exposed.Length == 0) && !hasOverrides))
                return Reuse(compiled, warnings);

            // 按受影响的下标把有效值累积起来，于是同一个节点上的多个暴露参数是叠加生效，
            // 而不是每一个都从写死的那份数组重新出发。
            var modifiedNodes = new Dictionary<int, PCGNode>(8);

            for (int i = 0; i < (exposed?.Length ?? 0); i++)
            {
                var definition = exposed[i];
                if (definition == null)
                    continue;

                if (definition.NodeIndex < 0 || definition.NodeIndex >= nodeCount)
                {
                    warnings.Add($"Exposed parameter '{definition.Name}' targets node index {definition.NodeIndex}, " +
                                 $"which is outside the graph's {nodeCount} node(s); ignored.");
                    continue;
                }

                var authored = graph.Nodes?[definition.NodeIndex];
                if (authored == null)
                    continue;

                // 先在本地副本上做，等真有槽位变了再提交：写死的值本来就赢的那些定义，
                // 不该触发一次毫无意义的节点重建。
                if (!modifiedNodes.TryGetValue(definition.NodeIndex, out var target))
                {
                    target = new PCGNode
                    {
                        TypeId = authored.TypeId,
                        EditorPosition = authored.EditorPosition,
                        Comment = authored.Comment,
                        Disabled = authored.Disabled,
                        Values = authored.Values == null
                            ? Array.Empty<object>()
                            : (object[])authored.Values.Clone(),
                    };
                }

                var provided = default(PCGParameterValue);
                bool overridden = overrides != null && overrides.TryGetValue(definition.Name, out provided);
                object value;

                if (overridden)
                {
                    value = provided.ToObject(definition.Type);
                }
                else
                {
                    // 图的默认值只填**空着**的槽位：写死的值是这张图的作者做的决定，
                    // 默认值不能悄悄把它换掉。
                    bool slotHasValue = definition.ValueIndex >= 0 && definition.ValueIndex < target.Values.Length &&
                                        target.Values[definition.ValueIndex] != null;
                    if (slotHasValue)
                    {
                        // 这个槽位没什么可叠的，但要把节点留在"改过"的集合里：
                        // 另一个暴露参数可能正是冲着它来的。
                        continue;
                    }

                    value = definition.DefaultValue.ToObject(definition.Type);
                }

                target.Values = EnsureLength(target.Values, definition.ValueIndex + 1);
                target.Values[definition.ValueIndex] = value;
                modifiedNodes[definition.NodeIndex] = target;
            }

            if (hasOverrides)
                ReportUnknownOverrides(graph, exposed, overrides, warnings);

            if (modifiedNodes.Count == 0)
                return Reuse(compiled, warnings);

            var nodes = new PCGNodeBase[nodeCount];
            for (int i = 0; i < nodeCount; i++)
            {
                if (!modifiedNodes.TryGetValue(i, out var replacement))
                {
                    nodes[i] = compiled.Nodes[i];
                    continue;
                }

                var instance = PCGNodeRegistry.CreateNode(replacement, i, out string error);
                if (instance == null)
                {
                    // 回退到编译好的实例能让这次运行活下去，但设计者必须看见
                    // 自己那条覆盖没有生效。
                    warnings.Add($"Node {i} ('{replacement.TypeId}') could not be rebuilt with overridden " +
                                 $"parameters: {error}. Using the authored values instead.");
                    instance = compiled.Nodes[i];
                }

                nodes[i] = instance;
            }

            return new PCGExecutionPlan { Nodes = nodes, Warnings = warnings.ToArray() };
        }

        private static PCGExecutionPlan Reuse(PCGCompiledGraph compiled, List<string> warnings)
        {
            return new PCGExecutionPlan
            {
                Nodes = compiled.Nodes,
                Warnings = warnings.ToArray(),
                ReusedCompiledNodes = true,
            };
        }

        private static void ReportUnknownOverrides(
            PCGGraph graph, PCGParameterDefinition[] exposed,
            IReadOnlyDictionary<string, PCGParameterValue> overrides, List<string> warnings)
        {
            // 指不到任何参数的覆盖原本会是一次静默的空操作：一个体积，它的参数在图里被改了名，
            // 就会继续按写死的默认值生成，看起来像是生成器坏了。
            foreach (var kv in overrides)
            {
                bool known = false;
                for (int i = 0; exposed != null && i < exposed.Length; i++)
                {
                    if (exposed[i] != null && string.Equals(exposed[i].Name, kv.Key, StringComparison.Ordinal))
                    {
                        known = true;
                        break;
                    }
                }

                if (!known)
                {
                    warnings.Add($"Override '{kv.Key}' does not match any exposed parameter of graph " +
                                 $"'{graph?.Name}'; ignored. Mark the parameter as exposed in the graph first.");
                }
            }
        }

        private static object[] EnsureLength(object[] values, int length)
        {
            if (length <= 0)
                length = 1;

            if (values != null && values.Length >= length)
                return values;

            var grown = new object[length];
            if (values != null)
                Array.Copy(values, 0, grown, 0, values.Length);

            return grown;
        }
    }
}

// PCG 插件 - 图剪贴板与批量节点编辑
// FlaxEngine PCG 插件的一部分。见 docs/PCG_PLUGIN_DESIGN.md §6.2。

using System;
using System.Collections.Generic;
using FlaxEngine;
using PCG.Execution;

namespace PCG.Graph
{
    /// <summary>
    /// 图的一部分的脱离副本，用于贴回同一张图或另一张图。
    /// </summary>
    public sealed class PCGGraphCopy
    {
        /// <summary>被复制的节点，按选中的顺序，带着各自的值数组。</summary>
        internal readonly List<PCGNode> Nodes = new List<PCGNode>();

        /// <summary>两端都在被复制集合内的边，按副本表示。</summary>
        internal readonly List<PCGEdge> Edges = new List<PCGEdge>();

        /// <summary>属于被复制节点的已暴露参数，按副本表示。</summary>
        internal readonly List<PCGParameterDefinition> Exposed = new List<PCGParameterDefinition>();

        /// <summary>因为另一端没被选中而被丢掉的边数。</summary>
        public int DroppedEdgeCount { get; internal set; }

        /// <summary>副本里的节点数量。</summary>
        public int NodeCount => Nodes.Count;

        /// <summary>副本空得不值得贴回去时为正。</summary>
        public bool IsEmpty => Nodes.Count == 0;
    }

    /// <summary>
    /// 在 <see cref="PCGGraph"/> 数据上做复制、粘贴与批量参数编辑。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 这些运算都是纯粹的图算术——下标重映射、边过滤、名字唯一化——所以
    /// 它们住在运行时程序集而不是编辑器里，在那里不需要画布就能断言。
    /// 编辑器的职责是交出选中的下标并应用结果；关于一次粘贴
    /// 意味着什么的规则，在这里定一次。
    /// </para>
    /// <para>
    /// <b>一次粘贴要么全成、要么全不动。</b>贴了一半的簇是最坏的结果，因为活下来的
    /// 那几块看上去就像一次完成了的编辑：一份复制来的子图若引用了这个项目没注册
    /// 的类型，会按名字被拒绝，图因此分毫不动。
    /// </para>
    /// </remarks>
    public static class PCGGraphClipboard
    {
        /// <summary>默认的粘贴偏移，好让贴进去的节点不至于正好压在原件上面。</summary>
        public static readonly Vector2 DefaultPasteOffset = new Vector2(40.0f, 40.0f);

        /// <summary>
        /// 复制给定的那些节点，只带走它们彼此之间的连接。
        /// </summary>
        /// <param name="graph">来源图。</param>
        /// <param name="nodeIndices">选中的节点下标；顺序保留。</param>
        public static PCGGraphCopy Copy(PCGGraph graph, IEnumerable<int> nodeIndices)
        {
            var copy = new PCGGraphCopy();
            if (graph?.Nodes == null || nodeIndices == null)
                return copy;

            // 选中顺序要保留，但边过滤需要的是"属不属于"，而设计者的
            // 框选可以给出任意顺序。
            var positionInCopy = new Dictionary<int, int>();

            foreach (int index in nodeIndices)
            {
                if (index < 0 || index >= graph.Nodes.Length || positionInCopy.ContainsKey(index))
                    continue;

                var source = graph.Nodes[index];
                if (source == null)
                    continue;

                positionInCopy[index] = copy.Nodes.Count;
                copy.Nodes.Add(CloneNode(source));
            }

            var edges = graph.Edges;
            if (edges != null)
            {
                for (int i = 0; i < edges.Length; i++)
                {
                    var edge = edges[i];
                    if (edge == null)
                        continue;

                    bool from = positionInCopy.TryGetValue(edge.OutputNodeIndex, out int sourceIndex);
                    bool to = positionInCopy.TryGetValue(edge.InputNodeIndex, out int targetIndex);

                    if (from && to)
                    {
                        copy.Edges.Add(new PCGEdge
                        {
                            OutputNodeIndex = sourceIndex,
                            OutputPinIndex = edge.OutputPinIndex,
                            InputNodeIndex = targetIndex,
                            InputPinIndex = edge.InputPinIndex,
                        });
                        continue;
                    }

                    // 连向选中集合之外的东西没法贴——另一端不跟着来——
                    // 但静静地丢掉它会让这份副本看上去是完整的，所以要计数。
                    if (from || to)
                        copy.DroppedEdgeCount++;
                }
            }

            var exposed = graph.ExposedParameters;
            for (int i = 0; exposed != null && i < exposed.Length; i++)
            {
                var definition = exposed[i];
                if (definition == null)
                    continue;

                if (positionInCopy.TryGetValue(definition.NodeIndex, out int copied))
                {
                    var clone = CloneDefinition(definition);
                    clone.NodeIndex = copied;
                    copy.Exposed.Add(clone);
                }
            }

            return copy;
        }

        /// <summary>
        /// 把一份副本追加到一张图上。
        /// </summary>
        /// <param name="graph">要贴进去的图；可以和副本的来源是同一张。</param>
        /// <param name="copy">来自 <see cref="Copy"/>。</param>
        /// <param name="offset">加到每个贴入节点的画布位置上。</param>
        /// <param name="newIndices">这次粘贴新建的节点下标，按副本顺序。</param>
        /// <param name="error">为什么什么都没贴进去。</param>
        /// <returns>粘贴真的发生时为真。</returns>
        public static bool Paste(PCGGraph graph, PCGGraphCopy copy, Vector2 offset, out int[] newIndices, out string error)
        {
            newIndices = Array.Empty<int>();
            error = null;

            if (graph == null)
            {
                error = "No graph to paste into.";
                return false;
            }

            if (copy == null || copy.IsEmpty)
            {
                error = "Nothing to paste.";
                return false;
            }

            // 在动任何东西之前先检查：贴一个类型缺失的簇，否则会造出
            // 编译成错误、却看上去像一次完成了的编辑的那些节点。
            var unknown = new List<string>();
            for (int i = 0; i < copy.Nodes.Count; i++)
            {
                string typeId = copy.Nodes[i].TypeId;
                if (IsRegistered(typeId) || unknown.Contains(typeId ?? "(null)"))
                    continue;

                unknown.Add(typeId ?? "(null)");
            }

            if (unknown.Count > 0)
            {
                error = $"This graph does not know the node type(s) {string.Join(", ", unknown)}; " +
                        "the paste was cancelled rather than left half-finished.";
                return false;
            }

            int first = graph.Nodes?.Length ?? 0;
            var nodes = new PCGNode[first + copy.Nodes.Count];
            for (int i = 0; i < first; i++)
                nodes[i] = graph.Nodes[i];

            for (int i = 0; i < copy.Nodes.Count; i++)
            {
                var pasted = CloneNode(copy.Nodes[i]);
                pasted.EditorPosition += offset;
                nodes[first + i] = pasted;
            }

            graph.Nodes = nodes;

            var existing = graph.Edges ?? Array.Empty<PCGEdge>();
            var edges = new PCGEdge[existing.Length + copy.Edges.Count];
            for (int i = 0; i < existing.Length; i++)
                edges[i] = existing[i];

            for (int i = 0; i < copy.Edges.Count; i++)
            {
                var edge = copy.Edges[i];
                edges[existing.Length + i] = new PCGEdge
                {
                    OutputNodeIndex = first + edge.OutputNodeIndex,
                    OutputPinIndex = edge.OutputPinIndex,
                    InputNodeIndex = first + edge.InputNodeIndex,
                    InputPinIndex = edge.InputPinIndex,
                };
            }

            graph.Edges = edges;

            // 已暴露的参数在副本上仍然保持暴露，但它们的名字必须在图内保持唯一——
            // 正是这份唯一性让某个体积的覆盖表能指到唯一一个参数。
            var exposed = new List<PCGParameterDefinition>(graph.ExposedParameters ?? Array.Empty<PCGParameterDefinition>());
            int renamed = 0;

            for (int i = 0; i < copy.Exposed.Count; i++)
            {
                var definition = CloneDefinition(copy.Exposed[i]);
                definition.NodeIndex = first + definition.NodeIndex;
                definition.Name = UniqueName(exposed, definition.Name);
                exposed.Add(definition);
                renamed++;
            }

            graph.ExposedParameters = exposed.ToArray();

            newIndices = new int[copy.Nodes.Count];
            for (int i = 0; i < newIndices.Length; i++)
                newIndices[i] = first + i;

            return true;
        }

        /// <summary>
        /// 一步完成复制再粘贴，也就是编辑器的"重复"操作。
        /// </summary>
        public static bool Duplicate(
            PCGGraph graph, IEnumerable<int> nodeIndices, Vector2 offset, out int[] newIndices, out string error)
        {
            return Paste(graph, Copy(graph, nodeIndices), offset, out newIndices, out error);
        }

        /// <summary>
        /// 按 schema 名字定位一个参数，并在每个拥有它的给定节点上设置它。
        /// </summary>
        /// <param name="graph">持有这些节点的图。</param>
        /// <param name="nodeIndices">选中的节点下标。</param>
        /// <param name="parameterName">schema 名字，按每个节点自己的类型解析。</param>
        /// <param name="value">要写入的值。</param>
        /// <param name="changed">有多少节点有这个参数并接受了这个值。</param>
        /// <param name="skipped">没有那个参数的节点，或种类拒绝该值的节点。</param>
        /// <param name="error">在什么都改不了时设置，此时图分毫不动。</param>
        /// <remarks>
        /// <para>
        /// 按名字而不是按槽位下标定位，才让一次批量编辑在不同节点类型之间
        /// 说的是同一件事："Density Threshold" 在两个采样器上是槽位 1、在另一个上是槽位 2，
        /// 而混合选中的设计者指的是那个具名控件，不是一个下标。
        /// </para>
        /// <para>
        /// 值在写入前先对照每个节点自己的 schema 检查，于是一个被拒绝的种类
        /// 不会落到选择的一半上、让其余那半看上去已经应用了。
        /// </para>
        /// </remarks>
        public static bool SetParameterOnNodes(
            PCGGraph graph, IEnumerable<int> nodeIndices, string parameterName, object value,
            out int changed, out List<string> skipped, out string error)
        {
            changed = 0;
            skipped = new List<string>();
            error = null;

            if (graph?.Nodes == null || string.IsNullOrEmpty(parameterName))
            {
                error = "Nothing selected, or no parameter named.";
                return false;
            }

            var targets = new List<(int index, int slot, PCGNode node)>();

            foreach (int index in nodeIndices ?? Array.Empty<int>())
            {
                if (index < 0 || index >= graph.Nodes.Length)
                {
                    skipped.Add($"node {index}: outside the graph");
                    continue;
                }

                var node = graph.Nodes[index];
                if (node == null)
                {
                    skipped.Add($"node {index}: empty slot");
                    continue;
                }

                int slot = FindSlot(node.TypeId, parameterName, out string reason);
                if (slot < 0)
                {
                    skipped.Add($"node {index} ({node.TypeId}): {reason}");
                    continue;
                }

                if (!TryGetKind(node.TypeId, parameterName, out PCGParameterType kind) || !KindAccepts(kind, value))
                {
                    skipped.Add($"node {index} ({node.TypeId}): {parameterName} does not accept that value type");
                    continue;
                }

                targets.Add((index, slot, node));
            }

            if (targets.Count == 0)
            {
                error = skipped.Count > 0
                    ? $"No selected node accepts \"{parameterName}\": {skipped[0]}"
                    : $"No selected node accepts \"{parameterName}\".";
                return false;
            }

            foreach (var target in targets)
                WriteSlot(target.node, target.slot, value);

            changed = targets.Count;
            return true;
        }

        /// <summary>
        /// 给定的每一个节点都有的参数名，按它们在第一个节点上出现的顺序。
        /// </summary>
        /// <remarks>
        /// 只有当那个参数在整个选择上都存在时，一个批量控件才算诚实：给一份
        /// 含过滤器的混合提供 "Point Count"，要么静静地跳过节点，要么往一个
        /// 在那里意味着别的东西的槽位里写。类型没注册的节点会让结果为空而不是猜，
        /// 因为没有 schema 可拿来求交集。
        /// </remarks>
        public static List<string> CommonParameterNames(PCGGraph graph, IEnumerable<int> nodeIndices)
        {
            var names = new List<string>();
            if (graph?.Nodes == null || nodeIndices == null)
                return names;

            var schemas = new List<PCGNodeParameterInfo[]>();

            foreach (int index in nodeIndices)
            {
                if (index < 0 || index >= graph.Nodes.Length)
                    continue;

                var node = graph.Nodes[index];
                if (node == null)
                    continue;

                var schema = SchemaOf(node.TypeId);
                if (schema == null || schema.Length == 0)
                {
                    names.Clear();
                    return names;
                }

                schemas.Add(schema);
            }

            if (schemas.Count == 0)
                return names;

            for (int i = 0; i < schemas[0].Length; i++)
            {
                string name = schemas[0][i]?.Name;
                if (string.IsNullOrEmpty(name))
                    continue;

                bool inAll = true;
                for (int n = 1; n < schemas.Count; n++)
                {
                    if (FindSlotIn(schemas[n], name) < 0)
                    {
                        inAll = false;
                        break;
                    }
                }

                if (inAll && !names.Contains(name))
                    names.Add(name);
            }

            return names;
        }

        /// <summary>
        /// 某个共享参数的 schema 种类，取自第一个带有它的被选节点。
        /// </summary>
        /// <remarks>
        /// 编辑器需要在批量写入之前把输入的文字变成值，所以一次批量编辑不许
        /// 落成一个类型错的值、然后被图文件永远带着。
        /// </remarks>
        public static bool TryGetParameterKind(
            PCGGraph graph, IEnumerable<int> nodeIndices, string parameterName, out PCGParameterType kind)
        {
            kind = default;

            if (graph?.Nodes == null || string.IsNullOrEmpty(parameterName) || nodeIndices == null)
                return false;

            foreach (int index in nodeIndices)
            {
                if (index < 0 || index >= graph.Nodes.Length)
                    continue;

                var node = graph.Nodes[index];
                if (node == null)
                    continue;

                if (FindSlot(node.TypeId, parameterName, out _) < 0)
                    continue;

                return TryGetKind(node.TypeId, parameterName, out kind);
            }

            return false;
        }

        private static int FindSlotIn(PCGNodeParameterInfo[] schema, string name)
        {
            for (int i = 0; i < schema.Length; i++)
            {
                if (string.Equals(schema[i]?.Name, name, StringComparison.Ordinal))
                    return i;
            }

            return -1;
        }

        // ---- 助手 --------------------------------------------------------

        private static PCGNode CloneNode(PCGNode source) => new PCGNode
        {
            TypeId = source.TypeId,
            // 共享的数组会让副本与原件变成同一个值：编辑一个贴出来的节点，会连带
            // 挪动它被复制来源的那个节点。
            Values = (object[])(source.Values ?? Array.Empty<object>()).Clone(),
            EditorPosition = source.EditorPosition,
            Comment = source.Comment,
            Disabled = source.Disabled,
        };

        private static PCGParameterDefinition CloneDefinition(PCGParameterDefinition source) => new PCGParameterDefinition
        {
            Name = source.Name,
            DisplayName = source.DisplayName,
            Type = source.Type,
            DefaultValue = source.DefaultValue,
            NodeIndex = source.NodeIndex,
        };

        private static string UniqueName(List<PCGParameterDefinition> existing, string name)
        {
            bool Taken(string candidate)
            {
                for (int i = 0; i < existing.Count; i++)
                {
                    if (string.Equals(existing[i]?.Name, candidate, StringComparison.Ordinal))
                        return true;
                }

                return false;
            }

            if (!Taken(name))
                return name;

            for (int suffix = 2; suffix < 1000; suffix++)
            {
                string candidate = $"{name}#{suffix}";
                if (!Taken(candidate))
                    return candidate;
            }

            return $"{name}#{Guid.NewGuid():N}";
        }

        private static bool IsRegistered(string typeId)
        {
            if (string.IsNullOrEmpty(typeId))
                return false;

            foreach (var info in PCGNodeRegistry.All)
            {
                if (string.Equals(info.TypeId, typeId, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private static PCGNodeParameterInfo[] SchemaOf(string typeId)
        {
            foreach (var info in PCGNodeRegistry.All)
            {
                if (string.Equals(info.TypeId, typeId, StringComparison.Ordinal))
                    return info.Parameters;
            }

            return null;
        }

        private static int FindSlot(string typeId, string parameterName, out string reason)
        {
            reason = null;

            var schema = SchemaOf(typeId);
            if (schema == null)
            {
                reason = "the node type is not registered";
                return -1;
            }

            for (int i = 0; i < schema.Length; i++)
            {
                if (string.Equals(schema[i]?.Name, parameterName, StringComparison.Ordinal))
                    return i;
            }

            reason = $"has no parameter \"{parameterName}\"";
            return -1;
        }

        private static bool TryGetKind(string typeId, string parameterName, out PCGParameterType kind)
        {
            var schema = SchemaOf(typeId);
            kind = default;

            if (schema == null)
                return false;

            for (int i = 0; i < schema.Length; i++)
            {
                if (string.Equals(schema[i]?.Name, parameterName, StringComparison.Ordinal))
                {
                    kind = schema[i].Kind;
                    return true;
                }
            }

            return false;
        }

        private static bool KindAccepts(PCGParameterType kind, object value)
        {
            switch (kind)
            {
            case PCGParameterType.Float: return value is float || value is double;
            case PCGParameterType.Int:
            case PCGParameterType.Enum: return value is int || value is long;
            case PCGParameterType.Bool: return value is bool;
            case PCGParameterType.Vector: return value is Vector3 || value is Float3;
            case PCGParameterType.Color: return value is Color || value is Color32;
            case PCGParameterType.Asset: return value is Guid;
            case PCGParameterType.String: return value is string;
            default: return false;
            }
        }

        private static void WriteSlot(PCGNode node, int slot, object value)
        {
            var values = node.Values ?? Array.Empty<object>();

            if (slot >= values.Length)
            {
                // 节点类型增加参数时，节点可能带着一个较短的数组，所以一次批量编辑是把
                // 它撑长，而不是丢弃这次写入。
                Array.Resize(ref values, slot + 1);
                node.Values = values;
            }

            values[slot] = value;
        }
    }
}

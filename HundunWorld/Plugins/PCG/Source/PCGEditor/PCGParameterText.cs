// PCG Plugin - Parameter text conversion for the graph editor
// Part of the FlaxEngine PCG plugin. See docs/PCG_PLUGIN_DESIGN.md §6.2.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using FlaxEngine;
using PCG.Execution;
using PCG.Graph;

namespace PCGEditor
{
    /// <summary>
    /// Converts between a parameter slot's authored value and the text a field shows.
    /// </summary>
    /// <remarks>
    /// Split out from the panel because this is where the boxed runtime type is decided, and a slot that
    /// comes back as the wrong type is silently ignored by the node's own reader (it falls back to its
    /// default). A panel bug would therefore look like "the graph ignores my numbers" - so the conversion is
    /// plain data and gets executed by the headless harness rather than only by an open editor.
    /// </remarks>
    internal static class PCGParameterText
    {
        /// <summary>The label shown beside a slot's fields, naming the options for enum kinds.</summary>
        public static string LabelFor(PCGNodeParameterInfo parameter)
        {
            if (parameter == null)
                return "参数";

            // 显示名走中文表（键保持英文）：表在本目录的 PCGParameterLabels 里，覆盖率由离屏断言遍历
            // 注册表里每个槽位与每个选项来钉，所以"某个参数还写着英文"漏不过去。
            string label = PCGParameterLabels.Label(parameter.Name);

            if (parameter.Kind != PCGParameterType.Enum || parameter.Options == null || parameter.Options.Length == 0)
                return label;

            var builder = new StringBuilder();
            builder.Append(label).Append("（");

            for (int i = 0; i < parameter.Options.Length; i++)
            {
                if (i > 0)
                    builder.Append("、");

                builder.Append(i).Append('=').Append(PCGParameterLabels.Label(parameter.Options[i]));
            } 

            builder.Append('）');
            return builder.ToString();
        }

        /// <summary>一个参数该用哪种编辑控件。</summary>
        /// <remarks>
        /// 抽成纯判定是为了能离线断言：编辑器看不见，"资产槽位给成了让人手打 GUID 的文本框"这种缺陷
        /// 只能靠眼睛发现，而它恰恰是设计者最先碰到的一道墙（一个 32 位十六进制串，还得自己去找）。
        /// </remarks>
        public enum PCGParameterEditorKind
        {
            /// <summary>一个（或几个）文本框：数字、开关、字符串。</summary>
            Text,

            /// <summary>资产选择器：可筛选、带搜索弹窗，值是一个资产 id。</summary>
            Asset,

            /// <summary>下拉框：从写好的选项里挑一个（值是选项序号）。</summary>
            Combo,

            /// <summary>路径选择器：挑一个资产文件，但槽位里存的是**路径字符串**（不是资产 id）。</summary>
            PathPicker,
        }

        /// <summary>
        /// 值就是"内容路径"的字符串槽位。
        /// </summary>
        /// <remarks>
        /// 这几个槽位存的是路径而不是资产 id：生成器在运行期按路径解析资产（这样同一份图能被不同的工程用），
        /// 子图按路径找另一张图文件。它们的共同点是——**手打一个路径和手打一个 GUID 一样不可接受**，
        /// 所以它们也给选择器，只是写回的是路径。名字写死在这里是有意的：认的是"这个槽位叫什么"，
        /// 而不是"它是字符串"（`Head`、`Rules`、`Productions` 这些也是字符串，但它们不是路径）。
        /// </remarks>
        public static bool IsContentPathSlot(string slotName)
        {
            switch (slotName)
            {
            case "Model Path":
            case "Texture Path":
            case "Child Graph":
                return true;
            default:
                return false;
            }
        }

        /// <summary>按槽位名与参数种类挑编辑控件。</summary>
        public static PCGParameterEditorKind EditorKindOf(string slotName, PCGParameterType kind)
        {
            if (IsContentPathSlot(slotName))
                return PCGParameterEditorKind.PathPicker;

            switch (kind)
            {
            case PCGParameterType.Asset: return PCGParameterEditorKind.Asset;
            case PCGParameterType.Enum: return PCGParameterEditorKind.Combo;
            default: return PCGParameterEditorKind.Text;
            }
        }

        /// <summary>
        /// 路径选择器交回来的路径规整一下：去掉首尾空白、统一成正斜杠。
        /// </summary>
        /// <remarks>
        /// 引擎给的是工程相对路径（`Content/...`），正是这些槽位要的形式；这里只做两件不会改变语义的事，
        /// 免得把 Windows 上偶尔出现的反斜杠写进图文件、换台机器就解析不到。
        /// </remarks>
        public static string NormalizeContentPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;

            return path.Trim().Replace('\\', '/');
        }

        /// <summary>Number of text fields a kind needs (one per component).</summary>
        public static int ComponentCount(PCGParameterType kind)
        {
            switch (kind)
            {
            case PCGParameterType.Vector: return 3;
            case PCGParameterType.Color: return 4;
            default: return 1;
            }
        }

        /// <summary>Renders a stored value into the text its fields show.</summary>
        public static string[] Components(PCGParameterType kind, object value)
        {
            switch (kind)
            {
            case PCGParameterType.Vector:
            {
                var v = AsVector(value);
                return new[] { Number(v.X), Number(v.Y), Number(v.Z) };
            }
            case PCGParameterType.Color:
            {
                var c = AsColor(value);
                return new[] { Number(c.R), Number(c.G), Number(c.B), Number(c.A) };
            }
            case PCGParameterType.Bool:
                return new[] { AsBool(value) ? "true" : "false" };
            case PCGParameterType.Int:
            case PCGParameterType.Enum:
                return new[] { AsLong(value).ToString(CultureInfo.InvariantCulture) };
            case PCGParameterType.Asset:
            {
                var guid = AsGuid(value);
                return new[] { guid == Guid.Empty ? string.Empty : guid.ToString("N") };
            }
            case PCGParameterType.Float:
                return new[] { Number(AsDouble(value)) };
            default:
                return new[] { value as string ?? string.Empty };
            }
        }

        /// <summary>
        /// Parses field text back into a boxed value of the kind's runtime type.
        /// </summary>
        /// <param name="kind">Slot kind, from the schema.</param>
        /// <param name="texts">Field text, <see cref="ComponentCount"/> entries.</param>
        /// <param name="value">The boxed value: float, int, bool, Vector3, Color, Guid or string.</param>
        /// <param name="error">Which field is wrong, and what was expected.</param>
        public static bool TryParse(PCGParameterType kind, string[] texts, out object value, out string error)
        {
            value = null;
            error = null;

            int needed = ComponentCount(kind);
            if (texts == null || texts.Length < needed)
            {
                error = $"需要 {needed} 个输入框的值。";
                return false;
            }

            switch (kind)
            {
            case PCGParameterType.Float:
                if (!TryFloat(texts[0], out float f, out error))
                    return false;
                value = f;
                return true;

            case PCGParameterType.Int:
                if (!TryFloat(texts[0], out float iRaw, out error))
                    return false;
                // Rounded, because a field holding "3" parses as a float and the slot wants a boxed int.
                value = (int)MathF.Round(iRaw);
                return true;

            case PCGParameterType.Bool:
                value = ParseBool(texts[0]);
                return true;

            case PCGParameterType.Enum:
            {
                if (!TryFloat(texts[0], out float option, out error))
                    return false;

                int index = (int)MathF.Round(option);
                if (index < 0)
                {
                    error = "选项索引不能是负数。";
                    return false;
                }

                value = index;
                return true;
            }

            case PCGParameterType.Vector:
            {
                if (!TryFloat(texts[0], out float x, out error) ||
                    !TryFloat(texts[1], out float y, out error) ||
                    !TryFloat(texts[2], out float z, out error))
                    return false;

                value = new Vector3(x, y, z);
                return true;
            }

            case PCGParameterType.Color:
            {
                if (!TryFloat(texts[0], out float r, out error) ||
                    !TryFloat(texts[1], out float g, out error) ||
                    !TryFloat(texts[2], out float b, out error) ||
                    !TryFloat(texts[3], out float a, out error))
                    return false;

                value = new Color(r, g, b, a);
                return true;
            }

            case PCGParameterType.Asset:
            {
                var text = (texts[0] ?? string.Empty).Trim();
                if (text.Length == 0)
                {
                    value = Guid.Empty;
                    return true;
                }

                if (!Guid.TryParse(text, out var guid))
                {
                    // Asset pickers hand over a path; accept the two forms the editor can produce so the
                    // field is not a dead end for someone pasting from the Content Browser.
                    if (Guid.TryParse(text.Replace("Content/", string.Empty).Replace('.', '_'), out guid))
                    {
                        value = guid;
                        return true;
                    }

                    error = "这里需要资产的 Guid（32 位十六进制），留空表示不引用。";
                    return false;
                }

                value = guid;
                return true;
            }

            default:
                value = texts[0] ?? string.Empty;
                return true;
            }
        }

        /// <summary>
        /// Merges parsed component values into an existing value array without shrinking it.
        /// </summary>
        /// <remarks>
        /// Node parameter arrays are positional and a node may already have more slots than the schema
        /// describes; writing one slot must not truncate the rest of the authored graph.
        /// </remarks>
        public static object[] WithValue(object[] values, int index, object value)
        {
            int length = Math.Max(index + 1, values?.Length ?? 0);
            var result = new object[length];

            if (values != null)
                Array.Copy(values, 0, result, 0, Math.Min(values.Length, length));

            result[index] = value;
            return result;
        }

        private static bool TryFloat(string text, out float value, out string error)
        {
            value = 0.0f;

            if (string.IsNullOrWhiteSpace(text))
            {
                // An empty field is how a designer starts typing; treat it as zero rather than an error, so
                // a mid-edit panel does not flash red on every keystroke.
                error = null;
                return true;
            }

            if (float.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                error = null;
                return true;
            }

            error = $"“{text}” 不是数字。";
            return false;
        }

        private static bool ParseBool(string text)
        {
            var trimmed = (text ?? string.Empty).Trim();

            return string.Equals(trimmed, "true", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(trimmed, "1", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(trimmed, "yes", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(trimmed, "on", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// An exposed parameter remembered across a canvas edit session, keyed by node id.
        /// </summary>
        /// <remarks>
        /// A graph stores exposed parameters by node <em>index</em>. Indices shift whenever a node is added or
        /// deleted, so saving the canvas would silently retarget an exposed parameter onto whatever node ended
        /// up in that slot; the ids are stable, and the indices are recomputed from them at save time.
        /// </remarks>
        internal readonly struct TrackedExposed
        {
            public readonly uint NodeID;
            public readonly int ValueIndex;
            public readonly string Name;
            public readonly string DisplayName;
            public readonly PCGParameterType Kind;
            public readonly object DefaultValue;

            public TrackedExposed(uint nodeID, int valueIndex, string name, string displayName,
                PCGParameterType kind, object defaultValue)
            {
                NodeID = nodeID;
                ValueIndex = valueIndex;
                Name = name;
                DisplayName = displayName;
                Kind = kind;
                DefaultValue = defaultValue;
            }
        }

        /// <summary>
        /// Turns tracked exposed parameters back into the indexed form a graph stores.
        /// </summary>
        /// <param name="tracked">The tracked entries, in the order they were added.</param>
        /// <param name="nodeIDsByIndex">Node id per canvas position, index for index.</param>
        /// <remarks>
        /// An entry whose node has disappeared is dropped rather than renumbered onto a neighbour: the worst a
        /// designer can then see is the override list complaining that a parameter is gone, which is the truth,
        /// whereas retargeting it would quietly make one node's knob drive another's parameter.
        /// </remarks>
        public static PCGParameterDefinition[] MaterializeExposed(
            IList<TrackedExposed> tracked, IList<uint> nodeIDsByIndex)
        {
            if (tracked == null || tracked.Count == 0 || nodeIDsByIndex == null || nodeIDsByIndex.Count == 0)
                return Array.Empty<PCGParameterDefinition>();

            var indexByNodeID = new Dictionary<uint, int>(nodeIDsByIndex.Count * 2);
            for (int i = 0; i < nodeIDsByIndex.Count; i++)
            {
                if (!indexByNodeID.ContainsKey(nodeIDsByIndex[i]))
                    indexByNodeID.Add(nodeIDsByIndex[i], i);
            }

            var result = new List<PCGParameterDefinition>(tracked.Count);

            for (int i = 0; i < tracked.Count; i++)
            {
                var entry = tracked[i];
                if (!indexByNodeID.TryGetValue(entry.NodeID, out int nodeIndex))
                    continue;

                var definition = new PCGParameterDefinition
                {
                    Name = entry.Name,
                    DisplayName = entry.DisplayName,
                    Type = entry.Kind,
                    NodeIndex = nodeIndex,
                    ValueIndex = entry.ValueIndex,
                };

                definition.DefaultValue.TrySet(entry.Kind, entry.DefaultValue);
                result.Add(definition);
            }

            return result.ToArray();
        }

        /// <summary>A unique exposed-parameter name for a slot, stable enough to override from a volume.</summary>
        public static string ExposedName(string nodeTypeId, int valueIndex, PCGNodeParameterInfo parameter)
        {
            var slot = parameter != null && !string.IsNullOrEmpty(parameter.Name) ? parameter.Name : $"slot{valueIndex}";
            var cleaned = new StringBuilder();

            for (int i = 0; i < slot.Length; i++)
            {
                char c = slot[i];
                cleaned.Append(char.IsLetterOrDigit(c) ? c : '_');
            }

            return $"{nodeTypeId}_{cleaned}";
        }

        private static string Number(double value)
        {
            // float.PositiveInfinity renders as "∞" under "R", which no field can type back and
            // float.TryParse would not accept - and an unbounded height range is exactly what that renders.
            if (double.IsPositiveInfinity(value))
                return "Infinity";

            if (double.IsNegativeInfinity(value))
                return "-Infinity";

            if (double.IsNaN(value))
                return "NaN";

            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static double AsDouble(object value)
        {
            try
            {
                return value == null ? 0.0 : Convert.ToDouble(value, CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                return 0.0;
            }
        }

        private static long AsLong(object value)
        {
            try
            {
                return value == null ? 0L : Convert.ToInt64(value, CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                return 0L;
            }
        }

        private static bool AsBool(object value)
        {
            if (value == null)
                return false;

            if (value is bool flag)
                return flag;

            return AsLong(value) != 0 || Math.Abs(AsDouble(value)) > 0.0;
        }

        private static Guid AsGuid(object value)
        {
            switch (value)
            {
            case Guid guid: return guid;
            case string text when Guid.TryParse(text, out var parsed): return parsed;
            default: return Guid.Empty;
            }
        }

        private static Vector3 AsVector(object value)
        {
            switch (value)
            {
            case Vector3 v: return v;
            case Float3 f: return new Vector3(f.X, f.Y, f.Z);
            default: return Vector3.Zero;
            }
        }

        private static Color AsColor(object value)
        {
            return value is Color c ? c : new Color();
        }
    }
}

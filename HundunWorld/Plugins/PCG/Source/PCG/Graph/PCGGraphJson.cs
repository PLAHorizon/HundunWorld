// PCG Plugin - Graph JSON codec
// Part of the FlaxEngine PCG plugin. See docs/PCG_PLUGIN_DESIGN.md §3.3 note 4.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FlaxEngine;
using PCG.Execution;

namespace PCG.Graph
{
    /// <summary>
    /// Explicit, version-tagged JSON for a <see cref="PCGGraph"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Why not hand the graph to Flax's own asset serializer: a graph is a tree of polymorphic <c>object</c>
    /// parameter values (float, int, bool, Vector3, Guid, string, an enum option index), and whether each one
    /// survives with its original boxed type is an engine-implementation detail. The nodes read their slots
    /// with a fallback on any type mismatch, so a value that comes back as a <c>double</c> where a
    /// <c>float</c> was authored is still accepted - while one that comes back as a string is silently
    /// dropped and the graph runs on defaults. A codec owned by the plugin makes the stored shape explicit
    /// and testable without an engine.
    /// </para>
    /// <para>
    /// The shape mirrors the parameter schema declared at registration: every value carries its kind, so a
    /// file cannot quietly change the type of a slot between sessions, and a reader can report the exact slot
    /// when a kind is unknown rather than guessing.
    /// </para>
    /// </remarks>
    public static class PCGGraphJson
    {
        /// <summary>Format version written into new files. Read side accepts anything at or below it.</summary>
        public const int CurrentFormat = 2;

        /// <summary>
        /// First format whose <see cref="PCGGraph.EditorData"/> is base64. Format 1 stored the same payload as
        /// UTF-8 text, which cannot represent arbitrary bytes - see <see cref="MigrateEditorData"/>.
        /// </summary>
        public const int Base64EditorDataFormat = 2;

        /// <summary>
        /// Packs the canvas payload for storage.
        /// </summary>
        /// <remarks>
        /// Base64, because the payload is whatever <c>VisjectSurfaceContext.Save</c> wrote through a
        /// <c>BinaryWriter</c>: node meta, floats, ints and length-prefixed strings in one binary stream. Text
        /// encodings cannot carry that losslessly, and a lossy payload is worse than none - it fails to parse on
        /// load, which is indistinguishable from a corrupt file.
        /// </remarks>
        public static string EncodeEditorData(byte[] bytes) =>
            bytes == null || bytes.Length == 0 ? string.Empty : Convert.ToBase64String(bytes);

        /// <summary>
        /// Unpacks a stored canvas payload.
        /// </summary>
        /// <returns>False when the text is not base64, with the reason in <paramref name="error"/>.</returns>
        public static bool TryDecodeEditorData(string text, out byte[] bytes, out string error)
        {
            bytes = Array.Empty<byte>();
            error = null;

            if (string.IsNullOrEmpty(text))
                return true;

            try
            {
                bytes = Convert.FromBase64String(text);
                return true;
            }
            catch (FormatException ex)
            {
                error = $"画布布局不是合法的 base64（{ex.Message}）";
                return false;
            }
        }

        /// <summary>
        /// Brings a payload written by an older format up to the current one.
        /// </summary>
        /// <remarks>
        /// Format 1 kept the binary payload in a UTF-8 string. Decoding arbitrary bytes as UTF-8 replaces every
        /// invalid sequence with U+FFFD, and encoding the result back does not restore the original - the layout
        /// is gone by the time it reaches the file, which is why every v1 canvas reopened empty. A damaged
        /// payload is therefore <b>dropped</b> rather than migrated: the nodes and edges around it are intact, so
        /// the editor rebuilds the canvas from those and the next save writes a clean one. Keeping the damaged
        /// bytes would only make the loader throw again.
        /// </remarks>
        private static string MigrateEditorData(int format, string stored, out string note)
        {
            note = null;

            if (string.IsNullOrEmpty(stored) || format >= Base64EditorDataFormat)
                return stored ?? string.Empty;

            if (stored.IndexOf('\uFFFD') >= 0)
            {
                note = "画布布局是 v1 旧格式且已被 UTF-8 破坏（文件里有替换字符 U+FFFD），节点位置无法恢复；" +
                       "节点与连线完好，已按数据重建画布——保存一次就会写成新格式。";
                return string.Empty;
            }

            note = "画布布局来自 v1 旧格式，已转换为新的 base64 存储。";
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(stored));
        }

        // The document types are field-based (a value node is data, not an API), and System.Text.Json
        // ignores public fields unless told otherwise - without this switch everything serialised to "{}".
        private static readonly JsonSerializerOptions WriteOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            IncludeFields = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        };

        private static readonly JsonSerializerOptions ReadOptions = new JsonSerializerOptions
        {
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            IncludeFields = true,
        };

        /// <summary>Serialises a graph to indented JSON, suitable for version control.</summary>
        public static string ToJson(PCGGraph graph)
        {
            if (graph == null)
                throw new ArgumentNullException(nameof(graph));

            var document = new GraphDocument
            {
                Format = CurrentFormat,
                Name = graph.Name,
                EditorData = graph.EditorData,
                Nodes = ToNodes(graph.Nodes),
                Edges = ToEdges(graph.Edges),
                Exposed = ToExposed(graph.ExposedParameters),
                Settings = ToSettings(graph.Settings),
            };

            return JsonSerializer.Serialize(document, WriteOptions);
        }

        /// <summary>
        /// Parses a graph written by <see cref="ToJson"/>.
        /// </summary>
        /// <returns>False with a human-readable reason when the text is not a graph this build can run.</returns>
        public static bool TryParse(string json, out PCGGraph graph, out string error)
        {
            graph = null;
            error = null;

            if (string.IsNullOrWhiteSpace(json))
            {
                error = "The graph document is empty.";
                return false;
            }

            GraphDocument document;
            try
            {
                document = JsonSerializer.Deserialize<GraphDocument>(json, ReadOptions);
            }
            catch (Exception ex)
            {
                error = $"The graph document is not valid JSON: {ex.Message}";
                return false;
            }

            if (document == null)
            {
                error = "The graph document is empty.";
                return false;
            }

            if (document.Format <= 0)
            {
                error = "The graph document has no format version; it was not written by this codec.";
                return false;
            }

            if (document.Format > CurrentFormat)
            {
                error = $"The graph document is format v{document.Format}, but this PCG build reads up to v{CurrentFormat}. " +
                        "Update the plugin before opening it.";
                return false;
            }

            var result = new PCGGraph
            {
                Name = string.IsNullOrEmpty(document.Name) ? "PCG Graph" : document.Name,
                EditorData = MigrateEditorData(document.Format, document.EditorData, out string editorDataNote),
                EditorDataNote = editorDataNote,
                Nodes = FromNodes(document.Nodes, out string nodeError),
                Edges = FromEdges(document.Edges),
                ExposedParameters = FromExposed(document.Exposed),
                Settings = FromSettings(document.Settings),
            };

            if (nodeError != null)
            {
                error = nodeError;
                return false;
            }

            graph = result;
            return true;
        }

        /// <summary>True when the text looks like a PCG graph document, without fully parsing it.</summary>
        public static bool LooksLikeGraph(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return false;

            try
            {
                using var document = JsonDocument.Parse(json, new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true,
                });

                return document.RootElement.ValueKind == JsonValueKind.Object &&
                       document.RootElement.TryGetProperty(nameof(GraphDocument.Format), out var format) &&
                       format.ValueKind == JsonValueKind.Number;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // ---- mapping --------------------------------------------------------

        private static NodeRecord[] ToNodes(PCGNode[] nodes)
        {
            if (nodes == null || nodes.Length == 0)
                return Array.Empty<NodeRecord>();

            var records = new NodeRecord[nodes.Length];
            for (int i = 0; i < nodes.Length; i++)
            {
                var node = nodes[i];
                if (node == null)
                {
                    records[i] = null;
                    continue;
                }

                var values = new ValueRecord[node.Values?.Length ?? 0];
                for (int v = 0; v < values.Length; v++)
                    values[v] = ValueRecord.FromObject(node.Values[v]);

                records[i] = new NodeRecord
                {
                    TypeId = node.TypeId,
                    X = node.EditorPosition.X,
                    Y = node.EditorPosition.Y,
                    Comment = node.Comment,
                    Disabled = node.Disabled,
                    Values = values,
                };
            }

            return records;
        }

        private static PCGNode[] FromNodes(NodeRecord[] records, out string error)
        {
            error = null;

            if (records == null || records.Length == 0)
                return Array.Empty<PCGNode>();

            var nodes = new PCGNode[records.Length];
            for (int i = 0; i < records.Length; i++)
            {
                var record = records[i];
                if (record == null)
                {
                    error = $"Node entry {i} is null.";
                    return Array.Empty<PCGNode>();
                }

                if (string.IsNullOrEmpty(record.TypeId))
                {
                    error = $"Node entry {i} has no type id.";
                    return Array.Empty<PCGNode>();
                }

                var values = new object[record.Values?.Length ?? 0];
                for (int v = 0; v < values.Length; v++)
                {
                    var valueError = ValueRecord.ToObject(record.Values[v], out object value);
                    if (valueError != null)
                    {
                        error = $"Node {i} ('{record.TypeId}') slot {v}: {valueError}";
                        return Array.Empty<PCGNode>();
                    }

                    values[v] = value;
                }

                nodes[i] = new PCGNode
                {
                    TypeId = record.TypeId,
                    EditorPosition = new Vector2(record.X, record.Y),
                    Comment = record.Comment,
                    Disabled = record.Disabled,
                    Values = values,
                };
            }

            return nodes;
        }

        private static EdgeRecord[] ToEdges(PCGEdge[] edges)
        {
            if (edges == null || edges.Length == 0)
                return Array.Empty<EdgeRecord>();

            var records = new EdgeRecord[edges.Length];
            for (int i = 0; i < edges.Length; i++)
            {
                var edge = edges[i];
                records[i] = edge == null ? null : new EdgeRecord
                {
                    FromNode = edge.OutputNodeIndex,
                    FromPin = edge.OutputPinIndex,
                    ToNode = edge.InputNodeIndex,
                    ToPin = edge.InputPinIndex,
                };
            }

            return records;
        }

        private static PCGEdge[] FromEdges(EdgeRecord[] records)
        {
            if (records == null || records.Length == 0)
                return Array.Empty<PCGEdge>();

            var edges = new PCGEdge[records.Length];
            for (int i = 0; i < records.Length; i++)
            {
                var record = records[i];
                if (record == null)
                    continue;

                edges[i] = new PCGEdge
                {
                    OutputNodeIndex = record.FromNode,
                    OutputPinIndex = record.FromPin,
                    InputNodeIndex = record.ToNode,
                    InputPinIndex = record.ToPin,
                };
            }

            return edges;
        }

        private static ExposedRecord[] ToExposed(PCGParameterDefinition[] parameters)
        {
            if (parameters == null || parameters.Length == 0)
                return Array.Empty<ExposedRecord>();

            var records = new ExposedRecord[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                var parameter = parameters[i];
                records[i] = parameter == null ? null : new ExposedRecord
                {
                    Name = parameter.Name,
                    DisplayName = parameter.DisplayName,
                    Kind = parameter.Type.ToString(),
                    NodeIndex = parameter.NodeIndex,
                    ValueIndex = parameter.ValueIndex,
                    Default = ValueRecord.FromObject(parameter.DefaultValue.ToObject(parameter.Type)),
                };
            }

            return records;
        }

        private static PCGParameterDefinition[] FromExposed(ExposedRecord[] records)
        {
            if (records == null || records.Length == 0)
                return Array.Empty<PCGParameterDefinition>();

            var parameters = new PCGParameterDefinition[records.Length];
            for (int i = 0; i < records.Length; i++)
            {
                var record = records[i];
                if (record == null)
                    continue;

                // An unknown kind here means the file was written by a newer plugin; the value would be
                // unreadable, so the definition is dropped rather than guessed at.
                if (!Enum.TryParse(record.Kind, true, out PCGParameterType kind))
                    continue;

                ValueRecord.ToObject(record.Default, out object value);

                var definition = new PCGParameterDefinition
                {
                    Name = record.Name,
                    DisplayName = record.DisplayName,
                    Type = kind,
                    NodeIndex = record.NodeIndex,
                    ValueIndex = record.ValueIndex,
                };

                definition.DefaultValue.TrySet(kind, value);
                parameters[i] = definition;
            }

            return parameters;
        }

        private static SettingsRecord ToSettings(PCGGraphSettings settings)
        {
            settings ??= new PCGGraphSettings();

            return new SettingsRecord
            {
                DefaultSeed = settings.DefaultSeed,
                EnableDebugDraw = settings.EnableDebugDraw,
                MaxPointBudget = settings.MaxPointBudget,
                MaxExpandedNodeCount = settings.MaxExpandedNodeCount,
                Terrain = ToTerrainRecord(settings.Terrain),
            };
        }

        private static PCGGraphSettings FromSettings(SettingsRecord record)
        {
            if (record == null)
                return new PCGGraphSettings();

            return new PCGGraphSettings
            {
                DefaultSeed = record.DefaultSeed,
                EnableDebugDraw = record.EnableDebugDraw,
                MaxPointBudget = record.MaxPointBudget,
                MaxExpandedNodeCount = record.MaxExpandedNodeCount,
                Terrain = FromTerrainRecord(record.Terrain),
            };
        }

        private static TerrainRecord ToTerrainRecord(PCGTerrainRecipe terrain)
        {
            if (terrain == null)
                return null;

            var features = terrain.Features ?? Array.Empty<PCGTerrainFeature>();
            var records = new TerrainFeatureRecord[features.Length];

            for (int i = 0; i < features.Length; i++)
            {
                records[i] = new TerrainFeatureRecord
                {
                    Kind = (int)features[i].Shape,
                    X = features[i].Center.X,
                    Z = features[i].Center.Y,
                    Radius = features[i].Radius,
                    Height = features[i].Height,
                    Falloff = features[i].Falloff,
                };
            }

            return new TerrainRecord
            {
                NoiseAmplitude = terrain.NoiseAmplitude,
                NoiseScale = terrain.NoiseScale,
                Features = records,
                Holes = ToFeatureRecords(terrain.Holes),
            };
        }

        /// <summary>把一组形状特征转成文件里的记录（洞掩码与高度特征共用这一种记录）。</summary>
        private static TerrainFeatureRecord[] ToFeatureRecords(PCGTerrainFeature[] features)
        {
            if (features == null || features.Length == 0)
                return Array.Empty<TerrainFeatureRecord>();

            var records = new TerrainFeatureRecord[features.Length];

            for (int i = 0; i < features.Length; i++)
            {
                records[i] = new TerrainFeatureRecord
                {
                    Kind = (int)features[i].Shape,
                    X = features[i].Center.X,
                    Z = features[i].Center.Y,
                    Radius = features[i].Radius,
                    Height = features[i].Height,
                    Falloff = features[i].Falloff,
                };
            }

            return records;
        }

        /// <summary>从文件里的记录还原一组形状特征（洞掩码与高度特征共用）。</summary>
        private static PCGTerrainFeature[] FromFeatureRecords(TerrainFeatureRecord[] records)
        {
            if (records == null || records.Length == 0)
                return Array.Empty<PCGTerrainFeature>();

            var features = new PCGTerrainFeature[records.Length];

            for (int i = 0; i < records.Length; i++)
            {
                var feature = records[i];

                features[i] = feature == null
                    ? default
                    : new PCGTerrainFeature(
                        (PCGTerrainShape)feature.Kind,
                        new Vector2(feature.X, feature.Z),
                        feature.Radius,
                        feature.Height,
                        feature.Falloff);
            }

            return features;
        }

        private static PCGTerrainRecipe FromTerrainRecord(TerrainRecord record)
        {
            if (record == null)
                return null;

            var records = record.Features ?? Array.Empty<TerrainFeatureRecord>();
            var features = new PCGTerrainFeature[records.Length];

            for (int i = 0; i < records.Length; i++)
            {
                var feature = records[i];

                features[i] = feature == null
                    ? default
                    : new PCGTerrainFeature(
                        (PCGTerrainShape)feature.Kind,
                        new Vector2(feature.X, feature.Z),
                        feature.Radius,
                        feature.Height,
                        feature.Falloff);
            }

            return new PCGTerrainRecipe
            {
                NoiseAmplitude = record.NoiseAmplitude,
                NoiseScale = record.NoiseScale,
                Features = features,
                Holes = FromFeatureRecords(record.Holes),
            };
        }

        /// <summary>The kind label written for a value slot; mirrors the parameter schema vocabulary.</summary>
        public static string KindNameOf(object value)
        {
            switch (value)
            {
            case null: return "Empty";
            case float _:
            case double _: return nameof(PCGParameterType.Float);
            case int _:
            case long _: return nameof(PCGParameterType.Int);
            case bool _: return nameof(PCGParameterType.Bool);
            case Vector3 _:
            case Float3 _: return nameof(PCGParameterType.Vector);
            case Color _: return nameof(PCGParameterType.Color);
            case Guid _: return nameof(PCGParameterType.Asset);
            case string _: return nameof(PCGParameterType.String);
            default: return nameof(PCGParameterType.Enum);
            }
        }

        /// <summary>Document shape written to disk.</summary>
        internal sealed class GraphDocument
        {
            public int Format;
            public string Name;
            public string EditorData;
            public NodeRecord[] Nodes;
            public EdgeRecord[] Edges;
            public ExposedRecord[] Exposed;
            public SettingsRecord Settings;
        }

        internal sealed class NodeRecord
        {
            public string TypeId;
            public float X;
            public float Y;
            public string Comment;
            public bool Disabled;
            public ValueRecord[] Values;
        }

        internal sealed class EdgeRecord
        {
            public int FromNode;
            public int FromPin;
            public int ToNode;
            public int ToPin;
        }

        internal sealed class ExposedRecord
        {
            public string Name;
            public string DisplayName;
            public string Kind;
            public int NodeIndex;
            public int ValueIndex;
            public ValueRecord Default;
        }

        internal sealed class SettingsRecord
        {
            public int DefaultSeed;
            public bool EnableDebugDraw;
            public int MaxPointBudget;
            public int MaxExpandedNodeCount;

            /// <summary>地形合成配方；没写 `terrain` 的图没有这一段（也读得回 null）。</summary>
            public TerrainRecord Terrain;
        }

        /// <summary>地形合成配方在文件里的形状。</summary>
        internal sealed class TerrainRecord
        {
            public float NoiseAmplitude;
            public float NoiseScale;
            public TerrainFeatureRecord[] Features;

            /// <summary>洞掩码的形状（老文件里没有这个键 ⇒ 反序列化后是 null ⇒ 按"不挖洞"处理）。</summary>
            public TerrainFeatureRecord[] Holes;
        }

        /// <summary>
        /// 一条地形特征在文件里的形状。
        /// </summary>
        /// <remarks>
        /// `Kind` 存的是**枚举的数值**（0 台地 / 1 盆地 / 2 土丘），而不是名字：名字要多一处
        /// "字符串 ↔ 枚举"的映射，那种映射正是"改了一处、旧存档整体平移"的来源。
        /// 顺序由 <c>TerrainTests</c> 里那条"枚举数值与文件口径一致"的断言钉住——谁重排了枚举，
        /// 断的是测试，而不是别人的存档。
        /// </remarks>
        internal sealed class TerrainFeatureRecord
        {
            public int Kind;
            public float X;
            public float Z;
            public float Radius;
            public float Height;
            public float Falloff;
        }

        /// <summary>
        /// One authored value, tagged with the kind it must be read back as.
        /// </summary>
        /// <remarks>
        /// Fields are all optional and only the ones the kind uses are written, which keeps a saved graph
        /// readable in a diff. Values are stored as strings for exact round-tripping: a float written as a
        /// JSON number can come back through a double, which is fine for a reader that converts but would
        /// let the stored type drift away from the kind the schema declares.
        /// </remarks>
        internal sealed class ValueRecord
        {
            public string Kind;
            public string Value;
            public string[] Components;

            public static ValueRecord FromObject(object value)
            {
                var record = new ValueRecord { Kind = KindNameOf(value) };

                switch (value)
                {
                case null:
                    break;
                case bool flag:
                    record.Value = flag ? "1" : "0";
                    break;
                case float f:
                    record.Value = f.ToString("R", CultureInfo.InvariantCulture);
                    break;
                case double d:
                    record.Value = d.ToString("R", CultureInfo.InvariantCulture);
                    break;
                case int i:
                    record.Value = i.ToString(CultureInfo.InvariantCulture);
                    break;
                case long l:
                    record.Value = l.ToString(CultureInfo.InvariantCulture);
                    break;
                case Vector3 v:
                    record.Components = new[]
                    {
                        v.X.ToString("R", CultureInfo.InvariantCulture),
                        v.Y.ToString("R", CultureInfo.InvariantCulture),
                        v.Z.ToString("R", CultureInfo.InvariantCulture),
                    };
                    break;
                case Float3 f3:
                    // 画布上的向量这一路写回来的是 **Float3**（Visject 的向量类型），而槽位类型表与节点读参数
                    // 两边都认它（`KindOf` 里 Vector3 与 Float3 并列，`GetVector3` 也接 Float3）——只有这里漏了，
                    // 于是它掉进 default：Kind 被写成 Enum、值被 `ToString()` 成一个字符串，存档**丢信息**，
                    // 再打开时被自己的校验拒掉（"…is not an option index"）。写读两端必须都认这两种。
                    record.Components = new[]
                    {
                        f3.X.ToString("R", CultureInfo.InvariantCulture),
                        f3.Y.ToString("R", CultureInfo.InvariantCulture),
                        f3.Z.ToString("R", CultureInfo.InvariantCulture),
                    };
                    break;
                case Color c:
                    record.Components = new[]
                    {
                        c.R.ToString("R", CultureInfo.InvariantCulture),
                        c.G.ToString("R", CultureInfo.InvariantCulture),
                        c.B.ToString("R", CultureInfo.InvariantCulture),
                        c.A.ToString("R", CultureInfo.InvariantCulture),
                    };
                    break;
                case Guid guid:
                    record.Value = guid.ToString("N");
                    break;
                case string text:
                    record.Value = text;
                    break;
                case Enum enumeration:
                    record.Value = Convert.ToInt64(enumeration, CultureInfo.InvariantCulture)
                                     .ToString(CultureInfo.InvariantCulture);
                    break;
                default:
                    record.Value = value.ToString();
                    break;
                }

                return record;
            }

            public static string ToObject(ValueRecord record, out object value)
            {
                value = null;

                if (record == null || string.IsNullOrEmpty(record.Kind))
                    return "the value entry has no kind";

                switch (record.Kind)
                {
                case "Empty":
                    return null;
                case nameof(PCGParameterType.Float):
                    if (float.TryParse(record.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float f))
                    {
                        value = f;
                        return null;
                    }
                    return $"'{record.Value}' is not a float";
                case nameof(PCGParameterType.Int):
                    // Written as an integer, read back as int: a slot the schema calls Int must hold a boxed
                    // int, because the node's reader casts rather than converts.
                    if (int.TryParse(record.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i))
                    {
                        value = i;
                        return null;
                    }
                    return $"'{record.Value}' is not an int";
                case nameof(PCGParameterType.Bool):
                    value = record.Value == "1" || string.Equals(record.Value, "true", StringComparison.OrdinalIgnoreCase);
                    return null;
                case nameof(PCGParameterType.Vector):
                    if (record.Components == null || record.Components.Length < 3 ||
                        !float.TryParse(record.Components[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) ||
                        !float.TryParse(record.Components[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) ||
                        !float.TryParse(record.Components[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
                    {
                        return "a vector value needs three numeric components";
                    }

                    value = new Vector3(x, y, z);
                    return null;
                case nameof(PCGParameterType.Color):
                    if (record.Components == null || record.Components.Length < 4 ||
                        !float.TryParse(record.Components[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float r) ||
                        !float.TryParse(record.Components[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float g) ||
                        !float.TryParse(record.Components[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float b) ||
                        !float.TryParse(record.Components[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float a))
                    {
                        return "a colour value needs four numeric components";
                    }

                    value = new Color(r, g, b, a);
                    return null;
                case nameof(PCGParameterType.Asset):
                    if (Guid.TryParse(record.Value, out Guid guid))
                    {
                        value = guid;
                        return null;
                    }

                    value = Guid.Empty;
                    return null;
                case nameof(PCGParameterType.String):
                    value = record.Value;
                    return null;
                case nameof(PCGParameterType.Enum):
                    if (int.TryParse(record.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int option))
                    {
                        value = option;
                        return null;
                    }
                    return $"'{record.Value}' is not an option index";
                default:
                    return $"unknown value kind '{record.Kind}'";
                }
            }
        }
    }
}

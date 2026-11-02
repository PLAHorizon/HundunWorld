// PCG Plugin - Executable tests for the graph JSON codec (docs/PCG_PLUGIN_DESIGN.md §3.3 note 4).
//
// The codec exists because a graph is a tree of polymorphic parameter values, and the plugin cannot let an
// engine-side serializer decide whether a boxed float comes back as a float. These tests hold that line:
// every kind is written, read back, checked for its runtime type, and then the graph is executed twice -
// once from memory, once from the parsed text - and the two runs must agree point for point.

using System;
using System.Collections.Generic;
using System.Text;
using FlaxEngine;
using PCG;
using PCG.Execution;
using PCG.Graph;
using PCG.Nodes;

// One check callback for the whole harness: reuse the delegate NodeBehaviourTests already takes.
using CheckDelegate = PCGGoldenRunner.NodeBehaviourTests.CheckDelegate;

namespace PCGGoldenRunner
{
    internal static class GraphJsonTests
    {

        internal static void Run(CheckDelegate check)
        {
            PCGNodeRegistry.Clear();
            PCGNodeRegistry.Register(PCGDensityFilterNode.Id, "Filter", d => new PCGDensityFilterNode(d), "Density Filter");
            PCGNodeRegistry.Register(PCGTransformPointsNode.Id, "Modifier", d => new PCGTransformPointsNode(d), "Transform Points");
            PCGNodeRegistry.Register(JsonSourceNode.Id, "Sampler", d => new JsonSourceNode(d), "JSON Source");

            RoundTripTests(check);
            TerrainHolesRoundTripTests(check);
            VectorValueCodecTests(check);
            EditorDataPayloadTests(check);
            FailureTests(check);
            ExecutedGraphEquivalenceTests(check);
            GraphFileTests(check);

            PCGNodeRegistry.Clear();
        }

        /// <summary>
        /// Holds the line the canvas payload needs: it is binary and must survive a file round trip byte for byte.
        /// </summary>
        /// <remarks>
        /// The first version of this stored the payload as UTF-8 text, which silently replaced every invalid byte
        /// sequence with U+FFFD. Nothing failed at save time; reopening simply produced a payload that Visject
        /// could not parse, so every graph came back with an empty canvas. These checks pin both halves: arbitrary
        /// bytes survive, and the text encoding that broke them is demonstrated to be lossy on the very same
        /// bytes - so the test cannot pass by accident on a payload that happens to be text.
        /// </remarks>
        private static void EditorDataPayloadTests(CheckDelegate check)
        {
            // Deliberately illegal as UTF-8: a lone continuation byte, 0xFF, a truncated two-byte sequence, plus
            // the control bytes a BinaryWriter emits for ints and floats. A real payload is full of these.
            byte[] payload =
            {
                0x1B, 0x00, 0x0E, 0x0A, 0x80, 0xFF, 0xC3, 0x28, 0x09, 0x7F, 0xED, 0xA0, 0x80, 0x00, 0x01, 0xFE,
            };

            string encoded = PCGGraphJson.EncodeEditorData(payload);
            check("a binary canvas payload is stored as base64",
                encoded != null && encoded.Length > 0 && encoded.IndexOf('\uFFFD') < 0, encoded);

            var graph = new PCGGraph { Name = "PayloadProbe", EditorData = encoded };
            string json = PCGGraphJson.ToJson(graph);

            bool parsed = PCGGraphJson.TryParse(json, out var restored, out string error);
            check("the document with a payload parses", parsed && restored != null, error);

            if (parsed)
            {
                bool decoded = PCGGraphJson.TryDecodeEditorData(restored.EditorData, out byte[] bytes, out string decodeError);

                bool identical = decoded && bytes.Length == payload.Length;
                for (int i = 0; identical && i < payload.Length; i++)
                    identical = bytes[i] == payload[i];

                check("every byte of the canvas payload survives the file", identical,
                    decoded ? $"{bytes.Length} byte(s) back" : decodeError);
            }

            // The twin: the same bytes through the encoding the first version used must come back different.
            // Without this, a future "optimisation" back to text storage could pass the round trip above whenever
            // the payload happens to be valid UTF-8, and the loss would only reappear months later.
            byte[] throughText = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(payload));
            bool textIsLossy = throughText.Length != payload.Length;

            for (int i = 0; !textIsLossy && i < payload.Length; i++)
                textIsLossy = throughText[i] != payload[i];

            check("the UTF-8 text storage this replaced is demonstrably lossy on the same bytes",
                textIsLossy, $"{payload.Length} -> {throughText.Length} byte(s)");

            // Both v1 documents below are the same graph with the version rolled back and the payload replaced. The
            // placeholder keeps the substitution independent of how the JSON writer escapes a real base64 payload.
            var v1Source = BuildGraph();
            v1Source.EditorData = "PAYLOAD";

            string v1Damaged = PCGGraphJson.ToJson(v1Source)
                .Replace($"\"Format\": {PCGGraphJson.CurrentFormat}", "\"Format\": 1")
                .Replace("\"PAYLOAD\"", "\"VG\\tuX\\u001B\\u0000\\uFFFD\\uFFFD\\u0000layout\"");

            bool v1Parsed = PCGGraphJson.TryParse(v1Damaged, out var v1Graph, out string v1Error);
            check("a v1 document is still read",
                v1Parsed && v1Graph != null, v1Error);

            if (v1Parsed)
            {
                check("a v1 payload destroyed by UTF-8 is dropped, with the reason recorded",
                    string.IsNullOrEmpty(v1Graph.EditorData) &&
                    !string.IsNullOrEmpty(v1Graph.EditorDataNote) &&
                    v1Graph.EditorDataNote.Contains("U+FFFD"),
                    v1Graph.EditorDataNote);

                check("the nodes of that v1 document are kept, so the canvas can be rebuilt",
                    v1Graph.NodeCount == 3 && v1Graph.EdgeCount == 2, $"{v1Graph.NodeCount} node(s)");
            }

            string v1Intact = PCGGraphJson.ToJson(v1Source)
                .Replace($"\"Format\": {PCGGraphJson.CurrentFormat}", "\"Format\": 1")
                .Replace("\"PAYLOAD\"", "\"v1:layout\"");

            bool intactParsed = PCGGraphJson.TryParse(v1Intact, out var intactGraph, out string intactError);
            check("a v1 payload that survived as text is migrated rather than dropped",
                intactParsed && intactGraph != null &&
                PCGGraphJson.TryDecodeEditorData(intactGraph.EditorData, out byte[] migrated, out _) &&
                Encoding.UTF8.GetString(migrated) == "v1:layout",
                intactError ?? intactGraph?.EditorData);

            bool refused = !PCGGraphJson.TryDecodeEditorData("not*base64*at*all", out _, out string badPayload) &&
                           badPayload != null;
            check("a payload that is not base64 is refused with a reason", refused, badPayload);

            bool empty = PCGGraphJson.TryDecodeEditorData(string.Empty, out byte[] none, out _) && none.Length == 0;
            check("an absent payload decodes to nothing without complaining", empty);
        }

        private static PCGGraph BuildGraph()
        {
            var graph = new PCGGraph
            {
                Name = "CodecProbe",

                // Shaped like the real thing - base64 of binary drawn from Visject's BinaryWriter - so the fixture
                // cannot drift back into a text payload that would pass a weaker round trip than the canvas needs.
                EditorData = PCGGraphJson.EncodeEditorData(new byte[] { 0x00, 0x1B, 0x80, 0xFF, 0xC3, 0x28 }),
                Nodes = new[]
                {
                    new PCGNode { TypeId = JsonSourceNode.Id, EditorPosition = new Vector2(10, 20) },
                    new PCGNode
                    {
                        TypeId = PCGDensityFilterNode.Id,
                        EditorPosition = new Vector2(300, 40),
                        Values = new object[] { 0.25f, 0.95f, true },
                        Comment = "keep the sparse half",
                    },
                    new PCGNode
                    {
                        TypeId = PCGTransformPointsNode.Id,
                        EditorPosition = new Vector2(600, 80),
                        Values = new object[]
                        {
                            new Vector3(1.5f, -2.25f, 300f),
                            new Vector3(0f, 90f, 0f),
                            new Vector3(2f, 0.5f, 1f),
                            false,
                        },
                    },
                },
                Edges = new[]
                {
                    new PCGEdge { OutputNodeIndex = 0, OutputPinIndex = 0, InputNodeIndex = 1, InputPinIndex = 0 },
                    new PCGEdge { OutputNodeIndex = 1, OutputPinIndex = 0, InputNodeIndex = 2, InputPinIndex = 0 },
                },
                ExposedParameters = new[]
                {
                    new PCGParameterDefinition
                    {
                        Name = "MinDensity",
                        DisplayName = "Minimum Density",
                        Type = PCGParameterType.Float,
                        NodeIndex = 1,
                        ValueIndex = 0,
                        DefaultValue = PCGParameterValue.FromFloat(0.25f),
                    },
                    new PCGParameterDefinition
                    {
                        Name = "Lift",
                        Type = PCGParameterType.Vector,
                        NodeIndex = 2,
                        ValueIndex = 0,
                        DefaultValue = PCGParameterValue.FromVector(new Vector3(0f, 1f, 0f)),
                    },
                },
                Settings = new PCGGraphSettings
                {
                    DefaultSeed = 99,
                    EnableDebugDraw = false,
                    MaxPointBudget = 12345,
                    MaxExpandedNodeCount = 77,
                },
            };

            return graph;
        }

        /// <summary>
        /// 向量槽位里放 <c>Float3</c> 也要能存能读——画布写回来的就是这个类型。
        /// </summary>
        /// <remarks>
        /// 这条是补一个真缺陷：槽位类型表（<c>KindOf</c>）与节点读参数（<c>GetVector3</c>）都把 <c>Float3</c> 当向量接，
        /// 只有序列化那一处漏了——它掉进 <c>default</c> 分支，把 Kind 写成 Enum、值写成
        /// <c>Vector3.ToString()</c> 那种字符串（"X:1000.00 Y:200.00 Z:1000.00"）。后果不是显示难看，而是
        /// **存档丢信息**：三个数只剩一个字符串、Components 为空，再打开时被自己的校验拒掉，状态行报
        /// "…is not an option index"，那张图直接打不开。编辑画布上改过任何向量参数的图都会中招。
        /// </remarks>
        private static void VectorValueCodecTests(CheckDelegate check)
        {
            check("Float3 与 Vector3 都算向量槽位（类型表认、节点也认，序列化必须跟上）",
                PCGGraphJson.KindNameOf(new Float3(1f, 2f, 3f)) == nameof(PCGParameterType.Vector) &&
                PCGGraphJson.KindNameOf(new Vector3(1f, 2f, 3f)) == nameof(PCGParameterType.Vector),
                PCGGraphJson.KindNameOf(new Float3(1f, 2f, 3f)));

            // 同一个槽位换两种装箱类型各走一遍完整读写，免得"只修好了其中一种"。
            bool float3Ok = VectorSlotRoundTrips(new Float3(10f, 20f, 30f), out string float3Reason);
            bool vectorOk = VectorSlotRoundTrips(new Vector3(10f, 20f, 30f), out string vectorReason);

            check("向量槽位里放 Float3 能存能读（这条在修之前是红的）", float3Ok, float3Reason);
            check("向量槽位里放 Vector3 也能存能读（上一条的正面同夹具）", vectorOk, vectorReason);
        }

        /// <summary>把给定装箱值放进一个向量槽位，走一遍 ToJson → TryParse，报告数值是否原样回来。</summary>
        private static bool VectorSlotRoundTrips(object boxed, out string reason)
        {
            var graph = new PCGGraph
            {
                Name = "向量编解码",
                Nodes = new[]
                {
                    new PCGNode
                    {
                        TypeId = PCGTransformPointsNode.Id,
                        Values = new object[] { boxed, Vector3.Zero, Vector3.One, false },
                    },
                },
                Edges = Array.Empty<PCGEdge>(),
            };

            string json = PCGGraphJson.ToJson(graph);

            if (!PCGGraphJson.TryParse(json, out var restored, out string error))
            {
                reason = $"读不回来：{error}";
                return false;
            }

            if (restored.Nodes[0].Values[0] is not Vector3 back)
            {
                reason = $"回来的是 {restored.Nodes[0].Values[0]?.GetType().Name ?? "null"}，不是 Vector3";
                return false;
            }

            bool same = Math.Abs(back.X - 10f) <= 1e-4f && Math.Abs(back.Y - 20f) <= 1e-4f &&
                        Math.Abs(back.Z - 30f) <= 1e-4f;

            reason = same ? "ok" : $"数值变了 ({back.X}, {back.Y}, {back.Z})";
            return same;
        }

        /// <summary>
        /// 地形**洞掩码**那一组特征要能过一遍文件：写进去、读回来、逐项相等。
        /// </summary>
        /// <remarks>
        /// 为什么单拎一条：`Holes` 是 2026-10-11 新加的一个键，而它的**写**与**读**分别在两个方法里
        /// （`ToTerrainRecord` / `FromTerrainRecord`）——只改一处会留下"写得出来、读回来是空"的静默失效。
        /// 所以这里两头都要对上，而且**逐项**比（形状 / 中心 / 半径 / 幅度 / 过渡），不是"非空就算过"。
        /// 另配一条**对照**：没有洞的地形过一遍文件之后，洞那组必须仍是空的（不是"冒出一个洞"）。
        /// </remarks>
        private static void TerrainHolesRoundTripTests(CheckDelegate check)
        {
            var graph = BuildGraph();

            if (graph.Settings == null)
                graph.Settings = new PCGGraphSettings();

            graph.Settings.Terrain = new PCGTerrainRecipe
            {
                NoiseAmplitude = 7.0f,
                NoiseScale = 555.0f,
                Features = new[]
                {
                    new PCGTerrainFeature(PCGTerrainShape.Plateau, new Vector2(10f, 20f), 30f, 40f, 0.5f),
                },
                Holes = new[]
                {
                    new PCGTerrainFeature(PCGTerrainShape.Dome, new Vector2(900f, -400f), 250f, 0f, 0f),
                },
            };

            string json = PCGGraphJson.ToJson(graph);

            check("洞那一组真的被写进了 JSON（不是只声明在字段上）",
                !string.IsNullOrEmpty(json) && json.Contains("\"Holes\""),
                json == null ? "null" : $"长度 {json.Length}");

            bool ok = PCGGraphJson.TryParse(json, out var restored, out string error);

            check("带洞掩码的图能从文本解析回来", ok && restored?.Settings?.Terrain != null, error);

            if (ok && restored?.Settings?.Terrain != null)
            {
                var holes = restored.Settings.Terrain.Holes;
                var features = restored.Settings.Terrain.Features;

                check("洞那一组逐项相等（形状 / 中心 / 半径 / 幅度 / 过渡）",
                    holes != null && holes.Length == 1 &&
                    holes[0].Shape == PCGTerrainShape.Dome &&
                    Math.Abs(holes[0].Center.X - 900f) <= 1e-4f &&
                    Math.Abs(holes[0].Center.Y + 400f) <= 1e-4f &&
                    Math.Abs(holes[0].Radius - 250f) <= 1e-4f &&
                    Math.Abs(holes[0].Height) <= 1e-4f &&
                    Math.Abs(holes[0].Falloff) <= 1e-4f,
                    holes == null ? "null" : $"{holes.Length} 项");

                check("高度那一组没被洞那一组顶掉（两组各走各的）",
                    features != null && features.Length == 1 &&
                    features[0].Shape == PCGTerrainShape.Plateau &&
                    Math.Abs(features[0].Radius - 30f) <= 1e-4f &&
                    Math.Abs(features[0].Falloff - 0.5f) <= 1e-4f,
                    features == null ? "null" : $"{features.Length} 项");
            }

            // 对照：只写高度特征的地形，过一遍文件之后洞那组必须是空数组（而不是 null 崩、也不是冒出一个洞）。
            var plain = BuildGraph();

            if (plain.Settings == null)
                plain.Settings = new PCGGraphSettings();

            plain.Settings.Terrain = new PCGTerrainRecipe
            {
                Features = new[]
                {
                    new PCGTerrainFeature(PCGTerrainShape.Dome, new Vector2(0f, 0f), 100f, 20f),
                },
            };

            bool plainOk = PCGGraphJson.TryParse(PCGGraphJson.ToJson(plain), out var plainBack, out string plainError);

            check("没有洞的地形过一遍文件之后，洞那组仍是空的（对照）",
                plainOk && plainBack?.Settings?.Terrain?.Holes != null &&
                plainBack.Settings.Terrain.Holes.Length == 0,
                plainOk ? $"{(plainBack?.Settings?.Terrain?.Holes?.Length.ToString() ?? "null")} 项" : plainError);
        }

        private static void RoundTripTests(CheckDelegate check)
        {            var original = BuildGraph();
            var json = PCGGraphJson.ToJson(original);

            check("a graph serialises to readable JSON",
                json != null && json.Contains("CodecProbe") && json.Contains(PCGDensityFilterNode.Id),
                json?.Length.ToString());

            bool parsed = PCGGraphJson.TryParse(json, out var restored, out string error);
            check("the text parses back into a graph", parsed && restored != null, error);
            if (!parsed)
                return;

            check("the fixture's canvas payload is a payload the canvas could actually use",
                PCGGraphJson.TryDecodeEditorData(restored.EditorData, out byte[] fixtureBytes, out _) &&
                fixtureBytes.Length == 6 && fixtureBytes[1] == 0x1B && fixtureBytes[3] == 0xFF);

            check("graph identity and editor payload survive",
                restored.Name == original.Name && restored.EditorData == original.EditorData &&
                restored.NodeCount == original.NodeCount && restored.EdgeCount == original.EdgeCount);

            check("node editor positions survive",
                restored.Nodes[2].EditorPosition.X == 600f && restored.Nodes[2].EditorPosition.Y == 80f &&
                restored.Nodes[1].Comment == "keep the sparse half" && restored.Nodes[0].Disabled == false);

            check("settings survive",
                restored.Settings.DefaultSeed == 99 && !restored.Settings.EnableDebugDraw &&
                restored.Settings.MaxPointBudget == 12345 && restored.Settings.MaxExpandedNodeCount == 77);

            check("exposed parameters survive with their targets",
                restored.ExposedParameters.Length == 2 &&
                restored.ExposedParameters[0].Name == "MinDensity" &&
                restored.ExposedParameters[0].DisplayName == "Minimum Density" &&
                restored.ExposedParameters[0].Type == PCGParameterType.Float &&
                restored.ExposedParameters[0].NodeIndex == 1 && restored.ExposedParameters[0].ValueIndex == 0 &&
                restored.ExposedParameters[1].Type == PCGParameterType.Vector &&
                VectorNear(restored.ExposedParameters[1].DefaultValue.ToObject(PCGParameterType.Vector) as Vector3?,
                           new Vector3(0f, 1f, 0f)));

            // The reason the codec exists at all: each slot must come back as the same boxed runtime type,
            // because a node casts where it could otherwise fall back to its default without saying so.
            var values = restored.Nodes[2].Values;
            check("parameter slots keep their boxed runtime types",
                restored.Nodes[1].Values.Length == 3 &&
                restored.Nodes[1].Values[0] is float && restored.Nodes[1].Values[1] is float &&
                restored.Nodes[1].Values[2] is bool &&
                values.Length == 4 && values[0] is Vector3 && values[1] is Vector3 &&
                values[2] is Vector3 && values[3] is bool,
                DescribeTypes(restored.Nodes[1].Values) + " | " + DescribeTypes(values));

            check("parameter values survive by value",
                Math.Abs((float)restored.Nodes[1].Values[0] - 0.25f) < 1e-6f &&
                Math.Abs((float)restored.Nodes[1].Values[1] - 0.95f) < 1e-6f &&
                (bool)restored.Nodes[1].Values[2] == true &&
                VectorNear(values[0] as Vector3?, new Vector3(1.5f, -2.25f, 300f)) &&
                VectorNear(values[1] as Vector3?, new Vector3(0f, 90f, 0f)) &&
                VectorNear(values[2] as Vector3?, new Vector3(2f, 0.5f, 1f)));

            check("structural hash is invariant across a save and reload",
                PCGGraphCompiler.ComputeStructuralHash(original) == PCGGraphCompiler.ComputeStructuralHash(restored),
                $"{PCGGraphCompiler.ComputeStructuralHash(original)} vs {PCGGraphCompiler.ComputeStructuralHash(restored)}");

            check("an empty graph round-trips",
                PCGGraphJson.TryParse(PCGGraphJson.ToJson(new PCGGraph()), out var empty, out _) &&
                empty != null && empty.NodeCount == 0);

            // Wiring is the part of a graph a designer cannot retype from memory, so the endpoints are checked by
            // value rather than through the count or the structural hash. The fixture graph above uses pin 0 on
            // both ends of both edges, where a swapped pair would compare equal; this one deliberately does not,
            // and node indices differ from their positions so an off-by-one shows up as a value mismatch.
            var wired = new PCGGraph
            {
                Name = "WiringProbe",
                Edges = new[]
                {
                    new PCGEdge { OutputNodeIndex = 3, OutputPinIndex = 1, InputNodeIndex = 5, InputPinIndex = 2 },
                    new PCGEdge { OutputNodeIndex = 0, OutputPinIndex = 0, InputNodeIndex = 3, InputPinIndex = 1 },
                },
            };

            bool wiredParsed = PCGGraphJson.TryParse(PCGGraphJson.ToJson(wired), out var wiredBack, out string wiredError);

            check("every endpoint of every wire survives the file",
                wiredParsed && wiredBack.EdgeCount == 2 &&
                wiredBack.Edges[0].OutputNodeIndex == 3 && wiredBack.Edges[0].OutputPinIndex == 1 &&
                wiredBack.Edges[0].InputNodeIndex == 5 && wiredBack.Edges[0].InputPinIndex == 2 &&
                wiredBack.Edges[1].OutputNodeIndex == 0 && wiredBack.Edges[1].OutputPinIndex == 0 &&
                wiredBack.Edges[1].InputNodeIndex == 3 && wiredBack.Edges[1].InputPinIndex == 1,
                wiredError ?? (wiredBack?.EdgeCount.ToString() ?? "no graph"));

            // The direction nobody would notice: output and input are stored as separate fields, so writing the
            // target's node into the source's slot is a plausible copy-paste slip that no count would catch.
            check("a wire is not reversed by the codec",
                wiredParsed && wiredBack.Edges[0].OutputNodeIndex != wiredBack.Edges[0].InputNodeIndex &&
                wiredBack.Edges[0].OutputPinIndex != wiredBack.Edges[0].InputPinIndex,
                wiredParsed ? $"{wiredBack.Edges[0].OutputNodeIndex}.{wiredBack.Edges[0].OutputPinIndex} -> " +
                              $"{wiredBack.Edges[0].InputNodeIndex}.{wiredBack.Edges[0].InputPinIndex}" : wiredError);
        }

        private static void FailureTests(CheckDelegate check)
        {
            bool rejected;

            rejected = !PCGGraphJson.TryParse(null, out _, out string emptyError) && emptyError != null;
            check("missing text is refused with a reason", rejected, emptyError);

            rejected = !PCGGraphJson.TryParse("{ not json", out _, out string junkError) && junkError != null;
            check("malformed JSON is refused rather than throwing", rejected, junkError);

            rejected = !PCGGraphJson.TryParse("{\"Value\":null}", out _, out string foreignError);
            check("JSON from another asset type is refused", rejected, foreignError);

            var future = PCGGraphJson.ToJson(BuildGraph())
                .Replace($"\"Format\": {PCGGraphJson.CurrentFormat}", "\"Format\": 99");
            rejected = !PCGGraphJson.TryParse(future, out _, out string versionError);
            check("a newer format is refused with a version hint",
                rejected && versionError.Contains("99"), versionError);

            check("LooksLikeGraph recognises our own document and nothing else",
                PCGGraphJson.LooksLikeGraph(PCGGraphJson.ToJson(BuildGraph())) &&
                !PCGGraphJson.LooksLikeGraph("{\"Value\":null}") &&
                !PCGGraphJson.LooksLikeGraph("<html>"));

            var tampered = PCGGraphJson.ToJson(BuildGraph())
                .Replace("\"Kind\": \"Float\"", "\"Kind\": \"Quaternion\"");
            bool reported;
            string kindError;
            try
            {
                reported = !PCGGraphJson.TryParse(tampered, out _, out kindError) &&
                           kindError != null && kindError.Contains("Quaternion");
            }
            catch (Exception ex)
            {
                reported = false;
                kindError = "threw " + ex.GetType().Name;
            }

            check("an unknown value kind is reported against its slot", reported, kindError);
        }

        private static void ExecutedGraphEquivalenceTests(CheckDelegate check)
        {
            var original = BuildGraph();
            PCGGraphJson.TryParse(PCGGraphJson.ToJson(original), out var restored, out _);

            JsonSourceNode.Produced = MakePoints(12);

            var first = Run(original);
            var second = Run(restored);

            check("both graphs compiled without errors",
                first.compiled != null && second.compiled != null,
                $"{first.error ?? string.Empty} / {second.error ?? string.Empty}");

            if (first.compiled == null || second.compiled == null)
                return;

            var a = first.result?.PrimaryOutput;
            var b = second.result?.PrimaryOutput;

            bool same = a != null && b != null && a.Count == b.Count && a.Count > 0;
            for (int i = 0; same && i < a.Count; i++)
            {
                same = VectorNear(a.Points[i].Position, b.Points[i].Position) &&
                       Math.Abs(a.Points[i].Scale.X - b.Points[i].Scale.X) < 1e-5f;
            }

            check("the reloaded graph produces exactly the same points",
                same, $"{a?.Count ?? -1} vs {b?.Count ?? -1} point(s)");
        }

        private static (PCGCompiledGraph compiled, PCGExecutionResult result, string error) Run(PCGGraph graph)
        {
            if (!PCGGraphCompiler.TryCompile(graph, out var compiled))
                return (null, null, compiled?.Diagnostics != null && compiled.Diagnostics.Count > 0
                    ? compiled.Diagnostics[0].Message : "compile failed");

            var context = new PCGExecutionContext
            {
                Seed = 5,
                CurrentNodeId = 0,
                GenerationBounds = new BoundingBox(new Vector3(-100, -100, -100), new Vector3(100, 100, 100)),
            };

            return (compiled, new PCGGraphExecutor(compiled).Execute(context), null);
        }

        private static PCGData MakePoints(int count)
        {
            var data = PCGData.Create(count);
            for (int i = 0; i < count; i++)
            {
                var point = PCGPoint.Create(new Vector3(i * 10f, 0f, 0f), i % 2 == 0 ? 0.9f : 0.1f, i);
                point.UpdateBounds(new Vector3(1f, 1f, 1f));
                data.Points[i] = point;
            }

            return data;
        }

        private static bool VectorNear(Vector3? a, Vector3 b) => a.HasValue && VectorNear(a.Value, b);

        private static bool VectorNear(Vector3 a, Vector3 b)
        {
            return Math.Abs(a.X - b.X) < 1e-4f && Math.Abs(a.Y - b.Y) < 1e-4f && Math.Abs(a.Z - b.Z) < 1e-4f;
        }

        private static string DescribeTypes(object[] values)
        {
            var names = new List<string>();
            foreach (var value in values)
                names.Add(value?.GetType().Name ?? "null");

            return string.Join(",", names);
        }

        /// <summary>
        /// Exercises the plugin-owned graph files: path resolution, atomic save, and load errors.
        /// </summary>
        /// <remarks>
        /// Runs against a throwaway directory under the system temp path. The engine's project folders are not
        /// available headless, which is exactly the packaged-build case, so the fallback behaviour also gets
        /// asserted rather than assumed.
        /// </remarks>
        private static void GraphFileTests(CheckDelegate check)
        {
            string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pcg_graph_file_tests");
            string file = System.IO.Path.Combine(root, "nested", "probe_graph.json");

            try
            {
                if (System.IO.Directory.Exists(root))
                    System.IO.Directory.Delete(root, true);

                var graph = BuildGraph();

                // A missing file must say where it looked, since the search path depends on whether the
                // project content folder is available at all.
                bool refusedMissing = !PCG.Runtime.PCGGraphFile.TryLoad(file, out _, out string missingError);
                check("loading a missing graph file is refused with the searched paths",
                    refusedMissing && missingError != null && missingError.Contains("Looked in"), missingError);

                bool refusedEmptyPath = !PCG.Runtime.PCGGraphFile.TryLoad("   ", out _, out string emptyPathError);
                check("an unset graph path is refused with a plain reason",
                    refusedEmptyPath && emptyPathError != null && emptyPathError.Contains("path"), emptyPathError);

                bool refusedNullGraph = !PCG.Runtime.PCGGraphFile.TrySave(file, null, out string nullError);
                check("saving a null graph is refused rather than truncating a file",
                    refusedNullGraph && nullError != null && nullError.Contains("null"), nullError);

                bool saved = PCG.Runtime.PCGGraphFile.TrySave(file, graph, out string saveError);
                check("saving creates the missing parent folder and the file",
                    saved && System.IO.File.Exists(file), saveError);

                check("a saved file leaves no temporary sibling behind",
                    !System.IO.File.Exists(file + ".tmp"));

                check("the file on disk is readable as a graph document",
                    PCG.Runtime.PCGGraphFile.Exists(file) &&
                    PCG.Graph.PCGGraphJson.LooksLikeGraph(System.IO.File.ReadAllText(file)));

                bool loaded = PCG.Runtime.PCGGraphFile.TryLoad(file, out var read, out string loadError);
                check("the saved graph loads back", loaded && read != null, loadError);

                if (loaded)
                {
                    check("the loaded graph matches the saved one structurally",
                        PCGGraphCompiler.ComputeStructuralHash(graph) ==
                        PCGGraphCompiler.ComputeStructuralHash(read));

                    check("parameter slots keep their boxed types through the file",
                        read.Nodes[1].Values[0] is float && read.Nodes[2].Values[0] is Vector3 &&
                        read.Nodes[1].Values[2] is bool);
                }

                // Re-saving over an existing file must replace it, not append or leave a stale copy.
                graph.Name = "Renamed";
                check("re-saving over an existing graph file succeeds",
                    PCG.Runtime.PCGGraphFile.TrySave(file, graph, out _) &&
                    System.IO.File.ReadAllText(file).Contains("Renamed"));

                string bare = PCG.Runtime.PCGGraphFile.ResolvePath("forest_rocks");
                check("a bare graph name resolves into the graphs folder with an extension",
                    bare.EndsWith("forest_rocks.json", StringComparison.OrdinalIgnoreCase) &&
                    bare.Contains("Graphs") &&
                    System.IO.Path.GetFullPath(bare) == bare,
                    bare);

                string absolute = PCG.Runtime.PCGGraphFile.ResolvePath(file);
                check("an absolute path is passed through untouched", absolute == file, absolute);

                check("listing graphs never throws when the folder does not exist",
                    PCG.Runtime.PCGGraphFile.ListGraphs() != null);
            }
            finally
            {
                try
                {
                    if (System.IO.Directory.Exists(root))
                        System.IO.Directory.Delete(root, true);
                }
                catch (Exception)
                {
                    // Scratch files are ours to clean; a leftover under the temp path is not worth a failure.
                }
            }
        }

        /// <summary>Sampler stand-in handing out a fixed data set, so a graph can be executed without a scene.</summary>
        internal sealed class JsonSourceNode : PCGNodeBase
        {
            public const string Id = "TestJsonSource";

            public static PCGData Produced;

            public JsonSourceNode(PCGNodeRegistry.PCGNodeSerializationData data)
            {
            }

            public override string TypeId => Id;

            public override string DisplayName => "JSON Source";

            public override PCGPinDefinition[] InputPins => Array.Empty<PCGPinDefinition>();

            public override PCGPinDefinition[] OutputPins => new[]
            {
                new PCGPinDefinition("Out", PCGPinDirection.Output),
            };

            public override PCGData OnExecute(PCGExecutionContext context, PCGData[] inputs) =>
                Produced ?? PCGData.Empty;
        }
    }
}

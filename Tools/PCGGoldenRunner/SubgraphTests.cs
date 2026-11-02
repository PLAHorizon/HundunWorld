// PCG Plugin - Executable tests for sub-graphs and cross-asset cycle detection.
//
// Both halves are pure logic over PCGGraph objects with an injected loader, so they run here rather than only
// inside the editor: a stack overflow from A-includes-B-includes-A would otherwise be discovered by whoever
// clicked Generate.

using System;
using System.Collections.Generic;
using FlaxEngine;
using PCG;
using PCG.Execution;
using PCG.Graph;
using PCG.Nodes;

// One check callback for the whole harness: reuse the delegate NodeBehaviourTests already takes.
using CheckDelegate = PCGGoldenRunner.NodeBehaviourTests.CheckDelegate;

namespace PCGGoldenRunner
{
    internal static class SubgraphTests
    {
        internal static void Run(CheckDelegate check)
        {
            PCGNodeRegistry.Clear();
            PCGNodeRegistry.Register(PCGSubgraphNode.Id, "Combinator", d => new PCGSubgraphNode(d), "Sub-graph");
            PCGNodeRegistry.Register(GraphSourceNode.Id, "Sampler", d => new GraphSourceNode(d), "Graph Source");
            PCGNodeRegistry.Register(ContextProbeNode.Id, "Sampler", d => new ContextProbeNode(d), "Context Probe");
            PCGNodeRegistry.Register(PCGDensityFilterNode.Id, "Filter", d => new PCGDensityFilterNode(d), "Density Filter");
            PCGNodeRegistry.Register(PCGTransformPointsNode.Id, "Modifier", d => new PCGTransformPointsNode(d), "Transform Points");

            InspectorTests(check);
            ExecutionTests(check);
            ContextTravelTests(check);

            PCGNodeRegistry.Clear();
        }

        // ---- fixtures -------------------------------------------------------

        private static PCGGraph GraphWithChilds(string name, params string[] children)
        {
            var nodes = new List<PCGNode>();

            for (int i = 0; i < children.Length; i++)
            {
                nodes.Add(new PCGNode
                {
                    TypeId = PCGSubgraphNode.Id,
                    Values = new object[] { children[i], false, 0 },
                });
            }

            return new PCGGraph { Name = name, Nodes = nodes.ToArray(), Edges = Array.Empty<PCGEdge>() };
        }

        private static PCGGraph LeafGraph(string name, int points)
        {
            var graph = new PCGGraph
            {
                Name = name,
                Nodes = new[]
                {
                    new PCGNode { TypeId = GraphSourceNode.Id },
                },
                Edges = Array.Empty<PCGEdge>(),
            };

            GraphSourceNode.Produced = MakePoints(points);
            return graph;
        }

        private static PCGData MakePoints(int count)
        {
            var data = PCGData.Create(count);
            for (int i = 0; i < count; i++)
            {
                var point = PCGPoint.Create(new Vector3(i * 10f, 0f, 0f), 1.0f, i);
                point.UpdateBounds(new Vector3(1f, 1f, 1f));
                data.Points[i] = point;
            }

            return data;
        }

        private static Func<string, PCGGraph> LoaderOf(params PCGGraph[] graphs)
        {
            var byName = new Dictionary<string, PCGGraph>(StringComparer.Ordinal);
            foreach (var graph in graphs)
                byName[graph.Name] = graph;

            return name => byName.TryGetValue(name ?? string.Empty, out var graph) ? graph : null;
        }

        private static string FirstError(List<PCGCompileDiagnostic> diagnostics)
        {
            foreach (var diagnostic in diagnostics)
            {
                if (diagnostic.Severity == PCGCompileSeverity.Error)
                    return diagnostic.Message;
            }

            return null;
        }

        // ---- tests ----------------------------------------------------------

        private static void InspectorTests(CheckDelegate check)
        {
            var cycle = GraphWithChilds("a", "b");
            var cycleChild = GraphWithChilds("b", "a");

            var issues = PCGSubgraphInspector.Inspect(cycle, "a", LoaderOf(cycle, cycleChild));
            string message = FirstError(issues);
            check("a cycle across two graph files is reported as an error",
                PCGSubgraphInspector.HasErrors(issues) && message != null &&
                message.Contains("a -> b -> a"), message);

            var selfCycle = GraphWithChilds("solo", "solo");
            issues = PCGSubgraphInspector.Inspect(selfCycle, "solo", LoaderOf(selfCycle));
            message = FirstError(issues);
            check("a graph that includes itself is caught",
                PCGSubgraphInspector.HasErrors(issues) && message != null &&
                message.Contains("solo -> solo"), message);

            var diamond = GraphWithChilds("root", "left", "right");
            var diamondLeft = GraphWithChilds("left", "shared");
            var diamondRight = GraphWithChilds("right", "shared");
            var diamondLeaf = GraphWithChilds("shared");

            issues = PCGSubgraphInspector.Inspect(
                diamond, "root", LoaderOf(diamond, diamondLeft, diamondRight, diamondLeaf));
            check("a diamond (the same child twice, no cycle) is not flagged",
                !PCGSubgraphInspector.HasErrors(issues), FirstError(issues));

            var dangling = GraphWithChilds("root", "nowhere");
            issues = PCGSubgraphInspector.Inspect(dangling, "root", LoaderOf(dangling));
            message = FirstError(issues);
            check("a missing child graph is an error naming it",
                message != null && message.Contains("nowhere"), message);

            var unnamed = new PCGGraph
            {
                Name = "root",
                Nodes = new[] { new PCGNode { TypeId = PCGSubgraphNode.Id, Values = new object[] { "   ", false, 0 } } },
                Edges = Array.Empty<PCGEdge>(),
            };
            issues = PCGSubgraphInspector.Inspect(unnamed, "root", LoaderOf(unnamed));
            message = FirstError(issues);
            check("a sub-graph node without a child name is reported before the run",
                message != null && message.Contains("no child graph name"), message);

            // A chain deeper than the ceiling: level1 -> level2 -> ... -> level7.
            var chain = new List<PCGGraph>();
            for (int level = 1; level <= 7; level++)
                chain.Add(level == 7 ? GraphWithChilds("level7") : GraphWithChilds($"level{level}", $"level{level + 1}"));

            issues = PCGSubgraphInspector.Inspect(chain[0], "level1", LoaderOf(chain.ToArray()), maxDepth: 4);
            message = FirstError(issues);
            check("sub-graph nesting past the depth limit is refused",
                message != null && message.Contains("limit of 4"), message);

            var wide = GraphWithChilds("root", "wide");
            var wideChild = new PCGGraph
            {
                Name = "wide",
                Nodes = new PCGNode[300],
                Edges = Array.Empty<PCGEdge>(),
            };
            for (int i = 0; i < wideChild.Nodes.Length; i++)
                wideChild.Nodes[i] = new PCGNode { TypeId = GraphSourceNode.Id };

            issues = PCGSubgraphInspector.Inspect(wide, "root", LoaderOf(wide, wideChild), maxExpandedNodes: 100);
            bool budgetWarned = false;
            foreach (var diagnostic in issues)
            {
                if (diagnostic.Severity == PCGCompileSeverity.Warning)
                    budgetWarned = true;
            }

            check("an oversized expanded graph is warned about rather than refused", budgetWarned);
        }

        private static void ExecutionTests(CheckDelegate check)
        {
            var child = LeafGraph("child", 6);
            var parent = new PCGGraph
            {
                Name = "parent",
                Nodes = new[]
                {
                    new PCGNode { TypeId = PCGSubgraphNode.Id, Values = new object[] { "child", false, 0 } },
                },
                Edges = Array.Empty<PCGEdge>(),
            };

            var result = RunGraph(parent, LoaderOf(child));
            check("a sub-graph's points reach the parent graph",
                result.output != null && result.output.Count == 6, result.error ?? result.nodeError ?? "ok");

            // The child's seed is derived from the parent's plus the node's own offset: the same child included
            // twice with different offsets must not produce the same layout.
            var twoNodes = new PCGGraph
            {
                Name = "twice",
                Nodes = new[]
                {
                    new PCGNode { TypeId = PCGSubgraphNode.Id, Values = new object[] { "jittered", false, 0 } },
                    new PCGNode { TypeId = PCGSubgraphNode.Id, Values = new object[] { "jittered", false, 999 } },
                },
                Edges = Array.Empty<PCGEdge>(),
            };

            var randomOffsetChild = new PCGGraph
            {
                Name = "jittered",
                Nodes = new[]
                {
                    new PCGNode { TypeId = GraphSourceNode.Id },
                    new PCGNode
                    {
                        TypeId = PCGRandomOffsetNode.Id,
                        Values = new object[] { new Vector3(-50f, 0f, -50f), new Vector3(50f, 0f, 50f),
                                                new Vector3(), 1f, 1f, 0 },
                    },
                },
                Edges = new[] { new PCGEdge { OutputNodeIndex = 0, OutputPinIndex = 0, InputNodeIndex = 1, InputPinIndex = 0 } },
            };

            PCGNodeRegistry.Register(PCGRandomOffsetNode.Id, "Modifier", d => new PCGRandomOffsetNode(d), "Random Offset");

            var twiceResult = RunGraph(twoNodes, LoaderOf(randomOffsetChild));
            check("the same child included twice with different seed offsets yields different layouts",
                twiceResult.output != null && twiceResult.output.Count == 6 &&
                !ReferenceEquals(twiceResult.sinkOutputs?[0], twiceResult.sinkOutputs?[1]) &&
                twiceResult.sinkOutputs?[0] != null && twiceResult.sinkOutputs?[1] != null &&
                twiceResult.sinkOutputs[0].Points[0].Position != twiceResult.sinkOutputs[1].Points[0].Position,
                twiceResult.error ?? twiceResult.nodeError ?? "ok");

            var missing = new PCGGraph
            {
                Name = "orphan",
                Nodes = new[] { new PCGNode { TypeId = PCGSubgraphNode.Id, Values = new object[] { "gone", false, 0 } } },
                Edges = Array.Empty<PCGEdge>(),
            };

            var missingResult = RunGraph(missing, LoaderOf());
            check("a missing child surfaces as the node's reported problem, not a crash",
                missingResult.output != null && missingResult.output.Count == 0 &&
                missingResult.nodeError != null && missingResult.nodeError.Contains("gone"),
                missingResult.nodeError ?? missingResult.error);

            // The loader ignores the inspector entirely here, so only the runtime depth guard stands between
            // this graph and a stack overflow.
            var cyclic = GraphWithChilds("loop", "loop");
            var cyclicResult = RunGraph(cyclic, LoaderOf(cyclic));
            check("a cyclic loader is stopped by the runtime depth guard instead of overflowing",
                cyclicResult.nodeError != null && cyclicResult.nodeError.Contains("nesting"),
                cyclicResult.nodeError ?? cyclicResult.error ?? "no problem reported");
        }

        private struct RunOutcome
        {
            public PCGData output;
            public PCGData[] sinkOutputs;
            public string error;
            public string nodeError;
        }

        private static RunOutcome RunGraph(PCGGraph graph, Func<string, PCGGraph> loader)
        {
            var outcome = new RunOutcome();

            if (!PCGGraphCompiler.TryCompile(graph, out var compiled))
            {
                outcome.error = FirstError(compiled?.Diagnostics != null ? new List<PCGCompileDiagnostic>(compiled.Diagnostics) : null)
                              ?? "compile failed";
                return outcome;
            }

            var context = new PCGExecutionContext
            {
                Seed = 4242,
                GenerationBounds = new BoundingBox(new Vector3(-100, -100, -100), new Vector3(100, 100, 100)),
                SubgraphLoader = loader,
                GraphName = graph.Name,
            };

            try
            {
                var result = new PCGGraphExecutor(compiled).Execute(context);
                outcome.sinkOutputs = result.Outputs;
                outcome.output = result.PrimaryOutput;

                // The executor clears a node's LastError once it has collected it, so the run's warnings are the
                // only place a reported problem still exists afterwards.
                if (result.Warnings != null && result.Warnings.Length > 0)
                    outcome.nodeError = result.Warnings[0];
            }
            catch (Exception ex)
            {
                outcome.error = $"{ex.GetType().Name}: {ex.Message}";
                return outcome;
            }

            return outcome;
        }

        /// <summary>Sampler handing out a fixed data set, so sub-graphs can be executed without a scene.</summary>
        internal sealed class GraphSourceNode : PCGNodeBase
        {
            public const string Id = "TestGraphSource";

            public static PCGData Produced;

            public GraphSourceNode(PCGNodeRegistry.PCGNodeSerializationData data)
            {
            }

            public override string TypeId => Id;

            public override string DisplayName => "Graph Source";

            public override PCGPinDefinition[] InputPins => Array.Empty<PCGPinDefinition>();

            public override PCGPinDefinition[] OutputPins => new[]
            {
                new PCGPinDefinition("Out", PCGPinDirection.Output),
            };

            public override PCGData OnExecute(PCGExecutionContext context, PCGData[] inputs) =>
                Produced ?? PCGData.Empty;
        }

        /// <summary>
        /// 探针：把"这次运行看到了什么约束"记下来。用来证明子图里**真的**看得见那些字段，而不是"传了但没人读"。
        /// </summary>
        private static void ContextTravelTests(CheckDelegate check)
        {
            // 干跑副本：DryRun 的契约是"不调引擎"，而裁剪/锚点是节点判定要读的输入——一起翻掉等于干跑与真跑
            // 用两套边界判据（预览说会建、烘焙说不会，或反之）。
            var parent = new PCGExecutionContext
            {
                Seed = 1,
                GenerationBounds = new BoundingBox(Vector3.Zero, new Vector3(10, 10, 10)),
                SourceBounds = new BoundingBox(new Vector3(-5, -5, -5), new Vector3(5, 5, 5)),
                ClipRegion = new BoundingBox(Vector3.Zero, new Vector3(20, 20, 20)),
            };

            var dry = parent.AsDryRun();

            check("干跑副本带上裁剪区域", dry.ClipRegion.HasValue && Near(dry.ClipRegion.Value.Maximum.X, 20.0f),
                dry.ClipRegion.HasValue ? dry.ClipRegion.Value.Maximum.X.ToString() : "null");
            check("干跑副本带上体积锚点", dry.SourceBounds.HasValue && Near(dry.SourceBounds.Value.Minimum.X, -5.0f));
            check("干跑副本确实置位了 DryRun", dry.DryRun);

            // 子图：内层节点必须在**自己那一份**上下文里看见裁剪与锚点。少了这些字段，子图里的生成器不认边界
            // ——分区运行会把 padding 里的物件也建一遍，整块运行会把内容落到体积外面。
            var cell = new PCGPartitionCell(
                new BoundingBox(new Vector3(-100, -100, -100), new Vector3(100, 100, 100)),
                new BoundingBox(new Vector3(-50, -50, -50), new Vector3(50, 50, 50)), 0, 0);

            var child = new PCGGraph
            {
                Name = "probe_child",
                Nodes = new[] { new PCGNode { TypeId = ContextProbeNode.Id } },
                Edges = Array.Empty<PCGEdge>(),
            };

            var root = new PCGGraph
            {
                Name = "probe_root",
                Nodes = new[]
                {
                    new PCGNode
                    {
                        TypeId = PCGSubgraphNode.Id,
                        Values = new object[] { child.Name, false, 0 },
                    },
                },
                Edges = Array.Empty<PCGEdge>(),
            };

            ContextProbeNode.Reset();

            bool compiledOk = PCGGraphCompiler.TryCompile(root, out var compiled);

            check("探针子图能编译", compiledOk,
                compiledOk ? null : FirstError(compiled?.Diagnostics != null ? new List<PCGCompileDiagnostic>(compiled.Diagnostics) : null));

            if (compiledOk)
            {
                var context = new PCGExecutionContext
                {
                    Seed = 4242,
                    GenerationBounds = new BoundingBox(new Vector3(-100, -100, -100), new Vector3(100, 100, 100)),
                    SourceBounds = new BoundingBox(new Vector3(-200, -200, -200), new Vector3(200, 200, 200)),
                    ClipRegion = new BoundingBox(new Vector3(-150, -150, -150), new Vector3(150, 150, 150)),
                    ClipCell = cell,
                    SubgraphLoader = LoaderOf(child),
                    GraphName = root.Name,
                };

                string failure = null;

                try
                {
                    new PCGGraphExecutor(compiled).Execute(context);
                }
                catch (Exception ex)
                {
                    failure = $"{ex.GetType().Name}: {ex.Message}";
                }

                check("探针子图跑完不抛异常", failure == null, failure);
            }

            check("探针确实被跑到了（下面对照的前提）", ContextProbeNode.Ran,
                ContextProbeNode.Ran ? "ran" : "not evaluated");
            check("子图里的节点看见了裁剪格子", ContextProbeNode.SawClipCell);
            check("子图里的节点看见了裁剪区域", ContextProbeNode.SawClipRegion);
            check("子图里的节点看见了体积锚点", ContextProbeNode.SawSourceBounds);
        }

        private static bool Near(float a, float b) => Math.Abs(a - b) < 1e-4f;

        /// <summary>记录"这次运行看到了什么约束"的探针节点（子图里的生成器能不能认出边界，就靠这些字段）。</summary>
        internal sealed class ContextProbeNode : PCGNodeBase
        {
            public const string Id = "TestContextProbe";

            public static bool Ran;
            public static bool SawClipCell;
            public static bool SawClipRegion;
            public static bool SawSourceBounds;

            public ContextProbeNode(PCGNodeRegistry.PCGNodeSerializationData data)
            {
            }

            public static void Reset()
            {
                Ran = false;
                SawClipCell = false;
                SawClipRegion = false;
                SawSourceBounds = false;
            }

            public override string TypeId => Id;

            public override string DisplayName => "Context Probe";

            public override PCGPinDefinition[] InputPins => Array.Empty<PCGPinDefinition>();

            public override PCGPinDefinition[] OutputPins => new[]
            {
                new PCGPinDefinition("Out", PCGPinDirection.Output),
            };

            public override PCGData OnExecute(PCGExecutionContext context, PCGData[] inputs)
            {
                Ran = true;
                SawClipCell = context.ClipCell.HasValue;
                SawClipRegion = context.ClipRegion.HasValue;
                SawSourceBounds = context.SourceBounds.HasValue;
                return GraphSourceNode.Produced ?? PCGData.Empty;
            }
        }
    }
}

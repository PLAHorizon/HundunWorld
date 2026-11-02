// PCG Plugin - Executable tests for the graph clipboard and batch parameter editing.
//
// Paste is the operation where a wrong index remap produces a graph that still saves, still looks right on the
// canvas, and runs as a different graph than the designer made. So every assertion below checks the data after
// the fact - node counts, edge endpoints, positions, exposed-parameter names - rather than trusting that
// "something was appended".

using System;
using System.Collections.Generic;
using FlaxEngine;
using PCG;
using PCG.Execution;
using PCG.Graph;
using PCG.Nodes;

using CheckDelegate = PCGGoldenRunner.NodeBehaviourTests.CheckDelegate;

namespace PCGGoldenRunner
{
    internal static class ClipboardTests
    {
        private static CheckDelegate _check;

        // Condition first, so an assertion reads as "this holds, because ...".
        private static void check(bool ok, string what, string detail = null) => _check(what, ok, detail);

        internal static void Run(CheckDelegate callback)
        {
            _check = callback;

            PCGNodeRegistry.Clear();
            PCG.Runtime.PCGNodeRegistration.RegisterAll();

            try
            {
                CopyTests();
                PasteTests();
                BatchEditTests();
            }
            finally
            {
                PCGNodeRegistry.Clear();
            }
        }

        // ---- fixtures -------------------------------------------------------

        /// <summary>Sampler -> pruner -> density filter -> transform, with the pruner's spacing exposed.</summary>
        private static PCGGraph ChainGraph()
        {
            var graph = new PCGGraph
            {
                Name = "ClipboardChain",
                Nodes = new[]
                {
                    new PCGNode
                    {
                        TypeId = PCGVolumeSamplerNode.Id,
                        Values = new object[] { 100, 0, 1.0f, true, 1.0f },
                        EditorPosition = new Vector2(0.0f, 0.0f),
                        Comment = "sampler",
                    },
                    new PCGNode
                    {
                        TypeId = PCGSelfPruningFilterNode.Id,
                        Values = new object[] { 60.0f, false, true },
                        EditorPosition = new Vector2(200.0f, 0.0f),
                        Disabled = true,
                    },
                    new PCGNode
                    {
                        TypeId = PCGDensityFilterNode.Id,
                        Values = new object[] { 0.3f, 0.9f, false },
                        EditorPosition = new Vector2(400.0f, 0.0f),
                    },
                    new PCGNode
                    {
                        TypeId = PCGTransformPointsNode.Id,
                        Values = new object[] { new Vector3(0, 1, 0), new Vector3(), new Vector3(1, 1, 1), false },
                        EditorPosition = new Vector2(600.0f, 0.0f),
                    },
                },
                Edges = new[]
                {
                    new PCGEdge { OutputNodeIndex = 0, InputNodeIndex = 1 },
                    new PCGEdge { OutputNodeIndex = 1, InputNodeIndex = 2 },
                    new PCGEdge { OutputNodeIndex = 2, InputNodeIndex = 3 },
                },
                ExposedParameters = new[]
                {
                    new PCGParameterDefinition
                    {
                        Name = "Spacing",
                        DisplayName = "Spacing",
                        Type = PCGParameterType.Float,
                        NodeIndex = 1,
                        ValueIndex = 0,
                    },
                },
            };

            return graph;
        }

        private static PCGGraph CloneOf(PCGGraph graph)
        {
            // Copy through the codec so the paste tests all start from an identical, detached graph.
            var json = PCGGraphJson.ToJson(graph);
            if (!PCGGraphJson.TryParse(json, out var clone, out _))
                throw new InvalidOperationException("the clipboard fixture would not round-trip");
            return clone;
        }

        // ---- copy -----------------------------------------------------------

        private static void CopyTests()
        {
            var graph = ChainGraph();
            var copy = PCGGraphClipboard.Copy(graph, new[] { 0, 1 });

            check(copy.NodeCount == 2, "a two-node selection copies two nodes", copy.NodeCount.ToString());
            check(copy.Edges.Count == 1,
                "only the connection inside the selection is carried", $"edges={copy.Edges.Count}");
            check(copy.DroppedEdgeCount == 1,
                "the connection to the unselected node is counted as dropped, not lost silently",
                $"dropped={copy.DroppedEdgeCount}");
            // The exposure belongs to the pruner, which is the second node of this copy, so its local address
            // is 1 - and selecting the pruner alone has to yield 0. Getting this wrong is what would make the
            // pasted exposure edit somebody else's node.
            check(copy.Exposed.Count == 1 && copy.Exposed[0].NodeIndex == 1,
                "an exposed parameter is re-addressed against the copy's own indices",
                copy.Exposed.Count == 0 ? "none carried" : $"nodeIndex={copy.Exposed[0].NodeIndex}");

            var single = PCGGraphClipboard.Copy(graph, new[] { 1 });
            check(single.Exposed.Count == 1 && single.Exposed[0].NodeIndex == 0,
                "selecting only the exposed node addresses it at 0 in the copy",
                single.Exposed.Count == 0 ? "none carried" : $"nodeIndex={single.Exposed[0].NodeIndex}");

            // Aliasing here would make the pasted node and its original share one value array: editing one would
            // move the other, and the graph file would then disagree with both.
            var valuesDetached = !ReferenceEquals(graph.Nodes[0].Values, copy.Nodes[0].Values);
            graph.Nodes[0].Values[0] = 999;
            check(valuesDetached && (int)copy.Nodes[0].Values[0] == 100,
                "a copy does not share its source's value array",
                $"shared={!valuesDetached} value={copy.Nodes[0].Values[0]}");

            check(copy.Nodes[0].Comment == "sampler" && copy.Nodes[1].Disabled &&
                  copy.Nodes[1].EditorPosition.X == 200.0f,
                "comments, the disabled flag and canvas positions come along");

            var empty = PCGGraphClipboard.Copy(graph, Array.Empty<int>());
            check(empty.IsEmpty, "an empty selection copies nothing");

            var outOfRange = PCGGraphClipboard.Copy(graph, new[] { 0, 40, -1 });
            check(outOfRange.NodeCount == 1,
                "indices outside the graph are ignored rather than pasted as holes",
                $"nodes={outOfRange.NodeCount}");
        }

        // ---- paste ----------------------------------------------------------

        private static void PasteTests()
        {
            var graph = ChainGraph();
            var copy = PCGGraphClipboard.Copy(graph, new[] { 0, 1 });

            var target = CloneOf(graph);
            bool pasted = PCGGraphClipboard.Paste(target, copy, PCGGraphClipboard.DefaultPasteOffset,
                out int[] newIndices, out string error);

            check(pasted && error == null, "a paste of registered types succeeds", error);
            check(target.Nodes.Length == 6, "pasting two nodes into a four-node graph makes six",
                $"{target.Nodes.Length}");
            check(target.Edges.Length == 4, "the copied connection is appended, existing edges kept",
                $"{target.Edges.Length}");
            check(newIndices.Length == 2 && newIndices[0] == 4 && newIndices[1] == 5,
                "the paste reports the indices it created",
                $"{newIndices[0]},{newIndices[1]}");

            var pastedEdge = target.Edges[target.Edges.Length - 1];
            check(pastedEdge.OutputNodeIndex == 4 && pastedEdge.InputNodeIndex == 5,
                "the pasted edge points at the pasted nodes, not at the originals",
                $"{pastedEdge.OutputNodeIndex}->{pastedEdge.InputNodeIndex}");

            check(Math.Abs(target.Nodes[4].EditorPosition.X - 40.0f) < 0.01f &&
                  Math.Abs(target.Nodes[5].EditorPosition.X - 240.0f) < 0.01f,
                "pasted nodes land offset from their originals rather than underneath them",
                $"{target.Nodes[4].EditorPosition.X},{target.Nodes[5].EditorPosition.X}");

            check(target.ExposedParameters.Length == 2 &&
                  target.ExposedParameters[0].Name == "Spacing" &&
                  target.ExposedParameters[1].Name == "Spacing#2" &&
                  target.ExposedParameters[1].NodeIndex == 5,
                "the pasted exposure keeps working under a name that is still unique in the graph",
                $"{target.ExposedParameters[1].Name}@{target.ExposedParameters[1].NodeIndex}");

            // A second paste of the same copy must not collide with the first.
            PCGGraphClipboard.Paste(target, copy, PCGGraphClipboard.DefaultPasteOffset, out _, out _);
            check(target.Nodes.Length == 8 && target.ExposedParameters.Length == 3 &&
                  target.ExposedParameters[2].Name == "Spacing#3",
                "repeated pastes keep producing distinct nodes and distinct names",
                $"{target.Nodes.Length} nodes, {target.ExposedParameters.Length} exposed");

            check(PCGGraphCompiler.TryCompile(target, out var compiled) && compiled.NodeCount == 8,
                "the pasted graph is still a graph the compiler accepts",
                compiled == null ? "compile failed" : $"{compiled.NodeCount} nodes");

            var unknown = new PCGGraph
            {
                Name = "UnknownType",
                Nodes = new[] { new PCGNode { TypeId = "ThisTypeDoesNotExist" } },
                Edges = Array.Empty<PCGEdge>(),
            };

            var before = CloneOf(graph).Nodes.Length;
            var unknownCopy = new PCGGraphCopy();
            unknownCopy.Nodes.Add(new PCGNode { TypeId = "ThisTypeDoesNotExist" });

            bool rejected = PCGGraphClipboard.Paste(CloneOf(graph), unknownCopy, Vector2.Zero, out _, out string rejectError);
            var untouched = CloneOf(graph);
            check(!rejected && rejectError != null && rejectError.Contains("ThisTypeDoesNotExist"),
                "a paste of an unknown node type is refused by name", rejectError);
            check(!rejected && untouched.Nodes.Length == before,
                "the refused paste leaves the graph alone", $"{untouched.Nodes.Length}");

            check(!PCGGraphClipboard.Paste(CloneOf(graph), copy = null, Vector2.Zero, out _, out string emptyError) &&
                  emptyError != null,
                "pasting nothing is reported, not accepted", emptyError);

            // Duplicate is copy + paste, and must drop only the edge leaving the selection.
            var viaDuplicate = CloneOf(graph);
            check(PCGGraphClipboard.Duplicate(viaDuplicate, new[] { 0, 1, 2 },
                      new Vector2(0.0f, 120.0f), out int[] duplicated, out _) &&
                  viaDuplicate.Nodes.Length == 7 && viaDuplicate.Edges.Length == 5 &&
                  duplicated.Length == 3 && duplicated[0] == 4,
                "duplicating three chained nodes yields six nodes and five edges",
                $"{viaDuplicate.Nodes.Length} nodes, {viaDuplicate.Edges.Length} edges");

            check(Math.Abs(viaDuplicate.Nodes[4].EditorPosition.Y - 120.0f) < 0.01f,
                "the duplicate offset applies on the axis the designer asked for");
        }

        // ---- batch parameter editing ----------------------------------------

        private static void BatchEditTests()
        {
            var graph = new PCGGraph
            {
                Name = "BatchEdit",
                Nodes = new[]
                {
                    new PCGNode { TypeId = PCGVolumeSamplerNode.Id, Values = new object[] { 100, 0, 1.0f, true, 1.0f } },
                    new PCGNode { TypeId = PCGMeshSamplerNode.Id, Values = new object[] { Guid.Empty, string.Empty, 500, 1, 1.0f, 0, Guid.Empty } },
                    new PCGNode { TypeId = PCGSelfPruningFilterNode.Id, Values = new object[] { 60.0f, false, true } },
                },
                Edges = Array.Empty<PCGEdge>(),
            };

            // The same named control sits at a different slot on each of these types, which is the whole reason a
            // batch edit addresses parameters by name.
            bool ok = PCGGraphClipboard.SetParameterOnNodes(graph, new[] { 0, 1 }, "Seed Offset", 7,
                out int changed, out List<string> skipped, out string error);

            check(ok && changed == 2 && error == null, "a named parameter sets on every selected node that has it", error);
            check((int)graph.Nodes[0].Values[1] == 7 && (int)graph.Nodes[1].Values[3] == 7,
                "each node is written at its own slot for that name",
                $"sampler slot 1 = {graph.Nodes[0].Values[1]}, mesh slot 3 = {graph.Nodes[1].Values[3]}");
            check(skipped.Count == 0, "nothing was skipped when both nodes carry the parameter",
                skipped.Count > 0 ? skipped[0] : "none");

            ok = PCGGraphClipboard.SetParameterOnNodes(graph, new[] { 0, 1, 2 }, "Point Count", 250,
                out changed, out skipped, out error);
            check(ok && changed == 2 && skipped.Count == 1 && skipped[0].Contains("Point Count"),
                "a node without the parameter is skipped and says which",
                $"changed={changed} skipped={skipped.Count}");
            check((int)graph.Nodes[0].Values[0] == 250 && (int)graph.Nodes[1].Values[2] == 250 &&
                  Math.Abs((float)graph.Nodes[2].Values[0] - 60.0f) < 0.01f,
                "the skipped node keeps its own values untouched");

            var before = (int)graph.Nodes[0].Values[0];
            ok = PCGGraphClipboard.SetParameterOnNodes(graph, new[] { 0, 1 }, "Point Count", 1.5f,
                out changed, out skipped, out error);
            check(!ok && changed == 0 && error != null && (int)graph.Nodes[0].Values[0] == before,
                "a value of the wrong kind is refused for the whole selection, not half applied", error);

            ok = PCGGraphClipboard.SetParameterOnNodes(graph, new[] { 2 }, "Point Count", 10,
                out changed, out skipped, out error);
            check(!ok && changed == 0 && error != null && error.Contains("Point Count"),
                "selecting only nodes that lack the parameter reports it rather than claiming success", error);

            ok = PCGGraphClipboard.SetParameterOnNodes(graph, new[] { 9 }, "Point Count", 10,
                out changed, out skipped, out error);
            check(!ok && skipped.Count == 1 && skipped[0].Contains("outside the graph"),
                "an index outside the graph is reported as skipped",
                skipped.Count > 0 ? skipped[0] : "none");

            var shared = PCGGraphClipboard.CommonParameterNames(graph, new[] { 0, 1 });
            check(shared.Count == 3 && shared.Contains("Point Count") && shared.Contains("Seed Offset") &&
                  shared.Contains("Density"),
                "the batch panel offers exactly the parameters both node types carry",
                string.Join("|", shared));
            check(shared[0] == "Point Count",
                "shared parameters keep the first selected node's order", shared[0]);

            var noOverlap = PCGGraphClipboard.CommonParameterNames(graph, new[] { 0, 2 });
            check(noOverlap.Count == 0,
                "a selection with nothing in common offers nothing rather than a wrong control",
                $"names={noOverlap.Count}");

            var withUnknown = new PCGGraph
            {
                Name = "Unknown",
                Nodes = new[]
                {
                    graph.Nodes[0],
                    new PCGNode { TypeId = "NotARegisteredType" },
                },
                Edges = Array.Empty<PCGEdge>(),
            };
            check(PCGGraphClipboard.CommonParameterNames(withUnknown, new[] { 0, 1 }).Count == 0,
                "an unregistered type in the selection clears the shared list rather than guessing");

            check(PCGGraphClipboard.TryGetParameterKind(graph, new[] { 0, 1 }, "Point Count", out var kind) &&
                  kind == PCGParameterType.Int,
                "a shared parameter reports its kind so text can be converted before writing", kind.ToString());
            check(!PCGGraphClipboard.TryGetParameterKind(graph, new[] { 0, 1 }, "Nope", out _),
                "an unknown parameter reports no kind rather than a default");

            ok = PCGGraphClipboard.SetParameterOnNodes(graph, new[] { 0 }, "Density", 0.25f,
                out changed, out skipped, out error);
            check(ok && changed == 1 && Math.Abs((float)graph.Nodes[0].Values[4] - 0.25f) < 0.01f,
                "a float parameter takes a float value at its own slot", error);
        }
    }
}

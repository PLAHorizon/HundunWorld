// PCG Plugin - Pipeline-level verification for the v1.4 node families
// Part of the FlaxEngine PCG plugin. See docs/PCG_PLUGIN_DESIGN.md §5.1.29–§5.1.34.
//
// The per-node tests check each node in isolation. These check the things that only exist once nodes are inside a
// graph and a file: that a pipeline compiles, that it runs and passes its payload on, that the editor's
// "does this write the scene" gate classifies it the way the run behaves, and that a saved graph can be read back -
// which is how a designer keeps a pipeline between sessions, and the one place a new node's slot order is load
// bearing.

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
    internal static class PipelineTests
    {
        internal static void Run(CheckDelegate check)
        {
            PCGNodeRegistry.Clear();
            PCG.Runtime.PCGNodeRegistration.RegisterAll();

            GraphRoundTripTests(check);
            SceneWritingGateTests(check);
            GeometryPipelineRunTests(check);

            PCGNodeRegistry.Clear();
        }

        /// <summary>
        /// Every node family added in this session survives a save and a reload, with its slots intact.
        /// </summary>
        /// <remarks>
        /// A new node's parameter indices are its file format: reading them in a different order in the constructor
        /// than the registration declares them loads an authored graph with its values shuffled, and nothing throws.
        /// The graph file is the only place that shows up, so the check is done through the file.
        /// </remarks>
        private static void GraphRoundTripTests(CheckDelegate check)
        {
            var graph = new PCGGraph
            {
                Name = "PipelineProbe",
                Nodes = new[]
                {
                    Node(PCGGeometryNode.Id, 0, 0,
                        PCGGeometryNode.ShapeStairs, 12, 6, new Vector3(4.0f, 2.0f, 8.0f), false),
                    Node(PCGSpawnGeometryNode.Id, 1, 0, 250, 0.5f, 7),
                    Node(PCGActorSamplerNode.Id, 2, 0, "Marker_", "village", "Triggers", false, 5, 3, true),
                    Node(PCGWorldRayProjectNode.Id, 3, 0, 4, true, new Vector3(0.0f, -1.0f, 0.0f), 500.0f, 1.5f, false, true),
                    Node(PCGSplitGrammarNode.Id, 4, 0, "X:0.5,0.5; Z:1", 3, 25.0f, new Vector3(10.0f, 2.0f, 20.0f), false, false),
                    Node(PCGGrammarNode.Id, 5, 0, "Lot = Shop,Shop: X:0.5,0.5", "Lot", 5, 0.0f, new Vector3(1, 1, 1), true, false),
                },
                Edges = Array.Empty<PCGEdge>(),
            };

            string json = PCGGraphJson.ToJson(graph);
            bool parsed = PCGGraphJson.TryParse(json, out var restored, out string error);

            check("a graph holding every new node family round-trips through the file",
                parsed && restored != null && restored.NodeCount == 6, error);

            if (!parsed)
                return;

            bool typesMatch = true;
            var expected = new[]
            {
                PCGGeometryNode.Id, PCGSpawnGeometryNode.Id, PCGActorSamplerNode.Id,
                PCGWorldRayProjectNode.Id, PCGSplitGrammarNode.Id, PCGGrammarNode.Id,
            };

            for (int i = 0; i < expected.Length && typesMatch; i++)
                typesMatch = restored.Nodes[i].TypeId == expected[i];

            check("each node keeps its type id", typesMatch);

            // The slot values, by position and by boxed type: an index read out of order shows up here as a string
            // where a float was written, or as two values swapped.
            var geometry = restored.Nodes[0].Values;

            check("an enum, an int and a vector all come back where they were written",
                geometry.Length == 5 && geometry[0] is int && (int)geometry[0] == PCGGeometryNode.ShapeStairs &&
                geometry[1] is int && (int)geometry[1] == 12 &&
                geometry[3] is Vector3 && Near(((Vector3)geometry[3]).Z, 8.0f) &&
                geometry[4] is bool && (bool)geometry[4] == false,
                Describe(geometry));

            var spawner = restored.Nodes[1].Values;

            check("a spawner's threshold survives as the float it was",
                spawner.Length == 3 && spawner[0] is int && (int)spawner[0] == 250 &&
                spawner[1] is float && Near((float)spawner[1], 0.5f),
                Describe(spawner));

            var actor = restored.Nodes[2].Values;

            check("the actor sampler's filters survive as their own strings",
                actor.Length == 7 && (string)actor[0] == "Marker_" && (string)actor[1] == "village" &&
                (string)actor[2] == "Triggers" && (bool)actor[3] == false &&
                (int)actor[4] == 5 && (bool)actor[6] == true,
                Describe(actor));

            var ray = restored.Nodes[3].Values;

            check("the ray node's direction and distance survive",
                ray.Length == 7 && (int)ray[0] == 4 && (bool)ray[1] &&
                ray[2] is Vector3 && Near(((Vector3)ray[2]).Y, -1.0f) &&
                Near((float)ray[3], 500.0f) && (bool)ray[6],
                Describe(ray));

            var split = restored.Nodes[4].Values;

            check("the split grammar's rule table survives verbatim",
                split.Length == 6 && (string)split[0] == "X:0.5,0.5; Z:1",
                Describe(split));

            var grammar = restored.Nodes[5].Values;

            check("the symbol grammar's productions and root survive verbatim",
                grammar.Length == 7 && (string)grammar[0] == "Lot = Shop,Shop: X:0.5,0.5" &&
                (string)grammar[1] == "Lot",
                Describe(grammar));

            // The file must stay parseable by the registry as well: a type id that no longer exists would load as an
            // empty node, so the round trip is only meaningful if every one of them is still registered.
            bool allRegistered = true;

            foreach (var node in restored.Nodes)
                allRegistered &= PCGNodeRegistry.TryGet(node.TypeId, out _);

            check("every type in the file is still a registered node", allRegistered);
        }

        /// <summary>
        /// The gate the editor's dry run uses: does this graph write into the scene?
        /// </summary>
        /// <remarks>
        /// It decides whether 试跑 is allowed and, at run time, whether a pass is a decider or a builder. Getting it
        /// wrong in the permissive direction spawns actors nothing owns; in the strict direction it refuses to
        /// preview a graph that is perfectly safe.
        /// </remarks>
        private static void SceneWritingGateTests(CheckDelegate check)
        {
            var data = Chain(PCGVolumeSamplerNode.Id, PCGGeometryNode.Id);
            check("a pipeline of samplers and geometry does not write the scene",
                !PCGGraphEffects.GraphWritesScene(data, null));

            // The twin: the same pipeline with the geometry spawner as its sink. Without it, the check above would
            // pass on a classifier that always said no.
            var writing = Chain(PCGVolumeSamplerNode.Id, PCGGeometryNode.Id, PCGSpawnGeometryNode.Id);

            check("adding the geometry spawner makes it a scene-writing graph",
                PCGGraphEffects.GraphWritesScene(writing, null));

            bool listed = PCGGraphEffects.EverySpawnerIsListed(out string missing);

            check("every registered spawner, including the geometry one, is on the scene-writing list",
                listed, missing ?? "none missing");
        }

        /// <summary>
        /// A geometry pipeline compiles and runs end to end, and its payload arrives at the end of the chain.
        /// </summary>
        private static void GeometryPipelineRunTests(CheckDelegate check)
        {
            var graph = Chain(PCGVolumeSamplerNode.Id, PCGGeometryNode.Id, PCGGeometryNode.Id);
            graph.Nodes[1].Values = new object[]
            {
                PCGGeometryNode.ShapeBox, 4, 2, new Vector3(10.0f, 2.0f, 10.0f), true,
            };

            // The second geometry node receives an input that already carries meshes; it must replace them with its
            // own rather than appending, or the array would drift out of alignment with the points.
            graph.Nodes[2].Values = new object[]
            {
                PCGGeometryNode.ShapeCylinder, 6, 2, new Vector3(10.0f, 2.0f, 10.0f), false,
            };

            if (!PCGGraphCompiler.TryCompile(graph, out var compiled))
            {
                check("a two-stage geometry pipeline compiles", false,
                    compiled?.Diagnostics != null && compiled.Diagnostics.Count > 0
                        ? compiled.Diagnostics[0].Message
                        : "compile failed");
                return;
            }

            int errors = 0;

            if (compiled.Diagnostics != null)
            {
                foreach (var diagnostic in compiled.Diagnostics)
                {
                    if (diagnostic.Severity == PCGCompileSeverity.Error)
                        errors++;
                }
            }

            check("a two-stage geometry pipeline compiles without errors", errors == 0, errors.ToString());

            var context = new PCGExecutionContext
            {
                Seed = 20261008,
                GenerationBounds = new BoundingBox(new Vector3(-200.0f, -10.0f, -200.0f),
                                                   new Vector3(200.0f, 10.0f, 200.0f)),
            };

            var result = new PCGGraphExecutor(compiled).Execute(context);

            var output = result.PrimaryOutput;

            check("the pipeline runs and produces points",
                output != null && output.Count > 0, $"{output?.Count ?? 0} point(s)");

            if (output == null || output.Count == 0)
                return;

            check("every point comes out of the chain carrying a mesh",
                output.Meshes != null && output.Meshes.Length == output.Count,
                output.Meshes == null ? "no meshes" : $"{output.Meshes.Length} mesh(es) for {output.Count} point(s)");

            // The last stage's shape is the one that survives, and it is a cylinder of six sides - not the box the
            // first stage built, which is what would arrive if the second node appended instead of replacing.
            bool cylinders = output.Meshes != null;

            for (int i = 0; cylinders && i < output.Meshes.Length; i++)
                cylinders = output.Meshes[i] != null && output.Meshes[i].TriangleCount == 6 * 4;

            check("the last stage's shape is the one that reaches the end",
                cylinders, output.Meshes != null && output.Meshes.Length > 0
                    ? output.Meshes[0].TriangleCount.ToString()
                    : "no mesh");

            check("the run reports no node errors",
                result.Warnings == null ||
                !Array.Exists(result.Warnings, w => w != null && w.Contains("threw")),
                result.Warnings == null ? "none" : string.Join(" | ", result.Warnings));
        }

        // ---- fixtures ---------------------------------------------------------------------------------------

        private static PCGNode Node(string typeId, int index, int _unused, params object[] values) =>
            new PCGNode { TypeId = typeId, EditorPosition = new Vector2(index * 100.0f, 0.0f), Values = values };

        /// <summary>Chains nodes into a graph, each feeding the next.</summary>
        private static PCGGraph Chain(params string[] typeIds)
        {
            var nodes = new PCGNode[typeIds.Length];
            var edges = new PCGEdge[Math.Max(0, typeIds.Length - 1)];

            for (int i = 0; i < typeIds.Length; i++)
                nodes[i] = new PCGNode { TypeId = typeIds[i], EditorPosition = new Vector2(i * 200.0f, 0.0f) };

            for (int i = 0; i < edges.Length; i++)
            {
                edges[i] = new PCGEdge
                {
                    OutputNodeIndex = i,
                    OutputPinIndex = 0,
                    InputNodeIndex = i + 1,
                    InputPinIndex = 0,
                };
            }

            return new PCGGraph { Name = "Pipeline", Nodes = nodes, Edges = edges };
        }

        private static string Describe(object[] values)
        {
            if (values == null)
                return "no values";

            var parts = new List<string>(values.Length);

            foreach (var value in values)
                parts.Add(value == null ? "null" : $"{value.GetType().Name}:{value}");

            return string.Join(" | ", parts);
        }

        private static bool Near(float a, float b, float tolerance = 1e-3f) => Math.Abs(a - b) <= tolerance;
    }
}

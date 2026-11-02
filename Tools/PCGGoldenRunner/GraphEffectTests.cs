// PCG Plugin - Executable tests for graph effect classification.
//
// The editor's dry-run button is refused by this classification, so the list is a safety gate rather than a
// report: a spawner that is not on it lets a scene-writing graph run with nothing owning the actors. The
// assertions therefore check the list against the registry (the failure mode is forgetting to add a node) and
// check that data-only types are still allowed through (the failure mode is a gate nobody can use).

using System;
using System.Collections.Generic;
using PCG.Execution;
using PCG.Graph;
using PCG.Nodes;

using CheckDelegate = PCGGoldenRunner.NodeBehaviourTests.CheckDelegate;

namespace PCGGoldenRunner
{
    internal static class GraphEffectTests
    {
        private static CheckDelegate _check;

        // Condition first, so an assertion reads as "this holds, because ...".
        private static void check(bool ok, string what, string detail = null) => _check(what, ok, detail);

        private static readonly string[] DataOnlyIds =
        {
            PCGVolumeSamplerNode.Id,
            PCGNoiseSamplerNode.Id,
            PCGLandscapeSamplerNode.Id,
            PCGMeshSamplerNode.Id,
            PCGSplineSamplerNode.Id,
            PCGSelfPruningFilterNode.Id,
            PCGDensityFilterNode.Id,
            PCGDistanceFilterNode.Id,
            PCGHeightFilterNode.Id,
            PCGSlopeFilterNode.Id,
            PCGRandomOffsetNode.Id,
            PCGTransformPointsNode.Id,
            PCGMergePointsNode.Id,
        };

        private static readonly string[] SceneWritingIds =
        {
            PCGSpawnStaticModelNode.Id,
            PCGSpawnFoliageNode.Id,
            PCGSpawnActorNode.Id,
            PCGSpawnDecalNode.Id,
            PCGSpawnSplineMeshNode.Id,
            PCGSpawnGeometryNode.Id,
            PCGSubgraphNode.Id,
        };

        internal static void Run(CheckDelegate callback)
        {
            _check = callback;

            PCGNodeRegistry.Clear();
            PCG.Runtime.PCGNodeRegistration.RegisterAll();

            try
            {
                ClassificationTests();
                GraphTests();
                SubgraphTests();
            }
            finally
            {
                PCGNodeRegistry.Clear();
            }
        }

        private static void ClassificationTests()
        {
            foreach (var id in SceneWritingIds)
                check(PCGGraphEffects.IsSceneWriting(id), $"{id} counts as writing to the scene");

            foreach (var id in DataOnlyIds)
                check(!PCGGraphEffects.IsSceneWriting(id), $"{id} stays allowed through the gate");

            // The conservative side of the unknown case, with its positive twin directly above: a type the
            // registry does not know is refused, while every registered data-only type is allowed - so the
            // rejection cannot be a gate that simply rejects everything.
            check(PCGGraphEffects.IsSceneWriting("NoSuchNodeType"),
                "an unregistered type is treated as scene-writing rather than assumed harmless");
            check(PCGGraphEffects.IsSceneWriting(null) && PCGGraphEffects.IsSceneWriting(string.Empty),
                "a missing type id is treated as scene-writing");

            // The tripwire for the failure that actually happens: a new spawner registered and not listed.
            check(PCGGraphEffects.EverySpawnerIsListed(out string missing),
                "every registered Spawn* node type is on the scene-writing list",
                missing == null ? "none missing" : $"missing: {missing}");

            check(PCGGraphEffects.SceneWritingCount == SceneWritingIds.Length,
                "the list has exactly the types the tests enumerate - removing one goes red here",
                $"{PCGGraphEffects.SceneWritingCount} vs {SceneWritingIds.Length}");
        }

        private static void GraphTests()
        {
            var dataOnly = BuildNodes(DataOnlyIds);
            check(dataOnly != null && dataOnly.Length == DataOnlyIds.Length,
                "the fixture really built one node per data-only type",
                dataOnly == null ? "construction failed" : $"{dataOnly.Length}");

            check(!PCGGraphEffects.GraphWritesScene(dataOnly),
                "a graph of data-only nodes is allowed through");

            foreach (var id in SceneWritingIds)
            {
                var nodes = BuildNodes(new[] { PCGVolumeSamplerNode.Id, id });
                check(nodes != null && PCGGraphEffects.GraphWritesScene(nodes),
                    $"one {id} node makes the whole graph scene-writing",
                    $"nodes={nodes?.Length ?? -1}");
            }

            // Order independent: the spawner is not always last.
            var firstIsSpawner = BuildNodes(new[] { PCGSpawnActorNode.Id, PCGVolumeSamplerNode.Id });
            check(firstIsSpawner != null && PCGGraphEffects.GraphWritesScene(firstIsSpawner),
                "a spawner at the head of the node list is still found");

            check(PCGGraphEffects.GraphWritesScene(null),
                "a graph that did not compile at all is treated as writing, not as safe");
        }

        private static void SubgraphTests()
        {
            var plain = Graph(PCGVolumeSamplerNode.Id, PCGSelfPruningFilterNode.Id);
            check(!PCGGraphEffects.GraphWritesScene(plain, null),
                "a graph with no sub-graph reference needs no loader to be allowed");

            var rocks = Graph(PCGMeshSamplerNode.Id);
            var withChild = Graph(PCGVolumeSamplerNode.Id, Child("rocks"));

            check(!PCGGraphEffects.GraphWritesScene(withChild, name => name == "rocks" ? rocks : null),
                "a data-only sub-graph is resolved and allowed, not refused for being a sub-graph");

            var rocksSpawning = Graph(PCGVolumeSamplerNode.Id, PCGSpawnActorNode.Id);
            check(PCGGraphEffects.GraphWritesScene(withChild, name => name == "rocks" ? rocksSpawning : null),
                "a spawner one level down is still refused");

            check(PCGGraphEffects.GraphWritesScene(withChild, null),
                "a sub-graph reference with no loader cannot be proven, so it is refused");
            check(PCGGraphEffects.GraphWritesScene(withChild, name => null),
                "a sub-graph name that does not resolve is refused rather than treated as empty and safe");

            // A name that points back into the chain has no finite contents to check.
            var a = Graph(Child("b"));
            var b = Graph(Child("a"));
            check(PCGGraphEffects.GraphWritesScene(a, name => name == "b" ? b : a),
                "a sub-graph cycle is refused: there is no finite contents to certify as safe");

            // The same child referenced twice is a diamond, not a cycle, and must not be refused - the chain is
            // per path, so a shared library graph still works.
            var diamond = Graph(Child("rocks"), Child("rocks"));
            check(!PCGGraphEffects.GraphWritesScene(diamond, name => name == "rocks" ? rocks : null),
                "the same data-only sub-graph used twice is not mistaken for a cycle");

            // Second child hides the spawner: proves the walk checks every reference, not just the first.
            var hidesAtSecond = Graph(Child("rocks"), Child("props"));
            var propsSpawning = Graph(Child("sawmill"), PCGSpawnFoliageNode.Id);
            var sawmillData = Graph(PCGRandomOffsetNode.Id);
            Func<string, PCGGraph> mixed = name =>
                name == "rocks" ? rocks : name == "props" ? propsSpawning : name == "sawmill" ? sawmillData : null;

            check(PCGGraphEffects.GraphWritesScene(hidesAtSecond, mixed),
                "a spawner in the second sub-graph, two levels down, is still refused");

            var deep = Graph(Child("l1"));
            var l1 = Graph(Child("l2"));
            var l2 = Graph(Child("l3"));
            var l3 = Graph(Child("l4"));
            var l4 = Graph(Child("l5"));
            var l5 = Graph(PCGTransformPointsNode.Id);
            Func<string, PCGGraph> chainLoader = name =>
                name == "l1" ? l1 : name == "l2" ? l2 : name == "l3" ? l3 : name == "l4" ? l4 : name == "l5" ? l5 : null;

            check(PCGGraphEffects.GraphWritesScene(deep, chainLoader),
                "a chain deeper than the walk's ceiling is refused rather than guessed safe");
            check(!PCGGraphEffects.GraphWritesScene(deep, chainLoader, 8),
                "raising the ceiling to cover the chain allows it - the depth cap is a reach limit, not a verdict",
                $"default ceiling {PCGGraphEffects.DefaultSubgraphCheckDepth}");

            // A sub-graph node with no name set says nothing about its contents.
            var unnamed = Graph(new PCGNode { TypeId = PCGSubgraphNode.Id, Values = new object[0] });
            check(PCGGraphEffects.GraphWritesScene(unnamed, name => rocks),
                "a sub-graph node without a graph name is refused, not treated as pointing at nothing");
        }

        private static PCGGraph Graph(params object[] nodesOrTypes)
        {
            var nodes = new PCGNode[nodesOrTypes.Length];

            for (int i = 0; i < nodesOrTypes.Length; i++)
            {
                nodes[i] = nodesOrTypes[i] is PCGNode node
                    ? node
                    : new PCGNode { TypeId = (string)nodesOrTypes[i], Values = new object[0] };
            }

            return new PCGGraph { Name = "EffectFixture", Nodes = nodes };
        }

        private static PCGNode Child(string name) =>
            new PCGNode { TypeId = PCGSubgraphNode.Id, Values = new object[] { name } };

        private static PCGNodeBase[] BuildNodes(string[] typeIds)        {
            var nodes = new PCGNodeBase[typeIds.Length];

            for (int i = 0; i < typeIds.Length; i++)
            {
                var node = PCGNodeRegistry.CreateNode(
                    new PCGNode { TypeId = typeIds[i], Values = new object[0] }, i, out _);

                if (node == null)
                    return null;

                nodes[i] = node;
            }

            return nodes;
        }
    }
}

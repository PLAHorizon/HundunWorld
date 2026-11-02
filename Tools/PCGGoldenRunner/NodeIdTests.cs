// PCG Plugin - Executable tests for the stable node identities (docs/PCG_PLUGIN_DESIGN.md §6.1).
//
// The ids the canvas packs into a saved payload used to come from registration order, which made a graph written in
// one session resolve to nothing in the next whenever the registry was laid out differently. The property that
// replaces that is "the ids depend on the set of names and on nothing else", and it is exactly the property a test
// can assert on its own - no engine, no canvas.

using System;
using System.Collections.Generic;
using PCG;
using PCG.Execution;
using PCGEditor;

using CheckDelegate = PCGGoldenRunner.NodeBehaviourTests.CheckDelegate;

namespace PCGGoldenRunner
{
    internal static class NodeIdTests
    {
        internal static void Run(CheckDelegate check)
        {
            HashTests(check);
            AssignmentTests(check);
            RegistrySetTests(check);
        }

        private static void HashTests(CheckDelegate check)
        {
            check("the same name always hashes to the same id",
                PCGNodeStableIds.Hash("SplitGrammar") == PCGNodeStableIds.Hash("SplitGrammar"),
                PCGNodeStableIds.Hash("SplitGrammar").ToString());

            check("different names hash apart",
                PCGNodeStableIds.Hash("SplitGrammar") != PCGNodeStableIds.Hash("Grammar") &&
                PCGNodeStableIds.Hash("SpawnActor") != PCGNodeStableIds.Hash("SpawnDecal"));

            check("an id is never zero, which the engine reads as 'no id'",
                PCGNodeStableIds.Hash(string.Empty) != 0 && PCGNodeStableIds.Hash(null) != 0 &&
                PCGNodeStableIds.Hash("Geometry") != 0);

            check("ids stay inside what the canvas can pack",
                PCGNodeStableIds.Hash("RayCastProject") <= PCGNodeStableIds.MaxId &&
                PCGNodeStableIds.Pack(1, 1) == ((1u << 16) | 1u));
        }

        private static void AssignmentTests(CheckDelegate check)
        {
            var names = new List<string> { "Alpha", "Beta", "Gamma", "Delta", "Epsilon" };
            var first = PCGNodeStableIds.Assign(names);

            // The decisive property: the same set of names handed over in any order produces the same answer for each
            // name. This is what a payload written by an older session depends on.
            var shuffled = new List<string> { "Epsilon", "Gamma", "Alpha", "Delta", "Beta" };
            var second = PCGNodeStableIds.Assign(shuffled);

            bool same = first.Length == names.Count;

            for (int i = 0; same && i < names.Count; i++)
            {
                int index = shuffled.IndexOf(names[i]);
                same = first[i] == second[index];
            }

            check("the assignment does not depend on the order the names arrive in", same,
                string.Join(",", first));

            // Uniqueness over a set large enough that a naive hash would collide somewhere: the probe has to be doing
            // its job, or two node types would share an id and one of them would come back as the other.
            var many = new List<string>(512);

            for (int i = 0; i < 512; i++)
                many.Add($"Node_{i}_{i * 7919 % 1000}");

            var ids = PCGNodeStableIds.Assign(many);
            var seen = new HashSet<ushort>();

            foreach (var id in ids)
                seen.Add(id);

            check("five hundred names all get distinct ids", seen.Count == many.Count,
                $"{seen.Count} distinct for {many.Count} name(s)");

            // ...and the collision handling is itself order independent.
            var reversed = new List<string>(many);
            reversed.Reverse();
            var idsReversed = PCGNodeStableIds.Assign(reversed);

            bool stable = true;

            for (int i = 0; stable && i < many.Count; i++)
                stable = ids[i] == idsReversed[many.Count - 1 - i];

            check("the collision handling is order independent too", stable);

            check("an empty set assigns nothing rather than throwing",
                PCGNodeStableIds.Assign(Array.Empty<string>()).Length == 0);
        }

        /// <summary>
        /// The real registry: the ids a saved payload would carry, checked across two different orderings.
        /// </summary>
        private static void RegistrySetTests(CheckDelegate check)
        {
            PCGNodeRegistry.Clear();
            PCG.Runtime.PCGNodeRegistration.RegisterAll();

            var names = new List<string>();

            foreach (var info in PCGNodeRegistry.All)
            {
                if (info != null && !string.IsNullOrEmpty(info.TypeId))
                    names.Add(info.TypeId);
            }

            var ids = PCGNodeStableIds.Assign(names);
            var reversedNames = new List<string>(names);
            reversedNames.Reverse();
            var reversedIds = PCGNodeStableIds.Assign(reversedNames);

            bool stable = names.Count > 0;

            for (int i = 0; i < names.Count && stable; i++)
                stable = ids[i] == reversedIds[reversedNames.IndexOf(names[i])];

            check("every registered node keeps its id when the registry is laid out the other way round",
                stable, $"{names.Count} node(s)");

            // The packed pairs have to be unique as pairs, which is what the canvas looks a node up by.
            var packed = new HashSet<uint>();
            var groups = new List<string>();

            foreach (var info in PCGNodeRegistry.All)
            {
                if (info != null && !string.IsNullOrEmpty(info.Category) && !groups.Contains(info.Category))
                    groups.Add(info.Category);
            }

            var groupIds = PCGNodeStableIds.Assign(groups);
            bool pairsUnique = true;

            for (int i = 0; i < names.Count && pairsUnique; i++)
            {
                var info = PCGNodeRegistry.All;
                var category = string.Empty;

                foreach (var candidate in info)
                {
                    if (candidate != null && candidate.TypeId == names[i])
                    {
                        category = candidate.Category;
                        break;
                    }
                }

                int groupIndex = groups.IndexOf(category);

                if (groupIndex < 0)
                    continue;

                pairsUnique = packed.Add(PCGNodeStableIds.Pack(groupIds[groupIndex], ids[i]));
            }

            check("every node packs to a distinct (group, type) pair", pairsUnique,
                $"{packed.Count} pair(s) for {names.Count} node(s)");

            PCGNodeRegistry.Clear();
        }
    }
}

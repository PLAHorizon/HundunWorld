// PCG Plugin - Stable identities for the graph editor's node types
// Part of the FlaxEngine PCG plugin. See docs/PCG_PLUGIN_DESIGN.md §6.1.

using System;
using System.Collections.Generic;

namespace PCGEditor
{
    /// <summary>
    /// Turns the stable name of a node type or a category into the numeric id the Visject canvas identifies it by.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The canvas payload stores each node as a packed pair - group id and type id - and both used to be handed out
    /// in registration order. That made them only meaningful inside the session that wrote them: reordering the
    /// registry (which happens every time a node is added somewhere other than the end) moved every id after the
    /// insertion point, and a payload written by an older session then resolved to nothing. The symptom is not an
    /// error the designer can act on - it is a node quietly missing from a reopened graph, with the only trace in
    /// the engine log.
    /// </para>
    /// <para>
    /// Deriving the id from the name fixes that direction of failure: the same node set produces the same ids in any
    /// session and in any registration order, so a payload written today still resolves tomorrow. The name is the
    /// thing the graph file already treats as the stable contract (it stores type <i>names</i>, not numbers), so the
    /// canvas and the file now agree about what identity means.
    /// </para>
    /// <para>
    /// Collisions are resolved deterministically rather than by chance: the names are processed in sorted order and
    /// a colliding id is probed upwards, so the outcome is a function of the <i>set</i> of names and not of the order
    /// they were handed over in. With fifty entries in a fifteen-bit space a collision is unlikely but not
    /// negligible, and "unlikely" is not good enough for something a saved file depends on.
    /// </para>
    /// </remarks>
    internal static class PCGNodeStableIds
    {
        /// <summary>Highest id that can be assigned; ids stay positive and inside a <c>ushort</c>.</summary>
        public const ushort MaxId = 0x7FFF;

        /// <summary>
        /// The id a name maps to, before any collision is taken into account.
        /// </summary>
        /// <remarks>
        /// FNV-1a: a few lines, no dependencies, and stable across runs and machines - which is the only property
        /// that matters here, since the value is never compared against a hash computed anywhere else.
        /// </remarks>
        public static ushort Hash(string name)
        {
            if (string.IsNullOrEmpty(name))
                return 1;

            const uint offset = 2166136261;
            const uint prime = 16777619;

            uint hash = offset;

            for (int i = 0; i < name.Length; i++)
            {
                hash ^= name[i];
                hash *= prime;
            }

            ushort id = (ushort)(hash & MaxId);

            // Zero is reserved for "no id" in the engine's own tables, so a name that hashes to it moves to one.
            return id == 0 ? (ushort)1 : id;
        }

        /// <summary>
        /// Assigns an id to every name, resolving collisions so that all ids differ.
        /// </summary>
        /// <param name="names">The names, in the caller's order.</param>
        /// <returns>One id per name, in the same order as the input.</returns>
        public static ushort[] Assign(IReadOnlyList<string> names)
        {
            if (names == null)
                throw new ArgumentNullException(nameof(names));

            var ids = new ushort[names.Count];

            if (names.Count == 0)
                return ids;

            // Sorted so the result depends on the set rather than the sequence.
            var order = new int[names.Count];

            for (int i = 0; i < order.Length; i++)
                order[i] = i;

            Array.Sort(order, (a, b) => string.CompareOrdinal(names[a], names[b]));

            var used = new HashSet<ushort>();

            for (int i = 0; i < order.Length; i++)
            {
                int index = order[i];
                ushort id = Hash(names[index]);

                while (!used.Add(id))
                {
                    id = id >= MaxId ? (ushort)1 : (ushort)(id + 1);
                }

                ids[index] = id;
            }

            return ids;
        }

        /// <summary>
        /// The id the canvas packs a node under, which is what a saved payload contains.
        /// </summary>
        public static uint Pack(ushort groupId, ushort typeId) => ((uint)groupId << 16) | typeId;
    }
}

// PCG Plugin - Node type registry

using System;
using System.Collections.Generic;
using FlaxEngine;

namespace PCG.Execution
{
    /// <summary>
    /// Maps node type ids to factories, so graph data can be instantiated into executable nodes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The registry is static state that holds delegates into the plugin assembly. Flax destroys all script
    /// instances on a scripts reload, so <see cref="Clear"/> must be called on reload and the tables rebuilt,
    /// otherwise the registry would keep calling into types from the unloaded assembly.
    /// </para>
    /// <para>
    /// Registration is idempotent per type id: re-registering replaces the existing entry, which makes it
    /// safe to rebuild the table after a reload without duplicating.
    /// </para>
    /// </remarks>
    public static class PCGNodeRegistry
    {
        /// <summary>Factory that instantiates a node from its serialized form.</summary>
        public delegate PCGNodeBase NodeFactory(PCGNodeSerializationData node);

        /// <summary>
        /// The subset of a graph node a factory needs: its type id and parameter values.
        /// </summary>
        public readonly struct PCGNodeSerializationData
        {
            /// <summary>Type id of the node.</summary>
            public readonly string TypeId;

            /// <summary>Parameter values, positionally indexed.</summary>
            public readonly object[] Values;

            /// <summary>Index of the node within the graph, useful for stable node ids in logs.</summary>
            public readonly int NodeIndex;

            /// <summary>Creates the snapshot.</summary>
            public PCGNodeSerializationData(string typeId, object[] values, int nodeIndex)
            {
                TypeId = typeId;
                Values = values ?? Array.Empty<object>();
                NodeIndex = nodeIndex;
            }

            /// <summary>Reads a value by index, returning <paramref name="fallback"/> when absent.</summary>
            public T Get<T>(int index, T fallback)
            {
                if (Values == null || index < 0 || index >= Values.Length)
                    return fallback;

                var v = Values[index];
                if (v == null)
                    return fallback;

                try
                {
                    if (v is T typed)
                        return typed;

                    return (T)Convert.ChangeType(v, typeof(T));
                }
                catch (Exception)
                {
                    return fallback;
                }
            }
        }

        /// <summary>Describes a registered node type for editor and diagnostics use.</summary>
        public sealed class PCGNodeTypeInfo
        {
            /// <summary>Type id.</summary>
            public string TypeId;

            /// <summary>Display name.</summary>
            public string DisplayName;

            /// <summary>Category the node is grouped under in the editor.</summary>
            public string Category;

            /// <summary>Factory creating instances.</summary>
            public NodeFactory Factory;

            /// <summary>Human-readable description.</summary>
            public string Description;

            /// <inheritdoc />
            public override string ToString() => $"{DisplayName} ({TypeId}) [{Category}]";
        }

        private static readonly Dictionary<string, PCGNodeTypeInfo> Types =
            new Dictionary<string, PCGNodeTypeInfo>(StringComparer.Ordinal);

        private static readonly List<PCGNodeTypeInfo> Ordered = new List<PCGNodeTypeInfo>();

        /// <summary>Raised whenever the registry contents change, so the editor can refresh its menus.</summary>
        public static event Action Changed;

        /// <summary>Number of registered node types.</summary>
        public static int Count => Ordered.Count;

        /// <summary>Enumerates registered types in registration order.</summary>
        public static IReadOnlyList<PCGNodeTypeInfo> All => Ordered;

        /// <summary>
        /// Registers a node type. Re-registering the same id replaces the previous entry.
        /// </summary>
        /// <param name="typeId">Stable type id, matching <c>PCGNodeBase.TypeId</c>.</param>
        /// <param name="category">Editor grouping.</param>
        /// <param name="factory">Factory creating instances from serialized data.</param>
        /// <param name="displayName">Optional display name; defaults to the type id.</param>
        /// <param name="description">Optional description.</param>
        public static void Register(string typeId, string category, NodeFactory factory,
            string displayName = null, string description = null)
        {
            if (string.IsNullOrEmpty(typeId))
                throw new ArgumentException("Node type id cannot be null or empty.", nameof(typeId));

            if (factory == null)
                throw new ArgumentNullException(nameof(factory));

            var info = new PCGNodeTypeInfo
            {
                TypeId = typeId,
                DisplayName = string.IsNullOrEmpty(displayName) ? typeId : displayName,
                Category = string.IsNullOrEmpty(category) ? "Misc" : category,
                Factory = factory,
                Description = description,
            };

            if (Types.TryGetValue(typeId, out var existing))
            {
                // Replace in place to preserve registration order across reloads.
                int index = Ordered.IndexOf(existing);
                if (index >= 0)
                    Ordered[index] = info;

                Types[typeId] = info;
            }
            else
            {
                Types.Add(typeId, info);
                Ordered.Add(info);
            }

            Changed?.Invoke();
        }

        /// <summary>Returns true when a type id is registered.</summary>
        public static bool Contains(string typeId)
        {
            return !string.IsNullOrEmpty(typeId) && Types.ContainsKey(typeId);
        }

        /// <summary>Looks up a type by id.</summary>
        public static bool TryGet(string typeId, out PCGNodeTypeInfo info)
        {
            info = null;
            if (string.IsNullOrEmpty(typeId))
                return false;

            return Types.TryGetValue(typeId, out info);
        }

        /// <summary>
        /// Creates a node instance from serialized data.
        /// </summary>
        /// <param name="node">Serialized node.</param>
        /// <param name="nodeIndex">Index of the node within the graph.</param>
        /// <param name="error">Set to a message when instantiation fails.</param>
        public static PCGNodeBase CreateNode(Graph.PCGNode node, int nodeIndex, out string error)
        {
            error = null;

            if (node == null)
            {
                error = "Node is null.";
                return null;
            }

            if (!Types.TryGetValue(node.TypeId ?? string.Empty, out var info))
            {
                error = $"Unknown node type '{node.TypeId}'. The graph may reference a node from a plugin that is not loaded.";
                return null;
            }

            try
            {
                var snapshot = new PCGNodeSerializationData(node.TypeId, node.Values, nodeIndex);
                var instance = info.Factory(snapshot);

                if (instance == null)
                {
                    error = $"Factory for node type '{node.TypeId}' returned null.";
                    return null;
                }

                return instance;
            }
            catch (Exception ex)
            {
                error = $"Failed to construct node type '{node.TypeId}': {ex.Message}";
                return null;
            }
        }

        /// <summary>
        /// Clears all registrations. Must be called on a scripts reload, since the stored delegates point
        /// into the assembly being unloaded.
        /// </summary>
        public static void Clear()
        {
            Types.Clear();
            Ordered.Clear();
            Changed?.Invoke();
        }

        /// <summary>Categories present in the registry, for building editor menus.</summary>
        public static string[] GetCategories()
        {
            var set = new List<string>();
            for (int i = 0; i < Ordered.Count; i++)
            {
                if (!set.Contains(Ordered[i].Category))
                    set.Add(Ordered[i].Category);
            }

            return set.ToArray();
        }

        /// <summary>Logs the registered node types. Useful when diagnosing why a graph fails to compile.</summary>
        public static void LogRegisteredTypes()
        {
            Debug.Log($"[PCG] {Ordered.Count} node type(s) registered:");
            for (int i = 0; i < Ordered.Count; i++)
                Debug.Log($"  [PCG]   {Ordered[i]}");
        }
    }
}

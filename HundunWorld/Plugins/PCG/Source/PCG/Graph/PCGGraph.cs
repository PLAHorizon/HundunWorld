// PCG Plugin - Graph asset data model

using System;
using System.Collections.Generic;
using FlaxEngine;

namespace PCG.Graph
{
    /// <summary>Kind of a graph-level exposed parameter.</summary>
    public enum PCGParameterType
    {
        /// <summary>Floating point value.</summary>
        Float = 0,
        /// <summary>Integer value.</summary>
        Int = 1,
        /// <summary>Boolean value.</summary>
        Bool = 2,
        /// <summary>Vector value.</summary>
        Vector = 3,
        /// <summary>Colour value.</summary>
        Color = 4,
        /// <summary>Model asset reference. Stored in <see cref="PCGParameterValue.AssetId"/>.</summary>
        Asset = 5,
        /// <summary>Free-form string.</summary>
        String = 6,
    }

    /// <summary>Serializable parameter value, covering every value type a node may expose.</summary>
    [Serializable]
    public struct PCGParameterValue
    {
        /// <summary>Numeric payload. Used by Float, Int (round-tripped) and Bool (0 or 1).</summary>
        public float Number;

        /// <summary>Vector payload.</summary>
        public Vector3 Vector;

        /// <summary>Colour payload.</summary>
        public Color Color;

        /// <summary>Asset payload, as an asset id.</summary>
        public Guid AssetId;

        /// <summary>String payload.</summary>
        public string Text;

        /// <summary>Creates a float value.</summary>
        public static PCGParameterValue FromFloat(float v) => new PCGParameterValue { Number = v };

        /// <summary>Creates an int value.</summary>
        public static PCGParameterValue FromInt(int v) => new PCGParameterValue { Number = v };

        /// <summary>Creates a bool value.</summary>
        public static PCGParameterValue FromBool(bool v) => new PCGParameterValue { Number = v ? 1.0f : 0.0f };

        /// <summary>Creates a Vector3 value.</summary>
        public static PCGParameterValue FromVector(Vector3 v) => new PCGParameterValue { Vector = v };

        /// <summary>Creates a colour value.</summary>
        public static PCGParameterValue FromColor(Color v) => new PCGParameterValue { Color = v };

        /// <summary>Creates an asset reference value.</summary>
        public static PCGParameterValue FromAsset(Guid id) => new PCGParameterValue { AssetId = id };

        /// <summary>Creates a string value.</summary>
        public static PCGParameterValue FromString(string v) => new PCGParameterValue { Text = v };

        /// <summary>
        /// Reads this value back as the shape <paramref name="type"/> describes.
        /// </summary>
        public object ToObject(PCGParameterType type)
        {
            switch (type)
            {
            case PCGParameterType.Float: return Number;
            case PCGParameterType.Int: return (int)Number;
            case PCGParameterType.Bool: return Number != 0.0f;
            case PCGParameterType.Vector: return Vector;
            case PCGParameterType.Color: return Color;
            case PCGParameterType.Asset: return AssetId;
            case PCGParameterType.String: return Text;
            default: return Number;
            }
        }

        /// <summary>Writes <paramref name="value"/> into this struct according to <paramref name="type"/>.</summary>
        /// <returns>True when the value was compatible.</returns>
        public bool TrySet(PCGParameterType type, object value)
        {
            if (value == null)
                return false;

            try
            {
                switch (type)
                {
                case PCGParameterType.Float:
                    Number = Convert.ToSingle(value);
                    return true;
                case PCGParameterType.Int:
                    Number = Convert.ToInt32(value);
                    return true;
                case PCGParameterType.Bool:
                    Number = Convert.ToBoolean(value) ? 1.0f : 0.0f;
                    return true;
                case PCGParameterType.Vector:
                    Vector = (Vector3)value;
                    return true;
                case PCGParameterType.Color:
                    Color = (Color)value;
                    return true;
                case PCGParameterType.Asset:
                    AssetId = (Guid)value;
                    return true;
                case PCGParameterType.String:
                    Text = (string)value;
                    return true;
                default:
                    return false;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Declares a parameter a node exposes to the graph, so a <c>PCGVolume</c> can override it per instance
    /// without the graph being recompiled.
    /// </summary>
    /// <remarks>
    /// Overrides are deliberately kept out of the structural hash. Otherwise two volumes using the same
    /// graph with different values would each force a full recompile, and the reuse mechanism would lose
    /// all of its benefit.
    /// </remarks>
    [Serializable]
    public sealed class PCGParameterDefinition
    {
        /// <summary>Stable parameter name, unique within the graph.</summary>
        public string Name;

        /// <summary>Name shown in the editor.</summary>
        public string DisplayName;

        /// <summary>Value type.</summary>
        public PCGParameterType Type;

        /// <summary>Default value, used when no override is supplied.</summary>
        public PCGParameterValue DefaultValue;

        /// <summary>Index of the node this parameter belongs to.</summary>
        public int NodeIndex;

        /// <summary>Index into that node's <see cref="PCGNode.Values"/> array.</summary>
        public int ValueIndex;

        /// <inheritdoc />
        public override string ToString() => $"{Name} ({Type}) -> node {NodeIndex}[{ValueIndex}]";
    }

    /// <summary>One node in a graph, serialized.</summary>
    [Serializable]
    public sealed class PCGNode
    {
        /// <summary>Node type identifier, matching a <c>PCGNodeBase.TypeId</c>.</summary>
        public string TypeId;

        /// <summary>Position in the graph editor.</summary>
        public Vector2 EditorPosition;

        /// <summary>
        /// Node parameter values, indexed by the node's own parameter order.
        /// </summary>
        /// <remarks>
        /// Kept as a plain <c>object[]</c> rather than a name-keyed dictionary because the planned graph
        /// editor is built on <c>VisjectSurface</c>, whose <c>SurfaceNode.Values</c> is a positional array.
        /// Matching that shape now avoids a data migration when the editor lands.
        /// </remarks>
        public object[] Values = Array.Empty<object>();

        /// <summary>Optional user comment.</summary>
        public string Comment;

        /// <summary>True when the node is muted and should pass through or produce nothing.</summary>
        public bool Disabled;

        /// <inheritdoc />
        public override string ToString() => $"{TypeId} @ {EditorPosition}";
    }

    /// <summary>One edge in a graph: an output pin index on one node feeding an input pin index on another.</summary>
    [Serializable]
    public sealed class PCGEdge
    {
        /// <summary>Source node index.</summary>
        public int OutputNodeIndex;

        /// <summary>Source pin index, indexing <c>PCGNodeBase.OutputPins</c>.</summary>
        public int OutputPinIndex;

        /// <summary>Target node index.</summary>
        public int InputNodeIndex;

        /// <summary>Target pin index, indexing <c>PCGNodeBase.InputPins</c>.</summary>
        public int InputPinIndex;

        /// <inheritdoc />
        public override string ToString() =>
            $"{OutputNodeIndex}[{OutputPinIndex}] -> {InputNodeIndex}[{InputPinIndex}]";
    }

    /// <summary>Graph-wide settings that affect execution.</summary>
    [Serializable]
    public sealed class PCGGraphSettings
    {
        /// <summary>Seed used when a volume does not supply one.</summary>
        public int DefaultSeed;

        /// <summary>Draw debug output while previewing in the editor.</summary>
        public bool EnableDebugDraw = true;

        /// <summary>Maximum points a single graph run may produce before it aborts.</summary>
        /// <remarks>
        /// A runaway graph (for example a sampler misconfigured to a huge count) would otherwise exhaust
        /// memory before anyone can react. The executor aborts on this budget with a clear message.
        /// </remarks>
        public int MaxPointBudget = 2000000;

        /// <summary>Maximum number of nodes allowed in an expanded graph, guarding recursive subgraphs.</summary>
        public int MaxExpandedNodeCount = 4096;
    }

    /// <summary>
    /// A PCG graph: nodes, edges, exposed parameters and settings.
    /// </summary>
    /// <remarks>
    /// This type is plain serializable state. Persisting it as a Flax asset is a separate concern; see the
    /// asset integration notes in docs/PCG_PLUGIN_DESIGN.md §3.3 note 4.
    /// </remarks>
    [Serializable]
    public sealed class PCGGraph
    {
        /// <summary>Graph name, for logs and generated-object naming.</summary>
        public string Name = "PCG Graph";

        /// <summary>Nodes in the graph. Node indices are referenced by edges and parameters.</summary>
        public PCGNode[] Nodes = Array.Empty<PCGNode>();

        /// <summary>Edges in the graph.</summary>
        public PCGEdge[] Edges = Array.Empty<PCGEdge>();

        /// <summary>Parameters exposed to volumes.</summary>
        public PCGParameterDefinition[] ExposedParameters = Array.Empty<PCGParameterDefinition>();

        /// <summary>Execution settings.</summary>
        public PCGGraphSettings Settings = new PCGGraphSettings();

        /// <summary>
        /// Opaque payload reserved for the graph editor's surface serialization.
        /// </summary>
        /// <remarks>
        /// Present from the start so the editor can persist its own layout state later without a file
        /// format change.
        /// </remarks>
        public string EditorData;

        /// <summary>Creates an empty graph.</summary>
        public PCGGraph()
        {
        }

        /// <summary>Creates a graph over the supplied topology, normalising null collections.</summary>
        public PCGGraph(PCGNode[] nodes, PCGEdge[] edges)
        {
            Nodes = nodes ?? Array.Empty<PCGNode>();
            Edges = edges ?? Array.Empty<PCGEdge>();
            Settings = new PCGGraphSettings();
        }

        /// <summary>Gets the number of nodes.</summary>
        public int NodeCount => Nodes?.Length ?? 0;

        /// <summary>Gets the number of edges.</summary>
        public int EdgeCount => Edges?.Length ?? 0;
    }
}
